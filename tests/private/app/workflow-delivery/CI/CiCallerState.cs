using System.IO.Compression;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Text.Json.Serialization.Metadata;
using WorkflowDelivery.CI;
using WorkflowDelivery.CI.Callers;
using WorkflowDelivery.Repository;
using WorkflowDelivery.Tests.Platform;

namespace WorkflowDelivery.Tests.CI;

internal sealed class CiCallerState : IDisposable
{
    internal const string ActionsOutputSwitch = "--workflow-delivery-test-actions-output";
    internal static readonly string Candidate = new('a', 40);
    internal static readonly string Foreign = new('b', 40);
    private static readonly string[] Distribution =
        ["WorkflowDelivery.dll", "WorkflowDelivery.DotNet.dll",
        "WorkflowDelivery.deps.json", "WorkflowDelivery.runtimeconfig.json"];
    private static readonly string[] Runtimes = ["node22", "node24"];
    private static readonly string[] NodeOperations =
        ["node/project-build-v1", "node/project-test-v1"];

    internal string Root { get; } = Directory.CreateTempSubdirectory("caller-").FullName;
    internal string At(string name) => Path.GetFullPath(name, Root);
    internal string Make(string name) => Directory.CreateDirectory(At(name)).FullName;
    internal T Read<T>(string name, JsonTypeInfo<T> type) =>
        CiCallerTransfer.Read(At(name), type);

    internal string Put(string name, string text)
    {
        string path = At(name);
        File.WriteAllText(path, text);
        return path;
    }

    internal string Put<T>(string name, T value, JsonTypeInfo<T> type)
    {
        string path = At(name);
        CiCallerTransfer.Write(path, value, type);
        return path;
    }

    internal string Transfer()
    {
        Make("transfer/control");
        foreach (string file in Distribution) Put("transfer/control/" + file, "fixture");
        Make("transfer/ruby");
        Put("transfer/ruby/facts.rb", "fixture");
        using var zip = ZipFile.Open(At("transfer/python-control.zip"), ZipArchiveMode.Create);
        zip.CreateEntry("src/private/app/workflow-delivery/Native/Python/passive.py");
        return At("transfer");
    }

    internal PythonGroupEndpoint PreparePython(CiPlan plan)
    {
        var endpoint = Endpoint();
        var graph = new PythonGraphRequest(endpoint.Directory, At("uv-shim"), At("python-shim"),
            At("helper-python"), "passive.py");
        var operation = new PythonPytestOperation("ubuntu-latest", "python-native",
            new() { ["python"] = "3.14" });
        var environment = new Dictionary<string, string?>
        {
            ["PATH"] = "native path",
            ["UV_CACHE_DIR"] = "prepared cache",
            ["PYTHONSAFEPATH"] = "1"
        };
        var prepared = new PythonGroupEndpoint(endpoint, new(graph, "pytest.py", "packages.py",
            operation), environment);
        Put("context.json", new PythonCiCallerContext(endpoint, endpoint, true, At("scope.json"),
            At("WorkflowDelivery.dll"), At("control-source")),
            TransferJson.Default.PythonCiCallerContext);
        Put("readback.json", new PythonCiCallerReadback([new("native-root", ".", "root")],
            graph.Interpreter, new("pyproject.toml", ["tests/root"], []),
            new(Candidate, "pyproject.toml", [new("tests/root", null, [])]), operation),
            TransferJson.Default.PythonCiCallerReadback);
        Put("plan.json", plan, TransferJson.Default.CiPlan);
        ReplacePythonPreparation(prepared, prepared);
        return prepared;
    }

    internal void ReplacePythonPreparation(PythonGroupEndpoint basis,
        PythonGroupEndpoint candidate) =>
        Put("preparation.json",
            new PythonGroupPreparationResult(new(basis, candidate, true, At("scope.json")), []),
            TransferJson.Default.PythonGroupPreparationResult);

    internal (RubyGroupEndpoint Prepared, RubyQueryTools Tools) PrepareRuby(CiPlan plan)
    {
        var endpoint = Endpoint();
        string scope = At("scope.json");
        string helper = At("facts.rb");
        var tools = new RubyQueryTools(At("ruby-shim"), At("gem-shim"), At("bundle-shim"),
            At("dotnet-shim"));
        var facts = new RubyFactTools(tools.Ruby, helper, Root,
            new Dictionary<string, string?> { ["GEM_HOME"] = At("prepared-gems") }, 900);
        var prepared = new RubyGroupEndpoint(endpoint, new(facts, At("nbgv"), "probe"));
        var inputs = new RubyRevisionInputs(new(new(Candidate, []), new(new([], []), []), [],
            []), [], [], []);
        Put("context.json", new RubyCiCallerContext(endpoint, endpoint, true, scope,
            At("WorkflowDelivery.dll"), helper), TransferJson.Default.RubyCiCallerContext);
        Put("readback.json", new RubyCiCallerReadback(inputs, inputs),
            TransferJson.Default.RubyCiCallerReadback);
        Put("preparation-request.json", new RubyGroupPreparationRequest(endpoint, endpoint,
            true, scope, helper, Root, tools, "probe", 900),
            TransferJson.Default.RubyGroupPreparationRequest);
        Put("plan.json", plan, TransferJson.Default.CiPlan);
        ReplaceRubyPreparation(prepared, prepared);
        return (prepared, tools);
    }

