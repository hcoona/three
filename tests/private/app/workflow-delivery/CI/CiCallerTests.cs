using System.Text.Json;
using WorkflowDelivery.CI;
using WorkflowDelivery.CI.Callers;
using WorkflowDelivery.Platform;
using WorkflowDelivery.Repository;
using WorkflowDelivery.Tests.Platform;
using static WorkflowDelivery.Tests.CI.CiCallerState;
using State = WorkflowDelivery.Tests.CI.CiCallerState;
using GitFixture = WorkflowDelivery.Tests.Repository.GitReaderTests.GitFixture;

namespace WorkflowDelivery.Tests.CI;

[TestClass]
public sealed class CiCallerTests(TestContext context)
{
    private static readonly string[] Retained = ["src/retained"];
    private static readonly string[] LateAndRetained = ["src/late", "src/retained"];
    private static readonly string[] DifficultDirectories = ["src/ordinary", "src/with spaces",
        "quote\"\\\n\r"];

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task ControlPrepareTransfersAbsentBasisOrReusesFullEndpoint(bool full)
    {
        using var repo = await GitFixture.CreateAsync(context.CancellationToken);
        await repo.SetAsync("README.md", "initial");
        string previous = await repo.CommitAsync();
        await repo.SetAsync(ControlTestInputs.Project, "<Project />");
        string candidate = await repo.CommitAsync();
        using var files = new State();
        string payload = files.Put("event.json",
            $$$$"""{"pull_request":{"base":{"sha":"{{{{previous}}}}"}}}""");
        var call = Run(0, "caller-control", "prepare", repo.Directory, files.At("control"),
            full ? "workflow_dispatch" : "pull_request", payload, candidate);
        var request = call.Read(TransferJson.Default.ControlTestInputRequest);
        string basis = full ? candidate : previous;
        Assert.AreEqual(new CiComparison(basis, candidate, full), request.Comparison);
        Assert.AreEqual(candidate, (await new GitReader(request.CandidateDirectory)
            .ReadAsync("HEAD", context.CancellationToken)).Commit);
        Assert.AreEqual(full ? request.CandidateDirectory : null, request.BasisDirectory);
        var endpoints = files.Read("control/group-endpoints.json",
            TransferJson.Default.CiCallerEndpoints);
        Assert.AreEqual(basis, endpoints.Basis.Reference);
        Assert.AreEqual(candidate, endpoints.Candidate.Reference);
        Assert.AreEqual(full, endpoints.Basis.Directory == endpoints.Candidate.Directory);
        Assert.AreEqual(request, files.Read("control/request.json",
            TransferJson.Default.ControlTestInputRequest));
    }

    [TestMethod]
    public async Task ControlPrepareRejectsWrongCandidateAndCheckoutOverlap()
    {
        using var repo = await GitFixture.CreateAsync(context.CancellationToken);
        await repo.SetAsync("README.md");
        string basis = await repo.CommitAsync();
        await repo.SetAsync(ControlTestInputs.Project, "<Project />");
        string candidate = await repo.CommitAsync();
        using var files = new State();
        string payload = files.Put("event.json", "{}");
        Run(2, "caller-control", "prepare", repo.Directory, files.At("control"),
            "workflow_dispatch", payload, basis);
        Run(2, "caller-control", "prepare", repo.Directory, repo.Directory,
            "workflow_dispatch", payload, candidate);
        await Assert.ThrowsExactlyAsync<InvalidDataException>(() => ControlCiCaller.PrepareAsync(
            repo.Directory, files.At("control"), new(basis, candidate, true),
            context.CancellationToken));
        Assert.IsFalse(Directory.Exists(files.At("control")));
    }

    [TestMethod]
    public async Task MaterializeUsesExactCommittedNodeEndpointsAndPythonFullReuse()
    {
        using var repo = await GitFixture.CreateAsync(context.CancellationToken);
        await repo.SetAsync("source.txt", "original");
        string commit = await repo.CommitAsync();
        await repo.GitAsync("reset", "--hard", commit);
        using var files = new State();
        string scope = files.Put("scope.json", $$$$"""
            {"base":"{{{{commit}}}}","candidate":"{{{{commit}}}}","full":true,"changed_paths":[],
             "endpoint_owners":{"basis":{"revision":"{{{{commit}}}}","paths":[]},
                "candidate":{"revision":"{{{{commit}}}}","paths":[]}}}
            """);
        string transfer = files.Transfer();
        var node = Run(0, "caller-node", "materialize", repo.Directory, scope, transfer,
            files.At("node")).Read(TransferJson.Default.NodeCiCallerContext);
        Assert.AreNotEqual(node.Basis.Directory, node.Candidate.Directory);
        Assert.AreEqual(commit, node.Basis.Reference);
        Assert.AreEqual(commit, node.Candidate.Reference);
        Assert.AreEqual("original", File.ReadAllText(Path.Combine(node.Candidate.Directory,
            "source.txt")));
        var python = Run(0, "caller-python", "materialize", repo.Directory, scope, transfer,
            files.At("python")).Read(TransferJson.Default.PythonCiCallerContext);
        Assert.AreEqual(python.Basis, python.Candidate);
        Assert.IsTrue(Directory.Exists(python.ControlSources));
        Assert.AreEqual(scope, python.ScopePath);
        Run(2, "caller-node", "materialize", repo.Directory, scope, transfer, files.At("node"));

        await repo.SetAsync("other.txt");
        await repo.CommitAsync(commit);
        Run(2, "caller-node", "materialize", repo.Directory, scope, transfer, files.At("foreign"));
        Assert.IsFalse(Directory.Exists(files.At("foreign")));
    }

