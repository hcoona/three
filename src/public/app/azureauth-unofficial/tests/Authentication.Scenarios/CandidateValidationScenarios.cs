using Authentication.Core;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Authentication.Scenarios;

[TestClass]
public sealed class CandidateValidationScenarios
{
    private const string Email = "selected@example.test";
    private const string Scope = "499b84ac-1321-427f-aa17-267ca6975798/user_impersonation";
    private static readonly Guid Tenant = new("11111111-2222-3333-4444-555555555555");
    private static readonly DateTimeOffset Now = new(2026, 9, 13, 0, 0, 0, TimeSpan.Zero);

    [TestMethod]
    [DataRow("empty-token")]
    [DataRow("missing-email")]
    [DataRow("missing-tenant")]
    [DataRow("missing-token-type")]
    [DataRow("expired")]
    [DataRow("expires-now")]
    [DataRow("missing-scope")]
    [DataRow("scope-case-mismatch")]
    [DataRow("different-operation")]
    public async Task IncompleteOrUnrelatedProviderSuccessCannotExposeAnAccessToken(string defect)
    {
        var provider = new CandidateProvider(candidate => defect switch
        {
            "empty-token" => candidate with { AccessToken = "" },
            "missing-email" => candidate with { Email = null },
            "missing-tenant" => candidate with { Tenant = null },
            "missing-token-type" => candidate with { TokenType = "" },
            "expired" => candidate with { ExpiresOn = Now.AddTicks(-1) },
            "expires-now" => candidate with { ExpiresOn = Now },
            "missing-scope" => candidate with { Scopes = [] },
            "scope-case-mismatch" => candidate with { Scopes = [Scope.ToUpperInvariant()] },
            "different-operation" => candidate with { OperationId = Guid.Empty },
            _ => throw new InvalidOperationException("Unknown synthetic defect."),
        });

        var outcome = await new RequestCoordinator(provider, new ScenarioHost())
            .AuthenticateAsync(new AuthenticationRequest(Email, [Scope], false, null));

        Assert.AreEqual(AuthenticationFailure.IdentityValidationFailed, outcome.Failure);
        Assert.IsNull(outcome.Success);
    }

    [TestMethod]
    public async Task ValidSuccessPreservesMetadataWithoutAnExtraMinimumLifetime()
    {
        var provider = new CandidateProvider(candidate => candidate with
        {
            Email = "SELECTED@example.test",
            ExpiresOn = Now.AddTicks(1),
            Scopes = [Scope, "499b84ac-1321-427f-aa17-267ca6975798/extra_permission"],
        });

        var outcome = await new RequestCoordinator(provider, new ScenarioHost())
            .AuthenticateAsync(new AuthenticationRequest(Email, [Scope], false, null));

        Assert.IsNull(outcome.Failure);
        Assert.IsNotNull(outcome.Success);
        Assert.AreEqual("SELECTED@example.test", outcome.Success.Email);
        Assert.AreEqual(Now.AddTicks(1), outcome.Success.ExpiresOn);
        Assert.AreEqual(Tenant, outcome.Success.Tenant);
        Assert.AreEqual("Bearer", outcome.Success.TokenType);
        Assert.AreEqual("synthetic-selected-access", outcome.Success.AccessToken);
        CollectionAssert.AreEqual(
            new[] { Scope, "499b84ac-1321-427f-aa17-267ca6975798/extra_permission" },
            outcome.Success.Scopes.ToArray());
        Assert.IsTrue(outcome.PersistenceUnconfirmed);
    }

    [TestMethod]
    [DataRow(false, "")]
    [DataRow(false, null)]
    [DataRow(true, "")]
    [DataRow(true, null)]
    public async Task MalformedExtraGrantedScopeCannotEnterSuccessfulMetadata(bool defaultPermission, string? malformed)
    {
        var provider = new CandidateProvider(candidate => candidate with { Scopes = [Scope, malformed!] });
        var requested = defaultPermission ? "499b84ac-1321-427f-aa17-267ca6975798/.default" : Scope;

        var outcome = await new RequestCoordinator(provider, new ScenarioHost())
            .AuthenticateAsync(new AuthenticationRequest(Email, [requested], false, null));

        Assert.AreEqual(AuthenticationFailure.IdentityValidationFailed, outcome.Failure);
        Assert.IsNull(outcome.Success);
    }

