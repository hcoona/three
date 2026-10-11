using System.Text.Json;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using WorkflowDelivery.Platform;
using WorkflowDelivery.Repository;
using GitFixture = WorkflowDelivery.Tests.Repository.GitReaderTests.GitFixture;

namespace WorkflowDelivery.Tests.Repository;

[TestClass]
public sealed class RubyRepositoryReaderTests(TestContext context)
{
    private static readonly string[] RequirePaths = ["lib"];
    private static NativeCommandResult Answer(object value) => new(NativeTermination.Exited,
        0, JsonSerializer.Serialize(value), "", 0, null);
    private static RubyRepositoryRequest Request(string root) => new(new(
        Path.Combine(root, "ruby"), Path.Combine(root, "facts.rb"), root,
        new Dictionary<string, string?> { ["PATH"] = "prepared" }, 30),
        Path.Combine(root, "nbgv"), "2.7.2");

    private async Task<GitMaterialization> BindAsync(GitFixture repo)
    {
        string head = await repo.CommitAsync();
        await repo.GitAsync("reset", "--hard", head);
        return await GitMaterialization.BindAsync(repo.Directory,
            await new GitReader(repo.Directory).ReadAsync(head, context.CancellationToken),
            context.CancellationToken);
    }

    [TestMethod]
    public async Task NativeCollectorsUseLockedBundleWithoutInventingOwner()
    {
        using var repo = await GitFixture.CreateAsync(context.CancellationToken);
        await repo.SetAsync("node/Gemfile", "native declaration");
        await repo.SetAsync("node/Gemfile.lock", "native lock");
        GitMaterialization checkout = await BindAsync(repo);
        RubyRepositoryRequest request = Request(repo.Directory);
        RubyRevisionInputs result = await RubyRepositoryReader.ReadAsync(checkout, request,
            async (command, token) =>
            {
                using JsonDocument input = JsonDocument.Parse(
                    await File.ReadAllTextAsync(command.Arguments[^1], token));
                string gemfile = Path.Combine(repo.Directory, "node/Gemfile");
                Assert.AreEqual(gemfile, input.RootElement.GetProperty("path").GetString());
                Assert.IsFalse(command.InheritEnvironment);
                if (input.RootElement.GetProperty("operation").GetString() == "bundle-context")
                    return Answer(new { gemfile, lockfile = gemfile + ".lock",
                        bundler_version = "2.7.2", locked_bundler_version = "2.4.20" });
                Assert.AreEqual("bundle", input.RootElement.GetProperty("operation").GetString());
                Assert.AreEqual("2.4.20", command.Arguments[2]);
                Assert.AreEqual("1", command.Environment!["BUNDLE_FROZEN"]);
                return Answer(new { gemfile, lockfile = gemfile + ".lock",
                    bundler_version = "2.4.20", locked_bundler_version = "2.4.20",
                    gemfiles = new[] { gemfile }, direct_dependencies = Array.Empty<string>(),
                    specifications = Array.Empty<object>() });
            }, context.CancellationToken);
        Assert.IsEmpty(result.Sources);
        Assert.IsEmpty(result.Facts.Owners);
        Assert.IsEmpty(result.Facts.Native.Graph.Projects);
        Assert.AreEqual("node/Gemfile", Assert.ContainsSingle(result.Facts.Native.Graph.Bundles)
            .Gemfile);
        Assert.AreEqual("2.4.20", Assert.ContainsSingle(result.Bundles).BundlerVersion);
        Assert.IsEmpty(result.Facts.OperationInputs);
    }

