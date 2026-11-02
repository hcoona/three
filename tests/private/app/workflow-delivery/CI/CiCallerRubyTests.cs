using System.Text.Json;
using WorkflowDelivery.CI;
using WorkflowDelivery.CI.Callers;
using WorkflowDelivery.Platform;
using WorkflowDelivery.Repository;

namespace WorkflowDelivery.Tests.CI;

[TestClass]
public sealed class CiCallerRubyTests(TestContext context)
{
    private static readonly string Candidate = new('a', 40);

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task ExecuteConsumesPreparedCandidateAndRetainsRequiredOutcome(bool missing)
    {
        using var files = new CiCallerFiles();
        (string checkout, MaterializedEndpoint endpoint, RubyQueryTools tools,
            RubyFactTools facts, RubyGroupEndpoint preparedEndpoint,
            RubyGroupPreparationResult prepared, CiPlan plan) = Arrange(files);
        CiOutcome outcome = await RubyCiCaller.ExecuteAsync(files.Root, context.CancellationToken,
            (received, request, _) =>
            {
                Assert.AreEqual(plan.Checks[0].Work.Key, received.Checks[0].Work.Key);
                Assert.AreEqual(checkout, request.Checkout);
                Assert.AreEqual(tools.Bundle, request.Bundle);
                Assert.AreEqual(facts.Environment["GEM_HOME"],
                    request.Facts.Environment["GEM_HOME"]);
                Assert.AreEqual(Candidate, request.Candidate.Facts.Revision.Commit);
                CheckResult[] results = missing ? [] : [new(Candidate,
                    received.Checks[0].Work.Key, CheckStatus.Passed)];
                return Task.FromResult(new RubyGroupRun(results, [], new([], [], [], []),
                    ResultCollector.Collect(received, results)));
            });
        Assert.AreEqual(!missing, outcome.Satisfied);
        Assert.AreEqual(missing ? "missing" : "passed", outcome.Checks.Single().Status);
        Assert.IsTrue(File.Exists(Path.Combine(files.Root, "result.json")));
        Assert.IsTrue(File.Exists(Path.Combine(files.Root, "outcome.json")));
        CiCallerTransfer.Write(Path.Combine(files.Root, "preparation.json"), prepared with
        {
            Request = prepared.Request with
            {
                Candidate = preparedEndpoint with
                { Checkout = endpoint with { Reference = new('b', 40) } }
            }
        }, TransferJson.Default.RubyGroupPreparationResult);
        await Assert.ThrowsExactlyAsync<InvalidDataException>(() => RubyCiCaller.ExecuteAsync(
            files.Root, context.CancellationToken));
    }

    [TestMethod]
    public async Task FailedRSpecWithoutReportErrorReportsBoundedCommandDiagnostic()
    {
        using var files = new CiCallerFiles();
        (string checkout, _, RubyQueryTools tools, _, _, _, CiPlan plan) = Arrange(files);
        string tail = new string('x', NativeDiagnostic.TailLength - 23) + " controlled rspec error";
        using var diagnostics = new StringWriter();

        CiOutcome outcome = await RubyCiCaller.ExecuteAsync(files.Root, context.CancellationToken,
            (received, request, _) =>
            {
                CheckKey key = received.Checks[0].Work.Key;
                var command = new NativeCommand(tools.Ruby, checkout,
                    [tools.Bundle, "_2.7.2_", "exec", "rspec"], 900);
                var result = new NativeCommandResult(NativeTermination.Exited, 1, "",
                    "omitted rspec head " + tail, 2, null);
                CheckResult[] results = [new(Candidate, key, CheckStatus.Failed)];
                return Task.FromResult(new RubyGroupRun(results,
                    [new(key, new(command, result, null, CheckStatus.Failed, null), [])],
                    new([], [], [], []), ResultCollector.Collect(received, results)));
            }, diagnostics);

        Assert.IsFalse(outcome.Satisfied);
        Assert.AreEqual("failed", outcome.Checks.Single().Status);
        // The outcome contract is unchanged; failure text goes only to diagnostics.
        Assert.AreEqual(JsonSerializer.Serialize(outcome, TransferJson.Default.CiOutcome),
            JsonSerializer.Serialize(CiCallerTransfer.Read(Path.Combine(files.Root,
                "outcome.json"), TransferJson.Default.CiOutcome),
                TransferJson.Default.CiOutcome));
        Assert.AreEqual(
            $"Check failed (required): src/ruby {RubyProjectChecks.RSpec} default" +
            Environment.NewLine + "  | Native command ruby-shim _2.7.2_ failed: Exited, exit 1. " +
            "stderr: [truncated] " + tail + Environment.NewLine, diagnostics.ToString());
    }

