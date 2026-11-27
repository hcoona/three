using System.Globalization;
using System.Text.Json.Nodes;
using Microsoft.Data.Sqlite;

namespace LiteAgensh;

internal sealed class RunMetadata(RunDatabase database)
{
    public const string LifecycleKey = "lifecycle";
    public const string StopReasonKey = "lifecycle:stop_reason";
    public const string FailureKey = "lifecycle:failure";
    public const string SettlementFailuresKey = "lifecycle:settlement_failures";
    public const string OwnershipAmbiguityKey = "lifecycle:ownership_ambiguity";
    public const string CompletionAmbiguityKey = "lifecycle:completion_ambiguity";
    public const string RootClosedByKey = "lifecycle:root_closed_by";
    public const string DeliveryKey = "lifecycle:delivery";
    public const string CreditWarningKey = "lifecycle:credit_warning";
    public const string DeadlineKey = "lifecycle:deadline";

    public const string GitBaseKey = "git:base";
    public const string GitBaseCommitKey = "git:base_commit";
    public const string GitIntegrationBranchKey = "git:integration_branch";
    public const string GitIntegrationPathKey = "git:integration_path";
    public const string GitDeliveryBranchKey = "git:delivery_branch";
    public const string GitInitializedKey = "git:initialized";
    public const string GitCleaningKey = "git:cleaning";
    public const string GitCleanedKey = "git:cleaned";
    public const string GitFinalizedKey = "git:finalized";
    public const string GitDeliveryReportKey = "git:delivery_report";
    public const string GitPlanIntegratedKey = "git:plan_integrated";
    public const string GitHealthTaskKey = "git:health_task";

    public const string LedgerBindingKey = "native:ledger_binding";
    public const string SdkPreflightReceiptKey = "native:sdk_preflight_receipt";
    public const string ControlledPreflightReceiptKey = "native:controlled_preflight_receipt";
    public const string SdkBillingReceiptKey = "native:sdk_billing_receipt";
    public const string ControlledBillingReceiptKey = "native:controlled_billing_receipt";

    public static string WorktreeKey(string member) => $"worktree:{member}";
    public static string WorkerLifecycleKey(string worker) => $"worker:{worker}:lifecycle";
    public static string WorkerSessionKey(string worker) => $"worker:{worker}:session";
    public static string WorkerReservationKey(string worker) => $"worker:{worker}:reservation";
    public static string WorkerRecoveryKey(string worker) => $"worker:{worker}:recovery";
    public static string BootstrapKey(string sessionId) => $"native:bootstrap:{sessionId}";
    public static string SessionDiagnosticKey(string sessionId) => $"native:session:{sessionId}";
    public static string PreflightDiagnosticKey(string sessionId) =>
        $"native:preflight:{sessionId}";
    public static string RuntimeKey(string runtimeId) => $"native:runtime:{runtimeId}";
    public static string RuntimeSessionKey(string runtimeId, string sessionId) =>
        $"native:runtime:{runtimeId}:session:{sessionId}";
    public static string DispatchContractKey(string reservationId) =>
        $"native:dispatch_contract:{reservationId}";
    public static string DeadlineDrainKey(long segment) => $"lifecycle:deadline_drain:{segment}";
    public static string FailureDetailsKey(long segment) => $"lifecycle:failure_details:{segment}";

    public string Lifecycle => database.ReadMetadata(LifecycleKey) ?? "initialized";

    public string? RawLifecycle => database.ReadMetadata(LifecycleKey);

    public string? StopReason
    {
        get => database.ReadMetadata(StopReasonKey);
        set => WriteRequired(StopReasonKey, value);
    }

    public string? Failure
    {
        get => database.ReadMetadata(FailureKey);
        set => WriteRequired(FailureKey, value);
    }

    public string? OwnershipAmbiguity => database.ReadMetadata(OwnershipAmbiguityKey);

    public string? CompletionAmbiguity => database.ReadMetadata(CompletionAmbiguityKey);

    public string? RootClosedBy => database.ReadMetadata(RootClosedByKey);

    public string? LedgerBinding => database.ReadMetadata(LedgerBindingKey);

    public bool IsCreditWarningSent => database.ReadMetadata(CreditWarningKey) == "sent";

    public bool IsPlanIntegrated => database.ReadMetadata(GitPlanIntegratedKey) == "true";

    public bool IsGitFinalized => database.ReadMetadata(GitFinalizedKey) == "true";

    public void SetLifecycle(string state) => database.WriteMetadata(LifecycleKey, state);

    public void RecordOwnershipAmbiguity(string message) =>
        database.WriteMetadata(OwnershipAmbiguityKey, message);

    public void RecordCompletionAmbiguity(long batchId) =>
        database.WriteMetadata(
            CompletionAmbiguityKey,
            batchId.ToString(CultureInfo.InvariantCulture)
        );

    public void MarkCreditWarningSent() => database.WriteMetadata(CreditWarningKey, "sent");

    public bool HasUnresolvedState()
    {
        using SqliteConnection connection = database.Open();
        return HasUnresolvedState(connection);
    }

