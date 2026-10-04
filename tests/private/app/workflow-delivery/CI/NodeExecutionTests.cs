using System.Security.Cryptography;
using System.Text.Json.Nodes;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using WorkflowDelivery.CI;
using WorkflowDelivery.Platform;

namespace WorkflowDelivery.Tests.CI;

[TestClass]
public sealed class NodeExecutionTests(TestContext context)
{
    [TestMethod]
    [DataRow("check")]
    [DataRow("runner")]
    [DataRow("variant")]
    [DataRow("dimensions")]
    [DataRow("preset")]
    [DataRow("association")]
    [DataRow("prerequisite")]
    [DataRow("missing-consumer")]
    [DataRow("output-identity")]
    [DataRow("definition")]
    [DataRow("publish-directory")]
    [DataRow("output-kind")]
    [DataRow("scope")]
    public async Task RunRejectsCompletePlanBeforeAnyCommand(string defect)
    {
        using var fixture = await NodeExecutionFixture.CreateAsync(context.CancellationToken,
            "first", "second");
        PlannedCheck[] checks = fixture.Plan.Checks.ToArray();
        PlannedCheck last = checks[^1];
        CheckSpec work = last.Work;
        work = defect switch
        {
            "check" => work with { Key = work.Key with { Check = "unknown" } },
            "runner" => work with { Runner = "windows-latest" },
            "variant" => work with { Key = work.Key with { Variant = "alternate" } },
            "dimensions" => work with { Dimensions = new() { ["os"] = "ubuntu" } },
            "association" => work with { Package = work.Package! with
                { ExpectedVersion = "9.9.9" } },
            "prerequisite" => work with { Prerequisites = [] },
            _ => work,
        };
        checks[^1] = last with { Work = work,
            QualityPresets = defect == "preset" ? ["unknown"] : last.QualityPresets };
        if (defect == "missing-consumer")
            checks = checks[..^1];
        if (defect == "output-identity")
            checks = checks.Select(item => item.Work.Package?.Build == "second"
                ? item with { Work = item.Work with { Package = item.Work.Package with
                    { Outputs = [new("first-package", "primary-package", "npm-tarball")] } } }
                : item).ToArray();
        if (defect is "definition" or "publish-directory" or "output-kind")
            checks = checks.Select(item => item.Work.Package?.Build == "second"
                ? item with { Work = item.Work with { Package = defect switch
                {
                    "definition" => item.Work.Package with { Definition = "unknown" },
                    "publish-directory" => item.Work.Package with { PublishDirectory = "packed" },
                    _ => item.Work.Package with
                        { Outputs = [new("second-package", "primary-package", "unknown")] },
                } } } : item).ToArray();
        CiPlan plan = fixture.Plan with { Checks = checks,
            Scope = defect == "scope" ? "unknown" : fixture.Plan.Scope };

        await Assert.ThrowsAsync<InvalidDataException>(() => fixture.RunAsync(
            context.CancellationToken, plan));

        Assert.IsEmpty(fixture.Commands);
        Assert.IsEmpty(Directory.EnumerateFileSystemEntries(fixture.Scratch));
    }

    [TestMethod]
    [DataRow("dirty")]
    [DataRow("wrong-head")]
    [DataRow("relative-checkout")]
    [DataRow("relative-scratch")]
    [DataRow("missing-scratch")]
    [DataRow("same")]
    [DataRow("nested")]
    [DataRow("nonempty")]
    public async Task RunRejectsUnboundCheckoutOrInvalidScratch(string defect)
    {
        using var fixture = await NodeExecutionFixture.CreateAsync(context.CancellationToken);
        NodeRunRequest request = fixture.Request;
        if (defect == "dirty")
            await File.WriteAllTextAsync(Path.Combine(request.Checkout,
                NodeExecutionFixture.Source, "index.js"), "changed", context.CancellationToken);
        if (defect == "wrong-head")
        {
            await fixture.Repository.SetAsync("new.txt");
            string next = await fixture.Repository.CommitAsync(fixture.Plan.Candidate);
            await fixture.Repository.GitAsync("reset", "--hard", next);
        }
        if (defect == "nested")
        {
            string nested = Path.Combine(request.Checkout, "scratch");
            Directory.CreateDirectory(nested);
            request = request with { Scratch = nested };
        }
        if (defect == "nonempty")
            File.WriteAllText(Path.Combine(request.Scratch, "previous.txt"), "previous");
        request = defect switch
        {
            "relative-checkout" => request with { Checkout = "repository" },
            "relative-scratch" => request with { Scratch = "scratch" },
            "missing-scratch" => request with { Scratch = Path.Combine(request.Scratch, "absent") },
            "same" => request with { Scratch = request.Checkout },
            _ => request,
        };

        await Assert.ThrowsAsync<InvalidDataException>(() => fixture.RunAsync(
            context.CancellationToken, request: request));

        Assert.IsEmpty(fixture.Commands);
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

        Assert.IsTrue(ResultCollector.Collect(fixture.Plan, result.Results).Satisfied);
        Assert.HasCount(1, result.Outputs);
        Assert.Contains("native build mutation", await File.ReadAllTextAsync(Path.Combine(
            fixture.Repository.Directory, NodeExecutionFixture.Source, "index.js"),
            context.CancellationToken));
    }

