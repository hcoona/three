using Hcoona.AzureAuth.CredProvider.Platform.AdapterHost;
using Hcoona.AzureAuth.CredProvider.Platform.AzureAuthProvider;
using Hcoona.AzureAuth.CredProvider.Platform.Configuration;
using Hcoona.AzureAuth.CredProvider.Platform.CredentialCore;
using Hcoona.AzureAuth.CredProvider.Platform.Diagnostics;
using Hcoona.AzureAuth.CredProvider.Platform.FileSystem;
using Hcoona.AzureAuth.CredProvider.Platform.Processes;
using Hcoona.AzureAuth.CredProvider.Platform.TokenMaterialization;
using Hcoona.AzureAuth.CredProvider.Platform.VerticalSlice;

namespace Hcoona.AzureAuth.CredProvider.Platform.Composition;

public enum CredentialProviderCompositionMode
{
    Production = 1,
    TestScaffold = 2,
}

public sealed record CredentialProviderProductionOptions
{
    public AzureAuthProviderConfig? ProviderConfig { get; init; }
    public AzureAuthBinding? Binding { get; init; }
    public IAzureAuthSecureRecordStore? SecureRecordStore { get; init; }
    public IAzureAuthInstallationDiscovery? InstallationDiscovery { get; init; }
    public IProcessRunner? ProcessRunner { get; init; }
    public HttpClient? HttpClient { get; init; }
    public TimeProvider? TimeProvider { get; init; }
    public DiagnosticRouter? Diagnostics { get; init; }
    public TextWriter? DeviceCodePromptWriter { get; init; }
    public IFileSystem? FileSystem { get; init; }
    public ConfigurationPhase14VerticalSliceOptions? ConfigurationOptions { get; init; }
    public Func<string, string?>? EnvironmentVariableReader { get; init; }
    public string? SecureStoreRootPath { get; init; }
    public string? WindowsLocalApplicationDataPath { get; init; }
    public string? WindowsMountRoot { get; init; }
    public string? WindowsPowerShellPath { get; init; }
    public string? NativeLinuxAzureAuthExecutablePath { get; init; }
    public bool? IsWslEnvironment { get; init; }
}

public sealed record CredentialProviderReadiness
{
    public required AzureAuthProviderSelection Provider { get; init; }
    public required CredentialProviderCapabilityReadiness Interactive { get; init; }
    public required CredentialProviderCapabilityReadiness Silent { get; init; }

    public bool IsReady => Interactive.IsReady;
    public string Code => Interactive.Code;
    public string SafeMessage => Interactive.SafeMessage;
}

public sealed record CredentialProviderCapabilityReadiness
{
    public required string Code { get; init; }
    public required string SafeMessage { get; init; }
    public required bool IsReady { get; init; }
}

public sealed class CredentialProviderCompositionRoot
{
    public const string ProviderConfigRecordName = "azureauth/provider-config.json";
    public const string BindingRecordName = "azureauth/account-binding.json";

    private readonly BoundedCredentialAcquisitionAdapter boundary;

    private CredentialProviderCompositionRoot(
        CredentialProviderCompositionMode mode,
        AzureAuthProviderConfig providerConfig,
        AzureAuthPersistedRecord<AzureAuthBinding> bindingRecord,
        AzureAuthInstallation installation,
        AzureAuthProcessLaunchOptions? launchOptions,
        ICredentialAcquisitionService acquisitionService,
        CredentialProviderReadiness readiness,
        CredentialProviderProductionOptions options
    )
    {
        Mode = mode;
        ProviderConfig = providerConfig;
        BindingRecord = bindingRecord;
        Installation = installation;
        LaunchOptions = launchOptions;
        AcquisitionService = acquisitionService;
        Readiness = readiness;
        ProductionOptions = options;
        boundary = new BoundedCredentialAcquisitionAdapter(acquisitionService);
    }

    public CredentialProviderCompositionMode Mode { get; }
    public AzureAuthProviderConfig ProviderConfig { get; }
    public AzureAuthPersistedRecord<AzureAuthBinding> BindingRecord { get; }
    public AzureAuthInstallation Installation { get; }
    private AzureAuthProcessLaunchOptions? LaunchOptions { get; }
    public ICredentialAcquisitionService AcquisitionService { get; }
    public CredentialProviderReadiness Readiness { get; }
    public CredentialProviderProductionOptions ProductionOptions { get; }
    public BoundedCredentialAcquisitionAdapter Boundary => boundary;

    public CredentialProviderReadiness GetReadiness(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Readiness;
    }

