using System.Text;
using System.Text.Json.Nodes;
using Hcoona.CelesphoniaModifier.Atlas;
using Xunit;

namespace Hcoona.CelesphoniaModifier.Atlas.Tests;

public sealed class AtlasDiscoveryTests
{
    [Fact]
    public async Task DiscoverAsyncPublishesPendingManifestRootMapCopyPlanAndState()
    {
        await using AtlasSyntheticWorkspace workspace = await AtlasSyntheticWorkspace.CreateAsync();

        await AtlasDiscovery.DiscoverAsync(
            workspace.Layout.CanonicalDiscoverRequestPath,
            TestContext.Current.CancellationToken);

        AtlasLoadedDocument<AtlasCorpusIntakeManifest> pendingManifest =
            await AtlasIntakeContracts.ReadManifestAsync(
                workspace.Layout.CanonicalPendingManifestPath,
                TestContext.Current.CancellationToken);
        AtlasLoadedDocument<AtlasSourceRootMapDocument> rootMap =
            await AtlasIntakeContracts.ReadSourceRootMapAsync(
                workspace.Layout.CanonicalSourceRootMapPath,
                TestContext.Current.CancellationToken);
        AtlasLoadedDocument<AtlasCopyPlanDocument> copyPlan =
            await AtlasIntakeContracts.ReadCopyPlanAsync(
                workspace.Layout.CanonicalCopyPlanPath,
                TestContext.Current.CancellationToken);
        AtlasLoadedDocument<AtlasIntakeStateDocument> state =
            await AtlasIntakeContracts.ReadStateAsync(
                workspace.Layout.CanonicalDiscoveredStatePath,
                TestContext.Current.CancellationToken);
        AtlasLoadedDocument<AtlasPrivateArtifactInventoryDocument> inventory =
            await AtlasIntakeContracts.ReadInventoryAsync(
                workspace.Layout.CanonicalInventoryPath,
                TestContext.Current.CancellationToken);

        Assert.Equal(
            AtlasIntakeContracts.PendingManifestRevision,
            pendingManifest.Document.ManifestRevision);
        Assert.Equal(
            AtlasIntakeContracts.PendingConfirmationStatus,
            pendingManifest.Document.Confirmation.Status);
        Assert.Equal(
            AtlasIntakeContracts.AtlasToolValidationMethod,
            pendingManifest.Document.Validation.Method);
        Assert.Equal(2, rootMap.Document.SaveRoots.Length);
        Assert.Equal(
            AtlasIntakeContracts.ExactIncludedSaveCount
            + AtlasIntakeContracts.ExactIncludedDefinitionCount,
            copyPlan.Document.Entries.Length);
        Assert.Equal(AtlasIntakeContracts.DiscoveredPhase, state.Document.Phase);
        Assert.Contains(
            inventory.Document.Artifacts,
            artifact => artifact.Purpose == AtlasIntakeContracts.ManifestRevision4Purpose);
        Assert.Contains(
            inventory.Document.Artifacts,
            artifact => artifact.Purpose == AtlasIntakeContracts.SourceRootMapPurpose);
        Assert.Contains(
            inventory.Document.Artifacts,
            artifact => artifact.Purpose == AtlasIntakeContracts.CopyPlanPurpose);
        Assert.True(File.Exists(workspace.Layout.CanonicalDiscoveredInventoryBackupPath));
    }

