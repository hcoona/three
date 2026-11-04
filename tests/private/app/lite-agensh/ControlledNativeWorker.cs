using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
using LiteAgensh;
using Microsoft.Data.Sqlite;

namespace LiteAgensh.Tests;

// Controlled dependency evidence only. This implements neither SDK framing nor native billing.
internal sealed class ControlledNativeFactory(
    RunDatabase database,
    CreditLedger credits,
    string directory,
    Func<CancellationToken, Task>? onExactLoadSettled = null
) : INativeWorkerFactory
{
    public INativeWorker Create(CreditReservation? reservation, bool calibration) =>
        new ControlledNativeWorker(
            database,
            credits,
            directory,
            reservation,
            calibration,
            onExactLoadSettled
        );

    public Task<NativeUsage> ReadSettledUsageAsync(
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
        return Task.FromResult(usage);
    }
}

internal sealed class ControlledNativeWorker(
    RunDatabase database,
    CreditLedger credits,
    string directory,
    CreditReservation? reservation,
    bool calibration,
    Func<CancellationToken, Task>? onExactLoadSettled = null
) : INativeWorker
{
    private readonly CancellationTokenSource stop = new();
    private readonly SemaphoreSlim settlement = new(1, 1);
    private readonly SemaphoreSlim turn = new(1, 1);
    private TaskCompletionSource turnCompleted = CompletedTurn();
    private bool started;
    private bool stopped;
    private bool failedExactLoad;
    private CancellationToken loadCancellation;

    public string? SessionId { get; private set; }

    public string ReservationId =>
        reservation?.Id ?? throw new InvalidOperationException("Controlled diagnostic session.");

    public JsonObject? Diagnostic { get; private set; }

    public Task StartAsync(
        string cwd,
        string? exactSession,
        CancellationToken cancellationToken,
        string? recoveryLabel = null
    )
    {
        cancellationToken.ThrowIfCancellationRequested();
        string id = reservation?.SessionId ?? Guid.NewGuid().ToString("D");
        Record(directory, exactSession is null ? "start" : "resume", id, cwd);
        if (exactSession is not null && exactSession != id)
        {
            throw new OperationException("credit_unknown", "Controlled exact identity mismatch.");
        }

        if (reservation is not null)
        {
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

        return Task.CompletedTask;
    }

    public async Task<JsonObject> PromptAsync(string text, CancellationToken cancellationToken)
    {
        if (reservation is null || SessionId is null || stopped)
        {
            throw new OperationException("credit_unknown", "No controlled paid session.");
        }

        using CancellationTokenSource linked = CancellationTokenSource.CreateLinkedTokenSource(
            cancellationToken,
            stop.Token
        );
        await turn.WaitAsync(linked.Token);
        try
        {
            Record(directory, calibration ? "calibration-enter" : "prompt-enter", SessionId);
            turnCompleted = new(TaskCreationOptions.RunContinuationsAsynchronously);
            await VerifyCreditsCoreAsync(linked.Token);
            credits.BeginDispatch(
                database.RunId,
                reservation.Id,
                database.Configuration.RunAiCreditAllowance
            );
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

                throw new OperationException(
                    "native_stopped",
                    $"Native query for {SessionId}: prompt exceeds model input budget."
                );
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
            database.WriteMetadata($"worker:{reservation.Worker}:lifecycle", "ready");
            return new()
            {
                ["text"] = calibration ? "READY" : "controlled completed work",
                ["stopReason"] = "end_turn",
            };
        }
        catch
        {
            credits.MarkUncertain(reservation.Id);
            throw;
        }
        finally
        {
            turnCompleted.TrySetResult();
            turn.Release();
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
                    credits.RecordSdkUsage(
                        reservation.Id,
                        ReadUsage(directory, SessionId),
                        final: true
                    );
                }
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
        finally
        {
            if (turnOwned)
            {
                turn.Release();
            }

            settlement.Release();
        }
    }

    public async ValueTask DisposeAsync() => await StopAsync();

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
