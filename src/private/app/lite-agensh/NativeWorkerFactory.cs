using System.Text.Json.Nodes;

namespace LiteAgensh;

internal interface INativeWorker : IAsyncDisposable
{
    string? SessionId { get; }

    string ReservationId { get; }

    JsonObject? Diagnostic { get; }

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
    INativeWorker Create(CreditReservation? reservation, bool calibration);

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
    public INativeWorker Create(CreditReservation? reservation, bool calibration) =>
        new NativeWorker(database, processes, credits, reservation, calibration);

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
