using System.Diagnostics;
using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
using GitHub.Copilot;
using LiteAgensh;
using Microsoft.Data.Sqlite;

namespace LiteAgensh.Tests;

// Controlled dependency evidence only. This implements neither SDK framing nor native billing.
internal sealed class ControlledNativeFactory(
    RunDatabase database,
    CreditLedger credits,
    string directory,
    Func<CancellationToken, Task>? onExactLoadSettled = null,
    Func<CreditReservation, CancellationToken, Task>? onStart = null,
    Func<Task>? onHostSettlement = null,
    Action<CreditReservation, string>? onSettlement = null,
    Func<CreditReservation, CancellationToken, Task>? onPrompt = null,
    Func<CreditReservation, Exception, CancellationToken, Task>? onPromptFailure = null
) : INativeWorkerFactory
{
    private readonly List<(ControlledNativeWorker Worker, CreditReservation? Reservation)> workers =
        [];

    public JsonArray SettlementFailures { get; } = [];

    public JsonArray SettlementDetails { get; } = [];

    public INativeWorker Create(CreditReservation? reservation, bool calibration)
    {
        ControlledNativeWorker worker = new(
            database,
            credits,
            directory,
            reservation,
            calibration,
            onExactLoadSettled,
            onStart,
            onSettlement,
            onPrompt,
            onPromptFailure
        );
        workers.Add((worker, reservation));
        return worker;
    }

    public async Task<JsonObject> PreflightAsync(
        IReadOnlyList<(string Worker, string Cwd)> peers,
        CancellationToken cancellationToken
    )
    {
        JsonArray sessions = [];
        foreach ((string worker, string cwd) in peers)
        {
            INativeWorker native = Create(null, calibration: false);
            await native.StartAsync(cwd, null, cancellationToken);
            JsonObject diagnostic = native.Diagnostic!.DeepClone().AsObject();
            diagnostic["worker"] = worker;
            sessions.Add(diagnostic);
            await native.StopAsync();
        }
        return new()
        {
            ["sessionId"] = sessions[0]!["sessionId"]!.DeepClone(),
            ["modelPromptsSent"] = 0,
            ["diagnostics"] = sessions[0]!.DeepClone(),
            ["sessions"] = sessions,
        };
    }

    public async Task CompleteAsync()
    {
        SettlementFailures.Clear();
        SettlementDetails.Clear();
        HashSet<ControlledNativeWorker> unsettled = [];
        Exception? failure = null;
        foreach ((ControlledNativeWorker worker, CreditReservation? reservation) in workers)
        {
            try
            {
                await worker.StopAsync();
            }
            catch (Exception exception)
            {
                failure ??= exception;
                unsettled.Add(worker);
                Json.AddSettlementDetails(
                    SettlementDetails, reservation?.Worker, reservation?.Id, "stop", exception
                );
                SettlementFailures.Add(NativeWorkerFactory.CreateSettlementFailure(
                    reservation, "stop", exception
                ));
            }
        }
        RecordHost("host-closing");
        bool runtimeSettled = false;
        try
        {
            if (onHostSettlement is not null)
            {
                await onHostSettlement();
            }
            if (ControlledNativeWorker.Mode(directory) == "unsettled-host")
            {
                throw new OperationException(
                    "ownership_unresolved", "Controlled common host unsettled."
                );
            }
            RecordHost("host-settled");
            runtimeSettled = true;
        }
        catch (Exception exception)
        {
            failure ??= exception;
            Json.AddSettlementDetails(
                SettlementDetails, null, null, "native-runtime", exception
            );
            SettlementFailures.Add(NativeWorkerFactory.CreateSettlementFailure(
                null, "native-runtime", exception
            ));
        }
        foreach ((ControlledNativeWorker worker, CreditReservation? reservation) in workers)
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
                SettlementFailures.Add(NativeWorkerFactory.CreateSettlementFailure(
                    reservation, finalize ? "final-usage" : "retain-uncertainty", exception
                ));
            }
        }
        if (failure is not null)
        {
            throw new OperationException("credit_or_ownership_unknown", failure.Message);
        }
        workers.Clear();
    }

    private void RecordHost(string operation) =>
        ControlledNativeWorker.Record(directory, operation, null);

    public Task<RecoveredNativeUsage> ReadSettledUsageAsync(
        CreditReservation reservation,
        string cwd,
        string evidencePrefix,
        CancellationToken cancellationToken
    )
    {
        cancellationToken.ThrowIfCancellationRequested();
        ControlledNativeWorker.Record(directory, "recover", reservation.SessionId, cwd);
        if (ControlledNativeWorker.Mode(directory) == "unsettled-recovery")
        {
            throw new OperationException("ownership_unresolved", "Controlled recovery unsettled.");
        }

        NativeUsage usage = ControlledNativeWorker.ReadUsage(directory, reservation.SessionId!);
        ControlledNativeWorker.Record(directory, "recovered-settled", reservation.SessionId);
        return Task.FromResult(new RecoveredNativeUsage(reservation, usage));
    }
}

