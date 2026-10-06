using System.Text.Json;
using WorkflowDelivery.CI;
using WorkflowDelivery.Platform;
using WorkflowDelivery.Repository;

namespace WorkflowDelivery.Tests.Repository;

[TestClass]
public sealed class DotNetRepositoryReaderTests(TestContext context)
{
    private static readonly string[] Frameworks = ["net10.0", "net9.0"];
    private static readonly string[] Projects = ["product/A.csproj", "product/B.csproj"];
    [TestMethod]
    public async Task ReadPreservesNativeVariantsAndOriginalGitCoordinates()
    {
        using var fixture = await DotNetEndpointFixture.CreateAsync(context.CancellationToken);
        NativeCommand? observed = null;
        var files = new DotNetReadFiles(typeof(DotNetGraphTask).Assembly.Location,
            Path.Combine(fixture.Scratch, "read.proj"),
            Path.Combine(fixture.Scratch, "request.json"),
            Path.Combine(fixture.Scratch, "response.json"), 30);
        var reader = new DotNetRepositoryReader(fixture.Checkout, files, async (command, token) =>
        {
            observed = command;
            await File.WriteAllTextAsync(files.Response, JsonSerializer.Serialize(fixture.Graph,
                TransferJson.Default.DotNetGraphResponse), token);
            return new(NativeTermination.Exited, 0, "", "", 0, null);
        });
        DotNetRevisionInputs result = await reader.ReadAsync(context.CancellationToken);

        Assert.HasCount(3, result.Nodes.Where(node => node.Project == "product/A.csproj"));
        CollectionAssert.AreEquivalent(Frameworks, result.Nodes.Where(node =>
            node.Project == "product/A.csproj" && !node.Native.OuterBuild)
            .Select(node => node.Native.Identity.Globals["TargetFramework"]).ToArray());
        Assert.Contains("product/code.cs", result.Nodes.SelectMany(node => node.OwnedPaths));
        Assert.Contains("linked/data.txt", result.Inputs.Select(input => input.Path));
        Assert.IsNotNull(observed);
        Assert.AreEqual(fixture.Root, observed.Directory);
        CollectionAssert.AreEqual(new[] { "msbuild", files.Wrapper, "-nologo", "-noAutoResponse",
            "-target:Read" }, observed.Arguments);
        DotNetGraphRequest request = JsonSerializer.Deserialize(await File.ReadAllTextAsync(
            files.Request, context.CancellationToken), TransferJson.Default.DotNetGraphRequest)!;
        Assert.AreEqual(fixture.Revision.Commit, request.Revision);
        Assert.HasCount(3, request.Globals);
        Assert.Contains(fixture.PathOf("product/A.csproj"), request.CommittedPaths);
        Assert.IsFalse(request.Globals.Values.Contains(files.Wrapper, StringComparer.Ordinal));
        Assert.IsTrue(result.Inputs.All(input =>
            input.Path is null || !Path.IsPathRooted(input.Path)));
    }

