using System.Globalization;
using System.Runtime.ExceptionServices;
using System.Text.Json.Nodes;
using LiteAgensh.Tests.Support;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace LiteAgensh.Tests;

[TestClass]
public sealed class PeerCoordinationSpecs(TestContext testContext)
{
    private static readonly string[] peerActors = ["worker1", "worker2"];

    [TestMethod]
    [TestCategory("TwoPeersCooperateWithoutScheduler")]
    [Timeout(120_000, CooperativeCancellation = true)]
    public async Task PeersSelectWorkCommunicateAndIntegrateIndependentlyReviewedChanges()
    {
        CancellationToken token = testContext.CancellationToken;
        CoordinatorScenarioRun state = await CoordinatorScenarioRun
            .CreateAsync(testContext, token, ownedHosts: false, realGit: true)
            .ConfigureAwait(false);
        await state
            .RunBodyAsync(async () =>
            {
                Coordinator inspector = state.Coordinator("status");
                _ = state.Start();
                ControlledSessionPool.Turn first = await state
                    .FixedAsync(next => state.Sessions.NextTurnAsync("worker1", next), token)
                    .ConfigureAwait(false);
                ControlledSessionPool.Turn second = await state
                    .FixedAsync(next => state.Sessions.NextTurnAsync("worker2", next), token)
                    .ConfigureAwait(false);
                JsonObject task1 = state.Invoke(
                    "worker1",
                    "task_write",
                    new()
                    {
                        ["action"] = "create",
                        ["title"] = "Parser change",
                        ["body"] = "Deliver parser.txt with a passing declared check.",
                    }
                );
                JsonObject task2 = state.Invoke(
                    "worker2",
                    "task_write",
                    new()
                    {
                        ["action"] = "create",
                        ["title"] = "Renderer change",
                        ["body"] = "Deliver renderer.txt independently.",
                    }
                );
                long taskId = task1["id"]!.GetValue<long>();
                foreach (string actor in peerActors)
                {
                    state.Invoke(
                        actor,
                        "board_publish",
                        new()
                        {
                            ["type"] = "CLAIM",
                            ["content"] = $"Cooperating on #{taskId}",
                            ["workLabel"] = actor + "-parser-review",
                            ["ttlSeconds"] = 60,
                        }
                    );
                }
                JsonObject fact = state.Invoke(
                    "worker2",
                    "board_publish",
                    new()
                    {
                        ["type"] = "FACT",
                        ["content"] = "Renderer work is independent of parser.txt.",
                    }
                );
                JsonObject team = state.Invoke(
                    "worker1",
                    "message_send",
                    new()
                    {
                        ["recipient"] = "team",
                        ["body"] = "Parser selection; renderer is available.",
                    }
                );
                JsonObject direct = state.Invoke(
                    "worker2",
                    "message_send",
                    new()
                    {
                        ["recipient"] = "worker1",
                        ["body"] = "I selected renderer; please review my exact head.",
                    }
                );
                JsonObject reply = state.Invoke(
                    "worker1",
                    "message_send",
                    new()
                    {
                        ["recipient"] = "worker2",
                        ["threadId"] = direct["threadId"]!.GetValue<long>(),
                        ["body"] = "Independent review agreed.",
                    }
                );
                JsonObject thread = state.Invoke(
                    "worker2",
                    "message_thread",
                    new() { ["threadId"] = direct["threadId"]!.GetValue<long>() }
                );
                Assert.AreEqual("worker1", task1["author"]!.GetValue<string>());
                Assert.AreEqual("worker2", task2["author"]!.GetValue<string>());
                Assert.HasCount(2, thread["items"]!.AsArray());
                Assert.AreEqual(
                    reply["id"]!.GetValue<long>(),
                    thread["items"]![1]!["id"]!.GetValue<long>()
                );
                JsonObject claimedTask = state.Invoke(
                    "owner",
                    "task_read",
                    new() { ["id"] = taskId }
                );
                CollectionAssert.AreEqual(
                    peerActors,
                    claimedTask["assignees"]!
                        .AsArray()
                        .Select(item => item!.GetValue<string>())
                        .ToArray()
                );

                (JsonObject change1, _) = await SubmitReviewedAsync(
                        state,
                        "worker1",
                        "worker2",
                        "parser.txt",
                        "parser feature\n",
                        token
                    )
                    .ConfigureAwait(false);
                (JsonObject change2, _) = await SubmitReviewedAsync(
                        state,
                        "worker2",
                        "worker1",
                        "renderer.txt",
                        "renderer feature\n",
                        token
                    )
                    .ConfigureAwait(false);
                TaskCompletionSource integrationPass = new(
                    TaskCreationOptions.RunContinuationsAsynchronously
                );
                state.ObserveWorkerCompletion = _ => integrationPass.TrySetResult();
                await state
                    .UntilAsync(integrationPass.Task, state.WorkDeadline, token)
                    .ConfigureAwait(false);
                state.ObserveWorkerCompletion = null;
                await state
                    .WaitDurableAsync(
                        () => IntegratedChanges(state, token).Count == 2,
                        state.WorkDeadline,
                        token
                    )
                    .ConfigureAwait(false);
                string integration = $"refs/heads/lite-agensh/{state.Database.RunId}/integration";
                Assert.AreEqual(
                    "parser feature\n",
                    await state
                        .Git.GitAsync(
                            state.Git.Repository,
                            token,
                            "show",
                            integration + ":parser.txt"
                        )
                        .ConfigureAwait(false)
                );
                Assert.AreEqual(
                    "renderer feature\n",
                    await state
                        .Git.GitAsync(
                            state.Git.Repository,
                            token,
                            "show",
                            integration + ":renderer.txt"
                        )
                        .ConfigureAwait(false)
                );
                foreach (JsonObject change in new[] { change1, change2 })
                {
                    JsonObject detailed = await state
                        .GitToolAsync(
                            "owner",
                            "change_read",
                            new() { ["id"] = change["id"]!.GetValue<long>() },
                            token
                        )
                        .ConfigureAwait(false);
                    Assert.AreEqual("integrated", detailed["state"]!.GetValue<string>());
                    JsonObject check = Assert.ContainsSingle(
                        detailed["checks"]!.AsArray().OfType<JsonObject>()
                    );
                    Assert.AreEqual("passed", check["status"]!.GetValue<string>());
                    Assert.AreEqual(0, check["exitCode"]!.GetValue<int>());
                    JsonObject review = Assert.ContainsSingle(
                        detailed["reviews"]!.AsArray().OfType<JsonObject>()
                    );
                    Assert.AreNotEqual(
                        change["author"]!.GetValue<string>(),
                        review["actor"]!.GetValue<string>()
                    );
                    Assert.AreEqual(
                        change["head"]!.GetValue<string>(),
                        review["head"]!.GetValue<string>()
                    );
                    Assert.AreEqual("approve", review["decision"]!.GetValue<string>());
                }
                first.Complete();
                second.Complete();
                ControlledSessionPool.Turn context1 = await state
                    .FixedAsync(next => state.Sessions.NextTurnAsync("worker1", next), token)
                    .ConfigureAwait(false);
                ControlledSessionPool.Turn context2 = await state
                    .FixedAsync(next => state.Sessions.NextTurnAsync("worker2", next), token)
                    .ConfigureAwait(false);
                Assert.Contains(direct["body"]!.GetValue<string>(), context1.Text);
                Assert.Contains(fact["content"]!.GetValue<string>(), context1.Text);
                Assert.Contains(team["body"]!.GetValue<string>(), context2.Text);
                Assert.Contains("Cooperating on #", context2.Text);
                long batch1 = state.SendingBatch("worker1");
                long batch2 = state.SendingBatch("worker2");
                ObservedManualClock.ObservedPoll ownerPoll = await state
                    .ObserveOwnerPollAsync(token)
                    .ConfigureAwait(false);
                context1.Complete();
                context2.Complete();
                await state.WaitAckAsync(batch1, state.WorkDeadline, token).ConfigureAwait(false);
                await state.WaitAckAsync(batch2, state.WorkDeadline, token).ConfigureAwait(false);
                GitScenarioState.Data(
                    await state
                        .StopRunningAsync("pause", ownerPoll, token)
                        .ConfigureAwait(false)
                );
                JsonObject status = StatusData(inspector);
                Assert.AreEqual("paused", status["lifecycle"]!.GetValue<string>());
                Assert.AreEqual("owner-pause", status["stopReason"]!.GetValue<string>());
            })
            .ConfigureAwait(false);
    }

