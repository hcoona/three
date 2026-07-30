namespace Hcoona.CelesphoniaModifier.Atlas;

/// <summary>
/// Applies one released Gold mutation to one canonical Atlas save-slot file.
/// </summary>
/// <remarks>
/// This Windows-only API requires the game and every other save writer to be closed and assumes a
/// trusted local user. It accepts only a fully qualified, exact ordinal
/// <c>file1.rpgsave</c> through <c>file20.rpgsave</c> path.
///
/// A changed application first preserves <c>&lt;slot&gt;.celesphonia-original.bak</c>, using
/// <c>&lt;slot&gt;.celesphonia-original.bak.staging</c>, then replaces the slot from
/// <c>&lt;slot&gt;.celesphonia-stage.tmp</c>. Deleting the completed backup resets the baseline:
/// the next changed application archives the then-current slot and cannot recreate the older
/// deleted baseline. A proven replacement failure intentionally retains the fixed candidate stage
/// for an identical retry.
///
/// Cancellation is honored until the final pre-replacement boundary. Once replacement begins,
/// cancellation is ignored until the actual filesystem state is classified. The API performs no
/// rollback or automatic cleanup. Actual use requires a later persisted operation increment with
/// explicit confirmation of the exact canonical slot and Gold value; this library API grants no
/// private-operation authority.
/// </remarks>
public static class AtlasGoldFileApplication
{
    private const string BackupSuffix = ".celesphonia-original.bak";
    private const string BackupStagingSuffix = ".celesphonia-original.bak.staging";
    private const string CandidateStagingSuffix = ".celesphonia-stage.tmp";
    private const int CopyBufferSize = 8192;
    private const FileOptions ReadOptions =
        FileOptions.Asynchronous | FileOptions.SequentialScan;
    private const FileOptions WriteOptions =
        FileOptions.Asynchronous | FileOptions.WriteThrough;
    private const FileShare ReadShare = FileShare.Read | FileShare.Delete;

    /// <summary>
    /// Applies <paramref name="value"/> to one exact canonical Atlas save-slot path.
    /// </summary>
    /// <param name="slotPath">
    /// A fully qualified Windows path whose leaf is exactly <c>file1.rpgsave</c> through
    /// <c>file20.rpgsave</c>.
    /// </param>
    /// <param name="value">The Gold value passed to the released mutation kernel.</param>
    /// <param name="limits">The released save-reader limits used for every bounded read.</param>
    /// <param name="cancellationToken">
    /// Cancellation is observed before replacement starts and ignored from replacement invocation
    /// through outcome classification.
    /// </param>
    /// <returns>The classified application disposition.</returns>
    /// <exception cref="AtlasGoldFileApplicationException">
    /// The platform, path, fixed artifacts, source convergence, replacement outcome, or
    /// post-replacement verification failed as described by
    /// <see cref="AtlasGoldFileApplicationException.Failure"/>.
    /// </exception>
    /// <remarks>
    /// The game and other save writers must be closed. The operation is archive-first and uses only
    /// the three fixed adjacent artifacts documented on <see cref="AtlasGoldFileApplication"/>.
    /// Deleting the completed backup intentionally resets the baseline. Proven replacement failure
    /// retains the candidate stage. No rollback or automatic cleanup is performed, and calling this
    /// API does not by itself establish authority to operate on private data.
    /// </remarks>
    public static ValueTask<AtlasGoldFileApplicationDisposition> ApplyAsync(
        string slotPath,
        long value,
        AtlasSaveReaderLimits limits,
        CancellationToken cancellationToken = default) =>
        ApplyAsync(
            slotPath,
            value,
            limits,
            AtlasIoSeams.Default,
            OperatingSystem.IsWindows(),
            cancellationToken);