    [Fact]
    public async Task ConfirmAsyncPublishesApprovedManifestAndState()
    {
        await using AtlasSyntheticWorkspace workspace = await AtlasSyntheticWorkspace.CreateAsync();
        await AtlasDiscovery.DiscoverAsync(
            workspace.Layout.CanonicalDiscoverRequestPath,
            TestContext.Current.CancellationToken);
        AtlasIntakeConfirmationRequest request = workspace.CreateConfirmationRequest();
        workspace.WriteRequest(request);

        await AtlasDiscovery.ConfirmAsync(
            workspace.Layout.CanonicalConfirmRequestPath,
            TestContext.Current.CancellationToken);

        AtlasLoadedDocument<AtlasCorpusIntakeManifest> approvedManifest =
            await AtlasIntakeContracts.ReadManifestAsync(
                workspace.Layout.CanonicalApprovedManifestPath,
                TestContext.Current.CancellationToken);
        AtlasLoadedDocument<AtlasIntakeStateDocument> state =
            await AtlasIntakeContracts.ReadStateAsync(
                workspace.Layout.CanonicalApprovedStatePath,
                TestContext.Current.CancellationToken);
        AtlasLoadedDocument<AtlasPrivateArtifactInventoryDocument> inventory =
            await AtlasIntakeContracts.ReadInventoryAsync(
                workspace.Layout.CanonicalInventoryPath,
                TestContext.Current.CancellationToken);

        Assert.Equal(
            AtlasIntakeContracts.ApprovedManifestRevision,
            approvedManifest.Document.ManifestRevision);
        Assert.Equal(
            AtlasIntakeContracts.ApprovedConfirmationStatus,
            approvedManifest.Document.Confirmation.Status);
        Assert.Equal(AtlasIntakeContracts.ApprovedPhase, state.Document.Phase);
        Assert.Equal(request.DecisionCommit, state.Document.DecisionCommit);
        Assert.Contains(
            inventory.Document.Artifacts,
            artifact => artifact.Purpose == AtlasIntakeContracts.ManifestRevision5Purpose);
        Assert.Contains(
            inventory.Document.Artifacts,
            artifact => artifact.Purpose == AtlasIntakeContracts.State2Purpose);
        Assert.True(File.Exists(workspace.Layout.CanonicalApprovedInventoryBackupPath));
    }

