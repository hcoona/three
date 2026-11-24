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
        Phase7Run state = await Phase7Run.CreateAsync(testContext, token).ConfigureAwait(false);
        await state
            .RunBodyAsync(async () =>
            {
                _ = state.Start();
                Phase7Sessions.Turn first = await state
                    .FixedAsync(next => state.Sessions.NextTurnAsync("worker1", next), token)
                    .ConfigureAwait(false);
                Phase7Sessions.Turn second = await state
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
                Assert.AreEqual(
                    2L,
                    state.Number(
                        (
                            "SELECT COUNT(DISTINCT actor) FROM board_history WHERE type='CLAIM' "
                            + "AND content=$content;"
                        ),
                        ("$content", $"Cooperating on #{taskId}")
                    )
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
                        () =>
                            state.Number("SELECT COUNT(*) FROM changes WHERE state='integrated';")
                            == 2,
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
                    Assert.IsGreaterThan(
                        0L,
                        state.Number(
                            (
                                "SELECT COUNT(*) FROM checks WHERE change_id=$id AND "
                                + "status='passed' AND exit_code=0;"
                            ),
                            ("$id", change["id"]!.GetValue<long>())
                        )
                    );
                    Assert.AreEqual(
                        1L,
                        state.Number(
                            (
                                "SELECT COUNT(*) FROM reviews WHERE change_id=$id AND "
                                + "actor!=$author AND decision='approve' AND stale=0;"
                            ),
                            ("$id", change["id"]!.GetValue<long>()),
                            ("$author", change["author"]!.GetValue<string>())
                        )
                    );
                }
                first.Complete();
                second.Complete();
                Phase7Sessions.Turn context1 = await state
                    .FixedAsync(next => state.Sessions.NextTurnAsync("worker1", next), token)
                    .ConfigureAwait(false);
                Phase7Sessions.Turn context2 = await state
                    .FixedAsync(next => state.Sessions.NextTurnAsync("worker2", next), token)
                    .ConfigureAwait(false);
                Assert.Contains(direct["body"]!.GetValue<string>(), context1.Text);
                Assert.Contains(fact["content"]!.GetValue<string>(), context1.Text);
                Assert.Contains(team["body"]!.GetValue<string>(), context2.Text);
                Assert.Contains("Cooperating on #", context2.Text);
                long batch1 = state.SendingBatch("worker1");
                long batch2 = state.SendingBatch("worker2");
                Phase7Clock.ObservedPoll ownerPoll = await state
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
                Assert.AreEqual(
                    "owner-pause",
                    state.Database.ReadMetadata("lifecycle:stop_reason")
                );
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
        Phase7Run state = await Phase7Run
            .CreateAsync(
                testContext, token, planning: 10, interval: interval, allocation: "shared",
                ownedHosts: !reject,
                configuredCheck: reject
                    ? (_, arguments, _, _, _) => Task.FromResult(
                        new ProcessResult(
                            int.Parse(arguments[1], CultureInfo.InvariantCulture),
                            "Controlled planner check.",
                            "",
                            false
                        )
                    )
                    : null
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
                using Phase7Handoff handoff = new();
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
                        // This timestamp belongs to the actual typed Open entry,
                        // not to a later channel-reader continuation.
                        Phase7Sessions.Host host = state.Sessions.Hosts.Single(item =>
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
                        if (!reject)
                        {
                            followerIdentity["pid"] = host.Ready!.Pid;
                            followerIdentity["birthTicks"] = host.Ready.BirthTicks;
                        }
                        followerEntry.TrySetResult(state.Clock.GetUtcNow());
                    }
                };
                state.Sessions.BeforeRead = (session, _) =>
                {
                    if (
                        session.Worker == "worker1"
                        && session.CompletedTurns == 0
                        && state.Database.ReadMetadata("worker:worker1:lifecycle") == "ready"
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
                Assert.IsNull(state.Database.ReadMetadata("worker:worker2:session"));
                Assert.AreEqual(0m, state.Reservation("worker2").Allowance);
                // While the actual Open is still held, wait for registered progress
                // again. No successful first admission can yet have been consumed.
                await ObserveLivePlannerPollAsync("the second pre-release planner progress poll")
                    .ConfigureAwait(false);
                DateTimeOffset openedAt = state.Clock.GetUtcNow();
                release.TrySetResult();
                Phase7Handoff.Startup plannerStart = await state
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
                Phase7Sessions.Turn planner = await state
                    .FixedAsync(next => state.Sessions.NextTurnAsync("worker1", next), token)
                    .ConfigureAwait(false);
                long plannerBatch = state.SendingBatch("worker1");
                DateTimeOffset eligibleAt = admittedAt.AddSeconds(interval);
                long nativeLeases = state.Number(
                    "SELECT COUNT(*) FROM process_leases WHERE kind LIKE 'native:%';"
                );
                Assert.IsLessThan(planningDeadline, state.Clock.GetUtcNow());
                Console.WriteLine(
                    "PHASE7_PLANNER_READY "
                        + new JsonObject
                        {
                            ["typedPreparationReadyAt"] = readyAt.ToString("O"),
                            ["consumedAdmissionPhaseAt"] = admittedAt.ToString("O"),
                            ["turnObservedAt"] = state.Clock.GetUtcNow().ToString("O"),
                            ["strictPlanningCutoff"] = planningDeadline.ToString("O"),
                            ["intervalSeconds"] = interval,
                            ["eligibleAt"] = eligibleAt,
                        }.ToJsonString()
                );
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
                Assert.AreNotEqual("true", state.Database.ReadMetadata("git:plan_integrated"));
                if (interval == 0)
                {
                    Assert.IsGreaterThanOrEqualTo(eligibleAt, state.Clock.GetUtcNow());
                }
                Console.WriteLine(
                    "PHASE7_PLANNER_CLOSED "
                        + new JsonObject
                        {
                            ["intervalSeconds"] = interval,
                            ["eligibleAt"] = eligibleAt,
                            ["observedAt"] = state.Clock.GetUtcNow(),
                            ["planningDeadline"] = planningDeadline,
                            ["healthCheckedAt"] = Convert.ToString(
                                state.Value("SELECT checked_at FROM git_health;")
                            ),
                            ["nativeLeaseCount"] = nativeLeases,
                            ["worker2Allowance"] = state.Reservation("worker2").Allowance,
                            ["worker2GrantOpen"] = state.Reservation("worker2").GrantOpen,
                            ["worker2StartupObserved"] = handoff.SecondStarting.Task.IsCompleted,
                            ["worker2OpenObserved"] = followerEntry.Task.IsCompleted,
                        }.ToJsonString()
                );
                Phase7Clock.ObservedPoll? rejectionPoll = null;
                if (candidate != PlannerCandidate.None)
                {
                    Phase7Clock.ObservedPoll? requestPoll = reject
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
                Phase7Clock.ObservedPoll? timeoutStartupPoll = null;
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
                            () => state.Database.ReadMetadata("git:plan_integrated") == "true",
                            planningDeadline,
                            token
                        )
                        .ConfigureAwait(false);
                    Assert.AreEqual("true", state.Database.ReadMetadata("git:plan_integrated"));
                    Assert.IsLessThan(planningDeadline, state.Clock.GetUtcNow());
                }
                else
                {
                    Phase7Clock.ObservedPoll edgePoll;
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
                        Assert.AreEqual(
                            0L, state.Number("SELECT COUNT(*) FROM integration_requests;")
                        );
                        edgePoll = await ObservePlanningPollAsync().ConfigureAwait(false);
                    }
                    AssertColdPeer();
                    Assert.IsNull(PeerKickoff(), "No peer kickoff is due before planning timeout.");
                    if (reject)
                    {
                        Assert.AreNotEqual(
                            "true", state.Database.ReadMetadata("git:plan_integrated")
                        );
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
                Phase7Handoff.Startup? eligibleStartup = null;
                if (interval > 0)
                {
                    // The positive representative still owes the interval from the
                    // preceding COMPLETED admission, despite earlier planning release.
                    DateTimeOffset probe = eligibleAt.AddSeconds(-1);
                    Task<JsonObject> run = state.Active;
                    Task<Phase7Handoff.Startup> target = handoff.SecondStarting.Task;
                    await Phase7Run.RequireColdRampTargetAsync(target, run, eligibleAt)
                        .ConfigureAwait(false);
                    Phase7Clock.ObservedPoll wake = await state.ObserveRampProbePollAsync(
                        target, run, eligibleAt, registeredAfter: 0, token
                    ).ConfigureAwait(false);
                    try
                    {
                        state.Clock.Advance(wake, state.WorkDeadline, token, through: probe);
                    }
                    catch (Exception error) when (error is InvalidOperationException
                        or OperationCanceledException)
                    {
                        await Phase7Run.RequireColdRampTargetAsync(target, run, eligibleAt)
                            .ConfigureAwait(false);
                        throw;
                    }
                    Phase7Clock.ObservedPoll eligiblePoll = await state.ObserveRampProbePollAsync(
                        target, run, eligibleAt, wake.TimerId, token
                    ).ConfigureAwait(false);
                    await Phase7Run.RequireColdRampTargetAsync(target, run, eligibleAt)
                        .ConfigureAwait(false);
                    Assert.AreEqual(probe, state.Clock.GetUtcNow());
                    AssertColdPeer();
                    state.Clock.Advance(
                        eligiblePoll, state.WorkDeadline, token, through: eligibleAt
                    );
                    using CancellationTokenSource observation =
                        CancellationTokenSource.CreateLinkedTokenSource(token);
                    Task<Phase7Clock.ObservedPoll> freshPoll = state.Clock.WaitForPollAsync(
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
                Phase7Handoff.Startup startup;
                if (eligibleStartup is not null)
                {
                    startup = eligibleStartup;
                }
                else if (timeoutStartupPoll is { } startupPoll)
                {
                    // Release is already committed. Only the next eligibility
                    // poll may advance time; native startup gets fixed-clock I/O.
                    releaseCutoff = planningDeadline.AddSeconds(1);
                    Task<Phase7Handoff.Startup> targetStart = handoff.SecondStarting.Task;
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
                // Now the prerequisite really is non-clock startup. Observe its
                // ACTUAL task, including faults/cancellation, not a success-only flag.
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
                    Assert.AreNotEqual("true", state.Database.ReadMetadata("git:plan_integrated"));
                    if (!reject)
                    {
                        Assert.AreEqual(
                            0L, state.Number("SELECT COUNT(*) FROM integration_requests;")
                        );
                    }
                }
                // Startup completion still isn't consumption. A queued kickoff may
                // predate admission; a real sending batch means the admitted loop
                // has CLAIMED work before its asynchronous bootstrap Git operation.
                await state
                    .WaitDurableAsync(
                        () =>
                            state.Number(
                                (
                                    "SELECT COUNT(*) FROM delivery_batches WHERE "
                                    + "recipient='worker2' AND state='sending';"
                                )
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
                Phase7Sessions.Turn peer = await state
                    .FixedAsync(next => state.Sessions.NextTurnAsync("worker2", next), token)
                    .ConfigureAwait(false);
                Assert.AreEqual(claimedAt, state.Clock.GetUtcNow());
                Assert.IsLessThan(releaseCutoff, claimedAt);
                Assert.AreEqual(
                    "sending",
                    state.Value(
                        "SELECT state FROM delivery_batches WHERE id=$id;",
                        ("$id", followerBatch)
                    )
                );
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
                if (reject)
                {
                    Assert.IsNull(peer.Session.Metadata["pid"]);
                    Assert.IsNull(peer.Session.Metadata["birthTicks"]);
                    Assert.IsGreaterThan(0L, state.Dispatches(startup.ReservationId));
                }
                else
                {
                    Assert.AreEqual(
                        followerIdentity["pid"]!.GetValue<int>(),
                        peer.Session.Metadata["pid"]!.GetValue<int>()
                    );
                    Assert.AreEqual(
                        followerIdentity["birthTicks"]!.GetValue<long>(),
                        peer.Session.Metadata["birthTicks"]!.GetValue<long>()
                    );
                }
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
                Console.WriteLine(
                    "PHASE7_PLANNER_FOLLOWER "
                        + new JsonObject
                        {
                            ["integrate"] = integrate,
                            ["rejectedPlan"] = reject,
                            ["intervalSeconds"] = interval,
                            ["admittedAt"] = admittedAt,
                            ["typedPlannerReadyAt"] = readyAt,
                            ["planningDeadline"] = planningDeadline,
                            ["eligibleAt"] = eligibleAt,
                            ["actualFollowerOpenAt"] = enteredAt,
                            ["actualStartupCapturedAt"] = startupCapturedAt,
                            ["actualStartupTaskStatus"] = startup.Operation.Status.ToString(),
                            ["startupWorker"] = startup.Worker,
                            ["startupReservationId"] = startup.ReservationId,
                            ["startupCwd"] = startup.Cwd,
                            ["claimedBatch"] = followerBatch,
                            ["claimedBatchObservedAt"] = claimedAt,
                            ["turnObservedAt"] = state.Clock.GetUtcNow(),
                            ["strictReleaseCutoff"] = releaseCutoff,
                            ["originalPerCallTimeoutSeconds"] = peer.Timeout.TotalSeconds,
                            ["identity"] = followerIdentity.DeepClone(),
                        }.ToJsonString()
                );
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
                Assert.AreEqual("deadline", state.Database.ReadMetadata("lifecycle:stop_reason"));
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
                foreach (long batch in new[] { plannerBatch, followerBatch })
                {
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

                long? PeerKickoff() =>
                    (long?)state.Value(
                        "SELECT id FROM events WHERE recipient='worker2' AND kind='kickoff';"
                    );

                Task<Phase7Clock.ObservedPoll> ObservePlanningPollAsync(long registeredAfter = 0) =>
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
                    foreach (Exception error in secondary)
                    {
                        Console.Error.WriteLine(
                            "PHASE7_READINESS_SECONDARY " + Json.ExceptionDetails(error)
                        );
                    }

                    // Prefer a known terminal producer to a ready poll or racing case cancellation.
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
                    Assert.IsNull(state.Database.ReadMetadata("worker:worker2:session"));
                    Assert.AreEqual(0m, state.Reservation("worker2").Allowance);
                    Assert.IsFalse(state.Reservation("worker2").GrantOpen);
                    Assert.AreEqual(0L, state.Dispatches(state.Reservation("worker2").Id));
                    Assert.IsEmpty(
                        state
                            .Sessions.Hosts.Where(host => host.Operation == "run")
                            .SelectMany(host => host.Sessions)
                            .Where(session => session.Worker == "worker2")
                    );
                    Assert.AreEqual(
                        nativeLeases,
                        state.Number(
                            "SELECT COUNT(*) FROM process_leases WHERE kind LIKE 'native:%';"
                        )
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
        Phase7Run state = await Phase7Run
            .CreateAsync(testContext, token, workers: 1)
            .ConfigureAwait(false);
        await state
            .RunBodyAsync(async () =>
            {
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
                Phase7Sessions.Turn turn = await state
                    .FixedAsync(next => state.Sessions.NextTurnAsync("worker1", next), token)
                    .ConfigureAwait(false);
                Assert.Contains("Never overwrite AGENTS.md", turn.Text);
                Assert.Contains(
                    "Do not spawn nested agent teams or modify authentication",
                    turn.Text
                );
                long batch = state.SendingBatch("worker1");
                Phase7Clock.ObservedPoll ownerPoll = await state
                    .ObserveOwnerPollAsync(token)
                    .ConfigureAwait(false);
                turn.Complete();
                await state.WaitAckAsync(batch, state.WorkDeadline, token).ConfigureAwait(false);
                GitScenarioState.Data(
                    await state
                        .StopRunningAsync(action, ownerPoll, token)
                        .ConfigureAwait(false)
                );
                Assert.AreEqual(reason, state.Database.ReadMetadata("lifecycle:stop_reason"));
                Assert.AreEqual(lifecycle, state.Database.ReadMetadata("lifecycle"));
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
                    Phase7Sessions.Session session in state
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
                Console.WriteLine(
                    "PHASE7_APPLICATION_NONMUTATION "
                        + new JsonObject
                        {
                            ["action"] = action,
                            ["protectedOwnedPolicyFiles"] = protectedFiles.Length,
                            ["targetInstructionsByteExact"] = true,
                            ["globalAndSystemGitConfigByteExact"] = true,
                            ["nativePolicyEffectivenessClaimed"] = false,
                        }.ToJsonString()
                );
                Assert.AreEqual(10m, state.Reservation("worker1").Actual);
                Assert.IsTrue(Directory.Exists(state.Database.StatePath));
                Assert.AreEqual(
                    "sent",
                    state.Value("SELECT state FROM delivery_batches WHERE id=$id;", ("$id", batch))
                );
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
                const string resourcesSql = (
                    "SELECT json_group_array(json_object(\n    "
                    + "'ref',ref,'path',path,'originalCommit',original_commit,'state',state"
                    + ",'retained',retained))\nFROM (SELECT * FROM git_resources ORDER BY "
                    + "ref);"
                );
                object? resources = state.Value(resourcesSql);
                string worktreeHead = await state
                    .Git.GitAsync(ownedWorktree, token, "rev-parse", "HEAD")
                    .ConfigureAwait(false);
                string refs = await state
                    .Git.GitAsync(state.Git.Repository, token, "show-ref")
                    .ConfigureAwait(false);
                string? finalized = state.Database.ReadMetadata("git:finalized");
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
                    Assert.AreEqual("paused", state.Database.ReadMetadata("lifecycle"));
                    Assert.AreNotEqual("true", state.Database.ReadMetadata("git:finalized"));
                    Assert.AreEqual(finalized, state.Database.ReadMetadata("git:finalized"));
                    Assert.AreEqual(resources, state.Value(resourcesSql));
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
                    Assert.AreEqual(
                        0L,
                        state.Number(
                            (
                                "SELECT COUNT(*) FROM git_resources WHERE path IS NOT NULL AND "
                                + "state<>'removed';"
                            )
                        )
                    );
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
                Assert.AreEqual(
                    "sent",
                    state.Value("SELECT state FROM delivery_batches WHERE id=$id;", ("$id", batch))
                );
                Assert.IsTrue(global.SequenceEqual(File.ReadAllBytes(state.Git.GlobalConfig)));
                Assert.IsTrue(system.SequenceEqual(File.ReadAllBytes(state.Git.SystemConfig)));
                Console.WriteLine(
                    "PHASE7_EXPLICIT_CLEANUP "
                        + new JsonObject
                        {
                            ["action"] = action,
                            ["publicResult"] = cleanupResult.DeepClone(),
                            ["retainedWhenPaused"] =
                                action == "pause" && Directory.Exists(ownedWorktree),
                            ["unregisteredSentinelRetained"] = File.Exists(unrelated),
                            ["financialDeliveryAndProtectedFilesUnchanged"] = true,
                        }.ToJsonString()
                );
            })
            .ConfigureAwait(false);
    }

    [TestMethod]
    [TestCategory("EndReasonAndDeliveryControl")]
    [Timeout(120_000, CooperativeCancellation = true)]
    public async Task PeerTaskClosureDoesNotClosePeerWorkOrTheRun()
    {
        CancellationToken token = testContext.CancellationToken;
        Phase7Run state = await Phase7Run.CreateAsync(testContext, token, ownedHosts: false)
            .ConfigureAwait(false);
        await state
            .RunBodyAsync(async () =>
            {
                _ = state.Start();
                Phase7Sessions.Turn one = await state
                    .FixedAsync(next => state.Sessions.NextTurnAsync("worker1", next), token)
                    .ConfigureAwait(false);
                long ownBatch = state.SendingBatch("worker1");
                Phase7Sessions.Turn two = await state
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
                state.Invoke(
                    "worker1",
                    "task_write",
                    new() { ["action"] = "close", ["id"] = own["id"]!.GetValue<long>() }
                );
                await state.StepAsync(state.WorkDeadline, token).ConfigureAwait(false);
                Assert.AreEqual(
                    "open",
                    state.Value(
                        "SELECT state FROM tasks WHERE id=$id;",
                        ("$id", peer["id"]!.GetValue<long>())
                    )
                );
                Assert.IsNull(state.Database.ReadMetadata("lifecycle:root_closed_by"));
                Assert.IsNull(state.Database.ReadMetadata("lifecycle:stop_reason"));
                Assert.IsFalse(one.Token.IsCancellationRequested);
                Assert.IsFalse(two.Token.IsCancellationRequested);
                JsonObject closedRoot = state.Invoke(
                    "worker1",
                    "task_write",
                    new() { ["action"] = "close", ["id"] = 1 }
                );
                Assert.AreEqual("closed", closedRoot["state"]!.GetValue<string>());
                Assert.AreEqual("worker1", state.Value("SELECT closed_by FROM tasks WHERE id=1;"));
                await state.UntilAsync(
                    () => (string?)state.Value("SELECT state FROM tasks WHERE id=1;") == "open",
                    state.WorkDeadline,
                    token
                ).ConfigureAwait(false);
                Assert.AreEqual("worker1", state.Database.ReadMetadata("lifecycle:root_closed_by"));
                Assert.AreEqual("worker1", state.Value("SELECT closed_by FROM tasks WHERE id=1;"));
                JsonObject reopened = JsonNode.Parse((string)state.Value(
                    "SELECT payload FROM events WHERE recipient='worker2' AND kind='task_reopened';"
                )!)!.AsObject();
                Assert.AreEqual(1L, reopened["taskId"]!.GetValue<long>());
                Assert.AreEqual("worker1", reopened["closedBy"]!.GetValue<string>());
                Assert.IsNull(state.Database.ReadMetadata("lifecycle:stop_reason"));
                Assert.IsFalse(one.Token.IsCancellationRequested);
                Assert.IsFalse(two.Token.IsCancellationRequested);
                long peerBatch = state.SendingBatch("worker2");
                two.Complete();
                await state.WaitAckAsync(peerBatch, state.WorkDeadline, token)
                    .ConfigureAwait(false);
                Phase7Sessions.Turn followup = await state.FixedAsync(
                    next => state.Sessions.NextTurnAsync("worker2", next), token
                ).ConfigureAwait(false);
                long followupBatch = state.SendingBatch("worker2");
                Assert.AreEqual(two.Session.SessionId, followup.Session.SessionId);
                Assert.Contains("task_reopened", followup.Text);
                Assert.AreEqual(1, two.Session.CompletedTurns);
                Assert.AreEqual(
                    "open",
                    state.Value(
                        "SELECT state FROM tasks WHERE id=$id;",
                        ("$id", peer["id"]!.GetValue<long>())
                    )
                );
                state.Invoke(
                    "owner",
                    "task_write",
                    new() { ["action"] = "close", ["id"] = 1 }
                );
                await state.UntilAsync(
                    state.Handoff!.OwnerStopped.Task, state.WorkDeadline, token
                ).ConfigureAwait(false);
                await state.Active.WaitAsync(token).ConfigureAwait(false);
                JsonObject result = await state.Active.ConfigureAwait(false);
                Assert.IsFalse(result["ok"]!.GetValue<bool>());
                Assert.AreEqual(
                    "ownership_or_usage_unresolved", result["code"]!.GetValue<string>()
                );
                Assert.AreEqual(
                    "owner-root-closure", state.Database.ReadMetadata("lifecycle:stop_reason")
                );
                Assert.AreEqual("owner", state.Database.ReadMetadata("lifecycle:root_closed_by"));
                Assert.AreEqual("owner", state.Value("SELECT closed_by FROM tasks WHERE id=1;"));
                Assert.AreEqual("closed", state.Value("SELECT state FROM tasks WHERE id=1;"));
                Assert.AreEqual("unresolved", state.Database.ReadMetadata("lifecycle"));
                Assert.AreEqual(0L, state.Number(
                    "SELECT COUNT(*) FROM coordinator_segments WHERE ended_at IS NULL;"
                ));
                Assert.AreEqual("owner-root-closure", state.Value(
                    "SELECT stop_reason FROM coordinator_segments ORDER BY id DESC LIMIT 1;"
                ));
                Assert.IsTrue(one.Token.IsCancellationRequested);
                Assert.IsTrue(followup.Token.IsCancellationRequested);
                foreach (long heldBatch in new[] { ownBatch, followupBatch })
                {
                    Assert.AreEqual("failed", state.Value(
                        "SELECT state FROM delivery_batches WHERE id=$id;", ("$id", heldBatch)
                    ));
                    Assert.AreEqual(
                        "Turn interrupted by coordinator stop; durable replay may repeat actions.",
                        state.Value(
                            "SELECT last_error FROM delivery_batches WHERE id=$id;",
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
                Assert.AreEqual(
                    0L, state.Number("SELECT COUNT(*) FROM checks WHERE kind='final';")
                );
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
        Phase7OwnedCreator creator = await Phase7OwnedCreator
            .OpenAsync(testContext, minimumBacking, token)
            .ConfigureAwait(false);
        await creator
            .RunOwnedAsync(async () =>
            {
                Phase7Run state = Phase7Run.Attach(creator, token);
                await state
                    .RunBodyAsync(async () =>
                    {
                        string originalSession = creator.SessionId,
                            originalReservation = creator.ReservationId;
                        string originalJournal = Phase6OwnedCreator.Hash(creator.JournalPath);
                        Assert.AreEqual(
                            "true",
                            state.Database.ReadMetadata("native:bootstrap:" + originalSession)
                        );
                        Assert.AreEqual(
                            minimumBacking ? 30m : 200m,
                            state.Reservation("worker1").Allowance
                        );
                        Assert.AreEqual(10m, state.Reservation("worker1").Actual);
                        await creator.CrashAsync(token).ConfigureAwait(false);
                        bool exactLoadEntered = false;
                        state.Sessions.BeforeOpen = (session, next) =>
                        {
                            next.ThrowIfCancellationRequested();
                            if (session.ExactSession == originalSession)
                            {
                                exactLoadEntered = true;
                                // Supported typed dependency response, not an altered
                                // application association or manufactured native history.
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
                            Assert.AreEqual(
                                "credit_exhausted",
                                state.Database.ReadMetadata("lifecycle:stop_reason")
                            );
                            Assert.AreEqual(
                                0L,
                                state.Number(
                                    (
                                        "SELECT COUNT(*) FROM coordinator_segments WHERE "
                                        + "ended_at IS NULL;"
                                    )
                                )
                            );
                            Assert.HasCount(2, state.Ledger.Reservations(state.Database.RunId));
                            Assert.AreEqual(originalReservation, state.Reservation("worker1").Id);
                            Assert.AreEqual("reconciled", state.Reservation("worker1").State);
                            Assert.AreEqual(10m, state.Reservation("worker1").Actual);
                            Assert.AreEqual(1L, state.Dispatches(originalReservation));
                            Assert.AreEqual(
                                0L,
                                state.Number(
                                    "SELECT COUNT(*) FROM delivery_batches WHERE state='sending';"
                                )
                            );
                            Assert.IsEmpty(
                                state
                                    .Sessions.Hosts.SelectMany(host => host.Sessions)
                                    .Where(session => session.HasSession)
                            );
                            Assert.AreEqual(
                                originalJournal,
                                Phase6OwnedCreator.Hash(creator.JournalPath)
                            );
                        }
                        else
                        {
                            Phase7Sessions.Turn fallback = await state
                                .FixedAsync(
                                    next => state.Sessions.NextTurnAsync("worker1", next),
                                    token
                                )
                                .ConfigureAwait(false);
                            CreditReservation replacement = state.Reservation("worker1");
                            Assert.AreNotEqual(originalSession, fallback.Session.SessionId);
                            Assert.AreNotEqual(originalReservation, replacement.Id);
                            Assert.AreEqual(190m, replacement.Allowance);
                            Assert.IsNull(replacement.Actual);
                            Assert.AreEqual(
                                "fresh-summary-fallback: exact-session load unavailable or failed",
                                state.Database.ReadMetadata("worker:worker1:recovery")
                            );
                            Assert.Contains(
                                "Goal: " + state.Database.Configuration.Goal,
                                fallback.Text
                            );
                            Assert.Contains("==== DURABLE CHANGES ====", fallback.Text);
                            Assert.IsNull(
                                state.Database.ReadMetadata(
                                    "native:bootstrap:" + fallback.Session.SessionId
                                )
                            );
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
                            Phase7Clock.ObservedPoll ownerPoll = await state
                                .ObserveOwnerPollAsync(token)
                                .ConfigureAwait(false);
                            fallback.Complete();
                            await state
                                .WaitAckAsync(batch, state.WorkDeadline, token)
                                .ConfigureAwait(false);
                            Assert.AreEqual(
                                "true",
                                state.Database.ReadMetadata(
                                    "native:bootstrap:" + fallback.Session.SessionId
                                )
                            );
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
                        Console.WriteLine(
                            "PHASE7_EXACT_FALLBACK "
                                + new JsonObject
                                {
                                    ["budgetDenied"] = minimumBacking,
                                    ["originalSession"] = originalSession,
                                    ["originalReservation"] = originalReservation,
                                    ["originalHistory"] = state.Sessions.Journal.Snapshot(
                                        originalSession
                                    ),
                                    ["financial"] = state.Ledger.Status(
                                        state.Database.RunId,
                                        state.Database.Configuration.RunAiCreditAllowance
                                    ),
                                }.ToJsonString()
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
        Phase7Run state = await Phase7Run
            .CreateAsync(testContext, token, workers: 1)
            .ConfigureAwait(false);
        await state
            .RunBodyAsync(async () =>
            {
                _ = state.Start();
                Phase7Sessions.Turn original = await state
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
                Phase7Sessions.Turn changeNotice = await state
                   .FixedAsync(next => state.Sessions.NextTurnAsync("worker1", next), token)
                   .ConfigureAwait(false);
                Assert.Contains("Retained resume witness", changeNotice.Text);
                long changeBatch = state.SendingBatch("worker1");
                Phase7Clock.ObservedPoll originalOwnerPoll = await state
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
                Phase7Sessions.Turn resumed = await state
                   .FixedAsync(next => state.Sessions.NextTurnAsync("worker1", next), token)
                   .ConfigureAwait(false);
                Assert.AreNotEqual(original.Session.SessionId, resumed.Session.SessionId);
                Assert.AreEqual(
                   "fresh-session-with-durable-summary",
                   state.Database.ReadMetadata("worker:worker1:recovery")
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
                Assert.AreEqual(2L, state.Number("SELECT COUNT(*) FROM coordinator_segments;"));
                Assert.AreEqual(
                   20m,
                   state
                       .Ledger.Reservations(state.Database.RunId)
                       .Single(row => row.Id == old.Id)
                       .Actual
                );
                Assert.IsNull(
                   state.Database.ReadMetadata("native:bootstrap:" + resumed.Session.SessionId)
                );
                Assert.AreEqual(180m, state.Reservation("worker1").Allowance);
                long resumedBatch = state.SendingBatch("worker1");
                Phase7Clock.ObservedPoll resumedOwnerPoll = await state
                    .ObserveOwnerPollAsync(token)
                    .ConfigureAwait(false);
                resumed.Complete();
                await state
                    .WaitAckAsync(resumedBatch, state.WorkDeadline, token)
                    .ConfigureAwait(false);
                Assert.AreEqual(
                    "true",
                    state.Database.ReadMetadata("native:bootstrap:" + resumed.Session.SessionId)
                );
                GitScenarioState.Data(
                    await state
                        .StopRunningAsync("pause", resumedOwnerPoll, token)
                        .ConfigureAwait(false)
                );
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
                Assert.AreEqual(2L, state.Number("SELECT COUNT(*) FROM coordinator_segments;"));
                Assert.AreEqual(
                    0L,
                    state.Number("SELECT COUNT(*) FROM delivery_batches WHERE state='sending';")
                );
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

    private static async Task AssertRejectedPlannerCandidateAsync(
        Phase7Run state,
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
                state.Database.ReadMetadata("git:integration_branch")!, token
            ).ConfigureAwait(false)
        );
    }

    private static async Task<(JsonObject Change, JsonObject Request)> SubmitReviewedAsync(
        Phase7Run state,
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