    internal static async ValueTask<AtlasGoldFileApplicationDisposition> ApplyAsync(
        string slotPath,
        long value,
        AtlasSaveReaderLimits limits,
        AtlasIoSeams io,
        bool isWindows,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(slotPath);
        ArgumentNullException.ThrowIfNull(limits);
        ArgumentNullException.ThrowIfNull(io);
        limits.Validate();
        cancellationToken.ThrowIfCancellationRequested();

        if (!isWindows)
        {
            throw CreateException(AtlasGoldFileApplicationFailure.UnsupportedPlatform);
        }

        ValidateSlotPathSyntax(slotPath);
        AtlasDiscovery.ValidateExistingOrdinaryFile(slotPath, io);

        await using Stream sourceStream = io.OpenFile(
            slotPath,
            FileMode.Open,
            FileAccess.Read,
            ReadShare,
            ReadOptions);
        AtlasSaveReadResult source = await AtlasSaveReader.ReadAsync(
                sourceStream,
                limits,
                cancellationToken)
            .ConfigureAwait(false);
        byte[] initialSourceBytes = source.GetSemanticNoOpBytes();
        cancellationToken.ThrowIfCancellationRequested();

        AtlasGoldMutationResult mutation = AtlasGoldMutationKernel.CreateCandidate(
            source,
            value,
            limits,
            cancellationToken);
        if (mutation.Disposition == AtlasGoldMutationDisposition.Unchanged)
        {
            return AtlasGoldFileApplicationDisposition.Unchanged;
        }

        byte[] candidateBytes = mutation.GetCompressedBytes(cancellationToken);
        ArtifactPaths paths = new(slotPath);
        BackupSnapshot backup = await EnsureBackupAsync(
                paths,
                initialSourceBytes,
                limits,
                io,
                cancellationToken)
            .ConfigureAwait(false);
        await EnsureCandidateStageAsync(
                paths.CandidateStagePath,
                candidateBytes,
                limits,
                io,
                cancellationToken)
            .ConfigureAwait(false);

        await EnsureSourceUnchangedAsync(
                slotPath,
                initialSourceBytes,
                limits,
                io,
                cancellationToken)
            .ConfigureAwait(false);
        EnsureAbsent(
            paths.BackupStagingPath,
            io,
            AtlasGoldFileApplicationFailure.BackupConflict);
        await EnsureExactArtifactAsync(
                paths.BackupPath,
                backup.Bytes,
                limits,
                io,
                AtlasGoldFileApplicationFailure.BackupConflict,
                cancellationToken)
            .ConfigureAwait(false);
        await EnsureExactArtifactAsync(
                paths.CandidateStagePath,
                candidateBytes,
                limits,
                io,
                AtlasGoldFileApplicationFailure.StagingConflict,
                cancellationToken)
            .ConfigureAwait(false);
        cancellationToken.ThrowIfCancellationRequested();

        try
        {
            io.ReplaceFile(paths.CandidateStagePath, slotPath, null);
        }
        catch (IOException exception)
        {
            return await ClassifyThrownReplacementAsync(
                    paths,
                    initialSourceBytes,
                    candidateBytes,
                    backup,
                    limits,
                    io,
                    exception)
                .ConfigureAwait(false);
        }
        catch (UnauthorizedAccessException exception)
        {
            return await ClassifyThrownReplacementAsync(
                    paths,
                    initialSourceBytes,
                    candidateBytes,
                    backup,
                    limits,
                    io,
                    exception)
                .ConfigureAwait(false);
        }

        PostReplacementState returnedState = await ObservePostReplacementStateAsync(
                paths,
                initialSourceBytes,
                candidateBytes,
                backup.Bytes,
                limits,
                io)
            .ConfigureAwait(false);
        if (!returnedState.IsEffectiveSuccess)
        {
            throw CreateException(
                AtlasGoldFileApplicationFailure.PostReplaceVerificationFailed,
                returnedState.Diagnostic);
        }

        return GetAppliedDisposition(backup.Created);
    }

    private static void ValidateSlotPathSyntax(string slotPath)
    {
        if (!Path.IsPathFullyQualified(slotPath))
        {
            throw CreateException(AtlasGoldFileApplicationFailure.UnsupportedSlotPath);
        }

        string fullPath;
        try
        {
            fullPath = Path.GetFullPath(slotPath);
        }
        catch (Exception exception) when (
            exception is ArgumentException
            or NotSupportedException
            or PathTooLongException)
        {
            throw CreateException(
                AtlasGoldFileApplicationFailure.UnsupportedSlotPath,
                exception);
        }

        string leaf = Path.GetFileName(slotPath);
        if (!StringComparer.Ordinal.Equals(fullPath, slotPath)
            || !AtlasSaveSnapshot.TryGetCanonicalName(
                leaf,
                out string canonicalName,
                out int order)
            || order is < 2 or > 21
            || !StringComparer.Ordinal.Equals(canonicalName, leaf))
        {
            throw CreateException(AtlasGoldFileApplicationFailure.UnsupportedSlotPath);
        }
    }

