using System.ComponentModel;
using System.Globalization;
using System.Text.Json.Nodes;
using Microsoft.Data.Sqlite;

namespace LiteAgensh;

internal sealed class NativeWorker : INativeWorker
{
    private readonly RunDatabase database;
    private readonly NativeSdkHost host;
    private readonly CreditLedger credits;
    private CreditReservation? reservation;
    private readonly bool calibration;
    private readonly SemaphoreSlim turn = new(1, 1);
    private readonly SemaphoreSlim settlement = new(1, 1);
    private readonly CancellationTokenSource stop = new();
    private NativeSdkSession? session;
    private bool started;
    private bool stopped;
    private bool verified;
    private decimal limit;
    private decimal windowBaseline;
    private bool nativeIdle;
    private string? evidencePath;
    private (NativeUsage Usage, long Dispatches)? undispatchedBaseline;
    private NativeUsage? pendingFinalUsage;
    private Exception? settlementFailure;
    private bool usageFinalized;

    public NativeWorker(
        RunDatabase database,
        NativeSdkHost host,
        CreditLedger credits,
        CreditReservation? reservation = null,
        bool calibration = false
    )
    {
        this.database = database;
        this.host = host;
        this.credits = credits;
        this.reservation = reservation;
        this.calibration = calibration;
    }

    public string? SessionId { get; private set; }

    public string ReservationId =>
        reservation?.Id ?? throw new InvalidOperationException("Non-paid diagnostic session.");

    public JsonObject? Diagnostic { get; private set; }

    public string? NativeFailure => session?.NativeFailure;

    public async Task StartAsync(
        string cwd,
        string? exactSession,
        CancellationToken cancellationToken,
        string? recoveryLabel = null
    )
    {
        cancellationToken.ThrowIfCancellationRequested();
        using CancellationTokenSource linked = CancellationTokenSource.CreateLinkedTokenSource(
            cancellationToken, stop.Token
        );
        await turn.WaitAsync(linked.Token).ConfigureAwait(false);
        try
        {
            await StartCoreAsync(cwd, exactSession, recoveryLabel, linked.Token)
                .ConfigureAwait(false);
        }
        finally
        {
            turn.Release();
        }
    }

    private async Task StartCoreAsync(
        string cwd,
        string? exactSession,
        string? recoveryLabel,
        CancellationToken cancellationToken
    )
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (exactSession is not null && exactSession != reservation?.SessionId)
        {
            throw new OperationException("credit_unknown", "Exact session association mismatched.");
        }

        string id = reservation?.SessionId ?? Guid.NewGuid().ToString("D");
        if (reservation is { Shared: true })
        {
            reservation = await credits
                .AcquireSharedWindowAsync(reservation.Id, cancellationToken)
                .ConfigureAwait(false);
        }
        limit =
            reservation is null ? 30
            : exactSession is null ? reservation.Allowance
            : reservation.Allowance - (reservation.Actual ?? 0);
        if (limit <= 0)
        {
            throw new OperationException("credit_exhausted", "No native window allowance remains.");
        }

        evidencePath =
            reservation?.UsagePath
            ?? Path.Combine(database.StatePath, "native", $"preflight-{id}.sdk-evidence");
        string worker = reservation?.Worker ?? "worker1";
        string recovery =
            recoveryLabel
            ?? (exactSession is null ? "fresh-session-with-durable-summary" : "exact-session-load");
        if (reservation is not null)
        {
            credits.MarkStarted(reservation.Id);
            started = true;
            RecordAssociation(id, worker, recovery);
        }
        else
        {
            database.WriteMetadata(
                $"native:preflight:{id}",
                new JsonObject
                {
                    ["sessionId"] = id,
                    ["evidencePrefix"] = evidencePath,
                    ["state"] = "opening",
                    ["paidReservation"] = false,
                }.ToJsonString()
            );
        }

