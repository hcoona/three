using System.Security.Cryptography;
using System.Text.Json.Nodes;
using WorkflowDelivery.CI;
using WorkflowDelivery.Platform;
using GitFixture = WorkflowDelivery.Tests.Repository.GitReaderTests.GitFixture;

namespace WorkflowDelivery.Tests.CI;

[TestClass]
public sealed class NodeExecutionTests(TestContext context)
{
    private static readonly string[] Scripts = ["build", "test"];
    private static readonly string[] PackageChecks =
        [NodeExecutionFixture.Pack, NodeExecutionFixture.Contents, NodeExecutionFixture.Consumer];
    private static GitFixture? shared;
    private static string? sharedCandidate;

    [ClassInitialize]
    public static async Task InitializeAsync(TestContext classContext)
    {
        GitFixture repo = await GitFixture.CreateAsync(classContext.CancellationToken);
        try
        {
            sharedCandidate = await NodeExecutionFixture.CommitAsync(repo);
            shared = repo;
        }
        catch { repo.Dispose(); throw; }
    }

    [ClassCleanup]
    public static void Cleanup() => shared?.Dispose();

    private static NodeExecutionFixture Shared(params string[] builds) =>
        NodeExecutionFixture.Shared(shared!, sharedCandidate!, builds);

    [TestMethod]
    [DataRow("node22", "22.20.0", 0)]
    [DataRow("node24", "24.21.0", 1)]
    public async Task SelectedRuntimeRunsProjectScriptsAndOnlyItsPackageObligations(string runtime,
        string version, int outputCount)
    {
        using var fixture = Shared();
        fixture.Runtime = runtime;
        fixture.NodeVersion = version;

        NodeRunResult result = await fixture.RunAsync(context.CancellationToken);

        Assert.AreEqual(runtime, result.Runtime);
        Assert.AreEqual(fixture.Plan.Candidate, result.Candidate);
        CollectionAssert.AreEquivalent(fixture.SelectedPlan.Checks.Select(item => item.Work.Key)
            .ToArray(), result.Results.Select(item => item.Key).ToArray());
        Assert.IsTrue(ResultCollector.Collect(fixture.SelectedPlan, result.Results).Satisfied);
        Assert.HasCount(outputCount, result.Outputs);
        Assert.IsNotNull(result.RuntimeVersions);
        Assert.AreEqual(version, result.RuntimeVersions.NodeVersion);
        Assert.AreEqual("12.8.2", result.RuntimeVersions.PnpmVersion);
        NativeCommand[] scripts = fixture.Commands.Where(command =>
            command.Arguments.Contains("run")).ToArray();
        CollectionAssert.AreEquivalent(Scripts,
            scripts.Select(command => command.Arguments[^1]).ToArray());
        foreach (NativeCommand script in scripts)
        {
            Assert.AreEqual("pnpm", script.Executable);
            Assert.AreEqual(Path.Combine(fixture.Repository.Directory, NodeExecutionFixture.Source),
                Value(script, "--dir"));
        }
    }

    [TestMethod]
    [DataRow("24.22.0", "12.8.2")]
    [DataRow("24.21.0", "12.8.3")]
    public async Task RuntimeOrPackageManagerMismatchStopsBeforeProductWork(string node,
        string pnpm)
    {
        using var fixture = Shared();
        fixture.NodeVersion = node;
        fixture.PnpmVersion = pnpm;

        InvalidDataException failure = await Assert.ThrowsExactlyAsync<InvalidDataException>(() =>
            fixture.RunAsync(context.CancellationToken));

        Assert.Contains("differs from the selected Node context", failure.Message);
        Assert.IsTrue(fixture.Commands.All(command => command.Arguments.Contains("--version")));
        Assert.IsEmpty(Directory.EnumerateFileSystemEntries(fixture.Scratch));
    }

    [TestMethod]
    [DataRow("24.21.0", 0, "Malformed native Node version.")]
    [DataRow("v24.21.0", 7, "Unavailable native runtime version: node")]
    public async Task RejectedVersionProbeStopsBeforeProductWork(string stdout, int exitCode,
        string diagnostic)
    {
        using var fixture = Shared();
        fixture.Override = command => command.Executable == "node" &&
            command.Arguments is ["--version"]
                ? new(NativeTermination.Exited, exitCode, stdout, "native probe stderr", 0.1, null)
                : null;

        InvalidDataException failure = await Assert.ThrowsExactlyAsync<InvalidDataException>(() =>
            fixture.RunAsync(context.CancellationToken));

        Assert.AreEqual(diagnostic, failure.Message);
        Assert.HasCount(2, fixture.Commands);
        Assert.IsTrue(fixture.Commands.All(command => command.Arguments is ["--version"]));
        Assert.IsEmpty(Directory.EnumerateFileSystemEntries(fixture.Scratch));
    }

