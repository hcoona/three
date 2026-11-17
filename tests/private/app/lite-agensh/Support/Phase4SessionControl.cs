using System.Diagnostics;
using System.Text.Json.Nodes;
using GitHub.Copilot;

namespace LiteAgensh.Tests.Support;

// Adds dependency-owned work/settlement transitions to the existing typed guard.
// No native client, transport, model or historical recovery is implemented here.
internal sealed class Phase4SessionControl : INativeSdkHost, IAsyncDisposable
{
    private readonly TaskCompletionSource release = Signal();
    private OwnedChildControl? child;
    private Process? process;
    private ProcessRunner? processes;
    private bool settled;

    internal Phase4SessionControl(
        TypedHostControl? state = null,
        TimeProvider? settlementTimeProvider = null
    )
    {
        State = state ?? new();
        Factory = new(
            State.Database,
            new ProcessRunner(State.Database),
            State.Ledger,
            () => this,
            settlementTimeProvider
        );
    }

    internal TypedHostControl State { get; }

    internal NativeWorkerFactory Factory { get; }

    public string Id { get; } = Guid.NewGuid().ToString("N");

    public long LeaseId { get; private set; }

    internal OwnedChildIdentity? ReadyIdentity { get; private set; }

    internal List<Session> Sessions { get; } = [];

    internal TaskCompletionSource HostEntered { get; } = Signal();

    internal bool HoldHost { get; set; }

    internal OperationException? HostFailure { get; set; }

    internal bool FailExactOpen { get; set; }

    internal async Task StartOwnedRuntimeAsync(CancellationToken token)
    {
        child = new();
        process = child.StartDirect();
        processes = new(State.Database);
        LeaseId = processes.Register(process, $"native:runtime:{Id}");
        ReadyIdentity = await child.ReadReadyAsync(token).ConfigureAwait(false);
        if (
            ReadyIdentity.Pid != process.Id
            || ReadyIdentity.BirthTicks != process.StartTime.ToUniversalTime().Ticks
        )
        {
            throw new InvalidOperationException("The own-build readiness identity disagrees.");
        }
        RecordRuntime("ready");
    }

    internal CreditReservation Reserve(string worker, bool shared = true) =>
        shared
            ? State.Reserve(worker)
            : State.Ledger.Reserve(
                State.Database.RunId,
                worker,
                100,
                400,
                State.Cwd(worker + ".usage"),
                shared: false
            );

    public INativeSdkSession CreateSession(string cwd, bool isolated)
    {
        ObjectDisposedException.ThrowIf(settled, this);
        Session session = new(
            this,
            (TypedHostControl.TypedSession)State.CreateSession(cwd, isolated),
            Path.GetFullPath(cwd),
            isolated
        );
        Sessions.Add(session);
        return session;
    }

    internal void ReleaseHost() => release.TrySetResult();

    public async ValueTask DisposeAsync()
    {
        // The factory invokes this boundary after its worker stop attempts.
        HostEntered.TrySetResult();
        if (HoldHost)
        {
            await release
                .Task.WaitAsync(TimeSpan.FromSeconds(5), CancellationToken.None)
                .ConfigureAwait(false);
        }
        if (HostFailure is not null)
        {
            throw HostFailure;
        }
        if (!settled && child is not null)
        {
            await child.ReleaseAsync(CancellationToken.None).ConfigureAwait(false);
            await process!
                .WaitForExitAsync(CancellationToken.None)
                .WaitAsync(TimeSpan.FromSeconds(5), CancellationToken.None)
                .ConfigureAwait(false);
            if (process.ExitCode != 0)
            {
                throw new InvalidOperationException(
                    "The owned runtime helper did not exit successfully."
                );
            }
            processes!.Complete(LeaseId);
            RecordRuntime("settled");
        }
        settled = true;
    }

    internal async Task CleanupAsync(bool preserveState = false)
    {
        ReleaseHost();
        if (child is not null)
        {
            await child.DisposeAsync().ConfigureAwait(false);
        }
        if (!preserveState)
        {
            State.Dispose();
        }
    }

    private void RecordRuntime(string state) =>
        State.Database.WriteMetadata(
            $"native:runtime:{Id}",
            new JsonObject
            {
                ["runtimeId"] = Id,
                ["runtimeLeaseId"] = LeaseId,
                ["runtimeVersion"] = Phase6NativeControl.RuntimeVersion,
                ["state"] = state,
                ["evidenceKind"] = "controlled-native-dependency",
            }.ToJsonString()
        );

    internal static TaskCompletionSource Signal() =>
        new(TaskCreationOptions.RunContinuationsAsynchronously);

