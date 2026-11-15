using System.Globalization;
using System.Runtime.ExceptionServices;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Data.Sqlite;

namespace LiteAgensh;

internal interface INativeWorker : IAsyncDisposable
{
    string? SessionId { get; }

    string ReservationId { get; }

    JsonObject? Diagnostic { get; }

    string? NativeFailure { get; }

    Exception? FirstFailure => null;

    void BindOwner(CancellationToken cancellationToken) { }

    NativeRequest BeginRequest(CancellationToken cancellationToken) => new(cancellationToken);

    Task StartAsync(
        string cwd,
        string? exactSession,
        CancellationToken cancellationToken,
        string? recoveryLabel = null,
        WorkAdmission? admission = null
    );

    Task<JsonObject> PromptAsync(
        string text, CancellationToken cancellationToken, WorkAdmission? admission = null
    );

    Task VerifyCreditsAsync(CancellationToken cancellationToken);

    Task StopAsync();
}

internal sealed class NativeRequest(
    CancellationToken token,
    CancellationTokenSource? lifetime = null,
    IDisposable? observation = null
) : IDisposable
{
    public CancellationToken Token { get; } = token;

    public void Dispose()
    {
        try
        {
            observation?.Dispose();
        }
        finally
        {
            lifetime?.Dispose();
        }
    }
}

internal interface INativeWorkerFactory
{
    JsonArray SettlementFailures { get; }

    JsonArray SettlementDetails { get; }

    INativeWorker Create(CreditReservation? reservation, bool calibration);

    Task<JsonObject> PreflightAsync(
        IReadOnlyList<(string Worker, string Cwd)> peers,
        CancellationToken cancellationToken
    );

    Task CompleteAsync();

    Task<RecoveredNativeUsage> ReadSettledUsageAsync(
        CreditReservation reservation,
        string cwd,
        string evidencePrefix,
        CancellationToken cancellationToken
    );
}

