using WorkflowDelivery.CI;
using WorkflowDelivery.Repository;
using System.Text.Json;

namespace WorkflowDelivery.Tests.Repository;

[TestClass]
public sealed class RubyNodeInputConsumersTests
{
    private const string Consumer = "node/client";
    private const string Dependent = "node/dependent";
    private const string Unrelated = "node/unrelated";
    private const string Producer = "ruby/native";
    private static readonly string[] Checks = ["node/project-build-v1", "node/project-test-v1"];

    [TestMethod]
    public async Task PairedCallerContributionSurvivesJsonWithoutLosingBasisConsumption()
    {
        (NodeRevisionInputs before, RubyFactsEndpoint rubyBefore) = Endpoint(NodeScenario.Basis);
        (NodeRevisionInputs after, RubyFactsEndpoint rubyAfter) = Endpoint(NodeScenario.Candidate,
            removed: true);
        var request = new NodeGroupRequest(new(Path.GetFullPath("basis"), NodeScenario.Basis),
            new(Path.GetFullPath("candidate"), NodeScenario.Candidate), false,
            Path.GetFullPath("scope.json"), null,
            new(new(Path.GetFullPath("control.dll"), Path.GetFullPath("control.proj"),
                Path.GetFullPath("request.json"), Path.GetFullPath("response.json"), 900),
                Path.GetFullPath("control.dll")),
            new(rubyBefore, rubyAfter));
        NodeGroupRequest received = JsonSerializer.Deserialize(JsonSerializer.Serialize(request,
            TransferJson.Default.NodeGroupRequest), TransferJson.Default.NodeGroupRequest)!;
        (before, after) = NodeGroupReader.WithRubyInputs(before, after, received.Ruby!);
        const string path = Producer + "/lib.rb";
        NodeGroupPlan plan = await Plan(before, after, path);
        Assert.HasCount(2, plan.Adopted.Checks);
        var reason = new SelectionReason(path, NodeScenario.Basis, Consumer);
        foreach (PlannedCheck check in plan.Adopted.Checks)
            Assert.AreEqual(reason, Assert.ContainsSingle(check.Reasons));
        Assert.AreEqual(reason, Assert.ContainsSingle(
            Assert.ContainsSingle(plan.Retained).Reasons));
    }

    [TestMethod]
    [DataRow("ruby/native/lib.rb")]
    [DataRow("shared/version.json")]
    [DataRow("ruby/native/workflow-delivery.quality.yml")]
    [DataRow("node/client/Gemfile")]
    [DataRow("node/client/Gemfile.lock")]
    public async Task NativeBundleInputsSelectConsumerAndPnpmDependents(string path)
    {
        (NodeRevisionInputs before, RubyFactsEndpoint rubyBefore) = Endpoint(NodeScenario.Basis);
        (NodeRevisionInputs after, RubyFactsEndpoint rubyAfter) = Endpoint(NodeScenario.Candidate);
        NodeGroupPlan plan = await Plan(RubyNodeInputConsumers.Join(before, rubyBefore),
            RubyNodeInputConsumers.Join(after, rubyAfter), path);

        CollectionAssert.AreEquivalent(Checks,
            plan.Adopted.Checks.Select(check => check.Work.Key.Check).ToArray());
        RetainedNodeSelection retained = Assert.ContainsSingle(plan.Retained);
        Assert.AreEqual(Dependent, retained.Directory);
        CollectionAssert.AreEquivalent(new[] {
            new SelectionReason(path, NodeScenario.Basis, Consumer),
            new SelectionReason(path, NodeScenario.Candidate, Consumer) }, retained.Reasons);
        foreach (PlannedCheck check in plan.Adopted.Checks)
            CollectionAssert.AreEquivalent(retained.Reasons, check.Reasons);
        Assert.IsFalse(ResultCollector.Collect(plan.Adopted,
            [new(NodeScenario.Candidate, plan.Adopted.Checks[0].Work.Key,
                CheckStatus.Passed)]).Satisfied);
    }

    [TestMethod]
    public async Task RemovedProducerKeepsBasisReasons()
    {
        (NodeRevisionInputs before, RubyFactsEndpoint rubyBefore) = Endpoint(NodeScenario.Basis);
        (NodeRevisionInputs after, RubyFactsEndpoint rubyAfter) = Endpoint(NodeScenario.Candidate,
            removed: true);
        const string path = Producer + "/lib.rb";
        NodeGroupPlan plan = await Plan(RubyNodeInputConsumers.Join(before, rubyBefore),
            RubyNodeInputConsumers.Join(after, rubyAfter), path);

        Assert.HasCount(2, plan.Adopted.Checks);
        var reason = new SelectionReason(path, NodeScenario.Basis, Consumer);
        foreach (PlannedCheck check in plan.Adopted.Checks)
            Assert.AreEqual(reason, Assert.ContainsSingle(check.Reasons));
        Assert.AreEqual(reason, Assert.ContainsSingle(
            Assert.ContainsSingle(plan.Retained).Reasons));
    }

