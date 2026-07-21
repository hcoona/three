using System.Diagnostics;
using System.Text;
using Hcoona.AzureAuth.CredProvider.Contracts;
using Hcoona.AzureAuth.CredProvider.Contracts.AzureAuthDeployment;
using Hcoona.AzureAuth.CredProvider.Platform.AdapterHost;
using Hcoona.AzureAuth.CredProvider.Platform.AzureAuthProvider;
using Hcoona.AzureAuth.CredProvider.Platform.Composition;
using Hcoona.AzureAuth.CredProvider.Platform.CredentialCore;
using Hcoona.AzureAuth.CredProvider.Platform.Diagnostics;
using Hcoona.AzureAuth.CredProvider.Platform.FileSystem;
using Hcoona.AzureAuth.CredProvider.Platform.Processes;
using Hcoona.AzureAuth.CredProvider.Platform.Redaction;
using Hcoona.AzureAuth.CredProvider.Platform.Tests.TestDoubles;
using Hcoona.AzureAuth.CredProvider.Platform.VerticalSlice;
using NuGet.Protocol.Plugins;
using Xunit;

namespace Hcoona.AzureAuth.CredProvider.Platform.Tests;

public sealed class CredentialProviderCompositionRootWp6Tests
{
    [Fact]
    public void ProductionDefaultIsDirectMsalAndFailsClosedWithoutSyntheticCredential()
    {
        CredentialProviderCompositionRoot root =
            CredentialProviderCompositionRoot.CreateProduction();

        CredentialResult result = root.Boundary.Acquire(
            CreateGitRequest(AcquisitionMode.InteractionAllowed),
            TestContext.Current.CancellationToken);

        Assert.Equal(CredentialProviderCompositionMode.Production, root.Mode);
        Assert.Equal("DirectMsalNotImplemented", result.Error?.Code);
        Assert.False(root.Readiness.Interactive.IsReady);
        Assert.False(root.Readiness.Silent.IsReady);
        Assert.False(result.ContainsCredentialMaterial);
    }

    [Fact]
    public void SynchronousBoundaryTimesOutWhenProviderIgnoresCancellation()
    {
        var provider = new NeverCompletingAcquisitionService();
        var boundary = new BoundedCredentialAcquisitionAdapter(
            provider,
            TimeSpan.FromMilliseconds(50));
        Stopwatch stopwatch = Stopwatch.StartNew();

        CredentialResult result = boundary.Acquire(
            CreateGitRequest(AcquisitionMode.SilentOnly),
            TestContext.Current.CancellationToken);

        stopwatch.Stop();
        Assert.Equal(CredentialResultStatus.CredentialUnavailable, result.Status);
        Assert.Equal("CredentialAcquisitionTimedOut", result.Error?.Code);
        Assert.False(result.ContainsCredentialMaterial);
        Assert.True(stopwatch.Elapsed < TimeSpan.FromSeconds(2), stopwatch.Elapsed.ToString());
        Assert.True(
            SpinWait.SpinUntil(
                () => provider.CancellationObserved,
                TimeSpan.FromSeconds(1)));
        provider.Complete();
    }

    [Fact]
    public void SynchronousBoundaryTimesOutWhenProviderBlocksBeforeReturningValueTask()
    {
        using var provider = new SynchronouslyBlockingAcquisitionService();
        var boundary = new BoundedCredentialAcquisitionAdapter(
            provider,
            TimeSpan.FromMilliseconds(100));
        Stopwatch stopwatch = Stopwatch.StartNew();

        CredentialResult result;
        try
        {
            result = boundary.Acquire(
                CreateGitRequest(AcquisitionMode.SilentOnly),
                TestContext.Current.CancellationToken);
        }
        finally
        {
            provider.Release();
        }

        stopwatch.Stop();
        Assert.True(provider.Started);
        Assert.Equal(CredentialResultStatus.CredentialUnavailable, result.Status);
        Assert.Equal("CredentialAcquisitionTimedOut", result.Error?.Code);
        Assert.False(result.ContainsCredentialMaterial);
        Assert.True(stopwatch.Elapsed < TimeSpan.FromSeconds(2), stopwatch.Elapsed.ToString());
        Assert.True(
            SpinWait.SpinUntil(
                () => provider.Completed,
                TimeSpan.FromSeconds(1)));
    }

