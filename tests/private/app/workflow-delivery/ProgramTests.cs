using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using WorkflowDelivery.CI;
using WorkflowDelivery.Tests.CI;

namespace WorkflowDelivery.Tests;

[TestClass]
public sealed class ProgramTests
{
    [TestMethod]
    [DataRow(true, 0, "passed")]
    [DataRow(false, 1, "failed")]
    public void RunJsonFilesPlanAndCollectCandidateBoundOutcome(bool passed, int exitCode,
        string status)
    {
        using var files = new TransferFiles();
        PlanRequest request = Scenario.Request(
            [Scenario.Project("library"), Scenario.Project("unrelated")], "src/library/code.cs");
        string requestPath = files.Write("request.json", JsonSerializer.Serialize(request,
            TransferJson.Default.PlanRequest));
        using var planOutput = new StringWriter();
        using var planError = new StringWriter();

        int planExit = Program.Run(["ci", "plan", requestPath], planOutput, planError);

        Assert.AreEqual(0, planExit, planError.ToString());
        Assert.AreEqual(string.Empty, planError.ToString());
        using JsonDocument planDocument = JsonDocument.Parse(planOutput.ToString());
        JsonElement planJson = planDocument.RootElement;
        Assert.AreEqual("candidate", planJson.GetProperty("candidate").GetString());
        Assert.AreEqual("base", planJson.GetProperty("comparison").GetString());
        Assert.AreEqual(1, planJson.GetProperty("checks").GetArrayLength());
        Assert.AreEqual("library", planJson.GetProperty("checks")[0].GetProperty(
            "work").GetProperty("key")
            .GetProperty("target").GetString());

        string planPath = files.Write("plan.json", planOutput.ToString());
        string resultPath = files.Write("results.json", JsonSerializer.Serialize(
            new[] { new CheckResult("candidate", Scenario.Check("library").Key, passed ?
                CheckStatus.Passed : CheckStatus.Failed) },
            TransferJson.Default.CheckResultArray));
        using var outcomeOutput = new StringWriter();
        using var outcomeError = new StringWriter();

        int resultExit = Program.Run(["ci", "result", planPath, resultPath], outcomeOutput,
            outcomeError);

        Assert.AreEqual(exitCode, resultExit, outcomeError.ToString());
        Assert.AreEqual(string.Empty, outcomeError.ToString());
        using JsonDocument outcomeDocument = JsonDocument.Parse(outcomeOutput.ToString());
        JsonElement outcome = outcomeDocument.RootElement;
        Assert.AreEqual(passed, outcome.GetProperty("satisfied").GetBoolean());
        Assert.AreEqual(status, outcome.GetProperty("checks")[0].GetProperty("status").GetString());
        Assert.AreEqual(0, outcome.GetProperty("errors").GetArrayLength());
    }

    [TestMethod]
    [DataRow("malformed")]
    [DataRow("null-request")]
    [DataRow("missing-property")]
    [DataRow("null-candidate")]
    [DataRow("null-project")]
    [DataRow("unknown-property")]
    [DataRow("unresolved-scope")]
    public void RunInvalidPlanRequestReturnsInputErrorWithoutSuccessJson(string defect)
    {
        using var files = new TransferFiles();
        PlanRequest request = Scenario.Request([Scenario.Project("library")],
            "src/library/code.cs");
        JsonNode json = JsonNode.Parse(JsonSerializer.Serialize(request,
            TransferJson.Default.PlanRequest))!;
        switch (defect)
        {
            case "missing-property": json.AsObject().Remove("changedPaths"); break;
            case "null-candidate": json["candidate"] = null; break;
            case "null-project": json["candidate"]!["projects"]![0] = null; break;
            case "unknown-property": json["executeArbitraryGraph"] = true; break;
            case "unresolved-scope": json["changedPaths"]![0] = "unsupported/file.cs"; break;
        }
        string content = defect switch
        {
            "malformed" => "{broken",
            "null-request" => "null",
            _ => json.ToJsonString(),
        };
        string requestPath = files.Write("request.json", content);
        using var output = new StringWriter();
        using var error = new StringWriter();

        int exit = Program.Run(["ci", "plan", requestPath], output, error);

        Assert.AreEqual(2, exit);
        Assert.AreEqual(string.Empty, output.ToString());
        Assert.IsNotEmpty(error.ToString());
    }

    [TestMethod]
    [DataRow("unknown-status")]
    [DataRow("missing-status")]
    [DataRow("null-status")]
    [DataRow("null-results")]
    [DataRow("null-result")]
    public void RunInvalidResultsReturnsInputErrorWithoutOutcomeJson(string defect)
    {
        using var files = new TransferFiles();
        CheckSpec check = Scenario.Check("library");
        string planPath = files.Write("plan.json", JsonSerializer.Serialize(Scenario.Plan(
            check), TransferJson.Default.CiPlan));
        JsonNode json = JsonNode.Parse(JsonSerializer.Serialize(
            new[] { new CheckResult("candidate", check.Key, CheckStatus.Passed) },
                TransferJson.Default.CheckResultArray))!;
        switch (defect)
        {
            case "unknown-status": json[0]!["status"] = "Invented"; break;
            case "missing-status": json[0]!.AsObject().Remove("status"); break;
            case "null-status": json[0]!["status"] = null; break;
            case "null-result": json[0] = null; break;
        }
        string resultPath = files.Write("results.json", defect == "null-results" ? "null" :
            json.ToJsonString());
        using var output = new StringWriter();
        using var error = new StringWriter();

        int exit = Program.Run(["ci", "result", planPath, resultPath], output, error);

        Assert.AreEqual(2, exit);
        Assert.AreEqual(string.Empty, output.ToString());
        Assert.IsNotEmpty(error.ToString());
    }

    [TestMethod]
    public void RunMissingInputFileReturnsInputErrorWithoutSuccessJson()
    {
        using var files = new TransferFiles();
        using var output = new StringWriter();
        using var error = new StringWriter();

        int exit = Program.Run(["ci", "plan", Path.Combine(files.Root, "absent.json")], output,
            error);

        Assert.AreEqual(2, exit);
        Assert.AreEqual(string.Empty, output.ToString());
        Assert.IsNotEmpty(error.ToString());
    }

    [TestMethod]
    [DataRow("")]
    [DataRow("ci")]
    [DataRow("ci plan")]
    [DataRow("release publish")]
    [DataRow("ci result only-one.json")]
    public void RunUnsupportedArgumentsReportsUsage(string arguments)
    {
        using var output = new StringWriter();
        using var error = new StringWriter();

        int exit = Program.Run(arguments.Split(' ', StringSplitOptions.RemoveEmptyEntries),
            output, error);

        Assert.AreEqual(2, exit);
        Assert.AreEqual(string.Empty, output.ToString());
        Assert.StartsWith("Usage: workflow-delivery ci plan", error.ToString());
    }

    private sealed class TransferFiles : IDisposable
    {
        internal string Root { get; } = Directory.CreateTempSubdirectory(
            "workflow-delivery-test-").FullName;

        internal string Write(string name, string content)
        {
            string path = Path.Combine(Root, name);
            File.WriteAllText(path, content);
            return path;
        }

        public void Dispose() => Directory.Delete(Root, recursive: true);
    }
}
