using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using WorkflowDelivery.Platform;
using WorkflowDelivery.Repository;

namespace WorkflowDelivery.Tests.Repository;

[TestClass]
public sealed class RubyNativeFactCollectorTests
{
    private string root = null!;

    [TestInitialize]
    public void PrepareScratch() => root = Directory.CreateDirectory(Path.Combine(
        Path.GetTempPath(), "ruby-fact-collector-" + Guid.NewGuid())).FullName;

    [TestCleanup]
    public void RemoveScratch() => Directory.Delete(root, recursive: true);

    private RubyFactTools Tools() => new(Path.Combine(root, "ruby"),
        Path.Combine(root, "facts.rb"), root,
        new Dictionary<string, string?> { ["GEM_HOME"] = Path.Combine(root, "gems"),
            ["BUNDLE_FROZEN"] = "0", ["BUNDLE_GEMFILE"] = "old context" }, 30);

    private static JsonObject Gem() => JsonNode.Parse("""
        {"name":"native-gem","version":"1.2.3","platform":"ruby",
         "require_paths":["lib"],"files":["lib/native.rb"],"runtime_dependencies":[]}
        """)!.AsObject();

    private static NativeCommandResult Answer(JsonObject value) => new(
        NativeTermination.Exited, 0, value.ToJsonString(), "", 0, null);

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task SourceAndArchiveUseNativeAnswersBoundToRequestedPath(bool archive)
    {
        RubyFactTools tools = Tools();
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
            JsonObject value = Gem();
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
        RubyFactTools tools = Tools();
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
            return Answer(new JsonObject { ["gemfile"] = path,
                ["lockfile"] = Path.Combine(root, "gems.locked"),
                ["bundler_version"] = version, ["locked_bundler_version"] = version,
                ["gemfiles"] = new JsonArray(path), ["direct_dependencies"] = new JsonArray(),
                ["specifications"] = new JsonArray() });
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
        string path = Path.Combine(root, "native.gemspec");
        JsonObject value = Gem();
        value["gemspec"] = path;
        var collector = new RubyNativeFactCollector(Tools(), (_, _) => Task.FromResult(
            new NativeCommandResult(Enum.Parse<NativeTermination>(termination),
                termination == "Exited" ? 1 : 0,
                value.ToJsonString(), "failed", 0, null)));
        await Assert.ThrowsExactlyAsync<InvalidDataException>(() =>
            collector.SourceAsync(path, CancellationToken.None));
    }

    [TestMethod]
    public async Task NativeErrorCannotBeHiddenBySuccessfulExitAndWellFormedFacts()
    {
        string path = Path.Combine(root, "native.gemspec");
        JsonObject value = Gem();
        value["gemspec"] = path;
        var collector = new RubyNativeFactCollector(Tools(), (_, _) => Task.FromResult(
            Answer(value) with { Error = "incomplete capture" }));
        await Assert.ThrowsExactlyAsync<InvalidDataException>(() =>
            collector.SourceAsync(path, CancellationToken.None));
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task CancellationCannotBecomeFactCollectionSuccess(bool duringProcess)
    {
        using var cancellation = new CancellationTokenSource();
        string path = Path.Combine(root, "native.gemspec");
        var collector = new RubyNativeFactCollector(Tools(), (_, _) =>
        {
            Assert.IsTrue(duringProcess, "Pre-cancelled collection must not launch Ruby.");
            cancellation.Cancel();
            JsonObject value = Gem();
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
        string path = Path.Combine(root, "native.gemspec");
        JsonObject value = Gem();
        value["gemspec"] = Path.Combine(root, "other.gemspec");
        var collector = new RubyNativeFactCollector(Tools(), (_, _) => Task.FromResult(
            substituted ? Answer(value) : new NativeCommandResult(NativeTermination.Exited, 0,
                "{", "", 0, null)));
        await Assert.ThrowsExactlyAsync<InvalidDataException>(() =>
            collector.SourceAsync(path, CancellationToken.None));
    }
}
