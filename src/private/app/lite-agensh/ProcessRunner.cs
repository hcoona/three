using System.Diagnostics;
using System.Globalization;
using System.Text.Json.Nodes;
using Microsoft.Data.Sqlite;

namespace LiteAgensh;

public sealed record ProcessResult(
    int ExitCode,
    string StandardOutput,
    string StandardError,
    bool TimedOut
);

public sealed class ProcessRunner
{
    internal const string CancellationEvidenceKey = "LiteAgensh.CommandCancellation";
    private const int CancellationOutputLimit = 8192;
    private static readonly AsyncLocal<string?> ambientScope = new();
    private readonly RunDatabase database;
    private readonly TimeProvider commandTimeProvider;

    public ProcessRunner(RunDatabase database)
        : this(database, TimeProvider.System) { }

    internal ProcessRunner(RunDatabase database, TimeProvider commandTimeProvider)
    {
        this.database = database;
        this.commandTimeProvider = commandTimeProvider;
        EnsureSchema(database);
    }

    internal static IDisposable UseScope(string scope)
    {
        string? previous = ambientScope.Value;
        ambientScope.Value = scope;
        return new ScopeReset(previous);
    }

    internal enum OwnerLiveness
    {
        Live,
        Absent,
        Unknown,
    }

    internal static (int Pid, long StartTicks) CurrentOwnerIdentity()
    {
        using Process owner = Process.GetCurrentProcess();
        return (owner.Id, owner.StartTime.ToUniversalTime().Ticks);
    }

    internal static OwnerLiveness InspectOwner(int pid, long startTicks)
    {
        Process owner;
        try
        {
            owner = Process.GetProcessById(pid);
        }
        catch (ArgumentException)
        {
            return OwnerLiveness.Absent;
        }
        catch (Exception exception)
            when (exception is InvalidOperationException or System.ComponentModel.Win32Exception)
        {
            return OwnerLiveness.Unknown;
        }

        using (owner)
        {
            try
            {
                if (HasProcessExited(owner))
                {
                    return OwnerLiveness.Absent;
                }

                return owner.StartTime.ToUniversalTime().Ticks == startTicks
                    ? OwnerLiveness.Live
                    : OwnerLiveness.Absent;
            }
            catch (InvalidOperationException) when (HasProcessExited(owner))
            {
                return OwnerLiveness.Absent;
            }
            catch (System.ComponentModel.Win32Exception exception)
                when (exception.NativeErrorCode == 87 || HasProcessExited(owner))
            {
                return OwnerLiveness.Absent;
            }
            catch (Exception exception)
                when (exception
                        is InvalidOperationException
                            or System.ComponentModel.Win32Exception
                )
            {
                return OwnerLiveness.Unknown;
            }
        }
    }

    internal static bool HasUnsettledLeases(
        SqliteConnection connection,
        SqliteTransaction? transaction = null,
        bool includeLiveHelpers = false
    )
    {
        if (HasUnsettledSidecar(connection))
        {
            return true;
        }

        if (
            Sql.Scalar(
                connection,
                "SELECT 1 FROM sqlite_master WHERE type='table' AND name='process_leases';",
                transaction
            )
            is null
        )
        {
            return false;
        }

        if (
            Sql.Scalar(
                connection,
                "SELECT 1 FROM sqlite_master "
                    + "WHERE type='table' AND name='process_helper_admissions';",
                transaction
            )
                is not null
            && Sql.Scalar(
                connection,
                "SELECT id FROM process_helper_admissions LIMIT 1;",
                transaction
            )
                is not null
        )
        {
            return true;
        }

        if (includeLiveHelpers)
        {
            return Sql.Scalar(
                    connection,
                    "SELECT id FROM process_leases WHERE state<>'exited' LIMIT 1;",
                    transaction
                )
                is not null;
        }

        bool hasOwners =
            Sql.Scalar(
                connection,
                "SELECT 1 FROM sqlite_master "
                    + "WHERE type='table' AND name='process_lease_owners';",
                transaction
            )
            is not null;
        if (!hasOwners)
        {
            return Sql.Scalar(
                    connection,
                    "SELECT id FROM process_leases WHERE state<>'exited' LIMIT 1;",
                    transaction
                )
                is not null;
        }

        using SqliteCommand command = Sql.Command(
            connection,
            """
            SELECT p.state,o.scope,o.owner_pid,o.owner_start_ticks
            FROM process_leases p
            LEFT JOIN process_lease_owners o ON o.lease_id=p.id
            WHERE p.state<>'exited';
            """,
            transaction
        );
        using ObservedSqliteReader reader = Sql.Reader(command);
        while (reader.Read())
        {
            string state = reader.GetString(0);
            string scope = reader.IsDBNull(1) ? "coordinator" : reader.GetString(1);
            if (state == "unresolved" || scope != "helper")
            {
                return true;
            }

            if (
                reader.IsDBNull(2)
                || reader.IsDBNull(3)
                || InspectOwner(reader.GetInt32(2), reader.GetInt64(3)) != OwnerLiveness.Live
            )
            {
                return true;
            }
        }

        return false;
    }

