using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Hcoona.CelesphoniaModifier.Atlas;
using Xunit;

namespace Hcoona.CelesphoniaModifier.Atlas.Tests;

public sealed class AtlasDiscoveryTests
{
    public static TheoryData<
        string,
        Func<AtlasPrivateArtifactInventoryDocument, AtlasPrivateArtifactInventoryDocument>>
        InvalidBaselineManifestRows
    {
        get
        {
            TheoryData<
                string,
                Func<AtlasPrivateArtifactInventoryDocument,
                    AtlasPrivateArtifactInventoryDocument>> data = [];
            data.Add(
                "missing",
                static inventory => ReplaceBaselineManifestArtifact(
                    inventory,
                    artifact => artifact with
                    {
                        Purpose = AtlasIntakeContracts.ManifestRevision4Purpose,
                    }));
            data.Add(
                "wrong-class",
                static inventory => ReplaceBaselineManifestArtifact(
                    inventory,
                    artifact => artifact with
                    {
                        ArtifactClass = AtlasIntakeContracts.PrivateEvidenceArtifactClass,
                    }));
            data.Add(
                "deleted",
                static inventory => ReplaceBaselineManifestArtifact(
                    inventory,
                    artifact => artifact with
                    {
                        Status = AtlasIntakeContracts.DeletedArtifactStatus,
                    }));
            data.Add(
                "blocked",
                static inventory => ReplaceBaselineManifestArtifact(
                    inventory,
                    artifact => artifact with
                    {
                        Status = AtlasIntakeContracts.BlockedArtifactStatus,
                    }));
            data.Add(
                "wrong-disposition",
                static inventory => ReplaceBaselineManifestArtifact(
                    inventory,
                    artifact => artifact with
                    {
                        PlannedDisposition = AtlasIntakeContracts.DeleteDisposition,
                    }));
            data.Add(
                "expired",
                static inventory => ReplaceBaselineManifestArtifact(
                    inventory,
                    artifact => artifact with
                    {
                        LastUseMilestone = "A8",
                        ExpiryCondition = "after:A8",
                    }));
            data.Add(
                "wrong-verification",
                static inventory => ReplaceBaselineManifestArtifact(
                    inventory,
                    artifact => artifact with
                    {
                        VerificationMethod = AtlasIntakeContracts.ManualA0ValidationMethod,
                    }));
            return data;
        }
    }

    public static TheoryData<string> ValidDiscoverySourceRootLayouts
    {
        get
        {
            TheoryData<string> data = [];
            data.Add("exact");
            data.Add("trailing-separators");
            return data;
        }
    }

    public static TheoryData<string> InvalidDiscoverySourceRootLayouts
    {
        get
        {
            TheoryData<string> data = [];
            data.Add("swapped-save-roles");
            data.Add("nested-primary-save-root");
            data.Add("sibling-runtime-save-root");
            data.Add("unrelated-primary-save-root");
            data.Add("duplicate-save-roots");
            data.Add("contained-game-executable");
            return data;
        }
    }

    public static TheoryData<string, Action<JsonObject>> FrozenA0ManifestMutationCases
    {
        get
        {
            TheoryData<string, Action<JsonObject>> data = [];
            data.Add(
                "save-root-order",
                json =>
                {
                    JsonArray saveRoots = (JsonArray)json["saveRoots"]!;
                    SwapArrayItems(saveRoots, 0, 1);
                });
            data.Add(
                "save-root-role",
                json =>
                    ((JsonObject)((JsonArray)json["saveRoots"]!)[0]!)["locationRole"] =
                        AtlasIntakeContracts.WebRootSaveRole);
            data.Add(
                "save-entry-order",
                json =>
                {
                    JsonArray saveEntries = (JsonArray)json["saveEntries"]!;
                    SwapArrayItems(saveEntries, 0, 1);
                });
            data.Add(
                "save-entry-slot",
                json => ((JsonObject)((JsonArray)json["saveEntries"]!)[7]!)["slotNumber"] = 8);
            data.Add(
                "save-entry-alias-renumber",
                json =>
                    ((JsonObject)((JsonArray)json["saveEntries"]!)[0]!)["sourceAlias"] =
                        "save-source-9999");
            data.Add(
                "save-entry-alias-swap",
                json =>
                {
                    JsonArray entries = (JsonArray)json["saveEntries"]!;
                    JsonObject first = (JsonObject)entries[0]!;
                    JsonObject second = (JsonObject)entries[1]!;
                    string firstAlias = first["sourceAlias"]!.GetValue<string>();
                    first["sourceAlias"] = second["sourceAlias"]!.GetValue<string>();
                    second["sourceAlias"] = firstAlias;
                });
            data.Add(
                "save-entry-alias-duplicate",
                json =>
                {
                    JsonArray entries = (JsonArray)json["saveEntries"]!;
                    ((JsonObject)entries[1]!)["sourceAlias"] =
                        ((JsonObject)entries[0]!)["sourceAlias"]!.GetValue<string>();
                });
            data.Add(
                "definition-group-id",
                json =>
                    ((JsonObject)((JsonArray)json["definitionGroups"]!)[0]!)["groupId"] =
                        "unexpected-group");
            data.Add(
                "definition-group-rule",
                json =>
                    ((JsonObject)((JsonArray)json["definitionGroups"]!)[0]!)["selectionRule"] =
                        "package-lock.json");
            data.Add(
                "definition-group-order",
                json =>
                {
                    JsonArray groups = (JsonArray)json["definitionGroups"]!;
                    SwapArrayItems(groups, 6, 7);
                });
            return data;
        }
    }

    public static TheoryData<
        string,
        Func<AtlasCorpusIntakeManifest, AtlasCorpusIntakeManifest>>
        CompletedDefinitionIdentityMutationCases
    {
        get
        {
            TheoryData<
                string,
                Func<AtlasCorpusIntakeManifest, AtlasCorpusIntakeManifest>> data = [];
            data.Add(
                "alias",
                static manifest => SwapDefinitionIdentityFields(
                    manifest,
                    3,
                    4,
                    swapAlias: true));
            data.Add(
                "path",
                static manifest => SwapDefinitionIdentityFields(
                    manifest,
                    3,
                    4,
                    swapPath: true));
            data.Add(
                "group",
                static manifest => SwapDefinitionIdentityFields(
                    manifest,
                    0,
                    1,
                    swapGroup: true));
            data.Add(
                "decision",
                static manifest => SwapDefinitionIdentityFields(
                    manifest,
                    3,
                    496,
                    swapGroup: true,
                    swapDecision: true));
            data.Add(
                "combined",
                static manifest => SwapDefinitionIdentityFields(
                    manifest,
                    0,
                    496,
                    swapAlias: true,
                    swapPath: true,
                    swapGroup: true,
                    swapDecision: true));
            return data;
        }
    }

    public static TheoryData<
        string,
        Func<AtlasPrivateArtifactInventoryDocument, AtlasPrivateArtifactInventoryDocument>>
        InvalidConfirmationRecoveryAliasCases
    {
        get
        {
            TheoryData<
                string,
                Func<AtlasPrivateArtifactInventoryDocument,
                    AtlasPrivateArtifactInventoryDocument>> data = [];
            data.Add(
                "swapped-request-manifest",
                inventory => SwapRecoveryAliases(
                    inventory,
                    AtlasIntakeContracts.ConfirmRequestPurpose,
                    AtlasIntakeContracts.ManifestRevision5Purpose));
            data.Add(
                "forged-manifest",
                inventory => ReplaceRecoveryAlias(
                    inventory,
                    AtlasIntakeContracts.ManifestRevision5Purpose,
                    "private-artifact-900000",
                    AtlasIntakeContracts.State2Purpose));
            data.Add(
                "skipped-cursor",
                inventory => ReplaceRecoveryAlias(
                    inventory,
                    AtlasIntakeContracts.ApprovedInventoryBackupPurpose,
                    "private-artifact-000009"));
            return data;
        }
    }

    [Fact]
    public async Task DiscoverAsyncCategorizesUnspecifiedRequestPreflightFailure()
    {
        await using AtlasSyntheticWorkspace workspace = await AtlasSyntheticWorkspace.CreateAsync();
        AtlasSafetyException sourceException = new("synthetic private request detail");
        AtlasIoSeams io = AtlasTestSupport.CreateIo(
            readAllBytesAsync: (_, _) => ValueTask.FromException<byte[]>(sourceException));

        AtlasSafetyException exception = await Assert.ThrowsAsync<AtlasSafetyException>(
            () => AtlasDiscovery.DiscoverAsync(
                workspace.Layout.CanonicalDiscoverRequestPath,
                io,
                TestContext.Current.CancellationToken).AsTask());

        Assert.Equal(AtlasDiscoveryFailureStage.RequestPreflight, exception.DiscoveryStage);
        Assert.Same(sourceException, exception.InnerException);
    }

