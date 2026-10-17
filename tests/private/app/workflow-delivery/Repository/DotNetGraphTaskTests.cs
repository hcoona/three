using System.Security;
using System.Text.Json;
using Microsoft.Build.Construction;
using Microsoft.Build.Evaluation;
using Microsoft.Build.Execution;
using NuGet.ProjectModel;
using WorkflowDelivery.CI;
using WorkflowDelivery.Platform;
using WorkflowDelivery.Repository;

namespace WorkflowDelivery.Tests.Repository;

[TestClass]
public sealed class DotNetGraphTaskTests(TestContext context)
{
    private static readonly StringComparer PhysicalPathComparer = OperatingSystem.IsWindows()
        ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal;
    private static readonly string[] NativeFrameworks = ["net10.0", "net9.0"];

    [TestMethod]
    // Fixture restore phases and the graph read retain their own command deadlines.
    [Timeout(360000, CooperativeCancellation = true)]
    public async Task NativeNbgvHelperPreservesBusinessVariantsAndInputs()
    {
        using var fixture = new GraphFixture();
        fixture.ConfigureNbgv(rootVersionBase: false);
        fixture.Globals.Add("BusinessSentinel", "original semantic value");
        string versionBase = fixture.PathOf("product");

        DotNetGraphResponse response = await fixture.ReadAsync(context.CancellationToken);
        Assert.AreEqual(GraphFixture.Revision, response.Revision);
        Assert.AreEqual(fixture.Root, response.Root);
        Assert.HasCount(7, response.Nodes);
        // The pinned package's caching helper is an input, never a business node.
        string helper = response.Inputs.Select(input => input.Path).Distinct(PhysicalPathComparer)
            .Single(path => Path.GetFileName(path) == "PrivateP2PCaching.proj");
        Assert.IsFalse(response.Nodes.Any(node => PhysicalPathComparer.Equals(
            node.Identity.Project, helper)));
        Assert.IsTrue(response.Nodes.All(node => node.Dimension.Configuration == "Debug"));
        foreach (DotNetGraphNode node in response.Nodes)
        {
            foreach ((string name, string value) in fixture.Globals)
                Assert.AreEqual(value, node.Identity.Globals[name], name);
            Assert.IsFalse(node.Identity.Globals.ContainsKey("GitVersionBaseDirectory"));
            Assert.IsFalse(node.Identity.Globals.ContainsKey("NBGV_PrivateP2PAuxTargets"));
            Assert.IsFalse(node.Identity.Globals.ContainsKey("WrapperSentinel"));
            Assert.IsFalse(node.Identity.Globals.ContainsKey("RequestFile"));
        }
        // Native reference edges include a transitive reference below the entry graph.
        Assert.IsTrue(response.Edges.Any(edge => PhysicalPathComparer.Equals(
                edge.Consumer.Project, fixture.PathOf("dependency/dependency.csproj")) &&
            PhysicalPathComparer.Equals(edge.Dependency.Project,
                fixture.PathOf("leaf/leaf.csproj"))));
        // Both effective native values are retained: the consumer's local base and the root
        // base other projects inherit (the SDK fallback alone would be each project directory).
        foreach (DotNetGraphNode node in response.Nodes.Where(node => PhysicalPathComparer.Equals(
            node.Identity.Project, fixture.PathOf("dependency/dependency.csproj"))))
            Assert.IsTrue(PhysicalPathComparer.Equals(fixture.Root,
                Path.TrimEndingDirectorySeparator(node.VersionBaseDirectory)),
                "dependency version base: " + node.VersionBaseDirectory);
        DotNetGraphNode[] consumers = response.Nodes.Where(node => PhysicalPathComparer.Equals(
            node.Identity.Project, fixture.PathOf("product/consumer.csproj"))).ToArray();
        Assert.HasCount(3, consumers);
        DotNetGraphNode outer = Assert.ContainsSingle(consumers.Where(node => node.OuterBuild));
        foreach (DotNetGraphNode node in consumers)
        {
            Assert.IsTrue(PhysicalPathComparer.Equals(versionBase,
                Path.TrimEndingDirectorySeparator(node.VersionBaseDirectory)),
                "consumer version base: " + node.VersionBaseDirectory);
            Dictionary<string, string> expected = new(outer.Identity.Globals,
                StringComparer.Ordinal);
            if (!node.OuterBuild)
            {
                Assert.Contains(node.Dimension.TargetFramework, NativeFrameworks);
                expected["TargetFramework"] = node.Dimension.TargetFramework;
                foreach (string project in new[] { "dependency/dependency.csproj",
                    "analyzer/analyzer.csproj" })
                    Assert.HasCount(1, response.Edges.Where(edge =>
                        SameIdentity(edge.Consumer, node.Identity) && PhysicalPathComparer.Equals(
                            edge.Dependency.Project, fixture.PathOf(project))));
            }
            CollectionAssert.AreEquivalent(expected.ToArray(), node.Identity.Globals.ToArray());
            if (node.OuterBuild)
            {
                Assert.IsTrue(response.Inputs.Any(input => input.Role == "RestoreAssets" &&
                    SameIdentity(input.Consumer, node.Identity)));
                continue;
            }
            foreach ((string path, string role) in new[]
            {
                ("shared/nbgv-aux.targets", "Import"),
                ("shared/aux-input.txt", "AdditionalFiles"),
            })
            {
                DotNetGraphInput input = Assert.ContainsSingle(response.Inputs.Where(input =>
                    SameIdentity(input.Consumer, node.Identity) && input.Role == role &&
                    PhysicalPathComparer.Equals(input.Path, fixture.PathOf(path))),
                    $"Expected {role} {path} for {node.Dimension.TargetFramework} " +
                    $"outer={node.OuterBuild}. " +
                    "Observed helper consumers: " + string.Join("; ", response.Inputs.Where(input =>
                        input.Path == helper || input.Path == fixture.PathOf(path)).Select(input =>
                        $"{input.Role}: {input.Consumer.Project}, " +
                        JsonSerializer.Serialize(input.Consumer.Globals,
                            TransferJson.Default.DictionaryStringString))));
                Assert.AreEqual("Evaluation", input.Stage);
                Assert.IsNull(input.Provider);
            }
            foreach (string name in new[] { "PrivateP2PCaching.proj",
                "Nerdbank.GitVersioning.Inner.targets", "Nerdbank.GitVersioning.Common.targets" })
            {
                DotNetGraphInput[] inputs = response.Inputs.Where(input =>
                    SameIdentity(input.Consumer, node.Identity) && input.Role == "Import" &&
                    Path.GetFileName(input.Path) == name).ToArray();
                Assert.IsNotEmpty(inputs, name);
                Assert.IsTrue(inputs.All(input => input.Stage == "Evaluation" &&
                    input.Provider is
                    {
                        Kind: "LockedPackage",
                        Identity: "Nerdbank.GitVersioning", Version: "3.10.94"
                    }));
            }
        }
        Assert.IsTrue(response.Edges.All(edge => response.Nodes.Any(node =>
            SameIdentity(node.Identity, edge.Consumer)) && response.Nodes.Any(node =>
            SameIdentity(node.Identity, edge.Dependency))));
        Assert.IsTrue(response.Inputs.All(input => response.Nodes.Any(node =>
            SameIdentity(node.Identity, input.Consumer))));
        DotNetGraphNode root = Assert.ContainsSingle(response.Nodes.Where(node =>
            node.Identity.Project == fixture.PathOf("dirs.proj")));
        // The official traversal graph also consumes the reachable native helper.
        DotNetGraphInput[] auxiliary = response.Inputs.Where(input =>
            input.Role == "AdditionalFiles" && PhysicalPathComparer.Equals(input.Path,
                fixture.PathOf("shared/aux-input.txt"))).ToArray();
        DotNetNodeIdentity[] expectedAuxiliaryConsumers = consumers.Where(node => !node.OuterBuild)
            .Select(node => node.Identity).Append(root.Identity).ToArray();
        Assert.HasCount(expectedAuxiliaryConsumers.Length, auxiliary);
        foreach (DotNetNodeIdentity consumer in expectedAuxiliaryConsumers)
            Assert.ContainsSingle(auxiliary.Where(input => SameIdentity(consumer, input.Consumer)));
        Assert.IsEmpty(root.OwnedPaths);
        Assert.HasCount(1, response.Edges.Where(edge =>
            SameIdentity(edge.Consumer, root.Identity) &&
            SameIdentity(edge.Dependency, outer.Identity)));
        Assert.HasCount(2, response.Edges.Where(edge =>
            SameIdentity(edge.Consumer, outer.Identity) && consumers.Any(node =>
                !node.OuterBuild && SameIdentity(edge.Dependency, node.Identity))));
        Assert.IsFalse(response.Inputs.Any(input => SameIdentity(input.Consumer, root.Identity) &&
            input.Role == "RestoreLock"));
        Assert.IsTrue(response.Inputs.Any(input => SameIdentity(input.Consumer, root.Identity) &&
            input.Path == helper && input.Provider is
            {
                Kind: "TraversalPackage",
                Identity: "Nerdbank.GitVersioning", Version: "3.10.94"
            }));
        Assert.IsTrue(response.Inputs.Any(input => SameIdentity(input.Consumer, root.Identity) &&
            input.Provider?.Kind == "RestoreGenerated"));
        Assert.IsTrue(response.Inputs.Any(input => SameIdentity(input.Consumer, root.Identity) &&
            input.Role == "RestoreAssets"));
        // The traversal consumes a transitive restored package import with its package provider.
        DotNetGraphInput common = Assert.ContainsSingle(response.Inputs.Where(input =>
            SameIdentity(input.Consumer, root.Identity) && input.Role == "Import" &&
            Path.GetFileName(input.Path) == "Microsoft.SourceLink.Common.props"),
            "Observed root imports: " + string.Join("; ", response.Inputs.Where(input =>
                SameIdentity(input.Consumer, root.Identity) && input.Role == "Import")
                .Select(input => $"{input.Path} {input.Provider}")));
        Assert.IsNotNull(common.Provider);
        Assert.AreEqual("TraversalPackage", common.Provider.Kind);
        Assert.AreEqual("Microsoft.SourceLink.Common", common.Provider.Identity);
        // The provider version is the app's pass-through of the traversal's restored assets.
        LockFileLibrary restoredCommon = Assert.ContainsSingle(new LockFileFormat().Read(
            fixture.PathOf("obj/dirs/project.assets.json")).Libraries.Where(library =>
            library.Type == "package" && library.Name == "Microsoft.SourceLink.Common"));
        Assert.AreEqual(restoredCommon.Version.ToNormalizedString(), common.Provider.Version);
        Assert.IsTrue(PhysicalPathComparer.Equals(Path.Combine(common.Provider.Root, "build",
            "Microsoft.SourceLink.Common.props"), common.Path), common.Path);
        foreach (DotNetGraphNode node in consumers.Where(node => !node.OuterBuild))
        {
            CheckSpec[] checks = DotNetChecks.Expand("product/consumer.csproj", node);
            Assert.AreEqual("product/consumer.csproj", Assert.ContainsSingle(checks).Key.Target);
            Dictionary<string, string> globals = JsonSerializer.Deserialize(
                checks[0].Dimensions["globals"], TransferJson.Default.DictionaryStringString)!;
            CollectionAssert.AreEquivalent(node.Identity.Globals.ToArray(), globals.ToArray());
        }
    }

