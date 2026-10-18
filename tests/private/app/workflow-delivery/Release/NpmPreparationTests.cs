using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using WorkflowDelivery.Platform;
using WorkflowDelivery.Release;
using WorkflowDelivery.Tests.CI;
using GitFixture = WorkflowDelivery.Tests.Repository.GitReaderTests.GitFixture;

namespace WorkflowDelivery.Tests.Release;

[TestClass]
public sealed class NpmPreparationTests(TestContext context)
{
    // Every row varies only the request or controlled native answers and reads one
    // class-owned commit; preparation writes only to the row's own scratch.
    private static GitFixture? shared;
    private static string sharedCommit = "";

    [ClassInitialize]
    public static async Task InitializeAsync(TestContext classContext)
    {
        GitFixture repo = await GitFixture.CreateAsync(classContext.CancellationToken);
        try
        {
            sharedCommit = await Fixture.CommitAsync(repo);
            shared = repo;
        }
        catch { repo.Dispose(); throw; }
    }

    [ClassCleanup]
    public static void Cleanup() => shared?.Dispose();

    [TestMethod]
    [DataRow(true)]
    [DataRow(false)]
    public async Task BuddyDevelopmentRevisionUsesSameCompleteNativePreparation(
        bool dryRun)
    {
        using var fixture = Fixture.Shared(shared!, sharedCommit);
        fixture.Request = fixture.Request with { Subject = fixture.Request.Subject with
            { Channel = "buddy", Destination = "github-packages-npm", DryRun = dryRun } };

        NpmPreparationResult result = await fixture.RunAsync(context.CancellationToken);

        Assert.IsEmpty(result.Errors);
        Assert.ContainsSingle(result.Prepared.Files);
        Assert.AreEqual(fixture.Request.Subject, result.Prepared.Subject);
        Assert.HasCount(7, result.Prepared.Checks);
        Assert.IsTrue(result.Prepared.Checks.All(check =>
            check.Status == ReleaseCheckStatus.Passed));
    }

    [TestMethod]
    public async Task SelectedPreparationQualifiesOriginalWithoutCiPlan()
    {
        // Defect-free control for the shared commit.
        using var fixture = Fixture.Shared(shared!, sharedCommit);

        NpmPreparationResult result = await fixture.RunAsync(context.CancellationToken);

        Assert.IsEmpty(result.Errors);
        Assert.HasCount(7, result.Prepared.Checks);
        Assert.IsTrue(result.Prepared.Checks.All(check =>
            check.Status == ReleaseCheckStatus.Passed));
        ReleaseFile output = Assert.ContainsSingle(result.Prepared.Files);
        Assert.AreEqual("npm-tarball", output.Output);
        Assert.AreEqual("1.2.3", output.Version);
        Assert.AreEqual("original/package.tgz", output.Path);
        byte[] original = await File.ReadAllBytesAsync(Path.Combine(fixture.Scratch, output.Path),
            context.CancellationToken);
        Assert.AreEqual(original.LongLength, output.Size);
        Assert.AreEqual(Convert.ToHexStringLower(SHA256.HashData(original)), output.Sha256);
        Assert.AreEqual(fixture.Request.Subject, result.Prepared.Subject);
        Assert.IsTrue(result.Prepared.Subject.DryRun);
        Assert.HasCount(1, result.Commands.Where(command =>
            command.Command.Arguments.Contains("pack")));
        NativeCommand installed = result.Commands.Single(command =>
            command.Command.Arguments.Contains("install")).Command;
        Assert.Contains("--offline", installed.Arguments);
        Assert.Contains("--ignore-scripts", installed.Arguments);
        Assert.DoesNotContain("publish", result.Commands.SelectMany(command =>
            command.Command.Arguments));
        foreach (ReleaseCommand command in result.Commands)
        {
            Assert.IsFalse(command.Command.InheritEnvironment);
            Assert.AreEqual(Path.Combine(fixture.Scratch, "home"),
                command.Command.Environment!["HOME"]);
            Assert.IsFalse(command.Command.Environment.ContainsKey("GITHUB_TOKEN"));
            Assert.IsFalse(command.Command.Environment.ContainsKey("NODE_OPTIONS"));
            Assert.IsFalse(command.Command.Environment.ContainsKey("NPM_TOKEN"));
        }
    }

