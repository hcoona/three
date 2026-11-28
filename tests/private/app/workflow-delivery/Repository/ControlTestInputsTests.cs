using System.Text.Json;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using WorkflowDelivery.CI;
using WorkflowDelivery.Platform;
using WorkflowDelivery.Repository;
using GitFixture = WorkflowDelivery.Tests.Repository.GitReaderTests.GitFixture;

namespace WorkflowDelivery.Tests.Repository;

[TestClass]
public sealed class ControlTestInputsTests(TestContext context)
{
    private const string Resource = "inputs/Version 雪.json";

    private static GitFixture? shared;
    private static ControlTestInputRequest? sharedRequest;

    [ClassInitialize]
    public static async Task InitializeAsync(TestContext classContext)
    {
        GitFixture repo = await GitFixture.CreateAsync(classContext.CancellationToken);
        try
        {
            (string basis, string candidate) = await CommitPairAsync(
                repo, Project(Resource), "100644");
            sharedRequest = new(new(basis, candidate, false), repo.Directory,
                await MaterializeAsync(repo, "basis", basis),
                await MaterializeAsync(repo, "candidate", candidate));
            shared = repo;
        }
        catch { repo.Dispose(); throw; }
    }

    [ClassCleanup]
    public static void Cleanup() => shared?.Dispose();

    [TestMethod]
    [DataRow("control/Program.cs", true)]
    [DataRow("shared/runtime.dll", true)]
    [DataRow("control/Native/facts.rb", true)]
    [DataRow("Directory.Build.targets", false)]
    public async Task EvaluatedControlInputsFlowToNodeSelection(string path, bool committed)
    {
        DotNetRevisionInputs evaluated = EvaluatedControl();
        NodeRevisionInputs candidate = NodeScenario.Inputs(NodeScenario.Candidate,
            NodeScenario.Project("src/a"));
        NodeRevisionInputs basis = NodeScenario.Inputs(NodeScenario.Basis,
            NodeScenario.Project("src/a"));
        candidate = candidate with
        {
            Revision = candidate.Revision with
            { Entries = [.. candidate.Revision.Entries, .. evaluated.Revision.Entries] },
        };
        if (!committed)
            basis = basis with
            {
                Revision = basis.Revision with
                { Entries = [.. basis.Revision.Entries, NodeScenario.File(path)] },
            };
        var comparison = new CiComparison(NodeScenario.Basis, NodeScenario.Candidate, false);
        var scope = new ScopeInputs(comparison, [path],
            [new(path, !committed, committed ? null : "100644", committed ? [] :
                [new("dotnet", path, "native-control", [path])])],
            [new(path, committed, committed ? "100644" : null, [])]);

        candidate = NodeGroupReader.WithControlInputs(candidate,
            ControlInputs.Read(evaluated, true), true);
        NodeGroupPlan plan = await NodeGroupPlanning.PlanAsync(basis, candidate, scope, "src/a",
            (project, _) => Task.FromResult(NodeScenario.Scripts(project)),
            (_, _) => Task.FromResult("1.2.3"), context.CancellationToken);

        Assert.IsNotEmpty(plan.Adopted.Checks);
        foreach (PlannedCheck check in plan.Adopted.Checks)
            Assert.AreEqual(new SelectionReason(path, NodeScenario.Candidate, "src/a"),
                Assert.ContainsSingle(check.Reasons));
    }

    [TestMethod]
    [DataRow(0)]
    [DataRow(2)]
    public void EvaluatedControlRequiresOneInnerNodeUnlessAbsentAndOptional(int count)
    {
        DotNetRevisionInputs evaluated = EvaluatedControl();
        DotNetBoundNode inner = Assert.ContainsSingle(evaluated.Nodes);
        evaluated = evaluated with { Nodes = Enumerable.Repeat(inner, count).ToArray() };

        InvalidDataException failure = Assert.ThrowsExactly<InvalidDataException>(() =>
            ControlInputs.Read(evaluated, true));
        Assert.Contains("The control requires one complete native dimension.", failure.Message);
        if (count == 0)
            Assert.IsNull(ControlInputs.Read(evaluated, false));
        else
        {
            failure = Assert.ThrowsExactly<InvalidDataException>(() =>
                ControlInputs.Read(evaluated, false));
            Assert.Contains("The control requires one complete native dimension.", failure.Message);
        }
    }

