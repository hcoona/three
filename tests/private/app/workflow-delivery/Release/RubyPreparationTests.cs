using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Nodes;
using WorkflowDelivery.Platform;
using WorkflowDelivery.Destinations;
using WorkflowDelivery.Release;
using GitFixture = WorkflowDelivery.Tests.Repository.GitReaderTests.GitFixture;

namespace WorkflowDelivery.Tests.Release;

[TestClass]
public sealed class RubyPreparationTests(TestContext context)
{
    private static readonly string[] Checks = ["native-input", "build", "contents",
        "consumer", "original-bytes"];
    private static readonly string[] Tested = ["native-input", "build", "contents",
        "consumer", "tests", "original-bytes"];
    private static readonly string[] AmbientSecrets = ["GEM_HOST_API_KEY", "GITHUB_TOKEN",
        "HTTPS_PROXY"];
    private static GitFixture? shared;
    private static string sharedRevision = "";
    private const string Channel = "Unsupported Ruby Release channel or ref.";
    private const string Recipe = "Unsupported complete Ruby Release recipe.";
    private const string Quality = "Unsupported complete Ruby quality selection.";

    // The smoke gem generates version.rb in its checkout; those rows need a writable copy.
    private static Fixture Smoke(bool writable = false, string directory = "product") =>
        Fixture.Shared(shared!, sharedRevision, directory, writable: writable);

    private static Fixture Latexmath() =>
        Fixture.Shared(shared!, sharedRevision, "latexmath", unit: Fixture.Latexmath);

    [ClassInitialize]
    public static async Task InitializeAsync(TestContext classContext) =>
        (shared, sharedRevision) = await Fixture.CommitAsync(async repo =>
        {
            await Fixture.StageRootAsync(repo);
            await Fixture.StageProductAsync(repo, "product");
            await Fixture.StageProductAsync(repo, "recipes/control");
            await Fixture.StageProductAsync(repo, "recipes/quality", preset: "ruby/unknown-v1");
            await Fixture.StageProductAsync(repo, "latexmath", Fixture.Latexmath);
        }, classContext.CancellationToken);

    [ClassCleanup]
    public static void Cleanup() => shared?.Dispose();

    [TestMethod]
    [DataRow("product")]
    [DataRow("recipes/control")]
    [DataRow("product", "official", "rubygems", "refs/heads/main", false, false, false)]
    [DataRow("product", "official", "rubygems", "refs/tags/preview", true, false)]
    [DataRow("product", "buddy", "github-packages-ruby", "refs/heads/topic", false, false)]
    public async Task SmokeQualifiesOriginalForAdmittedPathAndProjection(string directory,
        string channel = "buddy", string destination = "github-packages-ruby",
        string selectedRef = "refs/heads/topic", bool dryRun = false, bool publicRelease = true,
        bool allowed = true)
    {
        using var fixture = Smoke(writable: allowed, directory);
        fixture.Subject = fixture.Subject with
        {
            Channel = channel,
            Destination = destination,
            SelectedRef = selectedRef,
            DryRun = dryRun,
        };
        fixture.ChangeAnswer("projection", answer => answer["PublicRelease"] = publicRelease);
        if (!allowed)
        {
            ReleasePreparationResult rejected = await StoppedAsync(fixture, "native-input");
            Assert.AreEqual("Native Ruby source/ref projection differs.",
                Assert.ContainsSingle(rejected.Errors));
            return;
        }
        ReleasePreparationResult result = await fixture.RunAsync(context.CancellationToken);
        Assert.IsEmpty(result.Errors, string.Join("; ", result.Errors));
        ReleaseCompletion.Validate(result.Prepared);
        Assert.AreEqual(fixture.Subject, result.Prepared.Subject);
        CollectionAssert.AreEqual(Checks, result.Prepared.RequiredChecks);
        Assert.IsTrue(result.Prepared.Checks.All(check =>
            check.Status == ReleaseCheckStatus.Passed));
        ReleaseFile file = Assert.ContainsSingle(result.Prepared.Files);
        Assert.AreEqual(("gem-output", Fixture.Unit, "1.2.3", "package/original.gem", 8L),
            (file.Output, file.Name, file.Version, file.Path, file.Size));
        Assert.AreEqual(Convert.ToHexStringLower(SHA256.HashData("original"u8)), file.Sha256);
        Assert.AreEqual("original", File.ReadAllText(Path.Combine(fixture.Scratch, file.Path)));
        Assert.AreEqual(fixture.Subject.Revision, result.Native["commit"]);
        Assert.AreEqual(RepositoryToolSelection.Locked("ruby"), result.Native["ruby"]);
        Assert.AreEqual("3.10.94", result.Native["nbgv"]);
        Assert.AreEqual(publicRelease ? "true" : "false", result.Native["public-release"]);
        foreach (ReleaseCommand receipt in result.Commands)
        {
            NativeCommand command = receipt.Command;
            Assert.IsFalse(command.InheritEnvironment);
            Assert.IsTrue(command.CloseStandardInput);
            Assert.AreEqual(fixture.Subject.SelectedRef, command.Environment!["GITHUB_REF"]);
            Assert.AreEqual(fixture.Subject.Revision, command.Environment["GITHUB_SHA"]);
            Assert.StartsWith(fixture.Scratch, command.Environment["HOME"]!);
            Assert.IsFalse(AmbientSecrets.Any(command.Environment.ContainsKey));
        }
        NativeCommand mise = Assert.ContainsSingle(result.Commands.Where(command =>
            command.Command.Executable == "mise")).Command;
        Assert.AreEqual(fixture.Request.MiseDataDirectory, mise.Environment!["MISE_DATA_DIR"]);
        Assert.AreEqual(fixture.Checkout, mise.Environment["MISE_TRUSTED_CONFIG_PATHS"]);
        NativeCommand build = result.Commands.First(command =>
            command.Command.Arguments.Contains("build")).Command;
        Assert.Contains("--norc", build.Arguments);
        Assert.Contains(Path.Combine(fixture.Checkout, $"{directory}/{Fixture.Unit}.gemspec"),
            build.Arguments);
        NativeCommand install = result.Commands.Single(command =>
            command.Command.Arguments is [_, "install", "--norc", ..]).Command;
        Assert.Contains(Path.Combine(fixture.Scratch, "package", "original.gem"),
            install.Arguments);
        Assert.Contains("--local", install.Arguments);
        NativeCommand consumer = result.Commands[^1].Command;
        Assert.StartsWith(fixture.Scratch, consumer.Directory);
        Assert.Contains("File.realpath(spec.full_gem_path)", consumer.Arguments[1]);
        Assert.Contains("spec.platform.to_s == 'ruby'", consumer.Arguments[1]);
        Assert.IsEmpty(Directory.GetFiles(Path.Combine(fixture.Scratch, "queries")));
    }

