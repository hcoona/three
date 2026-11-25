using System.Text.Json;
using WorkflowDelivery.CI;
using WorkflowDelivery.Platform;
using WorkflowDelivery.Repository;
using WorkflowDelivery.Tests.Release;
using GitFixture = WorkflowDelivery.Tests.Repository.GitReaderTests.GitFixture;

namespace WorkflowDelivery.Tests.CI;

[TestClass]
public sealed class RubyPackageExecutionTests(TestContext context)
{
    private static readonly string[] Files =
    [
        "LICENSE",
        "README.md",
        "lib/hcoona_release_smoke_ruby.rb",
        "lib/hcoona_release_smoke_ruby/version.rb",
    ];
    private static readonly string[] RequirePaths = ["lib"];
    private const string DirectoryName = "src/hcoona-release-smoke-ruby";

    private static GitFixture? shared;
    private static string sharedCandidate = "";

    [ClassInitialize]
    public static async Task InitializeAsync(TestContext classContext)
    {
        GitFixture repo = await GitFixture.CreateAsync(classContext.CancellationToken);
        try
        {
            await repo.SetAsync(DirectoryName + "/hcoona-release-smoke-ruby.gemspec");
            await repo.SetAsync(DirectoryName + "/lib/hcoona_release_smoke_ruby.rb");
            await repo.SetAsync("src/unrelated/asciidoctor-latexmath.gemspec");
            sharedCandidate = await repo.CommitAsync();
            await repo.GitAsync("reset", "--hard", sharedCandidate);
            shared = repo;
        }
        catch { repo.Dispose(); throw; }
    }

    [ClassCleanup]
    public static void Cleanup() => shared?.Dispose();

    private static NativeCommandResult Answer(object value) =>
        new(NativeTermination.Exited, 0, JsonSerializer.Serialize(value), "", 0, null);

    private static (CiPlan Plan, RubyPackageRequest Request) Request(string checkout,
        string candidate, string directory, string scratch)
    {
        var source = new RubySourceGemFacts(
            Path.Combine(checkout, directory, "hcoona-release-smoke-ruby.gemspec"),
            new("hcoona-release-smoke-ruby", "1.2.3", "ruby", RequirePaths, Files, []));
        var unit = new ReleaseUnitDeclaration(
            directory + "/workflow-delivery.release-unit.yml", "native-smoke",
            [new("gem", RubyPackageChecks.Build, "hcoona-release-smoke-ruby.gemspec",
                [new("gem", "primary-package", "ruby-gem")])]);
        CheckSpec[] checks = RubyPackageChecks.Expand(unit, checkout,
            new Dictionary<string, RubySourceGemFacts> { [directory] = source });
        var plan = new CiPlan(candidate, candidate, RubyFactsAssembler.Scope,
            checks.Select(check => new PlannedCheck(check, [RubyPackageChecks.Preset],
                [], [CheckOrigin.Preset])).ToArray());
        var tools = new RubyFactTools(Path.Combine(checkout, "ruby"),
            Path.Combine(checkout, "facts.rb"), scratch,
            new Dictionary<string, string?> { ["PATH"] = "native tools" }, 30);
        return (plan, new(checkout, scratch, tools, Path.Combine(checkout, "gem"),
            Path.Combine(checkout, "nbgv")));
    }

    private static async Task<NativeCommandResult> NativeAsync(RubyPackageRequest request,
        NativeCommand command, string directory, CancellationToken token)
    {
        Assert.IsFalse(command.InheritEnvironment);
        if (command.Executable == request.Nbgv)
        {
            Assert.AreEqual(Path.Combine(request.Checkout, directory), command.Directory);
            return Answer(new { SemVer2 = "1.2.3" });
        }
        if (command.Arguments[0] == request.Gem)
        {
            Assert.AreEqual("--norc", command.Arguments[2]);
            Assert.IsTrue(command.CloseStandardInput);
            if (command.Arguments[1] == "build")
            {
                string version = Path.Combine(request.Checkout, directory,
                    "lib/hcoona_release_smoke_ruby/version.rb");
                Assert.Contains("1.2.3", await File.ReadAllTextAsync(version, token));
                await File.WriteAllTextAsync(command.Arguments[^1], "original gem", token);
            }
            else
            {
                Assert.AreEqual("install", command.Arguments[1]);
                Assert.Contains("--local", command.Arguments);
                Assert.StartsWith(request.Scratch, command.Environment!["GEM_HOME"]!);
                Assert.AreEqual(command.Environment["GEM_HOME"], command.Environment["GEM_PATH"]);
            }
            return Answer(new { });
        }
        if (command.Arguments[0] == "-e")
        {
            Assert.Contains("HcoonaReleaseSmokeRuby.project_id", command.Arguments[1]);
            Assert.AreEqual("1.2.3", command.Arguments[2]);
            Assert.IsNull(command.Environment!["RUBYLIB"]);
            return Answer(new { });
        }
        using JsonDocument input = JsonDocument.Parse(
            await File.ReadAllTextAsync(command.Arguments[^1], token));
        string path = input.RootElement.GetProperty("path").GetString()!;
        string operation = input.RootElement.GetProperty("operation").GetString()!;
        if (operation is "version" or "gemspec")
            Assert.AreEqual(Path.GetFullPath(Path.Combine(request.Checkout, directory,
                "hcoona-release-smoke-ruby.gemspec")), Path.GetFullPath(path));
        if (operation == "version")
            return Answer(new
            {
                gemspec = path,
                raw = "1.2.3",
                version = "1.2.3",
                literal = "\"1.2.3\"",
            });
        var gem = new
        {
            name = "hcoona-release-smoke-ruby",
            version = "1.2.3",
            platform = "ruby",
            require_paths = RequirePaths,
            files = Files,
            runtime_dependencies = Array.Empty<object>(),
        };
        if (operation == "gemspec")
            return Answer(new
            {
                gemspec = path,
                gem.name,
                gem.version,
                gem.platform,
                gem.require_paths,
                gem.files,
                gem.runtime_dependencies,
            });
        Assert.AreEqual("archive", operation);
        return Answer(new
        {
            archive = path,
            gem.name,
            gem.version,
            gem.platform,
            gem.require_paths,
            gem.files,
            gem.runtime_dependencies,
            contents = Files,
        });
    }

