using System.Text;
using System.Text.Json;
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
    public async Task OutputSchemasCoverSerializedTopLevelProperties()
    {
        await using AtlasSyntheticWorkspace workspace = await AtlasSyntheticWorkspace.CreateAsync();
        byte[] sourceRootMapBytes = AtlasIntakeContracts.SerializeSourceRootMap(
            new AtlasSourceRootMapDocument
            {
                SchemaVersion = AtlasIntakeContracts.SourceRootMapSchemaVersion,
                SurveyAlias = AtlasSyntheticWorkspace.SurveyAlias,
                ManifestRevision = AtlasIntakeContracts.PendingManifestRevision,
                SteamAppId = AtlasIntakeContracts.ExactSteamAppId,
                BuildId = AtlasIntakeContracts.ExactBuildId,
                SaveRoots =
                [
                    new AtlasSourceRootBinding
                    {
                        RootAlias = "save-root-0001",
                        LocationRole = AtlasIntakeContracts.DeploymentRootSaveRole,
                        AbsolutePath = workspace.SaveRootPath,
                    },
                    new AtlasSourceRootBinding
                    {
                        RootAlias = "save-root-0002",
                        LocationRole = AtlasIntakeContracts.WebRootSaveRole,
                        AbsolutePath = workspace.WebSaveRootPath,
                    },
                ],
                DefinitionRootPath = workspace.DefinitionRootPath,
                GameExecutablePath = workspace.GameExecutablePath,
            });
        byte[] copyPlanBytes = AtlasIntakeContracts.SerializeCopyPlan(
            new AtlasCopyPlanDocument
            {
                SchemaVersion = AtlasIntakeContracts.CopyPlanSchemaVersion,
                SurveyAlias = AtlasSyntheticWorkspace.SurveyAlias,
                ManifestRevision = AtlasIntakeContracts.PendingManifestRevision,
                Entries =
                [
                    new AtlasCopyPlanEntry
                    {
                        SourceAlias = "save-source-0001",
                        DestinationArtifactAlias = "private-artifact-000011",
                        ArtifactClass = AtlasIntakeContracts.SaveCopyArtifactClass,
                        DestinationRelativePath = "saves/save-source-0001.rpgsave",
                    },
                ],
            });
        byte[] stateBytes = AtlasIntakeContracts.SerializeState(
            new AtlasIntakeStateDocument
            {
                SchemaVersion = AtlasIntakeContracts.IntakeStateSchemaVersion,
                SurveyAlias = AtlasSyntheticWorkspace.SurveyAlias,
                StateRevision = 1,
                Phase = AtlasIntakeContracts.DiscoveredPhase,
                StateArtifactAlias = "private-artifact-000014",
                SteamAppId = AtlasIntakeContracts.ExactSteamAppId,
                BuildId = AtlasIntakeContracts.ExactBuildId,
                InventorySha256 = new string('a', 64),
                DocumentBindings =
                [
                    new AtlasDocumentBinding
                    {
                        Role = AtlasIntakeContracts.BaselineManifestRole,
                        ArtifactAlias = AtlasSyntheticWorkspace.BaselineManifestArtifactAlias,
                        RelativePath = "intake/corpus-intake-manifest.json",
                        Sha256 = new string('b', 64),
                    },
                ],
                ArtifactBindings =
                [
                    new AtlasArtifactBinding
                    {
                        Role = AtlasIntakeContracts.DiscoveredRequestRole,
                        ArtifactAlias = "private-artifact-000011",
                        RelativePath = "intake/requests/discover.json",
                        Sha256 = new string('c', 64),
                    },
                    new AtlasArtifactBinding
                    {
                        Role = AtlasIntakeContracts.DiscoveredInventoryBackupRole,
                        ArtifactAlias = "private-artifact-000015",
                        RelativePath =
                            "intake/inventory-backups/private-artifact-inventory.discovered.json",
                        Sha256 = new string('d', 64),
                    },
                ],
            });
        byte[] receiptBytes = AtlasIntakeContracts.SerializeCopyReceipt(
            new AtlasCopyReceiptDocument
            {
                SchemaVersion = AtlasIntakeContracts.CopyReceiptSchemaVersion,
                SurveyAlias = AtlasSyntheticWorkspace.SurveyAlias,
                ReceiptArtifactAlias = "private-artifact-000020",
                Profile = AtlasIntakeContracts.TrustedLocalFilesystemProfile,
                CopyRequestSha256 = new string('1', 64),
                ApprovedStateSha256 = new string('2', 64),
                ApprovedManifestSha256 = new string('3', 64),
                SourceRootMapSha256 = new string('4', 64),
                CopyPlanSha256 = new string('5', 64),
                DecisionReference = "commit:" + new string('a', 40),
                ApprovedManifestArtifactAlias = "private-artifact-000016",
                FinalCopyRootRelativePath = AtlasIntakeContracts.SaveSnapshotRelativeRoot,
                SteamAppId = AtlasIntakeContracts.ExactSteamAppId,
                BuildId = AtlasIntakeContracts.ExactBuildId,
                GameExecutableSha256 = new string('6', 64),
                SaveCount = 1,
                DefinitionCount = 0,
                Entries =
                [
                    new AtlasCopyReceiptEntry
                    {
                        DestinationArtifactAlias = "private-artifact-000017",
                        SourceAlias = "save-source-0001",
                        ArtifactClass = AtlasIntakeContracts.SaveCopyArtifactClass,
                        DestinationRelativePath = "saves/save-source-0001.rpgsave",
                        SourceLength = 1,
                        SourceLastWriteTimeUtc = DateTimeOffset.UnixEpoch,
                        SourceSha256 = new string('7', 64),
                    },
                ],
            });
        byte[] reportBytes = AtlasIntakeContracts.SerializeCleanupPreflightReport(
            new AtlasCleanupPreflightReportDocument
            {
                SchemaVersion = AtlasIntakeContracts.CleanupPreflightReportSchemaVersion,
                SurveyAlias = AtlasSyntheticWorkspace.SurveyAlias,
                ReportArtifactAlias = "private-artifact-000030",
                InventorySha256 = new string('8', 64),
                ProposedMilestone = "A8",
                Results =
                [
                    new AtlasCleanupPreflightResult
                    {
                        ArtifactAlias = "private-artifact-000017",
                        ArtifactClass = AtlasIntakeContracts.SaveCopyArtifactClass,
                        Status = AtlasIntakeContracts.PresentArtifactStatus,
                        PlannedDisposition = AtlasIntakeContracts.DeleteDisposition,
                        LastUseMilestone = "A8",
                        ExpiryCondition = "after:A8",
                        Result = "blocked-status",
                    },
                ],
            });

        AssertSchemaHasTopLevelProperties("source-root-map.schema.json", sourceRootMapBytes);
        AssertSchemaHasTopLevelProperties("copy-plan.schema.json", copyPlanBytes);
        AssertSchemaHasTopLevelProperties("intake-state.schema.json", stateBytes);
        AssertSchemaHasTopLevelProperties("copy-receipt.schema.json", receiptBytes);
        AssertSchemaHasTopLevelProperties("cleanup-preflight-report.schema.json", reportBytes);
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
        Directory.CreateDirectory(Path.Combine(DefinitionRootPath, "www", "js"));

        await File.WriteAllTextAsync(
            Layout.PrivateGitIgnorePath,
            "*\n!.gitignore\n",
            new UTF8Encoding(false),
            TestContext.Current.CancellationToken);
        await File.WriteAllBytesAsync(
            Path.Combine(SaveRootPath, "file1.rpgsave"),
            [1, 2, 3],
            TestContext.Current.CancellationToken);
        await File.WriteAllBytesAsync(
            Path.Combine(SaveRootPath, "global.rpgsave"),
            [4, 5],
            TestContext.Current.CancellationToken);
        await File.WriteAllBytesAsync(
            Path.Combine(SaveRootPath, "config.rpgsave"),
            [6],
            TestContext.Current.CancellationToken);
        await File.WriteAllTextAsync(
            Path.Combine(SaveRootPath, "steam_autocloud.vdf"),
            "steam",
            Encoding.UTF8,
            TestContext.Current.CancellationToken);
        await File.WriteAllTextAsync(
            Path.Combine(SaveRootPath, "notes.txt"),
            "notes",
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
        await File.WriteAllTextAsync(
            Path.Combine(DefinitionRootPath, "www", "data", "System.json"),
            "{}",
            Encoding.UTF8,
            TestContext.Current.CancellationToken);
        await File.WriteAllTextAsync(
            Path.Combine(DefinitionRootPath, "www", "js", "plugins.js"),
            "[]",
            Encoding.UTF8,
            TestContext.Current.CancellationToken);
        await File.WriteAllTextAsync(
            Path.Combine(DefinitionRootPath, "www", "notes.txt"),
            "notes",
            Encoding.UTF8,
            TestContext.Current.CancellationToken);

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

    private static AtlasCorpusIntakeManifest CreateBaselineManifest() => new()
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
                Activity = "active",
                Decision = AtlasIntakeContracts.IncludeSaveRootDecision,
                ObservedEntryCount = 5,
                IsReparsePoint = false,
            },
            new AtlasManifestSaveRoot
            {
                RootAlias = "save-root-0002",
                LocationRole = AtlasIntakeContracts.WebRootSaveRole,
                Activity = "inactive",
                Decision = AtlasIntakeContracts.ExcludeNoSaveInputsDecision,
                ObservedEntryCount = 1,
                IsReparsePoint = false,
            },
        ],
        DiscoveredSaveDirectoryEntryCount = 6,
        IncludedSaveCount = 3,
        SaveEntries =
        [
            new AtlasManifestSaveEntry
            {
                SourceAlias = "save-source-0001",
                RootAlias = "save-root-0001",
                RelativePath = "file1.rpgsave",
                Role = AtlasIntakeContracts.SlotSaveRole,
                SlotNumber = 1,
                Decision = AtlasIntakeContracts.IncludeSaveDecision,
                EntryType = AtlasIntakeContracts.FileEntryType,
                IsReparsePoint = false,
            },
            new AtlasManifestSaveEntry
            {
                SourceAlias = "save-source-0002",
                RootAlias = "save-root-0001",
                RelativePath = "global.rpgsave",
                Role = AtlasIntakeContracts.GlobalSaveRole,
                Decision = AtlasIntakeContracts.IncludeSaveDecision,
                EntryType = AtlasIntakeContracts.FileEntryType,
                IsReparsePoint = false,
            },
            new AtlasManifestSaveEntry
            {
                SourceAlias = "save-source-0003",
                RootAlias = "save-root-0001",
                RelativePath = "config.rpgsave",
                Role = AtlasIntakeContracts.ConfigSaveRole,
                Decision = AtlasIntakeContracts.IncludeSaveDecision,
                EntryType = AtlasIntakeContracts.FileEntryType,
                IsReparsePoint = false,
            },
            new AtlasManifestSaveEntry
            {
                SourceAlias = "save-source-0004",
                RootAlias = "save-root-0001",
                RelativePath = "notes.txt",
                Role = AtlasIntakeContracts.OtherSaveRole,
                Decision = AtlasIntakeContracts.ExcludeNonSaveDecision,
                EntryType = AtlasIntakeContracts.FileEntryType,
                IsReparsePoint = false,
            },
            new AtlasManifestSaveEntry
            {
                SourceAlias = "save-source-0005",
                RootAlias = "save-root-0001",
                RelativePath = "steam_autocloud.vdf",
                Role = AtlasIntakeContracts.SteamAutoCloudSaveRole,
                Decision = AtlasIntakeContracts.ExcludeSteamAutoCloudDecision,
                EntryType = AtlasIntakeContracts.FileEntryType,
                IsReparsePoint = false,
            },
            new AtlasManifestSaveEntry
            {
                SourceAlias = "save-source-0006",
                RootAlias = "save-root-0002",
                RelativePath = "steam_autocloud.vdf",
                Role = AtlasIntakeContracts.SteamAutoCloudSaveRole,
                Decision = AtlasIntakeContracts.ExcludeSteamAutoCloudDecision,
                EntryType = AtlasIntakeContracts.FileEntryType,
                IsReparsePoint = false,
            },
        ],
        DiscoveredDefinitionEntryCount = 3,
        IncludedDefinitionCount = 2,
        DefinitionGroups =
        [
            new AtlasManifestDefinitionGroup
            {
                GroupId = "data",
                SelectionRule = "www/data/*.json",
                DiscoveredCount = 1,
                Decision = "include",
            },
            new AtlasManifestDefinitionGroup
            {
                GroupId = "scripts",
                SelectionRule = "www/js/*.js",
                DiscoveredCount = 1,
                Decision = "include",
            },
            new AtlasManifestDefinitionGroup
            {
                GroupId = "auxiliary",
                SelectionRule = "www/*.txt",
                DiscoveredCount = 1,
                Decision = "exclude",
            },
        ],
        DefinitionEntries =
        [
            new AtlasManifestDefinitionEntry
            {
                SourceAlias = "definition-source-000001",
                RelativePath = "www/data/System.json",
                GroupId = "data",
                Decision = "include",
                EntryType = AtlasIntakeContracts.FileEntryType,
                IsReparsePoint = false,
            },
            new AtlasManifestDefinitionEntry
            {
                SourceAlias = "definition-source-000002",
                RelativePath = "www/js/plugins.js",
                GroupId = "scripts",
                Decision = "include",
                EntryType = AtlasIntakeContracts.FileEntryType,
                IsReparsePoint = false,
            },
            new AtlasManifestDefinitionEntry
            {
                SourceAlias = "definition-source-000003",
                RelativePath = "www/notes.txt",
                GroupId = "auxiliary",
                Decision = "exclude",
                EntryType = AtlasIntakeContracts.FileEntryType,
                IsReparsePoint = false,
            },
        ],
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
