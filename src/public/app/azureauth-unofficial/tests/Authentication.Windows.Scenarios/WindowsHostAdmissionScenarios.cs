using System.Runtime.InteropServices;
using System.Text;
using Authentication.Core;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Authentication.Windows.Scenarios;

// Real admission/provider/coordinator boundaries consume synthetic local facts.
// No native query, Windows account metadata, window, MSAL application, file,
// network operation or child-product process is used by these scenarios.
[TestClass]
public sealed class WindowsHostAdmissionScenarios
{
    private const string Email = "personal@example.test";
    private const string Scope = "499b84ac-1321-427f-aa17-267ca6975798/user_impersonation";
    private const string PrivateMarker = "SYNTHETIC-HOST-OBSERVATION-PRIVATE-MARKER";
    private static readonly Guid Tenant = new("11111111-2222-3333-4444-555555555555");
    private static readonly DateTimeOffset Now = new(2026, 9, 14, 0, 0, 0, TimeSpan.Zero);
    private static readonly string[] AdmissionObservations =
        ["platform", "product", "thread", "logon", "session", "desktop"];

    [TestMethod]
    public void ConstructionDoesNotObserveLocalState()
    {
        using var scene = new Scene();
        _ = new WindowsHostAdmission(scene);
        Assert.AreEqual(0, scene.Observed.Count);
        NoProviderEffects(scene);
    }

    [TestMethod]
    public async Task PrecancelledRequestDoesNotObserveLocalState()
    {
        using var scene = new Scene();
        scene.Original.Cancel();
        Failure(AuthenticationFailure.Cancelled, await scene.RunAsync());
        Assert.AreEqual(0, scene.Observed.Count);
        NoProviderEffects(scene);
    }

    [TestMethod]
    public async Task OrdinaryInteractiveLogonKindsPermitSelectedAccountAcquisition()
    {
        foreach (var kind in new uint[] { 2, 10, 11, 12 })
        {
            using var scene = new Scene();
            scene.Logon = scene.Logon! with { LogonType = kind };
            if (kind != 2) scene.Platform = scene.Platform with { Version = new Version(10, 0, 26200) };
            var outcome = await scene.RunAsync();
            Assert.IsNotNull(outcome.Success, "An admitted ordinary-user host did not reach acquisition.");
            Assert.AreEqual(Email, outcome.Success.Email);
            Assert.AreEqual(1, scene.Initializations);
            Assert.AreEqual(1, scene.Discoveries);
            Assert.AreEqual(1, scene.SilentCalls);
            Assert.AreEqual(0, scene.Opened);
            Assert.IsTrue(scene.Tokens.All(token => token == scene.Original.Token));
        }
    }

    [TestMethod]
    public async Task UnsupportedPlatformStopsBeforeWindowsObservations()
    {
        var valid = ValidPlatform();
        foreach (var platform in new[]
        {
            valid with { IsWindows = false },
            valid with { ProcessArchitecture = Architecture.Arm64 },
            valid with { ProcessArchitecture = Architecture.X86 },
            valid with { OsArchitecture = Architecture.Arm64 },
            valid with { Version = new Version(10, 0, 21999) },
            valid with { Version = new Version(6, 3, 9600) },
            valid with { Version = new Version(10, 1, 22000) },
        })
        {
            using var scene = new Scene { Platform = platform };
            await RejectedAfter(scene, "platform");
            CollectionAssert.AreEqual(new[] { "platform" }, scene.Observed);
        }
    }

    [TestMethod]
    public async Task ServerOrUnobservableProductPreventsInitialization()
    {
        foreach (var product in new bool?[] { false, null })
        {
            using var scene = new Scene { Workstation = product };
            await RejectedAfter(scene, "product");
        }
    }

    [TestMethod]
    public async Task ImpersonationOrUnknownThreadIdentityPreventsInitialization()
    {
        foreach (var identity in new[] { WindowsThreadIdentity.Impersonating, WindowsThreadIdentity.Unavailable })
        {
            using var scene = new Scene { ThreadIdentity = identity };
            await RejectedAfter(scene, "thread");
        }
    }