    [TestMethod]
    public async Task OriginalGemExecutesCompleteChecksInCleanConsumer()
    {
        using var fixture = new Fixture(copyCheckout: true);
        RubyPackageRun result = await fixture.RunAsync(context.CancellationToken);
        Assert.IsTrue(ResultCollector.Collect(fixture.Plan, result.Results).Satisfied);
        RubyOriginalGem gem = Assert.ContainsSingle(result.Gems);
        Assert.AreEqual("original gem",
            await File.ReadAllTextAsync(gem.Path, context.CancellationToken));
        Assert.IsEmpty(result.Failures);
        NativeCommand install = Assert.ContainsSingle(result.Commands.Where(item =>
            item.Command.Arguments is [_, "install", ..])).Command;
        Assert.Contains(gem.Path, install.Arguments);
        NativeCommand consumer = Assert.ContainsSingle(result.Commands.Where(item =>
            item.Command.Arguments[0] == "-e")).Command;
        Assert.AreEqual(install.Directory, consumer.Directory);
        Assert.StartsWith(fixture.NativeRequest.Scratch, consumer.Directory);
        Assert.IsFalse(File.Exists(Path.Combine(shared!.Directory, DirectoryName,
            "lib/hcoona_release_smoke_ruby/version.rb")));
    }

    [TestMethod]
    public async Task FailedBuildRetainsBoundedNativeDiagnosticAndSkipsConsumers()
    {
        using var fixture = new Fixture(copyCheckout: true);
        RubyPackageRun result = await RubyPackageExecution.RunAsync(fixture.Plan,
            fixture.NativeRequest, (command, token) => command.Arguments is [_, "build", ..]
                ? Task.FromResult(Answer(new { }) with { ExitCode = 7, Stderr = LongGemStderr })
                : fixture.ExecuteAsync(command, token), context.CancellationToken);

        Assert.IsFalse(ResultCollector.Collect(fixture.Plan, result.Results).Satisfied);
        string failure = Assert.ContainsSingle(result.Failures).Error;
        Assert.StartsWith("Native gem command failed: Exited, exit 7. stderr: [truncated] ",
            failure);
        Assert.EndsWith(" controlled gem build error", failure);
        Assert.DoesNotContain("controlled gem head", failure);
        Assert.AreEqual(CheckStatus.Failed, Status(result, RubyPackageChecks.Build));
        Assert.AreEqual(CheckStatus.Skipped, Status(result, RubyPackageChecks.Contents));
        Assert.AreEqual(CheckStatus.Skipped, Status(result, RubyPackageChecks.Consumer));
        Assert.IsEmpty(result.Gems);
    }

    [TestMethod]
    public async Task ArchiveChangedDuringInspectionCannotQualify()
    {
        using var fixture = new Fixture(copyCheckout: true);
        RubyPackageRequest request = fixture.NativeRequest;
        RubyPackageRun result = await RubyPackageExecution.RunAsync(fixture.Plan, request,
            async (command, token) =>
        {
            NativeCommandResult native = await fixture.ExecuteAsync(command, token);
            if (command.Arguments[0] != request.Gem && command.Arguments[0] != "-e" &&
                command.Executable != request.Nbgv)
            {
                using JsonDocument input = JsonDocument.Parse(
                    await File.ReadAllTextAsync(command.Arguments[^1], token));
                if (input.RootElement.GetProperty("operation").GetString() == "archive")
                    await File.WriteAllTextAsync(
                        input.RootElement.GetProperty("path").GetString()!, "changed gem", token);
            }
            return native;
        }, context.CancellationToken);

        Assert.IsFalse(ResultCollector.Collect(fixture.Plan, result.Results).Satisfied);
        Assert.AreEqual(CheckStatus.Failed, Status(result, RubyPackageChecks.Build));
        Assert.AreEqual(CheckStatus.Skipped, Status(result, RubyPackageChecks.Consumer));
        Assert.IsEmpty(result.Gems);
    }

