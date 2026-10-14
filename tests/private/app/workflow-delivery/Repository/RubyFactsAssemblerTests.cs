using WorkflowDelivery.CI;
using WorkflowDelivery.Repository;

namespace WorkflowDelivery.Tests.Repository;

[TestClass]
public sealed class RubyFactsAssemblerTests
{
    private static readonly string[] Variants = ["first", "second"];
    private static readonly string[] NestedPaths = ["src/parent/nested/lib.rb"];
    private static RubyProjectDependencies Producer(string directory,
        params string[] dependencies) => new(directory, "native-" + directory, dependencies);

    private static ProjectFacts Owner(string directory) => new("ruby:" + directory,
        directory, [], [], null, RubyProjectChecks.Preset,
        [new(new(directory, "suite", "first"), [], "ubuntu-latest", true, []),
            new(new(directory, "suite", "second"), [], "ubuntu-latest", true, [])]);

    private static RubyFactsEndpoint Endpoint(string revision,
        RubyProjectDependencies[] projects, ProjectFacts[] owners, params string[] extraPaths)
    {
        GitEntry[] entries = projects.SelectMany(project => new[] {
            project.Directory + "/native.gemspec", project.Directory + "/lib.rb" })
            .Concat(extraPaths).Distinct(StringComparer.Ordinal)
            .Select(path => new GitEntry(path, "100644", "blob")).ToArray();
        return new(new(revision, entries), new(new(projects, []), []), owners, []);
    }

    private static CiPlan Plan(RubyFactAssembly assembly) =>
        ImpactPlanner.Plan(assembly.Request, assembly.PairedReasons);

    [TestMethod]
    [DataRow("tests/private/app/workflow-delivery/Native/Ruby/spec/facts_spec.rb")]
    [DataRow("tests/private/app/workflow-delivery/Native/Ruby/Gemfile.lock")]
    [DataRow("src/private/app/workflow-delivery/Native/Ruby/facts.rb")]
    [DataRow("src/private/app/workflow-delivery/Native/contracts/ruby-bundle.schema.json")]
    public void BundleOnlyNativeSuiteRetainsInputsAndRequiredResultWithoutSyntheticGem(
        string changed)
    {
        string directory = RubyNativeTestInputs.Directory;
        string[] paths = [directory + "/Gemfile", directory + "/Gemfile.lock",
            directory + "/spec/facts_spec.rb",
            "src/private/app/workflow-delivery/Native/Ruby/facts.rb",
            "src/private/app/workflow-delivery/Native/contracts/ruby-bundle.schema.json"];
        string id = "ruby:" + directory;
        var owner = new ProjectFacts(id, directory, [], [], null, RubyProjectChecks.Preset,
            [new(new(directory, RubyProjectChecks.RSpec, "default"),
                [], "ubuntu-latest", true, [])],
            paths.Where(path => path.StartsWith(directory + "/", StringComparison.Ordinal))
                .ToArray());
        RubyFactsEndpoint Endpoint(string commit) => new(new(commit,
            paths.Select(path => new GitEntry(path, "100644", "blob")).ToArray()),
            new(new([], [new(directory + "/Gemfile", directory,
                [directory + "/Gemfile", directory + "/Gemfile.lock"], [])]), []), [owner],
            paths.Where(RubyNativeTestInputs.Consumes).Select(path => new SharedInput(path, [id]))
                .ToArray());
        RubyFactAssembly assembly = RubyFactsAssembler.Assemble(Endpoint("basis"),
            Endpoint("candidate"), [changed], false);
        CiPlan plan = Plan(assembly);
        PlannedCheck check = Assert.ContainsSingle(plan.Checks);
        Assert.AreEqual(new CheckKey(directory, RubyProjectChecks.RSpec, "default"),
            check.Work.Key);
        Assert.IsTrue(check.Work.Required);
        Assert.IsFalse(ResultCollector.Collect(plan, []).Satisfied);
        Assert.IsTrue(ResultCollector.Collect(plan,
            [new("candidate", check.Work.Key, CheckStatus.Passed)]).Satisfied);
        Assert.IsEmpty(assembly.Request.Candidate.Projects[0].Dependencies);
        Assert.IsEmpty(Endpoint("candidate").Native.Graph.Projects);
    }

