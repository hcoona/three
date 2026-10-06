using WorkflowDelivery.CI;
using WorkflowDelivery.Repository;

namespace WorkflowDelivery.Tests.Repository;

[TestClass]
public sealed class DotNetFactsAssemblerTests(TestContext context)
{
    private static readonly string[] Projects = ["product/A.csproj", "product/B.csproj"];
    private static readonly string[] Dependency = ["product/B.csproj"];
    private static readonly string[] Owned = ["product/A.csproj", "product/code.cs"];
    [TestMethod]
    public async Task SelectionUsesExactOwnershipAndExplicitConsumers()
    {
        using var fixture = await DotNetEndpointFixture.CreateAsync(context.CancellationToken);
        DotNetRevisionInputs inputs = await fixture.BindAsync();
        DotNetSelection linked = DotNetFactsAssembler.Select(inputs, inputs,
            ["linked/data.txt"], false, [], []);
        Assert.AreEqual("product/A.csproj", Assert.ContainsSingle(linked.Reasons.Keys));
        DotNetSelection owned = DotNetFactsAssembler.Select(inputs, inputs,
            ["product/code.cs"], false, [], []);
        Assert.Contains("product/A.csproj", owned.Reasons.Keys);
        RepositoryFacts facts = linked.Request.Candidate;
        Assert.IsTrue(facts.Projects.All(project => project.OwnedPaths is not null &&
            !project.OwnedPaths.Contains("product/fixtures/example.txt", StringComparer.Ordinal)));
        DotNetSelection excluded = DotNetFactsAssembler.Select(inputs, inputs,
            ["product/fixtures/example.txt"], false, ["product/fixtures/example.txt"],
            ["product/fixtures/example.txt"]);
        Assert.IsEmpty(excluded.Reasons);
        Assert.IsTrue(facts.Projects.All(project => project.Checks.Length == 0 &&
            project.Origin == CheckOrigin.NativeRetained && project.QualityPreset is null));
    }

    [TestMethod]
    public async Task SelectionPreservesTraversalConsumersAndSameDirectoryProjects()
    {
        using var fixture = await DotNetEndpointFixture.CreateAsync(context.CancellationToken);
        DotNetRevisionInputs inputs = await fixture.BindAsync();
        DotNetSelection selection = DotNetFactsAssembler.Select(inputs, inputs,
            ["dirs.proj"], false, [], []);
        CollectionAssert.AreEquivalent(Projects,
            selection.Reasons.Keys.ToArray());
        Assert.HasCount(2, selection.Request.Candidate.Projects);
        Assert.IsTrue(selection.Request.Candidate.Projects.All(project =>
            project.Directory == "product" && project.Id != "dirs.proj"));
        ProjectFacts first = Assert.ContainsSingle(selection.Request.Candidate.Projects.Where(
            project => project.Id == "product/A.csproj"));
        CollectionAssert.AreEqual(Dependency, first.Dependencies);
        CollectionAssert.AreEquivalent(Owned,
            first.OwnedPaths!);
        Assert.HasCount(3, selection.CandidateNodes["product/A.csproj"]);
        Assert.HasCount(1, selection.CandidateNodes["product/A.csproj"]
            .Where(node => node.OuterBuild));
        SharedInput traversal = Assert.ContainsSingle(selection.Request.Candidate.SharedInputs
            .Where(input => input.Path == "dirs.proj"));
        CollectionAssert.AreEquivalent(selection.Reasons.Keys.ToArray(), traversal.Consumers);
    }

    [TestMethod]
    [DataRow(".editorconfig", "PotentialEditorConfigFiles")]
    [DataRow(".globalconfig", "GlobalAnalyzerConfigFiles")]
    public async Task LinkedConfigurationAdditionAndDeletionSelectConsumers(
        string file, string role)
    {
        using var fixture = await DotNetEndpointFixture.CreateAsync(context.CancellationToken);
        string path = "linked/" + file;
        DotNetRevisionInputs before = await WithCandidateAsync();
        await fixture.Repo.SetAsync(path, "root = true");
        await fixture.CommitAsync();
        DotNetRevisionInputs added = await WithCandidateAsync();
        DotNetSelection addition = DotNetFactsAssembler.Select(
            before, added, [path], false, [], []);
        Assert.Contains("product/A.csproj", addition.Reasons.Keys);
        Assert.AreEqual(path, Assert.ContainsSingle(added.Inputs.Where(input =>
            input.Native.Role == role)).Path);
        Assert.Contains(path, addition.Request.Basis.SharedInputs.Select(input => input.Path));
        await fixture.Repo.RemoveAsync(path);
        File.Delete(fixture.PathOf(path));
        await fixture.CommitAsync();
        DotNetRevisionInputs removed = await WithCandidateAsync();
        DotNetSelection deletion = DotNetFactsAssembler.Select(
            added, removed, [path], false, [], []);
        Assert.Contains("product/A.csproj", deletion.Reasons.Keys);
        Assert.IsTrue(deletion.Reasons["product/A.csproj"].Any(reason =>
            reason.Revision == added.Revision.Commit));
        Assert.Contains(path, deletion.Request.Candidate.SharedInputs.Select(input => input.Path));

        async Task<DotNetRevisionInputs> WithCandidateAsync()
        {
            fixture.Graph = fixture.Response();
            fixture.Graph = fixture.Graph with
            {
                Inputs = [.. fixture.Graph.Inputs,
                new(fixture.PathOf(path), role, "Evaluation", fixture.Graph.Nodes[2].Identity)]
            };
            return await fixture.BindAsync();
        }
    }

