using Microsoft.VisualStudio.TestTools.UnitTesting;
using WorkflowDelivery.Repository;

namespace WorkflowDelivery.Tests.Repository;

[TestClass]
public sealed class RubyDependencyProjectionTests
{
    private static readonly string Root = Path.GetFullPath("ruby dependency fixtures");
    private static readonly string[] FromA = ["src/b", "src/c"];
    private static readonly string[] AllProducers = ["src/a", "src/b", "src/c"];
    private static readonly string[] BothProducers = ["src/a", "src/b"];
    private static readonly string[] BundleInputs = ["node/client/dependencies.rb",
        "node/client/gems.locked", "node/client/gems.rb"];
    private static string Absolute(string path) => Path.Combine(Root,
        path.Replace('/', Path.DirectorySeparatorChar));
    private static RubyGemFacts Gem(string name, params string[] dependencies) =>
        new(name, "1.2.3", "ruby", ["lib"], ["lib/native.rb"],
            dependencies.Select(name => new RubyDependency(name, ">= 1.0")).ToArray());
    private static RubySourceGemFacts Source(string name) =>
        new(Absolute("src/" + name + "/" + name + ".gemspec"), Gem(name));
    private static RubyBundleSpecification Local(string name, string kind = "path",
        params string[] dependencies) => new(Gem(name, dependencies), new(kind,
            Absolute("src/" + name), Source(name).Gemspec,
            Path.GetFullPath(Path.Combine(Root, "..", "installed", name))));
    private static RubyBundleFacts Bundle(string directory,
        string[] direct, params RubyBundleSpecification[] specifications) =>
        new(Absolute(directory + "/gems.rb"), Absolute(directory + "/gems.locked"),
            "2.7.2", [Absolute(directory + "/gems.rb"),
                Absolute(directory + "/dependencies.rb")], direct, specifications);

    [TestMethod]
    [DataRow("path")]
    [DataRow("local_git")]
    public void NativeLocalIdentityAndTransitiveEdgesReachBundleOnlyConsumers(string kind)
    {
        RubySourceGemFacts[] sources = [Source("a"), Source("b"), Source("c")];
        RubyBundleFacts bundle = Bundle("node/client", ["wrapper"],
            new(Gem("wrapper", "a"), new("rubygems")),
            Local("a", kind, "b"), Local("b", "path", "c"), Local("c"),
            new(Gem("bundler"), new("metadata")));
        RubyDependencyGraph result = RubyDependencyProjection.Project(Root, sources, [bundle]);
        Assert.HasCount(3, result.Projects);
        CollectionAssert.AreEqual(FromA,
            result.Projects.Single(project => project.Name == "a").Ordinary);
        Assert.AreEqual("src/c", Assert.ContainsSingle(result.Projects.Single(project =>
            project.Name == "b").Ordinary));
        Assert.IsEmpty(result.Projects.Single(project => project.Name == "c").Ordinary);
        RubyBundleConsumption consumer = Assert.ContainsSingle(result.Bundles);
        Assert.AreEqual("node/client", consumer.Directory);
        Assert.AreEqual("node/client/gems.rb", consumer.Gemfile);
        CollectionAssert.AreEqual(AllProducers, consumer.Producers);
        CollectionAssert.AreEqual(BundleInputs, consumer.Inputs);
        Assert.IsFalse(result.Projects.Any(project => project.Directory == consumer.Directory));
        Assert.IsEmpty(sources[0].Gem.RuntimeDependencies);
        Assert.AreEqual("b", bundle.Specifications[1].Gem.RuntimeDependencies[0].Name);
    }

    [TestMethod]
    public void AProjectBundleKeepsItsDependenciesWithoutSelfEdges()
    {
        RubySourceGemFacts[] sources = [Source("a"), Source("b")];
        RubyBundleFacts bundle = Bundle("src/a", ["a"],
            Local("a", "path", "b"), Local("b"));
        RubyDependencyGraph result = RubyDependencyProjection.Project(Root, sources, [bundle]);
        Assert.AreEqual("src/b", Assert.ContainsSingle(result.Projects.Single(project =>
            project.Name == "a").Ordinary));
        Assert.AreEqual("src/b", Assert.ContainsSingle(result.Bundles[0].Producers));
        Assert.DoesNotContain("src/a", result.Projects[0].Ordinary);
        Assert.HasCount(2, result.Projects);
    }