    private static async ValueTask<BackupSnapshot> EnsureBackupAsync(
        ArtifactPaths paths,
        byte[] initialSourceBytes,
        AtlasSaveReaderLimits limits,
        AtlasIoSeams io,
        CancellationToken cancellationToken)
    {
        bool backupAbsent = IsAbsent(paths.BackupPath, io);
        bool stagingAbsent = IsAbsent(paths.BackupStagingPath, io);
        if (!stagingAbsent)
        {
            throw CreateException(AtlasGoldFileApplicationFailure.BackupConflict);
        }

        if (!backupAbsent)
        {
            byte[] existingBackup = await ReadExistingBackupAsync(
                    paths.BackupPath,
                    limits,
                    io,
                    cancellationToken)
                .ConfigureAwait(false);
            return new BackupSnapshot(existingBackup, Created: false);
        }

        await CreateArtifactAsync(
                paths.BackupStagingPath,
                initialSourceBytes,
                io,
                AtlasGoldFileApplicationFailure.BackupConflict,
                cancellationToken)
            .ConfigureAwait(false);
        await EnsureExactArtifactAsync(
                paths.BackupStagingPath,
                initialSourceBytes,
                limits,
                io,
                AtlasGoldFileApplicationFailure.BackupConflict,
                cancellationToken)
            .ConfigureAwait(false);
        if (!IsAbsent(paths.BackupPath, io))
        {
            throw CreateException(AtlasGoldFileApplicationFailure.BackupConflict);
        }

        cancellationToken.ThrowIfCancellationRequested();
        try
        {
            io.MoveFile(paths.BackupStagingPath, paths.BackupPath);
        }
        catch (IOException exception) when (IsPresentAfterCollision(paths.BackupPath, io))
        {
            throw CreateException(
                AtlasGoldFileApplicationFailure.BackupConflict,
                exception);
        }

        await EnsureExactArtifactAsync(
                paths.BackupPath,
                initialSourceBytes,
                limits,
                io,
                AtlasGoldFileApplicationFailure.BackupConflict,
                cancellationToken)
            .ConfigureAwait(false);
        return new BackupSnapshot(initialSourceBytes.ToArray(), Created: true);
    }

    private static async ValueTask<byte[]> ReadExistingBackupAsync(
        string backupPath,
        AtlasSaveReaderLimits limits,
        AtlasIoSeams io,
        CancellationToken cancellationToken)
    {
        try
        {
            AtlasDiscovery.ValidateExistingOrdinaryFile(backupPath, io);
        }
        catch (AtlasSafetyException)
        {
            throw CreateException(AtlasGoldFileApplicationFailure.BackupConflict);
        }

        try
        {
            await using Stream stream = io.OpenFile(
                backupPath,
                FileMode.Open,
                FileAccess.Read,
                ReadShare,
                ReadOptions);
            AtlasSaveReadResult parsed = await AtlasSaveReader.ReadAsync(
                    stream,
                    limits,
                    cancellationToken)
                .ConfigureAwait(false);
            AtlasGoldInspectionResult inspection = AtlasGoldReadModel.Inspect(
                parsed,
                cancellationToken);
            if (AtlasGoldMutationKernel.ClassifySource(inspection, out _) is not null)
            {
                throw CreateException(AtlasGoldFileApplicationFailure.BackupConflict);
            }

            return parsed.GetSemanticNoOpBytes();
        }
        catch (AtlasSaveReadException)
        {
            throw CreateException(AtlasGoldFileApplicationFailure.BackupConflict);
        }
        catch (FileNotFoundException exception)
        {
            throw CreateException(
                AtlasGoldFileApplicationFailure.BackupConflict,
                exception);
        }
        catch (DirectoryNotFoundException exception)
        {
            throw CreateException(
                AtlasGoldFileApplicationFailure.BackupConflict,
                exception);
        }
    }

