using WorkflowDelivery.CI;
using WorkflowDelivery.Repository;

namespace WorkflowDelivery.Tests.Repository;

[TestClass]
public sealed class DotNetGroupReaderTests(TestContext context)
{
    private const string Fixture = "tests/control/fixtures/node";
    private const string Parent = "tests/control/control.csproj";
    private static readonly string[] NativeOwners = ["node", "dotnet"];
    private static readonly string[] NativeProjects = ["product/A.csproj", "product/B.csproj"];

    [TestMethod]
    public void NodeOnlyFixtureDoesNotSelectExcludedDotNetParent()
    {
        var scenario = new GroupScenario(Fixture + "/index.js");
        ScopeInputs joined = scenario.Join();
        ScopeResponsibility reason = Assert.ContainsSingle(joined.Basis[0].Reasons);
        Assert.AreEqual("node", reason.Owner);
        Assert.AreEqual(Fixture, reason.Target);
        Assert.Contains(Fixture + "/package.json", reason.Sources);
        PlanRequest request = joined.Bind(scenario.DotNetBasis, scenario.DotNetCandidate);
        Assert.IsEmpty(ImpactPlanner.SelectProjects(request));
        Assert.Contains(Fixture + "/index.js", request.Basis.UnaffectedPaths);
    }

    [TestMethod]
    public void EmbeddedVersionKeepsBothNativeConsumers()
    {
        string path = Fixture + "/version.json";
        var scenario = new GroupScenario(path);
        scenario.DotNetBasis = scenario.DotNetBasis with { SharedInputs = [new(path, [Parent])] };
        scenario.DotNetCandidate = scenario.DotNetCandidate with
        {
            SharedInputs = [new(path,
            [Parent])]
        };
        ScopeInputs joined = scenario.Join();
        foreach (ScopeCoordinate row in joined.Basis.Concat(joined.Candidate))
            CollectionAssert.AreEquivalent(NativeOwners,
                row.Reasons.Select(reason => reason.Owner).ToArray());
        var selected = ImpactPlanner.SelectProjects(joined.Bind(scenario.DotNetBasis,
            scenario.DotNetCandidate));
        CollectionAssert.AreEquivalent(new[] {
            new SelectionReason(path, NodeScenario.Basis, Parent),
            new SelectionReason(path, NodeScenario.Candidate, Parent)
        }, selected[Parent].ToArray());
    }

