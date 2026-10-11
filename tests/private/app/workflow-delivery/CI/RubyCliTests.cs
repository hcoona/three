using System.Text.Json;
using WorkflowDelivery.CI;
using WorkflowDelivery.Repository;
using WorkflowDelivery.Tests.Repository;

namespace WorkflowDelivery.Tests.CI;

[TestClass]
public sealed class RubyCliTests
{
    private static readonly string Candidate = new('a', 40);
    private static CiPlan Plan() => new(Candidate, Candidate, RubyFactsAssembler.Scope,
        [new(new(new("src/native", RubyProjectChecks.RSpec, "default"), [], "ubuntu-latest",
            true, []), [RubyProjectChecks.Preset], [], [CheckOrigin.Preset])]);
    private static RubyFactTools Tools(string root) => new(Path.Combine(root, "ruby"),
        Path.Combine(root, "facts.rb"), root,
        new Dictionary<string, string?> { ["GEM_HOME"] = Path.Combine(root, "gems") }, 30);
    private static RubyRevisionInputs Inputs() => new(new(new(Candidate, []),
        new(new([], []), []), [], []), [], [], []);

    [TestMethod]
    public void PreparationAndPlanningPreserveOriginalNativeContext()
    {
        using var files = new DriverFiles();
        RubyFactTools tools = Tools(files.Root);
        var endpoint = new MaterializedEndpoint(files.Root, Candidate);
        var prepared = new RubyGroupRequest(new(endpoint, new(tools,
            Path.Combine(files.Root, "nbgv"), "2.7.2")), new(endpoint, new(tools,
            Path.Combine(files.Root, "nbgv"), "2.7.2")), true,
            Path.Combine(files.Root, "scope.json"));
        var request = new RubyGroupPreparationRequest(endpoint, endpoint, true,
            prepared.ScopePath, tools.Helper, files.Root,
            new(tools.Ruby, Path.Combine(files.Root, "gem"), Path.Combine(files.Root, "bundle"),
                Path.Combine(files.Root, "dotnet")), "2.7.2", 30);
        string path = Path.Combine(files.Root, "request.json");
        File.WriteAllText(path, JsonSerializer.Serialize(request,
            TransferJson.Default.RubyGroupPreparationRequest));
        using var output = new StringWriter();
        using var error = new StringWriter();
        int exit = Program.Run(["ci", "prepare-ruby-group", path], output, error,
            prepareRubyGroup: (received, _) =>
            {
                Assert.AreEqual(Candidate, received.Candidate.Reference);
                Assert.AreEqual(request.Tools.Dotnet, received.Tools.Dotnet);
                Assert.AreEqual(request.ScopePath, received.ScopePath);
                return Task.FromResult(new RubyGroupPreparationResult(prepared, []));
            });
        Assert.AreEqual(0, exit, error.ToString());
        RubyGroupPreparationResult result = JsonSerializer.Deserialize(output.ToString(),
            TransferJson.Default.RubyGroupPreparationResult)!;
        Assert.AreEqual(tools.Environment["GEM_HOME"],
            result.Request.Candidate.Collection.Tools.Environment["GEM_HOME"]);
        File.WriteAllText(path, JsonSerializer.Serialize(result.Request,
            TransferJson.Default.RubyGroupRequest));
        output.GetStringBuilder().Clear();
        exit = Program.Run(["ci", "plan-ruby-group", path], output, error,
            planRubyGroup: (received, _) =>
            {
                Assert.AreEqual(prepared.Candidate.Collection.Nbgv,
                    received.Candidate.Collection.Nbgv);
                return Task.FromResult(new RubyGroupReadback(Plan(), Inputs()));
            });
        Assert.AreEqual(0, exit, error.ToString());
        RubyGroupReadback readback = JsonSerializer.Deserialize(output.ToString(),
            TransferJson.Default.RubyGroupReadback)!;
        Assert.AreEqual(Candidate, readback.Plan.Candidate);
        Assert.AreEqual(Candidate, readback.Candidate.Facts.Revision.Commit);
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public void RunUsesOriginalResultsRatherThanSuppliedOutcome(bool missing)
    {
        using var files = new DriverFiles();
        CiPlan plan = Plan();
        var request = new RubyGroupRunRequest(files.Root, files.Root, Tools(files.Root),
            Path.Combine(files.Root, "gem"), Path.Combine(files.Root, "bundle"),
            Path.Combine(files.Root, "nbgv"), Inputs());
        string planPath = Path.Combine(files.Root, "plan.json"), requestPath =
            Path.Combine(files.Root, "request.json");
        File.WriteAllText(planPath, JsonSerializer.Serialize(plan, TransferJson.Default.CiPlan));
        File.WriteAllText(requestPath, JsonSerializer.Serialize(request,
            TransferJson.Default.RubyGroupRunRequest));
        using var output = new StringWriter();
        using var error = new StringWriter();
        int exit = Program.Run(["ci", "run-ruby-group", planPath, requestPath], output, error,
            runRubyGroup: (received, native, _) =>
            {
                Assert.AreEqual(plan.Checks[0].Work.Key, received.Checks[0].Work.Key);
                Assert.AreEqual(request.Facts.Environment["GEM_HOME"],
                    native.Facts.Environment["GEM_HOME"]);
                CheckResult[] results = missing ? [] :
                    [new(Candidate, received.Checks[0].Work.Key, CheckStatus.Passed)];
                var claimed = ResultCollector.Collect(plan with { Checks = [] }, []);
                return Task.FromResult(new RubyGroupRun(results, [], new([], [], [], []), claimed));
            });
        Assert.AreEqual(missing ? 1 : 0, exit, error.ToString());
        RubyGroupRun result = JsonSerializer.Deserialize(output.ToString(),
            TransferJson.Default.RubyGroupRun)!;
        Assert.AreEqual(!missing, ResultCollector.Collect(plan, result.Results).Satisfied);
    }

    [TestMethod]
    [DataRow("null")]
    [DataRow("{}")]
    [DataRow("{\"candidate\":null}")]
    public void InvalidRubyRequestCannotDispatch(string json)
    {
        using var files = new DriverFiles();
        string path = Path.Combine(files.Root, "request.json");
        File.WriteAllText(path, json);
        using var output = new StringWriter();
        using var error = new StringWriter();
        int exit = Program.Run(["ci", "prepare-ruby-group", path], output, error,
            prepareRubyGroup: (_, _) =>
            {
                Assert.Fail("An incomplete request cannot dispatch preparation.");
                return Task.FromResult<RubyGroupPreparationResult>(null!);
            });
        Assert.AreEqual(2, exit);
        Assert.AreEqual("", output.ToString());
        Assert.IsFalse(string.IsNullOrEmpty(error.ToString()));
    }
    private sealed class DriverFiles : IDisposable
    {
        internal string Root { get; } = Directory.CreateDirectory(Path.Combine(
            Path.GetTempPath(), "ruby-cli-" + Guid.NewGuid())).FullName;
        public void Dispose() => Directory.Delete(Root, recursive: true);
    }

}
