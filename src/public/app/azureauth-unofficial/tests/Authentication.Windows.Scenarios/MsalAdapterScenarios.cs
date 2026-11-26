using System.Net.Http;
using System.Text;
using System.Text.Json.Nodes;
using Authentication.Core;
using Microsoft.Identity.Client;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Authentication.Windows.Scenarios;

// Public synthetic MSAL values cross our adapter seam and the real coordinator. No
// account store, broker, window, process, or network is used; only the real non-broker
// interactive scenario builds an MSAL application, against a synthetic transport.
[TestClass]
public sealed class MsalAdapterScenarios
{
    private const string Email = "personal@example.test";
    private const string Scope = "499b84ac-1321-427f-aa17-267ca6975798/user_impersonation";
    private const string PrivateMarker = "SYNTHETIC-PRIVATE-MARKER";
    private static readonly Guid Tenant = new("11111111-2222-3333-4444-555555555555");
    private static readonly DateTimeOffset Now = new(2026, 9, 13, 0, 0, 0, TimeSpan.Zero);

    [TestMethod]
    public void MechanismTraceKeepsFirstFixedCauseAndNeverProviderText()
    {
        var trace = new WindowsMechanismTrace();
        var failure = MsalBoundary.MapFailure(new MsalClientException("wam_runtime_init_failed", PrivateMarker), CancellationToken.None, trace);
        Assert.AreEqual(AuthenticationFailure.MechanismUnavailable, failure.Failure);
        Assert.AreEqual(WindowsMechanismFailure.BrokerInitialization, trace.Failure);
        trace.Record(WindowsMechanismFailure.HostSession);
        Assert.AreEqual(WindowsMechanismFailure.BrokerInitialization, trace.Failure);
        var text = Encoding.ASCII.GetString(WindowsDiagnostics.MechanismIndication(trace.Failure));
        Assert.AreEqual("Mechanism unavailable at broker_initialization.\n", text);
        Assert.IsFalse(text.Contains(PrivateMarker, StringComparison.Ordinal));
        using var cancelled = new CancellationTokenSource(); cancelled.Cancel();
        var cancelledTrace = new WindowsMechanismTrace();
        Assert.ThrowsExactly<OperationCanceledException>(() => MsalBoundary.MapFailure(
            new MsalClientException("platform_not_supported", PrivateMarker), cancelled.Token, cancelledTrace));
        Assert.AreEqual(WindowsMechanismFailure.Unavailable, cancelledTrace.Failure);
    }

    [TestMethod]
    [DataRow("query", ProviderDiagnosticError.QueryParameterConflict)]
    [DataRow("client", ProviderDiagnosticError.MsalClient)]
    [DataRow("service", ProviderDiagnosticError.MsalService)]
    [DataRow("argument", ProviderDiagnosticError.Argument)]
    [DataRow("operation", ProviderDiagnosticError.InvalidOperation)]
    [DataRow("unexpected", ProviderDiagnosticError.Unexpected)]
    public void InternalFailureRetainsOnlyFixedProviderCategory(string kind, ProviderDiagnosticError expected)
    {
        Exception exception = kind switch
        {
            "query" => new MsalClientException(MsalError.DuplicateQueryParameterError, PrivateMarker),
            "client" => new MsalClientException(PrivateMarker, PrivateMarker),
            "service" => new MsalServiceException(PrivateMarker, PrivateMarker) { IsRetryable = false },
            "argument" => new ArgumentException(PrivateMarker),
            "operation" => new InvalidOperationException(PrivateMarker),
            _ => new Exception(PrivateMarker),
        };
        var mapped = MsalBoundary.MapFailure(exception, CancellationToken.None);
        Assert.AreEqual(AuthenticationFailure.InternalFailure, mapped.Failure);
        Assert.AreEqual(expected, mapped.DiagnosticError);
        Assert.IsFalse(mapped.ToString().Contains(PrivateMarker, StringComparison.Ordinal));
        Assert.IsNull(mapped.InnerException);
        Assert.IsNull(mapped.Claims);
    }

