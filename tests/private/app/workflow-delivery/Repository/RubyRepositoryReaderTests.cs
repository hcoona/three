using System.Text.Json;
using WorkflowDelivery.CI;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using WorkflowDelivery.Platform;
using WorkflowDelivery.Repository;
using GitFixture = WorkflowDelivery.Tests.Repository.GitReaderTests.GitFixture;

namespace WorkflowDelivery.Tests.Repository;

[TestClass]
public sealed class RubyRepositoryReaderTests(TestContext context)
{
    private static readonly string[] RequirePaths = ["lib"];
    private static readonly string[] NestedOwnerDirectories =
        ["tests/suite", "tests/suite/vendored"];
    private static NativeCommandResult Answer(object value) => new(NativeTermination.Exited,
        0, JsonSerializer.Serialize(value), "", 0, null);
    private static RubyRepositoryRequest Request(string root) => new(new(
        Path.Combine(root, "ruby"), Path.Combine(root, "facts.rb"), root,
        new Dictionary<string, string?> { ["PATH"] = "prepared" }, 30),
        Path.Combine(root, "nbgv"), "2.7.2");

    // Bundle-collection rows that vary only native answers read one class-owned commit; each
    // row writes native requests to its own scratch, never the checkout.
    private static GitFixture? sharedBundle;
    private static GitMaterialization? sharedBundleCheckout;

    [ClassInitialize]
    public static async Task InitializeAsync(TestContext classContext)
    {
        GitFixture repo = await GitFixture.CreateAsync(classContext.CancellationToken);
        try
        {
            await repo.SetAsync("node/Gemfile", "native declaration");
            await repo.SetAsync("node/Gemfile.lock", "native lock");
            string head = await repo.CommitAsync();
            await repo.GitAsync("reset", "--hard", head);
            sharedBundleCheckout = await GitMaterialization.BindAsync(repo.Directory,
                await new GitReader(repo.Directory).ReadAsync(head,
                    classContext.CancellationToken), classContext.CancellationToken);
            sharedBundle = repo;
        }
        catch { repo.Dispose(); throw; }
    }

    [ClassCleanup]
    public static void Cleanup() => sharedBundle?.Dispose();

    private sealed class Scratch : IDisposable
    {
        internal string Directory { get; } =
            System.IO.Directory.CreateTempSubdirectory("workflow-ruby-reader-").FullName;
        public void Dispose() => System.IO.Directory.Delete(Directory, recursive: true);
    }

    // The shared commit with this row's own native request scratch.
    private static RubyRepositoryRequest SharedRequest(string scratch) => Request(
        sharedBundle!.Directory) with
    {
        Tools = Request(sharedBundle.Directory).Tools with { Scratch = scratch }
    };

    private async Task<GitMaterialization> BindAsync(GitFixture repo)
    {
        string head = await repo.CommitAsync();
        await repo.GitAsync("reset", "--hard", head);
        return await GitMaterialization.BindAsync(repo.Directory,
            await new GitReader(repo.Directory).ReadAsync(head, context.CancellationToken),
            context.CancellationToken);
    }

    [TestMethod]
    public async Task DeclaredBundleOnlySuiteOwnsNativeHelpersWithoutInventingPackage()
    {
        using var repo = await GitFixture.CreateAsync(context.CancellationToken);
        string directory = RubyNativeTestInputs.Directory;
        await repo.SetAsync(directory + "/Gemfile", "native declaration");
        await repo.SetAsync(directory + "/Gemfile.lock", "native lock");
        await repo.SetAsync(directory + "/spec/facts_spec.rb", "native examples");
        await repo.SetAsync(directory + "/workflow-delivery.quality.yml",
            "schema: workflow-delivery/v3/quality-selection\necosystems:\n  ruby:\n" +
            "    preset: ruby/rspec-v1\n");
        string helper = "src/private/app/workflow-delivery/Native/Ruby/facts.rb";
        await repo.SetAsync(helper, "native implementation");
        GitMaterialization checkout = await BindAsync(repo);
        RubyRevisionInputs result = await RubyRepositoryReader.ReadAsync(checkout,
            Request(repo.Directory), async (command, token) =>
            {
                using JsonDocument input = JsonDocument.Parse(
                    await File.ReadAllTextAsync(command.Arguments[^1], token));
                string gemfile = Path.Combine(repo.Directory, directory, "Gemfile");
                if (input.RootElement.GetProperty("operation").GetString() == "bundle-context")
                    return Answer(new
                    {
                        gemfile,
                        lockfile = gemfile + ".lock",
                        bundler_version = "2.7.2",
                        locked_bundler_version = "2.7.2"
                    });
                return Answer(new
                {
                    gemfile,
                    lockfile = gemfile + ".lock",
                    bundler_version = "2.7.2",
                    locked_bundler_version = "2.7.2",
                    gemfiles = new[] { gemfile },
                    direct_dependencies = Array.Empty<string>(),
                    specifications = Array.Empty<object>()
                });
            }, context.CancellationToken);
        Assert.IsEmpty(result.Sources);
        Assert.IsEmpty(result.Units);
        Assert.IsEmpty(result.Facts.Native.Graph.Projects);
        ProjectFacts owner = Assert.ContainsSingle(result.Facts.Owners);
        Assert.AreEqual(directory, owner.Directory);
        Assert.IsNull(owner.ReleaseUnit);
        Assert.AreEqual(RubyProjectChecks.RSpec, Assert.ContainsSingle(owner.Checks).Key.Check);
        SharedInput input = Assert.ContainsSingle(result.Facts.OperationInputs.Where(row =>
            row.Path == helper));
        CollectionAssert.AreEqual(new[] { owner.Id }, input.Consumers);
    }

