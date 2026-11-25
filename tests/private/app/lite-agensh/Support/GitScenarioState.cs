using System.Globalization;
using System.Text;
using System.Text.Json.Nodes;
using Microsoft.Data.Sqlite;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace LiteAgensh.Tests.Support;

// Each case owns its Git/SQLite state; inherited Git configuration is assembly-owned.
internal sealed class GitScenarioState : IDisposable
{
    private sealed record BaseTemplate(string Directory, string BaseCommit);

    private static readonly SemaphoreSlim TemplateGate = new(1, 1);
    private static volatile BaseTemplate? cachedTemplate;
    private readonly ScratchDirectory scratch = new();
    private readonly ProcessRunner processes;

    private GitScenarioState(
        CheckCommand[]? candidateChecks,
        CheckCommand[]? integrationChecks,
        CheckCommand[]? finalChecks,
        string[]? protectedPaths,
        ConfiguredCheckInvocation? configuredCheck
    )
    {
        try
        {
            AssertEnvironmentUnchanged();
            Directory.CreateDirectory(PathFor("hooks"));
            Directory.CreateDirectory(PathFor("git-template"));
            Directory.CreateDirectory(Repository);
            RunConfiguration configuration = new()
            {
                RepositoryPath = Repository,
                StatePath = PathFor("run"),
                CampaignLedgerPath = PathFor("campaign.sqlite"),
                Goal = "Integrate two independent changes without touching the initial checkout.",
                // A failed native-receipt gate must not reach an installed CLI.
                NativeExecutable = PathFor("unavailable-native", "copilot"),
                CandidateChecks = candidateChecks ?? [ExitCheck(0)],
                IntegrationChecks = integrationChecks ?? [],
                FinalChecks = finalChecks ?? [ExitCheck(0)],
                ProtectedPaths = protectedPaths ?? [],
            };
            Database = new(configuration.StatePath, configuration, Clock);
            Collaboration = new(Database);
            Operations = new(Database, Collaboration, configuredCheck);
            processes = new(Database);
        }
        catch
        {
            Dispose();
            throw;
        }
    }

    internal RunDatabase Database { get; }

    internal CollaborationOperations Collaboration { get; }

    internal GitOperations Operations { get; }

    internal ManualScenarioClock Clock { get; } = new();

    internal string Repository => PathFor("checkout");

    internal string GlobalConfig { get; } = TestAssembly.GlobalConfig;

    internal string SystemConfig { get; } = TestAssembly.SystemConfig;

    internal string IntegrationRef => $"refs/heads/lite-agensh/{Database.RunId}/integration";

    internal string DeliveryRef => $"refs/heads/lite-agensh/{Database.RunId}/delivery";

    internal string IntegrationPath => Path.Combine(Database.StatePath, "worktrees", "integration");

    internal string BaseCommit { get; private set; } = "";

    internal string PathFor(params string[] parts) => scratch.PathFor(parts);

    internal static CheckCommand ExitCheck(int exitCode) =>
        new()
        {
            Executable = OwnedChildControl.ExecutablePath,
            Arguments = ["exit", exitCode.ToString(CultureInfo.InvariantCulture)],
        };

    internal static async Task<GitScenarioState> CreateAsync(
        CancellationToken token,
        CheckCommand[]? candidateChecks = null,
        CheckCommand[]? integrationChecks = null,
        CheckCommand[]? finalChecks = null,
        string[]? protectedPaths = null,
        ConfiguredCheckInvocation? configuredCheck = null
    )
    {
        GitScenarioState state = new(
            candidateChecks, integrationChecks, finalChecks, protectedPaths, configuredCheck
        );
        try
        {
            BaseTemplate template = await EnsureBaseTemplateAsync(state, token)
                .ConfigureAwait(false);
            CopyDirectory(template.Directory, state.Repository);
            state.BaseCommit = template.BaseCommit;
            return state;
        }
        catch
        {
            state.Dispose();
            throw;
        }
    }

