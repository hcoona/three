using System.Runtime.ExceptionServices;
using System.Text.Json.Nodes;
using Microsoft.Data.Sqlite;
using static LiteAgensh.Json;
using static LiteAgensh.Operation;

namespace LiteAgensh;

internal sealed class CoordinatorRecovery(
    RunDatabase database,
    ProcessRunner processes,
    CreditLedger credits,
    IGitWorkspace git,
    INativeWorkerFactory nativeWorkers,
    bool hadInitialLedgerBinding,
    string ledgerBinding
)
{
    public static string EnsureCoordinatorStorage(RunDatabase database, CreditLedger credits)
    {
        string binding = ExpectedLedgerBinding(database, credits);
        using SqliteConnection connection = database.Open();
        string? existing = Sql.GetMetadata(connection, null, RunMetadata.LedgerBindingKey);
        if (existing is not null && existing != binding)
        {
            throw new InvalidOperationException(
                "The run's campaign identity/path or credit allowances changed; budget "
                    + "reset is blocked."
            );
        }

        if (
            existing == binding
            && Sql.Scalar(
                connection,
                """
                SELECT COUNT(*) FROM sqlite_master
                WHERE type='table' AND name IN (
                    'native_sessions','native_attempts','coordinator_segments',
                    'coordinator_turns','coordinator_controls'
                );
                """
            )
                is long tables
            && tables == 5
        )
        {
            return binding;
        }

        using SqliteTransactionScope transaction = Sql.BeginTransaction(connection);
        BindNativeStorage(database, credits, connection, transaction, binding);
        CoordinatorSegmentRunner.EnsureSegmentStorage(connection, transaction);
        CoordinatorControls.EnsureSchema(connection, transaction);
        transaction.Commit();
        return binding;
    }

    private static string ExpectedLedgerBinding(RunDatabase database, CreditLedger credits) =>
        SerializePayload(
            new
            {
                path = Path.GetFullPath(database.Configuration.CampaignLedgerPath),
                campaignIdentity = credits.Identity,
                database.Configuration.RunAiCreditAllowance,
                database.Configuration.CampaignAiCreditAllowance,
            }
        ).ToJsonString();

    private static void BindNativeStorage(
        RunDatabase database,
        CreditLedger credits,
        SqliteConnection connection,
        SqliteTransaction transaction,
        string binding
    )
    {
        string? existing = Sql.GetMetadata(connection, transaction, RunMetadata.LedgerBindingKey);
        if (existing is not null && existing != binding)
        {
            throw new InvalidOperationException(
                "The run's campaign identity/path or credit allowances changed; budget "
                    + "reset is blocked."
            );
        }

        Sql.Execute(
            connection,
            """
            INSERT OR IGNORE INTO meta(key,value) VALUES($key,$binding);
            CREATE TABLE IF NOT EXISTS native_sessions(
                id INTEGER PRIMARY KEY AUTOINCREMENT, reservation_id TEXT NOT NULL,
                worker TEXT NOT NULL,
                session_id TEXT NOT NULL, recovery TEXT NOT NULL, initialize_json TEXT NOT NULL,
                created_at TEXT NOT NULL);
            CREATE TABLE IF NOT EXISTS native_attempts(
                usage_path TEXT PRIMARY KEY, reservation_id TEXT NOT NULL, worker TEXT NOT NULL,
                initialize_json TEXT, session_id TEXT, baseline_usage TEXT NOT NULL,
                created_at TEXT NOT NULL);
            """,
            transaction,
            ("$key", RunMetadata.LedgerBindingKey),
            ("$binding", binding)
        );
    }

    public JsonObject Reconcile(string reservationId, string? evidencePath)
    {
        RequireOriginalLedger();
        if (database.Meta.LedgerBinding != ledgerBinding)
        {
            throw new OperationException(
                "credit_unknown", "The retained campaign binding changed."
            );
        }
        JsonObject result = NoDispatchCancellation.Cancel(
            database, credits, reservationId, retained: true, evidencePath
        );
        result["accounting"] = credits.Status(
            database.RunId, database.Configuration.RunAiCreditAllowance
        );
        JsonObject response = Success(result,
            "Local authorization cancelled; provider-complete zero usage is not established.");
        response["nativeEvidenceKind"] = result["proof"]!["source"]!["evidenceKind"]!
            .DeepClone();
        return response;
    }

    private void RequireOriginalLedger()
    {
        if (!hadInitialLedgerBinding)
        {
            throw new OperationException(
                "credit_unknown", "Retained recovery requires its original campaign ledger."
            );
        }
    }

    public bool RequiresSettlementOnly(string mode)
    {
        using SqliteConnection connection = database.Open();
        bool hasSegments = Sql.Scalar(connection,
            "SELECT id FROM coordinator_segments LIMIT 1;") is not null;
        bool hasOpenSegment = Sql.Scalar(connection,
            "SELECT id FROM coordinator_segments WHERE ended_at IS NULL LIMIT 1;") is not null;
        if (mode == "run" && hasSegments)
        {
            throw new OperationException(
                "conflict",
                "Run already has lifecycle segments; use explicit resume or crash reconnect."
            );
        }
        if (mode != "run" && !hasSegments)
        {
            throw new OperationException(
                "conflict", "Resume/reconnect requires a prior run segment."
            );
        }
        if (mode == "resume"
            && (hasOpenSegment
                || database.Meta.Lifecycle is not ("paused" or "stopped" or "finalized")))
        {
            throw new OperationException(
                "conflict", "Explicit resume requires an already quiescent run."
            );
        }
        if (mode != "reconnect")
        {
            return false;
        }
        RequireOriginalLedger();
        if (!hasOpenSegment)
        {
            if (database.Meta.Lifecycle == "unresolved")
            {
                return true;
            }
            throw new OperationException(
                "conflict",
                "Crash reconnect requires an interrupted, unfinished segment; use "
                    + "fresh-session resume for a quiescent run."
            );
        }

        foreach (string worker in database.Configuration.WorkerIds)
        {
            _ = ReadReconnectReservation(worker);
        }
        return false;
    }

    public void RequireCleanupSettlement()
    {
        using SqliteConnection connection = database.Open();
        if (ProcessRunner.HasUnsettledLeases(connection, includeLiveHelpers: true))
        {
            throw new OperationException(
                "ownership_unresolved", "Cleanup refuses unresolved owned process leases."
            );
        }

        if (credits.Reservations(database.RunId).Any(row =>
            row.State is not ("reconciled" or "released")))
        {
            throw new OperationException(
                "credit_unknown", "Cleanup retains sessions whose native usage is not reconciled."
            );
        }
    }

    public JsonObject ClosedRecoveryResult(Exception? failure = null)
    {
        JsonObject result = failure is null ? Success()
            : failure is OperationCanceledException
                ? Failure("cancelled",
                    "Usage settlement was cancelled; retained uncertainty remains.")
                : ExpectedFailure(failure);
        result["data"] = SerializePayload(new
        {
            operation = "usage-settlement",
            runId = database.RunId,
            lifecycle = database.Meta.RawLifecycle,
            runUncertain = credits.Reservations(database.RunId)
                .Any(row => row.State is "reserved" or "started" or "active" or "uncertain"),
            accounting = credits.Status(database.RunId,
                database.Configuration.RunAiCreditAllowance),
        });
        return result;
    }

    public async Task SettleAsync(
        CancellationToken cancellationToken,
        bool allowUnknownUsage = false,
        bool recoverNativeUsage = false,
        bool settlementOnly = false,
        bool deferUnresolvedLifecycle = false
    )
    {
        CreditReservation? recovering = null;
        Exception? localFailure = null;
        JsonArray details = [];
        try
        {
            await processes.ResolveSurvivorsAsync(cancellationToken).ConfigureAwait(false);
            ReconcileOpeningRuntimes();
            if (!string.IsNullOrEmpty(database.Meta.OwnershipAmbiguity))
            {
                throw new OperationException(
                    "ownership_unresolved",
                    "A native startup failed to settle an unjournaled child; explicit "
                        + "ownership resolution is required."
                );
            }

            if (!settlementOnly
                && !string.IsNullOrEmpty(database.Meta.CompletionAmbiguity))
            {
                throw new OperationException(
                    "delivery_unresolved",
                    "A completed native turn could not be durably acknowledged; "
                        + "automatic replay/replacement is blocked."
                );
            }

            if (allowUnknownUsage)
            {
                return;
            }

            foreach (CreditReservation reservation in credits.Reservations(database.RunId))
            {
                cancellationToken.ThrowIfCancellationRequested();
                recovering = reservation;
                if (reservation.State == "reserved")
                {
                    credits.ReleaseUnstarted(reservation.Id);
                }
                else if (reservation.State is "started" or "active" or "uncertain")
                {
                    if (reservation.State != "uncertain")
                    {
                        credits.MarkUncertain(reservation.Id);
                    }

                    CreditReservation current;
                    try
                    {
                        if (!recoverNativeUsage || !credits.HasSdkAssociation(reservation.Id))
                        {
                            throw new OperationException(
                                "credit_unknown",
                                "Unknown usage remains reserved. Only exact, non-dispatch SDK "
                                    + "recovery can reconcile known SDK sessions; legacy unknown "
                                    + "reservations require owner disposition."
                            );
                        }

                        current = credits.Reservations()
                            .Single(row => row.Id == reservation.Id);
                        ValidateSdkRecoveryAssociation(current);
                    }
                    catch (OperationException exception)
                        when (settlementOnly && exception.Code == "credit_unknown")
                    {
                        localFailure ??= exception;
                        AddSettlementDetails(details, reservation.Worker, reservation.Id,
                            "usage-recovery-association", exception);
                        continue;
                    }

                    RecoveredNativeUsage usage;
                    try
                    {
                        usage = await nativeWorkers
                            .ReadSettledUsageAsync(
                                current,
                                git.GetWorkerPath(
                                    current.Worker == "calibration" ? "worker1" : current.Worker
                                ),
                                database.CreateSdkEvidencePath("recovery"),
                                cancellationToken
                            )
                            .ConfigureAwait(false);
                    }
                    catch (OperationException exception)
                        when (settlementOnly
                            && NativeWorkerFactory.IsPreActivationEligibilityRefusal(exception))
                    {
                        cancellationToken.ThrowIfCancellationRequested();
                        localFailure ??= exception;
                        AddSettlementDetails(details, reservation.Worker, reservation.Id,
                            "usage-recovery-eligibility", exception);
                        continue;
                    }

                    try
                    {
                        credits.RecordRecoveredSdkUsage(usage);
                    }
                    catch (OperationException exception)
                        when (settlementOnly && exception.Code == "credit_unknown")
                    {
                        localFailure ??= exception;
                        AddSettlementDetails(details, reservation.Worker, reservation.Id,
                            "usage-recovery-ledger", exception);
                    }
                }
            }

            if (localFailure is not null)
            {
                ExceptionDispatchInfo.Throw(localFailure);
            }
        }
        catch (Exception exception)
        {
            if (settlementOnly)
            {
                if (!ReferenceEquals(exception, localFailure))
                {
                    AddSettlementDetails(details, recovering?.Worker, recovering?.Id,
                        "usage-recovery", exception);
                }
                try
                {
                    database.Meta.WriteRecoveryFailureDetails(details);
                }
                catch (Exception diagnosticFailure) when (IsExpected(diagnosticFailure))
                {
                    // Retention failure must not replace the recovery's original cause.
                }
            }
            else if (!deferUnresolvedLifecycle)
            {
                database.Meta.SetLifecycle("unresolved");
            }
            throw;
        }
    }

    private void ReconcileOpeningRuntimes()
    {
        List<(string Key, JsonObject State)> openingRuntimes = [];
        using (SqliteConnection connection = database.Open())
        using (
            SqliteCommand command = Sql.Command(
                connection,
                """
                SELECT key,value FROM meta
                WHERE key LIKE 'native:runtime:%' AND key NOT LIKE 'native:runtime:%:session:%';
                """
            )
        )
        using (ObservedSqliteReader reader = Sql.Reader(command))
        {
            while (reader.Read())
            {
                string key = reader.GetString(0);
                if (
                    JsonNode.Parse(reader.GetString(1)) is JsonObject record
                    && record["state"]?.GetValue<string>() == "opening"
                )
                {
                    openingRuntimes.Add((key, record));
                }
            }
        }

        if (openingRuntimes.Count == 0)
        {
            return;
        }

        using SqliteConnection verify = database.Open();
        foreach ((string key, JsonObject record) in openingRuntimes)
        {
            if (
                record["ownerPid"] is JsonValue pidValue
                && record["ownerStartTicks"] is JsonValue ticksValue
                && ProcessRunner.InspectOwner(
                    pidValue.GetValue<int>(),
                    ticksValue.GetValue<long>()
                ) == ProcessRunner.OwnerLiveness.Live
            )
            {
                continue;
            }

            if (ProcessRunner.HasSettledKind(verify, key))
            {
                record["state"] = "settled";
                database.Meta.WriteJson(key, record);
                continue;
            }

            database.Meta.RecordOwnershipAmbiguity(
                $"Runtime {key} recorded opening state without a settled process lease."
            );
        }
    }

    private void ValidateSdkRecoveryAssociation(CreditReservation reservation)
    {
        using (SqliteConnection connection = database.Open())
        {
            string? associated =
                Sql.Scalar(
                    connection,
                    """
                    SELECT session_id FROM native_attempts WHERE usage_path=$path
                        AND reservation_id=$reservation AND worker=$worker
                        AND initialize_json=$initialize;
                    """,
                    null,
                    ("$path", reservation.UsagePath),
                    ("$reservation", reservation.Id),
                    ("$worker", reservation.Worker),
                    ("$initialize", NativeSdkSession.InitializeMarker)
                ) as string;
            if (
                reservation.SessionId is null
                || associated != reservation.SessionId
                || (
                    reservation.Worker != "calibration"
                    && ReadReconnectReservation(reservation.Worker)?.Id != reservation.Id
                )
            )
            {
                throw new OperationException(
                    "credit_unknown",
                    "Interrupted SDK association is incomplete or disagrees across ledgers."
                );
            }
        }
    }

    public CreditReservation? ReadReconnectReservation(string worker)
    {
        (string? session, string? reservationId) = database.Meta.ReadWorkerAssociation(worker);

        if (session is null && reservationId is null)
        {
            return null;
        }

        CreditReservation? reservation = credits
            .Reservations(database.RunId)
            .FirstOrDefault(row => row.Id == reservationId);
        if (
            string.IsNullOrWhiteSpace(session)
            || string.IsNullOrWhiteSpace(reservationId)
            || reservation is null
            || reservation.RunId != database.RunId
            || reservation.Worker != worker
            || reservation.SessionId != session
        )
        {
            throw new OperationException(
                "credit_unknown",
                "The saved native session/reservation association is incomplete or "
                    + "disagrees with the campaign run, worker or session."
            );
        }

        return reservation;
    }
}
