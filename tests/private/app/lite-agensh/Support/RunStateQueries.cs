using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Data.Sqlite;

namespace LiteAgensh.Tests.Support;

// Explicit SQLite test projections, never DB/WAL byte comparisons.
internal static class RunStateQueries
{
    internal static SqliteConnection OpenCampaign(RunDatabase database) =>
        OpenFile(database.Configuration.CampaignLedgerPath);

    internal static SqliteConnection OpenFile(string path)
    {
        SqliteConnection connection = new(
            new SqliteConnectionStringBuilder
            {
                DataSource = path,
                Mode = SqliteOpenMode.ReadWrite,
                ForeignKeys = true,
                DefaultTimeout = 5,
                Pooling = false,
            }.ToString()
        );
        try
        {
            connection.Open();
            return connection;
        }
        catch
        {
            connection.Dispose();
            throw;
        }
    }

    internal static void QualifyExistingCreatorStore(RunConfiguration configuration)
    {
        // Qualify the store before a RunDatabase open can run initialization SQL.
        string path = Path.Combine(configuration.StatePath, "run.sqlite");
        using SqliteConnection existing = OpenFile(path);
        string? json = Sql.Scalar(existing, "SELECT value FROM meta WHERE key='configuration';")
            as string;
        RunConfiguration? stored = json is null
            ? null
            : JsonSerializer.Deserialize<RunConfiguration>(json, Json.Options);
        if (
            stored is null
            || stored.StatePath != configuration.StatePath
            || stored.CampaignLedgerPath != configuration.CampaignLedgerPath
            || stored.RepositoryPath != configuration.RepositoryPath
            || !Guid.TryParseExact(
                Sql.Scalar(existing, "SELECT value FROM meta WHERE key='run_id';") as string,
                "N",
                out _
            )
        )
        {
            throw new InvalidOperationException(
                "Creator descendant cleanup could not qualify its already configured store."
            );
        }
        if (
            Sql.Scalar(
                existing,
                "SELECT name FROM sqlite_schema WHERE type='table' AND name='process_leases';"
            ) is null
        )
        {
            throw new InvalidOperationException(
                "Creator descendant cleanup has no existing lease registry to resolve."
            );
        }
    }

    internal static long Number(
        RunDatabase database,
        string query,
        params (string Name, object? Value)[] parameters
    )
    {
        using SqliteConnection connection = database.Open();
        return Convert.ToInt64(
            Sql.Scalar(connection, query, null, parameters),
            CultureInfo.InvariantCulture
        );
    }

    internal static JsonArray Rows(
        SqliteConnection connection,
        string query,
        params (string Name, object? Value)[] parameters
    )
    {
        using SqliteCommand command = Sql.Command(connection, query, null, parameters);
        using ObservedSqliteReader reader = Sql.Reader(command);
        JsonArray rows = [];
        while (reader.Read())
        {
            JsonObject row = new();
            for (int i = 0; i < reader.FieldCount; i++)
            {
                row[reader.GetName(i)] = reader.IsDBNull(i)
                    ? null
                    : JsonSerializer.SerializeToNode(reader.GetValue(i));
            }
            rows.Add(row);
        }
        return rows;
    }

    internal static JsonObject History(RunDatabase database)
    {
        using SqliteConnection connection = database.Open();
        JsonObject result = new();
        foreach (
            string table in new[]
            {
                "coordinator_segments",
                "coordinator_turns",
                "coordinator_controls",
                "process_leases",
                "native_sessions",
                "native_attempts",
                "events",
                "delivery_batches",
                "shown_notices",
                "shown_gists",
                "batch_gists",
                "notice_cursors",
                "notice_leases",
                "checks",
            }
        )
        {
            result[table] = Rows(connection, $"SELECT * FROM {table} ORDER BY rowid;");
        }
        result["metadata"] = Rows(
            connection,
            "SELECT key,value FROM meta WHERE key NOT LIKE 'native:recovery_failure_details:%' "
                + "ORDER BY key;"
        );
        return result;
    }

    internal static JsonObject Financial(RunDatabase database, CreditLedger ledger)
    {
        using SqliteConnection campaign = OpenCampaign(database);
        return new()
        {
            ["status"] = ledger.Status(database.RunId, database.Configuration.RunAiCreditAllowance),
            ["rows"] = Rows(campaign, "SELECT * FROM credit_reservations ORDER BY id;"),
            ["usage"] = Rows(campaign, "SELECT * FROM credit_sdk_usage ORDER BY reservation_id;"),
            ["owners"] = Rows(campaign, "SELECT * FROM credit_sdk_owners ORDER BY reservation_id;"),
            ["reports"] = Rows(campaign, "SELECT * FROM credit_reports ORDER BY id;"),
        };
    }
}
