using System.Text.Json;
using WorkflowDelivery.CI;
using WorkflowDelivery.Platform;
using WorkflowDelivery.Repository;
using WorkflowDelivery.Tests.CI;

namespace WorkflowDelivery.Tests;

[TestClass]
public sealed class PythonGroupProgramTests
{
    [TestMethod]
    public void PreparationOutputFeedsPlanningWithOriginalNativeContext()
    {
        using var files = new TransferFiles();
        PythonGroupRequest prepared = Request() with { Full = true };
        PythonGroupPreparationRequest request = Preparation(prepared, files.Root);
        string preparationPath = files.Write("prepare.json", JsonSerializer.Serialize(request,
            TransferJson.Default.PythonGroupPreparationRequest));
        using var preparationOutput = new StringWriter();
        using var output = new StringWriter();
        using var error = new StringWriter();

        Assert.AreEqual(0, Program.Run(["ci", "prepare-python-group", preparationPath],
            preparationOutput, error, preparePythonGroup: (received, _) =>
            {
                Assert.AreEqual(request.Basis, received.Basis);
                Assert.AreEqual(request.Candidate, received.Candidate);
                Assert.AreEqual(request.Tools, received.Tools);
                return Task.FromResult(new PythonGroupPreparationResult(
                    prepared with { Full = received.Full, ScopePath = received.ScopePath }, []));
            }), error.ToString());
        PythonGroupPreparationResult? preparation = JsonSerializer.Deserialize(
            preparationOutput.ToString(), TransferJson.Default.PythonGroupPreparationResult);
        Assert.IsNotNull(preparation);
        string requestPath = files.Write("plan.json", JsonSerializer.Serialize(preparation.Request,
            TransferJson.Default.PythonGroupRequest));
        string candidate = prepared.Candidate.Checkout.Reference;
        var result = new PythonGroupReadback(new(prepared.Basis.Checkout.Reference,
            candidate, PythonFactsAssembler.Scope, []), [new("root", ".", "native-root")],
            "/runtime/python", new("pyproject.toml", ["tests/root.py"], []),
            new(candidate, "pyproject.toml", [new("tests/root.py", null, ["tests/root.py"])]),
            prepared.Candidate.Collection.Operation);

        int exit = Program.Run(["ci", "plan-python-group", requestPath], output, error,
            planPythonGroup: (received, _) =>
            {
                Assert.AreEqual(prepared.Basis.Checkout, received.Basis.Checkout);
                Assert.AreEqual(prepared.Basis.Collection.Graph, received.Basis.Collection.Graph);
                Assert.AreEqual(prepared.Candidate.Checkout, received.Candidate.Checkout);
                Assert.AreEqual(prepared.Candidate.Collection.Graph,
                    received.Candidate.Collection.Graph);
                Assert.IsTrue(received.Full);
                Assert.AreEqual(prepared.ScopePath, received.ScopePath);
                return Task.FromResult(result);
            });

        Assert.AreEqual(0, exit, error.ToString());
        Assert.IsEmpty(error.ToString());
        PythonGroupReadback? transferred = JsonSerializer.Deserialize(output.ToString(),
            TransferJson.Default.PythonGroupReadback);
        Assert.IsNotNull(transferred);
        Assert.AreEqual(candidate, transferred.Plan.Candidate);
        Assert.AreEqual("tests/root.py", Assert.ContainsSingle(transferred.Targets.Targets).Target);
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public void FailedPreparationKeepsPrivateDiagnosticsOffStderrAndNeverOverwrites(bool existing)
    {
        using var files = new TransferFiles();
        string path = files.Write("request.json", JsonSerializer.Serialize(
            Preparation(Request(), files.Root),
            TransferJson.Default.PythonGroupPreparationRequest));
        var command = new NativeCommand("/tools/uv", "/endpoint", ["sync"], 600,
            new Dictionary<string, string?> { ["HOME"] = "/owned/home" }, false);
        var result = new NativeCommandResult(NativeTermination.Exited, 7, "private native stdout",
            CiCallerTransferTests.WorkflowCommandText, 1, null);
        var failure = new PythonGroupPreparationException(files.Root, [new(command, result)]);
        if (existing)
            File.WriteAllText(failure.DiagnosticsPath, "existing observation");
        using var output = new StringWriter();
        using var error = new StringWriter();

        int exit = Program.Run(["ci", "prepare-python-group", path], output, error,
            preparePythonGroup: (_, _) => throw failure);

        Assert.AreEqual(2, exit);
        Assert.IsEmpty(output.ToString());
        string diagnostics = error.ToString();
        Assert.Contains("uv sync, Exited, exit 7.", diagnostics);
        Assert.Contains("add-mask]x", diagnostics);
        Assert.DoesNotContain(result.Stdout, diagnostics);
        Assert.DoesNotContain("/owned/home", diagnostics);
        CiCallerTransferTests.AssertNoWorkflowCommand(diagnostics);
        if (existing)
        {
            Assert.AreEqual("existing observation", File.ReadAllText(failure.DiagnosticsPath));
            Assert.Contains("could not be retained", diagnostics);
        }
        else
        {
            Assert.Contains(failure.DiagnosticsPath, diagnostics);
            PythonPreparationCommand[]? retained = JsonSerializer.Deserialize(
                File.ReadAllText(failure.DiagnosticsPath),
                TransferJson.Default.PythonPreparationCommandArray);
            Assert.IsNotNull(retained);
            PythonPreparationCommand observation = Assert.ContainsSingle(retained);
            Assert.AreEqual(result, observation.Result);
            Assert.AreEqual("/owned/home", observation.Command.Environment!["HOME"]);
        }
    }

    [TestMethod]
    public void PlanningFailureReportsNoJsonAndCannotStartWorkflowCommands()
    {
        using var files = new TransferFiles();
        string path = files.Write("request.json", JsonSerializer.Serialize(Request(),
            TransferJson.Default.PythonGroupRequest));
        using var output = new StringWriter();
        using var error = new StringWriter();

        int exit = Program.Run(["ci", "plan-python-group", path], output, error,
            planPythonGroup: (_, _) => throw new InvalidDataException(
                CiCallerTransferTests.WorkflowCommandText));

        Assert.AreEqual(2, exit);
        Assert.IsEmpty(output.ToString());
        Assert.Contains("add-mask]x", error.ToString());
        CiCallerTransferTests.AssertNoWorkflowCommand(error.ToString());
    }

    private static PythonGroupPreparationRequest Preparation(PythonGroupRequest request,
        string scratch) => new(request.Basis.Checkout, request.Candidate.Checkout, request.Full,
            request.ScopePath, "/control/python sources", scratch,
            new("/tools/uv", "/runtime/python", "/tools/dotnet"),
            request.Candidate.Collection.Operation, 600);

    private static PythonGroupRequest Request() => new(Endpoint("/basis", new('a', 40)),
        Endpoint("/candidate", new('b', 40)), false, "/state/scope.json");

    private static PythonGroupEndpoint Endpoint(string root, string revision) => new(
        new(root, revision), new(new(root, "uv", "/runtime/python", root + "/tools/python",
            "/control/passive.py"), "/control/pytest_configuration.py", "/control/packages.py",
            new("ubuntu-latest", "python-3.14",
                new() { ["python"] = "3.14", ["platform"] = "linux" })),
        new Dictionary<string, string?> { ["UV_PROJECT_ENVIRONMENT"] = root + "/environment" });

    private sealed class TransferFiles : IDisposable
    {
        internal string Root { get; } = Directory.CreateTempSubdirectory(
            "workflow-python-cli-").FullName;

        internal string Write(string name, string content)
        {
            string path = Path.Combine(Root, name);
            File.WriteAllText(path, content);
            return path;
        }

        public void Dispose() => Directory.Delete(Root, recursive: true);
    }
}
