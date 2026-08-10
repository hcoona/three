using System.Security.Cryptography;
using System.Text;
using Hcoona.AzureAuth.CredProvider.Contracts;
using Hcoona.AzureAuth.CredProvider.Platform.AdapterHost;
using Hcoona.AzureAuth.CredProvider.Platform.Composition;
using Hcoona.AzureAuth.CredProvider.Platform.Configuration;
using Hcoona.AzureAuth.CredProvider.Platform.FileSystem;
using Newtonsoft.Json.Linq;
using NuGet.Protocol.Plugins;

namespace Hcoona.AzureAuth.CredProvider.Platform.VerticalSlice;

public sealed record NuGetPhase10VerticalSliceOptions
{
    public string? StateDirectoryPath { get; init; }

    public string? ApplicationPayloadRootPath { get; init; }

    public IFileSystem? FileSystem { get; init; }

    public Func<string, string?>? EnvironmentVariableReader { get; init; }

    public BoundedCredentialAcquisitionAdapter? CredentialAcquisition { get; init; }
}

public sealed record NuGetPhase10VerticalSliceResolvedPaths
{
    public required string StateDirectoryPath { get; init; }

    public required string OwnershipManifestPath { get; init; }

    public required string PluginTargetRootPath { get; init; }

    public required string ApplicationPayloadRootPath { get; init; }

    public required string PluginEntrypointPath { get; init; }

    public required string PluginLayoutMarkerPath { get; init; }
}

public sealed record NuGetPhase10ConfigureDryRunResult
{
    public required NuGetPhase10VerticalSliceResolvedPaths Paths { get; init; }

    public required ConfigurationPlanValidationResult Validation { get; init; }

    public required ConfigurationPlanResult PlanResult { get; init; }
}

public sealed record NuGetPhase10ConfigureResult
{
    public required NuGetPhase10VerticalSliceResolvedPaths Paths { get; init; }

    public required ConfigurationPlanResult PlanResult { get; init; }

    public required bool PluginLayoutMarkerPresent { get; init; }

    public required bool OwnershipManifestPresent { get; init; }
}

public sealed record NuGetPhase10UnconfigureResult
{
    public required NuGetPhase10VerticalSliceResolvedPaths Paths { get; init; }

    public required bool HadOwnedConfiguration { get; init; }

    public ConfigurationPlanResult? PlanResult { get; init; }

    public required bool PluginLayoutMarkerPresent { get; init; }

    public required bool OwnershipManifestPresent { get; init; }
}

public sealed record NuGetPhase10DoctorResult
{
    public required NuGetPhase10VerticalSliceResolvedPaths Paths { get; init; }

    public required bool ConfigurationPlanValid { get; init; }

    public required bool PluginLayoutMarkerPresent { get; init; }

    public required bool OwnershipManifestPresent { get; init; }

    public required bool NetCorePluginEntrypointPresent { get; init; }

    public required bool PluginModeEntrypointResolvable { get; init; }

    public required bool AzureArtifactsSourceCanonicalizationSuccess { get; init; }

    public required bool InteractivePolicyGuidanceSuccess { get; init; }

    public required bool OptionalEnvironmentOverridesAbsent { get; init; }
}

public sealed class NuGetPhase10UnrecognizedStateException : InvalidOperationException
{
    public NuGetPhase10UnrecognizedStateException(string message)
        : base(message) { }

    public NuGetPhase10UnrecognizedStateException(string message, Exception innerException)
        : base(message, innerException) { }
}

public sealed class NuGetPhase10VerticalSliceService
{
    private const string ProductId = "azureauth-credprovider";
    private const string ProductVersion = "phase10";
    private const string ManifestId = "phase10-nuget-plugin-layout";
    private const string ConfigurePlanId = "phase10-nuget-configure-plan";
    private const string EntrySelector = "nuget.plugin-layout";
    private const string PhysicalTargetKey = "physical-target";
    private const string NuGetPluginPathsEnvironmentVariable = "NUGET_PLUGIN_PATHS";
    private const string NuGetNetCorePluginPathsEnvironmentVariable = "NUGET_NETCORE_PLUGIN_PATHS";

