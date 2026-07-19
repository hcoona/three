using System.Text;
using System.Text.Json.Nodes;
using Hcoona.CelesphoniaModifier.Atlas;
using Xunit;

namespace Hcoona.CelesphoniaModifier.Atlas.Tests;

public sealed class PrivateArtifactLifecycleTests
{
    public static TheoryData<string, string, string, string, string, string> CleanupResultCases
    {
        get
        {
            TheoryData<string, string, string, string, string, string> data = [];
            data.Add(
                AtlasIntakeContracts.PresentArtifactStatus,
                AtlasIntakeContracts.DeleteDisposition,
                "A8",
                "after:A8",
                "A8",
                "blocked-status");
            data.Add(
                AtlasIntakeContracts.LastUseCompleteArtifactStatus,
                AtlasIntakeContracts.RetainPrivateDisposition,
                "A8",
                "after:A8",
                "A8",
                "blocked-disposition");
            data.Add(
                AtlasIntakeContracts.LastUseCompleteArtifactStatus,
                AtlasIntakeContracts.DeleteDisposition,
                "A8",
                "after:A8",
                "A7",
                "blocked-before-last-use");
            data.Add(
                AtlasIntakeContracts.LastUseCompleteArtifactStatus,
                AtlasIntakeContracts.DeleteDisposition,
                "A8",
                "later",
                "A8",
                "indeterminate-expiry");
            data.Add(
                AtlasIntakeContracts.LastUseCompleteArtifactStatus,
                AtlasIntakeContracts.DeleteDisposition,
                "A8",
                "after:A8",
                "A8",
                "eligible-for-human-review");
            data.Add(
                AtlasIntakeContracts.PresentArtifactStatus,
                AtlasIntakeContracts.RetainPrivateDisposition,
                "A8",
                "later",
                "A7",
                "blocked-status");
            data.Add(
                AtlasIntakeContracts.LastUseCompleteArtifactStatus,
                AtlasIntakeContracts.RetainPrivateDisposition,
                "A8",
                "later",
                "A7",
                "blocked-disposition");
            data.Add(
                AtlasIntakeContracts.LastUseCompleteArtifactStatus,
                AtlasIntakeContracts.DeleteDisposition,
                "A8",
                "later",
                "A7",
                "blocked-before-last-use");
            return data;
        }
    }

    [Theory]
    [MemberData(nameof(CleanupResultCases))]
    public void EvaluateLifecycleResultUsesExpectedPrecedence(
        string status,
        string plannedDisposition,
        string lastUseMilestone,
        string expiryCondition,
        string proposedMilestone,
        string expectedResult)
    {
        AtlasPrivateArtifactEntry baseEntry = new()
        {
            ArtifactAlias = "private-artifact-000001",
            ArtifactClass = AtlasIntakeContracts.SaveCopyArtifactClass,
            Purpose = "snapshot-copy:save-source-0001",
            CustodianRole = AtlasIntakeContracts.ProjectLeaderRole,
            LineageAliases = ["private-artifact-000010"],
            LastUseMilestone = lastUseMilestone,
            ExpiryCondition = expiryCondition,
            PlannedDisposition = plannedDisposition,
            Status = status,
            VerificationMethod = AtlasIntakeContracts.TrustedLocalFilesystemProfile,
        };

        Assert.Equal(
            expectedResult,
            PrivateArtifactLifecycle.EvaluateLifecycleResult(baseEntry, proposedMilestone));
    }

