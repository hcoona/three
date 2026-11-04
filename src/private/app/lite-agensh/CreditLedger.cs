using System.Diagnostics;
using System.Globalization;
using System.Text.Json.Nodes;
using Microsoft.Data.Sqlite;

namespace LiteAgensh;

public sealed record CreditReservation(
    string Id,
    string RunId,
    string Worker,
    decimal Allowance,
    string State,
    string UsagePath,
    string? SessionId,
    decimal? Actual
);

public sealed class CreditLedger
{
    private readonly string connectionString;
    private readonly decimal campaignAllowance;

    public CreditLedger(string path, decimal allowance)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        ArgumentOutOfRangeException.ThrowIfLessThan(allowance, 30);
        if (!Path.IsPathFullyQualified(path))
        {
            throw new ArgumentException("The campaign ledger path must be absolute.", nameof(path));
        }

        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        connectionString = new SqliteConnectionStringBuilder
        {
            DataSource = path,
            Mode = SqliteOpenMode.ReadWriteCreate,
            Pooling = false,
            DefaultTimeout = 5,
            ForeignKeys = true,
        }.ToString();
        campaignAllowance = allowance;
        using SqliteConnection connection = Open();
        Sql.Execute(connection, "PRAGMA journal_mode=WAL;");
        using SqliteTransaction transaction = connection.BeginTransaction(deferred: false);
        Sql.Execute(
            connection,
            """
            CREATE TABLE IF NOT EXISTS credit_campaign(
                singleton INTEGER PRIMARY KEY CHECK(singleton=1), schema_version INTEGER NOT NULL,
                allowance TEXT NOT NULL, identity TEXT NOT NULL);
            CREATE TABLE IF NOT EXISTS credit_reservations(
                id TEXT PRIMARY KEY, run_id TEXT NOT NULL, worker TEXT NOT NULL,
                allowance TEXT NOT NULL, run_allowance TEXT NOT NULL,
                state TEXT NOT NULL, usage_path TEXT NOT NULL, session_id TEXT,
                actual TEXT, report_identity TEXT, updated_at TEXT NOT NULL);
            CREATE TABLE IF NOT EXISTS credit_reports(
                id INTEGER PRIMARY KEY AUTOINCREMENT, reservation_id TEXT NOT NULL,
                actual TEXT NOT NULL, usage_path TEXT NOT NULL, report_identity TEXT NOT NULL,
                scope TEXT NOT NULL, created_at TEXT NOT NULL,
                FOREIGN KEY(reservation_id) REFERENCES credit_reservations(id),
                UNIQUE(reservation_id,report_identity));
            CREATE TABLE IF NOT EXISTS credit_sdk_usage(
                reservation_id TEXT PRIMARY KEY, session_id TEXT NOT NULL,
                reported_nano_aiu TEXT, status TEXT, dispatch_count INTEGER NOT NULL DEFAULT 0,
                reported_requests INTEGER,
                FOREIGN KEY(reservation_id) REFERENCES credit_reservations(id));
            CREATE TABLE IF NOT EXISTS credit_sdk_owners(
                reservation_id TEXT PRIMARY KEY, pid INTEGER NOT NULL, start_ticks INTEGER NOT NULL,
                FOREIGN KEY(reservation_id) REFERENCES credit_reservations(id));
            """,
            transaction
        );
        Sql.Execute(
            connection,
            """
            INSERT OR IGNORE INTO credit_campaign(singleton,schema_version,allowance,identity)
            VALUES(1,1,$allowance,$identity);
            """,
            transaction,
            ("$allowance", Format(allowance)),
            ("$identity", Guid.NewGuid().ToString("N"))
        );
        using (
            SqliteCommand command = Sql.Command(
                connection,
                "SELECT schema_version,allowance FROM credit_campaign WHERE singleton=1;",
                transaction
            )
        )
        using (SqliteDataReader reader = command.ExecuteReader())
        {
            if (
                !reader.Read()
                || reader.GetInt32(0) != 1
                || Parse(reader.GetString(1)) != allowance
            )
            {
                throw new InvalidOperationException(
                    "Campaign schema or allowance differs; the durable budget cannot be reset."
                );
            }
        }

