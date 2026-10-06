using WorkflowDelivery.CI;
using WorkflowDelivery.Repository;

namespace WorkflowDelivery.Tests.Repository;

[TestClass]
public sealed class DotNetReleaseUnitTests(TestContext context)
{
    private const string Declaration = "product/workflow-delivery.release-unit.yml";

    [TestMethod]
    public async Task ReleaseUnitMembershipPrecedesSelection()
    {
        using var fixture = await DotNetEndpointFixture.CreateAsync(context.CancellationToken);
        await fixture.Repo.SetAsync(Declaration, Unit("product", "A.csproj", "B.csproj"));
        await fixture.CommitAsync();
        fixture.Graph = fixture.Response();
        DotNetRevisionInputs inputs = await fixture.BindAsync();

        foreach (string changed in new[] { "product/A.csproj", Declaration })
        {
            DotNetSelection selection = DotNetFactsAssembler.Select(inputs, inputs,
                [changed], false, [], []);
            Assert.AreEqual(Declaration, Assert.ContainsSingle(inputs.Units).SourcePath);
            Assert.HasCount(2, selection.Reasons);
            Assert.Contains("product/A.csproj", selection.Reasons.Keys);
            Assert.Contains("product/B.csproj", selection.Reasons.Keys);
            Assert.IsTrue(selection.Request.Candidate.Projects.All(project =>
                project.ReleaseUnit == "product"));
            Assert.IsTrue(selection.Reasons.Values.All(reasons =>
                reasons.Any(reason => reason.Path == changed)));
        }
    }

    [TestMethod]
    public async Task DeletedDeclarationKeepsBasisSelection()
    {
        using var fixture = await DotNetEndpointFixture.CreateAsync(context.CancellationToken);
        await fixture.Repo.SetAsync(Declaration, Unit("product", "A.csproj", "B.csproj"));
        await fixture.CommitAsync();
        fixture.Graph = fixture.Response();
        DotNetRevisionInputs before = await fixture.BindAsync();
        await fixture.Repo.RemoveAsync(Declaration);
        File.Delete(fixture.PathOf(Declaration));
        await fixture.CommitAsync();
        fixture.Graph = fixture.Response();
        DotNetRevisionInputs after = await fixture.BindAsync();

        CiPlan plan = DotNetFactsAssembler.Complete(DotNetFactsAssembler.Select(before,
            after, [Declaration], false, [], []), context.CancellationToken);

        Assert.IsEmpty(after.Units);
        Assert.HasCount(3, plan.Checks);
        Assert.IsTrue(plan.Checks.All(item => item.Work.Key.Check == DotNetChecks.Build &&
            item.Work.Package is null));
        Assert.IsTrue(plan.Checks.All(item => item.Reasons.Any(reason =>
            reason.Path == Declaration && reason.Revision == before.Revision.Commit)));
        Assert.IsTrue(after.Nodes.Where(node => node.Project != "dirs.proj").All(node =>
            plan.Checks.Any(item => item.Work.Key.Target == node.Project)));
    }

    [TestMethod]
    [DataRow("missing")]
    [DataRow("multiple")]
    [DataRow("duplicate-unit")]
    public async Task InvalidUnitAssociationFailsBeforeProjection(string defect)
    {
        using var fixture = await DotNetEndpointFixture.CreateAsync(context.CancellationToken);
        await fixture.Repo.SetAsync(Declaration,
            Unit("product", defect == "missing" ? "Absent.csproj" : "A.csproj"));
        if (defect != "missing")
            await fixture.Repo.SetAsync("workflow-delivery.release-unit.yml",
                Unit(defect == "multiple" ? "other" : "product", "product/A.csproj"));
        await fixture.CommitAsync();
        fixture.Graph = fixture.Response();
        DotNetRevisionInputs inputs = await fixture.BindAsync();

        Assert.ThrowsExactly<InvalidDataException>(() => DotNetFactsAssembler.Select(inputs,
            inputs, [Declaration], false, [], []));
    }

    internal static string Unit(string id, params string[] entries) =>
        "schema: workflow-delivery/v3/release-unit\nrelease-unit: " + id + "\nbuilds:\n" +
        string.Concat(entries.Select((entry, index) => "  - id: package-" + index +
            "\n    definition: dotnet/nuget-package-v1\n    entry-point: " + entry +
            "\n    outputs:\n      - id: archive-" + index +
            "\n        role: primary-package\n        kind: nuget-package\n"));
}