    [TestMethod]
    public void NativeControlProjectReferencesRequireAnAdapter()
    {
        DotNetRevisionInputs evaluated = EvaluatedControl();
        DotNetGraphNode control = Assert.ContainsSingle(evaluated.Nodes).Native;
        evaluated = evaluated with
        {
            Graph = evaluated.Graph with
            {
                Edges = [new(control.Identity, new(Path.GetFullPath("other.csproj",
                    evaluated.Graph.Root), control.Identity.Globals))],
            },
        };

        InvalidDataException failure = Assert.ThrowsExactly<InvalidDataException>(() =>
            ControlInputs.Read(evaluated, true));

        Assert.Contains("dependencies require a concrete adapter", failure.Message);
    }

    [TestMethod]
    [DataRow(true)]
    [DataRow(false)]
    public void CoreInputsFollowTheTransitiveEvaluatedProjectReferences(bool referenced)
    {
        const string coreRoot = "src/private/app/workflow-delivery/Core/";
        const string coreProject = coreRoot + "WorkflowDelivery.Core.csproj";
        const string engineProject =
            "src/private/app/workflow-delivery/Engine/WorkflowDelivery.Engine.csproj";
        const string source = coreRoot + "ReleaseDecisions.cs";
        const string restoreLock = coreRoot + "packages.lock.json";
        const string configuration = coreRoot + "version.json";
        DotNetRevisionInputs inputs = EvaluatedControl();
        DotNetGraphNode cli = Assert.ContainsSingle(inputs.Nodes).Native;
        string PathOf(string path) => Path.GetFullPath(path, inputs.Graph.Root);
        var engine = cli with { Identity = new(PathOf(engineProject), cli.Identity.Globals) };
        var core = cli with { Identity = new(PathOf(coreProject), cli.Identity.Globals) };
        var alternate = core with
        {
            Identity = core.Identity with
            { Globals = new(core.Identity.Globals) { ["Flavor"] = "B" } },
            VersionBaseDirectory = PathOf("other"),
        };
        var unrelatedEdge = new DotNetGraphEdge(alternate.Identity, new(PathOf("other.csproj"),
            cli.Identity.Globals));
        inputs = inputs with
        {
            Revision = inputs.Revision with
            {
                Entries = [.. inputs.Revision.Entries,
                    NodeScenario.File(engineProject), NodeScenario.File(coreProject),
                    NodeScenario.File(source), NodeScenario.File(restoreLock)],
            },
            Graph = inputs.Graph with
            {
                Edges = referenced
                    ? [new(cli.Identity, engine.Identity), new(engine.Identity, core.Identity),
                        unrelatedEdge] : [new(engine.Identity, core.Identity), unrelatedEdge],
            },
            Nodes = [.. inputs.Nodes, new(engineProject, engine, [engineProject]),
                new(coreProject, core, [source]), new(coreProject, alternate, [coreRoot + "B.cs"])],
            Inputs = [.. inputs.Inputs, new(restoreLock, new(PathOf(restoreLock),
                "RestoreLock", "LockedRestore", core.Identity)), new(coreRoot + "b.lock",
                new(PathOf(coreRoot + "b.lock"), "RestoreLock", "LockedRestore",
                    alternate.Identity))],
            SelectionInputs = [.. inputs.SelectionInputs, new(configuration,
                [coreProject]), new(coreRoot + "alternate.json", [coreProject])],
            Versions = [new(inputs.Revision.Commit, "other", [coreRoot + "alternate.json"], [])],
        };

        ControlSourceInput[] coreInputs = ControlInputs.Read(inputs, true)!.Sources
            .Where(input => input.Path.StartsWith(coreRoot, StringComparison.Ordinal)).ToArray();
        ControlSourceInput[] expected = referenced
            ? [new(source, "ControlSource", true), new(restoreLock, "RestoreLock", true),
                new(configuration, "ControlConfiguration", false)]
            : [];
        CollectionAssert.AreEquivalent(expected, coreInputs);
    }

    private static DotNetRevisionInputs EvaluatedControl()
    {
        string project = NodeGroupReader.ControlProject;
        string root = Path.Combine(Path.GetTempPath(), "control-endpoint");
        var identity = new DotNetNodeIdentity(Path.GetFullPath(project, root), new()
        {
            ["Configuration"] = "Debug",
            ["ContinuousIntegrationBuild"] = "true",
            ["RestoreLockedMode"] = "true",
        });
        var node = new DotNetGraphNode(identity, false, new("Debug", "net10.0"), "", "", [],
            "None");
        string[] paths = [project, "control/Program.cs", "shared/runtime.dll",
            "control/Native/facts.rb"];
        var revision = new GitRevision(NodeScenario.Candidate, paths.Select(
            NodeScenario.File).ToArray());
        return new(revision,
            new(revision.Commit, root, Path.Combine(root, "dirs.proj"), [], [], []),
            [new(project, node, [project, "control/Program.cs", "control/Native/facts.rb"])],
            [new("shared/runtime.dll", new(Path.GetFullPath("shared/runtime.dll", root),
                "ReferenceHintPath", "Evaluation", identity))],
            [], new Dictionary<string, QualitySelection?>(),
            [new SharedInput("Directory.Build.targets", [project])], []);
    }