    [TestMethod]
    [TestCategory("TwoPeersCooperateWithoutScheduler")]
    [TestCategory("RampAdmissionCadence")]
    [DataRow(true, 0)]
    [DataRow(false, 0)]
    [DataRow(true, 30)]
    [DataRow(false, 30)]
    [Timeout(120_000, CooperativeCancellation = true)]
    public Task PlannerReleasesPeerOnIntegratedPlanOrConfiguredTimeout(
        bool integrate,
        int interval
    ) =>
        RunPlannerReleaseAsync(
            integrate ? PlannerCandidate.Passing : PlannerCandidate.None,
            interval,
            testContext.CancellationToken
        );

    [TestMethod]
    [TestCategory("TwoPeersCooperateWithoutScheduler")]
    [TestCategory("RampAdmissionCadence")]
    [Timeout(120_000, CooperativeCancellation = true)]
    public Task PlannerReleasesPeerAtConfiguredTimeoutAfterRejectedPlan() =>
        RunPlannerReleaseAsync(PlannerCandidate.Rejected, 0, testContext.CancellationToken);

    private async Task RunPlannerReleaseAsync(
        PlannerCandidate candidate,
        int interval,
        CancellationToken token
    )
    {
        bool integrate = candidate == PlannerCandidate.Passing;
        bool reject = candidate == PlannerCandidate.Rejected;
        CoordinatorScenarioRun state = await CoordinatorScenarioRun
            .CreateAsync(
                testContext,
                token,
                planning: 10,
                interval: interval,
                allocation: "shared",
                ownedHosts: false,
                configuredCheck: reject
                    ? (_, arguments, _, _, _) => Task.FromResult(
                        new ProcessResult(
                            int.Parse(arguments[1], CultureInfo.InvariantCulture),
                            "Controlled planner check.",
                            "",
                            false
                        )
                    )
                    : null,
                realGit: candidate != PlannerCandidate.None
            )
            .ConfigureAwait(false);
        await state
            .RunBodyAsync(async () =>
            {
                const string PlanFile = "plan.md";
                if (reject)
                {
                    state.Database.SaveConfiguration(
                        state.Database.Configuration with
                        {
                            CandidateChecks = [GitScenarioState.ExitCheck(1)],
                        }
                    );
                }
                Coordinator inspector = state.Coordinator("status");
                using WorkerHandoffControl handoff = new();
                state.Handoff = handoff;
                TaskCompletionSource<DateTimeOffset> plannerReady = new(
                    TaskCreationOptions.RunContinuationsAsynchronously
                );
                TaskCompletionSource<DateTimeOffset> followerEntry = new(
                    TaskCreationOptions.RunContinuationsAsynchronously
                );
                JsonObject? followerIdentity = null;
                TaskCompletionSource<bool> opening = new(
                    TaskCreationOptions.RunContinuationsAsynchronously
                );
                TaskCompletionSource release = new(
                    TaskCreationOptions.RunContinuationsAsynchronously
                );
                state.Sessions.BeforeOpen = async (session, business) =>
                {
                    if (session.Worker == "worker1")
                    {
                        opening.TrySetResult(true);
                        await release.Task.WaitAsync(business).ConfigureAwait(false);
                    }
                    else if (session.Worker == "worker2")
                    {
                        ControlledSessionPool.Host host = state.Sessions.Hosts.Single(item =>
                            item.Sessions.Contains(session)
                        );
                        followerIdentity = new()
                        {
                            ["sessionId"] = session.SessionId,
                            ["runtimeId"] = host.Id,
                            ["leaseId"] = host.LeaseId,
                            ["reservationId"] = state.Reservation("worker2").Id,
                            ["backedAllowance"] = state.Reservation("worker2").Allowance,
                        };
                        followerEntry.TrySetResult(state.Clock.GetUtcNow());
                    }
                };
                state.Sessions.BeforeRead = (session, _) =>
                {
                    if (
                        session.Worker == "worker1"
                        && session.CompletedTurns == 0
                        && WorkerStatus(inspector, "worker1")["lifecycle"]!.GetValue<string>()
                            == "ready"
                    )
                    {
                        plannerReady.TrySetResult(state.Clock.GetUtcNow());
                    }
                    return Task.CompletedTask;
                };
                Task<JsonObject> producer = state.Start();
                await state
                    .FixedAsync(next => opening.Task.WaitAsync(next), token)
                    .ConfigureAwait(false);
                await ObserveLivePlannerPollAsync("the first pre-release planner progress poll")
                    .ConfigureAwait(false);
                state.Clock.Advance(TimeSpan.FromSeconds(20));
                Assert.IsNull(WorkerStatus(inspector, "worker2")["sessionId"]);
                Assert.AreEqual(0m, state.Reservation("worker2").Allowance);
                // While the actual Open is still held, wait for registered progress
                // again. No successful first admission can yet have been consumed.
                await ObserveLivePlannerPollAsync("the second pre-release planner progress poll")
                    .ConfigureAwait(false);
                DateTimeOffset openedAt = state.Clock.GetUtcNow();
                release.TrySetResult();
                WorkerHandoffControl.Startup plannerStart = await state
                    .FixedAsync(next => handoff.FirstStarting.Task.WaitAsync(next), token)
                    .ConfigureAwait(false);
                await state.FixedAsync(plannerStart.Operation, token).ConfigureAwait(false);
                Assert.AreEqual(openedAt, state.Clock.GetUtcNow());
                DateTimeOffset admittedAt = state.Clock.GetUtcNow();
                DateTimeOffset readyAt = await state
                    .FixedAsync(next => plannerReady.Task.WaitAsync(next), token)
                    .ConfigureAwait(false);
                Assert.AreEqual(admittedAt, readyAt);
                DateTimeOffset planningDeadline = admittedAt.AddSeconds(10);
                ControlledSessionPool.Turn planner = await state
                    .FixedAsync(next => state.Sessions.NextTurnAsync("worker1", next), token)
                    .ConfigureAwait(false);
                long plannerBatch = state.SendingBatch("worker1");
                DateTimeOffset eligibleAt = admittedAt.AddSeconds(interval);
                Assert.IsLessThan(planningDeadline, state.Clock.GetUtcNow());
                Assert.Contains("initial planner", planner.Text);
                TaskCompletionSource healthPass = new(
                    TaskCreationOptions.RunContinuationsAsynchronously
                );
                state.ObserveWorkerCompletion = _ =>
                {
                    if (state.Clock.GetUtcNow() > readyAt)
                    {
                        healthPass.TrySetResult();
                    }
                };
                await state
                    .UntilAsync(healthPass.Task, planningDeadline, token)
                    .ConfigureAwait(false);
                state.ObserveWorkerCompletion = null;
                await state
                    .WaitDurableAsync(
                        () =>
                            handoff.SecondStarting.Task.IsCompleted
                            || state.Number(
                                "SELECT COUNT(*) FROM git_health WHERE checked_at>$ready;",
                                ("$ready", readyAt.ToString("O"))
                            ) == 1,
                        planningDeadline,
                        token
                    )
                    .ConfigureAwait(false);
                AssertColdPeer();
                Assert.AreEqual(
                    1L,
                    state.Number(
                        "SELECT COUNT(*) FROM git_health WHERE checked_at>$ready;",
                        ("$ready", readyAt.ToString("O"))
                    )
                );
                Assert.IsGreaterThan(readyAt, state.Clock.GetUtcNow());
                Assert.IsLessThan(planningDeadline, state.Clock.GetUtcNow());
                Assert.IsEmpty(IntegratedChanges(state, token));
                if (interval == 0)
                {
                    Assert.IsGreaterThanOrEqualTo(eligibleAt, state.Clock.GetUtcNow());
                }

                ObservedManualClock.ObservedPoll? rejectionPoll = null;
                if (candidate != PlannerCandidate.None)
                {
                    ObservedManualClock.ObservedPoll? requestPoll = reject
                        ? await ObservePlanningPollAsync().ConfigureAwait(false)
                        : null;
                    DateTimeOffset submittedAt = state.Clock.GetUtcNow();
                    (JsonObject change, JsonObject request) = await SubmitReviewedAsync(
                            state,
                            "worker1",
                            "worker2",
                            PlanFile,
                            "Independent parser and renderer tasks; candidate-bound review.\n",
                            token
                        )
                        .ConfigureAwait(false);
                    Assert.AreEqual(submittedAt, state.Clock.GetUtcNow());
                    if (requestPoll is { } pending)
                    {
                        state.Clock.Advance(pending, planningDeadline, token);
                        rejectionPoll = await ObservePlanningPollAsync(pending.TimerId)
                            .ConfigureAwait(false);
                        await AssertRejectedPlannerCandidateAsync(state, change, request, token)
                            .ConfigureAwait(false);
                    }
                }
                ObservedManualClock.ObservedPoll? timeoutStartupPoll = null;
                long? timeoutKickoff = null;
                if (integrate)
                {
                    TaskCompletionSource integrationPass = new(
                        TaskCreationOptions.RunContinuationsAsynchronously
                    );
                    state.ObserveWorkerCompletion = _ => integrationPass.TrySetResult();
                    await state
                        .UntilAsync(integrationPass.Task, planningDeadline, token)
                        .ConfigureAwait(false);
                    state.ObserveWorkerCompletion = null;
                    await state
                        .WaitDurableAsync(
                            () => IntegratedChanges(state, token).Count == 1,
                            planningDeadline,
                            token
                        )
                        .ConfigureAwait(false);
                    Assert.ContainsSingle(IntegratedChanges(state, token));
                    Assert.IsLessThan(planningDeadline, state.Clock.GetUtcNow());
                }
                else
                {
                    ObservedManualClock.ObservedPoll edgePoll;
                    if (rejectionPoll is { } afterFailure)
                    {
                        // The failed request is durable. Wake a new decision while
                        // still inside the planning window and the planner turn is held.
                        DateTimeOffset failedAt = state.Clock.GetUtcNow();
                        state.Clock.Advance(afterFailure, planningDeadline, token);
                        edgePoll = await state.ObserveRampProbePollAsync(
                            handoff.SecondStarting.Task, state.Active, planningDeadline,
                            afterFailure.TimerId, token
                        ).ConfigureAwait(false);
                        DateTimeOffset decisionAt = state.Clock.GetUtcNow();
                        Assert.IsGreaterThan(failedAt, decisionAt);
                        Assert.IsLessThan(planningDeadline, decisionAt);
                        Assert.IsFalse(planner.Response.IsCompleted);
                        Assert.AreEqual(
                            1L,
                            state.Number(
                                "SELECT passed FROM git_health "
                                    + "WHERE checked_at=$decision AND commit_hash=$baseline;",
                                ("$decision", decisionAt.ToString("O")),
                                ("$baseline", state.Git.BaseCommit)
                            ),
                            "The coordinator must complete a healthy pass after rejection."
                        );
                    }
                    else
                    {
                        Assert.IsEmpty(AllChanges(state, token));
                        edgePoll = await ObservePlanningPollAsync().ConfigureAwait(false);
                    }
                    AssertColdPeer();
                    Assert.IsNull(PeerKickoff(), "No peer kickoff is due before planning timeout.");
                    if (reject)
                    {
                        Assert.IsEmpty(IntegratedChanges(state, token));
                    }
                    state.Clock.Advance(
                        edgePoll, state.WorkDeadline, token, through: planningDeadline
                    );
                    // The held planner cannot register another idle poll. A fresh
                    // coordinator poll follows the Git pass and the release decision.
                    timeoutStartupPoll = await state
                        .FixedAsync(
                            next => state.Clock.WaitForPollAsync(
                                next, state.WorkDeadline, requireSingle: true,
                                registeredAfter: edgePoll.TimerId
                            ),
                            token
                        )
                        .ConfigureAwait(false);
                    if (state.Active.IsCompleted)
                    {
                        Assert.Fail(
                            "Coordinator ended at the planning edge: "
                                + (await state.Active.ConfigureAwait(false)).ToJsonString()
                        );
                    }
                    Assert.AreEqual(planningDeadline, state.Clock.GetUtcNow());
                    Assert.AreEqual(
                        1L,
                        state.Number(
                            "SELECT passed FROM git_health WHERE checked_at=$edge;",
                            ("$edge", planningDeadline.ToString("O"))
                        ),
                        "The coordinator must complete a healthy pass at the planning edge."
                    );
                    timeoutKickoff = PeerKickoff();
                    Assert.IsNotNull(
                        timeoutKickoff,
                        "The first healthy decision at the configured planning deadline "
                        + "must release worker2."
                    );
                    if (interval > 0)
                    {
                        AssertColdPeer();
                    }
                }
                WorkerHandoffControl.Startup? eligibleStartup = null;
                if (interval > 0)
                {
                    // The positive representative still owes the interval from the
                    // preceding COMPLETED admission, despite earlier planning release.
                    DateTimeOffset probe = eligibleAt.AddSeconds(-1);
                    Task<JsonObject> run = state.Active;
                    Task<WorkerHandoffControl.Startup> target = handoff.SecondStarting.Task;
                    await CoordinatorScenarioRun.RequireColdRampTargetAsync(target, run, eligibleAt)
                        .ConfigureAwait(false);
                    ObservedManualClock.ObservedPoll wake = await state.ObserveRampProbePollAsync(
                        target, run, eligibleAt, registeredAfter: 0, token
                    ).ConfigureAwait(false);
                    try
                    {
                        state.Clock.Advance(wake, state.WorkDeadline, token, through: probe);
                    }
                    catch (Exception error) when (error is InvalidOperationException
                        or OperationCanceledException)
                    {
                        await CoordinatorScenarioRun
                            .RequireColdRampTargetAsync(target, run, eligibleAt)
                            .ConfigureAwait(false);
                        throw;
                    }
                    ObservedManualClock.ObservedPoll eligiblePoll = await state
                        .ObserveRampProbePollAsync(
                        target, run, eligibleAt, wake.TimerId, token
                    ).ConfigureAwait(false);
                    await CoordinatorScenarioRun.RequireColdRampTargetAsync(target, run, eligibleAt)
                        .ConfigureAwait(false);
                    Assert.AreEqual(probe, state.Clock.GetUtcNow());
                    AssertColdPeer();
                    state.Clock.Advance(
                        eligiblePoll, state.WorkDeadline, token, through: eligibleAt
                    );
                    using CancellationTokenSource observation =
                        CancellationTokenSource.CreateLinkedTokenSource(token);
                    Task<ObservedManualClock.ObservedPoll> freshPoll = state.Clock.WaitForPollAsync(
                        observation.Token, state.WorkDeadline,
                        registeredAfter: eligiblePoll.TimerId
                    );
                    try
                    {
                        await Task.WhenAny(target, run, freshPoll)
                            .WaitAsync(token).ConfigureAwait(false);
                        if (target.IsCompleted)
                        {
                            eligibleStartup = await target.ConfigureAwait(false);
                        }
                        else if (run.IsCompleted)
                        {
                            Assert.Fail(
                                "Run ended at original cadence eligibility: "
                                + (await run.ConfigureAwait(false)).ToJsonString()
                            );
                        }
                        else
                        {
                            _ = await freshPoll.ConfigureAwait(false);
                            Assert.Fail(
                                "A fresh eligible decision rearmed progress without starting "
                                + "worker2 after planning had already released it."
                            );
                        }
                    }
                    finally
                    {
                        await observation.CancelAsync().ConfigureAwait(false);
                        try
                        {
                            await freshPoll.ConfigureAwait(false);
                        }
                        catch (OperationCanceledException) when (
                            observation.IsCancellationRequested
                        )
                        {
                        }
                    }
                    Assert.AreEqual(eligibleAt, state.Clock.GetUtcNow());
                }
                // Zero/shared is incremental too, but already interval-eligible.
                // Its integrated release MUST actually dispatch before timeout.
                DateTimeOffset releaseCutoff =
                    integrate && interval == 0 ? planningDeadline : state.WorkDeadline;
                WorkerHandoffControl.Startup startup;
                if (eligibleStartup is not null)
                {
                    startup = eligibleStartup;
                }
                else if (timeoutStartupPoll is { } startupPoll)
                {
                    // Release is already committed. Only the next eligibility
                    // poll may advance time; native startup gets fixed-clock I/O.
                    releaseCutoff = planningDeadline.AddSeconds(1);
                    Task<WorkerHandoffControl.Startup> targetStart = handoff.SecondStarting.Task;
                    if (!targetStart.IsCompleted && !state.Active.IsCompleted)
                    {
                        try
                        {
                            state.Clock.Advance(startupPoll, releaseCutoff, token);
                        }
                        catch (InvalidOperationException) when (
                            targetStart.IsCompleted || state.Active.IsCompleted
                        )
                        {
                            // Report the actual startup or run result if it retired the poll.
                        }
                    }
                    startup = await state
                        .FixedAsync(next => targetStart.WaitAsync(next), token)
                        .ConfigureAwait(false);
                }
                else
                {
                    // The integrated controls still drive registered progress to
                    // the actual startup without requiring synchronous admission.
                    await state.UntilAsync(handoff.SecondStarting.Task, releaseCutoff, token)
                        .ConfigureAwait(false);
                    startup = await handoff.SecondStarting.Task
                        .WaitAsync(token).ConfigureAwait(false);
                }
                Assert.AreEqual("worker2", startup.Worker);
                Assert.AreEqual(state.Reservation("worker2").Id, startup.ReservationId);
                Assert.AreEqual(state.WorkerPath("worker2"), startup.Cwd);
                DateTimeOffset startupCapturedAt = state.Clock.GetUtcNow();
                await state.FixedAsync(startup.Operation, token).ConfigureAwait(false);
                Assert.AreEqual(startupCapturedAt, state.Clock.GetUtcNow());
                DateTimeOffset enteredAt = await state
                    .FixedAsync(next => followerEntry.Task.WaitAsync(next), token)
                    .ConfigureAwait(false);
                Assert.IsGreaterThanOrEqualTo(eligibleAt, enteredAt);
                Assert.IsLessThan(releaseCutoff, enteredAt);
                if (!integrate)
                {
                    Assert.IsGreaterThanOrEqualTo(planningDeadline, enteredAt);
                    Assert.IsEmpty(IntegratedChanges(state, token));
                    if (!reject)
                    {
                        Assert.IsEmpty(AllChanges(state, token));
                    }
                }
                await state
                    .WaitDurableAsync(
                        () =>
                            state.Number(
                                "SELECT COUNT(*) FROM delivery_batches "
                                    + "WHERE recipient='worker2' AND state='sending';"
                            ) == 1,
                        releaseCutoff,
                        token
                    )
                    .ConfigureAwait(false);
                long followerBatch = state.SendingBatch("worker2");
                if (timeoutKickoff is { } kickoff)
                {
                    Assert.AreEqual(
                        followerBatch,
                        state.Value("SELECT batch_id FROM events WHERE id=$id;", ("$id", kickoff))
                    );
                }
                DateTimeOffset claimedAt = state.Clock.GetUtcNow();
                ControlledSessionPool.Turn peer = await state
                    .FixedAsync(next => state.Sessions.NextTurnAsync("worker2", next), token)
                    .ConfigureAwait(false);
                Assert.AreEqual(claimedAt, state.Clock.GetUtcNow());
                Assert.IsLessThan(releaseCutoff, claimedAt);
                Assert.AreEqual(
                    TimeSpan.FromSeconds(state.Database.Configuration.DurationSeconds),
                    peer.Timeout
                );
                Assert.IsFalse(peer.Response.IsCompleted);
                Assert.IsNotNull(followerIdentity);
                Assert.AreEqual(
                    followerIdentity["sessionId"]!.GetValue<string>(),
                    peer.Session.SessionId
                );
                Assert.AreEqual(
                    followerIdentity["runtimeId"]!.GetValue<string>(),
                    peer.Session.Metadata!["runtimeId"]!.GetValue<string>()
                );
                Assert.AreEqual(
                    followerIdentity["leaseId"]!.GetValue<long>(),
                    peer.Session.Metadata["runtimeLeaseId"]!.GetValue<long>()
                );
                Assert.IsNull(peer.Session.Metadata["pid"]);
                Assert.IsNull(peer.Session.Metadata["birthTicks"]);
                Assert.IsGreaterThan(0L, state.Dispatches(startup.ReservationId));
                Assert.AreEqual(
                    state.Reservation("worker2").Id,
                    followerIdentity["reservationId"]!.GetValue<string>()
                );
                Assert.AreEqual(100m, followerIdentity["backedAllowance"]!.GetValue<decimal>());
                Assert.AreEqual(100d, peer.Session.Metadata["appliedLimit"]!.GetValue<double>());
                Assert.IsTrue(state.Reservation("worker2").GrantOpen);
                Assert.DoesNotContain("You are the initial planner", peer.Text);
                Assert.Contains("Planner release", peer.Text);
                Assert.AreNotEqual(planner.Session.SessionId, peer.Session.SessionId);

                // End this journey at the real work deadline; the already-dispatched
                // turns finish inside grace, without a fixture-owned control watcher.
                await ObserveLivePlannerPollAsync("the pre-drain progress poll")
                    .ConfigureAwait(false);
                state.Clock.Advance(state.Clock.WorkDeadline!.Value - state.Clock.GetUtcNow());
                planner.Complete();
                peer.Complete();
                await state
                    .WaitAckAsync(plannerBatch, state.DrainDeadline, token)
                    .ConfigureAwait(false);
                await state
                    .WaitAckAsync(followerBatch, state.DrainDeadline, token)
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
                Assert.AreEqual(10m, state.Reservation("worker1").Actual);
                Assert.AreEqual(10m, state.Reservation("worker2").Actual);
                Assert.IsFalse(state.Reservation("worker1").GrantOpen);
                Assert.IsFalse(state.Reservation("worker2").GrantOpen);
                Assert.AreEqual(
                    27.25m,
                    state.Ledger.Status(
                        state.Database.RunId,
                        state.Database.Configuration.RunAiCreditAllowance
                    )["runCommitted"]!.GetValue<decimal>()
                );

                long? PeerKickoff() =>
                    (long?)state.Value(
                        "SELECT id FROM events WHERE recipient='worker2' AND kind='kickoff';"
                    );

                Task<ObservedManualClock.ObservedPoll> ObservePlanningPollAsync(
                    long registeredAfter = 0
                ) =>
                    state.FixedAsync(
                        next => state.Clock.WaitForPollAsync(
                            next, planningDeadline, requireSingle: true,
                            registeredAfter: registeredAfter
                        ),
                        token
                    );

                async Task ObserveLivePlannerPollAsync(string phase)
                {
                    using CancellationTokenSource observation =
                        CancellationTokenSource.CreateLinkedTokenSource(token);
                    Task poll = state.Clock.WaitForPollAsync(observation.Token, state.WorkDeadline);
                    ExceptionDispatchInfo? primary = null;
                    try
                    {
                        await Task.WhenAny(producer, poll)
                            .WaitAsync(observation.Token)
                            .ConfigureAwait(false);
                        if (!producer.IsCompleted)
                        {
                            await poll.ConfigureAwait(false);
                        }
                    }
                    catch (Exception error)
                    {
                        primary = ExceptionDispatchInfo.Capture(error);
                    }

                    List<Exception> secondary = [];
                    try
                    {
                        observation.Cancel();
                    }
                    catch (Exception error)
                    {
                        secondary.Add(error);
                    }
                    try
                    {
                        await poll.ConfigureAwait(false);
                    }
                    catch (OperationCanceledException) when (observation.IsCancellationRequested)
                    {
                    }
                    catch (Exception error)
                    {
                        if (!ReferenceEquals(error, primary?.SourceException))
                        {
                            secondary.Add(error);
                        }
                    }

                    if (
                        producer.IsCompleted
                        && (primary is null
                            || (primary.SourceException is OperationCanceledException
                                && token.IsCancellationRequested))
                    )
                    {
                        JsonObject result = await producer.ConfigureAwait(false);
                        Assert.Fail($"Coordinator ended before {phase}: {result.ToJsonString()}");
                    }
                    primary?.Throw();
                    if (secondary.Count != 0)
                    {
                        throw new AggregateException(
                            "Planner poll observation cleanup failed.", secondary
                        );
                    }
                }

                void AssertColdPeer()
                {
                    Assert.IsFalse(handoff.SecondStarting.Task.IsCompleted);
                    Assert.IsFalse(followerEntry.Task.IsCompleted);
                    Assert.IsNull(followerIdentity);
                    Assert.IsNull(WorkerStatus(inspector, "worker2")["sessionId"]);
                    Assert.AreEqual(0m, state.Reservation("worker2").Allowance);
                    Assert.IsFalse(state.Reservation("worker2").GrantOpen);
                    Assert.AreEqual(0L, state.Dispatches(state.Reservation("worker2").Id));
                    Assert.IsEmpty(
                        state
                            .Sessions.Hosts.Where(host => host.Operation == "run")
                            .SelectMany(host => host.Sessions)
                            .Where(session => session.Worker == "worker2")
                    );
                }
            })
            .ConfigureAwait(false);
    }