    [Fact]
    public void SynchronousBoundaryPreservesCooperativeProviderResult()
    {
        CredentialResult expected = CredentialAcquisitionResultFactory.Failure(
            CredentialResultStatus.InteractionRequired,
            CredentialErrorKind.InteractionRequired,
            "CooperativeResult",
            "The cooperative provider completed.");
        var boundary = new BoundedCredentialAcquisitionAdapter(
            new CooperativeAcquisitionService(expected),
            TimeSpan.FromSeconds(1));

        CredentialResult result = boundary.Acquire(
            CreateGitRequest(AcquisitionMode.SilentOnly),
            TestContext.Current.CancellationToken);

        Assert.Same(expected, result);
    }

    [Fact]
    public void SynchronousBoundaryKeepsCallerCancellationDistinct()
    {
        var provider = new NeverCompletingAcquisitionService();
        var boundary = new BoundedCredentialAcquisitionAdapter(provider, TimeSpan.FromSeconds(5));
        using var cancellation = new CancellationTokenSource(TimeSpan.FromMilliseconds(50));

        CredentialResult result = boundary.Acquire(
            CreateGitRequest(AcquisitionMode.SilentOnly),
            cancellation.Token);

        Assert.Equal(CredentialResultStatus.CredentialUnavailable, result.Status);
        Assert.Equal("CredentialAcquisitionCanceled", result.Error?.Code);
        provider.Complete();
    }

    [Fact]
    public void ProductionCompositionSnapshotsWslInteropFromHostExactlyOnce()
    {
        var wslInteropReadCount = 0;
        string missingRoot = Path.Combine(
            AppContext.BaseDirectory,
            "missing-wsl-snapshot-" + Guid.NewGuid().ToString("N"));

        _ = CredentialProviderCompositionRoot.CreateProduction(
            new CredentialProviderProductionOptions
            {
                ProviderConfig = AzureAuthProviderConfig.CreateDefault(),
                SecureRecordStore = new SystemAzureAuthSecureRecordStore(missingRoot),
                EnvironmentVariableReader = name =>
                {
                    if (name == "WSL_INTEROP")
                    {
                        wslInteropReadCount++;
                        return "/run/WSL/123_interop";
                    }

                    return null;
                },
            });

        Assert.Equal(1, wslInteropReadCount);
        Assert.False(Directory.Exists(missingRoot));
    }

    [Fact]
    public void ExplicitTestScaffoldSucceedsInteractivelyAndProtocolFactoriesFailSilent()
    {
        CredentialProviderCompositionRoot root =
            CredentialProviderCompositionRoot.CreateExplicitTestScaffold(
                new CredentialCoreService(new DeterministicFakeIdentityProvider()));

        CredentialResult interactive = root.Boundary.Acquire(
            CreateGitRequest(AcquisitionMode.InteractionAllowed),
            TestContext.Current.CancellationToken);
        AdapterHostExecutionOutcome git = ExecuteGit(root.CreateGitCredentialHelperAdapter());
        GetAuthenticationCredentialsResponse nuget = root.CreateNuGetPluginAdapter()
            .HandleGetAuthenticationCredentials(
                new GetAuthenticationCredentialsRequest(
                    new Uri(
                        "https://pkgs.dev.azure.com/test-org/"
                            + "_packaging/test-feed/nuget/v3/index.json"),
                    isRetry: false,
                    isNonInteractive: true,
                    canShowDialog: false));
        AdapterHostExecutionOutcome keyring = ExecuteKeyring(
            root.CreateKeyringHelperAdapter());

        Assert.Equal(CredentialResultStatus.Success, interactive.Status);
        Assert.Equal(AdapterHostExitCode.InteractionRequired, git.Result.ExitCode);
        Assert.Equal(MessageResponseCode.Error, nuget.ResponseCode);
        Assert.Equal(AdapterHostExitCode.InteractionRequired, keyring.Result.ExitCode);
        Assert.False(root.Readiness.IsReady);
    }

    [Fact]
    public void ProtocolBuildersSetSilentOnlyAndNeverExactly()
    {
        var capture = new CapturingAcquisitionService();

        _ = ExecuteGit(new GitCredentialHelperAdapter(capture));
        AssertSilent(capture.LastRequest);

        _ = new NuGetPluginAdapter(capture).HandleGetAuthenticationCredentials(
            new GetAuthenticationCredentialsRequest(
                new Uri(
                    "https://pkgs.dev.azure.com/test-org/"
                        + "_packaging/test-feed/nuget/v3/index.json"),
                isRetry: false,
                isNonInteractive: false,
                canShowDialog: true));
        AssertSilent(capture.LastRequest);

        _ = ExecuteKeyring(new KeyringHelperAdapter(capture));
        AssertSilent(capture.LastRequest);
    }

