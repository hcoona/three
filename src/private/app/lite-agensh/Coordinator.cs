using System.Diagnostics;
using System.Text.Json.Nodes;
using Microsoft.Data.Sqlite;
using static LiteAgensh.Json;

namespace LiteAgensh;

public sealed class Coordinator
{
    private readonly RunDatabase database;
    private readonly CollaborationOperations collaboration;
    private readonly IGitWorkspace git;
    private readonly CreditLedger credits;
    private readonly INativeWorkerFactory nativeWorkers;
    private readonly Action<IReadOnlyList<Task>>? observeWorkerCompletion;
    private readonly string nativeEvidenceKind;
    private readonly NativeQualification qualification;
    private readonly CoordinatorRecovery recovery;
    private readonly CoordinatorControls controls;

    public Coordinator(
        RunDatabase database,
        CollaborationOperations collaboration,
        GitOperations git
    )
        : this(database, collaboration, (IGitWorkspace)git, null) { }

    internal Coordinator(
        RunDatabase database,
        CollaborationOperations collaboration,
        GitOperations git,
        Func<RunDatabase, ProcessRunner, CreditLedger, INativeWorkerFactory>? nativeWorkerFactory,
        Action<IReadOnlyList<Task>>? observeWorkerCompletion = null
    )
        : this(
            database,
            collaboration,
            (IGitWorkspace)git,
            nativeWorkerFactory,
            observeWorkerCompletion
        )
    { }

    internal Coordinator(
        RunDatabase database,
        CollaborationOperations collaboration,
        IGitWorkspace git,
        Func<RunDatabase, ProcessRunner, CreditLedger, INativeWorkerFactory>? nativeWorkerFactory,
        Action<IReadOnlyList<Task>>? observeWorkerCompletion = null
    )
    {
        this.database = database;
        this.collaboration = collaboration;
        this.git = git;
        this.observeWorkerCompletion = observeWorkerCompletion;
        ProcessRunner processes = new(database);
        bool hadInitialLedgerBinding = database.Meta.LedgerBinding is not null;
        credits = new(
            database.Configuration.CampaignLedgerPath,
            database.Configuration.CampaignAiCreditAllowance,
            requireExisting: hadInitialLedgerBinding
        );
        nativeEvidenceKind = nativeWorkerFactory is null
            ? "native-sdk"
            : "controlled-native-dependency";
        using SqliteConnection connection = database.Open();
        using SqliteTransactionScope transaction = Sql.BeginTransaction(connection);
        string ledgerBinding = CoordinatorRecovery.BindNativeStorage(
            database, credits, connection, transaction
        );
        CoordinatorSegmentRunner.EnsureSegmentStorage(connection, transaction);
        CoordinatorControls.EnsureSchema(connection, transaction);
        transaction.Commit();
        controls = new(database, git, Classify);
        nativeWorkers = nativeWorkerFactory is null
            ? new NativeWorkerFactory(database, processes, credits)
            : nativeWorkerFactory(database, processes, credits)
                ?? throw new ArgumentException("The native worker factory must not be null.");
        recovery = new(database, processes, credits, git, nativeWorkers,
            hadInitialLedgerBinding, ledgerBinding);
        qualification = new(database, git, credits, nativeWorkers, recovery, nativeEvidenceKind);
    }

    public JsonObject Reconcile(string reservationId, string? evidencePath = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(reservationId);
        using FileStream? ownership = TryOwn();
        if (ownership is null)
        {
            return Failure("conflict", "Exclusive coordinator ownership is unavailable.");
        }
        try
        {
            return recovery.Reconcile(reservationId, evidencePath);
        }
        catch (Exception exception) when (IsExpected(exception))
        {
            return Failure(exception);
        }
    }

    public async Task<JsonObject> InitializeAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        using FileStream? ownership = TryOwn();
        if (ownership is null)
        {
            return Failure("conflict", "Exclusive coordinator ownership is unavailable.");
        }

