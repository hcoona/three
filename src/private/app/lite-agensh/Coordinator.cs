using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Data.Sqlite;

namespace LiteAgensh;

public sealed class Coordinator
{
    private readonly RunDatabase database;
    private readonly CollaborationOperations collaboration;
    private readonly GitOperations git;
    private readonly ProcessRunner processes;
    private readonly CreditLedger credits;
    private readonly INativeWorkerFactory nativeWorkers;
    private readonly string nativeEvidenceKind;

    private string PreflightReceiptKey =>
        nativeEvidenceKind == "native-sdk"
            ? "native:sdk_preflight_receipt"
            : "native:controlled_preflight_receipt";

    private string BillingReceiptKey =>
        nativeEvidenceKind == "native-sdk"
            ? "native:sdk_billing_receipt"
            : "native:controlled_billing_receipt";

    public Coordinator(
        RunDatabase database,
        CollaborationOperations collaboration,
        GitOperations git
    )
        : this(database, collaboration, git, null) { }

    internal Coordinator(
        RunDatabase database,
        CollaborationOperations collaboration,
        GitOperations git,
        Func<RunDatabase, ProcessRunner, CreditLedger, INativeWorkerFactory>? nativeWorkerFactory
    )
    {
        this.database = database;
        this.collaboration = collaboration;
        this.git = git;
        processes = new(database);
        credits = new(
            database.Configuration.CampaignLedgerPath,
            database.Configuration.CampaignAiCreditAllowance
        );
        nativeEvidenceKind = nativeWorkerFactory is null
            ? "native-sdk"
            : "controlled-native-dependency";
        using SqliteConnection connection = database.Open();
        using SqliteTransaction transaction = connection.BeginTransaction();
        string ledgerBinding = Json.SerializePayload(
                new
                {
                    path = Path.GetFullPath(database.Configuration.CampaignLedgerPath),
                    campaignIdentity = credits.Identity,
                    database.Configuration.RunAiCreditAllowance,
                    database.Configuration.CampaignAiCreditAllowance,
                }
            )
            .ToJsonString();
        string? existingBinding =
            Sql.Scalar(
                connection,
                "SELECT value FROM meta WHERE key='native:ledger_binding';",
                transaction
            ) as string;
        if (existingBinding is not null && existingBinding != ledgerBinding)
        {
            throw new InvalidOperationException(
                "The run's campaign identity/path or credit allowances changed; budget "
                    + "reset is blocked."
            );
        }

        Sql.Execute(
            connection,
            "INSERT OR IGNORE INTO meta(key,value) VALUES('native:ledger_binding',$binding);",
            transaction,
            ("$binding", ledgerBinding)
        );
        Sql.Execute(
            connection,
            """
            CREATE TABLE IF NOT EXISTS coordinator_segments(
                id INTEGER PRIMARY KEY AUTOINCREMENT, mode TEXT NOT NULL, state TEXT NOT NULL,
                started_at TEXT NOT NULL, deadline TEXT NOT NULL, ended_at TEXT, stop_reason TEXT);
            CREATE TABLE IF NOT EXISTS coordinator_controls(
                id INTEGER PRIMARY KEY AUTOINCREMENT, action TEXT NOT NULL, state TEXT NOT NULL,
                requested_at TEXT NOT NULL, completed_at TEXT, result TEXT);
            CREATE TABLE IF NOT EXISTS coordinator_turns(
                batch_id INTEGER PRIMARY KEY, worker TEXT NOT NULL, state TEXT NOT NULL,
                stop_reason TEXT, completed_at TEXT);
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
            transaction
        );
        transaction.Commit();
        nativeWorkers = nativeWorkerFactory is null
            ? new NativeWorkerFactory(database, processes, credits)
            : nativeWorkerFactory(database, processes, credits)
                ?? throw new ArgumentException("The native worker factory must not be null.");
    }

    public async Task<JsonObject> InitializeAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        using FileStream? ownership = TryOwn();
        if (ownership is null)
        {
            return Failure("conflict", "Another coordinator owns this run.");
        }

        try
        {
            await SettleRecoveryAsync(cancellationToken).ConfigureAwait(false);
            await git.InitializeAsync(cancellationToken).ConfigureAwait(false);
            return Success(
                new
                {
                    runId = database.RunId,
                    statePath = database.StatePath,
                    repositoryPath = database.Configuration.RepositoryPath,
                    workers = database.Configuration.WorkerIds,
                },
                "Run initialized; no native agent or model was started."
            );
        }
        catch (Exception exception) when (IsExpected(exception))
        {
            return Failure(exception);
        }
    }

    public JsonObject Status()
    {
        JsonArray workers = [];
        foreach (string worker in database.Configuration.WorkerIds)
        {
            workers.Add(
                new JsonObject
                {
                    ["id"] = worker,
                    ["lifecycle"] =
                        database.ReadMetadata($"worker:{worker}:lifecycle") ?? "not-started",
                    ["sessionId"] = database.ReadMetadata($"worker:{worker}:session"),
                    ["recovery"] = database.ReadMetadata($"worker:{worker}:recovery"),
                }
            );
        }

        JsonObject data = new()
        {
            ["runId"] = database.RunId,
            ["lifecycle"] = database.ReadMetadata("lifecycle") ?? "initialized",
            ["active"] = IsOwned(),
            ["stopReason"] = database.ReadMetadata("lifecycle:stop_reason"),
            ["workers"] = workers,
            ["credits"] = credits.Status(
                database.RunId,
                database.Configuration.RunAiCreditAllowance
            ),
            ["nativePreflight"] = ParseMetadata(PreflightReceiptKey),
            ["nativeBillingCalibration"] = ParseMetadata(BillingReceiptKey),
            ["delivery"] = ParseMetadata("lifecycle:delivery"),
            ["failure"] = database.ReadMetadata("lifecycle:failure"),
            ["settlementFailures"] = ParseMetadata("lifecycle:settlement_failures")
                ?? new JsonArray(),
        };
        return Success(data);
    }

    public JsonObject RequestControl(string action)
    {
        if (action is not ("pause" or "finalize"))
        {
            return Failure("invalid_arguments", "Control action must be pause or finalize.");
        }

        using SqliteConnection connection = database.Open();
        using SqliteTransaction transaction = connection.BeginTransaction();
        long? pending =
            Sql.Scalar(
                connection,
                "SELECT id FROM coordinator_controls WHERE action=$action AND "
                    + "state='pending' ORDER BY id LIMIT 1;",
                transaction,
                ("$action", action)
            ) as long?;
        long id;
        if (pending is not null)
        {
            id = pending.Value;
        }
        else
        {
            Sql.Execute(
                connection,
                """
                INSERT INTO coordinator_controls(action,state,requested_at)
                VALUES($action,'pending',$time);
                """,
                transaction,
                ("$action", action),
                ("$time", database.Now.ToString("O"))
            );
            id = (long)(
                Sql.Scalar(connection, "SELECT last_insert_rowid();", transaction)
                ?? throw new InvalidOperationException()
            );
        }
        transaction.Commit();
        return Success(
            new
            {
                requestId = id,
                action,
                state = "pending",
            },
            "The coordinator has not completed this request yet."
        );
    }

    public async Task<JsonObject> PreflightAsync(CancellationToken cancellationToken = default)
    {
        using FileStream? ownership = TryOwn();
        if (ownership is null)
        {
            return Failure("conflict", "Another coordinator owns this run.");
        }

        INativeWorker? native = null;
        JsonObject result;
        try
        {
            await SettleRecoveryAsync(cancellationToken, allowUnknownUsage: true)
                .ConfigureAwait(false);
            database.WriteMetadata("lifecycle", "preflighting");
            await git.InitializeAsync(cancellationToken).ConfigureAwait(false);
            native = nativeWorkers.Create(null, calibration: false);
            await native
                .StartAsync(git.GetWorkerPath("worker1"), null, cancellationToken)
                .ConfigureAwait(false);
            result = Success(
                new
                {
                    sessionId = native.SessionId,
                    modelPromptsSent = 0,
                    diagnostics = native.Diagnostic,
                },
                "Native session identity, cwd and applied limit verified without model sends. "
                    + "Empty-session metrics are not measured zero-cost usage."
            );
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception exception) when (IsExpected(exception))
        {
            result = Failure(exception);
        }
        finally
        {
            if (native is not null)
            {
                try
                {
                    await native.DisposeAsync().ConfigureAwait(false);
                }
                catch (Exception exception) when (IsExpected(exception))
                {
                    database.WriteMetadata("lifecycle", "unresolved");
                    database.WriteMetadata("lifecycle:failure", exception.Message);
                }
            }
        }

        if (!string.IsNullOrEmpty(database.ReadMetadata("lifecycle:ownership_ambiguity")))
        {
            database.WriteMetadata("lifecycle", "unresolved");
        }

        if (database.ReadMetadata("lifecycle") == "unresolved")
        {
            return Failure(
                "unavailable",
                "Preflight did not establish native metadata and settled ownership together. "
                    + "No model prompt was sent; diagnostics and paid associations are retained."
            );
        }

        database.WriteMetadata("lifecycle", "stopped");
        if (result["ok"]?.GetValue<bool>() == true)
        {
            database.WriteMetadata(
                PreflightReceiptKey,
                new JsonObject
                {
                    ["nativeEvidenceKind"] = nativeEvidenceKind,
                    ["binding"] = CreditPreflightBinding(),
                    ["sessionId"] = native?.SessionId,
                    ["verifiedAt"] = database.Now.ToString("O"),
                }.ToJsonString()
            );
        }

        return result;
    }

    public async Task<JsonObject> CalibrateAsync(CancellationToken cancellationToken = default)
    {
        using FileStream? ownership = TryOwn();
        if (ownership is null)
        {
            return Failure("conflict", "Another coordinator owns this run.");
        }

        INativeWorker? native = null;
        JsonObject result;
        CreditReservation? reservation = null;
        bool settled = false;
        try
        {
            await SettleRecoveryAsync(cancellationToken).ConfigureAwait(false);
            credits.RequirePaidAdmission();
            RequireCreditPreflight();
            if (HasBillingReceipt())
            {
                return Success(
                    ParseMetadata(BillingReceiptKey),
                    "This exact subject already has a complete billing calibration."
                );
            }

            await git.InitializeAsync(cancellationToken).ConfigureAwait(false);
            reservation = credits.Reserve(
                database.RunId,
                "calibration",
                30,
                database.Configuration.RunAiCreditAllowance,
                EvidencePath("calibration")
            );
            native = nativeWorkers.Create(reservation, calibration: true);
            database.WriteMetadata("lifecycle", "calibrating");
            await native
                .StartAsync(git.GetWorkerPath("worker1"), null, cancellationToken)
                .ConfigureAwait(false);
            credits.RequirePromptAllowance(
                database.RunId,
                native.ReservationId,
                database.Configuration.RunAiCreditAllowance
            );
            JsonObject response = await native
                .PromptAsync("Reply with exactly READY and nothing else.", cancellationToken)
                .ConfigureAwait(false);
            await native.VerifyCreditsAsync(cancellationToken).ConfigureAwait(false);
            if (response["text"]?.GetValue<string>().Trim() != "READY")
            {
                throw new OperationException(
                    "calibration_failed",
                    "The single bounded calibration response was not READY."
                );
            }

            result = Success(new { sessionId = native.SessionId });
        }
        catch (Exception exception) when (IsExpected(exception))
        {
            result = Failure(exception);
        }
        finally
        {
            if (native is not null)
            {
                try
                {
                    await native.DisposeAsync().ConfigureAwait(false);
                    settled = true;
                }
                catch (Exception exception) when (IsExpected(exception))
                {
                    database.WriteMetadata("lifecycle", "unresolved");
                    database.WriteMetadata("lifecycle:failure", exception.Message);
                }
            }
        }

        if (reservation is not null && !settled)
        {
            return Failure(
                "credit_or_ownership_unknown",
                "Calibration did not reconcile complete usage and settle its native process."
            );
        }

        if (
            result["ok"]?.GetValue<bool>() == true
            && reservation is not null
            && credits.HasCompleteSdkUsage(reservation.Id)
        )
        {
            database.WriteMetadata(
                BillingReceiptKey,
                new JsonObject
                {
                    ["nativeEvidenceKind"] = nativeEvidenceKind,
                    ["binding"] = CreditPreflightBinding(),
                    ["reservationId"] = reservation.Id,
                    ["sessionId"] = native?.SessionId,
                    ["verifiedAt"] = database.Now.ToString("O"),
                }.ToJsonString()
            );
        }

        if (settled)
        {
            database.WriteMetadata("lifecycle", "stopped");
        }

        return result;
    }

    public async Task<JsonObject> RunAsync(
        string mode,
        CancellationToken cancellationToken = default
    )
    {
        if (mode is not ("run" or "resume" or "reconnect"))
        {
            return Failure("invalid_arguments", "Mode must be run, resume or reconnect.");
        }

        using FileStream? ownership = TryOwn();
        if (ownership is null)
        {
            return Failure("conflict", "Another coordinator owns this run.");
        }

        try
        {
            ValidateMode(mode);
            if (mode == "reconnect")
            {
                foreach (string worker in database.Configuration.WorkerIds)
                {
                    _ = ReadReconnectReservation(worker);
                }
            }

            await SettleRecoveryAsync(cancellationToken, recoverNativeUsage: mode == "reconnect")
                .ConfigureAwait(false);
            credits.RequirePaidAdmission();
            RequireCreditPreflight();
            if (!HasBillingReceipt())
            {
                throw new OperationException(
                    "unavailable",
                    "A complete paid calibration for this exact build/SDK/runtime/configuration "
                        + "is required before ordinary model dispatch."
                );
            }
        }
        catch (Exception exception) when (IsExpected(exception))
        {
            return Failure(exception);
        }

        List<INativeWorker> workers = [];
        List<Task> loops = [];
        using CancellationTokenSource stop = CancellationTokenSource.CreateLinkedTokenSource(
            cancellationToken
        );
        string reason = "startup-failed";
        long segment = 0;
        bool finalize = false;
        Exception? failure = null;
        JsonArray settlementFailures = [];
        try
        {
            RecoverCompletedTurns();
            collaboration.RecoverInterrupted();
            DateTimeOffset deadline;
            (segment, deadline) = BeginSegment(mode);
            database.WriteMetadata("lifecycle:settlement_failures", "[]");
            TimeSpan remaining = deadline - database.Now;
            if (remaining <= TimeSpan.Zero)
            {
                reason = "deadline";
                finalize = true;
            }
            else
            {
                stop.CancelAfter(remaining);
                await git.InitializeAsync(stop.Token).ConfigureAwait(false);
                database.WriteMetadata("lifecycle", "preflighting");
                string[] ids = database.Configuration.WorkerIds;
                bool planning =
                    database.Configuration.PlanningSeconds > 0
                    && database.ReadMetadata("git:plan_integrated") != "true";
                bool released = !planning;
                Dictionary<string, CreditReservation> allocations = [];
                try
                {
                    for (int i = 0; i < ids.Length; i++)
                    {
                        CreditReservation? previous =
                            mode == "reconnect" ? ReadReconnectReservation(ids[i]) : null;
                        allocations.Add(
                            ids[i],
                            previous is null
                                ? Reserve(ids[i], ids.Length - i)
                                : credits.ContinueExact(previous.Id, EvidencePath(ids[i]))
                        );
                    }

                    foreach (string id in ids)
                    {
                        INativeWorker native = nativeWorkers.Create(
                            allocations[id],
                            calibration: false
                        );
                        workers.Add(native);
                        string? exact = mode == "reconnect" ? allocations[id].SessionId : null;
                        try
                        {
                            await native
                                .StartAsync(git.GetWorkerPath(id), exact, stop.Token)
                                .ConfigureAwait(false);
                        }
                        catch (OperationException exception)
                            when (exception.Code == "session_load_failed")
                        {
                            await native.DisposeAsync().ConfigureAwait(false);
                            workers.Remove(native);
                            stop.Token.ThrowIfCancellationRequested();
                            CreditReservation fresh = Reserve(id, 1);
                            INativeWorker replacement = nativeWorkers.Create(
                                fresh,
                                calibration: false
                            );
                            workers.Add(replacement);
                            await replacement
                                .StartAsync(
                                    git.GetWorkerPath(id),
                                    null,
                                    stop.Token,
                                    recoveryLabel: "fresh-summary-fallback: "
                                        + "exact-session load unavailable or failed"
                                )
                                .ConfigureAwait(false);
                        }
                    }
                }
                finally
                {
                    HashSet<string> owned = [.. workers.Select(worker => worker.ReservationId)];
                    foreach (
                        CreditReservation allocation in allocations.Values.Where(item =>
                            !owned.Contains(item.Id)
                        )
                    )
                    {
                        CreditReservation current = credits
                            .Reservations(database.RunId)
                            .Single(row => row.Id == allocation.Id);
                        if (current.State == "reserved")
                        {
                            credits.ReleaseUnstarted(allocation.Id);
                        }
                    }
                }

                database.WriteMetadata("lifecycle", "running");
                DateTimeOffset plannerDeadline = database.Now.AddSeconds(
                    database.Configuration.PlanningSeconds
                );
                if (database.Now >= deadline)
                {
                    reason = "deadline";
                    finalize = true;
                    await stop.CancelAsync().ConfigureAwait(false);
                }

                foreach ((string id, INativeWorker native) in ids.Zip(workers))
                {
                    if (!planning || id == "worker1")
                    {
                        collaboration.QueueKickoff(
                            id,
                            mode == "run"
                                ? "Initial peer kickoff"
                                : $"Durable {mode}; inspect state before replay"
                        );
                    }

                    loops.Add(
                        WorkerLoopAsync(
                            id,
                            native,
                            () => !planning || released || id == "worker1",
                            planning && id == "worker1",
                            stop.Token
                        )
                    );
                }

                HashSet<int> reminders = (deadline - database.Now).TotalSeconds <= 900 ? [900] : [];
                while (!stop.IsCancellationRequested)
                {
                    if (database.Now >= deadline)
                    {
                        reason = "deadline";
                        finalize = true;
                        break;
                    }

                    long? control = PendingControl(out string? action);
                    if (control is not null)
                    {
                        reason = action == "pause" ? "owner-pause" : "owner-finalize";
                        finalize = action == "finalize";
                        break;
                    }

                    string? closedBy = database.ReadMetadata("lifecycle:root_closed_by");
                    if (closedBy == "owner")
                    {
                        reason = "owner-root-closure";
                        finalize = true;
                        break;
                    }

                    if (closedBy is not null)
                    {
                        collaboration.ReopenWorkerClosedRoot();
                    }

                    Task? ended = loops.FirstOrDefault(task => task.IsCompleted);
                    if (ended is not null)
                    {
                        await ended.ConfigureAwait(false);
                        throw new OperationException(
                            "worker_stopped",
                            "A worker loop ended before run quiescence."
                        );
                    }

                    await git.ProcessPendingAsync(stop.Token).ConfigureAwait(false);
                    if (
                        planning
                        && !released
                        && (
                            database.ReadMetadata("git:plan_integrated") == "true"
                            || database.Now >= plannerDeadline
                        )
                    )
                    {
                        released = true;
                        foreach (string peer in ids.Skip(1))
                        {
                            collaboration.QueueKickoff(
                                peer,
                                "Planner release: integrated plan or planning deadline reached"
                            );
                        }
                    }

                    PublishReminders(deadline, reminders);
                    PublishCreditWarning();
                    await Task.Delay(
                            TimeSpan.FromMilliseconds(250),
                            database.TimeProvider,
                            stop.Token
                        )
                        .ConfigureAwait(false);
                }

                if (reason == "startup-failed")
                {
                    reason = cancellationToken.IsCancellationRequested
                        ? "caller-cancelled"
                        : "deadline";
                    finalize = !cancellationToken.IsCancellationRequested;
                }
            }
        }
        catch (OperationCanceledException) when (stop.IsCancellationRequested)
        {
            reason = cancellationToken.IsCancellationRequested ? "caller-cancelled" : "deadline";
            finalize = !cancellationToken.IsCancellationRequested;
        }
        catch (Exception exception)
        {
            failure = exception;
            reason = exception is OperationException operation
                ? operation.Code
                : "coordinator-failed";
        }
        finally
        {
            await stop.CancelAsync().ConfigureAwait(false);
            database.WriteMetadata("lifecycle", "pausing");
            database.WriteMetadata("lifecycle:stop_reason", reason);
            try
            {
                collaboration.Invoke(
                    "owner",
                    "message_send",
                    new()
                    {
                        ["recipient"] = "team",
                        ["body"] =
                            $"Run stopping: {reason}. New dispatch is disabled; "
                            + "native cancellation and owned-process settlement follow. "
                            + "Committed and partial work, tasks, board findings and "
                            + "session associations are retained. "
                            + "Explicit resume uses fresh sessions and durable state; "
                            + "a stop reason is not final-check success.",
                        ["urgent"] = true,
                    }
                );
            }
            catch (Exception exception)
            {
                failure ??= exception;
            }

            foreach (INativeWorker worker in workers)
            {
                try
                {
                    await worker.StopAsync().ConfigureAwait(false);
                }
                catch (Exception exception)
                {
                    failure ??= exception;
                    settlementFailures.Add(
                        new JsonObject
                        {
                            ["worker"] = database.Configuration.WorkerIds[workers.IndexOf(worker)],
                            ["reservationId"] = worker.ReservationId,
                            ["phase"] = "stop",
                            ["message"] = exception.Message,
                        }
                    );
                    database.WriteMetadata(
                        "lifecycle:settlement_failures",
                        settlementFailures.ToJsonString()
                    );
                    database.WriteMetadata("lifecycle", "unresolved");
                    database.WriteMetadata("lifecycle:failure", failure.Message);
                }
            }

            if (database.ReadMetadata("lifecycle") != "unresolved")
            {
                foreach (INativeWorker worker in workers)
                {
                    try
                    {
                        await worker.DisposeAsync().ConfigureAwait(false);
                    }
                    catch (Exception exception)
                    {
                        failure ??= exception;
                        settlementFailures.Add(
                            new JsonObject
                            {
                                ["worker"] =
                                    database.Configuration.WorkerIds[workers.IndexOf(worker)],
                                ["reservationId"] = worker.ReservationId,
                                ["phase"] = "dispose",
                                ["message"] = exception.Message,
                            }
                        );
                        database.WriteMetadata(
                            "lifecycle:settlement_failures",
                            settlementFailures.ToJsonString()
                        );
                        database.WriteMetadata("lifecycle", "unresolved");
                    }
                }
            }

            try
            {
                await Task.WhenAll(loops)
                    .WaitAsync(TimeSpan.FromSeconds(15), CancellationToken.None)
                    .ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (stop.IsCancellationRequested)
            {
                // Native cancellation is expected; all processes were settled separately above.
            }
            catch (Exception exception)
            {
                failure ??= exception;
                if (exception is TimeoutException)
                {
                    database.WriteMetadata("lifecycle", "unresolved");
                }
            }
        }

        if (failure is not null)
        {
            database.WriteMetadata("lifecycle:failure", failure.Message);
        }

        if (
            !string.IsNullOrEmpty(database.ReadMetadata("lifecycle:completion_ambiguity"))
            || !string.IsNullOrEmpty(database.ReadMetadata("lifecycle:ownership_ambiguity"))
        )
        {
            database.WriteMetadata("lifecycle", "unresolved");
        }

        if (database.ReadMetadata("lifecycle") == "unresolved")
        {
            EndSegment(segment, "unresolved", reason);
            return Failure(
                "ownership_or_usage_unresolved",
                failure?.Message ?? "Quiescence could not be established."
            );
        }

        JsonObject result;
        if (failure is not null)
        {
            database.WriteMetadata("lifecycle", "stopped");
            result = Failure(failure);
        }
        else if (finalize)
        {
            try
            {
                result = await FinalizeOwnedAsync(CancellationToken.None).ConfigureAwait(false);
            }
            catch (Exception exception) when (IsExpected(exception))
            {
                database.WriteMetadata("lifecycle", "stopped");
                database.WriteMetadata("lifecycle:failure", exception.Message);
                result = Failure(exception);
            }
        }
        else
        {
            database.WriteMetadata("lifecycle", "paused");
            result = Success(
                new { lifecycle = "paused", stopReason = reason },
                "Owned native processes settled; work is retained."
            );
        }

        CompleteControls(result, "pause");
        if (database.ReadMetadata("lifecycle") == "finalized")
        {
            CompleteControls(result, "finalize");
        }
        EndSegment(segment, database.ReadMetadata("lifecycle") ?? "stopped", reason);
        cancellationToken.ThrowIfCancellationRequested();
        return result;
    }

    public async Task<JsonObject> FinalizeAsync(CancellationToken cancellationToken = default)
    {
        using FileStream? ownership = TryOwn();
        if (ownership is null)
        {
            return RequestControl("finalize");
        }

        try
        {
            await SettleRecoveryAsync(cancellationToken).ConfigureAwait(false);
            JsonObject result = await FinalizeOwnedAsync(cancellationToken).ConfigureAwait(false);
            CompleteControls(result, "pause");
            CompleteControls(result, "finalize");
            return result;
        }
        catch (Exception exception) when (IsExpected(exception))
        {
            database.WriteMetadata("lifecycle", "unresolved");
            database.WriteMetadata("lifecycle:failure", exception.Message);
            return Failure(exception);
        }
    }

    public async Task<JsonObject> CleanupAsync(CancellationToken cancellationToken = default)
    {
        using FileStream? ownership = TryOwn();
        if (ownership is null)
        {
            return Failure("conflict", "Cleanup refuses an active coordinator.");
        }

        if (database.ReadMetadata("lifecycle") is not ("paused" or "finalized" or "stopped"))
        {
            return Failure(
                "ownership_unresolved",
                "Cleanup requires an inspected, quiescent retained run."
            );
        }

        try
        {
            using (SqliteConnection connection = database.Open())
            {
                if (
                    Convert.ToInt64(
                        Sql.Scalar(
                            connection,
                            "SELECT COUNT(*) FROM process_leases WHERE state<>'exited';"
                        ),
                        CultureInfo.InvariantCulture
                    ) != 0
                )
                {
                    return Failure(
                        "ownership_unresolved",
                        "Cleanup refuses unresolved owned process leases."
                    );
                }
            }

            if (
                credits
                    .Reservations(database.RunId)
                    .Any(row => row.State is not ("reconciled" or "released"))
            )
            {
                return Failure(
                    "credit_unknown",
                    "Cleanup retains sessions whose native usage is not reconciled."
                );
            }

            return Classify(await git.CleanupAsync(cancellationToken).ConfigureAwait(false));
        }
        catch (Exception exception) when (IsExpected(exception))
        {
            return Failure(exception);
        }
    }

    private static bool IsExpected(Exception exception) =>
        exception
            is OperationException
                or IOException
                or InvalidOperationException
                or JsonException
                or SqliteException
                or ArgumentException
                or TimeoutException
                or UnauthorizedAccessException
                or System.ComponentModel.Win32Exception;

    private JsonObject Success(object? data = null, string message = "") =>
        Classify(Operation.Success(data, message));

    private JsonObject Failure(string code, string message) =>
        Classify(Operation.Failure(code, message));

    private JsonObject Classify(JsonObject result)
    {
        result["nativeEvidenceKind"] = nativeEvidenceKind;
        return result;
    }

    private JsonObject Failure(Exception exception) =>
        Failure(
            exception is OperationException operation ? operation.Code : "unavailable",
            exception.Message
        );

    private async Task WorkerLoopAsync(
        string id,
        INativeWorker native,
        Func<bool> enabled,
        bool planner,
        CancellationToken cancellationToken
    )
    {
        DateTimeOffset lastWork = database.Now;
        DateTimeOffset lastNudge = database.Now;
        bool initialPlanner = planner;
        while (!cancellationToken.IsCancellationRequested)
        {
            if (!enabled())
            {
                await Task.Delay(
                        TimeSpan.FromMilliseconds(250),
                        database.TimeProvider,
                        cancellationToken
                    )
                    .ConfigureAwait(false);
                continue;
            }

            DeliveryBatch? batch = collaboration.ClaimBatch(id);
            if (batch is null)
            {
                if (
                    database.Now - lastWork >= TimeSpan.FromSeconds(600)
                    && database.Now - lastNudge >= TimeSpan.FromSeconds(600)
                )
                {
                    collaboration.QueueKickoff(id, WorkerInstructions.Idle(CurrentPhase()));
                    lastNudge = database.Now;
                }

                await Task.Delay(
                        TimeSpan.FromMilliseconds(250),
                        database.TimeProvider,
                        cancellationToken
                    )
                    .ConfigureAwait(false);
                continue;
            }

            bool nativeCompleted = false;
            bool modelAttempted = false;
            bool acknowledged = false;
            string completedStopReason = "unknown";
            try
            {
                string context = Render(collaboration.ReadSharedContext(id));
                JsonObject changes = await git.InvokeAsync(
                        id,
                        "change_list",
                        new(),
                        cancellationToken
                    )
                    .ConfigureAwait(false);
                context += "\n==== DURABLE CHANGES ====\n" + changes.ToJsonString();
                string text = WorkerInstructions.Compose(
                    database,
                    id,
                    context,
                    batch.Prompt,
                    initialPlanner
                );
                modelAttempted = true;
                credits.RequirePromptAllowance(
                    database.RunId,
                    native.ReservationId,
                    database.Configuration.RunAiCreditAllowance
                );
                JsonObject result = await native
                    .PromptAsync(text, cancellationToken)
                    .ConfigureAwait(false);
                nativeCompleted = true;
                initialPlanner = false;
                completedStopReason = result["stopReason"]?.GetValue<string>() ?? "unknown";
                RecordCompletedTurn(batch.Id, id, completedStopReason);
                collaboration.CompleteBatch(batch.Id);
                MarkAcknowledged(batch.Id);
                acknowledged = true;
                // Unknown usage after a completed model response forbids the next model call.
                await native.VerifyCreditsAsync(cancellationToken).ConfigureAwait(false);
                lastWork = database.Now;
                string? stopReason = result["stopReason"]?.GetValue<string>();
                if (stopReason is not ("end_turn" or "max_turn_requests"))
                {
                    throw new OperationException(
                        stopReason == "max_tokens" ? "native_limit" : "native_stopped",
                        $"Native worker stopped with {stopReason ?? "unknown stop reason"}."
                    );
                }
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                if (!nativeCompleted)
                {
                    collaboration.FailBatch(
                        batch.Id,
                        "Turn interrupted by coordinator stop; durable replay may repeat actions."
                    );
                }

                throw;
            }
            catch (Exception exception)
            {
                if (nativeCompleted)
                {
                    if (acknowledged)
                    {
                        throw;
                    }

                    // Never schedule a completed model action again due to acknowledgement failure.
                    try
                    {
                        RecordCompletedTurn(batch.Id, id, completedStopReason);
                    }
                    catch (Exception persistenceFailure) when (IsExpected(persistenceFailure))
                    {
                        database.WriteMetadata(
                            "lifecycle:completion_ambiguity",
                            batch.Id.ToString(CultureInfo.InvariantCulture)
                        );
                    }

                    throw new OperationException(
                        "post_delivery_failed",
                        $"Completed native turn retained without retry: {exception.Message}"
                    );
                }

                collaboration.FailBatch(batch.Id, exception.Message);
                if (!modelAttempted && IsExpected(exception))
                {
                    continue;
                }

                // FailBatch owns the pinned five-attempt/backoff policy, but an uncertain native
                // transport or credit state is not permission to issue another model call.
                throw;
            }
        }
    }

    private static string Render(object? value) =>
        value switch
        {
            null => "",
            string text => text,
            JsonNode node => node.ToJsonString(),
            _ => JsonSerializer.Serialize(value, Json.Options),
        };

    private string CreditPreflightBinding()
    {
        string subject = Json.SerializePayload(
                new
                {
                    sdk = NativeSdkSession.SdkVersion,
                    runtime = NativeSdkSession.RuntimeVersion,
                    accounting = "sdk-session-cumulative-nano-aiu-complete-v1",
                    nativeEvidenceKind,
                    build = typeof(Coordinator).Assembly.ManifestModule.ModuleVersionId,
                    database.Configuration.NativeExecutable,
                    database.Configuration.NativeArguments,
                    database.Configuration.NativeModel,
                    database.Configuration.NativePermissionPolicy,
                    database.Configuration.RunAiCreditAllowance,
                    database.Configuration.CampaignAiCreditAllowance,
                    database.Configuration.CampaignLedgerPath,
                    database.Configuration.WorkerCount,
                    database.Configuration.RepositoryPath,
                    campaignIdentity = credits.Identity,
                }
            )
            .ToJsonString();
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(subject)));
    }

    private void RequireCreditPreflight()
    {
        string? stored = database.ReadMetadata(PreflightReceiptKey);
        JsonObject? receipt = stored is null ? null : JsonNode.Parse(stored)?.AsObject();
        if (
            receipt?["binding"]?.GetValue<string>() != CreditPreflightBinding()
            || receipt?["nativeEvidenceKind"]?.GetValue<string>() != nativeEvidenceKind
        )
        {
            throw new OperationException(
                "unavailable",
                "A successful non-model SDK metadata preflight is required for this exact "
                    + "build, runtime, native invocation and accounting contract."
            );
        }
    }

    private bool HasBillingReceipt()
    {
        JsonObject? receipt = ParseMetadata(BillingReceiptKey) as JsonObject;
        string? reservationId = receipt?["reservationId"]?.GetValue<string>();
        return receipt?["binding"]?.GetValue<string>() == CreditPreflightBinding()
            && receipt?["nativeEvidenceKind"]?.GetValue<string>() == nativeEvidenceKind
            && reservationId is not null
            && credits.HasCompleteSdkUsage(reservationId)
            && credits
                .Reservations(database.RunId)
                .Any(row =>
                    row.Id == reservationId
                    && row.Worker == "calibration"
                    && row.Allowance == 30
                    && row.SessionId == receipt?["sessionId"]?.GetValue<string>()
                );
    }

    private FileStream? TryOwn()
    {
        try
        {
            return new FileStream(
                Path.Combine(database.StatePath, "coordinator.lock"),
                FileMode.OpenOrCreate,
                FileAccess.ReadWrite,
                FileShare.None
            );
        }
        catch (IOException)
        {
            return null;
        }
    }

    private bool IsOwned()
    {
        using FileStream? probe = TryOwn();
        return probe is null;
    }

    private async Task SettleRecoveryAsync(
        CancellationToken cancellationToken,
        bool allowUnknownUsage = false,
        bool recoverNativeUsage = false
    )
    {
        try
        {
            await processes.ResolveSurvivorsAsync(cancellationToken).ConfigureAwait(false);
            if (!string.IsNullOrEmpty(database.ReadMetadata("lifecycle:ownership_ambiguity")))
            {
                throw new OperationException(
                    "ownership_unresolved",
                    "A native startup failed to settle an unjournaled child; explicit "
                        + "ownership resolution is required."
                );
            }

            if (!string.IsNullOrEmpty(database.ReadMetadata("lifecycle:completion_ambiguity")))
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

                    if (!recoverNativeUsage || !credits.HasSdkAssociation(reservation.Id))
                    {
                        throw new OperationException(
                            "credit_unknown",
                            "Unknown usage remains reserved. Only exact, non-dispatch SDK "
                                + "recovery can reconcile known SDK sessions; legacy unknown "
                                + "reservations require owner disposition."
                        );
                    }

                    await RecoverSdkUsageAsync(reservation, cancellationToken)
                        .ConfigureAwait(false);
                }
            }
        }
        catch
        {
            database.WriteMetadata("lifecycle", "unresolved");
            throw;
        }
    }

    private async Task RecoverSdkUsageAsync(
        CreditReservation reservation,
        CancellationToken cancellationToken
    )
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
                    ("$initialize", "sdk:" + NativeSdkSession.SdkVersion)
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

        NativeUsage usage = await nativeWorkers
            .ReadSettledUsageAsync(
                reservation,
                git.GetWorkerPath(
                    reservation.Worker == "calibration" ? "worker1" : reservation.Worker
                ),
                EvidencePath("recovery"),
                cancellationToken
            )
            .ConfigureAwait(false);

        credits.RecordSdkUsage(reservation.Id, usage, final: true);
    }

    private CreditReservation? ReadReconnectReservation(string worker)
    {
        string? session;
        string? reservationId;
        using (SqliteConnection connection = database.Open())
        using (SqliteTransaction transaction = connection.BeginTransaction(deferred: true))
        {
            session =
                Sql.Scalar(
                    connection,
                    "SELECT value FROM meta WHERE key=$key;",
                    transaction,
                    ("$key", $"worker:{worker}:session")
                ) as string;
            reservationId =
                Sql.Scalar(
                    connection,
                    "SELECT value FROM meta WHERE key=$key;",
                    transaction,
                    ("$key", $"worker:{worker}:reservation")
                ) as string;
            transaction.Commit();
        }

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

    private CreditReservation Reserve(string worker, int remainingWorkers)
    {
        JsonObject status = credits.Status(
            database.RunId,
            database.Configuration.RunAiCreditAllowance
        );
        decimal runRemaining =
            database.Configuration.RunAiCreditAllowance
            - status["runCommitted"]!.GetValue<decimal>();
        decimal campaignRemaining =
            database.Configuration.CampaignAiCreditAllowance
            - status["campaignCommitted"]!.GetValue<decimal>();
        decimal allowance =
            Math.Floor(Math.Min(runRemaining, campaignRemaining) / remainingWorkers * 100) / 100;
        if (allowance < 30)
        {
            throw new OperationException(
                "credit_exhausted",
                "Remaining durable allowance is below the native 30-credit minimum per worker."
            );
        }

        string path = EvidencePath(worker);
        return credits.Reserve(
            database.RunId,
            worker,
            allowance,
            database.Configuration.RunAiCreditAllowance,
            path
        );
    }

    private string EvidencePath(string worker) =>
        Path.Combine(database.StatePath, "native", $"{worker}-{Guid.NewGuid():N}.sdk-evidence");

    private void ValidateMode(string mode)
    {
        using SqliteConnection connection = database.Open();
        long count = Convert.ToInt64(
            Sql.Scalar(connection, "SELECT COUNT(*) FROM coordinator_segments;"),
            CultureInfo.InvariantCulture
        );
        if (mode == "run" && count != 0)
        {
            throw new OperationException(
                "conflict",
                "Run already has lifecycle segments; use explicit resume or crash reconnect."
            );
        }

        if (mode != "run" && count == 0)
        {
            throw new OperationException(
                "conflict",
                "Resume/reconnect requires a prior run segment."
            );
        }

        if (
            mode == "resume"
            && database.ReadMetadata("lifecycle") is not ("paused" or "stopped" or "finalized")
        )
        {
            throw new OperationException(
                "conflict",
                "Explicit resume requires an already quiescent run."
            );
        }

        if (
            mode == "reconnect"
            && Sql.Scalar(
                connection,
                "SELECT id FROM coordinator_segments WHERE ended_at IS NULL LIMIT 1;"
            )
                is null
        )
        {
            throw new OperationException(
                "conflict",
                "Crash reconnect requires an interrupted, unfinished segment; use "
                    + "fresh-session resume for a quiescent run."
            );
        }
    }

    private (long Segment, DateTimeOffset Deadline) BeginSegment(string mode)
    {
        using SqliteConnection connection = database.Open();
        using SqliteTransaction transaction = connection.BeginTransaction();
        DateTimeOffset deadline = database.Now.AddSeconds(database.Configuration.DurationSeconds);
        if (mode == "reconnect")
        {
            string? existing =
                Sql.Scalar(
                    connection,
                    "SELECT deadline FROM coordinator_segments WHERE ended_at IS NULL "
                        + "ORDER BY id DESC LIMIT 1;",
                    transaction
                ) as string;
            if (existing is not null)
            {
                deadline = DateTimeOffset.Parse(existing, CultureInfo.InvariantCulture);
            }

            Sql.Execute(
                connection,
                """
                UPDATE coordinator_segments SET state='interrupted',ended_at=$time,
                    stop_reason='crash-reconnect'
                WHERE ended_at IS NULL;
                """,
                transaction,
                ("$time", database.Now.ToString("O"))
            );
        }

        Sql.Execute(
            connection,
            """
            INSERT INTO coordinator_segments(mode,state,started_at,deadline)
            VALUES($mode,'starting',$time,$deadline);
            """,
            transaction,
            ("$mode", mode),
            ("$time", database.Now.ToString("O")),
            ("$deadline", deadline.ToString("O"))
        );
        long id = (long)(
            Sql.Scalar(connection, "SELECT last_insert_rowid();", transaction)
            ?? throw new InvalidOperationException()
        );
        Sql.Execute(
            connection,
            "UPDATE meta SET value='false' WHERE key='git:finalized';",
            transaction
        );
        transaction.Commit();
        database.WriteMetadata("lifecycle:deadline", deadline.ToString("O"));
        return (id, deadline);
    }

    private void EndSegment(long segment, string state, string reason)
    {
        if (segment == 0)
        {
            return;
        }

        using SqliteConnection connection = database.Open();
        Sql.Execute(
            connection,
            """
            UPDATE coordinator_segments SET state=$state,stop_reason=$reason,ended_at=$time
            WHERE id=$id;
            """,
            null,
            ("$state", state),
            ("$reason", reason),
            ("$time", database.Now.ToString("O")),
            ("$id", segment)
        );
    }

    private long? PendingControl(out string? action)
    {
        using SqliteConnection connection = database.Open();
        using SqliteCommand command = Sql.Command(
            connection,
            "SELECT id,action FROM coordinator_controls WHERE state='pending' ORDER BY id LIMIT 1;"
        );
        using SqliteDataReader reader = command.ExecuteReader();
        if (!reader.Read())
        {
            action = null;
            return null;
        }

        action = reader.GetString(1);
        return reader.GetInt64(0);
    }

    private void CompleteControls(JsonObject result, string action)
    {
        using SqliteConnection connection = database.Open();
        Sql.Execute(
            connection,
            """
            UPDATE coordinator_controls SET state='completed',completed_at=$time,result=$result
            WHERE state='pending' AND action=$action;
            """,
            null,
            ("$time", database.Now.ToString("O")),
            ("$result", result.ToJsonString()),
            ("$action", action)
        );
    }

    private async Task<JsonObject> FinalizeOwnedAsync(CancellationToken cancellationToken)
    {
        database.WriteMetadata("lifecycle", "finalizing");
        JsonObject delivery = Classify(
            await git.FinalizeAsync(cancellationToken).ConfigureAwait(false)
        );
        database.WriteMetadata("lifecycle:delivery", delivery.ToJsonString());
        database.WriteMetadata("lifecycle", "finalized");
        return delivery;
    }

    private void RecordCompletedTurn(long batchId, string worker, string stopReason)
    {
        using SqliteConnection connection = database.Open();
        Sql.Execute(
            connection,
            """
            INSERT INTO coordinator_turns(batch_id,worker,state,stop_reason,completed_at)
            VALUES($batch,$worker,'native-complete',$reason,$time)
            ON CONFLICT(batch_id) DO UPDATE SET state='native-complete',
                stop_reason=excluded.stop_reason,completed_at=excluded.completed_at;
            """,
            null,
            ("$batch", batchId),
            ("$worker", worker),
            ("$reason", stopReason),
            ("$time", database.Now.ToString("O"))
        );
    }

    private void MarkAcknowledged(long batchId)
    {
        using SqliteConnection connection = database.Open();
        Sql.Execute(
            connection,
            "UPDATE coordinator_turns SET state='acknowledged' WHERE batch_id=$id;",
            null,
            ("$id", batchId)
        );
    }

    private void RecoverCompletedTurns()
    {
        List<long> ids = [];
        using (SqliteConnection connection = database.Open())
        using (
            SqliteCommand command = Sql.Command(
                connection,
                "SELECT batch_id FROM coordinator_turns WHERE state='native-complete';"
            )
        )
        using (SqliteDataReader reader = command.ExecuteReader())
        {
            while (reader.Read())
            {
                ids.Add(reader.GetInt64(0));
            }
        }

        foreach (long id in ids)
        {
            collaboration.CompleteBatch(id);
            MarkAcknowledged(id);
        }
    }

    private string CurrentPhase()
    {
        string? text = database.ReadMetadata("lifecycle:deadline");
        double remaining = text is null
            ? 0
            : (
                DateTimeOffset.Parse(text, CultureInfo.InvariantCulture) - database.Now
            ).TotalSeconds;
        int deliveryWindow = database.Configuration.DurationSeconds > 2700 ? 2700 : 300;
        return remaining <= deliveryWindow ? "delivery" : "development";
    }

    private void PublishReminders(DateTimeOffset deadline, HashSet<int> sent)
    {
        foreach (int beforeEnd in new[] { 900 })
        {
            if (
                beforeEnd < database.Configuration.DurationSeconds
                && (deadline - database.Now).TotalSeconds <= beforeEnd
                && sent.Add(beforeEnd)
            )
            {
                collaboration.Invoke(
                    "owner",
                    "message_send",
                    new()
                    {
                        ["recipient"] = "team",
                        ["body"] =
                            $"T-{beforeEnd / 60}m: The run will end "
                            + "and retain a checked local delivery. "
                            + "Integrate what is ready, keep the integration branch passing, "
                            + "avoid new large pieces, and record unfinished work as tasks. "
                            + "Final checks, not task closure, determine delivery success.",
                    }
                );
            }
        }
    }

    private void PublishCreditWarning()
    {
        JsonObject status = credits.Status(
            database.RunId,
            database.Configuration.RunAiCreditAllowance
        );
        decimal actual = status["actualReported"]!.GetValue<decimal>();
        if (
            actual >= database.Configuration.RunAiCreditAllowance * 0.9m
            && database.ReadMetadata("lifecycle:credit_warning") != "sent"
        )
        {
            JsonObject result = collaboration.Invoke(
                "owner",
                "message_send",
                new()
                {
                    ["recipient"] = "team",
                    ["body"] =
                        "At least 90% of the run's native-reported AI Credits have been spent. "
                        + "Wrap up checked work, retain commits and record open "
                        + "tasks/board findings. "
                        + "Do not start large new pieces. Native soft limits may "
                        + "overshoot one in-flight response.",
                    ["urgent"] = true,
                }
            );
            if (result["ok"]?.GetValue<bool>() != true)
            {
                throw new OperationException(
                    "lifecycle_alert_failed",
                    result["message"]?.GetValue<string>() ?? "Credit warning could not be recorded."
                );
            }

            database.WriteMetadata("lifecycle:credit_warning", "sent");
        }
    }

    private JsonNode? ParseMetadata(string key)
    {
        string? value = database.ReadMetadata(key);
        return value is null ? null : JsonNode.Parse(value);
    }
}
