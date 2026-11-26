using System.Text;
using Authentication.Core;
using Authentication.Windows;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Authentication.Windows.Scenarios;

// Development validation (docs/research/experiment-safety.md): hidden windows owned by
// the test process. No test loads WAM, enumerates accounts, or starts a child process.
[TestClass]
[DoNotParallelize]
public sealed class OwnedHostScenarios
{
    private static readonly TimeSpan FixtureLimit = TimeSpan.FromSeconds(5);
    private const string Email = "personal@example.test";
    private const string Scope = "499b84ac-1321-427f-aa17-267ca6975798/user_impersonation";
    private const string ProfileJson = """
        {"schemaVersion":1,"name":"synthetic-owned-host-profile",
         "clientId":"aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee","cloud":"public",
         "tenantPolicy":{"kind":"multitenant"},"integration":"windows-wam",
         "registration":{"owner":"Synthetic external owner","externalDependency":true,
         "supportNotice":"Synthetic configuration; no support commitment."}}
        """;

    [TestMethod]
    public async Task SilentSuccessDoesNotCreateOwnedUi()
    {
        await using var fixture = new Fixture();
        fixture.Provider.VisibleAccount = true;
        var result = await fixture.InvokeAsync();
        Assert.IsNotNull(result.Success);
        Assert.IsFalse(result.Interactive);
        Assert.IsNull(fixture.UiThread);
        Assert.AreEqual(0, fixture.Provider.InteractiveCalls);
        Assert.AreEqual(0, fixture.CancelCalls);
        Assert.IsFalse(fixture.Fault.Task.IsCompleted);
        Assert.IsTrue(fixture.Notices.IsEmpty);
    }

    [TestMethod]
    public async Task ForbiddenInteractionDoesNotCreateOwnedUi()
    {
        await using var fixture = new Fixture();
        var result = await fixture.InvokeAsync(interactionAllowed: false);
        Assert.AreEqual(AuthenticationFailure.InteractionRequired, result.Failure);
        Assert.IsNull(fixture.UiThread);
        Assert.AreEqual(0, fixture.Provider.InteractiveCalls);
        Assert.AreEqual(0, fixture.CancelCalls);
    }

    [TestMethod]
    public async Task MissingPresentationPreventsInteraction()
    {
        await using var fixture = new Fixture();
        var result = await fixture.InvokeAsync(bindProfile: false);
        Assert.AreEqual(AuthenticationFailure.MechanismUnavailable, result.Failure);
        Assert.IsNull(fixture.UiThread);
        Assert.AreEqual(0, fixture.Provider.InteractiveCalls);
    }

    [TestMethod]
    public async Task ReadyParentIsHiddenChildlessAndNoticeCarriesAdmittedMetadata()
    {
        await using var fixture = new Fixture();
        fixture.Provider.Interactive = (request, parent, operation, token) =>
        {
            Assert.IsFalse(token.IsCancellationRequested);
            Assert.AreNotEqual((nint)0, parent);
            Assert.IsFalse(OwnedWindowObservation.IsVisible(parent)); // WAM owns the visible display.
            Assert.AreEqual(ApartmentState.STA, fixture.UiApartment);
            Assert.IsNotNull(fixture.UiThread);
            Assert.IsTrue(fixture.UiThread.IsBackground);
            // The hidden owner has no label, button or other owned control.
            Assert.IsEmpty(OwnedWindowObservation.ReadControls(parent));
            Assert.AreEqual("AzureAuth Unofficial V2 (hcoona/microsoft-authentication-cli)",
                OwnedWindowObservation.ReadText(parent));
            var notice = Encoding.UTF8.GetString(fixture.Notices.Single());
            Assert.AreEqual("Unofficial hcoona/microsoft-authentication-cli. Profile: synthetic-owned-host-profile. "
                + "Registration owner: Synthetic external owner. Sign-in continues in the provider display.\n", notice);
            return Task.FromResult(Candidate(request, operation));
        };
        fixture.AfterBinding = () => fixture.Profiles.Bytes = Encoding.UTF8.GetBytes(
            ProfileJson.Replace("synthetic-owned-host-profile", "replacement-owned-host-profile"));
        var result = await fixture.InvokeAsync();
        Assert.IsNotNull(result.Success);
        Assert.IsTrue(result.Interactive);
        Assert.AreEqual(1, fixture.Provider.InteractiveCalls);
        Assert.AreEqual(1, fixture.Profiles.Reads);
        await fixture.Host.Completion.WaitAsync(FixtureLimit);
        Assert.IsNotNull(fixture.UiThread);
        Assert.IsFalse(fixture.UiThread.IsAlive);
        Assert.AreEqual(0, fixture.CancelCalls);
        Assert.IsGreaterThan(0, fixture.WindowObservations, "The hidden owner was never observed.");
    }

