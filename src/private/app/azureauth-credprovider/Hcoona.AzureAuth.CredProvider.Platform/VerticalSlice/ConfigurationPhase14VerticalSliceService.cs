using System.Diagnostics.CodeAnalysis;
using System.Security.Cryptography;
using System.Text;
using Hcoona.AzureAuth.CredProvider.Contracts;
using Hcoona.AzureAuth.CredProvider.Platform.Configuration;
using Hcoona.AzureAuth.CredProvider.Platform.CredentialCore;
using Hcoona.AzureAuth.CredProvider.Platform.FileSystem;

namespace Hcoona.AzureAuth.CredProvider.Platform.VerticalSlice;

public sealed record ConfigurationPhase14VerticalSliceOptions
{
    public string? StateDirectoryPath { get; init; }

    public IFileSystem? FileSystem { get; init; }

    public CredentialCoreService? CredentialCoreService { get; init; }

    public Func<string, string?>? EnvironmentVariableReader { get; init; }
}

public sealed record ConfigurationPhase14ResolvedPaths
{
    public required string StateDirectoryPath { get; init; }

    public required string ManifestDirectoryPath { get; init; }

    public required string OwnershipManifestPath { get; init; }

    public required string NpmUserNpmrcPath { get; init; }

    public required string PnpmUserNpmrcPath { get; init; }

    public required string NpmCiTemporaryNpmrcPath { get; init; }

    public required string PnpmCiTemporaryNpmrcPath { get; init; }

    public required string YarnUserYarnrcPath { get; init; }

    public required string YarnCiTemporaryHomePath { get; init; }
}

public sealed record ConfigurationPhase14PlanResult
{
    public required ConfigurationPhase14ResolvedPaths Paths { get; init; }

    public required IReadOnlyList<ConfigurationPlanResult> PlanResults { get; init; }

    public required bool OwnershipManifestPresent { get; init; }

    public ConfigurationPlanResult PlanResult => PlanResults[^1];

    public int ChangeCount => PlanResults.Sum(static result => result.Changes.Count);
}

public sealed class ConfigurationPhase14VerticalSliceService
{
    private const string ProductId = "azureauth-credprovider";
    private const string ProductVersion = "phase14.2";
    private const string PythonPlanId = "phase14-python-keyring-configure-plan";
    private const string PythonChangeSetId = "phase14-python-keyring-configure-changeset";
    private const string PythonManifestId = "phase14-python-keyring";
    private const string PhysicalTargetKey = "physical-target";
    private const string AzurePipelinesSystemAccessTokenVariable = "SYSTEM_ACCESSTOKEN";
    private const string NpmUserConfigEnvironmentVariable = "NPM_CONFIG_USERCONFIG";
    private const string LowercaseNpmUserConfigEnvironmentVariable = "npm_config_userconfig";

    private static readonly Uri FakeNpmRegistryUrl = new(
        "https://pkgs.dev.azure.com/org/_packaging/feed/npm/registry/"
    );

    private static readonly Uri PythonServiceEndpoint = new("https://dev.azure.com/org");

    private readonly CredentialCoreService credentialCoreService;
    private readonly Func<string, string?> environmentVariableReader;
    private readonly IFileSystem fileSystem;
    private readonly ConfigurationPhase14ResolvedPaths paths;

    public ConfigurationPhase14VerticalSliceService(
        ConfigurationPhase14VerticalSliceOptions? options = null
    )
    {
        options ??= new ConfigurationPhase14VerticalSliceOptions();
        fileSystem = options.FileSystem ?? new SystemFileSystem();
        credentialCoreService = options.CredentialCoreService ?? new CredentialCoreService();
        environmentVariableReader =
            options.EnvironmentVariableReader ?? Environment.GetEnvironmentVariable;
        paths = ResolvePaths(options, fileSystem);
    }

    public ConfigurationPhase14ResolvedPaths Paths => paths;

