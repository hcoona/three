using System.Buffers;
using System.Security.Cryptography;
using System.Text.Json;

namespace Hcoona.CelesphoniaModifier.Atlas;

public static class AtlasSnapshotSurvey
{
    public static ValueTask RunAsync(
        string requestPath,
        CancellationToken cancellationToken = default) =>
        RunAsync(
            requestPath,
            AtlasIoSeams.Default,
            AtlasSnapshotSurveyLimits.Default,
            AtlasSaveReaderLimits.Default,
            AtlasStructuralScannerLimits.Default,
            cancellationToken);

    internal static async ValueTask RunAsync(
        string requestPath,
        AtlasIoSeams io,
        AtlasSnapshotSurveyLimits surveyLimits,
        AtlasSaveReaderLimits readerLimits,
        AtlasStructuralScannerLimits scannerLimits,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(requestPath);
        ArgumentNullException.ThrowIfNull(io);
        ArgumentNullException.ThrowIfNull(surveyLimits);
        ArgumentNullException.ThrowIfNull(readerLimits);
        ArgumentNullException.ThrowIfNull(scannerLimits);
        surveyLimits.Validate();
        readerLimits.Validate();
        scannerLimits.Validate();

        AtlasSnapshotSurveyRequest request =
            await AtlasSnapshotSurveyContracts.ReadRequestAsync(
                    requestPath,
                    io,
                    cancellationToken)
                .ConfigureAwait(false);
        AtlasSnapshotSurveyLayout layout =
            AtlasSnapshotSurveyContracts.CreateLayout(request);
        ValidateLayout(layout, io);
        AtlasValidatedSaveSnapshot snapshot =
            await AtlasFinalizedSaveSnapshot.OpenBoundedAsync(
                    request.RepositoryRoot,
                    request.SnapshotReceiptPath,
                    io,
                    readerLimits.MaximumEncodedBytes,
                    cancellationToken)
                .ConfigureAwait(false);
        if (snapshot.Entries.Count > surveyLimits.MaximumDocuments)
        {
            throw new AtlasSafetyException(
                "The snapshot survey exceeds its document limit.");
        }

        bool incompleteExists = PathExists(layout.IncompleteRoot, io);
        bool finalExists = PathExists(layout.FinalRoot, io);
        if (incompleteExists && finalExists)
        {
            ValidatePresentOrdinaryDirectory(layout.IncompleteRoot, io);
            ValidatePresentOrdinaryDirectory(layout.FinalRoot, io);
            throw new AtlasSafetyException("Both snapshot survey roots are present.");
        }

        if (finalExists)
        {
            ValidatePresentOrdinaryDirectory(layout.FinalRoot, io);
            if (!await IsValidCandidateAsync(
                    layout.FinalRoot,
                    layout.FinalManifestPath,
                    request,
                    io,
                    surveyLimits,
                    readerLimits,
                    scannerLimits,
                    cancellationToken).ConfigureAwait(false))
            {
                throw new AtlasSafetyException("The final snapshot survey is invalid.");
            }

            return;
        }

        List<string>? cleanableChildren = null;
        if (incompleteExists)
        {
            ValidatePresentOrdinaryDirectory(layout.IncompleteRoot, io);
            if (await IsValidCandidateAsync(
                    layout.IncompleteRoot,
                    layout.IncompleteManifestPath,
                    request,
                    io,
                    surveyLimits,
                    readerLimits,
                    scannerLimits,
                    cancellationToken).ConfigureAwait(false))
            {
                cancellationToken.ThrowIfCancellationRequested();
                io.MoveDirectory(layout.IncompleteRoot, layout.FinalRoot);
                return;
            }

            cleanableChildren = GetCleanableIncompleteChildren(
                layout.IncompleteRoot,
                snapshot,
                io,
                cancellationToken);
        }

        if (cleanableChildren is not null)
        {
            foreach (string child in cleanableChildren)
            {
                cancellationToken.ThrowIfCancellationRequested();
                io.DeleteFile(child);
            }

            cancellationToken.ThrowIfCancellationRequested();
            io.DeleteDirectory(layout.IncompleteRoot, false);
        }

        EnsureOutputDirectories(layout, io);
        List<AtlasSnapshotSurveyDocument> documents = [];
        foreach (AtlasValidatedSaveSnapshotEntry entry in snapshot.Entries)
        {
            cancellationToken.ThrowIfCancellationRequested();
            AtlasSnapshotSurveyDocument document = await ProcessEntryAsync(
                    entry,
                    layout.IncompleteRoot,
                    io,
                    readerLimits,
                    scannerLimits,
                    cancellationToken)
                .ConfigureAwait(false);
            documents.Add(document);
            _ = AtlasSnapshotSurveyManifestJson.CreateTotals(
                documents,
                surveyLimits,
                cancellationToken);
        }

        cancellationToken.ThrowIfCancellationRequested();
        AtlasSnapshotSurveyTotals totals =
            AtlasSnapshotSurveyManifestJson.CreateTotals(
                documents,
                surveyLimits,
                cancellationToken);
        AtlasSnapshotSurveyManifest manifest = new(documents, totals);
        byte[] manifestBytes = AtlasSnapshotSurveyManifestJson.Serialize(
            manifest,
            surveyLimits,
            cancellationToken);
        await WriteNewFileAsync(
                layout.IncompleteManifestPath,
                manifestBytes,
                io,
                cancellationToken)
            .ConfigureAwait(false);

        if (!await IsValidCandidateAsync(
                layout.IncompleteRoot,
                layout.IncompleteManifestPath,
                request,
                io,
                surveyLimits,
                readerLimits,
                scannerLimits,
                cancellationToken).ConfigureAwait(false))
        {
            throw new AtlasSafetyException("The completed snapshot survey is invalid.");
        }

        cancellationToken.ThrowIfCancellationRequested();
        io.MoveDirectory(layout.IncompleteRoot, layout.FinalRoot);
    }

