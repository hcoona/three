using System.Globalization;
using System.Runtime.CompilerServices;
using System.Text.Json;
using Microsoft.Data.Sqlite;

namespace LiteAgensh;

public sealed class RunDatabase
{
    private const int SchemaVersion = 1;
    private readonly string connectionString;

    public RunDatabase(
        string statePath,
        RunConfiguration? configuration = null,
        TimeProvider? timeProvider = null
    )
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(statePath);
        StatePath = Path.GetFullPath(statePath);
        TimeProvider = timeProvider ?? TimeProvider.System;
        string databasePath = Path.Combine(StatePath, "run.sqlite");
        if (configuration is null && !File.Exists(databasePath))
        {
            throw new FileNotFoundException(
                "No initialized LiteAgensh run exists here.",
                databasePath
            );
        }

        configuration?.Validate();
        Directory.CreateDirectory(StatePath);
        connectionString = new SqliteConnectionStringBuilder
        {
            DataSource = databasePath,
            Mode = SqliteOpenMode.ReadWriteCreate,
            ForeignKeys = true,
            DefaultTimeout = 5,
            Pooling = false,
        }.ToString();
        using SqliteConnection connection = Open();
        Sql.Execute(connection, "PRAGMA journal_mode=WAL;");
        using SqliteTransactionScope transaction = Sql.BeginTransaction(connection);
        Sql.Execute(
            connection,
            "CREATE TABLE IF NOT EXISTS meta(key TEXT PRIMARY KEY, value TEXT NOT NULL);",
            transaction
        );
        string? existingVersion =
            Sql.Scalar(
                connection,
                "SELECT value FROM meta WHERE key='schema_version';",
                transaction
            ) as string;
        if (
            existingVersion is not null
            && int.Parse(existingVersion, CultureInfo.InvariantCulture) != SchemaVersion
        )
        {
            throw new InvalidOperationException(
                $"Unsupported run schema {existingVersion}; state has not been recreated."
            );
        }

        string? stored =
            Sql.Scalar(connection, "SELECT value FROM meta WHERE key='configuration';", transaction)
            as string;
        if (stored is null)
        {
            if (configuration is null)
            {
                throw new InvalidOperationException("Run configuration is missing.");
            }

            if (
                !Path.GetFullPath(configuration.StatePath)
                    .Equals(StatePath, StringComparison.OrdinalIgnoreCase)
            )
            {
                throw new ArgumentException(
                    "Configuration and database state paths disagree.",
                    nameof(configuration)
                );
            }

            Configuration = configuration;
            Sql.Execute(
                connection,
                "INSERT INTO meta(key,value) VALUES('configuration',$configuration),"
                    + "('schema_version',$version),('run_id',$run);",
                transaction,
                ("$configuration", JsonSerializer.Serialize(configuration, Json.Options)),
                ("$version", SchemaVersion.ToString(CultureInfo.InvariantCulture)),
                ("$run", Guid.NewGuid().ToString("N"))
            );
        }
        else
        {
            if (configuration is not null)
            {
                throw new InvalidOperationException(
                    "An initialized run cannot be overwritten by init."
                );
            }

            Configuration =
                JsonSerializer.Deserialize<RunConfiguration>(stored, Json.Options)
                ?? throw new InvalidOperationException("Stored run configuration is invalid.");
            Configuration.Validate();
        }

        transaction.Commit();
    }

    public string StatePath { get; }

    public RunConfiguration Configuration { get; private set; }

    public TimeProvider TimeProvider { get; }

    public DateTimeOffset Now => TimeProvider.GetUtcNow();

    public string RunId =>
        ReadMetadata("run_id") ?? throw new InvalidOperationException("Run identity is missing.");

    public IReadOnlyList<string> Members => ["owner", .. Configuration.WorkerIds];

    public SqliteConnection Open()
    {
        SqliteConnection connection = new(connectionString);
        return Sql.Open(connection, "run");
    }

    public string? ReadMetadata(string key)
    {
        using SqliteConnection connection = Open();
        return Sql.Scalar(connection, "SELECT value FROM meta WHERE key=$key;", null, ("$key", key))
            as string;
    }

    public void WriteMetadata(string key, string value)
    {
        using SqliteConnection connection = Open();
        Sql.Execute(
            connection,
            "INSERT INTO meta(key,value) VALUES($key,$value) ON CONFLICT(key) DO UPDATE "
                + "SET value=excluded.value;",
            null,
            ("$key", key),
            ("$value", value)
        );
    }

    public void SaveConfiguration(RunConfiguration configuration)
    {
        configuration.Validate();
        WriteMetadata("configuration", JsonSerializer.Serialize(configuration, Json.Options));
        Configuration = configuration;
    }
}

public static class Sql
{
    internal static SqliteConnection Open(SqliteConnection connection, string kind)
    {
        SqliteDiagnostics.Attach(connection, kind);
        try
        {
            SqliteDiagnostics.Observe(
                connection, "Open", "open",
                () => { connection.Open(); return true; }
            );
            return connection;
        }
        catch
        {
            SqliteDiagnostics.Close(connection);
            connection.Dispose();
            throw;
        }
    }

    public static SqliteTransactionScope BeginTransaction(
        SqliteConnection connection,
        bool deferred = false,
        [CallerMemberName] string operation = ""
    )
    {
        string id = Guid.NewGuid().ToString("N");
        SqliteTransaction transaction = SqliteDiagnostics.Observe(
            connection, operation, "acquisition",
            () => connection.BeginTransaction(deferred), id, deferred
        );
        return new(connection, transaction, operation, id, deferred);
    }

    public static SqliteCommand Command(
        SqliteConnection connection,
        string text,
        SqliteTransaction? transaction = null,
        params (string Name, object? Value)[] parameters
    )
    {
        SqliteCommand command = connection.CreateCommand();
        command.CommandText = text;
        command.Transaction = transaction;
        foreach ((string name, object? value) in parameters)
        {
            command.Parameters.AddWithValue(name, value ?? DBNull.Value);
        }

        return command;
    }

    public static int Execute(
        SqliteConnection connection,
        string text,
        SqliteTransaction? transaction = null,
        params (string Name, object? Value)[] parameters
    )
    {
        using SqliteCommand command = Command(connection, text, transaction, parameters);
        return SqliteDiagnostics.Observe(
            connection, "Execute", "statement", command.ExecuteNonQuery
        );
    }

    public static object? Scalar(
        SqliteConnection connection,
        string text,
        SqliteTransaction? transaction = null,
        params (string Name, object? Value)[] parameters
    )
    {
        using SqliteCommand command = Command(connection, text, transaction, parameters);
        object? result = SqliteDiagnostics.Observe(
            connection, "Scalar", "statement", command.ExecuteScalar
        );
        return result is DBNull ? null : result;
    }

    public static ObservedSqliteReader Reader(SqliteCommand command)
    {
        SqliteConnection connection = command.Connection!;
        SqliteDataReader reader = SqliteDiagnostics.Observe(
            connection, "Reader", "statement", command.ExecuteReader
        );
        return new(connection, reader);
    }
}