    private static async Task<BaseTemplate> EnsureBaseTemplateAsync(
        GitScenarioState bootstrap,
        CancellationToken token
    )
    {
        if (cachedTemplate is { } ready)
        {
            return ready;
        }

        await TemplateGate.WaitAsync(token).ConfigureAwait(false);
        try
        {
            if (cachedTemplate is { } existing)
            {
                return existing;
            }

            string templateRepo = TestAssembly.OwnedPathFor(
                "git-base-template-" + Guid.NewGuid().ToString("N")
            );
            string emptyTemplate = TestAssembly.OwnedPathFor("git-empty-template");
            Directory.CreateDirectory(templateRepo);
            Directory.CreateDirectory(emptyTemplate);
            await bootstrap
                .GitAsync(
                    templateRepo,
                    token,
                    "init",
                    "--quiet",
                    "--initial-branch=main",
                    "--template=" + emptyTemplate
                )
                .ConfigureAwait(false);
            await bootstrap
                .GitAsync(
                    templateRepo,
                    token,
                    "config",
                    "--local",
                    "liteFixture.marker",
                    "owned-local"
                )
                .ConfigureAwait(false);
            File.WriteAllText(
                Path.Combine(templateRepo, "AGENTS.md"),
                "# Target instructions\nKeep these target-owned instructions intact.\n",
                new UTF8Encoding(false)
            );
            File.WriteAllText(Path.Combine(templateRepo, "shared.txt"), "base shared\n");
            File.WriteAllText(Path.Combine(templateRepo, "tracked.txt"), "base tracked\n");
            File.WriteAllText(Path.Combine(templateRepo, "stable.txt"), "base stable\n");
            await bootstrap
                .GitAsync(
                    templateRepo,
                    token,
                    "add",
                    "--",
                    "AGENTS.md",
                    "shared.txt",
                    "tracked.txt",
                    "stable.txt"
                )
                .ConfigureAwait(false);
            await bootstrap
                .GitAsync(templateRepo, token, "commit", "--quiet", "-m", "Owned base")
                .ConfigureAwait(false);
            string baseCommit = (
                await bootstrap
                    .GitAsync(templateRepo, token, "rev-parse", "HEAD")
                    .ConfigureAwait(false)
            ).Trim();
            BaseTemplate created = new(templateRepo, baseCommit);
            cachedTemplate = created;
            return created;
        }
        finally
        {
            TemplateGate.Release();
        }
    }

    private static void CopyDirectory(string source, string destination)
    {
        Directory.CreateDirectory(destination);
        foreach (
            string directory in Directory.GetDirectories(source, "*", SearchOption.AllDirectories)
        )
        {
            Directory.CreateDirectory(
                Path.Combine(destination, Path.GetRelativePath(source, directory))
            );
        }

        foreach (string file in Directory.GetFiles(source, "*", SearchOption.AllDirectories))
        {
            File.Copy(
                file,
                Path.Combine(destination, Path.GetRelativePath(source, file)),
                overwrite: true
            );
        }
    }

    internal async Task<string> GitAsync(string path, CancellationToken token, params string[] args)
    {
        ProcessResult result = await processes
            .RunAsync(
                "git",
                [
                    "--no-optional-locks",
                    "-c",
                    "user.name=Owned Scenario Author",
                    "-c",
                    "user.email=owned-scenario@localhost",
                    "-c",
                    "commit.gpgSign=false",
                    "-c",
                    "core.autocrlf=false",
                    "-c",
                    "core.hooksPath=" + PathFor("hooks"),
                    .. args,
                ],
                path,
                TimeSpan.FromSeconds(60),
                token
            )
            .ConfigureAwait(false);
        Assert.IsFalse(result.TimedOut, "Owned Git fixture command timed out.");
        Assert.AreEqual(0, result.ExitCode, result.StandardError);
        return result.StandardOutput;
    }

    internal async Task<string> ResolveAsync(string reference, CancellationToken token) =>
        (
            await GitAsync(Repository, token, "rev-parse", "--verify", reference)
                .ConfigureAwait(false)
        ).Trim();

    internal async Task<string> CommitAsync(
        string path,
        string file,
        string content,
        CancellationToken token
    )
    {
        File.WriteAllText(Path.Combine(path, file), content, new UTF8Encoding(false));
        await GitAsync(path, token, "add", "--", file).ConfigureAwait(false);
        await GitAsync(path, token, "commit", "--quiet", "-m", "Owned change: " + file)
            .ConfigureAwait(false);
        return (await GitAsync(path, token, "rev-parse", "HEAD").ConfigureAwait(false)).Trim();
    }

