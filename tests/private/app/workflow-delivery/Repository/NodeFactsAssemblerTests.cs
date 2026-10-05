using System.Text.Json;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using WorkflowDelivery.CI;
using WorkflowDelivery.Repository;

namespace WorkflowDelivery.Tests.Repository;

[TestClass]
public sealed class NodeFactsAssemblerTests
{
    private static readonly string[] TwoProjects = ["src/a", "src/b"];

    [TestMethod]
    public async Task PlanOrdinaryChangeExcludesUnrelatedProjectWithAbsentQuality()
    {
        NodeRevisionInputs basis = WithoutQuality(NodeScenario.Inputs(NodeScenario.Basis,
            NodeScenario.Project("src/a"), NodeScenario.Project("src/b")), "src/b");
        NodeRevisionInputs candidate = WithoutQuality(NodeScenario.Inputs(NodeScenario.Candidate,
            NodeScenario.Project("src/a"), NodeScenario.Project("src/b")), "src/b");

        CiPlan plan = await NodeFactsAssembler.PlanAsync(basis, candidate,
            ["src/a/index.js"], false,
            (project, _) => project.Directory == "src/a"
                ? Task.FromResult(NodeScenario.Scripts(project))
                : throw new InvalidOperationException("Unrelated project hydrated."),
            (_, _) => throw new InvalidOperationException("No package version is needed."),
            CancellationToken.None);

        Assert.AreEqual(NodeScenario.Basis, plan.Comparison);
        Assert.AreEqual(NodeScenario.Candidate, plan.Candidate);
        Assert.AreEqual(NodeFactsAssembler.Scope, plan.Scope);
        AssertProjectTargets(plan, "src/a");
        Assert.HasCount(2, plan.Checks);
        PlannedCheck sourceTest = plan.Checks.Single(check =>
            check.Work.Key.Check == "node/project-test-v1");
        Assert.IsEmpty(sourceTest.Work.Prerequisites);
        Assert.Contains(new SelectionReason("src/a/index.js", NodeScenario.Basis, "src/a"),
            sourceTest.Reasons);
        Assert.Contains(new SelectionReason("src/a/index.js", NodeScenario.Candidate, "src/a"),
            sourceTest.Reasons);
    }

    [TestMethod]
    public async Task PlanSharedChangeRejectsSelectedProjectWithoutQuality()
    {
        NodeRevisionInputs basis = WithoutQuality(NodeScenario.Inputs(NodeScenario.Basis,
            NodeScenario.Project("src/a"), NodeScenario.Project("src/b")), "src/b");
        NodeRevisionInputs candidate = WithoutQuality(NodeScenario.Inputs(NodeScenario.Candidate,
            NodeScenario.Project("src/a"), NodeScenario.Project("src/b")), "src/b");
        basis = basis with { OperationInputs = [new("pnpm-lock.yaml", TwoProjects)] };
        candidate = candidate with { OperationInputs = [new("pnpm-lock.yaml", TwoProjects)] };

        InvalidDataException error = await Assert.ThrowsExactlyAsync<InvalidDataException>(() =>
            NodeScenario.Plan(basis, candidate, "pnpm-lock.yaml"));

        Assert.Contains("quality", error.Message);
    }

    [TestMethod]
    [DataRow("120000", "blob")]
    [DataRow("160000", "commit")]
    public void AssembleRejectsNonregularOperationInput(string mode, string objectType)
    {
        NodeRevisionInputs inputs = NodeScenario.Inputs(NodeScenario.Candidate,
            NodeScenario.Project("src/a"));
        inputs = inputs with
        {
            OperationInputs = [new(".ignore", ["src/a"])],
            Revision = inputs.Revision with
            {
                Entries = [.. inputs.Revision.Entries, new(".ignore", mode, objectType)],
            },
        };

        InvalidDataException error = Assert.ThrowsExactly<InvalidDataException>(() =>
            NodeFactsAssembler.Assemble(inputs));

        Assert.Contains(".ignore", error.Message);
    }

