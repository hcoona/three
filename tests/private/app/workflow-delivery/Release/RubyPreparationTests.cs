using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Nodes;
using WorkflowDelivery.Platform;
using WorkflowDelivery.Release;
using GitFixture = WorkflowDelivery.Tests.Repository.GitReaderTests.GitFixture;

namespace WorkflowDelivery.Tests.Release;

[TestClass]
public sealed class RubyPreparationTests(TestContext context)
{
    private static readonly string[] Checks =
    [
        "native-input",
        "build",
        "contents",
        "consumer",
        "original-bytes",
    ];

    private static readonly string[] NativeKeys =
    [
        "name",
        "version",
        "platform",
        "commit",
        "public-release",
        "ruby",
        "rubygems",
        "nbgv",
    ];

    // Rows that only vary the request, native answers or committed recipe directory read one
    // committed repository; each row still owns fresh scratch and runs the full preparation.
    private static GitFixture? shared;
    private static string sharedRevision = "";
    // RubySourceCollector writes <product>/lib/.../version.rb into the checkout, so every
    // shared row that can reach source reading gets its own committed product directory.
    private const int LaneCount = 64;
    private static int nextLane = -1;

    private static Fixture SharedLane()
    {
        int lane = Interlocked.Increment(ref nextLane);
        Assert.IsLessThan(LaneCount, lane, "Raise LaneCount for the added shared rows.");
        return Fixture.Shared(shared!, sharedRevision, "lanes/" + lane, "");
    }

    [ClassInitialize]
    public static async Task InitializeAsync(TestContext classContext)
    {
        GitFixture repo = await GitFixture.CreateAsync(classContext.CancellationToken);
        try
        {
            await Fixture.StageRootAsync(repo, "");
            await Fixture.StageProductAsync(repo, "product", "");
            // A defect-free control at the same depth proves the location itself qualifies.
            await Fixture.StageProductAsync(repo, "recipes/control", "");
            foreach (string defect in RecipeDefects)
                await Fixture.StageProductAsync(repo, "recipes/" + defect, defect);
            for (int lane = 0; lane < LaneCount; lane++)
                await Fixture.StageProductAsync(repo, "lanes/" + lane, "");
            sharedRevision = await repo.CommitAsync();
            await repo.GitAsync("reset", "--hard", sharedRevision);
            shared = repo;
        }
        catch
        {
            repo.Dispose();
            throw;
        }
    }

    [ClassCleanup]
    public static void Cleanup() => shared?.Dispose();

    private static readonly string[] RecipeDefects =
    [
        "declaration",
        "build",
        "extra-build",
        "outputs",
        "extra-output",
        "quality",
        "missing-quality",
        "entry",
        "entry-name",
        "unit",
    ];

