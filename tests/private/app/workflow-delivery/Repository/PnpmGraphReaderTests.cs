using System.Text.Json;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using WorkflowDelivery.Repository;

namespace WorkflowDelivery.Tests.Repository;

[TestClass]
public sealed class PnpmGraphReaderTests(TestContext context)
{
    private static readonly string[] LocalConsumers = ["packages/app", "packages/top"];
    private static readonly string[] NestedConsumers = ["packages/one", "packages/two"];

    [TestMethod]
    public async Task ReadRetainsNativeProjectNamesWithoutChangingDependencyOwnership()
    {
        var native = new NativeQueries(".", "packages/library", "packages/app");
        native.Names["packages/library"] = "@example/library";
        native.Names["packages/app"] = "@example/app";
        native.Nodes["packages/app"] = [native.Node("link:../library", "packages/library"),
            native.Node("1.0.0", "node_modules/copied", "file:fixtures/library.tgz")];

        PnpmGraph graph = await native.Reader.ReadAsync(CancellationToken.None);

        PnpmProject library = graph.Projects.Single(project => project.Directory ==
            "packages/library");
        PnpmProject app = graph.Projects.Single(project => project.Directory == "packages/app");
        Assert.AreEqual("@example/library", library.Name);
        Assert.AreEqual("@example/app", app.Name);
        Assert.IsNull(graph.Projects.Single(project => project.Directory == ".").Name);
        Assert.AreEqual("packages/library", Assert.ContainsSingle(app.Dependencies));
        PnpmLocalInput archive = Assert.ContainsSingle(graph.LocalInputs);
        Assert.AreEqual("fixtures/library.tgz", archive.Path);
        Assert.AreEqual("packages/app", Assert.ContainsSingle(archive.Consumers));
        Assert.IsFalse(archive.IsDirectory);
    }

    [TestMethod]
    [DataRow("")]
    [DataRow(" ")]
    [DataRow(12)]
    public async Task ReadRejectsMalformedNativeProjectName(object name)
    {
        var native = new NativeQueries(".", "packages/library");
        native.Names["packages/library"] = name;

        await Assert.ThrowsExactlyAsync<InvalidDataException>(() =>
            native.Reader.ReadAsync(CancellationToken.None));
    }

    [TestMethod]
    [DataRow("dependencies")]
    [DataRow("devDependencies")]
    [DataRow("optionalDependencies")]
    public async Task ReadSavedGraphIgnoresExtraneousInstalledRelations(string group)
    {
        var native = new NativeQueries(".", "packages/library", "packages/app",
            "packages/stale")
        { DependencyGroup = group };
        native.Nodes["packages/app"] = [
            native.Node("link:../library", "packages/library"),
            native.Node("file:fixtures/saved", "node_modules/saved"),
        ];
        native.UnsavedNodes["packages/app"] = [
            native.Node("link:../stale", "packages/stale"),
            native.Node("file:fixtures/stale", "node_modules/stale"),
            native.Node("1.0.0", "node_modules/archive", "file:fixtures/stale.tgz"),
        ];

        PnpmGraph graph = await native.Reader.ReadAsync(CancellationToken.None);

        Assert.AreEqual("packages/library", Assert.ContainsSingle(graph.Projects.Single(
            p => p.Directory == "packages/app").Dependencies));
        PnpmLocalInput input = Assert.ContainsSingle(graph.LocalInputs);
        Assert.AreEqual("fixtures/saved", input.Path);
        Assert.AreEqual("packages/app", Assert.ContainsSingle(input.Consumers));
        Assert.IsTrue(input.IsDirectory);
    }

    [TestMethod]
    public async Task ReadDeepCompleteNativeTreeKeepsItsDeepestLocalInput()
    {
        var native = new NativeQueries(".", "packages/app");
        native.Nodes["packages/app"] = [Chain(native, 255)];

        PnpmGraph graph = await native.Reader.ReadAsync(CancellationToken.None);

        PnpmLocalInput input = Assert.ContainsSingle(graph.LocalInputs);
        Assert.AreEqual("fixtures/deep", input.Path);
        Assert.AreEqual("packages/app", Assert.ContainsSingle(input.Consumers));
        Assert.IsTrue(input.IsDirectory);
        Assert.IsEmpty(graph.Projects.Single(p => p.Directory == "packages/app").Dependencies);
    }

