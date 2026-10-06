using System.Security;
using System.Text.Json;
using WorkflowDelivery.Platform;
using WorkflowDelivery.Repository;

namespace WorkflowDelivery.Tests.Repository;

[TestClass]
public sealed class DotNetGraphTaskTests(TestContext context)
{
    [TestMethod]
    [Timeout(30000, CooperativeCancellation = true)]
    public async Task LoadedNativeGraphPreservesInnerVariantsAndReferenceEdges()
    {
        using var fixture = new GraphFixture();
        fixture.Globals.Add("BusinessSentinel", "semantic");
        fixture.Write("dirs.proj", """
            <Project>
              <ItemGroup><ProjectReference Include="consumer.csproj" /></ItemGroup>
            </Project>
            """);
        fixture.Write("consumer.csproj", """
            <Project Sdk="Microsoft.NET.Sdk">
              <PropertyGroup><TargetFrameworks>net10.0;net9.0</TargetFrameworks></PropertyGroup>
              <ItemGroup>
                <ProjectReference Include="dependency.csproj" />
                <ProjectReference Include="analyzer.csproj" OutputItemType="Analyzer"
                                  ReferenceOutputAssembly="false" />
              </ItemGroup>
            </Project>
            """);
        fixture.Write("dependency.csproj", """
            <Project Sdk="Microsoft.NET.Sdk">
              <PropertyGroup><TargetFramework>net10.0</TargetFramework></PropertyGroup>
              <ItemGroup><ProjectReference Include="leaf.csproj" /></ItemGroup>
            </Project>
            """);
        fixture.Write("leaf.csproj", GraphFixture.Project);
        fixture.Write("analyzer.csproj", GraphFixture.Project);
        DotNetGraphResponse response = await fixture.ReadAsync(context.CancellationToken);
        Assert.AreEqual(GraphFixture.Revision, response.Revision);
        Assert.AreEqual(fixture.Root, response.Root);
        DotNetGraphNode[] consumer = response.Nodes.Where(node =>
            node.Identity.Project == fixture.PathOf("consumer.csproj")).ToArray();
        Assert.HasCount(3, consumer);
        Assert.HasCount(1, consumer.Where(node => node.OuterBuild));
        string[] frameworks = consumer.Where(node => !node.OuterBuild)
            .Select(node => node.Dimension.TargetFramework).ToArray();
        Assert.HasCount(2, frameworks);
        Assert.Contains("net10.0", frameworks);
        Assert.Contains("net9.0", frameworks);
        foreach (DotNetGraphNode node in response.Nodes)
        {
            Assert.AreEqual("Debug", node.Identity.Globals["Configuration"]);
            Assert.AreEqual("true", node.Identity.Globals["ContinuousIntegrationBuild"]);
            Assert.AreEqual("true", node.Identity.Globals["RestoreLockedMode"]);
            Assert.AreEqual("semantic", node.Identity.Globals["BusinessSentinel"]);
            Assert.IsFalse(node.Identity.Globals.ContainsKey("WrapperSentinel"));
            Assert.IsFalse(node.Identity.Globals.ContainsKey("RequestFile"));
        }
        foreach (DotNetGraphNode node in consumer.Where(node => !node.OuterBuild))
        {
            Assert.AreEqual(node.Dimension.TargetFramework,
                node.Identity.Globals["TargetFramework"]);
            foreach (string reference in new[] { "dependency.csproj", "analyzer.csproj" })
                Assert.HasCount(1, response.Edges.Where(edge =>
                    edge.Consumer.Project == node.Identity.Project &&
                    edge.Consumer.Globals.TryGetValue("TargetFramework", out string? framework) &&
                    framework == node.Dimension.TargetFramework &&
                    edge.Dependency.Project == fixture.PathOf(reference)));
        }
        Assert.IsTrue(response.Edges.Any(edge =>
            edge.Consumer.Project == fixture.PathOf("dependency.csproj") &&
            edge.Dependency.Project == fixture.PathOf("leaf.csproj")));
    }

