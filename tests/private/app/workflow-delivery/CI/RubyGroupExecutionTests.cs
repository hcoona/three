using System.Text.Json;
using WorkflowDelivery.CI;
using WorkflowDelivery.Platform;
using WorkflowDelivery.Repository;
using GitFixture = WorkflowDelivery.Tests.Repository.GitReaderTests.GitFixture;

namespace WorkflowDelivery.Tests.CI;

[TestClass]
public sealed class RubyGroupExecutionTests(TestContext context)
{
    private const string Suite = "src/asciidoctor-latexmath";
    private const string Gem = "src/hcoona-release-smoke-ruby";
    private static readonly string[] Files = ["LICENSE", "README.md",
        "lib/hcoona_release_smoke_ruby.rb", "lib/hcoona_release_smoke_ruby/version.rb"];
    private static readonly string[] RequirePaths = ["lib"];
    private static NativeCommandResult Answer(object value) => new(NativeTermination.Exited,
        0, JsonSerializer.Serialize(value), "", 0, null);

    private async Task<(CiPlan Plan, RubyGroupRunRequest Request)> PrepareAsync(GitFixture repo)
    {
        await repo.SetAsync(Suite + "/asciidoctor-latexmath.gemspec");
        await repo.SetAsync(Suite + "/Gemfile");
        await repo.SetAsync(Suite + "/Gemfile.lock");
        await repo.SetAsync(Gem + "/hcoona-release-smoke-ruby.gemspec");
        string head = await repo.CommitAsync();
        await repo.GitAsync("reset", "--hard", head);
        GitRevision revision = await new GitReader(repo.Directory).ReadAsync(head,
            context.CancellationToken);
        string PathFor(string path) => Path.GetFullPath(Path.Combine(repo.Directory, path));
        var source = new RubySourceGemFacts(PathFor(Gem + "/hcoona-release-smoke-ruby.gemspec"),
            new("hcoona-release-smoke-ruby", "1.2.3", "ruby", RequirePaths, Files, []));
        var suite = new RubySourceGemFacts(PathFor(Suite + "/asciidoctor-latexmath.gemspec"),
            new("asciidoctor-latexmath", "1.2.3", "ruby", RequirePaths, [], []));
        var unit = new ReleaseUnitDeclaration(Gem + "/workflow-delivery.release-unit.yml",
            "smoke", [new("gem", RubyPackageChecks.Build,
                "hcoona-release-smoke-ruby.gemspec", [new("gem", "primary-package", "ruby-gem")])]);
        CheckSpec[] gemChecks = RubyPackageChecks.Expand(unit, repo.Directory,
            new Dictionary<string, RubySourceGemFacts> { [Gem] = source });
        var checks = new List<PlannedCheck> {
            new(new(new(Suite, RubyProjectChecks.RSpec, "default"), [], "ubuntu-latest", true,
                []), [RubyProjectChecks.Preset], [], [CheckOrigin.Preset]) };
        checks.AddRange(gemChecks.Select(check => new PlannedCheck(check,
            [RubyPackageChecks.Preset], [], [CheckOrigin.Preset])));
        var plan = JsonSerializer.Deserialize<CiPlan>(JsonSerializer.Serialize(
            new CiPlan(head, head, RubyFactsAssembler.Scope, checks.ToArray())))!;
        var facts = new RubyFactsEndpoint(revision,
            new(new([], []), []), [], []);
        var inputs = new RubyRevisionInputs(facts, [source, suite],
            [new(PathFor(Suite + "/Gemfile"), PathFor(Suite + "/Gemfile.lock"), "2.4.20",
                [PathFor(Suite + "/Gemfile")], [], [])], [unit]);
        string scratch = Directory.CreateDirectory(Path.Combine(
            Path.GetDirectoryName(repo.Directory)!, "execution")).FullName;
        return (plan, new(repo.Directory, scratch,
            new(PathFor("ruby"), PathFor("facts.rb"), scratch,
                new Dictionary<string, string?> { ["PATH"] = "prepared tools" }, 30),
            PathFor("gem"), PathFor("bundle"), PathFor("nbgv"), inputs));
    }