    [TestMethod]
    public async Task ReadRejectsNativeTreeAtIndistinguishableTruncationBoundary()
    {
        var native = new NativeQueries(".", "packages/app");
        native.Nodes["packages/app"] = [Chain(native, 256)];

        await Assert.ThrowsAsync<JsonException>(
            () => native.Reader.ReadAsync(CancellationToken.None));
    }

    private static object Chain(NativeQueries native, int depth)
    {
        object node = native.Node("file:fixtures/deep", "node_modules/deep");
        for (int level = 1; level < depth; level++)
            node = native.Node("1.0.0", "node_modules/level-" + level, children: [node]);
        return node;
    }

    [TestMethod]
    public async Task ReadSingletonTreesPreservesBothConsumersOfNestedLocalInput()
    {
        var native = new NativeQueries(".", "packages/one", "packages/two", "packages/unrelated");
        object nested = native.Node("file:fixtures/b", "node_modules/.pnpm/b");
        object first = native.Node("file:fixtures/a", "node_modules/.pnpm/a", children: [nested]);
        Dictionary<string, object> repeated = native.Node("file:fixtures/a",
            "node_modules/.pnpm/a");
        repeated["deduped"] = true;
        repeated["dedupedDependenciesCount"] = 1;
        native.Nodes["packages/one"] = [first, repeated];
        native.Nodes["packages/two"] = [first, repeated];

        PnpmGraph graph = await native.Reader.ReadAsync(CancellationToken.None);

        PnpmLocalInput input = graph.LocalInputs.Single(p => p.Path == "fixtures/b");
        CollectionAssert.AreEquivalent(NestedConsumers, input.Consumers);
        Assert.IsTrue(input.IsDirectory);
        Assert.IsEmpty(graph.Projects.Single(p => p.Directory ==
            "packages/unrelated").Dependencies);
    }

    [TestMethod]
    public async Task ReadAssociatesNativePublishCoordinatesAndPeerCopiesWithoutNames()
    {
        var native = new NativeQueries(".", "packages/library", "packages/app",
            "packages/unrelated");
        native.Outputs["packages/library"] = "dist output";
        native.Nodes["packages/app"] = [
            native.Node("file:packages/library/dist output", "node_modules/.pnpm/copy-one"),
            native.Node("file:packages/library/dist output", "node_modules/.pnpm/copy-two"),
            native.Node("link:../library/dist output", "packages/library/dist output"),
        ];

        PnpmGraph graph = await native.Reader.ReadAsync(CancellationToken.None);

        PnpmProject library = graph.Projects.Single(p => p.Directory == "packages/library");
        Assert.AreEqual("packages/library/dist output", library.PublishDirectory);
        Assert.AreEqual("packages/library", Assert.ContainsSingle(
            graph.Projects.Single(p => p.Directory == "packages/app").Dependencies));
        Assert.IsEmpty(graph.Projects.Single(p => p.Directory ==
            "packages/unrelated").Dependencies);
        Assert.IsEmpty(graph.LocalInputs);
    }

