using System.Buffers;
using System.Security.Cryptography;

namespace Hcoona.CelesphoniaModifier.Atlas;

public static class AtlasSaveSnapshot
{
    private static readonly string[] CanonicalNames =
    [
        "global.rpgsave",
        "config.rpgsave",
        .. Enumerable.Range(1, 20).Select(static index => $"file{index}.rpgsave"),
    ];

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

        AtlasSaveSnapshotRequest request = await AtlasSaveSnapshotContracts.ReadRequestAsync(
                requestPath,
                io,
                cancellationToken)
            .ConfigureAwait(false);
        AtlasSaveSnapshotLayout layout = AtlasSaveSnapshotContracts.CreateLayout(request);
        ValidateLayout(request, layout, io);

        bool incompleteExists = PathExists(layout.IncompleteRoot, io);
        bool finalExists = PathExists(layout.FinalRoot, io);
        if (incompleteExists && finalExists)
        {
            ValidateOutputRoot(layout.IncompleteRoot, io);
            ValidateOutputRoot(layout.FinalRoot, io);
            throw new AtlasSafetyException("Both save snapshot roots are present.");
        }

        if (finalExists)
        {
            ValidateOutputRoot(layout.FinalRoot, io);
            if (!await IsValidCandidateAsync(
                    layout.FinalRoot,
                    layout.FinalReceiptPath,
                    request,
                    layout,
                    io,
                    cancellationToken).ConfigureAwait(false))
            {
                throw new AtlasSafetyException("The final save snapshot is invalid.");
            }

            return;
        }

        List<string>? cleanableIncompleteChildren = null;
        if (incompleteExists)
        {
            ValidateOutputRoot(layout.IncompleteRoot, io);
            if (await IsValidCandidateAsync(
                    layout.IncompleteRoot,
                    layout.IncompleteReceiptPath,
                    request,
                    layout,
                    io,
                    cancellationToken).ConfigureAwait(false))
            {
                ValidatePhysicalSeparation(
                    request.SaveRoot,
                    layout,
                    io,
                    cancellationToken);
                cancellationToken.ThrowIfCancellationRequested();
                io.MoveDirectory(layout.IncompleteRoot, layout.FinalRoot);
                return;
            }

            cleanableIncompleteChildren = GetCleanableIncompleteChildren(
                layout.IncompleteRoot,
                io,
                cancellationToken);
        }

        ValidateSnapshotCopyPlatform(OperatingSystem.IsWindows());
        SaveSelection before = EnumerateSelection(
            request.SaveRoot,
            io,
            cancellationToken);
        ValidatePhysicalSeparation(
            request.SaveRoot,
            layout,
            io,
            cancellationToken);
        if (cleanableIncompleteChildren is not null)
        {
            foreach (string child in cleanableIncompleteChildren)
            {
                cancellationToken.ThrowIfCancellationRequested();
                io.DeleteFile(child);
            }

            cancellationToken.ThrowIfCancellationRequested();
            io.DeleteDirectory(layout.IncompleteRoot, false);
        }

        EnsureOutputDirectories(layout, io);
        List<AtlasSaveSnapshotReceiptEntry> receiptEntries = [];
        foreach (SaveObservedEntry entry in before.Entries)
        {
            cancellationToken.ThrowIfCancellationRequested();
            string destination = GetContainedChildPath(
                layout.IncompleteRoot,
                entry.CanonicalName);
            receiptEntries.Add(
                await CopyOneAsync(entry, destination, io, cancellationToken)
                    .ConfigureAwait(false));
        }

        SaveSelection after = EnumerateSelection(
            request.SaveRoot,
            io,
            cancellationToken);
        EnsureSelectionsEquivalent(before, after);