    [TestMethod]
    public async Task CompleteOriginalQualifiesRelease()
    {
        using var fixture = await Fixture.CreateAsync(context.CancellationToken);
        RubyPreparationResult result = await fixture.RunAsync(context.CancellationToken);
        Assert.IsEmpty(result.Errors, string.Join("; ", result.Errors));
        ReleaseCompletion.Validate(result.Prepared);
        Assert.AreEqual(fixture.Subject, result.Prepared.Subject);
        CollectionAssert.AreEqual(Checks, result.Prepared.RequiredChecks);
        CollectionAssert.AreEqual(
            Checks,
            result.Prepared.Checks.Select(check => check.Id).ToArray()
        );
        foreach (ReleaseCheck check in result.Prepared.Checks)
            Assert.AreEqual(ReleaseCheckStatus.Passed, check.Status, check.Id);
        ReleaseFile file = Assert.ContainsSingle(result.Prepared.Files);
        Assert.AreEqual("gem-build", file.Build);
        Assert.AreEqual("gem-output", file.Output);
        Assert.AreEqual("ruby-gem", file.Kind);
        Assert.AreEqual("hcoona-release-smoke-ruby", file.Name);
        Assert.AreEqual("1.2.3", file.Version);
        Assert.AreEqual("package/original.gem", file.Path);
        Assert.AreEqual(8L, file.Size);
        Assert.AreEqual(Convert.ToHexStringLower(SHA256.HashData("original"u8)), file.Sha256);
        Assert.AreEqual(
            "original",
            await File.ReadAllTextAsync(
                Path.Combine(fixture.Scratch, file.Path),
                context.CancellationToken
            )
        );
        CollectionAssert.AreEquivalent(NativeKeys, result.Native.Keys.ToArray());
        Assert.AreEqual(file.Name, result.Native["name"]);
        Assert.AreEqual(file.Version, result.Native["version"]);
        Assert.AreEqual("ruby", result.Native["platform"]);
        Assert.AreEqual(fixture.Subject.Revision, result.Native["commit"]);
        Assert.AreEqual("true", result.Native["public-release"]);
        Assert.AreEqual("4.0.7", result.Native["ruby"]);
        Assert.AreEqual("4.0.7", result.Native["rubygems"]);
        Assert.AreEqual("3.10.94", result.Native["nbgv"]);
        Assert.IsFalse(
            result.Native.Values.Any(value =>
                value.Contains(fixture.Repo.Directory, StringComparison.Ordinal)
            )
        );
        RubyPreparationRequest request = JsonSerializer.Deserialize(
            JsonSerializer.Serialize(fixture.Request, TransferJson.Default.RubyPreparationRequest),
            TransferJson.Default.RubyPreparationRequest
        )!;
        Assert.AreEqual(fixture.Request, request);
        RubyPreparationResult transfer = JsonSerializer.Deserialize(
            JsonSerializer.Serialize(result, TransferJson.Default.RubyPreparationResult),
            TransferJson.Default.RubyPreparationResult
        )!;
        Assert.AreEqual(file, Assert.ContainsSingle(transfer.Prepared.Files));
        Assert.AreEqual(result.Prepared.Subject, transfer.Prepared.Subject);
        CollectionAssert.AreEqual(Checks, transfer.Prepared.RequiredChecks);
        CollectionAssert.AreEqual(
            result.Commands.Select(command => command.Check).ToArray(),
            transfer.Commands.Select(command => command.Check).ToArray()
        );
        Assert.HasCount(2, result.Commands.Where(command => command.Check == "consumer"));
        Assert.IsEmpty(result.Commands.Where(command => command.Check == "original-bytes"));
        foreach (ReleaseCommand command in result.Commands)
        {
            Assert.IsTrue(command.Result.Succeeded);
            Assert.IsFalse(command.Command.InheritEnvironment);
            Assert.IsTrue(command.Command.CloseStandardInput);
            var environment = command.Command.Environment!;
            Assert.AreEqual(fixture.Subject.SelectedRef, environment["GITHUB_REF"]);
            Assert.AreEqual(fixture.Subject.Revision, environment["GITHUB_SHA"]);
            Assert.StartsWith(fixture.Scratch, environment["HOME"]!);
            foreach (
                string excluded in new[]
                {
                    "GITHUB_TOKEN",
                    "GH_TOKEN",
                    "GEM_HOST_API_KEY",
                    "BUNDLE_GEMS__RUBYGEMS__ORG",
                    "HTTP_PROXY",
                    "HTTPS_PROXY",
                    "SOURCE_DATE_EPOCH",
                }
            )
                Assert.IsFalse(environment.ContainsKey(excluded), excluded);
            Assert.IsFalse(
                environment.TryGetValue("RUBYOPT", out string? options) && options is not null
            );
            Assert.IsFalse(
                environment.TryGetValue("RUBYLIB", out string? library) && library is not null
            );
        }
        NativeCommand preparation = Assert
            .ContainsSingle(
                result.Commands.Where(command => command.Command.Arguments[0] == "tool")
            )
            .Command;
        Assert.AreEqual(fixture.Tools.Dotnet, preparation.Executable);
        CollectionAssert.AreEqual(
            new[]
            {
                "tool",
                "install",
                "nbgv",
                "--version",
                "3.10.94",
                "--tool-path",
                Path.Combine(fixture.Scratch, "native-tools"),
            },
            preparation.Arguments
        );
        NativeCommand build = Assert
            .ContainsSingle(
                result.Commands.Where(command => command.Command.Arguments.Contains("build"))
            )
            .Command;
        CollectionAssert.AreEqual(
            new[]
            {
                fixture.Tools.Gem,
                "build",
                "--norc",
                Path.Combine(fixture.Repo.Directory, Fixture.Entry),
                "--output",
                Path.Combine(fixture.Scratch, "package", "original.gem"),
            },
            build.Arguments
        );
        NativeCommand install = Assert
            .ContainsSingle(
                result.Commands.Where(command =>
                    command.Command.Arguments.Contains("install")
                    && command.Command.Executable == fixture.Tools.Ruby
                )
            )
            .Command;
        CollectionAssert.AreEqual(
            new[]
            {
                fixture.Tools.Gem,
                "install",
                "--norc",
                Path.Combine(fixture.Scratch, "package", "original.gem"),
                "--local",
                "--no-document",
            },
            install.Arguments
        );
        NativeCommand consumer = result.Commands[^1].Command;
        Assert.StartsWith(fixture.Scratch, consumer.Directory);
        Assert.AreEqual(consumer.Environment!["GEM_HOME"], consumer.Environment["GEM_PATH"]);
        Assert.AreEqual(consumer.Environment["GEM_HOME"], consumer.Arguments[3]);
        Assert.Contains("Gem.loaded_specs", consumer.Arguments[1]);
        Assert.Contains("spec.platform.to_s == 'ruby'", consumer.Arguments[1]);
        Assert.Contains("File.realpath(spec.full_gem_path)", consumer.Arguments[1]);
        Assert.Contains("loaded.length == 2", consumer.Arguments[1]);
        Assert.DoesNotContain(fixture.Repo.Directory, consumer.Arguments);
        Assert.IsEmpty(Directory.GetFiles(Path.Combine(fixture.Scratch, "queries")));
    }