    private static async Task<NativeCommandResult> NativeAsync(RubyGroupRunRequest request,
        NativeCommand command, bool failedSuite, CancellationToken token)
    {
        Assert.IsFalse(command.InheritEnvironment);
        if (command.Executable == request.Nbgv) return Answer(new { SemVer2 = "1.2.3" });
        if (command.Arguments.Contains("rspec", StringComparer.Ordinal))
        {
            Assert.Contains("_2.4.20_", command.Arguments);
            await File.WriteAllTextAsync(command.Arguments[^1], JsonSerializer.Serialize(new
            {
                version = "3.13.0",
                examples = new[] { new { id = "./spec/whole_spec.rb[1:1]",
                    description = "whole suite", full_description = "whole suite",
                    status = failedSuite ? "failed" : "passed", file_path = "./spec/whole_spec.rb",
                    line_number = 1, run_time = 0.01 } },
                summary = new { duration = 0.01, example_count = 1,
                    failure_count = failedSuite ? 1 : 0, pending_count = 0,
                    errors_outside_of_examples_count = 0 }
            }), token);
            return failedSuite ? Answer(new { }) with { ExitCode = 1 } : Answer(new { });
        }
        if (command.Arguments[0] == request.Gem)
        {
            if (command.Arguments[1] == "build")
                await File.WriteAllTextAsync(command.Arguments[^1], "original gem", token);
            return Answer(new { });
        }
        if (command.Arguments[0] == "-e") return Answer(new { });
        using JsonDocument input = JsonDocument.Parse(
            await File.ReadAllTextAsync(command.Arguments[^1], token));
        string path = input.RootElement.GetProperty("path").GetString()!;
        string operation = input.RootElement.GetProperty("operation").GetString()!;
        if (operation == "version") return Answer(new { gemspec = path, raw = "1.2.3",
            version = "1.2.3", literal = "\"1.2.3\"" });
        var gem = new { name = "hcoona-release-smoke-ruby", version = "1.2.3", platform = "ruby",
            require_paths = RequirePaths, files = Files,
            runtime_dependencies = Array.Empty<object>() };
        return operation == "gemspec" ? Answer(new { gemspec = path, gem.name, gem.version,
            gem.platform, gem.require_paths, gem.files, gem.runtime_dependencies }) :
            Answer(new { archive = path, gem.name, gem.version, gem.platform,
                gem.require_paths, gem.files, gem.runtime_dependencies, contents = Files });
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task OriginalMixedPlanRetainsEveryRequiredResult(bool failedSuite)
    {
        using var repo = await GitFixture.CreateAsync(context.CancellationToken);
        var (plan, request) = await PrepareAsync(repo);
        RubyGroupRun result = await RubyGroupExecution.RunAsync(plan, request,
            (command, token) => NativeAsync(request, command, failedSuite, token),
            context.CancellationToken);
        Assert.AreEqual(!failedSuite, result.Outcome.Satisfied, JsonSerializer.Serialize(result));
        Assert.HasCount(4, result.Results);
        Assert.HasCount(3, result.Packages.Results);
        Assert.IsTrue(result.Packages.Results.All(check => check.Status == CheckStatus.Passed));
        Assert.AreEqual(failedSuite ? CheckStatus.Failed : CheckStatus.Passed,
            Assert.ContainsSingle(result.RSpec).Run.Status);
        Assert.IsTrue(result.Results.All(check => check.Candidate == plan.Candidate));
    }

    [TestMethod]
    [DataRow("partial-gem")]
    [DataRow("missing-bundle")]
    [DataRow("candidate")]
    public async Task InvalidOriginalPlanCannotExecute(string defect)
    {
        using var repo = await GitFixture.CreateAsync(context.CancellationToken);
        var (plan, request) = await PrepareAsync(repo);
        if (defect == "partial-gem") plan = plan with { Checks = plan.Checks[..^1] };
        if (defect == "missing-bundle") request = request with
            { Candidate = request.Candidate with { Bundles = [] } };
        if (defect == "candidate") plan = plan with { Candidate = plan.Candidate[..8] };
        await Assert.ThrowsExactlyAsync<InvalidDataException>(() => RubyGroupExecution.RunAsync(
            plan, request, (_, _) =>
            {
                Assert.Fail("Invalid original obligations cannot execute.");
                return Task.FromResult(Answer(new { }));
            }, context.CancellationToken));
        Assert.IsEmpty(Directory.GetFileSystemEntries(request.Scratch));
    }

    [TestMethod]
    public async Task EmptySelectionPerformsNoProductPreparation()
    {
        using var repo = await GitFixture.CreateAsync(context.CancellationToken);
        var (plan, request) = await PrepareAsync(repo);
        RubyGroupRun result = await RubyGroupExecution.RunAsync(plan with { Checks = [] },
            request, (_, _) =>
            {
                Assert.Fail("Empty selection cannot prepare products.");
                return Task.FromResult(Answer(new { }));
            }, context.CancellationToken);
        Assert.IsTrue(result.Outcome.Satisfied);
        Assert.IsEmpty(result.Results);
        Assert.IsEmpty(Directory.GetFileSystemEntries(request.Scratch));
    }

    [TestMethod]
    public async Task CancellationRetainsOriginalKeys()
    {
        using var repo = await GitFixture.CreateAsync(context.CancellationToken);
        var (plan, request) = await PrepareAsync(repo);
        using var cancellation = new CancellationTokenSource();
        RubyGroupRun result = await RubyGroupExecution.RunAsync(plan, request,
            async (command, token) =>
            {
                NativeCommandResult answer = await NativeAsync(request, command, false, token);
                cancellation.Cancel();
                return answer;
            }, cancellation.Token);
        Assert.IsFalse(result.Outcome.Satisfied);
        Assert.HasCount(4, result.Results);
        Assert.IsTrue(result.Results.All(check => check.Status == CheckStatus.Cancelled));
        Assert.IsFalse(Directory.Exists(Path.Combine(request.Scratch, "packages")));
    }
}
