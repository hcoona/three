using WorkflowDelivery.Repository;
using WorkflowDelivery.Platform;
using GitFixture = WorkflowDelivery.Tests.Repository.GitReaderTests.GitFixture;

namespace WorkflowDelivery.Tests.Repository;

[TestClass]
public sealed class ControlNativeInputTests(TestContext context)
{
    private static GitFixture repository = null!;
    private static GitMaterialization checkout = null!;
    private const string Project = NodeGroupReader.ControlProject;
    private const string Source = "src/private/app/workflow-delivery/Program.cs";
    private const string Copy = "src/private/app/workflow-delivery/settings.json";
    private const string Documentation = "src/private/app/workflow-delivery/docs/design.md";
    private const string Helper = "src/private/app/workflow-delivery/Native/Python/native.py";

    [ClassInitialize]
    public static async Task Initialize(TestContext _)
    {
        repository = await GitFixture.CreateAsync(CancellationToken.None);
        foreach (string path in new[] { Project, Source, Copy, Documentation, Helper,
            "Directory.Build.props", "version.json" })
            await repository.SetAsync(path);
        string revision = await repository.CommitAsync();
        await repository.GitAsync("reset", "--hard", revision);
        checkout = await GitMaterialization.BindAsync(repository.Directory,
            await new GitReader(repository.Directory).ReadAsync("HEAD", CancellationToken.None),
            CancellationToken.None);
    }

    [ClassCleanup]
    public static void Cleanup() => repository.Dispose();

    [TestMethod]
    [DataRow("same")]
    [DataRow("revision")]
    [DataRow("root")]
    [DataRow("entry")]
    public void NativeNestedEntryKeepsExactSubject(string subject)
    {
        using var row = new Row();
        DotNetGraphResponse graph = row.Graph with { Entry = Path.GetFullPath(row.Graph.Entry) };
        graph = subject switch
        {
            "revision" => graph with { Revision = new string('f', 40) },
            "root" => graph with { Root = Path.GetTempPath() },
            "entry" => graph with { Entry = Path.GetFullPath(Path.Combine(checkout.Root, Source)) },
            _ => graph,
        };
        var reader = new DotNetNativeGraphReader(checkout, new("", "", "", "", 30));
        if (subject != "same")
        {
            Assert.ThrowsExactly<InvalidDataException>(() => reader.Bind(graph, Project,
                context.CancellationToken));
            return;
        }
        DotNetBoundGraph bound = reader.Bind(graph, Project, context.CancellationToken);
        Assert.AreEqual(Project, Assert.ContainsSingle(bound.Nodes).Project);
        Assert.Contains(Source, bound.Inputs.Select(input => input.Path));
    }

    [TestMethod]
    public void NativeInputsRetainCompilerCopyConfigurationAndAbsentCandidates()
    {
        using var row = new Row();
        ControlNativeInputs result = row.Read();
        foreach (string path in new[] { Source, Copy, Project, "Directory.Build.props",
            "version.json", "src/private/app/workflow-delivery/.editorconfig",
            "src/private/app/workflow-delivery/runtimeconfig.template.json" })
            Assert.Contains(path, result.Sources.Select(input => input.Path));
        Assert.DoesNotContain(Documentation, result.Sources.Select(input => input.Path));
        Assert.DoesNotContain(Helper, result.Sources.Select(input => input.Path));
        Assert.IsFalse(result.Sources.Single(input => input.Role ==
            "UserRuntimeConfigCandidate").Present);
        Assert.AreEqual(checkout.Revision.Commit, result.Operation.Revision);
        Assert.AreEqual(row.Output, result.Operation.TargetFile);
    }

    [TestMethod]
    [DataRow("")]
    [DataRow("Never")]
    [DataRow("never")]
    public void PassiveContentDoesNotBecomeAControlDependency(string mode)
    {
        using var row = new Row();
        DotNetGraphInput[] changed = row.Graph.Inputs.Select(input => input.Role == "Content"
            ? input with { CopyToOutputDirectory = mode } : input).ToArray();
        row.Graph = row.Graph with { Inputs = changed };
        Assert.DoesNotContain(Copy, row.Read().Sources.Select(input => input.Path));
        Assert.Contains(Source, row.Read().Sources.Select(input => input.Path));
    }

    [TestMethod]
    [DataRow("Always")]
    [DataRow("PreserveNewest")]
    [DataRow("IfDifferent")]
    public void NativeCopyModesSelectTheirSource(string mode)
    {
        using var row = new Row();
        row.Graph = row.Graph with { Inputs = row.Graph.Inputs.Select(input =>
            input.Role == "Content" ? input with { CopyToOutputDirectory = mode } : input)
            .ToArray() };
        Assert.Contains(Copy, row.Read().Sources.Select(input => input.Path));
    }