    [TestMethod]
    [DataRow("buddy", "github-packages-ruby", "refs/heads/topic", false, true)]
    [DataRow("official", "rubygems", "refs/heads/main", false, true)]
    [DataRow("official", "rubygems", "refs/heads/topic", true, true)]
    [DataRow("buddy", "github-packages-ruby", "refs/tags/preview", true, true)]
    [DataRow("official", "rubygems", "refs/tags/preview", true, true)]
    [DataRow("buddy", "github-packages-ruby", "refs/tags/preview", false, false)]
    [DataRow("buddy", "github-packages-ruby", "refs/heads/", false, false)]
    [DataRow("official", "rubygems", "refs/heads/topic", false, false)]
    [DataRow("buddy", "rubygems", "refs/heads/main", false, false)]
    [DataRow("official", "github-packages-ruby", "refs/heads/main", true, false)]
    [DataRow("buddy", "github-packages-ruby", "", true, false)]
    [DataRow("unknown", "rubygems", "refs/heads/main", true, false)]
    public async Task ChannelRefAndDryRunRemainIndependent(
        string channel,
        string destination,
        string selectedRef,
        bool dryRun,
        bool accepted
    )
    {
        using var fixture = SharedLane();
        fixture.Subject = fixture.Subject with
        {
            Channel = channel,
            Destination = destination,
            SelectedRef = selectedRef,
            DryRun = dryRun,
        };
        RubyPreparationResult result = await fixture.RunAsync(context.CancellationToken);
        Assert.AreEqual(fixture.Subject, result.Prepared.Subject);
        if (accepted)
        {
            Assert.IsEmpty(result.Errors, string.Join("; ", result.Errors));
            Assert.AreEqual("1.2.3", Assert.ContainsSingle(result.Prepared.Files).Version);
            Assert.IsTrue(
                result.Prepared.Checks.All(check => check.Status == ReleaseCheckStatus.Passed)
            );
        }
        else
        {
            RequireStopped(result, "native-input", ReleaseCheckStatus.Failed);
            Assert.IsEmpty(result.Commands);
        }
    }

    [TestMethod]
    [DataRow("declaration")]
    [DataRow("build")]
    [DataRow("extra-build")]
    [DataRow("outputs")]
    [DataRow("extra-output")]
    [DataRow("quality")]
    [DataRow("missing-quality")]
    [DataRow("entry")]
    [DataRow("entry-name")]
    [DataRow("unit")]
    public async Task UnsupportedCommittedRecipeStopsBeforeNativeWork(string defect)
    {
        using var fixture = Fixture.Shared(shared!, sharedRevision, "recipes/" + defect, defect);
        RubyPreparationResult result = await fixture.RunAsync(context.CancellationToken);
        RequireStopped(result, "native-input", ReleaseCheckStatus.Failed);
        Assert.IsEmpty(result.Commands);
    }

    [TestMethod]
    public async Task RelocatedRecipeControlQualifies()
    {
        using var fixture = Fixture.Shared(shared!, sharedRevision, "recipes/control", "");
        RubyPreparationResult result = await fixture.RunAsync(context.CancellationToken);
        Assert.IsEmpty(result.Errors, string.Join("; ", result.Errors));
        Assert.IsTrue(
            result.Prepared.Checks.All(check => check.Status == ReleaseCheckStatus.Passed)
        );
    }

    [TestMethod]
    [DataRow("commit")]
    [DataRow("missing-commit")]
    [DataRow("missing-version")]
    [DataRow("missing-public-release")]
    [DataRow("public-release")]
    [DataRow("ruby")]
    [DataRow("rubygems")]
    [DataRow("name")]
    [DataRow("version")]
    [DataRow("platform")]
    [DataRow("source-path")]
    [DataRow("projection-raw")]
    public async Task NativeIdentityCannotQualifyAnotherRequest(string defect)
    {
        using var fixture = SharedLane();
        fixture.Subject = fixture.Subject with
        {
            Channel = "official",
            Destination = "rubygems",
            SelectedRef = "refs/heads/main",
        };
        fixture.Defect = defect;
        RubyPreparationResult result = await fixture.RunAsync(context.CancellationToken);
        RequireStopped(result, "native-input", ReleaseCheckStatus.Failed);
        Assert.DoesNotContain("build", result.Commands.Select(command => command.Check).ToArray());
    }

