using System.Reflection;
using Hcoona.CelesphoniaModifier.Atlas;
using Xunit;

namespace Hcoona.CelesphoniaModifier.WinUI.Tests;

public sealed class GoldEditorViewModelTests
{
    private static readonly string SlotPath = Path.Combine(
        Path.GetTempPath(),
        "celesphonia-view-model-tests",
        "file1.rpgsave");

    public static TheoryData<string, long, string> ValidGoldText =>
        new()
        {
            { "0", 0, "0" },
            { " +7 ", 7, "7" },
            { "-7", -7, "-7" },
            {
                long.MinValue.ToString(System.Globalization.CultureInfo.InvariantCulture),
                long.MinValue,
                long.MinValue.ToString(System.Globalization.CultureInfo.InvariantCulture)
            },
            {
                long.MaxValue.ToString(System.Globalization.CultureInfo.InvariantCulture),
                long.MaxValue,
                long.MaxValue.ToString(System.Globalization.CultureInfo.InvariantCulture)
            },
        };

    public static TheoryData<string> InvalidGoldText =>
        new()
        {
            string.Empty,
            " ",
            "+",
            "-",
            "1.0",
            "1e2",
            "1,000",
            "１２",
            "9223372036854775808",
            "-9223372036854775809",
            "1 2",
        };

    public static TheoryData<AtlasGoldFileApplicationFailure, string> ApplicationFailures =>
        new()
        {
            {
                AtlasGoldFileApplicationFailure.UnsupportedPlatform,
                nameof(GoldEditorState.BlockedUntilReload)
            },
            {
                AtlasGoldFileApplicationFailure.UnsupportedSlotPath,
                nameof(GoldEditorState.BlockedUntilReload)
            },
            {
                AtlasGoldFileApplicationFailure.BackupConflict,
                nameof(GoldEditorState.Ready)
            },
            {
                AtlasGoldFileApplicationFailure.StagingConflict,
                nameof(GoldEditorState.Ready)
            },
            {
                AtlasGoldFileApplicationFailure.SourceChanged,
                nameof(GoldEditorState.BlockedUntilReload)
            },
            {
                AtlasGoldFileApplicationFailure.ReplacementFailed,
                nameof(GoldEditorState.Ready)
            },
            {
                AtlasGoldFileApplicationFailure.ReplacementOutcomeUnknown,
                nameof(GoldEditorState.BlockedUntilReload)
            },
            {
                AtlasGoldFileApplicationFailure.PostReplaceVerificationFailed,
                nameof(GoldEditorState.BlockedUntilReload)
            },
        };

    [Fact]
    public void InitialStateIsEmptyAndNonActionable()
    {
        GoldEditorViewModel viewModel = new(new FakeOperations());

        Assert.Equal(GoldEditorState.Empty, viewModel.State);
        Assert.Equal(GoldEditorActivity.None, viewModel.Activity);
        Assert.False(viewModel.IsBusy);
        Assert.True(viewModel.CanBrowse);
        Assert.False(viewModel.CanEdit);
        Assert.False(viewModel.CanApply);
        Assert.False(viewModel.CanCancel);
        Assert.Equal(string.Empty, viewModel.SlotPath);
        Assert.Equal("Not loaded", viewModel.CurrentGoldText);
    }

    [Fact]
    public async Task LoadingTransitionsThroughBusyToReady()
    {
        TaskCompletionSource<GoldEditorDocument> completion = new(
            TaskCreationOptions.RunContinuationsAsynchronously);
        FakeOperations operations = new()
        {
            LoadHandler = (_, _) => new ValueTask<GoldEditorDocument>(completion.Task),
        };
        GoldEditorViewModel viewModel = new(operations);

        Task loading = viewModel.LoadAsync(SlotPath);

        Assert.Equal(GoldEditorState.Busy, viewModel.State);
        Assert.Equal(GoldEditorActivity.Loading, viewModel.Activity);
        Assert.True(viewModel.IsBusy);
        Assert.True(viewModel.CanCancel);
        completion.SetResult(CreateDocument(7));
        await loading;

        Assert.Equal(GoldEditorState.Ready, viewModel.State);
        Assert.Equal(GoldEditorActivity.None, viewModel.Activity);
        Assert.Equal(SlotPath, viewModel.SlotPath);
        Assert.Equal("7", viewModel.CurrentGoldText);
        Assert.True(viewModel.CanEdit);
        Assert.False(viewModel.CanApply);
    }

