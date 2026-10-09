using System.Text.Json;
using System.Text.Json.Nodes;
using WorkflowDelivery.CI;
using WorkflowDelivery.Platform;
using WorkflowDelivery.Repository;
using GitFixture = WorkflowDelivery.Tests.Repository.GitReaderTests.GitFixture;

namespace WorkflowDelivery.Tests;

[TestClass]
public sealed class PythonGroupProgramTests(TestContext context)
{
    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task InvalidEndpointPreparationCannotEmitCompletedRequest(bool dirty)
    {
        using var repository = await GitFixture.CreateAsync(context.CancellationToken);
        await repository.SetAsync("input.txt", "original");
        string revision = await repository.CommitAsync();
        await repository.GitAsync("checkout", "--force", revision);
        if (dirty)
            await File.WriteAllTextAsync(Path.Combine(repository.Directory, "input.txt"),
                "modified", context.CancellationToken);
        else
        {
            await repository.SetAsync("input.txt", "later");
            string later = await repository.CommitAsync(revision);
            await repository.GitAsync("checkout", "--force", later);
        }
        string directory = Path.Combine(Path.GetTempPath(), "python endpoint failure " +
            Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            string source = Directory.CreateDirectory(Path.Combine(directory, "control")).FullName;
            string scratch = Directory.CreateDirectory(Path.Combine(directory, "scratch")).FullName;
            string Tool(string name) => Path.Combine(directory, "tools", name);
            var endpoint = new MaterializedEndpoint(repository.Directory, revision);
            var request = new PythonGroupPreparationRequest(endpoint, endpoint, true,
                Path.Combine(directory, "scope.json"), source, scratch,
                new(Tool("uv"), Tool("python"), Tool("dotnet"), Tool("rustup"), Tool("cargo")),
                Request(true).Candidate.Collection.Operation, 600);
            string path = Path.Combine(directory, "request.json");
            await File.WriteAllTextAsync(path, JsonSerializer.Serialize(request,
                TransferJson.Default.PythonGroupPreparationRequest), context.CancellationToken);
            using var output = new StringWriter();
            using var error = new StringWriter();

            int exit = Program.Run(["ci", "prepare-python-group", path], output, error);

            Assert.AreEqual(2, exit);
            Assert.AreEqual("", output.ToString());
            Assert.Contains(dirty ? "clean tracked checkout" : "HEAD is not the requested revision",
                error.ToString());
            Assert.IsEmpty(Directory.EnumerateFileSystemEntries(scratch));
        }
        finally { Directory.Delete(directory, recursive: true); }
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public void FailedPreparationRetainsPrivateDiagnosticsWithoutCompletion(bool existingFile)
    {
        string directory = Path.Combine(Path.GetTempPath(), "python preparation failure " +
            Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            PythonGroupRequest prepared = Request(false);
            var request = new PythonGroupPreparationRequest(prepared.Basis.Checkout,
                prepared.Candidate.Checkout, false, prepared.ScopePath, "/control", directory,
                new("/tools/uv", "/runtime/python", "/tools/dotnet", "/tools/rustup",
                    "/tools/cargo"), prepared.Candidate.Collection.Operation, 600);
            string path = Path.Combine(directory, "request.json");
            File.WriteAllText(path, JsonSerializer.Serialize(request,
                TransferJson.Default.PythonGroupPreparationRequest));
            NativeCommand command = new("/tools/uv", "/endpoint", ["sync", "--frozen"], 600,
                new Dictionary<string, string?> { ["HOME"] = "/owned/home" }, false);
            NativeCommandResult result = new(NativeTermination.Exited, 7,
                "private native stdout", "private native stderr", 1.25, null);
            var failure = new PythonGroupPreparationException(directory,
                [new(command, result)]);
            if (existingFile) File.WriteAllText(failure.DiagnosticsPath, "existing observation");
            using var output = new StringWriter();
            using var error = new StringWriter();

            int exit = Program.Run(["ci", "prepare-python-group", path], output, error,
                preparePythonGroup: (_, _) => throw failure);

            Assert.AreEqual(2, exit);
            Assert.AreEqual("", output.ToString());
            Assert.Contains("uv sync, Exited, exit 7", error.ToString());
            Assert.DoesNotContain(result.Stdout, error.ToString());
            Assert.DoesNotContain(result.Stderr, error.ToString());
            Assert.DoesNotContain("/owned/home", error.ToString());
            if (existingFile)
            {
                Assert.AreEqual("existing observation", File.ReadAllText(failure.DiagnosticsPath));
                Assert.Contains("could not be retained", error.ToString());
            }
            else
            {
                Assert.Contains(failure.DiagnosticsPath, error.ToString());
                PythonPreparationCommand observation = Assert.ContainsSingle(
                    JsonSerializer.Deserialize(File.ReadAllText(failure.DiagnosticsPath),
                        TransferJson.Default.PythonPreparationCommandArray)!);
                Assert.AreEqual(command.Executable, observation.Command.Executable);
                Assert.AreEqual(command.Directory, observation.Command.Directory);
                CollectionAssert.AreEqual(command.Arguments, observation.Command.Arguments);
                Assert.IsFalse(observation.Command.InheritEnvironment);
                Assert.AreEqual(result, observation.Result);
            }
        }
        finally { Directory.Delete(directory, recursive: true); }
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public void PreparationTransferRetainsOriginalEndpointAndToolContext(bool full)
    {
        PythonGroupRequest prepared = Request(full);
        var request = new PythonGroupPreparationRequest(prepared.Basis.Checkout,
            prepared.Candidate.Checkout, full, prepared.ScopePath, "/control/python sources",
            "/state/preparation", new("/tools/uv", "/runtime/python", "/tools/dotnet",
                "/tools/rustup", "/tools/cargo"), prepared.Candidate.Collection.Operation, 600);
        string directory = Path.Combine(Path.GetTempPath(), "python preparation transfer " +
            Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            string path = Path.Combine(directory, "request.json");
            File.WriteAllText(path, JsonSerializer.Serialize(request,
                TransferJson.Default.PythonGroupPreparationRequest));
            using var output = new StringWriter();
            using var error = new StringWriter();
            int exit = Program.Run(["ci", "prepare-python-group", path], output, error,
                preparePythonGroup: (received, _) =>
                {
                    Assert.AreEqual(request.Basis, received.Basis);
                    Assert.AreEqual(request.Candidate, received.Candidate);
                    Assert.AreEqual(full, received.Full);
                    Assert.AreEqual(request.Tools, received.Tools);
                    Assert.AreEqual("/control/python sources", received.ControlSources);
                    Assert.AreEqual(600, received.DeadlineSeconds);
                    return Task.FromResult(new PythonGroupPreparationResult(prepared, []));
                });
            Assert.AreEqual(0, exit, error.ToString());
            Assert.AreEqual("", error.ToString());
            PythonGroupPreparationResult result = JsonSerializer.Deserialize(output.ToString(),
                TransferJson.Default.PythonGroupPreparationResult)!;
            Assert.AreEqual(prepared.Basis.Checkout, result.Request.Basis.Checkout);
            Assert.AreEqual(prepared.Candidate.Checkout, result.Request.Candidate.Checkout);
            Assert.AreEqual(full, result.Request.Full);
            Assert.AreEqual(prepared.Candidate.Collection.Graph,
                result.Request.Candidate.Collection.Graph);
            Assert.IsEmpty(result.Commands);
        }
        finally { Directory.Delete(directory, recursive: true); }
    }

    [TestMethod]
    [DataRow("null")]
    [DataRow("malformed")]
    [DataRow("missing-context")]
    [DataRow("native-failure")]
    public void InvalidOrFailedPreparationCannotEmitCompletedRequest(string defect)
    {
        string directory = Path.Combine(Path.GetTempPath(), "python preparation invalid " +
            Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            string path = Path.Combine(directory, "request.json");
            PythonGroupRequest prepared = Request(false);
            var request = new PythonGroupPreparationRequest(prepared.Basis.Checkout,
                prepared.Candidate.Checkout, false, prepared.ScopePath, "/control", "/state",
                new("/tools/uv", "/runtime/python", "/tools/dotnet", "/tools/rustup",
                    "/tools/cargo"),
                prepared.Candidate.Collection.Operation, 600);
            File.WriteAllText(path, defect switch
            {
                "null" => "null",
                "malformed" => "{",
                "missing-context" => "{}",
                _ => JsonSerializer.Serialize(request,
                    TransferJson.Default.PythonGroupPreparationRequest)
            });
            using var output = new StringWriter();
            using var error = new StringWriter();
            int exit = Program.Run(["ci", "prepare-python-group", path], output, error,
                preparePythonGroup: (_, _) =>
                    throw new InvalidDataException("Required preparation failed."));
            Assert.AreEqual(2, exit);
            Assert.AreEqual("", output.ToString());
            Assert.IsNotEmpty(error.ToString());
        }
        finally { Directory.Delete(directory, recursive: true); }
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public void PreparedGroupTransferRetainsNativeCandidateContext(bool full)
    {
        PythonGroupRequest request = Request(full);
        string directory = Path.Combine(Path.GetTempPath(), "python group transfer " +
            Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            string path = Path.Combine(directory, "request.json");
            File.WriteAllText(path, JsonSerializer.Serialize(request,
                TransferJson.Default.PythonGroupRequest));
            var metadata = new PythonMetadata("/candidate", "native-workspace", ">=3.14",
                [new("root", ".", "native-root")],
                new Dictionary<string, PythonMetadataNode>());
            var result = new PythonGroupReadback(new(request.Basis.Checkout.Reference,
                request.Candidate.Checkout.Reference, PythonFactsAssembler.Scope, []),
                metadata.Members,
                "/runtime/python", new("pyproject.toml", ["tests/root.py"], []),
                new(request.Candidate.Checkout.Reference, "pyproject.toml",
                    [new("tests/root.py", null, ["tests/root.py"])]),
                request.Candidate.Collection.Operation);
            using var output = new StringWriter();
            using var error = new StringWriter();

            int exit = Program.Run(["ci", "plan-python-group", path], output, error,
                planPythonGroup: (received, _) =>
                {
                    Assert.AreEqual(full, received.Full);
                    Assert.AreEqual(request.Basis.Checkout, received.Basis.Checkout);
                    Assert.AreEqual(request.Candidate.Collection.Graph,
                        received.Candidate.Collection.Graph);
                    Assert.AreEqual("/state/scope.json", received.ScopePath);
                    Assert.AreEqual("/basis/environment", received.Basis.Environment[
                        "UV_PROJECT_ENVIRONMENT"]);
                    return Task.FromResult(result);
                });

            Assert.AreEqual(0, exit, error.ToString());
            Assert.AreEqual("", error.ToString());
            PythonGroupReadback transferred = JsonSerializer.Deserialize(output.ToString(),
                TransferJson.Default.PythonGroupReadback)!;
            Assert.IsEmpty(transferred.Plan.Checks);
            Assert.AreEqual(result.Plan.Candidate, transferred.Targets.Revision);
            Assert.AreEqual("root", Assert.ContainsSingle(transferred.Members).Name);
            Assert.AreEqual(result.Interpreter, transferred.Interpreter);
            Assert.AreEqual(result.Operation.Dimensions["platform"],
                transferred.Operation.Dimensions["platform"]);
            Assert.AreEqual("tests/root.py", Assert.ContainsSingle(
                transferred.Targets.Targets).Target);
        }
        finally { Directory.Delete(directory, recursive: true); }
    }

    [TestMethod]
    [DataRow("malformed")]
    [DataRow("null")]
    [DataRow("missing-endpoint")]
    [DataRow("null-environment")]
    [DataRow("unknown-field")]
    [DataRow("native-failure")]
    public void InvalidOrFailedPlanningCannotEmitSuccessfulNoWork(string defect)
    {
        string directory = Path.Combine(Path.GetTempPath(), "python group invalid " +
            Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            JsonNode json = JsonNode.Parse(JsonSerializer.Serialize(Request(false),
                TransferJson.Default.PythonGroupRequest))!;
            if (defect == "missing-endpoint") json.AsObject().Remove("basis");
            else if (defect == "null-environment") json["basis"]!["environment"] = null;
            else if (defect == "unknown-field") json["resolveAgain"] = true;
            string path = Path.Combine(directory, "request.json");
            File.WriteAllText(path, defect switch
            { "malformed" => "{", "null" => "null", _ => json.ToJsonString() });
            using var output = new StringWriter();
            using var error = new StringWriter();

            int exit = Program.Run(["ci", "plan-python-group", path], output, error,
                planPythonGroup: (_, _) =>
                    throw new InvalidDataException("Required native collection failed."));

            Assert.AreEqual(2, exit);
            Assert.AreEqual("", output.ToString());
            Assert.IsNotEmpty(error.ToString());
        }
        finally { Directory.Delete(directory, recursive: true); }
    }

    private static PythonGroupRequest Request(bool full)
    {
        string candidate = new('b', 40);
        return new(Endpoint("/basis", full ? candidate : new('a', 40)),
            Endpoint("/candidate", candidate), full, "/state/scope.json");

        static PythonGroupEndpoint Endpoint(string root, string revision) => new(
            new(root, revision), new(new(root, "uv", "/runtime/python", root + "/tools/python",
                "/control/passive.py", "/control/supplement", root + "/cache",
                root + "/bootstrap", root + "/activity"), "/control/pytest_configuration.py",
                "/control/packages.py", new("ubuntu-latest", "python-3.14", new()
                { ["python"] = "3.14", ["platform"] = "linux" })),
            new Dictionary<string, string?> { ["UV_PROJECT_ENVIRONMENT"] = root + "/environment" });
    }
}