    [TestMethod]
    [DataRow("build")]
    [DataRow("test")]
    [DataRow("pack")]
    [DataRow("contents")]
    [DataRow("consumer")]
    public async Task FailedQualificationRetainsFailureAndStopsDependentWork(string failed)
    {
        using var fixture = Fixture.Shared(shared!, sharedCommit);
        fixture.Fail = failed;

        NpmPreparationResult result = await fixture.RunAsync(context.CancellationToken);

        Assert.IsEmpty(result.Prepared.Files);
        Assert.AreEqual(ReleaseCheckStatus.Failed,
            result.Prepared.Checks.Single(check => check.Id == failed).Status);
        Assert.IsNotEmpty(result.Errors);
        Assert.IsFalse(result.Prepared.Checks.Any(check => check.Id == "original-bytes" &&
            check.Status == ReleaseCheckStatus.Passed));
        Assert.IsFalse(result.Commands.Any(command =>
            Array.IndexOf(result.Prepared.RequiredChecks, command.Check) >
                Array.IndexOf(result.Prepared.RequiredChecks, failed)));
    }

    [TestMethod]
    public async Task NativeVersionDisagreementCannotQualifyOrInstallPackage()
    {
        using var fixture = Fixture.Shared(shared!, sharedCommit);
        fixture.ExpectedVersion = "9.9.9";

        NpmPreparationResult result = await fixture.RunAsync(context.CancellationToken);

        Assert.IsEmpty(result.Prepared.Files);
        Assert.AreEqual(ReleaseCheckStatus.Failed,
            result.Prepared.Checks.Single(check => check.Id == "contents").Status);
        Assert.IsFalse(result.Commands.Any(command =>
            command.Command.Arguments.Contains("install")));
        Assert.Contains("version", Assert.ContainsSingle(result.Errors));
    }

    [TestMethod]
    public async Task ConsumerCannotModifyQualifiedOriginal()
    {
        using var fixture = Fixture.Shared(shared!, sharedCommit);
        fixture.MutateOriginal = true;

        NpmPreparationResult result = await fixture.RunAsync(context.CancellationToken);

        Assert.IsEmpty(result.Prepared.Files);
        Assert.AreEqual(ReleaseCheckStatus.Failed,
            result.Prepared.Checks.Single(check => check.Id == "original-bytes").Status);
        Assert.Contains("changed", Assert.ContainsSingle(result.Errors));
    }

    [TestMethod]
    public async Task MissingNativeArchiveCannotBecomeQualifiedOutput()
    {
        using var fixture = Fixture.Shared(shared!, sharedCommit);
        fixture.OmitArchive = true;
        NpmPreparationResult result = await fixture.RunAsync(context.CancellationToken);
        Assert.IsEmpty(result.Prepared.Files);
        Assert.AreEqual(ReleaseCheckStatus.Failed,
            result.Prepared.Checks.Single(check => check.Id == "pack").Status);
        Assert.IsNotEmpty(result.Errors);
        Assert.IsFalse(result.Commands.Any(command =>
            command.Command.Arguments.Contains("install")));
    }

    [TestMethod]
    public async Task CancelledQualificationStopsRemainingWorkAndCannotSucceed()
    {
        using var fixture = Fixture.Shared(shared!, sharedCommit);
        fixture.CancelCheck = "build";
        NpmPreparationResult result = await fixture.RunAsync(context.CancellationToken);
        Assert.IsEmpty(result.Prepared.Files);
        Assert.AreEqual(ReleaseCheckStatus.Cancelled,
            result.Prepared.Checks.Single(check => check.Id == "build").Status);
        Assert.IsTrue(result.Prepared.Checks.Skip(2).All(check =>
            check.Status == ReleaseCheckStatus.Skipped));
        Assert.IsFalse(result.Commands.Any(command => command.Command.Arguments.Contains("pack")));
    }