    [TestMethod]
    public async Task BundleOnlySuiteOwnershipStopsAtNestedNativeGems()
    {
        const string quality = "schema: workflow-delivery/v3/quality-selection\necosystems:\n" +
            "  ruby:\n    preset: ruby/rspec-v1\n";
        using var repo = await GitFixture.CreateAsync(context.CancellationToken);
        // A gem nested in a bundle-only suite inherits quality but keeps its own files.
        await repo.SetAsync("tests/suite/Gemfile", "native declaration");
        await repo.SetAsync("tests/suite/Gemfile.lock", "native lock");
        await repo.SetAsync("tests/suite/workflow-delivery.quality.yml", quality);
        await repo.SetAsync("tests/suite/contexts/Gemfile", "native declaration");
        await repo.SetAsync("tests/suite/contexts/Gemfile.lock", "native lock");
        await repo.SetAsync("tests/suite/spec/a_spec.rb", "native examples");
        await repo.SetAsync("tests/suite/vendored/asciidoctor-latexmath.gemspec",
            "native source");
        await repo.SetAsync("tests/suite/vendored/lib.rb", "native source");
        await repo.SetAsync("tests/suite/vendored/version.json", "{\"version\":\"1.2\"}");
        await repo.SetAsync("tests/suite/vendored/Gemfile", "native declaration");
        await repo.SetAsync("tests/suite/vendored/Gemfile.lock", "native lock");
        GitMaterialization checkout = await BindAsync(repo);
        RubyRepositoryRequest request = Request(repo.Directory);
        RubyRevisionInputs result = await RubyRepositoryReader.ReadAsync(checkout, request,
            async (command, token) =>
            {
                if (command.Executable == request.Nbgv)
                    return Answer(new { SemVer2 = "1.2.3" });
                using JsonDocument input = JsonDocument.Parse(
                    await File.ReadAllTextAsync(command.Arguments[^1], token));
                string path = input.RootElement.GetProperty("path").GetString()!;
                string operation = input.RootElement.GetProperty("operation").GetString()!;
                if (operation == "gemspec")
                    return Answer(new
                    {
                        gemspec = path,
                        name = Path.GetFileNameWithoutExtension(path),
                        version = "1.2.3",
                        platform = "ruby",
                        require_paths = RequirePaths,
                        files = Array.Empty<string>(),
                        runtime_dependencies = Array.Empty<object>()
                    });
                if (operation == "version")
                    return Answer(new
                    {
                        gemspec = path,
                        raw = "1.2.3",
                        version = "1.2.3",
                        literal = "\"1.2.3\""
                    });
                if (operation == "bundle-context")
                    return Answer(new
                    {
                        gemfile = path,
                        lockfile = path + ".lock",
                        bundler_version = "2.7.2",
                        locked_bundler_version = "2.7.2"
                    });
                return Answer(new
                {
                    gemfile = path,
                    lockfile = path + ".lock",
                    bundler_version = "2.7.2",
                    locked_bundler_version = "2.7.2",
                    gemfiles = new[] { path },
                    direct_dependencies = Array.Empty<string>(),
                    specifications = Array.Empty<object>()
                });
            }, context.CancellationToken);
        CollectionAssert.AreEquivalent(NestedOwnerDirectories,
            result.Facts.Owners.Select(owner => owner.Directory).ToArray());
        ProjectFacts suite = result.Facts.Owners.Single(owner =>
            owner.Directory == "tests/suite");
        Assert.Contains("tests/suite/spec/a_spec.rb", suite.OwnedPaths!);
        Assert.Contains("tests/suite/contexts/Gemfile", suite.OwnedPaths!);
        Assert.IsFalse(suite.OwnedPaths!.Any(path =>
            path.StartsWith("tests/suite/vendored/", StringComparison.Ordinal)));
        Assert.IsTrue(result.Facts.OperationInputs.Any(input =>
            input.Path == "tests/suite/contexts/Gemfile" && input.Consumers.Contains(suite.Id)));
        Assert.IsFalse(result.Facts.OperationInputs.Any(input =>
            input.Path == "tests/suite/vendored/Gemfile" && input.Consumers.Contains(suite.Id)));
    }