    [TestMethod]
    [DataRow("ApiContractViolation", ProviderDiagnosticError.BrokerContract)]
    [DataRow("IncorrectConfiguration", ProviderDiagnosticError.BrokerConfiguration)]
    [DataRow("6", ProviderDiagnosticError.MsalService)]
    [DataRow("9", ProviderDiagnosticError.MsalService)]
    [DataRow("06", ProviderDiagnosticError.MsalService)]
    [DataRow(" 6", ProviderDiagnosticError.MsalService)]
    [DataRow("9 ", ProviderDiagnosticError.MsalService)]
    [DataRow(" ApiContractViolation", ProviderDiagnosticError.MsalService)]
    [DataRow("IncorrectConfiguration ", ProviderDiagnosticError.MsalService)]
    [DataRow("0", ProviderDiagnosticError.MsalService)]
    [DataRow(PrivateMarker, ProviderDiagnosticError.MsalService)]
    [DataRow(null, ProviderDiagnosticError.MsalService)]
    public void BrokerStatusRequiresExactPinnedStructuredValue(string? status, ProviderDiagnosticError expected)
    {
        var exception = new MsalServiceException("WAM_provider_error_synthetic", PrivateMarker)
        {
            IsRetryable = false,
            AdditionalExceptionData = status is null ? null! : new Dictionary<string, string>
            {
                [MsalException.BrokerErrorStatus] = status,
                [MsalException.BrokerErrorContext] = PrivateMarker,
                [MsalException.BrokerErrorCode] = PrivateMarker,
                [MsalException.BrokerTelemetry] = PrivateMarker,
            },
        };
        var mapped = MsalBoundary.MapFailure(exception, CancellationToken.None);
        Assert.AreEqual(AuthenticationFailure.InternalFailure, mapped.Failure);
        Assert.AreEqual(expected, mapped.DiagnosticError);
        Assert.IsFalse(mapped.ToString().Contains(PrivateMarker, StringComparison.Ordinal));
        exception = new MsalServiceException("synthetic_other_service", PrivateMarker)
        {
            IsRetryable = false,
            AdditionalExceptionData = new Dictionary<string, string> { [MsalException.BrokerErrorStatus] = "ApiContractViolation" },
        };
        Assert.AreEqual(ProviderDiagnosticError.MsalService,
            MsalBoundary.MapFailure(exception, CancellationToken.None).DiagnosticError);
    }

    [TestMethod]
    public void MalformedBrokerBagCannotChangeTheSafeOutcome()
    {
        var exception = new MsalServiceException("WAM_provider_error_synthetic", PrivateMarker)
        {
            IsRetryable = false,
            AdditionalExceptionData = new ThrowingBrokerData(),
        };
        var mapped = MsalBoundary.MapFailure(exception, CancellationToken.None);
        Assert.AreEqual(AuthenticationFailure.InternalFailure, mapped.Failure);
        Assert.AreEqual(ProviderDiagnosticError.MsalService, mapped.DiagnosticError);
        Assert.IsFalse(mapped.ToString().Contains(PrivateMarker, StringComparison.Ordinal));
    }

    private sealed class ThrowingBrokerData : IReadOnlyDictionary<string, string>
    {
        public string this[string key] => throw new InvalidOperationException(PrivateMarker);
        public IEnumerable<string> Keys => [];
        public IEnumerable<string> Values => [];
        public int Count => 0;
        public bool ContainsKey(string key) => false;
        public bool TryGetValue(string key, out string value) => throw new InvalidOperationException(PrivateMarker);
        public IEnumerator<KeyValuePair<string, string>> GetEnumerator() => Enumerable.Empty<KeyValuePair<string, string>>().GetEnumerator();
        System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator() => GetEnumerator();
    }

