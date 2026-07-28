using System.Buffers;
using System.Security.Cryptography;

namespace Hcoona.CelesphoniaModifier.Atlas;

public static class AtlasDefinitionIntake
{
    public static ValueTask RunAsync(
        string requestPath,
        CancellationToken cancellationToken = default) =>
        RunAsync(requestPath, AtlasIoSeams.Default, cancellationToken);

    internal static async ValueTask RunAsync(
        string requestPath,
        AtlasIoSeams io,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(requestPath);
        ArgumentNullException.ThrowIfNull(io);

        AtlasDefinitionIntakeRequest request =
            await AtlasDefinitionIntakeContracts.ReadRequestAsync(
                    requestPath,
                    io,
                    cancellationToken)
                .ConfigureAwait(false);
        AtlasDefinitionIntakeLayout layout = AtlasDefinitionIntakeContracts.CreateLayout(request);
        ValidateLayout(request, layout, io);
        HistoricalDefinitionAuthority authority =
            await HistoricalAtlasDefinitionIngress.ReadAsync(
                    request,
                    layout,
                    io,
                    cancellationToken)
                .ConfigureAwait(false);
        DefinitionCopyPlan plan = CreateCopyPlan(authority);

        bool incompleteExists = PathExists(layout.IncompleteRoot, io);
        bool finalExists = PathExists(layout.FinalRoot, io);
        if (incompleteExists && finalExists)
        {
            ValidatePresentOutputRoot(layout.IncompleteRoot, io);
            ValidatePresentOutputRoot(layout.FinalRoot, io);
            throw new AtlasSafetyException(
                "Both incomplete and final definition snapshots are present.");
        }

        if (finalExists)
        {
            ValidatePresentOutputRoot(layout.FinalRoot, io);
            if (!await IsValidCandidateAsync(
                    layout.FinalRoot,
                    layout.FinalReceiptPath,
                    request,
                    authority,
                    plan,
                    io,
                    cancellationToken).ConfigureAwait(false))
            {
                throw new AtlasSafetyException("The final definition snapshot is invalid.");
            }

            return;
        }

        if (incompleteExists)
        {
            ValidatePresentOutputRoot(layout.IncompleteRoot, io);
            if (await IsValidCandidateAsync(
                    layout.IncompleteRoot,
                    layout.IncompleteReceiptPath,
                    request,
                    authority,
                    plan,
                    io,
                    cancellationToken).ConfigureAwait(false))
            {
                io.MoveDirectory(layout.IncompleteRoot, layout.FinalRoot);
                return;
            }

            io.DeleteDirectory(layout.IncompleteRoot, true);
        }

        DefinitionTraversalSnapshot before = TraverseAndReconcile(
            request.DefinitionRoot,
            authority,
            io);
        io.CreateDirectory(layout.IncompleteRoot);
        ValidateExistingDirectory(
            layout.WorkspaceRoot,
            io,
            "The definition intake workspace is invalid.");
        ValidatePresentOutputRoot(layout.IncompleteRoot, io);
        string definitionsRoot = Path.Combine(layout.IncompleteRoot, "definitions");
        io.CreateDirectory(definitionsRoot);

        List<AtlasDefinitionCopyReceiptEntry> receiptEntries = [];
        foreach (DefinitionCopyPlanEntry entry in plan.Entries)
        {
            cancellationToken.ThrowIfCancellationRequested();
            DefinitionObservedEntry observed = before.EntriesByAlias[entry.SourceAlias];
            string destinationPath = GetContainedDestinationPath(
                layout.IncompleteRoot,
                entry.DestinationRelativePath);
            AtlasDefinitionCopyReceiptEntry receiptEntry = await CopyOneAsync(
                    observed,
                    entry,
                    destinationPath,
                    io,
                    cancellationToken)
                .ConfigureAwait(false);
            receiptEntries.Add(receiptEntry);
        }

        DefinitionTraversalSnapshot after = TraverseAndReconcile(
            request.DefinitionRoot,
            authority,
            io);
        EnsureSnapshotsEquivalent(before, after);

        AtlasDefinitionCopyReceipt receipt = new()
        {
            SchemaVersion = AtlasDefinitionIntakeContracts.ReceiptSchemaVersion,
            HistoricalAuthoritySha256 = authority.Sha256,
            HistoricalAuthorityRevision = authority.Revision,
            ApplicationId = authority.ApplicationId,
            BuildId = authority.BuildId,
            RunId = request.RunId,
            DefinitionRoot =
                AtlasDefinitionIntakeContracts.NormalizeAbsolutePath(request.DefinitionRoot),
            FinalCopyRoot = layout.FinalRoot,
            Entries = [.. receiptEntries],
        };
        byte[] receiptBytes = AtlasDefinitionIntakeContracts.SerializeReceipt(receipt);
        await WriteNewFileAsync(
                layout.IncompleteReceiptPath,
                receiptBytes,
                io,
                cancellationToken)
            .ConfigureAwait(false);
        if (!await IsValidCandidateAsync(
                layout.IncompleteRoot,
                layout.IncompleteReceiptPath,
                request,
                authority,
                plan,
                io,
                cancellationToken).ConfigureAwait(false))
        {
            throw new AtlasSafetyException("The completed definition snapshot is invalid.");
        }

        io.MoveDirectory(layout.IncompleteRoot, layout.FinalRoot);
    }

