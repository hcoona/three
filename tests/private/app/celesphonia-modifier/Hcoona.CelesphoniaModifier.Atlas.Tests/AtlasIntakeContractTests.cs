using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Hcoona.CelesphoniaModifier.Atlas;
using Xunit;

namespace Hcoona.CelesphoniaModifier.Atlas.Tests;

public sealed class AtlasIntakeContractTests
{
    [Fact]
    public async Task DiscoveryRequestRejectsDuplicateProperty()
    {
        await using AtlasSyntheticWorkspace workspace = await AtlasSyntheticWorkspace.CreateAsync();
        string json = """
            {
              "schemaVersion":"atlas-intake-discovery-request/v1",
              "schemaVersion":"atlas-intake-discovery-request/v1"
            }
            """;
        await File.WriteAllTextAsync(
            workspace.Layout.CanonicalDiscoverRequestPath,
            json,
            Encoding.UTF8,
            TestContext.Current.CancellationToken);

        await Assert.ThrowsAsync<AtlasRequestException>(
            () => AtlasIntakeContracts.ReadDiscoveryRequestAsync(
                workspace.Layout.CanonicalDiscoverRequestPath,
                TestContext.Current.CancellationToken).AsTask());
    }

    [Fact]
    public async Task DiscoveryRequestRejectsCommentsTrailingCommaAndTrailingJson()
    {
        await using AtlasSyntheticWorkspace workspace = await AtlasSyntheticWorkspace.CreateAsync();
        string json = """
            {
              "schemaVersion":"atlas-intake-discovery-request/v1", // comment
            }
            {}
            """;
        await File.WriteAllTextAsync(
            workspace.Layout.CanonicalDiscoverRequestPath,
            json,
            new UTF8Encoding(encoderShouldEmitUTF8Identifier: false),
            TestContext.Current.CancellationToken);

        await Assert.ThrowsAsync<AtlasRequestException>(
            () => AtlasIntakeContracts.ReadDiscoveryRequestAsync(
                workspace.Layout.CanonicalDiscoverRequestPath,
                TestContext.Current.CancellationToken).AsTask());
    }

    [Fact]
    public async Task DiscoveryRequestRejectsUnknownProperty()
    {
        await using AtlasSyntheticWorkspace workspace = await AtlasSyntheticWorkspace.CreateAsync();
        AtlasIntakeDiscoveryRequest request = workspace.CreateDiscoveryRequest();
        string json = Encoding.UTF8.GetString(AtlasIntakeContracts.SerializeRequest(request));
        json = json[..^1] + ",\"unknown\":1}";
        await File.WriteAllTextAsync(
            workspace.Layout.CanonicalDiscoverRequestPath,
            json,
            Encoding.UTF8,
            TestContext.Current.CancellationToken);

        await Assert.ThrowsAsync<AtlasRequestException>(
            () => AtlasIntakeContracts.ReadDiscoveryRequestAsync(
                workspace.Layout.CanonicalDiscoverRequestPath,
                TestContext.Current.CancellationToken).AsTask());
    }

    [Fact]
    public void PathPolicyRejectsReparseAndNonFixedDrive()
    {
        AtlasIoSeams reparseIo = new()
        {
            GetAttributes = _ => FileAttributes.Directory | FileAttributes.ReparsePoint,
            GetDriveInfo = _ => new AtlasDriveInfo(true, DriveType.Fixed),
            DirectoryExists = _ => true,
            FileExists = _ => false,
        };

        Assert.Throws<AtlasSafetyException>(() =>
            AtlasDiscovery.ValidateExistingOrdinaryDirectory(@"Q:\synthetic", reparseIo));

        AtlasIoSeams removableIo = new()
        {
            GetAttributes = _ => FileAttributes.Directory,
            GetDriveInfo = _ => new AtlasDriveInfo(true, DriveType.Removable),
            DirectoryExists = _ => true,
            FileExists = _ => false,
        };

        Assert.Throws<AtlasSafetyException>(() =>
            AtlasDiscovery.ValidateExistingOrdinaryDirectory(@"Q:\synthetic", removableIo));
    }

    [Fact]
    public async Task PrivateWorkspaceGitIgnoreAllowsOnlyExactNormalizedPolicy()
    {
        await using AtlasSyntheticWorkspace workspace = await AtlasSyntheticWorkspace.CreateAsync();
        await File.WriteAllTextAsync(
            workspace.Layout.PrivateGitIgnorePath,
            "*\r\n!.gitignore\r\n",
            new UTF8Encoding(false),
            TestContext.Current.CancellationToken);

        AtlasDiscovery.ValidatePrivateWorkspace(workspace.Layout, AtlasIoSeams.Default);

        string[] invalidPolicies =
        [
            "*\n!.gitignore\n!snapshot/\n",
            "*\n!.gitignore\n# comment\n",
            "*\n!.gitignore\n\n",
        ];

        foreach (string invalidPolicy in invalidPolicies)
        {
            await File.WriteAllTextAsync(
                workspace.Layout.PrivateGitIgnorePath,
                invalidPolicy,
                new UTF8Encoding(false),
                TestContext.Current.CancellationToken);

            Assert.Throws<AtlasSafetyException>(() =>
                AtlasDiscovery.ValidatePrivateWorkspace(
                    workspace.Layout,
                    AtlasIoSeams.Default));
        }

        AtlasIoSeams bomIo = AtlasTestSupport.CreateIo(
            readAllText: _ => "\uFEFF*\n!.gitignore\n");
        Assert.Throws<AtlasSafetyException>(() =>
            AtlasDiscovery.ValidatePrivateWorkspace(workspace.Layout, bomIo));
    }