    private static async ValueTask EnsureCandidateStageAsync(
        string candidateStagePath,
        byte[] candidateBytes,
        AtlasSaveReaderLimits limits,
        AtlasIoSeams io,
        CancellationToken cancellationToken)
    {
        if (IsAbsent(candidateStagePath, io))
        {
            await CreateArtifactAsync(
                    candidateStagePath,
                    candidateBytes,
                    io,
                    AtlasGoldFileApplicationFailure.StagingConflict,
                    cancellationToken)
                .ConfigureAwait(false);
        }

        await EnsureExactArtifactAsync(
                candidateStagePath,
                candidateBytes,
                limits,
                io,
                AtlasGoldFileApplicationFailure.StagingConflict,
                cancellationToken)
            .ConfigureAwait(false);
    }

    private static async ValueTask CreateArtifactAsync(
        string path,
        byte[] bytes,
        AtlasIoSeams io,
        AtlasGoldFileApplicationFailure collisionFailure,
        CancellationToken cancellationToken)
    {
        Stream stream;
        try
        {
            stream = io.OpenFile(
                path,
                FileMode.CreateNew,
                FileAccess.Write,
                FileShare.None,
                WriteOptions);
        }
        catch (IOException exception) when (IsPresentAfterCollision(path, io))
        {
            throw CreateException(collisionFailure, exception);
        }

        await using (stream)
        {
            await stream.WriteAsync(bytes, cancellationToken).ConfigureAwait(false);
            await AtlasDiscovery.FlushAsync(stream, cancellationToken).ConfigureAwait(false);
        }
    }

    private static async ValueTask EnsureSourceUnchangedAsync(
        string slotPath,
        byte[] initialSourceBytes,
        AtlasSaveReaderLimits limits,
        AtlasIoSeams io,
        CancellationToken cancellationToken)
    {
        byte[] currentBytes;
        try
        {
            currentBytes = await ReadOrdinaryBytesAsync(
                    slotPath,
                    limits,
                    io,
                    cancellationToken)
                .ConfigureAwait(false);
        }
        catch (Exception exception) when (
            exception is AtlasSafetyException
            or AtlasSaveReadException
            or IOException
            or UnauthorizedAccessException
            or NotSupportedException)
        {
            throw CreateException(
                AtlasGoldFileApplicationFailure.SourceChanged,
                exception);
        }

        if (!currentBytes.AsSpan().SequenceEqual(initialSourceBytes))
        {
            throw CreateException(AtlasGoldFileApplicationFailure.SourceChanged);
        }
    }

    private static async ValueTask EnsureExactArtifactAsync(
        string path,
        byte[] expectedBytes,
        AtlasSaveReaderLimits limits,
        AtlasIoSeams io,
        AtlasGoldFileApplicationFailure failure,
        CancellationToken cancellationToken)
    {
        byte[] actualBytes;
        try
        {
            actualBytes = await ReadOrdinaryBytesAsync(
                    path,
                    limits,
                    io,
                    cancellationToken)
                .ConfigureAwait(false);
        }
        catch (AtlasSafetyException)
        {
            throw CreateException(failure);
        }
        catch (AtlasSaveReadException)
        {
            throw CreateException(failure);
        }
        catch (FileNotFoundException exception)
        {
            throw CreateException(failure, exception);
        }
        catch (DirectoryNotFoundException exception)
        {
            throw CreateException(failure, exception);
        }

        if (!actualBytes.AsSpan().SequenceEqual(expectedBytes))
        {
            throw CreateException(failure);
        }
    }

    private static async ValueTask<byte[]> ReadOrdinaryBytesAsync(
        string path,
        AtlasSaveReaderLimits limits,
        AtlasIoSeams io,
        CancellationToken cancellationToken)
    {
        AtlasDiscovery.ValidateExistingOrdinaryFile(path, io);
        await using Stream stream = io.OpenFile(
            path,
            FileMode.Open,
            FileAccess.Read,
            ReadShare,
            ReadOptions);
        return await ReadBoundedBytesAsync(
                stream,
                limits.MaximumEncodedBytes,
                cancellationToken)
            .ConfigureAwait(false);
    }

