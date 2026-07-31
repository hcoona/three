using Hcoona.CelesphoniaModifier.Atlas;

namespace Hcoona.CelesphoniaModifier.WinUI;

internal interface IGoldEditorOperations
{
    ValueTask<GoldEditorDocument> LoadAsync(
        string slotPath,
        CancellationToken cancellationToken);

    ValueTask<GoldEditorApplyOutcome> ApplyAsync(
        GoldEditorDocument document,
        long requestedGold,
        CancellationToken cancellationToken);
}

internal enum GoldEditorLoadFailure
{
    UnsupportedSlotPath,
    MissingOrInaccessibleFile,
    UnsupportedOrMalformedSave,
    InconsistentGoldLocations,
    ReadLimitExceeded,
    UnexpectedLocalFailure,
}

internal sealed class GoldEditorLoadException : Exception
{
    internal GoldEditorLoadException(
        GoldEditorLoadFailure failure,
        Exception? innerException = null)
        : base(GetMessage(failure), innerException)
    {
        Failure = failure;
    }

    internal GoldEditorLoadFailure Failure { get; }

    private static string GetMessage(GoldEditorLoadFailure failure)
    {
        return failure switch
        {
            GoldEditorLoadFailure.UnsupportedSlotPath =>
                "The selected path is not a supported canonical save slot.",
            GoldEditorLoadFailure.MissingOrInaccessibleFile =>
                "The selected save slot is missing or inaccessible.",
            GoldEditorLoadFailure.UnsupportedOrMalformedSave =>
                "The selected file is not a supported, well-formed save.",
            GoldEditorLoadFailure.InconsistentGoldLocations =>
                "The save has inconsistent Gold locations.",
            GoldEditorLoadFailure.ReadLimitExceeded =>
                "The save exceeds the released Atlas reader limits.",
            _ => "The save could not be loaded because of an unexpected local failure.",
        };
    }
}

internal sealed class GoldEditorDocument
{
    private readonly byte[] _baselineBytes;

    internal GoldEditorDocument(string slotPath, long currentGold, ReadOnlySpan<byte> baselineBytes)
    {
        if (!GoldEditorOperations.IsCanonicalSlotPath(slotPath))
        {
            throw new ArgumentException(
                "The document requires a canonical save-slot path.",
                nameof(slotPath));
        }

        SlotPath = slotPath;
        CurrentGold = currentGold;
        _baselineBytes = baselineBytes.ToArray();
    }

    internal string SlotPath { get; }

    internal long CurrentGold { get; }

    internal string BackupPath => SlotPath + ".celesphonia-original.bak";

    internal string BackupStagingPath => BackupPath + ".staging";

    internal string CandidateStagePath => SlotPath + ".celesphonia-stage.tmp";

    internal bool HasExactBaseline(ReadOnlySpan<byte> bytes)
    {
        return bytes.SequenceEqual(_baselineBytes);
    }

    internal byte[] CopyBaseline()
    {
        return _baselineBytes.ToArray();
    }
}

internal enum GoldEditorApplyOutcomeKind
{
    Unchanged,
    AppliedWithBackupCreated,
    AppliedWithBackupPreserved,
    PreviewChanged,
    PreviewReadFailed,
    ReloadFailed,
    ApplicationFailed,
}

internal sealed class GoldEditorApplyOutcome
{
    private GoldEditorApplyOutcome(
        GoldEditorApplyOutcomeKind kind,
        AtlasGoldFileApplicationDisposition? disposition = null,
        GoldEditorDocument? reloadedDocument = null,
        GoldEditorLoadException? previewReadException = null,
        AtlasGoldFileApplicationException? applicationException = null,
        Exception? reloadException = null)
    {
        Kind = kind;
        Disposition = disposition;
        ReloadedDocument = reloadedDocument;
        PreviewReadException = previewReadException;
        ApplicationException = applicationException;
        ReloadException = reloadException;
    }

    internal GoldEditorApplyOutcomeKind Kind { get; }

    internal AtlasGoldFileApplicationDisposition? Disposition { get; }

    internal GoldEditorDocument? ReloadedDocument { get; }

    internal GoldEditorLoadException? PreviewReadException { get; }

    internal AtlasGoldFileApplicationException? ApplicationException { get; }

    internal Exception? ReloadException { get; }

    internal bool WasWriteReported =>
        Disposition is AtlasGoldFileApplicationDisposition.AppliedWithBackupCreated
            or AtlasGoldFileApplicationDisposition.AppliedWithBackupPreserved;

    internal static GoldEditorApplyOutcome Successful(
        AtlasGoldFileApplicationDisposition disposition,
        GoldEditorDocument reloadedDocument)
    {
        ArgumentNullException.ThrowIfNull(reloadedDocument);
        return new GoldEditorApplyOutcome(
            MapDisposition(disposition),
            disposition,
            reloadedDocument);
    }

    internal static GoldEditorApplyOutcome PreviewChanged()
    {
        return new GoldEditorApplyOutcome(GoldEditorApplyOutcomeKind.PreviewChanged);
    }