    private static void ValidateLayout(
        AtlasDefinitionIntakeRequest request,
        AtlasDefinitionIntakeLayout layout,
        AtlasIoSeams io)
    {
        string expectedPrivateParent = Path.GetFullPath(
            Path.Combine(
                layout.RepositoryRoot,
                "src",
                "private",
                "app",
                "celesphonia-modifier",
                ".private",
                "atlas-definition-intake"));
        if (!AtlasDefinitionIntakeContracts.PathEquals(
                expectedPrivateParent,
                layout.PrivateParent)
            || !AtlasDefinitionIntakeContracts.ContainsPath(
                layout.PrivateParent,
                layout.WorkspaceRoot)
            || !StringComparer.Ordinal.Equals(
                Path.GetFileName(layout.IncompleteRoot),
                "definition-snapshot.incomplete")
            || !StringComparer.Ordinal.Equals(
                Path.GetFileName(layout.FinalRoot),
                "definition-snapshot")
            || !StringComparer.Ordinal.Equals(
                Path.GetFileName(layout.IncompleteReceiptPath),
                "definition-copy-receipt.json")
            || !StringComparer.Ordinal.Equals(
                Path.GetFileName(layout.FinalReceiptPath),
                "definition-copy-receipt.json")
            || !AtlasDefinitionIntakeContracts.ContainsPath(
                layout.IncompleteRoot,
                layout.IncompleteReceiptPath)
            || !AtlasDefinitionIntakeContracts.ContainsPath(
                layout.FinalRoot,
                layout.FinalReceiptPath))
        {
            throw new AtlasSafetyException("The definition intake workspace is invalid.");
        }

        ValidateExistingDirectory(
            AtlasDefinitionIntakeContracts.NormalizeAbsolutePath(request.DefinitionRoot),
            io,
            "The definition root is invalid.");
        if (PathExists(layout.WorkspaceRoot, io))
        {
            ValidateExistingDirectory(
                layout.WorkspaceRoot,
                io,
                "The definition intake workspace is invalid.");
        }
        if (AtlasDefinitionIntakeContracts.ContainsPath(
                request.DefinitionRoot,
                layout.WorkspaceRoot)
            || AtlasDefinitionIntakeContracts.ContainsPath(
                layout.WorkspaceRoot,
                request.DefinitionRoot))
        {
            throw new AtlasSafetyException(
                "The definition root and intake workspace must not overlap.");
        }

        if (PathExists(layout.IncompleteRoot, io))
        {
            ValidatePresentOutputRoot(layout.IncompleteRoot, io);
        }

        if (PathExists(layout.FinalRoot, io))
        {
            ValidatePresentOutputRoot(layout.FinalRoot, io);
        }
    }