    [TestMethod]
    [DataRow(false, false)]
    [DataRow(true, false)]
    [DataRow(true, true)]
    public void BundleOnlySuiteRetainsTransitiveAndRemovedNativeProducerInputs(
        bool nested, bool removed)
    {
        const string suite = "tests/native-ruby";
        string context = nested ? suite + "/contexts/bundler" : suite;
        string[] paths = [suite + "/Gemfile", suite + "/Gemfile.lock",
            context + "/Gemfile", context + "/Gemfile.lock", "shared/version.json"];
        var owner = new ProjectFacts("ruby:" + suite, suite, [], [], null,
            RubyProjectChecks.Preset,
            [new(new(suite, RubyProjectChecks.RSpec, "default"),
                [], "ubuntu-latest", true, [])]);
        RubyFactsEndpoint Native(string revision, bool remove)
        {
            RubyFactsEndpoint endpoint = Endpoint(revision,
                [Producer("src/leaf"), Producer("src/gem", "src/leaf"),
                    Producer("src/unrelated")], [owner], paths);
            RubyBundleConsumption[] bundles =
                [new(context + "/Gemfile", context,
                    [context + "/Gemfile", context + "/Gemfile.lock"],
                    remove ? [] : ["src/gem"])];
            if (nested) bundles = [new(suite + "/Gemfile", suite,
                [suite + "/Gemfile", suite + "/Gemfile.lock"], []), .. bundles];
            return endpoint with
            {
                Native = new(endpoint.Native.Graph with { Bundles = bundles },
                    [new("shared/version.json", ["src/leaf"])])
            };
        }
        RubyFactsEndpoint basis = Native("basis", false);
        RubyFactsEndpoint candidate = Native("candidate", removed);
        foreach (string path in new[] { "src/leaf/lib.rb", "shared/version.json" })
        {
            RubyFactAssembly assembly = RubyFactsAssembler.Assemble(basis, candidate,
                [path], false);
            PlannedCheck check = Assert.ContainsSingle(Plan(assembly).Checks);
            Assert.AreEqual(new CheckKey(suite, RubyProjectChecks.RSpec, "default"),
                check.Work.Key);
            Assert.Contains(new SelectionReason(path, "basis", "ruby:" + suite),
                assembly.PairedReasons);
            Assert.IsFalse(ResultCollector.Collect(Plan(assembly), []).Satisfied);
        }
        Assert.IsEmpty(Plan(RubyFactsAssembler.Assemble(basis, candidate,
            ["src/unrelated/lib.rb"], false)).Checks);
    }

    [TestMethod]
    public void PairedTransitiveEdgesRetainInputOnlyProducersAndCompleteOwnerVariants()
    {
        RubyFactsEndpoint basis = Endpoint("basis",
            [Producer("src/a"), Producer("src/b", "src/a"), Producer("src/c")],
            [Owner("src/c")]);
        RubyFactsEndpoint candidate = Endpoint("candidate",
            [Producer("src/a"), Producer("src/b"), Producer("src/c", "src/b")],
            [Owner("src/c")]);
        RubyFactAssembly assembly = RubyFactsAssembler.Assemble(basis, candidate,
            ["src/a/lib.rb"], false);
        CiPlan plan = Plan(assembly);
        Assert.HasCount(2, plan.Checks);
        Assert.IsTrue(plan.Checks.All(check => check.Work.Key.Target == "src/c"));
        CollectionAssert.AreEquivalent(Variants,
            plan.Checks.Select(check => check.Work.Key.Variant).ToArray());
        CollectionAssert.AreEquivalent(new[] {
            new SelectionReason("src/a/lib.rb", "basis", "ruby:src/c"),
            new SelectionReason("src/a/lib.rb", "candidate", "ruby:src/c") },
            assembly.PairedReasons);
        Assert.HasCount(1, assembly.Request.Candidate.Projects);
        Assert.IsEmpty(assembly.Request.Candidate.UnaffectedPaths);
        Assert.IsFalse(ResultCollector.Collect(plan,
            [new("candidate", plan.Checks[0].Work.Key, CheckStatus.Passed)]).Satisfied);
    }

