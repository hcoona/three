using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Data.Sqlite;

namespace LiteAgensh;

public sealed record DeliveryBatch(
    long Id,
    string Recipient,
    long[] EventIds,
    string Prompt,
    int Attempt
);

public sealed partial class CollaborationOperations
{
    private readonly RunDatabase database;

    public CollaborationOperations(RunDatabase database)
    {
        ArgumentNullException.ThrowIfNull(database);
        this.database = database;
        using var connection = database.Open();
        using var transaction = Sql.BeginTransaction(connection);
        Sql.Execute(
            connection,
            """
            CREATE TABLE IF NOT EXISTS tasks(
                id INTEGER PRIMARY KEY AUTOINCREMENT, title TEXT NOT NULL, body TEXT NOT NULL,
                state TEXT NOT NULL DEFAULT 'open', author TEXT NOT NULL, closed_by TEXT,
                created_at TEXT NOT NULL, updated_at TEXT NOT NULL);
            CREATE TABLE IF NOT EXISTS task_comments(
                id INTEGER PRIMARY KEY AUTOINCREMENT, task_id INTEGER NOT NULL REFERENCES tasks(id),
                actor TEXT NOT NULL, body TEXT NOT NULL, created_at TEXT NOT NULL);
            CREATE TABLE IF NOT EXISTS messages(
                id INTEGER PRIMARY KEY AUTOINCREMENT, actor TEXT NOT NULL, channel_id TEXT NOT NULL,
                recipient TEXT NOT NULL, body TEXT NOT NULL,
                thread_id INTEGER REFERENCES messages(id),
                mentions TEXT NOT NULL, urgent INTEGER NOT NULL, created_at TEXT NOT NULL);
            CREATE INDEX IF NOT EXISTS messages_channel ON messages(channel_id,id);
            CREATE TABLE IF NOT EXISTS board_history(
                id INTEGER PRIMARY KEY AUTOINCREMENT, actor TEXT NOT NULL, type TEXT NOT NULL,
                content TEXT NOT NULL, detail TEXT, work_label TEXT, expires_at INTEGER,
                created_at TEXT NOT NULL, verified_checkpoint INTEGER NOT NULL,
                invalidated_reason TEXT, local_failures INTEGER NOT NULL DEFAULT 0);
            CREATE TABLE IF NOT EXISTS board_recent(
                slot INTEGER PRIMARY KEY AUTOINCREMENT,
                revision_id INTEGER NOT NULL UNIQUE REFERENCES board_history(id),
                first_at TEXT NOT NULL);
            CREATE TABLE IF NOT EXISTS delivery_batches(
                id INTEGER PRIMARY KEY AUTOINCREMENT, recipient TEXT NOT NULL,
                state TEXT NOT NULL, created_at INTEGER NOT NULL, completed_at INTEGER,
                last_error TEXT);
            CREATE UNIQUE INDEX IF NOT EXISTS one_sending_batch
                ON delivery_batches(recipient) WHERE state='sending';
            CREATE TABLE IF NOT EXISTS events(
                id INTEGER PRIMARY KEY AUTOINCREMENT, recipient TEXT NOT NULL, kind TEXT NOT NULL,
                dedup_key TEXT NOT NULL, payload TEXT NOT NULL,
                state TEXT NOT NULL DEFAULT 'pending',
                attempts INTEGER NOT NULL DEFAULT 0, created_at INTEGER NOT NULL,
                batch_id INTEGER REFERENCES delivery_batches(id), next_retry_at INTEGER,
                sent_at INTEGER, last_error TEXT, UNIQUE(recipient,dedup_key));
            CREATE INDEX IF NOT EXISTS events_recipient_state ON events(recipient,state,id);
            CREATE INDEX IF NOT EXISTS events_batch_state ON events(batch_id,state);
            CREATE TABLE IF NOT EXISTS shown_notices(
                event_id INTEGER NOT NULL REFERENCES events(id), recipient TEXT NOT NULL,
                shown_at INTEGER NOT NULL, PRIMARY KEY(event_id,recipient));
            CREATE TABLE IF NOT EXISTS shown_gists(
                gist_id INTEGER NOT NULL REFERENCES board_history(id), recipient TEXT NOT NULL,
                shown_at INTEGER NOT NULL, PRIMARY KEY(gist_id,recipient));
            CREATE TABLE IF NOT EXISTS batch_gists(
                batch_id INTEGER NOT NULL REFERENCES delivery_batches(id),
                gist_id INTEGER NOT NULL REFERENCES board_history(id),
                PRIMARY KEY(batch_id,gist_id));
            CREATE TABLE IF NOT EXISTS notice_cursors(
                recipient TEXT PRIMARY KEY, gist_id INTEGER NOT NULL);
            CREATE TABLE IF NOT EXISTS notice_leases(
                recipient TEXT PRIMARY KEY, token TEXT NOT NULL, expires_at INTEGER NOT NULL);
            """,
            transaction
        );
        var now = Timestamp;
        Sql.Execute(
            connection,
            """
            INSERT OR IGNORE INTO tasks(id,title,body,author,created_at,updated_at)
            VALUES(1,'Run goal',$goal,'owner',$now,$now);
            """,
            transaction,
            ("$goal", database.Configuration.Goal),
            ("$now", now)
        );
        transaction.Commit();
    }