        try
        {
            await recovery.SettleAsync(cancellationToken).ConfigureAwait(false);
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
        JsonObject data = new()
        {
            ["runId"] = database.RunId,
            ["lifecycle"] = database.Meta.Lifecycle,
            ["active"] = IsOwned(),
            ["stopReason"] = database.Meta.StopReason,
            ["deadlineDrain"] = ReadDeadlineDrain(),
            ["workers"] = database.Meta.ReadWorkerStatuses(),
            ["credits"] = credits.Status(
                database.RunId,
                database.Configuration.RunAiCreditAllowance
            ),
            ["nativePreflight"] = qualification.ReadPreflightReceipt(),
            ["nativeBillingCalibration"] = qualification.ReadBillingReceipt(),
            ["delivery"] = database.Meta.ReadJson(RunMetadata.DeliveryKey),
            ["failure"] = database.Meta.Failure,
            ["settlementFailures"] = database.Meta.ReadJson(RunMetadata.SettlementFailuresKey)
                ?? new JsonArray(),
        };
        return Success(data);
    }

    public JsonObject RequestControl(string action) => controls.Request(action);

    public async Task<JsonObject> PreflightAsync(CancellationToken cancellationToken = default)
    {
        using FileStream? ownership = TryOwn();
        return ownership is null
            ? Failure("conflict", "Exclusive coordinator ownership is unavailable.")
            : Classify(await qualification.PreflightAsync(cancellationToken).ConfigureAwait(false));
    }

    public async Task<JsonObject> CalibrateAsync(CancellationToken cancellationToken = default)
    {
        using FileStream? ownership = TryOwn();
        return ownership is null
            ? Failure("conflict", "Exclusive coordinator ownership is unavailable.")
            : Classify(await qualification.CalibrateAsync(cancellationToken).ConfigureAwait(false));
    }

    public async Task<JsonObject> RunAsync(
        string mode,
        CancellationToken cancellationToken = default
    )
    {
        using Activity? activity =
            ApplicationTelemetry.Source.StartActivity("coordinator.run");
        if (mode is "run" or "resume" or "reconnect")
        {
            activity?.SetTag("liteagensh.mode", mode);
        }
        try
        {
            JsonObject result = await RunCoreAsync(mode, activity, cancellationToken)
                .ConfigureAwait(false);
            activity?.SetTag("liteagensh.result.code", result["code"]?.GetValue<string>());
            if (result["ok"]?.GetValue<bool>() == false)
            {
                activity?.SetStatus(ActivityStatusCode.Error);
            }
            return result;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            activity?.SetTag("liteagensh.result.code", "cancelled");
            throw;
        }
        catch (Exception exception)
        {
            activity?.SetStatus(ActivityStatusCode.Error);
            activity?.SetTag("error.type", exception.GetType().FullName);
            throw;
        }
    }