    [Fact]
    public async Task CanceledLoadingReturnsToEmptyWithClassifiedText()
    {
        FakeOperations operations = new()
        {
            LoadHandler = async (_, cancellationToken) =>
            {
                await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
                return CreateDocument(7);
            },
        };
        GoldEditorViewModel viewModel = new(operations);

        Task loading = viewModel.LoadAsync(SlotPath);
        viewModel.RequestCancellation();
        await loading;

        Assert.Equal(GoldEditorState.Empty, viewModel.State);
        Assert.Equal("Canceled", viewModel.ResultTitle);
        Assert.Contains("preserved", viewModel.ResultMessage, StringComparison.Ordinal);
        Assert.False(viewModel.IsBusy);
    }

    [Theory]
    [MemberData(nameof(ValidGoldText))]
    public void InvariantInt64ParserAcceptsAndNormalizes(
        string text,
        long expected,
        string normalized)
    {
        Assert.True(GoldEditorViewModel.TryParseGold(text, out long value, out string actual));
        Assert.Equal(expected, value);
        Assert.Equal(normalized, actual);
    }

    [Theory]
    [MemberData(nameof(InvalidGoldText))]
    public void InvariantInt64ParserRejectsNonContractText(string text)
    {
        Assert.False(GoldEditorViewModel.TryParseGold(text, out _, out _));
    }

    [Fact]
    public async Task ReadyInputControlsApplyAndConfirmation()
    {
        GoldEditorViewModel viewModel = await CreateReadyViewModelAsync(11);

        viewModel.NewGoldText = "1.0";
        Assert.False(viewModel.CanApply);
        Assert.False(viewModel.TryCreateConfirmation(out _));
        Assert.NotEmpty(viewModel.ValidationText);

        viewModel.NewGoldText = " +12 ";
        Assert.True(viewModel.CanApply);
        Assert.True(viewModel.TryCreateConfirmation(out GoldEditorConfirmation? confirmation));
        GoldEditorConfirmation actual = Assert.IsType<GoldEditorConfirmation>(confirmation);
        Assert.Equal(SlotPath, actual.SlotPath);
        Assert.Equal(11, actual.CurrentGold);
        Assert.Equal(12, actual.RequestedGold);
        Assert.Equal("12", actual.RequestedGoldText);
        Assert.Equal(SlotPath + ".celesphonia-original.bak", actual.BackupPath);
    }

    [Fact]
    public async Task PickerCancellationPreservesDocumentAndInput()
    {
        GoldEditorViewModel viewModel = await CreateReadyViewModelAsync(11);
        viewModel.NewGoldText = "12";
        GoldEditorDocument before = Assert.IsType<GoldEditorDocument>(viewModel.Document);

        viewModel.PreserveAfterPickerCancellation();

        Assert.Same(before, viewModel.Document);
        Assert.Equal(SlotPath, viewModel.SlotPath);
        Assert.Equal("11", viewModel.CurrentGoldText);
        Assert.Equal("12", viewModel.NewGoldText);
        Assert.True(viewModel.CanApply);
    }

    [Fact]
    public async Task ApplyingTransitionsThroughBusyAndCancellationRequest()
    {
        TaskCompletionSource<GoldEditorApplyOutcome> completion = new(
            TaskCreationOptions.RunContinuationsAsynchronously);
        FakeOperations operations = new()
        {
            ApplyHandler = async (_, _, cancellationToken) =>
            {
                using CancellationTokenRegistration registration =
                    cancellationToken.Register(
                        () => completion.TrySetCanceled(cancellationToken));
                return await completion.Task;
            },
        };
        GoldEditorViewModel viewModel = await CreateReadyViewModelAsync(11, operations);
        viewModel.NewGoldText = "12";

        Task applying = viewModel.ApplyConfirmedAsync(12);
        Assert.Equal(GoldEditorState.Busy, viewModel.State);
        Assert.Equal(GoldEditorActivity.Applying, viewModel.Activity);
        Assert.True(viewModel.CanCancel);

        viewModel.RequestCancellation();
        Assert.False(viewModel.CanCancel);
        Assert.Contains("Cancellation requested", viewModel.StatusText, StringComparison.Ordinal);
        await applying;

        Assert.Equal(GoldEditorState.Ready, viewModel.State);
        Assert.Equal("Canceled", viewModel.ResultTitle);
        Assert.True(viewModel.ShouldRestoreApplyFocus);
    }