    [TestMethod]
    public async Task DryRefPreservesNativeProjectionWhenPublicReleaseIsFalse()
    {
        using var fixture = await Fixture.CreateAsync(context.CancellationToken);
        fixture.Subject = fixture.Subject with
        {
            Channel = "official",
            Destination = "rubygems",
            SelectedRef = "refs/tags/preview",
            DryRun = true,
        };
        fixture.Defect = "public-release";
        RubyPreparationResult result = await fixture.RunAsync(context.CancellationToken);
        Assert.IsEmpty(result.Errors, string.Join("; ", result.Errors));
        Assert.AreEqual("false", result.Native["public-release"]);
        Assert.AreEqual("1.2.3", Assert.ContainsSingle(result.Prepared.Files).Version);
    }

    [TestMethod]
    [DataRow("history")]
    [DataRow("sdk")]
    [DataRow("missing-nbgv")]
    [DataRow("missing-tool")]
    [DataRow("manifest")]
    [DataRow("global")]
    [DataRow("helper")]
    public async Task MissingHistoryOrPreparedToolsCannotBuild(string defect)
    {
        using var fixture = await Fixture.CreateAsync(context.CancellationToken, defect);
        fixture.Defect = defect;
        RubyPreparationResult result = await fixture.RunAsync(context.CancellationToken);
        RequireStopped(result, "native-input", ReleaseCheckStatus.Failed);
        Assert.DoesNotContain("build", result.Commands.Select(command => command.Check).ToArray());
        if (defect == "missing-tool")
            Assert.AreEqual(NativeTermination.StartFailed, result.Commands[^1].Result.Termination);
    }

    [TestMethod]
    [DataRow("name")]
    [DataRow("version")]
    [DataRow("platform")]
    [DataRow("dependencies")]
    [DataRow("extensions")]
    [DataRow("missing-extensions")]
    [DataRow("repository")]
    [DataRow("missing-metadata")]
    [DataRow("require-paths")]
    [DataRow("missing-content")]
    [DataRow("extra-content")]
    [DataRow("witness")]
    [DataRow("archive-path")]
    public async Task UnsupportedArchiveCannotReachConsumer(string defect)
    {
        using var fixture = SharedLane();
        fixture.ArchiveDefect = defect;
        RubyPreparationResult result = await fixture.RunAsync(context.CancellationToken);
        RequireStopped(result, "contents", ReleaseCheckStatus.Failed);
        Assert.DoesNotContain(
            "consumer",
            result.Commands.Select(command => command.Check).ToArray()
        );
        Assert.IsTrue(result.Commands.All(command => command.Result.Succeeded));
        Assert.AreEqual("contents", result.Commands[^1].Check);
    }

    [TestMethod]
    [DataRow("native-input", "Exited", "Failed")]
    [DataRow("native-input", "TimedOut", "TimedOut")]
    [DataRow("native-input", "StartFailed", "Failed")]
    [DataRow("native-input", "Cancelled", "Cancelled")]
    [DataRow("build", "Exited", "Failed")]
    [DataRow("build", "StartFailed", "Failed")]
    [DataRow("build", "TimedOut", "TimedOut")]
    [DataRow("build", "Cancelled", "Cancelled")]
    [DataRow("contents", "Exited", "Failed")]
    [DataRow("contents", "InvalidOutput", "Failed")]
    [DataRow("contents", "TimedOut", "TimedOut")]
    [DataRow("contents", "Cancelled", "Cancelled")]
    [DataRow("consumer", "Exited", "Failed")]
    [DataRow("consumer", "StartFailed", "Failed")]
    [DataRow("consumer", "TimedOut", "TimedOut")]
    [DataRow("consumer", "Cancelled", "Cancelled")]
    public async Task FailedNativePhaseRetainsReceiptAndSkipsDependents(
        string phase,
        string terminationText,
        string statusText
    )
    {
        using var fixture = SharedLane();
        NativeTermination termination = Enum.Parse<NativeTermination>(terminationText);
        fixture.Override = command =>
            fixture.Phase(command) == phase
                ? new(
                    termination,
                    termination == NativeTermination.Exited ? 7 : null,
                    "partial native output",
                    "native diagnostic",
                    0.25,
                    null
                )
                : null;
        RubyPreparationResult result = await fixture.RunAsync(context.CancellationToken);
        RequireStopped(result, phase, Enum.Parse<ReleaseCheckStatus>(statusText));
        ReleaseCommand failed = Assert.ContainsSingle(
            result.Commands.Where(command => !command.Result.Succeeded)
        );
        Assert.AreEqual(failed, result.Commands[^1]);
        Assert.AreEqual(phase, failed.Check);
        Assert.AreEqual(termination, failed.Result.Termination);
        Assert.AreEqual("partial native output", failed.Result.Stdout);
        Assert.AreEqual("native diagnostic", failed.Result.Stderr);
        Assert.AreEqual(0.25, failed.Result.DurationSeconds);
    }