    [TestMethod]
    public void OriginalOutcomePrecedenceDoesNotAcquireDiagnosticClassification()
    {
        Exception[] failures =
        [
            new MsalClientException("authentication_canceled", PrivateMarker),
            new MsalServiceException("access_denied", PrivateMarker),
            new MsalClientException("platform_not_supported", PrivateMarker),
            new MsalClientException(MsalError.DuplicateQueryParameterError, PrivateMarker) { IsRetryable = true },
            new MsalUiRequiredException("invalid_grant", PrivateMarker),
        ];
        foreach (var failure in failures)
        {
            var mapped = MsalBoundary.MapFailure(failure, CancellationToken.None);
            Assert.AreNotEqual(AuthenticationFailure.InternalFailure, mapped.Failure);
            Assert.AreEqual(ProviderDiagnosticError.None, mapped.DiagnosticError);
        }
        using var caller = new CancellationTokenSource();
        caller.Cancel();
        Assert.ThrowsExactly<OperationCanceledException>(() => MsalBoundary.MapFailure(
            new MsalClientException(MsalError.DuplicateQueryParameterError, PrivateMarker), caller.Token));
    }

    [TestMethod]
    public async Task ConsentRequirementHonorsInteractionPermission()
    {
        var scene = new Scene(Consent());
        var outcome = await scene.RunAsync(interactionAllowed: false);
        Failure(AuthenticationFailure.InteractionRequired, outcome);
        Assert.AreEqual(AuthenticationReason.ConsentRequired, outcome.Reason);
        Assert.AreEqual(0, scene.InteractiveCalls);
    }

    [TestMethod]
    public async Task SilentClaimsReachOneContinuationAndSecondChallengeStops()
    {
        const string claims = "{\"access_token\":{\"synthetic_claim\":null}}";
        var envelope = JsonNode.Parse(new MsalUiRequiredException("invalid_grant", PrivateMarker).ToJsonString())!;
        envelope["claims"] = claims;
        var challenge = MsalException.FromJsonString(envelope.ToJsonString());
        Assert.IsInstanceOfType<MsalUiRequiredException>(challenge, "The public fixture must preserve its type.");
        Assert.AreEqual(claims, ((MsalUiRequiredException)challenge).Claims);
        var scene = new Scene(challenge) { InteractiveFailure = Consent() };
        using var original = new CancellationTokenSource();
        var outcome = await scene.RunAsync(cancellationToken: original.Token);

        Assert.AreEqual(claims, scene.InteractiveClaims);
        Assert.AreSame(scene.Account, scene.InteractiveAccount?.Handle);
        Assert.AreEqual(Email, scene.InteractiveRequest?.AccountEmail);
        Assert.AreEqual(Tenant, scene.InteractiveRequest?.ExactTenant);
        CollectionAssert.AreEqual(new[] { Scope }, scene.InteractiveRequest!.Scopes.ToArray());
        Assert.AreEqual(original.Token, scene.InteractiveToken);
        Failure(AuthenticationFailure.InteractionRequired, outcome);
        Assert.AreEqual(1, scene.SilentCalls);
        Assert.AreEqual(1, scene.InteractiveCalls);
    }

    [TestMethod]
    public async Task AccessDeniedWinsOverUiRequiredAndRetryHint()
    {
        var observation = new MsalUiRequiredException("access_denied", PrivateMarker, null,
            UiRequiredExceptionClassification.ConsentRequired) { IsRetryable = true };
        await TerminalAsync(observation, AuthenticationFailure.Denied);
    }

    [TestMethod]
    public async Task Structured65004WinsOverRetryHint()
    {
        await TerminalAsync(Service("{\"error_codes\":[65004]}", 503), AuthenticationFailure.Denied);
    }