    private string Timestamp => database.Now.ToString("O", CultureInfo.InvariantCulture);

    public JsonObject Invoke(string actor, string name, JsonObject arguments)
    {
        try
        {
            RequireActor(actor);
            using var diagnosticActor = SqliteDiagnostics.UseActor(actor);
            ArgumentNullException.ThrowIfNull(arguments);
            using var connection = database.Open();
            // SQLite's built-in lower()/NOCASE handles ASCII only.
            connection.CreateFunction<string, string, bool>(
                "lite_contains",
                (text, query) => text.Contains(query, StringComparison.OrdinalIgnoreCase),
                isDeterministic: true
            );
            using var transaction = Sql.BeginTransaction(connection,
                deferred: name is not ("task_write" or "message_send" or "board_publish"));
            var data = Dispatch(connection, transaction, actor, name, arguments);
            transaction.Commit();
            return Operation.Success(data);
        }
        catch (OperationException exception)
        {
            return Operation.Failure(exception.Code, exception.Message);
        }
    }

    public void EnqueueEvent(
        SqliteConnection connection,
        SqliteTransaction transaction,
        string recipient,
        string kind,
        string dedupKey,
        string payload
    )
    {
        RequireActor(recipient);
        ArgumentException.ThrowIfNullOrWhiteSpace(kind);
        ArgumentException.ThrowIfNullOrWhiteSpace(dedupKey);
        ArgumentNullException.ThrowIfNull(payload);
        if (JsonNode.Parse(payload) is not JsonObject)
        {
            throw new ArgumentException(
                "An event payload must be a serialized JSON object.",
                nameof(payload)
            );
        }

        // The caller's domain transaction owns this insert;
        // no connection or transaction is created here.
        Sql.Execute(
            connection,
            """
            INSERT OR IGNORE INTO events(recipient,kind,dedup_key,payload,created_at)
            VALUES($recipient,$kind,$key,$payload,$now);
            """,
            transaction,
            ("$recipient", recipient),
            ("$kind", kind),
            ("$key", dedupKey),
            ("$payload", payload),
            ("$now", database.Now.ToUnixTimeMilliseconds())
        );
    }

    public bool ReopenWorkerClosedRoot()
    {
        using var connection = database.Open();
        using var transaction = Sql.BeginTransaction(connection);
        var root =
            One(connection, transaction, "SELECT state,closed_by FROM tasks WHERE id=1;")
            ?? throw Missing("Task", 1);
        var closedBy = root["closed_by"]?.GetValue<string>();
        if (
            root["state"]!.GetValue<string>() != "closed"
            || closedBy is null
            || closedBy == "owner"
        )
        {
            return false;
        }

        Sql.Execute(
            connection,
            "UPDATE tasks SET state='open',updated_at=$now WHERE id=1;",
            transaction,
            ("$now", Timestamp)
        );
        var payload = new JsonObject
        {
            ["taskId"] = 1,
            ["closedBy"] = closedBy,
            ["urgent"] = true,
            ["summary"] =
                "The coordinator reopened the worker-closed root goal. Only owner "
                    + "closure ends this run.",
        }.ToJsonString(Json.Options);
        foreach (var member in database.Members)
        {
            EnqueueEvent(
                connection,
                transaction,
                member,
                "task_reopened",
                "root-reopened:" + Guid.NewGuid().ToString("N"),
                payload
            );
        }

        transaction.Commit();
        return true;
    }