    [TestMethod]
    [DataRow("rspec")]
    [DataRow("input-only")]
    [DataRow("gem")]
    public async Task NativeSourceAdoptionPreservesRequiredChecksAndVersionInputs(string adoption)
    {
        using var repo = await GitFixture.CreateAsync(context.CancellationToken);
        string name = adoption == "gem" ? "hcoona-release-smoke-ruby" : "asciidoctor-latexmath";
        string directory = "src/" + name;
        await repo.SetAsync(directory + "/" + name + ".gemspec", "original source");
        await repo.SetAsync(directory + "/version.json", "{\"version\":\"1.2\"}");
        await repo.SetAsync(directory + "/spec/check_spec.rb", "project test input");
        if (adoption != "input-only")
            await repo.SetAsync(directory + "/workflow-delivery.quality.yml",
                "schema: workflow-delivery/v3/quality-selection\n" +
                "ecosystems:\n  ruby:\n    preset: " +
                (adoption == "gem" ? RubyPackageChecks.Preset : RubyProjectChecks.Preset) +
                "\n");
        if (adoption == "gem")
            await repo.SetAsync(directory + "/workflow-delivery.release-unit.yml",
                "schema: workflow-delivery/v3/release-unit\nrelease-unit: native-smoke\nbuilds:\n" +
                "  - id: gem\n    definition: ruby/gem-v1\n    entry-point: " + name +
                ".gemspec\n    outputs:\n      - id: gem\n        role: primary-package\n" +
                "        kind: ruby-gem\n");
        GitMaterialization checkout = await BindAsync(repo);
        RubyRepositoryRequest request = Request(repo.Directory);
        RubyRevisionInputs result = await RubyRepositoryReader.ReadAsync(checkout, request,
            async (command, token) =>
            {
                if (command.Executable == request.Nbgv)
                {
                    Assert.AreEqual(Path.Combine(repo.Directory, directory), command.Directory);
                    return Answer(new { SemVer2 = "1.2.3+original" });
                }
                using JsonDocument input = JsonDocument.Parse(
                    await File.ReadAllTextAsync(command.Arguments[^1], token));
                string gemspec = Path.Combine(repo.Directory, directory, name + ".gemspec");
                if (input.RootElement.GetProperty("operation").GetString() == "version")
                    return Answer(new { gemspec, raw = "1.2.3+original", version = "1.2.3",
                        literal = "\"1.2.3\"" });
                Assert.AreEqual("gemspec", input.RootElement.GetProperty("operation").GetString());
                return Answer(new { gemspec, name, version = "1.2.3", platform = "ruby",
                    require_paths = RequirePaths, files = Array.Empty<string>(),
                    runtime_dependencies = Array.Empty<object>() });
            }, context.CancellationToken);
        Assert.AreEqual(directory, Assert.ContainsSingle(result.Facts.Native.Graph.Projects)
            .Directory);
        Assert.Contains(directory, result.Facts.Native.Inputs.Single(input =>
            input.Path == directory + "/spec/check_spec.rb").Consumers);
        Assert.Contains(directory, result.Facts.Native.Inputs.Single(input =>
            input.Path == directory + "/version.json").Consumers);
        if (adoption == "input-only")
        {
            Assert.IsEmpty(result.Facts.Owners);
            return;
        }
        var owner = Assert.ContainsSingle(result.Facts.Owners);
        Assert.AreEqual(directory, owner.Directory);
        Assert.AreEqual("ruby:" + directory, owner.Id);
        Assert.IsTrue(owner.Checks.All(check => check.Required));
        if (adoption == "rspec")
        {
            Assert.AreEqual(RubyProjectChecks.RSpec, Assert.ContainsSingle(owner.Checks).Key.Check);
            Assert.IsEmpty(result.Units);
        }
        else
        {
            Assert.HasCount(3, owner.Checks);
            Assert.AreEqual("native-smoke", owner.ReleaseUnit);
            Assert.IsTrue(owner.Checks.All(check => check.Package!.ExpectedVersion == "1.2.3"));
            Assert.AreEqual(owner.Id, Assert.ContainsSingle(
                Assert.ContainsSingle(result.Facts.OperationInputs).Consumers));
        }
    }

    [TestMethod]
    [DataRow("failure")]
    [DataRow("lock-mismatch")]
    [DataRow("incomplete-answer")]
    public async Task NativeCollectionFailureCannotBecomeEndpointFacts(string defect)
    {
        using var repo = await GitFixture.CreateAsync(context.CancellationToken);
        await repo.SetAsync("node/Gemfile");
        await repo.SetAsync("node/Gemfile.lock");
        GitMaterialization checkout = await BindAsync(repo);
        await Assert.ThrowsExactlyAsync<InvalidDataException>(() => RubyRepositoryReader.ReadAsync(
            checkout, Request(repo.Directory), async (command, token) =>
            {
                using JsonDocument input = JsonDocument.Parse(
                    await File.ReadAllTextAsync(command.Arguments[^1], token));
                if (defect == "failure") return Answer(new { }) with { ExitCode = 1 };
                string gemfile = Path.Combine(repo.Directory, "node/Gemfile");
                if (input.RootElement.GetProperty("operation").GetString() == "bundle-context")
                    return Answer(new { gemfile, lockfile = gemfile + ".lock",
                        bundler_version = "2.7.2", locked_bundler_version = "2.4.20" });
                if (defect == "incomplete-answer") return Answer(new { });
                return Answer(new { gemfile, lockfile = gemfile + ".lock",
                    bundler_version = "2.7.2", locked_bundler_version = "2.7.2",
                    gemfiles = new[] { gemfile }, direct_dependencies = Array.Empty<string>(),
                    specifications = Array.Empty<object>() });
            }, context.CancellationToken));
    }

    [TestMethod]
    public async Task CancellationStopsCollectionBeforeLaunchingNativeTools()
    {
        using var repo = await GitFixture.CreateAsync(context.CancellationToken);
        await repo.SetAsync("node/Gemfile");
        GitMaterialization checkout = await BindAsync(repo);
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        await Assert.ThrowsAsync<OperationCanceledException>(() => RubyRepositoryReader.ReadAsync(
            checkout, Request(repo.Directory), (_, _) =>
            {
                Assert.Fail("Pre-cancelled native collection must not launch tools.");
                return Task.FromResult(Answer(new { }));
            }, cancellation.Token));
    }

    [TestMethod]
    public async Task EmptyInventoryDoesNotLaunchNativeTools()
    {
        using var repo = await GitFixture.CreateAsync(context.CancellationToken);
        await repo.SetAsync("docs/guide.md");
        GitMaterialization checkout = await BindAsync(repo);
        RubyRevisionInputs result = await RubyRepositoryReader.ReadAsync(checkout,
            Request(repo.Directory), (_, _) =>
            {
                Assert.Fail("Empty native inventory requires no query or product preparation.");
                return Task.FromResult(Answer(new { }));
            }, context.CancellationToken);
        Assert.IsEmpty(result.Facts.Native.Graph.Projects);
        Assert.IsEmpty(result.Bundles);
        Assert.IsEmpty(result.Facts.Owners);
    }
}