    [TestMethod]
    [TestCategory("EndReasonAndDeliveryControl")]
    [DataRow("pause", "owner-pause", "paused")]
    [DataRow("finalize", "owner-finalize", "finalized")]
    [Timeout(120_000, CooperativeCancellation = true)]
    public async Task AlreadyRunningOwnerControlRetainsDeliveryAndTargetInstructions(
        string action,
        string reason,
        string lifecycle
    )
    {
        CancellationToken token = testContext.CancellationToken;
        CoordinatorScenarioRun state = await CoordinatorScenarioRun
            .CreateAsync(testContext, token, workers: 1, ownedHosts: false, realGit: true)
            .ConfigureAwait(false);
        await state
            .RunBodyAsync(async () =>
            {
                Coordinator inspector = state.Coordinator("status");
                byte[] instructions = File.ReadAllBytes(
                    Path.Combine(state.Git.Repository, "AGENTS.md")
                );
                // Owned disposable policy/credential-shaped files, never user home
                // credentials. Observe application-owned effects, not provider policy.
                string[] protectedFiles =
                [
                    Path.Combine(state.Git.Repository, ".github", "copilot-instructions.md"),
                    Path.Combine(state.Git.Repository, ".vscode", "settings.json"),
                    Path.Combine(state.Git.Repository, ".claude", "settings.json"),
                ];
                foreach (string file in protectedFiles)
                {
                    Directory.CreateDirectory(Path.GetDirectoryName(file)!);
                    File.WriteAllText(
                        file,
                        "{\"fixture\":\"owned-user-policy-not-a-credential\"}\n"
                    );
                }
                protectedFiles = [.. protectedFiles, TestAssembly.PolicyFile];
                Dictionary<string, byte[]> preserved = protectedFiles.ToDictionary(
                    file => file,
                    File.ReadAllBytes,
                    StringComparer.Ordinal
                );
                byte[] global = File.ReadAllBytes(state.Git.GlobalConfig);
                byte[] system = File.ReadAllBytes(state.Git.SystemConfig);
                _ = state.Start();
                ControlledSessionPool.Turn turn = await state
                    .FixedAsync(next => state.Sessions.NextTurnAsync("worker1", next), token)
                    .ConfigureAwait(false);
                Assert.Contains("Never overwrite AGENTS.md", turn.Text);
                Assert.Contains(
                    "Do not spawn nested agent teams or modify authentication",
                    turn.Text
                );
                long batch = state.SendingBatch("worker1");
                ObservedManualClock.ObservedPoll ownerPoll = await state
                    .ObserveOwnerPollAsync(token)
                    .ConfigureAwait(false);
                turn.Complete();
                await state.WaitAckAsync(batch, state.WorkDeadline, token).ConfigureAwait(false);
                GitScenarioState.Data(
                    await state
                        .StopRunningAsync(action, ownerPoll, token)
                        .ConfigureAwait(false)
                );
                JsonObject status = StatusData(inspector);
                Assert.AreEqual(reason, status["stopReason"]!.GetValue<string>());
                Assert.AreEqual(lifecycle, status["lifecycle"]!.GetValue<string>());
                Assert.AreEqual(
                    "Target instructions",
                    File.ReadAllLines(Path.Combine(state.Git.Repository, "AGENTS.md"))[0]
                        .TrimStart('#', ' ')
                );
                Assert.IsTrue(
                    instructions.SequenceEqual(
                        File.ReadAllBytes(Path.Combine(state.Git.Repository, "AGENTS.md"))
                    )
                );
                foreach ((string file, byte[] bytes) in preserved)
                {
                    Assert.IsTrue(bytes.SequenceEqual(File.ReadAllBytes(file)), file);
                }
                Assert.IsTrue(global.SequenceEqual(File.ReadAllBytes(state.Git.GlobalConfig)));
                Assert.IsTrue(system.SequenceEqual(File.ReadAllBytes(state.Git.SystemConfig)));
                foreach (
                    ControlledSessionPool.Session session in state
                        .Sessions.Hosts.SelectMany(host => host.Sessions)
                        .Where(session => session.HasSession)
                )
                {
                    Assert.AreEqual(
                        "controlled-model",
                        session.Metadata!["selectedModel"]!.GetValue<string>()
                    );
                    Assert.AreEqual(false, session.Metadata["isRemote"]!.GetValue<bool>());
                    Assert.IsTrue(
                        Directory.Exists(session.Metadata["workingDirectory"]!.GetValue<string>())
                    );
                }
                Assert.AreEqual(10m, state.Reservation("worker1").Actual);
                Assert.IsTrue(Directory.Exists(state.Database.StatePath));
                Assert.AreEqual(
                    state.Git.BaseCommit,
                    (
                        await state
                            .Git.GitAsync(state.Git.Repository, token, "rev-parse", "HEAD")
                            .ConfigureAwait(false)
                    ).Trim()
                );
                string ownedWorktree = state.WorkerPath("worker1");
                string unrelated = Path.Combine(
                    state.Database.StatePath,
                    "worktrees",
                    "unregistered-keep.txt"
                );
                File.WriteAllText(
                    unrelated,
                    "Owned fixture sentinel outside the recorded resource set.\n"
                );
                string financial = state
                    .Ledger.Status(state.Database.RunId, 207.25m)
                    .ToJsonString();
                string worktreeHead = await state
                    .Git.GitAsync(ownedWorktree, token, "rev-parse", "HEAD")
                    .ConfigureAwait(false);
                string refs = await state
                    .Git.GitAsync(state.Git.Repository, token, "show-ref")
                    .ConfigureAwait(false);
                JsonObject cleanupResult = await state
                    .Coordinator()
                    .CleanupAsync(token)
                    .ConfigureAwait(false);
                if (action == "pause")
                {
                    Assert.IsFalse(cleanupResult["ok"]!.GetValue<bool>());
                    Assert.AreEqual("conflict", cleanupResult["code"]!.GetValue<string>());
                    Assert.AreEqual(
                        "Inspect and finalize retained delivery before explicit cleanup.",
                        cleanupResult["message"]!.GetValue<string>()
                    );
                    JsonObject pausedStatus = StatusData(inspector);
                    Assert.AreEqual("paused", pausedStatus["lifecycle"]!.GetValue<string>());
                    Assert.IsNull(pausedStatus["delivery"]);
                    Assert.AreEqual(ownedWorktree, state.WorkerPath("worker1"));
                    Assert.IsTrue(Directory.Exists(ownedWorktree));
                    Assert.AreEqual(
                        worktreeHead,
                        await state
                            .Git.GitAsync(ownedWorktree, token, "rev-parse", "HEAD")
                            .ConfigureAwait(false)
                    );
                    Assert.AreEqual(
                        refs,
                        await state
                            .Git.GitAsync(state.Git.Repository, token, "show-ref")
                            .ConfigureAwait(false)
                    );
                }
                else
                {
                    JsonObject cleanup = GitScenarioState.Data(cleanupResult);
                    Assert.IsNotEmpty(cleanup["removed"]!.AsArray());
                    Assert.IsFalse(Directory.Exists(ownedWorktree));
                    Assert.IsNotNull(StatusData(inspector)["delivery"]);
                }
                Assert.AreEqual(
                    "Owned fixture sentinel outside the recorded resource set.\n",
                    File.ReadAllText(unrelated)
                );
                Assert.IsTrue(
                    instructions.SequenceEqual(
                        File.ReadAllBytes(Path.Combine(state.Git.Repository, "AGENTS.md"))
                    )
                );
                foreach ((string file, byte[] bytes) in preserved)
                {
                    Assert.IsTrue(bytes.SequenceEqual(File.ReadAllBytes(file)), file);
                }
                Assert.AreEqual(
                    financial,
                    state.Ledger.Status(state.Database.RunId, 207.25m).ToJsonString()
                );
                Assert.IsTrue(global.SequenceEqual(File.ReadAllBytes(state.Git.GlobalConfig)));
                Assert.IsTrue(system.SequenceEqual(File.ReadAllBytes(state.Git.SystemConfig)));
            })
            .ConfigureAwait(false);
    }

