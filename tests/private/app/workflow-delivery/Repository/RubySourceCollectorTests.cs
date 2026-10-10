using System.Text.Json;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using WorkflowDelivery.Platform;
using WorkflowDelivery.Repository;
using GitFixture = WorkflowDelivery.Tests.Repository.GitReaderTests.GitFixture;

namespace WorkflowDelivery.Tests.Repository;

[TestClass]
public sealed class RubySourceCollectorTests(TestContext context)
{
    private static readonly string[] VersionArguments = ["get-version", "--format", "json"];
    private static readonly string[] RequirePaths = ["lib"];
    private static readonly string[] NativeFiles = ["lib/native.rb"];
    private static RubyFactTools Tools(string root) => new(Path.Combine(root, "ruby"),
        Path.Combine(root, "facts.rb"), root,
        new Dictionary<string, string?> { ["PATH"] = "prepared native tools" }, 30);
    private static NativeCommandResult Answer(object value) => new(NativeTermination.Exited,
        0, JsonSerializer.Serialize(value), "", 0, null);
    private async Task<GitMaterialization> BindAsync(GitFixture repo)
    {
        string head = await repo.CommitAsync();
        await repo.GitAsync("reset", "--hard", head);
        return await GitMaterialization.BindAsync(repo.Directory,
            await new GitReader(repo.Directory).ReadAsync(head, context.CancellationToken),
            context.CancellationToken);
    }

    [TestMethod]
    [DataRow("asciidoctor-latexmath")]
    [DataRow("hcoona-release-smoke-ruby")]
    public async Task NativeVersionsBindTheOriginalProjectAndOnlyRequiredQueryInputs(string name)
    {
        using var repo = await GitFixture.CreateAsync(context.CancellationToken);
        string project = "src/" + name;
        await repo.SetAsync(project + "/" + name + ".gemspec", "original gemspec");
        await repo.SetAsync(project + "/lib/hcoona_release_smoke_ruby.rb", "original library");
        await repo.SetAsync(".gitignore", "**/version.rb\n");
        GitMaterialization checkout = await BindAsync(repo);
        RubyFactTools tools = Tools(repo.Directory);
        string nbgv = Path.Combine(repo.Directory, "nbgv");
        var commands = new List<NativeCommand>();
        RubySourceGemFacts[] result = await RubySourceCollector.ReadAsync(checkout, tools,
            nbgv, async (command, token) =>
            {
                commands.Add(command);
                Assert.IsFalse(command.InheritEnvironment);
                Assert.AreEqual(nbgv,
                    command.Environment!["THREE_WORKFLOW_RELEASE_NBGV_PATH"]);
                if (command.Executable == nbgv)
                {
                    Assert.AreEqual(Path.Combine(repo.Directory, project), command.Directory);
                    CollectionAssert.AreEqual(VersionArguments, command.Arguments);
                    return Answer(new { SemVer2 = "1.2.3-beta.4+gabcdef0" });
                }
                using JsonDocument request = JsonDocument.Parse(
                    await File.ReadAllTextAsync(command.Arguments[^1], token));
                string gemspec = Path.Combine(repo.Directory, project, name + ".gemspec");
                Assert.AreEqual(gemspec, request.RootElement.GetProperty("path").GetString());
                if (request.RootElement.GetProperty("operation").GetString() == "version")
                    return Answer(new { gemspec, raw = "1.2.3-beta.4+gabcdef0",
                        version = "1.2.3.pre.beta.4", literal = "\"1.2.3.pre.beta.4\"" });
                if (name == "hcoona-release-smoke-ruby")
                    Assert.Contains("VERSION = \"1.2.3.pre.beta.4\"",
                        await File.ReadAllTextAsync(Path.Combine(repo.Directory, project,
                            "lib/hcoona_release_smoke_ruby/version.rb"), token));
                return Answer(new { gemspec, name, version = "1.2.3.pre.beta.4", platform = "ruby",
                    require_paths = RequirePaths, files = NativeFiles,
                    runtime_dependencies = Array.Empty<object>() });
            }, context.CancellationToken);
        RubySourceGemFacts source = Assert.ContainsSingle(result);
        Assert.AreEqual(name, source.Gem.Name);
        Assert.AreEqual("1.2.3.pre.beta.4", source.Gem.Version);
        Assert.IsFalse(tools.Environment.ContainsKey("THREE_WORKFLOW_RELEASE_NBGV_PATH"));
        Assert.AreEqual("original gemspec", await File.ReadAllTextAsync(source.Gemspec,
            context.CancellationToken));
        string generated = Path.Combine(repo.Directory, project,
            "lib/hcoona_release_smoke_ruby/version.rb");
        if (name == "hcoona-release-smoke-ruby")
            Assert.AreEqual("# frozen_string_literal: true\n\nmodule HcoonaReleaseSmokeRuby\n" +
                "  VERSION = \"1.2.3.pre.beta.4\"\nend\n", await File.ReadAllTextAsync(generated,
                    context.CancellationToken));
        else
            Assert.IsFalse(File.Exists(generated));
        Assert.IsFalse(File.Exists(Path.Combine(Path.GetDirectoryName(generated)!,
            "_workflow_delivery_provenance.json")));
        Assert.IsTrue(commands.All(command => !command.Arguments.Contains("install",
            StringComparer.Ordinal) && !command.Arguments.Contains("build",
            StringComparer.Ordinal) && !command.Arguments.Contains("rspec",
            StringComparer.Ordinal)));
    }