    [TestMethod]
    public async Task MissingOrInvalidOwnLogonPreventsInitialization()
    {
        foreach (var logon in new WindowsLocalLogon?[]
        {
            null,
            new(2, WindowsLogonIdentity.Invalid, true, true),
        })
        {
            using var scene = new Scene { Logon = logon };
            await RejectedAfter(scene, "logon");
        }
    }

    [TestMethod]
    public async Task ServiceIdentitiesPrecludeAccountDiscovery()
    {
        foreach (var identity in new[]
        {
            WindowsLogonIdentity.LocalSystem, WindowsLogonIdentity.LocalService,
            WindowsLogonIdentity.NetworkService,
        })
        {
            using var scene = new Scene { Logon = new(2, identity, true, true) };
            await RejectedAfter(scene, "logon");
        }
    }

    [TestMethod]
    public async Task NoninteractiveAndAlternateCredentialLogonKindsAreRejected()
    {
        foreach (var kind in new uint[] { 0, 3, 4, 5, 8, 9, 13 })
        {
            using var scene = new Scene { Logon = new(kind, WindowsLogonIdentity.User, true, true) };
            await RejectedAfter(scene, "logon");
        }
    }

    [TestMethod]
    public async Task HiddenWindowStationPreventsInitialization()
    {
        using var scene = new Scene { Logon = new(2, WindowsLogonIdentity.User, false, true) };
        await RejectedAfter(scene, "logon");
    }

    [TestMethod]
    public async Task MissingOrDifferentWindowStationUserPreventsInitialization()
    {
        foreach (var matches in new bool?[] { false, null })
        {
            using var scene = new Scene { Logon = new(2, WindowsLogonIdentity.User, true, matches) };
            await RejectedAfter(scene, "logon");
        }
    }

    [TestMethod]
    public async Task InactiveOrUnobservableSessionPreventsInitialization()
    {
        foreach (var connection in new[]
        {
            WindowsSessionConnection.Inactive, WindowsSessionConnection.Unavailable,
            WindowsSessionConnection.ZeroSession, WindowsSessionConnection.Malformed,
        })
        {
            using var scene = new Scene { Session = connection };
            await RejectedAfter(scene, "session");
        }
    }

    [TestMethod]
    public async Task NoninputOrUnobservableDesktopPreventsInitialization()
    {
        foreach (var receivesInput in new bool?[] { false, null })
        {
            using var scene = new Scene { InputDesktop = receivesInput };
            await RejectedAfter(scene, "desktop");
        }
    }

    [TestMethod]
    public async Task CancellationAfterAnyObservationStopsFurtherQueriesAndInitialization()
    {
        foreach (var observation in AdmissionObservations)
        {
            using var scene = new Scene();
            scene.DuringObservation = name => { if (name == observation) scene.Original.Cancel(); };
            Failure(AuthenticationFailure.Cancelled, await scene.RunAsync());
            Assert.AreEqual(observation, scene.Observed.Last());
            NoProviderEffects(scene);
        }
    }

    [TestMethod]
    public async Task OriginalCancellationWinsWhenAnObservationThrows()
    {
        foreach (var observation in AdmissionObservations)
        {
            using var scene = new Scene();
            scene.DuringObservation = name =>
            {
                if (name != observation) return;
                scene.Original.Cancel();
                throw new InvalidOperationException(PrivateMarker);
            };
            Failure(AuthenticationFailure.Cancelled, await scene.RunAsync());
            Assert.AreEqual(observation, scene.Observed.Last());
            NoProviderEffects(scene);
        }
    }

    [TestMethod]
    public async Task UnexpectedObservationFaultRemainsSanitizedInternalFailure()
    {
        foreach (var observation in AdmissionObservations)
        {
            using var scene = new Scene();
            scene.DuringObservation = name =>
            {
                if (name == observation) throw new InvalidOperationException(PrivateMarker);
            };
            Failure(AuthenticationFailure.InternalFailure, await scene.RunAsync());
            NoProviderEffects(scene);
        }
    }

