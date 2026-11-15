using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Data.Sqlite;

namespace LiteAgensh.Tests.Support;

// Explicit Phase6 historical projections, never DB/WAL byte comparisons.
internal static class Phase6State
{
    private static readonly string[] gitEnvironmentVariables =
    [
        "GIT_CONFIG_GLOBAL",
        "GIT_CONFIG_SYSTEM",
        "GIT_CONFIG_NOSYSTEM",
        "HOME",
        "USERPROFILE",
        "XDG_CONFIG_HOME",
        "GIT_CONFIG",
        "GIT_CONFIG_COUNT",
        "GIT_CONFIG_PARAMETERS",
        "GIT_DIR",
        "GIT_COMMON_DIR",
        "GIT_WORK_TREE",
        "GIT_INDEX_FILE",
        "GIT_OBJECT_DIRECTORY",
        "GIT_ALTERNATE_OBJECT_DIRECTORIES",
        "GIT_NAMESPACE",
        "GIT_CEILING_DIRECTORIES",
    ];

    internal static Dictionary<string, string?> CaptureEnvironment() =>
        gitEnvironmentVariables.ToDictionary(
            name => name,
            Environment.GetEnvironmentVariable,
            StringComparer.Ordinal
        );

    internal static void VerifyEnvironment(Dictionary<string, string?> before)
    {
        JsonObject receipt = new();
        bool matches = true;
        foreach ((string name, string? original) in before)
        {
            string? actual = Environment.GetEnvironmentVariable(name);
            bool equal = string.Equals(original, actual, StringComparison.Ordinal);
            matches &= equal;
            static string? Digest(string? value) =>
                value is null
                    ? null
                    : Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(value)));
            receipt[name] = new JsonObject
            {
                ["beforeSha256"] = Digest(original),
                ["afterSha256"] = Digest(actual),
                ["exactOrdinalEquality"] = equal,
            };
        }
        Console.WriteLine("PHASE6_ENVIRONMENT_RESTORATION " + receipt.ToJsonString());
        if (!matches)
        {
            throw new InvalidOperationException("Owned Phase6 Git environment was not restored.");
        }
    }

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