    [TestMethod]
    public async Task DefaultPermissionMayRetainAnEmptyGrantArrayFromTheSameOperation()
    {
        var provider = new CandidateProvider(candidate => candidate with { Scopes = [] });

        var outcome = await new RequestCoordinator(provider, new ScenarioHost())
            .AuthenticateAsync(new AuthenticationRequest(
                Email, ["499b84ac-1321-427f-aa17-267ca6975798/.default"], false, null));

        Assert.IsNull(outcome.Failure);
        Assert.IsNotNull(outcome.Success);
        Assert.IsEmpty(outcome.Success.Scopes);
    }

    [TestMethod]
    public async Task DefaultPermissionsAcceptExpandedGrantsFromTheSameOperation()
    {
        var provider = new CandidateProvider(candidate => candidate with { Scopes = [Scope] });

        var outcome = await new RequestCoordinator(provider, new ScenarioHost())
            .AuthenticateAsync(new AuthenticationRequest(
                Email, ["499b84ac-1321-427f-aa17-267ca6975798/.default"], false, null));

        Assert.IsNull(outcome.Failure);
        Assert.IsNotNull(outcome.Success);
        CollectionAssert.AreEqual(new[] { Scope }, outcome.Success.Scopes.ToArray());
    }

    [TestMethod]
    public async Task DefaultPermissionsCannotAcceptAnUnrelatedOperation()
    {
        var provider = new CandidateProvider(candidate => candidate with { OperationId = Guid.Empty });

        var outcome = await new RequestCoordinator(provider, new ScenarioHost())
            .AuthenticateAsync(new AuthenticationRequest(
                Email, ["499b84ac-1321-427f-aa17-267ca6975798/.default"], false, null));

        Assert.AreEqual(AuthenticationFailure.IdentityValidationFailed, outcome.Failure);
        Assert.IsNull(outcome.Success);
    }

    [TestMethod]
    public async Task PartialGrantOfSeveralDynamicScopesCannotSucceed()
    {
        const string second = "499b84ac-1321-427f-aa17-267ca6975798/vso.code";
        var provider = new CandidateProvider(candidate => candidate with { Scopes = [Scope] });

        var outcome = await new RequestCoordinator(provider, new ScenarioHost())
            .AuthenticateAsync(new AuthenticationRequest(Email, [Scope, second], false, null));

        Assert.AreEqual(AuthenticationFailure.IdentityValidationFailed, outcome.Failure);
        Assert.IsNull(outcome.Success);
    }

    [TestMethod]
    public async Task CompleteGrantOfSeveralDynamicScopesSucceeds()
    {
        const string second = "499b84ac-1321-427f-aa17-267ca6975798/vso.code";
        var provider = new CandidateProvider(candidate => candidate with { Scopes = [second, Scope] });

        var outcome = await new RequestCoordinator(provider, new ScenarioHost())
            .AuthenticateAsync(new AuthenticationRequest(Email, [Scope, second], false, null));

        Assert.IsNull(outcome.Failure);
        Assert.IsNotNull(outcome.Success);
    }

    private sealed class CandidateProvider(Func<TokenCandidate, TokenCandidate> transform)
        : IAuthenticationProvider
    {
        public Task<IReadOnlyList<ProviderAccount>> GetAccountsAsync(CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<ProviderAccount>>([new(Email, new object())]);

        public Task<TokenCandidate> AcquireSilentAsync(
            AuthenticationRequest request,
            ProviderAccount account,
            Guid operationId,
            CancellationToken cancellationToken) => Task.FromResult(transform(new TokenCandidate(
                "synthetic-selected-access", Email, Tenant, [Scope], "Bearer", Now.AddMinutes(10), operationId)));
        public Task<TokenCandidate> AcquireInteractiveAsync(AuthenticationRequest request, ProviderAccount? account,
            string? claims, nint parentWindow, Guid operationId, CancellationToken cancellationToken) =>
            Task.FromException<TokenCandidate>(new ProviderFailureException(AuthenticationFailure.MechanismUnavailable));
    }

    private sealed class ScenarioHost : IRequestHost
    {
        public TimeProvider Clock { get; } = new ScenarioClock();
        public Task<nint> OpenInteractionAsync(CancellationToken cancellationToken) => Task.FromResult<nint>(0);
        public Task CloseInteractionAsync() => Task.CompletedTask;
    }

    private sealed class ScenarioClock : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => Now;
    }
}