    public static CredentialProviderCompositionRoot CreateProduction(
        CredentialProviderProductionOptions? options = null
    )
    {
        options ??= new CredentialProviderProductionOptions();
        IAzureAuthSecureRecordStore store =
            options.SecureRecordStore
            ?? new SystemAzureAuthSecureRecordStore(
                new SystemAzureAuthSecureRecordStoreOptions
                {
                    ConfigRootPath = options.SecureStoreRootPath,
                    EnvironmentVariableReader = options.EnvironmentVariableReader,
                }
            );
        AzureAuthPersistedRecord<AzureAuthProviderConfig> configRecord =
            new AzureAuthProviderConfigPersistence(store).Read(ProviderConfigRecordName);
        if (configRecord.Status == AzureAuthPersistedRecordStatus.Malformed)
        {
            throw new InvalidOperationException("Provider configuration is malformed.");
        }

        AzureAuthProviderConfig config =
            options.ProviderConfig
            ?? configRecord.Value
            ?? AzureAuthProviderConfig.CreateUnconfigured();
        if (config.Selection != AzureAuthProviderSelection.Unspecified)
        {
            AzureAuthProviderConfigPolicy.EnsureValid(config);
        }

        AzureAuthPersistedRecord<AzureAuthBinding> bindingRecord = new AzureAuthBindingPersistence(
            store
        ).Read(BindingRecordName);
        if (options.Binding is not null)
        {
            AzureAuthBindingPolicy.EnsureValid(options.Binding);
            bindingRecord = AzureAuthPersistedRecord<AzureAuthBinding>.Present(
                BindingRecordName,
                "explicit-composition-binding",
                options.Binding
            );
        }

        IProcessRunner processRunner = options.ProcessRunner ?? new SystemProcessRunner();
        IAzureAuthInstallationDiscovery discovery =
            options.InstallationDiscovery
            ?? new SystemAzureAuthInstallationDiscovery(
                processRunner,
                new SystemAzureAuthInstallationDiscoveryOptions
                {
                    IsWslEnvironment = options.IsWslEnvironment,
                    WindowsMountRoot = options.WindowsMountRoot ?? "/mnt/c",
                    LocalApplicationDataPath = options.WindowsLocalApplicationDataPath,
                    WindowsPowerShellPath = options.WindowsPowerShellPath,
                    NativeLinuxExecutablePath = options.NativeLinuxAzureAuthExecutablePath,
                    EnvironmentVariableReader =
                        options.EnvironmentVariableReader ?? Environment.GetEnvironmentVariable,
                }
            );
        AzureAuthInstallation installation =
            config.Selection == AzureAuthProviderSelection.AzureAuth
                ? discovery.Discover(config)
                : AzureAuthInstallation.Failure(
                    AzureAuthInstallationStatus.Unsupported,
                    config.Selection == AzureAuthProviderSelection.Unspecified
                        ? "ProviderNotConfigured"
                        : "DirectMsalNotImplemented",
                    config.Selection == AzureAuthProviderSelection.Unspecified
                        ? "Provider configuration is missing."
                        : "Direct MSAL is selected but is not implemented."
                );
        AzureAuthProcessLaunchOptions? launchOptions =
            AzureAuthProcessLaunchOptions.FromInstallation(installation);
        AzureAuthProductionPrerequisiteFailure? prerequisite =
            AzureAuthProductionPrerequisitePolicy.Evaluate(
                config,
                bindingRecord,
                installation,
                launchOptions
            );

        IAccessTokenIdentityProvider identityProvider = prerequisite is not null
            ? new PrerequisiteUnavailableAccessTokenProvider(
                prerequisite.Code,
                prerequisite.SafeMessage
            )
            : new AzureAuthIdentityProvider(
                config,
                bindingRecord.Value!,
                launchOptions!,
                processRunner,
                options.DeviceCodePromptWriter
            );
        var exchange = new AzureDevOpsSpsTokenExchange(options.HttpClient, options.TimeProvider);
        ICredentialAcquisitionService service = new ComposedCredentialAcquisitionService(
            _ => identityProvider,
            new CredentialMaterializationService(exchange, options.TimeProvider)
        );
        CredentialProviderReadiness readiness = CreateReadiness(config, prerequisite, installation);
        CredentialProviderProductionOptions effectiveOptions = options with
        {
            SecureRecordStore = store,
            InstallationDiscovery = discovery,
            ProcessRunner = processRunner,
        };
        return new CredentialProviderCompositionRoot(
            CredentialProviderCompositionMode.Production,
            config,
            bindingRecord,
            installation,
            launchOptions,
            service,
            readiness,
            effectiveOptions
        );
    }

    public static CredentialProviderCompositionRoot CreateExplicitTestScaffold(
        CredentialCoreService credentialCore,
        CredentialProviderProductionOptions? options = null
    )
    {
        ArgumentNullException.ThrowIfNull(credentialCore);
        return CreateExplicitTestScaffold(
            new LegacyV1CredentialAcquisitionService(credentialCore),
            options
        );
    }