    private static DefinitionCopyPlan CreateCopyPlan(HistoricalDefinitionAuthority authority)
    {
        List<DefinitionCopyPlanEntry> entries = [];
        HashSet<string> destinations = new(StringComparer.OrdinalIgnoreCase);
        foreach (HistoricalDefinitionEntry source in authority.Entries
                     .Where(static entry =>
                         StringComparer.Ordinal.Equals(
                             entry.Decision,
                             AtlasIntakeContracts.IncludeDefinitionDecision))
                     .OrderBy(static entry => entry.SourceAlias, StringComparer.Ordinal))
        {
            string extension = Path.GetExtension(source.RelativePath);
            if (!StringComparer.OrdinalIgnoreCase.Equals(extension, ".js")
                && !StringComparer.OrdinalIgnoreCase.Equals(extension, ".json"))
            {
                throw new AtlasSafetyException(
                    "An included historical definition has an unsupported extension.");
            }

            string destination =
                $"definitions/{source.SourceAlias}{extension.ToLowerInvariant()}";
            destination = AtlasDefinitionIntakeContracts.NormalizeRelativePath(destination);
            if (!destinations.Add(destination))
            {
                throw new AtlasSafetyException(
                    "Historical definition destinations collide.");
            }

            entries.Add(
                new DefinitionCopyPlanEntry(
                    source.SourceAlias,
                    source.RelativePath,
                    destination));
        }

        return new DefinitionCopyPlan(entries.AsReadOnly());
    }

    private static DefinitionTraversalSnapshot TraverseAndReconcile(
        string definitionRoot,
        HistoricalDefinitionAuthority authority,
        AtlasIoSeams io)
    {
        string normalizedRoot =
            AtlasDefinitionIntakeContracts.NormalizeAbsolutePath(definitionRoot);
        Dictionary<string, HistoricalDefinitionEntry> historicalByPath =
            authority.Entries.ToDictionary(
                static entry => entry.RelativePath,
                StringComparer.OrdinalIgnoreCase);
        Dictionary<string, DefinitionObservedEntry> observedByPath =
            new(StringComparer.OrdinalIgnoreCase);
        HashSet<string> seenEntrySpellings = new(StringComparer.Ordinal);
        Dictionary<string, string> seenEntryCasing = new(StringComparer.OrdinalIgnoreCase);

        TraverseDirectory(normalizedRoot);

        if (observedByPath.Count != historicalByPath.Count
            || historicalByPath.Keys.Any(path => !observedByPath.ContainsKey(path)))
        {
            throw new AtlasSafetyException(
                "The live definition set does not match historical authority.");
        }

        Dictionary<string, DefinitionObservedEntry> entriesByAlias =
            observedByPath.Values.ToDictionary(
                static entry => entry.SourceAlias,
                StringComparer.Ordinal);
        return new DefinitionTraversalSnapshot(
            observedByPath.Values
                .OrderBy(static entry => entry.RelativePath, StringComparer.OrdinalIgnoreCase)
                .ThenBy(static entry => entry.RelativePath, StringComparer.Ordinal)
                .ToArray(),
            entriesByAlias);

        void TraverseDirectory(string directoryPath)
        {
            IEnumerable<string> entries = io.EnumerateFileSystemEntries(
                directoryPath,
                SearchOption.TopDirectoryOnly);
            foreach (string candidate in entries
                         .OrderBy(
                             static path => Path.GetFileName(path),
                             StringComparer.OrdinalIgnoreCase)
                         .ThenBy(static path => Path.GetFileName(path), StringComparer.Ordinal))
            {
                string absolutePath = Path.GetFullPath(candidate);
                if (!AtlasDefinitionIntakeContracts.ContainsPath(normalizedRoot, absolutePath)
                    || AtlasDefinitionIntakeContracts.PathEquals(normalizedRoot, absolutePath))
                {
                    throw new AtlasSafetyException("A definition entry escapes its root.");
                }

                string relativePath = AtlasDefinitionIntakeContracts.NormalizeRelativePath(
                    Path.GetRelativePath(normalizedRoot, absolutePath));
                if (IsExcludedRootRelativePath(relativePath))
                {
                    continue;
                }

                if (!seenEntrySpellings.Add(relativePath)
                    || (seenEntryCasing.TryGetValue(relativePath, out string? priorSpelling)
                        && !StringComparer.Ordinal.Equals(priorSpelling, relativePath)))
                {
                    throw new AtlasSafetyException(
                        "Duplicate or case-colliding definition entries are invalid.");
                }

                seenEntryCasing[relativePath] = relativePath;
                FileAttributes attributes = io.GetAttributes(absolutePath);
                if ((attributes & FileAttributes.ReparsePoint) != 0)
                {
                    throw new AtlasSafetyException("A definition entry is reparse-backed.");
                }

                if ((attributes & FileAttributes.Directory) != 0)
                {
                    TraverseDirectory(absolutePath);
                    continue;
                }

                if ((attributes & FileAttributes.Device) != 0)
                {
                    throw new AtlasSafetyException("A definition entry type is unsupported.");
                }

                if (!historicalByPath.TryGetValue(
                        relativePath,
                        out HistoricalDefinitionEntry? historicalEntry))
                {
                    throw new AtlasSafetyException(
                        "The live definition set contains an unexpected file.");
                }

                HistoricalDefinitionGroup? matchedGroup = DefinitionRuleMatcher.FindFirstMatch(
                    authority.Groups,
                    relativePath);
                if (matchedGroup is null
                    || !StringComparer.Ordinal.Equals(
                        matchedGroup.GroupId,
                        historicalEntry.GroupId)
                    || !StringComparer.Ordinal.Equals(
                        matchedGroup.Decision,
                        historicalEntry.Decision)
                    || !observedByPath.TryAdd(
                        relativePath,
                        new DefinitionObservedEntry(
                            historicalEntry.SourceAlias,
                            relativePath,
                            absolutePath,
                            historicalEntry.Decision,
                            io.GetLength(absolutePath),
                            io.GetLastWriteTimeUtc(absolutePath))))
                {
                    throw new AtlasSafetyException(
                        "The live definition set does not match historical authority.");
                }
            }
        }
    }