    [TestMethod]
    public async Task RSpecLoadFailureReportsBoundedStdoutAndPreservesOriginalResults()
    {
        using var files = new CiCallerFiles();
        (string checkout, _, RubyQueryTools tools, _, _, _, _) = Arrange(files);
        const string reportJson = """
            {
              "version": "3.13.6",
              "messages": ["No examples found."],
              "examples": [],
              "summary": {
                "example_count": 0, "failure_count": 0, "pending_count": 0,
                "errors_outside_of_examples_count": 1
              }
            }
            """;
        string stdout = "omitted native output " + new string('x', NativeDiagnostic.TailLength) +
            "\nLoadError: cannot load such file -- logger\n::error::not a workflow command" +
            "\n##[error]also not a command";
        using var diagnostics = new StringWriter();

        CiOutcome outcome = await RubyCiCaller.ExecuteAsync(files.Root, context.CancellationToken,
            async (received, request, token) =>
            {
                CheckKey key = received.Checks[0].Work.Key;
                RubyRSpecRun run = await RubyRSpecExecution.RunAsync(key,
                    new(checkout, tools.Ruby, tools.Bundle, "2.7.2",
                        Path.Combine(checkout, "Gemfile"), files.Root, 900,
                        new Dictionary<string, string?>()),
                    (command, _) =>
                    {
                        File.WriteAllText(command.Arguments[^1], reportJson);
                        return Task.FromResult(new NativeCommandResult(NativeTermination.Exited,
                            1, stdout, "", 2, null));
                    }, token);
                CheckResult[] results = [new(Candidate, key, run.Status)];
                return new RubyGroupRun(results, [new(key, run, [])],
                    new([], [], [], []), ResultCollector.Collect(received, results));
            }, diagnostics);

        Assert.IsFalse(outcome.Satisfied);
        Assert.AreEqual("failed", outcome.Checks.Single().Status);
        string text = diagnostics.ToString();
        Assert.Contains("Exited, exit 1.", text);
        Assert.Contains("stdout: [truncated]", text);
        Assert.Contains("LoadError: cannot load such file -- logger", text);
        Assert.DoesNotContain("omitted native output", text);
        Assert.Contains("  | ::error::not a workflow command", text);
        Assert.Contains(@"  | #\#[error]also not a command", text);
        Assert.DoesNotContain("##[", text);
        Assert.IsLessThan(NativeDiagnostic.TailLength + 512, text.Length);

        RubyGroupRun retained = CiCallerTransfer.Read(Path.Combine(files.Root, "result.json"),
            TransferJson.Default.RubyGroupRun);
        RubyRSpecRun original = retained.RSpec.Single().Run;
        Assert.AreEqual(stdout, original.Result.Stdout);
        Assert.AreEqual("", original.Result.Stderr);
        Assert.IsNull(original.ReportError);
        Assert.IsNotNull(original.Report);
        Assert.AreEqual(1, original.Report.ErrorsOutsideExamples);
        Assert.AreEqual(JsonSerializer.Serialize(RubyRSpecResult.Read(reportJson).Original),
            JsonSerializer.Serialize(original.Report.Original));
    }

    private static (string Checkout, MaterializedEndpoint Endpoint, RubyQueryTools Tools,
        RubyFactTools Facts, RubyGroupEndpoint PreparedEndpoint,
        RubyGroupPreparationResult Prepared, CiPlan Plan) Arrange(CiCallerFiles files)
    {
        string checkout = Path.Combine(files.Root, "candidate");
        Directory.CreateDirectory(checkout);
        var endpoint = new MaterializedEndpoint(checkout, Candidate);
        var callerContext = new RubyCiCallerContext(endpoint, endpoint, true,
            Path.Combine(files.Root, "scope.json"), Path.Combine(files.Root,
                "WorkflowDelivery.dll"),
            Path.Combine(files.Root, "facts.rb"));
        var tools = new RubyQueryTools(Path.Combine(files.Root, "ruby-shim"),
            Path.Combine(files.Root, "gem-shim"), Path.Combine(files.Root, "bundle-shim"),
                Path.Combine(files.Root, "dotnet-shim"));
        var facts = new RubyFactTools(tools.Ruby, callerContext.Helper, files.Root,
            new Dictionary<string, string?>
            {
                ["GEM_HOME"] = Path.Combine(files.Root,
                "prepared-gems")
            }, 900);
        var preparedEndpoint = new RubyGroupEndpoint(endpoint, new(facts,
            Path.Combine(files.Root, "nbgv"), "probe"));
        var prepared = new RubyGroupPreparationResult(new(preparedEndpoint, preparedEndpoint,
            true, callerContext.ScopePath), []);
        var original = new RubyGroupPreparationRequest(endpoint, endpoint, true,
            callerContext.ScopePath,
            callerContext.Helper, files.Root, tools, "probe", 900);
        var inputs = new RubyRevisionInputs(new(new(Candidate, []), new(new([], []), []), [],
            []), [], [], []);
        var plan = new CiPlan(Candidate, Candidate, RubyCiCaller.Scope,
            [new(new(new("src/ruby", RubyProjectChecks.RSpec, "default"), [], "ubuntu-latest",
                true, []),
                [RubyProjectChecks.Preset], [], [CheckOrigin.Preset])]);
        CiCallerTransfer.Write(Path.Combine(files.Root, "context.json"), callerContext,
            TransferJson.Default.RubyCiCallerContext);
        CiCallerTransfer.Write(Path.Combine(files.Root, "plan.json"), plan,
            TransferJson.Default.CiPlan);
        CiCallerTransfer.Write(Path.Combine(files.Root, "preparation.json"), prepared,
            TransferJson.Default.RubyGroupPreparationResult);
        CiCallerTransfer.Write(Path.Combine(files.Root, "preparation-request.json"), original,
            TransferJson.Default.RubyGroupPreparationRequest);
        CiCallerTransfer.Write(Path.Combine(files.Root, "readback.json"), new
            RubyCiCallerReadback(inputs, inputs), TransferJson.Default.RubyCiCallerReadback);
        return (checkout, endpoint, tools, facts, preparedEndpoint, prepared, plan);
    }
}