    [TestMethod]
    [Timeout(300000, CooperativeCancellation = true)]
    public async Task CommittedNbgvAuxiliaryProducerFailsWithoutAResponse()
    {
        using var fixture = new GraphFixture();
        fixture.ConfigureNbgv(rootVersionBase: true);
        fixture.Write("shared/nbgv-aux.targets", """
            <Project><Target Name="UnsupportedNbgvAuxiliaryProducer" /></Project>
            """);
        InvalidDataException error = await Assert.ThrowsExactlyAsync<InvalidDataException>(() =>
            fixture.ReadAsync(context.CancellationToken));
        foreach (string expected in new[] { "RepositoryTarget", "UnsupportedNbgvAuxiliaryProducer",
            "nbgv-aux.targets", GraphFixture.Revision })
            Assert.Contains(expected, error.Message);
        Assert.IsFalse(File.Exists(fixture.PathOf("response.json")));
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    [Timeout(60000, CooperativeCancellation = true)]
    public async Task UnsupportedMarkedNbgvHelperFailsWithoutAResponse(bool packageMissing)
    {
        using var fixture = new GraphFixture();
        fixture.Write("dirs.proj", """
            <Project><ItemGroup>
              <ProjectReference Include="consumer.csproj" />
            </ItemGroup></Project>
            """);
        fixture.Write("consumer.csproj", """
            <Project Sdk="Microsoft.NET.Sdk"><PropertyGroup>
              <TargetFramework>net10.0</TargetFramework>
            </PropertyGroup><ItemGroup>
              <ProjectReference Include="PrivateP2PCaching.proj" NBGV_InnerProject="true" />
            </ItemGroup></Project>
            """);
        fixture.Write("PrivateP2PCaching.proj", "<Project />");
        if (!packageMissing)
            fixture.Write("Directory.Build.targets", """
                <Project><ItemGroup>
                  <PackageReference Include="Nerdbank.GitVersioning" Version="3.10.94"
                    PrivateAssets="all" />
                </ItemGroup></Project>
                """);
        fixture.CommittedPaths = [fixture.PathOf("dirs.proj"),
            fixture.PathOf("consumer.csproj"), fixture.PathOf("PrivateP2PCaching.proj")];
        InvalidDataException error = await Assert.ThrowsExactlyAsync<InvalidDataException>(() =>
            fixture.ReadAsync(context.CancellationToken));
        Assert.Contains("NBGV", error.Message);
        Assert.IsFalse(File.Exists(fixture.PathOf("response.json")));
    }

    private static bool SameIdentity(DotNetNodeIdentity first, DotNetNodeIdentity second) =>
        new DotNetRepositoryReader.NativeIdentityComparer().Equals(
            DotNetRepositoryReader.Key(first), DotNetRepositoryReader.Key(second));

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
    public void CallerVariantSelectorsAreRejectedBeforeNativeEvaluation(string name, string value)
    {
        using var fixture = new GraphFixture();
        fixture.Write("dirs.proj", "<Project />");
        fixture.Globals.Add(name, value);
        var request = new DotNetGraphRequest(GraphFixture.Revision, fixture.Root,
            fixture.PathOf("dirs.proj"), fixture.Globals, fixture.PathOf("response.json"), []);
        InvalidDataException error = Assert.ThrowsExactly<InvalidDataException>(() =>
            DotNetGraph.Read(request));
        Assert.Contains("does not accept caller variant selectors", error.Message);
    }

    [TestMethod]
    [Timeout(30000, CooperativeCancellation = true)]
    public async Task CallerVariantSelectorFailsWithoutANativeResponse()
    {
        using var fixture = new GraphFixture();
        fixture.Write("dirs.proj", """
            <Project><ItemGroup><ProjectReference Include="multi.csproj" /></ItemGroup></Project>
            """);
        fixture.Write("multi.csproj", """
            <Project Sdk="Microsoft.NET.Sdk">
              <PropertyGroup><TargetFrameworks>net10.0;net9.0</TargetFrameworks></PropertyGroup>
            </Project>
            """, restoreProject: false);
        fixture.Globals.Add("targetframework", "");
        InvalidDataException error = await Assert.ThrowsExactlyAsync<InvalidDataException>(() =>
            fixture.ReadAsync(context.CancellationToken));
        Assert.Contains("does not accept caller variant selectors", error.Message);
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
    [Timeout(30000, CooperativeCancellation = true)]
    public async Task NativeOwnershipPreservesNearestRegionsAndVariantExcludes()
    {
        // Same-directory projects share their region; a lone upper-case extension forms its own.
        using var fixture = new GraphFixture();
        fixture.Write("dirs.proj", """
            <Project><ItemGroup>
              <ProjectReference Include="parent/main.csproj" />
              <ProjectReference Include="parent/nested/one.csproj" />
              <ProjectReference Include="parent/nested/three.csproj" />
              <ProjectReference Include="parent/nested2/two.CSPROJ" />
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
        fixture.Write("parent/nested/one.csproj", GraphFixture.Project);
        fixture.Write("parent/nested/three.csproj", GraphFixture.Project);
        fixture.Write("parent/nested2/two.CSPROJ", GraphFixture.Project);
        fixture.CommittedPaths = [fixture.PathOf("parent/document.md"),
            fixture.PathOf("parent/fixtures/version.json"),
            fixture.PathOf("parent/.cache/hidden.txt"),
            fixture.PathOf("parent/semi;colon.txt"),
            fixture.PathOf("parent/variant-only.txt"),
            fixture.PathOf("parent/nested/new.cs"), fixture.PathOf("parent/nested2/upper.cs"),
            fixture.PathOf("unowned.md")];
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
            Assert.DoesNotContain(fixture.PathOf("parent/nested2/upper.cs"), node.OwnedPaths);
            Assert.DoesNotContain(fixture.PathOf("unowned.md"), node.OwnedPaths);
            Assert.AreEqual(node.Dimension.TargetFramework == "net10.0",
                node.OwnedPaths.Contains(fixture.PathOf("parent/variant-only.txt")));
        }
        foreach ((string project, string owned) in new[]
        {
            ("parent/nested/one.csproj", "parent/nested/new.cs"),
            ("parent/nested/three.csproj", "parent/nested/new.cs"),
            ("parent/nested2/two.CSPROJ", "parent/nested2/upper.cs"),
        })
        {
            DotNetGraphNode node = Assert.ContainsSingle(response.Nodes.Where(node =>
                node.Identity.Project == fixture.PathOf(project)), project);
            CollectionAssert.AreEqual(new[] { fixture.PathOf(owned) }, node.OwnedPaths,
                project + " owns: " + string.Join("; ", node.OwnedPaths));
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
        // One restore covers default and redirected CPM, custom lock paths and a native RID.
        using var fixture = new GraphFixture();
        fixture.Write("dirs.proj", """
            <Project><ItemGroup><ProjectReference Include="multi.csproj" />
              <ProjectReference Include="main.csproj" /></ItemGroup></Project>
            """);
        fixture.Write("Directory.Build.props", File.ReadAllText(
            fixture.PathOf("Directory.Build.props")).Replace("</PropertyGroup>",
            "<DirectoryPackagesPropsPath Condition=\"'$(MSBuildProjectName)' == 'main'\"" +
            ">$(MSBuildThisFileDirectory)config/packages.props" +
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
        DotNetGraphNode consumer = Assert.ContainsSingle(response.Nodes.Where(node =>
            node.Identity.Project == fixture.PathOf("main.csproj")));
        Assert.AreEqual("linux-x64", consumer.Dimension.RuntimeIdentifier);
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
    public async Task MissingOrDifferentRestoreMetadataFailsWithoutAResponse()
    {
        using var fixture = new GraphFixture();
        fixture.Write("dirs.proj", """
            <Project><ItemGroup><ProjectReference Include="main.csproj" /></ItemGroup></Project>
            """);
        fixture.Write("main.csproj", GraphFixture.Project);
        await fixture.PrepareAsync(context.CancellationToken);
        string path = fixture.PathOf("obj/main/project.assets.json");
        string lockPath = fixture.PathOf("packages.main.lock.json");
        string rootConfiguration = fixture.PathOf("nuget.config");
        byte[] originalAssets = await File.ReadAllBytesAsync(path, context.CancellationToken);
        byte[] originalLock = await File.ReadAllBytesAsync(lockPath, context.CancellationToken);
        using var collection = new ProjectCollection();
        ProjectRootElement element = ProjectRootElement.Create(collection);
        element.FullPath = fixture.PathOf("main.csproj");
        element.AddProperty("ProjectAssetsFile", path);
        var project = new ProjectInstance(element);
        var consumer = new DotNetNodeIdentity(project.FullPath, fixture.Globals);
        // The one real locked restore is valid input to the app's validator.
        DotNetRestoreInputs valid = DotNetConfigurationInputs.Restore(project, consumer,
            fixture.Root, [rootConfiguration]);
        Assert.IsTrue(valid.Inputs.Any(input => input.Role == "RestoreLock" &&
            PhysicalPathComparer.Equals(input.Path, lockPath)));
        var format = new LockFileFormat();
        foreach (string scenario in new[] { "unsupported-format", "missing-project",
            "missing-restore", "unsupported-project-style", "wrong-project", "unlocked",
            "empty-configurations", "missing-root-contributor",
            "uncommitted-root-configuration", "missing-lock" })
        {
            context.WriteLine("Restore metadata case: " + scenario);
            await File.WriteAllBytesAsync(path, originalAssets, context.CancellationToken);
            await File.WriteAllBytesAsync(lockPath, originalLock, context.CancellationToken);
            string[] committed = [rootConfiguration];
            LockFile assets = format.Read(path);
            switch (scenario)
            {
                case "missing-lock": File.Delete(lockPath); break;
                case "unsupported-format": assets.Version = LockFileFormat.Version + 1; break;
                case "missing-project": assets.PackageSpec = null; break;
                case "missing-restore": assets.PackageSpec.RestoreMetadata = null; break;
                case "unsupported-project-style":
                    assets.PackageSpec.RestoreMetadata.ProjectStyle = ProjectStyle.Unknown;
                    break;
                case "wrong-project":
                    assets.PackageSpec.RestoreMetadata.ProjectPath =
                        fixture.PathOf("other.csproj");
                    break;
                case "unlocked":
                    assets.PackageSpec.RestoreMetadata.RestoreLockProperties =
                        new("true", null, false);
                    break;
                case "empty-configurations":
                    assets.PackageSpec.RestoreMetadata.ConfigFilePaths.Clear();
                    break;
                case "missing-root-contributor":
                    var configurations = assets.PackageSpec.RestoreMetadata.ConfigFilePaths;
                    string contributor = Assert.ContainsSingle(configurations.Where(
                        configuration => PhysicalPathComparer.Equals(
                            configuration, rootConfiguration)),
                        $"Expected one root contributor at {rootConfiguration}. " +
                        "Native contributors: " +
                        string.Join("; ", configurations));
                    Assert.IsTrue(configurations.Remove(contributor));
                    break;
                case "uncommitted-root-configuration":
                    committed = [];
                    break;
            }
            if (scenario != "missing-lock") format.Write(path, assets);
            Assert.ThrowsExactly<InvalidDataException>(() => DotNetConfigurationInputs.Restore(
                project, consumer, fixture.Root, committed), scenario);
        }

        // The native task passes its own committed paths to the validator: valid assets with
        // an uncommitted root configuration fail end to end and write no response.
        await File.WriteAllBytesAsync(path, originalAssets, context.CancellationToken);
        await File.WriteAllBytesAsync(lockPath, originalLock, context.CancellationToken);
        fixture.IncludeRootConfiguration = false;
        InvalidDataException failure = await Assert.ThrowsExactlyAsync<InvalidDataException>(
            () => fixture.ReadAsync(context.CancellationToken));
        Assert.Contains("locked restore metadata", failure.Message);
        Assert.IsFalse(File.Exists(fixture.PathOf("response.json")));
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
    [Timeout(30000, CooperativeCancellation = true)]
    public async Task NativeTestCapabilitiesFollowEvaluatedPrecedence()
    {
        // The precedence rule reads evaluated properties; non-SDK nodes need no restore.
        using var fixture = new GraphFixture();
        (string Project, string Mtp, string VSTest, string Expected)[] cases =
        [
            ("mtp-first.proj", "true", "true", "MTP"),
            ("uppercase-mtp.proj", "TRUE", "malformed", "MTP"),
            ("mtp-only.proj", "true", "false", "MTP"),
            ("vstest.proj", "false", "true", "VSTest"),
            ("uppercase-vstest.proj", "", "TRUE", "VSTest"),
            ("disabled.proj", "false", "false", "None"),
            ("unspecified.proj", "", "", "None"),
            ("uppercase-false.proj", "FALSE", "", "None"),
        ];
        fixture.Write("dirs.proj", "<Project><ItemGroup>" + string.Join("",
            cases.Select(value => $"<ProjectReference Include=\"{value.Project}\" />")) +
            "</ItemGroup></Project>");
        foreach (var value in cases)
            fixture.Write(value.Project, $"""
                <Project><PropertyGroup>
                  <IsTestingPlatformApplication>{value.Mtp}</IsTestingPlatformApplication>
                  <IsTestProject>{value.VSTest}</IsTestProject>
                </PropertyGroup></Project>
                """);
        DotNetGraphResponse response = await fixture.ReadAsync(context.CancellationToken);
        Assert.HasCount(1 + cases.Length, response.Nodes);
        foreach (var value in cases)
        {
            DotNetGraphNode node = Assert.ContainsSingle(response.Nodes.Where(node =>
                PhysicalPathComparer.Equals(node.Identity.Project, fixture.PathOf(value.Project))),
                value.Project);
            Assert.AreEqual(value.Expected, node.TestCapability, value.Project);
            Assert.AreEqual("Debug", node.Identity.Globals["Configuration"], value.Project);
            Assert.AreEqual("true", node.Identity.Globals["RestoreLockedMode"], value.Project);
        }
        Assert.AreEqual("None", response.Nodes.Single(node =>
            PhysicalPathComparer.Equals(node.Identity.Project, fixture.PathOf("dirs.proj")))
            .TestCapability);
    }

    [TestMethod]
    [DataRow("malformed", "true", "IsTestingPlatformApplication")]
    [DataRow("", "malformed", "IsTestProject")]
    [DataRow("false", "malformed", "IsTestProject")]
    [Timeout(30000, CooperativeCancellation = true)]
    public async Task MalformedRequiredTestCapabilitiesFailWithoutAResponse(
        string mtp, string vstest, string role)
    {
        using var fixture = new GraphFixture();
        fixture.Write("dirs.proj", $"""
            <Project><PropertyGroup>
              <IsTestingPlatformApplication>{mtp}</IsTestingPlatformApplication>
              <IsTestProject>{vstest}</IsTestProject>
            </PropertyGroup></Project>
            """);
        await fixture.PrepareAsync(context.CancellationToken);
        InvalidDataException error = await Assert.ThrowsExactlyAsync<InvalidDataException>(() =>
            fixture.ReadAsync(context.CancellationToken));
        Assert.Contains("Malformed required native Boolean: malformed", error.Message);
        Assert.Contains("role " + role, error.Message);
        Assert.Contains(GraphFixture.Revision, error.Message);
        Assert.Contains(fixture.Root, error.Message);
        Assert.IsTrue(error.Message.Contains(fixture.PathOf("dirs.proj"),
            OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase :
                StringComparison.Ordinal));
        Assert.Contains("RestoreLockedMode", error.Message);
        Assert.IsFalse(File.Exists(fixture.PathOf("response.json")));
    }

    [TestMethod]
    [Timeout(30000, CooperativeCancellation = true)]
    public async Task SupportedRepositoryProducersRetainNativeConsumers()
    {
        (string Origin, string Name)[] producers =
        [
            ("src/public/lib/CircularList/CircularList.csproj", "SetPackageReleaseNotes"),
            ("src/public/lib/Hjg.Pngcs/Hjg.Pngcs.csproj", "SetPackageReleaseNotes"),
            ("src/public/lib/Memoization/Memoization.csproj", "SetPackageReleaseNotes"),
            ("src/public/lib/MicrosoftExtensions.Logging.MSTest/" +
                "MicrosoftExtensions.Logging.MSTest.csproj", "SetPackageReleaseNotes"),
            ("src/public/lib/MicrosoftExtensions.Logging.Xunit/" +
                "MicrosoftExtensions.Logging.Xunit.csproj", "SetPackageReleaseNotes"),
            ("src/public/lib/MicrosoftExtensions.Options.DedupChangeExtensions/" +
                "MicrosoftExtensions.Options.DedupChangeExtensions.csproj",
                "SetPackageReleaseNotes"),
            ("src/public/lib/PhiFailureDetector/PhiFailureDetector.csproj",
                "SetPackageReleaseNotes"),
            ("src/public/lib/WebHdfs.Extensions.FileProviders/" +
                "WebHdfs.Extensions.FileProviders.csproj", "SetPackageReleaseNotes"),
            ("src/private/app/workflow-delivery/WorkflowDelivery.csproj", "ReferenceOfficialNbgv"),
            ("tests/private/app/workflow-delivery/WorkflowDelivery.Tests.csproj",
                "ReferenceOfficialNbgv"),
            ("tests/public/lib/WebHdfs.Extensions.FileProviders.UnitTest/" +
                "WebHdfs.Extensions.FileProviders.UnitTest.csproj", "PreBuild"),
        ];
        // One graph read: each origin wins its target in its own wrapper node, so every
        // supported (origin, name) entry is validated; names use non-canonical casing.
        using var fixture = new GraphFixture();
        for (int index = 0; index < producers.Length; index++)
        {
            fixture.Write(producers[index].Origin, "<Project><Target Name=\"" +
                producers[index].Name.ToLowerInvariant() + "\" /></Project>",
                restoreProject: false);
            fixture.Write($"wrappers/w{index}.proj", "<Project><Import Project=\"../" +
                producers[index].Origin + "\" /></Project>");
        }
        fixture.Write("dirs.proj", "<Project><ItemGroup>" + string.Join("",
            producers.Select((_, index) =>
                $"<ProjectReference Include=\"wrappers/w{index}.proj\" />")) +
            "</ItemGroup><Target Name=\"restorewindowsonlyprojectsonnonwindows\" /></Project>");
        fixture.CommittedPaths = [fixture.PathOf("dirs.proj"),
            .. producers.Select(producer => fixture.PathOf(producer.Origin))];
        DotNetGraphResponse response = await fixture.ReadAsync(context.CancellationToken);
        Assert.HasCount(1 + producers.Length, response.Nodes);
        Assert.IsTrue(response.Nodes.All(node => node.TestCapability == "None"));
        foreach ((string origin, string role, string consumer) in producers.Select(
                (producer, index) => (producer.Origin, "Import", $"wrappers/w{index}.proj"))
            .Append(("dirs.proj", "Project", "dirs.proj")))
        {
            DotNetGraphNode node = Assert.ContainsSingle(response.Nodes.Where(node =>
                PhysicalPathComparer.Equals(node.Identity.Project, fixture.PathOf(consumer))),
                consumer);
            DotNetGraphInput input = Assert.ContainsSingle(response.Inputs.Where(input =>
                PhysicalPathComparer.Equals(input.Path, fixture.PathOf(origin)) &&
                input.Role == role), origin);
            Assert.AreEqual("Evaluation", input.Stage, origin);
            Assert.AreEqual(node.Identity.Project, input.Consumer.Project, origin);
            CollectionAssert.AreEquivalent(node.Identity.Globals.ToArray(),
                input.Consumer.Globals.ToArray(), origin);
        }
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    [Timeout(30000, CooperativeCancellation = true)]
    public async Task NativeTargetShadowingUsesWinningOrigin(bool unknownWins)
    {
        using var fixture = new GraphFixture();
        const string origin = "src/private/app/workflow-delivery/WorkflowDelivery.csproj";
        fixture.Write("shadow.targets",
            "<Project><Target Name=\"ReferenceOfficialNbgv\" /></Project>");
        string import = "<Import Project=\"../../../../shadow.targets\" />";
        string target = "<Target Name=\"ReferenceOfficialNbgv\" />";
        fixture.Write(origin, "<Project>" + (unknownWins ? target + import : import + target) +
            "</Project>", restoreProject: false);
        fixture.Write("dirs.proj", $"<Project><Import Project=\"{origin}\" /></Project>");
        fixture.CommittedPaths = [fixture.PathOf(origin), fixture.PathOf("shadow.targets")];
        await fixture.PrepareAsync(context.CancellationToken);
        if (unknownWins)
        {
            InvalidDataException error = await Assert.ThrowsExactlyAsync<InvalidDataException>(() =>
                fixture.ReadAsync(context.CancellationToken));
            Assert.Contains("ReferenceOfficialNbgv", error.Message);
            Assert.IsTrue(error.Message.Contains(fixture.PathOf("shadow.targets"),
                OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase :
                StringComparison.Ordinal));
            Assert.IsFalse(File.Exists(fixture.PathOf("response.json")));
        }
        else
        {
            DotNetGraphResponse response = await fixture.ReadAsync(context.CancellationToken);
            Assert.HasCount(1, response.Nodes);
            Assert.HasCount(1, response.Inputs.Where(input => input.Role == "Import" &&
                PhysicalPathComparer.Equals(input.Path, fixture.PathOf("shadow.targets"))));
        }
    }

    [TestMethod]
    [DataRow("dirs.proj", "UnknownProducer")]
    [DataRow("dirs.proj", "ReferenceOfficialNbgv")]
    [DataRow("tests/private/app/workflow-delivery/" +
        "fixtures/products/hcoona-release-smoke-github-packages/" +
        "hcoona-release-smoke-github-packages.csproj",
        "ValidateWorkflowDeliveryFrozenInputs")]
    [Timeout(30000, CooperativeCancellation = true)]
    public async Task UnknownRepositoryProducersFailWithoutAResponse(string origin, string name)
    {
        using var fixture = new GraphFixture();
        fixture.Write(origin, $"<Project><Target Name=\"{name}\" /></Project>",
            restoreProject: false);
        if (origin != "dirs.proj")
            fixture.Write("dirs.proj", $"<Project><Import Project=\"{origin}\" /></Project>");
        fixture.CommittedPaths = [fixture.PathOf(origin)];
        await fixture.PrepareAsync(context.CancellationToken);
        InvalidDataException error = await Assert.ThrowsExactlyAsync<InvalidDataException>(() =>
            fixture.ReadAsync(context.CancellationToken));
        foreach (string expected in new[] { name, fixture.Root, GraphFixture.Revision,
            "RepositoryTarget", "RestoreLockedMode" })
            Assert.Contains(expected, error.Message);
        Assert.IsTrue(error.Message.Contains(fixture.PathOf(origin),
            OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase :
                StringComparison.Ordinal));
        Assert.IsFalse(File.Exists(fixture.PathOf("response.json")));
    }

    [TestMethod]
    [DataRow("obj/generated.targets")]
    [DataRow("packages/native/build/package.targets")]
    [Timeout(30000, CooperativeCancellation = true)]
    public async Task EnvironmentalTargetsInsideEndpointRemainNative(string origin)
    {
        using var fixture = new GraphFixture();
        fixture.Write(origin,
            "<Project><Target Name=\"UnknownEnvironmentalProducer\" /></Project>");
        fixture.Write("dirs.proj", $"<Project><Import Project=\"{origin}\" /></Project>");
        fixture.CommittedPaths = [fixture.PathOf("dirs.proj")];
        DotNetGraphResponse response = await fixture.ReadAsync(context.CancellationToken);
        Assert.HasCount(1, response.Nodes);
        DotNetGraphInput input = Assert.ContainsSingle(response.Inputs.Where(input =>
            input.Role == "Import" &&
            PhysicalPathComparer.Equals(input.Path, fixture.PathOf(origin))));
        Assert.AreEqual(response.Nodes[0].Identity.Project, input.Consumer.Project);
        Assert.AreEqual("Evaluation", input.Stage);
    }

    [TestMethod]
    [Timeout(60000, CooperativeCancellation = true)]
    public async Task NativeProvidersRetainResolvedSdkAndGeneratedRestoreImports()
    {
        using var fixture = new GraphFixture();
        fixture.EnableTraversalRestore();
        fixture.Write("global.json", """
            {"sdk":{"version":"10.0.401","rollForward":"disable"},
             "msbuild-sdks":{"Microsoft.Build.Traversal":"4.1.82",
             "MSTest.Sdk":"4.3.3","Microsoft.Build.Artifacts":"6.1.63"}}
            """);
        fixture.Write("dirs.proj", """
            <Project Sdk="Microsoft.Build.Traversal"><ItemGroup>
              <ProjectReference Include="main.csproj" />
            </ItemGroup></Project>
            """);
        fixture.Write("main.csproj", """
            <Project Sdk="MSTest.Sdk;Microsoft.Build.Artifacts"><PropertyGroup>
              <TargetFramework>net10.0</TargetFramework>
            </PropertyGroup></Project>
            """);
        fixture.Write("Directory.Build.props", """
            <Project><PropertyGroup>
              <BaseIntermediateOutputPath>obj/$(MSBuildProjectName)/</BaseIntermediateOutputPath>
              <RestorePackagesWithLockFile
                Condition="'$(MSBuildProjectExtension)' != '.proj'"
                >true</RestorePackagesWithLockFile>
              <NuGetLockFilePath>packages.$(MSBuildProjectName).lock.json</NuGetLockFilePath>
              <NuGetAudit>false</NuGetAudit>
              <CustomAfterArtifactsProps
                >$(MSBuildThisFileDirectory)extra.props</CustomAfterArtifactsProps>
            </PropertyGroup></Project>
            """);
        fixture.Write("extra.props", "<Project />");
        DotNetGraphResponse response = await fixture.ReadAsync(context.CancellationToken);
        foreach (string identity in new[] { "Microsoft.Build.Traversal", "MSTest.Sdk",
                     "Microsoft.Build.Artifacts" })
            Assert.IsTrue(response.Inputs.Any(input => input.Role == "Import" &&
                input.Provider is { Kind: "Sdk" } provider && provider.Identity == identity));
        DotNetGraphInput[] generated = response.Inputs.Where(input =>
            input.Provider?.Kind == "RestoreGenerated").ToArray();
        Assert.HasCount(4, generated);
        foreach (string project in new[] { "dirs.proj", "main.csproj" })
        {
            DotNetGraphNode consumer = Assert.ContainsSingle(response.Nodes.Where(node =>
                PhysicalPathComparer.Equals(node.Identity.Project, fixture.PathOf(project))));
            foreach (string suffix in new[] { "props", "targets" })
            {
                string path = fixture.PathOf("obj/" + Path.GetFileNameWithoutExtension(project) +
                    "/" + project + ".nuget.g." + suffix);
                DotNetGraphInput input = Assert.ContainsSingle(generated.Where(input =>
                    PhysicalPathComparer.Equals(input.Path, path)));
                Assert.IsTrue(SameIdentity(consumer.Identity, input.Consumer));
                Assert.AreEqual("Import", input.Role);
                Assert.AreEqual("Evaluation", input.Stage);
                Assert.AreEqual(new DotNetInputProvider("RestoreGenerated", path,
                    fixture.PathOf(project), ""), input.Provider);
            }
        }
        DotNetGraphNode root = Assert.ContainsSingle(response.Nodes.Where(node =>
            PhysicalPathComparer.Equals(node.Identity.Project, fixture.PathOf("dirs.proj"))));
        DotNetGraphInput rootAssets = Assert.ContainsSingle(response.Inputs.Where(input =>
            SameIdentity(root.Identity, input.Consumer) && input.Role == "RestoreAssets"));
        string assetsPath = fixture.PathOf("obj/dirs/project.assets.json");
        Assert.AreEqual(assetsPath, rootAssets.Path);
        Assert.AreEqual("LockedRestore", rootAssets.Stage);
        Assert.AreEqual(new DotNetInputProvider("RestoreAssets", assetsPath,
            root.Identity.Project, ""), rootAssets.Provider);
        Assert.IsFalse(response.Inputs.Any(input => SameIdentity(root.Identity, input.Consumer) &&
            input.Role == "RestoreLock"));
        Assert.IsTrue(response.Inputs.Any(input => input.Role == "RestoreLock" &&
            PhysicalPathComparer.Equals(input.Path, fixture.PathOf("packages.main.lock.json")) &&
            PhysicalPathComparer.Equals(input.Consumer.Project, fixture.PathOf("main.csproj"))));
        Assert.IsTrue(response.Inputs.Any(input => input.Provider?.Kind == "Toolset"));
        Assert.IsTrue(response.Inputs.Any(input => input.Provider?.Kind == "LockedPackage"));
        DotNetGraphInput hook = Assert.ContainsSingle(response.Inputs.Where(input =>
            input.Path == fixture.PathOf("extra.props") && input.Role == "Import"));
        Assert.IsNull(hook.Provider);
        Assert.AreEqual("Import", hook.Role);
    }

    [TestMethod]
    [Timeout(60000, CooperativeCancellation = true)]
    public async Task NativeScalarFileInputsRetainEffectiveConsumers()
    {
        using var fixture = new GraphFixture();
        fixture.Write("dirs.proj", """
            <Project><ItemGroup><ProjectReference Include="product/main.csproj" />
              <ProjectReference Include="empty.csproj" /></ItemGroup></Project>
            """);
        fixture.Write("product/main.csproj", """
            <Project Sdk="Microsoft.NET.Sdk"><PropertyGroup>
              <TargetFrameworks>net10.0;net9.0</TargetFrameworks>
              <ApplicationIcon>../linked/app.ico</ApplicationIcon>
              <ApplicationManifest>../linked/app.manifest</ApplicationManifest>
              <Win32Manifest>../linked/native.manifest</Win32Manifest>
              <Win32Resource
                Condition="'$(TargetFramework)' == 'net9.0'">../linked/app.res</Win32Resource>
            </PropertyGroup><ItemGroup><Manifest Include="../linked/package.manifest" />
            </ItemGroup></Project>
            """);
        fixture.Write("empty.csproj", GraphFixture.Project);
        foreach (string name in new[] { "app.ico", "app.manifest", "native.manifest", "app.res",
                     "package.manifest" })
            fixture.Write("linked/" + name, "fixture source");
        DotNetGraphResponse response = await fixture.ReadAsync(context.CancellationToken);
        foreach ((string role, string path) in new[]
        {
            ("ApplicationIcon", "app.ico"), ("ApplicationManifest", "app.manifest"),
            ("Win32Manifest", "native.manifest"), ("Manifest", "package.manifest"),
        })
        {
            DotNetGraphInput[] inputs = response.Inputs.Where(input => input.Role == role &&
                input.Consumer.Project == fixture.PathOf("product/main.csproj")).ToArray();
            Assert.HasCount(3, inputs);
            Assert.IsTrue(inputs.All(input => input.Path == fixture.PathOf("linked/" + path) &&
                input.Stage == "Evaluation"));
        }
        DotNetGraphInput resource = Assert.ContainsSingle(response.Inputs.Where(input =>
            input.Role == "Win32Resource"));
        Assert.AreEqual("net9.0", resource.Consumer.Globals["TargetFramework"]);
        Assert.AreEqual(fixture.PathOf("linked/app.res"), resource.Path);
        Assert.IsFalse(response.Inputs.Any(input => input.Consumer.Project ==
            fixture.PathOf("empty.csproj") && input.Role is "ApplicationIcon" or
                "ApplicationManifest" or "Win32Manifest" or "Win32Resource"));
    }

    [TestMethod]
    [Timeout(30000, CooperativeCancellation = true)]
    public async Task NativeControlReferencesRetainExternalCommittedConsumers()
    {
        using var fixture = new GraphFixture();
        fixture.Write("dirs.proj", "<Project><ItemGroup><ProjectReference Include=\"" +
            NodeGroupReader.ControlProject + "\" /></ItemGroup></Project>");
        fixture.Write(NodeGroupReader.ControlProject, GraphFixture.Project.Replace("</Project>",
            "<ItemGroup><Reference Include=\"NativeFixture\"><HintPath>" +
            "$(MSBuildThisFileDirectory)../../../../shared/fixture.dll" +
            "</HintPath></Reference></ItemGroup></Project>"));
        fixture.Write("shared/fixture.dll",
            "Committed fixture input; evaluation does not execute it.");
        fixture.CommittedPaths = [fixture.PathOf(NodeGroupReader.ControlProject), fixture.PathOf(
            "shared/fixture.dll")];
        DotNetGraphResponse response = await fixture.ReadAsync(context.CancellationToken);
        DotNetGraphInput reference = Assert.ContainsSingle(response.Inputs.Where(
            input => input.Role == "ReferenceHintPath"));
        Assert.AreEqual(fixture.PathOf("shared/fixture.dll"), reference.Path);
        Assert.AreEqual(fixture.PathOf(NodeGroupReader.ControlProject), reference.Consumer.Project);
        Assert.IsNull(reference.Provider);
    }

    [TestMethod]
    [DataRow("CompilerResponseFile")]
    [DataRow("AdditionalLibPaths")]
    [DataRow("KeyOriginatorFile")]
    [Timeout(30000, CooperativeCancellation = true)]
    public async Task UnadaptedControlIndirectInputsFailNativePlanning(string property)
    {
        using var fixture = new GraphFixture();
        fixture.Write("dirs.proj", "<Project><ItemGroup><ProjectReference Include=\"" +
            NodeGroupReader.ControlProject + "\" /></ItemGroup></Project>");
        fixture.Write(NodeGroupReader.ControlProject, GraphFixture.Project.Replace("</Project>",
            "<PropertyGroup><" + property + ">unadapted-file</" + property +
                "></PropertyGroup></Project>"));
        InvalidDataException failure = await Assert.ThrowsExactlyAsync<InvalidDataException>(() =>
            fixture.ReadAsync(context.CancellationToken));
        Assert.Contains("Indirect control inputs require a concrete native adapter",
            failure.Message);
    }

    [TestMethod]
    [DataRow("AddModules")]
    [DataRow("LinkResource")]
    [Timeout(30000, CooperativeCancellation = true)]
    public async Task UnadaptedControlCompilerItemsFailNativePlanning(string item)
    {
        using var fixture = new GraphFixture();
        fixture.Write("dirs.proj", "<Project><ItemGroup><ProjectReference Include=\"" +
            NodeGroupReader.ControlProject + "\" /></ItemGroup></Project>");
        fixture.Write(NodeGroupReader.ControlProject, GraphFixture.Project.Replace("</Project>",
            "<ItemGroup><" + item +
                " Include=\"../../../../shared/input.bin\" /></ItemGroup></Project>"));
        InvalidDataException failure = await Assert.ThrowsExactlyAsync<InvalidDataException>(() =>
            fixture.ReadAsync(context.CancellationToken));
        Assert.Contains("Indirect control inputs require a concrete native adapter",
            failure.Message);
    }

    [TestMethod]
    [DataRow("AppConfig")]
    [DataRow("AppConfigForCompiler")]
    [Timeout(30000, CooperativeCancellation = true)]
    public async Task NativeControlCompilerConfigurationRetainsCommittedConsumer(string property)
    {
        using var fixture = new GraphFixture();
        fixture.Write("dirs.proj", "<Project><ItemGroup><ProjectReference Include=\"" +
            NodeGroupReader.ControlProject + "\" /></ItemGroup></Project>");
        fixture.Write(NodeGroupReader.ControlProject, GraphFixture.Project.Replace("</Project>",
            "<PropertyGroup><" + property + ">../../../../shared/app.config</" + property +
                "></PropertyGroup></Project>"));
        fixture.Write("shared/app.config", "<configuration />");
        fixture.CommittedPaths = [fixture.PathOf(NodeGroupReader.ControlProject), fixture.PathOf(
            "shared/app.config")];
        DotNetGraphResponse response = await fixture.ReadAsync(context.CancellationToken);
        DotNetGraphInput input = Assert.ContainsSingle(response.Inputs.Where(
            input => input.Role == property));
        Assert.AreEqual(fixture.PathOf("shared/app.config"), input.Path);
        Assert.AreEqual(fixture.PathOf(NodeGroupReader.ControlProject), input.Consumer.Project);
        Assert.IsNull(input.Provider);
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
        private bool _restoreTraversal;
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
        internal void Write(string name, string content, bool restoreProject = true)
        {
            string path = PathOf(name);
            Directory.CreateDirectory(System.IO.Path.GetDirectoryName(path)!);
            File.WriteAllText(path, content);
            if (restoreProject && new[] { ".csproj", ".fsproj", ".vbproj" }.Contains(
                System.IO.Path.GetExtension(path), StringComparer.OrdinalIgnoreCase))
                _projects.Add(path);
        }

        internal void ConfigureNbgv(bool rootVersionBase)
        {
            EnableTraversalRestore();
            const string intermediatePath =
                "$(MSBuildThisFileDirectory)obj/$(MSBuildProjectName)/";
            const string lockPath =
                "$(MSBuildThisFileDirectory)packages.$(MSBuildProjectName).lock.json";
            const string auxiliaryPath = "$(MSBuildThisFileDirectory)shared/nbgv-aux.targets";
            const string sourceLinkCommon = "$(PkgMicrosoft_SourceLink_Common)/" +
                "build/Microsoft.SourceLink.Common.props";
            Write("global.json", """
                {"sdk":{"version":"10.0.401","rollForward":"disable"},
                 "msbuild-sdks":{"Microsoft.Build.Traversal":"4.1.82"}}
                """);
            Write("Directory.Build.props", $$"""
                <Project><PropertyGroup>
                  <BaseIntermediateOutputPath
                    >{{intermediatePath}}</BaseIntermediateOutputPath>
                  <RestorePackagesWithLockFile
                    Condition="'$(MSBuildProjectExtension)' != '.proj'"
                    >true</RestorePackagesWithLockFile>
                  <NuGetLockFilePath>{{lockPath}}</NuGetLockFilePath>
                  <NuGetAudit>false</NuGetAudit>
                  <ManagePackageVersionsCentrally>true</ManagePackageVersionsCentrally>
                  <GitVersionBaseDirectory>$(MSBuildThisFileDirectory)</GitVersionBaseDirectory>
                  <NBGV_PrivateP2PAuxTargets Condition="'$(MSBuildProjectName)' == 'consumer'"
                    >{{auxiliaryPath}}</NBGV_PrivateP2PAuxTargets>
                </PropertyGroup><ItemGroup>
                  <GlobalPackageReference Include="DotNet.ReproducibleBuilds" Version="2.0.5" />
                  <GlobalPackageReference Include="Nerdbank.GitVersioning" Version="3.10.94" />
                  <GlobalPackageReference Include="Microsoft.SourceLink.GitHub"
                    Version="10.0.401" />
                </ItemGroup></Project>
                """);
            Write("Directory.Packages.props", "<Project />");
            Write("dirs.proj", $$"""
                <Project Sdk="Microsoft.Build.Traversal">
                <PropertyGroup><TargetFramework>net10.0</TargetFramework></PropertyGroup>
                <ItemGroup>
                  <ProjectReference Include="product/consumer.csproj" />
                </ItemGroup>
                  <!-- Explicitly consume a restored transitive package asset;
                       the SDK supplies SourceLink by default. -->
                  <Import Project="{{sourceLinkCommon}}"
                    Condition="'$(PkgMicrosoft_SourceLink_Common)' != ''" />
                </Project>
                """);
            string versionBase = rootVersionBase ? "" :
                "<GitVersionBaseDirectory>$(MSBuildProjectDirectory)</GitVersionBaseDirectory>";
            Write("product/consumer.csproj", $$"""
                <Project Sdk="Microsoft.NET.Sdk"><PropertyGroup>
                  <TargetFrameworks>net10.0;net9.0</TargetFrameworks>
                  {{versionBase}}
                </PropertyGroup><ItemGroup>
                  <ProjectReference Include="../dependency/dependency.csproj" />
                  <ProjectReference Include="../analyzer/analyzer.csproj" OutputItemType="Analyzer"
                    ReferenceOutputAssembly="false" />
                </ItemGroup></Project>
                """);
            Write("dependency/dependency.csproj", Project.Replace("net10.0", "net9.0").Replace(
                "</Project>", "<ItemGroup><ProjectReference Include=\"../leaf/leaf.csproj\" />" +
                "</ItemGroup></Project>"));
            Write("leaf/leaf.csproj", Project.Replace("net10.0", "net9.0"));
            Write("analyzer/analyzer.csproj", Project);
            Write("version.json", """{"version":"1.0"}""");
            Write("product/version.json", """{"version":"2.0"}""");
            Write("shared/nbgv-aux.targets", """
                <Project><ItemGroup>
                  <AdditionalFiles Include="$(MSBuildThisFileDirectory)aux-input.txt" />
                </ItemGroup></Project>
                """);
            Write("shared/aux-input.txt", "Committed helper input.");
            CommittedPaths = [PathOf("global.json"), PathOf("Directory.Build.props"),
                PathOf("Directory.Packages.props"),
                PathOf("dirs.proj"), PathOf("product/consumer.csproj"),
                PathOf("dependency/dependency.csproj"), PathOf("leaf/leaf.csproj"),
                PathOf("analyzer/analyzer.csproj"),
                PathOf("version.json"), PathOf("product/version.json"),
                PathOf("shared/nbgv-aux.targets"), PathOf("shared/aux-input.txt")];
        }

        internal void EnableTraversalRestore() => _restoreTraversal = true;

        internal async Task PrepareAsync(CancellationToken token)
        {
            if (_prepared) return;
            if (_projects.Count != 0)
            {
                string solution = PathOf("restore.slnx");
                new System.Xml.Linq.XDocument(new System.Xml.Linq.XElement("Solution",
                    _projects.Order(StringComparer.Ordinal).Select(project =>
                        new System.Xml.Linq.XElement("Project",
                            new System.Xml.Linq.XAttribute("Path", project)))))
                    .Save(solution);
                // Observed on the pinned SDK 10.0.401 (not a documented contract): locked mode
                // writes absent fixture locks. If that changes, commit literal locks as data.
                await RestoreAsync(["restore", solution, "--locked-mode",
                    "-property:Configuration=Debug", "-property:ContinuousIntegrationBuild=true"],
                    token);
            }
            if (_restoreTraversal)
                await RestoreAsync(["restore", PathOf("dirs.proj"), "--locked-mode",
                    "-property:Configuration=Debug", "-property:ContinuousIntegrationBuild=true"],
                    token);
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
            NativeCommandResult query = await NativeProcess.ExecuteAsync(new("dotnet", Root,
                ["msbuild", PathOf("read.proj"), "-nologo", "-noAutoResponse", "-target:Read",
                    "-property:WrapperSentinel=wrapper-only"], 30), token);
            token.ThrowIfCancellationRequested();
            if (query.Termination == NativeTermination.TimedOut)
                throw new TimeoutException(query.Error);
            if (!query.Succeeded)
                throw new InvalidDataException(
                    $"Native graph {query.Termination}, exit {query.ExitCode}: " +
                    query.Error + "\n" + query.Stdout + query.Stderr);
            return JsonSerializer.Deserialize(await File.ReadAllTextAsync(
                PathOf("response.json"), token), TransferJson.Default.DotNetGraphResponse)
                ?? throw new InvalidDataException("Missing graph response.");
        }

        public void Dispose() => Directory.Delete(Root, true);
    }
}
