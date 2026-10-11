using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using WorkflowDelivery.Platform;
using WorkflowDelivery.Repository;

namespace WorkflowDelivery.Tests.Repository;

[TestClass]
public sealed class RubyPreparationFactsTests
{
    private static readonly string Root = Path.GetFullPath("ruby preparation facts");
    private static string NativePath(string path) => Path.GetFullPath(Path.Combine(Root, path));
    private static JsonObject Context() => new() {
        ["gemfile"] = NativePath("bundle/Gemfile"),
        ["lockfile"] = NativePath("bundle/Gemfile.lock"),
        ["bundler_version"] = "2.7.2", ["locked_bundler_version"] = "2.4.20" };
    private static JsonObject Version() => new() {
        ["gemspec"] = NativePath("source/native.gemspec"),
        ["raw"] = "1.2.3-beta.4+gabcdef0", ["version"] = "1.2.3.pre.beta.4",
        ["literal"] = "\"1.2.3.pre.beta.4\"" };

    [TestMethod]
    public void ContextKeepsTheInstalledProbeSeparateFromTheLockedSelection()
    {
        RubyBundleContext result = RubyNativeFactsReader.ParseContext(Context().ToJsonString(),
            NativePath("bundle/Gemfile"), "2.7.2");
        Assert.AreEqual("2.7.2", result.BundlerVersion);
        Assert.AreEqual("2.4.20", result.LockedBundlerVersion);
        Assert.AreEqual(NativePath("bundle/Gemfile.lock"), result.Lockfile);
        Assert.AreEqual(NativePath("bundle/Gemfile"), result.Gemfile);
    }

    [TestMethod]
    public void ProjectionPreservesNativeInputVersionAndRubyLiteral()
    {
        RubyVersionProjection result = RubyNativeFactsReader.ParseVersion(
            Version().ToJsonString(), NativePath("source/native.gemspec"),
            "1.2.3-beta.4+gabcdef0");
        Assert.AreEqual("1.2.3-beta.4+gabcdef0", result.Raw);
        Assert.AreEqual("1.2.3.pre.beta.4", result.Version);
        Assert.AreEqual("\"1.2.3.pre.beta.4\"", result.Literal);
        Assert.AreEqual(NativePath("source/native.gemspec"), result.Gemspec);
    }

    [TestMethod]
    [DataRow("context", "gemfile", "other/Gemfile")]
    [DataRow("context", "bundler_version", "2.4.20")]
    [DataRow("context", "locked_bundler_version", null)]
    [DataRow("context", "lockfile", "relative.lock")]
    [DataRow("version", "gemspec", "other/native.gemspec")]
    [DataRow("version", "raw", "other-version")]
    [DataRow("version", "version", null)]
    [DataRow("version", "literal", null)]
    public void IncompleteOrSubstitutedNativePreparationAnswersFail(string kind,
        string field, string? replacement)
    {
        JsonObject value = kind == "context" ? Context() : Version();
        if (replacement is null) value.Remove(field);
        else value[field] = replacement;
        Assert.ThrowsExactly<InvalidDataException>(() =>
        {
            if (kind == "context")
                RubyNativeFactsReader.ParseContext(value.ToJsonString(),
                    NativePath("bundle/Gemfile"), "2.7.2");
            else
                RubyNativeFactsReader.ParseVersion(value.ToJsonString(),
                    NativePath("source/native.gemspec"), "1.2.3-beta.4+gabcdef0");
        });
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task CollectorTransfersOriginalNativeContextOrVersionInput(bool version)
    {
        string scratch = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(),
            "ruby-preparation-query-" + Guid.NewGuid())).FullName;
        try
        {
            var tools = new RubyFactTools(NativePath("ruby"), NativePath("facts.rb"), scratch,
                new Dictionary<string, string?> { ["BUNDLE_GEMFILE"] = "old context" }, 30);
            var collector = new RubyNativeFactCollector(tools, async (command, token) =>
            {
                Assert.IsFalse(command.InheritEnvironment);
                Assert.AreEqual(tools.Ruby, command.Executable);
                string requestPath = command.Arguments[^1];
                using JsonDocument request = JsonDocument.Parse(
                    await File.ReadAllTextAsync(requestPath, token));
                Assert.AreEqual(version ? "version" : "bundle-context",
                    request.RootElement.GetProperty("operation").GetString());
                if (version)
                    Assert.AreEqual("1.2.3-beta.4+gabcdef0",
                        request.RootElement.GetProperty("raw").GetString());
                else
                {
                    Assert.AreEqual("2.7.2", command.Arguments[2]);
                    Assert.AreEqual(NativePath("bundle/Gemfile"),
                        command.Environment!["BUNDLE_GEMFILE"]);
                    Assert.AreEqual("1", command.Environment["BUNDLE_FROZEN"]);
                }
                return new NativeCommandResult(NativeTermination.Exited, 0,
                    (version ? Version() : Context()).ToJsonString(), "", 0, null);
            });
            if (version)
                Assert.AreEqual("1.2.3.pre.beta.4", (await collector.VersionAsync(
                    NativePath("source/native.gemspec"), "1.2.3-beta.4+gabcdef0",
                    CancellationToken.None)).Version);
            else
                Assert.AreEqual("2.4.20", (await collector.ContextAsync(
                    NativePath("bundle/Gemfile"), "2.7.2",
                    CancellationToken.None)).LockedBundlerVersion);
            Assert.AreEqual("old context", tools.Environment["BUNDLE_GEMFILE"]);
            Assert.IsEmpty(Directory.GetFiles(scratch));
        }
        finally { Directory.Delete(scratch, recursive: true); }
    }
}