    private static readonly Uri OrganizationScopedSource = new(
        "https://pkgs.dev.azure.com/org/_packaging/feed/nuget/v3/index.json"
    );
    private static readonly Uri ProjectScopedSource = new(
        "https://dev.azure.com/org/project/_packaging/feed/nuget/v3/index.json"
    );
    private static readonly Uri LegacySource = new(
        "https://org.pkgs.visualstudio.com/_packaging/feed/nuget/v3/index.json"
    );
    private readonly Func<string, string?> environmentVariableReader;
    private readonly IFileSystem fileSystem;
    private readonly NuGetPhase10VerticalSliceResolvedPaths paths;

    public NuGetPhase10VerticalSliceService(NuGetPhase10VerticalSliceOptions? options = null)
    {
        options ??= new NuGetPhase10VerticalSliceOptions();
        fileSystem = options.FileSystem ?? new SystemFileSystem();
        environmentVariableReader =
            options.EnvironmentVariableReader ?? Environment.GetEnvironmentVariable;
        paths = ResolvePaths(options, fileSystem, environmentVariableReader);
    }

    public NuGetPhase10VerticalSliceResolvedPaths Paths => paths;

    public static NuGetPhase10VerticalSliceService CreateConfigurationOnly(
        NuGetPhase10VerticalSliceOptions? options = null
    ) => new(options);

    public async ValueTask<NuGetPhase10ConfigureDryRunResult> DryRunConfigureAsync(
        CancellationToken cancellationToken = default
    )
    {
        cancellationToken.ThrowIfCancellationRequested();

        ThrowIfUnrecognizedOwnershipManifestExists();
        ThrowIfMissingManifestLeavesProductOwnedNuGetPluginLayoutState();
        ConfigurationChangePlan plan = CreateConfigurePlan();
        ConfigurationManager manager = CreateManager();
        ConfigurationPlanValidationResult validation = manager.ValidatePlan(plan);
        ConfigurationPlanResult planResult;
        try
        {
            planResult = await manager.DryRunAsync(plan, cancellationToken);
        }
        catch (Exception exception) when (IsExpectedStateCheckFailure(exception))
        {
            throw new NuGetPhase10UnrecognizedStateException(
                "The Phase 10 NuGet plugin layout state is not recognized.",
                exception
            );
        }

        return new NuGetPhase10ConfigureDryRunResult
        {
            Paths = paths,
            Validation = validation,
            PlanResult = planResult,
        };
    }

    public async ValueTask<NuGetPhase10ConfigureResult> ConfigureAsync(
        CancellationToken cancellationToken = default
    )
    {
        cancellationToken.ThrowIfCancellationRequested();

        ThrowIfUnrecognizedOwnershipManifestExists();
        ThrowIfMissingManifestLeavesProductOwnedNuGetPluginLayoutState();
        ConfigurationPlanResult planResult;
        try
        {
            planResult = await CreateManager().ApplyAsync(CreateConfigurePlan(), cancellationToken);
        }
        catch (Exception exception) when (IsExpectedStateCheckFailure(exception))
        {
            throw new NuGetPhase10UnrecognizedStateException(
                "The Phase 10 NuGet plugin layout state is not recognized.",
                exception
            );
        }

        NuGetPhase10OwnedState ownedState = await InspectOwnedStateAsync(cancellationToken);
        return new NuGetPhase10ConfigureResult
        {
            Paths = paths,
            PlanResult = planResult,
            PluginLayoutMarkerPresent = ownedState.PluginLayoutMarkerPresent,
            OwnershipManifestPresent = ownedState.OwnershipManifestPresent,
        };
    }

