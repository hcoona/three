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
    decimal? Actual,
    bool Shared = false,
    bool GrantOpen = false,
    decimal Overshoot = 0
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
        using SqliteTransactionScope transaction =
            Sql.BeginTransaction(connection, deferred: false);
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
                actual TEXT, report_identity TEXT, updated_at TEXT NOT NULL,
                shared INTEGER NOT NULL DEFAULT 0, grant_open INTEGER NOT NULL DEFAULT 0,
                overshoot TEXT NOT NULL DEFAULT '0');
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
            VALUES(1,2,$allowance,$identity);
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
        using (ObservedSqliteReader reader = Sql.Reader(command))
        {
            if (
                !reader.Read()
                || reader.GetInt32(0) is not (1 or 2)
                || Parse(reader.GetString(1)) != allowance
            )
            {
                throw new InvalidOperationException(
                    "Campaign schema or allowance differs; the durable budget cannot be reset."
                );
            }
        }

        if ((long)Sql.Scalar(
                connection,
                "SELECT schema_version FROM credit_campaign WHERE singleton=1;",
                transaction
            )! == 1)
        {
            Sql.Execute(
                connection,
                """
                ALTER TABLE credit_reservations ADD COLUMN shared INTEGER NOT NULL DEFAULT 0;
                ALTER TABLE credit_reservations ADD COLUMN grant_open INTEGER NOT NULL DEFAULT 0;
                ALTER TABLE credit_reservations ADD COLUMN overshoot TEXT NOT NULL DEFAULT '0';
                UPDATE credit_campaign SET schema_version=2 WHERE singleton=1;
                """,
                transaction
            );
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
        using ObservedSqliteReader reader = Sql.Reader(command);
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

    internal (decimal? BookedActual, NativeUsage Reported)? ReadRecoveryBaseline(string id)
    {
        using SqliteConnection connection = Open();
        using SqliteCommand command = Sql.Command(
            connection,
            """
            SELECT r.session_id,r.actual,u.reported_nano_aiu,u.reported_requests,u.status
            FROM credit_reservations r
            JOIN credit_sdk_usage u ON u.reservation_id=r.id AND u.session_id=r.session_id
            WHERE r.id=$id AND u.dispatch_count>0;
            """, null, ("$id", id));
        using ObservedSqliteReader reader = Sql.Reader(command);
        if (!reader.Read() || reader.IsDBNull(2) || reader.IsDBNull(3)
            || !double.TryParse(reader.GetString(2), NumberStyles.Float,
                CultureInfo.InvariantCulture, out double nano)
            || !double.IsFinite(nano) || nano < 0 || reader.GetInt64(3) < 0)
        {
            return null;
        }
        try
        {
            decimal? booked = reader.IsDBNull(1) ? null : Parse(reader.GetString(1));
            if (booked < 0)
            {
                return null;
            }
            return (booked, new(reader.GetString(0), nano,
                reader.IsDBNull(4) ? null : reader.GetString(4), reader.GetInt64(3)));
        }
        catch (Exception exception) when (exception is FormatException or OverflowException)
        {
            return null;
        }
    }

    public void RecordSdkUsage(string id, NativeUsage usage, bool final = false) =>
        RecordSdkUsage(id, usage, final, recovery: null);

    internal void RecordRecoveredSdkUsage(RecoveredNativeUsage recovered) =>
        RecordSdkUsage(recovered.Reservation.Id, recovered.Usage, final: true, recovered);

    private void RecordSdkUsage(
        string id, NativeUsage usage, bool final, RecoveredNativeUsage? recovery
    )
    {
        using SqliteConnection connection = Open();
        using SqliteTransactionScope transaction =
            Sql.BeginTransaction(connection, deferred: false);
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

        long? previousRequests = Sql.Scalar(
            connection,
            "SELECT reported_requests FROM credit_sdk_usage WHERE reservation_id=$id;",
            transaction,
            ("$id", id)
        ) as long?;
        Sql.Execute(
            connection,
            """
            UPDATE credit_sdk_usage SET reported_nano_aiu=$nano,status=$status,
                reported_requests=CASE
                    WHEN $requests IS NULL THEN reported_requests
                    WHEN reported_requests IS NULL OR $requests > reported_requests THEN $requests
                    ELSE reported_requests END
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
        if (dispatches == 0
            && (usage.TotalNanoAiu != 0 || usage.TotalUserRequests != 0
                || usage.Status is not (null or "complete")))
        {
            Sql.Execute(connection,
                """
                CREATE TABLE IF NOT EXISTS credit_cancellation_disqualifiers(
                    reservation_id TEXT PRIMARY KEY, reported_nano_aiu TEXT,
                    status TEXT, reported_requests INTEGER, created_at TEXT NOT NULL,
                    FOREIGN KEY(reservation_id) REFERENCES credit_reservations(id));
                INSERT OR IGNORE INTO credit_cancellation_disqualifiers(
                    reservation_id,reported_nano_aiu,status,reported_requests,created_at)
                VALUES($id,$nano,$status,$requests,$time);
                """, transaction,
                ("$id", id),
                ("$nano", usage.TotalNanoAiu?.ToString("R", CultureInfo.InvariantCulture)),
                ("$status", usage.Status), ("$requests", usage.TotalUserRequests),
                ("$time", DateTimeOffset.UtcNow.ToString("O")));
        }
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
            if (recovery is not null
                && (recovery.Reservation.RunId != row.RunId
                    || recovery.Reservation.Worker != row.Worker
                    || recovery.Reservation.SessionId != row.SessionId
                    || recovery.Reservation.UsagePath != row.UsagePath
                    || recovery.Reservation.Allowance != row.Allowance
                    || recovery.Reservation.Shared != row.Shared
                    || recovery.Reservation.State != "uncertain"
                    || row.State is not ("uncertain" or "reconciled")))
            {
                throw new OperationException(
                    "credit_unknown", "Settled recovery association changed."
                );
            }
            if (
                dispatches == 0
                || actual < (row.Actual ?? 0)
                || usage.TotalUserRequests < (previousRequests ?? 0)
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
        decimal overshoot = row.Overshoot;
        if (row.Shared)
        {
            if (!row.GrantOpen && actual != row.Actual
                && !(recovery is not null && row.State == "uncertain"))
            {
                MarkUnknown(connection, transaction, id);
                transaction.Commit();
                throw new OperationException(
                    "credit_unknown",
                    "Native usage changed outside its backed shared spending window."
                );
            }

            overshoot += Math.Max(0, actual - row.Allowance)
                - Math.Max(0, (row.Actual ?? 0) - row.Allowance);
        }
        Sql.Execute(
            connection,
            """
            INSERT OR IGNORE INTO credit_reports(
                reservation_id,actual,usage_path,report_identity,scope,created_at)
            VALUES($id,$actual,$path,$identity,'sdk-session-cumulative',$time);
            UPDATE credit_reservations SET actual=$actual,report_identity=$identity,
                state=$state,updated_at=$time,overshoot=$overshoot,
                grant_open=CASE WHEN $final THEN 0 ELSE grant_open END WHERE id=$id;
            """,
            transaction,
            ("$id", id),
            ("$actual", Format(actual)),
            ("$overshoot", Format(overshoot)),
            ("$final", final),
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
        using SqliteTransactionScope transaction =
            Sql.BeginTransaction(connection, deferred: false);
        RequirePromptAllowance(
            connection, transaction, runId, id, runAllowance, requireGrant: true
        );
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

    internal JsonObject CancelBeforeDispatch(
        CreditReservation expected, JsonObject proof, string inspection
    )
    {
        using SqliteConnection connection = Open();
        using SqliteTransactionScope transaction =
            Sql.BeginTransaction(connection, deferred: false);
        CreditReservation row = Read(connection, transaction)
            .Single(item => item.Id == expected.Id);
        if (Sql.Scalar(connection,
                "SELECT name FROM sqlite_master WHERE type='table' "
                    + "AND name='credit_cancellation_disqualifiers';", transaction) is string
            && Sql.Scalar(connection,
                "SELECT reservation_id FROM credit_cancellation_disqualifiers "
                    + "WHERE reservation_id=$id;", transaction, ("$id", row.Id)) is string)
        {
            throw new OperationException(
                "credit_unknown", "A prior contradictory native reading prohibits cancellation.");
        }
        if (
            row.RunId != expected.RunId || row.Worker != expected.Worker
            || row.SessionId != expected.SessionId || row.UsagePath != expected.UsagePath
            || row.Allowance != expected.Allowance || row.Shared != expected.Shared
            || row.Actual is not null || row.Overshoot != 0
        )
        {
            throw new OperationException("credit_unknown", "Cancellation association changed.");
        }

        using (SqliteCommand command = Sql.Command(
            connection,
            """
            SELECT u.session_id,u.reported_nano_aiu,u.status,u.dispatch_count,
                u.reported_requests,o.pid,o.start_ticks,
                (SELECT COUNT(*) FROM credit_sdk_usage WHERE session_id=u.session_id),
                (SELECT COUNT(*) FROM credit_reports WHERE reservation_id=u.reservation_id)
            FROM credit_sdk_usage u JOIN credit_sdk_owners o
                ON o.reservation_id=u.reservation_id WHERE u.reservation_id=$id;
            """,
            transaction, ("$id", row.Id)
        ))
        using (ObservedSqliteReader reader = Sql.Reader(command))
        {
            if (
                !reader.Read() || reader.GetString(0) != row.SessionId
                || reader.IsDBNull(1) || !NoDispatchCancellation.IsZero(reader.GetString(1))
                || (!reader.IsDBNull(2) && reader.GetString(2) != "complete")
                || reader.GetString(1) !=
                    proof["diagnostic"]!["reportedNanoAiu"]!.GetValue<string>()
                || (reader.IsDBNull(2) ? null : reader.GetString(2)) !=
                    proof["diagnostic"]!["aiCreditsStatus"]?.GetValue<string>()
                || reader.GetInt64(3) != 0 || reader.IsDBNull(4) || reader.GetInt64(4) != 0
                || reader.GetInt32(5) != proof["owner"]!["pid"]!.GetValue<int>()
                || reader.GetInt64(6) != proof["owner"]!["startTicks"]!.GetValue<long>()
                || reader.GetInt64(7) != 1 || reader.GetInt64(8) != 0
            )
            {
                throw new OperationException(
                    "credit_unknown", "Live SDK usage or ownership contradicts cancellation."
                );
            }
        }

        Sql.Execute(connection,
            """
            CREATE TABLE IF NOT EXISTS credit_cancellations(
                reservation_id TEXT PRIMARY KEY, reason TEXT NOT NULL, proof TEXT NOT NULL,
                inspection TEXT NOT NULL, application_build TEXT NOT NULL, created_at TEXT NOT NULL,
                FOREIGN KEY(reservation_id) REFERENCES credit_reservations(id));
            """, transaction);
        string? existing = Sql.Scalar(connection,
            "SELECT proof FROM credit_cancellations WHERE reservation_id=$id;",
            transaction, ("$id", row.Id)) as string;
        if (existing is not null)
        {
            if (
                row.State != "released" || row.GrantOpen
                || !JsonNode.DeepEquals(JsonNode.Parse(existing), proof)
                || Sql.Scalar(connection,
                    "SELECT reason FROM credit_cancellations WHERE reservation_id=$id;",
                    transaction, ("$id", row.Id)) as string != "verified-before-first-dispatch"
            )
            {
                throw new OperationException(
                    "credit_unknown", "Existing cancellation has contradictory retained evidence."
                );
            }
        }
        else
        {
            if (row.State is not ("active" or "uncertain"))
            {
                throw new OperationException(
                    "credit_unknown", "Only an opened, unsettled session can be cancelled."
                );
            }
            Sql.Execute(connection,
                """
                INSERT INTO credit_cancellations(
                    reservation_id,reason,proof,inspection,application_build,created_at)
                VALUES($id,'verified-before-first-dispatch',$proof,$inspection,$build,$time);
                UPDATE credit_reservations SET state='released',grant_open=0,updated_at=$time
                    WHERE id=$id;
                """, transaction,
                ("$id", row.Id), ("$proof", proof.ToJsonString()),
                ("$inspection", inspection),
                ("$build", typeof(CreditLedger).Assembly.ManifestModule.ModuleVersionId.ToString()),
                ("$time", DateTimeOffset.UtcNow.ToString("O")));
        }
        transaction.Commit();
        return new()
        {
            ["classification"] = "verified-before-first-dispatch",
            ["reservationId"] = row.Id,
            ["idempotent"] = existing is not null,
            ["measuredActual"] = null,
            ["reportedNanoAiu"] = proof["diagnostic"]!["reportedNanoAiu"]!.DeepClone(),
            ["aiCreditsStatus"] = proof["diagnostic"]!["aiCreditsStatus"]?.DeepClone(),
            ["proof"] = proof.DeepClone(),
        };
    }

    public CreditReservation Reserve(
        string runId,
        string worker,
        decimal allowance,
        decimal runAllowance,
        string usagePath,
        bool shared = false
    )
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(runId);
        ArgumentException.ThrowIfNullOrWhiteSpace(worker);
        if (shared ? allowance != 0 : allowance < 30)
        {
            throw new ArgumentOutOfRangeException(nameof(allowance));
        }
        if (!Path.IsPathFullyQualified(usagePath))
        {
            throw new ArgumentException(
                "The native evidence prefix must have an absolute path.",
                nameof(usagePath)
            );
        }

        using SqliteConnection connection = Open();
        using SqliteTransactionScope transaction =
            Sql.BeginTransaction(connection, deferred: false);
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
            null,
            Shared: shared
        );
        Sql.Execute(
            connection,
            """
            INSERT INTO credit_reservations(id,run_id,worker,allowance,run_allowance,state,
                usage_path,session_id,updated_at,shared)
            VALUES($id,$run,$worker,$allowance,$runAllowance,'reserved',
                $path,$session,$time,$shared);
            INSERT INTO credit_sdk_usage(reservation_id,session_id)
            VALUES($id,$session);
            """,
            transaction,
            ("$id", reservation.Id),
            ("$run", runId),
            ("$worker", worker),
            ("$shared", shared),
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

    internal async Task<CreditReservation> AcquireSharedWindowAsync(
        string id,
        CancellationToken cancellationToken,
        bool renew = false
    )
    {
        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();
            CreditReservation? grant = TryAcquireSharedWindow(id, renew);
            if (grant is not null)
            {
                return grant;
            }

            await Task.Delay(TimeSpan.FromMilliseconds(100), cancellationToken)
                .ConfigureAwait(false);
        }
    }

    private CreditReservation? TryAcquireSharedWindow(string id, bool renew)
    {
        using SqliteConnection connection = Open();
        using SqliteTransactionScope transaction =
            Sql.BeginTransaction(connection, deferred: false);
        List<CreditReservation> rows = Read(connection, transaction);
        CreditReservation own = rows.Single(row => row.Id == id);
        bool ready = Sql.Scalar(
            connection,
            """
            SELECT reservation_id FROM credit_sdk_usage WHERE reservation_id=$id
                AND (dispatch_count=0 OR (status='complete' AND reported_nano_aiu IS NOT NULL));
            """,
            transaction,
            ("$id", id)
        ) is string;
        if (
            !own.Shared
            || own.State is not ("reserved" or "active")
            || !ready
            || HasUnknown(connection, transaction)
        )
        {
            throw new OperationException(
                "credit_unknown",
                "A shared grant needs known ownership, complete usage and its saved association."
            );
        }

        if (own.GrantOpen && !renew)
        {
            transaction.Commit();
            return own;
        }

        decimal runAllowance = Parse((string)Sql.Scalar(
            connection,
            "SELECT run_allowance FROM credit_reservations WHERE id=$id;",
            transaction,
            ("$id", id)
        )!);
        CreditReservation[] run = [.. rows.Where(row => row.RunId == own.RunId)];
        decimal actual = own.Actual ?? 0;
        decimal actualRemaining = Math.Min(
            runAllowance - run.Sum(row => row.Actual ?? 0),
            campaignAllowance - rows.Sum(row => row.Actual ?? 0)
        );
        if (actualRemaining < 30)
        {
            throw new OperationException(
                "credit_exhausted",
                "The shared run or campaign cannot fund another 30-credit native window."
            );
        }

        decimal available = Math.Min(
            runAllowance - run.Sum(Charge),
            campaignAllowance - rows.Sum(Charge)
        );
        decimal grant = Math.Min(100, available + Math.Max(0, Charge(own) - actual));
        if (available < 0 || grant < 30)
        {
            transaction.Commit();
            return null;
        }

        Sql.Execute(
            connection,
            """
            UPDATE credit_reservations SET allowance=$allowance,grant_open=1,updated_at=$time
                WHERE id=$id;
            """,
            transaction,
            ("$allowance", Format(actual + grant)),
            ("$time", DateTimeOffset.UtcNow.ToString("O")),
            ("$id", id)
        );
        transaction.Commit();
        return own with { Allowance = actual + grant, GrantOpen = true };
    }

    internal void CloseSharedWindow(string id)
    {
        using SqliteConnection connection = Open();
        using SqliteTransactionScope transaction =
            Sql.BeginTransaction(connection, deferred: false);
        CreditReservation own = Read(connection, transaction).Single(row => row.Id == id);
        if (!own.Shared || !own.GrantOpen)
        {
            return;
        }

        if (
            own.State != "active"
            || own.Actual is null
            || Sql.Scalar(
                connection,
                """
                SELECT reservation_id FROM credit_sdk_usage WHERE reservation_id=$id
                    AND status='complete' AND dispatch_count>0 AND reported_nano_aiu IS NOT NULL;
                """,
                transaction,
                ("$id", id)
            ) is not string
        )
        {
            throw new OperationException(
                "credit_unknown",
                "An unfinished shared window cannot return its spending authority."
            );
        }

        Sql.Execute(
            connection,
            "UPDATE credit_reservations SET grant_open=0 WHERE id=$id;",
            transaction,
            ("$id", id)
        );
        transaction.Commit();
    }

    public void ReleaseUnstarted(string id)
    {
        using SqliteConnection connection = Open();
        if (
            Sql.Execute(
                connection,
                """
                UPDATE credit_reservations SET state=
                    CASE WHEN actual IS NULL THEN 'released' ELSE 'reconciled' END,
                    updated_at=$time,grant_open=0 WHERE id=$id AND state='reserved';
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
        using SqliteTransactionScope transaction =
            Sql.BeginTransaction(connection, deferred: false);
        List<CreditReservation> rows = Read(connection, transaction);
        CreditReservation previous = rows.Single(row => row.Id == id);
        if (
            previous.State != "reconciled"
            || previous.SessionId is null
            || (!previous.Shared && previous.Actual >= previous.Allowance)
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
        decimal extra = previous.Shared ? 0 : previous.Allowance - previous.Actual!.Value;
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
        using SqliteTransactionScope transaction = Sql.BeginTransaction(connection, deferred: true);
        RequirePromptAllowance(connection, transaction, runId, reservationId, runAllowance);
        transaction.Commit();
    }

    private void RequirePromptAllowance(
        SqliteConnection connection,
        SqliteTransaction transaction,
        string runId,
        string reservationId,
        decimal runAllowance,
        bool requireGrant = false
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
        if (
            HasUnknown(connection, transaction)
            || own.State != "active"
            || !ready
            || (requireGrant && own.Shared && !own.GrantOpen)
        )
        {
            throw new OperationException(
                "credit_unknown",
                "Unknown campaign usage or unverified native state blocks additional model calls."
            );
        }

        if (
            (!own.Shared && own.Actual >= own.Allowance)
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

    internal JsonObject ReadSdkOwner(string id)
    {
        using SqliteConnection connection = Open();
        using SqliteCommand command = Sql.Command(connection,
            "SELECT pid,start_ticks FROM credit_sdk_owners WHERE reservation_id=$id;",
            null, ("$id", id));
        using ObservedSqliteReader reader = Sql.Reader(command);
        if (!reader.Read())
        {
            throw new OperationException("credit_unknown", "The SDK owner identity is missing.");
        }
        return new()
        {
            ["pid"] = reader.GetInt32(0),
            ["startTicks"] = reader.GetInt64(1),
        };
    }

    public JsonObject Status(string runId, decimal runAllowance)
    {
        IReadOnlyList<CreditReservation> rows = Reservations();
        CreditReservation[] run = [.. rows.Where(row => row.RunId == runId)];
        using SqliteConnection connection = Open();
        JsonArray reported = [];
        JsonArray cancellations = [];
        if (Sql.Scalar(connection,
            "SELECT name FROM sqlite_master WHERE type='table' AND name='credit_cancellations';")
            is string)
        {
            using SqliteCommand command = Sql.Command(connection,
                """
                SELECT c.reservation_id,c.reason,c.inspection,c.application_build,c.created_at
                FROM credit_cancellations c JOIN credit_reservations r ON r.id=c.reservation_id
                WHERE r.run_id=$run ORDER BY c.reservation_id;
                """, null, ("$run", runId));
            using ObservedSqliteReader reader = Sql.Reader(command);
            while (reader.Read())
            {
                cancellations.Add(new JsonObject
                {
                    ["reservationId"] = reader.GetString(0),
                    ["classification"] = reader.GetString(1),
                    ["inspection"] = reader.GetString(2),
                    ["applicationBuild"] = reader.GetString(3),
                    ["createdAt"] = reader.GetString(4),
                    ["measuredActual"] = null,
                });
            }
        }
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
        using (ObservedSqliteReader reader = Sql.Reader(command))
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
                overshoot = run.Sum(row => row.Shared
                    ? row.Overshoot
                    : Math.Max(0, (row.Actual ?? 0) - row.Allowance)),
                outstandingAuthorization = run.Where(row =>
                    row.Shared && row.GrantOpen && row.State is not ("reconciled" or "released")
                ).Sum(row => Math.Max(0, row.Allowance - (row.Actual ?? 0))),
                uncertain = HasUnknown(connection, null),
                reported,
                cancellations,
                reservations = run,
            }
        );
    }

    private static decimal Charge(CreditReservation row) =>
        row.State == "released" ? 0
        : row.State == "reconciled" ? row.Actual!.Value
        : row.Shared && !row.GrantOpen ? row.Actual ?? 0
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
        using ObservedSqliteReader reader = Sql.Reader(command);
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
            SELECT id,run_id,worker,allowance,state,usage_path,session_id,actual,
                shared,grant_open,overshoot
            FROM credit_reservations ORDER BY rowid;
            """,
            transaction
        );
        using ObservedSqliteReader reader = Sql.Reader(command);
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
                    reader.IsDBNull(7) ? null : Parse(reader.GetString(7)),
                    reader.GetBoolean(8),
                    reader.GetBoolean(9),
                    Parse(reader.GetString(10))
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
        return Sql.Open(connection, "campaign");
    }
}
