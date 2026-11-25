using System.Text.Json;
using WorkflowDelivery.CI;
using WorkflowDelivery.Ecosystems;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using WorkflowDelivery.Platform;
using WorkflowDelivery.Repository;
using GitFixture = WorkflowDelivery.Tests.Repository.GitReaderTests.GitFixture;

namespace WorkflowDelivery.Tests.Repository;

[TestClass]
public sealed class RubyRepositoryReaderTests(TestContext context)
{
    private static readonly string[] RequirePaths = ["lib"];
    private static readonly string[] VersionArguments = ["get-version", "--format", "json"];
    private static readonly string[] NestedOwnerDirectories =
        ["tests/suite", "tests/suite/vendored"];
    private const string Smoke = "hcoona-release-smoke-ruby";
    private const string GeneratedVersion = "/lib/hcoona_release_smoke_ruby/version.rb";
    private const string GemDeclaration = """
        schema: workflow-delivery/v3/release-unit
        release-unit: hcoona-release-smoke-ruby
        builds:
          - id: ruby-gem
            definition: ruby/gem-v1
            entry-point: hcoona-release-smoke-ruby.gemspec
            outputs:
              - id: gem
                role: primary-package
                kind: ruby-gem
        """;
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
        const string evaluated = "shared/dependencies.rb";
        await repo.SetAsync(helper, "native implementation");
        await repo.SetAsync(evaluated, "evaluated Gemfile input");
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
                    gemfiles = new[] { gemfile, Path.Combine(repo.Directory, evaluated) },
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
        RubyFactAssembly assembly = RubyFactsAssembler.Assemble(result.Facts, result.Facts,
            [helper, evaluated], false);
        PlannedCheck check = Assert.ContainsSingle(ImpactPlanner.Plan(assembly.Request,
            assembly.PairedReasons).Checks);
        Assert.AreEqual(RubyProjectChecks.RSpec, check.Work.Key.Check);
        Assert.Contains(new SelectionReason(helper, checkout.Revision.Commit, owner.Id),
            check.Reasons);
        Assert.Contains(new SelectionReason(evaluated, checkout.Revision.Commit, owner.Id),
            check.Reasons);
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
        RubyProjectDependencies producer = Assert.ContainsSingle(result.Facts.Native.Graph
            .Projects);
        Assert.AreEqual("asciidoctor-latexmath", producer.Name);
        Assert.IsEmpty(producer.Ordinary);
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
    [DataRow("node/Gemfile", "node/Gemfile.lock")]
    [DataRow("node/gems.rb", "node/gems.locked")]
    public async Task NativeCollectorsUseLockedBundleWithoutInventingOwner(string entry,
        string locked)
    {
        using GitFixture? alternate = entry == "node/Gemfile" ? null :
            await GitFixture.CreateAsync(context.CancellationToken);
        GitMaterialization checkout = sharedBundleCheckout!;
        using var scratch = new Scratch();
        RubyRepositoryRequest request = SharedRequest(scratch.Directory);
        if (alternate is not null)
        {
            await alternate.SetAsync(entry);
            await alternate.SetAsync(locked);
            checkout = await BindAsync(alternate);
            request = Request(scratch.Directory);
        }
        string gemfile = Path.Combine(checkout.Root, entry);
        string lockfile = Path.Combine(checkout.Root, locked);
        RubyRevisionInputs result = await RubyRepositoryReader.ReadAsync(checkout, request,
            async (command, token) =>
            {
                using JsonDocument input = JsonDocument.Parse(
                    await File.ReadAllTextAsync(command.Arguments[^1], token));
                Assert.AreEqual(gemfile, input.RootElement.GetProperty("path").GetString());
                Assert.IsFalse(command.InheritEnvironment);
                if (input.RootElement.GetProperty("operation").GetString() == "bundle-context")
                    return Answer(new
                    {
                        gemfile,
                        lockfile,
                        bundler_version = "2.7.2",
                        locked_bundler_version = "2.4.20"
                    });
                Assert.AreEqual("bundle", input.RootElement.GetProperty("operation").GetString());
                Assert.AreEqual("2.4.20", command.Arguments[2]);
                Assert.AreEqual("1", command.Environment!["BUNDLE_FROZEN"]);
                return Answer(new
                {
                    gemfile,
                    lockfile,
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
        Assert.AreEqual(entry, Assert.ContainsSingle(result.Facts.Native.Graph.Bundles)
            .Gemfile);
        Assert.AreEqual("2.4.20", Assert.ContainsSingle(result.Bundles).BundlerVersion);
        Assert.IsTrue(result.Facts.OperationInputs.All(input => input.Consumers.Length == 0));
    }

    [TestMethod]
    [DataRow("rspec")]
    [DataRow("input-only")]
    [DataRow("latexmath")]
    [DataRow("latexmath-package")]
    [DataRow("latexmath-rspec")]
    public async Task NativeSourceAdoptionPreservesRequiredChecksAndVersionInputs(string adoption)
    {
        using var repo = await GitFixture.CreateAsync(context.CancellationToken);
        const string name = "asciidoctor-latexmath";
        string real = Path.Combine(RepositoryToolSelection.SourceRoot, "src", "public", "lib",
            name);
        string directory = "src/" + name;
        foreach (string path in RubyRepositoryReader.CallerSources)
            await repo.SetAsync(path, "original caller input");
        await repo.SetAsync(directory + "/" + name + ".gemspec", "original source");
        await repo.SetAsync(directory + "/version.json", "{\"version\":\"1.2\"}");
        await repo.SetAsync(directory + "/spec/check_spec.rb", "project test input");
        if (adoption == "latexmath")
            await repo.SetAsync(directory + "/workflow-delivery.quality.yml",
                await File.ReadAllTextAsync(Path.Combine(real, "workflow-delivery.quality.yml"),
                    context.CancellationToken));
        else if (adoption != "input-only")
            await repo.SetAsync(directory + "/workflow-delivery.quality.yml",
                "schema: workflow-delivery/v3/quality-selection\n" +
                "ecosystems:\n  ruby:\n    preset: " +
                (adoption == "latexmath-package" ? RubyPackageChecks.Preset :
                    RubyProjectChecks.Preset) +
                "\n");
        if (adoption.StartsWith("latexmath", StringComparison.Ordinal))
            await repo.SetAsync(directory + "/workflow-delivery.release-unit.yml",
                await File.ReadAllTextAsync(Path.Combine(real,
                    "workflow-delivery.release-unit.yml"), context.CancellationToken));
        GitMaterialization checkout = await BindAsync(repo);
        RubyRepositoryRequest request = Request(repo.Directory);
        Func<Task<RubyRevisionInputs>> read = () => RubyRepositoryReader.ReadAsync(checkout,
            request, async (command, token) =>
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
        RubyRevisionInputs result = await read();
        if (adoption is "latexmath-package" or "latexmath-rspec")
        {
            // A basis keeps its own adoption, such as the earlier RSpec-only selection, but a
            // registered unit's candidate is held to its registered obligations.
            Assert.AreEqual(adoption == "latexmath-rspec" ? RubyProjectChecks.RSpec :
                RubyPackageChecks.Build, result.Facts.Owners.Single().Checks[0].Key.Check);
            Assert.ThrowsExactly<InvalidDataException>(() =>
                RubyRepositoryReader.RequireRegisteredPresets(result));
            return;
        }
        RubyRepositoryReader.RequireRegisteredPresets(result);
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
            // The committed selection composes whole-project RSpec with the declared gem checks.
            Assert.AreEqual(RubyPackageChecks.RSpecPreset, owner.QualityPreset);
            CollectionAssert.AreEquivalent(
                (string[])[RubyProjectChecks.RSpec, .. RubyPackageChecks.Kinds],
                owner.Checks.Select(check => check.Key.Check).ToArray());
            Assert.AreEqual(directory, owner.Checks.Single(check =>
                check.Key.Check == RubyProjectChecks.RSpec).Key.Target);
            Assert.IsTrue(owner.Checks.Where(check => check.Package is not null).All(check =>
                check.Package!.Unit == "asciidoctor-latexmath" &&
                check.Package.ExpectedVersion == "1.2.3"));
            Assert.AreEqual("asciidoctor-latexmath", owner.ReleaseUnit);
            Assert.AreEqual(owner.Id, Assert.ContainsSingle(
                result.Facts.OperationInputs.Single(input => input.Path ==
                    directory + "/workflow-delivery.release-unit.yml").Consumers));
        }
    }

    [TestMethod]
    [DataRow("src/original", "1.2.3", "1.2.3")]
    [DataRow("src/moved", "1.2.3-beta.4+gabcdef0", "1.2.3.pre.beta.4")]
    public async Task SelectedGemUsesItsOriginalNativeSubjectAndCompletePackageChecks(
        string directory, string raw, string expectedVersion)
    {
        using var repo = await GitFixture.CreateAsync(context.CancellationToken);
        string declaration = directory + "/workflow-delivery.release-unit.yml";
        GitMaterialization checkout = await StageGemAsync(repo, directory, GemDeclaration);
        RubyRepositoryRequest request = Request(repo.Directory);
        RubyRevisionInputs native = await RubyRepositoryReader.ReadAsync(checkout, request,
            (command, token) => ReadGemAsync(command, repo.Directory, directory, raw,
                expectedVersion, (Smoke, expectedVersion, "ruby"), token),
            context.CancellationToken);
        RubyFactAssembly assembly = RubyFactsAssembler.Assemble(native.Facts, native.Facts,
            [declaration], false);
        CiPlan plan = ImpactPlanner.Plan(assembly.Request, assembly.PairedReasons);
        CheckSpec[] checks = plan.Checks.Select(check => check.Work).ToArray();

        CollectionAssert.AreEquivalent(RubyPackageChecks.Kinds,
            checks.Select(check => check.Key.Check).ToArray());
        CheckSpec build = checks.Single(check => check.Key.Check == RubyPackageChecks.Build);
        CheckSpec contents = checks.Single(check => check.Key.Check == RubyPackageChecks.Contents);
        CheckSpec consumer = checks.Single(check => check.Key.Check == RubyPackageChecks.Consumer);
        PackageTarget subject = build.Package!;
        Assert.AreEqual(directory, subject.Directory);
        Assert.AreEqual(directory + "/" + Smoke + ".gemspec", subject.EntryPoint);
        Assert.AreEqual(declaration, subject.Declaration);
        Assert.AreEqual(expectedVersion, subject.ExpectedVersion);
        Assert.AreEqual(new PackageOutput("gem", "primary-package", "ruby-gem"),
            Assert.ContainsSingle(subject.Outputs));
        Assert.IsTrue(checks.All(check => check.Required && check.Package == subject));
        Assert.AreEqual(build.Key, Assert.ContainsSingle(contents.Prerequisites));
        CheckKey[] prerequisites = [build.Key, contents.Key];
        CollectionAssert.AreEquivalent(prerequisites, consumer.Prerequisites);
        Assert.AreEqual("original gemspec", await File.ReadAllTextAsync(
            Assert.ContainsSingle(native.Sources).Gemspec, context.CancellationToken));
        Assert.IsFalse(request.Tools.Environment.ContainsKey("THREE_WORKFLOW_RELEASE_NBGV_PATH"));
    }

    [TestMethod]
    [DataRow("definition: ruby/gem-v1", "definition: ruby/other-v1",
        "Unresolved adopted native Ruby gem subject.")]
    [DataRow("entry-point: " + Smoke + ".gemspec", "entry-point: other.gemspec",
        "Unresolved adopted native Ruby gem subject.")]
    [DataRow("kind: ruby-gem", "kind: npm-tarball", "Unsupported complete Ruby gem output set.")]
    [DataRow("role: primary-package", "role: source-package",
        "Unsupported complete Ruby gem output set.")]
    public async Task UnsupportedGemDeclarationCannotBecomePartialPackageWork(
        string original, string replacement, string diagnostic)
    {
        const string directory = "src/package";
        using var repo = await GitFixture.CreateAsync(context.CancellationToken);
        GitMaterialization checkout = await StageGemAsync(repo, directory,
            GemDeclaration.Replace(original, replacement, StringComparison.Ordinal));

        InvalidDataException failure = await Assert.ThrowsExactlyAsync<InvalidDataException>(() =>
            RubyRepositoryReader.ReadAsync(checkout, Request(repo.Directory),
                (command, token) => ReadGemAsync(command, repo.Directory, directory,
                    "1.2.3", "1.2.3", (Smoke, "1.2.3", "ruby"), token),
                context.CancellationToken));
        Assert.Contains(diagnostic, failure.Message);
    }

    [TestMethod]
    [DataRow(Smoke, "9.9.9", "ruby", "Native gemspec differs from its NBGV projection.")]
    [DataRow(RubyReleaseUnits.Latexmath, "1.2.3", "ruby",
        "Native gemspec differs from its NBGV projection.")]
    [DataRow(Smoke, "1.2.3", "x86_64-linux", "Unresolved adopted native Ruby gem subject.")]
    public async Task NativeGemMustMatchTheProjectedIdentityAndSupportedSubject(
        string name, string version, string platform, string diagnostic)
    {
        const string directory = "src/package";
        using var repo = await GitFixture.CreateAsync(context.CancellationToken);
        GitMaterialization checkout = await StageGemAsync(repo, directory, GemDeclaration);

        InvalidDataException failure = await Assert.ThrowsExactlyAsync<InvalidDataException>(() =>
            RubyRepositoryReader.ReadAsync(checkout, Request(repo.Directory),
                (command, token) => ReadGemAsync(command, repo.Directory, directory,
                    "1.2.3", "1.2.3", (name, version, platform), token),
                context.CancellationToken));
        Assert.Contains(diagnostic, failure.Message);
    }

    [TestMethod]
    public async Task NativePreparationCannotOverwriteACommittedGeneratedVersion()
    {
        const string directory = "src/package";
        const string generated = directory + GeneratedVersion;
        using var repo = await GitFixture.CreateAsync(context.CancellationToken);
        await repo.SetAsync(generated, "committed version");
        GitMaterialization checkout = await StageGemAsync(repo, directory, GemDeclaration);

        InvalidDataException failure = await Assert.ThrowsExactlyAsync<InvalidDataException>(() =>
            RubyRepositoryReader.ReadAsync(checkout, Request(repo.Directory),
                (command, token) => ReadGemAsync(command, repo.Directory, directory,
                    "1.2.3", "1.2.3", (Smoke, "1.2.3", "ruby"), token),
                context.CancellationToken));

        Assert.Contains("Generated Ruby version is a committed input", failure.Message);
        Assert.AreEqual("committed version", await File.ReadAllTextAsync(
            Path.Combine(repo.Directory, generated), context.CancellationToken));
    }

    [TestMethod]
    [DataRow(1, "{\"SemVer2\":\"1.2.3\"}", "Native Ruby source version query failed")]
    [DataRow(0, "{}", "Incomplete native NBGV version answer")]
    public async Task MissingNativeVersionCannotBecomeGemFacts(
        int exitCode, string stdout, string diagnostic)
    {
        using var repo = await GitFixture.CreateAsync(context.CancellationToken);
        GitMaterialization checkout = await StageGemAsync(repo, "src/package", GemDeclaration);
        var result = new NativeCommandResult(NativeTermination.Exited, exitCode, stdout, "",
            0, null);

        InvalidDataException failure = await Assert.ThrowsExactlyAsync<InvalidDataException>(() =>
            RubyRepositoryReader.ReadAsync(checkout, Request(repo.Directory),
                (_, _) => Task.FromResult(result), context.CancellationToken));
        Assert.Contains(diagnostic, failure.Message);
    }

    [TestMethod]
    public async Task UnregisteredGemCannotSilentlyDisappearFromTheNativeInventory()
    {
        using var repo = await GitFixture.CreateAsync(context.CancellationToken);
        await repo.SetAsync("src/other/other.gemspec");
        GitMaterialization checkout = await BindAsync(repo);

        InvalidDataException failure = await Assert.ThrowsExactlyAsync<InvalidDataException>(() =>
            RubyRepositoryReader.ReadAsync(checkout, Request(repo.Directory), (_, _) =>
                throw new AssertFailedException("No version query is valid for this gem."),
                context.CancellationToken));

        Assert.Contains("Unsupported native Ruby source input scope", failure.Message);
    }

    private async Task<GitMaterialization> StageGemAsync(GitFixture repo, string directory,
        string declaration)
    {
        await repo.SetAsync(directory + "/" + Smoke + ".gemspec", "original gemspec");
        await repo.SetAsync(directory + "/lib/hcoona_release_smoke_ruby.rb", "original library");
        await repo.SetAsync(directory + "/version.json", "{\"version\":\"1.2\"}");
        await repo.SetAsync(".gitignore", "**/version.rb\n");
        await repo.SetAsync(directory + "/workflow-delivery.release-unit.yml", declaration);
        await repo.SetAsync(directory + "/workflow-delivery.quality.yml",
            "schema: workflow-delivery/v3/quality-selection\necosystems:\n  ruby:\n    preset: " +
            RubyPackageChecks.Preset + "\n");
        return await BindAsync(repo);
    }

    private static async Task<NativeCommandResult> ReadGemAsync(NativeCommand command, string root,
        string directory, string raw, string version,
        (string Name, string Version, string Platform) gem, CancellationToken token)
    {
        string nbgv = Path.Combine(root, "nbgv");
        Assert.IsFalse(command.InheritEnvironment);
        Assert.AreEqual(nbgv, command.Environment!["THREE_WORKFLOW_RELEASE_NBGV_PATH"]);
        if (command.Executable == nbgv)
        {
            Assert.AreEqual(Path.Combine(root, directory), command.Directory);
            CollectionAssert.AreEqual(VersionArguments, command.Arguments);
            return Answer(new { SemVer2 = raw });
        }
        using JsonDocument input = JsonDocument.Parse(
            await File.ReadAllTextAsync(command.Arguments[^1], token));
        string gemspec = Path.GetFullPath(Path.Combine(root, directory, Smoke + ".gemspec"));
        Assert.AreEqual(gemspec, Path.GetFullPath(
            input.RootElement.GetProperty("path").GetString()!));
        string? operation = input.RootElement.GetProperty("operation").GetString();
        if (operation == "version")
            return Answer(new
            {
                gemspec,
                raw,
                version,
                literal = JsonSerializer.Serialize(version),
            });
        Assert.AreEqual("gemspec", operation);
        Assert.Contains("VERSION = " + JsonSerializer.Serialize(version),
            await File.ReadAllTextAsync(Path.Combine(root, directory + GeneratedVersion), token));
        return Answer(new
        {
            gemspec,
            name = gem.Name,
            version = gem.Version,
            platform = gem.Platform,
            require_paths = RequirePaths,
            files = Array.Empty<string>(),
            runtime_dependencies = Array.Empty<object>(),
        });
    }

    [TestMethod]
    [DataRow(1, "2.7.2", "2.7.2", "Native Ruby fact collection failed")]
    [DataRow(0, "2.4.20", "2.7.2", "Native Bundler version differs from the request.")]
    [DataRow(0, "2.7.2", null, "Incomplete native Ruby fact response.")]
    public async Task NativeBundleCollectionFailureCannotBecomeEndpointFacts(
        int exitCode, string lockedVersion, string? observedVersion, string diagnostic)
    {
        GitFixture repo = sharedBundle!;
        using var scratch = new Scratch();
        InvalidDataException error = await Assert.ThrowsExactlyAsync<InvalidDataException>(() =>
            RubyRepositoryReader.ReadAsync(sharedBundleCheckout!, SharedRequest(scratch.Directory),
            async (command, token) =>
            {
                using JsonDocument input = JsonDocument.Parse(
                    await File.ReadAllTextAsync(command.Arguments[^1], token));
                string gemfile = Path.Combine(repo.Directory, "node/Gemfile");
                if (input.RootElement.GetProperty("operation").GetString() == "bundle-context")
                    return Answer(new
                    {
                        gemfile,
                        lockfile = gemfile + ".lock",
                        bundler_version = "2.7.2",
                        locked_bundler_version = lockedVersion
                    }) with
                    { ExitCode = exitCode };
                if (observedVersion is null) return Answer(new { });
                return Answer(new
                {
                    gemfile,
                    lockfile = gemfile + ".lock",
                    bundler_version = observedVersion,
                    locked_bundler_version = lockedVersion,
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