    [Fact]
    public void ProductionProtocolColdCacheReturnsStableSilentUnavailableWithoutProcessCall()
    {
        var runner = new SuccessfulProcessRunner();
        string missingRoot = Path.Combine(
            AppContext.BaseDirectory,
            "missing-wp6-silent-" + Guid.NewGuid().ToString("N"));
        CredentialProviderCompositionRoot root =
            CredentialProviderCompositionRoot.CreateProduction(
                new CredentialProviderProductionOptions
                {
                    ProviderConfig = AzureAuthProviderConfig.CreateDefault(),
                    SecureRecordStore = new SystemAzureAuthSecureRecordStore(missingRoot),
                    ProcessRunner = runner,
                });

        AdapterHostExecutionOutcome outcome = ExecuteGit(
            root.CreateGitCredentialHelperAdapter());

        Assert.Equal(AdapterHostExitCode.InteractionRequired, outcome.Result.ExitCode);
        Assert.Equal("SilentAcquisitionUnavailable", outcome.Result.SafeDiagnosticCode);
        Assert.NotEqual("ProtocolViolation", outcome.Result.SafeDiagnosticCode);
        Assert.Equal(0, runner.CallCount);
        Assert.False(Directory.Exists(missingRoot));
    }

    [Fact]
    public void ProductionAzureAuthRequestPreflightPreservesProviderCodesWithoutTrustInspection()
    {
        AzureAuthProviderConfig config = CreateAzureAuthConfig();
        var trustedInspector = new TrustedInspector(config.DeploymentConfig!);
        AzureAuthBinding binding = AzureAuthBindingPolicy.CreateBound(
            config,
            "user@example.invalid",
            "tenant-1",
            DateTimeOffset.FromUnixTimeSeconds(
                DateTimeOffset.UtcNow.AddMinutes(-1).ToUnixTimeSeconds()),
            AzureAuthTrustPolicy.Evaluate(config.DeploymentConfig!, trustedInspector));
        var inspector = new CountingTrustInspector(trustedInspector);
        CredentialProviderCompositionRoot root =
            CredentialProviderCompositionRoot.CreateProduction(
                new CredentialProviderProductionOptions
                {
                    ProviderConfig = config,
                    Binding = binding,
                    TrustInspector = inspector,
                    IsWslEnvironment = false,
                });
        Assert.Equal(0, inspector.CallCount);
        (CredentialRequestV2 Request, string Code)[] cases =
        [
            (
                CreateGitRequest(AcquisitionMode.Unspecified),
                "AzureAuthAcquisitionModeRequired"
            ),
            (
                CreateGitRequest(AcquisitionMode.SilentOnly) with
                {
                    InteractivePolicy = InteractivePolicy.Never,
                },
                "SilentAcquisitionUnavailable"
            ),
            (
                CreateGitRequest(AcquisitionMode.InteractionAllowed) with
                {
                    IdentityFlow = IdentityFlow.DeviceCode,
                },
                "AzureAuthDeviceCodeUnsupported"
            ),
            (
                CreateGitRequest(AcquisitionMode.InteractionAllowed) with
                {
                    CachePolicy = CachePolicyMode.FuturePersistentCacheRequested,
                },
                "AzureAuthPersistentCacheUnsupported"
            ),
            (
                CreateGitRequest(AcquisitionMode.InteractionAllowed) with
                {
                    AccountHint = "invalid account hint",
                },
                "AzureAuthRequestRejected"
            ),
            (
                CreateGitRequest(AcquisitionMode.InteractionAllowed) with
                {
                    InteractivePolicy = InteractivePolicy.Never,
                },
                "AzureAuthRequestRejected"
            ),
            (
                CreateGitRequest(AcquisitionMode.InteractionAllowed) with
                {
                    IdentityFlow = IdentityFlow.ServicePrincipal,
                },
                "AzureAuthRequestRejected"
            ),
        ];

        foreach ((CredentialRequestV2 request, string expectedCode) in cases)
        {
            CredentialResult result = root.Boundary.Acquire(
                request,
                TestContext.Current.CancellationToken);

            Assert.Equal(expectedCode, result.Error?.Code);
            Assert.False(result.ContainsCredentialMaterial);
        }

        Assert.Equal(0, inspector.CallCount);

        CredentialResult browser = root.Boundary.Acquire(
            CreateGitRequest(AcquisitionMode.InteractionAllowed),
            TestContext.Current.CancellationToken);
        Assert.Equal("AccountEnforcementUnavailable", browser.Error?.Code);
        Assert.Equal(1, inspector.CallCount);

        _ = root.Readiness;
        Assert.Equal(2, inspector.CallCount);
        _ = root.RunProviderDoctor();
        Assert.Equal(3, inspector.CallCount);
    }