    [TestMethod]
    [DataRow("revision")]
    [DataRow("root")]
    [DataRow("entry")]
    [DataRow("duplicate-node")]
    [DataRow("duplicate-effective-node")]
    [DataRow("missing-globals")]
    [DataRow("ambiguous-globals")]
    [DataRow("dangling-edge")]
    [DataRow("substituted-edge")]
    [DataRow("dangling-consumer")]
    [DataRow("unknown-role")]
    [DataRow("unknown-stage")]
    [DataRow("missing-ownership")]
    [DataRow("traversal-ownership")]
    [DataRow("missing-inputs")]
    [DataRow("missing-project-input")]
    public async Task ReadRejectsSubstitutedNativeFacts(string defect)
    {
        using var fixture = await DotNetEndpointFixture.CreateAsync(context.CancellationToken);
        DotNetGraphResponse graph = fixture.Graph;
        DotNetNodeIdentity consumer = graph.Nodes[1].Identity;
        DotNetNodeIdentity absent = fixture.Identity("product/A.csproj", "net8.0");
        graph = defect switch
        {
            "revision" => graph with { Revision = new string('f', 40) },
            "root" => graph with { Root = fixture.Scratch },
            "entry" => graph with { Entry = fixture.PathOf("product/A.csproj") },
            "duplicate-node" => graph with { Nodes = [.. graph.Nodes, graph.Nodes[1]] },
            "duplicate-effective-node" => graph with
            {
                Nodes = [.. graph.Nodes,
                graph.Nodes[1] with { Identity = consumer with { Globals = consumer.Globals
                    .ToDictionary(pair => pair.Key.ToUpperInvariant(), pair => pair.Value,
                        StringComparer.Ordinal) } }]
            },
            "missing-globals" => graph with
            {
                Nodes = [graph.Nodes[0] with
                { Identity = graph.Nodes[0].Identity with { Globals = null! } },
                    .. graph.Nodes[1..]]
            },
            "ambiguous-globals" => graph with
            {
                Nodes = [graph.Nodes[0] with
                { Identity = graph.Nodes[0].Identity with { Globals = new(consumer.Globals)
                    { ["configuration"] = "Debug" } } }, .. graph.Nodes[1..]]
            },
            "dangling-edge" => graph with
            {
                Edges = [.. graph.Edges, new(consumer,
                fixture.Identity("missing.csproj"))]
            },
            "substituted-edge" => graph with { Edges = [.. graph.Edges, new(consumer, absent)] },
            "dangling-consumer" => graph with
            {
                Inputs = [.. graph.Inputs,
                new(fixture.PathOf("linked/data.txt"), "Content", "Evaluation", absent)]
            },
            "unknown-role" => graph with
            {
                Inputs = [.. graph.Inputs,
                new(fixture.PathOf("linked/data.txt"), "CustomInput", "Evaluation", consumer)]
            },
            "unknown-stage" => graph with
            {
                Inputs = [.. graph.Inputs,
                new(fixture.PathOf("linked/data.txt"), "Content", "Build", consumer)]
            },
            "missing-ownership" => graph with
            {
                Nodes = [graph.Nodes[0], graph.Nodes[1] with
                { OwnedPaths = [fixture.PathOf("missing.txt")] }, .. graph.Nodes[2..]]
            },
            "traversal-ownership" => graph with
            {
                Nodes = [graph.Nodes[0] with
                { OwnedPaths = [fixture.PathOf("product/code.cs")] }, .. graph.Nodes[1..]]
            },
            "missing-inputs" => graph with { Inputs = null! },
            "missing-project-input" => graph with
            {
                Inputs = graph.Inputs.Where(input =>
                input.Consumer != consumer).ToArray()
            },
            _ => throw new ArgumentOutOfRangeException(nameof(defect)),
        };
        await Assert.ThrowsExactlyAsync<InvalidDataException>(() =>
            fixture.Reader().BindAsync(graph, context.CancellationToken));
    }

    [TestMethod]
    [DataRow("untracked")]
    [DataRow("missing-ordinary")]
    [DataRow("unknown-provider")]
    [DataRow("untracked-lock")]
    public async Task ReadRejectsUntrackedSourceAndUnknownProviders(string defect)
    {
        using var fixture = await DotNetEndpointFixture.CreateAsync(context.CancellationToken);
        string path = fixture.PathOf("linked/.globalconfig");
        if (defect != "missing-ordinary")
            await File.WriteAllTextAsync(path, "uncommitted", context.CancellationToken);
        string role = defect switch
        {
            "untracked" => "GlobalAnalyzerConfigFiles",
            "untracked-lock" => "RestoreLock",
            _ => "Content",
        };
        DotNetInputProvider? provider = defect == "unknown-provider"
            ? new("Custom", fixture.Root, "custom", "") : null;
        DotNetGraphInput input = new(path, role, defect == "untracked-lock" ?
            "LockedRestore" : "Evaluation", fixture.Graph.Nodes[1].Identity, provider);
        fixture.Graph = fixture.Graph with { Inputs = [.. fixture.Graph.Inputs, input] };

        InvalidDataException failure = await Assert.ThrowsExactlyAsync<InvalidDataException>(
            fixture.BindAsync);
        Assert.Contains(role, failure.Message);
        Assert.Contains(fixture.Revision.Commit, failure.Message);
        Assert.Contains(fixture.Root, failure.Message);
    }