    [TestMethod]
    [DataRow("ruby/native/nested/lib.rb")]
    [DataRow("ruby/unrelated/lib.rb")]
    public async Task NestedAndUnrelatedInputsDoNotSelectParentConsumer(string path)
    {
        (NodeRevisionInputs before, RubyFactsEndpoint rubyBefore) = Endpoint(NodeScenario.Basis);
        (NodeRevisionInputs after, RubyFactsEndpoint rubyAfter) = Endpoint(NodeScenario.Candidate);
        NodeGroupPlan plan = await Plan(RubyNodeInputConsumers.Join(before, rubyBefore),
            RubyNodeInputConsumers.Join(after, rubyAfter), path, rubyResponsibility: true);
        Assert.IsEmpty(plan.Adopted.Checks);
        Assert.IsEmpty(plan.Retained);
    }

    [TestMethod]
    public void RubyOwnedBundlesKeepTheirEcosystem()
    {
        (NodeRevisionInputs node, RubyFactsEndpoint ruby) = Endpoint(NodeScenario.Candidate);
        ruby = ruby with { Revision = ruby.Revision with { Entries =
            [.. ruby.Revision.Entries, NodeScenario.File(Producer + "/Gemfile"),
                NodeScenario.File(Producer + "/Gemfile.lock")] }, Native = ruby.Native with {
            Graph = ruby.Native.Graph with { Bundles = [.. ruby.Native.Graph.Bundles,
                new(Producer + "/Gemfile", Producer,
                    [Producer + "/Gemfile", Producer + "/Gemfile.lock"], [])] } } };
        node = node with { Revision = ruby.Revision,
            OperationInputs = [new("existing/input", [Unrelated])] };
        NodeRevisionInputs result = RubyNodeInputConsumers.Join(node, ruby);
        Assert.IsFalse(result.OperationInputs.Any(input => input.Path == Producer + "/Gemfile"));
        CollectionAssert.AreEqual(new[] { Unrelated }, result.OperationInputs.Single(input =>
            input.Path == "existing/input").Consumers);
    }

    [TestMethod]
    [DataRow("revision")]
    [DataRow("consumer")]
    [DataRow("producer")]
    public void InvalidEndpointOrUnmappedConsumerCannotYieldInputs(string defect)
    {
        (NodeRevisionInputs node, RubyFactsEndpoint ruby) = Endpoint(NodeScenario.Candidate);
        if (defect == "revision")
            ruby = ruby with { Revision = ruby.Revision with { Commit = NodeScenario.Basis } };
        if (defect == "consumer")
            node = node with { Graph = node.Graph with { Projects = node.Graph.Projects
                .Where(project => project.Directory != Consumer).ToArray() } };
        if (defect == "producer")
            ruby = ruby with { Native = ruby.Native with { Graph = ruby.Native.Graph with {
                Bundles = [ruby.Native.Graph.Bundles[0] with { Producers = ["missing"] }] } } };
        Assert.ThrowsExactly<InvalidDataException>(() => RubyNodeInputConsumers.Join(node, ruby));
    }

    private static (NodeRevisionInputs Node, RubyFactsEndpoint Ruby) Endpoint(string revision,
        bool removed = false)
    {
        NodeRevisionInputs node = NodeScenario.Inputs(revision,
            NodeScenario.Project(Consumer), NodeScenario.Project(Dependent, Consumer),
            NodeScenario.Project(Unrelated));
        string[] producers = removed ? [] : [Producer, Producer + "/nested", "ruby/unrelated"];
        string[] paths = [Consumer + "/Gemfile", Consumer + "/Gemfile.lock",
            "shared/version.json", .. producers.SelectMany(directory => new[] {
                directory + "/native.gemspec", directory + "/lib.rb" })];
        GitRevision git = node.Revision with { Entries = [.. node.Revision.Entries,
            .. paths.Select(NodeScenario.File)] };
        var graph = new RubyDependencyGraph(producers.Select(directory =>
            new RubyProjectDependencies(directory, directory, [])).ToArray(),
            [new(Consumer + "/Gemfile", Consumer,
                [Consumer + "/Gemfile", Consumer + "/Gemfile.lock"],
                removed ? [] : [Producer])]);
        SharedInput[] inputs = removed ? [] : [.. producers.Select(directory =>
            new SharedInput(directory + "/lib.rb", [directory])),
            new("shared/version.json", [Producer]),
            new(Producer + "/workflow-delivery.quality.yml", [Producer])];
        RubyFactsEndpoint ruby = new(git, new(graph, inputs), [], []);
        return (node with { Revision = git }, ruby);
    }

    private static Task<NodeGroupPlan> Plan(NodeRevisionInputs before, NodeRevisionInputs after,
        string path, bool rubyResponsibility = false)
    {
        ScopeCoordinate Row(GitRevision revision)
        {
            GitEntry? entry = revision.Entries.SingleOrDefault(entry => entry.Path == path);
            ScopeResponsibility[] reasons = rubyResponsibility && entry is not null
                ? [new("ruby", path, "native-source", [path])] : [];
            return new(path, entry is not null, entry?.Mode, reasons);
        }
        var scope = new ScopeInputs(new(before.Revision.Commit, after.Revision.Commit, false),
            [path], [Row(before.Revision)], [Row(after.Revision)]);
        return NodeGroupPlanning.PlanAsync(before, after, scope, Consumer,
            (project, _) => Task.FromResult(NodeScenario.Scripts(project)),
            (_, _) => throw new InvalidOperationException("Unneeded package version."),
            CancellationToken.None);
    }
}
