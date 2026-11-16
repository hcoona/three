using System.Diagnostics;
using System.Globalization;
using System.Text.Json.Nodes;
using LiteAgensh.Tests.Support;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace LiteAgensh.Tests;

[TestClass]
public sealed class GitCandidateSpecs(TestContext testContext)
{
    private static readonly string[] OwnedEnvironmentNames =
    [
        "GIT_CONFIG_GLOBAL",
        "GIT_CONFIG_SYSTEM",
        "GIT_CONFIG_NOSYSTEM",
        "HOME",
        "USERPROFILE",
        "XDG_CONFIG_HOME",
    ];

    [TestMethod]
    [TestCategory("DirtyCheckoutIsolation")]
    public async Task DirtyCheckoutRemainsByteExactThroughCheckedDelivery()
    {
        CancellationToken token = testContext.CancellationToken;
        Dictionary<string, string?> hostEnvironment = OwnedEnvironmentNames.ToDictionary(
            name => name,
            Environment.GetEnvironmentVariable,
            StringComparer.Ordinal
        );
        using GitScenarioState state = await GitScenarioState
            .CreateAsync(token)
            .ConfigureAwait(false);
        string tracked = Path.Combine(state.Repository, "tracked.txt");
        File.WriteAllText(tracked, "staged owned edit\n");
        await state
            .GitAsync(state.Repository, token, "add", "--", "tracked.txt")
            .ConfigureAwait(false);
        File.WriteAllText(tracked, "unstaged owned edit on top of staged edit\n");
        string untracked = Path.Combine(state.Repository, "untracked.txt");
        File.WriteAllText(untracked, "never import this untracked content\n");
        byte[] trackedBytes = File.ReadAllBytes(tracked);
        byte[] untrackedBytes = File.ReadAllBytes(untracked);
        byte[] instructions = File.ReadAllBytes(Path.Combine(state.Repository, "AGENTS.md"));
        string indexPath = Path.Combine(state.Repository, ".git", "index");
        byte[] localConfig = File.ReadAllBytes(Path.Combine(state.Repository, ".git", "config"));
        byte[] globalConfig = File.ReadAllBytes(state.GlobalConfig);
        byte[] systemConfig = File.ReadAllBytes(state.SystemConfig);
        string status = await state
            .GitAsync(state.Repository, token, "status", "--porcelain=v1", "--untracked-files=all")
            .ConfigureAwait(false);
        Assert.Contains("MM tracked.txt", status);
        Assert.Contains("?? untracked.txt", status);
        string indexEntries = await state
            .GitAsync(state.Repository, token, "ls-files", "--stage")
            .ConfigureAwait(false);
        string indexTree = await state
            .GitAsync(state.Repository, token, "write-tree")
            .ConfigureAwait(false);
        byte[] indexBytes = File.ReadAllBytes(indexPath);

        await state.Operations.InitializeAsync(token).ConfigureAwait(false);
        string workerPath = state.Operations.GetWorkerPath("worker1");
        string submitted = await state
            .CommitAsync(workerPath, "feature.txt", "owned delivered feature\n", token)
            .ConfigureAwait(false);
        JsonObject change = await state
            .SubmitAsync("worker1", "Isolated feature", token)
            .ConfigureAwait(false);
        await state.ReviewAsync("worker2", change, token).ConfigureAwait(false);
        await state.QueueAsync(change, token).ConfigureAwait(false);
        await state.Operations.ProcessPendingAsync(token).ConfigureAwait(false);
        JsonObject integrated = await state.ReadAsync(change, token).ConfigureAwait(false);
        Assert.AreEqual("integrated", integrated["state"]!.GetValue<string>());
        // Git seam's quiescent-owner prerequisite.
        state.Database.WriteMetadata("lifecycle", "paused");
        JsonObject report = GitScenarioState.Data(
            await state.Operations.FinalizeAsync(token).ConfigureAwait(false)
        );

        Assert.IsTrue(report["accepted"]!.GetValue<bool>());
        Assert.AreEqual(
            state.BaseCommit,
            await state.ResolveAsync("HEAD", token).ConfigureAwait(false)
        );
        Assert.AreEqual(
            "refs/heads/main",
            (
                await state
                    .GitAsync(state.Repository, token, "symbolic-ref", "HEAD")
                    .ConfigureAwait(false)
            ).Trim()
        );
        Assert.AreEqual(
            status,
            await state
                .GitAsync(
                    state.Repository,
                    token,
                    "status",
                    "--porcelain=v1",
                    "--untracked-files=all"
                )
                .ConfigureAwait(false)
        );
        Assert.AreEqual(
            indexEntries,
            await state
                .GitAsync(state.Repository, token, "ls-files", "--stage")
                .ConfigureAwait(false)
        );
        Assert.AreEqual(
            indexTree,
            await state.GitAsync(state.Repository, token, "write-tree").ConfigureAwait(false)
        );
        CollectionAssert.AreEqual(indexBytes, File.ReadAllBytes(indexPath));
        CollectionAssert.AreEqual(trackedBytes, File.ReadAllBytes(tracked));
        CollectionAssert.AreEqual(untrackedBytes, File.ReadAllBytes(untracked));
        CollectionAssert.AreEqual(
            instructions,
            File.ReadAllBytes(Path.Combine(state.Repository, "AGENTS.md"))
        );
        CollectionAssert.AreEqual(
            localConfig,
            File.ReadAllBytes(Path.Combine(state.Repository, ".git", "config"))
        );
        CollectionAssert.AreEqual(globalConfig, File.ReadAllBytes(state.GlobalConfig));
        CollectionAssert.AreEqual(systemConfig, File.ReadAllBytes(state.SystemConfig));
        Assert.AreEqual(
            "owned-local",
            (
                await state
                    .GitAsync(
                        state.Repository,
                        token,
                        "config",
                        "--local",
                        "--get",
                        "liteFixture.marker"
                    )
                    .ConfigureAwait(false)
            ).Trim()
        );
        Assert.AreEqual(
            "Owned Global Author",
            (
                await state
                    .GitAsync(state.Repository, token, "config", "--global", "--get", "user.name")
                    .ConfigureAwait(false)
            ).Trim()
        );
        Assert.AreEqual(
            "owned delivered feature\n",
            await state
                .GitAsync(state.Repository, token, "show", state.DeliveryRef + ":feature.txt")
                .ConfigureAwait(false)
        );
        Assert.IsFalse(File.Exists(Path.Combine(state.Repository, "feature.txt")));
        Assert.AreEqual(
            submitted,
            await state
                .ResolveAsync($"refs/heads/lite-agensh/{state.Database.RunId}/worker1", token)
                .ConfigureAwait(false)
        );
        foreach ((string _, string path) in state.Worktrees())
        {
            CollectionAssert.AreEqual(
                instructions,
                File.ReadAllBytes(Path.Combine(path, "AGENTS.md"))
            );
            Assert.IsFalse(File.Exists(Path.Combine(path, "untracked.txt")));
        }

        GitScenarioState.AssertEnvironmentUnchanged();
        foreach ((string name, string? value) in hostEnvironment)
        {
            Assert.AreEqual(value, Environment.GetEnvironmentVariable(name), name);
        }
    }

