using System.Text;
using System.Text.Json;
using Authentication.Core;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Authentication.Scenarios;

// Producer obligations: contracts/v1/result.schema.json and V2-REQ-030 through 036.
// Providers, accounts, scopes and token markers are synthetic; no transport is used.
[TestClass]
public sealed class ResultContractScenarios
{
    private const string Scope = "499b84ac-1321-427f-aa17-267ca6975798/user_impersonation";
    private const string Token = "synthetic-opaque-\"\\\n\u2603";
    private static readonly Guid Tenant = new("11111111-2222-3333-4444-555555555555");
    private static readonly Guid Correlation = new("aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee");
    private static readonly DateTimeOffset Now = new(2030, 1, 1, 0, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset Expires = new(2030, 1, 1, 6, 0, 0, TimeSpan.FromMinutes(330));

    [TestMethod]
    [DataRow(false, false)]
    [DataRow(false, true)]
    [DataRow(true, false)]
    [DataRow(true, true)]
    public async Task SuccessfulRequestPreservesMetadataAndEmitsOnlyPublicFields(bool workAccount, bool interactive)
    {
        var email = workAccount ? "WORK@example.test" : "PERSONAL@example.test";
        var provider = new ContractProvider(email) { RequiresInteraction = interactive };
        var outcome = await Acquire(provider, interactive);

        var serialized = ResultProjection.Serialize(outcome);

        Assert.AreEqual(0, serialized.ExitCode, "A validated success must retain exit status zero.");
        using var json = Parse(serialized);
        var root = json.RootElement;
        AssertFields(root, "protocol", "outcome", "accessToken", "tokenType", "expiresOn",
            "accountEmail", "tenantId", "authority", "scopes", "mechanism", "interaction", "warnings", "correlationId");
        Assert.AreEqual("success", root.GetProperty("outcome").GetString());
        Assert.AreEqual(Token, root.GetProperty("accessToken").GetString());
        Assert.AreEqual("Bearer", root.GetProperty("tokenType").GetString());
        Assert.AreEqual(Expires, root.GetProperty("expiresOn").GetDateTimeOffset());
        Assert.AreEqual(email, root.GetProperty("accountEmail").GetString());
        Assert.AreEqual(Tenant, root.GetProperty("tenantId").GetGuid());
        Assert.AreEqual($"https://login.microsoftonline.com/{Tenant:D}", root.GetProperty("authority").GetString());
        CollectionAssert.AreEquivalent(provider.GrantedScopes.ToArray(), Strings(root.GetProperty("scopes")));
        Assert.AreEqual("wam", root.GetProperty("mechanism").GetString());
        Assert.AreEqual(interactive ? "interactive" : "silent", root.GetProperty("interaction").GetString());
        CollectionAssert.AreEqual(new[] { "persistence_unconfirmed" }, Strings(root.GetProperty("warnings")));
        Assert.AreEqual(Correlation, root.GetProperty("correlationId").GetGuid());
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task MissingOrEmptyProviderCorrelationDoesNotInventAnIdentifier(bool emptyGuid)
    {
        var provider = new ContractProvider("personal@example.test")
        {
            ProviderCorrelation = emptyGuid ? Guid.Empty : null,
        };

        var serialized = ResultProjection.Serialize(await Acquire(provider));

        Assert.AreEqual(0, serialized.ExitCode, "Missing optional correlation must not suppress success.");
        using var json = Parse(serialized);
        AssertFields(json.RootElement, "protocol", "outcome", "accessToken", "tokenType", "expiresOn",
            "accountEmail", "tenantId", "authority", "scopes", "mechanism", "interaction", "warnings");
    }

    [TestMethod]
    public async Task DefaultPermissionResultPreservesAnEmptyReportedGrantSet()
    {
        var provider = new ContractProvider("personal@example.test") { GrantedScopes = [] };
        var request = new AuthenticationRequest(provider.Email,
            ["499b84ac-1321-427f-aa17-267ca6975798/.default"], false, Tenant);
        var outcome = await new RequestCoordinator(provider, new ContractHost()).AuthenticateAsync(request);

        var serialized = ResultProjection.Serialize(outcome);

        Assert.AreEqual(0, serialized.ExitCode, "A validated default-permission result remains success.");
        using var json = Parse(serialized);
        Assert.AreEqual(0, json.RootElement.GetProperty("scopes").GetArrayLength());
    }

    [TestMethod]
    [DataRow(AuthenticationFailure.InvalidRequest, "invalid_request")]
    [DataRow(AuthenticationFailure.InteractionRequired, "interaction_required")]
    [DataRow(AuthenticationFailure.AccountAmbiguous, "account_ambiguous")]
    [DataRow(AuthenticationFailure.IdentityValidationFailed, "identity_validation_failed")]
    [DataRow(AuthenticationFailure.Cancelled, "cancelled")]
    [DataRow(AuthenticationFailure.Denied, "denied")]
    [DataRow(AuthenticationFailure.MechanismUnavailable, "mechanism_unavailable")]
    [DataRow(AuthenticationFailure.TemporarilyUnavailable, "temporarily_unavailable")]
    [DataRow(AuthenticationFailure.Timeout, "timeout")]
    [DataRow(AuthenticationFailure.InternalFailure, "internal_failure")]
    public void EveryTypedFailureUsesThePublicOutcomeAndOneNonzeroExit(AuthenticationFailure failure, string expected)
    {
        AssertFailure(ResultProjection.Serialize(new(null, failure)), expected, expected);
    }

    [TestMethod]
    [DataRow(AuthenticationFailure.InteractionRequired, AuthenticationReason.ConsentRequired, "interaction_required", "consent_required")]
    [DataRow(AuthenticationFailure.TemporarilyUnavailable, AuthenticationReason.ProviderTransient, "temporarily_unavailable", "provider_transient")]
    [DataRow(AuthenticationFailure.TemporarilyUnavailable, AuthenticationReason.NetworkTransient, "temporarily_unavailable", "network_transient")]
    [DataRow(AuthenticationFailure.TemporarilyUnavailable, AuthenticationReason.ServiceTransient, "temporarily_unavailable", "service_transient")]
    public async Task SafeProviderReasonsSurviveWithoutBecomingNewOutcomes(AuthenticationFailure failure,
        AuthenticationReason reason, string expectedOutcome, string expectedReason)
    {
        var provider = new ContractProvider("personal@example.test") { Failure = failure, Reason = reason };

        var outcome = await Acquire(provider);

        Assert.AreEqual(reason, outcome.Reason, "The request must preserve the structured reason for the caller.");
        AssertFailure(ResultProjection.Serialize(outcome), expectedOutcome, expectedReason);
    }

    [TestMethod]
    public async Task SuccessfulInteractionDoesNotRetainTheEarlierConsentRequirement()
    {
        var provider = new ContractProvider("personal@example.test") { RequiresInteraction = true };

        var outcome = await Acquire(provider, true);

        Assert.IsNull(outcome.Failure);
        Assert.AreEqual(AuthenticationReason.None, outcome.Reason);
        var serialized = ResultProjection.Serialize(outcome);
        Assert.AreEqual(0, serialized.ExitCode, "A later validated interactive success replaces the earlier requirement.");
        using var json = Parse(serialized);
        Assert.IsFalse(json.RootElement.TryGetProperty("reason", out _));
    }

    [TestMethod]
    public async Task CancellationBeforeCommitCannotEmitTheValidatedTokenOrWarning()
    {
        var provider = new ContractProvider("personal@example.test");
        var host = new ContractHost();
        using var caller = new CancellationTokenSource();
        using var lifetime = new RequestLifetime(host.Clock, host.Clock.GetTimestamp(), TimeSpan.FromSeconds(30), caller.Token);
        var candidate = await lifetime.RunAsync(token => new RequestCoordinator(provider, host).AuthenticateAsync(
            new(provider.Email, [Scope], false, Tenant), token));
        Assert.IsNotNull(candidate.Success);

        caller.Cancel();

        Assert.IsTrue(lifetime.TryCommit(out var committed));
        Assert.IsNotNull(committed);
        AssertFailure(ResultProjection.Serialize(committed), "cancelled", "cancelled");
        Assert.IsFalse(lifetime.TryCommit(out _));
        await lifetime.OperationCompletion;
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public void InvalidInternalOutcomeCannotEmitCandidateMaterial(bool hasConflictingSuccess)
    {
        var candidate = new TokenCandidate(Token, "personal@example.test", Tenant, [Scope], "Bearer", Expires, Guid.NewGuid(), Correlation);
        var outcome = hasConflictingSuccess
            ? new AuthenticationOutcome(candidate, AuthenticationFailure.Denied, true, true)
            : new AuthenticationOutcome(null, null);

        AssertFailure(ResultProjection.Serialize(outcome), "internal_failure", "internal_failure");
    }

    [TestMethod]
    [DataRow(AuthenticationReason.InvalidConfiguration, "invalid_configuration")]
    [DataRow(AuthenticationReason.TenantConflict, "tenant_conflict")]
    public void InvalidRequestReasonsHaveDistinctFixedValues(AuthenticationReason reason, string expected)
    {
        AssertFailure(ResultProjection.Serialize(new(null, AuthenticationFailure.InvalidRequest, Reason: reason)),
            "invalid_request", expected);
        AssertFailure(ResultProjection.Serialize(new(null, AuthenticationFailure.Denied, Reason: reason)),
            "internal_failure", "internal_failure");
    }

    [TestMethod]
    public void UnknownFailureCannotBecomeAnArbitraryPublicOutcome()
    {
        AssertFailure(ResultProjection.Serialize(new(null, (AuthenticationFailure)12345)), "internal_failure", "internal_failure");
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public void UnknownOrIncompatibleReasonCannotChangeTheFailureContract(bool unknownReason)
    {
        var reason = unknownReason ? (AuthenticationReason)12345 : AuthenticationReason.ConsentRequired;

        AssertFailure(ResultProjection.Serialize(new(null, AuthenticationFailure.Denied, Reason: reason)),
            "internal_failure", "internal_failure");
    }

    [TestMethod]
    [DataRow(AuthenticationFailure.Denied, AuthenticationReason.ConsentRequired)]
    [DataRow(AuthenticationFailure.InteractionRequired, AuthenticationReason.NetworkTransient)]
    [DataRow(AuthenticationFailure.MechanismUnavailable, AuthenticationReason.ProviderTransient)]
    public async Task ProviderReasonThatDoesNotMatchItsFailureBecomesInternalFailure(
        AuthenticationFailure failure, AuthenticationReason reason)
    {
        var provider = new ContractProvider("personal@example.test") { Failure = failure, Reason = reason };

        var serialized = ResultProjection.Serialize(await Acquire(provider));

        AssertFailure(serialized, "internal_failure", "internal_failure");
        Assert.IsFalse(Encoding.UTF8.GetString(serialized.Utf8Json).Contains("synthetic-private-claims",
            StringComparison.Ordinal));
    }

    [TestMethod]
    [DataRow("empty-token")]
    [DataRow("empty-token-type")]
    [DataRow("missing-email")]
    [DataRow("empty-email")]
    [DataRow("null-scopes")]
    [DataRow("empty-scope")]
    public void IncompleteSuccessCannotBeProjected(string defect)
    {
        var candidate = new TokenCandidate(Token, "personal@example.test", Tenant, [Scope], "Bearer", Expires,
            Correlation);
        candidate = defect switch
        {
            "empty-token" => candidate with { AccessToken = "" },
            "empty-token-type" => candidate with { TokenType = "" },
            "missing-email" => candidate with { Email = null },
            "empty-email" => candidate with { Email = "" },
            "null-scopes" => candidate with { Scopes = null! },
            "empty-scope" => candidate with { Scopes = [Scope, ""] },
            _ => throw new InvalidOperationException("Unknown synthetic defect."),
        };

        var serialized = ResultProjection.Serialize(new(candidate, null, PersistenceUnconfirmed: true));

        AssertFailure(serialized, "internal_failure", "internal_failure");
        Assert.IsFalse(Encoding.UTF8.GetString(serialized.Utf8Json).Contains("synthetic-opaque",
            StringComparison.Ordinal));
    }

    private static async Task<AuthenticationOutcome> Acquire(ContractProvider provider, bool interactionAllowed = false)
    {
        var host = new ContractHost();
        using var lifetime = new RequestLifetime(host.Clock, host.Clock.GetTimestamp(), TimeSpan.FromSeconds(30));
        await lifetime.RunAsync(token => new RequestCoordinator(provider, host).AuthenticateAsync(
            new(provider.Email.ToLowerInvariant(), [Scope], interactionAllowed, Tenant), token));
        Assert.IsTrue(lifetime.TryCommit(out var committed));
        Assert.IsNotNull(committed);
        await lifetime.OperationCompletion;
        return committed;
    }

    private static void AssertFailure(SerializedResult serialized, string outcome, string reason)
    {
        Assert.AreEqual(1, serialized.ExitCode);
        using var json = Parse(serialized);
        AssertFields(json.RootElement, "protocol", "outcome", "reason");
        Assert.AreEqual(outcome, json.RootElement.GetProperty("outcome").GetString());
        Assert.AreEqual(reason, json.RootElement.GetProperty("reason").GetString());
    }

    private static JsonDocument Parse(SerializedResult serialized)
    {
        var text = new UTF8Encoding(false, true).GetString(serialized.Utf8Json);
        Assert.IsTrue(text.StartsWith('{'));
        Assert.IsTrue(text.EndsWith("}\n", StringComparison.Ordinal));
        var json = JsonDocument.Parse(serialized.Utf8Json);
        Assert.AreEqual(JsonValueKind.Object, json.RootElement.ValueKind);
        Assert.AreEqual(1, json.RootElement.GetProperty("protocol").GetInt32());
        return json;
    }

    private static void AssertFields(JsonElement root, params string[] expected) =>
        CollectionAssert.AreEquivalent(expected, root.EnumerateObject().Select(property => property.Name).ToArray());

    private static string[] Strings(JsonElement array) =>
        array.EnumerateArray().Select(element => element.GetString()!).ToArray();

    private sealed class ContractHost : IRequestHost
    {
        public TimeProvider Clock { get; } = new ContractClock();
        public Task<nint> OpenInteractionAsync(CancellationToken cancellationToken) => Task.FromResult<nint>(1);
        public Task CloseInteractionAsync() => Task.CompletedTask;
    }

    private sealed class ContractClock : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => Now;
    }

    private sealed class ContractProvider(string email) : IAuthenticationProvider
    {
        public string Email { get; } = email;
        public bool RequiresInteraction { get; init; }
        public AuthenticationFailure? Failure { get; init; }
        public AuthenticationReason Reason { get; init; }
        public Guid? ProviderCorrelation { get; init; } = Correlation;
        public IReadOnlyList<string> GrantedScopes { get; init; } = [Scope, "provider-extra-scope"];

        public Task<IReadOnlyList<ProviderAccount>> GetAccountsAsync(CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<ProviderAccount>>([new(Email, new object())]);

        public Task<TokenCandidate> AcquireSilentAsync(AuthenticationRequest request, ProviderAccount account,
            Guid operationId, CancellationToken cancellationToken)
        {
            if (Failure is { } failure) return Task.FromException<TokenCandidate>(
                new ProviderFailureException(failure, "synthetic-private-claims", Reason));
            if (RequiresInteraction) return Task.FromException<TokenCandidate>(
                new ProviderFailureException(AuthenticationFailure.InteractionRequired,
                    "synthetic-private-claims", AuthenticationReason.ConsentRequired));
            return Task.FromResult(Candidate(operationId));
        }

        public Task<TokenCandidate> AcquireInteractiveAsync(AuthenticationRequest request, ProviderAccount? account,
            string? claims, nint parentWindow, Guid operationId, CancellationToken cancellationToken) =>
            Task.FromResult(Candidate(operationId));

        private TokenCandidate Candidate(Guid operationId) =>
            new(Token, Email, Tenant, GrantedScopes, "Bearer", Expires, operationId, ProviderCorrelation);
    }
}