    [TestMethod]
    public async Task ReadUsesNativeVersionAndCommittedQualitySelection()
    {
        using var fixture = await DotNetEndpointFixture.CreateAsync(context.CancellationToken);
        await fixture.Repo.SetAsync("product/workflow-delivery.quality.yml", """
            schema: workflow-delivery/v3/quality-selection
            ecosystems:
              dotnet:
                preset: dotnet/hcoona-release-smoke-github-packages-v1
            """);
        await fixture.CommitAsync();
        fixture.Graph = fixture.Response();
        DotNetRevisionInputs inputs = await fixture.BindAsync();
        Assert.HasCount(1, inputs.Versions);
        Assert.AreEqual(fixture.Revision.Commit, inputs.Versions[0].Commit);
        RepositoryFacts facts = DotNetFactsAssembler.Assemble(inputs, []);
        Assert.HasCount(2, facts.Projects);
        Assert.IsTrue(facts.Projects.All(project => project.Origin == CheckOrigin.Preset &&
            project.QualityPreset == "dotnet/hcoona-release-smoke-github-packages-v1" &&
            project.Checks.Length == 0));
        SharedInput version = Assert.ContainsSingle(facts.SharedInputs.Where(input =>
            input.Path == "version.json"));
        CollectionAssert.AreEquivalent(Projects,
            version.Consumers);
        Assert.Contains("product/workflow-delivery.quality.yml", facts.SharedInputs
            .Select(input => input.Path));
        Assert.IsFalse(facts.SharedInputs.Any(input =>
            input.Path == "workflow-delivery.quality.yml"));
    }

    [TestMethod]
    public async Task ReadRejectsInvalidAdoptedQualityWithoutNativeFallback()
    {
        using var fixture = await DotNetEndpointFixture.CreateAsync(context.CancellationToken);
        await fixture.Repo.SetAsync("product/workflow-delivery.quality.yml", "presets: []");
        await fixture.CommitAsync();
        fixture.Graph = fixture.Response();
        await Assert.ThrowsExactlyAsync<InvalidDataException>(fixture.BindAsync);
    }

    [TestMethod]
    [DataRow("project")]
    [DataRow("input")]
    public async Task ReadRejectsRelevantNonregularGitSource(string role)
    {
        using var fixture = await DotNetEndpointFixture.CreateAsync(context.CancellationToken);
        string path = role == "project" ? "product/A.csproj" : "linked/data.txt";
        await fixture.Repo.SetAsync(path, "code.cs", "120000");
        await fixture.CommitAsync();
        fixture.Graph = fixture.Response();
        InvalidDataException failure = await Assert.ThrowsExactlyAsync<InvalidDataException>(
            fixture.BindAsync);
        Assert.Contains("regular committed source", failure.Message);
        Assert.Contains(path.Replace('/', Path.DirectorySeparatorChar), failure.Message);
    }

    [TestMethod]
    public async Task ReadCancellationAndNativeFailureReturnNoFacts()
    {
        using var fixture = await DotNetEndpointFixture.CreateAsync(context.CancellationToken);
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(
            context.CancellationToken);
        await cancellation.CancelAsync();
        await Assert.ThrowsAsync<OperationCanceledException>(() => fixture.Reader().BindAsync(
            fixture.Graph, cancellation.Token));
        var files = new DotNetReadFiles(typeof(DotNetGraphTask).Assembly.Location,
            Path.Combine(fixture.Scratch, "failed.proj"),
            Path.Combine(fixture.Scratch, "failed.request.json"),
            Path.Combine(fixture.Scratch, "failed.response.json"), 30);
        var reader = new DotNetRepositoryReader(fixture.Checkout, files, (_, _) =>
            Task.FromResult(new NativeCommandResult(NativeTermination.Exited, 1,
                "", "task load failed", 0, null)));
        InvalidDataException failure = await Assert.ThrowsExactlyAsync<InvalidDataException>(() =>
            reader.ReadAsync(context.CancellationToken));
        Assert.Contains("task load failed", failure.Message);
        Assert.Contains(fixture.Revision.Commit, failure.Message);
        Assert.IsFalse(File.Exists(files.Response));
    }

