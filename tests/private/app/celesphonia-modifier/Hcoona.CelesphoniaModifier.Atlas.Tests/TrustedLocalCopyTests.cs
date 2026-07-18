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
    public async Task CopyAsyncFailsClosedAfterBeforeRenameFailure()
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

        await Assert.ThrowsAsync<AtlasSafetyException>(
            () => TrustedLocalCopy.CopyAsync(
                workspace.Layout.CanonicalCopyRequestPath,
                TestContext.Current.CancellationToken).AsTask());
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
    public async Task HasCompleteCopySetRejectsReparseEntryViaInjectedSeam()
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
        AtlasIoSeams io = AtlasTestSupport.CreateIo(
            enumerateFileSystemEntries: (path, searchOption) =>
                AtlasIoSeams.Default.EnumerateFileSystemEntries(path, searchOption)
                    .Append(injectedPath),
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
}