    [TestMethod]
    public async Task DenialTextAndNativeCodeDoNotImplyEntraDenial()
    {
        var observation = new MsalServiceException("invalid_grant", "AADSTS65004 " + PrivateMarker, 400)
        {
            ResponseBody = "{\"error_description\":\"AADSTS65004\",\"detail\":{\"error_codes\":[65004]}}",
            AdditionalExceptionData = new Dictionary<string, string> { ["BrokerErrorCode"] = "65004" },
            IsRetryable = false,
        };
        await TerminalAsync(observation, AuthenticationFailure.InternalFailure);
    }

    [TestMethod]
    public async Task DuplicateErrorCodesDoNotCreateDenial()
    {
        await TerminalAsync(Service("{\"error_codes\":[65004],\"error_codes\":[0]}"), AuthenticationFailure.InternalFailure);
    }

    [TestMethod]
    public async Task NonNumericErrorCodesDoNotCreateDenial()
    {
        await TerminalAsync(Service("{\"error_codes\":[65004,\"other\"]}"), AuthenticationFailure.InternalFailure);
    }

    [TestMethod]
    public async Task MalformedOrOverBudgetBodiesDoNotCreateDenial()
    {
        var nested = string.Concat(Enumerable.Repeat("{\"detail\":", 9)) + "0" + new string('}', 9);
        string[] bodies =
        [
            "{\"error_codes\":[65004]",
            "{\"error_codes\":[65004]}".PadRight(8193),
            "{\"error_codes\":[65004],\"detail\":" + nested + "}",
            "{\"error_codes\":[65004," + string.Join(',', Enumerable.Repeat("0", 16)) + "]}",
        ];
        foreach (var body in bodies)
            await TerminalAsync(Service(body), AuthenticationFailure.InternalFailure);
    }

    [TestMethod]
    public async Task ProviderUserCancellationRemainsCancelled()
    {
        await TerminalAsync(new MsalClientException("authentication_canceled", PrivateMarker) { IsRetryable = true },
            AuthenticationFailure.Cancelled);
    }

    [TestMethod]
    public void OriginalCancellationWinsOverDenial()
    {
        using var original = new CancellationTokenSource();
        var observation = Service("{\"error_codes\":[65004]}");
        original.Cancel();
        try
        {
            MsalBoundary.MapFailure(observation, original.Token);
            Assert.Fail("Original cancellation must propagate before provider classification.");
        }
        catch (OperationCanceledException cancellation)
        {
            Assert.AreEqual(original.Token, cancellation.CancellationToken);
        }
    }

    [TestMethod]
    public async Task OriginalDeadlineWinsLateProviderCancellation()
    {
        var scene = new Scene(new MsalClientException("authentication_canceled", PrivateMarker));
        var entry = scene.Clock.GetTimestamp();
        scene.BeforeMapping = () => scene.Clock.Advance(TimeSpan.FromSeconds(120));
        using var lifetime = new RequestLifetime(scene.Clock, entry, TimeSpan.FromSeconds(120));
        var outcome = await lifetime.RunAsync(token => scene.RunAsync(cancellationToken: token));
        Failure(AuthenticationFailure.Timeout, outcome);
        await lifetime.OperationCompletion.WaitAsync(TimeSpan.FromSeconds(2));
        Assert.IsTrue(lifetime.TryCommit(out var committed));
        Failure(AuthenticationFailure.Timeout, committed!);
        Assert.AreEqual(1, scene.SilentCalls);
        Assert.AreEqual(0, scene.InteractiveCalls);
    }

    [TestMethod]
    public async Task HttpTimeoutDoesNotConsumeRequestDeadline()
    {
        using var original = new CancellationTokenSource();
        var observation = new OperationCanceledException(PrivateMarker, new TimeoutException(PrivateMarker), CancellationToken.None);
        var scene = new Scene(observation);
        var entry = scene.Clock.GetTimestamp();
        var outcome = await scene.RunAsync(cancellationToken: original.Token);
        Failure(AuthenticationFailure.TemporarilyUnavailable, outcome);
        Assert.AreEqual(AuthenticationReason.NetworkTransient, outcome.Reason);
        Assert.IsFalse(original.IsCancellationRequested);
        Assert.AreEqual(entry, scene.Clock.GetTimestamp());
        Assert.AreEqual(1, scene.SilentCalls);
        Assert.AreEqual(0, scene.InteractiveCalls);
    }

