using System.Text.Json.Nodes;
using Hcoona.CelesphoniaModifier.Atlas;
using Xunit;

namespace Hcoona.CelesphoniaModifier.Atlas.Tests;

public sealed class TrustedLocalCopyTests
{
    [Fact]
    public async Task CopyAsyncCreatesQualifiedSnapshotReceiptAndState()
    {
        await using AtlasSyntheticWorkspace workspace = await AtlasSyntheticWorkspace.CreateAsync();
        await PrepareApprovedWorkspaceAsync(workspace);
        AtlasIntakeCopyRequest request = workspace.CreateCopyRequest();
        workspace.WriteRequest(request);

        await TrustedLocalCopy.CopyAsync(
            workspace.Layout.CanonicalCopyRequestPath,
            TestContext.Current.CancellationToken);

        AtlasLoadedDocument<AtlasCopyReceiptDocument> receipt =
            await AtlasIntakeContracts.ReadCopyReceiptAsync(
                workspace.Layout.CanonicalCopyReceiptPath,
                TestContext.Current.CancellationToken);
        AtlasLoadedDocument<AtlasIntakeStateDocument> state =
            await AtlasIntakeContracts.ReadStateAsync(
                workspace.Layout.CanonicalQualifiedStatePath,
                TestContext.Current.CancellationToken);
        AtlasLoadedDocument<AtlasPrivateArtifactInventoryDocument> inventory =
            await AtlasIntakeContracts.ReadInventoryAsync(
                workspace.Layout.CanonicalInventoryPath,
                TestContext.Current.CancellationToken);

        Assert.Equal(AtlasIntakeContracts.ExactIncludedSaveCount, receipt.Document.SaveCount);
        Assert.Equal(
            AtlasIntakeContracts.ExactIncludedDefinitionCount,
            receipt.Document.DefinitionCount);
        Assert.Equal(
            AtlasIntakeContracts.ExactIncludedSaveCount
            + AtlasIntakeContracts.ExactIncludedDefinitionCount,
            receipt.Document.Entries.Length);
        Assert.Equal(AtlasIntakeContracts.QualifiedPhase, state.Document.Phase);
        Assert.Equal(
            AtlasIntakeContracts.SaveSnapshotRelativeRoot,
            state.Document.FinalCopyRootRelativePath);
        Assert.True(Directory.Exists(workspace.Layout.CanonicalFinalCopyPath));
        Assert.False(Directory.Exists(workspace.Layout.CanonicalIncompleteCopyPath));
        Assert.Contains(
            inventory.Document.Artifacts,
            artifact => artifact.Purpose == AtlasIntakeContracts.CopyReceiptPurpose);
        Assert.Contains(
            inventory.Document.Artifacts,
            artifact => artifact.Purpose == AtlasIntakeContracts.State3Purpose);
    }

    [Fact]
    public async Task CopyAsyncRejectsPreExistingInventoryBackupBeforeSourceAccess()
    {
        await using AtlasSyntheticWorkspace workspace = await AtlasSyntheticWorkspace.CreateAsync();
        await PrepareApprovedWorkspaceAsync(workspace);
        workspace.WriteRequest(workspace.CreateCopyRequest());
        await File.WriteAllBytesAsync(
            workspace.Layout.CanonicalQualifiedInventoryBackupPath,
            await File.ReadAllBytesAsync(
                workspace.Layout.CanonicalInventoryPath,
                TestContext.Current.CancellationToken),
            TestContext.Current.CancellationToken);
        AtlasIoSeams io = AtlasTestSupport.CreateIo(
            openFile: (path, mode, access, share, options) =>
                mode == FileMode.Open
                    && access == FileAccess.Read
                    && AtlasDiscovery.ContainsPath(workspace.GameRootPath, path)
                    ? throw new InvalidOperationException("Source access is not expected.")
                    : AtlasIoSeams.Default.OpenFile(path, mode, access, share, options));

        await Assert.ThrowsAsync<AtlasSafetyException>(
            () => TrustedLocalCopy.CopyAsync(
                workspace.Layout.CanonicalCopyRequestPath,
                io,
                TestContext.Current.CancellationToken).AsTask());
        Assert.False(Directory.Exists(workspace.Layout.CanonicalIncompleteCopyPath));
        Assert.False(Directory.Exists(workspace.Layout.CanonicalFinalCopyPath));
    }

    [Fact]
    public async Task
        ValidateCurrentSourcesAgainstManifestRejectsInactiveReparseRootBeforeEnumeration()
    {
        await using AtlasSyntheticWorkspace workspace = await AtlasSyntheticWorkspace.CreateAsync();
        await PrepareApprovedWorkspaceAsync(workspace);
        (AtlasCorpusIntakeManifest manifest,
            AtlasSourceRootMapDocument sourceRootMap,
            AtlasCopyPlanDocument copyPlan) =
            await LoadApprovedCopyInputsAsync(workspace);
        int enumerationCount = 0;
        AtlasIoSeams io = AtlasTestSupport.CreateIo(
            getAttributes: path =>
                AtlasIntakeContracts.PathEquals(path, workspace.WebSaveRootPath)
                    ? FileAttributes.Directory | FileAttributes.ReparsePoint
                    : AtlasIoSeams.Default.GetAttributes(path),
            enumerateFileSystemEntries: (path, searchOption) =>
            {
                enumerationCount++;
                return AtlasIoSeams.Default.EnumerateFileSystemEntries(path, searchOption);
            });

        Assert.Throws<AtlasSafetyException>(() =>
            TrustedLocalCopy.ValidateCurrentSourcesAgainstManifest(
                manifest,
                sourceRootMap,
                copyPlan,
                io));
        Assert.Equal(0, enumerationCount);
    }