    [TestMethod]
    [DataRow("missing-version")]
    [DataRow("failed-version")]
    [DataRow("wrong-name")]
    [DataRow("wrong-version")]
    [DataRow("committed-generated-version")]
    public async Task IncompleteOrConflictingSourceVersionFactsCannotPass(string defect)
    {
        using var repo = await GitFixture.CreateAsync(context.CancellationToken);
        const string project = "src/hcoona-release-smoke-ruby";
        await repo.SetAsync(project + "/hcoona-release-smoke-ruby.gemspec");
        await repo.SetAsync(project + "/lib/hcoona_release_smoke_ruby.rb");
        if (defect == "committed-generated-version")
            await repo.SetAsync(project +
                "/lib/hcoona_release_smoke_ruby/version.rb");
        GitMaterialization checkout = await BindAsync(repo);
        string nbgv = Path.Combine(repo.Directory, "nbgv");
        await Assert.ThrowsExactlyAsync<InvalidDataException>(() =>
            RubySourceCollector.ReadAsync(checkout, Tools(repo.Directory), nbgv,
                async (command, token) =>
                {
                    if (command.Executable == nbgv)
                        return defect == "missing-version" ?
                            Answer(new { OtherVersion = "1.2.3" }) :
                            defect == "failed-version" ?
                                Answer(new { SemVer2 = "1.2.3" }) with
                                { ExitCode = 1 } : Answer(new { SemVer2 = "1.2.3" });
                    using JsonDocument request = JsonDocument.Parse(
                        await File.ReadAllTextAsync(command.Arguments[^1], token));
                    string gemspec = Path.Combine(repo.Directory, project,
                        "hcoona-release-smoke-ruby.gemspec");
                    if (request.RootElement.GetProperty("operation").GetString() == "version")
                        return Answer(new { gemspec, raw = "1.2.3", version = "1.2.3",
                            literal = "\"1.2.3\"" });
                    return Answer(new { gemspec, name = defect == "wrong-name" ? "other" :
                        "hcoona-release-smoke-ruby", version = defect == "wrong-version" ?
                        "9.9.9" : "1.2.3", platform = "ruby", require_paths = RequirePaths,
                        files = Array.Empty<string>(),
                        runtime_dependencies = Array.Empty<object>() });
                }, context.CancellationToken));
    }

    [TestMethod]
    public async Task AbsentSourcesPerformNoNativeOrProductPreparation()
    {
        using var repo = await GitFixture.CreateAsync(context.CancellationToken);
        await repo.SetAsync("docs/guide.md");
        GitMaterialization checkout = await BindAsync(repo);
        RubySourceGemFacts[] sources = await RubySourceCollector.ReadAsync(checkout,
            Tools(repo.Directory), Path.Combine(repo.Directory, "nbgv"), (_, _) =>
            {
                Assert.Fail("An absent source set cannot invoke native or product preparation.");
                return Task.FromResult(Answer(new { }));
            }, context.CancellationToken);
        Assert.IsEmpty(sources);
    }

    [TestMethod]
    public async Task CancellationCannotBecomeASuccessfulNativeVersionQuery()
    {
        using var repo = await GitFixture.CreateAsync(context.CancellationToken);
        await repo.SetAsync("src/hcoona-release-smoke-ruby/hcoona-release-smoke-ruby.gemspec");
        GitMaterialization checkout = await BindAsync(repo);
        using var cancellation = new CancellationTokenSource();
        await Assert.ThrowsAsync<OperationCanceledException>(() =>
            RubySourceCollector.ReadAsync(checkout, Tools(repo.Directory),
                Path.Combine(repo.Directory, "nbgv"), (_, _) =>
                {
                    cancellation.Cancel();
                    return Task.FromResult(Answer(new { SemVer2 = "1.2.3" }));
                }, cancellation.Token));
    }

    [TestMethod]
    public async Task UnsupportedSourceIntegrationCannotBeSilentlyOmitted()
    {
        using var repo = await GitFixture.CreateAsync(context.CancellationToken);
        await repo.SetAsync("src/unknown/unknown.gemspec");
        GitMaterialization checkout = await BindAsync(repo);
        await Assert.ThrowsExactlyAsync<InvalidDataException>(() =>
            RubySourceCollector.ReadAsync(checkout, Tools(repo.Directory),
                Path.Combine(repo.Directory, "nbgv"), (_, _) =>
                {
                    Assert.Fail("Unsupported source integration must not invent native inputs.");
                    return Task.FromResult(Answer(new { }));
                }, context.CancellationToken));
    }
}