    [TestMethod]
    public async Task CreationFailurePreventsInteractiveAcquisition()
    {
        await using var fixture = new Fixture();
        var reached = false;
        fixture.Checkpoint = (stage, window) =>
        {
            if (stage != OwnedHostCheckpoint.HiddenParentCreated) return;
            OwnedWindowObservation.RequireOwned(window);
            reached = true;
            throw new InvalidOperationException("Synthetic creation checkpoint failure.");
        };
        var result = await fixture.InvokeAsync();
        Assert.IsTrue(reached, "The actual creation checkpoint was not reached.");
        Assert.AreEqual(AuthenticationFailure.MechanismUnavailable, result.Failure);
        Assert.AreEqual(0, fixture.Provider.InteractiveCalls);
        Assert.AreEqual(0, fixture.CancelCalls);
        await fixture.Host.Completion.WaitAsync(FixtureLimit);
    }

    [TestMethod]
    public async Task OriginalCancellationBeforeCreationWins()
    {
        await using var fixture = new Fixture();
        await fixture.CancelCallerAsync();
        var result = await fixture.InvokeAsync();
        Assert.AreEqual(AuthenticationFailure.Cancelled, result.Failure);
        Assert.IsNull(fixture.UiThread);
        Assert.AreEqual(0, fixture.Provider.InteractiveCalls);
        Assert.AreEqual(0, fixture.Profiles.Reads);
    }

    [TestMethod]
    public async Task CancellationDuringCreationRejectsLateParent()
    {
        await using var fixture = new Fixture();
        var gate = fixture.PauseAt(OwnedHostCheckpoint.HiddenParentCreated);
        var invocation = fixture.InvokeAsync();
        await RequireCheckpointAsync(gate, invocation);
        Assert.IsFalse(OwnedWindowObservation.IsVisible(gate.Window));
        var opening = await fixture.Opening.Task.WaitAsync(FixtureLimit);
        await fixture.CancelCallerAsync();
        gate.Release();
        Assert.AreEqual((nint)0, await opening.WaitAsync(FixtureLimit),
            "Cancellation must invalidate the host's pending readiness itself.");
        var result = await invocation;
        Assert.AreEqual(AuthenticationFailure.Cancelled, result.Failure);
        Assert.AreEqual(0, fixture.Provider.InteractiveCalls);
        await fixture.Host.Completion.WaitAsync(FixtureLimit);
        Assert.IsFalse(OwnedWindowObservation.Exists(gate.Window));
        Assert.AreEqual((nint)0, await fixture.Host.OpenInteractionAsync(CancellationToken.None));
        // These observations cannot exclude a transient display. Source review
        // must verify terminal invalidation at every show/publication boundary.
    }

    [TestMethod]
    public async Task CloseDuringCreationCannotReopenHost()
    {
        await using var fixture = new Fixture();
        fixture.Bind();
        var gate = fixture.PauseAt(OwnedHostCheckpoint.HiddenParentCreated);
        var opening = fixture.Host.OpenInteractionAsync(fixture.Caller.Token);
        await RequireCheckpointAsync(gate, opening);
        Assert.IsFalse(OwnedWindowObservation.IsVisible(gate.Window));
        await fixture.Host.CloseInteractionAsync().WaitAsync(FixtureLimit);
        Assert.IsFalse(fixture.Caller.IsCancellationRequested);
        gate.Release();
        Assert.AreEqual((nint)0, await opening.WaitAsync(FixtureLimit));
        await fixture.Host.Completion.WaitAsync(FixtureLimit);
        Assert.IsFalse(OwnedWindowObservation.Exists(gate.Window));
        Assert.AreEqual((nint)0, await fixture.Host.OpenInteractionAsync(CancellationToken.None));
        Assert.AreEqual(0, fixture.CancelCalls);
    }