    internal async Task<JsonObject> InvokeAsync(
        string actor,
        string name,
        JsonObject arguments,
        CancellationToken token
    ) => Data(await Operations.InvokeAsync(actor, name, arguments, token).ConfigureAwait(false));

    internal static JsonObject Data(JsonObject result)
    {
        Assert.AreEqual("ok", result["code"]!.GetValue<string>(), result.ToJsonString());
        Assert.IsTrue(result["ok"]!.GetValue<bool>(), result.ToJsonString());
        return result["data"]!.AsObject();
    }

    internal async Task<JsonObject> SubmitAsync(
        string actor,
        string title,
        CancellationToken token,
        params long[] taskIds
    )
    {
        JsonArray tasks = [];
        foreach (long id in taskIds)
        {
            tasks.Add(id);
        }

        return await InvokeAsync(
                actor,
                "change_write",
                new()
                {
                    ["action"] = "submit",
                    ["title"] = title,
                    ["body"] = title,
                    ["taskIds"] = tasks,
                },
                token
            )
            .ConfigureAwait(false);
    }

    internal async Task<JsonObject> ReviewAsync(
        string reviewer,
        JsonObject change,
        CancellationToken token
    ) =>
        await InvokeAsync(
                reviewer,
                "change_review",
                new()
                {
                    ["id"] = change["id"]!.GetValue<long>(),
                    ["head"] = change["head"]!.GetValue<string>(),
                    ["decision"] = "approve",
                    ["body"] = "Approve this exact submitted commit.",
                },
                token
            )
            .ConfigureAwait(false);

    internal async Task<JsonObject> QueueAsync(JsonObject change, CancellationToken token) =>
        await InvokeAsync(
                change["author"]!.GetValue<string>(),
                "change_write",
                new()
                {
                    ["action"] = "integrate",
                    ["id"] = change["id"]!.GetValue<long>(),
                    ["head"] = change["head"]!.GetValue<string>(),
                },
                token
            )
            .ConfigureAwait(false);

    internal async Task<JsonObject> ReadAsync(JsonObject change, CancellationToken token) =>
        await InvokeAsync(
                "owner",
                "change_read",
                new() { ["id"] = change["id"]!.GetValue<long>() },
                token
            )
            .ConfigureAwait(false);

    internal string? Text(string sql, params (string Name, object? Value)[] parameters)
    {
        using SqliteConnection connection = Database.Open();
        return Sql.Scalar(connection, sql, null, parameters) as string;
    }

    internal long Number(string sql, params (string Name, object? Value)[] parameters)
    {
        using SqliteConnection connection = Database.Open();
        return Convert.ToInt64(
            Sql.Scalar(connection, sql, null, parameters),
            CultureInfo.InvariantCulture
        );
    }

    internal List<(string Ref, string Path)> Worktrees()
    {
        using SqliteConnection connection = Database.Open();
        using SqliteCommand command = Sql.Command(
            connection,
            "SELECT ref,path FROM git_resources WHERE retained=0 AND state='ready' ORDER BY ref;"
        );
        using ObservedSqliteReader reader = Sql.Reader(command);
        List<(string Ref, string Path)> result = [];
        while (reader.Read())
        {
            result.Add((reader.GetString(0), reader.GetString(1)));
        }

        return result;
    }

    public void Dispose()
    {
        try
        {
            // Git object files may be read-only. Only this instance's disposable root is touched.
            foreach (
                string file in Directory.EnumerateFiles(
                    scratch.Root,
                    "*",
                    SearchOption.AllDirectories
                )
            )
            {
                File.SetAttributes(file, File.GetAttributes(file) & ~FileAttributes.ReadOnly);
            }

            scratch.Dispose();
        }
        finally
        {
            AssertEnvironmentUnchanged();
        }
    }

    internal static void AssertEnvironmentUnchanged() => TestAssembly.AssertEnvironmentUnchanged();
}
