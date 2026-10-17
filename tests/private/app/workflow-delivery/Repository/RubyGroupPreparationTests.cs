using System.Text.Json;
using WorkflowDelivery.Platform;
using WorkflowDelivery.Repository;
using GitFixture = WorkflowDelivery.Tests.Repository.GitReaderTests.GitFixture;

namespace WorkflowDelivery.Tests.Repository;

[TestClass]
public sealed class RubyGroupPreparationTests(TestContext context)
{
    private static readonly string[] RequirePaths = ["lib"];
    private static NativeCommandResult Answer(object value) => new(NativeTermination.Exited,
        0, JsonSerializer.Serialize(value), "", 0, null);

    // Rows over the bundle-only commit read one class-owned repository: it has no gem source,
    // so preparation writes nothing into the checkout. Rows that add a gem source or a
    // committed defect keep a private repository. Each row owns its scope and scratch.
    private static GitFixture? shared;
    private static string sharedRevision = "";

    [ClassInitialize]
    public static async Task InitializeAsync(TestContext classContext)
    {
        GitFixture repo = await GitFixture.CreateAsync(classContext.CancellationToken);
        try
        {
            sharedRevision = await CommitAsync(repo, true);
            shared = repo;
        }
        catch { repo.Dispose(); throw; }
    }

    [ClassCleanup]
    public static void Cleanup() => shared?.Dispose();

    private static async Task<string> CommitAsync(GitFixture repo, bool manifest)
    {
        await repo.SetAsync("node/Gemfile");
        await repo.SetAsync("node/Gemfile.lock");
        if (manifest)
            await repo.SetAsync(".config/dotnet-tools.json",
                "{\"tools\":{\"nbgv\":{\"version\":\"3.10.94\"}}}");
        string revision = await repo.CommitAsync();
        await repo.GitAsync("reset", "--hard", revision);
        return revision;
    }

    private static async Task<RubyGroupPreparationRequest> RequestAsync(GitFixture repo,
        CancellationToken token, bool manifest = true) => await RequestAsync(repo,
            await CommitAsync(repo, manifest), Path.GetDirectoryName(repo.Directory)!, token);

    // Reads the class-owned commit; the row owns only its state directory.
    private sealed class SharedState : IDisposable
    {
        internal string Directory { get; } =
            System.IO.Directory.CreateTempSubdirectory("workflow-ruby-preparation-").FullName;
        internal Task<RubyGroupPreparationRequest> RequestAsync(CancellationToken token) =>
            RubyGroupPreparationTests.RequestAsync(shared!, sharedRevision, Directory, token);
        public void Dispose() => System.IO.Directory.Delete(Directory, recursive: true);
    }

    private static async Task<RubyGroupPreparationRequest> RequestAsync(GitFixture repo,
        string revision, string parent, CancellationToken token)
    {
        string scope = Path.Combine(parent, "scope.json");
        await File.WriteAllTextAsync(scope, JsonSerializer.Serialize(new
        {
            @base = revision, candidate = revision, full = true,
            changed_paths = Array.Empty<string>(),
            endpoint_owners = new { basis = new { revision, paths = Array.Empty<object>() },
                candidate = new { revision, paths = Array.Empty<object>() } }
        }), token);
        string scratch = Directory.CreateDirectory(Path.Combine(parent, "preparation")).FullName;
        string Tool(string name) => Path.Combine(parent, "tools", name);
        return new(new(repo.Directory, revision), new(repo.Directory, revision), true, scope,
            Tool("facts.rb"), scratch, new(Tool("ruby"), Tool("gem"), Tool("bundle"),
                Tool("dotnet")), "2.7.2", 30);
    }