    [TestMethod]
    public async Task SessionLossBeforeSilentAcquisitionPreventsItsEffect()
    {
        using var scene = new Scene();
        scene.DuringObservation = _ =>
        {
            if (scene.Discoveries != 0) scene.Session = WindowsSessionConnection.Inactive;
        };
        Failure(AuthenticationFailure.MechanismUnavailable, await scene.RunAsync());
        Assert.AreEqual(1, scene.Discoveries, "Discovery was not reached before the volatile session change.");
        Assert.AreEqual(0, scene.SilentCalls);
        Assert.AreEqual(0, scene.Opened);
    }

    [TestMethod]
    public async Task ImpersonationBeforeSilentAcquisitionPreventsItsEffect()
    {
        using var scene = new Scene();
        scene.DuringObservation = _ =>
        {
            if (scene.Discoveries != 0) scene.ThreadIdentity = WindowsThreadIdentity.Impersonating;
        };
        Failure(AuthenticationFailure.MechanismUnavailable, await scene.RunAsync());
        Assert.AreEqual(1, scene.Discoveries, "Discovery was not reached before the volatile identity change.");
        Assert.AreEqual(0, scene.SilentCalls);
    }

    [TestMethod]
    public async Task InputDesktopLossAfterReadinessPreventsInteractionAndClosesParent()
    {
        using var scene = new Scene { RequiresInteraction = true };
        scene.DuringObservation = _ => { if (scene.Opened != 0) scene.InputDesktop = false; };
        Failure(AuthenticationFailure.MechanismUnavailable, await scene.RunAsync());
        Assert.AreEqual(1, scene.Opened, "The controlled parent was not reached before the desktop change.");
        Assert.AreEqual(1, scene.Closed);
        Assert.AreEqual(0, scene.InteractiveCalls);
    }

    [TestMethod]
    public async Task CancellationDuringVolatileObservationPreventsAcquisition()
    {
        using var scene = new Scene();
        scene.DuringObservation = name =>
        {
            if (scene.Discoveries != 0 && name == "thread") scene.Original.Cancel();
        };
        Failure(AuthenticationFailure.Cancelled, await scene.RunAsync());
        Assert.AreEqual(1, scene.Discoveries);
        Assert.AreEqual("thread", scene.Observed.Last());
        Assert.AreEqual(0, scene.SilentCalls);
    }

    [TestMethod]
    public async Task EachProviderEffectHasFreshVolatileObservations()
    {
        using var scene = new Scene { RequiresInteraction = true };
        var outcome = await scene.RunAsync();
        Assert.IsNotNull(outcome.Success, "The admitted interactive continuation was not completed.");
        Assert.IsTrue(outcome.Interactive);
        // Every provider effect, including the interactive one, follows its own fresh
        // volatile observations rather than relying on an earlier effect's.
        CollectionAssert.IsSubsetOf(new[] { "discover", "silent", "interactive" },
            scene.ProviderObservationSets.Select(set => set.Effect).ToArray());
        foreach (var (effect, observations) in scene.ProviderObservationSets)
            foreach (var required in new[] { "thread", "session", "desktop" })
                Assert.IsTrue(observations.Contains(required), $"{effect} lacked a fresh {required} observation.");
    }

    [TestMethod]
    public async Task ObservationsRunOnTheCallingThreadWithOriginalCancellation()
    {
        using var scene = new Scene { RequiresInteraction = true };
        var thread = Environment.CurrentManagedThreadId;
        scene.DuringObservation = _ => Assert.AreEqual(thread, Environment.CurrentManagedThreadId);
        var outcome = await scene.RunAsync();
        Assert.IsNotNull(outcome.Success, "Calling-thread observations did not release the request.");
        Assert.IsTrue(scene.Tokens.All(token => token == scene.Original.Token));
        Assert.AreEqual(1, scene.InteractiveCalls);
    }

