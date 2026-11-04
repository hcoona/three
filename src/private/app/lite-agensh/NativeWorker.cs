using System.ComponentModel;
using System.Globalization;
using System.Text.Json.Nodes;
using Microsoft.Data.Sqlite;

namespace LiteAgensh;

internal sealed class NativeWorker : INativeWorker
{
    private readonly RunDatabase database;
    private readonly ProcessRunner processes;
    private readonly CreditLedger credits;
    private readonly CreditReservation? reservation;
    private readonly bool calibration;
    private readonly SemaphoreSlim turn = new(1, 1);
    private readonly SemaphoreSlim settlement = new(1, 1);
    private readonly CancellationTokenSource stop = new();
    private NativeSdkSession? session;
    private bool started;
    private bool stopped;
    private bool verified;
    private decimal limit;
    private string? evidencePath;
    private (NativeUsage Usage, long Dispatches)? undispatchedBaseline;

    public NativeWorker(
        RunDatabase database,
        ProcessRunner processes,
        CreditLedger credits,
        CreditReservation? reservation = null,
        bool calibration = false
    )
    {
        this.database = database;
        this.processes = processes;
        this.credits = credits;
        this.reservation = reservation;
        this.calibration = calibration;
    }

    public string? SessionId { get; private set; }

    public string ReservationId =>
        reservation?.Id ?? throw new InvalidOperationException("Non-paid diagnostic session.");

    public JsonObject? Diagnostic { get; private set; }

    public async Task StartAsync(
        string cwd,
        string? exactSession,
        CancellationToken cancellationToken,
        string? recoveryLabel = null
    )
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (exactSession is not null && exactSession != reservation?.SessionId)
        {
            throw new OperationException("credit_unknown", "Exact session association mismatched.");
        }

        string id = reservation?.SessionId ?? Guid.NewGuid().ToString("D");
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

        session = new(database, processes, cwd, calibration);
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
            await VerifyCreditsAsync(linked.Token).ConfigureAwait(false);
            credits.BeginDispatch(
                database.RunId,
                reservation.Id,
                database.Configuration.RunAiCreditAllowance
            );
            database.WriteMetadata($"worker:{reservation.Worker}:lifecycle", "prompting");
            JsonObject result = await session
                .SendAsync(
                    text,
                    calibration
                        ? TimeSpan.FromMinutes(2)
                        : TimeSpan.FromSeconds(database.Configuration.DurationSeconds),
                    linked.Token
                )
                .ConfigureAwait(false);
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
                    credits.RecordSdkUsage(reservation.Id, finalUsage, final: true);
                }
                catch (Exception exception)
                {
                    failure = exception;
                }
            }

            if (failure is not null)
            {
                if (reservation is not null)
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
