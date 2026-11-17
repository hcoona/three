using System.Runtime.ExceptionServices;
using System.Text.Json.Nodes;
using LiteAgensh.Tests.Support;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace LiteAgensh.Tests;

[TestClass]
public sealed class DeadlineRampSpecs(TestContext testContext)
{
    [TestMethod]
    [TestCategory("InterruptionAndPeerDrainSafety")]
    [DataRow("pause", "owner-pause")]
    [DataRow("finalize", "owner-finalize")]
    [Timeout(120_000, CooperativeCancellation = true)]
    public async Task LegacyStartupOwnerControlCancelsEnteredWaitBeforeDeadline(
        string action,
        string reason
    )
    {
        CancellationToken token = testContext.CancellationToken;
        Phase7Run state = await Phase7Run
            .CreateAsync(token, workers: 1, hostRole: null)
            .ConfigureAwait(false);
        CancellationTokenRegistration registration = default;
        Exception? primary = null;
        ExceptionDispatchInfo? teardownFailure = null;
        List<Exception> teardownErrors = [];
        void RetainTeardownFailure(string step, Exception secondary)
        {
            teardownErrors.Add(secondary);
            JsonObject details = Json.ExceptionDetails(secondary);
            details["cleanupStep"] = step;
            Console.Error.WriteLine("PHASE7_STARTUP_OWNER_TEARDOWN_SECONDARY " + details);
        }
        try
        {
            await state
                .RunBodyAsync(async () =>
                {
                    TaskCompletionSource<(
                        Phase7Sessions.Session Session,
                        CancellationToken Token
                    )> entered = new(TaskCreationOptions.RunContinuationsAsynchronously);
                    TaskCompletionSource<DateTimeOffset> cancelled = new(
                        TaskCreationOptions.RunContinuationsAsynchronously
                    );
                    TaskCompletionSource release = new(
                        TaskCreationOptions.RunContinuationsAsynchronously
                    );
                    state.Sessions.BeforeOpen = async (session, business) =>
                    {
                        entered.TrySetResult((session, business));
                        await release.Task.WaitAsync(business).ConfigureAwait(false);
                    };
                    _ = state.Start();
                    (Phase7Sessions.Session session, CancellationToken business) = await entered
                        .Task.WaitAsync(token)
                        .ConfigureAwait(false);
                    Assert.IsTrue(business.CanBeCanceled);
                    Assert.IsFalse(business.IsCancellationRequested);
                    Assert.IsFalse(session.HasSession);
                    Assert.AreEqual(0L, state.Dispatches(state.Reservation("worker1").Id));
                    CancellationTokenSource? observation = null;
                    Task? drive = null;
                    try
                    {
                        // This observer belongs to the whole scenario, not the Open hook.
                        // Keep it alive through the driver join and RunBody's owned cleanup.
                        registration = business.Register(() =>
                            cancelled.TrySetResult(state.Clock.GetUtcNow())
                        );
                        Assert.IsFalse(business.IsCancellationRequested);
                        JsonObject request = GitScenarioState.Data(
                            state.Coordinator().RequestControl(action)
                        );
                        long requestId = request["requestId"]!.GetValue<long>();
                        Assert.AreEqual("pending", request["state"]!.GetValue<string>());
                        TimeSpan observationWindow = state
                            .Sessions.Hosts.Single(item => item.Operation == "run")
                            .StartupOwnerObservationWindow();
                        observation = CancellationTokenSource.CreateLinkedTokenSource(token);
                        drive = DriveOwnerPollingAsync(state, cancelled.Task, observation.Token);
                        try
                        {
                            // This lifetime-budgeted watchdog observes only. Its expiry does not
                            // cancel
                            // the caller, business token, native operation or run.
                            await drive.WaitAsync(observationWindow, token).ConfigureAwait(false);
                        }
                        catch (TimeoutException)
                        {
                            Console.WriteLine(
                                "PHASE7_STARTUP_OWNER_RED "
                                    + new JsonObject
                                    {
                                        ["action"] = action,
                                        ["requestId"] = requestId,
                                        ["enteredBusinessTokenCancelled"] =
                                            business.IsCancellationRequested,
                                        ["callerCancelled"] = token.IsCancellationRequested,
                                        ["gateReleased"] = release.Task.IsCompleted,
                                        ["hasNativeSession"] = session.HasSession,
                                        ["manualNow"] = state.Clock.GetUtcNow().ToString("O"),
                                        ["deadline"] = state.Clock.WorkDeadline!.Value.ToString(
                                            "O"
                                        ),
                                        ["controlState"] = (string?)
                                            state.Value(
                                                (
                                                    "SELECT state FROM coordinator_controls "
                                                    + "WHERE id=$id;"
                                                ),
                                                ("$id", requestId)
                                            ),
                                        ["dispatchCount"] = state.Dispatches(
                                            state.Reservation("worker1").Id
                                        ),
                                        ["boundary"] = "neutral-watchdog-before-fixture-teardown",
                                    }.ToJsonString()
                            );
                            Assert.Fail("P7-F01_RED_OWNER_CANCELLATION_NOT_OBSERVED: " + action);
                        }
                        Assert.IsTrue(business.IsCancellationRequested);
                        Assert.IsFalse(token.IsCancellationRequested);
                        Assert.IsFalse(release.Task.IsCompleted);
                        Assert.IsLessThan(
                            state.Clock.WorkDeadline!.Value,
                            await cancelled.Task.ConfigureAwait(false)
                        );
                        JsonObject result = await state
                            .Active.WaitAsync(TimeSpan.FromSeconds(5), token)
                            .ConfigureAwait(false);
                        Assert.AreEqual(
                            "ownership_or_usage_unresolved",
                            result["code"]!.GetValue<string>()
                        );
                        Assert.AreEqual(
                            reason,
                            state.Database.ReadMetadata("lifecycle:stop_reason")
                        );
                        Assert.AreEqual(
                            reason,
                            state.Value(
                                (
                                    "SELECT stop_reason FROM coordinator_segments ORDER BY id "
                                    + "DESC LIMIT 1;"
                                )
                            )
                        );
                        Assert.AreEqual(
                            0L,
                            state.Number(
                                "SELECT COUNT(*) FROM coordinator_segments WHERE ended_at IS NULL;"
                            )
                        );
                        Assert.AreEqual(
                            action,
                            state.Value(
                                "SELECT action FROM coordinator_controls WHERE id=$id;",
                                ("$id", requestId)
                            )
                        );
                        // Missing Open/usage evidence cannot acknowledge quiescent
                        // success, even though the common owned-host join completed.
                        Assert.AreEqual(
                            "pending",
                            state.Value(
                                "SELECT state FROM coordinator_controls WHERE id=$id;",
                                ("$id", requestId)
                            )
                        );
                        Assert.IsNull(
                            state.Value(
                                "SELECT completed_at FROM coordinator_controls WHERE id=$id;",
                                ("$id", requestId)
                            )
                        );
                        Assert.AreEqual("uncertain", state.Reservation("worker1").State);
                        Assert.IsNull(state.Reservation("worker1").Actual);
                        Assert.IsGreaterThan(0m, state.Reservation("worker1").Allowance);
                        Assert.AreEqual(0L, state.Dispatches(state.Reservation("worker1").Id));
                        Assert.AreEqual(
                            0L,
                            state.Number("SELECT COUNT(*) FROM coordinator_turns;")
                        );
                        Assert.IsFalse(session.HasSession);
                        Phase7Sessions.Host host = state.Sessions.Hosts.Single(item =>
                            item.Operation == "run"
                        );
                        Assert.IsNotNull(host.Ready);
                        Assert.IsGreaterThan(0, host.Ready.Pid);
                        Assert.IsGreaterThan(0L, host.Ready.BirthTicks);
                        Assert.IsTrue(
                            host.Witness["normalExitBeforeWrapperDispose"]!.GetValue<bool>()
                        );
                        Console.WriteLine(
                            "PHASE7_STARTUP_OWNER_GREEN "
                                + new JsonObject
                                {
                                    ["action"] = action,
                                    ["requestId"] = requestId,
                                    ["reason"] = reason,
                                    ["result"] = result.DeepClone(),
                                    ["host"] = host.Witness,
                                    ["beforeDeadline"] =
                                        state.Clock.GetUtcNow() < state.Clock.WorkDeadline.Value,
                                    ["gateReleased"] = false,
                                    ["callerCancelled"] = false,
                                    ["reservationState"] = state.Reservation("worker1").State,
                                    ["allowance"] = state.Reservation("worker1").Allowance,
                                    ["actual"] = state.Reservation("worker1").Actual,
                                }.ToJsonString()
                        );
                    }
                    catch (Exception error)
                    {
                        primary = error;
                        throw;
                    }
                    finally
                    {
                        try
                        {
                            observation?.Cancel();
                        }
                        catch (Exception secondary)
                        {
                            RetainTeardownFailure("observer-cancel", secondary);
                        }
                        if (drive is not null)
                        {
                            try
                            {
                                await drive.ConfigureAwait(false);
                            }
                            catch (OperationCanceledException)
                                when (observation?.IsCancellationRequested == true)
                            { }
                            catch (Exception secondary)
                            {
                                RetainTeardownFailure("observer-join", secondary);
                            }
                        }
                        // After a RED assertion only, release fixture-owned work and
                        // join the real run. This release is never owner-stop proof.
                        if (!state.Active.IsCompleted)
                        {
                            release.TrySetResult();
                            try
                            {
                                JsonObject teardown = await state
                                    .Active.WaitAsync(
                                        TimeSpan.FromSeconds(5),
                                        CancellationToken.None
                                    )
                                    .ConfigureAwait(false);
                                Console.WriteLine(
                                    "PHASE7_STARTUP_OWNER_TEARDOWN_JOIN "
                                        + new JsonObject
                                        {
                                            ["action"] = action,
                                            ["afterPrimaryFailure"] = primary is not null,
                                            ["fixtureGateReleased"] = true,
                                            ["result"] = teardown.DeepClone(),
                                            ["host"] = state
                                                .Sessions.Hosts.Single(item =>
                                                    item.Operation == "run"
                                                )
                                                .Witness,
                                        }.ToJsonString()
                                );
                            }
                            catch (Exception secondary)
                            {
                                RetainTeardownFailure("subject-release-join", secondary);
                            }
                        }
                        try
                        {
                            observation?.Dispose();
                        }
                        catch (Exception secondary)
                        {
                            RetainTeardownFailure("observer-dispose", secondary);
                        }
                    }
                    if (teardownErrors.Count > 0)
                    {
                        throw new AggregateException(
                            "Startup owner observer teardown failed.",
                            teardownErrors
                        );
                    }
                })
                .ConfigureAwait(false);
        }
        catch (Exception error)
        {
            primary ??= error;
            throw;
        }
        finally
        {
            // RunBody has now finished or attempted the common subject/host
            // cleanup as well. Ending the real-token observer is the final step.
            try
            {
                registration.Dispose();
            }
            catch (Exception secondary)
            {
                RetainTeardownFailure("registration-dispose-last", secondary);
                if (primary is null)
                {
                    teardownFailure = ExceptionDispatchInfo.Capture(secondary);
                }
            }
        }
        teardownFailure?.Throw();
    }

