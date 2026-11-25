using System.Text.Json;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using WorkflowDelivery.Platform;
using WorkflowDelivery.Repository;
using GitFixture = WorkflowDelivery.Tests.Repository.GitReaderTests.GitFixture;

namespace WorkflowDelivery.Tests.Repository;

[TestClass]
public sealed class RubyBundlePreparationTests(TestContext context)
{
    private const string Gemfile = "src/a/Gemfile";
    private static GitFixture? shared;
    private static GitMaterialization? empty;
    private static GitMaterialization? unlocked;
    private static GitMaterialization? locked;

    [ClassInitialize]
    public static async Task InitializeAsync(TestContext classContext)
    {
        CancellationToken token = classContext.CancellationToken;
        GitFixture repo = await GitFixture.CreateAsync(token);
        try
        {
            string parent = Path.GetDirectoryName(repo.Directory)!;
            await repo.SetAsync("docs/guide.md");
            empty = await CommitCheckoutAsync("empty");
            await repo.SetAsync(Gemfile, "original declaration");
            unlocked = await CommitCheckoutAsync("unlocked");
            await repo.SetAsync(Gemfile + ".lock", "original lock");
            locked = await CommitCheckoutAsync("locked");
            shared = repo;

            async Task<GitMaterialization> CommitCheckoutAsync(string name)
            {
                string commit = await repo.CommitAsync();
                string checkout = Path.Combine(parent, name);
                await repo.GitAsync("worktree", "add", "--detach", checkout, commit);
                return await GitMaterialization.BindAsync(checkout, commit, token);
            }
        }
        catch
        {
            repo.Dispose();
            throw;
        }
    }

    [ClassCleanup]
    public static void Cleanup() => shared?.Dispose();

    private sealed class ToolsScratch : IDisposable
    {
        private readonly string root = Directory.CreateTempSubdirectory(
            "ruby-bundle-preparation-").FullName;

        internal RubyBundleTools Tools => new(new(Path.Combine(root, "ruby"),
            Path.Combine(root, "facts.rb"), root,
            new Dictionary<string, string?> { ["GEM_HOME"] = Path.Combine(root, "gems") }, 30),
            Path.Combine(root, "gem"), Path.Combine(root, "bundle"), "2.7.2");

        public void Dispose() => Directory.Delete(root, recursive: true);
    }

    private static NativeCommandResult Success => new(NativeTermination.Exited, 0, "", "", 0, null);
    [TestMethod]
    public async Task PlanningPreparationActivatesLockedBundlerWithoutInstallingTheBundle()
    {
        GitMaterialization checkout = locked!;
        using var scratch = new ToolsScratch();
        RubyBundleTools tools = scratch.Tools;
        RubyPreparedBundles result = await RubyBundlePreparation.PrepareAsync(checkout, tools,
            async (command, token) =>
            {
                Assert.IsFalse(command.InheritEnvironment);
                Assert.AreEqual(tools.Facts.Environment["GEM_HOME"],
                    command.Environment!["GEM_HOME"]);
                if (command.Arguments[0] == tools.Gem) return Success;
                string gemfile = Path.Combine(checkout.Root, Gemfile);
                Assert.AreNotEqual(tools.Bundle, command.Arguments[0],
                    "Planning reads the lock; only selected execution installs the bundle.");
                using JsonDocument request = JsonDocument.Parse(
                    await File.ReadAllTextAsync(command.Arguments[^1], token));
                Assert.AreEqual("bundle-context",
                    request.RootElement.GetProperty("operation").GetString());
                return Success with
                {
                    Stdout = JsonSerializer.Serialize(new
                    {
                        gemfile,
                        lockfile = gemfile + ".lock",
                        bundler_version = "2.7.2",
                        locked_bundler_version = "2.4.20"
                    })
                };
            }, context.CancellationToken);
        Assert.HasCount(2, result.Commands);
        Assert.AreEqual("2.7.2", result.Commands[0].Command.Arguments[4]);
        Assert.AreEqual("2.4.20", result.Commands[1].Command.Arguments[4]);
        Assert.AreEqual("2.4.20", Assert.ContainsSingle(result.Contexts).LockedBundlerVersion);
        Assert.AreEqual("original lock", await File.ReadAllTextAsync(
            Path.Combine(checkout.Root, Gemfile + ".lock"), context.CancellationToken));
    }

    [TestMethod]
    public async Task FailedDependencyInstallationRetainsNativeDiagnostics()
    {
        GitMaterialization checkout = unlocked!;
        using var scratch = new ToolsScratch();
        string stderr = "native installation head " + new string('x', NativeDiagnostic.TailLength) +
            " native installation error";
        RubyBundlePreparationException error = await Assert.ThrowsExactlyAsync<
            RubyBundlePreparationException>(() => RubyBundlePreparation.PrepareAsync(checkout,
                scratch.Tools, (_, _) => Task.FromResult(Success with
                { ExitCode = 7, Stderr = stderr }),
                context.CancellationToken));
        RubyPreparationCommand command = Assert.ContainsSingle(error.Commands);
        Assert.AreEqual(7, command.Result.ExitCode);
        Assert.AreEqual(stderr, command.Result.Stderr);
        Assert.StartsWith("Native Ruby dependency preparation failed: Exited, exit 7. " +
            "stderr: [truncated] ", error.Message);
        Assert.EndsWith(" native installation error", error.Message);
        Assert.DoesNotContain("native installation head", error.Message);
        Assert.IsLessThan(NativeDiagnostic.TailLength + 200, error.Message.Length);
    }

    [TestMethod]
    public async Task MissingOriginalLockCannotCompletePreparation()
    {
        GitMaterialization checkout = unlocked!;
        using var scratch = new ToolsScratch();
        RubyBundleTools tools = scratch.Tools;
        Task<RubyPreparedBundles> RunAsync() => RubyBundlePreparation.PrepareAsync(
            checkout, tools, (command, _) =>
            {
                if (command.Arguments[0] == tools.Gem) return Task.FromResult(Success);
                string gemfile = Path.Combine(checkout.Root, Gemfile);
                return Task.FromResult(Success with
                {
                    Stdout = JsonSerializer.Serialize(new
                    {
                        gemfile,
                        lockfile = gemfile + ".lock",
                        bundler_version = "2.7.2",
                        locked_bundler_version = "2.7.2"
                    })
                });
            }, context.CancellationToken);
        InvalidDataException failure = await Assert.ThrowsExactlyAsync<InvalidDataException>(
            RunAsync);
        Assert.Contains("lock is not committed", failure.Message);
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task EmptyOrCancelledPreparationCannotLaunchTools(bool cancelled)
    {
        GitMaterialization checkout = empty!;
        using var scratch = new ToolsScratch();
        using var cancellation = new CancellationTokenSource();
        if (cancelled) cancellation.Cancel();
        Task<RubyPreparedBundles> RunAsync() => RubyBundlePreparation.PrepareAsync(checkout,
            scratch.Tools, (_, _) =>
            {
                Assert.Fail("Empty/cancelled collection cannot install tools or dependencies.");
                return Task.FromResult(Success);
            }, cancellation.Token);
        if (cancelled) await Assert.ThrowsAsync<OperationCanceledException>(RunAsync);
        else
        {
            RubyPreparedBundles result = await RunAsync();
            Assert.IsEmpty(result.Commands);
            Assert.IsEmpty(result.Contexts);
        }
    }
}
