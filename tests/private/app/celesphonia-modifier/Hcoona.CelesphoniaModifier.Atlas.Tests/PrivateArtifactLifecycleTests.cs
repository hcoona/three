using Hcoona.CelesphoniaModifier.Atlas;
using Xunit;

namespace Hcoona.CelesphoniaModifier.Atlas.Tests;

public sealed class PrivateArtifactLifecycleTests
{
    [Fact]
    public void EvaluateLifecycleResultUsesExpectedPrecedence()
    {
        AtlasPrivateArtifactEntry baseEntry = new()
        {
            ArtifactAlias = "private-artifact-000001",
            ArtifactClass = AtlasIntakeContracts.SaveCopyArtifactClass,
            Purpose = "snapshot-copy:save-source-0001",
            CustodianRole = AtlasIntakeContracts.ProjectLeaderRole,
            LineageAliases = ["private-artifact-000010"],
            LastUseMilestone = "A8",
            ExpiryCondition = "after:A8",
            PlannedDisposition = AtlasIntakeContracts.DeleteDisposition,
            Status = AtlasIntakeContracts.PresentArtifactStatus,
            VerificationMethod = AtlasIntakeContracts.TrustedLocalFilesystemProfile,
        };

        Assert.Equal(
            "blocked-status",
            PrivateArtifactLifecycle.EvaluateLifecycleResult(baseEntry, "A8"));
        Assert.Equal(
            "blocked-disposition",
            PrivateArtifactLifecycle.EvaluateLifecycleResult(
                baseEntry with
                {
                    Status = AtlasIntakeContracts.LastUseCompleteArtifactStatus,
                    PlannedDisposition = AtlasIntakeContracts.RetainPrivateDisposition,
                },
                "A8"));
        Assert.Equal(
            "blocked-before-last-use",
            PrivateArtifactLifecycle.EvaluateLifecycleResult(
                baseEntry with
                {
                    Status = AtlasIntakeContracts.LastUseCompleteArtifactStatus,
                },
                "A7"));
        Assert.Equal(
            "indeterminate-expiry",
            PrivateArtifactLifecycle.EvaluateLifecycleResult(
                baseEntry with
                {
                    Status = AtlasIntakeContracts.LastUseCompleteArtifactStatus,
                    ExpiryCondition = "later",
                },
                "A8"));
        Assert.Equal(
            "eligible-for-human-review",
            PrivateArtifactLifecycle.EvaluateLifecycleResult(
                baseEntry with
                {
                    Status = AtlasIntakeContracts.LastUseCompleteArtifactStatus,
                },
                "A8"));
    }

    [Fact]
    public async Task CleanupPreflightPublishesReportAndState4()
    {
        await using AtlasSyntheticWorkspace workspace = await AtlasSyntheticWorkspace.CreateAsync();
        await AtlasDiscovery.DiscoverAsync(
            workspace.Layout.CanonicalDiscoverRequestPath,
            TestContext.Current.CancellationToken);
        workspace.WriteRequest(workspace.CreateConfirmationRequest());
        await AtlasDiscovery.ConfirmAsync(
            workspace.Layout.CanonicalConfirmRequestPath,
            TestContext.Current.CancellationToken);
        workspace.WriteRequest(workspace.CreateCopyRequest());
        await TrustedLocalCopy.CopyAsync(
            workspace.Layout.CanonicalCopyRequestPath,
            TestContext.Current.CancellationToken);

        AtlasCleanupPreflightRequest request = workspace.CreatePreflightRequest();
        workspace.WriteRequest(request);

        await PrivateArtifactLifecycle.CleanupPreflightAsync(
            workspace.Layout.CanonicalCleanupPreflightRequestPath,
            TestContext.Current.CancellationToken);

        AtlasLoadedDocument<AtlasCleanupPreflightReportDocument> report =
            await AtlasIntakeContracts.ReadCleanupPreflightReportAsync(
                workspace.Layout.CanonicalCleanupPreflightReportPath,
                TestContext.Current.CancellationToken);
        AtlasLoadedDocument<AtlasIntakeStateDocument> state =
            await AtlasIntakeContracts.ReadStateAsync(
                workspace.Layout.CanonicalPreflightedStatePath,
                TestContext.Current.CancellationToken);
        AtlasLoadedDocument<AtlasPrivateArtifactInventoryDocument> inventory =
            await AtlasIntakeContracts.ReadInventoryAsync(
                workspace.Layout.CanonicalInventoryPath,
                TestContext.Current.CancellationToken);

        Assert.Equal(AtlasIntakeContracts.PreflightedPhase, state.Document.Phase);
        Assert.Contains(
            report.Document.Results,
            result => result.Result == "blocked-status");
        Assert.Contains(
            inventory.Document.Artifacts,
            artifact => artifact.Purpose == AtlasIntakeContracts.CleanupPreflightReportPurpose);
        Assert.Contains(
            inventory.Document.Artifacts,
            artifact => artifact.Purpose == AtlasIntakeContracts.State4Purpose);
        Assert.True(File.Exists(workspace.Layout.CanonicalPreflightedInventoryBackupPath));
    }
}