    [TestMethod]
    public async Task RunRetainsEveryMultiBuildCheckAndOriginalOutput()
    {
        using var fixture = await NodeExecutionFixture.CreateAsync(context.CancellationToken,
            "first", "second");
        CiPlan plan = fixture.Plan with { Checks = fixture.Plan.Checks.Reverse().ToArray() };

        NodeRunResult result = await fixture.RunAsync(context.CancellationToken, plan);

        Assert.AreEqual(plan.Candidate, result.Candidate);
        CollectionAssert.AreEqual(plan.Checks.Select(item => item.Work.Key).ToArray(),
            result.Results.Select(item => item.Key).ToArray());
        Assert.IsTrue(result.Results.All(item => item.Status == CheckStatus.Passed));
        Assert.IsTrue(result.Results.All(item => item.Candidate == plan.Candidate));
        Assert.IsTrue(ResultCollector.Collect(plan, result.Results).Satisfied);
        Assert.HasCount(2, result.Outputs);
        Assert.HasCount(12, result.Commands);
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
    public async Task RunFailedBuildStillExecutesIndependentSourceTest()
    {
        using var fixture = await NodeExecutionFixture.CreateAsync(context.CancellationToken);
        fixture.Override = command => command.Arguments[^1] == "build"
            ? NodeExecutionFixture.Failure() : null;

        NodeRunResult result = await fixture.RunAsync(context.CancellationToken);

        Assert.AreEqual(CheckStatus.Failed, Status(result, NodeExecutionFixture.Build));
        Assert.AreEqual(CheckStatus.Passed, Status(result, NodeExecutionFixture.Test));
        foreach (string check in new[] { NodeExecutionFixture.Pack, NodeExecutionFixture.Contents,
            NodeExecutionFixture.Consumer })
            Assert.AreEqual(CheckStatus.Skipped, Status(result, check));
        Assert.HasCount(2, result.Commands);
        Assert.IsEmpty(result.Outputs);
        Assert.IsFalse(ResultCollector.Collect(fixture.Plan, result.Results).Satisfied);
    }

    [TestMethod]
    public async Task RunFailedPackWithLeftoverSkipsOnlyItsArtifactChecks()
    {
        using var fixture = await NodeExecutionFixture.CreateAsync(context.CancellationToken,
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
        Assert.IsFalse(ResultCollector.Collect(fixture.Plan, result.Results).Satisfied);
    }

    [TestMethod]
    [DataRow("missing")]
    [DataRow("directory")]
    [DataRow("empty")]
    public async Task RunRejectsMissingOrNonregularPackOutput(string defect)
    {
        using var fixture = await NodeExecutionFixture.CreateAsync(context.CancellationToken);
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
        Assert.IsFalse(ResultCollector.Collect(fixture.Plan, result.Results).Satisfied);
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
    public async Task RunRejectsUnexpectedNativeMetadata(string field, string value)
    {
        using var fixture = await NodeExecutionFixture.CreateAsync(context.CancellationToken);
        JsonObject metadata = JsonNode.Parse(NodeExecutionFixture.Metadata)!.AsObject();
        if (value == "missing")
            metadata.Remove(field);
        else if (value == "number")
            metadata[field] = 42;
        else
            metadata[field] = value;
        fixture.NativeMetadata = value switch
        {
            "malformed" => "{broken",
            "array" => "[]",
            "duplicate" => NodeExecutionFixture.Metadata.TrimEnd()[..^1] +
                ",\"name\":\"@hcoona/hcoona-release-smoke-npm\"}",
            _ => metadata.ToJsonString(),
        };

        NodeRunResult result = await fixture.RunAsync(context.CancellationToken);

        AssertMetadataFailed(result);
    }

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
    public async Task RunRejectsUnsupportedInstalledShape(string field, string json)
    {
        using var fixture = await NodeExecutionFixture.CreateAsync(context.CancellationToken);
        JsonObject metadata = JsonNode.Parse(NodeExecutionFixture.Metadata)!.AsObject();
        metadata[field] = JsonNode.Parse(json);
        fixture.NativeMetadata = metadata.ToJsonString();

        NodeRunResult result = await fixture.RunAsync(context.CancellationToken);

        AssertMetadataFailed(result);
    }

    [TestMethod]
    public async Task RunAllowsDevelopmentAndPackLifecycles()
    {
        using var fixture = await NodeExecutionFixture.CreateAsync(context.CancellationToken);
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

        Assert.IsTrue(ResultCollector.Collect(fixture.Plan, result.Results).Satisfied);
        Assert.IsEmpty(result.Failures);
        Assert.HasCount(1, fixture.Commands.Where(item => item.Arguments.Contains("pack")));
    }

    [TestMethod]
    public async Task RunUsesOriginalTarballAndFreshNativeConsumerPaths()
    {
        using var fixture = await NodeExecutionFixture.CreateAsync(context.CancellationToken);
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
                NodeExecutionFixture.Source), "run", script];
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
        NativeCommand import = fixture.Commands.Single(item => item.Executable == "node");
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
        using var fixture = await NodeExecutionFixture.CreateAsync(context.CancellationToken);
        fixture.Override = command => command.Arguments.Contains("pkg") &&
            Path.GetFileName(command.Directory) == "contents-metadata"
            ? NodeExecutionFixture.Failure() : null;

        NodeRunResult result = await fixture.RunAsync(context.CancellationToken);

        Assert.AreEqual(CheckStatus.Failed, Status(result, NodeExecutionFixture.Contents));
        Assert.AreEqual(CheckStatus.Passed, Status(result, NodeExecutionFixture.Consumer));
        Assert.IsTrue(result.Commands.Any(item => item.Command.Executable == "node"));
        Assert.IsFalse(ResultCollector.Collect(fixture.Plan, result.Results).Satisfied);
    }

    [TestMethod]
    [DataRow("install")]
    [DataRow("node")]
    public async Task RunInstallOrPublicApiFailureCannotPass(string stage)
    {
        using var fixture = await NodeExecutionFixture.CreateAsync(context.CancellationToken);
        fixture.Override = command => (stage == "node" ? command.Executable == "node" :
            command.Arguments.Contains("install")) ? NodeExecutionFixture.Failure() : null;

        NodeRunResult result = await fixture.RunAsync(context.CancellationToken);

        Assert.AreEqual(CheckStatus.Passed, Status(result, NodeExecutionFixture.Contents));
        Assert.AreEqual(CheckStatus.Failed, Status(result, NodeExecutionFixture.Consumer));
        Assert.IsFalse(ResultCollector.Collect(fixture.Plan, result.Results).Satisfied);
        Assert.AreEqual("visible failure", result.Commands.Last().Result.Stderr);
        if (stage == "install")
            Assert.IsFalse(fixture.Commands.Any(item => item.Executable == "node"));
    }

    [TestMethod]
    [DataRow("Exited", "Failed")]
    [DataRow("StartFailed", "Failed")]
    [DataRow("Cancelled", "Cancelled")]
    [DataRow("TimedOut", "TimedOut")]
    [DataRow("InvalidOutput", "Failed")]
    [DataRow("CleanupFailed", "Failed")]
    public async Task RunNativeTerminationNeverPasses(string nativeTermination, string status)
    {
        NativeTermination termination = Enum.Parse<NativeTermination>(nativeTermination);
        CheckStatus expected = Enum.Parse<CheckStatus>(status);
        using var fixture = await NodeExecutionFixture.CreateAsync(context.CancellationToken);
        fixture.Override = command => command.Arguments[^1] == "build"
            ? NodeExecutionFixture.Failure(termination) : null;

        NodeRunResult result = await fixture.RunAsync(context.CancellationToken);

        Assert.AreEqual(expected, Status(result, NodeExecutionFixture.Build));
        Assert.AreEqual(CheckStatus.Passed, Status(result, NodeExecutionFixture.Test));
        Assert.AreEqual(CheckStatus.Skipped, Status(result, NodeExecutionFixture.Pack));
        Assert.AreEqual(termination, result.Commands[0].Result.Termination);
        Assert.AreEqual("partial stdout", result.Commands[0].Result.Stdout);
        Assert.IsFalse(ResultCollector.Collect(fixture.Plan, result.Results).Satisfied);
    }

    [TestMethod]
    public async Task RunCancellationRetainsEveryRemainingKey()
    {
        using var fixture = await NodeExecutionFixture.CreateAsync(context.CancellationToken);
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(
            context.CancellationToken);
        fixture.Override = command =>
        {
            cancellation.Cancel();
            return NodeExecutionFixture.Failure(NativeTermination.Cancelled, null);
        };

        NodeRunResult result = await fixture.RunAsync(cancellation.Token);

        Assert.HasCount(1, result.Commands);
        CollectionAssert.AreEqual(fixture.Plan.Checks.Select(item => item.Work.Key).ToArray(),
            result.Results.Select(item => item.Key).ToArray());
        Assert.IsTrue(result.Results.All(item => item.Status == CheckStatus.Cancelled));
        Assert.IsEmpty(result.Outputs);
        Assert.IsFalse(ResultCollector.Collect(fixture.Plan, result.Results).Satisfied);
    }

    [TestMethod]
    [DataRow("HEAD", false)]
    [DataRow("abbreviation", false)]
    [DataRow("tag", false)]
    [DataRow("HEAD", true)]
    [DataRow("abbreviation", true)]
    [DataRow("tag", true)]
    public async Task RunRejectsNativeCandidateAliasesBeforeProductCommands(string alias,
        bool empty)
    {
        using var fixture = await NodeExecutionFixture.CreateAsync(context.CancellationToken);
        await fixture.Repository.GitAsync("tag", "candidate-alias", fixture.Plan.Candidate);
        string candidate = alias switch
        {
            "abbreviation" => fixture.Plan.Candidate[..12],
            "tag" => "candidate-alias",
            _ => alias,
        };
        CiPlan plan = fixture.Plan with
        {
            Candidate = candidate,
            Comparison = candidate,
            Checks = empty ? [] : fixture.Plan.Checks.Select(item => item with
            {
                Reasons = [new(NodeExecutionFixture.Source + "/index.js", candidate,
                    NodeExecutionFixture.Source)],
            }).ToArray(),
        };

        InvalidDataException error = await Assert.ThrowsExactlyAsync<InvalidDataException>(() =>
            fixture.RunAsync(context.CancellationToken, plan));

        Assert.Contains("exact native commit identity", error.Message);
        Assert.IsEmpty(fixture.Commands);
        Assert.IsEmpty(Directory.EnumerateFileSystemEntries(fixture.Scratch));
    }

    [TestMethod]
    public async Task RunEmptyPlanBindsExactCandidateWithoutProductWork()
    {
        using var fixture = await NodeExecutionFixture.CreateAsync(context.CancellationToken);
        CiPlan plan = fixture.Plan with { Checks = [] };

        NodeRunResult result = await fixture.RunAsync(context.CancellationToken, plan);

        Assert.AreEqual(fixture.Plan.Candidate, result.Candidate);
        Assert.IsEmpty(result.Results);
        Assert.IsEmpty(result.Commands);
        Assert.IsEmpty(result.Outputs);
        Assert.IsEmpty(result.Failures);
        Assert.IsEmpty(fixture.Commands);
        Assert.IsEmpty(Directory.EnumerateFileSystemEntries(fixture.Scratch));
        Assert.IsTrue(ResultCollector.Collect(plan, result.Results).Satisfied);
    }

    [TestMethod]
    [DataRow("dirty")]
    [DataRow("wrong-head")]
    [DataRow("non-git")]
    public async Task RunEmptyPlanRejectsInvalidStartingBind(string defect)
    {
        using var fixture = await NodeExecutionFixture.CreateAsync(context.CancellationToken);
        using var nonGit = new ScratchFixture("non-git-checkout");
        NodeRunRequest request = fixture.Request;
        if (defect == "dirty")
            await File.WriteAllTextAsync(Path.Combine(request.Checkout,
                NodeExecutionFixture.Source, "index.js"), "changed", context.CancellationToken);
        if (defect == "wrong-head")
        {
            await fixture.Repository.SetAsync("new.txt");
            string next = await fixture.Repository.CommitAsync(fixture.Plan.Candidate);
            await fixture.Repository.GitAsync("reset", "--hard", next);
        }
        if (defect == "non-git")
            request = request with { Checkout = nonGit.Scratch };

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
        using var fixture = await NodeExecutionFixture.CreateAsync(context.CancellationToken);
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
        using var fixture = await NodeExecutionFixture.CreateAsync(context.CancellationToken);
        using var scratch = new ScratchFixture("ordinary-%q-%20-parent");

        NodeRunResult result = await fixture.RunAsync(context.CancellationToken,
            request: fixture.Request with { Scratch = scratch.Scratch });

        Assert.IsTrue(ResultCollector.Collect(fixture.Plan, result.Results).Satisfied);
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

    private static void AssertMetadataFailed(NodeRunResult result)
    {
        Assert.AreEqual(CheckStatus.Passed, Status(result, NodeExecutionFixture.Pack));
        Assert.AreEqual(CheckStatus.Failed, Status(result, NodeExecutionFixture.Contents));
        Assert.AreEqual(CheckStatus.Failed, Status(result, NodeExecutionFixture.Consumer));
        Assert.HasCount(2, result.Failures);
        Assert.IsFalse(result.Commands.Any(item => item.Command.Arguments.Contains("install")));
    }

    private static CheckStatus Status(NodeRunResult result, string operation,
        string? build = null) => result.Results.Single(item => item.Key.Check == operation &&
            (build is null || item.Key.Target == "release/product/" + build)).Status;

    private static string Value(NativeCommand command, string key) =>
        command.Arguments[Array.IndexOf(command.Arguments, key) + 1];
}
