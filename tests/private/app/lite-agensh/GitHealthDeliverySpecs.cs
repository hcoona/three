using System.Globalization;
using System.Text.Json.Nodes;
using LiteAgensh.Tests.Support;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace LiteAgensh.Tests;

[TestClass]
public sealed class GitHealthDeliverySpecs(TestContext testContext)
{
    [TestMethod]
    [TestCategory("BrokenMainRestrictionAndTimers")]
    public async Task BrokenMainRestrictsImmediatelyAndHonorsManualAlertAndLiftBoundaries()
    {
        CancellationToken token = testContext.CancellationToken;
        CheckCommand failure = GitScenarioState.ExitCheck(17);
        using GitScenarioState state = await GitScenarioState
            .CreateAsync(
                token, integrationChecks: [failure], finalChecks: [failure],
                configuredCheck: (_, arguments, _, _, checkToken) =>
                {
                    checkToken.ThrowIfCancellationRequested();
                    string outcome = arguments[1];
                    return Task.FromResult(new ProcessResult(
                        int.Parse(outcome, CultureInfo.InvariantCulture),
                        "controlled check stdout " + outcome,
                        "controlled check stderr " + outcome,
                        TimedOut: false
                    ));
                }
            )
            .ConfigureAwait(false);
        await state.Operations.InitializeAsync(token).ConfigureAwait(false);
        DateTimeOffset redSince = state.Clock.GetUtcNow();
        JsonObject health = GitScenarioState.Data(
            await state.Operations.EvaluateHealthAsync(token).ConfigureAwait(false)
        );
        AssertHealth(health, restricted: true, alerted: false, lifted: false, redSince);
        Assert.AreEqual(state.BaseCommit, health["brokenCommit"]!.GetValue<string>());
        long healthTask = health["taskId"]!.GetValue<long>();
        JsonObject task = GitScenarioState.Data(
            state.Collaboration.Invoke("worker2", "task_read", new() { ["id"] = healthTask })
        );
        Assert.Contains("Integration health (fix-main)", task["title"]!.GetValue<string>());
        Assert.Contains(
            "Candidate and final checks remain required",
            task["body"]!.GetValue<string>()
        );
        string firstNotice = ReadAndCompleteEvents(state, "worker1");
        Assert.Contains(
            "only changes linked to integration-health task "
                + healthTask.ToString(CultureInfo.InvariantCulture),
            firstNotice
        );
        Assert.Contains("Integration fails at " + state.BaseCommit, firstNotice);
        Assert.Contains(
            "Integration fails at " + state.BaseCommit,
            ReadAndCompleteEvents(state, "worker2")
        );

        await state
            .CommitAsync(
                state.Operations.GetWorkerPath("worker2"),
                "feature.txt",
                "feature unrelated to broken main\n",
                token
            )
            .ConfigureAwait(false);
        JsonObject feature = await state
            .SubmitAsync("worker2", "Unrelated feature", token)
            .ConfigureAwait(false);
        await state.ReviewAsync("worker1", feature, token).ConfigureAwait(false);
        await state.QueueAsync(feature, token).ConfigureAwait(false);
        await state.Operations.ProcessPendingAsync(token).ConfigureAwait(false);
        JsonObject rejected = await state.ReadAsync(feature, token).ConfigureAwait(false);
        AssertRestrictedRequest(
            Assert.ContainsSingle(rejected["integrationRequests"]!.AsArray())!.AsObject()
        );
        Assert.IsEmpty(rejected["checks"]!.AsArray());
        Assert.AreEqual(
            state.BaseCommit,
            await state.ResolveAsync(state.IntegrationRef, token).ConfigureAwait(false)
        );

        // The restriction admits a task-linked submission, not a blanket integration shutdown.
        await state
            .CommitAsync(
                state.Operations.GetWorkerPath("worker1"),
                "repair.txt",
                "task-linked repair attempt\n",
                token
            )
            .ConfigureAwait(false);
        JsonObject repair = await state
            .SubmitAsync("worker1", "Attempt health repair", token, healthTask)
            .ConfigureAwait(false);
        await state.ReviewAsync("worker2", repair, token).ConfigureAwait(false);
        await state.QueueAsync(repair, token).ConfigureAwait(false);
        await state.Operations.ProcessPendingAsync(token).ConfigureAwait(false);
        JsonObject repaired = await state.ReadAsync(repair, token).ConfigureAwait(false);
        Assert.AreEqual("integrated", repaired["state"]!.GetValue<string>());
        JsonObject repairCheck = Assert.ContainsSingle(repaired["checks"]!.AsArray())!.AsObject();
        Assert.AreEqual("passed", repairCheck["status"]!.GetValue<string>());
        string restrictedHead = await state
            .ResolveAsync(state.IntegrationRef, token)
            .ConfigureAwait(false);

        state.Clock.Advance(TimeSpan.FromMinutes(15) - TimeSpan.FromTicks(1));
        health = GitScenarioState.Data(
            await state.Operations.EvaluateHealthAsync(token).ConfigureAwait(false)
        );
        AssertHealth(health, restricted: true, alerted: false, lifted: false, redSince);
        Assert.AreEqual(0L, state.Number("SELECT COUNT(*) FROM events WHERE kind='health-alert';"));
        Assert.DoesNotContain(
            "failed for at least 15 minutes",
            ReadAndCompleteEvents(state, "worker1")
        );
        Assert.DoesNotContain(
            "failed for at least 15 minutes",
            ReadAndCompleteEvents(state, "worker2")
        );

        state.Clock.Advance(TimeSpan.FromTicks(1));
        health = GitScenarioState.Data(
            await state.Operations.EvaluateHealthAsync(token).ConfigureAwait(false)
        );
        AssertHealth(health, restricted: true, alerted: true, lifted: false, redSince);
        Assert.Contains(
            "All workers should help restore integration health",
            ReadAndCompleteEvents(state, "worker1")
        );
        Assert.Contains(
            "Integration has failed for at least 15 minutes",
            ReadAndCompleteEvents(state, "worker2")
        );

        state.Clock.Advance(TimeSpan.FromMinutes(45) - TimeSpan.FromTicks(1));
        health = GitScenarioState.Data(
            await state.Operations.EvaluateHealthAsync(token).ConfigureAwait(false)
        );
        AssertHealth(health, restricted: true, alerted: true, lifted: false, redSince);
        Assert.AreEqual(0L, state.Number("SELECT COUNT(*) FROM events WHERE kind='health-lift';"));
        await state.QueueAsync(feature, token).ConfigureAwait(false);
        await state.Operations.ProcessPendingAsync(token).ConfigureAwait(false);
        rejected = await state.ReadAsync(feature, token).ConfigureAwait(false);
        AssertRestrictedRequest(rejected["integrationRequests"]!.AsArray()[^1]!.AsObject());
        Assert.IsEmpty(rejected["checks"]!.AsArray());
        Assert.AreEqual(
            restrictedHead,
            await state.ResolveAsync(state.IntegrationRef, token).ConfigureAwait(false)
        );
        Assert.DoesNotContain("restriction lifted", ReadAndCompleteEvents(state, "worker1"));
        Assert.DoesNotContain("restriction lifted", ReadAndCompleteEvents(state, "worker2"));

        state.Clock.Advance(TimeSpan.FromTicks(1));
        health = GitScenarioState.Data(
            await state.Operations.EvaluateHealthAsync(token).ConfigureAwait(false)
        );
        AssertHealth(health, restricted: false, alerted: true, lifted: true, redSince);
        Assert.Contains(
            "restriction lifted after 60 minutes",
            ReadAndCompleteEvents(state, "worker1")
        );
        Assert.Contains(
            "Required candidate/final checks still apply",
            ReadAndCompleteEvents(state, "worker2")
        );
        state.Clock.Advance(TimeSpan.FromTicks(1));
        RunConfiguration passingCandidate = state.Database.Configuration;
        state.Database.SaveConfiguration(passingCandidate with
        {
            CandidateChecks = [GitScenarioState.ExitCheck(23)],
        });
        await state.QueueAsync(feature, token).ConfigureAwait(false);
        await state.Operations.ProcessPendingAsync(token).ConfigureAwait(false);
        JsonObject vetoed = await state.ReadAsync(feature, token).ConfigureAwait(false);
        JsonObject veto = Assert.ContainsSingle(vetoed["checks"]!.AsArray())!.AsObject();
        Assert.AreEqual("failed", veto["status"]!.GetValue<string>());
        Assert.AreEqual(23, veto["exitCode"]!.GetValue<int>());
        Assert.AreEqual(
            restrictedHead,
            await state.ResolveAsync(state.IntegrationRef, token).ConfigureAwait(false)
        );
        Assert.AreEqual(
            "feature unrelated to broken main\n",
            File.ReadAllText(Path.Combine(state.Operations.GetWorkerPath("worker2"), "feature.txt"))
        );
        state.Database.SaveConfiguration(passingCandidate);
        await state.QueueAsync(feature, token).ConfigureAwait(false);
        await state.Operations.ProcessPendingAsync(token).ConfigureAwait(false);
        JsonObject admitted = await state.ReadAsync(feature, token).ConfigureAwait(false);
        Assert.AreEqual("integrated", admitted["state"]!.GetValue<string>());
        JsonObject featureCheck = Assert.ContainsSingle(
            admitted["checks"]!.AsArray().OfType<JsonObject>()
                .Where(check => check["status"]!.GetValue<string>() == "passed")
        );
        Assert.AreEqual("passed", featureCheck["status"]!.GetValue<string>());
        Assert.AreEqual(0, featureCheck["exitCode"]!.GetValue<int>());
        string finalHead = await state
            .ResolveAsync(state.IntegrationRef, token)
            .ConfigureAwait(false);
        Assert.AreEqual(
            "task-linked repair attempt\n",
            File.ReadAllText(Path.Combine(state.IntegrationPath, "repair.txt"))
        );
        Assert.AreEqual(
            "feature unrelated to broken main\n",
            File.ReadAllText(Path.Combine(state.IntegrationPath, "feature.txt"))
        );
        Assert.AreEqual(
            0L,
            state.Number(
                "SELECT COUNT(*) FROM checks WHERE kind='integration' AND status<>'failed';"
            )
        );
        Assert.AreEqual(
            2L,
            state.Number("SELECT COUNT(*) FROM events WHERE kind='health-alert';"),
            "The 15-minute transition must notify each of the two actors once."
        );
        Assert.AreEqual(
            2L,
            state.Number("SELECT COUNT(*) FROM events WHERE kind='health-lift';"),
            "The 60-minute transition must not rebroadcast on subsequent evaluation."
        );

        // Quiescence, not a coordinator-stop claim.
        state.Database.WriteMetadata("lifecycle", "stopped");
        JsonObject final = await state.Operations.FinalizeAsync(token).ConfigureAwait(false);
        Assert.IsFalse(final["ok"]!.GetValue<bool>());
        Assert.AreEqual("check_failed", final["code"]!.GetValue<string>());
        Assert.Contains("Final checks failed", final["message"]!.GetValue<string>());
        JsonObject report = final["data"]!.AsObject();
        Assert.IsFalse(report["accepted"]!.GetValue<bool>());
        Assert.AreEqual(finalHead, report["finalCommit"]!.GetValue<string>());
        Assert.AreEqual(
            finalHead,
            await state.ResolveAsync(state.DeliveryRef, token).ConfigureAwait(false)
        );
        Assert.IsFalse(report["integrationHealth"]!["passed"]!.GetValue<bool>());
        Assert.IsTrue(report["integrationHealth"]!["restrictionLifted"]!.GetValue<bool>());
        JsonObject finalCheck = Assert.ContainsSingle(
            report["checks"]!
                .AsArray()
                .OfType<JsonObject>()
                .Where(check => check["kind"]!.GetValue<string>() == "final")
        );
        Assert.AreEqual("failed", finalCheck["status"]!.GetValue<string>());
        Assert.AreEqual(17, finalCheck["exitCode"]!.GetValue<int>());
        Assert.AreEqual(finalHead, finalCheck["commit"]!.GetValue<string>());
        Assert.AreEqual(
            await state.ResolveAsync(finalHead + "^{tree}", token).ConfigureAwait(false),
            finalCheck["tree"]!.GetValue<string>()
        );
        string finalLog = File.ReadAllText(finalCheck["logPath"]!.GetValue<string>());
        Assert.StartsWith("Configured check execution: controlled.\n", finalLog);
        Assert.Contains("controlled check stdout 17", finalLog);
        Assert.Contains("controlled check stderr 17", finalLog);
        JsonObject[] integrationChecks = report["checks"]!
            .AsArray()
            .OfType<JsonObject>()
            .Where(check => check["kind"]!.GetValue<string>() == "integration")
            .ToArray();
        Assert.IsTrue(integrationChecks.Any(check =>
            check["commit"]!.GetValue<string>() == finalHead
        ));
        foreach (JsonObject check in integrationChecks)
        {
            Assert.AreEqual("failed", check["status"]!.GetValue<string>());
            Assert.AreEqual(17, check["exitCode"]!.GetValue<int>());
            Assert.AreEqual(
                await state.ResolveAsync(check["commit"]!.GetValue<string>() + "^{tree}", token)
                    .ConfigureAwait(false),
                check["tree"]!.GetValue<string>()
            );
            Assert.AreEqual(
                failure.Executable,
                check["command"]!["executable"]!.GetValue<string>()
            );
            Assert.AreEqual("exit", check["command"]!["arguments"]![0]!.GetValue<string>());
            Assert.AreEqual("17", check["command"]!["arguments"]![1]!.GetValue<string>());
            string log = File.ReadAllText(check["logPath"]!.GetValue<string>());
            Assert.StartsWith("Configured check execution: controlled.\n", log);
            Assert.Contains("controlled check stdout 17", log);
            Assert.Contains("controlled check stderr 17", log);
        }
    }

