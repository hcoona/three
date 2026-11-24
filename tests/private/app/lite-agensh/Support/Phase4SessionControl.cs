using System.Diagnostics;
using System.Runtime.ExceptionServices;
using System.Text.Json.Nodes;
using GitHub.Copilot;

namespace LiteAgensh.Tests.Support;

// Adds dependency-owned work/settlement transitions to the existing typed guard.
// No native client, transport, model or historical recovery is implemented here.
internal sealed class Phase4SessionControl : INativeSdkHost, IAsyncDisposable
{
    private static readonly TimeSpan HeldFactoryBound = TimeSpan.FromSeconds(5);
    private readonly TaskCompletionSource release = Signal();
    private OwnedChildControl? child;
    private Process? process;
    private ProcessRunner? processes;
    private bool settled;
    private CancellationTokenSource? heldFactoryIo;
    private Task<HeldOutcome>? heldFactoryOutcome;
    private bool heldFactoryObservedByCase;
    private bool heldFactoryWaitExpired;

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

    internal bool ReportSettlement { get; init; }

    internal OperationException? HostFailure { get; set; }

    internal Func<HostStopEntry>? ReadStopAtHostEntry { get; set; }

    internal HostStopEntry? StopAtHostEntry { get; private set; }

    internal bool FailExactOpen { get; set; }

