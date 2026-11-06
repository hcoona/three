using System.Globalization;
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
        decimal cap = reservation.Allowance - (reservation.Actual ?? 0);
        if (cap <= 0)
        {
            throw new OperationException(
                "credit_unknown", "Exact recovery has no remaining finite backed native cap."
            );
        }
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
                .ReadSettledAsync(reservation.SessionId!, cap, deadline.Token)
                .ConfigureAwait(false);
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
}