    internal static GoldEditorApplyOutcome PreviewReadFailed(
        GoldEditorLoadException exception)
    {
        ArgumentNullException.ThrowIfNull(exception);
        return new GoldEditorApplyOutcome(
            GoldEditorApplyOutcomeKind.PreviewReadFailed,
            previewReadException: exception);
    }

    internal static GoldEditorApplyOutcome ReloadFailed(
        AtlasGoldFileApplicationDisposition disposition,
        Exception exception)
    {
        ArgumentNullException.ThrowIfNull(exception);
        return new GoldEditorApplyOutcome(
            GoldEditorApplyOutcomeKind.ReloadFailed,
            disposition,
            reloadException: exception);
    }

    internal static GoldEditorApplyOutcome ApplicationFailed(
        AtlasGoldFileApplicationException exception)
    {
        ArgumentNullException.ThrowIfNull(exception);
        return new GoldEditorApplyOutcome(
            GoldEditorApplyOutcomeKind.ApplicationFailed,
            applicationException: exception);
    }

    private static GoldEditorApplyOutcomeKind MapDisposition(
        AtlasGoldFileApplicationDisposition disposition)
    {
        return disposition switch
        {
            AtlasGoldFileApplicationDisposition.Unchanged =>
                GoldEditorApplyOutcomeKind.Unchanged,
            AtlasGoldFileApplicationDisposition.AppliedWithBackupCreated =>
                GoldEditorApplyOutcomeKind.AppliedWithBackupCreated,
            AtlasGoldFileApplicationDisposition.AppliedWithBackupPreserved =>
                GoldEditorApplyOutcomeKind.AppliedWithBackupPreserved,
            _ => throw new ArgumentOutOfRangeException(nameof(disposition)),
        };
    }
}

internal delegate Stream GoldEditorReadStreamFactory(string path);

internal delegate ValueTask<AtlasGoldFileApplicationDisposition> GoldEditorApplyInvoker(
    string slotPath,
    long requestedGold,
    AtlasSaveReaderLimits limits,
    CancellationToken cancellationToken);

internal sealed class GoldEditorOperations : IGoldEditorOperations
{
    private const int FileBufferSize = 8192;
    private const FileOptions ReadOptions =
        FileOptions.Asynchronous | FileOptions.SequentialScan;
    private const FileShare ReadShare = FileShare.Read | FileShare.Delete;
    private readonly GoldEditorApplyInvoker _applyInvoker;
    private readonly GoldEditorReadStreamFactory _readStreamFactory;

    internal GoldEditorOperations(
        GoldEditorReadStreamFactory? readStreamFactory = null,
        GoldEditorApplyInvoker? applyInvoker = null)
    {
        _readStreamFactory = readStreamFactory ?? OpenReadStream;
        _applyInvoker = applyInvoker ?? AtlasGoldFileApplication.ApplyAsync;
    }