    private static bool IsExcludedRootRelativePath(string relativePath) =>
        StringComparer.OrdinalIgnoreCase.Equals(relativePath, "Game.exe")
        || StringComparer.OrdinalIgnoreCase.Equals(relativePath, "save")
        || relativePath.StartsWith("save/", StringComparison.OrdinalIgnoreCase)
        || StringComparer.OrdinalIgnoreCase.Equals(relativePath, "www/save")
        || relativePath.StartsWith("www/save/", StringComparison.OrdinalIgnoreCase);

    private static void EnsureSnapshotsEquivalent(
        DefinitionTraversalSnapshot before,
        DefinitionTraversalSnapshot after)
    {
        if (before.OrderedEntries.Count != after.OrderedEntries.Count)
        {
            throw new AtlasSafetyException("The definition tree changed during copying.");
        }

        for (int index = 0; index < before.OrderedEntries.Count; index++)
        {
            DefinitionObservedEntry first = before.OrderedEntries[index];
            DefinitionObservedEntry second = after.OrderedEntries[index];
            if (!StringComparer.OrdinalIgnoreCase.Equals(
                    first.RelativePath,
                    second.RelativePath)
                || !StringComparer.Ordinal.Equals(first.SourceAlias, second.SourceAlias)
                || !StringComparer.Ordinal.Equals(first.Decision, second.Decision)
                || first.Length != second.Length
                || first.LastWriteTimeUtc != second.LastWriteTimeUtc)
            {
                throw new AtlasSafetyException("The definition tree changed during copying.");
            }
        }
    }