    [Fact]
    public async Task
        ValidateCurrentSourcesAgainstManifestRejectsReparseComponentBeforeEnumeration()
    {
        await using AtlasSyntheticWorkspace workspace = await AtlasSyntheticWorkspace.CreateAsync();
        await PrepareApprovedWorkspaceAsync(workspace);
        (AtlasCorpusIntakeManifest manifest,
            AtlasSourceRootMapDocument sourceRootMap,
            AtlasCopyPlanDocument copyPlan) =
            await LoadApprovedCopyInputsAsync(workspace);
        int enumerationCount = 0;
        AtlasIoSeams io = AtlasTestSupport.CreateIo(
            getAttributes: path =>
                AtlasIntakeContracts.PathEquals(path, workspace.GameRootPath)
                    ? FileAttributes.Directory | FileAttributes.ReparsePoint
                    : AtlasIoSeams.Default.GetAttributes(path),
            enumerateFileSystemEntries: (path, searchOption) =>
            {
                enumerationCount++;
                return AtlasIoSeams.Default.EnumerateFileSystemEntries(path, searchOption);
            });

        Assert.Throws<AtlasSafetyException>(() =>
            TrustedLocalCopy.ValidateCurrentSourcesAgainstManifest(
                manifest,
                sourceRootMap,
                copyPlan,
                io));
        Assert.Equal(0, enumerationCount);
    }

    [Fact]
    public async Task CopyAsyncRecoversAfterReceiptPublicationWithoutReopeningSources()
    {
        await using AtlasSyntheticWorkspace workspace = await AtlasSyntheticWorkspace.CreateAsync();
        await PrepareApprovedWorkspaceAsync(workspace);
        AtlasIntakeCopyRequest request = workspace.CreateCopyRequest();
        workspace.WriteRequest(request);
        bool failed = false;
        AtlasIoSeams failingIo = AtlasTestSupport.CreateIo(
            moveFile: (source, destination) =>
            {
                AtlasIoSeams.Default.MoveFile(source, destination);
                if (!failed
                    && AtlasIntakeContracts.PathEquals(
                        destination,
                        workspace.Layout.CanonicalCopyReceiptPath))
                {
                    failed = true;
                    throw new IOException("synthetic receipt publication failure");
                }
            });

        await Assert.ThrowsAsync<IOException>(
            () => TrustedLocalCopy.CopyAsync(
                workspace.Layout.CanonicalCopyRequestPath,
                failingIo,
                TestContext.Current.CancellationToken).AsTask());

        int liveSourceOpenCount = 0;
        AtlasIoSeams io = AtlasTestSupport.CreateSourceReadCountingIo(
            workspace,
            () => liveSourceOpenCount++);

        await TrustedLocalCopy.CopyAsync(
            workspace.Layout.CanonicalCopyRequestPath,
            io,
            TestContext.Current.CancellationToken);

        Assert.Equal(0, liveSourceOpenCount);
        Assert.True(File.Exists(workspace.Layout.CanonicalQualifiedStatePath));
    }

    [Fact]
    public async Task CopyAsyncRecoversFromCompletePreRenameDirectoryWithoutReopeningSources()
    {
        await using AtlasSyntheticWorkspace workspace = await AtlasSyntheticWorkspace.CreateAsync();
        await PrepareApprovedWorkspaceAsync(workspace);
        workspace.WriteRequest(workspace.CreateCopyRequest());
        bool failed = false;
        AtlasIoSeams failingIo = AtlasTestSupport.CreateIo(
            moveDirectory: (source, destination) =>
            {
                if (!failed
                    && AtlasIntakeContracts.PathEquals(
                        destination,
                        workspace.Layout.CanonicalFinalCopyPath))
                {
                    failed = true;
                    throw new IOException("synthetic rename failure");
                }

                AtlasIoSeams.Default.MoveDirectory(source, destination);
            });

        await Assert.ThrowsAsync<IOException>(
            () => TrustedLocalCopy.CopyAsync(
                workspace.Layout.CanonicalCopyRequestPath,
                failingIo,
                TestContext.Current.CancellationToken).AsTask());

        Assert.True(Directory.Exists(workspace.Layout.CanonicalIncompleteCopyPath));
        Assert.False(Directory.Exists(workspace.Layout.CanonicalFinalCopyPath));

        int liveSourceOpenCount = 0;
        await TrustedLocalCopy.CopyAsync(
            workspace.Layout.CanonicalCopyRequestPath,
            AtlasTestSupport.CreateSourceReadCountingIo(
                workspace,
                () => liveSourceOpenCount++),
            TestContext.Current.CancellationToken);

        Assert.Equal(0, liveSourceOpenCount);
        Assert.False(Directory.Exists(workspace.Layout.CanonicalIncompleteCopyPath));
        Assert.True(File.Exists(workspace.Layout.CanonicalQualifiedStatePath));
    }

    [Fact]
    public async Task
        CopyAsyncPreservesRecoverableIncompleteDirectoryWhenCallerCanceledBeforeRename()
    {
        await using AtlasSyntheticWorkspace workspace = await AtlasSyntheticWorkspace.CreateAsync();
        await PrepareApprovedWorkspaceAsync(workspace);
        workspace.WriteRequest(workspace.CreateCopyRequest());
        using CancellationTokenSource source = new();
        bool cancelled = false;
        AtlasIoSeams failingIo = AtlasTestSupport.CreateIo(
            moveDirectory: (currentSource, destination) =>
            {
                if (!cancelled
                    && AtlasIntakeContracts.PathEquals(
                        destination,
                        workspace.Layout.CanonicalFinalCopyPath))
                {
                    cancelled = true;
                    source.Cancel();
                    throw new OperationCanceledException(source.Token);
                }

                AtlasIoSeams.Default.MoveDirectory(currentSource, destination);
            });

        await Assert.ThrowsAsync<OperationCanceledException>(
            () => TrustedLocalCopy.CopyAsync(
                workspace.Layout.CanonicalCopyRequestPath,
                failingIo,
                source.Token).AsTask());

        Assert.True(Directory.Exists(workspace.Layout.CanonicalIncompleteCopyPath));
        Assert.False(Directory.Exists(workspace.Layout.CanonicalFinalCopyPath));

        int liveSourceOpenCount = 0;
        await TrustedLocalCopy.CopyAsync(
            workspace.Layout.CanonicalCopyRequestPath,
            AtlasTestSupport.CreateSourceReadCountingIo(
                workspace,
                () => liveSourceOpenCount++),
            TestContext.Current.CancellationToken);

        Assert.Equal(0, liveSourceOpenCount);
        Assert.False(Directory.Exists(workspace.Layout.CanonicalIncompleteCopyPath));
        Assert.True(File.Exists(workspace.Layout.CanonicalQualifiedStatePath));
    }

