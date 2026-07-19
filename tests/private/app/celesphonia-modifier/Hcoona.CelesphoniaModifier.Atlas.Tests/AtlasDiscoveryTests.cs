using System.Text;
using System.Text.Json.Nodes;
using Hcoona.CelesphoniaModifier.Atlas;
using Xunit;

namespace Hcoona.CelesphoniaModifier.Atlas.Tests;

public sealed class AtlasDiscoveryTests
{
    private const string OverlappingIncludeGroupId = "z-json-include";
    private const string OverlappingExcludeGroupId = "a-data-exclude";
    private const string OverlappingNotesGroupId = "m-notes-exclude";

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
    public async Task OverlappingDefinitionRuleOrderPersistsThroughQualification()
    {
        await using AtlasSyntheticWorkspace workspace = await AtlasSyntheticWorkspace.CreateAsync();
        await RewriteBaselineManifestWithOverlappingDefinitionRulesAsync(
            workspace,
            reverseGroupOrder: false);

        await AtlasDiscovery.DiscoverAsync(
            workspace.Layout.CanonicalDiscoverRequestPath,
            TestContext.Current.CancellationToken);

        AtlasLoadedDocument<AtlasCorpusIntakeManifest> pendingManifest =
            await AtlasIntakeContracts.ReadManifestAsync(
                workspace.Layout.CanonicalPendingManifestPath,
                TestContext.Current.CancellationToken);
        string[] expectedGroupOrder =
        [
            OverlappingIncludeGroupId,
            OverlappingNotesGroupId,
            OverlappingExcludeGroupId,
        ];
        Assert.Equal(
            expectedGroupOrder,
            pendingManifest.Document.DefinitionGroups
                .Select(static group => group.GroupId)
                .ToArray());

        workspace.WriteRequest(workspace.CreateConfirmationRequest());
        await AtlasDiscovery.ConfirmAsync(
            workspace.Layout.CanonicalConfirmRequestPath,
            TestContext.Current.CancellationToken);

        AtlasLoadedDocument<AtlasCorpusIntakeManifest> approvedManifest =
            await AtlasIntakeContracts.ReadManifestAsync(
                workspace.Layout.CanonicalApprovedManifestPath,
                TestContext.Current.CancellationToken);
        Assert.Equal(
            expectedGroupOrder,
            approvedManifest.Document.DefinitionGroups
                .Select(static group => group.GroupId)
                .ToArray());

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

    [Fact]
    public async Task DiscoverAsyncRejectsReversedOverlappingDefinitionRuleOrder()
    {
        await using AtlasSyntheticWorkspace workspace = await AtlasSyntheticWorkspace.CreateAsync();
        await RewriteBaselineManifestWithOverlappingDefinitionRulesAsync(
            workspace,
            reverseGroupOrder: true);

        AtlasSafetyException exception = await Assert.ThrowsAsync<AtlasSafetyException>(
            () => AtlasDiscovery.DiscoverAsync(
                workspace.Layout.CanonicalDiscoverRequestPath,
                TestContext.Current.CancellationToken).AsTask());

        Assert.Contains("classification changed", exception.Message, StringComparison.Ordinal);
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

        await Assert.ThrowsAsync<AtlasSafetyException>(
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

    [Fact]
    public async Task DiscoverAsyncRejectsNonCanonicalRevisionDirectory()
    {
        await using AtlasSyntheticWorkspace workspace = await AtlasSyntheticWorkspace.CreateAsync();
        AtlasIntakeDiscoveryRequest request = workspace.CreateDiscoveryRequest() with
        {
            ManifestRevisionDirectory = Path.Combine(workspace.Layout.IntakeDirectory, "alternate"),
        };
        workspace.WriteRequest(request);

        await Assert.ThrowsAsync<AtlasSafetyException>(
            () => AtlasDiscovery.DiscoverAsync(
                workspace.Layout.CanonicalDiscoverRequestPath,
                TestContext.Current.CancellationToken).AsTask());
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

    private static async Task RewriteBaselineManifestWithOverlappingDefinitionRulesAsync(
        AtlasSyntheticWorkspace workspace,
        bool reverseGroupOrder)
    {
        AtlasLoadedDocument<AtlasCorpusIntakeManifest> baselineManifest =
            await AtlasIntakeContracts.ReadManifestAsync(
                workspace.Layout.CanonicalBaselineManifestPath,
                TestContext.Current.CancellationToken);
        AtlasManifestDefinitionGroup[] definitionGroups = CreateOverlappingDefinitionGroups(
            reverseGroupOrder);
        AtlasManifestDefinitionEntry[] definitionEntries =
            baselineManifest.Document.DefinitionEntries
                .Select(entry => entry with
                {
                    GroupId = IsSyntheticDataDefinition(entry)
                        ? OverlappingIncludeGroupId
                        : OverlappingNotesGroupId,
                    Decision = IsSyntheticDataDefinition(entry)
                        ? AtlasIntakeContracts.IncludeDefinitionDecision
                        : AtlasIntakeContracts.ExcludeDefinitionDecision,
                })
                .ToArray();
        AtlasCorpusIntakeManifest updatedManifest = baselineManifest.Document with
        {
            DefinitionGroups = definitionGroups,
            DefinitionEntries = definitionEntries,
        };
        await File.WriteAllBytesAsync(
            workspace.Layout.CanonicalBaselineManifestPath,
            AtlasIntakeContracts.SerializeManifest(updatedManifest),
            TestContext.Current.CancellationToken);
        workspace.WriteRequest(workspace.CreateDiscoveryRequest());
    }

    private static AtlasManifestDefinitionGroup[] CreateOverlappingDefinitionGroups(
        bool reverseGroupOrder)
    {
        AtlasManifestDefinitionGroup[] groups =
        [
            new AtlasManifestDefinitionGroup
            {
                GroupId = OverlappingIncludeGroupId,
                SelectionRule = "www/**/*.json",
                DiscoveredCount = AtlasIntakeContracts.ExactIncludedDefinitionCount,
                Decision = AtlasIntakeContracts.IncludeDefinitionDecision,
            },
            new AtlasManifestDefinitionGroup
            {
                GroupId = OverlappingNotesGroupId,
                SelectionRule = "www/notes/*.txt",
                DiscoveredCount = AtlasIntakeContracts.ExactExcludedDefinitionCount,
                Decision = AtlasIntakeContracts.ExcludeDefinitionDecision,
            },
            new AtlasManifestDefinitionGroup
            {
                GroupId = OverlappingExcludeGroupId,
                SelectionRule = "www/data/*.json",
                DiscoveredCount = 0,
                Decision = AtlasIntakeContracts.ExcludeDefinitionDecision,
            },
        ];
        return reverseGroupOrder ? [.. groups.Reverse()] : groups;
    }

    private static bool IsSyntheticDataDefinition(AtlasManifestDefinitionEntry entry) =>
        AtlasIntakeContracts.NormalizeRelativePath(entry.RelativePath)
            .StartsWith("www/data/", StringComparison.Ordinal);

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
