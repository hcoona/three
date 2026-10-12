using System.Text.Json;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using WorkflowDelivery.CI;
using WorkflowDelivery.Platform;
using WorkflowDelivery.Repository;
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

    private static NativeCommandResult Answer(object value) =>
        new(NativeTermination.Exited, 0, JsonSerializer.Serialize(value), "", 0, null);

    private static async Task<(CiPlan Plan, RubyPackageRequest Request)> PrepareAsync(
        GitFixture repo
    )
    {
        await repo.SetAsync(DirectoryName + "/hcoona-release-smoke-ruby.gemspec");
        await repo.SetAsync(DirectoryName + "/lib/hcoona_release_smoke_ruby.rb");
        await repo.SetAsync("src/unrelated/asciidoctor-latexmath.gemspec");
        string candidate = await repo.CommitAsync();
        await repo.GitAsync("reset", "--hard", candidate);
        string scratch = Path.Combine(Path.GetDirectoryName(repo.Directory)!, "gem-output");
        System.IO.Directory.CreateDirectory(scratch);
        var source = new RubySourceGemFacts(
            Path.Combine(repo.Directory, DirectoryName, "hcoona-release-smoke-ruby.gemspec"),
            new("hcoona-release-smoke-ruby", "1.2.3", "ruby", RequirePaths, Files, [])
        );
        var unit = new ReleaseUnitDeclaration(
            DirectoryName + "/workflow-delivery.release-unit.yml",
            "native-smoke",
            [
                new(
                    "gem",
                    RubyPackageChecks.Build,
                    "hcoona-release-smoke-ruby.gemspec",
                    [new("gem", "primary-package", "ruby-gem")]
                ),
            ]
        );
        CheckSpec[] checks = RubyPackageChecks.Expand(
            unit,
            repo.Directory,
            new Dictionary<string, RubySourceGemFacts> { [DirectoryName] = source }
        );
        var plan = new CiPlan(
            candidate,
            candidate,
            RubyFactsAssembler.Scope,
            checks
                .Select(check => new PlannedCheck(
                    check,
                    [RubyPackageChecks.Preset],
                    [],
                    [CheckOrigin.Preset]
                ))
                .ToArray()
        );
        return (
            plan,
            new(
                repo.Directory,
                scratch,
                new(
                    Path.Combine(repo.Directory, "ruby"),
                    Path.Combine(repo.Directory, "facts.rb"),
                    scratch,
                    new Dictionary<string, string?> { ["PATH"] = "native tools" },
                    30
                ),
                Path.Combine(repo.Directory, "gem"),
                Path.Combine(repo.Directory, "nbgv")
            )
        );
    }

    private static async Task<NativeCommandResult> NativeAsync(
        RubyPackageRequest request,
        NativeCommand command,
        string defect,
        CancellationToken token
    )
    {
        Assert.IsFalse(command.InheritEnvironment);
        if (command.Executable == request.Nbgv)
        {
            Assert.AreEqual(Path.Combine(request.Checkout, DirectoryName), command.Directory);
            return defect == "source-failed"
                ? Answer(new { }) with
                {
                    ExitCode = 4,
                }
                : Answer(new { SemVer2 = "1.2.3" });
        }
        if (command.Arguments[0] == request.Gem)
        {
            Assert.AreEqual("--norc", command.Arguments[2]);
            Assert.IsTrue(command.CloseStandardInput);
            if (command.Arguments[1] == "build")
            {
                if (defect == "failed-build")
                    return Answer(new { }) with { ExitCode = 7 };
                if (defect != "missing-output")
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
            return defect == "consumer-failed"
                ? Answer(new { }) with
                {
                    ExitCode = 3,
                }
                : Answer(new { });
        }
        using JsonDocument input = JsonDocument.Parse(
            await File.ReadAllTextAsync(command.Arguments[^1], token)
        );
        string path = input.RootElement.GetProperty("path").GetString()!;
        string operation = input.RootElement.GetProperty("operation").GetString()!;
        if (operation is "version" or "gemspec")
            Assert.AreEqual(
                Path.GetFullPath(
                    Path.Combine(
                        request.Checkout,
                        DirectoryName,
                        "hcoona-release-smoke-ruby.gemspec"
                    )
                ),
                Path.GetFullPath(path)
            );
        if (operation == "version")
            return Answer(
                new
                {
                    gemspec = path,
                    raw = "1.2.3",
                    version = "1.2.3",
                    literal = "\"1.2.3\"",
                }
            );
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
            return Answer(
                new
                {
                    gemspec = path,
                    gem.name,
                    gem.version,
                    gem.platform,
                    gem.require_paths,
                    gem.files,
                    gem.runtime_dependencies,
                }
            );
        Assert.AreEqual("archive", operation);
        if (defect == "changed-original")
            await File.WriteAllTextAsync(path, "changed gem", token);
        return Answer(
            new
            {
                archive = path,
                gem.name,
                gem.version,
                gem.platform,
                gem.require_paths,
                gem.files,
                gem.runtime_dependencies,
                contents = defect == "wrong-contents" ? Array.Empty<string>() : Files,
            }
        );
    }

    [TestMethod]
    public async Task OriginalGemExecutesCompleteChecksInCleanConsumer()
    {
        using var repo = await GitFixture.CreateAsync(context.CancellationToken);
        var (plan, request) = await PrepareAsync(repo);
        plan = JsonSerializer.Deserialize<CiPlan>(JsonSerializer.Serialize(plan))!;
        RubyPackageRun result = await RubyPackageExecution.RunAsync(
            plan,
            request,
            (command, token) => NativeAsync(request, command, "", token),
            context.CancellationToken
        );
        Assert.IsTrue(ResultCollector.Collect(plan, result.Results).Satisfied);
        RubyOriginalGem gem = Assert.ContainsSingle(result.Gems);
        Assert.AreEqual(
            "original gem",
            await File.ReadAllTextAsync(gem.Path, context.CancellationToken)
        );
        Assert.HasCount(3, result.Results);
        Assert.IsEmpty(result.Failures);
        NativeCommand consumer = result.Commands.Last().Command;
        Assert.StartsWith(request.Scratch, consumer.Directory);
        Assert.DoesNotContain(request.Checkout, consumer.Arguments);
    }

    [TestMethod]
    [DataRow("source-failed")]
    [DataRow("failed-build")]
    [DataRow("missing-output")]
    [DataRow("changed-original")]
    [DataRow("wrong-contents")]
    [DataRow("consumer-failed")]
    public async Task FailedBuildContentsOrConsumerCannotPass(string defect)
    {
        using var repo = await GitFixture.CreateAsync(context.CancellationToken);
        var (plan, request) = await PrepareAsync(repo);
        RubyPackageRun result = await RubyPackageExecution.RunAsync(
            plan,
            request,
            (command, token) => NativeAsync(request, command, defect, token),
            context.CancellationToken
        );
        Assert.IsFalse(ResultCollector.Collect(plan, result.Results).Satisfied);
        Assert.IsNotEmpty(result.Failures);
        if (defect != "consumer-failed")
        {
            Assert.AreEqual(CheckStatus.Failed, result.Results[0].Status);
            Assert.AreEqual(CheckStatus.Skipped, result.Results[1].Status);
            Assert.AreEqual(CheckStatus.Skipped, result.Results[2].Status);
        }
        else
        {
            Assert.AreEqual(CheckStatus.Passed, result.Results[0].Status);
            Assert.AreEqual(CheckStatus.Passed, result.Results[1].Status);
            Assert.AreEqual(CheckStatus.Failed, result.Results[2].Status);
        }
    }

    [TestMethod]
    public async Task EmptySelectionPerformsNoProductPreparation()
    {
        using var repo = await GitFixture.CreateAsync(context.CancellationToken);
        var (plan, request) = await PrepareAsync(repo);
        RubyPackageRun result = await RubyPackageExecution.RunAsync(
            plan with
            {
                Checks = [],
            },
            request,
            (_, _) =>
            {
                Assert.Fail("An empty gem selection cannot prepare or build products.");
                return Task.FromResult(Answer(new { }));
            },
            context.CancellationToken
        );
        Assert.IsEmpty(result.Commands);
        Assert.IsEmpty(result.Gems);
        Assert.IsEmpty(System.IO.Directory.GetFileSystemEntries(request.Scratch));
    }

    [TestMethod]
    public async Task CancelledNativeBuildCannotPassOrRunConsumers()
    {
        using var repo = await GitFixture.CreateAsync(context.CancellationToken);
        var (plan, request) = await PrepareAsync(repo);
        using var cancellation = new CancellationTokenSource();
        RubyPackageRun result = await RubyPackageExecution.RunAsync(
            plan,
            request,
            async (command, token) =>
            {
                NativeCommandResult answer = await NativeAsync(request, command, "", token);
                if (command.Arguments[0] == request.Gem && command.Arguments[1] == "build")
                    cancellation.Cancel();
                return answer;
            },
            cancellation.Token
        );
        Assert.IsFalse(ResultCollector.Collect(plan, result.Results).Satisfied);
        Assert.IsTrue(result.Results.All(result => result.Status == CheckStatus.Cancelled));
        Assert.IsEmpty(result.Gems);
        Assert.IsFalse(result.Commands.Any(command => command.Command.Arguments[0] == "-e"));
    }

    [TestMethod]
    public async Task UnsupportedOrIncompleteGemPlanFailsBeforeEffects()
    {
        using var repo = await GitFixture.CreateAsync(context.CancellationToken);
        var (plan, request) = await PrepareAsync(repo);
        await Assert.ThrowsExactlyAsync<InvalidDataException>(() =>
            RubyPackageExecution.RunAsync(
                plan with
                {
                    Checks = [plan.Checks[0]],
                },
                request,
                (_, _) =>
                {
                    Assert.Fail("Incomplete package obligations cannot execute.");
                    return Task.FromResult(Answer(new { }));
                },
                context.CancellationToken
            )
        );
    }
}