    private static async ValueTask<AtlasDefinitionCopyReceiptEntry> CopyOneAsync(
        DefinitionObservedEntry source,
        DefinitionCopyPlanEntry planEntry,
        string destinationPath,
        AtlasIoSeams io,
        CancellationToken cancellationToken)
    {
        await using Stream sourceStream = io.OpenFile(
            source.AbsolutePath,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read,
            FileOptions.Asynchronous | FileOptions.SequentialScan);
        long initialLength = io.GetLength(source.AbsolutePath);
        DateTimeOffset initialLastWrite = io.GetLastWriteTimeUtc(source.AbsolutePath);
        if (initialLength != source.Length
            || initialLastWrite != source.LastWriteTimeUtc
            || (sourceStream.CanSeek && sourceStream.Length != initialLength))
        {
            throw new AtlasSafetyException("A definition source changed before copying.");
        }

        long copiedLength = 0;
        string sourceSha256;
        using (IncrementalHash hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256))
        {
            await using Stream destinationStream = io.OpenFile(
                destinationPath,
                FileMode.CreateNew,
                FileAccess.Write,
                FileShare.None,
                FileOptions.Asynchronous | FileOptions.SequentialScan);
            byte[] buffer = ArrayPool<byte>.Shared.Rent(64 * 1024);
            try
            {
                while (true)
                {
                    int read = await sourceStream.ReadAsync(
                            buffer.AsMemory(0, buffer.Length),
                            cancellationToken)
                        .ConfigureAwait(false);
                    if (read == 0)
                    {
                        break;
                    }

                    hash.AppendData(buffer, 0, read);
                    await destinationStream.WriteAsync(
                            buffer.AsMemory(0, read),
                            cancellationToken)
                        .ConfigureAwait(false);
                    copiedLength += read;
                }

                await destinationStream.FlushAsync(cancellationToken).ConfigureAwait(false);
                destinationStream.Flush();
            }
            finally
            {
                ArrayPool<byte>.Shared.Return(buffer);
            }

            sourceSha256 = Convert.ToHexStringLower(hash.GetHashAndReset());
        }

        if (copiedLength != initialLength
            || io.GetLength(source.AbsolutePath) != initialLength
            || io.GetLastWriteTimeUtc(source.AbsolutePath) != initialLastWrite
            || (sourceStream.CanSeek && sourceStream.Length != initialLength))
        {
            throw new AtlasSafetyException("A definition source changed during copying.");
        }

        (long destinationLength, string destinationSha256) =
            await HashFileAsync(destinationPath, io, cancellationToken).ConfigureAwait(false);
        if (destinationLength != copiedLength
            || !StringComparer.Ordinal.Equals(sourceSha256, destinationSha256))
        {
            throw new AtlasSafetyException("A copied definition failed verification.");
        }

