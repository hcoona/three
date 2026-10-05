using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using WorkflowDelivery.CI;
using WorkflowDelivery.Platform;
using WorkflowDelivery.Repository;
using WorkflowDelivery.Tests.Repository;
using WorkflowDelivery.Tests.CI;

namespace WorkflowDelivery.Tests;

[TestClass]
public sealed class ProgramTests
{
    [TestMethod]
    public void RunComparisonReadsNativePayloadAndEmitsMinimalTransfer()
    {
        using var files = new TransferFiles();
        string basis = new('a', 40);
        string candidate = new('b', 40);
        string eventPath = files.Write("native-event.json", $$$"""
            {"pull_request":{"base":{"sha":"{{{basis}}}"},
             "head":{"sha":"{{{new string('c', 40)}}}"}},"unrelated":"native field"}
            """);
        using var output = new StringWriter();
        using var error = new StringWriter();

        int exit = Program.Run(["ci", "comparison", "pull_request", eventPath, candidate],
            output, error);

        Assert.AreEqual(0, exit, error.ToString());
        Assert.AreEqual("", error.ToString());
        using JsonDocument document = JsonDocument.Parse(output.ToString());
        JsonElement comparison = document.RootElement;
        Assert.HasCount(3, comparison.EnumerateObject().ToArray());
        Assert.AreEqual(basis, comparison.GetProperty("basis").GetString());
        Assert.AreEqual(candidate, comparison.GetProperty("candidate").GetString());
        Assert.IsFalse(comparison.GetProperty("full").GetBoolean());
    }

