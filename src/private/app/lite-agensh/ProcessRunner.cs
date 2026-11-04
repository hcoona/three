using System.Diagnostics;
using Microsoft.Data.Sqlite;

namespace LiteAgensh;

public sealed record ProcessResult(int ExitCode, string StandardOutput, string StandardError, bool TimedOut);

public sealed class ProcessRunner
{
    private readonly RunDatabase database;

    public ProcessRunner(RunDatabase database)
    {
        this.database = database;
        using SqliteConnection connection = database.Open();
        Sql.Execute(connection, """
            CREATE TABLE IF NOT EXISTS process_leases(
                id INTEGER PRIMARY KEY AUTOINCREMENT,
                pid INTEGER NOT NULL,
                start_ticks INTEGER NOT NULL,
                kind TEXT NOT NULL,
                state TEXT NOT NULL,
                created_at TEXT NOT NULL
            );
            """);
    }

    public long Register(Process process, string kind)
    {
        using SqliteConnection connection = database.Open();
        Sql.Execute(connection, """
            INSERT INTO process_leases(pid,start_ticks,kind,state,created_at)
            VALUES($pid,$ticks,$kind,'running',$time);
            """, null, ("$pid", process.Id), ("$ticks", process.StartTime.ToUniversalTime().Ticks),
            ("$kind", kind), ("$time", database.Now.ToString("O")));
        return (long)(Sql.Scalar(connection, "SELECT last_insert_rowid();") ?? throw new InvalidOperationException());
    }

    public void Complete(long leaseId)
    {
        using SqliteConnection connection = database.Open();
        Sql.Execute(connection, "UPDATE process_leases SET state='exited' WHERE id=$id;", null, ("$id", leaseId));
    }

    public async Task<ProcessResult> RunAsync(string executable, IReadOnlyList<string> arguments, string workingDirectory,
        TimeSpan timeout, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(executable);
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(timeout, TimeSpan.Zero);

        cancellationToken.ThrowIfCancellationRequested();
        using CancellationTokenSource deadline = new(timeout);
        using CancellationTokenSource linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, deadline.Token);
        ProcessStartInfo start = new(executable)
        {
            WorkingDirectory = workingDirectory,
            UseShellExecute = false,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true,
        };
        foreach (string argument in arguments)
        {
            start.ArgumentList.Add(argument);
        }

        using Process process = new() { StartInfo = start };
        process.Start();
        long lease;
        try
        {
            lease = Register(process, "command");
        }
        catch
        {
            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: true);
                await process.WaitForExitAsync(CancellationToken.None)
                    .WaitAsync(TimeSpan.FromSeconds(5), CancellationToken.None).ConfigureAwait(false);
            }

            throw;
        }

        process.StandardInput.Close();
        Task<string> output = process.StandardOutput.ReadToEndAsync(cancellationToken: CancellationToken.None);
        Task<string> error = process.StandardError.ReadToEndAsync(cancellationToken: CancellationToken.None);
        bool timedOut = false;
        try
        {
            await process.WaitForExitAsync(linked.Token).ConfigureAwait(false);
            await Task.WhenAll(output, error).WaitAsync(linked.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (linked.IsCancellationRequested)
        {
            timedOut = deadline.IsCancellationRequested;
            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: true);
            }

            try
            {
                await process.WaitForExitAsync(CancellationToken.None).WaitAsync(TimeSpan.FromSeconds(5), CancellationToken.None).ConfigureAwait(false);
                await Task.WhenAll(output, error).WaitAsync(TimeSpan.FromSeconds(5), CancellationToken.None).ConfigureAwait(false);
            }
            catch (TimeoutException exception)
            {
                using SqliteConnection connection = database.Open();
                Sql.Execute(connection, "UPDATE process_leases SET state='unresolved' WHERE id=$id;", null, ("$id", lease));
                throw new InvalidOperationException("Owned command or inherited pipes did not settle; replacement is blocked.", exception);
            }
        }

        Complete(lease);
        cancellationToken.ThrowIfCancellationRequested();
        return new(process.ExitCode, await output.ConfigureAwait(false), await error.ConfigureAwait(false), timedOut);
    }

    public async Task ResolveSurvivorsAsync(CancellationToken cancellationToken = default)
    {
        List<(long Id, int Pid, long Ticks, string State)> leases = [];
        using (SqliteConnection connection = database.Open())
        using (SqliteCommand command = Sql.Command(connection,
            "SELECT id,pid,start_ticks,state FROM process_leases WHERE state<>'exited';"))
        using (SqliteDataReader reader = command.ExecuteReader())
        {
            while (reader.Read())
            {
                leases.Add((reader.GetInt64(0), reader.GetInt32(1), reader.GetInt64(2), reader.GetString(3)));
            }
        }

        foreach ((long id, int pid, long ticks, string state) in leases)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (state == "unresolved")
            {
                throw new InvalidOperationException($"Process lease {id} needs explicit ownership resolution.");
            }

            Process process;
            try
            {
                process = Process.GetProcessById(pid);
            }
            catch (ArgumentException)
            {
                Complete(id);
                continue;
            }

            using (process)
            {
                if (process.StartTime.ToUniversalTime().Ticks != ticks)
                {
                    Complete(id);
                    continue;
                }

                if (!process.HasExited)
                {
                    process.Kill(entireProcessTree: true);
                    await process.WaitForExitAsync(cancellationToken).WaitAsync(TimeSpan.FromSeconds(5), cancellationToken)
                        .ConfigureAwait(false);
                }
            }

            Complete(id);
        }
    }
}