    [Fact]
    public async Task OutputSchemasCoverSerializedTopLevelProperties()
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
        workspace.WriteRequest(workspace.CreatePreflightRequest());
        await PrivateArtifactLifecycle.CleanupPreflightAsync(
            workspace.Layout.CanonicalCleanupPreflightRequestPath,
            TestContext.Current.CancellationToken);

        byte[] sourceRootMapBytes = await File.ReadAllBytesAsync(
            workspace.Layout.CanonicalSourceRootMapPath,
            TestContext.Current.CancellationToken);
        byte[] copyPlanBytes = await File.ReadAllBytesAsync(
            workspace.Layout.CanonicalCopyPlanPath,
            TestContext.Current.CancellationToken);
        byte[] stateBytes = await File.ReadAllBytesAsync(
            workspace.Layout.CanonicalPreflightedStatePath,
            TestContext.Current.CancellationToken);
        byte[] receiptBytes = await File.ReadAllBytesAsync(
            workspace.Layout.CanonicalCopyReceiptPath,
            TestContext.Current.CancellationToken);
        byte[] reportBytes = await File.ReadAllBytesAsync(
            workspace.Layout.CanonicalCleanupPreflightReportPath,
            TestContext.Current.CancellationToken);

        AssertSchemaHasTopLevelProperties("source-root-map.schema.json", sourceRootMapBytes);
        AssertSchemaHasTopLevelProperties("copy-plan.schema.json", copyPlanBytes);
        AssertSchemaHasTopLevelProperties("intake-state.schema.json", stateBytes);
        AssertSchemaHasTopLevelProperties("copy-receipt.schema.json", receiptBytes);
        AssertSchemaHasTopLevelProperties("cleanup-preflight-report.schema.json", reportBytes);
    }

    [Fact]
    public async Task PipelineUsesExactApprovedCensus()
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
        workspace.WriteRequest(workspace.CreatePreflightRequest());
        await PrivateArtifactLifecycle.CleanupPreflightAsync(
            workspace.Layout.CanonicalCleanupPreflightRequestPath,
            TestContext.Current.CancellationToken);

        AtlasLoadedDocument<AtlasCorpusIntakeManifest> pendingManifest =
            await AtlasIntakeContracts.ReadManifestAsync(
                workspace.Layout.CanonicalPendingManifestPath,
                TestContext.Current.CancellationToken);
        AtlasLoadedDocument<AtlasCopyPlanDocument> copyPlan =
            await AtlasIntakeContracts.ReadCopyPlanAsync(
                workspace.Layout.CanonicalCopyPlanPath,
                TestContext.Current.CancellationToken);
        AtlasLoadedDocument<AtlasCopyReceiptDocument> receipt =
            await AtlasIntakeContracts.ReadCopyReceiptAsync(
                workspace.Layout.CanonicalCopyReceiptPath,
                TestContext.Current.CancellationToken);
        AtlasLoadedDocument<AtlasCleanupPreflightReportDocument> report =
            await AtlasIntakeContracts.ReadCleanupPreflightReportAsync(
                workspace.Layout.CanonicalCleanupPreflightReportPath,
                TestContext.Current.CancellationToken);
        AtlasLoadedDocument<AtlasPrivateArtifactInventoryDocument> preflightBackup =
            await AtlasIntakeContracts.ReadInventoryAsync(
                workspace.Layout.CanonicalPreflightedInventoryBackupPath,
                TestContext.Current.CancellationToken);
        AtlasLoadedDocument<AtlasPrivateArtifactInventoryDocument> inventory =
            await AtlasIntakeContracts.ReadInventoryAsync(
                workspace.Layout.CanonicalInventoryPath,
                TestContext.Current.CancellationToken);

        Assert.Equal(
            AtlasIntakeContracts.ExactSaveEntryCount,
            pendingManifest.Document.SaveEntries.Length);
        Assert.Equal(
            AtlasIntakeContracts.ExactIncludedSaveCount,
            pendingManifest.Document.IncludedSaveCount);
        Assert.Equal(
            AtlasIntakeContracts.ExactDefinitionEntryCount,
            pendingManifest.Document.DefinitionEntries.Length);
        Assert.Equal(
            AtlasIntakeContracts.ExactIncludedDefinitionCount,
            pendingManifest.Document.IncludedDefinitionCount);
        Assert.Equal(
            AtlasIntakeContracts.ExactIncludedSaveCount
            + AtlasIntakeContracts.ExactIncludedDefinitionCount,
            copyPlan.Document.Entries.Length);
        Assert.Equal(copyPlan.Document.Entries.Length, receipt.Document.Entries.Length);
        Assert.Equal(
            preflightBackup.Document.Artifacts.Length,
            report.Document.Results.Length);
        Assert.Equal(
            preflightBackup.Document.Artifacts.Length + 4,
            inventory.Document.Artifacts.Length);
    }

    [Fact]
    public async Task InventoryReadRejectsInvalidLifecycleRows()
    {
        await using AtlasSyntheticWorkspace workspace = await AtlasSyntheticWorkspace.CreateAsync();
        byte[] originalBytes = await File.ReadAllBytesAsync(
            workspace.Layout.CanonicalInventoryPath,
            TestContext.Current.CancellationToken);

        await AssertRejectedInventoryMutationAsync(
            originalBytes,
            workspace.Layout.CanonicalInventoryPath,
            json =>
                ((JsonObject)((JsonArray)json["artifacts"]!)[0]!)["artifactClass"]
                    = "invalid-class");
        await AssertRejectedInventoryMutationAsync(
            originalBytes,
            workspace.Layout.CanonicalInventoryPath,
            json =>
                ((JsonObject)((JsonArray)json["artifacts"]!)[0]!)["status"] = "unknown-status");
        await AssertRejectedInventoryMutationAsync(
            originalBytes,
            workspace.Layout.CanonicalInventoryPath,
            json =>
                ((JsonObject)((JsonArray)json["artifacts"]!)[0]!)["plannedDisposition"]
                    = "retain-public");
        await AssertRejectedInventoryMutationAsync(
            originalBytes,
            workspace.Layout.CanonicalInventoryPath,
            json => ((JsonObject)((JsonArray)json["artifacts"]!)[0]!)["expiryCondition"] = "");
        await AssertRejectedInventoryMutationAsync(
            originalBytes,
            workspace.Layout.CanonicalInventoryPath,
            json =>
                ((JsonObject)((JsonArray)json["artifacts"]!)[0]!)["lineageAliases"] =
                    new JsonArray(AtlasSyntheticWorkspace.BaselineManifestArtifactAlias));
        await AssertRejectedInventoryMutationAsync(
            originalBytes,
            workspace.Layout.CanonicalInventoryPath,
            json =>
            {
                JsonArray artifacts = (JsonArray)json["artifacts"]!;
                artifacts.Add(new JsonObject
                {
                    ["artifactAlias"] = "private-artifact-999999",
                    ["artifactClass"] = AtlasIntakeContracts.SaveCopyArtifactClass,
                    ["purpose"] = "snapshot-copy:save-source-0001",
                    ["custodianRole"] = AtlasIntakeContracts.ProjectLeaderRole,
                    ["lineageAliases"] =
                        new JsonArray(AtlasSyntheticWorkspace.BaselineManifestArtifactAlias),
                    ["lastUseMilestone"] = "A8",
                    ["expiryCondition"] = "after:A8",
                    ["plannedDisposition"] = AtlasIntakeContracts.DeleteDisposition,
                    ["status"] = AtlasIntakeContracts.PresentArtifactStatus,
                    ["verificationMethod"] =
                        AtlasIntakeContracts.TrustedLocalFilesystemProfile
                        + ";receipt:private-artifact-999998",
                    ["qualification"] = null,
                });
            });
    }

    [Fact]
    public async Task CleanupReportSchemaMatchesRuntimeValues()
    {
        await using AtlasSyntheticWorkspace workspace = await AtlasSyntheticWorkspace.CreateAsync();
        AtlasLoadedDocument<AtlasPrivateArtifactInventoryDocument> inventory =
            await AtlasIntakeContracts.ReadInventoryAsync(
                workspace.Layout.CanonicalInventoryPath,
                TestContext.Current.CancellationToken);
        AtlasCleanupPreflightRequest request = new()
        {
            SchemaVersion = AtlasIntakeContracts.CleanupPreflightRequestSchemaVersion,
            SurveyAlias = AtlasSyntheticWorkspace.SurveyAlias,
            ProposedMilestone = "A8",
        };
        AtlasCleanupPreflightReportDocument report = PrivateArtifactLifecycle.CreateCleanupReport(
            request,
            inventory.Document with
            {
                Artifacts =
                [
                    inventory.Document.Artifacts[0] with
                    {
                        Status = AtlasIntakeContracts.LastUseCompleteArtifactStatus,
                        LastUseMilestone = "A8",
                        PlannedDisposition = AtlasIntakeContracts.DeleteDisposition,
                        ExpiryCondition = "later",
                    },
                ],
            },
            "private-artifact-000099");
        _ = AtlasIntakeContracts.SerializeCleanupPreflightReport(report);

        using JsonDocument schema = JsonDocument.Parse(File.ReadAllBytes(
            AtlasSyntheticWorkspace.GetSchemaPath("cleanup-preflight-report.schema.json")));
        JsonElement properties = schema.RootElement
            .GetProperty("$defs")
            .GetProperty("result")
            .GetProperty("properties");
        HashSet<string> artifactClasses = GetEnumValues(properties.GetProperty("artifactClass"));
        HashSet<string> statuses = GetEnumValues(properties.GetProperty("status"));
        HashSet<string> dispositions = GetEnumValues(properties.GetProperty("plannedDisposition"));
        HashSet<string> milestones = GetEnumValues(properties.GetProperty("lastUseMilestone"));
        HashSet<string> results = GetEnumValues(properties.GetProperty("result"));

        Assert.Equal(
            "string",
            properties.GetProperty("expiryCondition").GetProperty("type").GetString());
        Assert.Equal(
            1,
            properties.GetProperty("expiryCondition").GetProperty("minLength").GetInt32());
        Assert.Contains("indeterminate-expiry", results);
        Assert.All(
            report.Results,
            result =>
            {
                Assert.Contains(result.ArtifactClass, artifactClasses);
                Assert.Contains(result.Status, statuses);
                Assert.Contains(result.PlannedDisposition, dispositions);
                Assert.Contains(result.LastUseMilestone, milestones);
                Assert.Contains(result.Result, results);
                Assert.False(string.IsNullOrWhiteSpace(result.ExpiryCondition));
            });
        Assert.Equal("indeterminate-expiry", report.Results[0].Result);
    }

    private static void AssertSchemaHasTopLevelProperties(string schemaName, byte[] documentBytes)
    {
        using JsonDocument schema = JsonDocument.Parse(File.ReadAllBytes(
            AtlasSyntheticWorkspace.GetSchemaPath(schemaName)));
        using JsonDocument document = JsonDocument.Parse(documentBytes);
        HashSet<string> propertyNames = schema.RootElement.GetProperty("properties")
            .EnumerateObject()
            .Select(static property => property.Name)
            .ToHashSet(StringComparer.Ordinal);
        string[] documentPropertyNames = document.RootElement.EnumerateObject()
            .Select(static property => property.Name)
            .ToArray();

        Assert.All(
            documentPropertyNames,
            propertyName => Assert.Contains(propertyName, propertyNames));
    }

    private static async Task AssertRejectedInventoryMutationAsync(
        byte[] originalBytes,
        string inventoryPath,
        Action<JsonObject> mutate)
    {
        JsonNode? node = JsonNode.Parse(originalBytes);
        JsonObject json = node as JsonObject
            ?? throw new InvalidOperationException("Expected a JSON object.");
        mutate(json);
        await AtlasTestSupport.WriteJsonAsync(
            inventoryPath,
            json,
            TestContext.Current.CancellationToken);

        await Assert.ThrowsAsync<AtlasSafetyException>(
            () => AtlasIntakeContracts.ReadInventoryAsync(
                inventoryPath,
                TestContext.Current.CancellationToken).AsTask());
    }

    private static HashSet<string> GetEnumValues(JsonElement schemaProperty) =>
        schemaProperty.GetProperty("enum")
            .EnumerateArray()
            .Select(static value => value.GetString()!)
            .ToHashSet(StringComparer.Ordinal);
}