    [TestMethod]
    public async Task CallerMaterializationBypassesRequiredLfsFilters()
    {
        const string pointer = "version https://git-lfs.github.com/spec/v1\n" +
            "oid sha256:0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef\n" +
            "size 123\n";
        using var repo = await GitFixture.CreateAsync(context.CancellationToken);
        await repo.SetAsync(".gitattributes", "*.sf2 filter=lfs diff=lfs merge=lfs -text\n");
        await repo.SetAsync("asset.sf2", pointer);
        await repo.SetAsync(ControlTestInputs.Project, "<Project />");
        string commit = await repo.CommitAsync();
        string hooks = Directory.CreateDirectory(Path.Combine(repo.Directory, ".git", "hooks"))
            .FullName.Replace('\\', '/');
        await repo.GitAsync("config", "core.hooksPath", hooks);
        await repo.GitAsync("lfs", "install", "--local");

        using var files = new State();
        NativeCommandResult bare = await NativeProcess.ExecuteAsync(new("git", repo.Directory,
            ["worktree", "add", "--detach", files.At("bare"), commit], 30,
            new Dictionary<string, string?> { ["GIT_LFS_SKIP_SMUDGE"] = null }),
            context.CancellationToken);
        Assert.IsFalse(bare.Succeeded);

        string payload = files.Put("event.json", "{}");
        ControlTestInputRequest control = Run(0, "caller-control", "prepare", repo.Directory,
            files.At("control"), "workflow_dispatch", payload, commit)
            .Read(TransferJson.Default.ControlTestInputRequest);
        Assert.AreEqual(pointer, File.ReadAllText(Path.Combine(control.CandidateDirectory,
            "asset.sf2")).Replace("\r\n", "\n", StringComparison.Ordinal));

        string scope = files.Put("scope.json", $$$$"""
            {"base":"{{{{commit}}}}","candidate":"{{{{commit}}}}","full":true,"changed_paths":[],
             "endpoint_owners":{"basis":{"revision":"{{{{commit}}}}","paths":[]},
                "candidate":{"revision":"{{{{commit}}}}","paths":[]}}}
            """);
        string nodeState = files.At("node");
        var node = Run(0, "caller-node", "materialize", repo.Directory, scope, files.Transfer(),
            nodeState).Read(TransferJson.Default.NodeCiCallerContext);
        var group = NodeGroup();
        CiCallerTransfer.Write(Path.Combine(nodeState, "group-plan.json"), group with
        {
            Adopted = group.Adopted with { Comparison = commit, Candidate = commit },
            Runtime = group.Runtime with { Revision = commit },
        }, TransferJson.Default.NodeGroupReadback);
        var runtime = Run(0, "caller-node", "runtime", repo.Directory, nodeState, "node22")
            .Read(TransferJson.Default.NodeRunRequest);
        GitMaterialization bound = await GitMaterialization.BindAsync(runtime.Checkout, commit,
            context.CancellationToken);
        Assert.AreEqual(pointer, (await bound.ReadOptionalTextAsync("asset.sf2",
            context.CancellationToken))?.Replace("\r\n", "\n", StringComparison.Ordinal));
        Assert.AreEqual(commit, node.Candidate.Reference);
    }

    [TestMethod]
    [DataRow("{}", false)]
    [DataRow("""{"application":"a","root":"r","transfer":"t","unknown":1}""", false)]
    [DataRow("""{"application":"a","root":"r","root":"other","transfer":"t"}""", true)]
    public void TransferReadRejectsMissingUnknownAndDuplicateFields(string payload, bool duplicate)
    {
        using var files = new State();
        files.Put("context.json", payload);
        if (duplicate)
            Assert.ThrowsExactly<InvalidDataException>(() =>
                files.Read("context.json", TransferJson.Default.CiCallerControl));
        else
            Assert.ThrowsExactly<JsonException>(() =>
                files.Read("context.json", TransferJson.Default.CiCallerControl));
    }