    [TestMethod]
    [DataRow("100644")]
    [DataRow("100755")]
    public async Task PlanOperationInputsRetainAbsentCandidatesAndRegularConsumers(string mode)
    {
        NodeRevisionInputs basis = WithOperations(NodeScenario.Basis);
        NodeRevisionInputs candidate = WithOperations(NodeScenario.Candidate);
        RepositoryFacts facts = NodeFactsAssembler.Assemble(candidate);

        foreach (string path in new[] { "biome.jsonc", ".ignore" })
            Assert.AreEqual("src/a", Assert.ContainsSingle(facts.SharedInputs.Single(input =>
                input.Path == path).Consumers));
        CiPlan plan = await NodeScenario.Plan(basis, candidate, "biome.jsonc");

        AssertProjectTargets(plan, "src/a");
        Assert.HasCount(2, plan.Checks);
        foreach (PlannedCheck check in plan.Checks)
            Assert.IsNull(check.Work.Package);

        NodeRevisionInputs WithOperations(string revision)
        {
            NodeRevisionInputs inputs = NodeScenario.Inputs(revision,
                NodeScenario.Project("src/a"), NodeScenario.Project("src/b"));
            return inputs with
            {
                OperationInputs = [new("biome.jsonc", ["src/a"]), new(".ignore", ["src/a"])],
                Revision = inputs.Revision with
                {
                    Entries = [.. inputs.Revision.Entries, new("biome.jsonc", mode, "blob")],
                },
            };
        }
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task PlanAddedOrDeletedOperationInputKeepsEndpointConsumers(bool deleted)
    {
        NodeRevisionInputs basis = NodeScenario.Inputs(NodeScenario.Basis,
            NodeScenario.Project("src/a"), NodeScenario.Project("src/b")) with
        { OperationInputs = [new(".ignore", ["src/a"])] };
        NodeRevisionInputs candidate = NodeScenario.Inputs(NodeScenario.Candidate,
            NodeScenario.Project("src/a"), NodeScenario.Project("src/b")) with
        { OperationInputs = [new(".ignore", ["src/a"])] };
        NodeRevisionInputs present = deleted ? basis : candidate;
        present = present with
        {
            Revision = present.Revision with
            {
                Entries = [.. present.Revision.Entries, NodeScenario.File(".ignore")],
            },
        };
        if (deleted) basis = present;
        else candidate = present;

        CiPlan plan = await NodeScenario.Plan(basis, candidate, ".ignore");

        AssertProjectTargets(plan, "src/a");
        Assert.HasCount(2, plan.Checks);
        foreach (PlannedCheck check in plan.Checks)
        {
            Assert.Contains(new SelectionReason(".ignore", NodeScenario.Basis, "src/a"),
                check.Reasons);
            Assert.Contains(new SelectionReason(".ignore", NodeScenario.Candidate, "src/a"),
                check.Reasons);
        }
    }

    [TestMethod]
    public async Task PlanDeletedProducerKeepsSurvivingConsumerWithoutOldPackageVersion()
    {
        NodeRevisionInputs basis = NodeScenario.Units(NodeScenario.Inputs(NodeScenario.Basis,
            NodeScenario.Project("src/a"), NodeScenario.Project("src/b", "src/a")),
            NodeScenario.Unit("removed", ("old", "src/a"))) with
        { Versions = [] };
        NodeRevisionInputs candidate = NodeScenario.Inputs(NodeScenario.Candidate,
            NodeScenario.Project("src/b"));

        CiPlan plan = await NodeFactsAssembler.PlanAsync(basis, candidate,
            ["src/a/index.js"], false,
            (project, _) => Task.FromResult(NodeScenario.Scripts(project)),
            (_, _) => throw new InvalidOperationException("Removed packages need no version."),
            CancellationToken.None);

        AssertProjectTargets(plan, "src/b");
        Assert.HasCount(2, plan.Checks);
        foreach (PlannedCheck check in plan.Checks)
        {
            Assert.IsNull(check.Work.Package);
            Assert.AreEqual(new SelectionReason("src/a/index.js", NodeScenario.Basis, "src/a"),
                Assert.ContainsSingle(check.Reasons));
        }
    }

    [TestMethod]
    public async Task PlanRemovedDependencyUsesBaseRelation()
    {
        NodeRevisionInputs basis = NodeScenario.Inputs(NodeScenario.Basis,
            NodeScenario.Project("src/a"), NodeScenario.Project("src/b", "src/a"));
        NodeRevisionInputs candidate = NodeScenario.Inputs(NodeScenario.Candidate,
            NodeScenario.Project("src/a"), NodeScenario.Project("src/b"));

        CiPlan plan = await NodeScenario.Plan(basis, candidate, "src/a/index.js");

        AssertProjectTargets(plan, TwoProjects);
        foreach (PlannedCheck check in plan.Checks.Where(check =>
            check.Work.Key.Target == "src/b"))
            Assert.Contains(new SelectionReason("src/a/index.js", NodeScenario.Basis, "src/a"),
                check.Reasons);
    }

    [TestMethod]
    public async Task PlanRemovedUnitMembershipKeepsSurvivingMembers()
    {
        NodeRevisionInputs basis = NodeScenario.Units(NodeScenario.Inputs(NodeScenario.Basis,
            NodeScenario.Project("src/a"), NodeScenario.Project("src/b")),
            NodeScenario.Unit("old", ("first", "src/a"), ("second", "src/b")))
            with
        { Versions = [] };
        NodeRevisionInputs candidate = NodeScenario.Inputs(NodeScenario.Candidate,
            NodeScenario.Project("src/a"), NodeScenario.Project("src/b"));

        CiPlan plan = await NodeScenario.Plan(basis, candidate,
            "workflow-delivery.release-unit.yml");

        AssertProjectTargets(plan, TwoProjects);
        Assert.IsTrue(plan.Checks.All(check => check.Work.Package is null));
        Assert.IsTrue(plan.Checks.All(check => check.Reasons.Contains(new SelectionReason(
            "workflow-delivery.release-unit.yml", NodeScenario.Basis,
            check.Work.Key.Target))));
    }

    [TestMethod]
    public void AssembleUsesSourceIdentityAndKeepsPublishAndLocalConsumers()
    {
        NodeRevisionInputs inputs = NodeScenario.Inputs(NodeScenario.Candidate,
            new("src/a", "build/a", []), NodeScenario.Project("src/b", "src/a"));
        inputs = inputs with
        {
            Graph = inputs.Graph with
            { LocalInputs = [new("archives/input.tgz", false, ["src/b"])] },
            Revision = inputs.Revision with
            {
                Entries = [.. inputs.Revision.Entries, NodeScenario.File("archives/input.tgz"),
                    NodeScenario.File("build/a/index.js")],
            },
        };

        RepositoryFacts facts = NodeFactsAssembler.Assemble(inputs);

        CollectionAssert.AreEqual(TwoProjects,
            facts.Projects.Select(project => project.Id).ToArray());
        Assert.DoesNotContain(".", facts.Projects.Select(project => project.Id));
        Assert.AreEqual("src/a", Assert.ContainsSingle(facts.Projects.Single(project =>
            project.Id == "src/b").Dependencies));
        SharedInput input = facts.SharedInputs.Single(input => input.Path == "archives/input.tgz");
        Assert.AreEqual("src/b", Assert.ContainsSingle(input.Consumers));
        SharedInput publishInput = facts.SharedInputs.Single(input =>
            input.Path == "build/a/index.js");
        Assert.AreEqual("src/a", Assert.ContainsSingle(publishInput.Consumers));
        Assert.DoesNotContain("build/a", facts.Projects.Select(project => project.Id));
    }

    [TestMethod]
    public async Task PlanNestedSourceChangeUsesNearestOwner()
    {
        NodeRevisionInputs basis = NodeScenario.Inputs(NodeScenario.Basis,
            NodeScenario.Project("src/outer"), NodeScenario.Project("src/outer/nested"));
        NodeRevisionInputs candidate = NodeScenario.Inputs(NodeScenario.Candidate,
            NodeScenario.Project("src/outer"), NodeScenario.Project("src/outer/nested"));

        CiPlan plan = await NodeScenario.Plan(basis, candidate, "src/outer/nested/index.js");

        AssertProjectTargets(plan, "src/outer/nested");
        Assert.HasCount(2, plan.Checks);
    }

    [TestMethod]
    [DataRow("dependency")]
    [DataRow("target")]
    [DataRow("local-input")]
    public void AssembleRejectsUnresolvedWorkspaceRootRelation(string relation)
    {
        NodeRevisionInputs inputs = NodeScenario.Inputs(NodeScenario.Candidate,
            NodeScenario.Project("src/a"));
        PnpmGraph graph = inputs.Graph;
        graph = relation switch
        {
            "dependency" => graph with
            {
                Projects = [NodeScenario.Project(".", "src/a"), NodeScenario.Project("src/a")],
            },
            "target" => graph with
            {
                Projects = [NodeScenario.Project("."), NodeScenario.Project("src/a", ".")],
            },
            _ => graph with { LocalInputs = [new("archive.tgz", false, ["."])] },
        };

        InvalidDataException error = Assert.ThrowsExactly<InvalidDataException>(() =>
            NodeFactsAssembler.Assemble(inputs with { Graph = graph }));

        Assert.Contains("root", error.Message);
    }

    [TestMethod]
    [DataRow("120000", "blob", false)]
    [DataRow("160000", "commit", false)]
    [DataRow("120000", "blob", true)]
    [DataRow("160000", "commit", true)]
    public void AssembleRejectsUnsupportedRelevantSourceEntry(string mode, string objectType,
        bool releaseEntry)
    {
        NodeRevisionInputs inputs = NodeScenario.Inputs(NodeScenario.Candidate,
            NodeScenario.Project("src/a"));
        if (releaseEntry)
        {
            ReleaseUnitDeclaration unit = NodeScenario.Unit("product", ("build", "src/a"));
            unit = unit with
            { Builds = [unit.Builds[0] with { EntryPoint = "src/a/native-entry" }] };
            inputs = NodeScenario.Units(inputs, unit);
        }
        inputs = inputs with
        {
            Revision = inputs.Revision with
            {
                Entries = [.. inputs.Revision.Entries, new("src/a/native-entry", mode, objectType)],
            },
        };

        Assert.ThrowsExactly<InvalidDataException>(() => NodeFactsAssembler.Assemble(inputs));
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task PlanAddedOrDeletedAncestorConfigurationKeepsEndpointConsumers(bool deleted)
    {
        NodeRevisionInputs basis = NodeScenario.Inputs(NodeScenario.Basis,
            NodeScenario.Project("src/a"));
        NodeRevisionInputs candidate = NodeScenario.Inputs(NodeScenario.Candidate,
            NodeScenario.Project("src/a"));
        NodeRevisionInputs withAncestor = deleted ? basis : candidate;
        withAncestor = withAncestor with
        {
            Quality = new Dictionary<string, QualitySelection?>
            {
                ["src/a"] = new(NodeScenario.Preset, "src/" + NodeScenario.Declaration),
            },
            Revision = withAncestor.Revision with
            {
                Entries = [.. withAncestor.Revision.Entries,
                    NodeScenario.File("src/" + NodeScenario.Declaration)],
            },
        };
        if (deleted) basis = withAncestor;
        else candidate = withAncestor;

        CiPlan plan = await NodeScenario.Plan(basis, candidate, "src/" + NodeScenario.Declaration);

        AssertProjectTargets(plan, "src/a");
        Assert.IsTrue(plan.Checks.All(check => check.Reasons.Contains(new SelectionReason(
            "src/" + NodeScenario.Declaration,
            deleted ? NodeScenario.Basis : NodeScenario.Candidate, "src/a"))));
    }

    [TestMethod]
    public async Task PlanSelectedPackageRetainsEveryBuildAndRejectsMissingNativeVersionInputs()
    {
        NodeRevisionInputs basis = NodeScenario.Inputs(NodeScenario.Basis,
            NodeScenario.Project("src/a"));
        NodeRevisionInputs candidate = NodeScenario.Units(
            NodeScenario.Inputs(NodeScenario.Candidate,
                NodeScenario.Project("src/a")), NodeScenario.Unit("product",
            ("first", "src/a"), ("second", "src/a")));

        CiPlan plan = await NodeScenario.Plan(basis, candidate, "src/a/index.js");

        Assert.HasCount(8, plan.Checks);
        Assert.HasCount(6, plan.Checks.Where(check => check.Work.Package is not null).ToArray());
        PlannedCheck sourceTest = plan.Checks.Single(check =>
            check.Work.Key.Check == "node/project-test-v1");
        Assert.IsEmpty(sourceTest.Work.Prerequisites);
        Assert.IsTrue(plan.Checks.All(check => check.Work.Required));
        InvalidDataException error = await Assert.ThrowsExactlyAsync<InvalidDataException>(() =>
            NodeFactsAssembler.PlanAsync(basis, candidate with { Versions = [] },
                ["src/a/index.js"], false,
                (project, _) => Task.FromResult(NodeScenario.Scripts(project)),
                (_, _) => throw new InvalidOperationException(
                    "Missing inputs must stop projection."),
                CancellationToken.None));
        Assert.Contains("version inputs", error.Message);
    }

    [TestMethod]
    [DataRow("src/a/index.js")]
    [DataRow("workflow-delivery.release-unit.yml")]
    public async Task PlanPublishEntryKeepsSourceUnitAndRejectsUnsupportedBuild(string changedPath)
    {
        NodeRevisionInputs basis = NodeScenario.Inputs(NodeScenario.Basis,
            new PnpmProject("src/a", "build/a", []));
        ReleaseUnitDeclaration unit = NodeScenario.Unit("product", ("build", "build/a"));
        NodeRevisionInputs candidate = NodeScenario.Units(
            NodeScenario.Inputs(NodeScenario.Candidate,
                new PnpmProject("src/a", "build/a", [])), unit);
        candidate = candidate with
        {
            Revision = candidate.Revision with
            {
                Entries = [.. candidate.Revision.Entries,
                    NodeScenario.File("build/a/package.json")],
            },
        };

        RepositoryFacts facts = NodeFactsAssembler.Assemble(candidate);

        ProjectFacts project = Assert.ContainsSingle(facts.Projects);
        Assert.AreEqual("src/a", project.Id);
        Assert.AreEqual("product", project.ReleaseUnit);
        Assert.AreEqual("src/a", Assert.ContainsSingle(facts.SharedInputs.Single(input =>
            input.Path == unit.SourcePath).Consumers));
        Assert.DoesNotContain("build/a", facts.Projects.Select(value => value.Id));
        InvalidDataException error = await Assert.ThrowsExactlyAsync<InvalidDataException>(() =>
            NodeScenario.Plan(basis, candidate, changedPath));
        Assert.Contains("product/build", error.Message);
    }

    [TestMethod]
    public async Task PlanUnrelatedOtherEcosystemUnitKeepsNodeWork()
    {
        NodeRevisionInputs basis = NodeScenario.Inputs(NodeScenario.Basis,
            NodeScenario.Project("src/a"));
        ReleaseUnitDeclaration unit = NodeScenario.Unit("python", ("build", "src/python"));
        unit = unit with
        {
            Builds = [unit.Builds[0] with
            {
                Definition = "python/package-v1",
                EntryPoint = "src/python/pyproject.toml",
                Outputs = [new("wheel", "primary-package", "python-wheel")],
            }],
        };
        NodeRevisionInputs candidate = NodeScenario.Units(
            NodeScenario.Inputs(NodeScenario.Candidate,
                NodeScenario.Project("src/a")), unit);
        candidate = candidate with
        {
            Revision = candidate.Revision with
            {
                Entries = [.. candidate.Revision.Entries,
                    NodeScenario.File("src/python/pyproject.toml")],
            },
        };

        RepositoryFacts facts = NodeFactsAssembler.Assemble(candidate);
        CiPlan plan = await NodeScenario.Plan(basis, candidate, "src/a/index.js");

        Assert.IsNull(Assert.ContainsSingle(facts.Projects).ReleaseUnit);
        Assert.DoesNotContain(unit.SourcePath, facts.SharedInputs.Select(input => input.Path));
        AssertProjectTargets(plan, "src/a");
        Assert.HasCount(2, plan.Checks);
        Assert.IsTrue(plan.Checks.All(check => check.Work.Package is null));
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public void AssembleRejectsCompetingSourceAndPublishEntryOwners(bool competingPublishOwners)
    {
        string output = competingPublishOwners ? "build/shared" : "src/b";
        NodeRevisionInputs inputs = NodeScenario.Inputs(NodeScenario.Candidate,
            new("src/a", output, []), competingPublishOwners
                ? new("src/b", output, []) : NodeScenario.Project("src/b"));
        ReleaseUnitDeclaration unit = NodeScenario.Unit("product", ("build", output));
        inputs = NodeScenario.Units(inputs, unit);
        if (competingPublishOwners)
            inputs = inputs with
            {
                Revision = inputs.Revision with
                {
                    Entries = [.. inputs.Revision.Entries,
                        NodeScenario.File(output + "/package.json")],
                },
            };

        InvalidDataException error = Assert.ThrowsExactly<InvalidDataException>(() =>
            NodeFactsAssembler.Assemble(inputs));

        Assert.Contains("Ambiguous native release-unit entry ownership", error.Message);
    }

    [TestMethod]
    public void AssembleRejectsExplicitNodeEntryWithoutNativeOwner()
    {
        ReleaseUnitDeclaration unit = NodeScenario.Unit("unknown", ("build", "src/missing"));
        NodeRevisionInputs inputs = NodeScenario.Units(NodeScenario.Inputs(NodeScenario.Candidate,
            NodeScenario.Project("src/a")), unit);
        inputs = inputs with
        {
            Revision = inputs.Revision with
            {
                Entries = [.. inputs.Revision.Entries,
                    NodeScenario.File("src/missing/package.json")],
            },
        };

        InvalidDataException error = Assert.ThrowsExactly<InvalidDataException>(() =>
            NodeFactsAssembler.Assemble(inputs));

        Assert.Contains("Unresolved Node release-unit entry", error.Message);
        Assert.Contains("src/missing/package.json", error.Message);
    }

    [TestMethod]
    public void AssembleKeepsSingleOwnerWhenSourceAndPublishCoordinatesOverlap()
    {
        ReleaseUnitDeclaration unit = NodeScenario.Unit("product", ("build", "src/a/dist"));
        NodeRevisionInputs inputs = NodeScenario.Units(NodeScenario.Inputs(NodeScenario.Candidate,
            new PnpmProject("src/a", "src/a/dist", [])), unit);
        inputs = inputs with
        {
            Revision = inputs.Revision with
            {
                Entries = [.. inputs.Revision.Entries,
                    NodeScenario.File("src/a/dist/package.json")],
            },
        };

        RepositoryFacts facts = NodeFactsAssembler.Assemble(inputs);

        Assert.AreEqual("product", Assert.ContainsSingle(facts.Projects).ReleaseUnit);
        Assert.AreEqual("src/a", Assert.ContainsSingle(facts.SharedInputs.Single(input =>
            input.Path == unit.SourcePath).Consumers));
    }

    [TestMethod]
    public async Task PlanSelectedMixedUnitRejectsWithoutPartialWork()
    {
        NodeRevisionInputs basis = NodeScenario.Inputs(NodeScenario.Basis,
            NodeScenario.Project("src/a"));
        ReleaseUnitDeclaration unit = NodeScenario.Unit("mixed", ("node", "src/a"));
        unit = unit with
        {
            Builds = [.. unit.Builds, new("python", "python/package-v1",
                "src/python/pyproject.toml", [new("wheel", "primary-package", "python-wheel")])],
        };
        NodeRevisionInputs candidate = NodeScenario.Units(
            NodeScenario.Inputs(NodeScenario.Candidate,
                NodeScenario.Project("src/a")), unit);

        InvalidDataException error = await Assert.ThrowsExactlyAsync<InvalidDataException>(() =>
            NodeScenario.Plan(basis, candidate, "src/a/index.js"));

        Assert.Contains("mixed/python", error.Message);
    }

    [TestMethod]
    [DataRow("node/unsupported-v1", "package.json")]
    [DataRow("custom/unsupported-v1", ".")]
    [DataRow("custom/unsupported-v1", "generated")]
    public async Task PlanUnselectedUnsupportedDefinitionDoesNotPreventOrdinaryChange(
        string definition, string entryPoint)
    {
        NodeRevisionInputs basis = NodeScenario.Inputs(NodeScenario.Basis,
            NodeScenario.Project("src/a"), NodeScenario.Project("src/b"));
        ReleaseUnitDeclaration unit = NodeScenario.Unit("other", ("build", "src/b"));
        unit = unit with
        {
            SourcePath = "src/b/workflow-delivery.release-unit.yml",
            Builds = [unit.Builds[0] with { Definition = definition, EntryPoint = entryPoint }],
        };
        NodeRevisionInputs candidate = NodeScenario.Units(
            NodeScenario.Inputs(NodeScenario.Candidate,
                NodeScenario.Project("src/a"), NodeScenario.Project("src/b")), unit);

        candidate = candidate with
        {
            Revision = candidate.Revision with
            {
                Entries = [.. candidate.Revision.Entries,
                    NodeScenario.File("src/b/generated/input.txt")],
            },
        };
        RepositoryFacts facts = NodeFactsAssembler.Assemble(candidate);
        Assert.AreEqual("other", facts.Projects.Single(project => project.Id == "src/b")
            .ReleaseUnit);
        Assert.AreEqual("src/b", Assert.ContainsSingle(facts.SharedInputs.Single(input =>
            input.Path == unit.SourcePath).Consumers));
        CiPlan plan = await NodeFactsAssembler.PlanAsync(basis, candidate, ["src/a/index.js"],
            false, (project, _) => project.Directory == "src/a"
                ? Task.FromResult(NodeScenario.Scripts(project))
                : throw new InvalidOperationException("Unselected directory build hydrated."),
            (_, _) => throw new InvalidOperationException("Unselected package version hydrated."),
            CancellationToken.None);

        AssertProjectTargets(plan, "src/a");
        Assert.HasCount(2, plan.Checks);
        foreach (PlannedCheck check in plan.Checks)
            Assert.IsNull(check.Work.Package);
    }

    [TestMethod]
    [DataRow("src/b/index.js")]
    [DataRow("src/b/workflow-delivery.release-unit.yml")]
    public async Task PlanSelectedDirectoryEntryRejectsUnsupportedBuild(string changedPath)
    {
        ReleaseUnitDeclaration unit = NodeScenario.Unit("other", ("build", "src/b"));
        unit = unit with
        {
            SourcePath = "src/b/workflow-delivery.release-unit.yml",
            Builds = [unit.Builds[0] with
            { Definition = "custom/unsupported-v1", EntryPoint = "." }],
        };
        NodeRevisionInputs basis = NodeScenario.Units(NodeScenario.Inputs(NodeScenario.Basis,
            NodeScenario.Project("src/b")), unit);
        NodeRevisionInputs candidate = NodeScenario.Units(
            NodeScenario.Inputs(NodeScenario.Candidate, NodeScenario.Project("src/b")), unit);

        InvalidDataException error = await Assert.ThrowsExactlyAsync<InvalidDataException>(() =>
            NodeScenario.Plan(basis, candidate, changedPath));

        Assert.Contains("other/build", error.Message);
    }

    [TestMethod]
    public void AssembleRejectsMissingReleaseEntry()
    {
        ReleaseUnitDeclaration unit = NodeScenario.Unit("other", ("build", "src/b"));
        unit = unit with
        {
            SourcePath = "src/b/workflow-delivery.release-unit.yml",
            Builds = [unit.Builds[0] with
            { Definition = "custom/unsupported-v1", EntryPoint = "missing" }],
        };
        NodeRevisionInputs inputs = NodeScenario.Units(
            NodeScenario.Inputs(NodeScenario.Candidate, NodeScenario.Project("src/b")), unit);
        inputs = inputs with
        {
            Revision = inputs.Revision with
            {
                Entries = [.. inputs.Revision.Entries,
                    NodeScenario.File("src/b/missing-other/input.txt")],
            },
        };

        InvalidDataException error = Assert.ThrowsExactly<InvalidDataException>(() =>
            NodeFactsAssembler.Assemble(inputs));

        Assert.Contains("src/b/missing", error.Message);
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public void AssembleRejectsAmbiguousUnitOrMultipleMembership(bool sameIdentity)
    {
        NodeRevisionInputs inputs = NodeScenario.Inputs(NodeScenario.Candidate,
            NodeScenario.Project("src/a"));
        ReleaseUnitDeclaration first = NodeScenario.Unit("first", ("build", "src/a"));
        ReleaseUnitDeclaration second = first with
        {
            SourcePath = "src/workflow-delivery.release-unit.yml",
            Id = sameIdentity ? first.Id : "second",
            Builds = [first.Builds[0] with { EntryPoint = "a/package.json" }],
        };
        inputs = NodeScenario.Units(inputs, first, second);

        Assert.ThrowsExactly<InvalidDataException>(() => NodeFactsAssembler.Assemble(inputs));
    }

    [TestMethod]
    public async Task PlanUnknownChangedPathRejectsWithoutFullFallback()
    {
        NodeRevisionInputs basis = NodeScenario.Inputs(NodeScenario.Basis,
            NodeScenario.Project("src/a"));
        NodeRevisionInputs candidate = NodeScenario.Inputs(NodeScenario.Candidate,
            NodeScenario.Project("src/a"));

        InvalidDataException error = await Assert.ThrowsExactlyAsync<InvalidDataException>(() =>
            NodeScenario.Plan(basis, candidate, "docs/guide.md"));

        Assert.Contains("Unresolved changed path", error.Message);
    }

    [TestMethod]
    public async Task PlanFullRejectsUnavailableSelectedCoverage()
    {
        NodeRevisionInputs basis = NodeScenario.Inputs(NodeScenario.Basis,
            NodeScenario.Project("src/a"), NodeScenario.Project("src/b"));
        NodeRevisionInputs candidate = WithoutQuality(NodeScenario.Inputs(NodeScenario.Candidate,
            NodeScenario.Project("src/a"), NodeScenario.Project("src/b")), "src/b");

        await Assert.ThrowsExactlyAsync<InvalidDataException>(() =>
            NodeFactsAssembler.PlanAsync(basis, candidate, [], true,
                (project, _) => Task.FromResult(NodeScenario.Scripts(project)),
                (_, _) => Task.FromResult("1.2.3"), CancellationToken.None));
    }

    [TestMethod]
    public async Task AssembledAndSuppliedFactsSelectEquivalentCandidateWork()
    {
        NodeRevisionInputs basis = NodeScenario.Inputs(NodeScenario.Basis,
            NodeScenario.Project("src/a"), NodeScenario.Project("src/b", "src/a"));
        NodeRevisionInputs candidate = NodeScenario.Inputs(NodeScenario.Candidate,
            NodeScenario.Project("src/a"), NodeScenario.Project("src/b"));
        RepositoryFacts before = NodeFactsAssembler.Assemble(basis);
        RepositoryFacts after = NodeFactsAssembler.Assemble(candidate);
        after = after with
        {
            Projects = after.Projects.Select(project => project with
            {
                Checks = NodeQualityChecks.Expand(candidate.Quality[project.Id],
                    NodeScenario.Scripts(candidate.Graph.Projects.Single(native =>
                        native.Directory == project.Id))),
            }).ToArray(),
        };

        CiPlan assembled = await NodeScenario.Plan(basis, candidate, "src/a/index.js");
        CiPlan supplied = ImpactPlanner.Plan(new(before, after, ["src/a/index.js"], false));

        Assert.AreEqual(JsonSerializer.Serialize(supplied, TransferJson.Default.CiPlan),
            JsonSerializer.Serialize(assembled, TransferJson.Default.CiPlan));
        AssertProjectTargets(assembled, TwoProjects);
    }

    [TestMethod]
    [DataRow("script")]
    [DataRow("version")]
    [DataRow("mismatch")]
    public async Task PlanNativeHydrationFailureReturnsNoPartialPlan(string defect)
    {
        NodeRevisionInputs basis = NodeScenario.Inputs(NodeScenario.Basis,
            NodeScenario.Project("src/a"));
        NodeRevisionInputs candidate = NodeScenario.Units(
            NodeScenario.Inputs(NodeScenario.Candidate,
                NodeScenario.Project("src/a")), NodeScenario.Unit("product", ("build", "src/a")));
        Task<CiPlan> Plan() => NodeFactsAssembler.PlanAsync(basis, candidate,
            ["src/a/index.js"], false,
            (project, _) => defect == "script"
                ? throw new IOException("controlled script failure")
                : Task.FromResult(defect == "mismatch"
                    ? new PnpmScripts("src/wrong", new Dictionary<string, string>())
                    : NodeScenario.Scripts(project)),
            (_, _) => throw new IOException("controlled version failure"), CancellationToken.None);

        if (defect == "mismatch")
            await Assert.ThrowsExactlyAsync<InvalidDataException>(Plan);
        else
            await Assert.ThrowsExactlyAsync<IOException>(Plan);
    }

    [TestMethod]
    public async Task PlanNativeHydrationCancellationReturnsNoPartialPlan()
    {
        NodeRevisionInputs basis = NodeScenario.Inputs(NodeScenario.Basis,
            NodeScenario.Project("src/a"));
        NodeRevisionInputs candidate = NodeScenario.Inputs(NodeScenario.Candidate,
            NodeScenario.Project("src/a"));
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        await Assert.ThrowsExactlyAsync<OperationCanceledException>(() =>
            NodeFactsAssembler.PlanAsync(basis, candidate, ["src/a/index.js"], false,
                (_, _) => throw new InvalidOperationException("Cancelled work must not hydrate."),
                (_, _) => throw new InvalidOperationException("Cancelled work must not project."),
                cancellation.Token));
    }


    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task PlanAddedOrDeletedVersionConfigurationKeepsEndpointConsumers(bool deleted)
    {
        NodeRevisionInputs basis = NodeScenario.Inputs(NodeScenario.Basis,
            NodeScenario.Project("src/a"));
        NodeRevisionInputs candidate = NodeScenario.Inputs(NodeScenario.Candidate,
            NodeScenario.Project("src/a"));
        basis = basis with
        {
            Versions = [basis.Versions[0] with
                { ConfigurationCandidates = ["version.json", "src/a/version.json"] }],
        };
        candidate = candidate with
        {
            Versions = [candidate.Versions[0] with
                { ConfigurationCandidates = ["version.json", "src/a/version.json"] }],
        };
        NodeRevisionInputs withFile = deleted ? basis : candidate;
        withFile = withFile with
        {
            Revision = withFile.Revision with
            { Entries = [.. withFile.Revision.Entries, NodeScenario.File("version.json")] },
        };
        if (deleted) basis = withFile;
        else candidate = withFile;

        CiPlan plan = await NodeScenario.Plan(basis, candidate, "version.json");

        AssertProjectTargets(plan, "src/a");
        foreach (PlannedCheck check in plan.Checks)
        {
            Assert.Contains(new SelectionReason("version.json", NodeScenario.Basis, "src/a"),
                check.Reasons);
            Assert.Contains(new SelectionReason("version.json", NodeScenario.Candidate, "src/a"),
                check.Reasons);
        }
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task PlanMovedNamedProjectRetainsBasisReverseConsumers(bool sharedInput)
    {
        NodeRevisionInputs basis = NodeScenario.Inputs(NodeScenario.Basis,
            NodeScenario.Project("src/old-library") with { Name = "@example/library" },
            NodeScenario.Project("src/old-consumer", "src/old-library") with
            { Name = "@example/consumer" });
        NodeRevisionInputs candidate = NodeScenario.Inputs(NodeScenario.Candidate,
            NodeScenario.Project("tests/new-library") with { Name = "@example/library" },
            NodeScenario.Project("tests/new-consumer") with { Name = "@example/consumer" });
        string changed = sharedInput ? "external/removed.json" : "src/old-library/index.js";
        if (sharedInput)
            basis = basis with
            {
                Revision = basis.Revision with
                { Entries = [.. basis.Revision.Entries, NodeScenario.File(changed)] },
                OperationInputs = [new(changed, ["src/old-library"])],
            };

        CiPlan plan = await NodeScenario.Plan(basis, candidate, changed);

        AssertProjectTargets(plan, "tests/new-library", "tests/new-consumer");
        Assert.HasCount(4, plan.Checks);
        foreach (PlannedCheck check in plan.Checks)
            Assert.AreEqual(new(changed, NodeScenario.Basis, "npm:@example/library"),
                Assert.ContainsSingle(check.Reasons));
    }

    [TestMethod]
    public async Task PlanMovedNamedPackageUsesCandidateDirectories()
    {
        const string before = "src/old-package";
        const string after = "tests/new-package";
        NodeRevisionInputs basis = NodeScenario.Units(NodeScenario.Inputs(NodeScenario.Basis,
            NodeScenario.Project(before) with { Name = "@example/product" }),
            NodeScenario.Unit("product", ("package", before)));
        NodeRevisionInputs candidate = NodeScenario.Units(NodeScenario.Inputs(
            NodeScenario.Candidate, NodeScenario.Project(after) with { Name = "@example/product" }),
            NodeScenario.Unit("product", ("package", after)));

        CiPlan plan = await NodeFactsAssembler.PlanAsync(basis, candidate,
            [before + "/index.js"], false,
            (project, _) => project.Directory == after
                ? Task.FromResult(NodeScenario.Scripts(project))
                : throw new InvalidOperationException("Wrong script directory."),
            (project, _) => project.Directory == after ? Task.FromResult("1.2.3")
                : throw new InvalidOperationException("Wrong version directory."),
            CancellationToken.None);

        AssertProjectTargets(plan, after, "release/product/package");
        Assert.HasCount(5, plan.Checks);
        Assert.HasCount(3, plan.Checks.Where(check => check.Work.Package is not null));
        foreach (PlannedCheck check in plan.Checks)
        {
            Assert.AreEqual(new(before + "/index.js", NodeScenario.Basis,
                "npm:@example/product"), Assert.ContainsSingle(check.Reasons));
            if (check.Work.Package is { } package)
            {
                Assert.AreEqual(after, package.Directory);
                Assert.AreEqual(after + "/package.json", package.EntryPoint);
                Assert.AreEqual("1.2.3", package.ExpectedVersion);
            }
        }
    }

    [TestMethod]
    public async Task PlanRejectsAmbiguousNativeNames()
    {
        NodeRevisionInputs inputs = NodeScenario.Inputs(NodeScenario.Candidate,
            NodeScenario.Project("src/a") with { Name = "@example/same" },
            NodeScenario.Project("src/b") with { Name = "@example/same" });

        await Assert.ThrowsExactlyAsync<InvalidDataException>(() =>
            NodeScenario.Plan(inputs, inputs, "src/a/index.js"));
    }

    private static NodeRevisionInputs WithoutQuality(NodeRevisionInputs inputs, string project)
    {
        var quality = new Dictionary<string, QualitySelection?>(
            inputs.Quality)
        { [project] = null };
        return inputs with { Quality = quality };
    }

    private static void AssertProjectTargets(CiPlan plan, params string[] targets) =>
        CollectionAssert.AreEquivalent(targets,
            plan.Checks.Select(check => check.Work.Key.Target).Distinct().ToArray());
}
