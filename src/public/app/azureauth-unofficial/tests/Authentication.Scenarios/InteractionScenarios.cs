using Authentication.Core;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Authentication.Scenarios;

// Exercise caller-visible outcomes and required provider/UI effects at the application
// boundary. The host and provider are synthetic; no window or account store is used.
[TestClass]
public sealed class InteractionScenarios
{
    private const string Email = "personal@example.test";
    private const string WorkEmail = "work@example.test";
    private const string Scope = "499b84ac-1321-427f-aa17-267ca6975798/user_impersonation";
    private static readonly Guid Tenant = new("11111111-2222-3333-4444-555555555555");
    private static readonly DateTimeOffset Now = new(2026, 9, 13, 0, 0, 0, TimeSpan.Zero);

    [TestMethod]
    public async Task PermissionDoesNotCreateUiWhenSelectedAccountSilentReuseSucceeds()
    {
        var scene = new Scene();
        var outcome = await scene.RunAsync();

        Assert.IsNotNull(outcome.Success);
        Assert.IsNull(outcome.Failure);
        Assert.IsFalse(outcome.Interactive);
        CollectionAssert.AreEqual(new[] { "discover", "silent" }, scene.Effects.ToArray());
    }

    [TestMethod]
    public async Task ForcedInteractionSkipsSuccessfulSilentReuseAndHandsTheDiscoveredAccountToTheProvider()
    {
        var scene = new Scene();
        var outcome = await scene.RunAsync(forceInteractive: true);

        // Core hands the account across the provider interface; the Windows adapter
        // passes neither it nor a login hint for forced interaction (see
        // MsalCompositionScenarios).
        Assert.IsNotNull(outcome.Success);
        Assert.IsTrue(outcome.Interactive);
        Assert.AreSame(scene.Accounts[0], scene.InteractiveAccount);
        Assert.IsNull(scene.InteractiveClaims);
        Assert.IsTrue(scene.InteractiveForce);
        CollectionAssert.AreEqual(new[] { "discover", "open", "interactive", "close" }, scene.Effects.ToArray());
    }

    [TestMethod]
    public async Task ForcedInteractionCannotOverrideProhibitedInteraction()
    {
        var scene = new Scene();
        var outcome = await scene.RunAsync(interactionAllowed: false, forceInteractive: true);
        Assert.AreEqual(AuthenticationFailure.InvalidRequest, outcome.Failure);
        Assert.IsNull(outcome.Success);
        Assert.IsEmpty(scene.Effects);
    }

