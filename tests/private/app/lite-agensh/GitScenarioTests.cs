using System.Text.Json.Nodes;
using LiteAgensh;
using Microsoft.Data.Sqlite;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace LiteAgensh.Tests;

[TestClass]
public sealed class GitScenarioTests(TestContext testContext)
{
    [TestMethod]
    [DataRow(true)]
    [DataRow(false)]
    [Timeout(120000, CooperativeCancellation = true)]
    public async Task InitializeWithFailedGitIdentityCheckRetainsExitTimeoutAndStderr(
        bool silentFailure
    )
    {
        CancellationToken token = testContext.CancellationToken;
        using GitScenario fixture = await GitScenario.CreateAsync(token);
        string worker = fixture.Operations.GetWorkerPath("worker1");
        if (silentFailure)
        {
            await fixture.GitAsync(worker, token, "checkout", "--detach", "--quiet");
        }
        else
        {
            File.SetAttributes(Path.Combine(worker, ".git"), FileAttributes.Normal);
            File.WriteAllText(
                Path.Combine(worker, ".git"),
                "gitdir: " + Path.Combine(fixture.Fixture.RootPath, "missing-git-directory")
            );
        }

        OperationException failure = await Assert.ThrowsExactlyAsync<OperationException>(
            () => fixture.Operations.InitializeAsync(token)
        );

        Assert.AreEqual("unavailable", failure.Code);
        string message = failure.Message;
        Assert.Contains(
            silentFailure ? "Git symbolic-ref failed" : "Git rev-parse failed",
            message
        );
        Assert.Contains(silentFailure ? "exit: 1" : "exit: 128", message);
        Assert.Contains("timeout: False", message);
        if (!silentFailure)
        {
            Assert.Contains("fatal:", message);
        }

        await fixture.AssertOriginalPreservedAsync(token);
    }