    public static bool HasUnresolvedState(
        SqliteConnection connection,
        SqliteTransaction? transaction = null
    )
    {
        if (
            Convert.ToInt64(
                Sql.Scalar(
                    connection,
                    """
                    SELECT EXISTS(
                        SELECT 1 FROM meta
                        WHERE (key='lifecycle' AND value='unresolved')
                            OR (key IN (
                                'lifecycle:ownership_ambiguity',
                                'lifecycle:completion_ambiguity'
                            ) AND length(value)>0)
                    );
                    """,
                    transaction
                ),
                CultureInfo.InvariantCulture
            ) != 0
        )
        {
            return true;
        }

        return ProcessRunner.HasUnsettledLeases(
            connection,
            transaction,
            includeLiveHelpers: false
        );
    }

    public bool IsBootstrapConfirmed(string? sessionId) =>
        sessionId is not null && database.ReadMetadata(BootstrapKey(sessionId)) == "true";

    public void ConfirmBootstrap(string? sessionId)
    {
        if (sessionId is not null)
        {
            database.WriteMetadata(BootstrapKey(sessionId), "true");
        }
    }

    public void SetWorkerLifecycle(string worker, string state) =>
        database.WriteMetadata(WorkerLifecycleKey(worker), state);

    public JsonArray ReadWorkerStatuses()
    {
        Dictionary<string, string> values = new(StringComparer.Ordinal);
        using (SqliteConnection connection = database.Open())
        using (
            SqliteCommand command = Sql.Command(
                connection,
                "SELECT key,value FROM meta WHERE key LIKE 'worker:%';"
            )
        )
        using (ObservedSqliteReader reader = Sql.Reader(command))
        {
            while (reader.Read())
            {
                values[reader.GetString(0)] = reader.GetString(1);
            }
        }

        JsonArray workers = [];
        foreach (string worker in database.Configuration.WorkerIds)
        {
            workers.Add(
                new JsonObject
                {
                    ["id"] = worker,
                    ["lifecycle"] =
                        values.GetValueOrDefault(WorkerLifecycleKey(worker)) ?? "not-started",
                    ["sessionId"] = values.GetValueOrDefault(WorkerSessionKey(worker)),
                    ["recovery"] = values.GetValueOrDefault(WorkerRecoveryKey(worker)),
                }
            );
        }

        return workers;
    }

    public (string? SessionId, string? ReservationId) ReadWorkerAssociation(string worker)
    {
        using SqliteConnection connection = database.Open();
        using SqliteTransactionScope transaction = Sql.BeginTransaction(connection, deferred: true);
        string? session = Sql.GetMetadata(connection, transaction, WorkerSessionKey(worker));
        string? reservation = Sql.GetMetadata(
            connection,
            transaction,
            WorkerReservationKey(worker)
        );
        transaction.Commit();
        return (session, reservation);
    }

    public static void WriteWorkerAssociation(
        SqliteConnection connection,
        SqliteTransaction transaction,
        string worker,
        string sessionId,
        string reservationId,
        string recovery
    ) =>
        Sql.Execute(
            connection,
            """
            INSERT INTO meta(key,value)
            VALUES($sessionKey,$session),($reservationKey,$reservation),($recoveryKey,$recovery)
            ON CONFLICT(key) DO UPDATE SET value=excluded.value;
            """,
            transaction,
            ("$sessionKey", WorkerSessionKey(worker)),
            ("$session", sessionId),
            ("$reservationKey", WorkerReservationKey(worker)),
            ("$reservation", reservationId),
            ("$recoveryKey", WorkerRecoveryKey(worker)),
            ("$recovery", recovery)
        );

    public bool IsGitActive()
    {
        using SqliteConnection connection = database.Open();
        return Convert.ToInt64(
                Sql.Scalar(
                    connection,
                    """
                    SELECT (SELECT value FROM meta WHERE key='git:initialized')='true'
                        AND COALESCE((SELECT value FROM meta WHERE key='git:cleaned'),'')<>'true'
                        AND COALESCE((SELECT value FROM meta WHERE key='git:cleaning'),'')<>'true';
                    """
                ),
                CultureInfo.InvariantCulture
            ) != 0;
    }

    public static bool IsGitWritable(SqliteConnection connection, SqliteTransaction transaction) =>
        Convert.ToInt64(
            Sql.Scalar(
                connection,
                """
                SELECT NOT EXISTS(
                    SELECT 1 FROM meta
                    WHERE key IN ('git:cleaned','git:cleaning') AND value='true'
                );
                """,
                transaction
            ),
            CultureInfo.InvariantCulture
        ) != 0;

    public JsonNode? ReadJson(string key) =>
        database.ReadMetadata(key) is { } value ? JsonNode.Parse(value) : null;

    public void WriteJson(string key, JsonNode value) =>
        database.WriteMetadata(key, value.ToJsonString());

    public void WriteRecoveryFailureDetails(JsonArray details) =>
        WriteJson($"native:recovery_failure_details:{Guid.NewGuid():N}", details);

    private void WriteRequired(string key, string? value)
    {
        ArgumentNullException.ThrowIfNull(value);
        database.WriteMetadata(key, value);
    }
}