    private static async Task<NativeCommandResult> NativeAsync(RubyGroupPreparationRequest request,
        NativeCommand command, CancellationToken token)
    {
        Assert.IsFalse(command.InheritEnvironment);
        Assert.StartsWith(request.Scratch, command.Environment!["HOME"]!);
        Assert.AreEqual(command.Environment["HOME"], command.Environment["DOTNET_CLI_HOME"]);
        Assert.AreEqual(Path.GetDirectoryName(request.Tools.Dotnet),
            command.Environment["DOTNET_ROOT"]);
        if (command.Executable == request.Tools.Dotnet)
        {
            Assert.Contains("3.10.94", command.Arguments);
            string executable = Path.Combine(command.Arguments[^1],
                OperatingSystem.IsWindows() ? "nbgv.exe" : "nbgv");
            await File.WriteAllTextAsync(executable, "controlled native tool", token);
            return Answer(new { });
        }
        if (Path.GetFileName(command.Executable) is "nbgv" or "nbgv.exe")
            return Answer(new { SemVer2 = "1.2.3-beta.4" });
        Assert.AreEqual(request.Tools.Ruby, command.Executable);
        Assert.StartsWith(request.Scratch, command.Environment["GEM_HOME"]!);
        Assert.AreEqual(command.Environment["GEM_HOME"], command.Environment["GEM_PATH"]);
        if (command.Arguments[0] == request.Tools.Gem)
        {
            Assert.AreEqual("bundler", command.Arguments[2]);
            return Answer(new { });
        }
        if (command.Arguments[0] == request.Tools.Bundle)
        {
            Assert.AreEqual("_2.4.20_", command.Arguments[1]);
            Assert.AreEqual("1", command.Environment["BUNDLE_FROZEN"]);
            Assert.AreEqual(Path.GetFullPath(Path.Combine(request.Candidate.Directory,
                "node/Gemfile")), Path.GetFullPath(command.Environment["BUNDLE_GEMFILE"]!));
            return Answer(new { });
        }
        using JsonDocument input = JsonDocument.Parse(
            await File.ReadAllTextAsync(command.Arguments[^1], token));
        string operation = input.RootElement.GetProperty("operation").GetString()!;
        if (operation is "gemspec" or "version")
        {
            string gemspec = input.RootElement.GetProperty("path").GetString()!;
            if (operation == "version")
                return Answer(new { gemspec, raw = "1.2.3-beta.4",
                    version = "1.2.3.pre.beta.4", literal = "\"1.2.3.pre.beta.4\"" });
            return Answer(new { gemspec, name = "hcoona-release-smoke-ruby",
                version = "1.2.3.pre.beta.4", platform = "ruby", require_paths = RequirePaths,
                files = Array.Empty<string>(), runtime_dependencies = Array.Empty<object>() });
        }
        Assert.AreEqual("bundle-context", operation);
        string gemfile = Path.Combine(request.Candidate.Directory, "node/Gemfile");
        return Answer(new { gemfile, lockfile = gemfile + ".lock", bundler_version = "2.7.2",
            locked_bundler_version = "2.4.20" });
    }

    [TestMethod]
    public async Task NativePreparationUsesOriginalPinAndOwnedEndpointContexts()
    {
        // Defect-free control for the shared commit.
        using var state = new SharedState();
        RubyGroupPreparationRequest request = await state.RequestAsync(context.CancellationToken);
        RubyGroupPreparationResult result = await RubyGroupPreparation.PrepareAsync(request,
            (command, token) => NativeAsync(request, command, token), context.CancellationToken);
        Assert.AreEqual(request.Candidate.Reference, result.Request.Candidate.Checkout.Reference);
        Assert.AreEqual(request.ScopePath, result.Request.ScopePath);
        Assert.IsTrue(result.Request.Full);
        RubyFactTools before = result.Request.Basis.Collection.Tools;
        RubyFactTools after = result.Request.Candidate.Collection.Tools;
        Assert.AreNotEqual(before.Environment["GEM_HOME"], after.Environment["GEM_HOME"]);
        Assert.AreNotEqual(before.Scratch, after.Scratch);
        Assert.AreEqual(result.Request.Basis.Collection.Nbgv,
            result.Request.Candidate.Collection.Nbgv);
        Assert.IsTrue(File.Exists(result.Request.Candidate.Collection.Nbgv));
        Assert.IsTrue(result.Commands.All(command => command.Result.Succeeded));
    }

