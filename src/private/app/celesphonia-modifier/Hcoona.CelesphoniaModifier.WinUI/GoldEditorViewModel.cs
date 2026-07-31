using System.ComponentModel;
using System.Globalization;
using System.Runtime.CompilerServices;
using Hcoona.CelesphoniaModifier.Atlas;

namespace Hcoona.CelesphoniaModifier.WinUI;

internal enum GoldEditorState
{
    Empty,
    Ready,
    Busy,
    BlockedUntilReload,
    ResultReloadFailed,
}

internal enum GoldEditorActivity
{
    None,
    Loading,
    Applying,
}

internal enum GoldEditorResultSeverity
{
    Informational,
    Success,
    Warning,
    Error,
}

internal enum GoldEditorAnnouncement
{
    Polite,
    Assertive,
}

internal sealed record GoldEditorConfirmation(
    string SlotPath,
    long CurrentGold,
    long RequestedGold,
    string RequestedGoldText,
    string BackupPath);

internal sealed class GoldEditorViewModel : INotifyPropertyChanged, IDisposable
{
    private readonly IGoldEditorOperations _operations;
    private CancellationTokenSource? _operationCancellation;
    private GoldEditorActivity _activity;
    private bool _cancellationRequested;
    private GoldEditorDocument? _document;
    private bool _isResultOpen;
    private AtlasGoldFileApplicationFailure? _lastApplicationFailure;
    private AtlasGoldFileApplicationDisposition? _lastDisposition;
    private string _newGoldText = string.Empty;
    private string _resultMessage = string.Empty;
    private GoldEditorResultSeverity _resultSeverity;
    private string _resultTitle = string.Empty;
    private GoldEditorState _state = GoldEditorState.Empty;
    private string _statusText = "Select a save slot to begin.";
    private GoldEditorAnnouncement _resultAnnouncement = GoldEditorAnnouncement.Polite;
    private string _validationText = "Enter a whole-number Int64 value.";
    private bool _disposed;

