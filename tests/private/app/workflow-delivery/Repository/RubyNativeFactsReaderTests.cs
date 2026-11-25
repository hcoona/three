using System.Text.Json;
using WorkflowDelivery.Platform;
using System.Text.Json.Nodes;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using WorkflowDelivery.Repository;

namespace WorkflowDelivery.Tests.Repository;

[TestClass]
public sealed class RubyNativeFactsReaderTests
{
    [TestMethod]
    [DataRow("context", "gemfile", "other/Gemfile")]
    [DataRow("context", "bundler_version", "2.4.20")]
    [DataRow("context", "locked_bundler_version", null)]
    [DataRow("context", "lockfile", "relative.lock")]
    [DataRow("version", "gemspec", "other/native.gemspec")]
    [DataRow("version", "raw", "other-version")]
    [DataRow("version", "version", null)]
    [DataRow("version", "literal", null)]
    public void PreparationRejectsIncompleteOrSubstitutedNativeAnswers(string operation,
        string field, string? replacement)
    {
        string gemfile = NativePath("bundle/Gemfile");
        string gemspec = NativePath("source/native.gemspec");
        const string raw = "1.2.3-beta.4+gabcdef0";
        JsonObject answer = operation == "context"
            ? new()
            {
                ["gemfile"] = gemfile,
                ["lockfile"] = gemfile + ".lock",
                ["bundler_version"] = "2.7.2",
                ["locked_bundler_version"] = "2.4.20"
            }
            : new()
            {
                ["gemspec"] = gemspec,
                ["raw"] = raw,
                ["version"] = "1.2.3.pre.beta.4",
                ["literal"] = "\"1.2.3.pre.beta.4\""
            };
        if (replacement is null)
            answer.Remove(field);
        else
            answer[field] = field is "gemfile" or "gemspec"
                ? NativePath(replacement) : replacement;

        Assert.ThrowsExactly<InvalidDataException>(() =>
        {
            if (operation == "context")
                RubyNativeFactsReader.ParseContext(answer.ToJsonString(), gemfile, "2.7.2");
            else
                RubyNativeFactsReader.ParseVersion(answer.ToJsonString(), gemspec, raw);
        });
    }

    private static string NativePath(string path) =>
        Path.GetFullPath(
            Path.Combine("native-ruby-fixtures", path.Replace('/', Path.DirectorySeparatorChar))
        );

    private static JsonObject Gem() =>
        JsonNode
            .Parse(
                """
                {"name":"native-gem","version":"1.2.3","platform":"ruby",
                 "require_paths":["lib"],"files":["lib/native.rb"],
                 "runtime_dependencies":[{"name":"dependency","requirement":">= 1.0, < 2.0"}]}
                """
            )!
            .AsObject();

