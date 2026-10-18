using System.Text.Json;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using WorkflowDelivery.Platform;
using WorkflowDelivery.Repository;
using GitFixture = WorkflowDelivery.Tests.Repository.GitReaderTests.GitFixture;

namespace WorkflowDelivery.Tests.Repository;

[TestClass]
public sealed class RubyBundlePreparationTests(TestContext context)
{
    private static RubyBundleTools Tools(string root) => new(new(Path.Combine(root, "ruby"),
        Path.Combine(root, "facts.rb"), root,
        new Dictionary<string, string?> { ["GEM_HOME"] = Path.Combine(root, "owned-gems") }, 30),
        Path.Combine(root, "gem"), Path.Combine(root, "bundle"), "2.7.2");
    private static NativeCommandResult Success => new(NativeTermination.Exited, 0, "", "", 0, null);
    private async Task<GitMaterialization> BindAsync(GitFixture repo)
    {
        string head = await repo.CommitAsync();
        await repo.GitAsync("reset", "--hard", head);
        return await GitMaterialization.BindAsync(repo.Directory,
            await new GitReader(repo.Directory).ReadAsync(head, context.CancellationToken),
            context.CancellationToken);
    }

    [TestMethod]
    public async Task NativePreparationUsesFrozenOriginalLocksAndOwnedState()
    {
        using var repo = await GitFixture.CreateAsync(context.CancellationToken);
        await repo.SetAsync("src/a/Gemfile", "original declaration");
        await repo.SetAsync("src/a/Gemfile.lock", "original lock");
        GitMaterialization checkout = await BindAsync(repo);
        RubyBundleTools tools = Tools(repo.Directory);
        RubyPreparedBundles result = await RubyBundlePreparation.PrepareAsync(checkout, tools,
            async (command, token) =>
            {
                Assert.IsFalse(command.InheritEnvironment);
                Assert.AreEqual(tools.Facts.Environment["GEM_HOME"],
                    command.Environment!["GEM_HOME"]);
                if (command.Arguments[0] == tools.Gem) return Success;
                string gemfile = Path.Combine(repo.Directory, "src/a/Gemfile");
                if (command.Arguments[0] == tools.Bundle)
                {
                    Assert.AreEqual("_2.4.20_", command.Arguments[1]);
                    Assert.AreEqual(gemfile, command.Environment["BUNDLE_GEMFILE"]);
                    Assert.AreEqual("1", command.Environment["BUNDLE_FROZEN"]);
                    return Success;
                }
                using JsonDocument request = JsonDocument.Parse(
                    await File.ReadAllTextAsync(command.Arguments[^1], token));
                Assert.AreEqual("bundle-context",
                    request.RootElement.GetProperty("operation").GetString());
                return Success with { Stdout = JsonSerializer.Serialize(new { gemfile,
                    lockfile = gemfile + ".lock", bundler_version = "2.7.2",
                    locked_bundler_version = "2.4.20" }) };
            }, context.CancellationToken);
        Assert.HasCount(3, result.Commands);
        Assert.AreEqual("2.7.2", result.Commands[0].Command.Arguments[4]);
        Assert.AreEqual("2.4.20", result.Commands[1].Command.Arguments[4]);
        Assert.AreEqual(tools.Bundle, result.Commands[2].Command.Arguments[0]);
        Assert.AreEqual("2.4.20", Assert.ContainsSingle(result.Contexts).LockedBundlerVersion);
        Assert.AreEqual("original lock", await File.ReadAllTextAsync(
            Path.Combine(repo.Directory, "src/a/Gemfile.lock"), context.CancellationToken));
    }

    [TestMethod]
    public async Task FailedDependencyInstallationRetainsNativeDiagnostics()
    {
        using var repo = await GitFixture.CreateAsync(context.CancellationToken);
        await repo.SetAsync("src/a/Gemfile");
        GitMaterialization checkout = await BindAsync(repo);
        string stderr = "native installation head " + new string('x', NativeDiagnostic.TailLength) +
            " native installation error";
        RubyBundlePreparationException error = await Assert.ThrowsExactlyAsync<
            RubyBundlePreparationException>(() => RubyBundlePreparation.PrepareAsync(checkout,
                Tools(repo.Directory), (_, _) => Task.FromResult(Success with
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
    [DataRow(false)]
    [DataRow(true)]
    public async Task MissingOriginalLockOrFailedBundleCannotCompletePreparation(bool failedBundle)
    {
        using var repo = await GitFixture.CreateAsync(context.CancellationToken);
        await repo.SetAsync("src/a/Gemfile");
        if (failedBundle) await repo.SetAsync("src/a/Gemfile.lock");
        GitMaterialization checkout = await BindAsync(repo);
        RubyBundleTools tools = Tools(repo.Directory);
        async Task<RubyPreparedBundles> RunAsync() => await RubyBundlePreparation.PrepareAsync(
            checkout, tools, async (command, token) =>
            {
                if (command.Arguments[0] == tools.Gem) return Success;
                if (command.Arguments[0] == tools.Bundle)
                {
                    Assert.IsTrue(failedBundle,
                        "A missing committed lock must prevent native bundle installation.");
                    return Success with { ExitCode = 9, Stderr = "native frozen bundle failed" };
                }
                string gemfile = Path.Combine(repo.Directory, "src/a/Gemfile");
                using JsonDocument request = JsonDocument.Parse(
                    await File.ReadAllTextAsync(command.Arguments[^1], token));
                Assert.AreEqual("bundle-context",
                    request.RootElement.GetProperty("operation").GetString());
                return Success with { Stdout = JsonSerializer.Serialize(new { gemfile,
                    lockfile = gemfile + ".lock", bundler_version = "2.7.2",
                    locked_bundler_version = "2.7.2" }) };
            }, context.CancellationToken);
        if (!failedBundle) await Assert.ThrowsExactlyAsync<InvalidDataException>(RunAsync);
        else
        {
            RubyBundlePreparationException error = await Assert.ThrowsExactlyAsync<
                RubyBundlePreparationException>(RunAsync);
            Assert.AreEqual(9, error.Commands[^1].Result.ExitCode);
            Assert.AreEqual(tools.Bundle, error.Commands[^1].Command.Arguments[0]);
            Assert.Contains("native frozen bundle failed", error.Message);
        }
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task EmptyOrCancelledPreparationCannotLaunchTools(bool cancelled)
    {
        using var repo = await GitFixture.CreateAsync(context.CancellationToken);
        await repo.SetAsync("docs/guide.md");
        GitMaterialization checkout = await BindAsync(repo);
        using var cancellation = new CancellationTokenSource();
        if (cancelled) cancellation.Cancel();
        Task<RubyPreparedBundles> RunAsync() => RubyBundlePreparation.PrepareAsync(checkout,
            Tools(repo.Directory), (_, _) =>
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