    [Fact]
    public async Task CopyAsyncRecoversAfterRenameWithoutReopeningSources()
    {
        await using AtlasSyntheticWorkspace workspace = await AtlasSyntheticWorkspace.CreateAsync();
        await PrepareApprovedWorkspaceAsync(workspace);
        workspace.WriteRequest(workspace.CreateCopyRequest());
        bool failed = false;
        AtlasIoSeams failingIo = AtlasTestSupport.CreateIo(
            moveDirectory: (source, destination) =>
            {
                AtlasIoSeams.Default.MoveDirectory(source, destination);
                if (!failed
                    && AtlasIntakeContracts.PathEquals(
                        destination,
                        workspace.Layout.CanonicalFinalCopyPath))
                {
                    failed = true;
                    throw new IOException("synthetic post-rename failure");
                }
            });

        await Assert.ThrowsAsync<IOException>(
            () => TrustedLocalCopy.CopyAsync(
                workspace.Layout.CanonicalCopyRequestPath,
                failingIo,
                TestContext.Current.CancellationToken).AsTask());

        Assert.False(Directory.Exists(workspace.Layout.CanonicalIncompleteCopyPath));
        Assert.True(Directory.Exists(workspace.Layout.CanonicalFinalCopyPath));

        int liveSourceOpenCount = 0;
        await TrustedLocalCopy.CopyAsync(
            workspace.Layout.CanonicalCopyRequestPath,
            AtlasTestSupport.CreateSourceReadCountingIo(
                workspace,
                () => liveSourceOpenCount++),
            TestContext.Current.CancellationToken);

        Assert.Equal(0, liveSourceOpenCount);
        Assert.True(File.Exists(workspace.Layout.CanonicalQualifiedStatePath));
    }

    [Fact]
    public async Task CopyAsyncRecoversAfterInventoryReplacementBeforeReceiptPublication()
    {
        await using AtlasSyntheticWorkspace workspace = await AtlasSyntheticWorkspace.CreateAsync();
        await PrepareApprovedWorkspaceAsync(workspace);
        workspace.WriteRequest(workspace.CreateCopyRequest());
        bool failed = false;
        AtlasIoSeams failingIo = AtlasTestSupport.CreateIo(
            moveFile: (source, destination) =>
            {
                if (!failed
                    && AtlasIntakeContracts.PathEquals(
                        destination,
                        workspace.Layout.CanonicalCopyReceiptPath))
                {
                    failed = true;
                    throw new IOException("synthetic pre-receipt publication failure");
                }

                AtlasIoSeams.Default.MoveFile(source, destination);
            });

        await Assert.ThrowsAsync<IOException>(
            () => TrustedLocalCopy.CopyAsync(
                workspace.Layout.CanonicalCopyRequestPath,
                failingIo,
                TestContext.Current.CancellationToken).AsTask());

        Assert.True(Directory.Exists(workspace.Layout.CanonicalFinalCopyPath));
        Assert.False(File.Exists(workspace.Layout.CanonicalQualifiedStatePath));
        Assert.False(File.Exists(workspace.Layout.CanonicalCopyReceiptPath));

        int liveSourceOpenCount = 0;
        await TrustedLocalCopy.CopyAsync(
            workspace.Layout.CanonicalCopyRequestPath,
            AtlasTestSupport.CreateSourceReadCountingIo(
                workspace,
                () => liveSourceOpenCount++),
            TestContext.Current.CancellationToken);

        Assert.Equal(0, liveSourceOpenCount);
        Assert.True(File.Exists(workspace.Layout.CanonicalCopyReceiptPath));
        Assert.True(File.Exists(workspace.Layout.CanonicalQualifiedStatePath));
    }

    [Fact]
    public async Task CopyAsyncRecoversAfterCancellationDuringReceiptPromotion()
    {
        await using AtlasSyntheticWorkspace workspace = await AtlasSyntheticWorkspace.CreateAsync();
        await PrepareApprovedWorkspaceAsync(workspace);
        workspace.WriteRequest(workspace.CreateCopyRequest());
        using CancellationTokenSource source = new();
        bool cancelled = false;
        AtlasIoSeams failingIo = AtlasTestSupport.CreateIo(
            moveFile: (currentSource, destination) =>
            {
                if (!cancelled
                    && AtlasIntakeContracts.PathEquals(
                        destination,
                        workspace.Layout.CanonicalCopyReceiptPath))
                {
                    cancelled = true;
                    source.Cancel();
                    throw new OperationCanceledException(source.Token);
                }

                AtlasIoSeams.Default.MoveFile(currentSource, destination);
            });

        await Assert.ThrowsAsync<OperationCanceledException>(
            () => TrustedLocalCopy.CopyAsync(
                workspace.Layout.CanonicalCopyRequestPath,
                failingIo,
                source.Token).AsTask());

        Assert.True(Directory.Exists(workspace.Layout.CanonicalFinalCopyPath));
        Assert.False(File.Exists(workspace.Layout.CanonicalCopyReceiptPath));
        Assert.False(File.Exists(workspace.Layout.CanonicalQualifiedStatePath));

        int liveSourceOpenCount = 0;
        await TrustedLocalCopy.CopyAsync(
            workspace.Layout.CanonicalCopyRequestPath,
            AtlasTestSupport.CreateSourceReadCountingIo(
                workspace,
                () => liveSourceOpenCount++),
            TestContext.Current.CancellationToken);

        Assert.Equal(0, liveSourceOpenCount);
        Assert.True(File.Exists(workspace.Layout.CanonicalCopyReceiptPath));
        Assert.True(File.Exists(workspace.Layout.CanonicalQualifiedStatePath));
    }

