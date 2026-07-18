using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Hcoona.CelesphoniaModifier.Atlas;
using Xunit;

namespace Hcoona.CelesphoniaModifier.Atlas.Tests;

public sealed class AtlasIntakeContractTests
{
    [Fact]
    public async Task DiscoveryRequestRejectsDuplicatePropertyOnly()
    {
        await using AtlasSyntheticWorkspace workspace = await AtlasSyntheticWorkspace.CreateAsync();
        await AssertDiscoveryRequestRawJsonRejectedAsync(
            workspace,
            json => json[..^1] + ",\"schemaVersion\":\"atlas-intake-discovery-request/v1\"}",
            TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task DiscoveryRequestRejectsUnknownPropertyOnly()
    {
        await using AtlasSyntheticWorkspace workspace = await AtlasSyntheticWorkspace.CreateAsync();
        await AssertRejectedDocumentMutationAsync(
            workspace.Layout.CanonicalDiscoverRequestPath,
            static (path, cancellationToken) =>
                AtlasIntakeContracts.ReadDiscoveryRequestAsync(path, cancellationToken).AsTask(),
            typeof(AtlasRequestException),
            json => json["unknown"] = 1,
            TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task DiscoveryRequestRejectsCommentOnly()
    {
        await using AtlasSyntheticWorkspace workspace = await AtlasSyntheticWorkspace.CreateAsync();
        await AssertDiscoveryRequestRawJsonRejectedAsync(
            workspace,
            json =>
                json.Replace(
                    "\"surveyAlias\":\"survey-000001\",",
                    "\"surveyAlias\":\"survey-000001\",/* comment */",
                    StringComparison.Ordinal),
            TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task DiscoveryRequestRejectsTrailingCommaOnly()
    {
        await using AtlasSyntheticWorkspace workspace = await AtlasSyntheticWorkspace.CreateAsync();
        await AssertDiscoveryRequestRawJsonRejectedAsync(
            workspace,
            json => json[..^1] + ",}",
            TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task DiscoveryRequestRejectsTrailingJsonOnly()
    {
        await using AtlasSyntheticWorkspace workspace = await AtlasSyntheticWorkspace.CreateAsync();
        await AssertDiscoveryRequestRawJsonRejectedAsync(
            workspace,
            json => json + "{}",
            TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task DiscoveryRequestRejectsMissingRequiredPropertyOnly()
    {
        await using AtlasSyntheticWorkspace workspace = await AtlasSyntheticWorkspace.CreateAsync();
        await AssertRejectedDocumentMutationAsync(
            workspace.Layout.CanonicalDiscoverRequestPath,
            static (path, cancellationToken) =>
                AtlasIntakeContracts.ReadDiscoveryRequestAsync(path, cancellationToken).AsTask(),
            typeof(AtlasRequestException),
            json => json.Remove("expectedBuildId"),
            TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task DiscoveryRequestRejectsExplicitNullOnly()
    {
        await using AtlasSyntheticWorkspace workspace = await AtlasSyntheticWorkspace.CreateAsync();
        await AssertRejectedDocumentMutationAsync(
            workspace.Layout.CanonicalDiscoverRequestPath,
            static (path, cancellationToken) =>
                AtlasIntakeContracts.ReadDiscoveryRequestAsync(path, cancellationToken).AsTask(),
            typeof(AtlasRequestException),
            json => json["expectedBuildId"] = null,
            TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task DiscoveryRequestRejectsNullCollectionElementOnly()
    {
        await using AtlasSyntheticWorkspace workspace = await AtlasSyntheticWorkspace.CreateAsync();
        await AssertRejectedDocumentMutationAsync(
            workspace.Layout.CanonicalDiscoverRequestPath,
            static (path, cancellationToken) =>
                AtlasIntakeContracts.ReadDiscoveryRequestAsync(path, cancellationToken).AsTask(),
            typeof(AtlasRequestException),
            json => ((JsonArray)json["saveRoots"]!)[0] = null,
            TestContext.Current.CancellationToken);
    }

    [Fact]
    public void CanonicalSurveyRelativePathMatchesSchemaRules()
    {
        Assert.True(AtlasIntakeContracts.IsCanonicalSurveyRelativePath("x"));
        Assert.False(AtlasIntakeContracts.IsCanonicalSurveyRelativePath("x/"));
        Assert.False(AtlasIntakeContracts.IsCanonicalSurveyRelativePath(@"x\"));
        Assert.False(AtlasIntakeContracts.IsCanonicalSurveyRelativePath("x//y"));
        Assert.False(AtlasIntakeContracts.IsCanonicalSurveyRelativePath("x/./y"));
        Assert.False(AtlasIntakeContracts.IsCanonicalSurveyRelativePath("x/../y"));
        Assert.False(AtlasIntakeContracts.IsCanonicalSurveyRelativePath(":x"));
        Assert.False(AtlasIntakeContracts.IsCanonicalSurveyRelativePath("x:"));
        Assert.False(AtlasIntakeContracts.IsCanonicalSurveyRelativePath("C:foo"));
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
    public async Task OutputSchemasCoverSerializedNestedProperties()
    {
        await using AtlasSyntheticWorkspace workspace = await AtlasSyntheticWorkspace.CreateAsync();
        await PrepareWorkspaceThroughPreflightAsync(workspace);

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

        AssertSchemaCoversSerializedDocument("source-root-map.schema.json", sourceRootMapBytes);
        AssertSchemaCoversSerializedDocument("copy-plan.schema.json", copyPlanBytes);
        AssertSchemaCoversSerializedDocument("intake-state.schema.json", stateBytes);
        AssertSchemaCoversSerializedDocument("copy-receipt.schema.json", receiptBytes);
        AssertSchemaCoversSerializedDocument("cleanup-preflight-report.schema.json", reportBytes);
    }

    [Fact]
    public async Task StrictReadersRejectMissingRequiredPropertiesAcrossContracts()
    {
        await using AtlasSyntheticWorkspace workspace = await AtlasSyntheticWorkspace.CreateAsync();
        await PrepareWorkspaceThroughPreflightAsync(workspace);
        foreach (StrictReaderCase contractCase in GetStrictReaderCases(workspace))
        {
            await AssertReaderRejectsMutationsAsync(
                contractCase,
                JsonMutationKind.RemoveProperty,
                TestContext.Current.CancellationToken);
        }
    }

    [Fact]
    public async Task StrictReadersRejectNullForNonNullablePropertiesAcrossContracts()
    {
        await using AtlasSyntheticWorkspace workspace = await AtlasSyntheticWorkspace.CreateAsync();
        await PrepareWorkspaceThroughPreflightAsync(workspace);
        foreach (StrictReaderCase contractCase in GetStrictReaderCases(workspace))
        {
            await AssertReaderRejectsMutationsAsync(
                contractCase,
                JsonMutationKind.SetNull,
                TestContext.Current.CancellationToken);
        }
    }

    [Fact]
    public async Task StrictReadersRejectNullCollectionElementsAcrossContracts()
    {
        await using AtlasSyntheticWorkspace workspace = await AtlasSyntheticWorkspace.CreateAsync();
        await PrepareWorkspaceThroughPreflightAsync(workspace);
        foreach (DocumentMutationCase mutationCase in GetNullCollectionElementCases(workspace))
        {
            await AssertRejectedMutationCaseAsync(
                mutationCase,
                TestContext.Current.CancellationToken);
        }
    }

    [Fact]
    public async Task StrictReadersRejectExplicitNullOptionalPropertiesAcrossContracts()
    {
        await using AtlasSyntheticWorkspace workspace = await AtlasSyntheticWorkspace.CreateAsync();
        await PrepareWorkspaceThroughPreflightAsync(workspace);
        IReadOnlyList<DocumentMutationCase> mutationCases =
            GetExplicitNullOptionalPropertyCases(workspace);
        foreach (DocumentMutationCase mutationCase in mutationCases)
        {
            await AssertRejectedMutationCaseAsync(
                mutationCase,
                TestContext.Current.CancellationToken);
        }
    }

    [Fact]
    public async Task StrictReadersRejectInvalidDomainValuesAcrossContracts()
    {
        await using AtlasSyntheticWorkspace workspace = await AtlasSyntheticWorkspace.CreateAsync();
        await PrepareWorkspaceThroughPreflightAsync(workspace);

        await AssertRejectedDocumentMutationAsync(
            workspace.Layout.CanonicalApprovedManifestPath,
            static (path, cancellationToken) =>
                AtlasIntakeContracts.ReadManifestAsync(path, cancellationToken).AsTask(),
            typeof(AtlasApprovalException),
            json => json["discoveredSaveDirectoryEntryCount"] = -1,
            TestContext.Current.CancellationToken);
        await AssertRejectedDocumentMutationAsync(
            workspace.Layout.CanonicalSourceRootMapPath,
            static (path, cancellationToken) =>
                AtlasIntakeContracts.ReadSourceRootMapAsync(path, cancellationToken).AsTask(),
            typeof(AtlasSafetyException),
            json =>
                ((JsonObject)((JsonArray)json["saveRoots"]!)[0]!)["absolutePath"] = "relative",
            TestContext.Current.CancellationToken);
        await AssertRejectedDocumentMutationAsync(
            workspace.Layout.CanonicalCopyReceiptPath,
            static (path, cancellationToken) =>
                AtlasIntakeContracts.ReadCopyReceiptAsync(path, cancellationToken).AsTask(),
            typeof(AtlasSafetyException),
            json => json["saveCount"] = -1,
            TestContext.Current.CancellationToken);
        await AssertRejectedDocumentMutationAsync(
            workspace.Layout.CanonicalCopyReceiptPath,
            static (path, cancellationToken) =>
                AtlasIntakeContracts.ReadCopyReceiptAsync(path, cancellationToken).AsTask(),
            typeof(AtlasSafetyException),
            json => ((JsonObject)((JsonArray)json["entries"]!)[0]!)["sourceLength"] = -1,
            TestContext.Current.CancellationToken);
        await AssertRejectedDocumentMutationAsync(
            workspace.Layout.CanonicalCopyReceiptPath,
            static (path, cancellationToken) =>
                AtlasIntakeContracts.ReadCopyReceiptAsync(path, cancellationToken).AsTask(),
            typeof(AtlasSafetyException),
            json =>
                ((JsonObject)((JsonArray)json["entries"]!)[0]!)["sourceLastWriteTimeUtc"]
                    = "0001-01-01T00:00:00+00:00",
            TestContext.Current.CancellationToken);
        await AssertRejectedDocumentMutationAsync(
            workspace.Layout.CanonicalCopyReceiptPath,
            static (path, cancellationToken) =>
                AtlasIntakeContracts.ReadCopyReceiptAsync(path, cancellationToken).AsTask(),
            typeof(AtlasSafetyException),
            json =>
                ((JsonObject)((JsonArray)json["entries"]!)[0]!)["sourceLastWriteTimeUtc"]
                    = "2024-01-01T00:00:00+01:00",
            TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task PipelineUsesExactApprovedCensus()
    {
        await using AtlasSyntheticWorkspace workspace = await AtlasSyntheticWorkspace.CreateAsync();
        await PrepareWorkspaceThroughPreflightAsync(workspace);

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

    private static void AssertSchemaCoversSerializedDocument(
        string schemaName,
        byte[] documentBytes)
    {
        using JsonDocument schema = JsonDocument.Parse(File.ReadAllBytes(
            AtlasSyntheticWorkspace.GetSchemaPath(schemaName)));
        using JsonDocument document = JsonDocument.Parse(documentBytes);
        AssertSchemaNodeMatchesDocument(
            schema.RootElement,
            document.RootElement,
            schema.RootElement,
            "$");
    }

    private static void AssertSchemaNodeMatchesDocument(
        JsonElement schemaNode,
        JsonElement documentNode,
        JsonElement rootSchema,
        string path)
    {
        schemaNode = ResolveSchemaNode(schemaNode, rootSchema);
        if (documentNode.ValueKind == JsonValueKind.Object)
        {
            Dictionary<string, JsonElement> properties = GetSchemaProperties(
                schemaNode,
                documentNode,
                rootSchema);
            foreach (string requiredProperty in GetRequiredSchemaProperties(
                         schemaNode,
                         documentNode,
                         rootSchema))
            {
                Assert.True(
                    documentNode.TryGetProperty(requiredProperty, out _),
                    $"Missing schema-required property '{path}.{requiredProperty}'.");
            }

            foreach (JsonProperty property in documentNode.EnumerateObject())
            {
                Assert.True(
                    properties.TryGetValue(property.Name, out JsonElement propertySchema),
                    $"Unexpected runtime property '{path}.{property.Name}'.");
                AssertSchemaNodeMatchesDocument(
                    propertySchema,
                    property.Value,
                    rootSchema,
                    $"{path}.{property.Name}");
            }

            return;
        }

        if (documentNode.ValueKind == JsonValueKind.Array
            && schemaNode.TryGetProperty("items", out JsonElement itemsSchema)
            && documentNode.GetArrayLength() > 0)
        {
            AssertSchemaNodeMatchesDocument(
                itemsSchema,
                documentNode[0],
                rootSchema,
                $"{path}[0]");
        }
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

    private static Dictionary<string, JsonElement> GetSchemaProperties(
        JsonElement schemaNode,
        JsonElement documentNode,
        JsonElement rootSchema)
    {
        Dictionary<string, JsonElement> properties = new(StringComparer.Ordinal);
        if (schemaNode.TryGetProperty("properties", out JsonElement directProperties))
        {
            foreach (JsonProperty property in directProperties.EnumerateObject())
            {
                properties[property.Name] = property.Value;
            }
        }

        if (schemaNode.TryGetProperty("allOf", out JsonElement allOf))
        {
            foreach (JsonElement clause in allOf.EnumerateArray())
            {
                if (!SchemaClauseApplies(clause, documentNode, rootSchema)
                    || !clause.TryGetProperty("then", out JsonElement thenNode))
                {
                    continue;
                }

                JsonElement resolvedThen = ResolveSchemaNode(thenNode, rootSchema);
                if (!resolvedThen.TryGetProperty("properties", out JsonElement thenProperties))
                {
                    continue;
                }

                foreach (JsonProperty property in thenProperties.EnumerateObject())
                {
                    properties[property.Name] = property.Value;
                }
            }
        }

        return properties;
    }

    private static HashSet<string> GetRequiredSchemaProperties(
        JsonElement schemaNode,
        JsonElement documentNode,
        JsonElement rootSchema)
    {
        HashSet<string> required = new(StringComparer.Ordinal);
        AddRequiredProperties(schemaNode, required);
        if (schemaNode.TryGetProperty("allOf", out JsonElement allOf))
        {
            foreach (JsonElement clause in allOf.EnumerateArray())
            {
                if (!SchemaClauseApplies(clause, documentNode, rootSchema)
                    || !clause.TryGetProperty("then", out JsonElement thenNode))
                {
                    continue;
                }

                AddRequiredProperties(ResolveSchemaNode(thenNode, rootSchema), required);
            }
        }

        return required;
    }

    private static void AddRequiredProperties(JsonElement schemaNode, HashSet<string> required)
    {
        if (!schemaNode.TryGetProperty("required", out JsonElement requiredNode))
        {
            return;
        }

        foreach (JsonElement element in requiredNode.EnumerateArray())
        {
            required.Add(element.GetString() ?? throw new InvalidOperationException());
        }
    }

    private static bool SchemaClauseApplies(
        JsonElement clause,
        JsonElement documentNode,
        JsonElement rootSchema)
    {
        if (!clause.TryGetProperty("if", out JsonElement ifNode)
            || !ifNode.TryGetProperty("properties", out JsonElement propertiesNode))
        {
            return false;
        }

        foreach (JsonProperty property in propertiesNode.EnumerateObject())
        {
            if (!documentNode.TryGetProperty(property.Name, out JsonElement actualValue))
            {
                return false;
            }

            JsonElement expectedNode = ResolveSchemaNode(property.Value, rootSchema);
            if (expectedNode.TryGetProperty("const", out JsonElement constNode))
            {
                if (!JsonElement.DeepEquals(actualValue, constNode))
                {
                    return false;
                }

                continue;
            }

            if (!expectedNode.TryGetProperty("enum", out JsonElement enumNode))
            {
                return false;
            }

            bool matched = enumNode.EnumerateArray()
                .Any(value => JsonElement.DeepEquals(actualValue, value));
            if (!matched)
            {
                return false;
            }
        }

        return true;
    }

    private static JsonElement ResolveSchemaNode(JsonElement schemaNode, JsonElement rootSchema)
    {
        while (schemaNode.TryGetProperty("$ref", out JsonElement referenceNode))
        {
            string reference = referenceNode.GetString()
                ?? throw new InvalidOperationException("Schema reference is missing.");
            string[] segments = reference.TrimStart('#', '/')
                .Split('/', StringSplitOptions.RemoveEmptyEntries);
            JsonElement current = rootSchema;
            foreach (string segment in segments)
            {
                current = current.GetProperty(segment);
            }

            schemaNode = current;
        }

        return schemaNode;
    }

    private static async Task AssertReaderRejectsMutationsAsync(
        StrictReaderCase contractCase,
        JsonMutationKind mutationKind,
        CancellationToken cancellationToken)
    {
        byte[] originalBytes = await File.ReadAllBytesAsync(contractCase.Path, cancellationToken);
        JsonObject document = (JsonNode.Parse(originalBytes) as JsonObject)
            ?? throw new InvalidOperationException("Expected a JSON object.");
        foreach (JsonMutation mutation in EnumerateJsonMutations(document, mutationKind))
        {
            try
            {
                await AssertRejectedDocumentMutationAsync(
                    contractCase.Path,
                    contractCase.ReadAsync,
                    contractCase.ExpectedExceptionType,
                    json => ApplyJsonMutation(json, mutation),
                    cancellationToken);
            }
            catch (Exception exception)
            {
                throw new InvalidOperationException(
                    $"Mutation '{DescribeMutation(mutation)}' failed for '{contractCase.Name}'.",
                    exception);
            }
        }

        await File.WriteAllBytesAsync(contractCase.Path, originalBytes, cancellationToken);
    }

    private static async Task AssertRejectedDocumentMutationAsync(
        string path,
        Func<string, CancellationToken, Task> readAsync,
        Type expectedExceptionType,
        Action<JsonObject> mutate,
        CancellationToken cancellationToken = default)
    {
        byte[] originalBytes = await File.ReadAllBytesAsync(path, cancellationToken);
        JsonObject document = (JsonNode.Parse(originalBytes) as JsonObject)
            ?? throw new InvalidOperationException("Expected a JSON object.");
        mutate(document);
        try
        {
            await AtlasTestSupport.WriteJsonAsync(path, document, cancellationToken);
            Exception exception = await Assert.ThrowsAnyAsync<Exception>(
                () => readAsync(path, cancellationToken));
            Assert.IsType(expectedExceptionType, exception);
        }
        finally
        {
            await File.WriteAllBytesAsync(path, originalBytes, cancellationToken);
        }
    }

    private static void ApplyJsonMutation(JsonObject json, JsonMutation mutation)
    {
        JsonNode parent = NavigateToMutationParent(json, mutation.Path);
        JsonPropertyStep finalStep = mutation.Path[^1];
        JsonObject parentObject = parent as JsonObject
            ?? throw new InvalidOperationException("Expected an object parent.");
        if (mutation.Kind == JsonMutationKind.RemoveProperty)
        {
            parentObject.Remove(finalStep.PropertyName);
            return;
        }

        parentObject[finalStep.PropertyName] = null;
    }

    private static JsonNode NavigateToMutationParent(
        JsonNode root,
        IReadOnlyList<JsonPropertyStep> path)
    {
        JsonNode current = root;
        for (int index = 0; index < path.Count - 1; index++)
        {
            JsonPropertyStep step = path[index];
            JsonNode next = ((JsonObject)current)[step.PropertyName]!;
            if (step.ArrayIndex is null)
            {
                current = next;
                continue;
            }

            current = ((JsonArray)next)[step.ArrayIndex.Value]!;
        }

        return current;
    }

    private static List<JsonMutation> EnumerateJsonMutations(
        JsonObject document,
        JsonMutationKind mutationKind)
    {
        List<JsonMutation> mutations = [];
        HashSet<string> visitedArrayPatterns = new(StringComparer.Ordinal);
        VisitObject(document, [], string.Empty);
        return mutations;

        void VisitObject(
            JsonObject currentObject,
            IReadOnlyList<JsonPropertyStep> currentPath,
            string currentPattern)
        {
            foreach ((string propertyName, JsonNode? value) in currentObject)
            {
                List<JsonPropertyStep> propertyPath =
                [
                    .. currentPath,
                    new JsonPropertyStep(propertyName),
                ];
                mutations.Add(new JsonMutation(propertyPath, mutationKind));
                if (value is JsonObject childObject)
                {
                    string nextPattern = AppendPattern(currentPattern, propertyName);
                    VisitObject(childObject, propertyPath, nextPattern);
                }
                else if (value is JsonArray childArray && childArray.Count > 0)
                {
                    string nextPattern = AppendPattern(currentPattern, propertyName) + "[*]";
                    if (!visitedArrayPatterns.Add(nextPattern))
                    {
                        continue;
                    }

                    if (childArray[0] is JsonObject elementObject)
                    {
                        List<JsonPropertyStep> arrayPath =
                        [
                            .. currentPath,
                            new JsonPropertyStep(propertyName, 0),
                        ];
                        VisitObject(elementObject, arrayPath, nextPattern);
                    }
                }
            }
        }

        static string AppendPattern(string prefix, string propertyName) =>
            string.IsNullOrEmpty(prefix) ? propertyName : $"{prefix}.{propertyName}";
    }

    private static string DescribeMutation(JsonMutation mutation) =>
        string.Join(
            ".",
            mutation.Path.Select(step =>
                step.ArrayIndex is null
                    ? step.PropertyName
                    : $"{step.PropertyName}[{step.ArrayIndex.Value}]"));

    private static IReadOnlyList<StrictReaderCase> GetStrictReaderCases(
        AtlasSyntheticWorkspace workspace) =>
        [
            new(
                "discover-request",
                workspace.Layout.CanonicalDiscoverRequestPath,
                static (path, cancellationToken) =>
                    AtlasIntakeContracts.ReadDiscoveryRequestAsync(
                        path,
                        cancellationToken).AsTask(),
                typeof(AtlasRequestException)),
            new(
                "confirm-request",
                workspace.Layout.CanonicalConfirmRequestPath,
                static (path, cancellationToken) =>
                    AtlasIntakeContracts.ReadConfirmationRequestAsync(path, cancellationToken)
                        .AsTask(),
                typeof(AtlasRequestException)),
            new(
                "copy-request",
                workspace.Layout.CanonicalCopyRequestPath,
                static (path, cancellationToken) =>
                    AtlasIntakeContracts.ReadCopyRequestAsync(path, cancellationToken).AsTask(),
                typeof(AtlasRequestException)),
            new(
                "cleanup-request",
                workspace.Layout.CanonicalCleanupPreflightRequestPath,
                static (path, cancellationToken) =>
                    AtlasIntakeContracts.ReadCleanupPreflightRequestAsync(path, cancellationToken)
                        .AsTask(),
                typeof(AtlasRequestException)),
            new(
                "approved-manifest",
                workspace.Layout.CanonicalApprovedManifestPath,
                static (path, cancellationToken) =>
                    AtlasIntakeContracts.ReadManifestAsync(path, cancellationToken).AsTask(),
                typeof(AtlasApprovalException)),
            new(
                "inventory",
                workspace.Layout.CanonicalInventoryPath,
                static (path, cancellationToken) =>
                    AtlasIntakeContracts.ReadInventoryAsync(path, cancellationToken).AsTask(),
                typeof(AtlasSafetyException)),
            new(
                "source-root-map",
                workspace.Layout.CanonicalSourceRootMapPath,
                static (path, cancellationToken) =>
                    AtlasIntakeContracts.ReadSourceRootMapAsync(path, cancellationToken).AsTask(),
                typeof(AtlasSafetyException)),
            new(
                "copy-plan",
                workspace.Layout.CanonicalCopyPlanPath,
                static (path, cancellationToken) =>
                    AtlasIntakeContracts.ReadCopyPlanAsync(path, cancellationToken).AsTask(),
                typeof(AtlasSafetyException)),
            new(
                "state-r1",
                workspace.Layout.CanonicalDiscoveredStatePath,
                static (path, cancellationToken) =>
                    AtlasIntakeContracts.ReadStateAsync(path, cancellationToken).AsTask(),
                typeof(AtlasApprovalException)),
            new(
                "state-r2",
                workspace.Layout.CanonicalApprovedStatePath,
                static (path, cancellationToken) =>
                    AtlasIntakeContracts.ReadStateAsync(path, cancellationToken).AsTask(),
                typeof(AtlasApprovalException)),
            new(
                "state-r3",
                workspace.Layout.CanonicalQualifiedStatePath,
                static (path, cancellationToken) =>
                    AtlasIntakeContracts.ReadStateAsync(path, cancellationToken).AsTask(),
                typeof(AtlasApprovalException)),
            new(
                "state-r4",
                workspace.Layout.CanonicalPreflightedStatePath,
                static (path, cancellationToken) =>
                    AtlasIntakeContracts.ReadStateAsync(path, cancellationToken).AsTask(),
                typeof(AtlasApprovalException)),
            new(
                "copy-receipt",
                workspace.Layout.CanonicalCopyReceiptPath,
                static (path, cancellationToken) =>
                    AtlasIntakeContracts.ReadCopyReceiptAsync(path, cancellationToken).AsTask(),
                typeof(AtlasSafetyException)),
            new(
                "cleanup-report",
                workspace.Layout.CanonicalCleanupPreflightReportPath,
                static (path, cancellationToken) =>
                    AtlasIntakeContracts.ReadCleanupPreflightReportAsync(path, cancellationToken)
                        .AsTask(),
                typeof(AtlasSafetyException)),
        ];

    private static IReadOnlyList<DocumentMutationCase> GetNullCollectionElementCases(
        AtlasSyntheticWorkspace workspace) =>
        [
            new(
                "discover-request.saveRoots[0]",
                workspace.Layout.CanonicalDiscoverRequestPath,
                static (path, cancellationToken) =>
                    AtlasIntakeContracts.ReadDiscoveryRequestAsync(path, cancellationToken)
                        .AsTask(),
                typeof(AtlasRequestException),
                json => ((JsonArray)json["saveRoots"]!)[0] = null),
            new(
                "approved-manifest.saveRoots[0]",
                workspace.Layout.CanonicalApprovedManifestPath,
                static (path, cancellationToken) =>
                    AtlasIntakeContracts.ReadManifestAsync(path, cancellationToken).AsTask(),
                typeof(AtlasApprovalException),
                json => ((JsonArray)json["saveRoots"]!)[0] = null),
            new(
                "approved-manifest.saveEntries[0]",
                workspace.Layout.CanonicalApprovedManifestPath,
                static (path, cancellationToken) =>
                    AtlasIntakeContracts.ReadManifestAsync(path, cancellationToken).AsTask(),
                typeof(AtlasApprovalException),
                json => ((JsonArray)json["saveEntries"]!)[0] = null),
            new(
                "approved-manifest.definitionGroups[0]",
                workspace.Layout.CanonicalApprovedManifestPath,
                static (path, cancellationToken) =>
                    AtlasIntakeContracts.ReadManifestAsync(path, cancellationToken).AsTask(),
                typeof(AtlasApprovalException),
                json => ((JsonArray)json["definitionGroups"]!)[0] = null),
            new(
                "approved-manifest.definitionEntries[0]",
                workspace.Layout.CanonicalApprovedManifestPath,
                static (path, cancellationToken) =>
                    AtlasIntakeContracts.ReadManifestAsync(path, cancellationToken).AsTask(),
                typeof(AtlasApprovalException),
                json => ((JsonArray)json["definitionEntries"]!)[0] = null),
            new(
                "inventory.artifacts[0]",
                workspace.Layout.CanonicalInventoryPath,
                static (path, cancellationToken) =>
                    AtlasIntakeContracts.ReadInventoryAsync(path, cancellationToken).AsTask(),
                typeof(AtlasSafetyException),
                json => ((JsonArray)json["artifacts"]!)[0] = null),
            new(
                "inventory.artifacts[*].lineageAliases[0]",
                workspace.Layout.CanonicalInventoryPath,
                static (path, cancellationToken) =>
                    AtlasIntakeContracts.ReadInventoryAsync(path, cancellationToken).AsTask(),
                typeof(AtlasSafetyException),
                SetFirstLineageAliasElementToNull),
            new(
                "source-root-map.saveRoots[0]",
                workspace.Layout.CanonicalSourceRootMapPath,
                static (path, cancellationToken) =>
                    AtlasIntakeContracts.ReadSourceRootMapAsync(path, cancellationToken)
                        .AsTask(),
                typeof(AtlasSafetyException),
                json => ((JsonArray)json["saveRoots"]!)[0] = null),
            new(
                "copy-plan.entries[0]",
                workspace.Layout.CanonicalCopyPlanPath,
                static (path, cancellationToken) =>
                    AtlasIntakeContracts.ReadCopyPlanAsync(path, cancellationToken).AsTask(),
                typeof(AtlasSafetyException),
                json => ((JsonArray)json["entries"]!)[0] = null),
            new(
                "state-r4.documentBindings[0]",
                workspace.Layout.CanonicalPreflightedStatePath,
                static (path, cancellationToken) =>
                    AtlasIntakeContracts.ReadStateAsync(path, cancellationToken).AsTask(),
                typeof(AtlasApprovalException),
                json => ((JsonArray)json["documentBindings"]!)[0] = null),
            new(
                "state-r4.artifactBindings[0]",
                workspace.Layout.CanonicalPreflightedStatePath,
                static (path, cancellationToken) =>
                    AtlasIntakeContracts.ReadStateAsync(path, cancellationToken).AsTask(),
                typeof(AtlasApprovalException),
                json => ((JsonArray)json["artifactBindings"]!)[0] = null),
            new(
                "copy-receipt.entries[0]",
                workspace.Layout.CanonicalCopyReceiptPath,
                static (path, cancellationToken) =>
                    AtlasIntakeContracts.ReadCopyReceiptAsync(path, cancellationToken).AsTask(),
                typeof(AtlasSafetyException),
                json => ((JsonArray)json["entries"]!)[0] = null),
            new(
                "cleanup-report.results[0]",
                workspace.Layout.CanonicalCleanupPreflightReportPath,
                static (path, cancellationToken) =>
                    AtlasIntakeContracts.ReadCleanupPreflightReportAsync(path, cancellationToken)
                        .AsTask(),
                typeof(AtlasSafetyException),
                json => ((JsonArray)json["results"]!)[0] = null),
        ];

    private static IReadOnlyList<DocumentMutationCase> GetExplicitNullOptionalPropertyCases(
        AtlasSyntheticWorkspace workspace) =>
        [
            new(
                "pending-manifest.saveRoots[0].reasonCode",
                workspace.Layout.CanonicalPendingManifestPath,
                static (path, cancellationToken) =>
                    AtlasIntakeContracts.ReadManifestAsync(path, cancellationToken).AsTask(),
                typeof(AtlasApprovalException),
                json => ((JsonObject)((JsonArray)json["saveRoots"]!)[0]!)["reasonCode"] = null),
            new(
                "approved-manifest.saveEntries[19].slotNumber",
                workspace.Layout.CanonicalApprovedManifestPath,
                static (path, cancellationToken) =>
                    AtlasIntakeContracts.ReadManifestAsync(path, cancellationToken).AsTask(),
                typeof(AtlasApprovalException),
                json => ((JsonObject)((JsonArray)json["saveEntries"]!)[19]!)["slotNumber"] = null),
            new(
                "approved-manifest.saveEntries[19].reasonCode",
                workspace.Layout.CanonicalApprovedManifestPath,
                static (path, cancellationToken) =>
                    AtlasIntakeContracts.ReadManifestAsync(path, cancellationToken).AsTask(),
                typeof(AtlasApprovalException),
                json => ((JsonObject)((JsonArray)json["saveEntries"]!)[19]!)["reasonCode"] = null),
            new(
                "approved-manifest.definitionGroups[0].reasonCode",
                workspace.Layout.CanonicalApprovedManifestPath,
                static (path, cancellationToken) =>
                    AtlasIntakeContracts.ReadManifestAsync(path, cancellationToken).AsTask(),
                typeof(AtlasApprovalException),
                json =>
                    ((JsonObject)((JsonArray)json["definitionGroups"]!)[0]!)["reasonCode"] = null),
            new(
                "approved-manifest.definitionEntries[0].reasonCode",
                workspace.Layout.CanonicalApprovedManifestPath,
                static (path, cancellationToken) =>
                    AtlasIntakeContracts.ReadManifestAsync(path, cancellationToken).AsTask(),
                typeof(AtlasApprovalException),
                json =>
                    ((JsonObject)((JsonArray)json["definitionEntries"]!)[0]!)["reasonCode"] = null),
            new(
                "pending-manifest.confirmation.confirmedByRole",
                workspace.Layout.CanonicalPendingManifestPath,
                static (path, cancellationToken) =>
                    AtlasIntakeContracts.ReadManifestAsync(path, cancellationToken).AsTask(),
                typeof(AtlasApprovalException),
                json => ((JsonObject)json["confirmation"]!)["confirmedByRole"] = null),
            new(
                "pending-manifest.confirmation.decisionReference",
                workspace.Layout.CanonicalPendingManifestPath,
                static (path, cancellationToken) =>
                    AtlasIntakeContracts.ReadManifestAsync(path, cancellationToken).AsTask(),
                typeof(AtlasApprovalException),
                json => ((JsonObject)json["confirmation"]!)["decisionReference"] = null),
            new(
                "inventory.artifacts[0].qualification",
                workspace.Layout.CanonicalInventoryPath,
                static (path, cancellationToken) =>
                    AtlasIntakeContracts.ReadInventoryAsync(path, cancellationToken).AsTask(),
                typeof(AtlasSafetyException),
                json => ((JsonObject)((JsonArray)json["artifacts"]!)[0]!)["qualification"] = null),
            new(
                "state-r1.decisionCommit",
                workspace.Layout.CanonicalDiscoveredStatePath,
                static (path, cancellationToken) =>
                    AtlasIntakeContracts.ReadStateAsync(path, cancellationToken).AsTask(),
                typeof(AtlasApprovalException),
                json => json["decisionCommit"] = null),
            new(
                "state-r2.finalCopyRootRelativePath",
                workspace.Layout.CanonicalApprovedStatePath,
                static (path, cancellationToken) =>
                    AtlasIntakeContracts.ReadStateAsync(path, cancellationToken).AsTask(),
                typeof(AtlasApprovalException),
                json => json["finalCopyRootRelativePath"] = null),
            new(
                "state-r3.decisionCommit",
                workspace.Layout.CanonicalQualifiedStatePath,
                static (path, cancellationToken) =>
                    AtlasIntakeContracts.ReadStateAsync(path, cancellationToken).AsTask(),
                typeof(AtlasApprovalException),
                json => json["decisionCommit"] = null),
            new(
                "state-r4.finalCopyRootRelativePath",
                workspace.Layout.CanonicalPreflightedStatePath,
                static (path, cancellationToken) =>
                    AtlasIntakeContracts.ReadStateAsync(path, cancellationToken).AsTask(),
                typeof(AtlasApprovalException),
                json => json["finalCopyRootRelativePath"] = null),
        ];

    private static async Task AssertRejectedMutationCaseAsync(
        DocumentMutationCase mutationCase,
        CancellationToken cancellationToken)
    {
        try
        {
            await AssertRejectedDocumentMutationAsync(
                mutationCase.Path,
                mutationCase.ReadAsync,
                mutationCase.ExpectedExceptionType,
                mutationCase.Mutate,
                cancellationToken);
        }
        catch (Exception exception)
        {
            throw new InvalidOperationException(
                $"Mutation case '{mutationCase.Name}' failed.",
                exception);
        }
    }

    private static void SetFirstLineageAliasElementToNull(JsonObject json)
    {
        JsonArray artifacts = (JsonArray)json["artifacts"]!;
        foreach (JsonNode? artifactNode in artifacts)
        {
            JsonObject artifact = artifactNode as JsonObject
                ?? throw new InvalidOperationException("Expected an artifact object.");
            JsonArray lineageAliases = (JsonArray)artifact["lineageAliases"]!;
            if (lineageAliases.Count == 0)
            {
                continue;
            }

            lineageAliases[0] = null;
            return;
        }

        throw new InvalidOperationException("Expected an artifact with lineage aliases.");
    }

    private static async Task AssertDiscoveryRequestRawJsonRejectedAsync(
        AtlasSyntheticWorkspace workspace,
        Func<string, string> mutate,
        CancellationToken cancellationToken)
    {
        string json = Encoding.UTF8.GetString(
            AtlasIntakeContracts.SerializeRequest(workspace.CreateDiscoveryRequest()));
        string mutated = mutate(json);
        Assert.NotEqual(json, mutated);
        await WriteUtf8NoBomAsync(
            workspace.Layout.CanonicalDiscoverRequestPath,
            mutated,
            cancellationToken);
        await Assert.ThrowsAsync<AtlasRequestException>(
            () => AtlasIntakeContracts.ReadDiscoveryRequestAsync(
                workspace.Layout.CanonicalDiscoverRequestPath,
                cancellationToken).AsTask());
    }

    private static Task WriteUtf8NoBomAsync(
        string path,
        string contents,
        CancellationToken cancellationToken) =>
        File.WriteAllTextAsync(path, contents, new UTF8Encoding(false), cancellationToken);

    private static async Task PrepareWorkspaceThroughPreflightAsync(
        AtlasSyntheticWorkspace workspace)
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
        workspace.WriteRequest(workspace.CreatePreflightRequest());
        await PrivateArtifactLifecycle.CleanupPreflightAsync(
            workspace.Layout.CanonicalCleanupPreflightRequestPath,
            TestContext.Current.CancellationToken);
    }

    private sealed record StrictReaderCase(
        string Name,
        string Path,
        Func<string, CancellationToken, Task> ReadAsync,
        Type ExpectedExceptionType);

    private sealed record DocumentMutationCase(
        string Name,
        string Path,
        Func<string, CancellationToken, Task> ReadAsync,
        Type ExpectedExceptionType,
        Action<JsonObject> Mutate);

    private sealed record JsonMutation(
        IReadOnlyList<JsonPropertyStep> Path,
        JsonMutationKind Kind);

    private sealed record JsonPropertyStep(string PropertyName, int? ArrayIndex = null);

    private enum JsonMutationKind
    {
        RemoveProperty,
        SetNull,
    }
}

internal static class AtlasTestSupport
{
    public static AtlasIoSeams CreateIo(
        Func<string, CancellationToken, ValueTask<byte[]>>? readAllBytesAsync = null,
        Func<string, string>? readAllText = null,
        Func<string, bool>? fileExists = null,
        Func<string, bool>? directoryExists = null,
        Func<string, FileAttributes>? getAttributes = null,
        Func<string, AtlasDriveInfo>? getDriveInfo = null,
        Func<string, SearchOption, IEnumerable<string>>? enumerateFileSystemEntries = null,
        Func<string, FileMode, FileAccess, FileShare, FileOptions, Stream>? openFile = null,
        Action<string>? createDirectory = null,
        Action<string, string>? moveFile = null,
        Action<string, string>? moveDirectory = null,
        Action<string, string, string?>? replaceFile = null,
        Action<string, bool>? deleteDirectory = null,
        Action<string, FileAttributes>? setAttributes = null,
        Func<string, long>? getLength = null,
        Func<string, DateTimeOffset>? getLastWriteTimeUtc = null) =>
        new()
        {
            ReadAllBytesAsync = readAllBytesAsync ?? AtlasIoSeams.Default.ReadAllBytesAsync,
            ReadAllText = readAllText ?? AtlasIoSeams.Default.ReadAllText,
            FileExists = fileExists ?? AtlasIoSeams.Default.FileExists,
            DirectoryExists = directoryExists ?? AtlasIoSeams.Default.DirectoryExists,
            GetAttributes = getAttributes ?? AtlasIoSeams.Default.GetAttributes,
            GetDriveInfo = getDriveInfo ?? AtlasIoSeams.Default.GetDriveInfo,
            EnumerateFileSystemEntries =
                enumerateFileSystemEntries ?? AtlasIoSeams.Default.EnumerateFileSystemEntries,
            OpenFile = openFile ?? AtlasIoSeams.Default.OpenFile,
            CreateDirectory = createDirectory ?? AtlasIoSeams.Default.CreateDirectory,
            MoveFile = moveFile ?? AtlasIoSeams.Default.MoveFile,
            MoveDirectory = moveDirectory ?? AtlasIoSeams.Default.MoveDirectory,
            ReplaceFile = replaceFile ?? AtlasIoSeams.Default.ReplaceFile,
            DeleteDirectory = deleteDirectory ?? AtlasIoSeams.Default.DeleteDirectory,
            SetAttributes = setAttributes ?? AtlasIoSeams.Default.SetAttributes,
            GetLength = getLength ?? AtlasIoSeams.Default.GetLength,
            GetLastWriteTimeUtc = getLastWriteTimeUtc ?? AtlasIoSeams.Default.GetLastWriteTimeUtc,
        };

    public static AtlasIoSeams CreateSourceReadCountingIo(
        AtlasSyntheticWorkspace workspace,
        Action onTrackedSourceOpen) =>
        CreateIo(
            openFile: (path, mode, access, share, options) =>
            {
                if (mode == FileMode.Open
                    && access == FileAccess.Read
                    && (AtlasDiscovery.ContainsPath(workspace.SaveRootPath, path)
                        || AtlasDiscovery.ContainsPath(workspace.DefinitionRootPath, path)))
                {
                    onTrackedSourceOpen();
                }

                return AtlasIoSeams.Default.OpenFile(path, mode, access, share, options);
            });

    public static async Task<JsonObject> LoadJsonObjectAsync(
        string path,
        CancellationToken cancellationToken)
    {
        await using FileStream stream = File.OpenRead(path);
        JsonNode? node = await JsonNode.ParseAsync(stream, cancellationToken: cancellationToken);
        return node as JsonObject
            ?? throw new InvalidOperationException("Expected a JSON object.");
    }

    public static Task WriteJsonAsync(
        string path,
        JsonNode node,
        CancellationToken cancellationToken) =>
        File.WriteAllTextAsync(
            path,
            node.ToJsonString(new JsonSerializerOptions { WriteIndented = true }),
            new UTF8Encoding(false),
            cancellationToken);
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
