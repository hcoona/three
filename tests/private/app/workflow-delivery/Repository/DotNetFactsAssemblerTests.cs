using WorkflowDelivery.CI;
using WorkflowDelivery.Repository;

namespace WorkflowDelivery.Tests.Repository;

[TestClass]
public sealed class DotNetFactsAssemblerTests(TestContext context)
{
    private static readonly string[] Projects = ["product/A.csproj", "product/B.csproj"];
    private static readonly string[] Dependency = ["product/B.csproj"];
    private static readonly string[] Owned = ["product/A.csproj", "product/code.cs"];
    private static readonly string[] VersionDirectories = [".", "product"];
    private static readonly string[] NativeFrameworks = ["net10.0", "net9.0"];

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task EffectiveNativeVersionBaseSelectsRootInputsDespiteLocalShadow(
        bool mixedVariants)
    {
        using var fixture = await DotNetEndpointFixture.CreateAsync(context.CancellationToken);
        const string rootInput = "shared/root-version-input.txt";
        await fixture.Repo.SetAsync(rootInput);
        await fixture.Repo.SetAsync("version.json", """
            {"version":"1.0","pathFilters":["./shared/root-version-input.txt"]}
            """);
        await fixture.Repo.SetAsync("product/version.json", """
            {"version":"2.0","pathFilters":["./code.cs"]}
            """);
        await fixture.CommitAsync();
        fixture.Graph = fixture.Response();
        fixture.Graph = fixture.Graph with
        {
            Nodes = fixture.Graph.Nodes.Select(node => node.Identity.Project ==
                fixture.PathOf("dirs.proj") ? node with { VersionBaseDirectory = fixture.Root } :
                node.Identity.Project == fixture.PathOf("product/A.csproj") ? node with
                {
                    VersionBaseDirectory = mixedVariants &&
                        node.Dimension.TargetFramework != "net9.0"
                        ? fixture.PathOf("product") : fixture.Root,
                } : node).ToArray(),
        };
        DotNetRevisionInputs inputs = await fixture.BindAsync();
        CollectionAssert.AreEquivalent(VersionDirectories,
            inputs.Versions.Select(version => version.Directory).Distinct().ToArray());
        Assert.IsFalse(inputs.SelectionInputs.Any(input =>
            input.Consumers.Contains("dirs.proj", StringComparer.Ordinal)));
        Assert.Contains(rootInput, Assert.ContainsSingle(inputs.Versions.Where(version =>
            version.Directory == ".")).Paths);
        Assert.DoesNotContain(rootInput, Assert.ContainsSingle(inputs.Versions.Where(version =>
            version.Directory == "product")).Paths);
        DotNetSelection selected = DotNetFactsAssembler.Select(inputs, inputs,
            [rootInput], false, [], []);
        Assert.AreEqual("product/A.csproj", Assert.ContainsSingle(selected.Reasons.Keys));
        SharedInput root = Assert.ContainsSingle(selected.Request.Candidate.SharedInputs.Where(
            input => input.Path == rootInput));
        Assert.AreEqual("product/A.csproj", Assert.ContainsSingle(root.Consumers));
        SharedInput shadow = Assert.ContainsSingle(selected.Request.Candidate.SharedInputs.Where(
            input => input.Path == "product/version.json"));
        CollectionAssert.AreEquivalent(mixedVariants ? Projects : Dependency, shadow.Consumers);
        Assert.IsTrue(selected.Request.Candidate.Projects.All(project =>
            project.Directory == "product"));
        CiPlan plan = DotNetFactsAssembler.Complete(selected, context.CancellationToken);
        Assert.HasCount(2, plan.Checks);
        Assert.IsTrue(plan.Checks.All(check => check.Work.Key.Target == "product/A.csproj"));
        CollectionAssert.AreEquivalent(NativeFrameworks, plan.Checks.Select(check =>
            DotNetChecks.Read(check.Work, fixture.Root).Dimension.TargetFramework).ToArray());
        Assert.IsTrue(plan.Checks.All(check => check.Work.Dimensions.Count == 3 &&
            !check.Work.Dimensions.ContainsKey("VersionBaseDirectory")));
    }