    [Fact]
    public async Task CopyAsyncRejectsMissingFinalCopyAfterInventoryReplacementWithoutSourceAccess()
    {
        await using AtlasSyntheticWorkspace workspace = await AtlasSyntheticWorkspace.CreateAsync();
        await PrepareApprovedWorkspaceAsync(workspace);
        workspace.WriteRequest(workspace.CreateCopyRequest());
        bool failed = false;
        AtlasIoSeams failingIo = AtlasTestSupport.CreateIo(
            moveFile: (source, destination) =>
            {
                if (!failed
                    && AtlasIntakeContracts.PathEquals(
                        destination,
                        workspace.Layout.CanonicalCopyReceiptPath))
                {
                    failed = true;
                    throw new IOException("synthetic pre-receipt publication failure");
                }

                AtlasIoSeams.Default.MoveFile(source, destination);
            });

        await Assert.ThrowsAsync<IOException>(
            () => TrustedLocalCopy.CopyAsync(
                workspace.Layout.CanonicalCopyRequestPath,
                failingIo,
                TestContext.Current.CancellationToken).AsTask());

        DeleteDirectoryTree(workspace.Layout.CanonicalFinalCopyPath);
        int liveSourceOpenCount = 0;

        await Assert.ThrowsAsync<AtlasSafetyException>(
            () => TrustedLocalCopy.CopyAsync(
                workspace.Layout.CanonicalCopyRequestPath,
                AtlasTestSupport.CreateSourceReadCountingIo(
                    workspace,
                    () => liveSourceOpenCount++),
                TestContext.Current.CancellationToken).AsTask());

        Assert.Equal(0, liveSourceOpenCount);
    }

    [Fact]
    public async Task CopyAsyncRejectsMissingReceiptAfterInventoryReplacementWithoutSourceAccess()
    {
        await using AtlasSyntheticWorkspace workspace = await AtlasSyntheticWorkspace.CreateAsync();
        await PrepareApprovedWorkspaceAsync(workspace);
        workspace.WriteRequest(workspace.CreateCopyRequest());
        bool failed = false;
        AtlasIoSeams failingIo = AtlasTestSupport.CreateIo(
            moveFile: (source, destination) =>
            {
                if (!failed
                    && AtlasIntakeContracts.PathEquals(
                        destination,
                        workspace.Layout.CanonicalCopyReceiptPath))
                {
                    failed = true;
                    throw new IOException("synthetic pre-receipt publication failure");
                }

                AtlasIoSeams.Default.MoveFile(source, destination);
            });

        await Assert.ThrowsAsync<IOException>(
            () => TrustedLocalCopy.CopyAsync(
                workspace.Layout.CanonicalCopyRequestPath,
                failingIo,
                TestContext.Current.CancellationToken).AsTask());

        string stagedReceiptPath = AtlasDiscovery.GetStagingPath(
            workspace.Layout.CanonicalCopyReceiptPath,
            AtlasIntakeContracts.QualifiedPhase);
        File.Delete(stagedReceiptPath);
        int liveSourceOpenCount = 0;

        await Assert.ThrowsAsync<AtlasSafetyException>(
            () => TrustedLocalCopy.CopyAsync(
                workspace.Layout.CanonicalCopyRequestPath,
                AtlasTestSupport.CreateSourceReadCountingIo(
                    workspace,
                    () => liveSourceOpenCount++),
                TestContext.Current.CancellationToken).AsTask());

        Assert.Equal(0, liveSourceOpenCount);
    }

    [Fact]
    public async Task CopyAsyncRejectsFinalReceiptBeforeInventoryPublication()
    {
        await using AtlasSyntheticWorkspace workspace = await AtlasSyntheticWorkspace.CreateAsync();
        await PrepareApprovedWorkspaceAsync(workspace);
        workspace.WriteRequest(workspace.CreateCopyRequest());
        await TrustedLocalCopy.CopyAsync(
            workspace.Layout.CanonicalCopyRequestPath,
            TestContext.Current.CancellationToken);
        File.Delete(workspace.Layout.CanonicalQualifiedStatePath);
        byte[] priorInventoryBytes = await File.ReadAllBytesAsync(
            workspace.Layout.CanonicalQualifiedInventoryBackupPath,
            TestContext.Current.CancellationToken);
        await File.WriteAllBytesAsync(
            workspace.Layout.CanonicalInventoryPath,
            priorInventoryBytes,
            TestContext.Current.CancellationToken);
        int liveSourceOpenCount = 0;

        await Assert.ThrowsAsync<AtlasSafetyException>(
            () => TrustedLocalCopy.CopyAsync(
                workspace.Layout.CanonicalCopyRequestPath,
                AtlasTestSupport.CreateSourceReadCountingIo(
                    workspace,
                    () => liveSourceOpenCount++),
                TestContext.Current.CancellationToken).AsTask());

        Assert.Equal(0, liveSourceOpenCount);
    }