    [TestMethod]
    public async Task NativeReceiptKeepsBothRevisionsAndRemovedAddedResources()
    {
        var repo = await GitFixture.CreateAsync(context.CancellationToken);
        await FixtureCleanup.RunAsync(async () =>
        {
            await repo.SetAsync(ControlTestInputs.Project, Project(Resource));
            await repo.SetAsync(Resource, "before");
            string basis = await repo.CommitAsync();
            await repo.RemoveAsync(Resource);
            await repo.SetAsync("inputs/moved.json", "after");
            await repo.SetAsync(ControlTestInputs.Project, Project("inputs/moved.json"));
            string candidate = await repo.CommitAsync(basis);
            var request = new ControlTestInputRequest(new(basis, candidate, false), repo.Directory,
                await MaterializeAsync(repo, "basis", basis),
                await MaterializeAsync(repo, "candidate", candidate));

            ControlTestInputResponse result = await ControlTestInputs.ReadAsync(request,
                context.CancellationToken);

            Assert.AreEqual(request.Comparison, result.Comparison);
            Assert.AreEqual(basis, result.Basis.Revision);
            Assert.AreEqual(candidate, result.Candidate.Revision);
            Assert.AreEqual(ControlTestInputs.Project, result.Basis.Project);
            Assert.IsTrue(result.Basis.Present);
            Assert.IsTrue(result.Candidate.Present);
            Assert.AreEqual(new("Debug", "net10.0"), result.Candidate.Dimension);
            Assert.AreEqual(Resource, Assert.ContainsSingle(result.Basis.Inputs));
            Assert.AreEqual("inputs/moved.json", Assert.ContainsSingle(result.Candidate.Inputs));
        }, repo.Dispose);
    }

    [TestMethod]
    public async Task NativeReceiptDistinguishesConfirmedBaseAbsence()
    {
        using var repo = await GitFixture.CreateAsync(context.CancellationToken);
        string basis = await repo.CommitAsync();
        await repo.SetAsync(ControlTestInputs.Project, Project(Resource));
        await repo.SetAsync(Resource);
        string candidate = await repo.CommitAsync(basis);
        var request = new ControlTestInputRequest(new(basis, candidate, false), repo.Directory,
            null, await MaterializeAsync(repo, "candidate", candidate));

        ControlTestInputResponse result = await ControlTestInputs.ReadAsync(request,
            context.CancellationToken);

        Assert.AreEqual(basis, result.Basis.Revision);
        Assert.AreEqual(ControlTestInputs.Project, result.Basis.Project);
        Assert.IsFalse(result.Basis.Present);
        Assert.IsNull(result.Basis.Dimension);
        Assert.IsEmpty(result.Basis.Inputs);
        Assert.AreEqual(Resource, Assert.ContainsSingle(result.Candidate.Inputs));
    }

    [TestMethod]
    public async Task NativeReceiptRetainsCoherentExplicitFullAndCallerPreparation()
    {
        var repo = await GitFixture.CreateAsync(context.CancellationToken);
        await FixtureCleanup.RunAsync(async () =>
        {
            await repo.SetAsync(ControlTestInputs.Project, Project(Resource));
            await repo.SetAsync(Resource);
            string candidate = await repo.CommitAsync();
            string parent = Path.GetDirectoryName(repo.Directory)!;
            string stateDir = Path.Combine(parent, "control-state");
            string eventPath = Path.Combine(parent, "event.json");
            File.WriteAllText(Path.Combine(parent, "global.json"),
                RepositoryToolSelection.GlobalJson());
            File.WriteAllText(eventPath, "{}");
            using var output = new StringWriter();
            using var error = new StringWriter();

            int code = Program.Run(
                ["ci", "caller-control", "prepare", repo.Directory, stateDir, "workflow_dispatch",
                    eventPath, candidate], output, error);

            Assert.AreEqual(0, code, error.ToString());
            Assert.IsEmpty(error.ToString());
            var prepared = JsonSerializer.Deserialize(output.ToString(),
                TransferJson.Default.ControlTestInputRequest);
            Assert.IsNotNull(prepared);
            ControlTestInputResponse result = await ControlTestInputs.ReadAsync(prepared,
                context.CancellationToken);
            Assert.AreEqual(new(candidate, candidate, true), result.Comparison);
            Assert.Contains(Resource, result.Basis.Inputs);
            CollectionAssert.AreEqual(result.Basis.Inputs, result.Candidate.Inputs);
            Assert.IsTrue(result.Basis.Present);
        }, repo.Dispose);
    }