    private async Task<JsonObject> RunCoreAsync(
        string mode,
        Activity? activity,
        CancellationToken cancellationToken
    )
    {
        if (mode is not ("run" or "resume" or "reconnect"))
        {
            return Failure("invalid_arguments", "Mode must be run, resume or reconnect.");
        }

        using Activity? ownershipActivity =
            ApplicationTelemetry.Source.StartActivity("coordinator.ownership");
        ownershipActivity?.SetTag("process.pid", Environment.ProcessId);
        using FileStream? ownership = TryOwn(activity);
        ownershipActivity?.SetTag("liteagensh.ownership.acquired", ownership is not null);
        if (ownership is null)
        {
            ownershipActivity?.SetStatus(ActivityStatusCode.Error);
            return Failure("conflict", "Exclusive coordinator ownership is unavailable.");
        }
        ownershipActivity?.AddEvent(new ActivityEvent("coordinator.ownership.acquired"));

        bool settlementOnly = false;
        try
        {
            settlementOnly = recovery.RequiresSettlementOnly(mode);
            if (settlementOnly)
            {
                await recovery.SettleAsync(cancellationToken, recoverNativeUsage: true,
                    settlementOnly: true).ConfigureAwait(false);
                return Classify(recovery.ClosedRecoveryResult());
            }
            await recovery.SettleAsync(cancellationToken, recoverNativeUsage: mode == "reconnect")
                .ConfigureAwait(false);
            qualification.RequireDispatchQualification();
        }
        catch (Exception exception) when (IsExpected(exception)
            || (settlementOnly && exception is OperationCanceledException))
        {
            return settlementOnly
                ? Classify(recovery.ClosedRecoveryResult(exception)) : Failure(exception);
        }

        using CoordinatorSegmentRunner segment = new(
            controls, database, collaboration, git, credits, nativeWorkers, recovery,
            observeWorkerCompletion, mode, cancellationToken
        );
        return Classify(await segment.ExecuteAsync().ConfigureAwait(false));
    }

    public async Task<JsonObject> FinalizeAsync(CancellationToken cancellationToken = default)
    {
        using FileStream? ownership = TryOwn();
        if (ownership is null)
        {
            return RequestControl("finalize");
        }

        string? initialLifecycle = null;
        bool recoverySettled = false;
        try
        {
            initialLifecycle = database.Meta.RawLifecycle;
            if (
                initialLifecycle is not
                    (null or "initialized" or "paused" or "finalized" or "stopped" or "finalizing")
                || HasOpenSegment()
            )
            {
                return Failure("conflict", "Quiescent coordinator lifecycle is required.");
            }

            await recovery.SettleAsync(cancellationToken, deferUnresolvedLifecycle: true)
                .ConfigureAwait(false);
            recoverySettled = true;
            cancellationToken.ThrowIfCancellationRequested();
            (JsonObject result, string state) = await controls
                .FinalizeOwnedAsync(cancellationToken, initialLifecycle ?? "initialized")
                .ConfigureAwait(false);
            return controls.Commit(0, "", state, result, ControlActions.Finalize).Result;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            bool reservationsUnsettled = false;
            string? reservationError = null;
            if (!recoverySettled)
            {
                try
                {
                    reservationsUnsettled = credits
                        .Reservations(database.RunId)
                        .Any(row => row.State is not ("reconciled" or "released"));
                }
                catch (Exception ledgerException) when (IsExpected(ledgerException))
                {
                    reservationsUnsettled = true;
                    reservationError = ledgerException.Message;
                }
            }

            string targetState = reservationsUnsettled
                ? "unresolved"
                : CoordinatorControls.RestoredQuiescentLifecycle(initialLifecycle);
            JsonObject cancelled = reservationsUnsettled
                ? Failure(
                    "ownership_or_usage_unresolved",
                    reservationError
                        ?? "Quiescence could not be established before cancellation."
                )
                : Failure(
                    "cancelled",
                    "The operation was cancelled; inspect retained run status before replacement."
                );
            try
            {
                (JsonObject committedResult, string committedState) = controls.Commit(
                    0,
                    "",
                    targetState,
                    cancelled,
                    ControlActions.None,
                    failureMessage: reservationError
                );
                if (committedState == "unresolved")
                {
                    return committedResult;
                }
            }
            catch (Exception persistenceException) when (IsExpected(persistenceException))
            {
                return Failure(
                    "ownership_or_usage_unresolved",
                    $"Cancellation could not confirm quiescence ({persistenceException.Message})."
                );
            }

            throw;
        }
        catch (Exception exception) when (IsExpected(exception))
        {
            JsonObject failure = Failure(exception);
            try
            {
                return controls
                    .Commit(
                        0,
                        "",
                        "unresolved",
                        failure,
                        ControlActions.None,
                        failureMessage: exception.Message
                    )
                    .Result;
            }
            catch (Exception persistenceException) when (IsExpected(persistenceException))
            {
                return Failure(
                    "ownership_or_usage_unresolved",
                    $"{exception.Message} ({persistenceException.Message})"
                );
            }
        }
    }