    private static async ValueTask<AtlasSnapshotSurveyDocument> ProcessEntryAsync(
        AtlasValidatedSaveSnapshotEntry entry,
        string candidateRoot,
        AtlasIoSeams io,
        AtlasSaveReaderLimits readerLimits,
        AtlasStructuralScannerLimits scannerLimits,
        CancellationToken cancellationToken)
    {
        SourceDocument source = await ReadSourceAsync(
                entry,
                io,
                readerLimits,
                cancellationToken)
            .ConfigureAwait(false);
        AtlasDocumentRole role =
            AtlasSnapshotSurveyManifestJson.GetDocumentRole(entry.RelativePath);
        string scanRelativePath = entry.RelativePath + ".structural-scan.json";
        string scanPath = GetContainedChildPath(candidateRoot, scanRelativePath);
        GeneratedScan generated = await GenerateAndWriteScanAsync(
                scanPath,
                source.ReadResult,
                role,
                io,
                scannerLimits,
                cancellationToken)
            .ConfigureAwait(false);

        PersistedScan persisted = await ReadAndValidateScanAsync(
                scanPath,
                source.ReadResult,
                role,
                io,
                scannerLimits,
                cancellationToken)
            .ConfigureAwait(false);
        if (persisted.Length != generated.Length
            || !StringComparer.Ordinal.Equals(persisted.Sha256, generated.Sha256))
        {
            throw new AtlasSafetyException("The persisted structural scan is invalid.");
        }

        return new AtlasSnapshotSurveyDocument(
            entry.RelativePath,
            role,
            scanRelativePath,
            entry.Length,
            entry.Sha256,
            persisted.Length,
            persisted.Sha256,
            persisted.Census);
    }

