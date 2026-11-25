using System.Text.Json.Nodes;

namespace LiteAgensh.Tests.Support;

// One controlled exhausted-limit notification on an actual pending send.
// The ordinary owned host/session, usage fence and real worker stay intact.
// Use the supported typed handler with the worker's REAL renewal callback.
internal sealed class ExhaustionSessionHost
{
    private readonly TaskCompletionSource requested = new(
        TaskCreationOptions.RunContinuationsAsynchronously
    );
    internal TaskCompletionSource<bool> Entered { get; } =
        new(TaskCreationOptions.RunContinuationsAsynchronously);
    internal TaskCompletionSource<bool> Rejected { get; } =
        new(TaskCreationOptions.RunContinuationsAsynchronously);
    internal int Applied { get; private set; }
    internal int Verified { get; private set; }

    internal void Request() => requested.TrySetResult();

    internal INativeSdkHost Wrap(INativeSdkHost host) => new Host(this, host);

    private sealed class Host(ExhaustionSessionHost control, INativeSdkHost inner) : INativeSdkHost
    {
        public string Id => inner.Id;
        public long LeaseId => inner.LeaseId;

        public INativeSdkSession CreateSession(string cwd, bool isolated) =>
            new Session(control, inner.CreateSession(cwd, isolated));

        public ValueTask DisposeAsync() => inner.DisposeAsync();
    }

    private sealed class Session(ExhaustionSessionHost control, INativeSdkSession inner)
        : INativeSdkSession
    {
        public string SessionId => inner.SessionId;
        public bool HasSession => inner.HasSession;
        public string? NativeFailure => inner.NativeFailure;
        public JsonObject? Metadata => inner.Metadata;

        public Task OpenAsync(
            string worker,
            string sessionId,
            decimal? limit,
            string? exactSession,
            string evidencePath,
            CancellationToken token
        ) => inner.OpenAsync(worker, sessionId, limit, exactSession, evidencePath, token);

        public Task<NativeUsage> ReadAsync(
            string session,
            decimal? limit,
            CancellationToken token,
            bool validateEvents = true
        ) => inner.ReadAsync(session, limit, token, validateEvents);

        public Task<NativeUsage> ReadSettledAsync(
            string session,
            decimal? limit,
            CancellationToken token,
            bool validateEvents = true,
            bool verifyLocalWork = false
        ) => inner.ReadSettledAsync(session, limit, token, validateEvents, verifyLocalWork);

        public void WithSettledUsage(NativeUsage usage, Action record) =>
            inner.WithSettledUsage(usage, record);

        public Task<NativeUsage> UpdateLimitAsync(
            string session,
            decimal limit,
            CancellationToken token
        ) => inner.UpdateLimitAsync(session, limit, token);

        public void MarkDispatchPending() => inner.MarkDispatchPending();

        public async Task<JsonObject> SendAsync(
            string text,
            TimeSpan timeout,
            CancellationToken token,
            Func<CancellationToken, Task<decimal>>? renewLimit = null,
            WorkAdmission? admission = null
        )
        {
            using CancellationTokenSource sending = CancellationTokenSource.CreateLinkedTokenSource(
                token
            );
            Task<JsonObject> response = inner.SendAsync(
                text,
                timeout,
                sending.Token,
                renewLimit,
                admission
            );
            Task request = control.requested.Task.WaitAsync(sending.Token);
            control.Entered.TrySetResult(true);
            Exception? primary = null;
            try
            {
                if (await Task.WhenAny(response, request).ConfigureAwait(false) == response)
                {
                    return await response.ConfigureAwait(false);
                }
                await request.ConfigureAwait(false);
#pragma warning disable GHCP001
                await NativeSdkSession
                    .HandleExhaustedLimitAsync(
                        "controlled-exhausted",
                        renewLimit
                            ?? throw new InvalidOperationException(
                                "Shared renewal callback missing."
                            ),
                        async (_, response, next) =>
                        {
                            control.Applied++;
                            await inner
                                .UpdateLimitAsync(
                                    SessionId,
                                    (decimal)(
                                        response.MaxAiCredits
                                        ?? throw new InvalidOperationException(
                                            "Missing finite Set cap."
                                        )
                                    ),
                                    next
                                )
                                .ConfigureAwait(false);
                        },
                        async (limit, next) =>
                        {
                            control.Verified++;
                            await inner.ReadAsync(SessionId, limit, next).ConfigureAwait(false);
                        },
                        sending.Token,
                        admission
                    )
                    .ConfigureAwait(false);
#pragma warning restore GHCP001
                return await response.ConfigureAwait(false);
            }
            catch (Exception error)
            {
                primary = error;
                if (error is NativeWorker.RenewalAdmissionCanceledException)
                {
                    control.Rejected.TrySetResult(true);
                }
                throw;
            }
            finally
            {
                await sending.CancelAsync().ConfigureAwait(false);
                try
                {
                    await response.ConfigureAwait(false);
                }
                catch (Exception error)
                    when (primary is not null || error is OperationCanceledException)
                {
                    // This is the same cancelled child Send, not a second result.
                    // Its missing terminal evidence remains in the inner fence.
                }
                try
                {
                    await request.ConfigureAwait(false);
                }
                catch (OperationCanceledException) when (sending.IsCancellationRequested) { }
            }
        }

        public Task AbortAsync(CancellationToken token) => inner.AbortAsync(token);

        public void Quiesce() => inner.Quiesce();

        public void RequireHealthy() => inner.RequireHealthy();

        public ValueTask DisposeAsync() => inner.DisposeAsync();

        public Task<OriginalNativeShutdown> ReadOriginalShutdownAsync(
            NativeRecoveryOrigin origin,
            string path,
            CancellationToken token
        ) => inner.ReadOriginalShutdownAsync(origin, path, token);
    }
}