    internal static bool HasUnresolvedLease(
        SqliteConnection connection,
        SqliteTransaction? transaction = null
    ) =>
        HasUnsettledSidecar(connection)
        || (
            Sql.Scalar(
                connection,
                "SELECT 1 FROM sqlite_master WHERE type='table' AND name='process_leases';",
                transaction
            )
                is not null
            && Sql.Scalar(
                connection,
                "SELECT id FROM process_leases WHERE state='unresolved' LIMIT 1;",
                transaction
            )
                is not null
        );

    private static bool HasUnsettledSidecar(SqliteConnection connection)
    {
        if (string.IsNullOrWhiteSpace(connection.DataSource))
        {
            return false;
        }

        string? stateDirectory = Path.GetDirectoryName(Path.GetFullPath(connection.DataSource));
        if (string.IsNullOrWhiteSpace(stateDirectory))
        {
            return false;
        }

        string sidecarDirectory = Path.Combine(stateDirectory, "unsettled-leases");
        return Directory.Exists(sidecarDirectory)
            && Directory.EnumerateFiles(sidecarDirectory, "*.unresolved").Any();
    }

    internal static bool HasSettledKind(
        SqliteConnection connection,
        string kind,
        SqliteTransaction? transaction = null
    ) =>
        Sql.Scalar(
            connection,
            "SELECT 1 FROM sqlite_master WHERE type='table' AND name='process_leases';",
            transaction
        )
            is not null
        && Sql.Scalar(
            connection,
            "SELECT id FROM process_leases WHERE kind=$kind AND state='exited' LIMIT 1;",
            transaction,
            ("$kind", kind)
        )
            is not null;

    private static void EnsureSchema(RunDatabase database)
    {
        using SqliteConnection connection = database.Open();
        if (IsSchemaInitialized(connection))
        {
            return;
        }

        using SqliteTransactionScope transaction = Sql.BeginTransaction(connection);
        Sql.Execute(
            connection,
            """
            CREATE TABLE IF NOT EXISTS process_leases(
                id INTEGER PRIMARY KEY AUTOINCREMENT,
                pid INTEGER NOT NULL,
                start_ticks INTEGER NOT NULL,
                kind TEXT NOT NULL,
                state TEXT NOT NULL,
                created_at TEXT NOT NULL
            );
            CREATE TABLE IF NOT EXISTS process_lease_owners(
                lease_id INTEGER PRIMARY KEY REFERENCES process_leases(id),
                owner_pid INTEGER NOT NULL,
                owner_start_ticks INTEGER NOT NULL,
                scope TEXT NOT NULL
            );
            CREATE TABLE IF NOT EXISTS process_helper_admissions(
                id TEXT PRIMARY KEY,
                owner_pid INTEGER NOT NULL,
                owner_start_ticks INTEGER NOT NULL,
                created_at TEXT NOT NULL
            );
            """,
            transaction
        );
        transaction.Commit();
    }

    private static bool IsSchemaInitialized(SqliteConnection connection) =>
        Convert.ToInt64(
            Sql.Scalar(
                connection,
                """
                SELECT COUNT(*) FROM sqlite_master
                WHERE type='table' AND name IN (
                    'process_leases','process_lease_owners','process_helper_admissions'
                );
                """
            ),
            CultureInfo.InvariantCulture
        ) == 3;

    public long Register(Process process, string kind) =>
        RegisterCore(process, kind, admissionId: null);

