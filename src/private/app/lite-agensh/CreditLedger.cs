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
                "Native usage output must have an absolute path.",
                nameof(usagePath)
            );
        }

        using SqliteConnection connection = Open();
        using SqliteTransaction transaction = connection.BeginTransaction(deferred: false);
        List<CreditReservation> rows = Read(connection, transaction);
        if (rows.Any(row => row.State == "uncertain"))
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
            null,
            null
        );
        Sql.Execute(
            connection,
            """
            INSERT INTO credit_reservations(id,run_id,worker,allowance,run_allowance,state,
                usage_path,updated_at)
            VALUES($id,$run,$worker,$allowance,$runAllowance,'reserved',$path,$time);
            """,
            transaction,
            ("$id", reservation.Id),
            ("$run", runId),
            ("$worker", worker),
            ("$allowance", Format(allowance)),
            ("$runAllowance", Format(runAllowance)),
            ("$path", reservation.UsagePath),
            ("$time", DateTimeOffset.UtcNow.ToString("O"))
        );
        transaction.Commit();
        return reservation;
    }

    public void MarkStarted(string id) => Transition(id, "reserved", "started");

    public void ReleaseFailedStart(string id)
    {
        using SqliteConnection connection = Open();
        if (
            Sql.Execute(
                connection,
                """
                UPDATE credit_reservations SET state=
                    CASE WHEN actual IS NULL THEN 'released' ELSE 'reconciled' END,
                    updated_at=$time WHERE id=$id AND state='started';
                """,
                null,
                ("$id", id),
                ("$time", DateTimeOffset.UtcNow.ToString("O"))
            ) != 1
        )
        {
            throw new InvalidOperationException(
                "The known failed process start is not awaiting rollback."
            );
        }
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
            || rows.Any(row => row.State == "uncertain")
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
                WHERE id=$id AND state='started';
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

    public void ObserveUsage(string id, decimal actual)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(actual);
        using SqliteConnection connection = Open();
        using SqliteTransaction transaction = connection.BeginTransaction(deferred: false);
        CreditReservation previous = Read(connection, transaction).Single(row => row.Id == id);
        if (previous.State != "active" || actual < previous.Actual)
        {
            throw new OperationException(
                "credit_unknown",
                "Native cumulative usage is not monotonic on an active reservation."
            );
        }

        Sql.Execute(
            connection,
            "UPDATE credit_reservations SET actual=$actual,updated_at=$time WHERE id=$id;",
            transaction,
            ("$id", id),
            ("$actual", Format(actual)),
            ("$time", DateTimeOffset.UtcNow.ToString("O"))
        );
        transaction.Commit();
    }

    public void RequirePromptAllowance(string runId, string reservationId, decimal runAllowance)
    {
        IReadOnlyList<CreditReservation> rows = Reservations();
        CreditReservation own = rows.Single(row => row.Id == reservationId && row.RunId == runId);
        if (
            rows.Any(row => row.State == "uncertain")
            || own.State != "active"
            || own.Actual is null
        )
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

    public void Reconcile(
        string id,
        decimal actual,
        string reportIdentity,
        bool unassociatedZero = false,
        decimal? processBaseline = null
    )
    {
        ArgumentOutOfRangeException.ThrowIfNegative(actual);
        ArgumentException.ThrowIfNullOrWhiteSpace(reportIdentity);
        using SqliteConnection connection = Open();
        using SqliteTransaction transaction = connection.BeginTransaction(deferred: false);
        CreditReservation row = Read(connection, transaction).Single(item => item.Id == id);
        if (unassociatedZero && actual != 0)
        {
            throw new OperationException(
                "credit_unknown",
                "A process without an associated session must explicitly report zero usage."
            );
        }

        if (processBaseline is not null)
        {
            ArgumentOutOfRangeException.ThrowIfNegative(processBaseline.Value);
        }

        decimal cumulative = unassociatedZero ? row.Actual ?? 0 : (processBaseline ?? 0) + actual;
        if (row.State == "reconciled")
        {
            if (row.Actual != cumulative)
            {
                throw new InvalidOperationException(
                    "A final native report cannot revise reconciled usage."
                );
            }

            return;
        }

        if (row.State is not ("started" or "active" or "uncertain") || cumulative < row.Actual)
        {
            throw new InvalidOperationException(
                "Only a started native session can report nondecreasing cumulative usage."
            );
        }

        Sql.Execute(
            connection,
            """
            INSERT OR IGNORE INTO credit_reports(reservation_id,actual,usage_path,
                report_identity,scope,created_at)
            VALUES($id,$actual,$path,$identity,$scope,$time);
            """,
            transaction,
            ("$id", id),
            ("$actual", Format(actual)),
            ("$path", row.UsagePath),
            ("$identity", reportIdentity),
            (
                "$scope",
                unassociatedZero ? "unassociated-process"
                : processBaseline is null ? "session-cumulative"
                : "process-incremental"
            ),
            ("$time", DateTimeOffset.UtcNow.ToString("O"))
        );
        Sql.Execute(
            connection,
            """
            UPDATE credit_reservations SET actual=$actual,report_identity=$identity,
                state='reconciled',updated_at=$time WHERE id=$id;
            """,
            transaction,
            ("$actual", Format(cumulative)),
            ("$identity", reportIdentity),
            ("$id", id),
            ("$time", DateTimeOffset.UtcNow.ToString("O"))
        );
        transaction.Commit();
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
                uncertain = rows.Any(row => row.State == "uncertain"),
                reservations = run,
            }
        );
    }

    private static decimal Charge(CreditReservation row) =>
        row.State == "released" ? 0
        : row.State == "reconciled" ? row.Actual!.Value
        : Math.Max(row.Allowance, row.Actual ?? 0);

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