    [TestMethod]
    public void FixedDiagnosticsIdentifyRejectedGateWithoutProviderEffects()
    {
        foreach (var failure in new[] { WindowsMechanismFailure.HostPlatform, WindowsMechanismFailure.HostWorkstation,
            WindowsMechanismFailure.HostThreadToken, WindowsMechanismFailure.HostLogon,
            WindowsMechanismFailure.HostSessionInactive, WindowsMechanismFailure.HostInputDesktopNotReceiving })
        {
            using var scene = new Scene();
            switch (failure)
            {
                case WindowsMechanismFailure.HostPlatform: scene.Platform = scene.Platform with { IsWindows = false }; break;
                case WindowsMechanismFailure.HostWorkstation: scene.Workstation = false; break;
                case WindowsMechanismFailure.HostThreadToken: scene.ThreadIdentity = WindowsThreadIdentity.Impersonating; break;
                case WindowsMechanismFailure.HostLogon: scene.Logon = null; break;
                case WindowsMechanismFailure.HostSessionInactive: scene.Session = WindowsSessionConnection.Inactive; break;
                case WindowsMechanismFailure.HostInputDesktopNotReceiving: scene.InputDesktop = false; break;
            }
            var trace = new WindowsMechanismTrace();
            var exception = Assert.ThrowsExactly<ProviderFailureException>(() => new WindowsHostAdmission(scene, trace).Admit(scene.Original.Token));
            Assert.AreEqual(AuthenticationFailure.MechanismUnavailable, exception.Failure);
            Assert.AreEqual(failure, trace.Failure);
            NoProviderEffects(scene);
            var indication = Encoding.ASCII.GetString(WindowsDiagnostics.MechanismIndication(trace.Failure));
            Assert.IsFalse(indication.Contains(PrivateMarker, StringComparison.Ordinal));
            Assert.IsFalse(indication.Contains(Email, StringComparison.Ordinal));
        }
    }

    [TestMethod]
    public void SessionAndDesktopDiagnosticsPreserveAdmissionAndRecheckBoundaries()
    {
        (WindowsSessionConnection Session, bool? Desktop, string Indication)[] cases =
        [
            (WindowsSessionConnection.Unavailable, true, "host_session_unavailable"),
            (WindowsSessionConnection.ZeroSession, true, "host_session_zero"),
            (WindowsSessionConnection.Inactive, true, "host_session_inactive"),
            (WindowsSessionConnection.Malformed, true, "host_session_malformed"),
            (WindowsSessionConnection.SessionIdUnavailable, true, "host_session_id_unavailable"),
            (WindowsSessionConnection.QueryUnavailable, true, "host_session_query_unavailable"),
            ((WindowsSessionConnection)int.MaxValue, true, "host_session"),
            (WindowsSessionConnection.Active, null, "host_input_desktop_unavailable"),
            (WindowsSessionConnection.Active, false, "host_input_desktop_not_receiving"),
        ];
        foreach (var (session, desktop, indication) in cases)
        foreach (var recheck in new[] { false, true })
        {
            using var scene = new Scene { Session = session, InputDesktop = desktop };
            var trace = new WindowsMechanismTrace();
            var admission = new WindowsHostAdmission(scene, trace);
            var exception = Assert.ThrowsExactly<ProviderFailureException>(() =>
            {
                if (recheck) admission.Recheck(scene.Original.Token);
                else admission.Admit(scene.Original.Token);
            });
            Assert.AreEqual(AuthenticationFailure.MechanismUnavailable, exception.Failure);
            Assert.AreEqual($"Mechanism unavailable at {indication}.\n",
                Encoding.ASCII.GetString(WindowsDiagnostics.MechanismIndication(trace.Failure)));
            Assert.AreEqual(session == WindowsSessionConnection.Active ? "desktop" : "session", scene.Observed.Last());
            NoProviderEffects(scene);
        }
    }

