using Authentication.Core;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Authentication.Scenarios;

// Controlled application scenarios, plus focused result-commitment rules. The harness
// timeout only detects a stuck test; it is not evidence of the Windows shutdown bound.
[TestClass]
public sealed class RequestLifetimeScenarios
{
    private static readonly TimeSpan Budget = TimeSpan.FromSeconds(120);
    // Failure-only harness bound: passing runs never wait for it. Shared CI runners can
    // delay thread-pool and timer callbacks by seconds under parallel scenarios.
    private static readonly TimeSpan HarnessLimit = TimeSpan.FromSeconds(30);

    [TestMethod]
    public async Task AlreadyCancelledRequestDoesNotDiscoverAccountsOrCreateUi()
    {
        using var caller = new CancellationTokenSource();
        caller.Cancel();
        var scene = new Scene();
        using var lifetime = new RequestLifetime(scene.Clock, scene.Clock.GetTimestamp(), Budget, caller.Token);
        var outcome = await scene.RunAsync(lifetime);

        AssertFailure(AuthenticationFailure.Cancelled, outcome);
        Assert.AreEqual(0, scene.Effects.Count);
    }

    [TestMethod]
    public async Task ExpiredEntryBudgetPreventsDiscoveryEvenWhenLifetimeIsCreatedLater()
    {
        var scene = new Scene();
        var entry = scene.Clock.GetTimestamp();
        scene.Clock.Advance(Budget);
        using var lifetime = new RequestLifetime(scene.Clock, entry, Budget);
        var outcome = await scene.RunAsync(lifetime);

        AssertFailure(AuthenticationFailure.Timeout, outcome);
        Assert.AreEqual(0, scene.Effects.Count);
    }

    [TestMethod]
    [DataRow("discover", false)]
    [DataRow("silent", false)]
    [DataRow("open", false)]
    [DataRow("interactive", false)]
    [DataRow("discover", true)]
    [DataRow("silent", true)]
    [DataRow("open", true)]
    [DataRow("interactive", true)]
    public async Task CancellationOrDeadlineEndsTheRequestWhileDependencyRemainsPending(string phase, bool deadline)
    {
        using var caller = new CancellationTokenSource();
        var scene = new Scene { PendingPhase = phase };
        using var lifetime = new RequestLifetime(scene.Clock, scene.Clock.GetTimestamp(), Budget, caller.Token);
        var run = scene.RunAsync(lifetime);
        try
        {
            await scene.Entered.Task.WaitAsync(HarnessLimit);
            Assert.IsFalse(run.IsCompleted);
            if (deadline) scene.Clock.Advance(Budget);
            else caller.Cancel();

            var outcome = await RequireOutcomeAsync(run);
            AssertFailure(deadline ? AuthenticationFailure.Timeout : AuthenticationFailure.Cancelled, outcome);
            Assert.IsTrue(scene.ProviderToken.IsCancellationRequested);
            Assert.IsFalse(scene.Body.IsCompleted, "A terminal outcome must not wait for an uncooperative dependency.");
            Assert.IsTrue(lifetime.TryCommit(out var committed));
            AssertFailure(deadline ? AuthenticationFailure.Timeout : AuthenticationFailure.Cancelled, committed!);
        }
        finally
        {
            await scene.ReleaseAndDrainAsync(run);
        }

        Assert.IsFalse(lifetime.TryCommit(out var duplicate));
        Assert.IsNull(duplicate);
        Assert.IsFalse(scene.Effects.SkipWhile(effect => effect != phase).Skip(1)
            .Any(effect => effect is "silent" or "open" or "interactive"));
    }

    [TestMethod]
    public async Task AdmissionAndProviderTimeShareTheOriginalDeadline()
    {
        var scene = new Scene { PendingPhase = "silent" };
        var entry = scene.Clock.GetTimestamp();
        scene.Clock.Advance(TimeSpan.FromSeconds(60));
        using var lifetime = new RequestLifetime(scene.Clock, entry, Budget);
        var run = scene.RunAsync(lifetime);
        try
        {
            await scene.Entered.Task.WaitAsync(HarnessLimit);
            scene.Clock.Advance(TimeSpan.FromSeconds(59));
            Assert.IsFalse(run.IsCompleted);
            scene.Clock.Advance(TimeSpan.FromSeconds(1));
            AssertFailure(AuthenticationFailure.Timeout, await RequireOutcomeAsync(run));
        }
        finally
        {
            await scene.ReleaseAndDrainAsync(run);
        }
    }