    [Fact]
    public async Task RootFactoriesShareSilentBoundaryWithoutInvokingInteractiveProvider()
    {
        var identityProvider = new InteractionCountingIdentityProvider();
        CredentialProviderCompositionRoot root =
            CredentialProviderCompositionRoot.CreateExplicitTestScaffold(
                new CredentialCoreService(identityProvider));

        _ = ExecuteGit(root.CreateGitCredentialHelperAdapter());
        _ = root.CreateNuGetPluginAdapter().HandleGetAuthenticationCredentials(
            new GetAuthenticationCredentialsRequest(
                new Uri(
                    "https://pkgs.dev.azure.com/test-org/"
                        + "_packaging/test-feed/nuget/v3/index.json"),
                isRetry: false,
                isNonInteractive: true,
                canShowDialog: false));
        _ = ExecuteKeyring(root.CreateKeyringHelperAdapter());
        Assert.NotNull(root.CreateGitService());
        Assert.NotNull(root.CreateNuGetService());
        AzureAuthDoctorReport doctor = root.RunProviderDoctor();
        var fileSystem = new InMemoryFileSystem(InMemoryPathSemantics.Posix);
        ConfigurationPhase14VerticalSliceService configuration =
            root.CreateConfigurationService(
                new ConfigurationPhase14VerticalSliceOptions
                {
                    FileSystem = fileSystem,
                    StateDirectoryPath = "/state/wp6-factories",
                    EnvironmentVariableReader = _ => null,
                });
        await Assert.ThrowsAsync<InvalidOperationException>(
            async () => await configuration.ConfigureAsync(
                CredentialEcosystem.Npm,
                ConfigurationPhase14Scope.User,
                TestContext.Current.CancellationToken));

        Assert.Equal(0, identityProvider.InteractionCount);
        Assert.NotEmpty(doctor.Checks);

        CredentialResult interactive = root.CreateAuthService().Login(
            new AuthPhase14LoginRequest
            {
                IdentityFlow = IdentityFlow.InteractiveBrowser,
            }).CredentialResult;
        Assert.Equal(CredentialResultStatus.Success, interactive.Status);
        Assert.Equal(1, identityProvider.InteractionCount);
    }

    [Fact]
    public void SilentTestScaffoldTranslationNeverInvokesInteractionProvider()
    {
        var identityProvider = new InteractionCountingIdentityProvider();
        CredentialProviderCompositionRoot root =
            CredentialProviderCompositionRoot.CreateExplicitTestScaffold(
                new CredentialCoreService(identityProvider));

        CredentialResult result = root.Boundary.Acquire(
            CreateGitRequest(AcquisitionMode.SilentOnly) with
            {
                InteractivePolicy = InteractivePolicy.Never,
            },
            TestContext.Current.CancellationToken);

        Assert.Equal(CredentialResultStatus.InteractionBlocked, result.Status);
        Assert.Equal(0, identityProvider.InteractionCount);
    }

    [Fact]
    public void ComposedAzureAuthFailsClosedWhenBoundAccountCannotBeEnforced()
    {
        AzureAuthProviderConfig config = CreateAzureAuthConfig();
        var inspector = new TrustedInspector(config.DeploymentConfig!);
        AzureAuthTrustResult trust = AzureAuthTrustPolicy.Evaluate(
            config.DeploymentConfig!,
            inspector);
        AzureAuthBinding binding = AzureAuthBindingPolicy.CreateBound(
            config,
            "user@example.invalid",
            "tenant-1",
            DateTimeOffset.FromUnixTimeSeconds(
                DateTimeOffset.UtcNow.AddMinutes(-1).ToUnixTimeSeconds()),
            trust);
        var runner = new SuccessfulProcessRunner();
        CredentialProviderCompositionRoot root =
            CredentialProviderCompositionRoot.CreateProduction(
                new CredentialProviderProductionOptions
                {
                    ProviderConfig = config,
                    Binding = binding,
                    TrustInspector = inspector,
                    ProcessRunner = runner,
                });

        CredentialResult interactive = root.Boundary.Acquire(
            CreateGitRequest(AcquisitionMode.InteractionAllowed),
            TestContext.Current.CancellationToken);
        AdapterHostExecutionOutcome protocol = ExecuteGit(
            root.CreateGitCredentialHelperAdapter());

        Assert.False(root.Readiness.Interactive.IsReady);
        Assert.Equal("AccountEnforcementUnavailable", root.Readiness.Interactive.Code);
        Assert.False(root.Readiness.Silent.IsReady);
        Assert.Equal("SilentAcquisitionUnavailable", root.Readiness.Silent.Code);
        Assert.Equal("AccountEnforcementUnavailable", interactive.Error?.Code);
        Assert.Equal(CredentialResultStatus.CredentialUnavailable, interactive.Status);
        Assert.Equal(AdapterHostExitCode.InteractionRequired, protocol.Result.ExitCode);
        Assert.Equal("SilentAcquisitionUnavailable", protocol.Result.SafeDiagnosticCode);
        Assert.Equal(0, runner.CallCount);
    }