        return new AtlasDefinitionCopyReceiptEntry
        {
            SourceAlias = planEntry.SourceAlias,
            DestinationRelativePath = planEntry.DestinationRelativePath,
            Length = copiedLength,
            Sha256 = sourceSha256,
        };
    }

    private static async ValueTask<(long Length, string Sha256)> HashFileAsync(
        string path,
        AtlasIoSeams io,
        CancellationToken cancellationToken)
    {
        FileAttributes attributes = io.GetAttributes(path);
        if ((attributes & (FileAttributes.Directory | FileAttributes.ReparsePoint)) != 0)
        {
            throw new AtlasSafetyException("A copied definition is not an ordinary file.");
        }

        await using Stream stream = io.OpenFile(
            path,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read,
            FileOptions.Asynchronous | FileOptions.SequentialScan);
        using IncrementalHash hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        long length = 0;
        byte[] buffer = ArrayPool<byte>.Shared.Rent(64 * 1024);
        try
        {
            while (true)
            {
                int read = await stream.ReadAsync(
                        buffer.AsMemory(0, buffer.Length),
                        cancellationToken)
                    .ConfigureAwait(false);
                if (read == 0)
                {
                    break;
                }

                hash.AppendData(buffer, 0, read);
                length += read;
            }
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(buffer);
        }

        long reportedLength = io.GetLength(path);
        if (length != reportedLength || (stream.CanSeek && stream.Length != length))
        {
            throw new AtlasSafetyException("A copied definition length is unstable.");
        }

        return (length, Convert.ToHexStringLower(hash.GetHashAndReset()));
    }

    private static async ValueTask WriteNewFileAsync(
        string path,
        ReadOnlyMemory<byte> bytes,
        AtlasIoSeams io,
        CancellationToken cancellationToken)
    {
        await using Stream stream = io.OpenFile(
            path,
            FileMode.CreateNew,
            FileAccess.Write,
            FileShare.None,
            FileOptions.Asynchronous | FileOptions.SequentialScan);
        await stream.WriteAsync(bytes, cancellationToken).ConfigureAwait(false);
        await stream.FlushAsync(cancellationToken).ConfigureAwait(false);
        stream.Flush();
    }

    private static async ValueTask<bool> IsValidCandidateAsync(
        string candidateRoot,
        string receiptPath,
        AtlasDefinitionIntakeRequest request,
        HistoricalDefinitionAuthority authority,
        DefinitionCopyPlan plan,
        AtlasIoSeams io,
        CancellationToken cancellationToken)
    {
        try
        {
            if (!io.DirectoryExists(candidateRoot) || !io.FileExists(receiptPath))
            {
                return false;
            }

            ValidatePresentOutputRoot(candidateRoot, io);
            AtlasDefinitionCopyReceipt receipt =
                await AtlasDefinitionIntakeContracts.ReadReceiptAsync(
                        receiptPath,
                        io,
                        cancellationToken)
                    .ConfigureAwait(false);
            if (!StringComparer.Ordinal.Equals(
                    receipt.HistoricalAuthoritySha256,
                    authority.Sha256)
                || receipt.HistoricalAuthorityRevision != authority.Revision
                || receipt.ApplicationId != authority.ApplicationId
                || receipt.BuildId != authority.BuildId
                || !StringComparer.Ordinal.Equals(receipt.RunId, request.RunId)
                || !AtlasDefinitionIntakeContracts.PathEquals(
                    receipt.DefinitionRoot,
                    request.DefinitionRoot)
                || !AtlasDefinitionIntakeContracts.PathEquals(
                    receipt.FinalCopyRoot,
                    Path.Combine(
                        Path.GetDirectoryName(candidateRoot)!,
                        "definition-snapshot"))
                || receipt.Entries.Length != plan.Entries.Count)
            {
                return false;
            }

            HashSet<string> expectedFiles = new(StringComparer.OrdinalIgnoreCase)
            {
                "definition-copy-receipt.json",
            };
            for (int index = 0; index < plan.Entries.Count; index++)
            {
                DefinitionCopyPlanEntry expected = plan.Entries[index];
                AtlasDefinitionCopyReceiptEntry actual = receipt.Entries[index];
                if (!StringComparer.Ordinal.Equals(actual.SourceAlias, expected.SourceAlias)
                    || !StringComparer.Ordinal.Equals(
                        AtlasDefinitionIntakeContracts.NormalizeRelativePath(
                            actual.DestinationRelativePath),
                        expected.DestinationRelativePath))
                {
                    return false;
                }

                string destination = GetContainedDestinationPath(
                    candidateRoot,
                    expected.DestinationRelativePath);
                if (!io.FileExists(destination))
                {
                    return false;
                }

                (long length, string sha256) = await HashFileAsync(
                        destination,
                        io,
                        cancellationToken)
                    .ConfigureAwait(false);
                if (length != actual.Length
                    || !StringComparer.Ordinal.Equals(sha256, actual.Sha256))
                {
                    return false;
                }

                expectedFiles.Add(expected.DestinationRelativePath);
            }

            CandidateTree tree = EnumerateCandidateTree(candidateRoot, io);
            HashSet<string> expectedDirectories = new(StringComparer.OrdinalIgnoreCase)
            {
                "definitions",
            };
            return tree.Files.SetEquals(expectedFiles)
                && tree.Directories.SetEquals(expectedDirectories);
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
            or InvalidOperationException)
        {
            return false;
        }
    }

    private static CandidateTree EnumerateCandidateTree(string candidateRoot, AtlasIoSeams io)
    {
        HashSet<string> files = new(StringComparer.OrdinalIgnoreCase);
        HashSet<string> directories = new(StringComparer.OrdinalIgnoreCase);
        Traverse(candidateRoot);
        return new CandidateTree(files, directories);

        void Traverse(string directory)
        {
            foreach (string candidate in io.EnumerateFileSystemEntries(
                         directory,
                         SearchOption.TopDirectoryOnly))
            {
                string absolutePath = Path.GetFullPath(candidate);
                if (!AtlasDefinitionIntakeContracts.ContainsPath(candidateRoot, absolutePath)
                    || AtlasDefinitionIntakeContracts.PathEquals(candidateRoot, absolutePath))
                {
                    throw new AtlasSafetyException("A copied definition path escapes its root.");
                }

                string relativePath = AtlasDefinitionIntakeContracts.NormalizeRelativePath(
                    Path.GetRelativePath(candidateRoot, absolutePath));
                FileAttributes attributes = io.GetAttributes(absolutePath);
                if ((attributes & FileAttributes.ReparsePoint) != 0)
                {
                    throw new AtlasSafetyException("A copied definition path is reparse-backed.");
                }

                if ((attributes & FileAttributes.Directory) != 0)
                {
                    if (!directories.Add(relativePath))
                    {
                        throw new AtlasSafetyException("A copied definition directory collides.");
                    }

                    Traverse(absolutePath);
                }
                else if (!files.Add(relativePath))
                {
                    throw new AtlasSafetyException("A copied definition file collides.");
                }
            }
        }
    }

    private static string GetContainedDestinationPath(
        string root,
        string relativePath)
    {
        string normalizedRelative =
            AtlasDefinitionIntakeContracts.NormalizeRelativePath(relativePath);
        string destination = Path.GetFullPath(
            Path.Combine(
                root,
                normalizedRelative.Replace('/', Path.DirectorySeparatorChar)));
        if (!AtlasDefinitionIntakeContracts.ContainsPath(root, destination)
            || AtlasDefinitionIntakeContracts.PathEquals(root, destination))
        {
            throw new AtlasSafetyException("A definition destination escapes its root.");
        }

        return destination;
    }

    private static bool PathExists(string path, AtlasIoSeams io) =>
        io.FileExists(path) || io.DirectoryExists(path);

    private static void ValidateExistingDirectory(
        string path,
        AtlasIoSeams io,
        string message)
    {
        if (!io.DirectoryExists(path))
        {
            throw new AtlasSafetyException(message);
        }

        FileAttributes attributes = io.GetAttributes(path);
        if ((attributes & FileAttributes.Directory) == 0
            || (attributes & FileAttributes.ReparsePoint) != 0)
        {
            throw new AtlasSafetyException(message);
        }
    }

    private static void ValidatePresentOutputRoot(string path, AtlasIoSeams io)
    {
        if (!io.DirectoryExists(path))
        {
            throw new AtlasSafetyException("A definition snapshot root is not a directory.");
        }

        FileAttributes attributes = io.GetAttributes(path);
        if ((attributes & FileAttributes.Directory) == 0
            || (attributes & FileAttributes.ReparsePoint) != 0)
        {
            throw new AtlasSafetyException("A definition snapshot root is invalid.");
        }
    }

    private sealed record DefinitionCopyPlan(IReadOnlyList<DefinitionCopyPlanEntry> Entries);

    private sealed record DefinitionCopyPlanEntry(
        string SourceAlias,
        string SourceRelativePath,
        string DestinationRelativePath);

    private sealed record DefinitionTraversalSnapshot(
        IReadOnlyList<DefinitionObservedEntry> OrderedEntries,
        IReadOnlyDictionary<string, DefinitionObservedEntry> EntriesByAlias);

    private sealed record DefinitionObservedEntry(
        string SourceAlias,
        string RelativePath,
        string AbsolutePath,
        string Decision,
        long Length,
        DateTimeOffset LastWriteTimeUtc);

    private sealed record CandidateTree(
        HashSet<string> Files,
        HashSet<string> Directories);
}

