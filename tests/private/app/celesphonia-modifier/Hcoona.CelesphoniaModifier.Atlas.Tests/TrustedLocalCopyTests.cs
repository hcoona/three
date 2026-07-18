using Hcoona.CelesphoniaModifier.Atlas;
using Xunit;

namespace Hcoona.CelesphoniaModifier.Atlas.Tests;

public sealed class TrustedLocalCopyTests
{
    [Fact]
    public async Task CopyAsyncCreatesQualifiedSnapshotReceiptAndState()
    {
        await using AtlasSyntheticWorkspace workspace = await AtlasSyntheticWorkspace.CreateAsync();
        await AtlasDiscovery.DiscoverAsync(
            workspace.Layout.CanonicalDiscoverRequestPath,
            TestContext.Current.CancellationToken);
        workspace.WriteRequest(workspace.CreateConfirmationRequest());
        await AtlasDiscovery.ConfirmAsync(
            workspace.Layout.CanonicalConfirmRequestPath,
            TestContext.Current.CancellationToken);
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

        Assert.Equal(3, receipt.Document.SaveCount);
        Assert.Equal(2, receipt.Document.DefinitionCount);
        Assert.Equal(5, receipt.Document.Entries.Length);
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
        await AtlasDiscovery.DiscoverAsync(
            workspace.Layout.CanonicalDiscoverRequestPath,
            TestContext.Current.CancellationToken);
        workspace.WriteRequest(workspace.CreateConfirmationRequest());
        await AtlasDiscovery.ConfirmAsync(
            workspace.Layout.CanonicalConfirmRequestPath,
            TestContext.Current.CancellationToken);
        AtlasIntakeCopyRequest request = workspace.CreateCopyRequest();
        workspace.WriteRequest(request);
        await TrustedLocalCopy.CopyAsync(
            workspace.Layout.CanonicalCopyRequestPath,
            TestContext.Current.CancellationToken);

        File.Delete(workspace.Layout.CanonicalQualifiedStatePath);
        int liveSourceOpenCount = 0;
        AtlasIoSeams io = new()
        {
            ReadAllBytesAsync = AtlasIoSeams.Default.ReadAllBytesAsync,
            ReadAllText = AtlasIoSeams.Default.ReadAllText,
            FileExists = AtlasIoSeams.Default.FileExists,
            DirectoryExists = AtlasIoSeams.Default.DirectoryExists,
            GetAttributes = AtlasIoSeams.Default.GetAttributes,
            GetDriveInfo = AtlasIoSeams.Default.GetDriveInfo,
            EnumerateFileSystemEntries = AtlasIoSeams.Default.EnumerateFileSystemEntries,
            CreateDirectory = AtlasIoSeams.Default.CreateDirectory,
            MoveFile = AtlasIoSeams.Default.MoveFile,
            MoveDirectory = AtlasIoSeams.Default.MoveDirectory,
            ReplaceFile = AtlasIoSeams.Default.ReplaceFile,
            DeleteDirectory = AtlasIoSeams.Default.DeleteDirectory,
            SetAttributes = AtlasIoSeams.Default.SetAttributes,
            GetLength = AtlasIoSeams.Default.GetLength,
            GetLastWriteTimeUtc = AtlasIoSeams.Default.GetLastWriteTimeUtc,
            OpenFile = (path, mode, access, share, options) =>
            {
                if (mode == FileMode.Open
                    && access == FileAccess.Read
                    && (AtlasDiscovery.ContainsPath(workspace.SaveRootPath, path)
                        || AtlasDiscovery.ContainsPath(workspace.DefinitionRootPath, path)))
                {
                    liveSourceOpenCount++;
                }

                return AtlasIoSeams.Default.OpenFile(path, mode, access, share, options);
            },
        };

        await TrustedLocalCopy.CopyAsync(
            workspace.Layout.CanonicalCopyRequestPath,
            io,
            TestContext.Current.CancellationToken);

        Assert.Equal(0, liveSourceOpenCount);
        Assert.True(File.Exists(workspace.Layout.CanonicalQualifiedStatePath));
    }
}
