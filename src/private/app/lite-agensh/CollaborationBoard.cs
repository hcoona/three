using System.Globalization;
using System.Text.Json.Nodes;
using Microsoft.Data.Sqlite;

namespace LiteAgensh;

public sealed partial class CollaborationOperations
{
    private const string BoardSelect = """
        SELECT h.id,h.actor,h.type,h.content,h.detail,h.work_label AS workLabel,h.expires_at AS expiresAt,
            h.created_at AS createdAt,h.verified_checkpoint AS verifiedCheckpoint,
            h.invalidated_reason AS invalidatedReason,h.local_failures AS localFailures FROM board_history h
        """;

    private JsonObject PublishBoard(SqliteConnection connection, SqliteTransaction transaction, string actor, JsonObject arguments)
    {
        Fields(arguments, "type", "content", "detail", "workLabel", "ttlSeconds");
        var type = Text(arguments, "type");
        var content = BoardPolicy.Admit(type, Text(arguments, "content"));
        var detail = OptionalText(arguments, "detail", allowEmpty: true);
        var workLabel = OptionalText(arguments, "workLabel", allowEmpty: true);
        if (workLabel is not null && workLabel.EnumerateRunes().Count() > 128)
        {
            throw Invalid("A work label allows at most 128 characters.");
        }

        if (arguments.ContainsKey("ttlSeconds") && type != "CLAIM")
        {
            throw Invalid("ttlSeconds is accepted only for CLAIM.");
        }

        long? expiry = null;
        if (type == "CLAIM")
        {
            var ttl = arguments.ContainsKey("ttlSeconds") ? Integer(arguments, "ttlSeconds") : 3600;
            var now = database.Now.ToUnixTimeMilliseconds();
            if (ttl > (DateTimeOffset.MaxValue.ToUnixTimeMilliseconds() - now) / 1000)
            {
                throw Invalid("Claim TTL exceeds the supported timestamp range.");
            }

            expiry = now + ttl * 1000;
        }

        var failures = (long)Sql.Scalar(connection, """
            SELECT COUNT(*) FROM board_history h JOIN board_recent r ON r.revision_id=h.id
            WHERE h.type='FAIL' AND h.actor=$actor AND h.work_label IS $work;
            """, transaction, ("$actor", actor), ("$work", workLabel))!;
        var previous = One(connection, transaction, """
            SELECT h.id,r.slot FROM board_history h JOIN board_recent r ON r.revision_id=h.id
            WHERE h.type='PATCH_SUMMARY' AND h.actor=$actor AND h.work_label IS $work ORDER BY r.slot DESC LIMIT 1;
            """, ("$actor", actor), ("$work", workLabel));
        if (type == "FAIL" && previous is not null)
        {
            Sql.Execute(connection, """
                UPDATE board_history SET verified_checkpoint=0,invalidated_reason=$reason,local_failures=$count WHERE id=$id;
                """, transaction, ("$reason", content), ("$count", failures + 1), ("$id", Id(previous)));
        }

        var id = Insert(connection, transaction, """
            INSERT INTO board_history(actor,type,content,detail,work_label,expires_at,created_at,verified_checkpoint,local_failures)
            VALUES($actor,$type,$content,$detail,$work,$expiry,$now,$verified,$failures);
            """, ("$actor", actor), ("$type", type), ("$content", content), ("$detail", detail),
            ("$work", workLabel), ("$expiry", expiry), ("$now", Timestamp),
            ("$verified", type == "PATCH_SUMMARY" && BoardPolicy.IsVerifiedCheckpoint(content)),
            ("$failures", type == "PATCH_SUMMARY" ? failures : 0));
        if (type == "PATCH_SUMMARY" && previous is not null)
        {
            Sql.Execute(connection, "UPDATE board_recent SET revision_id=$id WHERE slot=$slot;",
                transaction, ("$id", id), ("$slot", previous["slot"]!.GetValue<long>()));
        }
        else
        {
            Sql.Execute(connection, "INSERT INTO board_recent(revision_id,first_at) VALUES($id,$now);",
                transaction, ("$id", id), ("$now", Timestamp));
            Sql.Execute(connection, """
                DELETE FROM board_recent WHERE slot NOT IN (SELECT slot FROM board_recent ORDER BY slot DESC LIMIT 2000);
                """, transaction);
        }

        var result = BoardRevision(One(connection, transaction, BoardSelect + " WHERE h.id=$id;", ("$id", id))!);
        Broadcast(connection, transaction, actor, "board", $"board:{id}",
            new JsonObject { ["gistId"] = id, ["actor"] = actor, ["summary"] = $"[G{id}] {type}: {content}", ["gist"] = result.DeepClone() });
        return result;
    }

