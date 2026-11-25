using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Nodes;
using WorkflowDelivery.CI;
using WorkflowDelivery.Ecosystems;
using WorkflowDelivery.Platform;
using WorkflowDelivery.Tests.Repository;
using GitFixture = WorkflowDelivery.Tests.Repository.GitReaderTests.GitFixture;

namespace WorkflowDelivery.Tests.CI;

[TestClass]
public sealed class NodeExecutionTests(TestContext context)
{
    private static readonly string[] VersionArguments = ["--version"];

    // Rows that only vary native answers, the request or scratch read one class-owned
    // committed checkout. Rows that edit, tag or fetch into the checkout keep their own.
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

    [TestMethod]
    [DataRow("missing-consumer", "Incomplete or conflicting Node package checks")]
    [DataRow("conflicting-pnpm", "Conflicting planned PNPM versions")]
    [DataRow("node22-package", "Incomplete or conflicting Node package checks")]
    public async Task RunRejectsCompletePlanBeforeAnyCommand(string defect, string diagnostic)
    {
        using var checkout = new ScratchFixture("checkout");
        using var scratch = new ScratchFixture("scratch");
        CiPlan original = NodeExecutionFixture.CreatePlan(new string('a', 40), "first", "second");
        PlannedCheck[] checks = original.Checks.ToArray();
        if (defect == "missing-consumer")
            checks = checks[..^1];
        else
        {
            PlannedCheck last = checks[^1];
            CheckSpec work = last.Work;
            checks[^1] = last with
            {
                Work = defect == "conflicting-pnpm" ? work with
                {
                    Dimensions = new(work.Dimensions) { ["pnpm"] = "12.8.3" },
                } : work with
                {
                    Key = work.Key with { Variant = "node22" },
                    Dimensions = new() { ["node"] = "22.x", ["pnpm"] = "12.8.2" },
                },
            };
        }

        InvalidDataException error = await Assert.ThrowsExactlyAsync<InvalidDataException>(() =>
            NodeExecution.RunAsync(original with { Checks = checks },
                new(checkout.Scratch, scratch.Scratch, "node24"),
                (_, _) => throw new AssertFailedException("Invalid plan reached native work."),
                context.CancellationToken));

        Assert.Contains(diagnostic, error.Message);
        Assert.IsEmpty(Directory.EnumerateFileSystemEntries(checkout.Scratch));
        Assert.IsEmpty(Directory.EnumerateFileSystemEntries(scratch.Scratch));
    }

    [TestMethod]
    [DataRow("node22", "22.20.0", 0)]
    [DataRow("node24", "24.21.0", 1)]
    public async Task RunSelectedRuntimeRetainsExactKeysAndReadback(string runtime,
        string nodeVersion, int packageCount)
    {
        // Defect-free control for the shared checkout.
        using var fixture = NodeExecutionFixture.Shared(shared!, sharedCandidate!);
        fixture.Runtime = runtime;
        fixture.NodeVersion = nodeVersion;

        NodeRunResult result = await fixture.RunAsync(context.CancellationToken);

        Assert.AreEqual(runtime, result.Runtime);
        Assert.AreEqual(fixture.Plan.Candidate, result.Candidate);
        CollectionAssert.AreEqual(fixture.SelectedPlan.Checks.Select(item => item.Work.Key)
            .ToArray(), result.Results.Select(item => item.Key).ToArray());
        Assert.IsTrue(result.Results.All(item => item.Status == CheckStatus.Passed));
        Assert.HasCount(packageCount, result.Outputs);
        Assert.IsNotNull(result.RuntimeVersions);
        Assert.AreEqual(nodeVersion, result.RuntimeVersions.NodeVersion);
        Assert.AreEqual("12.8.2", result.RuntimeVersions.PnpmVersion);
        Assert.AreEqual("v" + nodeVersion + "\n", result.RuntimeVersions.Node.Stdout);
        Assert.AreEqual("12.8.2\n", result.RuntimeVersions.Pnpm.Stdout);
        Assert.IsTrue(result.RuntimeVersions.Node.Succeeded);
        Assert.IsTrue(result.RuntimeVersions.Pnpm.Succeeded);
        string[] tools = ["node", "pnpm"];
        CollectionAssert.AreEqual(tools,
            fixture.Commands.Take(2).Select(item => item.Executable).ToArray());
        foreach (NativeCommand command in fixture.Commands.Take(2))
        {
            CollectionAssert.AreEqual(VersionArguments, command.Arguments);
            Assert.AreEqual(fixture.Repository.Directory, command.Directory);
            Assert.AreEqual(30, command.DeadlineSeconds);
        }
        Assert.IsTrue(ResultCollector.Collect(fixture.SelectedPlan, result.Results).Satisfied);
    }

