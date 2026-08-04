using System.Security.Cryptography;
using System.Text;
using Hcoona.AzureAuth.CredProvider.Contracts;
using Hcoona.AzureAuth.CredProvider.Platform.AdapterHost;
using Hcoona.AzureAuth.CredProvider.Platform.Composition;
using Hcoona.AzureAuth.CredProvider.Platform.Configuration;
using Hcoona.AzureAuth.CredProvider.Platform.FileSystem;
using Newtonsoft.Json.Linq;
using NuGet.Common;
using NuGet.Protocol.Plugins;

namespace Hcoona.AzureAuth.CredProvider.Platform.VerticalSlice;

public sealed record NuGetPhase10VerticalSliceOptions
{
    public string? StateDirectoryPath { get; init; }

    public IFileSystem? FileSystem { get; init; }

    public Func<string, string?>? EnvironmentVariableReader { get; init; }

    public BoundedCredentialAcquisitionAdapter? CredentialAcquisition { get; init; }
}

public sealed record NuGetPhase10VerticalSliceResolvedPaths
{
    public required string StateDirectoryPath { get; init; }

    public required string OwnershipManifestPath { get; init; }

    public required string PluginTargetRootPath { get; init; }

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
    internal const string MarkerFileName = ".azureauth-credprovider.nuget-plugin-layout";
    internal const string MarkerValue =
        "azureauth-credprovider nuget-plugin-layout\n"
        + "phase=10\n"
        + "runtime=netcore\n"
        + "entrypoint=azureauth-credprovider.dll\n";

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
    private readonly Lazy<BoundedCredentialAcquisitionAdapter>? credentialAcquisition;

    public NuGetPhase10VerticalSliceService(NuGetPhase10VerticalSliceOptions? options = null)
        : this(options, configurationOnly: false) { }

    private NuGetPhase10VerticalSliceService(
        NuGetPhase10VerticalSliceOptions? options,
        bool configurationOnly
    )
    {
        options ??= new NuGetPhase10VerticalSliceOptions();
        fileSystem = options.FileSystem ?? new SystemFileSystem();
        environmentVariableReader =
            options.EnvironmentVariableReader ?? Environment.GetEnvironmentVariable;
        paths = ResolvePaths(options, fileSystem);
        if (!configurationOnly)
        {
            credentialAcquisition = new Lazy<BoundedCredentialAcquisitionAdapter>(
                () =>
                    options.CredentialAcquisition
                    ?? new BoundedCredentialAcquisitionAdapter(
                        CredentialProviderCompositionRoot.CreateProduction().AcquisitionService
                    ),
                LazyThreadSafetyMode.ExecutionAndPublication
            );
        }
    }

    public NuGetPhase10VerticalSliceResolvedPaths Paths => paths;

    public static NuGetPhase10VerticalSliceService CreateConfigurationOnly(
        NuGetPhase10VerticalSliceOptions? options = null
    ) => new(options, configurationOnly: true);

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
                await TryValidateAzureArtifactsSourceCanonicalizationAsync(cancellationToken),
            InteractivePolicyGuidanceSuccess =
                await TryValidateInteractivePolicyGuidanceAsync(cancellationToken),
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
            [CreateNuGetPluginLayoutChange(ConfigurationChangeOperation.Set, MarkerValue)]
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
            if (!fileSystem.FileExists(paths.PluginLayoutMarkerPath))
            {
                return false;
            }

            if (fileSystem.DirectoryExists(paths.PluginLayoutMarkerPath))
            {
                throw new NuGetPhase10UnrecognizedStateException(
                    "The Phase 10 NuGet plugin layout state is not recognized."
                );
            }

            return string.Equals(
                fileSystem.ReadAllText(paths.PluginLayoutMarkerPath),
                MarkerValue,
                StringComparison.Ordinal
            );
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

    private async ValueTask<NuGetPhase10OwnedState> InspectOwnedStateAsync(
        CancellationToken cancellationToken
    )
    {
        bool ownershipManifestPresent = OwnershipManifestPathExists();
        if (!ownershipManifestPresent)
        {
            return new NuGetPhase10OwnedState(
                ProductOwnedNuGetPluginLayoutStateExists(),
                OwnershipManifestPresent: false
            );
        }

        bool pluginLayoutMarkerPresent = false;
        try
        {
            if (TryLoadExpectedOwnershipManifest(out ConfigurationOwnershipManifest? manifest))
            {
                pluginLayoutMarkerPresent = await CanDryRunOwnedNuGetRemovalAsync(
                    manifest,
                    cancellationToken
                );
            }
            else
            {
                pluginLayoutMarkerPresent = ProductOwnedNuGetPluginLayoutStateExists();
            }
        }
        catch (Exception exception) when (IsExpectedStateCheckFailure(exception))
        {
            pluginLayoutMarkerPresent = false;
        }

        return new NuGetPhase10OwnedState(pluginLayoutMarkerPresent, ownershipManifestPresent);
    }