    private static async ValueTask<bool> IsValidCandidateAsync(
        string candidateRoot,
        string manifestPath,
        AtlasSnapshotSurveyRequest request,
        AtlasIoSeams io,
        AtlasSnapshotSurveyLimits surveyLimits,
        AtlasSaveReaderLimits readerLimits,
        AtlasStructuralScannerLimits scannerLimits,
        CancellationToken cancellationToken)
    {
        try
        {
            AtlasValidatedSaveSnapshot snapshot =
                await AtlasFinalizedSaveSnapshot.OpenBoundedAsync(
                        request.RepositoryRoot,
                        request.SnapshotReceiptPath,
                        io,
                        readerLimits.MaximumEncodedBytes,
                        cancellationToken)
                    .ConfigureAwait(false);
            Dictionary<string, string> children =
                EnumerateCandidateChildren(candidateRoot, snapshot, io, cancellationToken);
            if (!children.TryGetValue(
                    AtlasSnapshotSurveyContracts.ManifestFileName,
                    out string? actualManifestPath)
                || !AtlasSaveSnapshotContracts.PathEquals(manifestPath, actualManifestPath))
            {
                return false;
            }

            List<AtlasSnapshotSurveyDocument> documents = [];
            foreach (AtlasValidatedSaveSnapshotEntry entry in snapshot.Entries)
            {
                cancellationToken.ThrowIfCancellationRequested();
                string scanRelativePath = entry.RelativePath + ".structural-scan.json";
                if (!children.TryGetValue(scanRelativePath, out string? scanPath))
                {
                    return false;
                }

                SourceDocument source = await ReadSourceAsync(
                        entry,
                        io,
                        readerLimits,
                        cancellationToken)
                    .ConfigureAwait(false);
                AtlasDocumentRole role =
                    AtlasSnapshotSurveyManifestJson.GetDocumentRole(entry.RelativePath);
                PersistedScan persisted = await ReadAndValidateScanAsync(
                        scanPath,
                        source.ReadResult,
                        role,
                        io,
                        scannerLimits,
                        cancellationToken)
                    .ConfigureAwait(false);
                documents.Add(
                    new AtlasSnapshotSurveyDocument(
                        entry.RelativePath,
                        role,
                        scanRelativePath,
                        entry.Length,
                        entry.Sha256,
                        persisted.Length,
                        persisted.Sha256,
                        persisted.Census));
                _ = AtlasSnapshotSurveyManifestJson.CreateTotals(
                    documents,
                    surveyLimits,
                    cancellationToken);
            }

            byte[] persistedManifestBytes =
                await AtlasSnapshotSurveyContracts.ReadBoundedAsync(
                        actualManifestPath,
                        surveyLimits.MaximumManifestBytes,
                        io,
                        cancellationToken)
                    .ConfigureAwait(false);
            _ = AtlasSnapshotSurveyManifestJson.Parse(
                persistedManifestBytes,
                surveyLimits,
                cancellationToken);
            AtlasSnapshotSurveyManifest expected = new(
                documents,
                AtlasSnapshotSurveyManifestJson.CreateTotals(
                    documents,
                    surveyLimits,
                    cancellationToken));
            byte[] expectedBytes = AtlasSnapshotSurveyManifestJson.Serialize(
                expected,
                surveyLimits,
                cancellationToken);
            return BytesEqual(expectedBytes, persistedManifestBytes, cancellationToken);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception exception) when (
            exception is AtlasSafetyException
            or AtlasSaveReadException
            or AtlasStructuralScanException
            or ArgumentException
            or JsonException
            or InvalidOperationException)
        {
            return false;
        }
    }

    private static Dictionary<string, string> EnumerateCandidateChildren(
        string candidateRoot,
        AtlasValidatedSaveSnapshot snapshot,
        AtlasIoSeams io,
        CancellationToken cancellationToken)
    {
        ValidatePresentOrdinaryDirectory(candidateRoot, io);
        HashSet<string> expected = new(StringComparer.Ordinal)
        {
            AtlasSnapshotSurveyContracts.ManifestFileName,
        };
        foreach (AtlasValidatedSaveSnapshotEntry entry in snapshot.Entries)
        {
            expected.Add(entry.RelativePath + ".structural-scan.json");
        }

        Dictionary<string, string> children = new(StringComparer.OrdinalIgnoreCase);
        foreach (string child in io.EnumerateFileSystemEntries(
                     candidateRoot,
                     SearchOption.TopDirectoryOnly))
        {
            cancellationToken.ThrowIfCancellationRequested();
            string leaf = Path.GetFileName(child);
            string absolute = Path.GetFullPath(child);
            if (!expected.Contains(leaf)
                || !AtlasSaveSnapshotContracts.ContainsPath(candidateRoot, absolute)
                || AtlasSaveSnapshotContracts.PathEquals(candidateRoot, absolute)
                || children.ContainsKey(leaf)
                || !IsOrdinaryFile(absolute, io))
            {
                throw new AtlasSafetyException(
                    "The snapshot survey contains an unexpected child.");
            }

            children.Add(leaf, absolute);
        }

        if (children.Count != expected.Count)
        {
            throw new AtlasSafetyException("The snapshot survey is incomplete.");
        }

        return children;
    }