    [TestMethod]
    [DataRow("node24", "node", "v24.22.0", "Exited", Differs)]
    [DataRow("node22", "node", "v24.21.0", "Exited", Differs)]
    [DataRow("node24", "node", "24.21.0", "Exited", "Malformed native Node version.")]
    [DataRow("node24", "node", "v24.21", "Exited", Unavailable + "node")]
    [DataRow("node24", "node", "v24.21.0\nv24.21.0", "Exited", Unavailable + "node")]
    [DataRow("node24", "pnpm", "12.8.3", "Exited", Differs)]
    [DataRow("node24", "pnpm", "invalid", "Exited", Unavailable + "pnpm")]
    [DataRow("node24", "node", "v24.21.0", "FailedExit", Unavailable + "node")]
    [DataRow("node24", "node", "v24.21.0", "StartFailed", Unavailable + "node")]
    [DataRow("node24", "pnpm", "12.8.2", "TimedOut", Unavailable + "pnpm")]
    [DataRow("node24", "node", "v24.21.0", "Cancelled", Unavailable + "node")]
    [DataRow("node24", "node", "v24.21.0", "InvalidOutput", Unavailable + "node")]
    [DataRow("node24", "pnpm", "12.8.2", "CleanupFailed", Unavailable + "pnpm")]
    public void UnsupportedRuntimeReadbackIsRejected(string runtime, string tool,
        string output, string termination, string message)
    {
        CiPlan selected = NodeExecution.SelectRuntime(
            NodeExecutionFixture.CreatePlan(NodeScenario.Candidate), runtime);
        NativeCommandResult answer = RuntimeAnswer(output, termination);
        NativeCommandResult node = tool == "node" ? answer :
            NodeExecutionFixture.Success("v24.21.0\n");
        NativeCommandResult pnpm = tool == "pnpm" ? answer :
            NodeExecutionFixture.Success("12.8.2\n");

        Assert.AreEqual(message, Assert.ThrowsExactly<InvalidDataException>(() =>
            NodeExecution.ReadRuntime(selected, node, pnpm)).Message);
    }

    [TestMethod]
    [DataRow("node22", "v22.20.0\n", "22.20.0")]
    [DataRow("node24", "v24.21.0\n", "24.21.0")]
    public void SupportedRuntimeReadbackIsRetained(string runtime, string output,
        string version)
    {
        CiPlan selected = NodeExecution.SelectRuntime(
            NodeExecutionFixture.CreatePlan(NodeScenario.Candidate), runtime);
        NativeCommandResult node = NodeExecutionFixture.Success(output);
        NativeCommandResult pnpm = NodeExecutionFixture.Success("12.8.2\n");

        Assert.AreEqual(new NodeRuntimeReadback(version, "12.8.2", node, pnpm),
            NodeExecution.ReadRuntime(selected, node, pnpm));
    }

    private const string Differs = "Native runtime differs from the selected Node context.";
    private const string Unavailable = "Unavailable native runtime version: ";

    private static NativeCommandResult RuntimeAnswer(string output, string termination) =>
        new(termination == "FailedExit" ? NativeTermination.Exited :
            Enum.Parse<NativeTermination>(termination), termination == "FailedExit" ? 7 : 0,
            output, "", 0.1, null);

    [TestMethod]
    public async Task RunRejectsRuntimeBeforeProductWork()
    {
        using var fixture = NodeExecutionFixture.Shared(shared!, sharedCandidate!);
        fixture.Override = command => command.Executable == "node" &&
            command.Arguments.Contains("--version")
            ? RuntimeAnswer("v24.22.0", "Exited") : null;

        InvalidDataException error = await Assert.ThrowsExactlyAsync<InvalidDataException>(() =>
            fixture.RunAsync(context.CancellationToken));

        Assert.AreEqual(Differs, error.Message);
        Assert.IsTrue(fixture.Commands.All(item => item.Arguments.SequenceEqual(VersionArguments)));
        string[] tools = ["node", "pnpm"];
        CollectionAssert.AreEqual(tools, fixture.Commands.Select(item => item.Executable)
            .ToArray());
        Assert.IsEmpty(Directory.EnumerateFileSystemEntries(fixture.Scratch));
    }

    [TestMethod]
    [DataRow("dirty")]
    [DataRow("wrong-head")]
    public async Task RunRejectsUnboundCheckout(string defect)
    {
        using var fixture = await NodeExecutionFixture.CreateAsync(context.CancellationToken);
        if (defect == "dirty")
            await File.WriteAllTextAsync(Path.Combine(fixture.Request.Checkout,
                NodeExecutionFixture.Source, "index.js"), "changed", context.CancellationToken);
        if (defect == "wrong-head")
        {
            await fixture.Repository.SetAsync("new.txt");
            string next = await fixture.Repository.CommitAsync(fixture.Plan.Candidate);
            await fixture.Repository.GitAsync("reset", "--hard", next);
        }

        await Assert.ThrowsAsync<InvalidDataException>(() => fixture.RunAsync(
            context.CancellationToken));

        Assert.IsEmpty(fixture.Commands);
        Assert.IsEmpty(Directory.EnumerateFileSystemEntries(fixture.Scratch));
    }