    [Fact]
    public void ProductionAzureAuthOrdinaryPrerequisitesPrecedeAccountEnforcementBlocker()
    {
        AzureAuthProviderConfig config = CreateAzureAuthConfig();
        var trustedInspector = new TrustedInspector(config.DeploymentConfig!);
        AzureAuthTrustResult trust = AzureAuthTrustPolicy.Evaluate(
            config.DeploymentConfig!,
            trustedInspector);
        DateTimeOffset recordedAt = DateTimeOffset.FromUnixTimeSeconds(
            DateTimeOffset.UtcNow.AddMinutes(-1).ToUnixTimeSeconds());
        AzureAuthBinding validBinding = AzureAuthBindingPolicy.CreateBound(
            config,
            "user@example.invalid",
            "tenant-1",
            recordedAt,
            trust);
        AzureAuthBinding providerMismatch = AzureAuthBindingPolicy.CreateBound(
            AzureAuthProviderConfig.CreateDefault(),
            "user@example.invalid",
            "tenant-1",
            recordedAt);
        AzureAuthBinding deploymentMismatch = validBinding with
        {
            DeploymentKey = new string('b', 64),
        };
        AzureAuthBinding unbound = AzureAuthBindingPolicy.CreateUnbound(recordedAt);

        AssertProductionPrerequisite(
            config,
            binding: null,
            trustedInspector,
            "AzureAuthBindingRequired");
        AssertProductionPrerequisite(
            config,
            validBinding,
            new DeferredInspector(),
            "AzureAuthTrustDeferred");
        AssertProductionPrerequisite(
            config,
            unbound,
            trustedInspector,
            "AzureAuthBindingRequired");
        AssertProductionPrerequisite(
            config,
            providerMismatch,
            trustedInspector,
            "AzureAuthBindingProviderMismatch");
        AssertProductionPrerequisite(
            config,
            deploymentMismatch,
            trustedInspector,
            "AzureAuthBindingDeploymentMismatch");
    }

    [Fact]
    public void ProductionProviderSelectionPrecedesAzureAuthAccountEnforcementBlocker()
    {
        AzureAuthProviderConfig config = AzureAuthProviderConfig.CreateDefault();
        CredentialProviderCompositionRoot root =
            CredentialProviderCompositionRoot.CreateProduction(
                new CredentialProviderProductionOptions
                {
                    ProviderConfig = config,
                    Binding = AzureAuthBindingPolicy.CreateBound(
                        config,
                        "user@example.invalid",
                        "tenant-1",
                        DateTimeOffset.FromUnixTimeSeconds(
                            DateTimeOffset.UtcNow.AddMinutes(-1).ToUnixTimeSeconds())),
                    IsWslEnvironment = false,
                });

        CredentialResult result = root.Boundary.Acquire(
            CreateGitRequest(AcquisitionMode.InteractionAllowed),
            TestContext.Current.CancellationToken);

        Assert.Equal("DirectMsalNotImplemented", root.Readiness.Interactive.Code);
        Assert.Equal(root.Readiness.Interactive.Code, result.Error?.Code);
        Assert.Equal(
            root.Readiness.Interactive.SafeMessage,
            result.Error?.SafeMessage);
    }

