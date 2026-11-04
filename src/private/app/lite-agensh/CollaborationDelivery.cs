using System.Globalization;
using System.Text;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using Microsoft.Data.Sqlite;

namespace LiteAgensh;

public sealed partial class CollaborationOperations
{
    private static readonly int[] RetrySeconds = [1, 5, 30, 120];

    public void QueueKickoff(string recipient, string reason)
    {
        RequireWorker(recipient);
        ArgumentException.ThrowIfNullOrWhiteSpace(reason);
        using var connection = database.Open();
        using var transaction = connection.BeginTransaction();
        EnqueueEvent(connection, transaction, recipient, "kickoff", "kickoff:" + Guid.NewGuid().ToString("N"),
            new JsonObject { ["summary"] = reason, ["reason"] = reason }.ToJsonString(Json.Options));
        transaction.Commit();
    }

    public DeliveryBatch? ClaimBatch(string recipient)
    {
        RequireWorker(recipient);
        using var connection = database.Open();
        using var transaction = connection.BeginTransaction();
        if (Sql.Scalar(connection, """
            SELECT id FROM delivery_batches WHERE recipient=$recipient AND state='sending';
            """, transaction, ("$recipient", recipient)) is not null)
        {
            return null;
        }

        var now = database.Now.ToUnixTimeMilliseconds();
        var candidates = """
            e.recipient=$recipient AND s.event_id IS NULL AND e.attempts<5
                AND (e.state='pending' OR (e.state IN ('failed','retrying') AND e.next_retry_at <= $now))
                AND (e.kind!='board' OR EXISTS(
                    SELECT 1 FROM board_history h JOIN board_recent r ON r.revision_id=h.id
                    WHERE h.id=json_extract(e.payload,'$.gistId')
                        AND (h.expires_at IS NULL OR h.expires_at >= $now)
                        AND NOT EXISTS(SELECT 1 FROM shown_gists g WHERE g.gist_id=h.id AND g.recipient=e.recipient)))
            """;
        var rows = Rows(connection, transaction, $"""
            SELECT * FROM (
            SELECT e.id,e.kind,e.payload,e.attempts,e.recipient,e.created_at AS createdAt FROM events e
            LEFT JOIN shown_notices s ON s.event_id=e.id AND s.recipient=e.recipient
            WHERE {candidates} AND e.kind!='group' ORDER BY e.id LIMIT 32)
            UNION ALL
            SELECT e.id,e.kind,e.payload,e.attempts,e.recipient,e.created_at AS createdAt FROM events e
            LEFT JOIN shown_notices s ON s.event_id=e.id AND s.recipient=e.recipient
            WHERE {candidates} AND e.kind='group'
            ORDER BY id;
            """, ("$recipient", recipient), ("$now", now));
        if (rows.Count == 0)
        {
            transaction.Commit();
            return null;
        }

        var batch = Insert(connection, transaction, """
            INSERT INTO delivery_batches(recipient,state,created_at) VALUES($recipient,'sending',$now);
            """, ("$recipient", recipient), ("$now", now));
        foreach (var row in rows)
        {
            if (row["kind"]!.GetValue<string>() == "board")
            {
                var payload = Payload(row["payload"]!.GetValue<string>());
                var gist = One(connection, transaction, BoardSelect + " WHERE h.id=$id;",
                    ("$id", payload["gistId"]!.GetValue<long>()))!;
                payload["gist"] = BoardRevision(gist);
                row["payload"] = payload.ToJsonString(Json.Options);
            }

            Sql.Execute(connection, """
                UPDATE events SET state='sending',attempts=attempts+1,batch_id=$batch,last_error=NULL,next_retry_at=NULL
                WHERE id=$id;
                """, transaction, ("$batch", batch), ("$id", Id(row)));
        }

        var context = SharedContext(connection, transaction, recipient);
        // Ordinary queued messages arrive in their ordered batch, not again in the board prefix.
        context["messages"] = new JsonArray();
        context["limits"]!["messages"] = 0;
        var eventGists = rows.Where(row => row["kind"]!.GetValue<string>() == "board")
            .Select(row => Payload(row["payload"]!.GetValue<string>())["gistId"]!.GetValue<long>()).ToHashSet();
        var contextGists = context["board"]!.AsArray();
        for (var index = contextGists.Count - 1; index >= 0; index--)
        {
            if (eventGists.Contains(Id(contextGists[index]!.AsObject())))
            {
                contextGists.RemoveAt(index);
            }
        }

        foreach (var gist in contextGists.OfType<JsonObject>())
        {
            eventGists.Add(Id(gist));
        }

        foreach (var gist in eventGists)
        {
            Sql.Execute(connection, "INSERT INTO batch_gists(batch_id,gist_id) VALUES($batch,$gist);",
                transaction, ("$batch", batch), ("$gist", gist));
        }

        transaction.Commit();
        string prompt;
        try
        {
            prompt = new JsonObject
            {
                ["recipient"] = recipient,
                ["sharedContext"] = context,
                ["events"] = PromptEvents(rows, batch, recipient),
                ["deliverySemantics"] = "At-least-once delivery: interrupted turns may replay. Select work with peers; claims are advisory.",
            }.ToJsonString(Json.Options);
        }
        catch (IOException exception)
        {
            FailBatch(batch, exception.Message);
            throw;
        }
        catch (UnauthorizedAccessException exception)
        {
            FailBatch(batch, exception.Message);
            throw;
        }

        return new DeliveryBatch(batch, recipient, rows.Select(Id).ToArray(), prompt,
            checked((int)rows.Max(row => row["attempts"]!.GetValue<long>()) + 1));
    }