    [TestMethod]
    public void DeletedProducerStillSelectsItsSurvivingPreviousConsumer()
    {
        RubyFactsEndpoint basis = Endpoint("basis",
            [Producer("src/deleted"), Producer("src/consumer", "src/deleted")],
            [Owner("src/consumer")]);
        RubyFactsEndpoint candidate = Endpoint("candidate", [Producer("src/consumer")],
            [Owner("src/consumer")]);
        RubyFactAssembly assembly = RubyFactsAssembler.Assemble(basis, candidate,
            ["src/deleted/lib.rb"], false);
        CiPlan plan = Plan(assembly);
        Assert.HasCount(2, plan.Checks);
        Assert.IsTrue(plan.Checks.All(check => check.Work.Key.Target == "src/consumer"));
        Assert.AreEqual(new SelectionReason("src/deleted/lib.rb", "basis",
            "ruby:src/consumer"), Assert.ContainsSingle(assembly.PairedReasons));
    }

    [TestMethod]
    public void MovesRetainBothInitiatingCoordinatesWithoutAssociatingNames()
    {
        RubyFactsEndpoint basis = Endpoint("basis", [Producer("src/old")], [Owner("src/old")]);
        RubyFactsEndpoint candidate = Endpoint("candidate", [Producer("src/new")],
            [Owner("src/new")]);
        candidate = candidate with
        {
            Native = candidate.Native with
            {
                Graph = new(
            [candidate.Native.Graph.Projects[0] with {
                Name = basis.Native.Graph.Projects[0].Name }], [])
            }
        };
        RubyFactAssembly assembly = RubyFactsAssembler.Assemble(basis, candidate,
            ["src/old/lib.rb", "src/new/lib.rb"], false);
        CiPlan plan = Plan(assembly);
        Assert.HasCount(2, plan.Checks);
        Assert.IsTrue(plan.Checks.All(check => check.Work.Key.Target == "src/new"));
        CollectionAssert.AreEquivalent(new[] {
            new SelectionReason("src/old/lib.rb", "basis", "ruby:src/old"),
            new SelectionReason("src/new/lib.rb", "candidate", "ruby:src/new") },
            assembly.PairedReasons);
    }

    [TestMethod]
    public void CyclesTerminateAndSharedNativeInputsReachActualOwners()
    {
        RubyFactsEndpoint endpoint = Endpoint("candidate",
            [Producer("src/a", "src/b"), Producer("src/b", "src/a")],
            [Owner("src/b")], "version.json");
        endpoint = endpoint with
        {
            Native = endpoint.Native with
            {
                Inputs = [new("version.json", ["src/a"])]
            }
        };
        RubyFactAssembly assembly = RubyFactsAssembler.Assemble(endpoint, endpoint,
            ["version.json"], false);
        Assert.HasCount(2, Plan(assembly).Checks);
        Assert.AreEqual(new SelectionReason("version.json", "candidate", "ruby:src/b"),
            Assert.ContainsSingle(assembly.PairedReasons));
    }