    [TestMethod]
    public void RegistryNamesDoNotInventLocalProducerRelations()
    {
        RubySourceGemFacts[] sources = [Source("a") with { Gem = Gem("a", "b") }, Source("b")];
        RubyBundleFacts bundle = Bundle("node/client", ["a"],
            new(Gem("a", "b"), new("rubygems")), new(Gem("b"), new("rubygems")));
        RubyDependencyGraph result = RubyDependencyProjection.Project(Root, sources, [bundle]);
        Assert.IsEmpty(result.Bundles[0].Producers);
        Assert.IsTrue(result.Projects.All(project => project.Ordinary.Length == 0));
        Assert.AreEqual("node/client", result.Bundles[0].Directory);
    }

    [TestMethod]
    public void NativeCyclesRemainFiniteAndPreserveBothLocalDirections()
    {
        RubyDependencyGraph result = RubyDependencyProjection.Project(Root,
            [Source("a"), Source("b")], [Bundle("node/client", ["a"],
                Local("a", "path", "b"), Local("b", "path", "a"))]);
        Assert.AreEqual("src/b", Assert.ContainsSingle(result.Projects[0].Ordinary));
        Assert.AreEqual("src/a", Assert.ContainsSingle(result.Projects[1].Ordinary));
        CollectionAssert.AreEqual(BothProducers, result.Bundles[0].Producers);
    }

    [TestMethod]
    public void ASourceGemNeedsNoInventedBundleOrDependencyResolution()
    {
        RubyDependencyGraph result = RubyDependencyProjection.Project(Root, [Source("a")], []);
        RubyProjectDependencies project = Assert.ContainsSingle(result.Projects);
        Assert.AreEqual("src/a", project.Directory);
        Assert.AreEqual("a", project.Name);
        Assert.IsEmpty(project.Ordinary);
        Assert.IsEmpty(result.Bundles);
    }

    [TestMethod]
    [DataRow("missing-owner")]
    [DataRow("wrong-name")]
    [DataRow("escaped-source")]
    [DataRow("installed-as-source")]
    [DataRow("path-mismatch")]
    [DataRow("missing-direct")]
    [DataRow("missing-edge")]
    [DataRow("duplicate-spec")]
    [DataRow("outside-gemfile")]
    [DataRow("duplicate-bundle")]
    [DataRow("duplicate-source")]
    [DataRow("duplicate-name")]
    public void IncompleteOrAmbiguousNativeOwnershipCannotProduceAGraph(string defect)
    {
        RubySourceGemFacts[] sources = [Source("a")];
        RubyBundleSpecification local = Local("a");
        RubyBundleFacts bundle = Bundle("node/client", ["a"], local);
        switch (defect)
        {
            case "missing-owner": sources = []; break;
            case "wrong-name": sources = [sources[0] with { Gem = Gem("other") }]; break;
            case "escaped-source":
                local = local with { Source = local.Source with
                    { Path = Path.GetFullPath(Path.Combine(Root, "..", "outside")) } }; break;
            case "installed-as-source":
                local = local with { Source = local.Source with
                    { Gemspec = Absolute("cache/a/a.gemspec") } }; break;
            case "path-mismatch":
                local = local with { Source = local.Source with
                    { Path = Absolute("src/unrelated") } }; break;
            case "missing-direct": bundle = bundle with { DirectDependencies = ["missing"] }; break;
            case "missing-edge": local = local with { Gem = Gem("a", "missing") }; break;
            case "duplicate-spec": bundle = bundle with { Specifications = [local, local] }; break;
            case "outside-gemfile": bundle = bundle with
                { Gemfile = Path.GetFullPath(Path.Combine(Root, "..", "Gemfile")) }; break;
            case "duplicate-source": sources = [sources[0], sources[0]]; break;
            case "duplicate-name": sources = [sources[0],
                new(Absolute("src/other/a.gemspec"), Gem("a"))]; break;
        }
        if (defect is "escaped-source" or "installed-as-source" or "path-mismatch" or
            "missing-edge")
            bundle = bundle with { Specifications = [local] };
        RubyBundleFacts[] bundles = defect == "duplicate-bundle" ? [bundle, bundle] : [bundle];
        Assert.ThrowsExactly<InvalidDataException>(() =>
            RubyDependencyProjection.Project(Root, sources, bundles));
    }
}