    [TestMethod]
    public void CancelledSessionAndDesktopObservationsDoNotPublishFailureCategories()
    {
        foreach (var recheck in new[] { false, true })
        foreach (var observation in new[] { "session", "desktop" })
        {
            using var scene = new Scene
            {
                Session = observation == "session" ? WindowsSessionConnection.QueryUnavailable : WindowsSessionConnection.Active,
                InputDesktop = null,
            };
            scene.DuringObservation = name => { if (name == observation) scene.Original.Cancel(); };
            var trace = new WindowsMechanismTrace();
            var admission = new WindowsHostAdmission(scene, trace);
            Assert.ThrowsExactly<OperationCanceledException>(() =>
            {
                if (recheck) admission.Recheck(scene.Original.Token);
                else admission.Admit(scene.Original.Token);
            });
            Assert.AreEqual(WindowsMechanismFailure.Unavailable, trace.Failure);
            NoProviderEffects(scene);
        }
    }

    [TestMethod]
    public void OwnLogonDiagnosticsDistinguishRequiredReadAndConditionFailures()
    {
        var valid = new WindowsLocalLogon(2, WindowsLogonIdentity.User, true, true);
        (WindowsLocalLogon? Logon, string Indication)[] cases =
        [
            (null, "host_logon"),
            (valid with { ReadFailure = WindowsLogonReadFailure.ProcessToken }, "host_logon_process_token"),
            (valid with { ReadFailure = WindowsLogonReadFailure.TokenStatistics }, "host_logon_token_statistics"),
            (valid with { ReadFailure = WindowsLogonReadFailure.SessionData }, "host_logon_session_data"),
            (valid with { ReadFailure = WindowsLogonReadFailure.SessionDataSize }, "host_logon_session_data_size"),
            (valid with { ReadFailure = WindowsLogonReadFailure.SessionId }, "host_logon_session_id"),
            (valid with { ReadFailure = WindowsLogonReadFailure.SessionSid }, "host_logon_session_sid"),
            (valid with { ReadFailure = (WindowsLogonReadFailure)int.MaxValue }, "host_logon"),
            (valid with { LogonType = 9 }, "host_logon_type"),
            (valid with { Identity = WindowsLogonIdentity.Invalid }, "host_logon_identity"),
            (valid with { Identity = WindowsLogonIdentity.LocalSystem }, "host_logon_identity"),
            (valid with { VisibleStation = false }, "host_station_visible"),
            (valid with { StationUserMatches = null }, "host_station_user_unavailable"),
            (valid with { StationUserMatches = false }, "host_station_user_mismatch"),
        ];
        foreach (var (logon, indication) in cases)
        {
            using var scene = new Scene { Logon = logon };
            var trace = new WindowsMechanismTrace();
            var exception = Assert.ThrowsExactly<ProviderFailureException>(() =>
                new WindowsHostAdmission(scene, trace).Admit(scene.Original.Token));
            Assert.AreEqual(AuthenticationFailure.MechanismUnavailable, exception.Failure);
            Assert.AreEqual($"Mechanism unavailable at {indication}.\n",
                Encoding.ASCII.GetString(WindowsDiagnostics.MechanismIndication(trace.Failure)));
            Assert.AreEqual("logon", scene.Observed.Last());
            NoProviderEffects(scene);
        }
    }

    [TestMethod]
    public void CancelledLogonReadDoesNotPublishItsFailureCategory()
    {
        foreach (var failure in Enum.GetValues<WindowsLogonReadFailure>())
        {
            using var scene = new Scene { Logon = new(0, WindowsLogonIdentity.Invalid, false, null, failure) };
            scene.DuringObservation = name => { if (name == "logon") scene.Original.Cancel(); };
            var trace = new WindowsMechanismTrace();
            Assert.ThrowsExactly<OperationCanceledException>(() =>
                new WindowsHostAdmission(scene, trace).Admit(scene.Original.Token));
            Assert.AreEqual(WindowsMechanismFailure.Unavailable, trace.Failure);
            NoProviderEffects(scene);
        }
    }

    [TestMethod]
    public void CancelledObservationDoesNotPublishRejectedGate()
    {
        using var scene = new Scene { Session = WindowsSessionConnection.Inactive };
        scene.DuringObservation = name => { if (name == "session") scene.Original.Cancel(); };
        var trace = new WindowsMechanismTrace();
        Assert.ThrowsExactly<OperationCanceledException>(() => new WindowsHostAdmission(scene, trace).Admit(scene.Original.Token));
        Assert.AreEqual(WindowsMechanismFailure.Unavailable, trace.Failure);
        NoProviderEffects(scene);
    }