    [TestMethod]
    public async Task RunRejectsNestedScratchBeforeReadingGit()
    {
        using var checkout = new ScratchFixture("checkout");
        CiPlan plan = NodeExecutionFixture.CreatePlan(new string('a', 40));
        string scratch = Directory.CreateDirectory(Path.Combine(checkout.Scratch, "scratch"))
            .FullName;
        var request = new NodeRunRequest(checkout.Scratch, scratch, "node24");

        InvalidDataException error = await Assert.ThrowsExactlyAsync<InvalidDataException>(() =>
            NodeExecution.RunAsync(plan, request,
                (_, _) => throw new AssertFailedException("Nested scratch reached native work."),
                context.CancellationToken));

        Assert.AreEqual("Node execution requires fresh external scratch.", error.Message);
        Assert.IsEmpty(Directory.EnumerateFileSystemEntries(scratch));
    }

    [TestMethod]
    public async Task RunRejectsNonemptyExternalScratchWithoutTouchingSentinel()
    {
        using var fixture = NodeExecutionFixture.Shared(shared!, sharedCandidate!);
        string sentinel = Path.Combine(fixture.Scratch, "existing.txt");
        File.WriteAllText(sentinel, "existing scratch data");

        InvalidDataException error = await Assert.ThrowsExactlyAsync<InvalidDataException>(() =>
            fixture.RunAsync(context.CancellationToken));

        Assert.AreEqual("Node execution requires fresh external scratch.", error.Message);
        Assert.IsEmpty(fixture.Commands);
        Assert.AreEqual("existing scratch data", File.ReadAllText(sentinel));
    }

    [TestMethod]
    public async Task RunRejectsCheckoutFilesystemRootEvenForEmptyPlan()
    {
        using var fixture = NodeExecutionFixture.Shared(shared!, sharedCandidate!);
        string root = Path.GetPathRoot(fixture.Request.Checkout)!;
        Assert.IsTrue(Path.IsPathFullyQualified(root));
        Assert.IsTrue(Directory.Exists(root));
        Assert.AreEqual(root, Path.GetPathRoot(fixture.Scratch));
        Assert.IsEmpty(Directory.EnumerateFileSystemEntries(fixture.Scratch));
        NodeRunRequest request = fixture.Request with { Checkout = root };
        CiPlan plan = fixture.Plan with { Checks = [] };

        InvalidDataException error = await Assert.ThrowsExactlyAsync<InvalidDataException>(() =>
            fixture.RunAsync(context.CancellationToken, plan, request));

        Assert.AreEqual("Node execution requires fresh external scratch.", error.Message);
        Assert.IsEmpty(fixture.Commands);
        Assert.IsEmpty(Directory.EnumerateFileSystemEntries(fixture.Scratch));
    }

    [TestMethod]
    public async Task RunRejectsMakingRequiredWorkAdvisoryBeforeProductCommands()
    {
        using var fixture = NodeExecutionFixture.Shared(shared!, sharedCandidate!);
        CiPlan advisory = fixture.Plan with
        {
            Checks = fixture.Plan.Checks.Select(item =>
                item with { Work = item.Work with { Required = false } }).ToArray(),
        };
        Assert.IsTrue(ResultCollector.Collect(advisory, []).Satisfied);

        InvalidDataException error = await Assert.ThrowsExactlyAsync<InvalidDataException>(() =>
            fixture.RunAsync(context.CancellationToken, advisory));

        Assert.Contains("Unsupported Node execution quality/runner/variant", error.Message);
        Assert.IsEmpty(fixture.Commands);
        Assert.IsEmpty(Directory.EnumerateFileSystemEntries(fixture.Scratch));
    }

    [TestMethod]
    public async Task RunAllowsBuildMutationAfterStartingBind()
    {
        using var fixture = await NodeExecutionFixture.CreateAsync(context.CancellationToken);
        fixture.Override = command =>
        {
            if (command.Arguments[^1] == "build")
                File.WriteAllText(Path.Combine(fixture.Repository.Directory,
                    NodeExecutionFixture.Source, "index.js"), "native build mutation");
            return null;
        };

        NodeRunResult result = await fixture.RunAsync(context.CancellationToken);

        Assert.IsTrue(ResultCollector.Collect(fixture.SelectedPlan, result.Results).Satisfied);
        Assert.HasCount(1, result.Outputs);
        Assert.Contains("native build mutation", await File.ReadAllTextAsync(Path.Combine(
            fixture.Repository.Directory, NodeExecutionFixture.Source, "index.js"),
            context.CancellationToken));
    }

