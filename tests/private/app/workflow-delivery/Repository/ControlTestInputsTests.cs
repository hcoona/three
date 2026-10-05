using System.Text.Json;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using WorkflowDelivery.Platform;
using WorkflowDelivery.Repository;
using GitFixture = WorkflowDelivery.Tests.Repository.GitReaderTests.GitFixture;

namespace WorkflowDelivery.Tests.Repository;

[TestClass]
public sealed class ControlTestInputsTests(TestContext context)
{
    private const string Resource = "inputs/Version 雪.json";

    [TestMethod]
    public async Task NativeReceiptKeepsBothRevisionsAndRemovedAddedResources()
    {
        using var repo = await GitFixture.CreateAsync(context.CancellationToken);
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
        using JsonDocument transfer = JsonDocument.Parse(JsonSerializer.Serialize(result,
            TransferJson.Default.ControlTestInputResponse));
        Assert.AreEqual("Debug", transfer.RootElement.GetProperty("basis")
            .GetProperty("dimension").GetProperty("configuration").GetString());
        Assert.AreEqual(Resource, transfer.RootElement.GetProperty("basis")
            .GetProperty("inputs")[0].GetString());
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
    public async Task NativeReceiptRetainsCoherentExplicitFullAndCliTransfer()
    {
        using var repo = await GitFixture.CreateAsync(context.CancellationToken);
        await repo.SetAsync(ControlTestInputs.Project, Project(Resource));
        await repo.SetAsync(Resource);
        string candidate = await repo.CommitAsync();
        string endpoint = await MaterializeAsync(repo, "candidate", candidate);
        var request = new ControlTestInputRequest(new(candidate, candidate, true), repo.Directory,
            endpoint, endpoint);
        string path = Path.Combine(Path.GetDirectoryName(repo.Directory)!, "request.json");
        await File.WriteAllTextAsync(path, JsonSerializer.Serialize(request,
            TransferJson.Default.ControlTestInputRequest), context.CancellationToken);
        using var output = new StringWriter();
        using var error = new StringWriter();

        int code = Program.Run(["ci", "control-inputs", path], output, error);

        Assert.AreEqual(0, code, error.ToString());
        Assert.AreEqual("", error.ToString());
        ControlTestInputResponse result = JsonSerializer.Deserialize(output.ToString(),
            TransferJson.Default.ControlTestInputResponse)!;
        Assert.AreEqual(new(candidate, candidate, true), result.Comparison);
        Assert.AreEqual(Resource, Assert.ContainsSingle(result.Basis.Inputs));
        CollectionAssert.AreEqual(result.Basis.Inputs, result.Candidate.Inputs);
        Assert.IsTrue(result.Basis.Present);
    }

    [TestMethod]
    [DataRow("candidate-absent")]
    [DataRow("candidate-link")]
    [DataRow("missing-basis-directory")]
    [DataRow("wrong-materialization")]
    [DataRow("unavailable-basis")]
    [DataRow("nonexact-comparison")]
    [DataRow("incoherent-full")]
    [DataRow("failed-basis-evaluation")]
    public async Task InvalidNativeInputsDoNotBecomeAbsenceOrSuccessfulJson(string defect)
    {
        using var repo = await GitFixture.CreateAsync(context.CancellationToken);
        await repo.SetAsync(ControlTestInputs.Project, defect == "failed-basis-evaluation"
            ? "<Project><Import Project=\"unavailable.props\" /></Project>" : Project(Resource));
        await repo.SetAsync(Resource);
        string basis = await repo.CommitAsync();
        await repo.SetAsync("other.txt");
        await repo.SetAsync(ControlTestInputs.Project, Project(Resource));
        if (defect == "candidate-absent") await repo.RemoveAsync(ControlTestInputs.Project);
        if (defect == "candidate-link")
            await repo.SetAsync(ControlTestInputs.Project, "other.txt", "120000");
        string candidate = await repo.CommitAsync(basis);
        string before = await MaterializeAsync(repo, "basis", basis);
        string after = defect == "candidate-link" ? repo.Directory
            : await MaterializeAsync(repo, "candidate", candidate);
        var request = new ControlTestInputRequest(
            new(basis, candidate, defect == "incoherent-full"),
            repo.Directory, defect == "missing-basis-directory" ? null : before,
            defect == "wrong-materialization" ? before : after);
        if (defect == "unavailable-basis")
            request = request with
            {
                Comparison = request.Comparison with { Basis = new('f', 40) }
            };
        if (defect == "nonexact-comparison")
            request = request with
            {
                Comparison = request.Comparison with { Candidate = "HEAD" }
            };
        string path = Path.Combine(Path.GetDirectoryName(repo.Directory)!, "request.json");
        await File.WriteAllTextAsync(path, JsonSerializer.Serialize(request,
            TransferJson.Default.ControlTestInputRequest), context.CancellationToken);
        using var output = new StringWriter();
        using var error = new StringWriter();

        int code = Program.Run(["ci", "control-inputs", path], output, error);

        Assert.AreEqual(2, code);
        Assert.AreEqual("", output.ToString());
        Assert.IsNotEmpty(error.ToString());
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
        string path = Path.Combine(Path.GetDirectoryName(repo.Directory)!, name);
        await repo.GitAsync("worktree", "add", "--detach", path, revision);
        return path;
    }
}