    [TestMethod]
    public void DotNetBootstrapAndPrepareTransferOriginalAssemblyAndFullEndpoint()
    {
        using var files = new State();
        string source = files.Make("checkout");
        string transfer = files.Transfer();
        string state = files.At("dotnet");
        var endpoint = new MaterializedEndpoint(source, Candidate);
        string endpoints = files.Put("endpoints.json", new CiCallerEndpoints(endpoint, endpoint,
            true), TransferJson.Default.CiCallerEndpoints);
        Run(0, "caller-dotnet", "bootstrap", source, state, transfer);
        string path = Run(0, "caller-dotnet", "prepare", source, state, endpoints).Output.Trim();
        var request = files.Read(path, TransferJson.Default.DotNetGroupRequest);
        Assert.AreEqual(request.CandidateFiles, request.BasisFiles);
        Assert.AreEqual(files.At("transfer/control/WorkflowDelivery.DotNet.dll"),
            request.CandidateFiles.Assembly);
        Assert.AreEqual(endpoint, request.Candidate);
        Assert.AreEqual(source, request.Completion.Checkout);
        Assert.IsTrue(request.Full);
        Assert.IsEmpty(Directory.EnumerateFileSystemEntries(request.Completion.Scratch));
        Run(2, "caller-dotnet", "bootstrap", source, files.At("checkout/state"), transfer);
        Run(2, "caller-dotnet", "bootstrap", source, files.Root, transfer);
        File.Delete(files.At("transfer/control/WorkflowDelivery.DotNet.dll"));
        Assert.Contains("lacks its .NET task assembly", Run(2, "caller-dotnet", "bootstrap",
            source, files.At("no-task"), transfer).Error);
        File.Delete(files.At("transfer/control/WorkflowDelivery.deps.json"));
        Run(2, "caller-dotnet", "bootstrap", source, files.At("incomplete"), transfer);
    }

    [TestMethod]
    [DataRow((int)CheckStatus.Passed, true)]
    [DataRow((int)CheckStatus.Failed, false)]
    [DataRow((int)CheckStatus.Cancelled, false)]
    [DataRow((int)CheckStatus.TimedOut, false)]
    [DataRow((int)CheckStatus.Skipped, false)]
    [DataRow(999, false)]
    public void OutcomeForwardsExecutionOwnedRequiredStates(int value, bool satisfied)
    {
        var status = (CheckStatus)value;
        CiPlan plan = Plan(DotNetCiCaller.Scope, DotNetCheck());
        var reported = ResultCollector.Collect(plan,
            [new(Candidate, plan.Checks[0].Work.Key, status)]);
        Assert.AreSame(reported, CiCallerTransfer.Outcome(plan, reported));
        Assert.AreEqual(satisfied, reported.Satisfied);
        if (status == (CheckStatus)999)
            Assert.Contains("Unknown result status", Assert.ContainsSingle(reported.Errors));
        else
            Assert.AreEqual(status.ToString().ToLowerInvariant(),
                Assert.ContainsSingle(reported.Checks).Status);
    }

    [TestMethod]
    public void OutcomeRejectsMissingShapeAndSubstitutedBinding()
    {
        CiPlan plan = Plan(DotNetCiCaller.Scope, DotNetCheck());
        var missing = ResultCollector.Collect(plan, []);
        Assert.IsFalse(CiCallerTransfer.Outcome(plan, missing).Satisfied);
        Assert.AreEqual("missing", Assert.ContainsSingle(missing.Checks).Status);
        foreach (CiOutcome bad in new[] { missing with { Candidate = Foreign },
                     missing with { Comparison = Foreign }, missing with { Scope = "other" },
                     missing with { Checks = null! }, missing with { Errors = null! }, null! })
            Assert.ThrowsExactly<InvalidDataException>(() => CiCallerTransfer.Outcome(plan, bad));
    }

    [TestMethod]
    public async Task DotNetExecuteRetainsMissingResultAndOriginalPlan()
    {
        using var repo = await GitFixture.CreateAsync(context.CancellationToken);
        await repo.SetAsync("source.txt");
        string commit = await repo.CommitAsync();
        await repo.GitAsync("reset", "--hard", commit);
        using var files = new State();
        string transfer = files.Transfer();
        var plan = Plan(DotNetCiCaller.Scope, DotNetCheck()) with
        { Comparison = commit, Candidate = commit };
        files.WriteDotNetPlan(plan);
        CiOutcome outcome = await DotNetCiCaller.ExecuteAsync(repo.Directory, transfer,
            files.At("run"), commit, commit, DotNetTools.None, context.CancellationToken,
            (original, request, _) =>
            {
                Assert.AreEqual(commit, original.Candidate);
                Assert.AreEqual(plan.Checks[0].Work.Key, original.Checks[0].Work.Key);
                Assert.AreEqual(repo.Directory, request.Checkout);
                Assert.IsEmpty(Directory.EnumerateFileSystemEntries(request.Scratch));
                return Task.FromResult(new DotNetGroupRunResult(commit, [], new(commit, [], [], []),
                    new(commit, [], [], [], [], []), ResultCollector.Collect(original, [])));
            });
        Assert.IsFalse(outcome.Satisfied);
        Assert.AreEqual("missing", Assert.ContainsSingle(outcome.Checks).Status);
        Assert.AreEqual(commit,
            files.Read("run/result.json", TransferJson.Default.DotNetGroupRunResult).Candidate);
        Assert.AreEqual(repo.Directory, files.Read("run/request.json",
            TransferJson.Default.DotNetRunRequest).Checkout);
        Assert.AreEqual(DotNetTools.None, files.Read("run/" + DotNetCiCaller.GroupReceiptFile(
                DotNetTools.None),
            TransferJson.Default.CiGroupReceipt).Group);
        Assert.AreEqual(commit, files.Read("run/outcome.json", TransferJson.Default.CiOutcome)
            .Candidate);
    }