    [TestMethod]
    [DataRow("revision")]
    [DataRow("root")]
    [DataRow("entry")]
    [DataRow("output")]
    [DataRow("missing-output")]
    [DataRow("framework")]
    [DataRow("outer")]
    [DataRow("rid")]
    [DataRow("sdk")]
    [DataRow("project-reference")]
    [DataRow("missing-project-input")]
    [DataRow("unknown-provider")]
    public void UnsupportedOrSubstitutedNativeFactsFail(string failure)
    {
        using var row = new Row();
        DotNetGraphNode node = row.Graph.Nodes[0];
        row.Graph = failure switch
        {
            "revision" => row.Graph with { Revision = new string('f', 40) },
            "root" => row.Graph with { Root = Path.GetTempPath() },
            "entry" => row.Graph with { Entry = Path.Combine(checkout.Root, Source) },
            "output" => row.Graph with { Nodes = [node with { TargetFile =
                Path.Combine(checkout.Root, "other.dll") }] },
            "framework" => row.Graph with { Nodes = [node with { TargetFrameworks =
                "net10.0;net9.0" }] },
            "outer" => row.Graph with { Nodes = [node with { OuterBuild = true }] },
            "rid" => row.Graph with { Nodes = [node with { RuntimeIdentifiers = "win-x64" }] },
            "sdk" => row.Graph with { Nodes = [node with { SdkVersion = "" }] },
            "project-reference" => row.Graph with { Edges = [new(node.Identity, node.Identity)] },
            "missing-project-input" => row.Graph with { Inputs = row.Graph.Inputs
                .Where(input => input.Role != "Project").ToArray() },
            "unknown-provider" => row.Graph with { Inputs = [.. row.Graph.Inputs,
                new(Path.Combine(row.Directory, "unknown.dll"), "Analyzer", "Evaluation",
                    node.Identity)] },
            _ => row.Graph,
        };
        if (failure == "missing-output") File.Delete(row.Output);
        if (failure == "unknown-provider")
            File.WriteAllText(Path.Combine(row.Directory, "unknown.dll"), "unbound");
        Assert.ThrowsExactly<InvalidDataException>(() => row.Read());
    }

    [TestMethod]
    public void UncommittedOptionalConfigurationIsNotAbsence()
    {
        using var row = new Row();
        string candidate = Path.Combine(row.Directory, "optional.json");
        File.WriteAllText(candidate, "untracked");
        Assert.ThrowsExactly<InvalidDataException>(() => row.Read(
            [Path.GetRelativePath(checkout.Root, candidate).Replace('\\', '/') ]));
    }

    [TestMethod]
    public void RemovedInputKeepsOnlyItsActualEndpointConsumer()
    {
        using var before = new Row();
        using var after = new Row();
        after.Graph = after.Graph with { Inputs = after.Graph.Inputs.Where(input =>
            input.Role != "Content").ToArray() };
        Assert.Contains(Copy, before.Read().Sources.Select(input => input.Path));
        Assert.DoesNotContain(Copy, after.Read().Sources.Select(input => input.Path));
    }

    [TestMethod]
    public void CancelledProjectionDoesNotProduceInputs()
    {
        using var row = new Row();
        using var cancelled = new CancellationTokenSource();
        cancelled.Cancel();
        Assert.ThrowsExactly<OperationCanceledException>(() => row.Read(token: cancelled.Token));
    }