    private async ValueTask<bool> CanDryRunOwnedNuGetRemovalAsync(
        ConfigurationOwnershipManifest manifest,
        CancellationToken cancellationToken
    )
    {
        ConfigurationPlanResult planResult = await CreateManager()
            .DryRunAsync(CreateUnconfigurePlan(manifest), cancellationToken);
        return planResult.Operation == ConfigurationPlanOperation.DryRun;
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

    private BoundedCredentialAcquisitionAdapter GetCredentialAcquisition() =>
        credentialAcquisition?.Value
        ?? throw new InvalidOperationException(
            "Credential acquisition is unavailable in a configuration-only service."
        );

    private async ValueTask<bool> TryValidateAzureArtifactsSourceCanonicalizationAsync(
        CancellationToken cancellationToken
    )
    {
        try
        {
            var adapter = new NuGetPluginAdapter(GetCredentialAcquisition());
            return await AuthenticationSucceedsAsync(
                    adapter,
                    OrganizationScopedSource,
                    cancellationToken
                )
                && await AuthenticationSucceedsAsync(
                    adapter,
                    ProjectScopedSource,
                    cancellationToken
                )
                && await AuthenticationSucceedsAsync(adapter, LegacySource, cancellationToken)
                && await UnsupportedHostReturnsNotFoundAsync(adapter, cancellationToken)
                && await WrongAzureArtifactsSuffixReturnsErrorAsync(adapter, cancellationToken);
        }
        catch (Exception exception) when (IsExpectedStateCheckFailure(exception))
        {
            return false;
        }
    }

    private async ValueTask<bool> TryValidateInteractivePolicyGuidanceAsync(
        CancellationToken cancellationToken
    )
    {
        try
        {
            var adapter = new NuGetPluginAdapter(GetCredentialAcquisition());
            GetAuthenticationCredentialsResponse response =
                await adapter.HandleGetAuthenticationCredentialsAsync(
                    new GetAuthenticationCredentialsRequest(
                        OrganizationScopedSource,
                        isRetry: false,
                        isNonInteractive: true,
                        canShowDialog: false
                    ),
                    cancellationToken
                );

            return response.ResponseCode == MessageResponseCode.Error
                && response.Username is null
                && response.Password is null
                && response.Message is not null
                && response.Message.Contains("interaction is blocked", StringComparison.Ordinal);
        }
        catch (Exception exception) when (IsExpectedStateCheckFailure(exception))
        {
            return false;
        }
    }

    private static async ValueTask<bool> AuthenticationSucceedsAsync(
        NuGetPluginAdapter adapter,
        Uri source,
        CancellationToken cancellationToken
    )
    {
        GetAuthenticationCredentialsResponse response =
            await adapter.HandleGetAuthenticationCredentialsAsync(
                new GetAuthenticationCredentialsRequest(
                    source,
                    isRetry: false,
                    isNonInteractive: false,
                    canShowDialog: false
                ),
                cancellationToken
            );
        return response.ResponseCode == MessageResponseCode.Success
            && string.Equals(response.Username, "AzureDevOps", StringComparison.Ordinal)
            && response.Password?.StartsWith("fake-secret-", StringComparison.Ordinal) == true
            && response.AuthenticationTypes is ["Basic"];
    }

    private static async ValueTask<bool> UnsupportedHostReturnsNotFoundAsync(
        NuGetPluginAdapter adapter,
        CancellationToken cancellationToken
    )
    {
        GetAuthenticationCredentialsResponse response =
            await adapter.HandleGetAuthenticationCredentialsAsync(
                new GetAuthenticationCredentialsRequest(
                    new Uri("https://api.nuget.org/v3/index.json"),
                    isRetry: false,
                    isNonInteractive: false,
                    canShowDialog: false
                ),
                cancellationToken
            );
        return response.ResponseCode == MessageResponseCode.NotFound
            && response.Username is null
            && response.Password is null;
    }

    private static async ValueTask<bool> WrongAzureArtifactsSuffixReturnsErrorAsync(
        NuGetPluginAdapter adapter,
        CancellationToken cancellationToken
    )
    {
        GetAuthenticationCredentialsResponse response =
            await adapter.HandleGetAuthenticationCredentialsAsync(
                new GetAuthenticationCredentialsRequest(
                    new Uri("https://pkgs.dev.azure.com/org/_packaging/feed/npm"),
                    isRetry: false,
                    isNonInteractive: false,
                    canShowDialog: false
                ),
                cancellationToken
            );
        return response.ResponseCode == MessageResponseCode.Error
            && response.Username is null
            && response.Password is null;
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
        IFileSystem fileSystem
    )
    {
        string stateDirectoryPath = fileSystem.GetFullPath(
            options.StateDirectoryPath ?? GetDefaultStateDirectoryPath()
        );
        string ownershipManifestPath = fileSystem.GetFullPath(
            Path.Combine(
                stateDirectoryPath,
                "manifests",
                "nuget-plugin-layout-ownership-manifest.json"
            )
        );
        string homeDirectory = GetCurrentUserProfileDirectory();
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
                }
            );
        string pluginTargetRootPath = fileSystem.GetFullPath(projection.TargetPath);
        string pluginEntrypointPath = fileSystem.GetFullPath(
            Path.Combine(pluginTargetRootPath, "azureauth-credprovider.dll")
        );
        string pluginLayoutMarkerPath = fileSystem.GetFullPath(
            Path.Combine(pluginTargetRootPath, MarkerFileName)
        );

        return new NuGetPhase10VerticalSliceResolvedPaths
        {
            StateDirectoryPath = stateDirectoryPath,
            OwnershipManifestPath = ownershipManifestPath,
            PluginTargetRootPath = pluginTargetRootPath,
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

        return string.Empty;
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