    [TestMethod]
    [TestCategory("EndReasonAndDeliveryControl")]
    [Timeout(120_000, CooperativeCancellation = true)]
    public async Task PeerTaskClosureDoesNotClosePeerWorkOrTheRun()
    {
        CancellationToken token = testContext.CancellationToken;
        CoordinatorScenarioRun state = await CoordinatorScenarioRun
            .CreateAsync(testContext, token, ownedHosts: false)
            .ConfigureAwait(false);
        await state
            .RunBodyAsync(async () =>
            {
                Coordinator inspector = state.Coordinator("status");
                _ = state.Start();
                ControlledSessionPool.Turn one = await state
                    .FixedAsync(next => state.Sessions.NextTurnAsync("worker1", next), token)
                    .ConfigureAwait(false);
                long ownBatch = state.SendingBatch("worker1");
                ControlledSessionPool.Turn two = await state
                    .FixedAsync(next => state.Sessions.NextTurnAsync("worker2", next), token)
                    .ConfigureAwait(false);
                JsonObject own = state.Invoke(
                    "worker1",
                    "task_write",
                    new()
                    {
                        ["action"] = "create",
                        ["title"] = "Own parser work",
                        ["body"] = "Separate from peer work.",
                    }
                );
                JsonObject peer = state.Invoke(
                    "worker2",
                    "task_write",
                    new()
                    {
                        ["action"] = "create",
                        ["title"] = "Open renderer work",
                        ["body"] = "Must remain open.",
                    }
                );
                long peerTaskId = peer["id"]!.GetValue<long>();
                state.Invoke(
                    "worker1",
                    "task_write",
                    new() { ["action"] = "close", ["id"] = own["id"]!.GetValue<long>() }
                );
                await state.StepAsync(state.WorkDeadline, token).ConfigureAwait(false);
                Assert.AreEqual(
                    "open",
                    state.Invoke("owner", "task_read", new() { ["id"] = peerTaskId })[
                        "state"
                    ]!.GetValue<string>()
                );
                JsonObject runningStatus = StatusData(inspector);
                Assert.AreEqual("running", runningStatus["lifecycle"]!.GetValue<string>());
                Assert.IsNull(runningStatus["stopReason"]);
                Assert.IsFalse(one.Token.IsCancellationRequested);
                Assert.IsFalse(two.Token.IsCancellationRequested);

                JsonObject closedRoot = state.Invoke(
                    "worker1",
                    "task_write",
                    new() { ["action"] = "close", ["id"] = 1 }
                );
                Assert.AreEqual("closed", closedRoot["state"]!.GetValue<string>());
                Assert.AreEqual("worker1", closedRoot["closedBy"]!.GetValue<string>());
                await state
                    .UntilAsync(
                        () =>
                            state.Invoke("owner", "task_read", new() { ["id"] = 1 })[
                                "state"
                            ]!.GetValue<string>() == "open",
                        state.WorkDeadline,
                        token
                    )
                    .ConfigureAwait(false);
                JsonObject reopenedRoot = state.Invoke("owner", "task_read", new() { ["id"] = 1 });
                Assert.AreEqual("open", reopenedRoot["state"]!.GetValue<string>());
                Assert.AreEqual("worker1", reopenedRoot["closedBy"]!.GetValue<string>());
                Assert.IsNull(StatusData(inspector)["stopReason"]);
                Assert.IsFalse(one.Token.IsCancellationRequested);
                Assert.IsFalse(two.Token.IsCancellationRequested);

                long peerBatch = state.SendingBatch("worker2");
                two.Complete();
                await state.WaitAckAsync(peerBatch, state.WorkDeadline, token)
                    .ConfigureAwait(false);
                ControlledSessionPool.Turn followup = await state
                    .FixedAsync(next => state.Sessions.NextTurnAsync("worker2", next), token)
                    .ConfigureAwait(false);
                long followupBatch = state.SendingBatch("worker2");
                Assert.AreEqual(two.Session.SessionId, followup.Session.SessionId);
                JsonObject reopenedEvent = PromptEvents(followup.Text)
                    .Single(item => item["kind"]!.GetValue<string>() == "task_reopened");
                Assert.AreEqual(1L, reopenedEvent["payload"]!["taskId"]!.GetValue<long>());
                Assert.AreEqual(
                    "worker1",
                    reopenedEvent["payload"]!["closedBy"]!.GetValue<string>()
                );
                Assert.AreEqual(1, two.Session.CompletedTurns);
                Assert.AreEqual(
                    "open",
                    state.Invoke("owner", "task_read", new() { ["id"] = peerTaskId })[
                        "state"
                    ]!.GetValue<string>()
                );

                state.Invoke("owner", "task_write", new() { ["action"] = "close", ["id"] = 1 });
                await state
                    .UntilAsync(state.Handoff!.OwnerStopped.Task, state.WorkDeadline, token)
                    .ConfigureAwait(false);
                await state.Active.WaitAsync(token).ConfigureAwait(false);
                JsonObject result = await state.Active.ConfigureAwait(false);
                Assert.IsFalse(result["ok"]!.GetValue<bool>());
                Assert.AreEqual(
                    "ownership_or_usage_unresolved",
                    result["code"]!.GetValue<string>()
                );
                JsonObject finalStatus = StatusData(inspector);
                Assert.AreEqual("unresolved", finalStatus["lifecycle"]!.GetValue<string>());
                Assert.AreEqual(
                    "owner-root-closure",
                    finalStatus["stopReason"]!.GetValue<string>()
                );
                Assert.IsFalse(finalStatus["active"]!.GetValue<bool>());
                Assert.IsNull(finalStatus["delivery"]);
                JsonObject ownerClosedRoot = state.Invoke(
                    "owner",
                    "task_read",
                    new() { ["id"] = 1 }
                );
                Assert.AreEqual("closed", ownerClosedRoot["state"]!.GetValue<string>());
                Assert.AreEqual("owner", ownerClosedRoot["closedBy"]!.GetValue<string>());
                Assert.IsTrue(one.Token.IsCancellationRequested);
                Assert.IsTrue(followup.Token.IsCancellationRequested);
                foreach (long heldBatch in new[] { ownBatch, followupBatch })
                {
                    Assert.AreEqual(
                        "failed",
                        state.Value(
                            "SELECT state FROM delivery_batches WHERE id=$id;",
                            ("$id", heldBatch)
                        )
                    );
                }
                Assert.AreEqual(0, one.Session.CompletedTurns);
                Assert.AreEqual(1, followup.Session.CompletedTurns);
                Assert.AreEqual("uncertain", state.Reservation("worker1").State);
                Assert.IsNull(state.Reservation("worker1").Actual);
                Assert.AreEqual("uncertain", state.Reservation("worker2").State);
                Assert.AreEqual(10m, state.Reservation("worker2").Actual);
            })
            .ConfigureAwait(false);
    }

