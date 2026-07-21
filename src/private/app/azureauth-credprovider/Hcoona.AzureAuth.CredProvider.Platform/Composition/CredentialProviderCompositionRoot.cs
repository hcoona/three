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
    public IAzureAuthArtifactTrustInspector? TrustInspector { get; init; }
    public IProcessRunner? ProcessRunner { get; init; }
    public HttpClient? HttpClient { get; init; }
    public TimeProvider? TimeProvider { get; init; }
    public DiagnosticRouter? Diagnostics { get; init; }
    public IFileSystem? FileSystem { get; init; }
    public ConfigurationPhase14VerticalSliceOptions? ConfigurationOptions { get; init; }
    public Func<string, string?>? EnvironmentVariableReader { get; init; }
    public string? SecureStoreRootPath { get; init; }
    public IWindowsArtifactProbe? WindowsArtifactProbe { get; init; }
    public bool? IsWslEnvironment { get; init; }
}

public sealed record CredentialProviderReadiness
{
    public required AzureAuthProviderSelection Provider { get; init; }
    public required CredentialProviderCapabilityReadiness Interactive { get; init; }
    public required CredentialProviderCapabilityReadiness Silent { get; init; }

    public bool IsReady => Interactive.IsReady && Silent.IsReady;
    public string Code => !Interactive.IsReady ? Interactive.Code : Silent.Code;
    public string SafeMessage => !Interactive.IsReady
        ? Interactive.SafeMessage
        : Silent.SafeMessage;
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
    private readonly Lazy<CredentialProviderReadiness> readiness;

    private CredentialProviderCompositionRoot(
        CredentialProviderCompositionMode mode,
        AzureAuthProviderConfig providerConfig,
        AzureAuthPersistedRecord<AzureAuthBinding> bindingRecord,
        IAzureAuthArtifactTrustInspector trustInspector,
        ICredentialAcquisitionService acquisitionService,
        Func<CredentialProviderReadiness> readinessFactory,
        CredentialProviderProductionOptions options)
    {
        Mode = mode;
        ProviderConfig = providerConfig;
        BindingRecord = bindingRecord;
        TrustInspector = trustInspector;
        AcquisitionService = acquisitionService;
        readiness = new Lazy<CredentialProviderReadiness>(
            readinessFactory,
            LazyThreadSafetyMode.ExecutionAndPublication);
        ProductionOptions = options;
        boundary = new BoundedCredentialAcquisitionAdapter(acquisitionService);
    }

    public CredentialProviderCompositionMode Mode { get; }
    public AzureAuthProviderConfig ProviderConfig { get; }
    public AzureAuthPersistedRecord<AzureAuthBinding> BindingRecord { get; }
    public IAzureAuthArtifactTrustInspector TrustInspector { get; }
    public ICredentialAcquisitionService AcquisitionService { get; }
    public CredentialProviderReadiness Readiness => readiness.Value;
    public CredentialProviderProductionOptions ProductionOptions { get; }
    public BoundedCredentialAcquisitionAdapter Boundary => boundary;

