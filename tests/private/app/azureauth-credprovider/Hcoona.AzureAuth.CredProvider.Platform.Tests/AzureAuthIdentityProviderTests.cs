using System.Collections.Concurrent;
using System.Text;
using Hcoona.AzureAuth.CredProvider.Contracts;
using Hcoona.AzureAuth.CredProvider.Contracts.AzureAuthDeployment;
using Hcoona.AzureAuth.CredProvider.Platform.AzureAuthProvider;
using Hcoona.AzureAuth.CredProvider.Platform.CredentialCore;
using Hcoona.AzureAuth.CredProvider.Platform.FileSystem;
using Hcoona.AzureAuth.CredProvider.Platform.Processes;
using Hcoona.AzureAuth.CredProvider.Platform.Tests.TestDoubles;
using Xunit;

namespace Hcoona.AzureAuth.CredProvider.Platform.Tests;

public sealed class AzureAuthIdentityProviderTests
{
    private static readonly DateTimeOffset BoundAt = new(2026, 7, 20, 19, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset Now = new(2026, 7, 20, 20, 0, 0, TimeSpan.Zero);
    private static readonly string[] LaunchEnvironmentKeys =
    [
        "LOCALAPPDATA",
        "OEAUTH_MSAL_DISABLE_CACHE",
        "PATH",
        "SystemRoot",
        "TEMP",
        "TMP",
        "USERPROFILE",
        "WINDIR",
    ];
    private static readonly string[] ClrInjectionEnvironmentKeys =
    [
        "CORECLR_ENABLE_PROFILING",
        "CORECLR_PROFILER",
        "CORECLR_PROFILER_PATH",
        "CORECLR_PROFILER_PATH_32",
        "CORECLR_PROFILER_PATH_64",
        "COR_ENABLE_PROFILING",
        "COR_PROFILER",
        "COR_PROFILER_PATH",
        "COR_PROFILER_PATH_32",
        "COR_PROFILER_PATH_64",
        "DOTNET_STARTUP_HOOKS",
    ];

    public static TheoryData<string> InvalidTokenOutputs =>
        new()
        {
            string.Empty,
            "\n",
            " token\n",
            "token \n",
            "token\nextra\n",
            "token\r\nextra",
            "token\r",
            "tok\u0001en\n",
        };

    public static TheoryData<ProcessResult, AcquiredAccessTokenStatus, string> ProcessFailureCases =>
        new()
        {
            {
                new ProcessResult(1, "secret-token\n", "secret-stderr"),
                AcquiredAccessTokenStatus.ProcessFailed,
                "AzureAuthProcessExitNonZero"
            },
            {
                ProcessResult.LaunchFailure("secret-token", "secret-stderr"),
                AcquiredAccessTokenStatus.ProcessFailed,
                "AzureAuthProcessLaunchFailed"
            },
            {
                ProcessResult.Canceled("secret-token", "secret-stderr"),
                AcquiredAccessTokenStatus.Canceled,
                "AzureAuthProcessCanceled"
            },
            {
                ProcessResult.TimedOut("secret-token", "secret-stderr"),
                AcquiredAccessTokenStatus.TimedOut,
                "AzureAuthProcessTimedOut"
            },
            {
                ProcessResult.OutputTooLarge("secret-token", "secret-stderr"),
                AcquiredAccessTokenStatus.OutputRejected,
                "AzureAuthProcessOutputTooLarge"
            },
            {
                ProcessResult.InvalidOutput("secret-token", "secret-stderr"),
                AcquiredAccessTokenStatus.OutputRejected,
                "AzureAuthProcessOutputInvalid"
            },
        };

    public static TheoryData<CachePolicyMode> CacheDisabledPolicies =>
        new()
        {
            CachePolicyMode.NoCache,
            CachePolicyMode.ProductPersistentCacheDisabled,
            CachePolicyMode.NonPersistentCi,
        };

    [Fact]
    public async Task AcquireAccessTokenAsyncRejectsUnspecifiedAcquisitionModeWithoutTrustOrRunnerCalls()
    {
        var inspector = new CountingInspector(CreateTrustedInspection());
        var runner = new FakeProcessRunner();
        var provider = CreateProvider(inspector, runner);

        AcquiredAccessTokenResult result = await provider.AcquireAccessTokenAsync(
            CreateRequest(acquisitionMode: AcquisitionMode.Unspecified),
            TestContext.Current.CancellationToken
        );

        Assert.Equal(AcquiredAccessTokenStatus.InteractionBlocked, result.Status);
        Assert.Equal("AzureAuthAcquisitionModeRequired", result.Code);
        Assert.Empty(runner.RecordedStartSpecs);
        Assert.Equal(0, inspector.CallCount);
    }

    [Fact]
    public async Task AcquireAccessTokenAsyncReturnsSilentUnavailableWithoutTrustOrRunnerCalls()
    {
        var inspector = new CountingInspector(CreateTrustedInspection());
        var runner = new FakeProcessRunner();
        var provider = CreateProvider(inspector, runner);

        AcquiredAccessTokenResult result = await provider.AcquireAccessTokenAsync(
            CreateRequest(
                interactivePolicy: InteractivePolicy.Never,
                acquisitionMode: AcquisitionMode.SilentOnly),
            TestContext.Current.CancellationToken
        );

        Assert.Equal(AcquiredAccessTokenStatus.InteractionRequired, result.Status);
        Assert.Equal("SilentAcquisitionUnavailable", result.Code);
        Assert.Equal(
            "Silent AzureAuth acquisition is not implemented; use explicit interactive login "
                + "for interactive operations only. No automatic remediation is available.",
            result.SafeMessage);
        Assert.Empty(runner.RecordedStartSpecs);
        Assert.Equal(0, inspector.CallCount);
    }

    [Fact]
    public async Task AcquireAccessTokenAsyncRejectsInvalidInteractionPolicyWithoutRunnerCall()
    {
        var inspector = new CountingInspector(CreateTrustedInspection());
        var runner = new FakeProcessRunner();
        var provider = CreateProvider(inspector, runner);

        AcquiredAccessTokenResult result = await provider.AcquireAccessTokenAsync(
            CreateRequest(interactivePolicy: InteractivePolicy.Never),
            TestContext.Current.CancellationToken
        );

        Assert.Equal(AcquiredAccessTokenStatus.RequestRejected, result.Status);
        Assert.Equal("AzureAuthRequestRejected", result.Code);
        Assert.Empty(runner.RecordedStartSpecs);
        Assert.Equal(0, inspector.CallCount);
    }

    [Fact]
    public async Task AcquireAccessTokenAsyncRejectsExplicitCiInteractionWithoutRunnerCall()
    {
        var inspector = new CountingInspector(CreateTrustedInspection());
        var runner = new FakeProcessRunner();
        var provider = CreateProvider(inspector, runner);

        AcquiredAccessTokenResult result = await provider.AcquireAccessTokenAsync(
            CreateRequest(
                ciContext: new CiContext
                {
                    ExplicitCiMode = true,
                    Provider = CiProviderNames.AzurePipelines,
                    AllowsPersistentWrites = false,
                    HasAzurePipelinesSystemAccessToken = false,
                }
            ),
            TestContext.Current.CancellationToken
        );

        Assert.Equal(AcquiredAccessTokenStatus.RequestRejected, result.Status);
        Assert.Equal("AzureAuthRequestRejected", result.Code);
        Assert.Empty(runner.RecordedStartSpecs);
        Assert.Equal(0, inspector.CallCount);
    }

    [Fact]
    public async Task AcquireAccessTokenAsyncRejectsDeviceCodeWithoutTrustOrRunnerCalls()
    {
        var inspector = new CountingInspector(CreateTrustedInspection());
        var runner = new FakeProcessRunner();
        var provider = CreateProvider(inspector, runner);

        AcquiredAccessTokenResult result = await provider.AcquireAccessTokenAsync(
            CreateRequest(flow: IdentityFlow.DeviceCode),
            TestContext.Current.CancellationToken
        );

        Assert.Equal(AcquiredAccessTokenStatus.RequestRejected, result.Status);
        Assert.Equal("AzureAuthDeviceCodeUnsupported", result.Code);
        Assert.Empty(runner.RecordedStartSpecs);
        Assert.Equal(0, inspector.CallCount);
    }

    [Theory]
    [InlineData("user name@example.com", null)]
    [InlineData("usér@example.com", null)]
    [InlineData("", null)]
    [InlineData(null, "tenant one")]
    [InlineData(null, "ténant")]
    public async Task AcquireAccessTokenAsyncRejectsInvalidHintsWithoutTrustOrRunnerCalls(
        string? accountHint,
        string? tenantHint
    )
    {
        var inspector = new CountingInspector(CreateTrustedInspection());
        var runner = new FakeProcessRunner();
        var provider = CreateProvider(inspector, runner);

        AcquiredAccessTokenResult result = await provider.AcquireAccessTokenAsync(
            CreateRequest(accountHint: accountHint, tenantHint: tenantHint),
            TestContext.Current.CancellationToken
        );

        Assert.Equal(AcquiredAccessTokenStatus.RequestRejected, result.Status);
        Assert.Equal("AzureAuthRequestRejected", result.Code);
        Assert.Empty(runner.RecordedStartSpecs);
        Assert.Equal(0, inspector.CallCount);
    }

    [Theory]
    [MemberData(nameof(CacheDisabledPolicies))]
    public async Task AcquireAccessTokenAsyncDisablesUpstreamMsalCacheForAcceptedPolicies(
        CachePolicyMode cachePolicy
    )
    {
        var inspector = new CountingInspector(CreateTrustedInspection());
        var runner = new FakeProcessRunner();
        runner.EnqueueResult(new ProcessResult(0, CreateJwt(), string.Empty));
        var provider = CreateProvider(inspector, runner);

        AcquiredAccessTokenResult result = await provider.AcquireAccessTokenAsync(
            CreateRequest(cachePolicy: cachePolicy),
            TestContext.Current.CancellationToken
        );

        Assert.Equal(AcquiredAccessTokenStatus.Success, result.Status);
        ProcessStartSpec startSpec = Assert.Single(runner.RecordedStartSpecs);
        Assert.Equal("1", startSpec.Environment["OEAUTH_MSAL_DISABLE_CACHE"]);
    }

    [Fact]
    public async Task AcquireAccessTokenAsyncFailsClosedForFuturePersistentCacheWithoutRunnerCall()
    {
        var inspector = new CountingInspector(CreateTrustedInspection());
        var runner = new FakeProcessRunner();
        var provider = CreateProvider(inspector, runner);

        AcquiredAccessTokenResult result = await provider.AcquireAccessTokenAsync(
            CreateRequest(cachePolicy: CachePolicyMode.FuturePersistentCacheRequested),
            TestContext.Current.CancellationToken
        );

        Assert.Equal(AcquiredAccessTokenStatus.PrerequisiteFailed, result.Status);
        Assert.Equal("AzureAuthPersistentCacheUnsupported", result.Code);
        Assert.Empty(runner.RecordedStartSpecs);
        Assert.Equal(0, inspector.CallCount);
    }

    [Fact]
    public async Task AcquireAccessTokenAsyncRejectsWhenAzureAuthIsNotSelected()
    {
        var inspector = new CountingInspector(CreateTrustedInspection());
        var runner = new FakeProcessRunner();
        AzureAuthProviderConfig config = AzureAuthProviderConfig.CreateDirectMsal();
        AzureAuthBinding binding = AzureAuthBindingPolicy.CreateBound(
            config,
            "user@example.com",
            "tenant-1",
            BoundAt
        );
        var provider = new AzureAuthIdentityProvider(config, binding, CreateLaunchOptions(), inspector, runner);

        AcquiredAccessTokenResult result = await provider.AcquireAccessTokenAsync(
            CreateRequest(),
            TestContext.Current.CancellationToken
        );

        Assert.Equal(AcquiredAccessTokenStatus.PrerequisiteFailed, result.Status);
        Assert.Equal("AzureAuthProviderSelectionMismatch", result.Code);
        Assert.Empty(runner.RecordedStartSpecs);
        Assert.Equal(0, inspector.CallCount);
    }

    [Fact]
    public async Task AcquireAccessTokenAsyncRejectsDeferredTrustWithoutRunnerCall()
    {
        var inspector = new CountingInspector(AzureAuthArtifactInspection.Deferred());
        var runner = new FakeProcessRunner();
        var provider = CreateProvider(inspector, runner);

        AcquiredAccessTokenResult result = await provider.AcquireAccessTokenAsync(
            CreateRequest(),
            TestContext.Current.CancellationToken
        );

        Assert.Equal(AcquiredAccessTokenStatus.PrerequisiteFailed, result.Status);
        Assert.Equal("AzureAuthTrustDeferred", result.Code);
        Assert.Empty(runner.RecordedStartSpecs);
        Assert.Equal(1, inspector.CallCount);
    }

    [Fact]
    public async Task AcquireAccessTokenAsyncRejectsUntrustedDeploymentWithoutRunnerCall()
    {
        var inspector = new CountingInspector(AzureAuthArtifactInspection.Untrusted());
        var runner = new FakeProcessRunner();
        var provider = CreateProvider(inspector, runner);

        AcquiredAccessTokenResult result = await provider.AcquireAccessTokenAsync(
            CreateRequest(),
            TestContext.Current.CancellationToken
        );

        Assert.Equal(AcquiredAccessTokenStatus.PrerequisiteFailed, result.Status);
        Assert.Equal("AzureAuthTrustRejected", result.Code);
        Assert.Empty(runner.RecordedStartSpecs);
        Assert.Equal(1, inspector.CallCount);
    }

    [Fact]
    public async Task AcquireAccessTokenAsyncRejectsConfigurationDriftBeforeRunnerCall()
    {
        var inspector = new CountingInspector(CreateTrustedInspection());
        var runner = new FakeProcessRunner();
        AzureAuthProviderConfig driftedConfig = CreateAzureAuthProviderConfig(
            CreateDeploymentConfig() with { ExecutableVersion = "2.0.0.0" }
        );
        var provider = CreateProvider(
            inspector,
            runner,
            providerConfig: driftedConfig,
            binding: CreateBoundBinding(driftedConfig)
        );

        AcquiredAccessTokenResult result = await provider.AcquireAccessTokenAsync(
            CreateRequest(),
            TestContext.Current.CancellationToken
        );

        Assert.Equal(AcquiredAccessTokenStatus.PrerequisiteFailed, result.Status);
        Assert.Equal("AzureAuthTrustRejected", result.Code);
        Assert.Empty(runner.RecordedStartSpecs);
        Assert.Equal(1, inspector.CallCount);
    }

    [Fact]
    public async Task AcquireAccessTokenAsyncRejectsUnboundBindingWithoutRunnerCall()
    {
        var inspector = new CountingInspector(CreateTrustedInspection());
        var runner = new FakeProcessRunner();
        var provider = CreateProvider(
            inspector,
            runner,
            binding: AzureAuthBindingPolicy.CreateUnbound(BoundAt)
        );

        AcquiredAccessTokenResult result = await provider.AcquireAccessTokenAsync(
            CreateRequest(),
            TestContext.Current.CancellationToken
        );

        Assert.Equal(AcquiredAccessTokenStatus.PrerequisiteFailed, result.Status);
        Assert.Equal("AzureAuthBindingRequired", result.Code);
        Assert.Empty(runner.RecordedStartSpecs);
        Assert.Equal(1, inspector.CallCount);
    }

    [Fact]
    public async Task AcquireAccessTokenAsyncRejectsBindingForDifferentProviderWithoutRunnerCall()
    {
        var inspector = new CountingInspector(CreateTrustedInspection());
        var runner = new FakeProcessRunner();
        AzureAuthBinding binding = AzureAuthBindingPolicy.CreateBound(
            AzureAuthProviderConfig.CreateDirectMsal(),
            "user@example.com",
            "tenant-1",
            BoundAt
        );
        var provider = CreateProvider(inspector, runner, binding: binding);

        AcquiredAccessTokenResult result = await provider.AcquireAccessTokenAsync(
            CreateRequest(),
            TestContext.Current.CancellationToken
        );

        Assert.Equal(AcquiredAccessTokenStatus.PrerequisiteFailed, result.Status);
        Assert.Equal("AzureAuthBindingProviderMismatch", result.Code);
        Assert.Empty(runner.RecordedStartSpecs);
        Assert.Equal(1, inspector.CallCount);
    }

    [Fact]
    public async Task AcquireAccessTokenAsyncRejectsBindingDeploymentMismatchWithoutRunnerCall()
    {
        var inspector = new CountingInspector(CreateTrustedInspection());
        var runner = new FakeProcessRunner();
        AzureAuthProviderConfig config = CreateAzureAuthProviderConfig();
        AzureAuthTrustResult trust = CreateTrustedTrustResult(config.DeploymentConfig!);
        AzureAuthBinding binding = AzureAuthBindingPolicy.CreateBound(
            config,
            "user@example.com",
            "tenant-1",
            BoundAt,
            trust
        ) with
        {
            DeploymentKey = new string('b', 64),
        };
        var provider = new AzureAuthIdentityProvider(
            config,
            binding,
            CreateLaunchOptions(),
            inspector,
            runner
        );

        AcquiredAccessTokenResult result = await provider.AcquireAccessTokenAsync(
            CreateRequest(),
            TestContext.Current.CancellationToken
        );

        Assert.Equal(AcquiredAccessTokenStatus.PrerequisiteFailed, result.Status);
        Assert.Equal("AzureAuthBindingDeploymentMismatch", result.Code);
        Assert.Empty(runner.RecordedStartSpecs);
        Assert.Equal(1, inspector.CallCount);
    }

    [Theory]
    [InlineData("other@example.com", null, "AzureAuthBindingAccountMismatch")]
    [InlineData(null, "tenant-2", "AzureAuthBindingTenantMismatch")]
    public async Task AcquireAccessTokenAsyncRejectsMismatchedHintsWithoutRunnerCall(
        string? accountHint,
        string? tenantHint,
        string expectedCode
    )
    {
        var inspector = new CountingInspector(CreateTrustedInspection());
        var runner = new FakeProcessRunner();
        var provider = CreateProvider(inspector, runner);

        AcquiredAccessTokenResult result = await provider.AcquireAccessTokenAsync(
            CreateRequest(accountHint: accountHint, tenantHint: tenantHint),
            TestContext.Current.CancellationToken
        );

        Assert.Equal(AcquiredAccessTokenStatus.PrerequisiteFailed, result.Status);
        Assert.Equal(expectedCode, result.Code);
        Assert.Empty(runner.RecordedStartSpecs);
        Assert.Equal(1, inspector.CallCount);
    }

    [Fact]
    public async Task AcquireAccessTokenAsyncBuildsExactPinnedAzureAuthStartSpecAndReturnsToken()
    {
        var ambientEnvironment = new Dictionary<string, string?>
        {
            ["ADO_TOKEN"] = Environment.GetEnvironmentVariable("ADO_TOKEN"),
            ["HTTP_PROXY"] = Environment.GetEnvironmentVariable("HTTP_PROXY"),
            ["DOTNET_ROOT"] = Environment.GetEnvironmentVariable("DOTNET_ROOT"),
            ["NODE_OPTIONS"] = Environment.GetEnvironmentVariable("NODE_OPTIONS"),
        };
        Environment.SetEnvironmentVariable("ADO_TOKEN", "ambient-ado-token");
        Environment.SetEnvironmentVariable("HTTP_PROXY", "http://proxy.example");
        Environment.SetEnvironmentVariable("DOTNET_ROOT", @"C:\Injected\DotNet");
        Environment.SetEnvironmentVariable("NODE_OPTIONS", "--inspect");

        try
        {
            var inspector = new CountingInspector(CreateTrustedInspection());
            var runner = new FakeProcessRunner();
            string jwt = CreateJwt();
            runner.EnqueueResult(new ProcessResult(0, jwt + "\r\n", "secret-stderr"));
            AzureAuthProcessLaunchOptions launchOptions = CreateLaunchOptions() with
            {
                Timeout = TimeSpan.FromMinutes(5),
                MaxStandardOutputBytes = 256,
                MaxStandardOutputCharacters = 256,
                MaxStandardErrorBytes = 128,
                MaxStandardErrorCharacters = 128,
            };
            var provider = CreateProvider(inspector, runner, launchOptions: launchOptions);

            AcquiredAccessTokenResult result = await provider.AcquireAccessTokenAsync(
                CreateRequest(),
                TestContext.Current.CancellationToken
            );

            Assert.Equal(1, inspector.CallCount);
            ProcessStartSpec startSpec = Assert.Single(runner.RecordedStartSpecs);
            Assert.Equal(@"C:\Tools\AzureAuth.exe", startSpec.FileName);
            Assert.Equal(
                [
                    "aad",
                    "--client",
                    AzureAuthIdentityProvider.AzureDevOpsPublicClientId,
                    "--tenant",
                    "tenant-1",
                    "--scope",
                    AzureAuthIdentityProvider.AzureDevOpsDefaultScope,
                    "--mode",
                    "web",
                    "--output",
                    "token",
                ],
                startSpec.Arguments
            );
            Assert.Equal(@"C:\Windows\System32", startSpec.WorkingDirectory);
            Assert.Null(startSpec.StandardInput);
            Assert.Equal(ProcessEnvironmentMode.ExplicitOnly, startSpec.EnvironmentMode);
            Assert.True(startSpec.UseWindowsEnvironmentVariableSemantics);
            Assert.True(startSpec.Environment.ContainsKey("systemroot"));
            Assert.Equal(TimeSpan.FromMinutes(5), startSpec.Timeout);
            Assert.Equal(256, startSpec.OutputCaptureOptions.StandardOutputByteLimit);
            Assert.Equal(256, startSpec.OutputCaptureOptions.StandardOutputCharacterLimit);
            Assert.Equal(128, startSpec.OutputCaptureOptions.StandardErrorByteLimit);
            Assert.Equal(128, startSpec.OutputCaptureOptions.StandardErrorCharacterLimit);
            Assert.Equal(
                LaunchEnvironmentKeys,
                startSpec.Environment.Keys.OrderBy(static key => key, StringComparer.Ordinal)
            );
            Assert.Equal(@"C:\Windows", startSpec.Environment["SystemRoot"]);
            Assert.Equal(@"C:\Windows", startSpec.Environment["windir"]);
            Assert.Equal(@"C:\Users\user\AppData\Local\Temp", startSpec.Environment["TEMP"]);
            Assert.Equal(@"C:\Users\user\AppData\Local\Temp", startSpec.Environment["TMP"]);
            Assert.Equal(@"C:\Users\user\AppData\Local", startSpec.Environment["LOCALAPPDATA"]);
            Assert.Equal(@"C:\Users\user", startSpec.Environment["USERPROFILE"]);
            Assert.Equal(@"C:\Windows\System32", startSpec.Environment["PATH"]);
            Assert.Equal("1", startSpec.Environment["OEAUTH_MSAL_DISABLE_CACHE"]);
            Assert.False(startSpec.Environment.ContainsKey("ADO_TOKEN"));
            Assert.False(startSpec.Environment.ContainsKey("HTTP_PROXY"));
            Assert.False(startSpec.Environment.ContainsKey("DOTNET_ROOT"));
            Assert.False(startSpec.Environment.ContainsKey("NODE_OPTIONS"));
            Assert.All(
                ClrInjectionEnvironmentKeys,
                key => Assert.False(startSpec.Environment.ContainsKey(key)));

            Assert.Equal(AcquiredAccessTokenStatus.Success, result.Status);
            AcquiredAccessToken token = Assert.IsType<AcquiredAccessToken>(result.AccessToken);
            Assert.Equal(jwt, token.Token.Value);
            Assert.Null(token.AccountId);
            Assert.Equal("tenant-1", token.TenantId);
            Assert.Equal(CreateTrustedTrustResult(CreateDeploymentConfig()).DeploymentKey, token.DeploymentKey);
            Assert.Equal(Now.AddHours(1), token.ExpiresAt);
        }

        finally
        {
            foreach ((string key, string? value) in ambientEnvironment)
            {
                Environment.SetEnvironmentVariable(key, value);
            }
        }
    }

    [Fact]
    public async Task WslLaunchIncludesOnlyValidatedSnapshottedInteropValue()
    {
        var inspector = new CountingInspector(CreateTrustedInspection());
        var runner = new FakeProcessRunner();
        runner.EnqueueResult(new ProcessResult(0, CreateJwt() + "\n", string.Empty));
        AzureAuthProcessLaunchOptions launchOptions = CreateLaunchOptions() with
        {
            HostContext = AzureAuthLaunchHostContext.WslWindowsInterop,
            WslInterop = "/run/WSL/123_interop",
        };
        var provider = CreateProvider(inspector, runner, launchOptions: launchOptions);

        AcquiredAccessTokenResult result = await provider.AcquireAccessTokenAsync(
            CreateRequest(),
            TestContext.Current.CancellationToken);

        Assert.Equal(AcquiredAccessTokenStatus.Success, result.Status);
        ProcessStartSpec spec = Assert.Single(runner.RecordedStartSpecs);
        Assert.Equal("/mnt/c/Tools/AzureAuth.exe", spec.FileName);
        Assert.Equal("/mnt/c/Windows/System32", spec.WorkingDirectory);
        Assert.Equal("/run/WSL/123_interop", spec.Environment["WSL_INTEROP"]);
        Assert.Equal(
            spec.Environment.Keys
                .Where(static key => key is not "WSLENV" and not "WSL_INTEROP")
                .OrderBy(static key => key, StringComparer.Ordinal),
            spec.Environment["WSLENV"]!
                .Split(':')
                .OrderBy(static key => key, StringComparer.Ordinal));
        Assert.Equal(@"C:\Windows\System32", spec.Environment["PATH"]);
        Assert.Equal("1", spec.Environment["OEAUTH_MSAL_DISABLE_CACHE"]);
        Assert.Equal(string.Empty, spec.Environment["DOTNET_ROOT"]);
        Assert.Equal(string.Empty, spec.Environment["COREHOST_TRACE"]);
        Assert.Equal(string.Empty, spec.Environment["HTTP_PROXY"]);
        Assert.Equal(string.Empty, spec.Environment["AZURE_DEVOPS_EXT_PAT"]);
        Assert.All(
            ClrInjectionEnvironmentKeys,
            key => Assert.Equal(string.Empty, spec.Environment[key]));
        string[] bridged = spec.Environment["WSLENV"]!.Split(':');
        Assert.All(ClrInjectionEnvironmentKeys, key => Assert.Contains(key, bridged));
    }

    [Fact]
    public void LaunchOptionsCannotOverrideEvidenceDerivedHostPaths()
    {
        string[] propertyNames = typeof(AzureAuthProcessLaunchOptions)
            .GetProperties()
            .Select(static property => property.Name)
            .ToArray();

        Assert.DoesNotContain("HostExecutablePath", propertyNames);
        Assert.DoesNotContain("HostWorkingDirectory", propertyNames);
    }

    [Fact]
    public async Task AcquireAccessTokenAsyncRejectsEvidenceWithoutFixedSystemPath()
    {
        AzureAuthArtifactEvidence evidence = CreateMatchingEvidence(CreateDeploymentConfig()) with
        {
            TrustedPathEntries = [],
        };
        var inspector = new CountingInspector(AzureAuthArtifactInspection.Trusted(evidence));
        var runner = new FakeProcessRunner();
        runner.EnqueueResult(new ProcessResult(0, CreateJwt() + "\n", string.Empty));
        AzureAuthProviderConfig config = CreateAzureAuthProviderConfig();
        AzureAuthTrustResult trust = CreateTrustedTrustResult(config.DeploymentConfig!);
        AzureAuthBinding binding = AzureAuthBindingPolicy.CreateBound(
            config,
            "user@example.com",
            "tenant-1",
            BoundAt,
            trust
        );
        var provider = CreateProvider(
            inspector,
            runner,
            providerConfig: config,
            binding: binding
        );

        AcquiredAccessTokenResult result = await provider.AcquireAccessTokenAsync(
            CreateRequest(),
            TestContext.Current.CancellationToken
        );

        Assert.Empty(runner.RecordedStartSpecs);
        Assert.Equal(AcquiredAccessTokenStatus.PrerequisiteFailed, result.Status);
        Assert.Equal("AzureAuthTrustRejected", result.Code);
    }

    [Fact]
    public void TrustedLaunchPathEntriesAreSnapshotted()
    {
        var pathEntries = new List<string> { @"C:\Windows\System32" };
        AzureAuthArtifactEvidence evidence = CreateMatchingEvidence(CreateDeploymentConfig()) with
        {
            TrustedPathEntries = pathEntries,
        };

        pathEntries.Add(@"C:\Injected");

        Assert.Equal([@"C:\Windows\System32"], evidence.TrustedPathEntries);
    }

    [Theory]
    [InlineData("C:")]
    [InlineData(@"C:Windows\System32")]
    [InlineData(@"\Windows\System32")]
    [InlineData(@"C:\")]
    [InlineData(@"Windows\System32")]
    [InlineData(@"C:/Windows/System32")]
    [InlineData(@"\\server\share")]
    [InlineData(@"\\?\C:\Windows\System32")]
    [InlineData(@"C:\Windows\System32:stream")]
    [InlineData(@"C:\Windows\System32.")]
    [InlineData(@"C:\Windows\System32 ")]
    [InlineData(@"C:\Windows;C:\Tools")]
    [InlineData(@"c:\Windows\System32")]
    [InlineData(@"C:\PROGRA~1")]
    [InlineData(@"C:\CON")]
    public void AzureAuthLaunchDirectoryPolicyRejectsNonCanonicalPathEntries(string path)
    {
        Assert.Throws<ArgumentException>(() =>
            AzureAuthWindowsDirectoryPathPolicy.Validate(
                path,
                nameof(AzureAuthProcessLaunchOptions.SystemRoot)
            )
        );
    }

    [Fact]
    public void AzureAuthLaunchDirectoryPolicyAcceptsCanonicalAbsoluteDrivePath()
    {
        AzureAuthWindowsDirectoryPathPolicy.Validate(
            @"C:\Windows\System32",
            nameof(AzureAuthProcessLaunchOptions.SystemRoot)
        );
    }

    [Theory]
    [MemberData(nameof(InvalidTokenOutputs))]
    public async Task AcquireAccessTokenAsyncRejectsInvalidRawTokenOutput(string standardOutput)
    {
        var inspector = new CountingInspector(CreateTrustedInspection());
        var runner = new FakeProcessRunner();
        runner.EnqueueResult(new ProcessResult(0, standardOutput, "secret-stderr"));
        var provider = CreateProvider(inspector, runner);

        AcquiredAccessTokenResult result = await provider.AcquireAccessTokenAsync(
            CreateRequest(),
            TestContext.Current.CancellationToken
        );

        Assert.Equal(AcquiredAccessTokenStatus.OutputRejected, result.Status);
        Assert.Equal("AzureAuthTokenOutputInvalid", result.Code);
        Assert.Null(result.AccessToken);
        Assert.Single(runner.RecordedStartSpecs);
    }

    [Theory]
    [MemberData(nameof(ProcessFailureCases))]
    public async Task AcquireAccessTokenAsyncMapsProcessFailureStatusesWithoutLeakingCapturedSecrets(
        ProcessResult processResult,
        AcquiredAccessTokenStatus expectedStatus,
        string expectedCode
    )
    {
        var inspector = new CountingInspector(CreateTrustedInspection());
        var runner = new FakeProcessRunner();
        runner.EnqueueResult(processResult);
        var provider = CreateProvider(inspector, runner);

        AcquiredAccessTokenResult result = await provider.AcquireAccessTokenAsync(
            CreateRequest(),
            TestContext.Current.CancellationToken
        );

        Assert.Equal(expectedStatus, result.Status);
        Assert.Equal(expectedCode, result.Code);
        Assert.Null(result.AccessToken);
        Assert.DoesNotContain("secret-token", result.SafeMessage!, StringComparison.Ordinal);
        Assert.DoesNotContain("secret-stderr", result.SafeMessage!, StringComparison.Ordinal);
        Assert.DoesNotContain("secret-token", result.ToString(), StringComparison.Ordinal);
        Assert.DoesNotContain("secret-stderr", result.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task AcquireAccessTokenAsyncMapsRunnerExceptionWithoutLeakingExceptionText()
    {
        var inspector = new CountingInspector(CreateTrustedInspection());
        var runner = new FakeProcessRunner();
        runner.EnqueueFailure(new InvalidOperationException("secret-token runner failure"));
        var provider = CreateProvider(inspector, runner);

        AcquiredAccessTokenResult result = await provider.AcquireAccessTokenAsync(
            CreateRequest(),
            TestContext.Current.CancellationToken
        );

        Assert.Equal(AcquiredAccessTokenStatus.Fatal, result.Status);
        Assert.Equal("AzureAuthProviderFailure", result.Code);
        Assert.DoesNotContain("secret-token", result.SafeMessage!, StringComparison.Ordinal);
        Assert.DoesNotContain("secret-token", result.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task AcquireAccessTokenAsyncMapsCallerCancellationAfterRunnerCleanup()
    {
        var inspector = new CountingInspector(CreateTrustedInspection());
        var runner = new FakeProcessRunner();
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(
            TestContext.Current.CancellationToken
        );
        runner.EnqueueHandler(
            (_, token) =>
            {
                cancellation.Cancel();
                return Task.FromCanceled<ProcessResult>(token);
            }
        );
        var provider = CreateProvider(inspector, runner);

        AcquiredAccessTokenResult result = await provider.AcquireAccessTokenAsync(
            CreateRequest(),
            cancellation.Token
        );

        Assert.Equal(AcquiredAccessTokenStatus.Canceled, result.Status);
        Assert.Equal("AzureAuthProcessCanceled", result.Code);
    }

    [Fact]
    public async Task AcquireAccessTokenAsyncUsesDeferredInspectorByDefaultAndNeverLaunches()
    {
        var runner = new FakeProcessRunner();
        var provider = new AzureAuthIdentityProvider(
            CreateAzureAuthProviderConfig(),
            CreateBoundBinding(CreateAzureAuthProviderConfig()),
            CreateLaunchOptions(),
            processRunner: runner
        );

        AcquiredAccessTokenResult result = await provider.AcquireAccessTokenAsync(
            CreateRequest(),
            TestContext.Current.CancellationToken
        );

        Assert.Equal(AcquiredAccessTokenStatus.PrerequisiteFailed, result.Status);
        Assert.Equal("AzureAuthTrustDeferred", result.Code);
        Assert.Empty(runner.RecordedStartSpecs);
    }

    [Fact]
    public async Task AcquireAccessTokenAsyncHandlesConcurrentCallsIndependently()
    {
        var inspector = new CountingInspector(CreateTrustedInspection());
        var runner = new ConcurrentRecordingProcessRunner();
        var provider = CreateProvider(inspector, runner);

        Task<AcquiredAccessTokenResult> first = provider
            .AcquireAccessTokenAsync(
                CreateRequest(flow: IdentityFlow.InteractiveBrowser),
                TestContext.Current.CancellationToken
            )
            .AsTask();
        Task<AcquiredAccessTokenResult> second = provider
            .AcquireAccessTokenAsync(
                CreateRequest(),
                TestContext.Current.CancellationToken
            )
            .AsTask();

        AcquiredAccessTokenResult[] results = await Task.WhenAll(first, second);

        Assert.All(results, static result => Assert.Equal(AcquiredAccessTokenStatus.Success, result.Status));
        string firstToken = Assert.IsType<AcquiredAccessToken>(results[0].AccessToken).Token.Value;
        string secondToken = Assert.IsType<AcquiredAccessToken>(results[1].AccessToken).Token.Value;
        Assert.NotEqual(firstToken, secondToken);
        Assert.Equal(2, runner.StartSpecs.Count);
        Assert.Equal(2, inspector.CallCount);
    }

    private static AzureAuthIdentityProvider CreateProvider(
        IAzureAuthArtifactTrustInspector inspector,
        IProcessRunner runner,
        AzureAuthProviderConfig? providerConfig = null,
        AzureAuthBinding? binding = null,
        AzureAuthProcessLaunchOptions? launchOptions = null
    )
    {
        AzureAuthProviderConfig config = providerConfig ?? CreateAzureAuthProviderConfig();
        return new AzureAuthIdentityProvider(
            config,
            binding ?? CreateBoundBinding(config),
            launchOptions ?? CreateLaunchOptions(),
            inspector,
            runner,
            new FixedTimeProvider(Now)
        );
    }

    private static CredentialRequestV2 CreateRequest(
        IdentityFlow flow = IdentityFlow.InteractiveBrowser,
        InteractivePolicy interactivePolicy = InteractivePolicy.UserAllowed,
        AcquisitionMode acquisitionMode = AcquisitionMode.InteractionAllowed,
        CachePolicyMode cachePolicy = CachePolicyMode.ProductPersistentCacheDisabled,
        CiContext? ciContext = null,
        string? accountHint = null,
        string? tenantHint = null
    ) =>
        new()
        {
            ContractMajor = ContractVersions.CredentialContractV2Major,
            Ecosystem = CredentialEcosystem.Git,
            Operation = CredentialOperation.Get,
            Resource = CanonicalResourceIdentity.Create(
                "dev.azure.com",
                "org",
                new Uri("https://dev.azure.com/org")
            ),
            ServiceIdentity = "default",
            AccountHint = accountHint,
            TenantHint = tenantHint,
            RequestedAudience = TokenAudience.AzureDevOps,
            CredentialKind = CredentialKind.BearerToken,
            IdentityFlow = flow,
            InteractivePolicy = interactivePolicy,
            AcquisitionMode = acquisitionMode,
            CachePolicy = cachePolicy,
            CiContext = ciContext
                ?? new CiContext
                {
                    ExplicitCiMode = false,
                    AllowsPersistentWrites = false,
                },
        };

    private static AzureAuthProcessLaunchOptions CreateLaunchOptions() =>
        new()
        {
            SystemRoot = @"C:\Windows",
            Windir = @"C:\Windows",
            Temp = @"C:\Users\user\AppData\Local\Temp",
            Tmp = @"C:\Users\user\AppData\Local\Temp",
            LocalAppData = @"C:\Users\user\AppData\Local",
            UserProfile = @"C:\Users\user",
            HostContext = AzureAuthLaunchHostContext.WindowsDesktop,
            BrowserInteractionSupported = true,
        };

    private static AzureAuthProviderConfig CreateAzureAuthProviderConfig(
        AzureAuthDeploymentConfig? deploymentConfig = null
    ) => AzureAuthProviderConfig.CreateAzureAuth(deploymentConfig ?? CreateDeploymentConfig());

    private static AzureAuthBinding CreateBoundBinding(AzureAuthProviderConfig config) =>
        AzureAuthBindingPolicy.CreateBound(
            config,
            "user@example.com",
            "tenant-1",
            BoundAt,
            CreateTrustedTrustResult(config.DeploymentConfig!)
        );

    private static AzureAuthDeploymentConfig CreateDeploymentConfig() =>
        new()
        {
            SchemaVersion = ContractVersions.AzureAuthDeploymentConfigSchemaMajor,
            ExecutablePath = @"C:\Tools\AzureAuth.exe",
            ExecutableSha256 = new string('a', 64),
            SignerIdentity = "CN=AzureAuth, O=Hcoona, C=US",
            PublisherName = "Hcoona AzureAuth",
            ExecutableVersion = "1.0.0.0",
            ProvenanceIdentifier = "foundation/wp2",
        };

    private static string CreateJwt(int unique = 0)
    {
        string header = Base64Url("""{"alg":"RS256","typ":"JWT"}""");
        string payload = Base64Url(
            $$"""{"aud":"{{AzureAuthIdentityProvider.AzureDevOpsResourceId}}","tid":"tenant-1","iat":{{Now.AddMinutes(-1).ToUnixTimeSeconds()}},"nbf":{{Now.AddMinutes(-1).ToUnixTimeSeconds()}},"exp":{{Now.AddHours(1).ToUnixTimeSeconds()}},"jti":{{unique}}}""");
        return $"{header}.{payload}.c2lnbmF0dXJl";
    }

    private static string Base64Url(string value) =>
        Convert
            .ToBase64String(Encoding.UTF8.GetBytes(value))
            .TrimEnd('=')
            .Replace('+', '-')
            .Replace('/', '_');

    private static AzureAuthArtifactInspection CreateTrustedInspection() =>
        AzureAuthArtifactInspection.Trusted(CreateMatchingEvidence(CreateDeploymentConfig()));

    private static AzureAuthArtifactEvidence CreateMatchingEvidence(
        AzureAuthDeploymentConfig deploymentConfig
    ) =>
        new()
        {
            CanonicalPath = deploymentConfig.ExecutablePath,
            StableArtifactIdentity = new FileSystemEntryIdentity("artifact-1"),
            Sha256Hash = deploymentConfig.ExecutableSha256,
            SignerIdentity = deploymentConfig.SignerIdentity,
            PublisherName = deploymentConfig.PublisherName,
            ExecutableVersion = deploymentConfig.ExecutableVersion,
            ProvenanceIdentifier = deploymentConfig.ProvenanceIdentifier,
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
        };

    private static AzureAuthTrustResult CreateTrustedTrustResult(
        AzureAuthDeploymentConfig deploymentConfig
    ) =>
        AzureAuthTrustPolicy.Evaluate(
            deploymentConfig,
            AzureAuthArtifactInspection.Trusted(CreateMatchingEvidence(deploymentConfig))
        );

    private sealed class CountingInspector(AzureAuthArtifactInspection inspection)
        : IAzureAuthArtifactTrustInspector
    {
        private readonly AzureAuthArtifactInspection _inspection = inspection;
        private int _callCount;

        public int CallCount => _callCount;

        public AzureAuthArtifactInspection Inspect(
            AzureAuthDeploymentConfig config,
            CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(config);
            Interlocked.Increment(ref _callCount);
            return _inspection;
        }
    }

    private sealed class ConcurrentRecordingProcessRunner : IProcessRunner
    {
        private int _tokenCounter;

        public ConcurrentQueue<ProcessStartSpec> StartSpecs { get; } = new();

        public async Task<ProcessResult> RunAsync(
            ProcessStartSpec startSpec,
            CancellationToken cancellationToken = default
        )
        {
            ArgumentNullException.ThrowIfNull(startSpec);
            if (cancellationToken.IsCancellationRequested)
            {
                return ProcessResult.Canceled(string.Empty, string.Empty);
            }

            StartSpecs.Enqueue(startSpec);
            if (startSpec.PreStartValidation is not null)
            {
                await startSpec.PreStartValidation(cancellationToken).ConfigureAwait(false);
            }

            int tokenIndex = Interlocked.Increment(ref _tokenCounter);
            await Task.Delay(TimeSpan.FromMilliseconds(25), cancellationToken).ConfigureAwait(false);
            return new ProcessResult(0, CreateJwt(tokenIndex) + "\n", "ignored-stderr");
        }
    }

    private sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }
}