    [TestMethod]
    public async Task SharedComparisonQualifiesNativeReceipt()
    {
        ControlTestInputResponse result = await ControlTestInputs.ReadAsync(sharedRequest!,
            context.CancellationToken);
        Assert.AreEqual(sharedRequest!.Comparison, result.Comparison);
        Assert.AreEqual(Resource, Assert.ContainsSingle(result.Candidate.Inputs));
    }

    [TestMethod]
    [DataRow(null)]
    [DataRow("120000")]
    public async Task CandidateMustCommitARegularControlProject(string? candidateMode)
    {
        using var repo = await GitFixture.CreateAsync(context.CancellationToken);
        (string basis, string candidate) = await CommitPairAsync(repo,
            Project(Resource), candidateMode);
        var request = new ControlTestInputRequest(new(basis, candidate, false), repo.Directory,
            await MaterializeAsync(repo, "basis", basis), candidateMode == "120000"
                ? repo.Directory : await MaterializeAsync(repo, "candidate", candidate));

        var exception = await Assert.ThrowsExactlyAsync<InvalidDataException>(() =>
            ControlTestInputs.ReadAsync(request, context.CancellationToken));

        Assert.Contains("The control test owner must be a committed regular project.",
            exception.Message);
    }

    [TestMethod]
    public async Task ConfirmedBasisPresenceRequiresItsOwnMaterialization() =>
        await Assert.ThrowsExactlyAsync<InvalidDataException>(() => ControlTestInputs.ReadAsync(
            sharedRequest! with { BasisDirectory = null }, context.CancellationToken));

    [TestMethod]
    public async Task BasisCheckoutCannotSatisfyTheCandidateReceipt()
    {
        ControlTestInputRequest request = sharedRequest!;

        await Assert.ThrowsExactlyAsync<InvalidDataException>(() => ControlTestInputs.ReadAsync(
            request with { CandidateDirectory = request.BasisDirectory! },
            context.CancellationToken));
    }

    [TestMethod]
    public async Task InputReceiptRequiresExactComparisonRevisions()
    {
        ControlTestInputRequest request = sharedRequest!;

        await Assert.ThrowsExactlyAsync<InvalidDataException>(() => ControlTestInputs.ReadAsync(
            request with { Comparison = request.Comparison with { Candidate = "HEAD" } },
            context.CancellationToken));
    }

    [TestMethod]
    public async Task FailedBasisEvaluationCannotBeReportedAsAbsence()
    {
        using var repo = await GitFixture.CreateAsync(context.CancellationToken);
        (string basis, string candidate) = await CommitPairAsync(repo,
            "<Project><Import Project=\"unavailable.props\" /></Project>", "100644");
        var request = new ControlTestInputRequest(new(basis, candidate, false), repo.Directory,
            await MaterializeAsync(repo, "basis", basis),
            await MaterializeAsync(repo, "candidate", candidate));

        var exception = await Assert.ThrowsExactlyAsync<InvalidDataException>(() =>
            ControlTestInputs.ReadAsync(request, context.CancellationToken));

        Assert.Contains("Native query (dotnet msbuild) failed: Exited, exit 1.", exception.Message);
        Assert.Contains("unavailable.props", exception.Message);
    }

    private static async Task<(string Basis, string Candidate)> CommitPairAsync(GitFixture repo,
        string basisProject, string? candidateMode)
    {
        await repo.SetAsync(ControlTestInputs.Project, basisProject);
        await repo.SetAsync(Resource);
        string basis = await repo.CommitAsync();
        await repo.SetAsync("other.txt");
        if (candidateMode is null) await repo.RemoveAsync(ControlTestInputs.Project);
        else await repo.SetAsync(ControlTestInputs.Project,
            candidateMode == "120000" ? "other.txt" : Project(Resource), candidateMode);
        return (basis, await repo.CommitAsync(basis));
    }

    private static string Project(string input) => $$"""
        <Project>
          <PropertyGroup><TargetFramework>net10.0</TargetFramework></PropertyGroup>
          <ItemGroup Condition="'$(Configuration)' == 'Debug'">
            <EmbeddedResource Include="../../../../{{input}}" />
          </ItemGroup>
        </Project>
        """;

    private static async Task<string> MaterializeAsync(GitFixture repo, string name,
        string revision)
    {
        string parent = Path.GetDirectoryName(repo.Directory)!;
        File.WriteAllText(Path.Combine(parent, "global.json"),
            RepositoryToolSelection.GlobalJson());
        string path = Path.Combine(parent, name);
        await repo.GitAsync("worktree", "add", "--detach", path, revision);
        return path;
    }
}