    private JsonObject RecentBoard(SqliteConnection connection, SqliteTransaction transaction, JsonObject arguments)
    {
        Fields(arguments, "limit");
        return new JsonObject { ["items"] = RecentRevisions(connection, transaction, Limit(arguments)) };
    }

    private JsonArray RecentRevisions(SqliteConnection connection, SqliteTransaction transaction, int limit, string? unreadFor = null)
    {
        var rows = Rows(connection, transaction, BoardSelect + """

            JOIN board_recent r ON r.revision_id=h.id
            WHERE (h.expires_at IS NULL OR h.expires_at >= $now)
                AND ($actor IS NULL OR (h.actor!=$actor AND NOT EXISTS(
                    SELECT 1 FROM shown_gists g WHERE g.gist_id=h.id AND g.recipient=$actor)))
            ORDER BY h.id DESC LIMIT 2000;
            """, ("$now", database.Now.ToUnixTimeMilliseconds()), ("$actor", unreadFor));
        rows.Reverse();
        return new JsonArray(CollapseRecent(rows).TakeLast(limit).Select(row => (JsonNode)BoardRevision(row)).ToArray());
    }

    private static List<JsonObject> CollapseRecent(List<JsonObject> rows)
    {
        var output = new List<JsonObject>();
        var exact = new Dictionary<(string Type, string Content), JsonObject>();
        var semantic = new Dictionary<string, JsonObject>(StringComparer.Ordinal);
        foreach (var row in rows)
        {
            var type = row["type"]!.GetValue<string>();
            if (type is "CLAIM" or "PATCH_SUMMARY")
            {
                output.Add(row);
                continue;
            }

            var content = row["content"]!.GetValue<string>();
            var key = (type, BoardPolicy.Normalize(content));
            var semanticKey = BoardPolicy.SemanticKey(type, content);
            if (!exact.TryGetValue(key, out var previous) &&
                (semanticKey is null || !semantic.TryGetValue(semanticKey, out previous)))
            {
                row["duplicateCount"] = 1;
                output.Add(row);
                exact[key] = row;
                if (semanticKey is not null)
                {
                    semantic[semanticKey] = row;
                }

                continue;
            }

            previous!["duplicateCount"] = previous["duplicateCount"]!.GetValue<int>() + 1;
            exact[key] = previous;
        }

        return output;
    }

    private static JsonObject SearchBoard(SqliteConnection connection, SqliteTransaction transaction, JsonObject arguments)
    {
        Fields(arguments, "query", "cursor", "limit");
        var (sql, queryParameters) = BoardPolicy.CompileQuery(Text(arguments, "query"));
        var (cursor, limit) = Pagination(arguments);
        var parameters = new List<(string Name, object? Value)>(queryParameters)
        {
            ("$cursor", cursor), ("$limit", limit + 1),
        };
        return Page(Rows(connection, transaction, BoardSelect + $"""

            WHERE h.id>$cursor AND ({sql}) ORDER BY h.id LIMIT $limit;
            """, [.. parameters]).Select(BoardRevision).ToList(), limit);
    }

    private static JsonObject ExpandBoard(SqliteConnection connection, SqliteTransaction transaction, JsonObject arguments)
    {
        Fields(arguments, "labels");
        var labels = Strings(arguments, "labels", required: true);
        var items = new JsonArray();
        var unknown = new JsonArray();
        var requested = labels.Where(label => !string.IsNullOrWhiteSpace(label)).ToArray();
        foreach (var label in requested.Take(100))
        {
            var id = BoardPolicy.ParseLabel(label);
            var row = id is null ? null : One(connection, transaction,
                BoardSelect + " WHERE h.id=$id AND h.detail IS NOT NULL AND h.detail!='';", ("$id", id));
            if (row is null)
            {
                unknown.Add(label.Trim());
            }
            else
            {
                items.Add(BoardRevision(row));
            }
        }

        return new JsonObject { ["items"] = items, ["unknownLabels"] = unknown, ["truncated"] = requested.Length > 100 };
    }

    private static JsonObject BoardRevision(JsonObject row)
    {
        row["label"] = "G" + Id(row).ToString(CultureInfo.InvariantCulture);
        if (row["expiresAt"] is JsonValue expiresAt)
        {
            row["expiresAt"] = DateTimeOffset.FromUnixTimeMilliseconds(expiresAt.GetValue<long>()).ToString("O", CultureInfo.InvariantCulture);
        }

        row["verifiedCheckpoint"] = row["verifiedCheckpoint"]!.GetValue<long>() != 0;
        return row;
    }
}