    [TestMethod]
    public void NestedInputOnlySourceCannotInheritItsParentsQualityOwnership()
    {
        RubyFactsEndpoint endpoint = Endpoint("candidate",
            [Producer("src/parent"), Producer("src/parent/nested")], [Owner("src/parent")]);
        RubyFactAssembly assembly = RubyFactsAssembler.Assemble(endpoint, endpoint,
            ["src/parent/nested/lib.rb"], false);
        Assert.IsEmpty(Plan(assembly).Checks);
        Assert.IsEmpty(assembly.PairedReasons);
        CollectionAssert.AreEqual(NestedPaths,
            assembly.Request.Candidate.UnaffectedPaths);
        Assert.DoesNotContain("src/parent/nested/lib.rb",
            assembly.Request.Candidate.Projects[0].OwnedPaths!);
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public void KnownEmptyOperationScopeIsDistinctFromUnknownInputEvenInFullMode(bool full)
    {
        RubyFactsEndpoint endpoint = Endpoint("candidate", [], [], "docs/guide.md");
        Assert.ThrowsExactly<InvalidDataException>(() => RubyFactsAssembler.Assemble(
            endpoint, endpoint, ["docs/guide.md"], full));
        endpoint = endpoint with { OperationInputs = [new("docs/guide.md", [])] };
        RubyFactAssembly assembly = RubyFactsAssembler.Assemble(endpoint, endpoint,
            ["docs/guide.md"], full);
        CiPlan plan = Plan(assembly);
        Assert.IsEmpty(plan.Checks);
        Assert.IsTrue(ResultCollector.Collect(plan, []).Satisfied);
    }

    [TestMethod]
    public void BundleOnlyInputsRemainKnownWithoutInventingARubyQualityOwner()
    {
        RubyFactsEndpoint endpoint = Endpoint("candidate", [], [],
            "node/Gemfile", "node/Gemfile.lock");
        endpoint = endpoint with
        {
            Native = endpoint.Native with
            {
                Graph = new([],
            [new("node/Gemfile", "node", ["node/Gemfile", "node/Gemfile.lock"], [])])
            }
        };
        RubyFactAssembly assembly = RubyFactsAssembler.Assemble(endpoint, endpoint,
            ["node/Gemfile"], false);
        Assert.IsEmpty(assembly.Request.Candidate.Projects);
        Assert.IsEmpty(Plan(assembly).Checks);
        Assert.IsEmpty(assembly.PairedReasons);
        Assert.AreEqual("node/Gemfile.lock", endpoint.Native.Graph.Bundles[0].Inputs[1]);
    }

    [TestMethod]
    [DataRow("dangling-producer")]
    [DataRow("missing-source")]
    [DataRow("unknown-owner")]
    [DataRow("missing-prerequisite")]
    [DataRow("missing-preset")]
    [DataRow("unknown-operation-owner")]
    [DataRow("unsupported-owned-entry")]
    public void IncompleteFactsCannotHideInAnUnselectedOrBasisOnlyEndpoint(string defect)
    {
        RubyFactsEndpoint basis = Endpoint("basis", [Producer("src/a")], [Owner("src/a")]);
        RubyProjectDependencies producer = basis.Native.Graph.Projects[0];
        ProjectFacts owner = basis.Owners[0];
        if (defect == "dangling-producer")
            basis = basis with
            {
                Native = basis.Native with
                {
                    Graph = new(
                [producer with { Ordinary = ["missing"] }], [])
                }
            };
        if (defect == "missing-source")
            basis = basis with { Native = basis.Native with { Graph = new([], []) } };
        if (defect == "unknown-owner")
            basis = basis with { Owners = [Owner("src/unknown")] };
        if (defect == "missing-prerequisite")
            basis = basis with
            {
                Owners = [owner with { Checks = [owner.Checks[0] with {
                Prerequisites = [new("src/a", "missing", "first")] }] }]
            };
        if (defect == "missing-preset")
            basis = basis with { Owners = [owner with { QualityPreset = null }] };
        if (defect == "unknown-operation-owner")
            basis = basis with { OperationInputs = [new("version.json", ["unknown"])] };
        if (defect == "unsupported-owned-entry")
            basis = basis with
            {
                Revision = basis.Revision with
                {
                    Entries = basis.Revision.Entries.Select(entry => entry.Path == "src/a/lib.rb"
                        ? entry with { Mode = "120000" } : entry).ToArray()
                }
            };
        RubyFactsEndpoint candidate = Endpoint("candidate", [], []);
        Assert.ThrowsExactly<InvalidDataException>(() =>
            RubyFactsAssembler.Assemble(basis, candidate, [], false));
    }
}