    [TestMethod]
    public async Task DelayedTimerCannotAcceptAResultValidatedAfterTheDeadline()
    {
        var scene = new Scene { PendingPhase = "silent" };
        using var lifetime = new RequestLifetime(scene.Clock, scene.Clock.GetTimestamp(), Budget);
        var run = scene.RunAsync(lifetime);
        try
        {
            await scene.Entered.Task.WaitAsync(HarnessLimit);
            scene.Clock.Advance(Budget, dispatchTimers: false);
            scene.Release.TrySetResult();
            AssertFailure(AuthenticationFailure.Timeout, await RequireOutcomeAsync(run));
        }
        finally
        {
            await scene.ReleaseAndDrainAsync(run);
        }
    }

    [TestMethod]
    [DataRow(-24)]
    [DataRow(24)]
    public async Task WallClockChangesDoNotConsumeOrRenewTheMonotonicBudget(int hours)
    {
        var scene = new Scene { PendingPhase = "silent" };
        using var lifetime = new RequestLifetime(scene.Clock, scene.Clock.GetTimestamp(), Budget);
        var run = scene.RunAsync(lifetime);
        try
        {
            await scene.Entered.Task.WaitAsync(HarnessLimit);
            scene.Clock.ShiftUtc(TimeSpan.FromHours(hours));
            scene.Clock.Advance(TimeSpan.FromSeconds(119));
            Assert.IsFalse(run.IsCompleted);
            scene.Release.TrySetResult();
            Assert.IsNotNull((await RequireOutcomeAsync(run)).Success);
        }
        finally
        {
            await scene.ReleaseAndDrainAsync(run);
        }
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task LateSuccessOrFailureCannotReplaceCancellation(bool lateFailure)
    {
        using var caller = new CancellationTokenSource();
        var scene = new Scene();
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var pending = new TaskCompletionSource<AuthenticationOutcome>(TaskCreationOptions.RunContinuationsAsynchronously);
        var lateOutcome = lateFailure ? new AuthenticationOutcome(null, AuthenticationFailure.Denied) : scene.Success();
        using var lifetime = new RequestLifetime(scene.Clock, scene.Clock.GetTimestamp(), Budget, caller.Token);
        var run = lifetime.RunAsync(_ =>
        {
            entered.TrySetResult();
            return pending.Task;
        });
        try
        {
            await entered.Task.WaitAsync(HarnessLimit);
            caller.Cancel();
            AssertFailure(AuthenticationFailure.Cancelled, await RequireOutcomeAsync(run));
        }
        finally
        {
            pending.TrySetResult(lateOutcome);
            await lifetime.OperationCompletion.WaitAsync(HarnessLimit);
            await run.WaitAsync(HarnessLimit);
        }

        Assert.IsTrue(lifetime.TryCommit(out var committed));
        AssertFailure(AuthenticationFailure.Cancelled, committed!);
        Assert.IsFalse(committed!.PersistenceUnconfirmed);
    }

    [TestMethod]
    public async Task CallerCancellationBeforeCommitSuppressesValidatedSuccessAndWarning()
    {
        using var caller = new CancellationTokenSource();
        var scene = new Scene();
        using var lifetime = new RequestLifetime(scene.Clock, scene.Clock.GetTimestamp(), Budget, caller.Token);
        Assert.IsNotNull((await scene.RunAsync(lifetime)).Success);
        caller.Cancel();

        Assert.IsTrue(lifetime.TryCommit(out var committed));
        AssertFailure(AuthenticationFailure.Cancelled, committed!);
        Assert.IsFalse(committed!.PersistenceUnconfirmed);
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task OnlyOneResultCanCommitAndLaterEventsCannotRewriteIt(bool failure)
    {
        using var caller = new CancellationTokenSource();
        var scene = new Scene();
        using var lifetime = new RequestLifetime(scene.Clock, scene.Clock.GetTimestamp(), Budget, caller.Token);
        Assert.IsFalse(lifetime.TryCommit(out var premature));
        Assert.IsNull(premature);
        var observed = await lifetime.RunAsync(_ => Task.FromResult(failure
            ? new AuthenticationOutcome(null, AuthenticationFailure.Denied) : scene.Success()));

        Assert.IsTrue(lifetime.TryCommit(out var committed));
        Assert.AreSame(observed, committed);
        caller.Cancel();
        scene.Clock.Advance(Budget);
        Assert.IsFalse(lifetime.TryCommit(out var duplicate));
        Assert.IsNull(duplicate);
        Assert.AreSame(observed, committed);
    }

    [TestMethod]
    public async Task CallerCancellationDoesNotReplaceAnAlreadyLatchedFailure()
    {
        using var caller = new CancellationTokenSource();
        var clock = new ScenarioClock();
        using var lifetime = new RequestLifetime(clock, clock.GetTimestamp(), Budget, caller.Token);
        await lifetime.RunAsync(_ => Task.FromResult(new AuthenticationOutcome(null, AuthenticationFailure.Denied)));
        caller.Cancel();

        Assert.IsTrue(lifetime.TryCommit(out var committed));
        AssertFailure(AuthenticationFailure.Denied, committed!);
    }

    [TestMethod]
    [DataRow("success")]
    [DataRow("denied")]
    [DataRow("invalid")]
    public async Task EveryTerminalOutcomeNotifiesTheRequestToken(string kind)
    {
        var scene = new Scene();
        using var lifetime = new RequestLifetime(scene.Clock, scene.Clock.GetTimestamp(), Budget);
        var supplied = CancellationToken.None;
        var expected = kind switch
        {
            "denied" => new(null, AuthenticationFailure.Denied),
            "invalid" => new(null, AuthenticationFailure.InvalidRequest),
            _ => scene.Success(),
        };
        var actual = await lifetime.RunAsync(token =>
        {
            supplied = token;
            return Task.FromResult(expected);
        });

        Assert.AreSame(expected, actual);
        Assert.IsTrue(supplied.IsCancellationRequested);
    }

    [TestMethod]
    public async Task UnexpectedAdmissionFailureBecomesSafeInternalFailure()
    {
        var clock = new ScenarioClock();
        using var lifetime = new RequestLifetime(clock, clock.GetTimestamp(), Budget);
        AuthenticationOutcome outcome;
        try
        {
            outcome = await lifetime.RunAsync(_ => throw new InvalidOperationException("synthetic-private-marker"));
        }
        catch (Exception)
        {
            Assert.Fail("Unexpected admission exceptions must become a safe terminal outcome.");
            throw;
        }

        AssertFailure(AuthenticationFailure.InternalFailure, outcome);
        Assert.IsFalse(outcome.ToString().Contains("synthetic-private-marker", StringComparison.Ordinal));
    }

    [TestMethod]
    public async Task HostFaultBeforeStartPreventsDiscoveryAndUi()
    {
        var scene = new Scene();
        using var lifetime = new RequestLifetime(scene.Clock, scene.Clock.GetTimestamp(), Budget);
        lifetime.FailHost();
        var outcome = await scene.RunAsync(lifetime);

        AssertFailure(AuthenticationFailure.InternalFailure, outcome);
        Assert.AreEqual(0, scene.Effects.Count);
        await lifetime.CompleteAsync().WaitAsync(HarnessLimit);
    }

    [TestMethod]
    public async Task HostFaultEndsPendingRequestAndSuppressesLaterEffects()
    {
        foreach (var phase in new[] { "discover", "silent", "open", "interactive" })
        {
            var scene = new Scene { PendingPhase = phase };
            using var lifetime = new RequestLifetime(scene.Clock, scene.Clock.GetTimestamp(), Budget);
            var run = scene.RunAsync(lifetime);
            try
            {
                await scene.Entered.Task.WaitAsync(HarnessLimit);
                lifetime.FailHost();
                Assert.IsTrue(run.IsCompleted,
                    "Host failure must select a terminal result before the pending dependency returns.");
                AssertFailure(AuthenticationFailure.InternalFailure, await run);
                Assert.IsTrue(scene.ProviderToken.IsCancellationRequested);
                Assert.IsFalse(scene.Body.IsCompleted);
                Assert.IsTrue(lifetime.TryCommit(out var committed));
                AssertFailure(AuthenticationFailure.InternalFailure, committed!);
            }
            finally
            {
                await scene.ReleaseAndDrainAsync(run);
                await lifetime.CompleteAsync().WaitAsync(HarnessLimit);
            }

            Assert.IsFalse(scene.Effects.SkipWhile(effect => effect != phase).Skip(1)
                .Any(effect => effect is "silent" or "open" or "interactive"));
            Assert.IsFalse(lifetime.TryCommit(out _));
        }
    }

    [TestMethod]
    public async Task HostFaultBeforeCommitWithholdsValidatedTokenAndWarning()
    {
        var scene = new Scene();
        using var lifetime = new RequestLifetime(scene.Clock, scene.Clock.GetTimestamp(), Budget);
        Assert.IsNotNull((await scene.RunAsync(lifetime)).Success);
        lifetime.FailHost();

        Assert.IsTrue(lifetime.TryCommit(out var committed));
        AssertFailure(AuthenticationFailure.InternalFailure, committed!);
        Assert.IsFalse(committed!.PersistenceUnconfirmed);
        var serialized = ResultProjection.Serialize(committed);
        Assert.AreEqual(1, serialized.ExitCode);
        Assert.IsFalse(System.Text.Encoding.UTF8.GetString(serialized.Utf8Json)
            .Contains("synthetic-token", StringComparison.Ordinal));
    }

    [TestMethod]
    public async Task HostFaultPreservesPreviouslySelectedFailures()
    {
        foreach (var failure in new[]
            { AuthenticationFailure.Denied, AuthenticationFailure.Cancelled, AuthenticationFailure.Timeout })
        {
            var scene = new Scene();
            using var lifetime = new RequestLifetime(scene.Clock, scene.Clock.GetTimestamp(), Budget);
            await lifetime.RunAsync(_ => Task.FromResult(new AuthenticationOutcome(null, failure)));
            lifetime.FailHost();

            Assert.IsTrue(lifetime.TryCommit(out var committed));
            AssertFailure(failure, committed!);
        }
    }

    [TestMethod]
    public async Task CancelledCallerRetainsPrecedenceWhenHostFaultArrives()
    {
        using var caller = new CancellationTokenSource();
        var scene = new Scene { PendingPhase = "silent" };
        using var lifetime = new RequestLifetime(scene.Clock, scene.Clock.GetTimestamp(), Budget, caller.Token);
        var run = scene.RunAsync(lifetime);
        try
        {
            await scene.Entered.Task.WaitAsync(HarnessLimit);
            await caller.CancelAsync();
            lifetime.FailHost();
            AssertFailure(AuthenticationFailure.Cancelled, await run);
            Assert.IsTrue(lifetime.TryCommit(out var committed));
            AssertFailure(AuthenticationFailure.Cancelled, committed!);
        }
        finally
        {
            await scene.ReleaseAndDrainAsync(run);
        }
    }

    [TestMethod]
    public async Task HostFaultObservesExpiredDeadlineBeforeDelayedTimerDispatch()
    {
        var scene = new Scene { PendingPhase = "silent" };
        using var lifetime = new RequestLifetime(scene.Clock, scene.Clock.GetTimestamp(), Budget);
        var run = scene.RunAsync(lifetime);
        try
        {
            await scene.Entered.Task.WaitAsync(HarnessLimit);
            scene.Clock.Advance(Budget, dispatchTimers: false);
            lifetime.FailHost();
            Assert.IsTrue(run.IsCompleted,
                "Host failure must check the original deadline without waiting for timer dispatch.");
            AssertFailure(AuthenticationFailure.Timeout, await run);
            Assert.IsTrue(lifetime.TryCommit(out var committed));
            AssertFailure(AuthenticationFailure.Timeout, committed!);
        }
        finally
        {
            await scene.ReleaseAndDrainAsync(run);
        }
    }

    [TestMethod]
    public async Task HostFaultAfterCommitCannotReplaceOrDuplicateTheResult()
    {
        var scene = new Scene();
        using var lifetime = new RequestLifetime(scene.Clock, scene.Clock.GetTimestamp(), Budget);
        var outcome = await scene.RunAsync(lifetime);
        Assert.IsTrue(lifetime.TryCommit(out var committed));
        Assert.AreSame(outcome, committed);
        lifetime.FailHost();

        Assert.IsNotNull(committed!.Success);
        Assert.IsFalse(lifetime.TryCommit(out var duplicate));
        Assert.IsNull(duplicate);
    }

    [TestMethod]
    [DataRow(false, false)]
    [DataRow(true, false)]
    [DataRow(false, true)]
    [DataRow(true, true)]
    public async Task TerminalTaskIsReturnedWhileSynchronousDependencyAndCallbacksRemainPending(
        bool deadline, bool pendingCallback)
    {
        using var caller = new CancellationTokenSource();
        using var releaseDependency = new ManualResetEventSlim();
        using var releaseCallback = new ManualResetEventSlim();
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var callbackEntered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var returned = new TaskCompletionSource<Task<AuthenticationOutcome>>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var callerFinished = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var scene = new Scene();
        using var lifetime = new RequestLifetime(scene.Clock, scene.Clock.GetTimestamp(), Budget, caller.Token);
        CancellationToken observedToken = default;
        var thread = new Thread(() =>
        {
            try
            {
                var run = lifetime.RunAsync(token =>
                {
                    observedToken = token;
                    using var registration = pendingCallback
                        ? token.Register(() =>
                        { callbackEntered.TrySetResult(); releaseCallback.Wait(); }) : default;
                    entered.TrySetResult();
                    releaseDependency.Wait();
                    return Task.FromResult(scene.Success());
                });
                returned.TrySetResult(run);
            }
            catch (Exception exception) { returned.TrySetException(exception); }
            finally { callerFinished.TrySetResult(); }
        }) { IsBackground = true };
        var failures = new List<Exception>();
        thread.Start();
        try
        {
            await entered.Task.WaitAsync(HarnessLimit);
            if (deadline) scene.Clock.Advance(Budget);
            else caller.Cancel();
            if (pendingCallback) await callbackEntered.Task.WaitAsync(HarnessLimit);
            var run = await returned.Task.WaitAsync(HarnessLimit);
            var expected = deadline ? AuthenticationFailure.Timeout : AuthenticationFailure.Cancelled;
            AssertFailure(expected, await RequireOutcomeAsync(run));
            Assert.IsTrue(observedToken.IsCancellationRequested);
            Assert.IsFalse(lifetime.OperationCompletion.IsCompleted);
            Assert.IsFalse(lifetime.CompleteAsync().IsCompleted,
                "A terminal task is available before actual dependency/callback drain.");
            Assert.IsTrue(lifetime.TryCommit(out var committed));
            AssertFailure(expected, committed!);
        }
        catch (Exception exception) { failures.Add(exception); }
        finally
        {
            releaseDependency.Set();
            releaseCallback.Set();
            try
            {
                try { await callerFinished.Task.WaitAsync(HarnessLimit); }
                catch (Exception exception) { failures.Add(exception); }
            }
            finally
            {
                try
                {
                    try { Assert.IsTrue(thread.Join(HarnessLimit), "The owned caller thread must actually exit."); }
                    catch (Exception exception) { failures.Add(exception); }
                }
                finally
                {
                    try { await lifetime.CompleteAsync().WaitAsync(HarnessLimit); }
                    catch (Exception exception) { failures.Add(exception); }
                }
            }
        }
        if (failures.Count == 1)
            System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(failures[0]).Throw();
        if (failures.Count > 1) throw new AggregateException(failures);
        Assert.IsFalse(lifetime.TryCommit(out _));
    }

    private static void AssertFailure(AuthenticationFailure expected, AuthenticationOutcome outcome)
    {
        Assert.AreEqual(expected, outcome.Failure);
        Assert.IsNull(outcome.Success);
    }

    private static async Task<AuthenticationOutcome> RequireOutcomeAsync(Task<AuthenticationOutcome> run)
    {
        try
        {
            return await run.WaitAsync(HarnessLimit);
        }
        catch (TimeoutException)
        {
            Assert.Fail("The request did not select its terminal outcome while the controlled dependency remained pending.");
            throw;
        }
    }

    private sealed class Scene : IAuthenticationProvider, IRequestHost
    {
        private const string Email = "personal@example.test";
        private const string Scope = "499b84ac-1321-427f-aa17-267ca6975798/user_impersonation";
        private static readonly Guid Tenant = new("11111111-2222-3333-4444-555555555555");
        public string? PendingPhase { get; init; }
        public ScenarioClock Clock { get; } = new();
        TimeProvider IRequestHost.Clock => Clock;
        public List<string> Effects { get; } = [];
        public TaskCompletionSource Entered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly TaskCompletionSource bodyCompleted = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public Task Body => bodyCompleted.Task;
        public CancellationToken ProviderToken { get; private set; }

        private RequestLifetime? activeLifetime;
        public Task<AuthenticationOutcome> RunAsync(RequestLifetime lifetime)
        {
            activeLifetime = lifetime;
            return lifetime.RunAsync(async token =>
            {
                try
                {
                    return await new RequestCoordinator(this, this)
                        .AuthenticateAsync(new(Email, [Scope], true, Tenant), token);
                }
                finally { bodyCompleted.TrySetResult(); }
            });
        }

        public async Task ReleaseAndDrainAsync(Task<AuthenticationOutcome> run)
        {
            Release.TrySetResult();
            await run.WaitAsync(HarnessLimit);
            await activeLifetime!.OperationCompletion.WaitAsync(HarnessLimit);
        }

        public AuthenticationOutcome Success() => new(Candidate(Guid.NewGuid()), null, PersistenceUnconfirmed: true);

        public async Task<IReadOnlyList<ProviderAccount>> GetAccountsAsync(CancellationToken cancellationToken)
        {
            await PauseAsync("discover", cancellationToken);
            return PendingPhase is "open" or "interactive" ? [] : [new(Email, new object())];
        }

        public async Task<TokenCandidate> AcquireSilentAsync(AuthenticationRequest request, ProviderAccount account,
            Guid operationId, CancellationToken cancellationToken)
        {
            await PauseAsync("silent", cancellationToken);
            return Candidate(operationId);
        }

        public async Task<nint> OpenInteractionAsync(CancellationToken cancellationToken)
        {
            await PauseAsync("open", cancellationToken);
            return 42;
        }

        public async Task<TokenCandidate> AcquireInteractiveAsync(AuthenticationRequest request, ProviderAccount? account,
            string? claims, nint parentWindow, Guid operationId, CancellationToken cancellationToken)
        {
            await PauseAsync("interactive", cancellationToken);
            return Candidate(operationId);
        }

        public Task CloseInteractionAsync()
        {
            Effects.Add("close");
            return Task.CompletedTask;
        }

        private async Task PauseAsync(string phase, CancellationToken token)
        {
            Effects.Add(phase);
            ProviderToken = token;
            if (phase != PendingPhase) return;
            Entered.TrySetResult();
            await Release.Task;
        }

        private TokenCandidate Candidate(Guid operationId) => new("synthetic-token", Email, Tenant, [Scope],
            "Bearer", Clock.GetUtcNow().AddHours(1), operationId);
    }

    private sealed class ScenarioClock : TimeProvider
    {
        private readonly object gate = new();
        private readonly List<ManualTimer> timers = [];
        private long timestamp;
        private DateTimeOffset utc = new(2026, 9, 13, 0, 0, 0, TimeSpan.Zero);
        public override long TimestampFrequency => TimeSpan.TicksPerSecond;
        public override long GetTimestamp() { lock (gate) return timestamp; }
        public override DateTimeOffset GetUtcNow() { lock (gate) return utc; }
        public void ShiftUtc(TimeSpan delta) { lock (gate) utc += delta; }

        public void Advance(TimeSpan delta, bool dispatchTimers = true)
        {
            List<ManualTimer> due;
            lock (gate)
            {
                timestamp += delta.Ticks;
                utc += delta;
                due = dispatchTimers ? timers.Where(timer => timer.TakeDue(timestamp)).ToList() : [];
            }

            foreach (var timer in due) timer.Fire();
        }

        public override ITimer CreateTimer(TimerCallback callback, object? state, TimeSpan dueTime, TimeSpan period)
        {
            lock (gate)
            {
                var timer = new ManualTimer(this, callback, state);
                timers.Add(timer);
                timer.Change(dueTime, period);
                return timer;
            }
        }

        private sealed class ManualTimer(ScenarioClock clock, TimerCallback callback, object? state) : ITimer
        {
            private long? due;
            private bool disposed;
            public bool Change(TimeSpan dueTime, TimeSpan period)
            {
                if (period != global::System.Threading.Timeout.InfiniteTimeSpan) throw new NotSupportedException("One-shot fixture timers only.");
                lock (clock.gate)
                {
                    if (disposed) return false;
                    due = dueTime == global::System.Threading.Timeout.InfiniteTimeSpan ? null : clock.timestamp + dueTime.Ticks;
                    return true;
                }
            }

            public bool TakeDue(long now)
            {
                if (disposed || due is null || due > now) return false;
                due = null;
                return true;
            }

            public void Fire() => callback(state);
            public void Dispose() { lock (clock.gate) { disposed = true; due = null; } }
            public ValueTask DisposeAsync() { Dispose(); return ValueTask.CompletedTask; }
        }
    }
}
