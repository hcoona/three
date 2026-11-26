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
    private static readonly string[] RequirePaths = ["lib"];
    private static readonly string[] LatexmathFiles = ["LICENSE", "README.md",
        "lib/asciidoctor-latexmath.rb", "lib/asciidoctor/latexmath/version.rb"];
    private static NativeCommandResult Answer(object value) => new(NativeTermination.Exited,
        0, JsonSerializer.Serialize(value), "", 0, null);

    private static GitFixture? shared;
    private static GitRevision? sharedRevision;

    [ClassInitialize]
    public static async Task InitializeAsync(TestContext classContext)
    {
        GitFixture repo = await GitFixture.CreateAsync(classContext.CancellationToken);
        try
        {
            string head = await CommitAsync(repo);
            sharedRevision = await new GitReader(repo.Directory).ReadAsync(head,
                classContext.CancellationToken);
            shared = repo;
        }
        catch { repo.Dispose(); throw; }
    }

    [ClassCleanup]
    public static void Cleanup() => shared?.Dispose();

    private static async Task<string> CommitAsync(GitFixture repo)
    {
        await repo.SetAsync(Suite + "/asciidoctor-latexmath.gemspec");
        await repo.SetAsync(Suite + "/Gemfile");
        await repo.SetAsync(Suite + "/Gemfile.lock");
        foreach (string file in LatexmathFiles) await repo.SetAsync(Suite + "/" + file);
        string head = await repo.CommitAsync();
        await repo.GitAsync("reset", "--hard", head);
        return head;
    }

    private static (CiPlan Plan, RubyGroupRunRequest Request) Shared(string scratch) =>
        CreateRequest(shared!, sharedRevision!, scratch);

    private static (CiPlan Plan, RubyGroupRunRequest Request) CreateRequest(GitFixture repo,
        GitRevision revision, string scratch)
    {
        string head = revision.Commit;
        string PathFor(string path) => Path.GetFullPath(Path.Combine(repo.Directory, path));
        var suite = new RubySourceGemFacts(PathFor(Suite + "/asciidoctor-latexmath.gemspec"),
            new("asciidoctor-latexmath", "1.2.3", "ruby", RequirePaths, [], []));
        var unit = new ReleaseUnitDeclaration(Suite + "/workflow-delivery.release-unit.yml",
            "asciidoctor-latexmath", [new("ruby-gem", RubyPackageChecks.Build,
                "asciidoctor-latexmath.gemspec", [new("gem", "primary-package", "ruby-gem")])]);
        CheckSpec[] checks = [RubyProjectChecks.Suite(Suite), .. RubyPackageChecks.Expand(unit,
            repo.Directory, new Dictionary<string, RubySourceGemFacts> { [Suite] = suite })];
        var plan = new CiPlan(head, head, RubyFactsAssembler.Scope,
            [.. checks.Select(check => new PlannedCheck(check, [RubyPackageChecks.RSpecPreset],
                [], [CheckOrigin.Preset]))]);
        var facts = new RubyFactsEndpoint(revision,
            new(new([], []), []), [], []);
        var inputs = new RubyRevisionInputs(facts, [suite],
            [new(PathFor(Suite + "/Gemfile"), PathFor(Suite + "/Gemfile.lock"), "2.4.20",
                [PathFor(Suite + "/Gemfile")], [], [])], [unit]);
        return (plan, new(repo.Directory, scratch,
            new(PathFor("ruby"), PathFor("facts.rb"), scratch,
                new Dictionary<string, string?> { ["PATH"] = "prepared tools" }, 30),
            PathFor("gem"), PathFor("bundle"), PathFor("nbgv"), inputs));
    }

    private static async Task<NativeCommandResult> NativeAsync(RubyGroupRunRequest request,
        NativeCommand command, CancellationToken token)
    {
        Assert.IsFalse(command.InheritEnvironment);
        if (command.Executable == request.Nbgv) return Answer(new { SemVer2 = "1.2.3" });
        if (command.Arguments is [_, _, "install"])
        {
            // Selected execution, not planning, installs the suite's frozen locked bundle.
            Assert.AreEqual(request.Bundle, command.Arguments[0]);
            Assert.AreEqual("_2.4.20_", command.Arguments[1]);
            Assert.AreEqual(request.Candidate.Bundles[0].Gemfile,
                command.Environment!["BUNDLE_GEMFILE"]);
            Assert.AreEqual("1", command.Environment["BUNDLE_FROZEN"]);
            return Answer(new { });
        }
        if (command.Arguments.Contains("rspec", StringComparer.Ordinal))
        {
            await File.WriteAllTextAsync(command.Arguments[^1], ReadOriginalRSpecReport(), token);
            return Answer(new { });
        }
        throw new AssertFailedException($"Unexpected command: {command.Executable}");
    }

    private static string ReadOriginalRSpecReport()
    {
        var assembly = typeof(RubyGroupExecutionTests).Assembly;
        string name = assembly.GetManifestResourceNames().Single(resource =>
            resource.EndsWith("ruby-rspec-passed.json", StringComparison.Ordinal));
        using Stream stream = assembly.GetManifestResourceStream(name)!;
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }

    private static async Task<NativeCommandResult> LatexmathAsync(RubyGroupRunRequest request,
        NativeCommand command, CancellationToken token)
    {
        Assert.IsFalse(command.InheritEnvironment);
        string gemfile = Path.GetFullPath(Path.Combine(request.Checkout, Suite, "Gemfile"));
        if (command.Arguments is ["-r", "bundler", ..])
            return Answer(new { }) with { Stdout = "2.4.20\n" };
        if (command.Arguments is [var bundle, "_2.4.20_", "install"] &&
            bundle != request.Bundle)
        {
            // The consumer resolves runtime dependencies from Latexmath's own frozen lock.
            Assert.StartsWith(request.Scratch, bundle);
            Assert.AreEqual(gemfile, command.Environment!["BUNDLE_GEMFILE"]);
            Assert.AreEqual("development", command.Environment["BUNDLE_WITHOUT"]);
            Assert.IsNull(command.Environment["BUNDLE_PATH"]);
            return Answer(new { });
        }
        if (command.Arguments[0] == request.Gem)
        {
            if (command.Arguments[1] == "build")
                await File.WriteAllTextAsync(command.Arguments[^1], "original gem", token);
            else Assert.StartsWith(request.Scratch, command.Environment!["GEM_HOME"]!);
            return Answer(new { });
        }
        if (command.Arguments[0] == "-e")
        {
            Assert.StartsWith("require 'asciidoctor-latexmath'", command.Arguments[1]);
            return Answer(new { });
        }
        if (command.Executable == request.Nbgv || command.Arguments is [_, _, "install"] ||
            command.Arguments.Contains("rspec", StringComparer.Ordinal))
            return await NativeAsync(request, command, token);
        using JsonDocument input = JsonDocument.Parse(
            await File.ReadAllTextAsync(command.Arguments[^1], token));
        string path = input.RootElement.GetProperty("path").GetString()!;
        string operation = input.RootElement.GetProperty("operation").GetString()!;
        if (operation == "version") return Answer(new
        {
            gemspec = path,
            raw = "1.2.3",
            version = "1.2.3",
            literal = "\"1.2.3\""
        });
        var gem = new
        {
            name = "asciidoctor-latexmath",
            version = "1.2.3",
            platform = "ruby",
            require_paths = RequirePaths,
            files = LatexmathFiles,
            runtime_dependencies = new[] { new { name = "asciidoctor",
                requirement = ">= 2.0, < 3.0" } }
        };
        return operation == "gemspec" ? Answer(new
        {
            gemspec = path,
            gem.name,
            gem.version,
            gem.platform,
            gem.require_paths,
            gem.files,
            gem.runtime_dependencies
        }) :
            Answer(new
            {
                archive = path,
                gem.name,
                gem.version,
                gem.platform,
                gem.require_paths,
                gem.files,
                gem.runtime_dependencies,
                extensions = Array.Empty<string>(),
                metadata = new { github_repo = "https://github.com/hcoona/three" },
                contents = LatexmathFiles
            });
    }

    [TestMethod]
    public async Task LatexmathQualifiesWholeSuiteAndOriginalGemInCleanConsumer()
    {
        using var scratch = new Scratch();
        var (plan, request) = Shared(scratch.Directory);
        // Group preparation shares a bundle path; the clean consumer must not inherit it.
        request = request with
        {
            Facts = request.Facts with
            {
                Environment =
            new Dictionary<string, string?>(request.Facts.Environment)
            { ["BUNDLE_PATH"] = Path.Combine(request.Scratch, "shared-bundle") }
            }
        };
        RubyGroupRun result = await RubyGroupExecution.RunAsync(plan, request,
            (command, token) => LatexmathAsync(request, command, token),
            context.CancellationToken);

        Assert.IsTrue(result.Outcome.Satisfied, JsonSerializer.Serialize(result));
        CollectionAssert.AreEquivalent(plan.Checks.Select(item => new CheckResult(plan.Candidate,
            item.Work.Key, CheckStatus.Passed)).ToArray(), result.Results);
        Assert.AreEqual(CheckStatus.Passed, Assert.ContainsSingle(result.RSpec).Run.Status);
        RubyOriginalGem gem = Assert.ContainsSingle(result.Packages.Gems);
        Assert.AreEqual("original gem", await File.ReadAllTextAsync(gem.Path,
            context.CancellationToken));
        NativeCommand require = Assert.ContainsSingle(result.Packages.Commands.Where(
            item => item.Command.Arguments[0] == "-e")).Command;
        Assert.StartsWith(request.Scratch, require.Directory);
    }

    [TestMethod]
    public async Task SelectedSuiteInstallsItsGovernedBundlesBeforeRSpecAndRetainsThem()
    {
        using var scratch = new Scratch();
        var (plan, request) = Shared(scratch.Directory);
        string Gemfile(string directory) => Path.GetFullPath(Path.Combine(request.Checkout,
            directory, "Gemfile"));
        RubyBundleFacts suite = request.Candidate.Bundles[0];
        // A nested lock context belongs to the suite; a nested native gem's bundle does not.
        RubyBundleFacts nested = suite with { Gemfile = Gemfile(Suite + "/contexts/b") };
        RubyBundleFacts native = suite with { Gemfile = Gemfile(Suite + "/vendor/gem") };
        request = request with
        {
            Candidate = request.Candidate with
            {
                Bundles = [native, nested, suite],
                Facts = request.Candidate.Facts with
                {
                    Native = request.Candidate.Facts.Native with
                    {
                        Graph = new([new(Suite + "/vendor/gem", "vendored", [])], [])
                    }
                }
            }
        };
        var installed = new List<string>();
        RubyGroupRun result = await RubyGroupExecution.RunAsync(plan with
        { Checks = plan.Checks[..1] }, request, async (command, token) =>
        {
            if (command.Arguments is [_, _, "install"])
            {
                Assert.AreEqual("1", command.Environment!["BUNDLE_FROZEN"]);
                Assert.Contains("_2.4.20_", command.Arguments);
                installed.Add(command.Environment["BUNDLE_GEMFILE"]!);
                return Answer(new { });
            }
            string[] expected = [suite.Gemfile, nested.Gemfile];
            CollectionAssert.AreEquivalent(expected, installed);
            Assert.AreEqual(Path.GetFullPath(Path.Combine(request.Checkout, Suite)),
                command.Directory);
            Assert.AreEqual(suite.Gemfile, command.Environment!["BUNDLE_GEMFILE"]);
            Assert.Contains("_2.4.20_", command.Arguments);
            Assert.AreEqual("1", command.Environment["BUNDLE_FROZEN"]);
            Assert.IsFalse(command.InheritEnvironment);
            return await NativeAsync(request, command, token);
        }, context.CancellationToken);
        Assert.IsTrue(result.Outcome.Satisfied);
        RubyGroupRSpec observation = Assert.ContainsSingle(result.RSpec);
        CollectionAssert.AreEquivalent(installed, observation.Installs.Select(install =>
            install.Command.Environment!["BUNDLE_GEMFILE"]).ToArray());
        Assert.IsNotNull(observation.Run.Report);
        Assert.AreEqual(ReadOriginalRSpecReport().Trim(),
            observation.Run.Report.Original.GetRawText());
        Assert.IsFalse(File.Exists(observation.Run.Command.Arguments[^1]));
    }

    [TestMethod]
    public async Task CancellationAfterRSpecSuccessDeletesItsReport()
    {
        using var scratch = new Scratch();
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(
            context.CancellationToken);
        var (plan, request) = Shared(scratch.Directory);
        RubyGroupRun result = await RubyGroupExecution.RunAsync(plan with
        { Checks = plan.Checks[..1] }, request, async (command, token) =>
        {
            NativeCommandResult native = await NativeAsync(request, command, token);
            if (command.Arguments.Contains("rspec")) cancellation.Cancel();
            return native;
        }, cancellation.Token);

        RubyRSpecRun run = Assert.ContainsSingle(result.RSpec).Run;
        Assert.IsTrue(run.Result.Succeeded);
        Assert.AreEqual(CheckStatus.Cancelled, run.Status);
        Assert.IsFalse(result.Outcome.Satisfied);
        Assert.IsNull(run.Report);
        Assert.IsFalse(File.Exists(run.Command.Arguments[^1]));
    }

    [TestMethod]
    public async Task EmptyRSpecReportRetainsBoundedDiagnosticAndSeparateStreams()
    {
        using var scratch = new Scratch();
        var (plan, request) = Shared(scratch.Directory);
        const string stdout = "controlled RSpec stdout";
        const string stderrHead = "controlled RSpec stderr head";
        const string stderrTail = "controlled RSpec stderr tail";
        string stderr = stderrHead + new string('x', NativeDiagnostic.TailLength) + stderrTail;
        const NativeTermination termination = NativeTermination.TimedOut;
        RubyGroupRun result = await RubyGroupExecution.RunAsync(plan with
        { Checks = plan.Checks[..1] }, request, async (command, token) =>
        {
            if (!command.Arguments.Contains("rspec"))
                return await NativeAsync(request, command, token);
            await File.WriteAllTextAsync(command.Arguments[^1], "", token);
            return new(termination, null, stdout, stderr, 0.1, null);
        }, context.CancellationToken);

        RubyRSpecRun run = Assert.ContainsSingle(result.RSpec).Run;
        Assert.AreEqual(CheckStatus.TimedOut, run.Status);
        Assert.IsFalse(result.Outcome.Satisfied);
        Assert.IsNull(run.Report);
        Assert.AreEqual(stdout, run.Result.Stdout);
        Assert.AreEqual(stderr, run.Result.Stderr);
        Assert.StartsWith("Native RSpec result could not be read: ", run.ReportError);
        Assert.Contains("Native RSpec command failed: " + termination, run.ReportError);
        Assert.Contains("stderr: [truncated]", run.ReportError);
        Assert.EndsWith(stderrTail, run.ReportError);
        Assert.DoesNotContain(stderrHead, run.ReportError);
        Assert.DoesNotContain(stdout, run.ReportError);
        Assert.IsFalse(File.Exists(run.Command.Arguments[^1]));
    }

    [TestMethod]
    public async Task SuccessfulRSpecWithoutReportCannotQualifySuite()
    {
        using var scratch = new Scratch();
        var (plan, request) = Shared(scratch.Directory);
        RubyGroupRun result = await RubyGroupExecution.RunAsync(plan with
        { Checks = plan.Checks[..1] }, request, (command, token) =>
            command.Arguments.Contains("rspec") ? Task.FromResult(Answer(new { }))
                : NativeAsync(request, command, token), context.CancellationToken);

        RubyRSpecRun run = Assert.ContainsSingle(result.RSpec).Run;
        Assert.IsFalse(result.Outcome.Satisfied);
        Assert.AreEqual(CheckStatus.Failed, run.Status);
        Assert.IsNull(run.Report);
        Assert.IsNotNull(run.ReportError);
        Assert.Contains("required JSON result", run.ReportError);
    }

    [TestMethod]
    public async Task FailedSelectedBundleInstallationFailsItsSuiteWithoutRunningRSpec()
    {
        using var scratch = new Scratch();
        var (plan, request) = Shared(scratch.Directory);
        RubyGroupRun result = await RubyGroupExecution.RunAsync(plan, request,
            (command, token) =>
            {
                Assert.DoesNotContain("rspec", command.Arguments);
                return command.Arguments is [var bundle, _, "install"] &&
                    bundle == request.Bundle
                    ? Task.FromResult(Answer(new { }) with
                    { ExitCode = 5, Stderr = "native frozen bundle failed" })
                    : LatexmathAsync(request, command, token);
            }, context.CancellationToken);
        Assert.IsFalse(result.Outcome.Satisfied);
        RubyGroupRSpec suite = Assert.ContainsSingle(result.RSpec);
        Assert.AreEqual(CheckStatus.Failed, suite.Run.Status);
        Assert.AreEqual(5, suite.Run.Result.ExitCode);
        Assert.Contains("native frozen bundle failed", suite.Run.ReportError!);
        CheckResult[] gems = result.Results.Where(check => check.Key != suite.Check).ToArray();
        Assert.HasCount(3, gems);
        CollectionAssert.AreEquivalent(plan.Checks[1..].Select(check =>
            new CheckResult(plan.Candidate, check.Work.Key, CheckStatus.Passed)).ToArray(), gems);
    }

    [TestMethod]
    public async Task UnresolvedSuiteBundleStopsBothPartitions()
    {
        using var scratch = new Scratch();
        var (plan, request) = Shared(scratch.Directory);
        request = request with
        { Candidate = request.Candidate with { Bundles = [] } };
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
        // Defect-free control: the shared commit binds and satisfies an empty selection.
        using var scratch = new Scratch();
        var (plan, request) = Shared(scratch.Directory);
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

    private sealed class Scratch : IDisposable
    {
        internal string Directory { get; } =
            System.IO.Directory.CreateTempSubdirectory("workflow-ruby-group-").FullName;
        public void Dispose() => System.IO.Directory.Delete(Directory, recursive: true);
    }
}