    [TestMethod]
    [Timeout(240000, CooperativeCancellation = true)]
    public async Task TwoCandidatesIntegrateSeriallyCheckExactTreesAndPreserveDirtyOriginal()
    {
        CancellationToken token = testContext.CancellationToken;
        long started = System.Diagnostics.Stopwatch.GetTimestamp();
        void Phase(string name) =>
            testContext.WriteLine(
                $"Git scenario phase: {name}; "
                    + $"elapsed: {System.Diagnostics.Stopwatch.GetElapsedTime(started)}."
            );
        Phase("initialize repository and worktrees");
        using GitScenario fixture = await GitScenario.CreateAsync(
            token,
            configuration =>
                configuration with
                {
                    CandidateChecks =
                    [
                        new() { Executable = "git", Arguments = ["diff", "--check"] },
                    ],
                    FinalChecks =
                    [
                        new()
                        {
                            Executable = "pwsh",
                            Arguments =
                            [
                                "-NoProfile",
                                "-Command",
                                "$marker = '"
                                    + Path.Combine(configuration.StatePath, "final-check.txt")
                                        .Replace("'", "''", StringComparison.Ordinal)
                                    + "'; $owned = "
                                    + "[System.Diagnostics.Process]::GetCurrentProcess(); "
                                    + "[IO.File]::WriteAllText($marker, "
                                    + "('started pid=' + $owned.Id + ' ticks=' "
                                    + "+ $owned.StartTime.ToUniversalTime().Ticks)); "
                                    + "$owned.Dispose(); "
                                    + "if ((Get-Content a.txt -Raw) -ne 'a-fixed' -or "
                                    + "(Get-Content b.txt -Raw) -ne 'b-fixed') { exit 7 }; "
                                    + "'both independent fixes present'; "
                                    + "[IO.File]::AppendAllText($marker, '; completed exit=0')",
                            ],
                        },
                    ],
                },
            trace: message => testContext.WriteLine(message)
        );
        Phase("commit independent worker edits");
        await fixture.CommitAsync("worker1", "a.txt", "a-fixed", token);
        await fixture.CommitAsync("worker2", "b.txt", "b-fixed", token);
        Phase("submit and approve exact candidates");
        JsonObject first = await fixture.SubmitAsync("worker1", token);
        JsonObject second = await fixture.SubmitAsync("worker2", token);
        await fixture.ApproveAsync(first, "worker2", token);
        await fixture.ApproveAsync(second, "worker1", token);
        JsonObject pending = await fixture.QueueAsync(first, token);
        Assert.AreEqual("pending", pending["state"]!.GetValue<string>());
        JsonObject repeated = await fixture.QueueAsync(first, token);
        Assert.AreEqual(
            pending["requestId"]!.GetValue<long>(),
            repeated["requestId"]!.GetValue<long>()
        );
        await fixture.QueueAsync(second, token);

        Phase("integrate queued candidates and run git diff --check");
        await fixture.Operations.ProcessPendingAsync(token);

        Phase("verify integrated commit and tree checks");
        foreach (JsonObject change in new[] { first, second })
        {
            JsonObject integrated = await fixture.ReadAsync(change, token);
            Assert.AreEqual("integrated", integrated["state"]!.GetValue<string>());
            JsonObject request = Assert.ContainsSingle(
                integrated["integrationRequests"]!.AsArray().OfType<JsonObject>()
            );
            Assert.AreEqual("succeeded", request["state"]!.GetValue<string>());
            string commit = request["integratedCommit"]!.GetValue<string>();
            JsonObject check = Assert.ContainsSingle(
                integrated["checks"]!.AsArray().OfType<JsonObject>()
            );
            Assert.AreEqual("candidate", check["kind"]!.GetValue<string>());
            Assert.AreEqual("passed", check["status"]!.GetValue<string>());
            Assert.AreEqual(commit, check["commit"]!.GetValue<string>());
            Assert.AreEqual(
                (
                    await fixture.GitAsync(
                        fixture.Fixture.RepositoryPath,
                        token,
                        "rev-parse",
                        commit + "^{tree}"
                    )
                ).Trim(),
                check["tree"]!.GetValue<string>()
            );
            Assert.IsTrue(File.Exists(check["logPath"]!.GetValue<string>()));
        }

        fixture.Database.WriteMetadata("lifecycle", "paused");
        Phase("finalize with combined-fix PowerShell check");
        JsonObject delivery;
        try
        {
            delivery = Scenario.Data(await fixture.Operations.FinalizeAsync(token));
        }
        finally
        {
            WriteFinalizationDiagnostics(fixture);
        }

        Assert.IsTrue(delivery["accepted"]!.GetValue<bool>());
        Assert.AreNotEqual(
            delivery["baseCommit"]!.GetValue<string>(),
            delivery["finalCommit"]!.GetValue<string>()
        );
        Assert.AreEqual(
            "a-fixed",
            (
                await fixture.GitAsync(
                    fixture.Fixture.RepositoryPath,
                    token,
                    "show",
                    delivery["finalCommit"]!.GetValue<string>() + ":a.txt"
                )
            ).Trim()
        );
        Assert.AreEqual(
            "b-fixed",
            (
                await fixture.GitAsync(
                    fixture.Fixture.RepositoryPath,
                    token,
                    "show",
                    delivery["finalCommit"]!.GetValue<string>() + ":b.txt"
                )
            ).Trim()
        );
        Assert.IsTrue(File.Exists(Path.Combine(fixture.Fixture.StatePath, "run.sqlite")));
        Phase("verify original checkout before cleanup");
        await fixture.AssertOriginalPreservedAsync(token);
        Phase("cleanup owned worktrees");
        Scenario.Data(await fixture.Operations.CleanupAsync(token));
        Assert.IsFalse(Directory.Exists(fixture.Operations.GetWorkerPath("worker1")));
        Assert.AreEqual(
            delivery["finalCommit"]!.GetValue<string>(),
            (
                await fixture.GitAsync(
                    fixture.Fixture.RepositoryPath,
                    token,
                    "rev-parse",
                    delivery["deliveryRef"]!.GetValue<string>()
                )
            ).Trim()
        );
        Phase("verify original checkout after cleanup");
        await fixture.AssertOriginalPreservedAsync(token);
        Phase("completed");
    }