    [Fact]
    public async Task DiscoverAsyncRejectsChangedSaveDenominator()
    {
        await using AtlasSyntheticWorkspace workspace = await AtlasSyntheticWorkspace.CreateAsync();
        await File.WriteAllTextAsync(
            Path.Combine(workspace.SaveRootPath, "unexpected.json"),
            "{}",
            TestContext.Current.CancellationToken);

        AtlasSafetyException exception = await Assert.ThrowsAsync<AtlasSafetyException>(
            () => AtlasDiscovery.DiscoverAsync(
                workspace.Layout.CanonicalDiscoverRequestPath,
                TestContext.Current.CancellationToken).AsTask());

        Assert.Contains("denominator", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task DiscoverAsyncRejectsChangedDefinitionDenominator()
    {
        await using AtlasSyntheticWorkspace workspace = await AtlasSyntheticWorkspace.CreateAsync();
        File.Delete(Path.Combine(
            workspace.DefinitionRootPath,
            "www",
            "data",
            "definition-000001.json"));

        AtlasSafetyException exception = await Assert.ThrowsAsync<AtlasSafetyException>(
            () => AtlasDiscovery.DiscoverAsync(
                workspace.Layout.CanonicalDiscoverRequestPath,
                TestContext.Current.CancellationToken).AsTask());

        Assert.Contains("denominator", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task DiscoverAsyncRejectsUnexpectedSurveyAliasInBaselineManifest()
    {
        await using AtlasSyntheticWorkspace workspace = await AtlasSyntheticWorkspace.CreateAsync();
        JsonObject baselineManifest = await AtlasTestSupport.LoadJsonObjectAsync(
            workspace.Layout.CanonicalBaselineManifestPath,
            TestContext.Current.CancellationToken);
        baselineManifest["surveyAlias"] = "survey-999999";
        await AtlasTestSupport.WriteJsonAsync(
            workspace.Layout.CanonicalBaselineManifestPath,
            baselineManifest,
            TestContext.Current.CancellationToken);
        workspace.WriteRequest(workspace.CreateDiscoveryRequest());

        await Assert.ThrowsAsync<AtlasApprovalException>(
            () => AtlasDiscovery.DiscoverAsync(
                workspace.Layout.CanonicalDiscoverRequestPath,
                TestContext.Current.CancellationToken).AsTask());
    }

    [Fact]
    public async Task EnsureDeterministicFileAsyncRejectsUnauthorizedManifestBytes()
    {
        await using AtlasSyntheticWorkspace workspace = await AtlasSyntheticWorkspace.CreateAsync();
        JsonObject baselineManifest = await AtlasTestSupport.LoadJsonObjectAsync(
            workspace.Layout.CanonicalBaselineManifestPath,
            TestContext.Current.CancellationToken);
        baselineManifest["includedDefinitionCount"] =
            AtlasIntakeContracts.ExactIncludedDefinitionCount - 1;
        byte[] invalidBytes = Encoding.UTF8.GetBytes(baselineManifest.ToJsonString());
        string finalPath = Path.Combine(
            workspace.Layout.ManifestRevisionDirectory,
            "manifest-revision-000099.json");

        await Assert.ThrowsAsync<AtlasApprovalException>(
            () => AtlasDiscovery.EnsureDeterministicFileAsync(
                finalPath,
                AtlasIntakeContracts.DiscoveredPhase,
                invalidBytes,
                AtlasDiscovery.ReadManifestShaAsync,
                AtlasIoSeams.Default,
                TestContext.Current.CancellationToken).AsTask());

        Assert.False(File.Exists(finalPath));
    }

    [Fact]
    public async Task EnsureInventoryReplaceAsyncRejectsInvalidReplacementBytes()
    {
        await using AtlasSyntheticWorkspace workspace = await AtlasSyntheticWorkspace.CreateAsync();
        byte[] priorBytes = await File.ReadAllBytesAsync(
            workspace.Layout.CanonicalInventoryPath,
            TestContext.Current.CancellationToken);
        JsonObject inventory = await AtlasTestSupport.LoadJsonObjectAsync(
            workspace.Layout.CanonicalInventoryPath,
            TestContext.Current.CancellationToken);
        ((JsonObject)((JsonArray)inventory["artifacts"]!)[0]!)["expiryCondition"] = "";
        byte[] invalidBytes = Encoding.UTF8.GetBytes(inventory.ToJsonString());

        await Assert.ThrowsAsync<AtlasSafetyException>(
            () => AtlasDiscovery.EnsureInventoryReplaceAsync(
                workspace.Layout.CanonicalInventoryPath,
                workspace.Layout.CanonicalDiscoveredInventoryBackupPath,
                AtlasIntakeContracts.DiscoveredPhase,
                priorBytes,
                invalidBytes,
                AtlasIoSeams.Default,
                TestContext.Current.CancellationToken).AsTask());

        Assert.Equal(
            priorBytes,
            await File.ReadAllBytesAsync(
                workspace.Layout.CanonicalInventoryPath,
                TestContext.Current.CancellationToken));
        Assert.False(File.Exists(workspace.Layout.CanonicalDiscoveredInventoryBackupPath));
    }

    [Fact]
    public async Task DiscoverAsyncRecoversAfterInventoryReplacementBeforeStatePublication()
    {
        await using AtlasSyntheticWorkspace workspace = await AtlasSyntheticWorkspace.CreateAsync();
        bool failed = false;
        AtlasIoSeams failingIo = AtlasTestSupport.CreateIo(
            moveFile: (source, destination) =>
            {
                if (!failed
                    && AtlasIntakeContracts.PathEquals(
                        destination,
                        workspace.Layout.CanonicalDiscoveredStatePath))
                {
                    failed = true;
                    throw new IOException("synthetic discovery state publication failure");
                }

                AtlasIoSeams.Default.MoveFile(source, destination);
            });

        await Assert.ThrowsAsync<IOException>(
            () => AtlasDiscovery.DiscoverAsync(
                workspace.Layout.CanonicalDiscoverRequestPath,
                failingIo,
                TestContext.Current.CancellationToken).AsTask());

        AtlasLoadedDocument<AtlasPrivateArtifactInventoryDocument> afterFailure =
            await AtlasIntakeContracts.ReadInventoryAsync(
                workspace.Layout.CanonicalInventoryPath,
                TestContext.Current.CancellationToken);
        string[] expectedAliases = afterFailure.Document.Artifacts
            .Select(static artifact => artifact.ArtifactAlias)
            .OrderBy(static alias => alias, StringComparer.Ordinal)
            .ToArray();

        await AtlasDiscovery.DiscoverAsync(
            workspace.Layout.CanonicalDiscoverRequestPath,
            TestContext.Current.CancellationToken);

        AtlasLoadedDocument<AtlasIntakeStateDocument> recoveredState =
            await AtlasIntakeContracts.ReadStateAsync(
                workspace.Layout.CanonicalDiscoveredStatePath,
                TestContext.Current.CancellationToken);
        AtlasLoadedDocument<AtlasPrivateArtifactInventoryDocument> recoveredInventory =
            await AtlasIntakeContracts.ReadInventoryAsync(
                workspace.Layout.CanonicalInventoryPath,
                TestContext.Current.CancellationToken);

        Assert.Equal(AtlasIntakeContracts.DiscoveredPhase, recoveredState.Document.Phase);
        Assert.Equal(
            expectedAliases,
            recoveredInventory.Document.Artifacts
                .Select(static artifact => artifact.ArtifactAlias)
                .OrderBy(static alias => alias, StringComparer.Ordinal)
                .ToArray());
    }

    [Fact]
    public async Task ConfirmAsyncRecoversAfterInventoryReplacementBeforeStatePublication()
    {
        await using AtlasSyntheticWorkspace workspace = await AtlasSyntheticWorkspace.CreateAsync();
        await AtlasDiscovery.DiscoverAsync(
            workspace.Layout.CanonicalDiscoverRequestPath,
            TestContext.Current.CancellationToken);
        workspace.WriteRequest(workspace.CreateConfirmationRequest());
        bool failed = false;
        AtlasIoSeams failingIo = AtlasTestSupport.CreateIo(
            moveFile: (source, destination) =>
            {
                if (!failed
                    && AtlasIntakeContracts.PathEquals(
                        destination,
                        workspace.Layout.CanonicalApprovedStatePath))
                {
                    failed = true;
                    throw new IOException("synthetic confirmation state publication failure");
                }

                AtlasIoSeams.Default.MoveFile(source, destination);
            });

        await Assert.ThrowsAsync<IOException>(
            () => AtlasDiscovery.ConfirmAsync(
                workspace.Layout.CanonicalConfirmRequestPath,
                failingIo,
                TestContext.Current.CancellationToken).AsTask());

        AtlasLoadedDocument<AtlasPrivateArtifactInventoryDocument> afterFailure =
            await AtlasIntakeContracts.ReadInventoryAsync(
                workspace.Layout.CanonicalInventoryPath,
                TestContext.Current.CancellationToken);
        string[] expectedAliases = afterFailure.Document.Artifacts
            .Select(static artifact => artifact.ArtifactAlias)
            .OrderBy(static alias => alias, StringComparer.Ordinal)
            .ToArray();

        await AtlasDiscovery.ConfirmAsync(
            workspace.Layout.CanonicalConfirmRequestPath,
            TestContext.Current.CancellationToken);

        AtlasLoadedDocument<AtlasIntakeStateDocument> recoveredState =
            await AtlasIntakeContracts.ReadStateAsync(
                workspace.Layout.CanonicalApprovedStatePath,
                TestContext.Current.CancellationToken);
        AtlasLoadedDocument<AtlasPrivateArtifactInventoryDocument> recoveredInventory =
            await AtlasIntakeContracts.ReadInventoryAsync(
                workspace.Layout.CanonicalInventoryPath,
                TestContext.Current.CancellationToken);

        Assert.Equal(AtlasIntakeContracts.ApprovedPhase, recoveredState.Document.Phase);
        Assert.Equal(
            expectedAliases,
            recoveredInventory.Document.Artifacts
                .Select(static artifact => artifact.ArtifactAlias)
                .OrderBy(static alias => alias, StringComparer.Ordinal)
                .ToArray());
    }

    [Fact]
    public async Task DiscoverAsyncRejectsAmbiguousRecoveredAlias()
    {
        await using AtlasSyntheticWorkspace workspace = await AtlasSyntheticWorkspace.CreateAsync();
        await AtlasDiscovery.DiscoverAsync(
            workspace.Layout.CanonicalDiscoverRequestPath,
            TestContext.Current.CancellationToken);
        File.Delete(workspace.Layout.CanonicalDiscoveredStatePath);
        workspace.UpdateInventory(inventory => inventory with
        {
            Artifacts =
            [
                .. inventory.Artifacts,
                new AtlasPrivateArtifactEntry
                {
                    ArtifactAlias = "private-artifact-999999",
                    ArtifactClass = AtlasIntakeContracts.PrivateProvenanceArtifactClass,
                    Purpose = AtlasIntakeContracts.ManifestRevision4Purpose,
                    CustodianRole = AtlasIntakeContracts.ProjectLeaderRole,
                    LineageAliases = [AtlasSyntheticWorkspace.BaselineManifestArtifactAlias],
                    LastUseMilestone = "A8",
                    ExpiryCondition = "after:A8",
                    PlannedDisposition = AtlasIntakeContracts.RetainPrivateDisposition,
                    Status = AtlasIntakeContracts.PresentArtifactStatus,
                    VerificationMethod = AtlasIntakeContracts.IntakeManifestSchemaVersion,
                },
            ],
        });

        await Assert.ThrowsAsync<AtlasSafetyException>(
            () => AtlasDiscovery.DiscoverAsync(
                workspace.Layout.CanonicalDiscoverRequestPath,
                TestContext.Current.CancellationToken).AsTask());
    }

    [Fact]
    public async Task DiscoverAsyncRejectsState1WhenInventoryBackupIsMissing()
    {
        await using AtlasSyntheticWorkspace workspace = await AtlasSyntheticWorkspace.CreateAsync();
        await AtlasDiscovery.DiscoverAsync(
            workspace.Layout.CanonicalDiscoverRequestPath,
            TestContext.Current.CancellationToken);
        File.Delete(workspace.Layout.CanonicalDiscoveredInventoryBackupPath);

        await Assert.ThrowsAsync<FileNotFoundException>(
            () => AtlasDiscovery.DiscoverAsync(
                workspace.Layout.CanonicalDiscoverRequestPath,
                TestContext.Current.CancellationToken).AsTask());
    }

    [Fact]
    public async Task ConfirmAsyncRejectsState2WhenDiscoveredStateIsMissing()
    {
        await using AtlasSyntheticWorkspace workspace = await AtlasSyntheticWorkspace.CreateAsync();
        await AtlasDiscovery.DiscoverAsync(
            workspace.Layout.CanonicalDiscoverRequestPath,
            TestContext.Current.CancellationToken);
        workspace.WriteRequest(workspace.CreateConfirmationRequest());
        await AtlasDiscovery.ConfirmAsync(
            workspace.Layout.CanonicalConfirmRequestPath,
            TestContext.Current.CancellationToken);
        File.Delete(workspace.Layout.CanonicalDiscoveredStatePath);

        await Assert.ThrowsAsync<AtlasSafetyException>(
            () => AtlasDiscovery.ConfirmAsync(
                workspace.Layout.CanonicalConfirmRequestPath,
                TestContext.Current.CancellationToken).AsTask());
    }
}