    [TestMethod]
    [TestCategory("BrokenMainRestrictionAndTimers")]
    public async Task RecoveredIntegrationRearmsRepairRestrictionForANewFailure()
    {
        CancellationToken token = testContext.CancellationToken;
        int checkExitCode = 17;
        using GitScenarioState state = await GitScenarioState.CreateAsync(
            token,
            integrationChecks: [new() { Executable = "controlled-integration-health-check" }],
            configuredCheck: (_, _, _, _, checkToken) =>
            {
                checkToken.ThrowIfCancellationRequested();
                return Task.FromResult(new ProcessResult(
                    checkExitCode,
                    "controlled integration-health check",
                    "",
                    TimedOut: false
                ));
            }
        );
        await state.Operations.InitializeAsync(token);
        DateTimeOffset firstRed = state.Clock.GetUtcNow();
        JsonObject health = GitScenarioState.Data(
            await state.Operations.EvaluateHealthAsync(token)
        );
        AssertHealth(health, restricted: true, alerted: false, lifted: false, firstRed);
        state.Clock.Advance(TimeSpan.FromMinutes(60));
        health = GitScenarioState.Data(await state.Operations.EvaluateHealthAsync(token));
        AssertHealth(health, restricted: false, alerted: true, lifted: true, firstRed);
        Assert.AreEqual(2L, state.Number("SELECT COUNT(*) FROM events WHERE kind='health-alert';"));
        foreach (string worker in state.Database.Configuration.WorkerIds)
        {
            Assert.Contains(
                "Integration has failed for at least 15 minutes",
                ReadAndCompleteEvents(state, worker)
            );
        }

        checkExitCode = 0;
        health = GitScenarioState.Data(await state.Operations.EvaluateHealthAsync(token));
        Assert.IsTrue(health["passed"]!.GetValue<bool>());
        Assert.IsNull(health["redSince"]);
        Assert.IsFalse(health["teamAlerted"]!.GetValue<bool>());
        Assert.IsFalse(health["restrictionLifted"]!.GetValue<bool>());

        state.Clock.Advance(TimeSpan.FromTicks(1));
        checkExitCode = 17;
        DateTimeOffset nextRed = state.Clock.GetUtcNow();
        health = GitScenarioState.Data(await state.Operations.EvaluateHealthAsync(token));
        AssertHealth(health, restricted: true, alerted: false, lifted: false, nextRed);
        Assert.AreEqual(state.BaseCommit, health["brokenCommit"]!.GetValue<string>());
        await state.CommitAsync(
            state.Operations.GetWorkerPath("worker1"),
            "after-recovery.txt",
            "unrelated work after integration broke again\n",
            token
        );
        JsonObject change = await state.SubmitAsync("worker1", "Unrelated new work", token);
        await state.ReviewAsync("worker2", change, token);
        await state.QueueAsync(change, token);
        await state.Operations.ProcessPendingAsync(token);
        JsonObject refused = await state.ReadAsync(change, token);
        AssertRestrictedRequest(
            Assert.ContainsSingle(refused["integrationRequests"]!.AsArray())!.AsObject()
        );
        Assert.IsEmpty(refused["checks"]!.AsArray());
        Assert.AreEqual(
            state.BaseCommit,
            await state.ResolveAsync(state.IntegrationRef, token)
        );

        state.Clock.Advance(TimeSpan.FromMinutes(15) - TimeSpan.FromTicks(1));
        health = GitScenarioState.Data(await state.Operations.EvaluateHealthAsync(token));
        AssertHealth(health, restricted: true, alerted: false, lifted: false, nextRed);
        Assert.AreEqual(2L, state.Number("SELECT COUNT(*) FROM events WHERE kind='health-alert';"));

        state.Clock.Advance(TimeSpan.FromTicks(1));
        health = GitScenarioState.Data(await state.Operations.EvaluateHealthAsync(token));
        AssertHealth(health, restricted: true, alerted: true, lifted: false, nextRed);
        Assert.AreEqual(4L, state.Number("SELECT COUNT(*) FROM events WHERE kind='health-alert';"));
        foreach (string worker in state.Database.Configuration.WorkerIds)
        {
            Assert.Contains(
                "Integration has failed for at least 15 minutes",
                ReadAndCompleteEvents(state, worker)
            );
        }

        health = GitScenarioState.Data(await state.Operations.EvaluateHealthAsync(token));
        AssertHealth(health, restricted: true, alerted: true, lifted: false, nextRed);
        Assert.AreEqual(4L, state.Number("SELECT COUNT(*) FROM events WHERE kind='health-alert';"));
    }