    [TestMethod]
    [Timeout(60000, CooperativeCancellation = true)]
    public async Task NativePreparedControlPreservesCopyRuntimeAndRestoreInputs()
    {
        using var repo = await GitFixture.CreateAsync(context.CancellationToken);
        string scratch = Path.Combine(Path.GetTempPath(), "control-native-" +
            Guid.NewGuid().ToString("N"));
        System.IO.Directory.CreateDirectory(scratch);
        try
        {
            await repo.SetAsync("global.json", """
                {"sdk":{"version":"10.0.401","rollForward":"disable"}}
                """);
            await repo.SetAsync("version.json", """{"version":"1.0"}""");
            await repo.SetAsync("nuget.config", """
                <configuration><packageSources><clear /></packageSources></configuration>
                """);
            await repo.SetAsync(Project, """
                <Project Sdk="Microsoft.NET.Sdk">
                  <PropertyGroup>
                    <TargetFramework>net10.0</TargetFramework><OutputType>Exe</OutputType>
                    <RestorePackagesWithLockFile>true</RestorePackagesWithLockFile>
                  </PropertyGroup>
                  <ItemGroup>
                    <Content Include="settings.json" CopyToOutputDirectory="PreserveNewest" />
                  </ItemGroup>
                </Project>
                """);
            await repo.SetAsync(Source, "System.Console.WriteLine(1);");
            await repo.SetAsync(Copy, "{}");
            await repo.SetAsync(Documentation, "unrelated documentation");
            await repo.SetAsync(Helper, "# independent helper");
            string template = "src/private/app/workflow-delivery/runtimeconfig.template.json";
            await repo.SetAsync(template, "{}");
            string config = Path.Combine(repo.Directory, "nuget.config");
            await repo.CommitAsync();
            await repo.GitAsync("reset", "--hard", "HEAD");
            string[] globals = ControlNativeInputReader.Globals().Select(pair =>
                "-property:" + pair.Key + "=" + pair.Value).ToArray();
            NativeCommandResult restore = await NativeProcess.ExecuteAsync(new("dotnet",
                repo.Directory, ["restore", Project, "--configfile", config,
                    "-p:RestoreLockedMode=false", "-p:ContinuousIntegrationBuild=true"], 30),
                context.CancellationToken);
            Assert.IsTrue(restore.Succeeded, restore.ToString());
            string lockFile = "src/private/app/workflow-delivery/packages.lock.json";
            await repo.SetAsync(lockFile, await File.ReadAllTextAsync(Path.Combine(repo.Directory,
                lockFile), context.CancellationToken));
            await repo.CommitAsync();
            await repo.GitAsync("reset", "--hard", "HEAD");
            NativeCommandResult locked = await NativeProcess.ExecuteAsync(new("dotnet",
                repo.Directory, ["restore", Project, "--locked-mode", "--configfile", config,
                    .. globals], 30), context.CancellationToken);
            Assert.IsTrue(locked.Succeeded, locked.ToString());
            NativeCommandResult built = await NativeProcess.ExecuteAsync(new("dotnet",
                repo.Directory, ["msbuild", Project, "-nologo", "-noAutoResponse", "-target:Build",
                    "-getProperty:TargetPath", .. globals], 30), context.CancellationToken);
            Assert.IsTrue(built.Succeeded, built.ToString());
            string target = built.Stdout.Trim();
            GitRevision revision = await new GitReader(repo.Directory).ReadAsync("HEAD",
                context.CancellationToken);
            GitMaterialization endpoint = await GitMaterialization.BindAsync(repo.Directory,
                revision, context.CancellationToken);
            var files = new ControlReadFiles(new(typeof(DotNetGraphTask).Assembly.Location,
                Path.Combine(scratch, "read.proj"), Path.Combine(scratch, "request.json"),
                Path.Combine(scratch, "response.json"), 30), target);
            ControlNativeInputs result = await new ControlNativeInputReader(endpoint, files)
                .ReadAsync(context.CancellationToken);
            foreach (string path in new[] { Source, Copy, Project, template, lockFile,
                "version.json", "global.json" })
                Assert.Contains(path, result.Sources.Select(input => input.Path));
            Assert.DoesNotContain(Documentation, result.Sources.Select(input => input.Path));
            Assert.DoesNotContain(Helper, result.Sources.Select(input => input.Path));
            Assert.AreEqual(target, result.Operation.TargetFile);
            Assert.Contains("nuget.config", result.Sources.Select(input => input.Path));
        }
        finally { System.IO.Directory.Delete(scratch, true); }
    }

    private sealed class Row : IDisposable
    {
        internal string Directory { get; } = Path.Combine(checkout.Root, "obj",
            Guid.NewGuid().ToString("N"));
        internal string Output => Path.Combine(Directory, "control.dll");
        internal DotNetGraphResponse Graph { get; set; }

        internal Row()
        {
            System.IO.Directory.CreateDirectory(Directory);
            File.WriteAllText(Output, "prepared");
            var identity = new DotNetNodeIdentity(Path.Combine(checkout.Root, Project),
                ControlNativeInputReader.Globals());
            var node = new DotNetGraphNode(identity, false, new("Debug", "net10.0"),
                "", "", [], "None", TargetFile: Output, SdkVersion: "10.0.401");
            DotNetGraphInput Input(string path, string role, string copy = "") =>
                new(Path.Combine(checkout.Root, path), role, "Evaluation", identity,
                    CopyToOutputDirectory: copy);
            Graph = new(checkout.Revision.Commit, checkout.Root, identity.Project, [node], [],
                [Input(Project, "Project"), Input(Source, "Compile"),
                    Input("Directory.Build.props", "Import"), Input(Copy, "Content", "Always"),
                    Input(Documentation, "None"), Input(Helper, "None"),
                    Input("src/private/app/workflow-delivery/.editorconfig",
                        "PotentialEditorConfigFiles"),
                    Input("src/private/app/workflow-delivery/runtimeconfig.template.json",
                        "UserRuntimeConfigCandidate")]);
        }

        internal ControlNativeInputs Read(string[]? candidates = null,
            CancellationToken token = default)
        {
            var reader = new DotNetNativeGraphReader(checkout, new("", "", "", "", 30));
            return ControlNativeInputReader.Project(checkout,
                reader.Bind(Graph, Project, token), Output, candidates ?? ["version.json"], token);
        }

        public void Dispose() => System.IO.Directory.Delete(Directory, true);
    }
}
