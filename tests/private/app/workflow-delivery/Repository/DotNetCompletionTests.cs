using System.Text.Json;
using WorkflowDelivery.CI;
using WorkflowDelivery.Repository;

namespace WorkflowDelivery.Tests.Repository;

[TestClass]
public sealed class DotNetCompletionTests(TestContext context)
{
    [TestMethod]
    public async Task SelectedVariantsRetainGlobalsDimensionsAndOrigins()
    {
        using var fixture = await DotNetEndpointFixture.CreateAsync(context.CancellationToken);
        DotNetRevisionInputs inputs = await fixture.BindAsync();
        DotNetSelection selected = DotNetFactsAssembler.Select(inputs, inputs,
            ["linked/data.txt"], false, [], []);
        DotNetGraphNode[] nodes = selected.CandidateNodes["product/A.csproj"];
        foreach (DotNetGraphNode node in nodes)
        {
            node.Identity.Globals.Add("IsGraphBuild", "true");
            node.Identity.Globals.Add("NativeDetail", "space %3B percent%25 equals=tail");
        }
        selected = selected with
        {
            CandidateNodes = new Dictionary<string, DotNetGraphNode[]>
            {
                ["product/A.csproj"] = nodes.Select(node => node with
                { TestCapability = "MTP" }).ToArray()
            }
        };
        CiPlan plan = DotNetFactsAssembler.Complete(selected, context.CancellationToken);
        CiPlan transferred = JsonSerializer.Deserialize(JsonSerializer.Serialize(plan,
            TransferJson.Default.CiPlan), TransferJson.Default.CiPlan)!;
        Assert.HasCount(4, transferred.Checks);
        Assert.AreEqual(inputs.Revision.Commit, transferred.Candidate);
        foreach (PlannedCheck item in transferred.Checks)
        {
            Assert.AreEqual("product/A.csproj", item.Work.Key.Target);
            Assert.AreEqual(CheckOrigin.NativeRetained, Assert.ContainsSingle(item.Origins));
            Assert.IsEmpty(item.QualityPresets);
            Assert.Contains(new SelectionReason("linked/data.txt", inputs.Revision.Commit,
                "product/A.csproj"), item.Reasons);
            DotNetGraphNode restored = DotNetChecks.Read(item.Work, fixture.Root);
            Assert.AreEqual("true", restored.Identity.Globals["IsGraphBuild"]);
            Assert.AreEqual("space %3B percent%25 equals=tail",
                restored.Identity.Globals["NativeDetail"]);
            Assert.AreEqual(restored.Identity.Globals["TargetFramework"],
                restored.Dimension.TargetFramework);
            Assert.AreEqual("", restored.Dimension.RuntimeIdentifier);
            Assert.IsFalse(restored.OuterBuild);
            if (item.Work.Key.Check == DotNetChecks.Test)
                Assert.AreEqual(item.Work.Key with { Check = DotNetChecks.Build },
                    Assert.ContainsSingle(item.Work.Prerequisites));
        }
    }

    [TestMethod]
    public async Task NoneCapabilityCreatesBuildOnlyAndRemovedNodesDoNotCreateChecks()
    {
        using var fixture = await DotNetEndpointFixture.CreateAsync(context.CancellationToken);
        DotNetRevisionInputs before = await fixture.BindAsync();
        await fixture.Repo.RemoveAsync("product/B.csproj");
        File.Delete(fixture.PathOf("product/B.csproj"));
        await fixture.CommitAsync();
        fixture.Graph = fixture.Response(includeB: false);
        DotNetRevisionInputs after = await fixture.BindAsync();
        CiPlan plan = DotNetFactsAssembler.Complete(DotNetFactsAssembler.Select(before, after,
            ["product/B.csproj"], false, [], []), context.CancellationToken);
        Assert.HasCount(2, plan.Checks);
        Assert.IsTrue(plan.Checks.All(item => item.Work.Key.Target == "product/A.csproj" &&
            item.Work.Key.Check == DotNetChecks.Build && item.Work.Prerequisites.Length == 0));
        Assert.Contains(before.Revision.Commit, plan.Checks.SelectMany(item => item.Reasons)
            .Select(reason => reason.Revision));
    }

    [TestMethod]
    public async Task SelectedAdoptedQualityCannotDowngrade()
    {
        using var fixture = await DotNetEndpointFixture.CreateAsync(context.CancellationToken);
        DotNetRevisionInputs inputs = await fixture.BindAsync();
        DotNetSelection selected = DotNetFactsAssembler.Select(inputs, inputs,
            ["linked/data.txt"], false, [], []);
        selected = selected with
        {
            Request = selected.Request with
            {
                Candidate = selected.Request.Candidate with
                {
                    Projects = selected.Request.Candidate.Projects.Select(project =>
                        project.Id == "product/A.csproj" ? project with
                        { Origin = CheckOrigin.Preset, QualityPreset = "dotnet/package-quality" }
                            : project).ToArray()
                }
            }
        };
        InvalidDataException error = Assert.ThrowsExactly<InvalidDataException>(() =>
            DotNetFactsAssembler.Complete(selected, context.CancellationToken));
        Assert.Contains("native package adapter", error.Message);
        Assert.Contains("product/A.csproj", error.Message);
        Assert.IsTrue(selected.Request.Candidate.Projects.All(project =>
            project.Checks.Length == 0));
    }

    [TestMethod]
    [DataRow("missing")]
    [DataRow("outer-only")]
    [DataRow("duplicate")]
    public async Task MissingNativeIdentitiesFailCompletion(string defect)
    {
        using var fixture = await DotNetEndpointFixture.CreateAsync(context.CancellationToken);
        DotNetRevisionInputs inputs = await fixture.BindAsync();
        DotNetSelection selected = DotNetFactsAssembler.Select(inputs, inputs,
            ["linked/data.txt"], false, [], []);
        DotNetGraphNode[] nodes = selected.CandidateNodes["product/A.csproj"];
        selected = selected with
        {
            CandidateNodes = defect == "missing" ? new Dictionary<string, DotNetGraphNode[]>() :
                new Dictionary<string, DotNetGraphNode[]>
                {
                    ["product/A.csproj"] = defect == "outer-only" ?
                        nodes.Where(node => node.OuterBuild).ToArray() : [.. nodes, nodes[1]]
                }
        };
        Assert.ThrowsExactly<InvalidDataException>(() => DotNetFactsAssembler.Complete(selected,
            context.CancellationToken));
    }
}