    [TestMethod]
    [TestCategory("DeliveryReportAndOwnedCleanup")]
    public async Task FinalizeRetainsExactDeliveryEvidenceAndCleanupRemovesOnlyRecordedWorktrees()
    {
        CancellationToken token = testContext.CancellationToken;
        using GitScenarioState state = await GitScenarioState
            .CreateAsync(
                token,
                integrationChecks: [GitScenarioState.ExitCheck(0)],
                finalChecks:
                [
                    new()
                    {
                        Executable = "git",
                        Arguments = ["rev-parse", "HEAD", "HEAD^{tree}"],
                    },
                ]
            )
            .ConfigureAwait(false);
        await state.Operations.InitializeAsync(token).ConfigureAwait(false);
        string alpha = await state
            .CommitAsync(
                state.Operations.GetWorkerPath("worker1"),
                "alpha.txt",
                "delivered alpha\n",
                token
            )
            .ConfigureAwait(false);
        string beta = await state
            .CommitAsync(
                state.Operations.GetWorkerPath("worker2"),
                "beta.txt",
                "delivered beta\n",
                token
            )
            .ConfigureAwait(false);
        JsonObject first = await state
            .SubmitAsync("worker1", "Deliver alpha", token)
            .ConfigureAwait(false);
        JsonObject second = await state
            .SubmitAsync("worker2", "Deliver beta", token)
            .ConfigureAwait(false);
        await state.ReviewAsync("worker2", first, token).ConfigureAwait(false);
        await state.ReviewAsync("worker1", second, token).ConfigureAwait(false);
        await state.QueueAsync(first, token).ConfigureAwait(false);
        await state.QueueAsync(second, token).ConfigureAwait(false);
        await state.Operations.ProcessPendingAsync(token).ConfigureAwait(false);
        foreach (JsonObject change in new[] { first, second })
        {
            JsonObject completion = await state.ReadAsync(change, token).ConfigureAwait(false);
            string integrationEvidence = completion.ToJsonString();
            JsonObject request = Assert.ContainsSingle(
                completion["integrationRequests"]!.AsArray(), integrationEvidence
            )!.AsObject();
            Assert.AreEqual(
                change["head"]!.GetValue<string>(),
                request["head"]!.GetValue<string>(),
                integrationEvidence
            );
            Assert.AreEqual(
                "succeeded", request["state"]!.GetValue<string>(), integrationEvidence
            );
        }
        string finalHead = await state
            .ResolveAsync(state.IntegrationRef, token)
            .ConfigureAwait(false);
        string finalTree = await state
            .ResolveAsync(finalHead + "^{tree}", token)
            .ConfigureAwait(false);
        string unresolvedHead = await state
            .CommitAsync(
                state.Operations.GetWorkerPath("worker1"),
                "unfinished.txt",
                "retained but not integrated\n",
                token
            )
            .ConfigureAwait(false);
        JsonObject unresolved = await state
            .SubmitAsync("worker1", "Still needs review", token)
            .ConfigureAwait(false);
        JsonObject task = GitScenarioState.Data(
            state.Collaboration.Invoke(
                "worker2",
                "task_write",
                new()
                {
                    ["action"] = "create",
                    ["title"] = "Inspect remaining edge",
                    ["body"] = "Unresolved review work",
                }
            )
        );
        JsonObject completedTask = GitScenarioState.Data(
            state.Collaboration.Invoke(
                "worker2",
                "task_write",
                new()
                {
                    ["action"] = "create",
                    ["title"] = "Record completed review",
                    ["body"] = "Resolved before delivery",
                }
            )
        );
        long completedTaskId = completedTask["id"]!.GetValue<long>();
        JsonObject closedTask = GitScenarioState.Data(
            state.Collaboration.Invoke(
                "worker2",
                "task_write",
                new() { ["action"] = "close", ["id"] = completedTaskId }
            )
        );
        Assert.AreEqual(completedTaskId, closedTask["id"]!.GetValue<long>());
        Assert.AreEqual("closed", closedTask["state"]!.GetValue<string>());
        JsonObject expectedTasks = GitScenarioState.Data(
            state.Collaboration.Invoke(
                "owner",
                "task_list",
                new() { ["state"] = "open", ["limit"] = 200 }
            )
        );
        for (int index = 0; index < 200; index++)
        {
            JsonObject additional = GitScenarioState.Data(
                state.Collaboration.Invoke(
                    "worker2",
                    "task_write",
                    new()
                    {
                        ["action"] = "create",
                        ["title"] = $"Unresolved page witness {index}",
                        ["body"] = "Must remain visible in the final delivery report.",
                    }
                )
            );
            expectedTasks["items"]!.AsArray().Add(additional.DeepClone());
        }
        List<(string Ref, string Path)> resources = state.Worktrees();
        Dictionary<string, string> ownedHeads = new(StringComparer.Ordinal);
        foreach ((string reference, string _) in resources)
        {
            ownedHeads.Add(
                reference,
                await state.ResolveAsync(reference, token).ConfigureAwait(false)
            );
        }

        const string linkedBranch = "user/retained-sentinel";
        const string linkedRef = "refs/heads/" + linkedBranch;
        const string linkedContent = "user linked checkout work\n";
        string linkedPath = state.PathFor("adjacent-checkout");
        await state
            .GitAsync(
                state.Repository,
                token,
                "worktree",
                "add",
                "-b",
                linkedBranch,
                "--",
                linkedPath,
                state.BaseCommit
            )
            .ConfigureAwait(false);
        string linkedHead = await state
            .CommitAsync(linkedPath, "sentinel.txt", linkedContent, token)
            .ConfigureAwait(false);
        string sentinelPath = Path.Combine(state.Database.StatePath, "worktrees", "not-owned");
        Directory.CreateDirectory(sentinelPath);
        File.WriteAllText(Path.Combine(sentinelPath, "keep.txt"), "unrecorded path must survive\n");
        byte[] originalInstructions = File.ReadAllBytes(
            Path.Combine(state.Repository, "AGENTS.md")
        );
        string originalIndex = await state
            .GitAsync(state.Repository, token, "ls-files", "--stage")
            .ConfigureAwait(false);
        state.Database.WriteMetadata("lifecycle", "paused");

        JsonObject report = GitScenarioState.Data(
            await state.Operations.FinalizeAsync(token).ConfigureAwait(false)
        );

        Assert.IsTrue(report["accepted"]!.GetValue<bool>());
        Assert.AreEqual(state.BaseCommit, report["baseCommit"]!.GetValue<string>());
        Assert.AreEqual(finalHead, report["finalCommit"]!.GetValue<string>());
        Assert.AreEqual(state.DeliveryRef, report["deliveryRef"]!.GetValue<string>());
        Assert.AreEqual(state.Database.StatePath, report["statePath"]!.GetValue<string>());
        Assert.AreEqual(
            finalHead,
            await state.ResolveAsync(state.DeliveryRef, token).ConfigureAwait(false)
        );
        Assert.AreEqual(
            finalTree,
            await state.ResolveAsync(state.DeliveryRef + "^{tree}", token).ConfigureAwait(false)
        );
        await state
            .GitAsync(
                state.Repository,
                token,
                "merge-base",
                "--is-ancestor",
                state.BaseCommit,
                finalHead
            )
            .ConfigureAwait(false);
        await state
            .GitAsync(state.Repository, token, "merge-base", "--is-ancestor", alpha, finalHead)
            .ConfigureAwait(false);
        await state
            .GitAsync(state.Repository, token, "merge-base", "--is-ancestor", beta, finalHead)
            .ConfigureAwait(false);
        Assert.AreEqual(
            "delivered alpha\n",
            await state
                .GitAsync(state.Repository, token, "show", state.DeliveryRef + ":alpha.txt")
                .ConfigureAwait(false)
        );
        Assert.AreEqual(
            "delivered beta\n",
            await state
                .GitAsync(state.Repository, token, "show", state.DeliveryRef + ":beta.txt")
                .ConfigureAwait(false)
        );
        Assert.IsFalse(File.Exists(Path.Combine(state.IntegrationPath, "unfinished.txt")));
        JsonObject remaining = Assert
            .ContainsSingle(report["unresolvedChanges"]!.AsArray())!
            .AsObject();
        Assert.AreEqual(unresolved["id"]!.GetValue<long>(), remaining["id"]!.GetValue<long>());
        Assert.AreEqual(unresolvedHead, remaining["head"]!.GetValue<string>());
        Assert.AreEqual("worker1", remaining["author"]!.GetValue<string>());
        Assert.AreEqual("Still needs review", remaining["title"]!.GetValue<string>());
        Assert.IsTrue(JsonNode.DeepEquals(expectedTasks["items"], report["unresolvedTasks"]));
        long[] unresolvedTaskIds = report["unresolvedTasks"]!
            .AsArray()
            .Select(item => item!["id"]!.GetValue<long>())
            .ToArray();
        Assert.Contains(task["id"]!.GetValue<long>(), unresolvedTaskIds);
        Assert.DoesNotContain(completedTaskId, unresolvedTaskIds);
        Assert.AreEqual("true", state.Database.ReadMetadata("git:finalized"));
        Assert.AreEqual(
            1L,
            state.Number(
                "SELECT retained FROM git_resources WHERE ref=$ref;",
                ("$ref", state.DeliveryRef)
            )
        );
        string storedReport = state.Database.ReadMetadata("git:delivery_report")!;
        Assert.IsTrue(JsonNode.DeepEquals(JsonNode.Parse(storedReport), report));
        Assert.AreEqual(
            state.Number("SELECT COUNT(*) FROM checks;"),
            (long)report["checks"]!.AsArray().Count
        );
        Assert.IsTrue(report["checks"]!.AsArray().OfType<JsonObject>().Any(check =>
            check["kind"]!.GetValue<string>() == "integration"
            && check["commit"]!.GetValue<string>() == finalHead
            && check["tree"]!.GetValue<string>() == finalTree
        ));
        Dictionary<long, string> retainedLogs = [];
        foreach (JsonObject check in report["checks"]!.AsArray().OfType<JsonObject>())
        {
            JsonObject read = await state
                .InvokeAsync(
                    "owner",
                    "check_read",
                    new() { ["id"] = check["id"]!.GetValue<long>() },
                    token
                )
                .ConfigureAwait(false);
            Assert.IsTrue(
                JsonNode.DeepEquals(Assert.ContainsSingle(read["items"]!.AsArray()), check)
            );
            Assert.AreEqual("passed", check["status"]!.GetValue<string>());
            Assert.AreEqual(0, check["exitCode"]!.GetValue<int>());
            Assert.AreEqual(
                await state
                    .ResolveAsync(check["commit"]!.GetValue<string>() + "^{tree}", token)
                    .ConfigureAwait(false),
                check["tree"]!.GetValue<string>()
            );
            string log = File.ReadAllText(check["logPath"]!.GetValue<string>());
            retainedLogs.Add(check["id"]!.GetValue<long>(), log);
            if (check["kind"]!.GetValue<string>() == "final")
            {
                Assert.AreEqual("git", check["command"]!["executable"]!.GetValue<string>());
                JsonArray arguments = check["command"]!["arguments"]!.AsArray();
                Assert.HasCount(3, arguments);
                Assert.AreEqual("rev-parse", arguments[0]!.GetValue<string>());
                Assert.AreEqual("HEAD", arguments[1]!.GetValue<string>());
                Assert.AreEqual("HEAD^{tree}", arguments[2]!.GetValue<string>());
                string normalized = log.ReplaceLineEndings("\n");
                const string outputStart = "Configured check execution: ProcessRunner.\nstdout:\n";
                const string outputEnd = "\nstderr:\n";
                Assert.StartsWith(outputStart, normalized);
                int end = normalized.IndexOf(
                    outputEnd, outputStart.Length, StringComparison.Ordinal
                );
                Assert.IsGreaterThanOrEqualTo(outputStart.Length, end);
                string[] output = normalized[outputStart.Length..end]
                    .Split('\n', StringSplitOptions.RemoveEmptyEntries);
                Assert.HasCount(2, output);
                Assert.AreEqual(finalHead, output[0]);
                Assert.AreEqual(finalTree, output[1]);
            }
            else
            {
                Assert.AreEqual(
                    OwnedChildControl.ExecutablePath,
                    check["command"]!["executable"]!.GetValue<string>()
                );
                Assert.AreEqual("exit", check["command"]!["arguments"]![0]!.GetValue<string>());
                Assert.AreEqual("0", check["command"]!["arguments"]![1]!.GetValue<string>());
                Assert.Contains("owned check exit 0", log);
            }
        }

        JsonObject finalCheck = Assert.ContainsSingle(
            report["checks"]!
                .AsArray()
                .OfType<JsonObject>()
                .Where(check => check["kind"]!.GetValue<string>() == "final")
        );
        Assert.AreEqual(finalHead, finalCheck["commit"]!.GetValue<string>());
        Assert.AreEqual(finalTree, finalCheck["tree"]!.GetValue<string>());
        Assert.IsNull(finalCheck["changeId"]);
        Assert.AreEqual(
            state.Database.Configuration.FinalChecks[0].TimeoutSeconds,
            finalCheck["command"]!["timeoutSeconds"]!.GetValue<int>()
        );
        Assert.AreEqual(
            report["finalCheckBatch"]!.GetValue<string>(),
            state.Text(
                "SELECT batch FROM checks WHERE id=$id;",
                ("$id", finalCheck["id"]!.GetValue<long>())
            )
        );
        foreach ((string reference, string path) in resources)
        {
            Assert.IsTrue(Directory.Exists(path), "Finalize removed owned work: " + path);
            Assert.AreEqual(
                ownedHeads[reference],
                await state.ResolveAsync(reference, token).ConfigureAwait(false)
            );
        }

        Assert.IsEmpty(
            await state
                .GitAsync(linkedPath, token, "status", "--porcelain=v1")
                .ConfigureAwait(false)
        );
        string registrations = await state
            .GitAsync(state.Repository, token, "worktree", "list", "--porcelain", "-z")
            .ConfigureAwait(false);
        Assert.Contains(
            Path.GetFullPath(linkedPath),
            registrations
                .Split('\0')
                .Where(field => field.StartsWith("worktree ", StringComparison.Ordinal))
                .Select(field => Path.GetFullPath(field["worktree ".Length..]))
        );
        Assert.AreEqual(
            0L,
            state.Number(
                "SELECT COUNT(*) FROM git_resources WHERE ref=$ref OR path=$path;",
                ("$ref", linkedRef),
                ("$path", linkedPath)
            )
        );

        JsonObject cleanup = GitScenarioState.Data(
            await state.Operations.CleanupAsync(token).ConfigureAwait(false)
        );
        string[] expectedRemoved = [.. resources.Select(resource => resource.Ref)];
        string[] actualRemoved =
        [
            .. cleanup["removed"]!.AsArray().Select(item => item!.GetValue<string>()),
        ];
        CollectionAssert.AreEquivalent(expectedRemoved, actualRemoved);
        Assert.AreEqual(state.DeliveryRef, cleanup["retainedDeliveryRef"]!.GetValue<string>());
        foreach ((string reference, string path) in resources)
        {
            Assert.IsFalse(
                Directory.Exists(path),
                "Explicit cleanup retained owned worktree: " + path
            );
            Assert.AreEqual(
                "removed",
                state.Text("SELECT state FROM git_resources WHERE ref=$ref;", ("$ref", reference))
            );
            Assert.AreEqual(
                "",
                await state
                    .GitAsync(
                        state.Repository,
                        token,
                        "for-each-ref",
                        "--format=%(refname)",
                        reference
                    )
                    .ConfigureAwait(false)
            );
        }

        Assert.AreEqual(storedReport, state.Database.ReadMetadata("git:delivery_report"));
        Assert.AreEqual(
            finalHead,
            await state.ResolveAsync(state.DeliveryRef, token).ConfigureAwait(false)
        );
        Assert.AreEqual(
            finalTree,
            await state.ResolveAsync(state.DeliveryRef + "^{tree}", token).ConfigureAwait(false)
        );
        Assert.AreEqual(
            state.BaseCommit,
            await state.ResolveAsync("HEAD", token).ConfigureAwait(false)
        );
        Assert.AreEqual(
            originalIndex,
            await state
                .GitAsync(state.Repository, token, "ls-files", "--stage")
                .ConfigureAwait(false)
        );
        CollectionAssert.AreEqual(
            originalInstructions,
            File.ReadAllBytes(Path.Combine(state.Repository, "AGENTS.md"))
        );
        Assert.IsTrue(Directory.Exists(linkedPath));
        Assert.AreEqual(
            linkedRef,
            (
                await state.GitAsync(linkedPath, token, "symbolic-ref", "HEAD")
                    .ConfigureAwait(false)
            ).Trim()
        );
        Assert.AreEqual(
            linkedHead,
            await state.ResolveAsync(linkedRef, token).ConfigureAwait(false)
        );
        Assert.AreEqual(linkedContent, File.ReadAllText(Path.Combine(linkedPath, "sentinel.txt")));
        Assert.AreEqual(
            "unrecorded path must survive\n",
            File.ReadAllText(Path.Combine(sentinelPath, "keep.txt"))
        );
        foreach (JsonObject check in report["checks"]!.AsArray().OfType<JsonObject>())
        {
            Assert.AreEqual(
                retainedLogs[check["id"]!.GetValue<long>()],
                File.ReadAllText(check["logPath"]!.GetValue<string>())
            );
        }
    }