    // Each row breaks one distinct invariant between the native answer and the fixture's own
    // committed root selection; the message names the tool and the failed condition.
    [TestMethod]
    [DataRow("node", "missing", "has no single locked selection")]
    [DataRow("node", "inactive", "is not active")]
    [DataRow("node", "source-type", "not the committed root")]
    [DataRow("node", "source-path", "not the committed root")]
    [DataRow("node", "readback", "differs from the committed locked selection")]
    [DataRow("pnpm", "inactive", "is not active")]
    [DataRow("pnpm", "readback", "differs from the committed locked selection")]
    public async Task ToolOutsideCommittedSelectionBlocksPreparationBeforeProductCode(
        string tool, string defect, string condition)
    {
        using var fixture = Fixture.Shared(shared!, sharedCommit);
        fixture.ToolDefect = (tool, defect);
        NpmPreparationResult result = await fixture.RunAsync(context.CancellationToken);
        Assert.IsEmpty(result.Prepared.Files);
        Assert.AreEqual(ReleaseCheckStatus.Failed, result.Prepared.Checks[0].Status);
        Assert.IsTrue(result.Prepared.Checks.Skip(1).All(check =>
            check.Status == ReleaseCheckStatus.Skipped));
        Assert.IsFalse(result.Commands.Any(command => command.Check == "build"));
        string error = Assert.ContainsSingle(result.Errors);
        Assert.Contains(" " + tool + " ", error);
        Assert.Contains(condition, error);
    }

    [TestMethod]
    [DataRow("Cancelled", "Cancelled", "native-input")]
    [DataRow("TimedOut", "TimedOut", "native-input")]
    [DataRow("Cancelled", "Cancelled", "build")]
    [DataRow("TimedOut", "TimedOut", "build")]
    public async Task NativeTerminationRemainsVisibleInRequiredQualification(
        string native, string status, string checkId)
    {
        using var fixture = Fixture.Shared(shared!, sharedCommit);
        fixture.Termination = Enum.Parse<NativeTermination>(native);
        fixture.TerminationCheck = checkId;
        NpmPreparationResult result = await fixture.RunAsync(context.CancellationToken);
        Assert.AreEqual(Enum.Parse<ReleaseCheckStatus>(status),
            result.Prepared.Checks.Single(check => check.Id == checkId).Status);
        Assert.AreEqual(fixture.Termination,
            result.Commands.Last(command => command.Check == checkId).Result.Termination);
        Assert.IsEmpty(result.Prepared.Files);
        Assert.IsTrue(result.Prepared.Checks.Skip(checkId == "build" ? 2 : 1).All(check =>
            check.Status == ReleaseCheckStatus.Skipped));
    }

