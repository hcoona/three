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

    // Request-only defects read one class-owned committed comparison with materialized
    // endpoints. Defects that need different committed revisions keep private repositories.
    private static readonly string[] RequestDefects = ["missing-basis-directory",
        "wrong-materialization", "unavailable-basis", "nonexact-comparison",
        "incoherent-full"];
    private static GitFixture? shared;
    private static ControlTestInputRequest? sharedRequest;

    [ClassInitialize]
    public static async Task InitializeAsync(TestContext classContext)
    {
        GitFixture repo = await GitFixture.CreateAsync(classContext.CancellationToken);
        try
        {
            (string basis, string candidate) = await CommitAsync(repo, "");
            sharedRequest = new(new(basis, candidate, false), repo.Directory,
                await MaterializeAsync(repo, "basis", basis),
                await MaterializeAsync(repo, "candidate", candidate));
            shared = repo;
        }
        catch { repo.Dispose(); throw; }
    }

    [ClassCleanup]
    public static void Cleanup() => shared?.Dispose();

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
    public async Task SharedComparisonQualifiesNativeReceipt()
    {
        // Defect-free control: the shared comparison itself produces successful JSON.
        (int code, string output, string error) = await RunAsync(sharedRequest!,
            context.CancellationToken);
        Assert.AreEqual(0, code, error);
        ControlTestInputResponse result = JsonSerializer.Deserialize(output,
            TransferJson.Default.ControlTestInputResponse)!;
        Assert.AreEqual(sharedRequest!.Comparison, result.Comparison);
        Assert.AreEqual(Resource, Assert.ContainsSingle(result.Candidate.Inputs));
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
        // Defects in committed revisions own a repository; request defects read the shared one.
        using GitFixture? repo = RequestDefects.Contains(defect) ? null
            : await GitFixture.CreateAsync(context.CancellationToken);
        ControlTestInputRequest request;
        if (repo is null) request = sharedRequest!;
        else
        {
            (string basis, string candidate) = await CommitAsync(repo, defect);
            string before = await MaterializeAsync(repo, "basis", basis);
            string after = defect == "candidate-link" ? repo.Directory
                : await MaterializeAsync(repo, "candidate", candidate);
            request = new(new(basis, candidate, false), repo.Directory, before, after);
        }
        if (defect == "incoherent-full")
            request = request with { Comparison = request.Comparison with { Full = true } };
        if (defect == "missing-basis-directory")
            request = request with { BasisDirectory = null };
        if (defect == "wrong-materialization")
            request = request with { CandidateDirectory = request.BasisDirectory! };
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

        (int code, string output, string error) = await RunAsync(request,
            context.CancellationToken);

        Assert.AreEqual(2, code);
        Assert.AreEqual("", output);
        Assert.IsNotEmpty(error);
    }

    private static async Task<(string Basis, string Candidate)> CommitAsync(GitFixture repo,
        string defect)
    {
        await repo.SetAsync(ControlTestInputs.Project, defect == "failed-basis-evaluation"
            ? "<Project><Import Project=\"unavailable.props\" /></Project>" : Project(Resource));
        await repo.SetAsync(Resource);
        string basis = await repo.CommitAsync();
        await repo.SetAsync("other.txt");
        await repo.SetAsync(ControlTestInputs.Project, Project(Resource));
        if (defect == "candidate-absent") await repo.RemoveAsync(ControlTestInputs.Project);
        if (defect == "candidate-link")
            await repo.SetAsync(ControlTestInputs.Project, "other.txt", "120000");
        return (basis, await repo.CommitAsync(basis));
    }

    // The request transfer file is row-owned scratch outside any checkout.
    private static async Task<(int Code, string Output, string Error)> RunAsync(
        ControlTestInputRequest request, CancellationToken token)
    {
        string state = Directory.CreateTempSubdirectory("workflow-control-inputs-").FullName;
        try
        {
            string path = Path.Combine(state, "request.json");
            await File.WriteAllTextAsync(path, JsonSerializer.Serialize(request,
                TransferJson.Default.ControlTestInputRequest), token);
            using var output = new StringWriter();
            using var error = new StringWriter();
            int code = Program.Run(["ci", "control-inputs", path], output, error);
            return (code, output.ToString(), error.ToString());
        }
        finally { Directory.Delete(state, recursive: true); }
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