    public async ValueTask<GoldEditorDocument> LoadAsync(
        string slotPath,
        CancellationToken cancellationToken)
    {
        ValidateSlotPath(slotPath);
        cancellationToken.ThrowIfCancellationRequested();

        AtlasSaveReadResult source;
        try
        {
            await using Stream stream = _readStreamFactory(slotPath);
            source = await AtlasSaveReader.ReadAsync(
                    stream,
                    AtlasSaveReaderLimits.Default,
                    cancellationToken)
                .ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (AtlasSaveReadException exception) when (IsLimitFailure(exception.Failure))
        {
            throw new GoldEditorLoadException(
                GoldEditorLoadFailure.ReadLimitExceeded,
                exception);
        }
        catch (AtlasSaveReadException exception)
        {
            throw new GoldEditorLoadException(
                GoldEditorLoadFailure.UnsupportedOrMalformedSave,
                exception);
        }
        catch (Exception exception) when (
            exception is FileNotFoundException
            or DirectoryNotFoundException
            or UnauthorizedAccessException
            or IOException)
        {
            throw new GoldEditorLoadException(
                GoldEditorLoadFailure.MissingOrInaccessibleFile,
                exception);
        }
        catch (Exception exception)
        {
            throw new GoldEditorLoadException(
                GoldEditorLoadFailure.UnexpectedLocalFailure,
                exception);
        }

        AtlasGoldReadModelResult gold;
        try
        {
            gold = AtlasGoldReadModel.Read(source, cancellationToken);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception exception)
        {
            throw new GoldEditorLoadException(
                GoldEditorLoadFailure.UnexpectedLocalFailure,
                exception);
        }

        if (gold.Aggregate == AtlasGoldAggregateState.Disagree)
        {
            throw new GoldEditorLoadException(
                GoldEditorLoadFailure.InconsistentGoldLocations);
        }

        if (gold.Aggregate != AtlasGoldAggregateState.Consistent
            || gold.PartyGold.State != AtlasGoldCandidateState.Present
            || gold.VariableGold.State != AtlasGoldCandidateState.Present
            || gold.PartyGold.Value is not long partyGold
            || gold.VariableGold.Value is not long variableGold
            || partyGold != variableGold)
        {
            throw new GoldEditorLoadException(
                GoldEditorLoadFailure.UnsupportedOrMalformedSave);
        }

        return new GoldEditorDocument(
            slotPath,
            partyGold,
            source.OriginalCompressedBytes.Span);
    }

    public async ValueTask<GoldEditorApplyOutcome> ApplyAsync(
        GoldEditorDocument document,
        long requestedGold,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(document);
        cancellationToken.ThrowIfCancellationRequested();

        AtlasGoldFileApplicationDisposition disposition;
        try
        {
            await using Stream convergenceStream = _readStreamFactory(document.SlotPath);
            AtlasSaveReadResult convergedSource;
            try
            {
                convergedSource = await AtlasSaveReader.ReadAsync(
                        convergenceStream,
                        AtlasSaveReaderLimits.Default,
                        cancellationToken)
                    .ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (AtlasSaveReadException exception) when (IsLimitFailure(exception.Failure))
            {
                return GoldEditorApplyOutcome.PreviewReadFailed(
                    new GoldEditorLoadException(
                        GoldEditorLoadFailure.ReadLimitExceeded,
                        exception));
            }
            catch (AtlasSaveReadException exception)
            {
                return GoldEditorApplyOutcome.PreviewReadFailed(
                    new GoldEditorLoadException(
                        GoldEditorLoadFailure.UnsupportedOrMalformedSave,
                        exception));
            }
            catch (Exception exception) when (
                exception is FileNotFoundException
                or DirectoryNotFoundException
                or UnauthorizedAccessException
                or IOException)
            {
                return GoldEditorApplyOutcome.PreviewReadFailed(
                    new GoldEditorLoadException(
                        GoldEditorLoadFailure.MissingOrInaccessibleFile,
                        exception));
            }
            catch (Exception exception)
            {
                return GoldEditorApplyOutcome.PreviewReadFailed(
                    new GoldEditorLoadException(
                        GoldEditorLoadFailure.UnexpectedLocalFailure,
                        exception));
            }

            if (!document.HasExactBaseline(convergedSource.OriginalCompressedBytes.Span))
            {
                return GoldEditorApplyOutcome.PreviewChanged();
            }

            try
            {
                disposition = await _applyInvoker(
                        document.SlotPath,
                        requestedGold,
                        AtlasSaveReaderLimits.Default,
                        cancellationToken)
                    .ConfigureAwait(false);
            }
            catch (AtlasGoldFileApplicationException exception)
            {
                return GoldEditorApplyOutcome.ApplicationFailed(exception);
            }
        }
        catch (OperationCanceledException)
        {
            throw;
        }

        try
        {
            GoldEditorDocument reloaded = await LoadAsync(
                    document.SlotPath,
                    CancellationToken.None)
                .ConfigureAwait(false);
            return GoldEditorApplyOutcome.Successful(disposition, reloaded);
        }
        catch (Exception exception)
        {
            return GoldEditorApplyOutcome.ReloadFailed(disposition, exception);
        }
    }

    internal static bool IsCanonicalSlotPath(string? slotPath)
    {
        if (string.IsNullOrWhiteSpace(slotPath) || !Path.IsPathFullyQualified(slotPath))
        {
            return false;
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
            return false;
        }

        if (!StringComparer.Ordinal.Equals(fullPath, slotPath))
        {
            return false;
        }

        string leaf = Path.GetFileName(slotPath);
        if (!leaf.StartsWith("file", StringComparison.Ordinal)
            || !leaf.EndsWith(".rpgsave", StringComparison.Ordinal))
        {
            return false;
        }

        ReadOnlySpan<char> indexText = leaf.AsSpan(4, leaf.Length - 12);
        if (indexText.Length is < 1 or > 2
            || indexText.Length == 2 && indexText[0] == '0')
        {
            return false;
        }

        int index = 0;
        foreach (char character in indexText)
        {
            if (character is < '0' or > '9')
            {
                return false;
            }

            index = checked((index * 10) + character - '0');
        }

        return index is >= 1 and <= 20;
    }

    private static FileStream OpenReadStream(string path)
    {
        return new FileStream(
            path,
            new FileStreamOptions
            {
                Mode = FileMode.Open,
                Access = FileAccess.Read,
                Share = ReadShare,
                Options = ReadOptions,
                BufferSize = FileBufferSize,
            });
    }

    private static void ValidateSlotPath(string? slotPath)
    {
        if (!IsCanonicalSlotPath(slotPath))
        {
            throw new GoldEditorLoadException(GoldEditorLoadFailure.UnsupportedSlotPath);
        }
    }

    private static bool IsLimitFailure(AtlasSaveReadFailure failure)
    {
        return failure is AtlasSaveReadFailure.EncodedInputLimit
            or AtlasSaveReadFailure.DecompressedSizeLimit
            or AtlasSaveReadFailure.JsonDepthLimit
            or AtlasSaveReadFailure.JsonTokenLimit
            or AtlasSaveReadFailure.ScalarSizeLimit
            or AtlasSaveReadFailure.GraphNodeLimit
            or AtlasSaveReadFailure.IdentityCountLimit
            or AtlasSaveReadFailure.ReferenceCountLimit;
    }
}
