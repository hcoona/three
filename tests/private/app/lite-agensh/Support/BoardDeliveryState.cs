using System.Text.Json.Nodes;
using Microsoft.Data.Sqlite;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace LiteAgensh.Tests.Support;

internal sealed class BoardDeliveryState : IDisposable
{
    private readonly ScratchDirectory scratch = new();

    internal BoardDeliveryState(int workerCount = 2)
    {
        RunConfiguration configuration = new()
        {
            RepositoryPath = scratch.PathFor("checkout"),
            StatePath = scratch.PathFor("run"),
            CampaignLedgerPath = scratch.PathFor("campaign.sqlite"),
            Goal = "Preserve peer findings and deliver each notice once.",
            WorkerCount = workerCount,
        };
        Database = new(configuration.StatePath, configuration, Clock);
        Operations = new(Database);
    }

    internal ManualScenarioClock Clock { get; } = new();

    internal RunDatabase Database { get; private set; }

    internal CollaborationOperations Operations { get; private set; }

    internal JsonObject Invoke(string actor, string operation, JsonObject arguments)
    {
        JsonObject result = Operations.Invoke(actor, operation, arguments);
        Assert.AreEqual("ok", result["code"]!.GetValue<string>(), result.ToJsonString());
        Assert.IsTrue(result["ok"]!.GetValue<bool>());
        return result["data"]!.AsObject();
    }

    internal JsonObject Publish(
        string actor, string type, string content, string? detail = null,
        string? workLabel = null, int? ttl = null
    )
    {
        JsonObject arguments = new() { ["type"] = type, ["content"] = content };
        if (detail is not null)
        {
            arguments["detail"] = detail;
        }
        if (workLabel is not null)
        {
            arguments["workLabel"] = workLabel;
        }
        if (ttl is not null)
        {
            arguments["ttlSeconds"] = ttl.Value;
        }
        return Invoke(actor, "board_publish", arguments);
    }

    internal JsonObject Send(string actor, string recipient, string body) =>
        Invoke(actor, "message_send", new() { ["recipient"] = recipient, ["body"] = body });

    internal long Count(string sql, params (string Name, object? Value)[] parameters)
    {
        using SqliteConnection connection = Database.Open();
        return (long)Sql.Scalar(connection, sql, null, parameters)!;
    }

    internal string? Text(string sql, params (string Name, object? Value)[] parameters)
    {
        using SqliteConnection connection = Database.Open();
        return Sql.Scalar(connection, sql, null, parameters) as string;
    }

    internal void Restart()
    {
        Database = new(Database.StatePath, timeProvider: Clock);
        Operations = new(Database);
    }

    public void Dispose() => scratch.Dispose();
}