    [TestMethod]
    [DataRow("product", "rubygems", Channel)]
    [DataRow("recipes/quality", "github-packages-ruby", Quality)]
    [DataRow("product", "github-packages-ruby", "Ruby Release requires the exact source revision.",
        12)]
    [DataRow("product", "github-packages-ruby", Channel, null, "refs/heads/main")]
    [DataRow("product", "rubygems", Channel, null, "")]
    public async Task InvalidSubjectOrQualityStopsBeforeNativeWork(string directory,
        string destination, string diagnostic, int? revisionLength = null,
        string? officialDryRunRef = null)
    {
        using var fixture = Smoke(directory: directory);
        string revision = fixture.Subject.Revision;
        fixture.Subject = fixture.Subject with
        {
            Destination = destination,
            Revision = revision[..(revisionLength ?? revision.Length)],
            Channel = officialDryRunRef is null ? "buddy" : "official",
            SelectedRef = officialDryRunRef ?? fixture.Subject.SelectedRef,
            DryRun = officialDryRunRef is not null,
        };
        ReleasePreparationResult result = await StoppedAsync(fixture, "native-input");
        Assert.AreEqual(fixture.Subject, result.Prepared.Subject);
        Assert.AreEqual(diagnostic, Assert.ContainsSingle(result.Errors));
        Assert.IsEmpty(result.Commands);
    }

    [TestMethod]
    [DataRow("product/workflow-delivery.release-unit.yml", null,
        "Missing committed Ruby release declaration.")]
    [DataRow(Fixture.Entry, null, "Missing committed adopted Ruby entry.")]
    [DataRow("ruby/gem-v1", "ruby/unknown-v1", Recipe)]
    [DataRow("entry-point: hcoona-release-smoke-ruby.gemspec", "entry-point: other.gemspec",
        "Missing committed adopted Ruby entry.")]
    public async Task UnsupportedCommittedRecipeIsRejected(string input, string? replacement,
        string message)
    {
        var files = Fixture.ProductFiles("product");
        ReleaseSubject subject = Fixture.SubjectFor(sharedRevision, "product");
        if (replacement is null) files.Remove(input);
        else files[subject.Declaration] = files[subject.Declaration]
            .Replace(input, replacement, StringComparison.Ordinal);
        InvalidDataException error = await Assert.ThrowsExactlyAsync<InvalidDataException>(() =>
            RubyPreparation.ReadRecipeAsync(subject, shared!.Directory,
                (path, _) => Task.FromResult(files.GetValueOrDefault(path)),
                context.CancellationToken));
        Assert.AreEqual(message, error.Message);
    }