    private long RegisterCore(Process process, string kind, string? admissionId)
    {
        long ticks;
        try
        {
            ticks = process.StartTime.ToUniversalTime().Ticks;
        }
        catch (Exception exception)
            when (kind == "command"
                && exception is InvalidOperationException or System.ComponentModel.Win32Exception
                && HasProcessExited(process))
        {
            ticks = 0;
        }

        string scope = ambientScope.Value ?? "coordinator";
        (int ownerPid, long ownerTicks) = CurrentOwnerIdentity();
        using SqliteConnection connection = database.Open();
        using SqliteTransactionScope transaction = Sql.BeginTransaction(connection);
        if (admissionId is not null)
        {
            Sql.Execute(
                connection,
                "DELETE FROM process_helper_admissions WHERE id=$id;",
                transaction,
                ("$id", admissionId)
            );
        }
        else if (scope == "helper" && !RunMetadata.IsGitWritable(connection, transaction))
        {
            throw new OperationException(
                "unavailable",
                "Git worktrees are not initialized or have been cleaned up."
            );
        }

        Sql.Execute(
            connection,
            """
            INSERT INTO process_leases(pid,start_ticks,kind,state,created_at)
            VALUES($pid,$ticks,$kind,'running',$time);
            """,
            transaction,
            ("$pid", process.Id),
            ("$ticks", ticks),
            ("$kind", kind),
            ("$time", database.Now.ToString("O"))
        );
        long id = (long)(
            Sql.Scalar(connection, "SELECT last_insert_rowid();", transaction)
            ?? throw new InvalidOperationException()
        );
        Sql.Execute(
            connection,
            """
            INSERT INTO process_lease_owners(lease_id,owner_pid,owner_start_ticks,scope)
            VALUES($id,$ownerPid,$ownerTicks,$scope);
            """,
            transaction,
            ("$id", id),
            ("$ownerPid", ownerPid),
            ("$ownerTicks", ownerTicks),
            ("$scope", scope)
        );
        transaction.Commit();
        return id;
    }

    private string? ReserveHelperAdmissionIfNeeded()
    {
        if ((ambientScope.Value ?? "coordinator") != "helper")
        {
            return null;
        }

        string admissionId = Guid.NewGuid().ToString("N");
        (int ownerPid, long ownerTicks) = CurrentOwnerIdentity();
        using SqliteConnection connection = database.Open();
        using SqliteTransactionScope transaction = Sql.BeginTransaction(connection);
        if (!RunMetadata.IsGitWritable(connection, transaction))
        {
            throw new OperationException(
                "unavailable",
                "Git worktrees are not initialized or have been cleaned up."
            );
        }

        Sql.Execute(
            connection,
            """
            INSERT INTO process_helper_admissions(id,owner_pid,owner_start_ticks,created_at)
            VALUES($id,$ownerPid,$ownerTicks,$time);
            """,
            transaction,
            ("$id", admissionId),
            ("$ownerPid", ownerPid),
            ("$ownerTicks", ownerTicks),
            ("$time", database.Now.ToString("O"))
        );
        transaction.Commit();
        return admissionId;
    }

    private void ReleaseHelperAdmission(string? admissionId)
    {
        if (admissionId is null)
        {
            return;
        }

        using SqliteConnection connection = database.Open();
        Sql.Execute(
            connection,
            "DELETE FROM process_helper_admissions WHERE id=$id;",
            null,
            ("$id", admissionId)
        );
    }

    public void Complete(long leaseId)
    {
        using SqliteConnection connection = database.Open();
        Sql.Execute(
            connection,
            "UPDATE process_leases SET state='exited' WHERE id=$id;",
            null,
            ("$id", leaseId)
        );
    }

    public Task<ProcessResult> RunAsync(
        string executable,
        IReadOnlyList<string> arguments,
        string workingDirectory,
        TimeSpan timeout,
        CancellationToken cancellationToken = default
    ) =>
        RunAsync(
            executable,
            arguments,
            workingDirectory,
            timeout,
            standardInput: null,
            cancellationToken
        );

    internal async Task<ProcessResult> RunAsync(
        string executable,
        IReadOnlyList<string> arguments,
        string workingDirectory,
        TimeSpan timeout,
        string? standardInput,
        CancellationToken cancellationToken = default
    )
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(executable);
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(timeout, TimeSpan.Zero);