    [TestMethod]
    [TestCategory("ExactFreshAndResumeLabels")]
    [DataRow(false)]
    [DataRow(true)]
    [Timeout(120_000, CooperativeCancellation = true)]
    public async Task GenuineExactLoadFailureFallsBackOnlyWithFreshBacking(bool minimumBacking)
    {
        CancellationToken token = testContext.CancellationToken;
        InterruptedRunCreator creator = await InterruptedRunCreator
            .OpenAsync(testContext, minimumBacking, token)
            .ConfigureAwait(false);
        await creator
            .RunOwnedAsync(async () =>
            {
                CoordinatorScenarioRun state = CoordinatorScenarioRun.Attach(creator, token);
                await state
                    .RunBodyAsync(async () =>
                    {
                        string originalSession = creator.SessionId;
                        string originalReservation = creator.ReservationId;
                        string originalJournal = OwnedCreatorProcess.Hash(creator.JournalPath);
                        Assert.AreEqual(
                            "true",
                            creator.Checkpoint["confirmation"]!.GetValue<string>()
                        );
                        Assert.AreEqual(
                            minimumBacking ? 30m : 200m,
                            state.Reservation("worker1").Allowance
                        );
                        Assert.AreEqual(10m, state.Reservation("worker1").Actual);
                        await creator.CrashAsync(token).ConfigureAwait(false);
                        Coordinator inspector = state.Coordinator("status");
                        bool exactLoadEntered = false;
                        state.Sessions.BeforeOpen = (session, next) =>
                        {
                            next.ThrowIfCancellationRequested();
                            if (session.ExactSession == originalSession)
                            {
                                exactLoadEntered = true;
                                throw new OperationException(
                                    "session_load_failed",
                                    "Controlled original session load is unavailable."
                                );
                            }
                            return Task.CompletedTask;
                        };
                        _ = state.Start("reconnect");
                        if (minimumBacking)
                        {
                            await state.Active.WaitAsync(token).ConfigureAwait(false);
                            JsonObject denied = await state.Active.ConfigureAwait(false);
                            Assert.IsFalse(denied["ok"]!.GetValue<bool>());
                            Assert.AreEqual("credit_exhausted", denied["code"]!.GetValue<string>());
                            JsonObject status = StatusData(inspector);
                            Assert.AreEqual("stopped", status["lifecycle"]!.GetValue<string>());
                            Assert.AreEqual(
                                "credit_exhausted",
                                status["stopReason"]!.GetValue<string>()
                            );
                            Assert.IsFalse(status["active"]!.GetValue<bool>());
                            Assert.HasCount(2, state.Ledger.Reservations(state.Database.RunId));
                            Assert.AreEqual(originalReservation, state.Reservation("worker1").Id);
                            Assert.AreEqual("reconciled", state.Reservation("worker1").State);
                            Assert.AreEqual(10m, state.Reservation("worker1").Actual);
                            Assert.AreEqual(1L, state.Dispatches(originalReservation));
                            Assert.IsEmpty(
                                state
                                    .Sessions.Hosts.SelectMany(host => host.Sessions)
                                    .Where(session => session.HasSession)
                            );
                            Assert.AreEqual(
                                originalJournal,
                                OwnedCreatorProcess.Hash(creator.JournalPath)
                            );
                        }
                        else
                        {
                            ControlledSessionPool.Turn fallback = await state
                                .FixedAsync(
                                    next => state.Sessions.NextTurnAsync("worker1", next),
                                    token
                                )
                                .ConfigureAwait(false);
                            CreditReservation replacement = state.Reservation("worker1");
                            JsonObject workerStatus = WorkerStatus(inspector, "worker1");
                            Assert.AreNotEqual(originalSession, fallback.Session.SessionId);
                            Assert.AreNotEqual(originalReservation, replacement.Id);
                            Assert.AreEqual(190m, replacement.Allowance);
                            Assert.IsNull(replacement.Actual);
                            Assert.AreEqual(
                                fallback.Session.SessionId,
                                workerStatus["sessionId"]!.GetValue<string>()
                            );
                            Assert.AreEqual(
                                "fresh-summary-fallback: exact-session load unavailable or failed",
                                workerStatus["recovery"]!.GetValue<string>()
                            );
                            Assert.Contains(
                                "Goal: " + state.Database.Configuration.Goal,
                                fallback.Text
                            );
                            Assert.Contains("==== DURABLE CHANGES ====", fallback.Text);
                            JsonObject fresh = state.Sessions.Journal!.Snapshot(
                                fallback.Session.SessionId
                            );
                            Assert.AreEqual(
                                0d,
                                fresh["generations"]![0]!["openingNano"]!.GetValue<double>()
                            );
                            Assert.AreEqual(
                                0L,
                                fresh["generations"]![0]!["openingRequests"]!.GetValue<long>()
                            );
                            Assert.IsNull(fresh["generations"]![0]!["exactSession"]);
                            long batch = state.SendingBatch("worker1");
                            ObservedManualClock.ObservedPoll ownerPoll = await state
                                .ObserveOwnerPollAsync(token)
                                .ConfigureAwait(false);
                            fallback.Complete();
                            await state
                                .WaitAckAsync(batch, state.WorkDeadline, token)
                                .ConfigureAwait(false);
                            Assert.AreEqual(
                                10m,
                                state
                                    .Ledger.Reservations(state.Database.RunId)
                                    .Single(row => row.Id == originalReservation)
                                    .Actual
                            );
                            Assert.AreEqual(1L, state.Dispatches(originalReservation));
                            Assert.AreEqual(1L, state.Dispatches(replacement.Id));
                            GitScenarioState.Data(
                                await state
                                    .StopRunningAsync("pause", ownerPoll, token)
                                    .ConfigureAwait(false)
                            );
                            JsonObject pausedStatus = StatusData(inspector);
                            Assert.AreEqual(
                                "paused",
                                pausedStatus["lifecycle"]!.GetValue<string>()
                            );
                            Assert.AreEqual(
                                "owner-pause",
                                pausedStatus["stopReason"]!.GetValue<string>()
                            );
                            Assert.AreEqual(10m, state.Reservation("worker1").Actual);
                            Assert.AreEqual(
                                27.25m,
                                state.Ledger.Status(
                                    state.Database.RunId,
                                    state.Database.Configuration.RunAiCreditAllowance
                                )["runCommitted"]!.GetValue<decimal>()
                            );
                        }
                        Assert.IsTrue(exactLoadEntered, "Reconnect did not enter the exact load.");
                        Assert.IsFalse(
                            state.Sessions.Journal!.Snapshot(originalSession)[
                                "pending"
                            ]!.GetValue<bool>()
                        );
                        Assert.AreEqual(
                            1L,
                            state.Sessions.Journal.Snapshot(originalSession)[
                                "requests"
                            ]!.GetValue<long>()
                        );
                    })
                    .ConfigureAwait(false);
            })
            .ConfigureAwait(false);
    }