    private JsonObject Dispatch(
        SqliteConnection connection,
        SqliteTransaction transaction,
        string actor,
        string name,
        JsonObject arguments
    ) =>
        name switch
        {
            "run_info" => RunInfo(connection, transaction, arguments),
            "agent_info" => AgentInfo(connection, transaction, arguments),
            "agent_list" => AgentList(connection, transaction, arguments),
            "task_write" => WriteTask(connection, transaction, actor, arguments),
            "task_read" => ReadTask(connection, transaction, arguments),
            "task_list" => ListTasks(connection, transaction, arguments),
            "work_search" => SearchWork(connection, transaction, arguments),
            "message_send" => SendMessage(connection, transaction, actor, arguments),
            "message_read" or "message_thread" or "message_search" => ReadMessages(
                connection,
                transaction,
                actor,
                name,
                arguments
            ),
            "channel_read" => ReadChannel(actor, arguments),
            "channel_list" => ListChannels(actor, arguments),
            "board_publish" => PublishBoard(connection, transaction, actor, arguments),
            "board_recent" => RecentBoard(connection, transaction, arguments),
            "board_search" => SearchBoard(connection, transaction, arguments),
            "board_expand" => ExpandBoard(connection, transaction, arguments),
            _ => throw new OperationException(
                "unavailable",
                $"Unknown collaboration operation '{name}'."
            ),
        };

    private JsonObject RunInfo(
        SqliteConnection connection,
        SqliteTransaction transaction,
        JsonObject arguments
    )
    {
        Fields(arguments);
        return new JsonObject
        {
            ["goal"] = database.Configuration.Goal,
            ["baseCommit"] =
                Metadata(connection, transaction, "git:base_commit")
                ?? database.Configuration.BaseRevision,
            ["lifecycle"] = Metadata(connection, transaction, "lifecycle") ?? "initialized",
            ["members"] = Array(database.Members),
            ["repositoryPath"] = database.Configuration.RepositoryPath,
            ["statePath"] = database.StatePath,
            ["capabilities"] = Array([
                "run_info",
                "agent_info",
                "agent_list",
                "task_write",
                "task_read",
                "task_list",
                "work_search",
                "message_send",
                "message_read",
                "message_thread",
                "message_search",
                "channel_read",
                "channel_list",
                "board_publish",
                "board_recent",
                "board_search",
                "board_expand",
                "change_write",
                "change_read",
                "change_list",
                "change_review",
                "check_read",
            ]),
        };
    }

    private JsonObject AgentInfo(
        SqliteConnection connection,
        SqliteTransaction transaction,
        JsonObject arguments
    )
    {
        Fields(arguments, "id");
        var id = Text(arguments, "id");
        RequireKnownMember(id);
        return Member(connection, transaction, id);
    }

    private JsonObject AgentList(
        SqliteConnection connection,
        SqliteTransaction transaction,
        JsonObject arguments
    )
    {
        Fields(arguments, "id");
        var id = OptionalText(arguments, "id");
        if (id is not null)
        {
            RequireKnownMember(id);
        }

        return new JsonObject
        {
            ["items"] = new JsonArray(
                database
                    .Members.Where(member => id is null || member == id)
                    .Select(member => (JsonNode)Member(connection, transaction, member))
                    .ToArray()
            ),
        };
    }

    private static JsonObject Member(
        SqliteConnection connection,
        SqliteTransaction transaction,
        string id
    ) =>
        new()
        {
            ["id"] = id,
            ["worktreePath"] = Metadata(connection, transaction, $"worktree:{id}"),
            ["lifecycle"] =
                Metadata(
                    connection,
                    transaction,
                    id == "owner" ? "lifecycle" : $"worker:{id}:lifecycle"
                ) ?? "initialized",
        };

