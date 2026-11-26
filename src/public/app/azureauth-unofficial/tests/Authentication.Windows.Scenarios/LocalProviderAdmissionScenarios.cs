using System.Text;
using Authentication.Core;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Authentication.Windows.Scenarios;

// The real coordinator and provider boundary use synthetic admission, initialization
// and acquisition outcomes. Admission gate behavior is exercised through the real
// WindowsHostAdmission in WindowsHostAdmissionScenarios; these cases cover only the
// provider boundary's own construction, initialization and continuation. No Windows
// query, MSAL application, account store, window, process, file or network is used.
[TestClass]
public sealed class LocalProviderAdmissionScenarios
{
    private const string Email = "personal@example.test";
    private const string Scope = "499b84ac-1321-427f-aa17-267ca6975798/user_impersonation";
    private const string PrivateMarker = "SYNTHETIC-LOCAL-ADMISSION-PRIVATE-MARKER";
    private static readonly Guid Tenant = new("11111111-2222-3333-4444-555555555555");
    private static readonly DateTimeOffset Now = new(2026, 9, 13, 0, 0, 0, TimeSpan.Zero);

    [TestMethod]
    public void ConstructionDoesNotObserveHostOrInitializeProvider()
    {
        using var scene = new Scene();
        _ = scene.CreateBoundary();
        Assert.AreEqual(0, scene.Admissions);
        Assert.AreEqual(0, scene.Rechecks);
        NoProviderEffects(scene);
    }

    [TestMethod]
    public async Task CancellationDuringInitializationPreventsDiscovery()
    {
        using var scene = new Scene();
        scene.DuringInitialization = _ => scene.Original.Cancel();
        Failure(AuthenticationFailure.Cancelled, await scene.RunAsync());
        Assert.AreEqual(1, scene.Initializations);
        Assert.AreEqual(0, scene.Discoveries);
        Assert.AreEqual(0, scene.SilentCalls);
        Assert.AreEqual(0, scene.Opened);
    }

    [TestMethod]
    public async Task UnavailableInitializationPreventsDiscoveryAndOwnedUi()
    {
        using var scene = new Scene { DuringInitialization = _ => throw Unavailable() };
        Failure(AuthenticationFailure.MechanismUnavailable, await scene.RunAsync());
        Assert.AreEqual(1, scene.Initializations, "The admitted provider initializer was not reached.");
        Assert.AreEqual(0, scene.Discoveries);
        Assert.AreEqual(0, scene.SilentCalls);
        Assert.AreEqual(0, scene.InteractiveCalls);
        Assert.AreEqual(0, scene.Opened);
    }

    [TestMethod]
    public async Task UnexpectedInitializationFaultStaysInternalFailure()
    {
        using var scene = new Scene
        {
            DuringInitialization = _ => throw new InvalidOperationException(PrivateMarker),
        };
        Failure(AuthenticationFailure.InternalFailure, await scene.RunAsync());
        Assert.AreEqual(1, scene.Initializations);
        Assert.AreEqual(0, scene.Discoveries);
        Assert.AreEqual(0, scene.Opened);
    }

    [TestMethod]
    public async Task EligibleInteractiveContinuationUsesOriginalRequestAndOneParent()
    {
        using var scene = new Scene { RequiresInteraction = true };
        var outcome = await scene.RunAsync();
        Assert.IsNotNull(outcome.Success, "The permitted interactive continuation was not reached.");
        Assert.IsTrue(outcome.Interactive);
        Assert.AreEqual(1, scene.Admissions);
        Assert.AreEqual(1, scene.Initializations);
        Assert.AreEqual(1, scene.Discoveries);
        Assert.AreEqual(1, scene.SilentCalls);
        Assert.AreEqual(1, scene.InteractiveCalls);
        Assert.AreEqual(1, scene.Opened);
        Assert.AreEqual(1, scene.Closed);
        Assert.IsTrue(scene.Tokens.All(token => token == scene.Original.Token));
    }

    [TestMethod]
    public async Task NoninteractivePermissionDoesNotOpenHostAfterSilentChallenge()
    {
        using var scene = new Scene { RequiresInteraction = true };
        Failure(AuthenticationFailure.InteractionRequired, await scene.RunAsync(interactionAllowed: false));
        Assert.AreEqual(1, scene.Discoveries);
        Assert.AreEqual(1, scene.SilentCalls);
        Assert.AreEqual(0, scene.InteractiveCalls);
        Assert.AreEqual(0, scene.Opened);
    }

    private static ProviderFailureException Unavailable() => new(AuthenticationFailure.MechanismUnavailable);