    [Fact]
    public async Task DiscoverAsyncPreservesCategorizedFailure()
    {
        await using AtlasSyntheticWorkspace workspace = await AtlasSyntheticWorkspace.CreateAsync();
        AtlasSafetyException expected = new(
            "synthetic private request detail",
            AtlasDiscoveryFailureStage.Publication);
        AtlasIoSeams io = AtlasTestSupport.CreateIo(
            readAllBytesAsync: (_, _) => ValueTask.FromException<byte[]>(expected));

        AtlasSafetyException actual = await Assert.ThrowsAsync<AtlasSafetyException>(
            () => AtlasDiscovery.DiscoverAsync(
                workspace.Layout.CanonicalDiscoverRequestPath,
                io,
                TestContext.Current.CancellationToken).AsTask());

        Assert.Same(expected, actual);
    }

    [Fact]
    public async Task DiscoverAsyncPublishesPendingManifestRootMapCopyPlanAndState()
    {
        await using AtlasSyntheticWorkspace workspace = await AtlasSyntheticWorkspace.CreateAsync();
        AtlasLoadedDocument<AtlasCorpusIntakeManifest> baselineManifest =
            await AtlasIntakeContracts.ReadManifestAsync(
                workspace.Layout.CanonicalBaselineManifestPath,
                TestContext.Current.CancellationToken);

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
        Dictionary<string, AtlasManifestSaveEntry> baselineSaveEntries =
            baselineManifest.Document.SaveEntries.ToDictionary(
                static entry => AtlasDiscovery.CreateSaveEntryIdentity(
                    entry.RootAlias,
                    entry.RelativePath),
                StringComparer.OrdinalIgnoreCase);
        Assert.Equal(
            baselineSaveEntries.Count,
            pendingManifest.Document.SaveEntries.Length);
        Assert.All(
            pendingManifest.Document.SaveEntries,
            actual =>
            {
                string identity = AtlasDiscovery.CreateSaveEntryIdentity(
                    actual.RootAlias,
                    actual.RelativePath);
                Assert.True(baselineSaveEntries.ContainsKey(identity));
                AtlasManifestSaveEntry expected = baselineSaveEntries[identity];
                Assert.Equal(expected.SourceAlias, actual.SourceAlias);
                Assert.Equal(expected.RootAlias, actual.RootAlias);
                Assert.Equal(expected.RelativePath, actual.RelativePath);
                Assert.Equal(expected.Role, actual.Role);
                Assert.Equal(expected.SlotNumber, actual.SlotNumber);
                Assert.Equal(expected.Decision, actual.Decision);
                Assert.Equal(expected.ReasonCode, actual.ReasonCode);
                Assert.Equal(expected.EntryType, actual.EntryType);
                Assert.Equal(expected.IsReparsePoint, actual.IsReparsePoint);
            });
        Assert.Equal(
            AtlasIntakeContracts.GetExactFrozenDefinitionGroups()
                .Select(static group => group.GroupId)
                .ToArray(),
            pendingManifest.Document.DefinitionGroups
                .Select(static group => group.GroupId)
                .ToArray());
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
    public async Task DiscoverAsyncCompletedRerunReturnsWhenLiveSourcesAreMissing()
    {
        await using AtlasSyntheticWorkspace workspace = await AtlasSyntheticWorkspace.CreateAsync();
        await AtlasDiscovery.DiscoverAsync(
            workspace.Layout.CanonicalDiscoverRequestPath,
            TestContext.Current.CancellationToken);
        Directory.Delete(workspace.GameRootPath, recursive: true);

        await AtlasDiscovery.DiscoverAsync(
            workspace.Layout.CanonicalDiscoverRequestPath,
            TestContext.Current.CancellationToken);

        Assert.True(File.Exists(workspace.Layout.CanonicalDiscoveredStatePath));
    }

    [Fact]
    public async Task DiscoverAsyncCategorizesLiveSourcePreflightFailure()
    {
        await using AtlasSyntheticWorkspace workspace = await AtlasSyntheticWorkspace.CreateAsync();
        File.Delete(workspace.GameExecutablePath);

        AtlasSafetyException exception = await Assert.ThrowsAsync<AtlasSafetyException>(
            () => AtlasDiscovery.DiscoverAsync(
                workspace.Layout.CanonicalDiscoverRequestPath,
                TestContext.Current.CancellationToken).AsTask());

        Assert.Equal(AtlasDiscoveryFailureStage.LiveSourcePreflight, exception.DiscoveryStage);
    }

    [Fact]
    public async Task DiscoverAsyncCompletedRerunReturnsWithoutLiveSourceAccess()
    {
        await using AtlasSyntheticWorkspace workspace = await AtlasSyntheticWorkspace.CreateAsync();
        await AtlasDiscovery.DiscoverAsync(
            workspace.Layout.CanonicalDiscoverRequestPath,
            TestContext.Current.CancellationToken);

        await AtlasDiscovery.DiscoverAsync(
            workspace.Layout.CanonicalDiscoverRequestPath,
            AtlasTestSupport.CreateLiveSourceAccessThrowingIo(workspace),
            TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task ConfirmAsyncCompletedRerunReturnsWhenLiveSourcesAreMissing()
    {
        await using AtlasSyntheticWorkspace workspace = await AtlasSyntheticWorkspace.CreateAsync();
        await AtlasDiscovery.DiscoverAsync(
            workspace.Layout.CanonicalDiscoverRequestPath,
            TestContext.Current.CancellationToken);
        workspace.WriteRequest(workspace.CreateConfirmationRequest());
        await AtlasDiscovery.ConfirmAsync(
            workspace.Layout.CanonicalConfirmRequestPath,
            TestContext.Current.CancellationToken);
        Directory.Delete(workspace.GameRootPath, recursive: true);

        await AtlasDiscovery.ConfirmAsync(
            workspace.Layout.CanonicalConfirmRequestPath,
            TestContext.Current.CancellationToken);

        Assert.True(File.Exists(workspace.Layout.CanonicalApprovedStatePath));
    }

    [Fact]
    public async Task ConfirmAsyncCompletedRerunReturnsWithoutLiveSourceAccess()
    {
        await using AtlasSyntheticWorkspace workspace = await AtlasSyntheticWorkspace.CreateAsync();
        await AtlasDiscovery.DiscoverAsync(
            workspace.Layout.CanonicalDiscoverRequestPath,
            TestContext.Current.CancellationToken);
        workspace.WriteRequest(workspace.CreateConfirmationRequest());
        await AtlasDiscovery.ConfirmAsync(
            workspace.Layout.CanonicalConfirmRequestPath,
            TestContext.Current.CancellationToken);

        await AtlasDiscovery.ConfirmAsync(
            workspace.Layout.CanonicalConfirmRequestPath,
            AtlasTestSupport.CreateLiveSourceAccessThrowingIo(workspace),
            TestContext.Current.CancellationToken);
    }

    [Theory]
    [MemberData(nameof(CompletedDefinitionIdentityMutationCases))]
    public async Task ConfirmAsyncRejectsReboundCompletedDefinitionIdentityMutation(
        string caseName,
        Func<AtlasCorpusIntakeManifest, AtlasCorpusIntakeManifest> mutate)
    {
        await using AtlasSyntheticWorkspace workspace = await AtlasSyntheticWorkspace.CreateAsync();
        await AtlasDiscovery.DiscoverAsync(
            workspace.Layout.CanonicalDiscoverRequestPath,
            TestContext.Current.CancellationToken);
        await RebindDiscoveredManifestEvidenceAsync(workspace, mutate);
        workspace.WriteRequest(workspace.CreateConfirmationRequest());

        await Assert.ThrowsAsync<AtlasSafetyException>(
            () => AtlasDiscovery.ConfirmAsync(
                workspace.Layout.CanonicalConfirmRequestPath,
                AtlasTestSupport.CreateLiveSourceAccessThrowingIo(workspace),
                TestContext.Current.CancellationToken).AsTask());

        Assert.NotNull(caseName);
        Assert.False(File.Exists(workspace.Layout.CanonicalApprovedManifestPath));
        Assert.False(File.Exists(workspace.Layout.CanonicalApprovedStatePath));
        Assert.False(File.Exists(workspace.Layout.CanonicalApprovedInventoryBackupPath));
    }

    [Theory]
    [InlineData("shifted-cursor")]
    [InlineData("baseline-custody")]
    public async Task DiscoverAsyncRejectsReboundCompletedState1AliasCorruption(string caseName)
    {
        await using AtlasSyntheticWorkspace workspace = await AtlasSyntheticWorkspace.CreateAsync();
        await AtlasDiscovery.DiscoverAsync(
            workspace.Layout.CanonicalDiscoverRequestPath,
            TestContext.Current.CancellationToken);
        if (StringComparer.Ordinal.Equals(caseName, "shifted-cursor"))
        {
            await ShiftCompletedPhaseAliasesAsync(
                workspace,
                workspace.Layout.CanonicalDiscoveredStatePath,
                [
                    AtlasIntakeContracts.DiscoverRequestPurpose,
                    AtlasIntakeContracts.ManifestRevision4Purpose,
                    AtlasIntakeContracts.SourceRootMapPurpose,
                    AtlasIntakeContracts.CopyPlanPurpose,
                    AtlasIntakeContracts.State1Purpose,
                    AtlasIntakeContracts.DiscoveryInventoryBackupPurpose,
                ],
                shiftCopyPlanReservations: true);
        }
        else
        {
            await RebindBaselineCustodyAliasAsync(workspace);
        }

        AtlasSafetyException exception = await Assert.ThrowsAsync<AtlasSafetyException>(
            () => AtlasDiscovery.DiscoverAsync(
                workspace.Layout.CanonicalDiscoverRequestPath,
                AtlasTestSupport.CreateLiveSourceAccessThrowingIo(workspace),
                TestContext.Current.CancellationToken).AsTask());

        Assert.Equal(AtlasDiscoveryFailureStage.ExistingState, exception.DiscoveryStage);
    }

    [Fact]
    public async Task ConfirmAsyncRejectsReboundCompletedState2AliasCursorShift()
    {
        await using AtlasSyntheticWorkspace workspace = await AtlasSyntheticWorkspace.CreateAsync();
        await AtlasDiscovery.DiscoverAsync(
            workspace.Layout.CanonicalDiscoverRequestPath,
            TestContext.Current.CancellationToken);
        workspace.WriteRequest(workspace.CreateConfirmationRequest());
        await AtlasDiscovery.ConfirmAsync(
            workspace.Layout.CanonicalConfirmRequestPath,
            TestContext.Current.CancellationToken);
        await ShiftCompletedPhaseAliasesAsync(
            workspace,
            workspace.Layout.CanonicalApprovedStatePath,
            [
                AtlasIntakeContracts.ConfirmRequestPurpose,
                AtlasIntakeContracts.ManifestRevision5Purpose,
                AtlasIntakeContracts.State2Purpose,
                AtlasIntakeContracts.ApprovedInventoryBackupPurpose,
            ],
            shiftCopyPlanReservations: false);

        await Assert.ThrowsAsync<AtlasSafetyException>(
            () => AtlasDiscovery.ConfirmAsync(
                workspace.Layout.CanonicalConfirmRequestPath,
                AtlasTestSupport.CreateLiveSourceAccessThrowingIo(workspace),
                TestContext.Current.CancellationToken).AsTask());

        Assert.False(File.Exists(workspace.Layout.CanonicalQualifiedStatePath));
    }

    [Theory]
    [MemberData(nameof(ValidDiscoverySourceRootLayouts))]
    public async Task DiscoverAsyncAcceptsFrozenA0SourceRootLayout(string caseName)
    {
        await using AtlasSyntheticWorkspace workspace = await AtlasSyntheticWorkspace.CreateAsync();
        workspace.WriteRequest(CreateDiscoverySourceRootLayoutCase(workspace, caseName));

        await AtlasDiscovery.DiscoverAsync(
            workspace.Layout.CanonicalDiscoverRequestPath,
            TestContext.Current.CancellationToken);

        Assert.True(File.Exists(workspace.Layout.CanonicalDiscoveredStatePath));
    }

    [Theory]
    [MemberData(nameof(InvalidDiscoverySourceRootLayouts))]
    public async Task DiscoverAsyncRejectsNonA0SourceRootLayout(string caseName)
    {
        await using AtlasSyntheticWorkspace workspace = await AtlasSyntheticWorkspace.CreateAsync();
        workspace.WriteRequest(CreateDiscoverySourceRootLayoutCase(workspace, caseName));

        AtlasSafetyException exception = await Assert.ThrowsAsync<AtlasSafetyException>(
            () => AtlasDiscovery.DiscoverAsync(
                workspace.Layout.CanonicalDiscoverRequestPath,
                TestContext.Current.CancellationToken).AsTask());

        Assert.Contains("source-root layout", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ExactFrozenA0CorpusQualifiesThroughCopy()
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

        AtlasLoadedDocument<AtlasIntakeStateDocument> state =
            await AtlasIntakeContracts.ReadStateAsync(
                workspace.Layout.CanonicalQualifiedStatePath,
                TestContext.Current.CancellationToken);
        Assert.Equal(AtlasIntakeContracts.QualifiedPhase, state.Document.Phase);
    }

    [Theory]
    [MemberData(nameof(FrozenA0ManifestMutationCases))]
    public async Task DiscoverAsyncRejectsFrozenA0ManifestMutations(
        string caseName,
        Action<JsonObject> mutate)
    {
        await using AtlasSyntheticWorkspace workspace = await AtlasSyntheticWorkspace.CreateAsync();
        await MutateBaselineManifestAsync(workspace, mutate);

        AtlasApprovalException exception = await Assert.ThrowsAsync<AtlasApprovalException>(
            () => AtlasDiscovery.DiscoverAsync(
                workspace.Layout.CanonicalDiscoverRequestPath,
                TestContext.Current.CancellationToken).AsTask());

        Assert.NotNull(caseName);
        Assert.False(File.Exists(workspace.Layout.CanonicalPendingManifestPath));
        Assert.Contains("invalid", exception.Message, StringComparison.OrdinalIgnoreCase);
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
        Assert.Equal(AtlasDiscoveryFailureStage.CorpusReconciliation, exception.DiscoveryStage);
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
    public void EnumerateDefinitionEntriesRejectsNovelExtensionMatchedByRecursiveAllRule()
    {
        string definitionRoot = CreateTemporaryDirectory();
        try
        {
            Directory.CreateDirectory(Path.Combine(definitionRoot, "content"));
            File.WriteAllText(
                Path.Combine(definitionRoot, "content", "known.json"),
                "{}",
                Encoding.UTF8);
            File.WriteAllText(
                Path.Combine(definitionRoot, "content", "novel.bin"),
                "bin",
                Encoding.UTF8);
            AtlasManifestDefinitionGroup[] groups =
            [
                CreateDefinitionGroup(
                    "all-files",
                    "content/**/*",
                    AtlasIntakeContracts.IncludeDefinitionDecision),
            ];
            Dictionary<string, AtlasManifestDefinitionEntry> baselineEntries =
                CreateDefinitionEntryMap(
                    CreateDefinitionEntry(
                        "definition-source-000001",
                        "content/known.json",
                        "all-files",
                        AtlasIntakeContracts.IncludeDefinitionDecision));

            Assert.Throws<AtlasSafetyException>(() =>
                AtlasDiscovery.EnumerateDefinitionEntries(
                    definitionRoot,
                    groups,
                    baselineEntries,
                    [],
                    AtlasIoSeams.Default));
        }
        finally
        {
            Directory.Delete(definitionRoot, recursive: true);
        }
    }

    [Fact]
    public void EnumerateDefinitionEntriesTreatsNonRecursiveRuleLiterally()
    {
        string definitionRoot = CreateTemporaryDirectory();
        try
        {
            Directory.CreateDirectory(Path.Combine(definitionRoot, "content", "nested"));
            File.WriteAllText(
                Path.Combine(definitionRoot, "content", "root.json"),
                "{}",
                Encoding.UTF8);
            File.WriteAllText(
                Path.Combine(definitionRoot, "content", "nested", "child.json"),
                "{}",
                Encoding.UTF8);
            AtlasManifestDefinitionGroup[] groups =
            [
                CreateDefinitionGroup(
                    "root-only",
                    "content/*.json",
                    AtlasIntakeContracts.IncludeDefinitionDecision),
            ];
            Dictionary<string, AtlasManifestDefinitionEntry> baselineEntries =
                CreateDefinitionEntryMap(
                    CreateDefinitionEntry(
                        "definition-source-000001",
                        "content/root.json",
                        "root-only",
                        AtlasIntakeContracts.IncludeDefinitionDecision));

            List<AtlasManifestDefinitionEntry> entries = AtlasDiscovery.EnumerateDefinitionEntries(
                definitionRoot,
                groups,
                baselineEntries,
                [],
                AtlasIoSeams.Default);

            Assert.Equal(
                ["content/root.json"],
                entries.Select(entry => entry.RelativePath).ToArray());
        }
        finally
        {
            Directory.Delete(definitionRoot, recursive: true);
        }
    }

    [Fact]
    public void EnumerateDefinitionEntriesTreatsRecursiveExtensionRuleLiterally()
    {
        string definitionRoot = CreateTemporaryDirectory();
        try
        {
            Directory.CreateDirectory(Path.Combine(definitionRoot, "content", "nested"));
            File.WriteAllText(
                Path.Combine(definitionRoot, "content", "root.json"),
                "{}",
                Encoding.UTF8);
            File.WriteAllText(
                Path.Combine(definitionRoot, "content", "nested", "child.json"),
                "{}",
                Encoding.UTF8);
            AtlasManifestDefinitionGroup[] groups =
            [
                CreateDefinitionGroup(
                    "recursive-json",
                    "content/**/*.json",
                    AtlasIntakeContracts.IncludeDefinitionDecision),
            ];
            Dictionary<string, AtlasManifestDefinitionEntry> baselineEntries =
                CreateDefinitionEntryMap(
                    CreateDefinitionEntry(
                        "definition-source-000001",
                        "content/root.json",
                        "recursive-json",
                        AtlasIntakeContracts.IncludeDefinitionDecision),
                    CreateDefinitionEntry(
                        "definition-source-000002",
                        "content/nested/child.json",
                        "recursive-json",
                        AtlasIntakeContracts.IncludeDefinitionDecision));

            List<AtlasManifestDefinitionEntry> entries = AtlasDiscovery.EnumerateDefinitionEntries(
                definitionRoot,
                groups,
                baselineEntries,
                [],
                AtlasIoSeams.Default);

            Assert.Equal(
                ["content/nested/child.json", "content/root.json"],
                entries.Select(entry => entry.RelativePath)
                    .OrderBy(static path => path, StringComparer.Ordinal)
                    .ToArray());
        }
        finally
        {
            Directory.Delete(definitionRoot, recursive: true);
        }
    }

    [Fact]
    public void EnumerateDefinitionEntriesRejectsNovelExtensionMatchedBySingleSegmentAllRule()
    {
        string definitionRoot = CreateTemporaryDirectory();
        try
        {
            Directory.CreateDirectory(Path.Combine(definitionRoot, "content"));
            File.WriteAllText(
                Path.Combine(definitionRoot, "content", "known.json"),
                "{}",
                Encoding.UTF8);
            File.WriteAllText(
                Path.Combine(definitionRoot, "content", "novel.bin"),
                "bin",
                Encoding.UTF8);
            AtlasManifestDefinitionGroup[] groups =
            [
                CreateDefinitionGroup(
                    "all-files",
                    "content/*",
                    AtlasIntakeContracts.IncludeDefinitionDecision),
            ];
            Dictionary<string, AtlasManifestDefinitionEntry> baselineEntries =
                CreateDefinitionEntryMap(
                    CreateDefinitionEntry(
                        "definition-source-000001",
                        "content/known.json",
                        "all-files",
                        AtlasIntakeContracts.IncludeDefinitionDecision));

            Assert.Throws<AtlasSafetyException>(() =>
                AtlasDiscovery.EnumerateDefinitionEntries(
                    definitionRoot,
                    groups,
                    baselineEntries,
                    [],
                    AtlasIoSeams.Default));
        }
        finally
        {
            Directory.Delete(definitionRoot, recursive: true);
        }
    }

    [Fact]
    public void EnumerateDefinitionEntriesSupportsBraceAlternationSlashNormalizationAndCase()
    {
        string definitionRoot = CreateTemporaryDirectory();
        try
        {
            Directory.CreateDirectory(Path.Combine(definitionRoot, "www", "nested"));
            string[] relativePaths =
            [
                "www/root.json",
                "www/root.csv",
                "www/root.txt",
                "www/nested/root.xml",
                "www/nested/root.yaml",
                "www/nested/root.yml",
                "www/nested/root.xlsx",
            ];
            foreach (string relativePath in relativePaths)
            {
                File.WriteAllText(
                    Path.Combine(
                        definitionRoot,
                        relativePath.Replace('/', Path.DirectorySeparatorChar)),
                    relativePath,
                    Encoding.UTF8);
            }

            AtlasManifestDefinitionGroup[] groups =
            [
                CreateDefinitionGroup(
                    "auxiliary",
                    @"www\**\*.{JSON,csv,TXT,xml,yaml,yml,xlsx}",
                    AtlasIntakeContracts.ExcludeDefinitionDecision),
            ];
            Dictionary<string, AtlasManifestDefinitionEntry> baselineEntries =
                CreateDefinitionEntryMap(
                    relativePaths.Select((path, index) =>
                        CreateDefinitionEntry(
                            $"definition-source-{index + 1:000000}",
                            path,
                            "auxiliary",
                            AtlasIntakeContracts.ExcludeDefinitionDecision)).ToArray());

            List<AtlasManifestDefinitionEntry> entries = AtlasDiscovery.EnumerateDefinitionEntries(
                definitionRoot,
                groups,
                baselineEntries,
                [],
                AtlasIoSeams.Default);

            Assert.Equal(
                relativePaths.OrderBy(static path => path, StringComparer.Ordinal).ToArray(),
                entries.Select(entry => entry.RelativePath)
                    .OrderBy(static path => path, StringComparer.Ordinal)
                    .ToArray());
        }
        finally
        {
            Directory.Delete(definitionRoot, recursive: true);
        }
    }

    [Fact]
    public void EnumerateDefinitionEntriesUsesFirstMatchingGroupForOverlap()
    {
        string definitionRoot = CreateTemporaryDirectory();
        try
        {
            Directory.CreateDirectory(Path.Combine(definitionRoot, "content"));
            File.WriteAllText(
                Path.Combine(definitionRoot, "content", "entry.json"),
                "{}",
                Encoding.UTF8);
            File.WriteAllText(
                Path.Combine(definitionRoot, "content", "entry.txt"),
                "note",
                Encoding.UTF8);
            AtlasManifestDefinitionGroup[] groups =
            [
                CreateDefinitionGroup(
                    "included-json",
                    "content/*.json",
                    AtlasIntakeContracts.IncludeDefinitionDecision),
                CreateDefinitionGroup(
                    "excluded-structured",
                    "content/*.{json,txt}",
                    AtlasIntakeContracts.ExcludeDefinitionDecision),
            ];
            Dictionary<string, AtlasManifestDefinitionEntry> baselineEntries =
                CreateDefinitionEntryMap(
                    CreateDefinitionEntry(
                        "definition-source-000001",
                        "content/entry.json",
                        "included-json",
                        AtlasIntakeContracts.IncludeDefinitionDecision),
                    CreateDefinitionEntry(
                        "definition-source-000002",
                        "content/entry.txt",
                        "excluded-structured",
                        AtlasIntakeContracts.ExcludeDefinitionDecision));

            List<AtlasManifestDefinitionEntry> entries = AtlasDiscovery.EnumerateDefinitionEntries(
                definitionRoot,
                groups,
                baselineEntries,
                [],
                AtlasIoSeams.Default);

            Assert.Equal(2, entries.Count);
            Assert.Equal(
                "included-json",
                entries.Single(entry => entry.RelativePath == "content/entry.json").GroupId);
            Assert.Equal(
                "excluded-structured",
                entries.Single(entry => entry.RelativePath == "content/entry.txt").GroupId);
        }
        finally
        {
            Directory.Delete(definitionRoot, recursive: true);
        }
    }

    [Theory]
    [InlineData("content/***.json")]
    [InlineData("content/?.json")]
    [InlineData("content/*.{json,}")]
    [InlineData("content/*.{json,txt")]
    public void EnumerateDefinitionEntriesRejectsInvalidSelectionRuleGrammar(string selectionRule)
    {
        string definitionRoot = CreateTemporaryDirectory();
        try
        {
            Directory.CreateDirectory(Path.Combine(definitionRoot, "content"));
            File.WriteAllText(
                Path.Combine(definitionRoot, "content", "entry.json"),
                "{}",
                Encoding.UTF8);
            AtlasManifestDefinitionGroup[] groups =
            [
                CreateDefinitionGroup(
                    "invalid",
                    selectionRule,
                    AtlasIntakeContracts.IncludeDefinitionDecision),
            ];
            Dictionary<string, AtlasManifestDefinitionEntry> baselineEntries =
                CreateDefinitionEntryMap(
                    CreateDefinitionEntry(
                        "definition-source-000001",
                        "content/entry.json",
                        "invalid",
                        AtlasIntakeContracts.IncludeDefinitionDecision));

            Assert.Throws<AtlasSafetyException>(() =>
                AtlasDiscovery.EnumerateDefinitionEntries(
                    definitionRoot,
                    groups,
                    baselineEntries,
                    [],
                    AtlasIoSeams.Default));
        }
        finally
        {
            Directory.Delete(definitionRoot, recursive: true);
        }
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

    [Theory]
    [MemberData(nameof(InvalidBaselineManifestRows))]
    public async Task DiscoverAsyncRejectsInvalidBaselineManifestArtifactRow(
        string _,
        Func<AtlasPrivateArtifactInventoryDocument, AtlasPrivateArtifactInventoryDocument> mutate)
    {
        await using AtlasSyntheticWorkspace workspace = await AtlasSyntheticWorkspace.CreateAsync();
        workspace.UpdateInventory(mutate);

        AtlasSafetyException exception = await Assert.ThrowsAsync<AtlasSafetyException>(
            () => AtlasDiscovery.DiscoverAsync(
                workspace.Layout.CanonicalDiscoverRequestPath,
                TestContext.Current.CancellationToken).AsTask());

        Assert.Equal(AtlasDiscoveryFailureStage.BaselineInventory, exception.DiscoveryStage);
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
    public async Task DiscoverAsyncRejectsLeftoverInventoryStagingAfterCompletedReplacement()
    {
        await using AtlasSyntheticWorkspace workspace = await AtlasSyntheticWorkspace.CreateAsync();
        await AtlasDiscovery.DiscoverAsync(
            workspace.Layout.CanonicalDiscoverRequestPath,
            TestContext.Current.CancellationToken);
        File.Delete(workspace.Layout.CanonicalDiscoveredStatePath);
        byte[] currentInventoryBytes = await File.ReadAllBytesAsync(
            workspace.Layout.CanonicalInventoryPath,
            TestContext.Current.CancellationToken);
        string stagingPath = AtlasDiscovery.GetStagingPath(
            workspace.Layout.CanonicalInventoryPath,
            AtlasIntakeContracts.DiscoveredPhase);
        await File.WriteAllBytesAsync(
            stagingPath,
            currentInventoryBytes,
            TestContext.Current.CancellationToken);

        await Assert.ThrowsAsync<AtlasSafetyException>(
            () => AtlasDiscovery.DiscoverAsync(
                workspace.Layout.CanonicalDiscoverRequestPath,
                TestContext.Current.CancellationToken).AsTask());

        Assert.False(File.Exists(workspace.Layout.CanonicalDiscoveredStatePath));
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
    public async Task DiscoverAsyncCategorizesPublicationFailure()
    {
        await using AtlasSyntheticWorkspace workspace = await AtlasSyntheticWorkspace.CreateAsync();
        AtlasIoSeams failingIo = AtlasTestSupport.CreateIo(
            moveFile: (source, destination) =>
            {
                if (AtlasIntakeContracts.PathEquals(
                        destination,
                        workspace.Layout.CanonicalPendingManifestPath))
                {
                    throw new AtlasSafetyException("synthetic private publication detail");
                }

                AtlasIoSeams.Default.MoveFile(source, destination);
            });

        AtlasSafetyException exception = await Assert.ThrowsAsync<AtlasSafetyException>(
            () => AtlasDiscovery.DiscoverAsync(
                workspace.Layout.CanonicalDiscoverRequestPath,
                failingIo,
                TestContext.Current.CancellationToken).AsTask());

        Assert.Equal(AtlasDiscoveryFailureStage.Publication, exception.DiscoveryStage);
    }

    [Fact]
    public async Task DiscoverAsyncRejectsSwappedRecoveredArtifactOrderBeforeSourceAccess()
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
        AtlasLoadedDocument<AtlasPrivateArtifactInventoryDocument> priorInventory =
            await AtlasIntakeContracts.ReadInventoryAsync(
                workspace.Layout.CanonicalDiscoveredInventoryBackupPath,
                TestContext.Current.CancellationToken);
        workspace.UpdateInventory(inventory =>
        {
            AtlasPrivateArtifactEntry[] artifacts = [.. inventory.Artifacts];
            int firstRecoveredIndex = priorInventory.Document.Artifacts.Length;
            (artifacts[firstRecoveredIndex], artifacts[firstRecoveredIndex + 1]) =
                (artifacts[firstRecoveredIndex + 1], artifacts[firstRecoveredIndex]);
            return inventory with { Artifacts = artifacts };
        });

        await Assert.ThrowsAsync<AtlasSafetyException>(
            () => AtlasDiscovery.DiscoverAsync(
                workspace.Layout.CanonicalDiscoverRequestPath,
                AtlasTestSupport.CreateLiveSourceAccessThrowingIo(workspace),
                TestContext.Current.CancellationToken).AsTask());
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

    [Theory]
    [MemberData(nameof(InvalidConfirmationRecoveryAliasCases))]
    public async Task ConfirmAsyncRejectsRecoveredConfirmationAliasMutations(
        string caseName,
        Func<AtlasPrivateArtifactInventoryDocument, AtlasPrivateArtifactInventoryDocument> mutate)
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
        workspace.UpdateInventory(mutate);

        AtlasSafetyException exception = await Assert.ThrowsAsync<AtlasSafetyException>(
            () => AtlasDiscovery.ConfirmAsync(
                workspace.Layout.CanonicalConfirmRequestPath,
                TestContext.Current.CancellationToken).AsTask());

        Assert.NotNull(caseName);
        Assert.Contains("alias", exception.Message, StringComparison.OrdinalIgnoreCase);
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

        await Assert.ThrowsAsync<AtlasSafetyException>(
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

    [Fact]
    public async Task ConfirmAsyncDistinguishesAbsentStateFromNonFileState()
    {
        await using AtlasSyntheticWorkspace workspace = await AtlasSyntheticWorkspace.CreateAsync();
        await AtlasDiscovery.DiscoverAsync(
            workspace.Layout.CanonicalDiscoverRequestPath,
            TestContext.Current.CancellationToken);
        workspace.WriteRequest(workspace.CreateConfirmationRequest());
        File.Delete(workspace.Layout.CanonicalDiscoveredStatePath);

        await Assert.ThrowsAsync<AtlasApprovalException>(
            () => AtlasDiscovery.ConfirmAsync(
                workspace.Layout.CanonicalConfirmRequestPath,
                TestContext.Current.CancellationToken).AsTask());

        Directory.CreateDirectory(workspace.Layout.CanonicalDiscoveredStatePath);
        await Assert.ThrowsAsync<AtlasSafetyException>(
            () => AtlasDiscovery.ConfirmAsync(
                workspace.Layout.CanonicalConfirmRequestPath,
                TestContext.Current.CancellationToken).AsTask());
    }

    [Fact]
    public async Task DiscoverAsyncRejectsNonCanonicalRevisionDirectory()
    {
        await using AtlasSyntheticWorkspace workspace = await AtlasSyntheticWorkspace.CreateAsync();
        AtlasIntakeDiscoveryRequest request = workspace.CreateDiscoveryRequest() with
        {
            ManifestRevisionDirectory = Path.Combine(workspace.Layout.IntakeDirectory, "alternate"),
        };
        workspace.WriteRequest(request);

        AtlasSafetyException exception = await Assert.ThrowsAsync<AtlasSafetyException>(
            () => AtlasDiscovery.DiscoverAsync(
                workspace.Layout.CanonicalDiscoverRequestPath,
                TestContext.Current.CancellationToken).AsTask());

        Assert.Equal(AtlasDiscoveryFailureStage.DiscoveryCanonicalPaths, exception.DiscoveryStage);
    }

    [Fact]
    public async Task DiscoverAsyncRejectsUnexpectedRevisionArtifact()
    {
        await using AtlasSyntheticWorkspace workspace = await AtlasSyntheticWorkspace.CreateAsync();
        Directory.CreateDirectory(workspace.Layout.ManifestRevisionDirectory);
        await File.WriteAllTextAsync(
            Path.Combine(
                workspace.Layout.ManifestRevisionDirectory,
                "corpus-intake-manifest.r000006.json"),
            "{}",
            TestContext.Current.CancellationToken);

        await Assert.ThrowsAsync<AtlasSafetyException>(
            () => AtlasDiscovery.DiscoverAsync(
                workspace.Layout.CanonicalDiscoverRequestPath,
                TestContext.Current.CancellationToken).AsTask());
    }

    [Fact]
    public async Task ConfirmAsyncRejectsUnexpectedStateRevisionArtifact()
    {
        await using AtlasSyntheticWorkspace workspace = await AtlasSyntheticWorkspace.CreateAsync();
        await AtlasDiscovery.DiscoverAsync(
            workspace.Layout.CanonicalDiscoverRequestPath,
            TestContext.Current.CancellationToken);
        workspace.WriteRequest(workspace.CreateConfirmationRequest());
        await File.WriteAllTextAsync(
            Path.Combine(workspace.Layout.StatesDirectory, "atlas-intake-state.r000005.json"),
            "{}",
            TestContext.Current.CancellationToken);

        await Assert.ThrowsAsync<AtlasSafetyException>(
            () => AtlasDiscovery.ConfirmAsync(
                workspace.Layout.CanonicalConfirmRequestPath,
                TestContext.Current.CancellationToken).AsTask());
    }

    [Fact]
    public async Task DiscoverAsyncAdmitsExactReleasedA0EntriesWithoutReadingTheirContent()
    {
        await using AtlasSyntheticWorkspace workspace = await AtlasSyntheticWorkspace.CreateAsync();
        (string privateProvenancePath, string[] opaqueDirectories) =
            await AddReleasedA0EvidenceAsync(workspace);

        bool IsOpaqueContentPath(string path) =>
            AtlasIntakeContracts.PathEquals(path, privateProvenancePath)
            || opaqueDirectories.Any(root => AtlasDiscovery.ContainsPath(root, path));

        bool IsOpaqueDirectoryPath(string path) =>
            opaqueDirectories.Any(root => AtlasDiscovery.ContainsPath(root, path));

        AtlasIoSeams io = AtlasTestSupport.CreateIo(
            readAllBytesAsync: (path, cancellationToken) =>
                IsOpaqueContentPath(path)
                    ? ValueTask.FromException<byte[]>(
                        new InvalidOperationException("Opaque content was read."))
                    : AtlasIoSeams.Default.ReadAllBytesAsync(path, cancellationToken),
            enumerateFileSystemEntries: (path, searchOption) =>
                IsOpaqueDirectoryPath(path)
                    ? throw new InvalidOperationException("Opaque content was enumerated.")
                    : AtlasIoSeams.Default.EnumerateFileSystemEntries(path, searchOption),
            openFile: (path, mode, access, share, options) =>
                IsOpaqueContentPath(path)
                    ? throw new InvalidOperationException("Opaque content was opened.")
                    : AtlasIoSeams.Default.OpenFile(path, mode, access, share, options));

        await AtlasDiscovery.DiscoverAsync(
            workspace.Layout.CanonicalDiscoverRequestPath,
            io,
            TestContext.Current.CancellationToken);
    }

    [Theory]
    [InlineData("survey-root", AtlasIntakeContracts.ReleasedA0DecodedDirectoryName, true)]
    [InlineData("intake", AtlasIntakeContracts.ReleasedA0PrivateProvenanceFileName, false)]
    [InlineData(
        "copies",
        AtlasIntakeContracts.ReleasedA0PreservationSnapshotDirectoryName,
        true)]
    public async Task DiscoverAsyncRejectsReleasedA0NearMatch(
        string boundary,
        string entryName,
        bool isDirectory)
    {
        await using AtlasSyntheticWorkspace workspace = await AtlasSyntheticWorkspace.CreateAsync();
        string nearMatchPath = Path.Combine(
            GetReleasedA0BoundaryDirectory(workspace, boundary),
            entryName + ".backup");
        if (isDirectory)
        {
            Directory.CreateDirectory(nearMatchPath);
        }
        else
        {
            await File.WriteAllTextAsync(
                nearMatchPath,
                "near-match",
                TestContext.Current.CancellationToken);
        }

        AtlasSafetyException exception = await Assert.ThrowsAsync<AtlasSafetyException>(
            () => AtlasDiscovery.DiscoverAsync(
                workspace.Layout.CanonicalDiscoverRequestPath,
                TestContext.Current.CancellationToken).AsTask());

        Assert.Equal(AtlasDiscoveryFailureStage.CommandWorkspaceCensus, exception.DiscoveryStage);
    }

    [Theory]
    [InlineData("survey-root", AtlasIntakeContracts.ReleasedA0DecodedDirectoryName, false)]
    [InlineData("intake", AtlasIntakeContracts.ReleasedA0PrivateProvenanceFileName, true)]
    [InlineData(
        "copies",
        AtlasIntakeContracts.ReleasedA0PreservationSnapshotDirectoryName,
        false)]
    public async Task DiscoverAsyncRejectsReleasedA0EntryWithWrongType(
        string boundary,
        string entryName,
        bool isDirectory)
    {
        await using AtlasSyntheticWorkspace workspace = await AtlasSyntheticWorkspace.CreateAsync();
        string wrongTypePath = Path.Combine(
            GetReleasedA0BoundaryDirectory(workspace, boundary),
            entryName);
        if (isDirectory)
        {
            Directory.CreateDirectory(wrongTypePath);
        }
        else
        {
            await File.WriteAllTextAsync(
                wrongTypePath,
                "wrong-type",
                TestContext.Current.CancellationToken);
        }

        await Assert.ThrowsAsync<AtlasSafetyException>(
            () => AtlasDiscovery.DiscoverAsync(
                workspace.Layout.CanonicalDiscoverRequestPath,
                TestContext.Current.CancellationToken).AsTask());
    }

    [Fact]
    public async Task DiscoverAsyncRejectsReparseBackedReleasedA0Entry()
    {
        await using AtlasSyntheticWorkspace workspace = await AtlasSyntheticWorkspace.CreateAsync();
        string targetPath = Path.Combine(
            workspace.WorkspaceRoot,
            AtlasIntakeContracts.ReleasedA0DecodedDirectoryName);
        Directory.CreateDirectory(targetPath);
        AtlasIoSeams io = AtlasTestSupport.CreateIo(
            getAttributes: path =>
                AtlasIntakeContracts.PathEquals(path, targetPath)
                    ? AtlasIoSeams.Default.GetAttributes(path) | FileAttributes.ReparsePoint
                    : AtlasIoSeams.Default.GetAttributes(path));

        await Assert.ThrowsAsync<AtlasSafetyException>(
            () => AtlasDiscovery.DiscoverAsync(
                workspace.Layout.CanonicalDiscoverRequestPath,
                io,
                TestContext.Current.CancellationToken).AsTask());
    }

    private static string GetReleasedA0BoundaryDirectory(
        AtlasSyntheticWorkspace workspace,
        string boundary) =>
        boundary switch
        {
            "survey-root" => workspace.WorkspaceRoot,
            "intake" => workspace.Layout.IntakeDirectory,
            "copies" => workspace.Layout.CopiesDirectory,
            _ => throw new InvalidOperationException("Unsupported boundary."),
        };

    private static async Task<(string PrivateProvenancePath, string[] OpaqueDirectories)>
        AddReleasedA0EvidenceAsync(
        AtlasSyntheticWorkspace workspace)
    {
        string WorkspacePath(string name) => Path.Combine(workspace.WorkspaceRoot, name);

        string privateProvenancePath = Path.Combine(
            workspace.Layout.IntakeDirectory,
            AtlasIntakeContracts.ReleasedA0PrivateProvenanceFileName);
        await File.WriteAllTextAsync(
            privateProvenancePath,
            "opaque",
            TestContext.Current.CancellationToken);

        string[] opaqueDirectories =
        [
            WorkspacePath(AtlasIntakeContracts.ReleasedA0DecodedDirectoryName),
            WorkspacePath(AtlasIntakeContracts.ReleasedA0EvidenceDirectoryName),
            WorkspacePath(AtlasIntakeContracts.ReleasedA0AgentEnvelopesDirectoryName),
            WorkspacePath(AtlasIntakeContracts.ReleasedA0ValidationDirectoryName),
            Path.Combine(
                workspace.Layout.CopiesDirectory,
                AtlasIntakeContracts.ReleasedA0PreservationSnapshotDirectoryName),
        ];
        foreach (string opaqueDirectory in opaqueDirectories)
        {
            string nestedDirectory = Path.Combine(opaqueDirectory, "nested");
            Directory.CreateDirectory(nestedDirectory);
            await File.WriteAllTextAsync(
                Path.Combine(nestedDirectory, "sentinel.bin"),
                "opaque",
                TestContext.Current.CancellationToken);
        }

        return (privateProvenancePath, opaqueDirectories);
    }

    private static AtlasManifestDefinitionGroup CreateDefinitionGroup(
        string groupId,
        string selectionRule,
        string decision) =>
        new()
        {
            GroupId = groupId,
            SelectionRule = selectionRule,
            DiscoveredCount = 0,
            Decision = decision,
        };

    private static AtlasManifestDefinitionEntry CreateDefinitionEntry(
        string sourceAlias,
        string relativePath,
        string groupId,
        string decision) =>
        new()
        {
            SourceAlias = sourceAlias,
            RelativePath = relativePath,
            GroupId = groupId,
            Decision = decision,
            EntryType = AtlasIntakeContracts.FileEntryType,
            IsReparsePoint = false,
        };

    private static Dictionary<string, AtlasManifestDefinitionEntry> CreateDefinitionEntryMap(
        params AtlasManifestDefinitionEntry[] entries) =>
        entries.ToDictionary(
            static entry => AtlasIntakeContracts.NormalizeRelativePath(entry.RelativePath),
            StringComparer.OrdinalIgnoreCase);

    private static AtlasIntakeDiscoveryRequest CreateDiscoverySourceRootLayoutCase(
        AtlasSyntheticWorkspace workspace,
        string caseName) =>
        caseName switch
        {
            "exact" => workspace.CreateDiscoveryRequest(),
            "trailing-separators" => CreateDiscoveryRequestWithRoots(
                workspace,
                workspace.DefinitionRootPath + Path.DirectorySeparatorChar,
                workspace.GameExecutablePath,
                workspace.SaveRootPath + Path.DirectorySeparatorChar,
                workspace.WebSaveRootPath + Path.DirectorySeparatorChar),
            "swapped-save-roles" => CreateDiscoveryRequestWithRoots(
                workspace,
                workspace.DefinitionRootPath,
                workspace.GameExecutablePath,
                workspace.WebSaveRootPath,
                workspace.SaveRootPath),
            "nested-primary-save-root" => CreateDiscoveryRequestWithNestedPrimarySaveRoot(
                workspace),
            "sibling-runtime-save-root" => CreateDiscoveryRequestWithSiblingRuntimeSaveRoot(
                workspace),
            "unrelated-primary-save-root" => CreateDiscoveryRequestWithUnrelatedPrimarySaveRoot(
                workspace),
            "duplicate-save-roots" => CreateDiscoveryRequestWithRoots(
                workspace,
                workspace.DefinitionRootPath,
                workspace.GameExecutablePath,
                workspace.SaveRootPath,
                workspace.SaveRootPath),
            "contained-game-executable" => CreateDiscoveryRequestWithContainedGameExecutable(
                workspace),
            _ => throw new InvalidOperationException("Unsupported source-root layout case."),
        };

    private static AtlasIntakeDiscoveryRequest CreateDiscoveryRequestWithRoots(
        AtlasSyntheticWorkspace workspace,
        string definitionRoot,
        string gameExecutablePath,
        string deploymentSaveRoot,
        string webSaveRoot) =>
        workspace.CreateDiscoveryRequest() with
        {
            DefinitionRoot = definitionRoot,
            GameExecutablePath = gameExecutablePath,
            SaveRoots =
            [
                new AtlasRequestSaveRoot
                {
                    LocationRole = AtlasIntakeContracts.DeploymentRootSaveRole,
                    Path = deploymentSaveRoot,
                },
                new AtlasRequestSaveRoot
                {
                    LocationRole = AtlasIntakeContracts.WebRootSaveRole,
                    Path = webSaveRoot,
                },
            ],
        };

    private static AtlasIntakeDiscoveryRequest CreateDiscoveryRequestWithNestedPrimarySaveRoot(
        AtlasSyntheticWorkspace workspace)
    {
        string nestedPrimarySaveRoot = Path.Combine(workspace.SaveRootPath, "nested");
        Directory.CreateDirectory(nestedPrimarySaveRoot);
        return CreateDiscoveryRequestWithRoots(
            workspace,
            workspace.DefinitionRootPath,
            workspace.GameExecutablePath,
            nestedPrimarySaveRoot,
            workspace.WebSaveRootPath);
    }

    private static AtlasIntakeDiscoveryRequest CreateDiscoveryRequestWithSiblingRuntimeSaveRoot(
        AtlasSyntheticWorkspace workspace)
    {
        string siblingRuntimeSaveRoot = Path.Combine(
            workspace.ProjectRoot,
            "sibling-runtime-save");
        Directory.CreateDirectory(siblingRuntimeSaveRoot);
        return CreateDiscoveryRequestWithRoots(
            workspace,
            workspace.DefinitionRootPath,
            workspace.GameExecutablePath,
            workspace.SaveRootPath,
            siblingRuntimeSaveRoot);
    }

    private static AtlasIntakeDiscoveryRequest CreateDiscoveryRequestWithUnrelatedPrimarySaveRoot(
        AtlasSyntheticWorkspace workspace)
    {
        string unrelatedPrimarySaveRoot = Path.Combine(
            workspace.ProjectRoot,
            "unrelated-primary-save");
        Directory.CreateDirectory(unrelatedPrimarySaveRoot);
        return CreateDiscoveryRequestWithRoots(
            workspace,
            workspace.DefinitionRootPath,
            workspace.GameExecutablePath,
            unrelatedPrimarySaveRoot,
            workspace.WebSaveRootPath);
    }

    private static AtlasIntakeDiscoveryRequest CreateDiscoveryRequestWithContainedGameExecutable(
        AtlasSyntheticWorkspace workspace)
    {
        string containedGameExecutablePath = Path.Combine(
            workspace.DefinitionRootPath,
            "www",
            "Game.exe");
        File.WriteAllText(containedGameExecutablePath, "synthetic");
        return CreateDiscoveryRequestWithRoots(
            workspace,
            workspace.DefinitionRootPath,
            containedGameExecutablePath,
            workspace.SaveRootPath,
            workspace.WebSaveRootPath);
    }

    private static AtlasCorpusIntakeManifest SwapDefinitionIdentityFields(
        AtlasCorpusIntakeManifest manifest,
        int firstIndex,
        int secondIndex,
        bool swapAlias = false,
        bool swapPath = false,
        bool swapGroup = false,
        bool swapDecision = false)
    {
        AtlasManifestDefinitionEntry[] entries = [.. manifest.DefinitionEntries];
        AtlasManifestDefinitionEntry first = entries[firstIndex];
        AtlasManifestDefinitionEntry second = entries[secondIndex];
        entries[firstIndex] = first with
        {
            SourceAlias = swapAlias ? second.SourceAlias : first.SourceAlias,
            RelativePath = swapPath ? second.RelativePath : first.RelativePath,
            GroupId = swapGroup ? second.GroupId : first.GroupId,
            Decision = swapDecision ? second.Decision : first.Decision,
        };
        entries[secondIndex] = second with
        {
            SourceAlias = swapAlias ? first.SourceAlias : second.SourceAlias,
            RelativePath = swapPath ? first.RelativePath : second.RelativePath,
            GroupId = swapGroup ? first.GroupId : second.GroupId,
            Decision = swapDecision ? first.Decision : second.Decision,
        };
        return manifest with { DefinitionEntries = entries };
    }

    private static async Task RebindDiscoveredManifestEvidenceAsync(
        AtlasSyntheticWorkspace workspace,
        Func<AtlasCorpusIntakeManifest, AtlasCorpusIntakeManifest> mutate)
    {
        AtlasLoadedDocument<AtlasCorpusIntakeManifest> pendingManifest =
            await AtlasIntakeContracts.ReadManifestAsync(
                workspace.Layout.CanonicalPendingManifestPath,
                TestContext.Current.CancellationToken);
        AtlasLoadedDocument<AtlasCopyPlanDocument> copyPlan =
            await AtlasIntakeContracts.ReadCopyPlanAsync(
                workspace.Layout.CanonicalCopyPlanPath,
                TestContext.Current.CancellationToken);
        AtlasLoadedDocument<AtlasIntakeStateDocument> state =
            await AtlasIntakeContracts.ReadStateAsync(
                workspace.Layout.CanonicalDiscoveredStatePath,
                TestContext.Current.CancellationToken);
        AtlasCorpusIntakeManifest mutatedManifest = mutate(pendingManifest.Document);
        int firstDestinationOrdinal = copyPlan.Document.Entries
            .Min(static entry => AtlasIntakeContracts.ParseArtifactOrdinal(
                entry.DestinationArtifactAlias));
        AtlasCopyPlanDocument mutatedCopyPlan = AtlasDiscovery.CreateCopyPlan(
            mutatedManifest,
            firstDestinationOrdinal);
        await File.WriteAllBytesAsync(
            workspace.Layout.CanonicalPendingManifestPath,
            AtlasIntakeContracts.SerializeManifest(mutatedManifest),
            TestContext.Current.CancellationToken);
        await File.WriteAllBytesAsync(
            workspace.Layout.CanonicalCopyPlanPath,
            AtlasIntakeContracts.SerializeCopyPlan(mutatedCopyPlan),
            TestContext.Current.CancellationToken);
        AtlasIntakeStateDocument reboundState = state.Document with
        {
            DocumentBindings =
            [
                .. state.Document.DocumentBindings.Select(binding => binding with
                {
                    Sha256 = binding.Role switch
                    {
                        AtlasIntakeContracts.PendingManifestRole =>
                            AtlasSyntheticWorkspace.ComputeSha256(
                                workspace.Layout.CanonicalPendingManifestPath),
                        AtlasIntakeContracts.CopyPlanRole =>
                            AtlasSyntheticWorkspace.ComputeSha256(
                                workspace.Layout.CanonicalCopyPlanPath),
                        _ => binding.Sha256,
                    },
                }),
            ],
        };
        await File.WriteAllBytesAsync(
            workspace.Layout.CanonicalDiscoveredStatePath,
            AtlasIntakeContracts.SerializeState(reboundState),
            TestContext.Current.CancellationToken);
    }

    private static async Task ShiftCompletedPhaseAliasesAsync(
        AtlasSyntheticWorkspace workspace,
        string statePath,
        string[] phasePurposes,
        bool shiftCopyPlanReservations)
    {
        AtlasLoadedDocument<AtlasPrivateArtifactInventoryDocument> inventory =
            await AtlasIntakeContracts.ReadInventoryAsync(
                workspace.Layout.CanonicalInventoryPath,
                TestContext.Current.CancellationToken);
        AtlasLoadedDocument<AtlasCopyPlanDocument> copyPlan =
            await AtlasIntakeContracts.ReadCopyPlanAsync(
                workspace.Layout.CanonicalCopyPlanPath,
                TestContext.Current.CancellationToken);
        AtlasLoadedDocument<AtlasIntakeStateDocument> state =
            await AtlasIntakeContracts.ReadStateAsync(
                statePath,
                TestContext.Current.CancellationToken);
        Dictionary<string, string> aliasMap = new(StringComparer.Ordinal);
        foreach (string purpose in phasePurposes)
        {
            string alias = inventory.Document.Artifacts.Single(artifact =>
                StringComparer.Ordinal.Equals(artifact.Purpose, purpose)).ArtifactAlias;
            aliasMap.Add(
                alias,
                AtlasIntakeContracts.FormatArtifactAlias(
                    AtlasIntakeContracts.ParseArtifactOrdinal(alias) + 1));
        }

        if (shiftCopyPlanReservations)
        {
            foreach (AtlasCopyPlanEntry entry in copyPlan.Document.Entries)
            {
                aliasMap.Add(
                    entry.DestinationArtifactAlias,
                    AtlasIntakeContracts.FormatArtifactAlias(
                        AtlasIntakeContracts.ParseArtifactOrdinal(
                            entry.DestinationArtifactAlias) + 1));
            }

            AtlasCopyPlanDocument shiftedCopyPlan = copyPlan.Document with
            {
                Entries =
                [
                    .. copyPlan.Document.Entries.Select(entry => entry with
                    {
                        DestinationArtifactAlias = RebindAlias(
                            entry.DestinationArtifactAlias,
                            aliasMap),
                    }),
                ],
            };
            await File.WriteAllBytesAsync(
                workspace.Layout.CanonicalCopyPlanPath,
                AtlasIntakeContracts.SerializeCopyPlan(shiftedCopyPlan),
                TestContext.Current.CancellationToken);
        }

        AtlasPrivateArtifactInventoryDocument shiftedInventory = inventory.Document with
        {
            Artifacts =
            [
                .. inventory.Document.Artifacts.Select(artifact => artifact with
                {
                    ArtifactAlias = RebindAlias(artifact.ArtifactAlias, aliasMap),
                    LineageAliases =
                    [
                        .. artifact.LineageAliases.Select(alias =>
                            RebindAlias(alias, aliasMap)),
                    ],
                }),
            ],
        };
        await File.WriteAllBytesAsync(
            workspace.Layout.CanonicalInventoryPath,
            AtlasIntakeContracts.SerializeInventory(shiftedInventory),
            TestContext.Current.CancellationToken);
        string copyPlanSha256 = AtlasSyntheticWorkspace.ComputeSha256(
            workspace.Layout.CanonicalCopyPlanPath);
        AtlasIntakeStateDocument shiftedState = state.Document with
        {
            StateArtifactAlias = RebindAlias(state.Document.StateArtifactAlias, aliasMap),
            InventorySha256 = AtlasSyntheticWorkspace.ComputeSha256(
                workspace.Layout.CanonicalInventoryPath),
            DocumentBindings =
            [
                .. state.Document.DocumentBindings.Select(binding => binding with
                {
                    ArtifactAlias = RebindAlias(binding.ArtifactAlias, aliasMap),
                    Sha256 = StringComparer.Ordinal.Equals(
                        binding.Role,
                        AtlasIntakeContracts.CopyPlanRole)
                        ? copyPlanSha256
                        : binding.Sha256,
                }),
            ],
            ArtifactBindings =
            [
                .. state.Document.ArtifactBindings.Select(binding => binding with
                {
                    ArtifactAlias = RebindAlias(binding.ArtifactAlias, aliasMap),
                }),
            ],
        };
        await File.WriteAllBytesAsync(
            statePath,
            AtlasIntakeContracts.SerializeState(shiftedState),
            TestContext.Current.CancellationToken);
    }

    private static async Task RebindBaselineCustodyAliasAsync(
        AtlasSyntheticWorkspace workspace)
    {
        AtlasLoadedDocument<AtlasPrivateArtifactInventoryDocument> inventory =
            await AtlasIntakeContracts.ReadInventoryAsync(
                workspace.Layout.CanonicalInventoryPath,
                TestContext.Current.CancellationToken);
        AtlasLoadedDocument<AtlasIntakeStateDocument> state =
            await AtlasIntakeContracts.ReadStateAsync(
                workspace.Layout.CanonicalDiscoveredStatePath,
                TestContext.Current.CancellationToken);
        string requestAlias = inventory.Document.Artifacts.Single(artifact =>
            StringComparer.Ordinal.Equals(
                artifact.Purpose,
                AtlasIntakeContracts.DiscoverRequestPurpose)).ArtifactAlias;
        AtlasPrivateArtifactInventoryDocument reboundInventory = inventory.Document with
        {
            Artifacts =
            [
                .. inventory.Document.Artifacts.Select(artifact =>
                    StringComparer.Ordinal.Equals(
                        artifact.Purpose,
                        AtlasIntakeContracts.ManifestRevision4Purpose)
                        ? artifact with { LineageAliases = [requestAlias] }
                        : artifact),
            ],
        };
        await File.WriteAllBytesAsync(
            workspace.Layout.CanonicalInventoryPath,
            AtlasIntakeContracts.SerializeInventory(reboundInventory),
            TestContext.Current.CancellationToken);
        AtlasIntakeStateDocument reboundState = state.Document with
        {
            InventorySha256 = AtlasSyntheticWorkspace.ComputeSha256(
                workspace.Layout.CanonicalInventoryPath),
            DocumentBindings =
            [
                .. state.Document.DocumentBindings.Select(binding =>
                    StringComparer.Ordinal.Equals(
                        binding.Role,
                        AtlasIntakeContracts.BaselineManifestRole)
                        ? binding with { ArtifactAlias = requestAlias }
                        : binding),
            ],
        };
        await File.WriteAllBytesAsync(
            workspace.Layout.CanonicalDiscoveredStatePath,
            AtlasIntakeContracts.SerializeState(reboundState),
            TestContext.Current.CancellationToken);
    }

    private static string RebindAlias(
        string alias,
        Dictionary<string, string> aliasMap) =>
        aliasMap.TryGetValue(alias, out string? replacement) ? replacement : alias;

    private static async Task MutateBaselineManifestAsync(
        AtlasSyntheticWorkspace workspace,
        Action<JsonObject> mutate)
    {
        JsonObject json = (JsonNode.Parse(await File.ReadAllBytesAsync(
                workspace.Layout.CanonicalBaselineManifestPath,
                TestContext.Current.CancellationToken))
            ?.AsObject())
            ?? throw new InvalidOperationException("The baseline manifest is required.");
        mutate(json);
        await File.WriteAllTextAsync(
            workspace.Layout.CanonicalBaselineManifestPath,
            json.ToJsonString(new JsonSerializerOptions { WriteIndented = true }),
            new UTF8Encoding(false),
            TestContext.Current.CancellationToken);
        workspace.WriteRequest(workspace.CreateDiscoveryRequest());
    }

    private static AtlasPrivateArtifactInventoryDocument ReplaceBaselineManifestArtifact(
        AtlasPrivateArtifactInventoryDocument inventory,
        Func<AtlasPrivateArtifactEntry, AtlasPrivateArtifactEntry> mutate) => inventory with
        {
            Artifacts =
            [
                .. inventory.Artifacts.Select(artifact =>
                    StringComparer.Ordinal.Equals(
                        artifact.Purpose,
                        AtlasIntakeContracts.ManifestRevision3Purpose)
                        ? mutate(artifact)
                        : artifact),
            ],
        };

    private static AtlasPrivateArtifactInventoryDocument ReplaceRecoveryAlias(
        AtlasPrivateArtifactInventoryDocument inventory,
        string purpose,
        string replacementAlias,
        string? dependentPurpose = null) => inventory with
        {
            Artifacts =
            [
                .. inventory.Artifacts.Select(artifact =>
                {
                    if (StringComparer.Ordinal.Equals(artifact.Purpose, purpose))
                    {
                        return artifact with { ArtifactAlias = replacementAlias };
                    }

                    if (dependentPurpose is not null
                        && StringComparer.Ordinal.Equals(artifact.Purpose, dependentPurpose))
                    {
                        return artifact with
                        {
                            LineageAliases =
                            [
                                .. artifact.LineageAliases.Select(lineageAlias =>
                                    StringComparer.Ordinal.Equals(
                                        lineageAlias,
                                        inventory.Artifacts.Single(current =>
                                            StringComparer.Ordinal.Equals(
                                                current.Purpose,
                                                purpose)).ArtifactAlias)
                                        ? replacementAlias
                                        : lineageAlias),
                            ],
                        };
                    }

                    return artifact;
                }),
            ],
        };

    private static AtlasPrivateArtifactInventoryDocument SwapRecoveryAliases(
        AtlasPrivateArtifactInventoryDocument inventory,
        string firstPurpose,
        string secondPurpose)
    {
        AtlasPrivateArtifactEntry first = inventory.Artifacts.Single(artifact =>
            StringComparer.Ordinal.Equals(artifact.Purpose, firstPurpose));
        AtlasPrivateArtifactEntry second = inventory.Artifacts.Single(artifact =>
            StringComparer.Ordinal.Equals(artifact.Purpose, secondPurpose));
        return inventory with
        {
            Artifacts =
            [
                .. inventory.Artifacts.Select(artifact =>
                {
                    if (StringComparer.Ordinal.Equals(artifact.Purpose, firstPurpose))
                    {
                        return artifact with { ArtifactAlias = second.ArtifactAlias };
                    }

                    if (StringComparer.Ordinal.Equals(artifact.Purpose, secondPurpose))
                    {
                        return artifact with { ArtifactAlias = first.ArtifactAlias };
                    }

                    return artifact;
                }),
            ],
        };
    }

    private static void SwapArrayItems(JsonArray array, int firstIndex, int secondIndex)
    {
        JsonNode? first = array[firstIndex]?.DeepClone();
        JsonNode? second = array[secondIndex]?.DeepClone();
        array[firstIndex] = second;
        array[secondIndex] = first;
    }

    private static string CreateTemporaryDirectory()
    {
        string path = Path.Combine(
            Path.GetTempPath(),
            "atlas-a2-definition-rules",
            Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path);
        return path;
    }
}