    [TestMethod]
    public async Task SessionLossDuringDesktopObservationPreventsInitialization()
    {
        foreach (var session in new[] { WindowsSessionConnection.Inactive, WindowsSessionConnection.QueryUnavailable })
        {
            using var scene = new Scene();
            scene.DuringObservation = name => { if (name == "desktop") scene.Session = session; };
            Failure(AuthenticationFailure.MechanismUnavailable, await scene.RunAsync());
            CollectionAssert.AreEqual(new[] { "desktop", "session" }, scene.Observed.TakeLast(2).ToArray());
            NoProviderEffects(scene);
        }
    }

    [TestMethod]
    public void AdmissionAndRecheckRejectSessionLossAfterDesktopComparison()
    {
        foreach (var recheck in new[] { false, true })
        {
            using var scene = new Scene();
            scene.DuringObservation = name => { if (name == "desktop") scene.Session = WindowsSessionConnection.Inactive; };
            var trace = new WindowsMechanismTrace();
            var admission = new WindowsHostAdmission(scene, trace);
            var exception = Assert.ThrowsExactly<ProviderFailureException>(() =>
            {
                if (recheck) admission.Recheck(scene.Original.Token);
                else admission.Admit(scene.Original.Token);
            });
            Assert.AreEqual(AuthenticationFailure.MechanismUnavailable, exception.Failure);
            Assert.AreEqual(WindowsMechanismFailure.HostSessionInactive, trace.Failure);
            CollectionAssert.AreEqual(new[] { "desktop", "session" }, scene.Observed.TakeLast(2).ToArray());
            NoProviderEffects(scene);
        }
    }

    [TestMethod]
    public void CancellationAfterTrailingSessionObservationHasPrecedence()
    {
        foreach (var recheck in new[] { false, true })
        foreach (var throwing in new[] { false, true })
        {
            using var scene = new Scene();
            scene.DuringObservation = name =>
            {
                if (name != "session" || !scene.Observed.Contains("desktop")) return;
                scene.Original.Cancel();
                scene.Session = WindowsSessionConnection.Inactive;
                if (throwing) throw new InvalidOperationException(PrivateMarker);
            };
            var trace = new WindowsMechanismTrace();
            var admission = new WindowsHostAdmission(scene, trace);
            Assert.ThrowsExactly<OperationCanceledException>(() =>
            {
                if (recheck) admission.Recheck(scene.Original.Token);
                else admission.Admit(scene.Original.Token);
            });
            Assert.AreEqual(WindowsMechanismFailure.Unavailable, trace.Failure);
            NoProviderEffects(scene);
        }
    }

    private static WindowsHostPlatform ValidPlatform() => new(true, Architecture.X64, Architecture.X64, new(10, 0, 22000));

    private static async Task RejectedAfter(Scene scene, string observation)
    {
        Failure(AuthenticationFailure.MechanismUnavailable, await scene.RunAsync());
        Assert.IsTrue(scene.Observed.Contains(observation), $"The required {observation} observation was not reached.");
        Assert.AreEqual(observation, scene.Observed.Last());
        NoProviderEffects(scene);
        Assert.AreEqual(0, scene.Opened);
    }

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