    private static async Task DriveOwnerPollingAsync(
        Phase7Run state,
        Task<DateTimeOffset> cancelled,
        CancellationToken token
    )
    {
        while (!cancelled.IsCompleted)
        {
            DateTimeOffset cutoff = state.Clock.WorkDeadline!.Value;
            using CancellationTokenSource waiting = CancellationTokenSource.CreateLinkedTokenSource(
                token
            );
            Task step = state.Clock.TickAsync(waiting.Token, cutoff, () => cancelled.IsCompleted);
            if (await Task.WhenAny(step, cancelled).ConfigureAwait(false) == cancelled)
            {
                waiting.Cancel();
                try
                {
                    await step.ConfigureAwait(false);
                }
                catch (OperationCanceledException) when (waiting.IsCancellationRequested) { }
                return;
            }
            await step.ConfigureAwait(false);
            Assert.IsLessThan(cutoff, state.Clock.GetUtcNow());
        }
    }

    [TestMethod]
    public async Task ActualTimerReadinessRevalidatesRegistrationStateAndObservationLifetime()
    {
        CancellationToken token = testContext.CancellationToken;
        Phase7Clock clock = new();
        DateTimeOffset origin = clock.GetUtcNow();
        List<string> callbacks = [];
        using IDisposable first = clock.BeginObservation();
        using ITimer early = clock.CreateTimer(
            _ => callbacks.Add("early"),
            null,
            TimeSpan.FromSeconds(1),
            Timeout.InfiniteTimeSpan
        );
        await clock.WaitForPollAsync(token).ConfigureAwait(false);
        Assert.IsTrue(early.Change(Timeout.InfiniteTimeSpan, Timeout.InfiniteTimeSpan));
        using ITimer removed = clock.CreateTimer(
            _ => callbacks.Add("removed"),
            null,
            TimeSpan.FromSeconds(2),
            Timeout.InfiniteTimeSpan
        );
        await clock.WaitForPollAsync(token).ConfigureAwait(false);
        removed.Dispose();
        using ITimer later = clock.CreateTimer(
            _ => callbacks.Add("later"),
            null,
            TimeSpan.FromSeconds(3),
            Timeout.InfiniteTimeSpan
        );
        Assert.IsTrue(later.Change(TimeSpan.FromSeconds(4), Timeout.InfiniteTimeSpan));
        await clock.TickAsync(token).ConfigureAwait(false);
        Assert.AreEqual(origin.AddSeconds(4), clock.GetUtcNow());
        Assert.AreEqual("later", string.Join("|", callbacks));
        Assert.IsTrue(later.Change(TimeSpan.FromSeconds(1), Timeout.InfiniteTimeSpan));
        first.Dispose();
        using IDisposable second = clock.BeginObservation();
        using ITimer current = clock.CreateTimer(
            _ => callbacks.Add("current"),
            null,
            TimeSpan.FromSeconds(3),
            Timeout.InfiniteTimeSpan
        );
        await clock.TickAsync(token).ConfigureAwait(false);
        Assert.AreEqual(origin.AddSeconds(7), clock.GetUtcNow());
        Assert.AreEqual("later|later|current", string.Join("|", callbacks));
        bool deadlineFired = false;
        using ITimer boundary = clock.CreateTimer(
            _ => deadlineFired = true,
            null,
            TimeSpan.FromSeconds(3),
            Timeout.InfiniteTimeSpan
        );
        using CancellationTokenSource observation = CancellationTokenSource.CreateLinkedTokenSource(
            token
        );
        Task stopped = clock.TickAsync(observation.Token, origin.AddSeconds(10));
        observation.Cancel();
        await Assert
            .ThrowsAsync<OperationCanceledException>(async () =>
                await stopped.ConfigureAwait(false)
            )
            .ConfigureAwait(false);
        Assert.AreEqual(origin.AddSeconds(7), clock.GetUtcNow());
        Assert.IsFalse(deadlineFired);

        using ITimer immediate = clock.CreateTimer(
            _ => { }, null, TimeSpan.Zero, Timeout.InfiniteTimeSpan
        );
        clock.Advance(TimeSpan.Zero);
        using ITimer future = clock.CreateTimer(
            _ => { }, null, TimeSpan.FromSeconds(1), Timeout.InfiniteTimeSpan
        );
        await clock.TickAsync(token, origin.AddSeconds(10)).ConfigureAwait(false);
        Assert.AreEqual(origin.AddSeconds(8), clock.GetUtcNow());
        Assert.IsTrue(immediate.Change(TimeSpan.Zero, Timeout.InfiniteTimeSpan));
        clock.Advance(TimeSpan.Zero);
        using ITimer changedFuture = clock.CreateTimer(
            _ => { }, null, TimeSpan.FromSeconds(1), Timeout.InfiniteTimeSpan
        );
        await clock.TickAsync(token, origin.AddSeconds(10)).ConfigureAwait(false);
        Assert.AreEqual(origin.AddSeconds(9), clock.GetUtcNow());
    }

    [TestMethod]
    [TestCategory("StartupWaitAndDrainCompletion")]
    [Timeout(120_000, CooperativeCancellation = true)]
    public async Task ExcludedExpiryCannotWakeProgressAndGoalChangeJoinsWithoutALateAdvance()
    {
        CancellationToken token = testContext.CancellationToken;
        Phase7Clock clock = new();
        using IDisposable scope = clock.BeginObservation();
        DateTimeOffset origin = clock.GetUtcNow();
        DateTimeOffset deadline = origin.AddSeconds(10);
        bool expired = false,
            callbackRan = false,
            goal = false;
        using ITimer expiry = clock.CreateTimer(
            _ => expired = true,
            null,
            TimeSpan.FromSeconds(10),
            Timeout.InfiniteTimeSpan
        );
        TaskCompletionSource subject = new(TaskCreationOptions.RunContinuationsAsynchronously);
        Task waiting = clock.UntilAsync(() => goal, subject.Task, deadline, token);
        Assert.IsFalse(waiting.IsCompleted);
        Assert.AreEqual(origin, clock.GetUtcNow());
        Assert.IsFalse(expired);
        goal = true;
        using ITimer actualChange = clock.CreateTimer(
            _ => callbackRan = true,
            null,
            TimeSpan.FromSeconds(1),
            Timeout.InfiniteTimeSpan
        );
        await waiting.WaitAsync(token).ConfigureAwait(false);
        Assert.AreEqual(origin, clock.GetUtcNow());
        Assert.IsFalse(expired);
        Assert.IsFalse(callbackRan);
        // The joined observation did not retain a mutating step. Actual
        // scheduling still belongs exclusively to the underlying manual clock.
        clock.Advance(TimeSpan.FromSeconds(1));
        Assert.AreEqual(origin.AddSeconds(1), clock.GetUtcNow());
        Assert.IsTrue(callbackRan);
        Assert.IsFalse(expired);
    }

    [TestMethod]
    [TestCategory("StartupWaitAndDrainCompletion")]
    [Timeout(120_000, CooperativeCancellation = true)]
    public async Task ActualTaskGoalWakesExcludedTimerObservationWithoutAnotherRegistration()
    {
        CancellationToken token = testContext.CancellationToken;
        Phase7Clock clock = new();
        using IDisposable scope = clock.BeginObservation();
        DateTimeOffset origin = clock.GetUtcNow();
        bool expired = false;
        using ITimer expiry = clock.CreateTimer(
            _ => expired = true,
            null,
            TimeSpan.FromSeconds(10),
            Timeout.InfiniteTimeSpan
        );
        TaskCompletionSource goal = new(TaskCreationOptions.RunContinuationsAsynchronously);
        TaskCompletionSource subject = new(TaskCreationOptions.RunContinuationsAsynchronously);
        Task waiting = clock.UntilAsync(goal.Task, subject.Task, origin.AddSeconds(10), token);
        Assert.IsFalse(waiting.IsCompleted);
        Assert.AreEqual(origin, clock.GetUtcNow());
        Assert.IsFalse(expired);
        // No CreateTimer/Change/Dispose occurs between the task notification and
        // this join: the goal itself, not another registration, must wake it.
        goal.SetResult();
        await waiting.WaitAsync(token).ConfigureAwait(false);
        Assert.AreEqual(origin, clock.GetUtcNow());
        Assert.IsFalse(expired);
        Assert.IsFalse(subject.Task.IsCompleted);
    }

    [TestMethod]
    [TestCategory("StartupWaitAndDrainCompletion")]
    public async Task AdmissionDeadlineClosesAtTheBoundaryAndCannotBeReopened()
    {
        ManualScenarioClock clock = new();
        DateTimeOffset deadline = clock.GetUtcNow().AddSeconds(10);
        using WorkAdmission admission = new(clock, deadline, testContext.CancellationToken);
        CancellationToken token = admission.Token;
        clock.Advance(TimeSpan.FromSeconds(9));
        Assert.IsFalse(admission.IsClosed);
        admission.ThrowIfClosed();
        clock.Advance(TimeSpan.FromSeconds(1));
        Assert.IsTrue(admission.IsClosed);
        OperationCanceledException stopped = Assert.ThrowsExactly<OperationCanceledException>(
            admission.ThrowIfClosed
        );
        Assert.AreEqual(token, stopped.CancellationToken);
        await admission.CloseAsync().ConfigureAwait(false);
        clock.Advance(TimeSpan.FromSeconds(30));
        Assert.AreEqual(deadline, admission.Deadline);
        Assert.IsTrue(token.IsCancellationRequested);
    }

    [TestMethod]
    [TestCategory("StartupWaitAndDrainCompletion")]
    [DataRow("open")]
    [DataRow("prepare")]
    [Timeout(120_000, CooperativeCancellation = true)]
    public async Task EnteredStartupOrPreparationWaitConsumesDeadlineWithoutDispatch(
        string boundary
    )
    {
        CancellationToken token = testContext.CancellationToken;
        Phase7Run state = await Phase7Run
            .CreateAsync(token, workers: 1, interval: 1, duration: 10, allocation: "shared")
            .ConfigureAwait(false);
        await state
            .RunBodyAsync(async () =>
            {
                TaskCompletionSource<Phase7Sessions.Session> entered = new(
                    TaskCreationOptions.RunContinuationsAsynchronously
                );
                TaskCompletionSource release = new(
                    TaskCreationOptions.RunContinuationsAsynchronously
                );
                async Task Hold(Phase7Sessions.Session session, CancellationToken business)
                {
                    entered.TrySetResult(session);
                    await release.Task.WaitAsync(business).ConfigureAwait(false);
                }
                if (boundary == "open")
                {
                    state.Sessions.BeforeOpen = Hold;
                }
                else
                {
                    state.Sessions.BeforeRead = (session, business) =>
                        state.Database.ReadMetadata($"worker:{session.Worker}:lifecycle") == "ready"
                        && session.CompletedTurns == 0
                            ? Hold(session, business)
                            : Task.CompletedTask;
                }
                _ = state.Start();
                Phase7Sessions.Session waiting = await state
                    .FixedAsync(next => entered.Task.WaitAsync(next), token)
                    .ConfigureAwait(false);
                await state.Clock.WaitForPollAsync(token, state.WorkDeadline).ConfigureAwait(false);
                Assert.AreEqual(boundary == "prepare", waiting.HasSession);
                Assert.AreEqual(0, waiting.CompletedTurns);
                state.Clock.Advance(TimeSpan.FromSeconds(10));
                await state.Active.WaitAsync(token).ConfigureAwait(false);
                JsonObject result = await state.Active.ConfigureAwait(false);
                Assert.AreEqual(
                    "ownership_or_usage_unresolved",
                    result["code"]!.GetValue<string>()
                );
                Assert.AreEqual("uncertain", state.Reservation("worker1").State);
                Assert.IsNull(state.Reservation("worker1").Actual);
                Assert.AreEqual(0L, state.Number("SELECT COUNT(*) FROM coordinator_turns;"));
                Assert.IsNull(state.Database.ReadMetadata("native:bootstrap:" + waiting.SessionId));
                Assert.AreEqual(0, waiting.CompletedTurns);
                // This is missing startup/preparation evidence, not measured zero
                // usage or verified pre-dispatch cancellation.
                Assert.IsGreaterThan(0m, state.Reservation("worker1").Allowance);
                release.TrySetResult();
            })
            .ConfigureAwait(false);
    }