    [TestMethod]
    public async Task FailedPublicApiCheckRetainsOriginalGemAndArchiveQualification()
    {
        using var fixture = new Fixture(copyCheckout: true);
        RubyPackageRun result = await RubyPackageExecution.RunAsync(fixture.Plan,
            fixture.NativeRequest,
            (command, token) => command.Arguments[0] == "-e"
                ? Task.FromResult(Answer(new { }) with { ExitCode = 3 })
                : fixture.ExecuteAsync(command, token), context.CancellationToken);

        Assert.IsFalse(ResultCollector.Collect(fixture.Plan, result.Results).Satisfied);
        Assert.AreEqual(CheckStatus.Passed, Status(result, RubyPackageChecks.Contents));
        Assert.AreEqual(CheckStatus.Failed, Status(result, RubyPackageChecks.Consumer));
        Assert.ContainsSingle(result.Gems);
    }

    private static CheckStatus Status(RubyPackageRun result, string check) =>
        result.Results.Single(item => item.Key.Check == check).Status;

    private static readonly string LongGemStderr = "controlled gem head " +
        new string('x', NativeDiagnostic.TailLength) + " controlled gem build error";

    [TestMethod]
    public async Task EmptySelectionPerformsNoProductPreparation()
    {
        using var fixture = new Fixture();
        RubyPackageRun result = await RubyPackageExecution.RunAsync(
            fixture.Plan with { Checks = [] }, fixture.NativeRequest, UnexpectedNativeWork,
            context.CancellationToken);
        Assert.IsEmpty(result.Commands);
        Assert.IsEmpty(result.Gems);
        Assert.IsEmpty(System.IO.Directory.GetFileSystemEntries(fixture.NativeRequest.Scratch));
    }

    [TestMethod]
    public async Task CancelledNativeBuildCannotPassOrRunConsumers()
    {
        using var fixture = new Fixture(copyCheckout: true);
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(
            context.CancellationToken);
        RubyPackageRun result = await RubyPackageExecution.RunAsync(fixture.Plan,
            fixture.NativeRequest,
            async (command, token) =>
            {
                NativeCommandResult answer = await fixture.ExecuteAsync(command, token);
                if (command.Arguments is [_, "build", ..])
                    cancellation.Cancel();
                return answer;
            }, cancellation.Token);
        Assert.IsFalse(ResultCollector.Collect(fixture.Plan, result.Results).Satisfied);
        Assert.IsTrue(result.Results.All(result => result.Status == CheckStatus.Cancelled));
        Assert.IsEmpty(result.Gems);
        Assert.IsFalse(result.Commands.Any(command => command.Command.Arguments[0] == "-e"));
    }

    [TestMethod]
    public async Task IncompleteGemObligationsFailBeforeNativeWork()
    {
        using var fixture = new Fixture();
        CiPlan incomplete = fixture.Plan with { Checks = [fixture.Plan.Checks[0]] };
        await Assert.ThrowsExactlyAsync<InvalidDataException>(() =>
            RubyPackageExecution.RunAsync(incomplete, fixture.NativeRequest, UnexpectedNativeWork,
                context.CancellationToken));
    }

    private static Task<NativeCommandResult> UnexpectedNativeWork(NativeCommand command,
        CancellationToken token) =>
        throw new AssertFailedException($"Unexpected native command: {command.Executable}");

    private sealed class Fixture : IDisposable
    {
        private readonly string root =
            System.IO.Directory.CreateTempSubdirectory("workflow-ruby-gem-").FullName;

        internal Fixture(bool copyCheckout = false)
        {
            try
            {
                string checkout = copyCheckout ? PreparationCheckout.Copy(shared!.Directory,
                    root) : shared!.Directory;
                string scratch = Directory.CreateDirectory(Path.Combine(root, "scratch"))
                    .FullName;
                (Plan, NativeRequest) = Request(checkout, sharedCandidate, DirectoryName, scratch);
            }
            catch { Dispose(); throw; }
        }

        internal CiPlan Plan { get; }
        internal RubyPackageRequest NativeRequest { get; }

        internal Task<NativeCommandResult> ExecuteAsync(NativeCommand command,
            CancellationToken token) => NativeAsync(NativeRequest, command, DirectoryName, token);

        internal Task<RubyPackageRun> RunAsync(CancellationToken token) =>
            RubyPackageExecution.RunAsync(Plan, NativeRequest, ExecuteAsync, token);

        public void Dispose() => PreparationCheckout.Delete(root);
    }
}