    private static async ValueTask<byte[]> ReadBoundedBytesAsync(
        Stream source,
        int maximumBytes,
        CancellationToken cancellationToken)
    {
        if (!source.CanRead)
        {
            throw new NotSupportedException("The source stream does not support reading.");
        }

        using MemoryStream bytes = new(Math.Min(maximumBytes, 64 * 1024));
        byte[] buffer = new byte[CopyBufferSize];
        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();
            int read = await source.ReadAsync(buffer, cancellationToken).ConfigureAwait(false);
            if (read == 0)
            {
                break;
            }

            if (bytes.Length > maximumBytes - read)
            {
                throw new AtlasSaveReadException(AtlasSaveReadFailure.EncodedInputLimit);
            }

            bytes.Write(buffer, 0, read);
        }

        return bytes.ToArray();
    }

    private static void EnsureAbsent(
        string path,
        AtlasIoSeams io,
        AtlasGoldFileApplicationFailure failure)
    {
        if (!IsAbsent(path, io))
        {
            throw CreateException(failure);
        }
    }

    private static bool IsAbsent(string path, AtlasIoSeams io)
    {
        try
        {
            _ = io.GetAttributes(path);
            return false;
        }
        catch (FileNotFoundException)
        {
            return true;
        }
        catch (DirectoryNotFoundException)
        {
            return true;
        }
    }

    private static bool IsPresentAfterCollision(string path, AtlasIoSeams io)
    {
        try
        {
            return !IsAbsent(path, io);
        }
        catch (Exception exception) when (
            exception is IOException
            or UnauthorizedAccessException
            or NotSupportedException)
        {
            return false;
        }
    }

    private static async ValueTask<AtlasGoldFileApplicationDisposition>
        ClassifyThrownReplacementAsync(
            ArtifactPaths paths,
            byte[] initialSourceBytes,
            byte[] candidateBytes,
            BackupSnapshot backup,
            AtlasSaveReaderLimits limits,
            AtlasIoSeams io,
            Exception replacementException)
    {
        PostReplacementState state = await ObservePostReplacementStateAsync(
                paths,
                initialSourceBytes,
                candidateBytes,
                backup.Bytes,
                limits,
                io)
            .ConfigureAwait(false);
        if (state.IsEffectiveSuccess)
        {
            return GetAppliedDisposition(backup.Created);
        }

        if (state.IsProvenFailure)
        {
            throw CreateException(
                AtlasGoldFileApplicationFailure.ReplacementFailed,
                replacementException);
        }

        throw CreateException(
            AtlasGoldFileApplicationFailure.ReplacementOutcomeUnknown,
            replacementException);
    }

    private static async ValueTask<PostReplacementState> ObservePostReplacementStateAsync(
        ArtifactPaths paths,
        byte[] initialSourceBytes,
        byte[] candidateBytes,
        byte[] backupBytes,
        AtlasSaveReaderLimits limits,
        AtlasIoSeams io)
    {
        FileObservation live = await ObserveFileAsync(
                paths.SlotPath,
                limits,
                io)
            .ConfigureAwait(false);
        FileObservation stage = await ObserveFileAsync(
                paths.CandidateStagePath,
                limits,
                io)
            .ConfigureAwait(false);
        FileObservation backup = await ObserveFileAsync(
                paths.BackupPath,
                limits,
                io)
            .ConfigureAwait(false);

        bool backupExact = backup.HasExactBytes(backupBytes);
        return new PostReplacementState(
            IsEffectiveSuccess:
                live.HasExactBytes(candidateBytes)
                && stage.Kind == FileObservationKind.Absent
                && backupExact,
            IsProvenFailure:
                live.HasExactBytes(initialSourceBytes)
                && stage.HasExactBytes(candidateBytes)
                && backupExact,
            Diagnostic: live.Diagnostic ?? stage.Diagnostic ?? backup.Diagnostic);
    }

    private static async ValueTask<FileObservation> ObserveFileAsync(
        string path,
        AtlasSaveReaderLimits limits,
        AtlasIoSeams io)
    {
        try
        {
            if (IsAbsent(path, io))
            {
                return FileObservation.Absent;
            }

            byte[] bytes = await ReadOrdinaryBytesAsync(
                    path,
                    limits,
                    io,
                    CancellationToken.None)
                .ConfigureAwait(false);
            return FileObservation.Present(bytes);
        }
        catch (Exception exception)
        {
            return FileObservation.Unreadable(exception);
        }
    }

    private static AtlasGoldFileApplicationDisposition GetAppliedDisposition(bool backupCreated) =>
        backupCreated
            ? AtlasGoldFileApplicationDisposition.AppliedWithBackupCreated
            : AtlasGoldFileApplicationDisposition.AppliedWithBackupPreserved;

    private static AtlasGoldFileApplicationException CreateException(
        AtlasGoldFileApplicationFailure failure,
        Exception? innerException = null) =>
        new(failure, innerException);

    private readonly record struct ArtifactPaths(string SlotPath)
    {
        public string BackupPath { get; } = SlotPath + BackupSuffix;

        public string BackupStagingPath { get; } = SlotPath + BackupStagingSuffix;

        public string CandidateStagePath { get; } = SlotPath + CandidateStagingSuffix;
    }

    private readonly record struct BackupSnapshot(byte[] Bytes, bool Created);

    private readonly record struct PostReplacementState(
        bool IsEffectiveSuccess,
        bool IsProvenFailure,
        Exception? Diagnostic);

    private enum FileObservationKind
    {
        Absent,
        Present,
        Unreadable,
    }

    private sealed class FileObservation
    {
        private FileObservation(
            FileObservationKind kind,
            byte[]? bytes,
            Exception? diagnostic)
        {
            Kind = kind;
            Bytes = bytes;
            Diagnostic = diagnostic;
        }

        public static FileObservation Absent { get; } =
            new(FileObservationKind.Absent, null, null);

        public FileObservationKind Kind { get; }

        public byte[]? Bytes { get; }

        public Exception? Diagnostic { get; }

        public static FileObservation Present(byte[] bytes) =>
            new(FileObservationKind.Present, bytes, null);

        public static FileObservation Unreadable(Exception diagnostic) =>
            new(FileObservationKind.Unreadable, null, diagnostic);

        public bool HasExactBytes(byte[] expected) =>
            Kind == FileObservationKind.Present
            && Bytes is not null
            && Bytes.AsSpan().SequenceEqual(expected);
    }
}