    [TestMethod]
    public async Task RemovedProjectSelectsSurvivingDependentAndCandidateVariants()
    {
        using var fixture = await DotNetEndpointFixture.CreateAsync(context.CancellationToken);
        DotNetRevisionInputs basis = await fixture.BindAsync();
        await fixture.Repo.RemoveAsync("product/B.csproj");
        File.Delete(fixture.PathOf("product/B.csproj"));
        await fixture.CommitAsync();
        fixture.Graph = fixture.Response(includeB: false);
        DotNetRevisionInputs candidate = await fixture.BindAsync();
        DotNetSelection selected = DotNetFactsAssembler.Select(basis, candidate,
            ["product/B.csproj"], false, [], []);

        Assert.Contains("product/A.csproj", selected.Reasons.Keys);
        Assert.IsFalse(selected.CandidateNodes.ContainsKey("product/B.csproj"));
        Assert.HasCount(3, selected.CandidateNodes["product/A.csproj"]);
        Assert.IsEmpty(selected.Request.Candidate.Projects.Single().Dependencies);
        Assert.IsTrue(selected.Reasons["product/A.csproj"].Any(reason =>
            reason.Project == "product/B.csproj" && reason.Revision == basis.Revision.Commit));
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task SelectionRejectsUnknownRequiredFactsEvenInFullMode(bool full)
    {
        using var fixture = await DotNetEndpointFixture.CreateAsync(context.CancellationToken);
        DotNetRevisionInputs inputs = await fixture.BindAsync();
        InvalidDataException failure = Assert.ThrowsExactly<InvalidDataException>(() =>
            DotNetFactsAssembler.Select(inputs, inputs, ["unowned/unknown.txt"], full, [], []));
        Assert.Contains("Unresolved changed path", failure.Message);
        Assert.Contains("unowned/unknown.txt", failure.Message);
        DotNetSelection complete = DotNetFactsAssembler.Select(inputs, inputs, [], true, [], []);
        Assert.HasCount(2, complete.Reasons);
        Assert.IsTrue(complete.Request.Candidate.Projects
            .All(project => project.Checks.Length == 0));
    }

    [TestMethod]
    public async Task RootProjectSelectionPreservesExactOwnership()
    {
        using var fixture = await DotNetEndpointFixture.CreateAsync(context.CancellationToken);
        foreach (string path in new[] { "root.csproj", "root-data.txt", "root-excluded.txt" })
            await fixture.Repo.SetAsync(path);
        await fixture.CommitAsync();
        fixture.Graph = fixture.Response();
        DotNetNodeIdentity identity = fixture.Identity("root.csproj", "net10.0");
        var node = new DotNetGraphNode(identity, false, new("Debug", "net10.0", ""), "", "",
            [fixture.PathOf("root.csproj"), fixture.PathOf("root-data.txt")], "None");
        fixture.Graph = fixture.Graph with
        {
            Nodes = [.. fixture.Graph.Nodes, node],
            Edges = [.. fixture.Graph.Edges, new(fixture.Graph.Nodes[0].Identity, identity)],
            Inputs = [.. fixture.Graph.Inputs,
                new(identity.Project, "Project", "Evaluation", identity),
                new(fixture.PathOf("linked/data.txt"), "EmbeddedResource", "Evaluation", identity)],
        };
        DotNetRevisionInputs inputs = await fixture.BindAsync();
        DotNetSelection owned = DotNetFactsAssembler.Select(inputs, inputs,
            ["root-data.txt"], false, [], []);
        Assert.AreEqual("root.csproj", Assert.ContainsSingle(owned.Reasons.Keys));
        ProjectFacts root = Assert.ContainsSingle(owned.Request.Candidate.Projects.Where(project =>
            project.Id == "root.csproj"));
        Assert.AreEqual(".", root.Directory);
        Assert.IsNotNull(root.OwnedPaths);
        Assert.Contains("root-data.txt", root.OwnedPaths);
        Assert.IsFalse(root.OwnedPaths.Contains("root-excluded.txt", StringComparer.Ordinal));
        DotNetSelection explicitInput = DotNetFactsAssembler.Select(inputs, inputs,
            ["linked/data.txt"], false, [], []);
        Assert.Contains("root.csproj", explicitInput.Reasons.Keys);
        DotNetSelection excluded = DotNetFactsAssembler.Select(inputs, inputs,
            ["root-excluded.txt"], false, ["root-excluded.txt"], ["root-excluded.txt"]);
        Assert.IsEmpty(excluded.Reasons);
    }

    [TestMethod]
    [DataRow("changed")]
    [DataRow("owned")]
    [DataRow("shared")]
    [DataRow("unaffected")]
    [DataRow("directory-segment")]
    public async Task RootDirectoryAdmissionKeepsDotFileCoordinatesInvalid(string defect)
    {
        using var fixture = await DotNetEndpointFixture.CreateAsync(context.CancellationToken);
        DotNetRevisionInputs inputs = await fixture.BindAsync();
        RepositoryFacts facts = DotNetFactsAssembler.Assemble(inputs, []);
        facts = defect switch
        {
            "owned" => facts with
            {
                Projects = [facts.Projects[0] with { OwnedPaths = ["."] },
                .. facts.Projects[1..]]
            },
            "shared" => facts with
            {
                SharedInputs = [.. facts.SharedInputs,
                new(".", [facts.Projects[0].Id])]
            },
            "unaffected" => facts with { UnaffectedPaths = ["."] },
            "directory-segment" => facts with
            {
                Projects = [facts.Projects[0] with
                { Directory = "product/./nested" }, .. facts.Projects[1..]]
            },
            _ => facts,
        };
        Assert.ThrowsExactly<InvalidDataException>(() => ImpactPlanner.SelectProjects(
            new(facts, facts, defect == "changed" ? ["."] : [], false)));
    }
}