    [TestMethod]
    public async Task GeneratedSourceIsAvailableBeforeFrozenBundlerEvaluation()
    {
        using var repo = await GitFixture.CreateAsync(context.CancellationToken);
        const string source = "src/hcoona-release-smoke-ruby";
        await repo.SetAsync(source + "/hcoona-release-smoke-ruby.gemspec", "original source");
        await repo.SetAsync(".gitignore", "**/version.rb\n");
        RubyGroupPreparationRequest request = await RequestAsync(repo, context.CancellationToken);
        var preparedHomes = new HashSet<string>(StringComparer.Ordinal);
        await RubyGroupPreparation.PrepareAsync(request, async (command, token) =>
        {
            bool readsBundle = command.Arguments[0] == request.Tools.Bundle;
            if (command.Arguments[0] == "-e")
            {
                using JsonDocument input = JsonDocument.Parse(
                    await File.ReadAllTextAsync(command.Arguments[^1], token));
                readsBundle = input.RootElement.GetProperty("operation").GetString() ==
                    "bundle-context";
            }
            if (readsBundle)
            {
                string generated = Path.Combine(request.Candidate.Directory, source,
                    "lib/hcoona_release_smoke_ruby/version.rb");
                Assert.Contains("VERSION = \"1.2.3.pre.beta.4\"",
                    await File.ReadAllTextAsync(generated, token));
                preparedHomes.Add(command.Environment!["HOME"]!);
            }
            return await NativeAsync(request, command, token);
        }, context.CancellationToken);
        Assert.HasCount(2, preparedHomes);
        Assert.AreEqual("original source", await File.ReadAllTextAsync(
            Path.Combine(repo.Directory, source, "hcoona-release-smoke-ruby.gemspec"),
            context.CancellationToken));
    }

    [TestMethod]
    public async Task MissingManifestBlocksNativePreparation()
    {
        using var repo = await GitFixture.CreateAsync(context.CancellationToken);
        RubyGroupPreparationRequest request = await RequestAsync(repo, context.CancellationToken,
            false);
        await Assert.ThrowsExactlyAsync<InvalidDataException>(() =>
            RubyGroupPreparation.PrepareAsync(request, (_, _) =>
            {
                Assert.Fail("Missing native pin must stop before tool installation.");
                return Task.FromResult(Answer(new { }));
            }, context.CancellationToken));
        Assert.IsEmpty(Directory.GetFileSystemEntries(request.Scratch));
    }

    [TestMethod]
    public async Task FailedNativePreparationPreservesDiagnostics()
    {
        using var state = new SharedState();
        RubyGroupPreparationRequest request = await state.RequestAsync(context.CancellationToken);
        RubyBundlePreparationException error = await Assert.ThrowsExactlyAsync<
            RubyBundlePreparationException>(() => RubyGroupPreparation.PrepareAsync(request,
                (_, _) => Task.FromResult(Answer(new { }) with
                { ExitCode = 17, Stderr = "native install rejected" }), context.CancellationToken));
        RubyPreparationCommand observed = Assert.ContainsSingle(error.Commands);
        Assert.AreEqual(request.Tools.Dotnet, observed.Command.Executable);
        Assert.AreEqual(17, observed.Result.ExitCode);
        Assert.Contains("native install rejected", error.Message);
    }

    [TestMethod]
    public async Task CancellationCannotPrepareCandidate()
    {
        using var state = new SharedState();
        RubyGroupPreparationRequest request = await state.RequestAsync(context.CancellationToken);
        using var cancellation = new CancellationTokenSource();
        await Assert.ThrowsExactlyAsync<OperationCanceledException>(() =>
            RubyGroupPreparation.PrepareAsync(request, async (command, token) =>
            {
                NativeCommandResult result = await NativeAsync(request, command, token);
                cancellation.Cancel();
                return result;
            }, cancellation.Token));
        Assert.IsFalse(Directory.Exists(Path.Combine(request.Scratch, "candidate")));
    }
}
