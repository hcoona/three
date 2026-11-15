using Microsoft.Data.Sqlite;

namespace LiteAgensh.Tests.Support;

internal sealed class SqliteScenarioState : IDisposable
{
    private readonly ScratchDirectory scratch = new();

    internal SqliteScenarioState()
    {
        ConnectionString = new SqliteConnectionStringBuilder
        {
            DataSource = scratch.PathFor("scenario.sqlite"),
            DefaultTimeout = 5,
            Pooling = false,
        }.ToString();
        using SqliteConnection connection = new(ConnectionString);
        connection.Open();
        using SqliteCommand command = connection.CreateCommand();
        command.CommandText = """
            CREATE TABLE entries(id TEXT PRIMARY KEY, value TEXT NOT NULL);
            """;
        command.ExecuteNonQuery();
    }

    internal string ConnectionString { get; }

    internal string DiagnosticsPath => scratch.PathFor("scenario.sqlite.diagnostics");

    internal SqliteConnection Open(string actor = "owner")
    {
        using IDisposable scope = SqliteDiagnostics.UseActor(actor);
        return Sql.Open(new SqliteConnection(ConnectionString), "run");
    }

    public void Dispose() => scratch.Dispose();
}