    [Fact]
    public void ProductionAzureAuthHintMismatchesPrecedeValidBindingBlocker()
    {
        AzureAuthProviderConfig config = CreateAzureAuthConfig();
        var inspector = new TrustedInspector(config.DeploymentConfig!);
        AzureAuthBinding binding = AzureAuthBindingPolicy.CreateBound(
            config,
            "user@example.invalid",
            "tenant-1",
            DateTimeOffset.FromUnixTimeSeconds(
                DateTimeOffset.UtcNow.AddMinutes(-1).ToUnixTimeSeconds()),
            AzureAuthTrustPolicy.Evaluate(config.DeploymentConfig!, inspector));
        CredentialProviderCompositionRoot root =
            CredentialProviderCompositionRoot.CreateProduction(
                new CredentialProviderProductionOptions
                {
                    ProviderConfig = config,
                    Binding = binding,
                    TrustInspector = inspector,
                    IsWslEnvironment = false,
                });

        CredentialResult accountMismatch = root.Boundary.Acquire(
            CreateGitRequest(AcquisitionMode.InteractionAllowed) with
            {
                AccountHint = "other@example.invalid",
            },
            TestContext.Current.CancellationToken);
        CredentialResult tenantMismatch = root.Boundary.Acquire(
            CreateGitRequest(AcquisitionMode.InteractionAllowed) with
            {
                TenantHint = "tenant-2",
            },
            TestContext.Current.CancellationToken);
        CredentialResult validBound = root.Boundary.Acquire(
            CreateGitRequest(AcquisitionMode.InteractionAllowed) with
            {
                AccountHint = " USER@EXAMPLE.INVALID ",
                TenantHint = "TENANT-1",
            },
            TestContext.Current.CancellationToken);

        Assert.Equal(
            "AzureAuthBindingAccountMismatch",
            accountMismatch.Error?.Code);
        Assert.Equal(
            "AzureAuthBindingTenantMismatch",
            tenantMismatch.Error?.Code);
        Assert.Equal(
            "AccountEnforcementUnavailable",
            root.Readiness.Interactive.Code);
        Assert.Equal(
            root.Readiness.Interactive.Code,
            validBound.Error?.Code);
        Assert.Equal(
            root.Readiness.Interactive.SafeMessage,
            validBound.Error?.SafeMessage);
    }

    [Fact]
    public void ProductionOptionsDoNotExposeIndependentHostLaunchPathOverrides()
    {
        string[] propertyNames = typeof(CredentialProviderProductionOptions)
            .GetProperties()
            .Select(static property => property.Name)
            .ToArray();

        Assert.DoesNotContain("AzureAuthLaunchOptions", propertyNames);
        Assert.DoesNotContain("WslWindowsMountRoot", propertyNames);
        Assert.DoesNotContain("HostExecutablePath", propertyNames);
        Assert.DoesNotContain("HostWorkingDirectory", propertyNames);
    }

    private static AdapterHostExecutionOutcome ExecuteGit(
        GitCredentialHelperAdapter adapter) =>
        adapter.Execute(
            "azureauth-credprovider",
            ["git", "credential-helper", "get"],
            new StringReader(
                "protocol=https\nhost=dev.azure.com\npath=test-org/project/_git/repo\n\n"),
            TextWriter.Null,
            TextWriter.Null,
            CreateDiagnostics());

    private static AdapterHostExecutionOutcome ExecuteKeyring(
        KeyringHelperAdapter adapter) =>
        adapter.Execute(
            "azureauth-credprovider",
            [
                KeyringHelperV2.CommandName,
                "get",
                "--protocol-version",
                "2",
                "--service",
                "https://pkgs.dev.azure.com/test-org/_packaging/test-feed/pypi/simple/",
                "--mode",
                "password",
            ],
            TextWriter.Null,
            TextWriter.Null,
            CreateDiagnostics());

    private static void AssertSilent(CredentialRequestV2? request)
    {
        Assert.NotNull(request);
        Assert.Equal(AcquisitionMode.SilentOnly, request.AcquisitionMode);
        Assert.Equal(InteractivePolicy.Never, request.InteractivePolicy);
        Assert.True(
            CredentialRequestV2Policy.IsValid(request),
            CredentialRequestV2Policy.GetViolation(request));
    }

    private static void AssertProductionPrerequisite(
        AzureAuthProviderConfig config,
        AzureAuthBinding? binding,
        IAzureAuthArtifactTrustInspector inspector,
        string expectedCode)
    {
        string missingRoot = Path.Combine(
            AppContext.BaseDirectory,
            "missing-wp6-prerequisite-" + Guid.NewGuid().ToString("N"));
        CredentialProviderCompositionRoot root =
            CredentialProviderCompositionRoot.CreateProduction(
                new CredentialProviderProductionOptions
                {
                    ProviderConfig = config,
                    Binding = binding,
                    SecureRecordStore = new SystemAzureAuthSecureRecordStore(missingRoot),
                    TrustInspector = inspector,
                    IsWslEnvironment = false,
                });

        CredentialResult result = root.Boundary.Acquire(
            CreateGitRequest(AcquisitionMode.InteractionAllowed),
            TestContext.Current.CancellationToken);

        Assert.Equal(expectedCode, root.Readiness.Interactive.Code);
        Assert.Equal(root.Readiness.Interactive.Code, result.Error?.Code);
        Assert.Equal(
            root.Readiness.Interactive.SafeMessage,
            result.Error?.SafeMessage);
        Assert.False(Directory.Exists(missingRoot));
    }