    [TestMethod]
    public async Task ClosedHostCannotReopen()
    {
        await using var fixture = new Fixture();
        fixture.Bind();
        await fixture.Host.CloseInteractionAsync();
        Assert.AreEqual((nint)0, await fixture.Host.OpenInteractionAsync(CancellationToken.None));
        await fixture.Host.Completion.WaitAsync(FixtureLimit);
        Assert.IsNull(fixture.UiThread);
        Assert.AreEqual(0, fixture.CancelCalls);
    }

    [TestMethod]
    public async Task InternalCloseDoesNotCancelCaller()
    {
        await using var fixture = new Fixture();
        var parent = await fixture.OpenAsync();
        await fixture.Host.CloseInteractionAsync().WaitAsync(FixtureLimit);
        await fixture.Host.CloseInteractionAsync().WaitAsync(FixtureLimit);
        await fixture.Host.Completion.WaitAsync(FixtureLimit);
        Assert.IsFalse(OwnedWindowObservation.Exists(parent));
        Assert.IsFalse(fixture.Caller.IsCancellationRequested);
        Assert.AreEqual(0, fixture.CancelCalls);
        Assert.AreEqual((nint)0, await fixture.Host.OpenInteractionAsync(CancellationToken.None));
    }

    [TestMethod]
    public async Task CompletionWaitsForActualUiThreadExit()
    {
        await using var fixture = new Fixture();
        var gate = fixture.PauseAt(OwnedHostCheckpoint.NativeCleanupCompleted);
        var parent = await fixture.OpenAsync();
        await fixture.Host.CloseInteractionAsync().WaitAsync(FixtureLimit);
        await RequireCheckpointAsync(gate, fixture.Host.Completion);
        Assert.IsFalse(OwnedWindowObservation.Exists(parent));
        Assert.IsNotNull(fixture.UiThread);
        Assert.IsTrue(fixture.UiThread.IsAlive);
        Assert.IsFalse(fixture.Host.Completion.IsCompleted,
            "Completion must wait for the creating thread to return after native cleanup.");
        gate.Release();
        await fixture.Host.Completion.WaitAsync(FixtureLimit);
        Assert.IsFalse(fixture.UiThread.IsAlive);
    }

    [TestMethod]
    public Task UserOrSystemCloseStopsPendingAuthentication() => ClosePendingAsync();

    [TestMethod]
    public async Task PostReadinessCallbackFaultIsContained()
    {
        await using var fixture = new Fixture();
        var parent = await fixture.OpenAsync();
        var inject = 1;
        var reached = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        fixture.Checkpoint = (stage, _) =>
        {
            if (stage != OwnedHostCheckpoint.MessageDispatch || Interlocked.Exchange(ref inject, 0) == 0) return;
            reached.TrySetResult();
            throw new InvalidOperationException("Synthetic owned callback failure.");
        };
        OwnedWindowObservation.Post(parent, 0); // WM_NULL, only this owned parent.
        await reached.Task.WaitAsync(FixtureLimit);
        await fixture.Fault.Task.WaitAsync(FixtureLimit);
        await fixture.Host.Completion.WaitAsync(FixtureLimit);
        Assert.IsFalse(OwnedWindowObservation.Exists(parent));
        Assert.AreEqual(0, fixture.CancelCalls);
        Assert.IsFalse(fixture.Caller.IsCancellationRequested);
        Assert.AreEqual((nint)0, await fixture.Host.OpenInteractionAsync(CancellationToken.None));
        // Process-level rejection of a subsequent success is a separate composition
        // obligation; this local case proves callback containment and notification.
    }

    [TestMethod]
    public async Task UiRejectionBeforeCreationPreventsParentAndAcquisition()
    {
        var admission = new UiAdmission
        {
            RecheckAction = _ => throw new ProviderFailureException(AuthenticationFailure.MechanismUnavailable),
        };
        await using var fixture = new Fixture(admission);
        var created = false;
        var nativeCreationObserved = false;
        fixture.Checkpoint = (stage, window) =>
        {
            created |= stage == OwnedHostCheckpoint.HiddenParentCreated;
            nativeCreationObserved |= stage == OwnedHostCheckpoint.MessageDispatch && window != 0;
        };

        var result = await fixture.InvokeAsync();
        Assert.AreEqual(AuthenticationFailure.MechanismUnavailable, result.Failure);
        await fixture.CompleteAsync();
        Assert.IsFalse(created);
        Assert.IsFalse(nativeCreationObserved);
        Assert.AreEqual(0, fixture.Provider.InteractiveCalls);
        Assert.AreEqual(1, admission.Observations.Count);
        Assert.IsNotNull(fixture.UiThread);
        Assert.IsFalse(fixture.UiThread.IsAlive);
    }