    [TestMethod]
    [Timeout(60000, CooperativeCancellation = true)]
    public async Task ReadNativePreparedExactGitEndpoint()
    {
        using var fixture = await DotNetEndpointFixture.CreateAsync(context.CancellationToken);
        await fixture.Repo.SetAsync("dirs.proj", """
            <Project><ItemGroup><ProjectReference Include="product/A.csproj" />
              <ProjectReference Include="product/B.csproj" /></ItemGroup></Project>
            """);
        await fixture.Repo.SetAsync("global.json", """
            {"sdk":{"version":"10.0.401","rollForward":"disable"}}
            """);
        await fixture.Repo.SetAsync("nuget.config", """
            <configuration><packageSources><clear />
              <add key="nuget.org" value="https://api.nuget.org/v3/index.json" />
            </packageSources></configuration>
            """);
        await fixture.Repo.SetAsync("Directory.Build.props", """
            <Project><PropertyGroup>
              <BaseIntermediateOutputPath>obj/$(MSBuildProjectName)/</BaseIntermediateOutputPath>
              <RestorePackagesWithLockFile>true</RestorePackagesWithLockFile>
              <NuGetLockFilePath>packages.$(MSBuildProjectName).lock.json</NuGetLockFilePath>
              <NuGetAudit>false</NuGetAudit>
            </PropertyGroup></Project>
            """);
        await fixture.Repo.SetAsync("product/A.csproj", """
            <Project Sdk="Microsoft.NET.Sdk"><PropertyGroup>
              <TargetFrameworks>net10.0;net9.0</TargetFrameworks>
              <DefaultItemExcludes>$(DefaultItemExcludes);fixtures/**</DefaultItemExcludes>
            </PropertyGroup><ItemGroup><ProjectReference Include="B.csproj" />
              <Compile Include="../linked/source.cs" />
              <EmbeddedResource Include="../linked/data.txt" />
            </ItemGroup></Project>
            """);
        await fixture.Repo.SetAsync("product/B.csproj", """
            <Project Sdk="Microsoft.NET.Sdk"><PropertyGroup>
              <TargetFramework>net9.0</TargetFramework>
              <DefaultItemExcludes>$(DefaultItemExcludes);fixtures/**</DefaultItemExcludes>
            </PropertyGroup></Project>
            """);
        await fixture.Repo.SetAsync("linked/source.cs", "");
        await fixture.CommitAsync();
        foreach (string project in new[] { "A", "B" })
        {
            foreach (string mode in new[] { "--use-lock-file", "--locked-mode" })
            {
                NativeCommandResult restored = await NativeProcess.ExecuteAsync(new("dotnet",
                    fixture.Root, ["restore", fixture.PathOf("product/" + project + ".csproj"),
                        mode, "-property:Configuration=Debug",
                        "-property:ContinuousIntegrationBuild=true"], 30),
                    context.CancellationToken);
                Assert.IsTrue(restored.Succeeded,
                    restored.Stdout + restored.Stderr + restored.Error);
            }
            string path = "product/packages." + project + ".lock.json";
            await fixture.Repo.SetAsync(path, await File.ReadAllTextAsync(fixture.PathOf(path),
                context.CancellationToken));
        }
        await fixture.CommitAsync();

        // Separate obj subdirectories are not blanket native-provider authority:
        // the sibling's SDK default None glob still sees another project's outputs.
        InvalidDataException untracked = await Assert.ThrowsExactlyAsync<InvalidDataException>(() =>
            fixture.Reader().ReadAsync(context.CancellationToken));
        Assert.Contains("role None", untracked.Message);
        Assert.Contains("/obj/", untracked.Message.Replace('\\', '/'));
        string props = await File.ReadAllTextAsync(fixture.PathOf("Directory.Build.props"),
            context.CancellationToken);
        await fixture.Repo.SetAsync("Directory.Build.props", props.Replace("</PropertyGroup>",
            "<DefaultItemExcludes>$(DefaultItemExcludes);obj/**</DefaultItemExcludes>" +
            "</PropertyGroup>", StringComparison.Ordinal));
        await fixture.CommitAsync();

        DotNetRevisionInputs inputs = await fixture.Reader().ReadAsync(context.CancellationToken);
        DotNetSelection selection = DotNetFactsAssembler.Select(inputs, inputs,
            ["linked/data.txt"], false, [], []);

        Assert.AreEqual(fixture.Revision.Commit, inputs.Graph.Revision);
        Assert.HasCount(3, selection.CandidateNodes["product/A.csproj"]);
        Assert.AreEqual("product/A.csproj", Assert.ContainsSingle(selection.Reasons.Keys));
        foreach (string role in new[] { "PotentialEditorConfigFiles", "GlobalAnalyzerConfigFiles" })
            Assert.IsTrue(inputs.Inputs.Any(input => input.Native.Role == role &&
                input.Path == "linked/" + (role == "PotentialEditorConfigFiles"
                    ? ".editorconfig" : ".globalconfig")));
        Assert.IsTrue(inputs.Inputs.Any(input => input.Native.Provider?.Kind == "RestoreGenerated"
            && input.Path is null));
        Assert.IsTrue(selection.Request.Candidate.SharedInputs.All(input =>
            !input.Path.Contains("/obj/", StringComparison.Ordinal)));
        Assert.IsFalse(selection.Request.Candidate.Projects.Any(project =>
            project.Id == "dirs.proj"));
    }
}