    [TestMethod]
    [DataRow(true)]
    [DataRow(false)]
    public async Task ExtraBuildOrOutputStopsBeforeNativeWork(bool extraBuild)
    {
        const string Product = "product";
        const string Declaration = Product + "/workflow-delivery.release-unit.yml";
        const string ExtraOutput = "{id: extra, role: primary-package, kind: ruby-gem}";
        (GitFixture repo, string revision) = await Fixture.CommitAsync(async git =>
        {
            await Fixture.StageRootAsync(git);
            await Fixture.StageProductAsync(git, Product);
            string declaration = Fixture.ProductFiles(Product)[Declaration];
            string expanded = extraBuild ? declaration + "\n" + $$"""
                  - id: extra
                    definition: ruby/gem-v1
                    entry-point: {{Fixture.Unit}}.gemspec
                    outputs: [{{ExtraOutput}}]
                """ : declaration[..^1] + ", " + ExtraOutput + "]";
            await git.SetAsync(Declaration, expanded);
        }, context.CancellationToken);
        using var repository = repo;
        using var fixture = Fixture.Shared(repository, revision, Product);
        fixture.Override = _ => throw new AssertFailedException("Unexpected native preparation.");

        ReleasePreparationResult result = await StoppedAsync(fixture, "native-input");
        Assert.AreEqual(Recipe, Assert.ContainsSingle(result.Errors));
        Assert.IsEmpty(result.Commands);
    }

    [TestMethod]
    [DataRow("gemspec", "name", "other")]
    [DataRow("projection", "GitCommitId", "0000000000000000000000000000000000000000")]
    [DataRow("runtime", "ruby", "0.0.0", "differs from the committed locked selection")]
    [DataRow("mise", "path", "product/mise.toml", "not the committed root", "source")]
    public async Task NativeFactsMustMatchCommittedInputs(string operation, string field,
        string value, string? diagnostic = null, string? sourceObject = null)
    {
        using var fixture = Smoke(writable: operation == "gemspec");
        fixture.ChangeAnswer(operation, answer =>
        {
            if (sourceObject is null) answer[field] = value;
            else answer[sourceObject]![field] = Path.GetFullPath(value, fixture.Checkout);
        });
        ReleasePreparationResult result = await StoppedAsync(fixture, "native-input");
        Assert.DoesNotContain("build", result.Commands.Select(command => command.Check).ToArray());
        if (diagnostic is not null)
        {
            string error = Assert.ContainsSingle(result.Errors);
            Assert.Contains(" ruby ", error);
            Assert.Contains(diagnostic, error);
        }
    }

    [TestMethod]
    [DataRow(null, null)]
    [DataRow("mise.lock", "Missing committed native input: mise.lock")]
    [DataRow(".config/dotnet-tools.json", "Missing committed NBGV tool manifest.")]
    [DataRow("global.json", "Missing SDK selection.")]
    [DataRow(Fixture.HelperPath, null)]
    public async Task CommittedRootDeterminesNativeToolSelection(string? omit, string? diagnostic)
    {
        using var fixture = await Fixture.CreateAsync(omit,
            omit is null ? (path, content) =>
                RepositoryToolSelection.RaiseMajor(path, content, "ruby") : null,
            context.CancellationToken);
        if (omit is null)
        {
            ReleasePreparationResult selected = await fixture.RunAsync(context.CancellationToken);
            Assert.IsEmpty(selected.Errors, string.Join("; ", selected.Errors));
            string expected = RepositoryToolSelection.NextMajor(
                RepositoryToolSelection.Locked("ruby"));
            Assert.AreEqual(expected, selected.Native["ruby"]);
            return;
        }
        ReleasePreparationResult result = await StoppedAsync(fixture, "native-input");
        if (diagnostic is not null)
        {
            Assert.AreEqual(diagnostic, Assert.ContainsSingle(result.Errors));
            Assert.DoesNotContain("mise",
                result.Commands.Select(command => command.Command.Executable));
        }
    }

