using System.Runtime.ExceptionServices;
using System.Text.Json.Nodes;

namespace LiteAgensh;

internal interface INativeWorker : IAsyncDisposable
{
    string? SessionId { get; }

    string ReservationId { get; }

    JsonObject? Diagnostic { get; }

    string? NativeFailure { get; }

    Task StartAsync(
        string cwd,
        string? exactSession,
        CancellationToken cancellationToken,
        string? recoveryLabel = null
    );

    Task<JsonObject> PromptAsync(string text, CancellationToken cancellationToken);

    Task VerifyCreditsAsync(CancellationToken cancellationToken);

    Task StopAsync();
}

internal interface INativeWorkerFactory
{
    JsonArray SettlementFailures { get; }

    INativeWorker Create(CreditReservation? reservation, bool calibration);

    Task<JsonObject> PreflightAsync(
        IReadOnlyList<(string Worker, string Cwd)> peers,
        CancellationToken cancellationToken
    );

    Task CompleteAsync();

    Task<NativeUsage> ReadSettledUsageAsync(
        CreditReservation reservation,
        string cwd,
        string evidencePrefix,
        CancellationToken cancellationToken
    );
}

internal sealed class NativeWorkerFactory(
    RunDatabase database,
    ProcessRunner processes,
    CreditLedger credits
) : INativeWorkerFactory
{
    private readonly object lifecycle = new();
    private readonly List<(NativeWorker Worker, CreditReservation? Reservation)> workers = [];
    private NativeSdkHost? host;
    private bool closing;

    public JsonArray SettlementFailures { get; } = [];

    public INativeWorker Create(CreditReservation? reservation, bool calibration)
    {
        lock (lifecycle)
        {
            NativeWorker worker = new(database, GetHost(), credits, reservation, calibration);
            workers.Add((worker, reservation));
            return worker;
        }
    }

    public async Task<JsonObject> PreflightAsync(
        IReadOnlyList<(string Worker, string Cwd)> peers,
        CancellationToken cancellationToken
    )
    {
        NativeSdkHost backend = GetHost();
        List<NativeSdkSession> sessions = [];
        JsonArray diagnostics = [];
        JsonObject? result = null;
        Exception? failure = null;
        try
        {
            foreach ((string worker, string cwd) in peers)
            {
                NativeSdkSession session = new(database, backend, cwd, isolated: false);
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

            NativeSdkSession first = sessions[0];
            foreach (decimal limit in new decimal[] { 60, 30 })
            {
                RequireUnprompted(await first.UpdateLimitAsync(
                    first.SessionId, limit, cancellationToken
                ).ConfigureAwait(false));
                foreach (NativeSdkSession other in sessions.Skip(1))
                {
                    RequireUnprompted(await other.ReadAsync(
                        other.SessionId, 30, cancellationToken
                    ).ConfigureAwait(false));
                }
            }
            diagnostics.Add(first.Metadata!.DeepClone());
            first.Quiesce();
            await first.DisposeAsync().ConfigureAwait(false);
            foreach (NativeSdkSession other in sessions.Skip(1))
            {
                RequireUnprompted(await other.ReadAsync(
                    other.SessionId, 30, cancellationToken
                ).ConfigureAwait(false));
                diagnostics.Add(other.Metadata!.DeepClone());
            }

            result = new()
            {
                ["sessionId"] = first.SessionId,
                ["modelPromptsSent"] = 0,
                ["diagnostics"] = first.Metadata!.DeepClone(),
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
            foreach (NativeSdkSession session in sessions)
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
        NativeSdkHost? backend;
        lock (lifecycle)
        {
            closing = true;
            cohort = workers.ToArray();
            backend = host;
        }
        SettlementFailures.Clear();
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

    private NativeSdkHost GetHost()
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
            return host ??= new(database, processes);
        }
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

    public async Task<NativeUsage> ReadSettledUsageAsync(
        CreditReservation reservation,
        string cwd,
        string evidencePrefix,
        CancellationToken cancellationToken
    )
    {
        decimal remaining = reservation.Allowance - (reservation.Actual ?? 0);
        decimal? cap = remaining > 0 ? remaining : null;
        NativeSdkSession recovery = new(
            database,
            processes,
            cwd,
            reservation.Worker == "calibration"
        );
        NativeUsage usage;
        try
        {
            await recovery
                .OpenAsync(
                    reservation.Worker,
                    reservation.SessionId!,
                    cap,
                    reservation.SessionId,
                    evidencePrefix,
                    cancellationToken
                )
                .ConfigureAwait(false);
            usage = await recovery
                .ReadAsync(reservation.SessionId!, cap, cancellationToken, validateEvents: false)
                .ConfigureAwait(false);
        }
        finally
        {
            recovery.Quiesce();
            await recovery.DisposeAsync().ConfigureAwait(false);
        }

        return usage;
    }
}