    [Fact]
    public async Task CleanupPreflightPublishesReportAndState4()
    {
        await using AtlasSyntheticWorkspace workspace = await AtlasSyntheticWorkspace.CreateAsync();
        await PrepareQualifiedWorkspaceAsync(workspace);

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
        AtlasPrivateArtifactEntry reportArtifact = inventory.Document.Artifacts.Single(
            artifact => artifact.Purpose == AtlasIntakeContracts.CleanupPreflightReportPurpose);
        AtlasPrivateArtifactEntry state4Artifact = inventory.Document.Artifacts.Single(
            artifact => artifact.Purpose == AtlasIntakeContracts.State4Purpose);
        AtlasPrivateArtifactEntry state3Artifact = inventory.Document.Artifacts.Single(
            artifact => artifact.Purpose == AtlasIntakeContracts.State3Purpose);
        AtlasPrivateArtifactEntry backupArtifact = inventory.Document.Artifacts.Single(
            artifact => artifact.Purpose == AtlasIntakeContracts.PreflightInventoryBackupPurpose);
        Assert.Equal(
            [
                state3Artifact.ArtifactAlias,
                reportArtifact.ArtifactAlias,
                backupArtifact.ArtifactAlias,
            ],
            state4Artifact.LineageAliases);
        Assert.True(File.Exists(workspace.Layout.CanonicalPreflightedInventoryBackupPath));
    }

    [Fact]
    public async Task CleanupPreflightCompletedRerunReturnsWhenLiveSourcesAreMissing()
    {
        await using AtlasSyntheticWorkspace workspace = await AtlasSyntheticWorkspace.CreateAsync();
        await PrepareQualifiedWorkspaceAsync(workspace);
        workspace.WriteRequest(workspace.CreatePreflightRequest());
        await PrivateArtifactLifecycle.CleanupPreflightAsync(
            workspace.Layout.CanonicalCleanupPreflightRequestPath,
            TestContext.Current.CancellationToken);
        Directory.Delete(workspace.GameRootPath, recursive: true);

        await PrivateArtifactLifecycle.CleanupPreflightAsync(
            workspace.Layout.CanonicalCleanupPreflightRequestPath,
            TestContext.Current.CancellationToken);

        Assert.True(File.Exists(workspace.Layout.CanonicalPreflightedStatePath));
    }