    private sealed class Fixture(GitFixture repository, NpmPreparationRequest request)
        : IDisposable
    {
        internal NpmPreparationRequest Request { get; set; } = request;
        internal string Scratch => Request.Scratch;
        internal string? Fail { get; set; }
        internal string ExpectedVersion { get; set; } = "1.2.3";
        internal bool MutateOriginal { get; set; }
        internal bool OmitArchive { get; set; }
        internal string? CancelCheck { get; set; }
        internal (string Tool, string Defect)? ToolDefect { get; set; }
        internal NativeTermination? Termination { get; set; }
        internal string TerminationCheck { get; set; } = "build";
        private string? archive;

        // Reads a class-owned committed repository; the row owns only its scratch.
        internal static Fixture Shared(GitFixture repository, string commit) => new(repository,
            new(new(commit, "refs/heads/development", "hcoona-release-smoke-npm",
                "product/workflow-delivery.release-unit.yml", "official", "npmjs", true),
                repository.Directory,
                Directory.CreateTempSubdirectory("workflow-release-").FullName));

        internal static async Task<string> CommitAsync(GitFixture repository)
        {
            await repository.SetAsync("product/package.json", NodeExecutionFixture.Metadata);
            await repository.SetAsync("product/workflow-delivery.release-unit.yml", """
                schema: workflow-delivery/v3/release-unit
                release-unit: hcoona-release-smoke-npm
                builds:
                  - id: npm-package
                    definition: node/npm-package-v1
                    entry-point: package.json
                    outputs:
                      - id: npm-tarball
                        role: primary-package
                        kind: npm-tarball
                """);
            await repository.SetAsync("product/workflow-delivery.quality.yml", """
                schema: workflow-delivery/v3/quality-selection
                ecosystems:
                  node:
                    preset: node/hcoona-release-smoke-npm-v1
                """);
            // The fake answers from the actual committed root tool configuration.
            await repository.SetAsync("mise.toml", RepositoryToolSelection.MiseToml);
            await repository.SetAsync("mise.lock", RepositoryToolSelection.MiseLock);
            foreach (string path in new[] { "pnpm-lock.yaml", "pnpm-workspace.yaml" })
                await repository.SetAsync(path, "committed native fixture");
            string commit = await repository.CommitAsync();
            await repository.GitAsync("reset", "--hard", commit);
            return commit;
        }

        internal Task<NpmPreparationResult> RunAsync(CancellationToken token) =>
            NpmPreparation.PrepareAsync(Request, ExecuteAsync,
                (_, _, _, _) => ExpectedVersion, token);

        private Task<NativeCommandResult> ExecuteAsync(NativeCommand command,
            CancellationToken token)
        {
            token.ThrowIfCancellationRequested();
            string[] args = command.Arguments;
            string operation = args.Contains("pack") ? "pack" : args.Contains("install")
                ? "consumer" : args.Contains("pkg") && !args.Contains("publishConfig.directory")
                    ? "contents" : args[^1];
            if (operation == Fail)
                return Task.FromResult(NodeExecutionFixture.Failure());
            if (operation == CancelCheck) throw new OperationCanceledException(token);
            if ((TerminationCheck == "build" && operation == "build" ||
                TerminationCheck == "native-input" && command.Executable == "mise") &&
                Termination is { } termination)
                return Task.FromResult(new NativeCommandResult(termination, null, "", "", 1,
                    "Native operation stopped."));
            string output = "";
            if (command.Executable == "mise")
            {
                string tool = args[^1];
                string? defect = ToolDefect?.Tool == tool ? ToolDefect.Value.Defect : null;
                output = defect == "missing" ? "[]" : JsonSerializer.Serialize(new[] { new
                {
                    version = Selected(tool),
                    requested_version = RepositoryToolSelection.Requested(File.ReadAllText(
                        Path.Combine(repository.Directory, "mise.toml")), tool),
                    active = defect != "inactive",
                    source = new
                    {
                        type = defect == "source-type" ? ".tool-versions" : "mise.toml",
                        path = Path.Combine(defect == "source-path" ? Path.Combine(
                            repository.Directory, "product") : repository.Directory,
                            "mise.toml"),
                    },
                } });
            }
            else if (args.Contains("--version"))
            {
                string tool = command.Executable;
                output = ToolDefect == (tool, "readback") ? "0.0.0"
                    : (tool == "node" ? "v" : "") + Selected(tool);
            }
            else if (args.Contains("pkg"))
                output = args.Contains("publishConfig.directory")
                    ? """
                      {"name":"@hcoona/hcoona-release-smoke-npm",
                       "scripts":{"build":"native build","test":"native test"}}
                      """
                    : NodeExecutionFixture.Metadata;
            else if (args.Contains("pack"))
            {
                archive = args[^1];
                if (!OmitArchive)
                    PackageArchiveFixture.Write(archive, PackageArchiveFixture.Required());
            }
            else if (command.Executable == "node" && args.Contains("--eval") && MutateOriginal)
                File.AppendAllText(archive!, "changed original bytes");
            return Task.FromResult(NodeExecutionFixture.Success(output));
        }

        // The fixture's own committed root lock selects each tool.
        private string Selected(string tool) => RepositoryToolSelection.Locked(
            File.ReadAllText(Path.Combine(repository.Directory, "mise.lock")), tool);

        public void Dispose() => Directory.Delete(Scratch, recursive: true);
    }
}
