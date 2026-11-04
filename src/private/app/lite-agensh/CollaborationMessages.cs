using System.Text.Json.Nodes;
using Microsoft.Data.Sqlite;

namespace LiteAgensh;

public sealed partial class CollaborationOperations
{
    private JsonObject SendMessage(
        SqliteConnection connection,
        SqliteTransaction transaction,
        string actor,
        JsonObject arguments
    )
    {
        Fields(arguments, "recipient", "body", "threadId", "mentions", "urgent");
        var recipient = Text(arguments, "recipient");
        if (recipient != "team")
        {
            RequireKnownMember(recipient);
        }

        var body = Text(arguments, "body");
        var mentions = Strings(arguments, "mentions");
        var urgent = Boolean(arguments, "urgent");
        var channel = recipient == "team" ? "team" : DirectChannel(actor, recipient);
        foreach (var mention in mentions)
        {
            RequireKnownMember(mention);
            if (channel != "team" && mention != actor && mention != recipient)
            {
                throw new OperationException(
                    "not_visible",
                    "A mention cannot widen a direct channel's visibility."
                );
            }
        }

        long? thread = arguments.ContainsKey("threadId") ? Integer(arguments, "threadId") : null;
        if (thread is not null)
        {
            var root =
                One(
                    connection,
                    transaction,
                    "SELECT id,channel_id,thread_id FROM messages WHERE id=$id;",
                    ("$id", thread)
                ) ?? throw Missing("Thread", thread.Value);
            var rootChannel = root["channel_id"]!.GetValue<string>();
            _ = Channel(actor, rootChannel);
            if (rootChannel != channel || root["thread_id"]!.GetValue<long>() != thread)
            {
                throw Invalid(
                    "threadId must identify a visible thread root in the destination channel."
                );
            }
        }

        var id = Insert(
            connection,
            transaction,
            """
            INSERT INTO messages(actor,channel_id,recipient,body,thread_id,mentions,
                urgent,created_at)
            VALUES($actor,$channel,$recipient,$body,$thread,$mentions,$urgent,$now);
            """,
            ("$actor", actor),
            ("$channel", channel),
            ("$recipient", recipient),
            ("$body", body),
            ("$thread", thread),
            ("$mentions", Array(mentions).ToJsonString()),
            ("$urgent", urgent),
            ("$now", Timestamp)
        );
        if (thread is null)
        {
            Sql.Execute(
                connection,
                "UPDATE messages SET thread_id=id WHERE id=$id;",
                transaction,
                ("$id", id)
            );
        }

        var message = Message(
            One(connection, transaction, MessageSelect + " WHERE id=$id;", ("$id", id))!
        );
        var recipients =
            recipient == "team" ? database.Members.Where(member => member != actor) : [recipient];
        foreach (var member in recipients)
        {
            EnqueueEvent(
                connection,
                transaction,
                member,
                recipient == "team" ? "group" : "dm",
                $"message:{id}",
                new JsonObject
                {
                    ["messageId"] = id,
                    ["actor"] = actor,
                    ["urgent"] =
                        urgent
                        || actor == "owner"
                        || mentions.Contains(member, StringComparer.Ordinal),
                    ["summary"] = $"{actor} in {channel}: {body}",
                    ["message"] = message.DeepClone(),
                }.ToJsonString(Json.Options)
            );
        }

        return message;
    }

    private const string MessageSelect = """
        SELECT id,actor,channel_id AS channelId,recipient,body,thread_id AS threadId,
            mentions,urgent,created_at AS createdAt
        FROM messages
        """;

    private JsonObject ReadMessages(
        SqliteConnection connection,
        SqliteTransaction transaction,
        string actor,
        string name,
        JsonObject arguments
    )
    {
        var field = name switch
        {
            "message_read" => "channelId",
            "message_thread" => "threadId",
            _ => "query",
        };
        Fields(arguments, field, "cursor", "limit");
        var (cursor, limit) = Pagination(arguments);
        var where = "";
        var parameters = new List<(string Name, object? Value)>
        {
            ("$actor", actor),
            ("$cursor", cursor),
            ("$limit", limit + 1),
        };
        switch (name)
        {
            case "message_read":
                var channelId = Text(arguments, "channelId");
                _ = Channel(actor, channelId);
                where = "channel_id=$channel";
                parameters.Add(("$channel", channelId));
                break;
            case "message_thread":
                var thread = Integer(arguments, "threadId");
                var root =
                    One(
                        connection,
                        transaction,
                        "SELECT channel_id,thread_id FROM messages WHERE id=$id;",
                        ("$id", thread)
                    ) ?? throw Missing("Thread", thread);
                _ = Channel(actor, root["channel_id"]!.GetValue<string>());
                if (root["thread_id"]!.GetValue<long>() != thread)
                {
                    throw Invalid("threadId must identify a thread root.");
                }

                where = "thread_id=$thread";
                parameters.Add(("$thread", thread));
                break;
            default:
                where = "lite_contains(body,$query)";
                parameters.Add(("$query", Text(arguments, "query")));
                break;
        }

        var rows = Rows(
            connection,
            transaction,
            MessageSelect
                + $"""

                WHERE id>$cursor AND ({where})
                    AND ($actor='owner' OR channel_id='team' OR actor=$actor OR recipient=$actor)
                ORDER BY id LIMIT $limit;
                """,
            [.. parameters]
        );
        return Page(rows.Select(Message).ToList(), limit);
    }

    private static JsonObject Message(JsonObject row)
    {
        row["mentions"] = JsonNode.Parse(row["mentions"]!.GetValue<string>());
        row["urgent"] = row["urgent"]!.GetValue<long>() != 0;
        return row;
    }

    private JsonObject ReadChannel(string actor, JsonObject arguments)
    {
        Fields(arguments, "id");
        return Channel(actor, Text(arguments, "id"));
    }

    private JsonObject ListChannels(string actor, JsonObject arguments)
    {
        Fields(arguments);
        var channels = new JsonArray { Channel(actor, "team") };
        var members = database.Members;
        for (var first = 0; first < members.Count; first++)
        {
            for (var second = first; second < members.Count; second++)
            {
                if (actor == "owner" || members[first] == actor || members[second] == actor)
                {
                    channels.Add(Channel(actor, DirectChannel(members[first], members[second])));
                }
            }
        }

        return new JsonObject { ["items"] = channels };
    }

    private JsonObject Channel(string actor, string channel)
    {
        if (channel == "team")
        {
            return new JsonObject
            {
                ["id"] = channel,
                ["members"] = Array(database.Members),
                ["kind"] = "team",
            };
        }

        var pair = channel.Split(':');
        if (
            pair.Length != 3
            || pair[0] != "dm"
            || !database.Members.Contains(pair[1], StringComparer.Ordinal)
            || !database.Members.Contains(pair[2], StringComparer.Ordinal)
            || DirectChannel(pair[1], pair[2]) != channel
        )
        {
            throw new OperationException("not_found", "Unknown local channel.");
        }

        if (actor != "owner" && actor != pair[1] && actor != pair[2])
        {
            throw new OperationException(
                "not_visible",
                "The bound actor cannot read that direct channel."
            );
        }

        return new JsonObject
        {
            ["id"] = channel,
            ["members"] = Array(new[] { pair[1], pair[2] }.Distinct(StringComparer.Ordinal)),
            ["kind"] = "direct",
        };
    }

    private static string DirectChannel(string first, string second) =>
        string.CompareOrdinal(first, second) <= 0 ? $"dm:{first}:{second}" : $"dm:{second}:{first}";
}
