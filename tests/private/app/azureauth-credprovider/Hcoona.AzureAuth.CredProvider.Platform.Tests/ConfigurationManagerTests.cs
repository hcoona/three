using System.Diagnostics;
using System.Reflection;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Hcoona.AzureAuth.CredProvider.Contracts;
using Hcoona.AzureAuth.CredProvider.Platform.Configuration;
using Hcoona.AzureAuth.CredProvider.Platform.FileSystem;
using Hcoona.AzureAuth.CredProvider.Platform.Tests.TestDoubles;
using Xunit;

namespace Hcoona.AzureAuth.CredProvider.Platform.Tests;

public sealed class ConfigurationManagerTests
{
    public static bool IsWindows => OperatingSystem.IsWindows();

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

    [Fact]
    public void ValidatePlanRejectsMultipleWholeFileChangesForSameCiTemporaryFile()
    {
        var manager = new ConfigurationManager();
        const string targetPath = "/config/duplicate-validation-target.txt";
        ConfigurationChangePlan plan = CreateGenericFilePlan(
            ConfigurationChangeOperation.Create,
            targetPath,
            "first-value"
        ) with
        {
            Changes =
            [
                CreateGenericFileChange(
                    ConfigurationChangeOperation.Create,
                    targetPath,
                    "first-value"
                ),
                CreateGenericFileChange(
                    ConfigurationChangeOperation.Create,
                    targetPath,
                    "second-value"
                ) with
                {
                    Key = "other-file-key",
                },
            ],
        };

        ConfigurationPlanValidationResult result = manager.ValidatePlan(plan);

        Assert.False(result.IsValid);
        Assert.NotNull(result.Violation);
        Assert.Contains("whole-file ownership", result.Violation, StringComparison.Ordinal);
    }