    internal async Task StartOwnedRuntimeAsync(CancellationToken token)
    {
        child = new();
        process = child.StartDirect(role: OwnedChildControl.ControlledNativeHostRole);
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

    internal void ReleaseHost()
    {
        bool signalled = release.TrySetResult();
        ReportSettlementBoundary(signalled ? "release-signalled" : "release-already-signalled");
    }

    internal void BeginHeldFactoryCompletion()
    {
        if (heldFactoryIo is not null)
        {
            throw new InvalidOperationException("The held Phase4 factory already has an owner.");
        }
        heldFactoryIo = new();
        heldFactoryOutcome = ObserveOutcomeAsync(Factory.CompleteAsync());
    }

    internal async Task AwaitHeldFactoryAsync(CancellationToken token)
    {
        ReleaseHost();
        HeldOutcome outcome;
        try
        {
            outcome = await heldFactoryOutcome!
                .WaitAsync(HeldFactoryBound, token)
                .ConfigureAwait(false);
        }
        catch (TimeoutException bound)
        {
            heldFactoryWaitExpired = true;
            throw new TimeoutException(
                $"Timed out observing the original Phase4 factory for runtime {Id}.", bound
            );
        }
        heldFactoryObservedByCase = true;
        if (outcome.Error is { } error)
        {
            ExceptionDispatchInfo.Capture(error).Throw();
        }
    }

    public async ValueTask DisposeAsync()
    {
        // The factory invokes this boundary after its worker stop attempts.
        StopAtHostEntry ??= ReadStopAtHostEntry?.Invoke();
        HostEntered.TrySetResult();
        ReportSettlementBoundary("host-entered");
        if (HoldHost)
        {
            try
            {
                await release
                    .Task.WaitAsync(TimeSpan.FromSeconds(5), CancellationToken.None)
                    .ConfigureAwait(false);
            }
            catch (TimeoutException exception)
            {
                ReportSettlementBoundary("release-gate-expired", exception);
                throw;
            }
            ReportSettlementBoundary("release-gate-returned");
        }
        if (HostFailure is not null)
        {
            throw HostFailure;
        }
        if (!settled && child is not null)
        {
            using CancellationTokenSource exit = CancellationTokenSource.CreateLinkedTokenSource(
                heldFactoryIo?.Token ?? CancellationToken.None
            );
            exit.CancelAfter(HeldFactoryBound);
            await child
                .ReleaseAsync(exit.Token)
                .ConfigureAwait(false);
            await process!.WaitForExitAsync(exit.Token).ConfigureAwait(false);
            ReportSettlementBoundary("runtime-exit-observed");
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
        ReportSettlementBoundary("host-completed");
    }

    internal async Task CleanupAsync(bool preserveState = false)
    {
        ReleaseHost();
        await DisposeOwnedResourcesAsync(preserveState).ConfigureAwait(false);
    }

    internal async Task CleanupHeldFactoryAsync(
        Exception? primary,
        Action<string> report,
        Action<TaskStatus?>? captureSettlement = null,
        bool reportRootCleanup = false
    )
    {
        List<Exception> failures = [];
        if (!release.Task.IsCompletedSuccessfully)
        {
            Attempt(failures, ReleaseHost);
        }

        Task<HeldOutcome>? ioCancellation = null;
        TimeoutException? unresolved = null;
        string root = State.Cwd(string.Empty);
        if (heldFactoryOutcome is { } factory)
        {
            if (heldFactoryWaitExpired && !factory.IsCompleted)
            {
                ioCancellation = CancelHeldFactoryIo();
            }
            try
            {
                Task outcomes =
                    ioCancellation is null ? factory : Task.WhenAll(factory, ioCancellation);
                await outcomes.WaitAsync(HeldFactoryBound, CancellationToken.None)
                    .ConfigureAwait(false);
            }
            catch (TimeoutException bound)
            {
                unresolved = new(
                    $"Phase4 factory or I/O cancellation missed the cleanup bound "
                        + $"for runtime {Id}, lease {LeaseId}. "
                        + $"The full owned scope is retained (root: {root}) "
                        + "pending terminal cleanup.",
                    bound
                );
                failures.Add(unresolved);
                Task<HeldOutcome> stop = ioCancellation ?? CancelHeldFactoryIo();
                Attempt(failures, () => captureSettlement?.Invoke(null));
                // Transfer the entire fixture, not just its files. The deferred owner reports
                // terminal cleanup without using the case's TestContext after it unwinds.
                unresolved.Data["Phase4FactoryOutcome"] = factory;
                unresolved.Data["Phase4IoCancellationOutcome"] = stop;
                unresolved.Data["Phase4DeferredCleanup"] = FinishHeldScopeAsync(
                    stop, deferred: true);
            }
        }

        if (unresolved is null)
        {
            failures.AddRange(
                await FinishHeldScopeAsync(ioCancellation, captureSettlement).ConfigureAwait(false)
            );
            if (reportRootCleanup)
            {
                Attempt(failures, () =>
                    report($"Phase4ManagedCleanup: {root}; exists={Directory.Exists(root)}")
                );
            }
        }
        failures.RemoveAll(error => ReferenceEquals(primary, error));
        if (failures.Count == 0)
        {
            return;
        }
        if (primary is null)
        {
            if (failures.Count == 1)
            {
                ExceptionDispatchInfo.Capture(failures[0]).Throw();
            }
            throw new AggregateException("Phase4 fixture teardown failed.", failures);
        }
        foreach (Exception error in failures.ToArray())
        {
            Attempt(failures, () => report($"Secondary Phase4 teardown failure: {error}"));
        }
        primary.Data["Phase4CleanupFailures"] = failures.ToArray();
    }

    private Task<HeldOutcome> CancelHeldFactoryIo() =>
        ObserveOutcomeAsync(heldFactoryIo!.CancelAsync());

    private async Task<List<Exception>> FinishHeldScopeAsync(
        Task<HeldOutcome>? ioCancellation,
        Action<TaskStatus?>? captureSettlement = null,
        bool deferred = false
    )
    {
        // A terminal result racing a timeout must not make the handoff run disposal inline.
        HeldOutcome? outcome = heldFactoryOutcome is { } factory
            ? await factory.ConfigureAwait(
                deferred ? ConfigureAwaitOptions.ForceYielding : ConfigureAwaitOptions.None
            )
            : null;
        List<Exception> failures = [];
        if (!heldFactoryObservedByCase && outcome?.Error is { } factoryError)
        {
            failures.Add(factoryError);
        }
        if (
            ioCancellation is not null
            && (await ioCancellation.ConfigureAwait(false)).Error is { } cancellationError
        )
        {
            failures.Add(cancellationError);
        }
        Attempt(failures, () => captureSettlement?.Invoke(outcome?.Status));
        try
        {
            await DisposeOwnedResourcesAsync(preserveState: false).ConfigureAwait(false);
        }
        catch (Exception error)
        {
            failures.Add(error);
        }
        Attempt(failures, () => heldFactoryIo?.Dispose());

        if (deferred)
        {
            Attempt(failures, () =>
            {
                string root = State.Cwd(string.Empty);
                Console.Error.WriteLine(
                    $"PHASE4_DEFERRED_CLEANUP runtime={Id}; factoryStatus={outcome?.Status}; "
                        + $"root={root}; exists={Directory.Exists(root)}; failures={failures.Count}"
                );
                foreach (Exception error in failures)
                {
                    Console.Error.WriteLine(
                        $"PHASE4_DEFERRED_CLEANUP_FAILURE runtime={Id}; {error}");
                }
            });
        }
        return failures;
    }

    private static void Attempt(List<Exception> failures, Action action)
    {
        try
        {
            action();
        }
        catch (Exception error)
        {
            failures.Add(error);
        }
    }

    private static async Task<HeldOutcome> ObserveOutcomeAsync(Task operation)
    {
        try
        {
            await operation.ConfigureAwait(false);
            return new(operation.Status, null);
        }
        catch (Exception error)
        {
            return new(operation.Status, error);
        }
    }

    private readonly record struct HeldOutcome(TaskStatus Status, Exception? Error);

    private async Task DisposeOwnedResourcesAsync(bool preserveState)
    {
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

    private void ReportSettlementBoundary(string boundary, Exception? error = null)
    {
        if (!ReportSettlement)
        {
            return;
        }
        Console.WriteLine(
            "PHASE4_HOST_SETTLEMENT "
                + new JsonObject
                {
                    ["runtimeId"] = Id,
                    ["runtimeLeaseId"] = LeaseId,
                    ["pid"] = ReadyIdentity?.Pid,
                    ["boundary"] = boundary,
                    ["timestamp"] = Stopwatch.GetTimestamp(),
                    ["timestampFrequency"] = Stopwatch.Frequency,
                    ["releaseSignalled"] = release.Task.IsCompletedSuccessfully,
                    ["settled"] = settled,
                    ["error"] = error is null ? null : Json.ExceptionDetails(error),
                }.ToJsonString()
        );
    }

    internal static TaskCompletionSource Signal() =>
        new(TaskCreationOptions.RunContinuationsAsynchronously);

    internal sealed record HostStopEntry(
        bool SessionClosed,
        string? WorkerLifecycle,
        CreditReservation Reservation
    );

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
        private Task? abort;

        public string SessionId => inner.SessionId;

        public bool HasSession => inner.HasSession;

        public string? NativeFailure => FirstFailure?.Message;

        public Exception? FirstFailure => inner.FirstFailure ?? StopFailure;

        public JsonObject? Metadata { get; private set; }

        internal TypedHostControl.TypedSession Guard => inner;

        internal bool Closed { get; private set; }

        internal TaskCompletionSource AbortEntered { get; } = Signal();

        internal Task? AbortWait { get; set; }

        internal string? OpenedWorker => worker;

        internal string OpenedCwd => cwd;

        internal bool Isolated => isolated;

        internal string? ExactSession { get; private set; }

        internal string? AcceptedText { get; private set; }

        internal NativeUsage Current =>
            usage ?? throw new InvalidOperationException("Session not opened.");

        internal double TurnNanoAiu { get; set; } = 7_250_000_000;

        internal NativeUsage? UsageOnLimitUpdate { get; set; }

        internal OperationException? StopFailure { get; set; }

        internal bool HoldSend { get; set; }

        internal bool ChangeDuringRead { get; set; }

        internal bool ChangeOnClose { get; set; }

        internal TaskCompletionSource SendEntered { get; } = Signal();

        internal Func<long>? ReadDispatchAtSendEntry { get; set; }

        internal List<long> DispatchesAtSendEntry { get; } = [];

        internal TaskCompletionSource<bool> FenceEntered { get; private set; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        internal Task<bool> ArmFence() =>
            (FenceEntered = new(TaskCreationOptions.RunContinuationsAsynchronously)).Task;

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
            usage = UsageOnLimitUpdate ?? usage;
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
            bool abortCompletedAtEntry = abort?.IsCompletedSuccessfully == true;
            Task<NativeUsage> captured = fence.CaptureAsync(
                token => ReadAsync(expectedSession, expectedLimit, token, validateEvents),
                cancellationToken
            );
            // CaptureAsync has synchronously reached its read/wait boundary.
            // This signal is explicit dependency entry, not an incomplete-task oracle.
            FenceEntered.TrySetResult(abortCompletedAtEntry);
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
            if (ReadDispatchAtSendEntry is { } read)
            {
                DispatchesAtSendEntry.Add(read());
            }
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

        public Task AbortAsync(CancellationToken cancellationToken) =>
            abort = AbortCoreAsync(cancellationToken);

        private async Task AbortCoreAsync(CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (StopFailure is { } failure)
            {
                throw failure;
            }
            AbortEntered.TrySetResult();
            if (AbortWait is { } wait)
            {
                await wait.WaitAsync(cancellationToken).ConfigureAwait(false);
            }
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
