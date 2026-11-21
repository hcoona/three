using Microsoft.Data.Sqlite;

namespace LiteAgensh.Tests.Support;

internal sealed record OwnedLease(long Id, int Pid, long BirthTicks, string Kind, string State);

internal sealed class OwnedProcessState : IDisposable
{
    private readonly ScratchDirectory scratch = new();

    internal OwnedProcessState(TimeProvider? commandTimeProvider = null)
    {
        Directory.CreateDirectory(scratch.PathFor("unused-checkout"));
        RunConfiguration configuration = new()
        {
            RepositoryPath = scratch.PathFor("unused-checkout"),
            StatePath = scratch.PathFor("run"),
            CampaignLedgerPath = scratch.PathFor("not-created-campaign.sqlite"),
            Goal = "Settle only the application's positively owned command identities.",
        };
        Database = new(configuration.StatePath, configuration);
        Runner = new(Database, commandTimeProvider ?? TimeProvider.System);
    }

    internal RunDatabase Database { get; }

    internal ProcessRunner Runner { get; }

    internal string WorkingDirectory => scratch.Root;

    internal OwnedLease Lease(long? id = null, int? pid = null)
    {
        using SqliteConnection connection = Database.Open();
        using SqliteCommand command = Sql.Command(
            connection,
            """
            SELECT id,pid,start_ticks,kind,state FROM process_leases
            WHERE ($id IS NULL AND $pid IS NULL) OR id=$id OR pid=$pid;
            """,
            null,
            ("$id", id),
            ("$pid", pid)
        );
        using ObservedSqliteReader reader = Sql.Reader(command);
        if (!reader.Read())
        {
            throw new InvalidOperationException(
                "The consuming command did not leave its ownership lease."
            );
        }

        OwnedLease result = new(
            reader.GetInt64(0),
            reader.GetInt32(1),
            reader.GetInt64(2),
            reader.GetString(3),
            reader.GetString(4)
        );
        if (reader.Read())
        {
            throw new InvalidOperationException(
                "Expected one exact owned lease, not ambiguous matches."
            );
        }

        return result;
    }

    internal void Execute(string statement, params (string Name, object? Value)[] parameters)
    {
        using SqliteConnection connection = Database.Open();
        Sql.Execute(connection, statement, null, parameters);
    }

    public void Dispose() => scratch.Dispose();
}