    [TestMethod]
    [TestCategory("DeliveryReportAndOwnedCleanup")]
    public async Task DirtyOwnedWorkBlocksCleanupBeforeAnyRecordedResourceRemoval()
    {
        CancellationToken token = testContext.CancellationToken;
        using GitScenarioState state = await GitScenarioState
            .CreateAsync(token)
            .ConfigureAwait(false);
        await state.Operations.InitializeAsync(token).ConfigureAwait(false);
        List<(string Ref, string Path)> resources = state.Worktrees();
        string dirtyPath = state.Operations.GetWorkerPath("worker2");
        File.WriteAllText(
            Path.Combine(dirtyPath, "untracked-work.txt"),
            "do not discard unfinished work\n"
        );
        state.Database.WriteMetadata("lifecycle", "paused");
        JsonObject report = GitScenarioState.Data(
            await state.Operations.FinalizeAsync(token).ConfigureAwait(false)
        );
        string reportBytes = state.Database.ReadMetadata("git:delivery_report")!;

        JsonObject cleanup = await state.Operations.CleanupAsync(token).ConfigureAwait(false);

        Assert.IsFalse(cleanup["ok"]!.GetValue<bool>());
        Assert.AreEqual("conflict", cleanup["code"]!.GetValue<string>());
        Assert.Contains("unresolved or uncommitted work", cleanup["message"]!.GetValue<string>());
        Assert.AreEqual(reportBytes, state.Database.ReadMetadata("git:delivery_report"));
        Assert.AreEqual("false", state.Database.ReadMetadata("git:cleaning"));
        Assert.IsNull(state.Database.ReadMetadata("git:cleaned"));
        Assert.AreEqual(
            "do not discard unfinished work\n",
            File.ReadAllText(Path.Combine(dirtyPath, "untracked-work.txt"))
        );
        foreach ((string reference, string path) in resources)
        {
            Assert.IsTrue(
                Directory.Exists(path),
                "Cleanup removed a resource before completing preflight."
            );
            Assert.AreEqual(
                state.BaseCommit,
                await state.ResolveAsync(reference, token).ConfigureAwait(false)
            );
            Assert.AreEqual(
                "ready",
                state.Text("SELECT state FROM git_resources WHERE ref=$ref;", ("$ref", reference))
            );
        }

        Assert.AreEqual(
            report["finalCommit"]!.GetValue<string>(),
            await state.ResolveAsync(state.DeliveryRef, token).ConfigureAwait(false)
        );
        Assert.AreEqual(
            state.BaseCommit,
            await state.ResolveAsync("HEAD", token).ConfigureAwait(false)
        );
    }

