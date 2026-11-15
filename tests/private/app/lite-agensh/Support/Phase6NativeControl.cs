using System.Diagnostics;
using System.Text.Json.Nodes;
using GitHub.Copilot;
using GitHub.Copilot.Rpc;

namespace LiteAgensh.Tests.Support;

// Controlled supported session boundary, shared only by Phase6 public consumers
// and their separate original-owner executable. Never native authorization.
internal sealed class Phase6NativeControl(RunDatabase database) : IAsyncDisposable
{
    internal List<Host> Hosts { get; } = [];
    internal bool MissingLimitCapability { get; set; }
    internal bool FailPaidStop { get; set; }

    internal INativeSdkHost CreateHost()
    {
        Host host = new(database, MissingLimitCapability, FailPaidStop);
        Hosts.Add(host);
        return host;
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
            throw new AggregateException("Phase6 owned runtime cleanup failed.", failures);
        }
    }

    internal sealed class Host(RunDatabase database, bool missingLimit, bool failPaidStop)
        : INativeSdkHost
    {
        private readonly OwnedChildControl child = new();
        private Process? process;
        private bool normalExit;
        private bool wrapperDisposed;
        private JsonObject? exit;

        public string Id { get; } = Guid.NewGuid().ToString("N");
        public long LeaseId { get; private set; }
        internal OwnedChildIdentity? Ready { get; private set; }
        internal List<Session> Sessions { get; } = [];
        internal JsonObject Witness =>
            new()
            {
                ["runtimeId"] = Id,
                ["leaseId"] = LeaseId,
                ["ready"] = System.Text.Json.JsonSerializer.SerializeToNode(Ready),
                ["normalHostSettlement"] = normalExit,
                ["exit"] = exit?.DeepClone(),
                ["wrapperDisposed"] = wrapperDisposed,
            };

        public INativeSdkSession CreateSession(string cwd, bool isolated)
        {
            Session session = new(
                database,
                this,
                Path.GetFullPath(cwd),
                missingLimit,
                failPaidStop
            );
            Sessions.Add(session);
            return session;
        }

        internal async Task StartAsync(CancellationToken token)
        {
            if (process is not null)
            {
                return;
            }
            process = child.StartDirect();
            LeaseId = new ProcessRunner(database).Register(process, $"native:runtime:{Id}");
            Ready = await child.ReadReadyAsync(token).ConfigureAwait(false);
            if (
                Ready.Pid != process.Id
                || Ready.BirthTicks != process.StartTime.ToUniversalTime().Ticks
            )
            {
                throw new InvalidOperationException("Owned runtime READY identity differs.");
            }
            Record("ready");
        }

        private void Record(string state) =>
            database.WriteMetadata(
                $"native:runtime:{Id}",
                new JsonObject
                {
                    ["runtimeId"] = Id,
                    ["runtimeLeaseId"] = LeaseId,
                    ["runtimeVersion"] = NativeSdkSession.RuntimeVersion,
                    ["state"] = state,
                    ["evidenceKind"] = "controlled-native-dependency",
                }.ToJsonString()
            );

        public async ValueTask DisposeAsync()
        {
            if (normalExit || process is null)
            {
                return;
            }
            await child.ReleaseAsync(CancellationToken.None).ConfigureAwait(false);
            await process
                .WaitForExitAsync(CancellationToken.None)
                .WaitAsync(TimeSpan.FromSeconds(5))
                .ConfigureAwait(false);
            exit = new()
            {
                ["pid"] = process.Id,
                ["exited"] = process.HasExited,
                ["exitCode"] = process.ExitCode,
                ["boundary"] = "normal-application-host-dispose-after-wait",
            };
            if (process.ExitCode != 0)
            {
                throw new InvalidOperationException("Owned runtime did not exit normally.");
            }
            new ProcessRunner(database).Complete(LeaseId);
            normalExit = true;
            Record("settled");
        }

        internal async Task CleanupAsync()
        {
            try
            {
                if (!normalExit && process is not null)
                {
                    if (!process.HasExited && Ready is not null)
                    {
                        await child.ReleaseAsync(CancellationToken.None).ConfigureAwait(false);
                    }
                    await process
                        .WaitForExitAsync(CancellationToken.None)
                        .WaitAsync(TimeSpan.FromSeconds(5))
                        .ConfigureAwait(false);
                    exit = new()
                    {
                        ["pid"] = process.Id,
                        ["exited"] = process.HasExited,
                        ["exitCode"] = process.ExitCode,
                        ["boundary"] = "exceptional-fixture-cleanup-not-normal-settlement",
                    };
                }
            }
            finally
            {
                // This owns only the exact directly launched process. Never query
                // the disposed Process wrapper to infer financial settlement.
                await child.DisposeAsync().ConfigureAwait(false);
                wrapperDisposed = true;
                Console.WriteLine("PHASE6_RUNTIME_CLEANUP " + Witness.ToJsonString());
            }
        }
    }

    internal sealed class Session(
        RunDatabase database,
        Host host,
        string cwd,
        bool missingLimit,
        bool failPaidStop
    ) : INativeSdkSession
    {
        private readonly NativeUsageFence fence = new();
        private decimal cap;
        private NativeUsage? current;
        public string SessionId { get; private set; } = "";
        public bool HasSession { get; private set; }
        public string? NativeFailure => null;
        public JsonObject? Metadata { get; private set; }
        internal string Worker { get; private set; } = "";
        internal NativeUsage? FinalOverride { get; set; }

        public async Task OpenAsync(
            string worker,
            string sessionId,
            decimal? limit,
            string? exactSession,
            string evidencePath,
            CancellationToken token
        )
        {
            token.ThrowIfCancellationRequested();
            if (exactSession is not null)
            {
                throw new InvalidOperationException(
                    "This fresh-session control cannot exact-load."
                );
            }
            await host.StartAsync(token).ConfigureAwait(false);
            Worker = worker;
            SessionId = sessionId;
            cap = limit ?? throw new InvalidOperationException("Expected a finite session cap.");
            current = new(sessionId, 0, null, 0);
            HasSession = true;
            database.WriteMetadata(
                $"native:runtime:{host.Id}:session:{sessionId}",
                new JsonObject
                {
                    ["runtimeId"] = host.Id,
                    ["runtimeLeaseId"] = host.LeaseId,
                    ["sessionId"] = sessionId,
                    ["worker"] = worker,
                    ["workingDirectory"] = cwd,
                }.ToJsonString()
            );
        }

#pragma warning disable GHCP001 // Existing supported typed metadata boundary.
        public Task<NativeUsage> ReadAsync(
            string expectedSession,
            decimal? expectedLimit,
            CancellationToken token,
            bool validateEvents = true
        )
        {
            token.ThrowIfCancellationRequested();
            NativeSdkSession.ValidateMetadata(
                new SessionMetadataSnapshot
                {
                    SessionId = SessionId,
                    WorkingDirectory = cwd,
                    SelectedModel = "controlled-model",
                    AlreadyInUse = false,
                    IsRemote = false,
                    SessionLimits = new() { MaxAiCredits = NativeSdkSession.NativeLimit(cap) },
                },
                SessionId,
                expectedSession,
                cwd,
                expectedLimit,
                "controlled-model"
            );
            Metadata = new()
            {
                ["sessionId"] = SessionId,
                ["workingDirectory"] = cwd,
                ["selectedModel"] = "controlled-model",
                ["alreadyInUse"] = false,
                ["isRemote"] = false,
                ["appliedLimit"] = NativeSdkSession.NativeLimit(cap),
                ["runtimeId"] = host.Id,
                ["runtimeLeaseId"] = host.LeaseId,
                ["runtimeVersion"] = NativeSdkSession.RuntimeVersion,
                ["sdkVersion"] = NativeSdkSession.SdkVersion,
            };
            return Task.FromResult(current!);
        }
#pragma warning restore GHCP001

        public Task<NativeUsage> ReadSettledAsync(
            string expectedSession,
            decimal? expectedLimit,
            CancellationToken token,
            bool validateEvents = true,
            bool verifyLocalWork = false
        ) =>
            fence.CaptureAsync(
                async next =>
                {
                    NativeUsage reading = await ReadAsync(
                            expectedSession,
                            expectedLimit,
                            next,
                            validateEvents
                        )
                        .ConfigureAwait(false);
                    return FinalOverride ?? reading;
                },
                token
            );

        public void WithSettledUsage(NativeUsage usage, Action record) => fence.Use(usage, record);

        public async Task<NativeUsage> UpdateLimitAsync(
            string expectedSession,
            decimal limit,
            CancellationToken token
        )
        {
            if (missingLimit)
            {
                throw new OperationException(
                    "native_probe_failed",
                    "Controlled session lacks the required limit-mutation capability."
                );
            }
            cap = limit;
            return await ReadAsync(expectedSession, limit, token).ConfigureAwait(false);
        }

        public void MarkDispatchPending() => fence.SetTurnPending(true);

        public Task<JsonObject> SendAsync(
            string text,
            TimeSpan timeout,
            CancellationToken token,
            Func<CancellationToken, Task<decimal>>? renewLimit = null,
            WorkAdmission? admission = null
        )
        {
            token.ThrowIfCancellationRequested();
            admission?.ThrowIfClosed();
            if (current!.TotalUserRequests != 0)
            {
                throw new InvalidOperationException(
                    "This bounded control already delivered its one turn."
                );
            }
            current = new(
                SessionId,
                Worker == "calibration" ? 7_250_000_000d : 80_000_000_000d,
                "complete",
                1
            );
            fence.SetTurnPending(false);
            return Task.FromResult(new JsonObject { ["text"] = "READY" });
        }

        public Task<OriginalNativeShutdown> ReadOriginalShutdownAsync(
            NativeRecoveryOrigin origin,
            string evidencePath,
            CancellationToken token
        ) =>
            throw new InvalidOperationException(
                "Fresh Phase6 control is not a native history reader."
            );

        public Task AbortAsync(CancellationToken token)
        {
            token.ThrowIfCancellationRequested();
            return Task.CompletedTask;
        }

        public void Quiesce() { }

        public void RequireHealthy() { }

        public ValueTask DisposeAsync() =>
            failPaidStop && Worker == "calibration"
                ? ValueTask.FromException(
                    new OperationException(
                        "phase6_stop_unsettled",
                        "Controlled paid session stop did not establish settlement."
                    )
                )
                : ValueTask.CompletedTask;
    }
}