    [Fact]
    public async Task CopyAsyncRecoversAfterCancellationBeforeStatePublication()
    {
        await using AtlasSyntheticWorkspace workspace = await AtlasSyntheticWorkspace.CreateAsync();
        await PrepareApprovedWorkspaceAsync(workspace);
        workspace.WriteRequest(workspace.CreateCopyRequest());
        bool cancelled = false;
        AtlasIoSeams failingIo = AtlasTestSupport.CreateIo(
            moveFile: (source, destination) =>
            {
                if (!cancelled
                    && AtlasIntakeContracts.PathEquals(
                        destination,
                        workspace.Layout.CanonicalQualifiedStatePath))
                {
                    cancelled = true;
                    throw new OperationCanceledException(
                        "synthetic state publication cancellation");
                }

                AtlasIoSeams.Default.MoveFile(source, destination);
            });

        await Assert.ThrowsAsync<OperationCanceledException>(
            () => TrustedLocalCopy.CopyAsync(
                workspace.Layout.CanonicalCopyRequestPath,
                failingIo,
                TestContext.Current.CancellationToken).AsTask());

        int liveSourceOpenCount = 0;
        await TrustedLocalCopy.CopyAsync(
            workspace.Layout.CanonicalCopyRequestPath,
            AtlasTestSupport.CreateSourceReadCountingIo(
                workspace,
                () => liveSourceOpenCount++),
            TestContext.Current.CancellationToken);

        Assert.Equal(0, liveSourceOpenCount);
        Assert.True(File.Exists(workspace.Layout.CanonicalQualifiedStatePath));
    }

    [Fact]
    public async Task CopyAsyncRejectsQualifiedStateWhenReceiptIsMissing()
    {
        await using AtlasSyntheticWorkspace workspace = await AtlasSyntheticWorkspace.CreateAsync();
        await PrepareApprovedWorkspaceAsync(workspace);
        workspace.WriteRequest(workspace.CreateCopyRequest());
        await TrustedLocalCopy.CopyAsync(
            workspace.Layout.CanonicalCopyRequestPath,
            TestContext.Current.CancellationToken);

        File.Delete(workspace.Layout.CanonicalCopyReceiptPath);

        await Assert.ThrowsAsync<FileNotFoundException>(
            () => TrustedLocalCopy.CopyAsync(
                workspace.Layout.CanonicalCopyRequestPath,
                TestContext.Current.CancellationToken).AsTask());
    }

    [Fact]
    public async Task CopyAsyncRejectsRecoveredReceiptBindingMismatch()
    {
        await using AtlasSyntheticWorkspace workspace = await AtlasSyntheticWorkspace.CreateAsync();
        await PrepareApprovedWorkspaceAsync(workspace);
        workspace.WriteRequest(workspace.CreateCopyRequest());
        await TrustedLocalCopy.CopyAsync(
            workspace.Layout.CanonicalCopyRequestPath,
            TestContext.Current.CancellationToken);
        File.Delete(workspace.Layout.CanonicalQualifiedStatePath);

        JsonObject receipt = await AtlasTestSupport.LoadJsonObjectAsync(
            workspace.Layout.CanonicalCopyReceiptPath,
            TestContext.Current.CancellationToken);
        receipt["approvedManifestArtifactAlias"] = "private-artifact-999999";
        await AtlasTestSupport.WriteJsonAsync(
            workspace.Layout.CanonicalCopyReceiptPath,
            receipt,
            TestContext.Current.CancellationToken);

        await Assert.ThrowsAsync<AtlasSafetyException>(
            () => TrustedLocalCopy.CopyAsync(
                workspace.Layout.CanonicalCopyRequestPath,
                TestContext.Current.CancellationToken).AsTask());
    }

    [Fact]
    public async Task HasCompleteCopySetRejectsUnexpectedExtraFile()
    {
        await using AtlasSyntheticWorkspace workspace = await AtlasSyntheticWorkspace.CreateAsync();
        await PrepareApprovedWorkspaceAsync(workspace);
        workspace.WriteRequest(workspace.CreateCopyRequest());
        await TrustedLocalCopy.CopyAsync(
            workspace.Layout.CanonicalCopyRequestPath,
            TestContext.Current.CancellationToken);
        AtlasLoadedDocument<AtlasCopyPlanDocument> copyPlan =
            await AtlasIntakeContracts.ReadCopyPlanAsync(
                workspace.Layout.CanonicalCopyPlanPath,
                TestContext.Current.CancellationToken);
        string extraPath = Path.Combine(
            workspace.Layout.CanonicalFinalCopyPath,
            "extras",
            "unexpected.bin");
        Directory.CreateDirectory(Path.GetDirectoryName(extraPath)!);
        await File.WriteAllBytesAsync(
            extraPath,
            [1],
            TestContext.Current.CancellationToken);

        Assert.Throws<AtlasSafetyException>(() =>
            TrustedLocalCopy.HasCompleteCopySet(
                workspace.Layout.CanonicalFinalCopyPath,
                copyPlan.Document,
                workspace.Layout.CanonicalCopyReceiptPath,
                AtlasIoSeams.Default));
    }