    [TestMethod]
    [TestCategory("SerialExactCandidateIntegration")]
    public async Task IndependentBranchesIntegrateTheirExactMergedTreesSerially()
    {
        CancellationToken token = testContext.CancellationToken;
        using GitScenarioState state = await GitScenarioState
            .CreateAsync(token)
            .ConfigureAwait(false);
        await state.Operations.InitializeAsync(token).ConfigureAwait(false);
        string firstHead = await state
            .CommitAsync(
                state.Operations.GetWorkerPath("worker1"),
                "alpha.txt",
                "alpha implemented independently\n",
                token
            )
            .ConfigureAwait(false);
        string secondHead = await state
            .CommitAsync(
                state.Operations.GetWorkerPath("worker2"),
                "beta.txt",
                "beta implemented independently\n",
                token
            )
            .ConfigureAwait(false);
        Assert.AreEqual(
            state.BaseCommit,
            await state.ResolveAsync(firstHead + "^", token).ConfigureAwait(false)
        );
        Assert.AreEqual(
            state.BaseCommit,
            await state.ResolveAsync(secondHead + "^", token).ConfigureAwait(false)
        );
        JsonObject first = await state
            .SubmitAsync("worker1", "Alpha feature", token)
            .ConfigureAwait(false);
        JsonObject second = await state
            .SubmitAsync("worker2", "Beta feature", token)
            .ConfigureAwait(false);
        JsonObject firstReview = await state
            .ReviewAsync("worker2", first, token)
            .ConfigureAwait(false);
        JsonObject secondReview = await state
            .ReviewAsync("worker1", second, token)
            .ConfigureAwait(false);
        JsonObject firstQueue = await state.QueueAsync(first, token).ConfigureAwait(false);
        JsonObject secondQueue = await state.QueueAsync(second, token).ConfigureAwait(false);

        await state.Operations.ProcessPendingAsync(token).ConfigureAwait(false);

        JsonObject firstRead = await state.ReadAsync(first, token).ConfigureAwait(false);
        JsonObject secondRead = await state.ReadAsync(second, token).ConfigureAwait(false);
        string firstCandidate = await AssertExactIntegrationAsync(
                state,
                firstRead,
                firstQueue,
                firstReview,
                state.BaseCommit,
                token
            )
            .ConfigureAwait(false);
        string secondCandidate = await AssertExactIntegrationAsync(
                state,
                secondRead,
                secondQueue,
                secondReview,
                firstCandidate,
                token
            )
            .ConfigureAwait(false);
        Assert.AreEqual(
            secondCandidate,
            await state.ResolveAsync(state.IntegrationRef, token).ConfigureAwait(false)
        );
        Assert.AreEqual(
            secondCandidate,
            (
                await state
                    .GitAsync(state.IntegrationPath, token, "rev-parse", "HEAD")
                    .ConfigureAwait(false)
            ).Trim()
        );
        Assert.AreEqual(
            "alpha implemented independently\n",
            await state
                .GitAsync(state.Repository, token, "show", secondCandidate + ":alpha.txt")
                .ConfigureAwait(false)
        );
        Assert.AreEqual(
            "beta implemented independently\n",
            await state
                .GitAsync(state.Repository, token, "show", secondCandidate + ":beta.txt")
                .ConfigureAwait(false)
        );
        Assert.AreEqual(
            "alpha implemented independently\n",
            await state
                .GitAsync(state.Repository, token, "show", firstCandidate + ":alpha.txt")
                .ConfigureAwait(false)
        );
        Assert.AreEqual(
            "AGENTS.md\nalpha.txt\nshared.txt\nstable.txt\ntracked.txt\n",
            await state
                .GitAsync(state.Repository, token, "ls-tree", "--name-only", "-r", firstCandidate)
                .ConfigureAwait(false)
        );
        Assert.AreEqual(
            "AGENTS.md\nalpha.txt\nbeta.txt\nshared.txt\nstable.txt\ntracked.txt\n",
            await state
                .GitAsync(state.Repository, token, "ls-tree", "--name-only", "-r", secondCandidate)
                .ConfigureAwait(false)
        );
        Assert.AreEqual(
            "base shared\n",
            await state
                .GitAsync(state.Repository, token, "show", secondCandidate + ":shared.txt")
                .ConfigureAwait(false)
        );
        Assert.AreEqual(
            "base tracked\n",
            await state
                .GitAsync(state.Repository, token, "show", secondCandidate + ":tracked.txt")
                .ConfigureAwait(false)
        );
        Assert.AreNotEqual(
            await state.ResolveAsync(secondHead + "^{tree}", token).ConfigureAwait(false),
            await state.ResolveAsync(secondCandidate + "^{tree}", token).ConfigureAwait(false)
        );
        Assert.AreEqual(
            state.BaseCommit,
            await state.ResolveAsync("HEAD", token).ConfigureAwait(false)
        );
    }

