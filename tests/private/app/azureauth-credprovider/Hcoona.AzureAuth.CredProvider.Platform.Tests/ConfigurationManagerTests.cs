using System.Reflection;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Hcoona.AzureAuth.CredProvider.Contracts;
using Hcoona.AzureAuth.CredProvider.Platform.Configuration;
using Hcoona.AzureAuth.CredProvider.Platform.FileSystem;
using Xunit;

namespace Hcoona.AzureAuth.CredProvider.Platform.Tests;

public sealed class ConfigurationManagerTests
{
    [Fact]
    public void ValidatePlanBindsToFrozenConfigurationChangePlanContract()
    {
        var manager = new ConfigurationManager();
        ConfigurationChangePlan plan = CreateValidPlan();

        ConfigurationPlanValidationResult result = manager.ValidatePlan(plan);

        Assert.True(result.IsValid);
        Assert.Null(result.Violation);
        Assert.Same(plan, result.Plan);
        Assert.Equal(ContractVersions.ConfigurationChangePlanMajor, result.Plan.ContractMajor);
        Assert.True(ConfigurationChangePlanPolicy.IsValid(result.Plan));
    }

    [Fact]
    public void ValidatePlanReportsFrozenContractViolationsWithoutWritingConfiguration()
    {
        var manager = new ConfigurationManager();
        ConfigurationChangePlan invalidPlan = CreateValidPlan() with
        {
            Scope = ConfigurationScope.WorkspaceReadOnly,
        };

        ConfigurationPlanValidationResult result = manager.ValidatePlan(invalidPlan);

        Assert.False(result.IsValid);
        Assert.NotNull(result.Violation);
        Assert.Contains(
            "workspace read-only",
            result.Violation,
            StringComparison.OrdinalIgnoreCase
        );
    }

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public void ValidatePlanRejectsUnsupportedConfigurationChangePlanContractMajor(
        int contractMajor
    )
    {
        var manager = new ConfigurationManager();
        ConfigurationChangePlan invalidPlan = CreateValidPlan() with
        {
            ContractMajor = contractMajor,
        };

        ConfigurationPlanValidationResult result = manager.ValidatePlan(invalidPlan);

        Assert.NotEqual(ContractVersions.ConfigurationChangePlanMajor, contractMajor);
        Assert.False(result.IsValid);
        Assert.Same(invalidPlan, result.Plan);
        Assert.NotNull(result.Violation);
        Assert.Contains("contract major", result.Violation, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task AcceptPlanAsyncIsAValidationOnlyAdapterBoundary()
    {
        var manager = new ConfigurationManager();
        ConfigurationChangePlan plan = CreateValidPlan();

        ConfigurationPlanValidationResult result = await manager.AcceptPlanAsync(
            plan,
            TestContext.Current.CancellationToken
        );

        Assert.True(result.IsValid);
        Assert.Same(plan, result.Plan);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public async Task AcceptPlanAsyncRejectsUnsupportedConfigurationChangePlanContractMajor(
        int contractMajor
    )
    {
        var manager = new ConfigurationManager();
        ConfigurationChangePlan invalidPlan = CreateValidPlan() with
        {
            ContractMajor = contractMajor,
        };

        ConfigurationPlanValidationResult result = await manager.AcceptPlanAsync(
            invalidPlan,
            TestContext.Current.CancellationToken
        );

        Assert.NotEqual(ContractVersions.ConfigurationChangePlanMajor, contractMajor);
        Assert.False(result.IsValid);
        Assert.Same(invalidPlan, result.Plan);
        Assert.NotNull(result.Violation);
        Assert.Contains("contract major", result.Violation, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData(nameof(IConfigurationManager.DryRunAsync))]
    [InlineData(nameof(IConfigurationManager.ApplyAsync))]
    [InlineData(nameof(IConfigurationManager.RemoveAsync))]
    public async Task ExecutionMethodsValidatePlansBeforeDeferredPhase4Engines(string methodName)
    {
        var manager = new ConfigurationManager();
        ConfigurationChangePlan invalidPlan = CreateValidPlan() with
        {
            Scope = ConfigurationScope.WorkspaceReadOnly,
        };
        Func<ValueTask<ConfigurationPlanResult>> call = CreateExecutionCall(
            manager,
            methodName,
            invalidPlan
        );

        var exception = await Assert.ThrowsAsync<ArgumentException>(async () => await call());

        Assert.Contains(
            "workspace read-only",
            exception.Message,
            StringComparison.OrdinalIgnoreCase
        );
    }

    [Fact]
    public async Task DryRunProjectsPlannedOperationsAndOwnershipManifest()
    {
        var manager = new ConfigurationManager();
        ConfigurationChangePlan plan = CreateValidPlan();
        string expectedHash =
            "b5bea41b6c623f7c09f1bf24dcae58ebab3c0cdd90ad966bc43a45b44867e12b";

        ConfigurationPlanResult result = await manager.DryRunAsync(
            plan,
            TestContext.Current.CancellationToken
        );

        Assert.IsNotType<ConfigurationChangePlan>((object)result.Plan);
        Assert.Equal(plan.PlanId, result.Plan.PlanId);
        Assert.Equal(plan.ChangeSetId, result.Plan.ChangeSetId);
        Assert.Equal(plan.OwnerProductId, result.Plan.OwnerProductId);
        Assert.Equal(plan.Scope, result.Plan.Scope);
        Assert.Equal(ConfigurationPlanOperation.DryRun, result.Operation);
        Assert.Equal(ConfigurationPlanState.Planned, result.State);
        ConfigurationPlannedOperation operation = Assert.Single(result.PlannedOperations);
        ConfigurationPlannedChange resultChange = Assert.Single(result.Changes);
        ConfigurationPlannedChange resultPlanChange = Assert.Single(result.Plan.Changes);
        Assert.Equal(resultPlanChange, operation.Change);
        Assert.Equal(resultPlanChange, resultChange);
        Assert.Equal(1, operation.Change.Sequence);
        Assert.Equal(plan.Changes[0].Operation, operation.Change.Operation);
        Assert.Equal(plan.Changes[0].TargetKind, operation.Change.TargetKind);
        Assert.Equal(plan.Changes[0].TargetPathOrName, operation.Change.TargetPathOrName);
        Assert.Equal(plan.Changes[0].Key, operation.Change.Key);
        Assert.True(operation.Change.HasPlannedValue);
        Assert.False(operation.Change.IsSecretValue);
        Assert.Equal(expectedHash, operation.Change.PlannedValueSha256);
        ConfigurationOwnershipManifestEntry entry =
            Assert.IsType<ConfigurationOwnershipManifestEntry>(
                operation.OwnershipEntry
            );
        Assert.Equal(1, entry.Sequence);
        Assert.Equal(ConfigurationChangeOperation.Set, entry.Operation);
        Assert.Equal(ConfigurationTargetKind.GitConfig, entry.TargetKind);
        Assert.Equal("global git config", entry.TargetPathOrName);
        Assert.Equal("credential.https://dev.azure.com.useHttpPath", entry.Key);
        Assert.True(entry.HasPlannedValue);
        Assert.False(entry.IsSecretValue);
        Assert.Equal(expectedHash, entry.PlannedValueSha256);

        ConfigurationOwnershipManifest manifest = Assert.IsType<ConfigurationOwnershipManifest>(
            result.OwnershipManifest
        );
        Assert.Equal(ConfigurationOwnershipManifest.CurrentSchemaVersion, manifest.SchemaVersion);
        Assert.Equal(plan.Manifest.ManifestId, manifest.ManifestId);
        Assert.Equal(plan.PlanId, manifest.PlanId);
        Assert.Equal(plan.ChangeSetId, manifest.ChangeSetId);
        Assert.Equal(plan.OwnerProductId, manifest.OwnerProductId);
        Assert.Equal(plan.Scope, manifest.Scope);
        Assert.Equal(plan.Manifest.EntrySelector, manifest.EntrySelector);
        Assert.Equal(plan.Manifest.ProductVersion, manifest.ProductVersion);
        Assert.Same(plan.Manifest.SafeMetadata, manifest.SafeMetadata);
        ConfigurationOwnershipManifestEntry manifestEntry = Assert.Single(manifest.Entries);
        Assert.Equal(entry, manifestEntry);
    }

    [Fact]
    public async Task ConfigurationPlanResultSerializesOperationAsStrictCamelCaseString()
    {
        var manager = new ConfigurationManager();
        ConfigurationChangePlan plan = CreateValidPlan();

        ConfigurationPlanResult result = await manager.DryRunAsync(
            plan,
            TestContext.Current.CancellationToken
        );
        string json = JsonSerializer.Serialize(result, CreateTestSerializerOptions());
        string numericOperationJson = json.Replace(
            "\"operation\":\"dryRun\"",
            "\"operation\":0",
            StringComparison.Ordinal
        );

        Assert.Contains("\"operation\":\"dryRun\"", json, StringComparison.Ordinal);
        Assert.DoesNotContain("\"operation\":0", json, StringComparison.Ordinal);
        ConfigurationPlanResult roundTrip =
            JsonSerializer.Deserialize<ConfigurationPlanResult>(
                json,
                CreateTestSerializerOptions()
            )!;
        Assert.Equal(ConfigurationPlanOperation.DryRun, roundTrip.Operation);
        Assert.Throws<JsonException>(() =>
            JsonSerializer.Deserialize<ConfigurationPlanResult>(
                numericOperationJson,
                CreateTestSerializerOptions()
            )
        );
    }

    [Fact]
    public async Task DryRunManifestOmitsSecretValuesAndSecretValueHashes()
    {
        var manager = new ConfigurationManager();
        const string secret = "azdops_pat_secret_for_manifest_tests";
        ConfigurationChangePlan plan = ConfigurationChangePlanPolicy.Create(
            "plan-secret",
            "changeset-secret",
            "azureauth-credprovider",
            ConfigurationScope.User,
            CreateManifest(),
            [
                new ConfigurationChange
                {
                    Operation = ConfigurationChangeOperation.Set,
                    TargetKind = ConfigurationTargetKind.Npmrc,
                    TargetPathOrName = "user npmrc",
                    Key = "//pkgs.dev.azure.com/org/_packaging/feed/npm/registry/:_authToken",
                    Value = secret,
                    RequiresOwnershipRecord = true,
                    IsSecretValue = true,
                },
            ]
        );

        ConfigurationPlanResult result = await manager.DryRunAsync(
            plan,
            TestContext.Current.CancellationToken
        );

        ConfigurationPlannedChange resultPlanChange = Assert.Single(result.Plan.Changes);
        ConfigurationPlannedChange resultChange = Assert.Single(result.Changes);
        ConfigurationPlannedOperation plannedOperation = Assert.Single(result.PlannedOperations);
        Assert.DoesNotContain(
            typeof(ConfigurationPlannedChange).GetProperties(),
            property => property.Name == nameof(ConfigurationChange.Value)
        );
        Assert.IsNotType<ConfigurationChangePlan>((object)result.Plan);
        Assert.True(resultPlanChange.HasPlannedValue);
        Assert.True(resultChange.HasPlannedValue);
        Assert.True(plannedOperation.Change.HasPlannedValue);
        Assert.Null(resultPlanChange.PlannedValueSha256);
        Assert.Null(resultChange.PlannedValueSha256);
        Assert.Null(plannedOperation.Change.PlannedValueSha256);
        Assert.True(resultPlanChange.IsSecretValue);
        Assert.True(resultChange.IsSecretValue);
        Assert.True(plannedOperation.Change.IsSecretValue);
        Assert.Equal(plan.Changes[0].Operation, resultPlanChange.Operation);
        Assert.Equal(plan.Changes[0].TargetKind, resultPlanChange.TargetKind);
        Assert.Equal(plan.Changes[0].TargetPathOrName, resultPlanChange.TargetPathOrName);
        Assert.Equal(plan.Changes[0].Key, resultPlanChange.Key);
        Assert.Equal(secret, plan.Changes[0].Value);
        AssertObjectGraphDoesNotContainSecret(result, secret);

        ConfigurationOwnershipManifest manifest = Assert.IsType<ConfigurationOwnershipManifest>(
            result.OwnershipManifest
        );
        Assert.True(ConfigurationOwnershipManifestPolicy.IsValid(manifest));
        ConfigurationOwnershipManifestEntry entry = Assert.Single(manifest.Entries);
        Assert.True(entry.IsSecretValue);
        Assert.True(entry.HasPlannedValue);
        Assert.Null(entry.PlannedValueSha256);
        string json = ConfigurationOwnershipManifestSerializer.Serialize(manifest);
        Assert.DoesNotContain(secret, json, StringComparison.Ordinal);
        Assert.DoesNotContain("azdops_pat", json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(secret, manifest.ToString(), StringComparison.Ordinal);
        Assert.DoesNotContain(secret, result.ToString(), StringComparison.Ordinal);
        Assert.DoesNotContain(
            secret,
            JsonSerializer.Serialize(
                result,
                ConfigurationManagerTestsJson.Default.ConfigurationPlanResult
            ),
            StringComparison.Ordinal
        );
    }

    [Fact]
    public async Task OwnershipManifestSerializesAndRoundTripsWithoutSecrets()
    {
        const string secret = "secret-value-that-must-not-be-serialized";
        ConfigurationChangePlan plan = ConfigurationChangePlanPolicy.Create(
            "plan-secret-serialization",
            "changeset-secret-serialization",
            "azureauth-credprovider",
            ConfigurationScope.User,
            CreateManifest(),
            [
                new ConfigurationChange
                {
                    Operation = ConfigurationChangeOperation.Set,
                    TargetKind = ConfigurationTargetKind.GitConfig,
                    TargetPathOrName = "global git config",
                    Key = "credential.helper",
                    Value = secret,
                    RequiresOwnershipRecord = true,
                    IsSecretValue = true,
                    PreviousOwnedEntryMetadata = "previous-metadata",
                },
            ]
        );
        var manager = new ConfigurationManager();
        ConfigurationPlanResult result = await manager.DryRunAsync(
            plan,
            TestContext.Current.CancellationToken
        );
        ConfigurationOwnershipManifest manifest = result.OwnershipManifest!;

        string json = ConfigurationOwnershipManifestSerializer.Serialize(manifest);
        ConfigurationOwnershipManifest roundTrip =
            ConfigurationOwnershipManifestSerializer.Deserialize(json);

        Assert.DoesNotContain(secret, json, StringComparison.Ordinal);
        Assert.Equal(
            ConfigurationOwnershipManifestSerializer.Serialize(manifest),
            ConfigurationOwnershipManifestSerializer.Serialize(roundTrip)
        );
        Assert.Contains("\"schemaVersion\":1", json, StringComparison.Ordinal);
        Assert.Contains("\"operation\":\"set\"", json, StringComparison.Ordinal);
        Assert.DoesNotContain("plannedValueSha256", json, StringComparison.Ordinal);
    }

    [Fact]
    public void OwnershipManifestStorePersistsSerializedManifestOnly()
    {
        var fileSystem = new InMemoryManifestFileSystem();
        var store = new ConfigurationOwnershipManifestStore(fileSystem);
        ConfigurationOwnershipManifest manifest = new()
        {
            ManifestId = "manifest-store",
            PlanId = "plan-store",
            ChangeSetId = "changeset-store",
            OwnerProductId = "azureauth-credprovider",
            Scope = ConfigurationScope.User,
            EntrySelector = "git.store",
            ContainsCredentialMaterial = true,
            Entries =
            [
                new ConfigurationOwnershipManifestEntry
                {
                    Sequence = 1,
                    Operation = ConfigurationChangeOperation.Set,
                    TargetKind = ConfigurationTargetKind.GitConfig,
                    TargetPathOrName = "global git config",
                    Key = "credential.helper",
                    PreserveDeclarationsAndComments = true,
                    HasPlannedValue = true,
                    IsSecretValue = true,
                },
            ],
        };

        store.Save("manifest.json", manifest);
        ConfigurationOwnershipManifest loaded = store.Load("manifest.json");

        Assert.Equal(
            ConfigurationOwnershipManifestSerializer.Serialize(manifest),
            ConfigurationOwnershipManifestSerializer.Serialize(loaded)
        );
        Assert.DoesNotContain("supersecret", fileSystem.StoredText, StringComparison.Ordinal);
    }

    [Fact]
    public void OwnershipManifestSerializerAcceptsValidManifest()
    {
        ConfigurationOwnershipManifest manifest = CreateValidOwnershipManifest();

        string json = ConfigurationOwnershipManifestSerializer.Serialize(manifest);
        ConfigurationOwnershipManifest roundTrip =
            ConfigurationOwnershipManifestSerializer.Deserialize(json);

        Assert.Equal(
            ConfigurationOwnershipManifestSerializer.Serialize(manifest),
            ConfigurationOwnershipManifestSerializer.Serialize(roundTrip)
        );
        Assert.Contains("\"scope\":\"user\"", json, StringComparison.Ordinal);
        Assert.Contains("\"operation\":\"set\"", json, StringComparison.Ordinal);
        Assert.Contains("\"targetKind\":\"gitConfig\"", json, StringComparison.Ordinal);
        Assert.True(ConfigurationOwnershipManifestPolicy.IsValid(roundTrip));
    }

    [Fact]
    public void OwnershipManifestSerializerRejectsMissingSchemaVersion()
    {
        string json = RawOwnershipManifestJson(CreateValidOwnershipManifest());
        Assert.Contains("\"schemaVersion\":1,", json, StringComparison.Ordinal);
        json = json.Replace("\"schemaVersion\":1,", "", StringComparison.Ordinal);

        Assert.Throws<JsonException>(() =>
            ConfigurationOwnershipManifestSerializer.Deserialize(json)
        );
    }

    [Fact]
    public void OwnershipManifestSerializerRejectsMissingEntries()
    {
        const string json = """
            {
              "schemaVersion": 1,
              "manifestId": "manifest-missing-entries",
              "planId": "plan-missing-entries",
              "changeSetId": "changeset-missing-entries",
              "ownerProductId": "azureauth-credprovider",
              "scope": "user",
              "entrySelector": "git.credential.helper"
            }
            """;

        Assert.Throws<JsonException>(() =>
            ConfigurationOwnershipManifestSerializer.Deserialize(json)
        );
    }

    [Theory]
    [InlineData("scope", "\"scope\":\"user\"", "\"scope\":1")]
    [InlineData("operation", "\"operation\":\"set\"", "\"operation\":1")]
    [InlineData("target kind", "\"targetKind\":\"gitConfig\"", "\"targetKind\":1")]
    public void OwnershipManifestSerializerRejectsNumericEnumValues(
        string caseName,
        string expectedStringValue,
        string numericValue
    )
    {
        Assert.False(string.IsNullOrWhiteSpace(caseName));
        string json = RawOwnershipManifestJson(CreateValidOwnershipManifest())
            .Replace(expectedStringValue, numericValue, StringComparison.Ordinal);

        Assert.Throws<JsonException>(() =>
            ConfigurationOwnershipManifestSerializer.Deserialize(json)
        );
    }

    [Theory]
    [InlineData(ConfigurationChangeOperation.Set)]
    [InlineData(ConfigurationChangeOperation.Create)]
    [InlineData(ConfigurationChangeOperation.Update)]
    [InlineData(ConfigurationChangeOperation.Refresh)]
    [InlineData(ConfigurationChangeOperation.Remove)]
    [InlineData(ConfigurationChangeOperation.RemoveAdapter)]
    [InlineData(ConfigurationChangeOperation.EnsureFile)]
    [InlineData(ConfigurationChangeOperation.InstallAdapter)]
    public void OwnershipManifestPolicyRejectsYarnNpmAuthIdentOwnershipEntry(
        ConfigurationChangeOperation operation
    )
    {
        ConfigurationOwnershipManifest manifest = CreateValidOwnershipManifest() with
        {
            ContainsCredentialMaterial = true,
            Entries =
            [
                CreateValidOwnershipEntry() with
                {
                    Operation = operation,
                    TargetKind = ConfigurationTargetKind.Yarnrc,
                    TargetPathOrName = "user yarnrc",
                    Key = "npmScopes.contoso.npmAuthIdent",
                    IsSecretValue = true,
                    HasPlannedValue = RequiresValue(operation),
                    PlannedValueSha256 = null,
                    PreviousOwnedEntryMetadata = operation
                        is ConfigurationChangeOperation.Update
                            or ConfigurationChangeOperation.Refresh
                            or ConfigurationChangeOperation.Remove
                            or ConfigurationChangeOperation.RemoveAdapter
                        ? "previous-metadata"
                        : null,
                },
            ],
        };
        string json = RawOwnershipManifestJson(manifest);

        Assert.False(ConfigurationOwnershipManifestPolicy.IsValid(manifest));
        Assert.Throws<ArgumentException>(() =>
            ConfigurationOwnershipManifestSerializer.Deserialize(json)
        );
    }

    [Theory]
    [InlineData(ConfigurationChangeOperation.Remove)]
    [InlineData(ConfigurationChangeOperation.RemoveAdapter)]
    public async Task YarnNpmAuthIdentRemovalPlansDoNotReturnInvalidDryRunManifests(
        ConfigurationChangeOperation operation
    )
    {
        var manager = new ConfigurationManager();
        ConfigurationChangePlan plan = CreateYarnNpmAuthIdentRemovalPlan(operation);
        ConfigurationOwnershipManifest rejectedManifest =
            CreateYarnNpmAuthIdentRemovalManifest(operation);

        Assert.False(ConfigurationChangePlanPolicy.IsValid(plan));
        Assert.False(ConfigurationOwnershipManifestPolicy.IsValid(rejectedManifest));
        Assert.Throws<ArgumentException>(() =>
            ConfigurationOwnershipManifestSerializer.Serialize(rejectedManifest)
        );

        ConfigurationPlanValidationResult validation = manager.ValidatePlan(plan);
        Assert.False(validation.IsValid);
        Assert.Contains("Yarn npmAuthIdent", validation.Violation, StringComparison.Ordinal);

        var exception = await Assert.ThrowsAsync<ArgumentException>(async () =>
            await manager.DryRunAsync(plan, TestContext.Current.CancellationToken)
        );
        Assert.Contains("Yarn npmAuthIdent", exception.Message, StringComparison.Ordinal);
    }

    [Theory]
    [MemberData(nameof(MalformedOwnershipManifestJson))]
    public void OwnershipManifestSerializerRejectsMalformedManifests(string json)
    {
        var exception = Assert.ThrowsAny<Exception>(() =>
            ConfigurationOwnershipManifestSerializer.Deserialize(json)
        );

        Assert.True(exception is ArgumentException or JsonException);
    }

    [Theory]
    [InlineData("top-level value field", "\"value\":\"secret-looking-field\",")]
    [InlineData("top-level secret field", "\"secret\":\"secret-looking-field\",")]
    public void OwnershipManifestSerializerRejectsUnknownTopLevelFields(
        string caseName,
        string unknownFieldJson
    )
    {
        Assert.False(string.IsNullOrWhiteSpace(caseName));
        string json = RawOwnershipManifestJson(CreateValidOwnershipManifest())
            .Replace("{", "{" + unknownFieldJson, StringComparison.Ordinal);

        Assert.Throws<JsonException>(() =>
            ConfigurationOwnershipManifestSerializer.Deserialize(json)
        );
    }

    [Theory]
    [InlineData("entry value field", "\"value\":\"secret-looking-field\",")]
    [InlineData("entry secret field", "\"secret\":\"secret-looking-field\",")]
    public void OwnershipManifestSerializerRejectsUnknownEntryFields(
        string caseName,
        string unknownFieldJson
    )
    {
        Assert.False(string.IsNullOrWhiteSpace(caseName));
        string json = RawOwnershipManifestJson(CreateValidOwnershipManifest())
            .Replace(
                "\"sequence\":1",
                unknownFieldJson + "\"sequence\":1",
                StringComparison.Ordinal
            );

        Assert.Throws<JsonException>(() =>
            ConfigurationOwnershipManifestSerializer.Deserialize(json)
        );
    }

    [Theory]
    [MemberData(nameof(MalformedOwnershipManifestCases))]
    public void OwnershipManifestPolicyRejectsMalformedManifestInvariants(
        string caseName,
        ConfigurationOwnershipManifest manifest
    )
    {
        string json = RawOwnershipManifestJson(manifest);

        Assert.False(ConfigurationOwnershipManifestPolicy.IsValid(manifest), caseName);
        var exception = Assert.ThrowsAny<Exception>(() =>
            ConfigurationOwnershipManifestSerializer.Deserialize(json)
        );
        Assert.True(exception is ArgumentException or JsonException, caseName);
    }

    [Theory]
    [InlineData(ConfigurationChangeOperation.Set)]
    [InlineData(ConfigurationChangeOperation.Create)]
    [InlineData(ConfigurationChangeOperation.Update)]
    [InlineData(ConfigurationChangeOperation.Refresh)]
    public void OwnershipManifestPolicyRejectsValueWritingEntriesWithoutPlannedValues(
        ConfigurationChangeOperation operation
    )
    {
        ConfigurationOwnershipManifest manifest = CreateValidOwnershipManifest() with
        {
            Entries =
            [
                CreateValidOwnershipEntry() with
                {
                    Operation = operation,
                    HasPlannedValue = false,
                    PlannedValueSha256 = null,
                    PreviousOwnedEntryMetadata = operation
                        is ConfigurationChangeOperation.Update
                            or ConfigurationChangeOperation.Refresh
                        ? "previous-metadata"
                        : null,
                },
            ],
        };
        string json = RawOwnershipManifestJson(manifest);

        Assert.False(ConfigurationOwnershipManifestPolicy.IsValid(manifest));
        Assert.ThrowsAny<Exception>(() =>
            ConfigurationOwnershipManifestSerializer.Deserialize(json)
        );
    }

    [Theory]
    [InlineData(ConfigurationChangeOperation.Remove)]
    [InlineData(ConfigurationChangeOperation.EnsureFile)]
    [InlineData(ConfigurationChangeOperation.InstallAdapter)]
    [InlineData(ConfigurationChangeOperation.RemoveAdapter)]
    public void OwnershipManifestPolicyRejectsNonValueEntriesWithPlannedValues(
        ConfigurationChangeOperation operation
    )
    {
        ConfigurationOwnershipManifest manifest = CreateValidOwnershipManifest() with
        {
            Entries =
            [
                CreateValidOwnershipEntry() with
                {
                    Operation = operation,
                    HasPlannedValue = true,
                    IsSecretValue = true,
                    PlannedValueSha256 = null,
                    PreviousOwnedEntryMetadata = operation
                        is ConfigurationChangeOperation.Remove
                            or ConfigurationChangeOperation.RemoveAdapter
                        ? "previous-metadata"
                        : null,
                },
            ],
        };
        string json = RawOwnershipManifestJson(manifest);

        Assert.False(ConfigurationOwnershipManifestPolicy.IsValid(manifest));
        Assert.ThrowsAny<Exception>(() =>
            ConfigurationOwnershipManifestSerializer.Deserialize(json)
        );
    }

    [Fact]
    public void OwnershipManifestStoreRejectsMalformedLoadedManifest()
    {
        var fileSystem = new InMemoryManifestFileSystem();
        var store = new ConfigurationOwnershipManifestStore(fileSystem);
        fileSystem.WriteAllText(
            "manifest.json",
            RawOwnershipManifestJson(
                CreateValidOwnershipManifest() with
                {
                    Entries =
                    [
                        CreateValidOwnershipEntry() with
                        {
                            Sequence = 0,
                        },
                    ],
                }
            )
        );

        Assert.Throws<ArgumentException>(() => store.Load("manifest.json"));
    }

    [Theory]
    [InlineData(nameof(IConfigurationManager.ApplyAsync))]
    [InlineData(nameof(IConfigurationManager.RemoveAsync))]
    public async Task ApplyAndRemoveRemainExplicitlyDeferredForLaterPhase4Groups(
        string methodName
    )
    {
        var manager = new ConfigurationManager();
        ConfigurationChangePlan plan = CreateValidPlan();
        Func<ValueTask<ConfigurationPlanResult>> call = CreateExecutionCall(
            manager,
            methodName,
            plan
        );

        var exception = await Assert.ThrowsAsync<NotImplementedException>(async () => await call());

        Assert.Contains("later Phase 4", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void ConfigurationManagerApiUsesDeclarativePlanContracts()
    {
        MethodInfo[] methods = typeof(IConfigurationManager)
            .GetMethods()
            .Where(method => method.DeclaringType == typeof(IConfigurationManager))
            .ToArray();

        Assert.Contains(
            methods,
            method =>
                method.Name == nameof(IConfigurationManager.ValidatePlan)
                && method.ReturnType == typeof(ConfigurationPlanValidationResult)
                && method.GetParameters().Select(parameter => parameter.ParameterType)
                    .SequenceEqual([typeof(ConfigurationChangePlan)])
        );
        Assert.All(
            methods.Where(method => method.Name != nameof(IConfigurationManager.ValidatePlan)),
            method =>
            {
                Assert.Equal(typeof(ValueTask<ConfigurationPlanResult>), method.ReturnType);
                Assert.Contains(
                    method.GetParameters(),
                    parameter => parameter.ParameterType == typeof(ConfigurationChangePlan)
                );
            }
        );
    }

    [Fact]
    public void AdapterPlanSeamsDoNotExposeFileSystemMutationTypes()
    {
        Type[] adapterFacingSeams =
        [
            typeof(IConfigurationChangePlanFactory<>),
            typeof(IConfigurationChangePlanSink),
        ];

        Assert.All(
            adapterFacingSeams.SelectMany(GetPublicSignatureTypes),
            type =>
            {
                Assert.NotEqual(typeof(IFileSystem), type);
                Assert.False(
                    type.Namespace?.StartsWith(
                        "Hcoona.AzureAuth.CredProvider.Platform.FileSystem",
                        StringComparison.Ordinal
                    ) == true,
                    $"Adapter-facing configuration seam exposes filesystem type {type.FullName}."
                );
            }
        );
        Assert.Equal(
            typeof(ConfigurationChangePlan),
            typeof(IConfigurationChangePlanFactory<>).GetMethod(
                nameof(IConfigurationChangePlanFactory<object>.CreatePlan)
            )?.ReturnType
        );
    }

    private static ConfigurationChangePlan CreateValidPlan() =>
        ConfigurationChangePlanPolicy.Create(
            "plan-git-user-config",
            "changeset-git-user-config",
            "azureauth-credprovider",
            ConfigurationScope.User,
            CreateManifest(),
            [
                new ConfigurationChange
                {
                    Operation = ConfigurationChangeOperation.Set,
                    TargetKind = ConfigurationTargetKind.GitConfig,
                    TargetPathOrName = "global git config",
                    Key = "credential.https://dev.azure.com.useHttpPath",
                    Value = "true",
                    RequiresOwnershipRecord = true,
                },
            ]
        );

    private static ConfigurationChangePlan CreateYarnNpmAuthIdentRemovalPlan(
        ConfigurationChangeOperation operation
    ) =>
        new()
        {
            PlanId = $"plan-yarn-npm-auth-ident-{operation}",
            ChangeSetId = $"changeset-yarn-npm-auth-ident-{operation}",
            OwnerProductId = "azureauth-credprovider",
            Scope = ConfigurationScope.User,
            Manifest = CreateManifest() with
            {
                ManifestId = $"manifest-yarn-npm-auth-ident-{operation}",
                EntrySelector = "yarn.npmScopes.contoso.npmAuthIdent",
            },
            ContainsCredentialMaterial = true,
            Changes =
            [
                new ConfigurationChange
                {
                    Operation = operation,
                    TargetKind = ConfigurationTargetKind.Yarnrc,
                    TargetPathOrName = "user yarnrc",
                    Key = "npmScopes.contoso.npmAuthIdent",
                    RequiresOwnershipRecord = true,
                    IsSecretValue = true,
                    PreviousOwnedEntryMetadata = "previous-yarn-npm-auth-ident-metadata",
                },
            ],
        };

    private static ConfigurationManifestMetadata CreateManifest() =>
        new()
        {
            ManifestId = "manifest-git-user-config",
            OwnerProductId = "azureauth-credprovider",
            EntrySelector = "git.credential.https://dev.azure.com.useHttpPath",
            ProductVersion = "0.0.0-test",
        };

    public static TheoryData<string> MalformedOwnershipManifestJson()
    {
        ConfigurationOwnershipManifest manifest = CreateValidOwnershipManifest();
        return
        [
            RawOwnershipManifestJson(
                manifest with
                {
                    SchemaVersion = ConfigurationOwnershipManifest.CurrentSchemaVersion + 1,
                }
            ),
            RawOwnershipManifestJson(
                manifest with
                {
                    PlanId = " ",
                }
            ),
            RawOwnershipManifestJson(
                manifest
            ).Replace("\"scope\":\"user\"", "\"scope\":999", StringComparison.Ordinal),
            RawOwnershipManifestJson(
                manifest with
                {
                    Entries =
                    [
                        CreateValidOwnershipEntry() with
                        {
                            Sequence = 2,
                        },
                    ],
                }
            ),
            RawOwnershipManifestJson(
                manifest
            ).Replace("\"operation\":\"set\"", "\"operation\":999", StringComparison.Ordinal),
            RawOwnershipManifestJson(
                manifest with
                {
                    Entries =
                    [
                        CreateValidOwnershipEntry() with
                        {
                            TargetPathOrName = "",
                        },
                    ],
                }
            ),
            RawOwnershipManifestJson(
                manifest with
                {
                    Entries =
                    [
                        CreateValidOwnershipEntry() with
                        {
                            IsSecretValue = true,
                            PlannedValueSha256 = new string('a', 64),
                        },
                    ],
                }
            ),
            RawOwnershipManifestJson(
                manifest with
                {
                    Entries =
                    [
                        CreateValidOwnershipEntry() with
                        {
                            HasPlannedValue = false,
                            PlannedValueSha256 = null,
                        },
                    ],
                }
            ),
            RawOwnershipManifestJson(
                manifest with
                {
                    Entries =
                    [
                        CreateValidOwnershipEntry() with
                        {
                            PlannedValueSha256 = null,
                        },
                    ],
                }
            ),
            RawOwnershipManifestJson(
                manifest with
                {
                    Entries =
                    [
                        CreateValidOwnershipEntry() with
                        {
                            PlannedValueSha256 = "not-a-sha256",
                        },
                    ],
                }
            ),
            RawOwnershipManifestJson(
                manifest with
                {
                    Entries =
                    [
                        CreateValidOwnershipEntry() with
                        {
                            PlannedValueSha256 =
                                "B5BEA41B6C623F7C09F1BF24DCAE58EBAB3C0CDD90AD966BC43A45B44867E12B",
                        },
                    ],
                }
            ),
            RawOwnershipManifestJson(
                manifest with
                {
                    Entries =
                    [
                        CreateValidOwnershipEntry() with
                        {
                            Operation = ConfigurationChangeOperation.Remove,
                            HasPlannedValue = false,
                            PlannedValueSha256 = null,
                        },
                    ],
                }
            ),
            RawOwnershipManifestJson(
                manifest with
                {
                    Entries =
                    [
                        CreateValidOwnershipEntry() with
                        {
                            Operation = ConfigurationChangeOperation.Update,
                            PreviousOwnedEntryMetadata = null,
                        },
                    ],
                }
            ),
            RawOwnershipManifestJson(
                manifest with
                {
                    Entries =
                    [
                        CreateValidOwnershipEntry() with
                        {
                            Operation = ConfigurationChangeOperation.Refresh,
                            PreviousOwnedEntryMetadata = null,
                        },
                    ],
                }
            ),
            RawOwnershipManifestJson(
                manifest with
                {
                    Entries =
                    [
                        CreateValidOwnershipEntry() with
                        {
                            Operation = ConfigurationChangeOperation.RemoveAdapter,
                            HasPlannedValue = false,
                            PlannedValueSha256 = null,
                            PreviousOwnedEntryMetadata = null,
                        },
                    ],
                }
            ),
            RawOwnershipManifestJson(
                manifest with
                {
                    Entries =
                    [
                        CreateValidOwnershipEntry() with
                        {
                            TargetKind = ConfigurationTargetKind.Npmrc,
                            TargetPathOrName = "user npmrc",
                            Key =
                                "//pkgs.dev.azure.com/org/_packaging/feed/npm/registry/"
                                + ":_authToken",
                            PlannedValueSha256 =
                                "b5bea41b6c623f7c09f1bf24dcae58ebab3c0cdd90ad966bc43a45b44867e12b",
                            IsSecretValue = false,
                        },
                    ],
                }
            ),
            RawOwnershipManifestJson(
                manifest with
                {
                    Entries =
                    [
                        CreateValidOwnershipEntry() with
                        {
                            TargetKind = ConfigurationTargetKind.Yarnrc,
                            TargetPathOrName = "user yarnrc",
                            Key = "npmScopes.contoso.npmAuthToken",
                            PlannedValueSha256 =
                                "b5bea41b6c623f7c09f1bf24dcae58ebab3c0cdd90ad966bc43a45b44867e12b",
                            IsSecretValue = false,
                        },
                    ],
                }
            ),
        ];
    }

    public static TheoryData<string, ConfigurationOwnershipManifest>
        MalformedOwnershipManifestCases()
    {
        ConfigurationOwnershipManifest manifest = CreateValidOwnershipManifest();
        return
        [
            new(
                "value-writing entry without planned value",
                manifest with
                {
                    Entries =
                    [
                        CreateValidOwnershipEntry() with
                        {
                            HasPlannedValue = false,
                            PlannedValueSha256 = null,
                        },
                    ],
                }
            ),
            new(
                "non-secret planned value without hash",
                manifest with
                {
                    Entries =
                    [
                        CreateValidOwnershipEntry() with
                        {
                            PlannedValueSha256 = null,
                        },
                    ],
                }
            ),
            new(
                "non-secret planned value with malformed hash",
                manifest with
                {
                    Entries =
                    [
                        CreateValidOwnershipEntry() with
                        {
                            PlannedValueSha256 = "not-a-sha256",
                        },
                    ],
                }
            ),
            new(
                "non-secret planned value with uppercase hash",
                manifest with
                {
                    Entries =
                    [
                        CreateValidOwnershipEntry() with
                        {
                            PlannedValueSha256 =
                                "B5BEA41B6C623F7C09F1BF24DCAE58EBAB3C0CDD90AD966BC43A45B44867E12B",
                        },
                    ],
                }
            ),
            new(
                "remove without previous metadata",
                manifest with
                {
                    Entries =
                    [
                        CreateValidOwnershipEntry() with
                        {
                            Operation = ConfigurationChangeOperation.Remove,
                            HasPlannedValue = false,
                            PlannedValueSha256 = null,
                        },
                    ],
                }
            ),
            new(
                "update without previous metadata",
                manifest with
                {
                    Entries =
                    [
                        CreateValidOwnershipEntry() with
                        {
                            Operation = ConfigurationChangeOperation.Update,
                            PreviousOwnedEntryMetadata = null,
                        },
                    ],
                }
            ),
            new(
                "refresh without previous metadata",
                manifest with
                {
                    Entries =
                    [
                        CreateValidOwnershipEntry() with
                        {
                            Operation = ConfigurationChangeOperation.Refresh,
                            PreviousOwnedEntryMetadata = null,
                        },
                    ],
                }
            ),
            new(
                "remove adapter without previous metadata",
                manifest with
                {
                    Entries =
                    [
                        CreateValidOwnershipEntry() with
                        {
                            Operation = ConfigurationChangeOperation.RemoveAdapter,
                            HasPlannedValue = false,
                            PlannedValueSha256 = null,
                            PreviousOwnedEntryMetadata = null,
                        },
                    ],
                }
            ),
            new(
                "npm auth token marked non-secret",
                manifest with
                {
                    Entries =
                    [
                        CreateValidOwnershipEntry() with
                        {
                            TargetKind = ConfigurationTargetKind.Npmrc,
                            TargetPathOrName = "user npmrc",
                            Key =
                                "//pkgs.dev.azure.com/org/_packaging/feed/npm/registry/"
                                + ":_authToken",
                            PlannedValueSha256 =
                                "b5bea41b6c623f7c09f1bf24dcae58ebab3c0cdd90ad966bc43a45b44867e12b",
                            IsSecretValue = false,
                        },
                    ],
                }
            ),
            new(
                "yarn auth token marked non-secret",
                manifest with
                {
                    Entries =
                    [
                        CreateValidOwnershipEntry() with
                        {
                            TargetKind = ConfigurationTargetKind.Yarnrc,
                            TargetPathOrName = "user yarnrc",
                            Key = "npmScopes.contoso.npmAuthToken",
                            PlannedValueSha256 =
                                "b5bea41b6c623f7c09f1bf24dcae58ebab3c0cdd90ad966bc43a45b44867e12b",
                            IsSecretValue = false,
                        },
                    ],
                }
            ),
        ];
    }

    private static ConfigurationOwnershipManifest CreateValidOwnershipManifest() =>
        new()
        {
            ManifestId = "manifest-ownership-valid",
            PlanId = "plan-ownership-valid",
            ChangeSetId = "changeset-ownership-valid",
            OwnerProductId = "azureauth-credprovider",
            Scope = ConfigurationScope.User,
            EntrySelector = "git.credential.helper",
            Entries = [CreateValidOwnershipEntry()],
        };

    private static ConfigurationOwnershipManifest CreateYarnNpmAuthIdentRemovalManifest(
        ConfigurationChangeOperation operation
    ) =>
        CreateValidOwnershipManifest() with
        {
            ManifestId = $"manifest-yarn-npm-auth-ident-{operation}",
            PlanId = $"plan-yarn-npm-auth-ident-{operation}",
            ChangeSetId = $"changeset-yarn-npm-auth-ident-{operation}",
            EntrySelector = "yarn.npmScopes.contoso.npmAuthIdent",
            ContainsCredentialMaterial = true,
            Entries =
            [
                new ConfigurationOwnershipManifestEntry
                {
                    Sequence = 1,
                    Operation = operation,
                    TargetKind = ConfigurationTargetKind.Yarnrc,
                    TargetPathOrName = "user yarnrc",
                    Key = "npmScopes.contoso.npmAuthIdent",
                    PreserveDeclarationsAndComments = false,
                    HasPlannedValue = false,
                    IsSecretValue = true,
                    PreviousOwnedEntryMetadata = "previous-yarn-npm-auth-ident-metadata",
                },
            ],
        };

    private static string RawOwnershipManifestJson(ConfigurationOwnershipManifest manifest) =>
        JsonSerializer.Serialize(
            manifest,
            CreateTestSerializerOptions()
        );

    private static bool RequiresValue(ConfigurationChangeOperation operation) =>
        operation
            is ConfigurationChangeOperation.Set
                or ConfigurationChangeOperation.Create
                or ConfigurationChangeOperation.Update
                or ConfigurationChangeOperation.Refresh;

    private static JsonSerializerOptions CreateTestSerializerOptions()
    {
        JsonSerializerOptions options = ContractJson.CreateSerializerOptions();
        options.TypeInfoResolver = ConfigurationManagerTestsJson.Default;
        return options;
    }

    private static ConfigurationOwnershipManifestEntry CreateValidOwnershipEntry() =>
        new()
        {
            Sequence = 1,
            Operation = ConfigurationChangeOperation.Set,
            TargetKind = ConfigurationTargetKind.GitConfig,
            TargetPathOrName = "global git config",
            Key = "credential.helper",
            PreserveDeclarationsAndComments = true,
            HasPlannedValue = true,
            PlannedValueSha256 =
                "b5bea41b6c623f7c09f1bf24dcae58ebab3c0cdd90ad966bc43a45b44867e12b",
        };

    private static Func<ValueTask<ConfigurationPlanResult>> CreateExecutionCall(
        ConfigurationManager manager,
        string methodName,
        ConfigurationChangePlan plan
    ) =>
        methodName switch
        {
            nameof(IConfigurationManager.DryRunAsync) => () =>
                manager.DryRunAsync(plan, TestContext.Current.CancellationToken),
            nameof(IConfigurationManager.ApplyAsync) => () =>
                manager.ApplyAsync(plan, TestContext.Current.CancellationToken),
            nameof(IConfigurationManager.RemoveAsync) => () =>
                manager.RemoveAsync(plan, TestContext.Current.CancellationToken),
            _ => throw new ArgumentOutOfRangeException(nameof(methodName), methodName, null),
        };

    private static void AssertObjectGraphDoesNotContainSecret(object? value, string secret)
    {
        var visited = new HashSet<object>(ReferenceEqualityComparer.Instance);
        Visit(value);

        void Visit(object? current)
        {
            if (current is null)
            {
                return;
            }

            if (current is string text)
            {
                Assert.DoesNotContain(secret, text, StringComparison.Ordinal);
                return;
            }

            Type type = current.GetType();
            if (type.IsValueType || type.IsEnum)
            {
                return;
            }

            if (!visited.Add(current))
            {
                return;
            }

            if (current is System.Collections.IEnumerable enumerable)
            {
                foreach (object? item in enumerable)
                {
                    Visit(item);
                }

                return;
            }

            foreach (
                PropertyInfo property in type.GetProperties(
                    BindingFlags.Instance | BindingFlags.Public
                )
            )
            {
                if (property.GetIndexParameters().Length == 0)
                {
                    Visit(property.GetValue(current));
                }
            }
        }
    }

    private static IEnumerable<Type> GetPublicSignatureTypes(Type type)
    {
        foreach (MethodInfo method in type.GetMethods())
        {
            yield return method.ReturnType;

            foreach (ParameterInfo parameter in method.GetParameters())
            {
                yield return parameter.ParameterType;
            }
        }

    }

    private sealed class InMemoryManifestFileSystem : IFileSystem
    {
        public string StoredText { get; private set; } = string.Empty;

        public bool FileExists(string path) => throw new NotSupportedException();

        public bool DirectoryExists(string path) => throw new NotSupportedException();

        public string GetFullPath(string path) => throw new NotSupportedException();

        public bool IsPathFullyQualified(string path) => throw new NotSupportedException();

        public bool IsSymbolicLink(string path) => throw new NotSupportedException();

        public byte[] ComputeSha256Hash(string path) => throw new NotSupportedException();

        public FileIntegritySnapshot CaptureFileIntegritySnapshot(string path) =>
            throw new NotSupportedException();

        public bool FileMatchesIntegritySnapshot(string path, FileIntegritySnapshot snapshot) =>
            throw new NotSupportedException();

        public IReadOnlyList<TrustedDirectorySnapshot> CaptureTrustedParentDirectorySnapshots(
            string path
        ) => throw new NotSupportedException();

        public FileSystemOwner GetCurrentOwner() => throw new NotSupportedException();

        public FileSystemOwner GetOwner(string path) => throw new NotSupportedException();

        public string ReadAllText(string path, Encoding? encoding = null) => StoredText;

        public void WriteAllText(string path, string contents, Encoding? encoding = null) =>
            StoredText = contents;

        public void AtomicWriteAllText(
            string path,
            string contents,
            Encoding? encoding = null,
            AtomicWriteOptions options = AtomicWriteOptions.None
        ) => StoredText = contents;

        public UnixFileMode GetUnixFileMode(string path) => throw new NotSupportedException();

        public void SetUnixFileMode(string path, UnixFileMode mode) =>
            throw new NotSupportedException();

        public void CreateDirectory(string path) => throw new NotSupportedException();

        public void DeleteFile(string path) => throw new NotSupportedException();

        public void DeleteDirectory(string path, bool recursive = false) =>
            throw new NotSupportedException();

        public IEnumerable<string> EnumerateFiles(
            string path,
            string searchPattern = "*",
            SearchOption searchOption = SearchOption.TopDirectoryOnly
        ) => throw new NotSupportedException();

        public IEnumerable<string> EnumerateDirectories(
            string path,
            string searchPattern = "*",
            SearchOption searchOption = SearchOption.TopDirectoryOnly
        ) => throw new NotSupportedException();
    }
}

[JsonSerializable(typeof(ConfigurationDryRunPlan))]
[JsonSerializable(typeof(ConfigurationPlannedChange))]
[JsonSerializable(typeof(ConfigurationPlanResult))]
[JsonSerializable(typeof(ConfigurationOwnershipManifest))]
[JsonSourceGenerationOptions(
    JsonSerializerDefaults.Web,
    WriteIndented = false,
    GenerationMode = JsonSourceGenerationMode.Metadata
)]
internal sealed partial class ConfigurationManagerTestsJson : JsonSerializerContext { }