    [TestMethod]
    public async Task ReadKeepsArchiveAndNonWorkspaceDirectoryInputsWithTheirConsumers()
    {
        var native = new NativeQueries(".", "packages/publisher", "packages/app", "packages/top");
        object archive = native.Node("1.0.0", "node_modules/.pnpm/publisher-copy",
            "file:fixtures/publisher.tgz");
        object input = native.Node("file:fixtures/local library", "node_modules/.pnpm/local-copy");
        native.Nodes["packages/app"] = [archive, input];
        native.Nodes["packages/top"] = [native.Node("1.0.0", "packages/app",
            children: [archive, input])];
        native.Closures["packages/top"] = ["packages/top", "packages/app"];

        PnpmGraph graph = await native.Reader.ReadAsync(CancellationToken.None);

        Assert.IsEmpty(graph.Projects.Single(p => p.Directory == "packages/app").Dependencies);
        Assert.DoesNotContain("packages/publisher", graph.Projects.Single(
            p => p.Directory == "packages/top").Dependencies);
        Assert.HasCount(2, graph.LocalInputs);
        PnpmLocalInput tarball = graph.LocalInputs.Single(p => p.Path == "fixtures/publisher.tgz");
        Assert.IsFalse(tarball.IsDirectory);
        CollectionAssert.AreEquivalent(LocalConsumers, tarball.Consumers);
        PnpmLocalInput directory = graph.LocalInputs.Single(p => p.Path ==
            "fixtures/local library");
        Assert.IsTrue(directory.IsDirectory);
        CollectionAssert.AreEquivalent(tarball.Consumers, directory.Consumers);
    }