    [Fact]
    public async Task DryRunAsyncRejectsMultipleWholeFileChangesForSameCiTemporaryFile()
    {
        var manager = new ConfigurationManager();
        const string targetPath = "/config/duplicate-dry-run-target.txt";
        ConfigurationChangePlan plan = CreateGenericFilePlan(
            ConfigurationChangeOperation.Create,
            targetPath,
            "first-value"
        ) with
        {
            Changes =
            [
                CreateGenericFileChange(
                    ConfigurationChangeOperation.Create,
                    targetPath,
                    "first-value"
                ),
                CreateGenericFileChange(
                    ConfigurationChangeOperation.Create,
                    targetPath,
                    "second-value"
                ) with
                {
                    Key = "other-file-key",
                },
            ],
        };

        var exception = await Assert.ThrowsAsync<ArgumentException>(async () =>
            await manager.DryRunAsync(plan, TestContext.Current.CancellationToken)
        );

        Assert.Contains("whole-file ownership", exception.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(ConfigurationChangeOperation.EnsureFile)]
    [InlineData(ConfigurationChangeOperation.InstallAdapter)]
    [InlineData(ConfigurationChangeOperation.RemoveAdapter)]
    public async Task ValidatePlanAndNoFilesystemDryRunRejectUnsupportedCiTemporaryFileOperations(
        ConfigurationChangeOperation operation
    )
    {
        var manager = new ConfigurationManager();
        string? previousMetadata =
            operation == ConfigurationChangeOperation.RemoveAdapter
                ? "previous-owned-entry-metadata"
                : null;
        ConfigurationChangePlan plan = CreateGenericFilePlan(
            operation,
            "/config/unsupported-operation-target.txt",
            value: null,
            previousOwnedEntryMetadata: previousMetadata
        );

        ConfigurationPlanValidationResult result = manager.ValidatePlan(plan);

        Assert.False(result.IsValid);
        Assert.NotNull(result.Violation);
        Assert.Contains("CI temporary file", result.Violation, StringComparison.Ordinal);
        var exception = await Assert.ThrowsAsync<ArgumentException>(async () =>
            await manager.DryRunAsync(plan, TestContext.Current.CancellationToken)
        );
        Assert.Contains("CI temporary file", exception.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(
        nameof(IConfigurationManager.ValidatePlan),
        "/state/ownership-manifest.json",
        "/state/ownership-manifest.json"
    )]
    [InlineData(
        nameof(IConfigurationManager.ValidatePlan),
        "/config/manifest-child-collision",
        "/config/manifest-child-collision/ownership-manifest.json"
    )]
    [InlineData(
        nameof(IConfigurationManager.ValidatePlan),
        "/config/manifest-parent-collision/target.txt",
        "/config/manifest-parent-collision"
    )]
    [InlineData(
        nameof(IConfigurationManager.DryRunAsync),
        "/state/ownership-manifest.json",
        "/state/ownership-manifest.json"
    )]
    [InlineData(
        nameof(IConfigurationManager.ApplyAsync),
        "/state/ownership-manifest.json",
        "/state/ownership-manifest.json"
    )]
    [InlineData(
        nameof(IConfigurationManager.RemoveAsync),
        "/state/ownership-manifest.json",
        "/state/ownership-manifest.json"
    )]
    [InlineData(
        nameof(IConfigurationManager.DryRunAsync),
        "/config/dry-run-manifest-child-collision",
        "/config/dry-run-manifest-child-collision/ownership-manifest.json"
    )]
    [InlineData(
        nameof(IConfigurationManager.ApplyAsync),
        "/config/apply-manifest-child-collision",
        "/config/apply-manifest-child-collision/ownership-manifest.json"
    )]
    [InlineData(
        nameof(IConfigurationManager.RemoveAsync),
        "/config/remove-manifest-child-collision",
        "/config/remove-manifest-child-collision/ownership-manifest.json"
    )]
    [InlineData(
        nameof(IConfigurationManager.DryRunAsync),
        "/config/dry-run-manifest-parent-collision/target.txt",
        "/config/dry-run-manifest-parent-collision"
    )]
    [InlineData(
        nameof(IConfigurationManager.ApplyAsync),
        "/config/apply-manifest-parent-collision/target.txt",
        "/config/apply-manifest-parent-collision"
    )]
    [InlineData(
        nameof(IConfigurationManager.RemoveAsync),
        "/config/remove-manifest-parent-collision/target.txt",
        "/config/remove-manifest-parent-collision"
    )]
public async Task
FilesystemBackedValidationAndExecutionRejectCiTemporaryFileTargetCollidingWithOwnershipManifestPath(
        string methodName,
        string collidingTargetPath,
        string manifestPath
    )
    {
        var fileSystem = new InMemoryFileSystem(InMemoryPathSemantics.Posix);
        var manager = new ConfigurationManager(fileSystem, manifestPath);
        ConfigurationChangeOperation operation =
            methodName == nameof(IConfigurationManager.RemoveAsync)
                ? ConfigurationChangeOperation.Remove
                : ConfigurationChangeOperation.Create;
        ConfigurationChangePlan plan = CreateGenericFilePlan(
            operation,
            collidingTargetPath,
            operation == ConfigurationChangeOperation.Remove ? null : "owned-value",
            operation == ConfigurationChangeOperation.Remove ? HashMetadata("owned-value") : null
        );

        ConfigurationPlanValidationResult validationResult = manager.ValidatePlan(plan);
        Assert.False(validationResult.IsValid);
        Assert.NotNull(validationResult.Violation);
        Assert.Contains(
            "ownership manifest path",
            validationResult.Violation,
            StringComparison.Ordinal
        );

        if (methodName == nameof(IConfigurationManager.ValidatePlan))
        {
            Assert.False(fileSystem.FileExists(manifestPath));
            Assert.False(fileSystem.FileExists(collidingTargetPath));
            return;
        }

        var exception = await Assert.ThrowsAsync<ArgumentException>(async () =>
            await CreateExecutionCall(manager, methodName, plan)()
        );

        Assert.Contains("ownership manifest path", exception.Message, StringComparison.Ordinal);
        Assert.False(fileSystem.FileExists(manifestPath));
        Assert.False(fileSystem.FileExists(collidingTargetPath));
    }

    [Fact]
    public void ValidatePlanRejectsParentChildCiTemporaryFileWholeFileTargets()
    {
        var manager = new ConfigurationManager();
        const string parentTargetPath = "/config/parent-child-validation/parent.txt";
        const string childTargetPath = "/config/parent-child-validation/parent.txt/child.txt";
        ConfigurationChangePlan plan = CreateGenericFilePlan(
            ConfigurationChangeOperation.Create,
            parentTargetPath,
            "parent-value"
        ) with
        {
            Changes =
            [
                CreateGenericFileChange(
                    ConfigurationChangeOperation.Create,
                    parentTargetPath,
                    "parent-value"
                ),
                CreateGenericFileChange(
                    ConfigurationChangeOperation.Create,
                    childTargetPath,
                    "child-value"
                ) with
                {
                    Key = "child-file",
                },
            ],
        };

        ConfigurationPlanValidationResult result = manager.ValidatePlan(plan);

        Assert.False(result.IsValid);
        Assert.NotNull(result.Violation);
        Assert.Contains("parent paths", result.Violation, StringComparison.Ordinal);
    }

    [Fact]
    public async Task NoFilesystemDryRunAsyncRejectsParentChildCiTemporaryFileWholeFileTargets()
    {
        var manager = new ConfigurationManager();
        const string parentTargetPath = "/config/parent-child-dry-run/parent.txt";
        const string childTargetPath = "/config/parent-child-dry-run/parent.txt/child.txt";
        ConfigurationChangePlan plan = CreateGenericFilePlan(
            ConfigurationChangeOperation.Create,
            parentTargetPath,
            "parent-value"
        ) with
        {
            Changes =
            [
                CreateGenericFileChange(
                    ConfigurationChangeOperation.Create,
                    parentTargetPath,
                    "parent-value"
                ),
                CreateGenericFileChange(
                    ConfigurationChangeOperation.Create,
                    childTargetPath,
                    "child-value"
                ) with
                {
                    Key = "child-file",
                },
            ],
        };

        var exception = await Assert.ThrowsAsync<ArgumentException>(async () =>
            await manager.DryRunAsync(plan, TestContext.Current.CancellationToken)
        );

        Assert.Contains("parent paths", exception.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(
        @"C:\config\parent-child-validation\parent.txt",
        @"C:\config\parent-child-validation\parent.txt\child.txt"
    )]
    [InlineData(
        @"C:\Config\parent-child-validation\Parent.txt",
        @"c:\config\parent-child-validation\parent.txt\child.txt"
    )]
    [InlineData(
        "//server/share/config/parent-child-validation/parent.txt",
        "//server/share/config/parent-child-validation/parent.txt/child.txt"
    )]
    public async Task
        ValidatePlanAndNoFilesystemDryRunRejectWindowsParentChildCiTemporaryFileWholeFileTargets(
        string parentTargetPath,
        string childTargetPath
    )
    {
        var manager = new ConfigurationManager();
        ConfigurationChangePlan plan = CreateGenericFilePlan(
            ConfigurationChangeOperation.Create,
            parentTargetPath,
            "parent-value"
        ) with
        {
            Changes =
            [
                CreateGenericFileChange(
                    ConfigurationChangeOperation.Create,
                    parentTargetPath,
                    "parent-value"
                ),
                CreateGenericFileChange(
                    ConfigurationChangeOperation.Create,
                    childTargetPath,
                    "child-value"
                ) with
                {
                    Key = "child-file",
                },
            ],
        };

        ConfigurationPlanValidationResult result = manager.ValidatePlan(plan);

        Assert.False(result.IsValid);
        Assert.NotNull(result.Violation);
        Assert.Contains("parent paths", result.Violation, StringComparison.Ordinal);
        var exception = await Assert.ThrowsAsync<ArgumentException>(async () =>
            await manager.DryRunAsync(plan, TestContext.Current.CancellationToken)
        );
        Assert.Contains("parent paths", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void ValidatePlanRejectsCiTemporaryFileAndYarnrcSamePhysicalPath()
    {
        var manager = new ConfigurationManager();
        const string targetPath = "/config/mixed-kind-validation/.yarnrc.yml";
        ConfigurationChangePlan plan = CreateGenericFilePlan(
            ConfigurationChangeOperation.Create,
            targetPath,
            "file-value"
        ) with
        {
            Changes =
            [
                CreateGenericFileChange(
                    ConfigurationChangeOperation.Create,
                    targetPath,
                    "file-value"
                ),
                CreateYarnrcFileChange(targetPath),
            ],
        };

        ConfigurationPlanValidationResult result = manager.ValidatePlan(plan);

        Assert.False(result.IsValid);
        Assert.NotNull(result.Violation);
        Assert.Contains("same physical target path", result.Violation, StringComparison.Ordinal);
    }

    [Fact]
    public async Task DryRunAsyncRejectsCiTemporaryFileAndYarnrcSamePhysicalPath()
    {
        var manager = new ConfigurationManager();
        const string targetPath = "/config/mixed-kind-dry-run/.yarnrc.yml";
        ConfigurationChangePlan plan = CreateGenericFilePlan(
            ConfigurationChangeOperation.Create,
            targetPath,
            "file-value"
        ) with
        {
            Changes =
            [
                CreateGenericFileChange(
                    ConfigurationChangeOperation.Create,
                    targetPath,
                    "file-value"
                ),
                CreateYarnrcFileChange(targetPath),
            ],
        };

        var exception = await Assert.ThrowsAsync<ArgumentException>(async () =>
            await manager.DryRunAsync(plan, TestContext.Current.CancellationToken)
        );

        Assert.Contains("same physical target path", exception.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(@"C:\config\mixed-kind-case\.yarnrc.yml", @"C:\CONFIG\MIXED-KIND-CASE\.YARNRC.YML")]
    [InlineData(
        "//server/share/config/mixed-kind-case/.yarnrc.yml",
        "//SERVER/SHARE/CONFIG/MIXED-KIND-CASE/.YARNRC.YML"
    )]
    public async Task
        ValidatePlanAndDryRunRejectWindowsConfigurationPathCaseVariantMixedKindConflicts(
        string ciTemporaryFilePath,
        string yarnrcPath
    )
    {
        var manager = new ConfigurationManager();
        ConfigurationChangePlan plan = CreateGenericFilePlan(
            ConfigurationChangeOperation.Create,
            ciTemporaryFilePath,
            "file-value"
        ) with
        {
            Changes =
            [
                CreateGenericFileChange(
                    ConfigurationChangeOperation.Create,
                    ciTemporaryFilePath,
                    "file-value"
                ),
                CreateYarnrcFileChange(yarnrcPath),
            ],
        };

        ConfigurationPlanValidationResult result = manager.ValidatePlan(plan);

        Assert.False(result.IsValid);
        Assert.NotNull(result.Violation);
        Assert.Contains("same physical target path", result.Violation, StringComparison.Ordinal);
        var exception = await Assert.ThrowsAsync<ArgumentException>(async () =>
            await manager.DryRunAsync(plan, TestContext.Current.CancellationToken)
        );
        Assert.Contains("same physical target path", exception.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(
        @"C:\config\case-variant-validation-target.txt",
        @"C:\CONFIG\CASE-VARIANT-VALIDATION-TARGET.TXT"
    )]
    [InlineData(
        "//server/share/config/case-variant-validation-target.txt",
        "//SERVER/SHARE/CONFIG/CASE-VARIANT-VALIDATION-TARGET.TXT"
    )]
    public void ValidatePlanRejectsWindowsConfigurationPathCaseVariantCiTemporaryFileDuplicates(
        string firstPath,
        string secondPath
    )
    {
        var manager = new ConfigurationManager();
        ConfigurationChangePlan plan = CreateGenericFilePlan(
            ConfigurationChangeOperation.Create,
            firstPath,
            "first-value"
        ) with
        {
            Changes =
            [
                CreateGenericFileChange(
                    ConfigurationChangeOperation.Create,
                    firstPath,
                    "first-value"
                ),
                CreateGenericFileChange(
                    ConfigurationChangeOperation.Create,
                    secondPath,
                    "second-value"
                ) with
                {
                    Key = "other-file-key",
                },
            ],
        };

        ConfigurationPlanValidationResult result = manager.ValidatePlan(plan);

        Assert.False(result.IsValid);
        Assert.NotNull(result.Violation);
        Assert.Contains("whole-file ownership", result.Violation, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(
        @"C:\config\case-variant-dry-run-target.txt",
        @"C:\CONFIG\CASE-VARIANT-DRY-RUN-TARGET.TXT"
    )]
    [InlineData(
        "//server/share/config/case-variant-dry-run-target.txt",
        "//SERVER/SHARE/CONFIG/CASE-VARIANT-DRY-RUN-TARGET.TXT"
    )]
    public async Task
        DryRunAsyncRejectsWindowsConfigurationPathCaseVariantCiTemporaryFileDuplicates(
        string firstPath,
        string secondPath
    )
    {
        var manager = new ConfigurationManager();
        ConfigurationChangePlan plan = CreateGenericFilePlan(
            ConfigurationChangeOperation.Create,
            firstPath,
            "first-value"
        ) with
        {
            Changes =
            [
                CreateGenericFileChange(
                    ConfigurationChangeOperation.Create,
                    firstPath,
                    "first-value"
                ),
                CreateGenericFileChange(
                    ConfigurationChangeOperation.Create,
                    secondPath,
                    "second-value"
                ) with
                {
                    Key = "other-file-key",
                },
            ],
        };

        var exception = await Assert.ThrowsAsync<ArgumentException>(async () =>
            await manager.DryRunAsync(plan, TestContext.Current.CancellationToken)
        );

        Assert.Contains("whole-file ownership", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ValidatePlanAndDryRunAllowPosixCaseVariantCiTemporaryFileTargets()
    {
        var manager = new ConfigurationManager();
        const string firstPath = "/config/case-sensitive-validation-target.txt";
        const string secondPath = "/config/CASE-SENSITIVE-VALIDATION-TARGET.TXT";
        ConfigurationChangePlan plan = CreateGenericFilePlan(
            ConfigurationChangeOperation.Create,
            firstPath,
            "first-value"
        ) with
        {
            Changes =
            [
                CreateGenericFileChange(
                    ConfigurationChangeOperation.Create,
                    firstPath,
                    "first-value"
                ),
                CreateGenericFileChange(
                    ConfigurationChangeOperation.Create,
                    secondPath,
                    "second-value"
                ) with
                {
                    Key = "other-file-key",
                },
            ],
        };

        ConfigurationPlanValidationResult result = manager.ValidatePlan(plan);

        Assert.True(result.IsValid);
        Assert.Null(result.Violation);

        ConfigurationPlanResult dryRun = await manager.DryRunAsync(
            plan,
            TestContext.Current.CancellationToken
        );

        Assert.Equal(ConfigurationPlanState.Planned, dryRun.State);
        Assert.Equal(2, dryRun.PlannedOperations.Count);
    }

    [Theory]
    [InlineData(@"C:\configuration-manager-tests\config", @"C:\configuration-manager-tests\config")]
    [InlineData("C:/configuration-manager-tests/config", @"C:\configuration-manager-tests\config")]
    [InlineData("/configuration-manager-tests/config", "/configuration-manager-tests/config")]
    [InlineData(
        @"\\server\share\configuration-manager-tests\config",
        "//server/share/configuration-manager-tests/config"
    )]
    public void ToConfigurationPathProducesCanonicalConfigurationPaths(
        string path,
        string expectedPath
    )
    {
        string configurationPath = ToConfigurationPath(path);

        Assert.Equal(expectedPath, configurationPath);
        Assert.True(
            ConfigurationChangePlanPolicy.IsValid(
                CreateGenericFilePlan(
                    ConfigurationChangeOperation.Create,
                    configurationPath,
                    "contract-check"
                )
            )
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
    [InlineData(ConfigurationChangeOperation.Create, null, "created-value")]
    [InlineData(ConfigurationChangeOperation.Update, "before-update", "after-update")]
    [InlineData(ConfigurationChangeOperation.Refresh, "before-refresh", "after-refresh")]
    public async Task ApplyWritesGenericFilesAndPersistsManifest(
        ConfigurationChangeOperation operation,
        string? before,
        string after
    )
    {
        var fileSystem = new InMemoryFileSystem(InMemoryPathSemantics.Posix);
        const string targetPath = "/config/generic.txt";
        const string manifestPath = "/state/manifest.json";
        if (before is not null)
        {
            fileSystem.AtomicWriteAllText(targetPath, before);
        }

        var manager = new ConfigurationManager(fileSystem, manifestPath);
        string? previousManifestHash = null;
        if (
            operation is ConfigurationChangeOperation.Update
                or ConfigurationChangeOperation.Refresh
        )
        {
            string manifestJson = await CreateSingleGenericFileManifestJsonAsync(
                targetPath,
                before!
            );
            fileSystem.AtomicWriteAllText(manifestPath, manifestJson);
            previousManifestHash = HashMetadata(manifestJson);
        }

        ConfigurationChangePlan plan = CreateGenericFilePlan(
            operation,
            targetPath,
            after,
            before is null ? null : HashMetadata(before),
            previousManifestHash
        );
        ConfigurationPlanResult dryRun = await manager.DryRunAsync(
            plan,
            TestContext.Current.CancellationToken
        );

        ConfigurationPlanResult result = await manager.ApplyAsync(
            plan,
            TestContext.Current.CancellationToken
        );

        Assert.Equal(ConfigurationPlanState.Applied, result.State);
        Assert.Equal(dryRun.PlannedOperations, result.PlannedOperations);
        Assert.Equal(after, fileSystem.ReadAllText(targetPath));
        ConfigurationOwnershipManifest manifest =
            ConfigurationOwnershipManifestSerializer.Deserialize(
                fileSystem.ReadAllText(manifestPath)
            );
        Assert.Equal(
            ConfigurationOwnershipManifestSerializer.Serialize(result.OwnershipManifest!),
            ConfigurationOwnershipManifestSerializer.Serialize(manifest)
        );
        Assert.DoesNotContain(
            after,
            fileSystem.ReadAllText(manifestPath),
            StringComparison.Ordinal
        );
    }

    [Fact]
    public async Task ApplyUpdateMergesOwnershipManifestAndPreservesUntouchedEntries()
    {
        var fileSystem = new InMemoryFileSystem(InMemoryPathSemantics.Posix);
        const string firstPath = "/config/merge-first.txt";
        const string secondPath = "/config/merge-second.txt";
        const string manifestPath = "/state/merge-manifest.json";
        var manager = new ConfigurationManager(fileSystem, manifestPath);
        ConfigurationChangePlan createPlan = CreateGenericFilePlan(
            ConfigurationChangeOperation.Create,
            firstPath,
            "first-owned"
        ) with
        {
            Changes =
            [
                CreateGenericFileChange(
                    ConfigurationChangeOperation.Create,
                    firstPath,
                    "first-owned"
                ),
                CreateGenericFileChange(
                    ConfigurationChangeOperation.Create,
                    secondPath,
                    "second-owned"
                ),
            ],
        };
        await manager.ApplyAsync(createPlan, TestContext.Current.CancellationToken);
        string manifestBefore = fileSystem.ReadAllText(manifestPath);
        ConfigurationChangePlan updatePlan = CreateGenericFilePlan(
            ConfigurationChangeOperation.Update,
            firstPath,
            "first-updated",
            HashMetadata("first-owned"),
            previousManifestHash: HashMetadata(manifestBefore)
        );

        ConfigurationPlanResult dryRun = await manager.DryRunAsync(
            updatePlan,
            TestContext.Current.CancellationToken
        );
        ConfigurationPlanResult result = await manager.ApplyAsync(
            updatePlan,
            TestContext.Current.CancellationToken
        );

        Assert.Equal("first-updated", fileSystem.ReadAllText(firstPath));
        Assert.Equal("second-owned", fileSystem.ReadAllText(secondPath));
        ConfigurationOwnershipManifest manifest =
            ConfigurationOwnershipManifestSerializer.Deserialize(
                fileSystem.ReadAllText(manifestPath)
            );
        Assert.Equal(
            ConfigurationOwnershipManifestSerializer.Serialize(result.OwnershipManifest!),
            ConfigurationOwnershipManifestSerializer.Serialize(dryRun.OwnershipManifest!)
        );
        Assert.Equal(
            ConfigurationOwnershipManifestSerializer.Serialize(result.OwnershipManifest!),
            ConfigurationOwnershipManifestSerializer.Serialize(manifest)
        );
        Assert.Equal([1, 2], manifest.Entries.Select(entry => entry.Sequence));
        Assert.Contains(
            manifest.Entries,
            entry =>
                entry.TargetPathOrName == firstPath
                && entry.Operation == ConfigurationChangeOperation.Update
                && entry.PlannedValueSha256 == HashMetadata("first-updated")["sha256:".Length..]
        );
        Assert.Contains(
            manifest.Entries,
            entry =>
                entry.TargetPathOrName == secondPath
                && entry.Operation == ConfigurationChangeOperation.Create
        );
    }

    [Fact]
    public async Task ApplyUpdateRejectsLegacyBomFileWithNoBomBeforeHash()
    {
        var fileSystem = new SystemFileSystem();
        string root = CreateSystemFileSystemTestDirectory();
        string containerPath = ToConfigurationPath(Path.Combine(root, "config"));
        string targetPath = ToConfigurationPath(Path.Combine(containerPath, "legacy-bom.txt"));
        string manifestPath = ToConfigurationPath(Path.Combine(root, "state", "manifest.json"));
        const string before = "legacy contents";
        const string after = "updated contents";
        byte[] bomBeforeBytes = CreateUtf8BomBytes(before);

        try
        {
            Directory.CreateDirectory(containerPath);
            File.WriteAllBytes(targetPath, bomBeforeBytes);
            string manifestJson = await CreateSingleGenericFileManifestJsonAsync(
                targetPath,
                before
            );
            fileSystem.AtomicWriteAllText(manifestPath, manifestJson);
            string manifestBefore = fileSystem.ReadAllText(manifestPath);
            var manager = new ConfigurationManager(fileSystem, manifestPath);
            ConfigurationChangePlan plan = CreateGenericFilePlan(
                ConfigurationChangeOperation.Update,
                targetPath,
                after,
                HashMetadata(before),
                previousManifestHash: HashMetadata(manifestJson)
            );

            await Assert.ThrowsAsync<InvalidOperationException>(async () =>
                await manager.ApplyAsync(plan, TestContext.Current.CancellationToken)
            );

            Assert.Equal(bomBeforeBytes, File.ReadAllBytes(targetPath));
            Assert.Equal(manifestBefore, fileSystem.ReadAllText(manifestPath));
        }
        finally
        {
            DeleteSystemFileSystemTestDirectory(root);
        }
    }

    [Fact]
    public async Task ApplyRefreshRejectsLegacyBomFileWithNoBomBeforeHash()
    {
        var fileSystem = new SystemFileSystem();
        string root = CreateSystemFileSystemTestDirectory();
        string containerPath = ToConfigurationPath(Path.Combine(root, "config"));
        string targetPath = ToConfigurationPath(Path.Combine(containerPath, "legacy-bom.txt"));
        string manifestPath = ToConfigurationPath(Path.Combine(root, "state", "manifest.json"));
        const string before = "legacy contents";
        const string after = "refreshed contents";
        byte[] bomBeforeBytes = CreateUtf8BomBytes(before);

        try
        {
            Directory.CreateDirectory(containerPath);
            File.WriteAllBytes(targetPath, bomBeforeBytes);
            string manifestJson = await CreateSingleGenericFileManifestJsonAsync(
                targetPath,
                before
            );
            fileSystem.AtomicWriteAllText(manifestPath, manifestJson);
            string manifestBefore = fileSystem.ReadAllText(manifestPath);
            var manager = new ConfigurationManager(fileSystem, manifestPath);
            ConfigurationChangePlan plan = CreateGenericFilePlan(
                ConfigurationChangeOperation.Refresh,
                targetPath,
                after,
                HashMetadata(before),
                previousManifestHash: HashMetadata(manifestJson)
            );

            await Assert.ThrowsAsync<InvalidOperationException>(async () =>
                await manager.ApplyAsync(plan, TestContext.Current.CancellationToken)
            );

            Assert.Equal(bomBeforeBytes, File.ReadAllBytes(targetPath));
            Assert.Equal(manifestBefore, fileSystem.ReadAllText(manifestPath));
        }
        finally
        {
            DeleteSystemFileSystemTestDirectory(root);
        }
    }

    [Fact]
    public async Task RemoveRejectsLegacyBomFileWithNoBomBeforeHash()
    {
        var fileSystem = new SystemFileSystem();
        string root = CreateSystemFileSystemTestDirectory();
        string containerPath = ToConfigurationPath(Path.Combine(root, "config"));
        string targetPath = ToConfigurationPath(Path.Combine(containerPath, "legacy-bom.txt"));
        string manifestPath = ToConfigurationPath(Path.Combine(root, "state", "manifest.json"));
        const string value = "owned contents";

        try
        {
            var manager = new ConfigurationManager(fileSystem, manifestPath);
            await manager.ApplyAsync(
                CreateGenericFilePlan(ConfigurationChangeOperation.Create, targetPath, value),
                TestContext.Current.CancellationToken
            );
            string manifestHash = HashMetadata(fileSystem.ReadAllText(manifestPath));
            string manifestBefore = fileSystem.ReadAllText(manifestPath);
            byte[] bomValueBytes = CreateUtf8BomBytes(value);
            File.WriteAllBytes(targetPath, bomValueBytes);
            ConfigurationChangePlan removePlan = CreateGenericFilePlan(
                ConfigurationChangeOperation.Remove,
                targetPath,
                value: null,
                previousOwnedEntryMetadata: HashMetadata(value),
                previousManifestHash: manifestHash
            );

            await Assert.ThrowsAsync<InvalidOperationException>(async () =>
                await manager.RemoveAsync(removePlan, TestContext.Current.CancellationToken)
            );

            Assert.Equal(bomValueBytes, File.ReadAllBytes(targetPath));
            Assert.Equal(manifestBefore, fileSystem.ReadAllText(manifestPath));
        }
        finally
        {
            DeleteSystemFileSystemTestDirectory(root);
        }
    }

    [Fact]
    public async Task ApplyUpdateRejectsManifestBomMutationWithNoBomManifestHash()
    {
        await AssertApplyRejectsManifestBomMutationWithNoBomManifestHashAsync(
            ConfigurationChangeOperation.Update
        );
    }

    [Fact]
    public async Task ApplyRefreshRejectsManifestBomMutationWithNoBomManifestHash()
    {
        await AssertApplyRejectsManifestBomMutationWithNoBomManifestHashAsync(
            ConfigurationChangeOperation.Refresh
        );
    }

    [Fact]
    public async Task RemoveRejectsManifestBomMutationWithNoBomManifestHash()
    {
        var fileSystem = new SystemFileSystem();
        string root = CreateSystemFileSystemTestDirectory();
        string containerPath = ToConfigurationPath(Path.Combine(root, "config"));
        string targetPath = ToConfigurationPath(Path.Combine(containerPath, "owned.txt"));
        string manifestPath = ToConfigurationPath(Path.Combine(root, "state", "manifest.json"));
        const string value = "owned contents";

        try
        {
            Directory.CreateDirectory(containerPath);
            File.WriteAllText(targetPath, value, new UTF8Encoding(false));
            string manifestJson = await CreateSingleGenericFileManifestJsonAsync(
                targetPath,
                value
            );
            Directory.CreateDirectory(Path.GetDirectoryName(manifestPath)!);
            File.WriteAllText(manifestPath, manifestJson, new UTF8Encoding(false));
            byte[] noBomManifestBytes = File.ReadAllBytes(manifestPath);
            string noBomManifestHash = HashMetadata(noBomManifestBytes);
            byte[] bomManifestBytes = CreateUtf8BomBytes(manifestJson);
            File.WriteAllBytes(manifestPath, bomManifestBytes);
            var manager = new ConfigurationManager(fileSystem, manifestPath);
            ConfigurationChangePlan removePlan = CreateGenericFilePlan(
                ConfigurationChangeOperation.Remove,
                targetPath,
                value: null,
                previousOwnedEntryMetadata: HashMetadata(value),
                previousManifestHash: noBomManifestHash
            );

            var exception = await Assert.ThrowsAsync<InvalidOperationException>(async () =>
                await manager.RemoveAsync(removePlan, TestContext.Current.CancellationToken)
            );

            Assert.Contains("before-state hash", exception.Message, StringComparison.Ordinal);
            Assert.Equal(Encoding.UTF8.GetBytes(value), File.ReadAllBytes(targetPath));
            Assert.Equal(bomManifestBytes, File.ReadAllBytes(manifestPath));
        }
        finally
        {
            DeleteSystemFileSystemTestDirectory(root);
        }
    }

    [Fact]
    public async Task FullRemoveDeletesProductCreatedContainerWithSystemFileSystemLockArtifact()
    {
        Assert.SkipWhen(
            OperatingSystem.IsMacOS(),
            "Conditional file mutations are unsupported on macOS."
        );

        var fileSystem = new SystemFileSystem();
        string root = CreateSystemFileSystemTestDirectory();
        string containerPath = ToConfigurationPath(Path.Combine(root, "config"));
        string targetPath = ToConfigurationPath(Path.Combine(containerPath, "owned.txt"));
        string lockPath = ToConfigurationPath(
            Path.Combine(containerPath, ".azureauth-credprovider.fs.lock")
        );
        string manifestPath = ToConfigurationPath(Path.Combine(root, "state", "manifest.json"));
        const string value = "owned contents";

        try
        {
            var manager = new ConfigurationManager(fileSystem, manifestPath);
            await manager.ApplyAsync(
                CreateGenericFilePlan(ConfigurationChangeOperation.Create, targetPath, value),
                TestContext.Current.CancellationToken
            );
            Assert.True(File.Exists(lockPath));
            string manifestHash = HashMetadata(fileSystem.ReadAllText(manifestPath));
            ConfigurationChangePlan removePlan = CreateGenericFilePlan(
                ConfigurationChangeOperation.Remove,
                targetPath,
                value: null,
                previousOwnedEntryMetadata: HashMetadata(value),
                previousManifestHash: manifestHash
            );

            await manager.RemoveAsync(removePlan, TestContext.Current.CancellationToken);

            Assert.False(Directory.Exists(containerPath));
            Assert.False(fileSystem.FileExists(targetPath));
            Assert.False(fileSystem.FileExists(manifestPath));
        }
        finally
        {
            DeleteSystemFileSystemTestDirectory(root);
        }
    }

    [Fact]
    public async Task ApplyRollbackRestoresLegacyBomBytesWhenLaterMutationFails()
    {
        string root = CreateSystemFileSystemTestDirectory();
        string containerPath = ToConfigurationPath(Path.Combine(root, "config"));
        string firstPath = ToConfigurationPath(Path.Combine(containerPath, "first-bom.txt"));
        string secondPath = ToConfigurationPath(Path.Combine(containerPath, "second.txt"));
        string manifestPath = ToConfigurationPath(Path.Combine(root, "state", "manifest.json"));
        const string firstBefore = "first before";
        const string firstAfter = "first after";
        const string secondBefore = "second before";
        const string secondAfter = "second after";
        byte[] firstBeforeBytes = CreateUtf8BomBytes(firstBefore);

        try
        {
            Directory.CreateDirectory(containerPath);
            File.WriteAllBytes(firstPath, firstBeforeBytes);
            File.WriteAllText(secondPath, secondBefore, new UTF8Encoding(false));
            var fileSystem = new SystemFileSystem((checkpoint, path) =>
            {
                if (
                    checkpoint == FileMutationCheckpoint.BeforeAtomicWriteMutation
                    && string.Equals(
                        ToConfigurationPath(path),
                        secondPath,
                        OperatingSystem.IsWindows()
                            ? StringComparison.OrdinalIgnoreCase
                            : StringComparison.Ordinal
                    )
                )
                {
                    throw new IOException("Injected later mutation failure.");
                }
            });
            ConfigurationChangePlan manifestlessPlan = CreateGenericFilePlan(
                ConfigurationChangeOperation.Update,
                firstPath,
                firstAfter,
                HashMetadata(firstBeforeBytes)
            ) with
            {
                Changes =
                [
                    CreateGenericFileChange(
                        ConfigurationChangeOperation.Update,
                        firstPath,
                        firstAfter,
                        HashMetadata(firstBeforeBytes)
                    ),
                    CreateGenericFileChange(
                        ConfigurationChangeOperation.Update,
                        secondPath,
                        secondAfter,
                        HashMetadata(secondBefore)
                    ),
                ],
            };
            var planningManager = new ConfigurationManager();
            ConfigurationPlanResult dryRun = await planningManager.DryRunAsync(
                manifestlessPlan,
                TestContext.Current.CancellationToken
            );
            string manifestJson = RawOwnershipManifestJson(dryRun.OwnershipManifest!);
            fileSystem.AtomicWriteAllText(manifestPath, manifestJson);
            var manager = new ConfigurationManager(fileSystem, manifestPath);
            ConfigurationChangePlan plan = manifestlessPlan with
            {
                Manifest = manifestlessPlan.Manifest with
                {
                    PreviousOwnedEntryHash = HashMetadata(manifestJson),
                },
            };

            await Assert.ThrowsAsync<IOException>(async () =>
                await manager.ApplyAsync(plan, TestContext.Current.CancellationToken)
            );

            Assert.Equal(firstBeforeBytes, File.ReadAllBytes(firstPath));
            Assert.Equal(Encoding.UTF8.GetBytes(secondBefore), File.ReadAllBytes(secondPath));
            Assert.Equal(manifestJson, fileSystem.ReadAllText(manifestPath));
        }
        finally
        {
            DeleteSystemFileSystemTestDirectory(root);
        }
    }

    [Fact]
    public async Task ApplyUpdateSameCiTemporaryFileWithDifferentKeyReplacesWholeFileEntry()
    {
        var fileSystem = new InMemoryFileSystem(InMemoryPathSemantics.Posix);
        const string targetPath = "/config/same-file-different-key.txt";
        const string manifestPath = "/state/same-file-different-key-manifest.json";
        var manager = new ConfigurationManager(fileSystem, manifestPath);
        await manager.ApplyAsync(
            CreateGenericFilePlan(
                ConfigurationChangeOperation.Create,
                targetPath,
                "owned-before"
            ),
            TestContext.Current.CancellationToken
        );
        string manifestBefore = fileSystem.ReadAllText(manifestPath);
        ConfigurationChangePlan updatePlan = CreateGenericFilePlan(
            ConfigurationChangeOperation.Update,
            targetPath,
            "owned-after",
            HashMetadata("owned-before"),
            previousManifestHash: HashMetadata(manifestBefore)
        ) with
        {
            Changes =
            [
                CreateGenericFileChange(
                    ConfigurationChangeOperation.Update,
                    targetPath,
                    "owned-after",
                    HashMetadata("owned-before")
                ) with
                {
                    Key = "other-file-key",
                },
            ],
        };

        ConfigurationPlanResult result = await manager.ApplyAsync(
            updatePlan,
            TestContext.Current.CancellationToken
        );

        Assert.Equal("owned-after", fileSystem.ReadAllText(targetPath));
        ConfigurationOwnershipManifest manifest =
            ConfigurationOwnershipManifestSerializer.Deserialize(
                fileSystem.ReadAllText(manifestPath)
            );
        ConfigurationOwnershipManifestEntry entry = Assert.Single(manifest.Entries);
        Assert.Equal("other-file-key", entry.Key);
        Assert.Equal(targetPath, entry.TargetPathOrName);
        Assert.Equal(
            ConfigurationOwnershipManifestSerializer.Serialize(result.OwnershipManifest!),
            ConfigurationOwnershipManifestSerializer.Serialize(manifest)
        );
        Assert.DoesNotContain(
            "\"key\":\"file\"",
            fileSystem.ReadAllText(manifestPath),
            StringComparison.Ordinal
        );
    }

    [Fact]
    public async Task RemoveSameCiTemporaryFileWithDifferentKeyRemovesWholeFileEntry()
    {
        var fileSystem = new InMemoryFileSystem(InMemoryPathSemantics.Posix);
        const string targetPath = "/config/remove-same-file-different-key.txt";
        const string manifestPath = "/state/remove-same-file-different-key-manifest.json";
        var manager = new ConfigurationManager(fileSystem, manifestPath);
        await manager.ApplyAsync(
            CreateGenericFilePlan(
                ConfigurationChangeOperation.Create,
                targetPath,
                "owned-before"
            ) with
            {
                Changes =
                [
                    CreateGenericFileChange(
                        ConfigurationChangeOperation.Create,
                        targetPath,
                        "owned-before"
                    ) with
                    {
                        Key = "original-file-key",
                    },
                ],
            },
            TestContext.Current.CancellationToken
        );
        string manifestBefore = fileSystem.ReadAllText(manifestPath);
        ConfigurationChangePlan removePlan = CreateGenericFilePlan(
            ConfigurationChangeOperation.Remove,
            targetPath,
            value: null,
            previousOwnedEntryMetadata: HashMetadata("owned-before"),
            previousManifestHash: HashMetadata(manifestBefore)
        ) with
        {
            Changes =
            [
                CreateGenericFileChange(
                    ConfigurationChangeOperation.Remove,
                    targetPath,
                    value: null,
                    previousOwnedEntryMetadata: HashMetadata("owned-before")
                ) with
                {
                    Key = "other-file-key",
                },
            ],
        };

        ConfigurationPlanResult result = await manager.RemoveAsync(
            removePlan,
            TestContext.Current.CancellationToken
        );

        Assert.Equal(ConfigurationPlanState.Applied, result.State);
        Assert.Null(result.OwnershipManifest);
        Assert.False(fileSystem.FileExists(targetPath));
        Assert.False(fileSystem.FileExists(manifestPath));
    }

    [Fact]
    public async Task ApplyRejectsRemoveChangeWithoutExistingManifestBeforeMutation()
    {
        var fileSystem = new InMemoryFileSystem(InMemoryPathSemantics.Posix);
        const string targetPath = "/config/apply-remove-without-manifest.txt";
        const string manifestPath = "/state/apply-remove-without-manifest.json";
        const string before = "owned-before";
        fileSystem.AtomicWriteAllText(targetPath, before);
        var manager = new ConfigurationManager(fileSystem, manifestPath);
        ConfigurationChangePlan plan = CreateGenericFilePlan(
            ConfigurationChangeOperation.Remove,
            targetPath,
            value: null,
            previousOwnedEntryMetadata: HashMetadata(before)
        );

        var exception = await Assert.ThrowsAsync<NotSupportedException>(async () =>
            await manager.ApplyAsync(plan, TestContext.Current.CancellationToken)
        );

        Assert.Contains("remove changes", exception.Message, StringComparison.Ordinal);
        Assert.Equal(before, fileSystem.ReadAllText(targetPath));
        Assert.False(fileSystem.FileExists(manifestPath));
    }

    [Fact]
    public async Task ApplyRejectsRemoveChangeWithExistingManifestBeforeMutation()
    {
        var fileSystem = new InMemoryFileSystem(InMemoryPathSemantics.Posix);
        const string targetPath = "/config/apply-remove-with-manifest.txt";
        const string manifestPath = "/state/apply-remove-with-manifest.json";
        const string before = "owned-before";
        var manager = new ConfigurationManager(fileSystem, manifestPath);
        await manager.ApplyAsync(
            CreateGenericFilePlan(ConfigurationChangeOperation.Create, targetPath, before),
            TestContext.Current.CancellationToken
        );
        string manifestBefore = fileSystem.ReadAllText(manifestPath);
        ConfigurationChangePlan plan = CreateGenericFilePlan(
            ConfigurationChangeOperation.Remove,
            targetPath,
            value: null,
            previousOwnedEntryMetadata: HashMetadata(before),
            previousManifestHash: HashMetadata(manifestBefore)
        );

        var exception = await Assert.ThrowsAsync<NotSupportedException>(async () =>
            await manager.ApplyAsync(plan, TestContext.Current.CancellationToken)
        );

        Assert.Contains("remove changes", exception.Message, StringComparison.Ordinal);
        Assert.Equal(before, fileSystem.ReadAllText(targetPath));
        Assert.Equal(manifestBefore, fileSystem.ReadAllText(manifestPath));
    }

    [Fact]
    public async Task ApplyRejectsReservedCiTemporaryFileSystemLockTargetBeforeMutation()
    {
        var fileSystem = new InMemoryFileSystem(InMemoryPathSemantics.Posix);
        const string targetPath = "/config/.azureauth-credprovider.fs.lock";
        const string manifestPath = "/state/reserved-lock-apply-manifest.json";
        var manager = new ConfigurationManager(fileSystem, manifestPath);
        ConfigurationChangePlan plan = CreateGenericFilePlan(
            ConfigurationChangeOperation.Create,
            targetPath,
            "owned-after"
        );

        var exception = await Assert.ThrowsAsync<NotSupportedException>(async () =>
            await manager.ApplyAsync(plan, TestContext.Current.CancellationToken)
        );

        Assert.Contains(
            "reserved internal filesystem artifact",
            exception.Message,
            StringComparison.Ordinal
        );
        Assert.False(fileSystem.FileExists(targetPath));
        Assert.False(fileSystem.FileExists(manifestPath));
    }

    [Fact]
    public async Task RemoveRejectsReservedCiTemporaryFileSystemLockTargetBeforeMutation()
    {
        var fileSystem = new InMemoryFileSystem(InMemoryPathSemantics.Posix);
        const string targetPath = "/config/.azureauth-credprovider.fs.lock";
        const string manifestPath = "/state/reserved-lock-remove-manifest.json";
        const string before = "owned-before";
        fileSystem.AtomicWriteAllText(targetPath, before);
        var manager = new ConfigurationManager(fileSystem, manifestPath);
        ConfigurationChangePlan plan = CreateGenericFilePlan(
            ConfigurationChangeOperation.Remove,
            targetPath,
            value: null,
            previousOwnedEntryMetadata: HashMetadata(before)
        );

        var exception = await Assert.ThrowsAsync<NotSupportedException>(async () =>
            await manager.RemoveAsync(plan, TestContext.Current.CancellationToken)
        );

        Assert.Contains(
            "reserved internal filesystem artifact",
            exception.Message,
            StringComparison.Ordinal
        );
        Assert.Equal(before, fileSystem.ReadAllText(targetPath));
        Assert.False(fileSystem.FileExists(manifestPath));
    }

    [Fact]
    public void ReservedCiTemporaryFileSystemLockComparisonUsesPathIdentitySemantics()
    {
        Assert.True(
            ConfigurationManager.IsReservedInternalFileSystemArtifact(
                "/config/.AZUREAUTH-CREDPROVIDER.FS.LOCK",
                StringComparison.OrdinalIgnoreCase
            )
        );
        Assert.False(
            ConfigurationManager.IsReservedInternalFileSystemArtifact(
                "/config/.AZUREAUTH-CREDPROVIDER.FS.LOCK",
                StringComparison.Ordinal
            )
        );
    }

    [Fact(
        Skip = "Windows case-insensitive filesystem semantics required.",
        SkipUnless = nameof(IsWindows)
    )]
    public async Task
        ApplyRejectsWindowsCaseVariantReservedCiTemporaryFileSystemLockTargetBeforeMutation()
    {
        var fileSystem = new InMemoryFileSystem(InMemoryPathSemantics.Posix);
        const string targetPath = "/config/.AZUREAUTH-CREDPROVIDER.FS.LOCK";
        const string manifestPath = "/state/reserved-lock-windows-case-apply-manifest.json";
        var manager = new ConfigurationManager(fileSystem, manifestPath);
        ConfigurationChangePlan plan = CreateGenericFilePlan(
            ConfigurationChangeOperation.Create,
            targetPath,
            "owned-after"
        );

        var exception = await Assert.ThrowsAsync<NotSupportedException>(async () =>
            await manager.ApplyAsync(plan, TestContext.Current.CancellationToken)
        );

        Assert.Contains(
            "reserved internal filesystem artifact",
            exception.Message,
            StringComparison.Ordinal
        );
        Assert.False(fileSystem.FileExists(targetPath));
        Assert.False(fileSystem.FileExists(manifestPath));
    }

    [Fact(
        Skip = "Windows case-insensitive filesystem semantics required.",
        SkipUnless = nameof(IsWindows)
    )]
    public async Task ApplyRejectsWindowsCaseVariantCiTemporaryFilePlanDuplicatesBeforeMutation()
    {
        var fileSystem = new InMemoryFileSystem(InMemoryPathSemantics.Posix);
        const string firstPath = "/config/case-variant.txt";
        const string secondPath = "/CONFIG/CASE-VARIANT.TXT";
        const string manifestPath = "/state/case-variant-manifest.json";
        var manager = new ConfigurationManager(fileSystem, manifestPath);
        ConfigurationChangePlan plan = CreateGenericFilePlan(
            ConfigurationChangeOperation.Create,
            firstPath,
            "first-value"
        ) with
        {
            Changes =
            [
                CreateGenericFileChange(
                    ConfigurationChangeOperation.Create,
                    firstPath,
                    "first-value"
                ),
                CreateGenericFileChange(
                    ConfigurationChangeOperation.Create,
                    secondPath,
                    "second-value"
                ),
            ],
        };

        var exception = await Assert.ThrowsAsync<NotSupportedException>(async () =>
            await manager.ApplyAsync(plan, TestContext.Current.CancellationToken)
        );

        Assert.Contains("whole-file ownership", exception.Message, StringComparison.Ordinal);
        Assert.False(fileSystem.FileExists(firstPath));
        Assert.False(fileSystem.FileExists(secondPath));
        Assert.False(fileSystem.FileExists(manifestPath));
    }

    [Fact]
    public async Task ApplyRejectsMultipleWholeFileChangesForSameCiTemporaryFileBeforeMutation()
    {
        var fileSystem = new InMemoryFileSystem(InMemoryPathSemantics.Posix);
        const string targetPath = "/config/duplicate-plan-target.txt";
        const string manifestPath = "/state/duplicate-plan-target-manifest.json";
        var manager = new ConfigurationManager(fileSystem, manifestPath);
        ConfigurationChangePlan plan = CreateGenericFilePlan(
            ConfigurationChangeOperation.Create,
            targetPath,
            "first-value"
        ) with
        {
            Changes =
            [
                CreateGenericFileChange(
                    ConfigurationChangeOperation.Create,
                    targetPath,
                    "first-value"
                ),
                CreateGenericFileChange(
                    ConfigurationChangeOperation.Create,
                    targetPath,
                    "second-value"
                ) with
                {
                    Key = "other-file-key",
                },
            ],
        };

        var exception = await Assert.ThrowsAsync<NotSupportedException>(async () =>
            await manager.ApplyAsync(plan, TestContext.Current.CancellationToken)
        );

        Assert.Contains("whole-file ownership", exception.Message, StringComparison.Ordinal);
        Assert.False(fileSystem.FileExists(targetPath));
        Assert.False(fileSystem.FileExists(manifestPath));
    }

    [Theory]
    [InlineData(nameof(IConfigurationManager.DryRunAsync))]
    [InlineData(nameof(IConfigurationManager.ApplyAsync))]
    public async Task ApplyAndDryRunRejectExistingManifestWithMultipleEntriesForSameCiTemporaryFile(
        string methodName
    )
    {
        var fileSystem = new InMemoryFileSystem(InMemoryPathSemantics.Posix);
        const string targetPath = "/config/duplicate-existing-apply.txt";
        const string manifestPath = "/state/duplicate-existing-apply-manifest.json";
        const string before = "owned-before";
        fileSystem.AtomicWriteAllText(targetPath, before);
        var manager = new ConfigurationManager(fileSystem, manifestPath);
        string duplicateManifestJson = await CreateDuplicateCiTemporaryFileManifestJsonAsync(
            targetPath,
            before
        );
        fileSystem.AtomicWriteAllText(manifestPath, duplicateManifestJson);
        ConfigurationChangePlan plan = CreateGenericFilePlan(
            ConfigurationChangeOperation.Update,
            targetPath,
            "owned-after",
            HashMetadata(before),
            previousManifestHash: HashMetadata(duplicateManifestJson)
        );

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(async () =>
            await CreateExecutionCall(manager, methodName, plan)()
        );

        Assert.Contains("whole-file ownership", exception.Message, StringComparison.Ordinal);
        Assert.Equal(before, fileSystem.ReadAllText(targetPath));
        Assert.Equal(duplicateManifestJson, fileSystem.ReadAllText(manifestPath));
    }

    [Theory]
    [InlineData(nameof(IConfigurationManager.ApplyAsync))]
    [InlineData(nameof(IConfigurationManager.DryRunAsync))]
    public async Task
        ApplyAndFilesystemBackedDryRunRejectManifestHashConflictBeforeTargetSnapshotReads(
        string methodName
    )
    {
        var fileSystem = new InMemoryFileSystem(InMemoryPathSemantics.Posix);
        const string targetPath = "/config/stale-manifest-before-target-read.txt";
        const string manifestPath = "/state/stale-manifest-before-target-read-manifest.json";
        const string before = "owned-before";
        fileSystem.AtomicWriteAllText(targetPath, before);
        var manager = new ConfigurationManager(fileSystem, manifestPath);
        string manifestJson = await CreateSingleGenericFileManifestJsonAsync(targetPath, before);
        fileSystem.AtomicWriteAllText(manifestPath, manifestJson);
        ConfigurationChangePlan plan = CreateGenericFilePlan(
            ConfigurationChangeOperation.Update,
            targetPath,
            "owned-after",
            HashMetadata(before),
            previousManifestHash: HashMetadata("stale manifest")
        );
        fileSystem.Calls.Clear();

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(async () =>
            await CreateExecutionCall(manager, methodName, plan)()
        );

        Assert.Contains(
            "configuration ownership manifest",
            exception.Message,
            StringComparison.Ordinal
        );
        AssertNoTargetSnapshotReads(fileSystem, targetPath);
        Assert.Equal(before, fileSystem.ReadAllText(targetPath));
        Assert.Equal(manifestJson, fileSystem.ReadAllText(manifestPath));
    }

    [Theory]
    [InlineData(nameof(IConfigurationManager.ApplyAsync), ConfigurationChangeOperation.Update)]
    [InlineData(nameof(IConfigurationManager.ApplyAsync), ConfigurationChangeOperation.Refresh)]
    [InlineData(nameof(IConfigurationManager.DryRunAsync), ConfigurationChangeOperation.Update)]
    [InlineData(nameof(IConfigurationManager.DryRunAsync), ConfigurationChangeOperation.Refresh)]
    public async Task
        ApplyAndFilesystemBackedDryRunRejectUnownedUpdateOrRefreshBeforeTargetSnapshotReads(
        string methodName,
        ConfigurationChangeOperation operation
    )
    {
        var fileSystem = new InMemoryFileSystem(InMemoryPathSemantics.Posix);
        const string targetPath = "/config/unowned-before-target-read.txt";
        const string ownedOtherPath = "/config/owned-other-before-target-read.txt";
        const string manifestPath = "/state/unowned-before-target-read-manifest.json";
        const string before = "unowned-before";
        fileSystem.AtomicWriteAllText(targetPath, before);
        fileSystem.AtomicWriteAllText(ownedOtherPath, "owned-other");
        var manager = new ConfigurationManager(fileSystem, manifestPath);
        string manifestJson = await CreateSingleGenericFileManifestJsonAsync(
            ownedOtherPath,
            "owned-other"
        );
        fileSystem.AtomicWriteAllText(manifestPath, manifestJson);
        ConfigurationChangePlan plan = CreateGenericFilePlan(
            operation,
            targetPath,
            "owned-after",
            HashMetadata(before),
            previousManifestHash: HashMetadata(manifestJson)
        );
        fileSystem.Calls.Clear();

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(async () =>
            await CreateExecutionCall(manager, methodName, plan)()
        );

        Assert.Contains(
            "owned by the existing manifest",
            exception.Message,
            StringComparison.Ordinal
        );
        AssertNoTargetSnapshotReads(fileSystem, targetPath);
        Assert.Equal(before, fileSystem.ReadAllText(targetPath));
        Assert.Equal(manifestJson, fileSystem.ReadAllText(manifestPath));
    }

    [Fact]
    public async Task RemoveRejectsMissingManifestBeforeTargetSnapshotReads()
    {
        var fileSystem = new InMemoryFileSystem(InMemoryPathSemantics.Posix);
        const string targetPath = "/config/missing-manifest-before-target-read.txt";
        const string manifestPath = "/state/missing-manifest-before-target-read-manifest.json";
        const string before = "owned-before";
        fileSystem.AtomicWriteAllText(targetPath, before);
        var manager = new ConfigurationManager(fileSystem, manifestPath);
        ConfigurationChangePlan plan = CreateGenericFilePlan(
            ConfigurationChangeOperation.Remove,
            targetPath,
            value: null,
            previousOwnedEntryMetadata: HashMetadata(before)
        );
        fileSystem.Calls.Clear();

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(async () =>
            await manager.RemoveAsync(plan, TestContext.Current.CancellationToken)
        );

        Assert.Contains("ownership manifest", exception.Message, StringComparison.Ordinal);
        AssertNoTargetSnapshotReads(fileSystem, targetPath);
        Assert.Equal(before, fileSystem.ReadAllText(targetPath));
        Assert.False(fileSystem.FileExists(manifestPath));
    }

    [Theory]
    [InlineData(nameof(IConfigurationManager.RemoveAsync))]
    [InlineData(nameof(IConfigurationManager.DryRunAsync))]
    public async Task
        RemoveAndFilesystemBackedDryRunRejectManifestHashConflictBeforeTargetSnapshotReads(
        string methodName
    )
    {
        var fileSystem = new InMemoryFileSystem(InMemoryPathSemantics.Posix);
        const string targetPath = "/config/remove-stale-manifest-before-target-read.txt";
        const string manifestPath = "/state/remove-stale-manifest-before-target-read-manifest.json";
        const string before = "owned-before";
        fileSystem.AtomicWriteAllText(targetPath, before);
        var manager = new ConfigurationManager(fileSystem, manifestPath);
        string manifestJson = await CreateSingleGenericFileManifestJsonAsync(targetPath, before);
        fileSystem.AtomicWriteAllText(manifestPath, manifestJson);
        ConfigurationChangePlan plan = CreateGenericFilePlan(
            ConfigurationChangeOperation.Remove,
            targetPath,
            value: null,
            previousOwnedEntryMetadata: HashMetadata(before),
            previousManifestHash: HashMetadata("stale manifest")
        );
        fileSystem.Calls.Clear();

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(async () =>
            await CreateExecutionCall(manager, methodName, plan)()
        );

        Assert.Contains(
            "configuration ownership manifest",
            exception.Message,
            StringComparison.Ordinal
        );
        AssertNoTargetSnapshotReads(fileSystem, targetPath);
        Assert.Equal(before, fileSystem.ReadAllText(targetPath));
        Assert.Equal(manifestJson, fileSystem.ReadAllText(manifestPath));
    }

    [Fact]
    public async Task ApplyRejectsExistingManifestWithSamePathDifferentTargetKindBeforeMutation()
    {
        var fileSystem = new InMemoryFileSystem(InMemoryPathSemantics.Posix);
        const string targetPath = "/config/same-path-different-kind-apply.txt";
        const string manifestPath = "/state/same-path-different-kind-apply-manifest.json";
        const string before = "owned-before";
        fileSystem.AtomicWriteAllText(targetPath, before);
        var manager = new ConfigurationManager(fileSystem, manifestPath);
        string conflictingManifestJson =
            await CreateSamePathDifferentTargetKindManifestJsonAsync(targetPath, before);
        fileSystem.AtomicWriteAllText(manifestPath, conflictingManifestJson);
        ConfigurationChangePlan plan = CreateGenericFilePlan(
            ConfigurationChangeOperation.Update,
            targetPath,
            "owned-after",
            HashMetadata(before),
            previousManifestHash: HashMetadata(conflictingManifestJson)
        );

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(async () =>
            await manager.ApplyAsync(plan, TestContext.Current.CancellationToken)
        );

        Assert.Contains("same physical target path", exception.Message, StringComparison.Ordinal);
        Assert.Equal(before, fileSystem.ReadAllText(targetPath));
        Assert.Equal(conflictingManifestJson, fileSystem.ReadAllText(manifestPath));
    }

    [Theory]
    [InlineData(nameof(IConfigurationManager.DryRunAsync))]
    [InlineData(nameof(IConfigurationManager.ApplyAsync))]
    public async Task FilesystemBackedApplyRejectsExistingChildManifestAndNewParentTargetConflict(
        string methodName
    )
    {
        var fileSystem = new InMemoryFileSystem(InMemoryPathSemantics.Posix);
        const string parentTargetPath = "/config/existing-child-conflict/parent.txt";
        const string childTargetPath = "/config/existing-child-conflict/parent.txt/child.txt";
        const string manifestPath = "/state/existing-child-conflict-manifest.json";
        string existingChildManifestJson =
            await CreateSingleGenericFileManifestJsonAsync(childTargetPath, "child-value");
        fileSystem.AtomicWriteAllText(manifestPath, existingChildManifestJson);
        var manager = new ConfigurationManager(fileSystem, manifestPath);
        ConfigurationChangePlan plan = CreateGenericFilePlan(
            ConfigurationChangeOperation.Create,
            parentTargetPath,
            "parent-value",
            previousManifestHash: HashMetadata(existingChildManifestJson)
        );

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(async () =>
            await CreateExecutionCall(manager, methodName, plan)()
        );

        Assert.Contains("parent paths", exception.Message, StringComparison.Ordinal);
        Assert.False(fileSystem.FileExists(parentTargetPath));
        Assert.False(fileSystem.FileExists(childTargetPath));
        Assert.Equal(existingChildManifestJson, fileSystem.ReadAllText(manifestPath));
        Assert.DoesNotContain(
            fileSystem.Calls,
            call =>
                string.Equals(
                    call.Operation,
                    nameof(IFileSystem.AtomicWriteAllText),
                    StringComparison.Ordinal
                ) && string.Equals(call.Path, parentTargetPath, StringComparison.Ordinal)
        );
    }

    [Theory]
    [InlineData(nameof(IConfigurationManager.DryRunAsync))]
    [InlineData(nameof(IConfigurationManager.ApplyAsync))]
    public async Task FilesystemBackedApplyRejectsExistingParentManifestAndNewChildTargetConflict(
        string methodName
    )
    {
        var fileSystem = new InMemoryFileSystem(InMemoryPathSemantics.Posix);
        const string productOwnedPath = "/config/existing-parent-conflict";
        const string parentTargetPath = "/config/existing-parent-conflict/parent.txt";
        const string childTargetPath = "/config/existing-parent-conflict/parent.txt/child.txt";
        const string manifestPath = "/state/existing-parent-conflict-manifest.json";
        string existingParentManifestJson =
            await CreateSingleGenericFileManifestJsonAsync(parentTargetPath, "parent-value");
        fileSystem.AtomicWriteAllText(manifestPath, existingParentManifestJson);
        var manager = new ConfigurationManager(fileSystem, manifestPath);
        ConfigurationChangePlan plan = CreateGenericFilePlan(
            ConfigurationChangeOperation.Create,
            childTargetPath,
            "child-value",
            previousManifestHash: HashMetadata(existingParentManifestJson)
        ) with
        {
            TemporaryContainer = CreateTemporaryHomeContainer(productOwnedPath),
        };

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(async () =>
            await CreateExecutionCall(manager, methodName, plan)()
        );

        Assert.Contains("parent paths", exception.Message, StringComparison.Ordinal);
        Assert.False(fileSystem.FileExists(parentTargetPath));
        Assert.False(fileSystem.FileExists(childTargetPath));
        Assert.Equal(existingParentManifestJson, fileSystem.ReadAllText(manifestPath));
        Assert.DoesNotContain(
            fileSystem.Calls,
            call =>
                string.Equals(
                    call.Operation,
                    nameof(IFileSystem.AtomicWriteAllText),
                    StringComparison.Ordinal
                ) && string.Equals(call.Path, childTargetPath, StringComparison.Ordinal)
        );
    }

    [Theory]
    [InlineData(nameof(IConfigurationManager.DryRunAsync), "equal")]
    [InlineData(nameof(IConfigurationManager.DryRunAsync), "parent")]
    [InlineData(nameof(IConfigurationManager.DryRunAsync), "child")]
    [InlineData(nameof(IConfigurationManager.ApplyAsync), "equal")]
    [InlineData(nameof(IConfigurationManager.ApplyAsync), "parent")]
    [InlineData(nameof(IConfigurationManager.ApplyAsync), "child")]
    [InlineData(nameof(IConfigurationManager.RemoveAsync), "equal")]
    [InlineData(nameof(IConfigurationManager.RemoveAsync), "parent")]
    [InlineData(nameof(IConfigurationManager.RemoveAsync), "child")]
    public async Task
        FilesystemBackedExecutionRejectsExistingManifestEntryCollidingWithOwnershipManifestPath(
        string methodName,
        string collisionKind
    )
    {
        var fileSystem = new InMemoryFileSystem(InMemoryPathSemantics.Posix);
        string suffix = $"existing-manifest-path-collision-{methodName}-{collisionKind}";
        string containerPath = $"/config/{suffix}";
        string ownedTargetPath = $"{containerPath}/owned.txt";
        string newTargetPath = $"{containerPath}/new-owned.txt";
        string manifestPath = $"{containerPath}/state/manifest.json";
        string collidingTargetPath = collisionKind switch
        {
            "equal" => manifestPath,
            "parent" => GetParentConfigurationPath(manifestPath),
            "child" => $"{manifestPath}/child.txt",
            _ => throw new UnreachableException(),
        };
        const string before = "owned-before";
        string manifestJson = await CreateManifestWithAdditionalGenericFileEntryJsonAsync(
            ownedTargetPath,
            before,
            collidingTargetPath,
            "colliding-value"
        );
        fileSystem.AtomicWriteAllText(ownedTargetPath, before);
        fileSystem.AtomicWriteAllText(manifestPath, manifestJson);
        var manager = new ConfigurationManager(fileSystem, manifestPath);
        ConfigurationChangePlan plan =
            methodName == nameof(IConfigurationManager.RemoveAsync)
                ? CreateGenericFilePlan(
                    ConfigurationChangeOperation.Remove,
                    ownedTargetPath,
                    value: null,
                    previousOwnedEntryMetadata: HashMetadata(before),
                    previousManifestHash: HashMetadata(manifestJson)
                )
                : CreateGenericFilePlan(
                    ConfigurationChangeOperation.Create,
                    newTargetPath,
                    "new-value",
                    previousManifestHash: HashMetadata(manifestJson)
                );

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(async () =>
            await CreateExecutionCall(manager, methodName, plan)()
        );

        Assert.Contains("ownership manifest path", exception.Message, StringComparison.Ordinal);
        Assert.Equal(before, fileSystem.ReadAllText(ownedTargetPath));
        Assert.Equal(manifestJson, fileSystem.ReadAllText(manifestPath));
        Assert.False(fileSystem.FileExists(newTargetPath));
    }

    [Fact]
    public async Task RemoveDeletesGenericFileAndManifestWhenHashesMatch()
    {
        var fileSystem = new InMemoryFileSystem(InMemoryPathSemantics.Posix);
        const string targetPath = "/config/remove.txt";
        const string manifestPath = "/state/remove-manifest.json";
        ConfigurationChangePlan createPlan = CreateGenericFilePlan(
            ConfigurationChangeOperation.Create,
            targetPath,
            "owned-value"
        );
        var manager = new ConfigurationManager(fileSystem, manifestPath);
        await manager.ApplyAsync(createPlan, TestContext.Current.CancellationToken);
        string manifestHash = HashMetadata(fileSystem.ReadAllText(manifestPath));
        ConfigurationChangePlan removePlan = CreateGenericFilePlan(
            ConfigurationChangeOperation.Remove,
            targetPath,
            value: null,
            previousOwnedEntryMetadata: HashMetadata("owned-value"),
            previousManifestHash: manifestHash
        );

        ConfigurationPlanResult result = await manager.RemoveAsync(
            removePlan,
            TestContext.Current.CancellationToken
        );

        Assert.Equal(ConfigurationPlanState.Applied, result.State);
        Assert.Null(result.OwnershipManifest);
        Assert.False(fileSystem.FileExists(targetPath));
        Assert.False(fileSystem.FileExists(manifestPath));
    }

    [Fact]
    public async Task RemoveDeletesTemporaryContainerCreatedByPreviousApplyAfterFullRemove()
    {
        var fileSystem = new InMemoryFileSystem(InMemoryPathSemantics.Posix);
        const string containerPath = "/config/full-remove";
        const string targetPath = "/config/full-remove/owned.txt";
        const string manifestPath = "/state/full-remove-manifest.json";
        var manager = new ConfigurationManager(fileSystem, manifestPath);
        await manager.ApplyAsync(
            CreateGenericFilePlan(ConfigurationChangeOperation.Create, targetPath, "owned-value"),
            TestContext.Current.CancellationToken
        );
        string manifestHash = HashMetadata(fileSystem.ReadAllText(manifestPath));
        ConfigurationChangePlan removePlan = CreateGenericFilePlan(
            ConfigurationChangeOperation.Remove,
            targetPath,
            value: null,
            previousOwnedEntryMetadata: HashMetadata("owned-value"),
            previousManifestHash: manifestHash
        );

        await manager.RemoveAsync(removePlan, TestContext.Current.CancellationToken);

        Assert.False(fileSystem.DirectoryExists(containerPath));
        Assert.False(fileSystem.FileExists(targetPath));
        Assert.False(fileSystem.FileExists(manifestPath));
    }

    [Fact]
    public async Task FullRemoveDeletesProductContainerWhenOnlyManifestInsideContainerPreExisted()
    {
        var fileSystem = new InMemoryFileSystem(InMemoryPathSemantics.Posix);
        const string containerPath = "/config/full-remove-manifest-inside";
        const string targetPath = "/config/full-remove-manifest-inside/owned.txt";
        const string manifestPath = "/config/full-remove-manifest-inside/state/manifest.json";
        var manager = new ConfigurationManager(fileSystem, manifestPath);
        await manager.ApplyAsync(
            CreateGenericFilePlan(ConfigurationChangeOperation.Create, targetPath, "owned-value"),
            TestContext.Current.CancellationToken
        );
        string manifestHash = HashMetadata(fileSystem.ReadAllText(manifestPath));
        ConfigurationChangePlan removePlan = CreateGenericFilePlan(
            ConfigurationChangeOperation.Remove,
            targetPath,
            value: null,
            previousOwnedEntryMetadata: HashMetadata("owned-value"),
            previousManifestHash: manifestHash
        );

        await manager.RemoveAsync(removePlan, TestContext.Current.CancellationToken);

        Assert.False(fileSystem.DirectoryExists(containerPath));
        Assert.False(fileSystem.FileExists(targetPath));
        Assert.False(fileSystem.FileExists(manifestPath));
    }

    [Fact]
    public async Task FullRemovePreservesContainerWithManifestInsideWhenUnrelatedContentPreExists()
    {
        var fileSystem = new InMemoryFileSystem(InMemoryPathSemantics.Posix);
        const string containerPath = "/config/full-remove-manifest-inside-unrelated";
        const string targetPath = "/config/full-remove-manifest-inside-unrelated/owned.txt";
        const string manifestPath = "/config/full-remove-manifest-inside-unrelated/manifest.json";
        const string unrelatedPath = "/config/full-remove-manifest-inside-unrelated/unrelated.txt";
        fileSystem.CreateDirectory(containerPath);
        fileSystem.WriteAllText(unrelatedPath, "unrelated");
        var manager = new ConfigurationManager(fileSystem, manifestPath);
        await manager.ApplyAsync(
            CreateGenericFilePlan(ConfigurationChangeOperation.Create, targetPath, "owned-value"),
            TestContext.Current.CancellationToken
        );
        string manifestHash = HashMetadata(fileSystem.ReadAllText(manifestPath));
        ConfigurationChangePlan removePlan = CreateGenericFilePlan(
            ConfigurationChangeOperation.Remove,
            targetPath,
            value: null,
            previousOwnedEntryMetadata: HashMetadata("owned-value"),
            previousManifestHash: manifestHash
        );

        await manager.RemoveAsync(removePlan, TestContext.Current.CancellationToken);

        Assert.True(fileSystem.DirectoryExists(containerPath));
        Assert.Equal("unrelated", fileSystem.ReadAllText(unrelatedPath));
        Assert.False(fileSystem.FileExists(targetPath));
        Assert.False(fileSystem.FileExists(manifestPath));
    }

    [Fact]
    public async Task FullRemovePreservesPreExistingTemporaryContainerUnrelatedContentAndLock()
    {
        var fileSystem = new InMemoryFileSystem(InMemoryPathSemantics.Posix);
        const string containerPath = "/config/full-remove-existing";
        const string targetPath = "/config/full-remove-existing/owned.txt";
        const string unrelatedPath = "/config/full-remove-existing/unrelated.txt";
        const string lockPath = "/config/full-remove-existing/.azureauth-credprovider.fs.lock";
        const string manifestPath = "/state/full-remove-existing-manifest.json";
        fileSystem.CreateDirectory(containerPath);
        fileSystem.WriteAllText(unrelatedPath, "unrelated");
        fileSystem.WriteAllText(lockPath, "pre-existing-lock");
        var manager = new ConfigurationManager(fileSystem, manifestPath);
        await manager.ApplyAsync(
            CreateGenericFilePlan(ConfigurationChangeOperation.Create, targetPath, "owned-value"),
            TestContext.Current.CancellationToken
        );
        string manifestHash = HashMetadata(fileSystem.ReadAllText(manifestPath));
        ConfigurationChangePlan removePlan = CreateGenericFilePlan(
            ConfigurationChangeOperation.Remove,
            targetPath,
            value: null,
            previousOwnedEntryMetadata: HashMetadata("owned-value"),
            previousManifestHash: manifestHash
        );

        await manager.RemoveAsync(removePlan, TestContext.Current.CancellationToken);

        Assert.True(fileSystem.DirectoryExists(containerPath));
        Assert.False(fileSystem.FileExists(targetPath));
        Assert.Equal("unrelated", fileSystem.ReadAllText(unrelatedPath));
        Assert.Equal("pre-existing-lock", fileSystem.ReadAllText(lockPath));
        Assert.False(fileSystem.FileExists(manifestPath));
    }

    [Fact]
    public async Task FullRemoveCleanupSkipsExistingContainerAfterAncestorSymlinkSwap()
    {
        var fileSystem = new InMemoryFileSystem(InMemoryPathSemantics.Posix);
        const string ancestorPath = "/config";
        const string containerPath = "/config/full-remove-ancestor-symlink-swap";
        const string targetPath = "/config/full-remove-ancestor-symlink-swap/owned.txt";
        const string outsidePath = "/outside";
        const string externalContainerPath = "/outside/full-remove-ancestor-symlink-swap";
        const string externalFilePath = "/outside/full-remove-ancestor-symlink-swap/.external.tmp";
        const string manifestPath = "/state/full-remove-ancestor-symlink-swap-manifest.json";
        fileSystem.CreateDirectory(externalContainerPath);
        fileSystem.WriteAllText(externalFilePath, "external");
        var manager = new ConfigurationManager(fileSystem, manifestPath);
        await manager.ApplyAsync(
            CreateGenericFilePlan(ConfigurationChangeOperation.Create, targetPath, "owned-value"),
            TestContext.Current.CancellationToken
        );
        string manifestHash = HashMetadata(fileSystem.ReadAllText(manifestPath));
        ConfigurationChangePlan removePlan = CreateGenericFilePlan(
            ConfigurationChangeOperation.Remove,
            targetPath,
            value: null,
            previousOwnedEntryMetadata: HashMetadata("owned-value"),
            previousManifestHash: manifestHash
        );
        var unsafeStateInstalled = false;
        var unsafeStateInstalledCallCount = 0;
        fileSystem.AfterRecord = (call, fs) =>
        {
            if (
                string.Equals(
                    call.Operation,
                    nameof(IFileSystem.DeleteFile),
                    StringComparison.Ordinal
                )
                && string.Equals(call.Path, manifestPath, StringComparison.Ordinal)
            )
            {
                fs.AfterRecord = null;
                fs.DeleteDirectory(containerPath, recursive: true);
                fs.DeleteDirectory(ancestorPath);
                fs.AddSymbolicLink(ancestorPath, outsidePath);
                unsafeStateInstalled = true;
                unsafeStateInstalledCallCount = fs.Calls.Count;
            }
        };

        await manager.RemoveAsync(removePlan, TestContext.Current.CancellationToken);

        Assert.True(unsafeStateInstalled);
        Assert.Contains(
            fileSystem.Calls.Skip(unsafeStateInstalledCallCount),
            call =>
                (
                    string.Equals(
                        call.Operation,
                        nameof(IFileSystem.IsSymbolicLink),
                        StringComparison.Ordinal
                    )
                    || string.Equals(
                        call.Operation,
                        nameof(IFileSystemReparsePointSafety.IsReparsePoint),
                        StringComparison.Ordinal
                    )
                )
                && string.Equals(call.Path, ancestorPath, StringComparison.Ordinal)
        );
        Assert.Equal("external", fileSystem.ReadAllText(externalFilePath));
        Assert.True(fileSystem.IsSymbolicLink(ancestorPath));
        Assert.False(fileSystem.FileExists(targetPath));
        Assert.False(fileSystem.FileExists(manifestPath));
    }

    [Fact]
    public async Task RemoveDeletesPartialGenericFilesAndPersistsRemainingManifestEntries()
    {
        var fileSystem = new InMemoryFileSystem(InMemoryPathSemantics.Posix);
        const string firstPath = "/config/partial-remove-first.txt";
        const string secondPath = "/config/partial-remove-second.txt";
        const string manifestPath = "/state/partial-remove-manifest.json";
        var manager = new ConfigurationManager(fileSystem, manifestPath);
        ConfigurationChangePlan createPlan = CreateGenericFilePlan(
            ConfigurationChangeOperation.Create,
            firstPath,
            "first-owned"
        ) with
        {
            Changes =
            [
                CreateGenericFileChange(
                    ConfigurationChangeOperation.Create,
                    firstPath,
                    "first-owned"
                ),
                CreateGenericFileChange(
                    ConfigurationChangeOperation.Create,
                    secondPath,
                    "second-owned"
                ),
            ],
        };
        await manager.ApplyAsync(createPlan, TestContext.Current.CancellationToken);
        string manifestBefore = fileSystem.ReadAllText(manifestPath);
        ConfigurationChangePlan removePlanBase = CreateGenericFilePlan(
            ConfigurationChangeOperation.Remove,
            firstPath,
            value: null,
            previousOwnedEntryMetadata: HashMetadata("first-owned"),
            previousManifestHash: HashMetadata(manifestBefore)
        );
        ConfigurationChangePlan removePlan = removePlanBase with
        {
            PlanId = "plan-generic-file-remove-current",
            ChangeSetId = "changeset-generic-file-remove-current",
            Manifest = removePlanBase.Manifest with
            {
                ProductVersion = "0.0.0-remove",
                SafeMetadata = new Dictionary<string, string>
                {
                    ["remove-metadata"] = "current",
                },
            },
        };

        ConfigurationPlanResult dryRun = await manager.DryRunAsync(
            removePlan,
            TestContext.Current.CancellationToken
        );
        Assert.Equal("first-owned", fileSystem.ReadAllText(firstPath));
        Assert.Equal(manifestBefore, fileSystem.ReadAllText(manifestPath));
        ConfigurationPlanResult result = await manager.RemoveAsync(
            removePlan,
            TestContext.Current.CancellationToken
        );

        Assert.Equal(ConfigurationPlanState.Applied, result.State);
        Assert.False(fileSystem.FileExists(firstPath));
        Assert.Equal("second-owned", fileSystem.ReadAllText(secondPath));
        Assert.True(fileSystem.FileExists(manifestPath));
        Assert.True(fileSystem.DirectoryExists("/config"));
        ConfigurationOwnershipManifest remainingManifest =
            ConfigurationOwnershipManifestSerializer.Deserialize(
                fileSystem.ReadAllText(manifestPath)
            );
        Assert.Equal(
            ConfigurationOwnershipManifestSerializer.Serialize(result.OwnershipManifest!),
            ConfigurationOwnershipManifestSerializer.Serialize(dryRun.OwnershipManifest!)
        );
        Assert.Equal(
            ConfigurationOwnershipManifestSerializer.Serialize(result.OwnershipManifest!),
            ConfigurationOwnershipManifestSerializer.Serialize(remainingManifest)
        );
        Assert.Equal(removePlan.PlanId, remainingManifest.PlanId);
        Assert.Equal(removePlan.ChangeSetId, remainingManifest.ChangeSetId);
        Assert.Equal("0.0.0-remove", remainingManifest.ProductVersion);
        Assert.Equal(
            removePlan.Manifest.PreviousOwnedEntryHash,
            remainingManifest.PreviousOwnedEntryHash
        );
        Assert.Equal("current", remainingManifest.SafeMetadata["remove-metadata"]);
        ConfigurationOwnershipManifestEntry remainingEntry = Assert.Single(
            remainingManifest.Entries
        );
        Assert.Equal(1, remainingEntry.Sequence);
        Assert.Equal(secondPath, remainingEntry.TargetPathOrName);
        Assert.Equal(ConfigurationChangeOperation.Create, remainingEntry.Operation);
        Assert.DoesNotContain(
            firstPath,
            fileSystem.ReadAllText(manifestPath),
            StringComparison.Ordinal
        );
    }

    [Fact]
    public async Task RemoveRejectsExistingManifestWithMultipleEntriesForSameCiTemporaryFile()
    {
        var fileSystem = new InMemoryFileSystem(InMemoryPathSemantics.Posix);
        const string targetPath = "/config/duplicate-existing-remove.txt";
        const string manifestPath = "/state/duplicate-existing-remove-manifest.json";
        const string before = "owned-before-remove";
        fileSystem.AtomicWriteAllText(targetPath, before);
        var manager = new ConfigurationManager(fileSystem, manifestPath);
        string duplicateManifestJson = await CreateDuplicateCiTemporaryFileManifestJsonAsync(
            targetPath,
            before
        );
        fileSystem.AtomicWriteAllText(manifestPath, duplicateManifestJson);
        ConfigurationChangePlan removePlan = CreateGenericFilePlan(
            ConfigurationChangeOperation.Remove,
            targetPath,
            value: null,
            previousOwnedEntryMetadata: HashMetadata(before),
            previousManifestHash: HashMetadata(duplicateManifestJson)
        );

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(async () =>
            await manager.RemoveAsync(removePlan, TestContext.Current.CancellationToken)
        );

        Assert.Contains("whole-file ownership", exception.Message, StringComparison.Ordinal);
        Assert.Equal(before, fileSystem.ReadAllText(targetPath));
        Assert.Equal(duplicateManifestJson, fileSystem.ReadAllText(manifestPath));
    }

    [Fact]
    public async Task RemoveRejectsExistingManifestWithSamePathDifferentTargetKindBeforeMutation()
    {
        var fileSystem = new InMemoryFileSystem(InMemoryPathSemantics.Posix);
        const string targetPath = "/config/same-path-different-kind-remove.txt";
        const string manifestPath = "/state/same-path-different-kind-remove-manifest.json";
        const string before = "owned-before-remove";
        fileSystem.AtomicWriteAllText(targetPath, before);
        var manager = new ConfigurationManager(fileSystem, manifestPath);
        string conflictingManifestJson =
            await CreateSamePathDifferentTargetKindManifestJsonAsync(targetPath, before);
        fileSystem.AtomicWriteAllText(manifestPath, conflictingManifestJson);
        ConfigurationChangePlan removePlan = CreateGenericFilePlan(
            ConfigurationChangeOperation.Remove,
            targetPath,
            value: null,
            previousOwnedEntryMetadata: HashMetadata(before),
            previousManifestHash: HashMetadata(conflictingManifestJson)
        );

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(async () =>
            await manager.RemoveAsync(removePlan, TestContext.Current.CancellationToken)
        );

        Assert.Contains("same physical target path", exception.Message, StringComparison.Ordinal);
        Assert.Equal(before, fileSystem.ReadAllText(targetPath));
        Assert.Equal(conflictingManifestJson, fileSystem.ReadAllText(manifestPath));
    }

    [Fact]
    public async Task RemoveRejectsExistingManifestParentChildConflictBeforeDeleting()
    {
        var fileSystem = new InMemoryFileSystem(InMemoryPathSemantics.Posix);
        const string parentTargetPath = "/config/existing-remove-conflict/parent.txt";
        const string childTargetPath = "/config/existing-remove-conflict/parent.txt/child.txt";
        const string manifestPath = "/state/existing-remove-conflict-manifest.json";
        const string before = "parent-value";
        fileSystem.AtomicWriteAllText(parentTargetPath, before);
        string manifestJson = await CreateParentChildConflictManifestJsonAsync(
            parentTargetPath,
            childTargetPath
        );
        fileSystem.AtomicWriteAllText(manifestPath, manifestJson);
        var manager = new ConfigurationManager(fileSystem, manifestPath);
        ConfigurationChangePlan removePlan = CreateGenericFilePlan(
            ConfigurationChangeOperation.Remove,
            parentTargetPath,
            value: null,
            previousOwnedEntryMetadata: HashMetadata(before),
            previousManifestHash: HashMetadata(manifestJson)
        );

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(async () =>
            await manager.RemoveAsync(removePlan, TestContext.Current.CancellationToken)
        );

        Assert.Contains("parent paths", exception.Message, StringComparison.Ordinal);
        Assert.Equal(before, fileSystem.ReadAllText(parentTargetPath));
        Assert.Equal(manifestJson, fileSystem.ReadAllText(manifestPath));
        Assert.DoesNotContain(
            fileSystem.Calls,
            call =>
                string.Equals(
                    call.Operation,
                    nameof(IFileSystem.DeleteFile),
                    StringComparison.Ordinal
                ) && string.Equals(call.Path, parentTargetPath, StringComparison.Ordinal)
        );
    }

    [Fact]
    public async Task PartialRemoveRollsBackTargetWhenRemainingManifestUpdateFails()
    {
        var fileSystem = new FailOnAtomicWriteFileSystem(InMemoryPathSemantics.Posix);
        const string firstPath = "/config/partial-remove-rollback-first.txt";
        const string secondPath = "/config/partial-remove-rollback-second.txt";
        const string manifestPath = "/state/partial-remove-rollback-manifest.json";
        var manager = new ConfigurationManager(fileSystem, manifestPath);
        ConfigurationChangePlan createPlan = CreateGenericFilePlan(
            ConfigurationChangeOperation.Create,
            firstPath,
            "first-owned"
        ) with
        {
            Changes =
            [
                CreateGenericFileChange(
                    ConfigurationChangeOperation.Create,
                    firstPath,
                    "first-owned"
                ),
                CreateGenericFileChange(
                    ConfigurationChangeOperation.Create,
                    secondPath,
                    "second-owned"
                ),
            ],
        };
        await manager.ApplyAsync(createPlan, TestContext.Current.CancellationToken);
        string manifestBefore = fileSystem.ReadAllText(manifestPath);
        fileSystem.ResetAtomicWriteCount();
        fileSystem.FailOnAtomicWriteNumber = 1;
        ConfigurationChangePlan removePlan = CreateGenericFilePlan(
            ConfigurationChangeOperation.Remove,
            firstPath,
            value: null,
            previousOwnedEntryMetadata: HashMetadata("first-owned"),
            previousManifestHash: HashMetadata(manifestBefore)
        );

        await Assert.ThrowsAnyAsync<IOException>(async () =>
            await manager.RemoveAsync(removePlan, TestContext.Current.CancellationToken)
        );

        Assert.Equal("first-owned", fileSystem.ReadAllText(firstPath));
        Assert.Equal("second-owned", fileSystem.ReadAllText(secondPath));
        Assert.Equal(manifestBefore, fileSystem.ReadAllText(manifestPath));
    }

    [Fact]
    public async Task PublicApplyRemoveRemainDeferredWithoutPublicFilesystemBackedSupport()
    {
        var manager = new ConfigurationManager();
        ConfigurationChangePlan plan = CreateGenericFilePlan(
            ConfigurationChangeOperation.Create,
            "/config/public-deferred.txt",
            "value"
        );

        await Assert.ThrowsAsync<InvalidOperationException>(async () =>
            await manager.ApplyAsync(plan, TestContext.Current.CancellationToken)
        );
        await Assert.ThrowsAsync<InvalidOperationException>(async () =>
            await manager.RemoveAsync(
                CreateGenericFilePlan(
                    ConfigurationChangeOperation.Remove,
                    "/config/public-deferred.txt",
                    value: null,
                    previousOwnedEntryMetadata: HashMetadata("value"),
                    previousManifestHash: HashMetadata("manifest")
                ),
                TestContext.Current.CancellationToken
            )
        );

    }

    [Fact]
    public async Task RemoveRejectsValueWritingChangesBeforeDurableWrites()
    {
        var fileSystem = new InMemoryFileSystem(InMemoryPathSemantics.Posix);
        const string targetPath = "/config/remove-rejects-value-writes.txt";
        const string manifestPath = "/state/remove-rejects-value-writes-manifest.json";
        var manager = new ConfigurationManager(fileSystem, manifestPath);
        await manager.ApplyAsync(
            CreateGenericFilePlan(ConfigurationChangeOperation.Create, targetPath, "owned-value"),
            TestContext.Current.CancellationToken
        );
        string manifestBefore = fileSystem.ReadAllText(manifestPath);
        ConfigurationChangePlan valueWritingRemovePlan = CreateGenericFilePlan(
            ConfigurationChangeOperation.Update,
            targetPath,
            "new-value",
            HashMetadata("owned-value"),
            previousManifestHash: HashMetadata(manifestBefore)
        );

        var exception = await Assert.ThrowsAsync<NotSupportedException>(async () =>
            await manager.RemoveAsync(valueWritingRemovePlan, TestContext.Current.CancellationToken)
        );

        Assert.Contains("value-writing", exception.Message, StringComparison.Ordinal);
        Assert.Equal("owned-value", fileSystem.ReadAllText(targetPath));
        Assert.Equal(manifestBefore, fileSystem.ReadAllText(manifestPath));
    }

    [Fact]
    public async Task ApplyCreateConflictsWhenTargetAlreadyExistsEvenWithMatchingMetadata()
    {
        var fileSystem = new InMemoryFileSystem(InMemoryPathSemantics.Posix);
        const string targetPath = "/config/create-existing.txt";
        const string manifestPath = "/state/create-existing-manifest.json";
        fileSystem.AtomicWriteAllText(targetPath, "existing-value");
        var manager = new ConfigurationManager(fileSystem, manifestPath);
        ConfigurationChangePlan plan = CreateGenericFilePlan(
            ConfigurationChangeOperation.Create,
            targetPath,
            "new-value",
            HashMetadata("existing-value")
        );

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(async () =>
            await manager.ApplyAsync(plan, TestContext.Current.CancellationToken)
        );

        Assert.Contains(
            "create target already exists",
            exception.Message,
            StringComparison.Ordinal
        );
        Assert.Equal("existing-value", fileSystem.ReadAllText(targetPath));
        Assert.False(fileSystem.FileExists(manifestPath));
    }

    [Fact]
    public async Task RemoveRequiresExistingOwnershipManifestBeforeDeletingTarget()
    {
        var fileSystem = new InMemoryFileSystem(InMemoryPathSemantics.Posix);
        const string targetPath = "/config/missing-manifest-remove.txt";
        const string manifestPath = "/state/missing-manifest-remove-manifest.json";
        fileSystem.AtomicWriteAllText(targetPath, "owned-value");
        var manager = new ConfigurationManager(fileSystem, manifestPath);
        ConfigurationChangePlan removePlan = CreateGenericFilePlan(
            ConfigurationChangeOperation.Remove,
            targetPath,
            value: null,
            previousOwnedEntryMetadata: HashMetadata("owned-value")
        );

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(async () =>
            await manager.RemoveAsync(removePlan, TestContext.Current.CancellationToken)
        );

        Assert.Contains("ownership manifest", exception.Message, StringComparison.Ordinal);
        Assert.Equal("owned-value", fileSystem.ReadAllText(targetPath));
        Assert.False(fileSystem.FileExists(manifestPath));
    }

    [Fact]
    public async Task ApplyFailsClosedOnConflictingBeforeStateBeforeDurableWrites()
    {
        var fileSystem = new InMemoryFileSystem(InMemoryPathSemantics.Posix);
        const string targetPath = "/config/conflict.txt";
        const string manifestPath = "/state/conflict-manifest.json";
        fileSystem.AtomicWriteAllText(targetPath, "unexpected");
        var manager = new ConfigurationManager(fileSystem, manifestPath);
        ConfigurationChangePlan plan = CreateGenericFilePlan(
            ConfigurationChangeOperation.Update,
            targetPath,
            "after",
            HashMetadata("expected")
        );
        fileSystem.Calls.Clear();

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(async () =>
            await manager.ApplyAsync(plan, TestContext.Current.CancellationToken)
        );

        Assert.Contains("ownership manifest", exception.Message, StringComparison.Ordinal);
        AssertNoTargetSnapshotReads(fileSystem, targetPath);
        Assert.Equal("unexpected", fileSystem.ReadAllText(targetPath));
        Assert.False(fileSystem.FileExists(manifestPath));
    }

    [Fact]
    public async Task ApplyFailsClosedOnStaleManifestBeforeDurableWrites()
    {
        var fileSystem = new InMemoryFileSystem(InMemoryPathSemantics.Posix);
        const string targetPath = "/config/stale.txt";
        const string manifestPath = "/state/stale-manifest.json";
        fileSystem.AtomicWriteAllText(targetPath, "before");
        var manager = new ConfigurationManager(fileSystem, manifestPath);
        await manager.ApplyAsync(
            CreateGenericFilePlan(
                ConfigurationChangeOperation.Set,
                targetPath,
                "first",
                HashMetadata("before")
            ),
            TestContext.Current.CancellationToken
        );
        ConfigurationChangePlan stalePlan = CreateGenericFilePlan(
            ConfigurationChangeOperation.Update,
            targetPath,
            "second",
            HashMetadata("first"),
            previousManifestHash: HashMetadata("not the current manifest")
        );

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(async () =>
            await manager.ApplyAsync(stalePlan, TestContext.Current.CancellationToken)
        );

        Assert.Contains("before-state hash", exception.Message, StringComparison.Ordinal);
        Assert.Equal("first", fileSystem.ReadAllText(targetPath));
    }

    [Fact]
    public async Task FilesystemBackedApplyRejectsExplicitPathGenericTargetsBeforeMutation()
    {
        var fileSystem = new InMemoryFileSystem(InMemoryPathSemantics.Posix);
        const string targetPath = "/config/explicit-path.txt";
        const string manifestPath = "/state/explicit-path-manifest.json";
        var manager = new ConfigurationManager(fileSystem, manifestPath);
        ConfigurationChangePlan plan = ConfigurationChangePlanPolicy.Create(
            "plan-explicit-path-generic-file",
            "changeset-explicit-path-generic-file",
            "azureauth-credprovider",
            ConfigurationScope.ExplicitPath,
            new ConfigurationManifestMetadata
            {
                ManifestId = "manifest-explicit-path-generic-file",
                OwnerProductId = "azureauth-credprovider",
                EntrySelector = "generic.file",
                ProductVersion = "0.0.0-test",
            },
            [
                CreateGenericFileChange(
                    ConfigurationChangeOperation.Create,
                    targetPath,
                    "new-value"
                ),
            ]
        );

        var exception = await Assert.ThrowsAsync<NotSupportedException>(async () =>
            await manager.ApplyAsync(plan, TestContext.Current.CancellationToken)
        );

        Assert.Contains("CI temporary", exception.Message, StringComparison.Ordinal);
        Assert.False(fileSystem.FileExists(targetPath));
        Assert.False(fileSystem.FileExists(manifestPath));
    }

    [Fact]
    public async Task FilesystemBackedDryRunRejectsDirectoryTarget()
    {
        var fileSystem = new InMemoryFileSystem(InMemoryPathSemantics.Posix);
        const string targetPath = "/config/directory-target";
        const string manifestPath = "/state/directory-target-dry-run-manifest.json";
        fileSystem.CreateDirectory(targetPath);
        var manager = new ConfigurationManager(fileSystem, manifestPath);
        ConfigurationChangePlan plan = CreateGenericFilePlan(
            ConfigurationChangeOperation.Create,
            targetPath,
            "owned-value"
        );

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(async () =>
            await manager.DryRunAsync(plan, TestContext.Current.CancellationToken)
        );

        Assert.Contains("directory", exception.Message, StringComparison.OrdinalIgnoreCase);
        Assert.True(fileSystem.DirectoryExists(targetPath));
        Assert.False(fileSystem.FileExists(manifestPath));
    }

    [Fact]
    public async Task FilesystemBackedDryRunRejectsRegularFileParent()
    {
        var fileSystem = new InMemoryFileSystem(InMemoryPathSemantics.Posix);
        const string parentPath = "/config/regular-file-parent";
        const string targetPath = "/config/regular-file-parent/owned.txt";
        const string manifestPath = "/state/regular-file-parent-dry-run-manifest.json";
        fileSystem.AtomicWriteAllText(parentPath, "parent-file");
        var manager = new ConfigurationManager(fileSystem, manifestPath);
        ConfigurationChangePlan plan = CreateGenericFilePlan(
            ConfigurationChangeOperation.Create,
            targetPath,
            "owned-value"
        );

        var exception = await Assert.ThrowsAsync<NotSupportedException>(async () =>
            await manager.DryRunAsync(plan, TestContext.Current.CancellationToken)
        );

        Assert.Contains("non-directory", exception.Message, StringComparison.Ordinal);
        Assert.Equal("parent-file", fileSystem.ReadAllText(parentPath));
        Assert.False(fileSystem.FileExists(targetPath));
        Assert.False(fileSystem.FileExists(manifestPath));
    }

    [Fact]
    public async Task FilesystemBackedDryRunRejectsRegularFileAncestorComponent()
    {
        var fileSystem = new InMemoryFileSystem(InMemoryPathSemantics.Posix);
        const string ancestorPath = "/config/regular-file-ancestor";
        const string targetPath = "/config/regular-file-ancestor/nested/owned.txt";
        const string manifestPath = "/state/regular-file-ancestor-dry-run-manifest.json";
        fileSystem.AtomicWriteAllText(ancestorPath, "ancestor-file");
        var manager = new ConfigurationManager(fileSystem, manifestPath);
        ConfigurationChangePlan plan = CreateGenericFilePlan(
            ConfigurationChangeOperation.Create,
            targetPath,
            "owned-value"
        );

        var exception = await Assert.ThrowsAsync<NotSupportedException>(async () =>
            await manager.DryRunAsync(plan, TestContext.Current.CancellationToken)
        );

        Assert.Contains("non-directory", exception.Message, StringComparison.Ordinal);
        Assert.Equal("ancestor-file", fileSystem.ReadAllText(ancestorPath));
        Assert.False(fileSystem.FileExists(targetPath));
        Assert.False(fileSystem.FileExists(manifestPath));
    }

    [Fact]
    public async Task FilesystemBackedApplyRejectsDirectoryTargetBeforeMutation()
    {
        var fileSystem = new InMemoryFileSystem(InMemoryPathSemantics.Posix);
        const string firstTargetPath = "/config/directory-target-first.txt";
        const string directoryTargetPath = "/config/directory-target";
        const string manifestPath = "/state/directory-target-apply-manifest.json";
        fileSystem.CreateDirectory(directoryTargetPath);
        var manager = new ConfigurationManager(fileSystem, manifestPath);
        ConfigurationChangePlan plan = CreateGenericFilePlan(
            ConfigurationChangeOperation.Create,
            firstTargetPath,
            "first-owned-value"
        ) with
        {
            Changes =
            [
                CreateGenericFileChange(
                    ConfigurationChangeOperation.Create,
                    firstTargetPath,
                    "first-owned-value"
                ),
                CreateGenericFileChange(
                    ConfigurationChangeOperation.Create,
                    directoryTargetPath,
                    "directory-owned-value"
                ) with
                {
                    Key = "other-file",
                },
            ],
        };

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(async () =>
            await manager.ApplyAsync(plan, TestContext.Current.CancellationToken)
        );

        Assert.Contains("directory", exception.Message, StringComparison.OrdinalIgnoreCase);
        Assert.False(fileSystem.FileExists(firstTargetPath));
        Assert.True(fileSystem.DirectoryExists(directoryTargetPath));
        Assert.False(fileSystem.FileExists(manifestPath));
        Assert.DoesNotContain(
            fileSystem.Calls,
            call =>
                string.Equals(
                    call.Operation,
                    nameof(IFileSystem.AtomicWriteAllText),
                    StringComparison.Ordinal
                )
                && string.Equals(call.Path, firstTargetPath, StringComparison.Ordinal)
        );
    }

    [Fact]
    public async Task FilesystemBackedApplyRejectsRegularFileParentBeforeMutation()
    {
        var fileSystem = new InMemoryFileSystem(InMemoryPathSemantics.Posix);
        const string parentPath = "/config/regular-file-parent";
        const string targetPath = "/config/regular-file-parent/owned.txt";
        const string manifestPath = "/state/regular-file-parent-apply-manifest.json";
        fileSystem.AtomicWriteAllText(parentPath, "parent-file");
        var manager = new ConfigurationManager(fileSystem, manifestPath);
        ConfigurationChangePlan plan = CreateGenericFilePlan(
            ConfigurationChangeOperation.Create,
            targetPath,
            "owned-value"
        );

        var exception = await Assert.ThrowsAsync<NotSupportedException>(async () =>
            await manager.ApplyAsync(plan, TestContext.Current.CancellationToken)
        );

        Assert.Contains("non-directory", exception.Message, StringComparison.Ordinal);
        Assert.Equal("parent-file", fileSystem.ReadAllText(parentPath));
        Assert.False(fileSystem.FileExists(targetPath));
        Assert.False(fileSystem.FileExists(manifestPath));
        Assert.DoesNotContain(
            fileSystem.Calls,
            call =>
                string.Equals(
                    call.Operation,
                    nameof(IFileSystem.AtomicWriteAllText),
                    StringComparison.Ordinal
                )
                && string.Equals(call.Path, targetPath, StringComparison.Ordinal)
        );
    }

    [Fact]
    public async Task FilesystemBackedApplyRejectsRegularFileAncestorComponentBeforeMutation()
    {
        var fileSystem = new InMemoryFileSystem(InMemoryPathSemantics.Posix);
        const string ancestorPath = "/config/regular-file-ancestor";
        const string targetPath = "/config/regular-file-ancestor/nested/owned.txt";
        const string manifestPath = "/state/regular-file-ancestor-apply-manifest.json";
        fileSystem.AtomicWriteAllText(ancestorPath, "ancestor-file");
        var manager = new ConfigurationManager(fileSystem, manifestPath);
        ConfigurationChangePlan plan = CreateGenericFilePlan(
            ConfigurationChangeOperation.Create,
            targetPath,
            "owned-value"
        );

        var exception = await Assert.ThrowsAsync<NotSupportedException>(async () =>
            await manager.ApplyAsync(plan, TestContext.Current.CancellationToken)
        );

        Assert.Contains("non-directory", exception.Message, StringComparison.Ordinal);
        Assert.Equal("ancestor-file", fileSystem.ReadAllText(ancestorPath));
        Assert.False(fileSystem.FileExists(targetPath));
        Assert.False(fileSystem.FileExists(manifestPath));
        Assert.DoesNotContain(
            fileSystem.Calls,
            call =>
                string.Equals(
                    call.Operation,
                    nameof(IFileSystem.AtomicWriteAllText),
                    StringComparison.Ordinal
                )
                && string.Equals(call.Path, targetPath, StringComparison.Ordinal)
        );
    }

    [Fact]
    public async Task FilesystemBackedRemoveRejectsRegularFileParentBeforeDeleting()
    {
        var fileSystem = new InMemoryFileSystem(InMemoryPathSemantics.Posix);
        const string parentPath = "/config/regular-file-parent";
        const string targetPath = "/config/regular-file-parent/owned.txt";
        const string manifestPath = "/state/regular-file-parent-remove-manifest.json";
        fileSystem.AtomicWriteAllText(parentPath, "parent-file");
        var planningOnlyManager = new ConfigurationManager();
        ConfigurationChangePlan manifestlessRemovePlan = CreateGenericFilePlan(
            ConfigurationChangeOperation.Remove,
            targetPath,
            value: null,
            previousOwnedEntryMetadata: HashMetadata("owned-value")
        );
        ConfigurationPlanResult dryRun = await planningOnlyManager.DryRunAsync(
            manifestlessRemovePlan,
            TestContext.Current.CancellationToken
        );
        string manifestJson = ConfigurationOwnershipManifestSerializer.Serialize(
            dryRun.OwnershipManifest!
        );
        fileSystem.AtomicWriteAllText(manifestPath, manifestJson);
        var manager = new ConfigurationManager(fileSystem, manifestPath);
        ConfigurationChangePlan removePlan = CreateGenericFilePlan(
            ConfigurationChangeOperation.Remove,
            targetPath,
            value: null,
            previousOwnedEntryMetadata: HashMetadata("owned-value"),
            previousManifestHash: HashMetadata(manifestJson)
        );

        var exception = await Assert.ThrowsAsync<NotSupportedException>(async () =>
            await manager.RemoveAsync(removePlan, TestContext.Current.CancellationToken)
        );

        Assert.Contains("non-directory", exception.Message, StringComparison.Ordinal);
        Assert.Equal("parent-file", fileSystem.ReadAllText(parentPath));
        Assert.Equal(manifestJson, fileSystem.ReadAllText(manifestPath));
        Assert.DoesNotContain(
            fileSystem.Calls,
            call =>
                string.Equals(
                    call.Operation,
                    nameof(IFileSystem.DeleteFile),
                    StringComparison.Ordinal
                ) && string.Equals(call.Path, targetPath, StringComparison.Ordinal)
        );
    }

    [Fact]
    public async Task FilesystemBackedRemoveRejectsRegularFileAncestorComponentBeforeDeleting()
    {
        var fileSystem = new InMemoryFileSystem(InMemoryPathSemantics.Posix);
        const string ancestorPath = "/config/regular-file-ancestor";
        const string targetPath = "/config/regular-file-ancestor/nested/owned.txt";
        const string manifestPath = "/state/regular-file-ancestor-remove-manifest.json";
        fileSystem.AtomicWriteAllText(ancestorPath, "ancestor-file");
        var planningOnlyManager = new ConfigurationManager();
        ConfigurationChangePlan manifestlessRemovePlan = CreateGenericFilePlan(
            ConfigurationChangeOperation.Remove,
            targetPath,
            value: null,
            previousOwnedEntryMetadata: HashMetadata("owned-value")
        );
        ConfigurationPlanResult dryRun = await planningOnlyManager.DryRunAsync(
            manifestlessRemovePlan,
            TestContext.Current.CancellationToken
        );
        string manifestJson = ConfigurationOwnershipManifestSerializer.Serialize(
            dryRun.OwnershipManifest!
        );
        fileSystem.AtomicWriteAllText(manifestPath, manifestJson);
        var manager = new ConfigurationManager(fileSystem, manifestPath);
        ConfigurationChangePlan removePlan = CreateGenericFilePlan(
            ConfigurationChangeOperation.Remove,
            targetPath,
            value: null,
            previousOwnedEntryMetadata: HashMetadata("owned-value"),
            previousManifestHash: HashMetadata(manifestJson)
        );

        var exception = await Assert.ThrowsAsync<NotSupportedException>(async () =>
            await manager.RemoveAsync(removePlan, TestContext.Current.CancellationToken)
        );

        Assert.Contains("non-directory", exception.Message, StringComparison.Ordinal);
        Assert.Equal("ancestor-file", fileSystem.ReadAllText(ancestorPath));
        Assert.Equal(manifestJson, fileSystem.ReadAllText(manifestPath));
        Assert.DoesNotContain(
            fileSystem.Calls,
            call =>
                string.Equals(
                    call.Operation,
                    nameof(IFileSystem.DeleteFile),
                    StringComparison.Ordinal
                ) && string.Equals(call.Path, targetPath, StringComparison.Ordinal)
        );
    }

    [Theory]
    [InlineData(nameof(IConfigurationManager.DryRunAsync))]
    [InlineData(nameof(IConfigurationManager.ApplyAsync))]
    public async Task FilesystemBackedExecutionRejectsParentChildTargetConflictsBeforeMutation(
        string methodName
    )
    {
        var fileSystem = new InMemoryFileSystem(InMemoryPathSemantics.Posix);
        const string parentTargetPath = "/config/parent-child-conflict/parent.txt";
        const string childTargetPath = "/config/parent-child-conflict/parent.txt/child.txt";
        const string manifestPath = "/state/parent-child-conflict-manifest.json";
        var manager = new ConfigurationManager(fileSystem, manifestPath);
        ConfigurationChangePlan plan = CreateGenericFilePlan(
            ConfigurationChangeOperation.Create,
            parentTargetPath,
            "parent-value"
        ) with
        {
            Changes =
            [
                CreateGenericFileChange(
                    ConfigurationChangeOperation.Create,
                    parentTargetPath,
                    "parent-value"
                ),
                CreateGenericFileChange(
                    ConfigurationChangeOperation.Create,
                    childTargetPath,
                    "child-value"
                ) with
                {
                    Key = "child-file",
                },
            ],
        };

        Exception exception =
            methodName == nameof(IConfigurationManager.DryRunAsync)
                ? await Assert.ThrowsAsync<ArgumentException>(async () =>
                    await CreateExecutionCall(manager, methodName, plan)()
                )
                : await Assert.ThrowsAsync<NotSupportedException>(async () =>
                    await CreateExecutionCall(manager, methodName, plan)()
                );

        Assert.Contains("parent paths", exception.Message, StringComparison.Ordinal);
        Assert.False(fileSystem.FileExists(parentTargetPath));
        Assert.False(fileSystem.FileExists(childTargetPath));
        Assert.False(fileSystem.FileExists(manifestPath));
        Assert.DoesNotContain(
            fileSystem.Calls,
            call =>
                string.Equals(
                    call.Operation,
                    nameof(IFileSystem.AtomicWriteAllText),
                    StringComparison.Ordinal
                )
                && (
                    string.Equals(call.Path, parentTargetPath, StringComparison.Ordinal)
                    || string.Equals(call.Path, childTargetPath, StringComparison.Ordinal)
                )
        );
    }

    [Fact]
    public async Task FilesystemBackedRemoveRejectsParentChildTargetConflictsBeforeDeleting()
    {
        var fileSystem = new InMemoryFileSystem(InMemoryPathSemantics.Posix);
        const string parentTargetPath = "/config/remove-parent-child-conflict/parent.txt";
        const string childTargetPath = "/config/remove-parent-child-conflict/parent.txt/child.txt";
        const string manifestPath = "/state/remove-parent-child-conflict-manifest.json";
        string manifestJson = await CreateParentChildConflictManifestJsonAsync(
            parentTargetPath,
            childTargetPath
        );
        fileSystem.AtomicWriteAllText(manifestPath, manifestJson);
        var manager = new ConfigurationManager(fileSystem, manifestPath);
        ConfigurationChangePlan plan = CreateGenericFilePlan(
            ConfigurationChangeOperation.Remove,
            parentTargetPath,
            value: null,
            previousOwnedEntryMetadata: HashMetadata("parent-value"),
            previousManifestHash: HashMetadata(manifestJson)
        ) with
        {
            Changes =
            [
                CreateGenericFileChange(
                    ConfigurationChangeOperation.Remove,
                    parentTargetPath,
                    value: null,
                    previousOwnedEntryMetadata: HashMetadata("parent-value")
                ),
                CreateGenericFileChange(
                    ConfigurationChangeOperation.Remove,
                    childTargetPath,
                    value: null,
                    previousOwnedEntryMetadata: HashMetadata("child-value")
                ) with
                {
                    Key = "child-file",
                },
            ],
        };

        var exception = await Assert.ThrowsAsync<NotSupportedException>(async () =>
            await manager.RemoveAsync(plan, TestContext.Current.CancellationToken)
        );

        Assert.Contains("parent paths", exception.Message, StringComparison.Ordinal);
        Assert.Equal(manifestJson, fileSystem.ReadAllText(manifestPath));
        Assert.DoesNotContain(
            fileSystem.Calls,
            call =>
                string.Equals(
                    call.Operation,
                    nameof(IFileSystem.DeleteFile),
                    StringComparison.Ordinal
                )
                && (
                    string.Equals(call.Path, parentTargetPath, StringComparison.Ordinal)
                    || string.Equals(call.Path, childTargetPath, StringComparison.Ordinal)
                )
        );
    }

    [Fact(
        Skip = "Windows case-insensitive filesystem semantics required.",
        SkipUnless = nameof(IsWindows)
    )]
    public async Task FilesystemBackedExecutionRejectsWindowsCaseVariantParentChildTargetConflicts()
    {
        var fileSystem = new InMemoryFileSystem(InMemoryPathSemantics.Host);
        const string parentTargetPath = @"C:\config\parent-child-conflict\parent.txt";
        const string childTargetPath = @"C:\CONFIG\PARENT-CHILD-CONFLICT\PARENT.TXT\child.txt";
        const string manifestPath = @"C:\state\parent-child-conflict-manifest.json";
        var manager = new ConfigurationManager(fileSystem, manifestPath);
        ConfigurationChangePlan plan = CreateGenericFilePlan(
            ConfigurationChangeOperation.Create,
            parentTargetPath,
            "parent-value"
        ) with
        {
            Changes =
            [
                CreateGenericFileChange(
                    ConfigurationChangeOperation.Create,
                    parentTargetPath,
                    "parent-value"
                ),
                CreateGenericFileChange(
                    ConfigurationChangeOperation.Create,
                    childTargetPath,
                    "child-value"
                ) with
                {
                    Key = "child-file",
                },
            ],
        };

        var exception = await Assert.ThrowsAsync<ArgumentException>(async () =>
            await manager.DryRunAsync(plan, TestContext.Current.CancellationToken)
        );

        Assert.Contains("parent", exception.Message, StringComparison.OrdinalIgnoreCase);
        Assert.False(fileSystem.FileExists(parentTargetPath));
        Assert.False(fileSystem.FileExists(childTargetPath));
        Assert.False(fileSystem.FileExists(manifestPath));
    }

    [Theory]
    [InlineData(nameof(IConfigurationManager.DryRunAsync))]
    [InlineData(nameof(IConfigurationManager.ApplyAsync))]
    public async Task FilesystemBackedExecutionRejectsNonSymbolicReparsePointParent(
        string methodName
    )
    {
        var fileSystem = new InMemoryFileSystem(InMemoryPathSemantics.Posix);
        const string containerPath = "/config/reparse-container";
        const string targetPath = "/config/reparse-container/owned.txt";
        const string manifestPath = "/state/reparse-parent-manifest.json";
        fileSystem.CreateDirectory(containerPath);
        fileSystem.MarkAsNonSymbolicReparsePoint(containerPath);
        var manager = new ConfigurationManager(fileSystem, manifestPath);
        ConfigurationChangePlan plan = CreateGenericFilePlan(
            ConfigurationChangeOperation.Create,
            targetPath,
            "owned-value"
        );

        var exception = await Assert.ThrowsAsync<NotSupportedException>(async () =>
            await CreateExecutionCall(manager, methodName, plan)()
        );

        Assert.Contains("reparse-point", exception.Message, StringComparison.Ordinal);
        Assert.False(fileSystem.FileExists(targetPath));
        Assert.False(fileSystem.FileExists(manifestPath));
    }

    [Theory]
    [InlineData(nameof(IConfigurationManager.DryRunAsync))]
    [InlineData(nameof(IConfigurationManager.ApplyAsync))]
    public async Task FilesystemBackedExecutionRejectsNonSymbolicReparsePointTarget(
        string methodName
    )
    {
        var fileSystem = new InMemoryFileSystem(InMemoryPathSemantics.Posix);
        const string containerPath = "/config/reparse-target";
        const string targetPath = "/config/reparse-target/owned.txt";
        const string manifestPath = "/state/reparse-target-manifest.json";
        fileSystem.CreateDirectory(containerPath);
        fileSystem.WriteAllText(targetPath, "existing");
        fileSystem.MarkAsNonSymbolicReparsePoint(targetPath);
        var manager = new ConfigurationManager(fileSystem, manifestPath);
        ConfigurationChangePlan plan = CreateGenericFilePlan(
            ConfigurationChangeOperation.Create,
            targetPath,
            "owned-value"
        );

        var exception = await Assert.ThrowsAsync<NotSupportedException>(async () =>
            await CreateExecutionCall(manager, methodName, plan)()
        );

        Assert.Contains("reparse-point", exception.Message, StringComparison.Ordinal);
        Assert.Equal("existing", fileSystem.ReadAllText(targetPath));
        Assert.False(fileSystem.FileExists(manifestPath));
    }

    [Theory]
    [InlineData(nameof(IConfigurationManager.DryRunAsync), ConfigurationChangeOperation.Create)]
    [InlineData(nameof(IConfigurationManager.ApplyAsync), ConfigurationChangeOperation.Create)]
    [InlineData(nameof(IConfigurationManager.RemoveAsync), ConfigurationChangeOperation.Remove)]
    public async Task FilesystemBackedExecutionRejectsNonSymbolicReparsePointManifest(
        string methodName,
        ConfigurationChangeOperation operation
    )
    {
        var fileSystem = new InMemoryFileSystem(InMemoryPathSemantics.Posix);
        const string containerPath = "/config/reparse-manifest";
        const string targetPath = "/config/reparse-manifest/owned.txt";
        const string manifestPath = "/config/reparse-manifest/manifest.json";
        fileSystem.CreateDirectory(containerPath);
        fileSystem.WriteAllText(manifestPath, "{}");
        fileSystem.MarkAsNonSymbolicReparsePoint(manifestPath);
        if (operation == ConfigurationChangeOperation.Remove)
        {
            fileSystem.WriteAllText(targetPath, "owned-value");
        }

        var manager = new ConfigurationManager(fileSystem, manifestPath);
        ConfigurationChangePlan plan = CreateGenericFilePlan(
            operation,
            targetPath,
            operation == ConfigurationChangeOperation.Remove ? null : "owned-value",
            previousOwnedEntryMetadata: operation == ConfigurationChangeOperation.Remove
                ? HashMetadata("owned-value")
                : null,
            previousManifestHash: operation == ConfigurationChangeOperation.Remove
                ? HashMetadata("{}")
                : null
        );

        var exception = await Assert.ThrowsAsync<NotSupportedException>(async () =>
            await CreateExecutionCall(manager, methodName, plan)()
        );

        Assert.Contains("reparse-point", exception.Message, StringComparison.Ordinal);
        Assert.True(fileSystem.FileExists(manifestPath));
    }

    [Theory]
    [InlineData(nameof(IConfigurationManager.DryRunAsync))]
    [InlineData(nameof(IConfigurationManager.ApplyAsync))]
    [InlineData(nameof(IConfigurationManager.RemoveAsync))]
    public async Task FilesystemBackedExecutionRejectsSymbolicLinkManifestParentBeforeManifestRead(
        string methodName
    )
    {
        var fileSystem = new InMemoryFileSystem(InMemoryPathSemantics.Posix);
        const string targetPath = "/config/manifest-parent-symlink/owned.txt";
        const string outsideStatePath = "/outside-state";
        const string manifestParentPath = "/state-link";
        const string manifestPath = "/state-link/manifest.json";
        fileSystem.CreateDirectory(outsideStatePath);
        fileSystem.AtomicWriteAllText("/outside-state/manifest.json", "{}");
        fileSystem.AddSymbolicLink(manifestParentPath, outsideStatePath);
        var manager = new ConfigurationManager(fileSystem, manifestPath);
        bool remove = methodName == nameof(IConfigurationManager.RemoveAsync);
        ConfigurationChangePlan plan = CreateGenericFilePlan(
            remove ? ConfigurationChangeOperation.Remove : ConfigurationChangeOperation.Create,
            targetPath,
            remove ? null : "owned-value",
            previousOwnedEntryMetadata: remove ? HashMetadata("owned-value") : null,
            previousManifestHash: remove ? HashMetadata("{}") : null
        );
        fileSystem.Calls.Clear();

        var exception = await Assert.ThrowsAsync<NotSupportedException>(async () =>
            await CreateExecutionCall(manager, methodName, plan)()
        );

        Assert.Contains("symbolic-link", exception.Message, StringComparison.Ordinal);
        AssertNoManifestContentReads(fileSystem, manifestPath);
        Assert.False(fileSystem.FileExists(targetPath));
        Assert.Equal("{}", fileSystem.ReadAllText("/outside-state/manifest.json"));
    }

    [Theory]
    [InlineData(nameof(IConfigurationManager.DryRunAsync))]
    [InlineData(nameof(IConfigurationManager.ApplyAsync))]
    [InlineData(nameof(IConfigurationManager.RemoveAsync))]
    public async Task FilesystemBackedExecutionRejectsReparsePointManifestParentBeforeManifestRead(
        string methodName
    )
    {
        var fileSystem = new InMemoryFileSystem(InMemoryPathSemantics.Posix);
        const string targetPath = "/config/manifest-parent-reparse/owned.txt";
        const string manifestParentPath = "/state-reparse";
        const string manifestPath = "/state-reparse/manifest.json";
        fileSystem.CreateDirectory(manifestParentPath);
        fileSystem.AtomicWriteAllText(manifestPath, "{}");
        fileSystem.MarkAsNonSymbolicReparsePoint(manifestParentPath);
        var manager = new ConfigurationManager(fileSystem, manifestPath);
        bool remove = methodName == nameof(IConfigurationManager.RemoveAsync);
        ConfigurationChangePlan plan = CreateGenericFilePlan(
            remove ? ConfigurationChangeOperation.Remove : ConfigurationChangeOperation.Create,
            targetPath,
            remove ? null : "owned-value",
            previousOwnedEntryMetadata: remove ? HashMetadata("owned-value") : null,
            previousManifestHash: remove ? HashMetadata("{}") : null
        );
        fileSystem.Calls.Clear();

        var exception = await Assert.ThrowsAsync<NotSupportedException>(async () =>
            await CreateExecutionCall(manager, methodName, plan)()
        );

        Assert.Contains("reparse-point", exception.Message, StringComparison.Ordinal);
        AssertNoManifestContentReads(fileSystem, manifestPath);
        Assert.False(fileSystem.FileExists(targetPath));
        Assert.Equal("{}", fileSystem.ReadAllText(manifestPath));
    }

    [Fact(
        Skip = "Windows directory junction reparse-point semantics required.",
        SkipUnless = nameof(IsWindows)
    )]
    public async Task FilesystemBackedDryRunRejectsWindowsNonSymbolicReparsePointParent()
    {
        var fileSystem = new SystemFileSystem();
        string root = CreateSystemFileSystemTestDirectory();
        string realContainerPath = ToConfigurationPath(Path.Combine(root, "real-container"));
        string junctionContainerPath = ToConfigurationPath(
            Path.Combine(root, "junction-container")
        );
        string targetPath = ToConfigurationPath(Path.Combine(junctionContainerPath, "owned.txt"));
        string manifestPath = ToConfigurationPath(Path.Combine(root, "state", "manifest.json"));

        try
        {
            Directory.CreateDirectory(realContainerPath);
            Assert.SkipWhen(
                !TryCreateWindowsDirectoryJunction(junctionContainerPath, realContainerPath),
                "Directory junction creation unavailable."
            );
            var manager = new ConfigurationManager(fileSystem, manifestPath);
            ConfigurationChangePlan plan = CreateGenericFilePlan(
                ConfigurationChangeOperation.Create,
                targetPath,
                "owned-value"
            );

            var exception = await Assert.ThrowsAsync<NotSupportedException>(async () =>
                await manager.DryRunAsync(plan, TestContext.Current.CancellationToken)
            );

            Assert.Contains("reparse-point", exception.Message, StringComparison.Ordinal);
            Assert.False(File.Exists(targetPath));
            Assert.False(File.Exists(manifestPath));
        }
        finally
        {
            DeleteSystemFileSystemTestDirectory(root);
        }
    }

    [Fact(
        Skip = "Windows directory junction reparse-point semantics required.",
        SkipUnless = nameof(IsWindows)
    )]
    public async Task FilesystemBackedDryRunRejectsWindowsNonSymbolicReparsePointManifest()
    {
        var fileSystem = new SystemFileSystem();
        string root = CreateSystemFileSystemTestDirectory();
        string containerPath = ToConfigurationPath(Path.Combine(root, "config"));
        string targetPath = ToConfigurationPath(Path.Combine(containerPath, "owned.txt"));
        string realManifestDirectory = ToConfigurationPath(Path.Combine(root, "real-manifest"));
        string manifestPath = ToConfigurationPath(Path.Combine(root, "state", "manifest.json"));

        try
        {
            Directory.CreateDirectory(containerPath);
            Directory.CreateDirectory(realManifestDirectory);
            Assert.SkipWhen(
                !TryCreateWindowsDirectoryJunction(manifestPath, realManifestDirectory),
                "Directory junction creation unavailable."
            );
            var manager = new ConfigurationManager(fileSystem, manifestPath);
            ConfigurationChangePlan plan = CreateGenericFilePlan(
                ConfigurationChangeOperation.Create,
                targetPath,
                "owned-value"
            );

            var exception = await Assert.ThrowsAsync<NotSupportedException>(async () =>
                await manager.DryRunAsync(plan, TestContext.Current.CancellationToken)
            );

            Assert.Contains("reparse-point", exception.Message, StringComparison.Ordinal);
            Assert.True(Directory.Exists(manifestPath));
            Assert.False(File.Exists(targetPath));
        }
        finally
        {
            DeleteSystemFileSystemTestDirectory(root);
        }
    }

    [Theory]
    [InlineData(nameof(IConfigurationManager.DryRunAsync))]
    [InlineData(nameof(IConfigurationManager.ApplyAsync))]
    public async Task FilesystemBackedExecutionRejectsSymbolicLinkAncestorAboveProductOwnedRoot(
        string methodName
    )
    {
        var fileSystem = new InMemoryFileSystem(InMemoryPathSemantics.Posix);
        const string targetPath = "/config/real-container/escape.txt";
        const string escapedPath = "/outside/real-container/escape.txt";
        const string manifestPath = "/state/ancestor-symlink-manifest.json";
        fileSystem.CreateDirectory("/outside");
        fileSystem.AddSymbolicLink("/config", "/outside");
        var manager = new ConfigurationManager(fileSystem, manifestPath);
        ConfigurationChangePlan plan = CreateGenericFilePlan(
            ConfigurationChangeOperation.Create,
            targetPath,
            "owned-value"
        );

        var exception = await Assert.ThrowsAsync<NotSupportedException>(async () =>
            await CreateExecutionCall(manager, methodName, plan)()
        );

        Assert.Contains("symbolic-link", exception.Message, StringComparison.Ordinal);
        Assert.False(fileSystem.FileExists(escapedPath));
        Assert.False(fileSystem.FileExists(manifestPath));
    }

    [Fact]
    public async Task RemoveRejectsSymbolicLinkAncestorAboveProductOwnedRootBeforeDeleting()
    {
        var fileSystem = new InMemoryFileSystem(InMemoryPathSemantics.Posix);
        const string targetPath = "/config/real-container/owned.txt";
        const string escapedPath = "/outside/real-container/owned.txt";
        const string manifestPath = "/state/ancestor-symlink-remove-manifest.json";
        fileSystem.CreateDirectory("/outside/real-container");
        fileSystem.AtomicWriteAllText(escapedPath, "owned-value");
        fileSystem.AddSymbolicLink("/config", "/outside");
        var planningOnlyManager = new ConfigurationManager();
        ConfigurationChangePlan manifestlessRemovePlan = CreateGenericFilePlan(
            ConfigurationChangeOperation.Remove,
            targetPath,
            value: null,
            previousOwnedEntryMetadata: HashMetadata("owned-value")
        );
        ConfigurationPlanResult dryRun = await planningOnlyManager.DryRunAsync(
            manifestlessRemovePlan,
            TestContext.Current.CancellationToken
        );
        string manifestJson = ConfigurationOwnershipManifestSerializer.Serialize(
            dryRun.OwnershipManifest!
        );
        fileSystem.AtomicWriteAllText(manifestPath, manifestJson);
        var manager = new ConfigurationManager(fileSystem, manifestPath);
        ConfigurationChangePlan removePlan = CreateGenericFilePlan(
            ConfigurationChangeOperation.Remove,
            targetPath,
            value: null,
            previousOwnedEntryMetadata: HashMetadata("owned-value"),
            previousManifestHash: HashMetadata(manifestJson)
        );

        var exception = await Assert.ThrowsAsync<NotSupportedException>(async () =>
            await manager.RemoveAsync(removePlan, TestContext.Current.CancellationToken)
        );

        Assert.Contains("symbolic-link", exception.Message, StringComparison.Ordinal);
        Assert.Equal("owned-value", fileSystem.ReadAllText(escapedPath));
        Assert.Equal(manifestJson, fileSystem.ReadAllText(manifestPath));
    }

    [Fact]
    public async Task FilesystemBackedDryRunRejectsNonCiTemporaryPlanBeforeLifecycleLock()
    {
        var fileSystem = new InMemoryFileSystem(InMemoryPathSemantics.Posix);
        const string targetPath = "/config/non-ci-temporary-dry-run.txt";
        const string manifestPath = "/state/non-ci-temporary-dry-run-manifest.json";
        var manager = new ConfigurationManager(fileSystem, manifestPath);
        ConfigurationChangePlan plan = ConfigurationChangePlanPolicy.Create(
            "plan-non-ci-temporary-dry-run",
            "changeset-non-ci-temporary-dry-run",
            "azureauth-credprovider",
            ConfigurationScope.ExplicitPath,
            new ConfigurationManifestMetadata
            {
                ManifestId = "manifest-non-ci-temporary-dry-run",
                OwnerProductId = "azureauth-credprovider",
                EntrySelector = "generic.file",
                ProductVersion = "0.0.0-test",
            },
            [
                CreateGenericFileChange(
                    ConfigurationChangeOperation.Create,
                    targetPath,
                    "owned-value"
                ),
            ]
        );

        var exception = await Assert.ThrowsAsync<NotSupportedException>(async () =>
            await manager.DryRunAsync(plan, TestContext.Current.CancellationToken)
        );

        Assert.Contains("CI temporary", exception.Message, StringComparison.Ordinal);
        Assert.DoesNotContain(
            fileSystem.Calls,
            call =>
                string.Equals(
                    call.Operation,
                    nameof(IFileSystemMutationLock.AcquireMutationLock),
                    StringComparison.Ordinal
                )
        );
        Assert.False(fileSystem.FileExists(targetPath));
        Assert.False(fileSystem.FileExists(manifestPath));
    }

    [Fact]
    public async Task FilesystemBackedDryRunRejectsSymbolicLinkParentEscape()
    {
        var fileSystem = new InMemoryFileSystem(InMemoryPathSemantics.Posix);
        const string targetPath = "/config/link/escape.txt";
        const string manifestPath = "/state/symlink-dry-run-manifest.json";
        fileSystem.CreateDirectory("/config");
        fileSystem.CreateDirectory("/outside");
        fileSystem.AddSymbolicLink("/config/link", "/outside");
        var manager = new ConfigurationManager(fileSystem, manifestPath);
        ConfigurationChangePlan plan = CreateGenericFilePlan(
            ConfigurationChangeOperation.Create,
            targetPath,
            "owned-value"
        );

        var exception = await Assert.ThrowsAsync<NotSupportedException>(async () =>
            await manager.DryRunAsync(plan, TestContext.Current.CancellationToken)
        );

        Assert.Contains("symbolic-link", exception.Message, StringComparison.Ordinal);
        Assert.False(fileSystem.FileExists("/outside/escape.txt"));
        Assert.False(fileSystem.FileExists(manifestPath));
    }

    [Fact]
    public async Task ApplyRejectsSymbolicLinkParentEscapeBeforeWriting()
    {
        var fileSystem = new InMemoryFileSystem(InMemoryPathSemantics.Posix);
        const string targetPath = "/config/link/escape.txt";
        const string manifestPath = "/state/symlink-write-manifest.json";
        fileSystem.CreateDirectory("/config");
        fileSystem.CreateDirectory("/outside");
        fileSystem.AddSymbolicLink("/config/link", "/outside");
        var manager = new ConfigurationManager(fileSystem, manifestPath);
        ConfigurationChangePlan plan = CreateGenericFilePlan(
            ConfigurationChangeOperation.Create,
            targetPath,
            "owned-value"
        );

        var exception = await Assert.ThrowsAsync<NotSupportedException>(async () =>
            await manager.ApplyAsync(plan, TestContext.Current.CancellationToken)
        );

        Assert.Contains("symbolic-link", exception.Message, StringComparison.Ordinal);
        Assert.False(fileSystem.FileExists("/outside/escape.txt"));
        Assert.False(fileSystem.FileExists(manifestPath));
    }

    [Fact]
    public async Task RemoveRejectsSymbolicLinkParentEscapeBeforeDeleting()
    {
        var fileSystem = new InMemoryFileSystem(InMemoryPathSemantics.Posix);
        const string targetPath = "/config/link/owned.txt";
        const string escapedPath = "/outside/owned.txt";
        const string manifestPath = "/state/symlink-delete-manifest.json";
        fileSystem.CreateDirectory("/config");
        fileSystem.CreateDirectory("/outside");
        fileSystem.AtomicWriteAllText(escapedPath, "owned-value");
        fileSystem.AddSymbolicLink("/config/link", "/outside");
        var manager = new ConfigurationManager(fileSystem, manifestPath);
        ConfigurationChangePlan manifestlessRemovePlan = CreateGenericFilePlan(
            ConfigurationChangeOperation.Remove,
            targetPath,
            value: null,
            previousOwnedEntryMetadata: HashMetadata("owned-value")
        );
        var planningOnlyManager = new ConfigurationManager();
        ConfigurationPlanResult dryRun = await planningOnlyManager.DryRunAsync(
            manifestlessRemovePlan,
            TestContext.Current.CancellationToken
        );
        string manifestJson = ConfigurationOwnershipManifestSerializer.Serialize(
            dryRun.OwnershipManifest!
        );
        fileSystem.AtomicWriteAllText(manifestPath, manifestJson);
        ConfigurationChangePlan removePlan = CreateGenericFilePlan(
            ConfigurationChangeOperation.Remove,
            targetPath,
            value: null,
            previousOwnedEntryMetadata: HashMetadata("owned-value"),
            previousManifestHash: HashMetadata(manifestJson)
        );

        var exception = await Assert.ThrowsAsync<NotSupportedException>(async () =>
            await manager.RemoveAsync(removePlan, TestContext.Current.CancellationToken)
        );

        Assert.Contains("symbolic-link", exception.Message, StringComparison.Ordinal);
        Assert.Equal("owned-value", fileSystem.ReadAllText(escapedPath));
        Assert.True(fileSystem.FileExists(manifestPath));
    }

    [Fact]
    public async Task ApplyRejectsSymbolicLinkFinalTargetBeforeMutation()
    {
        var fileSystem = new InMemoryFileSystem(InMemoryPathSemantics.Posix);
        const string targetPath = "/config/final-link.txt";
        const string externalPath = "/outside/final-link-target.txt";
        const string manifestPath = "/state/final-link-apply-manifest.json";
        fileSystem.CreateDirectory("/config");
        fileSystem.CreateDirectory("/outside");
        fileSystem.AtomicWriteAllText(externalPath, "outside-before");
        fileSystem.AddSymbolicLink(targetPath, externalPath);
        string manifestJson = await CreateSingleGenericFileManifestJsonAsync(
            targetPath,
            "outside-before"
        );
        fileSystem.AtomicWriteAllText(manifestPath, manifestJson);
        var manager = new ConfigurationManager(fileSystem, manifestPath);
        ConfigurationChangePlan plan = CreateGenericFilePlan(
            ConfigurationChangeOperation.Update,
            targetPath,
            "owned-after",
            HashMetadata("outside-before"),
            previousManifestHash: HashMetadata(manifestJson)
        );

        var exception = await Assert.ThrowsAsync<NotSupportedException>(async () =>
            await manager.ApplyAsync(plan, TestContext.Current.CancellationToken)
        );

        Assert.Contains("symbolic-link", exception.Message, StringComparison.Ordinal);
        Assert.True(fileSystem.IsSymbolicLink(targetPath));
        Assert.Equal("outside-before", fileSystem.ReadAllText(externalPath));
        Assert.Equal(manifestJson, fileSystem.ReadAllText(manifestPath));
    }

    [Fact]
    public async Task RemoveRejectsSymbolicLinkFinalTargetBeforeMutation()
    {
        var fileSystem = new InMemoryFileSystem(InMemoryPathSemantics.Posix);
        const string targetPath = "/config/final-remove-link.txt";
        const string externalPath = "/outside/final-remove-link-target.txt";
        const string manifestPath = "/state/final-link-remove-manifest.json";
        fileSystem.CreateDirectory("/config");
        fileSystem.CreateDirectory("/outside");
        fileSystem.AtomicWriteAllText(externalPath, "owned-value");
        fileSystem.AddSymbolicLink(targetPath, externalPath);
        var planningOnlyManager = new ConfigurationManager();
        ConfigurationChangePlan manifestlessRemovePlan = CreateGenericFilePlan(
            ConfigurationChangeOperation.Remove,
            targetPath,
            value: null,
            previousOwnedEntryMetadata: HashMetadata("owned-value")
        );
        ConfigurationPlanResult dryRun = await planningOnlyManager.DryRunAsync(
            manifestlessRemovePlan,
            TestContext.Current.CancellationToken
        );
        string manifestJson = ConfigurationOwnershipManifestSerializer.Serialize(
            dryRun.OwnershipManifest!
        );
        fileSystem.AtomicWriteAllText(manifestPath, manifestJson);
        var manager = new ConfigurationManager(fileSystem, manifestPath);
        ConfigurationChangePlan removePlan = CreateGenericFilePlan(
            ConfigurationChangeOperation.Remove,
            targetPath,
            value: null,
            previousOwnedEntryMetadata: HashMetadata("owned-value"),
            previousManifestHash: HashMetadata(manifestJson)
        );

        var exception = await Assert.ThrowsAsync<NotSupportedException>(async () =>
            await manager.RemoveAsync(removePlan, TestContext.Current.CancellationToken)
        );

        Assert.Contains("symbolic-link", exception.Message, StringComparison.Ordinal);
        Assert.True(fileSystem.IsSymbolicLink(targetPath));
        Assert.Equal("owned-value", fileSystem.ReadAllText(externalPath));
        Assert.Equal(manifestJson, fileSystem.ReadAllText(manifestPath));
    }

    [Fact]
    public async Task ApplyRejectsFinalTargetSymlinkSwapAtConditionalWrite()
    {
        var fileSystem = new InMemoryFileSystem(InMemoryPathSemantics.Posix);
        const string targetPath = "/config/final-link-write-race.txt";
        const string externalPath = "/outside/final-link-write-race-target.txt";
        const string manifestPath = "/state/final-link-write-race-manifest.json";
        fileSystem.CreateDirectory("/config");
        fileSystem.CreateDirectory("/outside");
        fileSystem.AtomicWriteAllText(externalPath, "outside-before");
        var manager = new ConfigurationManager(fileSystem, manifestPath);
        ConfigurationChangePlan plan = CreateGenericFilePlan(
            ConfigurationChangeOperation.Create,
            targetPath,
            "owned-after"
        );
        ArmOneShotRace(
            fileSystem,
            nameof(IFileSystem.AtomicWriteAllText),
            targetPath,
            fs => fs.AddSymbolicLink(targetPath, externalPath)
        );

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(async () =>
            await manager.ApplyAsync(plan, TestContext.Current.CancellationToken)
        );

        Assert.Contains("symbolic link", exception.Message, StringComparison.Ordinal);
        Assert.True(fileSystem.IsSymbolicLink(targetPath));
        Assert.Equal("outside-before", fileSystem.ReadAllText(externalPath));
        Assert.False(fileSystem.FileExists(manifestPath));
    }

    [Fact]
    public async Task RemoveRejectsFinalTargetSymlinkSwapAtConditionalDelete()
    {
        var fileSystem = new InMemoryFileSystem(InMemoryPathSemantics.Posix);
        const string targetPath = "/config/final-link-delete-race.txt";
        const string externalPath = "/outside/final-link-delete-race-target.txt";
        const string manifestPath = "/state/final-link-delete-race-manifest.json";
        fileSystem.CreateDirectory("/config");
        fileSystem.CreateDirectory("/outside");
        fileSystem.AtomicWriteAllText(externalPath, "outside-before");
        var manager = new ConfigurationManager(fileSystem, manifestPath);
        await manager.ApplyAsync(
            CreateGenericFilePlan(ConfigurationChangeOperation.Create, targetPath, "owned-value"),
            TestContext.Current.CancellationToken
        );
        string manifestHash = HashMetadata(fileSystem.ReadAllText(manifestPath));
        ConfigurationChangePlan removePlan = CreateGenericFilePlan(
            ConfigurationChangeOperation.Remove,
            targetPath,
            value: null,
            previousOwnedEntryMetadata: HashMetadata("owned-value"),
            previousManifestHash: manifestHash
        );
        ArmOneShotRace(
            fileSystem,
            nameof(IFileSystem.DeleteFile),
            targetPath,
            fs =>
            {
                fs.DeleteFile(targetPath);
                fs.AddSymbolicLink(targetPath, externalPath);
            }
        );

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(async () =>
            await manager.RemoveAsync(removePlan, TestContext.Current.CancellationToken)
        );

        Assert.Contains("symbolic link", exception.Message, StringComparison.Ordinal);
        Assert.True(fileSystem.IsSymbolicLink(targetPath));
        Assert.Equal("outside-before", fileSystem.ReadAllText(externalPath));
        Assert.True(fileSystem.FileExists(manifestPath));
    }

    [Fact]
    public async Task ApplyCreateDetectsTargetRaceAtMutationTime()
    {
        var fileSystem = new InMemoryFileSystem(InMemoryPathSemantics.Posix);
        const string targetPath = "/config/create-race.txt";
        const string manifestPath = "/state/create-race-manifest.json";
        var manager = new ConfigurationManager(fileSystem, manifestPath);
        ConfigurationChangePlan plan = CreateGenericFilePlan(
            ConfigurationChangeOperation.Create,
            targetPath,
            "after"
        );
        ArmNthRace(
            fileSystem,
            nameof(IFileSystem.FileExists),
            targetPath,
            occurrence: 2,
            fs => fs.AtomicWriteAllText(targetPath, "concurrent")
        );

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(async () =>
            await manager.ApplyAsync(plan, TestContext.Current.CancellationToken)
        );

        Assert.Contains(
            "create target already exists",
            exception.Message,
            StringComparison.Ordinal
        );
        Assert.Equal("concurrent", fileSystem.ReadAllText(targetPath));
        Assert.False(fileSystem.FileExists(manifestPath));
    }

    [Fact]
    public async Task ApplyCreateRejectsConcurrentTargetMutationAtConditionalWrite()
    {
        var fileSystem = new InMemoryFileSystem(InMemoryPathSemantics.Posix);
        const string targetPath = "/config/create-conditional-race.txt";
        const string manifestPath = "/state/create-conditional-race-manifest.json";
        var manager = new ConfigurationManager(fileSystem, manifestPath);
        ConfigurationChangePlan plan = CreateGenericFilePlan(
            ConfigurationChangeOperation.Create,
            targetPath,
            "after"
        );
        ArmOneShotRace(
            fileSystem,
            nameof(IFileSystem.AtomicWriteAllText),
            targetPath,
            fs => fs.AtomicWriteAllText(targetPath, "concurrent")
        );

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(async () =>
            await manager.ApplyAsync(plan, TestContext.Current.CancellationToken)
        );

        Assert.Contains("conflict", exception.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Equal("concurrent", fileSystem.ReadAllText(targetPath));
        Assert.False(fileSystem.FileExists(manifestPath));
    }

    [Fact]
    public async Task ApplyCreateConflictBeforeMutationDoesNotRollbackConcurrentIdenticalPostState()
    {
        var fileSystem = new InMemoryFileSystem(InMemoryPathSemantics.Posix);
        const string targetPath = "/config/create-identical-conditional-race.txt";
        const string manifestPath = "/state/create-identical-conditional-race-manifest.json";
        var manager = new ConfigurationManager(fileSystem, manifestPath);
        ConfigurationChangePlan plan = CreateGenericFilePlan(
            ConfigurationChangeOperation.Create,
            targetPath,
            "after"
        );
        ArmOneShotRace(
            fileSystem,
            nameof(IFileSystem.AtomicWriteAllText),
            targetPath,
            fs => fs.AtomicWriteAllText(targetPath, "after")
        );

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(async () =>
            await manager.ApplyAsync(plan, TestContext.Current.CancellationToken)
        );

        Assert.Contains("conflict", exception.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Equal("after", fileSystem.ReadAllText(targetPath));
        Assert.False(fileSystem.FileExists(manifestPath));
    }

    [Theory]
    [InlineData(ConfigurationChangeOperation.Update)]
    [InlineData(ConfigurationChangeOperation.Refresh)]
    public async Task ApplyUpdateDetectsTargetRaceAtMutationTime(
        ConfigurationChangeOperation operation
    )
    {
        var fileSystem = new InMemoryFileSystem(InMemoryPathSemantics.Posix);
        const string targetPath = "/config/update-race.txt";
        const string manifestPath = "/state/update-race-manifest.json";
        var manager = new ConfigurationManager(fileSystem, manifestPath);
        await manager.ApplyAsync(
            CreateGenericFilePlan(ConfigurationChangeOperation.Create, targetPath, "before"),
            TestContext.Current.CancellationToken
        );
        string manifestHash = HashMetadata(fileSystem.ReadAllText(manifestPath));
        ConfigurationChangePlan plan = CreateGenericFilePlan(
            operation,
            targetPath,
            "after",
            HashMetadata("before"),
            previousManifestHash: manifestHash
        );
        ArmNthRace(
            fileSystem,
            nameof(IFileSystem.FileExists),
            targetPath,
            occurrence: 2,
            fs => fs.AtomicWriteAllText(targetPath, "concurrent")
        );

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(async () =>
            await manager.ApplyAsync(plan, TestContext.Current.CancellationToken)
        );

        Assert.Contains("before-state hash", exception.Message, StringComparison.Ordinal);
        Assert.Equal("concurrent", fileSystem.ReadAllText(targetPath));
        Assert.True(fileSystem.FileExists(manifestPath));
    }

    [Fact]
    public async Task RemoveDetectsTargetRaceAtMutationTime()
    {
        var fileSystem = new InMemoryFileSystem(InMemoryPathSemantics.Posix);
        const string targetPath = "/config/remove-race.txt";
        const string manifestPath = "/state/remove-race-manifest.json";
        var manager = new ConfigurationManager(fileSystem, manifestPath);
        await manager.ApplyAsync(
            CreateGenericFilePlan(ConfigurationChangeOperation.Create, targetPath, "owned-value"),
            TestContext.Current.CancellationToken
        );
        string manifestHash = HashMetadata(fileSystem.ReadAllText(manifestPath));
        ConfigurationChangePlan removePlan = CreateGenericFilePlan(
            ConfigurationChangeOperation.Remove,
            targetPath,
            value: null,
            previousOwnedEntryMetadata: HashMetadata("owned-value"),
            previousManifestHash: manifestHash
        );
        ArmNthRace(
            fileSystem,
            nameof(IFileSystem.FileExists),
            targetPath,
            occurrence: 2,
            fs => fs.AtomicWriteAllText(targetPath, "concurrent")
        );

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(async () =>
            await manager.RemoveAsync(removePlan, TestContext.Current.CancellationToken)
        );

        Assert.Contains("before-state hash", exception.Message, StringComparison.Ordinal);
        Assert.Equal("concurrent", fileSystem.ReadAllText(targetPath));
        Assert.True(fileSystem.FileExists(manifestPath));
    }

    [Fact]
    public async Task RemoveRejectsConcurrentTargetMutationAtConditionalDelete()
    {
        var fileSystem = new InMemoryFileSystem(InMemoryPathSemantics.Posix);
        const string targetPath = "/config/remove-conditional-race.txt";
        const string manifestPath = "/state/remove-conditional-race-manifest.json";
        var manager = new ConfigurationManager(fileSystem, manifestPath);
        await manager.ApplyAsync(
            CreateGenericFilePlan(ConfigurationChangeOperation.Create, targetPath, "owned-value"),
            TestContext.Current.CancellationToken
        );
        string manifestHash = HashMetadata(fileSystem.ReadAllText(manifestPath));
        ConfigurationChangePlan removePlan = CreateGenericFilePlan(
            ConfigurationChangeOperation.Remove,
            targetPath,
            value: null,
            previousOwnedEntryMetadata: HashMetadata("owned-value"),
            previousManifestHash: manifestHash
        );
        ArmOneShotRace(
            fileSystem,
            nameof(IFileSystem.DeleteFile),
            targetPath,
            fs => fs.AtomicWriteAllText(targetPath, "concurrent")
        );

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(async () =>
            await manager.RemoveAsync(removePlan, TestContext.Current.CancellationToken)
        );

        Assert.Contains("conflict", exception.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Equal("concurrent", fileSystem.ReadAllText(targetPath));
        Assert.True(fileSystem.FileExists(manifestPath));
    }

    [Fact]
    public async Task ApplyDetectsManifestCommitRaceAtMutationTimeAndRollsBackTarget()
    {
        var fileSystem = new InMemoryFileSystem(InMemoryPathSemantics.Posix);
        const string targetPath = "/config/manifest-race.txt";
        const string manifestPath = "/state/manifest-race-manifest.json";
        fileSystem.AtomicWriteAllText(targetPath, "before");
        var manager = new ConfigurationManager(fileSystem, manifestPath);
        await manager.ApplyAsync(
            CreateGenericFilePlan(
                ConfigurationChangeOperation.Set,
                targetPath,
                "first",
                HashMetadata("before")
            ),
            TestContext.Current.CancellationToken
        );
        string manifestBefore = fileSystem.ReadAllText(manifestPath);
        ConfigurationOwnershipManifest racedManifest =
            ConfigurationOwnershipManifestSerializer.Deserialize(manifestBefore) with
            {
                ProductVersion = "0.0.0-raced",
            };
        string racedManifestJson =
            ConfigurationOwnershipManifestSerializer.Serialize(racedManifest);
        ConfigurationChangePlan plan = CreateGenericFilePlan(
            ConfigurationChangeOperation.Update,
            targetPath,
            "second",
            HashMetadata("first"),
            previousManifestHash: HashMetadata(manifestBefore)
        );
        ArmNthRace(
            fileSystem,
            nameof(IFileSystem.FileExists),
            manifestPath,
            occurrence: 2,
            fs => fs.AtomicWriteAllText(manifestPath, racedManifestJson)
        );

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(async () =>
            await manager.ApplyAsync(plan, TestContext.Current.CancellationToken)
        );

        Assert.Contains("ownership manifest", exception.Message, StringComparison.Ordinal);
        Assert.Equal("first", fileSystem.ReadAllText(targetPath));
        Assert.Equal(racedManifestJson, fileSystem.ReadAllText(manifestPath));
    }

    [Fact]
    public async Task ApplyRejectsConcurrentManifestMutationAtConditionalCommitAndRollsBackTarget()
    {
        var fileSystem = new InMemoryFileSystem(InMemoryPathSemantics.Posix);
        const string targetPath = "/config/manifest-conditional-race.txt";
        const string manifestPath = "/state/manifest-conditional-race-manifest.json";
        fileSystem.AtomicWriteAllText(targetPath, "before");
        var manager = new ConfigurationManager(fileSystem, manifestPath);
        await manager.ApplyAsync(
            CreateGenericFilePlan(
                ConfigurationChangeOperation.Set,
                targetPath,
                "first",
                HashMetadata("before")
            ),
            TestContext.Current.CancellationToken
        );
        string manifestBefore = fileSystem.ReadAllText(manifestPath);
        ConfigurationOwnershipManifest racedManifest =
            ConfigurationOwnershipManifestSerializer.Deserialize(manifestBefore) with
            {
                ProductVersion = "0.0.0-raced-at-commit",
            };
        string racedManifestJson =
            ConfigurationOwnershipManifestSerializer.Serialize(racedManifest);
        ConfigurationChangePlan plan = CreateGenericFilePlan(
            ConfigurationChangeOperation.Update,
            targetPath,
            "second",
            HashMetadata("first"),
            previousManifestHash: HashMetadata(manifestBefore)
        );
        ArmNthRace(
            fileSystem,
            nameof(IFileSystem.AtomicWriteAllText),
            manifestPath,
            occurrence: 1,
            fs => fs.AtomicWriteAllText(manifestPath, racedManifestJson)
        );

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(async () =>
            await manager.ApplyAsync(plan, TestContext.Current.CancellationToken)
        );

        Assert.Contains("conflict", exception.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Equal("first", fileSystem.ReadAllText(targetPath));
        Assert.Equal(racedManifestJson, fileSystem.ReadAllText(manifestPath));
    }

    [Fact]
    public async Task RollbackCasConflictDoesNotClobberConcurrentTargetContent()
    {
        var fileSystem = new FailOnAtomicWriteFileSystem(InMemoryPathSemantics.Posix);
        const string firstPath = "/config/rollback-cas-first.txt";
        const string secondPath = "/config/rollback-cas-second.txt";
        const string manifestPath = "/state/rollback-cas-manifest.json";
        fileSystem.AtomicWriteAllText(firstPath, "first-before");
        fileSystem.AtomicWriteAllText(secondPath, "second-before");
        ConfigurationChangePlan manifestlessPlan = CreateGenericFilePlan(
            ConfigurationChangeOperation.Update,
            firstPath,
            "first-after",
            HashMetadata("first-before")
        ) with
        {
            Changes =
            [
                CreateGenericFileChange(
                    ConfigurationChangeOperation.Update,
                    firstPath,
                    "first-after",
                    HashMetadata("first-before")
                ),
                CreateGenericFileChange(
                    ConfigurationChangeOperation.Update,
                    secondPath,
                    "second-after",
                    HashMetadata("second-before")
                ),
            ],
        };
        var planningManager = new ConfigurationManager();
        ConfigurationPlanResult dryRun = await planningManager.DryRunAsync(
            manifestlessPlan,
            TestContext.Current.CancellationToken
        );
        string manifestJson = RawOwnershipManifestJson(dryRun.OwnershipManifest!);
        fileSystem.AtomicWriteAllText(manifestPath, manifestJson);
        fileSystem.ResetAtomicWriteCount();
        fileSystem.FailOnAtomicWriteNumber = 2;
        fileSystem.AfterRecord = (call, fs) =>
        {
            if (
                (
                    string.Equals(
                        call.Operation,
                        nameof(IFileSystem.AtomicWriteAllText),
                        StringComparison.Ordinal
                    )
                    || string.Equals(
                        call.Operation,
                        nameof(IFileSystem.AtomicWriteAllBytes),
                        StringComparison.Ordinal
                    )
                )
                && string.Equals(call.Path, firstPath, StringComparison.Ordinal)
                && fs.AtomicWriteCount == 3
            )
            {
                fs.AfterRecord = null;
                fs.AtomicWriteAllText(firstPath, "concurrent");
            }
        };
        var manager = new ConfigurationManager(fileSystem, manifestPath);
        ConfigurationChangePlan plan = manifestlessPlan with
        {
            Manifest = manifestlessPlan.Manifest with
            {
                PreviousOwnedEntryHash = HashMetadata(manifestJson),
            },
        };

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(async () =>
            await manager.ApplyAsync(plan, TestContext.Current.CancellationToken)
        );

        Assert.Contains("rollback failed", exception.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Equal("concurrent", fileSystem.ReadAllText(firstPath));
        Assert.Equal("second-before", fileSystem.ReadAllText(secondPath));
        Assert.Equal(manifestJson, fileSystem.ReadAllText(manifestPath));
    }

    [Fact]
    public async Task ApplyRejectsSymbolicLinkParentSwappedAfterManagerCheck()
    {
        var fileSystem = new InMemoryFileSystem(InMemoryPathSemantics.Posix);
        const string targetPath = "/config/link/escape.txt";
        const string escapedPath = "/outside/escape.txt";
        const string manifestPath = "/state/symlink-swap-write-manifest.json";
        fileSystem.CreateDirectory("/config");
        fileSystem.CreateDirectory("/config/link");
        fileSystem.CreateDirectory("/outside");
        var manager = new ConfigurationManager(fileSystem, manifestPath);
        ConfigurationChangePlan plan = CreateGenericFilePlan(
            ConfigurationChangeOperation.Create,
            targetPath,
            "owned-value"
        );
        ArmOneShotRace(
            fileSystem,
            nameof(IFileSystem.AtomicWriteAllText),
            targetPath,
            fs =>
            {
                fs.DeleteDirectory("/config/link");
                fs.AddSymbolicLink("/config/link", "/outside");
            }
        );

        var exception = await Assert.ThrowsAsync<NotSupportedException>(async () =>
            await manager.ApplyAsync(plan, TestContext.Current.CancellationToken)
        );

        Assert.Contains("symbolic-link", exception.Message, StringComparison.Ordinal);
        Assert.False(fileSystem.FileExists(escapedPath));
        Assert.False(fileSystem.FileExists(manifestPath));
    }

    [Fact]
    public async Task
        ApplyRejectsTemporaryContainerSymlinkSwapAfterTargetSnapshotBeforeContainerSnapshot()
    {
        var fileSystem = new InMemoryFileSystem(InMemoryPathSemantics.Posix);
        const string containerPath = "/config/snapshot-symlink-swap";
        const string targetPath = "/config/snapshot-symlink-swap/owned.txt";
        const string outsideContainerPath = "/outside/snapshot-symlink-swap";
        const string externalFilePath = "/outside/snapshot-symlink-swap/external.txt";
        const string manifestPath = "/state/snapshot-symlink-swap-manifest.json";
        fileSystem.CreateDirectory(containerPath);
        fileSystem.CreateDirectory(outsideContainerPath);
        fileSystem.WriteAllText(externalFilePath, "external");
        var manager = new ConfigurationManager(fileSystem, manifestPath);
        bool targetSnapshotSeen = false;
        int? swapInstalledAtCallCount = null;
        fileSystem.AfterRecord = (call, fs) =>
        {
            if (
                string.Equals(
                    call.Operation,
                    nameof(IFileSystem.FileExists),
                    StringComparison.Ordinal
                )
                && string.Equals(call.Path, targetPath, StringComparison.Ordinal)
            )
            {
                targetSnapshotSeen = true;
                return;
            }

            if (
                targetSnapshotSeen
                && string.Equals(
                    call.Operation,
                    nameof(IFileSystem.IsSymbolicLink),
                    StringComparison.Ordinal
                )
                && string.Equals(call.Path, containerPath, StringComparison.Ordinal)
            )
            {
                fs.AfterRecord = null;
                fs.DeleteDirectory(containerPath, recursive: true);
                fs.AddSymbolicLink(containerPath, outsideContainerPath);
                swapInstalledAtCallCount = fs.Calls.Count;
            }
        };

        var exception = await Assert.ThrowsAsync<NotSupportedException>(async () =>
            await manager.ApplyAsync(
                CreateGenericFilePlan(
                    ConfigurationChangeOperation.Create,
                    targetPath,
                    "owned-value"
                ),
                TestContext.Current.CancellationToken
            )
        );

        Assert.True(swapInstalledAtCallCount.HasValue);
        Assert.Contains("symbolic-link", exception.Message, StringComparison.Ordinal);
        FileSystemCall[] callsAfterSwap = fileSystem.Calls
            .Skip(swapInstalledAtCallCount.Value)
            .ToArray();
        Assert.DoesNotContain(
            callsAfterSwap,
            call =>
                (
                    string.Equals(
                        call.Operation,
                        nameof(IFileSystem.DirectoryExists),
                        StringComparison.Ordinal
                    )
                    || string.Equals(
                        call.Operation,
                        nameof(IFileSystem.EnumerateFiles),
                        StringComparison.Ordinal
                    )
                    || string.Equals(
                        call.Operation,
                        nameof(IFileSystem.EnumerateDirectories),
                        StringComparison.Ordinal
                    )
                    || string.Equals(
                        call.Operation,
                        nameof(IFileSystem.ReadAllBytes),
                        StringComparison.Ordinal
                    )
                )
                && (
                    string.Equals(call.Path, containerPath, StringComparison.Ordinal)
                    || call.Path.StartsWith(containerPath + "/", StringComparison.Ordinal)
                    || call.Path.StartsWith(outsideContainerPath + "/", StringComparison.Ordinal)
                    || string.Equals(call.Path, outsideContainerPath, StringComparison.Ordinal)
                )
        );
        Assert.Equal("external", fileSystem.ReadAllText(externalFilePath));
        Assert.True(fileSystem.IsSymbolicLink(containerPath));
        Assert.False(fileSystem.FileExists(targetPath));
        Assert.False(fileSystem.FileExists(manifestPath));
    }

    [Fact]
    public async Task
        ApplyRejectsTemporaryContainerReparsePointSwapAfterTargetSnapshotBeforeContainerSnapshot()
    {
        var fileSystem = new InMemoryFileSystem(InMemoryPathSemantics.Posix);
        const string containerPath = "/config/snapshot-reparse-swap";
        const string targetPath = "/config/snapshot-reparse-swap/owned.txt";
        const string manifestPath = "/state/snapshot-reparse-swap-manifest.json";
        fileSystem.CreateDirectory(containerPath);
        var manager = new ConfigurationManager(fileSystem, manifestPath);
        bool targetSnapshotSeen = false;
        int? swapInstalledAtCallCount = null;
        fileSystem.AfterRecord = (call, fs) =>
        {
            if (
                string.Equals(
                    call.Operation,
                    nameof(IFileSystem.FileExists),
                    StringComparison.Ordinal
                )
                && string.Equals(call.Path, targetPath, StringComparison.Ordinal)
            )
            {
                targetSnapshotSeen = true;
                return;
            }

            if (
                targetSnapshotSeen
                && string.Equals(
                    call.Operation,
                    nameof(IFileSystemReparsePointSafety.IsReparsePoint),
                    StringComparison.Ordinal
                )
                && string.Equals(call.Path, containerPath, StringComparison.Ordinal)
            )
            {
                fs.AfterRecord = null;
                fs.MarkAsNonSymbolicReparsePoint(containerPath);
                swapInstalledAtCallCount = fs.Calls.Count;
            }
        };

        var exception = await Assert.ThrowsAsync<NotSupportedException>(async () =>
            await manager.ApplyAsync(
                CreateGenericFilePlan(
                    ConfigurationChangeOperation.Create,
                    targetPath,
                    "owned-value"
                ),
                TestContext.Current.CancellationToken
            )
        );

        Assert.True(swapInstalledAtCallCount.HasValue);
        Assert.Contains("reparse-point", exception.Message, StringComparison.Ordinal);
        FileSystemCall[] callsAfterSwap = fileSystem.Calls
            .Skip(swapInstalledAtCallCount.Value)
            .ToArray();
        Assert.DoesNotContain(
            callsAfterSwap,
            call =>
                (
                    string.Equals(
                        call.Operation,
                        nameof(IFileSystem.DirectoryExists),
                        StringComparison.Ordinal
                    )
                    || string.Equals(
                        call.Operation,
                        nameof(IFileSystem.EnumerateFiles),
                        StringComparison.Ordinal
                    )
                    || string.Equals(
                        call.Operation,
                        nameof(IFileSystem.EnumerateDirectories),
                        StringComparison.Ordinal
                    )
                    || string.Equals(
                        call.Operation,
                        nameof(IFileSystem.ReadAllBytes),
                        StringComparison.Ordinal
                    )
                )
                && (
                    string.Equals(call.Path, containerPath, StringComparison.Ordinal)
                    || call.Path.StartsWith(containerPath + "/", StringComparison.Ordinal)
                )
        );
        Assert.True(fileSystem.DirectoryExists(containerPath));
        Assert.True(((IFileSystemReparsePointSafety)fileSystem).IsReparsePoint(containerPath));
        Assert.False(fileSystem.FileExists(targetPath));
        Assert.False(fileSystem.FileExists(manifestPath));
    }

    [Fact]
    public async Task
        RemoveRejectsTemporaryContainerSymlinkSwapAfterTargetSnapshotBeforeContainerSnapshot()
    {
        var fileSystem = new InMemoryFileSystem(InMemoryPathSemantics.Posix);
        const string containerPath = "/config/remove-snapshot-symlink-swap";
        const string targetPath = "/config/remove-snapshot-symlink-swap/owned.txt";
        const string outsideContainerPath = "/outside/remove-snapshot-symlink-swap";
        const string externalFilePath = "/outside/remove-snapshot-symlink-swap/external.txt";
        const string manifestPath = "/state/remove-snapshot-symlink-swap-manifest.json";
        fileSystem.CreateDirectory(containerPath);
        fileSystem.CreateDirectory(outsideContainerPath);
        fileSystem.WriteAllText(externalFilePath, "external");
        var manager = new ConfigurationManager(fileSystem, manifestPath);
        await manager.ApplyAsync(
            CreateGenericFilePlan(ConfigurationChangeOperation.Create, targetPath, "owned-value"),
            TestContext.Current.CancellationToken
        );
        string manifestBefore = fileSystem.ReadAllText(manifestPath);
        ConfigurationChangePlan removePlan = CreateGenericFilePlan(
            ConfigurationChangeOperation.Remove,
            targetPath,
            value: null,
            previousOwnedEntryMetadata: HashMetadata("owned-value"),
            previousManifestHash: HashMetadata(manifestBefore)
        );
        fileSystem.Calls.Clear();
        bool targetSnapshotSeen = false;
        int? swapInstalledAtCallCount = null;
        fileSystem.AfterRecord = (call, fs) =>
        {
            if (
                string.Equals(
                    call.Operation,
                    nameof(IFileSystem.FileExists),
                    StringComparison.Ordinal
                )
                && string.Equals(call.Path, targetPath, StringComparison.Ordinal)
            )
            {
                targetSnapshotSeen = true;
                return;
            }

            if (
                targetSnapshotSeen
                && string.Equals(
                    call.Operation,
                    nameof(IFileSystem.IsSymbolicLink),
                    StringComparison.Ordinal
                )
                && string.Equals(call.Path, containerPath, StringComparison.Ordinal)
            )
            {
                fs.AfterRecord = null;
                fs.DeleteDirectory(containerPath, recursive: true);
                fs.AddSymbolicLink(containerPath, outsideContainerPath);
                swapInstalledAtCallCount = fs.Calls.Count;
            }
        };

        var exception = await Assert.ThrowsAsync<NotSupportedException>(async () =>
            await manager.RemoveAsync(removePlan, TestContext.Current.CancellationToken)
        );

        Assert.True(swapInstalledAtCallCount.HasValue);
        Assert.Contains("symbolic-link", exception.Message, StringComparison.Ordinal);
        FileSystemCall[] callsAfterSwap = fileSystem.Calls
            .Skip(swapInstalledAtCallCount.Value)
            .ToArray();
        Assert.DoesNotContain(
            callsAfterSwap,
            call =>
                (
                    string.Equals(
                        call.Operation,
                        nameof(IFileSystem.DirectoryExists),
                        StringComparison.Ordinal
                    )
                    || string.Equals(
                        call.Operation,
                        nameof(IFileSystem.EnumerateFiles),
                        StringComparison.Ordinal
                    )
                    || string.Equals(
                        call.Operation,
                        nameof(IFileSystem.EnumerateDirectories),
                        StringComparison.Ordinal
                    )
                    || string.Equals(
                        call.Operation,
                        nameof(IFileSystem.ReadAllBytes),
                        StringComparison.Ordinal
                    )
                )
                && (
                    string.Equals(call.Path, containerPath, StringComparison.Ordinal)
                    || call.Path.StartsWith(containerPath + "/", StringComparison.Ordinal)
                    || call.Path.StartsWith(outsideContainerPath + "/", StringComparison.Ordinal)
                    || string.Equals(call.Path, outsideContainerPath, StringComparison.Ordinal)
                )
        );
        Assert.Equal("external", fileSystem.ReadAllText(externalFilePath));
        Assert.True(fileSystem.IsSymbolicLink(containerPath));
        Assert.False(fileSystem.FileExists(targetPath));
        Assert.Equal(manifestBefore, fileSystem.ReadAllText(manifestPath));
    }

    [Fact]
    public async Task
        RemoveRejectsTemporaryContainerReparsePointSwapAfterTargetSnapshotBeforeContainerSnapshot()
    {
        var fileSystem = new InMemoryFileSystem(InMemoryPathSemantics.Posix);
        const string containerPath = "/config/remove-snapshot-reparse-swap";
        const string targetPath = "/config/remove-snapshot-reparse-swap/owned.txt";
        const string manifestPath = "/state/remove-snapshot-reparse-swap-manifest.json";
        fileSystem.CreateDirectory(containerPath);
        var manager = new ConfigurationManager(fileSystem, manifestPath);
        await manager.ApplyAsync(
            CreateGenericFilePlan(ConfigurationChangeOperation.Create, targetPath, "owned-value"),
            TestContext.Current.CancellationToken
        );
        string manifestBefore = fileSystem.ReadAllText(manifestPath);
        ConfigurationChangePlan removePlan = CreateGenericFilePlan(
            ConfigurationChangeOperation.Remove,
            targetPath,
            value: null,
            previousOwnedEntryMetadata: HashMetadata("owned-value"),
            previousManifestHash: HashMetadata(manifestBefore)
        );
        fileSystem.Calls.Clear();
        bool targetSnapshotSeen = false;
        int? swapInstalledAtCallCount = null;
        fileSystem.AfterRecord = (call, fs) =>
        {
            if (
                string.Equals(
                    call.Operation,
                    nameof(IFileSystem.FileExists),
                    StringComparison.Ordinal
                )
                && string.Equals(call.Path, targetPath, StringComparison.Ordinal)
            )
            {
                targetSnapshotSeen = true;
                return;
            }

            if (
                targetSnapshotSeen
                && string.Equals(
                    call.Operation,
                    nameof(IFileSystemReparsePointSafety.IsReparsePoint),
                    StringComparison.Ordinal
                )
                && string.Equals(call.Path, containerPath, StringComparison.Ordinal)
            )
            {
                fs.AfterRecord = null;
                fs.MarkAsNonSymbolicReparsePoint(containerPath);
                swapInstalledAtCallCount = fs.Calls.Count;
            }
        };

        var exception = await Assert.ThrowsAsync<NotSupportedException>(async () =>
            await manager.RemoveAsync(removePlan, TestContext.Current.CancellationToken)
        );

        Assert.True(swapInstalledAtCallCount.HasValue);
        Assert.Contains("reparse-point", exception.Message, StringComparison.Ordinal);
        FileSystemCall[] callsAfterSwap = fileSystem.Calls
            .Skip(swapInstalledAtCallCount.Value)
            .ToArray();
        Assert.DoesNotContain(
            callsAfterSwap,
            call =>
                (
                    string.Equals(
                        call.Operation,
                        nameof(IFileSystem.DirectoryExists),
                        StringComparison.Ordinal
                    )
                    || string.Equals(
                        call.Operation,
                        nameof(IFileSystem.EnumerateFiles),
                        StringComparison.Ordinal
                    )
                    || string.Equals(
                        call.Operation,
                        nameof(IFileSystem.EnumerateDirectories),
                        StringComparison.Ordinal
                    )
                    || string.Equals(
                        call.Operation,
                        nameof(IFileSystem.ReadAllBytes),
                        StringComparison.Ordinal
                    )
                )
                && (
                    string.Equals(call.Path, containerPath, StringComparison.Ordinal)
                    || call.Path.StartsWith(containerPath + "/", StringComparison.Ordinal)
                )
        );
        Assert.True(fileSystem.DirectoryExists(containerPath));
        Assert.True(((IFileSystemReparsePointSafety)fileSystem).IsReparsePoint(containerPath));
        Assert.Equal("owned-value", fileSystem.ReadAllText(targetPath));
        Assert.Equal(manifestBefore, fileSystem.ReadAllText(manifestPath));
    }

    [Fact]
    public async Task ApplyRollsBackDurableWritesWhenLaterWriteFails()
    {
        var fileSystem = new FailOnAtomicWriteFileSystem(InMemoryPathSemantics.Posix);
        const string firstPath = "/config/first.txt";
        const string secondPath = "/config/second.txt";
        const string manifestPath = "/state/rollback-manifest.json";
        fileSystem.AtomicWriteAllText(firstPath, "first-before");
        fileSystem.AtomicWriteAllText(secondPath, "second-before");
        ConfigurationChangePlan manifestlessPlan = CreateGenericFilePlan(
            ConfigurationChangeOperation.Update,
            firstPath,
            "first-after",
            HashMetadata("first-before")
        ) with
        {
            Changes =
            [
                CreateGenericFileChange(
                    ConfigurationChangeOperation.Update,
                    firstPath,
                    "first-after",
                    HashMetadata("first-before")
                ),
                CreateGenericFileChange(
                    ConfigurationChangeOperation.Update,
                    secondPath,
                    "second-after",
                    HashMetadata("second-before")
                ),
            ],
        };
        var planningManager = new ConfigurationManager();
        ConfigurationPlanResult dryRun = await planningManager.DryRunAsync(
            manifestlessPlan,
            TestContext.Current.CancellationToken
        );
        string manifestJson = RawOwnershipManifestJson(dryRun.OwnershipManifest!);
        fileSystem.AtomicWriteAllText(manifestPath, manifestJson);
        fileSystem.ResetAtomicWriteCount();
        fileSystem.FailOnAtomicWriteNumber = 2;
        var manager = new ConfigurationManager(fileSystem, manifestPath);
        ConfigurationChangePlan plan = manifestlessPlan with
        {
            Manifest = manifestlessPlan.Manifest with
            {
                PreviousOwnedEntryHash = HashMetadata(manifestJson),
            },
        };

        await Assert.ThrowsAnyAsync<IOException>(async () =>
            await manager.ApplyAsync(plan, TestContext.Current.CancellationToken)
        );

        Assert.Equal("first-before", fileSystem.ReadAllText(firstPath));
        Assert.Equal("second-before", fileSystem.ReadAllText(secondPath));
        Assert.Equal(manifestJson, fileSystem.ReadAllText(manifestPath));
    }

    [Fact]
    public async Task ApplyRollbackDeletesNewTemporaryContainerWhenManifestCommitFails()
    {
        var fileSystem = new FailOnAtomicWriteFileSystem(InMemoryPathSemantics.Posix)
        {
            FailOnAtomicWriteNumber = 2,
        };
        const string containerPath = "/config/rollback-new";
        const string targetPath = "/config/rollback-new/owned.txt";
        const string manifestPath = "/state/rollback-new-manifest.json";
        var manager = new ConfigurationManager(fileSystem, manifestPath);

        await Assert.ThrowsAnyAsync<IOException>(async () =>
            await manager.ApplyAsync(
                CreateGenericFilePlan(
                    ConfigurationChangeOperation.Create,
                    targetPath,
                    "owned-value"
                ),
                TestContext.Current.CancellationToken
            )
        );

        Assert.False(fileSystem.FileExists(targetPath));
        Assert.False(fileSystem.DirectoryExists(containerPath));
        Assert.False(fileSystem.FileExists(manifestPath));
    }

    [Fact]
    public async Task ApplyRollbackDeletesNewTemporaryContainerIncludingEmptyInternalLockArtifact()
    {
        var fileSystem = new FailOnAtomicWriteFileSystem(InMemoryPathSemantics.Posix)
        {
            FailOnAtomicWriteNumber = 2,
        };
        const string containerPath = "/config/rollback-new-lock";
        const string targetPath = "/config/rollback-new-lock/owned.txt";
        const string lockPath = "/config/rollback-new-lock/.azureauth-credprovider.fs.lock";
        const string tempArtifactPath = "/config/rollback-new-lock/.atomic-write.tmp";
        const string manifestPath = "/state/rollback-new-lock-manifest.json";
        fileSystem.AfterRecord = (call, fs) =>
        {
            if (
                string.Equals(
                    call.Operation,
                    nameof(IFileSystem.AtomicWriteAllText),
                    StringComparison.Ordinal
                )
                && string.Equals(call.Path, manifestPath, StringComparison.Ordinal)
                && fs.AtomicWriteCount == 2
            )
            {
                fs.AfterRecord = null;
                fs.WriteAllText(lockPath, string.Empty);
                fs.WriteAllText(tempArtifactPath, "temp");
            }
        };
        var manager = new ConfigurationManager(fileSystem, manifestPath);

        await Assert.ThrowsAnyAsync<IOException>(async () =>
            await manager.ApplyAsync(
                CreateGenericFilePlan(
                    ConfigurationChangeOperation.Create,
                    targetPath,
                    "owned-value"
                ),
                TestContext.Current.CancellationToken
            )
        );

        Assert.False(fileSystem.FileExists(targetPath));
        Assert.False(fileSystem.DirectoryExists(containerPath));
        Assert.False(fileSystem.FileExists(lockPath));
        Assert.False(fileSystem.FileExists(tempArtifactPath));
        Assert.False(fileSystem.FileExists(manifestPath));
    }

    [Fact]
    public async Task ApplyRollbackPreservesNewTemporaryContainerWithNonEmptyInternalLockArtifact()
    {
        var fileSystem = new FailOnAtomicWriteFileSystem(InMemoryPathSemantics.Posix)
        {
            FailOnAtomicWriteNumber = 2,
        };
        const string containerPath = "/config/rollback-new-non-empty-lock";
        const string targetPath = "/config/rollback-new-non-empty-lock/owned.txt";
        const string lockPath =
            "/config/rollback-new-non-empty-lock/.azureauth-credprovider.fs.lock";
        const string tempArtifactPath = "/config/rollback-new-non-empty-lock/.atomic-write.tmp";
        const string manifestPath = "/state/rollback-new-non-empty-lock-manifest.json";
        fileSystem.AfterRecord = (call, fs) =>
        {
            if (
                string.Equals(
                    call.Operation,
                    nameof(IFileSystem.AtomicWriteAllText),
                    StringComparison.Ordinal
                )
                && string.Equals(call.Path, manifestPath, StringComparison.Ordinal)
                && fs.AtomicWriteCount == 2
            )
            {
                fs.AfterRecord = null;
                fs.WriteAllText(lockPath, "lock");
                fs.WriteAllText(tempArtifactPath, "temp");
            }
        };
        var manager = new ConfigurationManager(fileSystem, manifestPath);

        await Assert.ThrowsAnyAsync<IOException>(async () =>
            await manager.ApplyAsync(
                CreateGenericFilePlan(
                    ConfigurationChangeOperation.Create,
                    targetPath,
                    "owned-value"
                ),
                TestContext.Current.CancellationToken
            )
        );

        Assert.False(fileSystem.FileExists(targetPath));
        Assert.True(fileSystem.DirectoryExists(containerPath));
        Assert.Equal("lock", fileSystem.ReadAllText(lockPath));
        Assert.True(fileSystem.FileExists(tempArtifactPath));
        Assert.False(fileSystem.FileExists(manifestPath));
    }

    [Fact]
    public async Task ApplyRollbackPreservesNewTemporaryContainerWhenDanglingLockLinkExists()
    {
        var fileSystem = new FailOnAtomicWriteFileSystem(InMemoryPathSemantics.Posix)
        {
            FailOnAtomicWriteNumber = 2,
        };
        const string containerPath = "/config/rollback-new-dangling-lock";
        const string targetPath = "/config/rollback-new-dangling-lock/owned.txt";
        const string lockPath =
            "/config/rollback-new-dangling-lock/.azureauth-credprovider.fs.lock";
        const string manifestPath = "/state/rollback-new-dangling-lock-manifest.json";
        fileSystem.AfterRecord = (call, fs) =>
        {
            if (
                string.Equals(
                    call.Operation,
                    nameof(IFileSystem.AtomicWriteAllText),
                    StringComparison.Ordinal
                )
                && string.Equals(call.Path, manifestPath, StringComparison.Ordinal)
                && fs.AtomicWriteCount == 2
            )
            {
                fs.AfterRecord = null;
                fs.AddSymbolicLink(lockPath, "/missing/rollback-new-dangling-lock-target");
            }
        };
        var manager = new ConfigurationManager(fileSystem, manifestPath);

        await Assert.ThrowsAnyAsync<IOException>(async () =>
            await manager.ApplyAsync(
                CreateGenericFilePlan(
                    ConfigurationChangeOperation.Create,
                    targetPath,
                    "owned-value"
                ),
                TestContext.Current.CancellationToken
            )
        );

        Assert.True(fileSystem.DirectoryExists(containerPath));
        Assert.False(fileSystem.FileExists(targetPath));
        Assert.True(fileSystem.IsSymbolicLink(lockPath));
        Assert.False(fileSystem.FileExists(manifestPath));
    }

    [Fact]
    public async Task ApplyRollbackPreservesExistingTemporaryContainerWhenDanglingLockLinkExists()
    {
        var fileSystem = new FailOnAtomicWriteFileSystem(InMemoryPathSemantics.Posix)
        {
            FailOnAtomicWriteNumber = 2,
        };
        const string containerPath = "/config/rollback-existing-dangling-lock";
        const string targetPath = "/config/rollback-existing-dangling-lock/owned.txt";
        const string lockPath =
            "/config/rollback-existing-dangling-lock/.azureauth-credprovider.fs.lock";
        const string manifestPath = "/state/rollback-existing-dangling-lock-manifest.json";
        fileSystem.CreateDirectory(containerPath);
        fileSystem.AddSymbolicLink(lockPath, "/missing/rollback-existing-dangling-lock-target");
        var manager = new ConfigurationManager(fileSystem, manifestPath);

        await Assert.ThrowsAnyAsync<IOException>(async () =>
            await manager.ApplyAsync(
                CreateGenericFilePlan(
                    ConfigurationChangeOperation.Create,
                    targetPath,
                    "owned-value"
                ),
                TestContext.Current.CancellationToken
            )
        );

        Assert.True(fileSystem.DirectoryExists(containerPath));
        Assert.False(fileSystem.FileExists(targetPath));
        Assert.True(fileSystem.IsSymbolicLink(lockPath));
        Assert.False(fileSystem.FileExists(manifestPath));
    }

    [Theory]
    [InlineData("/config/rollback-existing-unsafe-descendant/.dangling.tmp", false, true)]
    [InlineData("/config/rollback-existing-unsafe-descendant/dangling-directory", true, true)]
    [InlineData("/config/rollback-existing-unsafe-descendant/.reparse.tmp", false, false)]
    [InlineData("/config/rollback-existing-unsafe-descendant/reparse-directory", true, false)]
    public async Task ApplyRejectsExistingTemporaryContainerWhenNonLockDescendantIsUnsafe(
        string unsafePath,
        bool isDirectory,
        bool useSymbolicLink
    )
    {
        var fileSystem = new FailOnAtomicWriteFileSystem(InMemoryPathSemantics.Posix)
        {
            FailOnAtomicWriteNumber = 2,
        };
        const string containerPath = "/config/rollback-existing-unsafe-descendant";
        const string targetPath = "/config/rollback-existing-unsafe-descendant/owned.txt";
        const string manifestPath = "/state/rollback-existing-unsafe-descendant-manifest.json";
        fileSystem.CreateDirectory(containerPath);
        if (useSymbolicLink)
        {
            fileSystem.AddSymbolicLink(unsafePath, "/missing/rollback-existing-unsafe-descendant");
        }
        else if (isDirectory)
        {
            fileSystem.CreateDirectory(unsafePath);
            fileSystem.MarkAsNonSymbolicReparsePoint(unsafePath);
        }
        else
        {
            fileSystem.WriteAllText(unsafePath, "unsafe");
            fileSystem.MarkAsNonSymbolicReparsePoint(unsafePath);
        }
        var manager = new ConfigurationManager(fileSystem, manifestPath);

        await Assert.ThrowsAsync<NotSupportedException>(async () =>
            await manager.ApplyAsync(
                CreateGenericFilePlan(
                    ConfigurationChangeOperation.Create,
                    targetPath,
                    "owned-value"
                ),
                TestContext.Current.CancellationToken
            )
        );

        Assert.True(fileSystem.DirectoryExists(containerPath));
        Assert.False(fileSystem.FileExists(targetPath));
        Assert.False(fileSystem.FileExists(manifestPath));
        if (useSymbolicLink)
        {
            Assert.True(fileSystem.IsSymbolicLink(unsafePath));
        }
        else
        {
            Assert.True(((IFileSystemReparsePointSafety)fileSystem).IsReparsePoint(unsafePath));
        }
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task ApplySnapshotRejectsUnsafeDescendantWithoutRecursiveEnumeration(
        bool useSymbolicLink
    )
    {
        var fileSystem = new InMemoryFileSystem(InMemoryPathSemantics.Posix);
        const string containerPath = "/config/snapshot-unsafe-descendant";
        const string targetPath = "/config/snapshot-unsafe-descendant/owned.txt";
        const string unsafeDirectoryPath = "/config/snapshot-unsafe-descendant/unsafe-directory";
        const string hiddenChildPath =
            "/config/snapshot-unsafe-descendant/unsafe-directory/hidden.txt";
        const string outsideDirectoryPath = "/outside/snapshot-unsafe-descendant";
        const string outsideFilePath = "/outside/snapshot-unsafe-descendant/hidden.txt";
        const string manifestPath = "/state/snapshot-unsafe-descendant-manifest.json";
        fileSystem.CreateDirectory(containerPath);
        if (useSymbolicLink)
        {
            fileSystem.CreateDirectory(outsideDirectoryPath);
            fileSystem.WriteAllText(outsideFilePath, "outside");
            fileSystem.AddSymbolicLink(unsafeDirectoryPath, outsideDirectoryPath);
        }
        else
        {
            fileSystem.CreateDirectory(unsafeDirectoryPath);
            fileSystem.WriteAllText(hiddenChildPath, "hidden");
            fileSystem.MarkAsNonSymbolicReparsePoint(unsafeDirectoryPath);
        }
        var manager = new ConfigurationManager(fileSystem, manifestPath);

        var exception = await Assert.ThrowsAsync<NotSupportedException>(async () =>
            await manager.ApplyAsync(
                CreateGenericFilePlan(
                    ConfigurationChangeOperation.Create,
                    targetPath,
                    "owned-value"
                ),
                TestContext.Current.CancellationToken
            )
        );

        Assert.Contains(
            "rollback snapshot rejects symbolic-link or reparse-point descendants",
            exception.Message,
            StringComparison.Ordinal
        );
        Assert.DoesNotContain(
            fileSystem.Calls,
            call =>
                string.Equals(
                    call.Operation,
                    nameof(IFileSystem.EnumerateFiles),
                    StringComparison.Ordinal
                )
                || string.Equals(
                    call.Operation,
                    nameof(IFileSystem.EnumerateDirectories),
                    StringComparison.Ordinal
                )
        );
        Assert.Contains(
            fileSystem.Calls,
            call =>
                string.Equals(
                    call.Operation,
                    nameof(IFileSystemNoFollowEnumeration.EnumerateFileSystemEntriesNoFollow),
                    StringComparison.Ordinal
                )
        );
        Assert.False(fileSystem.FileExists(targetPath));
        Assert.False(fileSystem.FileExists(manifestPath));
        if (useSymbolicLink)
        {
            Assert.True(fileSystem.IsSymbolicLink(unsafeDirectoryPath));
            Assert.Equal("outside", fileSystem.ReadAllText(outsideFilePath));
        }
        else
        {
            Assert.True(
                ((IFileSystemReparsePointSafety)fileSystem).IsReparsePoint(unsafeDirectoryPath)
            );
            Assert.Equal("hidden", fileSystem.ReadAllText(hiddenChildPath));
        }
    }

    [Theory]
    [InlineData("/config/rollback-new-dangling-descendant/.dangling.tmp")]
    [InlineData("/config/rollback-new-dangling-descendant/dangling-directory")]
    public async Task
        ApplyRollbackPreservesNewTemporaryContainerWhenNonLockDanglingDescendantExists(
        string danglingPath
    )
    {
        var fileSystem = new FailOnAtomicWriteFileSystem(InMemoryPathSemantics.Posix)
        {
            FailOnAtomicWriteNumber = 2,
        };
        const string containerPath = "/config/rollback-new-dangling-descendant";
        const string targetPath = "/config/rollback-new-dangling-descendant/owned.txt";
        const string manifestPath = "/state/rollback-new-dangling-descendant-manifest.json";
        fileSystem.AfterRecord = (call, fs) =>
        {
            if (
                string.Equals(
                    call.Operation,
                    nameof(IFileSystem.AtomicWriteAllText),
                    StringComparison.Ordinal
                )
                && string.Equals(call.Path, manifestPath, StringComparison.Ordinal)
                && fs.AtomicWriteCount == 2
            )
            {
                fs.AfterRecord = null;
                fs.AddSymbolicLink(
                    danglingPath,
                    "/missing/rollback-new-dangling-descendant-target"
                );
            }
        };
        var manager = new ConfigurationManager(fileSystem, manifestPath);

        await Assert.ThrowsAnyAsync<IOException>(async () =>
            await manager.ApplyAsync(
                CreateGenericFilePlan(
                    ConfigurationChangeOperation.Create,
                    targetPath,
                    "owned-value"
                ),
                TestContext.Current.CancellationToken
            )
        );

        Assert.True(fileSystem.DirectoryExists(containerPath));
        Assert.False(fileSystem.FileExists(targetPath));
        Assert.True(fileSystem.IsSymbolicLink(danglingPath));
        Assert.False(fileSystem.FileExists(manifestPath));
    }

    [Fact]
    public async Task FullRemovePreservesExistingTemporaryContainerLockAndStableLifecycleLock()
    {
        var fileSystem = new InMemoryFileSystem(InMemoryPathSemantics.Posix);
        const string containerPath = "/config/full-remove-lock";
        const string targetPath = "/config/full-remove-lock/owned.txt";
        const string lockPath = "/config/full-remove-lock/.azureauth-credprovider.fs.lock";
        const string manifestPath = "/state/full-remove-lock-manifest.json";
        var manager = new ConfigurationManager(fileSystem, manifestPath);
        await manager.ApplyAsync(
            CreateGenericFilePlan(ConfigurationChangeOperation.Create, targetPath, "owned-value"),
            TestContext.Current.CancellationToken
        );
        string manifestBefore = fileSystem.ReadAllText(manifestPath);
        fileSystem.WriteAllText(lockPath, "lock");
        ConfigurationChangePlan removePlan = CreateGenericFilePlan(
            ConfigurationChangeOperation.Remove,
            targetPath,
            value: null,
            previousOwnedEntryMetadata: HashMetadata("owned-value"),
            previousManifestHash: HashMetadata(manifestBefore)
        );
        fileSystem.Calls.Clear();

        await manager.RemoveAsync(removePlan, TestContext.Current.CancellationToken);

        AssertNoLockArtifactContentReads(fileSystem, lockPath);
        AssertLockArtifactLengthChecked(fileSystem, lockPath);
        string lifecycleLockPath = Assert.Single(GetLifecycleLockPaths(fileSystem));
        Assert.True(fileSystem.DirectoryExists(containerPath));
        Assert.False(fileSystem.FileExists(targetPath));
        Assert.Equal("lock", fileSystem.ReadAllText(lockPath));
        Assert.True(fileSystem.DirectoryExists(lifecycleLockPath));
        Assert.False(fileSystem.FileExists(manifestPath));
    }

    [Fact]
    public async Task FullRemoveIgnoresEmptyInternalLockArtifactWithoutContentRead()
    {
        var fileSystem = new InMemoryFileSystem(InMemoryPathSemantics.Posix);
        const string containerPath = "/config/full-remove-empty-lock";
        const string targetPath = "/config/full-remove-empty-lock/owned.txt";
        const string lockPath = "/config/full-remove-empty-lock/.azureauth-credprovider.fs.lock";
        const string manifestPath = "/state/full-remove-empty-lock-manifest.json";
        var manager = new ConfigurationManager(fileSystem, manifestPath);
        await manager.ApplyAsync(
            CreateGenericFilePlan(ConfigurationChangeOperation.Create, targetPath, "owned-value"),
            TestContext.Current.CancellationToken
        );
        string manifestBefore = fileSystem.ReadAllText(manifestPath);
        fileSystem.WriteAllText(lockPath, string.Empty);
        ConfigurationChangePlan removePlan = CreateGenericFilePlan(
            ConfigurationChangeOperation.Remove,
            targetPath,
            value: null,
            previousOwnedEntryMetadata: HashMetadata("owned-value"),
            previousManifestHash: HashMetadata(manifestBefore)
        );
        fileSystem.Calls.Clear();

        await manager.RemoveAsync(removePlan, TestContext.Current.CancellationToken);

        AssertNoLockArtifactContentReads(fileSystem, lockPath);
        AssertLockArtifactLengthChecked(fileSystem, lockPath);
        Assert.False(fileSystem.DirectoryExists(containerPath));
        Assert.False(fileSystem.FileExists(targetPath));
        Assert.False(fileSystem.FileExists(lockPath));
        Assert.False(fileSystem.FileExists(manifestPath));
    }

    [Fact]
    public async Task FullRemovePreservesExistingContainerWhenLockArtifactIsDirectory()
    {
        var fileSystem = new InMemoryFileSystem(InMemoryPathSemantics.Posix);
        const string containerPath = "/config/full-remove-lock-directory";
        const string targetPath = "/config/full-remove-lock-directory/owned.txt";
        const string lockPath =
            "/config/full-remove-lock-directory/.azureauth-credprovider.fs.lock";
        const string manifestPath = "/state/full-remove-lock-directory-manifest.json";
        var manager = new ConfigurationManager(fileSystem, manifestPath);
        await manager.ApplyAsync(
            CreateGenericFilePlan(ConfigurationChangeOperation.Create, targetPath, "owned-value"),
            TestContext.Current.CancellationToken
        );
        string manifestBefore = fileSystem.ReadAllText(manifestPath);
        fileSystem.DeleteFile(lockPath);
        fileSystem.CreateDirectory(lockPath);
        ConfigurationChangePlan removePlan = CreateGenericFilePlan(
            ConfigurationChangeOperation.Remove,
            targetPath,
            value: null,
            previousOwnedEntryMetadata: HashMetadata("owned-value"),
            previousManifestHash: HashMetadata(manifestBefore)
        );
        fileSystem.Calls.Clear();

        await manager.RemoveAsync(removePlan, TestContext.Current.CancellationToken);

        AssertNoLockArtifactContentReads(fileSystem, lockPath);
        AssertNoLockArtifactLengthChecks(fileSystem, lockPath);
        Assert.True(fileSystem.DirectoryExists(containerPath));
        Assert.True(fileSystem.DirectoryExists(lockPath));
        Assert.False(fileSystem.FileExists(targetPath));
        Assert.False(fileSystem.FileExists(manifestPath));
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task
        FullRemovePreservesExistingContainerWhenLockArtifactIsUnsafeLinkOrReparsePoint(
        bool useSymbolicLink
    )
    {
        var fileSystem = new InMemoryFileSystem(InMemoryPathSemantics.Posix);
        const string containerPath = "/config/full-remove-unsafe-lock";
        const string targetPath = "/config/full-remove-unsafe-lock/owned.txt";
        const string lockPath = "/config/full-remove-unsafe-lock/.azureauth-credprovider.fs.lock";
        const string manifestPath = "/state/full-remove-unsafe-lock-manifest.json";
        var manager = new ConfigurationManager(fileSystem, manifestPath);
        await manager.ApplyAsync(
            CreateGenericFilePlan(ConfigurationChangeOperation.Create, targetPath, "owned-value"),
            TestContext.Current.CancellationToken
        );
        string manifestBefore = fileSystem.ReadAllText(manifestPath);
        if (useSymbolicLink)
        {
            fileSystem.CreateDirectory("/outside");
            fileSystem.WriteAllText("/outside/lock-target", string.Empty);
            fileSystem.AddSymbolicLink(lockPath, "/outside/lock-target");
        }
        else
        {
            fileSystem.WriteAllText(lockPath, string.Empty);
            fileSystem.MarkAsNonSymbolicReparsePoint(lockPath);
        }
        ConfigurationChangePlan removePlan = CreateGenericFilePlan(
            ConfigurationChangeOperation.Remove,
            targetPath,
            value: null,
            previousOwnedEntryMetadata: HashMetadata("owned-value"),
            previousManifestHash: HashMetadata(manifestBefore)
        );
        fileSystem.Calls.Clear();

        await manager.RemoveAsync(removePlan, TestContext.Current.CancellationToken);

        AssertNoLockArtifactContentReads(fileSystem, lockPath);
        AssertNoLockArtifactLengthChecks(fileSystem, lockPath);
        Assert.True(fileSystem.DirectoryExists(containerPath));
        Assert.False(fileSystem.FileExists(targetPath));
        Assert.False(fileSystem.FileExists(manifestPath));
        if (useSymbolicLink)
        {
            Assert.True(fileSystem.IsSymbolicLink(lockPath));
        }
        else
        {
            Assert.True(((IFileSystemReparsePointSafety)fileSystem).IsReparsePoint(lockPath));
        }
    }

    [Fact]
    public async Task FullRemovePreservesExistingContainerWhenLockArtifactIsDanglingLink()
    {
        var fileSystem = new InMemoryFileSystem(InMemoryPathSemantics.Posix);
        const string containerPath = "/config/full-remove-dangling-lock";
        const string targetPath = "/config/full-remove-dangling-lock/owned.txt";
        const string lockPath = "/config/full-remove-dangling-lock/.azureauth-credprovider.fs.lock";
        const string manifestPath = "/state/full-remove-dangling-lock-manifest.json";
        var manager = new ConfigurationManager(fileSystem, manifestPath);
        await manager.ApplyAsync(
            CreateGenericFilePlan(ConfigurationChangeOperation.Create, targetPath, "owned-value"),
            TestContext.Current.CancellationToken
        );
        string manifestBefore = fileSystem.ReadAllText(manifestPath);
        fileSystem.AddSymbolicLink(lockPath, "/missing/full-remove-dangling-lock-target");
        ConfigurationChangePlan removePlan = CreateGenericFilePlan(
            ConfigurationChangeOperation.Remove,
            targetPath,
            value: null,
            previousOwnedEntryMetadata: HashMetadata("owned-value"),
            previousManifestHash: HashMetadata(manifestBefore)
        );
        fileSystem.Calls.Clear();

        await manager.RemoveAsync(removePlan, TestContext.Current.CancellationToken);

        AssertNoLockArtifactContentReads(fileSystem, lockPath);
        AssertNoLockArtifactLengthChecks(fileSystem, lockPath);
        Assert.True(fileSystem.DirectoryExists(containerPath));
        Assert.False(fileSystem.FileExists(targetPath));
        Assert.False(fileSystem.FileExists(manifestPath));
        Assert.True(fileSystem.IsSymbolicLink(lockPath));
    }

    [Theory]
    [InlineData(nameof(IFileSystem.IsSymbolicLink), nameof(NotSupportedException))]
    [InlineData(
        nameof(IFileSystemReparsePointSafety.IsReparsePoint),
        nameof(UnauthorizedAccessException)
    )]
    [InlineData(nameof(IFileSystemFileLength.GetFileLength), nameof(IOException))]
    public async Task FullRemovePreservesExistingContainerWhenLockArtifactSafetyCheckFails(
        string failingOperation,
        string exceptionKind
    )
    {
        var fileSystem = new InMemoryFileSystem(InMemoryPathSemantics.Posix);
        const string containerPath = "/config/full-remove-lock-check-fails";
        const string targetPath = "/config/full-remove-lock-check-fails/owned.txt";
        const string lockPath =
            "/config/full-remove-lock-check-fails/.azureauth-credprovider.fs.lock";
        const string manifestPath = "/state/full-remove-lock-check-fails-manifest.json";
        var manager = new ConfigurationManager(fileSystem, manifestPath);
        await manager.ApplyAsync(
            CreateGenericFilePlan(ConfigurationChangeOperation.Create, targetPath, "owned-value"),
            TestContext.Current.CancellationToken
        );
        string manifestBefore = fileSystem.ReadAllText(manifestPath);
        fileSystem.WriteAllText(lockPath, string.Empty);
        fileSystem.AfterRecord = (call, fs) =>
        {
            if (
                string.Equals(call.Operation, failingOperation, StringComparison.Ordinal)
                && string.Equals(call.Path, lockPath, StringComparison.Ordinal)
            )
            {
                fs.AfterRecord = null;
                fs.FailNextCall(CreateLockArtifactSafetyCheckException(exceptionKind));
            }
        };
        ConfigurationChangePlan removePlan = CreateGenericFilePlan(
            ConfigurationChangeOperation.Remove,
            targetPath,
            value: null,
            previousOwnedEntryMetadata: HashMetadata("owned-value"),
            previousManifestHash: HashMetadata(manifestBefore)
        );
        fileSystem.Calls.Clear();

        await manager.RemoveAsync(removePlan, TestContext.Current.CancellationToken);

        AssertNoLockArtifactContentReads(fileSystem, lockPath);
        Assert.True(fileSystem.DirectoryExists(containerPath));
        Assert.False(fileSystem.FileExists(targetPath));
        Assert.True(fileSystem.FileExists(lockPath));
        Assert.False(fileSystem.FileExists(manifestPath));
    }

    [Fact]
    public async Task
        FullRemovePreservesExistingContainerWhenLockArtifactBecomesNonEmptyBeforeRecursiveDelete()
    {
        var fileSystem = new InMemoryFileSystem(InMemoryPathSemantics.Posix);
        const string containerPath = "/config/full-remove-raced-nonempty-lock";
        const string targetPath = "/config/full-remove-raced-nonempty-lock/owned.txt";
        const string lockPath =
            "/config/full-remove-raced-nonempty-lock/.azureauth-credprovider.fs.lock";
        const string manifestPath = "/state/full-remove-raced-nonempty-lock-manifest.json";
        var manager = new ConfigurationManager(fileSystem, manifestPath);
        await manager.ApplyAsync(
            CreateGenericFilePlan(ConfigurationChangeOperation.Create, targetPath, "owned-value"),
            TestContext.Current.CancellationToken
        );
        string manifestBefore = fileSystem.ReadAllText(manifestPath);
        fileSystem.WriteAllText(lockPath, string.Empty);
        fileSystem.AfterRecord = (call, fs) =>
        {
            if (
                string.Equals(
                    call.Operation,
                    nameof(IFileSystem.DeleteFile),
                    StringComparison.Ordinal
                )
                && string.Equals(call.Path, targetPath, StringComparison.Ordinal)
            )
            {
                fs.AfterRecord = null;
                fs.WriteAllText(lockPath, "raced-lock");
            }
        };
        ConfigurationChangePlan removePlan = CreateGenericFilePlan(
            ConfigurationChangeOperation.Remove,
            targetPath,
            value: null,
            previousOwnedEntryMetadata: HashMetadata("owned-value"),
            previousManifestHash: HashMetadata(manifestBefore)
        );
        fileSystem.Calls.Clear();

        await manager.RemoveAsync(removePlan, TestContext.Current.CancellationToken);

        AssertNoLockArtifactContentReads(fileSystem, lockPath);
        Assert.True(fileSystem.DirectoryExists(containerPath));
        Assert.False(fileSystem.FileExists(targetPath));
        Assert.Equal("raced-lock", fileSystem.ReadAllText(lockPath));
        Assert.False(fileSystem.FileExists(manifestPath));
    }

    [Fact]
    public async Task
        FullRemovePreservesExistingContainerWhenLockArtifactLengthMetadataIsUnsupported()
    {
        var innerFileSystem = new InMemoryFileSystem(InMemoryPathSemantics.Posix);
        var fileSystem = new FileSystemWithoutFileLength(innerFileSystem);
        const string containerPath = "/config/full-remove-no-length-lock";
        const string targetPath = "/config/full-remove-no-length-lock/owned.txt";
        const string lockPath =
            "/config/full-remove-no-length-lock/.azureauth-credprovider.fs.lock";
        const string manifestPath = "/state/full-remove-no-length-lock-manifest.json";
        var manager = new ConfigurationManager(fileSystem, manifestPath);
        await manager.ApplyAsync(
            CreateGenericFilePlan(ConfigurationChangeOperation.Create, targetPath, "owned-value"),
            TestContext.Current.CancellationToken
        );
        string manifestBefore = fileSystem.ReadAllText(manifestPath);
        fileSystem.WriteAllText(lockPath, string.Empty);
        ConfigurationChangePlan removePlan = CreateGenericFilePlan(
            ConfigurationChangeOperation.Remove,
            targetPath,
            value: null,
            previousOwnedEntryMetadata: HashMetadata("owned-value"),
            previousManifestHash: HashMetadata(manifestBefore)
        );
        innerFileSystem.Calls.Clear();

        await manager.RemoveAsync(removePlan, TestContext.Current.CancellationToken);

        AssertNoLockArtifactContentReads(innerFileSystem, lockPath);
        AssertNoLockArtifactLengthChecks(innerFileSystem, lockPath);
        Assert.True(fileSystem.DirectoryExists(containerPath));
        Assert.False(fileSystem.FileExists(targetPath));
        Assert.True(fileSystem.FileExists(lockPath));
        Assert.False(fileSystem.FileExists(manifestPath));
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task
        FullRemovePreservesExistingContainerWhenLockArtifactBecomesUnsafeBeforeRecursiveDelete(
        bool useSymbolicLink
    )
    {
        var fileSystem = new InMemoryFileSystem(InMemoryPathSemantics.Posix);
        const string containerPath = "/config/full-remove-raced-unsafe-lock";
        const string targetPath = "/config/full-remove-raced-unsafe-lock/owned.txt";
        const string lockPath =
            "/config/full-remove-raced-unsafe-lock/.azureauth-credprovider.fs.lock";
        const string manifestPath = "/state/full-remove-raced-unsafe-lock-manifest.json";
        var manager = new ConfigurationManager(fileSystem, manifestPath);
        await manager.ApplyAsync(
            CreateGenericFilePlan(ConfigurationChangeOperation.Create, targetPath, "owned-value"),
            TestContext.Current.CancellationToken
        );
        string manifestBefore = fileSystem.ReadAllText(manifestPath);
        fileSystem.WriteAllText(lockPath, string.Empty);
        fileSystem.AfterRecord = (call, fs) =>
        {
            if (
                string.Equals(
                    call.Operation,
                    nameof(IFileSystem.DeleteFile),
                    StringComparison.Ordinal
                )
                && string.Equals(call.Path, targetPath, StringComparison.Ordinal)
            )
            {
                fs.AfterRecord = null;
                if (useSymbolicLink)
                {
                    fs.DeleteFile(lockPath);
                    fs.CreateDirectory("/outside-raced-unsafe-lock");
                    fs.WriteAllText("/outside-raced-unsafe-lock/lock-target", string.Empty);
                    fs.AddSymbolicLink(lockPath, "/outside-raced-unsafe-lock/lock-target");
                }
                else
                {
                    fs.MarkAsNonSymbolicReparsePoint(lockPath);
                }
            }
        };
        ConfigurationChangePlan removePlan = CreateGenericFilePlan(
            ConfigurationChangeOperation.Remove,
            targetPath,
            value: null,
            previousOwnedEntryMetadata: HashMetadata("owned-value"),
            previousManifestHash: HashMetadata(manifestBefore)
        );

        await manager.RemoveAsync(removePlan, TestContext.Current.CancellationToken);

        Assert.True(fileSystem.DirectoryExists(containerPath));
        Assert.False(fileSystem.FileExists(targetPath));
        Assert.False(fileSystem.FileExists(manifestPath));
        if (useSymbolicLink)
        {
            Assert.True(fileSystem.IsSymbolicLink(lockPath));
        }
        else
        {
            Assert.True(((IFileSystemReparsePointSafety)fileSystem).IsReparsePoint(lockPath));
        }
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task
        FullRemovePreservesContainerWhenDescendantDirectoryBecomesUnsafeBeforeRecursiveDelete(
        bool useSymbolicLink
    )
    {
        var fileSystem = new InMemoryFileSystem(InMemoryPathSemantics.Posix);
        const string containerPath = "/config/full-remove-unsafe-descendant";
        const string targetPath = "/config/full-remove-unsafe-descendant/owned.txt";
        const string unsafeDirectoryPath = "/config/full-remove-unsafe-descendant/unsafe-directory";
        const string manifestPath = "/state/full-remove-unsafe-descendant-manifest.json";
        var manager = new ConfigurationManager(fileSystem, manifestPath);
        await manager.ApplyAsync(
            CreateGenericFilePlan(ConfigurationChangeOperation.Create, targetPath, "owned-value"),
            TestContext.Current.CancellationToken
        );
        string manifestBefore = fileSystem.ReadAllText(manifestPath);
        fileSystem.AfterRecord = (call, fs) =>
        {
            if (
                string.Equals(
                    call.Operation,
                    nameof(IFileSystem.DeleteFile),
                    StringComparison.Ordinal
                )
                && string.Equals(call.Path, targetPath, StringComparison.Ordinal)
            )
            {
                fs.AfterRecord = null;
                if (useSymbolicLink)
                {
                    fs.CreateDirectory("/outside-unsafe-descendant");
                    fs.AddSymbolicLink(unsafeDirectoryPath, "/outside-unsafe-descendant");
                }
                else
                {
                    fs.CreateDirectory(unsafeDirectoryPath);
                    fs.MarkAsNonSymbolicReparsePoint(unsafeDirectoryPath);
                }
            }
        };
        ConfigurationChangePlan removePlan = CreateGenericFilePlan(
            ConfigurationChangeOperation.Remove,
            targetPath,
            value: null,
            previousOwnedEntryMetadata: HashMetadata("owned-value"),
            previousManifestHash: HashMetadata(manifestBefore)
        );

        await manager.RemoveAsync(removePlan, TestContext.Current.CancellationToken);

        Assert.True(fileSystem.DirectoryExists(containerPath));
        Assert.False(fileSystem.FileExists(targetPath));
        Assert.False(fileSystem.FileExists(manifestPath));
        if (useSymbolicLink)
        {
            Assert.True(fileSystem.IsSymbolicLink(unsafeDirectoryPath));
        }
        else
        {
            Assert.True(
                ((IFileSystemReparsePointSafety)fileSystem).IsReparsePoint(unsafeDirectoryPath)
            );
        }
    }

    [Theory]
    [InlineData("/config/full-remove-dangling-descendant/.dangling.tmp")]
    [InlineData("/config/full-remove-dangling-descendant/dangling-directory")]
    public async Task FullRemoveRejectsContainerWhenDescendantDanglingLinkExistsDuringSnapshot(
        string danglingPath
    )
    {
        var fileSystem = new InMemoryFileSystem(InMemoryPathSemantics.Posix);
        const string containerPath = "/config/full-remove-dangling-descendant";
        const string targetPath = "/config/full-remove-dangling-descendant/owned.txt";
        const string manifestPath = "/state/full-remove-dangling-descendant-manifest.json";
        var manager = new ConfigurationManager(fileSystem, manifestPath);
        await manager.ApplyAsync(
            CreateGenericFilePlan(ConfigurationChangeOperation.Create, targetPath, "owned-value"),
            TestContext.Current.CancellationToken
        );
        string manifestBefore = fileSystem.ReadAllText(manifestPath);
        fileSystem.AddSymbolicLink(danglingPath, "/missing/full-remove-dangling-descendant-target");
        ConfigurationChangePlan removePlan = CreateGenericFilePlan(
            ConfigurationChangeOperation.Remove,
            targetPath,
            value: null,
            previousOwnedEntryMetadata: HashMetadata("owned-value"),
            previousManifestHash: HashMetadata(manifestBefore)
        );

        await Assert.ThrowsAsync<NotSupportedException>(async () =>
            await manager.RemoveAsync(removePlan, TestContext.Current.CancellationToken)
        );

        Assert.True(fileSystem.DirectoryExists(containerPath));
        Assert.Equal("owned-value", fileSystem.ReadAllText(targetPath));
        Assert.Equal(manifestBefore, fileSystem.ReadAllText(manifestPath));
        Assert.True(fileSystem.IsSymbolicLink(danglingPath));
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task
        ApplyRollbackPreservesCreatedDirectoryWhenDescendantDirectoryBecomesUnsafeBeforeEmptyDelete(
        bool useSymbolicLink
    )
    {
        var fileSystem = new FailOnAtomicWriteFileSystem(InMemoryPathSemantics.Posix)
        {
            FailOnAtomicWriteNumber = 2,
        };
        const string containerPath = "/config/rollback-unsafe-descendant";
        const string createdDirectoryPath = "/config/rollback-unsafe-descendant/created";
        const string targetPath = "/config/rollback-unsafe-descendant/created/owned.txt";
        const string unsafeDirectoryPath =
            "/config/rollback-unsafe-descendant/created/unsafe-directory";
        const string manifestPath = "/state/rollback-unsafe-descendant-manifest.json";
        fileSystem.CreateDirectory(containerPath);
        fileSystem.AfterRecord = (call, fs) =>
        {
            if (
                string.Equals(
                    call.Operation,
                    nameof(IFileSystem.DeleteFile),
                    StringComparison.Ordinal
                )
                && string.Equals(call.Path, targetPath, StringComparison.Ordinal)
            )
            {
                fs.AfterRecord = null;
                if (useSymbolicLink)
                {
                    fs.CreateDirectory("/outside-rollback-unsafe-descendant");
                    fs.AddSymbolicLink(unsafeDirectoryPath, "/outside-rollback-unsafe-descendant");
                }
                else
                {
                    fs.CreateDirectory(unsafeDirectoryPath);
                    fs.MarkAsNonSymbolicReparsePoint(unsafeDirectoryPath);
                }
            }
        };
        var manager = new ConfigurationManager(fileSystem, manifestPath);

        await Assert.ThrowsAnyAsync<IOException>(async () =>
            await manager.ApplyAsync(
                CreateGenericFilePlan(
                    ConfigurationChangeOperation.Create,
                    targetPath,
                    "owned-value"
                ),
                TestContext.Current.CancellationToken
            )
        );

        Assert.True(fileSystem.DirectoryExists(containerPath));
        Assert.True(fileSystem.DirectoryExists(createdDirectoryPath));
        Assert.False(fileSystem.FileExists(targetPath));
        Assert.False(fileSystem.FileExists(manifestPath));
        if (useSymbolicLink)
        {
            Assert.True(fileSystem.IsSymbolicLink(unsafeDirectoryPath));
        }
        else
        {
            Assert.True(
                ((IFileSystemReparsePointSafety)fileSystem).IsReparsePoint(unsafeDirectoryPath)
            );
        }
    }

    [Fact]
    public async Task FullRemoveAndReapplyUseSameStableLifecycleLockOutsideTemporaryContainer()
    {
        var fileSystem = new InMemoryFileSystem(InMemoryPathSemantics.Posix);
        const string containerPath = "/config/full-remove-reapply-lock";
        const string targetPath = "/config/full-remove-reapply-lock/owned.txt";
        const string manifestPath = "/state/full-remove-reapply-lock-manifest.json";
        var manager = new ConfigurationManager(fileSystem, manifestPath);
        await manager.ApplyAsync(
            CreateGenericFilePlan(ConfigurationChangeOperation.Create, targetPath, "owned-value"),
            TestContext.Current.CancellationToken
        );
        string manifestBefore = fileSystem.ReadAllText(manifestPath);
        ConfigurationChangePlan removePlan = CreateGenericFilePlan(
            ConfigurationChangeOperation.Remove,
            targetPath,
            value: null,
            previousOwnedEntryMetadata: HashMetadata("owned-value"),
            previousManifestHash: HashMetadata(manifestBefore)
        );

        await manager.RemoveAsync(removePlan, TestContext.Current.CancellationToken);
        Assert.False(fileSystem.DirectoryExists(containerPath));
        await manager.ApplyAsync(
            CreateGenericFilePlan(ConfigurationChangeOperation.Create, targetPath, "new-value"),
            TestContext.Current.CancellationToken
        );

        string lifecycleLockPath = Assert.Single(GetLifecycleLockPaths(fileSystem));
        Assert.StartsWith("/state/.azureauth-credprovider.lifecycle-locks/", lifecycleLockPath);
        Assert.False(lifecycleLockPath.StartsWith(containerPath + "/", StringComparison.Ordinal));
        Assert.True(fileSystem.DirectoryExists(lifecycleLockPath));
        Assert.Equal("new-value", fileSystem.ReadAllText(targetPath));
    }

    [Fact]
    public async Task DryRunAsyncAcquiresLifecycleLockBeforeReadingFilesystemState()
    {
        var fileSystem = new InMemoryFileSystem(InMemoryPathSemantics.Posix);
        const string targetPath = "/config/dry-run-lifecycle-lock/owned.txt";
        const string manifestPath = "/state/dry-run-lifecycle-lock-manifest.json";
        var manager = new ConfigurationManager(fileSystem, manifestPath);
        ConfigurationChangePlan plan = CreateGenericFilePlan(
            ConfigurationChangeOperation.Create,
            targetPath,
            "owned-value"
        );
        string lifecycleLockPath = ConfigurationManager.CreateConfigurationExecutionLockDirectory(
            fileSystem,
            fileSystem.GetFullPath(plan.TemporaryContainer!.ProductOwnedPath),
            manifestPath
        );
        using IDisposable heldLifecycleLock = (
            (IFileSystemMutationLock)fileSystem
        ).AcquireMutationLock(lifecycleLockPath);
        fileSystem.Calls.Clear();

        await Assert.ThrowsAsync<IOException>(async () =>
            await manager.DryRunAsync(plan, TestContext.Current.CancellationToken)
        );

        AssertLifecycleLockWasAttempted(fileSystem.Calls, lifecycleLockPath);
        AssertNoFilesystemStateReadCallsBeforeLockAcquisition(fileSystem.Calls);
    }

    [Fact]
    public async Task ApplyAsyncAcquiresLifecycleLockBeforeReadingFilesystemState()
    {
        var fileSystem = new InMemoryFileSystem(InMemoryPathSemantics.Posix);
        const string targetPath = "/config/apply-lifecycle-lock/owned.txt";
        const string manifestPath = "/state/apply-lifecycle-lock-manifest.json";
        var manager = new ConfigurationManager(fileSystem, manifestPath);
        ConfigurationChangePlan plan = CreateGenericFilePlan(
            ConfigurationChangeOperation.Create,
            targetPath,
            "owned-value"
        );
        string lifecycleLockPath = ConfigurationManager.CreateConfigurationExecutionLockDirectory(
            fileSystem,
            fileSystem.GetFullPath(plan.TemporaryContainer!.ProductOwnedPath),
            manifestPath
        );
        using IDisposable heldLifecycleLock = (
            (IFileSystemMutationLock)fileSystem
        ).AcquireMutationLock(lifecycleLockPath);
        fileSystem.Calls.Clear();

        await Assert.ThrowsAsync<IOException>(async () =>
            await manager.ApplyAsync(plan, TestContext.Current.CancellationToken)
        );

        AssertLifecycleLockWasAttempted(fileSystem.Calls, lifecycleLockPath);
        AssertNoFilesystemStateReadCallsBeforeLockAcquisition(fileSystem.Calls);
    }

    [Fact]
    public async Task RemoveAsyncAcquiresLifecycleLockBeforeReadingFilesystemState()
    {
        var fileSystem = new InMemoryFileSystem(InMemoryPathSemantics.Posix);
        const string targetPath = "/config/remove-lifecycle-lock/owned.txt";
        const string manifestPath = "/state/remove-lifecycle-lock-manifest.json";
        var manager = new ConfigurationManager(fileSystem, manifestPath);
        await manager.ApplyAsync(
            CreateGenericFilePlan(ConfigurationChangeOperation.Create, targetPath, "owned-value"),
            TestContext.Current.CancellationToken
        );
        string manifestBefore = fileSystem.ReadAllText(manifestPath);
        ConfigurationChangePlan removePlan = CreateGenericFilePlan(
            ConfigurationChangeOperation.Remove,
            targetPath,
            value: null,
            previousOwnedEntryMetadata: HashMetadata("owned-value"),
            previousManifestHash: HashMetadata(manifestBefore)
        );
        string lifecycleLockPath = ConfigurationManager.CreateConfigurationExecutionLockDirectory(
            fileSystem,
            fileSystem.GetFullPath(removePlan.TemporaryContainer!.ProductOwnedPath),
            manifestPath
        );
        using IDisposable heldLifecycleLock = (
            (IFileSystemMutationLock)fileSystem
        ).AcquireMutationLock(lifecycleLockPath);
        fileSystem.Calls.Clear();

        await Assert.ThrowsAsync<IOException>(async () =>
            await manager.RemoveAsync(removePlan, TestContext.Current.CancellationToken)
        );

        AssertLifecycleLockWasAttempted(fileSystem.Calls, lifecycleLockPath);
        AssertNoFilesystemStateReadCallsBeforeLockAcquisition(fileSystem.Calls);
    }

    [Fact]
    public async Task
        FilesystemBackedDryRunRejectsUnsupportedConditionalFileMutationsBeforeReadingState()
    {
        var fileSystem = new InMemoryFileSystem(InMemoryPathSemantics.Posix)
        {
            SupportsConditionalFileMutations = false,
        };
        const string targetPath = "/config/unsupported-conditional-dry-run/owned.txt";
        const string manifestPath = "/state/unsupported-conditional-dry-run-manifest.json";
        var manager = new ConfigurationManager(fileSystem, manifestPath);
        ConfigurationChangePlan plan = CreateGenericFilePlan(
            ConfigurationChangeOperation.Create,
            targetPath,
            "owned-value"
        );

        var exception = await Assert.ThrowsAsync<PlatformNotSupportedException>(async () =>
            await manager.DryRunAsync(plan, TestContext.Current.CancellationToken)
        );

        Assert.Contains(
            "conditional file mutation",
            exception.Message,
            StringComparison.OrdinalIgnoreCase
        );
        Assert.DoesNotContain(
            fileSystem.Calls,
            call =>
                string.Equals(
                    call.Operation,
                    nameof(IFileSystemMutationLock.AcquireMutationLock),
                    StringComparison.Ordinal
                )
                || string.Equals(
                    call.Operation,
                    nameof(IFileSystem.FileExists),
                    StringComparison.Ordinal
                )
                || string.Equals(
                    call.Operation,
                    nameof(IFileSystem.DirectoryExists),
                    StringComparison.Ordinal
                )
                || string.Equals(
                    call.Operation,
                    nameof(IFileSystem.ReadAllText),
                    StringComparison.Ordinal
                )
                || string.Equals(
                    call.Operation,
                    nameof(IFileSystem.IsSymbolicLink),
                    StringComparison.Ordinal
                )
                || string.Equals(
                    call.Operation,
                    nameof(IFileSystemReparsePointSafety.IsReparsePoint),
                    StringComparison.Ordinal
                )
                || string.Equals(
                    call.Operation,
                    nameof(IFileSystem.CaptureFileIntegritySnapshot),
                    StringComparison.Ordinal
                )
                || string.Equals(
                    call.Operation,
                    nameof(IFileSystem.CaptureTrustedParentDirectorySnapshots),
                    StringComparison.Ordinal
                )
                || string.Equals(
                    call.Operation,
                    nameof(IFileSystem.ComputeSha256Hash),
                    StringComparison.Ordinal
                )
        );
        Assert.False(fileSystem.FileExists(targetPath));
        Assert.False(fileSystem.FileExists(manifestPath));
    }

    [Fact]
    public void LifecycleLockDirectoryPreservesPosixSeparatorsForPosixFileSystemPaths()
    {
        var fileSystem = new InMemoryFileSystem(InMemoryPathSemantics.Posix);

        string lockDirectory = ConfigurationManager.CreateConfigurationExecutionLockDirectory(
            fileSystem,
            fileSystem.GetFullPath("/config/product"),
            "/state/manifest.json"
        );

        Assert.StartsWith(
            "/state/.azureauth-credprovider.lifecycle-locks/",
            lockDirectory,
            StringComparison.Ordinal
        );
        Assert.DoesNotContain('\\', lockDirectory);
    }

    [Fact]
    public void LifecycleLockDirectoryPreservesWindowsSeparatorsForWindowsStylePaths()
    {
        var fileSystem = new PassThroughFileSystem();

        string lockDirectory = ConfigurationManager.CreateConfigurationExecutionLockDirectory(
            fileSystem,
            @"C:\config\product",
            @"C:\state\manifest.json"
        );

        Assert.StartsWith(
            @"C:\state\.azureauth-credprovider.lifecycle-locks\",
            lockDirectory,
            StringComparison.OrdinalIgnoreCase
        );
        Assert.DoesNotContain('/', lockDirectory);
    }

    [Theory]
    [InlineData(
        "/config/product",
        "/config/product/state/manifest.json",
        "/config/.azureauth-credprovider.lifecycle-locks/",
        '/'
    )]
    [InlineData(
        @"C:\config\product",
        @"C:\config\product\state\manifest.json",
        @"C:\config\.azureauth-credprovider.lifecycle-locks\",
        '\\'
    )]
    [InlineData(
        @"\\server\share\config\product",
        @"\\server\share\config\product\state\manifest.json",
        @"\\server\share\config\.azureauth-credprovider.lifecycle-locks\",
        '\\'
    )]
    public void LifecycleLockDirectoryFallsBackOutsideProductOwnedContainerUsingLifecyclePathStyle(
        string productOwnedPath,
        string manifestPath,
        string expectedPrefix,
        char directorySeparator
    )
    {
        var fileSystem = new PassThroughFileSystem();

        string lockDirectory = ConfigurationManager.CreateConfigurationExecutionLockDirectory(
            fileSystem,
            productOwnedPath,
            manifestPath
        );

        Assert.StartsWith(expectedPrefix, lockDirectory, StringComparison.Ordinal);
        Assert.False(
            lockDirectory.StartsWith(
                productOwnedPath + directorySeparator,
                StringComparison.Ordinal
            )
        );
        if (directorySeparator == '\\')
        {
            Assert.DoesNotContain('/', lockDirectory);
        }
        else
        {
            Assert.DoesNotContain('\\', lockDirectory);
        }
    }

    [Fact]
    public void LifecycleLockNameNormalizesCaseWhenPathIdentityIsCaseInsensitive()
    {
        string lockName = ConfigurationManager.CreateLifecycleLockName(
            "/state/Manifest.json",
            "/config/Product",
            StringComparison.OrdinalIgnoreCase
        );
        string caseVariantLockName = ConfigurationManager.CreateLifecycleLockName(
            "/STATE/manifest.JSON/",
            "/CONFIG/product/",
            StringComparison.OrdinalIgnoreCase
        );

        Assert.Equal(lockName, caseVariantLockName);
    }

    [Fact]
    public void LifecycleLockNameTrimsWindowsStyleTrailingSeparators()
    {
        string lockName = ConfigurationManager.CreateLifecycleLockName(
            @"C:\state\manifest.json",
            @"C:\config\product",
            StringComparison.OrdinalIgnoreCase
        );
        string trailingSeparatorVariantLockName = ConfigurationManager.CreateLifecycleLockName(
            @"C:\state\manifest.json\",
            @"C:\config\product\",
            StringComparison.OrdinalIgnoreCase
        );

        Assert.Equal(lockName, trailingSeparatorVariantLockName);
    }

    [Fact]
    public void LifecycleLockNameKeepsCaseWhenPathIdentityIsCaseSensitive()
    {
        string lockName = ConfigurationManager.CreateLifecycleLockName(
            "/state/manifest.json",
            "/config/product",
            StringComparison.Ordinal
        );
        string trailingSeparatorVariantLockName = ConfigurationManager.CreateLifecycleLockName(
            "/state/manifest.json/",
            "/config/product/",
            StringComparison.Ordinal
        );
        string caseVariantLockName = ConfigurationManager.CreateLifecycleLockName(
            "/STATE/manifest.JSON",
            "/CONFIG/product",
            StringComparison.Ordinal
        );

        Assert.Equal(lockName, trailingSeparatorVariantLockName);
        Assert.NotEqual(lockName, caseVariantLockName);
    }

    [Fact]
    public async Task FullRemoveContainerCleanupPreventsInterleavedApplyFromRecreatingContent()
    {
        var fileSystem = new InMemoryFileSystem(InMemoryPathSemantics.Posix);
        const string containerPath = "/config/full-remove-interleaved";
        const string targetPath = "/config/full-remove-interleaved/owned.txt";
        const string manifestPath = "/state/full-remove-interleaved-manifest.json";
        var manager = new ConfigurationManager(fileSystem, manifestPath);
        await manager.ApplyAsync(
            CreateGenericFilePlan(ConfigurationChangeOperation.Create, targetPath, "owned-value"),
            TestContext.Current.CancellationToken
        );
        string manifestBefore = fileSystem.ReadAllText(manifestPath);
        ConfigurationChangePlan removePlan = CreateGenericFilePlan(
            ConfigurationChangeOperation.Remove,
            targetPath,
            value: null,
            previousOwnedEntryMetadata: HashMetadata("owned-value"),
            previousManifestHash: HashMetadata(manifestBefore)
        );
        ConfigurationChangePlan interleavedApplyPlan = CreateGenericFilePlan(
            ConfigurationChangeOperation.Create,
            targetPath,
            "new-value"
        );
        Exception? interleavedApplyException = null;
        fileSystem.AfterRecord = (call, fs) =>
        {
            if (
                string.Equals(
                    call.Operation,
                    nameof(IFileSystem.DeleteDirectory),
                    StringComparison.Ordinal
                )
                && string.Equals(call.Path, containerPath, StringComparison.Ordinal)
            )
            {
                fs.AfterRecord = null;
                interleavedApplyException = Record.Exception(() =>
                    manager
                        .ApplyAsync(interleavedApplyPlan, TestContext.Current.CancellationToken)
                        .AsTask()
                        .GetAwaiter()
                        .GetResult()
                );
            }
        };

        await manager.RemoveAsync(removePlan, TestContext.Current.CancellationToken);

        Assert.NotNull(interleavedApplyException);
        Assert.Contains(
            "lock",
            interleavedApplyException.Message,
            StringComparison.OrdinalIgnoreCase
        );
        Assert.False(fileSystem.DirectoryExists(containerPath));
        Assert.False(fileSystem.FileExists(targetPath));
        Assert.False(fileSystem.FileExists(manifestPath));
    }

    [Fact]
    public async Task RollbackCleanupSkipsContainerAfterSymlinkSwapBeforeEnumeration()
    {
        var fileSystem = new InMemoryFileSystem(InMemoryPathSemantics.Posix);
        const string containerPath = "/config/rollback-symlink-swap";
        const string targetPath = "/config/rollback-symlink-swap/owned.txt";
        const string outsidePath = "/outside/rollback-symlink-swap";
        const string externalFilePath = "/outside/rollback-symlink-swap/external.txt";
        const string manifestPath = "/state/rollback-symlink-swap-manifest.json";
        fileSystem.CreateDirectory(outsidePath);
        fileSystem.WriteAllText(externalFilePath, "external");
        var manager = new ConfigurationManager(fileSystem, manifestPath);
        bool targetWriteSeen = false;
        bool armSwap = false;
        fileSystem.AfterRecord = (call, fs) =>
        {
            if (
                string.Equals(
                    call.Operation,
                    nameof(IFileSystem.AtomicWriteAllText),
                    StringComparison.Ordinal
                )
                && string.Equals(call.Path, targetPath, StringComparison.Ordinal)
            )
            {
                targetWriteSeen = true;
                return;
            }

            if (
                targetWriteSeen
                && string.Equals(
                    call.Operation,
                    nameof(IFileSystem.FileExists),
                    StringComparison.Ordinal
                )
                && string.Equals(call.Path, manifestPath, StringComparison.Ordinal)
            )
            {
                fs.FailNextCall(new IOException("Injected manifest validation failure."));
                return;
            }

            if (
                string.Equals(
                    call.Operation,
                    nameof(IFileSystem.DeleteFile),
                    StringComparison.Ordinal
                )
                && string.Equals(call.Path, targetPath, StringComparison.Ordinal)
            )
            {
                armSwap = true;
                return;
            }

            if (
                armSwap
                && string.Equals(
                    call.Operation,
                    nameof(IFileSystem.DirectoryExists),
                    StringComparison.Ordinal
                )
                && string.Equals(call.Path, containerPath, StringComparison.Ordinal)
            )
            {
                fs.AfterRecord = null;
                fs.DeleteDirectory(containerPath);
                fs.AddSymbolicLink(containerPath, outsidePath);
            }
        };

        await Assert.ThrowsAsync<IOException>(async () =>
            await manager.ApplyAsync(
                CreateGenericFilePlan(
                    ConfigurationChangeOperation.Create,
                    targetPath,
                    "owned-value"
                ),
                TestContext.Current.CancellationToken
            )
        );

        Assert.Equal("external", fileSystem.ReadAllText(externalFilePath));
        Assert.True(fileSystem.IsSymbolicLink(containerPath));
        Assert.False(fileSystem.FileExists(targetPath));
        Assert.False(fileSystem.FileExists(manifestPath));
    }

    [Fact]
    public async Task
        RollbackCleanupSkipsContainerAfterNonSymbolicReparsePointSwapBeforeEnumeration()
    {
        var fileSystem = new InMemoryFileSystem(InMemoryPathSemantics.Posix);
        const string containerPath = "/config/rollback-reparse-point-swap";
        const string targetPath = "/config/rollback-reparse-point-swap/owned.txt";
        const string manifestPath = "/state/rollback-reparse-point-swap-manifest.json";
        var manager = new ConfigurationManager(fileSystem, manifestPath);
        bool targetWriteSeen = false;
        bool armSwap = false;
        fileSystem.AfterRecord = (call, fs) =>
        {
            if (
                string.Equals(
                    call.Operation,
                    nameof(IFileSystem.AtomicWriteAllText),
                    StringComparison.Ordinal
                )
                && string.Equals(call.Path, targetPath, StringComparison.Ordinal)
            )
            {
                targetWriteSeen = true;
                return;
            }

            if (
                targetWriteSeen
                && string.Equals(
                    call.Operation,
                    nameof(IFileSystem.FileExists),
                    StringComparison.Ordinal
                )
                && string.Equals(call.Path, manifestPath, StringComparison.Ordinal)
            )
            {
                fs.FailNextCall(new IOException("Injected manifest validation failure."));
                return;
            }

            if (
                string.Equals(
                    call.Operation,
                    nameof(IFileSystem.DeleteFile),
                    StringComparison.Ordinal
                )
                && string.Equals(call.Path, targetPath, StringComparison.Ordinal)
            )
            {
                armSwap = true;
                return;
            }

            if (
                armSwap
                && string.Equals(
                    call.Operation,
                    nameof(IFileSystem.DirectoryExists),
                    StringComparison.Ordinal
                )
                && string.Equals(call.Path, containerPath, StringComparison.Ordinal)
            )
            {
                fs.AfterRecord = null;
                fs.MarkAsNonSymbolicReparsePoint(containerPath);
            }
        };

        await Assert.ThrowsAsync<IOException>(async () =>
            await manager.ApplyAsync(
                CreateGenericFilePlan(
                    ConfigurationChangeOperation.Create,
                    targetPath,
                    "owned-value"
                ),
                TestContext.Current.CancellationToken
            )
        );

        Assert.True(fileSystem.DirectoryExists(containerPath));
        Assert.True(((IFileSystemReparsePointSafety)fileSystem).IsReparsePoint(containerPath));
        Assert.False(fileSystem.FileExists(targetPath));
        Assert.False(fileSystem.FileExists(manifestPath));
    }

    [Fact]
    public async Task RollbackCleanupSkipsContainerAfterAncestorSymlinkSwapBeforeEnumeration()
    {
        var fileSystem = new InMemoryFileSystem(InMemoryPathSemantics.Posix);
        const string ancestorPath = "/config";
        const string containerPath = "/config/rollback-ancestor-symlink-swap";
        const string targetPath = "/config/rollback-ancestor-symlink-swap/owned.txt";
        const string outsidePath = "/outside";
        const string externalContainerPath = "/outside/rollback-ancestor-symlink-swap";
        const string externalFilePath = "/outside/rollback-ancestor-symlink-swap/.external.tmp";
        const string manifestPath = "/state/rollback-ancestor-symlink-swap-manifest.json";
        fileSystem.CreateDirectory(externalContainerPath);
        fileSystem.WriteAllText(externalFilePath, "external");
        var manager = new ConfigurationManager(fileSystem, manifestPath);
        bool targetWriteSeen = false;
        bool armSwap = false;
        fileSystem.AfterRecord = (call, fs) =>
        {
            if (
                string.Equals(
                    call.Operation,
                    nameof(IFileSystem.AtomicWriteAllText),
                    StringComparison.Ordinal
                )
                && string.Equals(call.Path, targetPath, StringComparison.Ordinal)
            )
            {
                targetWriteSeen = true;
                return;
            }

            if (
                targetWriteSeen
                && string.Equals(
                    call.Operation,
                    nameof(IFileSystem.FileExists),
                    StringComparison.Ordinal
                )
                && string.Equals(call.Path, manifestPath, StringComparison.Ordinal)
            )
            {
                fs.FailNextCall(new IOException("Injected manifest validation failure."));
                return;
            }

            if (
                string.Equals(
                    call.Operation,
                    nameof(IFileSystem.DeleteFile),
                    StringComparison.Ordinal
                )
                && string.Equals(call.Path, targetPath, StringComparison.Ordinal)
            )
            {
                armSwap = true;
                return;
            }

            if (
                armSwap
                && string.Equals(
                    call.Operation,
                    nameof(IFileSystem.DirectoryExists),
                    StringComparison.Ordinal
                )
                && string.Equals(call.Path, containerPath, StringComparison.Ordinal)
            )
            {
                fs.AfterRecord = null;
                fs.DeleteDirectory(containerPath);
                fs.DeleteDirectory(ancestorPath);
                fs.AddSymbolicLink(ancestorPath, outsidePath);
            }
        };

        await Assert.ThrowsAsync<IOException>(async () =>
            await manager.ApplyAsync(
                CreateGenericFilePlan(
                    ConfigurationChangeOperation.Create,
                    targetPath,
                    "owned-value"
                ),
                TestContext.Current.CancellationToken
            )
        );

        Assert.Equal("external", fileSystem.ReadAllText(externalFilePath));
        Assert.True(fileSystem.IsSymbolicLink(ancestorPath));
        Assert.False(fileSystem.FileExists(targetPath));
        Assert.False(fileSystem.FileExists(manifestPath));
    }

    [Fact]
    public async Task ApplyRollbackPreservesPreExistingTemporaryContainerContent()
    {
        var fileSystem = new FailOnAtomicWriteFileSystem(InMemoryPathSemantics.Posix)
        {
            FailOnAtomicWriteNumber = 2,
        };
        const string containerPath = "/config/rollback-existing";
        const string existingPath = "/config/rollback-existing/existing.txt";
        const string lockPath = "/config/rollback-existing/.azureauth-credprovider.fs.lock";
        const string nestedPath = "/config/rollback-existing/nested/owned.txt";
        const string manifestPath = "/state/rollback-existing-manifest.json";
        fileSystem.CreateDirectory(containerPath);
        fileSystem.AtomicWriteAllText(existingPath, "pre-existing");
        fileSystem.WriteAllText(lockPath, "existing-lock");
        fileSystem.ResetAtomicWriteCount();
        var manager = new ConfigurationManager(fileSystem, manifestPath);

        await Assert.ThrowsAnyAsync<IOException>(async () =>
            await manager.ApplyAsync(
                CreateGenericFilePlan(
                    ConfigurationChangeOperation.Create,
                    nestedPath,
                    "owned-value"
                ),
                TestContext.Current.CancellationToken
            )
        );

        Assert.True(fileSystem.DirectoryExists(containerPath));
        Assert.Equal("pre-existing", fileSystem.ReadAllText(existingPath));
        Assert.Equal("existing-lock", fileSystem.ReadAllText(lockPath));
        Assert.False(fileSystem.FileExists(nestedPath));
        Assert.False(fileSystem.DirectoryExists("/config/rollback-existing/nested"));
        Assert.False(fileSystem.FileExists(manifestPath));
    }

    [Fact]
    public async Task ApplyRollbackDeletesNewEmptyLockFileBeforeDeletingNewNestedDirectory()
    {
        var fileSystem = new FailOnAtomicWriteFileSystem(InMemoryPathSemantics.Posix)
        {
            FailOnAtomicWriteNumber = 2,
        };
        const string containerPath = "/config/rollback-existing-new-lock";
        const string nestedDirectoryPath = "/config/rollback-existing-new-lock/nested";
        const string targetPath = "/config/rollback-existing-new-lock/nested/owned.txt";
        const string lockPath =
            "/config/rollback-existing-new-lock/nested/.azureauth-credprovider.fs.lock";
        const string manifestPath = "/state/rollback-existing-new-lock-manifest.json";
        fileSystem.CreateDirectory(containerPath);
        fileSystem.AfterRecord = (call, fs) =>
        {
            if (
                string.Equals(
                    call.Operation,
                    nameof(IFileSystem.AtomicWriteAllText),
                    StringComparison.Ordinal
                )
                && string.Equals(call.Path, manifestPath, StringComparison.Ordinal)
                && fs.AtomicWriteCount == 2
            )
            {
                fs.AfterRecord = null;
                fs.WriteAllText(lockPath, string.Empty);
            }
        };
        var manager = new ConfigurationManager(fileSystem, manifestPath);
        ConfigurationChangePlan plan = CreateGenericFilePlan(
            ConfigurationChangeOperation.Create,
            targetPath,
            "owned-value"
        ) with
        {
            TemporaryContainer = CreateTemporaryHomeContainer(containerPath),
        };

        await Assert.ThrowsAnyAsync<IOException>(async () =>
            await manager.ApplyAsync(plan, TestContext.Current.CancellationToken)
        );

        Assert.True(fileSystem.DirectoryExists(containerPath));
        Assert.False(fileSystem.FileExists(targetPath));
        Assert.False(fileSystem.FileExists(lockPath));
        Assert.False(fileSystem.DirectoryExists(nestedDirectoryPath));
        Assert.False(fileSystem.FileExists(manifestPath));
    }

    [Fact]
    public async Task ApplyRollbackPreservesNewNonEmptyLockFileAndNestedDirectory()
    {
        var fileSystem = new FailOnAtomicWriteFileSystem(InMemoryPathSemantics.Posix)
        {
            FailOnAtomicWriteNumber = 2,
        };
        const string containerPath = "/config/rollback-existing-new-non-empty-lock";
        const string nestedDirectoryPath = "/config/rollback-existing-new-non-empty-lock/nested";
        const string targetPath =
            "/config/rollback-existing-new-non-empty-lock/nested/owned.txt";
        const string lockPath =
            "/config/rollback-existing-new-non-empty-lock/nested/.azureauth-credprovider.fs.lock";
        const string manifestPath = "/state/rollback-existing-new-non-empty-lock-manifest.json";
        fileSystem.CreateDirectory(containerPath);
        fileSystem.AfterRecord = (call, fs) =>
        {
            if (
                string.Equals(
                    call.Operation,
                    nameof(IFileSystem.AtomicWriteAllText),
                    StringComparison.Ordinal
                )
                && string.Equals(call.Path, manifestPath, StringComparison.Ordinal)
                && fs.AtomicWriteCount == 2
            )
            {
                fs.AfterRecord = null;
                fs.WriteAllText(lockPath, "new-lock");
            }
        };
        var manager = new ConfigurationManager(fileSystem, manifestPath);
        ConfigurationChangePlan plan = CreateGenericFilePlan(
            ConfigurationChangeOperation.Create,
            targetPath,
            "owned-value"
        ) with
        {
            TemporaryContainer = CreateTemporaryHomeContainer(containerPath),
        };

        await Assert.ThrowsAnyAsync<IOException>(async () =>
            await manager.ApplyAsync(plan, TestContext.Current.CancellationToken)
        );

        Assert.True(fileSystem.DirectoryExists(containerPath));
        Assert.False(fileSystem.FileExists(targetPath));
        Assert.True(fileSystem.DirectoryExists(nestedDirectoryPath));
        Assert.Equal("new-lock", fileSystem.ReadAllText(lockPath));
        Assert.False(fileSystem.FileExists(manifestPath));
    }

    [Fact]
    public async Task ApplyRollbackPreservesPreExistingNestedLockFile()
    {
        var fileSystem = new FailOnAtomicWriteFileSystem(InMemoryPathSemantics.Posix)
        {
            FailOnAtomicWriteNumber = 2,
        };
        const string containerPath = "/config/rollback-existing-nested-lock";
        const string nestedDirectoryPath = "/config/rollback-existing-nested-lock/nested";
        const string targetPath = "/config/rollback-existing-nested-lock/nested/owned.txt";
        const string lockPath =
            "/config/rollback-existing-nested-lock/nested/.azureauth-credprovider.fs.lock";
        const string manifestPath = "/state/rollback-existing-nested-lock-manifest.json";
        fileSystem.CreateDirectory(nestedDirectoryPath);
        fileSystem.WriteAllText(lockPath, "existing-lock");
        var manager = new ConfigurationManager(fileSystem, manifestPath);
        ConfigurationChangePlan plan = CreateGenericFilePlan(
            ConfigurationChangeOperation.Create,
            targetPath,
            "owned-value"
        ) with
        {
            TemporaryContainer = CreateTemporaryHomeContainer(containerPath),
        };

        await Assert.ThrowsAnyAsync<IOException>(async () =>
            await manager.ApplyAsync(plan, TestContext.Current.CancellationToken)
        );

        Assert.True(fileSystem.DirectoryExists(containerPath));
        Assert.True(fileSystem.DirectoryExists(nestedDirectoryPath));
        Assert.False(fileSystem.FileExists(targetPath));
        Assert.Equal("existing-lock", fileSystem.ReadAllText(lockPath));
        Assert.False(fileSystem.FileExists(manifestPath));
    }

    [Fact]
    public async Task ApplyRollbackPreservesPreExistingEmptyNestedLockFile()
    {
        var fileSystem = new FailOnAtomicWriteFileSystem(InMemoryPathSemantics.Posix)
        {
            FailOnAtomicWriteNumber = 2,
        };
        const string containerPath = "/config/rollback-existing-empty-nested-lock";
        const string nestedDirectoryPath = "/config/rollback-existing-empty-nested-lock/nested";
        const string targetPath =
            "/config/rollback-existing-empty-nested-lock/nested/owned.txt";
        const string lockPath =
            "/config/rollback-existing-empty-nested-lock/nested/.azureauth-credprovider.fs.lock";
        const string manifestPath = "/state/rollback-existing-empty-nested-lock-manifest.json";
        fileSystem.CreateDirectory(nestedDirectoryPath);
        fileSystem.WriteAllText(lockPath, string.Empty);
        var manager = new ConfigurationManager(fileSystem, manifestPath);
        ConfigurationChangePlan plan = CreateGenericFilePlan(
            ConfigurationChangeOperation.Create,
            targetPath,
            "owned-value"
        ) with
        {
            TemporaryContainer = CreateTemporaryHomeContainer(containerPath),
        };

        await Assert.ThrowsAnyAsync<IOException>(async () =>
            await manager.ApplyAsync(plan, TestContext.Current.CancellationToken)
        );

        Assert.True(fileSystem.DirectoryExists(containerPath));
        Assert.True(fileSystem.DirectoryExists(nestedDirectoryPath));
        Assert.False(fileSystem.FileExists(targetPath));
        Assert.True(fileSystem.FileExists(lockPath));
        Assert.False(fileSystem.FileExists(manifestPath));
    }

    [Fact]
    public async Task ApplyRollsBackPartialWriteWhenCancellationHappensAfterMutationBegins()
    {
        var fileSystem = new InMemoryFileSystem(InMemoryPathSemantics.Posix);
        const string firstPath = "/config/cancel-first.txt";
        const string secondPath = "/config/cancel-second.txt";
        const string manifestPath = "/state/cancel-after-mutation-manifest.json";
        using var cancellation = new CancellationTokenSource();
        var manager = new ConfigurationManager(fileSystem, manifestPath);
        ConfigurationChangePlan plan = CreateGenericFilePlan(
            ConfigurationChangeOperation.Create,
            firstPath,
            "first-after"
        ) with
        {
            Changes =
            [
                CreateGenericFileChange(
                    ConfigurationChangeOperation.Create,
                    firstPath,
                    "first-after"
                ),
                CreateGenericFileChange(
                    ConfigurationChangeOperation.Create,
                    secondPath,
                    "second-after"
                ),
            ],
        };
        ArmOneShotRace(
            fileSystem,
            nameof(IFileSystem.AtomicWriteAllText),
            firstPath,
            _ => cancellation.Cancel()
        );

        await Assert.ThrowsAsync<OperationCanceledException>(async () =>
            await manager.ApplyAsync(plan, cancellation.Token)
        );

        Assert.False(fileSystem.FileExists(firstPath));
        Assert.False(fileSystem.FileExists(secondPath));
        Assert.False(fileSystem.FileExists(manifestPath));
    }

    [Fact]
    public async Task ApplyRollsBackTargetWhenAtomicWriteMutatesThenThrows()
    {
        var fileSystem = new FailOnAtomicWriteFileSystem(InMemoryPathSemantics.Posix);
        const string targetPath = "/config/post-write-failure.txt";
        const string manifestPath = "/state/post-write-failure-manifest.json";
        fileSystem.AtomicWriteAllText(targetPath, "before");
        string manifestJson = await CreateSingleGenericFileManifestJsonAsync(targetPath, "before");
        fileSystem.AtomicWriteAllText(manifestPath, manifestJson);
        fileSystem.ResetAtomicWriteCount();
        fileSystem.FailOnAtomicWriteNumber = 1;
        fileSystem.FailAfterAtomicWrite = true;
        var manager = new ConfigurationManager(fileSystem, manifestPath);
        ConfigurationChangePlan plan = CreateGenericFilePlan(
            ConfigurationChangeOperation.Update,
            targetPath,
            "after",
            HashMetadata("before"),
            previousManifestHash: HashMetadata(manifestJson)
        );

        await Assert.ThrowsAnyAsync<IOException>(async () =>
            await manager.ApplyAsync(plan, TestContext.Current.CancellationToken)
        );

        Assert.Equal("before", fileSystem.ReadAllText(targetPath));
        Assert.Equal(manifestJson, fileSystem.ReadAllText(manifestPath));
    }

    [Fact]
    public async Task ApplyDoesNotLeakSecretValuesThroughResultOrManifest()
    {
        var fileSystem = new InMemoryFileSystem(InMemoryPathSemantics.Posix);
        const string targetPath = "/config/secret.txt";
        const string manifestPath = "/state/secret-manifest.json";
        const string secret = "secret-token-value";
        var manager = new ConfigurationManager(fileSystem, manifestPath);
        ConfigurationChangePlan plan = CreateGenericFilePlan(
            ConfigurationChangeOperation.Create,
            targetPath,
            secret,
            isSecretValue: true
        );

        ConfigurationPlanResult result = await manager.ApplyAsync(
            plan,
            TestContext.Current.CancellationToken
        );

        string manifestJson = fileSystem.ReadAllText(manifestPath);
        Assert.Equal(secret, fileSystem.ReadAllText(targetPath));
        Assert.DoesNotContain(secret, manifestJson, StringComparison.Ordinal);
        Assert.DoesNotContain(secret, result.ToString(), StringComparison.Ordinal);
        Assert.Null(Assert.Single(result.Changes).PlannedValueSha256);
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

    private static ConfigurationChangePlan CreateGenericFilePlan(
        ConfigurationChangeOperation operation,
        string targetPath,
        string? value,
        string? previousOwnedEntryMetadata = null,
        string? previousManifestHash = null,
        bool isSecretValue = false
    ) =>
        ConfigurationChangePlanPolicy.Create(
            $"plan-generic-file-{operation}",
            $"changeset-generic-file-{operation}",
            "azureauth-credprovider",
            ConfigurationScope.CiTemporary,
            new ConfigurationManifestMetadata
            {
                ManifestId = "manifest-generic-file",
                OwnerProductId = "azureauth-credprovider",
                EntrySelector = "generic.file",
                ProductVersion = "0.0.0-test",
                PreviousOwnedEntryHash = previousManifestHash,
            },
            [
                CreateGenericFileChange(
                    operation,
                    targetPath,
                    value,
                    previousOwnedEntryMetadata,
                    isSecretValue
                ),
            ]
            ,
            temporaryContainer:
                CreateTemporaryHomeContainer(GetParentConfigurationPath(targetPath)),
            declarationPreservation:
                ConfigurationDeclarationPreservation.CompleteMergedTemporaryConfig
        );

    private static async Task<string> CreateDuplicateCiTemporaryFileManifestJsonAsync(
        string targetPath,
        string value
    )
    {
        var manager = new ConfigurationManager();
        ConfigurationPlanResult dryRun = await manager.DryRunAsync(
            CreateGenericFilePlan(ConfigurationChangeOperation.Create, targetPath, value),
            TestContext.Current.CancellationToken
        );
        ConfigurationOwnershipManifest manifest = dryRun.OwnershipManifest!;
        ConfigurationOwnershipManifestEntry entry = Assert.Single(manifest.Entries);
        ConfigurationOwnershipManifest duplicateManifest = manifest with
        {
            Entries =
            [
                entry,
                entry with
                {
                    Sequence = 2,
                    Key = "other-file-key",
                },
            ],
        };
        return RawOwnershipManifestJson(duplicateManifest);
    }

    private static async Task<string> CreateSingleGenericFileManifestJsonAsync(
        string targetPath,
        string value
    )
    {
        var manager = new ConfigurationManager();
        ConfigurationPlanResult dryRun = await manager.DryRunAsync(
            CreateGenericFilePlan(ConfigurationChangeOperation.Create, targetPath, value),
            TestContext.Current.CancellationToken
        );

        return RawOwnershipManifestJson(dryRun.OwnershipManifest!);
    }

    private static async Task<string> CreateManifestWithAdditionalGenericFileEntryJsonAsync(
        string firstTargetPath,
        string firstValue,
        string secondTargetPath,
        string secondValue
    )
    {
        var manager = new ConfigurationManager();
        ConfigurationPlanResult dryRun = await manager.DryRunAsync(
            CreateGenericFilePlan(ConfigurationChangeOperation.Create, firstTargetPath, firstValue),
            TestContext.Current.CancellationToken
        );
        ConfigurationOwnershipManifest manifest = dryRun.OwnershipManifest!;
        ConfigurationOwnershipManifestEntry entry = Assert.Single(manifest.Entries);
        ConfigurationOwnershipManifest manifestWithAdditionalEntry = manifest with
        {
            Entries =
            [
                entry,
                entry with
                {
                    Sequence = 2,
                    TargetPathOrName = secondTargetPath,
                    Key = "additional-file",
                    PlannedValueSha256 = HashMetadata(secondValue)["sha256:".Length..],
                },
            ],
        };

        return RawOwnershipManifestJson(manifestWithAdditionalEntry);
    }

    private static async Task AssertApplyRejectsManifestBomMutationWithNoBomManifestHashAsync(
        ConfigurationChangeOperation operation
    )
    {
        var fileSystem = new SystemFileSystem();
        string root = CreateSystemFileSystemTestDirectory();
        string containerPath = ToConfigurationPath(Path.Combine(root, "config"));
        string targetPath = ToConfigurationPath(Path.Combine(containerPath, "owned.txt"));
        string manifestPath = ToConfigurationPath(Path.Combine(root, "state", "manifest.json"));
        const string before = "owned contents";
        const string after = "updated contents";

        try
        {
            Directory.CreateDirectory(containerPath);
            File.WriteAllText(targetPath, before, new UTF8Encoding(false));
            string manifestJson = await CreateSingleGenericFileManifestJsonAsync(
                targetPath,
                before
            );
            Directory.CreateDirectory(Path.GetDirectoryName(manifestPath)!);
            File.WriteAllText(manifestPath, manifestJson, new UTF8Encoding(false));
            byte[] noBomManifestBytes = File.ReadAllBytes(manifestPath);
            string noBomManifestHash = HashMetadata(noBomManifestBytes);
            byte[] bomManifestBytes = CreateUtf8BomBytes(manifestJson);
            File.WriteAllBytes(manifestPath, bomManifestBytes);
            var manager = new ConfigurationManager(fileSystem, manifestPath);
            ConfigurationChangePlan plan = CreateGenericFilePlan(
                operation,
                targetPath,
                after,
                HashMetadata(before),
                previousManifestHash: noBomManifestHash
            );

            var exception = await Assert.ThrowsAsync<InvalidOperationException>(async () =>
                await manager.ApplyAsync(plan, TestContext.Current.CancellationToken)
            );

            Assert.Contains("before-state hash", exception.Message, StringComparison.Ordinal);
            Assert.Equal(Encoding.UTF8.GetBytes(before), File.ReadAllBytes(targetPath));
            Assert.Equal(bomManifestBytes, File.ReadAllBytes(manifestPath));
        }
        finally
        {
            DeleteSystemFileSystemTestDirectory(root);
        }
    }

    private static async Task<string> CreateParentChildConflictManifestJsonAsync(
        string parentTargetPath,
        string childTargetPath
    )
    {
        var manager = new ConfigurationManager();
        ConfigurationPlanResult dryRun = await manager.DryRunAsync(
            CreateGenericFilePlan(
                ConfigurationChangeOperation.Create,
                parentTargetPath,
                "parent-value"
            ),
            TestContext.Current.CancellationToken
        );
        ConfigurationOwnershipManifest manifest = dryRun.OwnershipManifest!;
        ConfigurationOwnershipManifestEntry entry = Assert.Single(manifest.Entries);
        ConfigurationOwnershipManifest parentChildConflictManifest = manifest with
        {
            Entries =
            [
                entry,
                entry with
                {
                    Sequence = 2,
                    TargetPathOrName = childTargetPath,
                    Key = "child-file",
                    PlannedValueSha256 = HashMetadata("child-value")["sha256:".Length..],
                },
            ],
        };

        return RawOwnershipManifestJson(parentChildConflictManifest);
    }

    private static async Task<string> CreateSamePathDifferentTargetKindManifestJsonAsync(
        string targetPath,
        string value
    )
    {
        var manager = new ConfigurationManager();
        ConfigurationPlanResult dryRun = await manager.DryRunAsync(
            CreateGenericFilePlan(ConfigurationChangeOperation.Create, targetPath, value),
            TestContext.Current.CancellationToken
        );
        ConfigurationOwnershipManifest manifest = dryRun.OwnershipManifest!;
        ConfigurationOwnershipManifestEntry entry = Assert.Single(manifest.Entries);
        ConfigurationOwnershipManifest conflictingManifest = manifest with
        {
            Entries =
            [
                entry with
                {
                    Operation = ConfigurationChangeOperation.Set,
                    TargetKind = ConfigurationTargetKind.Yarnrc,
                    Key = "nodeLinker",
                },
            ],
        };
        return RawOwnershipManifestJson(conflictingManifest);
    }

    private static ConfigurationTemporaryContainer CreateTemporaryHomeContainer(
        string productOwnedPath
    ) =>
        new()
        {
            Kind = ConfigurationTemporaryContainerKind.TemporaryHome,
            ProductOwnedPath = productOwnedPath,
            ActivationEnvironment = CreateTemporaryHomeActivationEnvironment(productOwnedPath),
        };

    private static ConfigurationActivationEnvironment CreateTemporaryHomeActivationEnvironment(
        string productOwnedPath
    ) =>
        IsWindowsConfigurationPath(productOwnedPath)
            ? new ConfigurationActivationEnvironment
            {
                Platform = "windows",
                SetVariables = new Dictionary<string, string>
                {
                    ["USERPROFILE"] = productOwnedPath,
                    ["HOME"] = productOwnedPath,
                },
                ClearVariables = ["HOMEDRIVE", "HOMEPATH"],
            }
            : new ConfigurationActivationEnvironment
            {
                Platform = "posix",
                SetVariables = new Dictionary<string, string>
                {
                    ["HOME"] = productOwnedPath,
                },
                ClearVariables = Array.Empty<string>(),
            };

    private static bool IsWindowsConfigurationPath(string path) =>
        IsWindowsDriveConfigurationPath(path)
        || path.StartsWith(@"\\", StringComparison.Ordinal)
        || path.StartsWith("//", StringComparison.Ordinal);

    private static string GetParentConfigurationPath(string path)
    {
        int separatorIndex = Math.Max(path.LastIndexOf('/'), path.LastIndexOf('\\'));
        if (
            separatorIndex == 2
            && path.Length >= 3
            && char.IsLetter(path[0])
            && path[1] == ':'
            && (path[2] == '\\' || path[2] == '/')
        )
        {
            return path[..3];
        }

        return separatorIndex <= 0 ? "/" : path[..separatorIndex];
    }

    private static string[] GetLifecycleLockPaths(InMemoryFileSystem fileSystem) =>
        fileSystem
            .Calls.Where(call =>
                string.Equals(
                    call.Operation,
                    nameof(IFileSystemMutationLock.AcquireMutationLock),
                    StringComparison.Ordinal
                )
                && call.Path.Contains(
                    "/.azureauth-credprovider.lifecycle-locks/",
                    StringComparison.Ordinal
                )
            )
            .Select(call => call.Path)
            .Distinct(StringComparer.Ordinal)
            .ToArray();

    private static void AssertLifecycleLockWasAttempted(
        IEnumerable<FileSystemCall> calls,
        string lifecycleLockPath
    )
    {
        FileSystemCall lockCall = Assert.Single(
            calls,
            call =>
                string.Equals(
                    call.Operation,
                    nameof(IFileSystemMutationLock.AcquireMutationLock),
                    StringComparison.Ordinal
                )
        );
        Assert.Equal(lifecycleLockPath, lockCall.Path);
    }

    private static void AssertNoFilesystemStateReadCallsBeforeLockAcquisition(
        IEnumerable<FileSystemCall> calls
    )
    {
        Assert.DoesNotContain(
            calls,
            call =>
                string.Equals(
                    call.Operation,
                    nameof(IFileSystem.FileExists),
                    StringComparison.Ordinal
                )
                || string.Equals(
                    call.Operation,
                    nameof(IFileSystem.DirectoryExists),
                    StringComparison.Ordinal
                )
                || string.Equals(
                    call.Operation,
                    nameof(IFileSystem.IsSymbolicLink),
                    StringComparison.Ordinal
                )
                || string.Equals(
                    call.Operation,
                    nameof(IFileSystem.ReadAllText),
                    StringComparison.Ordinal
                )
                || string.Equals(
                    call.Operation,
                    nameof(IFileSystemReparsePointSafety.IsReparsePoint),
                    StringComparison.Ordinal
                )
                || string.Equals(
                    call.Operation,
                    nameof(IFileSystem.CaptureFileIntegritySnapshot),
                    StringComparison.Ordinal
                )
                || string.Equals(
                    call.Operation,
                    nameof(IFileSystem.CaptureTrustedParentDirectorySnapshots),
                    StringComparison.Ordinal
                )
                || string.Equals(
                    call.Operation,
                    nameof(IFileSystem.ComputeSha256Hash),
                    StringComparison.Ordinal
                )
        );
    }

    private static void ArmOneShotRace(
        InMemoryFileSystem fileSystem,
        string operation,
        string path,
        Action<InMemoryFileSystem> mutate
    ) => ArmNthRace(fileSystem, operation, path, occurrence: 1, mutate);

    private static void ArmNthRace(
        InMemoryFileSystem fileSystem,
        string operation,
        string path,
        int occurrence,
        Action<InMemoryFileSystem> mutate
    )
    {
        int seen = 0;
        fileSystem.AfterRecord = (call, fs) =>
        {
            if (
                string.Equals(call.Operation, operation, StringComparison.Ordinal)
                && string.Equals(call.Path, path, StringComparison.Ordinal)
                && ++seen == occurrence
            )
            {
                fs.AfterRecord = null;
                mutate(fs);
            }
        };
    }

    private static ConfigurationChange CreateGenericFileChange(
        ConfigurationChangeOperation operation,
        string targetPath,
        string? value,
        string? previousOwnedEntryMetadata = null,
        bool isSecretValue = false
    ) =>
        new()
        {
            Operation = operation,
            TargetKind = ConfigurationTargetKind.CiTemporaryFile,
            TargetPathOrName = targetPath,
            Key = "file",
            Value = value,
            RequiresOwnershipRecord = true,
            IsSecretValue = isSecretValue,
            PreviousOwnedEntryMetadata = previousOwnedEntryMetadata,
            PreserveDeclarationsAndComments = false,
        };

    private static ConfigurationChange CreateYarnrcFileChange(string targetPath) =>
        new()
        {
            Operation = ConfigurationChangeOperation.Set,
            TargetKind = ConfigurationTargetKind.Yarnrc,
            TargetPathOrName = targetPath,
            Key = "nodeLinker",
            Value = "node-modules",
            RequiresOwnershipRecord = true,
            IsSecretValue = false,
            PreserveDeclarationsAndComments = true,
        };

    private static string HashMetadata(string value)
    {
        byte[] hash = System.Security.Cryptography.SHA256.HashData(Encoding.UTF8.GetBytes(value));
        return "sha256:" + Convert.ToHexString(hash).ToLowerInvariant();
    }

    private static string HashMetadata(byte[] value)
    {
        byte[] hash = System.Security.Cryptography.SHA256.HashData(value);
        return "sha256:" + Convert.ToHexString(hash).ToLowerInvariant();
    }

    private static string CreateSystemFileSystemTestDirectory()
    {
        string path = Path.Combine(
            AppContext.BaseDirectory,
            "configuration-manager-tests",
            Guid.NewGuid().ToString("N")
        );
        Directory.CreateDirectory(path);
        return path;
    }

    private static void DeleteSystemFileSystemTestDirectory(string path)
    {
        if (Directory.Exists(path))
        {
            Directory.Delete(path, recursive: true);
        }
    }

    private static bool TryCreateWindowsDirectoryJunction(string junctionPath, string targetPath)
    {
        if (!OperatingSystem.IsWindows())
        {
            return false;
        }

        string? parent = Path.GetDirectoryName(junctionPath);
        if (!string.IsNullOrEmpty(parent))
        {
            Directory.CreateDirectory(parent);
        }

        using Process process = Process.Start(
            new ProcessStartInfo(
                "cmd.exe",
                $"/c mklink /J \"{junctionPath}\" \"{targetPath}\""
            )
            {
                CreateNoWindow = true,
                RedirectStandardError = true,
                RedirectStandardOutput = true,
                UseShellExecute = false,
            }
        )!;
        process.WaitForExit();
        return process.ExitCode == 0 && Directory.Exists(junctionPath);
    }

    private static string ToConfigurationPath(string path) =>
        IsWindowsDriveConfigurationPath(path) ? path.Replace('/', '\\') : path.Replace('\\', '/');

    private static bool IsWindowsDriveConfigurationPath(string path) =>
        path.Length >= 3
        && char.IsLetter(path[0])
        && path[1] == ':'
        && (path[2] == '\\' || path[2] == '/');

    private static byte[] CreateUtf8BomBytes(string contents) =>
        [0xEF, 0xBB, 0xBF, .. Encoding.UTF8.GetBytes(contents)];

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

    private static void AssertNoTargetSnapshotReads(
        InMemoryFileSystem fileSystem,
        string targetPath
    )
    {
        Assert.DoesNotContain(
            fileSystem.Calls,
            call =>
                string.Equals(call.Path, targetPath, StringComparison.Ordinal)
                && (
                    string.Equals(
                        call.Operation,
                        nameof(IFileSystem.FileExists),
                        StringComparison.Ordinal
                    )
                    || string.Equals(
                        call.Operation,
                        nameof(IFileSystem.DirectoryExists),
                        StringComparison.Ordinal
                    )
                    || string.Equals(
                        call.Operation,
                        nameof(IFileSystem.ReadAllBytes),
                        StringComparison.Ordinal
                    )
                    || string.Equals(
                        call.Operation,
                        nameof(IFileSystem.ReadAllText),
                        StringComparison.Ordinal
                    )
                    || string.Equals(
                        call.Operation,
                        nameof(IFileSystem.ComputeSha256Hash),
                        StringComparison.Ordinal
                    )
                    || string.Equals(
                        call.Operation,
                        nameof(IFileSystem.CaptureFileIntegritySnapshot),
                        StringComparison.Ordinal
                    )
                    || string.Equals(
                        call.Operation,
                        nameof(IFileSystem.CaptureTrustedParentDirectorySnapshots),
                        StringComparison.Ordinal
                    )
                    || string.Equals(
                        call.Operation,
                        nameof(IFileSystem.IsSymbolicLink),
                        StringComparison.Ordinal
                    )
                    || string.Equals(
                        call.Operation,
                        nameof(IFileSystemReparsePointSafety.IsReparsePoint),
                        StringComparison.Ordinal
                    )
                )
        );
    }

    private static void AssertNoManifestContentReads(
        InMemoryFileSystem fileSystem,
        string manifestPath
    )
    {
        Assert.DoesNotContain(
            fileSystem.Calls,
            call =>
                string.Equals(call.Path, manifestPath, StringComparison.Ordinal)
                && (
                    string.Equals(
                        call.Operation,
                        nameof(IFileSystem.ReadAllBytes),
                        StringComparison.Ordinal
                    )
                    || string.Equals(
                        call.Operation,
                        nameof(IFileSystem.ReadAllText),
                        StringComparison.Ordinal
                    )
                    || string.Equals(
                        call.Operation,
                        nameof(IFileSystem.ComputeSha256Hash),
                        StringComparison.Ordinal
                    )
                    || string.Equals(
                        call.Operation,
                        nameof(IFileSystem.CaptureFileIntegritySnapshot),
                        StringComparison.Ordinal
                    )
                )
        );
    }

    private static void AssertNoLockArtifactContentReads(
        InMemoryFileSystem fileSystem,
        string lockPath
    )
    {
        Assert.DoesNotContain(
            fileSystem.Calls,
            call =>
                string.Equals(call.Path, lockPath, StringComparison.Ordinal)
                && (
                    string.Equals(
                        call.Operation,
                        nameof(IFileSystem.ReadAllBytes),
                        StringComparison.Ordinal
                    )
                    || string.Equals(
                        call.Operation,
                        nameof(IFileSystem.ReadAllText),
                        StringComparison.Ordinal
                    )
                    || string.Equals(
                        call.Operation,
                        nameof(IFileSystem.ComputeSha256Hash),
                        StringComparison.Ordinal
                    )
                    || string.Equals(
                        call.Operation,
                        nameof(IFileSystem.CaptureFileIntegritySnapshot),
                        StringComparison.Ordinal
                    )
                )
        );
    }

    private static void AssertLockArtifactLengthChecked(
        InMemoryFileSystem fileSystem,
        string lockPath
    )
    {
        Assert.Contains(
            fileSystem.Calls,
            call =>
                string.Equals(call.Path, lockPath, StringComparison.Ordinal)
                && string.Equals(
                    call.Operation,
                    nameof(IFileSystemFileLength.GetFileLength),
                    StringComparison.Ordinal
                )
        );
    }

    private static void AssertNoLockArtifactLengthChecks(
        InMemoryFileSystem fileSystem,
        string lockPath
    )
    {
        Assert.DoesNotContain(
            fileSystem.Calls,
            call =>
                string.Equals(call.Path, lockPath, StringComparison.Ordinal)
                && string.Equals(
                    call.Operation,
                    nameof(IFileSystemFileLength.GetFileLength),
                    StringComparison.Ordinal
                )
        );
    }

    private static Exception CreateLockArtifactSafetyCheckException(string exceptionKind) =>
        exceptionKind switch
        {
            nameof(NotSupportedException) => new NotSupportedException(
                "Injected lock artifact safety check failure."
            ),
            nameof(UnauthorizedAccessException) => new UnauthorizedAccessException(
                "Injected lock artifact safety check failure."
            ),
            nameof(IOException) => new IOException("Injected lock artifact safety check failure."),
            _ => throw new ArgumentOutOfRangeException(nameof(exceptionKind), exceptionKind, null),
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

    private sealed class PassThroughFileSystem : IFileSystem
    {
        public bool SupportsConditionalFileMutations => true;

        public bool FileExists(string path) => throw new NotSupportedException();

        public bool DirectoryExists(string path) => throw new NotSupportedException();

        public string GetFullPath(string path) => path;

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

        public string ReadAllText(string path, Encoding? encoding = null) =>
            throw new NotSupportedException();

        public byte[] ReadAllBytes(string path) => throw new NotSupportedException();

        public void WriteAllText(string path, string contents, Encoding? encoding = null) =>
            throw new NotSupportedException();

        public void AtomicWriteAllText(
            string path,
            string contents,
            Encoding? encoding = null,
            AtomicWriteOptions options = AtomicWriteOptions.None,
            FileMutationExpectation? expectation = null
        ) => throw new NotSupportedException();

        public void AtomicWriteAllBytes(
            string path,
            byte[] contents,
            AtomicWriteOptions options = AtomicWriteOptions.None,
            FileMutationExpectation? expectation = null
        ) => throw new NotSupportedException();

        public UnixFileMode GetUnixFileMode(string path) => throw new NotSupportedException();

        public void SetUnixFileMode(string path, UnixFileMode mode) =>
            throw new NotSupportedException();

        public void CreateDirectory(string path) => throw new NotSupportedException();

        public void DeleteFile(string path, FileMutationExpectation? expectation = null) =>
            throw new NotSupportedException();

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

    private sealed class FileSystemWithoutFileLength
        : IFileSystem,
            IFileSystemMutationLock,
            IFileSystemReparsePointSafety,
            IFileSystemNoFollowEnumeration
    {
        private readonly InMemoryFileSystem inner;

        public FileSystemWithoutFileLength(InMemoryFileSystem inner)
        {
            this.inner = inner;
        }

        public bool SupportsConditionalFileMutations => inner.SupportsConditionalFileMutations;

        public bool FileExists(string path) => inner.FileExists(path);

        public bool DirectoryExists(string path) => inner.DirectoryExists(path);

        public string GetFullPath(string path) => inner.GetFullPath(path);

        public bool IsPathFullyQualified(string path) => inner.IsPathFullyQualified(path);

        public bool IsSymbolicLink(string path) => inner.IsSymbolicLink(path);

        bool IFileSystemReparsePointSafety.IsReparsePoint(string path) =>
            ((IFileSystemReparsePointSafety)inner).IsReparsePoint(path);

        public byte[] ComputeSha256Hash(string path) => inner.ComputeSha256Hash(path);

        public FileIntegritySnapshot CaptureFileIntegritySnapshot(string path) =>
            inner.CaptureFileIntegritySnapshot(path);

        public bool FileMatchesIntegritySnapshot(string path, FileIntegritySnapshot snapshot) =>
            inner.FileMatchesIntegritySnapshot(path, snapshot);

        public IReadOnlyList<TrustedDirectorySnapshot> CaptureTrustedParentDirectorySnapshots(
            string path
        ) => inner.CaptureTrustedParentDirectorySnapshots(path);

        public FileSystemOwner GetCurrentOwner() => inner.GetCurrentOwner();

        public FileSystemOwner GetOwner(string path) => inner.GetOwner(path);

        public string ReadAllText(string path, Encoding? encoding = null) =>
            inner.ReadAllText(path, encoding);

        public byte[] ReadAllBytes(string path) => inner.ReadAllBytes(path);

        public void WriteAllText(string path, string contents, Encoding? encoding = null) =>
            inner.WriteAllText(path, contents, encoding);

        public void AtomicWriteAllText(
            string path,
            string contents,
            Encoding? encoding = null,
            AtomicWriteOptions options = AtomicWriteOptions.None,
            FileMutationExpectation? expectation = null
        ) => inner.AtomicWriteAllText(path, contents, encoding, options, expectation);

        public void AtomicWriteAllBytes(
            string path,
            byte[] contents,
            AtomicWriteOptions options = AtomicWriteOptions.None,
            FileMutationExpectation? expectation = null
        ) => inner.AtomicWriteAllBytes(path, contents, options, expectation);

        public UnixFileMode GetUnixFileMode(string path) => inner.GetUnixFileMode(path);

        public void SetUnixFileMode(string path, UnixFileMode mode) =>
            inner.SetUnixFileMode(path, mode);

        public void CreateDirectory(string path) => inner.CreateDirectory(path);

        public void DeleteFile(string path, FileMutationExpectation? expectation = null) =>
            inner.DeleteFile(path, expectation);

        public void DeleteDirectory(string path, bool recursive = false) =>
            inner.DeleteDirectory(path, recursive);

        public IEnumerable<string> EnumerateFiles(
            string path,
            string searchPattern = "*",
            SearchOption searchOption = SearchOption.TopDirectoryOnly
        ) => inner.EnumerateFiles(path, searchPattern, searchOption);

        public IEnumerable<string> EnumerateDirectories(
            string path,
            string searchPattern = "*",
            SearchOption searchOption = SearchOption.TopDirectoryOnly
        ) => inner.EnumerateDirectories(path, searchPattern, searchOption);

        IEnumerable<string> IFileSystemNoFollowEnumeration.EnumerateFileSystemEntriesNoFollow(
            string path,
            string searchPattern,
            SearchOption searchOption
        ) =>
            ((IFileSystemNoFollowEnumeration)inner).EnumerateFileSystemEntriesNoFollow(
                path,
                searchPattern,
                searchOption
            );

        IDisposable IFileSystemMutationLock.AcquireMutationLock(
            string directory,
            bool createDirectory
        ) => ((IFileSystemMutationLock)inner).AcquireMutationLock(directory, createDirectory);
    }

    private sealed class InMemoryManifestFileSystem : IFileSystem
    {
        public string StoredText { get; private set; } = string.Empty;

        public bool SupportsConditionalFileMutations => true;

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

        public byte[] ReadAllBytes(string path) => Encoding.UTF8.GetBytes(StoredText);

        public void WriteAllText(string path, string contents, Encoding? encoding = null) =>
            StoredText = contents;

        public void AtomicWriteAllText(
            string path,
            string contents,
            Encoding? encoding = null,
            AtomicWriteOptions options = AtomicWriteOptions.None,
            FileMutationExpectation? expectation = null
        ) => StoredText = contents;

        public void AtomicWriteAllBytes(
            string path,
            byte[] contents,
            AtomicWriteOptions options = AtomicWriteOptions.None,
            FileMutationExpectation? expectation = null
        ) => StoredText = Encoding.UTF8.GetString(contents);

        public UnixFileMode GetUnixFileMode(string path) => throw new NotSupportedException();

        public void SetUnixFileMode(string path, UnixFileMode mode) =>
            throw new NotSupportedException();

        public void CreateDirectory(string path) => throw new NotSupportedException();

        public void DeleteFile(string path, FileMutationExpectation? expectation = null) =>
            throw new NotSupportedException();

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

    private sealed class FailOnAtomicWriteFileSystem
        : IFileSystem,
            IFileSystemReparsePointSafety,
            IFileSystemNoFollowEnumeration,
            IFileSystemFileLength
    {
        private readonly InMemoryFileSystem inner;
        private int atomicWriteCount;

        public FailOnAtomicWriteFileSystem(InMemoryPathSemantics pathSemantics)
        {
            inner = new InMemoryFileSystem(pathSemantics);
        }

        public int FailOnAtomicWriteNumber { get; set; }

        public bool FailAfterAtomicWrite { get; set; }

        public int AtomicWriteCount => atomicWriteCount;

        public bool SupportsConditionalFileMutations => inner.SupportsConditionalFileMutations;

        public Action<FileSystemCall, FailOnAtomicWriteFileSystem>? AfterRecord { get; set; }

        public void ResetAtomicWriteCount() => atomicWriteCount = 0;

        public void AddSymbolicLink(string linkPath, string targetPath) =>
            inner.AddSymbolicLink(linkPath, targetPath);

        public void MarkAsNonSymbolicReparsePoint(string path) =>
            inner.MarkAsNonSymbolicReparsePoint(path);

        public bool FileExists(string path) => inner.FileExists(path);

        public bool DirectoryExists(string path) => inner.DirectoryExists(path);

        public string GetFullPath(string path) => inner.GetFullPath(path);

        public bool IsPathFullyQualified(string path) => inner.IsPathFullyQualified(path);

        public bool IsSymbolicLink(string path) => inner.IsSymbolicLink(path);

        bool IFileSystemReparsePointSafety.IsReparsePoint(string path) =>
            ((IFileSystemReparsePointSafety)inner).IsReparsePoint(path);

        public byte[] ComputeSha256Hash(string path) => inner.ComputeSha256Hash(path);

        public FileIntegritySnapshot CaptureFileIntegritySnapshot(string path) =>
            inner.CaptureFileIntegritySnapshot(path);

        public bool FileMatchesIntegritySnapshot(string path, FileIntegritySnapshot snapshot) =>
            inner.FileMatchesIntegritySnapshot(path, snapshot);

        public IReadOnlyList<TrustedDirectorySnapshot> CaptureTrustedParentDirectorySnapshots(
            string path
        ) => inner.CaptureTrustedParentDirectorySnapshots(path);

        public FileSystemOwner GetCurrentOwner() => inner.GetCurrentOwner();

        public FileSystemOwner GetOwner(string path) => inner.GetOwner(path);

        public string ReadAllText(string path, Encoding? encoding = null) =>
            inner.ReadAllText(path, encoding);

        public byte[] ReadAllBytes(string path) => inner.ReadAllBytes(path);

        public long GetFileLength(string path) => inner.GetFileLength(path);

        public void WriteAllText(string path, string contents, Encoding? encoding = null) =>
            inner.WriteAllText(path, contents, encoding);

        public void AtomicWriteAllText(
            string path,
            string contents,
            Encoding? encoding = null,
            AtomicWriteOptions options = AtomicWriteOptions.None,
            FileMutationExpectation? expectation = null
        )
        {
            atomicWriteCount++;
            AfterRecord?.Invoke(
                new FileSystemCall(nameof(AtomicWriteAllText), inner.GetFullPath(path), contents),
                this
            );
            if (atomicWriteCount == FailOnAtomicWriteNumber && !FailAfterAtomicWrite)
            {
                throw new IOException("Injected atomic write failure.");
            }

            inner.AtomicWriteAllText(path, contents, encoding, options, expectation);
            if (atomicWriteCount == FailOnAtomicWriteNumber)
            {
                throw new FileMutationException(
                    "Injected post-write atomic write failure.",
                    mutationMayHaveReachedDurableState: true,
                    new IOException("Injected post-write atomic write failure.")
                );
            }
        }

        public void AtomicWriteAllBytes(
            string path,
            byte[] contents,
            AtomicWriteOptions options = AtomicWriteOptions.None,
            FileMutationExpectation? expectation = null
        )
        {
            atomicWriteCount++;
            AfterRecord?.Invoke(
                new FileSystemCall(
                    nameof(AtomicWriteAllBytes),
                    inner.GetFullPath(path),
                    Encoding.UTF8.GetString(contents)
                ),
                this
            );
            if (atomicWriteCount == FailOnAtomicWriteNumber && !FailAfterAtomicWrite)
            {
                throw new IOException("Injected atomic write failure.");
            }

            inner.AtomicWriteAllBytes(path, contents, options, expectation);
            if (atomicWriteCount == FailOnAtomicWriteNumber)
            {
                throw new FileMutationException(
                    "Injected post-write atomic write failure.",
                    mutationMayHaveReachedDurableState: true,
                    new IOException("Injected post-write atomic write failure.")
                );
            }
        }

        public UnixFileMode GetUnixFileMode(string path) => inner.GetUnixFileMode(path);

        public void SetUnixFileMode(string path, UnixFileMode mode) =>
            inner.SetUnixFileMode(path, mode);

        public void CreateDirectory(string path) => inner.CreateDirectory(path);

        public void DeleteFile(string path, FileMutationExpectation? expectation = null)
        {
            AfterRecord?.Invoke(
                new FileSystemCall(nameof(DeleteFile), inner.GetFullPath(path)),
                this
            );
            inner.DeleteFile(path, expectation);
        }

        public void DeleteDirectory(string path, bool recursive = false) =>
            inner.DeleteDirectory(path, recursive);

        public IEnumerable<string> EnumerateFiles(
            string path,
            string searchPattern = "*",
            SearchOption searchOption = SearchOption.TopDirectoryOnly
        ) => inner.EnumerateFiles(path, searchPattern, searchOption);

        public IEnumerable<string> EnumerateDirectories(
            string path,
            string searchPattern = "*",
            SearchOption searchOption = SearchOption.TopDirectoryOnly
        ) => inner.EnumerateDirectories(path, searchPattern, searchOption);

        IEnumerable<string> IFileSystemNoFollowEnumeration.EnumerateFileSystemEntriesNoFollow(
            string path,
            string searchPattern,
            SearchOption searchOption
        ) =>
            ((IFileSystemNoFollowEnumeration)inner).EnumerateFileSystemEntriesNoFollow(
                path,
                searchPattern,
                searchOption
            );
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