    [TestMethod]
    public void MovedAndDeletedNodeOwnershipRetainsBasis()
    {
        string oldPath = Fixture + "/index.js";
        const string newDirectory = "src/moved-node";
        string newPath = newDirectory + "/index.js";
        var scenario = new GroupScenario(oldPath, newPath);
        scenario.Native = scenario.Native with
        {
            Basis = scenario.Native.Basis with
            {
                Entries = scenario.Native.Basis.Entries
                .Where(entry => entry.Path != newPath).ToArray()
            },
            Candidate = scenario.Native.Candidate with
            {
                Entries = scenario.Native.Candidate.Entries
                .Where(entry => !entry.Path.StartsWith(Fixture + "/", StringComparison.Ordinal))
                .Concat([NodeScenario.File(newDirectory + "/package.json")]).ToArray()
            },
        };
        scenario.Scope = scenario.Coordinates();
        scenario.NodeBasis = scenario.NodeBasis with
        {
            Projects = [scenario.NodeBasis.Projects[0]
            with { Id = "npm:stable" }]
        };
        scenario.NodeCandidate = scenario.NodeCandidate with
        {
            Projects = [scenario.NodeCandidate.Projects[0]
            with { Id = "npm:stable", Directory = newDirectory }]
        };
        ScopeInputs joined = scenario.Join();
        Assert.AreEqual("npm:stable", Assert.ContainsSingle(joined.Basis.Single(row =>
            row.Path == oldPath).Reasons).Target);
        Assert.IsEmpty(joined.Candidate.Single(row => row.Path == oldPath).Reasons);
        Assert.IsEmpty(joined.Basis.Single(row => row.Path == newPath).Reasons);
        ScopeResponsibility candidate = Assert.ContainsSingle(joined.Candidate.Single(row =>
            row.Path == newPath).Reasons);
        Assert.Contains(newDirectory + "/package.json", candidate.Sources);
        Assert.IsEmpty(ImpactPlanner.SelectProjects(joined.Bind(scenario.DotNetBasis,
            scenario.DotNetCandidate)));
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public void UnknownEndpointFailsEvenFull(bool full)
    {
        var scenario = new GroupScenario(Fixture + "/index.js");
        if (full)
        {
            scenario.Native = scenario.Native with
            { Candidate = scenario.Native.Candidate with { Commit = NodeScenario.Basis } };
            scenario.DotNetCandidate = scenario.DotNetCandidate with
            {
                Revision =
                NodeScenario.Basis
            };
            scenario.NodeCandidate = scenario.NodeCandidate with { Revision = NodeScenario.Basis };
            scenario.Scope = scenario.Coordinates() with
            { Comparison = new(NodeScenario.Basis, NodeScenario.Basis, true) };
        }
        scenario.NodeBasis = scenario.NodeBasis with { Projects = [] };
        ScopeInputs joined = scenario.Join();
        Assert.IsNotEmpty(joined.Candidate[0].Reasons);
        InvalidDataException error = Assert.ThrowsExactly<InvalidDataException>(() =>
            joined.Bind(scenario.DotNetBasis, scenario.DotNetCandidate));
        Assert.Contains("Unresolved endpoint responsibility", error.Message);
        Assert.Contains(Fixture + "/index.js", error.Message);
    }

    [TestMethod]
    public void JoinedOriginMustBeCommitted()
    {
        var scenario = new GroupScenario(Fixture + "/index.js");
        scenario.Native = scenario.Native with
        {
            Basis = scenario.Native.Basis with
            {
                Entries = scenario.Native.Basis.Entries.Where(entry =>
                entry.Path != Fixture + "/package.json").ToArray()
            }
        };
        Assert.Contains("committed source", Assert.ThrowsExactly<InvalidDataException>(() =>
            scenario.Join()).Message);
    }

    [TestMethod]
    public void OutsideOwnersRemainAndNativeRelationErrorsCannotBeIgnored()
    {
        var scenario = new GroupScenario(Fixture + "/index.js");
        var outside = new ScopeResponsibility("record-system", "docs", "record-binding", [Parent]);
        scenario.Scope = scenario.Scope with
        {
            Basis = [scenario.Scope.Basis[0] with
        { Reasons = [outside] }]
        };
        Assert.AreSame(outside, scenario.Join().Basis[0].Reasons[0]);
        scenario.NodeBasis = scenario.NodeBasis with { Errors = ["native node read failed"] };
        Assert.Contains("native node read failed", Assert.ThrowsExactly<InvalidDataException>(() =>
            scenario.Join()).Message);
    }

    [TestMethod]
    public async Task BoundSelectionPreservesNativeVariantsAndKnownOutsideChanges()
    {
        using var fixture = await DotNetEndpointFixture.CreateAsync(context.CancellationToken);
        DotNetRevisionInputs native = await fixture.BindAsync();
        RepositoryFacts facts = DotNetFactsAssembler.Assemble(native, []) with
        { UnaffectedPaths = ["docs/guide.md"] };
        var request = new PlanRequest(facts, facts, ["linked/data.txt", "docs/guide.md"], false);
        DotNetSelection selected = DotNetFactsAssembler.Select(request, native);
        Assert.AreSame(request, selected.Request);
        CiPlan plan = DotNetFactsAssembler.Complete(selected, context.CancellationToken);
        Assert.HasCount(2, plan.Checks);
        foreach (PlannedCheck item in plan.Checks)
        {
            Assert.AreEqual("product/A.csproj", item.Work.Key.Target);
            Assert.AreEqual(CheckOrigin.NativeRetained, Assert.ContainsSingle(item.Origins));
            Assert.AreEqual("linked/data.txt", Assert.ContainsSingle(item.Reasons).Path);
        }
    }

    [TestMethod]
    public async Task ControlInputsReachAllActualDotNetConsumers()
    {
        using var fixture = await DotNetEndpointFixture.CreateAsync(context.CancellationToken);
        const string source = "control/Source.cs";
        const string caller = "eng/scripts/run_dotnet_ci_group.py";
        await fixture.Repo.SetAsync(NodeGroupReader.ControlProject, "<Project />");
        await fixture.Repo.SetAsync(source);
        await fixture.Repo.SetAsync(caller);
        await fixture.CommitAsync();
        fixture.Graph = fixture.Response();
        DotNetRevisionInputs inputs = await fixture.BindAsync();
        var operation = new ControlBuildContext(inputs.Revision.Commit,
            fixture.PathOf(NodeGroupReader.ControlProject), new("Debug", "net10.0"),
            "10.0.401", "", "", "", "", "", "", "", "", [], [],
            new Dictionary<string, string>());
        var control = new ControlBuildConsumption(operation,
            [new(source, "Sources", true)], [], []);
        DotNetRevisionInputs joined = DotNetGroupReader.WithControlInputs(inputs, control, true);
        RepositoryFacts facts = DotNetFactsAssembler.Assemble(joined, []);
        foreach (string path in new[] { source, caller })
        {
            CollectionAssert.AreEquivalent(NativeProjects,
                ImpactPlanner.DirectConsumers(facts, path));
        }
        Assert.ThrowsExactly<InvalidDataException>(() =>
            DotNetGroupReader.WithControlInputs(inputs, null, true));
        Assert.ThrowsExactly<InvalidDataException>(() => DotNetGroupReader.WithControlInputs(inputs,
            control with { Operation = operation with { Revision = NodeScenario.Basis } }, true));
    }

    private sealed class GroupScenario
    {
        internal GitComparison Native { get; set; }
        internal ScopeInputs Scope { get; set; }
        internal RepositoryFacts DotNetBasis { get; set; }
        internal RepositoryFacts DotNetCandidate { get; set; }
        internal RepositoryFacts NodeBasis { get; set; }
        internal RepositoryFacts NodeCandidate { get; set; }

        internal GroupScenario(params string[] changed)
        {
            GitEntry[] files = new[] { Parent, Fixture + "/package.json", Fixture + "/version.json",
                Fixture + "/index.js" }.Concat(changed).Distinct(StringComparer.Ordinal)
                .Select(NodeScenario.File).ToArray();
            Native = new(new(NodeScenario.Basis, files), new(NodeScenario.Candidate, files),
                changed);
            Scope = Coordinates();
            DotNetBasis = DotNet(NodeScenario.Basis);
            DotNetCandidate = DotNet(NodeScenario.Candidate);
            NodeBasis = Node(NodeScenario.Basis);
            NodeCandidate = Node(NodeScenario.Candidate);
        }

        internal ScopeInputs Coordinates() => new(new(Native.Basis.Commit,
            Native.Candidate.Commit, false), Native.ChangedPaths,
            Rows(Native.Basis), Rows(Native.Candidate));
        private ScopeCoordinate[] Rows(GitRevision revision) => Native.ChangedPaths.Select(path =>
        {
            GitEntry? entry = revision.Entries.SingleOrDefault(entry => entry.Path == path);
            return new ScopeCoordinate(path, entry is not null, entry?.Mode, []);
        }).ToArray();
        internal ScopeInputs Join() => DotNetGroupReader.Join(Scope, Native, DotNetBasis,
            DotNetCandidate, NodeBasis, NodeCandidate);
        private static RepositoryFacts DotNet(string revision) => new(revision,
            DotNetFactsAssembler.Scope, [new(Parent, "tests/control", [], [], null, null, [],
                [Parent], CheckOrigin.NativeRetained)], [], [], []);
        private static RepositoryFacts Node(string revision) => new(revision,
            NodeFactsAssembler.Scope, [new(Fixture, Fixture, [], [], null, "node/preset",
                [])], [], [], []);
    }
}