    [TestMethod]
    public async Task RunRetainsEveryMultiBuildCheckAndOriginalOutput()
    {
        using var fixture = NodeExecutionFixture.Shared(shared!, sharedCandidate!,
            "first", "second");
        CiPlan plan = fixture.Plan with { Checks = fixture.Plan.Checks.Reverse().ToArray() };

        NodeRunResult result = await fixture.RunAsync(context.CancellationToken, plan);

        Assert.AreEqual(plan.Candidate, result.Candidate);
        CollectionAssert.AreEqual(plan.Checks
            .Where(item => item.Work.Key.Variant == fixture.Runtime)
            .Select(item => item.Work.Key).ToArray(),
            result.Results.Select(item => item.Key).ToArray());
        Assert.IsTrue(result.Results.All(item => item.Status == CheckStatus.Passed));
        Assert.IsTrue(result.Results.All(item => item.Candidate == plan.Candidate));
        Assert.IsTrue(ResultCollector.Collect(plan with
        {
            Checks = plan.Checks.Where(item =>
            item.Work.Key.Variant == fixture.Runtime).ToArray()
        }, result.Results).Satisfied);
        Assert.HasCount(2, result.Outputs);
        Assert.IsEmpty(result.Failures);
        foreach (NodeOriginalOutput output in result.Outputs)
        {
            Assert.AreEqual("product", output.Unit);
            Assert.AreEqual(output.Build + "-package", output.Output);
            Assert.AreEqual("primary-package", output.Role);
            Assert.AreEqual("npm-tarball", output.Kind);
            Assert.IsTrue(Path.IsPathFullyQualified(output.Path));
            Assert.AreEqual(new FileInfo(output.Path).Length, output.Size);
            using var original = File.OpenRead(output.Path);
            string digest = Convert.ToHexStringLower(await SHA256.HashDataAsync(original,
                context.CancellationToken));
            Assert.AreEqual(digest, output.Sha256);
            Assert.AreEqual(output.Path, result.Commands.Single(item =>
                item.Key.Target == "release/product/" + output.Build &&
                item.Key.Check == NodeExecutionFixture.Pack).Command.Arguments[^1]);
        }
        string[] expectedBuilds = ["first", "second"];
        CollectionAssert.AreEquivalent(expectedBuilds,
            result.Outputs.Select(item => item.Build).ToArray());
    }

    [TestMethod]
    [DataRow("node22", "22.20.0")]
    [DataRow("node24", "24.21.0")]
    public async Task RunFailedBuildStillExecutesIndependentSourceTest(string runtime,
        string nodeVersion)
    {
        using var fixture = NodeExecutionFixture.Shared(shared!, sharedCandidate!);
        fixture.Runtime = runtime;
        fixture.NodeVersion = nodeVersion;
        fixture.Override = command => command.Arguments[^1] == "build"
            ? NodeExecutionFixture.Failure() : null;

        NodeRunResult result = await fixture.RunAsync(context.CancellationToken);

        Assert.AreEqual(CheckStatus.Failed, Status(result, NodeExecutionFixture.Build));
        Assert.AreEqual(CheckStatus.Passed, Status(result, NodeExecutionFixture.Test));
        if (runtime == "node24")
            foreach (string check in new[] { NodeExecutionFixture.Pack,
                NodeExecutionFixture.Contents, NodeExecutionFixture.Consumer })
                Assert.AreEqual(CheckStatus.Skipped, Status(result, check));
        Assert.HasCount(runtime == "node24" ? 5 : 2, result.Results);
        Assert.IsEmpty(result.Outputs);
        Assert.IsFalse(ResultCollector.Collect(fixture.SelectedPlan, result.Results).Satisfied);
    }