    [TestMethod]
    public async Task OriginalTarballQualifiesArchiveAndCleanPublicApiConsumer()
    {
        using var fixture = Shared();
        fixture.Override = command =>
        {
            if (command.Arguments.Contains("install"))
            {
                Assert.IsEmpty(Directory.EnumerateFileSystemEntries(command.Directory));
                Assert.IsEmpty(Directory.EnumerateFileSystemEntries(Value(command, "--store-dir")));
                Assert.IsEmpty(Directory.EnumerateFileSystemEntries(Value(command, "--state-dir")));
                Assert.IsEmpty(File.ReadAllText(Value(command, "--npmrc-auth-file")));
            }
            return null;
        };

        NodeRunResult result = await fixture.RunAsync(context.CancellationToken);

        Assert.IsTrue(ResultCollector.Collect(fixture.SelectedPlan, result.Results).Satisfied);
        Assert.AreEqual(CheckStatus.Passed, Status(result, NodeExecutionFixture.Contents));
        Assert.AreEqual(CheckStatus.Passed, Status(result, NodeExecutionFixture.Consumer));
        NodeOriginalOutput original = Assert.ContainsSingle(result.Outputs);
        Assert.AreEqual("main-package", original.Output);
        Assert.AreEqual(new FileInfo(original.Path).Length, original.Size);
        using var stream = File.OpenRead(original.Path);
        Assert.AreEqual(Convert.ToHexStringLower(await SHA256.HashDataAsync(stream,
            context.CancellationToken)), original.Sha256);
        NativeCommand pack = Assert.ContainsSingle(result.Commands.Where(item =>
            item.Key.Check == NodeExecutionFixture.Pack)).Command;
        Assert.AreEqual(original.Path, Value(pack, "--out"));
        NativeCommand install = Assert.ContainsSingle(fixture.Commands.Where(command =>
            command.Arguments.Contains("install")));
        Assert.AreEqual(original.Path, Value(install, "install"));
        Assert.Contains("--offline", install.Arguments);
        Assert.Contains("--ignore-scripts", install.Arguments);
        Assert.Contains("--ignore-workspace", install.Arguments);
        Assert.StartsWith(fixture.Scratch + Path.DirectorySeparatorChar, install.Directory);
        NativeCommand import = Assert.ContainsSingle(fixture.Commands.Where(IsPublicApiCheck));
        Assert.AreEqual(install.Directory, import.Directory);
        Assert.Contains("import { smokeMessage } from '@hcoona/hcoona-release-smoke-npm'",
            Value(import, "--eval"));
    }

    [TestMethod]
    public async Task EachPackRetainsItsOwnTarballForTheOriginalSelectedBuild()
    {
        using var fixture = Shared("first", "second");
        CiPlan plan = fixture.Plan with { Checks = fixture.Plan.Checks.Reverse().ToArray() };

        NodeRunResult result = await fixture.RunAsync(context.CancellationToken, plan);

        Assert.IsTrue(ResultCollector.Collect(fixture.SelectedPlan, result.Results).Satisfied);
        Assert.HasCount(2, result.Outputs);
        foreach (NodeOriginalOutput output in result.Outputs)
        {
            Assert.AreEqual(output.Build + "-package", output.Output);
            Assert.AreEqual(output.Path, Value(Assert.ContainsSingle(result.Commands.Where(
                item => item.Key.Target == "release/product/" + output.Build &&
                    item.Key.Check == NodeExecutionFixture.Pack)).Command, "--out"));
            Assert.AreEqual(CheckStatus.Passed,
                Status(result, NodeExecutionFixture.Consumer, output.Build));
        }
        Assert.AreNotEqual(result.Outputs[0].Path, result.Outputs[1].Path);
    }