internal sealed class AtlasSyntheticWorkspace : IAsyncDisposable
{
    internal const string SurveyAlias = "survey-000001";
    internal const string BaselineManifestArtifactAlias =
        "private-artifact-000010";
    private const string ApprovalCommit = "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa";

    private readonly string rootPath;

    private AtlasSyntheticWorkspace(string rootPath)
    {
        this.rootPath = rootPath;
        Layout = AtlasIntakeContracts.CreateWorkspaceLayout(
            ProjectRoot,
            WorkspaceRoot,
            SurveyAlias);
        SaveRootPath = Path.Combine(GameRootPath, "save");
        WebSaveRootPath = Path.Combine(GameRootPath, "www", "save");
        DefinitionRootPath = GameRootPath;
        GameExecutablePath = Path.Combine(GameRootPath, "Game.exe");
    }

    public AtlasWorkspaceLayout Layout { get; }

    public string DefinitionRootPath { get; }

    public string GameExecutablePath { get; }

    public string GameRootPath => Path.Combine(rootPath, "synthetic-game");

    public string ProjectRoot => rootPath;

    public string SaveRootPath { get; }

    public string WebSaveRootPath { get; }

    public string WorkspaceRoot => Path.Combine(
        rootPath,
        "src",
        "private",
        "app",
        "celesphonia-modifier",
        ".private",
        "atlas-v0",
        SurveyAlias);