    private void WriteFinalizationDiagnostics(GitScenario fixture)
    {
        try
        {
            string marker = Path.Combine(fixture.Database.StatePath, "final-check.txt");
            testContext.WriteLine(
                File.Exists(marker)
                    ? $"Final command marker: {File.ReadAllText(marker)}"
                    : "Final command marker absent; first PowerShell statement not observed."
            );
            using SqliteConnection connection = fixture.Database.Open();
            foreach (string table in new[] { "checks", "process_leases" })
            {
                using SqliteCommand command = connection.CreateCommand();
                command.CommandTimeout = 2;
                command.CommandText = $"SELECT * FROM {table} ORDER BY id DESC LIMIT 8;";
                using SqliteDataReader rows = command.ExecuteReader();
                while (rows.Read())
                {
                    IEnumerable<string> fields = Enumerable
                        .Range(0, rows.FieldCount)
                        .Select(index => $"{rows.GetName(index)}={rows.GetValue(index)}");
                    testContext.WriteLine($"Finalization {table}: {string.Join("; ", fields)}");
                    if (table == "checks" && File.Exists((string)rows["log_path"]))
                    {
                        foreach (string line in File.ReadLines((string)rows["log_path"]).Take(12))
                        {
                            testContext.WriteLine(
                                $"Final check log: {line[..Math.Min(line.Length, 1000)]}"
                            );
                        }
                    }
                }
            }
        }
        catch (Exception exception)
        {
            testContext.WriteLine($"Secondary finalization diagnostic failure: {exception}");
        }
    }

    [TestMethod]
    [Timeout(120000, CooperativeCancellation = true)]
    [DataRow("self-review", "review_required")]
    [DataRow("protected-owner", "review_required")]
    [DataRow("stale-head", "stale_candidate")]
    [DataRow("check-failed", "check_failed")]
    public async Task InvalidCandidatesRetainVisibleFailureAndWorkerCommit(
        string scenario,
        string expectedCode
    )
    {
        CancellationToken token = testContext.CancellationToken;
        using GitScenario fixture = await GitScenario.CreateAsync(
            token,
            configuration =>
                configuration with
                {
                    ProtectedPaths = scenario == "protected-owner" ? ["a.txt"] : [],
                    CandidateChecks =
                        scenario == "check-failed"
                            ?
                            [
                                new()
                                {
                                    Executable = "pwsh",
                                    Arguments = ["-NoProfile", "-Command", "exit 7"],
                                },
                            ]
                            : [],
                }
        );
        string workerHead = await fixture.CommitAsync("worker1", "a.txt", "candidate fix", token);
        JsonObject change = await fixture.SubmitAsync("worker1", token);
        await fixture.ApproveAsync(
            change,
            scenario == "self-review" ? "worker1" : "worker2",
            token
        );
        if (scenario == "stale-head")
        {
            workerHead = await fixture.CommitAsync(
                "worker1",
                "a.txt",
                "newer unreviewed fix",
                token
            );
        }

        await fixture.QueueAsync(change, token);
        await fixture.Operations.ProcessPendingAsync(token);

        JsonObject retained = await fixture.ReadAsync(change, token);
        Assert.AreEqual("open", retained["state"]!.GetValue<string>());
        JsonObject failure = Assert.ContainsSingle(
            retained["integrationRequests"]!.AsArray().OfType<JsonObject>()
        );
        Assert.AreEqual("failed", failure["state"]!.GetValue<string>());
        Assert.AreEqual(expectedCode, failure["resultCode"]!.GetValue<string>());
        Assert.IsNull(failure["integratedCommit"]);
        Assert.AreEqual(
            workerHead,
            (
                await fixture.GitAsync(
                    fixture.Operations.GetWorkerPath("worker1"),
                    token,
                    "rev-parse",
                    "HEAD"
                )
            ).Trim()
        );
        Assert.AreEqual(
            fixture.Database.Configuration.BaseRevision,
            (
                await fixture.GitAsync(
                    fixture.Fixture.RepositoryPath,
                    token,
                    "rev-parse",
                    fixture.Database.ReadMetadata("git:integration_branch")!
                )
            ).Trim()
        );
        if (scenario == "protected-owner")
        {
            await fixture.ApproveAsync(change, "owner", token);
            await fixture.QueueAsync(change, token);
            await fixture.Operations.ProcessPendingAsync(token);
            Assert.AreEqual(
                "integrated",
                (await fixture.ReadAsync(change, token))["state"]!.GetValue<string>()
            );
        }

        await fixture.AssertOriginalPreservedAsync(token);
    }

