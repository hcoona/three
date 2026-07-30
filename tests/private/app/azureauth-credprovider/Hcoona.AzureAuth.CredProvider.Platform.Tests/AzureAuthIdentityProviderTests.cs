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
            CreateRequest(AcquisitionMode.SilentOnly),
            TestContext.Current.CancellationToken
        );

        Assert.Equal(AcquiredAccessTokenStatus.InteractionRequired, result.Status);
        Assert.Equal("SilentAcquisitionUnavailable", result.Code);
        Assert.Null(runner.StartSpec);
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
    public async Task InteractiveLaunchUsesExactArgvDomainAndInheritedEnvironment()
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
        Assert.Empty(start.Environment);
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

    private static AzureAuthIdentityProvider CreateProvider(IProcessRunner runner)
    {
        AzureAuthProviderConfig config = AzureAuthProviderConfig.CreateAzureAuth();
        AzureAuthBinding binding = AzureAuthBindingPolicy.CreateBound(
            config,
            "user@example.com",
            "tenant-1",
            DateTimeOffset.UtcNow
        );
        return new AzureAuthIdentityProvider(config, binding, CreateLaunchOptions(), runner);
    }

    private static AzureAuthProcessLaunchOptions CreateLaunchOptions() =>
        new()
        {
            ExecutablePath =
                "/mnt/c/Users/User/AppData/Local/Programs/AzureAuth/0.9.5/azureauth.exe",
            WorkingDirectory = "/mnt/c/Users/User/AppData/Local/Programs/AzureAuth/0.9.5",
        };

    private static CredentialRequestV2 CreateRequest(
        AcquisitionMode mode = AcquisitionMode.InteractionAllowed,
        string? accountHint = null,
        string? tenantHint = null,
        IdentityFlow identityFlow = IdentityFlow.InteractiveBrowser
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
            InteractivePolicy = InteractivePolicy.UserAllowed,
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
}
