using Microsoft.VisualStudio.TestTools.UnitTesting;
using WorkflowDelivery.Repository;

namespace WorkflowDelivery.Tests.Repository;

[TestClass]
public sealed class RubyRepositoryInventoryTests
{
    private static readonly string Root = Path.GetFullPath("ruby inventory fixtures");
    private static string Absolute(string path) => Path.Combine(Root, path);
    private static RubyGemFacts Gem => new("native", "1.2.3", "ruby", ["lib"], [], []);
    private static GitRevision Revision(params string[] paths) =>
        new("candidate", paths.Select(path => new GitEntry(path, "100644", "blob")).ToArray());

    [TestMethod]
    public void DiscoverSeparatesSourcesAndBundleOnlyConsumers()
    {
        RubyRepositoryInventory result = RubyRepositoryInventory.Discover(Revision(
            "src/gem/native.gemspec", "src/gem/Gemfile", "node/client/Gemfile",
            "src/gem/spec/behavior_spec.rb", "src/gem/Gemfile.lock",
            "docs/Gemfile.md", "tests/products/fixture/fixture.gemspec"));
        Assert.HasCount(2, result.Sources);
        Assert.AreEqual(new RubySourceLocation("src/gem", "src/gem/native.gemspec"),
            result.Sources[0]);
        Assert.AreEqual(new RubySourceLocation("tests/products/fixture",
            "tests/products/fixture/fixture.gemspec"), result.Sources[1]);
        CollectionAssert.AreEqual(new RubyBundleLocation[] {
            new("node/client", "node/client/Gemfile"), new("src/gem", "src/gem/Gemfile") },
            result.Bundles);
        Assert.IsFalse(result.Sources.Any(source => source.Directory == "node/client"));
    }

    [TestMethod]
    [DataRow("120000", "blob", "src/gem/native.gemspec")]
    [DataRow("160000", "commit", "node/client/Gemfile")]
    [DataRow("100644", "blob", "../native.gemspec")]
    public void EntryPointsRequireRegularCommittedFiles(string mode, string kind, string path)
    {
        var revision = new GitRevision("candidate", [new(path, mode, kind)]);
        Assert.ThrowsExactly<InvalidDataException>(() =>
            RubyRepositoryInventory.Discover(revision));
    }

    [TestMethod]
    public void MultipleSourceGemspecsInOneDirectoryCannotInventAnOwner()
    {
        Assert.ThrowsExactly<InvalidDataException>(() => RubyRepositoryInventory.Discover(
            Revision("src/gem/first.gemspec", "src/gem/second.gemspec")));
    }

    [TestMethod]
    public void NativeAlternateBundleEntryNeedsNoParallelDeclaration()
    {
        var inventory = RubyRepositoryInventory.Discover(Revision("node/client/gems.rb",
            "node/client/gems.locked"));
        Assert.IsEmpty(inventory.Sources);
        Assert.AreEqual(new RubyBundleLocation("node/client", "node/client/gems.rb"),
            Assert.ContainsSingle(inventory.Bundles));
        RubyDependencyGraph graph = inventory.Bind(Root, [],
            [new(Absolute("node/client/gems.rb"), Absolute("node/client/gems.locked"),
                "2.7.2", [Absolute("node/client/gems.rb")], [], [])]);
        Assert.AreEqual("node/client/gems.rb", Assert.ContainsSingle(graph.Bundles).Gemfile);
    }

    [TestMethod]
    public void CompetingBundleEntryPointsCannotBeSelectedByWorkflow()
    {
        Assert.ThrowsExactly<InvalidDataException>(() => RubyRepositoryInventory.Discover(
            Revision("node/client/Gemfile", "node/client/gems.rb")));
    }

    [TestMethod]
    public void SourceEvaluationIsRequiredEvenWhenTheBundleDoesNotLoadItsGemspec()
    {
        var inventory = RubyRepositoryInventory.Discover(Revision("src/gem/native.gemspec",
            "src/gem/Gemfile", "src/gem/Gemfile.lock"));
        RubyDependencyGraph result = inventory.Bind(Root,
            [new(Absolute("src/gem/native.gemspec"), Gem)],
            [new(Absolute("src/gem/Gemfile"), Absolute("src/gem/Gemfile.lock"), "2.4.20",
                [Absolute("src/gem/Gemfile")], [], [])]);
        Assert.AreEqual("native", Assert.ContainsSingle(result.Projects).Name);
        Assert.AreEqual("src/gem", Assert.ContainsSingle(result.Bundles).Directory);
        Assert.IsEmpty(result.Projects[0].Ordinary);
    }

    [TestMethod]
    [DataRow("missing-source")]
    [DataRow("missing-bundle")]
    [DataRow("duplicate-source")]
    [DataRow("duplicate-bundle")]
    [DataRow("foreign-source")]
    [DataRow("foreign-bundle")]
    public void BindRejectsMissingDuplicateOrForeignNativeAnswers(string defect)
    {
        var inventory = RubyRepositoryInventory.Discover(Revision("src/gem/native.gemspec",
            "node/client/Gemfile"));
        RubySourceGemFacts source = new(Absolute("src/gem/native.gemspec"), Gem);
        RubyBundleFacts bundle = new(Absolute("node/client/Gemfile"),
            Absolute("node/client/Gemfile.lock"), "2.7.2", [], [], []);
        RubySourceGemFacts[] sources = [source];
        RubyBundleFacts[] bundles = [bundle];
        switch (defect)
        {
            case "missing-source": sources = []; break;
            case "missing-bundle": bundles = []; break;
            case "duplicate-source": sources = [source, source]; break;
            case "duplicate-bundle": bundles = [bundle, bundle]; break;
            case "foreign-source": sources = [source with
                { Gemspec = Absolute("other/native.gemspec") }]; break;
            case "foreign-bundle": bundles = [bundle with
                { Gemfile = Absolute("other/Gemfile") }]; break;
        }
        Assert.ThrowsExactly<InvalidDataException>(() => inventory.Bind(Root, sources, bundles));
    }
}