    [TestMethod]
    public async Task PreparedTransferPreservesFailureAndNativeCommandReceipt()
    {
        using var fixture = await Fixture.CreateAsync(context.CancellationToken);
        fixture.Override = command =>
            fixture.Phase(command) == "build"
                ? new(NativeTermination.TimedOut, null, "partial", "diagnostic", 1.5, "deadline")
                : null;
        RubyPreparationResult result = await fixture.RunAsync(context.CancellationToken);
        RubyPreparationResult transfer = JsonSerializer.Deserialize(
            JsonSerializer.Serialize(result, TransferJson.Default.RubyPreparationResult),
            TransferJson.Default.RubyPreparationResult
        )!;
        RequireStopped(transfer, "build", ReleaseCheckStatus.TimedOut);
        ReleaseCommand command = transfer.Commands[^1];
        Assert.AreEqual("build", command.Check);
        Assert.AreEqual(NativeTermination.TimedOut, command.Result.Termination);
        Assert.IsNull(command.Result.ExitCode);
        Assert.AreEqual("partial", command.Result.Stdout);
        Assert.AreEqual("diagnostic", command.Result.Stderr);
        Assert.AreEqual("deadline", command.Result.Error);
        Assert.AreEqual(1.5, command.Result.DurationSeconds);
        Assert.IsTrue(command.Command.CloseStandardInput);
        Assert.IsFalse(command.Command.InheritEnvironment);
        Assert.AreEqual(fixture.Subject.SelectedRef, command.Command.Environment!["GITHUB_REF"]);
    }

    [TestMethod]
    public async Task BuddyBranchDoesNotRequireNativePublicRelease()
    {
        using var fixture = await Fixture.CreateAsync(context.CancellationToken);
        fixture.Defect = "public-release";
        RubyPreparationResult result = await fixture.RunAsync(context.CancellationToken);
        Assert.IsEmpty(result.Errors, string.Join("; ", result.Errors));
        Assert.AreEqual("false", result.Native["public-release"]);
        Assert.AreEqual("1.2.3", Assert.ContainsSingle(result.Prepared.Files).Version);
    }

    [TestMethod]
    [DataRow("missing-output")]
    [DataRow("missing-answer")]
    [DataRow("consumer-identity")]
    [DataRow("consumer-origin")]
    public async Task MissingOutputOrFailedInstalledIdentityCannotQualify(string defect)
    {
        using var fixture = await Fixture.CreateAsync(context.CancellationToken);
        fixture.Defect = defect;
        RubyPreparationResult result = await fixture.RunAsync(context.CancellationToken);
        string phase =
            defect == "missing-output" ? "build"
            : defect == "missing-answer" ? "contents"
            : "consumer";
        RequireStopped(result, phase, ReleaseCheckStatus.Failed);
        if (defect.StartsWith("consumer", StringComparison.Ordinal))
            Assert.Contains(defect, result.Commands[^1].Result.Stderr);
    }

    [TestMethod]
    [DataRow("build", "size")]
    [DataRow("contents", "digest")]
    [DataRow("consumer", "digest")]
    [DataRow("consumer", "missing")]
    public async Task ChangedOriginalCannotQualifyRelease(string phase, string change)
    {
        using var fixture = await Fixture.CreateAsync(context.CancellationToken);
        fixture.Override = command =>
        {
            if (
                fixture.Phase(command) == phase
                && (phase != "build" || command.Arguments[0] == fixture.Helper)
            )
            {
                string path = Path.Combine(fixture.Scratch, "package", "original.gem");
                if (change == "missing")
                    File.Delete(path);
                else
                    File.WriteAllText(path, change == "size" ? "short" : "replaced");
            }
            return null;
        };
        RubyPreparationResult result = await fixture.RunAsync(context.CancellationToken);
        RequireStopped(result, phase, ReleaseCheckStatus.Failed);
        Assert.Contains("Original gem changed", string.Join(";", result.Errors));
        Assert.IsTrue(result.Commands.All(command => command.Result.Succeeded));
    }

    [TestMethod]
    public async Task AbbreviatedRevisionCannotSupplyExactBinding()
    {
        using var fixture = await Fixture.CreateAsync(context.CancellationToken);
        fixture.Subject = fixture.Subject with { Revision = fixture.Subject.Revision[..12] };
        RubyPreparationResult result = await fixture.RunAsync(context.CancellationToken);
        RequireStopped(result, "native-input", ReleaseCheckStatus.Failed);
        Assert.IsEmpty(result.Commands);
    }