    public async ValueTask<ConfigurationPhase14PlanResult> ConfigureAsync(
        CredentialEcosystem ecosystem,
        ConfigurationPhase14Scope scope,
        CancellationToken cancellationToken = default
    )
    {
        string ownershipManifestPath = GetOwnershipManifestPath(ecosystem, scope);
        List<ConfigurationPlanResult> planResults = [];
        foreach (ConfigurationChangePlan plan in CreateApplyPlans(ecosystem, scope))
        {
            planResults.Add(
                await CreateManager(ownershipManifestPath).ApplyAsync(
                    AttachPreviousOwnershipManifestHashIfPresent(plan, ownershipManifestPath),
                    cancellationToken
                )
            );
        }

        return CreateResult(planResults, ownershipManifestPath);
    }

    public async ValueTask<ConfigurationPhase14PlanResult> UnconfigureAsync(
        CredentialEcosystem ecosystem,
        ConfigurationPhase14Scope scope,
        CancellationToken cancellationToken = default
    )
    {
        string ownershipManifestPath = GetOwnershipManifestPath(ecosystem, scope);
        if (!TryLoadOwnershipManifest(
                ownershipManifestPath,
                out ConfigurationOwnershipManifest? manifest,
                out string? manifestJson))
        {
            return CreateResult(
                [CreateNoOpPlanResult(ConfigurationPlanOperation.Remove)],
                ownershipManifestPath);
        }

        List<ConfigurationPlanResult> planResults = [];
        foreach (
            ConfigurationChangePlan plan in CreateRemovePlans(
                ecosystem,
                scope,
                manifest,
                manifestJson)
        )
        {
            planResults.Add(
                await CreateManager(ownershipManifestPath).RemoveAsync(
                    AttachPreviousOwnershipManifestHashIfPresent(plan, ownershipManifestPath),
                    cancellationToken)
            );
        }

        return planResults.Count == 0
            ? CreateResult([CreateNoOpPlanResult(ConfigurationPlanOperation.Remove)],
                ownershipManifestPath)
            : CreateResult(planResults, ownershipManifestPath);
    }

    private IReadOnlyList<ConfigurationChangePlan> CreateApplyPlans(
        CredentialEcosystem ecosystem,
        ConfigurationPhase14Scope scope
    ) =>
        ecosystem switch
        {
            CredentialEcosystem.Python => CreatePythonPlans(scope),
            CredentialEcosystem.Npm or CredentialEcosystem.Pnpm => [
                CreateNpmCompatiblePlan(
                ecosystem,
                scope
            )],
            CredentialEcosystem.Yarn => [CreateYarnPlan(scope)],
            _ => throw new NotSupportedException(
                "Phase 14.2 configuration orchestration supports Python, npm, pnpm, and Yarn."
            ),
        };

    private IReadOnlyList<ConfigurationChangePlan> CreatePythonPlans(
        ConfigurationPhase14Scope scope)
    {
        if (scope != ConfigurationPhase14Scope.User)
        {
            throw new NotSupportedException(
                "Phase 14.2 Python configuration supports only user scope."
            );
        }

        ConfigurationTargetLayoutProjection backendProjection =
            ConfigurationLayoutProjector.ProjectPythonKeyringBackend(
                CreateCurrentLayoutProjectionContext()
            );
        ConfigurationTargetLayoutProjection shimProjection =
            ConfigurationLayoutProjector.ProjectKeyringShim(CreateCurrentLayoutProjectionContext());
        return
            [
                CreatePythonPlan(
                    "backend",
                    ConfigurationTargetKind.PythonKeyringBackend,
                    backendProjection.TargetPath,
                    CreatePythonBackendManifestValue(shimProjection.TargetPath)
                ),
                CreatePythonPlan(
                    "shim",
                    ConfigurationTargetKind.KeyringShim,
                    shimProjection.TargetPath,
                    CreateKeyringShimValue()
                ),
            ];
    }

    private static ConfigurationChangePlan CreatePythonPlan(
        string suffix,
        ConfigurationTargetKind targetKind,
        string targetPath,
        string value
    )
    {
        return ConfigurationChangePlanPolicy.Create(
            PythonPlanId + "-" + suffix,
            PythonChangeSetId + "-" + suffix,
            ProductId,
            ConfigurationScope.User,
            CreatePythonManifestMetadata(),
            [CreatePythonPhysicalTargetChange(targetKind, targetPath, value)],
            containsCredentialMaterial: false
        );
    }

