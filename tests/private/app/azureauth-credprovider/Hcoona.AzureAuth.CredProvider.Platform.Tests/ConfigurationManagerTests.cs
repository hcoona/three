using System.Diagnostics;
using System.Globalization;
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

[Collection("ConfigurationManagerExecution")]
public sealed class ConfigurationManagerTests
{
    private const string PhysicalTargetManifestPreclaimMetadataKey =
        "hcoona.azureAuthCredProvider.physicalTargetManifestState";
    private const string PhysicalTargetManifestPreclaimMetadataValue = "prepared";

    private static readonly ConfigurationTargetKind[] NonPhase4DPhysicalTargetKindValues =
    [
        ConfigurationTargetKind.Npmrc,
        ConfigurationTargetKind.Yarnrc,
    ];

    private static readonly ConfigurationTargetKind[] Phase4DPhysicalTargetKindValues =
    [
        ConfigurationTargetKind.GitConfig,
    ];

    private static readonly ConfigurationTargetKind[]
        UnsupportedRetainedNonCiPhysicalTargetKindValues =
        [
            ConfigurationTargetKind.PythonKeyringBackend,
            ConfigurationTargetKind.KeyringShim,
            ConfigurationTargetKind.Npmrc,
            ConfigurationTargetKind.Yarnrc,
        ];

    private static string CreateNuGetPluginLayoutTargetRoot(string? userName = null)
    {
        string homeDirectory = GetCurrentUserProfileDirectory();
        if (userName is null)
        {
            return Path.Combine(
                homeDirectory,
                ".nuget",
                "plugins",
                "netcore",
                "azureauth-credprovider"
            );
        }

        string? parentDirectory = Path.GetDirectoryName(homeDirectory);
        string userProfileDirectory = string.IsNullOrEmpty(parentDirectory)
            ? Path.Combine(homeDirectory, userName)
            : Path.Combine(parentDirectory, userName);
        return Path.Combine(
            userProfileDirectory,
            ".nuget",
            "plugins",
            "netcore",
            "azureauth-credprovider"
        );
    }

    private static string GetCurrentUserProfileDirectory()
    {
        string? userProfile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        if (!string.IsNullOrWhiteSpace(userProfile))
        {
            return Path.TrimEndingDirectorySeparator(userProfile);
        }

        if (OperatingSystem.IsWindows())
        {
            string? windowsUserProfile = Environment.GetEnvironmentVariable("USERPROFILE");
            if (!string.IsNullOrWhiteSpace(windowsUserProfile))
            {
                return Path.TrimEndingDirectorySeparator(windowsUserProfile);
            }

            string? homeDrive = Environment.GetEnvironmentVariable("HOMEDRIVE");
            string? homePath = Environment.GetEnvironmentVariable("HOMEPATH");
            if (!string.IsNullOrWhiteSpace(homeDrive) && !string.IsNullOrWhiteSpace(homePath))
            {
                return Path.TrimEndingDirectorySeparator(homeDrive + homePath);
            }
        }
        else
        {
            string? home = Environment.GetEnvironmentVariable("HOME");
            if (!string.IsNullOrWhiteSpace(home))
            {
                return Path.TrimEndingDirectorySeparator(home);
            }
        }

        throw new InvalidOperationException("User profile directory is unavailable.");
    }

    public static bool IsWindows => OperatingSystem.IsWindows();

    public static TheoryData<string, ConfigurationTargetKind, string, string>
        PhysicalTargetOwnershipManifestCollisionCases =>
        CreatePhysicalTargetOwnershipManifestCollisionCases(
            NonPhase4DPhysicalTargetKindValues
        );

    public static TheoryData<string, ConfigurationTargetKind, string, string>
        Phase4DPhysicalTargetOwnershipManifestCollisionCases =>
        CreatePhysicalTargetOwnershipManifestCollisionCases(
            Phase4DPhysicalTargetKindValues
        );

    public static TheoryData<ConfigurationTargetKind> Phase4DPhysicalTargetKinds
    {
        get
        {
            var cases = new TheoryData<ConfigurationTargetKind>();
            foreach (ConfigurationTargetKind targetKind in Phase4DPhysicalTargetKindValues)
            {
                cases.Add(targetKind);
            }
            return cases;
        }
    }

    public static TheoryData<ConfigurationTargetKind>
        UnsupportedRetainedNonCiPhysicalTargetKinds
    {
        get
        {
            var cases = new TheoryData<ConfigurationTargetKind>();
            foreach (
                ConfigurationTargetKind targetKind in
                UnsupportedRetainedNonCiPhysicalTargetKindValues
            )
            {
                cases.Add(targetKind);
            }

            return cases;
        }
    }

    private static TheoryData<string, ConfigurationTargetKind, string, string>
        CreatePhysicalTargetOwnershipManifestCollisionCases(
            IEnumerable<ConfigurationTargetKind> targetKinds
        )
    {
        var cases = new TheoryData<string, ConfigurationTargetKind, string, string>();
        string[] methodNames =
        [
            nameof(IConfigurationManager.ValidatePlan),
            nameof(IConfigurationManager.DryRunAsync),
        ];

        foreach (string methodName in methodNames)
        {
            foreach (ConfigurationTargetKind targetKind in targetKinds)
            {
                string pathSegment = targetKind.ToString().ToLower(CultureInfo.InvariantCulture);
                cases.Add(
                    methodName,
                    targetKind,
                    $"/config/{pathSegment}/ownership-manifest.json",
                    $"/config/{pathSegment}/ownership-manifest.json"
                );
                cases.Add(
                    methodName,
                    targetKind,
                    $"/config/{pathSegment}/absolute-relative-target",
                    $"config/{pathSegment}/absolute-relative-target"
                );
                cases.Add(
                    methodName,
                    targetKind,
                    $"config/{pathSegment}/relative-absolute-target",
                    $"/config/{pathSegment}/relative-absolute-target"
                );
                cases.Add(
                    methodName,
                    targetKind,
                    $"/config/{pathSegment}/target-root",
                    $"/config/{pathSegment}/target-root/ownership-manifest.json"
                );
                cases.Add(
                    methodName,
                    targetKind,
                    $"/config/{pathSegment}/manifest-root/target",
                    $"/config/{pathSegment}/manifest-root"
                );
                cases.Add(
                    methodName,
                    targetKind,
                    $"config/{pathSegment}/ownership-manifest.json",
                    $"config/{pathSegment}/ownership-manifest.json"
                );
                cases.Add(
                    methodName,
                    targetKind,
                    $"config/{pathSegment}/sub/../ownership-manifest.json",
                    $"config/{pathSegment}/ownership-manifest.json"
                );
                cases.Add(
                    methodName,
                    targetKind,
                    $"config/{pathSegment}/target-root",
                    $"config/{pathSegment}/target-root/ownership-manifest.json"
                );
                cases.Add(
                    methodName,
                    targetKind,
                    $"config/{pathSegment}/manifest-root/target",
                    $"config/{pathSegment}/manifest-root"
                );
            }
        }

        return cases;
    }

    public static TheoryData<ConfigurationTargetKind, ConfigurationTargetKind>
        Phase4DPhysicalTargetKindConflictCases
    {
        get
        {
            var cases = new TheoryData<ConfigurationTargetKind, ConfigurationTargetKind>();
            cases.Add(ConfigurationTargetKind.NuGetPluginLayout, ConfigurationTargetKind.Npmrc);
            cases.Add(ConfigurationTargetKind.PythonKeyringBackend, ConfigurationTargetKind.Npmrc);
            cases.Add(ConfigurationTargetKind.KeyringShim, ConfigurationTargetKind.Yarnrc);
            cases.Add(ConfigurationTargetKind.GitConfig, ConfigurationTargetKind.Yarnrc);
            cases.Add(
                ConfigurationTargetKind.NuGetPluginLayout,
                ConfigurationTargetKind.KeyringShim
            );
            cases.Add(
                ConfigurationTargetKind.PythonKeyringBackend,
                ConfigurationTargetKind.GitConfig
            );
            return cases;
        }
    }

    public static TheoryData<ConfigurationTargetKind, string> ReservedPhase4DPhysicalTargetPaths
    {
        get
        {
            var cases = new TheoryData<ConfigurationTargetKind, string>();
            string[] reservedPaths =
            [
                "/config/.azureauth-credprovider.fs.lock",
                "/config/.azureauth-credprovider.fs.lock/descendant",
                "/config/.azureauth-credprovider.lifecycle-locks",
                "/config/.azureauth-credprovider.lifecycle-locks/descendant",
                ".azureauth-credprovider.fs.lock",
                ".azureauth-credprovider.fs.lock/descendant",
                ".azureauth-credprovider.lifecycle-locks",
                ".azureauth-credprovider.lifecycle-locks/descendant",
                "../.azureauth-credprovider.fs.lock",
                "../.azureauth-credprovider.lifecycle-locks",
            ];
            ConfigurationTargetKind[] targetKinds =
            [
                ConfigurationTargetKind.NuGetPluginLayout,
                ConfigurationTargetKind.PythonKeyringBackend,
                ConfigurationTargetKind.KeyringShim,
                ConfigurationTargetKind.GitConfig,
                ConfigurationTargetKind.Npmrc,
                ConfigurationTargetKind.Yarnrc,
            ];

            foreach (ConfigurationTargetKind targetKind in targetKinds)
            {
                foreach (string reservedPath in reservedPaths)
                {
                    cases.Add(targetKind, reservedPath);
                }
            }

            return cases;
        }
    }

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