    internal GoldEditorViewModel(IGoldEditorOperations operations)
    {
        ArgumentNullException.ThrowIfNull(operations);
        _operations = operations;
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    internal event EventHandler? OperationCompleted;

    internal GoldEditorState State => _state;

    internal GoldEditorActivity Activity => _activity;

    internal GoldEditorDocument? Document => _document;

    internal string SlotPath => _document?.SlotPath ?? string.Empty;

    internal string CurrentGoldText =>
        _document?.CurrentGold.ToString(CultureInfo.InvariantCulture) ?? "Not loaded";

    internal string NewGoldText
    {
        get => _newGoldText;
        set
        {
            if (StringComparer.Ordinal.Equals(_newGoldText, value))
            {
                return;
            }

            _newGoldText = value ?? string.Empty;
            ValidateNewGold();
            OnPropertyChanged();
            OnPropertyChanged(nameof(CanApply));
        }
    }

    internal string ValidationText => _validationText;

    internal bool IsBusy => _state == GoldEditorState.Busy;

    internal bool CanBrowse => !IsBusy;

    internal bool CanEdit => _state == GoldEditorState.Ready;

    internal bool CanApply =>
        _state == GoldEditorState.Ready
        && _document is not null
        && TryParseGold(_newGoldText, out _, out _);

    internal bool CanCancel => IsBusy && !_cancellationRequested;

    internal string StatusText => _statusText;

    internal bool IsResultOpen => _isResultOpen;

    internal string ResultTitle => _resultTitle;

    internal string ResultMessage => _resultMessage;

    internal GoldEditorResultSeverity ResultSeverity => _resultSeverity;

    internal GoldEditorAnnouncement ResultAnnouncement => _resultAnnouncement;

    internal AtlasGoldFileApplicationDisposition? LastDisposition => _lastDisposition;

    internal AtlasGoldFileApplicationFailure? LastApplicationFailure =>
        _lastApplicationFailure;

    internal bool ShouldRestoreApplyFocus { get; private set; }

    internal async Task LoadAsync(string slotPath)
    {
        if (IsBusy)
        {
            return;
        }

        GoldEditorDocument? previousDocument = _document;
        GoldEditorState previousState = _state;
        BeginOperation(GoldEditorActivity.Loading, "Loading the selected save slot...");

        CancellationTokenSource cancellation = _operationCancellation!;
        try
        {
            GoldEditorDocument loaded = await _operations.LoadAsync(
                slotPath,
                cancellation.Token);
            _document = loaded;
            _newGoldText = string.Empty;
            SetLastApplicationResult(null, null);
            SetState(GoldEditorState.Ready);
            SetStatus("Ready. Enter a new Gold value.");
            CloseResult();
            ValidateNewGold();
            RaiseDocumentProperties();
        }
        catch (OperationCanceledException)
        {
            RestoreDocument(previousDocument, previousState);
            SetResult(
                "Canceled",
                "Loading was canceled. The previous selection was preserved.",
                GoldEditorResultSeverity.Informational,
                GoldEditorAnnouncement.Polite);
            SetStatus("Loading canceled.");
            ShouldRestoreApplyFocus = _state == GoldEditorState.Ready;
        }
        catch (GoldEditorLoadException exception)
        {
            RestoreDocument(previousDocument, previousState);
            SetResult(
                GetLoadFailureTitle(exception.Failure),
                exception.Message,
                GoldEditorResultSeverity.Error,
                GoldEditorAnnouncement.Polite);
            SetStatus("The selected save slot was not loaded.");
        }
        catch (Exception)
        {
            RestoreDocument(previousDocument, previousState);
            SetResult(
                "Unexpected local failure",
                "The selected save slot could not be loaded because of an unexpected local "
                    + "failure.",
                GoldEditorResultSeverity.Error,
                GoldEditorAnnouncement.Assertive);
            SetStatus("The selected save slot was not loaded.");
        }
        finally
        {
            CompleteOperation(cancellation);
        }
    }

    internal void PreserveAfterPickerCancellation()
    {
        OnPropertyChanged(nameof(SlotPath));
        OnPropertyChanged(nameof(CurrentGoldText));
        OnPropertyChanged(nameof(NewGoldText));
    }

    internal void ReportPickerFailure()
    {
        SetResult(
            "File picker failed",
            "The file picker could not be opened. The current selection was preserved.",
            GoldEditorResultSeverity.Error,
            GoldEditorAnnouncement.Polite);
        SetStatus("The current selection was preserved.");
    }

    internal bool TryCreateConfirmation(out GoldEditorConfirmation? confirmation)
    {
        confirmation = null;
        if (!CanApply
            || _document is null
            || !TryParseGold(_newGoldText, out long requestedGold, out string normalized))
        {
            return false;
        }

        confirmation = new GoldEditorConfirmation(
            _document.SlotPath,
            _document.CurrentGold,
            requestedGold,
            normalized,
            _document.BackupPath);
        return true;
    }

    internal async Task ApplyConfirmedAsync(long requestedGold)
    {
        if (_state != GoldEditorState.Ready
            || _document is null
            || !TryParseGold(_newGoldText, out long parsedGold, out _)
            || parsedGold != requestedGold)
        {
            return;
        }

        GoldEditorDocument applyingDocument = _document;
        SetLastApplicationResult(null, null);
        BeginOperation(GoldEditorActivity.Applying, "Applying Gold and classifying the result...");

        CancellationTokenSource cancellation = _operationCancellation!;
        try
        {
            GoldEditorApplyOutcome outcome = await _operations.ApplyAsync(
                applyingDocument,
                requestedGold,
                cancellation.Token);
            HandleApplyOutcome(outcome);
        }
        catch (OperationCanceledException)
        {
            SetState(GoldEditorState.Ready);
            SetResult(
                "Canceled",
                "The operation was canceled before a classified write result was reported.",
                GoldEditorResultSeverity.Informational,
                GoldEditorAnnouncement.Polite);
            SetStatus("Operation canceled.");
            ShouldRestoreApplyFocus = true;
        }
        catch (Exception)
        {
            SetState(GoldEditorState.BlockedUntilReload);
            SetResult(
                "Unexpected local failure",
                "The operation ended with an unexpected local failure. Reload the slot before "
                    + "editing again.",
                GoldEditorResultSeverity.Error,
                GoldEditorAnnouncement.Assertive);
            SetStatus("Editing is blocked until the slot is reloaded.");
        }
        finally
        {
            CompleteOperation(cancellation);
        }
    }

    internal void RequestCancellation()
    {
        if (!IsBusy || _operationCancellation is null || _cancellationRequested)
        {
            return;
        }

        _cancellationRequested = true;
        _operationCancellation.Cancel();
        SetStatus("Cancellation requested. Waiting for a classified completion...");
        OnPropertyChanged(nameof(CanCancel));
    }

    internal static bool TryParseGold(
        string? text,
        out long value,
        out string normalized)
    {
        value = default;
        normalized = string.Empty;
        if (text is null)
        {
            return false;
        }

        ReadOnlySpan<char> trimmed = text.AsSpan().Trim();
        if (trimmed.IsEmpty)
        {
            return false;
        }

        int digitIndex = trimmed[0] is '+' or '-' ? 1 : 0;
        if (digitIndex == trimmed.Length)
        {
            return false;
        }

        for (int index = digitIndex; index < trimmed.Length; index++)
        {
            if (trimmed[index] is < '0' or > '9')
            {
                return false;
            }
        }

        if (!long.TryParse(
                trimmed,
                NumberStyles.AllowLeadingSign,
                CultureInfo.InvariantCulture,
                out value))
        {
            return false;
        }

        normalized = value.ToString(CultureInfo.InvariantCulture);
        return true;
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _operationCancellation?.Cancel();
        _operationCancellation?.Dispose();
        _operationCancellation = null;
    }

    private void HandleApplyOutcome(GoldEditorApplyOutcome outcome)
    {
        switch (outcome.Kind)
        {
            case GoldEditorApplyOutcomeKind.Unchanged:
            case GoldEditorApplyOutcomeKind.AppliedWithBackupCreated:
            case GoldEditorApplyOutcomeKind.AppliedWithBackupPreserved:
                HandleSuccessfulOutcome(outcome);
                break;
            case GoldEditorApplyOutcomeKind.PreviewChanged:
                SetState(GoldEditorState.BlockedUntilReload);
                SetResult(
                    "Preview changed",
                    "Preview changed — review and confirm again. Reload the slot first.",
                    GoldEditorResultSeverity.Warning,
                    GoldEditorAnnouncement.Polite);
                SetStatus("Editing is blocked until the slot is reloaded.");
                break;
            case GoldEditorApplyOutcomeKind.PreviewReadFailed:
                SetState(GoldEditorState.BlockedUntilReload);
                SetResult(
                    "Preview could not be verified",
                    GetPreviewReadFailureMessage(outcome.PreviewReadException),
                    GoldEditorResultSeverity.Error,
                    GoldEditorAnnouncement.Assertive);
                SetStatus("Editing is blocked until the slot is reloaded.");
                break;
            case GoldEditorApplyOutcomeKind.ReloadFailed:
                HandleReloadFailed(outcome);
                break;
            case GoldEditorApplyOutcomeKind.ApplicationFailed:
                HandleApplicationFailure(outcome);
                break;
            default:
                throw new InvalidOperationException("The apply outcome is not supported.");
        }
    }

    private void HandleSuccessfulOutcome(GoldEditorApplyOutcome outcome)
    {
        GoldEditorDocument reloaded = outcome.ReloadedDocument
            ?? throw new InvalidOperationException("A successful outcome requires a reload.");
        SetLastApplicationResult(outcome.Disposition, null);
        _document = reloaded;
        _newGoldText = string.Empty;
        SetState(GoldEditorState.Ready);
        ValidateNewGold();
        RaiseDocumentProperties();

        switch (outcome.Kind)
        {
            case GoldEditorApplyOutcomeKind.Unchanged:
                SetResult(
                    "Gold unchanged",
                    "Gold already has this value. No backup or staging file was touched.",
                    GoldEditorResultSeverity.Informational,
                    GoldEditorAnnouncement.Polite);
                break;
            case GoldEditorApplyOutcomeKind.AppliedWithBackupCreated:
                SetResult(
                    "Gold applied",
                    $"Gold was applied. The original slot was archived at "
                        + $"'{reloaded.BackupPath}'.",
                    GoldEditorResultSeverity.Success,
                    GoldEditorAnnouncement.Polite);
                break;
            case GoldEditorApplyOutcomeKind.AppliedWithBackupPreserved:
                SetResult(
                    "Gold applied",
                    $"Gold was applied. The existing archive at '{reloaded.BackupPath}' was "
                        + "preserved.",
                    GoldEditorResultSeverity.Success,
                    GoldEditorAnnouncement.Polite);
                break;
        }

        SetStatus("Ready. Enter a new Gold value.");
    }

    private void HandleReloadFailed(GoldEditorApplyOutcome outcome)
    {
        AtlasGoldFileApplicationDisposition disposition = outcome.Disposition
            ?? throw new InvalidOperationException("A reload failure requires a disposition.");
        SetLastApplicationResult(disposition, null);
        SetState(GoldEditorState.ResultReloadFailed);
        if (disposition == AtlasGoldFileApplicationDisposition.Unchanged)
        {
            SetResult(
                "No write needed; reload failed",
                "No write was needed, but the current slot could not be reloaded. Reopen the file "
                    + "before editing again.",
                GoldEditorResultSeverity.Error,
                GoldEditorAnnouncement.Assertive);
        }
        else
        {
            SetResult(
                "Gold applied; reload failed",
                "Gold was applied, but the slot could not be reloaded. Reopen the file before "
                    + "editing again.",
                GoldEditorResultSeverity.Error,
                GoldEditorAnnouncement.Assertive);
        }

        SetStatus("Editing is blocked until the slot is reopened.");
    }

    private void HandleApplicationFailure(GoldEditorApplyOutcome outcome)
    {
        AtlasGoldFileApplicationException exception = outcome.ApplicationException
            ?? throw new InvalidOperationException("An application failure requires an exception.");
        AtlasGoldFileApplicationFailure failure = exception.Failure;
        SetLastApplicationResult(null, failure);

        GoldEditorState nextState = failure switch
        {
            AtlasGoldFileApplicationFailure.BackupConflict
                or AtlasGoldFileApplicationFailure.StagingConflict
                or AtlasGoldFileApplicationFailure.ReplacementFailed =>
                GoldEditorState.Ready,
            _ => GoldEditorState.BlockedUntilReload,
        };
        SetState(nextState);

        string message = GetApplicationFailureMessage(failure);
        GoldEditorAnnouncement announcement =
            failure is AtlasGoldFileApplicationFailure.ReplacementOutcomeUnknown
                or AtlasGoldFileApplicationFailure.PostReplaceVerificationFailed
                ? GoldEditorAnnouncement.Assertive
                : GoldEditorAnnouncement.Polite;
        SetResult(
            GetApplicationFailureTitle(failure),
            message,
            GoldEditorResultSeverity.Error,
            announcement);
        SetStatus(
            nextState == GoldEditorState.Ready
                ? "The result was classified. A retry requires a new confirmation."
                : "Editing is blocked until the slot is reloaded.");
        ShouldRestoreApplyFocus = nextState == GoldEditorState.Ready;
    }

    private string GetApplicationFailureMessage(AtlasGoldFileApplicationFailure failure)
    {
        string slotPath = _document?.SlotPath ?? string.Empty;
        string backupPath = _document?.BackupPath ?? string.Empty;
        string backupStagingPath = _document?.BackupStagingPath ?? string.Empty;
        string candidateStagePath = _document?.CandidateStagePath ?? string.Empty;
        return failure switch
        {
            AtlasGoldFileApplicationFailure.UnsupportedPlatform =>
                "Gold file application is supported only on Windows. Reload before editing again.",
            AtlasGoldFileApplicationFailure.UnsupportedSlotPath =>
                "The slot path is not a supported canonical save-slot path. Reopen the file before "
                    + "editing again.",
            AtlasGoldFileApplicationFailure.BackupConflict =>
                "The fixed archive or archive staging path conflicts with this operation. Inspect "
                    + "the adjacent artifacts before confirming another attempt.",
            AtlasGoldFileApplicationFailure.StagingConflict =>
                "The fixed candidate stage conflicts with this operation. Inspect the retained "
                    + "candidate before confirming another attempt.",
            AtlasGoldFileApplicationFailure.SourceChanged =>
                "The source slot changed before replacement. Reload it, review the current Gold, "
                    + "and confirm again.",
            AtlasGoldFileApplicationFailure.ReplacementFailed =>
                "Replacement failed without changing the classified files. The exact candidate "
                    + "stage was retained. A retry requires a new confirmation.",
            AtlasGoldFileApplicationFailure.ReplacementOutcomeUnknown =>
                $"Replacement outcome is unknown. Do not assume success. Inspect live slot "
                    + $"'{slotPath}', archive '{backupPath}', backup staging "
                    + $"'{backupStagingPath}', and candidate stage '{candidateStagePath}', then "
                    + "reload.",
            AtlasGoldFileApplicationFailure.PostReplaceVerificationFailed =>
                $"Replacement returned, but post-replace verification failed. Do not assume "
                    + $"success. Inspect live slot '{slotPath}', archive '{backupPath}', backup "
                    + $"staging '{backupStagingPath}', and candidate stage "
                    + $"'{candidateStagePath}', then reload.",
            _ => "The Gold application failed without a success disposition.",
        };
    }

    private void BeginOperation(GoldEditorActivity activity, string status)
    {
        _activity = activity;
        _cancellationRequested = false;
        _operationCancellation = new CancellationTokenSource();
        ShouldRestoreApplyFocus = false;
        SetState(GoldEditorState.Busy);
        SetStatus(status);
        CloseResult();
        OnPropertyChanged(nameof(Activity));
        OnPropertyChanged(nameof(CanCancel));
    }

    private void CompleteOperation(CancellationTokenSource cancellation)
    {
        if (_state == GoldEditorState.Busy)
        {
            SetState(_document is null ? GoldEditorState.Empty : GoldEditorState.Ready);
        }

        _activity = GoldEditorActivity.None;
        _operationCancellation = null;
        _cancellationRequested = false;
        cancellation.Dispose();
        OnPropertyChanged(nameof(Activity));
        OnPropertyChanged(nameof(CanCancel));
        OperationCompleted?.Invoke(this, EventArgs.Empty);
    }

    private void RestoreDocument(
        GoldEditorDocument? previousDocument,
        GoldEditorState previousState)
    {
        _document = previousDocument;
        SetState(
            previousDocument is null
                ? GoldEditorState.Empty
                : previousState == GoldEditorState.Busy
                    ? GoldEditorState.Ready
                    : previousState);
        RaiseDocumentProperties();
    }

    private void ValidateNewGold()
    {
        string validation = TryParseGold(_newGoldText, out _, out _)
            ? string.Empty
            : "Enter an invariant whole number from -9223372036854775808 through "
                + "9223372036854775807.";
        if (StringComparer.Ordinal.Equals(_validationText, validation))
        {
            return;
        }

        _validationText = validation;
        OnPropertyChanged(nameof(ValidationText));
    }

    private void SetState(GoldEditorState state)
    {
        if (_state == state)
        {
            return;
        }

        _state = state;
        OnPropertyChanged(nameof(State));
        OnPropertyChanged(nameof(IsBusy));
        OnPropertyChanged(nameof(CanBrowse));
        OnPropertyChanged(nameof(CanEdit));
        OnPropertyChanged(nameof(CanApply));
        OnPropertyChanged(nameof(CanCancel));
    }

    private void SetStatus(string status)
    {
        if (StringComparer.Ordinal.Equals(_statusText, status))
        {
            return;
        }

        _statusText = status;
        OnPropertyChanged(nameof(StatusText));
    }

    private void SetResult(
        string title,
        string message,
        GoldEditorResultSeverity severity,
        GoldEditorAnnouncement announcement)
    {
        _resultTitle = title;
        _resultMessage = message;
        _resultSeverity = severity;
        _resultAnnouncement = announcement;
        _isResultOpen = true;
        OnPropertyChanged(nameof(ResultTitle));
        OnPropertyChanged(nameof(ResultMessage));
        OnPropertyChanged(nameof(ResultSeverity));
        OnPropertyChanged(nameof(ResultAnnouncement));
        OnPropertyChanged(nameof(IsResultOpen));
    }

    private void SetLastApplicationResult(
        AtlasGoldFileApplicationDisposition? disposition,
        AtlasGoldFileApplicationFailure? failure)
    {
        _lastDisposition = disposition;
        _lastApplicationFailure = failure;
        OnPropertyChanged(nameof(LastDisposition));
        OnPropertyChanged(nameof(LastApplicationFailure));
    }

    private void CloseResult()
    {
        if (!_isResultOpen)
        {
            return;
        }

        _isResultOpen = false;
        OnPropertyChanged(nameof(IsResultOpen));
    }

    private void RaiseDocumentProperties()
    {
        OnPropertyChanged(nameof(Document));
        OnPropertyChanged(nameof(SlotPath));
        OnPropertyChanged(nameof(CurrentGoldText));
        OnPropertyChanged(nameof(NewGoldText));
        OnPropertyChanged(nameof(CanApply));
        OnPropertyChanged(nameof(CanEdit));
    }

    private static string GetLoadFailureTitle(GoldEditorLoadFailure failure)
    {
        return failure switch
        {
            GoldEditorLoadFailure.UnsupportedSlotPath => "Unsupported slot path",
            GoldEditorLoadFailure.MissingOrInaccessibleFile => "Save slot unavailable",
            GoldEditorLoadFailure.UnsupportedOrMalformedSave => "Unsupported save",
            GoldEditorLoadFailure.InconsistentGoldLocations => "Inconsistent Gold",
            GoldEditorLoadFailure.ReadLimitExceeded => "Save limit exceeded",
            _ => "Unexpected local failure",
        };
    }

    private static string GetPreviewReadFailureMessage(GoldEditorLoadException? exception)
    {
        return exception is null
            ? "The live preview could not be read. Reload the slot before editing again."
            : $"{exception.Message} Reload the slot before editing again.";
    }

    private static string GetApplicationFailureTitle(
        AtlasGoldFileApplicationFailure failure)
    {
        return failure switch
        {
            AtlasGoldFileApplicationFailure.UnsupportedPlatform => "Unsupported platform",
            AtlasGoldFileApplicationFailure.UnsupportedSlotPath => "Unsupported slot path",
            AtlasGoldFileApplicationFailure.BackupConflict => "Archive conflict",
            AtlasGoldFileApplicationFailure.StagingConflict => "Candidate-stage conflict",
            AtlasGoldFileApplicationFailure.SourceChanged => "Source changed",
            AtlasGoldFileApplicationFailure.ReplacementFailed => "Replacement failed",
            AtlasGoldFileApplicationFailure.ReplacementOutcomeUnknown =>
                "Replacement outcome unknown",
            AtlasGoldFileApplicationFailure.PostReplaceVerificationFailed =>
                "Post-replace verification failed",
            _ => "Gold application failed",
        };
    }

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}