    private static JsonObject Bundle(string kind = "rubygems")
    {
        // Locked bundle members carry Bundler lock facts, not installed file metadata.
        JsonObject specification = Gem();
        specification.Remove("require_paths");
        specification.Remove("files");
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
            ["gemfiles"] = new JsonArray(
                NativePath("bundle/gems.rb"),
                NativePath("bundle/nested.rb")
            ),
            ["direct_dependencies"] = new JsonArray("native-gem"),
            ["specifications"] = new JsonArray(specification),
        };
    }

    private static RubyBundleFacts Parse(JsonObject bundle) =>
        RubyNativeFactsReader.ParseBundle(
            bundle.ToJsonString(),
            NativePath("bundle/gems.rb"),
            "2.7.2"
        );

    [TestMethod]
    public void SourceFactsPreserveNativeMetadataWithoutInventingTestInputs()
    {
        JsonObject value = Gem();
        value["gemspec"] = NativePath("source/native.gemspec");
        RubySourceGemFacts result = RubyNativeFactsReader.ParseSource(
            value.ToJsonString(),
            NativePath("source/native.gemspec")
        );
        Assert.AreEqual(NativePath("source/native.gemspec"), result.Gemspec);
        Assert.AreEqual("native-gem", result.Gem.Name);
        Assert.AreEqual("1.2.3", result.Gem.Version);
        Assert.AreEqual("ruby", result.Gem.Platform);
        Assert.AreEqual("lib", Assert.ContainsSingle(result.Gem.RequirePaths));
        Assert.AreEqual("lib/native.rb", Assert.ContainsSingle(result.Gem.Files));
        Assert.AreEqual(
            new RubyDependency("dependency", ">= 1.0, < 2.0"),
            Assert.ContainsSingle(result.Gem.RuntimeDependencies)
        );
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
        Assert.AreEqual(
            NativePath("installed/cache/native-gem-1.2.3"),
            specification.Source.Directory
        );
        Assert.AreNotEqual(specification.Source.Path, specification.Source.Directory);
        Assert.AreEqual(NativePath("bundle/gems.locked"), result.Lockfile);
        Assert.AreEqual("2.7.2", result.BundlerVersion);
        CollectionAssert.AreEqual(
            new[] { NativePath("bundle/gems.rb"), NativePath("bundle/nested.rb") },
            result.Gemfiles
        );
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
        Assert.AreEqual(
            kind == "git" ? "git@example.invalid:repository/native.git" : null,
            source.Uri
        );
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
        RubySourceGemFacts source = RubyNativeFactsReader.ParseSource(
            gem.ToJsonString(),
            NativePath("source/native.gemspec")
        );
        Assert.IsEmpty(source.Gem.Files);
        Assert.IsEmpty(source.Gem.RuntimeDependencies);
    }

    [TestMethod]
    public void ArchiveFactsRetainNativeContentAnswerIndependentlyOfSourceFiles()
    {
        JsonObject value = Gem();
        value["archive"] = NativePath("outputs/native.gem");
        value["contents"] = new JsonArray("lib/", "lib/native.rb");
        RubyArchiveFacts result = RubyNativeFactsReader.ParseArchive(
            value.ToJsonString(),
            NativePath("outputs/native.gem")
        );
        Assert.AreEqual(NativePath("outputs/native.gem"), result.Archive);
        Assert.AreEqual("native-gem", result.Gem.Name);
        Assert.HasCount(2, result.Contents);
        Assert.AreEqual("lib/", result.Contents[0]);
        Assert.AreEqual("lib/native.rb", result.Contents[1]);
        Assert.AreEqual("lib/native.rb", Assert.ContainsSingle(result.Gem.Files));
    }

    [TestMethod]
    public void ArchiveFactsPreserveOptionalPublicExtensionsAndRepositoryMetadata()
    {
        JsonObject value = Gem();
        value["archive"] = NativePath("outputs/native.gem");
        value["contents"] = new JsonArray("lib/native.rb");
        value["extensions"] = new JsonArray("ext/extconf.rb");
        value["metadata"] = new JsonObject
        {
            ["github_repo"] = "https://github.com/hcoona/three",
            ["other"] = "native metadata",
        };
        RubyArchiveFacts result = RubyNativeFactsReader.ParseArchive(
            value.ToJsonString(),
            NativePath("outputs/native.gem")
        );
        Assert.IsNotNull(result.Extensions);
        Assert.AreEqual("ext/extconf.rb", Assert.ContainsSingle(result.Extensions));
        Assert.AreEqual("https://github.com/hcoona/three", result.Repository);
        value.Remove("extensions");
        value.Remove("metadata");
        RubyArchiveFacts historical = RubyNativeFactsReader.ParseArchive(
            value.ToJsonString(),
            NativePath("outputs/native.gem")
        );
        Assert.IsNull(historical.Extensions);
        Assert.IsNull(historical.Repository);
        Assert.AreEqual(result.Gem.Name, historical.Gem.Name);
        CollectionAssert.AreEqual(result.Contents, historical.Contents);
    }

    [TestMethod]
    [DataRow("extensions")]
    [DataRow("metadata")]
    [DataRow("github_repo")]
    public void MalformedOptionalArchiveFieldsCannotInventReleaseFacts(string field)
    {
        JsonObject value = Gem();
        value["archive"] = NativePath("outputs/native.gem");
        value["contents"] = new JsonArray("lib/native.rb");
        if (field == "extensions")
            value[field] = "not an array";
        else if (field == "metadata")
            value[field] = "not an object";
        else
            value["metadata"] = new JsonObject { [field] = 42 };
        Assert.ThrowsExactly<InvalidDataException>(() =>
            RubyNativeFactsReader.ParseArchive(
                value.ToJsonString(),
                NativePath("outputs/native.gem")
            )
        );
    }

    [TestMethod]
    [DataRow("gemfile")]
    [DataRow("bundler_version")]
    [DataRow("locked_bundler_version")]
    [DataRow("gemfiles")]
    public void SubstitutedBundleContextCannotSupplyFacts(string field)
    {
        JsonObject value = Bundle();
        value[field] =
            field == "gemfiles"
                ? new JsonArray(NativePath("foreign/gems.rb"))
                : JsonValue.Create(field == "gemfile" ? NativePath("foreign/gems.rb") : "2.4.20");
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
            Assert.ThrowsExactly<InvalidDataException>(() =>
                RubyNativeFactsReader.ParseSource(
                    value.ToJsonString(),
                    NativePath("source/native.gemspec")
                )
            );
        }
        else
        {
            value["archive"] = NativePath("foreign/native.gem");
            value["contents"] = new JsonArray("lib/native.rb");
            Assert.ThrowsExactly<InvalidDataException>(() =>
                RubyNativeFactsReader.ParseArchive(
                    value.ToJsonString(),
                    NativePath("outputs/native.gem")
                )
            );
        }
    }

    [TestMethod]
    public void TruncatedNativeBundleCannotSupplyFacts() => RejectBundle("{");

    [TestMethod]
    [DataRow("specifications", null)]
    [DataRow("direct_dependencies", "3")]
    public void IncompleteNativeBundleCannotSupplyFacts(string field, string? json)
    {
        JsonObject value = Bundle("path");
        if (json is null) value.Remove(field);
        else value[field] = JsonNode.Parse(json);
        RejectBundle(value.ToJsonString());
    }

    [TestMethod]
    public void SpecificationWithoutRuntimeDependenciesCannotSupplyFacts()
    {
        JsonObject value = Bundle("path");
        value["specifications"]![0]!.AsObject().Remove("runtime_dependencies");
        RejectBundle(value.ToJsonString());
    }

    [TestMethod]
    [DataRow("kind", "unknown")]
    [DataRow("path", "relative")]
    public void UnsupportedNativeBundleSourceCannotSupplyFacts(string field, string value)
    {
        JsonObject bundle = Bundle("path");
        bundle["specifications"]![0]!["source"]![field] = value;
        RejectBundle(bundle.ToJsonString());
    }

    private static void RejectBundle(string json) =>
        Assert.ThrowsExactly<InvalidDataException>(() =>
            RubyNativeFactsReader.ParseBundle(json, NativePath("bundle/gems.rb"), "2.7.2"));

    private static RubyFactTools Tools(string root) => new(Path.Combine(root, "ruby"),
        Path.Combine(root, "facts.rb"), root,
        new Dictionary<string, string?>
        {
            ["GEM_HOME"] = Path.Combine(root, "gems"),
            ["BUNDLE_FROZEN"] = "0",
            ["BUNDLE_GEMFILE"] = "old context"
        }, 30);

    private static JsonObject CollectorGem() => JsonNode.Parse("""
        {"name":"native-gem","version":"1.2.3","platform":"ruby",
         "require_paths":["lib"],"files":["lib/native.rb"],"runtime_dependencies":[]}
        """)!.AsObject();

    private static NativeCommandResult Answer(JsonObject value) => new(
        NativeTermination.Exited, 0, value.ToJsonString(), "", 0, null);

    [TestMethod]
    [Timeout(60000, CooperativeCancellation = true)]
    public async Task NativeRequestsWorkWithReflectionDisabled()
    {
        const string marker = "WORKFLOW_RUBY_JSON_TEST_CHILD";
        if (Environment.GetEnvironmentVariable(marker) == "1")
        {
            Assert.IsFalse(JsonSerializer.IsReflectionEnabledByDefault);
            await SourceAndArchiveUseNativeAnswersBoundToRequestedPath(false);
            await SourceAndArchiveUseNativeAnswersBoundToRequestedPath(true);
            return;
        }
        using var scratch = new CollectorScratch();
        string root = scratch.Root;
        string assembly = typeof(RubyNativeFactsReaderTests).Assembly.Location;
        JsonNode configuration = JsonNode.Parse(await File.ReadAllTextAsync(
            Path.ChangeExtension(assembly, ".runtimeconfig.json")))!;
        configuration["runtimeOptions"]!["configProperties"]![
            "System.Text.Json.JsonSerializer.IsReflectionEnabledByDefault"] = false;
        string runtime = Path.Combine(root, "reflection-disabled.runtimeconfig.json");
        await File.WriteAllTextAsync(runtime, configuration.ToJsonString());
        NativeCommandResult result = await NativeProcess.ExecuteAsync(new("dotnet", root,
            ["exec", "--runtimeconfig", runtime, assembly, "--filter",
                "FullyQualifiedName=WorkflowDelivery.Tests.Repository." +
                nameof(RubyNativeFactsReaderTests) + "." +
                nameof(NativeRequestsWorkWithReflectionDisabled),
                "--minimum-expected-tests", "1"], 45,
            new Dictionary<string, string?> { [marker] = "1" }), CancellationToken.None);
        Assert.IsTrue(result.Succeeded, result.Stdout + result.Stderr + result.Error);
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task SourceAndArchiveUseNativeAnswersBoundToRequestedPath(bool archive)
    {
        using var scratch = new CollectorScratch();
        string root = scratch.Root;
        RubyFactTools tools = Tools(root);
        string path = Path.Combine(root, archive ? "native.gem" : "native.gemspec");
        var collector = new RubyNativeFactCollector(tools, async (command, token) =>
        {
            Assert.AreEqual(tools.Ruby, command.Executable);
            Assert.AreEqual(root, command.Directory);
            Assert.AreEqual(30, command.DeadlineSeconds);
            Assert.IsFalse(command.InheritEnvironment);
            Assert.AreEqual(tools.Helper, command.Arguments[0]);
            using JsonDocument request = JsonDocument.Parse(await File.ReadAllTextAsync(
                command.Arguments[1], token));
            Assert.AreEqual(archive ? "archive" : "gemspec",
                request.RootElement.GetProperty("operation").GetString());
            Assert.AreEqual(path, request.RootElement.GetProperty("path").GetString());
            JsonObject value = CollectorGem();
            value[archive ? "archive" : "gemspec"] = path;
            if (archive) value["contents"] = new JsonArray("lib/native.rb");
            return Answer(value);
        });
        if (archive)
        {
            RubyArchiveFacts result = await collector.ArchiveAsync(path, CancellationToken.None);
            Assert.AreEqual(path, result.Archive);
            Assert.AreEqual("native-gem", result.Gem.Name);
            Assert.AreEqual("lib/native.rb", Assert.ContainsSingle(result.Contents));
        }
        else
        {
            RubySourceGemFacts result = await collector.SourceAsync(path, CancellationToken.None);
            Assert.AreEqual(path, result.Gemspec);
            Assert.AreEqual("1.2.3", result.Gem.Version);
            Assert.AreEqual("lib/native.rb", Assert.ContainsSingle(result.Gem.Files));
        }
    }

    [TestMethod]
    [DataRow("2.4.20")]
    [DataRow("2.7.2")]
    public async Task BundleUsesSelectedNativeActivationAndFrozenOriginalContext(string version)
    {
        using var scratch = new CollectorScratch();
        string root = scratch.Root;
        RubyFactTools tools = Tools(root);
        string path = Path.Combine(root, "gems.rb");
        var collector = new RubyNativeFactCollector(tools, async (command, token) =>
        {
            Assert.AreEqual(version, command.Arguments[2]);
            Assert.Contains("gem \"bundler\", ARGV.shift", command.Arguments[1]);
            Assert.AreEqual(tools.Helper, command.Arguments[3]);
            Assert.IsFalse(command.InheritEnvironment);
            Assert.AreEqual(path, command.Environment!["BUNDLE_GEMFILE"]);
            Assert.AreEqual("1", command.Environment["BUNDLE_FROZEN"]);
            Assert.AreEqual(tools.Environment["GEM_HOME"], command.Environment["GEM_HOME"]);
            using JsonDocument request = JsonDocument.Parse(await File.ReadAllTextAsync(
                command.Arguments[4], token));
            Assert.AreEqual("bundle", request.RootElement.GetProperty("operation").GetString());
            Assert.AreEqual(path, request.RootElement.GetProperty("path").GetString());
            return Answer(new JsonObject
            {
                ["gemfile"] = path,
                ["lockfile"] = Path.Combine(root, "gems.locked"),
                ["bundler_version"] = version,
                ["locked_bundler_version"] = version,
                ["gemfiles"] = new JsonArray(path),
                ["direct_dependencies"] = new JsonArray(),
                ["specifications"] = new JsonArray()
            });
        });
        RubyBundleFacts result = await collector.BundleAsync(path, version, CancellationToken.None);
        Assert.AreEqual(version, result.BundlerVersion);
        Assert.AreEqual(path, result.Gemfile);
        Assert.IsEmpty(result.Specifications);
        Assert.AreEqual("0", tools.Environment["BUNDLE_FROZEN"]);
        Assert.AreEqual("old context", tools.Environment["BUNDLE_GEMFILE"]);
    }

    [TestMethod]
    [DataRow("Exited")]
    [DataRow("StartFailed")]
    [DataRow("Cancelled")]
    [DataRow("TimedOut")]
    [DataRow("InvalidOutput")]
    [DataRow("CleanupFailed")]
    public async Task FailedNativeCommandsCannotSupplyFacts(string termination)
    {
        using var scratch = new CollectorScratch();
        string root = scratch.Root;
        string path = Path.Combine(root, "native.gemspec");
        JsonObject value = CollectorGem();
        value["gemspec"] = path;
        var collector = new RubyNativeFactCollector(Tools(root), (_, _) => Task.FromResult(
            new NativeCommandResult(Enum.Parse<NativeTermination>(termination),
                termination == "Exited" ? 1 : 0,
                value.ToJsonString(), "failed", 0, null)));
        InvalidDataException error = await Assert.ThrowsExactlyAsync<InvalidDataException>(() =>
            collector.SourceAsync(path, CancellationToken.None));
        Assert.Contains(termination, error.Message);
        Assert.Contains(termination == "Exited" ? "exit 1" : "exit 0", error.Message);
        Assert.Contains("failed", error.Message);
        Assert.IsEmpty(Directory.GetFiles(root));
    }

    [TestMethod]
    public async Task NativeErrorCannotBeHiddenBySuccessfulExitAndWellFormedFacts()
    {
        using var scratch = new CollectorScratch();
        string root = scratch.Root;
        string path = Path.Combine(root, "native.gemspec");
        JsonObject value = CollectorGem();
        value["gemspec"] = path;
        var collector = new RubyNativeFactCollector(Tools(root), (_, _) => Task.FromResult(
            Answer(value) with { Error = "incomplete capture" }));
        InvalidDataException error = await Assert.ThrowsExactlyAsync<InvalidDataException>(() =>
            collector.SourceAsync(path, CancellationToken.None));
        Assert.Contains("incomplete capture", error.Message);
        Assert.Contains("exit 0", error.Message);
        Assert.IsEmpty(Directory.GetFiles(root));
    }

    [TestMethod]
    public async Task FailedNativeStartupKeepsItsAvailableDiagnostics()
    {
        using var scratch = new CollectorScratch();
        string root = scratch.Root;
        string path = Path.Combine(root, "native.gemspec");
        var collector = new RubyNativeFactCollector(Tools(root), (_, _) => Task.FromResult(
            new NativeCommandResult(NativeTermination.StartFailed, null, "", "native detail",
                0, "Ruby executable missing")));
        InvalidDataException error = await Assert.ThrowsExactlyAsync<InvalidDataException>(() =>
            collector.SourceAsync(path, CancellationToken.None));
        Assert.Contains("StartFailed", error.Message);
        Assert.Contains("exit unknown", error.Message);
        Assert.Contains("Ruby executable missing", error.Message);
        Assert.Contains("native detail", error.Message);
        Assert.IsEmpty(Directory.GetFiles(root));
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task CancellationCannotBecomeFactCollectionSuccess(bool duringProcess)
    {
        using var scratch = new CollectorScratch();
        string root = scratch.Root;
        using var cancellation = new CancellationTokenSource();
        string path = Path.Combine(root, "native.gemspec");
        var collector = new RubyNativeFactCollector(Tools(root), (_, _) =>
        {
            Assert.IsTrue(duringProcess, "Pre-cancelled collection must not launch Ruby.");
            cancellation.Cancel();
            JsonObject value = CollectorGem();
            value["gemspec"] = path;
            return Task.FromResult(Answer(value));
        });
        if (!duringProcess) cancellation.Cancel();
        await Assert.ThrowsAsync<OperationCanceledException>(() =>
            collector.SourceAsync(path, cancellation.Token));
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task SuccessfulExitStillRequiresCompleteCorrectlyBoundFacts(bool substituted)
    {
        using var scratch = new CollectorScratch();
        string root = scratch.Root;
        string path = Path.Combine(root, "native.gemspec");
        JsonObject value = CollectorGem();
        value["gemspec"] = Path.Combine(root, "other.gemspec");
        var collector = new RubyNativeFactCollector(Tools(root), (_, _) => Task.FromResult(
            substituted ? Answer(value) : new NativeCommandResult(NativeTermination.Exited, 0,
                "{", "", 0, null)));
        await Assert.ThrowsExactlyAsync<InvalidDataException>(() =>
            collector.SourceAsync(path, CancellationToken.None));
    }

    private sealed class CollectorScratch : IDisposable
    {
        internal string Root { get; } = Directory.CreateTempSubdirectory("ruby-facts-").FullName;

        public void Dispose() => Directory.Delete(Root, recursive: true);
    }
}