    public static async Task<AtlasSyntheticWorkspace> CreateAsync()
    {
        string root = Path.Combine(
            Path.GetTempPath(),
            "atlas-a2-tests",
            Guid.NewGuid().ToString("N"));
        AtlasSyntheticWorkspace workspace = new(root);
        await workspace.InitializeAsync();
        return workspace;
    }

    public static string GetSchemaPath(string schemaName) => Path.Combine(
        FindRepositoryRoot(),
        "src",
        "private",
        "app",
        "celesphonia-modifier",
        "docs",
        ".copilot",
        "schemas",
        "atlas-v0",
        schemaName);

    public AtlasIntakeConfirmationRequest CreateConfirmationRequest(
        string decisionCommit = "bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb")
    {
        string discoveredStatePath = Layout.CanonicalDiscoveredStatePath;
        string inventoryPath = Layout.CanonicalInventoryPath;
        return new AtlasIntakeConfirmationRequest
        {
            SchemaVersion = AtlasIntakeContracts.ConfirmationRequestSchemaVersion,
            SurveyAlias = SurveyAlias,
            ProjectRoot = ProjectRoot,
            WorkspaceRoot = WorkspaceRoot,
            DiscoveredStatePath = discoveredStatePath,
            ExpectedDiscoveredStateSha256 = ComputeSha256(discoveredStatePath),
            PendingManifestPath = Layout.CanonicalPendingManifestPath,
            SourceRootMapPath = Layout.CanonicalSourceRootMapPath,
            CopyPlanPath = Layout.CanonicalCopyPlanPath,
            DecisionCommit = decisionCommit,
            ManifestRevisionDirectory = Layout.ManifestRevisionDirectory,
            StateRevisionDirectory = Layout.StatesDirectory,
            InventoryPath = inventoryPath,
            ExpectedInventorySha256 = ComputeSha256(inventoryPath),
            InventoryBackupPath = Layout.CanonicalApprovedInventoryBackupPath,
        };
    }