    public static CredentialProviderCompositionRoot CreateExplicitTestScaffold(
        ICredentialAcquisitionService acquisitionService,
        CredentialProviderProductionOptions? options = null
    )
    {
        ArgumentNullException.ThrowIfNull(acquisitionService);
        options ??= new CredentialProviderProductionOptions();
        AzureAuthProviderConfig config =
            options.ProviderConfig ?? AzureAuthProviderConfig.CreateDefault();
        CredentialProviderCapabilityReadiness unavailable = Unavailable(
            "TestScaffold",
            "Explicit deterministic test scaffold; not production-ready."
        );
        return new CredentialProviderCompositionRoot(
            CredentialProviderCompositionMode.TestScaffold,
            config,
            AzureAuthPersistedRecord<AzureAuthBinding>.Missing(BindingRecordName),
            AzureAuthInstallation.Failure(
                AzureAuthInstallationStatus.Unsupported,
                "TestScaffold",
                "Installation discovery is not used by the test scaffold."
            ),
            launchOptions: null,
            acquisitionService,
            new CredentialProviderReadiness
            {
                Provider = config.Selection,
                Interactive = unavailable,
                Silent = unavailable,
            },
            options
        );
    }

    public GitCredentialHelperAdapter CreateGitCredentialHelperAdapter() =>
        new(boundary, ProductionOptions.EnvironmentVariableReader);

    public NuGetPluginAdapter CreateNuGetPluginAdapter() => new(boundary);

    public KeyringHelperAdapter CreateKeyringHelperAdapter() =>
        new(boundary, ProductionOptions.EnvironmentVariableReader);

    public KeyringCliAdapter CreateKeyringCliAdapter() =>
        new(boundary, ProductionOptions.EnvironmentVariableReader);

    public GitPhase8VerticalSliceService CreateGitService(
        GitPhase8VerticalSliceOptions? options = null
    ) =>
        new(
            (options ?? new GitPhase8VerticalSliceOptions()) with
            {
                CredentialAcquisition = boundary,
            }
        );

    public NuGetPhase10VerticalSliceService CreateNuGetService(
        NuGetPhase10VerticalSliceOptions? options = null
    ) =>
        new(
            (options ?? new NuGetPhase10VerticalSliceOptions()) with
            {
                CredentialAcquisition = boundary,
            }
        );

    public AuthPhase14VerticalSliceService CreateAuthService(
        AuthPhase14VerticalSliceOptions? options = null
    ) =>
        new(
            (options ?? new AuthPhase14VerticalSliceOptions()) with
            {
                CredentialAcquisition = boundary,
                EnvironmentVariableReader =
                    options?.EnvironmentVariableReader
                    ?? ProductionOptions.EnvironmentVariableReader,
            }
        );

    public ConfigurationPhase14VerticalSliceService CreateConfigurationService(
        ConfigurationPhase14VerticalSliceOptions? options = null
    )
    {
        ConfigurationPhase14VerticalSliceOptions source =
            options
            ?? ProductionOptions.ConfigurationOptions
            ?? new ConfigurationPhase14VerticalSliceOptions();
        return new ConfigurationPhase14VerticalSliceService(
            source with
            {
                CredentialAcquisition = boundary,
                FileSystem = source.FileSystem ?? ProductionOptions.FileSystem,
                EnvironmentVariableReader =
                    source.EnvironmentVariableReader ?? ProductionOptions.EnvironmentVariableReader,
            }
        );
    }

    public AzureAuthDoctorReport RunProviderDoctor(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return AzureAuthDoctor.Run(ProviderConfig, BindingRecord, Installation);
    }

    public ValueTask<AzureAuthHealthProbeResult> RunProviderHealthProbeAsync(
        CancellationToken cancellationToken = default
    ) =>
        AzureAuthHealthProbe.RunAsync(
            ProviderConfig,
            LaunchOptions,
            ProductionOptions.ProcessRunner ?? new SystemProcessRunner(),
            cancellationToken
        );

    private static CredentialProviderReadiness CreateReadiness(
        AzureAuthProviderConfig config,
        AzureAuthProductionPrerequisiteFailure? prerequisite,
        AzureAuthInstallation installation
    )
    {
        CredentialProviderCapabilityReadiness interactive = prerequisite is null
            ? Ready(
                "AzureAuthInteractiveReady",
                "AzureAuth interactive acquisition is ready and may reuse the host MSAL cache."
            )
            : Unavailable(prerequisite.Code, prerequisite.SafeMessage);
        return new CredentialProviderReadiness
        {
            Provider = config.Selection,
            Interactive = interactive,
            Silent =
                prerequisite is not null ? interactive
                : config.Selection == AzureAuthProviderSelection.AzureAuth
                && installation.HostPlatform == AzureAuthHostPlatform.NativeLinux
                    ? Ready(
                        "AzureAuthSilentReady",
                        "AzureAuth native Linux cache-only acquisition is ready."
                    )
                : config.Selection == AzureAuthProviderSelection.AzureAuth
                    ? Unavailable(
                        "SilentAcquisitionUnavailable",
                        "AzureAuth 0.9.5 has no cache-only command mode on Windows or WSL; "
                            + "silent acquisition is unavailable."
                    )
                : interactive,
        };
    }

    private static CredentialProviderCapabilityReadiness Ready(string code, string safeMessage) =>
        new()
        {
            Code = code,
            SafeMessage = safeMessage,
            IsReady = true,
        };

    private static CredentialProviderCapabilityReadiness Unavailable(
        string code,
        string safeMessage
    ) =>
        new()
        {
            Code = code,
            SafeMessage = safeMessage,
            IsReady = false,
        };
}