    [TestMethod]
    [DataRow("--is-shallow-repository", "true")]
    [DataRow("--version", "10.0.400")]
    [DataRow("tool", "")]
    public async Task UnpreparedHistoryOrToolCannotBuild(string argument, string answer)
    {
        using var fixture = Smoke();
        fixture.Override = command => command.Arguments.Contains(argument)
            ? Fixture.Success(answer) : null;
        await StoppedAsync(fixture, "native-input");
    }

    [TestMethod]
    public async Task LatexmathOriginalQualifiesWithLockedRuntimeDependencies()
    {
        using var fixture = Latexmath();
        fixture.Subject = fixture.Subject with
        {
            Channel = "official",
            Destination = "rubygems",
            SelectedRef = "refs/heads/main"
        };
        ReleasePreparationResult result = await fixture.RunAsync(context.CancellationToken);
        Assert.IsEmpty(result.Errors, string.Join("; ", result.Errors));
        ReleaseAdapters.Ruby.ValidatePreparation(result);
        Assert.AreEqual(Fixture.Latexmath, Assert.ContainsSingle(result.Prepared.Files).Name);
        CollectionAssert.AreEqual(Tested, result.Prepared.RequiredChecks);
        string gemfile = Path.Combine(fixture.Checkout, "latexmath", "Gemfile");
        NativeCommand[] tests = Commands("tests");
        Assert.HasCount(4, tests);
        Assert.IsTrue(tests.All(command => command.Environment!["BUNDLE_WITHOUT"] is null &&
            command.Environment["BUNDLE_FROZEN"] == "1" &&
            command.Environment["BUNDLE_GEMFILE"] == gemfile));
        Assert.Contains("rspec", tests[^1].Arguments);
        NativeCommand[] consumer = Commands("consumer");
        Assert.HasCount(5, consumer);
        Assert.Contains(Fixture.LockedBundler, consumer[1].Arguments);
        Assert.AreEqual("development", consumer[2].Environment!["BUNDLE_WITHOUT"]);
        Assert.AreEqual("1", consumer[2].Environment!["BUNDLE_FROZEN"]);
        Assert.AreEqual(gemfile, consumer[2].Environment!["BUNDLE_GEMFILE"]);
        Assert.Contains("--local", consumer[3].Arguments);
        Assert.IsNull(consumer[^1].Environment!["GEM_PATH"]);

        NativeCommand[] Commands(string phase) => [.. result.Commands
            .Where(command => command.Check == phase).Select(command => command.Command)];
    }

    [TestMethod]
    [DataRow(false, "name", "\"other\"")]
    [DataRow(false, "extensions", "[\"ext/extconf.rb\"]")]
    [DataRow(false, "metadata", "{\"github_repo\":\"https://other.invalid\"}")]
    [DataRow(false, "contents", "\"extra.rb\"", true)]
    [DataRow(false, "contents",
        "\"lib/hcoona_release_smoke_ruby/_workflow_delivery_provenance.json\"", true)]
    [DataRow(false, "runtime_dependencies", "[{\"name\":\"extra\",\"requirement\":\">= 0\"}]")]
    [DataRow(true, "runtime_dependencies", "[]")]
    [DataRow(true, "contents", "[]")]
    public async Task UnsupportedArchiveCannotReachConsumer(bool latexmath, string field,
        string? value, bool append = false)
    {
        using var fixture = latexmath ? Latexmath() : Smoke(writable: true);
        fixture.ChangeAnswer("contents", answer =>
        {
            if (value is null) answer.Remove(field);
            else if (append) answer[field]!.AsArray().Add(JsonNode.Parse(value));
            else answer[field] = JsonNode.Parse(value);
        });
        ReleasePreparationResult result = await StoppedAsync(fixture, "contents",
            expected: latexmath ? Tested : Checks);
        Assert.AreEqual("contents", result.Commands[^1].Check);
        Assert.IsTrue(result.Commands.All(command => command.Result.Succeeded));
    }

    [TestMethod]
    [DataRow(0)]
    [DataRow(1)]
    public async Task LatexmathMissingOrFailedSuiteBlocksQualification(int exitCode)
    {
        using var fixture = Latexmath();
        fixture.Override = command =>
        {
            if (!command.Arguments.Contains("rspec")) return null;
            if (exitCode != 0)
                File.WriteAllText(command.Arguments[^1], Fixture.SuiteReport
                    .Replace("\"passed\"", "\"failed\"", StringComparison.Ordinal)
                    .Replace("\"failure_count\":0", "\"failure_count\":1",
                        StringComparison.Ordinal));
            return Fixture.Success("") with { ExitCode = exitCode };
        };
        await StoppedAsync(fixture, "tests", expected: Tested);
    }