    [TestMethod]
    [DataRow(nameof(NativeTermination.Exited), nameof(CheckStatus.Failed))]
    [DataRow(nameof(NativeTermination.TimedOut), nameof(CheckStatus.TimedOut))]
    public async Task UnsuccessfulBuildSkipsPackagingButRunsIndependentTest(
        string nativeTermination, string expectedStatus)
    {
        using var fixture = Shared();
        NativeTermination termination = Enum.Parse<NativeTermination>(nativeTermination);
        fixture.Override = command => command.Arguments[^1] == "build"
            ? NodeExecutionFixture.Failure(termination) : null;

        NodeRunResult result = await fixture.RunAsync(context.CancellationToken);

        Assert.AreEqual(Enum.Parse<CheckStatus>(expectedStatus),
            Status(result, NodeExecutionFixture.Build));
        Assert.AreEqual(CheckStatus.Passed, Status(result, NodeExecutionFixture.Test));
        foreach (string check in PackageChecks)
            Assert.AreEqual(CheckStatus.Skipped, Status(result, check));
        Assert.IsFalse(fixture.Commands.Any(command => command.Arguments.Contains("pack")));
        Assert.IsEmpty(result.Outputs);
        Assert.IsFalse(ResultCollector.Collect(fixture.SelectedPlan, result.Results).Satisfied);
        NodeCommandObservation build = Assert.ContainsSingle(result.Commands.Where(item =>
            item.Key.Check == NodeExecutionFixture.Build));
        Assert.AreEqual(termination, build.Result.Termination);
        Assert.AreEqual("partial stdout", build.Result.Stdout);
    }

    [TestMethod]
    public async Task FailedPackCannotQualifyLeftoverTarballOrEraseIndependentOutput()
    {
        using var fixture = Shared("first", "second");
        string? leftover = null;
        fixture.Override = command =>
        {
            if (!command.Arguments.Contains("pack") || leftover is not null) return null;
            string path = Value(command, "--out");
            leftover = path;
            PackageArchiveFixture.Write(path, PackageArchiveFixture.Required());
            return NodeExecutionFixture.Failure();
        };

        NodeRunResult result = await fixture.RunAsync(context.CancellationToken);

        Assert.AreEqual(CheckStatus.Failed, Status(result, NodeExecutionFixture.Pack, "first"));
        Assert.AreEqual(CheckStatus.Skipped,
            Status(result, NodeExecutionFixture.Contents, "first"));
        Assert.AreEqual(CheckStatus.Skipped,
            Status(result, NodeExecutionFixture.Consumer, "first"));
        Assert.AreEqual(CheckStatus.Passed,
            Status(result, NodeExecutionFixture.Consumer, "second"));
        Assert.AreEqual("second", Assert.ContainsSingle(result.Outputs).Build);
        Assert.IsTrue(File.Exists(leftover));
        Assert.IsFalse(ResultCollector.Collect(fixture.SelectedPlan, result.Results).Satisfied);
    }

    [TestMethod]
    public async Task SuccessfulPackCannotQualifyAbsentTarball()
    {
        using var fixture = Shared();
        fixture.Override = command => command.Arguments.Contains("pack")
            ? NodeExecutionFixture.Success() : null;

        NodeRunResult result = await fixture.RunAsync(context.CancellationToken);

        Assert.AreEqual(CheckStatus.Failed, Status(result, NodeExecutionFixture.Pack));
        Assert.AreEqual(CheckStatus.Skipped, Status(result, NodeExecutionFixture.Consumer));
        Assert.IsEmpty(result.Outputs);
    }

    [TestMethod]
    public async Task UnsafeNativeMetadataCannotReachConsumerInstall()
    {
        using var fixture = Shared();
        JsonObject metadata = JsonNode.Parse(NodeExecutionFixture.Metadata)!.AsObject();
        metadata["scripts"] = JsonNode.Parse("""{"install":"native operation"}""");
        fixture.NativeMetadata = metadata.ToJsonString();

        NodeRunResult result = await fixture.RunAsync(context.CancellationToken);

        Assert.AreEqual(CheckStatus.Passed, Status(result, NodeExecutionFixture.Pack));
        Assert.AreEqual(CheckStatus.Failed, Status(result, NodeExecutionFixture.Contents));
        Assert.AreEqual(CheckStatus.Failed, Status(result, NodeExecutionFixture.Consumer));
        Assert.IsFalse(fixture.Commands.Any(command => command.Arguments.Contains("install")));
    }

    [TestMethod]
    public async Task ContentsFailureDoesNotEraseIndependentConsumerResult()
    {
        using var fixture = Shared();
        fixture.Override = command => command.Arguments.Contains("pkg") &&
            Path.GetFileName(command.Directory) == "contents-metadata"
            ? NodeExecutionFixture.Failure() : null;

        NodeRunResult result = await fixture.RunAsync(context.CancellationToken);

        Assert.AreEqual(CheckStatus.Failed, Status(result, NodeExecutionFixture.Contents));
        Assert.AreEqual(CheckStatus.Passed, Status(result, NodeExecutionFixture.Consumer));
        Assert.IsFalse(ResultCollector.Collect(fixture.SelectedPlan, result.Results).Satisfied);
    }

