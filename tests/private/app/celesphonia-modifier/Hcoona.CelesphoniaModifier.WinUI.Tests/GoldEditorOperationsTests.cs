using System.Reflection;
using Hcoona.CelesphoniaModifier.Atlas;
using Xunit;

namespace Hcoona.CelesphoniaModifier.WinUI.Tests;

public sealed class GoldEditorOperationsTests
{
    public static TheoryData<long> FullRangeGoldValues =>
        new()
        {
            long.MinValue,
            -1,
            0,
            1,
            long.MaxValue,
        };

    public static TheoryData<AtlasGoldFileApplicationFailure> ApplicationFailures =>
        new()
        {
            AtlasGoldFileApplicationFailure.UnsupportedPlatform,
            AtlasGoldFileApplicationFailure.UnsupportedSlotPath,
            AtlasGoldFileApplicationFailure.BackupConflict,
            AtlasGoldFileApplicationFailure.StagingConflict,
            AtlasGoldFileApplicationFailure.SourceChanged,
            AtlasGoldFileApplicationFailure.ReplacementFailed,
            AtlasGoldFileApplicationFailure.ReplacementOutcomeUnknown,
            AtlasGoldFileApplicationFailure.PostReplaceVerificationFailed,
        };

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData(" ")]
    [InlineData("file1.rpgsave")]
    [InlineData("C:\\synthetic\\global.rpgsave")]
    [InlineData("C:\\synthetic\\file0.rpgsave")]
    [InlineData("C:\\synthetic\\file01.rpgsave")]
    [InlineData("C:\\synthetic\\file21.rpgsave")]
    [InlineData("C:\\synthetic\\FILE1.RPGSAVE")]
    [InlineData("C:\\synthetic\\file1.rpgsave.bak")]
    public async Task LoadRejectsUnsupportedPaths(string? path)
    {
        GoldEditorOperations operations = new();

        GoldEditorLoadException exception = await Assert.ThrowsAsync<GoldEditorLoadException>(
            async () => await operations.LoadAsync(
                path!,
                TestContext.Current.CancellationToken));

        Assert.Equal(GoldEditorLoadFailure.UnsupportedSlotPath, exception.Failure);
    }

    [Fact]
    public async Task LoadRejectsNormalizedDifferentPath()
    {
        await using SyntheticGoldSave save = await SyntheticGoldSave.CreateAsync();
        string aliasDirectory = Path.Combine(save.RootPath, "alias");
        Directory.CreateDirectory(aliasDirectory);
        string aliasedPath = Path.Combine(
            aliasDirectory,
            "..",
            Path.GetFileName(save.SlotPath));

        GoldEditorLoadException exception = await Assert.ThrowsAsync<GoldEditorLoadException>(
            async () => await new GoldEditorOperations().LoadAsync(
                aliasedPath,
                TestContext.Current.CancellationToken));

        Assert.Equal(GoldEditorLoadFailure.UnsupportedSlotPath, exception.Failure);
    }

    [Fact]
    public async Task EveryCanonicalSlotLeafIsAccepted()
    {
        await using SyntheticGoldSave save = await SyntheticGoldSave.CreateAsync();
        GoldEditorOperations operations = new();
        foreach (int index in Enumerable.Range(1, 20))
        {
            string path = Path.Combine(save.RootPath, $"file{index}.rpgsave");
            await File.WriteAllBytesAsync(
                path,
                SyntheticGoldSave.CreateSaveBytes(index),
                TestContext.Current.CancellationToken);

            GoldEditorDocument document = await operations.LoadAsync(
                path,
                TestContext.Current.CancellationToken);

            Assert.Equal(index, document.CurrentGold);
        }
    }

    [Theory]
    [MemberData(nameof(FullRangeGoldValues))]
    public async Task LoadAcceptsConsistentFullRangeGold(long gold)
    {
        await using SyntheticGoldSave save = await SyntheticGoldSave.CreateAsync(gold);

        GoldEditorDocument document = await new GoldEditorOperations().LoadAsync(
            save.SlotPath,
            TestContext.Current.CancellationToken);

        Assert.Equal(save.SlotPath, document.SlotPath);
        Assert.Equal(gold, document.CurrentGold);
    }

    [Fact]
    public async Task LoadClassifiesInvalidGoldShapesAndMalformedSaves()
    {
        await using SyntheticGoldSave save = await SyntheticGoldSave.CreateAsync();
        GoldEditorOperations operations = new();
        (byte[] Bytes, GoldEditorLoadFailure Failure)[] cases =
        [
            (
                SyntheticGoldSave.CreateMissingSaveBytes(),
                GoldEditorLoadFailure.UnsupportedOrMalformedSave
            ),
            (
                SyntheticGoldSave.CreateAmbiguousSaveBytes(),
                GoldEditorLoadFailure.UnsupportedOrMalformedSave
            ),
            (
                SyntheticGoldSave.CreateWrongShapeSaveBytes(),
                GoldEditorLoadFailure.UnsupportedOrMalformedSave
            ),
            (
                SyntheticGoldSave.CreateSaveBytes("1.5", "1.5"),
                GoldEditorLoadFailure.UnsupportedOrMalformedSave
            ),
            (
                SyntheticGoldSave.CreateSaveBytes(
                    "9223372036854775808",
                    "9223372036854775808"),
                GoldEditorLoadFailure.UnsupportedOrMalformedSave
            ),
            (
                SyntheticGoldSave.CreateSaveBytes("1", "2"),
                GoldEditorLoadFailure.InconsistentGoldLocations
            ),
            (
                "not-a-save"u8.ToArray(),
                GoldEditorLoadFailure.UnsupportedOrMalformedSave
            ),
        ];

        foreach ((byte[] bytes, GoldEditorLoadFailure failure) in cases)
        {
            await save.WriteAsync(bytes);

            GoldEditorLoadException exception =
                await Assert.ThrowsAsync<GoldEditorLoadException>(
                    async () => await operations.LoadAsync(
                        save.SlotPath,
                        TestContext.Current.CancellationToken));

            Assert.Equal(failure, exception.Failure);
        }
    }

    [Fact]
    public async Task LoadClassifiesReaderLimitAndFileAccessFailures()
    {
        await using SyntheticGoldSave save = await SyntheticGoldSave.CreateAsync();
        GoldEditorOperations operations = new();
        await save.WriteAsync(
            new byte[AtlasSaveReaderLimits.Default.MaximumEncodedBytes + 1]);

        GoldEditorLoadException limit = await Assert.ThrowsAsync<GoldEditorLoadException>(
            async () => await operations.LoadAsync(
                save.SlotPath,
                TestContext.Current.CancellationToken));
        Assert.Equal(GoldEditorLoadFailure.ReadLimitExceeded, limit.Failure);

        await save.WriteAsync(SyntheticGoldSave.CreateSaveBytes(7));
        using (FileStream held = new(
            save.SlotPath,
            FileMode.Open,
            FileAccess.Read,
            FileShare.None))
        {
            GoldEditorLoadException inaccessible =
                await Assert.ThrowsAsync<GoldEditorLoadException>(
                    async () => await operations.LoadAsync(
                        save.SlotPath,
                        TestContext.Current.CancellationToken));
            Assert.Equal(
                GoldEditorLoadFailure.MissingOrInaccessibleFile,
                inaccessible.Failure);
        }

        File.Delete(save.SlotPath);
        GoldEditorLoadException missing = await Assert.ThrowsAsync<GoldEditorLoadException>(
            async () => await operations.LoadAsync(
                save.SlotPath,
                TestContext.Current.CancellationToken));
        Assert.Equal(GoldEditorLoadFailure.MissingOrInaccessibleFile, missing.Failure);
    }

    [Fact]
    public async Task DocumentOwnsMutationResistantBaselineBytes()
    {
        await using SyntheticGoldSave save = await SyntheticGoldSave.CreateAsync(19);
        GoldEditorDocument document = await new GoldEditorOperations().LoadAsync(
            save.SlotPath,
            TestContext.Current.CancellationToken);

        byte[] firstCopy = document.CopyBaseline();
        byte originalFirstByte = firstCopy[0];
        firstCopy[0] ^= byte.MaxValue;
        byte[] secondCopy = document.CopyBaseline();

        Assert.Equal(originalFirstByte, secondCopy[0]);
        Assert.True(document.HasExactBaseline(secondCopy));
        Assert.False(document.HasExactBaseline(firstCopy));
    }

    [Fact]
    public async Task UnchangedPreviewInvokesApplicationAndMandatoryReload()
    {
        await using SyntheticGoldSave save = await SyntheticGoldSave.CreateAsync(4);
        int invocations = 0;
        GoldEditorOperations operations = new(
            applyInvoker: (_, _, _, _) =>
            {
                invocations++;
                return ValueTask.FromResult(
                    AtlasGoldFileApplicationDisposition.Unchanged);
            });
        GoldEditorDocument document = await operations.LoadAsync(
            save.SlotPath,
            TestContext.Current.CancellationToken);

        GoldEditorApplyOutcome outcome = await operations.ApplyAsync(
            document,
            4,
            TestContext.Current.CancellationToken);

        Assert.Equal(1, invocations);
        Assert.Equal(GoldEditorApplyOutcomeKind.Unchanged, outcome.Kind);
        Assert.Equal(4, Assert.IsType<GoldEditorDocument>(outcome.ReloadedDocument).CurrentGold);
    }

    [Fact]
    public async Task CancellationAfterSuccessfulDispositionDoesNotCancelMandatoryReload()
    {
        await using SyntheticGoldSave save = await SyntheticGoldSave.CreateAsync(4);
        using CancellationTokenSource cancellation = new();
        GoldEditorOperations operations = new(
            applyInvoker: (_, _, _, _) =>
            {
                cancellation.Cancel();
                return ValueTask.FromResult(
                    AtlasGoldFileApplicationDisposition.Unchanged);
            });
        GoldEditorDocument document = await operations.LoadAsync(
            save.SlotPath,
            TestContext.Current.CancellationToken);

        GoldEditorApplyOutcome outcome = await operations.ApplyAsync(
            document,
            4,
            cancellation.Token);

        Assert.Equal(GoldEditorApplyOutcomeKind.Unchanged, outcome.Kind);
        Assert.Equal(4, Assert.IsType<GoldEditorDocument>(outcome.ReloadedDocument).CurrentGold);
    }

    [Fact]
    public async Task ChangedPreviewDoesNotInvokeApplication()
    {
        await using SyntheticGoldSave save = await SyntheticGoldSave.CreateAsync(4);
        int invocations = 0;
        GoldEditorOperations operations = new(
            applyInvoker: (_, _, _, _) =>
            {
                invocations++;
                return ValueTask.FromResult(
                    AtlasGoldFileApplicationDisposition.Unchanged);
            });
        GoldEditorDocument document = await operations.LoadAsync(
            save.SlotPath,
            TestContext.Current.CancellationToken);
        await save.WriteAsync(SyntheticGoldSave.CreateSaveBytes(5));

        GoldEditorApplyOutcome outcome = await operations.ApplyAsync(
            document,
            6,
            TestContext.Current.CancellationToken);

        Assert.Equal(0, invocations);
        Assert.Equal(GoldEditorApplyOutcomeKind.PreviewChanged, outcome.Kind);
    }

    [Fact]
    public async Task UnreadablePreviewDoesNotInvokeApplication()
    {
        await using SyntheticGoldSave save = await SyntheticGoldSave.CreateAsync(4);
        int invocations = 0;
        GoldEditorOperations operations = new(
            applyInvoker: (_, _, _, _) =>
            {
                invocations++;
                return ValueTask.FromResult(
                    AtlasGoldFileApplicationDisposition.Unchanged);
            });
        GoldEditorDocument document = await operations.LoadAsync(
            save.SlotPath,
            TestContext.Current.CancellationToken);
        await save.WriteAsync("malformed"u8.ToArray());

        GoldEditorApplyOutcome outcome = await operations.ApplyAsync(
            document,
            6,
            TestContext.Current.CancellationToken);

        Assert.Equal(0, invocations);
        Assert.Equal(GoldEditorApplyOutcomeKind.PreviewReadFailed, outcome.Kind);
        Assert.Equal(
            GoldEditorLoadFailure.UnsupportedOrMalformedSave,
            Assert.IsType<GoldEditorLoadException>(outcome.PreviewReadException).Failure);
    }

    [Fact]
    public async Task CancellationBeforeApplicationInvocationIsObserved()
    {
        await using SyntheticGoldSave save = await SyntheticGoldSave.CreateAsync(4);
        int invocations = 0;
        GoldEditorOperations operations = new(
            applyInvoker: (_, _, _, _) =>
            {
                invocations++;
                return ValueTask.FromResult(
                    AtlasGoldFileApplicationDisposition.Unchanged);
            });
        GoldEditorDocument document = await operations.LoadAsync(
            save.SlotPath,
            TestContext.Current.CancellationToken);
        using CancellationTokenSource cancellation = new();
        await cancellation.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            async () => await operations.ApplyAsync(document, 5, cancellation.Token));
        Assert.Equal(0, invocations);
    }

    [Fact]
    public async Task ConvergenceHandleDeniesNewWriteSharingOpen()
    {
        await using SyntheticGoldSave save = await SyntheticGoldSave.CreateAsync(4);
        bool writeDenied = false;
        GoldEditorOperations operations = new(
            applyInvoker: (path, _, _, _) =>
            {
                try
                {
                    using FileStream writer = new(
                        path,
                        FileMode.Open,
                        FileAccess.Write,
                        FileShare.Read | FileShare.Delete);
                }
                catch (IOException)
                {
                    writeDenied = true;
                }

                return ValueTask.FromResult(
                    AtlasGoldFileApplicationDisposition.Unchanged);
            });
        GoldEditorDocument document = await operations.LoadAsync(
            save.SlotPath,
            TestContext.Current.CancellationToken);

        GoldEditorApplyOutcome outcome = await operations.ApplyAsync(
            document,
            4,
            TestContext.Current.CancellationToken);

        Assert.True(writeDenied);
        Assert.Equal(GoldEditorApplyOutcomeKind.Unchanged, outcome.Kind);
    }

    [Fact]
    public async Task ForcedReplaceByPathRaceCanApplyToReplacementDocument()
    {
        await using SyntheticGoldSave save = await SyntheticGoldSave.CreateAsync(1);
        GoldEditorOperations operations = new(
            applyInvoker: async (path, value, limits, cancellationToken) =>
            {
                save.ReplaceLive(SyntheticGoldSave.CreateSaveBytes(2));
                return await AtlasGoldFileApplication.ApplyAsync(
                    path,
                    value,
                    limits,
                    cancellationToken);
            });
        GoldEditorDocument document = await operations.LoadAsync(
            save.SlotPath,
            TestContext.Current.CancellationToken);

        GoldEditorApplyOutcome outcome = await operations.ApplyAsync(
            document,
            3,
            TestContext.Current.CancellationToken);

        Assert.Equal(
            GoldEditorApplyOutcomeKind.AppliedWithBackupCreated,
            outcome.Kind);
        Assert.Equal(3, Assert.IsType<GoldEditorDocument>(outcome.ReloadedDocument).CurrentGold);
        Assert.Equal(2, await SyntheticGoldSave.ReadGoldAsync(save.BackupPath));
    }

    [Fact]
    public async Task ForcedReplaceByPathRaceUnchangedStillReloadsReplacement()
    {
        await using SyntheticGoldSave save = await SyntheticGoldSave.CreateAsync(1);
        GoldEditorOperations operations = new(
            applyInvoker: async (path, value, limits, cancellationToken) =>
            {
                save.ReplaceLive(SyntheticGoldSave.CreateSaveBytes(9));
                return await AtlasGoldFileApplication.ApplyAsync(
                    path,
                    value,
                    limits,
                    cancellationToken);
            });
        GoldEditorDocument document = await operations.LoadAsync(
            save.SlotPath,
            TestContext.Current.CancellationToken);

        GoldEditorApplyOutcome outcome = await operations.ApplyAsync(
            document,
            9,
            TestContext.Current.CancellationToken);

        Assert.Equal(GoldEditorApplyOutcomeKind.Unchanged, outcome.Kind);
        Assert.Equal(9, Assert.IsType<GoldEditorDocument>(outcome.ReloadedDocument).CurrentGold);
        Assert.False(File.Exists(save.BackupPath));
        Assert.False(File.Exists(save.CandidateStagePath));
    }

    [Fact]
    public async Task SemanticNoOpTouchesNoArtifacts()
    {
        await using SyntheticGoldSave save = await SyntheticGoldSave.CreateAsync(7);
        GoldEditorOperations operations = new();
        GoldEditorDocument document = await operations.LoadAsync(
            save.SlotPath,
            TestContext.Current.CancellationToken);

        GoldEditorApplyOutcome outcome = await operations.ApplyAsync(
            document,
            7,
            TestContext.Current.CancellationToken);

        Assert.Equal(GoldEditorApplyOutcomeKind.Unchanged, outcome.Kind);
        Assert.False(File.Exists(save.BackupPath));
        Assert.False(File.Exists(save.BackupStagingPath));
        Assert.False(File.Exists(save.CandidateStagePath));
    }

    [Fact]
    public async Task ChangedApplicationsCreatePreserveAndRecreateArchive()
    {
        await using SyntheticGoldSave save = await SyntheticGoldSave.CreateAsync(1);
        GoldEditorOperations operations = new();
        GoldEditorDocument original = await operations.LoadAsync(
            save.SlotPath,
            TestContext.Current.CancellationToken);

        GoldEditorApplyOutcome first = await operations.ApplyAsync(
            original,
            2,
            TestContext.Current.CancellationToken);
        Assert.Equal(
            GoldEditorApplyOutcomeKind.AppliedWithBackupCreated,
            first.Kind);
        Assert.Equal(1, await SyntheticGoldSave.ReadGoldAsync(save.BackupPath));

        GoldEditorApplyOutcome second = await operations.ApplyAsync(
            Assert.IsType<GoldEditorDocument>(first.ReloadedDocument),
            3,
            TestContext.Current.CancellationToken);
        Assert.Equal(
            GoldEditorApplyOutcomeKind.AppliedWithBackupPreserved,
            second.Kind);
        Assert.Equal(1, await SyntheticGoldSave.ReadGoldAsync(save.BackupPath));

        File.Delete(save.BackupPath);
        GoldEditorApplyOutcome third = await operations.ApplyAsync(
            Assert.IsType<GoldEditorDocument>(second.ReloadedDocument),
            4,
            TestContext.Current.CancellationToken);
        Assert.Equal(
            GoldEditorApplyOutcomeKind.AppliedWithBackupCreated,
            third.Kind);
        Assert.Equal(3, await SyntheticGoldSave.ReadGoldAsync(save.BackupPath));
    }

    [Theory]
    [MemberData(nameof(ApplicationFailures))]
    public async Task EveryClassifiedApplicationFailureIsPreserved(
        AtlasGoldFileApplicationFailure failure)
    {
        await using SyntheticGoldSave save = await SyntheticGoldSave.CreateAsync(1);
        AtlasGoldFileApplicationException expected = CreateApplicationException(failure);
        GoldEditorOperations operations = new(
            applyInvoker: (_, _, _, _) =>
                new ValueTask<AtlasGoldFileApplicationDisposition>(
                    Task.FromException<AtlasGoldFileApplicationDisposition>(expected)));
        GoldEditorDocument document = await operations.LoadAsync(
            save.SlotPath,
            TestContext.Current.CancellationToken);

        GoldEditorApplyOutcome outcome = await operations.ApplyAsync(
            document,
            2,
            TestContext.Current.CancellationToken);

        Assert.Equal(GoldEditorApplyOutcomeKind.ApplicationFailed, outcome.Kind);
        Assert.Same(expected, outcome.ApplicationException);
        AtlasGoldFileApplicationException actual =
            Assert.IsType<AtlasGoldFileApplicationException>(outcome.ApplicationException);
        Assert.Equal(failure, actual.Failure);
    }

    [Fact]
    public async Task AppliedReloadFailureRetainsWriteDisposition()
    {
        await using SyntheticGoldSave save = await SyntheticGoldSave.CreateAsync(1);
        GoldEditorOperations operations = new(
            applyInvoker: async (path, value, limits, cancellationToken) =>
            {
                AtlasGoldFileApplicationDisposition disposition =
                    await AtlasGoldFileApplication.ApplyAsync(
                        path,
                        value,
                        limits,
                        cancellationToken);
                File.Delete(path);
                return disposition;
            });
        GoldEditorDocument document = await operations.LoadAsync(
            save.SlotPath,
            TestContext.Current.CancellationToken);

        GoldEditorApplyOutcome outcome = await operations.ApplyAsync(
            document,
            2,
            TestContext.Current.CancellationToken);

        Assert.Equal(GoldEditorApplyOutcomeKind.ReloadFailed, outcome.Kind);
        Assert.Equal(
            AtlasGoldFileApplicationDisposition.AppliedWithBackupCreated,
            outcome.Disposition);
        Assert.True(outcome.WasWriteReported);
        Assert.NotNull(outcome.ReloadException);
    }

    [Fact]
    public async Task UnchangedReloadFailureDoesNotBecomeWriteDisposition()
    {
        await using SyntheticGoldSave save = await SyntheticGoldSave.CreateAsync(1);
        GoldEditorOperations operations = new(
            applyInvoker: async (path, value, limits, cancellationToken) =>
            {
                AtlasGoldFileApplicationDisposition disposition =
                    await AtlasGoldFileApplication.ApplyAsync(
                        path,
                        value,
                        limits,
                        cancellationToken);
                File.Delete(path);
                return disposition;
            });
        GoldEditorDocument document = await operations.LoadAsync(
            save.SlotPath,
            TestContext.Current.CancellationToken);

        GoldEditorApplyOutcome outcome = await operations.ApplyAsync(
            document,
            1,
            TestContext.Current.CancellationToken);

        Assert.Equal(GoldEditorApplyOutcomeKind.ReloadFailed, outcome.Kind);
        Assert.Equal(AtlasGoldFileApplicationDisposition.Unchanged, outcome.Disposition);
        Assert.False(outcome.WasWriteReported);
        Assert.NotNull(outcome.ReloadException);
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
}