    [TestMethod]
    [TestCategory("StartupWaitAndDrainCompletion")]
    [DataRow(false)]
    [DataRow(true)]
    [Timeout(120_000, CooperativeCancellation = true)]
    public async Task CompletedTurnBeforeDeadlineOrDuringThirtySecondGraceIsAcknowledged(
        bool duringGrace
    )
    {
        CancellationToken token = testContext.CancellationToken;
        Phase7Run state = await Phase7Run
            .CreateAsync(token, workers: 1, duration: 10)
            .ConfigureAwait(false);
        await state
            .RunBodyAsync(async () =>
            {
                _ = state.Start();
                Phase7Sessions.Turn turn = await state
                    .Sessions.NextTurnAsync("worker1", token)
                    .ConfigureAwait(false);
                long batch = state.SendingBatch("worker1");
                Assert.AreEqual(TimeSpan.FromSeconds(10), turn.Timeout);
                await state.Clock.WaitForPollAsync(token, state.WorkDeadline).ConfigureAwait(false);
                state.Clock.Advance(TimeSpan.FromSeconds(duringGrace ? 10 : 9));
                if (duringGrace)
                {
                    await state
                        .UntilAsync(
                            () => state.Database.ReadMetadata("lifecycle") == "draining",
                            state.DrainDeadline,
                            token
                        )
                        .ConfigureAwait(false);
                    Assert.IsFalse(turn.Token.IsCancellationRequested);
                    await state
                        .Clock.WaitForPollAsync(token, state.DrainDeadline)
                        .ConfigureAwait(false);
                    state.Clock.Advance(TimeSpan.FromSeconds(5));
                }
                turn.Complete();
                await state
                    .WaitAckAsync(
                        batch,
                        duringGrace ? state.DrainDeadline : state.WorkDeadline,
                        token
                    )
                    .ConfigureAwait(false);
                if (!duringGrace)
                {
                    Assert.AreEqual(
                        "sent",
                        state.Value(
                            "SELECT state FROM delivery_batches WHERE id=$id;",
                            ("$id", batch)
                        )
                    );
                    Assert.IsLessThan(state.WorkDeadline, state.Clock.GetUtcNow());
                    state.Clock.Advance(state.Clock.WorkDeadline!.Value - state.Clock.GetUtcNow());
                }
                await state.Active.WaitAsync(token).ConfigureAwait(false);
                GitScenarioState.Data(await state.Active.ConfigureAwait(false));
                JsonObject status = GitScenarioState.Data(state.Coordinator().Status());
                Assert.AreEqual(
                    "completed",
                    status["deadlineDrain"]!["outcome"]!.GetValue<string>()
                );
                Assert.AreEqual("deadline", status["stopReason"]!.GetValue<string>());
                Assert.AreEqual("reconciled", state.Reservation("worker1").State);
                Assert.AreEqual(10m, state.Reservation("worker1").Actual);
                Assert.AreEqual(
                    "true",
                    state.Database.ReadMetadata("native:bootstrap:" + turn.Session.SessionId)
                );
                Assert.AreEqual(
                    "sent",
                    state.Value("SELECT state FROM delivery_batches WHERE id=$id;", ("$id", batch))
                );
                Assert.AreEqual(1, turn.Session.CompletedTurns);
                Assert.AreEqual(1L, state.Number("SELECT COUNT(*) FROM coordinator_segments;"));
            })
            .ConfigureAwait(false);
    }

    [TestMethod]
    [TestCategory("StartupWaitAndDrainCompletion")]
    [Timeout(120_000, CooperativeCancellation = true)]
    public async Task GraceExpiryCancelsPendingTurnWithoutAcknowledgementOrReleasedBacking()
    {
        CancellationToken token = testContext.CancellationToken;
        Phase7Run state = await Phase7Run
            .CreateAsync(token, workers: 1, duration: 10)
            .ConfigureAwait(false);
        await state
            .RunBodyAsync(async () =>
            {
                _ = state.Start();
                Phase7Sessions.Turn turn = await state
                    .Sessions.NextTurnAsync("worker1", token)
                    .ConfigureAwait(false);
                long batch = state.SendingBatch("worker1");
                await state.Clock.WaitForPollAsync(token, state.WorkDeadline).ConfigureAwait(false);
                state.Clock.Advance(TimeSpan.FromSeconds(10));
                await state
                    .UntilAsync(
                        () => state.Database.ReadMetadata("lifecycle") == "draining",
                        state.DrainDeadline,
                        token
                    )
                    .ConfigureAwait(false);
                Assert.IsFalse(turn.Token.IsCancellationRequested);
                await state
                    .Clock.WaitForPollAsync(token, state.DrainDeadline)
                    .ConfigureAwait(false);
                state.Clock.Advance(
                    state.Clock.WorkDeadline!.Value.AddSeconds(30) - state.Clock.GetUtcNow()
                );
                // Explicit expiry has fired; only observe actual subject settlement.
                await state.Active.WaitAsync(token).ConfigureAwait(false);
                JsonObject result = await state.Active.ConfigureAwait(false);
                Assert.AreEqual(
                    "ownership_or_usage_unresolved",
                    result["code"]!.GetValue<string>()
                );
                Assert.IsTrue(turn.Token.IsCancellationRequested);
                Assert.AreEqual("uncertain", state.Reservation("worker1").State);
                Assert.IsGreaterThan(0m, state.Reservation("worker1").Allowance);
                Assert.AreEqual(0, turn.Session.CompletedTurns);
                Assert.IsNull(
                    state.Database.ReadMetadata("native:bootstrap:" + turn.Session.SessionId)
                );
                Assert.AreNotEqual(
                    "sent",
                    state.Value("SELECT state FROM delivery_batches WHERE id=$id;", ("$id", batch))
                );
                JsonObject status = GitScenarioState.Data(state.Coordinator().Status());
                Assert.AreEqual(
                    "timed-out",
                    status["deadlineDrain"]!["outcome"]!.GetValue<string>()
                );
                Assert.AreEqual("deadline", status["stopReason"]!.GetValue<string>());
            })
            .ConfigureAwait(false);
    }

    [TestMethod]
    [TestCategory("InterruptionAndPeerDrainSafety")]
    [Timeout(120_000, CooperativeCancellation = true)]
    public async Task IdlePeerCannotCancelAnotherPeersHealthyDispatchedDrain()
    {
        CancellationToken token = testContext.CancellationToken;
        Phase7Run state = await Phase7Run.CreateAsync(token, duration: 10).ConfigureAwait(false);
        await state
            .RunBodyAsync(async () =>
            {
                TaskCompletionSource<Task[]> loopTasks = new(
                    TaskCreationOptions.RunContinuationsAsynchronously
                );
                state.ObserveWorkerCompletion = loops =>
                {
                    if (loops.Count == 2)
                    {
                        loopTasks.TrySetResult(loops.ToArray());
                    }
                };
                _ = state.Start();
                Phase7Sessions.Turn idle = await state
                    .Sessions.NextTurnAsync("worker1", token)
                    .ConfigureAwait(false);
                Phase7Sessions.Turn healthy = await state
                    .Sessions.NextTurnAsync("worker2", token)
                    .ConfigureAwait(false);
                Task[] actualLoops = await state
                    .FixedAsync(next => loopTasks.Task.WaitAsync(next), token)
                    .ConfigureAwait(false);
                long idleBatch = state.SendingBatch("worker1");
                long healthyBatch = state.SendingBatch("worker2");
                idle.Complete();
                await state
                    .WaitAckAsync(idleBatch, state.WorkDeadline, token)
                    .ConfigureAwait(false);
                await state.Clock.WaitForPollAsync(token, state.WorkDeadline).ConfigureAwait(false);
                state.Clock.Advance(state.Clock.WorkDeadline!.Value - state.Clock.GetUtcNow());
                await state
                    .UntilAsync(
                        () => state.Database.ReadMetadata("lifecycle") == "draining",
                        state.DrainDeadline,
                        token
                    )
                    .ConfigureAwait(false);
                await state.FixedAsync(actualLoops[0], token).ConfigureAwait(false);
                await state.Clock
                    .WaitForPollAsync(token, state.DrainDeadline)
                    .ConfigureAwait(false);
                DateTimeOffset drainAt = state.Clock.GetUtcNow();
                Assert.IsFalse(healthy.Token.IsCancellationRequested);
                Assert.AreEqual(
                    "sending",
                    state.Value(
                        "SELECT state FROM delivery_batches WHERE id=$id;",
                        ("$id", healthyBatch)
                    )
                );
                Assert.AreEqual(TimeSpan.FromSeconds(10), healthy.Timeout);
                healthy.Complete();
                await state.Active.WaitAsync(token).ConfigureAwait(false);
                Assert.AreEqual(drainAt, state.Clock.GetUtcNow());
                GitScenarioState.Data(await state.Active.ConfigureAwait(false));
                Assert.AreEqual(
                    "sent",
                    state.Value(
                        "SELECT state FROM delivery_batches WHERE id=$id;",
                        ("$id", healthyBatch)
                    )
                );
                Assert.AreEqual(10m, state.Reservation("worker1").Actual);
                Assert.AreEqual(10m, state.Reservation("worker2").Actual);
                Assert.AreEqual(
                    "completed",
                    GitScenarioState.Data(state.Coordinator().Status())["deadlineDrain"]![
                        "outcome"
                    ]!.GetValue<string>()
                );
            })
            .ConfigureAwait(false);
    }