        cancellationToken.ThrowIfCancellationRequested();
        using CancellationTokenSource deadline = new(timeout, commandTimeProvider);
        using CancellationTokenSource linked = CancellationTokenSource.CreateLinkedTokenSource(
            cancellationToken,
            deadline.Token
        );
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
        string? admissionId = ReserveHelperAdmissionIfNeeded();
        try
        {
            linked.Token.ThrowIfCancellationRequested();
            process.Start();
        }
        catch
        {
            ReleaseHelperAdmission(admissionId);
            throw;
        }

        long lease;
        try
        {
            lease = RegisterCore(process, "command", admissionId);
        }
        catch
        {
            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: true);
            }

            await process
                .WaitForExitAsync(CancellationToken.None)
                .WaitAsync(TimeSpan.FromSeconds(5), CancellationToken.None)
                .ConfigureAwait(false);
            ReleaseHelperAdmission(admissionId);
            throw;
        }

        bool leaseSettled = false;
        Task<string> output = Task.FromResult("");
        Task<string> error = Task.FromResult("");
        bool timedOut = false;
        try
        {
            output = process.StandardOutput.ReadToEndAsync(
                cancellationToken: CancellationToken.None
            );
            error = process.StandardError.ReadToEndAsync(
                cancellationToken: CancellationToken.None
            );
            try
            {
                try
                {
                    if (standardInput is not null)
                    {
                        await process
                            .StandardInput.WriteAsync(standardInput.AsMemory(), linked.Token)
                            .ConfigureAwait(false);
                    }

                    process.StandardInput.Close();
                }
                catch (IOException)
                {
                    // Child may exit before consuming stdin; collect its actual exit and stderr.
                }

                await process.WaitForExitAsync(linked.Token).ConfigureAwait(false);
                await Task.WhenAll(output, error).WaitAsync(linked.Token).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (linked.IsCancellationRequested)
            {
                timedOut = deadline.IsCancellationRequested;
                try
                {
                    if (!process.HasExited)
                    {
                        try
                        {
                            process.Kill(entireProcessTree: true);
                        }
                        catch (InvalidOperationException) when (HasProcessExited(process))
                        {
                        }
                    }

                    await process
                        .WaitForExitAsync(CancellationToken.None)
                        .WaitAsync(TimeSpan.FromSeconds(5), CancellationToken.None)
                        .ConfigureAwait(false);
                    await Task.WhenAll(output, error)
                        .WaitAsync(TimeSpan.FromSeconds(5), CancellationToken.None)
                        .ConfigureAwait(false);
                }
                catch (Exception exception)
                {
                    PersistUnresolvedLease(lease);
                    leaseSettled = true;
                    throw new InvalidOperationException(
                        "Owned command or inherited pipes did not settle; replacement is blocked.",
                        exception
                    );
                }
            }

            Complete(lease);
            leaseSettled = true;
        }
        finally
        {
            if (!leaseSettled)
            {
                PersistUnresolvedLease(lease);
            }
        }
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
        }
        catch (OperationCanceledException exception)
        {
            try
            {
                exception.Data[CancellationEvidenceKey] = new JsonObject
                {
                    ["leaseId"] = lease,
                    ["stdout"] = RetainedOutput(
                        output.IsCompletedSuccessfully ? output.Result : null
                    ),
                    ["stderr"] = RetainedOutput(
                        error.IsCompletedSuccessfully ? error.Result : null
                    ),
                }.ToJsonString();
            }
            catch (Exception secondary)
            {
                exception.Data["LiteAgensh.CommandCancellationCaptureError"] =
                    Json.ExceptionDetails(secondary);
            }
            throw;
        }
        return new(
            process.ExitCode,
            await output.ConfigureAwait(false),
            await error.ConfigureAwait(false),
            timedOut
        );
    }

    internal static JsonObject RetainedOutput(string? text)
    {
        return new()
        {
            ["complete"] = text is not null,
            ["characters"] = text?.Length,
            ["truncated"] = text is null ? null : text.Length > CancellationOutputLimit,
            ["text"] = text is null ? null : text[..Math.Min(text.Length, CancellationOutputLimit)],
        };
    }

    public async Task ResolveSurvivorsAsync(CancellationToken cancellationToken = default)
    {
        for (int pass = 0; ; pass++)
        {
            await DrainPendingHelperAdmissionsAsync(cancellationToken).ConfigureAwait(false);
            List<(
                long Id,
                int Pid,
                long Ticks,
                string State,
                string Scope,
                int? OwnerPid,
                long? OwnerTicks
            )> leases = [];
            using (SqliteConnection connection = database.Open())
            {
                if (HasUnsettledSidecar(connection))
                {
                    throw new InvalidOperationException(
                        "An unsettled process sidecar blocks survivor recovery."
                    );
                }

                using SqliteCommand command = Sql.Command(
                    connection,
                    """
                    SELECT p.id,p.pid,p.start_ticks,p.state,
                        o.scope,o.owner_pid,o.owner_start_ticks
                    FROM process_leases p
                    LEFT JOIN process_lease_owners o ON o.lease_id=p.id
                    WHERE p.state<>'exited';
                    """
                );
                using ObservedSqliteReader reader = Sql.Reader(command);
                while (reader.Read())
                {
                    leases.Add(
                        (
                            reader.GetInt64(0),
                            reader.GetInt32(1),
                            reader.GetInt64(2),
                            reader.GetString(3),
                            reader.IsDBNull(4) ? "coordinator" : reader.GetString(4),
                            reader.IsDBNull(5) ? null : reader.GetInt32(5),
                            reader.IsDBNull(6) ? null : reader.GetInt64(6)
                        )
                    );
                }
            }

            foreach (
                (
                    long id,
                    int pid,
                    long ticks,
                    string state,
                    string scope,
                    int? ownerPid,
                    long? ownerTicks
                ) in leases
            )
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (state == "unresolved")
                {
                    throw new InvalidOperationException(
                        $"Process lease {id} needs explicit ownership resolution."
                    );
                }

                if (scope == "helper")
                {
                    if (
                        ownerPid is not int helperOwnerPid
                        || ownerTicks is not long helperOwnerTicks
                    )
                    {
                        throw new InvalidOperationException(
                            $"Process lease {id} is missing helper owner identity."
                        );
                    }

                    OwnerLiveness liveness = InspectOwner(helperOwnerPid, helperOwnerTicks);
                    if (liveness == OwnerLiveness.Live)
                    {
                        continue;
                    }

                    if (liveness == OwnerLiveness.Unknown)
                    {
                        throw new InvalidOperationException(
                            $"Process lease {id} helper owner liveness could not be verified."
                        );
                    }

                    EnsureLeaseStillSettleable(id);
                }

                await ReapOrCompleteLeaseAsync(id, pid, ticks, cancellationToken)
                    .ConfigureAwait(false);
            }

            using (SqliteConnection verify = database.Open())
            {
                if (!HasUnsettledLeases(verify, includeLiveHelpers: false))
                {
                    return;
                }
            }

            if (pass >= 1)
            {
                throw new InvalidOperationException(
                    "Owned process ownership remained unsettled during survivor recovery."
                );
            }
        }
    }

    private async Task DrainPendingHelperAdmissionsAsync(CancellationToken cancellationToken)
    {
        List<(string Id, int OwnerPid, long OwnerTicks)> admissions = [];
        using (SqliteConnection connection = database.Open())
        using (
            SqliteCommand command = Sql.Command(
                connection,
                "SELECT id,owner_pid,owner_start_ticks FROM process_helper_admissions;"
            )
        )
        using (ObservedSqliteReader reader = Sql.Reader(command))
        {
            while (reader.Read())
            {
                admissions.Add((reader.GetString(0), reader.GetInt32(1), reader.GetInt64(2)));
            }
        }

        foreach ((string admissionId, int ownerPid, long ownerTicks) in admissions)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!HasHelperAdmission(admissionId))
            {
                continue;
            }

            if (InspectOwner(ownerPid, ownerTicks) == OwnerLiveness.Live)
            {
                for (int attempt = 0; attempt < 10; attempt++)
                {
                    if (!HasHelperAdmission(admissionId))
                    {
                        break;
                    }

                    await Task.Delay(
                            TimeSpan.FromMilliseconds(50),
                            commandTimeProvider,
                            cancellationToken
                        )
                        .ConfigureAwait(false);
                }
            }

            if (!HasHelperAdmission(admissionId))
            {
                continue;
            }

            throw new InvalidOperationException(
                $"Helper launch admission {admissionId} needs explicit ownership resolution."
            );
        }
    }

    private async Task ReapOrCompleteLeaseAsync(
        long id,
        int pid,
        long ticks,
        CancellationToken cancellationToken
    )
    {
        Process process;
        try
        {
            process = Process.GetProcessById(pid);
        }
        catch (ArgumentException)
        {
            CompleteSurvivor(id);
            return;
        }

        using (process)
        {
            long processTicks;
            try
            {
                if (HasProcessExited(process))
                {
                    CompleteSurvivor(id);
                    return;
                }

                processTicks = process.StartTime.ToUniversalTime().Ticks;
            }
            catch (InvalidOperationException) when (HasProcessExited(process))
            {
                CompleteSurvivor(id);
                return;
            }
            catch (System.ComponentModel.Win32Exception exception)
                when (exception.NativeErrorCode == 87 || HasProcessExited(process))
            {
                CompleteSurvivor(id);
                return;
            }

            if (processTicks != ticks)
            {
                CompleteSurvivor(id);
                return;
            }

            if (!process.HasExited)
            {
                try
                {
                    process.Kill(entireProcessTree: true);
                }
                catch (InvalidOperationException) when (HasProcessExited(process))
                {
                }

                await process
                    .WaitForExitAsync(cancellationToken)
                    .WaitAsync(TimeSpan.FromSeconds(5), cancellationToken)
                    .ConfigureAwait(false);
            }
        }

        CompleteSurvivor(id);
    }

    private void EnsureLeaseStillSettleable(long leaseId)
    {
        using SqliteConnection connection = database.Open();
        if (HasUnsettledSidecar(connection))
        {
            throw new InvalidOperationException(
                "An unsettled process sidecar blocks survivor recovery."
            );
        }

        string? currentState =
            Sql.Scalar(
                connection,
                "SELECT state FROM process_leases WHERE id=$id;",
                null,
                ("$id", leaseId)
            ) as string;
        if (currentState == "unresolved")
        {
            throw new InvalidOperationException(
                $"Process lease {leaseId} needs explicit ownership resolution."
            );
        }
    }

    private void CompleteSurvivor(long leaseId)
    {
        using SqliteConnection connection = database.Open();
        if (HasUnsettledSidecar(connection))
        {
            throw new InvalidOperationException(
                "An unsettled process sidecar blocks survivor recovery."
            );
        }

        Sql.Execute(
            connection,
            "UPDATE process_leases SET state='exited' WHERE id=$id AND state='running';",
            null,
            ("$id", leaseId)
        );
        string? updatedState =
            Sql.Scalar(
                connection,
                "SELECT state FROM process_leases WHERE id=$id;",
                null,
                ("$id", leaseId)
            ) as string;
        if (updatedState != "exited")
        {
            throw new InvalidOperationException(
                $"Process lease {leaseId} needs explicit ownership resolution."
            );
        }
    }

    private void PersistUnresolvedLease(long leaseId)
    {
        try
        {
            using SqliteConnection connection = database.Open();
            Sql.Execute(
                connection,
                "UPDATE process_leases SET state='unresolved' WHERE id=$id;",
                null,
                ("$id", leaseId)
            );
        }
        catch
        {
            string sidecarDirectory = Path.Combine(database.StatePath, "unsettled-leases");
            Directory.CreateDirectory(sidecarDirectory);
            File.WriteAllText(
                Path.Combine(
                    sidecarDirectory,
                    leaseId.ToString(CultureInfo.InvariantCulture) + ".unresolved"
                ),
                "unresolved\n"
            );
        }
    }

    private static bool HasProcessExited(Process process)
    {
        if (process.HasExited)
        {
            return true;
        }

        try
        {
            process.Refresh();
            return process.HasExited;
        }
        catch (InvalidOperationException)
        {
            return true;
        }
    }

    private bool HasHelperAdmission(string admissionId)
    {
        using SqliteConnection connection = database.Open();
        return Sql.Scalar(
                connection,
                "SELECT 1 FROM process_helper_admissions WHERE id=$id;",
                null,
                ("$id", admissionId)
            )
            is not null;
    }

    private sealed class ScopeReset(string? previous) : IDisposable
    {
        public void Dispose() => ambientScope.Value = previous;
    }
}