    [TestMethod]
    public void DotNetJoinRequiresEachGroupAndRejectsStrayOrMisplacedResults()
    {
        using var files = new State();
        string root = files.Make("checkout");
        string transfer = files.Transfer();
        var plain = DotNetCheck("src/plain.csproj");
        var node = DotNetCheck("src/node.csproj", """{"node":"24.1.0"}""");
        var plan = Plan(DotNetCiCaller.Scope, plain, node);
        Assert.AreEqual("node+none", string.Join('+', DotNetTools.Groups(plan).Select(g => g.Key)));
        Assert.AreEqual(node.Work.Key, Assert.ContainsSingle(DotNetTools.Select(plan, "node")
            .Checks).Work.Key);
        files.WriteDotNetPlan(plan);
        var valid = new CheckResult(Candidate, plain.Work.Key, CheckStatus.Passed);
        files.WriteDotNetGroup("none", "none", plan, valid);
        var missing = Join(1, "missing");
        Assert.AreEqual("missing,passed", string.Join(',', missing.Checks
            .Select(c => c.Status).Order(StringComparer.Ordinal)));
        Assert.IsTrue(File.Exists(files.At("missing/outcome.json")));
        string nodeFile = files.At("groups/" + DotNetCiCaller.GroupReceiptFile("node"));
        Directory.CreateDirectory(nodeFile);
        File.Copy(files.At("groups/" + DotNetCiCaller.GroupReceiptFile("none")),
            Path.Combine(nodeFile, DotNetCiCaller.GroupReceiptFile("node")));
        Join(2, "archive-claiming-a-group", "Unplanned .NET group result");
        Directory.Delete(nodeFile, recursive: true);
        foreach (CheckStatus status in new[] { CheckStatus.Passed, CheckStatus.Failed,
                     CheckStatus.Cancelled })
        {
            files.WriteDotNetGroup("node", "node", plan, new CheckResult(Candidate, node.Work.Key,
                status));
            Assert.AreEqual(status == CheckStatus.Passed, Join(status == CheckStatus.Passed ? 0 : 1,
                status.ToString()).Satisfied);
        }

        var misplaced = valid with { Key = node.Work.Key };
        CheckResult[][] malformed = [[valid, misplaced], [valid, valid], [misplaced]];
        foreach (CheckResult[] bad in malformed)
        {
            files.WriteDotNetGroup("none", "none", plan, bad);
            Join(2, "invalid");
        }
        files.WriteDotNetGroup("none", "none", plan, valid);
        files.WriteDotNetGroup("node", "none", plan, misplaced);
        Join(2, "wrong-group");
        files.WriteDotNetGroup("node", "node", plan with { Comparison = Foreign }, misplaced);
        Join(2, "foreign-basis", "different original plan");
        File.Delete(files.At("groups/" + DotNetCiCaller.GroupReceiptFile("none")));
        files.WriteDotNetGroup("node", "node", plan, misplaced);
        CiPlan toolsOnly = plan with { Checks = [node] };
        files.WriteDotNetPlan(toolsOnly);
        Join(2, "changed-work-in-another-group", "different original plan");
        files.WriteDotNetGroup("node", "node", toolsOnly, misplaced);
        Assert.Contains(".NET group reported no result set: none", Join(1, "tools-only").Errors);
        File.Delete(nodeFile);
        CiPlan empty = plan with { Checks = [] };
        files.WriteDotNetPlan(empty);
        Assert.IsFalse(Join(1, "empty").Satisfied);
        files.WriteDotNetGroup("none", "none", empty);
        Assert.IsTrue(Join(0, "one-artifact-for-one-planned-group").Satisfied);
        files.Make("groups/python");
        Join(2, "unplanned-group");
        Directory.Delete(files.At("groups/python"));
        files.Put("groups/stray.json", "{}");
        Join(2, "stray");
        File.Delete(files.At("groups/stray.json"));
        files.WriteDotNetPlan(plan with { Scope = "other" });
        Join(2, "wrong-plan", "The original CI plan has a different binding.");

        CiOutcome Join(int exit, string state, string? diagnostic = null)
        {
            var call = Run(exit, "caller-dotnet", "join", root, transfer, files.At("groups"),
                files.At(state), Candidate, Candidate);
            if (diagnostic is not null) Assert.Contains(diagnostic, call.Error);
            return exit == 2 ? null! : call.Read(TransferJson.Default.CiOutcome);
        }
    }

