using System.Text.Json.Nodes;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using WorkflowDelivery.Repository;

namespace WorkflowDelivery.Tests.Repository;

[TestClass]
public sealed class RubyNativeFactsReaderTests
{
    private static string NativePath(string path) => Path.GetFullPath(
        Path.Combine("native-ruby-fixtures", path.Replace('/', Path.DirectorySeparatorChar)));

    private static JsonObject Gem() => JsonNode.Parse("""
        {"name":"native-gem","version":"1.2.3","platform":"ruby",
         "require_paths":["lib"],"files":["lib/native.rb"],
         "runtime_dependencies":[{"name":"dependency","requirement":">= 1.0, < 2.0"}]}
        """)!.AsObject();

    private static JsonObject Bundle(string kind = "rubygems")
    {
        JsonObject specification = Gem();
        var source = new JsonObject { ["kind"] = kind };
        if (kind is "path" or "local_git")
        {
            source["path"] = NativePath("original/source");
            source["gemspec"] = NativePath("original/source/native.gemspec");
            source["directory"] = NativePath("installed/cache/native-gem-1.2.3");
        }
        if (kind == "git")
        {
            source["uri"] = "git@example.invalid:repository/native.git";
            source["revision"] = "native-git-revision";
        }
        specification["source"] = source;
        return new JsonObject
        {
            ["gemfile"] = NativePath("bundle/gems.rb"),
            ["lockfile"] = NativePath("bundle/gems.locked"),
            ["bundler_version"] = "2.7.2",
            ["locked_bundler_version"] = "2.7.2",
            ["gemfiles"] = new JsonArray(NativePath("bundle/gems.rb"),
                NativePath("bundle/nested.rb")),
            ["direct_dependencies"] = new JsonArray("native-gem"),
            ["specifications"] = new JsonArray(specification)
        };
    }

    private static RubyBundleFacts Parse(JsonObject bundle) => RubyNativeFactsReader.ParseBundle(
        bundle.ToJsonString(), NativePath("bundle/gems.rb"), "2.7.2");

    [TestMethod]
    public void SourceFactsPreserveNativeMetadataWithoutInventingTestInputs()
    {
        JsonObject value = Gem();
        value["gemspec"] = NativePath("source/native.gemspec");
        RubySourceGemFacts result = RubyNativeFactsReader.ParseSource(value.ToJsonString(),
            NativePath("source/native.gemspec"));
        Assert.AreEqual(NativePath("source/native.gemspec"), result.Gemspec);
        Assert.AreEqual("native-gem", result.Gem.Name);
        Assert.AreEqual("1.2.3", result.Gem.Version);
        Assert.AreEqual("ruby", result.Gem.Platform);
        Assert.AreEqual("lib", Assert.ContainsSingle(result.Gem.RequirePaths));
        Assert.AreEqual("lib/native.rb", Assert.ContainsSingle(result.Gem.Files));
        Assert.AreEqual(new RubyDependency("dependency", ">= 1.0, < 2.0"),
            Assert.ContainsSingle(result.Gem.RuntimeDependencies));
    }

    [TestMethod]
    [DataRow("path")]
    [DataRow("local_git")]
    public void LocalSourcesKeepOriginalAndInstalledIdentitiesSeparate(string kind)
    {
        RubyBundleFacts result = Parse(Bundle(kind));
        RubyBundleSpecification specification = Assert.ContainsSingle(result.Specifications);
        Assert.AreEqual(kind, specification.Source.Kind);
        Assert.AreEqual(NativePath("original/source"), specification.Source.Path);
        Assert.AreEqual(NativePath("original/source/native.gemspec"), specification.Source.Gemspec);
        Assert.AreEqual(NativePath("installed/cache/native-gem-1.2.3"),
            specification.Source.Directory);
        Assert.AreNotEqual(specification.Source.Path, specification.Source.Directory);
        Assert.AreEqual(NativePath("bundle/gems.locked"), result.Lockfile);
        Assert.AreEqual("2.7.2", result.BundlerVersion);
        CollectionAssert.AreEqual(new[] { NativePath("bundle/gems.rb"),
            NativePath("bundle/nested.rb") }, result.Gemfiles);
        Assert.AreEqual("native-gem", Assert.ContainsSingle(result.DirectDependencies));
        Assert.AreEqual(">= 1.0, < 2.0", specification.Gem.RuntimeDependencies[0].Requirement);
    }