    private ConfigurationChangePlan CreateNpmCompatiblePlan(
        CredentialEcosystem ecosystem,
        ConfigurationPhase14Scope scope
    )
    {
        var service = new NpmPhase12VerticalSliceService(
            new NpmPhase12VerticalSliceOptions
            {
                FileSystem = fileSystem,
                EnvironmentVariableReader = environmentVariableReader,
                UserNpmrcPath = ecosystem == CredentialEcosystem.Pnpm
                    ? paths.PnpmUserNpmrcPath
                    : paths.NpmUserNpmrcPath,
            }
        );
        NpmPhase12RegistryDeclaration declaration = CreateNpmDeclaration();
        string authToken = GetPackageAuthToken(ecosystem, scope, declaration.ResourceIdentity);
        var request = new NpmPhase12CredentialPlanRequest
        {
            Declaration = declaration,
            AuthToken = authToken,
            Ecosystem = ecosystem,
            TargetNpmrcPath = GetNpmTargetPath(ecosystem, scope),
        };
        return scope == ConfigurationPhase14Scope.CiTemporary
            ? service.CreateCiTemporaryCredentialPlan(request)
            : service.CreateUserCredentialPlan(request);
    }

    private ConfigurationChangePlan CreateYarnPlan(ConfigurationPhase14Scope scope)
    {
        var service = new YarnPhase13VerticalSliceService(
            new YarnPhase13VerticalSliceOptions
            {
                FileSystem = fileSystem,
                EnvironmentVariableReader = environmentVariableReader,
                UserYarnrcPath = paths.YarnUserYarnrcPath,
            }
        );
        YarnPhase13RegistryDeclaration declaration = CreateYarnDeclaration();
        string authToken = GetPackageAuthToken(
            CredentialEcosystem.Yarn,
            scope,
            declaration.ResourceIdentity
        );
        var request = new YarnPhase13CredentialPlanRequest
        {
            Declaration = declaration,
            AuthToken = authToken,
            TargetYarnrcPath = paths.YarnUserYarnrcPath,
            TemporaryHomePath = paths.YarnCiTemporaryHomePath,
        };
        return scope == ConfigurationPhase14Scope.CiTemporary
            ? service.CreateCiTemporaryCredentialPlan(request)
            : service.CreateUserCredentialPlan(request);
    }

    private string GetPackageAuthToken(
        CredentialEcosystem ecosystem,
        ConfigurationPhase14Scope scope,
        CanonicalResourceIdentity resource
    )
    {
        bool ciTemporary = scope == ConfigurationPhase14Scope.CiTemporary;
        if (
            ciTemporary
            && string.IsNullOrWhiteSpace(environmentVariableReader(
                AzurePipelinesSystemAccessTokenVariable
            ))
        )
        {
            throw new InvalidOperationException(
                "Azure Pipelines system access token is unavailable in the environment."
            );
        }

        CredentialResult result = credentialCoreService.Execute(
            new CredentialRequest
            {
                Ecosystem = ecosystem,
                Operation = CredentialOperation.Get,
                Resource = resource,
                ServiceIdentity = "default",
                RequestedAudience = TokenAudience.AzureArtifacts,
                CredentialKind = CredentialKind.NpmAuthToken,
                IdentityFlow = ciTemporary
                    ? IdentityFlow.AzurePipelinesSystemAccessToken
                    : IdentityFlow.DeviceCode,
                InteractivePolicy = ciTemporary
                    ? InteractivePolicy.Never
                    : InteractivePolicy.UserAllowed,
                CachePolicy = ciTemporary
                    ? CachePolicyMode.NonPersistentCi
                    : CachePolicyMode.ProductPersistentCacheDisabled,
                CiContext = ciTemporary
                    ? new CiContext
                    {
                        ExplicitCiMode = true,
                        Provider = CiProviderNames.AzurePipelines,
                        HasAzurePipelinesSystemAccessToken = true,
                        AllowsPersistentWrites = false,
                    }
                    : null,
            }
        );
        return result.Status == CredentialResultStatus.Success
            && !string.IsNullOrWhiteSpace(result.BearerToken)
            ? result.BearerToken
            : throw new InvalidOperationException("Failed to create a fake package auth token.");
    }