    [TestMethod]
    [TestCategory("InterruptionAndPeerDrainSafety")]
    [TestCategory("RampAdmissionCadence")]
    [DataRow("caller", "caller-cancelled")]
    [DataRow("owner", "owner-pause")]
    [DataRow("failure", "controlled_turn_failure")]
    [Timeout(120_000, CooperativeCancellation = true)]
    public async Task AdmittedPeerSafetyStopDoesNotUseGraceOrStartColdPeer(
        string cause,
        string reason
    )
    {
        CancellationToken token = testContext.CancellationToken;
        Phase7Run state = await Phase7Run
            .CreateAsync(token, interval: 10, allocation: "shared")
            .ConfigureAwait(false);
        await state
            .RunBodyAsync(async () =>
            {
                DateTimeOffset began = state.Clock.GetUtcNow();
                _ = state.Start();
                Phase7Sessions.Turn turn = await state
                    .FixedAsync(
                        next => state.Sessions.NextTurnAsync("worker1", next),
                        token
                    )
                    .ConfigureAwait(false);
                await state
                    .Clock.WaitForPollAsync(token, began.AddSeconds(10))
                    .ConfigureAwait(false);
                Assert.AreEqual(0m, state.Reservation("worker2").Allowance);
                JsonObject? failureEvidence = null;
                if (cause == "caller")
                {
                    long batch = state.SendingBatch("worker1");
                    state.CancelCaller();
                    JsonObject result = await state.Active.WaitAsync(token).ConfigureAwait(false);
                    Assert.IsFalse(result["ok"]!.GetValue<bool>());
                    Assert.AreEqual(
                        "ownership_or_usage_unresolved",
                        result["code"]!.GetValue<string>()
                    );
                    Assert.AreEqual("unresolved", state.Database.ReadMetadata("lifecycle"));
                    Assert.AreEqual(
                        1L,
                        state.Number(
                            (
                                "SELECT COUNT(*) FROM coordinator_segments\nWHERE "
                                + "state='unresolved' AND stop_reason='caller-cancelled' AND "
                                + "ended_at IS NOT NULL;"
                            )
                        )
                    );
                    Assert.IsTrue(turn.Token.IsCancellationRequested);
                    Assert.AreEqual("uncertain", state.Reservation("worker1").State);
                    Assert.IsGreaterThan(0m, state.Reservation("worker1").Allowance);
                    Assert.IsNull(state.Reservation("worker1").Actual);
                    Assert.AreEqual(0, turn.Session.CompletedTurns);
                    Assert.IsNull(
                        state.Database.ReadMetadata("native:bootstrap:" + turn.Session.SessionId)
                    );
                    Assert.AreEqual(
                        0L,
                        state.Number(
                            "SELECT COUNT(*) FROM coordinator_turns WHERE batch_id=$id;",
                            ("$id", batch)
                        )
                    );
                    Assert.AreNotEqual(
                        "sent",
                        state.Value(
                            "SELECT state FROM delivery_batches WHERE id=$id;",
                            ("$id", batch)
                        )
                    );
                    Assert.AreEqual(
                        0L,
                        state.Number(
                            (
                                "SELECT COUNT(*) FROM shown_notices s JOIN events e ON "
                                + "e.id=s.event_id WHERE e.batch_id=$id;"
                            ),
                            ("$id", batch)
                        )
                    );
                }
                else if (cause == "owner")
                {
                    GitScenarioState.Data(state.Coordinator().RequestControl("pause"));
                    await state
                        .UntilAsync(state.Handoff!.OwnerStopped.Task, began.AddSeconds(10), token)
                        .ConfigureAwait(false);
                    await state.Active.WaitAsync(token).ConfigureAwait(false);
                    _ = await state.Active.ConfigureAwait(false);
                }
                else
                {
                    long batch = state.SendingBatch("worker1");
                    CreditReservation original = state.Reservation("worker1");
                    Assert.AreEqual(1L, state.Dispatches(original.Id));
                    OperationException supplied = new(
                        "controlled_turn_failure",
                        "Controlled terminal failure."
                    );
                    turn.Fail(supplied);
                    OperationException observed = await Assert
                        .ThrowsExactlyAsync<OperationException>(() => turn.Response)
                        .ConfigureAwait(false);
                    Assert.AreSame(supplied, observed);
                    Assert.AreEqual("controlled_turn_failure", observed.Code);
                    Assert.IsTrue(turn.Response.IsFaulted);
                    Assert.IsFalse(turn.Response.IsCanceled);
                    Assert.IsFalse(turn.Response.IsCompletedSuccessfully);
                    await state.Active.WaitAsync(token).ConfigureAwait(false);
                    JsonObject result = await state.Active.ConfigureAwait(false);
                    Assert.IsFalse(result["ok"]!.GetValue<bool>());
                    Assert.AreEqual(
                        "ownership_or_usage_unresolved",
                        result["code"]!.GetValue<string>()
                    );
                    Assert.AreEqual("unresolved", state.Database.ReadMetadata("lifecycle"));
                    Assert.AreEqual(
                        1L,
                        state.Number(
                            (
                                "SELECT COUNT(*) FROM coordinator_segments\nWHERE "
                                + "state='unresolved' AND stop_reason='controlled_turn_failure' "
                                + "AND ended_at IS NOT NULL;"
                            )
                        )
                    );
                    CreditReservation retained = state.Reservation("worker1");
                    Assert.AreEqual(original.Id, retained.Id);
                    Assert.AreEqual("uncertain", retained.State);
                    Assert.IsGreaterThan(0m, retained.Allowance);
                    Assert.IsNull(retained.Actual);
                    Assert.AreEqual(0, turn.Session.CompletedTurns);
                    Assert.IsNull(
                        state.Database.ReadMetadata("native:bootstrap:" + turn.Session.SessionId)
                    );
                    Assert.AreEqual(
                        0L,
                        state.Number(
                            "SELECT COUNT(*) FROM coordinator_turns WHERE batch_id=$id;",
                            ("$id", batch)
                        )
                    );
                    Assert.AreNotEqual(
                        "sent",
                        state.Value(
                            "SELECT state FROM delivery_batches WHERE id=$id;",
                            ("$id", batch)
                        )
                    );
                    Assert.AreEqual(
                        0L,
                        state.Number(
                            (
                                "SELECT COUNT(*) FROM shown_notices s JOIN events e ON "
                                + "e.id=s.event_id WHERE e.batch_id=$id;"
                            ),
                            ("$id", batch)
                        )
                    );
                    Assert.AreEqual(1L, state.Dispatches(original.Id));
                    failureEvidence = new()
                    {
                        ["result"] = result,
                        ["faultCode"] = observed.Code,
                        ["responseState"] = turn.Response.Status.ToString(),
                        ["completedOperationTokenCancelled"] = turn.Token.IsCancellationRequested,
                        ["reservationId"] = retained.Id,
                        ["reservationState"] = retained.State,
                        ["allowance"] = retained.Allowance,
                        ["actual"] = retained.Actual,
                        ["dispatches"] = state.Dispatches(retained.Id),
                        ["completedTurns"] = turn.Session.CompletedTurns,
                        ["batchId"] = batch,
                    };
                }
                Assert.AreEqual(reason, state.Database.ReadMetadata("lifecycle:stop_reason"));
                Assert.IsLessThan(TimeSpan.FromSeconds(30), state.Clock.GetUtcNow() - began);
                if (cause != "failure")
                {
                    Assert.IsTrue(turn.Token.IsCancellationRequested);
                }
                Assert.IsNull(state.Database.ReadMetadata("worker:worker2:session"));
                Assert.AreEqual("released", state.Reservation("worker2").State);
                Assert.AreEqual(0m, state.Reservation("worker2").Allowance);
                Assert.AreEqual(
                    "not-entered",
                    GitScenarioState.Data(state.Coordinator().Status())["deadlineDrain"]![
                        "outcome"
                    ]!.GetValue<string>()
                );
                if (failureEvidence is not null)
                {
                    failureEvidence["stopReason"] = state.Database.ReadMetadata(
                        "lifecycle:stop_reason"
                    );
                    failureEvidence["manualElapsedSeconds"] = (
                        state.Clock.GetUtcNow() - began
                    ).TotalSeconds;
                    failureEvidence["coldSession"] = state.Database.ReadMetadata(
                        "worker:worker2:session"
                    );
                    failureEvidence["coldReservationState"] = state.Reservation("worker2").State;
                    failureEvidence["coldAllowance"] = state.Reservation("worker2").Allowance;
                    failureEvidence["drainOutcome"] = GitScenarioState.Data(
                        state.Coordinator().Status()
                    )["deadlineDrain"]!["outcome"]!.GetValue<string>();
                    Console.WriteLine(
                        "PHASE7_CONTROLLED_FAILURE_GREEN " + failureEvidence.ToJsonString()
                    );
                }
            })
            .ConfigureAwait(false);
    }

    [TestMethod]
    [TestCategory("RampAdmissionCadence")]
    [Timeout(120_000, CooperativeCancellation = true)]
    public async Task ZeroIntervalLegacyPeersAreEligibleWithoutAdvancingManualTime()
    {
        CancellationToken token = testContext.CancellationToken;
        Phase7Run state = await Phase7Run.CreateAsync(token).ConfigureAwait(false);
        await state
            .RunBodyAsync(async () =>
            {
                DateTimeOffset began = state.Clock.GetUtcNow();
                _ = state.Start();
                Phase7Sessions.Turn one = await state
                    .Sessions.NextTurnAsync("worker1", token)
                    .ConfigureAwait(false);
                Phase7Sessions.Turn two = await state
                    .Sessions.NextTurnAsync("worker2", token)
                    .ConfigureAwait(false);
                Assert.AreEqual(began, state.Clock.GetUtcNow());
                Assert.AreNotEqual(one.Session.SessionId, two.Session.SessionId);
                Assert.AreEqual(100m, state.Reservation("worker1").Allowance);
                Assert.AreEqual(100m, state.Reservation("worker2").Allowance);
                long batch1 = state.SendingBatch("worker1");
                long batch2 = state.SendingBatch("worker2");
                one.Complete();
                two.Complete();
                await state.WaitAckAsync(batch1, state.WorkDeadline, token).ConfigureAwait(false);
                await state.WaitAckAsync(batch2, state.WorkDeadline, token).ConfigureAwait(false);
                GitScenarioState.Data(
                    await state
                        .StopRunningAsync("pause", state.WorkDeadline, token)
                        .ConfigureAwait(false)
                );
            })
            .ConfigureAwait(false);
    }