    public static CredentialProviderCompositionRoot CreateProduction(
        CredentialProviderProductionOptions? options = null)
    {
        options ??= new CredentialProviderProductionOptions();
        Func<string, string?> readEnvironment =
            options.EnvironmentVariableReader ?? Environment.GetEnvironmentVariable;
        string? rawWslInterop = readEnvironment("WSL_INTEROP");
        string? wslInterop = WslInteropPathPolicy.IsValid(rawWslInterop) ? rawWslInterop : null;
        bool isWsl = IsWsl(options, rawWslInterop);
        IAzureAuthSecureRecordStore store =
            options.SecureRecordStore
            ?? new SystemAzureAuthSecureRecordStore(
                new SystemAzureAuthSecureRecordStoreOptions
                {
                    ConfigRootPath = options.SecureStoreRootPath,
                    EnvironmentVariableReader = options.EnvironmentVariableReader,
                });
        AzureAuthPersistedRecord<AzureAuthProviderConfig> configRecord =
            new AzureAuthProviderConfigPersistence(store).Read(ProviderConfigRecordName);
        AzureAuthProviderConfig config = options.ProviderConfig
            ?? configRecord.Status switch
            {
                AzureAuthPersistedRecordStatus.Present => configRecord.Value!,
                AzureAuthPersistedRecordStatus.Missing
                    or AzureAuthPersistedRecordStatus.Unsupported => AzureAuthProviderConfig.CreateDefault(),
                AzureAuthPersistedRecordStatus.Malformed => throw new InvalidOperationException(
                    "Provider configuration is malformed."),
                AzureAuthPersistedRecordStatus.Unsafe => throw new InvalidOperationException(
                    "Provider configuration storage is unsafe."),
                _ => throw new InvalidOperationException("Provider configuration is unavailable."),
            };
        AzureAuthProviderConfigPolicy.EnsureValid(config);

        AzureAuthPersistedRecord<AzureAuthBinding> bindingRecord =
            new AzureAuthBindingPersistence(store).Read(BindingRecordName);
        if (bindingRecord.Status is AzureAuthPersistedRecordStatus.Malformed
            or AzureAuthPersistedRecordStatus.Unsafe
            or AzureAuthPersistedRecordStatus.Unavailable
            or AzureAuthPersistedRecordStatus.Unspecified)
        {
            throw new InvalidOperationException("Provider binding storage is unavailable.");
        }
        if (options.Binding is not null)
        {
            bindingRecord = AzureAuthPersistedRecord<AzureAuthBinding>.Present(
                BindingRecordName,
                "explicit-composition-binding",
                options.Binding);
        }
        AzureAuthBinding binding = options.Binding
            ?? bindingRecord.Value
            ?? AzureAuthBindingPolicy.CreateUnbound(
                DateTimeOffset.FromUnixTimeSeconds(
                    (options.TimeProvider ?? TimeProvider.System)
                        .GetUtcNow()
                        .ToUnixTimeSeconds()));
        AzureAuthBindingPolicy.EnsureValid(binding);

        IProcessRunner processRunner = options.ProcessRunner ?? new SystemProcessRunner();
        IAzureAuthArtifactTrustInspector inspector = options.TrustInspector
            ?? CreateProductionTrustInspector(options, processRunner, wslInterop, isWsl);
        CredentialProviderProductionOptions effectiveOptions = options with
        {
            SecureRecordStore = store,
            TrustInspector = inspector,
            ProcessRunner = processRunner,
        };
        var exchange = new AzureDevOpsSpsTokenExchange(
            effectiveOptions.HttpClient,
            effectiveOptions.TimeProvider);
        ICredentialAcquisitionService service = new ComposedCredentialAcquisitionService(
            () => CreateIdentityProvider(config, bindingRecord, inspector),
            new CredentialMaterializationService(exchange, options.TimeProvider),
            applyAzureAuthRequestPreflight:
                config.Selection == AzureAuthProviderSelection.AzureAuth);
        return new CredentialProviderCompositionRoot(
            CredentialProviderCompositionMode.Production,
            config,
            bindingRecord,
            inspector,
            service,
            () => GetReadiness(
                config,
                bindingRecord,
                inspector,
                options.ProviderConfig is null
                    ? configRecord.Status
                    : AzureAuthPersistedRecordStatus.Present),
            effectiveOptions);
    }

    public static CredentialProviderCompositionRoot CreateExplicitTestScaffold(
        CredentialCoreService credentialCore,
        CredentialProviderProductionOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(credentialCore);
        return CreateExplicitTestScaffold(
            new LegacyV1CredentialAcquisitionService(credentialCore),
            options);
    }