    [TestMethod]
    public async Task FailedConsumerInstallDoesNotRunPublicApiCheck()
    {
        using var fixture = Shared();
        fixture.Override = command => command.Arguments.Contains("install")
            ? NodeExecutionFixture.Failure() : null;

        NodeRunResult result = await fixture.RunAsync(context.CancellationToken);

        Assert.AreEqual(CheckStatus.Passed, Status(result, NodeExecutionFixture.Contents));
        Assert.AreEqual(CheckStatus.Failed, Status(result, NodeExecutionFixture.Consumer));
        Assert.IsFalse(fixture.Commands.Any(IsPublicApiCheck));
        Assert.IsFalse(ResultCollector.Collect(fixture.SelectedPlan, result.Results).Satisfied);
    }

    [TestMethod]
    public async Task FailedPublicApiImportRetainsPassingContentsAndNativeStderr()
    {
        using var fixture = Shared();
        fixture.Override = command => IsPublicApiCheck(command)
            ? NodeExecutionFixture.Failure() : null;

        NodeRunResult result = await fixture.RunAsync(context.CancellationToken);

        Assert.AreEqual(CheckStatus.Passed, Status(result, NodeExecutionFixture.Contents));
        Assert.AreEqual(CheckStatus.Failed, Status(result, NodeExecutionFixture.Consumer));
        Assert.IsFalse(ResultCollector.Collect(fixture.SelectedPlan, result.Results).Satisfied);
        Assert.ContainsSingle(result.Outputs);
        var import = Assert.ContainsSingle(result.Commands.Where(item =>
            IsPublicApiCheck(item.Command)));
        Assert.AreEqual("visible failure", import.Result.Stderr);
    }

    [TestMethod]
    public async Task NonemptyExternalScratchIsRejectedWithoutReplacingSentinel()
    {
        using var fixture = Shared();
        string sentinel = Path.Combine(fixture.Scratch, "existing.txt");
        File.WriteAllText(sentinel, "existing scratch data");

        InvalidDataException failure = await Assert.ThrowsExactlyAsync<InvalidDataException>(() =>
            fixture.RunAsync(context.CancellationToken));

        Assert.Contains("fresh external scratch", failure.Message);
        Assert.IsEmpty(fixture.Commands);
        Assert.AreEqual("existing scratch data", File.ReadAllText(sentinel));
    }

    [TestMethod]
    public async Task ChangedCheckoutIsRejectedBeforeNativeCommands()
    {
        using var fixture = await NodeExecutionFixture.CreateAsync(context.CancellationToken);
        string source = Path.Combine(fixture.Request.Checkout, NodeExecutionFixture.Source,
            "index.js");
        await File.WriteAllTextAsync(source, "uncommitted change", context.CancellationToken);

        await Assert.ThrowsAsync<InvalidDataException>(() =>
            fixture.RunAsync(context.CancellationToken));

        Assert.IsEmpty(fixture.Commands);
        Assert.AreEqual("uncommitted change", File.ReadAllText(source));
    }

    [TestMethod]
    public async Task CleanCheckoutAtDifferentCommitCannotQualifyCandidate()
    {
        using var fixture = await NodeExecutionFixture.CreateAsync(context.CancellationToken);
        await fixture.Repository.SetAsync("next.txt");
        string next = await fixture.Repository.CommitAsync(fixture.Plan.Candidate);
        await fixture.Repository.GitAsync("reset", "--hard", next);

        await Assert.ThrowsAsync<InvalidDataException>(() =>
            fixture.RunAsync(context.CancellationToken));

        Assert.IsEmpty(fixture.Commands);
        Assert.IsEmpty(Directory.EnumerateFileSystemEntries(fixture.Scratch));
    }

    [TestMethod]
    public async Task NativeBuildMayModifyCheckoutAfterBindingCandidate()
    {
        using var fixture = await NodeExecutionFixture.CreateAsync(context.CancellationToken);
        string source = Path.Combine(fixture.Request.Checkout, NodeExecutionFixture.Source,
            "index.js");
        fixture.Override = command =>
        {
            if (command.Arguments[^1] == "build")
                File.WriteAllText(source, "native build output");
            return null;
        };

        NodeRunResult result = await fixture.RunAsync(context.CancellationToken);

        Assert.IsTrue(ResultCollector.Collect(fixture.SelectedPlan, result.Results).Satisfied);
        Assert.AreEqual("native build output", File.ReadAllText(source));
    }