    [TestMethod]
    public async Task UiRejectionBeforeShowingWithholdsParentAndAcquisition()
    {
        var admission = new UiAdmission();
        nint hidden = 0;
        bool? visibleAtCreation = null;
        bool? visibleAtRejection = null;
        admission.RecheckAction = _ =>
        {
            // Reject the first recheck that observes the created owner.
            if (hidden != 0)
            {
                visibleAtRejection = OwnedWindowObservation.IsVisible(hidden);
                throw new ProviderFailureException(AuthenticationFailure.MechanismUnavailable);
            }
        };
        await using var fixture = new Fixture(admission);
        fixture.Checkpoint = (stage, window) =>
        {
            if (stage != OwnedHostCheckpoint.HiddenParentCreated) return;
            hidden = window;
            visibleAtCreation = OwnedWindowObservation.IsVisible(window);
        };

        var result = await fixture.InvokeAsync();
        Assert.AreEqual(AuthenticationFailure.MechanismUnavailable, result.Failure);
        await fixture.CompleteAsync();
        Assert.AreNotEqual((nint)0, hidden);
        Assert.AreEqual(false, visibleAtCreation);
        Assert.AreEqual(false, visibleAtRejection);
        Assert.AreEqual(0, fixture.Provider.InteractiveCalls);
        Assert.IsTrue(fixture.Notices.IsEmpty);
        Assert.IsFalse(OwnedWindowObservation.Exists(hidden));
        Assert.AreEqual((nint)0, await fixture.Host.OpenInteractionAsync(CancellationToken.None));
        // The snapshots catch showing before rejection; source-placement review
        // must still exclude a transient show/hide between these observations.
    }

    [TestMethod]
    public async Task UiRechecksUseOriginalTokenOnTheOwnedStaThread()
    {
        var admission = new UiAdmission();
        await using var fixture = new Fixture(admission);
        CancellationToken original = default;
        var events = new System.Collections.Concurrent.ConcurrentQueue<string>();
        admission.RecheckAction = _ => events.Enqueue("recheck");
        fixture.Checkpoint = (stage, _) =>
        {
            if (stage == OwnedHostCheckpoint.HiddenParentCreated) events.Enqueue("created");
        };
        fixture.Provider.Interactive = (request, _, operation, token) =>
        {
            original = token;
            events.Enqueue("interactive");
            return Task.FromResult(Candidate(request, operation));
        };

        var result = await fixture.InvokeAsync();
        await fixture.CompleteAsync();
        Assert.IsNotNull(result.Success);
        Assert.IsTrue(original.CanBeCanceled);
        // Each owned-UI effect follows its own fresh recheck: one before the owner
        // is created, and one after creation before readiness enables interaction.
        var order = events.ToList();
        var created = order.IndexOf("created");
        var interactive = order.IndexOf("interactive");
        Assert.IsTrue(created > 0 && order.Take(created).Contains("recheck"),
            "Owner creation needs a fresh recheck first.");
        Assert.IsTrue(interactive > created && order.Skip(created).Take(interactive - created).Contains("recheck"),
            "Interaction needs a fresh recheck after owner creation.");
        foreach (var observation in admission.Observations)
        {
            Assert.AreSame(fixture.UiThread, observation.Thread);
            Assert.AreEqual(ApartmentState.STA, observation.Apartment);
            Assert.AreEqual(original, observation.Token);
        }
    }

    [TestMethod]
    [DataRow(1)]
    [DataRow(2)]
    public async Task CancellationDuringUiRecheckPreventsAcquisition(int observation)
    {
        var admission = new UiAdmission();
        await using var fixture = new Fixture(admission);
        admission.RecheckAction = _ =>
        {
            if (admission.Observations.Count == observation)
                fixture.CancelCallerAsync().WaitAsync(FixtureLimit).GetAwaiter().GetResult();
        };

        var result = await fixture.InvokeAsync();
        Assert.AreEqual(AuthenticationFailure.Cancelled, result.Failure);
        Assert.IsNull(result.Success);
        await fixture.CompleteAsync();
        Assert.AreEqual(0, fixture.Provider.InteractiveCalls);
        Assert.AreEqual(observation, admission.Observations.Count);
        Assert.AreEqual((nint)0, await fixture.Host.OpenInteractionAsync(CancellationToken.None));
    }

