using System.Text.Json.Nodes;

namespace LiteAgensh.Tests.Support;

// Observe actual operation boundaries, not scheduling or native responses.
// In particular ACK is not treated as financial settlement.
internal sealed class Phase7Handoff : IDisposable
{
    private bool followingUp;
    private bool observingOwner;
    private CancellationTokenRegistration ownerObservation;
    internal TaskCompletionSource OwnerStopped { get; } =
        new(TaskCreationOptions.RunContinuationsAsynchronously);
    internal TaskCompletionSource<bool> FirstStarted { get; } =
        new(TaskCreationOptions.RunContinuationsAsynchronously);
    internal TaskCompletionSource<bool> SecondStarted { get; } =
        new(TaskCreationOptions.RunContinuationsAsynchronously);
    internal TaskCompletionSource<Startup> FirstStarting { get; } =
        new(TaskCreationOptions.RunContinuationsAsynchronously);
    internal TaskCompletionSource<Startup> SecondStarting { get; } =
        new(TaskCreationOptions.RunContinuationsAsynchronously);
    internal TaskCompletionSource<Startup> ThirdStarting { get; } =
        new(TaskCreationOptions.RunContinuationsAsynchronously);
    internal TaskCompletionSource<decimal> PositiveRecorded { get; } =
        new(TaskCreationOptions.RunContinuationsAsynchronously);
    internal TaskCompletionSource<decimal> FollowupRecorded { get; } =
        new(TaskCreationOptions.RunContinuationsAsynchronously);
    internal TaskCompletionSource<Task<JsonObject>> FollowupPreparation { get; } =
        new(TaskCreationOptions.RunContinuationsAsynchronously);

    internal INativeSdkHost Wrap(INativeSdkHost host) => new Host(this, host);

    internal INativeWorker Wrap(INativeWorker worker, string name) =>
        new Worker(this, worker, name);

    public void Dispose()
    {
        ownerObservation.Dispose();
        OwnerStopped.TrySetCanceled();
        FirstStarted.TrySetCanceled();
        SecondStarted.TrySetCanceled();
        FirstStarting.TrySetCanceled();
        SecondStarting.TrySetCanceled();
        ThirdStarting.TrySetCanceled();
        PositiveRecorded.TrySetCanceled();
        FollowupRecorded.TrySetCanceled();
        FollowupPreparation.TrySetCanceled();
    }

    internal sealed record Startup(string Worker, string ReservationId, string Cwd, Task Operation);

    private TaskCompletionSource<Startup>? Starting(string name) =>
        name switch
        {
            "worker1" => FirstStarting,
            "worker2" => SecondStarting,
            "worker3" => ThirdStarting,
            _ => null,
        };

    private sealed class Worker(Phase7Handoff observation, INativeWorker inner, string name)
        : INativeWorker
    {
        private int prompts;
        public string? SessionId => inner.SessionId;
        public string ReservationId => inner.ReservationId;
        public JsonObject? Diagnostic => inner.Diagnostic;
        public string? NativeFailure => inner.NativeFailure;
        public Exception? FirstFailure => inner.FirstFailure;

        public void BindOwner(CancellationToken token)
        {
            inner.BindOwner(token);
            if (!observation.observingOwner)
            {
                observation.observingOwner = true;
                observation.ownerObservation = token.Register(() =>
                    observation.OwnerStopped.TrySetResult()
                );
            }
        }

        public NativeRequest BeginRequest(CancellationToken token) => inner.BeginRequest(token);

        public async Task StartAsync(
            string cwd,
            string? exactSession,
            CancellationToken token,
            string? recoveryLabel = null,
            WorkAdmission? admission = null
        )
        {
            Task actual;
            try
            {
                actual = inner.StartAsync(cwd, exactSession, token, recoveryLabel, admission);
            }
            catch (Exception error)
            {
                // A synchronous interface failure has no returned Task. Wake
                // the entry observer with that SAME failure, then propagate it.
                observation.Starting(name)?.TrySetException(error);
                throw;
            }
            observation.Starting(name)?.TrySetResult(new(name, inner.ReservationId, cwd, actual));
            await actual.ConfigureAwait(false);
            (name == "worker1" ? observation.FirstStarted : observation.SecondStarted).TrySetResult(
                true
            );
        }

        public Task<JsonObject> PromptAsync(
            string text,
            CancellationToken token,
            WorkAdmission? admission = null
        )
        {
            // The real method runs synchronously through completed typed reads
            // and into its cancellable backing wait before returning this task.
            bool followup = name == "worker1" && ++prompts == 2;
            if (followup)
            {
                observation.followingUp = true;
            }
            Task<JsonObject> actual = inner.PromptAsync(text, token, admission);
            if (followup)
            {
                observation.FollowupPreparation.TrySetResult(actual);
            }
            return actual;
        }

        public Task VerifyCreditsAsync(CancellationToken token) => inner.VerifyCreditsAsync(token);

        public Task StopAsync() => inner.StopAsync();

        public ValueTask DisposeAsync() => inner.DisposeAsync();
    }

    private sealed class Host(Phase7Handoff observation, INativeSdkHost inner) : INativeSdkHost
    {
        public string Id => inner.Id;
        public long LeaseId => inner.LeaseId;

        public INativeSdkSession CreateSession(string cwd, bool isolated) =>
            new Session(observation, inner.CreateSession(cwd, isolated));

        public ValueTask DisposeAsync() => inner.DisposeAsync();
    }

    private sealed class Session(Phase7Handoff observation, INativeSdkSession inner)
        : INativeSdkSession
    {
        private string worker = "";
        public string SessionId => inner.SessionId;
        public bool HasSession => inner.HasSession;
        public string? NativeFailure => inner.NativeFailure;
        public Exception? FirstFailure => inner.FirstFailure;
        public JsonObject? Metadata => inner.Metadata;

        public IDisposable? ObserveRequest(CancellationToken token) => inner.ObserveRequest(token);

        public Task OpenAsync(
            string name,
            string sessionId,
            decimal? limit,
            string? exactSession,
            string evidencePath,
            CancellationToken token
        )
        {
            worker = name;
            return inner.OpenAsync(name, sessionId, limit, exactSession, evidencePath, token);
        }

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

        public void WithSettledUsage(NativeUsage usage, Action record)
        {
            inner.WithSettledUsage(usage, record);
            // The REAL record action has returned, including CloseSharedWindow.
            // Never call it a second time or infer its effects from an ACK.
            if (worker == "worker1" && usage.RequireComplete() > 0)
            {
                observation.PositiveRecorded.TrySetResult(usage.RequireComplete());
                if (observation.followingUp)
                {
                    observation.FollowupRecorded.TrySetResult(usage.RequireComplete());
                }
            }
        }

        public Task<NativeUsage> UpdateLimitAsync(
            string session,
            decimal limit,
            CancellationToken token
        ) => inner.UpdateLimitAsync(session, limit, token);

        public void MarkDispatchPending() => inner.MarkDispatchPending();

        public Task<JsonObject> SendAsync(
            string text,
            TimeSpan timeout,
            CancellationToken token,
            Func<CancellationToken, Task<decimal>>? renewLimit = null,
            WorkAdmission? admission = null
        ) => inner.SendAsync(text, timeout, token, renewLimit, admission);

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
