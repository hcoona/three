using System.Globalization;
using System.Text.Json.Nodes;
using Microsoft.Data.Sqlite;

namespace LiteAgensh;

internal sealed class IntegrationHealthGate(
    RunDatabase database,
    CollaborationOperations collaboration,
    GitWorktrees worktrees,
    GitCheckRunner checks,
    Action requireSettledProcessOwnership,
    Func<string, string?> findBreakingAuthor
)
{
    private static readonly TimeSpan AlertAfter = TimeSpan.FromMinutes(15);
    private static readonly TimeSpan LiftAfter = TimeSpan.FromMinutes(60);

    private string Stamp => database.Now.ToString("O", CultureInfo.InvariantCulture);

    internal static void CreateSchema(
        SqliteConnection connection,
        SqliteTransaction transaction
    ) =>
        Sql.Execute(
            connection,
            """
            CREATE TABLE IF NOT EXISTS git_health(
                singleton INTEGER PRIMARY KEY CHECK(singleton=1), commit_hash TEXT NOT NULL,
                passed INTEGER NOT NULL, red_since TEXT, broken_commit TEXT NOT NULL,
                alerted INTEGER NOT NULL, lifted INTEGER NOT NULL, checked_at TEXT NOT NULL
            );
            """,
            transaction
        );

    internal bool TryRecordIdlePass(string expectedHead)
    {
        using SqliteConnection connection = database.Open();
        bool idle;
        using (
            SqliteCommand command = Sql.Command(
                connection,
                "SELECT commit_hash,passed,red_since,alerted,lifted "
                    + "FROM git_health WHERE singleton=1;"
            )
        )
        using (ObservedSqliteReader reader = Sql.Reader(command))
        {
            if (!reader.Read() || reader.GetString(0) != expectedHead)
            {
                return false;
            }

            if (reader.GetBoolean(1))
            {
                idle = true;
            }
            else if (reader.IsDBNull(2))
            {
                idle = false;
            }
            else
            {
                DateTimeOffset redSince = DateTimeOffset.Parse(
                    reader.GetString(2),
                    CultureInfo.InvariantCulture
                );
                TimeSpan elapsed = database.Now - redSince;
                idle =
                    (reader.GetBoolean(3) || elapsed < AlertAfter)
                    && (reader.GetBoolean(4) || elapsed < LiftAfter);
            }
        }

        if (!idle)
        {
            return false;
        }

        Sql.Execute(
            connection,
            "UPDATE git_health SET checked_at=$time WHERE singleton=1;",
            null,
            ("$time", Stamp)
        );
        return true;
    }

    internal async Task<JsonObject> EvaluateAsync(bool forceChecks, CancellationToken token)
    {
        requireSettledProcessOwnership();
        string head = await worktrees.ResolveAsync(worktrees.IntegrationBranch, token)
            .ConfigureAwait(false);
        JsonObject? old = Read();
        bool passed;
        if (forceChecks || old is null || old["commit"]!.GetValue<string>() != head)
        {
            await worktrees.RequireReadyIntegrationWorktreeAsync(token).ConfigureAwait(false);
            passed = await checks.RunChecksAsync(
                    null,
                    "integration",
                    head,
                    worktrees.IntegrationPath,
                    database.Configuration.IntegrationChecks,
                    Guid.NewGuid().ToString("N"),
                    token
                )
                .ConfigureAwait(false);
            await worktrees.RequireExactTreeAsync(worktrees.IntegrationPath, head, token)
                .ConfigureAwait(false);
        }
        else
        {
            passed = old["passed"]!.GetValue<bool>();
        }

        if (
            await worktrees.ResolveAsync(worktrees.IntegrationBranch, token)
                .ConfigureAwait(false) != head
        )
        {
            throw new OperationException(
                "stale_candidate",
                "Integration moved during health evaluation."
            );
        }

        DateTimeOffset? redSince = old?["redSince"] is JsonValue since
            ? DateTimeOffset.Parse(since.GetValue<string>(), CultureInfo.InvariantCulture)
            : null;
        bool alerted = old?["teamAlerted"]?.GetValue<bool>() ?? false;
        bool lifted = old?["restrictionLifted"]?.GetValue<bool>() ?? false;
        string broken = old?["brokenCommit"]?.GetValue<string>() ?? "";
        List<(string Kind, string Message, string? Recipient)> events = [];
        if (!passed)
        {
            if (redSince is null)
            {
                redSince = database.Now;
                broken = head;
                alerted = lifted = false;
                events.Add(
                    (
                        "health-broken",
                        $"Integration fails at {head}; only changes linked to "
                            + $"integration-health task {EnsureHealthTask()} may integrate.",
                        findBreakingAuthor(head)
                    )
                );
            }
            else if (!alerted && database.Now - redSince.Value >= AlertAfter)
            {
                alerted = true;
                events.Add(
                    (
                        "health-alert",
                        "Integration has failed for at least 15 minutes. All workers "
                            + "should help restore integration health.",
                        null
                    )
                );
            }

            if (!lifted && database.Now - redSince.Value >= LiftAfter)
            {
                lifted = true;
                events.Add(
                    (
                        "health-lift",
                        "Integration still fails; the fix-main restriction lifted after "
                            + "60 minutes. Required candidate/final checks still apply.",
                        null
                    )
                );
            }
        }
        else if (redSince is not null)
        {
            events.Add(("health-recovered", $"Integration passes again at {head}.", null));
            redSince = null;
            broken = "";
            alerted = lifted = false;
        }

        using SqliteConnection connection = database.Open();
        using SqliteTransactionScope transaction = Sql.BeginTransaction(
            connection, operation: "EvaluateHealthCoreAsync"
        );
        Sql.Execute(
            connection,
            """
            INSERT INTO git_health(singleton,commit_hash,passed,red_since,broken_commit,
                alerted,lifted,checked_at)
            VALUES(1,$head,$passed,$since,$broken,$alerted,$lifted,$time)
            ON CONFLICT(singleton) DO UPDATE SET commit_hash=excluded.commit_hash,
            passed=excluded.passed,red_since=excluded.red_since,
            broken_commit=excluded.broken_commit,alerted=excluded.alerted,
            lifted=excluded.lifted,checked_at=excluded.checked_at;
            """,
            transaction,
            ("$head", head),
            ("$passed", passed),
            ("$since", redSince?.ToString("O", CultureInfo.InvariantCulture)),
            ("$broken", broken),
            ("$alerted", alerted),
            ("$lifted", lifted),
            ("$time", Stamp)
        );
        foreach ((string kind, string message, string? recipient) in events)
        {
            worktrees.PublishEvent(connection, transaction, kind, message, recipient);
        }

        transaction.Commit();
        return Read()!;
    }

    internal JsonObject? Read()
    {
        using SqliteConnection connection = database.Open();
        using SqliteCommand command = Sql.Command(
            connection,
            "SELECT commit_hash,passed,red_since,broken_commit,alerted,lifted,checked_at "
                + "FROM git_health WHERE singleton=1;"
        );
        using ObservedSqliteReader reader = Sql.Reader(command);
        return !reader.Read()
            ? null
            : new JsonObject
            {
                ["commit"] = reader.GetString(0),
                ["passed"] = reader.GetBoolean(1),
                ["redSince"] = reader.IsDBNull(2) ? null : reader.GetString(2),
                ["brokenCommit"] = reader.GetString(3),
                ["teamAlerted"] = reader.GetBoolean(4),
                ["restrictionLifted"] = reader.GetBoolean(5),
                ["fixMainOnly"] = !reader.GetBoolean(1) && !reader.GetBoolean(5),
                ["checkedAt"] = reader.GetString(6),
                ["taskId"] = long.TryParse(
                    database.ReadMetadata(RunMetadata.GitHealthTaskKey),
                    CultureInfo.InvariantCulture,
                    out long task
                )
                    ? task
                    : null,
            };
    }

    internal void Enforce(IReadOnlyList<long> changeTaskIds)
    {
        JsonObject? health = Read();
        if (
            health?["fixMainOnly"]?.GetValue<bool>() == true
            && (changeTaskIds.Count == 0 || !changeTaskIds.Contains(EnsureHealthTask()))
        )
        {
            throw new OperationException(
                "check_failed",
                "Integration is failing; only changes linked to the dedicated "
                    + "integration-health task may integrate before the 60-minute lift."
            );
        }
    }

    private long EnsureHealthTask()
    {
        if (
            long.TryParse(
                database.ReadMetadata(RunMetadata.GitHealthTaskKey),
                CultureInfo.InvariantCulture,
                out long existing
            )
        )
        {
            return existing;
        }

        string title = "Integration health (fix-main) " + database.RunId;
        if (collaboration.FindOwnerTaskByTitle(title) is long recoveredId)
        {
            database.WriteMetadata(
                RunMetadata.GitHealthTaskKey,
                recoveredId.ToString(CultureInfo.InvariantCulture)
            );
            return recoveredId;
        }

        JsonObject result = collaboration.Invoke(
            "owner",
            "task_write",
            new JsonObject
            {
                ["action"] = "create",
                ["title"] = title,
                ["body"] =
                    "Restore the integration branch's declared checks. Link fix-main "
                        + "submissions to this task. Candidate and final checks remain "
                        + "required.",
            }
        );
        if (result["ok"]?.GetValue<bool>() != true || result["data"]?["id"] is not JsonValue id)
        {
            throw new OperationException(
                "unavailable",
                "Could not create the dedicated integration-health task."
            );
        }

        long value = id.GetValue<long>();
        database.WriteMetadata(
            RunMetadata.GitHealthTaskKey, value.ToString(CultureInfo.InvariantCulture)
        );
        return value;
    }
}