    [TestMethod]
    public async Task RetryableProviderStopsWithoutApplicationRetry()
    {
        Exception[] observations =
        [
            new MsalServiceException("synthetic_service_failure", PrivateMarker, 503),
            new MsalClientException("unknown_broker_error", PrivateMarker) { IsRetryable = true },
        ];
        foreach (var observation in observations)
            await TerminalAsync(observation, AuthenticationFailure.TemporarilyUnavailable, AuthenticationReason.ProviderTransient);
    }

    [TestMethod]
    public async Task RecognizedNetworkErrorStopsWithoutRetry()
    {
        await TerminalAsync(new HttpRequestException(HttpRequestError.ConnectionError, PrivateMarker),
            AuthenticationFailure.TemporarilyUnavailable, AuthenticationReason.NetworkTransient);
    }

    [TestMethod]
    public async Task UnknownProviderConfigurationStaysInternal()
    {
        await TerminalAsync(new MsalClientException("invalid_client", PrivateMarker) { IsRetryable = false },
            AuthenticationFailure.InternalFailure);
    }

    [TestMethod]
    public async Task UnexplainedCancellationStaysInternal()
    {
        await TerminalAsync(new OperationCanceledException(PrivateMarker), AuthenticationFailure.InternalFailure);
    }

    [TestMethod]
    public async Task UserMismatchWinsOverRetryHint()
    {
        await TerminalAsync(new MsalClientException("user_mismatch", PrivateMarker) { IsRetryable = true },
            AuthenticationFailure.IdentityValidationFailed);
    }

    [TestMethod]
    public void ResultProjectionPreservesObservedMetadata()
    {
        var account = new SyntheticAccount("work@example.test");
        var observedTenant = new Guid("22222222-3333-4444-5555-666666666666");
        var correlation = new Guid("33333333-4444-5555-6666-777777777777");
        var result = new AuthenticationResult("synthetic-access", false, PrivateMarker, Now.AddHours(1), Now.AddHours(1),
            observedTenant.ToString(), account, PrivateMarker, [Scope, "synthetic-observed-scope"], correlation, tokenType: "Bearer");
        var candidate = MsalBoundary.Project(result);

        Assert.AreEqual("work@example.test", candidate.Email);
        Assert.AreEqual(observedTenant, candidate.Tenant);
        CollectionAssert.AreEqual(result.Scopes.ToArray(), candidate.Scopes.ToArray());
        Assert.AreEqual("synthetic-access", candidate.AccessToken);
        Assert.AreEqual("Bearer", candidate.TokenType);
        Assert.AreEqual(Now.AddHours(1), candidate.ExpiresOn);
        Assert.AreEqual(correlation, candidate.CorrelationId);
        Assert.AreEqual(correlation, candidate.OperationId, "The operation binding is the provider-returned correlation ID.");
    }