    public static CredentialProviderCompositionRoot CreateExplicitTestScaffold(
        ICredentialAcquisitionService acquisitionService,
        CredentialProviderProductionOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(acquisitionService);
        options ??= new CredentialProviderProductionOptions();
        AzureAuthProviderConfig config =
            options.ProviderConfig ?? AzureAuthProviderConfig.CreateDefault();
        var binding = AzureAuthPersistedRecord<AzureAuthBinding>.Missing(BindingRecordName);
        return new CredentialProviderCompositionRoot(
            CredentialProviderCompositionMode.TestScaffold,
            config,
            binding,
            options.TrustInspector ?? new DeferredAzureAuthArtifactTrustInspector(),
            acquisitionService,
            () => new CredentialProviderReadiness
            {
                Provider = config.Selection,
                Interactive = Unavailable(
                    "TestScaffold",
                    "Explicit deterministic test scaffold; not production-ready."),
                Silent = Unavailable(
                    "TestScaffold",
                    "Explicit deterministic test scaffold; not production-ready."),
            },
            options);
    }

    public GitCredentialHelperAdapter CreateGitCredentialHelperAdapter() => new(boundary);
    public NuGetPluginAdapter CreateNuGetPluginAdapter() => new(boundary);
    public KeyringHelperAdapter CreateKeyringHelperAdapter() => new(boundary);

    public GitPhase8VerticalSliceService CreateGitService(
        GitPhase8VerticalSliceOptions? options = null) =>
        new((options ?? new GitPhase8VerticalSliceOptions()) with
        {
            CredentialAcquisition = boundary,
        });

    public NuGetPhase10VerticalSliceService CreateNuGetService(
        NuGetPhase10VerticalSliceOptions? options = null) =>
        new((options ?? new NuGetPhase10VerticalSliceOptions()) with
        {
            CredentialAcquisition = boundary,
        });

    public AuthPhase14VerticalSliceService CreateAuthService(
        AuthPhase14VerticalSliceOptions? options = null) =>
        new((options ?? new AuthPhase14VerticalSliceOptions()) with
        {
            CredentialAcquisition = boundary,
            EnvironmentVariableReader = options?.EnvironmentVariableReader
                ?? ProductionOptions.EnvironmentVariableReader,
        });

    public ConfigurationPhase14VerticalSliceService CreateConfigurationService(
        ConfigurationPhase14VerticalSliceOptions? options = null)
    {
        ConfigurationPhase14VerticalSliceOptions source =
            options ?? ProductionOptions.ConfigurationOptions
            ?? new ConfigurationPhase14VerticalSliceOptions();
        return new ConfigurationPhase14VerticalSliceService(source with
        {
            CredentialAcquisition = boundary,
            FileSystem = source.FileSystem ?? ProductionOptions.FileSystem,
            EnvironmentVariableReader = source.EnvironmentVariableReader
                ?? ProductionOptions.EnvironmentVariableReader,
        });
    }

    public AzureAuthDoctorReport RunProviderDoctor()
    {
        AzureAuthTrustResult trust = ProviderConfig.Selection == AzureAuthProviderSelection.AzureAuth
            ? AzureAuthTrustPolicy.Evaluate(ProviderConfig.DeploymentConfig!, TrustInspector)
            : AzureAuthTrustResult.Unspecified();
        return AzureAuthDoctor.Run(ProviderConfig, BindingRecord, trust);
    }

