namespace Hcoona.CelesphoniaModifier.Atlas;

internal sealed record AtlasValidatedSaveSnapshotEntry(
    string SourceFileName,
    string RelativePath,
    string AbsolutePath,
    long Length,
    string Sha256,
    int Order);

internal sealed record AtlasValidatedSaveSnapshot(
    string ReceiptPath,
    string FinalRoot,
    IReadOnlyList<AtlasValidatedSaveSnapshotEntry> Entries);

internal static class AtlasFinalizedSaveSnapshot
{
    public static async ValueTask<AtlasValidatedSaveSnapshot> OpenAsync(
        string repositoryRoot,
        string receiptPath,
        AtlasIoSeams io,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(repositoryRoot);
        ArgumentException.ThrowIfNullOrWhiteSpace(receiptPath);
        ArgumentNullException.ThrowIfNull(io);
        cancellationToken.ThrowIfCancellationRequested();

        try
        {
            string normalizedRepositoryRoot =
                AtlasSaveSnapshotContracts.NormalizeAbsolutePath(repositoryRoot);
            string normalizedReceiptPath =
                AtlasSaveSnapshotContracts.NormalizeAbsolutePath(receiptPath);
            string finalRoot =
                Path.GetDirectoryName(normalizedReceiptPath)
                ?? throw new AtlasSafetyException("The snapshot receipt path is invalid.");
            string workspaceRoot =
                Directory.GetParent(finalRoot)?.FullName
                ?? throw new AtlasSafetyException("The snapshot receipt path is invalid.");
            string privateParent =
                Directory.GetParent(workspaceRoot)?.FullName
                ?? throw new AtlasSafetyException("The snapshot receipt path is invalid.");
            string runId = Path.GetFileName(workspaceRoot);
            AtlasSaveSnapshotContracts.ValidateRunId(runId);

            string applicationRoot = Path.Combine(
                normalizedRepositoryRoot,
                "src",
                "private",
                "app",
                "celesphonia-modifier");
            string expectedPrivateParent = Path.GetFullPath(
                Path.Combine(applicationRoot, ".private", "atlas-save-snapshot"));
            string expectedFinalRoot = Path.GetFullPath(
                Path.Combine(expectedPrivateParent, runId, "save-snapshot"));
            string expectedReceiptPath = Path.Combine(
                expectedFinalRoot,
                AtlasSaveSnapshotContracts.ReceiptFileName);
            if (!AtlasSaveSnapshotContracts.PathEquals(privateParent, expectedPrivateParent)
                || !AtlasSaveSnapshotContracts.PathEquals(finalRoot, expectedFinalRoot)
                || !AtlasSaveSnapshotContracts.PathEquals(
                    normalizedReceiptPath,
                    expectedReceiptPath)
                || !StringComparer.Ordinal.Equals(
                    Path.GetFileName(finalRoot),
                    "save-snapshot")
                || !StringComparer.Ordinal.Equals(
                    Path.GetFileName(normalizedReceiptPath),
                    AtlasSaveSnapshotContracts.ReceiptFileName))
            {
                throw new AtlasSafetyException("The finalized snapshot layout is invalid.");
            }

            string[] requiredDirectories =
            [
                normalizedRepositoryRoot,
                Path.Combine(normalizedRepositoryRoot, "src"),
                Path.Combine(normalizedRepositoryRoot, "src", "private"),
                Path.Combine(normalizedRepositoryRoot, "src", "private", "app"),
                applicationRoot,
                Path.Combine(applicationRoot, ".private"),
                expectedPrivateParent,
                workspaceRoot,
                expectedFinalRoot,
            ];
            foreach (string directory in requiredDirectories)
            {
                ValidateOrdinaryDirectory(directory, io);
            }

            AtlasValidatedSaveSnapshot? result = await TryOpenCandidateAsync(
                    expectedFinalRoot,
                    expectedReceiptPath,
                    runId,
                    expectedFinalRoot,
                    io,
                    cancellationToken)
                .ConfigureAwait(false);
            return result
                ?? throw new AtlasSafetyException("The finalized snapshot is invalid.");
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (AtlasSafetyException)
        {
            throw;
        }
        catch (Exception exception) when (
            exception is ArgumentException
            or IOException
            or UnauthorizedAccessException
            or InvalidOperationException
            or NotSupportedException)
        {
            throw new AtlasSafetyException(
                "The finalized snapshot is invalid.",
                AtlasDiscoveryFailureStage.Unspecified,
                exception);
        }
    }

    internal static async ValueTask<AtlasValidatedSaveSnapshot?> TryOpenCandidateAsync(
        string candidateRoot,
        string receiptPath,
        string expectedRunId,
        string expectedFinalRoot,
        AtlasIoSeams io,
        CancellationToken cancellationToken)
    {
        try
        {
            ValidateOrdinaryDirectory(candidateRoot, io);
            Dictionary<string, string> actualChildren =
                new(StringComparer.OrdinalIgnoreCase);
            foreach (string child in io.EnumerateFileSystemEntries(
                         candidateRoot,
                         SearchOption.TopDirectoryOnly))
            {
                cancellationToken.ThrowIfCancellationRequested();
                string leaf = Path.GetFileName(child);
                bool isReceipt = StringComparer.OrdinalIgnoreCase.Equals(
                    leaf,
                    AtlasSaveSnapshotContracts.ReceiptFileName);
                if (isReceipt)
                {
                    if (!StringComparer.Ordinal.Equals(
                            leaf,
                            AtlasSaveSnapshotContracts.ReceiptFileName))
                    {
                        return null;
                    }
                }
                else if (!AtlasSaveSnapshot.TryGetCanonicalName(
                             leaf,
                             out string canonicalLeaf,
                             out _)
                         || !StringComparer.Ordinal.Equals(leaf, canonicalLeaf))
                {
                    return null;
                }

                string absolute = Path.GetFullPath(child);
                if (!AtlasSaveSnapshotContracts.ContainsPath(candidateRoot, absolute)
                    || AtlasSaveSnapshotContracts.PathEquals(candidateRoot, absolute)
                    || actualChildren.ContainsKey(leaf)
                    || !IsOrdinaryFile(absolute, io))
                {
                    return null;
                }

                actualChildren.Add(leaf, absolute);
            }

            if (!actualChildren.TryGetValue(
                    AtlasSaveSnapshotContracts.ReceiptFileName,
                    out string? actualReceiptPath)
                || !AtlasSaveSnapshotContracts.PathEquals(receiptPath, actualReceiptPath))
            {
                return null;
            }

            AtlasSaveSnapshotReceipt receipt =
                await AtlasSaveSnapshotContracts.ReadReceiptAsync(
                        actualReceiptPath,
                        io,
                        cancellationToken)
                    .ConfigureAwait(false);
            if (!StringComparer.Ordinal.Equals(receipt.RunId, expectedRunId)
                || !AtlasSaveSnapshotContracts.PathEquals(
                    receipt.FinalSnapshotRoot,
                    expectedFinalRoot)
                || actualChildren.Count != receipt.Entries.Length + 1)
            {
                return null;
            }

            List<AtlasValidatedSaveSnapshotEntry> entries = [];
            int priorOrder = -1;
            HashSet<string> sources = new(StringComparer.OrdinalIgnoreCase);
            foreach (AtlasSaveSnapshotReceiptEntry entry in receipt.Entries)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (!AtlasSaveSnapshot.TryGetCanonicalName(
                        entry.SourceFileName,
                        out string canonical,
                        out int order)
                    || order <= priorOrder
                    || !StringComparer.Ordinal.Equals(
                        entry.DestinationRelativePath,
                        canonical)
                    || !sources.Add(entry.SourceFileName)
                    || !actualChildren.TryGetValue(canonical, out string? destination))
                {
                    return null;
                }

                (long length, string sha256) = await AtlasSaveSnapshot.HashOrdinaryFileAsync(
                        destination,
                        io,
                        cancellationToken)
                    .ConfigureAwait(false);
                if (length != entry.Length
                    || !StringComparer.Ordinal.Equals(sha256, entry.Sha256))
                {
                    return null;
                }

                entries.Add(
                    new AtlasValidatedSaveSnapshotEntry(
                        entry.SourceFileName,
                        canonical,
                        destination,
                        length,
                        sha256,
                        order));
                priorOrder = order;
            }

            return new AtlasValidatedSaveSnapshot(
                actualReceiptPath,
                expectedFinalRoot,
                entries.AsReadOnly());
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception exception) when (
            exception is AtlasSafetyException
            or ArgumentException
            or IOException
            or UnauthorizedAccessException
            or InvalidOperationException
            or NotSupportedException)
        {
            return null;
        }
    }

    private static void ValidateOrdinaryDirectory(string path, AtlasIoSeams io)
    {
        if (!io.DirectoryExists(path))
        {
            throw new AtlasSafetyException("A finalized snapshot directory is missing.");
        }

        FileAttributes attributes = io.GetAttributes(path);
        if ((attributes & FileAttributes.Directory) == 0
            || (attributes & (FileAttributes.ReparsePoint | FileAttributes.Device)) != 0)
        {
            throw new AtlasSafetyException("A finalized snapshot directory is invalid.");
        }
    }

    private static bool IsOrdinaryFile(string path, AtlasIoSeams io)
    {
        FileAttributes attributes = io.GetAttributes(path);
        return io.FileExists(path)
            && (attributes
                & (FileAttributes.Directory
                    | FileAttributes.ReparsePoint
                    | FileAttributes.Device)) == 0;
    }
}