    [Fact]
    public async Task HasCompleteCopySetRejectsReparseDirectoryBeforeDescending()
    {
        await using AtlasSyntheticWorkspace workspace = await AtlasSyntheticWorkspace.CreateAsync();
        await PrepareApprovedWorkspaceAsync(workspace);
        workspace.WriteRequest(workspace.CreateCopyRequest());
        await TrustedLocalCopy.CopyAsync(
            workspace.Layout.CanonicalCopyRequestPath,
            TestContext.Current.CancellationToken);
        AtlasLoadedDocument<AtlasCopyPlanDocument> copyPlan =
            await AtlasIntakeContracts.ReadCopyPlanAsync(
                workspace.Layout.CanonicalCopyPlanPath,
                TestContext.Current.CancellationToken);
        string injectedPath = Path.Combine(
            workspace.Layout.CanonicalFinalCopyPath,
            "injected-reparse");
        List<string> enumeratedDirectories = [];
        AtlasIoSeams io = AtlasTestSupport.CreateIo(
            enumerateFileSystemEntries: (path, searchOption) =>
            {
                Assert.Equal(SearchOption.TopDirectoryOnly, searchOption);
                enumeratedDirectories.Add(AtlasIntakeContracts.NormalizePath(path));
                if (AtlasIntakeContracts.PathEquals(path, injectedPath))
                {
                    throw new InvalidOperationException(
                        "Reparse directories must not be enumerated.");
                }

                IEnumerable<string> entries = AtlasIoSeams.Default.EnumerateFileSystemEntries(
                    path,
                    SearchOption.TopDirectoryOnly);
                return AtlasIntakeContracts.PathEquals(
                    path,
                    workspace.Layout.CanonicalFinalCopyPath)
                    ? entries.Append(injectedPath)
                    : entries;
            },
            getAttributes: path =>
                AtlasIntakeContracts.PathEquals(path, injectedPath)
                    ? FileAttributes.Directory | FileAttributes.ReparsePoint
                    : AtlasIoSeams.Default.GetAttributes(path));

        Assert.Throws<AtlasSafetyException>(() =>
            TrustedLocalCopy.HasCompleteCopySet(
                workspace.Layout.CanonicalFinalCopyPath,
                copyPlan.Document,
                workspace.Layout.CanonicalCopyReceiptPath,
                io));
        Assert.DoesNotContain(
            enumeratedDirectories,
            path => AtlasIntakeContracts.PathEquals(path, injectedPath));
    }

    [Fact]
    public async Task CopyAsyncRejectsFreshOutputFileConflictsBeforeSourceAccess()
    {
        await using AtlasSyntheticWorkspace workspace = await AtlasSyntheticWorkspace.CreateAsync();
        await PrepareApprovedWorkspaceAsync(workspace);
        workspace.WriteRequest(workspace.CreateCopyRequest());
        await File.WriteAllTextAsync(
            workspace.Layout.CanonicalFinalCopyPath,
            "conflict",
            TestContext.Current.CancellationToken);
        int liveSourceOpenCount = 0;

        await Assert.ThrowsAsync<AtlasSafetyException>(
            () => TrustedLocalCopy.CopyAsync(
                workspace.Layout.CanonicalCopyRequestPath,
                AtlasTestSupport.CreateSourceReadCountingIo(
                    workspace,
                    () => liveSourceOpenCount++),
                TestContext.Current.CancellationToken).AsTask());

        Assert.Equal(0, liveSourceOpenCount);

        File.Delete(workspace.Layout.CanonicalFinalCopyPath);
        await File.WriteAllTextAsync(
            workspace.Layout.CanonicalIncompleteCopyPath,
            "conflict",
            TestContext.Current.CancellationToken);
        await Assert.ThrowsAsync<AtlasSafetyException>(
            () => TrustedLocalCopy.CopyAsync(
                workspace.Layout.CanonicalCopyRequestPath,
                AtlasTestSupport.CreateSourceReadCountingIo(
                    workspace,
                    () => liveSourceOpenCount++),
                TestContext.Current.CancellationToken).AsTask());

        Assert.Equal(0, liveSourceOpenCount);
    }

    [Fact]
    public async Task CopyAsyncRejectsOutOfPhaseCleanupArtifact()
    {
        await using AtlasSyntheticWorkspace workspace = await AtlasSyntheticWorkspace.CreateAsync();
        await PrepareApprovedWorkspaceAsync(workspace);
        workspace.WriteRequest(workspace.CreateCopyRequest());
        Directory.CreateDirectory(workspace.Layout.CleanupDirectory);
        await File.WriteAllTextAsync(
            workspace.Layout.CanonicalCleanupPreflightReportPath,
            "{}",
            TestContext.Current.CancellationToken);

        await Assert.ThrowsAsync<AtlasSafetyException>(
            () => TrustedLocalCopy.CopyAsync(
                workspace.Layout.CanonicalCopyRequestPath,
                TestContext.Current.CancellationToken).AsTask());
    }

    [Fact]
    public async Task CopyAsyncPropagatesSharingViolation()
    {
        await using AtlasSyntheticWorkspace workspace = await AtlasSyntheticWorkspace.CreateAsync();
        await PrepareApprovedWorkspaceAsync(workspace);
        workspace.WriteRequest(workspace.CreateCopyRequest());
        string firstTrackedSource = Path.Combine(workspace.DefinitionRootPath, "www", "data");
        firstTrackedSource = Path.Combine(firstTrackedSource, "definition-000001.json");
        AtlasIoSeams failingIo = AtlasTestSupport.CreateIo(
            openFile: (path, mode, access, share, options) =>
                AtlasIntakeContracts.PathEquals(path, firstTrackedSource)
                    && mode == FileMode.Open
                    && access == FileAccess.Read
                    ? throw new IOException("synthetic sharing violation")
                    : AtlasIoSeams.Default.OpenFile(path, mode, access, share, options));

        await Assert.ThrowsAsync<IOException>(
            () => TrustedLocalCopy.CopyAsync(
                workspace.Layout.CanonicalCopyRequestPath,
                failingIo,
                TestContext.Current.CancellationToken).AsTask());
    }