    private static void Failure(AuthenticationFailure expected, AuthenticationOutcome outcome)
    {
        Assert.AreEqual(expected, outcome.Failure);
        Assert.IsNull(outcome.Success);
        var json = Encoding.UTF8.GetString(ResultProjection.Serialize(outcome).Utf8Json);
        Assert.IsFalse(json.Contains(PrivateMarker, StringComparison.Ordinal));
        Assert.IsFalse(json.Contains(Email, StringComparison.Ordinal));
    }

    private static void NoProviderEffects(Scene scene)
    {
        Assert.AreEqual(0, scene.Initializations);
        Assert.AreEqual(0, scene.Discoveries);
        Assert.AreEqual(0, scene.SilentCalls);
        Assert.AreEqual(0, scene.InteractiveCalls);
    }

    private sealed class Scene : IWindowsHostAdmission, IAuthenticationProvider, IRequestHost, IDisposable
    {
        internal CancellationTokenSource Original { get; } = new();
        internal List<CancellationToken> Tokens { get; } = [];
        internal Action<CancellationToken>? DuringInitialization { get; set; }
        internal bool RequiresInteraction { get; set; }
        internal int Admissions, Rechecks, Initializations, Discoveries, SilentCalls, InteractiveCalls, Opened, Closed;
        private readonly ProviderAccount account = new(Email, new object());
        public TimeProvider Clock { get; } = new FixedClock();

        internal LocalWindowsProvider CreateBoundary() => new(this, token =>
        {
            Initializations++;
            Tokens.Add(token);
            DuringInitialization?.Invoke(token);
            return this;
        });

        internal Task<AuthenticationOutcome> RunAsync(bool interactionAllowed = true) =>
            new RequestCoordinator(CreateBoundary(), this).AuthenticateAsync(
                new(Email, [Scope], interactionAllowed, Tenant), Original.Token);

        public void Admit(CancellationToken cancellationToken)
        {
            Admissions++;
            Tokens.Add(cancellationToken);
        }

        public void Recheck(CancellationToken cancellationToken)
        {
            Rechecks++;
            Tokens.Add(cancellationToken);
        }

        // These fixtures deliberately return after cancellation: the production
        // boundary, not a cooperative substitute, must prevent the next effect.
        public Task<IReadOnlyList<ProviderAccount>> GetAccountsAsync(CancellationToken cancellationToken)
        {
            Discoveries++;
            Tokens.Add(cancellationToken);
            return Task.FromResult<IReadOnlyList<ProviderAccount>>([account]);
        }

        public Task<TokenCandidate> AcquireSilentAsync(AuthenticationRequest request,
            ProviderAccount selected, Guid operationId, CancellationToken cancellationToken)
        {
            SilentCalls++;
            Tokens.Add(cancellationToken);
            Assert.AreEqual(account.Email, selected.Email);
            Assert.AreSame(account.Handle, selected.Handle);
            return RequiresInteraction
                ? Task.FromException<TokenCandidate>(new ProviderFailureException(AuthenticationFailure.InteractionRequired))
                : Task.FromResult(Candidate(request, operationId));
        }

        public Task<TokenCandidate> AcquireInteractiveAsync(AuthenticationRequest request,
            ProviderAccount? selected, string? claims, nint parentWindow, Guid operationId,
            CancellationToken cancellationToken)
        {
            InteractiveCalls++;
            Tokens.Add(cancellationToken);
            Assert.IsNotNull(selected);
            Assert.AreEqual(account.Email, selected.Email);
            Assert.AreSame(account.Handle, selected.Handle);
            Assert.AreEqual((nint)1, parentWindow);
            Assert.AreEqual(Email, request.AccountEmail);
            Assert.AreEqual(Tenant, request.ExactTenant);
            CollectionAssert.AreEqual(new[] { Scope }, request.Scopes.ToArray());
            return Task.FromResult(Candidate(request, operationId));
        }

        public Task<nint> OpenInteractionAsync(CancellationToken cancellationToken)
        {
            Opened++;
            Tokens.Add(cancellationToken);
            return Task.FromResult<nint>(1); // Synthetic sentinel; never passed to a native API.
        }

        public Task CloseInteractionAsync()
        {
            Closed++;
            return Task.CompletedTask;
        }

        public void Dispose() => Original.Dispose();

        private static TokenCandidate Candidate(AuthenticationRequest request, Guid operationId) =>
            new("synthetic-token", Email, Tenant, request.Scopes, "Bearer", Now.AddHours(1), operationId);
    }

    private sealed class FixedClock : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => Now;
    }
}