    [Fact]
    public async Task CleanupPreflightCompletedRerunReturnsWithoutLiveSourceAccess()
    {
        await using AtlasSyntheticWorkspace workspace = await AtlasSyntheticWorkspace.CreateAsync();
        await PrepareQualifiedWorkspaceAsync(workspace);
        workspace.WriteRequest(workspace.CreatePreflightRequest());
        await PrivateArtifactLifecycle.CleanupPreflightAsync(
            workspace.Layout.CanonicalCleanupPreflightRequestPath,
            TestContext.Current.CancellationToken);

        await PrivateArtifactLifecycle.CleanupPreflightAsync(
            workspace.Layout.CanonicalCleanupPreflightRequestPath,
            AtlasTestSupport.CreateLiveSourceAccessThrowingIo(workspace),
            TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task CleanupReportRejectsContradictoryResults()
    {
        await using AtlasSyntheticWorkspace workspace = await AtlasSyntheticWorkspace.CreateAsync();
        await PrepareQualifiedWorkspaceAsync(workspace);
        workspace.WriteRequest(workspace.CreatePreflightRequest());
        await PrivateArtifactLifecycle.CleanupPreflightAsync(
            workspace.Layout.CanonicalCleanupPreflightRequestPath,
            TestContext.Current.CancellationToken);

        await AssertRejectedCleanupReportMutationAsync(
            workspace.Layout.CanonicalCleanupPreflightReportPath,
            json =>
                ((JsonObject)((JsonArray)json["results"]!)[0]!)["result"] =
                    "eligible-for-human-review");
    }

    [Fact]
    public async Task CleanupPreflightAsyncRecoversAfterReportPublication()
    {
        await using AtlasSyntheticWorkspace workspace = await AtlasSyntheticWorkspace.CreateAsync();
        await PrepareQualifiedWorkspaceAsync(workspace);
        workspace.WriteRequest(workspace.CreatePreflightRequest());
        bool failed = false;
        AtlasIoSeams failingIo = AtlasTestSupport.CreateIo(
            replaceFile: (source, destination, backup) =>
            {
                if (!failed
                    && AtlasIntakeContracts.PathEquals(
                        destination,
                        workspace.Layout.CanonicalInventoryPath))
                {
                    failed = true;
                    throw new IOException("synthetic preflight replace failure");
                }

                AtlasIoSeams.Default.ReplaceFile(source, destination, backup);
            });

        await Assert.ThrowsAsync<IOException>(
            () => PrivateArtifactLifecycle.CleanupPreflightAsync(
                workspace.Layout.CanonicalCleanupPreflightRequestPath,
                failingIo,
                TestContext.Current.CancellationToken).AsTask());

        Assert.True(File.Exists(workspace.Layout.CanonicalCleanupPreflightReportPath));
        Assert.False(File.Exists(workspace.Layout.CanonicalPreflightedStatePath));

        await PrivateArtifactLifecycle.CleanupPreflightAsync(
            workspace.Layout.CanonicalCleanupPreflightRequestPath,
            TestContext.Current.CancellationToken);

        Assert.True(File.Exists(workspace.Layout.CanonicalPreflightedStatePath));
    }

    [Fact]
    public async Task CleanupPreflightAsyncRecoversAfterInventoryReplacementBeforeStatePublication()
    {
        await using AtlasSyntheticWorkspace workspace = await AtlasSyntheticWorkspace.CreateAsync();
        await PrepareQualifiedWorkspaceAsync(workspace);
        workspace.WriteRequest(workspace.CreatePreflightRequest());
        bool failed = false;
        AtlasIoSeams failingIo = AtlasTestSupport.CreateIo(
            moveFile: (source, destination) =>
            {
                if (!failed
                    && AtlasIntakeContracts.PathEquals(
                        destination,
                        workspace.Layout.CanonicalPreflightedStatePath))
                {
                    failed = true;
                    throw new IOException("synthetic preflight state publication failure");
                }

                AtlasIoSeams.Default.MoveFile(source, destination);
            });

        await Assert.ThrowsAsync<IOException>(
            () => PrivateArtifactLifecycle.CleanupPreflightAsync(
                workspace.Layout.CanonicalCleanupPreflightRequestPath,
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

        await PrivateArtifactLifecycle.CleanupPreflightAsync(
            workspace.Layout.CanonicalCleanupPreflightRequestPath,
            TestContext.Current.CancellationToken);

        AtlasLoadedDocument<AtlasIntakeStateDocument> recoveredState =
            await AtlasIntakeContracts.ReadStateAsync(
                workspace.Layout.CanonicalPreflightedStatePath,
                TestContext.Current.CancellationToken);
        AtlasLoadedDocument<AtlasPrivateArtifactInventoryDocument> recoveredInventory =
            await AtlasIntakeContracts.ReadInventoryAsync(
                workspace.Layout.CanonicalInventoryPath,
                TestContext.Current.CancellationToken);

        Assert.Equal(AtlasIntakeContracts.PreflightedPhase, recoveredState.Document.Phase);
        Assert.Equal(
            expectedAliases,
            recoveredInventory.Document.Artifacts
                .Select(static artifact => artifact.ArtifactAlias)
                .OrderBy(static alias => alias, StringComparer.Ordinal)
                .ToArray());
    }

    [Fact]
    public async Task CleanupPreflightAsyncRejectsRecoveredCursorMismatch()
    {
        await using AtlasSyntheticWorkspace workspace = await AtlasSyntheticWorkspace.CreateAsync();
        await PrepareQualifiedWorkspaceAsync(workspace);
        workspace.WriteRequest(workspace.CreatePreflightRequest());
        bool failed = false;
        AtlasIoSeams failingIo = AtlasTestSupport.CreateIo(
            moveFile: (source, destination) =>
            {
                if (!failed
                    && AtlasIntakeContracts.PathEquals(
                        destination,
                        workspace.Layout.CanonicalPreflightedStatePath))
                {
                    failed = true;
                    throw new IOException("synthetic preflight state publication failure");
                }

                AtlasIoSeams.Default.MoveFile(source, destination);
            });
        await Assert.ThrowsAsync<IOException>(
            () => PrivateArtifactLifecycle.CleanupPreflightAsync(
                workspace.Layout.CanonicalCleanupPreflightRequestPath,
                failingIo,
                TestContext.Current.CancellationToken).AsTask());
        workspace.UpdateInventory(inventory =>
        {
            AtlasPrivateArtifactEntry report = inventory.Artifacts.Single(artifact =>
                StringComparer.Ordinal.Equals(
                    artifact.Purpose,
                    AtlasIntakeContracts.CleanupPreflightReportPurpose));
            const string ForgedAlias = "private-artifact-900000";
            return inventory with
            {
                Artifacts =
                [
                    .. inventory.Artifacts.Select(artifact => artifact with
                    {
                        ArtifactAlias = ReferenceEquals(artifact, report)
                            ? ForgedAlias
                            : artifact.ArtifactAlias,
                        LineageAliases =
                        [
                            .. artifact.LineageAliases.Select(alias =>
                                StringComparer.Ordinal.Equals(alias, report.ArtifactAlias)
                                    ? ForgedAlias
                                    : alias),
                        ],
                    }),
                ],
            };
        });

        await Assert.ThrowsAsync<AtlasSafetyException>(
            () => PrivateArtifactLifecycle.CleanupPreflightAsync(
                workspace.Layout.CanonicalCleanupPreflightRequestPath,
                TestContext.Current.CancellationToken).AsTask());
        Assert.False(File.Exists(workspace.Layout.CanonicalPreflightedStatePath));
    }

    [Fact]
    public async Task CleanupPreflightAsyncRejectsPreflightedStateWhenReportIsMissing()
    {
        await using AtlasSyntheticWorkspace workspace = await AtlasSyntheticWorkspace.CreateAsync();
        await PrepareQualifiedWorkspaceAsync(workspace);
        workspace.WriteRequest(workspace.CreatePreflightRequest());
        await PrivateArtifactLifecycle.CleanupPreflightAsync(
            workspace.Layout.CanonicalCleanupPreflightRequestPath,
            TestContext.Current.CancellationToken);

        File.Delete(workspace.Layout.CanonicalCleanupPreflightReportPath);

        await Assert.ThrowsAsync<AtlasSafetyException>(
            () => PrivateArtifactLifecycle.CleanupPreflightAsync(
                workspace.Layout.CanonicalCleanupPreflightRequestPath,
                TestContext.Current.CancellationToken).AsTask());
    }

    [Fact]
    public async Task CleanupPreflightAsyncRejectsUnexpectedFutureStateArtifact()
    {
        await using AtlasSyntheticWorkspace workspace = await AtlasSyntheticWorkspace.CreateAsync();
        await PrepareQualifiedWorkspaceAsync(workspace);
        workspace.WriteRequest(workspace.CreatePreflightRequest());
        await File.WriteAllTextAsync(
            Path.Combine(workspace.Layout.StatesDirectory, "atlas-intake-state.r000005.json"),
            "{}",
            TestContext.Current.CancellationToken);

        await Assert.ThrowsAsync<AtlasSafetyException>(
            () => PrivateArtifactLifecycle.CleanupPreflightAsync(
                workspace.Layout.CanonicalCleanupPreflightRequestPath,
                TestContext.Current.CancellationToken).AsTask());
    }

    private static async Task AssertRejectedCleanupReportMutationAsync(
        string path,
        Action<JsonObject> mutate)
    {
        byte[] originalBytes = await File.ReadAllBytesAsync(
            path,
            TestContext.Current.CancellationToken);
        JsonObject json = (JsonNode.Parse(originalBytes) as JsonObject)
            ?? throw new InvalidOperationException("Expected a JSON object.");
        mutate(json);
        try
        {
            await File.WriteAllTextAsync(
                path,
                json.ToJsonString(),
                new UTF8Encoding(false),
                TestContext.Current.CancellationToken);
            await Assert.ThrowsAsync<AtlasSafetyException>(
                () => AtlasIntakeContracts.ReadCleanupPreflightReportAsync(
                    path,
                    TestContext.Current.CancellationToken).AsTask());
        }
        finally
        {
            await File.WriteAllBytesAsync(
                path,
                originalBytes,
                TestContext.Current.CancellationToken);
        }
    }

    private static async Task PrepareQualifiedWorkspaceAsync(AtlasSyntheticWorkspace workspace)
    {
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
    }
}