    [TestMethod]
    public async Task CancellationBeforeWorkRetainsCompleteObligations()
    {
        using var fixture = await Fixture.CreateAsync(context.CancellationToken);
        using var cancelled = new CancellationTokenSource();
        await cancelled.CancelAsync();
        RubyPreparationResult result = await fixture.RunAsync(cancelled.Token);
        RequireStopped(result, "native-input", ReleaseCheckStatus.Cancelled);
        Assert.IsEmpty(result.Commands);
    }

    [TestMethod]
    [DataRow("relative-checkout")]
    [DataRow("relative-tool")]
    [DataRow("nested-scratch")]
    [DataRow("nonempty-scratch")]
    public async Task UnownedOrUnpreparedPathsFailBeforeCommands(string defect)
    {
        using var fixture = await Fixture.CreateAsync(context.CancellationToken);
        RubyPreparationRequest request = fixture.Request;
        if (defect == "relative-checkout")
            request = request with { Checkout = "relative" };
        if (defect == "relative-tool")
            request = request with { Tools = request.Tools with { Gem = "gem" } };
        if (defect == "nested-scratch")
            request = request with { Scratch = fixture.Repo.Directory };
        if (defect == "nonempty-scratch")
            await File.WriteAllTextAsync(
                Path.Combine(fixture.Scratch, "unowned"),
                "keep",
                context.CancellationToken
            );
        await Assert.ThrowsExactlyAsync<InvalidDataException>(() =>
            RubyPreparation.PrepareAsync(
                request,
                (_, _) =>
                {
                    Assert.Fail("Invalid owned state cannot execute native commands.");
                    return Task.FromResult(Fixture.Success(""));
                },
                context.CancellationToken
            )
        );
    }

    private static void RequireStopped(
        RubyPreparationResult result,
        string phase,
        ReleaseCheckStatus status
    )
    {
        CollectionAssert.AreEqual(Checks, result.Prepared.RequiredChecks);
        CollectionAssert.AreEqual(
            Checks,
            result.Prepared.Checks.Select(check => check.Id).ToArray()
        );
        int index = Array.IndexOf(Checks, phase);
        for (int position = 0; position < Checks.Length; position++)
            Assert.AreEqual(
                position < index ? ReleaseCheckStatus.Passed
                    : position == index ? status
                    : ReleaseCheckStatus.Skipped,
                result.Prepared.Checks[position].Status,
                Checks[position]
            );
        Assert.IsEmpty(result.Prepared.Files);
        Assert.IsNotEmpty(result.Errors);
    }

