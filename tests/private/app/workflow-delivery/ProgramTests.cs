using System.Text.Json;
using WorkflowDelivery.CI;
using WorkflowDelivery.Tests.CI;

namespace WorkflowDelivery.Tests;

[TestClass]
public sealed class ProgramTests
{
    [TestMethod]
    public void ComparisonEmitsTestedCandidateAndNativeBasis()
    {
        using var files = new TransferFiles();
        string basis = new('a', 40);
        string candidate = new('b', 40);
        string eventPath = files.Write("event.json", $$$$"""
            {"pull_request":{"base":{"sha":"{{{{basis}}}}"},
             "head":{"sha":"{{{{new string('c', 40)}}}}"}}}
            """);
        using var output = new StringWriter();
        using var error = new StringWriter();

        int exit = Program.Run(["ci", "comparison", "pull_request", eventPath, candidate],
            output, error);

        Assert.AreEqual(0, exit, error.ToString());
        Assert.IsEmpty(error.ToString());
        using JsonDocument document = JsonDocument.Parse(output.ToString());
        Assert.AreEqual(basis, document.RootElement.GetProperty("basis").GetString());
        Assert.AreEqual(candidate, document.RootElement.GetProperty("candidate").GetString());
        Assert.IsFalse(document.RootElement.GetProperty("full").GetBoolean());
    }

    [TestMethod]
    [DataRow(true, 0, "passed")]
    [DataRow(false, 1, "failed")]
    public void PlanOutputFeedsResultAndRequiredFailureSetsExitCode(bool passed, int expectedExit,
        string status)
    {
        using var files = new TransferFiles();
        ProjectFacts project = Scenario.Project("library");
        string request = files.Write("request.json", JsonSerializer.Serialize(
            Scenario.Request([project], "src/library/code.cs"), TransferJson.Default.PlanRequest));
        using var planOutput = new StringWriter();
        using var output = new StringWriter();
        using var error = new StringWriter();

        Assert.AreEqual(0, Program.Run(["ci", "plan", request], planOutput, error),
            error.ToString());
        string plan = files.Write("plan.json", planOutput.ToString());
        CheckResult[] results = [new("candidate", Assert.ContainsSingle(project.Checks).Key,
            passed ? CheckStatus.Passed : CheckStatus.Failed)];
        string result = files.Write("results.json", JsonSerializer.Serialize(results,
            TransferJson.Default.CheckResultArray));
        int exit = Program.Run(["ci", "result", plan, result], output, error);

        Assert.AreEqual(expectedExit, exit, error.ToString());
        Assert.IsEmpty(error.ToString());
        CiOutcome? outcome = JsonSerializer.Deserialize(output.ToString(),
            TransferJson.Default.CiOutcome);
        Assert.IsNotNull(outcome);
        Assert.AreEqual("candidate", outcome.Candidate);
        Assert.AreEqual(passed, outcome.Satisfied);
        Assert.AreEqual(status, Assert.ContainsSingle(outcome.Checks).Status);
    }

    [TestMethod]
    public void MissingInputFileReportsErrorWithoutJson()
    {
        using var files = new TransferFiles();
        using var output = new StringWriter();
        using var error = new StringWriter();

        int exit = Program.Run(["ci", "plan", Path.Combine(files.Root, "absent.json")],
            output, error);

        Assert.AreEqual(2, exit);
        Assert.IsEmpty(output.ToString());
        Assert.IsNotEmpty(error.ToString());
    }

    [TestMethod]
    [DataRow("")]
    [DataRow("ci plan")]
    [DataRow("ci comparison pull_request event.json")]
    [DataRow("release publish")]
    public void UnsupportedArgumentsReportUsage(string arguments)
    {
        using var output = new StringWriter();
        using var error = new StringWriter();

        int exit = Program.Run(arguments.Split(' ', StringSplitOptions.RemoveEmptyEntries),
            output, error);

        Assert.AreEqual(2, exit);
        Assert.IsEmpty(output.ToString());
        Assert.StartsWith("Usage: WorkflowDelivery ci plan", error.ToString());
    }

    [TestMethod]
    [DataRow(false, 0)]
    [DataRow(true, 1)]
    public void RunNodeSerializesOriginalOutputAndSignalsMissingRequiredResult(
        bool omitRequiredResult, int expectedExit)
    {
        using var files = new TransferFiles();
        CiPlan plan = NodeExecutionFixture.CreatePlan(new string('a', 40));
        var request = new NodeRunRequest(Path.Combine(files.Root, "checkout"),
            Path.Combine(files.Root, "scratch"), "node24");
        string planPath = files.Write("plan.json", JsonSerializer.Serialize(plan,
            TransferJson.Default.CiPlan));
        string requestPath = files.Write("request.json", JsonSerializer.Serialize(request,
            TransferJson.Default.NodeRunRequest));
        var original = new NodeOriginalOutput("product", "main", "main-package",
            "primary-package", "npm-tarball", "original.tgz", 123, new string('b', 64));
        CheckKey required = plan.Checks.Last(item => item.Work.Required &&
            item.Work.Key.Variant == request.Runtime).Work.Key;
        using var output = new StringWriter();
        using var error = new StringWriter();

        int exit = Program.Run(["ci", "run-node", planPath, requestPath], output, error,
            (receivedPlan, receivedRequest, _) =>
            {
                Assert.AreEqual(plan.Candidate, receivedPlan.Candidate);
                Assert.AreEqual(request, receivedRequest);
                CheckResult[] results = receivedPlan.Checks
                    .Where(item => item.Work.Key.Variant == receivedRequest.Runtime &&
                        (!omitRequiredResult || item.Work.Key != required))
                    .Select(item => new CheckResult(receivedPlan.Candidate, item.Work.Key,
                        CheckStatus.Passed)).ToArray();
                return Task.FromResult(new NodeRunResult(receivedPlan.Candidate, results, [],
                    [original], [], receivedRequest.Runtime, null));
            });

        Assert.AreEqual(expectedExit, exit, error.ToString());
        Assert.IsEmpty(error.ToString());
        NodeRunResult? transferred = JsonSerializer.Deserialize(output.ToString(),
            TransferJson.Default.NodeRunResult);
        Assert.IsNotNull(transferred);
        Assert.AreEqual(plan.Candidate, transferred.Candidate);
        Assert.AreEqual(request.Runtime, transferred.Runtime);
        Assert.AreEqual(!omitRequiredResult, transferred.Results.Any(item => item.Key == required));
        Assert.AreEqual(original, Assert.ContainsSingle(transferred.Outputs));
    }

    [TestMethod]
    public void MalformedNodeRequestCannotDispatch()
    {
        using var files = new TransferFiles();
        string planPath = files.Write("plan.json", JsonSerializer.Serialize(
            NodeExecutionFixture.CreatePlan(new string('a', 40)), TransferJson.Default.CiPlan));
        string requestPath = files.Write("request.json", "{broken");
        using var output = new StringWriter();
        using var error = new StringWriter();

        int exit = Program.Run(["ci", "run-node", planPath, requestPath], output, error,
            (_, _, _) => throw new AssertFailedException("Invalid transfer reached executor."));

        Assert.AreEqual(2, exit);
        Assert.IsEmpty(output.ToString());
        Assert.IsNotEmpty(error.ToString());
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
