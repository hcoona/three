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
        CoordinatorScenarioRun state = await CoordinatorScenarioRun
            .CreateAsync(
                testContext, token, workers: 1, hostRole: OwnedChildControl.ControlledNativeHostRole
            )
            .ConfigureAwait(false);
        CancellationTokenRegistration registration = default;
        Exception? primary = null;
        ExceptionDispatchInfo? teardownFailure = null;
        List<Exception> teardownErrors = [];
        try
        {
            await state
                .RunBodyAsync(async () =>
                {
                    Coordinator inspector = state.Coordinator("status");
                    TaskCompletionSource<(
                        ControlledSessionPool.Session Session,
                        CancellationToken Token
                    )> entered = new(TaskCreationOptions.RunContinuationsAsynchronously);
                    TaskCompletionSource<DateTimeOffset> cancelled = new(
                        TaskCreationOptions.RunContinuationsAsynchronously
                    );
                    TaskCompletionSource release = new(
                        TaskCreationOptions.RunContinuationsAsynchronously
                    );
                    Task<JsonObject>? active = null;
                    CancellationTokenSource? observation = null;
                    Task? drive = null;
                    try
                    {
                        state.Sessions.BeforeOpen = async (session, business) =>
                        {
                            entered.TrySetResult((session, business));
                            await release.Task.WaitAsync(business).ConfigureAwait(false);
                        };
                        active = state.Start();
                        (ControlledSessionPool.Session session, CancellationToken business) =
                            await state
                            .FixedAsync(next => entered.Task.WaitAsync(next), token)
                            .ConfigureAwait(false);
                        Assert.IsTrue(business.CanBeCanceled);
                        Assert.IsFalse(business.IsCancellationRequested);
                        Assert.IsFalse(session.HasSession);
                        Assert.AreEqual(0L, state.Dispatches(state.Reservation("worker1").Id));

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
                        TimeSpan observationWindow = TimeSpan.FromSeconds(5);
                        observation = CancellationTokenSource.CreateLinkedTokenSource(token);
                        drive = DriveOwnerPollingAsync(state, cancelled.Task, observation.Token);
                        try
                        {
                            // Observation expiry does not cancel the caller or business work.
                            await drive.WaitAsync(observationWindow, token).ConfigureAwait(false);
                        }
                        catch (TimeoutException)
                        {
                            Assert.Fail("P7-F01_RED_OWNER_CANCELLATION_NOT_OBSERVED: " + action);
                        }
                        Assert.IsTrue(business.IsCancellationRequested);
                        Assert.IsFalse(token.IsCancellationRequested);
                        Assert.IsFalse(release.Task.IsCompleted);
                        Assert.IsLessThan(
                            state.Clock.WorkDeadline!.Value,
                            await cancelled.Task.ConfigureAwait(false)
                        );
                        JsonObject result = await active
                            .WaitAsync(TimeSpan.FromSeconds(5), token)
                            .ConfigureAwait(false);
                        Assert.AreEqual(
                            "ownership_or_usage_unresolved",
                            result["code"]!.GetValue<string>()
                        );
                        JsonObject status = StatusData(inspector);
                        Assert.AreEqual("unresolved", status["lifecycle"]!.GetValue<string>());
                        Assert.AreEqual(reason, status["stopReason"]!.GetValue<string>());
                        Assert.IsFalse(status["active"]!.GetValue<bool>());
                        // Missing Open/usage evidence cannot acknowledge quiescent
                        // success, even though the common owned-host join completed.
                        JsonObject retainedControl = GitScenarioState.Data(
                            state.Coordinator().RequestControl(action)
                        );
                        Assert.AreEqual(
                            requestId,
                            retainedControl["requestId"]!.GetValue<long>()
                        );
                        Assert.AreEqual("pending", retainedControl["state"]!.GetValue<string>());
                        Assert.AreEqual("uncertain", state.Reservation("worker1").State);
                        Assert.IsNull(state.Reservation("worker1").Actual);
                        Assert.IsGreaterThan(0m, state.Reservation("worker1").Allowance);
                        Assert.AreEqual(0L, state.Dispatches(state.Reservation("worker1").Id));
                        Assert.IsFalse(session.HasSession);
                        ControlledSessionPool.Host host = state.Sessions.Hosts.Single(item =>
                            item.Operation == "run"
                        );
                        Assert.IsNotNull(host.Ready);
                        Assert.IsGreaterThan(0, host.Ready.Pid);
                        Assert.IsGreaterThan(0L, host.Ready.BirthTicks);
                        Assert.IsTrue(
                            host.Witness["normalExitBeforeWrapperDispose"]!.GetValue<bool>()
                        );
                    }
                    catch (Exception error)
                    {
                        primary = error;
                        throw;
                    }
                    finally
                    {
                        // The fixture owns this release even if Open readiness fails.
                        // It must precede the joins and is never owner-stop evidence.
                        release.TrySetResult();
                        try
                        {
                            observation?.Cancel();
                        }
                        catch (Exception secondary)
                        {
                            teardownErrors.Add(secondary);
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
                                teardownErrors.Add(secondary);
                            }
                        }
                        if (drive is not null && active is { IsCompleted: false })
                        {
                            try
                            {
                                _ = await active
                                    .WaitAsync(
                                        TimeSpan.FromSeconds(5),
                                        CancellationToken.None
                                    )
                                    .ConfigureAwait(false);
                            }
                            catch (Exception secondary)
                            {
                                teardownErrors.Add(secondary);
                            }
                        }
                        try
                        {
                            observation?.Dispose();
                        }
                        catch (Exception secondary)
                        {
                            teardownErrors.Add(secondary);
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
                teardownErrors.Add(secondary);
                if (primary is null)
                {
                    teardownFailure = ExceptionDispatchInfo.Capture(secondary);
                }
            }
        }
        teardownFailure?.Throw();
    }

    private static async Task DriveOwnerPollingAsync(
        CoordinatorScenarioRun state,
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
    [Timeout(5_000, CooperativeCancellation = true)]
    public async Task ActualTimerReadinessRevalidatesRegistrationStateAndObservationLifetime()
    {
        CancellationToken token = testContext.CancellationToken;
        ObservedManualClock clock = new();
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
        ObservedManualClock clock = new();
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
        ObservedManualClock clock = new();
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
        ManualScenarioClock settlementClock = new();
        CoordinatorScenarioRun state = await CoordinatorScenarioRun
            .CreateAsync(
                testContext,
                token,
                workers: 1,
                interval: 1,
                duration: 10,
                allocation: "shared",
                ownedHosts: false
            )
            .ConfigureAwait(false);
        state.SettlementTimeProvider = settlementClock;
        await state
            .RunBodyAsync(async () =>
            {
                Coordinator inspector = state.Coordinator("status");
                TaskCompletionSource<ControlledSessionPool.Session> entered = new(
                    TaskCreationOptions.RunContinuationsAsynchronously
                );
                TaskCompletionSource<CancellationToken> settlementRead = new(
                    TaskCreationOptions.RunContinuationsAsynchronously
                );
                TaskCompletionSource release = new(
                    TaskCreationOptions.RunContinuationsAsynchronously
                );
                async Task Hold(ControlledSessionPool.Session session, CancellationToken business)
                {
                    if (state.Clock.GetUtcNow() >= state.WorkDeadline)
                    {
                        settlementRead.TrySetResult(business);
                    }
                    entered.TrySetResult(session);
                    await release.Task.WaitAsync(business).ConfigureAwait(false);
                }
                try
                {
                    if (boundary == "open")
                    {
                        state.Sessions.BeforeOpen = Hold;
                    }
                    else
                    {
                        state.Sessions.BeforeRead = (session, business) =>
                            WorkerStatus(inspector, session.Worker)[
                                "lifecycle"
                            ]!.GetValue<string>() == "ready"
                            && session.CompletedTurns == 0
                                ? Hold(session, business)
                                : Task.CompletedTask;
                    }
                    _ = state.Start();
                    ControlledSessionPool.Session waiting = await state
                        .FixedAsync(next => entered.Task.WaitAsync(next), token)
                        .ConfigureAwait(false);
                    await state
                        .FixedAsync(
                            next => state.Clock.WaitForPollAsync(next, state.WorkDeadline),
                            token
                        )
                        .ConfigureAwait(false);
                    Assert.AreEqual(boundary == "prepare", waiting.HasSession);
                    Assert.AreEqual(0, waiting.CompletedTurns);
                    CreditReservation waitingReservation = state.Reservation(waiting.Worker);
                    Assert.AreEqual(waiting.SessionId, waitingReservation.SessionId);
                    Assert.AreEqual(0L, state.Dispatches(waitingReservation.Id));
                    state.Clock.Advance(TimeSpan.FromSeconds(10));
                    if (boundary == "prepare")
                    {
                        CancellationToken settlement = await state
                            .FixedAsync(next => settlementRead.Task.WaitAsync(next), token)
                            .ConfigureAwait(false);
                        Assert.IsFalse(settlement.IsCancellationRequested);
                        settlementClock.Advance(
                            TimeSpan.FromSeconds(30) - TimeSpan.FromTicks(1)
                        );
                        Assert.IsFalse(settlement.IsCancellationRequested);
                        settlementClock.Advance(TimeSpan.FromTicks(1));
                        Assert.IsTrue(settlement.IsCancellationRequested);
                    }
                    await state.Active.WaitAsync(token).ConfigureAwait(false);
                    JsonObject result = await state.Active.ConfigureAwait(false);
                    Assert.AreEqual(
                        "ownership_or_usage_unresolved",
                        result["code"]!.GetValue<string>()
                    );
                    JsonObject status = StatusData(inspector);
                    Assert.AreEqual("unresolved", status["lifecycle"]!.GetValue<string>());
                    Assert.AreEqual("credit_unknown", status["stopReason"]!.GetValue<string>());
                    Assert.IsFalse(status["active"]!.GetValue<bool>());
                    CreditReservation stoppedReservation = state.Reservation(waiting.Worker);
                    Assert.AreEqual(waitingReservation.Id, stoppedReservation.Id);
                    Assert.AreEqual(waitingReservation.SessionId, stoppedReservation.SessionId);
                    Assert.AreEqual(0L, state.Dispatches(stoppedReservation.Id));
                    Assert.AreEqual("uncertain", stoppedReservation.State);
                    Assert.IsNull(stoppedReservation.Actual);
                    Assert.AreEqual(0, waiting.CompletedTurns);
                    // This is missing startup/preparation evidence, not measured zero
                    // usage or verified pre-dispatch cancellation.
                    Assert.IsGreaterThan(0m, stoppedReservation.Allowance);
                }
                finally
                {
                    release.TrySetResult();
                }
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
        CoordinatorScenarioRun state = await CoordinatorScenarioRun
            .CreateAsync(testContext, token, workers: 1, duration: 10, ownedHosts: false)
            .ConfigureAwait(false);
        await state
            .RunBodyAsync(async () =>
            {
                Coordinator inspector = state.Coordinator("status");
                _ = state.Start();
                ControlledSessionPool.Turn turn = await state
                    .FixedAsync(next => state.Sessions.NextTurnAsync("worker1", next), token)
                    .ConfigureAwait(false);
                long batch = state.SendingBatch("worker1");
                long dispatches = state.Dispatches(state.Reservation("worker1").Id);
                const string followupBody =
                    "Retain this followup without claiming it during drain.";
                if (duringGrace)
                {
                    _ = state.Invoke(
                        "owner",
                        "message_send",
                        new()
                        {
                            ["recipient"] = "worker1",
                            ["body"] = followupBody,
                        }
                    );
                }
                Assert.AreEqual(TimeSpan.FromSeconds(10), turn.Timeout);
                await state
                    .FixedAsync(
                        next => state.Clock.WaitForPollAsync(next, state.WorkDeadline),
                        token
                    )
                    .ConfigureAwait(false);
                state.Clock.Advance(TimeSpan.FromSeconds(duringGrace ? 10 : 9));
                if (duringGrace)
                {
                    await state
                        .UntilAsync(
                            () =>
                                StatusData(inspector)["lifecycle"]!.GetValue<string>()
                                == "draining",
                            state.DrainDeadline,
                            token
                        )
                        .ConfigureAwait(false);
                    Assert.IsFalse(turn.Token.IsCancellationRequested);
                    await state
                        .FixedAsync(
                            next => state.Clock.WaitForPollAsync(next, state.DrainDeadline),
                            token
                        )
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
                    Assert.IsLessThan(state.WorkDeadline, state.Clock.GetUtcNow());
                    state.Clock.Advance(state.Clock.WorkDeadline!.Value - state.Clock.GetUtcNow());
                    await state.UntilAsync(state.Active, state.DrainDeadline, token)
                        .ConfigureAwait(false);
                }
                await state.Active.WaitAsync(token).ConfigureAwait(false);
                GitScenarioState.Data(await state.Active.ConfigureAwait(false));
                JsonObject status = StatusData(inspector);
                Assert.AreEqual("finalized", status["lifecycle"]!.GetValue<string>());
                Assert.AreEqual(
                    "completed",
                    status["deadlineDrain"]!["outcome"]!.GetValue<string>()
                );
                Assert.AreEqual("deadline", status["stopReason"]!.GetValue<string>());
                Assert.IsFalse(status["active"]!.GetValue<bool>());
                Assert.AreEqual("reconciled", state.Reservation("worker1").State);
                Assert.AreEqual(10m, state.Reservation("worker1").Actual);
                Assert.AreEqual(1, turn.Session.CompletedTurns);
                if (duringGrace)
                {
                    Assert.AreEqual(dispatches, state.Dispatches(state.Reservation("worker1").Id));
                    DeliveryBatch? retained = state.Collaboration.ClaimBatch("worker1", false);
                    Assert.IsNotNull(retained);
                    Assert.AreEqual(1, retained.Attempt);
                    Assert.Contains(followupBody, retained.Prompt);
                }
            })
            .ConfigureAwait(false);
    }

    [TestMethod]
    [TestCategory("InterruptionAndPeerDrainSafety")]
    [Timeout(120_000, CooperativeCancellation = true)]
    [DataRow("owner", "owner-pause")]
    [DataRow("caller", "caller-cancelled")]
    [DataRow("accounting", "credit_unknown")]
    [DataRow("root", "owner-root-closure")]
    [DataRow("failure", "controlled_turn_failure")]
    public async Task EnteredDrainSafetyStopInterruptsHeldTurn(string cause, string reason)
    {
        CancellationToken token = testContext.CancellationToken;
        const string failureMessage = "Controlled terminal failure during deadline drain.";
        CoordinatorScenarioRun state = await CoordinatorScenarioRun.CreateAsync(
            testContext, token, workers: 1, duration: 10, ownedHosts: false
        ).ConfigureAwait(false);
        await state.RunBodyAsync(async () =>
        {
            Coordinator inspector = state.Coordinator("status");
            _ = state.Start();
            ControlledSessionPool.Turn turn = await state
                .FixedAsync(next => state.Sessions.NextTurnAsync("worker1", next), token)
                .ConfigureAwait(false);
            long batch = state.SendingBatch("worker1");
            CreditReservation? accounting = null;
            if (cause == "accounting")
            {
                // Use unused backing from the same campaign, not the held worker's reservation.
                accounting = state.Ledger.Reserve(
                    "drain-accounting-fixture", "fixture-peer", 30m, 30m,
                    state.Git.PathFor("drain-accounting.usage")
                );
                state.Ledger.MarkStarted(accounting.Id);
                state.Ledger.AttachSession(accounting.Id, accounting.SessionId!);
            }
            await state
                .FixedAsync(next => state.Clock.WaitForPollAsync(next, state.WorkDeadline), token)
                .ConfigureAwait(false);
            long beforeDrain = state.Clock.TimerWatermark;
            state.Clock.Advance(state.WorkDeadline - state.Clock.GetUtcNow());
            await state.UntilAsync(
                () => StatusData(inspector)["lifecycle"]!.GetValue<string>() == "draining",
                state.DrainDeadline,
                token
            ).ConfigureAwait(false);
            Assert.IsFalse(turn.Token.IsCancellationRequested);
            long? requestId = null;
            if (accounting is not null || cause is "root" or "failure")
            {
                ObservedManualClock.ObservedPoll poll = await state.FixedAsync(
                    next => state.Clock.WaitForPollAsync(
                        next, state.DrainDeadline, requireSingle: true, registeredAfter: beforeDrain
                    ),
                    token
                ).ConfigureAwait(false);
                Assert.AreEqual(
                    "draining",
                    StatusData(inspector)["deadlineDrain"]!["outcome"]!.GetValue<string>()
                );
                Assert.IsFalse(turn.Token.IsCancellationRequested);
                Assert.IsFalse(turn.Response.IsCompleted);
                CreditReservation held = state.Reservation("worker1");
                Assert.AreEqual(turn.Session.SessionId, held.SessionId);
                Assert.AreEqual(1L, state.Dispatches(held.Id));
                if (accounting is not null)
                {
                    JsonObject before = state.Ledger.Status(accounting.RunId, 30m);
                    Assert.IsFalse(before["uncertain"]!.GetValue<bool>());
                    Assert.AreEqual(237.25m, before["campaignCommitted"]!.GetValue<decimal>());
                }
                if (cause == "failure")
                {
                    OperationException supplied = new("controlled_turn_failure", failureMessage);
                    turn.Fail(supplied);
                    OperationException observed = await Assert
                        .ThrowsExactlyAsync<OperationException>(() => turn.Response)
                        .ConfigureAwait(false);
                    Assert.AreSame(supplied, observed);
                    Assert.IsTrue(turn.Response.IsFaulted);
                    Assert.IsFalse(turn.Response.IsCanceled);
                    await state.Active.WaitAsync(TimeSpan.FromSeconds(5), token)
                        .ConfigureAwait(false);
                }
                else
                {
                    TaskCompletionSource<DateTimeOffset> cancelled = new(
                        TaskCreationOptions.RunContinuationsAsynchronously
                    );
                    using CancellationTokenRegistration observation = turn.Token.Register(() =>
                        cancelled.TrySetResult(state.Clock.GetUtcNow())
                    );
                    if (accounting is not null)
                    {
                        state.Ledger.MarkUncertain(accounting.Id);
                    }
                    else
                    {
                        JsonObject closed = state.Invoke(
                            "owner", "task_write", new() { ["action"] = "close", ["id"] = 1 }
                        );
                        Assert.AreEqual("closed", closed["state"]!.GetValue<string>());
                        Assert.AreEqual("owner", closed["closedBy"]!.GetValue<string>());
                    }
                    state.Clock.Advance(poll, state.DrainDeadline, token);
                    // Freeze business time: a missed poll must not get a grace-expiry stop.
                    Assert.IsLessThan(
                        state.DrainDeadline,
                        await cancelled.Task.WaitAsync(TimeSpan.FromSeconds(5), token)
                            .ConfigureAwait(false)
                    );
                }
            }
            else if (cause == "caller")
            {
                state.CancelCaller();
            }
            else
            {
                JsonObject request = GitScenarioState.Data(
                    state.Coordinator().RequestControl("pause")
                );
                requestId = request["requestId"]!.GetValue<long>();
                Assert.AreEqual("pending", request["state"]!.GetValue<string>());
            }
            if (accounting is null && cause is not ("root" or "failure"))
            {
                await state.UntilAsync(
                    state.Handoff!.OwnerStopped.Task, state.DrainDeadline, token
                ).ConfigureAwait(false);
            }
            JsonObject result = await state.Active.WaitAsync(token).ConfigureAwait(false);
            Assert.IsLessThan(state.DrainDeadline, state.Clock.GetUtcNow());
            Assert.IsFalse(token.IsCancellationRequested);
            if (cause != "failure")
            {
                Assert.IsTrue(turn.Token.IsCancellationRequested);
            }
            Assert.AreEqual(0, turn.Session.CompletedTurns);
            Assert.IsFalse(result["ok"]!.GetValue<bool>());
            Assert.AreEqual("ownership_or_usage_unresolved", result["code"]!.GetValue<string>());
            JsonObject status = StatusData(inspector);
            JsonObject drain = Assert.IsInstanceOfType<JsonObject>(status["deadlineDrain"]);
            Assert.AreEqual("unresolved", status["lifecycle"]!.GetValue<string>());
            Assert.AreEqual("interrupted", drain["outcome"]!.GetValue<string>());
            Assert.AreEqual(reason, drain["stopReason"]!.GetValue<string>());
            Assert.AreEqual(reason, status["stopReason"]!.GetValue<string>());
            Assert.IsFalse(status["active"]!.GetValue<bool>());
            CreditReservation worker = state.Reservation("worker1");
            Assert.AreEqual("uncertain", worker.State);
            Assert.IsNull(worker.Actual);
            Assert.IsGreaterThan(0m, worker.Allowance);
            Assert.AreEqual(turn.Session.SessionId, worker.SessionId);
            Assert.AreEqual(1L, state.Dispatches(worker.Id));
            if (accounting is not null)
            {
                Assert.AreEqual(
                    "credit_unknown", drain["accountingStop"]!["code"]!.GetValue<string>()
                );
                Assert.AreEqual(200m, worker.Allowance);
                CreditReservation retained = state.Ledger.Reservations(accounting.RunId).Single();
                Assert.AreEqual(accounting.Id, retained.Id);
                Assert.AreEqual(accounting.SessionId, retained.SessionId);
                Assert.AreEqual("uncertain", retained.State);
                Assert.AreEqual(30m, retained.Allowance);
                Assert.IsNull(retained.Actual);
                Assert.AreEqual(
                    237.25m,
                    state.Ledger.Status(accounting.RunId, 30m)["campaignCommitted"]!
                        .GetValue<decimal>()
                );
            }
            if (cause == "root")
            {
                JsonObject rootTask = state.Invoke("owner", "task_read", new() { ["id"] = 1 });
                Assert.AreEqual("closed", rootTask["state"]!.GetValue<string>());
                Assert.AreEqual("owner", rootTask["closedBy"]!.GetValue<string>());
            }
            Assert.AreEqual("failed", state.Value(
                "SELECT state FROM delivery_batches WHERE id=$id;", ("$id", batch)
            ));
            Assert.AreEqual(
                cause == "failure"
                    ? failureMessage
                    : "Turn interrupted by coordinator stop; durable replay may repeat actions.",
                state.Value(
                    "SELECT last_error FROM delivery_batches WHERE id=$id;", ("$id", batch)
                )
            );
            if (requestId is long id)
            {
                JsonObject retainedControl = GitScenarioState.Data(
                    state.Coordinator().RequestControl("pause")
                );
                Assert.AreEqual(id, retainedControl["requestId"]!.GetValue<long>());
                Assert.AreEqual("pending", retainedControl["state"]!.GetValue<string>());
            }
            ControlledSessionPool.Host host = state.Sessions.Hosts.Single(
                item => item.Operation == "run"
            );
            Assert.AreEqual("managed", host.Witness["hostKind"]!.GetValue<string>());
            Assert.IsTrue(host.Witness["managedSettlementBeforeWrapperDispose"]!.GetValue<bool>());
            Assert.IsFalse(host.Witness["normalExitBeforeWrapperDispose"]!.GetValue<bool>());
        }).ConfigureAwait(false);
    }

    [TestMethod]
    [TestCategory("StartupWaitAndDrainCompletion")]
    [Timeout(120_000, CooperativeCancellation = true)]
    public async Task GraceExpiryCancelsPendingTurnWithoutAcknowledgementOrReleasedBacking()
    {
        CancellationToken token = testContext.CancellationToken;
        CoordinatorScenarioRun state = await CoordinatorScenarioRun
            .CreateAsync(testContext, token, workers: 1, duration: 10, ownedHosts: false)
            .ConfigureAwait(false);
        await state
            .RunBodyAsync(async () =>
            {
                Coordinator inspector = state.Coordinator("status");
                _ = state.Start();
                ControlledSessionPool.Turn turn = await state
                    .FixedAsync(next => state.Sessions.NextTurnAsync("worker1", next), token)
                    .ConfigureAwait(false);
                long batch = state.SendingBatch("worker1");
                DateTimeOffset workDeadline = state.WorkDeadline;
                DateTimeOffset graceDeadline = state.DrainDeadline;
                await state
                    .FixedAsync(
                        next => state.Clock.WaitForPollAsync(next, state.WorkDeadline),
                        token
                    )
                    .ConfigureAwait(false);
                state.Clock.Advance(TimeSpan.FromSeconds(10));
                await state
                    .UntilAsync(
                        () => StatusData(inspector)["lifecycle"]!.GetValue<string>() == "draining",
                        state.DrainDeadline,
                        token
                    )
                    .ConfigureAwait(false);
                Assert.IsFalse(turn.Token.IsCancellationRequested);
                ObservedManualClock.ObservedPoll wake = await state
                    .FixedAsync(
                        next => state.Clock.WaitForPollAsync(
                            next, graceDeadline, requireSingle: true
                        ),
                        token
                    )
                    .ConfigureAwait(false);
                DateTimeOffset probe = graceDeadline.AddTicks(-1);
                state.Clock.Advance(wake, graceDeadline, token, through: probe);
                await state.FixedAsync(
                    next => state.Clock.WaitForPollAsync(
                        next, requireSingle: true, registeredAfter: wake.TimerId
                    ),
                    token
                ).ConfigureAwait(false);
                Assert.AreEqual(probe, state.Clock.GetUtcNow());
                JsonObject draining = StatusData(inspector);
                Assert.AreEqual("draining", draining["lifecycle"]!.GetValue<string>());
                Assert.AreEqual(
                    "draining", draining["deadlineDrain"]!["outcome"]!.GetValue<string>()
                );
                Assert.AreEqual(
                    workDeadline,
                    draining["deadlineDrain"]!["workDeadline"]!.GetValue<DateTimeOffset>()
                );
                Assert.AreEqual(
                    graceDeadline,
                    draining["deadlineDrain"]!["drainDeadline"]!.GetValue<DateTimeOffset>()
                );
                Assert.IsFalse(turn.Token.IsCancellationRequested);
                Assert.IsFalse(turn.Response.IsCompleted);
                state.Clock.Advance(graceDeadline - state.Clock.GetUtcNow());
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
                Assert.AreNotEqual(
                    "sent",
                    state.Value("SELECT state FROM delivery_batches WHERE id=$id;", ("$id", batch))
                );
                JsonObject status = StatusData(inspector);
                Assert.AreEqual("unresolved", status["lifecycle"]!.GetValue<string>());
                Assert.AreEqual(
                    "timed-out",
                    status["deadlineDrain"]!["outcome"]!.GetValue<string>()
                );
                Assert.AreEqual("deadline", status["stopReason"]!.GetValue<string>());
                Assert.IsFalse(status["active"]!.GetValue<bool>());
            })
            .ConfigureAwait(false);
    }

    [TestMethod]
    [TestCategory("InterruptionAndPeerDrainSafety")]
    [Timeout(120_000, CooperativeCancellation = true)]
    public async Task IdlePeerCannotCancelAnotherPeersHealthyDispatchedDrain()
    {
        CancellationToken token = testContext.CancellationToken;
        CoordinatorScenarioRun state = await CoordinatorScenarioRun
            .CreateAsync(testContext, token, duration: 10, ownedHosts: false)
            .ConfigureAwait(false);
        await state
            .RunBodyAsync(async () =>
            {
                Coordinator inspector = state.Coordinator("status");
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
                ControlledSessionPool.Turn idle = await state
                    .FixedAsync(next => state.Sessions.NextTurnAsync("worker1", next), token)
                    .ConfigureAwait(false);
                ControlledSessionPool.Turn healthy = await state
                    .FixedAsync(next => state.Sessions.NextTurnAsync("worker2", next), token)
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
                await state
                    .FixedAsync(
                        next => state.Clock.WaitForPollAsync(next, state.WorkDeadline),
                        token
                    )
                    .ConfigureAwait(false);
                state.Clock.Advance(state.Clock.WorkDeadline!.Value - state.Clock.GetUtcNow());
                await state
                    .UntilAsync(
                        () => StatusData(inspector)["lifecycle"]!.GetValue<string>() == "draining",
                        state.DrainDeadline,
                        token
                    )
                    .ConfigureAwait(false);
                await state.FixedAsync(actualLoops[0], token).ConfigureAwait(false);
                await state
                    .FixedAsync(
                        next => state.Clock.WaitForPollAsync(next, state.DrainDeadline),
                        token
                    )
                    .ConfigureAwait(false);
                DateTimeOffset drainAt = state.Clock.GetUtcNow();
                Assert.IsFalse(healthy.Token.IsCancellationRequested);
                Assert.AreEqual(healthyBatch, state.SendingBatch("worker2"));
                Assert.AreEqual(TimeSpan.FromSeconds(10), healthy.Timeout);
                healthy.Complete();
                await state
                    .WaitAckAsync(healthyBatch, state.DrainDeadline, token)
                    .ConfigureAwait(false);
                await state.Active.WaitAsync(token).ConfigureAwait(false);
                Assert.AreEqual(drainAt, state.Clock.GetUtcNow());
                GitScenarioState.Data(await state.Active.ConfigureAwait(false));
                Assert.AreEqual(10m, state.Reservation("worker1").Actual);
                Assert.AreEqual(10m, state.Reservation("worker2").Actual);
                JsonObject status = StatusData(inspector);
                Assert.AreEqual("finalized", status["lifecycle"]!.GetValue<string>());
                Assert.AreEqual(
                    "completed",
                    status["deadlineDrain"]!["outcome"]!.GetValue<string>()
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
        CoordinatorScenarioRun state = await CoordinatorScenarioRun
            .CreateAsync(
                testContext, token, interval: 10, allocation: "shared", ownedHosts: false
            )
            .ConfigureAwait(false);
        await state
            .RunBodyAsync(async () =>
            {
                Coordinator inspector = state.Coordinator("status");
                DateTimeOffset began = state.Clock.GetUtcNow();
                _ = state.Start();
                ControlledSessionPool.Turn turn = await state
                    .FixedAsync(
                        next => state.Sessions.NextTurnAsync("worker1", next),
                        token
                    )
                    .ConfigureAwait(false);
                await state
                    .Clock.WaitForPollAsync(token, began.AddSeconds(10))
                    .ConfigureAwait(false);
                Assert.AreEqual(0m, state.Reservation("worker2").Allowance);
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
                    Assert.IsTrue(turn.Token.IsCancellationRequested);
                    Assert.AreEqual("uncertain", state.Reservation("worker1").State);
                    Assert.IsGreaterThan(0m, state.Reservation("worker1").Allowance);
                    Assert.IsNull(state.Reservation("worker1").Actual);
                    Assert.AreEqual(0, turn.Session.CompletedTurns);
                    Assert.AreNotEqual(
                        "sent",
                        state.Value(
                            "SELECT state FROM delivery_batches WHERE id=$id;",
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
                    CreditReservation retained = state.Reservation("worker1");
                    Assert.AreEqual(original.Id, retained.Id);
                    Assert.AreEqual("uncertain", retained.State);
                    Assert.IsGreaterThan(0m, retained.Allowance);
                    Assert.IsNull(retained.Actual);
                    Assert.AreEqual(0, turn.Session.CompletedTurns);
                    Assert.AreNotEqual(
                        "sent",
                        state.Value(
                            "SELECT state FROM delivery_batches WHERE id=$id;",
                            ("$id", batch)
                        )
                    );
                    Assert.AreEqual(1L, state.Dispatches(original.Id));
                }

                JsonObject status = StatusData(inspector);
                Assert.AreEqual("unresolved", status["lifecycle"]!.GetValue<string>());
                Assert.AreEqual(reason, status["stopReason"]!.GetValue<string>());
                Assert.IsFalse(status["active"]!.GetValue<bool>());
                Assert.IsLessThan(TimeSpan.FromSeconds(30), state.Clock.GetUtcNow() - began);
                if (cause != "failure")
                {
                    Assert.IsTrue(turn.Token.IsCancellationRequested);
                }
                Assert.IsNull(WorkerStatus(inspector, "worker2")["sessionId"]);
                Assert.AreEqual("released", state.Reservation("worker2").State);
                Assert.AreEqual(0m, state.Reservation("worker2").Allowance);
                Assert.AreEqual(
                    "not-entered",
                    status["deadlineDrain"]!["outcome"]!.GetValue<string>()
                );
            })
            .ConfigureAwait(false);
    }

    [TestMethod]
    [TestCategory("RampAdmissionCadence")]
    [Timeout(120_000, CooperativeCancellation = true)]
    public async Task ZeroIntervalLegacyPeersAreEligibleWithoutAdvancingManualTime()
    {
        CancellationToken token = testContext.CancellationToken;
        CoordinatorScenarioRun state = await CoordinatorScenarioRun
            .CreateAsync(testContext, token, ownedHosts: false)
            .ConfigureAwait(false);
        await state
            .RunBodyAsync(async () =>
            {
                DateTimeOffset began = state.Clock.GetUtcNow();
                _ = state.Start();
                ControlledSessionPool.Turn one = await state
                    .FixedAsync(next => state.Sessions.NextTurnAsync("worker1", next), token)
                    .ConfigureAwait(false);
                ControlledSessionPool.Turn two = await state
                    .FixedAsync(next => state.Sessions.NextTurnAsync("worker2", next), token)
                    .ConfigureAwait(false);
                Assert.AreEqual(began, state.Clock.GetUtcNow());
                Assert.AreNotEqual(one.Session.SessionId, two.Session.SessionId);
                Assert.AreEqual(100m, state.Reservation("worker1").Allowance);
                Assert.AreEqual(100m, state.Reservation("worker2").Allowance);
                long batch1 = state.SendingBatch("worker1");
                long batch2 = state.SendingBatch("worker2");
                ObservedManualClock.ObservedPoll ownerPoll = await state
                    .ObserveOwnerPollAsync(token)
                    .ConfigureAwait(false);
                one.Complete();
                two.Complete();
                await state.WaitAckAsync(batch1, state.WorkDeadline, token).ConfigureAwait(false);
                await state.WaitAckAsync(batch2, state.WorkDeadline, token).ConfigureAwait(false);
                GitScenarioState.Data(
                    await state
                        .StopRunningAsync("pause", ownerPoll, token)
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
        CoordinatorScenarioRun state = await CoordinatorScenarioRun
            .CreateAsync(
                testContext,
                token,
                duration: 60,
                allocation: "shared",
                allowance: 127.25m,
                ownedHosts: false
            )
            .ConfigureAwait(false);
        await state
            .RunBodyAsync(async () =>
            {
                Coordinator inspector = state.Coordinator("status");
                using WorkerHandoffControl handoff = new();
                state.Handoff = handoff;
                _ = state.Start();
                WorkerHandoffControl.Startup firstStart = await state
                    .FixedAsync(next => handoff.FirstStarting.Task.WaitAsync(next), token)
                    .ConfigureAwait(false);
                await state.FixedAsync(firstStart.Operation, token).ConfigureAwait(false);
                await state
                    .WaitDurableAsync(
                        () =>
                            state.Number(
                                "SELECT COUNT(*) FROM delivery_batches "
                                    + "WHERE recipient='worker1' AND state='sending';"
                            ) == 1,
                        state.WorkDeadline,
                        token
                    )
                    .ConfigureAwait(false);
                ControlledSessionPool.Turn first = await state
                    .FixedAsync(next => state.Sessions.NextTurnAsync("worker1", next), token)
                    .ConfigureAwait(false);
                Assert.AreEqual(100m, state.Reservation("worker1").Allowance);
                Assert.AreEqual(0m, state.Reservation("worker2").Allowance);
                long firstBatch = state.SendingBatch("worker1");
                // Capture the real second operation while the first 100 grant still
                // leaves only 20 backing: entry is not successful startup/consumption.
                WorkerHandoffControl.Startup secondStart = await state
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
                // The 100ms insufficient-backing retry is a real cancellable delay.
                // Join that same pending operation without consuming business time.
                await state.FixedAsync(secondStart.Operation, token).ConfigureAwait(false);
                Assert.AreEqual(accountingAt, state.Clock.GetUtcNow());
                await state
                    .WaitDurableAsync(
                        () =>
                            state.Number(
                                "SELECT COUNT(*) FROM delivery_batches "
                                    + "WHERE recipient='worker2' AND state='sending';"
                            ) == 1,
                        state.WorkDeadline,
                        token
                    )
                    .ConfigureAwait(false);
                ControlledSessionPool.Turn second = await state
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
                long followupBatch = state.SendingBatch("worker1");
                Assert.AreNotEqual(firstBatch, followupBatch);
                Task<JsonObject> preparation = await state
                    .FixedAsync(next => handoff.FollowupPreparation.Task.WaitAsync(next), token)
                    .ConfigureAwait(false);
                Assert.AreEqual(
                    10m,
                    await state
                        .FixedAsync(next => handoff.FollowupRecorded.Task.WaitAsync(next), token)
                        .ConfigureAwait(false)
                );
                // Only 10 credits remain; the 30-minimum next window must really wait.
                JsonObject money = state.Ledger.Status(state.Database.RunId, 127.25m);
                Assert.AreEqual(117.25m, money["runCommitted"]!.GetValue<decimal>());
                Assert.IsFalse(preparation.IsCompleted);
                Assert.IsFalse(second.Token.IsCancellationRequested);
                Assert.IsLessThan(30m, 127.25m - money["runCommitted"]!.GetValue<decimal>());
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
                        () => StatusData(inspector)["lifecycle"]!.GetValue<string>() == "draining",
                        state.DrainDeadline,
                        token
                    )
                    .ConfigureAwait(false);
                OperationException rejected = await Assert
                    .ThrowsExactlyAsync<OperationException>(() =>
                        state.FixedAsync(preparation, token)
                    )
                    .ConfigureAwait(false);
                Assert.AreEqual("admission_closed", rejected.Code);
                Assert.IsFalse(state.Reservation("worker1").GrantOpen);
                CreditReservation peerBacking = state.Reservation("worker2");
                Assert.AreEqual(secondStart.ReservationId, peerBacking.Id);
                Assert.AreEqual(100m, peerBacking.Allowance);
                Assert.IsTrue(peerBacking.GrantOpen);
                Assert.AreNotEqual(first.Session.SessionId, second.Session.SessionId);
                Assert.AreEqual(
                    first.Session.Metadata!["runtimeId"]!.GetValue<string>(),
                    second.Session.Metadata!["runtimeId"]!.GetValue<string>()
                );
                Assert.IsFalse(second.Response.IsCompleted);
                Assert.IsFalse(second.Token.IsCancellationRequested);
                Assert.AreEqual(TimeSpan.FromSeconds(60), second.Timeout);
                second.Complete();
                await state
                    .WaitAckAsync(secondBatch, state.DrainDeadline, token)
                    .ConfigureAwait(false);
                await state.Active.WaitAsync(token).ConfigureAwait(false);
                GitScenarioState.Data(await state.Active.ConfigureAwait(false));
                JsonObject status = StatusData(inspector);
                Assert.AreEqual("finalized", status["lifecycle"]!.GetValue<string>());
                Assert.AreEqual("deadline", status["stopReason"]!.GetValue<string>());
                Assert.AreEqual(
                    "completed",
                    status["deadlineDrain"]!["outcome"]!.GetValue<string>()
                );
                Assert.AreNotEqual(
                    "sent",
                    state.Value(
                        "SELECT state FROM delivery_batches WHERE id=$id;",
                        ("$id", followupBatch)
                    )
                );
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
            })
            .ConfigureAwait(false);
    }

    [TestMethod]
    [TestCategory("StartupWaitAndDrainCompletion")]
    [Timeout(120_000, CooperativeCancellation = true)]
    public async Task ExhaustedInflightSharedSendCannotRenewAfterAdmissionCloses()
    {
        CancellationToken token = testContext.CancellationToken;
        CoordinatorScenarioRun state = await CoordinatorScenarioRun
            .CreateAsync(
                testContext,
                token,
                workers: 1,
                duration: 10,
                allocation: "shared",
                ownedHosts: false
            )
            .ConfigureAwait(false);
        await state
            .RunBodyAsync(async () =>
            {
                Coordinator inspector = state.Coordinator("status");
                ExhaustionSessionHost exhaustion = new();
                state.Exhaustion = exhaustion;
                _ = state.Start();
                ControlledSessionPool.Turn turn = await state
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
                        () => StatusData(inspector)["lifecycle"]!.GetValue<string>() == "draining",
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
                JsonObject status = StatusData(inspector);
                Assert.AreEqual("unresolved", status["lifecycle"]!.GetValue<string>());
                Assert.AreEqual("credit_unknown", status["stopReason"]!.GetValue<string>());
                Assert.AreEqual(
                    "interrupted",
                    status["deadlineDrain"]!["outcome"]!.GetValue<string>()
                );
                Assert.AreEqual(original.Id, state.Reservation("worker1").Id);
                Assert.AreEqual(100m, state.Reservation("worker1").Allowance);
                Assert.AreEqual("uncertain", state.Reservation("worker1").State);
                Assert.IsNull(state.Reservation("worker1").Actual);
                Assert.AreEqual(0, turn.Session.CompletedTurns);
                Assert.AreEqual(100d, turn.Session.Metadata["appliedLimit"]!.GetValue<double>());
                Assert.AreEqual(0, exhaustion.Applied);
                Assert.AreEqual(0, exhaustion.Verified);
                Assert.AreEqual(1L, state.Dispatches(original.Id));
                Assert.AreEqual(1L, state.Number("SELECT COUNT(*) FROM delivery_batches;"));
                Assert.AreNotEqual(
                    "sent",
                    state.Value("SELECT state FROM delivery_batches WHERE id=$id;", ("$id", batch))
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
        CoordinatorScenarioRun state = await CoordinatorScenarioRun
            .CreateAsync(testContext, token, interval: 30, duration: 10, allocation: "shared")
            .ConfigureAwait(false);
        await state
            .RunBodyAsync(async () =>
            {
                Coordinator inspector = state.Coordinator("status");
                _ = state.Start();
                ControlledSessionPool.Turn first = await state
                    .FixedAsync(
                        next => state.Sessions.NextTurnAsync("worker1", next),
                        token
                    )
                    .ConfigureAwait(false);
                long batch = state.SendingBatch("worker1");
                string cold = state.Reservation("worker2").Id;
                Assert.AreEqual(0m, state.Reservation("worker2").Allowance);
                Assert.IsFalse(state.Reservation("worker2").GrantOpen);
                Assert.IsNull(WorkerStatus(inspector, "worker2")["sessionId"]);
                long leases = state.Number(
                    "SELECT COUNT(*) FROM process_leases WHERE kind LIKE 'native:%';"
                );
                await state.Clock.WaitForPollAsync(token, state.WorkDeadline).ConfigureAwait(false);
                state.Clock.Advance(state.WorkDeadline - state.Clock.GetUtcNow());
                await state
                    .UntilAsync(
                        () => StatusData(inspector)["lifecycle"]!.GetValue<string>() == "draining",
                        state.DrainDeadline,
                        token
                    )
                    .ConfigureAwait(false);
                first.Complete();
                await state.WaitAckAsync(batch, state.DrainDeadline, token).ConfigureAwait(false);
                await state.Active.WaitAsync(token).ConfigureAwait(false);
                GitScenarioState.Data(await state.Active.ConfigureAwait(false));
                JsonObject status = StatusData(inspector);
                Assert.AreEqual("finalized", status["lifecycle"]!.GetValue<string>());
                Assert.AreEqual("deadline", status["stopReason"]!.GetValue<string>());
                Assert.AreEqual(10m, state.Reservation("worker1").Actual);
                Assert.AreEqual(0m, state.Reservation("worker2").Allowance);
                Assert.IsFalse(state.Reservation("worker2").GrantOpen);
                Assert.AreEqual(0L, state.Dispatches(cold));
                Assert.IsNull(WorkerStatus(inspector, "worker2")["sessionId"]);
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
        CoordinatorScenarioRun state = await CoordinatorScenarioRun
            .CreateAsync(testContext, token, interval: 20, ownedHosts: false)
            .ConfigureAwait(false);
        await state
            .RunBodyAsync(async () =>
            {
                Coordinator inspector = state.Coordinator("status");
                _ = state.Start(ownerPollWorkers: 1);
                ControlledSessionPool.Turn first = await state
                    .FixedAsync(
                        next => state.Sessions.NextTurnAsync("worker1", next),
                        token
                    )
                    .ConfigureAwait(false);
                Assert.AreEqual(100m, state.Reservation("worker1").Allowance);
                // fair legacy reservation, NOT a shared grant
                Assert.AreEqual(100m, state.Reservation("worker2").Allowance);
                long batch = state.SendingBatch("worker1");
                ObservedManualClock.ObservedPoll originalOwnerPoll = await state
                    .ObserveOwnerPollAsync(token)
                    .ConfigureAwait(false);
                first.Complete();
                await state.WaitAckAsync(batch, state.WorkDeadline, token).ConfigureAwait(false);
                GitScenarioState.Data(
                    await state
                        .StopRunningAsync("pause", originalOwnerPoll, token)
                        .ConfigureAwait(false)
                );
                JsonObject pausedStatus = StatusData(inspector);
                Assert.AreEqual("paused", pausedStatus["lifecycle"]!.GetValue<string>());
                Assert.IsFalse(pausedStatus["active"]!.GetValue<bool>());
                Assert.AreEqual("reconciled", state.Reservation("worker1").State);
                Assert.IsNull(WorkerStatus(inspector, "worker2")["sessionId"]);
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
                ControlledSessionPool.Turn resumed;
                try
                {
                    _ = state.Start("resume");
                    WorkerHandoffControl.Startup startup = await state
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
                    WorkerStatus(inspector, "worker1")["recovery"]!.GetValue<string>()
                );
                Assert.IsTrue(StatusData(inspector)["active"]!.GetValue<bool>());
                Assert.AreEqual(95m, state.Reservation("worker1").Allowance);
                Assert.AreEqual(95m, state.Reservation("worker2").Allowance);
                Assert.IsEmpty(
                    state
                        .Sessions.Hosts.Where(host => host.Operation == "resume")
                        .SelectMany(host => host.Sessions)
                        .Where(session => session.Worker == "worker2")
                );
                long resumedBatch = state.SendingBatch("worker1");
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
                WorkerHandoffControl.Startup peerStart = await state.Handoff.SecondStarting.Task
                    .WaitAsync(token).ConfigureAwait(false);
                await state.FixedAsync(peerStart.Operation, token).ConfigureAwait(false);
                ControlledSessionPool.Turn peer = await state
                    .FixedAsync(next => state.Sessions.NextTurnAsync("worker2", next), token)
                    .ConfigureAwait(false);
                Assert.IsGreaterThanOrEqualTo(admitted.AddSeconds(20), state.Clock.GetUtcNow());
                Assert.AreEqual(95m, state.Reservation("worker2").Allowance);
                long peerBatch = state.SendingBatch("worker2");
                ObservedManualClock.ObservedPoll resumedOwnerPoll = await state
                    .ObserveOwnerPollAsync(token)
                    .ConfigureAwait(false);
                resumed.Complete();
                peer.Complete();
                await state
                    .WaitAckAsync(resumedBatch, state.WorkDeadline, token)
                    .ConfigureAwait(false);
                await state
                    .WaitAckAsync(peerBatch, state.WorkDeadline, token)
                    .ConfigureAwait(false);
                GitScenarioState.Data(
                    await state
                        .StopRunningAsync("pause", resumedOwnerPoll, token)
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
        CoordinatorScenarioRun state = await CoordinatorScenarioRun
            .CreateAsync(
                testContext, token, workers: 3, interval: 10,
                allocation: "shared", allowance: 307.25m
            )
            .ConfigureAwait(false);
        await state
            .RunBodyAsync(async () =>
            {
                Coordinator inspector = state.Coordinator("status");
                using WorkerHandoffControl handoff = new();
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
                        ControlledSessionPool.Host host = state.Sessions.Hosts.Single(item =>
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
                    WorkerHandoffControl.Startup firstStart = await state
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
                    await ProbeAsync("worker2", handoff.SecondStarting.Task, first.CompletedAt)
                        .ConfigureAwait(false);

                    await state.UntilAsync(handoff.SecondStarting.Task, state.WorkDeadline, token)
                        .ConfigureAwait(false);
                    WorkerHandoffControl.Startup secondStart = await handoff.SecondStarting.Task
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
                    await ProbeAsync("worker3", handoff.ThirdStarting.Task, second.CompletedAt)
                        .ConfigureAwait(false);

                    await state.UntilAsync(handoff.ThirdStarting.Task, state.WorkDeadline, token)
                        .ConfigureAwait(false);
                    WorkerHandoffControl.Startup thirdStart = await handoff.ThirdStarting.Task
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
                    ControlledSessionPool.Turn[] turns = [first.Turn, second.Turn, third.Turn];
                    ControlledSessionPool.Host runHost = Assert.ContainsSingle(
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
                            "SELECT COUNT(*) FROM process_leases WHERE id=$lease AND "
                                + "kind=$kind AND pid=$pid AND start_ticks=$birth AND "
                                + "state='running';",
                            ("$lease", runHost.LeaseId),
                            ("$kind", "native:runtime:" + runHost.Id),
                            ("$pid", runHost.Ready.Pid),
                            ("$birth", runHost.Ready.BirthTicks)
                        )
                    );
                    foreach (string worker in workerNames)
                    {
                        Assert.Contains(worker, entries.Keys);
                        ControlledSessionPool.Turn turn = turns.Single(item =>
                            item.Session.Worker == worker
                        );
                        string sessionId = turn.Session.SessionId;
                        CreditReservation reservation = state.Reservation(worker);
                        Assert.AreEqual(sessionId, reservation.SessionId);
                        Assert.AreEqual(
                            sessionId,
                            WorkerStatus(inspector, worker)["sessionId"]!.GetValue<string>()
                        );
                        Assert.AreEqual(
                            state.WorkerPath(worker),
                            turn.Session.Metadata!["workingDirectory"]!.GetValue<string>()
                        );
                        Assert.AreEqual(
                            runHost.Id,
                            turn.Session.Metadata["runtimeId"]!.GetValue<string>()
                        );
                        Assert.AreEqual(
                            runHost.LeaseId,
                            turn.Session.Metadata["runtimeLeaseId"]!.GetValue<long>()
                        );
                        Assert.AreEqual(
                            runHost.Ready.Pid,
                            turn.Session.Metadata["pid"]!.GetValue<int>()
                        );
                        Assert.AreEqual(
                            runHost.Ready.BirthTicks,
                            turn.Session.Metadata["birthTicks"]!.GetValue<long>()
                        );
                        Assert.AreEqual(
                            (double)reservation.Allowance,
                            turn.Session.Metadata["appliedLimit"]!.GetValue<double>()
                        );
                    }
                    ObservedManualClock.ObservedPoll ownerPoll = await state
                        .ObserveOwnerPollAsync(token)
                        .ConfigureAwait(false);
                    first.Turn.Complete();
                    second.Turn.Complete();
                    third.Turn.Complete();
                    foreach (long batch in new[] { first.Batch, second.Batch, third.Batch })
                    {
                        await state
                            .WaitAckAsync(batch, state.WorkDeadline, token)
                            .ConfigureAwait(false);
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
                            .StopRunningAsync("pause", ownerPoll, token)
                            .ConfigureAwait(false)
                    );
                    JsonObject status = StatusData(inspector);
                    Assert.AreEqual("paused", status["lifecycle"]!.GetValue<string>());
                    Assert.AreEqual("owner-pause", status["stopReason"]!.GetValue<string>());
                    foreach (string worker in workerNames)
                    {
                        Assert.AreEqual(10m, state.Reservation(worker).Actual);
                        Assert.AreEqual("reconciled", state.Reservation(worker).State);
                        Assert.IsFalse(state.Reservation(worker).GrantOpen);
                        Assert.AreEqual(1L, state.Dispatches(state.Reservation(worker).Id));
                    }
                    Assert.AreEqual(
                        37.25m,
                        state.Ledger.Status(
                            state.Database.RunId,
                            state.Database.Configuration.RunAiCreditAllowance
                        )["runCommitted"]!.GetValue<decimal>()
                    );
                }
                finally
                {
                    // Only fixture-owned Open gates, BEFORE primary-aware run cleanup.
                    firstRelease.TrySetResult();
                    secondRelease.TrySetResult();
                }

                async Task HeldAsync(WorkerHandoffControl.Startup startup, Task entered)
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
                    ControlledSessionPool.Turn Turn,
                    long Batch,
                    DateTimeOffset CompletedAt
                )> ConsumeAsync(WorkerHandoffControl.Startup startup)
                {
                    Assert.AreEqual(state.Reservation(startup.Worker).Id, startup.ReservationId);
                    Assert.AreEqual(state.WorkerPath(startup.Worker), startup.Cwd);
                    DateTimeOffset fixedAt = state.Clock.GetUtcNow();
                    await state.FixedAsync(startup.Operation, token).ConfigureAwait(false);
                    Assert.AreEqual(fixedAt, state.Clock.GetUtcNow());
                    // For the two held operations this is their genuine completion
                    // phase. Consumption is a separate, later durable milestone.
                    await state
                        .WaitDurableAsync(
                            () =>
                                state.Number(
                                    "SELECT COUNT(*) FROM delivery_batches WHERE "
                                        + "recipient=$worker AND state='sending';",
                                    ("$worker", startup.Worker)
                                ) == 1,
                            startup.Worker == "worker3"
                                ? state.WorkDeadline
                                : fixedAt.AddSeconds(9),
                            token
                        )
                        .ConfigureAwait(false);
                    long batch = state.SendingBatch(startup.Worker);
                    long consumedAtMilliseconds = state.Number(
                        "SELECT created_at FROM delivery_batches WHERE id=$id;",
                        ("$id", batch)
                    );
                    DateTimeOffset claimObservedAt = state.Clock.GetUtcNow();
                    ControlledSessionPool.Turn turn = await state
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
                        // The durable batch timestamp has only millisecond precision.
                        Assert.IsGreaterThanOrEqualTo(
                            fixedAt.ToUnixTimeMilliseconds(), consumedAtMilliseconds
                        );
                    }
                    Assert.IsTrue(state.Reservation(startup.Worker).GrantOpen);
                    Assert.AreEqual(TimeSpan.FromSeconds(1800), turn.Timeout);
                    Assert.IsFalse(turn.Response.IsCompleted);
                    return (turn, batch, fixedAt);
                }

                void Cold(string worker, Task starting)
                {
                    Assert.IsFalse(starting.IsCompleted);
                    AssertColdState(worker);
                }

                void AssertColdState(string worker)
                {
                    Assert.IsFalse(entries.ContainsKey(worker));
                    Assert.IsNull(WorkerStatus(inspector, worker)["sessionId"]);
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

                async Task ProbeAsync(
                    string worker,
                    Task<WorkerHandoffControl.Startup> starting,
                    DateTimeOffset completedAt
                )
                {
                    DateTimeOffset eligible = completedAt.AddSeconds(10);
                    DateTimeOffset probe = eligible.AddTicks(-1);
                    Task<JsonObject> run = state.Active;
                    await CoordinatorScenarioRun.RequireColdRampTargetAsync(starting, run, eligible)
                        .ConfigureAwait(false);
                    AssertColdState(worker);
                    Assert.IsLessThanOrEqualTo(
                        probe,
                        state.Clock.GetUtcNow(),
                        "Required pre-eligibility probe was missed."
                    );

                    // A pass that sampled time before the jump may still be in Git.
                    // Wake its known progress wait, then witness the following pass.
                    ObservedManualClock.ObservedPoll wake = await state
                        .ObserveRampProbePollAsync(
                            starting, run, eligible, registeredAfter: 0, token
                        )
                        .ConfigureAwait(false);
                    try
                    {
                        state.Clock.Advance(wake, state.WorkDeadline, token, through: probe);
                    }
                    catch (Exception error) when (error is InvalidOperationException
                        or OperationCanceledException)
                    {
                        await CoordinatorScenarioRun
                            .RequireColdRampTargetAsync(starting, run, eligible)
                            .ConfigureAwait(false);
                        throw;
                    }
                    // Qualify against the work deadline: the next poll can be due after
                    // eligibility even though this decision completed before it.
                    await state
                        .ObserveRampProbePollAsync(starting, run, eligible, wake.TimerId, token)
                        .ConfigureAwait(false);
                    await CoordinatorScenarioRun.RequireColdRampTargetAsync(starting, run, eligible)
                        .ConfigureAwait(false);
                    Assert.AreEqual(probe, state.Clock.GetUtcNow());
                    AssertColdState(worker);
                    Assert.IsLessThan(eligible, state.Clock.GetUtcNow());
                    await CoordinatorScenarioRun.RequireColdRampTargetAsync(starting, run, eligible)
                        .ConfigureAwait(false);
                    state.Clock.Advance(eligible - state.Clock.GetUtcNow());
                }
            })
            .ConfigureAwait(false);
    }

    private static JsonObject StatusData(Coordinator inspector) =>
        GitScenarioState.Data(inspector.Status());

    private static JsonObject WorkerStatus(Coordinator inspector, string worker) =>
        StatusData(inspector)["workers"]!
            .AsArray()
            .OfType<JsonObject>()
            .Single(item => item["id"]!.GetValue<string>() == worker);
}