    private static CredentialRequestV2 CreateGitRequest(AcquisitionMode acquisitionMode) =>
        new()
        {
            Ecosystem = CredentialEcosystem.Git,
            Operation = CredentialOperation.Get,
            Resource = CanonicalResourceIdentity.Create(
                "dev.azure.com",
                "test-org",
                new Uri("https://dev.azure.com/test-org")),
            ServiceIdentity = "default",
            RequestedAudience = TokenAudience.AzureDevOps,
            CredentialKind = CredentialKind.BasicPassword,
            IdentityFlow = IdentityFlow.InteractiveBrowser,
            InteractivePolicy = InteractivePolicy.UserAllowed,
            AcquisitionMode = acquisitionMode,
            CachePolicy = CachePolicyMode.ProductPersistentCacheDisabled,
        };

    private static DiagnosticRouter CreateDiagnostics() =>
        new([], SecretRedactor.Empty);

    private static AzureAuthProviderConfig CreateAzureAuthConfig() =>
        AzureAuthProviderConfig.CreateAzureAuth(
            new AzureAuthDeploymentConfig
            {
                SchemaVersion = ContractVersions.AzureAuthDeploymentConfigSchemaMajor,
                ExecutablePath = @"C:\Tools\AzureAuth.exe",
                ExecutableSha256 = new string('a', 64),
                SignerIdentity = "CN=AzureAuth, O=Hcoona, C=US",
                PublisherName = "Hcoona AzureAuth",
                ExecutableVersion = "1.0.0.0",
                ProvenanceIdentifier = "foundation/wp6",
            });

    private static string CreateJwt()
    {
        DateTimeOffset now = DateTimeOffset.UtcNow;
        string header = Base64Url("""{"alg":"RS256","typ":"JWT"}""");
        string payload = Base64Url(
            $$"""{"aud":"{{AzureAuthIdentityProvider.AzureDevOpsResourceId}}","tid":"tenant-1","iat":{{now.AddMinutes(-1).ToUnixTimeSeconds()}},"nbf":{{now.AddMinutes(-1).ToUnixTimeSeconds()}},"exp":{{now.AddHours(1).ToUnixTimeSeconds()}}}""");
        return $"{header}.{payload}.c2lnbmF0dXJl";
    }

    private static string Base64Url(string value) =>
        Convert
            .ToBase64String(Encoding.UTF8.GetBytes(value))
            .TrimEnd('=')
            .Replace('+', '-')
            .Replace('/', '_');

    private sealed class CapturingAcquisitionService : ICredentialAcquisitionService
    {
        public CredentialRequestV2? LastRequest { get; private set; }

        public ValueTask<CredentialResult> AcquireAsync(
            CredentialRequestV2 request,
            CancellationToken cancellationToken = default)
        {
            LastRequest = request;
            return ValueTask.FromResult(
                new CredentialResult
                {
                    Status = CredentialResultStatus.InteractionRequired,
                    DiagnosticsCorrelationId = "wp6-capture",
                    Error = new CredentialError
                    {
                        Kind = CredentialErrorKind.InteractionRequired,
                        Code = "SilentAcquisitionUnavailable",
                        SafeMessage = "Interactive login is required.",
                    },
                });
        }
    }

    private sealed class NeverCompletingAcquisitionService : ICredentialAcquisitionService
    {
        private readonly TaskCompletionSource<CredentialResult> completion =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        private CancellationToken cancellationToken;

        public bool CancellationObserved => cancellationToken.IsCancellationRequested;

        public ValueTask<CredentialResult> AcquireAsync(
            CredentialRequestV2 request,
            CancellationToken cancellationToken = default)
        {
            this.cancellationToken = cancellationToken;
            return new ValueTask<CredentialResult>(completion.Task);
        }

        public void Complete() =>
            completion.TrySetResult(
                CredentialAcquisitionResultFactory.Failure(
                    CredentialResultStatus.CredentialUnavailable,
                    CredentialErrorKind.CredentialUnavailable,
                    "TestCompleted",
                    "Test provider completed."));
    }