    internal void ReplaceRubyPreparation(RubyGroupEndpoint basis, RubyGroupEndpoint candidate) =>
        Put("preparation.json",
            new RubyGroupPreparationResult(new(basis, candidate, true, At("scope.json")), []),
            TransferJson.Default.RubyGroupPreparationResult);

    private MaterializedEndpoint Endpoint() => new(Make("candidate"), Candidate);

    internal NodeGroupReadback PrepareNodeJoin()
    {
        var group = NodeGroup();
        var endpoint = new MaterializedEndpoint(Root, Candidate);
        Put("context.json", new NodeCiCallerContext(endpoint, endpoint, true, At("scope.json"),
            At("WorkflowDelivery.dll")), TransferJson.Default.NodeCiCallerContext);
        Put("group-plan.json", group, TransferJson.Default.NodeGroupReadback);
        Put("parent-plan.json", group.Adopted, TransferJson.Default.CiPlan);
        return group;
    }

    internal static NodeGroupReadback NodeGroup() => new(Plan(NodeFactsAssembler.Scope,
        [.. Runtimes.SelectMany(runtime => NodeOperations.Select(operation => Check("src/node",
            operation, runtime, new() { ["node"] = runtime[4..] + ".x", ["pnpm"] = "10.0.0" },
            "node/hcoona-release-smoke-npm-v1")))]) with
    { Full = true }, [], "src/node",
        new(Candidate, [new("node22", "22.x", "22.1.0"), new("node24", "24.x", null)],
            "10.0.0", ["mise.toml", "eng/node-release-schedule.json"], "node22",
            new string('d', 40), "24.1.0", 22, []));

    internal static NodeRunResult NodeResult(NodeGroupReadback group, string runtime,
        CheckStatus status) => new(Candidate, [.. group.Adopted.Checks
            .Where(c => c.Work.Key.Variant == runtime)
            .Select(c => new CheckResult(Candidate, c.Work.Key, status))],
            [], [], [], runtime, null, group.Adopted);

    internal static PlannedCheck Check(string target, string operation, string variant,
        Dictionary<string, string>? arguments = null, string? preset = null,
        string runner = "ubuntu-latest") =>
        new(new(new(target, operation, variant), arguments ?? [], runner, true, []),
            preset is null ? [] : [preset], [],
            preset is null ? [CheckOrigin.NativeRetained] : [CheckOrigin.Preset]);

    internal static CiPlan Plan(string scope, params PlannedCheck[] checks) =>
        new(Candidate, Candidate, scope, checks);

    internal void WriteDotNetPlan(CiPlan plan) =>
        Put("transfer/dotnet-plan.json", plan, TransferJson.Default.CiPlan);

    internal void WriteDotNetGroup(string artifact, string group, CiPlan original,
        params CheckResult[] results)
    {
        string path = Make("groups");
        Put(Path.Combine(path, DotNetCiCaller.GroupReceiptFile(artifact)),
            new CiGroupReceipt(group, original.Candidate, results, original),
            TransferJson.Default.CiGroupReceipt);
    }

    internal static Call Run(int expected, params string[] args)
    {
        using var output = new StringWriter();
        using var error = new StringWriter();
        int exit = Program.Run(["ci", .. args], output, error);
        Assert.AreEqual(expected, exit, error.ToString());
        if (exit == 2) Assert.IsEmpty(output.ToString());
        return new(output.ToString(), error.ToString());
    }

    internal sealed record Call(string Output, string Error)
    {
        internal T Read<T>(JsonTypeInfo<T> type) =>
            JsonSerializer.Deserialize(Output, type) ?? throw new InvalidDataException(Output);
    }

    [ModuleInitializer]
    internal static void DispatchActionsOutput()
    {
        if (Environment.GetEnvironmentVariable(TestProcess.GuardVariable) != "1" ||
            Environment.GetCommandLineArgs() is not [_, ActionsOutputSwitch, var json])
            return;

        CiCallerTransfer.OutputArray("retained_directories",
            JsonSerializer.Deserialize(json, TransferJson.Default.StringArray)!);
        Assert.ThrowsExactly<InvalidDataException>(() => CiCallerTransfer.Output("bad", "a\nb"));
        Environment.Exit(0);
    }

    public void Dispose() => ScratchRoots.TryDelete(Root);
}

// ProgramTests shares these assertions across CLI diagnostic boundaries.
internal static class CiCallerTransferTests
{
    internal const string WorkflowCommandText = "native failure\n::add-mask::x\r\n" +
        "  ##[add-mask]x\nwarning ##[error]inline";

    internal static void AssertNoWorkflowCommand(string log)
    {
        foreach (string line in log.ReplaceLineEndings("\n").Split('\n'))
        {
            Assert.IsFalse(line.TrimStart().StartsWith("::", StringComparison.Ordinal), line);
            Assert.DoesNotContain("##[", line);
        }
    }
}