    [TestMethod]
    [TestCategory("SerialExactCandidateIntegration")]
    public async Task ConflictingSubmissionsRetainBothBranchesAndIntegrationTree()
    {
        CancellationToken token = testContext.CancellationToken;
        using GitScenarioState state = await GitScenarioState
            .CreateAsync(token)
            .ConfigureAwait(false);
        await state.Operations.InitializeAsync(token).ConfigureAwait(false);
        string firstHead = await state
            .CommitAsync(
                state.Operations.GetWorkerPath("worker1"),
                "shared.txt",
                "first incompatible implementation\n",
                token
            )
            .ConfigureAwait(false);
        string secondHead = await state
            .CommitAsync(
                state.Operations.GetWorkerPath("worker2"),
                "shared.txt",
                "second incompatible implementation\n",
                token
            )
            .ConfigureAwait(false);
        JsonObject first = await state
            .SubmitAsync("worker1", "First shared edit", token)
            .ConfigureAwait(false);
        JsonObject second = await state
            .SubmitAsync("worker2", "Second shared edit", token)
            .ConfigureAwait(false);
        await state.ReviewAsync("worker2", first, token).ConfigureAwait(false);
        await state.ReviewAsync("worker1", second, token).ConfigureAwait(false);
        await state.QueueAsync(first, token).ConfigureAwait(false);
        await state.Operations.ProcessPendingAsync(token).ConfigureAwait(false);
        string before = await state.ResolveAsync(state.IntegrationRef, token).ConfigureAwait(false);
        string beforeTree = await state
            .ResolveAsync(before + "^{tree}", token)
            .ConfigureAwait(false);
        JsonObject queued = await state.QueueAsync(second, token).ConfigureAwait(false);

        await state.Operations.ProcessPendingAsync(token).ConfigureAwait(false);

        JsonObject failed = await state.ReadAsync(second, token).ConfigureAwait(false);
        JsonObject request = Assert
            .ContainsSingle(failed["integrationRequests"]!.AsArray())!
            .AsObject();
        AssertRefusal(failed, request, "conflict", "Candidate merge failed");
        Assert.AreEqual(
            before,
            await state.ResolveAsync(state.IntegrationRef, token).ConfigureAwait(false)
        );
        Assert.AreEqual(
            beforeTree,
            await state.ResolveAsync(state.IntegrationRef + "^{tree}", token).ConfigureAwait(false)
        );
        Assert.AreEqual(
            "first incompatible implementation\n",
            File.ReadAllText(Path.Combine(state.IntegrationPath, "shared.txt"))
        );
        Assert.AreEqual(
            firstHead,
            await state
                .ResolveAsync(first["branch"]!.GetValue<string>(), token)
                .ConfigureAwait(false)
        );
        Assert.AreEqual(
            secondHead,
            await state
                .ResolveAsync(second["branch"]!.GetValue<string>(), token)
                .ConfigureAwait(false)
        );
        Assert.IsEmpty(failed["checks"]!.AsArray());
        string retained = Path.Combine(
            state.Database.StatePath,
            "worktrees",
            "candidate-"
                + queued["requestId"]!.GetValue<long>().ToString(CultureInfo.InvariantCulture)
        );
        string unmerged = await state
            .GitAsync(retained, token, "ls-files", "--unmerged")
            .ConfigureAwait(false);
        Assert.Contains("shared.txt", unmerged);
        Assert.Contains(
            "second incompatible implementation",
            File.ReadAllText(Path.Combine(retained, "shared.txt"))
        );
    }

    [TestMethod]
    [DataRow("head", "stale_candidate", "submitted worker head changed")]
    [DataRow("review", "review_required", "different worker must approve")]
    [TestCategory("SerialExactCandidateIntegration")]
    public async Task ChangedSubmissionCannotReuseAnOldHeadOrReview(
        string mutation,
        string code,
        string reason
    )
    {
        CancellationToken token = testContext.CancellationToken;
        using GitScenarioState state = await GitScenarioState
            .CreateAsync(token)
            .ConfigureAwait(false);
        await state.Operations.InitializeAsync(token).ConfigureAwait(false);
        string worker = state.Operations.GetWorkerPath("worker1");
        string oldHead = await state
            .CommitAsync(worker, "alpha.txt", "reviewed alpha\n", token)
            .ConfigureAwait(false);
        JsonObject change = await state
            .SubmitAsync("worker1", "Changing alpha", token)
            .ConfigureAwait(false);
        JsonObject review = await state.ReviewAsync("worker2", change, token).ConfigureAwait(false);
        if (mutation == "head")
        {
            await state.QueueAsync(change, token).ConfigureAwait(false);
        }

        string newHead = await state
            .CommitAsync(worker, "beta.txt", "not yet reviewed beta\n", token)
            .ConfigureAwait(false);
        if (mutation == "review")
        {
            change = await state
                .InvokeAsync(
                    "worker1",
                    "change_write",
                    new()
                    {
                        ["action"] = "update",
                        ["id"] = change["id"]!.GetValue<long>(),
                        ["head"] = newHead,
                    },
                    token
                )
                .ConfigureAwait(false);
            JsonObject staleReview = await state
                .Operations.InvokeAsync(
                    "worker2",
                    "change_review",
                    new()
                    {
                        ["id"] = change["id"]!.GetValue<long>(),
                        ["head"] = oldHead,
                        ["decision"] = "approve",
                        ["body"] = "Attempt old approval",
                    },
                    token
                )
                .ConfigureAwait(false);
            Assert.IsFalse(staleReview["ok"]!.GetValue<bool>());
            Assert.AreEqual("stale_candidate", staleReview["code"]!.GetValue<string>());
            await state.QueueAsync(change, token).ConfigureAwait(false);
        }

        await state.Operations.ProcessPendingAsync(token).ConfigureAwait(false);

        JsonObject read = await state.ReadAsync(change, token).ConfigureAwait(false);
        JsonObject request = Assert
            .ContainsSingle(read["integrationRequests"]!.AsArray())!
            .AsObject();
        AssertRefusal(read, request, code, reason);
        Assert.AreEqual(
            state.BaseCommit,
            await state.ResolveAsync(state.IntegrationRef, token).ConfigureAwait(false)
        );
        Assert.AreEqual(
            state.BaseCommit,
            (
                await state
                    .GitAsync(state.IntegrationPath, token, "rev-parse", "HEAD")
                    .ConfigureAwait(false)
            ).Trim()
        );
        Assert.AreEqual(
            newHead,
            await state
                .ResolveAsync(change["branch"]!.GetValue<string>(), token)
                .ConfigureAwait(false)
        );
        Assert.AreEqual(
            "not yet reviewed beta\n",
            File.ReadAllText(Path.Combine(worker, "beta.txt"))
        );
        JsonObject retainedReview = Assert.ContainsSingle(read["reviews"]!.AsArray())!.AsObject();
        Assert.AreEqual(review["id"]!.GetValue<long>(), retainedReview["id"]!.GetValue<long>());
        Assert.AreEqual(oldHead, retainedReview["head"]!.GetValue<string>());
        Assert.AreEqual("approve", retainedReview["decision"]!.GetValue<string>());
        Assert.IsEmpty(read["checks"]!.AsArray());
        if (mutation == "review")
        {
            Assert.AreEqual(
                1L,
                state.Number(
                    "SELECT stale FROM reviews WHERE id=$id;",
                    ("$id", review["id"]!.GetValue<long>())
                )
            );
        }
    }