    private string GetNpmTargetPath(CredentialEcosystem ecosystem, ConfigurationPhase14Scope scope)
    {
        return (ecosystem, scope) switch
        {
            (CredentialEcosystem.Npm, ConfigurationPhase14Scope.User) => paths.NpmUserNpmrcPath,
            (CredentialEcosystem.Pnpm, ConfigurationPhase14Scope.User) => paths.PnpmUserNpmrcPath,
            (CredentialEcosystem.Npm, ConfigurationPhase14Scope.CiTemporary) =>
                paths.NpmCiTemporaryNpmrcPath,
            (CredentialEcosystem.Pnpm, ConfigurationPhase14Scope.CiTemporary) =>
                paths.PnpmCiTemporaryNpmrcPath,
            _ => throw new NotSupportedException("Unsupported npm-compatible configuration scope."),
        };
    }

    private static Dictionary<string, string> CreateNpmrcActivationSetVariables(
        string platform,
        string targetNpmrcPath
    ) =>
        string.Equals(platform, "windows", StringComparison.Ordinal)
            ? new Dictionary<string, string>(StringComparer.Ordinal)
            {
                [NpmUserConfigEnvironmentVariable] = targetNpmrcPath,
            }
            : new Dictionary<string, string>(StringComparer.Ordinal)
            {
                [NpmUserConfigEnvironmentVariable] = targetNpmrcPath,
                [LowercaseNpmUserConfigEnvironmentVariable] = targetNpmrcPath,
            };