    [TestMethod]
    [DataRow("native-input", "StartFailed", null, "Failed")]
    [DataRow("build", "Exited", 7, "Failed")]
    [DataRow("build", "TimedOut", null, "TimedOut")]
    [DataRow("consumer", "Cancelled", null, "Cancelled")]
    [DataRow("build", "Exited", 0, "Failed", "build")]
    [DataRow("consumer", "Exited", 2, "Failed", "-e")]
    public async Task FailedPhaseRetainsReceiptAndSkipsDependents(string phase,
        string termination, int? exitCode, string status,
        string? argument = null)
    {
        using var fixture = Smoke(writable: phase != "native-input");
        var native = exitCode is 0 or 2 ? Fixture.Success("{}") with { ExitCode = exitCode }
            : new NativeCommandResult(Enum.Parse<NativeTermination>(termination), exitCode,
                "partial native output",
                "native diagnostic", 0.25, "controlled failure");
        fixture.Override = command => fixture.SmokePhase(command) == phase &&
            (argument is null || command.Arguments.Contains(argument)) ? native : null;
        ReleasePreparationResult result = await fixture.RunAsync(context.CancellationToken);
        ReleasePreparationResult transfer = JsonSerializer.Deserialize(
            JsonSerializer.Serialize(result, TransferJson.Default.ReleasePreparationResult),
            TransferJson.Default.ReleasePreparationResult)!;
        RequireStopped(transfer, phase, Enum.Parse<ReleaseCheckStatus>(status));
        ReleaseCommand failed = transfer.Commands[^1];
        if (!native.Succeeded)
            Assert.ContainsSingle(transfer.Commands.Where(command => !command.Result.Succeeded));
        Assert.AreEqual(phase, failed.Check);
        Assert.AreEqual(native, failed.Result);
        Assert.IsTrue(failed.Command.CloseStandardInput);
        Assert.IsFalse(failed.Command.InheritEnvironment);
    }

    [TestMethod]
    [DataRow("build", "short")]
    [DataRow("contents", "replaced")]
    [DataRow("consumer", "replaced")]
    [DataRow("consumer", null)]
    public async Task ChangedOriginalCannotQualifyRelease(string phase, string? content)
    {
        using var fixture = Smoke(writable: true);
        fixture.Override = command =>
        {
            if (fixture.SmokePhase(command) != phase ||
                phase == "build" && command.Arguments[0] != fixture.Helper) return null;
            if (content is null) File.Delete(fixture.Original);
            else File.WriteAllText(fixture.Original, content);
            return null;
        };
        ReleasePreparationResult result = await StoppedAsync(fixture, phase);
        Assert.Contains("Original gem changed", string.Join(";", result.Errors));
        Assert.IsTrue(result.Commands.All(command => command.Result.Succeeded));
    }

    [TestMethod]
    public async Task CancellationBeforeWorkRetainsCompleteObligations()
    {
        using var fixture = Smoke();
        using var cancelled = new CancellationTokenSource();
        await cancelled.CancelAsync();
        ReleasePreparationResult result = await fixture.RunAsync(cancelled.Token);
        RequireStopped(result, "native-input", ReleaseCheckStatus.Cancelled);
        Assert.IsEmpty(result.Commands);
    }

    [TestMethod]
    [DataRow("relative", null, null, null, false)]
    [DataRow(null, "gem", null, null, false)]
    [DataRow(null, null, "mise", null, false)]
    [DataRow(null, null, null, "mise-data", false)]
    [DataRow(null, null, null, null, true)]
    [DataRow(null, null, null, null, false, "keep")]
    public async Task UnownedOrRelativePathsFailBeforeCommands(string? checkout,
        string? gem, string? mise, string? miseData, bool nestedScratch,
        string? unownedContent = null)
    {
        using var fixture = Smoke();
        string unowned = Path.Combine(fixture.Scratch, "unowned");
        if (unownedContent is not null) File.WriteAllText(unowned, unownedContent);
        ReleasePreparationRequest request = fixture.Request with
        {
            Checkout = checkout ?? fixture.Checkout,
            Scratch = nestedScratch ? fixture.Checkout : fixture.Scratch,
            MiseDataDirectory = miseData ?? fixture.Request.MiseDataDirectory,
            Tools = fixture.Tools with
            {
                Gem = gem ?? fixture.Tools.Gem,
                Mise = mise ?? fixture.Tools.Mise
            },
        };
        await AssertPathsRejectedAsync(request);
        if (unownedContent is not null) Assert.AreEqual(unownedContent, File.ReadAllText(unowned));
    }

