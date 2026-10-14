using WorkflowDelivery.CI;
using WorkflowDelivery.CI.Callers;
using WorkflowDelivery.Platform;
using WorkflowDelivery.Repository;

namespace WorkflowDelivery.Tests.CI;

[TestClass]
public sealed class CiCallerPythonTests(TestContext context)
{
    [TestMethod]
    public void AuxiliarySelectsOnlyActualRetainedNativeConsumers()
    {
        CiPlan plan = Plan("tests/private/app/workflow-delivery/Native/Python",
            "tests/eng/test_legacy_release_contract.py",
                "src/private/app/azureauth-credprovider/python/tests");
        Assert.AreEqual(new PythonCiAuxiliary(true, true, true), PythonCiCaller.Auxiliary(plan));
        Assert.AreEqual(new PythonCiAuxiliary(false, false, false), PythonCiCaller.Auxiliary(Plan(
            "tests/private/app/workflow-delivery/Native/Ruby",
                "tests/eng/test_python_release_preparation_gate.py",
            "tests/eng/test_ruby_release_preparation_gate.py")));
        Assert.AreEqual(new PythonCiAuxiliary(false, false, false),
            PythonCiCaller.Auxiliary(plan with { Checks = [] }));
    }

    [TestMethod]
    public async Task CargoGateUsesPreparedNativeIdentityAndRetainsOriginalReports()
    {
        using var files = new CiCallerFiles();
        NativeCommand original = Compilation(files.Root);
        var seen = new List<NativeCommand>();
        await PythonCiCaller.CargoGateAsync(new(true, false, false),
            [new(original, Passed())], files.Root, (command, _) =>
            {
                seen.Add(command);
                return Task.FromResult(Passed());
            }, context.CancellationToken);
        Assert.AreEqual("fmt,test,clippy", string.Join(',', seen.Select(c => c.Arguments[0])));
        Assert.IsTrue(seen.All(command => command.Executable == original.Executable &&
            command.Directory == original.Directory && !command.InheritEnvironment &&
            command.Environment!["CARGO_HOME"] == original.Environment!["CARGO_HOME"]));
        Assert.IsTrue(seen[0].Arguments.SequenceEqual(["fmt", "--all", "--", "--check"]));
        Assert.Contains("--locked", seen[1].Arguments);
        Assert.Contains("warnings", seen[2].Arguments);
        foreach (string label in new[] { "format", "test", "clippy" })
        {
            CiCallerCommand retained = CiCallerTransfer.Read(Path.Combine(files.Root,
                "native-helper-" + label + ".command.json"), TransferJson.Default.CiCallerCommand);
            Assert.AreEqual(original.Executable, retained.Command.Executable);
            Assert.AreEqual("original stdout", retained.Result.Stdout);
            Assert.IsTrue(retained.Result.Succeeded);
        }
    }

    [TestMethod]
    [DataRow((int)NativeTermination.Exited, 1)]
    [DataRow((int)NativeTermination.Cancelled, null)]
    [DataRow((int)NativeTermination.TimedOut, null)]
    [DataRow((int)NativeTermination.InvalidOutput, null)]
    public async Task CargoGateAbnormalNativeOutcomeStopsDependentExecution(int value, int? code)
    {
        var termination = (NativeTermination)value;
        using var files = new CiCallerFiles();
        NativeCommand original = Compilation(files.Root);
        await Assert.ThrowsExactlyAsync<IOException>(() => PythonCiCaller.CargoGateAsync(
            new(true, false, false), [new(original, Passed())], files.Root,
            (_, _) => Task.FromResult(new NativeCommandResult(termination, code,
                "original stdout", "original stderr", 1, "native failure")),
            context.CancellationToken));
        CiCallerCommand report = CiCallerTransfer.Read(Path.Combine(files.Root,
            "native-helper-format.command.json"), TransferJson.Default.CiCallerCommand);
        Assert.AreEqual(termination, report.Result.Termination);
        Assert.AreEqual(code, report.Result.ExitCode);
        Assert.AreEqual("original stderr", report.Result.Stderr);
        Assert.IsFalse(File.Exists(Path.Combine(files.Root, "native-helper-test.command.json")));
    }