    [TestMethod]
    [DataRow("tracked-tree", "check_failed")]
    [DataRow("index", "check_failed")]
    [DataRow("candidate-head", "check_failed")]
    [DataRow("integration-head", "stale_candidate")]
    [TestCategory("SerialExactCandidateIntegration")]
    [Timeout(120_000, CooperativeCancellation = true)]
    public async Task HeldCheckCannotAuthorizeChangedCandidate(string mutation, string code)
    {
        // Enforce the existing case budget at entry, including all prelaunch preparation.
        using CancellationTokenSource deadline = new(120_000);
        long began = Stopwatch.GetTimestamp();
        using CancellationTokenSource cancellation =
            CancellationTokenSource.CreateLinkedTokenSource(
                testContext.CancellationToken,
                deadline.Token
            );
        CancellationToken token = cancellation.Token;
        OwnedChildControl control = new();
        GitScenarioState? ownedState = null;
        Task? processing = null;
        long? ownedRequest = null, ownedChange = null;
        Exception? primary = null;
        AggregateException? cleanupFailure = null;
        try
        {
            CheckCommand held = new()
            {
                Executable = OwnedChildControl.ExecutablePath,
                Arguments = control.Arguments(role: OwnedChildControl.HeldCandidateCheckRole),
            };
            GitScenarioState state = await GitScenarioState
                .CreateAsync(token, [held])
                .ConfigureAwait(false);
            ownedState = state;
            await state.Operations.InitializeAsync(token).ConfigureAwait(false);
            string submitted = await state
                .CommitAsync(
                    state.Operations.GetWorkerPath("worker1"),
                    "alpha.txt",
                    "exact reviewed alpha\n",
                    token
                )
                .ConfigureAwait(false);
            JsonObject change = await state
                .SubmitAsync("worker1", "Held alpha", token)
                .ConfigureAwait(false);
            ownedChange = change["id"]!.GetValue<long>();
            await state.ReviewAsync("worker2", change, token).ConfigureAwait(false);
            JsonObject queued = await state.QueueAsync(change, token).ConfigureAwait(false);
            long requestId = queued["requestId"]!.GetValue<long>();
            ownedRequest = requestId;
            string path = Path.Combine(
                state.Database.StatePath,
                "worktrees",
                "candidate-" + requestId.ToString(CultureInfo.InvariantCulture)
            );
            processing = state.Operations.ProcessPendingAsync(cancellation.Token);
            // Actual helper readiness is the ordering witness, never elapsed time or stdout
            // polling.
            OwnedChildIdentity identity = await control
                .ReadReadyAsync(processing, token)
                .ConfigureAwait(false);
            string candidate = state.Text(
                "SELECT candidate_commit FROM integration_requests WHERE id=$id;",
                ("$id", requestId)
            )!;
            Assert.IsNotNull(candidate);
            string candidateTree = await state
                .ResolveAsync(candidate + "^{tree}", token)
                .ConfigureAwait(false);
            string preservedIntegration = state.BaseCommit;
            if (mutation is "tracked-tree" or "index")
            {
                File.WriteAllText(
                    Path.Combine(path, "shared.txt"),
                    "changed during actual check\n"
                );
                if (mutation == "index")
                {
                    await state
                        .GitAsync(path, token, "add", "--", "shared.txt")
                        .ConfigureAwait(false);
                }
            }
            else if (mutation == "candidate-head")
            {
                await state
                    .CommitAsync(path, "late.txt", "unchecked candidate commit\n", token)
                    .ConfigureAwait(false);
            }
            else
            {
                preservedIntegration = await state
                    .CommitAsync(
                        state.IntegrationPath,
                        "external.txt",
                        "integration moved during check\n",
                        token
                    )
                    .ConfigureAwait(false);
            }

            string retainedHead = (
                await state.GitAsync(path, token, "rev-parse", "HEAD").ConfigureAwait(false)
            ).Trim();
            string retainedIndex = await state
                .GitAsync(path, token, "ls-files", "--stage")
                .ConfigureAwait(false);
            string retainedShared = File.ReadAllText(Path.Combine(path, "shared.txt"));
            string preservedIntegrationTree = await state
                .ResolveAsync(preservedIntegration + "^{tree}", token)
                .ConfigureAwait(false);
            await control.ReleaseAsync(token).ConfigureAwait(false);
            await processing.ConfigureAwait(false);
            JsonObject read = await state.ReadAsync(change, token).ConfigureAwait(false);
            JsonObject request = Assert
                .ContainsSingle(read["integrationRequests"]!.AsArray())!
                .AsObject();
            AssertRefusal(
                read,
                request,
                code,
                mutation == "integration-head"
                    ? "Integration or candidate moved"
                    : "Candidate checks failed"
            );
            Assert.AreEqual(
                preservedIntegration,
                await state.ResolveAsync(state.IntegrationRef, token).ConfigureAwait(false)
            );
            Assert.AreEqual(
                preservedIntegration,
                (
                    await state
                        .GitAsync(state.IntegrationPath, token, "rev-parse", "HEAD")
                        .ConfigureAwait(false)
                ).Trim()
            );
            Assert.AreEqual(
                preservedIntegrationTree,
                await state
                    .ResolveAsync(state.IntegrationRef + "^{tree}", token)
                    .ConfigureAwait(false)
            );
            Assert.AreEqual(
                retainedHead,
                (
                    await state.GitAsync(path, token, "rev-parse", "HEAD").ConfigureAwait(false)
                ).Trim()
            );
            Assert.AreEqual(
                retainedIndex,
                await state.GitAsync(path, token, "ls-files", "--stage").ConfigureAwait(false)
            );
            Assert.AreEqual(retainedShared, File.ReadAllText(Path.Combine(path, "shared.txt")));
            Assert.AreEqual(
                submitted,
                await state
                    .ResolveAsync(change["branch"]!.GetValue<string>(), token)
                    .ConfigureAwait(false)
            );
            JsonObject check = Assert.ContainsSingle(read["checks"]!.AsArray())!.AsObject();
            Assert.AreEqual(candidate, check["commit"]!.GetValue<string>());
            Assert.AreEqual(candidateTree, check["tree"]!.GetValue<string>());
            Assert.AreEqual(0, check["exitCode"]!.GetValue<int>());
            Assert.AreEqual("candidate", check["kind"]!.GetValue<string>());
            Assert.AreEqual(
                mutation == "integration-head" ? "passed" : "failed",
                check["status"]!.GetValue<string>()
            );
            string log = File.ReadAllText(check["logPath"]!.GetValue<string>());
            Assert.Contains("owned check exit 0", log);
            if (mutation != "integration-head")
            {
                Assert.Contains("Candidate identity failure:", log);
                Assert.Contains("old check evidence cannot advance integration", log);
            }

            Assert.AreEqual(
                0L,
                state.Number(
                    "SELECT ready FROM integration_requests WHERE id=$id;",
                    ("$id", requestId)
                )
            );
            Assert.AreEqual(
                "exited",
                state.Text(
                    "SELECT state FROM process_leases WHERE pid=$pid AND start_ticks=$birth;",
                    ("$pid", identity.Pid),
                    ("$birth", identity.BirthTicks)
                )
            );
        }
        catch (Exception error)
        {
            primary = error;
            try
            {
                CaptureHeldCheckFailure(
                    ownedState, ownedRequest, ownedChange, processing, began, error, token
                );
            }
            catch (Exception captureError)
            {
                error.Data["HeldCheckCaptureError"] = captureError.ToString();
            }
            throw;
        }
        finally
        {
            List<Exception> failures = [];
            try
            {
                await cancellation.CancelAsync().ConfigureAwait(false);
            }
            catch (Exception error)
            {
                failures.Add(error);
            }
            // Cancel AND join the real producer before disposing its pipe or SQLite/Git state.
            if (processing is not null)
            {
                try
                {
                    await processing.ConfigureAwait(false);
                }
                catch (OperationCanceledException) when (cancellation.IsCancellationRequested)
                {
                }
                catch (Exception error)
                {
                    if (!ReferenceEquals(error, primary))
                    {
                        failures.Add(error);
                    }
                }
            }
            try
            {
                await control.DisposeAsync().ConfigureAwait(false);
            }
            catch (Exception error)
            {
                failures.Add(error);
            }
            try
            {
                ownedState?.Dispose();
            }
            catch (Exception error)
            {
                failures.Add(error);
            }
            if (failures.Count != 0)
            {
                AggregateException cleanup = new("Held-check owned cleanup failed.", failures);
                if (primary is null)
                {
                    cleanupFailure = cleanup;
                }
                else
                {
                    primary.Data["HeldCheckCleanupError"] = cleanup.ToString();
                    try
                    {
                        Console.Error.WriteLine("HELD_CHECK_SECONDARY " + cleanup);
                    }
                    catch (Exception loggingError)
                    {
                        primary.Data["HeldCheckCleanupLoggingError"] = loggingError.ToString();
                    }
                }
            }
        }
        if (cleanupFailure is not null)
        {
            throw cleanupFailure;
        }
    }

