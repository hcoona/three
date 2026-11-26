using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Data.Sqlite;
using static LiteAgensh.ToolArguments;

namespace LiteAgensh;

internal delegate Task<ProcessResult> ConfiguredCheckInvocation(
    string executable,
    IReadOnlyList<string> arguments,
    string workingDirectory,
    TimeSpan timeout,
    CancellationToken cancellationToken
);

internal sealed class GitCheckRunner(
    RunDatabase database,
    GitWorktrees worktrees,
    ProcessRunner processes,
    ConfiguredCheckInvocation? configuredCheck,
    Action requireSettledProcessOwnership
)
{
    private string Stamp => database.Now.ToString("O", CultureInfo.InvariantCulture);

    internal static void CreateSchema(
        SqliteConnection connection,
        SqliteTransaction transaction
    ) =>
        Sql.Execute(
            connection,
            """
            CREATE TABLE IF NOT EXISTS checks(
                id INTEGER PRIMARY KEY AUTOINCREMENT, change_id INTEGER REFERENCES changes(id),
                kind TEXT NOT NULL, commit_hash TEXT NOT NULL, tree TEXT NOT NULL,
                command_json TEXT NOT NULL, status TEXT NOT NULL, exit_code INTEGER,
                log_path TEXT NOT NULL, created_at TEXT NOT NULL, batch TEXT NOT NULL
            );
            """,
            transaction
        );

    internal async Task<bool> RunChecksAsync(
        long? changeId,
        string kind,
        string commit,
        string path,
        CheckCommand[] commands,
        string batch,
        CancellationToken token
    )
    {
        if (commands.Length == 0)
        {
            token.ThrowIfCancellationRequested();
            return true;
        }

        string tree = await worktrees.ResolveTreeAsync(commit, token).ConfigureAwait(false);
        bool passed = true;
        foreach (CheckCommand command in commands)
        {
            token.ThrowIfCancellationRequested();
            requireSettledProcessOwnership();
            await worktrees.RequireExactTreeAsync(path, commit, token).ConfigureAwait(false);
            string status;
            int? exit = null;
            string log;
            bool cancelled = false;
            ProcessResult? completedCommand = null;
            try
            {
                completedCommand = await (configuredCheck ?? processes.RunAsync)(
                        command.Executable,
                        command.Arguments,
                        path,
                        TimeSpan.FromSeconds(command.TimeoutSeconds),
                        token
                    )
                    .ConfigureAwait(false);
                exit = completedCommand.ExitCode;
                status =
                    completedCommand.TimedOut ? "timed-out"
                    : completedCommand.ExitCode == 0 ? "passed"
                    : "failed";
                log = $"stdout:\n{completedCommand.StandardOutput}"
                    + $"\nstderr:\n{completedCommand.StandardError}";
                try
                {
                    await worktrees.RequireExactTreeAsync(path, commit, token)
                        .ConfigureAwait(false);
                }
                catch (OperationException exception)
                {
                    status = "failed";
                    log += "\nCandidate identity failure: " + exception.Message;
                }
            }
            catch (OperationCanceledException exception) when (token.IsCancellationRequested)
            {
                status = "cancelled";
                exit = null;
                log = completedCommand is null
                    ? configuredCheck is null
                        ? "Caller cancelled the check; ProcessRunner settled owned execution "
                            + "before returning."
                        : "Caller cancelled the check; the controlled invocation joined "
                            + "its owned operation before returning."
                    : "Caller cancelled the check; post-command verification did not complete.";
                if (completedCommand is not null)
                {
                    log += "\nCompleted configured command evidence:\n"
                        + new JsonObject
                        {
                            ["observedExitCode"] = completedCommand.ExitCode,
                            ["timedOut"] = completedCommand.TimedOut,
                            ["stdout"] = ProcessRunner.RetainedOutput(
                                completedCommand.StandardOutput
                            ),
                            ["stderr"] = ProcessRunner.RetainedOutput(
                                completedCommand.StandardError
                            ),
                        }.ToJsonString();
                }
                if (
                    (configuredCheck is null || completedCommand is not null)
                    && exception.Data[ProcessRunner.CancellationEvidenceKey] is string evidence
                )
                {
                    log += completedCommand is null
                        ? "\nCommand cancellation evidence:\n"
                        : "\nVerification probe cancellation evidence "
                            + "(not configured-command output):\n";
                    log += evidence;
                }
                cancelled = true;
            }
            catch (Exception exception)
                when (exception
                        is IOException
                            or InvalidOperationException
                            or System.ComponentModel.Win32Exception
                )
            {
                status = "failed";
                log = exception.ToString();
            }

            log = $"Configured check execution: "
                + $"{(configuredCheck is null ? "ProcessRunner" : "controlled")}.\n{log}";
            Directory.CreateDirectory(Path.Combine(database.StatePath, "logs", "checks"));
            string logPath = Path.Combine(
                database.StatePath,
                "logs",
                "checks",
                Guid.NewGuid().ToString("N") + ".log"
            );
            await File.WriteAllTextAsync(logPath, log, Encoding.UTF8, CancellationToken.None)
                .ConfigureAwait(false);
            using (SqliteConnection connection = database.Open())
            using (SqliteTransactionScope transaction = Sql.BeginTransaction(connection))
            {
                Sql.Execute(
                    connection,
                    """
                    INSERT INTO checks(change_id,kind,commit_hash,tree,command_json,status,
                        exit_code,log_path,created_at,batch)
                    VALUES($change,$kind,$commit,$tree,$command,$status,$exit,$log,$time,$batch);
                    """,
                    transaction,
                    ("$change", changeId),
                    ("$kind", kind),
                    ("$commit", commit),
                    ("$tree", tree),
                    ("$command", JsonSerializer.Serialize(command, Json.Options)),
                    ("$status", status),
                    ("$exit", exit),
                    ("$log", logPath),
                    ("$time", Stamp),
                    ("$batch", batch)
                );
                long checkId = Convert.ToInt64(
                    Sql.Scalar(connection, "SELECT last_insert_rowid();", transaction),
                    CultureInfo.InvariantCulture
                );
                worktrees.PublishEvent(
                    connection,
                    transaction,
                    "check",
                    $"{kind} check {checkId} on {commit}: "
                        + $"{status}; log {logPath}."
                );
                transaction.Commit();
            }

            passed &= status == "passed";
            if (cancelled)
            {
                token.ThrowIfCancellationRequested();
            }

            requireSettledProcessOwnership();
        }

        return passed;
    }

    internal JsonObject ReadChecks(
        JsonObject arguments,
        Action<SqliteConnection, long> ensureChangeExists
    )
    {
        Fields(arguments, "id", "changeId");
        bool byChange = arguments.ContainsKey("changeId");
        if (arguments.ContainsKey("id") == byChange)
        {
            throw Invalid("Exactly one of id or changeId is required.");
        }

        long id = Integer(arguments, byChange ? "changeId" : "id");
        using SqliteConnection connection = database.Open();
        if (byChange)
        {
            ensureChangeExists(connection, id);
            return Operation.Success(new { items = ListForChange(connection, null, id) });
        }

        using SqliteCommand command = Sql.Command(
            connection,
            CheckColumns + " WHERE id=$id ORDER BY id;",
            null,
            ("$id", id)
        );
        JsonArray items = ReadRows(command);
        if (items.Count == 0)
        {
            throw new OperationException("not_found", "Check does not exist.");
        }

        return Operation.Success(new { items });
    }

    internal static JsonArray ListAll(SqliteConnection connection)
    {
        using SqliteCommand command = Sql.Command(connection, CheckColumns + " ORDER BY id;");
        return ReadRows(command);
    }

    internal static JsonArray ListForChange(
        SqliteConnection connection,
        SqliteTransaction? transaction,
        long changeId
    )
    {
        using SqliteCommand command = Sql.Command(
            connection,
            CheckColumns + " WHERE change_id=$id ORDER BY id;",
            transaction,
            ("$id", changeId)
        );
        return ReadRows(command);
    }

    private const string CheckColumns = """
        SELECT id,change_id,kind,commit_hash,tree,command_json,status,exit_code,log_path,
            created_at
        FROM checks
        """;

    private static JsonArray ReadRows(SqliteCommand command)
    {
        JsonArray items = [];
        using ObservedSqliteReader reader = Sql.Reader(command);
        while (reader.Read())
        {
            items.Add(
                new JsonObject
                {
                    ["id"] = reader.GetInt64(0),
                    ["changeId"] = reader.IsDBNull(1) ? null : reader.GetInt64(1),
                    ["kind"] = reader.GetString(2),
                    ["commit"] = reader.GetString(3),
                    ["tree"] = reader.GetString(4),
                    ["command"] = JsonNode.Parse(reader.GetString(5)),
                    ["status"] = reader.GetString(6),
                    ["exitCode"] = reader.IsDBNull(7) ? null : reader.GetInt32(7),
                    ["logPath"] = reader.GetString(8),
                    ["createdAt"] = reader.GetString(9),
                }
            );
        }

        return items;
    }
}
