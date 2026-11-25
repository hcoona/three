using System.Text.Json;
using Microsoft.Build.Construction;
using Microsoft.Build.Evaluation;
using Microsoft.Build.Execution;
using NuGet.ProjectModel;
using WorkflowDelivery.CI;
using WorkflowDelivery.Repository;
using GraphFixture = WorkflowDelivery.Tests.Repository.DotNetGraphTaskFixture;
using static WorkflowDelivery.Tests.Repository.DotNetGraphTaskFixture;

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
        Assert.IsTrue(response.Edges.Any(edge => PhysicalPathComparer.Equals(
                edge.Consumer.Project, fixture.PathOf("dependency/dependency.csproj")) &&
            PhysicalPathComparer.Equals(edge.Dependency.Project,
                fixture.PathOf("leaf/leaf.csproj"))));
        // Both effective native values are retained: the consumer's local base and the root
        // base other projects inherit (the SDK fallback alone would be each project directory).
        foreach (DotNetGraphNode node in Nodes(response, fixture, "dependency/dependency.csproj"))
            Assert.IsTrue(PhysicalPathComparer.Equals(fixture.Root,
                Path.TrimEndingDirectorySeparator(node.VersionBaseDirectory)),
                "dependency version base: " + node.VersionBaseDirectory);
        DotNetGraphNode[] consumers = Nodes(response, fixture, "product/consumer.csproj").ToArray();
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
            DotNetGraphInput[] inputs = Inputs(response, node);
            if (node.OuterBuild)
            {
                Assert.IsTrue(inputs.Any(input => input.Role == "RestoreAssets"));
                continue;
            }
            foreach ((string path, string role) in new[]
            {
                ("shared/nbgv-aux.targets", "Import"),
                ("shared/aux-input.txt", "AdditionalFiles"),
            })
            {
                DotNetGraphInput input = Assert.ContainsSingle(inputs.Where(input =>
                    input.Role == role &&
                    PhysicalPathComparer.Equals(input.Path, fixture.PathOf(path))),
                    $"Expected {role} {path} for {node.Dimension.TargetFramework}.");
                Assert.AreEqual("Evaluation", input.Stage);
                Assert.IsNull(input.Provider);
            }
            foreach (string name in new[] { "PrivateP2PCaching.proj",
                "Nerdbank.GitVersioning.Inner.targets", "Nerdbank.GitVersioning.Common.targets" })
            {
                DotNetGraphInput[] imports = inputs.Where(input => input.Role == "Import" &&
                    Path.GetFileName(input.Path) == name).ToArray();
                Assert.IsNotEmpty(imports, name);
                Assert.IsTrue(imports.All(input => input.Stage == "Evaluation" &&
                    input.Provider is
                    {
                        Kind: "LockedPackage", Identity: "Nerdbank.GitVersioning"
                    } && input.Provider.Version == GraphFixture.NbgvVersion));
            }
        }
        Assert.IsTrue(response.Edges.All(edge => response.Nodes.Any(node =>
            SameIdentity(node.Identity, edge.Consumer)) && response.Nodes.Any(node =>
            SameIdentity(node.Identity, edge.Dependency))));
        Assert.IsTrue(response.Inputs.All(input => response.Nodes.Any(node =>
            SameIdentity(node.Identity, input.Consumer))));
        DotNetGraphNode root = Assert.ContainsSingle(Nodes(response, fixture, "dirs.proj"));
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
        DotNetGraphInput[] rootInputs = Inputs(response, root);
        Assert.IsFalse(rootInputs.Any(input => input.Role == "RestoreLock"));
        Assert.IsTrue(rootInputs.Any(input => input.Path == helper && input.Provider is
        {
            Kind: "TraversalPackage", Identity: "Nerdbank.GitVersioning"
        } && input.Provider.Version == GraphFixture.NbgvVersion));
        Assert.IsTrue(rootInputs.Any(input => input.Provider?.Kind == "RestoreGenerated"));
        Assert.IsTrue(rootInputs.Any(input => input.Role == "RestoreAssets"));
        // The traversal consumes a transitive restored package import with its package provider.
        DotNetGraphInput common = Assert.ContainsSingle(rootInputs.Where(input =>
            input.Role == "Import" &&
            Path.GetFileName(input.Path) == "Microsoft.SourceLink.Common.props"));
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
        await fixture.PrepareAsync(context.CancellationToken);
        InvalidDataException error = await RejectAsync(fixture);
        foreach (string expected in new[] { "RepositoryTarget", "UnsupportedNbgvAuxiliaryProducer",
            "nbgv-aux.targets", GraphFixture.Revision })
            Assert.Contains(expected, error.Message);
    }

    [TestMethod]
    [Timeout(360000, CooperativeCancellation = true)]
    public async Task NbgvHelperFromADifferentRestoredVersionFailsWithoutAResponse()
    {
        using var fixture = new GraphFixture();
        fixture.ConfigureNbgv(rootVersionBase: true);
        await fixture.PrepareAsync(context.CancellationToken);
        // The restored package supplies the helper; the consumer now declares another version.
        const string declared = "0.0.1-declared";
        // NuGet adds the global reference after project bodies, so update it from targets.
        fixture.WriteProject("Directory.Build.targets", $"""
            <ItemGroup Condition="'$(MSBuildProjectName)' == 'consumer'">
              <PackageReference Update="Nerdbank.GitVersioning" VersionOverride="{declared}" />
            </ItemGroup>
            """);
        InvalidDataException error = await RejectAsync(fixture);
        foreach (string expected in new[] { "NbgvCachingHelper", "declared " + declared,
            "restored " + GraphFixture.NbgvVersion })
            Assert.Contains(expected, error.Message);
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    [Timeout(60000, CooperativeCancellation = true)]
    public async Task UnsupportedMarkedNbgvHelperFailsWithoutAResponse(bool packageMissing)
    {
        using var fixture = new GraphFixture();
        fixture.Entry("consumer.csproj");
        fixture.SdkProject("consumer.csproj", body: Items(
            """<ProjectReference Include="PrivateP2PCaching.proj" NBGV_InnerProject="true" />"""));
        fixture.Write("PrivateP2PCaching.proj", "<Project />");
        if (!packageMissing)
            fixture.WriteProject("Directory.Build.targets", Items($"""
                <PackageReference Include="Nerdbank.GitVersioning"
                  Version="{GraphFixture.NbgvVersion}" PrivateAssets="all" />
                """));
        fixture.Commit("dirs.proj", "consumer.csproj", "PrivateP2PCaching.proj");
        InvalidDataException error = await RejectAsync(fixture);
        Assert.Contains("NBGV", error.Message);
    }

    private static bool SameIdentity(DotNetNodeIdentity first, DotNetNodeIdentity second) =>
        new DotNetRepositoryReader.NativeIdentityComparer().Equals(
            DotNetRepositoryReader.Key(first), DotNetRepositoryReader.Key(second));

    private static IEnumerable<DotNetGraphNode> Nodes(DotNetGraphResponse graph,
        GraphFixture fixture, string project) => graph.Nodes.Where(node =>
        PhysicalPathComparer.Equals(node.Identity.Project, fixture.PathOf(project)));

    private static DotNetGraphNode Node(DotNetGraphResponse graph, GraphFixture fixture,
        string project) => Assert.ContainsSingle(Nodes(graph, fixture, project), project);

    private static DotNetGraphInput[] Inputs(DotNetGraphResponse graph, DotNetGraphNode node) =>
        graph.Inputs.Where(input => SameIdentity(input.Consumer, node.Identity)).ToArray();

    private async Task<InvalidDataException> RejectAsync(GraphFixture fixture)
    {
        InvalidDataException error = await Assert.ThrowsExactlyAsync<InvalidDataException>(() =>
            fixture.ReadAsync(context.CancellationToken));
        Assert.IsFalse(File.Exists(fixture.PathOf("response.json")));
        return error;
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
        fixture.Entry("multi.csproj");
        fixture.SdkProject("multi.csproj", "net10.0;net9.0", restoreProject: false);
        fixture.Globals.Add("targetframework", "");
        InvalidDataException error = await RejectAsync(fixture);
        Assert.Contains("does not accept caller variant selectors", error.Message);
    }

    [TestMethod]
    [Timeout(30000, CooperativeCancellation = true)]
    public async Task MissingNativeReferenceFailsWithoutAResponse()
    {
        using var fixture = new GraphFixture();
        fixture.Entry("missing.csproj");
        await RejectAsync(fixture);
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    [Timeout(30000, CooperativeCancellation = true)]
    public async Task EndpointDependentSemanticGlobalFailsWithoutAResponse(bool trailingSeparator)
    {
        using var fixture = new GraphFixture();
        fixture.SdkProject("dirs.proj");
        fixture.RequestRoot = trailingSeparator
            ? fixture.Root + Path.DirectorySeparatorChar : fixture.Root;
        fixture.Globals.Add("CustomSourceDirectory", fixture.Root);
        await RejectAsync(fixture);
    }

    [TestMethod]
    [Timeout(30000, CooperativeCancellation = true)]
    public async Task NativeOwnershipPreservesNearestRegionsAndVariantExcludes()
    {
        // Same-directory projects share their region; a lone upper-case extension forms its own.
        using var fixture = new GraphFixture();
        fixture.Entry("parent/main.csproj", "parent/nested/one.csproj",
            "parent/nested/three.csproj", "parent/nested2/two.CSPROJ");
        fixture.SdkProject("parent/main.csproj", "net10.0;net9.0", body:
            Properties(Excludes("fixtures/**;semi%3bcolon.txt")) + """
            <PropertyGroup Condition="'$(TargetFramework)' == 'net9.0'">
              <DefaultItemExcludes>$(DefaultItemExcludes);variant-only.txt</DefaultItemExcludes>
            </PropertyGroup>
            """);
        fixture.SdkProject("parent/nested/one.csproj");
        fixture.SdkProject("parent/nested/three.csproj");
        fixture.SdkProject("parent/nested2/two.CSPROJ");
        fixture.Commit("parent/document.md", "parent/fixtures/version.json",
            "parent/.cache/hidden.txt", "parent/semi;colon.txt", "parent/variant-only.txt",
            "parent/nested/new.cs", "parent/nested2/upper.cs", "unowned.md");
        DotNetGraphResponse response = await fixture.ReadAsync(context.CancellationToken);
        DotNetGraphNode[] parent = Nodes(response, fixture, "parent/main.csproj")
            .Where(node => !node.OuterBuild).ToArray();
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
            DotNetGraphNode node = Node(response, fixture, project);
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
        fixture.Entry("main.csproj");
        fixture.SdkProject("main.csproj", body: Properties(Excludes(exclude)));
        fixture.Commit("source.cs");
        await fixture.PrepareAsync(context.CancellationToken);
        await RejectAsync(fixture);
    }

    [TestMethod]
    [Timeout(30000, CooperativeCancellation = true)]
    public async Task ExcludedNativeResourceRetainsItsExplicitConsumerAndImport()
    {
        using var fixture = new GraphFixture();
        fixture.Entry("parent/main.csproj");
        fixture.WriteProject("common.props", "");
        fixture.Write("shared.cs", "");
        fixture.Write("parent/fixtures/version.json", "{}");
        fixture.SdkProject("parent/main.csproj", before: """<Import Project="../common.props" />""",
            body: Properties(Excludes("fixtures/**")) +
                Items("""<EmbeddedResource Include="fixtures/version.json" />""",
                    """<Compile Include="../shared.cs" />"""));
        fixture.Commit("parent/fixtures/version.json", "shared.cs", "common.props");
        DotNetGraphResponse response = await fixture.ReadAsync(context.CancellationToken);
        DotNetGraphNode owner = Node(response, fixture, "parent/main.csproj");
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
        fixture.Entry("multi.csproj", "main.csproj");
        fixture.BuildProperties("""
            <DirectoryPackagesPropsPath Condition="'$(MSBuildProjectName)' == 'main'"
              >$(MSBuildThisFileDirectory)config/packages.props</DirectoryPackagesPropsPath>
            """);
        string cpm = Properties(
            "<ManagePackageVersionsCentrally>true</ManagePackageVersionsCentrally>");
        fixture.WriteProject("config/packages.props", cpm);
        fixture.WriteProject("Directory.Packages.props", cpm);
        fixture.SdkProject("main.csproj", body: Properties(
            "<RuntimeIdentifier>linux-x64</RuntimeIdentifier>", "<UseAppHost>false</UseAppHost>"));
        fixture.SdkProject("multi.csproj", "net10.0;net9.0",
            Properties("<NuGetLockFilePath>locks/multi.json</NuGetLockFilePath>"));
        fixture.Write("locks/.keep", "");
        DotNetGraphResponse response = await fixture.ReadAsync(context.CancellationToken);
        DotNetGraphNode[] managed = Nodes(response, fixture, "multi.csproj").ToArray();
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
                DotNetGraphInput input = Assert.ContainsSingle(response.Inputs.Where(input =>
                    PhysicalPathComparer.Equals(input.Path, fixture.PathOf(path)) &&
                    input.Role == role && SameIdentity(input.Consumer, node.Identity)),
                    $"Expected {role} at {path} for [{string.Join(", ", node.Identity.Globals)}].");
                Assert.AreEqual(stage, input.Stage);
            }
        }
        Assert.IsFalse(response.Inputs.Any(input => input.Role == "RestoreAssets" &&
            input.Consumer.Project == fixture.PathOf("dirs.proj")));
        DotNetGraphNode consumer = Assert.ContainsSingle(Nodes(response, fixture, "main.csproj"));
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
        fixture.Entry("main.csproj");
        fixture.SdkProject("main.csproj");
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
        void Reject(string scenario, string[] committed) =>
            Assert.ThrowsExactly<InvalidDataException>(() => DotNetConfigurationInputs.Restore(
                project, consumer, fixture.Root, committed), scenario);

        void RejectAssets(string scenario, Action<LockFile> change)
        {
            LockFile assets = format.Read(path);
            change(assets);
            format.Write(path, assets);
            Reject(scenario, [rootConfiguration]);
            File.WriteAllBytes(path, originalAssets);
        }

        RejectAssets("unsupported format", assets => assets.Version = LockFileFormat.Version + 1);
        RejectAssets("missing project", assets => assets.PackageSpec = null);
        RejectAssets("missing restore metadata",
            assets => assets.PackageSpec.RestoreMetadata = null);
        RejectAssets("unsupported style", assets =>
            assets.PackageSpec.RestoreMetadata.ProjectStyle = ProjectStyle.Unknown);
        RejectAssets("wrong project", assets =>
            assets.PackageSpec.RestoreMetadata.ProjectPath = fixture.PathOf("other.csproj"));
        RejectAssets("unlocked", assets =>
            assets.PackageSpec.RestoreMetadata.RestoreLockProperties = new("true", null, false));
        RejectAssets("empty configurations", assets =>
            assets.PackageSpec.RestoreMetadata.ConfigFilePaths.Clear());
        RejectAssets("missing root contributor", assets =>
        {
            var configurations = assets.PackageSpec.RestoreMetadata.ConfigFilePaths;
            string contributor = Assert.ContainsSingle(configurations.Where(configuration =>
                PhysicalPathComparer.Equals(configuration, rootConfiguration)));
            Assert.IsTrue(configurations.Remove(contributor));
        });
        Reject("uncommitted root configuration", []);
        File.Delete(lockPath);
        Reject("missing lock", [rootConfiguration]);
        await File.WriteAllBytesAsync(lockPath, originalLock, context.CancellationToken);

        // The native task passes its own committed paths to the validator: valid assets with
        // an uncommitted root configuration fail end to end and write no response.
        fixture.IncludeRootConfiguration = false;
        InvalidDataException failure = await RejectAsync(fixture);
        Assert.Contains("locked restore metadata", failure.Message);
    }

    [TestMethod]
    [Timeout(60000, CooperativeCancellation = true)]
    public async Task NativeAnalyzerConfigCandidatesPreserveAbsentLinkedPaths()
    {
        using var fixture = new GraphFixture();
        fixture.Entry("product/main.csproj");
        fixture.SdkProject("product/main.csproj",
            body: Items("""<Compile Include="../linked/source.cs" />"""));
        fixture.Write("linked/source.cs", "");
        DotNetGraphResponse response = await fixture.ReadAsync(context.CancellationToken);
        DotNetGraphNode consumer = Node(response, fixture, "product/main.csproj");
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
        fixture.Entry(cases.Select(value => value.Project).ToArray());
        foreach (var value in cases)
            fixture.WriteProject(value.Project, Capabilities(value.Mtp, value.VSTest));
        DotNetGraphResponse response = await fixture.ReadAsync(context.CancellationToken);
        Assert.HasCount(1 + cases.Length, response.Nodes);
        foreach (var value in cases)
        {
            DotNetGraphNode node = Assert.ContainsSingle(Nodes(response, fixture, value.Project),
                value.Project);
            Assert.AreEqual(value.Expected, node.TestCapability, value.Project);
            Assert.AreEqual("Debug", node.Identity.Globals["Configuration"], value.Project);
            Assert.AreEqual("true", node.Identity.Globals["RestoreLockedMode"], value.Project);
        }
        Assert.AreEqual("None", Nodes(response, fixture, "dirs.proj").Single()
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
        fixture.WriteProject("dirs.proj", Capabilities(mtp, vstest));
        await fixture.PrepareAsync(context.CancellationToken);
        InvalidDataException error = await RejectAsync(fixture);
        Assert.Contains("Malformed required native Boolean: malformed", error.Message);
        Assert.Contains("role " + role, error.Message);
        Assert.Contains(GraphFixture.Revision, error.Message);
        Assert.Contains(fixture.Root, error.Message);
        Assert.IsTrue(error.Message.Contains(fixture.PathOf("dirs.proj"),
            OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase :
                StringComparison.Ordinal));
        Assert.Contains("RestoreLockedMode", error.Message);
    }

    [TestMethod]
    [Timeout(30000, CooperativeCancellation = true)]
    public async Task NativeRequiredToolsAreSortedPerTestProject()
    {
        using var fixture = new GraphFixture();
        fixture.Entry("tools.proj", "plain.proj");
        fixture.WriteProject("tools.proj", Capabilities("true", "") + Items(
            """<ThreeRequiredTool Include="python" /><ThreeRequiredTool Include="npm" />""",
            """<ThreeRequiredTool Include="node" />"""));
        fixture.WriteProject("plain.proj", Capabilities("", "true"));
        DotNetGraphResponse response = await fixture.ReadAsync(context.CancellationToken);
        string[] Tools(string project) => Nodes(response, fixture, project).Single().RequiredTools!;
        CollectionAssert.AreEqual(Supported, Tools("tools.proj"));
        Assert.IsEmpty(Tools("plain.proj"));
        Assert.IsEmpty(Tools("dirs.proj"));
    }

    private static readonly string[] Supported = ["node", "npm", "python"];

    [TestMethod]
    [DataRow("<ThreeRequiredTool Include=\"ruby\" />", "true",
        "Unsupported required external tool: ruby")]
    [DataRow("<ThreeRequiredTool Include=\"node\" /><ThreeRequiredTool Include=\"node\" />",
        "true", "Repeated required external tool.")]
    [DataRow("<ThreeRequiredTool Include=\"npm\" />", "true",
        "Required external tool npm needs node.")]
    [DataRow("<ThreeRequiredTool Include=\"node\" Version=\"24\" />", "true",
        "Required tool declarations do not accept metadata.")]
    [DataRow("</ItemGroup><ItemDefinitionGroup><ThreeRequiredTool><Version>24</Version>" +
        "</ThreeRequiredTool></ItemDefinitionGroup><ItemGroup>" +
        "<ThreeRequiredTool Include=\"node\" />", "true",
        "Required tool declarations do not accept metadata.")]
    [DataRow("<ThreeRequiredTool Include=\"node\" />", "false",
        "Only test projects declare required tools.")]
    [Timeout(30000, CooperativeCancellation = true)]
    public async Task MalformedRequiredToolsFailWithoutAResponse(string items, string test,
        string reason)
    {
        using var fixture = new GraphFixture();
        fixture.WriteProject("dirs.proj", Capabilities(test, "") + Items(items));
        await fixture.PrepareAsync(context.CancellationToken);
        InvalidDataException error = await RejectAsync(fixture);
        Assert.Contains(reason, error.Message);
        Assert.Contains("role ThreeRequiredTool", error.Message);
    }

    [TestMethod]
    [Timeout(30000, CooperativeCancellation = true)]
    public async Task SupportedRepositoryProducersRetainNativeConsumers()
    {
        string[] releaseNotesProjects =
        [
            "CircularList", "Hjg.Pngcs", "Memoization", "MicrosoftExtensions.Logging.MSTest",
            "MicrosoftExtensions.Logging.Xunit",
            "MicrosoftExtensions.Options.DedupChangeExtensions", "PhiFailureDetector",
            "WebHdfs.Extensions.FileProviders",
        ];
        (string Origin, string Name)[] producers =
        [
            .. releaseNotesProjects.Select(name =>
                ($"src/public/lib/{name}/{name}.csproj", "SetPackageReleaseNotes")),
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
            fixture.WriteProject(producers[index].Origin,
                $"""<Target Name="{producers[index].Name.ToLowerInvariant()}" />""",
                restoreProject: false);
            fixture.WriteProject($"wrappers/w{index}.proj",
                Import("../" + producers[index].Origin));
        }
        fixture.WriteProject("dirs.proj",
            References(producers.Select((_, index) => $"wrappers/w{index}.proj").ToArray()) +
            """<Target Name="restorewindowsonlyprojectsonnonwindows" />""");
        fixture.Commit(["dirs.proj", .. producers.Select(producer => producer.Origin)]);
        DotNetGraphResponse response = await fixture.ReadAsync(context.CancellationToken);
        Assert.HasCount(1 + producers.Length, response.Nodes);
        Assert.IsTrue(response.Nodes.All(node => node.TestCapability == "None"));
        foreach ((string origin, string role, string consumer) in producers.Select(
                (producer, index) => (producer.Origin, "Import", $"wrappers/w{index}.proj"))
            .Append(("dirs.proj", "Project", "dirs.proj")))
        {
            DotNetGraphNode node = Node(response, fixture, consumer);
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
        const string target = """<Target Name="ReferenceOfficialNbgv" />""";
        fixture.WriteProject("shadow.targets", target);
        string import = Import("../../../../shadow.targets");
        fixture.WriteProject(origin, unknownWins ? target + import : import + target,
            restoreProject: false);
        fixture.WriteProject("dirs.proj", Import(origin));
        fixture.Commit(origin, "shadow.targets");
        await fixture.PrepareAsync(context.CancellationToken);
        if (unknownWins)
        {
            InvalidDataException error = await RejectAsync(fixture);
            Assert.Contains("ReferenceOfficialNbgv", error.Message);
            Assert.IsTrue(error.Message.Contains(fixture.PathOf("shadow.targets"),
                OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase :
                StringComparison.Ordinal));
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
        fixture.WriteProject(origin, $"""<Target Name="{name}" />""",
            restoreProject: false);
        if (origin != "dirs.proj")
            fixture.WriteProject("dirs.proj", Import(origin));
        fixture.Commit(origin);
        await fixture.PrepareAsync(context.CancellationToken);
        InvalidDataException error = await RejectAsync(fixture);
        foreach (string expected in new[] { name, fixture.Root, GraphFixture.Revision,
            "RepositoryTarget", "RestoreLockedMode" })
            Assert.Contains(expected, error.Message);
        Assert.IsTrue(error.Message.Contains(fixture.PathOf(origin),
            OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase :
                StringComparison.Ordinal));
    }

    [TestMethod]
    [DataRow("obj/generated.targets")]
    [DataRow("packages/native/build/package.targets")]
    [Timeout(30000, CooperativeCancellation = true)]
    public async Task EnvironmentalTargetsInsideEndpointRemainNative(string origin)
    {
        using var fixture = new GraphFixture();
        fixture.WriteProject(origin, """<Target Name="UnknownEnvironmentalProducer" />""");
        fixture.WriteProject("dirs.proj", Import(origin));
        fixture.Commit("dirs.proj");
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
        fixture.Write("global.json", RepositoryToolSelection.GlobalJson(
            "Microsoft.Build.Traversal", "MSTest.Sdk", "Microsoft.Build.Artifacts"));
        fixture.WriteProject("dirs.proj", References("main.csproj"), "Microsoft.Build.Traversal");
        fixture.SdkProject("main.csproj", sdk: "MSTest.Sdk;Microsoft.Build.Artifacts");
        fixture.BuildProperties("""
            <CustomAfterArtifactsProps
              >$(MSBuildThisFileDirectory)extra.props</CustomAfterArtifactsProps>
            """, traversal: true);
        fixture.WriteProject("extra.props", "");
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
            DotNetGraphNode consumer = Assert.ContainsSingle(Nodes(response, fixture, project));
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
        DotNetGraphNode root = Node(response, fixture, "dirs.proj");
        DotNetGraphInput[] rootInputs = Inputs(response, root);
        DotNetGraphInput rootAssets = Assert.ContainsSingle(rootInputs.Where(input =>
            input.Role == "RestoreAssets"));
        string assetsPath = fixture.PathOf("obj/dirs/project.assets.json");
        Assert.AreEqual(assetsPath, rootAssets.Path);
        Assert.AreEqual("LockedRestore", rootAssets.Stage);
        Assert.AreEqual(new DotNetInputProvider("RestoreAssets", assetsPath,
            root.Identity.Project, ""), rootAssets.Provider);
        Assert.IsFalse(rootInputs.Any(input => input.Role == "RestoreLock"));
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
        fixture.Entry("product/main.csproj", "empty.csproj");
        fixture.SdkProject("product/main.csproj", "net10.0;net9.0", Properties("""
            <ApplicationIcon>../linked/app.ico</ApplicationIcon>
            <ApplicationManifest>../linked/app.manifest</ApplicationManifest>
            <Win32Manifest>../linked/native.manifest</Win32Manifest>
            <Win32Resource
              Condition="'$(TargetFramework)' == 'net9.0'">../linked/app.res</Win32Resource>
            """) + Items("""<Manifest Include="../linked/package.manifest" />"""));
        fixture.SdkProject("empty.csproj");
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
        fixture.ControlProject(Items("""
            <Reference Include="NativeFixture">
              <HintPath>$(MSBuildThisFileDirectory)../../../../shared/fixture.dll</HintPath>
            </Reference>
            """));
        fixture.Write("shared/fixture.dll",
            "Committed fixture input; evaluation does not execute it.");
        fixture.Commit(NodeGroupReader.ControlProject, "shared/fixture.dll");
        DotNetGraphResponse response = await fixture.ReadAsync(context.CancellationToken);
        DotNetGraphInput reference = Assert.ContainsSingle(response.Inputs.Where(
            input => input.Role == "ReferenceHintPath"));
        Assert.AreEqual(fixture.PathOf("shared/fixture.dll"), reference.Path);
        Assert.AreEqual(fixture.PathOf(NodeGroupReader.ControlProject), reference.Consumer.Project);
        Assert.IsNull(reference.Provider);
    }

    [TestMethod]
    [DataRow("<CompilerResponseFile>unadapted-file</CompilerResponseFile>", null)]
    [DataRow("<AdditionalLibPaths>unadapted-file</AdditionalLibPaths>", null)]
    [DataRow("<KeyOriginatorFile>unadapted-file</KeyOriginatorFile>", null)]
    [DataRow(null, """<AddModules Include="../../../../shared/input.bin" />""")]
    [DataRow(null, """<LinkResource Include="../../../../shared/input.bin" />""")]
    [Timeout(30000, CooperativeCancellation = true)]
    public async Task UnadaptedControlInputsFailNativePlanning(string? property, string? item)
    {
        using var fixture = new GraphFixture();
        fixture.ControlProject(property is null ? Items(item!) : Properties(property));
        InvalidDataException failure = await RejectAsync(fixture);
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
        fixture.ControlProject(Properties(
            $"<{property}>../../../../shared/app.config</{property}>"));
        fixture.Write("shared/app.config", "<configuration />");
        fixture.Commit(NodeGroupReader.ControlProject, "shared/app.config");
        DotNetGraphResponse response = await fixture.ReadAsync(context.CancellationToken);
        DotNetGraphInput input = Assert.ContainsSingle(response.Inputs.Where(
            input => input.Role == property));
        Assert.AreEqual(fixture.PathOf("shared/app.config"), input.Path);
        Assert.AreEqual(fixture.PathOf(NodeGroupReader.ControlProject), input.Consumer.Project);
        Assert.IsNull(input.Provider);
    }

    private static string Excludes(string pattern) =>
        $"<DefaultItemExcludes>$(DefaultItemExcludes);{pattern}</DefaultItemExcludes>";

    private static string Import(string path) => $"""<Import Project="{path}" />""";

    private static string Capabilities(string mtp, string vstest) => Properties(
        $"<IsTestingPlatformApplication>{mtp}</IsTestingPlatformApplication>",
        $"<IsTestProject>{vstest}</IsTestProject>");
}