    [TestMethod]
    public async Task ReadPreservesNativePeerClosureAndExcludesNestedUnrelatedProject()
    {
        var native = new NativeQueries(".", "packages/parent", "packages/parent/child",
            "packages/peer");
        native.Closures["packages/parent"] = ["packages/parent", "packages/peer"];

        PnpmGraph graph = await native.Reader.ReadAsync(CancellationToken.None);

        Assert.AreEqual("packages/peer", Assert.ContainsSingle(graph.Projects.Single(
            p => p.Directory == "packages/parent").Dependencies));
        Assert.IsEmpty(graph.Projects.Single(p => p.Directory == ".").Dependencies);
        Assert.HasCount(4, graph.Projects);
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task ReadRejectsUnreferencedDirectoryOwnershipConflicts(bool sharedOutput)
    {
        var native = new NativeQueries(".", "packages/library", "packages/app");
        native.Outputs["packages/app"] = sharedOutput ? "../output" : "../library";
        if (sharedOutput)
            native.Outputs["packages/library"] = "../output";

        await Assert.ThrowsExactlyAsync<InvalidDataException>(
            () => native.Reader.ReadAsync(CancellationToken.None));
    }

    [TestMethod]
    public async Task ReadAllowsProjectToPublishFromItsOwnSourceDirectory()
    {
        var native = new NativeQueries(".", "packages/library");
        native.Outputs["packages/library"] = ".";

        PnpmGraph graph = await native.Reader.ReadAsync(CancellationToken.None);

        PnpmProject library = graph.Projects.Single(p => p.Directory == "packages/library");
        Assert.AreEqual("packages/library", library.PublishDirectory);
        Assert.IsEmpty(library.Dependencies);
        Assert.IsEmpty(graph.LocalInputs);
    }

    [TestMethod]
    [DataRow("file:packages/library", "../library", DisplayName = "Ambiguous output")]
    [DataRow("file:../external", null, DisplayName = "Outside input")]
    [DataRow("file:packages/library(patch_hash=0123456789abcdef)", null)]
    [DataRow("local-library@file:packages/library", null)]
    public async Task ReadRejectsUnsupportedFileDependency(string version, string? output)
    {
        var native = Workspace();
        if (output is not null) native.Outputs["packages/app"] = output;
        native.Nodes["packages/app"] = [native.Node(version, "node_modules/copy")];
        await RejectAsync(native);
    }

    [TestMethod]
    public async Task ReadRejectsUnknownClosureMember()
    {
        var native = Workspace();
        native.Closures["packages/app"] = ["packages/app", "unknown"];
        await RejectAsync(native);
    }

    [TestMethod]
    [DataRow("packages/library", null, DisplayName = "Missing from full listing")]
    [DataRow(null, "packages/library", DisplayName = "Extra in full listing")]
    public async Task ReadRejectsDisagreementBetweenMembershipAndFullListing(
        string? omittedProject, string? extraProject) =>
        await RejectAsync(new NativeQueries(".", "packages/library", "packages/app")
        {
            OmitFullProject = omittedProject,
            ExtraFullProject = extraProject,
        });

    [TestMethod]
    [DataRow("packages/app", DisplayName = "Missing root")]
    [DataRow(".", "packages/[pattern]", DisplayName = "Selector pattern")]
    public async Task ReadRejectsInvalidWorkspaceMembership(params string[] projects) =>
        await RejectAsync(new NativeQueries(projects));

    [TestMethod]
    [DataRow("false", "null", DisplayName = "Dedicated lockfile")]
    [DataRow("null", "\"elsewhere\"", DisplayName = "Relocated lockfile")]
    public async Task ReadRejectsUnsupportedLockfile(string shared, string directory) =>
        await RejectAsync(new NativeQueries(".")
        { SharedLock = shared, LockDirectory = directory });

    [TestMethod]
    [DataRow("pnpmfile", "\"hooks.cjs\"")]
    [DataRow("global-pnpmfile", "\"/g.cjs\"")]
    [DataRow("config-dependencies", "{\"hooks\":\"1.0.0\"}")]
    [DataRow("node-linker", "\"hoisted\"")]
    public async Task ReadRejectsUnsupportedSettings(string setting, string json)
    {
        var native = Workspace();
        native.Settings[setting] = json;
        await RejectAsync(native);
    }

    private static NativeQueries Workspace() => new(".", "packages/library", "packages/app");

    private static Task<InvalidDataException> RejectAsync(NativeQueries native) =>
        Assert.ThrowsExactlyAsync<InvalidDataException>(
            () => native.Reader.ReadAsync(CancellationToken.None));

    [TestMethod]
    [DataRow("null")]
    [DataRow("{}")]
    [DataRow("[{\"path\":null}]")]
    [DataRow("[{\"notPath\":\"missing\"}]")]
    public async Task ReadRejectsMalformedNativeMembership(string output)
    {
        var native = new NativeQueries(".") { MembershipOverride = output };

        await Assert.ThrowsExactlyAsync<InvalidDataException>(
            () => native.Reader.ReadAsync(CancellationToken.None));
    }

    [TestMethod]
    public async Task ReadStopsWhenNativeEvaluationFailsWithoutReturningPartialGraph()
    {
        var native = new NativeQueries(".", "packages/app")
        {
            Failure = new TimeoutException("query deadline"),
        };

        TimeoutException failure = await Assert.ThrowsExactlyAsync<TimeoutException>(
            () => native.Reader.ReadAsync(CancellationToken.None));

        Assert.AreEqual("query deadline", failure.Message);
        Assert.AreEqual(1, native.Calls);
    }

    [TestMethod]
    public async Task ReadPropagatesCancellation()
    {
        using var cancellation = new CancellationTokenSource();
        await cancellation.CancelAsync();
        var native = new NativeQueries(".");

        await Assert.ThrowsAsync<OperationCanceledException>(
            () => native.Reader.ReadAsync(cancellation.Token));

        Assert.AreEqual(0, native.Calls);
    }

    private sealed class NativeQueries(params string[] directories)
    {
        private static readonly JsonSerializerOptions OutputOptions = new() { MaxDepth = 1024 };
        private readonly string root = Path.GetFullPath(Path.Combine(Path.GetTempPath(),
            "pnpm reader fixture"));
        internal List<string> Directories { get; } = [.. directories];
        internal Dictionary<string, string> Outputs { get; } = new(StringComparer.Ordinal);
        internal Dictionary<string, object> Names { get; } = new(StringComparer.Ordinal);
        internal Dictionary<string, object[]> Nodes { get; } = new(StringComparer.Ordinal);
        internal Dictionary<string, object[]> UnsavedNodes { get; } = new(StringComparer.Ordinal);
        internal string DependencyGroup { get; set; } = "dependencies";
        internal Dictionary<string, string[]> Closures { get; } = new(StringComparer.Ordinal);
        internal string SharedLock { get; set; } = "null";
        internal string LockDirectory { get; set; } = "null";
        internal Dictionary<string, string> Settings { get; } = new(StringComparer.Ordinal);
        internal string? OmitFullProject { get; set; }
        internal string? ExtraFullProject { get; set; }
        internal string? MembershipOverride { get; set; }
        internal Exception? Failure { get; set; }
        internal int Calls { get; private set; }
        internal PnpmGraphReader Reader => new(root + Path.DirectorySeparatorChar, Query);

        internal Dictionary<string, object> Node(string version, string path,
            string? resolved = null, object[]? children = null)
        {
            var node = new Dictionary<string, object>
            {
                ["version"] = version,
                ["path"] = Path.GetFullPath(path, root),
                ["from"] = "same-name",
                ["name"] = "same-name",
            };
            if (resolved is not null) node["resolved"] = resolved;
            if (children is not null) node["dependencies"] = Group(children);
            return node;
        }

        private Task<string> Query(string[] arguments, CancellationToken token)
        {
            token.ThrowIfCancellationRequested();
            Calls++;
            if (Failure is not null) throw Failure;
            if (arguments[0] == "config")
                return Task.FromResult(arguments[2] switch
                {
                    "shared-workspace-lockfile" => SharedLock,
                    "lockfile-dir" => LockDirectory,
                    _ => Settings.GetValueOrDefault(arguments[2], "null"),
                });
            if (arguments[0] == "--dir")
            {
                string identity = Path.GetRelativePath(root, arguments[1])
                    .Replace(Path.DirectorySeparatorChar, '/');
                return Task.FromResult(Outputs.TryGetValue(identity, out string? value)
                    ? JsonSerializer.Serialize(value) : "");
            }
            if (arguments.Contains("--filter"))
            {
                Assert.AreEqual("--config.legacy-dir-filtering=false", arguments[0]);
                string selector = arguments[Array.IndexOf(arguments, "--filter") + 1];
                bool closure = selector.EndsWith("...", StringComparison.Ordinal);
                string identity = closure ? selector[3..^4] : selector[3..^1];
                if (closure)
                {
                    Assert.AreEqual("-1", arguments[^1]);
                    return Task.FromResult(Projects(Closures.GetValueOrDefault(identity,
                        [identity]), false));
                }
                Assert.AreEqual("Infinity", arguments[^1]);
                List<string> full = identity == OmitFullProject ? [] : [identity];
                if (ExtraFullProject is not null) full.Add(ExtraFullProject);
                return Task.FromResult(Projects(full, true));
            }
            Assert.Contains("--lockfile-only", arguments);
            Assert.AreEqual("-1", arguments[^1], "Full native trees require singleton selection.");
            return Task.FromResult(MembershipOverride ?? Projects(Directories, false));
        }

        private string Projects(IEnumerable<string> identities, bool full) =>
            JsonSerializer.Serialize(identities.Select(identity =>
            {
                var project = new Dictionary<string, object>
                {
                    ["path"] = Path.GetFullPath(identity, root),
                    [DependencyGroup] = full ? Group(Nodes.GetValueOrDefault(identity, []))
                        : new Dictionary<string, object>(),
                    ["unsavedDependencies"] = full
                        ? Group(UnsavedNodes.GetValueOrDefault(identity, []))
                        : new Dictionary<string, object>(),
                };
                if (Names.TryGetValue(identity, out object? name)) project["name"] = name;
                return project;
            }), OutputOptions);

        private static Dictionary<string, object> Group(object[] nodes) =>
            nodes.Select((node, index) => (node, index)).ToDictionary(p => "alias-" + p.index,
                p => p.node, StringComparer.Ordinal);
    }

    [TestMethod]
    [DataRow("base", "dist output", 27, true)]
    [DataRow("candidate", "build output", 26, false)]
    public async Task ReadRecordedNativeIdentitiesPreservesRelationsAcrossRevisions(
        string revision, string publishDirectory, int projectCount, bool topConsumesPublisher)
    {
        using Stream resource = typeof(PnpmGraphReaderTests).Assembly.GetManifestResourceStream(
            $"WorkflowDelivery.Tests.Repository.fixtures.{revision}.json")!;
        using var text = new StreamReader(resource);
        string root = Path.GetFullPath(Path.Combine(Path.GetTempPath(), "recorded pnpm workspace"));
        string escapedRoot = JsonSerializer.Serialize(root)[1..^1];
        using JsonDocument fixture = JsonDocument.Parse((await text.ReadToEndAsync(
            context.CancellationToken))
            .Replace("__WORKSPACE__", escapedRoot, StringComparison.Ordinal));
        JsonElement recorded = fixture.RootElement;
        var reader = new PnpmGraphReader(root, Query);

        PnpmGraph graph = await reader.ReadAsync(context.CancellationToken);

        Assert.HasCount(projectCount, graph.Projects);
        Assert.AreEqual("packages/publisher/" + publishDirectory,
            graph.Projects.Single(p => p.Directory == "packages/publisher").PublishDirectory);
        foreach (string consumer in new[] { "publish-link", "publish-workspace", "publish-alias" })
            Assert.Contains("packages/publisher", graph.Projects.Single(
                p => p.Directory == "packages/" + consumer).Dependencies);
        foreach (string consumer in new[] { "peer-consumer-one", "peer-consumer-two" })
            Assert.Contains("packages/peer-lib", graph.Projects.Single(
                p => p.Directory == "packages/" + consumer).Dependencies);
        Assert.AreEqual(topConsumesPublisher, graph.Projects.Single(
            p => p.Directory == "packages/top").Dependencies.Contains("packages/publisher"));
        Assert.AreEqual(topConsumesPublisher, graph.Projects.Any(p => p.Directory ==
            "packages/deleted"));
        Assert.IsEmpty(graph.Projects.Single(p => p.Directory ==
            "packages/unrelated").Dependencies);
        Assert.DoesNotContain("packages/publisher", graph.Projects.Single(
            p => p.Directory == "packages/tarball-consumer").Dependencies);
        PnpmLocalInput archive = graph.LocalInputs.Single(p => p.Path == "fixtures/publisher.tgz");
        Assert.IsFalse(archive.IsDirectory);
        Assert.AreEqual("packages/tarball-consumer", Assert.ContainsSingle(archive.Consumers));
        Assert.IsTrue(graph.LocalInputs.Single(p => p.Path == "fixtures/peer-one").IsDirectory);

        Task<string> Query(string[] arguments, CancellationToken token)
        {
            token.ThrowIfCancellationRequested();
            // Unrecorded settings, metadata and selectors use controlled substitutes.
            if (arguments[0] == "config") return Task.FromResult("null");
            if (arguments[0] == "--dir")
            {
                string identity = Path.GetRelativePath(root, arguments[1])
                    .Replace(Path.DirectorySeparatorChar, '/');
                return Task.FromResult(recorded.GetProperty("publishMetadata")
                    .TryGetProperty(identity, out JsonElement metadata)
                    ? metadata.GetString()! : "");
            }
            if (arguments.Contains("--filter"))
            {
                string selector = arguments[Array.IndexOf(arguments, "--filter") + 1];
                bool isClosure = selector.EndsWith("...", StringComparison.Ordinal);
                string identity = isClosure ? selector[3..^4] : selector[3..^1];
                if (isClosure && recorded.GetProperty("closures").TryGetProperty(identity,
                    out JsonElement closure))
                    return Task.FromResult(closure.GetRawText());
                JsonElement project = recorded.GetProperty("listing").EnumerateArray().Single(p =>
                    Path.GetRelativePath(root, p.GetProperty("path").GetString()!)
                        .Replace(Path.DirectorySeparatorChar, '/') == identity);
                return Task.FromResult("[" + project.GetRawText() + "]");
            }
            return Task.FromResult(recorded.GetProperty("listing").GetRawText());
        }
    }
}
