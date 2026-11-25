using System.Text.Json;
using WorkflowDelivery.CI;
using WorkflowDelivery.Platform;
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
    private const string Declaration = "product/workflow-delivery.release-unit.yml";
    private const string LinkedInput = "linked/data.txt";
    private static DotNetEndpointFixture? shared;

    [ClassInitialize]
    public static async Task InitializeAsync(TestContext classContext) =>
        shared = await DotNetEndpointFixture.CreateAsync(classContext.CancellationToken);

    [ClassCleanup]
    public static void Cleanup() => shared?.Dispose();

    private CiPlan Plan(DotNetRevisionInputs basis, DotNetRevisionInputs candidate,
        params string[] changed) => DotNetFactsAssembler.Complete(
            DotNetFactsAssembler.Select(basis, candidate, changed, false, [], []),
            context.CancellationToken);

    [TestMethod]
    public async Task ImportedInputSelectsOnlyItsConsumersAndTransfersNativeVariants()
    {
        DotNetEndpointFixture fixture = shared!;
        DotNetGraphResponse graph = fixture.Response();
        const string nativeValue = "space %3B percent%25 equals=tail";
        DotNetGraphNode[] nodes = graph.Nodes.Select(node =>
        {
            if (node.Identity.Project != fixture.PathOf("product/A.csproj") || node.OuterBuild)
                return node;
            node.Identity.Globals.Add("IsGraphBuild", "true");
            node.Identity.Globals.Add("NativeDetail", nativeValue);
            return node with { TestCapability = "MTP" };
        }).ToArray();
        DotNetGraphInput input = new(fixture.PathOf(LinkedInput), "Import", "Evaluation",
            nodes[2].Identity);
        DotNetRevisionInputs native = await fixture.BindAsync(graph with
        { Nodes = nodes, Inputs = [.. graph.Inputs.Where(item => item.Path != input.Path), input] },
            context.CancellationToken);
        RepositoryFacts facts = DotNetFactsAssembler.Assemble(native, ["docs/guide.md"]);
        var request = new PlanRequest(facts, facts, [LinkedInput, "docs/guide.md"], false);
        CiPlan plan = DotNetFactsAssembler.Complete(DotNetFactsAssembler.Select(request, native),
            context.CancellationToken);
        CiPlan transferred = JsonSerializer.Deserialize(JsonSerializer.Serialize(plan,
            TransferJson.Default.CiPlan), TransferJson.Default.CiPlan)!;

        Assert.AreEqual(native.Revision.Commit, transferred.Candidate);
        string[] expected =
        [
            "net10.0:" + DotNetChecks.Build, "net9.0:" + DotNetChecks.Build,
            "net10.0:" + DotNetChecks.Test, "net9.0:" + DotNetChecks.Test
        ];
        var variants = new List<string>();
        foreach (PlannedCheck item in transferred.Checks)
        {
            Assert.AreEqual("product/A.csproj", item.Work.Key.Target);
            Assert.AreEqual(CheckOrigin.NativeRetained, Assert.ContainsSingle(item.Origins));
            Assert.IsEmpty(item.QualityPresets);
            Assert.AreEqual(new SelectionReason(LinkedInput, native.Revision.Commit,
                "product/A.csproj"), Assert.ContainsSingle(item.Reasons));
            DotNetGraphNode restored = DotNetChecks.Read(item.Work, fixture.Root);
            variants.Add(restored.Dimension.TargetFramework + ":" + item.Work.Key.Check);
            Assert.AreEqual("true", restored.Identity.Globals["IsGraphBuild"]);
            Assert.AreEqual(nativeValue, restored.Identity.Globals["NativeDetail"]);
            Assert.AreEqual(restored.Dimension.TargetFramework,
                restored.Identity.Globals["TargetFramework"]);
            Assert.AreEqual("", restored.Dimension.RuntimeIdentifier);
            Assert.IsFalse(restored.OuterBuild);
            if (item.Work.Key.Check == DotNetChecks.Test)
                Assert.AreEqual(item.Work.Key with { Check = DotNetChecks.Build },
                    Assert.ContainsSingle(item.Work.Prerequisites));
        }
        CollectionAssert.AreEquivalent(expected, variants.ToArray());
    }

    [TestMethod]
    [DataRow("missing", "Dangling/substituted complete native identity")]
    [DataRow("outer-only", "no native inner identity")]
    [DataRow("duplicate", "Duplicate complete native identity")]
    public async Task IncompleteNativeIdentitiesCannotProduceAPlan(string defect, string detail)
    {
        DotNetEndpointFixture fixture = shared!;
        DotNetGraphResponse graph = fixture.Response();
        string project = fixture.PathOf("product/A.csproj");
        if (defect == "outer-only")
        {
            bool Keep(DotNetNodeIdentity identity) =>
                identity.Project != project || !identity.Globals.ContainsKey("TargetFramework");
            DotNetNodeIdentity outer = graph.Nodes.Single(node =>
                node.Identity.Project == project && node.OuterBuild).Identity;
            graph = graph with
            {
                Nodes = graph.Nodes.Where(node => Keep(node.Identity)).ToArray(),
                Edges = graph.Edges.Where(edge => Keep(edge.Consumer) && Keep(edge.Dependency))
                    .ToArray(),
                Inputs = graph.Inputs.Select(input => input.Path == fixture.PathOf(LinkedInput)
                    ? input with { Consumer = outer } : input)
                    .Where(input => Keep(input.Consumer)).ToArray()
            };
        }
        else
        {
            graph = graph with
            {
                Nodes = defect == "duplicate" ? [.. graph.Nodes, graph.Nodes[2]] :
                    graph.Nodes.Where(node => node.Identity.Project != project).ToArray()
            };
        }

        InvalidDataException failure = await Assert.ThrowsExactlyAsync<InvalidDataException>(
            async () =>
            {
                DotNetRevisionInputs native = await fixture.BindAsync(graph,
                    context.CancellationToken);
                Plan(native, native, LinkedInput);
            });
        Assert.Contains(detail, failure.Message);
    }

    [TestMethod]
    public async Task AdoptedCheckCannotSilentlyBecomeRetainedBuildWork()
    {
        using var fixture = await DotNetEndpointFixture.CreateAsync(context.CancellationToken);
        await fixture.Repo.SetAsync("product/workflow-delivery.quality.yml", """
            schema: workflow-delivery/v3/quality-selection
            ecosystems:
              dotnet:
                preset: dotnet/package-quality
            """);
        await fixture.CommitAsync();
        DotNetRevisionInputs native = await fixture.BindAsync(fixture.Response());

        InvalidDataException failure = Assert.ThrowsExactly<InvalidDataException>(() =>
            Plan(native, native, LinkedInput));
        Assert.Contains("native package adapter", failure.Message);
        Assert.Contains("product/A.csproj", failure.Message);
    }

    [TestMethod]
    public async Task DeletedReleaseDeclarationStillSelectsEveryBasisMember()
    {
        using var fixture = await DotNetEndpointFixture.CreateAsync(context.CancellationToken);
        await fixture.Repo.SetAsync(Declaration, Unit("product", "A.csproj", "B.csproj"));
        await fixture.CommitAsync();
        DotNetRevisionInputs basis = await fixture.BindAsync(fixture.Response());
        foreach (string path in new[] { "product/A.csproj", Declaration })
            CollectionAssert.AreEquivalent(Projects, Plan(basis, basis, path).Checks
                .Select(check => check.Work.Key.Target).Distinct().ToArray());

        await fixture.Repo.RemoveAsync(Declaration);
        File.Delete(fixture.PathOf(Declaration));
        await fixture.CommitAsync();
        DotNetRevisionInputs candidate = await fixture.BindAsync(fixture.Response());
        CiPlan plan = Plan(basis, candidate, Declaration);

        CollectionAssert.AreEquivalent(Projects, plan.Checks.Select(check => check.Work.Key.Target)
            .Distinct().ToArray());
        Assert.IsTrue(plan.Checks.All(check => check.Work.Key.Check == DotNetChecks.Build &&
            check.Work.Package is null && check.Reasons.Any(reason =>
                reason.Path == Declaration && reason.Revision == basis.Revision.Commit)));
    }

    [TestMethod]
    [DataRow("missing")]
    [DataRow("multiple")]
    [DataRow("duplicate-unit")]
    public async Task InvalidReleaseAssociationCannotProduceAnyPlan(string defect)
    {
        using var fixture = await DotNetEndpointFixture.CreateAsync(context.CancellationToken);
        await fixture.Repo.SetAsync(Declaration,
            Unit("product", defect == "missing" ? "Absent.csproj" : "A.csproj"));
        if (defect != "missing")
            await fixture.Repo.SetAsync("workflow-delivery.release-unit.yml",
                Unit(defect == "multiple" ? "other" : "product", "product/A.csproj"));
        await fixture.CommitAsync();
        DotNetRevisionInputs inputs = await fixture.BindAsync(fixture.Response());

        Assert.ThrowsExactly<InvalidDataException>(() => Plan(inputs, inputs, Declaration));
    }

    private static string Unit(string id, params string[] entries) =>
        "schema: workflow-delivery/v3/release-unit\nrelease-unit: " + id + "\nbuilds:\n" +
        string.Concat(entries.Select((entry, index) => "  - id: package-" + index +
            "\n    definition: dotnet/nuget-package-v1\n    entry-point: " + entry +
            "\n    outputs:\n      - id: archive-" + index +
            "\n        role: primary-package\n        kind: nuget-package\n"));

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
        Assert.IsTrue(plan.Checks.All(check => check.Work.Dimensions.Count == 4 &&
            check.Work.Dimensions[DotNetTools.Dimension] == "{}" &&
            !check.Work.Dimensions.ContainsKey("VersionBaseDirectory")));
    }

    [TestMethod]
    [DataRow("", "", "")]
    [DataRow("python", "3.13", "Malformed planned python version")]
    [DataRow("python", null, "Missing committed Python runtime selection.")]
    [DataRow("node", "failed", "Native locked node query failed")]
    [DataRow("node", "ambiguous", "no single locked selection")]
    public async Task PlannedToolVersionsComeFromCommittedConfiguration(string defect,
        string? value, string reason)
    {
        using var repo = await GitReaderTests.GitFixture.CreateAsync(context.CancellationToken);
        if (!(defect == "python" && value is null))
            await repo.SetAsync(".python-version", defect == "python" ? value! : "3.13.1\n");
        await repo.SetAsync("mise.toml", "[tools]\n");
        string commit = await repo.CommitAsync();
        await repo.GitAsync("reset", "--hard", commit);
        GitMaterialization candidate = await GitMaterialization.BindAsync(repo.Directory,
            commit, context.CancellationToken);
        string selection =
            """{"version":"24.1.0","active":true,"source":{"type":"mise.toml","path":""" +
            System.Text.Json.JsonSerializer.Serialize(Path.Combine(repo.Directory, "mise.toml")) +
            "}}";
        var commands = new List<NativeCommand>();
        Task<NativeCommandResult> ExecuteAsync(NativeCommand command,
            CancellationToken token)
        {
            commands.Add(command);
            return Task.FromResult(new NativeCommandResult(
                NativeTermination.Exited, defect == "node" && value == "failed" ? 1 : 0,
                defect == "node" && value == "ambiguous" ? "[" + selection + "," + selection +
                    "]" : "[" + selection + "]", "", 0, null));
        }
        string[] tools = ["node", "npm", "python"];
        if (defect.Length != 0)
        {
            InvalidDataException error = await Assert.ThrowsExactlyAsync<InvalidDataException>(
                () => DotNetFactsAssembler.VersionsAsync(tools, candidate, repo.Directory,
                    ExecuteAsync, context.CancellationToken));
            Assert.Contains(reason, error.Message);
            return;
        }
        Assert.IsEmpty(await DotNetFactsAssembler.VersionsAsync([], candidate, repo.Directory,
            ExecuteAsync, context.CancellationToken));
        Assert.IsEmpty(commands);
        Dictionary<string, string> versions = await DotNetFactsAssembler.VersionsAsync(tools,
            candidate, repo.Directory, ExecuteAsync, context.CancellationToken);
        Assert.AreEqual("""{"node":"24.1.0","npm":"bundled","python":"3.13.1"}""",
            DotNetTools.Serialize(versions));
        Assert.AreEqual("mise ls --json --current --locked node", commands.Single().Executable +
            " " + string.Join(' ', commands.Single().Arguments));
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
        DotNetEndpointFixture fixture = shared!;
        DotNetRevisionInputs inputs = await fixture.BindAsync(fixture.Response(),
            context.CancellationToken);
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
        DotNetEndpointFixture fixture = shared!;
        DotNetRevisionInputs inputs = await fixture.BindAsync(fixture.Response(),
            context.CancellationToken);
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
        CiPlan plan = Plan(basis, candidate, "product/B.csproj");

        CollectionAssert.AreEquivalent(NativeFrameworks, plan.Checks.Select(check =>
            DotNetChecks.Read(check.Work, fixture.Root).Dimension.TargetFramework).ToArray());
        foreach (PlannedCheck check in plan.Checks)
        {
            Assert.AreEqual("product/A.csproj", check.Work.Key.Target);
            Assert.AreEqual(DotNetChecks.Build, check.Work.Key.Check);
            Assert.IsEmpty(check.Work.Prerequisites);
            Assert.Contains(new SelectionReason("product/B.csproj", basis.Revision.Commit,
                "product/B.csproj"), check.Reasons);
        }
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task SelectionRejectsUnknownRequiredFactsEvenInFullMode(bool full)
    {
        DotNetEndpointFixture fixture = shared!;
        DotNetRevisionInputs inputs = await fixture.BindAsync(fixture.Response(),
            context.CancellationToken);
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