    private static void CaptureHeldCheckFailure(
        GitScenarioState? state,
        long? request,
        long? change,
        Task? producer,
        long began,
        Exception primary,
        CancellationToken token
    )
    {
        JsonObject receipt = new()
        {
            ["requestId"] = request,
            ["changeId"] = change,
            ["producerStatus"] = producer?.Status.ToString(),
            ["caseCancelledBeforeCleanup"] = token.IsCancellationRequested,
            ["ownedStatePath"] = state?.Database.StatePath,
        };
        // One owned-db snapshot only. Reserve both existing five-second SQLite busy stages;
        // never start a fresh read when the case budget is exhausted or nearly exhausted.
        TimeSpan remaining = TimeSpan.FromMilliseconds(120_000) - Stopwatch.GetElapsedTime(began);
        if (state is not null && request is not null && change is not null
            && !token.IsCancellationRequested && remaining >= TimeSpan.FromSeconds(5 + 5))
        {
            receipt["persistedOwnedState"] = JsonNode.Parse(state.Text(
                "SELECT json_object("
                    + "'request',json((SELECT json_object('id',id,'state',state,"
                    + "'resultCode',result_code,'ready',ready,'candidate',candidate_commit) "
                    + "FROM integration_requests WHERE id=$request)),"
                    + "'checks',json((SELECT json_group_array(json_object('id',id,"
                    + "'status',status,'exitCode',exit_code,'logPath',log_path)) "
                    + "FROM checks WHERE change_id=$change)),"
                    + "'leases',json((SELECT json_group_array(json_object('id',id,'pid',pid,"
                    + "'birthTicks',start_ticks,'kind',kind,'state',state)) "
                    + "FROM process_leases)));",
                ("$request", request), ("$change", change)
            ) ?? "null");
        }
        else
        {
            receipt["persistedOwnedState"] =
                "not-read: no owned row or insufficient case budget";
        }
        primary.Data["HeldCheckBeforeProducerCleanup"] = receipt.ToJsonString();
        Console.Error.WriteLine("HELD_CHECK_BEFORE_CLEANUP " + receipt.ToJsonString());
    }