    private sealed class SynchronouslyBlockingAcquisitionService
        : ICredentialAcquisitionService, IDisposable
    {
        private readonly ManualResetEventSlim entered = new();
        private readonly ManualResetEventSlim release = new();
        private volatile bool completed;

        public bool Started => entered.IsSet;
        public bool Completed => completed;

        public ValueTask<CredentialResult> AcquireAsync(
            CredentialRequestV2 request,
            CancellationToken cancellationToken = default)
        {
            entered.Set();
            release.Wait(CancellationToken.None);
            completed = true;
            return ValueTask.FromResult(
                CredentialAcquisitionResultFactory.Failure(
                    CredentialResultStatus.CredentialUnavailable,
                    CredentialErrorKind.CredentialUnavailable,
                    "BlockingProviderReleased",
                    "The blocking test provider was released."));
        }

        public void Release() => release.Set();

        public void Dispose()
        {
            release.Set();
            entered.Dispose();
            release.Dispose();
        }
    }

    private sealed class CooperativeAcquisitionService(CredentialResult result)
        : ICredentialAcquisitionService
    {
        public async ValueTask<CredentialResult> AcquireAsync(
            CredentialRequestV2 request,
            CancellationToken cancellationToken = default)
        {
            await Task.Yield();
            cancellationToken.ThrowIfCancellationRequested();
            return result;
        }
    }

    private sealed class InteractionCountingIdentityProvider : IIdentityProvider
    {
        public int InteractionCount { get; private set; }

        public IdentityMaterial GetIdentity(CredentialRequest request)
        {
            InteractionCount++;
            return new IdentityMaterial
            {
                Account = "test@example.invalid",
                Tenant = "test-tenant",
                AccessToken = "test-token",
                ExpiresAt = DateTimeOffset.UtcNow.AddHours(1),
            };
        }
    }

    private sealed class TrustedInspector(AzureAuthDeploymentConfig deployment)
        : IAzureAuthArtifactTrustInspector
    {
        public AzureAuthArtifactInspection Inspect(AzureAuthDeploymentConfig config) =>
            AzureAuthArtifactInspection.Trusted(
                new AzureAuthArtifactEvidence
                {
                    CanonicalPath = deployment.ExecutablePath,
                    StableArtifactIdentity = new FileSystemEntryIdentity("wp6-artifact"),
                    Sha256Hash = deployment.ExecutableSha256,
                    SignerIdentity = deployment.SignerIdentity,
                    PublisherName = deployment.PublisherName,
                    ExecutableVersion = deployment.ExecutableVersion,
                    ProvenanceIdentifier = deployment.ProvenanceIdentifier,
                    Owner = new FileSystemOwner("current-user"),
                    CurrentUserOwnsArtifact = true,
                    OwnerOnlyWritable = true,
                    DiscretionaryAclsPresentAndNonNull = true,
                    TrustedExecutableDirectory = @"C:\Tools",
                    ExecutableDirectoryChainHasNoReparsePoints = true,
                    ExecutableDirectoryChainOwnerOnlyWritable = true,
                    TrustedSystemDirectory = @"C:\Windows\System32",
                    SystemDirectoryChainHasNoReparsePoints = true,
                    SystemDirectoryChainOwnerOnlyWritable = true,
                    TrustedWorkingDirectory = @"C:\Windows\System32",
                    TrustedPathEntries = [@"C:\Windows\System32"],
                });
    }

    private sealed class DeferredInspector : IAzureAuthArtifactTrustInspector
    {
        public AzureAuthArtifactInspection Inspect(AzureAuthDeploymentConfig config) =>
            AzureAuthArtifactInspection.Deferred();
    }

    private sealed class CountingTrustInspector(IAzureAuthArtifactTrustInspector inner)
        : IAzureAuthArtifactTrustInspector
    {
        public int CallCount { get; private set; }

        public AzureAuthArtifactInspection Inspect(AzureAuthDeploymentConfig config)
        {
            CallCount++;
            return inner.Inspect(config);
        }
    }

    private sealed class SuccessfulProcessRunner : IProcessRunner
    {
        public int CallCount { get; private set; }

        public async Task<ProcessResult> RunAsync(
            ProcessStartSpec startSpec,
            CancellationToken cancellationToken = default)
        {
            CallCount++;
            if (startSpec.PreStartValidation is not null)
            {
                await startSpec.PreStartValidation(cancellationToken);
            }

            return new ProcessResult(0, CreateJwt() + "\n", string.Empty);
        }
    }
}