    [TestMethod]
    public async Task NativeCollectorsUseLockedBundleWithoutInventingOwner()
    {
        // Defect-free control for the shared bundle commit.
        GitFixture repo = sharedBundle!;
        GitMaterialization checkout = sharedBundleCheckout!;
        using var scratch = new Scratch();
        RubyRepositoryRequest request = SharedRequest(scratch.Directory);
        RubyRevisionInputs result = await RubyRepositoryReader.ReadAsync(checkout, request,
            async (command, token) =>
            {
                using JsonDocument input = JsonDocument.Parse(
                    await File.ReadAllTextAsync(command.Arguments[^1], token));
                string gemfile = Path.Combine(repo.Directory, "node/Gemfile");
                Assert.AreEqual(gemfile, input.RootElement.GetProperty("path").GetString());
                Assert.IsFalse(command.InheritEnvironment);
                if (input.RootElement.GetProperty("operation").GetString() == "bundle-context")
                    return Answer(new
                    {
                        gemfile,
                        lockfile = gemfile + ".lock",
                        bundler_version = "2.7.2",
                        locked_bundler_version = "2.4.20"
                    });
                Assert.AreEqual("bundle", input.RootElement.GetProperty("operation").GetString());
                Assert.AreEqual("2.4.20", command.Arguments[2]);
                Assert.AreEqual("1", command.Environment!["BUNDLE_FROZEN"]);
                return Answer(new
                {
                    gemfile,
                    lockfile = gemfile + ".lock",
                    bundler_version = "2.4.20",
                    locked_bundler_version = "2.4.20",
                    gemfiles = new[] { gemfile },
                    direct_dependencies = Array.Empty<string>(),
                    specifications = Array.Empty<object>()
                });
            }, context.CancellationToken);
        Assert.IsEmpty(result.Sources);
        Assert.IsEmpty(result.Facts.Owners);
        Assert.IsEmpty(result.Facts.Native.Graph.Projects);
        Assert.AreEqual("node/Gemfile", Assert.ContainsSingle(result.Facts.Native.Graph.Bundles)
            .Gemfile);
        Assert.AreEqual("2.4.20", Assert.ContainsSingle(result.Bundles).BundlerVersion);
        Assert.IsTrue(result.Facts.OperationInputs.All(input => input.Consumers.Length == 0));
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
        foreach (string path in RubyRepositoryReader.CallerSources)
            await repo.SetAsync(path, "original caller input");
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
                    return Answer(new
                    {
                        gemspec,
                        raw = "1.2.3+original",
                        version = "1.2.3",
                        literal = "\"1.2.3\""
                    });
                Assert.AreEqual("gemspec", input.RootElement.GetProperty("operation").GetString());
                return Answer(new
                {
                    gemspec,
                    name,
                    version = "1.2.3",
                    platform = "ruby",
                    require_paths = RequirePaths,
                    files = Array.Empty<string>(),
                    runtime_dependencies = Array.Empty<object>()
                });
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
        foreach (string path in RubyRepositoryReader.CallerSources)
            Assert.AreEqual(owner.Id, Assert.ContainsSingle(result.Facts.OperationInputs
                .Single(input => input.Path == path).Consumers));
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
                result.Facts.OperationInputs.Single(input => input.Path ==
                    directory + "/workflow-delivery.release-unit.yml").Consumers));
        }
    }

    [TestMethod]
    [DataRow("failure", "Native Ruby fact collection failed")]
    [DataRow("lock-mismatch", "Native Bundler version differs from the request.")]
    [DataRow("incomplete-answer", "Incomplete native Ruby fact response.")]
    public async Task NativeCollectionFailureCannotBecomeEndpointFacts(string defect,
        string diagnostic)
    {
        GitFixture repo = sharedBundle!;
        using var scratch = new Scratch();
        InvalidDataException error = await Assert.ThrowsExactlyAsync<InvalidDataException>(() =>
            RubyRepositoryReader.ReadAsync(sharedBundleCheckout!, SharedRequest(scratch.Directory),
            async (command, token) =>
            {
                using JsonDocument input = JsonDocument.Parse(
                    await File.ReadAllTextAsync(command.Arguments[^1], token));
                if (defect == "failure") return Answer(new { }) with { ExitCode = 1 };
                string gemfile = Path.Combine(repo.Directory, "node/Gemfile");
                if (input.RootElement.GetProperty("operation").GetString() == "bundle-context")
                    return Answer(new
                    {
                        gemfile,
                        lockfile = gemfile + ".lock",
                        bundler_version = "2.7.2",
                        locked_bundler_version = "2.4.20"
                    });
                if (defect == "incomplete-answer") return Answer(new { });
                return Answer(new
                {
                    gemfile,
                    lockfile = gemfile + ".lock",
                    bundler_version = "2.7.2",
                    locked_bundler_version = "2.7.2",
                    gemfiles = new[] { gemfile },
                    direct_dependencies = Array.Empty<string>(),
                    specifications = Array.Empty<object>()
                });
            }, context.CancellationToken));
        Assert.StartsWith(diagnostic, error.Message);
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