    [TestMethod]
    [Timeout(120000, CooperativeCancellation = true)]
    public async Task ChangedHeadInvalidatesReviewsAndPreservesOmittedTaskLinksUntilExplicitClear()
    {
        CancellationToken token = testContext.CancellationToken;
        using GitScenario fixture = await GitScenario.CreateAsync(token);
        string oldHead = await fixture.CommitAsync("worker1", "a.txt", "first reviewed fix", token);
        JsonObject change = await fixture.SubmitAsync("worker1", token);
        await fixture.ApproveAsync(change, "worker2", token);
        JsonObject edited = await fixture.CallAsync(
            "worker1",
            "change_write",
            new
            {
                action = "update",
                id = Scenario.Id(change),
                title = "Reworded without changing candidate",
            },
            token
        );
        Assert.AreEqual(oldHead, edited["head"]!.GetValue<string>());
        Assert.AreEqual(1L, Assert.ContainsSingle(edited["taskIds"]!.AsArray())!.GetValue<long>());
        JsonObject unchanged = await fixture.ReadAsync(change, token);
        Assert.AreEqual(
            oldHead,
            Assert.ContainsSingle(unchanged["reviews"]!.AsArray().OfType<JsonObject>())[
                "head"
            ]!.GetValue<string>()
        );

        string newHead = await fixture.CommitAsync("worker1", "a.txt", "new unreviewed fix", token);
        edited = await fixture.CallAsync(
            "worker1",
            "change_write",
            new
            {
                action = "update",
                id = Scenario.Id(change),
                head = newHead,
            },
            token
        );
        Assert.AreEqual("Reworded without changing candidate", edited["title"]!.GetValue<string>());
        Assert.AreEqual(1L, Assert.ContainsSingle(edited["taskIds"]!.AsArray())!.GetValue<long>());
        await fixture.QueueAsync(edited, token);
        await fixture.Operations.ProcessPendingAsync(token);
        JsonObject rejected = await fixture.ReadAsync(change, token);
        JsonObject review = Assert.ContainsSingle(
            rejected["reviews"]!.AsArray().OfType<JsonObject>()
        );
        Assert.AreEqual("approve", review["decision"]!.GetValue<string>());
        Assert.AreEqual(oldHead, review["head"]!.GetValue<string>());
        Assert.AreEqual(
            "review_required",
            Assert.ContainsSingle(rejected["integrationRequests"]!.AsArray().OfType<JsonObject>())[
                "resultCode"
            ]!.GetValue<string>()
        );
        Assert.AreEqual("open", rejected["state"]!.GetValue<string>());

        JsonObject cleared = await fixture.CallAsync(
            "worker1",
            "change_write",
            new
            {
                action = "update",
                id = Scenario.Id(change),
                taskIds = Array.Empty<int>(),
            },
            token
        );
        Assert.IsEmpty(cleared["taskIds"]!.AsArray());
        Assert.AreEqual(newHead, cleared["head"]!.GetValue<string>());
        await fixture.ApproveAsync(cleared, "worker2", token);
        await fixture.QueueAsync(cleared, token);
        await fixture.Operations.ProcessPendingAsync(token);
        Assert.AreEqual(
            "integrated",
            (await fixture.ReadAsync(change, token))["state"]!.GetValue<string>()
        );
        await fixture.AssertOriginalPreservedAsync(token);
    }

    [TestMethod]
    [Timeout(120000, CooperativeCancellation = true)]
    public async Task ConflictingSecondCandidateRetainsBothCommitsAndFirstIntegration()
    {
        CancellationToken token = testContext.CancellationToken;
        using GitScenario fixture = await GitScenario.CreateAsync(token);
        await fixture.CommitAsync("worker1", "shared.txt", "first conflicting edit", token);
        string secondHead = await fixture.CommitAsync(
            "worker2",
            "shared.txt",
            "second conflicting edit",
            token
        );
        JsonObject first = await fixture.SubmitAsync("worker1", token);
        JsonObject second = await fixture.SubmitAsync("worker2", token);
        await fixture.ApproveAsync(first, "worker2", token);
        await fixture.ApproveAsync(second, "worker1", token);
        await fixture.QueueAsync(first, token);
        await fixture.Operations.ProcessPendingAsync(token);
        string integration = (
            await fixture.GitAsync(
                fixture.Fixture.RepositoryPath,
                token,
                "rev-parse",
                fixture.Database.ReadMetadata("git:integration_branch")!
            )
        ).Trim();
        await fixture.QueueAsync(second, token);

        await fixture.Operations.ProcessPendingAsync(token);

        JsonObject retained = await fixture.ReadAsync(second, token);
        JsonObject failed = Assert.ContainsSingle(
            retained["integrationRequests"]!.AsArray().OfType<JsonObject>()
        );
        Assert.AreEqual("conflict", failed["resultCode"]!.GetValue<string>());
        Assert.AreEqual("failed", failed["state"]!.GetValue<string>());
        Assert.AreEqual(
            integration,
            (
                await fixture.GitAsync(
                    fixture.Fixture.RepositoryPath,
                    token,
                    "rev-parse",
                    fixture.Database.ReadMetadata("git:integration_branch")!
                )
            ).Trim()
        );
        Assert.AreEqual(
            secondHead,
            (
                await fixture.GitAsync(
                    fixture.Operations.GetWorkerPath("worker2"),
                    token,
                    "rev-parse",
                    "HEAD"
                )
            ).Trim()
        );
        Assert.AreEqual(
            "second conflicting edit",
            File.ReadAllText(
                Path.Combine(fixture.Operations.GetWorkerPath("worker2"), "shared.txt")
            )
        );
        await fixture.AssertOriginalPreservedAsync(token);
    }