    [Fact]
    public async Task
        ValidatePlanDryRunApplyRemovePreserveGenericCiTempProjectionOrdering()
    {
        var manager = new ConfigurationManager();
        ConfigurationChangePlan plan = CreateGenericFilePlan(
            ConfigurationChangeOperation.EnsureFile,
            "/config/projection-first-generic-file.txt",
            value: null
        ) with
        {
            Manifest = new ConfigurationManifestMetadata
            {
                ManifestId = "manifest-generic-file-null-metadata",
                OwnerProductId = "azureauth-credprovider",
                EntrySelector = "generic.file",
                ProductVersion = "0.0.0-test",
                SafeMetadata = null!,
            },
        };

        ConfigurationPlanValidationResult validationResult = manager.ValidatePlan(plan);

        Assert.False(validationResult.IsValid);
        Assert.NotNull(validationResult.Violation);
        Assert.Contains("metadata", validationResult.Violation, StringComparison.OrdinalIgnoreCase);
        var exception = await Assert.ThrowsAsync<ArgumentException>(async () =>
            await manager.DryRunAsync(plan, TestContext.Current.CancellationToken)
        );
        Assert.Contains("metadata", exception.Message, StringComparison.OrdinalIgnoreCase);
        var applyException = Assert.Throws<ArgumentException>(() =>
        {
            ValueTask<ConfigurationPlanResult> result = manager.ApplyAsync(
                plan,
                TestContext.Current.CancellationToken
            );
            _ = result.AsTask();
        });
        Assert.Contains("metadata", applyException.Message, StringComparison.OrdinalIgnoreCase);
        var removeException = Assert.Throws<ArgumentException>(() =>
        {
            ValueTask<ConfigurationPlanResult> result = manager.RemoveAsync(
                plan,
                TestContext.Current.CancellationToken
            );
            _ = result.AsTask();
        });
        Assert.Contains("metadata", removeException.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task
        ValidateAcceptDryRunRejectMultiplePhase4DPhysicalChangesBeforeProjection()
    {
        var manager = new ConfigurationManager();
        const string firstTargetPath = "/config/planning-multi-phase4d-first.gitconfig";
        const string secondTargetPath = "/config/planning-multi-phase4d-second.gitconfig";
        ConfigurationChangePlan setPlan = CreatePhysicalTargetPlan(
            ConfigurationTargetKind.GitConfig,
            firstTargetPath
        );
        ConfigurationChangePlan plan = setPlan with
        {
            // Null safe metadata is contract-valid but ownership-manifest projection-invalid.
            // The Phase 4D physical scaffold guard must reject before projection sees it.
            Manifest = setPlan.Manifest with { SafeMetadata = null! },
            Changes =
            [
                setPlan.Changes[0],
                setPlan.Changes[0] with
                {
                    Key = "credential \"https://dev.azure.com\".useHttpPath",
                    TargetPathOrName = secondTargetPath,
                    Value = "true",
                },
            ],
        };

        ConfigurationPlanValidationResult validationResult = manager.ValidatePlan(plan);
        ConfigurationPlanValidationResult acceptResult = await manager.AcceptPlanAsync(
            plan,
            TestContext.Current.CancellationToken
        );

        Assert.False(validationResult.IsValid);
        Assert.NotNull(validationResult.Violation);
        Assert.Contains(
            "only one 4D physical target change",
            validationResult.Violation,
            StringComparison.Ordinal
        );
        Assert.DoesNotContain(
            "metadata",
            validationResult.Violation,
            StringComparison.OrdinalIgnoreCase
        );
        Assert.False(acceptResult.IsValid);
        Assert.NotNull(acceptResult.Violation);
        Assert.Contains(
            "only one 4D physical target change",
            acceptResult.Violation,
            StringComparison.Ordinal
        );
        Assert.DoesNotContain(
            "metadata",
            acceptResult.Violation,
            StringComparison.OrdinalIgnoreCase
        );
        var exception = await Assert.ThrowsAsync<ArgumentException>(async () =>
            await manager.DryRunAsync(plan, TestContext.Current.CancellationToken)
        );
        Assert.Contains(
            "only one 4D physical target change",
            exception.Message,
            StringComparison.Ordinal
        );
        Assert.DoesNotContain("metadata", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task
        ValidateAcceptDryRunRejectUnsupportedPhase4DInstallAdapterBeforeProjection()
    {
        var manager = new ConfigurationManager();
        string targetPath = CreateNuGetPluginLayoutTargetRoot();
        ConfigurationChangePlan setPlan = CreatePhysicalTargetPlan(
            ConfigurationTargetKind.NuGetPluginLayout,
            targetPath
        );
        ConfigurationChangePlan plan = setPlan with
        {
            // Null safe metadata is contract-valid but ownership-manifest projection-invalid.
            // The Phase 4D physical scaffold guard must reject before projection sees it.
            Manifest = setPlan.Manifest with { SafeMetadata = null! },
            Changes =
            [
                setPlan.Changes[0] with
                {
                    Operation = ConfigurationChangeOperation.InstallAdapter,
                    Value = null,
                },
            ],
        };

        ConfigurationPlanValidationResult validationResult = manager.ValidatePlan(plan);
        ConfigurationPlanValidationResult acceptResult = await manager.AcceptPlanAsync(
            plan,
            TestContext.Current.CancellationToken
        );

        Assert.False(validationResult.IsValid);
        Assert.NotNull(validationResult.Violation);
        Assert.Contains(
            "can be executed by apply or remove",
            validationResult.Violation,
            StringComparison.Ordinal
        );
        Assert.DoesNotContain(
            "metadata",
            validationResult.Violation,
            StringComparison.OrdinalIgnoreCase
        );
        Assert.False(acceptResult.IsValid);
        Assert.NotNull(acceptResult.Violation);
        Assert.Contains(
            "can be executed by apply or remove",
            acceptResult.Violation,
            StringComparison.Ordinal
        );
        Assert.DoesNotContain(
            "metadata",
            acceptResult.Violation,
            StringComparison.OrdinalIgnoreCase
        );
        var exception = await Assert.ThrowsAsync<ArgumentException>(async () =>
            await manager.DryRunAsync(plan, TestContext.Current.CancellationToken)
        );
        Assert.Contains(
            "can be executed by apply or remove",
            exception.Message,
            StringComparison.Ordinal
        );
        Assert.DoesNotContain("metadata", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task
        ValidatePlanAndNoFilesystemDryRunRejectUnsupportedNuGetPluginLayoutRemoveAdapter()
    {
        var manager = new ConfigurationManager();
        ConfigurationChangePlan setPlan = CreatePhysicalTargetPlan(
            ConfigurationTargetKind.NuGetPluginLayout,
            CreateNuGetPluginLayoutTargetRoot()
        );
        ConfigurationChangePlan plan = setPlan with
        {
            Changes =
            [
                setPlan.Changes[0] with
                {
                    Operation = ConfigurationChangeOperation.RemoveAdapter,
                    Value = null,
                    PreviousOwnedEntryMetadata = "previous-adapter-entry",
                },
            ],
        };

        ConfigurationPlanValidationResult validationResult = manager.ValidatePlan(plan);
        ConfigurationPlanValidationResult acceptResult = await manager.AcceptPlanAsync(
            plan,
            TestContext.Current.CancellationToken
        );

        Assert.False(validationResult.IsValid);
        Assert.NotNull(validationResult.Violation);
        Assert.Contains(
            "remove-adapter",
            validationResult.Violation,
            StringComparison.OrdinalIgnoreCase
        );
        Assert.False(acceptResult.IsValid);
        Assert.NotNull(acceptResult.Violation);
        Assert.Contains(
            "remove-adapter",
            acceptResult.Violation,
            StringComparison.OrdinalIgnoreCase
        );

        var exception = await Assert.ThrowsAsync<ArgumentException>(async () =>
            await manager.DryRunAsync(plan, TestContext.Current.CancellationToken)
        );

        Assert.Contains("remove-adapter", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task
        ValidatePlanAcceptPlanAndNoFilesystemDryRunRejectUnsupportedNuGetPluginLayoutKey()
    {
        var manager = new ConfigurationManager();
        ConfigurationChangePlan setPlan = CreatePhysicalTargetPlan(
            ConfigurationTargetKind.NuGetPluginLayout,
            CreateNuGetPluginLayoutTargetRoot()
        );
        ConfigurationChangePlan plan = setPlan with
        {
            Changes =
            [
                setPlan.Changes[0] with
                {
                    Key = "unexpected-key",
                },
            ],
        };

        ConfigurationPlanValidationResult validationResult = manager.ValidatePlan(plan);
        ConfigurationPlanValidationResult acceptResult = await manager.AcceptPlanAsync(
            plan,
            TestContext.Current.CancellationToken
        );

        Assert.False(validationResult.IsValid);
        Assert.NotNull(validationResult.Violation);
        Assert.Contains(
            "canonical physical target key",
            validationResult.Violation,
            StringComparison.Ordinal
        );
        Assert.False(acceptResult.IsValid);
        Assert.NotNull(acceptResult.Violation);
        Assert.Contains(
            "canonical physical target key",
            acceptResult.Violation,
            StringComparison.Ordinal
        );

        var exception = await Assert.ThrowsAsync<ArgumentException>(async () =>
            await manager.DryRunAsync(plan, TestContext.Current.CancellationToken)
        );
        Assert.Contains(
            "canonical physical target key",
            exception.Message,
            StringComparison.Ordinal
        );
    }

    [Fact]
    public async Task
        ValidatePlanAcceptPlanAndNoFilesystemDryRunRejectWhitespaceNuGetPluginLayoutValue()
    {
        var manager = new ConfigurationManager();
        ConfigurationChangePlan setPlan = CreatePhysicalTargetPlan(
            ConfigurationTargetKind.NuGetPluginLayout,
            CreateNuGetPluginLayoutTargetRoot()
        );
        ConfigurationChangePlan plan = setPlan with
        {
            Changes =
            [
                setPlan.Changes[0] with
                {
                    Value = "   ",
                },
            ],
        };

        ConfigurationPlanValidationResult validationResult = manager.ValidatePlan(plan);
        ConfigurationPlanValidationResult acceptResult = await manager.AcceptPlanAsync(
            plan,
            TestContext.Current.CancellationToken
        );

        Assert.False(validationResult.IsValid);
        Assert.NotNull(validationResult.Violation);
        Assert.Contains(
            "non-empty",
            validationResult.Violation,
            StringComparison.OrdinalIgnoreCase
        );
        Assert.False(acceptResult.IsValid);
        Assert.NotNull(acceptResult.Violation);
        Assert.Contains(
            "non-empty",
            acceptResult.Violation,
            StringComparison.OrdinalIgnoreCase
        );

        var exception = await Assert.ThrowsAsync<ArgumentException>(async () =>
            await manager.DryRunAsync(plan, TestContext.Current.CancellationToken)
        );

        Assert.Contains("non-empty", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task
        ValidatePlanAcceptPlanAndNoFilesystemDryRunRejectSecretNuGetPluginLayoutValue()
    {
        var manager = new ConfigurationManager();
        ConfigurationChangePlan setPlan = CreatePhysicalTargetPlan(
            ConfigurationTargetKind.NuGetPluginLayout,
            CreateNuGetPluginLayoutTargetRoot()
        );
        ConfigurationChangePlan plan = setPlan with
        {
            Changes =
            [
                setPlan.Changes[0] with
                {
                    IsSecretValue = true,
                    Value = "planned-value",
                },
            ],
        };

        ConfigurationPlanValidationResult validationResult = manager.ValidatePlan(plan);
        ConfigurationPlanValidationResult acceptResult = await manager.AcceptPlanAsync(
            plan,
            TestContext.Current.CancellationToken
        );

        Assert.False(validationResult.IsValid);
        Assert.NotNull(validationResult.Violation);
        Assert.Contains("secret", validationResult.Violation, StringComparison.OrdinalIgnoreCase);
        Assert.False(acceptResult.IsValid);
        Assert.NotNull(acceptResult.Violation);
        Assert.Contains("secret", acceptResult.Violation, StringComparison.OrdinalIgnoreCase);

        var exception = await Assert.ThrowsAsync<ArgumentException>(async () =>
            await manager.DryRunAsync(plan, TestContext.Current.CancellationToken)
        );

        Assert.Contains("secret", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task
        ValidateAcceptDryRunRejectMixedPhase4DAndNonPhase4DTargetsBeforeProjection()
    {
        var manager = new ConfigurationManager();
        ConfigurationChangePlan setPlan = CreatePhysicalTargetPlan(
            ConfigurationTargetKind.GitConfig,
            "/config/planning-mixed-phase4d.gitconfig"
        );
        ConfigurationChangePlan plan = setPlan with
        {
            // Null safe metadata is contract-valid but ownership-manifest projection-invalid.
            // The Phase 4D physical scaffold guard must reject before projection sees it.
            Manifest = setPlan.Manifest with { SafeMetadata = null! },
            Changes =
            [
                setPlan.Changes[0],
                CreateNpmrcFileChange("/config/planning-mixed-phase4d.npmrc"),
            ],
        };

        ConfigurationPlanValidationResult validationResult = manager.ValidatePlan(plan);
        ConfigurationPlanValidationResult acceptResult = await manager.AcceptPlanAsync(
            plan,
            TestContext.Current.CancellationToken
        );

        Assert.False(validationResult.IsValid);
        Assert.NotNull(validationResult.Violation);
        Assert.Contains(
            "mixing 4D physical configuration targets",
            validationResult.Violation,
            StringComparison.Ordinal
        );
        Assert.DoesNotContain(
            "metadata",
            validationResult.Violation,
            StringComparison.OrdinalIgnoreCase
        );
        Assert.False(acceptResult.IsValid);
        Assert.NotNull(acceptResult.Violation);
        Assert.Contains(
            "mixing 4D physical configuration targets",
            acceptResult.Violation,
            StringComparison.Ordinal
        );
        Assert.DoesNotContain(
            "metadata",
            acceptResult.Violation,
            StringComparison.OrdinalIgnoreCase
        );
        var exception = await Assert.ThrowsAsync<ArgumentException>(async () =>
            await manager.DryRunAsync(plan, TestContext.Current.CancellationToken)
        );
        Assert.Contains(
            "mixing 4D physical configuration targets",
            exception.Message,
            StringComparison.Ordinal
        );
        Assert.DoesNotContain("metadata", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task
        ValidateAcceptDryRunRejectMultiplePhase4DTargetKindsBeforeProjection()
    {
        var manager = new ConfigurationManager();
        ConfigurationChangePlan setPlan = CreatePhysicalTargetPlan(
            ConfigurationTargetKind.GitConfig,
            "/config/planning-multi-kind-phase4d.gitconfig"
        );
        ConfigurationChangePlan plan = setPlan with
        {
            // Null safe metadata is contract-valid but ownership-manifest projection-invalid.
            // The Phase 4D physical scaffold guard must reject before projection sees it.
            Manifest = setPlan.Manifest with { SafeMetadata = null! },
            Changes =
            [
                setPlan.Changes[0],
                CreatePhysicalTargetChange(
                    ConfigurationTargetKind.KeyringShim,
                    "/config/planning-multi-kind-phase4d-keyring-shim"
                ),
            ],
        };

        ConfigurationPlanValidationResult validationResult = manager.ValidatePlan(plan);
        ConfigurationPlanValidationResult acceptResult = await manager.AcceptPlanAsync(
            plan,
            TestContext.Current.CancellationToken
        );

        Assert.False(validationResult.IsValid);
        Assert.NotNull(validationResult.Violation);
        Assert.Contains(
            "only one 4D physical target kind",
            validationResult.Violation,
            StringComparison.Ordinal
        );
        Assert.DoesNotContain(
            "metadata",
            validationResult.Violation,
            StringComparison.OrdinalIgnoreCase
        );
        Assert.False(acceptResult.IsValid);
        Assert.NotNull(acceptResult.Violation);
        Assert.Contains(
            "only one 4D physical target kind",
            acceptResult.Violation,
            StringComparison.Ordinal
        );
        Assert.DoesNotContain(
            "metadata",
            acceptResult.Violation,
            StringComparison.OrdinalIgnoreCase
        );
        var exception = await Assert.ThrowsAsync<ArgumentException>(async () =>
            await manager.DryRunAsync(plan, TestContext.Current.CancellationToken)
        );
        Assert.Contains(
            "only one 4D physical target kind",
            exception.Message,
            StringComparison.Ordinal
        );
        Assert.DoesNotContain("metadata", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData("/state/.azureauth-credprovider.fs.lock")]
    [InlineData("/state/.azureauth-credprovider.fs.lock/descendant")]
    [InlineData("/state/.azureauth-credprovider.lifecycle-locks")]
    [InlineData("/state/.azureauth-credprovider.lifecycle-locks/descendant")]
    public void ConstructorRejectsReservedInternalOwnershipManifestPath(string manifestPath)
    {
        var fileSystem = new InMemoryFileSystem(InMemoryPathSemantics.Posix);

        var exception = Assert.Throws<ArgumentException>(
            () => new ConfigurationManager(fileSystem, manifestPath)
        );

        Assert.Equal("ownershipManifestPath", exception.ParamName);
        Assert.Contains(
            "reserved internal filesystem artifact",
            exception.Message,
            StringComparison.Ordinal
        );
    }

    [Theory]
    [InlineData("state/manifest.json", "/state/.azureauth-credprovider.fs.lock")]
    [InlineData(
        "state/nested-manifest.json",
        "/state/.azureauth-credprovider.fs.lock/manifest.json"
    )]
    [InlineData("locks/manifest.json", "/state/.azureauth-credprovider.lifecycle-locks")]
    [InlineData(
        "locks/nested-manifest.json",
        "/state/.azureauth-credprovider.lifecycle-locks/manifest.json"
    )]
    public void
        ConstructorRejectsOwnershipManifestPathWhenFullPathResolvesToReservedInternalArtifact(
            string rawManifestPath,
            string normalizedManifestPath
        )
    {
        var innerFileSystem = new InMemoryFileSystem(InMemoryPathSemantics.Posix);
        var fileSystem = new FullPathRemappingFileSystem(
            innerFileSystem,
            rawManifestPath,
            normalizedManifestPath
        );

        var exception = Assert.Throws<ArgumentException>(
            () => new ConfigurationManager(fileSystem, rawManifestPath)
        );

        Assert.Equal("ownershipManifestPath", exception.ParamName);
        Assert.Contains(
            "reserved internal filesystem artifact",
            exception.Message,
            StringComparison.Ordinal
        );
    }

    [Fact]
    public void ConstructorWrapsOwnershipManifestPathNormalizationFailure()
    {
        var fileSystem = new InMemoryFileSystem(InMemoryPathSemantics.Posix);
        var injectedException = new IOException(
            "Injected ownership manifest path normalization failure."
        );
        fileSystem.FailNextCall(injectedException);

        var exception = Assert.Throws<ArgumentException>(
            () => new ConfigurationManager(fileSystem, "state/manifest.json")
        );

        Assert.Equal("ownershipManifestPath", exception.ParamName);
        Assert.Same(injectedException, exception.InnerException);
        Assert.Contains(
            "normalizable physical path",
            exception.Message,
            StringComparison.Ordinal
        );
        Assert.Contains(
            fileSystem.Calls,
            call =>
                string.Equals(
                    call.Operation,
                    nameof(IFileSystem.GetFullPath),
                    StringComparison.Ordinal
                )
                && string.Equals(call.Path, "/state/manifest.json", StringComparison.Ordinal)
        );
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
        FilesystemBackedAllOperationsRejectCiTemporaryTargetCollidingWithOwnershipManifestPath(
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

    [Theory]
    [MemberData(nameof(PhysicalTargetOwnershipManifestCollisionCases))]
    public async Task
        FilesystemBackedValidationAndDryRunRejectPhysicalTargetCollidingWithOwnershipManifestPath(
            string methodName,
            ConfigurationTargetKind targetKind,
            string collidingTargetPath,
            string manifestPath
        )
    {
        await AssertPhysicalTargetOwnershipManifestCollisionRejectedAsync(
            methodName,
            targetKind,
            collidingTargetPath,
            manifestPath
        );
    }

    [Theory]
    [MemberData(nameof(Phase4DPhysicalTargetOwnershipManifestCollisionCases))]
    public async Task
        FilesystemBackedPhase4DValidationAndDryRunRejectPhysicalTargetManifestCollision(
            string methodName,
            ConfigurationTargetKind targetKind,
            string collidingTargetPath,
            string manifestPath
        )
    {
        await AssertPhysicalTargetOwnershipManifestCollisionRejectedAsync(
            methodName,
            targetKind,
            collidingTargetPath,
            manifestPath
        );
    }

    [Theory]
    [MemberData(nameof(Phase4DPhysicalTargetKinds))]
    public async Task FilesystemBackedDryRunProjectsPhase4DPhysicalTargetKindsWithoutMutation(
        ConfigurationTargetKind targetKind
    )
    {
        var fileSystem = new InMemoryFileSystem(InMemoryPathSemantics.Posix);
        const string manifestPath = "/state/phase4d-manifest.json";
        string targetPath =
            $"/config/{targetKind.ToString().ToLower(CultureInfo.InvariantCulture)}";
        var manager = new ConfigurationManager(fileSystem, manifestPath);
        ConfigurationChangePlan plan = CreatePhysicalTargetPlan(targetKind, targetPath);

        ConfigurationPlanResult result = await manager.DryRunAsync(
            plan,
            TestContext.Current.CancellationToken
        );

        Assert.Equal(ConfigurationPlanState.Planned, result.State);
        ConfigurationPlannedOperation operation = Assert.Single(result.PlannedOperations);
        Assert.Equal(targetKind, operation.Change.TargetKind);
        ConfigurationOwnershipManifestEntry entry = Assert.Single(
            result.OwnershipManifest!.Entries
        );
        Assert.Equal(targetKind, entry.TargetKind);
        Assert.Equal(targetPath, entry.TargetPathOrName);
        Assert.False(fileSystem.FileExists(manifestPath));
        Assert.False(fileSystem.FileExists(targetPath));
        AssertNoFilesystemMutationOrLockCalls(fileSystem.Calls);
    }

    [Fact]
    public async Task
        FilesystemBackedValidateAcceptDryRunApplyRejectsNormalizedReservedPhase4DTarget()
    {
        var innerFileSystem = new InMemoryFileSystem(InMemoryPathSemantics.Posix);
        const string manifestPath = "/state/normalized-reserved-first-physical-manifest.json";
        const string rawTargetPath = "/config/normalizes-to-reserved-phase4d-target";
        const string normalizedReservedTargetPath =
            "/config/.azureauth-credprovider.lifecycle-locks/phase4d-target";
        var fileSystem = new FullPathRemappingFileSystem(
            innerFileSystem,
            rawTargetPath,
            normalizedReservedTargetPath
        );
        var dispatcher = new RecordingPhysicalTargetWriterDispatcher(fileSystem);
        var manager = new ConfigurationManager(fileSystem, manifestPath, dispatcher);
        ConfigurationChangePlan setPlan = CreatePhysicalTargetPlan(
            ConfigurationTargetKind.GitConfig,
            rawTargetPath
        );
        ConfigurationChangePlan plan = setPlan with
        {
            // Null safe metadata is contract-valid but ownership-manifest projection-invalid.
            // The filesystem-aware reserved path guard must reject normalized physical target
            // paths before projection validates manifest metadata.
            Manifest = setPlan.Manifest with { SafeMetadata = null! },
        };

        ConfigurationPlanValidationResult validationResult = manager.ValidatePlan(plan);
        ConfigurationPlanValidationResult acceptResult = await manager.AcceptPlanAsync(
            plan,
            TestContext.Current.CancellationToken
        );

        Assert.False(validationResult.IsValid);
        Assert.NotNull(validationResult.Violation);
        Assert.Contains(
            "reserved internal filesystem artifact",
            validationResult.Violation,
            StringComparison.Ordinal
        );
        Assert.DoesNotContain(
            "metadata",
            validationResult.Violation,
            StringComparison.OrdinalIgnoreCase
        );
        Assert.False(acceptResult.IsValid);
        Assert.NotNull(acceptResult.Violation);
        Assert.Contains(
            "reserved internal filesystem artifact",
            acceptResult.Violation,
            StringComparison.Ordinal
        );
        Assert.DoesNotContain(
            "metadata",
            acceptResult.Violation,
            StringComparison.OrdinalIgnoreCase
        );
        var dryRunException = await Assert.ThrowsAsync<ArgumentException>(async () =>
            await manager.DryRunAsync(plan, TestContext.Current.CancellationToken)
        );
        Assert.Contains(
            "reserved internal filesystem artifact",
            dryRunException.Message,
            StringComparison.Ordinal
        );
        Assert.DoesNotContain(
            "metadata",
            dryRunException.Message,
            StringComparison.OrdinalIgnoreCase
        );
        Assert.Empty(dispatcher.Requests);
        Assert.False(innerFileSystem.Files.ContainsKey(manifestPath));
        Assert.False(innerFileSystem.Files.ContainsKey(rawTargetPath));
        Assert.False(innerFileSystem.Files.ContainsKey(normalizedReservedTargetPath));
        AssertNoFilesystemMutationOrLockCalls(innerFileSystem.Calls);

        innerFileSystem.Calls.Clear();
        var applyException = await Assert.ThrowsAsync<ArgumentException>(async () =>
            await manager.ApplyAsync(plan, TestContext.Current.CancellationToken)
        );
        Assert.Contains(
            "reserved internal filesystem artifact",
            applyException.Message,
            StringComparison.Ordinal
        );
        Assert.DoesNotContain(
            "metadata",
            applyException.Message,
            StringComparison.OrdinalIgnoreCase
        );
        Assert.Empty(dispatcher.Requests);
        Assert.False(innerFileSystem.Files.ContainsKey(manifestPath));
        Assert.False(innerFileSystem.Files.ContainsKey(rawTargetPath));
        Assert.False(innerFileSystem.Files.ContainsKey(normalizedReservedTargetPath));
        AssertNoFilesystemMutationOrLockCalls(innerFileSystem.Calls);
    }

    [Fact]
    public async Task
        FilesystemBackedDryRunRejectsUnsupportedPhase4DPhysicalShapeBeforeManifestProjection()
    {
        var fileSystem = new InMemoryFileSystem(InMemoryPathSemantics.Posix);
        const string manifestPath = "/state/shape-first-physical-manifest.json";
        const string firstTargetPath = "/config/shape-first-physical.gitconfig";
        const string secondTargetPath = "/config/shape-first-physical-second.gitconfig";
        var dispatcher = new RecordingPhysicalTargetWriterDispatcher(fileSystem);
        var manager = new ConfigurationManager(fileSystem, manifestPath, dispatcher);
        ConfigurationChangePlan setPlan = CreatePhysicalTargetPlan(
            ConfigurationTargetKind.GitConfig,
            firstTargetPath
        );
        ConfigurationChangePlan plan = setPlan with
        {
            Manifest = setPlan.Manifest with { SafeMetadata = null! },
            Changes =
            [
                setPlan.Changes[0],
                setPlan.Changes[0] with
                {
                    Key = "credential.https://dev.azure.com.useHttpPath",
                    TargetPathOrName = secondTargetPath,
                    Value = "true",
                },
            ],
        };

        var exception = await Assert.ThrowsAsync<NotSupportedException>(async () =>
            await manager.DryRunAsync(plan, TestContext.Current.CancellationToken)
        );

        Assert.Contains(
            "only one 4D physical target change",
            exception.Message,
            StringComparison.Ordinal
        );
        Assert.DoesNotContain("metadata", exception.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Empty(dispatcher.Requests);
        Assert.False(fileSystem.Files.ContainsKey(manifestPath));
        Assert.False(fileSystem.Files.ContainsKey(firstTargetPath));
        Assert.False(fileSystem.Files.ContainsKey(secondTargetPath));
        AssertNoFilesystemStateReadCallsBeforeLockAcquisition(fileSystem.Calls);
        AssertNoFilesystemMutationOrLockCalls(fileSystem.Calls);
    }

    [Theory]
    [InlineData(nameof(IConfigurationManager.ApplyAsync))]
    [InlineData(nameof(IConfigurationManager.RemoveAsync))]
    public async Task
        FilesystemBackedApplyRemoveRejectUnsupportedPhase4DPhysicalShapeBeforeManifestProjection(
            string methodName
        )
    {
        var fileSystem = new InMemoryFileSystem(InMemoryPathSemantics.Posix);
        const string manifestPath = "/state/shape-first-apply-remove-physical-manifest.json";
        const string firstTargetPath = "/config/shape-first-apply-remove-physical.gitconfig";
        const string secondTargetPath =
            "/config/shape-first-apply-remove-physical-second.gitconfig";
        var dispatcher = new RecordingPhysicalTargetWriterDispatcher(fileSystem);
        var manager = new ConfigurationManager(fileSystem, manifestPath, dispatcher);
        ConfigurationChangePlan setPlan = CreatePhysicalTargetPlan(
            ConfigurationTargetKind.GitConfig,
            firstTargetPath
        );
        bool remove = methodName == nameof(IConfigurationManager.RemoveAsync);
        ConfigurationChangeOperation operation = remove
            ? ConfigurationChangeOperation.Remove
            : ConfigurationChangeOperation.Set;
        string? firstValue = remove ? null : "helper";
        string? secondValue = remove ? null : "true";
        string? previousMetadata = remove ? "previous-physical-target-entry" : null;
        ConfigurationChangePlan plan = setPlan with
        {
            Manifest = setPlan.Manifest with { SafeMetadata = null! },
            Changes =
            [
                setPlan.Changes[0] with
                {
                    Key = "credential.helper",
                    Operation = operation,
                    Value = firstValue,
                    PreviousOwnedEntryMetadata = previousMetadata,
                },
                setPlan.Changes[0] with
                {
                    Key = "credential.https://dev.azure.com.useHttpPath",
                    TargetPathOrName = secondTargetPath,
                    Operation = operation,
                    Value = secondValue,
                    PreviousOwnedEntryMetadata = previousMetadata,
                },
            ],
        };

        var exception = await Assert.ThrowsAsync<NotSupportedException>(async () =>
            await CreateExecutionCall(manager, methodName, plan)()
        );

        Assert.Contains(
            "only one 4D physical target change",
            exception.Message,
            StringComparison.Ordinal
        );
        Assert.DoesNotContain("metadata", exception.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Empty(dispatcher.Requests);
        Assert.False(fileSystem.Files.ContainsKey(manifestPath));
        Assert.False(fileSystem.Files.ContainsKey(firstTargetPath));
        Assert.False(fileSystem.Files.ContainsKey(secondTargetPath));
        AssertNoFilesystemMutationOrLockCalls(fileSystem.Calls);
    }

    [Fact]
    public async Task
        FilesystemBackedPhase4DDryRunRejectsProjectionOnlyPhysicalTargetStaleManifestHash()
    {
        var fileSystem = new InMemoryFileSystem(InMemoryPathSemantics.Posix);
        const string manifestPath = "/state/stale-physical-manifest.json";
        const string targetPath = "/config/stale-physical.gitconfig";
        ConfigurationChangePlan plan = CreatePhysicalTargetPlan(
            ConfigurationTargetKind.GitConfig,
            targetPath
        );
        string existingManifestJson = await CreateDryRunManifestJsonAsync(plan);
        fileSystem.AtomicWriteAllText(manifestPath, existingManifestJson);
        fileSystem.Calls.Clear();
        var manager = new ConfigurationManager(fileSystem, manifestPath);
        ConfigurationChangePlan stalePlan = plan with
        {
            Manifest = plan.Manifest with
            {
                PreviousOwnedEntryHash = HashMetadata("not-the-existing-manifest"),
            },
        };

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(async () =>
            await manager.DryRunAsync(stalePlan, TestContext.Current.CancellationToken)
        );

        Assert.Contains("before-state hash", exception.Message, StringComparison.Ordinal);
        Assert.Equal(existingManifestJson, fileSystem.ReadAllText(manifestPath));
        Assert.False(fileSystem.FileExists(targetPath));
        AssertNoFilesystemMutationOrLockCalls(fileSystem.Calls);
    }

    [Fact]
    public async Task
        FilesystemBackedPhase4DDryRunRejectsProjectionOnlyPhysicalTargetForeignManifest()
    {
        var fileSystem = new InMemoryFileSystem(InMemoryPathSemantics.Posix);
        const string manifestPath = "/state/foreign-physical-manifest.json";
        const string targetPath = "/config/foreign-physical.gitconfig";
        ConfigurationChangePlan plan = CreatePhysicalTargetPlan(
            ConfigurationTargetKind.GitConfig,
            targetPath
        );
        ConfigurationOwnershipManifest plannedManifest = await CreateDryRunManifestAsync(plan);
        string foreignManifestJson = RawOwnershipManifestJson(
            plannedManifest with
            {
                OwnerProductId = "foreign-product",
            }
        );
        fileSystem.AtomicWriteAllText(manifestPath, foreignManifestJson);
        fileSystem.Calls.Clear();
        var manager = new ConfigurationManager(fileSystem, manifestPath);

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(async () =>
            await manager.DryRunAsync(plan, TestContext.Current.CancellationToken)
        );

        Assert.Contains("existing manifest identity", exception.Message, StringComparison.Ordinal);
        Assert.Equal(foreignManifestJson, fileSystem.ReadAllText(manifestPath));
        Assert.False(fileSystem.FileExists(targetPath));
        AssertNoFilesystemMutationOrLockCalls(fileSystem.Calls);
    }

    [Fact]
    public async Task
        FilesystemBackedPhase4DDryRunMergesProjectionOnlyPhysicalTargetManifestEntries()
    {
        var fileSystem = new InMemoryFileSystem(InMemoryPathSemantics.Posix);
        const string manifestPath = "/state/merge-physical-manifest.json";
        const string existingTargetPath = "/config/existing-physical.gitconfig";
        const string newTargetPath = "/config/new-physical.gitconfig";
        ConfigurationChangePlan plan = CreatePhysicalTargetPlan(
            ConfigurationTargetKind.GitConfig,
            newTargetPath
        );
        ConfigurationOwnershipManifest plannedManifest = await CreateDryRunManifestAsync(plan);
        ConfigurationOwnershipManifestEntry plannedEntry = Assert.Single(plannedManifest.Entries);
        ConfigurationOwnershipManifest existingManifest = plannedManifest with
        {
            PlanId = "previous-physical-plan",
            ChangeSetId = "previous-physical-changeset",
            Entries =
            [
                plannedEntry with
                {
                    TargetPathOrName = existingTargetPath,
                    Key = "credential.helper",
                },
            ],
        };
        string existingManifestJson = RawOwnershipManifestJson(existingManifest);
        const string existingGitConfig = "[credential]\n\thelper = \"planned-value\"\n";
        fileSystem.AtomicWriteAllText(existingTargetPath, existingGitConfig);
        fileSystem.AtomicWriteAllText(manifestPath, existingManifestJson);
        fileSystem.Calls.Clear();
        var manager = new ConfigurationManager(fileSystem, manifestPath);
        ConfigurationChangePlan planWithManifestHash = plan with
        {
            Manifest = plan.Manifest with
            {
                PreviousOwnedEntryHash = HashMetadata(existingManifestJson),
            },
        };

        ConfigurationPlanResult result = await manager.DryRunAsync(
            planWithManifestHash,
            TestContext.Current.CancellationToken
        );

        Assert.Equal(ConfigurationPlanState.Planned, result.State);
        Assert.Equal(existingManifestJson, fileSystem.ReadAllText(manifestPath));
        Assert.Equal(existingGitConfig, fileSystem.ReadAllText(existingTargetPath));
        Assert.False(fileSystem.FileExists(newTargetPath));
        ConfigurationOwnershipManifest simulatedManifest = result.OwnershipManifest!;
        Assert.Equal(plan.PlanId, simulatedManifest.PlanId);
        Assert.Equal(plan.ChangeSetId, simulatedManifest.ChangeSetId);
        Assert.Equal(
            planWithManifestHash.Manifest.PreviousOwnedEntryHash,
            simulatedManifest.PreviousOwnedEntryHash
        );
        Assert.Collection(
            simulatedManifest.Entries,
            entry =>
            {
                Assert.Equal(1, entry.Sequence);
                Assert.Equal(existingTargetPath, entry.TargetPathOrName);
                Assert.Equal("credential.helper", entry.Key);
            },
            entry =>
            {
                Assert.Equal(2, entry.Sequence);
                Assert.Equal(newTargetPath, entry.TargetPathOrName);
                Assert.Equal("credential.helper", entry.Key);
            }
        );
        AssertNoFilesystemMutationOrLockCalls(fileSystem.Calls);
    }

    [Fact]
    public async Task
        FilesystemBackedPhase4DDryRunMatchesPhysicalTargetsByNormalizedEquivalentPath()
    {
        var fileSystem = new InMemoryFileSystem(InMemoryPathSemantics.Posix);
        const string manifestPath = "/state/normalized-physical-manifest.json";
        const string manifestTargetPath = "/config/normalized-physical/.gitconfig";
        const string planTargetPath = "/config/normalized-physical/sub/../.gitconfig";
        ConfigurationChangePlan createPlan = CreatePhysicalTargetPlan(
            ConfigurationTargetKind.GitConfig,
            manifestTargetPath
        );
        ConfigurationOwnershipManifest existingManifest = await CreateDryRunManifestAsync(
            createPlan
        );
        string existingManifestJson = RawOwnershipManifestJson(existingManifest);
        fileSystem.AtomicWriteAllText(
            manifestTargetPath,
            "[credential]\n\thelper = \"planned-value\"\n"
        );
        fileSystem.AtomicWriteAllText(manifestPath, existingManifestJson);
        fileSystem.Calls.Clear();
        var manager = new ConfigurationManager(fileSystem, manifestPath);
        ConfigurationChangePlan updatePlan = CreatePhysicalTargetPlan(
            ConfigurationTargetKind.GitConfig,
            planTargetPath
        ) with
        {
            Manifest = createPlan.Manifest with
            {
                PreviousOwnedEntryHash = HashMetadata(existingManifestJson),
            },
            Changes =
            [
                createPlan.Changes[0] with
                {
                    Operation = ConfigurationChangeOperation.Update,
                    TargetPathOrName = planTargetPath,
                    PreviousOwnedEntryMetadata = "previous-physical-target-entry",
                    Value = "updated-planned-value",
                },
            ],
        };

        ConfigurationPlanResult result = await manager.DryRunAsync(
            updatePlan,
            TestContext.Current.CancellationToken
        );

        Assert.Equal(ConfigurationPlanState.Planned, result.State);
        ConfigurationOwnershipManifestEntry entry = Assert.Single(
            result.OwnershipManifest!.Entries
        );
        Assert.Equal(manifestTargetPath, entry.TargetPathOrName);
        Assert.Equal(ConfigurationChangeOperation.Update, entry.Operation);
        AssertNoFilesystemMutationOrLockCalls(fileSystem.Calls);
    }

    [Fact]
    public async Task
        FilesystemBackedPhase4DDryRunProjectsNuGetPluginLayoutWithoutMutation()
    {
        var fileSystem = new InMemoryFileSystem(InMemoryPathSemantics.Posix);
        const string manifestPath = "/state/key-casing-physical-manifest.json";
        string targetPath = CreateNuGetPluginLayoutTargetRoot();
        var manager = new ConfigurationManager(
            fileSystem,
            manifestPath,
            new ConfigurationPhysicalTargetWriterDispatcher(fileSystem)
        );
        ConfigurationChangePlan casingPlan = CreatePhysicalTargetPlan(
            ConfigurationTargetKind.NuGetPluginLayout,
            targetPath
        );

        ConfigurationPlanResult result = await manager.DryRunAsync(
            casingPlan,
            TestContext.Current.CancellationToken
        );

        Assert.Equal(ConfigurationPlanState.Planned, result.State);
        ConfigurationOwnershipManifestEntry entry = Assert.Single(
            result.OwnershipManifest!.Entries
        );
        Assert.Equal(ConfigurationTargetKind.NuGetPluginLayout, entry.TargetKind);
        Assert.Equal(targetPath, entry.TargetPathOrName);
        Assert.Equal("physical-target", entry.Key);
        Assert.False(fileSystem.FileExists(manifestPath));
        AssertNoFilesystemMutationOrLockCalls(fileSystem.Calls);
    }

    [Fact]
    public async Task
        FilesystemBackedPhase4DDryRunRejectsUnsupportedExistingNonCiEntryBeforeProjectionMerge()
    {
        var fileSystem = new InMemoryFileSystem(InMemoryPathSemantics.Posix);
        const string manifestPath = "/state/existing-npmrc-projection-conflict-manifest.json";
        const string targetPath = "/config/existing-npmrc-projection-conflict";
        ConfigurationChangePlan plan = CreatePhysicalTargetPlan(
            ConfigurationTargetKind.GitConfig,
            targetPath
        );
        ConfigurationOwnershipManifest plannedManifest = await CreateDryRunManifestAsync(plan);
        ConfigurationOwnershipManifestEntry plannedEntry = Assert.Single(plannedManifest.Entries);
        ConfigurationOwnershipManifest existingManifest = plannedManifest with
        {
            PlanId = "previous-existing-npmrc-projection-conflict-plan",
            ChangeSetId = "previous-existing-npmrc-projection-conflict-changeset",
            Entries =
            [
                plannedEntry with
                {
                    TargetKind = ConfigurationTargetKind.Npmrc,
                    Key = "registry",
                },
            ],
        };
        string existingManifestJson = RawOwnershipManifestJson(existingManifest);
        fileSystem.AtomicWriteAllText(manifestPath, existingManifestJson);
        fileSystem.Calls.Clear();
        var manager = new ConfigurationManager(fileSystem, manifestPath);
        ConfigurationChangePlan planWithManifestHash = plan with
        {
            Manifest = plan.Manifest with
            {
                PreviousOwnedEntryHash = HashMetadata(existingManifestJson),
            },
        };

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(async () =>
            await manager.DryRunAsync(planWithManifestHash, TestContext.Current.CancellationToken)
        );

        Assert.Contains(
            "registered retained-proof validator",
            exception.Message,
            StringComparison.Ordinal
        );
        Assert.Equal(existingManifestJson, fileSystem.ReadAllText(manifestPath));
        Assert.False(fileSystem.FileExists(targetPath));
        AssertNoFilesystemMutationOrLockCalls(fileSystem.Calls);
    }

    [Fact]
    public async Task
        FilesystemBackedPhase4DDryRunRejectsSamePathDifferentNonCiPhysicalEntries()
    {
        var fileSystem = new InMemoryFileSystem(InMemoryPathSemantics.Posix);
        const string manifestPath = "/state/existing-non-ci-physical-conflict-manifest.json";
        const string existingTargetPath = "/config/existing-non-ci-physical-conflict";
        const string planTargetPath = "/config/existing-non-ci-physical-conflict-plan-target";
        ConfigurationChangePlan plan = CreatePhysicalTargetPlan(
            ConfigurationTargetKind.GitConfig,
            planTargetPath
        );
        ConfigurationOwnershipManifest plannedManifest = await CreateDryRunManifestAsync(plan);
        ConfigurationOwnershipManifestEntry plannedEntry = Assert.Single(plannedManifest.Entries);
        ConfigurationOwnershipManifest existingManifest = plannedManifest with
        {
            PlanId = "previous-existing-non-ci-physical-conflict-plan",
            ChangeSetId = "previous-existing-non-ci-physical-conflict-changeset",
            Entries =
            [
                plannedEntry with
                {
                    TargetKind = ConfigurationTargetKind.Npmrc,
                    TargetPathOrName = existingTargetPath,
                    Key = "registry",
                },
                plannedEntry with
                {
                    Sequence = 2,
                    TargetKind = ConfigurationTargetKind.GitConfig,
                    TargetPathOrName = existingTargetPath,
                    Key = "credential.helper",
                },
            ],
        };
        string existingManifestJson = RawOwnershipManifestJson(existingManifest);
        fileSystem.AtomicWriteAllText(manifestPath, existingManifestJson);
        fileSystem.Calls.Clear();
        var manager = new ConfigurationManager(fileSystem, manifestPath);
        ConfigurationChangePlan planWithManifestHash = plan with
        {
            Manifest = plan.Manifest with
            {
                PreviousOwnedEntryHash = HashMetadata(existingManifestJson),
            },
        };

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(async () =>
            await manager.DryRunAsync(planWithManifestHash, TestContext.Current.CancellationToken)
        );

        Assert.Contains("same physical target path", exception.Message, StringComparison.Ordinal);
        Assert.Equal(existingManifestJson, fileSystem.ReadAllText(manifestPath));
        Assert.False(fileSystem.FileExists(existingTargetPath));
        Assert.False(fileSystem.FileExists(planTargetPath));
        AssertNoFilesystemMutationOrLockCalls(fileSystem.Calls);
    }

    [Theory]
    [MemberData(nameof(ReservedPhase4DPhysicalTargetPaths))]
    public async Task FilesystemBackedPhase4DDryRunRejectsExistingReservedNonCiPhysicalEntry(
        ConfigurationTargetKind existingTargetKind,
        string reservedTargetPath
    )
    {
        var fileSystem = new InMemoryFileSystem(InMemoryPathSemantics.Posix);
        const string manifestPath = "/state/existing-reserved-physical-entry-manifest.json";
        const string targetPath = "/config/existing-reserved-physical-entry";
        ConfigurationChangePlan plan = CreatePhysicalTargetPlan(
            ConfigurationTargetKind.GitConfig,
            targetPath
        );
        ConfigurationOwnershipManifest plannedManifest = await CreateDryRunManifestAsync(plan);
        ConfigurationOwnershipManifestEntry plannedEntry = Assert.Single(plannedManifest.Entries);
        ConfigurationOwnershipManifest existingManifest = plannedManifest with
        {
            PlanId = "previous-existing-reserved-physical-entry-plan",
            ChangeSetId = "previous-existing-reserved-physical-entry-changeset",
            Entries =
            [
                plannedEntry with
                {
                    TargetKind = existingTargetKind,
                    TargetPathOrName = reservedTargetPath,
                    Key = $"{existingTargetKind}.reserved-physical-entry",
                },
            ],
        };
        string existingManifestJson = RawOwnershipManifestJson(existingManifest);
        fileSystem.AtomicWriteAllText(manifestPath, existingManifestJson);
        fileSystem.Calls.Clear();
        var manager = new ConfigurationManager(fileSystem, manifestPath);
        ConfigurationChangePlan planWithManifestHash = plan with
        {
            Manifest = plan.Manifest with
            {
                PreviousOwnedEntryHash = HashMetadata(existingManifestJson),
            },
        };

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(async () =>
            await manager.DryRunAsync(planWithManifestHash, TestContext.Current.CancellationToken)
        );

        Assert.Contains(
            "reserved internal filesystem artifact",
            exception.Message,
            StringComparison.Ordinal
        );
        Assert.Equal(existingManifestJson, fileSystem.ReadAllText(manifestPath));
        Assert.False(fileSystem.FileExists(targetPath));
        AssertNoFilesystemMutationOrLockCalls(fileSystem.Calls);
    }

    [Theory]
    [InlineData("/config/.azureauth-credprovider.fs.lock")]
    [InlineData("/config/.azureauth-credprovider.lifecycle-locks/descendant")]
    public async Task FilesystemBackedPhase4DDryRunRejectsExistingReservedCiTemporaryEntry(
        string reservedTargetPath
    )
    {
        var fileSystem = new InMemoryFileSystem(InMemoryPathSemantics.Posix);
        const string manifestPath = "/state/existing-reserved-ci-entry-manifest.json";
        const string targetPath = "/config/existing-reserved-ci-entry";
        ConfigurationChangePlan plan = CreatePhysicalTargetPlan(
            ConfigurationTargetKind.GitConfig,
            targetPath
        );
        ConfigurationOwnershipManifest plannedManifest = await CreateDryRunManifestAsync(plan);
        ConfigurationOwnershipManifestEntry plannedEntry = Assert.Single(plannedManifest.Entries);
        ConfigurationOwnershipManifest existingManifest = plannedManifest with
        {
            PlanId = "previous-existing-reserved-ci-entry-plan",
            ChangeSetId = "previous-existing-reserved-ci-entry-changeset",
            Entries =
            [
                plannedEntry with
                {
                    TargetKind = ConfigurationTargetKind.CiTemporaryFile,
                    TargetPathOrName = reservedTargetPath,
                    Key = "file",
                },
            ],
        };
        string existingManifestJson = RawOwnershipManifestJson(existingManifest);
        fileSystem.AtomicWriteAllText(manifestPath, existingManifestJson);
        fileSystem.Calls.Clear();
        var manager = new ConfigurationManager(fileSystem, manifestPath);
        ConfigurationChangePlan planWithManifestHash = plan with
        {
            Manifest = plan.Manifest with
            {
                PreviousOwnedEntryHash = HashMetadata(existingManifestJson),
            },
        };

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(async () =>
            await manager.DryRunAsync(planWithManifestHash, TestContext.Current.CancellationToken)
        );

        Assert.Contains(
            "reserved internal filesystem artifact",
            exception.Message,
            StringComparison.Ordinal
        );
        Assert.Equal(existingManifestJson, fileSystem.ReadAllText(manifestPath));
        Assert.False(fileSystem.FileExists(targetPath));
        AssertNoFilesystemMutationOrLockCalls(fileSystem.Calls);
    }

    [Theory]
    [InlineData(
        "/config/existing-entry-manifest-collision",
        "config/existing-entry-manifest-collision"
    )]
    [InlineData(
        "config/existing-entry-manifest-collision-reverse",
        "/config/existing-entry-manifest-collision-reverse"
    )]
    public async Task
        FilesystemBackedPhase4DDryRunRejectsExistingEntryCollidingWithManifestPath(
            string manifestPath,
            string existingTargetPath
        )
    {
        var fileSystem = new InMemoryFileSystem(InMemoryPathSemantics.Posix);
        const string planTargetPath = "/config/existing-entry-manifest-collision-target";
        ConfigurationChangePlan plan = CreatePhysicalTargetPlan(
            ConfigurationTargetKind.GitConfig,
            planTargetPath
        );
        ConfigurationOwnershipManifest plannedManifest = await CreateDryRunManifestAsync(plan);
        ConfigurationOwnershipManifestEntry plannedEntry = Assert.Single(plannedManifest.Entries);
        ConfigurationOwnershipManifest existingManifest = plannedManifest with
        {
            PlanId = "previous-existing-entry-manifest-collision-plan",
            ChangeSetId = "previous-existing-entry-manifest-collision-changeset",
            Entries =
            [
                plannedEntry with
                {
                    TargetKind = ConfigurationTargetKind.Npmrc,
                    TargetPathOrName = existingTargetPath,
                    Key = "registry",
                },
            ],
        };
        string existingManifestJson = RawOwnershipManifestJson(existingManifest);
        fileSystem.AtomicWriteAllText(manifestPath, existingManifestJson);
        fileSystem.Calls.Clear();
        var manager = new ConfigurationManager(fileSystem, manifestPath);
        ConfigurationChangePlan planWithManifestHash = plan with
        {
            Manifest = plan.Manifest with
            {
                PreviousOwnedEntryHash = HashMetadata(existingManifestJson),
            },
        };

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(async () =>
            await manager.DryRunAsync(planWithManifestHash, TestContext.Current.CancellationToken)
        );

        Assert.Contains("ownership manifest path", exception.Message, StringComparison.Ordinal);
        Assert.Equal(existingManifestJson, fileSystem.ReadAllText(manifestPath));
        Assert.False(fileSystem.FileExists(planTargetPath));
        AssertNoFilesystemMutationOrLockCalls(fileSystem.Calls);
    }

    [Fact]
    public async Task FilesystemBackedPhase4DDryRunRemovesProjectionOnlyPhysicalTargetEntries()
    {
        var fileSystem = new InMemoryFileSystem(InMemoryPathSemantics.Posix);
        const string manifestPath = "/state/remove-physical-manifest.json";
        const string remainingTargetPath = "/config/remaining-physical.gitconfig";
        const string removedTargetPath = "/config/removed-physical.gitconfig";
        ConfigurationChangePlan setPlan = CreatePhysicalTargetPlan(
            ConfigurationTargetKind.GitConfig,
            removedTargetPath
        );
        ConfigurationOwnershipManifest plannedManifest = await CreateDryRunManifestAsync(setPlan);
        ConfigurationOwnershipManifestEntry plannedEntry = Assert.Single(plannedManifest.Entries);
        ConfigurationOwnershipManifest existingManifest = plannedManifest with
        {
            PlanId = "previous-remove-physical-plan",
            ChangeSetId = "previous-remove-physical-changeset",
            Entries =
            [
                plannedEntry with
                {
                    TargetPathOrName = remainingTargetPath,
                    Key = "credential.helper",
                },
                plannedEntry with
                {
                    Sequence = 2,
                    TargetPathOrName = removedTargetPath,
                    Key = "credential.helper",
                },
            ],
        };
        string existingManifestJson = RawOwnershipManifestJson(existingManifest);
        const string remainingGitConfig = "[credential]\n\thelper = \"planned-value\"\n";
        fileSystem.AtomicWriteAllText(remainingTargetPath, remainingGitConfig);
        fileSystem.AtomicWriteAllText(
            removedTargetPath,
            "[credential]\n\thelper = \"planned-value\"\n"
        );
        fileSystem.AtomicWriteAllText(manifestPath, existingManifestJson);
        fileSystem.Calls.Clear();
        var manager = new ConfigurationManager(fileSystem, manifestPath);
        ConfigurationChangePlan removePlan = setPlan with
        {
            Manifest = setPlan.Manifest with
            {
                PreviousOwnedEntryHash = HashMetadata(existingManifestJson),
            },
            Changes =
            [
                setPlan.Changes[0] with
                {
                    Operation = ConfigurationChangeOperation.Remove,
                    TargetPathOrName = removedTargetPath,
                    Key = "credential.helper",
                    Value = null,
                    PreviousOwnedEntryMetadata = "previous-physical-target-entry",
                },
            ],
        };

        ConfigurationPlanResult result = await manager.DryRunAsync(
            removePlan,
            TestContext.Current.CancellationToken
        );

        Assert.Equal(existingManifestJson, fileSystem.ReadAllText(manifestPath));
        Assert.Equal(remainingGitConfig, fileSystem.ReadAllText(remainingTargetPath));
        Assert.True(fileSystem.FileExists(removedTargetPath));
        ConfigurationOwnershipManifest simulatedManifest = result.OwnershipManifest!;
        ConfigurationOwnershipManifestEntry remainingEntry = Assert.Single(
            simulatedManifest.Entries
        );
        Assert.Equal(1, remainingEntry.Sequence);
        Assert.Equal(remainingTargetPath, remainingEntry.TargetPathOrName);
        Assert.Equal("credential.helper", remainingEntry.Key);
        Assert.DoesNotContain(
            removedTargetPath,
            RawOwnershipManifestJson(simulatedManifest),
            StringComparison.Ordinal
        );
        AssertNoFilesystemMutationOrLockCalls(fileSystem.Calls);
    }

    [Fact]
    public async Task FilesystemBackedPhase4DDryRunRejectsUnownedProjectionOnlyPhysicalRemove()
    {
        var fileSystem = new InMemoryFileSystem(InMemoryPathSemantics.Posix);
        const string manifestPath = "/state/unowned-remove-physical-manifest.json";
        const string ownedTargetPath = "/config/owned-physical.gitconfig";
        const string unownedTargetPath = "/config/unowned-physical.gitconfig";
        ConfigurationChangePlan setPlan = CreatePhysicalTargetPlan(
            ConfigurationTargetKind.GitConfig,
            ownedTargetPath
        );
        string existingManifestJson = await CreateDryRunManifestJsonAsync(setPlan);
        fileSystem.AtomicWriteAllText(manifestPath, existingManifestJson);
        fileSystem.Calls.Clear();
        var manager = new ConfigurationManager(fileSystem, manifestPath);
        ConfigurationChangePlan removePlan = setPlan with
        {
            Manifest = setPlan.Manifest with
            {
                PreviousOwnedEntryHash = HashMetadata(existingManifestJson),
            },
            Changes =
            [
                setPlan.Changes[0] with
                {
                    Operation = ConfigurationChangeOperation.Remove,
                    TargetPathOrName = unownedTargetPath,
                    Value = null,
                    PreviousOwnedEntryMetadata = "previous-physical-target-entry",
                },
            ],
        };

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(async () =>
            await manager.DryRunAsync(removePlan, TestContext.Current.CancellationToken)
        );

        Assert.Contains("remove target is not owned", exception.Message, StringComparison.Ordinal);
        Assert.Equal(existingManifestJson, fileSystem.ReadAllText(manifestPath));
        Assert.False(fileSystem.FileExists(ownedTargetPath));
        Assert.False(fileSystem.FileExists(unownedTargetPath));
        AssertNoFilesystemMutationOrLockCalls(fileSystem.Calls);
    }

    [Fact]
    public async Task
        DryRunRejectsUnsupportedNuGetPluginLayoutRemoveAdapterNoManifest()
    {
        var fileSystem = new InMemoryFileSystem(InMemoryPathSemantics.Posix);
        const string manifestPath = "/state/missing-remove-adapter-physical-manifest.json";
        string removedTargetPath = CreateNuGetPluginLayoutTargetRoot();
        var manager = new ConfigurationManager(fileSystem, manifestPath);
        ConfigurationChangePlan setPlan = CreatePhysicalTargetPlan(
            ConfigurationTargetKind.NuGetPluginLayout,
            removedTargetPath
        );
        ConfigurationChangePlan removeAdapterPlan = setPlan with
        {
            Changes =
            [
                setPlan.Changes[0] with
                {
                    Operation = ConfigurationChangeOperation.RemoveAdapter,
                    Value = null,
                    PreviousOwnedEntryMetadata = "previous-adapter-entry",
                },
            ],
        };

        var exception = await Assert.ThrowsAsync<ArgumentException>(async () =>
            await manager.DryRunAsync(removeAdapterPlan, TestContext.Current.CancellationToken)
        );

        Assert.Contains(
            "remove-adapter",
            exception.Message,
            StringComparison.Ordinal
        );
        Assert.False(fileSystem.FileExists(manifestPath));
        Assert.False(fileSystem.FileExists(removedTargetPath));
        AssertNoFilesystemMutationOrLockCalls(fileSystem.Calls);
    }

    [Fact]
    public async Task
        DryRunRejectsUnsupportedNuGetPluginLayoutRemoveAdapterExistingManifest()
    {
        var fileSystem = new InMemoryFileSystem(InMemoryPathSemantics.Posix);
        const string manifestPath = "/state/unowned-remove-adapter-physical-manifest.json";
        string ownedTargetPath = CreateNuGetPluginLayoutTargetRoot();
        string unownedTargetPath = CreateNuGetPluginLayoutTargetRoot("bob");
        ConfigurationChangePlan setPlan = CreatePhysicalTargetPlan(
            ConfigurationTargetKind.NuGetPluginLayout,
            ownedTargetPath
        );
        string existingManifestJson = await CreateDryRunManifestJsonAsync(setPlan);
        fileSystem.AtomicWriteAllText(manifestPath, existingManifestJson);
        fileSystem.Calls.Clear();
        var manager = new ConfigurationManager(fileSystem, manifestPath);
        ConfigurationChangePlan removeAdapterPlan = setPlan with
        {
            Manifest = setPlan.Manifest with
            {
                PreviousOwnedEntryHash = HashMetadata(existingManifestJson),
            },
            Changes =
            [
                setPlan.Changes[0] with
                {
                    Operation = ConfigurationChangeOperation.RemoveAdapter,
                    TargetPathOrName = unownedTargetPath,
                    Value = null,
                    PreviousOwnedEntryMetadata = "previous-adapter-entry",
                },
            ],
        };

        var exception = await Assert.ThrowsAsync<ArgumentException>(async () =>
            await manager.DryRunAsync(removeAdapterPlan, TestContext.Current.CancellationToken)
        );

        Assert.Contains(
            "official per-user plugin convention root",
            exception.Message,
            StringComparison.Ordinal
        );
        Assert.Equal(existingManifestJson, fileSystem.ReadAllText(manifestPath));
        Assert.False(fileSystem.FileExists(ownedTargetPath));
        Assert.False(fileSystem.FileExists(unownedTargetPath));
        AssertNoFilesystemMutationOrLockCalls(fileSystem.Calls);
    }

    [Fact]
    public async Task
        DryRunRejectsUnsupportedNuGetPluginLayoutRemoveAdapterSamePathKeyManifest()
    {
        var fileSystem = new InMemoryFileSystem(InMemoryPathSemantics.Posix);
        const string manifestPath = "/state/unowned-key-remove-adapter-physical-manifest.json";
        string targetPath = CreateNuGetPluginLayoutTargetRoot();
        ConfigurationChangePlan setPlan = CreatePhysicalTargetPlan(
            ConfigurationTargetKind.NuGetPluginLayout,
            targetPath
        );
        ConfigurationOwnershipManifest plannedManifest = await CreateDryRunManifestAsync(setPlan);
        ConfigurationOwnershipManifestEntry plannedEntry = Assert.Single(plannedManifest.Entries);
        ConfigurationOwnershipManifest existingManifest = plannedManifest with
        {
            PlanId = "previous-unowned-key-remove-adapter-plan",
            ChangeSetId = "previous-unowned-key-remove-adapter-changeset",
            Entries =
            [
                plannedEntry with
                {
                    Key = "owned-adapter-target",
                },
            ],
        };
        string existingManifestJson = RawOwnershipManifestJson(existingManifest);
        fileSystem.AtomicWriteAllText(manifestPath, existingManifestJson);
        fileSystem.Calls.Clear();
        var manager = new ConfigurationManager(fileSystem, manifestPath);
        ConfigurationChangePlan removeAdapterPlan = setPlan with
        {
            Manifest = setPlan.Manifest with
            {
                PreviousOwnedEntryHash = HashMetadata(existingManifestJson),
            },
            Changes =
            [
                setPlan.Changes[0] with
                {
                    Operation = ConfigurationChangeOperation.RemoveAdapter,
                    Key = "unowned-adapter-target",
                    Value = null,
                    PreviousOwnedEntryMetadata = "previous-adapter-entry",
                },
            ],
        };

        var exception = await Assert.ThrowsAsync<ArgumentException>(async () =>
            await manager.DryRunAsync(removeAdapterPlan, TestContext.Current.CancellationToken)
        );

        Assert.Contains(
            "canonical physical target key",
            exception.Message,
            StringComparison.Ordinal
        );
        Assert.Equal(existingManifestJson, fileSystem.ReadAllText(manifestPath));
        Assert.False(fileSystem.FileExists(targetPath));
        AssertNoFilesystemMutationOrLockCalls(fileSystem.Calls);
    }

    [Fact]
    public async Task
        DryRunRejectsUnsupportedNuGetPluginLayoutRemoveAdapterBeforeRemovalMerge()
    {
        var fileSystem = new InMemoryFileSystem(InMemoryPathSemantics.Posix);
        const string manifestPath = "/state/remove-adapter-physical-manifest.json";
        string remainingTargetPath = CreateNuGetPluginLayoutTargetRoot();
        string removedTargetPath = CreateNuGetPluginLayoutTargetRoot("bob");
        ConfigurationChangePlan setPlan = CreatePhysicalTargetPlan(
            ConfigurationTargetKind.NuGetPluginLayout,
            removedTargetPath
        );
        ConfigurationOwnershipManifest plannedManifest = await CreateDryRunManifestAsync(setPlan);
        ConfigurationOwnershipManifestEntry plannedEntry = Assert.Single(plannedManifest.Entries);
        ConfigurationOwnershipManifest existingManifest = plannedManifest with
        {
            PlanId = "previous-remove-adapter-plan",
            ChangeSetId = "previous-remove-adapter-changeset",
            Entries =
            [
                plannedEntry with
                {
                    TargetPathOrName = remainingTargetPath,
                    Key = "remaining-adapter-target",
                },
                plannedEntry with
                {
                    Sequence = 2,
                    TargetPathOrName = removedTargetPath,
                    Key = "removed-adapter-target",
                },
            ],
        };
        string existingManifestJson = RawOwnershipManifestJson(existingManifest);
        fileSystem.AtomicWriteAllText(manifestPath, existingManifestJson);
        fileSystem.Calls.Clear();
        var manager = new ConfigurationManager(fileSystem, manifestPath);
        ConfigurationChangePlan removeAdapterPlan = setPlan with
        {
            Manifest = setPlan.Manifest with
            {
                PreviousOwnedEntryHash = HashMetadata(existingManifestJson),
            },
            Changes =
            [
                setPlan.Changes[0] with
                {
                    Operation = ConfigurationChangeOperation.RemoveAdapter,
                    TargetPathOrName = removedTargetPath,
                    Key = "removed-adapter-target",
                    Value = null,
                    PreviousOwnedEntryMetadata = "previous-adapter-entry",
                },
            ],
        };

        var exception = await Assert.ThrowsAsync<ArgumentException>(async () =>
            await manager.DryRunAsync(removeAdapterPlan, TestContext.Current.CancellationToken)
        );

        Assert.Contains(
            "official per-user plugin convention root",
            exception.Message,
            StringComparison.Ordinal
        );
        Assert.Equal(existingManifestJson, fileSystem.ReadAllText(manifestPath));
        Assert.False(fileSystem.FileExists(remainingTargetPath));
        Assert.False(fileSystem.FileExists(removedTargetPath));
        AssertNoFilesystemMutationOrLockCalls(fileSystem.Calls);
    }

    [Fact]
    public async Task
        DryRunRejectsUnsupportedNuGetPluginLayoutRemoveAdapterBeforeSamePathKeyMerge()
    {
        var fileSystem = new InMemoryFileSystem(InMemoryPathSemantics.Posix);
        const string manifestPath = "/state/remove-adapter-preserve-key-physical-manifest.json";
        string targetPath = CreateNuGetPluginLayoutTargetRoot();
        ConfigurationChangePlan setPlan = CreatePhysicalTargetPlan(
            ConfigurationTargetKind.NuGetPluginLayout,
            targetPath
        );
        ConfigurationOwnershipManifest plannedManifest = await CreateDryRunManifestAsync(setPlan);
        ConfigurationOwnershipManifestEntry plannedEntry = Assert.Single(plannedManifest.Entries);
        ConfigurationOwnershipManifest existingManifest = plannedManifest with
        {
            PlanId = "previous-remove-adapter-preserve-key-plan",
            ChangeSetId = "previous-remove-adapter-preserve-key-changeset",
            Entries =
            [
                plannedEntry with
                {
                    Key = "preserved-adapter-target",
                },
                plannedEntry with
                {
                    Sequence = 2,
                    Key = "removed-adapter-target",
                },
            ],
        };
        string existingManifestJson = RawOwnershipManifestJson(existingManifest);
        fileSystem.AtomicWriteAllText(manifestPath, existingManifestJson);
        fileSystem.Calls.Clear();
        var manager = new ConfigurationManager(fileSystem, manifestPath);
        ConfigurationChangePlan removeAdapterPlan = setPlan with
        {
            Manifest = setPlan.Manifest with
            {
                PreviousOwnedEntryHash = HashMetadata(existingManifestJson),
            },
            Changes =
            [
                setPlan.Changes[0] with
                {
                    Operation = ConfigurationChangeOperation.RemoveAdapter,
                    Key = "removed-adapter-target",
                    Value = null,
                    PreviousOwnedEntryMetadata = "previous-adapter-entry",
                },
            ],
        };

        var exception = await Assert.ThrowsAsync<ArgumentException>(async () =>
            await manager.DryRunAsync(removeAdapterPlan, TestContext.Current.CancellationToken)
        );

        Assert.Contains(
            "canonical physical target key",
            exception.Message,
            StringComparison.Ordinal
        );
        Assert.Equal(existingManifestJson, fileSystem.ReadAllText(manifestPath));
        Assert.False(fileSystem.FileExists(targetPath));
        AssertNoFilesystemMutationOrLockCalls(fileSystem.Calls);
    }

    [Fact]
    public async Task
        FilesystemBackedPhase4DDryRunRejectsProjectionOnlyInstallAdapterAfterRemoveAdapter()
    {
        var fileSystem = new InMemoryFileSystem(InMemoryPathSemantics.Posix);
        const string manifestPath = "/state/replace-adapter-physical-manifest.json";
        string remainingTargetPath = CreateNuGetPluginLayoutTargetRoot();
        string removedTargetPath = CreateNuGetPluginLayoutTargetRoot("bob");
        string installedTargetPath = CreateNuGetPluginLayoutTargetRoot("carol");
        ConfigurationChangePlan setPlan = CreatePhysicalTargetPlan(
            ConfigurationTargetKind.NuGetPluginLayout,
            removedTargetPath
        );
        ConfigurationOwnershipManifest plannedManifest = await CreateDryRunManifestAsync(setPlan);
        ConfigurationOwnershipManifestEntry plannedEntry = Assert.Single(plannedManifest.Entries);
        ConfigurationOwnershipManifest existingManifest = plannedManifest with
        {
            PlanId = "previous-replace-adapter-plan",
            ChangeSetId = "previous-replace-adapter-changeset",
            Entries =
            [
                plannedEntry with
                {
                    TargetPathOrName = remainingTargetPath,
                    Key = "remaining-replace-adapter-target",
                },
                plannedEntry with
                {
                    Sequence = 2,
                    TargetPathOrName = removedTargetPath,
                    Key = "removed-replace-adapter-target",
                },
            ],
        };
        string existingManifestJson = RawOwnershipManifestJson(existingManifest);
        fileSystem.AtomicWriteAllText(manifestPath, existingManifestJson);
        fileSystem.Calls.Clear();
        var manager = new ConfigurationManager(fileSystem, manifestPath);
        ConfigurationChangePlan replaceAdapterPlan = setPlan with
        {
            Manifest = setPlan.Manifest with
            {
                PreviousOwnedEntryHash = HashMetadata(existingManifestJson),
            },
            Changes =
            [
                setPlan.Changes[0] with
                {
                    Operation = ConfigurationChangeOperation.RemoveAdapter,
                    TargetPathOrName = removedTargetPath,
                    Key = "physical-target",
                    Value = null,
                    PreviousOwnedEntryMetadata = "previous-adapter-entry",
                },
                setPlan.Changes[0] with
                {
                    Operation = ConfigurationChangeOperation.InstallAdapter,
                    TargetPathOrName = installedTargetPath,
                    Key = "physical-target",
                    Value = null,
                },
            ],
        };

        var exception = await Assert.ThrowsAsync<ArgumentException>(async () =>
            await manager.DryRunAsync(replaceAdapterPlan, TestContext.Current.CancellationToken)
        );

        Assert.Contains(
            "official per-user plugin convention root",
            exception.Message,
            StringComparison.Ordinal
        );
        Assert.Equal(existingManifestJson, fileSystem.ReadAllText(manifestPath));
        Assert.False(fileSystem.FileExists(remainingTargetPath));
        Assert.False(fileSystem.FileExists(removedTargetPath));
        Assert.False(fileSystem.FileExists(installedTargetPath));
        AssertNoFilesystemMutationOrLockCalls(fileSystem.Calls);
    }

    [Fact]
    public async Task
        FilesystemBackedPhase4DDryRunRejectsProjectionOnlyInstallAdapterWithValueWritingChange()
    {
        var fileSystem = new InMemoryFileSystem(InMemoryPathSemantics.Posix);
        const string manifestPath = "/state/install-adapter-with-value-physical-manifest.json";
        string setTargetPath = CreateNuGetPluginLayoutTargetRoot();
        string installedTargetPath = CreateNuGetPluginLayoutTargetRoot("bob");
        var manager = new ConfigurationManager(fileSystem, manifestPath);
        ConfigurationChangePlan setPlan = CreatePhysicalTargetPlan(
            ConfigurationTargetKind.NuGetPluginLayout,
            setTargetPath
        );
        ConfigurationChangePlan plan = setPlan with
        {
            Changes =
            [
                setPlan.Changes[0],
                new ConfigurationChange
                {
                    Operation = ConfigurationChangeOperation.InstallAdapter,
                    TargetKind = ConfigurationTargetKind.NuGetPluginLayout,
                    TargetPathOrName = installedTargetPath,
                    Key = "installed-value-writing-adapter-target",
                    Value = null,
                    RequiresOwnershipRecord = true,
                    PreserveDeclarationsAndComments = true,
                },
            ],
        };

        var exception = await Assert.ThrowsAsync<ArgumentException>(async () =>
            await manager.DryRunAsync(plan, TestContext.Current.CancellationToken)
        );

        Assert.Contains(
            "official per-user plugin convention root",
            exception.Message,
            StringComparison.Ordinal
        );
        Assert.False(fileSystem.FileExists(manifestPath));
        Assert.False(fileSystem.FileExists(setTargetPath));
        Assert.False(fileSystem.FileExists(installedTargetPath));
        AssertNoFilesystemMutationOrLockCalls(fileSystem.Calls);
    }

    [Theory]
    [InlineData(ConfigurationChangeOperation.Set)]
    [InlineData(ConfigurationChangeOperation.Create)]
    [InlineData(ConfigurationChangeOperation.Update)]
    [InlineData(ConfigurationChangeOperation.Refresh)]
    public async Task
        FilesystemBackedPhase4DDryRunRejectsRemoveAdapterMixedWithValueWritingProjectionOnlyPlan(
        ConfigurationChangeOperation valueWritingOperation
    )
    {
        var fileSystem = new InMemoryFileSystem(InMemoryPathSemantics.Posix);
        const string manifestPath = "/state/remove-adapter-with-value-physical-manifest.json";
        string removeAdapterTargetPath = CreateNuGetPluginLayoutTargetRoot();
        string valueTargetPath = CreateNuGetPluginLayoutTargetRoot("bob");
        var manager = new ConfigurationManager(fileSystem, manifestPath);
        ConfigurationChangePlan setPlan = CreatePhysicalTargetPlan(
            ConfigurationTargetKind.NuGetPluginLayout,
            valueTargetPath
        );
        ConfigurationChangePlan plan = setPlan with
        {
            Changes =
            [
                new ConfigurationChange
                {
                    Operation = ConfigurationChangeOperation.RemoveAdapter,
                    TargetKind = ConfigurationTargetKind.NuGetPluginLayout,
                    TargetPathOrName = removeAdapterTargetPath,
                    Key = "physical-target",
                    Value = null,
                    RequiresOwnershipRecord = true,
                    PreserveDeclarationsAndComments = true,
                    PreviousOwnedEntryMetadata = "previous-adapter-entry",
                },
                setPlan.Changes[0] with
                {
                    Operation = valueWritingOperation,
                    PreviousOwnedEntryMetadata =
                        valueWritingOperation
                            is ConfigurationChangeOperation.Update
                                or ConfigurationChangeOperation.Refresh
                            ? "previous-value-writing-entry"
                            : null,
                },
            ],
        };
        fileSystem.Calls.Clear();

        var exception = await Assert.ThrowsAsync<ArgumentException>(async () =>
            await manager.DryRunAsync(plan, TestContext.Current.CancellationToken)
        );
        Assert.Contains("remove-adapter", exception.Message, StringComparison.Ordinal);
        Assert.False(fileSystem.FileExists(manifestPath));
        Assert.False(fileSystem.FileExists(removeAdapterTargetPath));
        Assert.False(fileSystem.FileExists(valueTargetPath));
        AssertNoFilesystemMutationOrLockCalls(fileSystem.Calls);
    }

    [Fact]
    public async Task
        FilesystemBackedPhase4DDryRunRejectsProjectionOnlyInstallAdapterWithoutContainer()
    {
        var fileSystem = new InMemoryFileSystem(InMemoryPathSemantics.Posix);
        const string manifestPath = "/state/projection-only-with-ci-entry-manifest.json";
        const string ciTargetPath = "/config/projection-only-ci-entry/owned.txt";
        const string physicalTargetPath = "/config/projection-only-ci-entry.gitconfig";
        ConfigurationChangePlan setProjectionPlan = CreatePhysicalTargetPlan(
            ConfigurationTargetKind.GitConfig,
            physicalTargetPath
        );
        ConfigurationChangePlan projectionPlan = setProjectionPlan with
        {
            Changes =
            [
                setProjectionPlan.Changes[0] with
                {
                    Operation = ConfigurationChangeOperation.InstallAdapter,
                    Value = null,
                },
            ],
        };
        ConfigurationOwnershipManifest projectedManifest = await CreateDryRunManifestAsync(
            setProjectionPlan
        );
        ConfigurationOwnershipManifest ciManifest = await CreateDryRunManifestAsync(
            CreateGenericFilePlan(ConfigurationChangeOperation.Create, ciTargetPath, "owned-value")
        );
        ConfigurationOwnershipManifestEntry ciEntry = Assert.Single(ciManifest.Entries);
        ConfigurationOwnershipManifest existingManifest = projectedManifest with
        {
            PlanId = "previous-projection-only-with-ci-entry-plan",
            ChangeSetId = "previous-projection-only-with-ci-entry-changeset",
            Entries = [ciEntry],
        };
        string existingManifestJson = RawOwnershipManifestJson(existingManifest);
        fileSystem.AtomicWriteAllText(manifestPath, existingManifestJson);
        fileSystem.Calls.Clear();
        var manager = new ConfigurationManager(fileSystem, manifestPath);
        ConfigurationChangePlan projectionPlanWithManifestHash = projectionPlan with
        {
            Manifest = projectionPlan.Manifest with
            {
                PreviousOwnedEntryHash = HashMetadata(existingManifestJson),
            },
        };

        var exception = await Assert.ThrowsAsync<NotSupportedException>(async () =>
            await manager.DryRunAsync(
                projectionPlanWithManifestHash,
                TestContext.Current.CancellationToken
            )
        );

        Assert.Contains(
            "can be executed by apply or remove",
            exception.Message,
            StringComparison.Ordinal
        );
        Assert.Equal(existingManifestJson, fileSystem.ReadAllText(manifestPath));
        Assert.False(fileSystem.FileExists(ciTargetPath));
        Assert.False(fileSystem.FileExists(physicalTargetPath));
        AssertNoFilesystemMutationOrLockCalls(fileSystem.Calls);
    }

    [Fact]
    public async Task
        FilesystemBackedPhase4DDryRunDoesNotTreatEmptyPlanAsProjectionOnlyPhysicalTarget()
    {
        var fileSystem = new InMemoryFileSystem(InMemoryPathSemantics.Posix);
        const string manifestPath = "/manifest.json";
        var manager = new ConfigurationManager(fileSystem, manifestPath);
        ConfigurationChangePlan plan = CreateGenericFilePlan(
            ConfigurationChangeOperation.Create,
            "/config/unused.txt",
            "unused"
        ) with
        {
            Changes = [],
        };

        ConfigurationPlanResult result = await manager.DryRunAsync(
            plan,
            TestContext.Current.CancellationToken
        );

        Assert.Empty(result.PlannedOperations);
        Assert.Null(result.OwnershipManifest);
        Assert.Contains(
            fileSystem.Calls,
            call =>
                string.Equals(
                    call.Operation,
                    nameof(IFileSystem.FileExists),
                    StringComparison.Ordinal
                )
                && string.Equals(call.Path, manifestPath, StringComparison.Ordinal)
        );
    }

    [Theory]
    [MemberData(nameof(ReservedPhase4DPhysicalTargetPaths))]
    public async Task ValidatePlanAndNoFilesystemDryRunRejectReservedPhase4DPhysicalTargetPaths(
        ConfigurationTargetKind targetKind,
        string reservedTargetPath
    )
    {
        var manager = new ConfigurationManager();
        ConfigurationChangePlan plan = CreatePhysicalTargetPlan(targetKind, reservedTargetPath);

        ConfigurationPlanValidationResult validationResult = manager.ValidatePlan(plan);

        Assert.False(validationResult.IsValid);
        Assert.NotNull(validationResult.Violation);
        Assert.Contains(
            "reserved internal filesystem artifact",
            validationResult.Violation,
            StringComparison.Ordinal
        );
        var exception = await Assert.ThrowsAsync<ArgumentException>(async () =>
            await manager.DryRunAsync(plan, TestContext.Current.CancellationToken)
        );
        Assert.Contains(
            "reserved internal filesystem artifact",
            exception.Message,
            StringComparison.Ordinal
        );
    }

    [Theory]
    [InlineData("/config/.azureauth-credprovider.fs.lock")]
    [InlineData("/config/.azureauth-credprovider.fs.lock/descendant")]
    [InlineData("/config/.azureauth-credprovider.lifecycle-locks")]
    [InlineData("/config/.azureauth-credprovider.lifecycle-locks/descendant")]
    public async Task
        ValidateAcceptPlanAndNoFilesystemDryRunRejectReservedCiTemporaryFileTargetPaths(
            string reservedTargetPath
        )
    {
        var manager = new ConfigurationManager();
        ConfigurationChangePlan plan = CreateGenericFilePlan(
            ConfigurationChangeOperation.Create,
            reservedTargetPath,
            "owned-after"
        );

        ConfigurationPlanValidationResult validationResult = manager.ValidatePlan(plan);
        ConfigurationPlanValidationResult acceptResult = await manager.AcceptPlanAsync(
            plan,
            TestContext.Current.CancellationToken
        );

        Assert.False(validationResult.IsValid);
        Assert.NotNull(validationResult.Violation);
        Assert.Contains(
            "reserved internal filesystem artifact",
            validationResult.Violation,
            StringComparison.Ordinal
        );
        Assert.False(acceptResult.IsValid);
        Assert.NotNull(acceptResult.Violation);
        Assert.Contains(
            "reserved internal filesystem artifact",
            acceptResult.Violation,
            StringComparison.Ordinal
        );
        var exception = await Assert.ThrowsAsync<ArgumentException>(async () =>
            await manager.DryRunAsync(plan, TestContext.Current.CancellationToken)
        );
        Assert.Contains(
            "reserved internal filesystem artifact",
            exception.Message,
            StringComparison.Ordinal
        );
    }

    [Fact]
    public async Task
        FilesystemBackedValidateDryRunApplyRejectReservedPhase4DTargetWithoutManifest()
    {
        var fileSystem = new InMemoryFileSystem(InMemoryPathSemantics.Posix);
        const string manifestPath = "/state/reserved-phase4d-apply-without-manifest.json";
        const string reservedTargetPath =
            "/config/.azureauth-credprovider.lifecycle-locks/phase4d-target";
        var dispatcher = new RecordingPhysicalTargetWriterDispatcher(fileSystem);
        var manager = new ConfigurationManager(fileSystem, manifestPath, dispatcher);
        ConfigurationChangePlan plan = CreatePhysicalTargetPlan(
            ConfigurationTargetKind.GitConfig,
            reservedTargetPath
        );

        ConfigurationPlanValidationResult validationResult = manager.ValidatePlan(plan);

        Assert.False(validationResult.IsValid);
        Assert.NotNull(validationResult.Violation);
        Assert.Contains(
            "reserved internal filesystem artifact",
            validationResult.Violation,
            StringComparison.Ordinal
        );
        var dryRunException = await Assert.ThrowsAsync<ArgumentException>(async () =>
            await manager.DryRunAsync(plan, TestContext.Current.CancellationToken)
        );
        Assert.Contains(
            "reserved internal filesystem artifact",
            dryRunException.Message,
            StringComparison.Ordinal
        );
        fileSystem.Calls.Clear();
        var applyException = await Assert.ThrowsAsync<ArgumentException>(async () =>
            await manager.ApplyAsync(plan, TestContext.Current.CancellationToken)
        );
        Assert.Contains(
            "reserved internal filesystem artifact",
            applyException.Message,
            StringComparison.Ordinal
        );
        Assert.Empty(dispatcher.Requests);
        Assert.False(fileSystem.FileExists(manifestPath));
        AssertNoFilesystemMutationOrLockCalls(fileSystem.Calls);
    }

    [Theory]
    [InlineData("config/reserved-cancelled/.azureauth-credprovider.fs.lock/../target")]
    [InlineData("config/reserved-cancelled/.azureauth-credprovider.lifecycle-locks/../target")]
    public async Task
        ValidatePlanAndNoFilesystemDryRunAllowCancelledReservedPhase4DPhysicalSegments(
        string targetPath
    )
    {
        var manager = new ConfigurationManager();
        ConfigurationChangePlan plan = CreatePhysicalTargetPlan(
            ConfigurationTargetKind.GitConfig,
            targetPath
        );

        ConfigurationPlanValidationResult validationResult = manager.ValidatePlan(plan);

        Assert.True(validationResult.IsValid);
        Assert.Null(validationResult.Violation);
        ConfigurationPlanResult dryRun = await manager.DryRunAsync(
            plan,
            TestContext.Current.CancellationToken
        );
        Assert.Equal(ConfigurationPlanState.Planned, dryRun.State);
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

    [Fact]
    public async Task ValidatePlanAndDryRunRejectNpmrcAndYarnrcSamePhysicalPath()
    {
        var manager = new ConfigurationManager();
        const string targetPath = "/config/npm-yarn-same-path/.npmrc";
        ConfigurationChangePlan plan = CreatePhysicalTargetPlan(
            ConfigurationTargetKind.Npmrc,
            targetPath
        ) with
        {
            Changes =
            [
                CreateNpmrcFileChange(targetPath),
                CreateYarnrcFileChange(targetPath),
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

    [Fact]
    public async Task ValidatePlanAndDryRunRejectRelativeNpmrcAndYarnrcSamePhysicalPath()
    {
        var manager = new ConfigurationManager();
        const string npmrcPath = "config/npm-yarn-relative/sub/../.npmrc";
        const string yarnrcPath = "config/npm-yarn-relative/.npmrc";
        ConfigurationChangePlan plan = CreatePhysicalTargetPlan(
            ConfigurationTargetKind.Npmrc,
            npmrcPath
        ) with
        {
            Changes =
            [
                CreateNpmrcFileChange(npmrcPath),
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

    [Fact]
    public async Task
        FilesystemBackedValidatePlanAndDryRunRejectAbsoluteAndRelativeSameLocationPhysicalPath()
    {
        var fileSystem = new InMemoryFileSystem(InMemoryPathSemantics.Posix);
        var manager = new ConfigurationManager(
            fileSystem,
            "/state/absolute-relative-manifest.json"
        );
        const string absoluteTargetPath = "/config/absolute-relative/.npmrc";
        const string relativeTargetPath = "config/absolute-relative/.npmrc";
        ConfigurationChangePlan plan = CreatePhysicalTargetPlan(
            ConfigurationTargetKind.Npmrc,
            absoluteTargetPath
        ) with
        {
            Changes =
            [
                CreateNpmrcFileChange(absoluteTargetPath),
                CreateYarnrcFileChange(relativeTargetPath),
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

    [Fact]
    public async Task
        ValidatePlanAndDryRunAllowLeadingDotDotRelativePhysicalPathsDistinctFromSibling()
    {
        var manager = new ConfigurationManager();
        const string leadingDotDotTargetPath = "../config/leading-dotdot/.npmrc";
        const string siblingTargetPath = "config/leading-dotdot/.npmrc";
        ConfigurationChangePlan plan = CreatePhysicalTargetPlan(
            ConfigurationTargetKind.Npmrc,
            leadingDotDotTargetPath
        ) with
        {
            Changes =
            [
                CreateNpmrcFileChange(leadingDotDotTargetPath),
                CreateYarnrcFileChange(siblingTargetPath),
            ],
        };

        ConfigurationPlanValidationResult result = manager.ValidatePlan(plan);

        Assert.True(result.IsValid, result.Violation);
        Assert.Null(result.Violation);
        ConfigurationPlanResult dryRun = await manager.DryRunAsync(
            plan,
            TestContext.Current.CancellationToken
        );
        Assert.Equal(ConfigurationPlanState.Planned, dryRun.State);
        Assert.Equal(2, dryRun.PlannedOperations.Count);
    }

    [Theory]
    [MemberData(nameof(Phase4DPhysicalTargetKindConflictCases))]
    public async Task ValidatePlanAndDryRunRejectPhase4DPhysicalTargetKindSharingPhysicalTargetPath(
        ConfigurationTargetKind phase4DTargetKind,
        ConfigurationTargetKind otherTargetKind
    )
    {
        var manager = new ConfigurationManager();
        string targetPath = phase4DTargetKind == ConfigurationTargetKind.NuGetPluginLayout
            ? CreateNuGetPluginLayoutTargetRoot()
            : $"config/phase4d/{phase4DTargetKind
                .ToString()
                .ToLower(CultureInfo.InvariantCulture)}";
        ConfigurationChangePlan plan = CreatePhysicalTargetPlan(phase4DTargetKind, targetPath) with
        {
            Changes =
            [
                CreatePhysicalTargetChange(phase4DTargetKind, targetPath),
                CreatePhysicalTargetChange(otherTargetKind, targetPath),
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
    [MemberData(nameof(Phase4DPhysicalTargetKindConflictCases))]
    public async Task
        FilesystemBackedValidateAndDryRunRejectPhase4DPhysicalTargetSharingAbsoluteRelativeLocation(
            ConfigurationTargetKind phase4DTargetKind,
            ConfigurationTargetKind otherTargetKind
        )
    {
        var fileSystem = new InMemoryFileSystem(InMemoryPathSemantics.Posix);
        var manager = new ConfigurationManager(
            fileSystem,
            "/state/phase4d-absolute-relative-manifest.json"
        );
        string targetPath =
            phase4DTargetKind == ConfigurationTargetKind.NuGetPluginLayout
                ? CreateNuGetPluginLayoutTargetRoot()
                : $"config/phase4d-absolute-relative/{phase4DTargetKind
                    .ToString()
                    .ToLower(CultureInfo.InvariantCulture)}";
        ConfigurationChangePlan plan = CreatePhysicalTargetPlan(phase4DTargetKind, targetPath) with
        {
            Changes =
            [
                CreatePhysicalTargetChange(phase4DTargetKind, "/" + targetPath),
                CreatePhysicalTargetChange(otherTargetKind, targetPath),
            ],
        };

        ConfigurationPlanValidationResult result = manager.ValidatePlan(plan);
        string expectedViolation =
            phase4DTargetKind == ConfigurationTargetKind.NuGetPluginLayout
                ? "official per-user plugin convention root"
                : "same physical target path";

        Assert.False(result.IsValid);
        Assert.NotNull(result.Violation);
        Assert.Contains(expectedViolation, result.Violation, StringComparison.Ordinal);
        var exception = await Assert.ThrowsAsync<ArgumentException>(async () =>
            await manager.DryRunAsync(plan, TestContext.Current.CancellationToken)
        );
        Assert.Contains(expectedViolation, exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ValidatePlanAndDryRunRejectNpmrcAndYarnrcDotDotEquivalentPhysicalPath()
    {
        var manager = new ConfigurationManager();
        const string npmrcPath = "/config/npm-yarn-dotdot/sub/../.npmrc";
        const string yarnrcPath = "/config/npm-yarn-dotdot/.npmrc";
        ConfigurationChangePlan plan = CreatePhysicalTargetPlan(
            ConfigurationTargetKind.Npmrc,
            npmrcPath
        ) with
        {
            Changes =
            [
                CreateNpmrcFileChange(npmrcPath),
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
        @"/config/npm-yarn-distinct-paths/.npmrc",
        @"/config/npm-yarn-distinct-paths/.yarnrc.yml"
    )]
    [InlineData(
        @"/config/npm-yarn-case-sensitive/.npmrc",
        @"/config/npm-yarn-case-sensitive/.NPMRC"
    )]
    public async Task ValidatePlanAndDryRunAllowNpmrcAndYarnrcDistinctPhysicalPaths(
        string npmrcPath,
        string yarnrcPath
    )
    {
        var manager = new ConfigurationManager();
        ConfigurationChangePlan plan = CreatePhysicalTargetPlan(
            ConfigurationTargetKind.Npmrc,
            npmrcPath
        ) with
        {
            Changes =
            [
                CreateNpmrcFileChange(npmrcPath),
                CreateYarnrcFileChange(yarnrcPath),
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

    [Fact]
    public async Task ValidatePlanAndDryRunAllowMultipleNpmrcChangesAtSamePhysicalPath()
    {
        var manager = new ConfigurationManager();
        const string targetPath = "/config/npm-same-path/.npmrc";
        ConfigurationChangePlan plan = CreatePhysicalTargetPlan(
            ConfigurationTargetKind.Npmrc,
            targetPath
        ) with
        {
            Changes =
            [
                CreateNpmrcFileChange(targetPath),
                CreateNpmrcFileChange(targetPath) with
                {
                    Key = "always-auth",
                    Value = "true",
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
    [InlineData(@"C:\config\npm-yarn-case\.npmrc", @"C:\CONFIG\NPM-YARN-CASE\.NPMRC")]
    [InlineData(
        "//server/share/config/npm-yarn-case/.npmrc",
        "//SERVER/SHARE/CONFIG/NPM-YARN-CASE/.NPMRC"
    )]
    public async Task
        ValidatePlanAndDryRunRejectWindowsConfigurationPathCaseVariantNpmrcYarnrcConflicts(
            string npmrcPath,
            string yarnrcPath
        )
    {
        var manager = new ConfigurationManager();
        ConfigurationChangePlan plan = CreatePhysicalTargetPlan(
            ConfigurationTargetKind.Npmrc,
            npmrcPath
        ) with
        {
            Changes =
            [
                CreateNpmrcFileChange(npmrcPath),
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

    [Theory]
    [InlineData(nameof(IConfigurationManager.ApplyAsync))]
    [InlineData(nameof(IConfigurationManager.RemoveAsync))]
    public void ExecutionMethodsRejectNullPlansSynchronouslyBeforeReturningValueTask(
        string methodName
    )
    {
        var manager = new ConfigurationManager();

        Assert.Throws<ArgumentNullException>(() =>
        {
            ValueTask<ConfigurationPlanResult> result =
                methodName == nameof(IConfigurationManager.ApplyAsync)
                ? manager.ApplyAsync(null!, TestContext.Current.CancellationToken)
                : manager.RemoveAsync(null!, TestContext.Current.CancellationToken);
            _ = result.AsTask();
        });
    }

    [Theory]
    [InlineData(nameof(IConfigurationManager.ApplyAsync))]
    [InlineData(nameof(IConfigurationManager.RemoveAsync))]
    public void ExecutionMethodsRejectCanceledTokenSynchronouslyBeforeReturningValueTask(
        string methodName
    )
    {
        var manager = new ConfigurationManager();
        ConfigurationChangePlan plan = CreateValidPlan();
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        Assert.Throws<OperationCanceledException>(() =>
        {
            ValueTask<ConfigurationPlanResult> result =
                methodName == nameof(IConfigurationManager.ApplyAsync)
                ? manager.ApplyAsync(plan, cancellation.Token)
                : manager.RemoveAsync(plan, cancellation.Token);
            _ = result.AsTask();
        });
    }

    [Theory]
    [InlineData(nameof(IConfigurationManager.ApplyAsync))]
    [InlineData(nameof(IConfigurationManager.RemoveAsync))]
    public void ExecutionMethodsValidatePlansSynchronouslyBeforeReturningValueTask(
        string methodName
    )
    {
        var manager = new ConfigurationManager();
        ConfigurationChangePlan invalidPlan = CreateValidPlan() with
        {
            Scope = ConfigurationScope.WorkspaceReadOnly,
        };

        var exception = Assert.Throws<ArgumentException>(() =>
        {
            ValueTask<ConfigurationPlanResult> result =
                methodName == nameof(IConfigurationManager.ApplyAsync)
                ? manager.ApplyAsync(invalidPlan, TestContext.Current.CancellationToken)
                : manager.RemoveAsync(invalidPlan, TestContext.Current.CancellationToken);
            _ = result.AsTask();
        });

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
                    TargetKind = ConfigurationTargetKind.Npmrc,
                    TargetPathOrName = "user npmrc",
                    Key =
                        "//pkgs.dev.azure.com/org/_packaging/feed/npm/registry/:_authToken",
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
    [MemberData(nameof(Phase4DPhysicalTargetKinds))]
    public async Task FilesystemBackedApplyRejectsPhase4DPhysicalTargetsWithoutRegisteredWriter(
        ConfigurationTargetKind targetKind
    )
    {
        var fileSystem = new InMemoryFileSystem(InMemoryPathSemantics.Posix);
        const string manifestPath = "/state/no-writer-apply-manifest.json";
        string targetPath =
            "/config/no-writer-apply-"
            + targetKind.ToString().ToLower(CultureInfo.InvariantCulture);
        const string existingTargetContents = "pre-existing physical target contents";
        var manager = new ConfigurationManager(fileSystem, manifestPath);
        ConfigurationChangePlan plan = CreatePhysicalTargetPlan(targetKind, targetPath);
        string existingManifestJson = await CreateDryRunManifestJsonAsync(plan);
        fileSystem.AtomicWriteAllText(targetPath, existingTargetContents);
        fileSystem.AtomicWriteAllText(manifestPath, existingManifestJson);
        fileSystem.Calls.Clear();
        plan = plan with
        {
            Manifest = plan.Manifest with
            {
                PreviousOwnedEntryHash = HashMetadata(existingManifestJson),
            },
        };

        var exception = await Assert.ThrowsAsync<NotSupportedException>(async () =>
            await manager.ApplyAsync(plan, TestContext.Current.CancellationToken)
        );

        Assert.Contains("no registered writer", exception.Message, StringComparison.Ordinal);
        AssertNoFilesystemMutationOrLockCalls(fileSystem.Calls);
        Assert.Equal(existingTargetContents, fileSystem.ReadAllText(targetPath));
        Assert.Equal(existingManifestJson, fileSystem.ReadAllText(manifestPath));
    }

    [Theory]
    [MemberData(nameof(Phase4DPhysicalTargetKinds))]
    public async Task FilesystemBackedRemoveRejectsPhase4DPhysicalTargetsWithoutRegisteredWriter(
        ConfigurationTargetKind targetKind
    )
    {
        var fileSystem = new InMemoryFileSystem(InMemoryPathSemantics.Posix);
        const string manifestPath = "/state/no-writer-remove-manifest.json";
        string targetPath =
            "/config/no-writer-remove-"
            + targetKind.ToString().ToLower(CultureInfo.InvariantCulture);
        const string existingTargetContents = "pre-existing physical target contents";
        var manager = new ConfigurationManager(fileSystem, manifestPath);
        ConfigurationChangePlan setPlan = CreatePhysicalTargetPlan(targetKind, targetPath);
        string existingManifestJson = await CreateDryRunManifestJsonAsync(setPlan);
        fileSystem.AtomicWriteAllText(targetPath, existingTargetContents);
        fileSystem.AtomicWriteAllText(manifestPath, existingManifestJson);
        fileSystem.Calls.Clear();
        ConfigurationChangePlan removePlan = setPlan with
        {
            Manifest = setPlan.Manifest with
            {
                PreviousOwnedEntryHash = HashMetadata(existingManifestJson),
            },
            Changes =
            [
                setPlan.Changes[0] with
                {
                    Operation = ConfigurationChangeOperation.Remove,
                    Value = null,
                    PreviousOwnedEntryMetadata = "previous-physical-target-entry",
                },
            ],
        };

        var exception = await Assert.ThrowsAsync<NotSupportedException>(async () =>
            await manager.RemoveAsync(removePlan, TestContext.Current.CancellationToken)
        );

        Assert.Contains("no registered writer", exception.Message, StringComparison.Ordinal);
        AssertNoFilesystemMutationOrLockCalls(fileSystem.Calls);
        Assert.Equal(existingTargetContents, fileSystem.ReadAllText(targetPath));
        Assert.Equal(existingManifestJson, fileSystem.ReadAllText(manifestPath));
    }

    [Theory]
    [InlineData(nameof(IConfigurationManager.ApplyAsync))]
    [InlineData(nameof(IConfigurationManager.RemoveAsync))]
    public async Task
        FilesystemBackedPhase4DPhysicalExecutionRejectsUnsupportedConditionalMutationBeforeDispatch(
        string methodName
    )
    {
        var fileSystem = new InMemoryFileSystem(InMemoryPathSemantics.Posix);
        const string manifestPath = "/state/physical-unsupported-conditional-manifest.json";
        const string targetPath = "/config/physical-unsupported-conditional.gitconfig";
        const string existingTargetContents = "pre-existing physical target contents";
        var dispatcher = new RecordingPhysicalTargetWriterDispatcher(fileSystem);
        var manager = new ConfigurationManager(fileSystem, manifestPath, dispatcher);
        ConfigurationChangePlan setPlan = CreatePhysicalTargetPlan(
            ConfigurationTargetKind.GitConfig,
            targetPath
        );
        ConfigurationChangePlan plan = setPlan;
        string? existingManifestJson = null;
        if (methodName == nameof(IConfigurationManager.RemoveAsync))
        {
            existingManifestJson = await CreateDryRunManifestJsonAsync(setPlan);
            fileSystem.AtomicWriteAllText(targetPath, existingTargetContents);
            fileSystem.AtomicWriteAllText(manifestPath, existingManifestJson);
            plan = setPlan with
            {
                Manifest = setPlan.Manifest with
                {
                    PreviousOwnedEntryHash = HashMetadata(existingManifestJson),
                },
                Changes =
                [
                    setPlan.Changes[0] with
                    {
                        Operation = ConfigurationChangeOperation.Remove,
                        Value = null,
                        PreviousOwnedEntryMetadata = "previous-physical-target-entry",
                    },
                ],
            };
        }

        fileSystem.SupportsConditionalFileMutations = false;
        fileSystem.Calls.Clear();

        var exception = await Assert.ThrowsAsync<PlatformNotSupportedException>(async () =>
            await CreateExecutionCall(manager, methodName, plan)()
        );

        Assert.Contains(
            "conditional file mutation",
            exception.Message,
            StringComparison.OrdinalIgnoreCase
        );
        Assert.Empty(dispatcher.Requests);
        AssertNoFilesystemMutationOrLockCalls(fileSystem.Calls);
        if (existingManifestJson is null)
        {
            Assert.False(fileSystem.FileExists(targetPath));
            Assert.False(fileSystem.FileExists(manifestPath));
        }
        else
        {
            Assert.Equal(existingTargetContents, fileSystem.ReadAllText(targetPath));
            Assert.Equal(existingManifestJson, fileSystem.ReadAllText(manifestPath));
        }
    }

    [Fact]
    public async Task
        FilesystemBackedApplyRejectsUnsupportedPhase4DManifestEntryBeforeDispatch()
    {
        var fileSystem = new InMemoryFileSystem(InMemoryPathSemantics.Posix);
        const string manifestPath = "/state/phase4d-merge-conflict-manifest.json";
        const string targetPath = "/config/phase4d-merge-conflict.gitconfig";
        const string existingTargetContents = "pre-existing physical target contents";
        var dispatcher = new RecordingPhysicalTargetWriterDispatcher(fileSystem);
        var manager = new ConfigurationManager(fileSystem, manifestPath, dispatcher);
        ConfigurationChangePlan plan = CreatePhysicalTargetPlan(
            ConfigurationTargetKind.GitConfig,
            targetPath
        );
        ConfigurationOwnershipManifest projectedManifest = await CreateDryRunManifestAsync(plan);
        ConfigurationOwnershipManifestEntry projectedEntry =
            Assert.Single(projectedManifest.Entries);
        ConfigurationOwnershipManifest conflictingExistingManifest = projectedManifest with
        {
            Entries =
            [
                projectedEntry with
                {
                    TargetKind = ConfigurationTargetKind.Npmrc,
                    Key = "registry",
                },
            ],
        };
        string existingManifestJson = RawOwnershipManifestJson(conflictingExistingManifest);
        fileSystem.AtomicWriteAllText(targetPath, existingTargetContents);
        fileSystem.AtomicWriteAllText(manifestPath, existingManifestJson);
        fileSystem.Calls.Clear();
        plan = plan with
        {
            Manifest = plan.Manifest with
            {
                PreviousOwnedEntryHash = HashMetadata(existingManifestJson),
            },
        };

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(async () =>
            await manager.ApplyAsync(plan, TestContext.Current.CancellationToken)
        );

        Assert.Contains(
            "registered retained-proof validator",
            exception.Message,
            StringComparison.Ordinal
        );
        Assert.Empty(dispatcher.Requests);
        AssertNoFilesystemMutationOrLockCalls(fileSystem.Calls);
        Assert.Equal(existingTargetContents, fileSystem.ReadAllText(targetPath));
        Assert.Equal(existingManifestJson, fileSystem.ReadAllText(manifestPath));
    }

    [Fact]
    public async Task
        FilesystemBackedApplyRejectsOffTreeNuGetPluginLayoutManifestEntryBeforeDispatch()
    {
        var fileSystem = new InMemoryFileSystem(InMemoryPathSemantics.Posix);
        const string manifestPath = "/state/phase4d-off-tree-nuget-manifest.json";
        string targetPath = CreateNuGetPluginLayoutTargetRoot();
        string offTreeTargetPath = CreateNuGetPluginLayoutTargetRoot("bob");
        var dispatcher = new RecordingPhysicalTargetWriterDispatcher(fileSystem);
        var manager = new ConfigurationManager(fileSystem, manifestPath, dispatcher);
        ConfigurationChangePlan plan = CreatePhysicalTargetPlan(
            ConfigurationTargetKind.NuGetPluginLayout,
            targetPath
        );
        ConfigurationOwnershipManifest projectedManifest = await CreateDryRunManifestAsync(plan);
        ConfigurationOwnershipManifestEntry projectedEntry = Assert.Single(
            projectedManifest.Entries
        );
        ConfigurationOwnershipManifest existingManifest = projectedManifest with
        {
            PlanId = "previous-phase4d-off-tree-nuget-manifest-plan",
            ChangeSetId = "previous-phase4d-off-tree-nuget-manifest-changeset",
            Entries =
            [
                projectedEntry with
                {
                    TargetPathOrName = offTreeTargetPath,
                },
            ],
        };
        string existingManifestJson = RawOwnershipManifestJson(existingManifest);
        fileSystem.AtomicWriteAllText(manifestPath, existingManifestJson);
        fileSystem.Calls.Clear();
        ConfigurationChangePlan planWithManifestHash = plan with
        {
            Manifest = plan.Manifest with
            {
                PreviousOwnedEntryHash = HashMetadata(existingManifestJson),
            },
        };

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(async () =>
            await manager.ApplyAsync(planWithManifestHash, TestContext.Current.CancellationToken)
        );

        Assert.Contains(
            "official per-user plugin convention root",
            exception.Message,
            StringComparison.Ordinal
        );
        Assert.Empty(dispatcher.Requests);
        Assert.Equal(existingManifestJson, fileSystem.ReadAllText(manifestPath));
        AssertNoFilesystemMutationOrLockCalls(fileSystem.Calls);
    }

    [Theory]
    [MemberData(nameof(UnsupportedRetainedNonCiPhysicalTargetKinds))]
    public async Task
        FilesystemBackedPhase4DApplyRejectsUnsupportedRetainedEntryBeforeGitConfigMutation(
        ConfigurationTargetKind unsupportedTargetKind
    )
    {
        var fileSystem = new InMemoryFileSystem(InMemoryPathSemantics.Posix);
        const string manifestPath = "/state/phase4d-unsupported-retained-gitconfig-manifest.json";
        const string targetPath = "/config/phase4d-unsupported-retained.gitconfig";
        var dispatcher = new RecordingPhysicalTargetWriterDispatcher(fileSystem);
        var manager = new ConfigurationManager(fileSystem, manifestPath, dispatcher);
        ConfigurationChangePlan plan = CreatePhysicalTargetPlan(
            ConfigurationTargetKind.GitConfig,
            targetPath
        );
        ConfigurationOwnershipManifest existingManifest =
            CreateUnsupportedRetainedNonCiPhysicalManifest(
                plan,
                unsupportedTargetKind,
                $"/config/retained-{unsupportedTargetKind.ToString().ToLowerInvariant()}"
            );
        string existingManifestJson = RawOwnershipManifestJson(existingManifest);
        fileSystem.AtomicWriteAllText(manifestPath, existingManifestJson);
        fileSystem.Calls.Clear();
        plan = plan with
        {
            Manifest = plan.Manifest with
            {
                PreviousOwnedEntryHash = HashMetadata(existingManifestJson),
            },
        };

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(async () =>
            await manager.ApplyAsync(plan, TestContext.Current.CancellationToken)
        );

        Assert.Contains(
            "registered retained-proof validator",
            exception.Message,
            StringComparison.Ordinal
        );
        Assert.Empty(dispatcher.Requests);
        AssertNoFilesystemMutationOrLockCalls(fileSystem.Calls);
        Assert.False(fileSystem.FileExists(targetPath));
        Assert.Equal(existingManifestJson, fileSystem.ReadAllText(manifestPath));
    }

    [Theory]
    [MemberData(nameof(UnsupportedRetainedNonCiPhysicalTargetKinds))]
    public async Task
        FilesystemBackedPhase4DApplyRejectsUnsupportedRetainedEntryBeforeGenericMutation(
        ConfigurationTargetKind unsupportedTargetKind
    )
    {
        var fileSystem = new InMemoryFileSystem(InMemoryPathSemantics.Posix);
        const string manifestPath = "/state/phase4d-unsupported-retained-generic-manifest.json";
        const string genericTargetPath = "/config/generic-owned.txt";
        const string genericValue = "owned-value";
        fileSystem.AtomicWriteAllText(genericTargetPath, genericValue);
        ConfigurationChangePlan plan = CreateGenericFilePlan(
            ConfigurationChangeOperation.Update,
            genericTargetPath,
            "updated-value",
            previousOwnedEntryMetadata: HashMetadata(genericValue)
        );
        ConfigurationOwnershipManifest existingManifest =
            CreateGenericFileAndUnsupportedRetainedNonCiPhysicalManifest(
                plan,
                genericTargetPath,
                genericValue,
                unsupportedTargetKind,
                $"/config/retained-{unsupportedTargetKind.ToString().ToLowerInvariant()}"
            );
        string existingManifestJson = RawOwnershipManifestJson(existingManifest);
        fileSystem.AtomicWriteAllText(manifestPath, existingManifestJson);
        fileSystem.Calls.Clear();
        plan = plan with
        {
            Manifest = plan.Manifest with
            {
                PreviousOwnedEntryHash = HashMetadata(existingManifestJson),
            },
        };
        var manager = new ConfigurationManager(fileSystem, manifestPath);

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(async () =>
            await manager.ApplyAsync(plan, TestContext.Current.CancellationToken)
        );

        Assert.Contains(
            "registered retained-proof validator",
            exception.Message,
            StringComparison.Ordinal
        );
        Assert.Equal(genericValue, fileSystem.ReadAllText(genericTargetPath));
        Assert.Equal(existingManifestJson, fileSystem.ReadAllText(manifestPath));
        Assert.DoesNotContain(
            fileSystem.Calls,
            call =>
                call.Operation
                    is "WriteAllText"
                        or "AtomicWriteAllText"
                        or "AtomicWriteAllBytes"
                        or "DeleteFile"
                        or "DeleteDirectory"
        );
    }

    [Theory]
    [InlineData(ConfigurationChangeOperation.InstallAdapter)]
    [InlineData(ConfigurationChangeOperation.RemoveAdapter)]
    [InlineData(ConfigurationChangeOperation.Remove)]
    public async Task
        FilesystemBackedApplyRejectsNonValueWritingPhase4DPhysicalTargetsWithoutMutation(
        ConfigurationChangeOperation operation
    )
    {
        var fileSystem = new InMemoryFileSystem(InMemoryPathSemantics.Posix);
        const string manifestPath = "/state/non-value-writing-apply-manifest.json";
        string targetPath = CreateNuGetPluginLayoutTargetRoot();
        var dispatcher = new RecordingPhysicalTargetWriterDispatcher(fileSystem);
        var manager = new ConfigurationManager(fileSystem, manifestPath, dispatcher);
        ConfigurationChangePlan setPlan = CreatePhysicalTargetPlan(
            ConfigurationTargetKind.NuGetPluginLayout,
            targetPath
        );
        ConfigurationChangePlan plan = setPlan with
        {
            Changes =
            [
                setPlan.Changes[0] with
                {
                    Operation = operation,
                    Value = null,
                    PreviousOwnedEntryMetadata =
                        operation
                            is ConfigurationChangeOperation.RemoveAdapter
                                or ConfigurationChangeOperation.Remove
                            ? "previous-physical-target-entry"
                            : null,
                },
            ],
        };

        Exception exception = operation == ConfigurationChangeOperation.RemoveAdapter
            ? await Assert.ThrowsAsync<ArgumentException>(async () =>
                await manager.ApplyAsync(plan, TestContext.Current.CancellationToken)
            )
            : await Assert.ThrowsAsync<NotSupportedException>(async () =>
                await manager.ApplyAsync(plan, TestContext.Current.CancellationToken)
            );

        if (operation == ConfigurationChangeOperation.RemoveAdapter)
        {
            Assert.Contains("remove-adapter", exception.Message, StringComparison.Ordinal);
        }
        else
        {
            Assert.Contains(
                "apply currently supports only value-writing",
                exception.Message,
                StringComparison.Ordinal
            );
        }
        Assert.Empty(dispatcher.Requests);
        Assert.False(fileSystem.FileExists(targetPath));
        Assert.False(fileSystem.FileExists(manifestPath));
        AssertNoFilesystemMutationOrLockCalls(fileSystem.Calls);
    }

    [Fact]
    public async Task
        FilesystemBackedRemoveRejectsNonOwnershipRemovingPhase4DPhysicalTargetsWithoutMutation()
    {
        var fileSystem = new InMemoryFileSystem(InMemoryPathSemantics.Posix);
        const string manifestPath = "/state/non-ownership-removing-remove-manifest.json";
        string targetPath = CreateNuGetPluginLayoutTargetRoot();
        var dispatcher = new RecordingPhysicalTargetWriterDispatcher(fileSystem);
        var manager = new ConfigurationManager(fileSystem, manifestPath, dispatcher);
        ConfigurationChangePlan setPlan = CreatePhysicalTargetPlan(
            ConfigurationTargetKind.NuGetPluginLayout,
            targetPath
        );
        ConfigurationChangePlan plan = setPlan with
        {
            Changes =
            [
                setPlan.Changes[0] with
                {
                    Operation = ConfigurationChangeOperation.InstallAdapter,
                    Value = null,
                },
            ],
        };

        var exception = await Assert.ThrowsAsync<NotSupportedException>(async () =>
            await manager.RemoveAsync(plan, TestContext.Current.CancellationToken)
        );

        Assert.Contains(
            "remove currently supports only ownership-removing",
            exception.Message,
            StringComparison.Ordinal
        );
        Assert.Empty(dispatcher.Requests);
        Assert.False(fileSystem.FileExists(targetPath));
        Assert.False(fileSystem.FileExists(manifestPath));
        AssertNoFilesystemMutationOrLockCalls(fileSystem.Calls);
    }

    [Theory]
    [InlineData(ConfigurationPlanOperation.Apply)]
    [InlineData(ConfigurationPlanOperation.Remove)]
    public async Task
        FilesystemBackedDispatchRejectsMultiplePhase4DChangesBeforeManifestMutation(
            ConfigurationPlanOperation operation
        )
    {
        var fileSystem = new InMemoryFileSystem(InMemoryPathSemantics.Posix);
        const string manifestPath = "/state/multi-physical-target-dispatch-manifest.json";
        const string firstTargetPath = "/config/multi-physical-target-dispatch-first.gitconfig";
        const string secondTargetPath = "/config/multi-physical-target-dispatch-second.gitconfig";
        const string existingManifestJson = """{"sentinel":"manifest must remain unchanged"}""";
        var dispatcher = new RecordingPhysicalTargetWriterDispatcher(fileSystem);
        var manager = new ConfigurationManager(fileSystem, manifestPath, dispatcher);
        ConfigurationChangePlan setPlan = CreatePhysicalTargetPlan(
            ConfigurationTargetKind.GitConfig,
            firstTargetPath
        );
        ConfigurationChangeOperation changeOperation =
            operation == ConfigurationPlanOperation.Apply
                ? ConfigurationChangeOperation.Set
                : ConfigurationChangeOperation.Remove;
        string? value =
            operation == ConfigurationPlanOperation.Apply ? "planned-value" : null;
        string? previousOwnedEntryMetadata =
            operation == ConfigurationPlanOperation.Remove
                ? "previous-physical-target-entry"
                : null;
        ConfigurationChangePlan plan = setPlan with
        {
            Changes =
            [
                setPlan.Changes[0] with
                {
                    Key = "credential.helper",
                    Operation = changeOperation,
                    Value = value,
                    PreviousOwnedEntryMetadata = previousOwnedEntryMetadata,
                },
                setPlan.Changes[0] with
                {
                    Key = "credential.https://dev.azure.com.useHttpPath",
                    TargetPathOrName = secondTargetPath,
                    Operation = changeOperation,
                    Value =
                        operation == ConfigurationPlanOperation.Apply
                            ? "true"
                            : null,
                    PreviousOwnedEntryMetadata = previousOwnedEntryMetadata,
                },
            ],
        };
        fileSystem.AtomicWriteAllText(manifestPath, existingManifestJson);
        fileSystem.Calls.Clear();

        var exception = await Assert.ThrowsAsync<NotSupportedException>(async () =>
        {
            if (operation == ConfigurationPlanOperation.Apply)
            {
                await manager.ApplyAsync(plan, TestContext.Current.CancellationToken);
            }
            else
            {
                await manager.RemoveAsync(plan, TestContext.Current.CancellationToken);
            }
        });

        Assert.Contains(
            "only one 4D physical target change",
            exception.Message,
            StringComparison.Ordinal
        );
        Assert.Empty(dispatcher.Requests);
        Assert.Equal(existingManifestJson, fileSystem.ReadAllText(manifestPath));
        AssertNoFilesystemMutationOrLockCalls(fileSystem.Calls);
    }

    [Theory]
    [InlineData(ConfigurationChangeOperation.Set)]
    [InlineData(ConfigurationChangeOperation.Remove)]
    public async Task
        FilesystemBackedDryRunRejectsMultiplePhase4DPhysicalTargetChangesBeforeMutation(
        ConfigurationChangeOperation operation
    )
    {
        var fileSystem = new InMemoryFileSystem(InMemoryPathSemantics.Posix);
        const string manifestPath = "/state/multi-physical-target-dry-run-manifest.json";
        const string firstTargetPath = "/config/multi-physical-target-dry-run-first.gitconfig";
        const string secondTargetPath = "/config/multi-physical-target-dry-run-second.gitconfig";
        const string existingManifestJson = """{"sentinel":"manifest must remain unchanged"}""";
        var manager = new ConfigurationManager(fileSystem, manifestPath);
        ConfigurationChangePlan setPlan = CreatePhysicalTargetPlan(
            ConfigurationTargetKind.GitConfig,
            firstTargetPath
        );
        ConfigurationChangePlan plan = setPlan with
        {
            Changes =
            [
                setPlan.Changes[0] with
                {
                    Key = "credential.helper",
                    Operation = operation,
                    Value = operation == ConfigurationChangeOperation.Set ? "helper" : null,
                    PreviousOwnedEntryMetadata =
                        operation == ConfigurationChangeOperation.Remove
                            ? "previous-helper-entry"
                            : null,
                },
                setPlan.Changes[0] with
                {
                    Key = "credential.https://dev.azure.com.useHttpPath",
                    TargetPathOrName = secondTargetPath,
                    Operation = operation,
                    Value = operation == ConfigurationChangeOperation.Set ? "true" : null,
                    PreviousOwnedEntryMetadata =
                        operation == ConfigurationChangeOperation.Remove
                            ? "previous-use-http-path-entry"
                            : null,
                },
            ],
        };
        fileSystem.AtomicWriteAllText(manifestPath, existingManifestJson);
        fileSystem.Calls.Clear();

        var exception = await Assert.ThrowsAsync<NotSupportedException>(async () =>
            await manager.DryRunAsync(plan, TestContext.Current.CancellationToken)
        );

        Assert.Contains(
            "only one 4D physical target change",
            exception.Message,
            StringComparison.Ordinal
        );
        Assert.Equal(existingManifestJson, fileSystem.ReadAllText(manifestPath));
        Assert.False(fileSystem.FileExists(firstTargetPath));
        Assert.False(fileSystem.FileExists(secondTargetPath));
        AssertNoFilesystemMutationOrLockCalls(fileSystem.Calls);
    }

    [Theory]
    [MemberData(nameof(Phase4DPhysicalTargetKinds))]
    public async Task FilesystemBackedApplyDispatchesSinglePhase4DPhysicalTarget(
        ConfigurationTargetKind targetKind
    )
    {
        var fileSystem = new InMemoryFileSystem(InMemoryPathSemantics.Posix);
        const string manifestPath = "/state/fake-writer-apply-manifest.json";
        string targetPath =
            "/config/fake-writer-apply-"
            + targetKind.ToString().ToLower(CultureInfo.InvariantCulture);
        var dispatcher = new RecordingPhysicalTargetWriterDispatcher(fileSystem);
        var manager = new ConfigurationManager(fileSystem, manifestPath, dispatcher);
        ConfigurationChangePlan plan = CreatePhysicalTargetPlan(targetKind, targetPath);

        ConfigurationPlanResult result = await manager.ApplyAsync(
            plan,
            TestContext.Current.CancellationToken
        );

        Assert.Equal(ConfigurationPlanState.Applied, result.State);
        Assert.Collection(
            dispatcher.Requests,
            request =>
            {
                Assert.Equal(ConfigurationPlanOperation.Apply, request.PlanOperation);
                Assert.Equal(targetKind, request.TargetKind);
                Assert.Equal(ConfigurationChangeOperation.Set, request.ChangeOperation);
                Assert.Equal(CreatePhysicalTargetKey(targetKind), request.Change.Key);
            }
        );
        Assert.True(fileSystem.FileExists(targetPath));
        Assert.True(fileSystem.FileExists(manifestPath));
    }

    [Theory]
    [MemberData(nameof(Phase4DPhysicalTargetKinds))]
    public async Task FilesystemBackedRemoveDispatchesSinglePhase4DPhysicalTarget(
        ConfigurationTargetKind targetKind
    )
    {
        var fileSystem = new InMemoryFileSystem(InMemoryPathSemantics.Posix);
        const string manifestPath = "/state/fake-writer-remove-manifest.json";
        string targetPath =
            "/config/fake-writer-remove-"
            + targetKind.ToString().ToLower(CultureInfo.InvariantCulture);
        var dispatcher = new RecordingPhysicalTargetWriterDispatcher(fileSystem);
        var manager = new ConfigurationManager(fileSystem, manifestPath, dispatcher);
        ConfigurationChangePlan applyPlan = CreatePhysicalTargetPlan(targetKind, targetPath);
        await manager.ApplyAsync(applyPlan, TestContext.Current.CancellationToken);
        string manifestBeforeRemove = fileSystem.ReadAllText(manifestPath);
        dispatcher.Requests.Clear();
        ConfigurationChangePlan removePlan = applyPlan with
        {
            Manifest = applyPlan.Manifest with
            {
                PreviousOwnedEntryHash = HashMetadata(manifestBeforeRemove),
            },
            Changes =
            [
                applyPlan.Changes[0] with
                {
                    Operation = ConfigurationChangeOperation.Remove,
                    Value = null,
                    PreviousOwnedEntryMetadata = "previous-physical-target-entry",
                },
            ],
        };

        ConfigurationPlanResult result = await manager.RemoveAsync(
            removePlan,
            TestContext.Current.CancellationToken
        );

        Assert.Equal(ConfigurationPlanState.Applied, result.State);
        Assert.Collection(
            dispatcher.Requests,
            request =>
            {
                Assert.Equal(ConfigurationPlanOperation.Remove, request.PlanOperation);
                Assert.Equal(targetKind, request.TargetKind);
                Assert.Equal(ConfigurationChangeOperation.Remove, request.ChangeOperation);
                Assert.Equal(CreatePhysicalTargetKey(targetKind), request.Change.Key);
            }
        );
        Assert.True(fileSystem.FileExists(targetPath));
        Assert.False(fileSystem.FileExists(manifestPath));
    }

    [Fact]
    public async Task
        FilesystemBackedApplyMergesEquivalentPhase4DPhysicalTargetManifestPathInOriginalSlot()
    {
        var fileSystem = new InMemoryFileSystem(InMemoryPathSemantics.Posix);
        const string manifestPath = "/state/phase4d-normalized-merge-manifest.json";
        const string targetPath = "/config/phase4d-normalized-merge.gitconfig";
        const string equivalentTargetPath = "/config/sub/../phase4d-normalized-merge.gitconfig";
        const string otherTargetPath = "/config/phase4d-normalized-merge-other.gitconfig";
        var dispatcher = new RecordingPhysicalTargetWriterDispatcher(fileSystem);
        var manager = new ConfigurationManager(fileSystem, manifestPath, dispatcher);
        ConfigurationChangePlan plan = CreatePhysicalTargetPlan(
            ConfigurationTargetKind.GitConfig,
            targetPath
        );
        ConfigurationOwnershipManifest plannedManifest = await CreateDryRunManifestAsync(plan);
        ConfigurationOwnershipManifestEntry plannedEntry = Assert.Single(plannedManifest.Entries);
        ConfigurationOwnershipManifest existingManifest = plannedManifest with
        {
            PlanId = "previous-phase4d-normalized-merge-plan",
            ChangeSetId = "previous-phase4d-normalized-merge-changeset",
            Entries =
            [
                plannedEntry with
                {
                    TargetPathOrName = equivalentTargetPath,
                },
                plannedEntry with
                {
                    Sequence = 2,
                    TargetPathOrName = otherTargetPath,
                    Key = "credential.helper",
                },
            ],
        };
        string existingManifestJson = RawOwnershipManifestJson(existingManifest);
        fileSystem.AtomicWriteAllText(manifestPath, existingManifestJson);
        fileSystem.AtomicWriteAllText(
            targetPath,
            CreateGitConfigCredentialHelperContents("planned-value")
        );
        fileSystem.AtomicWriteAllText(
            otherTargetPath,
            CreateGitConfigCredentialHelperContents("planned-value")
        );
        ConfigurationChangePlan planWithManifestHash = plan with
        {
            Manifest = plan.Manifest with
            {
                PreviousOwnedEntryHash = HashMetadata(existingManifestJson),
            },
        };

        ConfigurationPlanResult result = await manager.ApplyAsync(
            planWithManifestHash,
            TestContext.Current.CancellationToken
        );

        Assert.Equal(ConfigurationPlanState.Applied, result.State);
        Assert.Single(dispatcher.Requests);
        ConfigurationOwnershipManifest appliedManifest = result.OwnershipManifest!;
        Assert.Collection(
            appliedManifest.Entries,
            entry =>
            {
                Assert.Equal(1, entry.Sequence);
                Assert.Equal(targetPath, entry.TargetPathOrName);
                Assert.Equal("credential.helper", entry.Key);
            },
            entry =>
            {
                Assert.Equal(2, entry.Sequence);
                Assert.Equal(otherTargetPath, entry.TargetPathOrName);
                Assert.Equal("credential.helper", entry.Key);
            }
        );
        Assert.DoesNotContain(
            equivalentTargetPath,
            RawOwnershipManifestJson(appliedManifest),
            StringComparison.Ordinal
        );
    }

    [Fact]
    public async Task
        FilesystemBackedRemoveUsesEquivalentPhase4DPhysicalTargetManifestPathIdentity()
    {
        var fileSystem = new InMemoryFileSystem(InMemoryPathSemantics.Posix);
        const string manifestPath = "/state/phase4d-normalized-remove-manifest.json";
        const string targetPath = "/config/phase4d-normalized-remove.gitconfig";
        const string equivalentTargetPath = "/config/sub/../phase4d-normalized-remove.gitconfig";
        const string remainingTargetPath = "/config/phase4d-normalized-remove-other.gitconfig";
        var dispatcher = new RecordingPhysicalTargetWriterDispatcher(fileSystem);
        var manager = new ConfigurationManager(fileSystem, manifestPath, dispatcher);
        ConfigurationChangePlan setPlan = CreatePhysicalTargetPlan(
            ConfigurationTargetKind.GitConfig,
            equivalentTargetPath
        );
        ConfigurationOwnershipManifest plannedManifest = await CreateDryRunManifestAsync(setPlan);
        ConfigurationOwnershipManifestEntry plannedEntry = Assert.Single(plannedManifest.Entries);
        ConfigurationOwnershipManifest existingManifest = plannedManifest with
        {
            PlanId = "previous-phase4d-normalized-remove-plan",
            ChangeSetId = "previous-phase4d-normalized-remove-changeset",
            Entries =
            [
                plannedEntry with
                {
                    TargetPathOrName = targetPath,
                },
                plannedEntry with
                {
                    Sequence = 2,
                    TargetPathOrName = remainingTargetPath,
                    Key = "credential.helper",
                },
            ],
        };
        string existingManifestJson = RawOwnershipManifestJson(existingManifest);
        fileSystem.AtomicWriteAllText(manifestPath, existingManifestJson);
        fileSystem.AtomicWriteAllText(
            targetPath,
            CreateGitConfigCredentialHelperContents("planned-value")
        );
        fileSystem.AtomicWriteAllText(
            remainingTargetPath,
            CreateGitConfigCredentialHelperContents("planned-value")
        );
        ConfigurationChangePlan removePlan = setPlan with
        {
            Manifest = setPlan.Manifest with
            {
                PreviousOwnedEntryHash = HashMetadata(existingManifestJson),
            },
            Changes =
            [
                setPlan.Changes[0] with
                {
                    Operation = ConfigurationChangeOperation.Remove,
                    Value = null,
                    PreviousOwnedEntryMetadata = "previous-physical-target-entry",
                },
            ],
        };

        ConfigurationPlanResult result = await manager.RemoveAsync(
            removePlan,
            TestContext.Current.CancellationToken
        );

        Assert.Equal(ConfigurationPlanState.Applied, result.State);
        Assert.Single(dispatcher.Requests);
        ConfigurationOwnershipManifest remainingManifest = result.OwnershipManifest!;
        ConfigurationOwnershipManifestEntry entry = Assert.Single(remainingManifest.Entries);
        Assert.Equal(1, entry.Sequence);
        Assert.Equal(remainingTargetPath, entry.TargetPathOrName);
        Assert.Equal("credential.helper", entry.Key);
        string remainingManifestJson = fileSystem.ReadAllText(manifestPath);
        Assert.DoesNotContain(targetPath, remainingManifestJson, StringComparison.Ordinal);
        Assert.DoesNotContain(
            equivalentTargetPath,
            remainingManifestJson,
            StringComparison.Ordinal
        );
    }

    [Fact]
    public async Task FilesystemBackedApplyRestoresPhase4DManifestWhenDispatcherThrowsAfterMerge()
    {
        var fileSystem = new InMemoryFileSystem(InMemoryPathSemantics.Posix);
        const string manifestPath = "/state/phase4d-apply-dispatch-rollback-manifest.json";
        const string targetPath = "/config/phase4d-apply-dispatch-rollback.gitconfig";
        const string equivalentTargetPath =
            "/config/sub/../phase4d-apply-dispatch-rollback.gitconfig";
        const string remainingTargetPath =
            "/config/phase4d-apply-dispatch-rollback-other.gitconfig";
        var dispatcher = new CallbackPhysicalTargetWriterDispatcher((request, cancellationToken) =>
        {
            cancellationToken.ThrowIfCancellationRequested();
            throw new IOException("Injected Phase 4D apply dispatch failure.");
        });
        var manager = new ConfigurationManager(fileSystem, manifestPath, dispatcher);
        ConfigurationChangePlan plan = CreatePhysicalTargetPlan(
            ConfigurationTargetKind.GitConfig,
            targetPath
        );
        ConfigurationOwnershipManifest plannedManifest = await CreateDryRunManifestAsync(plan);
        ConfigurationOwnershipManifestEntry plannedEntry = Assert.Single(plannedManifest.Entries);
        ConfigurationOwnershipManifest existingManifest = plannedManifest with
        {
            PlanId = "previous-phase4d-apply-dispatch-rollback-plan",
            ChangeSetId = "previous-phase4d-apply-dispatch-rollback-changeset",
            Entries =
            [
                plannedEntry with
                {
                    TargetPathOrName = equivalentTargetPath,
                },
                plannedEntry with
                {
                    Sequence = 2,
                    TargetPathOrName = remainingTargetPath,
                    Key = "credential.helper",
                },
            ],
        };
        string existingManifestJson = RawOwnershipManifestJson(existingManifest);
        fileSystem.AtomicWriteAllText(manifestPath, existingManifestJson);
        fileSystem.AtomicWriteAllText(
            targetPath,
            CreateGitConfigCredentialHelperContents("planned-value")
        );
        fileSystem.AtomicWriteAllText(
            remainingTargetPath,
            CreateGitConfigCredentialHelperContents("planned-value")
        );
        ConfigurationChangePlan planWithManifestHash = plan with
        {
            Manifest = plan.Manifest with
            {
                PreviousOwnedEntryHash = HashMetadata(existingManifestJson),
            },
        };

        var exception = await Assert.ThrowsAsync<IOException>(async () =>
            await manager.ApplyAsync(planWithManifestHash, TestContext.Current.CancellationToken)
        );

        Assert.Contains("dispatch failure", exception.Message, StringComparison.Ordinal);
        Assert.Single(dispatcher.Requests);
        Assert.Equal(existingManifestJson, fileSystem.ReadAllText(manifestPath));
        Assert.Equal(
            CreateGitConfigCredentialHelperContents("planned-value"),
            fileSystem.ReadAllText(targetPath)
        );
        Assert.Equal(
            CreateGitConfigCredentialHelperContents("planned-value"),
            fileSystem.ReadAllText(remainingTargetPath)
        );
        var followUpDispatcher = new RecordingPhysicalTargetWriterDispatcher(fileSystem);
        var followUpManager = new ConfigurationManager(
            fileSystem,
            manifestPath,
            followUpDispatcher
        );
        ConfigurationChangePlan followUpPlan = plan with
        {
            Manifest = plan.Manifest with
            {
                PreviousOwnedEntryHash = HashMetadata(existingManifestJson),
            },
            Changes =
            [
                plan.Changes[0] with
                {
                    Value = "follow-up-planned-value",
                    PreviousOwnedEntryMetadata = "previous-physical-target-entry",
                },
            ],
        };
        ConfigurationPlanResult followUpResult = await followUpManager.ApplyAsync(
            followUpPlan,
            TestContext.Current.CancellationToken
        );

        Assert.Equal(ConfigurationPlanState.Applied, followUpResult.State);
        Assert.Single(followUpDispatcher.Requests);
    }

    [Fact]
    public async Task
        FilesystemBackedApplyRollbackLeavesOtherPhase4DManifestAfterDispatcherMutation()
    {
        var fileSystem = new InMemoryFileSystem(InMemoryPathSemantics.Posix);
        const string manifestPath = "/state/phase4d-apply-dispatch-resequence-rollback.json";
        const string targetPath = "/config/phase4d-apply-dispatch-resequence-rollback.gitconfig";
        const string independentTargetPath =
            "/config/phase4d-apply-dispatch-resequence-independent.gitconfig";
        string? mutatedManifestJson = null;
        var dispatcher = new CallbackPhysicalTargetWriterDispatcher((request, cancellationToken) =>
        {
            cancellationToken.ThrowIfCancellationRequested();
            mutatedManifestJson = """{"sentinel":"mutated-manifest"}""";
            fileSystem.AtomicWriteAllText(manifestPath, mutatedManifestJson);
            throw new IOException("Injected Phase 4D apply dispatch failure after resequence.");
        });
        var manager = new ConfigurationManager(fileSystem, manifestPath, dispatcher);
        ConfigurationChangePlan independentPlan = CreatePhysicalTargetPlan(
            ConfigurationTargetKind.GitConfig,
            independentTargetPath
        ) with
        {
            Changes =
            [
                CreatePhysicalTargetChange(ConfigurationTargetKind.GitConfig, independentTargetPath)
                    with
                    {
                        Key = "credential.helper",
                        Value = "independent-planned-value",
                    },
            ],
        };
        string existingManifestJson = await CreateDryRunManifestJsonAsync(independentPlan);
        fileSystem.AtomicWriteAllText(manifestPath, existingManifestJson);
        ConfigurationChangePlan plan = CreatePhysicalTargetPlan(
            ConfigurationTargetKind.GitConfig,
            targetPath
        ) with
        {
            Manifest = independentPlan.Manifest with
            {
                PreviousOwnedEntryHash = HashMetadata(existingManifestJson),
            },
        };

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(async () =>
            await manager.ApplyAsync(plan, TestContext.Current.CancellationToken)
        );

        Assert.Contains(
            "owned Git config key is missing or duplicated",
            exception.Message,
            StringComparison.Ordinal
        );
        Assert.Empty(dispatcher.Requests);
        Assert.Null(mutatedManifestJson);
        Assert.Equal(existingManifestJson, fileSystem.ReadAllText(manifestPath));
        Assert.False(fileSystem.FileExists(targetPath));
        Assert.False(fileSystem.FileExists(independentTargetPath));
    }

    [Fact]
    public async Task
        FilesystemBackedApplyPhase4DRollbackSurfacesConflictWhenDispatcherConflictFailsRollback()
    {
        var fileSystem = new InMemoryFileSystem(InMemoryPathSemantics.Posix);
        const string manifestPath = "/state/phase4d-apply-dispatch-conflict-rollback-conflict.json";
        const string targetPath = "/config/phase4d-apply-dispatch-conflict-rollback.gitconfig";
        const string replacementTargetPath =
            "/config/phase4d-apply-dispatch-conflict-replacement.gitconfig";
        ConfigurationChangePlan replacementPlan = CreatePhysicalTargetPlan(
            ConfigurationTargetKind.GitConfig,
            replacementTargetPath
        ) with
        {
            Changes =
            [
                CreatePhysicalTargetChange(
                    ConfigurationTargetKind.GitConfig,
                    replacementTargetPath
                ) with
                {
                    Key = "credential.helper",
                    Value = "replacement-planned-value",
                },
            ],
        };
        string replacementManifestJson = await CreateDryRunManifestJsonAsync(replacementPlan);
        const string dispatchFailureMessage =
            "Injected Phase 4D apply dispatch conflict after manifest replacement.";
        var dispatchFailure = new InvalidOperationException(dispatchFailureMessage);
        var dispatcher = new CallbackPhysicalTargetWriterDispatcher((request, cancellationToken) =>
        {
            cancellationToken.ThrowIfCancellationRequested();
            fileSystem.AtomicWriteAllText(manifestPath, replacementManifestJson);
            throw dispatchFailure;
        });
        var manager = new ConfigurationManager(fileSystem, manifestPath, dispatcher);
        ConfigurationChangePlan plan = CreatePhysicalTargetPlan(
            ConfigurationTargetKind.GitConfig,
            targetPath
        );

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(async () =>
            await manager.ApplyAsync(plan, TestContext.Current.CancellationToken)
        );

        Assert.Contains(dispatchFailureMessage, exception.Message, StringComparison.Ordinal);
        Assert.Empty(exception.Data);
        Assert.Single(dispatcher.Requests);
        Assert.True(fileSystem.FileExists(manifestPath));
        Assert.Equal(replacementManifestJson, fileSystem.ReadAllText(manifestPath));
        Assert.False(fileSystem.FileExists(targetPath));
        Assert.False(fileSystem.FileExists(replacementTargetPath));
    }

    [Fact]
    public async Task FilesystemBackedApplyPhase4DRollbackConflictNoSecretDispatchFailureDetails()
    {
        var fileSystem = new InMemoryFileSystem(InMemoryPathSemantics.Posix);
        const string manifestPath = "/state/phase4d-apply-secret-dispatch-rollback-conflict.json";
        const string targetPath = "/config/phase4d-apply-secret-dispatch-rollback.gitconfig";
        const string replacementTargetPath =
            "/config/phase4d-apply-secret-dispatch-replacement.gitconfig";
        const string secret = "phase4d-dispatch-secret-value";
        ConfigurationChangePlan replacementPlan = CreatePhysicalTargetPlan(
            ConfigurationTargetKind.GitConfig,
            replacementTargetPath
        ) with
        {
            Changes =
            [
                CreatePhysicalTargetChange(
                    ConfigurationTargetKind.GitConfig,
                    replacementTargetPath
                ) with
                {
                    Key = "credential.helper",
                    Value = "replacement-planned-value",
                },
            ],
        };
        string replacementManifestJson = await CreateDryRunManifestJsonAsync(replacementPlan);
        var dispatchFailure = new InvalidOperationException(
            $"Injected Phase 4D dispatch failure containing {secret}."
        );
        var dispatcher = new CallbackPhysicalTargetWriterDispatcher((request, cancellationToken) =>
        {
            cancellationToken.ThrowIfCancellationRequested();
            fileSystem.AtomicWriteAllText(manifestPath, replacementManifestJson);
            throw dispatchFailure;
        });
        var manager = new ConfigurationManager(fileSystem, manifestPath, dispatcher);
        ConfigurationChangePlan plan = CreatePhysicalTargetPlan(
            ConfigurationTargetKind.GitConfig,
            targetPath
        ) with
        {
            ContainsCredentialMaterial = true,
            Changes =
            [
                CreatePhysicalTargetChange(ConfigurationTargetKind.GitConfig, targetPath) with
                {
                    Key = "credential.helper",
                    Value = secret,
                },
            ],
        };

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(async () =>
            await manager.ApplyAsync(plan, TestContext.Current.CancellationToken)
        );

        Assert.Contains("dispatch failure", exception.Message, StringComparison.Ordinal);
        AssertExceptionAndDataDoNotContainSecret(exception, secret);
        Assert.False(exception.Data.Contains("ConfigurationDispatchFailure"));
        Assert.False(exception.Data.Contains("ConfigurationDispatchException"));
        Assert.Equal(
            typeof(InvalidOperationException).FullName,
            Assert.IsType<string>(exception.Data["ConfigurationDispatchExceptionType"])
        );
        Assert.Equal(
            dispatchFailure.HResult,
            Assert.IsType<int>(exception.Data["ConfigurationDispatchExceptionHResult"])
        );
        Assert.Single(dispatcher.Requests);
        Assert.True(fileSystem.FileExists(manifestPath));
        Assert.Equal(replacementManifestJson, fileSystem.ReadAllText(manifestPath));
        Assert.False(fileSystem.FileExists(targetPath));
        Assert.False(fileSystem.FileExists(replacementTargetPath));
    }

    [Fact]
    public async Task
        FilesystemBackedApplyRollbackLeavesNormalizedEquivalentPhase4DManifestUntouchedOnConflict()
    {
        var fileSystem = new InMemoryFileSystem(InMemoryPathSemantics.Posix);
        const string manifestPath = "/state/phase4d-apply-normalized-current-rollback.json";
        const string targetPath = "/config/phase4d-apply-normalized-current-rollback.gitconfig";
        string? equivalentManifestJson = null;
        var dispatcher = new CallbackPhysicalTargetWriterDispatcher((request, cancellationToken) =>
        {
            cancellationToken.ThrowIfCancellationRequested();
            equivalentManifestJson = """{"sentinel":"equivalent-mutated-manifest"}""";
            fileSystem.AtomicWriteAllText(manifestPath, equivalentManifestJson);
            throw new IOException(
                "Injected Phase 4D apply dispatch failure after normalized preclaim."
            );
        });
        var manager = new ConfigurationManager(fileSystem, manifestPath, dispatcher);
        ConfigurationChangePlan plan = CreatePhysicalTargetPlan(
            ConfigurationTargetKind.GitConfig,
            targetPath
        );

        var exception = await Assert.ThrowsAsync<IOException>(async () =>
            await manager.ApplyAsync(plan, TestContext.Current.CancellationToken)
        );

        Assert.Contains("dispatch failure", exception.Message, StringComparison.Ordinal);
        Assert.Single(dispatcher.Requests);
        Assert.NotNull(equivalentManifestJson);
        Assert.Equal(equivalentManifestJson, fileSystem.ReadAllText(manifestPath));
        Assert.False(fileSystem.FileExists(targetPath));
    }

    [Fact]
    public async Task
        FilesystemBackedApplyDeletesPhase4DManifestAfterMissingOriginalDispatchFailure()
    {
        var fileSystem = new InMemoryFileSystem(InMemoryPathSemantics.Posix);
        const string manifestPath = "/state/phase4d-apply-missing-original-rollback.json";
        const string targetPath = "/config/phase4d-apply-missing-original-rollback.gitconfig";
        var dispatcher = new CallbackPhysicalTargetWriterDispatcher((request, cancellationToken) =>
        {
            cancellationToken.ThrowIfCancellationRequested();
            throw new IOException("Injected Phase 4D apply dispatch failure.");
        });
        var manager = new ConfigurationManager(fileSystem, manifestPath, dispatcher);
        ConfigurationChangePlan plan = CreatePhysicalTargetPlan(
            ConfigurationTargetKind.GitConfig,
            targetPath
        );

        var exception = await Assert.ThrowsAsync<IOException>(async () =>
            await manager.ApplyAsync(plan, TestContext.Current.CancellationToken)
        );

        Assert.Contains("dispatch failure", exception.Message, StringComparison.Ordinal);
        Assert.Single(dispatcher.Requests);
        Assert.False(fileSystem.FileExists(manifestPath));
        Assert.False(fileSystem.FileExists(targetPath));
    }

    [Fact]
    public async Task
        FilesystemBackedApplyPhase4DRollbackReportsConflictWhenMissingOriginalPreclaimDeleted()
    {
        var fileSystem = new InMemoryFileSystem(InMemoryPathSemantics.Posix);
        const string manifestPath = "/state/phase4d-apply-deleted-preclaim-rollback.json";
        const string targetPath = "/config/phase4d-apply-deleted-preclaim-rollback.gitconfig";
        var dispatcher = new CallbackPhysicalTargetWriterDispatcher((request, cancellationToken) =>
        {
            cancellationToken.ThrowIfCancellationRequested();
            fileSystem.DeleteFile(manifestPath);
            throw new IOException(
                "Injected Phase 4D apply dispatch failure after preclaim deletion."
            );
        });
        var manager = new ConfigurationManager(fileSystem, manifestPath, dispatcher);
        ConfigurationChangePlan plan = CreatePhysicalTargetPlan(
            ConfigurationTargetKind.GitConfig,
            targetPath
        );

        var exception = await Assert.ThrowsAsync<IOException>(async () =>
            await manager.ApplyAsync(plan, TestContext.Current.CancellationToken)
        );

        Assert.Contains("dispatch failure", exception.Message, StringComparison.Ordinal);
        Assert.Single(dispatcher.Requests);
        Assert.False(fileSystem.FileExists(manifestPath));
        Assert.False(fileSystem.FileExists(targetPath));
    }

    [Fact]
    public async Task
        FilesystemBackedApplyRollbackLeavesMissingPhase4DManifestWhenPreclaimDeleted()
    {
        var fileSystem = new InMemoryFileSystem(InMemoryPathSemantics.Posix);
        const string manifestPath = "/state/phase4d-apply-missing-current-rollback.json";
        const string targetPath = "/config/phase4d-apply-missing-current-rollback.gitconfig";
        const string independentTargetPath =
            "/config/phase4d-apply-missing-current-independent.gitconfig";
        var dispatcher = new CallbackPhysicalTargetWriterDispatcher((request, cancellationToken) =>
        {
            cancellationToken.ThrowIfCancellationRequested();
            fileSystem.DeleteFile(manifestPath);
            throw new IOException(
                "Injected Phase 4D apply dispatch failure after manifest deletion."
            );
        });
        var manager = new ConfigurationManager(fileSystem, manifestPath, dispatcher);
        ConfigurationChangePlan independentPlan = CreatePhysicalTargetPlan(
            ConfigurationTargetKind.GitConfig,
            independentTargetPath
        ) with
        {
            Changes =
            [
                CreatePhysicalTargetChange(ConfigurationTargetKind.GitConfig, independentTargetPath)
                    with
                    {
                        Key = "credential.helper",
                        Value = "independent-planned-value",
                    },
            ],
        };
        string existingManifestJson = await CreateDryRunManifestJsonAsync(independentPlan);
        fileSystem.AtomicWriteAllText(manifestPath, existingManifestJson);
        ConfigurationChangePlan plan = CreatePhysicalTargetPlan(
            ConfigurationTargetKind.GitConfig,
            targetPath
        ) with
        {
            Manifest = independentPlan.Manifest with
            {
                PreviousOwnedEntryHash = HashMetadata(existingManifestJson),
            },
        };

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(async () =>
            await manager.ApplyAsync(plan, TestContext.Current.CancellationToken)
        );

        Assert.Contains(
            "owned Git config key is missing or duplicated",
            exception.Message,
            StringComparison.Ordinal
        );
        Assert.Empty(dispatcher.Requests);
        Assert.True(fileSystem.FileExists(manifestPath));
        Assert.Equal(existingManifestJson, fileSystem.ReadAllText(manifestPath));
        Assert.False(fileSystem.FileExists(targetPath));
        Assert.False(fileSystem.FileExists(independentTargetPath));
    }

    [Fact]
    public async Task
        FilesystemBackedRemoveRestoresPhase4DManifestWhenDispatcherThrowsAfterPreremoval()
    {
        var fileSystem = new InMemoryFileSystem(InMemoryPathSemantics.Posix);
        const string manifestPath = "/state/phase4d-remove-dispatch-rollback-manifest.json";
        const string targetPath = "/config/phase4d-remove-dispatch-rollback.gitconfig";
        const string remainingTargetPath =
            "/config/phase4d-remove-dispatch-rollback-other.gitconfig";
        var dispatcher = new CallbackPhysicalTargetWriterDispatcher((request, cancellationToken) =>
        {
            cancellationToken.ThrowIfCancellationRequested();
            throw new IOException("Injected Phase 4D remove dispatch failure.");
        });
        var manager = new ConfigurationManager(fileSystem, manifestPath, dispatcher);
        ConfigurationChangePlan setPlan = CreatePhysicalTargetPlan(
            ConfigurationTargetKind.GitConfig,
            targetPath
        );
        ConfigurationOwnershipManifest plannedManifest = await CreateDryRunManifestAsync(setPlan);
        ConfigurationOwnershipManifestEntry plannedEntry = Assert.Single(plannedManifest.Entries);
        ConfigurationOwnershipManifest existingManifest = plannedManifest with
        {
            PlanId = "previous-phase4d-remove-dispatch-rollback-plan",
            ChangeSetId = "previous-phase4d-remove-dispatch-rollback-changeset",
            Entries =
            [
                plannedEntry with
                {
                    TargetPathOrName = targetPath,
                },
                plannedEntry with
                {
                    Sequence = 2,
                    TargetPathOrName = remainingTargetPath,
                    Key = "credential.helper",
                },
            ],
        };
        string existingManifestJson = RawOwnershipManifestJson(existingManifest);
        fileSystem.AtomicWriteAllText(manifestPath, existingManifestJson);
        fileSystem.AtomicWriteAllText(
            targetPath,
            CreateGitConfigCredentialHelperContents("planned-value")
        );
        fileSystem.AtomicWriteAllText(
            remainingTargetPath,
            CreateGitConfigCredentialHelperContents("planned-value")
        );
        ConfigurationChangePlan removePlan = setPlan with
        {
            Manifest = setPlan.Manifest with
            {
                PreviousOwnedEntryHash = HashMetadata(existingManifestJson),
            },
            Changes =
            [
                setPlan.Changes[0] with
                {
                    Operation = ConfigurationChangeOperation.Remove,
                    Value = null,
                    PreviousOwnedEntryMetadata = "previous-physical-target-entry",
                },
            ],
        };

        var exception = await Assert.ThrowsAsync<IOException>(async () =>
            await manager.RemoveAsync(removePlan, TestContext.Current.CancellationToken)
        );

        Assert.Contains("dispatch failure", exception.Message, StringComparison.Ordinal);
        Assert.Single(dispatcher.Requests);
        Assert.Equal(existingManifestJson, fileSystem.ReadAllText(manifestPath));
        Assert.Equal(
            CreateGitConfigCredentialHelperContents("planned-value"),
            fileSystem.ReadAllText(targetPath)
        );
        Assert.Equal(
            CreateGitConfigCredentialHelperContents("planned-value"),
            fileSystem.ReadAllText(remainingTargetPath)
        );
    }

    [Fact]
    public async Task
        FilesystemBackedApplyRollbackLeavesForeignPhase4DManifestWhenReplaced()
    {
        var fileSystem = new InMemoryFileSystem(InMemoryPathSemantics.Posix);
        const string manifestPath = "/state/phase4d-apply-foreign-dispatch-rollback.json";
        const string targetPath = "/config/phase4d-apply-foreign-dispatch-rollback.gitconfig";
        ConfigurationChangePlan plan = CreatePhysicalTargetPlan(
            ConfigurationTargetKind.GitConfig,
            targetPath
        );
        ConfigurationOwnershipManifest preparedManifest = await CreateDryRunManifestAsync(plan);
        ConfigurationOwnershipManifestEntry preparedEntry = Assert.Single(preparedManifest.Entries);
        ConfigurationOwnershipManifest foreignManifest = preparedManifest with
        {
            ManifestId = "foreign-phase4d-dispatch-rollback-manifest",
            PlanId = "foreign-phase4d-dispatch-rollback-plan",
            ChangeSetId = "foreign-phase4d-dispatch-rollback-changeset",
            OwnerProductId = "foreign-product",
            EntrySelector = "foreign.phase4d.selector",
            Entries =
            [
                preparedEntry with
                {
                    TargetPathOrName = targetPath,
                    Key = "credential.helper",
                },
            ],
        };
        string foreignManifestJson = RawOwnershipManifestJson(foreignManifest);
        var dispatcher = new CallbackPhysicalTargetWriterDispatcher((request, cancellationToken) =>
        {
            cancellationToken.ThrowIfCancellationRequested();
            fileSystem.AtomicWriteAllText(manifestPath, foreignManifestJson);
            throw new IOException(
                "Injected Phase 4D apply dispatch failure after foreign manifest replacement."
            );
        });
        var manager = new ConfigurationManager(fileSystem, manifestPath, dispatcher);

        var exception = await Assert.ThrowsAsync<IOException>(async () =>
            await manager.ApplyAsync(plan, TestContext.Current.CancellationToken)
        );

        Assert.Contains("dispatch failure", exception.Message, StringComparison.Ordinal);
        Assert.Single(dispatcher.Requests);
        Assert.Equal(foreignManifestJson, fileSystem.ReadAllText(manifestPath));
        Assert.False(fileSystem.FileExists(targetPath));
    }

    [Fact]
    public async Task
        FilesystemBackedApplyRollbackLeavesPhase4DManifestWithUnrelatedEntryAddedAfterPreclaim()
    {
        var fileSystem = new InMemoryFileSystem(InMemoryPathSemantics.Posix);
        const string manifestPath = "/state/phase4d-apply-unrelated-post-preclaim.json";
        const string targetPath = "/config/phase4d-apply-unrelated-post-preclaim.gitconfig";
        const string unrelatedTargetPath =
            "/config/phase4d-apply-unrelated-post-preclaim-unrelated.gitconfig";
        ConfigurationChangePlan unrelatedPlan = CreatePhysicalTargetPlan(
            ConfigurationTargetKind.GitConfig,
            unrelatedTargetPath
        ) with
        {
            Changes =
            [
                CreatePhysicalTargetChange(ConfigurationTargetKind.GitConfig, unrelatedTargetPath)
                    with
                    {
                        Key = "credential.helper",
                        Value = "unrelated-planned-value",
                    },
            ],
        };
        ConfigurationOwnershipManifest unrelatedManifest = await CreateDryRunManifestAsync(
            unrelatedPlan
        );
        ConfigurationOwnershipManifestEntry unrelatedEntry = Assert.Single(
            unrelatedManifest.Entries
        );
        string? postPreclaimManifestJson = null;
        var dispatcher = new CallbackPhysicalTargetWriterDispatcher((request, cancellationToken) =>
        {
            cancellationToken.ThrowIfCancellationRequested();
            postPreclaimManifestJson = RawOwnershipManifestJson(unrelatedManifest);
            fileSystem.AtomicWriteAllText(manifestPath, postPreclaimManifestJson);
            throw new IOException("Injected Phase 4D apply dispatch failure after preclaim.");
        });
        var manager = new ConfigurationManager(fileSystem, manifestPath, dispatcher);
        ConfigurationChangePlan plan = CreatePhysicalTargetPlan(
            ConfigurationTargetKind.GitConfig,
            targetPath
        );

        var exception = await Assert.ThrowsAsync<IOException>(async () =>
            await manager.ApplyAsync(plan, TestContext.Current.CancellationToken)
        );

        Assert.Contains("dispatch failure", exception.Message, StringComparison.Ordinal);
        Assert.Single(dispatcher.Requests);
        Assert.NotNull(postPreclaimManifestJson);
        Assert.Equal(postPreclaimManifestJson, fileSystem.ReadAllText(manifestPath));
        Assert.False(fileSystem.FileExists(targetPath));
        Assert.False(fileSystem.FileExists(unrelatedTargetPath));
    }

    [Fact]
    public async Task
        FilesystemBackedApplyRollbackLeavesMutatedSameKeyPhase4DPreclaimManifestUntouched()
    {
        var fileSystem = new InMemoryFileSystem(InMemoryPathSemantics.Posix);
        const string manifestPath = "/state/phase4d-apply-mutated-same-key-preclaim.json";
        const string targetPath = "/config/phase4d-apply-mutated-same-key-preclaim.gitconfig";
        const string unrelatedTargetPath =
            "/config/phase4d-apply-mutated-same-key-preclaim-unrelated.gitconfig";
        ConfigurationChangePlan unrelatedPlan = CreatePhysicalTargetPlan(
            ConfigurationTargetKind.GitConfig,
            unrelatedTargetPath
        ) with
        {
            Changes =
            [
                CreatePhysicalTargetChange(ConfigurationTargetKind.GitConfig, unrelatedTargetPath)
                    with
                    {
                        Key = "credential.helper",
                        Value = "unrelated-planned-value",
                    },
            ],
        };
        string existingManifestJson = await CreateDryRunManifestJsonAsync(unrelatedPlan);
        fileSystem.AtomicWriteAllText(manifestPath, existingManifestJson);
        string? mutatedManifestJson = null;
        var dispatcher = new CallbackPhysicalTargetWriterDispatcher((request, cancellationToken) =>
        {
            cancellationToken.ThrowIfCancellationRequested();
            mutatedManifestJson = """{"sentinel":"same-key-mutated-manifest"}""";
            fileSystem.AtomicWriteAllText(manifestPath, mutatedManifestJson);
            throw new IOException(
                "Injected Phase 4D apply dispatch failure after same-key preclaim mutation."
            );
        });
        var manager = new ConfigurationManager(fileSystem, manifestPath, dispatcher);
        ConfigurationChangePlan plan = CreatePhysicalTargetPlan(
            ConfigurationTargetKind.GitConfig,
            targetPath
        ) with
        {
            Manifest = unrelatedPlan.Manifest with
            {
                PreviousOwnedEntryHash = HashMetadata(existingManifestJson),
            },
        };

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(async () =>
            await manager.ApplyAsync(plan, TestContext.Current.CancellationToken)
        );

        Assert.Contains(
            "owned Git config key is missing or duplicated",
            exception.Message,
            StringComparison.Ordinal
        );
        Assert.Empty(dispatcher.Requests);
        Assert.Null(mutatedManifestJson);
        Assert.Equal(existingManifestJson, fileSystem.ReadAllText(manifestPath));
        Assert.False(fileSystem.FileExists(targetPath));
        Assert.False(fileSystem.FileExists(unrelatedTargetPath));
    }

    [Fact]
    public async Task
        FilesystemBackedApplyRollbackLeavesSameKeyPhase4DManifestUntouchedAfterDispatcherMutation()
    {
        var fileSystem = new InMemoryFileSystem(InMemoryPathSemantics.Posix);
        const string manifestPath = "/state/phase4d-apply-mutated-same-key-restore.json";
        const string targetPath = "/config/phase4d-apply-mutated-same-key-restore.gitconfig";
        const string unrelatedTargetPath =
            "/config/phase4d-apply-mutated-same-key-restore-unrelated.gitconfig";
        ConfigurationChangePlan originalPlan = CreatePhysicalTargetPlan(
            ConfigurationTargetKind.GitConfig,
            targetPath
        ) with
        {
            Changes =
            [
                CreatePhysicalTargetChange(ConfigurationTargetKind.GitConfig, targetPath) with
                {
                    Key = "credential.helper",
                    Value = "original-value",
                },
                CreatePhysicalTargetChange(ConfigurationTargetKind.GitConfig, unrelatedTargetPath)
                    with
                    {
                        Key = "credential.helper",
                        Value = "unrelated-original-value",
                    },
            ],
        };
        ConfigurationOwnershipManifest originalManifest =
            await CreatePhase4DPhysicalManifestForTestAsync(originalPlan);
        string existingManifestJson = RawOwnershipManifestJson(originalManifest);
        fileSystem.AtomicWriteAllText(manifestPath, existingManifestJson);
        string? mutatedManifestJson = null;
        var dispatcher = new CallbackPhysicalTargetWriterDispatcher((request, cancellationToken) =>
        {
            cancellationToken.ThrowIfCancellationRequested();
            ConfigurationOwnershipManifest preparedManifest =
                ConfigurationOwnershipManifestSerializer.Deserialize(
                    fileSystem.ReadAllText(manifestPath)
                );
            ConfigurationOwnershipManifestEntry mutatedTargetEntry = preparedManifest
                .Entries.Single(entry => entry.TargetPathOrName == targetPath) with
            {
                PlannedValueSha256 =
                    "0000000000000000000000000000000000000000000000000000000000000000",
            };
            ConfigurationOwnershipManifestEntry unrelatedEntry = preparedManifest
                .Entries.Single(entry => entry.TargetPathOrName == unrelatedTargetPath) with
            {
                Sequence = 1,
            };
            mutatedManifestJson = RawOwnershipManifestJson(
                preparedManifest with
                {
                    ContainsCredentialMaterial =
                        mutatedTargetEntry.IsSecretValue || unrelatedEntry.IsSecretValue,
                    Entries =
                    [
                        unrelatedEntry,
                        mutatedTargetEntry with
                        {
                            Sequence = 2,
                        },
                    ],
                }
            );
            fileSystem.AtomicWriteAllText(manifestPath, mutatedManifestJson);
            throw new IOException(
                "Injected Phase 4D apply dispatch failure after same-key target mutation."
            );
        });
        var manager = new ConfigurationManager(fileSystem, manifestPath, dispatcher);
        ConfigurationChangePlan updatePlan = originalPlan with
        {
            Manifest = originalPlan.Manifest with
            {
                PreviousOwnedEntryHash = HashMetadata(existingManifestJson),
            },
            Changes =
            [
                originalPlan.Changes[0] with
                {
                    Value = "updated-value",
                    PreviousOwnedEntryMetadata = "previous-same-key-entry",
                },
            ],
        };

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(async () =>
            await manager.ApplyAsync(updatePlan, TestContext.Current.CancellationToken)
        );

        Assert.Contains(
            "owned Git config key is missing or duplicated",
            exception.Message,
            StringComparison.Ordinal
        );
        Assert.Empty(dispatcher.Requests);
        Assert.Null(mutatedManifestJson);
        Assert.Equal(existingManifestJson, fileSystem.ReadAllText(manifestPath));
        Assert.False(fileSystem.FileExists(targetPath));
        Assert.False(fileSystem.FileExists(unrelatedTargetPath));
    }

    [Fact]
    public async Task
        FilesystemBackedRemoveRollbackLeavesPhase4DManifestWithUnrelatedEntryAddedAfterPreremoval()
    {
        var fileSystem = new InMemoryFileSystem(InMemoryPathSemantics.Posix);
        const string manifestPath = "/state/phase4d-remove-unrelated-post-preremoval.json";
        const string targetPath = "/config/phase4d-remove-unrelated-post-preremoval.gitconfig";
        const string unrelatedTargetPath =
            "/config/phase4d-remove-unrelated-post-preremoval-unrelated.gitconfig";
        ConfigurationChangePlan setPlan = CreatePhysicalTargetPlan(
            ConfigurationTargetKind.GitConfig,
            targetPath
        );
        ConfigurationOwnershipManifest plannedManifest = await CreateDryRunManifestAsync(setPlan);
        ConfigurationOwnershipManifestEntry plannedEntry = Assert.Single(plannedManifest.Entries);
        ConfigurationOwnershipManifestEntry removedEntry = plannedEntry with
        {
            TargetPathOrName = targetPath,
            Key = "credential.helper",
        };
        ConfigurationOwnershipManifestEntry unrelatedEntry = plannedEntry with
        {
            Sequence = 2,
            TargetPathOrName = unrelatedTargetPath,
            Key = "credential.helper",
        };
        ConfigurationOwnershipManifest existingManifest = plannedManifest with
        {
            PlanId = "previous-phase4d-remove-unrelated-post-preremoval-plan",
            ChangeSetId = "previous-phase4d-remove-unrelated-post-preremoval-changeset",
            Entries =
            [
                removedEntry,
                unrelatedEntry,
            ],
        };
        string existingManifestJson = RawOwnershipManifestJson(existingManifest);
        fileSystem.AtomicWriteAllText(manifestPath, existingManifestJson);
        string? postPreremovalManifestJson = null;
        var dispatcher = new CallbackPhysicalTargetWriterDispatcher((request, cancellationToken) =>
        {
            cancellationToken.ThrowIfCancellationRequested();
            postPreremovalManifestJson = RawOwnershipManifestJson(
                existingManifest with
                {
                    ContainsCredentialMaterial =
                        existingManifest.ContainsCredentialMaterial || removedEntry.IsSecretValue,
                    Entries =
                    [
                        unrelatedEntry with
                        {
                            Sequence = 1,
                        },
                        removedEntry with
                        {
                            Sequence = 2,
                        },
                    ],
                }
            );
            fileSystem.AtomicWriteAllText(manifestPath, postPreremovalManifestJson);
            throw new IOException("Injected Phase 4D remove dispatch failure after preremoval.");
        });
        var manager = new ConfigurationManager(fileSystem, manifestPath, dispatcher);
        ConfigurationChangePlan removePlan = setPlan with
        {
            Manifest = setPlan.Manifest with
            {
                PreviousOwnedEntryHash = HashMetadata(existingManifestJson),
            },
            Changes =
            [
                setPlan.Changes[0] with
                {
                    Operation = ConfigurationChangeOperation.Remove,
                    Key = "credential.helper",
                    Value = null,
                    PreviousOwnedEntryMetadata = "previous-physical-target-entry",
                },
            ],
        };

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(async () =>
            await manager.RemoveAsync(removePlan, TestContext.Current.CancellationToken)
        );

        Assert.Contains(
            "owned Git config key is missing or duplicated",
            exception.Message,
            StringComparison.Ordinal
        );
        Assert.Empty(dispatcher.Requests);
        Assert.Null(postPreremovalManifestJson);
        Assert.Equal(existingManifestJson, fileSystem.ReadAllText(manifestPath));
        Assert.False(fileSystem.FileExists(targetPath));
        Assert.False(fileSystem.FileExists(unrelatedTargetPath));
    }

    [Fact]
    public async Task
        FilesystemBackedRemoveRestoresPhase4DManifestForNormalizedEquivalentTargetOnFailure()
    {
        var fileSystem = new InMemoryFileSystem(InMemoryPathSemantics.Posix);
        const string manifestPath = "/state/phase4d-remove-normalized-dispatch-rollback.json";
        const string manifestTargetPath = "/config/file.gitconfig";
        const string removeTargetPath = "/config/sub/../file.gitconfig";
        var dispatcher = new CallbackPhysicalTargetWriterDispatcher((request, cancellationToken) =>
        {
            cancellationToken.ThrowIfCancellationRequested();
            throw new IOException("Injected Phase 4D remove dispatch failure.");
        });
        var manager = new ConfigurationManager(fileSystem, manifestPath, dispatcher);
        ConfigurationChangePlan setPlan = CreatePhysicalTargetPlan(
            ConfigurationTargetKind.GitConfig,
            manifestTargetPath
        );
        string existingManifestJson = await CreateDryRunManifestJsonAsync(setPlan);
        fileSystem.AtomicWriteAllText(manifestPath, existingManifestJson);
        fileSystem.AtomicWriteAllText(
            manifestTargetPath,
            CreateGitConfigCredentialHelperContents("planned-value")
        );
        ConfigurationChangePlan removePlan = setPlan with
        {
            Manifest = setPlan.Manifest with
            {
                PreviousOwnedEntryHash = HashMetadata(existingManifestJson),
            },
            Changes =
            [
                setPlan.Changes[0] with
                {
                    Operation = ConfigurationChangeOperation.Remove,
                    TargetPathOrName = removeTargetPath,
                    Value = null,
                    PreviousOwnedEntryMetadata = "previous-physical-target-entry",
                },
            ],
        };

        var exception = await Assert.ThrowsAsync<IOException>(async () =>
            await manager.RemoveAsync(removePlan, TestContext.Current.CancellationToken)
        );

        Assert.Contains("dispatch failure", exception.Message, StringComparison.Ordinal);
        Assert.Single(dispatcher.Requests);
        Assert.Equal(existingManifestJson, fileSystem.ReadAllText(manifestPath));
        Assert.Equal(
            CreateGitConfigCredentialHelperContents("planned-value"),
            fileSystem.ReadAllText(manifestTargetPath)
        );
    }

    [Fact]
    public async Task
        FilesystemBackedRemoveRestoresByteExactNonCanonicalPhase4DManifestAfterPreremovalFailure()
    {
        var fileSystem = new InMemoryFileSystem(InMemoryPathSemantics.Posix);
        const string manifestPath = "/state/phase4d-remove-byte-exact-rollback.json";
        const string removedTargetPath = "/config/phase4d-remove-byte-exact-removed.gitconfig";
        const string remainingTargetPath = "/config/phase4d-remove-byte-exact-remaining.gitconfig";
        var dispatcher = new CallbackPhysicalTargetWriterDispatcher((request, cancellationToken) =>
        {
            cancellationToken.ThrowIfCancellationRequested();
            throw new IOException("Injected Phase 4D remove dispatch failure after preremoval.");
        });
        var manager = new ConfigurationManager(fileSystem, manifestPath, dispatcher);
        ConfigurationChangePlan originalPlan = CreatePhysicalTargetPlan(
            ConfigurationTargetKind.GitConfig,
            removedTargetPath
        ) with
        {
            Changes =
            [
                CreatePhysicalTargetChange(
                    ConfigurationTargetKind.GitConfig,
                    removedTargetPath
                ) with
                    {
                        Key = "credential.helper",
                        Value = "removed-planned-value",
                    },
                CreatePhysicalTargetChange(
                    ConfigurationTargetKind.GitConfig,
                    remainingTargetPath
                ) with
                    {
                        Key = "credential.helper",
                        Value = "remaining-planned-value",
                    },
            ],
        };
        ConfigurationOwnershipManifest originalManifest =
            await CreatePhase4DPhysicalManifestForTestAsync(originalPlan);
        string canonicalManifestJson = RawOwnershipManifestJson(originalManifest);
        string nonCanonicalManifestJson = " \n\t" + canonicalManifestJson + "\n";
        byte[] originalManifestBytes = CreateUtf8BomBytes(nonCanonicalManifestJson);
        fileSystem.AtomicWriteAllBytes(manifestPath, originalManifestBytes);
        fileSystem.AtomicWriteAllText(
            removedTargetPath,
            CreateGitConfigCredentialHelperContents("removed-planned-value")
        );
        fileSystem.AtomicWriteAllText(
            remainingTargetPath,
            CreateGitConfigCredentialHelperContents("remaining-planned-value")
        );
        ConfigurationChangePlan removePlan = originalPlan with
        {
            Manifest = originalPlan.Manifest with
            {
                PreviousOwnedEntryHash = HashMetadata(originalManifestBytes),
            },
            Changes =
            [
                originalPlan.Changes[0] with
                {
                    Operation = ConfigurationChangeOperation.Remove,
                    Value = null,
                    PreviousOwnedEntryMetadata = "previous-physical-target-entry",
                },
            ],
        };

        var exception = await Assert.ThrowsAsync<IOException>(async () =>
            await manager.RemoveAsync(removePlan, TestContext.Current.CancellationToken)
        );

        Assert.Contains("dispatch failure", exception.Message, StringComparison.Ordinal);
        Assert.Single(dispatcher.Requests);
        Assert.Equal(originalManifestBytes, fileSystem.ReadAllBytes(manifestPath));
        Assert.NotEqual(Encoding.UTF8.GetBytes(canonicalManifestJson), originalManifestBytes);
        Assert.Equal(
            CreateGitConfigCredentialHelperContents("removed-planned-value"),
            fileSystem.ReadAllText(removedTargetPath)
        );
        Assert.Equal(
            CreateGitConfigCredentialHelperContents("remaining-planned-value"),
            fileSystem.ReadAllText(remainingTargetPath)
        );
    }

    [Fact]
    public async Task
        FilesystemBackedRemoveRollbackLeavesRecreatedPhase4DManifestUntouchedAfterPreremoval()
    {
        var fileSystem = new InMemoryFileSystem(InMemoryPathSemantics.Posix);
        const string manifestPath = "/state/phase4d-remove-recreated-dispatch-rollback.json";
        const string targetPath = "/config/phase4d-remove-recreated-dispatch-rollback.gitconfig";
        const string replacementTargetPath =
            "/config/phase4d-remove-recreated-dispatch-rollback-replacement.gitconfig";
        ConfigurationChangePlan replacementPlan = CreatePhysicalTargetPlan(
            ConfigurationTargetKind.GitConfig,
            replacementTargetPath
        ) with
        {
            Changes =
            [
                CreatePhysicalTargetChange(ConfigurationTargetKind.GitConfig, replacementTargetPath)
                    with
                    {
                        Key = "credential.helper",
                        Value = "replacement-planned-value",
                    },
            ],
        };
        string replacementManifestJson = await CreateDryRunManifestJsonAsync(replacementPlan);
        var dispatcher = new CallbackPhysicalTargetWriterDispatcher((request, cancellationToken) =>
        {
            cancellationToken.ThrowIfCancellationRequested();
            fileSystem.AtomicWriteAllText(manifestPath, replacementManifestJson);
            throw new IOException(
                "Injected Phase 4D remove dispatch failure after manifest recreation."
            );
        });
        var manager = new ConfigurationManager(fileSystem, manifestPath, dispatcher);
        ConfigurationChangePlan setPlan = CreatePhysicalTargetPlan(
            ConfigurationTargetKind.GitConfig,
            targetPath
        );
        string existingManifestJson = await CreateDryRunManifestJsonAsync(setPlan);
        fileSystem.AtomicWriteAllText(manifestPath, existingManifestJson);
        ConfigurationChangePlan removePlan = setPlan with
        {
            Manifest = setPlan.Manifest with
            {
                PreviousOwnedEntryHash = HashMetadata(existingManifestJson),
            },
            Changes =
            [
                setPlan.Changes[0] with
                {
                    Operation = ConfigurationChangeOperation.Remove,
                    Value = null,
                    PreviousOwnedEntryMetadata = "previous-physical-target-entry",
                },
            ],
        };

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(async () =>
            await manager.RemoveAsync(removePlan, TestContext.Current.CancellationToken)
        );

        Assert.Contains(
            "owned Git config key is missing or duplicated",
            exception.Message,
            StringComparison.Ordinal
        );
        Assert.Empty(dispatcher.Requests);
        Assert.Equal(existingManifestJson, fileSystem.ReadAllText(manifestPath));
        Assert.False(fileSystem.FileExists(targetPath));
        Assert.False(fileSystem.FileExists(replacementTargetPath));
    }

    [Theory]
    [InlineData(ConfigurationChangeOperation.Update)]
    [InlineData(ConfigurationChangeOperation.Remove)]
    public async Task
        FilesystemBackedPhase4DRollbackSkipsStaleGitConfigOwnershipSnapshotAfterPreclaimDrift(
        ConfigurationChangeOperation operation
    )
    {
        var fileSystem = new InMemoryFileSystem(InMemoryPathSemantics.Posix);
        string manifestPath = $"/state/phase4d-stale-proof-rollback-{operation}.json";
        string targetPath = $"/config/phase4d-stale-proof-rollback-{operation}.gitconfig";
        const string originalHelper = "owned-helper";
        const string updatedHelper = "updated-helper";
        const string driftedHelper = "drifted-helper";
        string originalGitConfig = CreateGitConfigCredentialHelperContents(originalHelper);
        string driftedGitConfig = CreateGitConfigCredentialHelperContents(driftedHelper);
        fileSystem.AtomicWriteAllText(targetPath, originalGitConfig);
        ConfigurationChangePlan originalPlan = CreateGitConfigCredentialHelperPlan(
            targetPath,
            originalHelper
        );
        string existingManifestJson = await CreatePhase4DPhysicalManifestJsonForTestAsync(
            originalPlan
        );
        fileSystem.AtomicWriteAllText(manifestPath, existingManifestJson);
        var driftInjected = false;
        var dispatcher = new CallbackPhysicalTargetWriterDispatcher((request, cancellationToken) =>
        {
            cancellationToken.ThrowIfCancellationRequested();
            fileSystem.AtomicWriteAllText(targetPath, driftedGitConfig);
            driftInjected = true;
            throw new IOException(
                "Injected Phase 4D dispatch failure after stale proof drift."
            );
        });
        var manager = new ConfigurationManager(fileSystem, manifestPath, dispatcher);
        ConfigurationChangePlan plan = originalPlan with
        {
            Manifest = originalPlan.Manifest with
            {
                PreviousOwnedEntryHash = HashMetadata(existingManifestJson),
            },
            Changes =
            [
                originalPlan.Changes[0] with
                {
                    Operation = operation,
                    Value =
                        operation == ConfigurationChangeOperation.Remove
                            ? null
                            : updatedHelper,
                    PreviousOwnedEntryMetadata = "previous-gitconfig-entry",
                },
            ],
        };

        var exception = await Assert.ThrowsAsync<IOException>(async () =>
        {
            if (operation == ConfigurationChangeOperation.Remove)
            {
                await manager.RemoveAsync(plan, TestContext.Current.CancellationToken);
            }
            else
            {
                await manager.ApplyAsync(plan, TestContext.Current.CancellationToken);
            }
        });

        Assert.Contains("dispatch failure", exception.Message, StringComparison.Ordinal);
        Assert.True(driftInjected);
        Assert.Single(dispatcher.Requests);
        Assert.Equal(driftedGitConfig, fileSystem.ReadAllText(targetPath));
        AssertManifestAbsentOrPreparedPreclaim(fileSystem, manifestPath, existingManifestJson);
    }

    [Fact]
    public async Task
        FilesystemBackedApplyRejectsReentrantPhase4DDispatchWithoutDeadlockOrStaleClaim()
    {
        var fileSystem = new InMemoryFileSystem(InMemoryPathSemantics.Posix);
        const string manifestPath = "/state/phase4d-apply-reentrant-rollback-manifest.json";
        const string outerTargetPath = "/config/phase4d-apply-reentrant-rollback-outer.gitconfig";
        const string reentryTargetPath = "/config/phase4d-apply-reentrant-rollback-inner.gitconfig";
        int entered = 0;
        ConfigurationManager? manager = null;
        ConfigurationChangePlan reentryPlan = CreatePhysicalTargetPlan(
            ConfigurationTargetKind.GitConfig,
            reentryTargetPath
        );
        var dispatcher = new CallbackPhysicalTargetWriterDispatcher(
            async (request, cancellationToken) =>
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (Interlocked.Exchange(ref entered, 1) == 0)
                {
                    await manager!.ApplyAsync(reentryPlan, cancellationToken);
                }
            }
        );
        manager = new ConfigurationManager(fileSystem, manifestPath, dispatcher);
        ConfigurationChangePlan outerPlan = CreatePhysicalTargetPlan(
            ConfigurationTargetKind.GitConfig,
            outerTargetPath
        );

        var exception = await Assert
            .ThrowsAsync<InvalidOperationException>(async () =>
                await manager
                    .ApplyAsync(outerPlan, TestContext.Current.CancellationToken)
                    .AsTask()
                    .WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken)
            );

        Assert.Contains("reentrant execution", exception.Message, StringComparison.Ordinal);
        Assert.Single(dispatcher.Requests);
        Assert.False(fileSystem.FileExists(manifestPath));
        Assert.False(fileSystem.FileExists(outerTargetPath));
        Assert.False(fileSystem.FileExists(reentryTargetPath));
    }

    [Fact]
    public async Task
        FilesystemBackedRemoveRejectsReentrantPhase4DDispatchWithoutDeadlockOrStaleRemoval()
    {
        var fileSystem = new InMemoryFileSystem(InMemoryPathSemantics.Posix);
        const string manifestPath = "/state/phase4d-remove-reentrant-rollback-manifest.json";
        const string outerTargetPath = "/config/phase4d-remove-reentrant-rollback-outer.gitconfig";
        const string reentryTargetPath =
            "/config/phase4d-remove-reentrant-rollback-inner.gitconfig";
        int entered = 0;
        ConfigurationManager? manager = null;
        ConfigurationChangePlan applyPlan = CreatePhysicalTargetPlan(
            ConfigurationTargetKind.GitConfig,
            outerTargetPath
        ) with
        {
            Changes =
            [
                CreatePhysicalTargetChange(ConfigurationTargetKind.GitConfig, outerTargetPath) with
                {
                    Key = "credential.helper",
                    Value = "outer-planned-value",
                },
                CreatePhysicalTargetChange(
                    ConfigurationTargetKind.GitConfig,
                    reentryTargetPath
                ) with
                {
                    Key = "credential.helper",
                    Value = "inner-planned-value",
                },
            ],
        };
        string existingManifestJson = await CreatePhase4DPhysicalManifestJsonForTestAsync(
            applyPlan
        );
        fileSystem.AtomicWriteAllText(manifestPath, existingManifestJson);
        fileSystem.AtomicWriteAllText(
            outerTargetPath,
            CreateGitConfigCredentialHelperContents("outer-planned-value")
        );
        fileSystem.AtomicWriteAllText(
            reentryTargetPath,
            CreateGitConfigCredentialHelperContents("inner-planned-value")
        );
        ConfigurationChangePlan innerRemovePlan = applyPlan with
        {
            Changes =
            [
                applyPlan.Changes[1] with
                {
                    Operation = ConfigurationChangeOperation.Remove,
                    Value = null,
                    PreviousOwnedEntryMetadata = "previous-reentry-physical-target-entry",
                },
            ],
        };
        var dispatcher = new CallbackPhysicalTargetWriterDispatcher(
            async (request, cancellationToken) =>
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (Interlocked.Exchange(ref entered, 1) == 0)
                {
                    string currentManifestJson = fileSystem.ReadAllText(manifestPath);
                    ConfigurationChangePlan reentryRemovePlan = innerRemovePlan with
                    {
                        Manifest = innerRemovePlan.Manifest with
                        {
                            PreviousOwnedEntryHash = HashMetadata(currentManifestJson),
                        },
                    };
                    await manager!.RemoveAsync(reentryRemovePlan, cancellationToken);
                }
            }
        );
        manager = new ConfigurationManager(fileSystem, manifestPath, dispatcher);
        ConfigurationChangePlan outerRemovePlan = applyPlan with
        {
            Manifest = applyPlan.Manifest with
            {
                PreviousOwnedEntryHash = HashMetadata(existingManifestJson),
            },
            Changes =
            [
                applyPlan.Changes[0] with
                {
                    Operation = ConfigurationChangeOperation.Remove,
                    Value = null,
                    PreviousOwnedEntryMetadata = "previous-outer-physical-target-entry",
                },
            ],
        };

        var exception = await Assert
            .ThrowsAsync<InvalidOperationException>(async () =>
                await manager
                    .RemoveAsync(outerRemovePlan, TestContext.Current.CancellationToken)
                    .AsTask()
                    .WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken)
            );

        Assert.Contains("reentrant execution", exception.Message, StringComparison.Ordinal);
        Assert.Single(dispatcher.Requests);
        Assert.Equal(existingManifestJson, fileSystem.ReadAllText(manifestPath));
    }

    [Fact]
    public async Task
        FilesystemBackedApplyRejectsReentrantOverlappingPhase4DDispatchWithoutStaleClaim()
    {
        var fileSystem = new InMemoryFileSystem(InMemoryPathSemantics.Posix);
        const string manifestPath =
            "/state/phase4d-apply-overlapping-reentrant-rollback-manifest.json";
        const string targetPath = "/config/phase4d-apply-overlapping-reentrant-rollback.gitconfig";
        int entered = 0;
        ConfigurationManager? manager = null;
        ConfigurationChangePlan originalPlan = CreatePhysicalTargetPlan(
            ConfigurationTargetKind.GitConfig,
            targetPath
        ) with
        {
            Changes =
            [
                CreatePhysicalTargetChange(ConfigurationTargetKind.GitConfig, targetPath) with
                {
                    Key = "credential.helper",
                    Value = "original-value",
                },
            ],
        };
        string existingManifestJson = await CreateDryRunManifestJsonAsync(originalPlan);
        fileSystem.AtomicWriteAllText(manifestPath, existingManifestJson);
        fileSystem.AtomicWriteAllText(
            targetPath,
            CreateGitConfigCredentialHelperContents("original-value")
        );
        ConfigurationChangePlan reentryPlan = originalPlan with
        {
            Changes =
            [
                originalPlan.Changes[0] with
                {
                    Value = "reentrant-value",
                },
            ],
        };
        var dispatcher = new CallbackPhysicalTargetWriterDispatcher(
            async (request, cancellationToken) =>
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (Interlocked.Exchange(ref entered, 1) == 0)
                {
                    await manager!.ApplyAsync(reentryPlan, cancellationToken);
                }
            }
        );
        manager = new ConfigurationManager(fileSystem, manifestPath, dispatcher);
        ConfigurationChangePlan outerPlan = originalPlan with
        {
            Manifest = originalPlan.Manifest with
            {
                PreviousOwnedEntryHash = HashMetadata(existingManifestJson),
            },
            Changes =
            [
                originalPlan.Changes[0] with
                {
                    Value = "outer-value",
                },
            ],
        };

        var exception = await Assert
            .ThrowsAsync<InvalidOperationException>(async () =>
                await manager
                    .ApplyAsync(outerPlan, TestContext.Current.CancellationToken)
                    .AsTask()
                    .WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken)
            );

        Assert.Contains("reentrant execution", exception.Message, StringComparison.Ordinal);
        Assert.Single(dispatcher.Requests);
        Assert.Equal(existingManifestJson, fileSystem.ReadAllText(manifestPath));
    }

    [Fact]
    public async Task
        FilesystemBackedRemoveRejectsReentrantOverlappingPhase4DDispatchWithoutStaleClaim()
    {
        var fileSystem = new InMemoryFileSystem(InMemoryPathSemantics.Posix);
        const string manifestPath =
            "/state/phase4d-remove-overlapping-reentrant-rollback-manifest.json";
        const string targetPath = "/config/phase4d-remove-overlapping-reentrant-rollback.gitconfig";
        int entered = 0;
        ConfigurationManager? manager = null;
        ConfigurationChangePlan originalPlan = CreatePhysicalTargetPlan(
            ConfigurationTargetKind.GitConfig,
            targetPath
        ) with
        {
            Changes =
            [
                CreatePhysicalTargetChange(ConfigurationTargetKind.GitConfig, targetPath) with
                {
                    Key = "credential.helper",
                    Value = "original-value",
                },
            ],
        };
        string existingManifestJson = await CreateDryRunManifestJsonAsync(originalPlan);
        fileSystem.AtomicWriteAllText(manifestPath, existingManifestJson);
        fileSystem.AtomicWriteAllText(
            targetPath,
            CreateGitConfigCredentialHelperContents("original-value")
        );
        ConfigurationChangePlan reentryPlan = originalPlan with
        {
            Changes =
            [
                originalPlan.Changes[0] with
                {
                    Value = "reentrant-value",
                },
            ],
        };
        var dispatcher = new CallbackPhysicalTargetWriterDispatcher(
            async (request, cancellationToken) =>
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (Interlocked.Exchange(ref entered, 1) == 0)
                {
                    await manager!.ApplyAsync(reentryPlan, cancellationToken);
                }
            }
        );
        manager = new ConfigurationManager(fileSystem, manifestPath, dispatcher);
        ConfigurationChangePlan outerRemovePlan = originalPlan with
        {
            Manifest = originalPlan.Manifest with
            {
                PreviousOwnedEntryHash = HashMetadata(existingManifestJson),
            },
            Changes =
            [
                originalPlan.Changes[0] with
                {
                    Operation = ConfigurationChangeOperation.Remove,
                    Value = null,
                    PreviousOwnedEntryMetadata = "previous-overlapping-physical-target-entry",
                },
            ],
        };

        var exception = await Assert
            .ThrowsAsync<InvalidOperationException>(async () =>
                await manager
                    .RemoveAsync(outerRemovePlan, TestContext.Current.CancellationToken)
                    .AsTask()
                    .WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken)
            );

        Assert.Contains("reentrant execution", exception.Message, StringComparison.Ordinal);
        Assert.Single(dispatcher.Requests);
        Assert.Equal(existingManifestJson, fileSystem.ReadAllText(manifestPath));
    }

    [Fact]
    public async Task FilesystemBackedApplyRejectsPhase4DStaleManifestBeforeDispatch()
    {
        var fileSystem = new InMemoryFileSystem(InMemoryPathSemantics.Posix);
        const string manifestPath = "/state/phase4d-stale-manifest-apply.json";
        const string targetPath = "/config/phase4d-stale-manifest-apply.gitconfig";
        var dispatcher = new RecordingPhysicalTargetWriterDispatcher(fileSystem);
        var manager = new ConfigurationManager(fileSystem, manifestPath, dispatcher);
        ConfigurationChangePlan physicalPlan = CreatePhysicalTargetPlan(
            ConfigurationTargetKind.GitConfig,
            targetPath
        );
        string existingManifestJson = await CreateDryRunManifestJsonAsync(physicalPlan);
        fileSystem.AtomicWriteAllText(manifestPath, existingManifestJson);
        fileSystem.Calls.Clear();
        ConfigurationChangePlan plan = physicalPlan with
        {
            Manifest = physicalPlan.Manifest with
            {
                PreviousOwnedEntryHash = HashMetadata("some-other-manifest"),
            },
        };

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(async () =>
            await manager.ApplyAsync(plan, TestContext.Current.CancellationToken)
        );

        Assert.Contains(
            "before-state hash does not match",
            exception.Message,
            StringComparison.Ordinal
        );
        Assert.Empty(dispatcher.Requests);
        Assert.Equal(existingManifestJson, fileSystem.ReadAllText(manifestPath));
        Assert.False(fileSystem.FileExists(targetPath));
        AssertNoFilesystemMutationOrLockCalls(fileSystem.Calls);
    }

    [Fact]
    public async Task FilesystemBackedRemoveRejectsPhase4DStaleManifestBeforeDispatch()
    {
        var fileSystem = new InMemoryFileSystem(InMemoryPathSemantics.Posix);
        const string manifestPath = "/state/phase4d-stale-manifest-remove.json";
        const string targetPath = "/config/phase4d-stale-manifest-remove.gitconfig";
        var dispatcher = new RecordingPhysicalTargetWriterDispatcher(fileSystem);
        var manager = new ConfigurationManager(fileSystem, manifestPath, dispatcher);
        ConfigurationChangePlan applyPlan = CreatePhysicalTargetPlan(
            ConfigurationTargetKind.GitConfig,
            targetPath
        );
        string existingManifestJson = await CreatePhase4DPhysicalManifestJsonForTestAsync(
            applyPlan
        );
        fileSystem.AtomicWriteAllText(manifestPath, existingManifestJson);
        fileSystem.Calls.Clear();
        ConfigurationChangePlan removePlan = applyPlan with
        {
            Manifest = applyPlan.Manifest with
            {
                PreviousOwnedEntryHash = HashMetadata("some-other-manifest"),
            },
            Changes =
            [
                applyPlan.Changes[0] with
                {
                    Operation = ConfigurationChangeOperation.Remove,
                    Value = null,
                    PreviousOwnedEntryMetadata = "previous-physical-target-entry",
                },
            ],
        };

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(async () =>
            await manager.RemoveAsync(removePlan, TestContext.Current.CancellationToken)
        );

        Assert.Contains(
            "before-state hash does not match",
            exception.Message,
            StringComparison.Ordinal
        );
        Assert.Empty(dispatcher.Requests);
        Assert.Equal(existingManifestJson, fileSystem.ReadAllText(manifestPath));
        Assert.False(fileSystem.FileExists(targetPath));
        AssertNoFilesystemMutationOrLockCalls(fileSystem.Calls);
    }

    [Fact]
    public async Task FilesystemBackedApplyDoesNotRejectAfterPhase4DDispatchWhenManifestChanges()
    {
        var fileSystem = new InMemoryFileSystem(InMemoryPathSemantics.Posix);
        const string manifestPath = "/state/phase4d-no-post-dispatch-reject.json";
        const string targetPath = "/config/phase4d-no-post-dispatch-reject.gitconfig";
        ConfigurationChangePlan concurrentManifestPlan = CreatePhysicalTargetPlan(
            ConfigurationTargetKind.GitConfig,
            "/config/phase4d-no-post-dispatch-reject-foreign.gitconfig"
        );
        string concurrentManifestJson = await CreateDryRunManifestJsonAsync(concurrentManifestPlan);
        var dispatcher = new CallbackPhysicalTargetWriterDispatcher((request, cancellationToken) =>
        {
            cancellationToken.ThrowIfCancellationRequested();
            string targetContents = CreateGitConfigCredentialHelperContents("planned-value");
            byte[] targetContentsBytes = Encoding.UTF8.GetBytes(targetContents);
            fileSystem.AtomicWriteAllText(targetPath, targetContents);
            request.RegisterCompletedFileMutation(
                new ConfigurationPhysicalTargetFileMutation(
                    targetPath,
                    PreviouslyExisted: false,
                    PreviousContentsBytes: null,
                    ExpectedCurrentSha256Hash: HashMetadata(targetContentsBytes)[
                        "sha256:".Length..
                    ]
                )
            );
            fileSystem.AtomicWriteAllText(manifestPath, concurrentManifestJson);
            return ValueTask.CompletedTask;
        });
        var manager = new ConfigurationManager(fileSystem, manifestPath, dispatcher);
        ConfigurationChangePlan plan = CreatePhysicalTargetPlan(
            ConfigurationTargetKind.GitConfig,
            targetPath
        );

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(async () =>
            await manager.ApplyAsync(plan, TestContext.Current.CancellationToken)
        );

        Assert.Contains("ownership manifest", exception.Message, StringComparison.Ordinal);
        Assert.Single(dispatcher.Requests);
        Assert.Equal(concurrentManifestJson, fileSystem.ReadAllText(manifestPath));
    }

    [Fact]
    public async Task FilesystemBackedRemoveDoesNotRejectAfterPhase4DDispatchWhenManifestChanges()
    {
        var fileSystem = new InMemoryFileSystem(InMemoryPathSemantics.Posix);
        const string manifestPath = "/state/phase4d-remove-no-post-dispatch-reject.json";
        const string targetPath = "/config/phase4d-remove-no-post-dispatch-reject.gitconfig";
        ConfigurationChangePlan applyPlan = CreatePhysicalTargetPlan(
            ConfigurationTargetKind.GitConfig,
            targetPath
        );
        string existingManifestJson = await CreatePhase4DPhysicalManifestJsonForTestAsync(
            applyPlan
        );
        byte[] previousTargetContents = Encoding.UTF8.GetBytes(
            CreateGitConfigCredentialHelperContents("planned-value")
        );
        fileSystem.AtomicWriteAllText(
            targetPath,
            Encoding.UTF8.GetString(previousTargetContents)
        );
        fileSystem.AtomicWriteAllText(manifestPath, existingManifestJson);
        ConfigurationChangePlan concurrentManifestPlan = CreatePhysicalTargetPlan(
            ConfigurationTargetKind.GitConfig,
            "/config/phase4d-remove-no-post-dispatch-reject-foreign.gitconfig"
        );
        string concurrentManifestJson = await CreateDryRunManifestJsonAsync(concurrentManifestPlan);
        var dispatcher = new CallbackPhysicalTargetWriterDispatcher((request, cancellationToken) =>
        {
            cancellationToken.ThrowIfCancellationRequested();
            const string targetContents = "";
            byte[] targetContentsBytes = Encoding.UTF8.GetBytes(targetContents);
            fileSystem.AtomicWriteAllText(targetPath, targetContents);
            request.RegisterCompletedFileMutation(
                new ConfigurationPhysicalTargetFileMutation(
                    targetPath,
                    PreviouslyExisted: true,
                    PreviousContentsBytes: previousTargetContents,
                    ExpectedCurrentSha256Hash: HashMetadata(targetContentsBytes)[
                        "sha256:".Length..
                    ]
                )
            );
            fileSystem.AtomicWriteAllText(manifestPath, concurrentManifestJson);
            return ValueTask.CompletedTask;
        });
        var manager = new ConfigurationManager(fileSystem, manifestPath, dispatcher);
        ConfigurationChangePlan removePlan = applyPlan with
        {
            Manifest = applyPlan.Manifest with
            {
                PreviousOwnedEntryHash = HashMetadata(existingManifestJson),
            },
            Changes =
            [
                applyPlan.Changes[0] with
                {
                    Operation = ConfigurationChangeOperation.Remove,
                    Value = null,
                    PreviousOwnedEntryMetadata = "previous-physical-target-entry",
                },
            ],
        };

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(async () =>
            await manager.RemoveAsync(removePlan, TestContext.Current.CancellationToken)
        );

        Assert.Contains("ownership manifest", exception.Message, StringComparison.Ordinal);
        Assert.Single(dispatcher.Requests);
        Assert.Equal(concurrentManifestJson, fileSystem.ReadAllText(manifestPath));
    }

    [Fact]
    public async Task
        FilesystemBackedApplyPhase4DRollbackRejectsDuplicateCompletedMutationNoOpReport()
    {
        var fileSystem = new InMemoryFileSystem(InMemoryPathSemantics.Posix);
        const string manifestPath = "/state/phase4d-duplicate-noop-report-manifest.json";
        const string targetPath = "/config/phase4d-duplicate-noop-report.gitconfig";
        const string originalContents = "[credential]\n\thelper = \"original-helper\"\n";
        const string mutatedContents = "[credential]\n\thelper = \"mutated-helper\"\n";
        byte[] originalBytes = Encoding.UTF8.GetBytes(originalContents);
        byte[] mutatedBytes = Encoding.UTF8.GetBytes(mutatedContents);
        string mutatedHash = HashMetadata(mutatedBytes)["sha256:".Length..];
        fileSystem.AtomicWriteAllText(targetPath, originalContents);
        var dispatcher = new CallbackPhysicalTargetWriterDispatcher((request, cancellationToken) =>
        {
            cancellationToken.ThrowIfCancellationRequested();
            fileSystem.AtomicWriteAllText(targetPath, mutatedContents);
            request.RegisterCompletedFileMutation(
                new ConfigurationPhysicalTargetFileMutation(
                    targetPath,
                    PreviouslyExisted: true,
                    PreviousContentsBytes: originalBytes,
                    ExpectedCurrentSha256Hash: mutatedHash
                )
            );
            request.RegisterCompletedFileMutation(
                new ConfigurationPhysicalTargetFileMutation(
                    targetPath,
                    PreviouslyExisted: true,
                    PreviousContentsBytes: mutatedBytes,
                    ExpectedCurrentSha256Hash: mutatedHash,
                    RequiresRollback: false
                )
            );
            return ValueTask.CompletedTask;
        });
        var manager = new ConfigurationManager(fileSystem, manifestPath, dispatcher);
        ConfigurationChangePlan plan = CreatePhysicalTargetPlan(
            ConfigurationTargetKind.GitConfig,
            targetPath
        );

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(async () =>
            await manager.ApplyAsync(plan, TestContext.Current.CancellationToken)
        );

        Assert.Contains(
            "effective Git credential helper is not proven to be owned",
            exception.Message,
            StringComparison.Ordinal
        );
        Assert.Empty(dispatcher.Requests);
        Assert.Equal(originalContents, fileSystem.ReadAllText(targetPath));
        Assert.False(fileSystem.FileExists(manifestPath));
    }

    [Fact]
    public async Task
        FilesystemBackedApplyPhase4DRollbackRejectsNoOpFirstDuplicateCompletedMutation()
    {
        var fileSystem = new InMemoryFileSystem(InMemoryPathSemantics.Posix);
        const string manifestPath = "/state/phase4d-duplicate-noop-first-manifest.json";
        const string targetPath = "/config/phase4d-duplicate-noop-first.gitconfig";
        const string originalContents = "[credential]\n\thelper = \"original-helper\"\n";
        const string mutatedContents = "[credential]\n\thelper = \"mutated-helper\"\n";
        byte[] originalBytes = Encoding.UTF8.GetBytes(originalContents);
        byte[] mutatedBytes = Encoding.UTF8.GetBytes(mutatedContents);
        string mutatedHash = HashMetadata(mutatedBytes)["sha256:".Length..];
        fileSystem.AtomicWriteAllText(targetPath, originalContents);
        var dispatcher = new CallbackPhysicalTargetWriterDispatcher((request, cancellationToken) =>
        {
            cancellationToken.ThrowIfCancellationRequested();
            fileSystem.AtomicWriteAllText(targetPath, mutatedContents);
            request.RegisterCompletedFileMutation(
                new ConfigurationPhysicalTargetFileMutation(
                    targetPath,
                    PreviouslyExisted: true,
                    PreviousContentsBytes: mutatedBytes,
                    ExpectedCurrentSha256Hash: mutatedHash,
                    RequiresRollback: false
                )
            );
            request.RegisterCompletedFileMutation(
                new ConfigurationPhysicalTargetFileMutation(
                    targetPath,
                    PreviouslyExisted: true,
                    PreviousContentsBytes: originalBytes,
                    ExpectedCurrentSha256Hash: mutatedHash
                )
            );
            return ValueTask.CompletedTask;
        });
        var manager = new ConfigurationManager(fileSystem, manifestPath, dispatcher);
        ConfigurationChangePlan plan = CreatePhysicalTargetPlan(
            ConfigurationTargetKind.GitConfig,
            targetPath
        );

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(async () =>
            await manager.ApplyAsync(plan, TestContext.Current.CancellationToken)
        );

        Assert.Contains(
            "effective Git credential helper is not proven to be owned",
            exception.Message,
            StringComparison.Ordinal
        );
        Assert.Empty(dispatcher.Requests);
        Assert.Equal(originalContents, fileSystem.ReadAllText(targetPath));
        Assert.False(fileSystem.FileExists(manifestPath));
    }

    [Fact]
    public async Task
        FilesystemBackedApplyPhase4DRollbackRegistersValidDuplicateAfterStaleReport()
    {
        var fileSystem = new InMemoryFileSystem(InMemoryPathSemantics.Posix);
        const string manifestPath = "/state/phase4d-duplicate-stale-first-manifest.json";
        const string targetPath = "/config/phase4d-duplicate-stale-first.gitconfig";
        const string equivalentTargetPath =
            "/config/subdirectory/../phase4d-duplicate-stale-first.gitconfig";
        const string originalContents = "[credential]\n\thelper = \"original-helper\"\n";
        const string mutatedContents = "[credential]\n\thelper = \"mutated-helper\"\n";
        byte[] originalBytes = Encoding.UTF8.GetBytes(originalContents);
        byte[] mutatedBytes = Encoding.UTF8.GetBytes(mutatedContents);
        string originalHash = HashMetadata(originalBytes)["sha256:".Length..];
        string mutatedHash = HashMetadata(mutatedBytes)["sha256:".Length..];
        fileSystem.AtomicWriteAllText(targetPath, originalContents);
        var dispatcher = new CallbackPhysicalTargetWriterDispatcher((request, cancellationToken) =>
        {
            cancellationToken.ThrowIfCancellationRequested();
            fileSystem.AtomicWriteAllText(targetPath, mutatedContents);
            request.RegisterCompletedFileMutation(
                new ConfigurationPhysicalTargetFileMutation(
                    targetPath,
                    PreviouslyExisted: true,
                    PreviousContentsBytes: originalBytes,
                    ExpectedCurrentSha256Hash: originalHash
                )
            );
            request.RegisterCompletedFileMutation(
                new ConfigurationPhysicalTargetFileMutation(
                    equivalentTargetPath,
                    PreviouslyExisted: true,
                    PreviousContentsBytes: originalBytes,
                    ExpectedCurrentSha256Hash: mutatedHash
                )
            );
            return ValueTask.CompletedTask;
        });
        var manager = new ConfigurationManager(fileSystem, manifestPath, dispatcher);
        ConfigurationChangePlan plan = CreatePhysicalTargetPlan(
            ConfigurationTargetKind.GitConfig,
            targetPath
        );

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(async () =>
            await manager.ApplyAsync(plan, TestContext.Current.CancellationToken)
        );

        Assert.Contains(
            "effective Git credential helper is not proven to be owned",
            exception.Message,
            StringComparison.Ordinal
        );
        Assert.Empty(dispatcher.Requests);
        Assert.Equal(originalContents, fileSystem.ReadAllText(targetPath));
        Assert.False(fileSystem.FileExists(manifestPath));
    }

    [Fact]
    public async Task
        FilesystemBackedApplyPhase4DRollbackRegistersValidMutationAfterInvalidReportedPath()
    {
        var innerFileSystem = new InMemoryFileSystem(InMemoryPathSemantics.Posix);
        const string manifestPath = "/state/phase4d-invalid-first-report-manifest.json";
        const string targetPath = "/config/phase4d-invalid-first-report.gitconfig";
        const string unrelatedPath = "/config/phase4d-invalid-first-report-unrelated.txt";
        const string invalidReportedPath = "/config/phase4d-invalid-first-report-invalid";
        const string originalContents = "[credential]\n\thelper = \"original-helper\"\n";
        const string mutatedContents = "[credential]\n\thelper = \"mutated-helper\"\n";
        const string unrelatedContents = "unrelated contents";
        byte[] originalBytes = Encoding.UTF8.GetBytes(originalContents);
        byte[] mutatedBytes = Encoding.UTF8.GetBytes(mutatedContents);
        string mutatedHash = HashMetadata(mutatedBytes)["sha256:".Length..];
        innerFileSystem.AtomicWriteAllText(targetPath, originalContents);
        innerFileSystem.AtomicWriteAllText(unrelatedPath, unrelatedContents);
        var fileSystem = new FullPathRemappingFileSystem(
            innerFileSystem,
            invalidReportedPath,
            invalidReportedPath,
            new ArgumentException("Injected invalid reported path.")
        );
        var dispatcher = new CallbackPhysicalTargetWriterDispatcher((request, cancellationToken) =>
        {
            cancellationToken.ThrowIfCancellationRequested();
            fileSystem.AtomicWriteAllText(targetPath, mutatedContents);
            request.RegisterCompletedFileMutation(
                new ConfigurationPhysicalTargetFileMutation(
                    invalidReportedPath,
                    PreviouslyExisted: false,
                    PreviousContentsBytes: null,
                    ExpectedCurrentSha256Hash:
                        "0000000000000000000000000000000000000000000000000000000000000000"
                )
            );
            request.RegisterCompletedFileMutation(
                new ConfigurationPhysicalTargetFileMutation(
                    targetPath,
                    PreviouslyExisted: true,
                    PreviousContentsBytes: originalBytes,
                    ExpectedCurrentSha256Hash: mutatedHash
                )
            );
            return ValueTask.CompletedTask;
        });
        var manager = new ConfigurationManager(fileSystem, manifestPath, dispatcher);
        ConfigurationChangePlan plan = CreatePhysicalTargetPlan(
            ConfigurationTargetKind.GitConfig,
            targetPath
        );

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(async () =>
            await manager.ApplyAsync(plan, TestContext.Current.CancellationToken)
        );

        Assert.Contains(
            "effective Git credential helper is not proven to be owned",
            exception.Message,
            StringComparison.Ordinal
        );
        Assert.Empty(dispatcher.Requests);
        Assert.Equal(originalContents, innerFileSystem.ReadAllText(targetPath));
        Assert.Equal(unrelatedContents, innerFileSystem.ReadAllText(unrelatedPath));
        Assert.False(innerFileSystem.FileExists(manifestPath));
    }

    [Fact]
    public async Task FilesystemBackedApplyFailsFastOnPhase4DDispatcherReentryWithoutDeadlock()
    {
        var fileSystem = new InMemoryFileSystem(InMemoryPathSemantics.Posix);
        const string manifestPath = "/state/phase4d-dispatch-reentry-manifest.json";
        const string outerTargetPath = "/config/phase4d-dispatch-reentry-outer.gitconfig";
        const string reentryTargetPath = "/config/phase4d-dispatch-reentry-inner.gitconfig";
        int entered = 0;
        ConfigurationManager? manager = null;
        ConfigurationChangePlan reentryPlan = CreatePhysicalTargetPlan(
            ConfigurationTargetKind.GitConfig,
            reentryTargetPath
        );
        var dispatcher = new CallbackPhysicalTargetWriterDispatcher(
            async (request, cancellationToken) =>
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (Interlocked.Exchange(ref entered, 1) == 0)
                {
                    await manager!.ApplyAsync(reentryPlan, cancellationToken);
                }
            }
        );
        manager = new ConfigurationManager(fileSystem, manifestPath, dispatcher);
        ConfigurationChangePlan outerPlan = CreatePhysicalTargetPlan(
            ConfigurationTargetKind.GitConfig,
            outerTargetPath
        );

        var exception = await Assert
            .ThrowsAsync<InvalidOperationException>(async () =>
                await manager
                    .ApplyAsync(outerPlan, TestContext.Current.CancellationToken)
                    .AsTask()
                    .WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken)
            );

        Assert.Contains("reentrant execution", exception.Message, StringComparison.Ordinal);
        Assert.Single(dispatcher.Requests);
        Assert.False(fileSystem.FileExists(manifestPath));
    }

    [Fact]
    public async Task
        FilesystemBackedApplyFailsFastOnIdenticalSameKeyPhase4DReentryWithoutStaleClaim()
    {
        var fileSystem = new InMemoryFileSystem(InMemoryPathSemantics.Posix);
        const string manifestPath = "/state/phase4d-identical-reentry-manifest.json";
        const string targetPath = "/config/phase4d-identical-reentry.gitconfig";
        int entered = 0;
        ConfigurationManager? manager = null;
        ConfigurationChangePlan plan = CreatePhysicalTargetPlan(
            ConfigurationTargetKind.GitConfig,
            targetPath
        );
        var dispatcher = new CallbackPhysicalTargetWriterDispatcher(
            async (request, cancellationToken) =>
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (Interlocked.Exchange(ref entered, 1) == 0)
                {
                    await manager!.ApplyAsync(plan, cancellationToken);
                }
            }
        );
        manager = new ConfigurationManager(fileSystem, manifestPath, dispatcher);

        var exception = await Assert
            .ThrowsAsync<InvalidOperationException>(async () =>
                await manager
                    .ApplyAsync(plan, TestContext.Current.CancellationToken)
                    .AsTask()
                    .WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken)
            );

        Assert.Contains("reentrant execution", exception.Message, StringComparison.Ordinal);
        Assert.Single(dispatcher.Requests);
        Assert.False(fileSystem.FileExists(manifestPath));
    }

    [Fact]
    public async Task FilesystemBackedApplyFailsFastOnConcurrentPhase4DDispatch()
    {
        var fileSystem = new InMemoryFileSystem(InMemoryPathSemantics.Posix);
        const string manifestPath = "/state/phase4d-concurrent-dispatch-manifest.json";
        const string outerTargetPath = "/config/phase4d-concurrent-dispatch-outer.gitconfig";
        var dispatchEntered = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously
        );
        var releaseDispatch = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously
        );
        var dispatcher = new CallbackPhysicalTargetWriterDispatcher(
            async (request, cancellationToken) =>
            {
                dispatchEntered.TrySetResult();
                await releaseDispatch.Task.WaitAsync(cancellationToken);
                string targetContents = CreateGitConfigCredentialHelperContents("planned-value");
                byte[] targetContentsBytes = Encoding.UTF8.GetBytes(targetContents);
                fileSystem.AtomicWriteAllText(outerTargetPath, targetContents);
                request.RegisterCompletedFileMutation(
                    new ConfigurationPhysicalTargetFileMutation(
                        outerTargetPath,
                        PreviouslyExisted: false,
                        PreviousContentsBytes: null,
                        ExpectedCurrentSha256Hash: HashMetadata(targetContentsBytes)[
                            "sha256:".Length..
                        ]
                    )
                );
            }
        );
        var manager = new ConfigurationManager(fileSystem, manifestPath, dispatcher);
        ConfigurationChangePlan outerPlan = CreatePhysicalTargetPlan(
            ConfigurationTargetKind.GitConfig,
            outerTargetPath
        );
        ConfigurationChangePlan concurrentPlan = outerPlan;
        Task<ConfigurationPlanResult> outerTask = manager
            .ApplyAsync(outerPlan, TestContext.Current.CancellationToken)
            .AsTask();
        await dispatchEntered.Task.WaitAsync(
            TimeSpan.FromSeconds(5),
            TestContext.Current.CancellationToken
        );

        InvalidOperationException exception;
        try
        {
            exception = await Assert.ThrowsAsync<InvalidOperationException>(async () =>
                await manager
                    .ApplyAsync(concurrentPlan, TestContext.Current.CancellationToken)
                    .AsTask()
                    .WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken)
            );
        }
        finally
        {
            releaseDispatch.TrySetResult();
        }

        ConfigurationPlanResult result = await outerTask.WaitAsync(
            TimeSpan.FromSeconds(5),
            TestContext.Current.CancellationToken
        );

        Assert.Contains(
            "concurrent or reentrant execution",
            exception.Message,
            StringComparison.Ordinal
        );
        Assert.Equal(ConfigurationPlanState.Applied, result.State);
        Assert.Single(dispatcher.Requests);
    }

    [Fact]
    public async Task FilesystemBackedRemoveFailsFastOnConcurrentPhase4DDispatch()
    {
        var fileSystem = new InMemoryFileSystem(InMemoryPathSemantics.Posix);
        const string manifestPath = "/state/phase4d-concurrent-remove-dispatch-manifest.json";
        const string targetPath = "/config/phase4d-concurrent-remove-dispatch.gitconfig";
        byte[] previousTargetContents = Encoding.UTF8.GetBytes(
            CreateGitConfigCredentialHelperContents("planned-value")
        );
        var dispatchEntered = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously
        );
        var releaseDispatch = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously
        );
        var dispatcher = new CallbackPhysicalTargetWriterDispatcher(
            async (request, cancellationToken) =>
            {
                dispatchEntered.TrySetResult();
                await releaseDispatch.Task.WaitAsync(cancellationToken);
                const string targetContents = "";
                byte[] targetContentsBytes = Encoding.UTF8.GetBytes(targetContents);
                fileSystem.AtomicWriteAllText(targetPath, targetContents);
                request.RegisterCompletedFileMutation(
                    new ConfigurationPhysicalTargetFileMutation(
                        targetPath,
                        PreviouslyExisted: true,
                        PreviousContentsBytes: previousTargetContents,
                        ExpectedCurrentSha256Hash: HashMetadata(targetContentsBytes)[
                            "sha256:".Length..
                        ]
                    )
                );
            }
        );
        var manager = new ConfigurationManager(fileSystem, manifestPath, dispatcher);
        ConfigurationChangePlan applyPlan = CreatePhysicalTargetPlan(
            ConfigurationTargetKind.GitConfig,
            targetPath
        );
        string existingManifestJson = await CreatePhase4DPhysicalManifestJsonForTestAsync(
            applyPlan
        );
        fileSystem.AtomicWriteAllText(
            targetPath,
            Encoding.UTF8.GetString(previousTargetContents)
        );
        fileSystem.AtomicWriteAllText(manifestPath, existingManifestJson);
        ConfigurationChangePlan removePlan = applyPlan with
        {
            Manifest = applyPlan.Manifest with
            {
                PreviousOwnedEntryHash = HashMetadata(existingManifestJson),
            },
            Changes =
            [
                applyPlan.Changes[0] with
                {
                    Operation = ConfigurationChangeOperation.Remove,
                    Value = null,
                    PreviousOwnedEntryMetadata = "previous-physical-target-entry",
                },
            ],
        };
        Task<ConfigurationPlanResult> outerTask = manager
            .RemoveAsync(removePlan, TestContext.Current.CancellationToken)
            .AsTask();
        await dispatchEntered.Task.WaitAsync(
            TimeSpan.FromSeconds(5),
            TestContext.Current.CancellationToken
        );

        InvalidOperationException exception;
        try
        {
            exception = await Assert.ThrowsAsync<InvalidOperationException>(async () =>
                await manager
                    .RemoveAsync(removePlan, TestContext.Current.CancellationToken)
                    .AsTask()
                    .WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken)
            );
        }
        finally
        {
            releaseDispatch.TrySetResult();
        }

        ConfigurationPlanResult result = await outerTask.WaitAsync(
            TimeSpan.FromSeconds(5),
            TestContext.Current.CancellationToken
        );

        Assert.Contains(
            "concurrent or reentrant execution",
            exception.Message,
            StringComparison.Ordinal
        );
        Assert.Equal(ConfigurationPlanState.Applied, result.State);
        Assert.Single(dispatcher.Requests);
    }

    [Theory]
    [InlineData(nameof(IConfigurationManager.DryRunAsync))]
    [InlineData(nameof(IConfigurationManager.ApplyAsync))]
    [InlineData(nameof(IConfigurationManager.RemoveAsync))]
    public async Task FilesystemBackedProjectionOnlyDryRunFailsFastOnConcurrentPhase4DExecution(
        string concurrentMethodName
    )
    {
        var fileSystem = new InMemoryFileSystem(InMemoryPathSemantics.Posix);
        const string manifestPath = "/state/phase4d-concurrent-dry-run-lock-manifest.json";
        const string targetPath = "/config/phase4d-concurrent-dry-run-lock.gitconfig";
        var dryRunEntered = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously
        );
        var releaseDryRun = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously
        );
        var dispatcher = new RecordingPhysicalTargetWriterDispatcher(fileSystem);
        var manager = new ConfigurationManager(fileSystem, manifestPath, dispatcher);
        ConfigurationChangePlan applyPlan = CreatePhysicalTargetPlan(
            ConfigurationTargetKind.GitConfig,
            targetPath
        );
        string existingManifestJson = await CreatePhase4DPhysicalManifestJsonForTestAsync(
            applyPlan
        );
        fileSystem.AtomicWriteAllText(manifestPath, existingManifestJson);
        fileSystem.AtomicWriteAllText(targetPath, "[credential]\n\thelper = \"planned-value\"\n");
        ConfigurationChangePlan dryRunAndApplyPlan = applyPlan with
        {
            Manifest = applyPlan.Manifest with
            {
                PreviousOwnedEntryHash = HashMetadata(existingManifestJson),
            },
        };
        ConfigurationChangePlan removePlan = dryRunAndApplyPlan with
        {
            Changes =
            [
                dryRunAndApplyPlan.Changes[0] with
                {
                    Operation = ConfigurationChangeOperation.Remove,
                    Value = null,
                    PreviousOwnedEntryMetadata = "previous-physical-target-entry",
                },
            ],
        };
        ConfigurationChangePlan concurrentPlan =
            concurrentMethodName == nameof(IConfigurationManager.RemoveAsync)
                ? removePlan
                : dryRunAndApplyPlan;
        int entered = 0;
        fileSystem.AfterRecord = (call, fs) =>
        {
            if (
                !string.Equals(
                    call.Operation,
                    nameof(InMemoryFileSystem.FileExists),
                    StringComparison.Ordinal
                )
                || !string.Equals(call.Path, manifestPath, StringComparison.Ordinal)
                || Interlocked.Exchange(ref entered, 1) != 0
            )
            {
                return;
            }

            dryRunEntered.TrySetResult();
            releaseDryRun.Task.GetAwaiter().GetResult();
            fs.AfterRecord = null;
        };
        Task<ConfigurationPlanResult> dryRunTask = Task.Run(async () =>
            await manager.DryRunAsync(dryRunAndApplyPlan, TestContext.Current.CancellationToken)
        );
        await dryRunEntered.Task.WaitAsync(
            TimeSpan.FromSeconds(5),
            TestContext.Current.CancellationToken
        );

        InvalidOperationException exception;
        try
        {
            exception = await Assert.ThrowsAsync<InvalidOperationException>(async () =>
                await CreateExecutionCall(manager, concurrentMethodName, concurrentPlan)()
                    .AsTask()
                    .WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken)
            );
        }
        finally
        {
            releaseDryRun.TrySetResult();
        }

        ConfigurationPlanResult dryRunResult = await dryRunTask.WaitAsync(
            TimeSpan.FromSeconds(5),
            TestContext.Current.CancellationToken
        );

        Assert.Contains(
            "execution is already in progress",
            exception.Message,
            StringComparison.Ordinal
        );
        Assert.Equal(ConfigurationPlanState.Planned, dryRunResult.State);
        Assert.Empty(dispatcher.Requests);
        Assert.Equal(existingManifestJson, fileSystem.ReadAllText(manifestPath));
        Assert.Equal(
            "[credential]\n\thelper = \"planned-value\"\n",
            fileSystem.ReadAllText(targetPath)
        );
    }

    [Fact]
    public async Task FilesystemBackedRemoveFailsFastOnPhase4DDispatcherReentryWithoutDeadlock()
    {
        var fileSystem = new InMemoryFileSystem(InMemoryPathSemantics.Posix);
        const string manifestPath = "/state/phase4d-remove-dispatch-reentry-manifest.json";
        const string outerTargetPath = "/config/phase4d-remove-dispatch-reentry-outer.gitconfig";
        const string reentryTargetPath = "/config/phase4d-remove-dispatch-reentry-inner.gitconfig";
        int entered = 0;
        ConfigurationManager? manager = null;
        ConfigurationChangePlan applyPlan = CreatePhysicalTargetPlan(
            ConfigurationTargetKind.GitConfig,
            outerTargetPath
        ) with
        {
            Changes =
            [
                CreatePhysicalTargetChange(ConfigurationTargetKind.GitConfig, outerTargetPath) with
                {
                    Key = "credential.helper",
                    Value = "outer-planned-value",
                },
                CreatePhysicalTargetChange(
                    ConfigurationTargetKind.GitConfig,
                    reentryTargetPath
                ) with
                {
                    Key = "credential.helper",
                    Value = "inner-planned-value",
                },
            ],
        };
        string existingManifestJson = await CreatePhase4DPhysicalManifestJsonForTestAsync(
            applyPlan
        );
        fileSystem.AtomicWriteAllText(manifestPath, existingManifestJson);
        fileSystem.AtomicWriteAllText(
            outerTargetPath,
            CreateGitConfigCredentialHelperContents("outer-planned-value")
        );
        fileSystem.AtomicWriteAllText(
            reentryTargetPath,
            CreateGitConfigCredentialHelperContents("inner-planned-value")
        );
        ConfigurationChangePlan innerRemovePlan = applyPlan with
        {
            Changes =
            [
                applyPlan.Changes[1] with
                {
                    Operation = ConfigurationChangeOperation.Remove,
                    Value = null,
                    PreviousOwnedEntryMetadata = "previous-reentry-physical-target-entry",
                },
            ],
        };
        var dispatcher = new CallbackPhysicalTargetWriterDispatcher(
            async (request, cancellationToken) =>
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (Interlocked.Exchange(ref entered, 1) == 0)
                {
                    string currentManifestJson = fileSystem.ReadAllText(manifestPath);
                    ConfigurationChangePlan reentryRemovePlan = innerRemovePlan with
                    {
                        Manifest = innerRemovePlan.Manifest with
                        {
                            PreviousOwnedEntryHash = HashMetadata(currentManifestJson),
                        },
                    };
                    await manager!.RemoveAsync(reentryRemovePlan, cancellationToken);
                }
            }
        );
        manager = new ConfigurationManager(fileSystem, manifestPath, dispatcher);
        ConfigurationChangePlan outerRemovePlan = applyPlan with
        {
            Manifest = applyPlan.Manifest with
            {
                PreviousOwnedEntryHash = HashMetadata(existingManifestJson),
            },
            Changes =
            [
                applyPlan.Changes[0] with
                {
                    Operation = ConfigurationChangeOperation.Remove,
                    Value = null,
                    PreviousOwnedEntryMetadata = "previous-outer-physical-target-entry",
                },
            ],
        };

        var exception = await Assert
            .ThrowsAsync<InvalidOperationException>(async () =>
                await manager
                    .RemoveAsync(outerRemovePlan, TestContext.Current.CancellationToken)
                    .AsTask()
                    .WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken)
            );

        Assert.Contains("reentrant execution", exception.Message, StringComparison.Ordinal);
        Assert.Single(dispatcher.Requests);
        Assert.Equal(existingManifestJson, fileSystem.ReadAllText(manifestPath));
    }

    [Theory]
    [InlineData(nameof(IConfigurationManager.ApplyAsync))]
    [InlineData(nameof(IConfigurationManager.RemoveAsync))]
    public async Task
        FilesystemBackedPhase4DPhysicalDispatchRejectsReentrantDryRunWithoutDeadlockOrStaleClaim(
        string methodName
    )
    {
        var fileSystem = new InMemoryFileSystem(InMemoryPathSemantics.Posix);
        const string manifestPath = "/state/phase4d-dispatch-dry-run-reentry-manifest.json";
        const string outerTargetPath = "/config/phase4d-dispatch-dry-run-reentry-outer.gitconfig";
        const string reentryTargetPath = "/config/phase4d-dispatch-dry-run-reentry-inner.gitconfig";
        int entered = 0;
        ConfigurationManager? manager = null;
        ConfigurationChangePlan reentryDryRunPlan = CreatePhysicalTargetPlan(
            ConfigurationTargetKind.GitConfig,
            reentryTargetPath
        );
        var dispatcher = new CallbackPhysicalTargetWriterDispatcher(
            async (request, cancellationToken) =>
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (Interlocked.Exchange(ref entered, 1) == 0)
                {
                    await manager!.DryRunAsync(reentryDryRunPlan, cancellationToken);
                }
            }
        );
        manager = new ConfigurationManager(fileSystem, manifestPath, dispatcher);
        ConfigurationChangePlan outerSetPlan = CreatePhysicalTargetPlan(
            ConfigurationTargetKind.GitConfig,
            outerTargetPath
        );
        string? existingManifestJson = null;
        ConfigurationChangePlan outerPlan = outerSetPlan;
        if (methodName == nameof(IConfigurationManager.RemoveAsync))
        {
            existingManifestJson = await CreateDryRunManifestJsonAsync(outerSetPlan);
            fileSystem.AtomicWriteAllText(manifestPath, existingManifestJson);
            fileSystem.AtomicWriteAllText(
                outerTargetPath,
                CreateGitConfigCredentialHelperContents("planned-value")
            );
            outerPlan = outerSetPlan with
            {
                Manifest = outerSetPlan.Manifest with
                {
                    PreviousOwnedEntryHash = HashMetadata(existingManifestJson),
                },
                Changes =
                [
                    outerSetPlan.Changes[0] with
                    {
                        Operation = ConfigurationChangeOperation.Remove,
                        Value = null,
                        PreviousOwnedEntryMetadata = "previous-physical-target-entry",
                    },
                ],
            };
        }

        var exception = await Assert
            .ThrowsAsync<InvalidOperationException>(async () =>
                await CreateExecutionCall(manager, methodName, outerPlan)()
                    .AsTask()
                    .WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken)
            );

        Assert.Contains("reentrant execution", exception.Message, StringComparison.Ordinal);
        Assert.Single(dispatcher.Requests);
        if (existingManifestJson is null)
        {
            Assert.False(fileSystem.FileExists(manifestPath));
        }
        else
        {
            Assert.Equal(existingManifestJson, fileSystem.ReadAllText(manifestPath));
        }

        if (existingManifestJson is null)
        {
            Assert.False(fileSystem.FileExists(outerTargetPath));
        }
        else
        {
            Assert.Equal(
                CreateGitConfigCredentialHelperContents("planned-value"),
                fileSystem.ReadAllText(outerTargetPath)
            );
        }

        Assert.False(fileSystem.FileExists(reentryTargetPath));
    }

    [Theory]
    [InlineData(nameof(IConfigurationManager.ApplyAsync))]
    [InlineData(nameof(IConfigurationManager.RemoveAsync))]
    [InlineData(nameof(IConfigurationManager.DryRunAsync))]
    public async Task
        FilesystemBackedDryRunRejectsGenericReentryFromSameAsyncFlowWithoutDeadlock(
        string reentryMethodName
    )
    {
        var fileSystem = new InMemoryFileSystem(InMemoryPathSemantics.Posix);
        const string manifestPath = "/state/phase4d-dry-run-generic-reentry-manifest.json";
        const string outerTargetPath = "/config/phase4d-dry-run-generic-reentry-outer.txt";
        const string reentryTargetPath = "/config/phase4d-dry-run-generic-reentry-inner.txt";
        var manager = new ConfigurationManager(fileSystem, manifestPath);
        ConfigurationChangePlan outerPlan = CreateGenericFilePlan(
            ConfigurationChangeOperation.Create,
            outerTargetPath,
            "outer-value"
        );
        ConfigurationChangePlan reentryPlan = CreateGenericFilePlan(
            ConfigurationChangeOperation.Create,
            reentryTargetPath,
            "inner-value"
        );
        string? existingManifestJson = null;
        if (reentryMethodName == nameof(IConfigurationManager.RemoveAsync))
        {
            existingManifestJson = await CreateDryRunManifestJsonAsync(reentryPlan);
            fileSystem.AtomicWriteAllText(reentryTargetPath, "inner-value");
            fileSystem.AtomicWriteAllText(manifestPath, existingManifestJson);
            outerPlan = CreateGenericFilePlan(
                ConfigurationChangeOperation.Create,
                outerTargetPath,
                "outer-value",
                previousManifestHash: HashMetadata(existingManifestJson)
            );
            reentryPlan = CreateGenericFilePlan(
                ConfigurationChangeOperation.Remove,
                reentryTargetPath,
                null,
                HashMetadata("inner-value"),
                HashMetadata(existingManifestJson)
            );
        }

        Exception? reentryException = null;
        int entered = 0;
        fileSystem.AfterRecord = (call, _) =>
        {
            if (
                !string.Equals(
                    call.Operation,
                    nameof(InMemoryFileSystem.FileExists),
                    StringComparison.Ordinal
                )
                || !string.Equals(call.Path, manifestPath, StringComparison.Ordinal)
                || Interlocked.Exchange(ref entered, 1) != 0
            )
            {
                return;
            }

            using var reentryTimeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
            try
            {
                ValueTask<ConfigurationPlanResult> reentryCall = reentryMethodName switch
                {
                    nameof(IConfigurationManager.ApplyAsync) =>
                        manager.ApplyAsync(reentryPlan, reentryTimeout.Token),
                    nameof(IConfigurationManager.RemoveAsync) =>
                        manager.RemoveAsync(reentryPlan, reentryTimeout.Token),
                    nameof(IConfigurationManager.DryRunAsync) =>
                        manager.DryRunAsync(reentryPlan, reentryTimeout.Token),
                    _ => throw new ArgumentOutOfRangeException(
                        nameof(reentryMethodName),
                        reentryMethodName,
                        null
                    ),
                };
                reentryCall.AsTask().GetAwaiter().GetResult();
            }
            catch (Exception exception)
            {
                reentryException = exception;
            }
        };

        ConfigurationPlanResult result = await manager
            .DryRunAsync(outerPlan, TestContext.Current.CancellationToken)
            .AsTask()
            .WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);

        Assert.Equal(ConfigurationPlanState.Planned, result.State);
        var invalidOperationException = Assert.IsType<InvalidOperationException>(reentryException);
        Assert.Contains(
            "reentrant execution",
            invalidOperationException.Message,
            StringComparison.Ordinal
        );
        Assert.Equal(1, entered);
        Assert.False(fileSystem.FileExists(outerTargetPath));
        if (existingManifestJson is null)
        {
            Assert.False(fileSystem.FileExists(manifestPath));
        }
        else
        {
            Assert.Equal(existingManifestJson, fileSystem.ReadAllText(manifestPath));
        }
    }

    [Fact]
    public async Task
        FilesystemBackedApplyRejectsMixedPhase4DPhysicalAndNonPhase4DTargetsWithoutMutation()
    {
        var fileSystem = new InMemoryFileSystem(InMemoryPathSemantics.Posix);
        const string manifestPath = "/state/mixed-physical-manifest.json";
        const string gitTargetPath = "/config/mixed-physical.gitconfig";
        const string npmrcTargetPath = "/config/mixed-physical.npmrc";
        var dispatcher = new RecordingPhysicalTargetWriterDispatcher(fileSystem);
        var manager = new ConfigurationManager(fileSystem, manifestPath, dispatcher);
        ConfigurationChangePlan physicalPlan = CreatePhysicalTargetPlan(
            ConfigurationTargetKind.GitConfig,
            gitTargetPath
        );
        ConfigurationChangePlan plan = physicalPlan with
        {
            Changes =
            [
                physicalPlan.Changes[0],
                CreateNpmrcFileChange(npmrcTargetPath),
            ],
        };

        var exception = await Assert.ThrowsAsync<NotSupportedException>(async () =>
            await manager.ApplyAsync(plan, TestContext.Current.CancellationToken)
        );

        Assert.Contains("mixing 4D physical", exception.Message, StringComparison.Ordinal);
        Assert.Empty(dispatcher.Requests);
        Assert.False(fileSystem.FileExists(gitTargetPath));
        Assert.False(fileSystem.FileExists(npmrcTargetPath));
        Assert.False(fileSystem.FileExists(manifestPath));
        AssertNoFilesystemMutationOrLockCalls(fileSystem.Calls);
    }

    [Fact]
    public async Task
        FilesystemBackedRemoveRejectsMixedPhase4DPhysicalAndNonPhase4DTargetsWithoutMutation()
    {
        var fileSystem = new InMemoryFileSystem(InMemoryPathSemantics.Posix);
        const string manifestPath = "/state/mixed-remove-physical-manifest.json";
        const string gitTargetPath = "/config/mixed-remove-physical.gitconfig";
        const string npmrcTargetPath = "/config/mixed-remove-physical.npmrc";
        var dispatcher = new RecordingPhysicalTargetWriterDispatcher(fileSystem);
        var manager = new ConfigurationManager(fileSystem, manifestPath, dispatcher);
        ConfigurationChangePlan physicalPlan = CreatePhysicalTargetPlan(
            ConfigurationTargetKind.GitConfig,
            gitTargetPath
        );
        ConfigurationChange physicalRemove = physicalPlan.Changes[0] with
        {
            Operation = ConfigurationChangeOperation.Remove,
            Value = null,
            PreviousOwnedEntryMetadata = "previous-physical-entry",
        };
        ConfigurationChange npmrcRemove = CreateNpmrcFileChange(npmrcTargetPath) with
        {
            Operation = ConfigurationChangeOperation.Remove,
            Value = null,
            PreviousOwnedEntryMetadata = "previous-npmrc-entry",
        };
        ConfigurationChangePlan plan = physicalPlan with
        {
            Changes =
            [
                physicalRemove,
                npmrcRemove,
            ],
        };

        var exception = await Assert.ThrowsAsync<NotSupportedException>(async () =>
            await manager.RemoveAsync(plan, TestContext.Current.CancellationToken)
        );

        Assert.Contains("mixing 4D physical", exception.Message, StringComparison.Ordinal);
        Assert.Empty(dispatcher.Requests);
        Assert.False(fileSystem.FileExists(gitTargetPath));
        Assert.False(fileSystem.FileExists(npmrcTargetPath));
        Assert.False(fileSystem.FileExists(manifestPath));
        AssertNoFilesystemMutationOrLockCalls(fileSystem.Calls);
    }

    [Theory]
    [InlineData(nameof(IConfigurationManager.ApplyAsync))]
    [InlineData(nameof(IConfigurationManager.RemoveAsync))]
    public async Task
        FilesystemBackedExecutionRejectsMixedPhase4DTargetKindsAtDistinctPathsWithoutMutation(
        string methodName
    )
    {
        var fileSystem = new InMemoryFileSystem(InMemoryPathSemantics.Posix);
        const string manifestPath = "/state/mixed-phase4d-distinct-paths-manifest.json";
        const string gitTargetPath = "/config/mixed-phase4d-distinct-paths.gitconfig";
        const string keyringTargetPath = "/config/mixed-phase4d-distinct-paths-keyring";
        const string existingGitTargetContents = "pre-existing git target contents";
        const string existingKeyringTargetContents = "pre-existing keyring target contents";
        var dispatcher = new RecordingPhysicalTargetWriterDispatcher(fileSystem);
        var manager = new ConfigurationManager(fileSystem, manifestPath, dispatcher);
        ConfigurationChangePlan gitPlan = CreatePhysicalTargetPlan(
            ConfigurationTargetKind.GitConfig,
            gitTargetPath
        );
        string existingManifestJson = await CreateDryRunManifestJsonAsync(gitPlan);
        fileSystem.AtomicWriteAllText(gitTargetPath, existingGitTargetContents);
        fileSystem.AtomicWriteAllText(keyringTargetPath, existingKeyringTargetContents);
        fileSystem.AtomicWriteAllText(manifestPath, existingManifestJson);
        fileSystem.Calls.Clear();
        bool remove = methodName == nameof(IConfigurationManager.RemoveAsync);
        ConfigurationChange gitChange = remove
            ? gitPlan.Changes[0] with
            {
                Operation = ConfigurationChangeOperation.Remove,
                Value = null,
                PreviousOwnedEntryMetadata = "previous-git-physical-entry",
            }
            : gitPlan.Changes[0];
        ConfigurationChange keyringChange = CreatePhysicalTargetChange(
            ConfigurationTargetKind.KeyringShim,
            keyringTargetPath
        );
        if (remove)
        {
            keyringChange = keyringChange with
            {
                Operation = ConfigurationChangeOperation.Remove,
                Value = null,
                PreviousOwnedEntryMetadata = "previous-keyring-physical-entry",
            };
        }

        ConfigurationChangePlan plan = gitPlan with
        {
            Manifest = gitPlan.Manifest with
            {
                PreviousOwnedEntryHash = HashMetadata(existingManifestJson),
            },
            Changes =
            [
                gitChange,
                keyringChange,
            ],
        };

        var exception = await Assert.ThrowsAsync<NotSupportedException>(async () =>
            await CreateExecutionCall(manager, methodName, plan)()
        );

        Assert.Contains("one 4D physical target kind", exception.Message, StringComparison.Ordinal);
        Assert.Empty(dispatcher.Requests);
        AssertNoFilesystemMutationOrLockCalls(fileSystem.Calls);
        Assert.Equal(existingGitTargetContents, fileSystem.ReadAllText(gitTargetPath));
        Assert.Equal(existingKeyringTargetContents, fileSystem.ReadAllText(keyringTargetPath));
        Assert.Equal(existingManifestJson, fileSystem.ReadAllText(manifestPath));
    }

    [Fact]
    public async Task FilesystemBackedDryRunWithDispatcherDoesNotDispatchPhase4DPhysicalTargets()
    {
        var fileSystem = new InMemoryFileSystem(InMemoryPathSemantics.Posix);
        const string manifestPath = "/state/dry-run-dispatcher-manifest.json";
        const string targetPath = "/config/dry-run-dispatcher.gitconfig";
        var dispatcher = new RecordingPhysicalTargetWriterDispatcher(fileSystem);
        var manager = new ConfigurationManager(fileSystem, manifestPath, dispatcher);
        ConfigurationChangePlan plan = CreatePhysicalTargetPlan(
            ConfigurationTargetKind.GitConfig,
            targetPath
        );

        ConfigurationPlanResult result = await manager.DryRunAsync(
            plan,
            TestContext.Current.CancellationToken
        );

        Assert.Equal(ConfigurationPlanState.Planned, result.State);
        Assert.Empty(dispatcher.Requests);
        Assert.False(fileSystem.FileExists(targetPath));
        Assert.False(fileSystem.FileExists(manifestPath));
        AssertNoFilesystemMutationOrLockCalls(fileSystem.Calls);
    }

    [Fact]
    public async Task
        FilesystemBackedPhase4DDryRunRejectsUnsupportedConditionalMutationsBeforeRead()
    {
        var fileSystem = new InMemoryFileSystem(InMemoryPathSemantics.Posix)
        {
            SupportsConditionalFileMutations = false,
        };
        const string manifestPath = "/state/phase4d-dry-run-unsupported-conditional.json";
        const string targetPath = "/config/phase4d-dry-run-unsupported-conditional.gitconfig";
        var dispatcher = new RecordingPhysicalTargetWriterDispatcher(fileSystem);
        var manager = new ConfigurationManager(fileSystem, manifestPath, dispatcher);
        ConfigurationChangePlan plan = CreatePhysicalTargetPlan(
            ConfigurationTargetKind.GitConfig,
            targetPath
        );

        var exception = await Assert.ThrowsAsync<PlatformNotSupportedException>(async () =>
            await manager.DryRunAsync(plan, TestContext.Current.CancellationToken)
        );

        Assert.Contains(
            "conditional file mutation",
            exception.Message,
            StringComparison.OrdinalIgnoreCase
        );
        Assert.Empty(dispatcher.Requests);
        AssertNoFilesystemMutationOrLockCalls(fileSystem.Calls);
        AssertNoFilesystemStateReadCallsBeforeLockAcquisition(fileSystem.Calls);
        Assert.False(fileSystem.FileExists(targetPath));
        Assert.False(fileSystem.FileExists(manifestPath));
    }

    [Fact]
    public async Task FilesystemBackedPhase4DPhysicalDryRunThenApplyKeepsOneChangeShapeConsistent()
    {
        var fileSystem = new InMemoryFileSystem(InMemoryPathSemantics.Posix);
        const string manifestPath = "/state/phase4d-dry-run-then-apply-shape.json";
        const string targetPath = "/config/phase4d-dry-run-then-apply-shape.gitconfig";
        var dispatcher = new RecordingPhysicalTargetWriterDispatcher(fileSystem);
        var manager = new ConfigurationManager(fileSystem, manifestPath, dispatcher);
        ConfigurationChangePlan plan = CreatePhysicalTargetPlan(
            ConfigurationTargetKind.GitConfig,
            targetPath
        );

        ConfigurationPlanResult dryRun = await manager.DryRunAsync(
            plan,
            TestContext.Current.CancellationToken
        );
        ConfigurationPlanResult result = await manager.ApplyAsync(
            plan,
            TestContext.Current.CancellationToken
        );

        Assert.Equal(ConfigurationPlanState.Planned, dryRun.State);
        Assert.Equal(ConfigurationPlanState.Applied, result.State);
        Assert.Equal(dryRun.PlannedOperations, result.PlannedOperations);
        Assert.Equal(dryRun.Changes, result.Changes);
        ConfigurationPlannedOperation plannedOperation = Assert.Single(dryRun.PlannedOperations);
        Assert.Equal(ConfigurationChangeOperation.Set, plannedOperation.Change.Operation);
        Assert.Equal(ConfigurationTargetKind.GitConfig, plannedOperation.Change.TargetKind);
        Assert.Equal(targetPath, plannedOperation.Change.TargetPathOrName);
        Assert.Single(result.PlannedOperations);
        ConfigurationPlannedChange plannedChange = Assert.Single(dryRun.Changes);
        Assert.Equal(ConfigurationChangeOperation.Set, plannedChange.Operation);
        Assert.Equal(ConfigurationTargetKind.GitConfig, plannedChange.TargetKind);
        Assert.Equal(targetPath, plannedChange.TargetPathOrName);
        Assert.Single(result.Changes);
        Assert.Equal(
            ConfigurationOwnershipManifestSerializer.Serialize(dryRun.OwnershipManifest!),
            ConfigurationOwnershipManifestSerializer.Serialize(result.OwnershipManifest!)
        );
        Assert.Equal(
            ConfigurationOwnershipManifestSerializer.Serialize(result.OwnershipManifest!),
            fileSystem.ReadAllText(manifestPath)
        );
        Assert.Single(dispatcher.Requests);
        Assert.True(fileSystem.FileExists(targetPath));
    }

    [Fact]
    public async Task FilesystemBackedPhase4DPhysicalDryRunThenRemoveKeepsOneChangeShapeConsistent()
    {
        var fileSystem = new InMemoryFileSystem(InMemoryPathSemantics.Posix);
        const string manifestPath = "/state/phase4d-dry-run-then-remove-shape.json";
        const string targetPath = "/config/phase4d-dry-run-then-remove-shape.gitconfig";
        var dispatcher = new RecordingPhysicalTargetWriterDispatcher(fileSystem);
        var manager = new ConfigurationManager(fileSystem, manifestPath, dispatcher);
        ConfigurationChangePlan setPlan = CreatePhysicalTargetPlan(
            ConfigurationTargetKind.GitConfig,
            targetPath
        );
        string existingManifestJson = await CreateDryRunManifestJsonAsync(setPlan);
        fileSystem.AtomicWriteAllText(
            targetPath,
            "[credential]\n\thelper = \"planned-value\"\n"
        );
        fileSystem.AtomicWriteAllText(manifestPath, existingManifestJson);
        ConfigurationChangePlan removePlan = setPlan with
        {
            Manifest = setPlan.Manifest with
            {
                PreviousOwnedEntryHash = HashMetadata(existingManifestJson),
            },
            Changes =
            [
                setPlan.Changes[0] with
                {
                    Operation = ConfigurationChangeOperation.Remove,
                    Value = null,
                    PreviousOwnedEntryMetadata = "previous-physical-target-entry",
                },
            ],
        };

        ConfigurationPlanResult dryRun = await manager.DryRunAsync(
            removePlan,
            TestContext.Current.CancellationToken
        );
        ConfigurationPlanResult result = await manager.RemoveAsync(
            removePlan,
            TestContext.Current.CancellationToken
        );

        Assert.Equal(ConfigurationPlanState.Planned, dryRun.State);
        Assert.Equal(ConfigurationPlanState.Applied, result.State);
        Assert.Equal(dryRun.PlannedOperations, result.PlannedOperations);
        Assert.Equal(dryRun.Changes, result.Changes);
        ConfigurationPlannedOperation plannedOperation = Assert.Single(dryRun.PlannedOperations);
        Assert.Equal(ConfigurationChangeOperation.Remove, plannedOperation.Change.Operation);
        Assert.Equal(ConfigurationTargetKind.GitConfig, plannedOperation.Change.TargetKind);
        Assert.Equal(targetPath, plannedOperation.Change.TargetPathOrName);
        Assert.Single(result.PlannedOperations);
        ConfigurationPlannedChange plannedChange = Assert.Single(dryRun.Changes);
        Assert.Equal(ConfigurationChangeOperation.Remove, plannedChange.Operation);
        Assert.Equal(ConfigurationTargetKind.GitConfig, plannedChange.TargetKind);
        Assert.Equal(targetPath, plannedChange.TargetPathOrName);
        Assert.Single(result.Changes);
        Assert.Null(dryRun.OwnershipManifest);
        Assert.Null(result.OwnershipManifest);
        Assert.False(fileSystem.FileExists(manifestPath));
        Assert.Single(dispatcher.Requests);
        Assert.True(fileSystem.FileExists(targetPath));
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
    public async Task ApplyUpdateReplacesMiddleManifestEntryInOriginalOrder()
    {
        var fileSystem = new InMemoryFileSystem(InMemoryPathSemantics.Posix);
        const string firstPath = "/config/middle-merge-first.txt";
        const string secondPath = "/config/middle-merge-second.txt";
        const string thirdPath = "/config/middle-merge-third.txt";
        const string manifestPath = "/state/middle-merge-manifest.json";
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
                CreateGenericFileChange(
                    ConfigurationChangeOperation.Create,
                    thirdPath,
                    "third-owned"
                ),
            ],
        };
        await manager.ApplyAsync(createPlan, TestContext.Current.CancellationToken);
        string manifestBefore = fileSystem.ReadAllText(manifestPath);
        ConfigurationChangePlan updatePlan = CreateGenericFilePlan(
            ConfigurationChangeOperation.Update,
            secondPath,
            "second-updated",
            HashMetadata("second-owned"),
            previousManifestHash: HashMetadata(manifestBefore)
        );

        ConfigurationPlanResult result = await manager.ApplyAsync(
            updatePlan,
            TestContext.Current.CancellationToken
        );

        ConfigurationOwnershipManifest manifest =
            ConfigurationOwnershipManifestSerializer.Deserialize(
                fileSystem.ReadAllText(manifestPath)
            );
        Assert.Equal(
            ConfigurationOwnershipManifestSerializer.Serialize(result.OwnershipManifest!),
            ConfigurationOwnershipManifestSerializer.Serialize(manifest)
        );
        Assert.Collection(
            manifest.Entries,
            entry =>
            {
                Assert.Equal(1, entry.Sequence);
                Assert.Equal(firstPath, entry.TargetPathOrName);
                Assert.Equal(ConfigurationChangeOperation.Create, entry.Operation);
                Assert.Equal(
                    HashMetadata("first-owned")["sha256:".Length..],
                    entry.PlannedValueSha256
                );
            },
            entry =>
            {
                Assert.Equal(2, entry.Sequence);
                Assert.Equal(secondPath, entry.TargetPathOrName);
                Assert.Equal(ConfigurationChangeOperation.Update, entry.Operation);
                Assert.Equal(
                    HashMetadata("second-updated")["sha256:".Length..],
                    entry.PlannedValueSha256
                );
            },
            entry =>
            {
                Assert.Equal(3, entry.Sequence);
                Assert.Equal(thirdPath, entry.TargetPathOrName);
                Assert.Equal(ConfigurationChangeOperation.Create, entry.Operation);
                Assert.Equal(
                    HashMetadata("third-owned")["sha256:".Length..],
                    entry.PlannedValueSha256
                );
            }
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

    [Theory]
    [InlineData(nameof(IConfigurationManager.ApplyAsync))]
    [InlineData(nameof(IConfigurationManager.RemoveAsync))]
    [InlineData(nameof(IConfigurationManager.DryRunAsync))]
    public async Task ExistingCiTemporaryFileManifestEntryMergesWhenEntryMergeKeyHasNoKeyPart(
        string methodName
    )
    {
        var fileSystem = new InMemoryFileSystem(InMemoryPathSemantics.Posix);
        const string targetPath = "/config/existing-ci-temporary-merge-key.txt";
        const string manifestPath = "/state/existing-ci-temporary-merge-key-manifest.json";
        const string before = "owned-before";
        const string after = "owned-after";
        string manifestBefore = await CreateSingleGenericFileManifestJsonAsync(targetPath, before);
        fileSystem.AtomicWriteAllText(targetPath, before);
        fileSystem.AtomicWriteAllText(manifestPath, manifestBefore);
        var manager = new ConfigurationManager(fileSystem, manifestPath);
        bool remove = methodName == nameof(IConfigurationManager.RemoveAsync);
        ConfigurationChangePlan plan = CreateGenericFilePlan(
            remove ? ConfigurationChangeOperation.Remove : ConfigurationChangeOperation.Update,
            targetPath,
            remove ? null : after,
            HashMetadata(before),
            HashMetadata(manifestBefore)
        );

        ConfigurationPlanResult result = await CreateExecutionCall(manager, methodName, plan)();

        if (methodName == nameof(IConfigurationManager.DryRunAsync))
        {
            Assert.Equal(before, fileSystem.ReadAllText(targetPath));
            Assert.Equal(manifestBefore, fileSystem.ReadAllText(manifestPath));
            ConfigurationOwnershipManifestEntry entry = Assert.Single(
                result.OwnershipManifest!.Entries
            );
            Assert.Equal(HashMetadata(after)["sha256:".Length..], entry.PlannedValueSha256);
        }
        else if (remove)
        {
            Assert.Equal(ConfigurationPlanState.Applied, result.State);
            Assert.Null(result.OwnershipManifest);
            Assert.False(fileSystem.FileExists(targetPath));
            Assert.False(fileSystem.FileExists(manifestPath));
        }
        else
        {
            Assert.Equal(ConfigurationPlanState.Applied, result.State);
            Assert.Equal(after, fileSystem.ReadAllText(targetPath));
            ConfigurationOwnershipManifestEntry entry = Assert.Single(
                result.OwnershipManifest!.Entries
            );
            Assert.Equal(targetPath, entry.TargetPathOrName);
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
    public async Task FilesystemBackedDryRunRejectsMixedRemoveAndSetCiTemporaryPlan()
    {
        var fileSystem = new InMemoryFileSystem(InMemoryPathSemantics.Posix);
        const string removeTargetPath = "/config/dry-run-mixed-remove-set/remove.txt";
        const string setTargetPath = "/config/dry-run-mixed-remove-set/set.txt";
        const string manifestPath = "/state/dry-run-mixed-remove-set-manifest.json";
        const string before = "owned-before";
        const string after = "owned-after";
        var manager = new ConfigurationManager(fileSystem, manifestPath);
        await manager.ApplyAsync(
            CreateGenericFilePlan(ConfigurationChangeOperation.Create, removeTargetPath, before),
            TestContext.Current.CancellationToken
        );
        string manifestBefore = fileSystem.ReadAllText(manifestPath);
        ConfigurationChangePlan plan = CreateGenericFilePlan(
            ConfigurationChangeOperation.Remove,
            removeTargetPath,
            value: null,
            previousOwnedEntryMetadata: HashMetadata(before),
            previousManifestHash: HashMetadata(manifestBefore)
        ) with
        {
            Changes =
            [
                CreateGenericFileChange(
                    ConfigurationChangeOperation.Remove,
                    removeTargetPath,
                    value: null,
                    previousOwnedEntryMetadata: HashMetadata(before)
                ),
                CreateGenericFileChange(ConfigurationChangeOperation.Set, setTargetPath, after),
            ],
        };
        fileSystem.Calls.Clear();

        var exception = await Assert.ThrowsAsync<NotSupportedException>(async () =>
            await manager.DryRunAsync(plan, TestContext.Current.CancellationToken)
        );

        Assert.Contains(
            "cannot be executed by apply or remove",
            exception.Message,
            StringComparison.Ordinal
        );
        AssertNoFilesystemMutationOrLockCalls(fileSystem.Calls);
        Assert.Equal(before, fileSystem.ReadAllText(removeTargetPath));
        Assert.False(fileSystem.FileExists(setTargetPath));
        Assert.Equal(manifestBefore, fileSystem.ReadAllText(manifestPath));
    }

    [Fact]
    public async Task FilesystemBackedDryRunSimulatesRemoveOnlyPartialCiTemporaryPlan()
    {
        var fileSystem = new InMemoryFileSystem(InMemoryPathSemantics.Posix);
        const string removeTargetPath = "/config/dry-run-remove-only-partial/remove.txt";
        const string keepTargetPath = "/config/dry-run-remove-only-partial/keep.txt";
        const string manifestPath = "/state/dry-run-remove-only-partial-manifest.json";
        const string removeValue = "remove-owned";
        const string keepValue = "keep-owned";
        var manager = new ConfigurationManager(fileSystem, manifestPath);
        ConfigurationChangePlan createPlan = CreateGenericFilePlan(
            ConfigurationChangeOperation.Create,
            removeTargetPath,
            removeValue
        ) with
        {
            Changes =
            [
                CreateGenericFileChange(
                    ConfigurationChangeOperation.Create,
                    removeTargetPath,
                    removeValue
                ),
                CreateGenericFileChange(
                    ConfigurationChangeOperation.Create,
                    keepTargetPath,
                    keepValue
                ),
            ],
        };
        await manager.ApplyAsync(createPlan, TestContext.Current.CancellationToken);
        string manifestBefore = fileSystem.ReadAllText(manifestPath);
        ConfigurationChangePlan removePlan = CreateGenericFilePlan(
            ConfigurationChangeOperation.Remove,
            removeTargetPath,
            value: null,
            previousOwnedEntryMetadata: HashMetadata(removeValue),
            previousManifestHash: HashMetadata(manifestBefore)
        );
        fileSystem.Calls.Clear();

        ConfigurationPlanResult result = await manager.DryRunAsync(
            removePlan,
            TestContext.Current.CancellationToken
        );

        Assert.Equal(ConfigurationPlanState.Planned, result.State);
        ConfigurationOwnershipManifest manifest = Assert.IsType<ConfigurationOwnershipManifest>(
            result.OwnershipManifest
        );
        ConfigurationOwnershipManifestEntry entry = Assert.Single(manifest.Entries);
        Assert.Equal(keepTargetPath, entry.TargetPathOrName);
        Assert.Equal(ConfigurationChangeOperation.Create, entry.Operation);
        AssertNoFilesystemMutationOrLockCalls(fileSystem.Calls);
        Assert.Equal(removeValue, fileSystem.ReadAllText(removeTargetPath));
        Assert.Equal(keepValue, fileSystem.ReadAllText(keepTargetPath));
        Assert.Equal(manifestBefore, fileSystem.ReadAllText(manifestPath));
    }

    [Fact]
    public async Task FilesystemBackedDryRunSimulatesRemoveOnlyFullCiTemporaryPlan()
    {
        var fileSystem = new InMemoryFileSystem(InMemoryPathSemantics.Posix);
        const string targetPath = "/config/dry-run-remove-only-full/remove.txt";
        const string manifestPath = "/state/dry-run-remove-only-full-manifest.json";
        const string value = "remove-owned";
        var manager = new ConfigurationManager(fileSystem, manifestPath);
        await manager.ApplyAsync(
            CreateGenericFilePlan(ConfigurationChangeOperation.Create, targetPath, value),
            TestContext.Current.CancellationToken
        );
        string manifestBefore = fileSystem.ReadAllText(manifestPath);
        ConfigurationChangePlan removePlan = CreateGenericFilePlan(
            ConfigurationChangeOperation.Remove,
            targetPath,
            value: null,
            previousOwnedEntryMetadata: HashMetadata(value),
            previousManifestHash: HashMetadata(manifestBefore)
        );
        fileSystem.Calls.Clear();

        ConfigurationPlanResult result = await manager.DryRunAsync(
            removePlan,
            TestContext.Current.CancellationToken
        );

        Assert.Equal(ConfigurationPlanState.Planned, result.State);
        Assert.Null(result.OwnershipManifest);
        AssertNoFilesystemMutationOrLockCalls(fileSystem.Calls);
        Assert.Equal(value, fileSystem.ReadAllText(targetPath));
        Assert.Equal(manifestBefore, fileSystem.ReadAllText(manifestPath));
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
    public async Task DryRunRejectsReservedCiTemporaryFileSystemLockTargetBeforeMutation()
    {
        var fileSystem = new InMemoryFileSystem(InMemoryPathSemantics.Posix);
        const string targetPath = "/config/.azureauth-credprovider.fs.lock";
        const string manifestPath = "/state/reserved-lock-dry-run-manifest.json";
        var manager = new ConfigurationManager(fileSystem, manifestPath);
        ConfigurationChangePlan plan = CreateGenericFilePlan(
            ConfigurationChangeOperation.Create,
            targetPath,
            "owned-after"
        );

        var exception = await Assert.ThrowsAsync<NotSupportedException>(async () =>
            await manager.DryRunAsync(plan, TestContext.Current.CancellationToken)
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
    public void ReservedCiTemporaryFileSystemLockComparisonUsesPathIdentitySemantics()
    {
        Assert.True(
            ConfigurationManager.IsReservedInternalFileSystemArtifact(
                "/config/.AZUREAUTH-CREDPROVIDER.FS.LOCK",
                StringComparison.OrdinalIgnoreCase
            )
        );
        Assert.True(
            ConfigurationManager.IsReservedInternalFileSystemArtifact(
                "/config/.AZUREAUTH-CREDPROVIDER.FS.LOCK/descendant",
                StringComparison.OrdinalIgnoreCase
            )
        );
        Assert.True(
            ConfigurationManager.IsReservedInternalFileSystemArtifact(
                "/config/.AZUREAUTH-CREDPROVIDER.LIFECYCLE-LOCKS/descendant",
                StringComparison.OrdinalIgnoreCase
            )
        );
        Assert.True(
            ConfigurationManager.IsReservedInternalFileSystemArtifact(
                "/config/.azureauth-credprovider.lifecycle-locks/descendant",
                StringComparison.Ordinal
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
    public async Task ApplyRejectsUnsupportedExistingManifestTargetKindBeforeMutation()
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

        Assert.Contains(
            "registered retained-proof validator",
            exception.Message,
            StringComparison.Ordinal
        );
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
    public async Task RemoveRejectsUnsupportedExistingManifestTargetKindBeforeMutation()
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

        Assert.Contains(
            "registered retained-proof validator",
            exception.Message,
            StringComparison.Ordinal
        );
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

    [Theory]
    [InlineData(ConfigurationChangeOperation.Update)]
    [InlineData(ConfigurationChangeOperation.Remove)]
    public async Task
        GenericCiTemporaryFileRetainedGitConfigDriftAfterFinalManifestWriteDeletesUnsafeManifest(
        ConfigurationChangeOperation operation
    )
    {
        var fileSystem = new InMemoryFileSystem(InMemoryPathSemantics.Posix);
        string genericTargetPath = $"/config/generic-retained-drift-{operation}.txt";
        string gitConfigPath = $"/config/generic-retained-drift-{operation}.gitconfig";
        string manifestPath = $"/state/generic-retained-drift-{operation}-manifest.json";
        const string genericBefore = "generic-before";
        const string genericAfter = "generic-after";
        const string retainedHelper = "retained-helper";
        const string driftedHelper = "drifted-helper";
        string retainedGitConfig = CreateGitConfigCredentialHelperContents(retainedHelper);
        string driftedGitConfig = CreateGitConfigCredentialHelperContents(driftedHelper);
        fileSystem.AtomicWriteAllText(genericTargetPath, genericBefore);
        fileSystem.AtomicWriteAllText(gitConfigPath, retainedGitConfig);
        ConfigurationChangePlan existingGenericPlan = CreateGenericFilePlan(
            ConfigurationChangeOperation.Create,
            genericTargetPath,
            genericBefore
        );
        ConfigurationOwnershipManifest existingManifest =
            await CreateGenericFileAndRetainedGitConfigManifestForTestAsync(
                existingGenericPlan,
                gitConfigPath,
                retainedHelper
            );
        string existingManifestJson = RawOwnershipManifestJson(existingManifest);
        fileSystem.AtomicWriteAllText(manifestPath, existingManifestJson);
        var finalManifestWriteObserved = false;
        fileSystem.AfterRecord = (call, fs) =>
        {
            if (
                string.Equals(
                    call.Operation,
                    nameof(IFileSystem.AtomicWriteAllText),
                    StringComparison.Ordinal
                )
                && string.Equals(call.Path, manifestPath, StringComparison.Ordinal)
            )
            {
                fs.AfterRecord = null;
                finalManifestWriteObserved = true;
                fs.AtomicWriteAllText(gitConfigPath, driftedGitConfig);
            }
        };
        var manager = new ConfigurationManager(fileSystem, manifestPath);
        ConfigurationChangePlan plan = CreateGenericFilePlan(
            operation,
            genericTargetPath,
            operation == ConfigurationChangeOperation.Remove ? null : genericAfter,
            previousOwnedEntryMetadata: HashMetadata(genericBefore),
            previousManifestHash: HashMetadata(existingManifestJson)
        );

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(async () =>
        {
            if (operation == ConfigurationChangeOperation.Remove)
            {
                await manager.RemoveAsync(plan, TestContext.Current.CancellationToken);
            }
            else
            {
                await manager.ApplyAsync(plan, TestContext.Current.CancellationToken);
            }
        });

        Assert.Contains("Git config", exception.Message, StringComparison.Ordinal);
        Assert.True(finalManifestWriteObserved);
        Assert.Equal(genericBefore, fileSystem.ReadAllText(genericTargetPath));
        Assert.Equal(driftedGitConfig, fileSystem.ReadAllText(gitConfigPath));
        Assert.False(fileSystem.FileExists(manifestPath));
    }

    [Theory]
    [InlineData(ConfigurationChangeOperation.Update)]
    [InlineData(ConfigurationChangeOperation.Remove)]
    public async Task
        GenericCiTemporaryFileRetainedGitConfigCancellationBeforeFinalWritePreservesCancellation(
        ConfigurationChangeOperation operation
    )
    {
        var fileSystem = new InMemoryFileSystem(InMemoryPathSemantics.Posix);
        string genericTargetPath =
            $"/config/generic-retained-cancel-before-final-{operation}.txt";
        string gitConfigPath =
            $"/config/generic-retained-cancel-before-final-{operation}.gitconfig";
        string manifestPath =
            $"/state/generic-retained-cancel-before-final-{operation}-manifest.json";
        const string genericBefore = "generic-before";
        const string genericAfter = "generic-after";
        const string retainedHelper = "retained-helper";
        string retainedGitConfig = CreateGitConfigCredentialHelperContents(retainedHelper);
        fileSystem.AtomicWriteAllText(genericTargetPath, genericBefore);
        fileSystem.AtomicWriteAllText(gitConfigPath, retainedGitConfig);
        ConfigurationChangePlan existingGenericPlan = CreateGenericFilePlan(
            ConfigurationChangeOperation.Create,
            genericTargetPath,
            genericBefore
        );
        ConfigurationOwnershipManifest existingManifest =
            await CreateGenericFileAndRetainedGitConfigManifestForTestAsync(
                existingGenericPlan,
                gitConfigPath,
                retainedHelper
            );
        string existingManifestJson = RawOwnershipManifestJson(existingManifest);
        fileSystem.AtomicWriteAllText(manifestPath, existingManifestJson);
        using var cancellation = new CancellationTokenSource();
        var genericMutationObserved = false;
        fileSystem.AfterRecord = (call, fs) =>
        {
            bool isExpectedMutation =
                operation == ConfigurationChangeOperation.Remove
                    ? string.Equals(
                        call.Operation,
                        nameof(IFileSystem.DeleteFile),
                        StringComparison.Ordinal
                    )
                    : string.Equals(
                        call.Operation,
                        nameof(IFileSystem.AtomicWriteAllText),
                        StringComparison.Ordinal
                    );
            if (
                !genericMutationObserved
                && isExpectedMutation
                && string.Equals(call.Path, genericTargetPath, StringComparison.Ordinal)
            )
            {
                genericMutationObserved = true;
                fs.AfterRecord = null;
                cancellation.Cancel();
            }
        };
        var manager = new ConfigurationManager(fileSystem, manifestPath);
        ConfigurationChangePlan plan = CreateGenericFilePlan(
            operation,
            genericTargetPath,
            operation == ConfigurationChangeOperation.Remove ? null : genericAfter,
            previousOwnedEntryMetadata: HashMetadata(genericBefore),
            previousManifestHash: HashMetadata(existingManifestJson)
        );

        await Assert.ThrowsAsync<OperationCanceledException>(async () =>
        {
            if (operation == ConfigurationChangeOperation.Remove)
            {
                await manager.RemoveAsync(plan, cancellation.Token);
            }
            else
            {
                await manager.ApplyAsync(plan, cancellation.Token);
            }
        });

        Assert.True(genericMutationObserved);
        Assert.Equal(genericBefore, fileSystem.ReadAllText(genericTargetPath));
        Assert.Equal(retainedGitConfig, fileSystem.ReadAllText(gitConfigPath));
        Assert.Equal(existingManifestJson, fileSystem.ReadAllText(manifestPath));
    }

    [Theory]
    [InlineData(ConfigurationChangeOperation.Update)]
    [InlineData(ConfigurationChangeOperation.Remove)]
    public async Task
        GenericCiTemporaryFileRetainedGitConfigNonDurableFinalWritePreservesWriteError(
        ConfigurationChangeOperation operation
    )
    {
        var fileSystem = new FailOnAtomicWriteFileSystem(InMemoryPathSemantics.Posix);
        string genericTargetPath =
            $"/config/generic-retained-nondurable-final-{operation}.txt";
        string gitConfigPath =
            $"/config/generic-retained-nondurable-final-{operation}.gitconfig";
        string manifestPath =
            $"/state/generic-retained-nondurable-final-{operation}-manifest.json";
        const string genericBefore = "generic-before";
        const string genericAfter = "generic-after";
        const string retainedHelper = "retained-helper";
        string retainedGitConfig = CreateGitConfigCredentialHelperContents(retainedHelper);
        fileSystem.AtomicWriteAllText(genericTargetPath, genericBefore);
        fileSystem.AtomicWriteAllText(gitConfigPath, retainedGitConfig);
        ConfigurationChangePlan existingGenericPlan = CreateGenericFilePlan(
            ConfigurationChangeOperation.Create,
            genericTargetPath,
            genericBefore
        );
        ConfigurationOwnershipManifest existingManifest =
            await CreateGenericFileAndRetainedGitConfigManifestForTestAsync(
                existingGenericPlan,
                gitConfigPath,
                retainedHelper
            );
        string existingManifestJson = RawOwnershipManifestJson(existingManifest);
        fileSystem.AtomicWriteAllText(manifestPath, existingManifestJson);
        fileSystem.ResetAtomicWriteCount();
        fileSystem.FailOnAtomicWriteNumber =
            operation == ConfigurationChangeOperation.Remove ? 1 : 2;
        var finalManifestWriteObserved = false;
        fileSystem.AfterRecord = (call, _) =>
        {
            if (
                string.Equals(
                    call.Operation,
                    nameof(IFileSystem.AtomicWriteAllText),
                    StringComparison.Ordinal
                )
                && string.Equals(call.Path, manifestPath, StringComparison.Ordinal)
            )
            {
                finalManifestWriteObserved = true;
            }
        };
        var manager = new ConfigurationManager(fileSystem, manifestPath);
        ConfigurationChangePlan plan = CreateGenericFilePlan(
            operation,
            genericTargetPath,
            operation == ConfigurationChangeOperation.Remove ? null : genericAfter,
            previousOwnedEntryMetadata: HashMetadata(genericBefore),
            previousManifestHash: HashMetadata(existingManifestJson)
        );

        var exception = await Assert.ThrowsAsync<IOException>(async () =>
        {
            if (operation == ConfigurationChangeOperation.Remove)
            {
                await manager.RemoveAsync(plan, TestContext.Current.CancellationToken);
            }
            else
            {
                await manager.ApplyAsync(plan, TestContext.Current.CancellationToken);
            }
        });

        Assert.Contains(
            "Injected atomic write failure",
            exception.Message,
            StringComparison.Ordinal
        );
        Assert.True(finalManifestWriteObserved);
        Assert.Equal(genericBefore, fileSystem.ReadAllText(genericTargetPath));
        Assert.Equal(retainedGitConfig, fileSystem.ReadAllText(gitConfigPath));
        Assert.Equal(existingManifestJson, fileSystem.ReadAllText(manifestPath));
    }

    [Theory]
    [InlineData(ConfigurationChangeOperation.Update)]
    [InlineData(ConfigurationChangeOperation.Remove)]
    public async Task
        GenericCiTemporaryFileRetainedGitConfigCommitFailureDeletesUnsafeManifest(
        ConfigurationChangeOperation operation
    )
    {
        var fileSystem = new FailOnAtomicWriteFileSystem(InMemoryPathSemantics.Posix);
        string genericTargetPath = $"/config/generic-retained-commit-failure-{operation}.txt";
        string gitConfigPath = $"/config/generic-retained-commit-failure-{operation}.gitconfig";
        string manifestPath =
            $"/state/generic-retained-commit-failure-{operation}-manifest.json";
        const string genericBefore = "generic-before";
        const string genericAfter = "generic-after";
        const string retainedHelper = "retained-helper";
        const string driftedHelper = "drifted-helper";
        string retainedGitConfig = CreateGitConfigCredentialHelperContents(retainedHelper);
        string driftedGitConfig = CreateGitConfigCredentialHelperContents(driftedHelper);
        fileSystem.AtomicWriteAllText(genericTargetPath, genericBefore);
        fileSystem.AtomicWriteAllText(gitConfigPath, retainedGitConfig);
        ConfigurationChangePlan existingGenericPlan = CreateGenericFilePlan(
            ConfigurationChangeOperation.Create,
            genericTargetPath,
            genericBefore
        );
        ConfigurationOwnershipManifest existingManifest =
            await CreateGenericFileAndRetainedGitConfigManifestForTestAsync(
                existingGenericPlan,
                gitConfigPath,
                retainedHelper
            );
        string existingManifestJson = RawOwnershipManifestJson(existingManifest);
        fileSystem.AtomicWriteAllText(manifestPath, existingManifestJson);
        fileSystem.ResetAtomicWriteCount();
        fileSystem.FailOnAtomicWriteNumber =
            operation == ConfigurationChangeOperation.Remove ? 1 : 2;
        fileSystem.FailAfterAtomicWrite = true;
        var finalManifestCommitObserved = false;
        fileSystem.AfterRecord = (call, fs) =>
        {
            if (
                string.Equals(
                    call.Operation,
                    nameof(IFileSystem.AtomicWriteAllText),
                    StringComparison.Ordinal
                )
                && string.Equals(call.Path, manifestPath, StringComparison.Ordinal)
                && fs.AtomicWriteCount == fs.FailOnAtomicWriteNumber
            )
            {
                finalManifestCommitObserved = true;
                fs.WriteAllText(gitConfigPath, driftedGitConfig);
            }
        };
        var manager = new ConfigurationManager(fileSystem, manifestPath);
        ConfigurationChangePlan plan = CreateGenericFilePlan(
            operation,
            genericTargetPath,
            operation == ConfigurationChangeOperation.Remove ? null : genericAfter,
            previousOwnedEntryMetadata: HashMetadata(genericBefore),
            previousManifestHash: HashMetadata(existingManifestJson)
        );

        var exception = await Assert.ThrowsAsync<FileMutationException>(async () =>
        {
            if (operation == ConfigurationChangeOperation.Remove)
            {
                await manager.RemoveAsync(plan, TestContext.Current.CancellationToken);
            }
            else
            {
                await manager.ApplyAsync(plan, TestContext.Current.CancellationToken);
            }
        });

        Assert.Contains("Injected post-write atomic write failure", exception.Message);
        Assert.True(finalManifestCommitObserved);
        Assert.Equal(genericBefore, fileSystem.ReadAllText(genericTargetPath));
        Assert.Equal(driftedGitConfig, fileSystem.ReadAllText(gitConfigPath));
        Assert.False(fileSystem.FileExists(manifestPath));
    }

    [Fact]
    public async Task
        GenericCiTemporaryFileRetainedGitConfigPostFinalFailureRestoresValidManifest()
    {
        var fileSystem = new FailOnAtomicWriteFileSystem(InMemoryPathSemantics.Posix);
        const string genericTargetPath = "/config/generic-retained-valid-final.txt";
        const string gitConfigPath = "/config/generic-retained-valid-final.gitconfig";
        const string manifestPath = "/state/generic-retained-valid-final-manifest.json";
        const string genericBefore = "generic-before";
        const string retainedHelper = "retained-helper";
        string retainedGitConfig = CreateGitConfigCredentialHelperContents(retainedHelper);
        fileSystem.AtomicWriteAllText(genericTargetPath, genericBefore);
        fileSystem.AtomicWriteAllText(gitConfigPath, retainedGitConfig);
        ConfigurationChangePlan existingGenericPlan = CreateGenericFilePlan(
            ConfigurationChangeOperation.Create,
            genericTargetPath,
            genericBefore
        );
        ConfigurationOwnershipManifest existingManifest =
            await CreateGenericFileAndRetainedGitConfigManifestForTestAsync(
                existingGenericPlan,
                gitConfigPath,
                retainedHelper
            );
        string existingManifestJson = RawOwnershipManifestJson(existingManifest);
        fileSystem.AtomicWriteAllText(manifestPath, existingManifestJson);
        fileSystem.ResetAtomicWriteCount();
        fileSystem.FailOnAtomicWriteNumber = 2;
        fileSystem.FailAfterAtomicWrite = true;
        var finalManifestCommitObserved = false;
        fileSystem.AfterRecord = (call, fs) =>
        {
            if (
                string.Equals(
                    call.Operation,
                    nameof(IFileSystem.AtomicWriteAllText),
                    StringComparison.Ordinal
                )
                && string.Equals(call.Path, manifestPath, StringComparison.Ordinal)
                && fs.AtomicWriteCount == fs.FailOnAtomicWriteNumber
            )
            {
                finalManifestCommitObserved = true;
            }
        };
        var manager = new ConfigurationManager(fileSystem, manifestPath);
        ConfigurationChangePlan plan = CreateGenericFilePlan(
            ConfigurationChangeOperation.Update,
            genericTargetPath,
            genericBefore,
            previousOwnedEntryMetadata: HashMetadata(genericBefore),
            previousManifestHash: HashMetadata(existingManifestJson)
        );

        var exception = await Assert.ThrowsAsync<FileMutationException>(async () =>
            await manager.ApplyAsync(plan, TestContext.Current.CancellationToken)
        );
        fileSystem.AfterRecord = null;

        Assert.Contains("Injected post-write atomic write failure", exception.Message);
        Assert.True(finalManifestCommitObserved);
        Assert.Equal(genericBefore, fileSystem.ReadAllText(genericTargetPath));
        Assert.Equal(retainedGitConfig, fileSystem.ReadAllText(gitConfigPath));
        Assert.Equal(existingManifestJson, fileSystem.ReadAllText(manifestPath));
    }

    [Theory]
    [InlineData(ConfigurationChangeOperation.Update)]
    [InlineData(ConfigurationChangeOperation.Remove)]
    public async Task
        GenericCiTemporaryFileRetainedGitConfigRollbackKeepsSameIdentityReplacement(
        ConfigurationChangeOperation operation
    )
    {
        var fileSystem = new InMemoryFileSystem(InMemoryPathSemantics.Posix);
        string genericTargetPath = $"/config/generic-retained-replacement-{operation}.txt";
        string gitConfigPath = $"/config/generic-retained-replacement-{operation}.gitconfig";
        string manifestPath = $"/state/generic-retained-replacement-{operation}-manifest.json";
        const string genericBefore = "generic-before";
        const string genericAfter = "generic-after";
        const string retainedHelper = "retained-helper";
        const string driftedHelper = "drifted-helper";
        string retainedGitConfig = CreateGitConfigCredentialHelperContents(retainedHelper);
        string driftedGitConfig = CreateGitConfigCredentialHelperContents(driftedHelper);
        fileSystem.AtomicWriteAllText(genericTargetPath, genericBefore);
        fileSystem.AtomicWriteAllText(gitConfigPath, retainedGitConfig);
        ConfigurationChangePlan existingGenericPlan = CreateGenericFilePlan(
            ConfigurationChangeOperation.Create,
            genericTargetPath,
            genericBefore
        );
        ConfigurationOwnershipManifest existingManifest =
            await CreateGenericFileAndRetainedGitConfigManifestForTestAsync(
                existingGenericPlan,
                gitConfigPath,
                retainedHelper
            );
        string existingManifestJson = RawOwnershipManifestJson(existingManifest);
        string replacementManifestJson = RawOwnershipManifestJson(
            existingManifest with
            {
                SafeMetadata = new Dictionary<string, string>
                {
                    ["replacement"] = "same-identity",
                },
            }
        );
        fileSystem.AtomicWriteAllText(manifestPath, existingManifestJson);
        var finalManifestWriteObserved = false;
        var replacementManifestWritten = false;
        fileSystem.AfterRecord = (call, fs) =>
        {
            if (
                !finalManifestWriteObserved
                && string.Equals(
                    call.Operation,
                    nameof(IFileSystem.AtomicWriteAllText),
                    StringComparison.Ordinal
                )
                && string.Equals(call.Path, manifestPath, StringComparison.Ordinal)
            )
            {
                finalManifestWriteObserved = true;
                fs.AtomicWriteAllText(gitConfigPath, driftedGitConfig);
                return;
            }

            if (
                finalManifestWriteObserved
                && !replacementManifestWritten
                && string.Equals(
                    call.Operation,
                    nameof(IFileSystem.AtomicWriteAllBytes),
                    StringComparison.Ordinal
                )
                && string.Equals(call.Path, genericTargetPath, StringComparison.Ordinal)
            )
            {
                replacementManifestWritten = true;
                fs.AtomicWriteAllText(manifestPath, replacementManifestJson);
            }
        };
        var manager = new ConfigurationManager(fileSystem, manifestPath);
        ConfigurationChangePlan plan = CreateGenericFilePlan(
            operation,
            genericTargetPath,
            operation == ConfigurationChangeOperation.Remove ? null : genericAfter,
            previousOwnedEntryMetadata: HashMetadata(genericBefore),
            previousManifestHash: HashMetadata(existingManifestJson)
        );

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(async () =>
        {
            if (operation == ConfigurationChangeOperation.Remove)
            {
                await manager.RemoveAsync(plan, TestContext.Current.CancellationToken);
            }
            else
            {
                await manager.ApplyAsync(plan, TestContext.Current.CancellationToken);
            }
        });

        Assert.Contains("Git config", exception.Message, StringComparison.Ordinal);
        Assert.True(exception.Data.Contains("ConfigurationRollbackFailure"));
        Assert.True(finalManifestWriteObserved);
        Assert.True(replacementManifestWritten);
        Assert.Equal(genericBefore, fileSystem.ReadAllText(genericTargetPath));
        Assert.Equal(driftedGitConfig, fileSystem.ReadAllText(gitConfigPath));
        Assert.Equal(replacementManifestJson, fileSystem.ReadAllText(manifestPath));
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
        fileSystem.Calls.Clear();

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(async () =>
            await manager.DryRunAsync(plan, TestContext.Current.CancellationToken)
        );

        Assert.Contains("directory", exception.Message, StringComparison.OrdinalIgnoreCase);
        AssertNoFilesystemMutationOrLockCalls(fileSystem.Calls);
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
        fileSystem.Calls.Clear();

        var exception = await Assert.ThrowsAsync<NotSupportedException>(async () =>
            await manager.DryRunAsync(plan, TestContext.Current.CancellationToken)
        );

        Assert.Contains("non-directory", exception.Message, StringComparison.Ordinal);
        AssertNoFilesystemMutationOrLockCalls(fileSystem.Calls);
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
        fileSystem.Calls.Clear();

        var exception = await Assert.ThrowsAsync<NotSupportedException>(async () =>
            await manager.DryRunAsync(plan, TestContext.Current.CancellationToken)
        );

        Assert.Contains("non-directory", exception.Message, StringComparison.Ordinal);
        AssertNoFilesystemMutationOrLockCalls(fileSystem.Calls);
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
        AssertNoFilesystemMutationOrLockCalls(fileSystem.Calls);
        Assert.False(fileSystem.FileExists(targetPath));
        Assert.False(fileSystem.FileExists(manifestPath));
    }

    [Fact]
    public async Task FilesystemBackedDryRunRejectsCiTemporaryPlanWithNonCiTemporaryFileTarget()
    {
        var fileSystem = new InMemoryFileSystem(InMemoryPathSemantics.Posix);
        const string manifestPath = "/state/non-ci-temporary-file-target-dry-run-manifest.json";
        var manager = new ConfigurationManager(fileSystem, manifestPath);
        ConfigurationChangePlan plan = ConfigurationChangePlanPolicy.Create(
            "plan-non-ci-temporary-file-target-dry-run",
            "changeset-non-ci-temporary-file-target-dry-run",
            "azureauth-credprovider",
            ConfigurationScope.CiTemporary,
            new ConfigurationManifestMetadata
            {
                ManifestId = "manifest-non-ci-temporary-file-target-dry-run",
                OwnerProductId = "azureauth-credprovider",
                EntrySelector = "yarn.nodeLinker",
                ProductVersion = "0.0.0-test",
            },
            [
                new ConfigurationChange
                {
                    Operation = ConfigurationChangeOperation.Set,
                    TargetKind = ConfigurationTargetKind.Yarnrc,
                    TargetPathOrName = "/config/non-ci-temporary-file-target/.yarnrc.yml",
                    Key = "nodeLinker",
                    Value = "node-modules",
                    RequiresOwnershipRecord = true,
                    PreserveDeclarationsAndComments = true,
                },
            ],
            temporaryContainer: CreateTemporaryHomeContainer(
                "/config/non-ci-temporary-file-target"
            ),
            declarationPreservation:
                ConfigurationDeclarationPreservation.CompleteMergedTemporaryConfig
        );

        var exception = await Assert.ThrowsAsync<NotSupportedException>(async () =>
            await manager.DryRunAsync(plan, TestContext.Current.CancellationToken)
        );

        Assert.Contains(
            "supports only CI temporary file targets",
            exception.Message,
            StringComparison.Ordinal
        );
        AssertNoFilesystemMutationOrLockCalls(fileSystem.Calls);
        Assert.Empty(fileSystem.Files);
        Assert.False(fileSystem.FileExists(manifestPath));
    }

    [Fact]
    public async Task
        ApplyAsyncReportsTargetKindErrorForNonCiTemporaryFileWithUnsupportedOperation()
    {
        var fileSystem = new InMemoryFileSystem(InMemoryPathSemantics.Posix);
        const string manifestPath =
            "/state/non-ci-temporary-file-unsupported-operation-manifest.json";
        var manager = new ConfigurationManager(fileSystem, manifestPath);
        ConfigurationChangePlan plan = ConfigurationChangePlanPolicy.Create(
            "plan-non-ci-temporary-file-unsupported-operation",
            "changeset-non-ci-temporary-file-unsupported-operation",
            "azureauth-credprovider",
            ConfigurationScope.CiTemporary,
            new ConfigurationManifestMetadata
            {
                ManifestId = "manifest-non-ci-temporary-file-unsupported-operation",
                OwnerProductId = "azureauth-credprovider",
                EntrySelector = "yarn.adapter",
                ProductVersion = "0.0.0-test",
            },
            [
                new ConfigurationChange
                {
                    Operation = ConfigurationChangeOperation.InstallAdapter,
                    TargetKind = ConfigurationTargetKind.Yarnrc,
                    TargetPathOrName = "/config/non-ci-temporary-file-target/.yarnrc.yml",
                    Key = "nodeLinker",
                    Value = null,
                    RequiresOwnershipRecord = true,
                    PreserveDeclarationsAndComments = true,
                },
            ],
            temporaryContainer: CreateTemporaryHomeContainer(
                "/config/non-ci-temporary-file-target"
            ),
            declarationPreservation:
                ConfigurationDeclarationPreservation.CompleteMergedTemporaryConfig
        );

        var exception = await Assert.ThrowsAsync<NotSupportedException>(async () =>
            await manager.ApplyAsync(plan, TestContext.Current.CancellationToken)
        );

        Assert.Contains(
            "supports only CI temporary file targets",
            exception.Message,
            StringComparison.Ordinal
        );
        Assert.DoesNotContain("operations only", exception.Message, StringComparison.Ordinal);
        Assert.Empty(fileSystem.Files);
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
    public async Task DryRunAsyncDoesNotAcquireLifecycleLock()
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
        fileSystem.Calls.Clear();

        ConfigurationPlanResult result = await manager.DryRunAsync(
            plan,
            TestContext.Current.CancellationToken
        );

        Assert.Equal(ConfigurationPlanState.Planned, result.State);
        AssertNoFilesystemMutationOrLockCalls(fileSystem.Calls);
        AssertNoLifecycleLockWasAttempted(fileSystem.Calls, lifecycleLockPath);
        Assert.False(fileSystem.FileExists(targetPath));
        Assert.False(fileSystem.FileExists(manifestPath));
    }

    [Fact]
    public async Task FilesystemBackedDryRunDoesNotCreateLifecycleLockDirectory()
    {
        var fileSystem = new InMemoryFileSystem(InMemoryPathSemantics.Posix);
        const string targetPath = "/config/dry-run-no-lock-mutation/owned.txt";
        const string manifestPath = "/state/dry-run-no-lock-mutation-manifest.json";
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
        fileSystem.Calls.Clear();

        ConfigurationPlanResult result = await manager.DryRunAsync(
            plan,
            TestContext.Current.CancellationToken
        );

        Assert.Equal(ConfigurationPlanState.Planned, result.State);
        AssertNoFilesystemMutationOrLockCalls(fileSystem.Calls);
        AssertNoLifecycleLockWasAttempted(fileSystem.Calls, lifecycleLockPath);
        Assert.DoesNotContain(
            fileSystem.Directories,
            directory =>
                directory.Contains(
                    ".azureauth-credprovider.lifecycle-locks",
                    StringComparison.Ordinal
                )
        );
        Assert.False(fileSystem.FileExists(targetPath));
        Assert.False(fileSystem.FileExists(manifestPath));
    }

    [Fact]
    public async Task FilesystemBackedDryRunDoesNotMutateExistingLifecycleLockDirectory()
    {
        var fileSystem = new InMemoryFileSystem(InMemoryPathSemantics.Posix);
        const string targetPath = "/config/dry-run-existing-lock-directory/owned.txt";
        const string manifestPath = "/state/dry-run-existing-lock-directory-manifest.json";
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
        fileSystem.CreateDirectory(lifecycleLockPath);
        fileSystem.Calls.Clear();

        ConfigurationPlanResult result = await manager.DryRunAsync(
            plan,
            TestContext.Current.CancellationToken
        );

        Assert.Equal(ConfigurationPlanState.Planned, result.State);
        Assert.True(fileSystem.DirectoryExists(lifecycleLockPath));
        AssertNoFilesystemMutationOrLockCalls(fileSystem.Calls);
        AssertNoLifecycleLockWasAttempted(fileSystem.Calls, lifecycleLockPath);
        Assert.DoesNotContain(
            fileSystem.Files.Keys,
            path => path.StartsWith(lifecycleLockPath + "/", StringComparison.Ordinal)
        );
        Assert.False(fileSystem.FileExists(targetPath));
        Assert.False(fileSystem.FileExists(manifestPath));
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
        AssertNoFilesystemMutationOrLockCalls(fileSystem.Calls);
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
        Assert.IsType<InvalidOperationException>(interleavedApplyException);
        Assert.Contains(
            "reentrant execution",
            interleavedApplyException.Message,
            StringComparison.Ordinal
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

    private sealed class RecordingPhysicalTargetWriterDispatcher(IFileSystem? fileSystem = null)
        : IConfigurationPhysicalTargetWriterDispatcher
    {
        private readonly ConfigurationPhysicalTargetWriterDispatcher? builtInDispatcher =
            fileSystem is null ? null : new ConfigurationPhysicalTargetWriterDispatcher(fileSystem);

        public List<ConfigurationPhysicalTargetWriterRequest> Requests { get; } = [];

        public ValueTask Dispatch(
            ConfigurationPhysicalTargetWriterRequest request,
            CancellationToken cancellationToken
        )
        {
            cancellationToken.ThrowIfCancellationRequested();
            Requests.Add(request);
            if (builtInDispatcher is not null)
            {
                return builtInDispatcher.Dispatch(request, cancellationToken);
            }

            return ValueTask.CompletedTask;
        }
    }

    private sealed class CallbackPhysicalTargetWriterDispatcher(
        Func<ConfigurationPhysicalTargetWriterRequest, CancellationToken, ValueTask> callback
    ) : IConfigurationPhysicalTargetWriterDispatcher,
        IConfigurationPhysicalTargetWriterDispatcherPreclaimPolicy
    {
        public List<ConfigurationPhysicalTargetWriterRequest> Requests { get; } = [];

        public bool RejectSecretGitConfigValueWritesBeforeManifestPreclaim => false;

        public ValueTask Dispatch(
            ConfigurationPhysicalTargetWriterRequest request,
            CancellationToken cancellationToken
        )
        {
            cancellationToken.ThrowIfCancellationRequested();
            Requests.Add(request);
            return callback(request, cancellationToken);
        }
    }

    private static void AssertPhase4DRollbackConflict(InvalidOperationException exception)
    {
        Assert.Contains("rollback failed", exception.Message, StringComparison.OrdinalIgnoreCase);
        Assert.NotNull(exception.InnerException);
        Exception innerException = exception.InnerException!;
        Assert.Contains(
            "Configuration conflict",
            innerException.Message,
            StringComparison.OrdinalIgnoreCase
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

    private static async Task<string> CreateDryRunManifestJsonAsync(ConfigurationChangePlan plan) =>
        RawOwnershipManifestJson(await CreateDryRunManifestAsync(plan));

    private static async Task<ConfigurationOwnershipManifest> CreateDryRunManifestAsync(
        ConfigurationChangePlan plan
    )
    {
        await Task.Yield();
        ConfigurationPlannedOperation[] plannedOperations =
            ConfigurationPlanProjector.CreatePlannedOperations(plan);
        ConfigurationOwnershipManifest manifest =
            ConfigurationPlanProjector.CreateOwnershipManifest(plan, plannedOperations);
        ConfigurationOwnershipManifestPolicy.EnsureValid(manifest);
        return manifest;
    }

    private static async Task<string> CreatePhase4DPhysicalManifestJsonForTestAsync(
        ConfigurationChangePlan plan
    ) => RawOwnershipManifestJson(await CreatePhase4DPhysicalManifestForTestAsync(plan));

    private static async Task<ConfigurationOwnershipManifest>
        CreatePhase4DPhysicalManifestForTestAsync(ConfigurationChangePlan plan)
    {
        ConfigurationOwnershipManifest? manifest = null;
        var entries = new List<ConfigurationOwnershipManifestEntry>();
        foreach (ConfigurationChange change in plan.Changes)
        {
            ConfigurationOwnershipManifest singleChangeManifest = await CreateDryRunManifestAsync(
                plan with { Changes = [change] }
            );
            manifest ??= singleChangeManifest;
            ConfigurationOwnershipManifestEntry entry = Assert.Single(
                singleChangeManifest.Entries
            );
            entries.Add(entry with { Sequence = entries.Count + 1 });
        }

        if (manifest is null)
        {
            return await CreateDryRunManifestAsync(plan);
        }

        return manifest with
        {
            ContainsCredentialMaterial =
                manifest.ContainsCredentialMaterial || entries.Any(entry => entry.IsSecretValue),
            Entries = entries.ToArray(),
        };
    }

    private static async Task<ConfigurationOwnershipManifest>
        CreateGenericFileAndRetainedGitConfigManifestForTestAsync(
        ConfigurationChangePlan genericPlan,
        string gitConfigPath,
        string gitConfigHelperValue
    )
    {
        ConfigurationOwnershipManifest genericManifest = await CreateDryRunManifestAsync(
            genericPlan
        );
        ConfigurationOwnershipManifestEntry genericEntry = Assert.Single(
            genericManifest.Entries
        );
        ConfigurationOwnershipManifest gitConfigManifest =
            await CreatePhase4DPhysicalManifestForTestAsync(
                CreateGitConfigCredentialHelperPlan(gitConfigPath, gitConfigHelperValue)
            );
        ConfigurationOwnershipManifestEntry gitConfigEntry = Assert.Single(
            gitConfigManifest.Entries
        );
        return genericManifest with
        {
            ContainsCredentialMaterial =
                genericManifest.ContainsCredentialMaterial || gitConfigEntry.IsSecretValue,
            Entries =
            [
                genericEntry,
                gitConfigEntry with
                {
                    Sequence = 2,
                },
            ],
        };
    }

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

    private static void AssertNoLifecycleLockWasAttempted(
        IEnumerable<FileSystemCall> calls,
        string lifecycleLockPath
    )
    {
        Assert.DoesNotContain(
            calls,
            call =>
                string.Equals(
                    call.Operation,
                    nameof(IFileSystemMutationLock.AcquireMutationLock),
                    StringComparison.Ordinal
                )
                && string.Equals(call.Path, lifecycleLockPath, StringComparison.Ordinal)
        );
    }

    private static async Task AssertPhysicalTargetOwnershipManifestCollisionRejectedAsync(
        string methodName,
        ConfigurationTargetKind targetKind,
        string collidingTargetPath,
        string manifestPath
    )
    {
        var fileSystem = new InMemoryFileSystem(InMemoryPathSemantics.Posix);
        var manager = new ConfigurationManager(fileSystem, manifestPath);
        ConfigurationChangePlan plan = CreatePhysicalTargetPlan(targetKind, collidingTargetPath);

        ConfigurationPlanValidationResult validationResult = manager.ValidatePlan(plan);

        Assert.False(validationResult.IsValid);
        Assert.NotNull(validationResult.Violation);
        Assert.Contains(
            "ownership manifest path",
            validationResult.Violation,
            StringComparison.Ordinal
        );

        if (methodName == nameof(IConfigurationManager.DryRunAsync))
        {
            var exception = await Assert.ThrowsAsync<ArgumentException>(async () =>
                await manager.DryRunAsync(plan, TestContext.Current.CancellationToken)
            );
            Assert.Contains("ownership manifest path", exception.Message, StringComparison.Ordinal);
        }

        Assert.False(fileSystem.FileExists(manifestPath));
        Assert.False(fileSystem.FileExists(collidingTargetPath));
        AssertNoFilesystemMutationOrLockCalls(fileSystem.Calls);
    }

    private static void AssertNoFilesystemMutationOrLockCalls(IEnumerable<FileSystemCall> calls)
    {
        string[] forbiddenOperations =
        [
            nameof(IFileSystem.WriteAllText),
            nameof(IFileSystem.AtomicWriteAllText),
            nameof(IFileSystem.AtomicWriteAllBytes),
            nameof(IFileSystem.SetUnixFileMode),
            nameof(IFileSystem.CreateDirectory),
            nameof(IFileSystem.DeleteFile),
            nameof(IFileSystem.DeleteDirectory),
            nameof(IFileSystemMutationLock.AcquireMutationLock),
            nameof(InMemoryFileSystem.AddSymbolicLink),
        ];

        Assert.DoesNotContain(
            calls,
            call => forbiddenOperations.Contains(call.Operation, StringComparer.Ordinal)
        );
    }

    private static void AssertManifestAbsentOrPreparedPreclaim(
        InMemoryFileSystem fileSystem,
        string manifestPath,
        string forbiddenFinalManifestJson
    )
    {
        if (!fileSystem.FileExists(manifestPath))
        {
            return;
        }

        string manifestJson = fileSystem.ReadAllText(manifestPath);
        Assert.NotEqual(forbiddenFinalManifestJson, manifestJson);
        ConfigurationOwnershipManifest manifest =
            ConfigurationOwnershipManifestSerializer.Deserialize(manifestJson);
        Assert.True(
            manifest.SafeMetadata.TryGetValue(
                PhysicalTargetManifestPreclaimMetadataKey,
                out string? preclaimState
            )
        );
        Assert.Equal(PhysicalTargetManifestPreclaimMetadataValue, preclaimState);
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

    private static ConfigurationChangePlan CreatePhysicalTargetPlan(
        ConfigurationTargetKind targetKind,
        string targetPath
    ) =>
        ConfigurationChangePlanPolicy.Create(
            $"plan-{targetKind}-physical-target",
            $"changeset-{targetKind}-physical-target",
            "azureauth-credprovider",
            ConfigurationScope.User,
            CreateManifest() with
            {
                ManifestId = $"manifest-{targetKind}-physical-target",
                EntrySelector = $"{targetKind}.physical-target",
            },
            [
                new ConfigurationChange
                {
                    Operation = ConfigurationChangeOperation.Set,
                    TargetKind = targetKind,
                    TargetPathOrName = targetPath,
                    Key = CreatePhysicalTargetKey(targetKind),
                    Value = "planned-value",
                    RequiresOwnershipRecord = true,
                    PreserveDeclarationsAndComments = true,
                },
            ]
        );

    private static ConfigurationChangePlan CreateGitConfigCredentialHelperPlan(
        string targetPath,
        string helperValue
    ) =>
        CreatePhysicalTargetPlan(ConfigurationTargetKind.GitConfig, targetPath) with
        {
            Changes =
            [
                CreatePhysicalTargetChange(ConfigurationTargetKind.GitConfig, targetPath) with
                {
                    Key = "credential.helper",
                    Value = helperValue,
                },
            ],
        };

    private static ConfigurationChange CreatePhysicalTargetChange(
        ConfigurationTargetKind targetKind,
        string targetPath
    ) =>
        new()
        {
            Operation = ConfigurationChangeOperation.Set,
            TargetKind = targetKind,
            TargetPathOrName = targetPath,
            Key = CreatePhysicalTargetKey(targetKind),
            Value = "planned-value",
            RequiresOwnershipRecord = true,
            PreserveDeclarationsAndComments = true,
        };

    private static ConfigurationOwnershipManifest CreateUnsupportedRetainedNonCiPhysicalManifest(
        ConfigurationChangePlan plan,
        ConfigurationTargetKind unsupportedTargetKind,
        string unsupportedTargetPath
    ) =>
        new()
        {
            ManifestId = plan.Manifest.ManifestId,
            PlanId = "existing-unsupported-retained-physical-plan",
            ChangeSetId = "existing-unsupported-retained-physical-changeset",
            OwnerProductId = plan.OwnerProductId,
            Scope = plan.Scope,
            EntrySelector = plan.Manifest.EntrySelector,
            ProductVersion = plan.Manifest.ProductVersion,
            Entries =
            [
                CreateUnsupportedRetainedNonCiPhysicalManifestEntry(
                    1,
                    unsupportedTargetKind,
                    unsupportedTargetPath
                ),
            ],
        };

    private static ConfigurationOwnershipManifest
        CreateGenericFileAndUnsupportedRetainedNonCiPhysicalManifest(
        ConfigurationChangePlan plan,
        string genericTargetPath,
        string genericValue,
        ConfigurationTargetKind unsupportedTargetKind,
        string unsupportedTargetPath
    ) =>
        new()
        {
            ManifestId = plan.Manifest.ManifestId,
            PlanId = "existing-generic-unsupported-retained-physical-plan",
            ChangeSetId = "existing-generic-unsupported-retained-physical-changeset",
            OwnerProductId = plan.OwnerProductId,
            Scope = plan.Scope,
            EntrySelector = plan.Manifest.EntrySelector,
            ProductVersion = plan.Manifest.ProductVersion,
            Entries =
            [
                new ConfigurationOwnershipManifestEntry
                {
                    Sequence = 1,
                    Operation = ConfigurationChangeOperation.Set,
                    TargetKind = ConfigurationTargetKind.CiTemporaryFile,
                    TargetPathOrName = genericTargetPath,
                    Key = "file",
                    PreserveDeclarationsAndComments = false,
                    HasPlannedValue = true,
                    IsSecretValue = false,
                    PlannedValueSha256 = HashMetadata(genericValue)["sha256:".Length..],
                },
                CreateUnsupportedRetainedNonCiPhysicalManifestEntry(
                    2,
                    unsupportedTargetKind,
                    unsupportedTargetPath
                ),
            ],
        };

    private static ConfigurationOwnershipManifestEntry
        CreateUnsupportedRetainedNonCiPhysicalManifestEntry(
        int sequence,
        ConfigurationTargetKind targetKind,
        string targetPath
    ) =>
        new()
        {
            Sequence = sequence,
            Operation = ConfigurationChangeOperation.Set,
            TargetKind = targetKind,
            TargetPathOrName = targetPath,
            Key = CreatePhysicalTargetKey(targetKind),
            PreserveDeclarationsAndComments = true,
            HasPlannedValue = true,
            IsSecretValue = false,
            PlannedValueSha256 = HashMetadata("retained-value")["sha256:".Length..],
        };

    private static string CreatePhysicalTargetKey(ConfigurationTargetKind targetKind) =>
        targetKind == ConfigurationTargetKind.GitConfig
            ? "credential.helper"
            : "physical-target";

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

    private static ConfigurationChange CreateNpmrcFileChange(string targetPath) =>
        new()
        {
            Operation = ConfigurationChangeOperation.Set,
            TargetKind = ConfigurationTargetKind.Npmrc,
            TargetPathOrName = targetPath,
            Key = "registry",
            Value = "https://registry.npmjs.org/",
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

    private static string CreateGitConfigCredentialHelperContents(string helperValue) =>
        string.Join(
            '\n',
            "[credential]",
            string.Create(CultureInfo.InvariantCulture, $"\thelper = \"{helperValue}\""),
            string.Empty
        );

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

    private static void AssertExceptionAndDataDoNotContainSecret(Exception exception, string secret)
    {
        Assert.DoesNotContain(secret, exception.Message, StringComparison.Ordinal);
        Assert.DoesNotContain(secret, exception.ToString(), StringComparison.Ordinal);
        if (exception.InnerException is { } innerException)
        {
            Assert.DoesNotContain(secret, innerException.Message, StringComparison.Ordinal);
            Assert.DoesNotContain(secret, innerException.ToString(), StringComparison.Ordinal);
        }

        foreach (System.Collections.DictionaryEntry entry in exception.Data)
        {
            Assert.DoesNotContain(
                secret,
                Convert.ToString(entry.Key, CultureInfo.InvariantCulture) ?? string.Empty,
                StringComparison.Ordinal
            );
            Assert.DoesNotContain(
                secret,
                Convert.ToString(entry.Value, CultureInfo.InvariantCulture) ?? string.Empty,
                StringComparison.Ordinal
            );
        }
    }

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

    private sealed class FullPathRemappingFileSystem : IFileSystem
    {
        private readonly InMemoryFileSystem inner;
        private readonly string sourcePath;
        private readonly string mappedFullPath;
        private readonly Exception? sourcePathException;

        public FullPathRemappingFileSystem(
            InMemoryFileSystem inner,
            string sourcePath,
            string mappedFullPath,
            Exception? sourcePathException = null
        )
        {
            this.inner = inner;
            this.sourcePath = sourcePath;
            this.mappedFullPath = mappedFullPath;
            this.sourcePathException = sourcePathException;
        }

        public bool SupportsConditionalFileMutations => inner.SupportsConditionalFileMutations;

        public bool FileExists(string path) => inner.FileExists(path);

        public bool DirectoryExists(string path) => inner.DirectoryExists(path);

        public string GetFullPath(string path)
        {
            if (!string.Equals(path, sourcePath, StringComparison.Ordinal))
            {
                return inner.GetFullPath(path);
            }

            if (sourcePathException is not null)
            {
                throw sourcePathException;
            }

            return mappedFullPath;
        }

        public bool IsPathFullyQualified(string path) => inner.IsPathFullyQualified(path);

        public bool IsSymbolicLink(string path) => inner.IsSymbolicLink(path);

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