    [TestMethod]
    public async Task TraversalSelfVersionDoesNotWidenBusinessSelection()
    {
        using var fixture = await DotNetEndpointFixture.CreateAsync(context.CancellationToken);
        const string unrelated = "node/app/index.ts";
        const string configuration = "Directory.Build.props";
        string[] otherOwned = [unrelated];
        await fixture.Repo.SetAsync(unrelated, "export const value = 1;");
        await fixture.Repo.SetAsync(configuration, "<Project />");
        await fixture.Repo.SetAsync("version.json", """
            {"version":"1.0","pathFilters":[":!product/fixtures"]}
            """);
        await fixture.Repo.SetAsync("product/version.json", """
            {"version":"2.0","inherit":false,"pathFilters":["./code.cs"]}
            """);
        await fixture.CommitAsync();
        fixture.Graph = fixture.Response();
        fixture.Graph = fixture.Graph with
        {
            Nodes = fixture.Graph.Nodes.Select(node => node with
            {
                VersionBaseDirectory = node.Identity.Project == fixture.PathOf("dirs.proj")
                    ? fixture.Root : fixture.PathOf("product"),
            }).ToArray(),
            Inputs = [.. fixture.Graph.Inputs, new(fixture.PathOf(configuration), "Import",
                "Evaluation", fixture.Identity("dirs.proj"))],
        };
        NbgvInputs traversalVersion = new NbgvInputReader(fixture.Root).Read(fixture.Revision,
            ".", context.CancellationToken);
        Assert.Contains(unrelated, traversalVersion.Paths);
        DotNetRevisionInputs inputs = await fixture.BindAsync();
        NbgvInputs businessVersion = Assert.ContainsSingle(inputs.Versions);
        Assert.AreEqual("product", businessVersion.Directory);
        Assert.Contains("product/code.cs", businessVersion.Paths);
        Assert.DoesNotContain(unrelated, businessVersion.Paths);
        Assert.IsFalse(inputs.SelectionInputs.Any(input =>
            input.Consumers.Contains("dirs.proj", StringComparer.Ordinal)));
        Assert.IsFalse(inputs.SelectionInputs.Any(input => input.Path == unrelated));
        DotNetSelection excluded = DotNetFactsAssembler.Select(inputs, inputs,
            otherOwned, false, otherOwned, otherOwned);
        Assert.IsEmpty(excluded.Reasons);
        Assert.IsFalse(excluded.Request.Candidate.SharedInputs.Any(input =>
            input.Path == unrelated));
        Assert.IsEmpty(DotNetFactsAssembler.Complete(excluded, context.CancellationToken).Checks);
        foreach (string path in new[] { "dirs.proj", configuration, "product/code.cs" })
        {
            DotNetSelection required = DotNetFactsAssembler.Select(inputs, inputs,
                [path], false, otherOwned, otherOwned);
            CollectionAssert.AreEquivalent(Projects, required.Reasons.Keys.ToArray());
            SharedInput shared = Assert.ContainsSingle(required.Request.Candidate.SharedInputs
                .Where(input => input.Path == path));
            CollectionAssert.AreEquivalent(Projects, shared.Consumers);
        }
        foreach (string path in new[] { "dirs.proj", configuration })
        {
            DotNetBoundInput native = Assert.ContainsSingle(inputs.Inputs.Where(input =>
                input.Path == path));
            Assert.AreEqual("dirs.proj", Path.GetRelativePath(fixture.Root,
                native.Native.Consumer.Project));
            Assert.AreEqual(path == configuration ? "Import" : "Project", native.Native.Role);
            Assert.AreEqual("Evaluation", native.Native.Stage);
        }
        DotNetSelection linked = DotNetFactsAssembler.Select(inputs, inputs,
            ["linked/data.txt"], false, otherOwned, otherOwned);
        Assert.AreEqual("product/A.csproj", Assert.ContainsSingle(linked.Reasons.Keys));
        SharedInput linkedInput = Assert.ContainsSingle(linked.Request.Candidate.SharedInputs
            .Where(input => input.Path == "linked/data.txt"));
        Assert.AreEqual("product/A.csproj", Assert.ContainsSingle(linkedInput.Consumers));
    }

    [TestMethod]
    [DataRow("shared/nbgv-aux.targets", "Import")]
    [DataRow("shared/aux-input.txt", "AdditionalFiles")]
    public async Task NbgvAuxiliaryInputChangeSelectsOnlyOwningBusinessConsumer(
        string path, string role)
    {
        using var fixture = await DotNetEndpointFixture.CreateAsync(context.CancellationToken);
        await fixture.Repo.SetAsync(path, "original committed auxiliary input");
        await fixture.CommitAsync();
        DotNetRevisionInputs basis = await BindAuxiliaryAsync();
        await fixture.Repo.SetAsync(path, "changed committed auxiliary input");
        await fixture.CommitAsync();
        DotNetRevisionInputs candidate = await BindAuxiliaryAsync();
        DotNetSelection selected = DotNetFactsAssembler.Select(basis, candidate,
            [path], false, [], []);
        Assert.AreEqual("product/A.csproj", Assert.ContainsSingle(selected.Reasons.Keys));
        Assert.IsTrue(selected.Reasons["product/A.csproj"].Any(reason => reason.Path == path));
        SharedInput input = Assert.ContainsSingle(selected.Request.Candidate.SharedInputs.Where(
            input => input.Path == path));
        Assert.AreEqual("product/A.csproj", Assert.ContainsSingle(input.Consumers));
        DotNetBoundInput[] bound = candidate.Inputs.Where(input => input.Path == path).ToArray();
        Assert.HasCount(3, bound);
        Assert.IsTrue(bound.All(input => input.Native.Role == role &&
            input.Native.Provider is null && input.Native.Consumer.Project ==
                fixture.PathOf("product/A.csproj")));
        CiPlan plan = DotNetFactsAssembler.Complete(selected, context.CancellationToken);
        Assert.HasCount(2, plan.Checks);
        Assert.IsTrue(plan.Checks.All(check => check.Work.Key.Target == "product/A.csproj"));
        CollectionAssert.AreEquivalent(NativeFrameworks, plan.Checks.Select(check =>
            DotNetChecks.Read(check.Work, fixture.Root).Dimension.TargetFramework).ToArray());

        async Task<DotNetRevisionInputs> BindAuxiliaryAsync()
        {
            fixture.Graph = fixture.Response();
            fixture.Graph = fixture.Graph with
            {
                Inputs = [.. fixture.Graph.Inputs, .. fixture.Graph.Nodes.Where(node =>
                    node.Identity.Project == fixture.PathOf("product/A.csproj")).Select(node =>
                    new DotNetGraphInput(fixture.PathOf(path), role, "Evaluation", node.Identity))],
            };
            return await fixture.BindAsync();
        }
    }

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
