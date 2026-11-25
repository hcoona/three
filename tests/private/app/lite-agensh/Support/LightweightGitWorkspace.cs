using System.Globalization;
using System.Text.Json.Nodes;
using Microsoft.Data.Sqlite;

namespace LiteAgensh.Tests.Support;

// Fast directory-and-SQLite workspace substitute for coordinator scenarios whose subject is
// lifecycle, credit, prompt, or session recovery rather than Git worktrees or merges.
internal sealed class LightweightGitWorkspace : IGitWorkspace
{
    internal const string SyntheticBaseCommit = "0123456789abcdef0123456789abcdef01234567";
    private readonly RunDatabase database;
    private readonly CollaborationOperations collaboration;

    internal LightweightGitWorkspace(RunDatabase database, CollaborationOperations collaboration)
    {
        this.database = database;
        this.collaboration = collaboration;
        GitOperations.EnsureSchema(database);
    }

    private string IntegrationBranch => $"refs/heads/lite-agensh/{database.RunId}/integration";
    private string DeliveryBranch => $"refs/heads/lite-agensh/{database.RunId}/delivery";
    private string IntegrationPath => Path.Combine(database.StatePath, "worktrees", "integration");
    private string Stamp => database.Now.ToString("O", CultureInfo.InvariantCulture);

    public Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        RunConfiguration configuration = database.Configuration;
        Directory.CreateDirectory(configuration.RepositoryPath);
        Directory.CreateDirectory(IntegrationPath);
        string baseCommit =
            configuration.BaseRevision == "HEAD" ? SyntheticBaseCommit : configuration.BaseRevision;
        if (configuration.BaseRevision != baseCommit)
        {
            database.SaveConfiguration(configuration with { BaseRevision = baseCommit });
        }

        database.WriteMetadata("git:base", baseCommit);
        database.WriteMetadata("git:base_commit", baseCommit);
        database.WriteMetadata("git:integration_branch", IntegrationBranch);
        database.WriteMetadata("git:integration_path", IntegrationPath);
        database.WriteMetadata("git:delivery_branch", DeliveryBranch);
        foreach (string worker in database.Configuration.WorkerIds)
        {
            string path = GetWorkerPath(worker);
            Directory.CreateDirectory(path);
            database.WriteMetadata("worktree:" + worker, path);
        }

        database.WriteMetadata("git:initialized", "true");
        return Task.CompletedTask;
    }

    public string GetWorkerPath(string worker)
    {
        if (!database.Configuration.WorkerIds.Contains(worker, StringComparer.Ordinal))
        {
            throw new OperationException(
                "unknown_actor",
                "Only a configured worker has a worker worktree."
            );
        }

        return Path.Combine(database.StatePath, "worktrees", worker);
    }

    public Task ProcessPendingAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        string head = database.ReadMetadata("git:base_commit") ?? SyntheticBaseCommit;
        using SqliteConnection connection = database.Open();
        Sql.Execute(
            connection,
            """
            INSERT INTO git_health(singleton,commit_hash,passed,red_since,broken_commit,
                alerted,lifted,checked_at)
            VALUES(1,$head,1,NULL,'',0,0,$time)
            ON CONFLICT(singleton) DO UPDATE SET checked_at=excluded.checked_at;
            """,
            null,
            ("$head", head),
            ("$time", Stamp)
        );
        return Task.CompletedTask;
    }

    public Task<JsonObject> InvokeAsync(
        string actor,
        string name,
        JsonObject arguments,
        CancellationToken cancellationToken = default
    )
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (name == "change_list")
        {
            return Task.FromResult(Operation.Success(new { items = new JsonArray() }));
        }

        throw new NotSupportedException(
            $"Git operation '{name}' requires real GitOperations, not LightweightGitWorkspace."
        );
    }

    public Task<JsonObject> FinalizeAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        string head = database.ReadMetadata("git:base_commit") ?? SyntheticBaseCommit;
        string batch = Guid.NewGuid().ToString("N");
        JsonArray tasks = [];
        long cursor = 0;
        while (true)
        {
            JsonObject pageResult = collaboration.Invoke(
                "owner",
                "task_list",
                new JsonObject
                {
                    ["state"] = "open",
                    ["cursor"] = cursor,
                    ["limit"] = 200,
                }
            );
            JsonObject page = pageResult["data"]!.AsObject();
            foreach (JsonNode? item in page["items"]!.AsArray())
            {
                tasks.Add(item!.DeepClone());
            }

            if (page["nextCursor"] is not JsonValue next)
            {
                break;
            }

            cursor = next.GetValue<long>();
        }

        JsonObject health = new()
        {
            ["commit"] = head,
            ["passed"] = true,
            ["redSince"] = null,
            ["brokenCommit"] = "",
            ["teamAlerted"] = false,
            ["restrictionLifted"] = false,
            ["fixMainOnly"] = false,
            ["checkedAt"] = Stamp,
            ["taskId"] = null,
        };
        JsonObject report = new()
        {
            ["baseCommit"] = database.Configuration.BaseRevision,
            ["finalCommit"] = head,
            ["deliveryRef"] = DeliveryBranch,
            ["accepted"] = true,
            ["checks"] = new JsonArray(),
            ["finalCheckBatch"] = batch,
            ["integrationHealth"] = health,
            ["unresolvedTasks"] = tasks,
            ["unresolvedChanges"] = new JsonArray(),
            ["statePath"] = database.StatePath,
            ["createdAt"] = Stamp,
        };
        database.WriteMetadata("git:delivery_report", report.ToJsonString(Json.Options));
        database.WriteMetadata("git:finalized", "true");
        JsonObject result = Operation.Success(report);
        result["data"] = report;
        return Task.FromResult(result);
    }

    public Task<JsonObject> CleanupAsync(CancellationToken cancellationToken = default) =>
        throw new NotSupportedException(
            "Explicit Git cleanup scenarios require real GitOperations."
        );
}