    [TestMethod]
    public async Task CargoGateUnselectedHelperDoesNotRequireCompilation()
    {
        using var files = new CiCallerFiles();
        await PythonCiCaller.CargoGateAsync(new(false, false, false), [], files.Root,
            (_, _) => throw new
                AssertFailedException("Unselected native helper must not execute."),
                context.CancellationToken);
        Assert.IsEmpty(Directory.EnumerateFileSystemEntries(files.Root));
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task ExecutePreservesNativeContextAndOriginalUnsatisfiedResults(bool missing)
    {
        using var files = new CiCallerFiles();
        string candidate = new('a', 40);
        string checkout = Path.Combine(files.Root, "candidate");
        Directory.CreateDirectory(checkout);
        var endpoint = new MaterializedEndpoint(checkout, candidate);
        var callerContext = new PythonCiCallerContext(endpoint, endpoint, true,
            Path.Combine(files.Root, "scope.json"), Path.Combine(files.Root,
                "WorkflowDelivery.dll"),
            Path.Combine(files.Root, "control-source"));
        var operation = new PythonPytestOperation("ubuntu-latest", "python-native", new()
        {
            ["python"] = "3.14"
        });
        var graph = new PythonGraphRequest(checkout, Path.Combine(files.Root, "uv-shim"),
            Path.Combine(files.Root, "python-shim"), Path.Combine(files.Root, "helper-python"),
            "passive.py", "supplement", "cache", "bootstrap", "activity");
        var environment = new Dictionary<string, string?>
        {
            ["PATH"] = "native path",
            ["UV_CACHE_DIR"] = "prepared cache",
            ["PYTHONSAFEPATH"] = "1"
        };
        var preparedEndpoint = new PythonGroupEndpoint(endpoint, new(graph, "pytest.py",
            "packages.py", operation), environment);
        var preparation = new PythonGroupPreparationResult(new(preparedEndpoint,
            preparedEndpoint, true, callerContext.ScopePath), []);
        var plan = new CiPlan(candidate, candidate, PythonCiCaller.Scope,
            [new(new(new("repository", "python/pytest-v1", "python-native"), new() {
                ["testPath"] = "tests/root" },
                "ubuntu-latest", true, []), [], [], [CheckOrigin.NativeRetained])]);
        var readback = new PythonCiCallerReadback([new("native-root", ".", "root")],
            graph.Interpreter,
            new("pyproject.toml", ["tests/root"], []), new(candidate, "pyproject.toml",
                [new("tests/root", null, [])]), operation);
        CiCallerTransfer.Write(Path.Combine(files.Root, "context.json"), callerContext,
            TransferJson.Default.PythonCiCallerContext);
        CiCallerTransfer.Write(Path.Combine(files.Root, "plan.json"), plan,
            TransferJson.Default.CiPlan);
        CiCallerTransfer.Write(Path.Combine(files.Root, "preparation.json"), preparation,
            TransferJson.Default.PythonGroupPreparationResult);
        CiCallerTransfer.Write(Path.Combine(files.Root, "readback.json"), readback,
            TransferJson.Default.PythonCiCallerReadback);
        CiOutcome outcome = await PythonCiCaller.ExecuteAsync(files.Root, null, null, null, null,
            context.CancellationToken, (received, request, _) =>
            {
                Assert.AreEqual(plan.Checks[0].Work.Key, received.Checks[0].Work.Key);
                Assert.AreEqual(checkout, request.Native.Checkout);
                Assert.AreEqual(graph.Uv, request.Native.Uv);
                Assert.AreEqual(graph.Interpreter, request.Native.Interpreter);
                Assert.AreEqual("native-root", request.Native.Members.Single().Name);
                Assert.AreEqual("prepared cache", request.Native.Environment["UV_CACHE_DIR"]);
                Assert.IsNull(request.Native.Environment["PYTHONSAFEPATH"]);
                Assert.AreEqual("packages.py", request.PackageHelper);
                CheckResult[] results = missing ? [] : [new(candidate,
                    received.Checks[0].Work.Key, CheckStatus.Passed)];
                return Task.FromResult(new PythonGroupRunResult(candidate, results,
                    new(candidate, results, [], []), new(candidate, [], [], [], []),
                        ResultCollector.Collect(received, results)));
            });
        Assert.AreEqual(!missing, outcome.Satisfied);
        Assert.AreEqual(missing ? "missing" : "passed", outcome.Checks.Single().Status);
        Assert.AreEqual("1", environment["PYTHONSAFEPATH"]);
        Assert.IsTrue(File.Exists(Path.Combine(files.Root, "execution-request.json")));
        Assert.IsTrue(File.Exists(Path.Combine(files.Root, "result.json")));
        CiCallerTransfer.Write(Path.Combine(files.Root, "preparation.json"), preparation with
        {
            Request = preparation.Request with
            {
                Candidate = preparedEndpoint with
                { Checkout = endpoint with { Reference = new('b', 40) } }
            }
        }, TransferJson.Default.PythonGroupPreparationResult);
        await Assert.ThrowsExactlyAsync<InvalidDataException>(() => PythonCiCaller.ExecuteAsync(
            files.Root, null, null, null, null, context.CancellationToken));
    }

    private static NativeCommand Compilation(string root) => new(Path.Combine(root,
        "cargo-shim"), root,
        ["build", "--locked"], 900, new Dictionary<string, string?>
        { ["PATH"] = "prepared path", ["CARGO_HOME"] = Path.Combine(root, "cargo-home") },
            InheritEnvironment: false);
    private static NativeCommandResult Passed() => new(NativeTermination.Exited, 0,
        "original stdout", "", 1, null);
    private static CiPlan Plan(params string[] targets) => new(new('a', 40), new('a', 40),
        PythonCiCaller.Scope,
        targets.Select(target => new PlannedCheck(new(new("root", "python/pytest-v1", target),
            new() { ["testPath"] = target }, "ubuntu-latest", true, []), [], [],
                [CheckOrigin.NativeRetained])).ToArray());
}