    [TestMethod]
    public async Task OriginalCancellationWithholdsForcedSuccessAndClosesTheOwnedParent()
    {
        using var original = new CancellationTokenSource();
        var scene = new Scene { DuringInteraction = () => original.Cancel() };
        var outcome = await scene.RunAsync(forceInteractive: true, cancellationToken: original.Token);
        Assert.AreEqual(AuthenticationFailure.Cancelled, outcome.Failure);
        Assert.IsNull(outcome.Success);
        Assert.AreEqual(original.Token, scene.InteractiveCancellation);
        CollectionAssert.AreEqual(new[] { "discover", "open", "interactive", "close" }, scene.Effects.ToArray());
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task MissingSelectedAccountUsesOnePermittedInteractiveCallWithoutSubstitutingWorkAccount(bool forced)
    {
        var scene = new Scene { Accounts = [new(WorkEmail, new object())] };
        var outcome = await scene.RunAsync(forceInteractive: forced);

        Assert.IsNotNull(outcome.Success);
        Assert.AreEqual(Email, outcome.Success.Email);
        Assert.IsTrue(outcome.Interactive);
        Assert.IsTrue(outcome.PersistenceUnconfirmed);
        Assert.IsNull(scene.InteractiveAccount);
        Assert.IsNull(scene.InteractiveClaims);
        Assert.AreEqual(Email, scene.InteractiveEmail);
        Assert.AreEqual(scene.ParentWindow, scene.InteractiveParent);
        Assert.AreEqual(forced, scene.InteractiveForce);
        CollectionAssert.AreEqual(new[] { "discover", "open", "interactive", "close" }, scene.Effects.ToArray());
    }

    [TestMethod]
    public async Task SilentInteractionRequirementPreservesSelectedAccountAndRequestLocalClaims()
    {
        var scene = new Scene
        {
            SilentFailure = new ProviderFailureException(AuthenticationFailure.InteractionRequired, "synthetic-claims"),
        };
        var outcome = await scene.RunAsync();

        Assert.IsNotNull(outcome.Success);
        Assert.IsTrue(outcome.Interactive);
        Assert.AreSame(scene.Accounts[0], scene.InteractiveAccount);
        Assert.AreEqual("synthetic-claims", scene.InteractiveClaims);
        Assert.AreEqual(Email, scene.InteractiveEmail);
        CollectionAssert.AreEqual(new[] { "discover", "silent", "open", "interactive", "close" }, scene.Effects.ToArray());
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task ProhibitedInteractionNeverCreatesUiOrAcquiresInteractively(bool accountVisible)
    {
        var scene = new Scene
        {
            Accounts = accountVisible ? [new(Email, new object())] : [],
            SilentFailure = new ProviderFailureException(AuthenticationFailure.InteractionRequired),
        };
        var outcome = await scene.RunAsync(interactionAllowed: false);

        Assert.AreEqual(AuthenticationFailure.InteractionRequired, outcome.Failure);
        Assert.IsNull(outcome.Success);
        CollectionAssert.AreEqual(accountVisible ? new[] { "discover", "silent" } : ["discover"], scene.Effects.ToArray());
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task PermissionCannotResolveAmbiguityByOpeningAnAccountPicker(bool forced)
    {
        var scene = new Scene { Accounts = [new(Email, new object()), new(Email.ToUpperInvariant(), new object())] };
        var outcome = await scene.RunAsync(forceInteractive: forced);

        Assert.AreEqual(AuthenticationFailure.AccountAmbiguous, outcome.Failure);
        Assert.IsNull(outcome.Success);
        CollectionAssert.AreEqual(new[] { "discover" }, scene.Effects.ToArray());
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task ParentCreationFailurePreventsTheInteractiveCall(bool forced)
    {
        var scene = new Scene { Accounts = [], ParentWindow = 0 };
        var outcome = await scene.RunAsync(forceInteractive: forced);

        Assert.AreEqual(AuthenticationFailure.MechanismUnavailable, outcome.Failure);
        Assert.IsNull(outcome.Success);
        Assert.IsFalse(scene.Effects.Contains("interactive"));
    }

    [TestMethod]
    public async Task InteractiveAcquisitionWaitsForTheOwnedParentToBecomeReady()
    {
        var readiness = new TaskCompletionSource<nint>(TaskCreationOptions.RunContinuationsAsynchronously);
        var scene = new Scene { Accounts = [], ParentReadiness = readiness.Task };
        var operation = scene.RunAsync();

        try
        {
            Assert.IsFalse(operation.IsCompleted, "Authentication must await its owned parent.");
            Assert.IsFalse(scene.Effects.Contains("interactive"));
        }
        finally
        {
            readiness.SetResult(scene.ParentWindow);
        }

        var outcome = await operation;
        Assert.IsNotNull(outcome.Success);
        Assert.AreEqual(scene.ParentWindow, scene.InteractiveParent);
        CollectionAssert.AreEqual(new[] { "discover", "open", "interactive", "close" }, scene.Effects.ToArray());
    }

    [TestMethod]
    [DataRow(AuthenticationFailure.Cancelled)]
    [DataRow(AuthenticationFailure.Denied)]
    [DataRow(AuthenticationFailure.MechanismUnavailable)]
    [DataRow(AuthenticationFailure.TemporarilyUnavailable)]
    [DataRow(AuthenticationFailure.InternalFailure)]
    public async Task TerminalSilentFailuresDoNotOpenUiOrRetry(AuthenticationFailure failure)
    {
        var scene = new Scene { SilentFailure = new ProviderFailureException(failure) };
        var outcome = await scene.RunAsync();

        Assert.AreEqual(failure, outcome.Failure);
        Assert.IsNull(outcome.Success);
        CollectionAssert.AreEqual(new[] { "discover", "silent" }, scene.Effects.ToArray());
    }

    [TestMethod]
    [DataRow(AuthenticationFailure.Timeout, false)]
    [DataRow(AuthenticationFailure.InvalidRequest, false)]
    [DataRow((AuthenticationFailure)999, false)]
    [DataRow(AuthenticationFailure.Timeout, true)]
    [DataRow(AuthenticationFailure.InvalidRequest, true)]
    [DataRow((AuthenticationFailure)999, true)]
    public async Task ProviderCannotReportEngineOwnedOrUndefinedOutcomes(AuthenticationFailure reported, bool interactive)
    {
        var failure = new ProviderFailureException(reported, "synthetic-claims", AuthenticationReason.InvalidConfiguration);
        var scene = interactive
            ? new Scene { Accounts = [], InteractiveFailure = failure }
            : new Scene { SilentFailure = failure };
        var outcome = await scene.RunAsync();

        Assert.AreEqual(AuthenticationFailure.InternalFailure, outcome.Failure);
        Assert.AreEqual(AuthenticationReason.None, outcome.Reason);
        Assert.IsNull(outcome.Success);
        CollectionAssert.AreEqual(interactive
            ? new[] { "discover", "open", "interactive", "close" }
            : new[] { "discover", "silent" }, scene.Effects.ToArray());
    }

    [TestMethod]
    [DataRow(AuthenticationFailure.InteractionRequired, false)]
    [DataRow(AuthenticationFailure.Cancelled, false)]
    [DataRow(AuthenticationFailure.Denied, false)]
    [DataRow(AuthenticationFailure.MechanismUnavailable, false)]
    [DataRow(AuthenticationFailure.TemporarilyUnavailable, false)]
    [DataRow(AuthenticationFailure.InternalFailure, false)]
    [DataRow(AuthenticationFailure.InteractionRequired, true)]
    [DataRow(AuthenticationFailure.Cancelled, true)]
    [DataRow(AuthenticationFailure.Denied, true)]
    [DataRow(AuthenticationFailure.MechanismUnavailable, true)]
    [DataRow(AuthenticationFailure.TemporarilyUnavailable, true)]
    [DataRow(AuthenticationFailure.InternalFailure, true)]
    public async Task InteractiveFailureClosesOwnedUiWithoutAnotherAttempt(AuthenticationFailure failure, bool forced)
    {
        var scene = new Scene { Accounts = [], InteractiveFailure = new ProviderFailureException(failure, "synthetic-claims") };
        var outcome = await scene.RunAsync(forceInteractive: forced);

        Assert.AreEqual(failure, outcome.Failure);
        Assert.IsNull(outcome.Success);
        CollectionAssert.AreEqual(new[] { "discover", "open", "interactive", "close" }, scene.Effects.ToArray());
    }

    [TestMethod]
    [DataRow("wrong-email", false)]
    [DataRow("wrong-tenant", false)]
    [DataRow("wrong-operation", false)]
    [DataRow("wrong-email", true)]
    [DataRow("wrong-tenant", true)]
    [DataRow("wrong-operation", true)]
    public async Task InteractiveSuccessStillMustSatisfyTheOriginalRequest(string defect, bool forced)
    {
        var scene = new Scene { Accounts = [], InteractiveDefect = defect };
        var outcome = await scene.RunAsync(forceInteractive: forced);

        Assert.AreEqual(AuthenticationFailure.IdentityValidationFailed, outcome.Failure);
        Assert.IsNull(outcome.Success);
        CollectionAssert.AreEqual(new[] { "discover", "open", "interactive", "close" }, scene.Effects.ToArray());
    }

    [TestMethod]
    [DataRow("discover")]
    [DataRow("silent")]
    [DataRow("interactive")]
    public async Task UnrecognizedProviderFailureBecomesSafeInternalFailure(string phase)
    {
        var scene = new Scene
        {
            Accounts = phase == "interactive" ? [] : [new(Email, new object())],
            UnexpectedFailurePhase = phase,
        };
        var outcome = await scene.RunAsync();

        Assert.AreEqual(AuthenticationFailure.InternalFailure, outcome.Failure);
        Assert.IsNull(outcome.Success);
        Assert.IsFalse(outcome.ToString().Contains("synthetic-private-marker", StringComparison.Ordinal));
        if (phase == "interactive") Assert.AreEqual("close", scene.Effects[^1]);
    }

    [TestMethod]
    [DataRow(AuthenticationFailure.Cancelled)]
    [DataRow(AuthenticationFailure.Denied)]
    [DataRow(AuthenticationFailure.MechanismUnavailable)]
    [DataRow(AuthenticationFailure.TemporarilyUnavailable)]
    [DataRow(AuthenticationFailure.IdentityValidationFailed)]
    public async Task TypedDiscoveryFailureEndsTheRequestBeforeAnyAcquisition(AuthenticationFailure failure)
    {
        var scene = new Scene { DiscoveryFailure = new ProviderFailureException(failure, "synthetic-claims") };
        var outcome = await scene.RunAsync();

        Assert.AreEqual(failure, outcome.Failure);
        Assert.IsNull(outcome.Success);
        CollectionAssert.AreEqual(new[] { "discover" }, scene.Effects.ToArray());
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task DiscoveryStageInteractionRequirementIsTerminalEvenWhenInteractionIsPermitted(bool forced)
    {
        var scene = new Scene
        {
            DiscoveryFailure = new ProviderFailureException(AuthenticationFailure.InteractionRequired,
                "synthetic-claims", AuthenticationReason.ConsentRequired),
        };
        var outcome = await scene.RunAsync(forceInteractive: forced);

        Assert.AreEqual(AuthenticationFailure.InteractionRequired, outcome.Failure);
        Assert.AreEqual(AuthenticationReason.ConsentRequired, outcome.Reason);
        Assert.IsNull(outcome.Success);
        Assert.IsFalse(outcome.Interactive);
        CollectionAssert.AreEqual(new[] { "discover" }, scene.Effects.ToArray());
    }

    [TestMethod]
    public async Task DiscoveredAccountWithoutEmailIsNotTheRequestedAccount()
    {
        var scene = new Scene { Accounts = [new(null, new object())] };
        var outcome = await scene.RunAsync(interactionAllowed: false);

        Assert.AreEqual(AuthenticationFailure.InteractionRequired, outcome.Failure);
        Assert.IsNull(outcome.Success);
        CollectionAssert.AreEqual(new[] { "discover" }, scene.Effects.ToArray());
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task NullProviderCandidateCannotBecomeSuccess(bool interactive)
    {
        var scene = new Scene { Accounts = interactive ? [] : [new(Email, new object())], NullCandidate = true };
        var outcome = await scene.RunAsync();

        Assert.AreEqual(AuthenticationFailure.IdentityValidationFailed, outcome.Failure);
        Assert.IsNull(outcome.Success);
        Assert.AreEqual(interactive, outcome.Interactive);
    }

    [TestMethod]
    public async Task FailingInteractionOpeningPreventsTheInteractiveCallAndStillCloses()
    {
        var scene = new Scene { Accounts = [], OpenFailure = new InvalidOperationException("synthetic-private-marker") };
        var outcome = await scene.RunAsync();

        Assert.AreEqual(AuthenticationFailure.InternalFailure, outcome.Failure);
        Assert.IsNull(outcome.Success);
        Assert.IsFalse(outcome.Interactive);
        CollectionAssert.AreEqual(new[] { "discover", "open", "close" }, scene.Effects.ToArray());
    }

    [TestMethod]
    public async Task CloseFailureAfterInteractiveSuccessWithholdsTheToken()
    {
        var scene = new Scene { Accounts = [], CloseFailure = new InvalidOperationException("synthetic-private-marker") };
        var outcome = await scene.RunAsync();

        Assert.AreEqual(AuthenticationFailure.InternalFailure, outcome.Failure);
        Assert.IsNull(outcome.Success);
        Assert.IsTrue(outcome.Interactive);
        Assert.AreEqual(1, ResultProjection.Serialize(outcome).ExitCode);
        CollectionAssert.AreEqual(new[] { "discover", "open", "interactive", "close" }, scene.Effects.ToArray());
    }

    [TestMethod]
    [DataRow(AuthenticationFailure.Denied)]
    [DataRow(AuthenticationFailure.Cancelled)]
    public async Task CloseFailureAfterInteractiveFailureKeepsTheEarlierFailure(AuthenticationFailure failure)
    {
        var scene = new Scene
        {
            Accounts = [],
            InteractiveFailure = new ProviderFailureException(failure),
            CloseFailure = new InvalidOperationException("synthetic-private-marker"),
        };
        var outcome = await scene.RunAsync();

        Assert.AreEqual(failure, outcome.Failure);
        Assert.IsNull(outcome.Success);
        CollectionAssert.AreEqual(new[] { "discover", "open", "interactive", "close" }, scene.Effects.ToArray());
    }

    private sealed class Scene : IAuthenticationProvider, IRequestHost
    {
        public IReadOnlyList<ProviderAccount> Accounts { get; init; } = [new(Email, new object())];
        public Exception? DiscoveryFailure { get; init; }
        public Exception? SilentFailure { get; init; }
        public Exception? InteractiveFailure { get; init; }
        public Exception? OpenFailure { get; init; }
        public Exception? CloseFailure { get; init; }
        public bool NullCandidate { get; init; }
        public string? UnexpectedFailurePhase { get; init; }
        public string? InteractiveDefect { get; init; }
        public nint ParentWindow { get; init; } = 42;
        public Task<nint>? ParentReadiness { get; init; }
        public List<string> Effects { get; } = [];
        public ProviderAccount? InteractiveAccount { get; private set; }
        public string? InteractiveEmail { get; private set; }
        public string? InteractiveClaims { get; private set; }
        public nint InteractiveParent { get; private set; }
        public bool InteractiveForce { get; private set; }
        public CancellationToken InteractiveCancellation { get; private set; }
        public Action? DuringInteraction { get; init; }
        public TimeProvider Clock { get; } = new ScenarioClock();

        public async Task<AuthenticationOutcome> RunAsync(bool interactionAllowed = true, bool forceInteractive = false,
            CancellationToken cancellationToken = default)
        {
            try
            {
                return await new RequestCoordinator(this, this).AuthenticateAsync(
                    new(Email, [Scope], interactionAllowed, Tenant, forceInteractive), cancellationToken);
            }
            catch (Exception)
            {
                Assert.Fail("Provider failures must become an authentication outcome without escaping the application boundary.");
                throw;
            }
        }

        public Task<IReadOnlyList<ProviderAccount>> GetAccountsAsync(CancellationToken cancellationToken)
        {
            Effects.Add("discover");
            FailIfRequested("discover");
            if (DiscoveryFailure is not null) throw DiscoveryFailure;
            return Task.FromResult(Accounts);
        }

        public Task<TokenCandidate> AcquireSilentAsync(AuthenticationRequest request, ProviderAccount account,
            Guid operationId, CancellationToken cancellationToken)
        {
            Effects.Add("silent");
            FailIfRequested("silent");
            if (SilentFailure is not null) throw SilentFailure;
            return Task.FromResult(NullCandidate ? null! : Candidate(operationId));
        }

        public Task<TokenCandidate> AcquireInteractiveAsync(AuthenticationRequest request, ProviderAccount? account,
            string? claims, nint parentWindow, Guid operationId, CancellationToken cancellationToken)
        {
            Effects.Add("interactive");
            InteractiveParent = parentWindow;
            InteractiveAccount = account;
            InteractiveEmail = request.AccountEmail;
            InteractiveClaims = claims;
            InteractiveForce = request.ForceInteractive;
            InteractiveCancellation = cancellationToken;
            DuringInteraction?.Invoke();
            FailIfRequested("interactive");
            if (InteractiveFailure is not null) throw InteractiveFailure;
            if (NullCandidate) return Task.FromResult<TokenCandidate>(null!);
            var candidate = Candidate(operationId);
            return Task.FromResult(InteractiveDefect switch
            {
                "wrong-email" => candidate with { Email = WorkEmail },
                "wrong-tenant" => candidate with { Tenant = Guid.Empty },
                "wrong-operation" => candidate with { OperationId = Guid.Empty },
                _ => candidate,
            });
        }

        public Task<nint> OpenInteractionAsync(CancellationToken cancellationToken)
        {
            Effects.Add("open");
            if (OpenFailure is not null) throw OpenFailure;
            return ParentReadiness ?? Task.FromResult(ParentWindow);
        }

        public Task CloseInteractionAsync()
        {
            Effects.Add("close");
            if (CloseFailure is not null) throw CloseFailure;
            return Task.CompletedTask;
        }

        private void FailIfRequested(string phase)
        {
            if (UnexpectedFailurePhase == phase) throw new InvalidOperationException("synthetic-private-marker");
        }

        private static TokenCandidate Candidate(Guid operationId) =>
            new("synthetic-interactive-access", Email, Tenant, [Scope], "Bearer", Now.AddMinutes(5), operationId);
    }

    private sealed class ScenarioClock : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => Now;
    }
}