    [TestMethod]
    public void NodeJoinRequiresIndependentRuntimeResults()
    {
        using var files = new State();
        var group = files.PrepareNodeJoin();
        Assert.IsTrue(Join(1).Checks.All(c => c.Status == "missing"));
        var first = NodeResult(group, "node22", CheckStatus.Passed);
        var second = NodeResult(group, "node24", CheckStatus.Passed);
        Put("node22", first);
        Assert.HasCount(2, Join(1).Checks.Where(c => c.Status == "missing"));
        Assert.IsTrue(Join(1).Checks.Where(c => c.Key.Variant == "node22")
            .All(c => c.Status == "passed"));
        Put("node24", second);
        Assert.IsTrue(Join(0).Satisfied);
        Assert.HasCount(4, files.Read("results.json", TransferJson.Default.CheckResultArray));
        Put("node99", first with { Runtime = "node99", Results = [] });
        Join(2);
        File.Delete(files.At("node99-result.json"));
        Put("node24", first);
        Put("node22", second);
        Join(2);
        Put("node24", second);
        Put("node22", first with { Results = [.. first.Results, first.Results[0]] });
        Assert.Contains(first.Results[0].Key.ToString(), Assert.ContainsSingle(Join(1).Errors));
        Put("node22", first);
        foreach (CheckStatus status in new[] { CheckStatus.Failed, CheckStatus.Cancelled,
                     CheckStatus.Skipped, CheckStatus.TimedOut })
        {
            Put("node24", NodeResult(group, "node24", status));
            Assert.AreEqual(status.ToString().ToLowerInvariant(), Join(1).Checks
                .First(c => c.Key.Variant == "node24").Status);
        }

        Put("node22", first with
        { Results = [.. first.Results, first.Results[0] with { Status = CheckStatus.Failed }] });
        Put("node24", second);
        Assert.Contains("conflicting", Join(1).Checks.Select(c => c.Status));
        File.Delete(files.At("node24-result.json"));
        CiPlan changedRunner = group.Adopted with
        {
            Checks = [.. group.Adopted.Checks.Select(check => check.Work.Key.Variant == "node24"
                ? check with { Work = check.Work with { Runner = "other" } } : check)],
        };
        NodeRunResult[] malformed = [first with { Candidate = Foreign },
            first with { OriginalPlan = group.Adopted with { Comparison = Foreign } },
            first with { OriginalPlan = changedRunner },
            first with { Runtime = "node24" }, first with { Runtime = "node99" }];
        foreach (NodeRunResult bad in malformed)
        {
            Put("node22", bad);
            Join(2);
        }
        files.Put("node22-result.json", JsonSerializer.Serialize(first,
            TransferJson.Default.NodeRunResult).Replace("\"Passed\"", "\"unknown\"",
            StringComparison.Ordinal));
        Join(2);
        var invalid = first with
        { Results = [first.Results[0] with { Status = (CheckStatus)999 }] };
        Assert.ThrowsExactly<InvalidDataException>(() => NodeCiCaller.Join(group.Adopted, group,
            [new("node22", invalid)]));
        Assert.ThrowsExactly<InvalidDataException>(() => NodeCiCaller.Join(group.Adopted, group,
            [new("node22", first), new("node22", first)]));
        var wrongRevision = group with { Runtime = group.Runtime with { Revision = Foreign } };
        Put("node22", first);
        files.Put("group-plan.json", wrongRevision, TransferJson.Default.NodeGroupReadback);
        Join(2, "Node runtime join has a different or repeated subject.");
        CiPlan empty = group.Adopted with { Checks = [] };
        files.Put("group-plan.json", group with { Adopted = empty },
            TransferJson.Default.NodeGroupReadback);
        Join(2);
        files.Put("parent-plan.json", empty, TransferJson.Default.CiPlan);
        Join(2);
        File.Delete(files.At("node22-result.json"));
        Assert.IsTrue(Join(0).Satisfied);
        CiPlan selective = empty with { Full = false };
        files.Put("group-plan.json", group with { Adopted = selective },
            TransferJson.Default.NodeGroupReadback);
        files.Put("parent-plan.json", selective, TransferJson.Default.CiPlan);
        Join(2, "The original CI plan has a different binding.");

        void Put(string runtime, NodeRunResult result) =>
            files.Put(runtime + "-result.json", result, TransferJson.Default.NodeRunResult);
        CiOutcome Join(int exit, string? diagnostic = null)
        {
            var call = Run(exit, "caller-node", "join", files.Root);
            if (diagnostic is not null) Assert.Contains(diagnostic, call.Error);
            return exit == 2 ? null! : call.Read(TransferJson.Default.CiOutcome);
        }
    }

