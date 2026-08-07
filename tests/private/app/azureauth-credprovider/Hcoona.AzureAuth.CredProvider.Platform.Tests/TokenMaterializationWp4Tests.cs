using System.Net;
using System.Text;
using Hcoona.AzureAuth.CredProvider.Contracts;
using Hcoona.AzureAuth.CredProvider.Platform.AzureAuthProvider;
using Hcoona.AzureAuth.CredProvider.Platform.CredentialCore;
using Hcoona.AzureAuth.CredProvider.Platform.TokenMaterialization;
using Xunit;
using TokenMaterializationApi = Hcoona.AzureAuth.CredProvider.Platform.TokenMaterialization;

namespace Hcoona.AzureAuth.CredProvider.Platform.Tests;

public sealed class TokenMaterializationWp4Tests
{
    private const string TokenDurationPolicyBadRequest =
        """{"type":"TokenDurationPolicy","message":"""
        + "\"The requested validTo violates the token duration policy.\"}";

    private static readonly DateTimeOffset Now = new(2026, 7, 20, 20, 0, 0, TimeSpan.Zero);

    public static TheoryData<string> MalformedJwtCases =>
        new()
        {
            "",
            "one.two",
            "one.two.three.four",
            "=.e30.c2ln",
            "e30.!!!!.c2ln",
            $"{Base64Url("""{"alg":"RS256"}""")}.{Base64Url("""{"aud":""")}.c2ln",
            $"{Base64Url("""{"alg":"RS256"}""")}.{Base64UrlBytes([0xff, 0xfe])}.c2ln",
        };

    public static TheoryData<CredentialEcosystem, CredentialKind, bool> PolicyCases
    {
        get
        {
            var data = new TheoryData<CredentialEcosystem, CredentialKind, bool>();
            CredentialEcosystem[] ecosystems =
            [
                CredentialEcosystem.Git,
                CredentialEcosystem.NuGet,
                CredentialEcosystem.Python,
                CredentialEcosystem.Npm,
                CredentialEcosystem.Pnpm,
                CredentialEcosystem.Yarn,
            ];
            CredentialKind[] kinds =
            [
                CredentialKind.BasicPassword,
                CredentialKind.BearerToken,
                CredentialKind.NpmAuthToken,
                CredentialKind.NuGetPluginCredential,
                CredentialKind.PatCompatibility,
            ];
            foreach (CredentialEcosystem ecosystem in ecosystems)
            {
                foreach (CredentialKind kind in kinds)
                {
                    bool enabled =
                        (ecosystem, kind)
                        is
                            (CredentialEcosystem.Git, CredentialKind.BasicPassword)
                            or
                            (CredentialEcosystem.NuGet, CredentialKind.NuGetPluginCredential)
                            or
                            (CredentialEcosystem.Python, CredentialKind.BasicPassword)
                            or
                            (
                                CredentialEcosystem.Npm
                                    or CredentialEcosystem.Pnpm
                                    or CredentialEcosystem.Yarn,
                                CredentialKind.NpmAuthToken
                            );
                    data.Add(ecosystem, kind, enabled);
                }
            }

            return data;
        }
    }

    [Fact]
    public void ExpiryMetadataReaderReturnsExpWithoutTreatingClaimsAsAuthentication()
    {
        string token = CreateJwt();

        bool valid = JwtExpiryMetadataReader.TryReadExpiration(token, out DateTimeOffset expiresAt);

        Assert.True(valid);
        Assert.Equal(Now.AddHours(1), expiresAt);
    }

    [Theory]
    [MemberData(nameof(MalformedJwtCases))]
    public void ExpiryMetadataReaderRejectsMalformedTokens(string token)
    {
        bool valid = JwtExpiryMetadataReader.TryReadExpiration(token, out _);

        Assert.False(valid);
    }

    [Theory]
    [MemberData(nameof(PolicyCases))]
    public void CredentialFormPolicyCoversEveryEcosystemAndForm(
        CredentialEcosystem ecosystem,
        CredentialKind kind,
        bool enabled
    )
    {
        CredentialFormPolicyDecision decision = CredentialFormPolicy.Evaluate(
            CreateRequest(ecosystem, kind)
        );

        Assert.Equal(enabled, decision.IsEnabled);
    }

    [Fact]
    public async Task SpsExchangeUsesExactDiscoveryAndPostWireWithoutSecretLeakage()
    {
        var handler = new RecordingHandler(
            static (_, call, _) =>
            {
                if (call == 1)
                {
                    var discovery = new HttpResponseMessage(HttpStatusCode.Unauthorized);
                    discovery.Headers.Add(
                        "X-VSS-AuthorizationEndpoint",
                        "https://vssps.dev.azure.com/org/"
                    );
                    return Task.FromResult(discovery);
                }

                return Task.FromResult(
                    JsonResponse(
                        $$"""{"token":"session-secret","validTo":"{{Now.AddMinutes(30):O}}"}"""
                    )
                );
            }
        );
        using var client = new HttpClient(handler) { Timeout = Timeout.InfiniteTimeSpan };
        using var exchange = new AzureDevOpsSpsTokenExchange(client, new FixedTimeProvider(Now));
        AcquiredAccessToken source = CreateAcquiredToken("source-secret");

        AsyncTokenExchangeResult result = await exchange.ExchangeAsync(
            CreateRequest(CredentialEcosystem.NuGet, CredentialKind.NuGetPluginCredential),
            source,
            TestContext.Current.CancellationToken
        );

        Assert.Equal(AsyncTokenExchangeStatus.Success, result.Status);
        Assert.Equal("session-secret", result.Token!.Value);
        Assert.Equal(2, handler.Calls.Count);
        Assert.Equal(HttpMethod.Get, handler.Calls[0].Method);
        Assert.Equal(
            new Uri("https://pkgs.dev.azure.com/org/_packaging/feed/nuget/v3/index.json"),
            handler.Calls[0].Uri
        );
        Assert.Null(handler.Calls[0].Authorization);
        Assert.Equal(HttpMethod.Post, handler.Calls[1].Method);
        Assert.Equal(
            new Uri(
                "https://vssps.dev.azure.com/org/_apis/Token/SessionTokens"
                    + "?tokenType=SelfDescribing&api-version=5.0-preview.1"
            ),
            handler.Calls[1].Uri
        );
        Assert.Equal("Bearer source-secret", handler.Calls[1].Authorization);
        Assert.DoesNotContain(
            "source-secret",
            handler.Calls[1].Uri.ToString(),
            StringComparison.Ordinal
        );
        Assert.DoesNotContain("source-secret", handler.Calls[1].Body!, StringComparison.Ordinal);
        Assert.Contains("\"displayName\":", handler.Calls[1].Body!, StringComparison.Ordinal);
        Assert.Contains(
            "\"scope\":\"vso.packaging_write vso.drop_write\"",
            handler.Calls[1].Body!,
            StringComparison.Ordinal
        );
    }

    [Theory]
    [InlineData("http://vssps.dev.azure.com/org/", "SpsEndpointRejected")]
    [InlineData("https://evil.example/org/", "SpsEndpointRejected")]
    [InlineData("https://vssps.dev.azure.com/other/", "SpsEndpointRejected")]
    [InlineData("https://vssps.dev.azure.com/org/wrong", "SpsEndpointRejected")]
    public async Task SpsExchangeRejectsUntrustedEndpointBeforePosting(
        string endpoint,
        string expectedCode
    )
    {
        var handler = new RecordingHandler(
            (_, _, _) =>
            {
                var response = new HttpResponseMessage(HttpStatusCode.Unauthorized);
                response.Headers.Add("X-VSS-AuthorizationEndpoint", endpoint);
                return Task.FromResult(response);
            }
        );
        using var exchange = new AzureDevOpsSpsTokenExchange(
            new HttpClient(handler),
            new FixedTimeProvider(Now)
        );

        AsyncTokenExchangeResult result = await exchange.ExchangeAsync(
            CreateRequest(CredentialEcosystem.NuGet, CredentialKind.NuGetPluginCredential),
            CreateAcquiredToken(),
            TestContext.Current.CancellationToken
        );

        Assert.Equal(AsyncTokenExchangeStatus.Failed, result.Status);
        Assert.Equal(expectedCode, result.Code);
        Assert.Single(handler.Calls);
    }

    [Fact]
    public async Task SpsExchangeDisablesWhenDiscoveryHeaderIsAbsent()
    {
        var handler = new RecordingHandler(
            static (_, _, _) =>
                Task.FromResult(new HttpResponseMessage(HttpStatusCode.Unauthorized))
        );
        using var exchange = new AzureDevOpsSpsTokenExchange(
            new HttpClient(handler),
            new FixedTimeProvider(Now)
        );

        AsyncTokenExchangeResult result = await exchange.ExchangeAsync(
            CreateRequest(CredentialEcosystem.NuGet, CredentialKind.NuGetPluginCredential),
            CreateAcquiredToken(),
            TestContext.Current.CancellationToken
        );

        Assert.Equal(AsyncTokenExchangeStatus.Disabled, result.Status);
        Assert.Equal("SpsExchangeNotAdvertised", result.Code);
        Assert.Single(handler.Calls);
    }

    [Fact]
    public async Task SpsExchangeDoesNoNetworkForDisabledForm()
    {
        var handler = new RecordingHandler(
            static (_, _, _) => throw new InvalidOperationException("Network must not be used.")
        );
        using var exchange = new AzureDevOpsSpsTokenExchange(new HttpClient(handler));

        AsyncTokenExchangeResult result = await exchange.ExchangeAsync(
            CreateRequest(CredentialEcosystem.Git, CredentialKind.BasicPassword),
            CreateAcquiredToken(),
            TestContext.Current.CancellationToken
        );

        Assert.Equal(AsyncTokenExchangeStatus.Disabled, result.Status);
        Assert.Empty(handler.Calls);

        using var invalidExchange = new AzureDevOpsSpsTokenExchange(
            new HttpClient(handler),
            new FixedTimeProvider(Now)
        );
        AsyncTokenExchangeResult invalid = await invalidExchange.ExchangeAsync(
            CreateRequest(CredentialEcosystem.NuGet, CredentialKind.NuGetPluginCredential),
            CreateAcquiredToken(secret: ""),
            TestContext.Current.CancellationToken
        );
        Assert.Equal("SpsSourceTokenInvalid", invalid.Code);
        Assert.Empty(handler.Calls);
    }

    [Fact]
    public async Task SpsExchangeRejectsRedirectAndOversizedOrDuplicateResponse()
    {
        var redirectHandler = new RecordingHandler(
            static (_, _, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.Redirect))
        );
        using var redirectExchange = new AzureDevOpsSpsTokenExchange(
            new HttpClient(redirectHandler),
            new FixedTimeProvider(Now)
        );
        AsyncTokenExchangeResult redirect = await redirectExchange.ExchangeAsync(
            CreateRequest(CredentialEcosystem.NuGet, CredentialKind.NuGetPluginCredential),
            CreateAcquiredToken(),
            TestContext.Current.CancellationToken
        );
        Assert.Equal("SpsEndpointRejected", redirect.Code);
        Assert.Single(redirectHandler.Calls);

        var duplicateHandler = CreateTwoStepHandler(
            """{"token":"one","token":"two","validTo":"2026-07-20T21:00:00Z"}"""
        );
        using var duplicateExchange = new AzureDevOpsSpsTokenExchange(
            new HttpClient(duplicateHandler),
            new FixedTimeProvider(Now)
        );
        AsyncTokenExchangeResult duplicate = await duplicateExchange.ExchangeAsync(
            CreateRequest(CredentialEcosystem.NuGet, CredentialKind.NuGetPluginCredential),
            CreateAcquiredToken(),
            TestContext.Current.CancellationToken
        );
        Assert.Equal("SpsExchangeResponseInvalid", duplicate.Code);

        var largeHandler = CreateTwoStepHandler(new string('x', 257));
        using var largeExchange = new AzureDevOpsSpsTokenExchange(
            new HttpClient(largeHandler),
            new FixedTimeProvider(Now),
            maxResponseBytes: 256
        );
        AsyncTokenExchangeResult large = await largeExchange.ExchangeAsync(
            CreateRequest(CredentialEcosystem.NuGet, CredentialKind.NuGetPluginCredential),
            CreateAcquiredToken(),
            TestContext.Current.CancellationToken
        );
        Assert.Equal("SpsExchangeResponseInvalid", large.Code);
    }

    [Fact]
    public void SpsExchangeRejectsUnsupportedTransportLimitsAtConstruction()
    {
        using var minimum = new AzureDevOpsSpsTokenExchange(
            timeout: AzureDevOpsSpsTokenExchange.MinimumTimeout,
            maxResponseBytes: AzureDevOpsSpsTokenExchange.MinimumMaxResponseBytes
        );
        using var maximum = new AzureDevOpsSpsTokenExchange(
            timeout: AzureDevOpsSpsTokenExchange.MaximumTimeout,
            maxResponseBytes: AzureDevOpsSpsTokenExchange.MaximumMaxResponseBytes
        );

        Assert.Throws<ArgumentOutOfRangeException>(() =>
            new AzureDevOpsSpsTokenExchange(
                timeout: AzureDevOpsSpsTokenExchange.MinimumTimeout - TimeSpan.FromTicks(1)
            )
        );
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            new AzureDevOpsSpsTokenExchange(timeout: TimeSpan.MaxValue)
        );
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            new AzureDevOpsSpsTokenExchange(
                maxResponseBytes: AzureDevOpsSpsTokenExchange.MinimumMaxResponseBytes - 1
            )
        );
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            new AzureDevOpsSpsTokenExchange(maxResponseBytes: int.MaxValue)
        );
    }

    [Theory]
    [InlineData(30, 25)]
    [InlineData(30, 31)]
    public async Task SpsExchangeRejectsUnusableResponseExpiry(
        int responseExpiryMinutes,
        int completionAdvanceMinutes
    )
    {
        DateTimeOffset responseExpiry = Now.AddMinutes(responseExpiryMinutes);
        var handler = CreateTwoStepHandler(
            $$"""{"token":"session-secret","validTo":"{{responseExpiry:O}}"}"""
        );
        using var exchange = new AzureDevOpsSpsTokenExchange(
            new HttpClient(handler),
            new AdvancingTimeProvider(Now, Now.AddMinutes(completionAdvanceMinutes))
        );

        AsyncTokenExchangeResult result = await exchange.ExchangeAsync(
            CreateRequest(CredentialEcosystem.NuGet, CredentialKind.NuGetPluginCredential),
            CreateAcquiredToken(),
            TestContext.Current.CancellationToken
        );

        Assert.Equal(AsyncTokenExchangeStatus.Failed, result.Status);
        Assert.Equal("SpsExchangeResponseInvalid", result.Code);
        Assert.Equal(2, handler.Calls.Count);
    }

    [Theory]
    [InlineData("""{"token":"secret","validTo":"not-a-date"}""")]
    [InlineData("""{"token":"secret","validTo":"2026-07-20T19:59:59Z"}""")]
    [InlineData("""{"Token":"secret","validTo":"2026-07-20T21:00:00Z"}""")]
    [InlineData("""{"token":123,"validTo":"2026-07-20T21:00:00Z"}""")]
    [InlineData("{")]
    public async Task SpsExchangeRejectsMalformedOrExpiredResponse(string body)
    {
        var handler = CreateTwoStepHandler(body);
        using var exchange = new AzureDevOpsSpsTokenExchange(
            new HttpClient(handler),
            new FixedTimeProvider(Now)
        );

        AsyncTokenExchangeResult result = await exchange.ExchangeAsync(
            CreateRequest(CredentialEcosystem.NuGet, CredentialKind.NuGetPluginCredential),
            CreateAcquiredToken(),
            TestContext.Current.CancellationToken
        );

        Assert.Equal(AsyncTokenExchangeStatus.Failed, result.Status);
        Assert.DoesNotContain("source-secret", result.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task SpsExchangeRejectsNonSuccessPostStatus()
    {
        var handler = new RecordingHandler(
            static (_, call, _) =>
            {
                if (call == 1)
                {
                    var discovery = new HttpResponseMessage(HttpStatusCode.Unauthorized);
                    discovery.Headers.Add(
                        "X-VSS-AuthorizationEndpoint",
                        "https://vssps.dev.azure.com/org/"
                    );
                    return Task.FromResult(discovery);
                }

                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.Forbidden));
            }
        );
        using var exchange = new AzureDevOpsSpsTokenExchange(
            new HttpClient(handler),
            new FixedTimeProvider(Now)
        );

        AsyncTokenExchangeResult result = await exchange.ExchangeAsync(
            CreateRequest(CredentialEcosystem.NuGet, CredentialKind.NuGetPluginCredential),
            CreateAcquiredToken(),
            TestContext.Current.CancellationToken
        );

        Assert.Equal("SpsExchangeHttpStatus", result.Code);
        Assert.Equal(2, handler.Calls.Count);
    }

    [Fact]
    public async Task SpsExchangeHonorsCancellationAndTimeout()
    {
        var handler = new RecordingHandler(
            static async (_, _, token) =>
            {
                await Task.Delay(Timeout.InfiniteTimeSpan, token);
                throw new InvalidOperationException();
            }
        );
        using var canceledSource = new CancellationTokenSource();
        canceledSource.Cancel();
        using var exchange = new AzureDevOpsSpsTokenExchange(
            new HttpClient(handler),
            new FixedTimeProvider(Now)
        );
        AsyncTokenExchangeResult canceled = await exchange.ExchangeAsync(
            CreateRequest(CredentialEcosystem.NuGet, CredentialKind.NuGetPluginCredential),
            CreateAcquiredToken(),
            canceledSource.Token
        );
        Assert.Equal(AsyncTokenExchangeStatus.Canceled, canceled.Status);

        using var timeoutExchange = new AzureDevOpsSpsTokenExchange(
            new HttpClient(handler),
            new FixedTimeProvider(Now),
            timeout: TimeSpan.FromMilliseconds(20)
        );
        AsyncTokenExchangeResult timedOut = await timeoutExchange.ExchangeAsync(
            CreateRequest(CredentialEcosystem.NuGet, CredentialKind.NuGetPluginCredential),
            CreateAcquiredToken(),
            TestContext.Current.CancellationToken
        );
        Assert.Equal(AsyncTokenExchangeStatus.TimedOut, timedOut.Status);
    }

    [Fact]
    public async Task MaterializerUsesSourceMetadataForDirectAndAuthoritativeExchangeExpiry()
    {
        DateTimeOffset exchangeExpiry = Now.AddHours(2);
        var fakeExchange = new FixedExchange(
            AsyncTokenExchangeResult.Success(
                new SecretText { Value = "session-secret" },
                exchangeExpiry
            )
        );
        var service = new CredentialMaterializationService(
            fakeExchange,
            new FixedTimeProvider(Now)
        );
        AcquiredAccessToken token = CreateAcquiredToken("source-secret", Now.AddHours(1));

        CredentialMaterializationResult direct = await service.MaterializeAsync(
            CreateRequest(CredentialEcosystem.Git, CredentialKind.BasicPassword),
            token,
            TestContext.Current.CancellationToken
        );
        Assert.Equal("AzureDevOps", direct.Username);
        Assert.Equal("source-secret", direct.Password);
        Assert.Equal(Now.AddHours(1), direct.ExpiresAt);
        Assert.Equal(0, fakeExchange.CallCount);

        CredentialMaterializationResult exchanged = await service.MaterializeAsync(
            CreateRequest(CredentialEcosystem.NuGet, CredentialKind.NuGetPluginCredential),
            token,
            TestContext.Current.CancellationToken
        );
        Assert.Equal("session-secret", exchanged.Password);
        Assert.Equal("VssSessionToken", exchanged.Username);
        Assert.Equal(exchangeExpiry, exchanged.ExpiresAt);
        Assert.Equal(1, fakeExchange.CallCount);
        Assert.DoesNotContain("source-secret", exchanged.ToString(), StringComparison.Ordinal);
        Assert.DoesNotContain("session-secret", exchanged.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task MaterializerAcceptsUnknownSourceExpiryAndUsesExchangeExpiryWhenAvailable()
    {
        DateTimeOffset exchangeExpiry = Now.AddHours(2);
        var fakeExchange = new FixedExchange(
            AsyncTokenExchangeResult.Success(
                new SecretText { Value = "session-secret" },
                exchangeExpiry
            )
        );
        var service = new CredentialMaterializationService(
            fakeExchange,
            new FixedTimeProvider(Now)
        );
        AcquiredAccessToken token = CreateAcquiredToken(includeExpiry: false);

        CredentialMaterializationResult direct = await service.MaterializeAsync(
            CreateRequest(CredentialEcosystem.Git, CredentialKind.BasicPassword),
            token,
            TestContext.Current.CancellationToken
        );
        CredentialMaterializationResult exchanged = await service.MaterializeAsync(
            CreateRequest(CredentialEcosystem.NuGet, CredentialKind.NuGetPluginCredential),
            token,
            TestContext.Current.CancellationToken
        );

        Assert.Equal(CredentialMaterializationStatus.Success, direct.Status);
        Assert.Null(direct.ExpiresAt);
        Assert.Equal(exchangeExpiry, exchanged.ExpiresAt);
    }

    [Fact]
    public async Task MaterializerRejectsDisabledAndExpiredTokensWithoutExchange()
    {
        var fakeExchange = new FixedExchange(
            AsyncTokenExchangeResult.Failure(AsyncTokenExchangeStatus.Failed, "Unexpected")
        );
        var service = new CredentialMaterializationService(
            fakeExchange,
            new FixedTimeProvider(Now)
        );

        CredentialMaterializationResult disabled = await service.MaterializeAsync(
            CreateRequest(CredentialEcosystem.Git, CredentialKind.BearerToken),
            CreateAcquiredToken(),
            TestContext.Current.CancellationToken
        );
        CredentialMaterializationResult expired = await service.MaterializeAsync(
            CreateRequest(CredentialEcosystem.NuGet, CredentialKind.NuGetPluginCredential),
            CreateAcquiredToken(expiresAt: Now),
            TestContext.Current.CancellationToken
        );

        Assert.Equal(CredentialMaterializationStatus.Disabled, disabled.Status);
        Assert.Equal(CredentialMaterializationStatus.InvalidToken, expired.Status);
        Assert.Equal(0, fakeExchange.CallCount);
    }

    private static CredentialRequestV2 CreateRequest(
        CredentialEcosystem ecosystem,
        CredentialKind kind
    )
    {
        bool git = ecosystem == CredentialEcosystem.Git;
        Uri endpoint = ecosystem switch
        {
            CredentialEcosystem.Git => new("https://dev.azure.com/org/project/_git/repo"),
            CredentialEcosystem.NuGet => new(
                "https://pkgs.dev.azure.com/org/_packaging/feed/nuget/v3/index.json"
            ),
            CredentialEcosystem.Python => new(
                "https://pkgs.dev.azure.com/org/_packaging/feed/pypi/simple/"
            ),
            _ => new("https://pkgs.dev.azure.com/org/_packaging/feed/npm/registry/"),
        };
        return new CredentialRequestV2
        {
            Ecosystem = ecosystem,
            Operation = CredentialOperation.Get,
            Resource = CanonicalResourceIdentity.Create(
                endpoint.Host,
                "org",
                endpoint,
                project: git ? "project" : null,
                feed: git ? null : "feed",
                repository: git ? "repo" : null
            ),
            ServiceIdentity = "default",
            RequestedAudience = git ? TokenAudience.AzureDevOps : TokenAudience.AzureArtifacts,
            CredentialKind = kind,
            IdentityFlow = IdentityFlow.InteractiveBrowser,
            InteractivePolicy = InteractivePolicy.UserAllowed,
            AcquisitionMode = AcquisitionMode.InteractionAllowed,
            CachePolicy = CachePolicyMode.ProductPersistentCacheDisabled,
            CiContext = new CiContext { ExplicitCiMode = false, AllowsPersistentWrites = false },
        };
    }

    private static AcquiredAccessToken CreateAcquiredToken(
        string secret = "source-secret",
        DateTimeOffset? expiresAt = null,
        bool includeExpiry = true
    ) =>
        new()
        {
            AccountId = null,
            TenantId = "tenant-1",
            Token = new SecretText { Value = secret },
            ExpiresAt = includeExpiry ? expiresAt ?? Now.AddHours(1) : null,
            Provenance = AccessTokenAcquisitionProvenance.AzureAuthProcess,
        };

    private static string CreateJwt(
        string audience = AzureAuthIdentityProvider.AzureDevOpsResourceId,
        string tenant = "tenant-1",
        int issuedOffset = -60,
        int notBeforeOffset = -60,
        int expiryOffset = 3600
    )
    {
        string payload =
            $$"""{"aud":"{{audience}}","tid":"{{tenant}}","iat":"""
            + Now.AddSeconds(issuedOffset).ToUnixTimeSeconds()
            + ""","nbf":"""
            + Now.AddSeconds(notBeforeOffset).ToUnixTimeSeconds()
            + ""","exp":"""
            + Now.AddSeconds(expiryOffset).ToUnixTimeSeconds()
            + "}";

        return $"{Base64Url("""{"alg":"RS256","typ":"JWT"}""")}.{Base64Url(payload)}."
            + "c2lnbmF0dXJl";
    }

    private static string ValidPayload() =>
        Base64Url(
            "{\"aud\":\""
                + AzureAuthIdentityProvider.AzureDevOpsResourceId
                + "\",\"tid\":\"tenant-1\",\"iat\":"
                + Now.AddMinutes(-1).ToUnixTimeSeconds()
                + ""","nbf":"""
                + Now.AddMinutes(-1).ToUnixTimeSeconds()
                + ""","exp":"""
                + Now.AddHours(1).ToUnixTimeSeconds()
                + "}"
        );

    private static string Base64Url(string value) => Base64UrlBytes(Encoding.UTF8.GetBytes(value));

    private static string Base64UrlBytes(byte[] value) =>
        Convert.ToBase64String(value).TrimEnd('=').Replace('+', '-').Replace('/', '_');

    private static HttpResponseMessage JsonResponse(string json) =>
        new(HttpStatusCode.OK)
        {
            Content = new StringContent(json, Encoding.UTF8, "application/json"),
        };

    private static RecordingHandler CreateTwoStepHandler(string responseBody) =>
        new(
            (_, call, _) =>
            {
                if (call == 1)
                {
                    var discovery = new HttpResponseMessage(HttpStatusCode.Unauthorized);
                    discovery.Headers.Add(
                        "X-VSS-AuthorizationEndpoint",
                        "https://vssps.dev.azure.com/org/"
                    );
                    return Task.FromResult(discovery);
                }

                return Task.FromResult(JsonResponse(responseBody));
            }
        );

    private sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }

    private sealed class AdvancingTimeProvider(params DateTimeOffset[] values) : TimeProvider
    {
        private int _index = -1;

        public override DateTimeOffset GetUtcNow()
        {
            int index = Interlocked.Increment(ref _index);
            return values[Math.Min(index, values.Length - 1)];
        }
    }

    private sealed class FixedExchange(AsyncTokenExchangeResult result)
        : TokenMaterializationApi.ITokenExchange
    {
        public int CallCount { get; private set; }

        public ValueTask<AsyncTokenExchangeResult> ExchangeAsync(
            CredentialRequestV2 request,
            AcquiredAccessToken sourceToken,
            CancellationToken cancellationToken = default
        )
        {
            _ = request;
            _ = sourceToken;
            _ = cancellationToken;
            CallCount++;
            return ValueTask.FromResult(result);
        }
    }

    private sealed record RecordedCall(
        HttpMethod Method,
        Uri Uri,
        string? Authorization,
        string? Body
    );

    private sealed class RecordingHandler(
        Func<HttpRequestMessage, int, CancellationToken, Task<HttpResponseMessage>> handler
    ) : HttpMessageHandler
    {
        private int _callCount;
        public List<RecordedCall> Calls { get; } = [];

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken
        )
        {
            int call = Interlocked.Increment(ref _callCount);
            string? body = request.Content is null
                ? null
                : await request.Content.ReadAsStringAsync(cancellationToken);
            Calls.Add(
                new RecordedCall(
                    request.Method,
                    request.RequestUri!,
                    request.Headers.Authorization?.ToString(),
                    body
                )
            );
            return await handler(request, call, cancellationToken);
        }
    }

    [Theory]
    [InlineData(
        "https://vssps.dev.azure.com/org/",
        "https://vssps.dev.azure.com/org/_apis/Token/SessionTokens"
            + "?tokenType=SelfDescribing&api-version=5.0-preview.1"
    )]
    [InlineData(
        "https://wcus0.app.vssps.dev.azure.com/org/",
        "https://wcus0.app.vssps.dev.azure.com/org/_apis/Token/SessionTokens"
            + "?tokenType=SelfDescribing&api-version=5.0-preview.1"
    )]
    [InlineData(
        "https://vssps.visualstudio.com/",
        "https://vssps.visualstudio.com/_apis/Token/SessionTokens"
            + "?tokenType=SelfDescribing&api-version=5.0-preview.1"
    )]
    [InlineData(
        "https://wcus0.app.vssps.visualstudio.com/",
        "https://wcus0.app.vssps.visualstudio.com/_apis/Token/SessionTokens"
            + "?tokenType=SelfDescribing&api-version=5.0-preview.1"
    )]
    [InlineData(
        "https://vsspsext.dev.azure.com/org/",
        "https://vsspsext.dev.azure.com/org/_apis/Token/SessionTokens"
            + "?tokenType=SelfDescribing&api-version=5.0-preview.1"
    )]
    [InlineData(
        "https://VSSPSEXT.VISUALSTUDIO.COM/",
        "https://vsspsext.visualstudio.com/_apis/Token/SessionTokens"
            + "?tokenType=SelfDescribing&api-version=5.0-preview.1"
    )]
    public void SpsSessionEndpointAcceptsOfficialAzureDevOpsHosts(
        string baseEndpoint,
        string expectedEndpoint
    )
    {
        bool accepted = AzureDevOpsSpsTokenExchange.TryCreateAllowedSessionEndpoint(
            new Uri(baseEndpoint),
            "org",
            out Uri? endpoint
        );

        Assert.True(accepted);
        Assert.Equal(new Uri(expectedEndpoint), endpoint);
    }

    [Theory]
    [InlineData("https://evilvssps.dev.azure.com/org/")]
    [InlineData("https://vssps.dev.azure.com.evil.example/org/")]
    [InlineData("https://evilvssps.visualstudio.com/")]
    [InlineData("https://vssps.visualstudio.com.evil.example/")]
    [InlineData("https://app.vsspsext.dev.azure.com/org/")]
    [InlineData("https://vsspsext.dev.azure.com.evil.example/org/")]
    [InlineData("https://app.vsspsext.visualstudio.com/")]
    [InlineData("https://vsspsext.visualstudio.com.evil.example/")]
    [InlineData("https://vssps.devppe.azure.com/org/")]
    [InlineData("https://app.vssps.vsallin.net/")]
    [InlineData("http://vssps.dev.azure.com/org/")]
    [InlineData("https://vssps.dev.azure.com:444/org/")]
    [InlineData("https://user@vssps.dev.azure.com/org/")]
    [InlineData("https://vssps.dev.azure.com/other/")]
    [InlineData("https://vssps.dev.azure.com/org/wrong")]
    [InlineData("https://vsspsext.dev.azure.com/other/")]
    [InlineData("https://vsspsext.dev.azure.com/org/wrong")]
    [InlineData("https://vssps.visualstudio.com/org/")]
    [InlineData("https://vsspsext.visualstudio.com/org/")]
    [InlineData("https://vssps.dev.azure.com/org/?unsafe=true")]
    [InlineData("https://vssps.dev.azure.com/org/#unsafe")]
    public void SpsSessionEndpointRejectsLookalikeOrUnsafeUris(string baseEndpoint)
    {
        bool accepted = AzureDevOpsSpsTokenExchange.TryCreateAllowedSessionEndpoint(
            new Uri(baseEndpoint),
            "org",
            out Uri? endpoint
        );

        Assert.False(accepted);
        Assert.Null(endpoint);
    }

    [Fact]
    public void CreateProductionHttpHandlerUsesSecureDefaults()
    {
        using SocketsHttpHandler handler =
            AzureDevOpsSpsTokenExchange.CreateProductionHttpHandler();

        Assert.True(handler.UseProxy);
        Assert.Null(handler.Proxy);
        Assert.False(handler.AllowAutoRedirect);
        Assert.False(handler.UseCookies);
        Assert.Null(handler.SslOptions.RemoteCertificateValidationCallback);
    }

    [Fact]
    public async Task SpsExchangeRetriesTokenDurationPolicyBadRequestOnceWithoutValidTo()
    {
        DateTimeOffset serviceExpiry = Now.AddMinutes(45);
        var handler = new RecordingHandler(
            (_, call, _) =>
            {
                if (call == 1)
                {
                    var discovery = new HttpResponseMessage(HttpStatusCode.Unauthorized);
                    discovery.Headers.Add(
                        "X-VSS-AuthorizationEndpoint",
                        "https://vssps.dev.azure.com/org/"
                    );
                    return Task.FromResult(discovery);
                }

                if (call == 2)
                {
                    return Task.FromResult(
                        new HttpResponseMessage(HttpStatusCode.BadRequest)
                        {
                            Content = new StringContent(
                                TokenDurationPolicyBadRequest,
                                Encoding.UTF8,
                                "application/json"
                            ),
                        }
                    );
                }

                if (call == 3)
                {
                    return Task.FromResult(
                        JsonResponse(
                            $$"""
                            {"token":"relaxed-session-token",
                            "validTo":"{{serviceExpiry:O}}"}
                            """
                        )
                    );
                }

                throw new InvalidOperationException("Only one relaxed retry is allowed.");
            }
        );
        using var exchange = new AzureDevOpsSpsTokenExchange(
            new HttpClient(handler),
            new FixedTimeProvider(Now)
        );
        AcquiredAccessToken sourceToken = CreateAcquiredToken();
        string expectedAuthorization =
            new System.Net.Http.Headers.AuthenticationHeaderValue(
                "Bearer",
                sourceToken.Token.Value
            ).ToString();

        AsyncTokenExchangeResult result = await exchange.ExchangeAsync(
            CreateRequest(CredentialEcosystem.NuGet, CredentialKind.NuGetPluginCredential),
            sourceToken,
            TestContext.Current.CancellationToken
        );

        Assert.Equal(AsyncTokenExchangeStatus.Success, result.Status);
        Assert.Equal("relaxed-session-token", result.Token!.Value);
        Assert.Equal(serviceExpiry, result.ExpiresAt);
        Assert.Collection(
            handler.Calls,
            call =>
            {
                Assert.Equal(HttpMethod.Get, call.Method);
                Assert.Equal(
                    new Uri(
                        "https://pkgs.dev.azure.com/org/_packaging/feed/nuget/v3/index.json"
                    ),
                    call.Uri
                );
                Assert.Null(call.Authorization);
            },
            firstPost =>
            {
                Assert.Equal(HttpMethod.Post, firstPost.Method);
                Assert.Equal(
                    new Uri(
                        "https://vssps.dev.azure.com/org/_apis/Token/SessionTokens"
                            + "?tokenType=SelfDescribing&api-version=5.0-preview.1"
                    ),
                    firstPost.Uri
                );
                Assert.Equal(expectedAuthorization, firstPost.Authorization);
            },
            retryPost =>
            {
                Assert.Equal(HttpMethod.Post, retryPost.Method);
                Assert.Equal(handler.Calls[1].Uri, retryPost.Uri);
                Assert.Equal(handler.Calls[1].Authorization, retryPost.Authorization);
            }
        );

        using System.Text.Json.JsonDocument firstBody = System.Text.Json.JsonDocument.Parse(
            handler.Calls[1].Body!
        );
        Assert.True(firstBody.RootElement.TryGetProperty("validTo", out var firstValidTo));
        Assert.Equal(System.Text.Json.JsonValueKind.String, firstValidTo.ValueKind);
        Assert.False(string.IsNullOrWhiteSpace(firstValidTo.GetString()));
        string? firstDisplayName = firstBody.RootElement.GetProperty("displayName").GetString();
        string? firstScope = firstBody.RootElement.GetProperty("scope").GetString();
        Assert.Equal("Azure DevOps Artifacts Credential Provider", firstDisplayName);
        Assert.Equal("vso.packaging_write vso.drop_write", firstScope);

        using System.Text.Json.JsonDocument retryBody = System.Text.Json.JsonDocument.Parse(
            handler.Calls[2].Body!
        );
        Assert.Equal(
            firstDisplayName,
            retryBody.RootElement.GetProperty("displayName").GetString()
        );
        Assert.Equal(firstScope, retryBody.RootElement.GetProperty("scope").GetString());
        bool retryOmitsUsableValidTo =
            !retryBody.RootElement.TryGetProperty("validTo", out var retryValidTo)
            || retryValidTo.ValueKind == System.Text.Json.JsonValueKind.Null;
        Assert.True(retryOmitsUsableValidTo);
    }

    [Fact]
    public async Task SpsExchangeStopsAfterSecondTokenDurationPolicyBadRequest()
    {
        var handler = new RecordingHandler(
            (_, call, _) =>
            {
                if (call == 1)
                {
                    var discovery = new HttpResponseMessage(HttpStatusCode.Unauthorized);
                    discovery.Headers.Add(
                        "X-VSS-AuthorizationEndpoint",
                        "https://vssps.dev.azure.com/org/"
                    );
                    return Task.FromResult(discovery);
                }

                if (call is 2 or 3)
                {
                    return Task.FromResult(
                        new HttpResponseMessage(HttpStatusCode.BadRequest)
                        {
                            Content = new StringContent(
                                TokenDurationPolicyBadRequest,
                                Encoding.UTF8,
                                "application/json"
                            ),
                        }
                    );
                }

                throw new InvalidOperationException("A second retry must not be attempted.");
            }
        );
        using var exchange = new AzureDevOpsSpsTokenExchange(
            new HttpClient(handler),
            new FixedTimeProvider(Now)
        );
        AcquiredAccessToken sourceToken = CreateAcquiredToken();
        string expectedAuthorization =
            new System.Net.Http.Headers.AuthenticationHeaderValue(
                "Bearer",
                sourceToken.Token.Value
            ).ToString();

        AsyncTokenExchangeResult result = await exchange.ExchangeAsync(
            CreateRequest(CredentialEcosystem.NuGet, CredentialKind.NuGetPluginCredential),
            sourceToken,
            TestContext.Current.CancellationToken
        );

        Assert.Equal(AsyncTokenExchangeStatus.Failed, result.Status);
        Assert.Equal("SpsExchangeHttpStatus", result.Code);
        Assert.Null(result.Token);
        Assert.Null(result.ExpiresAt);
        Assert.Collection(
            handler.Calls,
            call =>
            {
                Assert.Equal(HttpMethod.Get, call.Method);
                Assert.Equal(
                    new Uri(
                        "https://pkgs.dev.azure.com/org/_packaging/feed/nuget/v3/index.json"
                    ),
                    call.Uri
                );
                Assert.Null(call.Authorization);
            },
            firstPost =>
            {
                Assert.Equal(HttpMethod.Post, firstPost.Method);
                Assert.Equal(
                    new Uri(
                        "https://vssps.dev.azure.com/org/_apis/Token/SessionTokens"
                            + "?tokenType=SelfDescribing&api-version=5.0-preview.1"
                    ),
                    firstPost.Uri
                );
                Assert.Equal(expectedAuthorization, firstPost.Authorization);
            },
            retryPost =>
            {
                Assert.Equal(HttpMethod.Post, retryPost.Method);
                Assert.Equal(handler.Calls[1].Uri, retryPost.Uri);
                Assert.Equal(handler.Calls[1].Authorization, retryPost.Authorization);
            }
        );

        using System.Text.Json.JsonDocument firstBody = System.Text.Json.JsonDocument.Parse(
            handler.Calls[1].Body!
        );
        Assert.True(firstBody.RootElement.TryGetProperty("validTo", out var firstValidTo));
        Assert.Equal(System.Text.Json.JsonValueKind.String, firstValidTo.ValueKind);
        Assert.False(string.IsNullOrWhiteSpace(firstValidTo.GetString()));
        string? firstDisplayName = firstBody.RootElement.GetProperty("displayName").GetString();
        string? firstScope = firstBody.RootElement.GetProperty("scope").GetString();
        Assert.Equal("Azure DevOps Artifacts Credential Provider", firstDisplayName);
        Assert.Equal("vso.packaging_write vso.drop_write", firstScope);

        using System.Text.Json.JsonDocument retryBody = System.Text.Json.JsonDocument.Parse(
            handler.Calls[2].Body!
        );
        Assert.Equal(
            firstDisplayName,
            retryBody.RootElement.GetProperty("displayName").GetString()
        );
        Assert.Equal(firstScope, retryBody.RootElement.GetProperty("scope").GetString());
        bool retryOmitsUsableValidTo =
            !retryBody.RootElement.TryGetProperty("validTo", out var retryValidTo)
            || retryValidTo.ValueKind == System.Text.Json.JsonValueKind.Null;
        Assert.True(retryOmitsUsableValidTo);
    }

    [Fact]
    public async Task SpsExchangeAcceptsServiceAuthoritativeExpiryBeyondRequestedLifetime()
    {
        DateTimeOffset serviceExpiry = Now.AddHours(5);
        var handler = CreateTwoStepHandler(
            $$"""{"token":"service-authoritative-token","validTo":"{{serviceExpiry:O}}"}"""
        );
        using var exchange = new AzureDevOpsSpsTokenExchange(
            new HttpClient(handler),
            new FixedTimeProvider(Now)
        );

        AsyncTokenExchangeResult result = await exchange.ExchangeAsync(
            CreateRequest(CredentialEcosystem.NuGet, CredentialKind.NuGetPluginCredential),
            CreateAcquiredToken(),
            TestContext.Current.CancellationToken
        );

        Assert.Equal(AsyncTokenExchangeStatus.Success, result.Status);
        Assert.Equal("service-authoritative-token", result.Token!.Value);
        Assert.Equal(serviceExpiry, result.ExpiresAt);
        Assert.Collection(
            handler.Calls,
            call => Assert.Equal(HttpMethod.Get, call.Method),
            call => Assert.Equal(HttpMethod.Post, call.Method)
        );

        using System.Text.Json.JsonDocument requestBody = System.Text.Json.JsonDocument.Parse(
            handler.Calls[1].Body!
        );
        Assert.True(requestBody.RootElement.TryGetProperty("validTo", out var validToElement));
        Assert.Equal(System.Text.Json.JsonValueKind.String, validToElement.ValueKind);
        DateTimeOffset requestedValidTo = validToElement.GetDateTimeOffset();
        Assert.Equal(Now.AddHours(4), requestedValidTo);
        Assert.True(result.ExpiresAt > requestedValidTo);
    }
}