    [Fact]
    public async Task CopyAsyncPreservesOriginalFailureWhenOwnedIncompleteDeleteFails()
    {
        await using AtlasSyntheticWorkspace workspace = await AtlasSyntheticWorkspace.CreateAsync();
        await PrepareApprovedWorkspaceAsync(workspace);
        workspace.WriteRequest(workspace.CreateCopyRequest());
        string priorInventorySha256 =
            AtlasSyntheticWorkspace.ComputeSha256(workspace.Layout.CanonicalInventoryPath);
        int trackedSourceOpenCount = 0;
        AtlasIoSeams failingIo = AtlasTestSupport.CreateIo(
            openFile: (path, mode, access, share, options) =>
            {
                if (mode == FileMode.Open
                    && access == FileAccess.Read
                    && AtlasDiscovery.ContainsPath(workspace.GameRootPath, path))
                {
                    trackedSourceOpenCount++;
                    if (trackedSourceOpenCount == 2)
                    {
                        throw new IOException("synthetic tracked source failure");
                    }
                }

                return AtlasIoSeams.Default.OpenFile(path, mode, access, share, options);
            },
            deleteDirectory: (path, recursive) =>
            {
                if (AtlasIntakeContracts.PathEquals(
                        path,
                        workspace.Layout.CanonicalIncompleteCopyPath))
                {
                    throw new IOException("synthetic incomplete delete failure");
                }

                AtlasIoSeams.Default.DeleteDirectory(path, recursive);
            });

        IOException exception = await Assert.ThrowsAsync<IOException>(
            () => TrustedLocalCopy.CopyAsync(
                workspace.Layout.CanonicalCopyRequestPath,
                failingIo,
                TestContext.Current.CancellationToken).AsTask());

        Assert.Equal("synthetic tracked source failure", exception.Message);
        Assert.True(Directory.Exists(workspace.Layout.CanonicalIncompleteCopyPath));
        Assert.False(Directory.Exists(workspace.Layout.CanonicalFinalCopyPath));
        Assert.False(File.Exists(workspace.Layout.CanonicalCopyReceiptPath));
        Assert.False(File.Exists(workspace.Layout.CanonicalQualifiedStatePath));
        Assert.False(File.Exists(workspace.Layout.CanonicalQualifiedInventoryBackupPath));
        Assert.Equal(
            priorInventorySha256,
            AtlasSyntheticWorkspace.ComputeSha256(workspace.Layout.CanonicalInventoryPath));
        Assert.True(File.Exists(Path.Combine(
            workspace.DefinitionRootPath,
            "www",
            "data",
            "definition-000001.json")));

        AtlasLoadedDocument<AtlasCopyPlanDocument> copyPlan =
            await AtlasIntakeContracts.ReadCopyPlanAsync(
                workspace.Layout.CanonicalCopyPlanPath,
                TestContext.Current.CancellationToken);
        string incompleteReceiptPath = Path.Combine(
            workspace.Layout.CanonicalIncompleteCopyPath,
            Path.GetFileName(AtlasDiscovery.GetStagingPath(
                workspace.Layout.CanonicalCopyReceiptPath,
                AtlasIntakeContracts.QualifiedPhase)));
        Assert.False(TrustedLocalCopy.HasCompleteCopySet(
            workspace.Layout.CanonicalIncompleteCopyPath,
            copyPlan.Document,
            incompleteReceiptPath,
            AtlasIoSeams.Default));

        int liveSourceOpenCount = 0;
        await Assert.ThrowsAsync<AtlasSafetyException>(
            () => TrustedLocalCopy.CopyAsync(
                workspace.Layout.CanonicalCopyRequestPath,
                AtlasTestSupport.CreateSourceReadCountingIo(
                    workspace,
                    () => liveSourceOpenCount++),
                TestContext.Current.CancellationToken).AsTask());

        Assert.Equal(0, liveSourceOpenCount);
    }

    [Fact]
    public async Task CopySourceFileAsyncRejectsShortReadFlushFailureAndHashMismatch()
    {
        await using AtlasSyntheticWorkspace workspace = await AtlasSyntheticWorkspace.CreateAsync();
        await PrepareApprovedWorkspaceAsync(workspace);
        AtlasLoadedDocument<AtlasCopyPlanDocument> copyPlan =
            await AtlasIntakeContracts.ReadCopyPlanAsync(
                workspace.Layout.CanonicalCopyPlanPath,
                TestContext.Current.CancellationToken);
        ResolvedCopySource source = new(
            copyPlan.Document.Entries[0],
            Path.Combine(
                workspace.DefinitionRootPath,
                "www",
                "data",
                "definition-000001.json"));

        string shortReadDestination = Path.Combine(
            workspace.Layout.CopiesDirectory,
            "short-read",
            "definition-000001.json");
        Directory.CreateDirectory(Path.GetDirectoryName(shortReadDestination)!);
        AtlasIoSeams shortReadIo = AtlasTestSupport.CreateIo(
            openFile: (path, mode, access, share, options) =>
                AtlasIntakeContracts.PathEquals(path, source.AbsolutePath)
                    && mode == FileMode.Open
                    && access == FileAccess.Read
                    ? new ShortReadStream(File.OpenRead(path))
                    : AtlasIoSeams.Default.OpenFile(path, mode, access, share, options));
        await Assert.ThrowsAsync<AtlasSafetyException>(
            () => TrustedLocalCopy.CopySourceFileAsync(
                source,
                shortReadDestination,
                shortReadIo,
                TestContext.Current.CancellationToken).AsTask());

        string flushFailureDestination = Path.Combine(
            workspace.Layout.CopiesDirectory,
            "flush-failure",
            "definition-000001.json");
        Directory.CreateDirectory(Path.GetDirectoryName(flushFailureDestination)!);
        AtlasIoSeams flushFailureIo = AtlasTestSupport.CreateIo(
            openFile: (path, mode, access, share, options) =>
                AtlasIntakeContracts.PathEquals(path, flushFailureDestination)
                    && mode == FileMode.CreateNew
                    && access == FileAccess.Write
                    ? new FlushFailingStream(
                        AtlasIoSeams.Default.OpenFile(path, mode, access, share, options))
                    : AtlasIoSeams.Default.OpenFile(path, mode, access, share, options));
        await Assert.ThrowsAsync<IOException>(
            () => TrustedLocalCopy.CopySourceFileAsync(
                source,
                flushFailureDestination,
                flushFailureIo,
                TestContext.Current.CancellationToken).AsTask());

        string mismatchedDestination = Path.Combine(
            workspace.Layout.CopiesDirectory,
            "hash-mismatch",
            "definition-000001.json");
        Directory.CreateDirectory(Path.GetDirectoryName(mismatchedDestination)!);
        AtlasIoSeams mismatchedIo = AtlasTestSupport.CreateIo(
            openFile: (path, mode, access, share, options) =>
                AtlasIntakeContracts.PathEquals(path, mismatchedDestination)
                    && mode == FileMode.CreateNew
                    && access == FileAccess.Write
                    ? new CorruptingWriteStream(
                        AtlasIoSeams.Default.OpenFile(path, mode, access, share, options))
                    : AtlasIoSeams.Default.OpenFile(path, mode, access, share, options));
        await Assert.ThrowsAsync<AtlasSafetyException>(
            () => TrustedLocalCopy.CopySourceFileAsync(
                source,
                mismatchedDestination,
                mismatchedIo,
                TestContext.Current.CancellationToken).AsTask());
    }