    private static async ValueTask<SourceDocument> ReadSourceAsync(
        AtlasValidatedSaveSnapshotEntry entry,
        AtlasIoSeams io,
        AtlasSaveReaderLimits readerLimits,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (entry.Length > readerLimits.MaximumEncodedBytes
            || !IsOrdinaryFile(entry.AbsolutePath, io))
        {
            throw new AtlasSafetyException("A snapshot source is invalid.");
        }

        await using Stream stream = io.OpenFile(
            entry.AbsolutePath,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read,
            FileOptions.Asynchronous | FileOptions.SequentialScan);
        using MemoryStream destination = new(
            Math.Min(readerLimits.MaximumEncodedBytes, 64 * 1024));
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

                length = checked(length + read);
                if (length > readerLimits.MaximumEncodedBytes)
                {
                    throw new AtlasSafetyException(
                        "A snapshot source exceeds the reader limit.");
                }

                hash.AppendData(buffer, 0, read);
                destination.Write(buffer, 0, read);
            }
        }
        catch (OverflowException exception)
        {
            throw new AtlasSafetyException(
                "A snapshot source length overflowed.",
                AtlasDiscoveryFailureStage.Unspecified,
                exception);
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(buffer);
        }

        if (io.GetLength(entry.AbsolutePath) != length
            || stream.CanSeek && stream.Length != length
            || length != entry.Length
            || !StringComparer.Ordinal.Equals(
                Convert.ToHexStringLower(hash.GetHashAndReset()),
                entry.Sha256))
        {
            throw new AtlasSafetyException("A snapshot source changed during reading.");
        }

        byte[] bytes = destination.ToArray();
        try
        {
            AtlasSaveReadResult result = AtlasSaveReader.Read(
                bytes,
                readerLimits,
                cancellationToken);
            return new SourceDocument(result);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (AtlasSaveReadException exception)
        {
            throw new AtlasSafetyException(
                "The snapshot save reader refused a document.",
                AtlasDiscoveryFailureStage.Unspecified,
                exception);
        }
    }

    private static async ValueTask<PersistedScan> ReadAndValidateScanAsync(
        string scanPath,
        AtlasSaveReadResult source,
        AtlasDocumentRole role,
        AtlasIoSeams io,
        AtlasStructuralScannerLimits scannerLimits,
        CancellationToken cancellationToken)
    {
        byte[] bytes = await ReadScanBytesAsync(
                scanPath,
                scannerLimits.MaximumCanonicalUtf8Bytes,
                io,
                cancellationToken)
            .ConfigureAwait(false);
        string sha256 = HashMemory(bytes, cancellationToken);
        try
        {
            AtlasStructuralScanCensus census = AtlasStructuralScanJson.ParsePersisted(
                bytes,
                source,
                role,
                scannerLimits,
                cancellationToken);
            AtlasStructuralScanJson.ValidateExpectedCanonical(
                bytes,
                source,
                role,
                scannerLimits,
                cancellationToken);
            return new PersistedScan(bytes.LongLength, sha256, census);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (AtlasStructuralScanException exception)
        {
            throw new AtlasSafetyException(
                "The persisted structural scan was refused.",
                AtlasDiscoveryFailureStage.Unspecified,
                exception);
        }
    }

    private static async ValueTask<byte[]> ReadScanBytesAsync(
        string scanPath,
        int maximumBytes,
        AtlasIoSeams io,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!IsOrdinaryFile(scanPath, io))
        {
            throw new AtlasSafetyException("A persisted structural scan is invalid.");
        }

        long observedLength = io.GetLength(scanPath);
        if (observedLength < 0 || observedLength > maximumBytes || observedLength > int.MaxValue)
        {
            throw new AtlasSafetyException(
                "A persisted structural scan exceeds its byte limit.");
        }

        byte[] bytes = new byte[checked((int)observedLength)];
        await using Stream stream = io.OpenFile(
            scanPath,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read,
            FileOptions.Asynchronous | FileOptions.SequentialScan);
        int offset = 0;
        while (offset < bytes.Length)
        {
            cancellationToken.ThrowIfCancellationRequested();
            int read = await stream.ReadAsync(
                    bytes.AsMemory(offset),
                    cancellationToken)
                .ConfigureAwait(false);
            if (read == 0)
            {
                throw new AtlasSafetyException(
                    "A persisted structural scan changed during reading.");
            }

            offset = checked(offset + read);
        }

        byte[] extra = new byte[1];
        if (await stream.ReadAsync(extra, cancellationToken).ConfigureAwait(false) != 0
            || io.GetLength(scanPath) != observedLength
            || stream.CanSeek && stream.Length != observedLength)
        {
            throw new AtlasSafetyException(
                "A persisted structural scan changed during reading.");
        }

        return bytes;
    }

    private static async ValueTask<GeneratedScan> GenerateAndWriteScanAsync(
        string scanPath,
        AtlasSaveReadResult source,
        AtlasDocumentRole role,
        AtlasIoSeams io,
        AtlasStructuralScannerLimits scannerLimits,
        CancellationToken cancellationToken)
    {
        AtlasStructuralScanResult scan;
        try
        {
            scan = AtlasStructuralScanner.Scan(
                source,
                role,
                scannerLimits,
                cancellationToken);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (AtlasStructuralScanException exception)
        {
            throw new AtlasSafetyException(
                "The snapshot structural scan was refused.",
                AtlasDiscoveryFailureStage.Unspecified,
                exception);
        }

        using AtlasStructuralScanPersistence persistence = scan.DetachForPersistence();
        ReadOnlyMemory<byte> canonicalUtf8 = persistence.CanonicalUtf8;
        GeneratedScan generated = new(
            canonicalUtf8.Length,
            HashMemory(canonicalUtf8.Span, cancellationToken));
        await WriteNewFileAsync(scanPath, canonicalUtf8, io, cancellationToken)
            .ConfigureAwait(false);
        return generated;
    }

    private static List<string> GetCleanableIncompleteChildren(
        string incompleteRoot,
        AtlasValidatedSaveSnapshot snapshot,
        AtlasIoSeams io,
        CancellationToken cancellationToken)
    {
        HashSet<string> allowlist = new(StringComparer.Ordinal)
        {
            AtlasSnapshotSurveyContracts.ManifestFileName,
        };
        foreach (AtlasValidatedSaveSnapshotEntry entry in snapshot.Entries)
        {
            allowlist.Add(entry.RelativePath + ".structural-scan.json");
        }

        List<string> children = [];
        HashSet<string> leaves = new(StringComparer.OrdinalIgnoreCase);
        foreach (string child in io.EnumerateFileSystemEntries(
                     incompleteRoot,
                     SearchOption.TopDirectoryOnly))
        {
            cancellationToken.ThrowIfCancellationRequested();
            string leaf = Path.GetFileName(child);
            string absolute = Path.GetFullPath(child);
            if (!allowlist.Contains(leaf)
                || !AtlasSaveSnapshotContracts.ContainsPath(incompleteRoot, absolute)
                || AtlasSaveSnapshotContracts.PathEquals(incompleteRoot, absolute)
                || !leaves.Add(leaf)
                || !IsOrdinaryFile(absolute, io))
            {
                throw new AtlasSafetyException(
                    "The incomplete snapshot survey contains an unsupported child.");
            }

            children.Add(absolute);
        }

        return children;
    }

    private static void ValidateLayout(
        AtlasSnapshotSurveyLayout layout,
        AtlasIoSeams io)
    {
        string applicationRoot = Path.Combine(
            layout.RepositoryRoot,
            "src",
            "private",
            "app",
            "celesphonia-modifier");
        string[] requiredDirectories =
        [
            layout.RepositoryRoot,
            Path.Combine(layout.RepositoryRoot, "src"),
            Path.Combine(layout.RepositoryRoot, "src", "private"),
            Path.Combine(layout.RepositoryRoot, "src", "private", "app"),
            applicationRoot,
        ];
        foreach (string directory in requiredDirectories)
        {
            ValidatePresentOrdinaryDirectory(directory, io);
        }

        string expectedPrivateParent = Path.GetFullPath(
            Path.Combine(applicationRoot, ".private", "atlas-snapshot-survey"));
        if (!AtlasSaveSnapshotContracts.PathEquals(
                layout.PrivateParent,
                expectedPrivateParent)
            || !AtlasSaveSnapshotContracts.ContainsPath(
                layout.PrivateParent,
                layout.WorkspaceRoot)
            || !StringComparer.Ordinal.Equals(
                Path.GetFileName(layout.IncompleteRoot),
                "survey.incomplete")
            || !StringComparer.Ordinal.Equals(
                Path.GetFileName(layout.FinalRoot),
                "survey")
            || !StringComparer.Ordinal.Equals(
                Path.GetFileName(layout.IncompleteManifestPath),
                AtlasSnapshotSurveyContracts.ManifestFileName)
            || !StringComparer.Ordinal.Equals(
                Path.GetFileName(layout.FinalManifestPath),
                AtlasSnapshotSurveyContracts.ManifestFileName))
        {
            throw new AtlasSafetyException("The snapshot survey layout is invalid.");
        }

        string[] optionalDirectories =
        [
            Path.Combine(applicationRoot, ".private"),
            layout.PrivateParent,
            layout.WorkspaceRoot,
            layout.IncompleteRoot,
            layout.FinalRoot,
        ];
        foreach (string directory in optionalDirectories)
        {
            if (PathExists(directory, io))
            {
                ValidatePresentOrdinaryDirectory(directory, io);
            }
        }
    }

    private static void EnsureOutputDirectories(
        AtlasSnapshotSurveyLayout layout,
        AtlasIoSeams io)
    {
        string applicationRoot = Path.Combine(
            layout.RepositoryRoot,
            "src",
            "private",
            "app",
            "celesphonia-modifier");
        string[] directories =
        [
            Path.Combine(applicationRoot, ".private"),
            layout.PrivateParent,
            layout.WorkspaceRoot,
            layout.IncompleteRoot,
        ];
        foreach (string directory in directories)
        {
            ValidateContainedDirectory(applicationRoot, directory);
            if (!io.DirectoryExists(directory))
            {
                if (io.FileExists(directory))
                {
                    throw new AtlasSafetyException(
                        "A snapshot survey output component is invalid.");
                }

                io.CreateDirectory(directory);
            }

            ValidatePresentOrdinaryDirectory(directory, io);
        }

        static void ValidateContainedDirectory(string root, string candidate)
        {
            if (!AtlasSaveSnapshotContracts.ContainsPath(root, candidate))
            {
                throw new AtlasSafetyException(
                    "A snapshot survey output component escapes its root.");
            }
        }
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
        for (int offset = 0; offset < bytes.Length; offset += 64 * 1024)
        {
            cancellationToken.ThrowIfCancellationRequested();
            int count = Math.Min(64 * 1024, bytes.Length - offset);
            await stream.WriteAsync(
                    bytes.Slice(offset, count),
                    cancellationToken)
                .ConfigureAwait(false);
        }

        await stream.FlushAsync(cancellationToken).ConfigureAwait(false);
        cancellationToken.ThrowIfCancellationRequested();
        stream.Flush();
    }

    private static string HashMemory(
        ReadOnlySpan<byte> bytes,
        CancellationToken cancellationToken)
    {
        using IncrementalHash hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        for (int offset = 0; offset < bytes.Length; offset += 64 * 1024)
        {
            cancellationToken.ThrowIfCancellationRequested();
            int count = Math.Min(64 * 1024, bytes.Length - offset);
            hash.AppendData(bytes.Slice(offset, count));
        }

        cancellationToken.ThrowIfCancellationRequested();
        return Convert.ToHexStringLower(hash.GetHashAndReset());
    }

    private static string GetContainedChildPath(string root, string leaf)
    {
        if (!StringComparer.Ordinal.Equals(Path.GetFileName(leaf), leaf))
        {
            throw new AtlasSafetyException("A snapshot survey output name is invalid.");
        }

        string path = Path.GetFullPath(Path.Combine(root, leaf));
        if (!AtlasSaveSnapshotContracts.ContainsPath(root, path)
            || AtlasSaveSnapshotContracts.PathEquals(root, path))
        {
            throw new AtlasSafetyException("A snapshot survey output escapes its root.");
        }

        return path;
    }

    private static void ValidatePresentOrdinaryDirectory(string path, AtlasIoSeams io)
    {
        if (!io.DirectoryExists(path))
        {
            throw new AtlasSafetyException("A snapshot survey directory is missing.");
        }

        FileAttributes attributes = io.GetAttributes(path);
        if ((attributes & FileAttributes.Directory) == 0
            || (attributes & (FileAttributes.ReparsePoint | FileAttributes.Device)) != 0)
        {
            throw new AtlasSafetyException("A snapshot survey directory is invalid.");
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

    private static bool BytesEqual(
        ReadOnlySpan<byte> left,
        ReadOnlySpan<byte> right,
        CancellationToken cancellationToken)
    {
        if (left.Length != right.Length)
        {
            return false;
        }

        for (int offset = 0; offset < left.Length; offset += 4096)
        {
            cancellationToken.ThrowIfCancellationRequested();
            int count = Math.Min(4096, left.Length - offset);
            if (!left.Slice(offset, count).SequenceEqual(right.Slice(offset, count)))
            {
                return false;
            }
        }

        return true;
    }

    private static bool PathExists(string path, AtlasIoSeams io) =>
        io.FileExists(path) || io.DirectoryExists(path);

    private sealed record SourceDocument(AtlasSaveReadResult ReadResult);

    private sealed record GeneratedScan(long Length, string Sha256);

    private sealed record PersistedScan(
        long Length,
        string Sha256,
        AtlasStructuralScanCensus Census);
}