    [TestMethod]
    [DataRow("not-a-guid")]
    [DataRow("00000000-0000-0000-0000-000000000000")]
    public void MissingAccountAndInvalidTenantRemainMissing(string tenantId)
    {
        var result = new AuthenticationResult("synthetic-access", false, PrivateMarker, Now.AddHours(1), Now.AddHours(1),
            tenantId, null, PrivateMarker, [Scope], Guid.Empty, tokenType: "Bearer");
        var candidate = MsalBoundary.Project(result);
        Assert.IsNull(candidate.Email);
        Assert.IsNull(candidate.Tenant);
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task RealNonBrokerInteractiveCallReachesTheRejectingWebUi(bool forced)
    {
        // A real MSAL public-client application without a broker models broker loss:
        // its interactive call falls back to the web UI the production builder installs.
        // The canned transport answers only instance discovery and fails the test on any
        // other request, so no browser, account or identity endpoint is reached.
        var transport = new CannedDiscoveryTransport();
        using var http = new MsalHttpClientFactory(transport);
        var application = (PublicClientApplication)PublicClientApplicationBuilder
            .Create("aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee")
            .WithAuthority("https://login.microsoftonline.com/" + Tenant.ToString("D"))
            .WithRedirectUri("http://localhost")
            .WithHttpClientFactory(http)
            .Build();
        var trace = new WindowsMechanismTrace();
        var operation = new MsalInteractiveOperation([Scope], null, forced ? null : Email,
            Tenant.ToString("D"), null, (nint)42, Guid.NewGuid(), forced);
        var builder = MsalSessionFactory.BuildInteractiveRequest(application, operation, trace, CancellationToken.None);
        // Refuse to execute unless the production builder installed the rejecting web UI,
        // so a regression cannot open the default browser or web view.
        Assert.IsInstanceOfType<RejectingWebUi>(MsalCompositionScenarios.BuilderMember(
            MsalCompositionScenarios.BuilderMember(builder, "Parameters")!, "CustomWebUi"));

        Exception? observed = null;
        try
        {
            _ = await builder.ExecuteAsync(CancellationToken.None).WaitAsync(TimeSpan.FromSeconds(30));
            Assert.Fail("A rejected web UI cannot produce an authentication result.");
        }
        catch (Exception exception) when (exception is not AssertFailedException) { observed = exception; }

        Assert.IsNull(transport.Unexpected, transport.Unexpected);
        Assert.AreEqual(WindowsMechanismFailure.RejectedWebUi, trace.Failure, "The rejecting web UI was not reached.");
        var mapped = MsalBoundary.MapFailure(observed!, CancellationToken.None, trace);
        Assert.AreEqual(AuthenticationFailure.MechanismUnavailable, mapped.Failure);
        Assert.AreEqual("Mechanism unavailable at rejected_web_ui.\n",
            Encoding.ASCII.GetString(WindowsDiagnostics.MechanismIndication(trace.Failure)));
    }

    // Terminal synthetic transport: no inner handler and no socket.
    private sealed class CannedDiscoveryTransport : HttpMessageHandler
    {
        internal string? Unexpected { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            var uri = request.RequestUri;
            if (request.Method == HttpMethod.Get && uri is { Host: "login.microsoftonline.com" }
                && uri.AbsolutePath == "/common/discovery/instance")
            {
                return Task.FromResult(new HttpResponseMessage(System.Net.HttpStatusCode.OK)
                {
                    Content = new StringContent("""
                        {"tenant_discovery_endpoint":"https://login.microsoftonline.com/common/v2.0/.well-known/openid-configuration",
                         "api-version":"1.1","metadata":[{"preferred_network":"login.microsoftonline.com",
                         "preferred_cache":"login.windows.net","aliases":["login.microsoftonline.com","login.windows.net"]}]}
                        """, Encoding.UTF8, "application/json"),
                });
            }

            Unexpected ??= request.Method + " " + uri?.GetLeftPart(UriPartial.Path);
            throw new InvalidOperationException("The synthetic transport forbids this request.");
        }
    }

    private static MsalUiRequiredException Consent() =>
        new("invalid_grant", PrivateMarker, null, UiRequiredExceptionClassification.ConsentRequired);

    private static MsalServiceException Service(string body, int status = 400) =>
        new("invalid_grant", PrivateMarker, status) { ResponseBody = body };

    private static async Task TerminalAsync(Exception observation, AuthenticationFailure expected,
        AuthenticationReason reason = AuthenticationReason.None)
    {
        var scene = new Scene(observation);
        var outcome = await scene.RunAsync();
        Failure(expected, outcome);
        Assert.AreEqual(reason, outcome.Reason);
        Assert.AreEqual(1, scene.SilentCalls);
        Assert.AreEqual(0, scene.InteractiveCalls);
    }

    private static void Failure(AuthenticationFailure expected, AuthenticationOutcome outcome)
    {
        Assert.AreEqual(expected, outcome.Failure);
        Assert.IsNull(outcome.Success);
        var serialized = ResultProjection.Serialize(outcome);
        Assert.AreEqual(1, serialized.ExitCode);
        var json = Encoding.UTF8.GetString(serialized.Utf8Json);
        Assert.IsFalse(json.Contains(PrivateMarker, StringComparison.Ordinal));
        Assert.IsFalse(json.Contains("accessToken", StringComparison.Ordinal));
    }

    private sealed class Scene(Exception silentFailure) : IAuthenticationProvider, IRequestHost
    {
        public SyntheticAccount Account { get; } = new(Email);
        public ScenarioClock Clock { get; } = new();
        TimeProvider IRequestHost.Clock => Clock;
        public Exception? InteractiveFailure { get; init; }
        public Action? BeforeMapping { get; set; }
        public int SilentCalls { get; private set; }
        public int InteractiveCalls { get; private set; }
        public ProviderAccount? InteractiveAccount { get; private set; }
        public AuthenticationRequest? InteractiveRequest { get; private set; }
        public string? InteractiveClaims { get; private set; }
        public CancellationToken InteractiveToken { get; private set; }

        public Task<AuthenticationOutcome> RunAsync(bool interactionAllowed = true, CancellationToken cancellationToken = default) =>
            new RequestCoordinator(this, this).AuthenticateAsync(new(Email, [Scope], interactionAllowed, Tenant), cancellationToken);

        public Task<IReadOnlyList<ProviderAccount>> GetAccountsAsync(CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<ProviderAccount>>([new(Email, Account)]);

        public Task<TokenCandidate> AcquireSilentAsync(AuthenticationRequest request, ProviderAccount account,
            Guid operationId, CancellationToken cancellationToken)
        {
            SilentCalls++;
            BeforeMapping?.Invoke();
            return Task.FromException<TokenCandidate>(MsalBoundary.MapFailure(silentFailure, cancellationToken));
        }

        public Task<TokenCandidate> AcquireInteractiveAsync(AuthenticationRequest request, ProviderAccount? account,
            string? claims, nint parentWindow, Guid operationId, CancellationToken cancellationToken)
        {
            InteractiveCalls++;
            InteractiveAccount = account;
            InteractiveRequest = request;
            InteractiveClaims = claims;
            InteractiveToken = cancellationToken;
            return Task.FromException<TokenCandidate>(MsalBoundary.MapFailure(
                InteractiveFailure ?? new InvalidOperationException(PrivateMarker), cancellationToken));
        }

        public Task<nint> OpenInteractionAsync(CancellationToken cancellationToken) => Task.FromResult<nint>(42);

        public Task CloseInteractionAsync() => Task.CompletedTask;
    }

    private sealed class ScenarioClock : TimeProvider
    {
        private long timestamp;
        public override DateTimeOffset GetUtcNow() => Now;
        public override long GetTimestamp() => timestamp;
        public override long TimestampFrequency => TimeSpan.TicksPerSecond;
        public void Advance(TimeSpan elapsed) => timestamp += elapsed.Ticks;
    }

    private sealed class SyntheticAccount(string username) : IAccount
    {
        public string Username => username;
        public string Environment => "login.example.test";
        public AccountId HomeAccountId { get; } = new(
            "55555555-6666-7777-8888-999999999999.11111111-2222-3333-4444-555555555555",
            "55555555-6666-7777-8888-999999999999", "11111111-2222-3333-4444-555555555555");
    }
}