    private sealed class Fixture(
        GitFixture repo,
        ReleaseSubject subject,
        bool ownsRepository = true
    ) : IDisposable
    {
        internal const string Entry = "product/hcoona-release-smoke-ruby.gemspec";
        private const string HelperPath = "src/private/app/workflow-delivery/Native/Ruby/facts.rb";
        private static readonly string[] Files =
        [
            "LICENSE",
            "README.md",
            "lib/hcoona_release_smoke_ruby.rb",
            "lib/hcoona_release_smoke_ruby/version.rb",
        ];
        private int archiveQueries;
        internal GitFixture Repo { get; } = repo;
        internal ReleaseSubject Subject { get; set; } = subject;
        internal string Scratch { get; } =
            Directory.CreateTempSubdirectory("ruby-release-").FullName;
        internal string Helper => Path.Combine(Repo.Directory, HelperPath);
        internal RubyPreparationTools Tools =>
            new(
                Path.Combine(Repo.Directory, "ruby"),
                Path.Combine(Repo.Directory, "gem"),
                Path.Combine(Repo.Directory, "dotnet")
            );
        internal RubyPreparationRequest Request => new(Subject, Repo.Directory, Scratch, Tools);
        internal string Defect { get; set; } = "";
        internal string ArchiveDefect { get; set; } = "";
        internal Func<NativeCommand, NativeCommandResult?>? Override { get; set; }

        internal static async Task<Fixture> CreateAsync(CancellationToken token, string defect = "")
        {
            GitFixture repo = await GitFixture.CreateAsync(token);
            try
            {
                await StageRootAsync(repo, defect);
                await StageProductAsync(repo, "product", defect);
                string revision = await repo.CommitAsync();
                await repo.GitAsync("reset", "--hard", revision);
                return new(repo, SubjectFor(revision, "product", defect));
            }
            catch
            {
                repo.Dispose();
                throw;
            }
        }

        // Reads a class-owned committed repository; the row owns only its scratch.
        internal static Fixture Shared(
            GitFixture repo,
            string revision,
            string directory = "product",
            string defect = ""
        ) => new(repo, SubjectFor(revision, directory, defect), ownsRepository: false);

        private static ReleaseSubject SubjectFor(
            string revision,
            string directory,
            string defect
        ) =>
            new(
                revision,
                "refs/heads/topic",
                defect == "unit" ? "another" : "product",
                directory + "/workflow-delivery.release-unit.yml",
                "buddy",
                "github-packages-ruby",
                false
            );

        internal static async Task StageRootAsync(GitFixture repo, string defect)
        {
            if (defect != "helper")
                await repo.SetAsync(HelperPath);
            if (defect != "manifest")
                await repo.SetAsync(
                    ".config/dotnet-tools.json",
                    """{"tools":{"nbgv":{"version":"3.10.94"}}}"""
                );
            if (defect != "global")
                await repo.SetAsync(
                    "global.json",
                    """{"sdk":{"version":"10.0.401","rollForward":"disable"}}"""
                );
        }

        // Stages one committed recipe directory; recipe defects are directory-local.
        internal static async Task StageProductAsync(
            GitFixture repo,
            string directory,
            string defect
        )
        {
            if (defect != "entry")
                await repo.SetAsync(directory + "/hcoona-release-smoke-ruby.gemspec");
            if (defect != "missing-quality")
                await repo.SetAsync(
                    directory + "/workflow-delivery.quality.yml",
                    """
                    schema: workflow-delivery/v3/quality-selection
                    ecosystems:
                      ruby:
                        preset: ruby/hcoona-release-smoke-ruby-v1
                    """.Replace(
                        "ruby/hcoona-release-smoke-ruby-v1",
                        defect == "quality"
                            ? "ruby/unknown-v1"
                            : "ruby/hcoona-release-smoke-ruby-v1",
                        StringComparison.Ordinal
                    )
                );
            string text = """
                schema: workflow-delivery/v3/release-unit
                release-unit: product
                builds:
                  - id: gem-build
                    definition: ruby/gem-v1
                    entry-point: hcoona-release-smoke-ruby.gemspec
                    outputs:
                      - id: gem-output
                        role: primary-package
                        kind: ruby-gem
                """;
            text = defect switch
            {
                "build" => text.Replace("ruby/gem-v1", "ruby/unknown-v1", StringComparison.Ordinal),
                "outputs" => text.Replace("ruby-gem", "unknown-kind", StringComparison.Ordinal),
                "entry-name" => text.Replace(
                    "hcoona-release-smoke-ruby.gemspec",
                    "other.gemspec",
                    StringComparison.Ordinal
                ),
                "extra-output" => text
                    + "\n      - id: extra\n        role: source-package"
                    + "\n        kind: ruby-gem\n",
                "extra-build" => text
                    + "\n  - id: extra\n    definition: ruby/gem-v1"
                    + "\n    entry-point: hcoona-release-smoke-ruby.gemspec\n    outputs:"
                    + "\n      - id: extra\n        role: primary-package"
                    + "\n        kind: ruby-gem\n",
                _ => text,
            };
            if (defect != "declaration")
                await repo.SetAsync(directory + "/workflow-delivery.release-unit.yml", text);
        }

        internal Task<RubyPreparationResult> RunAsync(CancellationToken token) =>
            RubyPreparation.PrepareAsync(Request, ExecuteAsync, token);

        internal string Phase(NativeCommand command)
        {
            string[] args = command.Arguments;
            if (args.Contains("build"))
                return "build";
            if (args.Contains("install") && command.Executable == Tools.Ruby || args[0] == "-e")
                return "consumer";
            if (
                args[0] == Helper
                && File.ReadAllText(args[^1]).Contains("\"archive\"", StringComparison.Ordinal)
            )
                return archiveQueries == 0 ? "build" : "contents";
            return "native-input";
        }

        private async Task<NativeCommandResult> ExecuteAsync(
            NativeCommand command,
            CancellationToken token
        )
        {
            if (Override?.Invoke(command) is { } result)
                return result;
            string[] args = command.Arguments;
            if (args.Contains("--is-shallow-repository"))
                return Success(Defect == "history" ? "true" : "false");
            if (args[0] == "--version")
                return Defect == "missing-tool"
                    ? new(NativeTermination.StartFailed, null, "", "", 0, "missing prepared dotnet")
                    : Success(Defect == "sdk" ? "10.0.400" : "10.0.401");
            if (args[0] == "tool")
            {
                if (Defect != "missing-nbgv")
                    await File.WriteAllTextAsync(
                        Path.Combine(args[^1], OperatingSystem.IsWindows() ? "nbgv.exe" : "nbgv"),
                        "tool",
                        token
                    );
                return Success("");
            }
            if (args[0] == "-r")
                return Success(
                    JsonSerializer.Serialize(
                        new
                        {
                            ruby = Defect == "ruby" ? "3.4.0" : "4.0.7",
                            rubygems = Defect == "rubygems" ? "" : "4.0.7",
                        }
                    )
                );
            if (args[0] == "get-version")
            {
                var value = new JsonObject
                {
                    ["SemVer2"] = "1.2.3+g1234567",
                    ["GitCommitId"] = Defect == "commit" ? new string('0', 40) : Subject.Revision,
                    ["PublicRelease"] = Defect != "public-release",
                };
                if (Defect == "missing-version")
                    value.Remove("SemVer2");
                if (Defect == "missing-commit")
                    value.Remove("GitCommitId");
                if (Defect == "missing-public-release")
                    value.Remove("PublicRelease");
                return Success(value.ToJsonString());
            }
            if (args[0] == Tools.Gem)
            {
                Assert.AreEqual("--norc", args[2]);
                if (args[1] == "build" && Defect != "missing-output")
                    await File.WriteAllTextAsync(args[^1], "original", token);
                return Success("");
            }
            if (args[0] == "-e")
                return Defect.StartsWith("consumer", StringComparison.Ordinal)
                    ? Success("") with
                    {
                        ExitCode = 2,
                        Stderr = Defect,
                    }
                    : Success("");
            Assert.AreEqual(Helper, args[0]);
            using JsonDocument input = JsonDocument.Parse(
                await File.ReadAllTextAsync(args[^1], token)
            );
            string operation = input.RootElement.GetProperty("operation").GetString()!;
            string path = input.RootElement.GetProperty("path").GetString()!;
            if (operation == "version")
            {
                string raw = input.RootElement.GetProperty("raw").GetString()!;
                return Success(
                    JsonSerializer.Serialize(
                        new
                        {
                            gemspec = path,
                            raw = Defect == "projection-raw" ? "other" : raw,
                            version = "1.2.3",
                            literal = "\"1.2.3\"",
                        }
                    )
                );
            }
            var gem = new JsonObject
            {
                ["name"] = "hcoona-release-smoke-ruby",
                ["version"] = "1.2.3",
                ["platform"] = "ruby",
                ["require_paths"] = new JsonArray("lib"),
                ["files"] = JsonSerializer.SerializeToNode(Files),
                ["runtime_dependencies"] = new JsonArray(),
                ["extensions"] = new JsonArray(),
                ["metadata"] = new JsonObject
                {
                    ["github_repo"] = "https://github.com/hcoona/three",
                },
            };
            if (operation == "gemspec")
            {
                gem["gemspec"] = Defect == "source-path" ? Path.Combine(Scratch, "other") : path;
                if (Defect is "name" or "version" or "platform")
                    gem[Defect] = "other";
            }
            else
            {
                Assert.AreEqual("archive", operation);
                archiveQueries++;
                gem["archive"] = path;
                gem["contents"] = JsonSerializer.SerializeToNode(Files);
                if (archiveQueries > 1)
                {
                    if (Defect == "missing-answer")
                        return Success("{}");
                    switch (ArchiveDefect)
                    {
                        case "name":
                        case "version":
                        case "platform":
                            gem[ArchiveDefect] = "other";
                            break;
                        case "dependencies":
                            gem["runtime_dependencies"] = new JsonArray(
                                new JsonObject { ["name"] = "dependency", ["requirement"] = ">= 0" }
                            );
                            break;
                        case "extensions":
                            gem["extensions"] = new JsonArray("ext/extconf.rb");
                            break;
                        case "missing-extensions":
                            gem.Remove("extensions");
                            break;
                        case "repository":
                            gem["metadata"]!["github_repo"] = "https://other";
                            break;
                        case "missing-metadata":
                            gem.Remove("metadata");
                            break;
                        case "require-paths":
                            gem["require_paths"] = new JsonArray("other");
                            break;
                        case "missing-content":
                            gem["contents"]!.AsArray().RemoveAt(0);
                            break;
                        case "extra-content":
                            gem["contents"]!.AsArray().Add("extra.rb");
                            break;
                        case "witness":
                            gem["contents"]!
                                .AsArray()
                                .Add(
                                    "lib/hcoona_release_smoke_ruby/"
                                        + "_workflow_delivery_provenance.json"
                                );
                            break;
                        case "archive-path":
                            gem["archive"] = Path.Combine(Scratch, "other.gem");
                            break;
                    }
                }
            }
            return Success(gem.ToJsonString());
        }

        internal static NativeCommandResult Success(string stdout) =>
            new(NativeTermination.Exited, 0, stdout, "", 0.1, null);

        public void Dispose()
        {
            if (ownsRepository)
                Repo.Dispose();
            Directory.Delete(Scratch, recursive: true);
        }
    }
}