    public AtlasIntakeCopyRequest CreateCopyRequest(
        string decisionCommit = "bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb") =>
        new()
        {
            SchemaVersion = AtlasIntakeContracts.CopyRequestSchemaVersion,
            SurveyAlias = SurveyAlias,
            ProjectRoot = ProjectRoot,
            WorkspaceRoot = WorkspaceRoot,
            ApprovedStatePath = Layout.CanonicalApprovedStatePath,
            ExpectedApprovedStateSha256 = ComputeSha256(Layout.CanonicalApprovedStatePath),
            ApprovedManifestPath = Layout.CanonicalApprovedManifestPath,
            SourceRootMapPath = Layout.CanonicalSourceRootMapPath,
            CopyPlanPath = Layout.CanonicalCopyPlanPath,
            DecisionCommit = decisionCommit,
            IncompleteCopyPath = Layout.CanonicalIncompleteCopyPath,
            FinalCopyPath = Layout.CanonicalFinalCopyPath,
            StateRevisionDirectory = Layout.StatesDirectory,
            InventoryPath = Layout.CanonicalInventoryPath,
            ExpectedInventorySha256 = ComputeSha256(Layout.CanonicalInventoryPath),
            InventoryBackupPath = Layout.CanonicalQualifiedInventoryBackupPath,
        };

    public AtlasIntakeDiscoveryRequest CreateDiscoveryRequest() => new()
    {
        SchemaVersion = AtlasIntakeContracts.DiscoveryRequestSchemaVersion,
        SurveyAlias = SurveyAlias,
        ProjectRoot = ProjectRoot,
        WorkspaceRoot = WorkspaceRoot,
        BaselineManifestPath = Layout.CanonicalBaselineManifestPath,
        ExpectedBaselineSha256 = ComputeSha256(Layout.CanonicalBaselineManifestPath),
        ExpectedBaselineRevision = AtlasIntakeContracts.BaselineManifestRevision,
        NextManifestRevision = AtlasIntakeContracts.PendingManifestRevision,
        ManifestRevisionDirectory = Layout.ManifestRevisionDirectory,
        SaveRoots =
        [
            new AtlasRequestSaveRoot
            {
                LocationRole = AtlasIntakeContracts.DeploymentRootSaveRole,
                Path = SaveRootPath,
            },
            new AtlasRequestSaveRoot
            {
                LocationRole = AtlasIntakeContracts.WebRootSaveRole,
                Path = WebSaveRootPath,
            },
        ],
        DefinitionRoot = DefinitionRootPath,
        GameExecutablePath = GameExecutablePath,
        SourceRootMapOutputPath = Layout.CanonicalSourceRootMapPath,
        InventoryPath = Layout.CanonicalInventoryPath,
        ExpectedInventorySha256 = ComputeSha256(Layout.CanonicalInventoryPath),
        InventoryBackupPath = Layout.CanonicalDiscoveredInventoryBackupPath,
        CopyPlanOutputPath = Layout.CanonicalCopyPlanPath,
        StateRevisionDirectory = Layout.StatesDirectory,
        ExpectedSteamAppId = AtlasIntakeContracts.ExactSteamAppId,
        ExpectedBuildId = AtlasIntakeContracts.ExactBuildId,
    };