    private async Task AssertPathsRejectedAsync(ReleasePreparationRequest request) =>
        await Assert.ThrowsExactlyAsync<InvalidDataException>(() =>
            RubyPreparation.PrepareAsync(request, (_, _) => throw new AssertFailedException(
                "Invalid owned state cannot execute native commands."), context.CancellationToken));

    private async Task<ReleasePreparationResult> StoppedAsync(Fixture fixture, string phase,
        string[]? expected = null)
    {
        ReleasePreparationResult result = await fixture.RunAsync(context.CancellationToken);
        RequireStopped(result, phase, ReleaseCheckStatus.Failed, expected);
        return result;
    }

    private static void RequireStopped(ReleasePreparationResult result, string phase,
        ReleaseCheckStatus status, string[]? expected = null)
    {
        string[] checks = expected ?? Checks;
        CollectionAssert.AreEqual(checks, result.Prepared.RequiredChecks);
        CollectionAssert.AreEqual(checks,
            result.Prepared.Checks.Select(check => check.Id).ToArray());
        int index = Array.IndexOf(checks, phase);
        for (int position = 0; position < checks.Length; position++)
            Assert.AreEqual(position < index ? ReleaseCheckStatus.Passed
                : position == index ? status : ReleaseCheckStatus.Skipped,
                result.Prepared.Checks[position].Status, checks[position]);
        Assert.IsEmpty(result.Prepared.Files);
        Assert.IsNotEmpty(result.Errors);
    }

    private sealed class Fixture : IDisposable
    {
        internal const string Unit = "hcoona-release-smoke-ruby";
        internal const string Latexmath = "asciidoctor-latexmath";
        internal const string LockedBundler = "2.4.20";
        internal static readonly string[] LatexmathLibrary = ["lib/asciidoctor-latexmath.rb",
            "lib/asciidoctor/latexmath/version.rb"];
        private static readonly string[] LatexmathFiles = [.. LatexmathLibrary, "README.md",
            "LICENSE"];
        internal const string Entry = "product/hcoona-release-smoke-ruby.gemspec";
        internal const string HelperPath = "src/private/app/workflow-delivery/Native/Ruby/facts.rb";
        private static readonly string[] SmokeFiles = ["LICENSE", "README.md",
            "lib/hcoona_release_smoke_ruby.rb", "lib/hcoona_release_smoke_ruby/version.rb"];
        private int archiveQueries;
        private readonly GitFixture? ownedRepository;
        private readonly string root = Directory.CreateTempSubdirectory("ruby-release-").FullName;
        internal ReleaseSubject Subject { get; set; }
        internal string Scratch { get; }
        internal string Original => Path.Combine(Scratch, "package", "original.gem");
        internal string Checkout { get; }
        internal string Helper => Path.Combine(Checkout, HelperPath);
        internal RubyPreparationTools Tools => new(Path.Combine(Checkout, "ruby"),
            Path.Combine(Checkout, "gem"), Path.Combine(Checkout, "dotnet"),
            Path.Combine(Checkout, "provider", "mise"));
        internal const string RubyGems = "fixture-rubygems";
        internal ReleasePreparationRequest Request => new(Subject, Checkout, Scratch, Tools: Tools,
            MiseDataDirectory: Path.Combine(Checkout, "mise-data"));
        private Action<string, JsonObject>? changeAnswer;
        internal Func<NativeCommand, NativeCommandResult?>? Override { get; set; }

        internal void ChangeAnswer(string operation, Action<JsonObject> change) =>
            changeAnswer = (phase, answer) => { if (phase == operation) change(answer); };

        private Fixture(GitFixture repo, ReleaseSubject subject, bool ownsRepository,
            bool writable = false)
        {
            ownedRepository = ownsRepository ? repo : null;
            Subject = subject;
            try
            {
                Checkout = writable ? PreparationCheckout.Copy(repo.Directory, root)
                    : repo.Directory;
                Scratch = Directory.CreateDirectory(Path.Combine(root, "prepared")).FullName;
            }
            catch
            {
                PreparationCheckout.Delete(root);
                ownedRepository?.Dispose();
                throw;
            }
        }

        internal static async Task<Fixture> CreateAsync(string? omit,
            Func<string, string, string>? changeConfig = null, CancellationToken token = default)
        {
            (GitFixture repo, string revision) = await CommitAsync(async git =>
            {
                await StageRootAsync(git, omit, changeConfig);
                await StageProductAsync(git, "product");
            }, token);
            return new(repo, SubjectFor(revision, "product"), ownsRepository: true);
        }