    [TestMethod]
    [TestCategory("ExactFreshAndResumeLabels")]
    [Timeout(120_000, CooperativeCancellation = true)]
    public async Task ExplicitResumeCreatesNewSessionSegmentAndDurableBootstrap()
    {
        CancellationToken token = testContext.CancellationToken;
        CoordinatorScenarioRun state = await CoordinatorScenarioRun
            .CreateAsync(testContext, token, workers: 1, ownedHosts: false, realGit: true)
            .ConfigureAwait(false);
        await state
            .RunBodyAsync(async () =>
            {
                Coordinator inspector = state.Coordinator("status");
                _ = state.Start();
                ControlledSessionPool.Turn original = await state
                    .FixedAsync(next => state.Sessions.NextTurnAsync("worker1", next), token)
                    .ConfigureAwait(false);
                string retainedHead = await state.Git.CommitAsync(
                    state.WorkerPath("worker1"),
                    "retained-resume.txt",
                    "Retained open change.\n",
                    token
                ).ConfigureAwait(false);
                JsonObject retained = await state.GitToolAsync(
                    "worker1",
                    "change_write",
                    new()
                    {
                        ["action"] = "submit",
                        ["title"] = "Retained resume witness",
                        ["body"] = "Keep this open change across pause.",
                    },
                    token
                ).ConfigureAwait(false);
                long retainedId = retained["id"]!.GetValue<long>();
                long originalBatch = state.SendingBatch("worker1");
                original.Complete();
                await state
                    .WaitAckAsync(originalBatch, state.WorkDeadline, token)
                    .ConfigureAwait(false);
                ControlledSessionPool.Turn changeNotice = await state
                    .FixedAsync(next => state.Sessions.NextTurnAsync("worker1", next), token)
                    .ConfigureAwait(false);
                Assert.Contains("Retained resume witness", changeNotice.Text);
                long changeBatch = state.SendingBatch("worker1");
                ObservedManualClock.ObservedPoll originalOwnerPoll = await state
                    .ObserveOwnerPollAsync(token)
                    .ConfigureAwait(false);
                changeNotice.Complete();
                await state
                    .WaitAckAsync(changeBatch, state.WorkDeadline, token)
                    .ConfigureAwait(false);
                GitScenarioState.Data(
                    await state
                        .StopRunningAsync("pause", originalOwnerPoll, token)
                        .ConfigureAwait(false)
                );
                CreditReservation old = state.Reservation("worker1");
                state.Invoke(
                    "owner",
                    "message_send",
                    new()
                    {
                        ["recipient"] = "worker1",
                        ["body"] = "Retained decision for the next segment.",
                    }
                );
                _ = state.Start("resume");
                ControlledSessionPool.Turn resumed = await state
                    .FixedAsync(next => state.Sessions.NextTurnAsync("worker1", next), token)
                    .ConfigureAwait(false);
                JsonObject workerStatus = WorkerStatus(inspector, "worker1");
                Assert.AreNotEqual(original.Session.SessionId, resumed.Session.SessionId);
                Assert.AreEqual(
                    resumed.Session.SessionId,
                    workerStatus["sessionId"]!.GetValue<string>()
                );
                Assert.AreEqual(
                    "fresh-session-with-durable-summary",
                    workerStatus["recovery"]!.GetValue<string>()
                );
                const string changesHeading = "==== DURABLE CHANGES ====\n";
                int changesStart = resumed.Text.IndexOf(changesHeading, StringComparison.Ordinal);
                Assert.IsGreaterThan(-1, changesStart);
                changesStart += changesHeading.Length;
                int changesEnd = resumed.Text.IndexOf(
                    "\n\n==== EVENTS ====\n", changesStart, StringComparison.Ordinal
                );
                Assert.IsGreaterThan(changesStart, changesEnd);
                JsonObject changes = Assert.IsInstanceOfType<JsonObject>(
                    JsonNode.Parse(resumed.Text[changesStart..changesEnd])
                );
                JsonObject retainedSummary = Assert.ContainsSingle(
                    changes["data"]!["items"]!.AsArray().OfType<JsonObject>()
                        .Where(item => item["id"]!.GetValue<long>() == retainedId)
                );
                Assert.AreEqual(retainedHead, retainedSummary["head"]!.GetValue<string>());
                Assert.AreEqual(
                    "Retained resume witness", retainedSummary["title"]!.GetValue<string>()
                );
                Assert.AreEqual("open", retainedSummary["state"]!.GetValue<string>());
                Assert.Contains("Retained decision for the next segment.", resumed.Text);
                Assert.Contains("Goal: " + state.Database.Configuration.Goal, resumed.Text);
                Assert.AreEqual(
                    20m,
                    state
                        .Ledger.Reservations(state.Database.RunId)
                        .Single(row => row.Id == old.Id)
                        .Actual
                );
                Assert.AreEqual(180m, state.Reservation("worker1").Allowance);
                long resumedBatch = state.SendingBatch("worker1");
                ObservedManualClock.ObservedPoll resumedOwnerPoll = await state
                    .ObserveOwnerPollAsync(token)
                    .ConfigureAwait(false);
                resumed.Complete();
                await state
                    .WaitAckAsync(resumedBatch, state.WorkDeadline, token)
                    .ConfigureAwait(false);
                GitScenarioState.Data(
                    await state
                        .StopRunningAsync("pause", resumedOwnerPoll, token)
                        .ConfigureAwait(false)
                );
                string settledStatus = StatusData(inspector).ToJsonString();
                string settledFinancial = state
                    .Ledger.Status(
                        state.Database.RunId,
                        state.Database.Configuration.RunAiCreditAllowance
                    )
                    .ToJsonString();
                JsonObject ineligible = await state
                    .Coordinator("reconnect")
                    .RunAsync("reconnect", token)
                    .ConfigureAwait(false);
                Assert.IsFalse(ineligible["ok"]!.GetValue<bool>());
                Assert.AreEqual("conflict", ineligible["code"]!.GetValue<string>());
                Assert.Contains("unfinished segment", ineligible["message"]!.GetValue<string>());
                Assert.AreEqual(settledStatus, StatusData(inspector).ToJsonString());
                Assert.AreEqual(
                    settledFinancial,
                    state
                        .Ledger.Status(
                            state.Database.RunId,
                            state.Database.Configuration.RunAiCreditAllowance
                        )
                        .ToJsonString()
                );
            })
            .ConfigureAwait(false);
    }

