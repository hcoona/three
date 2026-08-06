using System.Text;
using Hcoona.AzureAuth.CredProvider.Contracts;
using Hcoona.AzureAuth.CredProvider.Platform.AzureAuthProvider;
using Hcoona.AzureAuth.CredProvider.Platform.CredentialCore;
using Hcoona.AzureAuth.CredProvider.Platform.Processes;
using Xunit;

namespace Hcoona.AzureAuth.CredProvider.Platform.Tests;

public sealed class AzureAuthIdentityProviderTests
{
    [Fact]
    public async Task SilentOnlyRemainsUnavailableWithoutLaunching()
    {
        var runner = new RecordingRunner(new ProcessResult(0, CreateToken(), ""));
        AzureAuthIdentityProvider provider = CreateProvider(runner);

        AcquiredAccessTokenResult result = await provider.AcquireAccessTokenAsync(
            CreateRequest(AcquisitionMode.SilentOnly, interactivePolicy: InteractivePolicy.Never),
            TestContext.Current.CancellationToken
        );

        Assert.Equal(AcquiredAccessTokenStatus.InteractionRequired, result.Status);
        Assert.Equal("SilentAcquisitionUnavailable", result.Code);
        Assert.Null(runner.StartSpec);
    }

    [Fact]
    public async Task NativeLinuxSilentOnlyClearsAmbientControlsAndUsesExplicitWebMode()
    {
        var runner = new RecordingRunner(new ProcessResult(0, CreateToken(), ""));
        AzureAuthIdentityProvider provider = CreateProvider(
            runner,
            AzureAuthHostPlatform.NativeLinux
        );

        AcquiredAccessTokenResult result = await provider.AcquireAccessTokenAsync(
            CreateRequest(AcquisitionMode.SilentOnly, interactivePolicy: InteractivePolicy.Never),
            TestContext.Current.CancellationToken
        );

        Assert.Equal(AcquiredAccessTokenStatus.Success, result.Status);
        ProcessStartSpec start = Assert.IsType<ProcessStartSpec>(runner.StartSpec);
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
                "--domain",
                "example.com",
                "--output",
                "token",
            ],
            start.Arguments
        );
        Assert.Null(start.Environment["AZUREAUTH_NO_USER"]);
        Assert.Null(start.Environment["AZUREAUTH_MODE"]);
        Assert.Null(start.Environment["Corext_NonInteractive"]);
    }

    [Fact]
    public async Task NativeLinuxInteractiveBrowserClearsInheritedModeControls()
    {
        var runner = new RecordingRunner(new ProcessResult(0, CreateToken(), ""));
        AzureAuthIdentityProvider provider = CreateProvider(
            runner,
            AzureAuthHostPlatform.NativeLinux
        );

        AcquiredAccessTokenResult result = await provider.AcquireAccessTokenAsync(
            CreateRequest(),
            TestContext.Current.CancellationToken
        );

        Assert.Equal(AcquiredAccessTokenStatus.Success, result.Status);
        ProcessStartSpec start = Assert.IsType<ProcessStartSpec>(runner.StartSpec);
        Assert.Equal(["--mode", "web"], start.Arguments.Skip(7).Take(2));
        Assert.Null(start.Environment["AZUREAUTH_MODE"]);
        Assert.Null(start.Environment["AZUREAUTH_NO_USER"]);
        Assert.Null(start.Environment["Corext_NonInteractive"]);
    }

    [Fact]
    public async Task NativeLinuxSilentCacheMissRequiresInteraction()
    {
        AzureAuthIdentityProvider provider = CreateProvider(
            new RecordingRunner(new ProcessResult(1, "", "cache miss")),
            AzureAuthHostPlatform.NativeLinux
        );

        AcquiredAccessTokenResult result = await provider.AcquireAccessTokenAsync(
            CreateRequest(AcquisitionMode.SilentOnly, interactivePolicy: InteractivePolicy.Never),
            TestContext.Current.CancellationToken
        );

        Assert.Equal(AcquiredAccessTokenStatus.InteractionRequired, result.Status);
        Assert.Equal("AzureAuthSilentTokenUnavailable", result.Code);
        Assert.DoesNotContain("cache miss", result.SafeMessage, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(
        AzureAuthHostPlatform.Windows,
        AcquisitionMode.InteractionAllowed,
        InteractivePolicy.UserAllowed
    )]
    [InlineData(
        AzureAuthHostPlatform.Wsl,
        AcquisitionMode.InteractionAllowed,
        InteractivePolicy.UserAllowed
    )]
    [InlineData(
        AzureAuthHostPlatform.NativeLinux,
        AcquisitionMode.SilentOnly,
        InteractivePolicy.Never
    )]
    public async Task LaunchFailureIsNotMappedAsAnOrdinaryAzureAuthExit(
        AzureAuthHostPlatform hostPlatform,
        AcquisitionMode acquisitionMode,
        InteractivePolicy interactivePolicy
    )
    {
        var runner = new RecordingRunner(ProcessResult.LaunchFailure());
        AzureAuthIdentityProvider provider = CreateProvider(runner, hostPlatform);

        AcquiredAccessTokenResult result = await provider.AcquireAccessTokenAsync(
            CreateRequest(acquisitionMode, interactivePolicy: interactivePolicy),
            TestContext.Current.CancellationToken
        );

        Assert.Equal(AcquiredAccessTokenStatus.ProcessFailed, result.Status);
        Assert.Equal("AzureAuthProcessLaunchFailed", result.Code);
        Assert.NotEqual("AzureAuthSilentTokenUnavailable", result.Code);
        Assert.Equal("AzureAuth process launch failed.", result.SafeMessage);
    }

    [Fact]
    public async Task DeviceCodeRemainsUnavailableWithoutLaunching()
    {
        var runner = new RecordingRunner(new ProcessResult(0, CreateToken(), ""));
        AzureAuthIdentityProvider provider = CreateProvider(runner);

        AcquiredAccessTokenResult result = await provider.AcquireAccessTokenAsync(
            CreateRequest(identityFlow: IdentityFlow.DeviceCode),
            TestContext.Current.CancellationToken
        );

        Assert.Equal(AcquiredAccessTokenStatus.RequestRejected, result.Status);
        Assert.Equal("AzureAuthDeviceCodeUnsupported", result.Code);
        Assert.Null(runner.StartSpec);
    }

    [Fact]
    public async Task WslInteractiveLaunchUsesExactArgvDomainAndClearsAmbientControls()
    {
        var runner = new RecordingRunner(new ProcessResult(0, CreateToken() + "\n", ""));
        AzureAuthIdentityProvider provider = CreateProvider(runner);

        AcquiredAccessTokenResult result = await provider.AcquireAccessTokenAsync(
            CreateRequest(accountHint: "USER@example.COM", tenantHint: "TENANT-1"),
            TestContext.Current.CancellationToken
        );

        Assert.Equal(AcquiredAccessTokenStatus.Success, result.Status);
        ProcessStartSpec start = Assert.IsType<ProcessStartSpec>(runner.StartSpec);
        Assert.Equal(
            "/mnt/c/Users/User/AppData/Local/Programs/AzureAuth/0.9.5/azureauth.exe",
            start.FileName
        );
        Assert.Equal(
            [
                "aad",
                "--client",
                AzureAuthIdentityProvider.AzureDevOpsPublicClientId,
                "--tenant",
                "tenant-1",
                "--scope",
                AzureAuthIdentityProvider.AzureDevOpsDefaultScope,
                "--domain",
                "example.com",
                "--output",
                "token",
            ],
            start.Arguments
        );
        Assert.Equal(3, start.Environment.Count);
        Assert.Null(start.Environment["AZUREAUTH_MODE"]);
        Assert.Null(start.Environment["AZUREAUTH_NO_USER"]);
        Assert.Null(start.Environment["Corext_NonInteractive"]);
        Assert.DoesNotContain(
            "OEAUTH_MSAL_DISABLE_CACHE",
            start.Environment.Keys,
            StringComparer.OrdinalIgnoreCase
        );
        Assert.Equal(
            "/mnt/c/Users/User/AppData/Local/Programs/AzureAuth/0.9.5",
            start.WorkingDirectory
        );
        Assert.Null(result.AccessToken!.AccountId);
        Assert.Equal("tenant-1", result.AccessToken.TenantId);
    }

    [Fact]
    public async Task WindowsInteractiveLaunchKeepsBrokerDefaultAndClearsAmbientControls()
    {
        var runner = new RecordingRunner(new ProcessResult(0, CreateToken(), ""));
        AzureAuthIdentityProvider provider = CreateProvider(
            runner,
            AzureAuthHostPlatform.Windows
        );

        AcquiredAccessTokenResult result = await provider.AcquireAccessTokenAsync(
            CreateRequest(),
            TestContext.Current.CancellationToken
        );

        Assert.Equal(AcquiredAccessTokenStatus.Success, result.Status);
        ProcessStartSpec start = Assert.IsType<ProcessStartSpec>(runner.StartSpec);
        Assert.DoesNotContain("--mode", start.Arguments);
        Assert.Equal(3, start.Environment.Count);
        Assert.Null(start.Environment["AZUREAUTH_MODE"]);
        Assert.Null(start.Environment["AZUREAUTH_NO_USER"]);
        Assert.Null(start.Environment["Corext_NonInteractive"]);
    }

    [Theory]
    [InlineData(" ", null)]
    [InlineData(null, "\t")]
    public async Task WhitespaceOnlyHintsReturnRequestRejectedWithoutLaunching(
        string? accountHint,
        string? tenantHint
    )
    {
        var runner = new RecordingRunner(new ProcessResult(0, CreateToken(), ""));
        AzureAuthIdentityProvider provider = CreateProvider(runner);

        AcquiredAccessTokenResult result = await provider.AcquireAccessTokenAsync(
            CreateRequest(accountHint: accountHint, tenantHint: tenantHint),
            TestContext.Current.CancellationToken
        );

        Assert.Equal(AcquiredAccessTokenStatus.RequestRejected, result.Status);
        Assert.Equal("AzureAuthRequestRejected", result.Code);
        Assert.Equal("AzureAuth rejected the credential request.", result.SafeMessage);
        Assert.Equal(0, runner.InvocationCount);
        Assert.Null(runner.StartSpec);
    }

    [Fact]
    public async Task AccountWithoutDomainDoesNotAddDomainArgument()
    {
        var runner = new RecordingRunner(new ProcessResult(0, CreateToken(), ""));
        AzureAuthProviderConfig config = AzureAuthProviderConfig.CreateAzureAuth();
        AzureAuthBinding binding = AzureAuthBindingPolicy.CreateBound(
            config,
            "account-id",
            "tenant-1",
            DateTimeOffset.UtcNow
        );
        var provider = new AzureAuthIdentityProvider(
            config,
            binding,
            CreateLaunchOptions(),
            runner
        );

        AcquiredAccessTokenResult result = await provider.AcquireAccessTokenAsync(
            CreateRequest(),
            TestContext.Current.CancellationToken
        );

        Assert.Equal(AcquiredAccessTokenStatus.Success, result.Status);
        Assert.DoesNotContain("--domain", runner.StartSpec!.Arguments);
    }

    [Theory]
    [InlineData("")]
    [InlineData(" token")]
    [InlineData("token ")]
    [InlineData("token extra")]
    [InlineData("token\nextra")]
    public async Task InvalidRawTokenOutputIsRejected(string output)
    {
        AzureAuthIdentityProvider provider = CreateProvider(
            new RecordingRunner(new ProcessResult(0, output, "secret stderr"))
        );

        AcquiredAccessTokenResult result = await provider.AcquireAccessTokenAsync(
            CreateRequest(),
            TestContext.Current.CancellationToken
        );

        Assert.Equal(AcquiredAccessTokenStatus.OutputRejected, result.Status);
        Assert.DoesNotContain("secret", result.SafeMessage, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task OpaqueTokenIsAcceptedWithoutClaimValidation()
    {
        AcquiredAccessTokenResult result = await CreateProvider(
                new RecordingRunner(new ProcessResult(0, "opaque-token", ""))
            )
            .AcquireAccessTokenAsync(CreateRequest(), TestContext.Current.CancellationToken);

        Assert.Equal(AcquiredAccessTokenStatus.Success, result.Status);
        Assert.Equal("opaque-token", result.AccessToken!.Token.Value);
        Assert.Null(result.AccessToken.ExpiresAt);
    }

    [Theory]
    [InlineData(ProcessExecutionStatus.NonZeroExit, "AzureAuthProcessExitNonZero")]
    [InlineData(ProcessExecutionStatus.TimedOut, "AzureAuthProcessTimedOut")]
    [InlineData(ProcessExecutionStatus.OutputTooLarge, "AzureAuthProcessOutputTooLarge")]
    [InlineData(ProcessExecutionStatus.InvalidOutput, "AzureAuthProcessOutputInvalid")]
    [InlineData(ProcessExecutionStatus.LaunchFailure, "AzureAuthProcessLaunchFailed")]
    public async Task ProcessFailuresMapToActionableCodes(
        ProcessExecutionStatus status,
        string code
    )
    {
        ProcessResult processResult = status switch
        {
            ProcessExecutionStatus.NonZeroExit => new ProcessResult(1, "", "secret"),
            ProcessExecutionStatus.TimedOut => ProcessResult.TimedOut("", "secret"),
            ProcessExecutionStatus.OutputTooLarge => ProcessResult.OutputTooLarge("", "secret"),
            ProcessExecutionStatus.InvalidOutput => ProcessResult.InvalidOutput("", "secret"),
            ProcessExecutionStatus.LaunchFailure => ProcessResult.LaunchFailure("", "secret"),
            _ => throw new ArgumentOutOfRangeException(nameof(status)),
        };

        AcquiredAccessTokenResult result = await CreateProvider(new RecordingRunner(processResult))
            .AcquireAccessTokenAsync(CreateRequest(), TestContext.Current.CancellationToken);

        Assert.Equal(code, result.Code);
        Assert.DoesNotContain("secret", result.SafeMessage, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task CancellationDoesNotLaunchWhenAlreadyCanceled()
    {
        var runner = new RecordingRunner(new ProcessResult(0, CreateToken(), ""));
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        AcquiredAccessTokenResult result = await CreateProvider(runner)
            .AcquireAccessTokenAsync(CreateRequest(), cancellation.Token);

        Assert.Equal(AcquiredAccessTokenStatus.Canceled, result.Status);
        Assert.Null(runner.StartSpec);
    }

    [Fact]
    public async Task RawTokenSuccessOmitsAccountIdAndPreservesExplicitTenant()
    {
        DateTimeOffset expectedExpiration = DateTimeOffset.FromUnixTimeSeconds(1_900_000_000);
        string token = CreateToken(expectedExpiration);
        var runner = new RecordingRunner(new ProcessResult(0, token, ""));
        AzureAuthProviderConfig config = AzureAuthProviderConfig.CreateAzureAuth();
        AzureAuthBinding binding = AzureAuthBindingPolicy.CreateBound(
            config,
            "bound-account@example.test",
            "tenant-explicit-001",
            DateTimeOffset.UtcNow
        );
        var provider = new AzureAuthIdentityProvider(
            config,
            binding,
            CreateLaunchOptions(),
            runner
        );

        AcquiredAccessTokenResult result = await provider.AcquireAccessTokenAsync(
            CreateRequest(tenantHint: "tenant-explicit-001"),
            TestContext.Current.CancellationToken
        );

        Assert.Equal(AcquiredAccessTokenStatus.Success, result.Status);
        Assert.Equal(token, result.AccessToken!.Token.Value);
        Assert.Null(result.AccessToken.AccountId);
        Assert.Equal("tenant-explicit-001", result.AccessToken.TenantId);
        Assert.Equal(expectedExpiration, result.AccessToken.ExpiresAt);
        Assert.Equal(1, runner.InvocationCount);
        Assert.Equal(
            [
                "aad",
                "--client",
                AzureAuthIdentityProvider.AzureDevOpsPublicClientId,
                "--tenant",
                "tenant-explicit-001",
                "--scope",
                AzureAuthIdentityProvider.AzureDevOpsDefaultScope,
                "--domain",
                "example.test",
                "--output",
                "token",
            ],
            runner.StartSpec!.Arguments
        );
    }

    private static AzureAuthIdentityProvider CreateProvider(
        IProcessRunner runner,
        AzureAuthHostPlatform hostPlatform = AzureAuthHostPlatform.Wsl
    )
    {
        AzureAuthProviderConfig config = AzureAuthProviderConfig.CreateAzureAuth();
        AzureAuthBinding binding = AzureAuthBindingPolicy.CreateBound(
            config,
            "user@example.com",
            "tenant-1",
            DateTimeOffset.UtcNow
        );
        return new AzureAuthIdentityProvider(
            config,
            binding,
            CreateLaunchOptions(hostPlatform),
            runner
        );
    }

    private static AzureAuthProcessLaunchOptions CreateLaunchOptions(
        AzureAuthHostPlatform hostPlatform = AzureAuthHostPlatform.Wsl
    ) =>
        new()
        {
            ExecutablePath =
                hostPlatform switch
                {
                    AzureAuthHostPlatform.Windows =>
                        @"C:\Users\User\AppData\Local\Programs\AzureAuth\0.9.5\azureauth.exe",
                    AzureAuthHostPlatform.NativeLinux => "/usr/lib/azureauth/azureauth",
                    _ =>
                        "/mnt/c/Users/User/AppData/Local/Programs/AzureAuth/0.9.5/azureauth.exe",
                },
            WorkingDirectory =
                hostPlatform switch
                {
                    AzureAuthHostPlatform.Windows =>
                        @"C:\Users\User\AppData\Local\Programs\AzureAuth\0.9.5",
                    AzureAuthHostPlatform.NativeLinux => "/usr/lib/azureauth",
                    _ => "/mnt/c/Users/User/AppData/Local/Programs/AzureAuth/0.9.5",
                },
            HostPlatform = hostPlatform,
        };

    private static CredentialRequestV2 CreateRequest(
        AcquisitionMode mode = AcquisitionMode.InteractionAllowed,
        string? accountHint = null,
        string? tenantHint = null,
        IdentityFlow identityFlow = IdentityFlow.InteractiveBrowser,
        InteractivePolicy interactivePolicy = InteractivePolicy.UserAllowed
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
            IdentityFlow = identityFlow,
            InteractivePolicy = interactivePolicy,
            AcquisitionMode = mode,
            CachePolicy = CachePolicyMode.ProductPersistentCacheDisabled,
            CiContext = new CiContext { ExplicitCiMode = false, AllowsPersistentWrites = false },
        };

    private static string CreateToken()
    {
        string header = Base64Url("""{"alg":"RS256"}""");
        string payload = Base64Url(
            $$"""{"exp":{{DateTimeOffset.UtcNow.AddHours(1).ToUnixTimeSeconds()}}}"""
        );
        return $"{header}.{payload}.signature";
    }

    private static string CreateToken(DateTimeOffset expiration)
    {
        string header = Base64Url("""{"alg":"RS256"}""");
        string payload = Base64Url($$"""{"exp":{{expiration.ToUnixTimeSeconds()}}}""");
        return $"{header}.{payload}.signature";
    }

    private static string Base64Url(string value) =>
        Convert
            .ToBase64String(Encoding.UTF8.GetBytes(value))
            .TrimEnd('=')
            .Replace('+', '-')
            .Replace('/', '_');

    private sealed class RecordingRunner(ProcessResult result) : IProcessRunner
    {
        public ProcessStartSpec? StartSpec { get; private set; }

        public int InvocationCount { get; private set; }

        public Task<ProcessResult> RunAsync(
            ProcessStartSpec startSpec,
            CancellationToken cancellationToken = default
        )
        {
            cancellationToken.ThrowIfCancellationRequested();
            InvocationCount++;
            StartSpec = startSpec;
            return Task.FromResult(result);
        }
    }

    [Theory]
    [InlineData(AzureAuthHostPlatform.Windows)]
    [InlineData(AzureAuthHostPlatform.Wsl)]
    public async Task AcquireAccessTokenAsyncRejectsUnsupportedDeviceCodePlatformBeforeLaunch(
        AzureAuthHostPlatform hostPlatform
    )
    {
        var promptWriter = new StringWriter();
        var runner = new RecordingRunner(new ProcessResult(0, CreateToken(), ""));
        AzureAuthIdentityProvider provider = CreateDeviceCodeProvider(
            runner,
            hostPlatform,
            promptWriter
        );

        AcquiredAccessTokenResult result = await provider.AcquireAccessTokenAsync(
            CreateDeviceCodeRequest(
                AcquisitionMode.InteractionAllowed,
                InteractivePolicy.UserAllowed
            ),
            TestContext.Current.CancellationToken
        );

        Assert.Equal(AcquiredAccessTokenStatus.RequestRejected, result.Status);
        Assert.Equal("AzureAuthDeviceCodeUnsupported", result.Code);
        Assert.Equal(
            "AzureAuth device-code login requires an explicit interactive native Linux request.",
            result.SafeMessage
        );
        Assert.Null(result.AccessToken);
        Assert.Equal(0, runner.InvocationCount);
        Assert.Null(runner.StartSpec);
        Assert.Equal(string.Empty, promptWriter.ToString());
    }

    [Fact]
    public async Task AcquireAccessTokenAsyncRejectsSilentOnlyDeviceCodeBeforeLaunch()
    {
        var promptWriter = new StringWriter();
        var runner = new RecordingRunner(new ProcessResult(0, CreateToken(), ""));
        AzureAuthIdentityProvider provider = CreateDeviceCodeProvider(
            runner,
            AzureAuthHostPlatform.NativeLinux,
            promptWriter
        );

        AcquiredAccessTokenResult result = await provider.AcquireAccessTokenAsync(
            CreateDeviceCodeRequest(AcquisitionMode.SilentOnly, InteractivePolicy.UserAllowed),
            TestContext.Current.CancellationToken
        );

        Assert.Equal(AcquiredAccessTokenStatus.RequestRejected, result.Status);
        Assert.Equal("AzureAuthRequestRejected", result.Code);
        Assert.Equal("AzureAuth rejected the credential request.", result.SafeMessage);
        Assert.Null(result.AccessToken);
        Assert.Equal(0, runner.InvocationCount);
        Assert.Null(runner.StartSpec);
        Assert.Equal(string.Empty, promptWriter.ToString());
    }

    [Fact]
    public async Task AcquireAccessTokenAsyncRejectsHostToolAllowsDeviceCodeBeforeLaunch()
    {
        var promptWriter = new StringWriter();
        var runner = new RecordingRunner(new ProcessResult(0, CreateToken(), ""));
        AzureAuthIdentityProvider provider = CreateDeviceCodeProvider(
            runner,
            AzureAuthHostPlatform.NativeLinux,
            promptWriter
        );

        AcquiredAccessTokenResult result = await provider.AcquireAccessTokenAsync(
            CreateDeviceCodeRequest(
                AcquisitionMode.InteractionAllowed,
                InteractivePolicy.HostToolAllows
            ),
            TestContext.Current.CancellationToken
        );

        Assert.Equal(AcquiredAccessTokenStatus.RequestRejected, result.Status);
        Assert.Equal("AzureAuthDeviceCodeUnsupported", result.Code);
        Assert.Null(result.AccessToken);
        Assert.Equal(0, runner.InvocationCount);
        Assert.Null(runner.StartSpec);
        Assert.Equal(string.Empty, promptWriter.ToString());
    }

    private static AzureAuthIdentityProvider CreateDeviceCodeProvider(
        IProcessRunner runner,
        AzureAuthHostPlatform hostPlatform,
        TextWriter promptWriter
    )
    {
        AzureAuthProviderConfig config = AzureAuthProviderConfig.CreateAzureAuth();
        AzureAuthBinding binding = AzureAuthBindingPolicy.CreateBound(
            config,
            "device.user@example.com",
            "tenant-device",
            DateTimeOffset.UtcNow
        );
        return new AzureAuthIdentityProvider(
            config,
            binding,
            CreateLaunchOptions(hostPlatform),
            runner,
            promptWriter
        );
    }

    private static CredentialRequestV2 CreateDeviceCodeRequest(
        AcquisitionMode acquisitionMode,
        InteractivePolicy interactivePolicy
    ) =>
        CreateRequest(
            acquisitionMode,
            accountHint: "device.user@example.com",
            tenantHint: "tenant-device",
            identityFlow: IdentityFlow.DeviceCode,
            interactivePolicy: interactivePolicy
        ) with
        {
            CredentialKind = CredentialKind.BasicPassword,
        };

    [Fact]
    public async Task AcquireAccessTokenAsyncAcceptsNativeLinuxDeviceCodeWithWriter()
    {
        const string Token = "phase2-device-token";
        var promptWriter = new StringWriter();
        var runner = new RecordingRunner(new ProcessResult(0, Token, string.Empty));
        AzureAuthIdentityProvider provider = CreatePhase2DeviceCodeProvider(runner, promptWriter);

        AcquiredAccessTokenResult result = await provider.AcquireAccessTokenAsync(
            CreateDeviceCodeRequest(
                AcquisitionMode.InteractionAllowed,
                InteractivePolicy.UserAllowed
            ),
            TestContext.Current.CancellationToken
        );

        Assert.Equal(AcquiredAccessTokenStatus.Success, result.Status);
        Assert.Equal(Token, result.AccessToken?.Token.Value);
        Assert.Equal("tenant-device", result.AccessToken?.TenantId);
        Assert.Equal(1, runner.InvocationCount);
        ProcessStartSpec start = Assert.IsType<ProcessStartSpec>(runner.StartSpec);
        Assert.Equal(
            [
                "aad",
                "--client",
                AzureAuthIdentityProvider.AzureDevOpsPublicClientId,
                "--tenant",
                "tenant-device",
                "--scope",
                AzureAuthIdentityProvider.AzureDevOpsDefaultScope,
                "--mode",
                "devicecode",
                "--domain",
                "example.com",
                "--output",
                "token",
            ],
            start.Arguments
        );
        Assert.Equal("/usr/lib/azureauth/azureauth", start.FileName);
        Assert.Equal("/usr/lib/azureauth", start.WorkingDirectory);
        Assert.Equal(3, start.Environment.Count);
        Assert.True(start.Environment.ContainsKey("AZUREAUTH_MODE"));
        Assert.True(start.Environment.ContainsKey("AZUREAUTH_NO_USER"));
        Assert.True(start.Environment.ContainsKey("Corext_NonInteractive"));
        Assert.All(start.Environment.Values, Assert.Null);
        Assert.Same(promptWriter, start.StandardErrorTee);
        Assert.Equal(string.Empty, promptWriter.ToString());
    }

    [Fact]
    public async Task AcquireAccessTokenAsyncWithoutPromptWriterBlocksBeforeLaunch()
    {
        var runner = new RecordingRunner(
            new ProcessResult(0, "must-not-launch-token", string.Empty)
        );
        AzureAuthProviderConfig config = AzureAuthProviderConfig.CreateAzureAuth();
        AzureAuthBinding binding = AzureAuthBindingPolicy.CreateBound(
            config,
            "device.user@example.com",
            "tenant-device",
            DateTimeOffset.UtcNow
        );
        var provider = new AzureAuthIdentityProvider(
            config,
            binding,
            CreateLaunchOptions(AzureAuthHostPlatform.NativeLinux),
            runner,
            deviceCodePromptWriter: null
        );

        AcquiredAccessTokenResult result = await provider.AcquireAccessTokenAsync(
            CreateDeviceCodeRequest(
                AcquisitionMode.InteractionAllowed,
                InteractivePolicy.UserAllowed
            ),
            TestContext.Current.CancellationToken
        );

        Assert.Equal(AcquiredAccessTokenStatus.InteractionBlocked, result.Status);
        Assert.NotEqual("ProtocolViolation", result.Code);
        Assert.Equal("AzureAuthDeviceCodePromptUnavailable", result.Code);
        Assert.Equal(
            "Native Linux device-code login requires an attached human prompt stream.",
            result.SafeMessage
        );
        Assert.Null(result.AccessToken);
        Assert.Equal(0, runner.InvocationCount);
        Assert.Null(runner.StartSpec);
    }

    [Theory]
    [InlineData("device.user@example.com", true)]
    [InlineData("device-user", false)]
    public async Task NativeLinuxDeviceCodeUsesExactArgv(string accountId, bool hasDomain)
    {
        var promptWriter = new StringWriter();
        var runner = new RecordingRunner(new ProcessResult(0, "exact-argv-token", string.Empty));
        AzureAuthIdentityProvider provider = CreatePhase2DeviceCodeProvider(
            runner,
            promptWriter,
            accountId: accountId
        );

        AcquiredAccessTokenResult result = await provider.AcquireAccessTokenAsync(
            CreateDeviceCodeRequest(
                AcquisitionMode.InteractionAllowed,
                InteractivePolicy.UserAllowed
            ) with
            {
                AccountHint = accountId,
            },
            TestContext.Current.CancellationToken
        );

        Assert.Equal(AcquiredAccessTokenStatus.Success, result.Status);
        ProcessStartSpec start = Assert.IsType<ProcessStartSpec>(runner.StartSpec);
        var expectedArguments = new List<string>
        {
            "aad",
            "--client",
            AzureAuthIdentityProvider.AzureDevOpsPublicClientId,
            "--tenant",
            "tenant-device",
            "--scope",
            AzureAuthIdentityProvider.AzureDevOpsDefaultScope,
            "--mode",
            "devicecode",
        };
        if (hasDomain)
        {
            expectedArguments.Add("--domain");
            expectedArguments.Add("example.com");
        }
        expectedArguments.Add("--output");
        expectedArguments.Add("token");
        Assert.Equal(expectedArguments, start.Arguments);
        Assert.Equal(hasDomain, start.Arguments.Contains("--domain", StringComparer.Ordinal));
        Assert.Equal("/usr/lib/azureauth/azureauth", start.FileName);
        Assert.Equal("/usr/lib/azureauth", start.WorkingDirectory);
        Assert.Equal(3, start.Environment.Count);
        Assert.True(start.Environment.ContainsKey("AZUREAUTH_MODE"));
        Assert.True(start.Environment.ContainsKey("AZUREAUTH_NO_USER"));
        Assert.True(start.Environment.ContainsKey("Corext_NonInteractive"));
        Assert.All(start.Environment.Values, Assert.Null);
        Assert.Equal(1, runner.InvocationCount);
    }

    [Fact]
    public async Task NativeLinuxDeviceCodeAttachesOnlyStderrTeeAndCaptureLimits()
    {
        const string Token = "private-bounded-token";
        var promptWriter = new StringWriter();
        var runner = new RecordingRunner(new ProcessResult(0, Token, string.Empty));
        AzureAuthIdentityProvider provider = CreatePhase2DeviceCodeProvider(
            runner,
            promptWriter,
            maxStandardOutputBytes: 137,
            maxStandardErrorBytes: 251
        );

        AcquiredAccessTokenResult result = await provider.AcquireAccessTokenAsync(
            CreateDeviceCodeRequest(
                AcquisitionMode.InteractionAllowed,
                InteractivePolicy.UserAllowed
            ),
            TestContext.Current.CancellationToken
        );

        Assert.Equal(AcquiredAccessTokenStatus.Success, result.Status);
        ProcessStartSpec start = Assert.IsType<ProcessStartSpec>(runner.StartSpec);
        Assert.Same(promptWriter, start.StandardErrorTee);
        Assert.Equal(137, start.OutputCaptureOptions.StandardOutputByteLimit);
        Assert.Equal(251, start.OutputCaptureOptions.StandardErrorByteLimit);
        Assert.Equal(Token, result.AccessToken?.Token.Value);
        Assert.DoesNotContain(Token, promptWriter.ToString(), StringComparison.Ordinal);
        Assert.Equal(string.Empty, promptWriter.ToString());
    }

    [Fact]
    public async Task NativeLinuxSilentBrowserNeverAttachesPromptTee()
    {
        const string Token = "silent-browser-token";
        var promptWriter = new StringWriter();
        var runner = new RecordingRunner(new ProcessResult(0, Token, string.Empty));
        AzureAuthIdentityProvider provider = CreatePhase2DeviceCodeProvider(runner, promptWriter);

        AcquiredAccessTokenResult result = await provider.AcquireAccessTokenAsync(
            CreateRequest(
                AcquisitionMode.SilentOnly,
                accountHint: "device.user@example.com",
                tenantHint: "tenant-device",
                identityFlow: IdentityFlow.InteractiveBrowser,
                interactivePolicy: InteractivePolicy.Never
            ),
            TestContext.Current.CancellationToken
        );

        Assert.Equal(AcquiredAccessTokenStatus.Success, result.Status);
        ProcessStartSpec start = Assert.IsType<ProcessStartSpec>(runner.StartSpec);
        Assert.Null(start.StandardErrorTee);
        Assert.Equal(["--mode", "web"], start.Arguments.Skip(7).Take(2));
        Assert.Null(start.Environment["AZUREAUTH_NO_USER"]);
        Assert.Null(start.Environment["AZUREAUTH_MODE"]);
        Assert.Null(start.Environment["Corext_NonInteractive"]);
        Assert.Equal(Token, result.AccessToken?.Token.Value);
        Assert.Equal(string.Empty, promptWriter.ToString());
    }

    [Theory]
    [InlineData(
        ProcessExecutionStatus.NonZeroExit,
        AcquiredAccessTokenStatus.ProcessFailed,
        "AzureAuthProcessExitNonZero",
        "AzureAuth did not return a token."
    )]
    [InlineData(
        ProcessExecutionStatus.TimedOut,
        AcquiredAccessTokenStatus.TimedOut,
        "AzureAuthProcessTimedOut",
        "AzureAuth token acquisition timed out."
    )]
    [InlineData(
        ProcessExecutionStatus.OutputTooLarge,
        AcquiredAccessTokenStatus.OutputRejected,
        "AzureAuthProcessOutputTooLarge",
        "AzureAuth process output exceeded the configured limit."
    )]
    [InlineData(
        ProcessExecutionStatus.InvalidOutput,
        AcquiredAccessTokenStatus.OutputRejected,
        "AzureAuthProcessOutputInvalid",
        "AzureAuth process output was invalid."
    )]
    [InlineData(
        ProcessExecutionStatus.LaunchFailure,
        AcquiredAccessTokenStatus.ProcessFailed,
        "AzureAuthProcessLaunchFailed",
        "AzureAuth process launch failed."
    )]
    public async Task DeviceCodeProcessFailuresMapToExistingActionableCodes(
        ProcessExecutionStatus processStatus,
        AcquiredAccessTokenStatus expectedStatus,
        string expectedCode,
        string expectedSafeMessage
    )
    {
        const string Secret = "arbitrary-azureauth-stderr-secret";
        ProcessResult processResult = processStatus switch
        {
            ProcessExecutionStatus.NonZeroExit => new ProcessResult(23, string.Empty, Secret),
            ProcessExecutionStatus.TimedOut => ProcessResult.TimedOut(string.Empty, Secret),
            ProcessExecutionStatus.OutputTooLarge => ProcessResult.OutputTooLarge(
                "partial-private-token",
                Secret
            ),
            ProcessExecutionStatus.InvalidOutput => ProcessResult.InvalidOutput(
                string.Empty,
                Secret
            ),
            ProcessExecutionStatus.LaunchFailure => ProcessResult.LaunchFailure(
                string.Empty,
                Secret
            ),
            _ => throw new ArgumentOutOfRangeException(nameof(processStatus)),
        };
        var promptWriter = new StringWriter();
        var runner = new RecordingRunner(processResult);
        AzureAuthIdentityProvider provider = CreatePhase2DeviceCodeProvider(
            runner,
            promptWriter,
            maxStandardOutputBytes: 137,
            maxStandardErrorBytes: 251
        );

        AcquiredAccessTokenResult result = await provider.AcquireAccessTokenAsync(
            CreateDeviceCodeRequest(
                AcquisitionMode.InteractionAllowed,
                InteractivePolicy.UserAllowed
            ),
            TestContext.Current.CancellationToken
        );

        Assert.Equal(expectedStatus, result.Status);
        Assert.Equal(expectedCode, result.Code);
        Assert.Equal(expectedSafeMessage, result.SafeMessage);
        Assert.Null(result.AccessToken);
        Assert.DoesNotContain(Secret, result.SafeMessage, StringComparison.Ordinal);
        Assert.Equal(1, runner.InvocationCount);
        ProcessStartSpec start = Assert.IsType<ProcessStartSpec>(runner.StartSpec);
        Assert.Equal(137, start.OutputCaptureOptions.StandardOutputByteLimit);
        Assert.Equal(251, start.OutputCaptureOptions.StandardErrorByteLimit);
    }

    [Fact]
    public async Task NativeLinuxDeviceCodeCancellationMapsSafely()
    {
        var promptWriter = new StringWriter();
        var runner = new CancelableDeviceCodeRunner();
        AzureAuthIdentityProvider provider = CreatePhase2DeviceCodeProvider(runner, promptWriter);
        using var cancellation = new CancellationTokenSource();

        Task<AcquiredAccessTokenResult> acquisition = provider
            .AcquireAccessTokenAsync(
                CreateDeviceCodeRequest(
                    AcquisitionMode.InteractionAllowed,
                    InteractivePolicy.UserAllowed
                ),
                cancellation.Token
            )
            .AsTask();
        Task firstCompletion = await Task.WhenAny(runner.PromptWritten.Task, acquisition)
            .WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
        Assert.Same(runner.PromptWritten.Task, firstCompletion);
        Assert.False(acquisition.IsCompleted);

        cancellation.Cancel();
        AcquiredAccessTokenResult result = await acquisition;

        Assert.Equal(AcquiredAccessTokenStatus.Canceled, result.Status);
        Assert.Equal("AzureAuthProcessCanceled", result.Code);
        Assert.Equal("AzureAuth token acquisition was canceled.", result.SafeMessage);
        Assert.Null(result.AccessToken);
        Assert.Equal(1, runner.InvocationCount);
        Assert.Equal(CancelableDeviceCodeRunner.Prompt, promptWriter.ToString());
        Assert.DoesNotContain("token", promptWriter.ToString(), StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("token\tvalue")]
    [InlineData("token\r\nsecond-line")]
    [InlineData("token\0value")]
    public async Task NativeLinuxDeviceCodeRejectsInvalidTokenOutput(string processOutput)
    {
        var promptWriter = new StringWriter();
        var runner = new RecordingRunner(
            new ProcessResult(0, processOutput, "arbitrary-diagnostic-secret")
        );
        AzureAuthIdentityProvider provider = CreatePhase2DeviceCodeProvider(runner, promptWriter);

        AcquiredAccessTokenResult result = await provider.AcquireAccessTokenAsync(
            CreateDeviceCodeRequest(
                AcquisitionMode.InteractionAllowed,
                InteractivePolicy.UserAllowed
            ),
            TestContext.Current.CancellationToken
        );

        Assert.Equal(AcquiredAccessTokenStatus.OutputRejected, result.Status);
        Assert.Equal("AzureAuthTokenOutputInvalid", result.Code);
        Assert.Equal("AzureAuth token output was invalid.", result.SafeMessage);
        Assert.Null(result.AccessToken);
        Assert.Equal(1, runner.InvocationCount);
        Assert.Equal(string.Empty, promptWriter.ToString());
        Assert.DoesNotContain(
            "arbitrary-diagnostic-secret",
            result.SafeMessage,
            StringComparison.Ordinal
        );
    }

    private static AzureAuthIdentityProvider CreatePhase2DeviceCodeProvider(
        IProcessRunner runner,
        TextWriter promptWriter,
        int maxStandardOutputBytes = 8192,
        int maxStandardErrorBytes = 8192,
        string accountId = "device.user@example.com"
    )
    {
        AzureAuthProviderConfig config = AzureAuthProviderConfig.CreateAzureAuth();
        AzureAuthBinding binding = AzureAuthBindingPolicy.CreateBound(
            config,
            accountId,
            "tenant-device",
            DateTimeOffset.UtcNow
        );
        return new AzureAuthIdentityProvider(
            config,
            binding,
            CreateLaunchOptions(AzureAuthHostPlatform.NativeLinux) with
            {
                MaxStandardOutputBytes = maxStandardOutputBytes,
                MaxStandardErrorBytes = maxStandardErrorBytes,
            },
            runner,
            promptWriter
        );
    }

    private sealed class CancelableDeviceCodeRunner : IProcessRunner
    {
        public const string Prompt =
            "Open https://microsoft.com/devicelogin and enter CODE-1234.\n";

        public TaskCompletionSource<bool> PromptWritten { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public int InvocationCount { get; private set; }

        public async Task<ProcessResult> RunAsync(
            ProcessStartSpec startSpec,
            CancellationToken cancellationToken = default
        )
        {
            InvocationCount++;
            TextWriter promptWriter = Assert.IsAssignableFrom<TextWriter>(
                startSpec.StandardErrorTee
            );
            await promptWriter.WriteAsync(Prompt);
            await promptWriter.FlushAsync(cancellationToken);
            PromptWritten.TrySetResult(true);
            await Task.Delay(Timeout.Infinite, cancellationToken);
            return new ProcessResult(0, "unreachable-private-token", string.Empty);
        }
    }

    [Fact]
    public async Task HostToolAllowsDeviceCodeRejectionPreservesExactSafeMessage()
    {
        var promptWriter = new StringWriter();
        var runner = new RecordingRunner(new ProcessResult(0, CreateToken(), ""));
        AzureAuthIdentityProvider provider = CreateDeviceCodeProvider(
            runner,
            AzureAuthHostPlatform.NativeLinux,
            promptWriter
        );

        AcquiredAccessTokenResult result = await provider.AcquireAccessTokenAsync(
            CreateDeviceCodeRequest(
                AcquisitionMode.InteractionAllowed,
                InteractivePolicy.HostToolAllows
            ),
            TestContext.Current.CancellationToken
        );

        Assert.Equal(AcquiredAccessTokenStatus.RequestRejected, result.Status);
        Assert.Equal("AzureAuthDeviceCodeUnsupported", result.Code);
        Assert.Equal(
            "AzureAuth device-code login requires an explicit interactive native Linux request.",
            result.SafeMessage
        );
        Assert.Null(result.AccessToken);
        Assert.Equal(0, runner.InvocationCount);
        Assert.Null(runner.StartSpec);
        Assert.Equal(string.Empty, promptWriter.ToString());
    }
}