    [TestMethod]
    [DataRow("malformed")]
    [DataRow("nonobject")]
    [DataRow("missing-field")]
    [DataRow("unsupported-event")]
    [DataRow("missing-file")]
    [DataRow("missing-argument")]
    public void RunInvalidComparisonReturnsInputErrorWithoutSuccessJson(string defect)
    {
        using var files = new TransferFiles();
        string eventPath = files.Write("native-event.json", defect switch
        {
            "malformed" => "{broken",
            "nonobject" => "[]",
            _ => "{}",
        });
        string eventName = defect == "unsupported-event" ? "schedule" : "pull_request";
        if (defect == "missing-file")
            eventPath = Path.Combine(files.Root, "absent.json");
        string[] args = ["ci", "comparison", eventName, eventPath, new('b', 40)];
        if (defect == "missing-argument")
            args = args[..^1];
        using var output = new StringWriter();
        using var error = new StringWriter();

        int exit = Program.Run(args, output, error);

        Assert.AreEqual(2, exit);
        Assert.AreEqual("", output.ToString());
        Assert.IsNotEmpty(error.ToString());
    }

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
        Assert.AreEqual("base", outcome.GetProperty("comparison").GetString());
        Assert.AreEqual("candidate", outcome.GetProperty("candidate").GetString());
        Assert.AreEqual("fixture-projects", outcome.GetProperty("scope").GetString());
        Assert.AreEqual(passed, outcome.GetProperty("satisfied").GetBoolean());
        Assert.AreEqual(status, outcome.GetProperty("checks")[0].GetProperty("status").GetString());
        Assert.AreEqual(0, outcome.GetProperty("errors").GetArrayLength());
    }

    [TestMethod]
    [DataRow("base-one", "candidate-one", "dotnet-subset")]
    [DataRow("base-two", "candidate-two", "node-subset")]
    public void RunEmptyPlanPreservesResultSubject(string comparison, string candidate,
        string scope)
    {
        using var files = new TransferFiles();
        var plan = new CiPlan(comparison, candidate, scope, []);
        string planPath = files.Write("plan.json", JsonSerializer.Serialize(plan,
            TransferJson.Default.CiPlan));
        string resultsPath = files.Write("results.json", "[]");
        using var output = new StringWriter();
        using var error = new StringWriter();

        int exit = Program.Run(["ci", "result", planPath, resultsPath], output, error);

        Assert.AreEqual(0, exit, error.ToString());
        Assert.AreEqual(string.Empty, error.ToString());
        using JsonDocument document = JsonDocument.Parse(output.ToString());
        JsonElement outcome = document.RootElement;
        Assert.AreEqual(comparison, outcome.GetProperty("comparison").GetString());
        Assert.AreEqual(candidate, outcome.GetProperty("candidate").GetString());
        Assert.AreEqual(scope, outcome.GetProperty("scope").GetString());
        Assert.IsTrue(outcome.GetProperty("satisfied").GetBoolean());
        Assert.AreEqual(0, outcome.GetProperty("checks").GetArrayLength());
        Assert.AreEqual(0, outcome.GetProperty("errors").GetArrayLength());
    }

    [TestMethod]
    [DataRow(true, "")]
    [DataRow(true, " ")]
    [DataRow(false, "")]
    [DataRow(false, " ")]
    public void RunBlankReleaseUnitRejectsWithoutMergingUnrelatedProjects(bool basis,
        string releaseUnit)
    {
        using var files = new TransferFiles();
        PlanRequest request = Scenario.Request(
            [Scenario.Project("library"), Scenario.Project("unrelated")], "src/library/code.cs");
        RepositoryFacts facts = basis ? request.Basis : request.Candidate;
        facts = facts with
        {
            Projects = facts.Projects.Select(p => p with { ReleaseUnit = releaseUnit }).ToArray(),
        };
        request = basis ? request with { Basis = facts } : request with { Candidate = facts };
        string requestPath = files.Write("request.json", JsonSerializer.Serialize(request,
            TransferJson.Default.PlanRequest));
        using var output = new StringWriter();
        using var error = new StringWriter();

        int exit = Program.Run(["ci", "plan", requestPath], output, error);

        Assert.AreEqual(2, exit);
        Assert.AreEqual(string.Empty, output.ToString());
        Assert.Contains("release unit", error.ToString());
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
    [DataRow("numeric-status")]
    [DataRow("numeric-string-status")]
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
            case "numeric-status": json[0]!["status"] = 0; break;
            case "numeric-string-status": json[0]!["status"] = "0"; break;
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
    [DataRow("plan")]
    [DataRow("result")]
    public void RunNullPackageOutputReturnsInputErrorWithoutJson(string command)
    {
        using var files = new TransferFiles();
        CheckSpec check = Scenario.Check("library") with
        {
            Package = new("product", "workflow-delivery.release-unit.yml", "build",
                "node/npm-package-v1", "src/library", "src/library/package.json", null,
                "1.2.3", [new("package", "primary-package", "npm-tarball")]),
        };
        string[] arguments;
        if (command == "plan")
        {
            PlanRequest request = Scenario.Request(
                [Scenario.Project("library", checks: [check])], "src/library/code.cs");
            JsonNode json = JsonNode.Parse(JsonSerializer.Serialize(request,
                TransferJson.Default.PlanRequest))!;
            json["candidate"]!["projects"]![0]!["checks"]![0]!["package"]!["outputs"]![0] = null;
            arguments = ["ci", "plan", files.Write("request.json", json.ToJsonString())];
        }
        else
        {
            JsonNode json = JsonNode.Parse(JsonSerializer.Serialize(Scenario.Plan(check),
                TransferJson.Default.CiPlan))!;
            json["checks"]![0]!["work"]!["package"]!["outputs"]![0] = null;
            arguments = ["ci", "result", files.Write("plan.json", json.ToJsonString()),
                files.Write("results.json", "[]")];
        }
        using var output = new StringWriter();
        using var error = new StringWriter();

        int exit = Program.Run(arguments, output, error);

        Assert.AreEqual(2, exit);
        Assert.AreEqual(string.Empty, output.ToString());
        Assert.IsNotEmpty(error.ToString());
        Assert.Contains("output", error.ToString());
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


    [TestMethod]
    public async Task SerializedPlanPreservesPackageExecutionAssociations()
    {
        NodeRevisionInputs basis = NodeScenario.Inputs(NodeScenario.Basis,
            NodeScenario.Project("src/a"));
        NodeRevisionInputs candidate = NodeScenario.Units(
            NodeScenario.Inputs(NodeScenario.Candidate,
                NodeScenario.Project("src/a")), NodeScenario.Unit("product",
            ("first", "src/a"), ("second", "src/a")));
        CiPlan original = await NodeScenario.Plan(basis, candidate, "src/a/index.js");

        string json = JsonSerializer.Serialize(original, TransferJson.Default.CiPlan);
        CiPlan transferred = JsonSerializer.Deserialize(json, TransferJson.Default.CiPlan)!;

        Assert.AreEqual(NodeScenario.Basis, transferred.Comparison);
        Assert.AreEqual(NodeScenario.Candidate, transferred.Candidate);
        Assert.AreEqual(NodeFactsAssembler.Scope, transferred.Scope);
        Assert.HasCount(8, transferred.Checks);
        foreach (string build in new[] { "first", "second" })
        {
            PlannedCheck[] work = transferred.Checks.Where(check =>
                check.Work.Package?.Build == build).ToArray();
            Assert.HasCount(3, work);
            foreach (PlannedCheck check in work)
            {
                PackageTarget subject = check.Work.Package!;
                Assert.AreEqual("product", subject.Unit);
                Assert.AreEqual("workflow-delivery.release-unit.yml", subject.Declaration);
                Assert.AreEqual(build, subject.Build);
                Assert.AreEqual("node/npm-package-v1", subject.Definition);
                Assert.AreEqual("src/a", subject.Directory);
                Assert.AreEqual("src/a/package.json", subject.EntryPoint);
                Assert.IsNull(subject.PublishDirectory);
                Assert.AreEqual("1.2.3", subject.ExpectedVersion);
                Assert.AreEqual(new PackageOutput(build + "-package", "primary-package",
                    "npm-tarball"), Assert.ContainsSingle(subject.Outputs));
                Assert.AreEqual("release/product/" + build, check.Work.Key.Target);
                Assert.AreEqual("default", check.Work.Key.Variant);
                Assert.AreEqual("ubuntu-latest", check.Work.Runner);
                Assert.IsTrue(check.Work.Required);
                Assert.IsEmpty(check.Work.Dimensions);
                Assert.AreEqual(NodeScenario.Preset, Assert.ContainsSingle(check.QualityPresets));
                Assert.Contains(new SelectionReason("src/a/index.js", NodeScenario.Candidate,
                    "src/a"), check.Reasons);
            }
            PlannedCheck pack = work.Single(check =>
                check.Work.Key.Check == "node/npm-artifact-v1");
            Assert.AreEqual(new CheckKey("src/a", "node/project-build-v1", "default"),
                Assert.ContainsSingle(pack.Work.Prerequisites));
            foreach (PlannedCheck check in work.Where(check => check != pack))
                Assert.AreEqual(pack.Work.Key, Assert.ContainsSingle(check.Work.Prerequisites));
        }
    }

    [TestMethod]
    public async Task CollectRequiresAllSelectedCandidatePackageChecks()
    {
        NodeRevisionInputs basis = NodeScenario.Inputs(NodeScenario.Basis,
            NodeScenario.Project("src/a"));
        NodeRevisionInputs candidate = NodeScenario.Units(
            NodeScenario.Inputs(NodeScenario.Candidate,
                NodeScenario.Project("src/a")), NodeScenario.Unit("product",
            ("first", "src/a"), ("second", "src/a")));
        CiPlan plan = await NodeScenario.Plan(basis, candidate, "src/a/index.js");
        CheckResult[] complete = plan.Checks.Select(check =>
            new CheckResult(plan.Candidate, check.Work.Key, CheckStatus.Passed)).ToArray();
        Assert.IsTrue(ResultCollector.Collect(plan, complete).Satisfied);
        foreach (PlannedCheck selected in plan.Checks.Where(check =>
            check.Work.Package is not null))
        {
            CheckResult[] missing = complete.Where(result =>
                result.Key != selected.Work.Key).ToArray();
            CiOutcome outcome = ResultCollector.Collect(plan, missing);
            Assert.IsFalse(outcome.Satisfied, selected.Work.Key.ToString());
            Assert.AreEqual("missing", outcome.Checks.Single(check =>
                check.Key == selected.Work.Key).Status);
            CheckResult[] failed = complete.Select(result => result.Key == selected.Work.Key
                ? result with { Status = CheckStatus.Failed } : result).ToArray();
            Assert.IsFalse(ResultCollector.Collect(plan, failed).Satisfied);
            CheckResult[] skipped = complete.Select(result => result.Key == selected.Work.Key
                ? result with { Status = CheckStatus.Skipped } : result).ToArray();
            Assert.IsFalse(ResultCollector.Collect(plan, skipped).Satisfied);
        }
        CheckResult[] wrongCandidate = complete.Select(result =>
            result with { Candidate = NodeScenario.Basis }).ToArray();
        CiOutcome incompatible = ResultCollector.Collect(plan, wrongCandidate);
        Assert.IsFalse(incompatible.Satisfied);
        Assert.IsNotEmpty(incompatible.Errors);
    }

    [TestMethod]
    public void RunNodeSerializesBoundCommandsAndOutputs()
    {
        using var files = new TransferFiles();
        CiPlan plan = NodeExecutionFixture.CreatePlan(NodeScenario.Candidate);
        var request = new NodeRunRequest(Path.Combine(files.Root, "checkout"),
            Path.Combine(files.Root, "scratch"), "node24");
        string planPath = files.Write("plan.json", JsonSerializer.Serialize(plan,
            TransferJson.Default.CiPlan));
        string requestPath = files.Write("request.json", JsonSerializer.Serialize(request,
            TransferJson.Default.NodeRunRequest));
        CheckKey key = plan.Checks.Single(item =>
            item.Work.Key.Check == NodeExecutionFixture.Pack).Work.Key;
        var command = new NativeCommand("pnpm", request.Checkout,
            ["--dir", "literal source", "pack", "--out", "original.tgz"], 300);
        var original = new NodeOriginalOutput("product", "main", "main-package",
            "primary-package", "npm-tarball", "original.tgz", 123, new string('a', 64));
        var observation = new NodeCommandObservation(key, command,
            NodeExecutionFixture.Success("visible output"));
        bool invoked = false;
        using var output = new StringWriter();
        using var error = new StringWriter();

        int exit = Program.Run(["ci", "run-node", planPath, requestPath], output, error,
            (receivedPlan, receivedRequest, _) =>
            {
                invoked = true;
                Assert.HasCount(7, receivedPlan.Checks);
                Assert.AreEqual(plan.Candidate, receivedPlan.Candidate);
                Assert.AreEqual(request, receivedRequest);
                Assert.AreEqual("main-package", receivedPlan.Checks.Single(item =>
                    item.Work.Key == key).Work.Package!.Outputs.Single().Id);
                return Task.FromResult(new NodeRunResult(plan.Candidate,
                    plan.Checks.Where(item => item.Work.Key.Variant == request.Runtime)
                        .Select(item => new CheckResult(plan.Candidate, item.Work.Key,
                        CheckStatus.Passed)).ToArray(), [observation], [original], [],
                    request.Runtime,
                    new("24.21.0", "12.8.2", NodeExecutionFixture.Success("v24.21.0\n"),
                        NodeExecutionFixture.Success("12.8.2\n"))));
            });

        Assert.IsTrue(invoked);
        Assert.AreEqual(0, exit, error.ToString());
        Assert.AreEqual("", error.ToString());
        NodeRunResult transferred = JsonSerializer.Deserialize(output.ToString(),
            TransferJson.Default.NodeRunResult)!;
        Assert.AreEqual(plan.Candidate, transferred.Candidate);
        Assert.HasCount(5, transferred.Results);
        Assert.AreEqual("node24", transferred.Runtime);
        Assert.AreEqual("24.21.0", transferred.RuntimeVersions!.NodeVersion);
        Assert.AreEqual("12.8.2", transferred.RuntimeVersions.PnpmVersion);
        Assert.IsFalse(ResultCollector.Collect(plan, transferred.Results).Satisfied);
        Assert.AreEqual(original, Assert.ContainsSingle(transferred.Outputs));
        NodeCommandObservation received = Assert.ContainsSingle(transferred.Commands);
        Assert.AreEqual(key, received.Key);
        Assert.AreEqual(command.Executable, received.Command.Executable);
        Assert.AreEqual(command.Directory, received.Command.Directory);
        Assert.AreEqual(command.DeadlineSeconds, received.Command.DeadlineSeconds);
        CollectionAssert.AreEqual(command.Arguments, received.Command.Arguments);
        Assert.AreEqual(observation.Result, received.Result);
        using JsonDocument json = JsonDocument.Parse(output.ToString());
        Assert.AreEqual("Exited", json.RootElement.GetProperty("commands")[0]
            .GetProperty("result").GetProperty("termination").GetString());
        Assert.AreEqual("Passed", json.RootElement.GetProperty("results")[0]
            .GetProperty("status").GetString());
    }

    [TestMethod]
    [DataRow("Failed")]
    [DataRow("Skipped")]
    [DataRow("Cancelled")]
    [DataRow("TimedOut")]
    [DataRow("missing")]
    [DataRow("wrong-candidate")]
    public void RunNodeFailedOrMissingResultsCannotSucceed(string defect)
    {
        using var files = new TransferFiles();
        CiPlan plan = NodeExecutionFixture.CreatePlan(NodeScenario.Candidate);
        string planPath = files.Write("plan.json", JsonSerializer.Serialize(plan,
            TransferJson.Default.CiPlan));
        string requestPath = files.Write("request.json", JsonSerializer.Serialize(
            new NodeRunRequest(files.Root, files.Root, "node24"),
            TransferJson.Default.NodeRunRequest));
        CheckResult[] results = plan.Checks
            .Where(item => item.Work.Key.Variant == "node24").Select(item =>
            new CheckResult(plan.Candidate, item.Work.Key, CheckStatus.Passed)).ToArray();
        results = defect switch
        {
            "missing" => results[..^1],
            "wrong-candidate" => results.Select(item => item with
            { Candidate = NodeScenario.Basis }).ToArray(),
            _ => results.Select((item, index) => index == results.Length - 1
                ? item with { Status = Enum.Parse<CheckStatus>(defect) } : item).ToArray(),
        };
        using var output = new StringWriter();
        using var error = new StringWriter();

        int exit = Program.Run(["ci", "run-node", planPath, requestPath], output, error,
            (_, _, _) => Task.FromResult(new NodeRunResult(plan.Candidate, results, [], [], [],
                "node24", null)));

        Assert.AreEqual(1, exit);
        Assert.AreEqual("", error.ToString());
        NodeRunResult transferred = JsonSerializer.Deserialize(output.ToString(),
            TransferJson.Default.NodeRunResult)!;
        Assert.HasCount(results.Length, transferred.Results);
        Assert.IsFalse(ResultCollector.Collect(plan, transferred.Results).Satisfied);
    }

    [TestMethod]
    [DataRow("malformed-request")]
    [DataRow("null-request")]
    [DataRow("missing-property")]
    [DataRow("null-property")]
    [DataRow("unknown-property")]
    [DataRow("malformed-plan")]
    [DataRow("null-plan")]
    [DataRow("missing-runtime")]
    [DataRow("null-runtime")]
    [DataRow("unsupported-runtime")]
    public void RunNodeMalformedTransferReturnsInputErrorWithoutExecution(string defect)
    {
        using var files = new TransferFiles();
        CiPlan plan = NodeExecutionFixture.CreatePlan(NodeScenario.Candidate);
        string planJson = JsonSerializer.Serialize(plan, TransferJson.Default.CiPlan);
        var request = new NodeRunRequest(files.Root, Path.Combine(files.Root, "scratch"), "node24");
        JsonObject json = JsonNode.Parse(JsonSerializer.Serialize(request,
            TransferJson.Default.NodeRunRequest))!.AsObject();
        if (defect == "missing-property")
            json.Remove("scratch");
        if (defect == "null-property")
            json["checkout"] = null;
        if (defect == "unknown-property")
            json["embeddedNode"] = true;
        if (defect == "missing-runtime")
            json.Remove("runtime");
        if (defect == "null-runtime")
            json["runtime"] = null;
        if (defect == "unsupported-runtime")
            json["runtime"] = "default";
        string requestJson = defect switch
        {
            "malformed-request" => "{broken",
            "null-request" => "null",
            _ => json.ToJsonString(),
        };
        planJson = defect switch
        {
            "malformed-plan" => "{broken",
            "null-plan" => "null",
            _ => planJson,
        };
        string planPath = files.Write("plan.json", planJson);
        string requestPath = files.Write("request.json", requestJson);
        bool invoked = false;
        using var output = new StringWriter();
        using var error = new StringWriter();

        int exit = Program.Run(["ci", "run-node", planPath, requestPath], output, error,
            (_, _, _) =>
            {
                invoked = true;
                throw new AssertFailedException("Invalid transfer reached executor.");
            });

        Assert.IsFalse(invoked);
        Assert.AreEqual(2, exit);
        Assert.AreEqual("", output.ToString());
        Assert.IsNotEmpty(error.ToString());
    }

    [TestMethod]
    public void RunNodeSemanticInputErrorProducesNoExecutedOutcome()
    {
        using var files = new TransferFiles();
        CiPlan plan = NodeExecutionFixture.CreatePlan(NodeScenario.Candidate);
        string planPath = files.Write("plan.json", JsonSerializer.Serialize(plan,
            TransferJson.Default.CiPlan));
        string requestPath = files.Write("request.json", JsonSerializer.Serialize(
            new NodeRunRequest("relative-checkout", "relative-scratch", "node24"),
            TransferJson.Default.NodeRunRequest));
        using var output = new StringWriter();
        using var error = new StringWriter();

        int exit = Program.Run(["ci", "run-node", planPath, requestPath], output, error);

        Assert.AreEqual(2, exit);
        Assert.AreEqual("", output.ToString());
        Assert.Contains("absolute", error.ToString());
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