    [TestMethod]
    [DataRow("rubygems")]
    [DataRow("metadata")]
    [DataRow("git")]
    public void NonlocalSourcesDoNotGainRepositoryPaths(string kind)
    {
        RubyNativeSource source = Assert.ContainsSingle(Parse(Bundle(kind)).Specifications).Source;
        Assert.AreEqual(kind, source.Kind);
        Assert.IsNull(source.Path);
        Assert.IsNull(source.Gemspec);
        Assert.IsNull(source.Directory);
        Assert.AreEqual(kind == "git" ? "git@example.invalid:repository/native.git" : null,
            source.Uri);
        Assert.AreEqual(kind == "git" ? "native-git-revision" : null, source.Revision);
    }

    [TestMethod]
    public void EmptyNativeCollectionsRemainEmpty()
    {
        JsonObject value = Bundle();
        value["direct_dependencies"] = new JsonArray();
        value["specifications"] = new JsonArray();
        RubyBundleFacts bundle = Parse(value);
        Assert.IsEmpty(bundle.DirectDependencies);
        Assert.IsEmpty(bundle.Specifications);
        JsonObject gem = Gem();
        gem["gemspec"] = NativePath("source/native.gemspec");
        gem["files"] = new JsonArray();
        gem["runtime_dependencies"] = new JsonArray();
        RubySourceGemFacts source = RubyNativeFactsReader.ParseSource(gem.ToJsonString(),
            NativePath("source/native.gemspec"));
        Assert.IsEmpty(source.Gem.Files);
        Assert.IsEmpty(source.Gem.RuntimeDependencies);
    }

    [TestMethod]
    public void ArchiveFactsRetainNativeContentAnswerIndependentlyOfSourceFiles()
    {
        JsonObject value = Gem();
        value["archive"] = NativePath("outputs/native.gem");
        value["contents"] = new JsonArray("lib/", "lib/native.rb");
        RubyArchiveFacts result = RubyNativeFactsReader.ParseArchive(value.ToJsonString(),
            NativePath("outputs/native.gem"));
        Assert.AreEqual(NativePath("outputs/native.gem"), result.Archive);
        Assert.AreEqual("native-gem", result.Gem.Name);
        Assert.HasCount(2, result.Contents);
        Assert.AreEqual("lib/", result.Contents[0]);
        Assert.AreEqual("lib/native.rb", result.Contents[1]);
        Assert.AreEqual("lib/native.rb", Assert.ContainsSingle(result.Gem.Files));
    }

    [TestMethod]
    [DataRow("gemfile")]
    [DataRow("bundler_version")]
    [DataRow("locked_bundler_version")]
    [DataRow("gemfiles")]
    public void SubstitutedBundleContextCannotSupplyFacts(string field)
    {
        JsonObject value = Bundle();
        value[field] = field == "gemfiles" ? new JsonArray(NativePath("foreign/gems.rb")) :
            JsonValue.Create(field == "gemfile" ? NativePath("foreign/gems.rb") : "2.4.20");
        Assert.ThrowsExactly<InvalidDataException>(() => Parse(value));
    }

    [TestMethod]
    [DataRow("source")]
    [DataRow("archive")]
    public void SourceAndArchiveAnswersMustMatchTheirRequests(string operation)
    {
        JsonObject value = Gem();
        if (operation == "source")
        {
            value["gemspec"] = NativePath("foreign/native.gemspec");
            Assert.ThrowsExactly<InvalidDataException>(() => RubyNativeFactsReader.ParseSource(
                value.ToJsonString(), NativePath("source/native.gemspec")));
        }
        else
        {
            value["archive"] = NativePath("foreign/native.gem");
            value["contents"] = new JsonArray("lib/native.rb");
            Assert.ThrowsExactly<InvalidDataException>(() => RubyNativeFactsReader.ParseArchive(
                value.ToJsonString(), NativePath("outputs/native.gem")));
        }
    }

    [TestMethod]
    [DataRow("truncated")]
    [DataRow("missing-specifications")]
    [DataRow("missing-dependencies")]
    [DataRow("wrong-array-type")]
    [DataRow("unknown-source")]
    [DataRow("relative-local-source")]
    public void IncompleteOrUnsupportedNativeAnswersFail(string scenario)
    {
        JsonObject value = Bundle("path");
        JsonObject specification = value["specifications"]![0]!.AsObject();
        switch (scenario)
        {
            case "missing-specifications": value.Remove("specifications"); break;
            case "missing-dependencies": specification.Remove("runtime_dependencies"); break;
            case "wrong-array-type": value["direct_dependencies"] = 3; break;
            case "unknown-source": specification["source"]!["kind"] = "unknown"; break;
            case "relative-local-source": specification["source"]!["path"] = "relative"; break;
        }
        string text = scenario == "truncated" ? "{" : value.ToJsonString();
        Assert.ThrowsExactly<InvalidDataException>(() => RubyNativeFactsReader.ParseBundle(
            text, NativePath("bundle/gems.rb"), "2.7.2"));
    }
}
