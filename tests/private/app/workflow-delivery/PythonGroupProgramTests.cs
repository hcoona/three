using System.Text.Json;
using System.Text.Json.Nodes;
using WorkflowDelivery.CI;
using WorkflowDelivery.Repository;

namespace WorkflowDelivery.Tests;

[TestClass]
public sealed class PythonGroupProgramTests
{
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