    private static void AssertHealth(
        JsonObject health,
        bool restricted,
        bool alerted,
        bool lifted,
        DateTimeOffset redSince
    )
    {
        Assert.IsFalse(health["passed"]!.GetValue<bool>());
        Assert.AreEqual(restricted, health["fixMainOnly"]!.GetValue<bool>());
        Assert.AreEqual(alerted, health["teamAlerted"]!.GetValue<bool>());
        Assert.AreEqual(lifted, health["restrictionLifted"]!.GetValue<bool>());
        Assert.AreEqual(
            redSince,
            DateTimeOffset.Parse(
                health["redSince"]!.GetValue<string>(),
                CultureInfo.InvariantCulture
            )
        );
    }

    private static void AssertRestrictedRequest(JsonObject request)
    {
        Assert.AreEqual("failed", request["state"]!.GetValue<string>());
        Assert.AreEqual("check_failed", request["resultCode"]!.GetValue<string>());
        Assert.Contains(
            "only changes linked to the dedicated integration-health task",
            request["message"]!.GetValue<string>()
        );
        Assert.IsNull(request["integratedCommit"]);
    }

    private static string ReadAndCompleteEvents(GitScenarioState state, string actor)
    {
        DeliveryBatch? batch = state.Collaboration.ClaimBatch(actor, bootstrap: false);
        Assert.IsNotNull(batch);
        Assert.AreEqual(actor, batch.Recipient);
        string prompt = batch.Prompt;
        Assert.AreEqual(actor, JsonNode.Parse(prompt)!["recipient"]!.GetValue<string>());
        state.Collaboration.CompleteBatch(batch.Id);
        return prompt;
    }
}