    [TestMethod]
    [TestCategory("SerialExactCandidateIntegration")]
    public async Task FailingCandidateCheckRetainsActualFailureWithoutAdvancingIntegration()
    {
        CancellationToken token = testContext.CancellationToken;
        using GitScenarioState state = await GitScenarioState
            .CreateAsync(token, [GitScenarioState.ExitCheck(23)])
            .ConfigureAwait(false);
        await state.Operations.InitializeAsync(token).ConfigureAwait(false);
        string submitted = await state
            .CommitAsync(
                state.Operations.GetWorkerPath("worker1"),
                "alpha.txt",
                "submitted but failing validation\n",
                token
            )
            .ConfigureAwait(false);
        JsonObject change = await state
            .SubmitAsync("worker1", "Failing alpha", token)
            .ConfigureAwait(false);
        await state.ReviewAsync("worker2", change, token).ConfigureAwait(false);
        await state.QueueAsync(change, token).ConfigureAwait(false);

        await state.Operations.ProcessPendingAsync(token).ConfigureAwait(false);

        JsonObject read = await state.ReadAsync(change, token).ConfigureAwait(false);
        JsonObject request = Assert
            .ContainsSingle(read["integrationRequests"]!.AsArray())!
            .AsObject();
        AssertRefusal(read, request, "check_failed", "Candidate checks failed");
        JsonObject check = Assert.ContainsSingle(read["checks"]!.AsArray())!.AsObject();
        Assert.AreEqual("failed", check["status"]!.GetValue<string>());
        Assert.AreEqual(23, check["exitCode"]!.GetValue<int>());
        Assert.AreEqual("candidate", check["kind"]!.GetValue<string>());
        string candidate = check["commit"]!.GetValue<string>();
        Assert.AreEqual(
            state.BaseCommit,
            await state.ResolveAsync(candidate + "^1", token).ConfigureAwait(false)
        );
        Assert.AreEqual(
            submitted,
            await state.ResolveAsync(candidate + "^2", token).ConfigureAwait(false)
        );
        Assert.AreEqual(
            await state.ResolveAsync(candidate + "^{tree}", token).ConfigureAwait(false),
            check["tree"]!.GetValue<string>()
        );
        string log = File.ReadAllText(check["logPath"]!.GetValue<string>());
        Assert.Contains("owned check exit 23", log);
        Assert.Contains("owned check failed 23", log);
        Assert.AreEqual(
            state.BaseCommit,
            await state.ResolveAsync(state.IntegrationRef, token).ConfigureAwait(false)
        );
        Assert.AreEqual(
            state.BaseCommit,
            (
                await state
                    .GitAsync(state.IntegrationPath, token, "rev-parse", "HEAD")
                    .ConfigureAwait(false)
            ).Trim()
        );
        Assert.AreEqual(
            submitted,
            await state
                .ResolveAsync(read["branch"]!.GetValue<string>(), token)
                .ConfigureAwait(false)
        );
    }

