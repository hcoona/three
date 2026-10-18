using Microsoft.VisualStudio.TestTools.UnitTesting;
using WorkflowDelivery.CI;
using WorkflowDelivery.Repository;

namespace WorkflowDelivery.Tests.Repository;

[TestClass]
public sealed class RubyInputConsumersTests
{
    private static readonly string Root = Path.GetFullPath("ruby input fixtures");
    private static readonly string[] ParentOnly = ["src/gem"];
    private static readonly string[] ChildOnly = ["src/gem/nested"];
    private static readonly string[] ProjectFiles = ["src/gem/native.gemspec",
        "src/gem/lib/native.rb", "src/gem/spec/native_spec.rb"];
    private static readonly string[] BundleFiles = ["node/client/Gemfile",
        "node/client/Gemfile.lock", "shared/dependencies.rb"];
    private static string Absolute(string path) => Path.Combine(Root, path);
    private static GitRevision Revision(params string[] extra) => new("candidate",
        ProjectFiles.Concat(extra).Select(path => new GitEntry(path, "100644", "blob"))
            .ToArray());
    private static RubySourceGemFacts Source(string directory = "src/gem", string name = "native")
        => new(Absolute(directory + "/native.gemspec"),
            new(name, "1.2.3", "ruby", ["lib"], ["lib/native.rb"], []));
    private static Dictionary<string, QualitySelection?> Quality(params string[] directories) =>
        directories.ToDictionary(directory => directory, _ => (QualitySelection?)null,
            StringComparer.Ordinal);
    private static RubyRepositoryInputs Map(GitRevision revision,
        RubySourceGemFacts[]? sources = null, RubyBundleFacts[]? bundles = null,
        NbgvInputs[]? versions = null,
        Dictionary<string, QualitySelection?>? quality = null) => RubyInputConsumers.Map(Root,
            revision, sources ?? [Source()], bundles ?? [], versions ?? [],
            quality ?? Quality("src/gem"));

    [TestMethod]
    public void ProjectInputsIncludeSpecsAndExcludeNestedAndNeighborSources()
    {
        RubyRepositoryInputs result = Map(Revision("src/gem/nested/native.gemspec",
            "src/gem/nested/spec/child_spec.rb", "src/gem-neighbor/spec/other_spec.rb"),
            [Source(), Source("src/gem/nested", "child")],
            quality: Quality("src/gem", "src/gem/nested"));
        CollectionAssert.AreEqual(ParentOnly, result.Inputs.Single(input =>
            input.Path == "src/gem/spec/native_spec.rb").Consumers);
        CollectionAssert.AreEqual(ChildOnly, result.Inputs.Single(input =>
            input.Path == "src/gem/nested/spec/child_spec.rb").Consumers);
        Assert.IsFalse(result.Inputs.Any(input => input.Path.StartsWith("src/gem-neighbor/",
            StringComparison.Ordinal)));
        Assert.HasCount(2, result.Graph.Projects);
    }

    [TestMethod]
    public void NativeVersionAndQualityInputsKeepActualConsumersAndAncestorCandidates()
    {
        var revision = Revision("shared/version-input.txt", "version.json",
            "src/workflow-delivery.quality.yml", "workflow-delivery.quality.yml");
        NbgvInputs version = new("candidate", "src/gem",
            ["version.json", "src/version.json", "src/gem/version.json"],
            ["shared/version-input.txt", "version.json"]);
        var quality = new Dictionary<string, QualitySelection?> {
            ["src/gem"] = new("ruby/rspec-v1", "src/workflow-delivery.quality.yml") };
        RubyRepositoryInputs result = Map(revision, versions: [version], quality: quality);
        foreach (string path in new[] { "shared/version-input.txt", "src/gem/version.json",
            "src/gem/workflow-delivery.quality.yml", "src/workflow-delivery.quality.yml" })
            CollectionAssert.AreEqual(ParentOnly, result.Inputs.Single(input =>
                input.Path == path).Consumers);
        Assert.IsFalse(result.Inputs.Any(input =>
            input.Path == "workflow-delivery.quality.yml"));
    }

    [TestMethod]
    public void BundleOnlyConsumersKeepNativeInputsAndLocalProducerIdentity()
    {
        RubySourceGemFacts source = Source();
        RubyBundleFacts bundle = new(Absolute("node/client/Gemfile"),
            Absolute("node/client/Gemfile.lock"), "2.7.2",
            [Absolute("node/client/Gemfile"), Absolute("shared/dependencies.rb")], ["native"],
            [new(new(source.Gem.Name, source.Gem.Version, source.Gem.Platform,
                source.Gem.RuntimeDependencies), new("path", Absolute("src/gem"), source.Gemspec,
                Absolute("installed/cache/native")))]);
        RubyRepositoryInputs result = Map(Revision("node/client/Gemfile",
            "node/client/Gemfile.lock", "shared/dependencies.rb"), bundles: [bundle]);
        Assert.AreEqual("src/gem", Assert.ContainsSingle(result.Graph.Projects).Directory);
        RubyBundleConsumption consumer = Assert.ContainsSingle(result.Graph.Bundles);
        Assert.AreEqual("node/client", consumer.Directory);
        CollectionAssert.AreEqual(ParentOnly, consumer.Producers);
        CollectionAssert.AreEqual(BundleFiles, consumer.Inputs);
        Assert.IsFalse(result.Inputs.Any(input => input.Path == "shared/dependencies.rb"));
        Assert.IsFalse(result.Inputs.Any(input => input.Path.Contains("installed/cache",
            StringComparison.Ordinal)));
    }