    public async ValueTask<NuGetPhase10UnconfigureResult> UnconfigureAsync(
        CancellationToken cancellationToken = default
    )
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (!TryLoadExpectedOwnershipManifest(out ConfigurationOwnershipManifest? manifest))
        {
            ThrowIfUnrecognizedOwnershipManifestExists();
            ThrowIfMissingManifestLeavesProductOwnedNuGetPluginLayoutState();
            NuGetPhase10OwnedState absentState = await InspectOwnedStateAsync(cancellationToken);
            return new NuGetPhase10UnconfigureResult
            {
                Paths = paths,
                HadOwnedConfiguration = false,
                PlanResult = null,
                PluginLayoutMarkerPresent = absentState.PluginLayoutMarkerPresent,
                OwnershipManifestPresent = absentState.OwnershipManifestPresent,
            };
        }

        ConfigurationPlanResult planResult;
        try
        {
            planResult = await CreateManager()
                .RemoveAsync(CreateUnconfigurePlan(manifest), cancellationToken);
        }
        catch (Exception exception) when (IsExpectedStateCheckFailure(exception))
        {
            throw new NuGetPhase10UnrecognizedStateException(
                "The Phase 10 NuGet plugin layout state is not recognized.",
                exception
            );
        }

        NuGetPhase10OwnedState ownedState = await InspectOwnedStateAsync(cancellationToken);
        return new NuGetPhase10UnconfigureResult
        {
            Paths = paths,
            HadOwnedConfiguration = true,
            PlanResult = planResult,
            PluginLayoutMarkerPresent = ownedState.PluginLayoutMarkerPresent,
            OwnershipManifestPresent = ownedState.OwnershipManifestPresent,
        };
    }

    public async ValueTask ValidateUnconfigureDryRunAsync(
        CancellationToken cancellationToken = default
    )
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (!TryLoadExpectedOwnershipManifest(out ConfigurationOwnershipManifest? manifest))
        {
            ThrowIfUnrecognizedOwnershipManifestExists();
            ThrowIfMissingManifestLeavesProductOwnedNuGetPluginLayoutState();
            return;
        }

        try
        {
            await CreateManager().DryRunAsync(CreateUnconfigurePlan(manifest), cancellationToken);
        }
        catch (Exception exception) when (IsExpectedStateCheckFailure(exception))
        {
            throw new NuGetPhase10UnrecognizedStateException(
                "The Phase 10 NuGet plugin layout state is not recognized.",
                exception
            );
        }
    }

    public async ValueTask<NuGetPhase10DoctorResult> DoctorAsync(
        CancellationToken cancellationToken = default
    )
    {
        cancellationToken.ThrowIfCancellationRequested();

        NuGetPhase10OwnedState ownedState = await InspectOwnedStateAsync(cancellationToken);
        bool configurationPlanValid = await TryValidateConfigurationPlanAsync(cancellationToken);
        bool netCorePluginEntrypointPresent = TryPluginEntrypointExists();
        bool pluginModeEntrypointResolvable =
            netCorePluginEntrypointPresent && TryResolvePluginModeEntrypoint();

        return new NuGetPhase10DoctorResult
        {
            Paths = paths,
            ConfigurationPlanValid = configurationPlanValid,
            PluginLayoutMarkerPresent = ownedState.PluginLayoutMarkerPresent,
            OwnershipManifestPresent = ownedState.OwnershipManifestPresent,
            NetCorePluginEntrypointPresent = netCorePluginEntrypointPresent,
            PluginModeEntrypointResolvable = pluginModeEntrypointResolvable,
            AzureArtifactsSourceCanonicalizationSuccess =
                TryValidateAzureArtifactsSourceCanonicalization(),
            InteractivePolicyGuidanceSuccess = TryValidateInteractivePolicyGuidance(),
            OptionalEnvironmentOverridesAbsent = OptionalEnvironmentOverridesAreAbsent(),
        };
    }

    private ConfigurationManager CreateManager() =>
        new(
            fileSystem,
            paths.OwnershipManifestPath,
            new ConfigurationPhysicalTargetWriterDispatcher(fileSystem)
        );

    private ConfigurationChangePlan CreateConfigurePlan()
    {
        return ConfigurationChangePlanPolicy.Create(
            ConfigurePlanId,
            ProductId,
            ConfigurationScope.User,
            new ConfigurationManifestMetadata
            {
                ManifestId = ManifestId,
                OwnerProductId = ProductId,
                EntrySelector = EntrySelector,
                ProductVersion = ProductVersion,
            },
            [
                CreateNuGetPluginLayoutChange(
                    ConfigurationChangeOperation.Set,
                    paths.ApplicationPayloadRootPath
                ),
            ]
        );
    }

    private ConfigurationChangePlan CreateUnconfigurePlan(ConfigurationOwnershipManifest manifest)
    {
        ArgumentNullException.ThrowIfNull(manifest);

        ConfigurationChange[] changes = manifest
            .Entries.Where(IsManagedManifestEntry)
            .OrderBy(entry => entry.Sequence)
            .Select(CreateNuGetPluginLayoutRemoveChange)
            .ToArray();
        if (changes.Length == 0)
        {
            throw new InvalidOperationException(
                "Owned NuGet plugin layout manifest does not contain removable entries."
            );
        }

        return ConfigurationChangePlanPolicy.Create(
            "phase10-nuget-unconfigure-plan",
            ProductId,
            manifest.Scope,
            new ConfigurationManifestMetadata
            {
                ManifestId = manifest.ManifestId,
                OwnerProductId = manifest.OwnerProductId,
                EntrySelector = manifest.EntrySelector,
                ResourceIdentity = manifest.ResourceIdentity,
                ProductVersion = manifest.ProductVersion,
                SafeMetadata = manifest.SafeMetadata,
            },
            changes
        );
    }

    private ConfigurationChange CreateNuGetPluginLayoutChange(
        ConfigurationChangeOperation operation,
        string? value
    ) =>
        new()
        {
            Operation = operation,
            TargetKind = ConfigurationTargetKind.NuGetPluginLayout,
            TargetPathOrName = paths.PluginTargetRootPath,
            Key = PhysicalTargetKey,
            Value = value,
            RequiresOwnershipRecord = true,
            PreserveDeclarationsAndComments = true,
        };

    private ConfigurationChange CreateNuGetPluginLayoutRemoveChange(
        ConfigurationOwnershipManifestEntry entry
    )
    {
        ArgumentNullException.ThrowIfNull(entry);

        return CreateNuGetPluginLayoutChange(ConfigurationChangeOperation.Remove, value: null);
    }

    private void ThrowIfUnrecognizedOwnershipManifestExists()
    {
        if (OwnershipManifestPathExists() && !TryLoadExpectedOwnershipManifest(out _))
        {
            throw new NuGetPhase10UnrecognizedStateException(
                "The Phase 10 NuGet ownership manifest is not recognized."
            );
        }
    }

    private void ThrowIfMissingManifestLeavesProductOwnedNuGetPluginLayoutState()
    {
        if (OwnershipManifestPathExists() || !ProductOwnedNuGetPluginLayoutStateExists())
        {
            return;
        }

        throw new NuGetPhase10UnrecognizedStateException(
            "The Phase 10 NuGet plugin layout state is not recognized."
        );
    }

    private bool OwnershipManifestPathExists()
    {
        try
        {
            return fileSystem.FileExists(paths.OwnershipManifestPath)
                || fileSystem.DirectoryExists(paths.OwnershipManifestPath);
        }
        catch (Exception exception) when (IsExpectedStateCheckFailure(exception))
        {
            return true;
        }
    }

    private bool ProductOwnedNuGetPluginLayoutStateExists()
    {
        try
        {
            return fileSystem.FileExists(paths.PluginTargetRootPath)
                || fileSystem.FileExists(paths.PluginLayoutMarkerPath)
                || fileSystem.DirectoryExists(paths.PluginLayoutMarkerPath)
                || fileSystem.FileExists(paths.PluginEntrypointPath)
                || fileSystem.DirectoryExists(paths.PluginEntrypointPath);
        }
        catch (NuGetPhase10UnrecognizedStateException)
        {
            throw;
        }
        catch (Exception exception) when (IsExpectedStateCheckFailure(exception))
        {
            throw new NuGetPhase10UnrecognizedStateException(
                "The Phase 10 NuGet plugin layout state is not recognized.",
                exception
            );
        }
    }

    private bool TryLoadExpectedOwnershipManifest(
        [System.Diagnostics.CodeAnalysis.NotNullWhen(true)]
            out ConfigurationOwnershipManifest? manifest
    )
    {
        manifest = null;
        try
        {
            if (!fileSystem.FileExists(paths.OwnershipManifestPath))
            {
                return false;
            }

            manifest = ConfigurationOwnershipManifestSerializer.Deserialize(
                fileSystem.ReadAllText(paths.OwnershipManifestPath)
            );
            if (
                !HasExpectedManifestMetadata(manifest)
                || !HasExpectedManagedManifestEntries(manifest)
            )
            {
                manifest = null;
                return false;
            }

            return true;
        }
        catch (Exception exception) when (IsExpectedStateCheckFailure(exception))
        {
            manifest = null;
            return false;
        }
    }

    private ValueTask<NuGetPhase10OwnedState> InspectOwnedStateAsync(
        CancellationToken cancellationToken
    )
    {
        cancellationToken.ThrowIfCancellationRequested();
        bool ownershipManifestPresent = OwnershipManifestPathExists();
        if (!ownershipManifestPresent)
        {
            return ValueTask.FromResult(
                new NuGetPhase10OwnedState(
                    PluginLayoutMarkerPathExists(),
                    OwnershipManifestPresent: false
                )
            );
        }

        return ValueTask.FromResult(
            new NuGetPhase10OwnedState(
                PluginLayoutMarkerPathExists(),
                ownershipManifestPresent
            )
        );
    }

    private bool PluginLayoutMarkerPathExists()
    {
        try
        {
            return fileSystem.FileExists(paths.PluginLayoutMarkerPath)
                && !fileSystem.DirectoryExists(paths.PluginLayoutMarkerPath);
        }
        catch (Exception exception) when (IsExpectedStateCheckFailure(exception))
        {
            return false;
        }
    }

    private async ValueTask<bool> TryValidateConfigurationPlanAsync(
        CancellationToken cancellationToken
    )
    {
        try
        {
            ConfigurationChangePlan plan = CreateConfigurePlan();
            ConfigurationManager manager = CreateManager();
            ConfigurationPlanValidationResult validation = manager.ValidatePlan(plan);
            ConfigurationPlanResult planResult = await manager.DryRunAsync(plan, cancellationToken);
            return validation.IsValid && planResult.Operation == ConfigurationPlanOperation.DryRun;
        }
        catch (Exception exception) when (IsExpectedStateCheckFailure(exception))
        {
            return false;
        }
    }

    private bool TryPluginEntrypointExists()
    {
        try
        {
            return fileSystem.FileExists(paths.PluginEntrypointPath)
                && !fileSystem.DirectoryExists(paths.PluginEntrypointPath);
        }
        catch (Exception exception) when (IsExpectedStateCheckFailure(exception))
        {
            return false;
        }
    }

    private bool TryResolvePluginModeEntrypoint()
    {
        string managedAssemblyInvocationName = Path.GetFileNameWithoutExtension(
            paths.PluginEntrypointPath
        );
        bool resolved = NuGetPluginAdapter.TryResolveProtocolInvocation(
            managedAssemblyInvocationName,
            ["-Plugin"],
            out AdapterInvocationContext? context
        );
        return resolved
            && context is not null
            && context.IsProtocolInvocation
            && context.Protocol == AdapterProtocol.NuGetPlugin;
    }

    private static bool TryValidateAzureArtifactsSourceCanonicalization()
    {
        try
        {
            return IsExpectedAzureArtifactsSource(
                    OrganizationScopedSource,
                    "pkgs.dev.azure.com",
                    "org",
                    project: null,
                    "feed"
                )
                && IsExpectedAzureArtifactsSource(
                    ProjectScopedSource,
                    "dev.azure.com",
                    "org",
                    "project",
                    "feed"
                )
                && IsExpectedAzureArtifactsSource(
                    LegacySource,
                    "org.pkgs.visualstudio.com",
                    "org",
                    project: null,
                    "feed"
                )
                && HasExpectedClassification(
                    new Uri("https://api.nuget.org/v3/index.json"),
                    NuGetResourceParseStatus.NoCredential
                )
                && HasExpectedClassification(
                    new Uri("https://pkgs.dev.azure.com/org/_packaging/feed/npm"),
                    NuGetResourceParseStatus.ProtocolViolation
                );
        }
        catch (Exception exception) when (IsExpectedStateCheckFailure(exception))
        {
            return false;
        }
    }

    private static bool TryValidateInteractivePolicyGuidance()
    {
        try
        {
            NuGetResourceParseResult parseResult = NuGetResourceSourceParser.Parse(
                OrganizationScopedSource
            );
            if (
                parseResult.Status != NuGetResourceParseStatus.Success
                || parseResult.Resource is null
            )
            {
                return false;
            }

            CredentialRequestV2 silentRequest = NuGetPluginAdapter.CreateCredentialRequest(
                parseResult.Resource,
                new GetAuthenticationCredentialsRequest(
                    OrganizationScopedSource,
                    isRetry: false,
                    isNonInteractive: true,
                    canShowDialog: false
                )
            );
            CredentialRequestV2 deviceCodeRequest = NuGetPluginAdapter.CreateCredentialRequest(
                parseResult.Resource,
                new GetAuthenticationCredentialsRequest(
                    OrganizationScopedSource,
                    isRetry: false,
                    isNonInteractive: false,
                    canShowDialog: false
                )
            );
            CredentialRequestV2 browserRequest = NuGetPluginAdapter.CreateCredentialRequest(
                parseResult.Resource,
                new GetAuthenticationCredentialsRequest(
                    OrganizationScopedSource,
                    isRetry: false,
                    isNonInteractive: false,
                    canShowDialog: true
                )
            );

            return silentRequest.InteractivePolicy == InteractivePolicy.Never
                && silentRequest.AcquisitionMode == AcquisitionMode.SilentOnly
                && deviceCodeRequest.InteractivePolicy == InteractivePolicy.HostToolAllows
                && deviceCodeRequest.AcquisitionMode == AcquisitionMode.InteractionAllowed
                && deviceCodeRequest.IdentityFlow == IdentityFlow.DeviceCode
                && browserRequest.InteractivePolicy == InteractivePolicy.HostToolAllows
                && browserRequest.AcquisitionMode == AcquisitionMode.InteractionAllowed
                && browserRequest.IdentityFlow == IdentityFlow.InteractiveBrowser;
        }
        catch (Exception exception) when (IsExpectedStateCheckFailure(exception))
        {
            return false;
        }
    }

    private static bool IsExpectedAzureArtifactsSource(
        Uri source,
        string host,
        string organization,
        string? project,
        string feed
    )
    {
        NuGetResourceParseResult parseResult = NuGetResourceSourceParser.Parse(source);
        CanonicalResourceIdentity? resource = parseResult.Resource;
        return parseResult.Status == NuGetResourceParseStatus.Success
            && resource is not null
            && string.Equals(resource.AzureDevOpsHost, host, StringComparison.Ordinal)
            && string.Equals(resource.Organization, organization, StringComparison.Ordinal)
            && string.Equals(resource.Project, project, StringComparison.Ordinal)
            && string.Equals(resource.Feed, feed, StringComparison.Ordinal)
            && resource.Repository is null
            && resource.ServiceEndpoint == source;
    }

    private static bool HasExpectedClassification(
        Uri source,
        NuGetResourceParseStatus expectedStatus
    )
    {
        NuGetResourceParseResult parseResult = NuGetResourceSourceParser.Parse(source);
        return parseResult.Status == expectedStatus && parseResult.Resource is null;
    }

    private bool OptionalEnvironmentOverridesAreAbsent()
    {
        return string.IsNullOrWhiteSpace(
                environmentVariableReader(NuGetPluginPathsEnvironmentVariable)
            )
            && string.IsNullOrWhiteSpace(
                environmentVariableReader(NuGetNetCorePluginPathsEnvironmentVariable)
            );
    }

    private static bool HasExpectedManifestMetadata(ConfigurationOwnershipManifest manifest) =>
        string.Equals(manifest.ManifestId, ManifestId, StringComparison.Ordinal)
        && string.Equals(manifest.OwnerProductId, ProductId, StringComparison.Ordinal)
        && manifest.Scope == ConfigurationScope.User
        && string.Equals(manifest.EntrySelector, EntrySelector, StringComparison.Ordinal)
        && string.Equals(manifest.ProductVersion, ProductVersion, StringComparison.Ordinal)
        && manifest.ResourceIdentity is null
        && manifest.SafeMetadata.Count == 0;

    private bool HasExpectedManagedManifestEntries(ConfigurationOwnershipManifest manifest) =>
        manifest.Entries.Count == 1 && HasExpectedManagedManifestEntry(manifest.Entries[0]);

    private bool HasExpectedManagedManifestEntry(ConfigurationOwnershipManifestEntry entry)
    {
        ArgumentNullException.ThrowIfNull(entry);

        return MatchesManagedManifestEntry(entry) && entry.Sequence == 1;
    }

    private bool IsManagedManifestEntry(ConfigurationOwnershipManifestEntry entry)
    {
        ArgumentNullException.ThrowIfNull(entry);
        return MatchesManagedManifestEntry(entry);
    }

    private bool MatchesManagedManifestEntry(ConfigurationOwnershipManifestEntry entry)
    {
        return entry.TargetKind == ConfigurationTargetKind.NuGetPluginLayout
            && string.Equals(entry.Key, PhysicalTargetKey, StringComparison.Ordinal)
            && string.Equals(
                NormalizeComparablePath(entry.TargetPathOrName),
                NormalizeComparablePath(paths.PluginTargetRootPath),
                GetPathComparison()
            );
    }

    private string NormalizeComparablePath(string path)
    {
        return Path.TrimEndingDirectorySeparator(fileSystem.GetFullPath(path)).Replace('\\', '/');
    }

    private static NuGetPhase10VerticalSliceResolvedPaths ResolvePaths(
        NuGetPhase10VerticalSliceOptions options,
        IFileSystem fileSystem,
        Func<string, string?> environmentVariableReader
    )
    {
        string stateDirectoryPath = fileSystem.GetFullPath(
            options.StateDirectoryPath ?? GetDefaultStateDirectoryPath()
        );
        string ownershipManifestPath = FileSystemPathSemantics.Combine(
            fileSystem,
            stateDirectoryPath,
            "manifests",
            "nuget-plugin-layout-ownership-manifest.json"
        );
        string homeDirectory = GetCurrentUserProfileDirectory(environmentVariableReader);
        if (string.IsNullOrWhiteSpace(homeDirectory))
        {
            throw new InvalidOperationException(
                "The NuGet plugin target root could not be resolved."
            );
        }

        ConfigurationTargetLayoutProjection projection =
            ConfigurationLayoutProjector.ProjectNuGetPluginLayout(
                new ConfigurationLayoutProjectionContext
                {
                    Platform = GetCurrentLayoutPlatform(),
                    HomeDirectory = homeDirectory,
                    LocalAppDataDirectory = GetCurrentLocalApplicationDataDirectory(
                        environmentVariableReader
                    ),
                }
            );
        string pluginTargetRootPath = fileSystem.GetFullPath(projection.TargetPath);
        string applicationPayloadRootPath = fileSystem.GetFullPath(
            options.ApplicationPayloadRootPath ?? AppContext.BaseDirectory
        );
        string pluginEntrypointPath = FileSystemPathSemantics.Combine(
            fileSystem,
            pluginTargetRootPath,
            "azureauth-credprovider.dll"
        );
        string pluginLayoutMarkerPath = FileSystemPathSemantics.Combine(
            fileSystem,
            pluginTargetRootPath,
            NuGetPluginLayoutPhysicalTargetWriter.MarkerFileName
        );

        return new NuGetPhase10VerticalSliceResolvedPaths
        {
            StateDirectoryPath = stateDirectoryPath,
            OwnershipManifestPath = ownershipManifestPath,
            PluginTargetRootPath = pluginTargetRootPath,
            ApplicationPayloadRootPath = applicationPayloadRootPath,
            PluginEntrypointPath = pluginEntrypointPath,
            PluginLayoutMarkerPath = pluginLayoutMarkerPath,
        };
    }

    private static string GetDefaultStateDirectoryPath()
    {
        string userProfile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        if (!string.IsNullOrWhiteSpace(userProfile))
        {
            return Path.Combine(userProfile, "." + ProductId, "phase10");
        }

        string localApplicationData = Environment.GetFolderPath(
            Environment.SpecialFolder.LocalApplicationData
        );
        if (!string.IsNullOrWhiteSpace(localApplicationData))
        {
            return Path.Combine(localApplicationData, ProductId, "phase10");
        }

        return Path.Combine(Path.GetTempPath(), ProductId, "phase10");
    }

    private static string GetCurrentUserProfileDirectory(
        Func<string, string?> environmentVariableReader
    )
    {
        string? userProfile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        if (!string.IsNullOrWhiteSpace(userProfile))
        {
            return Path.TrimEndingDirectorySeparator(userProfile);
        }

        if (OperatingSystem.IsWindows())
        {
            string? windowsUserProfile = environmentVariableReader("USERPROFILE");
            if (!string.IsNullOrWhiteSpace(windowsUserProfile))
            {
                return Path.TrimEndingDirectorySeparator(windowsUserProfile);
            }

            string? homeDrive = environmentVariableReader("HOMEDRIVE");
            string? homePath = environmentVariableReader("HOMEPATH");
            if (!string.IsNullOrWhiteSpace(homeDrive) && !string.IsNullOrWhiteSpace(homePath))
            {
                return Path.TrimEndingDirectorySeparator(homeDrive + homePath);
            }
        }
        else
        {
            string? home = environmentVariableReader("HOME");
            if (!string.IsNullOrWhiteSpace(home))
            {
                return Path.TrimEndingDirectorySeparator(home);
            }
        }

        return string.Empty;
    }

    private static string? GetCurrentLocalApplicationDataDirectory(
        Func<string, string?> environmentVariableReader
    )
    {
        string localApplicationData = Environment.GetFolderPath(
            Environment.SpecialFolder.LocalApplicationData
        );
        if (!string.IsNullOrWhiteSpace(localApplicationData))
        {
            return Path.TrimEndingDirectorySeparator(localApplicationData);
        }

        string? configured = environmentVariableReader("LOCALAPPDATA");
        return string.IsNullOrWhiteSpace(configured)
            ? null
            : Path.TrimEndingDirectorySeparator(configured);
    }

    private static ConfigurationLayoutPlatform GetCurrentLayoutPlatform() =>
        OperatingSystem.IsWindows() ? ConfigurationLayoutPlatform.Windows
        : OperatingSystem.IsMacOS() ? ConfigurationLayoutPlatform.MacOs
        : ConfigurationLayoutPlatform.Linux;

    private static StringComparison GetPathComparison() =>
        OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;

    private static bool IsExpectedStateCheckFailure(Exception exception) =>
        exception
            is ArgumentException
                or IOException
                or InvalidOperationException
                or NotSupportedException
                or PlatformNotSupportedException
                or System.Text.Json.JsonException
                or UnauthorizedAccessException;
}

internal sealed record NuGetPhase10OwnedState(
    bool PluginLayoutMarkerPresent,
    bool OwnershipManifestPresent
);