    [TestMethod]
    public async Task CallerCancellationRetainsEverySelectedKeyWithoutRunningDependents()
    {
        using var fixture = Shared();
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(
            context.CancellationToken);
        fixture.Override = command =>
        {
            if (command.Arguments.Contains("--version")) return null;
            cancellation.Cancel();
            return NodeExecutionFixture.Failure(NativeTermination.Cancelled, null);
        };

        NodeRunResult result = await fixture.RunAsync(cancellation.Token);

        CollectionAssert.AreEquivalent(fixture.SelectedPlan.Checks.Select(item => item.Work.Key)
            .ToArray(), result.Results.Select(item => item.Key).ToArray());
        Assert.IsTrue(result.Results.All(item => item.Status == CheckStatus.Cancelled));
        Assert.IsEmpty(result.Outputs);
        Assert.IsFalse(fixture.Commands.Any(command => command.Arguments.Contains("pack")));
        Assert.IsFalse(ResultCollector.Collect(fixture.SelectedPlan, result.Results).Satisfied);
    }

    [TestMethod]
    public async Task InvalidPlanStopsBeforeNativeEffects()
    {
        using var fixture = Shared();
        CiPlan plan = fixture.Plan;
        PlannedCheck last = plan.Checks[^1];
        CheckSpec package = last.Work;
        await RejectAsync(plan.Checks[..^1], "Incomplete or conflicting Node package checks");
        await RejectAsync(ReplacePackage(package with
        {
            Dimensions = new(package.Dimensions) { ["pnpm"] = "12.8.3" },
        }), "Conflicting planned PNPM versions");
        await RejectAsync(ReplacePackage(package with
        {
            Key = package.Key with { Variant = "node22" },
            Dimensions = new() { ["node"] = "22.x", ["pnpm"] = "12.8.2" },
        }), "Incomplete or conflicting Node package checks");

        Assert.IsEmpty(fixture.Commands);
        Assert.IsEmpty(Directory.EnumerateFileSystemEntries(fixture.Scratch));

        PlannedCheck[] ReplacePackage(CheckSpec work) =>
            [.. plan.Checks[..^1], last with { Work = work }];

        async Task RejectAsync(PlannedCheck[] checks, string diagnostic) =>
            Assert.Contains(diagnostic, (await Assert.ThrowsExactlyAsync<InvalidDataException>(
                () => fixture.RunAsync(context.CancellationToken, plan with { Checks = checks })))
                .Message);
    }

    [TestMethod]
    public async Task EmptyPlanStillRequiresExactCandidateButDoesNotRunProductWork()
    {
        using var fixture = Shared();
        CiPlan empty = fixture.Plan with { Checks = [] };

        NodeRunResult result = await fixture.RunAsync(context.CancellationToken, empty);
        InvalidDataException failure = await Assert.ThrowsExactlyAsync<InvalidDataException>(() =>
            fixture.RunAsync(context.CancellationToken, empty with { Candidate = "HEAD" }));

        Assert.AreEqual(fixture.Plan.Candidate, result.Candidate);
        Assert.IsEmpty(result.Results);
        Assert.Contains("exact native commit identity", failure.Message);
        Assert.IsEmpty(fixture.Commands);
        Assert.IsEmpty(Directory.EnumerateFileSystemEntries(fixture.Scratch));
    }

    [TestMethod]
    [DataRow("%s")]
    [DataRow("%v")]
    public async Task NativePackOutputMarkersAreRejectedInExternalScratchPath(string marker)
    {
        using var fixture = Shared();
        string scratch = Directory.CreateDirectory(Path.Combine(fixture.Scratch, marker, "fresh"))
            .FullName;

        InvalidDataException failure = await Assert.ThrowsExactlyAsync<InvalidDataException>(() =>
            fixture.RunAsync(context.CancellationToken,
                request: fixture.Request with { Scratch = scratch }));

        Assert.Contains("native pack output markers", failure.Message);
        Assert.IsEmpty(fixture.Commands);
        Assert.IsEmpty(Directory.EnumerateFileSystemEntries(scratch));
    }

    private static bool IsPublicApiCheck(NativeCommand command) =>
        command.Executable == "node" && command.Arguments.Contains("--eval");

    private static CheckStatus Status(NodeRunResult result, string operation,
        string? build = null) => result.Results.Single(item => item.Key.Check == operation &&
            (build is null || item.Key.Target == "release/product/" + build)).Status;

    private static string Value(NativeCommand command, string key) =>
        command.Arguments[Array.IndexOf(command.Arguments, key) + 1];
}