    public AtlasCleanupPreflightRequest CreatePreflightRequest() => new()
    {
        SchemaVersion = AtlasIntakeContracts.CleanupPreflightRequestSchemaVersion,
        SurveyAlias = SurveyAlias,
        ProjectRoot = ProjectRoot,
        WorkspaceRoot = WorkspaceRoot,
        QualifiedStatePath = Layout.CanonicalQualifiedStatePath,
        ExpectedQualifiedStateSha256 = ComputeSha256(Layout.CanonicalQualifiedStatePath),
        StateRevisionDirectory = Layout.StatesDirectory,
        InventoryPath = Layout.CanonicalInventoryPath,
        ExpectedInventorySha256 = ComputeSha256(Layout.CanonicalInventoryPath),
        InventoryBackupPath = Layout.CanonicalPreflightedInventoryBackupPath,
        ProposedMilestone = "A8",
        ReportOutputPath = Layout.CanonicalCleanupPreflightReportPath,
    };

    public async ValueTask DisposeAsync()
    {
        if (Directory.Exists(rootPath))
        {
            await Task.Run(() =>
            {
                foreach (string filePath in Directory.EnumerateFiles(
                             rootPath,
                             "*",
                             SearchOption.AllDirectories))
                {
                    File.SetAttributes(filePath, FileAttributes.Normal);
                }

                Directory.Delete(rootPath, true);
            });
        }
    }

    public static string ComputeSha256(string path) =>
        AtlasIntakeContracts.ComputeSha256Hex(File.ReadAllBytes(path));

    public void UpdateInventory(Func<AtlasPrivateArtifactInventoryDocument,
        AtlasPrivateArtifactInventoryDocument> update)
    {
        AtlasPrivateArtifactInventoryDocument inventory = AtlasIntakeContracts.ReadInventoryAsync(
            Layout.CanonicalInventoryPath,
            TestContext.Current.CancellationToken).AsTask().GetAwaiter().GetResult().Document;
        byte[] bytes = AtlasIntakeContracts.SerializeInventory(update(inventory));
        File.WriteAllBytes(Layout.CanonicalInventoryPath, bytes);
    }

    public void WriteRequest(AtlasIntakeDiscoveryRequest request) =>
        File.WriteAllBytes(
            Layout.CanonicalDiscoverRequestPath,
            AtlasIntakeContracts.SerializeRequest(request));

    public void WriteRequest(AtlasIntakeConfirmationRequest request) =>
        File.WriteAllBytes(
            Layout.CanonicalConfirmRequestPath,
            AtlasIntakeContracts.SerializeRequest(request));

    public void WriteRequest(AtlasIntakeCopyRequest request) =>
        File.WriteAllBytes(
            Layout.CanonicalCopyRequestPath,
            AtlasIntakeContracts.SerializeRequest(request));

    public void WriteRequest(AtlasCleanupPreflightRequest request) =>
        File.WriteAllBytes(
            Layout.CanonicalCleanupPreflightRequestPath,
            AtlasIntakeContracts.SerializeRequest(request));