    [TestMethod]
    [DataRow("TargetFramework", "net10.0")]
    [DataRow("targetframework", "net10.0")]
    [DataRow("TargetFrameworks", "net10.0")]
    [DataRow("TARGETFRAMEWORKS", "net10.0")]
    [DataRow("RuntimeIdentifier", "linux-x64")]
    [DataRow("runtimeidentifier", "linux-x64")]
    [DataRow("RuntimeIdentifiers", "linux-x64")]
    [DataRow("RUNTIMEIDENTIFIERS", "linux-x64")]
    [DataRow("TargetFramework", "")]
    [DataRow("TargetFrameworks", "")]
    [DataRow("RuntimeIdentifier", "")]
    [DataRow("RuntimeIdentifiers", "")]
    [Timeout(30000, CooperativeCancellation = true)]
    public async Task CallerVariantSelectorsFailWithoutAResponse(string name, string value)
    {
        using var fixture = new GraphFixture();
        fixture.Write("dirs.proj", """
            <Project><ItemGroup><ProjectReference Include="multi.csproj" /></ItemGroup></Project>
            """);
        fixture.Write("multi.csproj", """
            <Project Sdk="Microsoft.NET.Sdk">
              <PropertyGroup><TargetFrameworks>net10.0;net9.0</TargetFrameworks></PropertyGroup>
            </Project>
            """);
        fixture.Globals.Add(name, value);
        await Assert.ThrowsExactlyAsync<InvalidDataException>(() =>
            fixture.ReadAsync(context.CancellationToken));
        Assert.IsFalse(File.Exists(fixture.PathOf("response.json")));
    }