    public void CompleteBatch(long batchId)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(batchId);

        using var connection = database.Open();
        using var transaction = connection.BeginTransaction();
        var batch = One(connection, transaction, "SELECT state,recipient FROM delivery_batches WHERE id=$id;", ("$id", batchId))
            ?? throw Missing("Delivery batch", batchId);
        if (batch["state"]!.GetValue<string>() == "sent")
        {
            return;
        }

        if (batch["state"]!.GetValue<string>() != "sending")
        {
            throw new OperationException("conflict", "Only a sending batch can complete.");
        }

        var recipient = batch["recipient"]!.GetValue<string>();
        var now = database.Now.ToUnixTimeMilliseconds();
        var rows = Rows(connection, transaction, "SELECT id FROM events WHERE batch_id=$id AND state='sending';", ("$id", batchId));
        foreach (var row in rows)
        {
            Show(connection, transaction, recipient, Id(row), now);
        }

        Sql.Execute(connection, """
            INSERT OR IGNORE INTO shown_gists(gist_id,recipient,shown_at)
                SELECT gist_id,$recipient,$now FROM batch_gists WHERE batch_id=$id;
            INSERT OR IGNORE INTO shown_notices(event_id,recipient,shown_at)
                SELECT e.id,e.recipient,$now FROM events e JOIN batch_gists g ON g.batch_id=$id
                    AND g.gist_id=json_extract(e.payload,'$.gistId')
                WHERE e.recipient=$recipient AND e.kind='board' AND e.state='pending';
            """, transaction, ("$id", batchId), ("$recipient", recipient), ("$now", now));
        Sql.Execute(connection, """
            UPDATE events SET state='sent',sent_at=$now WHERE batch_id=$id AND state='sending';
            UPDATE delivery_batches SET state='sent',completed_at=$now WHERE id=$id AND state='sending';
            """, transaction, ("$id", batchId), ("$now", now));
        transaction.Commit();
    }

    public void FailBatch(long batchId, string error)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(batchId);

        ArgumentNullException.ThrowIfNull(error);
        using var connection = database.Open();
        using var transaction = connection.BeginTransaction();
        var batch = One(connection, transaction, "SELECT state FROM delivery_batches WHERE id=$id;", ("$id", batchId))
            ?? throw Missing("Delivery batch", batchId);
        if (batch["state"]!.GetValue<string>() != "sending")
        {
            return;
        }

        var now = database.Now.ToUnixTimeMilliseconds();
        foreach (var row in Rows(connection, transaction,
            "SELECT id,attempts FROM events WHERE batch_id=$id AND state='sending';", ("$id", batchId)))
        {
            var attempts = checked((int)row["attempts"]!.GetValue<long>());
            long? next = attempts >= 5 ? null : now + RetrySeconds[attempts - 1] * 1000;
            Sql.Execute(connection, """
                UPDATE events SET state='failed',last_error=$error,next_retry_at=$next WHERE id=$id AND state='sending';
                """, transaction, ("$error", error), ("$next", next), ("$id", Id(row)));
        }

        Sql.Execute(connection, "UPDATE delivery_batches SET state='failed',last_error=$error WHERE id=$id;",
            transaction, ("$error", error), ("$id", batchId));
        transaction.Commit();
    }

    public void RecoverInterrupted()
    {
        using var connection = database.Open();
        using var transaction = connection.BeginTransaction();
        // Claims count attempts before prompting, including interrupted attempts.
        Sql.Execute(connection, """
            UPDATE events SET state=CASE WHEN attempts>=5 THEN 'failed' ELSE 'pending' END,
                next_retry_at=NULL,last_error=COALESCE(last_error,'Recovered interrupted sending batch'),batch_id=NULL
                WHERE state='sending';
            UPDATE delivery_batches SET state='interrupted',last_error='Recovered interrupted sending batch'
                WHERE state='sending';
            """, transaction);
        Sql.Execute(connection, "DELETE FROM notice_leases WHERE expires_at<=$now;",
            transaction, ("$now", database.Now.ToUnixTimeMilliseconds()));
        transaction.Commit();
    }

    public JsonObject ReadSharedContext(string actor)
    {
        RequireActor(actor);
        using var connection = database.Open();
        using var transaction = connection.BeginTransaction();
        var result = SharedContext(connection, transaction, actor);
        transaction.Commit();
        return result;
    }

    private JsonObject SharedContext(SqliteConnection connection, SqliteTransaction transaction, string actor)
    {
        var tasks = Rows(connection, transaction, """
            SELECT id,title,body,state FROM tasks WHERE state='open' ORDER BY id LIMIT 50;
            """);
        foreach (var task in tasks)
        {
            BoundText(task, "title");
            BoundText(task, "body");
        }
        var messages = Rows(connection, transaction, MessageSelect + """

            WHERE $actor='owner' OR channel_id='team' OR actor=$actor OR recipient=$actor
            ORDER BY id DESC LIMIT 50;
            """, ("$actor", actor));
        messages.Reverse();
        foreach (var message in messages)
        {
            BoundText(message, "body");
        }

        var board = RecentRevisions(connection, transaction, 50, actor);
        foreach (var gist in board.OfType<JsonObject>())
        {
            GistOnly(gist);
        }

        return new JsonObject
        {
            ["actor"] = actor,
            ["snapshotAt"] = Timestamp,
            ["goal"] = database.Configuration.Goal,
            ["members"] = Array(database.Members),
            ["tasks"] = new JsonArray(tasks.Select(row => (JsonNode)row).ToArray()),
            ["messages"] = new JsonArray(messages.Select(row => (JsonNode)Message(row)).ToArray()),
            ["board"] = board,
            ["limits"] = new JsonObject { ["tasks"] = 50, ["messages"] = 50, ["board"] = 50, ["textChars"] = 4000 },
            ["cooperation"] = """
                Read the repository's instructions before selecting work. Choose and finish work with equal peers, not a central scheduler.
                Claims are advisory, expire and do not exclude other claimants; settle editing collisions by direct message.
                Cite #<task-id> in CLAIM content to appear as an advisory task assignee; workLabel links patch revisions, not ownership.
                Share FACT for confirmations, OBSERVED for reference behavior, FAIL for falsified hypotheses, CLAIM for current work,
                and PATCH_SUMMARY as files= | idea= | evidence= for candidate fixes. Do not rederive a peer's FACT or retry its FAIL.
                Put long evidence in detail; expand G-labels only as needed. Events report facts: deciding what to do remains yours.
                Text admission of evidence is not check execution. Submit exact candidates and obtain required peer review.
                Ask the coordinator to integrate; do not merge shared delivery refs yourself or start recursive agents.
                Direct/urgent messages and peer gists appear only at infrastructure tool-result boundaries, not arbitrary interrupts.
                Task closure or a completed native turn does not prove delivery checks passed. Use task/message/board reads for full history.
                """,
        };
    }

    public JsonObject AddNotices(string actor, JsonObject result)
    {
        RequireActor(actor);
        ArgumentNullException.ThrowIfNull(result);
        var envelopeResult = (JsonObject)result.DeepClone();
        using var connection = database.Open();
        using var transaction = connection.BeginTransaction();
        var now = database.Now.ToUnixTimeMilliseconds();
        var token = Guid.NewGuid().ToString("N");
        var acquired = Sql.Execute(connection, """
            INSERT INTO notice_leases(recipient,token,expires_at) VALUES($actor,$token,$until)
            ON CONFLICT(recipient) DO UPDATE SET token=excluded.token,expires_at=excluded.expires_at
                WHERE notice_leases.expires_at <= $now;
            """, transaction, ("$actor", actor), ("$token", token), ("$until", now + 30000), ("$now", now));
        var notices = new JsonArray();
        if (acquired == 0)
        {
            envelopeResult["notices"] = new JsonObject { ["recipient"] = actor, ["items"] = notices, ["snapshotAt"] = Timestamp };
            transaction.Commit();
            return envelopeResult;
        }

        var events = Rows(connection, transaction, """
            SELECT e.id,e.kind,e.payload FROM events e
            LEFT JOIN shown_notices s ON s.event_id=e.id AND s.recipient=e.recipient
            WHERE e.recipient=$actor AND e.state='pending' AND s.event_id IS NULL
                AND e.kind!='board' AND (e.kind='dm' OR json_extract(e.payload,'$.urgent')=1)
            ORDER BY e.id LIMIT 50;
            """, ("$actor", actor));
        foreach (var row in events)
        {
            var kind = row["kind"]!.GetValue<string>();
            var payload = Payload(row["payload"]!.GetValue<string>());
            notices.Add(new JsonObject
            {
                ["id"] = "e" + Id(row).ToString(CultureInfo.InvariantCulture),
                ["kind"] = kind,
                ["data"] = payload,
            });
            Show(connection, transaction, actor, Id(row), now);
        }

        var cursor = (long?)Sql.Scalar(connection, "SELECT gist_id FROM notice_cursors WHERE recipient=$actor;",
            transaction, ("$actor", actor)) ?? 0;
        // Ascending IDs ensure a bounded envelope cannot advance past a peer gist it did not display.
        var gists = Rows(connection, transaction, BoardSelect + """

            JOIN board_recent r ON r.revision_id=h.id WHERE h.actor!=$actor
                AND (h.id>$cursor OR EXISTS(
                    SELECT 1 FROM events e WHERE e.recipient=$actor AND e.kind='board'
                        AND json_extract(e.payload,'$.gistId')=h.id AND e.state!='sent'))
                AND (h.expires_at IS NULL OR h.expires_at >= $now)
                AND NOT EXISTS(SELECT 1 FROM shown_gists g WHERE g.gist_id=h.id AND g.recipient=$actor)
                AND NOT EXISTS(SELECT 1 FROM batch_gists g JOIN delivery_batches b ON b.id=g.batch_id
                    WHERE g.gist_id=h.id AND b.recipient=$actor AND b.state='sending')
            ORDER BY h.id LIMIT 50;
            """, ("$cursor", cursor), ("$actor", actor), ("$now", now));
        foreach (var row in gists)
        {
            var id = Id(row);
            notices.Add(new JsonObject { ["id"] = "g" + id.ToString(CultureInfo.InvariantCulture), ["kind"] = "board", ["data"] = GistOnly(BoardRevision(row)) });
            Sql.Execute(connection, "INSERT OR IGNORE INTO shown_gists(gist_id,recipient,shown_at) VALUES($gist,$actor,$now);",
                transaction, ("$gist", id), ("$actor", actor), ("$now", now));
            AdvanceCursor(connection, transaction, actor, id);
            Sql.Execute(connection, """
                INSERT OR IGNORE INTO shown_notices(event_id,recipient,shown_at)
                SELECT id,recipient,$now FROM events WHERE recipient=$actor AND kind='board'
                    AND json_extract(payload,'$.gistId')=$gist AND state='pending';
                """, transaction, ("$now", now), ("$actor", actor), ("$gist", id));
        }

        // Shown receipts suppress redundant queue delivery; they are not native-turn acknowledgements.
        envelopeResult["notices"] = new JsonObject
        {
            ["items"] = notices,
            ["recipient"] = actor,
            ["snapshotAt"] = Timestamp,
            ["leaseToken"] = token,
        };
        Sql.Execute(connection, "DELETE FROM notice_leases WHERE recipient=$actor AND token=$token;",
            transaction, ("$actor", actor), ("$token", token));
        transaction.Commit();
        return envelopeResult;
    }

    private void RequireWorker(string recipient)
    {
        RequireActor(recipient);
        if (recipient == "owner")
        {
            throw Invalid("Only a configured worker can receive a native turn.");
        }
    }

    private static void Show(SqliteConnection connection, SqliteTransaction transaction, string recipient, long id, long now) =>
        Sql.Execute(connection, """
            INSERT OR IGNORE INTO shown_notices(event_id,recipient,shown_at) VALUES($id,$recipient,$now);
            """, transaction, ("$id", id), ("$recipient", recipient), ("$now", now));

    private static void AdvanceCursor(SqliteConnection connection, SqliteTransaction transaction, string recipient, long gist) =>
        Sql.Execute(connection, """
            INSERT INTO notice_cursors(recipient,gist_id) VALUES($recipient,$gist)
            ON CONFLICT(recipient) DO UPDATE SET gist_id=MAX(notice_cursors.gist_id,excluded.gist_id);
            """, transaction, ("$recipient", recipient), ("$gist", gist));

    private static JsonObject Payload(string payload) =>
        JsonNode.Parse(payload)?.AsObject() ?? throw new InvalidOperationException("Persisted event payload is not an object.");

    private static JsonObject GistOnly(JsonObject gist)
    {
        gist["hasDetail"] = gist["detail"] is JsonValue detail && detail.GetValue<string>().Length > 0;
        gist.Remove("detail");
        return gist;
    }

    private static void BoundText(JsonObject row, string field)
    {
        var text = row[field]!.GetValue<string>();
        if (text.Length > 4000)
        {
            var runes = text.EnumerateRunes().Take(4001).ToArray();
            if (runes.Length > 4000)
            {
                row[field] = string.Concat(runes.Take(4000).Select(rune => rune.ToString()));
                row[field + "Truncated"] = true;
            }
        }
    }

    private JsonArray PromptEvents(List<JsonObject> rows, long batch, string recipient)
    {
        var group = rows.Where(row => row["kind"]!.GetValue<string>() == "group").ToArray();
        var included = rows;
        var events = new JsonArray();
        if (group.Length > 32)
        {
            var directory = Path.Combine(database.StatePath, "delivery");
            Directory.CreateDirectory(directory);
            var path = Path.Combine(directory, $"group-messages-{batch.ToString(CultureInfo.InvariantCulture)}.jsonl");
            using (var stream = new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.Read, 4096, FileOptions.WriteThrough))
            {
                using (var writer = new StreamWriter(stream, new UTF8Encoding(false), leaveOpen: true))
                {
                    foreach (var row in group)
                    {
                        writer.WriteLine(PromptEvent(row, bound: false).ToJsonString(Json.Options));
                    }

                    writer.Flush();
                }

                stream.Flush(flushToDisk: true);
            }

            var latest = group.TakeLast(32).Select(Id).ToHashSet();
            included = rows.Where(row => row["kind"]!.GetValue<string>() != "group" || latest.Contains(Id(row))).ToList();
            var mention = new Regex(@"(?<![a-zA-Z0-9_.@-])@" + Regex.Escape(recipient) + @"(?![a-zA-Z0-9_-]|\.[a-zA-Z0-9_.-])",
                RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1));
            var mentions = group.SkipLast(32).Sum(row =>
            {
                var payload = Payload(row["payload"]!.GetValue<string>());
                var body = payload["message"]?["body"]?.GetValue<string>() ?? payload["summary"]?.GetValue<string>() ?? "";
                return mention.Count(body);
            });
            events.Add(new JsonObject
            {
                ["kind"] = "group_message_history",
                ["path"] = path,
                ["total"] = group.Length,
                ["earlier"] = group.Length - 32,
                ["mentions"] = mentions,
                ["summary"] = "Latest 32 group messages follow. Full queued group text is retained in the JSONL file; search earlier messages for @" + recipient + ".",
            });
        }

        foreach (var row in included)
        {
            events.Add(PromptEvent(row));
        }

        return events;
    }

    private static JsonObject PromptEvent(JsonObject row, bool bound = true)
    {
        var payload = Payload(row["payload"]!.GetValue<string>());
        if (row["kind"]!.GetValue<string>() == "board" && payload["gist"] is JsonObject gist)
        {
            GistOnly(gist);
        }

        if (bound)
        {
            if (payload["summary"] is JsonValue)
            {
                BoundText(payload, "summary");
            }

            if (payload["message"] is JsonObject message)
            {
                BoundText(message, "body");
            }
        }

        return new JsonObject
        {
            ["id"] = Id(row),
            ["kind"] = row["kind"]!.GetValue<string>(),
            ["recipient"] = row["recipient"]!.GetValue<string>(),
            ["createdAt"] = DateTimeOffset.FromUnixTimeMilliseconds(row["createdAt"]!.GetValue<long>()).ToString("O", CultureInfo.InvariantCulture),
            ["payload"] = payload,
        };
    }
}