    [Theory]
    [InlineData(
        AtlasGoldFileApplicationDisposition.Unchanged,
        nameof(GoldEditorApplyOutcomeKind.Unchanged))]
    [InlineData(
        AtlasGoldFileApplicationDisposition.AppliedWithBackupCreated,
        nameof(GoldEditorApplyOutcomeKind.AppliedWithBackupCreated))]
    [InlineData(
        AtlasGoldFileApplicationDisposition.AppliedWithBackupPreserved,
        nameof(GoldEditorApplyOutcomeKind.AppliedWithBackupPreserved))]
    public async Task SuccessfulDispositionAlwaysReplacesBaselineAndCurrentGold(
        AtlasGoldFileApplicationDisposition disposition,
        string expectedKind)
    {
        GoldEditorDocument replacement = CreateDocument(22, [9, 8, 7]);
        FakeOperations operations = new()
        {
            ApplyHandler = (_, _, _) => ValueTask.FromResult(
                GoldEditorApplyOutcome.Successful(disposition, replacement)),
        };
        GoldEditorViewModel viewModel = await CreateReadyViewModelAsync(11, operations);
        GoldEditorDocument original = Assert.IsType<GoldEditorDocument>(viewModel.Document);
        viewModel.NewGoldText = "22";

        await viewModel.ApplyConfirmedAsync(22);

        Assert.Equal(expectedKind, operations.LastOutcomeKind?.ToString());
        Assert.Equal(disposition, viewModel.LastDisposition);
        Assert.Null(viewModel.LastApplicationFailure);
        Assert.Equal(GoldEditorState.Ready, viewModel.State);
        Assert.Same(replacement, viewModel.Document);
        Assert.NotSame(original, viewModel.Document);
        Assert.Equal("22", viewModel.CurrentGoldText);
        Assert.Equal(string.Empty, viewModel.NewGoldText);
        Assert.True(viewModel.IsResultOpen);
        Assert.DoesNotContain(
            "failed",
            viewModel.ResultMessage,
            StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task StalePreviewBlocksUntilExplicitReloadAndRequiresReconfirmation()
    {
        FakeOperations operations = new()
        {
            ApplyHandler = (_, _, _) => ValueTask.FromResult(
                GoldEditorApplyOutcome.PreviewChanged()),
        };
        GoldEditorViewModel viewModel = await CreateReadyViewModelAsync(11, operations);
        viewModel.NewGoldText = "12";

        await viewModel.ApplyConfirmedAsync(12);

        Assert.Equal(GoldEditorState.BlockedUntilReload, viewModel.State);
        Assert.False(viewModel.CanApply);
        Assert.Contains(
            "review and confirm again",
            viewModel.ResultMessage,
            StringComparison.Ordinal);

        operations.LoadHandler = (_, _) => ValueTask.FromResult(CreateDocument(13));
        await viewModel.LoadAsync(SlotPath);
        Assert.Equal(GoldEditorState.Ready, viewModel.State);
        Assert.Equal("13", viewModel.CurrentGoldText);
        Assert.False(viewModel.CanApply);
    }

    [Fact]
    public async Task PreviewReadFailureBlocksUntilReload()
    {
        GoldEditorLoadException previewFailure = new(
            GoldEditorLoadFailure.MissingOrInaccessibleFile);
        FakeOperations operations = new()
        {
            ApplyHandler = (_, _, _) => ValueTask.FromResult(
                GoldEditorApplyOutcome.PreviewReadFailed(previewFailure)),
        };
        GoldEditorViewModel viewModel = await CreateReadyViewModelAsync(11, operations);
        viewModel.NewGoldText = "12";

        await viewModel.ApplyConfirmedAsync(12);

        Assert.Equal(GoldEditorState.BlockedUntilReload, viewModel.State);
        Assert.Contains("Reload", viewModel.ResultMessage, StringComparison.Ordinal);
        Assert.Equal(GoldEditorResultSeverity.Error, viewModel.ResultSeverity);
    }

    [Theory]
    [MemberData(nameof(ApplicationFailures))]
    public async Task EveryApplicationFailureMapsWithoutSuccessFallback(
        AtlasGoldFileApplicationFailure failure,
        string expectedState)
    {
        AtlasGoldFileApplicationException exception = CreateApplicationException(failure);
        FakeOperations operations = new()
        {
            ApplyHandler = (_, _, _) => ValueTask.FromResult(
                GoldEditorApplyOutcome.ApplicationFailed(exception)),
        };
        GoldEditorViewModel viewModel = await CreateReadyViewModelAsync(11, operations);
        viewModel.NewGoldText = "12";

        await viewModel.ApplyConfirmedAsync(12);

        Assert.Equal(expectedState, viewModel.State.ToString());
        Assert.Equal(failure, viewModel.LastApplicationFailure);
        Assert.Null(viewModel.LastDisposition);
        Assert.Equal(GoldEditorResultSeverity.Error, viewModel.ResultSeverity);
        Assert.True(viewModel.IsResultOpen);
        Assert.DoesNotContain(
            "Gold was applied",
            viewModel.ResultMessage,
            StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(
            "Gold already has",
            viewModel.ResultMessage,
            StringComparison.OrdinalIgnoreCase);
        if (failure is AtlasGoldFileApplicationFailure.ReplacementOutcomeUnknown
            or AtlasGoldFileApplicationFailure.PostReplaceVerificationFailed)
        {
            Assert.Equal(GoldEditorAnnouncement.Assertive, viewModel.ResultAnnouncement);
            Assert.Contains(SlotPath, viewModel.ResultMessage, StringComparison.Ordinal);
            Assert.Contains(
                ".celesphonia-original.bak.staging",
                viewModel.ResultMessage,
                StringComparison.Ordinal);
            Assert.Contains(
                ".celesphonia-stage.tmp",
                viewModel.ResultMessage,
                StringComparison.Ordinal);
        }
    }

    [Fact]
    public async Task AppliedAndUnchangedReloadFailuresRemainDistinct()
    {
        await AssertReloadFailureTextAsync(
            AtlasGoldFileApplicationDisposition.AppliedWithBackupCreated,
            expectedWrite: true,
            expectedText: "Gold was applied",
            forbiddenText: "No write was needed");
        await AssertReloadFailureTextAsync(
            AtlasGoldFileApplicationDisposition.Unchanged,
            expectedWrite: false,
            expectedText: "No write was needed",
            forbiddenText: "Gold was applied");
    }

    [Fact]
    public async Task FailedReopenRetainsReloadFailureDisposition()
    {
        GoldEditorApplyOutcome outcome = GoldEditorApplyOutcome.ReloadFailed(
            AtlasGoldFileApplicationDisposition.AppliedWithBackupPreserved,
            new IOException("Synthetic reload failure."));
        FakeOperations operations = new()
        {
            ApplyHandler = (_, _, _) => ValueTask.FromResult(outcome),
        };
        GoldEditorViewModel viewModel = await CreateReadyViewModelAsync(11, operations);
        viewModel.NewGoldText = "12";
        await viewModel.ApplyConfirmedAsync(12);
        operations.LoadHandler = (_, _) => throw new GoldEditorLoadException(
            GoldEditorLoadFailure.MissingOrInaccessibleFile);

        await viewModel.LoadAsync(SlotPath);

        Assert.Equal(GoldEditorState.ResultReloadFailed, viewModel.State);
        Assert.Equal(
            AtlasGoldFileApplicationDisposition.AppliedWithBackupPreserved,
            viewModel.LastDisposition);
        Assert.Null(viewModel.LastApplicationFailure);
    }

    [Fact]
    public async Task RetryRequiresAnotherConfirmedInvocation()
    {
        int invocations = 0;
        AtlasGoldFileApplicationException exception = CreateApplicationException(
            AtlasGoldFileApplicationFailure.ReplacementFailed);
        FakeOperations operations = new()
        {
            ApplyHandler = (_, _, _) =>
            {
                invocations++;
                return ValueTask.FromResult(
                    GoldEditorApplyOutcome.ApplicationFailed(exception));
            },
        };
        GoldEditorViewModel viewModel = await CreateReadyViewModelAsync(11, operations);
        viewModel.NewGoldText = "12";

        await viewModel.ApplyConfirmedAsync(12);
        Assert.Equal(1, invocations);
        Assert.Equal(GoldEditorState.Ready, viewModel.State);
        Assert.True(viewModel.TryCreateConfirmation(out _));

        await viewModel.ApplyConfirmedAsync(12);
        Assert.Equal(2, invocations);
    }

    [Fact]
    public async Task UnexpectedExceptionNeverBecomesSuccessShapedResult()
    {
        FakeOperations operations = new()
        {
            ApplyHandler = (_, _, _) => throw new InvalidOperationException(
                "Synthetic unexpected failure."),
        };
        GoldEditorViewModel viewModel = await CreateReadyViewModelAsync(11, operations);
        viewModel.NewGoldText = "12";

        await viewModel.ApplyConfirmedAsync(12);

        Assert.Equal(GoldEditorState.BlockedUntilReload, viewModel.State);
        Assert.Equal("Unexpected local failure", viewModel.ResultTitle);
        Assert.DoesNotContain(
            "applied",
            viewModel.ResultMessage,
            StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(
            "unchanged",
            viewModel.ResultMessage,
            StringComparison.OrdinalIgnoreCase);
        Assert.Equal(GoldEditorAnnouncement.Assertive, viewModel.ResultAnnouncement);
    }

    [Fact]
    public async Task FailedLoadPreservesExistingReadyDocumentAndInput()
    {
        FakeOperations operations = new();
        GoldEditorViewModel viewModel = await CreateReadyViewModelAsync(11, operations);
        viewModel.NewGoldText = "12";
        GoldEditorDocument before = Assert.IsType<GoldEditorDocument>(viewModel.Document);
        operations.LoadHandler = (_, _) => throw new GoldEditorLoadException(
            GoldEditorLoadFailure.UnsupportedSlotPath);

        await viewModel.LoadAsync("C:\\synthetic\\global.rpgsave");

        Assert.Equal(GoldEditorState.Ready, viewModel.State);
        Assert.Same(before, viewModel.Document);
        Assert.Equal("12", viewModel.NewGoldText);
        Assert.Equal("Unsupported slot path", viewModel.ResultTitle);
    }

    private static async Task AssertReloadFailureTextAsync(
        AtlasGoldFileApplicationDisposition disposition,
        bool expectedWrite,
        string expectedText,
        string forbiddenText)
    {
        GoldEditorApplyOutcome outcome = GoldEditorApplyOutcome.ReloadFailed(
            disposition,
            new IOException("Synthetic reload failure."));
        Assert.Equal(expectedWrite, outcome.WasWriteReported);
        FakeOperations operations = new()
        {
            ApplyHandler = (_, _, _) => ValueTask.FromResult(outcome),
        };
        GoldEditorViewModel viewModel = await CreateReadyViewModelAsync(11, operations);
        viewModel.NewGoldText = "12";

        await viewModel.ApplyConfirmedAsync(12);

        Assert.Equal(GoldEditorState.ResultReloadFailed, viewModel.State);
        Assert.Equal(disposition, viewModel.LastDisposition);
        Assert.Contains(expectedText, viewModel.ResultMessage, StringComparison.Ordinal);
        Assert.DoesNotContain(forbiddenText, viewModel.ResultMessage, StringComparison.Ordinal);
        Assert.False(viewModel.CanApply);
        Assert.Equal(GoldEditorAnnouncement.Assertive, viewModel.ResultAnnouncement);
    }

    private static async Task<GoldEditorViewModel> CreateReadyViewModelAsync(
        long gold,
        FakeOperations? operations = null)
    {
        FakeOperations actualOperations = operations ?? new FakeOperations();
        actualOperations.LoadHandler ??= (_, _) => ValueTask.FromResult(CreateDocument(gold));
        GoldEditorViewModel viewModel = new(actualOperations);
        await viewModel.LoadAsync(SlotPath);
        return viewModel;
    }

    private static GoldEditorDocument CreateDocument(long gold, byte[]? baseline = null)
    {
        return new GoldEditorDocument(SlotPath, gold, baseline ?? [1, 2, 3]);
    }

    private static AtlasGoldFileApplicationException CreateApplicationException(
        AtlasGoldFileApplicationFailure failure)
    {
        ConstructorInfo constructor = Assert.Single(
            typeof(AtlasGoldFileApplicationException).GetConstructors(
                BindingFlags.Instance | BindingFlags.NonPublic));
        return Assert.IsType<AtlasGoldFileApplicationException>(
            constructor.Invoke([failure, null]));
    }

    private sealed class FakeOperations : IGoldEditorOperations
    {
        internal Func<string, CancellationToken, ValueTask<GoldEditorDocument>>? LoadHandler
        {
            get;
            set;
        }

        internal Func<
            GoldEditorDocument,
            long,
            CancellationToken,
            ValueTask<GoldEditorApplyOutcome>>? ApplyHandler
        { get; set; }

        internal GoldEditorApplyOutcomeKind? LastOutcomeKind { get; private set; }

        public ValueTask<GoldEditorDocument> LoadAsync(
            string slotPath,
            CancellationToken cancellationToken)
        {
            return LoadHandler?.Invoke(slotPath, cancellationToken)
                ?? ValueTask.FromResult(CreateDocument(7));
        }

        public async ValueTask<GoldEditorApplyOutcome> ApplyAsync(
            GoldEditorDocument document,
            long requestedGold,
            CancellationToken cancellationToken)
        {
            GoldEditorApplyOutcome outcome = ApplyHandler is null
                ? GoldEditorApplyOutcome.Successful(
                    AtlasGoldFileApplicationDisposition.Unchanged,
                    CreateDocument(requestedGold))
                : await ApplyHandler(document, requestedGold, cancellationToken);
            LastOutcomeKind = outcome.Kind;
            return outcome;
        }
    }
}