        session = new(database, host, cwd, calibration);
        (NativeUsage Usage, long Dispatches)? priorUsage = null;
        try
        {
            if (exactSession is not null && reservation is not null)
            {
                priorUsage = credits.ReadCompleteSdkUsage(reservation.Id);
            }

            await session
                .OpenAsync(worker, id, limit, exactSession, evidencePath, cancellationToken)
                .ConfigureAwait(false);
            SessionId = session.SessionId;
            NativeUsage usage = await session
                .ReadAsync(id, limit, cancellationToken)
                .ConfigureAwait(false);
            if (reservation is null && database.Configuration.CreditAllocation == "shared")
            {
                foreach (decimal probeLimit in new decimal[] { 60, 30 })
                {
                    usage = await session
                        .UpdateLimitAsync(id, probeLimit, cancellationToken)
                        .ConfigureAwait(false);
                    if (
                        usage.TotalUserRequests != 0
                        || (usage.TotalNanoAiu is double total && total != 0)
                    )
                    {
                        throw new OperationException(
                            "credit_unknown",
                            "A non-model limit preflight unexpectedly created native usage."
                        );
                    }
                }
            }
            SaveDiagnostic(usage);
            if (reservation is not null)
            {
                if (exactSession is not null)
                {
                    decimal actual = usage.RequireComplete();
                    if (actual < (reservation.Actual ?? 0))
                    {
                        throw new OperationException(
                            "credit_unknown",
                            "Exact resume lost previously observed cumulative native usage."
                        );
                    }

                    if (actual + limit > reservation.Allowance)
                    {
                        throw new OperationException(
                            "credit_unknown",
                            "The resumed cumulative baseline leaves less than the applied window."
                        );
                    }
                }
                else if (
                    (usage.TotalNanoAiu is double initial && initial != 0)
                    || usage.TotalUserRequests != 0
                )
                {
                    throw new OperationException(
                        "credit_unknown",
                        "A fresh session unexpectedly reports prior usage."
                    );
                }

                credits.AttachSession(reservation.Id, id);
                credits.RecordSdkUsage(reservation.Id, usage);
                windowBaseline = exactSession is null ? 0 : usage.RequireComplete();
                database.WriteMetadata($"worker:{worker}:lifecycle", "ready");
            }

            verified = true;
        }
        catch (Exception exception)
        {
            if (
                !session.HasSession
                && priorUsage is not null
                && (
                    exception is OperationException { Code: "session_load_failed" }
                    || exception is Win32Exception { NativeErrorCode: 2 or 3 }
                    || exception is OperationCanceledException
                )
            )
            {
                undispatchedBaseline = priorUsage;
            }

            if (reservation is not null)
            {
                credits.MarkUncertain(reservation.Id);
            }

            throw;
        }
    }

    public async Task<JsonObject> PromptAsync(string text, CancellationToken cancellationToken)
    {
        if (reservation is null || !verified || session is null || stopped)
        {
            throw new OperationException(
                "credit_unknown",
                "A diagnostic or unverified session cannot send model prompts."
            );
        }

        using CancellationTokenSource linked = CancellationTokenSource.CreateLinkedTokenSource(
            cancellationToken,
            stop.Token
        );
        await turn.WaitAsync(linked.Token).ConfigureAwait(false);
        try
        {
            await VerifyCreditsCoreAsync(linked.Token).ConfigureAwait(false);
            if (reservation.Shared)
            {
                reservation = await credits
                    .AcquireSharedWindowAsync(reservation.Id, linked.Token)
                    .ConfigureAwait(false);
                limit = reservation.Allowance - windowBaseline;
                NativeUsage usage = await session
                    .UpdateLimitAsync(SessionId!, limit, linked.Token)
                    .ConfigureAwait(false);
                SaveDiagnostic(usage);
                credits.RecordSdkUsage(reservation.Id, usage);
            }
            credits.BeginDispatch(
                database.RunId,
                reservation.Id,
                database.Configuration.RunAiCreditAllowance
            );
            nativeIdle = false;
            database.WriteMetadata($"worker:{reservation.Worker}:lifecycle", "prompting");
            JsonObject result = await session
                .SendAsync(
                    text,
                    calibration
                        ? TimeSpan.FromMinutes(2)
                        : TimeSpan.FromSeconds(database.Configuration.DurationSeconds),
                    linked.Token,
                    reservation.Shared ? RenewLimitAsync : null
                )
                .ConfigureAwait(false);
            nativeIdle = true;
            database.WriteMetadata($"worker:{reservation.Worker}:lifecycle", "ready");
            return result;
        }
        catch
        {
            verified = false;
            credits.MarkUncertain(reservation.Id);
            throw;
        }
        finally
        {
            turn.Release();
        }
    }

    public async Task VerifyCreditsAsync(CancellationToken cancellationToken)
    {
        using CancellationTokenSource linked = CancellationTokenSource.CreateLinkedTokenSource(
            cancellationToken,
            stop.Token
        );
        await turn.WaitAsync(linked.Token).ConfigureAwait(false);
        try
        {
            await VerifyCreditsCoreAsync(linked.Token).ConfigureAwait(false);
        }
        finally
        {
            turn.Release();
        }
    }

    private async Task VerifyCreditsCoreAsync(CancellationToken cancellationToken)
    {
        if (reservation is null || session is null || SessionId is null)
        {
            throw new OperationException("credit_unknown", "No paid native association.");
        }

        try
        {
            NativeUsage usage = await session
                .ReadAsync(SessionId, limit, cancellationToken)
                .ConfigureAwait(false);
            SaveDiagnostic(usage);
            credits.RecordSdkUsage(reservation.Id, usage);
            if (reservation.Shared && nativeIdle)
            {
                credits.CloseSharedWindow(reservation.Id);
            }
            credits.RequirePromptAllowance(
                database.RunId,
                reservation.Id,
                database.Configuration.RunAiCreditAllowance
            );
        }
        catch
        {
            verified = false;
            credits.MarkUncertain(reservation.Id);
            throw;
        }
    }

    private async Task<decimal> RenewLimitAsync(CancellationToken cancellationToken)
    {
        NativeUsage usage = await session!
            .ReadAsync(SessionId!, limit, cancellationToken)
            .ConfigureAwait(false);
        SaveDiagnostic(usage);
        credits.RecordSdkUsage(reservation!.Id, usage);
        reservation = await credits
            .AcquireSharedWindowAsync(reservation.Id, cancellationToken, renew: true)
            .ConfigureAwait(false);
        limit = reservation.Allowance - windowBaseline;
        return limit;
    }

    public Task CancelAsync(CancellationToken cancellationToken = default) =>
        session?.AbortAsync(cancellationToken) ?? Task.CompletedTask;

    public async Task StopAsync()
    {
        await settlement.WaitAsync(CancellationToken.None).ConfigureAwait(false);
        try
        {
            if (stopped)
            {
                return;
            }

            verified = false;
            await stop.CancelAsync().ConfigureAwait(false);
            Exception? failure = null;
            NativeUsage? finalUsage = null;
            bool turnOwned = false;
            try
            {
                if (session is not null)
                {
                    await session.AbortAsync(CancellationToken.None).ConfigureAwait(false);
                }

                turnOwned = await turn.WaitAsync(TimeSpan.FromSeconds(15), CancellationToken.None)
                    .ConfigureAwait(false);
                if (!turnOwned)
                {
                    throw new OperationException(
                        "ownership_unresolved",
                        "The native turn did not quiesce before usage reconciliation."
                    );
                }

                if (reservation is not null && started)
                {
                    if (
                        undispatchedBaseline is { } baseline
                        && session is { HasSession: false }
                        && credits.ReadCompleteSdkUsage(reservation.Id) is { } current
                        && current == baseline
                    )
                    {
                        finalUsage = baseline.Usage;
                    }
                    else
                    {
                        finalUsage = await (
                            session ?? throw new InvalidOperationException("No SDK connection.")
                        )
                            .ReadAsync(
                                reservation.SessionId!,
                                limit,
                                CancellationToken.None,
                                validateEvents: false
                            )
                            .ConfigureAwait(false);
                        SaveDiagnostic(finalUsage);
                    }
                }
                else if (reservation is not null)
                {
                    credits.ReleaseUnstarted(reservation.Id);
                }
            }
            catch (Exception exception)
            {
                failure = exception;
                if (reservation is not null)
                {
                    credits.MarkUncertain(reservation.Id);
                }
            }
            finally
            {
                try
                {
                    if (session is not null)
                    {
                        session.Quiesce();
                        if (calibration || undispatchedBaseline is not null)
                        {
                            try
                            {
                                session.RequireHealthy();
                            }
                            catch (Exception exception)
                            {
                                failure ??= exception;
                            }
                        }

                        await session.DisposeAsync().ConfigureAwait(false);
                    }
                }
                catch (Exception exception)
                {
                    failure ??= exception;
                }

                if (turnOwned)
                {
                    turn.Release();
                }
            }

            stopped = true;
            if (reservation is not null && finalUsage is not null && failure is null)
            {
                try
                {
                    // A failed unopened resume still belongs to the already-settled old runtime.
                    bool final = undispatchedBaseline is not null && !session!.HasSession;
                    credits.RecordSdkUsage(reservation.Id, finalUsage, final: final);
                    usageFinalized = final;
                    pendingFinalUsage = finalUsage;
                }
                catch (Exception exception)
                {
                    failure = exception;
                }
            }

            if (failure is not null)
            {
                settlementFailure = failure;
                if (reservation is not null && usageFinalized)
                {
                    credits.MarkUncertain(reservation.Id);
                }

                throw new OperationException(
                    "credit_or_ownership_unknown",
                    $"Native settlement retained unresolved evidence: {failure.Message}"
                );
            }

            if (reservation is not null)
            {
                database.WriteMetadata($"worker:{reservation.Worker}:lifecycle", "stopped");
            }
        }
        finally
        {
            settlement.Release();
        }
    }

    public async ValueTask DisposeAsync()
    {
        await StopAsync().ConfigureAwait(false);
        GC.SuppressFinalize(this);
    }

    internal void CompleteUsage()
    {
        if (reservation is null || !started || usageFinalized)
        {
            return;
        }
        if (!stopped || settlementFailure is not null || pendingFinalUsage is null)
        {
            RetainUncertainty();
            throw new OperationException(
                "credit_or_ownership_unknown",
                "A peer's session did not establish complete, quiescent final usage."
            );
        }

        try
        {
            credits.RecordSdkUsage(reservation.Id, pendingFinalUsage, final: true);
            usageFinalized = true;
            database.WriteMetadata($"worker:{reservation.Worker}:lifecycle", "stopped");
        }
        catch
        {
            RetainUncertainty();
            throw;
        }
    }

    internal void RetainUncertainty()
    {
        if (reservation is not null && started && !usageFinalized)
        {
            credits.MarkUncertain(reservation.Id);
        }
    }

    private void SaveDiagnostic(NativeUsage usage)
    {
        Diagnostic =
            session?.Metadata?.DeepClone().AsObject()
            ?? throw new InvalidOperationException("No observed native metadata.");
        Diagnostic["reportedNanoAiu"] = usage.TotalNanoAiu?.ToString(
            "R",
            CultureInfo.InvariantCulture
        );
        Diagnostic["aiCreditsStatus"] = usage.Status;
        Diagnostic["totalUserRequests"] = usage.TotalUserRequests;
        Diagnostic["paidReservation"] = reservation is not null;
        database.WriteMetadata(
            reservation is null
                ? $"native:preflight:{usage.SessionId}"
                : $"native:session:{usage.SessionId}",
            Diagnostic.ToJsonString()
        );
    }

    private void RecordAssociation(string id, string worker, string recovery)
    {
        CreditReservation allocation =
            reservation ?? throw new InvalidOperationException("No paid reservation.");
        using SqliteConnection connection = database.Open();
        using SqliteTransaction transaction = connection.BeginTransaction();
        Sql.Execute(
            connection,
            """
            INSERT INTO native_attempts(
                usage_path,reservation_id,worker,session_id,baseline_usage,
                initialize_json,created_at)
            VALUES($path,$reservation,$worker,$session,$baseline,$initialize,$time);
            INSERT INTO native_sessions(
                reservation_id,worker,session_id,recovery,initialize_json,created_at)
            VALUES($reservation,$worker,$session,$recovery,$initialize,$time);
            """,
            transaction,
            ("$path", allocation.UsagePath),
            ("$reservation", allocation.Id),
            ("$worker", worker),
            ("$session", id),
            ("$baseline", (allocation.Actual ?? 0).ToString(CultureInfo.InvariantCulture)),
            ("$initialize", "sdk:" + NativeSdkSession.SdkVersion),
            ("$recovery", recovery),
            ("$time", database.Now.ToString("O"))
        );
        if (!calibration)
        {
            Sql.Execute(
                connection,
                """
                INSERT INTO meta(key,value)
                VALUES($sessionKey,$session),($reservationKey,$reservation),($recoveryKey,$recovery)
                ON CONFLICT(key) DO UPDATE SET value=excluded.value;
                """,
                transaction,
                ("$sessionKey", $"worker:{worker}:session"),
                ("$session", id),
                ("$reservationKey", $"worker:{worker}:reservation"),
                ("$reservation", allocation.Id),
                ("$recoveryKey", $"worker:{worker}:recovery"),
                ("$recovery", recovery)
            );
        }

        transaction.Commit();
    }
}