        AtlasSaveSnapshotReceipt receipt = new()
        {
            SchemaVersion = AtlasSaveSnapshotContracts.ReceiptSchemaVersion,
            RunId = request.RunId,
            SaveRoot = AtlasSaveSnapshotContracts.NormalizeAbsolutePath(request.SaveRoot),
            FinalSnapshotRoot = layout.FinalRoot,
            Entries = [.. receiptEntries],
        };
        await WriteNewFileAsync(
                layout.IncompleteReceiptPath,
                AtlasSaveSnapshotContracts.SerializeReceipt(receipt),
                io,
                cancellationToken)
            .ConfigureAwait(false);
        if (!await IsValidCandidateAsync(
                layout.IncompleteRoot,
                layout.IncompleteReceiptPath,
                request,
                layout,
                io,
                cancellationToken).ConfigureAwait(false))
        {
            throw new AtlasSafetyException("The completed save snapshot is invalid.");
        }

        cancellationToken.ThrowIfCancellationRequested();
        io.MoveDirectory(layout.IncompleteRoot, layout.FinalRoot);
    }

    internal static bool TryGetCanonicalName(
        string sourceName,
        out string canonicalName,
        out int order)
    {
        for (int index = 0; index < CanonicalNames.Length; index++)
        {
            if (StringComparer.OrdinalIgnoreCase.Equals(sourceName, CanonicalNames[index]))
            {
                canonicalName = CanonicalNames[index];
                order = index;
                return true;
            }
        }

        canonicalName = string.Empty;
        order = -1;
        return false;
    }

    private static void ValidateLayout(
        AtlasSaveSnapshotRequest request,
        AtlasSaveSnapshotLayout layout,
        AtlasIoSeams io)
    {
        string repositoryRoot = AtlasSaveSnapshotContracts.NormalizeAbsolutePath(
            request.RepositoryRoot);
        if (!AtlasSaveSnapshotContracts.PathEquals(repositoryRoot, layout.RepositoryRoot))
        {
            throw new AtlasSafetyException("The save snapshot layout is invalid.");
        }

        string[] requiredComponents =
        [
            repositoryRoot,
            Path.Combine(repositoryRoot, "src"),
            Path.Combine(repositoryRoot, "src", "private"),
            Path.Combine(repositoryRoot, "src", "private", "app"),
            Path.Combine(repositoryRoot, "src", "private", "app", "celesphonia-modifier"),
        ];
        foreach (string component in requiredComponents)
        {
            ValidateExistingOrdinaryDirectory(component, io, "The repository layout is invalid.");
        }

        string expectedPrivateParent = Path.GetFullPath(
            Path.Combine(
                requiredComponents[^1],
                ".private",
                "atlas-save-snapshot"));
        if (!AtlasSaveSnapshotContracts.PathEquals(expectedPrivateParent, layout.PrivateParent)
            || !AtlasSaveSnapshotContracts.ContainsPath(
                layout.PrivateParent,
                layout.WorkspaceRoot)
            || !StringComparer.Ordinal.Equals(
                Path.GetFileName(layout.IncompleteRoot),
                "save-snapshot.incomplete")
            || !StringComparer.Ordinal.Equals(
                Path.GetFileName(layout.FinalRoot),
                "save-snapshot")
            || !StringComparer.Ordinal.Equals(
                Path.GetFileName(layout.IncompleteReceiptPath),
                AtlasSaveSnapshotContracts.ReceiptFileName)
            || !StringComparer.Ordinal.Equals(
                Path.GetFileName(layout.FinalReceiptPath),
                AtlasSaveSnapshotContracts.ReceiptFileName))
        {
            throw new AtlasSafetyException("The save snapshot layout is invalid.");
        }

        string normalizedSaveRoot = AtlasSaveSnapshotContracts.NormalizeAbsolutePath(
            request.SaveRoot);
        if (AtlasSaveSnapshotContracts.ContainsPath(normalizedSaveRoot, layout.WorkspaceRoot)
            || AtlasSaveSnapshotContracts.ContainsPath(
                layout.WorkspaceRoot,
                normalizedSaveRoot))
        {
            throw new AtlasSafetyException("The save root and output workspace overlap.");
        }

        string applicationRoot = requiredComponents[^1];
        string[] optionalComponents =
        [
            Path.Combine(applicationRoot, ".private"),
            layout.PrivateParent,
            layout.WorkspaceRoot,
            layout.IncompleteRoot,
            layout.FinalRoot,
        ];
        foreach (string component in optionalComponents)
        {
            ValidateOptionalOutputComponent(component, io);
        }
    }

    private static void EnsureOutputDirectories(
        AtlasSaveSnapshotLayout layout,
        AtlasIoSeams io)
    {
        string applicationRoot = Path.Combine(
            layout.RepositoryRoot,
            "src",
            "private",
            "app",
            "celesphonia-modifier");
        string[] components =
        [
            Path.Combine(applicationRoot, ".private"),
            layout.PrivateParent,
            layout.WorkspaceRoot,
            layout.IncompleteRoot,
        ];
        foreach (string component in components)
        {
            if (!AtlasSaveSnapshotContracts.ContainsPath(applicationRoot, component))
            {
                throw new AtlasSafetyException("The output directory escapes its root.");
            }

            if (!io.DirectoryExists(component))
            {
                if (io.FileExists(component))
                {
                    throw new AtlasSafetyException("An output component has an unsupported type.");
                }

                io.CreateDirectory(component);
            }

            ValidateExistingOrdinaryDirectory(
                component,
                io,
                "An output component is invalid.");
        }
    }

    internal static void ValidateSnapshotCopyPlatform(bool isWindows)
    {
        if (!isWindows)
        {
            throw new AtlasSafetyException(
                "Creating a save snapshot requires Windows file-sharing semantics.");
        }
    }

    private static SaveSelection EnumerateSelection(
        string saveRoot,
        AtlasIoSeams io,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        string normalizedRoot = AtlasSaveSnapshotContracts.NormalizeAbsolutePath(saveRoot);
        ValidateExistingOrdinaryDirectory(
            normalizedRoot,
            io,
            "The save root is invalid.");

        Dictionary<string, SaveObservedEntry> selected =
            new(StringComparer.OrdinalIgnoreCase);
        foreach (string candidate in io.EnumerateFileSystemEntries(
                     normalizedRoot,
                     SearchOption.TopDirectoryOnly))
        {
            cancellationToken.ThrowIfCancellationRequested();
            string leaf = Path.GetFileName(candidate);
            if (!TryGetCanonicalName(leaf, out string canonicalName, out int order))
            {
                continue;
            }

            if (selected.ContainsKey(canonicalName))
            {
                throw new AtlasSafetyException("Supported save names collide.");
            }

            string absolutePath = Path.GetFullPath(candidate);
            if (!AtlasSaveSnapshotContracts.ContainsPath(normalizedRoot, absolutePath)
                || AtlasSaveSnapshotContracts.PathEquals(normalizedRoot, absolutePath)
                || !StringComparer.Ordinal.Equals(Path.GetFileName(absolutePath), leaf))
            {
                throw new AtlasSafetyException("A supported save path escapes its root.");
            }

            FileAttributes attributes = io.GetAttributes(absolutePath);
            if ((attributes
                    & (FileAttributes.Directory
                       | FileAttributes.ReparsePoint
                       | FileAttributes.Device)) != 0
                || !io.FileExists(absolutePath))
            {
                throw new AtlasSafetyException("A supported save entry is not an ordinary file.");
            }

            selected.Add(
                canonicalName,
                new SaveObservedEntry(
                    leaf,
                    canonicalName,
                    order,
                    absolutePath,
                    io.GetLength(absolutePath),
                    io.GetLastWriteTimeUtc(absolutePath)));
        }

        cancellationToken.ThrowIfCancellationRequested();
        if (selected.Count == 0)
        {
            throw new AtlasSafetyException("The save root contains no supported ordinary file.");
        }

        return new SaveSelection(
            selected.Values.OrderBy(static entry => entry.Order).ToArray());
    }

    private static void EnsureSelectionsEquivalent(
        SaveSelection before,
        SaveSelection after)
    {
        if (before.Entries.Count != after.Entries.Count)
        {
            throw new AtlasSafetyException("The supported save set changed during copying.");
        }

        for (int index = 0; index < before.Entries.Count; index++)
        {
            SaveObservedEntry first = before.Entries[index];
            SaveObservedEntry second = after.Entries[index];
            if (!StringComparer.Ordinal.Equals(first.SourceFileName, second.SourceFileName)
                || !StringComparer.Ordinal.Equals(first.CanonicalName, second.CanonicalName)
                || first.Length != second.Length
                || first.LastWriteTimeUtc != second.LastWriteTimeUtc)
            {
                throw new AtlasSafetyException("The supported save set changed during copying.");
            }
        }
    }

    private static void ValidatePhysicalSeparation(
        string saveRoot,
        AtlasSaveSnapshotLayout layout,
        AtlasIoSeams io,
        CancellationToken cancellationToken)
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        cancellationToken.ThrowIfCancellationRequested();
        string normalizedSaveRoot =
            AtlasSaveSnapshotContracts.NormalizeAbsolutePath(saveRoot);
        string? resolvedSaveRoot = io.TryGetDirectoryFinalPath(normalizedSaveRoot);
        if (resolvedSaveRoot is null)
        {
            return;
        }

        string physicalSaveRoot = NormalizePhysicalPath(resolvedSaveRoot);
        string[] outputRoots =
        {
            layout.PrivateParent,
            layout.WorkspaceRoot,
            layout.IncompleteRoot,
            layout.FinalRoot,
        };
        foreach (string outputRoot in outputRoots)
        {
            cancellationToken.ThrowIfCancellationRequested();
            string physicalOutputRoot = ResolvePhysicalDirectoryPath(
                outputRoot,
                io,
                cancellationToken);
            if (PhysicalContains(physicalSaveRoot, physicalOutputRoot)
                || PhysicalContains(physicalOutputRoot, physicalSaveRoot))
            {
                throw new AtlasSafetyException(
                    "The save root and output workspace physically overlap.");
            }
        }
    }

    private static string ResolvePhysicalDirectoryPath(
            string path,
            AtlasIoSeams io,
            CancellationToken cancellationToken)
    {
        Stack<string> missingSegments = [];
        string current = AtlasSaveSnapshotContracts.NormalizeAbsolutePath(path);
        string? resolved;
        while ((resolved = io.TryGetDirectoryFinalPath(current)) is null)
        {
            cancellationToken.ThrowIfCancellationRequested();
            string leaf = Path.GetFileName(current);
            DirectoryInfo? parent = Directory.GetParent(current);
            if (leaf.Length == 0 || parent is null)
            {
                throw new AtlasSafetyException(
                    "An output path has no existing directory ancestor.");
            }

            missingSegments.Push(leaf);
            current = parent.FullName;
        }
        cancellationToken.ThrowIfCancellationRequested();
        cancellationToken.ThrowIfCancellationRequested();
        while (missingSegments.TryPop(out string? segment))
        {
            resolved = Path.Combine(resolved, segment);
        }

        return NormalizePhysicalPath(resolved);
    }

    private static bool PhysicalContains(string root, string candidate)
    {
        string rootPrefix = root.EndsWith('\\')
            ? root
            : root + '\\';
        return StringComparer.OrdinalIgnoreCase.Equals(root, candidate)
            || candidate.StartsWith(rootPrefix, StringComparison.OrdinalIgnoreCase);
    }

    private static string NormalizePhysicalPath(string path)
    {
        string normalized = path.Replace('/', '\\');
        string? root = Path.GetPathRoot(normalized);
        return root is not null && normalized.Length > root.Length
            ? normalized.TrimEnd('\\')
            : normalized;
    }

    private static async ValueTask<AtlasSaveSnapshotReceiptEntry> CopyOneAsync(
        SaveObservedEntry source,
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
        long heldLength = io.GetLength(source.AbsolutePath);
        DateTimeOffset heldLastWrite = io.GetLastWriteTimeUtc(source.AbsolutePath);
        if (heldLength != source.Length
            || heldLastWrite != source.LastWriteTimeUtc
            || (sourceStream.CanSeek && sourceStream.Length != heldLength))
        {
            throw new AtlasSafetyException("A save source changed before copying.");
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
                    cancellationToken.ThrowIfCancellationRequested();
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
                    copiedLength = checked(copiedLength + read);
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

        (long destinationLength, string destinationSha256) = await HashOrdinaryFileAsync(
                destinationPath,
                io,
                cancellationToken)
            .ConfigureAwait(false);
        if (destinationLength != copiedLength
            || copiedLength != heldLength
            || !StringComparer.Ordinal.Equals(sourceSha256, destinationSha256))
        {
            throw new AtlasSafetyException("A copied save failed verification.");
        }

        if (io.GetLength(source.AbsolutePath) != heldLength
            || io.GetLastWriteTimeUtc(source.AbsolutePath) != heldLastWrite
            || (sourceStream.CanSeek && sourceStream.Length != heldLength))
        {
            throw new AtlasSafetyException("A save source changed during copying.");
        }

        return new AtlasSaveSnapshotReceiptEntry
        {
            SourceFileName = source.SourceFileName,
            DestinationRelativePath = source.CanonicalName,
            Length = copiedLength,
            Sha256 = sourceSha256,
        };
    }

    private static async ValueTask<(long Length, string Sha256)> HashOrdinaryFileAsync(
        string path,
        AtlasIoSeams io,
        CancellationToken cancellationToken)
    {
        FileAttributes attributes = io.GetAttributes(path);
        if ((attributes
                & (FileAttributes.Directory
                   | FileAttributes.ReparsePoint
                   | FileAttributes.Device)) != 0
            || !io.FileExists(path))
        {
            throw new AtlasSafetyException("A snapshot entry is not an ordinary file.");
        }

        await using Stream stream = io.OpenFile(
            path,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read,
            FileOptions.Asynchronous | FileOptions.SequentialScan);
        using IncrementalHash hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        byte[] buffer = ArrayPool<byte>.Shared.Rent(64 * 1024);
        long length = 0;
        try
        {
            while (true)
            {
                cancellationToken.ThrowIfCancellationRequested();
                int read = await stream.ReadAsync(
                        buffer.AsMemory(0, buffer.Length),
                        cancellationToken)
                    .ConfigureAwait(false);
                if (read == 0)
                {
                    break;
                }

                hash.AppendData(buffer, 0, read);
                length = checked(length + read);
            }
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(buffer);
        }

        if (io.GetLength(path) != length || (stream.CanSeek && stream.Length != length))
        {
            throw new AtlasSafetyException("A snapshot entry length is unstable.");
        }

        return (length, Convert.ToHexStringLower(hash.GetHashAndReset()));
    }

    private static async ValueTask<bool> IsValidCandidateAsync(
        string candidateRoot,
        string receiptPath,
        AtlasSaveSnapshotRequest request,
        AtlasSaveSnapshotLayout layout,
        AtlasIoSeams io,
        CancellationToken cancellationToken)
    {
        try
        {
            ValidateOutputRoot(candidateRoot, io);
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
                        return false;
                    }
                }
                else if (!TryGetCanonicalName(
                             leaf,
                             out string canonicalLeaf,
                             out _)
                         || !StringComparer.Ordinal.Equals(leaf, canonicalLeaf))
                {
                    return false;
                }

                string absolute = Path.GetFullPath(child);
                if (!AtlasSaveSnapshotContracts.ContainsPath(candidateRoot, absolute)
                    || AtlasSaveSnapshotContracts.PathEquals(candidateRoot, absolute)
                    || actualChildren.ContainsKey(leaf))
                {
                    return false;
                }

                FileAttributes attributes = io.GetAttributes(absolute);
                if ((attributes
                        & (FileAttributes.Directory
                           | FileAttributes.ReparsePoint
                           | FileAttributes.Device)) != 0
                    || !io.FileExists(absolute))
                {
                    return false;
                }

                actualChildren.Add(leaf, absolute);
            }

            if (!actualChildren.TryGetValue(
                    AtlasSaveSnapshotContracts.ReceiptFileName,
                    out string? actualReceiptPath)
                || !AtlasSaveSnapshotContracts.PathEquals(receiptPath, actualReceiptPath))
            {
                return false;
            }

            AtlasSaveSnapshotReceipt receipt =
                await AtlasSaveSnapshotContracts.ReadReceiptAsync(
                        actualReceiptPath,
                        io,
                        cancellationToken)
                    .ConfigureAwait(false);
            if (!StringComparer.Ordinal.Equals(receipt.RunId, request.RunId)
                || !AtlasSaveSnapshotContracts.PathEquals(receipt.SaveRoot, request.SaveRoot)
                || !AtlasSaveSnapshotContracts.PathEquals(
                    receipt.FinalSnapshotRoot,
                    layout.FinalRoot)
                || actualChildren.Count != receipt.Entries.Length + 1)
            {
                return false;
            }

            int priorOrder = -1;
            HashSet<string> sources = new(StringComparer.OrdinalIgnoreCase);
            foreach (AtlasSaveSnapshotReceiptEntry entry in receipt.Entries)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (!TryGetCanonicalName(
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
                    return false;
                }

                (long length, string sha256) = await HashOrdinaryFileAsync(
                        destination,
                        io,
                        cancellationToken)
                    .ConfigureAwait(false);
                if (length != entry.Length
                    || !StringComparer.Ordinal.Equals(sha256, entry.Sha256))
                {
                    return false;
                }

                priorOrder = order;
            }

            return true;
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
            return false;
        }
    }

    private static List<string> GetCleanableIncompleteChildren(
        string incompleteRoot,
        AtlasIoSeams io,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        List<string> children = [];
        HashSet<string> leaves = new(StringComparer.OrdinalIgnoreCase);
        foreach (string child in io.EnumerateFileSystemEntries(
                     incompleteRoot,
                     SearchOption.TopDirectoryOnly))
        {
            cancellationToken.ThrowIfCancellationRequested();
            string leaf = Path.GetFileName(child);
            bool allowlisted = TryGetCanonicalName(leaf, out string canonical, out _)
                && StringComparer.Ordinal.Equals(leaf, canonical)
                || StringComparer.Ordinal.Equals(
                    leaf,
                    AtlasSaveSnapshotContracts.ReceiptFileName);
            string absolute = Path.GetFullPath(child);
            if (!allowlisted
                || !AtlasSaveSnapshotContracts.ContainsPath(incompleteRoot, absolute)
                || AtlasSaveSnapshotContracts.PathEquals(incompleteRoot, absolute)
                || !leaves.Add(leaf))
            {
                throw new AtlasSafetyException(
                    "The incomplete save snapshot contains an unexpected child.");
            }

            FileAttributes attributes = io.GetAttributes(absolute);
            if ((attributes
                    & (FileAttributes.Directory
                       | FileAttributes.ReparsePoint
                       | FileAttributes.Device)) != 0
                || !io.FileExists(absolute))
            {
                throw new AtlasSafetyException(
                    "The incomplete save snapshot contains an unsupported child.");
            }

            children.Add(absolute);
        }

        return children;
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

    private static string GetContainedChildPath(string root, string leaf)
    {
        if (!StringComparer.Ordinal.Equals(Path.GetFileName(leaf), leaf))
        {
            throw new AtlasSafetyException("A snapshot destination is invalid.");
        }

        string path = Path.GetFullPath(Path.Combine(root, leaf));
        if (!AtlasSaveSnapshotContracts.ContainsPath(root, path)
            || AtlasSaveSnapshotContracts.PathEquals(root, path))
        {
            throw new AtlasSafetyException("A snapshot destination escapes its root.");
        }

        return path;
    }

    private static void ValidateExistingOrdinaryDirectory(
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
            || (attributes
                    & (FileAttributes.ReparsePoint | FileAttributes.Device)) != 0)
        {
            throw new AtlasSafetyException(message);
        }
    }

    private static void ValidateOptionalOutputComponent(string path, AtlasIoSeams io)
    {
        if (!PathExists(path, io))
        {
            return;
        }

        ValidateExistingOrdinaryDirectory(
            path,
            io,
            "An output component is invalid.");
    }

    private static void ValidateOutputRoot(string path, AtlasIoSeams io) =>
        ValidateExistingOrdinaryDirectory(
            path,
            io,
            "A save snapshot root is invalid.");

    private static bool PathExists(string path, AtlasIoSeams io) =>
        io.FileExists(path) || io.DirectoryExists(path);

    private sealed record SaveSelection(IReadOnlyList<SaveObservedEntry> Entries);

    private sealed record SaveObservedEntry(
        string SourceFileName,
        string CanonicalName,
        int Order,
        string AbsolutePath,
        long Length,
        DateTimeOffset LastWriteTimeUtc);
}