    [TestMethod]
    [Timeout(30000, CooperativeCancellation = true)]
    public async Task MissingNativeReferenceFailsWithoutAResponse()
    {
        using var fixture = new GraphFixture();
        fixture.Write("dirs.proj", """
            <Project><ItemGroup><ProjectReference Include="missing.csproj" /></ItemGroup></Project>
            """);
        await Assert.ThrowsExactlyAsync<InvalidDataException>(() =>
            fixture.ReadAsync(context.CancellationToken));
        Assert.IsFalse(File.Exists(fixture.PathOf("response.json")));
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    [Timeout(30000, CooperativeCancellation = true)]
    public async Task EndpointDependentSemanticGlobalFailsWithoutAResponse(bool trailingSeparator)
    {
        using var fixture = new GraphFixture();
        fixture.Write("dirs.proj", GraphFixture.Project);
        fixture.RequestRoot = trailingSeparator
            ? fixture.Root + Path.DirectorySeparatorChar : fixture.Root;
        fixture.Globals.Add("CustomSourceDirectory", fixture.Root);
        await Assert.ThrowsExactlyAsync<InvalidDataException>(() =>
            fixture.ReadAsync(context.CancellationToken));
        Assert.IsFalse(File.Exists(fixture.PathOf("response.json")));
    }

    [TestMethod]
    [DataRow(".csproj")]
    [DataRow(".CSPROJ")]
    [Timeout(30000, CooperativeCancellation = true)]
    public async Task NativeOwnershipPreservesNearestRegionsAndVariantExcludes(string extension)
    {
        using var fixture = new GraphFixture();
        fixture.Write("dirs.proj", $"""
            <Project><ItemGroup>
              <ProjectReference Include="parent/main.csproj" />
              <ProjectReference Include="parent/nested/one{extension}" />
              <ProjectReference Include="parent/nested/two{extension}" />
            </ItemGroup></Project>
            """);
        fixture.Write("parent/main.csproj", """
            <Project Sdk="Microsoft.NET.Sdk">
              <PropertyGroup>
                <TargetFrameworks>net10.0;net9.0</TargetFrameworks>
                <DefaultItemExcludes>
                  $(DefaultItemExcludes);fixtures/**;semi%3bcolon.txt
                </DefaultItemExcludes>
              </PropertyGroup>
              <PropertyGroup Condition="'$(TargetFramework)' == 'net9.0'">
                <DefaultItemExcludes>$(DefaultItemExcludes);variant-only.txt</DefaultItemExcludes>
              </PropertyGroup>
            </Project>
            """);
        fixture.Write($"parent/nested/one{extension}", GraphFixture.Project);
        fixture.Write($"parent/nested/two{extension}", GraphFixture.Project);
        fixture.CommittedPaths = [fixture.PathOf("parent/document.md"),
            fixture.PathOf("parent/fixtures/version.json"),
            fixture.PathOf("parent/.cache/hidden.txt"),
            fixture.PathOf("parent/semi;colon.txt"),
            fixture.PathOf("parent/variant-only.txt"),
            fixture.PathOf("parent/nested/new.cs"), fixture.PathOf("unowned.md")];
        DotNetGraphResponse response = await fixture.ReadAsync(context.CancellationToken);
        DotNetGraphNode[] parent = response.Nodes.Where(node =>
            node.Identity.Project == fixture.PathOf("parent/main.csproj") && !node.OuterBuild)
            .ToArray();
        Assert.HasCount(2, parent);
        foreach (DotNetGraphNode node in parent)
        {
            Assert.Contains(fixture.PathOf("parent/document.md"), node.OwnedPaths);
            Assert.DoesNotContain(fixture.PathOf("parent/fixtures/version.json"), node.OwnedPaths);
            Assert.DoesNotContain(fixture.PathOf("parent/.cache/hidden.txt"), node.OwnedPaths);
            Assert.DoesNotContain(fixture.PathOf("parent/semi;colon.txt"), node.OwnedPaths);
            Assert.DoesNotContain(fixture.PathOf("parent/nested/new.cs"), node.OwnedPaths);
            Assert.DoesNotContain(fixture.PathOf("unowned.md"), node.OwnedPaths);
            Assert.AreEqual(node.Dimension.TargetFramework == "net10.0",
                node.OwnedPaths.Contains(fixture.PathOf("parent/variant-only.txt")));
        }
        DotNetGraphNode[] nested = response.Nodes.Where(node =>
            node.Identity.Project == fixture.PathOf($"parent/nested/one{extension}") ||
            node.Identity.Project == fixture.PathOf($"parent/nested/two{extension}")).ToArray();
        Assert.HasCount(2, nested);
        foreach (DotNetGraphNode node in nested)
        {
            Assert.HasCount(1, node.OwnedPaths);
            Assert.Contains(fixture.PathOf("parent/nested/new.cs"), node.OwnedPaths);
        }
    }

    [TestMethod]
    [DataRow("literal%2a.txt")]
    [DataRow("bad/**suffix/file.txt")]
    [Timeout(30000, CooperativeCancellation = true)]
    public async Task UnsupportedNativeOwnershipExcludesFailWithoutAResponse(string exclude)
    {
        using var fixture = new GraphFixture();
        fixture.Write("dirs.proj", """
            <Project><ItemGroup><ProjectReference Include="main.csproj" /></ItemGroup></Project>
            """);
        fixture.Write("main.csproj", $"""
            <Project Sdk="Microsoft.NET.Sdk"><PropertyGroup>
              <TargetFramework>net10.0</TargetFramework>
              <DefaultItemExcludes>$(DefaultItemExcludes);{exclude}</DefaultItemExcludes>
            </PropertyGroup></Project>
            """);
        fixture.CommittedPaths = [fixture.PathOf("source.cs")];
        await Assert.ThrowsExactlyAsync<InvalidDataException>(() =>
            fixture.ReadAsync(context.CancellationToken));
        Assert.IsFalse(File.Exists(fixture.PathOf("response.json")));
    }

    [TestMethod]
    [Timeout(30000, CooperativeCancellation = true)]
    public async Task ExcludedNativeResourceRetainsItsExplicitConsumerAndImport()
    {
        using var fixture = new GraphFixture();
        fixture.Write("dirs.proj", """
            <Project><ItemGroup>
              <ProjectReference Include="parent/main.csproj" />
            </ItemGroup></Project>
            """);
        fixture.Write("common.props", "<Project />");
        fixture.Write("shared.cs", "");
        fixture.Write("parent/fixtures/version.json", "{}");
        fixture.Write("parent/main.csproj", """
            <Project Sdk="Microsoft.NET.Sdk">
              <Import Project="../common.props" />
              <PropertyGroup>
                <TargetFramework>net10.0</TargetFramework>
                <DefaultItemExcludes>$(DefaultItemExcludes);fixtures/**</DefaultItemExcludes>
              </PropertyGroup>
              <ItemGroup>
                <EmbeddedResource Include="fixtures/version.json" />
                <Compile Include="../shared.cs" />
              </ItemGroup>
            </Project>
            """);
        fixture.CommittedPaths = [fixture.PathOf("parent/fixtures/version.json"),
            fixture.PathOf("shared.cs"), fixture.PathOf("common.props")];
        DotNetGraphResponse response = await fixture.ReadAsync(context.CancellationToken);
        DotNetGraphNode owner = Assert.ContainsSingle(response.Nodes.Where(node =>
            node.Identity.Project == fixture.PathOf("parent/main.csproj")));
        Assert.IsEmpty(owner.OwnedPaths);
        foreach ((string path, string role) in new[]
        {
            ("parent/main.csproj", "Project"), ("common.props", "Import"),
            ("shared.cs", "Compile"), ("parent/fixtures/version.json", "EmbeddedResource"),
        })
        {
            DotNetGraphInput input = Assert.ContainsSingle(response.Inputs.Where(input =>
                input.Consumer.Project == owner.Identity.Project && input.Role == role &&
                input.Path == fixture.PathOf(path)));
            Assert.AreEqual("Evaluation", input.Stage);
            CollectionAssert.AreEquivalent(owner.Identity.Globals.ToArray(),
                input.Consumer.Globals.ToArray());
        }
    }

    private sealed class GraphFixture : IDisposable
    {
        internal const string Revision = "1111111111111111111111111111111111111111";
        internal const string Project = """
            <Project Sdk="Microsoft.NET.Sdk">
              <PropertyGroup><TargetFramework>net10.0</TargetFramework></PropertyGroup>
            </Project>
            """;
        internal string Root { get; } = System.IO.Path.Combine(System.IO.Path.GetTempPath(),
            "workflow-native-graph-" + Guid.NewGuid().ToString("N"));
        internal Dictionary<string, string> Globals { get; } = new(StringComparer.Ordinal)
        {
            ["Configuration"] = "Debug",
            ["ContinuousIntegrationBuild"] = "true",
            ["RestoreLockedMode"] = "true",
        };
        internal string[] CommittedPaths { get; set; } = [];
        internal string? RequestRoot { get; set; }

        internal GraphFixture() => Directory.CreateDirectory(Root);
        internal string PathOf(string name) => System.IO.Path.GetFullPath(
            System.IO.Path.Combine(Root, name));
        internal void Write(string name, string content)
        {
            string path = PathOf(name);
            Directory.CreateDirectory(System.IO.Path.GetDirectoryName(path)!);
            File.WriteAllText(path, content);
        }

        internal async Task<DotNetGraphResponse> ReadAsync(CancellationToken token)
        {
            Write("request.json", JsonSerializer.Serialize(new DotNetGraphRequest(Revision,
                RequestRoot ?? Root, PathOf("dirs.proj"), Globals, PathOf("response.json"),
                CommittedPaths), TransferJson.Default.DotNetGraphRequest));
            string assembly = SecurityElement.Escape(typeof(DotNetGraphTask).Assembly.Location)!;
            string request = SecurityElement.Escape(PathOf("request.json"))!;
            Write("read.proj", $"""
                <Project>
                  <UsingTask TaskName="WorkflowDelivery.Repository.DotNetGraphTask"
                             AssemblyFile="{assembly}" />
                  <Target Name="Read">
                    <WorkflowDelivery.Repository.DotNetGraphTask RequestFile="{request}" />
                  </Target>
                </Project>
                """);
            await NativeProcess.RunAsync("dotnet", Root,
                ["msbuild", PathOf("read.proj"), "-nologo", "-noAutoResponse", "-target:Read",
                    "-property:WrapperSentinel=wrapper-only"], token);
            return JsonSerializer.Deserialize(await File.ReadAllTextAsync(
                PathOf("response.json"), token), TransferJson.Default.DotNetGraphResponse)
                ?? throw new InvalidDataException("Missing graph response.");
        }

        public void Dispose() => Directory.Delete(Root, true);
    }
}