internal sealed class ControlledNativeWorker(
    RunDatabase database,
    CreditLedger credits,
    string directory,
    CreditReservation? reservation,
    bool calibration,
    Func<CancellationToken, Task>? onExactLoadSettled = null,
    Func<CreditReservation, CancellationToken, Task>? onStart = null,
    Action<CreditReservation, string>? onSettlement = null,
    Func<CreditReservation, CancellationToken, Task>? onPrompt = null,
    Func<CreditReservation, Exception, CancellationToken, Task>? onPromptFailure = null
) : INativeWorker
{
    private readonly CancellationTokenSource stop = new();
    private readonly SemaphoreSlim settlement = new(1, 1);
    private readonly SemaphoreSlim turn = new(1, 1);
    private TaskCompletionSource turnCompleted = CompletedTurn();
    private bool started;
    private bool stopped;
    private bool failedExactLoad;
    private bool completedSharedTurn;
    private CancellationToken loadCancellation;
    private NativeSdkSession? failedSession;
    private NativeUsage? pendingFinalUsage;
    private bool usageFinalized;
    private Exception? settlementFailure;

    public string? SessionId { get; private set; }

    public string ReservationId =>
        reservation?.Id ?? throw new InvalidOperationException("Controlled diagnostic session.");

    public JsonObject? Diagnostic { get; private set; }

    public string? NativeFailure => failedSession?.NativeFailure;

    public async Task StartAsync(
        string cwd,
        string? exactSession,
        CancellationToken cancellationToken,
        string? recoveryLabel = null,
        WorkAdmission? admission = null
    )
    {
        cancellationToken.ThrowIfCancellationRequested();
        admission?.ThrowIfClosed();
        string id = reservation?.SessionId ?? Guid.NewGuid().ToString("D");
        Record(directory, exactSession is null ? "start" : "resume", id, cwd);
        if (exactSession is not null && exactSession != id)
        {
            throw new OperationException("credit_unknown", "Controlled exact identity mismatch.");
        }

        if (reservation is not null)
        {
            if (reservation.Shared)
            {
                reservation = await credits.AcquireSharedWindowAsync(
                    reservation.Id,
                    cancellationToken,
                    admission: admission
                );
            }
            admission?.ThrowIfClosed();
            credits.MarkStarted(reservation.Id);
            started = true;
            RecordAssociation(
                id,
                recoveryLabel
                    ?? (
                        exactSession is null
                            ? "fresh-session-with-durable-summary"
                            : "exact-session-load"
                    )
            );
        }

        if (Mode(directory) == "reject-startup")
        {
            throw new OperationException("unavailable", "Controlled runtime startup rejected.");
        }

        if (Mode(directory) is "wrong-limit" or "identity-mismatch")
        {
            throw new OperationException("credit_unknown", "Controlled metadata rejected.");
        }

        SessionId = id;
        if (exactSession is null)
        {
            SaveUsage(new(id, 0, "complete"));
        }

        if (reservation is not null)
        {
            credits.AttachSession(reservation.Id, id);
            credits.RecordSdkUsage(reservation.Id, ReadUsage(directory, id));
            if (onStart is not null)
            {
                await onStart(reservation, cancellationToken);
            }
        }

        if (exactSession is not null && Mode(directory) == "no-load")
        {
            failedExactLoad = true;
            loadCancellation = cancellationToken;
            // Existing paid usage stays recoverable before the coordinator selects fresh fallback.
            throw new OperationException(
                "session_load_failed",
                "Controlled exact load unavailable."
            );
        }

        Diagnostic = new()
        {
            ["sessionId"] = id,
            ["workingDirectory"] = cwd,
            ["maxAiCredits"] = reservation?.Allowance ?? 30,
            ["controlled"] = true,
        };
        if (reservation is not null && Mode(directory) == "uncertain-after-start")
        {
            credits.MarkUncertain(reservation.Id);
        }

        if (reservation is not null)
        {
            database.WriteMetadata($"worker:{reservation.Worker}:lifecycle", "ready");
        }

    }

    public async Task<JsonObject> PromptAsync(
        string text, CancellationToken cancellationToken, WorkAdmission? admission = null
    )
    {
        if (reservation is null || SessionId is null || stopped)
        {
            throw new OperationException("credit_unknown", "No controlled paid session.");
        }

        using CancellationTokenSource linked = CancellationTokenSource.CreateLinkedTokenSource(
            cancellationToken,
            stop.Token
        );
        using CancellationTokenSource preparing = CancellationTokenSource.CreateLinkedTokenSource(
            linked.Token, admission?.Token ?? CancellationToken.None
        );
        bool turnOwned = false;
        bool dispatched = false;
        try
        {
            await turn.WaitAsync(preparing.Token);
            turnOwned = true;
            admission?.ThrowIfClosed();
            Record(directory, calibration ? "calibration-enter" : "prompt-enter", SessionId);
            turnCompleted = new(TaskCreationOptions.RunContinuationsAsynchronously);
            await VerifyCreditsCoreAsync(preparing.Token);
            if (reservation.Shared)
            {
                reservation = await credits.AcquireSharedWindowAsync(
                    reservation.Id, preparing.Token, admission: admission
                );
            }
            admission?.ThrowIfClosed();
            completedSharedTurn = false;
            credits.BeginDispatch(
                database.RunId,
                reservation.Id,
                database.Configuration.RunAiCreditAllowance,
                admission
            );
            dispatched = true;
            database.WriteMetadata($"worker:{reservation.Worker}:lifecycle", "prompting");
            NativeUsage previous = ReadUsage(directory, SessionId);
            SaveUsage(
                new(
                    SessionId,
                    (previous.TotalNanoAiu ?? 0) + 1_000_000_000,
                    "complete",
                    previous.TotalUserRequests + 1
                )
            );
            Record(directory, calibration ? "calibration" : "prompt", SessionId, text);
            if (!calibration && onPrompt is not null)
            {
                await onPrompt(reservation, linked.Token);
            }
            if (!calibration && Mode(directory) == "hold")
            {
                await Task.Delay(Timeout.InfiniteTimeSpan, linked.Token);
            }

            if (!calibration && Mode(directory) == "failed-turn")
            {
                NativeUsage current = ReadUsage(directory, SessionId);
                SaveUsage(current with { TotalNanoAiu = null, Status = "pending" });
                throw new OperationException("native_stopped", "Controlled turn failed.");
            }

            if (!calibration && Mode(directory) == "native-error")
            {
                failedSession = new(
                    database,
                    new ProcessRunner(database),
                    directory,
                    isolated: false
                );
                failedSession.RecordEvent(
                    SessionEvent.FromJson(
                        JsonSerializer.Serialize(
                            new
                            {
                                id = Guid.NewGuid(),
                                timestamp = DateTimeOffset.UtcNow,
                                type = "session.error",
                                data = new
                                {
                                    errorType = "query",
                                    message =
                                        $"Native query for {SessionId}: "
                                        + "prompt exceeds model input budget.",
                                },
                            }
                        )
                    )!
                );
                File.WriteAllText(Path.Combine(directory, $"native-error-{SessionId}"), "");
                while (
                    Directory.EnumerateFiles(directory, "native-error-*").Count()
                    < database.Configuration.WorkerCount
                )
                {
                    if (linked.IsCancellationRequested)
                    {
                        if (
                            Directory.EnumerateFiles(directory, "native-error-*").Count()
                            >= database.Configuration.WorkerCount
                        )
                        {
                            break;
                        }

                        linked.Token.ThrowIfCancellationRequested();
                    }

                    await Task.Delay(TimeSpan.FromMilliseconds(25), CancellationToken.None);
                }

                string abortMarker = Path.Combine(directory, "native-abort-entered");
                if (reservation.Worker == "worker2")
                {
                    await failedSession.AbortFailedSendAsync(
                        async () =>
                        {
                            File.WriteAllText(abortMarker, "");
                            await Task.Delay(Timeout.InfiniteTimeSpan, linked.Token);
                        },
                        linked.Token
                    );
                }
                else
                {
                    while (!File.Exists(abortMarker))
                    {
                        await Task.Delay(TimeSpan.FromMilliseconds(25), linked.Token);
                    }

                    failedSession.CheckEvents(linked.Token);
                }

                throw new InvalidOperationException("The prepared native error did not stop.");
            }

            if (
                Mode(directory) == "unknown-usage"
                || (!calibration && Mode(directory) == "partial-after-turn")
            )
            {
                NativeUsage current = ReadUsage(directory, SessionId);
                SaveUsage(current with { TotalNanoAiu = null, Status = "pending" });
            }

            Record(directory, "completed", SessionId);
            completedSharedTurn = true;
            database.WriteMetadata($"worker:{reservation.Worker}:lifecycle", "ready");
            return new()
            {
                ["text"] = calibration ? "READY" : "controlled completed work",
                ["stopReason"] = "end_turn",
            };
        }
        catch (OperationCanceledException exception) when (
            !dispatched
        )
        {
            if (linked.IsCancellationRequested || admission?.IsClosed != true)
            {
                throw;
            }
            throw new OperationException(
                "admission_closed", "Controlled admission ended before dispatch.", exception
            );
        }
        catch (Exception exception)
        {
            if (exception is OperationCanceledException && NativeFailure is not null)
            {
                Record(directory, "native-error-canceled", SessionId);
            }

            exception.Data["liteagensh.nativeFailureObserved"] = Stopwatch.GetTimestamp();
            credits.MarkUncertain(reservation.Id);
            if (!calibration && onPromptFailure is not null)
            {
                await onPromptFailure(reservation, exception, linked.Token);
            }
            throw;
        }
        finally
        {
            if (turnOwned)
            {
                turnCompleted.TrySetResult();
                turn.Release();
            }
        }
    }

    public async Task VerifyCreditsAsync(CancellationToken cancellationToken)
    {
        using CancellationTokenSource linked = CancellationTokenSource.CreateLinkedTokenSource(
            cancellationToken,
            stop.Token
        );
        await turn.WaitAsync(linked.Token);
        try
        {
            await VerifyCreditsCoreAsync(linked.Token);
        }
        finally
        {
            turn.Release();
        }
    }

    private Task VerifyCreditsCoreAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (reservation is null || SessionId is null)
        {
            throw new OperationException("credit_unknown", "No controlled paid association.");
        }

        credits.RecordSdkUsage(reservation.Id, ReadUsage(directory, SessionId));
        if (reservation.Shared && completedSharedTurn)
        {
            credits.CloseSharedWindow(reservation.Id);
        }
        credits.RequirePromptAllowance(
            database.RunId,
            reservation.Id,
            database.Configuration.RunAiCreditAllowance
        );
        return Task.CompletedTask;
    }

    public async Task StopAsync()
    {
        await settlement.WaitAsync();
        bool turnOwned = false;
        try
        {
            if (stopped)
            {
                if (settlementFailure is not null)
                {
                    throw settlementFailure;
                }
                return;
            }

            await stop.CancelAsync();
            await turnCompleted.Task.WaitAsync(TimeSpan.FromSeconds(10));
            turnOwned = await turn.WaitAsync(TimeSpan.FromSeconds(10));
            if (!turnOwned)
            {
                if (reservation is not null)
                {
                    credits.MarkUncertain(reservation.Id);
                }

                throw new OperationException(
                    "ownership_unresolved",
                    "Controlled credit verification did not quiesce."
                );
            }

            Record(directory, "stop", SessionId);
            if (Mode(directory) == "unsettled")
            {
                if (reservation is not null)
                {
                    credits.MarkUncertain(reservation.Id);
                }

                throw new OperationException("ownership_unresolved", "Controlled stop unsettled.");
            }

            if (reservation is not null)
            {
                if (!started)
                {
                    credits.ReleaseUnstarted(reservation.Id);
                }
                else if (SessionId is null)
                {
                    credits.MarkUncertain(reservation.Id);
                    throw new OperationException(
                        "credit_or_ownership_unknown",
                        "Controlled startup cost unknown."
                    );
                }
                else
                {
                    // This is controlled worker accounting, not proof of real SDK settlement.
                    pendingFinalUsage = ReadUsage(directory, SessionId);
                    credits.RecordSdkUsage(
                        reservation.Id,
                        pendingFinalUsage,
                        final: failedExactLoad
                    );
                    usageFinalized = failedExactLoad;
                }
            }

            if (reservation is not null)
            {
                onSettlement?.Invoke(reservation, "stop");
            }
            stopped = true;
            if (reservation is not null)
            {
                database.WriteMetadata($"worker:{reservation.Worker}:lifecycle", "stopped");
            }

            Record(directory, "settled", SessionId);
            if (failedExactLoad && onExactLoadSettled is not null)
            {
                await onExactLoadSettled(loadCancellation);
            }
        }
        catch (Exception exception) when (!stopped)
        {
            stopped = true;
            settlementFailure = exception;
            throw;
        }
        finally
        {
            if (turnOwned)
            {
                turn.Release();
            }

            settlement.Release();
        }
    }

    public async ValueTask DisposeAsync()
    {
        await StopAsync();
        if (reservation is not null)
        {
            onSettlement?.Invoke(reservation, "dispose");
        }
        if (failedSession is not null)
        {
            await failedSession.DisposeAsync();
        }
    }

    internal void CompleteUsage()
    {
        if (reservation is null || !started || usageFinalized)
        {
            return;
        }
        if (!stopped || pendingFinalUsage is null)
        {
            RetainUncertainty();
            throw new OperationException("ownership_unresolved", "Controlled peer did not settle.");
        }
        credits.RecordSdkUsage(reservation.Id, pendingFinalUsage, final: true);
        usageFinalized = true;
    }

    internal void RetainUncertainty()
    {
        if (reservation is not null && started && !usageFinalized)
        {
            credits.MarkUncertain(reservation.Id);
        }
    }

    internal static string Mode(string directory) =>
        File.ReadAllText(Path.Combine(directory, "mode.txt"));

    internal static NativeUsage ReadUsage(string directory, string id) =>
        JsonSerializer.Deserialize<NativeUsage>(
            File.ReadAllText(Path.Combine(directory, $"session-{id}.json")),
            Json.Options
        ) ?? throw new InvalidOperationException("Missing controlled usage.");

    internal static void Record(
        string directory,
        string operation,
        string? session,
        string? value = null
    )
    {
        string path = Path.Combine(directory, $"operation-{Guid.NewGuid():N}.json");
        File.WriteAllText(
            path + ".tmp",
            new JsonObject
            {
                ["operation"] = operation,
                ["sessionId"] = session,
                ["value"] = value,
            }.ToJsonString()
        );
        File.Move(path + ".tmp", path);
    }

    private void SaveUsage(NativeUsage usage)
    {
        string path = Path.Combine(directory, $"session-{usage.SessionId}.json");
        File.WriteAllText(path + ".tmp", JsonSerializer.Serialize(usage, Json.Options));
        File.Move(path + ".tmp", path, overwrite: true);
    }

    private void RecordAssociation(string id, string recovery)
    {
        CreditReservation allocation =
            reservation ?? throw new InvalidOperationException("No controlled reservation.");
        using SqliteConnection connection = database.Open();
        using SqliteTransaction transaction = connection.BeginTransaction();
        // Fixture association shape needed by real coordinator recovery validation.
        // Atomicity of this substitute is NOT evidence about NativeWorker.RecordAssociation.
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
            ("$worker", allocation.Worker),
            ("$session", id),
            ("$baseline", (allocation.Actual ?? 0).ToString(CultureInfo.InvariantCulture)),
            ("$initialize", "sdk:" + NativeSdkSession.SdkVersion),
            ("$time", database.Now.ToString("O")),
            ("$recovery", recovery)
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
                ("$sessionKey", $"worker:{allocation.Worker}:session"),
                ("$session", id),
                ("$reservationKey", $"worker:{allocation.Worker}:reservation"),
                ("$reservation", allocation.Id),
                ("$recoveryKey", $"worker:{allocation.Worker}:recovery"),
                ("$recovery", recovery)
            );
        }

        transaction.Commit();
    }

    private static TaskCompletionSource CompletedTurn()
    {
        TaskCompletionSource completed = new(TaskCreationOptions.RunContinuationsAsynchronously);
        completed.SetResult();
        return completed;
    }
}