        internal static async Task<(GitFixture Repo, string Revision)> CommitAsync(
            Func<GitFixture, Task> stage, CancellationToken token)
        {
            GitFixture repo = await GitFixture.CreateAsync(token);
            try
            {
                await stage(repo);
                string revision = await repo.CommitAsync();
                await repo.GitAsync("reset", "--hard", revision);
                return (repo, revision);
            }
            catch
            {
                repo.Dispose();
                throw;
            }
        }

        internal static Fixture Shared(GitFixture repo, string revision, string directory,
            string unit = Unit, bool writable = false) =>
            new(repo, SubjectFor(revision, directory, unit), ownsRepository: false, writable);

        internal static ReleaseSubject SubjectFor(string revision, string directory,
            string unit = Unit) => new(revision, "refs/heads/topic", unit,
                directory + "/workflow-delivery.release-unit.yml", "buddy",
                "github-packages-ruby", false);

        internal static async Task StageRootAsync(GitFixture repo, string? omit = null,
            Func<string, string, string>? changeConfig = null)
        {
            var files = new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["mise.toml"] = RepositoryToolSelection.MiseToml,
                ["mise.lock"] = RepositoryToolSelection.MiseLock,
                [HelperPath] = "fixture",
                [".config/dotnet-tools.json"] = """{"tools":{"nbgv":{"version":"3.10.94"}}}""",
                ["global.json"] = """{"sdk":{"version":"10.0.401","rollForward":"disable"}}""",
            };
            foreach ((string path, string content) in files)
                if (path != omit)
                    await repo.SetAsync(path, changeConfig?.Invoke(path, content) ?? content);
        }

        internal static async Task StageProductAsync(GitFixture repo, string directory,
            string unit = Unit, string? preset = null)
        {
            foreach ((string path, string content) in ProductFiles(directory, unit, preset))
                await repo.SetAsync(path, content);
        }

        internal static Dictionary<string, string> ProductFiles(string directory,
            string unit = Unit, string? preset = null)
        {
            preset ??= unit == Latexmath
                ? "ruby/rspec-gem-v1" : "ruby/hcoona-release-smoke-ruby-v1";
            var files = new Dictionary<string, string>(StringComparer.Ordinal)
            {
                [$"{directory}/{unit}.gemspec"] = "fixture",
                [$"{directory}/workflow-delivery.quality.yml"] = $$$"""
                    schema: workflow-delivery/v3/quality-selection
                    ecosystems: {ruby: {preset: {{{preset}}} } }
                    """,
                [$"{directory}/workflow-delivery.release-unit.yml"] = $$"""
                    schema: workflow-delivery/v3/release-unit
                    release-unit: {{unit}}
                    builds:
                      - id: gem-build
                        definition: ruby/gem-v1
                        entry-point: {{unit}}.gemspec
                        outputs: [{id: gem-output, role: primary-package, kind: ruby-gem}]
                    """,
            };
            if (unit == Latexmath)
                foreach (string file in LatexmathLibrary) files[$"{directory}/{file}"] = "fixture";
            return files;
        }

        internal Task<ReleasePreparationResult> RunAsync(CancellationToken token) =>
            RubyPreparation.PrepareAsync(Request, ExecuteAsync, token);

        internal string SmokePhase(NativeCommand command)
        {
            string[] args = command.Arguments;
            if (args.Contains("build")) return "build";
            if (args is ["-e", ..] || args is [_, "install", ..] &&
                command.Executable == Tools.Ruby) return "consumer";
            if (args[0] == Helper &&
                File.ReadAllText(args[^1]).Contains("\"archive\"", StringComparison.Ordinal))
                return archiveQueries == 0 ? "build" : "contents";
            return "native-input";
        }