    [TestMethod]
    [TestCategory("SerialExactCandidateIntegration")]
    [Timeout(120_000, CooperativeCancellation = true)]
    public async Task CancelledActualCandidateCheckRetainsEvidenceAndSettlesItsOwnedLease()
    {
        using CancellationTokenSource deadline = new(120_000);
        using CancellationTokenSource lifetime =
            CancellationTokenSource.CreateLinkedTokenSource(
                testContext.CancellationToken,
                deadline.Token
            );
        CancellationToken token = lifetime.Token;
        await using OwnedChildControl control = new();
        CheckCommand held = new()
        {
            Executable = OwnedChildControl.ExecutablePath,
            Arguments = control.Arguments(),
        };
        using GitScenarioState state = await GitScenarioState
            .CreateAsync(token, [held])
            .ConfigureAwait(false);
        using CancellationTokenSource cancellation =
            CancellationTokenSource.CreateLinkedTokenSource(token);
        await state.Operations.InitializeAsync(token).ConfigureAwait(false);
        string submitted = await state
            .CommitAsync(
                state.Operations.GetWorkerPath("worker1"),
                "alpha.txt",
                "retained on check cancellation\n",
                token
            )
            .ConfigureAwait(false);
        JsonObject change = await state
            .SubmitAsync("worker1", "Cancelled alpha", token)
            .ConfigureAwait(false);
        await state.ReviewAsync("worker2", change, token).ConfigureAwait(false);
        await state.QueueAsync(change, token).ConfigureAwait(false);
        Task processing = state.Operations.ProcessPendingAsync(cancellation.Token);
        try
        {
            OwnedChildIdentity identity = await control
                .ReadReadyAsync(processing, token)
                .ConfigureAwait(false);
            await cancellation.CancelAsync().ConfigureAwait(false);
            await Assert
                .ThrowsAsync<OperationCanceledException>(async () =>
                    await processing.ConfigureAwait(false)
                )
                .ConfigureAwait(false);
            JsonObject read = await state.ReadAsync(change, token).ConfigureAwait(false);
            JsonObject request = Assert
                .ContainsSingle(read["integrationRequests"]!.AsArray())!
                .AsObject();
            AssertRefusal(read, request, "unavailable", "Integration cancelled");
            JsonObject check = Assert.ContainsSingle(read["checks"]!.AsArray())!.AsObject();
            Assert.AreEqual("cancelled", check["status"]!.GetValue<string>());
            Assert.IsNull(check["exitCode"]);
            Assert.AreEqual("candidate", check["kind"]!.GetValue<string>());
            string candidate = check["commit"]!.GetValue<string>();
            Assert.AreEqual(
                submitted,
                await state.ResolveAsync(candidate + "^2", token).ConfigureAwait(false)
            );
            Assert.AreEqual(
                await state.ResolveAsync(candidate + "^{tree}", token).ConfigureAwait(false),
                check["tree"]!.GetValue<string>()
            );
            Assert.Contains(
                "Caller cancelled the check",
                File.ReadAllText(check["logPath"]!.GetValue<string>())
            );
            Assert.AreEqual(
                state.BaseCommit,
                await state.ResolveAsync(state.IntegrationRef, token).ConfigureAwait(false)
            );
            Assert.AreEqual(
                submitted,
                await state
                    .ResolveAsync(read["branch"]!.GetValue<string>(), token)
                    .ConfigureAwait(false)
            );
            Assert.AreEqual(
                "exited",
                state.Text(
                    "SELECT state FROM process_leases WHERE pid=$pid AND start_ticks=$birth;",
                    ("$pid", identity.Pid),
                    ("$birth", identity.BirthTicks)
                )
            );
        }
        finally
        {
            await cancellation.CancelAsync().ConfigureAwait(false);
            try
            {
                await processing.ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (cancellation.IsCancellationRequested) { }
        }
    }

    [TestMethod]
    [TestCategory("SerialExactCandidateIntegration")]
    [Timeout(120_000, CooperativeCancellation = true)]
    public async Task ConfiguredCheckDeadlineRetainsTimedOutEvidenceWithoutAuthorizingDelivery()
    {
        using CancellationTokenSource deadline = new(120_000);
        using CancellationTokenSource lifetime =
            CancellationTokenSource.CreateLinkedTokenSource(
                testContext.CancellationToken,
                deadline.Token
            );
        CancellationToken token = lifetime.Token;
        await using OwnedChildControl control = new();
        // A public minimum-valid command configuration, not a changed product/default
        // bound. The helper's post-connect five-second failure guard stays unchanged.
        // Actual readiness and the recorded timeout result, never elapsed time, qualify it.
        CheckCommand held = new()
        {
            Executable = OwnedChildControl.ExecutablePath,
            Arguments = control.Arguments(),
            TimeoutSeconds = 1,
        };
        using GitScenarioState state = await GitScenarioState
            .CreateAsync(token, [held])
            .ConfigureAwait(false);
        using CancellationTokenSource cancellation =
            CancellationTokenSource.CreateLinkedTokenSource(token);
        await state.Operations.InitializeAsync(token).ConfigureAwait(false);
        string submitted = await state
            .CommitAsync(
                state.Operations.GetWorkerPath("worker1"),
                "deadline.txt",
                "retained when validation times out\n",
                token
            )
            .ConfigureAwait(false);
        JsonObject change = await state
            .SubmitAsync("worker1", "Timeout is not authorization", token)
            .ConfigureAwait(false);
        await state.ReviewAsync("worker2", change, token).ConfigureAwait(false);
        await state.QueueAsync(change, token).ConfigureAwait(false);
        Task processing = state.Operations.ProcessPendingAsync(cancellation.Token);
        try
        {
            OwnedChildIdentity ready = await control
                .ReadReadyAsync(processing, token)
                .ConfigureAwait(false);
            // Deliberately do not release: exercise the application's configured check
            // deadline against a positively started child, not a shell/sleep executor.
            await processing.ConfigureAwait(false);
            JsonObject read = await state.ReadAsync(change, token).ConfigureAwait(false);
            JsonObject request = Assert
                .ContainsSingle(read["integrationRequests"]!.AsArray())!
                .AsObject();
            AssertRefusal(read, request, "check_failed", "Candidate checks failed");
            JsonObject check = Assert.ContainsSingle(read["checks"]!.AsArray())!.AsObject();
            Assert.AreEqual("timed-out", check["status"]!.GetValue<string>());
            Assert.AreEqual(1, check["command"]!["timeoutSeconds"]!.GetValue<int>());
            Assert.AreEqual("candidate", check["kind"]!.GetValue<string>());
            string candidate = check["commit"]!.GetValue<string>();
            Assert.AreEqual(
                state.BaseCommit,
                await state.ResolveAsync(candidate + "^1", token).ConfigureAwait(false)
            );
            Assert.AreEqual(
                submitted,
                await state.ResolveAsync(candidate + "^2", token).ConfigureAwait(false)
            );
            Assert.AreEqual(
                await state.ResolveAsync(candidate + "^{tree}", token).ConfigureAwait(false),
                check["tree"]!.GetValue<string>()
            );
            Assert.AreEqual(
                state.BaseCommit,
                await state.ResolveAsync(state.IntegrationRef, token).ConfigureAwait(false)
            );
            Assert.AreEqual(
                submitted,
                await state
                    .ResolveAsync(read["branch"]!.GetValue<string>(), token)
                    .ConfigureAwait(false)
            );
            string log = File.ReadAllText(check["logPath"]!.GetValue<string>());
            Assert.DoesNotContain("owned check exit 0", log);
            Assert.DoesNotContain("protocol failure bound", log);
            Assert.AreEqual(
                "exited",
                state.Text(
                    "SELECT state FROM process_leases WHERE pid=$pid AND start_ticks=$birth;",
                    ("$pid", ready.Pid),
                    ("$birth", ready.BirthTicks)
                )
            );
        }
        finally
        {
            await cancellation.CancelAsync().ConfigureAwait(false);
            try
            {
                await processing.ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (cancellation.IsCancellationRequested) { }
        }
    }

    private static void AssertRefusal(
        JsonObject change,
        JsonObject request,
        string code,
        string reason
    )
    {
        Assert.AreEqual("open", change["state"]!.GetValue<string>());
        Assert.AreEqual("failed", request["state"]!.GetValue<string>());
        Assert.AreEqual(code, request["resultCode"]!.GetValue<string>());
        Assert.Contains(reason, request["message"]!.GetValue<string>());
        Assert.IsNull(request["integratedCommit"]);
    }

    [TestMethod]
    [TestCategory("SerialExactCandidateIntegration")]
    public async Task UpdatingSubmittedChangePreservesOmittedLinksAndClearsExplicitEmptyLinks()
    {
        CancellationToken token = testContext.CancellationToken;
        using GitScenarioState state = await GitScenarioState
            .CreateAsync(token)
            .ConfigureAwait(false);
        await state.Operations.InitializeAsync(token).ConfigureAwait(false);
        JsonObject task = GitScenarioState.Data(
            state.Collaboration.Invoke(
                "owner",
                "task_write",
                new()
                {
                    ["action"] = "create",
                    ["title"] = "Link transition witness",
                    ["body"] = "Preserve then clear this actual task link.",
                }
            )
        );
        long taskId = task["id"]!.GetValue<long>();
        string head = await state
            .CommitAsync(
                state.Operations.GetWorkerPath("worker1"),
                "links.txt",
                "real linked candidate\n",
                token
            )
            .ConfigureAwait(false);
        JsonObject change = await state
            .SubmitAsync("worker1", "Linked candidate", token, taskId)
            .ConfigureAwait(false);
        long id = change["id"]!.GetValue<long>();
        string expectedLinks = new JsonArray(taskId).ToJsonString();
        Assert.AreEqual(
            expectedLinks,
            state.Text("SELECT task_ids FROM changes WHERE id=$id;", ("$id", id))
        );
        Assert.AreEqual(
            taskId,
            Assert
                .ContainsSingle(
                    (await state.ReadAsync(change, token).ConfigureAwait(false))[
                        "taskIds"
                    ]!.AsArray()
                )!
                .GetValue<long>()
        );

        await state
            .InvokeAsync(
                "worker1",
                "change_write",
                new()
                {
                    ["action"] = "update",
                    ["id"] = id,
                    ["body"] = "Omitted links retained",
                },
                token
            )
            .ConfigureAwait(false);

        JsonObject omitted = await state.ReadAsync(change, token).ConfigureAwait(false);
        Assert.AreEqual(id, omitted["id"]!.GetValue<long>());
        Assert.AreEqual(head, omitted["head"]!.GetValue<string>());
        Assert.AreEqual("Linked candidate", omitted["title"]!.GetValue<string>());
        Assert.AreEqual("Omitted links retained", omitted["body"]!.GetValue<string>());
        Assert.AreEqual(
            taskId,
            Assert.ContainsSingle(omitted["taskIds"]!.AsArray())!.GetValue<long>()
        );
        Assert.AreEqual(
            expectedLinks,
            state.Text("SELECT task_ids FROM changes WHERE id=$id;", ("$id", id))
        );

        await state
            .InvokeAsync(
                "worker1",
                "change_write",
                new()
                {
                    ["action"] = "update",
                    ["id"] = id,
                    ["taskIds"] = new JsonArray(),
                },
                token
            )
            .ConfigureAwait(false);

        JsonObject cleared = await state.ReadAsync(change, token).ConfigureAwait(false);
        Assert.AreEqual(id, cleared["id"]!.GetValue<long>());
        Assert.AreEqual(head, cleared["head"]!.GetValue<string>());
        Assert.AreEqual("Linked candidate", cleared["title"]!.GetValue<string>());
        Assert.AreEqual("Omitted links retained", cleared["body"]!.GetValue<string>());
        Assert.IsEmpty(cleared["taskIds"]!.AsArray());
        Assert.AreEqual(
            "[]",
            state.Text("SELECT task_ids FROM changes WHERE id=$id;", ("$id", id))
        );
        Assert.AreEqual(
            "open",
            state.Collaboration.Invoke("owner", "task_read", new() { ["id"] = taskId })["data"]![
                "state"
            ]!.GetValue<string>()
        );
    }

    private static async Task<string> AssertExactIntegrationAsync(
        GitScenarioState state,
        JsonObject change,
        JsonObject queued,
        JsonObject review,
        string parent,
        CancellationToken token
    )
    {
        Assert.AreEqual("integrated", change["state"]!.GetValue<string>());
        JsonObject request = Assert
            .ContainsSingle(change["integrationRequests"]!.AsArray())!
            .AsObject();
        Assert.AreEqual(
            queued["requestId"]!.GetValue<long>(),
            request["requestId"]!.GetValue<long>()
        );
        Assert.AreEqual("succeeded", request["state"]!.GetValue<string>());
        Assert.AreEqual("ok", request["resultCode"]!.GetValue<string>());
        Assert.AreEqual(change["head"]!.GetValue<string>(), request["head"]!.GetValue<string>());
        string candidate = request["integratedCommit"]!.GetValue<string>();
        string submitted = change["head"]!.GetValue<string>();
        Assert.AreEqual(
            parent,
            await state.ResolveAsync(candidate + "^1", token).ConfigureAwait(false)
        );
        Assert.AreEqual(
            submitted,
            await state.ResolveAsync(candidate + "^2", token).ConfigureAwait(false)
        );
        Assert.AreNotEqual(submitted, candidate);
        JsonObject check = Assert.ContainsSingle(change["checks"]!.AsArray())!.AsObject();
        Assert.AreEqual("candidate", check["kind"]!.GetValue<string>());
        Assert.AreEqual(candidate, check["commit"]!.GetValue<string>());
        Assert.AreEqual(
            await state.ResolveAsync(candidate + "^{tree}", token).ConfigureAwait(false),
            check["tree"]!.GetValue<string>()
        );
        Assert.AreEqual(change["id"]!.GetValue<long>(), check["changeId"]!.GetValue<long>());
        Assert.AreEqual("passed", check["status"]!.GetValue<string>());
        Assert.AreEqual(0, check["exitCode"]!.GetValue<int>());
        Assert.AreEqual(
            OwnedChildControl.ExecutablePath,
            check["command"]!["executable"]!.GetValue<string>()
        );
        JsonArray arguments = check["command"]!["arguments"]!.AsArray();
        Assert.HasCount(2, arguments);
        Assert.AreEqual("exit", arguments[0]!.GetValue<string>());
        Assert.AreEqual("0", arguments[1]!.GetValue<string>());
        Assert.AreEqual(
            state.Database.Configuration.CandidateChecks[0].TimeoutSeconds,
            check["command"]!["timeoutSeconds"]!.GetValue<int>()
        );
        Assert.Contains(
            "owned check exit 0",
            File.ReadAllText(check["logPath"]!.GetValue<string>())
        );
        JsonObject exactReview = Assert.ContainsSingle(change["reviews"]!.AsArray())!.AsObject();
        Assert.AreEqual(review["id"]!.GetValue<long>(), exactReview["id"]!.GetValue<long>());
        Assert.AreEqual(submitted, exactReview["head"]!.GetValue<string>());
        Assert.AreEqual(
            review["actor"]!.GetValue<string>(),
            exactReview["actor"]!.GetValue<string>()
        );
        Assert.AreEqual("approve", exactReview["decision"]!.GetValue<string>());
        return candidate;
    }
}