    public async Task<JsonObject> CleanupAsync(CancellationToken cancellationToken = default)
    {
        using FileStream? ownership = TryOwn();
        if (ownership is null)
        {
            return Failure("conflict", "Exclusive coordinator ownership is unavailable.");
        }

        if (database.Meta.RawLifecycle is not ("paused" or "finalized" or "stopped"))
        {
            return Failure(
                "ownership_unresolved",
                "Cleanup requires an inspected, quiescent retained run."
            );
        }

        try
        {
            recovery.RequireCleanupSettlement();
            return Classify(await git.CleanupAsync(cancellationToken).ConfigureAwait(false));
        }
        catch (Exception exception) when (IsExpected(exception))
        {
            return Failure(exception);
        }
    }

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
        Classify(Operation.ExpectedFailure(exception));

    private FileStream? TryOwn(Activity? activity = null)
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
        catch (IOException exception)
        {
            activity?.AddEvent(
                new ActivityEvent(
                    "coordinator.ownership.failed",
                    tags: new ActivityTagsCollection
                    {
                        ["error.type"] = exception.GetType().FullName,
                        ["liteagensh.ownership.hresult"] = exception.HResult,
                    }
                )
            );
            return null;
        }
    }

    private bool IsOwned()
    {
        using FileStream? probe = TryOwn();
        return probe is null;
    }

    private JsonNode? ReadDeadlineDrain()
    {
        using SqliteConnection connection = database.Open();
        return Sql.Scalar(
            connection, "SELECT id FROM coordinator_segments ORDER BY id DESC LIMIT 1;"
        ) is long segment ? database.Meta.ReadJson(RunMetadata.DeadlineDrainKey(segment)) : null;
    }

    private bool HasOpenSegment()
    {
        using SqliteConnection connection = database.Open();
        return Sql.Scalar(
                connection,
                "SELECT id FROM coordinator_segments WHERE ended_at IS NULL LIMIT 1;"
            )
            is not null;
    }
}

[Flags]
internal enum ControlActions
{
    None = 0,
    Pause = 1,
    Finalize = 2,
}