    [TestMethod]
    public void ProjectBundleCanConsumeAnEvaluatedInputOutsideItsDirectory()
    {
        RubyBundleFacts bundle = new(Absolute("src/gem/Gemfile"),
            Absolute("src/gem/Gemfile.lock"), "2.4.20",
            [Absolute("src/gem/Gemfile"), Absolute("shared/dependencies.rb")], [], []);
        RubyRepositoryInputs result = Map(Revision("src/gem/Gemfile", "src/gem/Gemfile.lock",
            "shared/dependencies.rb"), bundles: [bundle]);
        CollectionAssert.AreEqual(ParentOnly, result.Inputs.Single(input =>
            input.Path == "shared/dependencies.rb").Consumers);
        Assert.IsEmpty(Assert.ContainsSingle(result.Graph.Bundles).Producers);
    }

    [TestMethod]
    public void OnlyNativeBundleConsumersDoNotInventRubyOwners()
    {
        var revision = new GitRevision("candidate", [
            new("node/client/Gemfile", "100644", "blob"),
            new("node/client/Gemfile.lock", "100644", "blob")]);
        RubyBundleFacts bundle = new(Absolute("node/client/Gemfile"),
            Absolute("node/client/Gemfile.lock"), "2.7.2",
            [Absolute("node/client/Gemfile")], [], []);
        RubyRepositoryInputs result = Map(revision, [], [bundle], quality: Quality());
        Assert.IsEmpty(result.Graph.Projects);
        Assert.IsEmpty(result.Inputs);
        Assert.AreEqual("node/client", Assert.ContainsSingle(result.Graph.Bundles).Directory);
    }

    [TestMethod]
    public void AbsentRubyEntryPointsProduceAnExplainableEmptyInputSet()
    {
        RubyRepositoryInputs result = Map(new("candidate", [
            new("docs/guide.md", "100644", "blob")]), [], quality: Quality());
        Assert.IsEmpty(result.Graph.Projects);
        Assert.IsEmpty(result.Graph.Bundles);
        Assert.IsEmpty(result.Inputs);
    }

    [TestMethod]
    [DataRow("missing-quality-owner")]
    [DataRow("foreign-quality-owner")]
    [DataRow("uncommitted-quality")]
    [DataRow("foreign-quality-location")]
    [DataRow("foreign-version")]
    [DataRow("foreign-version-owner")]
    [DataRow("uncommitted-version-input")]
    [DataRow("duplicate-version")]
    [DataRow("uncommitted-bundle-input")]
    [DataRow("nonregular-project-input")]
    [DataRow("nonregular-version-input")]
    [DataRow("nonregular-quality-input")]
    public void UnresolvedInputOrOwnerCannotProduceBindings(string defect)
    {
        GitRevision revision = Revision("other/workflow-delivery.quality.yml");
        Dictionary<string, QualitySelection?> quality = Quality("src/gem");
        NbgvInputs version = new("candidate", "src/gem", [], ["src/gem/native.gemspec"]);
        NbgvInputs[] versions = [];
        RubyBundleFacts[] bundles = [];
        switch (defect)
        {
            case "missing-quality-owner": quality.Clear(); break;
            case "foreign-quality-owner": quality = Quality("other"); break;
            case "uncommitted-quality": quality["src/gem"] = new("ruby/rspec-v1",
                "src/workflow-delivery.quality.yml"); break;
            case "foreign-quality-location": quality["src/gem"] = new("ruby/rspec-v1",
                "other/workflow-delivery.quality.yml"); break;
            case "foreign-version": versions = [version with { Commit = "other" }]; break;
            case "foreign-version-owner": versions = [version with { Directory = "other" }]; break;
            case "uncommitted-version-input": versions = [version with
                { Paths = ["missing/input.txt"] }]; break;
            case "duplicate-version": versions = [version, version]; break;
            case "uncommitted-bundle-input":
                revision = revision with { Entries = [.. revision.Entries,
                    new("node/client/Gemfile", "100644", "blob")] };
                bundles = [new(Absolute("node/client/Gemfile"),
                    Absolute("node/client/Gemfile.lock"), "2.7.2", [], [], [])]; break;
            case "nonregular-project-input": revision = revision with { Entries =
                [.. revision.Entries, new("src/gem/link", "120000", "blob")] }; break;
            case "nonregular-version-input":
                revision = revision with { Entries = [.. revision.Entries,
                    new("shared/version-input.txt", "120000", "blob")] };
                versions = [version with { Paths = ["shared/version-input.txt"] }]; break;
            case "nonregular-quality-input": revision = revision with { Entries =
                [.. revision.Entries, new("workflow-delivery.quality.yml", "120000", "blob")] };
                break;
        }
        Assert.ThrowsExactly<InvalidDataException>(() =>
            Map(revision, bundles: bundles, versions: versions, quality: quality));
    }
}