internal static class DefinitionRuleMatcher
{
    internal static HistoricalDefinitionGroup? FindFirstMatch(
        IReadOnlyList<HistoricalDefinitionGroup> groups,
        string relativePath)
    {
        foreach (HistoricalDefinitionGroup group in groups)
        {
            if (Matches(group.SelectionRule, relativePath))
            {
                return group;
            }
        }

        return null;
    }

    private static bool Matches(string selectionRule, string relativePath)
    {
        if (!AtlasIntakeContracts.TrySplitDefinitionSelectionRule(
                selectionRule,
                out string[] ruleSegments))
        {
            throw new AtlasSafetyException("A definition selection rule is invalid.");
        }

        string[] pathSegments = AtlasDefinitionIntakeContracts.NormalizeRelativePath(relativePath)
            .Split('/', StringSplitOptions.None);
        return Matches(ruleSegments, 0, pathSegments, 0);
    }

    private static bool Matches(
        string[] ruleSegments,
        int ruleIndex,
        string[] pathSegments,
        int pathIndex)
    {
        while (ruleIndex < ruleSegments.Length)
        {
            if (StringComparer.Ordinal.Equals(ruleSegments[ruleIndex], "**"))
            {
                while (ruleIndex + 1 < ruleSegments.Length
                       && StringComparer.Ordinal.Equals(ruleSegments[ruleIndex + 1], "**"))
                {
                    ruleIndex++;
                }

                if (ruleIndex == ruleSegments.Length - 1)
                {
                    return true;
                }

                for (int candidateIndex = pathIndex;
                     candidateIndex <= pathSegments.Length;
                     candidateIndex++)
                {
                    if (Matches(
                            ruleSegments,
                            ruleIndex + 1,
                            pathSegments,
                            candidateIndex))
                    {
                        return true;
                    }
                }

                return false;
            }

            if (pathIndex >= pathSegments.Length
                || !MatchesSegment(ruleSegments[ruleIndex], pathSegments[pathIndex]))
            {
                return false;
            }

            ruleIndex++;
            pathIndex++;
        }

        return pathIndex == pathSegments.Length;
    }