    [TestMethod]
    [TestCategory("StartupWaitAndDrainCompletion")]
    [Timeout(120_000, CooperativeCancellation = true)]
    public async Task SharedPreparationWaitingForBackingCannotDispatchOrAcquireWindowAfterDeadline()
    {
        CancellationToken token = testContext.CancellationToken;
        Phase7Run state = await Phase7Run
            .CreateAsync(token, duration: 60, allocation: "shared", allowance: 127.25m)
            .ConfigureAwait(false);
        await state
            .RunBodyAsync(async () =>
            {
                using Phase7Handoff handoff = new();
                state.Handoff = handoff;
                _ = state.Start();
                Phase7Handoff.Startup firstStart = await state
                    .FixedAsync(next => handoff.FirstStarting.Task.WaitAsync(next), token)
                    .ConfigureAwait(false);
                await state.FixedAsync(firstStart.Operation, token).ConfigureAwait(false);
                await state
                    .WaitDurableAsync(
                        () =>
                            state.Number(
                                (
                                    "SELECT COUNT(*) FROM delivery_batches WHERE "
                                    + "recipient='worker1' AND state='sending';"
                                )
                            ) == 1,
                        state.WorkDeadline,
                        token
                    )
                    .ConfigureAwait(false);
                Phase7Sessions.Turn first = await state
                    .FixedAsync(next => state.Sessions.NextTurnAsync("worker1", next), token)
                    .ConfigureAwait(false);
                Assert.AreEqual(100m, state.Reservation("worker1").Allowance);
                Assert.AreEqual(0m, state.Reservation("worker2").Allowance);
                long firstBatch = state.SendingBatch("worker1");
                // Capture the REAL second operation while the first100 grant still
                // leaves only20 backing: entry is not successful startup/consumption.
                Phase7Handoff.Startup secondStart = await state
                    .FixedAsync(next => handoff.SecondStarting.Task.WaitAsync(next), token)
                    .ConfigureAwait(false);
                Assert.AreEqual("worker2", secondStart.Worker);
                Assert.AreEqual(state.Reservation("worker2").Id, secondStart.ReservationId);
                Assert.AreEqual(state.WorkerPath("worker2"), secondStart.Cwd);
                Assert.IsFalse(secondStart.Operation.IsCompleted);
                Assert.IsTrue(state.Reservation("worker1").GrantOpen);
                Assert.AreEqual(0m, state.Reservation("worker2").Allowance);
                Assert.AreEqual(
                    107.25m,
                    state.Ledger.Status(state.Database.RunId, 127.25m)[
                        "runCommitted"
                    ]!.GetValue<decimal>()
                );
                DateTimeOffset accountingAt = state.Clock.GetUtcNow();
                first.Complete();
                Assert.AreEqual(
                    10m,
                    await state
                        .FixedAsync(next => handoff.PositiveRecorded.Task.WaitAsync(next), token)
                        .ConfigureAwait(false)
                );
                Assert.AreEqual(10m, state.Reservation("worker1").Actual);
                Assert.IsFalse(state.Reservation("worker1").GrantOpen);
                // The100ms insufficient-backing retry is a REAL cancellable delay.
                // Join THAT same pending operation without consuming business time.
                await state.FixedAsync(secondStart.Operation, token).ConfigureAwait(false);
                Assert.AreEqual(accountingAt, state.Clock.GetUtcNow());
                await state
                    .WaitDurableAsync(
                        () =>
                            state.Number(
                                (
                                    "SELECT COUNT(*) FROM delivery_batches WHERE "
                                    + "recipient='worker2' AND state='sending';"
                                )
                            ) == 1,
                        state.WorkDeadline,
                        token
                    )
                    .ConfigureAwait(false);
                Phase7Sessions.Turn second = await state
                    .FixedAsync(next => state.Sessions.NextTurnAsync("worker2", next), token)
                    .ConfigureAwait(false);
                long secondBatch = state.SendingBatch("worker2");
                Assert.AreEqual(10m, state.Reservation("worker1").Actual);
                Assert.IsFalse(state.Reservation("worker1").GrantOpen);
                Assert.AreEqual(100m, state.Reservation("worker2").Allowance);
                Assert.IsTrue(state.Reservation("worker2").GrantOpen);
                Assert.AreEqual(100d, second.Session.Metadata!["appliedLimit"]!.GetValue<double>());
                Assert.IsFalse(second.Response.IsCompleted);
                state.Invoke(
                    "owner",
                    "message_send",
                    new()
                    {
                        ["recipient"] = "worker1",
                        ["body"] = "Wait for genuinely backed follow-up work.",
                        ["urgent"] = true,
                    }
                );
                await state
                    .UntilAsync(handoff.FollowupPreparation.Task, state.WorkDeadline, token)
                    .ConfigureAwait(false);
                Assert.AreEqual(
                    1L,
                    state.Number(
                        "SELECT COUNT(*) FROM delivery_batches WHERE recipient='worker1' "
                            + "AND state='sending' AND id!=$first;",
                        ("$first", firstBatch)
                    )
                );
                long followupBatch = state.SendingBatch("worker1");
                Assert.AreNotEqual(firstBatch, followupBatch);
                // This same worker's NEW claim follows its previous ACK/verification,
                // unlike PositiveRecorded, which observes only the real record action.
                Assert.AreEqual(
                    "acknowledged",
                    state.Value(
                        "SELECT state FROM coordinator_turns WHERE batch_id=$id;",
                        ("$id", firstBatch)
                    )
                );
                Task<JsonObject> preparation = await state
                    .FixedAsync(next => handoff.FollowupPreparation.Task.WaitAsync(next), token)
                    .ConfigureAwait(false);
                Assert.AreEqual(
                    10m,
                    await state
                        .FixedAsync(next => handoff.FollowupRecorded.Task.WaitAsync(next), token)
                        .ConfigureAwait(false)
                );
                // Only10 credits remain; the30-minimum next window must really wait.
                JsonObject money = state.Ledger.Status(state.Database.RunId, 127.25m);
                Assert.AreEqual(117.25m, money["runCommitted"]!.GetValue<decimal>());
                Assert.IsFalse(preparation.IsCompleted);
                Assert.IsFalse(second.Token.IsCancellationRequested);
                Assert.IsLessThan(30m, 127.25m - money["runCommitted"]!.GetValue<decimal>());
                Assert.AreEqual(
                    "sending",
                    state.Value(
                        "SELECT state FROM delivery_batches WHERE id=$id;",
                        ("$id", followupBatch)
                    )
                );
                Assert.AreEqual(
                    0L,
                    state.Number(
                        "SELECT COUNT(*) FROM coordinator_turns WHERE batch_id=$id;",
                        ("$id", followupBatch)
                    )
                );
                Console.WriteLine(
                    "PHASE7_BACKING_PREPARATION "
                        + new JsonObject
                        {
                            ["firstRecordedActual"] = state.Reservation("worker1").Actual,
                            ["firstCommittedBatch"] = firstBatch,
                            ["secondCommittedBatch"] = secondBatch,
                            ["sameSecondStartupTaskStatus"] =
                                secondStart.Operation.Status.ToString(),
                            ["fixedAccountingAt"] = accountingAt,
                            ["firstWindowOpen"] = state.Reservation("worker1").GrantOpen,
                            ["secondPendingSend"] = !second.Response.IsCompleted,
                            ["secondBackedAllowance"] = state.Reservation("worker2").Allowance,
                            ["followupBatch"] = followupBatch,
                            ["actualPromptPendingAfterSynchronousCreditRead"] =
                                !preparation.IsCompleted,
                            ["runCommitted"] = money["runCommitted"]!.DeepClone(),
                            ["freeBacking"] = 127.25m - money["runCommitted"]!.GetValue<decimal>(),
                            ["setupAt"] = state.Clock.GetUtcNow(),
                            ["workDeadline"] = state.WorkDeadline,
                        }.ToJsonString()
                );
                Assert.AreEqual(1L, state.Dispatches(state.Reservation("worker1").Id));
                Assert.IsFalse(state.Reservation("worker1").GrantOpen);
                long batches = state.Number("SELECT COUNT(*) FROM delivery_batches;");
                long sessions = state
                    .Sessions.Hosts.SelectMany(host => host.Sessions)
                    .Count(session => session.HasSession);
                await state.Clock.WaitForPollAsync(token, state.WorkDeadline).ConfigureAwait(false);
                state.Clock.Advance(state.WorkDeadline - state.Clock.GetUtcNow());
                await state
                    .UntilAsync(
                        () => state.Database.ReadMetadata("lifecycle") == "draining",
                        state.DrainDeadline,
                        token
                    )
                    .ConfigureAwait(false);
                Assert.IsFalse(second.Token.IsCancellationRequested);
                Assert.AreEqual(TimeSpan.FromSeconds(60), second.Timeout);
                second.Complete();
                await state.Active.WaitAsync(token).ConfigureAwait(false);
                GitScenarioState.Data(await state.Active.ConfigureAwait(false));
                Assert.AreEqual("deadline", state.Database.ReadMetadata("lifecycle:stop_reason"));
                Assert.AreEqual(1L, state.Dispatches(state.Reservation("worker1").Id));
                Assert.AreEqual(1L, state.Dispatches(state.Reservation("worker2").Id));
                Assert.AreEqual(10m, state.Reservation("worker1").Actual);
                Assert.AreEqual(10m, state.Reservation("worker2").Actual);
                Assert.IsFalse(state.Reservation("worker1").GrantOpen);
                Assert.IsFalse(state.Reservation("worker2").GrantOpen);
                Assert.AreEqual(100m, state.Reservation("worker1").Allowance);
                Assert.AreEqual(100m, state.Reservation("worker2").Allowance);
                Assert.AreEqual(100d, first.Session.Metadata!["appliedLimit"]!.GetValue<double>());
                Assert.AreEqual(100d, second.Session.Metadata!["appliedLimit"]!.GetValue<double>());
                Assert.AreEqual(batches, state.Number("SELECT COUNT(*) FROM delivery_batches;"));
                Assert.AreEqual(
                    sessions,
                    (long)
                        state
                            .Sessions.Hosts.SelectMany(host => host.Sessions)
                            .Count(session => session.HasSession)
                );
                Assert.AreEqual(
                    27.25m,
                    state.Ledger.Status(state.Database.RunId, 127.25m)[
                        "runCommitted"
                    ]!.GetValue<decimal>()
                );
                Assert.AreEqual(
                    "acknowledged",
                    state.Value(
                        "SELECT state FROM coordinator_turns WHERE batch_id=$id;",
                        ("$id", secondBatch)
                    )
                );
            })
            .ConfigureAwait(false);
    }