    internal sealed class Session(
        Phase4SessionControl host,
        TypedHostControl.TypedSession inner,
        string cwd,
        bool isolated
    ) : INativeSdkSession
    {
        private readonly NativeUsageFence fence = new();
        private NativeUsage? usage;
        private string? worker;
        private decimal? limit;

        public string SessionId => inner.SessionId;

        public bool HasSession => inner.HasSession;

        public string? NativeFailure => FirstFailure?.Message;

        public Exception? FirstFailure => inner.FirstFailure ?? StopFailure;

        public JsonObject? Metadata { get; private set; }

        internal TypedHostControl.TypedSession Guard => inner;

        internal bool Closed { get; private set; }

        internal string? OpenedWorker => worker;

        internal string OpenedCwd => cwd;

        internal bool Isolated => isolated;

        internal string? ExactSession { get; private set; }

        internal string? AcceptedText { get; private set; }

        internal NativeUsage Current =>
            usage ?? throw new InvalidOperationException("Session not opened.");

        internal double TurnNanoAiu { get; set; } = 7_250_000_000;

        internal OperationException? StopFailure { get; set; }

        internal bool HoldSend { get; set; }

        internal bool ChangeDuringRead { get; set; }

        internal bool ChangeOnClose { get; set; }

        internal TaskCompletionSource SendEntered { get; } = Signal();

        internal TaskCompletionSource FenceEntered { get; private set; } = Signal();

        internal Task ArmFence() => (FenceEntered = Signal()).Task;

        internal TaskCompletionSource SendRelease { get; } = Signal();

        internal void Publish(double? nano, string? status, long requests) =>
            usage = new(SessionId, nano, status, requests);

        internal void CompactionStart() =>
            fence.Observe(
                new SessionCompactionStartEvent
                {
                    Id = Guid.NewGuid(),
                    Timestamp = DateTimeOffset.UtcNow,
                    Data = new() { CompactionAttemptId = "owned-compaction" },
                }
            );

        internal void CompactionComplete(bool success = true) =>
            fence.Observe(
                new SessionCompactionCompleteEvent
                {
                    Id = Guid.NewGuid(),
                    Timestamp = DateTimeOffset.UtcNow,
                    Data = new() { CompactionAttemptId = "owned-compaction", Success = success },
                }
            );

        public async Task OpenAsync(
            string actor,
            string sessionId,
            decimal? cap,
            string? exactSession,
            string evidencePath,
            CancellationToken cancellationToken
        )
        {
            worker = actor;
            limit = cap;
            ExactSession = exactSession;
            if (exactSession is not null && host.FailExactOpen)
            {
                throw new OperationException(
                    "session_load_failed",
                    "Controlled exact session is unavailable."
                );
            }
            await inner
                .OpenAsync(actor, sessionId, cap, exactSession, evidencePath, cancellationToken)
                .ConfigureAwait(false);
            Publish(0, null, 0);
            host.State.Database.WriteMetadata(
                $"native:runtime:{host.Id}:session:{sessionId}",
                new JsonObject
                {
                    ["runtimeId"] = host.Id,
                    ["runtimeLeaseId"] = host.LeaseId,
                    ["sessionId"] = sessionId,
                    ["worker"] = actor,
                    ["workingDirectory"] = cwd,
                }.ToJsonString()
            );
        }

        public async Task<NativeUsage> ReadAsync(
            string expectedSession,
            decimal? expectedLimit,
            CancellationToken cancellationToken,
            bool validateEvents = true
        )
        {
            _ = await inner
                .ReadAsync(expectedSession, expectedLimit, cancellationToken, validateEvents)
                .ConfigureAwait(false);
            Metadata = inner.Metadata!.DeepClone().AsObject();
            Metadata["runtimeId"] = host.Id;
            Metadata["runtimeLeaseId"] = host.LeaseId;
            if (ChangeDuringRead)
            {
                ChangeDuringRead = false;
                CompactionStart();
                CompactionComplete();
            }
            return Current;
        }

        public async Task<NativeUsage> UpdateLimitAsync(
            string expectedSession,
            decimal cap,
            CancellationToken cancellationToken
        )
        {
            _ = await inner
                .UpdateLimitAsync(expectedSession, cap, cancellationToken)
                .ConfigureAwait(false);
            limit = cap;
            return await ReadAsync(expectedSession, cap, cancellationToken).ConfigureAwait(false);
        }

        public Task<NativeUsage> ReadSettledAsync(
            string expectedSession,
            decimal? expectedLimit,
            CancellationToken cancellationToken,
            bool validateEvents = true,
            bool verifyLocalWork = false
        )
        {
            Task<NativeUsage> captured = fence.CaptureAsync(
                token => ReadAsync(expectedSession, expectedLimit, token, validateEvents),
                cancellationToken
            );
            // CaptureAsync has synchronously reached its read/wait boundary.
            // This signal is explicit dependency entry, not an incomplete-task oracle.
            FenceEntered.TrySetResult();
            return captured;
        }

        public void WithSettledUsage(NativeUsage reading, Action record) =>
            fence.Use(reading, record);

        public void MarkDispatchPending() => fence.SetTurnPending(true);

        public async Task<JsonObject> SendAsync(
            string text,
            TimeSpan timeout,
            CancellationToken cancellationToken,
            Func<CancellationToken, Task<decimal>>? renewLimit = null,
            WorkAdmission? admission = null
        )
        {
            SendEntered.TrySetResult();
            if (HoldSend)
            {
                await SendRelease.Task.WaitAsync(cancellationToken).ConfigureAwait(false);
            }
            cancellationToken.ThrowIfCancellationRequested();
            admission?.ThrowIfClosed();
            AcceptedText = text;
            Publish(TurnNanoAiu, "complete", Current.TotalUserRequests + 1);
            fence.SetTurnPending(false);
            return new()
            {
                ["sessionId"] = SessionId,
                ["actor"] = worker,
                ["cwd"] = cwd,
                ["acceptedText"] = text,
                ["appliedLimit"] = limit,
                ["evidenceKind"] = "controlled-native-dependency",
            };
        }

        public Task AbortAsync(CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return StopFailure is null ? Task.CompletedTask : Task.FromException(StopFailure);
        }

        public void Quiesce() { }

        public void RequireHealthy() => inner.RequireHealthy();

        public async ValueTask DisposeAsync()
        {
            Closed = true;
            if (ChangeOnClose)
            {
                CompactionStart();
                CompactionComplete();
            }
            await inner.DisposeAsync().ConfigureAwait(false);
        }

        public Task<OriginalNativeShutdown> ReadOriginalShutdownAsync(
            NativeRecoveryOrigin origin,
            string evidencePath,
            CancellationToken cancellationToken
        ) =>
            throw new NotSupportedException("Original shutdown recovery belongs to frozen Phase5.");
    }
}