    private static string FindRepositoryRoot()
    {
        DirectoryInfo? directory = new(AppContext.BaseDirectory);
        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "dirs.proj")))
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        throw new InvalidOperationException("Repository root was not found.");
    }

    private async Task InitializeAsync()
    {
        Directory.CreateDirectory(Path.Combine(
            rootPath,
            "src",
            "private",
            "app",
            "celesphonia-modifier",
            ".private"));
        Directory.CreateDirectory(Layout.RequestDirectory);
        Directory.CreateDirectory(Layout.ManifestRevisionDirectory);
        Directory.CreateDirectory(Layout.StatesDirectory);
        Directory.CreateDirectory(Layout.InventoryBackupsDirectory);
        Directory.CreateDirectory(Layout.CopiesDirectory);
        Directory.CreateDirectory(Layout.CleanupDirectory);
        Directory.CreateDirectory(SaveRootPath);
        Directory.CreateDirectory(WebSaveRootPath);
        Directory.CreateDirectory(Path.Combine(DefinitionRootPath, "www", "data"));
        Directory.CreateDirectory(Path.Combine(DefinitionRootPath, "www", "notes"));

        await File.WriteAllTextAsync(
            Layout.PrivateGitIgnorePath,
            "*\n!.gitignore\n",
            new UTF8Encoding(false),
            TestContext.Current.CancellationToken);
        foreach (int slot in Enumerable.Range(1, 19))
        {
            await File.WriteAllBytesAsync(
                Path.Combine(SaveRootPath, $"file{slot}.rpgsave"),
                [(byte)slot],
                TestContext.Current.CancellationToken);
        }

        await File.WriteAllBytesAsync(
            Path.Combine(SaveRootPath, "global.rpgsave"),
            [20],
            TestContext.Current.CancellationToken);
        await File.WriteAllBytesAsync(
            Path.Combine(SaveRootPath, "config.rpgsave"),
            [21],
            TestContext.Current.CancellationToken);
        await File.WriteAllTextAsync(
            Path.Combine(SaveRootPath, "steam_autocloud.vdf"),
            "steam",
            Encoding.UTF8,
            TestContext.Current.CancellationToken);
        await File.WriteAllTextAsync(
            Path.Combine(WebSaveRootPath, "steam_autocloud.vdf"),
            "steam",
            Encoding.UTF8,
            TestContext.Current.CancellationToken);
        await File.WriteAllTextAsync(
            GameExecutablePath,
            "Game",
            Encoding.UTF8,
            TestContext.Current.CancellationToken);
        foreach (int index in Enumerable.Range(
                     1,
                     AtlasIntakeContracts.ExactIncludedDefinitionCount))
        {
            await File.WriteAllTextAsync(
                Path.Combine(
                    DefinitionRootPath,
                    "www",
                    "data",
                    $"definition-{index:000000}.json"),
                "{}",
                Encoding.UTF8,
                TestContext.Current.CancellationToken);
        }

        foreach (int index in Enumerable.Range(
                     1,
                     AtlasIntakeContracts.ExactExcludedDefinitionCount))
        {
            await File.WriteAllTextAsync(
                Path.Combine(
                    DefinitionRootPath,
                    "www",
                    "notes",
                    $"excluded-{index:000000}.txt"),
                "excluded",
                Encoding.UTF8,
                TestContext.Current.CancellationToken);
        }

        AtlasCorpusIntakeManifest baselineManifest = CreateBaselineManifest();
        AtlasPrivateArtifactInventoryDocument inventory = CreateBaselineInventory();
        File.WriteAllBytes(
            Layout.CanonicalBaselineManifestPath,
            AtlasIntakeContracts.SerializeManifest(baselineManifest));
        File.WriteAllBytes(
            Layout.CanonicalInventoryPath,
            AtlasIntakeContracts.SerializeInventory(inventory));
        WriteRequest(CreateDiscoveryRequest());
    }

    private static AtlasPrivateArtifactInventoryDocument CreateBaselineInventory() => new()
    {
        SchemaVersion = AtlasIntakeContracts.InventorySchemaVersion,
        SurveyAlias = SurveyAlias,
        Artifacts =
        [
            new AtlasPrivateArtifactEntry
            {
                ArtifactAlias = BaselineManifestArtifactAlias,
                ArtifactClass = AtlasIntakeContracts.LiveDiscoveryArtifactClass,
                Purpose = AtlasIntakeContracts.ManifestRevision3Purpose,
                CustodianRole = AtlasIntakeContracts.ProjectLeaderRole,
                LineageAliases = [],
                LastUseMilestone = "A2",
                ExpiryCondition = "after:A2",
                PlannedDisposition = AtlasIntakeContracts.RetainPrivateDisposition,
                Status = AtlasIntakeContracts.PresentArtifactStatus,
                VerificationMethod = "atlas-intake/v2;r000003",
            },
        ],
    };

    private static AtlasCorpusIntakeManifest CreateBaselineManifest()
    {
        AtlasManifestSaveEntry[] saveEntries =
        [
            .. Enumerable.Range(1, 19).Select(index => new AtlasManifestSaveEntry
            {
                SourceAlias = $"save-source-{index:0000}",
                RootAlias = "save-root-0001",
                RelativePath = $"file{index}.rpgsave",
                Role = AtlasIntakeContracts.SlotSaveRole,
                SlotNumber = index,
                Decision = AtlasIntakeContracts.IncludeSaveDecision,
                EntryType = AtlasIntakeContracts.FileEntryType,
                IsReparsePoint = false,
            }),
            new AtlasManifestSaveEntry
            {
                SourceAlias = "save-source-0020",
                RootAlias = "save-root-0001",
                RelativePath = "global.rpgsave",
                Role = AtlasIntakeContracts.GlobalSaveRole,
                Decision = AtlasIntakeContracts.IncludeSaveDecision,
                EntryType = AtlasIntakeContracts.FileEntryType,
                IsReparsePoint = false,
            },
            new AtlasManifestSaveEntry
            {
                SourceAlias = "save-source-0021",
                RootAlias = "save-root-0001",
                RelativePath = "config.rpgsave",
                Role = AtlasIntakeContracts.ConfigSaveRole,
                Decision = AtlasIntakeContracts.IncludeSaveDecision,
                EntryType = AtlasIntakeContracts.FileEntryType,
                IsReparsePoint = false,
            },
            new AtlasManifestSaveEntry
            {
                SourceAlias = "save-source-0022",
                RootAlias = "save-root-0001",
                RelativePath = "steam_autocloud.vdf",
                Role = AtlasIntakeContracts.SteamAutoCloudSaveRole,
                Decision = AtlasIntakeContracts.ExcludeSteamAutoCloudDecision,
                EntryType = AtlasIntakeContracts.FileEntryType,
                IsReparsePoint = false,
            },
            new AtlasManifestSaveEntry
            {
                SourceAlias = "save-source-0023",
                RootAlias = "save-root-0002",
                RelativePath = "steam_autocloud.vdf",
                Role = AtlasIntakeContracts.SteamAutoCloudSaveRole,
                Decision = AtlasIntakeContracts.ExcludeSteamAutoCloudDecision,
                EntryType = AtlasIntakeContracts.FileEntryType,
                IsReparsePoint = false,
            },
        ];
        AtlasManifestDefinitionEntry[] definitionEntries =
        [
            .. Enumerable.Range(1, AtlasIntakeContracts.ExactIncludedDefinitionCount).Select(
                index => new AtlasManifestDefinitionEntry
                {
                    SourceAlias = $"definition-source-{index:000000}",
                    RelativePath = $"www/data/definition-{index:000000}.json",
                    GroupId = "data",
                    Decision = AtlasIntakeContracts.IncludeDefinitionDecision,
                    EntryType = AtlasIntakeContracts.FileEntryType,
                    IsReparsePoint = false,
                }),
            .. Enumerable.Range(1, AtlasIntakeContracts.ExactExcludedDefinitionCount).Select(
                index => new AtlasManifestDefinitionEntry
                {
                    SourceAlias =
                        "definition-source-"
                        + $"{AtlasIntakeContracts.ExactIncludedDefinitionCount + index:000000}",
                    RelativePath = $"www/notes/excluded-{index:000000}.txt",
                    GroupId = "auxiliary",
                    Decision = AtlasIntakeContracts.ExcludeDefinitionDecision,
                    EntryType = AtlasIntakeContracts.FileEntryType,
                    IsReparsePoint = false,
                }),
        ];
        return new AtlasCorpusIntakeManifest
        {
            SchemaVersion = AtlasIntakeContracts.IntakeManifestSchemaVersion,
            SurveyAlias = SurveyAlias,
            ManifestRevision = AtlasIntakeContracts.BaselineManifestRevision,
            SaveRoots =
            [
                new AtlasManifestSaveRoot
                {
                    RootAlias = "save-root-0001",
                    LocationRole = AtlasIntakeContracts.DeploymentRootSaveRole,
                    Activity = AtlasIntakeContracts.ActiveSaveRootActivity,
                    Decision = AtlasIntakeContracts.IncludeSaveRootDecision,
                    ObservedEntryCount = 22,
                    IsReparsePoint = false,
                },
                new AtlasManifestSaveRoot
                {
                    RootAlias = "save-root-0002",
                    LocationRole = AtlasIntakeContracts.WebRootSaveRole,
                    Activity = AtlasIntakeContracts.InactiveSaveRootActivity,
                    Decision = AtlasIntakeContracts.ExcludeNoSaveInputsDecision,
                    ObservedEntryCount = 1,
                    IsReparsePoint = false,
                },
            ],
            DiscoveredSaveDirectoryEntryCount = AtlasIntakeContracts.ExactSaveEntryCount,
            IncludedSaveCount = AtlasIntakeContracts.ExactIncludedSaveCount,
            SaveEntries = saveEntries,
            DiscoveredDefinitionEntryCount = AtlasIntakeContracts.ExactDefinitionEntryCount,
            IncludedDefinitionCount = AtlasIntakeContracts.ExactIncludedDefinitionCount,
            DefinitionGroups =
            [
                new AtlasManifestDefinitionGroup
                {
                    GroupId = "data",
                    SelectionRule = "www/data/*.json",
                    DiscoveredCount = AtlasIntakeContracts.ExactIncludedDefinitionCount,
                    Decision = AtlasIntakeContracts.IncludeDefinitionDecision,
                },
                new AtlasManifestDefinitionGroup
                {
                    GroupId = "auxiliary",
                    SelectionRule = "www/notes/*.txt",
                    DiscoveredCount = AtlasIntakeContracts.ExactExcludedDefinitionCount,
                    Decision = AtlasIntakeContracts.ExcludeDefinitionDecision,
                },
            ],
            DefinitionEntries = definitionEntries,
            Validation = new AtlasManifestValidation
            {
                Method = AtlasIntakeContracts.ManualA0ValidationMethod,
                AliasesUnique = true,
                SaveLocatorsUnique = true,
                DefinitionRelativePathsUnique = true,
                SaveRootMembershipReconciled = true,
                SaveRootCountsReconciled = true,
                SaveCountsReconciled = true,
                DefinitionCountsReconciled = true,
                RolesAndDecisionsConsistent = true,
                GroupMembershipReconciled = true,
            },
            Confirmation = new AtlasManifestConfirmation
            {
                Status = AtlasIntakeContracts.ApprovedConfirmationStatus,
                ConfirmedByRole = AtlasIntakeContracts.ProjectLeaderRole,
                DecisionReference = "commit:" + ApprovalCommit,
            },
        };
    }
}
