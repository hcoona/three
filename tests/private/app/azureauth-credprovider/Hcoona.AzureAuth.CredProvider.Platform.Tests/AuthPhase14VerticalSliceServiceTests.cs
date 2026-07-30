using Hcoona.AzureAuth.CredProvider.Contracts;
using Hcoona.AzureAuth.CredProvider.Platform.Composition;
using Hcoona.AzureAuth.CredProvider.Platform.CredentialCore;
using Hcoona.AzureAuth.CredProvider.Platform.VerticalSlice;
using Xunit;

namespace Hcoona.AzureAuth.CredProvider.Platform.Tests;

public sealed class AuthPhase14VerticalSliceServiceTests
{
    [Fact]
    public void LoginInteractiveBrowserUsesAcceptedMvpFlowWithoutPersistentDerivedCredentials()
    {
        var service = new AuthPhase14VerticalSliceService(
            new AuthPhase14VerticalSliceOptions
            {
                CredentialCoreService = new CredentialCoreService(
                    new DeterministicFakeIdentityProvider()),
            });

        AuthPhase14LoginResult result = service.Login(
            new AuthPhase14LoginRequest
            {
                IdentityFlow = IdentityFlow.InteractiveBrowser,
                AccountHint = "Alice@Example",
                TenantHint = "TenantA",
            },
            TestContext.Current.CancellationToken
        );

        Assert.Equal(CredentialResultStatus.Success, result.CredentialResult.Status);
        Assert.Equal("alice@example", result.CredentialResult.Account);
        Assert.Equal("tenanta", result.CredentialResult.Tenant);
        Assert.False(result.PersistentDerivedCredentialsStored);
        Assert.True(result.CredentialResult.ContainsCredentialMaterial);
    }

    [Fact]
    public void LoginPatCompatibilityIsDeferredWithoutMaterialization()
    {
        var service = new AuthPhase14VerticalSliceService();

        AuthPhase14LoginResult result = service.Login(
            new AuthPhase14LoginRequest
            {
                IdentityFlow = IdentityFlow.PatCompatibility,
                ExplicitPatMaterialProvided = true,
            },
            TestContext.Current.CancellationToken
        );

        Assert.Equal(CredentialResultStatus.FlowDeferred, result.CredentialResult.Status);
        Assert.Equal("PatCompatibilityDeferred", result.CredentialResult.Error?.Code);
        Assert.False(result.CredentialResult.ContainsCredentialMaterial);
    }

    [Fact]
    public async Task LoginCancellationStopsBoundedCredentialAcquisitionPromptly()
    {
        var acquisition = new BlockingCredentialAcquisitionService();
        var service = new AuthPhase14VerticalSliceService(
            new AuthPhase14VerticalSliceOptions
            {
                CredentialAcquisition = new BoundedCredentialAcquisitionAdapter(acquisition),
            }
        );
        using var cancellation = new CancellationTokenSource();
        Task<AuthPhase14LoginResult> login = Task.Run(() =>
            service.Login(
                new AuthPhase14LoginRequest
                {
                    IdentityFlow = IdentityFlow.InteractiveBrowser,
                },
                cancellation.Token
            )
        );
        await acquisition.Started.Task.WaitAsync(
            TimeSpan.FromSeconds(5),
            TestContext.Current.CancellationToken
        );

        cancellation.Cancel();
        AuthPhase14LoginResult result = await login.WaitAsync(
            TimeSpan.FromSeconds(5),
            TestContext.Current.CancellationToken
        );

        Assert.Equal("CredentialAcquisitionCanceled", result.CredentialResult.Error?.Code);
    }

    [Theory]
    [MemberData(nameof(MalformedPatRequests))]
    public void ExecuteCredentialRequestRejectsMalformedPatBeforeProviderOrCache(
        CredentialRequest request)
    {
        var provider = new CountingIdentityProvider();
        var cache = new CountingDerivedCredentialCache();
        var service = new AuthPhase14VerticalSliceService(
            new AuthPhase14VerticalSliceOptions
            {
                CredentialCoreService = new CredentialCoreService(provider, null, cache),
            }
        );

        CredentialResult result = service.ExecuteCredentialRequest(request);

        Assert.Equal(CredentialResultStatus.ProtocolViolation, result.Status);
        Assert.Equal(CredentialErrorKind.ProtocolViolation, result.Error?.Kind);
        Assert.Equal("ProtocolViolation", result.Error?.Code);
        Assert.False(result.ContainsCredentialMaterial);
        Assert.Equal(0, provider.InvocationCount);
        Assert.Equal(0, cache.InvocationCount);
    }

    [Fact]
    public void ExecuteCredentialRequestDefersValidPatBeforeProviderOrCache()
    {
        var provider = new CountingIdentityProvider();
        var cache = new CountingDerivedCredentialCache();
        var service = new AuthPhase14VerticalSliceService(
            new AuthPhase14VerticalSliceOptions
            {
                CredentialCoreService = new CredentialCoreService(provider, null, cache),
            }
        );

        CredentialResult result = service.ExecuteCredentialRequest(CreatePatRequest());

        Assert.Equal(CredentialResultStatus.FlowDeferred, result.Status);
        Assert.Equal("PatCompatibilityDeferred", result.Error?.Code);
        Assert.False(result.ContainsCredentialMaterial);
        Assert.Equal(0, provider.InvocationCount);
        Assert.Equal(0, cache.InvocationCount);
    }

