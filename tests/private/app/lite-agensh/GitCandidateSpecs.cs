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

    private static readonly ConfiguredCheckInvocation PassConfiguredCheckAsync = (
        _,
        _,
        _,
        _,
        cancellationToken
    ) =>
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(new ProcessResult(0, "controlled check exit 0", "", false));
    };

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
            .CreateAsync(token, configuredCheck: PassConfiguredCheckAsync)
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
        foreach (string path in OwnedWorktreePaths(state))
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
            .CreateAsync(
                token,
                candidateChecks:
                [
                    new() { Executable = "git", Arguments = ["rev-parse", "HEAD", "HEAD^{tree}"] },
                ]
            )
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
        JsonObject selfReview = await state
            .ReviewAsync("worker1", first, token)
            .ConfigureAwait(false);
        JsonObject refusedQueue = await state.QueueAsync(first, token).ConfigureAwait(false);
        await state.Operations.ProcessPendingAsync(token).ConfigureAwait(false);
        JsonObject refused = await state.ReadAsync(first, token).ConfigureAwait(false);
        JsonObject refusedRequest = Assert.ContainsSingle(
            refused["integrationRequests"]!.AsArray().OfType<JsonObject>()
                .Where(item => item["requestId"]!.GetValue<long>()
                    == refusedQueue["requestId"]!.GetValue<long>())
        );
        Assert.AreEqual("failed", refusedRequest["state"]!.GetValue<string>());
        Assert.AreEqual("review_required", refusedRequest["resultCode"]!.GetValue<string>());
        Assert.Contains("A different worker", refusedRequest["message"]!.GetValue<string>());
        Assert.IsEmpty(refused["checks"]!.AsArray());
        Assert.AreEqual(
            state.BaseCommit,
            await state.ResolveAsync(state.IntegrationRef, token).ConfigureAwait(false)
        );
        JsonObject retainedSelfReview = Assert.ContainsSingle(
            refused["reviews"]!.AsArray().OfType<JsonObject>()
                .Where(item => item["id"]!.GetValue<long>() == selfReview["id"]!.GetValue<long>())
        );
        Assert.AreEqual("worker1", retainedSelfReview["actor"]!.GetValue<string>());
        Assert.AreEqual(firstHead, retainedSelfReview["head"]!.GetValue<string>());
        Assert.AreEqual("approve", retainedSelfReview["decision"]!.GetValue<string>());
        JsonObject firstReview = await state
            .ReviewAsync("worker2", first, token)
            .ConfigureAwait(false);
        JsonObject secondReview = await state
            .ReviewAsync("worker1", second, token)
            .ConfigureAwait(false);
        JsonObject firstQueue = await state.QueueAsync(first, token).ConfigureAwait(false);
        JsonObject secondQueue = await state.QueueAsync(second, token).ConfigureAwait(false);
        Assert.AreNotEqual(
            refusedQueue["requestId"]!.GetValue<long>(),
            firstQueue["requestId"]!.GetValue<long>()
        );

        await state.Operations.ProcessPendingAsync(token).ConfigureAwait(false);

        JsonObject firstRead = await state.ReadAsync(first, token).ConfigureAwait(false);
        JsonObject secondRead = await state.ReadAsync(second, token).ConfigureAwait(false);
        Assert.HasCount(2, firstRead["integrationRequests"]!.AsArray());
        Assert.HasCount(2, firstRead["reviews"]!.AsArray());
        Assert.AreEqual(
            "review_required",
            firstRead["integrationRequests"]!.AsArray().OfType<JsonObject>()
                .Single(item => item["requestId"]!.GetValue<long>()
                    == refusedQueue["requestId"]!.GetValue<long>())["resultCode"]!
                .GetValue<string>()
        );
        JsonObject finalSelfReview = firstRead["reviews"]!.AsArray().OfType<JsonObject>()
            .Single(item => item["id"]!.GetValue<long>()
                == selfReview["id"]!.GetValue<long>());
        Assert.AreEqual("worker1", finalSelfReview["actor"]!.GetValue<string>());
        Assert.AreEqual(firstHead, finalSelfReview["head"]!.GetValue<string>());
        Assert.AreEqual("approve", finalSelfReview["decision"]!.GetValue<string>());
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
    [DataRow("ROOT_POLICY.md", "**/ROOT_POLICY.md", true)]
    [DataRow("nested/dir/ROOT_POLICY.md", "**/ROOT_POLICY.md", true)]
    [DataRow("OTHER_ROOT_POLICY.md", "**/ROOT_POLICY.md", false)]
    [DataRow("docs/overview.md", "docs/**/*.md", true)]
    [DataRow("docs/a/b/overview.md", "docs/**/*.md", true)]
    [DataRow("config/sub/app.json", @"config\sub\*.json", true)]
    [DataRow("routes/[id]/handler.cs", "routes/[id]/*.cs", true)]
    [DataRow("routes/i/handler.cs", "routes/[id]/*.cs", false)]
    [DataRow("exact-dir-name", "exact-dir-name", true)]
    [DataRow("exact-dir-name/unprotected.txt", "exact-dir-name", false)]
    [DataRow(@"notes\policy.md", "*.md", true)]
    [DataRow("any.txt", "", false)]
    [DataRow("any.txt", "   ", false)]
    public void ProtectedPathMatcherCoversGlobAndLiteralBoundaries(
        string path,
        string pattern,
        bool expected
    ) => Assert.AreEqual(expected, GitOperations.MatchesPath(path, pattern));

    [TestMethod]
    [TestCategory("SerialExactCandidateIntegration")]
    [Timeout(60_000, CooperativeCancellation = true)]
    public async Task ProtectedPathApprovalEvaluatesMergedCandidateAfterIntegrationRename()
    {
        CancellationToken token = testContext.CancellationToken;
        using GitScenarioState state = await GitScenarioState.CreateAsync(
            token,
            protectedPaths: ["protected/**"],
            configuredCheck: PassConfiguredCheckAsync
        ).ConfigureAwait(false);
        await state.Operations.InitializeAsync(token).ConfigureAwait(false);

        // Worker2 edits the original unprotected filename before any rename is integrated.
        string worker2Path = state.Operations.GetWorkerPath("worker2");
        string originalShared = await File.ReadAllTextAsync(
            Path.Combine(worker2Path, "shared.txt"), token
        ).ConfigureAwait(false);
        string worker2Head = await state.CommitAsync(
            worker2Path, "shared.txt", originalShared + "worker2 pre-rename edit\n", token
        ).ConfigureAwait(false);

        // Worker1 renames shared.txt into protected/** and integrates with owner approval.
        string worker1Path = state.Operations.GetWorkerPath("worker1");
        Directory.CreateDirectory(Path.Combine(worker1Path, "protected"));
        File.Move(
            Path.Combine(worker1Path, "shared.txt"),
            Path.Combine(worker1Path, "protected", "shared.txt")
        );
        await state.GitAsync(worker1Path, token, "add", "-A").ConfigureAwait(false);
        await state.GitAsync(worker1Path, token, "commit", "-m", "Rename shared into protected")
            .ConfigureAwait(false);
        JsonObject renameChange = await state.SubmitAsync("worker1", "Move into protected", token)
            .ConfigureAwait(false);
        await state.ReviewAsync("worker2", renameChange, token).ConfigureAwait(false);
        await state.ReviewAsync("owner", renameChange, token).ConfigureAwait(false);
        await state.QueueAsync(renameChange, token).ConfigureAwait(false);
        await state.Operations.ProcessPendingAsync(token).ConfigureAwait(false);
        Assert.AreEqual(
            "integrated",
            (await state.ReadAsync(renameChange, token).ConfigureAwait(false))["state"]!
                .GetValue<string>()
        );

        // Worker2's submitted diff touches only shared.txt, but merging onto integration
        // modifies protected/shared.txt and must require owner approval before checks run.
        JsonObject editChange = await state.SubmitAsync("worker2", "Edit pre-rename file", token)
            .ConfigureAwait(false);
        Assert.AreEqual(worker2Head, editChange["head"]!.GetValue<string>());
        await state.ReviewAsync("worker1", editChange, token).ConfigureAwait(false);
        JsonObject refusedQueue = await state.QueueAsync(editChange, token).ConfigureAwait(false);
        string before = await state.ResolveAsync(state.IntegrationRef, token).ConfigureAwait(false);
        await state.Operations.ProcessPendingAsync(token).ConfigureAwait(false);

        JsonObject refused = await state.ReadAsync(editChange, token).ConfigureAwait(false);
        JsonObject rejected = Assert.ContainsSingle(
            refused["integrationRequests"]!.AsArray().OfType<JsonObject>()
        );
        Assert.AreEqual(
            refusedQueue["requestId"]!.GetValue<long>(), rejected["requestId"]!.GetValue<long>()
        );
        Assert.AreEqual("review_required", rejected["resultCode"]!.GetValue<string>());
        Assert.Contains(
            "Protected paths require owner approval", rejected["message"]!.GetValue<string>()
        );
        Assert.IsEmpty(refused["checks"]!.AsArray());
        Assert.AreEqual(
            before, await state.ResolveAsync(state.IntegrationRef, token).ConfigureAwait(false)
        );

        await state.ReviewAsync("owner", editChange, token).ConfigureAwait(false);
        await state.QueueAsync(editChange, token).ConfigureAwait(false);
        await state.Operations.ProcessPendingAsync(token).ConfigureAwait(false);
        JsonObject integrated = await state.ReadAsync(editChange, token).ConfigureAwait(false);
        Assert.AreEqual("integrated", integrated["state"]!.GetValue<string>());
        StringAssert.Contains(
            await state.GitAsync(
                state.Repository, token, "show", state.IntegrationRef + ":protected/shared.txt"
            ).ConfigureAwait(false),
            "worker2 pre-rename edit"
        );
    }

    [TestMethod]
    [TestCategory("SerialExactCandidateIntegration")]
    [DataRow("worker2", "Protected paths require owner approval of this exact head.")]
    [DataRow("owner", "A different worker must approve the exact submitted head.")]
    public async Task ProtectedChangeRequiresSeparatePeerAndOwnerApproval(
        string firstReviewer,
        string missingApproval
    )
    {
        CancellationToken token = testContext.CancellationToken;
        using GitScenarioState state = await GitScenarioState
            .CreateAsync(
                token,
                protectedPaths: ["protected.txt"],
                configuredCheck: PassConfiguredCheckAsync
            )
            .ConfigureAwait(false);
        await state.Operations.InitializeAsync(token).ConfigureAwait(false);
        string head = await state.CommitAsync(
            state.Operations.GetWorkerPath("worker1"),
            "protected.txt",
            "Owner-reviewed protected change.\n",
            token
        ).ConfigureAwait(false);
        JsonObject change = await state.SubmitAsync("worker1", "Protected change", token)
            .ConfigureAwait(false);
        JsonObject firstReview = await state.ReviewAsync(firstReviewer, change, token)
            .ConfigureAwait(false);
        JsonObject refusedQueue = await state.QueueAsync(change, token).ConfigureAwait(false);
        await state.Operations.ProcessPendingAsync(token).ConfigureAwait(false);
        JsonObject refused = await state.ReadAsync(change, token).ConfigureAwait(false);
        JsonObject refusal = Assert.ContainsSingle(
            refused["integrationRequests"]!.AsArray().OfType<JsonObject>()
                .Where(item => item["requestId"]!.GetValue<long>()
                    == refusedQueue["requestId"]!.GetValue<long>())
        );
        Assert.AreEqual("failed", refusal["state"]!.GetValue<string>());
        Assert.AreEqual("review_required", refusal["resultCode"]!.GetValue<string>());
        Assert.AreEqual(head, refusal["head"]!.GetValue<string>());
        Assert.Contains(missingApproval, refusal["message"]!.GetValue<string>());
        JsonObject recordedFirstReview = Assert.ContainsSingle(
            refused["reviews"]!.AsArray().OfType<JsonObject>()
                .Where(item => item["id"]!.GetValue<long>() == firstReview["id"]!.GetValue<long>())
        );
        Assert.AreEqual(firstReviewer, recordedFirstReview["actor"]!.GetValue<string>());
        Assert.AreEqual(head, recordedFirstReview["head"]!.GetValue<string>());
        Assert.AreEqual("approve", recordedFirstReview["decision"]!.GetValue<string>());
        Assert.IsEmpty(refused["checks"]!.AsArray());
        Assert.AreEqual(
            state.BaseCommit,
            await state.ResolveAsync(state.IntegrationRef, token).ConfigureAwait(false)
        );
        if (firstReviewer == "owner")
        {
            return;
        }

        JsonObject secondReview = await state.ReviewAsync("owner", change, token)
            .ConfigureAwait(false);
        JsonObject withdrawal = await state.InvokeAsync(
            "worker2",
            "change_review",
            new()
            {
                ["id"] = change["id"]!.GetValue<long>(),
                ["head"] = head,
                ["decision"] = "request-changes",
                ["body"] = "Withdraw approval of this unchanged submitted commit.",
            },
            token
        ).ConfigureAwait(false);
        JsonObject withdrawnQueue = await state.QueueAsync(change, token).ConfigureAwait(false);
        await state.Operations.ProcessPendingAsync(token).ConfigureAwait(false);
        JsonObject withdrawn = await state.ReadAsync(change, token).ConfigureAwait(false);
        JsonObject withdrawnRequest = Assert.ContainsSingle(
            withdrawn["integrationRequests"]!.AsArray().OfType<JsonObject>()
                .Where(item => item["requestId"]!.GetValue<long>()
                    == withdrawnQueue["requestId"]!.GetValue<long>())
        );
        AssertRefusal(
            withdrawn, withdrawnRequest, "review_required",
            "A different worker must approve the exact submitted head."
        );
        Assert.AreEqual(head, withdrawnRequest["head"]!.GetValue<string>());
        Assert.IsEmpty(withdrawn["checks"]!.AsArray());
        Assert.AreEqual(
            state.BaseCommit,
            await state.ResolveAsync(state.IntegrationRef, token).ConfigureAwait(false)
        );
        JsonObject renewedPeerReview = await state.ReviewAsync("worker2", change, token)
            .ConfigureAwait(false);
        JsonObject acceptedQueue = await state.QueueAsync(change, token).ConfigureAwait(false);
        Assert.AreNotEqual(
            refusedQueue["requestId"]!.GetValue<long>(),
            acceptedQueue["requestId"]!.GetValue<long>()
        );
        await state.Operations.ProcessPendingAsync(token).ConfigureAwait(false);
        JsonObject integrated = await state.ReadAsync(change, token).ConfigureAwait(false);
        Assert.AreEqual("integrated", integrated["state"]!.GetValue<string>());
        Assert.HasCount(3, integrated["integrationRequests"]!.AsArray());
        JsonObject accepted = Assert.ContainsSingle(
            integrated["integrationRequests"]!.AsArray().OfType<JsonObject>()
                .Where(item => item["requestId"]!.GetValue<long>()
                    == acceptedQueue["requestId"]!.GetValue<long>())
        );
        Assert.AreEqual("succeeded", accepted["state"]!.GetValue<string>());
        Assert.AreEqual("ok", accepted["resultCode"]!.GetValue<string>());
        Assert.AreEqual(head, accepted["head"]!.GetValue<string>());
        foreach (JsonObject refusedRequest in new[] { refusedQueue, withdrawnQueue })
        {
            JsonObject retainedRefusal = Assert.ContainsSingle(
                integrated["integrationRequests"]!.AsArray().OfType<JsonObject>()
                    .Where(item => item["requestId"]!.GetValue<long>()
                        == refusedRequest["requestId"]!.GetValue<long>())
            );
            Assert.AreEqual("failed", retainedRefusal["state"]!.GetValue<string>());
            Assert.AreEqual("review_required", retainedRefusal["resultCode"]!.GetValue<string>());
            Assert.AreEqual(head, retainedRefusal["head"]!.GetValue<string>());
        }
        string candidate = accepted["integratedCommit"]!.GetValue<string>();
        Assert.AreEqual(
            candidate,
            await state.ResolveAsync(state.IntegrationRef, token).ConfigureAwait(false)
        );
        Assert.AreEqual(
            state.BaseCommit,
            await state.ResolveAsync(candidate + "^1", token).ConfigureAwait(false)
        );
        Assert.AreEqual(
            head,
            await state.ResolveAsync(candidate + "^2", token).ConfigureAwait(false)
        );
        JsonObject check = Assert.ContainsSingle(integrated["checks"]!.AsArray())!.AsObject();
        Assert.AreEqual(candidate, check["commit"]!.GetValue<string>());
        Assert.AreEqual("passed", check["status"]!.GetValue<string>());
        Assert.AreEqual(0, check["exitCode"]!.GetValue<int>());
        string checkLog = File.ReadAllText(check["logPath"]!.GetValue<string>());
        Assert.Contains("Configured check execution: controlled.", checkLog);
        Assert.Contains("controlled check exit 0", checkLog);
        foreach ((JsonObject review, string actor, string decision) in new[]
        {
            (firstReview, firstReviewer, "approve"), (secondReview, "owner", "approve"),
            (withdrawal, "worker2", "request-changes"), (renewedPeerReview, "worker2", "approve"),
        })
        {
            JsonObject recorded = Assert.ContainsSingle(
                integrated["reviews"]!.AsArray().OfType<JsonObject>()
                    .Where(item => item["id"]!.GetValue<long>() == review["id"]!.GetValue<long>())
            );
            Assert.AreEqual(actor, recorded["actor"]!.GetValue<string>());
            Assert.AreEqual(head, recorded["head"]!.GetValue<string>());
            Assert.AreEqual(decision, recorded["decision"]!.GetValue<string>());
        }
        Assert.AreEqual(
            "Owner-reviewed protected change.\n",
            await state.GitAsync(state.Repository, token, "show", candidate + ":protected.txt")
                .ConfigureAwait(false)
        );
        Assert.AreEqual(
            state.BaseCommit,
            await state.ResolveAsync("HEAD", token).ConfigureAwait(false)
        );
    }

    [TestMethod]
    [DataRow(false, false)]
    [DataRow(true, false)]
    [DataRow(true, true)]
    public async Task EmptyRepositoryRequiresExplicitInitializationOptIn(
        bool createRepository,
        bool hasExistingFile
    )
    {
        CancellationToken token = testContext.CancellationToken;
        using ScratchDirectory scratch = new();
        string repository = scratch.PathFor("empty-checkout");
        Directory.CreateDirectory(repository);
        const string existingContent = "Preserve the caller's existing project bytes.\n";
        string existingFile = Path.Combine(repository, "existing.txt");
        if (hasExistingFile)
        {
            File.WriteAllText(existingFile, existingContent);
        }
        byte[] existingBytes = hasExistingFile ? File.ReadAllBytes(existingFile) : [];
        RunConfiguration denied = new()
        {
            RepositoryPath = repository,
            StatePath = scratch.PathFor("state"),
            CampaignLedgerPath = scratch.PathFor("campaign.sqlite"),
            Goal = "Initialize only an explicitly authorized empty repository.",
        };
        RunConfiguration configuration = createRepository
            ? denied with { CreateRepository = true }
            : denied;
        RunDatabase database = new(configuration.StatePath, configuration);
        CollaborationOperations collaboration = new(database);
        GitOperations operations = new(database, collaboration, PassConfiguredCheckAsync);
        ProcessRunner processes = new(database);
        try
        {
            if (!createRepository || hasExistingFile)
            {
                OperationException error = await Assert.ThrowsExactlyAsync<OperationException>(
                    () => operations.InitializeAsync(token)
                ).ConfigureAwait(false);
                Assert.AreEqual("unavailable", error.Code);
                Assert.Contains("explicitly authorized empty directory", error.Message);
                Assert.IsFalse(Directory.Exists(Path.Combine(repository, ".git")));
                Assert.IsFalse(
                    Directory.Exists(Path.Combine(configuration.StatePath, "worktrees"))
                );
                if (hasExistingFile)
                {
                    CollectionAssert.AreEqual(existingBytes, File.ReadAllBytes(existingFile));
                    Assert.AreEqual(
                        existingFile,
                        Assert.ContainsSingle(Directory.EnumerateFileSystemEntries(repository))
                    );
                }
                else
                {
                    Assert.IsEmpty(Directory.EnumerateFileSystemEntries(repository));
                }
                JsonObject uninitialized = await operations
                    .InvokeAsync(
                        "worker1",
                        "change_write",
                        new()
                        {
                            ["action"] = "submit",
                            ["title"] = "Uninitialized",
                            ["body"] = "Must reject before Git initialization.",
                        },
                        token
                    )
                    .ConfigureAwait(false);
                Assert.IsFalse(uninitialized["ok"]!.GetValue<bool>());
                Assert.AreEqual("unavailable", uninitialized["code"]!.GetValue<string>());
                return;
            }

            await operations.InitializeAsync(token).ConfigureAwait(false);
            async Task<string> ReadGitAsync(string cwd, params string[] arguments)
            {
                ProcessResult result = await processes.RunAsync(
                    "git", arguments, cwd, TimeSpan.FromSeconds(60), token
                ).ConfigureAwait(false);
                Assert.IsFalse(result.TimedOut);
                Assert.AreEqual(0, result.ExitCode, result.StandardError);
                return result.StandardOutput;
            }
            string head = (await ReadGitAsync(repository, "rev-parse", "--verify", "HEAD^{commit}")
                .ConfigureAwait(false)).Trim();
            JsonObject runInfo = GitScenarioState.Data(
                collaboration.Invoke("owner", "run_info", new())
            );
            Assert.AreEqual(head, runInfo["baseCommit"]!.GetValue<string>());
            Assert.AreEqual(head, database.Configuration.BaseRevision);
            Assert.IsEmpty(await ReadGitAsync(repository, "ls-tree", "-r", "--name-only", head)
                .ConfigureAwait(false));
            JsonObject agents = GitScenarioState.Data(
                collaboration.Invoke("owner", "agent_list", new())
            );
            (string Ref, string Path)[] expectedWorktrees =
            [
                (
                    $"refs/heads/lite-agensh/{database.RunId}/integration",
                    Path.Combine(configuration.StatePath, "worktrees", "integration")
                ),
                .. configuration.WorkerIds.Select(worker => (
                    $"refs/heads/lite-agensh/{database.RunId}/{worker}",
                    operations.GetWorkerPath(worker)
                )),
            ];
            foreach (string worker in configuration.WorkerIds)
            {
                JsonObject agent = agents["items"]!
                    .AsArray()
                    .OfType<JsonObject>()
                    .Single(item => item["id"]!.GetValue<string>() == worker);
                Assert.AreEqual(
                    operations.GetWorkerPath(worker),
                    agent["worktreePath"]!.GetValue<string>()
                );
            }
            foreach ((string reference, string path) in expectedWorktrees)
            {
                Assert.IsTrue(Directory.Exists(path), path);
                Assert.AreEqual(
                    head,
                    (await ReadGitAsync(repository, "rev-parse", "--verify", reference)
                        .ConfigureAwait(false)).Trim()
                );
                Assert.AreEqual(
                    head,
                    (await ReadGitAsync(path, "rev-parse", "HEAD").ConfigureAwait(false)).Trim()
                );
            }
        }
        finally
        {
            foreach (string file in Directory.EnumerateFiles(
                scratch.Root, "*", SearchOption.AllDirectories
            ))
            {
                File.SetAttributes(file, File.GetAttributes(file) & ~FileAttributes.ReadOnly);
            }
            GitScenarioState.AssertEnvironmentUnchanged();
        }
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task UnbornRepositoryInitializationPreservesStagedCallerWork(bool indexOnly)
    {
        CancellationToken token = testContext.CancellationToken;
        using ScratchDirectory scratch = new();
        string repository = scratch.PathFor("unborn-checkout");
        Directory.CreateDirectory(repository);
        RunConfiguration configuration = new()
        {
            RepositoryPath = repository,
            StatePath = scratch.PathFor("state"),
            CampaignLedgerPath = scratch.PathFor("campaign.sqlite"),
            Goal = "Do not import staged caller work during empty repository initialization.",
            WorkerCount = 1,
            CreateRepository = true,
        };
        RunDatabase database = new(configuration.StatePath, configuration);
        CollaborationOperations collaboration = new(database);
        GitOperations operations = new(database, collaboration, PassConfiguredCheckAsync);
        ProcessRunner processes = new(database);
        async Task<string> GitAsync(bool expectSuccess, params string[] arguments)
        {
            ProcessResult result = await processes.RunAsync(
                "git", arguments, repository, TimeSpan.FromSeconds(60), token
            ).ConfigureAwait(false);
            Assert.IsFalse(result.TimedOut);
            if (expectSuccess)
            {
                Assert.AreEqual(0, result.ExitCode, result.StandardError);
            }
            else
            {
                Assert.AreNotEqual(0, result.ExitCode, "The caller's HEAD must remain unborn.");
            }
            return result.StandardOutput;
        }
        try
        {
            await GitAsync(true, "init", "--quiet").ConfigureAwait(false);
            string existingFile = Path.Combine(repository, "existing.txt");
            File.WriteAllText(
                existingFile, "Staged caller work must not become an initial commit.\n"
            );
            byte[] existingBytes = File.ReadAllBytes(existingFile);
            await GitAsync(true, "add", "--", "existing.txt").ConfigureAwait(false);
            string staged = await GitAsync(true, "ls-files", "--stage", "-z")
                .ConfigureAwait(false);
            Assert.IsNotEmpty(staged);
            await GitAsync(false, "rev-parse", "--verify", "HEAD").ConfigureAwait(false);
            if (indexOnly)
            {
                File.Delete(existingFile);
                Assert.AreEqual(
                    ".git",
                    Assert.ContainsSingle(
                        Directory.EnumerateFileSystemEntries(repository)
                            .Select(Path.GetFileName)
                    )
                );
            }
            string indexPath = Path.Combine(repository, ".git", "index");
            byte[] indexBytes = File.ReadAllBytes(indexPath);

            OperationException error = await Assert.ThrowsExactlyAsync<OperationException>(
                () => operations.InitializeAsync(token)
            ).ConfigureAwait(false);

            Assert.AreEqual("unavailable", error.Code);
            Assert.Contains("Empty repository initialization cannot import existing files",
                error.Message);
            CollectionAssert.AreEqual(indexBytes, File.ReadAllBytes(indexPath));
            if (indexOnly)
            {
                Assert.IsFalse(File.Exists(existingFile));
            }
            else
            {
                CollectionAssert.AreEqual(existingBytes, File.ReadAllBytes(existingFile));
            }
            Assert.AreEqual(
                staged,
                await GitAsync(true, "ls-files", "--stage", "-z").ConfigureAwait(false)
            );
            await GitAsync(false, "rev-parse", "--verify", "HEAD").ConfigureAwait(false);
            Assert.IsFalse(Directory.Exists(Path.Combine(configuration.StatePath, "worktrees")));
            Assert.IsEmpty(
                await GitAsync(
                        true,
                        "for-each-ref",
                        "--format=%(refname)",
                        "refs/heads/lite-agensh/"
                    )
                    .ConfigureAwait(false)
            );
        }
        finally
        {
            foreach (string file in Directory.EnumerateFiles(
                scratch.Root, "*", SearchOption.AllDirectories
            ))
            {
                File.SetAttributes(file, File.GetAttributes(file) & ~FileAttributes.ReadOnly);
            }
            GitScenarioState.AssertEnvironmentUnchanged();
        }
    }

    [TestMethod]
    [TestCategory("SerialExactCandidateIntegration")]
    public async Task ConflictingSubmissionsRetainBothBranchesAndIntegrationTree()
    {
        CancellationToken token = testContext.CancellationToken;
        using GitScenarioState state = await GitScenarioState
            .CreateAsync(token, configuredCheck: PassConfiguredCheckAsync)
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
            .CreateAsync(token, configuredCheck: PassConfiguredCheckAsync)
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
            // Reverting to oldHead proves via change_read refusal that superseded approvals stay
            // historical and cannot re-authorize integration without a fresh review.
            string branch = change["branch"]!.GetValue<string>();
            await state.GitAsync(worker, token, "branch", "retained-unreviewed", newHead)
                .ConfigureAwait(false);
            await state.GitAsync(worker, token, "switch", "--detach", oldHead)
                .ConfigureAwait(false);
            await state.GitAsync(worker, token, "update-ref", branch, oldHead, newHead)
                .ConfigureAwait(false);
            await state.GitAsync(worker, token, "switch", "-")
                .ConfigureAwait(false);
            change = await state.InvokeAsync(
                "worker1",
                "change_write",
                new()
                {
                    ["action"] = "update",
                    ["id"] = change["id"]!.GetValue<long>(),
                    ["head"] = oldHead,
                },
                token
            ).ConfigureAwait(false);
            Assert.AreEqual(oldHead, change["head"]!.GetValue<string>());
            Assert.AreEqual(
                oldHead,
                await state.ResolveAsync(branch, token).ConfigureAwait(false)
            );
            JsonObject returned = await state.QueueAsync(change, token).ConfigureAwait(false);
            await state.Operations.ProcessPendingAsync(token).ConfigureAwait(false);
            read = await state.ReadAsync(change, token).ConfigureAwait(false);
            JsonObject returnedRequest = Assert.ContainsSingle(
                read["integrationRequests"]!.AsArray().Where(row =>
                    row!["requestId"]!.GetValue<long>() == returned["requestId"]!.GetValue<long>()
                )
            )!.AsObject();
            AssertRefusal(read, returnedRequest, "review_required", reason);
            JsonObject historicalReview = Assert
                .ContainsSingle(read["reviews"]!.AsArray())!
                .AsObject();
            Assert.AreEqual(
                review["id"]!.GetValue<long>(),
                historicalReview["id"]!.GetValue<long>()
            );
            Assert.IsEmpty(read["checks"]!.AsArray());
            Assert.AreEqual(
                state.BaseCommit,
                await state.ResolveAsync(state.IntegrationRef, token).ConfigureAwait(false)
            );
            Assert.AreEqual(
                state.BaseCommit,
                (await state.GitAsync(state.IntegrationPath, token, "rev-parse", "HEAD")
                    .ConfigureAwait(false)).Trim()
            );
        }
    }

    [TestMethod]
    [DataRow("tracked-tree", "check_failed")]
    [DataRow("index", "check_failed")]
    [DataRow("candidate-head", "check_failed")]
    [DataRow("integration-head", "stale_candidate")]
    [DataRow("integration-tree", "stale_candidate")]
    [DataRow("worker-head", "stale_candidate")]
    [TestCategory("SerialExactCandidateIntegration")]
    [Timeout(120_000, CooperativeCancellation = true)]
    public async Task HeldCheckCannotAuthorizeChangedCandidate(string mutation, string code)
    {
        await using HeldCheckScope held = new(testContext.CancellationToken);
        CancellationToken token = held.Token;
        GitScenarioState state = held.Own(
            await GitScenarioState
                .CreateAsync(
                    token,
                    [new() { Executable = "controlled-candidate-check" }],
                    configuredCheck: (_, _, _, _, checkToken) =>
                        held.WaitCheckAsync("controlled check exit 0", checkToken)
                )
                .ConfigureAwait(false)
        );
        await state.Operations.InitializeAsync(token).ConfigureAwait(false);
        string worker = state.Operations.GetWorkerPath("worker1");
        string submitted = await state
            .CommitAsync(worker, "alpha.txt", "exact reviewed alpha\n", token)
            .ConfigureAwait(false);
        JsonObject change = await state
            .SubmitAsync("worker1", "Held alpha", token)
            .ConfigureAwait(false);
        JsonObject review = await state
            .ReviewAsync("worker2", change, token)
            .ConfigureAwait(false);
        JsonObject queued = await state.QueueAsync(change, token).ConfigureAwait(false);
        long requestId = queued["requestId"]!.GetValue<long>();
        string path = Path.Combine(
            state.Database.StatePath,
            "worktrees",
            "candidate-" + requestId.ToString(CultureInfo.InvariantCulture)
        );
        _ = held.Start(() => state.Operations.ProcessPendingAsync(held.ProducerToken));
        await held.AwaitEntryAsync().ConfigureAwait(false);
        string candidate = await state
            .ResolveAsync(
                $"refs/heads/lite-agensh/{state.Database.RunId}/candidate-{requestId}",
                token
            )
            .ConfigureAwait(false);
        string candidateTree = await state
            .ResolveAsync(candidate + "^{tree}", token)
            .ConfigureAwait(false);
        string preservedIntegration = state.BaseCommit;
        string preservedWorker = submitted;
        if (mutation is "tracked-tree" or "index")
        {
            string originalShared = File.ReadAllText(Path.Combine(path, "shared.txt"));
            File.WriteAllText(
                Path.Combine(path, "shared.txt"),
                "changed during controlled check\n"
            );
            if (mutation == "index")
            {
                await state
                    .GitAsync(path, token, "add", "--", "shared.txt")
                    .ConfigureAwait(false);
                File.WriteAllText(Path.Combine(path, "shared.txt"), originalShared);
                Assert.AreEqual(
                    "",
                    await state.GitAsync(path, token, "diff", "--name-only", "HEAD", "--")
                        .ConfigureAwait(false)
                );
                Assert.AreEqual(
                    "shared.txt",
                    (await state.GitAsync(
                        path, token, "diff", "--cached", "--name-only", "HEAD", "--"
                    ).ConfigureAwait(false)).Trim()
                );
                Assert.AreEqual(
                    candidate,
                    (await state.GitAsync(path, token, "rev-parse", "HEAD")
                        .ConfigureAwait(false)).Trim()
                );
            }
        }
        else if (mutation == "candidate-head")
        {
            await state
                .CommitAsync(path, "late.txt", "unchecked candidate commit\n", token)
                .ConfigureAwait(false);
        }
        else if (mutation == "worker-head")
        {
            preservedWorker = await state
                .CommitAsync(worker, "late.txt", "worker moved during check\n", token)
                .ConfigureAwait(false);
        }
        else if (mutation == "integration-tree")
        {
            File.WriteAllText(
                Path.Combine(state.IntegrationPath, "shared.txt"),
                "uncommitted integration work during check\n"
            );
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
        (string Index, string Shared)? integrationWorktree = null;
        if (mutation == "integration-tree")
        {
            integrationWorktree = (
                await state.GitAsync(state.IntegrationPath, token, "ls-files", "--stage")
                    .ConfigureAwait(false),
                File.ReadAllText(Path.Combine(state.IntegrationPath, "shared.txt"))
            );
        }
        await held.ReleaseAndJoinAsync().ConfigureAwait(false);
        JsonObject read = await state.ReadAsync(change, token).ConfigureAwait(false);
        JsonObject request = Assert
            .ContainsSingle(read["integrationRequests"]!.AsArray())!
            .AsObject();
        AssertRefusal(
            read,
            request,
            code,
            mutation switch
            {
                "integration-head" => "Integration or candidate moved",
                "integration-tree" => "Checked commit or tracked tree/index changed",
                "worker-head" => "submitted worker head changed",
                _ => "Candidate checks failed",
            }
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
        if (integrationWorktree is { } preservedWorktree)
        {
            Assert.AreEqual(
                preservedWorktree.Index,
                await state.GitAsync(state.IntegrationPath, token, "ls-files", "--stage")
                    .ConfigureAwait(false)
            );
            Assert.AreEqual(
                preservedWorktree.Shared,
                File.ReadAllText(Path.Combine(state.IntegrationPath, "shared.txt"))
            );
        }
        Assert.AreEqual(
            retainedHead,
            (await state.GitAsync(path, token, "rev-parse", "HEAD").ConfigureAwait(false)).Trim()
        );
        Assert.AreEqual(
            retainedIndex,
            await state.GitAsync(path, token, "ls-files", "--stage").ConfigureAwait(false)
        );
        Assert.AreEqual(retainedShared, File.ReadAllText(Path.Combine(path, "shared.txt")));
        Assert.AreEqual(
            preservedWorker,
            await state
                .ResolveAsync(change["branch"]!.GetValue<string>(), token)
                .ConfigureAwait(false)
        );
        if (mutation == "worker-head")
        {
            Assert.AreEqual(candidate, retainedHead);
            Assert.AreEqual(submitted, read["head"]!.GetValue<string>());
            JsonObject retainedReview = Assert
                .ContainsSingle(read["reviews"]!.AsArray())!
                .AsObject();
            Assert.AreEqual(
                review["id"]!.GetValue<long>(),
                retainedReview["id"]!.GetValue<long>()
            );
            Assert.AreEqual(submitted, retainedReview["head"]!.GetValue<string>());
            Assert.AreEqual("approve", retainedReview["decision"]!.GetValue<string>());
        }

        JsonObject check = Assert.ContainsSingle(read["checks"]!.AsArray())!.AsObject();
        Assert.AreEqual(candidate, check["commit"]!.GetValue<string>());
        Assert.AreEqual(candidateTree, check["tree"]!.GetValue<string>());
        Assert.AreEqual(0, check["exitCode"]!.GetValue<int>());
        Assert.AreEqual("candidate", check["kind"]!.GetValue<string>());
        bool candidateChanged = mutation is "tracked-tree" or "index" or "candidate-head";
        Assert.AreEqual(
            candidateChanged ? "failed" : "passed",
            check["status"]!.GetValue<string>()
        );
        string log = File.ReadAllText(check["logPath"]!.GetValue<string>());
        Assert.Contains("Configured check execution: controlled.", log);
        Assert.Contains("controlled check exit 0", log);
        if (candidateChanged)
        {
            Assert.Contains("Candidate identity failure:", log);
            Assert.Contains("old check evidence cannot advance integration", log);
        }
        held.Complete();
    }

    [TestMethod]
    [TestCategory("SerialExactCandidateIntegration")]
    public async Task FailingCandidateCheckRetainsActualFailureWithoutAdvancingIntegration()
    {
        CancellationToken token = testContext.CancellationToken;
        using GitScenarioState state = await GitScenarioState
            .CreateAsync(
                token,
                [new() { Executable = "controlled-failing-check" }],
                configuredCheck: (_, _, _, _, checkToken) =>
                {
                    checkToken.ThrowIfCancellationRequested();
                    return Task.FromResult(
                        new ProcessResult(
                            23,
                            "controlled check exit 23",
                            "controlled check failed 23",
                            TimedOut: false
                        )
                    );
                }
            )
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
        Assert.Contains("Configured check execution: controlled.", log);
        Assert.Contains("controlled check exit 23", log);
        Assert.Contains("controlled check failed 23", log);
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
    public async Task EarlierRequiredFailureCannotBeOverriddenByPassingLastCandidateCheck()
    {
        CancellationToken token = testContext.CancellationToken;
        using GitScenarioState state = await GitScenarioState.CreateAsync(
            token,
            [
                new() { Executable = "controlled-first-fails" },
                new() { Executable = "controlled-last-passes" },
            ],
            configuredCheck: (executable, _, _, _, _) => Task.FromResult(
                executable switch
                {
                    "controlled-first-fails" => new ProcessResult(23, "first failure", "", false),
                    "controlled-last-passes" => new ProcessResult(0, "last success", "", false),
                    _ => throw new InvalidOperationException("Unexpected controlled command."),
                }
            )
        ).ConfigureAwait(false);
        await state.Operations.InitializeAsync(token).ConfigureAwait(false);
        string submitted = await state.CommitAsync(
            state.Operations.GetWorkerPath("worker1"),
            "mixed-checks.txt",
            "Every required candidate check must pass.\n",
            token
        ).ConfigureAwait(false);
        JsonObject change = await state.SubmitAsync("worker1", "Mixed required checks", token)
            .ConfigureAwait(false);
        await state.ReviewAsync("worker2", change, token).ConfigureAwait(false);
        await state.QueueAsync(change, token).ConfigureAwait(false);

        await state.Operations.ProcessPendingAsync(token).ConfigureAwait(false);

        JsonObject read = await state.ReadAsync(change, token).ConfigureAwait(false);
        JsonObject request = Assert.ContainsSingle(read["integrationRequests"]!.AsArray())!
            .AsObject();
        AssertRefusal(read, request, "check_failed", "Candidate checks failed");
        JsonObject[] checks = read["checks"]!.AsArray().OfType<JsonObject>().ToArray();
        Assert.HasCount(2, checks);
        string candidate = checks[0]["commit"]!.GetValue<string>();
        string tree = await state.ResolveAsync(candidate + "^{tree}", token).ConfigureAwait(false);
        foreach (
            (int index, string command, string status, int exit, string output) in new[]
            {
                (0, "controlled-first-fails", "failed", 23, "first failure"),
                (1, "controlled-last-passes", "passed", 0, "last success"),
            }
        )
        {
            JsonObject check = checks[index];
            Assert.AreEqual("candidate", check["kind"]!.GetValue<string>());
            Assert.AreEqual(status, check["status"]!.GetValue<string>());
            Assert.AreEqual(exit, check["exitCode"]!.GetValue<int>());
            Assert.AreEqual(command, check["command"]!["executable"]!.GetValue<string>());
            Assert.AreEqual(candidate, check["commit"]!.GetValue<string>());
            Assert.AreEqual(tree, check["tree"]!.GetValue<string>());
            string log = File.ReadAllText(check["logPath"]!.GetValue<string>());
            Assert.Contains("Configured check execution: controlled.", log);
            Assert.Contains(output, log);
        }
        Assert.AreEqual(
            state.BaseCommit,
            await state.ResolveAsync(candidate + "^1", token).ConfigureAwait(false)
        );
        Assert.AreEqual(
            submitted, await state.ResolveAsync(candidate + "^2", token).ConfigureAwait(false)
        );
        Assert.AreEqual(
            state.BaseCommit,
            await state.ResolveAsync(state.IntegrationRef, token).ConfigureAwait(false)
        );
        Assert.AreEqual(
            submitted,
            await state.ResolveAsync(read["branch"]!.GetValue<string>(), token)
                .ConfigureAwait(false)
        );
    }

    [TestMethod]
    [TestCategory("SerialExactCandidateIntegration")]
    [Timeout(120_000, CooperativeCancellation = true)]
    public async Task CancelledActualCandidateCheckRetainsEvidenceAndSettlesItsOwnedLease()
    {
        await using HeldCheckScope held = new(testContext.CancellationToken);
        CancellationToken token = held.Token;
        OwnedChildControl control = held.OwnAsync(new OwnedChildControl());
        CheckCommand checkCommand = new()
        {
            Executable = OwnedChildControl.ExecutablePath,
            Arguments = control.Arguments(role: OwnedChildControl.HeldCandidateCheckRole),
        };
        GitScenarioState state = held.Own(
            await GitScenarioState.CreateAsync(token, [checkCommand]).ConfigureAwait(false)
        );
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
        Task processing = held.Start(
            () => state.Operations.ProcessPendingAsync(held.ProducerToken)
        );

        OwnedChildIdentity identity = await control
            .ReadReadyAsync(processing, token)
            .ConfigureAwait(false);
        await held.CancelAndAssertCancelledAsync().ConfigureAwait(false);

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
        string log = File.ReadAllText(check["logPath"]!.GetValue<string>());
        Assert.Contains("Caller cancelled the check", log);
        Assert.Contains("owned check diagnostic stdout before READY", log);
        Assert.Contains("owned check diagnostic stderr before READY", log);
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
        // Physical child lease settlement has no public reader; verify directly.
        Assert.AreEqual(
            "exited",
            state.Text(
                "SELECT state FROM process_leases WHERE pid=$pid AND start_ticks=$birth;",
                ("$pid", identity.Pid),
                ("$birth", identity.BirthTicks)
            )
        );
        held.Complete();
    }

    [TestMethod]
    [TestCategory("SerialExactCandidateIntegration")]
    [Timeout(120_000, CooperativeCancellation = true)]
    public async Task ReportedCheckTimeoutRetainsEvidenceWithoutAuthorizingDelivery()
    {
        CancellationToken token = testContext.CancellationToken;
        CheckCommand configured = new()
        {
            Executable = "controlled-candidate-check",
            TimeoutSeconds = 1,
        };
        TimeSpan effectiveTimeout = default;
        using GitScenarioState state = await GitScenarioState
            .CreateAsync(
                token,
                [configured],
                configuredCheck: (_, _, _, timeout, _) =>
                {
                    effectiveTimeout = timeout;
                    return Task.FromResult(
                        new ProcessResult(0, "controlled reported timeout", "", TimedOut: true)
                    );
                }
            )
            .ConfigureAwait(false);
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

        await state.Operations.ProcessPendingAsync(token).ConfigureAwait(false);

        JsonObject read = await state.ReadAsync(change, token).ConfigureAwait(false);
        JsonObject request = Assert
            .ContainsSingle(read["integrationRequests"]!.AsArray())!
            .AsObject();
        AssertRefusal(read, request, "check_failed", "Candidate checks failed");
        JsonObject check = Assert.ContainsSingle(read["checks"]!.AsArray())!.AsObject();
        Assert.AreEqual("timed-out", check["status"]!.GetValue<string>());
        Assert.AreEqual(0, check["exitCode"]!.GetValue<int>());
        Assert.AreEqual(1, check["command"]!["timeoutSeconds"]!.GetValue<int>());
        Assert.AreEqual(TimeSpan.FromSeconds(1), effectiveTimeout);
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
        Assert.Contains("Configured check execution: controlled.", log);
        Assert.Contains("controlled reported timeout", log);
        Assert.DoesNotContain("Command cancellation evidence:", log);
    }

    [TestMethod]
    [DataRow("cancelled")]
    [DataRow("failed")]
    [TestCategory("SerialExactCandidateIntegration")]
    [Timeout(120_000, CooperativeCancellation = true)]
    public async Task ControlledCheckInterruptionPreservesCandidateAndTruthfulEvidence(
        string outcome
    )
    {
        await using HeldCheckScope held = new(testContext.CancellationToken);
        CancellationToken token = held.Token;
        TaskCompletionSource settled = held.CreateBarrier();
        TaskCompletionSource cleanupEntered = held.CreateBarrier();
        TaskCompletionSource cleanupRelease = held.CreateBarrier();
        TaskCompletionSource<Task<ProcessResult>> invocationStarted = new(
            TaskCreationOptions.RunContinuationsAsynchronously
        );
        GitScenarioState state = held.Own(
            await GitScenarioState
                .CreateAsync(
                    token,
                    [new() { Executable = "controlled-candidate-check" }],
                    configuredCheck: (_, _, _, _, checkToken) =>
                    {
                        Task<ProcessResult> invocation = InvokeAsync(checkToken);
                        held.Track(invocation, outcome == "failed" ? typeof(IOException) : null);
                        invocationStarted.SetResult(invocation);
                        return invocation;
                    }
                )
                .ConfigureAwait(false)
        );
        await state.Operations.InitializeAsync(token).ConfigureAwait(false);
        string submitted = await state
            .CommitAsync(
                state.Operations.GetWorkerPath("worker1"),
                "interrupted.txt",
                "worker work survives failed validation\n",
                token
            )
            .ConfigureAwait(false);
        JsonObject change = await state
            .SubmitAsync("worker1", "Interrupted validation is not authorization", token)
            .ConfigureAwait(false);
        await state.ReviewAsync("worker2", change, token).ConfigureAwait(false);
        JsonObject queued = await state.QueueAsync(change, token).ConfigureAwait(false);
        long requestId = queued["requestId"]!.GetValue<long>();
        Task processing = held.Start(
            () => state.Operations.ProcessPendingAsync(held.ProducerToken)
        );

        await held.AwaitEntryAsync().ConfigureAwait(false);
        Task<ProcessResult> activeInvocation = await invocationStarted.Task
            .WaitAsync(token)
            .ConfigureAwait(false);
        Task<bool> earlyPublication = PublishedBeforeInvocationAsync(activeInvocation, processing);
        if (outcome == "cancelled")
        {
            await held.CancelProducerAsync().ConfigureAwait(false);
            await held.AwaitBarrierAsync(cleanupEntered.Task).ConfigureAwait(false);
            JsonObject running = await state.ReadAsync(change, token).ConfigureAwait(false);
            JsonObject activeRequest = Assert
                .ContainsSingle(running["integrationRequests"]!.AsArray())!
                .AsObject();
            Assert.AreEqual(requestId, activeRequest["requestId"]!.GetValue<long>());
            Assert.AreEqual("running", activeRequest["state"]!.GetValue<string>());
            Assert.IsEmpty(running["checks"]!.AsArray());
            Assert.IsFalse(activeInvocation.IsCompleted);
            Assert.IsFalse(processing.IsCompleted);
            cleanupRelease.TrySetResult();
            await Assert
                .ThrowsAsync<OperationCanceledException>(async () =>
                    await processing.ConfigureAwait(false)
                )
                .ConfigureAwait(false);
        }
        else
        {
            await held.ReleaseAndJoinAsync().ConfigureAwait(false);
        }

        Assert.IsFalse(await earlyPublication.ConfigureAwait(false));
        Assert.IsTrue(settled.Task.IsCompletedSuccessfully);
        JsonObject read = await state.ReadAsync(change, token).ConfigureAwait(false);
        JsonObject request = Assert.ContainsSingle(read["integrationRequests"]!.AsArray())!
            .AsObject();
        AssertRefusal(
            read,
            request,
            outcome == "cancelled" ? "unavailable" : "check_failed",
            outcome == "cancelled" ? "Integration cancelled" : "Candidate checks failed"
        );
        JsonObject check = Assert.ContainsSingle(read["checks"]!.AsArray())!.AsObject();
        Assert.AreEqual(outcome, check["status"]!.GetValue<string>());
        Assert.IsNull(check["exitCode"]);
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
            await state.ResolveAsync(read["branch"]!.GetValue<string>(), token)
                .ConfigureAwait(false)
        );
        string log = File.ReadAllText(check["logPath"]!.GetValue<string>());
        Assert.Contains("Configured check execution: controlled.", log);
        Assert.Contains(
            outcome == "cancelled"
                ? "the controlled invocation joined its owned operation"
                : "Controlled configured invocation failed.",
            log
        );
        Assert.DoesNotContain("ProcessRunner settled owned execution", log);
        Assert.DoesNotContain("Command cancellation evidence:", log);
        Assert.DoesNotContain("\"leaseId\"", log);
        held.Complete();

        async Task<ProcessResult> InvokeAsync(CancellationToken checkToken)
        {
            held.SignalEntered();
            bool cancelled = false;
            try
            {
                await held.Release.WaitAsync(checkToken).ConfigureAwait(false);
                throw new IOException("Controlled configured invocation failed.");
            }
            catch (OperationCanceledException) when (checkToken.IsCancellationRequested)
            {
                cancelled = true;
                throw;
            }
            finally
            {
                if (cancelled)
                {
                    cleanupEntered.TrySetResult();
                    await cleanupRelease.Task.ConfigureAwait(false);
                }
                settled.TrySetResult();
            }
        }

        static async Task<bool> PublishedBeforeInvocationAsync(Task invocation, Task processing)
        {
            Task first = await Task.WhenAny(invocation, processing).ConfigureAwait(false);
            return ReferenceEquals(first, processing);
        }
    }

    [TestMethod]
    [TestCategory("SerialExactCandidateIntegration")]
    [Timeout(120_000, CooperativeCancellation = true)]
    public async Task RunningCandidateFreezesExactHeadReviewUntilCheckedIntegration()
    {
        await using HeldCheckScope held = new(testContext.CancellationToken);
        CancellationToken token = held.Token;
        GitScenarioState state = held.Own(
            await GitScenarioState
                .CreateAsync(
                    token,
                    [new() { Executable = "controlled-review-freeze-check" }],
                    configuredCheck: (_, _, _, _, checkToken) =>
                        held.WaitCheckAsync("Review-freeze check completed.", checkToken)
                )
                .ConfigureAwait(false)
        );
        await state.Operations.InitializeAsync(token).ConfigureAwait(false);
        string submitted = await state.CommitAsync(
            state.Operations.GetWorkerPath("worker1"),
            "review-freeze.txt",
            "Checked under the original peer approval.\n",
            token
        ).ConfigureAwait(false);
        JsonObject change = await state.SubmitAsync("worker1", "Frozen review", token)
            .ConfigureAwait(false);
        JsonObject review = await state.ReviewAsync("worker2", change, token)
            .ConfigureAwait(false);
        JsonObject queued = await state.QueueAsync(change, token).ConfigureAwait(false);
        long requestId = queued["requestId"]!.GetValue<long>();
        _ = held.Start(() => state.Operations.ProcessPendingAsync(held.ProducerToken));

        await held.AwaitEntryAsync().ConfigureAwait(false);
        JsonObject running = await state.ReadAsync(change, token).ConfigureAwait(false);
        Assert.AreEqual("open", running["state"]!.GetValue<string>());
        Assert.AreEqual(submitted, running["head"]!.GetValue<string>());
        JsonObject active = Assert.ContainsSingle(
            running["integrationRequests"]!.AsArray()
        )!.AsObject();
        Assert.AreEqual(requestId, active["requestId"]!.GetValue<long>());
        Assert.AreEqual("running", active["state"]!.GetValue<string>());
        Assert.IsNull(active["resultCode"]);
        Assert.IsNull(active["integratedCommit"]);
        AssertReviewRetained(running);

        JsonObject rejected = await state.Operations.InvokeAsync(
            "worker2",
            "change_review",
            new()
            {
                ["id"] = change["id"]!.GetValue<long>(),
                ["head"] = submitted,
                ["decision"] = "request-changes",
                ["body"] = "Withdraw approval of this unchanged submitted commit.",
            },
            token
        ).ConfigureAwait(false);
        Assert.IsFalse(rejected["ok"]!.GetValue<bool>());
        Assert.AreEqual("conflict", rejected["code"]!.GetValue<string>());
        Assert.AreEqual(
            "Candidate identity/reviews are frozen while the coordinator integrates it.",
            rejected["message"]!.GetValue<string>()
        );
        JsonObject stillRunning = await state.ReadAsync(change, token).ConfigureAwait(false);
        AssertReviewRetained(stillRunning);
        Assert.AreEqual(
            "running",
            Assert.ContainsSingle(stillRunning["integrationRequests"]!.AsArray())![
                "state"
            ]!.GetValue<string>()
        );
        Assert.AreEqual(
            state.BaseCommit,
            await state.ResolveAsync(state.IntegrationRef, token).ConfigureAwait(false)
        );

        await held.ReleaseAndJoinAsync().ConfigureAwait(false);
        JsonObject integrated = await state.ReadAsync(change, token).ConfigureAwait(false);
        Assert.AreEqual("integrated", integrated["state"]!.GetValue<string>());
        AssertReviewRetained(integrated);
        JsonObject request = Assert.ContainsSingle(
            integrated["integrationRequests"]!.AsArray()
        )!.AsObject();
        Assert.AreEqual(requestId, request["requestId"]!.GetValue<long>());
        Assert.AreEqual("succeeded", request["state"]!.GetValue<string>());
        Assert.AreEqual("ok", request["resultCode"]!.GetValue<string>());
        string merged = request["integratedCommit"]!.GetValue<string>();
        Assert.AreEqual(
            merged, await state.ResolveAsync(state.IntegrationRef, token).ConfigureAwait(false)
        );
        JsonObject check = Assert.ContainsSingle(integrated["checks"]!.AsArray())!.AsObject();
        Assert.AreEqual("candidate", check["kind"]!.GetValue<string>());
        Assert.AreEqual("passed", check["status"]!.GetValue<string>());
        Assert.AreEqual(0, check["exitCode"]!.GetValue<int>());
        Assert.AreEqual(merged, check["commit"]!.GetValue<string>());
        Assert.AreEqual(
            await state.ResolveAsync(merged + "^{tree}", token).ConfigureAwait(false),
            check["tree"]!.GetValue<string>()
        );
        Assert.Contains(
            "Configured check execution: controlled.",
            File.ReadAllText(check["logPath"]!.GetValue<string>())
        );
        held.Complete();

        void AssertReviewRetained(JsonObject read)
        {
            JsonObject retained = Assert.ContainsSingle(read["reviews"]!.AsArray())!.AsObject();
            Assert.AreEqual(review["id"]!.GetValue<long>(), retained["id"]!.GetValue<long>());
            Assert.AreEqual(change["id"]!.GetValue<long>(), retained["changeId"]!.GetValue<long>());
            Assert.AreEqual("worker2", retained["actor"]!.GetValue<string>());
            Assert.AreEqual(submitted, retained["head"]!.GetValue<string>());
            Assert.AreEqual("approve", retained["decision"]!.GetValue<string>());
        }
    }

    [TestMethod]
    [TestCategory("SerialExactCandidateIntegration")]
    [Timeout(120_000, CooperativeCancellation = true)]
    public async Task CompletedControlledCommandSurvivesPostCommandVerificationCancellation()
    {
        CancellationToken token = testContext.CancellationToken;
        using CancellationTokenSource caller =
            CancellationTokenSource.CreateLinkedTokenSource(token);
        const string stdout = "b02-completed-stdout";
        const string stderr = "b02-completed-stderr!";
        using GitScenarioState state = await GitScenarioState.CreateAsync(
            token,
            [new() { Executable = "controlled-completed-check" }],
            configuredCheck: async (_, _, _, _, _) =>
            {
                await caller.CancelAsync().ConfigureAwait(false);
                return new(0, stdout, stderr, TimedOut: false);
            }
        ).ConfigureAwait(false);
        await state.Operations.InitializeAsync(token).ConfigureAwait(false);
        string submitted = await state.CommitAsync(
            state.Operations.GetWorkerPath("worker1"),
            "completed-check.txt",
            "Retain the completed command even when verification is cancelled.\n",
            token
        ).ConfigureAwait(false);
        JsonObject change = await state.SubmitAsync("worker1", "Completed command evidence", token)
            .ConfigureAwait(false);
        await state.ReviewAsync("worker2", change, token).ConfigureAwait(false);
        JsonObject queued = await state.QueueAsync(change, token).ConfigureAwait(false);
        OperationCanceledException cancelled = await Assert.ThrowsAsync<OperationCanceledException>(
            () => state.Operations.ProcessPendingAsync(caller.Token)
        ).ConfigureAwait(false);
        Assert.AreEqual(caller.Token, cancelled.CancellationToken);
        Assert.IsFalse(token.IsCancellationRequested);
        JsonObject read = await state.ReadAsync(change, token).ConfigureAwait(false);
        JsonObject request = Assert.ContainsSingle(read["integrationRequests"]!.AsArray())!
            .AsObject();
        Assert.AreEqual(
            queued["requestId"]!.GetValue<long>(), request["requestId"]!.GetValue<long>()
        );
        AssertRefusal(read, request, "unavailable", "Integration cancelled");
        JsonObject check = Assert.ContainsSingle(read["checks"]!.AsArray())!.AsObject();
        Assert.AreEqual("candidate", check["kind"]!.GetValue<string>());
        Assert.AreEqual("cancelled", check["status"]!.GetValue<string>());
        Assert.IsNull(check["exitCode"]);
        Assert.AreEqual(
            "controlled-completed-check", check["command"]!["executable"]!.GetValue<string>()
        );
        string candidate = check["commit"]!.GetValue<string>();
        Assert.AreEqual(
            state.BaseCommit,
            await state.ResolveAsync(candidate + "^1", token).ConfigureAwait(false)
        );
        Assert.AreEqual(
            submitted, await state.ResolveAsync(candidate + "^2", token).ConfigureAwait(false)
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
            await state.ResolveAsync(read["branch"]!.GetValue<string>(), token)
                .ConfigureAwait(false)
        );
        string log = File.ReadAllText(check["logPath"]!.GetValue<string>());
        Assert.Contains("Configured check execution: controlled.", log);
        Assert.Contains(
            "Caller cancelled the check; post-command verification did not complete.", log
        );
        const string heading = "Completed configured command evidence:\n";
        int evidenceStart = log.IndexOf(heading, StringComparison.Ordinal);
        Assert.IsGreaterThan(-1, evidenceStart);
        JsonObject completed = Assert.IsInstanceOfType<JsonObject>(
            JsonNode.Parse(log[(evidenceStart + heading.Length)..])
        );
        Assert.AreEqual(0, completed["observedExitCode"]!.GetValue<int>());
        Assert.IsFalse(completed["timedOut"]!.GetValue<bool>());
        foreach ((string name, string expected) in new[] { ("stdout", stdout), ("stderr", stderr) })
        {
            JsonNode output = completed[name]!;
            Assert.IsTrue(output["complete"]!.GetValue<bool>());
            Assert.AreEqual(expected.Length, output["characters"]!.GetValue<int>());
            Assert.IsFalse(output["truncated"]!.GetValue<bool>());
            Assert.AreEqual(expected, output["text"]!.GetValue<string>());
        }
        Assert.DoesNotContain("Command cancellation evidence:", log);
        Assert.DoesNotContain("Verification probe cancellation evidence", log);
        Assert.DoesNotContain("the controlled invocation joined its owned operation", log);
        Assert.DoesNotContain("ProcessRunner settled owned execution", log);
        Assert.DoesNotContain("\"leaseId\"", log);
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
        Assert.AreEqual(
            code, request["resultCode"]!.GetValue<string>(), request.ToJsonString()
        );
        Assert.Contains(reason, request["message"]!.GetValue<string>());
        Assert.IsNull(request["integratedCommit"]);
    }

    [TestMethod]
    [TestCategory("SerialExactCandidateIntegration")]
    public async Task UpdatingSubmittedChangePreservesOmittedLinksAndClearsExplicitEmptyLinks()
    {
        CancellationToken token = testContext.CancellationToken;
        using GitScenarioState state = await GitScenarioState
            .CreateAsync(token, configuredCheck: PassConfiguredCheckAsync)
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
        Assert.AreEqual(
            taskId,
            Assert.ContainsSingle(change["taskIds"]!.AsArray())!.GetValue<long>()
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

        JsonObject updated = await state
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
        Assert.AreEqual(
            taskId,
            Assert.ContainsSingle(updated["taskIds"]!.AsArray())!.GetValue<long>()
        );

        JsonObject omitted = await state.ReadAsync(change, token).ConfigureAwait(false);
        Assert.AreEqual(id, omitted["id"]!.GetValue<long>());
        Assert.AreEqual(head, omitted["head"]!.GetValue<string>());
        Assert.AreEqual("Linked candidate", omitted["title"]!.GetValue<string>());
        Assert.AreEqual("Omitted links retained", omitted["body"]!.GetValue<string>());
        Assert.AreEqual(
            taskId,
            Assert.ContainsSingle(omitted["taskIds"]!.AsArray())!.GetValue<long>()
        );

        JsonObject clearedWrite = await state
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
        Assert.IsEmpty(clearedWrite["taskIds"]!.AsArray());

        JsonObject cleared = await state.ReadAsync(change, token).ConfigureAwait(false);
        Assert.AreEqual(id, cleared["id"]!.GetValue<long>());
        Assert.AreEqual(head, cleared["head"]!.GetValue<string>());
        Assert.AreEqual("Linked candidate", cleared["title"]!.GetValue<string>());
        Assert.AreEqual("Omitted links retained", cleared["body"]!.GetValue<string>());
        Assert.IsEmpty(cleared["taskIds"]!.AsArray());
        JsonObject listed = await state
            .InvokeAsync("owner", "change_list", new(), token)
            .ConfigureAwait(false);
        Assert.IsEmpty(
            Assert.ContainsSingle(listed["items"]!.AsArray())!["taskIds"]!.AsArray()
        );
        Assert.AreEqual(
            "open",
            state.Collaboration.Invoke("owner", "task_read", new() { ["id"] = taskId })["data"]![
                "state"
            ]!.GetValue<string>()
        );
    }

    private static string[] OwnedWorktreePaths(GitScenarioState state) =>
    [
        state.IntegrationPath,
        .. state.Database.Configuration.WorkerIds.Select(state.Operations.GetWorkerPath),
    ];

    private sealed class HeldCheckScope : IAsyncDisposable
    {
        private readonly CancellationTokenSource _deadline = new(120_000);
        private readonly CancellationTokenSource _lifetime;
        private readonly CancellationTokenSource _producerCancellation;
        private readonly List<TaskCompletionSource> _barriers = [];
        private readonly List<(Task Task, Type? AllowedError)> _trackedTasks = [];
        private readonly List<Func<ValueTask>> _disposables = [];
        private readonly TaskCompletionSource _entered;
        private readonly TaskCompletionSource _release;
        private Task? _producer;
        private bool _completed;

        internal HeldCheckScope(CancellationToken caseToken)
        {
            _lifetime = CancellationTokenSource.CreateLinkedTokenSource(
                caseToken,
                _deadline.Token
            );
            _producerCancellation = CancellationTokenSource.CreateLinkedTokenSource(
                _lifetime.Token
            );
            _entered = CreateBarrier();
            _release = CreateBarrier();
        }

        internal CancellationToken Token => _lifetime.Token;

        internal CancellationToken ProducerToken => _producerCancellation.Token;

        internal Task Release => _release.Task;

        internal void SignalEntered() => _entered.TrySetResult();

        internal TaskCompletionSource CreateBarrier()
        {
            TaskCompletionSource barrier = new(
                TaskCreationOptions.RunContinuationsAsynchronously
            );
            _barriers.Add(barrier);
            return barrier;
        }

        internal T Own<T>(T resource)
            where T : IDisposable
        {
            _disposables.Add(() =>
            {
                resource.Dispose();
                return ValueTask.CompletedTask;
            });
            return resource;
        }

        internal T OwnAsync<T>(T resource)
            where T : IAsyncDisposable
        {
            _disposables.Add(resource.DisposeAsync);
            return resource;
        }

        internal void Track(Task task, Type? allowedError = null) =>
            _trackedTasks.Add((task, allowedError));

        internal Task Start(Func<Task> producer)
        {
            _producer = Task.Run(producer);
            return _producer;
        }

        internal async Task<ProcessResult> WaitCheckAsync(
            string output,
            CancellationToken checkToken
        )
        {
            _entered.TrySetResult();
            await _release.Task.WaitAsync(checkToken).ConfigureAwait(false);
            return new(0, output, "", false);
        }

        internal Task AwaitEntryAsync() => AwaitBarrierAsync(_entered.Task);

        internal async Task AwaitBarrierAsync(Task barrier)
        {
            Task producer =
                _producer
                ?? throw new InvalidOperationException("Held producer has not started.");
            Task completed = await Task.WhenAny(barrier, producer)
                .WaitAsync(Token)
                .ConfigureAwait(false);
            if (ReferenceEquals(completed, producer))
            {
                await producer.ConfigureAwait(false);
                Assert.Fail("Candidate processing ended before the held barrier was entered.");
            }
            await barrier.WaitAsync(Token).ConfigureAwait(false);
        }

        internal async Task ReleaseAndJoinAsync()
        {
            Task producer =
                _producer
                ?? throw new InvalidOperationException("Held producer has not started.");
            _release.TrySetResult();
            await producer.ConfigureAwait(false);
        }

        internal Task CancelProducerAsync() => _producerCancellation.CancelAsync();

        internal async Task CancelAndAssertCancelledAsync()
        {
            Task producer =
                _producer
                ?? throw new InvalidOperationException("Held producer has not started.");
            await _producerCancellation.CancelAsync().ConfigureAwait(false);
            await Assert
                .ThrowsAsync<OperationCanceledException>(async () =>
                    await producer.ConfigureAwait(false)
                )
                .ConfigureAwait(false);
        }

        internal void Complete() => _completed = true;

        public async ValueTask DisposeAsync()
        {
            List<Exception> failures = [];
            foreach (TaskCompletionSource barrier in _barriers)
            {
                barrier.TrySetResult();
            }
            try
            {
                await _producerCancellation.CancelAsync().ConfigureAwait(false);
            }
            catch (Exception error)
            {
                failures.Add(error);
            }
            if (_producer is not null)
            {
                try
                {
                    await _producer.ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                    when (_producerCancellation.IsCancellationRequested)
                {
                }
                catch (Exception error)
                {
                    failures.Add(error);
                }
            }
            foreach ((Task tracked, Type? allowedError) in _trackedTasks)
            {
                try
                {
                    await tracked.ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                    when (_producerCancellation.IsCancellationRequested)
                {
                }
                catch (Exception error)
                    when (allowedError?.IsInstanceOfType(error) == true)
                {
                }
                catch (Exception error)
                {
                    failures.Add(error);
                }
            }
            for (int index = _disposables.Count - 1; index >= 0; index--)
            {
                try
                {
                    await _disposables[index]().ConfigureAwait(false);
                }
                catch (Exception error)
                {
                    failures.Add(error);
                }
            }
            _producerCancellation.Dispose();
            _lifetime.Dispose();
            _deadline.Dispose();
            if (failures.Count != 0)
            {
                if (_completed)
                {
                    throw new AggregateException("Held-check cleanup failed.", failures);
                }
                foreach (Exception error in failures)
                {
                    Console.Error.WriteLine(
                        "HELD_CHECK_CLEANUP_SECONDARY " + Json.ExceptionDetails(error)
                    );
                }
            }
        }
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
        JsonObject request = Assert.ContainsSingle(
            change["integrationRequests"]!.AsArray().OfType<JsonObject>()
                .Where(item => item["requestId"]!.GetValue<long>()
                    == queued["requestId"]!.GetValue<long>())
        );
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
            "git",
            check["command"]!["executable"]!.GetValue<string>()
        );
        JsonArray arguments = check["command"]!["arguments"]!.AsArray();
        Assert.HasCount(3, arguments);
        Assert.AreEqual("rev-parse", arguments[0]!.GetValue<string>());
        Assert.AreEqual("HEAD", arguments[1]!.GetValue<string>());
        Assert.AreEqual("HEAD^{tree}", arguments[2]!.GetValue<string>());
        Assert.AreEqual(
            state.Database.Configuration.CandidateChecks[0].TimeoutSeconds,
            check["command"]!["timeoutSeconds"]!.GetValue<int>()
        );
        string log = File.ReadAllText(check["logPath"]!.GetValue<string>())
            .ReplaceLineEndings("\n");
        const string outputStart = "Configured check execution: ProcessRunner.\nstdout:\n";
        const string outputEnd = "\nstderr:\n";
        Assert.StartsWith(outputStart, log);
        int end = log.IndexOf(outputEnd, outputStart.Length, StringComparison.Ordinal);
        Assert.IsGreaterThanOrEqualTo(outputStart.Length, end);
        string[] output = log[outputStart.Length..end]
            .Split('\n', StringSplitOptions.RemoveEmptyEntries);
        Assert.HasCount(2, output);
        Assert.AreEqual(candidate, output[0]);
        Assert.AreEqual(
            await state.ResolveAsync(candidate + "^{tree}", token).ConfigureAwait(false),
            output[1]
        );
        JsonObject exactReview = Assert.ContainsSingle(
            change["reviews"]!.AsArray().OfType<JsonObject>()
                .Where(item => item["id"]!.GetValue<long>() == review["id"]!.GetValue<long>())
        );
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