    private enum PlannerCandidate
    {
        None,
        Passing,
        Rejected,
    }

    private static JsonObject StatusData(Coordinator inspector) =>
        GitScenarioState.Data(inspector.Status());

    private static JsonObject WorkerStatus(Coordinator inspector, string worker) =>
        StatusData(inspector)["workers"]!
            .AsArray()
            .OfType<JsonObject>()
            .Single(item => item["id"]!.GetValue<string>() == worker);

    private static JsonArray AllChanges(CoordinatorScenarioRun state, CancellationToken token) =>
        state
            .GitToolAsync("owner", "change_list", new(), token)
            .GetAwaiter()
            .GetResult()["items"]!
            .AsArray();

    private static JsonArray IntegratedChanges(
        CoordinatorScenarioRun state,
        CancellationToken token
    ) =>
        state
            .GitToolAsync("owner", "change_list", new() { ["state"] = "integrated" }, token)
            .GetAwaiter()
            .GetResult()["items"]!
            .AsArray();

    private static JsonObject[] PromptEvents(string prompt)
    {
        const string separator = "==== EVENTS ====\n";
        int index = prompt.LastIndexOf(separator, StringComparison.Ordinal);
        Assert.IsGreaterThanOrEqualTo(0, index);
        return JsonNode.Parse(prompt[(index + separator.Length)..])!["events"]!
            .AsArray()
            .OfType<JsonObject>()
            .ToArray();
    }

