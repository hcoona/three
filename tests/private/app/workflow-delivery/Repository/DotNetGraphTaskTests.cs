using System.Security;
using System.Text.Json;
using NuGet.ProjectModel;
using WorkflowDelivery.Platform;
using WorkflowDelivery.Repository;

namespace WorkflowDelivery.Tests.Repository;

[TestClass]
public sealed class DotNetGraphTaskTests(TestContext context)
{
    private static readonly StringComparer PhysicalPathComparer = OperatingSystem.IsWindows()
        ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal;

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
              <PropertyGroup><TargetFramework>net9.0</TargetFramework></PropertyGroup>
              <ItemGroup><ProjectReference Include="leaf.csproj" /></ItemGroup>
            </Project>
            """);
        fixture.Write("leaf.csproj", GraphFixture.Project.Replace("net10.0", "net9.0"));
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
        await fixture.PrepareAsync(context.CancellationToken);
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
        await fixture.PrepareAsync(context.CancellationToken);
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

    [TestMethod]
    [Timeout(60000, CooperativeCancellation = true)]
    public async Task LockedRestoreInputsRetainAllNativeConsumers()
    {
        using var fixture = new GraphFixture();
        fixture.Write("dirs.proj", """
            <Project><ItemGroup><ProjectReference Include="multi.csproj" /></ItemGroup></Project>
            """);
        fixture.Write("Directory.Packages.props", """
            <Project><PropertyGroup>
              <ManagePackageVersionsCentrally>true</ManagePackageVersionsCentrally>
            </PropertyGroup></Project>
            """);
        fixture.Write("multi.csproj", """
            <Project Sdk="Microsoft.NET.Sdk"><PropertyGroup>
              <TargetFrameworks>net10.0;net9.0</TargetFrameworks>
              <NuGetLockFilePath>locks/multi.json</NuGetLockFilePath>
            </PropertyGroup></Project>
            """);
        fixture.Write("locks/.keep", "");
        DotNetGraphResponse response = await fixture.ReadAsync(context.CancellationToken);
        DotNetGraphNode[] managed = response.Nodes.Where(node =>
            node.Identity.Project == fixture.PathOf("multi.csproj")).ToArray();
        Assert.HasCount(3, managed);
        Assert.HasCount(1, managed.Where(node => node.OuterBuild));
        foreach (DotNetGraphNode node in managed)
        {
            foreach ((string path, string role, string stage) in new[]
            {
                ("obj/multi/project.assets.json", "RestoreAssets", "LockedRestore"),
                ("locks/multi.json", "RestoreLock", "LockedRestore"),
                ("nuget.config", "RestoreConfiguration", "LockedRestore"),
                ("Directory.Packages.props", "CentralPackageConfiguration", "Evaluation"),
                ("global.json", "SdkConfigurationCandidate", "Evaluation"),
                ("packages.lock.json", "RestoreLockCandidate", "Evaluation"),
            })
            {
                string expectedPath = fixture.PathOf(path);
                DotNetGraphInput input = Assert.ContainsSingle(response.Inputs.Where(input =>
                    PhysicalPathComparer.Equals(input.Path, expectedPath) && input.Role == role &&
                    input.Consumer.Project == node.Identity.Project &&
                    input.Consumer.Globals.OrderBy(pair => pair.Key).SequenceEqual(
                        node.Identity.Globals.OrderBy(pair => pair.Key))),
                    $"Expected {role} at {expectedPath} for {node.Identity.Project} " +
                    $"[{string.Join(", ", node.Identity.Globals)}]. Native inputs: " +
                    string.Join("; ", response.Inputs.Where(input => input.Role == role)
                        .Select(input => $"{input.Path} for {input.Consumer.Project} " +
                            $"[{string.Join(", ", input.Consumer.Globals)}]")));
                Assert.AreEqual(stage, input.Stage);
            }
        }
        Assert.IsFalse(response.Inputs.Any(input => input.Role == "RestoreAssets" &&
            input.Consumer.Project == fixture.PathOf("dirs.proj")));
        Assert.IsFalse(File.Exists(fixture.PathOf("global.json")));
    }

    [TestMethod]
    [DataRow("missing-assets")]
    [DataRow("unsupported-format")]
    [DataRow("missing-project")]
    [DataRow("missing-restore")]
    [DataRow("wrong-project")]
    [DataRow("unlocked")]
    [DataRow("empty-configurations")]
    [DataRow("missing-root-contributor")]
    [DataRow("uncommitted-root-configuration")]
    [DataRow("missing-lock")]
    [Timeout(60000, CooperativeCancellation = true)]
    public async Task MissingOrDifferentRestoreMetadataFailsWithoutAResponse(string scenario)
    {
        using var fixture = new GraphFixture();
        fixture.Write("dirs.proj", """
            <Project><ItemGroup><ProjectReference Include="main.csproj" /></ItemGroup></Project>
            """);
        fixture.Write("main.csproj", GraphFixture.Project);
        await fixture.PrepareAsync(context.CancellationToken);
        string path = fixture.PathOf("obj/main/project.assets.json");
        var format = new LockFileFormat();
        LockFile assets = format.Read(path);
        switch (scenario)
        {
            case "missing-assets": File.Delete(path); break;
            case "missing-lock": File.Delete(fixture.PathOf("packages.main.lock.json")); break;
            case "unsupported-format": assets.Version = LockFileFormat.Version + 1; break;
            case "missing-project": assets.PackageSpec = null; break;
            case "missing-restore": assets.PackageSpec.RestoreMetadata = null; break;
            case "wrong-project":
                assets.PackageSpec.RestoreMetadata.ProjectPath = fixture.PathOf("other.csproj");
                break;
            case "unlocked":
                assets.PackageSpec.RestoreMetadata.RestoreLockProperties = new("true", null, false);
                break;
            case "empty-configurations":
                assets.PackageSpec.RestoreMetadata.ConfigFilePaths.Clear();
                break;
            case "missing-root-contributor":
                string rootConfiguration = fixture.PathOf("nuget.config");
                var configurations = assets.PackageSpec.RestoreMetadata.ConfigFilePaths;
                string contributor = Assert.ContainsSingle(configurations.Where(configuration =>
                    PhysicalPathComparer.Equals(configuration, rootConfiguration)),
                    $"Expected one root contributor at {rootConfiguration}. Native contributors: " +
                    string.Join("; ", configurations));
                Assert.IsTrue(configurations.Remove(contributor));
                break;
            case "uncommitted-root-configuration": fixture.IncludeRootConfiguration = false; break;
        }
        if (scenario is not ("missing-assets" or "missing-lock")) format.Write(path, assets);
        await Assert.ThrowsExactlyAsync<InvalidDataException>(() =>
            fixture.ReadAsync(context.CancellationToken));
        Assert.IsFalse(File.Exists(fixture.PathOf("response.json")));
    }

    [TestMethod]
    [Timeout(60000, CooperativeCancellation = true)]
    public async Task NativeRestoreRetainsRidAndRedirectedCpm()
    {
        using var fixture = new GraphFixture();
        fixture.Write("dirs.proj", """
            <Project><ItemGroup><ProjectReference Include="main.csproj" /></ItemGroup></Project>
            """);
        fixture.Write("Directory.Build.props", File.ReadAllText(
            fixture.PathOf("Directory.Build.props")).Replace("</PropertyGroup>",
            "<DirectoryPackagesPropsPath>$(MSBuildThisFileDirectory)config/packages.props" +
            "</DirectoryPackagesPropsPath></PropertyGroup>"));
        fixture.Write("config/packages.props", """
            <Project><PropertyGroup>
              <ManagePackageVersionsCentrally>true</ManagePackageVersionsCentrally>
            </PropertyGroup></Project>
            """);
        fixture.Write("main.csproj", """
            <Project Sdk="Microsoft.NET.Sdk"><PropertyGroup>
              <TargetFramework>net10.0</TargetFramework>
              <RuntimeIdentifier>linux-x64</RuntimeIdentifier>
              <UseAppHost>false</UseAppHost>
            </PropertyGroup></Project>
            """);
        DotNetGraphResponse response = await fixture.ReadAsync(context.CancellationToken);
        DotNetGraphNode consumer = Assert.ContainsSingle(response.Nodes.Where(node =>
            node.Identity.Project == fixture.PathOf("main.csproj")));
        Assert.AreEqual("linux-x64", consumer.Dimension.RuntimeIdentifier);
        LockFile assets = new LockFileFormat().Read(fixture.PathOf("obj/main/project.assets.json"));
        Assert.IsTrue(assets.Targets.Count > 1);
        Assert.IsTrue(assets.Targets.Any(target => target.RuntimeIdentifier == "linux-x64"));
        foreach ((string path, string role) in new[]
        {
            ("config/packages.props", "CentralPackageConfiguration"),
            ("packages.main.lock.json", "RestoreLock"),
            ("obj/main/project.assets.json", "RestoreAssets"),
        })
        {
            DotNetGraphInput input = Assert.ContainsSingle(response.Inputs.Where(input =>
                input.Path == fixture.PathOf(path) && input.Role == role));
            Assert.AreEqual(consumer.Identity.Project, input.Consumer.Project);
            CollectionAssert.AreEquivalent(consumer.Identity.Globals.ToArray(),
                input.Consumer.Globals.ToArray());
        }
    }

    [TestMethod]
    [Timeout(60000, CooperativeCancellation = true)]
    public async Task NativeAnalyzerConfigCandidatesPreserveAbsentLinkedPaths()
    {
        using var fixture = new GraphFixture();
        fixture.Write("dirs.proj", """
            <Project><ItemGroup>
              <ProjectReference Include="product/main.csproj" />
            </ItemGroup></Project>
            """);
        fixture.Write("product/main.csproj", """
            <Project Sdk="Microsoft.NET.Sdk"><PropertyGroup>
              <TargetFramework>net10.0</TargetFramework>
            </PropertyGroup><ItemGroup>
              <Compile Include="../linked/source.cs" />
            </ItemGroup></Project>
            """);
        fixture.Write("linked/source.cs", "");
        DotNetGraphResponse response = await fixture.ReadAsync(context.CancellationToken);
        DotNetGraphNode consumer = Assert.ContainsSingle(response.Nodes.Where(node =>
            node.Identity.Project == fixture.PathOf("product/main.csproj")));
        foreach ((string path, string role) in new[]
        {
            ("linked/.editorconfig", "PotentialEditorConfigFiles"),
            ("linked/.globalconfig", "GlobalAnalyzerConfigFiles"),
        })
        {
            DotNetGraphInput input = Assert.ContainsSingle(response.Inputs.Where(input =>
                input.Path == fixture.PathOf(path) && input.Role == role));
            Assert.AreEqual(consumer.Identity.Project, input.Consumer.Project);
            CollectionAssert.AreEquivalent(consumer.Identity.Globals.ToArray(),
                input.Consumer.Globals.ToArray());
            Assert.AreEqual("Evaluation", input.Stage);
            Assert.IsFalse(File.Exists(input.Path));
        }
    }

    [TestMethod]
    [Timeout(60000, CooperativeCancellation = true)]
    public async Task NativeAnalyzerConfigDiscoverySwitchesRemainEffective()
    {
        using var fixture = new GraphFixture();
        fixture.Write("dirs.proj", """
            <Project><ItemGroup><ProjectReference Include="main.csproj" /></ItemGroup></Project>
            """);
        fixture.Write("main.csproj", """
            <Project Sdk="Microsoft.NET.Sdk"><PropertyGroup>
              <TargetFramework>net10.0</TargetFramework>
              <DiscoverEditorConfigFiles>false</DiscoverEditorConfigFiles>
              <DiscoverGlobalAnalyzerConfigFiles>false</DiscoverGlobalAnalyzerConfigFiles>
            </PropertyGroup></Project>
            """);
        fixture.Write("source.cs", "");
        DotNetGraphResponse response = await fixture.ReadAsync(context.CancellationToken);
        Assert.IsFalse(response.Inputs.Any(input =>
            input.Role is "PotentialEditorConfigFiles" or "GlobalAnalyzerConfigFiles"));
        Assert.IsTrue(response.Inputs.Any(input => input.Role == "Compile" &&
            input.Path == fixture.PathOf("source.cs")));
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
        internal bool IncludeRootConfiguration { get; set; } = true;
        private readonly HashSet<string> _projects = new(StringComparer.Ordinal);
        private bool _prepared;

        internal GraphFixture()
        {
            Directory.CreateDirectory(Root);
            Write("nuget.config", """
                <configuration><packageSources><clear />
                  <add key="nuget.org" value="https://api.nuget.org/v3/index.json" />
                </packageSources></configuration>
                """);
            Write("Directory.Build.props", """
                <Project><PropertyGroup>
                <BaseIntermediateOutputPath>obj/$(MSBuildProjectName)/</BaseIntermediateOutputPath>
                  <RestorePackagesWithLockFile>true</RestorePackagesWithLockFile>
                  <NuGetLockFilePath>packages.$(MSBuildProjectName).lock.json</NuGetLockFilePath>
                  <NuGetAudit>false</NuGetAudit>
                </PropertyGroup></Project>
                """);
        }
        internal string PathOf(string name) => System.IO.Path.GetFullPath(
            System.IO.Path.Combine(Root, name));
        internal void Write(string name, string content)
        {
            string path = PathOf(name);
            Directory.CreateDirectory(System.IO.Path.GetDirectoryName(path)!);
            File.WriteAllText(path, content);
            if (new[] { ".csproj", ".fsproj", ".vbproj" }.Contains(
                System.IO.Path.GetExtension(path), StringComparer.OrdinalIgnoreCase))
                _projects.Add(path);
        }

        internal async Task PrepareAsync(CancellationToken token)
        {
            if (_prepared) return;
            foreach (string project in _projects)
            {
                string[] operation = ["-property:Configuration=Debug",
                    "-property:ContinuousIntegrationBuild=true"];
                // Seed only disposable fixture locks, then perform the supported locked operation.
                await RestoreAsync(
                    ["restore", project, "--use-lock-file", .. operation], token);
                await RestoreAsync(
                    ["restore", project, "--locked-mode", "--force", .. operation], token);
            }
            _prepared = true;
        }

        private async Task RestoreAsync(string[] arguments, CancellationToken token)
        {
            NativeCommandResult result = await NativeProcess.ExecuteAsync(
                new("dotnet", Root, arguments, 30), token);
            token.ThrowIfCancellationRequested();
            if (!result.Succeeded)
                throw new InvalidDataException(
                    $"Fixture restore {result.Termination}, exit {result.ExitCode}: " +
                    result.Error + "\n" + result.Stdout + result.Stderr);
        }

        internal async Task<DotNetGraphResponse> ReadAsync(CancellationToken token)
        {
            await PrepareAsync(token);
            Write("request.json", JsonSerializer.Serialize(new DotNetGraphRequest(Revision,
                RequestRoot ?? Root, PathOf("dirs.proj"), Globals, PathOf("response.json"),
                IncludeRootConfiguration ? [.. CommittedPaths, PathOf("nuget.config")]
                    : CommittedPaths),
                TransferJson.Default.DotNetGraphRequest));
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