        private async Task<NativeCommandResult> ExecuteAsync(NativeCommand command,
            CancellationToken token)
        {
            if (Override?.Invoke(command) is { } result) return result;
            string[] args = command.Arguments;
            if (command.Executable == "mise") return ToolAnswer();
            if (args.Contains("--is-shallow-repository")) return Success("false");
            if (args[0] == "--version") return Success("10.0.401");
            if (args[0] == "tool") return await EmitAsync(Path.Combine(args[^1],
                OperatingSystem.IsWindows() ? "nbgv.exe" : "nbgv"), "tool", token);
            if (args is ["-r", "bundler", ..]) return Success(LockedBundler + "\n");
            if (args.Contains("rspec")) return await EmitAsync(args[^1], SuiteReport, token);
            if (args[0] == Bundle("consumer") || args[0] == Bundle("tests"))
                return Success();
            if (args[0] == "-r") return Answer("runtime", new()
            { ["ruby"] = Selected(), ["rubygems"] = RubyGems });
            if (args[0] == "get-version") return Answer("projection", new()
            {
                ["SemVer2"] = "1.2.3+g1234567",
                ["GitCommitId"] = Subject.Revision,
                ["PublicRelease"] = true
            });
            if (args[0] == Tools.Gem)
            {
                Assert.AreEqual("--norc", args[2]);
                return args[1] == "build" ? await EmitAsync(args[^1], "original", token)
                    : Success();
            }
            if (args[0] == "-e") return Success();
            Assert.AreEqual(Helper, args[0]);
            using JsonDocument input = JsonDocument.Parse(await File.ReadAllTextAsync(args[^1],
                token));
            string operation = input.RootElement.GetProperty("operation").GetString()!;
            string path = input.RootElement.GetProperty("path").GetString()!;
            if (operation == "version") return Answer(operation, new()
            {
                ["gemspec"] = path,
                ["raw"] = input.RootElement.GetProperty("raw").GetString(),
                ["version"] = "1.2.3",
                ["literal"] = "\"1.2.3\"",
            });

            return GemAnswer(operation, path);
        }

        private string Bundle(string phase) => Path.Combine(Scratch, "package", phase, "gems",
            "bin", "bundle");

        private NativeCommandResult GemAnswer(string operation, string path)
        {
            bool latexmath = Subject.Unit == Latexmath;
            string[] files = latexmath ? LatexmathFiles : SmokeFiles;
            bool gemspec = operation == "gemspec";
            var gem = new JsonObject
            {
                ["name"] = Subject.Unit,
                ["version"] = "1.2.3",
                ["platform"] = "ruby",
                ["require_paths"] = new JsonArray("lib"),
                ["extensions"] = new JsonArray(),
                ["files"] = JsonSerializer.SerializeToNode(files),
                ["runtime_dependencies"] = JsonNode.Parse(latexmath
                    ? """[{"name":"asciidoctor","requirement":">= 2.0, < 3.0"}]""" : "[]"),
                ["metadata"] = new JsonObject
                { ["github_repo"] = "https://github.com/hcoona/three" },
                [gemspec ? "gemspec" : "archive"] = path,
            };
            if (gemspec) return Answer(operation, gem);
            Assert.AreEqual("archive", operation);
            gem["contents"] = JsonSerializer.SerializeToNode(files);
            return Answer(archiveQueries++ > 0 || latexmath ? "contents" : "build", gem);
        }

        private NativeCommandResult Answer(string operation, JsonObject answer)
        {
            changeAnswer?.Invoke(operation, answer);
            return Success(answer.ToJsonString());
        }

        internal static NativeCommandResult Success(string stdout = "") =>
            new(NativeTermination.Exited, 0, stdout, "", 0.1, null);

        private static async Task<NativeCommandResult> EmitAsync(string path, string content,
            CancellationToken token)
        {
            await File.WriteAllTextAsync(path, content, token);
            return Success();
        }

        internal const string SuiteReport = """
            {"version":"3.13.0","examples":[{"id":"./spec/suite_spec.rb[1:1]",
              "description":"suite","full_description":"suite","status":"passed",
              "file_path":"./spec/suite_spec.rb","line_number":1,"run_time":0.01}],
              "summary":{"duration":0.01,"example_count":1,"failure_count":0,
                "pending_count":0,"errors_outside_of_examples_count":0}}
            """;

        // The fixture's own committed root lock selects the runtime.
        private string Selected() => RepositoryToolSelection.Locked(
            File.ReadAllText(Path.Combine(Checkout, "mise.lock")), "ruby");

        private NativeCommandResult ToolAnswer()
        {
            string config = Path.Combine(Checkout, "mise.toml");
            var answer = new JsonObject
            {
                ["version"] = Selected(),
                ["active"] = true,
                ["requested_version"] = RepositoryToolSelection.Requested(
                    File.ReadAllText(config), "ruby"),
                ["source"] = new JsonObject { ["type"] = "mise.toml", ["path"] = config },
            };
            changeAnswer?.Invoke("mise", answer);
            return Success(new JsonArray(answer).ToJsonString());
        }

        public void Dispose()
        {
            ownedRepository?.Dispose();
            PreparationCheckout.Delete(root);
        }
    }
}