internal sealed class NativeWorkerFactory(
    RunDatabase database,
    ProcessRunner processes,
    CreditLedger credits,
    Func<INativeSdkHost>? createHost = null
) : INativeWorkerFactory
{
    private static readonly string[] RecoveryMetadataKeys =
        ["selectedModel", "sdkVersion"];
    private static readonly string[] OriginalRecoveryMetadataKeys =
        ["sessionId", "workingDirectory", "selectedModel", "sdkVersion",
            "runtimeVersion", "runtimeId", "runtimeLeaseId", "paidReservation",
            "alreadyInUse", "isRemote"];
    private readonly object lifecycle = new();
    private readonly List<(NativeWorker Worker, CreditReservation? Reservation)> workers = [];
    private INativeSdkHost? host;
    private bool closing;

    public JsonArray SettlementFailures { get; } = [];

    public JsonArray SettlementDetails { get; } = [];

    public INativeWorker Create(CreditReservation? reservation, bool calibration)
    {
        lock (lifecycle)
        {
            NativeWorker worker = new(
                database, GetHost(), credits, reservation, calibration,
                createHost is null ? "native-sdk" : "controlled-native-dependency"
            );
            workers.Add((worker, reservation));
            return worker;
        }
    }

    public async Task<JsonObject> PreflightAsync(
        IReadOnlyList<(string Worker, string Cwd)> peers,
        CancellationToken cancellationToken
    )
    {
        INativeSdkHost backend = GetHost();
        List<INativeSdkSession> sessions = [];
        JsonArray diagnostics = [];
        JsonObject? result = null;
        Exception? failure = null;
        try
        {
            foreach ((string worker, string cwd) in peers)
            {
                INativeSdkSession session = backend.CreateSession(cwd, isolated: false);
                sessions.Add(session);
                string id = Guid.NewGuid().ToString("D");
                await session.OpenAsync(
                    worker, id, 30, null,
                    Path.Combine(database.StatePath, "native", $"preflight-{id}.sdk-evidence"),
                    cancellationToken
                ).ConfigureAwait(false);
                RequireUnprompted(await session.ReadAsync(id, 30, cancellationToken)
                    .ConfigureAwait(false));
            }

            INativeSdkSession first = sessions[0];
            JsonObject? firstDiagnostic = null;
            foreach (decimal limit in new decimal[] { 60, 30 })
            {
                firstDiagnostic = PreflightDiagnostic(first, await first.UpdateLimitAsync(
                    first.SessionId, limit, cancellationToken
                ).ConfigureAwait(false));
                foreach (INativeSdkSession other in sessions.Skip(1))
                {
                    RequireUnprompted(await other.ReadAsync(
                        other.SessionId, 30, cancellationToken
                    ).ConfigureAwait(false));
                }
            }
            diagnostics.Add(firstDiagnostic);
            first.Quiesce();
            await first.DisposeAsync().ConfigureAwait(false);
            foreach (INativeSdkSession other in sessions.Skip(1))
            {
                diagnostics.Add(PreflightDiagnostic(other, await other.ReadAsync(
                    other.SessionId, 30, cancellationToken
                ).ConfigureAwait(false)));
            }

            result = new()
            {
                ["sessionId"] = first.SessionId,
                ["modelPromptsSent"] = 0,
                ["diagnostics"] = firstDiagnostic!.DeepClone(),
                ["sessions"] = diagnostics,
                ["runtimeId"] = backend.Id,
                ["runtimeLeaseId"] = backend.LeaseId,
            };
        }
        catch (Exception exception)
        {
            failure = exception;
        }
        finally
        {
            foreach (INativeSdkSession session in sessions)
            {
                try
                {
                    session.Quiesce();
                    await session.DisposeAsync().ConfigureAwait(false);
                }
                catch (Exception exception)
                {
                    failure ??= new OperationException(
                        "ownership_unresolved",
                        $"Native preflight session close failed: {exception.Message}"
                    );
                }
            }
        }
        if (failure is not null)
        {
            ExceptionDispatchInfo.Capture(failure).Throw();
        }
        return result!;
    }

    public async Task CompleteAsync()
    {
        (NativeWorker Worker, CreditReservation? Reservation)[] cohort;
        INativeSdkHost? backend;
        lock (lifecycle)
        {
            closing = true;
            cohort = workers.ToArray();
            backend = host;
        }
        SettlementFailures.Clear();
        SettlementDetails.Clear();
        HashSet<NativeWorker> unsettled = [];
        Exception? failure = null;
        foreach ((NativeWorker worker, CreditReservation? reservation) in cohort)
        {
            try
            {
                await worker.StopAsync().ConfigureAwait(false);
            }
            catch (Exception exception)
            {
                failure ??= exception;
                unsettled.Add(worker);
                Json.AddSettlementDetails(
                    SettlementDetails, reservation?.Worker, reservation?.Id, "stop", exception
                );
                SettlementFailures.Add(CreateSettlementFailure(reservation, "stop", exception));
            }
        }

        bool runtimeSettled = false;
        try
        {
            if (backend is not null)
            {
                await backend.DisposeAsync().ConfigureAwait(false);
            }
            runtimeSettled = true;
        }
        catch (Exception exception)
        {
            failure ??= exception;
            Json.AddSettlementDetails(
                SettlementDetails, null, null, "native-runtime", exception
            );
            SettlementFailures.Add(CreateSettlementFailure(null, "native-runtime", exception));
        }

        foreach ((NativeWorker worker, CreditReservation? reservation) in cohort)
        {
            bool finalize = runtimeSettled && !unsettled.Contains(worker);
            try
            {
                if (finalize)
                {
                    worker.CompleteUsage();
                }
                else
                {
                    worker.RetainUncertainty();
                }
            }
            catch (Exception exception)
            {
                failure ??= exception;
                Json.AddSettlementDetails(
                    SettlementDetails, reservation?.Worker, reservation?.Id,
                    finalize ? "final-usage" : "retain-uncertainty", exception
                );
                SettlementFailures.Add(CreateSettlementFailure(
                    reservation, finalize ? "final-usage" : "retain-uncertainty", exception
                ));
            }
        }
        if (failure is not null)
        {
            throw new OperationException(
                "credit_or_ownership_unknown",
                $"Native cohort settlement retained unresolved evidence: {failure.Message}"
            );
        }
        lock (lifecycle)
        {
            workers.Clear();
            host = null;
            closing = false;
        }
    }

    internal static JsonObject CreateSettlementFailure(
        CreditReservation? reservation, string phase, Exception exception
    )
    {
        JsonObject failure = new()
        {
            ["phase"] = phase,
            ["message"] = exception.Message,
        };
        if (reservation is not null)
        {
            failure["worker"] = reservation.Worker;
            failure["reservationId"] = reservation.Id;
        }
        return failure;
    }

    private INativeSdkHost GetHost()
    {
        lock (lifecycle)
        {
            if (closing)
            {
                throw new OperationException(
                    "ownership_unresolved",
                    "The operation's native runtime is closing or unresolved."
                );
            }
            return host ??= createHost?.Invoke() ?? new NativeSdkHost(database, processes);
        }
    }

    private static JsonObject PreflightDiagnostic(INativeSdkSession session, NativeUsage usage)
    {
        RequireUnprompted(usage);
        JsonObject diagnostic = session.Metadata!.DeepClone().AsObject();
        diagnostic["reportedNanoAiu"] = usage.TotalNanoAiu?.ToString(
            "R", CultureInfo.InvariantCulture
        );
        diagnostic["aiCreditsStatus"] = usage.Status;
        diagnostic["totalUserRequests"] = usage.TotalUserRequests;
        return diagnostic;
    }

    private static void RequireUnprompted(NativeUsage usage)
    {
        if (usage.TotalUserRequests != 0 || (usage.TotalNanoAiu is double total && total != 0))
        {
            throw new OperationException(
                "credit_unknown", "A non-model session preflight unexpectedly created native usage."
            );
        }
    }

    public async Task<RecoveredNativeUsage> ReadSettledUsageAsync(
        CreditReservation reservation,
        string cwd,
        string evidencePrefix,
        CancellationToken cancellationToken
    )
    {
        if (reservation.Allowance - (reservation.Actual ?? 0) <= 0)
        {
            throw new OperationException(
                "credit_unknown", "Exact recovery has no remaining backed capacity."
            );
        }
        (JsonObject retained, NativeRecoveryOrigin origin) =
            RecoveryLimitEvidence(reservation, cwd);
        (decimal? BookedActual, NativeUsage Reported)? baseline =
            credits.ReadRecoveryBaseline(reservation.Id);
        RequireRecoveryEvidence(baseline is not null
            && baseline.Value.Reported.SessionId == reservation.SessionId);
        decimal cap = retained["appliedLimit"]!.GetValue<decimal>();
        using CancellationTokenSource deadline =
            CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        deadline.CancelAfter(TimeSpan.FromSeconds(30));
        INativeSdkHost recoveryHost =
            createHost?.Invoke() ?? new NativeSdkHost(database, processes);
        INativeSdkSession? recovery = null;
        NativeUsage? usage = null;
        List<Exception> failures = [];
        try
        {
            recovery = recoveryHost.CreateSession(cwd, isolated: true);
            OriginalNativeShutdown original = await recovery.ReadOriginalShutdownAsync(
                origin, evidencePrefix, deadline.Token).ConfigureAwait(false);
            await recovery
                .OpenAsync(
                    reservation.Worker,
                    reservation.SessionId!,
                    cap,
                    reservation.SessionId,
                    evidencePrefix,
                    deadline.Token
                )
                .ConfigureAwait(false);
            usage = await recovery
                .ReadSettledAsync(
                    reservation.SessionId!, cap, deadline.Token, verifyLocalWork: true)
                .ConfigureAwait(false);
            if (usage.RequireComplete() < (baseline!.Value.BookedActual ?? 0)
                || usage.TotalNanoAiu < baseline.Value.Reported.TotalNanoAiu
                || usage.TotalUserRequests < baseline.Value.Reported.TotalUserRequests
                || usage.TotalNanoAiu < original.TotalNanoAiu)
            {
                throw new OperationException("credit_unknown",
                    "Fresh recovery usage regressed or does not cover original native termination."
                );
            }
            RequireRecoveryEvidence(
                recovery.Metadata is JsonObject observed
                && observed["sessionId"]?.GetValue<string>() == reservation.SessionId
                && SameRecoveryPath(observed["workingDirectory"]?.GetValue<string>(), cwd)
                && observed["appliedLimit"]?.GetValue<double>() == NativeSdkSession.NativeLimit(cap)
                && observed["alreadyInUse"]?.GetValue<bool>() == false
                && observed["isRemote"]?.GetValue<bool>() == false
                && observed["runtimeVersion"]?.GetValue<string>() == NativeSdkSession.RuntimeVersion
                && RecoveryMetadataKeys
                    .All(key => JsonNode.DeepEquals(observed[key], retained[key]))
            );
            recovery.RequireHealthy();
        }
        catch (Exception exception)
        {
            failures.Add(exception);
        }
        finally
        {
            try
            {
                if (recovery is not null)
                {
                    recovery.Quiesce();
                    await recovery.DisposeAsync().ConfigureAwait(false);
                }
            }
            catch (Exception exception)
            {
                failures.Add(exception);
            }
            try
            {
                await recoveryHost.DisposeAsync().ConfigureAwait(false);
            }
            catch (Exception exception)
            {
                failures.Add(exception);
            }
        }
        if (failures.Count != 0)
        {
            throw new OperationException(
                "credit_or_ownership_unknown",
                "Exact native recovery retained unresolved evidence.",
                new AggregateException(failures)
            );
        }

        recovery!.RequireHealthy();
        recovery.WithSettledUsage(usage!, () => { });
        return new(reservation, usage!);
    }

    private (JsonObject Metadata, NativeRecoveryOrigin Origin) RecoveryLimitEvidence(
        CreditReservation reservation, string cwd)
    {
        try
        {
            using SqliteConnection connection = database.Open();
            using SqliteTransactionScope snapshot =
                Sql.BeginTransaction(connection, deferred: true);
            RequireRecoveryEvidence(reservation.RunId == database.RunId
                && reservation.SessionId is not null
                && (long)Sql.Scalar(connection,
                    """
                    SELECT
                      (SELECT COUNT(*) FROM native_sessions
                       WHERE reservation_id=$id OR session_id=$session)=1
                      AND EXISTS(SELECT 1 FROM native_sessions
                        WHERE reservation_id=$id AND session_id=$session AND worker=$worker
                        AND recovery='fresh-session-with-durable-summary'
                        AND initialize_json=$sdk)
                      AND (SELECT COUNT(*) FROM native_attempts
                        WHERE reservation_id=$id OR session_id=$session OR usage_path=$path)=1
                      AND EXISTS(SELECT 1 FROM native_attempts
                        WHERE reservation_id=$id AND session_id=$session AND worker=$worker
                        AND usage_path=$path AND baseline_usage='0' AND initialize_json=$sdk);
                    """, snapshot, ("$id", reservation.Id), ("$session", reservation.SessionId),
                    ("$worker", reservation.Worker), ("$path", reservation.UsagePath),
                    ("$sdk", "sdk:" + NativeSdkSession.SdkVersion))! == 1);

            JsonObject Read(string key) =>
                JsonNode.Parse(Sql.Scalar(connection, "SELECT value FROM meta WHERE key=$key;",
                    snapshot, ("$key", key)) as string
                    ?? throw new OperationException("credit_unknown",
                        "Required original native limit evidence is missing."))?.AsObject()
                ?? throw new OperationException("credit_unknown",
                    "Required original native limit evidence is not structured.");

            JsonObject start = Read(NoDispatchCancellation.Marker(reservation.Id));
            JsonObject initial = start["initial"] as JsonObject
                ?? throw new OperationException("credit_unknown",
                    "Original native start observation is missing.");
            JsonObject retained = Read($"native:session:{reservation.SessionId}");
            RequireRecoveryEvidence(
                start["contract"]?.GetValue<string>() == NoDispatchCancellation.Contract
                && start["evidenceKind"]?.GetValue<string>() ==
                    (createHost is null ? "native-sdk" : "controlled-native-dependency")
                && Guid.TryParse(start["applicationBuild"]?.GetValue<string>(), out Guid build)
                && build != Guid.Empty
                && start["runId"]?.GetValue<string>() == database.RunId
                && start["reservationId"]?.GetValue<string>() == reservation.Id
                && start["sessionId"]?.GetValue<string>() == reservation.SessionId
                && SameRecoveryPath(start["workingDirectory"]?.GetValue<string>(), cwd)
                && NoDispatchCancellation.IsEmpty(initial)
                && initial["appliedLimit"]?.GetValue<decimal>() is > 0
                && initial["appliedLimit"]!.GetValue<decimal>() <= reservation.Allowance
                && retained["appliedLimit"]?.GetValue<decimal>() == reservation.Allowance
                && retained["sessionId"]?.GetValue<string>() == reservation.SessionId
                && SameRecoveryPath(retained["workingDirectory"]?.GetValue<string>(), cwd)
                && retained["paidReservation"]?.GetValue<bool>() == true
                && retained["alreadyInUse"]?.GetValue<bool>() == false
                && retained["isRemote"]?.GetValue<bool>() == false
                && retained["sdkVersion"]?.GetValue<string>() == NativeSdkSession.SdkVersion
                && CompatibleRecoveryRuntime(retained["runtimeVersion"]?.GetValue<string>())
                && !string.IsNullOrWhiteSpace(retained["selectedModel"]?.GetValue<string>())
                && (database.Configuration.NativeModel is null
                    || string.Equals(database.Configuration.NativeModel,
                        retained["selectedModel"]?.GetValue<string>(),
                        StringComparison.OrdinalIgnoreCase))
                && !string.IsNullOrWhiteSpace(retained["runtimeId"]?.GetValue<string>())
                && retained["runtimeLeaseId"]?.GetValue<long>() is > 0
                && OriginalRecoveryMetadataKeys
                    .All(key => JsonNode.DeepEquals(initial[key], retained[key]))
            );
            string runtimeId = retained["runtimeId"]!.GetValue<string>();
            JsonObject runtime = Read($"native:runtime:{runtimeId}");
            JsonObject association =
                Read($"native:runtime:{runtimeId}:session:{reservation.SessionId}");
            RequireRecoveryEvidence(
                runtime["runtimeId"]?.GetValue<string>() == runtimeId
                && JsonNode.DeepEquals(runtime["runtimeLeaseId"], retained["runtimeLeaseId"])
                && JsonNode.DeepEquals(runtime["runtimeVersion"], retained["runtimeVersion"])
                && association["runtimeId"]?.GetValue<string>() == runtimeId
                && JsonNode.DeepEquals(association["runtimeLeaseId"], retained["runtimeLeaseId"])
                && association["sessionId"]?.GetValue<string>() == reservation.SessionId
                && association["worker"]?.GetValue<string>() == reservation.Worker
                && SameRecoveryPath(association["workingDirectory"]?.GetValue<string>(), cwd)
                && (long)Sql.Scalar(connection,
                    "SELECT COUNT(*) FROM process_leases WHERE id=$lease AND kind=$kind "
                        + "AND state='exited' AND pid>0 AND start_ticks>0;",
                    snapshot, ("$lease", retained["runtimeLeaseId"]!.GetValue<long>()),
                    ("$kind", $"native:runtime:{runtimeId}"))! == 1
            );
            using SqliteCommand lifetime = Sql.Command(connection,
                """
                SELECT n.created_at,a.created_at,p.created_at,s.started_at,s.ended_at,s.id
                FROM native_sessions n JOIN native_attempts a
                  ON a.reservation_id=n.reservation_id AND a.session_id=n.session_id
                JOIN process_leases p ON p.id=$lease
                CROSS JOIN coordinator_segments s
                WHERE n.reservation_id=$id AND s.ended_at IS NOT NULL;
                """, snapshot, ("$id", reservation.Id),
                ("$lease", retained["runtimeLeaseId"]!.GetValue<long>()));
            using ObservedSqliteReader reader = Sql.Reader(lifetime);
            NativeRecoveryOrigin? origin = null;
            while (reader.Read())
            {
                DateTimeOffset associationTime = DateTimeOffset.Parse(
                    reader.GetString(0), CultureInfo.InvariantCulture);
                DateTimeOffset attemptTime = DateTimeOffset.Parse(
                    reader.GetString(1), CultureInfo.InvariantCulture);
                DateTimeOffset runtimeTime = DateTimeOffset.Parse(
                    reader.GetString(2), CultureInfo.InvariantCulture);
                DateTimeOffset segmentStart = DateTimeOffset.Parse(
                    reader.GetString(3), CultureInfo.InvariantCulture);
                DateTimeOffset segmentEnd = DateTimeOffset.Parse(
                    reader.GetString(4), CultureInfo.InvariantCulture);
                if (segmentStart <= associationTime && associationTime == attemptTime
                    && associationTime <= segmentEnd
                    && segmentStart <= runtimeTime && runtimeTime <= segmentEnd)
                {
                    RequireRecoveryEvidence(origin is null);
                    origin = new(reservation.SessionId!,
                        retained["runtimeVersion"]!.GetValue<string>(), runtimeId,
                        retained["runtimeLeaseId"]!.GetValue<long>(),
                        reader.GetInt64(5), associationTime,
                        runtimeTime, segmentStart, segmentEnd);
                }
            }
            RequireRecoveryEvidence(origin is not null);
            return (retained, origin!);
        }
        catch (Exception exception) when (exception is JsonException or InvalidOperationException
            or ArgumentException or FormatException or OverflowException)
        {
            throw new OperationException("credit_unknown",
                "Original native limit evidence is malformed or incompatible.", exception);
        }
    }

    private static bool CompatibleRecoveryRuntime(string? original) =>
        original == NativeSdkSession.RuntimeVersion
        || (original == "1.0.105" && NativeSdkSession.RuntimeVersion == "1.0.107");

    private static bool SameRecoveryPath(string? path, string cwd) =>
        path is not null && Path.TrimEndingDirectorySeparator(Path.GetFullPath(path)).Equals(
            Path.TrimEndingDirectorySeparator(Path.GetFullPath(cwd)),
            StringComparison.OrdinalIgnoreCase);

    private static void RequireRecoveryEvidence(bool condition)
    {
        if (!condition)
        {
            throw new OperationException("credit_unknown",
                "Exact recovery requires compatible original zero-origin and verified cap evidence."
            );
        }
    }
}