    private static CredentialProviderReadiness GetReadiness(
        AzureAuthProviderConfig config,
        AzureAuthPersistedRecord<AzureAuthBinding> bindingRecord,
        IAzureAuthArtifactTrustInspector inspector,
        AzureAuthPersistedRecordStatus configStatus)
    {
        if (config.Selection == AzureAuthProviderSelection.DirectMsal)
        {
            CredentialProviderCapabilityReadiness unavailable = Unavailable(
                configStatus
                    is AzureAuthPersistedRecordStatus.Missing
                        or AzureAuthPersistedRecordStatus.Unsupported
                    ? "ProviderNotConfigured"
                    : "DirectMsalNotImplemented",
                configStatus
                    is AzureAuthPersistedRecordStatus.Missing
                        or AzureAuthPersistedRecordStatus.Unsupported
                    ? "Provider configuration is missing; direct MSAL is the fail-closed default "
                        + "and is not implemented."
                    : "Direct MSAL is selected but its production provider is not implemented.");
            return new CredentialProviderReadiness
            {
                Provider = config.Selection,
                Interactive = unavailable,
                Silent = unavailable,
            };
        }

        AzureAuthProductionPrerequisiteFailure? failure =
            AzureAuthProductionPrerequisitePolicy.Evaluate(
                config,
                bindingRecord,
                inspector);
        if (failure is not null)
        {
            return CreateAzureAuthReadiness(
                Unavailable(failure.Code, failure.SafeMessage));
        }

        return CreateAzureAuthReadiness(
            Unavailable(
                "AccountEnforcementUnavailable",
                "The pinned AzureAuth aad command cannot enforce the bound account."));
    }

    private static CredentialProviderReadiness CreateAzureAuthReadiness(
        CredentialProviderCapabilityReadiness interactive) =>
        new()
        {
            Provider = AzureAuthProviderSelection.AzureAuth,
            Interactive = interactive,
            Silent = Unavailable(
                "SilentAcquisitionUnavailable",
                "Silent AzureAuth acquisition is not implemented; use explicit interactive login "
                    + "for interactive operations only. No automatic remediation is available."),
        };

    private static CredentialProviderCapabilityReadiness Unavailable(
        string code,
        string safeMessage) =>
        new()
        {
            Code = code,
            SafeMessage = safeMessage,
            IsReady = false,
        };

    private static IAccessTokenIdentityProvider CreateIdentityProvider(
        AzureAuthProviderConfig config,
        AzureAuthPersistedRecord<AzureAuthBinding> bindingRecord,
        IAzureAuthArtifactTrustInspector inspector)
    {
        if (config.Selection == AzureAuthProviderSelection.DirectMsal)
        {
            return new DirectMsalUnavailableAccessTokenProvider();
        }

        if (config.Selection != AzureAuthProviderSelection.AzureAuth)
        {
            throw new InvalidOperationException("Unsupported provider selection.");
        }

        return new AzureAuthAccountEnforcementUnavailableAccessTokenProvider(
            config,
            bindingRecord,
            inspector);
    }

    private static IAzureAuthArtifactTrustInspector CreateProductionTrustInspector(
        CredentialProviderProductionOptions options,
        IProcessRunner processRunner,
        string? wslInterop,
        bool isWsl)
    {
        if (options.WindowsArtifactProbe is not null)
        {
            return new WslWindowsArtifactTrustInspector(options.WindowsArtifactProbe);
        }

        if (!isWsl)
        {
            return new DeferredAzureAuthArtifactTrustInspector();
        }

        return new WslWindowsArtifactTrustInspector(
            new SystemWindowsArtifactProbe(
                processRunner,
                new SystemWindowsArtifactProbeOptions
                {
                    WslInterop = wslInterop,
                    EnvironmentVariableReader = _ => null,
                }));
    }

    private static bool IsWsl(
        CredentialProviderProductionOptions options,
        string? rawWslInterop)
    {
        if (options.IsWslEnvironment.HasValue)
        {
            return options.IsWslEnvironment.Value;
        }

        if (!OperatingSystem.IsLinux())
        {
            return false;
        }

        Func<string, string?> readEnvironment =
            options.EnvironmentVariableReader ?? Environment.GetEnvironmentVariable;
        if (!string.IsNullOrWhiteSpace(readEnvironment("WSL_DISTRO_NAME"))
            || !string.IsNullOrWhiteSpace(rawWslInterop))
        {
            return true;
        }

        try
        {
            string version = File.ReadAllText("/proc/sys/kernel/osrelease");
            return version.Contains("microsoft", StringComparison.OrdinalIgnoreCase);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }
}