    private static async Task AssertRejectedPlannerCandidateAsync(
        CoordinatorScenarioRun state,
        JsonObject submitted,
        JsonObject queued,
        CancellationToken token
    )
    {
        long changeId = submitted["id"]!.GetValue<long>();
        string head = submitted["head"]!.GetValue<string>();
        long requestId = queued["requestId"]!.GetValue<long>();
        Assert.AreEqual("pending", queued["state"]!.GetValue<string>());
        JsonObject retained = await state.GitToolAsync(
            "worker1", "change_read", new() { ["id"] = changeId }, token
        ).ConfigureAwait(false);
        Assert.AreEqual("open", retained["state"]!.GetValue<string>());
        JsonObject approval = Assert.ContainsSingle(retained["reviews"]!.AsArray())!.AsObject();
        Assert.AreEqual("worker2", approval["actor"]!.GetValue<string>());
        Assert.AreEqual(head, approval["head"]!.GetValue<string>());
        Assert.AreEqual("approve", approval["decision"]!.GetValue<string>());

        JsonObject failed = Assert.ContainsSingle(retained["integrationRequests"]!.AsArray())!
            .AsObject();
        Assert.AreEqual(requestId, failed["requestId"]!.GetValue<long>());
        Assert.AreEqual(changeId, failed["changeId"]!.GetValue<long>());
        Assert.AreEqual("worker1", failed["actor"]!.GetValue<string>());
        Assert.AreEqual(head, failed["head"]!.GetValue<string>());
        Assert.AreEqual("failed", failed["state"]!.GetValue<string>());
        Assert.AreEqual("check_failed", failed["resultCode"]!.GetValue<string>());
        Assert.IsNull(failed["integratedCommit"]);

        JsonObject check = Assert.ContainsSingle(retained["checks"]!.AsArray())!.AsObject();
        Assert.AreEqual(changeId, check["changeId"]!.GetValue<long>());
        Assert.AreEqual("candidate", check["kind"]!.GetValue<string>());
        Assert.AreEqual("failed", check["status"]!.GetValue<string>());
        Assert.AreEqual(1, check["exitCode"]!.GetValue<int>());
        // This fixture changes only the planner branch, so its reviewed tree is
        // the expected content of the merged candidate.
        string reviewedTree = await state.Git.ResolveAsync(head + "^{tree}", token)
            .ConfigureAwait(false);
        Assert.AreEqual(reviewedTree, check["tree"]!.GetValue<string>());
        Assert.AreEqual(
            state.Git.BaseCommit,
            await state.Git.ResolveAsync(
                $"refs/heads/lite-agensh/{state.Database.RunId}/integration",
                token
            ).ConfigureAwait(false)
        );
    }

    private static async Task<(JsonObject Change, JsonObject Request)> SubmitReviewedAsync(
        CoordinatorScenarioRun state,
        string author,
        string reviewer,
        string file,
        string content,
        CancellationToken token
    )
    {
        string head = await state
            .Git.CommitAsync(state.WorkerPath(author), file, content, token)
            .ConfigureAwait(false);
        JsonObject change = await state
            .GitToolAsync(
                author,
                "change_write",
                new()
                {
                    ["action"] = "submit",
                    ["title"] = file,
                    ["body"] = "Independent peer-selected change.",
                },
                token
            )
            .ConfigureAwait(false);
        Assert.AreEqual(head, change["head"]!.GetValue<string>());
        _ = await state
            .GitToolAsync(
                reviewer,
                "change_review",
                new()
                {
                    ["id"] = change["id"]!.GetValue<long>(),
                    ["head"] = head,
                    ["decision"] = "approve",
                    ["body"] = "Review of this exact candidate head.",
                },
                token
            )
            .ConfigureAwait(false);
        JsonObject request = await state
            .GitToolAsync(
                author,
                "change_write",
                new()
                {
                    ["action"] = "integrate",
                    ["id"] = change["id"]!.GetValue<long>(),
                    ["head"] = head,
                },
                token
            )
            .ConfigureAwait(false);
        return (change, request);
    }
}