    [TestMethod]
    public void NodeRetainedMembersUseAdmittingMajorsNotPackagingPins()
    {
        var group = NodeGroup();
        group = group with
        {
            Retained = [new("src/late", []), new("src/retained", [])],
            Runtime = group.Runtime with
            {
                Variants = [new("node20", "20.x", null), new("node22", "22.x", null),
                    new("node24", "24.x", "24.21.0"), new("node26", "26.x", null)],
                Members = [new("src/late", new Version(26, 0, 0)),
                    new("src/retained", new Version(24, 0, 0))],
            },
        };
        Assert.IsEmpty(NodeCiCaller.RetainedMembers(group, "node20"));
        Assert.IsEmpty(NodeCiCaller.RetainedMembers(group, "node22"));
        CollectionAssert.AreEqual(Retained, NodeCiCaller.RetainedMembers(group, "node24"));
        CollectionAssert.AreEqual(LateAndRetained, NodeCiCaller.RetainedMembers(group, "node26"));
        var moving = group with
        {
            Runtime = group.Runtime with
            {
                Members = [group.Runtime.Members[0] with { LowerBound = new Version(24, 21, 1) },
            group.Runtime.Members[1]]
            }
        };
        CollectionAssert.AreEqual(LateAndRetained, NodeCiCaller.RetainedMembers(moving, "node24"));
        CollectionAssert.AreEqual(LateAndRetained, NodeCiCaller.RetainedMembers(moving, "node26"));
        var unsupported = group with
        {
            Runtime = group.Runtime with
            { Members = [new("src/late", new Version(28, 0, 0)), group.Runtime.Members[1]] }
        };
        Assert.AreEqual("No planned Node runtime satisfies a retained member's engines range.",
            Assert.ThrowsExactly<InvalidDataException>(() =>
                NodeCiCaller.RetainedMembers(unsupported, "node24")).Message);
    }

    [TestMethod]
    public void PythonAzureBundleSelectsOnlyTheActualNativeConsumer()
    {
        PlannedCheck Target(string target) => Check("root", "python/pytest-v1", target,
            new() { ["testPath"] = target });
        PlannedCheck own = Target("tests/private/app/workflow-delivery/Native/Python");
        PlannedCheck azure = Target("src/private/app/azureauth-credprovider/python/tests");
        PlannedCheck svgedit = Target("tests/eng/test_svgedit_renovate.py");
        Assert.IsTrue(PythonCiCaller.AzureBundle(Plan(PythonCiCaller.Scope, own, azure)));
        Assert.IsFalse(PythonCiCaller.AzureBundle(Plan(PythonCiCaller.Scope, svgedit)));
        Assert.IsTrue(PythonCiCaller.RequiresPowerShell(Plan(PythonCiCaller.Scope, svgedit)));
        Assert.IsTrue(PythonCiCaller.RequiresPowerShell(Plan(PythonCiCaller.Scope,
            Target("tests/eng"))));
        Assert.IsFalse(PythonCiCaller.RequiresPowerShell(Plan(PythonCiCaller.Scope,
            Target("tests/eng/test_general_ci_workflow.py"))));
        Assert.IsFalse(PythonCiCaller.AzureBundle(Plan(PythonCiCaller.Scope,
            Target("tests/private/app/workflow-delivery/Native/Ruby"),
            Target("tests/eng/test_general_ci_workflow.py"))));
        Assert.IsFalse(PythonCiCaller.AzureBundle(Plan(PythonCiCaller.Scope)));
    }

