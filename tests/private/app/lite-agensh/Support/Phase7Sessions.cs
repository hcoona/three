using System.Diagnostics;
using System.Text.Json.Nodes;
using System.Threading.Channels;

namespace LiteAgensh.Tests.Support;

// Supported typed-session input/output control for Phase7. The coordinator,
// NativeWorker, ledger, dispatch fence, turn ACK and host settlement stay real.
// Scripted usage/terminal responses are application-contract evidence only.
internal sealed class Phase7Sessions(
    RunDatabase database,
    string? hostRole,
    Phase7Journal? journal = null,
    bool ownedHosts = true,
    Phase7Evidence? evidence = null
) : IAsyncDisposable
{
    private readonly Dictionary<string, Channel<Turn>> turns = new(StringComparer.Ordinal);
    private readonly object gate = new();
    internal List<Host> Hosts { get; } = [];
    internal Func<Session, CancellationToken, Task>? BeforeOpen { get; set; }
    internal Func<Session, CancellationToken, Task>? BeforeRead { get; set; }
    internal Phase7Journal? Journal { get; } = journal;
    internal Phase7Evidence? Evidence { get; } = evidence;

    private static void Require(bool condition, string message)
    {
        if (!condition)
        {
            throw new InvalidOperationException(message);
        }
    }

    internal Host CreateHost(string operation)
    {
        Host host = new(this, database, operation, hostRole, ownedHosts);
        Hosts.Add(host);
        Evidence?.Host("runtime-create", "prepared", host.Id, null, null, !ownedHosts);
        return host;
    }

    internal Task<Turn> NextTurnAsync(string worker, CancellationToken token) =>
        Queue(worker).Reader.ReadAsync(token).AsTask();

    private Channel<Turn> Queue(string worker)
    {
        lock (gate)
        {
            if (!turns.TryGetValue(worker, out Channel<Turn>? channel))
            {
                channel = Channel.CreateUnbounded<Turn>(
                    new UnboundedChannelOptions { SingleReader = true, SingleWriter = false }
                );
                turns.Add(worker, channel);
            }
            return channel;
        }
    }

    public async ValueTask DisposeAsync()
    {
        List<Exception> failures = [];
        foreach (Host host in Hosts)
        {
            try
            {
                await host.CleanupAsync().ConfigureAwait(false);
            }
            catch (Exception error)
            {
                failures.Add(error);
            }
        }
        if (failures.Count != 0)
        {
            throw new AggregateException("Phase7 owned-host cleanup failed.", failures);
        }
    }

    internal sealed class Turn(
        Session session,
        string text,
        TimeSpan timeout,
        CancellationToken token
    )
    {
        private readonly TaskCompletionSource<JsonObject> response = new(
            TaskCreationOptions.RunContinuationsAsynchronously
        );
        internal Session Session { get; } = session;
        internal string Text { get; } = text;
        internal TimeSpan Timeout { get; } = timeout;
        internal CancellationToken Token { get; } = token;
        internal Task<JsonObject> Response => response.Task;

        internal void CancelPending(CancellationToken token) => response.TrySetCanceled(token);

        internal void Complete(string stopReason = "end_turn") =>
            response.TrySetResult(
                new() { ["text"] = "Controlled completed turn.", ["stopReason"] = stopReason }
            );

        internal void Fail(Exception error) => response.TrySetException(error);
    }

    internal sealed class Host(
        Phase7Sessions control,
        RunDatabase database,
        string operation,
        string? role,
        bool owned
    ) : INativeSdkHost
    {
        private readonly Phase7Evidence? evidence = control.Evidence;
        private readonly OwnedChildControl? child = owned ? new() : null;
        private readonly SemaphoreSlim startup = new(1, 1);
        private Process? process;
        private bool normalExit;
        private bool managedReady;
        private bool managedSettled;
        private bool wrapperDisposed;
        private long? helperStarted;
        private int? observedExitCode;
        internal List<Session> Sessions { get; } = [];
        internal string Operation { get; } = operation;
        public string Id { get; } = Guid.NewGuid().ToString("N");
        public long LeaseId { get; private set; }
        internal OwnedChildIdentity? Ready { get; private set; }
        internal JsonObject Witness =>
            new()
            {
                ["runtimeId"] = Id,
                ["role"] = role ?? "default",
                ["helperAgeMilliseconds"] = helperStarted is { } began
                    ? Stopwatch.GetElapsedTime(began).TotalMilliseconds
                    : null,
                ["observedExitCode"] = observedExitCode ?? child?.ObservedExitCode,
                ["leaseId"] = LeaseId,
                ["readyPid"] = Ready?.Pid,
                ["readyBirthTicks"] = Ready?.BirthTicks,
                ["normalExitBeforeWrapperDispose"] = normalExit,
                ["managedSettlementBeforeWrapperDispose"] = managedSettled,
                ["hostKind"] = child is null ? "managed" : "owned-process",
                ["wrapperDisposed"] = wrapperDisposed,
                ["operation"] = Operation,
            };

        public INativeSdkSession CreateSession(string cwd, bool isolated)
        {
            Session session = new(control, database, this, Path.GetFullPath(cwd));
            Sessions.Add(session);
            return session;
        }

        internal async Task EnsureReadyAsync(CancellationToken token)
        {
            await startup.WaitAsync(token).ConfigureAwait(false);
            string phase = "runtime-ready";
            try
            {
                if (Ready is not null || managedReady)
                {
                    return;
                }
                if (child is null)
                {
                    evidence?.Host("runtime-ready", "started", Id, null, null, true);
                    managedReady = true;
                    Record("ready");
                    evidence?.Host("runtime-ready", "completed", Id, null, null, true);
                    return;
                }
                phase = "runtime-launch";
                evidence?.Host(phase, "started", Id, null, null, false);
                helperStarted = Stopwatch.GetTimestamp();
                process = child.StartDirect(role: role);
                evidence?.Process("runtime-launch", "completed", process.Id, null);
                evidence?.Host(phase, "completed", Id, null, null, false);
                phase = "runtime-registration";
                evidence?.Host(phase, "started", Id, null, null, false);
                LeaseId = new ProcessRunner(database).Register(process, $"native:runtime:{Id}");
                evidence?.Host(phase, "completed", Id, LeaseId, null, false);
                phase = "runtime-ready";
                evidence?.Host("runtime-ready", "started", Id, LeaseId, null, false);
                Ready = await child.ReadReadyAsync(token).ConfigureAwait(false);
                Require(process.Id == Ready.Pid, "READY PID differs from the owned process.");
                Require(
                    process.StartTime.ToUniversalTime().Ticks == Ready.BirthTicks,
                    "READY birth differs from the owned process."
                );
                Record("ready");
                evidence?.Host("runtime-ready", "completed", Id, LeaseId, Ready, false);
            }
            catch (Exception error)
            {
                evidence?.Host(
                    phase, "failed", Id, LeaseId, Ready, child is null, error: error
                );
                throw;
            }
            finally
            {
                startup.Release();
            }
        }

        private void Record(string state) =>
            database.WriteMetadata(
                $"native:runtime:{Id}",
                new JsonObject
                {
                    ["runtimeId"] = Id,
                    ["runtimeLeaseId"] = LeaseId,
                    ["runtimeVersion"] = Phase6NativeControl.RuntimeVersion,
                    ["state"] = state,
                    ["evidenceKind"] = child is null
                        ? "controlled-managed-dependency" : "controlled-native-dependency",
                }.ToJsonString()
            );

        public async ValueTask DisposeAsync()
        {
            if (child is null)
            {
                if (!managedSettled)
                {
                    evidence?.Host("shared-host-settlement", "started", Id, null, null, true);
                    managedSettled = true;
                    Record("settled");
                    Console.WriteLine("PHASE7_MANAGED_HOST_SETTLED " + Witness.ToJsonString());
                    evidence?.Host("shared-host-settlement", "completed", Id, null, null, true);
                }
                return;
            }
            if (normalExit || process is null)
            {
                return;
            }
            evidence?.Host("shared-host-settlement", "started", Id, LeaseId, Ready, false);
            using (CancellationTokenSource release = new(TimeSpan.FromSeconds(5)))
            {
                await child.ReleaseAsync(release.Token).ConfigureAwait(false);
            }
            using (CancellationTokenSource exit = new(TimeSpan.FromSeconds(5)))
            {
                await process.WaitForExitAsync(exit.Token).ConfigureAwait(false);
            }
            observedExitCode = process.ExitCode;
            Require(observedExitCode == 0, "Owned native host did not exit normally.");
            new ProcessRunner(database).Complete(LeaseId);
            normalExit = true;
            Record("settled");
            evidence?.Host(
                "shared-host-settlement", "joined", Id, LeaseId, Ready, false, observedExitCode
            );
            foreach (Session session in Sessions.Where(session => session.HasSession))
            {
                control.Journal?.NormalHostExit(session.SessionId, Id, Witness, database.Now);
            }
            Console.WriteLine("PHASE7_HOST_NORMAL_EXIT " + Witness.ToJsonString());
        }

        internal async Task CleanupAsync()
        {
            evidence?.Phase("host-wrapper-cleanup", "started");
            List<Exception> failures = [];
            bool childCleaned = child is null,
                startupDisposed = false;
            try
            {
                // Exceptional cleanup is NOT normal application settlement.
                if (child is not null && !normalExit
                    && process is not null && !process.HasExited && Ready is not null)
                {
                    using (CancellationTokenSource release = new(TimeSpan.FromSeconds(5)))
                    {
                        await child.ReleaseAsync(release.Token).ConfigureAwait(false);
                    }
                    using (CancellationTokenSource exit = new(TimeSpan.FromSeconds(5)))
                    {
                        await process.WaitForExitAsync(exit.Token).ConfigureAwait(false);
                    }
                    observedExitCode = process.ExitCode;
                }
            }
            catch (Exception error)
            {
                failures.Add(error);
            }
            try
            {
                if (child is not null)
                {
                    await child.DisposeAsync().ConfigureAwait(false);
                }
                childCleaned = true;
            }
            catch (Exception error)
            {
                failures.Add(error);
            }
            try
            {
                startup.Dispose();
                startupDisposed = true;
            }
            catch (Exception error)
            {
                failures.Add(error);
            }
            wrapperDisposed = failures.Count == 0;
            if (wrapperDisposed)
            {
                foreach (Session session in Sessions.Where(session => session.HasSession))
                {
                    control.Journal?.WrapperDisposed(session.SessionId, Id);
                }
            }
            JsonObject receipt = Witness;
            receipt["childCleanupAttempted"] = child is not null;
            receipt["childCleanupSucceeded"] = childCleaned;
            receipt["startupDisposeAttempted"] = true;
            receipt["startupDisposed"] = startupDisposed;
            receipt["failureCount"] = failures.Count;
            Console.WriteLine("PHASE7_HOST_CLEANUP " + receipt.ToJsonString());
            foreach (Exception error in failures)
            {
                evidence?.Host(
                    "host-wrapper-cleanup", "failed", Id, LeaseId, Ready, child is null,
                    observedExitCode, error
                );
            }
            if (failures.Count == 0)
            {
                evidence?.Phase("host-wrapper-cleanup", "completed");
            }
            if (failures.Count != 0)
            {
                throw new AggregateException("Phase7 host cleanup failed.", failures);
            }
        }
    }

    internal sealed class Session(
        Phase7Sessions control,
        RunDatabase database,
        Host host,
        string cwd
    ) : INativeSdkSession
    {
        private readonly Phase7Evidence? evidence = control.Evidence;
        private readonly NativeUsageFence fence = new();
        private NativeUsage? usage;
        private decimal cap;
        private bool disposed;
        public string SessionId { get; private set; } = "";
        public bool HasSession { get; private set; }
        public string? NativeFailure => null;
        public JsonObject? Metadata { get; private set; }
        internal string Worker { get; private set; } = "";
        internal string? ExactSession { get; private set; }
        internal string Operation => host.Operation;
        internal int CompletedTurns { get; private set; }
        internal bool Disposed => disposed;

        public async Task OpenAsync(
            string worker,
            string sessionId,
            decimal? limit,
            string? exactSession,
            string evidencePath,
            CancellationToken token
        )
        {
            Worker = worker;
            SessionId = sessionId;
            ExactSession = exactSession;
            evidence?.Session("session-open", "started", host.Id, sessionId, worker, database.Now);
            try
            {
                cap = limit ?? throw new InvalidOperationException(
                    "Phase7 requires finite backing."
                );
                await host.EnsureReadyAsync(token).ConfigureAwait(false);
                if (host.Operation is "run" or "resume" or "reconnect")
                {
                    if (control.BeforeOpen is { } before)
                    {
                        await before(this, token).ConfigureAwait(false);
                    }
                }
                token.ThrowIfCancellationRequested();
                if (exactSession is not null && control.Journal is null)
                {
                    // No synthetic persistent transcript or exact-load history here.
                    throw new InvalidOperationException(
                        "Exact-load history is not supplied by this fresh-session control."
                    );
                }
                Metadata = new()
                {
                    ["sessionId"] = sessionId,
                    ["workingDirectory"] = cwd,
                    ["selectedModel"] = database.Configuration.NativeModel,
                    ["alreadyInUse"] = false,
                    ["isRemote"] = false,
                    ["appliedLimit"] = NativeSdkSession.NativeLimit(cap),
                    ["runtimeId"] = host.Id,
                    ["runtimeLeaseId"] = host.LeaseId,
                    ["runtimeVersion"] = Phase6NativeControl.RuntimeVersion,
                    ["sdkVersion"] = NativeSdkSession.SdkVersion,
                };
                if (host.Ready is { } ready)
                {
                    Metadata["pid"] = ready.Pid;
                    Metadata["birthTicks"] = ready.BirthTicks;
                }
                (double nano, long requests) =
                    control.Journal?.Open(Metadata, worker, exactSession, database.Now) ?? (0d, 0L);
                usage = new(sessionId, nano, requests == 0 ? null : "complete", requests);
                CompletedTurns = checked((int)requests);
                HasSession = true;
                evidence?.Session(
                    "session-open", "completed", host.Id, sessionId, worker, database.Now
                );
            }
            catch (Exception error)
            {
                evidence?.Session(
                    "session-open", "failed", host.Id, sessionId, worker, database.Now, error
                );
                throw;
            }
        }

        public async Task<NativeUsage> ReadAsync(
            string expectedSession,
            decimal? expectedLimit,
            CancellationToken token,
            bool validateEvents = true
        )
        {
            evidence?.Session("usage-read", "started", host.Id, SessionId, Worker, database.Now);
            try
            {
                if (
                    host.Operation is "run" or "resume" or "reconnect"
                    && control.BeforeRead is { } before
                )
                {
                    await before(this, token).ConfigureAwait(false);
                }
                token.ThrowIfCancellationRequested();
                Require(expectedSession == SessionId, "Observed session identity differs.");
                Require(expectedLimit == cap, "Observed native cap differs.");
                NativeUsage current = usage
                    ?? throw new OperationException("native_probe_failed", "Session did not open.");
                evidence?.Session(
                    "usage-read", "completed", host.Id, SessionId, Worker, database.Now
                );
                return current;
            }
            catch (Exception error)
            {
                evidence?.Session(
                    "usage-read", "failed", host.Id, SessionId, Worker, database.Now, error
                );
                throw;
            }
        }

        public Task<NativeUsage> ReadSettledAsync(
            string expectedSession,
            decimal? expectedLimit,
            CancellationToken token,
            bool validateEvents = true,
            bool verifyLocalWork = false
        ) =>
            fence.CaptureAsync(
                next => ReadAsync(expectedSession, expectedLimit, next, validateEvents),
                token
            );

        public void WithSettledUsage(NativeUsage value, Action record) => fence.Use(value, record);

        public async Task<NativeUsage> UpdateLimitAsync(
            string expectedSession,
            decimal limit,
            CancellationToken token
        )
        {
            evidence?.Session("session-limit", "started", host.Id, SessionId, Worker, database.Now);
            try
            {
                token.ThrowIfCancellationRequested();
                cap = limit;
                Metadata!["appliedLimit"] = NativeSdkSession.NativeLimit(cap);
                NativeUsage current = await ReadAsync(expectedSession, limit, token)
                    .ConfigureAwait(false);
                evidence?.Session(
                    "session-limit", "completed", host.Id, SessionId, Worker, database.Now
                );
                return current;
            }
            catch (Exception error)
            {
                evidence?.Session(
                    "session-limit", "failed", host.Id, SessionId, Worker, database.Now, error
                );
                throw;
            }
        }

        public void MarkDispatchPending() => fence.SetTurnPending(true);

        public async Task<JsonObject> SendAsync(
            string text,
            TimeSpan timeout,
            CancellationToken token,
            Func<CancellationToken, Task<decimal>>? renewLimit = null,
            WorkAdmission? admission = null
        )
        {
            evidence?.Session("session-send", "started", host.Id, SessionId, Worker, database.Now);
            try
            {
                token.ThrowIfCancellationRequested();
                admission?.ThrowIfClosed();
                control.Journal?.Dispatch(SessionId, host.Id, text, database.Now);
                JsonObject response;
                if (Worker == "calibration")
                {
                    response = new() { ["text"] = "READY", ["stopReason"] = "end_turn" };
                }
                else
                {
                    Turn turn = new(this, text, timeout, token);
                    using CancellationTokenRegistration cancellation = token.Register(() =>
                        turn.CancelPending(token)
                    );
                    await control.Queue(Worker).Writer.WriteAsync(turn, token)
                        .ConfigureAwait(false);
                    response = await turn.Response.WaitAsync(token).ConfigureAwait(false);
                }
                CompletedTurns++;
                usage = new(
                    SessionId,
                    Worker == "calibration" ? 7_250_000_000d : CompletedTurns * 10_000_000_000d,
                    "complete",
                    CompletedTurns
                );
                control.Journal?.Complete(SessionId, host.Id, usage, response, database.Now);
                fence.SetTurnPending(false);
                evidence?.Session(
                    "session-send", "response-prepared", host.Id, SessionId, Worker, database.Now
                );
                return response;
            }
            catch (Exception error)
            {
                evidence?.Session(
                    "session-send", "failed", host.Id, SessionId, Worker, database.Now, error
                );
                throw;
            }
        }

        public Task AbortAsync(CancellationToken token)
        {
            token.ThrowIfCancellationRequested();
            evidence?.Session(
                "session-abort", "requested", host.Id, SessionId, Worker, database.Now
            );
            // An abort request alone is deliberately not terminal/usage proof.
            return Task.CompletedTask;
        }

        public void Quiesce() { }

        public void RequireHealthy() { }

        public ValueTask DisposeAsync()
        {
            disposed = true;
            evidence?.Session(
                "session-stop", "completed", host.Id, SessionId, Worker, database.Now
            );
            return ValueTask.CompletedTask;
        }

        public Task<OriginalNativeShutdown> ReadOriginalShutdownAsync(
            NativeRecoveryOrigin origin,
            string evidencePath,
            CancellationToken token
        ) =>
            control.Journal?.ReadOriginalAsync(origin, token)
            ?? throw new InvalidOperationException(
                "Phase7 fresh sessions cannot prove historical termination."
            );
    }
}