    [Fact]
    public void LoginAzurePipelinesRequiresExplicitCiModeAndTokenEnvironment()
    {
        var service = new AuthPhase14VerticalSliceService(
            new AuthPhase14VerticalSliceOptions { EnvironmentVariableReader = _ => null }
        );

        AuthPhase14LoginResult result = service.Login(
            new AuthPhase14LoginRequest
            {
                IdentityFlow = IdentityFlow.AzurePipelinesSystemAccessToken,
                ExplicitAzurePipelinesCiMode = true,
            },
            TestContext.Current.CancellationToken
        );

        Assert.Equal(
            CredentialResultStatus.CredentialUnavailable,
            result.CredentialResult.Status
        );
        Assert.Equal(
            "AzurePipelinesSystemAccessTokenUnavailable",
            result.CredentialResult.Error?.Code
        );
    }

    [Fact]
    public void LoginAzurePipelinesUsesNonPersistentCiPolicy()
    {
        var service = new AuthPhase14VerticalSliceService(
            new AuthPhase14VerticalSliceOptions { EnvironmentVariableReader = _ => "token" }
        );

        AuthPhase14LoginResult result = service.Login(
            new AuthPhase14LoginRequest
            {
                IdentityFlow = IdentityFlow.AzurePipelinesSystemAccessToken,
                ExplicitAzurePipelinesCiMode = true,
            },
            TestContext.Current.CancellationToken
        );

        Assert.Equal(CredentialResultStatus.Success, result.CredentialResult.Status);
        Assert.Null(result.CredentialResult.Account);
        Assert.Null(result.CredentialResult.Tenant);
        Assert.Null(result.CredentialResult.CacheKey);
        Assert.Null(result.CredentialResult.ExpiresAt);
        Assert.Equal("token", result.CredentialResult.BearerToken);
        Assert.False(result.PersistentDerivedCredentialsStored);
    }

    [Fact]
    public void LoginDeferredServiceIdentityFlowThrowsNotSupported()
    {
        var service = new AuthPhase14VerticalSliceService();

        NotSupportedException exception = Assert.Throws<NotSupportedException>(
            () =>
                service.Login(
                    new AuthPhase14LoginRequest
                    {
                        IdentityFlow = IdentityFlow.ManagedIdentity,
                    },
                    TestContext.Current.CancellationToken
                )
        );

        Assert.Contains("deferred for MVP", exception.Message);
    }

    public static TheoryData<CredentialRequest> MalformedPatRequests()
    {
        CredentialRequest request = CreatePatRequest();
        return new TheoryData<CredentialRequest>
        {
            request with { AccountHint = "account\u001B" },
            request with { TenantHint = "tenant\u009F" },
            request with { ContractMajor = ContractVersions.CredentialContractV2Major },
            request with { Resource = null! },
        };
    }

    private static CredentialRequest CreatePatRequest() =>
        new()
        {
            Ecosystem = CredentialEcosystem.Git,
            Operation = CredentialOperation.Get,
            Resource = CanonicalResourceIdentity.Create(
                "dev.azure.com",
                "phase14",
                new Uri("https://dev.azure.com/phase14")
            ),
            ServiceIdentity = "default",
            RequestedAudience = TokenAudience.AzureDevOps,
            CredentialKind = CredentialKind.PatCompatibility,
            IdentityFlow = IdentityFlow.PatCompatibility,
            InteractivePolicy = InteractivePolicy.UserAllowed,
            CachePolicy = CachePolicyMode.ProductPersistentCacheDisabled,
        };

    private sealed class CountingIdentityProvider : IIdentityProvider
    {
        public int InvocationCount { get; private set; }

        public IdentityMaterial GetIdentity(CredentialRequest request)
        {
            InvocationCount++;
            throw new InvalidOperationException("Identity provider must not execute.");
        }
    }

    private sealed class CountingDerivedCredentialCache : IDerivedCredentialCache
    {
        public int InvocationCount { get; private set; }

        public DerivedCredentialCacheAvailability GetPersistentAvailability(
            CredentialRequest request)
        {
            InvocationCount++;
            throw new InvalidOperationException("Credential cache must not execute.");
        }

        public DerivedCredentialCacheReadResult TryReadPersistent(
            CredentialRequest request,
            CacheKey cacheKey)
        {
            InvocationCount++;
            throw new InvalidOperationException("Credential cache must not execute.");
        }

        public DerivedCredentialCacheWriteResult TryWritePersistent(
            CredentialRequest request,
            CacheKey cacheKey,
            IdentityMaterial identity)
        {
            InvocationCount++;
            throw new InvalidOperationException("Credential cache must not execute.");
        }

    }

    private sealed class BlockingCredentialAcquisitionService : ICredentialAcquisitionService
    {
        public TaskCompletionSource Started { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public async ValueTask<CredentialResult> AcquireAsync(
            CredentialRequestV2 request,
            CancellationToken cancellationToken = default)
        {
            Started.TrySetResult();
            await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            throw new InvalidOperationException("Unreachable.");
        }
    }
}