    [TestMethod]
    [DataRow((int)CheckStatus.Passed, "passed")]
    [DataRow(null, "missing")]
    [DataRow((int)CheckStatus.Failed, "failed")]
    [DataRow((int)CheckStatus.TimedOut, "timedout")]
    public async Task PythonExecuteRetainsPreparedContextAndReportsOwnedStatus(
        int? checkStatus, string status)
    {
        using var files = new State();
        var check = Check("repository", "python/pytest-v1", "python-native",
            new() { ["testPath"] = "tests/root" });
        var prepared = files.PreparePython(Plan(PythonCiCaller.Scope, check));
        var endpoint = prepared.Checkout;
        var graph = prepared.Collection.Graph;
        string tail = new string('x', NativeDiagnostic.TailLength - 18) + "controlled stderr";
        const string failure = "Shared uv sync preparation failed: Exited, exit 2. " +
            "stderr: error: controlled lock mismatch";
        var observed = (CheckStatus?)checkStatus;
        bool timedOut = observed == CheckStatus.TimedOut;
        bool failed = observed == CheckStatus.Failed;
        using var diagnostics = new StringWriter();
        var outcome = await PythonCiCaller.ExecuteAsync(files.Root, null, context.CancellationToken,
            (plan, request, _) =>
            {
                Assert.AreEqual(check.Work.Key, Assert.ContainsSingle(plan.Checks).Work.Key);
                Assert.AreEqual(endpoint.Directory, request.Native.Checkout);
                Assert.AreEqual(graph.Uv, request.Native.Uv);
                Assert.AreEqual(graph.Interpreter, request.Native.Interpreter);
                Assert.AreEqual("native-root", Assert.ContainsSingle(request.Native.Members).Name);
                Assert.AreEqual("prepared cache", request.Native.Environment["UV_CACHE_DIR"]);
                Assert.IsNull(request.Native.Environment["PYTHONSAFEPATH"]);
                Assert.AreEqual("packages.py", request.PackageHelper);
                var key = check.Work.Key;
                CheckResult[] results = observed is { } reported ?
                    [new(Candidate, key, reported)] : [];
                var sync = new NativeCommandResult(
                    timedOut ? NativeTermination.TimedOut : NativeTermination.Exited,
                    timedOut ? null : 2, "",
                    timedOut ? "omitted head " + tail : "error: controlled lock mismatch\n",
                    timedOut ? 30 : 0.1, timedOut ? "Command timed out." : null);
                return Task.FromResult(new PythonGroupRunResult(Candidate, results,
                    new(Candidate, results, failed || timedOut ?
                        [new([key], new(graph.Uv, endpoint.Directory, ["sync", "--frozen"], 30),
                            sync)] : [], failed ? [new(key, failure)] : []),
                    new(Candidate, [], [], [], []), ResultCollector.Collect(plan, results)));
            }, diagnostics);
        Assert.AreEqual(observed == CheckStatus.Passed, outcome.Satisfied);
        Assert.AreEqual(status, Assert.ContainsSingle(outcome.Checks).Status);
        Assert.AreEqual("1", prepared.Environment["PYTHONSAFEPATH"]);
        Assert.IsTrue(File.Exists(files.At("execution-request.json")));
        Assert.IsTrue(File.Exists(files.At("result.json")));
        if (failed || timedOut)
            Assert.AreEqual(Lines(
                $"Check {status} (required): repository python/pytest-v1 python-native",
                "  | " + (failed ? failure :
                    "Native command uv-shim sync failed: TimedOut, exit unknown. " +
                    "Command timed out. stderr: [truncated] " + tail)), diagnostics.ToString());
        files.ReplacePythonPreparation(prepared,
            prepared with { Checkout = endpoint with { Reference = Foreign } });
        Assert.Contains("Prepared Python candidate differs", Run(2, "caller-python", "execute",
            files.Root, "-").Error);
    }