    private static async Task PrepareApprovedWorkspaceAsync(AtlasSyntheticWorkspace workspace)
    {
        await AtlasDiscovery.DiscoverAsync(
            workspace.Layout.CanonicalDiscoverRequestPath,
            TestContext.Current.CancellationToken);
        workspace.WriteRequest(workspace.CreateConfirmationRequest());
        await AtlasDiscovery.ConfirmAsync(
            workspace.Layout.CanonicalConfirmRequestPath,
            TestContext.Current.CancellationToken);
    }

    private static async Task<(
        AtlasCorpusIntakeManifest Manifest,
        AtlasSourceRootMapDocument SourceRootMap,
        AtlasCopyPlanDocument CopyPlan)> LoadApprovedCopyInputsAsync(
        AtlasSyntheticWorkspace workspace)
    {
        AtlasLoadedDocument<AtlasCorpusIntakeManifest> manifest =
            await AtlasIntakeContracts.ReadManifestAsync(
                workspace.Layout.CanonicalApprovedManifestPath,
                TestContext.Current.CancellationToken);
        AtlasLoadedDocument<AtlasSourceRootMapDocument> sourceRootMap =
            await AtlasIntakeContracts.ReadSourceRootMapAsync(
                workspace.Layout.CanonicalSourceRootMapPath,
                TestContext.Current.CancellationToken);
        AtlasLoadedDocument<AtlasCopyPlanDocument> copyPlan =
            await AtlasIntakeContracts.ReadCopyPlanAsync(
                workspace.Layout.CanonicalCopyPlanPath,
                TestContext.Current.CancellationToken);
        return (manifest.Document, sourceRootMap.Document, copyPlan.Document);
    }

    private static void DeleteDirectoryTree(string path)
    {
        foreach (string filePath in Directory.EnumerateFiles(
                     path,
                     "*",
                     SearchOption.AllDirectories))
        {
            File.SetAttributes(filePath, FileAttributes.Normal);
        }

        Directory.Delete(path, recursive: true);
    }

    private sealed class ShortReadStream(Stream innerStream) : DelegatingStream(innerStream)
    {
        public override long Length => base.Length + 1;
    }

    private sealed class FlushFailingStream(Stream innerStream) : DelegatingStream(innerStream)
    {
        public override Task FlushAsync(CancellationToken cancellationToken) =>
            Task.FromException(new IOException("synthetic flush failure"));

        public override void Flush() => throw new IOException("synthetic flush failure");
    }

    private sealed class CorruptingWriteStream(Stream innerStream) : DelegatingStream(innerStream)
    {
        public override void Write(byte[] buffer, int offset, int count)
        {
            byte[] copy = buffer[offset..(offset + count)];
            if (copy.Length > 0)
            {
                copy[0] ^= 0xFF;
            }

            InnerStream.Write(copy, 0, copy.Length);
        }

        public override async ValueTask WriteAsync(
            ReadOnlyMemory<byte> buffer,
            CancellationToken cancellationToken = default)
        {
            byte[] copy = buffer.ToArray();
            if (copy.Length > 0)
            {
                copy[0] ^= 0xFF;
            }

            await InnerStream.WriteAsync(copy, cancellationToken);
        }
    }

    private abstract class DelegatingStream(Stream innerStream) : Stream
    {
        protected Stream InnerStream { get; } = innerStream;

        public override bool CanRead => InnerStream.CanRead;

        public override bool CanSeek => InnerStream.CanSeek;

        public override bool CanWrite => InnerStream.CanWrite;

        public override long Length => InnerStream.Length;

        public override long Position
        {
            get => InnerStream.Position;
            set => InnerStream.Position = value;
        }

        public override void Flush() => InnerStream.Flush();

        public override int Read(byte[] buffer, int offset, int count) =>
            InnerStream.Read(buffer, offset, count);

        public override long Seek(long offset, SeekOrigin origin) =>
            InnerStream.Seek(offset, origin);

        public override void SetLength(long value) => InnerStream.SetLength(value);

        public override void Write(byte[] buffer, int offset, int count) =>
            InnerStream.Write(buffer, offset, count);

        public override Task<int> ReadAsync(
            byte[] buffer,
            int offset,
            int count,
            CancellationToken cancellationToken) =>
            InnerStream.ReadAsync(buffer, offset, count, cancellationToken);

        public override ValueTask<int> ReadAsync(
            Memory<byte> buffer,
            CancellationToken cancellationToken = default) =>
            InnerStream.ReadAsync(buffer, cancellationToken);

        public override Task FlushAsync(CancellationToken cancellationToken) =>
            InnerStream.FlushAsync(cancellationToken);

        public override async ValueTask DisposeAsync()
        {
            await InnerStream.DisposeAsync();
            await base.DisposeAsync();
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                InnerStream.Dispose();
            }

            base.Dispose(disposing);
        }
    }
}