    [TestMethod]
    public async Task SilentSuccessDoesNotInspectTheOwnedUiThread()
    {
        var admission = new UiAdmission
        {
            RecheckAction = _ => throw new InvalidOperationException("Unexpected synthetic UI recheck."),
        };
        await using var fixture = new Fixture(admission);
        fixture.Provider.VisibleAccount = true;

        var result = await fixture.InvokeAsync();
        Assert.IsNotNull(result.Success);
        await fixture.CompleteAsync();
        Assert.AreEqual(0, admission.Observations.Count);
        Assert.IsNull(fixture.UiThread);
        Assert.AreEqual(0, fixture.Provider.InteractiveCalls);
    }

    private static async Task ClosePendingAsync()
    {
        await using var fixture = new Fixture();
        var entered = new TaskCompletionSource<nint>(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        fixture.Provider.Interactive = async (request, parent, operation, _) =>
        {
            entered.TrySetResult(parent);
            await release.Task.ConfigureAwait(false);
            return Candidate(request, operation); // A cooperative late candidate must lose.
        };
        try
        {
            var invocation = fixture.InvokeAsync();
            _ = await Task.WhenAny(entered.Task, invocation).WaitAsync(FixtureLimit);
            Assert.IsTrue(entered.Task.IsCompletedSuccessfully, "A ready parent did not reach provider interaction.");
            var parent = await entered.Task;
            Assert.IsEmpty(OwnedWindowObservation.ReadControls(parent));
            OwnedWindowObservation.Post(parent, 0x0010); // WM_CLOSE from the user or system.
            await fixture.Cancelled.Task.WaitAsync(FixtureLimit);
            Assert.IsTrue(fixture.Caller.IsCancellationRequested);
            release.TrySetResult();
            var result = await invocation;
            Assert.AreEqual(AuthenticationFailure.Cancelled, result.Failure);
            Assert.IsNull(result.Success);
            Assert.AreEqual(1, fixture.Provider.InteractiveCalls);
            await fixture.Host.Completion.WaitAsync(FixtureLimit);
            Assert.IsFalse(OwnedWindowObservation.Exists(parent));
        }
        finally
        {
            release.TrySetResult();
        }
    }

    private static async Task RequireCheckpointAsync(Pause gate, Task operation)
    {
        _ = await Task.WhenAny(gate.Entered.Task, operation).WaitAsync(FixtureLimit);
        Assert.IsTrue(gate.Entered.Task.IsCompletedSuccessfully, "The required owned-thread checkpoint was not reached.");
    }

    private static TokenCandidate Candidate(AuthenticationRequest request, Guid operation) => new(
        "SYNTHETIC_OWNED_HOST_TOKEN", request.AccountEmail, Guid.Parse("11111111-2222-3333-4444-555555555555"),
        request.Scopes, "Bearer", DateTimeOffset.UtcNow.AddHours(1), operation);

    private sealed class UiAdmission : IWindowsHostAdmission
    {
        internal readonly List<(Thread Thread, ApartmentState Apartment, CancellationToken Token)> Observations = [];
        internal Action<CancellationToken>? RecheckAction;

        public void Admit(CancellationToken cancellationToken) =>
            throw new InvalidOperationException("UI work must not repeat process admission.");

        public void Recheck(CancellationToken cancellationToken)
        {
            Observations.Add((Thread.CurrentThread, Thread.CurrentThread.GetApartmentState(), cancellationToken));
            RecheckAction?.Invoke(cancellationToken);
        }
    }

    private sealed class Fixture : IAsyncDisposable, IRequestHost
    {
        // Serial scheduling alone does not stop another case after unsafe teardown.
        // Retain the failed fixture and its resources until process termination.
        private static Fixture? stoppedFixture;
        internal readonly CancellationTokenSource Caller = new();
        internal readonly TaskCompletionSource Cancelled = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal readonly TaskCompletionSource Fault = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal readonly TaskCompletionSource<Task<nint>> Opening = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal readonly OwnedRequestHost Host;
        internal readonly SyntheticProvider Provider = new();
        internal readonly MemoryProfile Profiles = new();
        internal volatile Action<OwnedHostCheckpoint, nint>? Checkpoint;
        internal Action? AfterBinding;
        internal Thread? UiThread;
        internal ApartmentState UiApartment;
        internal int CancelCalls;
        // Every creation and dispatch checkpoint samples the owner on its creating
        // thread; any visible sample fails disposal (never-visible lifetime invariant).
        internal int WindowObservations;
        internal int VisibleObservations;
        internal readonly System.Collections.Concurrent.ConcurrentQueue<byte[]> Notices = new();
        private RequestInvocation? invocation;
        private Task<AuthenticationOutcome>? invocationObservation;
        private readonly object cancellationGate = new();
        private Task? callerCancellation;
        private readonly List<Pause> pauses = [];

        internal Fixture(IWindowsHostAdmission? admission = null)
        {
            if (Volatile.Read(ref stoppedFixture) is not null)
                throw new InvalidOperationException("The owned-host batch stopped after a fixture safety failure.");
            Host = new(async () =>
            {
                Interlocked.Increment(ref CancelCalls);
                var completion = CancelCallerAsync();
                Cancelled.TrySetResult();
                await completion.ConfigureAwait(false);
            }, () => { Fault.TrySetResult(); }, (stage, window) =>
            {
                if (stage == OwnedHostCheckpoint.ThreadStarted)
                {
                    UiThread = Thread.CurrentThread;
                    UiApartment = Thread.CurrentThread.GetApartmentState();
                }
                if (window != 0 && stage is OwnedHostCheckpoint.HiddenParentCreated or OwnedHostCheckpoint.MessageDispatch)
                {
                    Interlocked.Increment(ref WindowObservations);
                    if (OwnedWindowObservation.IsVisibleOnCreatingThread(window))
                        Interlocked.Increment(ref VisibleObservations);
                }
                Checkpoint?.Invoke(stage, window);
            }, admission, Notices.Enqueue);
        }

        TimeProvider IRequestHost.Clock => Host.Clock;

        Task<nint> IRequestHost.OpenInteractionAsync(CancellationToken cancellationToken)
        {
            var opening = Host.OpenInteractionAsync(cancellationToken);
            Opening.TrySetResult(opening);
            return opening;
        }

        Task IRequestHost.CloseInteractionAsync() => Host.CloseInteractionAsync();

        internal Task CancelCallerAsync()
        {
            // Keep the first cancellation task: another CancelAsync call does not
            // prove that the first call's outstanding callbacks have returned.
            lock (cancellationGate) return callerCancellation ??= Caller.CancelAsync();
        }

        internal void Bind() => Host.BindProfile(ProfileSyntax.Parse(Encoding.UTF8.GetBytes(ProfileJson))!);

        internal async Task<nint> OpenAsync()
        {
            Bind();
            var parent = await Host.OpenInteractionAsync(Caller.Token).WaitAsync(FixtureLimit);
            Assert.AreNotEqual((nint)0, parent, "The owned parent is not ready.");
            OwnedWindowObservation.RequireOwned(parent);
            return parent;
        }

        internal Pause PauseAt(OwnedHostCheckpoint selected)
        {
            var pause = new Pause();
            pauses.Add(pause);
            Checkpoint = (stage, window) =>
            {
                if (stage == selected) pause.Enter(window);
            };
            return pause;
        }

        internal Task<AuthenticationOutcome> InvokeAsync(bool interactionAllowed = true, bool bindProfile = true) =>
            invocationObservation = InvokeCoreAsync(interactionAllowed, bindProfile);

        internal async Task CompleteAsync()
        {
            if (invocation is not null) await invocation.CompleteAsync().WaitAsync(FixtureLimit);
            await Host.Completion.WaitAsync(FixtureLimit);
        }

        private async Task<AuthenticationOutcome> InvokeCoreAsync(bool interactionAllowed, bool bindProfile)
        {
            invocation = new([
                "authenticate", "--protocol", "1", "--profile", @"C:\synthetic-owned-host-profile.json",
                "--account-email", Email, "--scope", Scope, "--interaction",
                interactionAllowed ? "interactive-if-needed" : "non-interactive-only",
            ], this, Host.Clock.GetTimestamp(), Caller.Token);
            var result = await invocation.RunAsync(Profiles, profile =>
            {
                if (bindProfile) Host.BindProfile(profile);
                AfterBinding?.Invoke();
                return Provider;
            }).WaitAsync(FixtureLimit);
            Assert.IsTrue(invocation.TryCommitResult(out var serialized));
            Assert.IsNotNull(serialized);
            return result;
        }

        public async ValueTask DisposeAsync()
        {
            var failures = new List<Exception>();
            void Failed(Exception exception)
            {
                Interlocked.CompareExchange(ref stoppedFixture, this, null);
                failures.Add(exception);
            }

            async Task AttemptAsync(Func<Task> cleanup)
            {
                try { await cleanup().WaitAsync(FixtureLimit); }
                catch (Exception exception) { Failed(exception); }
            }

            foreach (var pause in pauses)
            {
                try { pause.Release(); }
                catch (Exception exception) { Failed(exception); }
            }
            await AttemptAsync(CancelCallerAsync);
            await AttemptAsync(Host.CloseInteractionAsync);
            try { invocation?.Dispose(); }
            catch (Exception exception) { Failed(exception); }
            if (invocation is not null) await AttemptAsync(invocation.CompleteAsync);
            if (invocationObservation is not null) await AttemptAsync(() => invocationObservation);
            await AttemptAsync(() => Host.Completion);
            if (UiThread?.IsAlive == true)
                Failed(new InvalidOperationException("The owned UI thread remains active."));
            if (Volatile.Read(ref VisibleObservations) != 0)
                Failed(new InvalidOperationException("The hidden owner window became visible."));
            if (pauses.Any(pause => pause.TimedOut))
                Failed(new InvalidOperationException("A finite owned UI checkpoint exceeded its bound."));

            if (failures.Count != 0)
            {
                // No disposal without proven quiescence. Even if writing the
                // marker fails, the retained fixture has already stopped the batch.
                File.WriteAllText(Path.Combine(Path.GetTempPath(), "owned-host-safety-stop.json"),
                    "{\"ownedHostFixtureFailed\":true}");
                throw new AggregateException("Owned-host fixture safety failed.", failures);
            }

            Caller.Dispose();
            foreach (var pause in pauses) pause.Dispose();
        }
    }

    private sealed class Pause : IDisposable
    {
        private readonly ManualResetEventSlim released = new();
        internal readonly TaskCompletionSource Entered = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal nint Window;
        internal volatile bool TimedOut;
        internal void Enter(nint window)
        {
            Window = window;
            Entered.TrySetResult();
            if (!released.Wait(FixtureLimit))
            {
                TimedOut = true;
                throw new InvalidOperationException("Synthetic owned checkpoint timed out.");
            }
        }
        internal void Release() => released.Set();
        public void Dispose() => released.Dispose();
    }

    private sealed class MemoryProfile : IProfileSource
    {
        internal byte[] Bytes = Encoding.UTF8.GetBytes(ProfileJson);
        internal int Reads;
        public Task<ReadOnlyMemory<byte>> ReadAsync(string path, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Reads++;
            return Task.FromResult<ReadOnlyMemory<byte>>(Bytes.ToArray());
        }
    }

    private sealed class SyntheticProvider : IAuthenticationProvider
    {
        internal bool VisibleAccount;
        internal int InteractiveCalls;
        internal Func<AuthenticationRequest, nint, Guid, CancellationToken, Task<TokenCandidate>>? Interactive;

        public Task<IReadOnlyList<ProviderAccount>> GetAccountsAsync(CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<ProviderAccount>>(VisibleAccount ? [new(Email, this)] : []);

        public Task<TokenCandidate> AcquireSilentAsync(AuthenticationRequest request, ProviderAccount account,
            Guid operationId, CancellationToken cancellationToken) => Task.FromResult(Candidate(request, operationId));

        public Task<TokenCandidate> AcquireInteractiveAsync(AuthenticationRequest request, ProviderAccount? account,
            string? claims, nint parentWindow, Guid operationId, CancellationToken cancellationToken)
        {
            InteractiveCalls++;
            return Interactive?.Invoke(request, parentWindow, operationId, cancellationToken)
                ?? Task.FromResult(Candidate(request, operationId));
        }
    }
}
