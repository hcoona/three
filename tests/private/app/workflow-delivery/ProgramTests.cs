using System.Text.Json;
using WorkflowDelivery.CI;
using WorkflowDelivery.Platform;
using WorkflowDelivery.Repository;
using WorkflowDelivery.Tests.CI;
using WorkflowDelivery.Tests.Repository;

namespace WorkflowDelivery.Tests;

[TestClass]
public sealed class ProgramTests
{
    [TestMethod]
    [DataRow("pull_request", false)]
    [DataRow("workflow_dispatch", true)]
    public void ComparisonEmitsTestedCandidateNativeBasisAndFullFlag(string eventName, bool full)
    {
        using var files = new TransferFiles();
        string basis = new('a', 40);
        string candidate = new('b', 40);
        string eventPath = files.Write("event.json", full ? "{}" : $$$$"""
            {"pull_request":{"base":{"sha":"{{{{basis}}}}"},
                "head":{"sha":"{{{{new string('c', 40)}}}}"}}}
            """);
        using var output = new StringWriter();
        using var error = new StringWriter();

        int exit = Program.Run(["ci", "comparison", eventName, eventPath, candidate],
            output, error);

        Assert.AreEqual(0, exit, error.ToString());
        Assert.IsEmpty(error.ToString());
        using JsonDocument document = JsonDocument.Parse(output.ToString());
        Assert.AreEqual(full ? candidate : basis,
            document.RootElement.GetProperty("basis").GetString());
        Assert.AreEqual(candidate, document.RootElement.GetProperty("candidate").GetString());
        Assert.AreEqual(full, document.RootElement.GetProperty("full").GetBoolean());
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

    [TestMethod]
    [DataRow(false, 0, "passed")]
    [DataRow(true, 1, "failed")]
    public void PythonPreparationFeedsPlanAndRunWithOriginalNativeContext(bool failed,
        int expectedExit, string status)
    {
        using var files = new TransferFiles();
        using var output = new StringWriter();
        using var error = new StringWriter();
        PythonGroupRequest prepared = PythonRequest();
        PythonGroupPreparationRequest request = PythonPreparation(prepared, files.Root);
        string path = files.Write("prepare.json", JsonSerializer.Serialize(request,
            TransferJson.Default.PythonGroupPreparationRequest));

        Assert.AreEqual(0, Program.Run(["ci", "prepare-python-group", path], output, error,
            preparePythonGroup: (received, _) =>
            {
                Assert.AreEqual(request.Basis, received.Basis);
                Assert.AreEqual(request.Candidate, received.Candidate);
                Assert.AreEqual(request.Tools, received.Tools);
                return Task.FromResult(new PythonGroupPreparationResult(prepared with
                { Full = received.Full, ScopePath = received.ScopePath }, []));
            }), error.ToString());
        var preparation = JsonSerializer.Deserialize(output.ToString(),
            TransferJson.Default.PythonGroupPreparationResult);
        Assert.IsNotNull(preparation);
        path = files.Write("planning.json", JsonSerializer.Serialize(preparation.Request,
            TransferJson.Default.PythonGroupRequest));
        output.GetStringBuilder().Clear();
        string candidate = prepared.Candidate.Checkout.Reference;
        var configuration = new PythonPytestConfiguration("pyproject.toml", ["tests/root.py"], []);
        var targets = new PythonPytestTargetSet(candidate, configuration.ConfigurationFile,
            [new("tests/root.py", null, ["tests/root.py"])]);
        PythonPytestOperation operation = prepared.Candidate.Collection.Operation;
        CheckSpec check = Assert.ContainsSingle(PythonPytestChecks.Expand(configuration, targets,
            operation));
        var readback = new PythonGroupReadback(new(prepared.Basis.Checkout.Reference, candidate,
            PythonFactsAssembler.Scope, [new(check, [], [], [CheckOrigin.NativeRetained])]),
            [new("native-root", ".", "root-id")], "/runtime/python", configuration, targets,
            operation);

        Assert.AreEqual(0, Program.Run(["ci", "plan-python-group", path], output, error,
            planPythonGroup: (received, _) =>
            {
                Assert.AreEqual(prepared.Basis.Checkout, received.Basis.Checkout);
                Assert.AreEqual(prepared.Candidate.Checkout, received.Candidate.Checkout);
                Assert.IsTrue(received.Full);
                Assert.AreEqual(prepared.ScopePath, received.ScopePath);
                return Task.FromResult(readback);
            }), error.ToString());
        var planning = JsonSerializer.Deserialize(output.ToString(),
            TransferJson.Default.PythonGroupReadback);
        Assert.IsNotNull(planning);
        string planPath = files.Write("plan.json", JsonSerializer.Serialize(planning.Plan,
            TransferJson.Default.CiPlan));
        var run = new PythonGroupRunRequest(new(prepared.Candidate.Checkout.Directory, files.Root,
            request.Tools.Uv, planning.Interpreter, planning.Members, planning.Configuration,
            planning.Targets, planning.Operation, 30, prepared.Candidate.Environment),
            "/control/packages.py");
        path = files.Write("run.json", JsonSerializer.Serialize(run,
            TransferJson.Default.PythonGroupRunRequest));
        output.GetStringBuilder().Clear();

        int exit = Program.Run(["ci", "run-python-group", planPath, path], output, error,
            runPythonGroup: (plan, native, _) =>
            {
                Assert.AreEqual(prepared.Basis.Checkout.Reference, plan.Comparison);
                Assert.AreEqual(candidate, plan.Candidate);
                Assert.AreEqual("tests/root.py", Assert.ContainsSingle(native.Native.Targets
                    .Targets).Target);
                Assert.AreEqual(run.PackageHelper, native.PackageHelper);
                CheckResult[] results = [new(plan.Candidate, Assert.ContainsSingle(plan.Checks)
                    .Work.Key, failed ? CheckStatus.Failed : CheckStatus.Passed)];
                return Task.FromResult(new PythonGroupRunResult(plan.Candidate, results,
                    new(plan.Candidate, results, [], []), new(plan.Candidate, [], [], [], []),
                    ResultCollector.Collect(plan, results)));
            });

        Assert.AreEqual(expectedExit, exit, error.ToString());
        var result = JsonSerializer.Deserialize(output.ToString(),
            TransferJson.Default.PythonGroupRunResult);
        Assert.IsNotNull(result);
        Assert.AreEqual(candidate, result.Candidate);
        Assert.AreEqual(!failed, result.Outcome.Satisfied);
        Assert.AreEqual(status, Assert.ContainsSingle(result.Outcome.Checks).Status);
        Assert.IsEmpty(error.ToString());
    }

    [TestMethod]
    public void FailedPythonPackageQualificationEmitsOriginalDistributions()
    {
        using var files = new TransferFiles();
        using var output = new StringWriter();
        using var error = new StringWriter();
        string candidate = new('a', 40);
        var plan = new CiPlan(candidate, candidate, PythonFactsAssembler.Scope,
            PythonPackageExecutionTests.PackageChecks());
        var request = new PythonPackageRunRequest(files.Root, files.Root, "uv", "python",
            "helper.py", PythonPackageChecksTests.Members, 30, new Dictionary<string, string?>());
        string planPath = files.Write("plan.json",
            JsonSerializer.Serialize(plan, TransferJson.Default.CiPlan));
        string requestPath = files.Write("request.json",
            JsonSerializer.Serialize(request, TransferJson.Default.PythonPackageRunRequest));
        PythonOriginalDistribution[] originals = plan.Checks[0].Work.Package!.Outputs
            .Select(item => new PythonOriginalDistribution("product", "distributions", item,
                item.Kind == "python-wheel" ? "original.whl" : "original.tar.gz", 8,
                "original-digest")).ToArray();
        CheckResult[] results = plan.Checks.Select(item => new CheckResult(candidate,
            item.Work.Key, item.Work.Key.Check == PythonPackageChecks.Wheel
                ? CheckStatus.Failed : CheckStatus.Passed)).ToArray();

        int exit = Program.Run(["ci", "run-python-package", planPath, requestPath], output, error,
            runPythonPackage: (_, _, _) => Task.FromResult(
                new PythonPackageRunResult(candidate, results, [], originals, [])));

        Assert.AreEqual(1, exit);
        Assert.IsEmpty(error.ToString());
        var transferred = JsonSerializer.Deserialize(output.ToString(),
            TransferJson.Default.PythonPackageRunResult);
        Assert.IsNotNull(transferred);
        Assert.AreEqual(candidate, transferred.Candidate);
        CollectionAssert.AreEqual(originals, transferred.Distributions);
        CollectionAssert.AreEqual(results, transferred.Results);
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public void FailedPythonPreparationRetainsPrivateDiagnosticsWithoutOverwriting(bool existing)
    {
        using var files = new TransferFiles();
        string path = files.Write("request.json", JsonSerializer.Serialize(
            PythonPreparation(PythonRequest(), files.Root),
            TransferJson.Default.PythonGroupPreparationRequest));
        var command = new NativeCommand("/tools/uv", "/endpoint", ["sync"], 600,
            new Dictionary<string, string?> { ["HOME"] = "/owned/home" }, false);
        var native = new NativeCommandResult(NativeTermination.Exited, 7, "private stdout",
            CiCallerTransferTests.WorkflowCommandText, 1, null);
        var failure = new PythonGroupPreparationException(files.Root, [new(command, native)]);
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
        Assert.DoesNotContain(native.Stdout, diagnostics);
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
            var retained = JsonSerializer.Deserialize(File.ReadAllText(failure.DiagnosticsPath),
                TransferJson.Default.PythonPreparationCommandArray);
            Assert.IsNotNull(retained);
            PythonPreparationCommand observation = Assert.ContainsSingle(retained);
            Assert.AreEqual(native, observation.Result);
            Assert.AreEqual("/owned/home", observation.Command.Environment!["HOME"]);
        }
    }

    [TestMethod]
    public void PythonPlanningErrorCannotStartWorkflowCommands()
    {
        using var files = new TransferFiles();
        string path = files.Write("request.json", JsonSerializer.Serialize(PythonRequest(),
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

    [TestMethod]
    [DataRow(false, 0, "passed")]
    [DataRow(true, 1, "missing")]
    public void RubyPreparationFeedsPlanAndRun(bool missing, int expectedExit, string status)
    {
        using var files = new TransferFiles();
        using var output = new StringWriter();
        using var error = new StringWriter();
        string candidate = new('a', 40);
        var tools = new RubyFactTools(Path.Combine(files.Root, "ruby"),
            Path.Combine(files.Root, "facts.rb"), files.Root,
            new Dictionary<string, string?> { ["GEM_HOME"] = Path.Combine(files.Root, "gems") },
            30);
        var endpoint = new MaterializedEndpoint(files.Root, candidate);
        string nbgv = Path.Combine(files.Root, "nbgv");
        var prepared = new RubyGroupRequest(new(endpoint, new(tools, nbgv, "2.7.2")),
            new(endpoint, new(tools, nbgv, "2.7.2")), true,
            Path.Combine(files.Root, "scope.json"));
        var request = new RubyGroupPreparationRequest(endpoint, endpoint, true,
            prepared.ScopePath, tools.Helper, files.Root,
            new(tools.Ruby, Path.Combine(files.Root, "gem"), Path.Combine(files.Root, "bundle"),
                Path.Combine(files.Root, "dotnet")), "2.7.2", 30);
        string path = files.Write("prepare.json", JsonSerializer.Serialize(request,
            TransferJson.Default.RubyGroupPreparationRequest));

        Assert.AreEqual(0, Program.Run(["ci", "prepare-ruby-group", path], output, error,
            prepareRubyGroup: (received, _) =>
            {
                Assert.AreEqual(request, received);
                return Task.FromResult(new RubyGroupPreparationResult(prepared, []));
            }), error.ToString());
        var preparation = JsonSerializer.Deserialize(output.ToString(),
            TransferJson.Default.RubyGroupPreparationResult);
        Assert.IsNotNull(preparation);
        path = files.Write("planning.json", JsonSerializer.Serialize(preparation.Request,
            TransferJson.Default.RubyGroupRequest));
        output.GetStringBuilder().Clear();
        var key = new CheckKey("src/native", RubyProjectChecks.RSpec, "default");
        var plan = new CiPlan(candidate, candidate, RubyFactsAssembler.Scope,
            [new(new(key, [], "ubuntu-latest", true, []),
                [RubyProjectChecks.Preset], [], [CheckOrigin.Preset])]);
        var inputs = new RubyRevisionInputs(new(new(candidate, []), new(new([], []), []), [], []),
            [], [], []);

        Assert.AreEqual(0, Program.Run(["ci", "plan-ruby-group", path], output, error,
            planRubyGroup: (received, _) =>
            {
                Assert.AreEqual(nbgv, received.Candidate.Collection.Nbgv);
                Assert.AreEqual(tools.Environment["GEM_HOME"],
                    received.Candidate.Collection.Tools.Environment["GEM_HOME"]);
                return Task.FromResult(new RubyGroupReadback(plan, inputs, inputs));
            }), error.ToString());
        var readback = JsonSerializer.Deserialize(output.ToString(),
            TransferJson.Default.RubyGroupReadback);
        Assert.IsNotNull(readback);
        string planPath = files.Write("plan.json", JsonSerializer.Serialize(readback.Plan,
            TransferJson.Default.CiPlan));
        var run = new RubyGroupRunRequest(files.Root, files.Root, tools, request.Tools.Gem,
            request.Tools.Bundle, nbgv, readback.Candidate);
        path = files.Write("run.json", JsonSerializer.Serialize(run,
            TransferJson.Default.RubyGroupRunRequest));
        output.GetStringBuilder().Clear();

        int exit = Program.Run(["ci", "run-ruby-group", planPath, path], output, error,
            runRubyGroup: (received, native, _) =>
            {
                Assert.AreEqual(key, Assert.ContainsSingle(received.Checks).Work.Key);
                Assert.AreEqual(candidate, native.Candidate.Facts.Revision.Commit);
                CheckResult[] results = missing ? [] : [new(candidate, key, CheckStatus.Passed)];
                return Task.FromResult(new RubyGroupRun(results, [], new([], [], [], []),
                    ResultCollector.Collect(received, results)));
            });

        Assert.AreEqual(expectedExit, exit, error.ToString());
        Assert.IsEmpty(error.ToString());
        var result = JsonSerializer.Deserialize(output.ToString(),
            TransferJson.Default.RubyGroupRun);
        Assert.IsNotNull(result);
        Assert.AreEqual(!missing, result.Outcome.Satisfied);
        Assert.AreEqual(candidate, result.Outcome.Candidate);
        Assert.AreEqual(status, Assert.ContainsSingle(result.Outcome.Checks).Status);
    }

    [TestMethod]
    public void NullRubyPreparationRequestCannotDispatch()
    {
        using var files = new TransferFiles();
        using var output = new StringWriter();
        using var error = new StringWriter();
        string path = files.Write("request.json", "null");
        int dispatches = 0;

        int exit = Program.Run(["ci", "prepare-ruby-group", path], output, error,
            prepareRubyGroup: (_, _) =>
            {
                dispatches++;
                throw new AssertFailedException("Null request reached Ruby preparation.");
            });

        Assert.AreEqual(2, exit);
        Assert.IsEmpty(output.ToString());
        Assert.IsNotEmpty(error.ToString());
        Assert.AreEqual(0, dispatches);
    }

    private static PythonGroupPreparationRequest PythonPreparation(PythonGroupRequest request,
        string scratch) => new(request.Basis.Checkout, request.Candidate.Checkout, request.Full,
            request.ScopePath, "/control/python sources", scratch,
            new("/tools/uv", "/runtime/python", "/tools/dotnet"),
            request.Candidate.Collection.Operation, 600);

    private static PythonGroupRequest PythonRequest() => new(PythonEndpoint("/basis", new('a', 40)),
        PythonEndpoint("/candidate", new('b', 40)), true, "/state/scope.json");

    private static PythonGroupEndpoint PythonEndpoint(string root, string revision) => new(
        new(root, revision), new(new(root, "uv", "/runtime/python", root + "/tools/python",
            "/control/passive.py"), "/control/pytest_configuration.py", "/control/packages.py",
            new("ubuntu-latest", "python-3.14",
                new() { ["python"] = "3.14", ["platform"] = "linux" })),
        new Dictionary<string, string?> { ["UV_PROJECT_ENVIRONMENT"] = root + "/environment" });

    [TestMethod]
    public void MissingDotNetTestResultEmitsGroupOutcomeAndExitOne()
    {
        using var files = new TransferFiles();
        using var output = new StringWriter();
        using var error = new StringWriter();
        CiPlan plan = DotNetExecutionFixture.CreatePlan(files.Root, new string('a', 40), "MTP");
        var request = new DotNetRunRequest(files.Root, Path.Combine(files.Root, "scratch"), 60);
        string planPath = files.Write("plan.json", JsonSerializer.Serialize(plan,
            TransferJson.Default.CiPlan));
        string requestPath = files.Write("run.json", JsonSerializer.Serialize(request,
            TransferJson.Default.DotNetRunRequest));

        int exit = Program.Run(["ci", "run-dotnet", planPath, requestPath], output, error,
            runDotNet: (received, native, _) =>
            {
                Assert.AreEqual(request, native);
                CheckResult[] results = [new(received.Candidate, received.Checks.Single(item =>
                    item.Work.Key.Check == DotNetChecks.Build).Work.Key, CheckStatus.Passed)];
                return Task.FromResult(new DotNetGroupRunResult(received.Candidate, results,
                    new(received.Candidate, results, [], []),
                    new(received.Candidate, [], [], [], [], []),
                    ResultCollector.Collect(received, results)));
            });

        Assert.AreEqual(1, exit, error.ToString());
        Assert.IsEmpty(error.ToString());
        var result = JsonSerializer.Deserialize(output.ToString(),
            TransferJson.Default.DotNetGroupRunResult);
        Assert.IsNotNull(result);
        Assert.IsFalse(result.Outcome.Satisfied);
        Assert.AreEqual("missing", result.Outcome.Checks.Single(item =>
            item.Key.Check == DotNetChecks.Test).Status);
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