    [TestMethod]
    public async Task RunFailedPackWithLeftoverSkipsOnlyItsArtifactChecks()
    {
        using var fixture = NodeExecutionFixture.Shared(shared!, sharedCandidate!,
            "first", "second");
        fixture.Override = command =>
        {
            if (!command.Arguments.Contains("pack") ||
                fixture.Commands.Count(item => item.Arguments.Contains("pack")) != 1)
                return null;
            PackageArchiveFixture.Write(command.Arguments[^1], PackageArchiveFixture.Required());
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
        Assert.IsTrue(File.Exists(result.Commands.First(item =>
            item.Key.Check == NodeExecutionFixture.Pack).Command.Arguments[^1]));
        Assert.IsFalse(ResultCollector.Collect(fixture.SelectedPlan, result.Results).Satisfied);
    }

    [TestMethod]
    [DataRow("missing")]
    [DataRow("directory")]
    [DataRow("empty")]
    public async Task RunRejectsMissingOrNonregularPackOutput(string defect)
    {
        using var fixture = NodeExecutionFixture.Shared(shared!, sharedCandidate!);
        fixture.WriteArchive = path =>
        {
            if (defect == "directory")
                Directory.CreateDirectory(path);
            if (defect == "empty")
                File.WriteAllBytes(path, []);
        };

        NodeRunResult result = await fixture.RunAsync(context.CancellationToken);

        Assert.AreEqual(CheckStatus.Failed, Status(result, NodeExecutionFixture.Pack));
        Assert.AreEqual(CheckStatus.Skipped, Status(result, NodeExecutionFixture.Consumer));
        Assert.AreEqual(NodeExecutionFixture.Pack,
            Assert.ContainsSingle(result.Failures).Key.Check);
        Assert.IsEmpty(result.Outputs);
        Assert.IsFalse(ResultCollector.Collect(fixture.SelectedPlan, result.Results).Satisfied);
    }

    [TestMethod]
    [DataRow("name", "other")]
    [DataRow("version", "1.2.4")]
    [DataRow("type", "commonjs")]
    [DataRow("main", "./other.js")]
    [DataRow("version", "missing")]
    [DataRow("name", "number")]
    [DataRow("json", "malformed")]
    [DataRow("json", "array")]
    [DataRow("json", "duplicate")]
    public void UnexpectedNativeMetadataIsRejected(string field, string value) =>
        AssertMetadataRejected(UnexpectedMetadata(field, value));

    [TestMethod]
    [DataRow("dependencies", "{\"runtime\":\"1\"}")]
    [DataRow("optionalDependencies", "[]")]
    [DataRow("peerDependencies", "{\"peer\":\"1\"}")]
    [DataRow("peerDependenciesMeta", "{\"peer\":{\"optional\":true}}")]
    [DataRow("bundledDependencies", "true")]
    [DataRow("bundleDependencies", "[\"runtime\"]")]
    [DataRow("devDependencies", "[]")]
    [DataRow("engines.runtime", "{\"name\":\"node\",\"version\":\"24\"}")]
    [DataRow("scripts", "{\"install\":\"native operation\"}")]
    [DataRow("gypfile", "true")]
    public void UnsupportedInstalledShapeIsRejected(string field, string json) =>
        AssertMetadataRejected(InstalledShape(field, json));

    [TestMethod]
    public async Task RunRejectsNativeMetadataBeforeInstall()
    {
        using var fixture = NodeExecutionFixture.Shared(shared!, sharedCandidate!);
        fixture.NativeMetadata = InstalledShape("scripts", "{\"install\":\"native operation\"}");

        NodeRunResult result = await fixture.RunAsync(context.CancellationToken);

        Assert.AreEqual(CheckStatus.Passed, Status(result, NodeExecutionFixture.Pack));
        Assert.AreEqual(CheckStatus.Failed, Status(result, NodeExecutionFixture.Contents));
        Assert.AreEqual(CheckStatus.Failed, Status(result, NodeExecutionFixture.Consumer));
        Assert.IsFalse(result.Commands.Any(item => item.Command.Arguments.Contains("install")));
    }

    [TestMethod]
    public async Task RunAllowsDevelopmentAndPackLifecycles()
    {
        using var fixture = NodeExecutionFixture.Shared(shared!, sharedCandidate!);
        JsonObject metadata = JsonNode.Parse(NodeExecutionFixture.Metadata)!.AsObject();
        metadata["devDependencies"] = JsonNode.Parse("{\"development\":\"workspace:*\"}");
        metadata["scripts"] = JsonNode.Parse(
            "{\"build\":\"opaque\",\"prepack\":\"native\",\"postpack\":\"native\"}");
        foreach (string field in new[] { "dependencies", "optionalDependencies",
            "peerDependencies", "peerDependenciesMeta" })
            metadata[field] = new JsonObject();
        metadata["bundledDependencies"] = false;
        metadata["bundleDependencies"] = new JsonArray();
        metadata["gypfile"] = false;
        fixture.NativeMetadata = metadata.ToJsonString();

        NodeRunResult result = await fixture.RunAsync(context.CancellationToken);

        Assert.IsTrue(ResultCollector.Collect(fixture.SelectedPlan, result.Results).Satisfied);
        Assert.IsEmpty(result.Failures);
        Assert.ContainsSingle(result.Outputs);
    }

    [TestMethod]
    public async Task RunUsesOriginalTarballAndFreshNativeConsumerPaths()
    {
        using var fixture = NodeExecutionFixture.Shared(shared!, sharedCandidate!);
        fixture.Override = command =>
        {
            if (!command.Arguments.Contains("install"))
                return null;
            Assert.IsEmpty(Directory.EnumerateFileSystemEntries(command.Directory));
            foreach (string flag in new[] { "--store-dir", "--state-dir" })
                Assert.IsEmpty(Directory.EnumerateFileSystemEntries(Value(command, flag)));
            Assert.AreEqual("", File.ReadAllText(Value(command, "--npmrc-auth-file")));
            return null;
        };

        NodeRunResult result = await fixture.RunAsync(context.CancellationToken);

        NativeCommand pack = result.Commands.Single(item =>
            item.Key.Check == NodeExecutionFixture.Pack).Command;
        Assert.AreEqual(300, pack.DeadlineSeconds);
        CollectionAssert.AreEqual(new[] { "--dir", Path.Combine(fixture.Repository.Directory,
            NodeExecutionFixture.Source), "pack", "--out", result.Outputs.Single().Path },
            pack.Arguments);
        foreach (NodeCommandObservation source in result.Commands.Where(item =>
            item.Key.Check is NodeExecutionFixture.Build or NodeExecutionFixture.Test))
        {
            Assert.AreEqual("pnpm", source.Command.Executable);
            Assert.AreEqual(fixture.Repository.Directory, source.Command.Directory);
            Assert.AreEqual(300, source.Command.DeadlineSeconds);
            string script = source.Key.Check == NodeExecutionFixture.Build ? "build" : "test";
            string[] arguments = ["--dir", Path.Combine(fixture.Repository.Directory,
                NodeExecutionFixture.Source), "--config.verify-deps-before-run=false",
                "run", script];
            CollectionAssert.AreEqual(arguments, source.Command.Arguments);
        }
        NativeCommand install = fixture.Commands.Single(item => item.Arguments.Contains("install"));
        Assert.AreEqual(result.Outputs.Single().Path, Value(install, "install"));
        Assert.AreEqual(install.Directory, Value(install, "--dir"));
        Assert.Contains("--offline", install.Arguments);
        Assert.Contains("--ignore-scripts", install.Arguments);
        Assert.Contains("--ignore-workspace", install.Arguments);
        Assert.AreEqual(300, install.DeadlineSeconds);
        Assert.StartsWith(fixture.Scratch + Path.DirectorySeparatorChar, install.Directory);
        Assert.StartsWith(fixture.Scratch + Path.DirectorySeparatorChar,
            Value(install, "--store-dir"));
        Assert.StartsWith(fixture.Scratch + Path.DirectorySeparatorChar,
            Value(install, "--state-dir"));
        NativeCommand import = fixture.Commands.Single(item => item.Executable == "node" &&
            !item.Arguments.Contains("--version"));
        Assert.AreEqual(install.Directory, import.Directory);
        Assert.AreEqual(30, import.DeadlineSeconds);
        string[] nodeFlags = ["--input-type=module", "--eval"];
        CollectionAssert.AreEqual(nodeFlags, import.Arguments[..2]);
        Assert.Contains("import { smokeMessage } from '@hcoona/hcoona-release-smoke-npm'",
            import.Arguments[2]);
        Assert.Contains("assert.equal(smokeMessage(), 'hcoona-release-smoke-npm')",
            import.Arguments[2]);
        NativeCommand[] queries = fixture.Commands.Where(item => item.Arguments.Contains("pkg"))
            .ToArray();
        Assert.HasCount(2, queries);
        Assert.AreNotEqual(queries[0].Directory, queries[1].Directory);
        foreach (NativeCommand query in queries)
        {
            Assert.AreEqual(query.Directory, Value(query, "--dir"));
            Assert.AreEqual(30, query.DeadlineSeconds);
            Assert.Contains("--ignore-workspace", query.Arguments);
            Assert.Contains("peerDependenciesMeta", query.Arguments);
            Assert.Contains("engines.runtime", query.Arguments);
            Assert.AreEqual("", File.ReadAllText(Value(query, "--npmrc-auth-file")));
        }
    }

    [TestMethod]
    public async Task RunFailedContentsStillExecutesIndependentConsumer()
    {
        using var fixture = NodeExecutionFixture.Shared(shared!, sharedCandidate!);
        fixture.Override = command => command.Arguments.Contains("pkg") &&
            Path.GetFileName(command.Directory) == "contents-metadata"
            ? NodeExecutionFixture.Failure() : null;

        NodeRunResult result = await fixture.RunAsync(context.CancellationToken);

        Assert.AreEqual(CheckStatus.Failed, Status(result, NodeExecutionFixture.Contents));
        Assert.AreEqual(CheckStatus.Passed, Status(result, NodeExecutionFixture.Consumer));
        Assert.IsTrue(result.Commands.Any(item => item.Command.Executable == "node"));
        Assert.IsFalse(ResultCollector.Collect(fixture.SelectedPlan, result.Results).Satisfied);
    }

    [TestMethod]
    [DataRow("install")]
    [DataRow("node")]
    public async Task RunInstallOrPublicApiFailureCannotPass(string stage)
    {
        using var fixture = NodeExecutionFixture.Shared(shared!, sharedCandidate!);
        fixture.Override = command => (stage == "node" ? command.Executable == "node" &&
            !command.Arguments.Contains("--version") :
            command.Arguments.Contains("install")) ? NodeExecutionFixture.Failure() : null;

        NodeRunResult result = await fixture.RunAsync(context.CancellationToken);

        Assert.AreEqual(CheckStatus.Passed, Status(result, NodeExecutionFixture.Contents));
        Assert.AreEqual(CheckStatus.Failed, Status(result, NodeExecutionFixture.Consumer));
        Assert.IsFalse(ResultCollector.Collect(fixture.SelectedPlan, result.Results).Satisfied);
        Assert.AreEqual("visible failure", result.Commands.Last().Result.Stderr);
        if (stage == "install")
            Assert.IsFalse(fixture.Commands.Any(item => item.Executable == "node" &&
                !item.Arguments.Contains("--version")));
    }

    [TestMethod]
    [DataRow("Exited", "Failed")]
    [DataRow("StartFailed", "Failed")]
    [DataRow("Cancelled", "Cancelled")]
    [DataRow("TimedOut", "TimedOut")]
    [DataRow("InvalidOutput", "Failed")]
    [DataRow("CleanupFailed", "Failed")]
    public void NativeTerminationNeverPasses(string nativeTermination, string status) =>
        Assert.AreEqual(Enum.Parse<CheckStatus>(status), NodeExecution.Status(
            NodeExecutionFixture.Failure(Enum.Parse<NativeTermination>(nativeTermination))));

    [TestMethod]
    public void OnlySuccessfulExitPasses()
    {
        Assert.AreEqual(CheckStatus.Passed, NodeExecution.Status(NodeExecutionFixture.Success()));
        Assert.AreEqual(CheckStatus.Failed, NodeExecution.Status(
            NodeExecutionFixture.Success() with { ExitCode = null }));
    }

    [TestMethod]
    public async Task RunNativeTerminationNeverPasses()
    {
        NativeTermination termination = NativeTermination.TimedOut;
        using var fixture = NodeExecutionFixture.Shared(shared!, sharedCandidate!);
        fixture.Override = command => command.Arguments[^1] == "build"
            ? NodeExecutionFixture.Failure(termination) : null;

        NodeRunResult result = await fixture.RunAsync(context.CancellationToken);

        Assert.AreEqual(CheckStatus.TimedOut, Status(result, NodeExecutionFixture.Build));
        Assert.AreEqual(CheckStatus.Passed, Status(result, NodeExecutionFixture.Test));
        Assert.AreEqual(CheckStatus.Skipped, Status(result, NodeExecutionFixture.Pack));
        Assert.AreEqual(termination, result.Commands[0].Result.Termination);
        Assert.AreEqual("partial stdout", result.Commands[0].Result.Stdout);
        Assert.IsFalse(ResultCollector.Collect(fixture.SelectedPlan, result.Results).Satisfied);
    }

    [TestMethod]
    public async Task RunCancellationRetainsEveryRemainingKey()
    {
        using var fixture = NodeExecutionFixture.Shared(shared!, sharedCandidate!);
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(
            context.CancellationToken);
        fixture.Override = command =>
        {
            if (command.Arguments.Contains("--version"))
                return null;
            cancellation.Cancel();
            return NodeExecutionFixture.Failure(NativeTermination.Cancelled, null);
        };

        NodeRunResult result = await fixture.RunAsync(cancellation.Token);

        CollectionAssert.AreEqual(fixture.SelectedPlan.Checks
            .Select(item => item.Work.Key).ToArray(),
            result.Results.Select(item => item.Key).ToArray());
        Assert.IsTrue(result.Results.All(item => item.Status == CheckStatus.Cancelled));
        Assert.IsEmpty(result.Outputs);
        Assert.IsFalse(ResultCollector.Collect(fixture.SelectedPlan, result.Results).Satisfied);
    }

    [TestMethod]
    public async Task RunRejectsMutableCandidateEvenForEmptyPlan()
    {
        using var fixture = NodeExecutionFixture.Shared(shared!, sharedCandidate!);
        CiPlan plan = fixture.Plan with { Candidate = "HEAD", Comparison = "HEAD", Checks = [] };

        InvalidDataException error = await Assert.ThrowsExactlyAsync<InvalidDataException>(() =>
            fixture.RunAsync(context.CancellationToken, plan));

        Assert.Contains("exact native commit identity", error.Message);
        Assert.IsEmpty(fixture.Commands);
        Assert.IsEmpty(Directory.EnumerateFileSystemEntries(fixture.Scratch));
    }

    [TestMethod]
    public async Task RunEmptyPlanBindsExactCandidateWithoutProductWork()
    {
        using var fixture = NodeExecutionFixture.Shared(shared!, sharedCandidate!);
        CiPlan plan = fixture.Plan with { Checks = [] };

        NodeRunResult result = await fixture.RunAsync(context.CancellationToken, plan);

        Assert.AreEqual(fixture.Plan.Candidate, result.Candidate);
        Assert.IsEmpty(result.Results);
        Assert.IsEmpty(result.Commands);
        Assert.IsEmpty(result.Outputs);
        Assert.IsEmpty(result.Failures);
        Assert.AreEqual(fixture.Runtime, result.Runtime);
        Assert.IsNull(result.RuntimeVersions);
        Assert.IsEmpty(fixture.Commands);
        Assert.IsEmpty(Directory.EnumerateFileSystemEntries(fixture.Scratch));
        Assert.IsTrue(ResultCollector.Collect(plan with
        {
            Checks = plan.Checks.Where(item =>
            item.Work.Key.Variant == fixture.Runtime).ToArray()
        }, result.Results).Satisfied);
    }

    [TestMethod]
    public async Task RunEmptyPlanRejectsCheckoutWithoutGit()
    {
        using var fixture = NodeExecutionFixture.Shared(shared!, sharedCandidate!);
        using var nonGit = new ScratchFixture("non-git-checkout");
        NodeRunRequest request = fixture.Request with { Checkout = nonGit.Scratch };

        await Assert.ThrowsExactlyAsync<InvalidDataException>(() => fixture.RunAsync(
            context.CancellationToken, fixture.Plan with { Checks = [] }, request));

        Assert.IsEmpty(fixture.Commands);
        Assert.IsEmpty(Directory.EnumerateFileSystemEntries(fixture.Scratch));
    }

    [TestMethod]
    [DataRow("%s")]
    [DataRow("%v")]
    public async Task RunRejectsNativeOutputMarkersInEffectiveScratchParents(string marker)
    {
        using var fixture = NodeExecutionFixture.Shared(shared!, sharedCandidate!);
        using var scratch = new ScratchFixture("native-" + marker + "-parent");
        NodeRunRequest request = fixture.Request with { Scratch = scratch.Scratch };

        InvalidDataException error = await Assert.ThrowsExactlyAsync<InvalidDataException>(() =>
            fixture.RunAsync(context.CancellationToken, request: request));

        Assert.Contains("native pack output markers", error.Message);
        Assert.IsEmpty(fixture.Commands);
        Assert.IsEmpty(Directory.EnumerateFiles(scratch.Root, "*", SearchOption.AllDirectories));
        Assert.HasCount(2, Directory.EnumerateDirectories(scratch.Root, "*",
            SearchOption.AllDirectories));
    }

    [TestMethod]
    public async Task RunAllowsUnrelatedPercentPaths()
    {
        using var fixture = NodeExecutionFixture.Shared(shared!, sharedCandidate!);
        using var scratch = new ScratchFixture("ordinary-%q-%20-parent");

        NodeRunResult result = await fixture.RunAsync(context.CancellationToken,
            request: fixture.Request with { Scratch = scratch.Scratch });

        Assert.IsTrue(ResultCollector.Collect(fixture.SelectedPlan, result.Results).Satisfied);
        Assert.StartsWith(scratch.Scratch + Path.DirectorySeparatorChar,
            Assert.ContainsSingle(result.Outputs).Path);
        Assert.IsEmpty(result.Failures);
    }

    private sealed class ScratchFixture : IDisposable
    {
        internal string Root { get; } = Directory.CreateTempSubdirectory(
            "workflow-node-scratch-").FullName;
        internal string Scratch { get; }

        internal ScratchFixture(string parent)
        {
            Scratch = Path.Combine(Root, parent, "fresh");
            Directory.CreateDirectory(Scratch);
        }

        public void Dispose() => Directory.Delete(Root, recursive: true);
    }

    private static string UnexpectedMetadata(string field, string value)
    {
        JsonObject metadata = JsonNode.Parse(NodeExecutionFixture.Metadata)!.AsObject();
        if (value == "missing")
            metadata.Remove(field);
        else if (value == "number")
            metadata[field] = 42;
        else
            metadata[field] = value;
        return value switch
        {
            "malformed" => "{broken",
            "array" => "[]",
            "duplicate" => NodeExecutionFixture.Metadata.TrimEnd()[..^1] +
                ",\"name\":\"@hcoona/hcoona-release-smoke-npm\"}",
            _ => metadata.ToJsonString(),
        };
    }

    private static string InstalledShape(string field, string json)
    {
        JsonObject metadata = JsonNode.Parse(NodeExecutionFixture.Metadata)!.AsObject();
        metadata[field] = JsonNode.Parse(json);
        return metadata.ToJsonString();
    }

    // Execution records both exception types as a failed artifact check.
    private static void AssertMetadataRejected(string metadata)
    {
        Exception error = Assert.Throws<Exception>(() =>
            AdoptedNpmPackage.ValidateMetadata(metadata, "1.2.3"));
        Assert.IsTrue(error is InvalidDataException or JsonException, error.ToString());
    }

    private static CheckStatus Status(NodeRunResult result, string operation,
        string? build = null) => result.Results.Single(item => item.Key.Check == operation &&
            (build is null || item.Key.Target == "release/product/" + build)).Status;

    private static string Value(NativeCommand command, string key) =>
        command.Arguments[Array.IndexOf(command.Arguments, key) + 1];
}