    private static ConfigurationActivationEnvironment CreateTemporaryHomeActivationEnvironment(
        string temporaryHomePath
    )
    {
        if (IsWindowsDrivePath(temporaryHomePath) || IsWindowsUncPath(temporaryHomePath))
        {
            return new ConfigurationActivationEnvironment
            {
                Platform = "windows",
                SetVariables = new Dictionary<string, string>(StringComparer.Ordinal)
                {
                    ["USERPROFILE"] = temporaryHomePath,
                    ["HOME"] = temporaryHomePath,
                },
                ClearVariables = ["HOMEDRIVE", "HOMEPATH"],
            };
        }

        return new ConfigurationActivationEnvironment
        {
            Platform = "posix",
            SetVariables = new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["HOME"] = temporaryHomePath,
            },
            ClearVariables = [],
        };
    }

    private static string InferActivationPlatform(string targetNpmrcPath)
    {
        if (targetNpmrcPath.StartsWith('/'))
        {
            return "posix";
        }

        if (IsWindowsDrivePath(targetNpmrcPath) || IsWindowsUncPath(targetNpmrcPath))
        {
            return "windows";
        }

        return OperatingSystem.IsWindows() ? "windows" : "posix";
    }

    private static bool IsWindowsDrivePath(string path) =>
        path.Length >= 3
        && path[1] == ':'
        && (path[2] == '\\' || path[2] == '/')
        && char.IsAsciiLetter(path[0]);

    private static bool IsWindowsUncPath(string path) =>
        path.StartsWith(@"\\", StringComparison.Ordinal)
        || path.StartsWith("//", StringComparison.Ordinal);

    private ConfigurationChangePlan[] CreateRemovePlans(
        CredentialEcosystem ecosystem,
        ConfigurationPhase14Scope scope,
        ConfigurationOwnershipManifest manifest,
        string manifestJson
    )
    {
        string ecosystemName = ToContractEcosystemName(ecosystem);
        ConfigurationOwnershipManifestEntry[] entries = manifest
            .Entries.Where(entry => EntryMatchesEcosystem(entry, ecosystem))
            .OrderBy(entry => entry.Sequence)
            .ToArray();

        return entries
            .GroupBy(static entry => entry.TargetKind)
            .Select(group =>
                CreateRemovePlan(
                    ecosystem,
                    ecosystemName,
                    scope,
                    manifest,
                    manifestJson,
                    group.ToArray())
            )
            .ToArray();
    }

    private ConfigurationChangePlan CreateRemovePlan(
        CredentialEcosystem ecosystem,
        string ecosystemName,
        ConfigurationPhase14Scope scope,
        ConfigurationOwnershipManifest manifest,
        string manifestJson,
        IReadOnlyList<ConfigurationOwnershipManifestEntry> entries
    )
    {
        return ConfigurationChangePlanPolicy.Create(
            "phase14-" + ecosystemName + "-unconfigure-plan",
            "phase14-" + ecosystemName + "-unconfigure-changeset",
            ProductId,
            scope == ConfigurationPhase14Scope.CiTemporary
                ? ConfigurationScope.CiTemporary
                : ConfigurationScope.User,
            new ConfigurationManifestMetadata
            {
                ManifestId = manifest.ManifestId,
                OwnerProductId = manifest.OwnerProductId,
                EntrySelector = manifest.EntrySelector,
                ResourceIdentity = manifest.ResourceIdentity,
                ProductVersion = manifest.ProductVersion,
                PreviousOwnedEntryHash = ComputeSha256Metadata(manifestJson),
                SafeMetadata = manifest.SafeMetadata,
            },
            entries.Select(CreateRemoveChange).ToArray(),
            temporaryContainer: CreateRemoveTemporaryContainer(ecosystem, scope),
            declarationPreservation: scope == ConfigurationPhase14Scope.CiTemporary
                ? ConfigurationDeclarationPreservation.AuthOnlyWhenDeclarationsRemainVisible
                : ConfigurationDeclarationPreservation.NotApplicable,
            containsCredentialMaterial: manifest.ContainsCredentialMaterial
        );
    }

    private ConfigurationTemporaryContainer? CreateRemoveTemporaryContainer(
        CredentialEcosystem ecosystem,
        ConfigurationPhase14Scope scope
    )
    {
        if (scope != ConfigurationPhase14Scope.CiTemporary)
        {
            return null;
        }

        return ecosystem switch
        {
            CredentialEcosystem.Npm or CredentialEcosystem.Pnpm => CreateNpmrcTemporaryContainer(
                GetNpmTargetPath(ecosystem, scope)
            ),
            CredentialEcosystem.Yarn => new ConfigurationTemporaryContainer
            {
                Kind = ConfigurationTemporaryContainerKind.TemporaryHome,
                ProductOwnedPath = paths.YarnCiTemporaryHomePath,
                ActivationEnvironment = CreateTemporaryHomeActivationEnvironment(
                    paths.YarnCiTemporaryHomePath
                ),
            },
            _ => null,
        };
    }

    private static ConfigurationTemporaryContainer CreateNpmrcTemporaryContainer(
        string targetNpmrcPath
    )
    {
        string platform = InferActivationPlatform(targetNpmrcPath);
        return new ConfigurationTemporaryContainer
        {
            Kind = ConfigurationTemporaryContainerKind.NpmrcFile,
            ProductOwnedPath = targetNpmrcPath,
            ActivationEnvironment = new ConfigurationActivationEnvironment
            {
                Platform = platform,
                SetVariables = CreateNpmrcActivationSetVariables(platform, targetNpmrcPath),
                ClearVariables = [],
            },
        };
    }

    private static ConfigurationChange CreateRemoveChange(
        ConfigurationOwnershipManifestEntry entry
    ) =>
        new()
        {
            Operation = ConfigurationChangeOperation.Remove,
            TargetKind = entry.TargetKind,
            TargetPathOrName = entry.TargetPathOrName,
            Key = entry.Key,
            Value = null,
            RequiresOwnershipRecord = true,
            PreserveDeclarationsAndComments = entry.PreserveDeclarationsAndComments,
            PreviousOwnedEntryMetadata =
                entry.PreviousOwnedEntryMetadata
                    ?? entry.PlannedValueSha256
                    ?? "previous-secret-owned-entry",
        };

    private static bool EntryMatchesEcosystem(
        ConfigurationOwnershipManifestEntry entry,
        CredentialEcosystem ecosystem
    )
    {
        return ecosystem switch
        {
            CredentialEcosystem.Python => entry.TargetKind
                is ConfigurationTargetKind.PythonKeyringBackend
                    or ConfigurationTargetKind.KeyringShim,
            CredentialEcosystem.Npm or CredentialEcosystem.Pnpm =>
                entry.TargetKind == ConfigurationTargetKind.Npmrc,
            CredentialEcosystem.Yarn => entry.TargetKind == ConfigurationTargetKind.Yarnrc,
            _ => false,
        };
    }

    private ConfigurationPhase14PlanResult CreateResult(
        IReadOnlyList<ConfigurationPlanResult> planResults,
        string ownershipManifestPath
    )
    {
        return
        new()
        {
            Paths = paths,
            PlanResults = planResults,
            OwnershipManifestPresent = fileSystem.FileExists(ownershipManifestPath),
        };
    }

    private static ConfigurationPlanResult CreateNoOpPlanResult(
        ConfigurationPlanOperation operation) =>
        new()
        {
            Plan = new ConfigurationDryRunPlan
            {
                ContractMajor = ContractVersions.ConfigurationChangePlanMajor,
                PlanId = "phase14-configuration-noop",
                ChangeSetId = "phase14-configuration-noop",
                OwnerProductId = ProductId,
                Scope = ConfigurationScope.User,
                AtomicityPolicy = ConfigurationAtomicityPolicy.AtomicChangeSetRequired,
                RollbackPolicy = ConfigurationRollbackPolicy.Required,
                State = ConfigurationPlanState.Planned,
                ManifestCommitPolicy =
                    ConfigurationManifestCommitPolicy.CommitAfterDurableChanges,
                Manifest = CreatePythonManifestMetadata(),
                DeclarationPreservation = ConfigurationDeclarationPreservation.NotApplicable,
                ContainsCredentialMaterial = false,
            },
            Operation = operation,
            State = ConfigurationPlanState.Applied,
        };

    private ConfigurationManager CreateManager(string ownershipManifestPath) =>
        new(
            fileSystem,
            ownershipManifestPath,
            new ConfigurationPhysicalTargetWriterDispatcher(fileSystem)
        );

    private ConfigurationChangePlan AttachPreviousOwnershipManifestHashIfPresent(
        ConfigurationChangePlan plan,
        string ownershipManifestPath
    )
    {
        if (!fileSystem.FileExists(ownershipManifestPath))
        {
            return plan;
        }

        string manifestJson = fileSystem.ReadAllText(ownershipManifestPath);
        return plan with
        {
            Manifest = plan.Manifest with
            {
                PreviousOwnedEntryHash = ComputeSha256Metadata(manifestJson),
            },
        };
    }

    private ConfigurationLayoutProjectionContext CreateCurrentLayoutProjectionContext() =>
        new()
        {
            Platform = OperatingSystem.IsWindows()
                ? ConfigurationLayoutPlatform.Windows
                : OperatingSystem.IsMacOS()
                    ? ConfigurationLayoutPlatform.MacOs
                    : ConfigurationLayoutPlatform.Linux,
            HomeDirectory = GetHomeDirectory(),
            LocalAppDataDirectory = GetLocalAppDataDirectory(),
            XdgDataHomeDirectory = environmentVariableReader("XDG_DATA_HOME"),
            XdgConfigHomeDirectory = environmentVariableReader("XDG_CONFIG_HOME"),
            FileExists = fileSystem.FileExists,
        };

    private static ConfigurationChange CreatePythonPhysicalTargetChange(
        ConfigurationTargetKind targetKind,
        string targetPath,
        string value
    ) =>
        new()
        {
            Operation = ConfigurationChangeOperation.Set,
            TargetKind = targetKind,
            TargetPathOrName = targetPath,
            Key = PhysicalTargetKey,
            Value = value,
            IsSecretValue = false,
            RequiresOwnershipRecord = true,
        };

    private static ConfigurationManifestMetadata CreatePythonManifestMetadata() =>
        new()
        {
            ManifestId = PythonManifestId,
            OwnerProductId = ProductId,
            EntrySelector = "python.keyring",
            ResourceIdentity = CanonicalResourceIdentity.Create(
                "dev.azure.com",
                "org",
                PythonServiceEndpoint
            ),
            ProductVersion = ProductVersion,
            SafeMetadata = new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["ecosystem"] = "python",
            },
        };

    private static string CreatePythonBackendManifestValue(string shimPath) =>
        "azureauth-credprovider python-keyring-backend\n"
        + "phase=14.2\n"
        + "helper="
        + shimPath
        + "\n";

    private static string CreateKeyringShimValue() =>
        OperatingSystem.IsWindows()
            ? "azureauth-credprovider keyring shim phase=14.2\r\n"
            : "#!/usr/bin/env sh\nexec azureauth-credprovider keyring-helper-v2 \"$@\"\n";

    private NpmPhase12RegistryDeclaration CreateNpmDeclaration()
    {
        if (
            !NpmPhase12VerticalSliceService.TryCreateAzureArtifactsNpmResourceIdentity(
                FakeNpmRegistryUrl,
                out CanonicalResourceIdentity? resource
            )
        )
        {
            throw new InvalidOperationException("The fake npm registry URL is not canonical.");
        }

        return new NpmPhase12RegistryDeclaration
        {
            SourcePath = fileSystem.GetFullPath(
                Path.Combine(paths.StateDirectoryPath, "npm", "fake-registry.npmrc")
            ),
            Key = "registry",
            RegistryUrl = FakeNpmRegistryUrl,
            ResourceIdentity = resource,
            AuthSelectors = NpmCompatibleAuthSelectorPolicy.Create(resource),
        };
    }

    private YarnPhase13RegistryDeclaration CreateYarnDeclaration()
    {
        if (
            !NpmPhase12VerticalSliceService.TryCreateAzureArtifactsNpmResourceIdentity(
                FakeNpmRegistryUrl,
                out CanonicalResourceIdentity? resource
            )
        )
        {
            throw new InvalidOperationException("The fake Yarn registry URL is not canonical.");
        }

        return new YarnPhase13RegistryDeclaration
        {
            SourcePath = fileSystem.GetFullPath(
                Path.Combine(paths.StateDirectoryPath, "yarn", "fake-registry.yarnrc.yml")
            ),
            Key = "npmRegistryServer",
            RegistryUrl = FakeNpmRegistryUrl,
            ResourceIdentity = resource,
            NpmRegistriesKey = FakeNpmRegistryUrl.AbsoluteUri,
        };
    }

    private static ConfigurationPhase14ResolvedPaths ResolvePaths(
        ConfigurationPhase14VerticalSliceOptions options,
        IFileSystem fileSystem
    )
    {
        string stateDirectoryPath = fileSystem.GetFullPath(
            options.StateDirectoryPath ?? GetDefaultStateDirectoryPath()
        );
        return new ConfigurationPhase14ResolvedPaths
        {
            StateDirectoryPath = stateDirectoryPath,
            ManifestDirectoryPath = fileSystem.GetFullPath(
                Path.Combine(stateDirectoryPath, "manifests")
            ),
            OwnershipManifestPath = fileSystem.GetFullPath(
                Path.Combine(
                    stateDirectoryPath,
                    "manifests",
                    "python-user-ownership-manifest.json"
                )
            ),
            NpmUserNpmrcPath = fileSystem.GetFullPath(
                Path.Combine(stateDirectoryPath, "npm", "user.npmrc")
            ),
            PnpmUserNpmrcPath = fileSystem.GetFullPath(
                Path.Combine(stateDirectoryPath, "pnpm", "user.npmrc")
            ),
            NpmCiTemporaryNpmrcPath = fileSystem.GetFullPath(
                Path.Combine(stateDirectoryPath, "npm", "ci", "userconfig.npmrc")
            ),
            PnpmCiTemporaryNpmrcPath = fileSystem.GetFullPath(
                Path.Combine(stateDirectoryPath, "pnpm", "ci", "userconfig.npmrc")
            ),
            YarnUserYarnrcPath = fileSystem.GetFullPath(
                Path.Combine(stateDirectoryPath, "yarn", "user.yarnrc.yml")
            ),
            YarnCiTemporaryHomePath = fileSystem.GetFullPath(
                Path.Combine(stateDirectoryPath, "yarn", "ci", "home")
            ),
        };
    }

    private static string GetDefaultStateDirectoryPath()
    {
        string userProfile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        if (!string.IsNullOrWhiteSpace(userProfile))
        {
            return Path.Combine(userProfile, "." + ProductId, "phase14.2");
        }

        string localApplicationData = Environment.GetFolderPath(
            Environment.SpecialFolder.LocalApplicationData
        );
        if (!string.IsNullOrWhiteSpace(localApplicationData))
        {
            return Path.Combine(localApplicationData, ProductId, "phase14.2");
        }

        return Path.Combine(Path.GetTempPath(), ProductId, "phase14.2");
    }

    private static string GetHomeDirectory()
    {
        string profileDirectory = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        if (!string.IsNullOrWhiteSpace(profileDirectory))
        {
            return profileDirectory;
        }

        string environmentHome = Environment.GetEnvironmentVariable("HOME") ?? string.Empty;
        return string.IsNullOrWhiteSpace(environmentHome) ? Path.GetTempPath() : environmentHome;
    }

    private static string? GetLocalAppDataDirectory()
    {
        string localApplicationData = Environment.GetFolderPath(
            Environment.SpecialFolder.LocalApplicationData
        );
        return string.IsNullOrWhiteSpace(localApplicationData) ? null : localApplicationData;
    }

    private string GetOwnershipManifestPath(
        CredentialEcosystem ecosystem,
        ConfigurationPhase14Scope scope
    )
    {
        string fileName = (ecosystem, scope) switch
        {
            (CredentialEcosystem.Python, ConfigurationPhase14Scope.User) =>
                "python-user-ownership-manifest.json",
            (CredentialEcosystem.Npm, ConfigurationPhase14Scope.User) =>
                "npm-user-ownership-manifest.json",
            (CredentialEcosystem.Npm, ConfigurationPhase14Scope.CiTemporary) =>
                "npm-ci-temporary-ownership-manifest.json",
            (CredentialEcosystem.Pnpm, ConfigurationPhase14Scope.User) =>
                "pnpm-user-ownership-manifest.json",
            (CredentialEcosystem.Pnpm, ConfigurationPhase14Scope.CiTemporary) =>
                "pnpm-ci-temporary-ownership-manifest.json",
            (CredentialEcosystem.Yarn, ConfigurationPhase14Scope.User) =>
                "yarn-user-ownership-manifest.json",
            (CredentialEcosystem.Yarn, ConfigurationPhase14Scope.CiTemporary) =>
                "yarn-ci-temporary-ownership-manifest.json",
            _ => throw new NotSupportedException(
                "Phase 14.2 configuration orchestration supports Python user scope and "
                + "npm, pnpm, and Yarn user or CI temporary scopes."
            ),
        };

        return fileSystem.GetFullPath(Path.Combine(paths.ManifestDirectoryPath, fileName));
    }

    private bool TryLoadOwnershipManifest(
        string ownershipManifestPath,
        [NotNullWhen(true)] out ConfigurationOwnershipManifest? manifest,
        [NotNullWhen(true)] out string? manifestJson
    )
    {
        manifest = null;
        manifestJson = null;
        if (!fileSystem.FileExists(ownershipManifestPath))
        {
            return false;
        }

        manifestJson = fileSystem.ReadAllText(ownershipManifestPath);
        manifest = ConfigurationOwnershipManifestSerializer.Deserialize(manifestJson);
        return true;
    }

    private static string ToContractEcosystemName(CredentialEcosystem ecosystem) =>
        ecosystem switch
        {
            CredentialEcosystem.Python => "python",
            CredentialEcosystem.Npm => "npm",
            CredentialEcosystem.Pnpm => "pnpm",
            CredentialEcosystem.Yarn => "yarn",
            _ => throw new ArgumentOutOfRangeException(nameof(ecosystem), ecosystem, null),
        };

    private static string ComputeSha256Metadata(string value) => "sha256:" + ComputeSha256(value);

    private static string ComputeSha256(string value)
    {
        byte[] hash = SHA256.HashData(Encoding.UTF8.GetBytes(value));
        return Convert.ToHexString(hash).ToLowerInvariant();
    }
}

public enum ConfigurationPhase14Scope
{
    User,
    CiTemporary,
}
