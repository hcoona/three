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
    [TestMethod]
    [DataRow(true)]
    [DataRow(false)]
    public async Task BuddyDevelopmentRevisionUsesSameCompleteNativePreparation(
        bool dryRun)
    {
        using var fixture = await Fixture.CreateAsync(context.CancellationToken);
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
        using var fixture = await Fixture.CreateAsync(context.CancellationToken);

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
        using var fixture = await Fixture.CreateAsync(context.CancellationToken);
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
        using var fixture = await Fixture.CreateAsync(context.CancellationToken);
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
        using var fixture = await Fixture.CreateAsync(context.CancellationToken);
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
        using var fixture = await Fixture.CreateAsync(context.CancellationToken);
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
        using var fixture = await Fixture.CreateAsync(context.CancellationToken);
        fixture.CancelCheck = "build";
        NpmPreparationResult result = await fixture.RunAsync(context.CancellationToken);
        Assert.IsEmpty(result.Prepared.Files);
        Assert.AreEqual(ReleaseCheckStatus.Cancelled,
            result.Prepared.Checks.Single(check => check.Id == "build").Status);
        Assert.IsTrue(result.Prepared.Checks.Skip(2).All(check =>
            check.Status == ReleaseCheckStatus.Skipped));
        Assert.IsFalse(result.Commands.Any(command => command.Command.Arguments.Contains("pack")));
    }

    [TestMethod]
    public async Task WrongNativeNodeMajorBlocksPreparationBeforeProductCode()
    {
        using var fixture = await Fixture.CreateAsync(context.CancellationToken);
        fixture.NodeVersion = "22.21.0";
        NpmPreparationResult result = await fixture.RunAsync(context.CancellationToken);
        Assert.IsEmpty(result.Prepared.Files);
        Assert.AreEqual(ReleaseCheckStatus.Failed, result.Prepared.Checks[0].Status);
        Assert.IsTrue(result.Prepared.Checks.Skip(1).All(check =>
            check.Status == ReleaseCheckStatus.Skipped));
        Assert.IsFalse(result.Commands.Any(command => command.Check == "build"));
    }

    [TestMethod]
    [DataRow("Cancelled", "Cancelled", "native-input")]
    [DataRow("TimedOut", "TimedOut", "native-input")]
    [DataRow("Cancelled", "Cancelled", "build")]
    [DataRow("TimedOut", "TimedOut", "build")]
    public async Task NativeTerminationRemainsVisibleInRequiredQualification(
        string native, string status, string checkId)
    {
        using var fixture = await Fixture.CreateAsync(context.CancellationToken);
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
        internal string NodeVersion { get; set; } = "24.21.0";
        internal NativeTermination? Termination { get; set; }
        internal string TerminationCheck { get; set; } = "build";
        private string? archive;

        internal static async Task<Fixture> CreateAsync(CancellationToken token)
        {
            GitFixture repository = await GitFixture.CreateAsync(token);
            try
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
                foreach (string path in new[] { "mise.toml", "mise.lock", "pnpm-lock.yaml",
                    "pnpm-workspace.yaml" })
                    await repository.SetAsync(path, "committed native fixture");
                string commit = await repository.CommitAsync();
                await repository.GitAsync("reset", "--hard", commit);
                var subject = new ReleaseSubject(commit, "refs/heads/development",
                    "hcoona-release-smoke-npm", "product/workflow-delivery.release-unit.yml",
                    "official", "npmjs", true);
                return new(repository, new(subject, repository.Directory,
                    Directory.CreateTempSubdirectory("workflow-release-").FullName));
            }
            catch { repository.Dispose(); throw; }
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
                output = JsonSerializer.Serialize(new[] { new
                {
                    version = tool == "node" ? NodeVersion : "12.8.2",
                    requested_version = tool == "node" ? "24" : "12.8.2",
                    active = true,
                    source = new { type = "mise.toml", path = Path.Combine(repository.Directory,
                        "mise.toml") }
                } });
            }
            else if (args.Contains("--version"))
                output = command.Executable == "node" ? "v" + NodeVersion : "12.8.2";
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

        public void Dispose()
        {
            repository.Dispose();
            Directory.Delete(Scratch, recursive: true);
        }
    }
}