    private sealed class Scene : IWindowsHostObservations, IAuthenticationProvider, IRequestHost, IDisposable
    {
        internal CancellationTokenSource Original { get; } = new();
        internal WindowsHostPlatform Platform { get; set; } = ValidPlatform();
        internal bool? Workstation { get; set; } = true;
        internal WindowsThreadIdentity ThreadIdentity { get; set; } = WindowsThreadIdentity.NoToken;
        internal WindowsLocalLogon? Logon { get; set; } = new(2, WindowsLogonIdentity.User, true, true);
        internal WindowsSessionConnection Session { get; set; } = WindowsSessionConnection.Active;
        internal bool? InputDesktop { get; set; } = true;
        internal bool RequiresInteraction { get; set; }
        internal Action<string>? DuringObservation { get; set; }
        internal List<string> Observed { get; } = [];
        internal List<CancellationToken> Tokens { get; } = [];
        internal List<(string Effect, string[] Observations)> ProviderObservationSets { get; } = [];
        private readonly List<string> observationsSinceEffect = [];
        internal int Initializations, Discoveries, SilentCalls, InteractiveCalls, Opened, Closed;
        public TimeProvider Clock { get; } = new FixedClock();

        internal Task<AuthenticationOutcome> RunAsync()
        {
            var provider = new LocalWindowsProvider(new WindowsHostAdmission(this), token =>
            {
                Initializations++;
                Tokens.Add(token);
                observationsSinceEffect.Clear();
                return this;
            });
            return new RequestCoordinator(provider, this).AuthenticateAsync(
                new(Email, [Scope], true, Tenant), Original.Token);
        }

        private void Observe(string name, CancellationToken cancellationToken)
        {
            Observed.Add(name);
            observationsSinceEffect.Add(name);
            Tokens.Add(cancellationToken);
            DuringObservation?.Invoke(name);
        }

        // Deliberately do not cooperate with cancellation: the production boundary
        // must notice it before classifying a result or beginning the next query.
        public WindowsHostPlatform ReadPlatform(CancellationToken token) { Observe("platform", token); return Platform; }
        public bool? ReadWorkstationProduct(CancellationToken token) { Observe("product", token); return Workstation; }
        public WindowsThreadIdentity ReadThreadIdentity(CancellationToken token) { Observe("thread", token); return ThreadIdentity; }
        public WindowsLocalLogon? ReadOwnLogonAndStation(CancellationToken token) { Observe("logon", token); return Logon; }
        public WindowsSessionConnection ReadSessionConnection(CancellationToken token) { Observe("session", token); return Session; }
        public bool? ReadInputDesktop(CancellationToken token) { Observe("desktop", token); return InputDesktop; }

        public Task<IReadOnlyList<ProviderAccount>> GetAccountsAsync(CancellationToken token)
        {
            BeginProviderEffect("discover");
            Discoveries++;
            Tokens.Add(token);
            return Task.FromResult<IReadOnlyList<ProviderAccount>>([new(Email, new object())]);
        }

        public Task<TokenCandidate> AcquireSilentAsync(AuthenticationRequest request, ProviderAccount account,
            Guid operationId, CancellationToken token)
        {
            BeginProviderEffect("silent");
            SilentCalls++;
            Tokens.Add(token);
            return RequiresInteraction
                ? Task.FromException<TokenCandidate>(new ProviderFailureException(AuthenticationFailure.InteractionRequired))
                : Task.FromResult(Candidate(request, operationId));
        }

        public Task<TokenCandidate> AcquireInteractiveAsync(AuthenticationRequest request, ProviderAccount? account,
            string? claims, nint parent, Guid operationId, CancellationToken token)
        {
            BeginProviderEffect("interactive");
            InteractiveCalls++;
            Tokens.Add(token);
            return Task.FromResult(Candidate(request, operationId));
        }

        public Task<nint> OpenInteractionAsync(CancellationToken token)
        {
            Opened++;
            Tokens.Add(token);
            return Task.FromResult<nint>(1); // Synthetic parent; never used by a native API.
        }

        public Task CloseInteractionAsync() { Closed++; return Task.CompletedTask; }
        public void Dispose() => Original.Dispose();
        private void BeginProviderEffect(string effect)
        {
            ProviderObservationSets.Add((effect, observationsSinceEffect.ToArray()));
            observationsSinceEffect.Clear();
        }
        private static TokenCandidate Candidate(AuthenticationRequest request, Guid operationId) =>
            new("synthetic-token", Email, Tenant, request.Scopes, "Bearer", Now.AddHours(1), operationId);
    }

    private sealed class FixedClock : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => Now;
    }
}