    [TestMethod]
    [DataRow("passed", false)]
    [DataRow("missing", false)]
    [DataRow("failed", false)]
    [DataRow("failed", true)]
    public async Task RubyExecuteRetainsPreparedContextAndOriginalNativeResult(
        string status, bool loadError)
    {
        using var files = new State();
        var check = Check("src/ruby", RubyProjectChecks.RSpec, "default",
            preset: RubyProjectChecks.Preset);
        var (prepared, tools) = files.PrepareRuby(Plan(RubyCiCaller.Scope, check));
        var endpoint = prepared.Checkout;
        const string reportJson = """
            {"version":"3.13.6","messages":["No examples found."],"examples":[],
             "summary":{"example_count":0,"failure_count":0,"pending_count":0,
             "errors_outside_of_examples_count":1}}
            """;
        string tail = new string('x', NativeDiagnostic.TailLength - 23) + " controlled rspec error";
        string stdout = "omitted native output " + new string('x', NativeDiagnostic.TailLength) +
            "\nLoadError: cannot load such file -- logger\n::error::not a workflow command" +
            "\n##[error]also not a command";
        using var diagnostics = new StringWriter();
        var outcome = await RubyCiCaller.ExecuteAsync(files.Root, context.CancellationToken,
            async (received, request, token) =>
            {
                var key = Assert.ContainsSingle(received.Checks).Work.Key;
                Assert.AreEqual(check.Work.Key, key);
                Assert.AreEqual(endpoint.Directory, request.Checkout);
                Assert.AreEqual(tools.Bundle, request.Bundle);
                Assert.AreEqual(files.At("prepared-gems"), request.Facts.Environment["GEM_HOME"]);
                Assert.AreEqual(Candidate, request.Candidate.Facts.Revision.Commit);
                RubyRSpecRun? run = null;
                if (loadError)
                    run = await RubyRSpecExecution.RunAsync(key,
                        new(endpoint.Directory, tools.Ruby, tools.Bundle, "2.7.2",
                            files.At("candidate/Gemfile"), files.Root, 900,
                            new Dictionary<string, string?>()),
                        (command, _) =>
                        {
                            File.WriteAllText(command.Arguments[^1], reportJson);
                            return Task.FromResult(new NativeCommandResult(NativeTermination.Exited,
                                1, stdout, "", 2, null));
                        }, token);
                else if (status == "failed")
                    run = new(new(tools.Ruby, endpoint.Directory,
                        [tools.Bundle, "_2.7.2_", "exec", "rspec"], 900),
                        new(NativeTermination.Exited, 1, "", "omitted rspec head " + tail,
                            2, null), null, CheckStatus.Failed, null);
                CheckResult[] results = status == "missing" ? [] :
                    [new(Candidate, key, run?.Status ?? CheckStatus.Passed)];
                return new RubyGroupRun(results, run is null ? [] : [new(key, run, [])],
                    new([], [], [], []), ResultCollector.Collect(received, results));
            }, diagnostics);
        Assert.AreEqual(status == "passed", outcome.Satisfied);
        Assert.AreEqual(status, Assert.ContainsSingle(outcome.Checks).Status);
        var retained = files.Read("result.json", TransferJson.Default.RubyGroupRun);
        Assert.AreEqual(JsonSerializer.Serialize(outcome, TransferJson.Default.CiOutcome),
            File.ReadAllText(files.At("outcome.json")).Trim());
        string log = diagnostics.ToString();
        if (loadError)
        {
            Assert.Contains("Exited, exit 1.", log);
            Assert.Contains("stdout: [truncated]", log);
            Assert.Contains("LoadError: cannot load such file -- logger", log);
            Assert.DoesNotContain("omitted native output", log);
            Assert.Contains("  | ::error::not a workflow command", log);
            Assert.Contains(@"  | #\#[error]also not a command", log);
            CiCallerTransferTests.AssertNoWorkflowCommand(log);
            Assert.IsLessThan(NativeDiagnostic.TailLength + 512, log.Length);
            var original = Assert.ContainsSingle(retained.RSpec).Run;
            Assert.AreEqual(stdout, original.Result.Stdout);
            Assert.IsEmpty(original.Result.Stderr);
            Assert.IsNull(original.ReportError);
            Assert.IsNotNull(original.Report);
            Assert.AreEqual(1, original.Report.ErrorsOutsideExamples);
            Assert.AreEqual(JsonSerializer.Serialize(RubyRSpecResult.Read(reportJson).Original),
                JsonSerializer.Serialize(original.Report.Original));
        }
        else if (status == "failed")
            Assert.AreEqual(Lines(
                $"Check failed (required): src/ruby {RubyProjectChecks.RSpec} default",
                "  | Native command ruby-shim _2.7.2_ failed: Exited, exit 1. " +
                "stderr: [truncated] " + tail), log);
        files.ReplaceRubyPreparation(prepared,
            prepared with { Checkout = endpoint with { Reference = Foreign } });
        Assert.Contains("Prepared Ruby candidate differs",
            Run(2, "caller-ruby", "execute", files.Root).Error);
    }

    [TestMethod]
    public void ReportNeutralizesWorkflowCommandsInBothHeadersAndNativeText()
    {
        var key = new CheckKey("src/ruby/##[add-mask]native", RubyProjectChecks.RSpec, "default");
        using var log = new StringWriter();
        CiCallerTransfer.Report(new("a", "a", "scope", false, [new(key, true, "failed")], []),
            [new(key, CiCallerTransferTests.WorkflowCommandText)], [], log);
        CiCallerTransferTests.AssertNoWorkflowCommand(log.ToString());
        Assert.AreEqual(Lines($"Check failed (required): src/ruby/#\\#[add-mask]native " +
            $"{RubyProjectChecks.RSpec} default", "  | native failure", "  | ::add-mask::x",
            "  |   #\\#[add-mask]x", "  | warning #\\#[error]inline"), log.ToString());
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task ActionsOutputArrayIsOneNativeJsonLineAndRejectsMultilineScalars(bool empty)
    {
        using var files = new State();
        string path = files.At("outputs");
        string[] expected = empty ? [] : DifficultDirectories;
        var environment = TestProcess.Environment();
        environment["GITHUB_OUTPUT"] = path;
        var child = await NativeProcess.ExecuteAsync(new(TestProcess.Executable, files.Root,
            [ActionsOutputSwitch, JsonSerializer.Serialize(expected,
                TransferJson.Default.StringArray)], 30, environment), context.CancellationToken);

        Assert.AreEqual(NativeTermination.Exited, child.Termination);
        Assert.AreEqual(0, child.ExitCode, child.Stderr);
        string line = Assert.ContainsSingle(File.ReadAllLines(path));
        const string prefix = "retained_directories=";
        Assert.StartsWith(prefix, line);
        CollectionAssert.AreEqual(expected, JsonSerializer.Deserialize(line[prefix.Length..],
            TransferJson.Default.StringArray));
    }

    private static PlannedCheck DotNetCheck(string target = "src/project.csproj",
        string tools = "{}") =>
        Check(target, "dotnet/build-v1", "Debug", new() { [DotNetTools.Dimension] = tools },
            runner: "windows-latest");

    private static string Lines(params string[] lines) =>
        string.Join(Environment.NewLine, lines) + Environment.NewLine;
}