    private static bool MatchesSegment(string ruleSegment, string pathSegment)
    {
        foreach (string expanded in ExpandSegment(ruleSegment))
        {
            if (MatchesPattern(expanded, pathSegment))
            {
                return true;
            }
        }

        return false;
    }

    private static List<string> ExpandSegment(string ruleSegment)
    {
        List<string> expansions = [string.Empty];
        for (int index = 0; index < ruleSegment.Length; index++)
        {
            if (ruleSegment[index] != '{')
            {
                for (int expansionIndex = 0;
                     expansionIndex < expansions.Count;
                     expansionIndex++)
                {
                    expansions[expansionIndex] += ruleSegment[index];
                }

                continue;
            }

            int closeIndex = ruleSegment.IndexOf('}', index + 1);
            if (closeIndex < 0)
            {
                throw new AtlasSafetyException("A definition selection rule is invalid.");
            }

            string[] options = ruleSegment[(index + 1)..closeIndex]
                .Split(',', StringSplitOptions.None);
            if (options.Any(static option => option.Length == 0))
            {
                throw new AtlasSafetyException("A definition selection rule is invalid.");
            }

            expansions =
            [
                .. expansions.SelectMany(prefix => options.Select(option => prefix + option)),
            ];
            index = closeIndex;
        }

        return expansions;
    }

    private static bool MatchesPattern(string ruleSegment, string pathSegment)
    {
        int ruleIndex = 0;
        int pathIndex = 0;
        int starIndex = -1;
        int matchIndex = -1;
        while (pathIndex < pathSegment.Length)
        {
            if (ruleIndex < ruleSegment.Length
                && char.ToUpperInvariant(ruleSegment[ruleIndex])
                    == char.ToUpperInvariant(pathSegment[pathIndex]))
            {
                ruleIndex++;
                pathIndex++;
                continue;
            }

            if (ruleIndex < ruleSegment.Length && ruleSegment[ruleIndex] == '*')
            {
                starIndex = ruleIndex++;
                matchIndex = pathIndex;
                continue;
            }

            if (starIndex < 0)
            {
                return false;
            }

            ruleIndex = starIndex + 1;
            pathIndex = ++matchIndex;
        }

        while (ruleIndex < ruleSegment.Length && ruleSegment[ruleIndex] == '*')
        {
            ruleIndex++;
        }

        return ruleIndex == ruleSegment.Length;
    }
}
