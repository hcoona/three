using System.Text.Json.Nodes;
using LiteAgensh;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace LiteAgensh.Tests;

internal sealed class GitScenario : IDisposable
{
    private static readonly int[] RootTask = [1];
    private readonly ProcessRunner processes;
    private string originalHead = "";
    private string originalStatus = "";
    private string originalIndex = "";
    private string originalDiff = "";
    private string originalConfig = "";

    private GitScenario(Func<RunConfiguration, RunConfiguration>? configure, TimeProvider? clock)
    {
        Fixture = new();
        Directory.CreateDirectory(Path.Combine(Fixture.RootPath, "fixture-hooks"));
        RunConfiguration configuration =
            configure?.Invoke(Fixture.Configuration) ?? Fixture.Configuration;
        Database = new(Fixture.StatePath, configuration, clock);
        Collaboration = new(Database);
        Operations = new(Database, Collaboration);
        processes = new(Database);
    }

    public RunFixture Fixture { get; }

    public RunDatabase Database { get; }

    public CollaborationOperations Collaboration { get; }

    public GitOperations Operations { get; }

    public static async Task<GitScenario> CreateAsync(
        CancellationToken token,
        Func<RunConfiguration, RunConfiguration>? configure = null,
        TimeProvider? clock = null
    )
    {
        GitScenario scenario = new(configure, clock);
        try
        {
            string repository = scenario.Fixture.RepositoryPath;
            await scenario.GitAsync(repository, token, "init", "--quiet", "--initial-branch=main");
            foreach (
                (string name, string content) in new[]
                {
                    ("AGENTS.md", "Owner-authored instructions must remain intact."),
                    ("owner.note", "owner baseline"),
                    ("staged.txt", "index baseline"),
                    ("a.txt", "base-a"),
                    ("b.txt", "base-b"),
                    ("shared.txt", "shared baseline"),
                }
            )
            {
                File.WriteAllText(Path.Combine(repository, name), content);
            }

            await scenario.GitAsync(repository, token, "add", "--", ".");
            await scenario.GitAsync(
                repository,
                token,
                "commit",
                "--quiet",
                "-m",
                "Self-authored fixture baseline"
            );
            scenario.originalHead = (
                await scenario.GitAsync(repository, token, "rev-parse", "HEAD")
            ).Trim();
            scenario.Database.SaveConfiguration(
                scenario.Database.Configuration with
                {
                    BaseRevision = scenario.originalHead,
                }
            );
            File.WriteAllText(Path.Combine(repository, "owner.note"), "owner unstaged work");
            File.WriteAllText(Path.Combine(repository, "staged.txt"), "owner staged work");
            File.WriteAllText(
                Path.Combine(repository, "owner-untracked.txt"),
                "owner untracked work"
            );
            await scenario.GitAsync(repository, token, "add", "--", "staged.txt");
            scenario.originalStatus = await scenario.GitAsync(
                repository,
                token,
                "status",
                "--porcelain=v1",
                "--untracked-files=all"
            );
            scenario.originalDiff = await scenario.GitAsync(repository, token, "diff", "--binary");
            scenario.originalIndex = await scenario.GitAsync(
                repository,
                token,
                "diff",
                "--cached",
                "--binary"
            );
            scenario.originalConfig = File.ReadAllText(Path.Combine(repository, ".git", "config"));
            await scenario.Operations.InitializeAsync(token);
            return scenario;
        }
        catch
        {
            scenario.Dispose();
            throw;
        }
    }

    public async Task<string> CommitAsync(
        string worker,
        string file,
        string content,
        CancellationToken token
    )
    {
        string path = Operations.GetWorkerPath(worker);
        File.WriteAllText(Path.Combine(path, file), content);
        await GitAsync(path, token, "add", "--", file);
        await GitAsync(path, token, "commit", "--quiet", "-m", $"Change {file}");
        return (await GitAsync(path, token, "rev-parse", "HEAD")).Trim();
    }

    public async Task<JsonObject> CallAsync(
        string actor,
        string operation,
        object arguments,
        CancellationToken token
    ) =>
        Scenario.Data(
            await Operations.InvokeAsync(actor, operation, Json.SerializePayload(arguments), token)
        );

    public async Task<JsonObject> SubmitAsync(string worker, CancellationToken token) =>
        await CallAsync(
            worker,
            "change_write",
            new
            {
                action = "submit",
                title = $"{worker} change",
                body = "Inspect the actual diff.",
                taskIds = RootTask,
            },
            token
        );

    public async Task ApproveAsync(JsonObject change, string reviewer, CancellationToken token) =>
        await CallAsync(
            reviewer,
            "change_review",
            new
            {
                id = Scenario.Id(change),
                head = change["head"]!.GetValue<string>(),
                decision = "approve",
                body = "Reviewed this exact candidate.",
            },
            token
        );

    public async Task<JsonObject> QueueAsync(JsonObject change, CancellationToken token) =>
        await CallAsync(
            "owner",
            "change_write",
            new
            {
                action = "integrate",
                id = Scenario.Id(change),
                head = change["head"]!.GetValue<string>(),
            },
            token
        );

    public async Task<JsonObject> ReadAsync(JsonObject change, CancellationToken token) =>
        await CallAsync("owner", "change_read", new { id = Scenario.Id(change) }, token);

    public async Task<string> GitAsync(
        string cwd,
        CancellationToken token,
        params string[] arguments
    )
    {
        ProcessResult result = await processes.RunAsync(
            "git",
            [
                "-c",
                "user.name=ScriptedFixture",
                "-c",
                "user.email=fixture@localhost",
                "-c",
                "commit.gpgSign=false",
                "-c",
                "core.autocrlf=false",
                "-c",
                "core.hooksPath=" + Path.Combine(Fixture.RootPath, "fixture-hooks"),
                .. arguments,
            ],
            cwd,
            TimeSpan.FromSeconds(30),
            token
        );
        Assert.AreEqual(0, result.ExitCode, result.StandardError);
        Assert.IsFalse(result.TimedOut);
        return result.StandardOutput;
    }

    public async Task AssertOriginalPreservedAsync(CancellationToken token)
    {
        string repository = Fixture.RepositoryPath;
        Assert.AreEqual(
            originalHead,
            (await GitAsync(repository, token, "rev-parse", "HEAD")).Trim()
        );
        Assert.AreEqual(
            "main",
            (await GitAsync(repository, token, "branch", "--show-current")).Trim()
        );
        Assert.AreEqual(
            originalStatus,
            await GitAsync(repository, token, "status", "--porcelain=v1", "--untracked-files=all")
        );
        Assert.AreEqual(originalDiff, await GitAsync(repository, token, "diff", "--binary"));
        Assert.AreEqual(
            originalIndex,
            await GitAsync(repository, token, "diff", "--cached", "--binary")
        );
        Assert.AreEqual(
            originalConfig,
            File.ReadAllText(Path.Combine(repository, ".git", "config"))
        );
        Assert.AreEqual(
            "owner untracked work",
            File.ReadAllText(Path.Combine(repository, "owner-untracked.txt"))
        );
        Assert.AreEqual(
            "Owner-authored instructions must remain intact.",
            File.ReadAllText(Path.Combine(repository, "AGENTS.md"))
        );
    }

    public void Dispose() => Fixture.Dispose();
}