        transaction.Commit();
        Identity = (string)(
            Sql.Scalar(connection, "SELECT identity FROM credit_campaign WHERE singleton=1;")
            ?? throw new InvalidOperationException("The durable campaign identity is missing.")
        );
    }

    public string Identity { get; }

    public void RequirePaidAdmission()
    {
        using SqliteConnection connection = Open();
        if (HasUnknown(connection, null))
        {
            throw new OperationException(
                "credit_unknown",
                "Unknown campaign usage blocks all new paid work, including calibration."
            );
        }
    }

    public bool HasSdkAssociation(string id)
    {
        using SqliteConnection connection = Open();
        return Sql.Scalar(
                connection,
                "SELECT reservation_id FROM credit_sdk_usage WHERE reservation_id=$id;",
                null,
                ("$id", id)
            ) is string;
    }

    public bool HasCompleteSdkUsage(string id)
    {
        using SqliteConnection connection = Open();
        return Sql.Scalar(
                connection,
                """
                SELECT r.id FROM credit_reservations r
                JOIN credit_sdk_usage u ON u.reservation_id=r.id AND u.session_id=r.session_id
                WHERE r.id=$id AND r.state='reconciled' AND r.actual IS NOT NULL
                    AND u.status='complete' AND u.reported_nano_aiu IS NOT NULL
                    AND u.dispatch_count>0;
                """,
                null,
                ("$id", id)
            ) is string;
    }

    internal (NativeUsage Usage, long Dispatches)? ReadCompleteSdkUsage(string id)
    {
        using SqliteConnection connection = Open();
        using SqliteCommand command = Sql.Command(
            connection,
            """
            SELECT r.session_id,r.actual,u.reported_nano_aiu,u.reported_requests,u.dispatch_count
            FROM credit_reservations r
            JOIN credit_sdk_usage u ON u.reservation_id=r.id AND u.session_id=r.session_id
            WHERE r.id=$id AND r.actual IS NOT NULL AND u.status='complete'
                AND u.reported_nano_aiu IS NOT NULL AND u.reported_requests IS NOT NULL
                AND u.dispatch_count>0;
            """,
            null,
            ("$id", id)
        );
        using SqliteDataReader reader = command.ExecuteReader();
        if (
            !reader.Read()
            || !double.TryParse(
                reader.GetString(2),
                NumberStyles.Float,
                CultureInfo.InvariantCulture,
                out double nano
            )
        )
        {
            return null;
        }

        NativeUsage usage = new(reader.GetString(0), nano, "complete", reader.GetInt64(3));
        try
        {
            return usage.RequireComplete() == Parse(reader.GetString(1))
                ? (usage, reader.GetInt64(4))
                : null;
        }
        catch (OperationException)
        {
            return null;
        }
    }

    public void RecordSdkUsage(string id, NativeUsage usage, bool final = false)
    {
        using SqliteConnection connection = Open();
        using SqliteTransaction transaction = connection.BeginTransaction(deferred: false);
        CreditReservation row = Read(connection, transaction).Single(item => item.Id == id);
        if (
            row.SessionId != usage.SessionId
            || Sql.Scalar(
                connection,
                "SELECT session_id FROM credit_sdk_usage WHERE reservation_id=$id;",
                transaction,
                ("$id", id)
            ) as string
                != usage.SessionId
            || row.State is not ("active" or "uncertain" or "reconciled")
        )
        {
            MarkUnknown(connection, transaction, id);
            transaction.Commit();
            throw new OperationException(
                "credit_unknown",
                "Native usage is not associated with this durable SDK reservation."
            );
        }

        Sql.Execute(
            connection,
            """
            UPDATE credit_sdk_usage SET reported_nano_aiu=$nano,status=$status,
                reported_requests=$requests
            WHERE reservation_id=$id;
            """,
            transaction,
            ("$nano", usage.TotalNanoAiu?.ToString("R", CultureInfo.InvariantCulture)),
            ("$status", usage.Status),
            ("$requests", usage.TotalUserRequests),
            ("$id", id)
        );
        long dispatches = (long)(
            Sql.Scalar(
                connection,
                "SELECT dispatch_count FROM credit_sdk_usage WHERE reservation_id=$id;",
                transaction,
                ("$id", id)
            ) ?? throw new InvalidOperationException()
        );
        if (dispatches == 0 && !final)
        {
            if (
                (usage.TotalNanoAiu is double initial && initial != 0)
                || usage.TotalUserRequests != 0
            )
            {
                MarkUnknown(connection, transaction, id);
                transaction.Commit();
                throw new OperationException(
                    "credit_unknown",
                    "Native usage appeared before this fresh session's first paid dispatch."
                );
            }

            // Empty-session metadata is retained, never booked as measured free usage.
            transaction.Commit();
            return;
        }

        decimal actual;
        try
        {
            actual = usage.RequireComplete();
            if (
                dispatches == 0
                || actual < (row.Actual ?? 0)
                || (row.State == "reconciled" && row.Actual != actual)
            )
            {
                throw new OperationException(
                    "credit_unknown",
                    "Usage regressed, revised a final total or lacks completed-call evidence."
                );
            }
        }
        catch
        {
            MarkUnknown(connection, transaction, id);
            transaction.Commit();
            throw;
        }

        string identity =
            usage.SessionId
            + ":nano:"
            + usage.TotalNanoAiu?.ToString("R", CultureInfo.InvariantCulture);
        Sql.Execute(
            connection,
            """
            INSERT OR IGNORE INTO credit_reports(
                reservation_id,actual,usage_path,report_identity,scope,created_at)
            VALUES($id,$actual,$path,$identity,'sdk-session-cumulative',$time);
            UPDATE credit_reservations SET actual=$actual,report_identity=$identity,
                state=$state,updated_at=$time WHERE id=$id;
            """,
            transaction,
            ("$id", id),
            ("$actual", Format(actual)),
            ("$path", row.UsagePath),
            ("$identity", identity),
            ("$state", final || row.State == "reconciled" ? "reconciled" : row.State),
            ("$time", DateTimeOffset.UtcNow.ToString("O"))
        );
        transaction.Commit();
    }

    public void BeginDispatch(string runId, string id, decimal runAllowance)
    {
        using SqliteConnection connection = Open();
        using SqliteTransaction transaction = connection.BeginTransaction(deferred: false);
        RequirePromptAllowance(connection, transaction, runId, id, runAllowance);
        Sql.Execute(
            connection,
            """
            UPDATE credit_sdk_usage SET dispatch_count=dispatch_count+1,status='pending'
            WHERE reservation_id=$id;
            """,
            transaction,
            ("$id", id)
        );
        transaction.Commit();
    }

    public CreditReservation Reserve(
        string runId,
        string worker,
        decimal allowance,
        decimal runAllowance,
        string usagePath
    )
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(runId);
        ArgumentException.ThrowIfNullOrWhiteSpace(worker);
        ArgumentOutOfRangeException.ThrowIfLessThan(allowance, 30);
        if (!Path.IsPathFullyQualified(usagePath))
        {
            throw new ArgumentException(
                "The native evidence prefix must have an absolute path.",
                nameof(usagePath)
            );
        }

        using SqliteConnection connection = Open();
        using SqliteTransaction transaction = connection.BeginTransaction(deferred: false);
        List<CreditReservation> rows = Read(connection, transaction);
        if (HasUnknown(connection, transaction))
        {
            throw new OperationException(
                "credit_unknown",
                "Unreconciled uncertain native usage blocks new reservations."
            );
        }

        object? previousLimit = Sql.Scalar(
            connection,
            "SELECT run_allowance FROM credit_reservations WHERE run_id=$run LIMIT 1;",
            transaction,
            ("$run", runId)
        );
        if (previousLimit is string text && Parse(text) != runAllowance)
        {
            throw new OperationException(
                "credit_limit_changed",
                "A run allowance cannot be reset between sessions."
            );
        }

        decimal campaignUsed = rows.Sum(Charge);
        decimal runUsed = rows.Where(row => row.RunId == runId).Sum(Charge);
        if (campaignUsed + allowance > campaignAllowance || runUsed + allowance > runAllowance)
        {
            throw new OperationException(
                "credit_exhausted",
                "The remaining run or campaign allowance cannot fund this session."
            );
        }

        CreditReservation reservation = new(
            Guid.NewGuid().ToString("N"),
            runId,
            worker,
            allowance,
            "reserved",
            Path.GetFullPath(usagePath),
            Guid.NewGuid().ToString("D"),
            null
        );
        Sql.Execute(
            connection,
            """
            INSERT INTO credit_reservations(id,run_id,worker,allowance,run_allowance,state,
                usage_path,session_id,updated_at)
            VALUES($id,$run,$worker,$allowance,$runAllowance,'reserved',$path,$session,$time);
            INSERT INTO credit_sdk_usage(reservation_id,session_id)
            VALUES($id,$session);
            """,
            transaction,
            ("$id", reservation.Id),
            ("$run", runId),
            ("$worker", worker),
            ("$allowance", Format(allowance)),
            ("$runAllowance", Format(runAllowance)),
            ("$path", reservation.UsagePath),
            ("$session", reservation.SessionId),
            ("$time", DateTimeOffset.UtcNow.ToString("O"))
        );
        RecordOwner(connection, transaction, reservation.Id);
        transaction.Commit();
        return reservation;
    }

    public void MarkStarted(string id) => Transition(id, "reserved", "started");

    public void ReleaseUnstarted(string id)
    {
        using SqliteConnection connection = Open();
        if (
            Sql.Execute(
                connection,
                """
                UPDATE credit_reservations SET state=
                    CASE WHEN actual IS NULL THEN 'released' ELSE 'reconciled' END,
                    updated_at=$time WHERE id=$id AND state='reserved';
                """,
                null,
                ("$id", id),
                ("$time", DateTimeOffset.UtcNow.ToString("O"))
            ) != 1
        )
        {
            throw new InvalidOperationException(
                "Only a reservation known not to have started can be released."
            );
        }
    }

    public CreditReservation ContinueExact(string id, string usagePath)
    {
        using SqliteConnection connection = Open();
        using SqliteTransaction transaction = connection.BeginTransaction(deferred: false);
        List<CreditReservation> rows = Read(connection, transaction);
        CreditReservation previous = rows.Single(row => row.Id == id);
        if (
            previous.State != "reconciled"
            || previous.SessionId is null
            || previous.Actual >= previous.Allowance
            || HasUnknown(connection, transaction)
        )
        {
            throw new OperationException(
                "credit_unknown",
                "Exact-session continuation needs reconciled cumulative usage and "
                    + "unspent original allowance."
            );
        }

        decimal runAllowance = Parse(
            (string)(
                Sql.Scalar(
                    connection,
                    "SELECT run_allowance FROM credit_reservations WHERE id=$id;",
                    transaction,
                    ("$id", id)
                ) ?? throw new InvalidOperationException()
            )
        );
        decimal extra = previous.Allowance - previous.Actual!.Value;
        if (
            rows.Sum(Charge) + extra > campaignAllowance
            || rows.Where(row => row.RunId == previous.RunId).Sum(Charge) + extra > runAllowance
        )
        {
            throw new OperationException(
                "credit_exhausted",
                "The original session reservation cannot be restored within remaining "
                    + "durable allowances."
            );
        }

        Sql.Execute(
            connection,
            """
            UPDATE credit_reservations SET state='reserved',usage_path=$path,
                report_identity=NULL,updated_at=$time
            WHERE id=$id;
            """,
            transaction,
            ("$path", Path.GetFullPath(usagePath)),
            ("$time", DateTimeOffset.UtcNow.ToString("O")),
            ("$id", id)
        );
        RecordOwner(connection, transaction, id);
        transaction.Commit();
        return previous with { State = "reserved", UsagePath = Path.GetFullPath(usagePath) };
    }

    public void MarkUncertain(string id)
    {
        using SqliteConnection connection = Open();
        Sql.Execute(
            connection,
            """
            UPDATE credit_reservations SET state='uncertain',updated_at=$time
            WHERE id=$id AND state IN ('started','active','uncertain');
            """,
            null,
            ("$id", id),
            ("$time", DateTimeOffset.UtcNow.ToString("O"))
        );
    }

    public void AttachSession(string id, string sessionId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sessionId);
        using SqliteConnection connection = Open();
        if (
            Sql.Execute(
                connection,
                """
                UPDATE credit_reservations SET session_id=$session,state='active',updated_at=$time
                WHERE id=$id AND state='started' AND session_id=$session;
                """,
                null,
                ("$session", sessionId),
                ("$id", id),
                ("$time", DateTimeOffset.UtcNow.ToString("O"))
            ) != 1
        )
        {
            throw new InvalidOperationException(
                "The native reservation is not awaiting a session association."
            );
        }
    }

    public void RequirePromptAllowance(string runId, string reservationId, decimal runAllowance)
    {
        using SqliteConnection connection = Open();
        using SqliteTransaction transaction = connection.BeginTransaction(deferred: true);
        RequirePromptAllowance(connection, transaction, runId, reservationId, runAllowance);
        transaction.Commit();
    }

    private void RequirePromptAllowance(
        SqliteConnection connection,
        SqliteTransaction transaction,
        string runId,
        string reservationId,
        decimal runAllowance
    )
    {
        IReadOnlyList<CreditReservation> rows = Read(connection, transaction);
        CreditReservation own = rows.Single(row => row.Id == reservationId && row.RunId == runId);
        bool ready =
            Sql.Scalar(
                connection,
                """
                SELECT reservation_id FROM credit_sdk_usage WHERE reservation_id=$id
                AND (dispatch_count=0 OR (status='complete' AND reported_nano_aiu IS NOT NULL));
                """,
                transaction,
                ("$id", reservationId)
            ) is string;
        if (HasUnknown(connection, transaction) || own.State != "active" || !ready)
        {
            throw new OperationException(
                "credit_unknown",
                "Unknown campaign usage or unverified native state blocks additional model calls."
            );
        }

        if (
            own.Actual >= own.Allowance
            || rows.Sum(row => row.Actual ?? 0) >= campaignAllowance
            || rows.Where(row => row.RunId == runId).Sum(row => row.Actual ?? 0) >= runAllowance
        )
        {
            throw new OperationException(
                "credit_exhausted",
                "The native session, run or cumulative campaign allowance has been spent."
            );
        }
    }

    public IReadOnlyList<CreditReservation> Reservations(string? runId = null)
    {
        using SqliteConnection connection = Open();
        return Read(connection, null).Where(row => runId is null || row.RunId == runId).ToArray();
    }

    public JsonObject Status(string runId, decimal runAllowance)
    {
        IReadOnlyList<CreditReservation> rows = Reservations();
        CreditReservation[] run = [.. rows.Where(row => row.RunId == runId)];
        using SqliteConnection connection = Open();
        JsonArray reported = [];
        using (
            SqliteCommand command = Sql.Command(
                connection,
                """
                SELECT u.reservation_id,u.session_id,u.reported_nano_aiu,u.status,u.dispatch_count
                FROM credit_sdk_usage u JOIN credit_reservations r ON r.id=u.reservation_id
                WHERE r.run_id=$run;
                """,
                null,
                ("$run", runId)
            )
        )
        using (SqliteDataReader reader = command.ExecuteReader())
        {
            while (reader.Read())
            {
                reported.Add(
                    new JsonObject
                    {
                        ["reservationId"] = reader.GetString(0),
                        ["sessionId"] = reader.GetString(1),
                        ["reportedNanoAiu"] = reader.IsDBNull(2) ? null : reader.GetString(2),
                        ["status"] = reader.IsDBNull(3) ? null : reader.GetString(3),
                        ["dispatches"] = reader.GetInt64(4),
                    }
                );
            }
        }

        return Json.SerializePayload(
            new
            {
                semantics = "native-soft-limit; one in-flight response may overshoot",
                campaignAllowance,
                campaignCommitted = rows.Sum(Charge),
                runAllowance,
                runCommitted = run.Sum(Charge),
                actualReported = run.Sum(row => row.Actual ?? 0),
                overshoot = run.Sum(row => Math.Max(0, (row.Actual ?? 0) - row.Allowance)),
                uncertain = HasUnknown(connection, null),
                reported,
                reservations = run,
            }
        );
    }

    private static decimal Charge(CreditReservation row) =>
        row.State == "released" ? 0
        : row.State == "reconciled" ? row.Actual!.Value
        : Math.Max(row.Allowance, row.Actual ?? 0);

    private static bool HasUnknown(SqliteConnection connection, SqliteTransaction? transaction)
    {
        if (
            (long)(
                Sql.Scalar(
                    connection,
                    """
                    SELECT EXISTS(SELECT 1 FROM credit_reservations r WHERE state='uncertain'
                        OR (state IN ('started','active') AND NOT EXISTS(
                            SELECT 1 FROM credit_sdk_usage u WHERE u.reservation_id=r.id)));
                    """,
                    transaction
                ) ?? 1L
            ) != 0
        )
        {
            return true;
        }

        using SqliteCommand command = Sql.Command(
            connection,
            """
            SELECT o.pid,o.start_ticks FROM credit_reservations r
            LEFT JOIN credit_sdk_owners o ON o.reservation_id=r.id
            WHERE r.state IN ('started','active');
            """,
            transaction
        );
        using SqliteDataReader reader = command.ExecuteReader();
        while (reader.Read())
        {
            if (reader.IsDBNull(0) || reader.IsDBNull(1))
            {
                return true;
            }

            try
            {
                using Process owner = Process.GetProcessById(reader.GetInt32(0));
                if (
                    owner.HasExited
                    || owner.StartTime.ToUniversalTime().Ticks != reader.GetInt64(1)
                )
                {
                    return true;
                }
            }
            catch (Exception exception)
                when (exception
                        is ArgumentException
                            or InvalidOperationException
                            or System.ComponentModel.Win32Exception
                )
            {
                return true;
            }
        }

        return false;
    }

    private static void RecordOwner(
        SqliteConnection connection,
        SqliteTransaction transaction,
        string id
    )
    {
        using Process owner = Process.GetCurrentProcess();
        Sql.Execute(
            connection,
            """
            INSERT INTO credit_sdk_owners(reservation_id,pid,start_ticks)
            VALUES($id,$pid,$ticks)
            ON CONFLICT(reservation_id) DO UPDATE
                SET pid=excluded.pid,start_ticks=excluded.start_ticks;
            """,
            transaction,
            ("$id", id),
            ("$pid", owner.Id),
            ("$ticks", owner.StartTime.ToUniversalTime().Ticks)
        );
    }

    private static void MarkUnknown(
        SqliteConnection connection,
        SqliteTransaction transaction,
        string id
    ) =>
        Sql.Execute(
            connection,
            """
            UPDATE credit_reservations SET state='uncertain'
            WHERE id=$id AND state IN ('started','active','uncertain','reconciled');
            """,
            transaction,
            ("$id", id)
        );

    private static List<CreditReservation> Read(
        SqliteConnection connection,
        SqliteTransaction? transaction
    )
    {
        List<CreditReservation> rows = [];
        using SqliteCommand command = Sql.Command(
            connection,
            """
            SELECT id,run_id,worker,allowance,state,usage_path,session_id,actual
            FROM credit_reservations ORDER BY rowid;
            """,
            transaction
        );
        using SqliteDataReader reader = command.ExecuteReader();
        while (reader.Read())
        {
            rows.Add(
                new(
                    reader.GetString(0),
                    reader.GetString(1),
                    reader.GetString(2),
                    Parse(reader.GetString(3)),
                    reader.GetString(4),
                    reader.GetString(5),
                    reader.IsDBNull(6) ? null : reader.GetString(6),
                    reader.IsDBNull(7) ? null : Parse(reader.GetString(7))
                )
            );
        }

        return rows;
    }

    private static string Format(decimal value) => value.ToString(CultureInfo.InvariantCulture);

    private static decimal Parse(string value) =>
        decimal.Parse(value, CultureInfo.InvariantCulture);

    private void Transition(string id, string from, string to)
    {
        using SqliteConnection connection = Open();
        if (
            Sql.Execute(
                connection,
                """
                UPDATE credit_reservations SET state=$to,updated_at=$time
                WHERE id=$id AND state=$from;
                """,
                null,
                ("$id", id),
                ("$from", from),
                ("$to", to),
                ("$time", DateTimeOffset.UtcNow.ToString("O"))
            ) != 1
        )
        {
            throw new InvalidOperationException(
                "The durable credit reservation is not in the expected state."
            );
        }
    }

    private SqliteConnection Open()
    {
        SqliteConnection connection = new(connectionString);
        connection.Open();
        return connection;
    }
}