internal sealed class CoordinatorControls(
    RunDatabase database,
    IGitWorkspace git,
    Func<JsonObject, JsonObject> classify
)
{
    public static void EnsureSchema(SqliteConnection connection, SqliteTransaction transaction) =>
        Sql.Execute(
            connection,
            """
            CREATE TABLE IF NOT EXISTS coordinator_controls(
                id INTEGER PRIMARY KEY AUTOINCREMENT, action TEXT NOT NULL, state TEXT NOT NULL,
                requested_at TEXT NOT NULL, completed_at TEXT, result TEXT);
            """,
            transaction
        );

    public JsonObject Request(string action)
    {
        if (action is not ("pause" or "finalize"))
        {
            return Operation.Failure(
                "invalid_arguments",
                "Control action must be pause or finalize."
            );
        }

        using SqliteConnection connection = database.Open();
        using SqliteTransactionScope transaction = Sql.BeginTransaction(connection);
        Sql.Execute(
            connection,
            """
            INSERT INTO coordinator_controls(action,state,requested_at)
            SELECT $action,'pending',$time
            WHERE NOT EXISTS (
                SELECT 1 FROM coordinator_controls WHERE action=$action AND state='pending'
            );
            """,
            transaction,
            ("$action", action),
            ("$time", database.Now.ToString("O"))
        );
        long id = (long)Sql.Scalar(
            connection,
            """
            SELECT id FROM coordinator_controls WHERE action=$action AND state='pending'
            ORDER BY id LIMIT 1;
            """,
            transaction,
            ("$action", action)
        )!;
        transaction.Commit();
        return Operation.Success(
            new
            {
                requestId = id,
                action,
                state = "pending",
            },
            "The coordinator has not completed this request yet."
        );
    }

    public string? NextPending()
    {
        using SqliteConnection connection = database.Open();
        return Sql.Scalar(
                connection,
                "SELECT action FROM coordinator_controls WHERE state='pending' ORDER BY id LIMIT 1;"
            ) as string;
    }

    public bool HasPendingPause()
    {
        using SqliteConnection connection = database.Open();
        return Sql.Scalar(
                connection,
                "SELECT 1 FROM coordinator_controls "
                    + "WHERE state='pending' AND action='pause' LIMIT 1;"
            )
            is not null;
    }

    public async Task<(JsonObject Result, string Lifecycle)> FinalizeOwnedAsync(
        CancellationToken cancellationToken,
        string? priorLifecycle = null
    )
    {
        cancellationToken.ThrowIfCancellationRequested();
        priorLifecycle ??= database.Meta.RawLifecycle ?? "stopped";
        database.Meta.SetLifecycle("finalizing");
        JsonObject delivery = classify(
            await git.FinalizeAsync(cancellationToken).ConfigureAwait(false)
        );
        if (delivery["data"] is JsonObject)
        {
            database.Meta.WriteJson(RunMetadata.DeliveryKey, delivery);
            return (delivery, "finalized");
        }

        return (delivery, RestoredQuiescentLifecycle(priorLifecycle));
    }

    public (JsonObject Result, string State) Commit(
        long segment,
        string reason,
        string state,
        JsonObject result,
        ControlActions completedControls,
        string? failureMessage = null
    )
    {
        using SqliteConnection connection = database.Open();
        using SqliteTransactionScope transaction = Sql.BeginTransaction(connection);
        if (state != "unresolved" && RunMetadata.HasUnresolvedState(connection, transaction))
        {
            state = "unresolved";
            result = Operation.Failure(
                "ownership_or_usage_unresolved",
                failureMessage ?? "Quiescence could not be established."
            );
            completedControls = ControlActions.None;
        }

        classify(result);
        string now = database.Now.ToString("O");
        Sql.SetMetadata(connection, transaction, RunMetadata.LifecycleKey, state);
        if (failureMessage is not null)
        {
            Sql.SetMetadata(connection, transaction, RunMetadata.FailureKey, failureMessage);
        }

        if (state is "paused" or "stopped" or "finalized" or "initialized")
        {
            if (completedControls.HasFlag(ControlActions.Pause))
            {
                CompleteControls(connection, transaction, result, "pause", now);
            }

            if (completedControls.HasFlag(ControlActions.Finalize))
            {
                CompleteControls(connection, transaction, result, "finalize", now);
            }
        }

        if (segment != 0)
        {
            Sql.Execute(
                connection,
                """
                UPDATE coordinator_segments SET state=$state,stop_reason=$reason,ended_at=$time
                WHERE id=$id;
                """,
                transaction,
                ("$state", state),
                ("$reason", reason),
                ("$time", now),
                ("$id", segment)
            );
        }

        transaction.Commit();
        return (result, state);
    }

    public static string RestoredQuiescentLifecycle(string? priorLifecycle) =>
        priorLifecycle is null or "initialized" ? "initialized"
        : priorLifecycle is "paused" or "stopped" or "finalized" ? priorLifecycle
        : "stopped";

    private static void CompleteControls(
        SqliteConnection connection,
        SqliteTransaction transaction,
        JsonObject result,
        string action,
        string completedAt
    ) =>
        Sql.Execute(
            connection,
            """
            UPDATE coordinator_controls SET state='completed',completed_at=$time,result=$result
            WHERE state='pending' AND action=$action;
            """,
            transaction,
            ("$time", completedAt),
            ("$result", result.ToJsonString()),
            ("$action", action)
        );
}