    [TestMethod]
    [TestCategory("StartupWaitAndDrainCompletion")]
    [Timeout(120_000, CooperativeCancellation = true)]
    public async Task ExhaustedInflightSharedSendCannotRenewAfterAdmissionCloses()
    {
        CancellationToken token = testContext.CancellationToken;
        Phase7Run state = await Phase7Run
            .CreateAsync(token, workers: 1, duration: 10, allocation: "shared")
            .ConfigureAwait(false);
        await state
            .RunBodyAsync(async () =>
            {
                Phase7Exhaustion exhaustion = new();
                state.Exhaustion = exhaustion;
                _ = state.Start();
                Phase7Sessions.Turn turn = await state
                    .FixedAsync(
                        next => state.Sessions.NextTurnAsync("worker1", next),
                        token
                    )
                    .ConfigureAwait(false);
                await state
                    .FixedAsync(
                        next => exhaustion.Entered.Task.WaitAsync(next),
                        token
                    )
                    .ConfigureAwait(false);
                CreditReservation original = state.Reservation("worker1");
                Assert.AreEqual(100m, original.Allowance);
                Assert.IsTrue(original.GrantOpen);
                Assert.AreEqual(100d, turn.Session.Metadata!["appliedLimit"]!.GetValue<double>());
                Assert.AreEqual(TimeSpan.FromSeconds(10), turn.Timeout);
                long batch = state.SendingBatch("worker1");
                await state.Clock.WaitForPollAsync(token, state.WorkDeadline).ConfigureAwait(false);
                state.Clock.Advance(state.WorkDeadline - state.Clock.GetUtcNow());
                await state
                    .UntilAsync(
                        () => state.Database.ReadMetadata("lifecycle") == "draining",
                        state.DrainDeadline,
                        token
                    )
                    .ConfigureAwait(false);
                Assert.IsFalse(turn.Token.IsCancellationRequested);
                exhaustion.Request();
                await state
                    .FixedAsync(
                        next => exhaustion.Rejected.Task.WaitAsync(next),
                        token
                    )
                    .ConfigureAwait(false);
                await state.Active.WaitAsync(token).ConfigureAwait(false);
                JsonObject result = await state.Active.ConfigureAwait(false);
                Assert.AreEqual(
                    "ownership_or_usage_unresolved",
                    result["code"]!.GetValue<string>()
                );
                Assert.AreEqual(
                    "credit_unknown",
                    state.Database.ReadMetadata("lifecycle:stop_reason")
                );
                Assert.AreEqual(
                    "interrupted",
                    GitScenarioState.Data(state.Coordinator().Status())["deadlineDrain"]![
                        "outcome"
                    ]!.GetValue<string>()
                );
                Assert.AreEqual(original.Id, state.Reservation("worker1").Id);
                Assert.AreEqual(100m, state.Reservation("worker1").Allowance);
                Assert.AreEqual("uncertain", state.Reservation("worker1").State);
                Assert.IsNull(state.Reservation("worker1").Actual);
                Assert.AreEqual(100d, turn.Session.Metadata["appliedLimit"]!.GetValue<double>());
                Assert.AreEqual(0, exhaustion.Applied);
                Assert.AreEqual(0, exhaustion.Verified);
                Assert.AreEqual(1L, state.Dispatches(original.Id));
                Assert.AreEqual(1L, state.Number("SELECT COUNT(*) FROM delivery_batches;"));
                Assert.IsNull(
                    state.Database.ReadMetadata("native:bootstrap:" + turn.Session.SessionId)
                );
                Assert.AreNotEqual(
                    "sent",
                    state.Value("SELECT state FROM delivery_batches WHERE id=$id;", ("$id", batch))
                );
                Assert.AreEqual(
                    0L,
                    state.Number(
                        "SELECT COUNT(*) FROM coordinator_turns WHERE state='acknowledged';"
                    )
                );
                Assert.IsLessThan(state.DrainDeadline, state.Clock.GetUtcNow());
            })
            .ConfigureAwait(false);
    }

    [TestMethod]
    [TestCategory("RampAdmissionCadence")]
    [TestCategory("StartupWaitAndDrainCompletion")]
    [Timeout(120_000, CooperativeCancellation = true)]
    public async Task
    DeadlineDuringPositiveIntervalLeavesColdSharedPeerWithoutSessionGrantOrDispatch()
    {
        CancellationToken token = testContext.CancellationToken;
        Phase7Run state = await Phase7Run
            .CreateAsync(token, interval: 30, duration: 10, allocation: "shared")
            .ConfigureAwait(false);
        await state
            .RunBodyAsync(async () =>
            {
                _ = state.Start();
                Phase7Sessions.Turn first = await state
                    .FixedAsync(
                        next => state.Sessions.NextTurnAsync("worker1", next),
                        token
                    )
                    .ConfigureAwait(false);
                long batch = state.SendingBatch("worker1");
                string cold = state.Reservation("worker2").Id;
                Assert.AreEqual(0m, state.Reservation("worker2").Allowance);
                Assert.IsFalse(state.Reservation("worker2").GrantOpen);
                Assert.IsNull(state.Database.ReadMetadata("worker:worker2:session"));
                long leases = state.Number(
                    "SELECT COUNT(*) FROM process_leases WHERE kind LIKE 'native:%';"
                );
                await state.Clock.WaitForPollAsync(token, state.WorkDeadline).ConfigureAwait(false);
                state.Clock.Advance(state.WorkDeadline - state.Clock.GetUtcNow());
                await state
                    .UntilAsync(
                        () => state.Database.ReadMetadata("lifecycle") == "draining",
                        state.DrainDeadline,
                        token
                    )
                    .ConfigureAwait(false);
                first.Complete();
                await state.WaitAckAsync(batch, state.DrainDeadline, token).ConfigureAwait(false);
                await state.Active.WaitAsync(token).ConfigureAwait(false);
                GitScenarioState.Data(await state.Active.ConfigureAwait(false));
                Assert.AreEqual("deadline", state.Database.ReadMetadata("lifecycle:stop_reason"));
                Assert.AreEqual(10m, state.Reservation("worker1").Actual);
                Assert.AreEqual(0m, state.Reservation("worker2").Allowance);
                Assert.IsFalse(state.Reservation("worker2").GrantOpen);
                Assert.AreEqual(0L, state.Dispatches(cold));
                Assert.IsNull(state.Database.ReadMetadata("worker:worker2:session"));
                Assert.IsEmpty(
                    state
                        .Sessions.Hosts.Where(host => host.Operation == "run")
                        .SelectMany(host => host.Sessions)
                        .Where(session => session.Worker == "worker2")
                );
                Assert.AreEqual(
                    leases,
                    state.Number("SELECT COUNT(*) FROM process_leases WHERE kind LIKE 'native:%';")
                );
                Assert.AreEqual(1L, state.Number("SELECT COUNT(*) FROM delivery_batches;"));
            })
            .ConfigureAwait(false);
    }

    [TestMethod]
    [TestCategory("RampAdmissionCadence")]
    [Timeout(120_000, CooperativeCancellation = true)]
    public async Task SettledFreshResumeStartsNewCadenceWithoutInheritingPriorEligibility()
    {
        CancellationToken token = testContext.CancellationToken;
        Phase7Run state = await Phase7Run.CreateAsync(token, interval: 20).ConfigureAwait(false);
        await state
            .RunBodyAsync(async () =>
            {
                _ = state.Start();
                Phase7Sessions.Turn first = await state
                    .FixedAsync(
                        next => state.Sessions.NextTurnAsync("worker1", next),
                        token
                    )
                    .ConfigureAwait(false);
                Assert.AreEqual(100m, state.Reservation("worker1").Allowance);
                // fair legacy reservation, NOT a shared grant
                Assert.AreEqual(100m, state.Reservation("worker2").Allowance);
                long batch = state.SendingBatch("worker1");
                first.Complete();
                await state.WaitAckAsync(batch, state.WorkDeadline, token).ConfigureAwait(false);
                GitScenarioState.Data(
                    await state
                        .StopRunningAsync("pause", state.WorkDeadline, token)
                        .ConfigureAwait(false)
                );
                Assert.AreEqual(
                    0L,
                    state.Number(
                        "SELECT COUNT(*) FROM coordinator_segments WHERE ended_at IS NULL;"
                    )
                );
                Assert.AreEqual("reconciled", state.Reservation("worker1").State);
                Assert.IsNull(state.Database.ReadMetadata("worker:worker2:session"));
                Assert.AreEqual(
                    17.25m,
                    state.Ledger.Status(state.Database.RunId, 207.25m)[
                        "runCommitted"
                    ]!.GetValue<decimal>()
                );
                // Real settled paused time, not an edited segment or reset scheduler.
                state.Clock.Advance(TimeSpan.FromSeconds(100));
                DateTimeOffset resumedAt = state.Clock.GetUtcNow();
                TaskCompletionSource entered = new(
                    TaskCreationOptions.RunContinuationsAsynchronously
                );
                TaskCompletionSource release = new(
                    TaskCreationOptions.RunContinuationsAsynchronously
                );
                state.Sessions.BeforeOpen = async (session, business) =>
                {
                    if (session.Operation == "resume" && session.Worker == "worker1")
                    {
                        entered.TrySetResult();
                        await release.Task.WaitAsync(business).ConfigureAwait(false);
                    }
                };
                Phase7Sessions.Turn resumed;
                try
                {
                    _ = state.Start("resume");
                    Phase7Handoff.Startup startup = await state
                        .FixedAsync(
                            next => state.Handoff!.FirstStarting.Task.WaitAsync(next), token
                        )
                        .ConfigureAwait(false);
                    await state.FixedAsync(entered.Task, token).ConfigureAwait(false);
                    await state.Clock
                        .WaitForPollAsync(token, resumedAt.AddSeconds(1))
                        .ConfigureAwait(false);
                    release.TrySetResult();
                    await state.FixedAsync(startup.Operation, token).ConfigureAwait(false);
                    resumed = await state
                        .FixedAsync(next => state.Sessions.NextTurnAsync("worker1", next), token)
                        .ConfigureAwait(false);
                }
                finally
                {
                    release.TrySetResult();
                }
                DateTimeOffset admitted = state.Clock.GetUtcNow();
                Assert.AreEqual(resumedAt, admitted);
                Assert.IsLessThan(resumedAt.AddSeconds(1), admitted);
                Assert.AreNotEqual(first.Session.SessionId, resumed.Session.SessionId);
                Assert.AreEqual(
                    "fresh-session-with-durable-summary",
                    state.Database.ReadMetadata("worker:worker1:recovery")
                );
                Assert.AreEqual(95m, state.Reservation("worker1").Allowance);
                Assert.AreEqual(95m, state.Reservation("worker2").Allowance);
                Assert.AreEqual(2L, state.Number("SELECT COUNT(*) FROM coordinator_segments;"));
                Assert.AreEqual(
                    1L,
                    state.Number(
                        "SELECT COUNT(*) FROM coordinator_segments WHERE ended_at IS NULL;"
                    )
                );
                Assert.IsEmpty(
                    state
                        .Sessions.Hosts.Where(host => host.Operation == "resume")
                        .SelectMany(host => host.Sessions)
                        .Where(session => session.Worker == "worker2")
                );
                long resumedBatch = state.SendingBatch("worker1");
                resumed.Complete();
                await state
                    .WaitAckAsync(resumedBatch, admitted.AddSeconds(19), token)
                    .ConfigureAwait(false);
                await state
                    .Clock.WaitForPollAsync(token, admitted.AddSeconds(20))
                    .ConfigureAwait(false);
                state.Clock.Advance(admitted.AddSeconds(19) - state.Clock.GetUtcNow());
                await state
                    .Clock.WaitForPollAsync(token, admitted.AddSeconds(20))
                    .ConfigureAwait(false);
                Assert.IsEmpty(
                    state
                        .Sessions.Hosts.Where(host => host.Operation == "resume")
                        .SelectMany(host => host.Sessions)
                        .Where(session => session.Worker == "worker2")
                );
                await state
                    .UntilAsync(state.Handoff!.SecondStarting.Task, state.WorkDeadline, token)
                    .ConfigureAwait(false);
                Phase7Handoff.Startup peerStart = await state.Handoff.SecondStarting.Task
                    .WaitAsync(token).ConfigureAwait(false);
                await state.FixedAsync(peerStart.Operation, token).ConfigureAwait(false);
                Phase7Sessions.Turn peer = await state
                    .FixedAsync(next => state.Sessions.NextTurnAsync("worker2", next), token)
                    .ConfigureAwait(false);
                Assert.IsGreaterThanOrEqualTo(admitted.AddSeconds(20), state.Clock.GetUtcNow());
                Assert.AreEqual(95m, state.Reservation("worker2").Allowance);
                long peerBatch = state.SendingBatch("worker2");
                peer.Complete();
                await state
                    .WaitAckAsync(peerBatch, state.WorkDeadline, token)
                    .ConfigureAwait(false);
                GitScenarioState.Data(
                    await state
                        .StopRunningAsync("pause", state.WorkDeadline, token)
                        .ConfigureAwait(false)
                );
                Assert.AreEqual(
                    37.25m,
                    state.Ledger.Status(state.Database.RunId, 207.25m)[
                        "runCommitted"
                    ]!.GetValue<decimal>()
                );
            })
            .ConfigureAwait(false);
    }

