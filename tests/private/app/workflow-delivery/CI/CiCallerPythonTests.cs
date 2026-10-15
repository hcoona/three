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
        Assert.AreEqual(new PythonCiAuxiliary(true, true), PythonCiCaller.Auxiliary(plan));
        Assert.AreEqual(new PythonCiAuxiliary(false, false), PythonCiCaller.Auxiliary(Plan(
            "tests/private/app/workflow-delivery/Native/Ruby",
                "tests/eng/test_python_release_preparation_gate.py",
            "tests/eng/test_ruby_release_preparation_gate.py")));
        Assert.AreEqual(new PythonCiAuxiliary(false, false),
            PythonCiCaller.Auxiliary(plan with { Checks = [] }));
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
            "passive.py");
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

    private static CiPlan Plan(params string[] targets) => new(new('a', 40), new('a', 40),
        PythonCiCaller.Scope,
        targets.Select(target => new PlannedCheck(new(new("root", "python/pytest-v1", target),
            new() { ["testPath"] = target }, "ubuntu-latest", true, []), [], [],
                [CheckOrigin.NativeRetained])).ToArray());
}