    private JsonObject WriteTask(
        SqliteConnection connection,
        SqliteTransaction transaction,
        string actor,
        JsonObject arguments
    )
    {
        var action = Text(arguments, "action");
        long id;
        switch (action)
        {
            case "create":
                Fields(arguments, "action", "title", "body");
                var title = Text(arguments, "title");
                var body = Text(arguments, "body");
                id = Insert(
                    connection,
                    transaction,
                    """
                    INSERT INTO tasks(title,body,author,created_at,updated_at)
                    VALUES($title,$body,$actor,$now,$now);
                    """,
                    ("$title", title),
                    ("$body", body),
                    ("$actor", actor),
                    ("$now", Timestamp)
                );
                break;
            case "update":
                Fields(arguments, "action", "id", "title", "body");
                id = Integer(arguments, "id");
                _ = Task(connection, transaction, id);
                var updatedTitle = OptionalText(arguments, "title");
                var updatedBody = OptionalText(arguments, "body");
                if (updatedTitle is null && updatedBody is null)
                {
                    throw Invalid("An update requires title or body.");
                }

                Sql.Execute(
                    connection,
                    """
                    UPDATE tasks SET title=COALESCE($title,title),body=COALESCE($body,body),
                        updated_at=$now WHERE id=$id;
                    """,
                    transaction,
                    ("$title", updatedTitle),
                    ("$body", updatedBody),
                    ("$now", Timestamp),
                    ("$id", id)
                );
                break;
            case "comment":
                Fields(arguments, "action", "id", "body");
                id = Integer(arguments, "id");
                var comment = Text(arguments, "body");
                _ = Task(connection, transaction, id);
                Sql.Execute(
                    connection,
                    "INSERT INTO task_comments(task_id,actor,body,created_at) "
                        + "VALUES($id,$actor,$body,$now);",
                    transaction,
                    ("$id", id),
                    ("$actor", actor),
                    ("$body", comment),
                    ("$now", Timestamp)
                );
                Sql.Execute(
                    connection,
                    "UPDATE tasks SET updated_at=$now WHERE id=$id;",
                    transaction,
                    ("$now", Timestamp),
                    ("$id", id)
                );
                break;
            case "close":
                Fields(arguments, "action", "id");
                id = Integer(arguments, "id");
                var task = Task(connection, transaction, id);
                if (task["state"]!.GetValue<string>() == "closed")
                {
                    return task;
                }

                Sql.Execute(
                    connection,
                    "UPDATE tasks SET state='closed',closed_by=$actor,updated_at=$now "
                        + "WHERE id=$id;",
                    transaction,
                    ("$actor", actor),
                    ("$now", Timestamp),
                    ("$id", id)
                );
                if (id == 1)
                {
                    Sql.Execute(
                        connection,
                        """
                        INSERT INTO meta(key,value) VALUES('lifecycle:root_closed_by',$actor)
                        ON CONFLICT(key) DO UPDATE SET value=excluded.value;
                        """,
                        transaction,
                        ("$actor", actor)
                    );
                }

                break;
            default:
                throw Invalid("Unknown task action.");
        }

        var result = Task(connection, transaction, id);
        var payload = new JsonObject
        {
            ["taskId"] = id,
            ["action"] = action,
            ["actor"] = actor,
            ["urgent"] = actor == "owner",
            ["summary"] =
                $"{actor} {action} task #{id}: "
                + $"{(action == "comment" ? Text(arguments, "body")
                    : result["title"]!.GetValue<string>())}",
        };
        BoundText(payload, "summary");
        Broadcast(
            connection,
            transaction,
            actor,
            "task",
            $"task:{id}:{action}:{Guid.NewGuid():N}",
            payload
        );
        return result;
    }

    private JsonObject ReadTask(
        SqliteConnection connection,
        SqliteTransaction transaction,
        JsonObject arguments
    )
    {
        Fields(arguments, "id");
        return Task(connection, transaction, Integer(arguments, "id"));
    }

    private JsonObject ListTasks(
        SqliteConnection connection,
        SqliteTransaction transaction,
        JsonObject arguments
    )
    {
        Fields(arguments, "state", "cursor", "limit");
        var state = OptionalText(arguments, "state");
        if (state is not null and not ("open" or "closed"))
        {
            throw Invalid("Task state must be open or closed.");
        }

        var (cursor, limit) = Pagination(arguments);
        var rows = Rows(
            connection,
            transaction,
            """
            SELECT id FROM tasks WHERE id>$cursor AND ($state IS NULL OR state=$state)
            ORDER BY id LIMIT $limit;
            """,
            ("$cursor", cursor),
            ("$state", state),
            ("$limit", limit + 1)
        );
        return Page(rows.Select(row => Task(connection, transaction, Id(row))).ToList(), limit);
    }

    private JsonObject Task(SqliteConnection connection, SqliteTransaction transaction, long id)
    {
        var result =
            One(
                connection,
                transaction,
                """
                SELECT id,title,body,state,author,closed_by AS closedBy,
                    created_at AS createdAt,updated_at AS updatedAt
                FROM tasks WHERE id=$id;
                """,
                ("$id", id)
            ) ?? throw Missing("Task", id);
        result["comments"] = new JsonArray(
            Rows(
                    connection,
                    transaction,
                    """
                    SELECT id,actor,body,created_at AS createdAt FROM task_comments
                    WHERE task_id=$id ORDER BY id;
                    """,
                    ("$id", id)
                )
                .Select(row => (JsonNode)row)
                .ToArray()
        );
        var workers = database.Configuration.WorkerIds.ToHashSet(StringComparer.Ordinal);
        var claimants = Rows(
                connection,
                transaction,
                """
                SELECT h.actor,h.content FROM board_history h
                JOIN board_recent r ON r.revision_id=h.id
                WHERE h.type='CLAIM' AND h.expires_at > $now ORDER BY h.id;
                """,
                ("$now", database.Now.ToUnixTimeMilliseconds())
            )
            .Where(row =>
                workers.Contains(row["actor"]!.GetValue<string>())
                && BoardPolicy.CitesTask(row["content"]!.GetValue<string>(), id)
            )
            .Select(row => row["actor"]!.GetValue<string>())
            .Distinct(StringComparer.Ordinal)
            .Order(StringComparer.Ordinal);
        result["assignees"] = Array(claimants);
        return result;
    }

    private static JsonObject SearchWork(
        SqliteConnection connection,
        SqliteTransaction transaction,
        JsonObject arguments
    )
    {
        Fields(arguments, "query", "cursor", "limit");
        var query = Text(arguments, "query");
        var (cursor, limit) = Pagination(arguments);
        var hasChanges =
            Sql.Scalar(
                connection,
                "SELECT name FROM sqlite_master WHERE type='table' AND name='changes';",
                transaction
            ) is string;
        // A composite numeric cursor retains both kinds even when their independent IDs coincide.
        var union = """
            SELECT 'task' AS kind,id,title,state,id*2 AS pageKey FROM tasks
            WHERE lite_contains(title||char(10)||body,$query)
            """;
        if (hasChanges)
        {
            union += """

                UNION ALL SELECT 'change' AS kind,id,title,state,id*2+1 AS pageKey FROM changes
                WHERE lite_contains(title||char(10)||body,$query)
                """;
        }

        var rows = Rows(
            connection,
            transaction,
            $"SELECT * FROM ({union}) WHERE pageKey>$cursor ORDER BY pageKey LIMIT $limit;",
            ("$query", query),
            ("$cursor", cursor),
            ("$limit", limit + 1)
        );
        var more = rows.Count > limit;
        if (more)
        {
            rows.RemoveAt(rows.Count - 1);
        }

        var next = more ? rows[^1]["pageKey"]!.GetValue<long>() : (long?)null;
        foreach (var row in rows)
        {
            row.Remove("pageKey");
        }

        return new JsonObject
        {
            ["items"] = new JsonArray(rows.Select(row => (JsonNode)row).ToArray()),
            ["nextCursor"] = next,
        };
    }

    private void Broadcast(
        SqliteConnection connection,
        SqliteTransaction transaction,
        string actor,
        string kind,
        string key,
        JsonObject payload
    )
    {
        foreach (var recipient in database.Members.Where(member => member != actor))
        {
            EnqueueEvent(
                connection,
                transaction,
                recipient,
                kind,
                key,
                payload.ToJsonString(Json.Options)
            );
        }
    }

    private void RequireActor(string actor)
    {
        if (!database.Members.Contains(actor, StringComparer.Ordinal))
        {
            throw new OperationException(
                "unknown_actor",
                "The bound actor is not a member of this run."
            );
        }
    }

    private void RequireKnownMember(string id)
    {
        if (!database.Members.Contains(id, StringComparer.Ordinal))
        {
            throw new OperationException("not_found", $"Unknown member '{id}'.");
        }
    }

    private static string? Metadata(
        SqliteConnection connection,
        SqliteTransaction transaction,
        string key
    ) =>
        Sql.Scalar(connection, "SELECT value FROM meta WHERE key=$key;", transaction, ("$key", key))
        as string;

    private static long Insert(
        SqliteConnection connection,
        SqliteTransaction transaction,
        string sql,
        params (string Name, object? Value)[] parameters
    )
    {
        Sql.Execute(connection, sql, transaction, parameters);
        return (long)Sql.Scalar(connection, "SELECT last_insert_rowid();", transaction)!;
    }

    private static List<JsonObject> Rows(
        SqliteConnection connection,
        SqliteTransaction transaction,
        string sql,
        params (string Name, object? Value)[] parameters
    )
    {
        using var command = Sql.Command(connection, sql, transaction, parameters);
        using var reader = Sql.Reader(command);
        var rows = new List<JsonObject>();
        while (reader.Read())
        {
            var row = new JsonObject();
            for (var index = 0; index < reader.FieldCount; index++)
            {
                row[reader.GetName(index)] = reader.IsDBNull(index)
                    ? null
                    : JsonValue.Create(reader.GetValue(index));
            }

            rows.Add(row);
        }

        return rows;
    }

    private static JsonObject? One(
        SqliteConnection connection,
        SqliteTransaction transaction,
        string sql,
        params (string Name, object? Value)[] parameters
    ) => Rows(connection, transaction, sql, parameters).FirstOrDefault();

    private static long Id(JsonObject row) => row["id"]!.GetValue<long>();

    private static JsonArray Array(IEnumerable<string> values) =>
        new(values.Select(value => (JsonNode?)JsonValue.Create(value)).ToArray());

    private static JsonObject Page(List<JsonObject> rows, int limit)
    {
        var more = rows.Count > limit;
        if (more)
        {
            rows.RemoveAt(rows.Count - 1);
        }

        return new JsonObject
        {
            ["items"] = new JsonArray(rows.Select(row => (JsonNode)row).ToArray()),
            ["nextCursor"] = more ? Id(rows[^1]) : null,
        };
    }

    private static void Fields(JsonObject arguments, params string[] allowed)
    {
        foreach (var field in arguments)
        {
            if (!allowed.Contains(field.Key, StringComparer.Ordinal))
            {
                throw Invalid($"Unknown field '{field.Key}'.");
            }
        }
    }

    private static string Text(JsonObject arguments, string field)
    {
        if (
            arguments[field] is not JsonValue value
            || !value.TryGetValue<string>(out var text)
            || string.IsNullOrWhiteSpace(text)
        )
        {
            throw Invalid($"'{field}' must be a nonempty string.");
        }

        return text;
    }

    private static string? OptionalText(JsonObject arguments, string field, bool allowEmpty = false)
    {
        if (!arguments.ContainsKey(field))
        {
            return null;
        }

        if (!allowEmpty)
        {
            return Text(arguments, field);
        }

        if (arguments[field] is not JsonValue value || !value.TryGetValue<string>(out var text))
        {
            throw Invalid($"'{field}' must be a string.");
        }

        return text;
    }

    private static long Integer(JsonObject arguments, string field, long minimum = 1)
    {
        if (
            arguments[field] is not JsonValue value
            || value.GetValueKind() != JsonValueKind.Number
            || !JsonSerializer.SerializeToElement(value, Json.Options).TryGetInt64(out var number)
            || number < minimum
        )
        {
            throw Invalid($"'{field}' must be a JSON integer >= {minimum}.");
        }

        return number;
    }

    private static bool Boolean(JsonObject arguments, string field)
    {
        if (!arguments.ContainsKey(field))
        {
            return false;
        }

        if (arguments[field] is not JsonValue value || !value.TryGetValue<bool>(out var boolean))
        {
            throw Invalid($"'{field}' must be a boolean.");
        }

        return boolean;
    }

    private static string[] Strings(JsonObject arguments, string field, bool required = false)
    {
        if (!arguments.ContainsKey(field) && !required)
        {
            return [];
        }

        if (arguments[field] is not JsonArray array)
        {
            throw Invalid($"'{field}' must be an array of strings.");
        }

        return array
            .Select(node =>
            {
                if (node is not JsonValue value || !value.TryGetValue<string>(out var text))
                {
                    throw Invalid($"'{field}' must contain strings.");
                }

                return text;
            })
            .ToArray();
    }

    private static (long Cursor, int Limit) Pagination(JsonObject arguments) =>
        (arguments.ContainsKey("cursor") ? Integer(arguments, "cursor", 0) : 0, Limit(arguments));

    private static int Limit(JsonObject arguments)
    {
        var limit = arguments.ContainsKey("limit") ? Integer(arguments, "limit") : 50;
        if (limit > 200)
        {
            throw Invalid("'limit' must be between 1 and 200.");
        }

        return (int)limit;
    }

    private static OperationException Invalid(string message) => new("invalid_arguments", message);

    private static OperationException Missing(string kind, long id) =>
        new("not_found", $"{kind} {id} does not exist.");
}