    [TestMethod]
    [TestCategory("RampAdmissionCadence")]
    [Timeout(120_000, CooperativeCancellation = true)]
    public async Task DelayedAdmissionStartsNextIntervalAtCompletionWithoutCatchupBurst()
    {
        CancellationToken token = testContext.CancellationToken;
        Phase7Run state = await Phase7Run
            .CreateAsync(token, workers: 3, interval: 10, allocation: "shared")
            .ConfigureAwait(false);
        await state
            .RunBodyAsync(async () =>
            {
                using Phase7Handoff handoff = new();
                state.Handoff = handoff;
                TaskCompletionSource firstEntered = new(
                    TaskCreationOptions.RunContinuationsAsynchronously
                );
                TaskCompletionSource secondEntered = new(
                    TaskCreationOptions.RunContinuationsAsynchronously
                );
                TaskCompletionSource firstRelease = new(
                    TaskCreationOptions.RunContinuationsAsynchronously
                );
                TaskCompletionSource secondRelease = new(
                    TaskCreationOptions.RunContinuationsAsynchronously
                );
                Dictionary<string, JsonObject> entries = [];
                string[] workerNames = ["worker1", "worker2", "worker3"];
                try
                {
                    state.Sessions.BeforeOpen = async (session, business) =>
                    {
                        Phase7Sessions.Host host = state.Sessions.Hosts.Single(item =>
                            item.Sessions.Contains(session)
                        );
                        entries[session.Worker] = new()
                        {
                            ["enteredAt"] = state.Clock.GetUtcNow(),
                            ["sessionId"] = session.SessionId,
                            ["runtimeId"] = host.Id,
                            ["leaseId"] = host.LeaseId,
                            ["pid"] = host.Ready!.Pid,
                            ["birthTicks"] = host.Ready.BirthTicks,
                            ["reservationId"] = state.Reservation(session.Worker).Id,
                            ["backedAllowance"] = state.Reservation(session.Worker).Allowance,
                        };
                        if (session.Worker == "worker1")
                        {
                            firstEntered.TrySetResult();
                            await firstRelease.Task.WaitAsync(business).ConfigureAwait(false);
                        }
                        else if (session.Worker == "worker2")
                        {
                            secondEntered.TrySetResult();
                            await secondRelease.Task.WaitAsync(business).ConfigureAwait(false);
                        }
                    };
                    _ = state.Start();
                    Phase7Handoff.Startup firstStart = await state
                        .FixedAsync(next => handoff.FirstStarting.Task.WaitAsync(next), token)
                        .ConfigureAwait(false);
                    await HeldAsync(firstStart, firstEntered.Task).ConfigureAwait(false);
                    await state
                        .Clock.WaitForPollAsync(token, state.WorkDeadline)
                        .ConfigureAwait(false);
                    state.Clock.Advance(TimeSpan.FromSeconds(25));
                    Cold("worker2", handoff.SecondStarting.Task);
                    DateTimeOffset firstReleasedAt = state.Clock.GetUtcNow();
                    firstRelease.TrySetResult();
                    var first = await ConsumeAsync(firstStart).ConfigureAwait(false);
                    Assert.AreEqual(firstReleasedAt, first.CompletedAt);
                    first.Turn.Complete(); // ACK joins belong AFTER temporal probes.
                    Probe("worker2", handoff.SecondStarting.Task, first.CompletedAt);

                    await state.UntilAsync(handoff.SecondStarting.Task, state.WorkDeadline, token)
                        .ConfigureAwait(false);
                    Phase7Handoff.Startup secondStart = await handoff.SecondStarting.Task
                        .WaitAsync(token).ConfigureAwait(false);
                    await HeldAsync(secondStart, secondEntered.Task).ConfigureAwait(false);
                    Assert.IsGreaterThanOrEqualTo(
                        first.CompletedAt.AddSeconds(10),
                        entries["worker2"]["enteredAt"]!.GetValue<DateTimeOffset>()
                    );
                    Cold("worker3", handoff.ThirdStarting.Task);
                    DateTimeOffset secondHeldAt = state.Clock.GetUtcNow();
                    await state
                        .Clock.WaitForPollAsync(token, state.WorkDeadline)
                        .ConfigureAwait(false);
                    state.Clock.Advance(TimeSpan.FromSeconds(50));
                    Assert.IsFalse(secondStart.Operation.IsCompleted);
                    Cold("worker3", handoff.ThirdStarting.Task);
                    DateTimeOffset secondReleasedAt = state.Clock.GetUtcNow();
                    Assert.AreEqual(TimeSpan.FromSeconds(50), secondReleasedAt - secondHeldAt);
                    secondRelease.TrySetResult();
                    var second = await ConsumeAsync(secondStart).ConfigureAwait(false);
                    Assert.AreEqual(secondReleasedAt, second.CompletedAt);
                    second.Turn.Complete();
                    Probe("worker3", handoff.ThirdStarting.Task, second.CompletedAt);

                    await state.UntilAsync(handoff.ThirdStarting.Task, state.WorkDeadline, token)
                        .ConfigureAwait(false);
                    Phase7Handoff.Startup thirdStart = await handoff.ThirdStarting.Task
                        .WaitAsync(token).ConfigureAwait(false);
                    var third = await ConsumeAsync(thirdStart).ConfigureAwait(false);
                    Assert.IsGreaterThanOrEqualTo(
                        second.CompletedAt.AddSeconds(10),
                        entries["worker3"]["enteredAt"]!.GetValue<DateTimeOffset>()
                    );
                    Assert.HasCount(
                        3,
                        new[] { first.Turn, second.Turn, third.Turn }
                            .Select(turn => turn.Session.SessionId)
                            .Distinct()
                    );
                    Assert.HasCount(3, entries);
                    Assert.HasCount(
                        3,
                        entries
                            .Values.Select(entry => entry["reservationId"]!.GetValue<string>())
                            .Distinct()
                    );
                    Phase7Sessions.Turn[] turns = [first.Turn, second.Turn, third.Turn];
                    Phase7Sessions.Host runHost = Assert.ContainsSingle(
                        state.Sessions.Hosts.Where(host =>
                            host.Operation == "run"
                            && turns.All(turn => host.Sessions.Contains(turn.Session))
                        )
                    );
                    Assert.IsNotNull(runHost.Ready);
                    Assert.AreNotEqual(Environment.ProcessId, runHost.Ready.Pid);
                    Assert.IsEmpty(
                        state
                            .Sessions.Hosts.Where(host => host.Operation != "run")
                            .SelectMany(host => host.Sessions)
                            .Intersect(turns.Select(turn => turn.Session))
                    );
                    Assert.AreEqual(
                        1L,
                        state.Number(
                            (
                                "SELECT COUNT(*) FROM process_leases WHERE id=$lease AND "
                                + "kind=$kind AND pid=$pid AND start_ticks=$birth AND "
                                + "state='running';"
                            ),
                            ("$lease", runHost.LeaseId),
                            ("$kind", "native:runtime:" + runHost.Id),
                            ("$pid", runHost.Ready.Pid),
                            ("$birth", runHost.Ready.BirthTicks)
                        )
                    );
                    foreach (string worker in workerNames)
                    {
                        Assert.Contains(worker, entries.Keys);
                        Phase7Sessions.Turn turn = turns.Single(item =>
                            item.Session.Worker == worker
                        );
                        string sessionId = turn.Session.SessionId;
                        CreditReservation reservation = state.Reservation(worker);
                        JsonObject diagnostic = JsonNode
                            .Parse(state.Database.ReadMetadata("native:session:" + sessionId)!)!
                            .AsObject();
                        Assert.AreEqual(sessionId, reservation.SessionId);
                        Assert.AreEqual(
                            sessionId,
                            state.Database.ReadMetadata($"worker:{worker}:session")
                        );
                        Assert.AreEqual(
                            reservation.Id,
                            state.Database.ReadMetadata($"worker:{worker}:reservation")
                        );
                        Assert.AreEqual(
                            1L,
                            state.Number(
                                (
                                    "SELECT COUNT(*) FROM native_sessions s JOIN "
                                    + "native_attempts a\nON a.reservation_id=s.reservation_id "
                                    + "AND a.worker=s.worker AND a.session_id=s.session_id\nWHERE "
                                    + "s.worker=$worker AND s.reservation_id=$reservation AND "
                                    + "s.session_id=$session\nAND a.usage_path=$path;"
                                ),
                                ("$worker", worker),
                                ("$reservation", reservation.Id),
                                ("$session", sessionId),
                                ("$path", reservation.UsagePath)
                            )
                        );
                        Assert.AreEqual(sessionId, diagnostic["sessionId"]!.GetValue<string>());
                        Assert.AreEqual(
                            state.WorkerPath(worker),
                            diagnostic["workingDirectory"]!.GetValue<string>()
                        );
                        Assert.AreEqual(
                            state.WorkerPath(worker),
                            turn.Session.Metadata!["workingDirectory"]!.GetValue<string>()
                        );
                        Assert.IsTrue(diagnostic["paidReservation"]!.GetValue<bool>());
                        foreach (
                            JsonObject metadata in new[] { turn.Session.Metadata!, diagnostic }
                        )
                        {
                            Assert.AreEqual(runHost.Id, metadata["runtimeId"]!.GetValue<string>());
                            Assert.AreEqual(
                                runHost.LeaseId,
                                metadata["runtimeLeaseId"]!.GetValue<long>()
                            );
                            Assert.AreEqual(runHost.Ready.Pid, metadata["pid"]!.GetValue<int>());
                            Assert.AreEqual(
                                runHost.Ready.BirthTicks,
                                metadata["birthTicks"]!.GetValue<long>()
                            );
                            Assert.AreEqual(
                                (double)reservation.Allowance,
                                metadata["appliedLimit"]!.GetValue<double>()
                            );
                        }
                    }
                    Console.WriteLine(
                        "PHASE7_DELAYED_COHORT "
                            + new JsonObject
                            {
                                ["operation"] = runHost.Operation,
                                ["runtimeId"] = runHost.Id,
                                ["leaseId"] = runHost.LeaseId,
                                ["backendPid"] = runHost.Ready.Pid,
                                ["backendBirthTicks"] = runHost.Ready.BirthTicks,
                                ["coordinatorPid"] = Environment.ProcessId,
                                ["sessions"] = new JsonArray(
                                    turns
                                        .Select(turn => JsonValue.Create(turn.Session.SessionId))
                                        .ToArray()
                                ),
                                ["durableBackendLeaseAndAllSessionAssociationsMatched"] = true,
                            }.ToJsonString()
                    );
                    third.Turn.Complete();
                    foreach (long batch in new[] { first.Batch, second.Batch, third.Batch })
                    {
                        await state
                            .WaitAckAsync(batch, state.WorkDeadline, token)
                            .ConfigureAwait(false);
                        await state
                            .WaitDurableAsync(
                                () =>
                                    (string?)
                                        state.Value(
                                            (
                                                "SELECT state FROM coordinator_turns WHERE "
                                                + "batch_id=$id;"
                                            ),
                                            ("$id", batch)
                                        ) == "acknowledged",
                                state.WorkDeadline,
                                token
                            )
                            .ConfigureAwait(false);
                        Assert.AreEqual(
                            "sent",
                            state.Value(
                                "SELECT state FROM delivery_batches WHERE id=$id;",
                                ("$id", batch)
                            )
                        );
                        Assert.AreEqual(
                            "acknowledged",
                            state.Value(
                                "SELECT state FROM coordinator_turns WHERE batch_id=$id;",
                                ("$id", batch)
                            )
                        );
                    }
                    await state
                        .WaitDurableAsync(
                            () =>
                                workerNames.All(worker =>
                                    state.Reservation(worker).Actual == 10m
                                    && !state.Reservation(worker).GrantOpen
                                ),
                            state.WorkDeadline,
                            token
                        )
                        .ConfigureAwait(false);
                    GitScenarioState.Data(
                        await state
                            .StopRunningAsync("pause", state.WorkDeadline, token)
                            .ConfigureAwait(false)
                    );
                    Assert.AreEqual(
                        "owner-pause",
                        state.Database.ReadMetadata("lifecycle:stop_reason")
                    );
                    Assert.AreEqual("paused", state.Database.ReadMetadata("lifecycle"));
                    foreach (string worker in workerNames)
                    {
                        Assert.AreEqual(10m, state.Reservation(worker).Actual);
                        Assert.AreEqual("reconciled", state.Reservation(worker).State);
                        Assert.IsFalse(state.Reservation(worker).GrantOpen);
                        Assert.AreEqual(1L, state.Dispatches(state.Reservation(worker).Id));
                    }
                    Assert.AreEqual(
                        37.25m,
                        state.Ledger.Status(state.Database.RunId, 207.25m)[
                            "runCommitted"
                        ]!.GetValue<decimal>()
                    );
                    Console.WriteLine(
                        "PHASE7_DELAYED_FINAL "
                            + new JsonObject
                            {
                                ["firstCompletedAt"] = first.CompletedAt,
                                ["secondHeldAt"] = secondHeldAt,
                                ["secondCompletedAt"] = second.CompletedAt,
                                ["secondHeldJumpSeconds"] = (
                                    secondReleasedAt - secondHeldAt
                                ).TotalSeconds,
                                ["batches"] = new JsonArray(first.Batch, second.Batch, third.Batch),
                                ["entries"] = new JsonObject(
                                    entries.Select(pair => new KeyValuePair<string, JsonNode?>(
                                        pair.Key,
                                        pair.Value.DeepClone()
                                    ))
                                ),
                                ["committed"] = 37.25m,
                                ["publicPauseSucceeded"] = true,
                            }.ToJsonString()
                    );
                }
                finally
                {
                    // Only fixture-owned Open gates, BEFORE primary-aware run cleanup.
                    firstRelease.TrySetResult();
                    secondRelease.TrySetResult();
                }

                async Task HeldAsync(Phase7Handoff.Startup startup, Task entered)
                {
                    await state
                        .FixedAsync(
                            async next =>
                            {
                                await Task.WhenAny(entered, startup.Operation)
                                    .WaitAsync(next)
                                    .ConfigureAwait(false);
                                if (startup.Operation.IsCompleted)
                                {
                                    await startup.Operation.ConfigureAwait(false);
                                }
                                await entered.WaitAsync(next).ConfigureAwait(false);
                                return true;
                            },
                            token
                        )
                        .ConfigureAwait(false);
                    Assert.IsFalse(startup.Operation.IsCompleted);
                }

                async Task<(
                    Phase7Sessions.Turn Turn,
                    long Batch,
                    DateTimeOffset CompletedAt
                )> ConsumeAsync(Phase7Handoff.Startup startup)
                {
                    Assert.AreEqual(state.Reservation(startup.Worker).Id, startup.ReservationId);
                    Assert.AreEqual(state.WorkerPath(startup.Worker), startup.Cwd);
                    DateTimeOffset fixedAt = state.Clock.GetUtcNow();
                    await state.FixedAsync(startup.Operation, token).ConfigureAwait(false);
                    Assert.AreEqual(fixedAt, state.Clock.GetUtcNow());
                    // For the two HELD operations this is their genuine completion
                    // phase. Consumption is a separate, later durable milestone.
                    await state
                        .WaitDurableAsync(
                            () =>
                                state.Number(
                                    (
                                        "SELECT COUNT(*) FROM delivery_batches WHERE "
                                        + "recipient=$worker AND state='sending';"
                                    ),
                                    ("$worker", startup.Worker)
                                ) == 1,
                            startup.Worker == "worker3"
                                ? state.WorkDeadline
                                : fixedAt.AddSeconds(9),
                            token
                        )
                        .ConfigureAwait(false);
                    long batch = state.SendingBatch(startup.Worker);
                    DateTimeOffset consumed = DateTimeOffset.FromUnixTimeMilliseconds(
                        state.Number(
                            "SELECT created_at FROM delivery_batches WHERE id=$id;",
                            ("$id", batch)
                        )
                    );
                    DateTimeOffset claimObservedAt = state.Clock.GetUtcNow();
                    Phase7Sessions.Turn turn = await state
                        .FixedAsync(
                            next => state.Sessions.NextTurnAsync(startup.Worker, next),
                            token
                        )
                        .ConfigureAwait(false);
                    Assert.AreEqual(claimObservedAt, state.Clock.GetUtcNow());
                    JsonObject entry = entries[startup.Worker];
                    Assert.AreEqual(entry["sessionId"]!.GetValue<string>(), turn.Session.SessionId);
                    Assert.AreEqual(
                        entry["runtimeId"]!.GetValue<string>(),
                        turn.Session.Metadata!["runtimeId"]!.GetValue<string>()
                    );
                    Assert.AreEqual(
                        entry["leaseId"]!.GetValue<long>(),
                        turn.Session.Metadata["runtimeLeaseId"]!.GetValue<long>()
                    );
                    Assert.AreEqual(
                        entry["pid"]!.GetValue<int>(),
                        turn.Session.Metadata["pid"]!.GetValue<int>()
                    );
                    Assert.AreEqual(
                        entry["birthTicks"]!.GetValue<long>(),
                        turn.Session.Metadata["birthTicks"]!.GetValue<long>()
                    );
                    Assert.AreEqual(
                        startup.Cwd,
                        turn.Session.Metadata["workingDirectory"]!.GetValue<string>()
                    );
                    Assert.AreEqual(
                        startup.ReservationId,
                        entry["reservationId"]!.GetValue<string>()
                    );
                    Assert.AreEqual(
                        turn.Session.SessionId,
                        state.Reservation(startup.Worker).SessionId
                    );
                    decimal backed = entry["backedAllowance"]!.GetValue<decimal>();
                    Assert.IsGreaterThanOrEqualTo(30m, backed);
                    Assert.IsLessThanOrEqualTo(100m, backed);
                    Assert.AreEqual(backed, state.Reservation(startup.Worker).Allowance);
                    Assert.AreEqual(
                        (double)backed,
                        turn.Session.Metadata["appliedLimit"]!.GetValue<double>()
                    );
                    if (startup.Worker != "worker3")
                    {
                        Assert.AreEqual(100m, backed);
                        Assert.IsGreaterThanOrEqualTo(fixedAt, consumed);
                    }
                    Assert.IsTrue(state.Reservation(startup.Worker).GrantOpen);
                    Assert.AreEqual(TimeSpan.FromSeconds(1800), turn.Timeout);
                    Assert.IsFalse(turn.Response.IsCompleted);
                    Console.WriteLine(
                        "PHASE7_DELAYED_CONSUMED "
                            + new JsonObject
                            {
                                ["worker"] = startup.Worker,
                                ["startupJoinedAtFixedPhase"] = fixedAt,
                                ["startupTaskStatus"] = startup.Operation.Status.ToString(),
                                ["consumedSendingBatch"] = batch,
                                ["batchCreatedAt"] = consumed,
                                ["claimObservedAt"] = claimObservedAt,
                                ["turnObservedAt"] = state.Clock.GetUtcNow(),
                                ["entry"] = entry.DeepClone(),
                                ["cap"] = backed,
                            }.ToJsonString()
                    );
                    return (turn, batch, fixedAt);
                }

                void Cold(string worker, Task starting)
                {
                    Assert.IsFalse(starting.IsCompleted);
                    Assert.IsFalse(entries.ContainsKey(worker));
                    Assert.IsNull(state.Database.ReadMetadata($"worker:{worker}:session"));
                    Assert.AreEqual(0m, state.Reservation(worker).Allowance);
                    Assert.IsFalse(state.Reservation(worker).GrantOpen);
                    Assert.AreEqual(0L, state.Dispatches(state.Reservation(worker).Id));
                    Assert.IsEmpty(
                        state
                            .Sessions.Hosts.Where(host => host.Operation == "run")
                            .SelectMany(host => host.Sessions)
                            .Where(session => session.Worker == worker)
                    );
                }

                void Probe(string worker, Task starting, DateTimeOffset completedAt)
                {
                    DateTimeOffset eligible = completedAt.AddSeconds(10);
                    DateTimeOffset probe = eligible.AddSeconds(-1);
                    Cold(worker, starting);
                    Assert.IsFalse(state.Active.IsCompleted);
                    Assert.IsLessThanOrEqualTo(
                        probe,
                        state.Clock.GetUtcNow(),
                        "Required pre-eligibility probe was missed."
                    );
                    // Explicit scenario movement needs NO future timer < probe.
                    // A registration AT probe is valid and fires on this advance.
                    state.Clock.Advance(probe - state.Clock.GetUtcNow());
                    Cold(worker, starting);
                    Assert.IsLessThan(eligible, state.Clock.GetUtcNow());
                    Console.WriteLine(
                        "PHASE7_DELAYED_COLD "
                            + new JsonObject
                            {
                                ["worker"] = worker,
                                ["actualCompletedStartupAt"] = completedAt,
                                ["eligibilityLowerBound"] = eligible,
                                ["explicitProbeAt"] = probe,
                                ["observedAt"] = state.Clock.GetUtcNow(),
                                ["allowance"] = state.Reservation(worker).Allowance,
                                ["grantOpen"] = state.Reservation(worker).GrantOpen,
                            }.ToJsonString()
                    );
                    Assert.IsFalse(state.Active.IsCompleted);
                    Assert.IsFalse(starting.IsCompleted);
                    state.Clock.Advance(eligible - state.Clock.GetUtcNow());
                }
            })
            .ConfigureAwait(false);
    }
}