public enum AtlasGoldFileApplicationDisposition
{
    Unchanged,
    AppliedWithBackupCreated,
    AppliedWithBackupPreserved,
}

public enum AtlasGoldFileApplicationFailure
{
    UnsupportedPlatform,
    UnsupportedSlotPath,
    BackupConflict,
    StagingConflict,
    SourceChanged,
    ReplacementFailed,
    ReplacementOutcomeUnknown,
    PostReplaceVerificationFailed,
}

public sealed class AtlasGoldFileApplicationException : Exception
{
    internal AtlasGoldFileApplicationException(
        AtlasGoldFileApplicationFailure failure,
        Exception? innerException = null)
        : base(GetMessage(failure), innerException)
    {
        Failure = failure;
    }

    public AtlasGoldFileApplicationFailure Failure { get; }

    private static string GetMessage(AtlasGoldFileApplicationFailure failure) =>
        failure switch
        {
            AtlasGoldFileApplicationFailure.UnsupportedPlatform =>
                "Gold file application is supported only on Windows.",
            AtlasGoldFileApplicationFailure.UnsupportedSlotPath =>
                "The slot path is not a supported canonical save-slot path.",
            AtlasGoldFileApplicationFailure.BackupConflict =>
                "The fixed backup artifacts conflict with this operation.",
            AtlasGoldFileApplicationFailure.StagingConflict =>
                "The fixed candidate stage conflicts with this operation.",
            AtlasGoldFileApplicationFailure.SourceChanged =>
                "The source slot changed before replacement.",
            AtlasGoldFileApplicationFailure.ReplacementFailed =>
                "The source slot replacement failed without changing the classified files.",
            AtlasGoldFileApplicationFailure.ReplacementOutcomeUnknown =>
                "The source slot replacement outcome is unknown.",
            AtlasGoldFileApplicationFailure.PostReplaceVerificationFailed =>
                "The replaced slot failed verification.",
            _ => "The Gold file application reached an unsupported internal state.",
        };
}