    [TestMethod]
    [Timeout(120000, CooperativeCancellation = true)]
    public async Task HealthFifteenAndSixtyMinuteTransitionsDoNotPassFailedFinalChecks()
    {
        CancellationToken token = testContext.CancellationToken;
        ScenarioClock clock = new();
        using GitScenario fixture = await GitScenario.CreateAsync(
            token,
            configuration =>
                configuration with
                {
                    IntegrationChecks =
                    [
                        new()
                        {
                            Executable = "pwsh",
                            Arguments = ["-NoProfile", "-Command", "exit 5"],
                        },
                    ],
                    FinalChecks =
                    [
                        new()
                        {
                            Executable = "pwsh",
                            Arguments =
                            [
                                "-NoProfile",
                                "-Command",
                                "'retained final failure'; exit 9",
                            ],
                        },
                    ],
                },
            clock
        );
        JsonObject health = Scenario.Data(await fixture.Operations.EvaluateHealthAsync(token));
        Assert.IsFalse(health["passed"]!.GetValue<bool>());
        Assert.IsTrue(health["fixMainOnly"]!.GetValue<bool>());
        clock.Advance(TimeSpan.FromMinutes(15) - TimeSpan.FromMilliseconds(1));
        Assert.IsFalse(
            Scenario.Data(await fixture.Operations.EvaluateHealthAsync(token))[
                "teamAlerted"
            ]!.GetValue<bool>()
        );
        clock.Advance(TimeSpan.FromMilliseconds(1));
        Assert.IsTrue(
            Scenario.Data(await fixture.Operations.EvaluateHealthAsync(token))[
                "teamAlerted"
            ]!.GetValue<bool>()
        );
        clock.Advance(TimeSpan.FromMinutes(45) - TimeSpan.FromMilliseconds(1));
        Assert.IsFalse(
            Scenario.Data(await fixture.Operations.EvaluateHealthAsync(token))[
                "restrictionLifted"
            ]!.GetValue<bool>()
        );
        clock.Advance(TimeSpan.FromMilliseconds(1));
        health = Scenario.Data(await fixture.Operations.EvaluateHealthAsync(token));
        Assert.IsTrue(health["restrictionLifted"]!.GetValue<bool>());
        Assert.IsFalse(health["fixMainOnly"]!.GetValue<bool>());
        Assert.IsFalse(health["passed"]!.GetValue<bool>());
        fixture.Database.WriteMetadata("lifecycle", "paused");

        JsonObject final = await fixture.Operations.FinalizeAsync(token);

        Scenario.Failure(final, "check_failed");
        JsonObject report = final["data"]!.AsObject();
        Assert.IsFalse(report["accepted"]!.GetValue<bool>());
        JsonObject check = Assert.ContainsSingle(
            report["checks"]!
                .AsArray()
                .OfType<JsonObject>()
                .Where(item => item["kind"]!.GetValue<string>() == "final")
        );
        Assert.AreEqual("failed", check["status"]!.GetValue<string>());
        Assert.AreEqual(9, check["exitCode"]!.GetValue<int>());
        Assert.Contains(
            "retained final failure",
            File.ReadAllText(check["logPath"]!.GetValue<string>())
        );
        Assert.IsTrue(Directory.Exists(fixture.Operations.GetWorkerPath("worker1")));
        Assert.AreEqual(
            report["finalCommit"]!.GetValue<string>(),
            (
                await fixture.GitAsync(
                    fixture.Fixture.RepositoryPath,
                    token,
                    "rev-parse",
                    report["deliveryRef"]!.GetValue<string>()
                )
            ).Trim()
        );
        Assert.Contains("accepted", fixture.Database.ReadMetadata("git:delivery_report")!);
        await fixture.AssertOriginalPreservedAsync(token);
    }
}
