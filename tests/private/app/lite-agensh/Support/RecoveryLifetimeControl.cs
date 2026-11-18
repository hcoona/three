using System.Diagnostics;
using System.Text.Json.Nodes;
using GitHub.Copilot;
using GitHub.Copilot.Rpc;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace LiteAgensh.Tests.Support;

// Original-lifetime arrangement only: public coordinator -> real worker/factory/
// ledger, plus the selected public typed dependency. Earlier helpers stay immutable.
internal sealed class RecoveryLifetimeControl : IAsyncDisposable
{
    private readonly GitScenarioState git;
    private readonly TaskCompletionSource peerStarted = Signal();
    private readonly TaskCompletionSource originalFailed = Signal();
    private readonly List<Host> hosts = [];
    private readonly string originalVersion;
    private readonly bool undelivered;
    private readonly Dictionary<string, string?> environment;
    private readonly CancellationToken originalLifetime;

    private RecoveryLifetimeControl(
        GitScenarioState git,
        bool shared,
        string version,
        bool undelivered,
        Dictionary<string, string?> environment,
        CancellationToken originalLifetime
    )
    {
        this.git = git;
        originalVersion = version;
        this.undelivered = undelivered;
        this.environment = environment;
        this.originalLifetime = originalLifetime;
        RunConfiguration configuration = git.Database.Configuration with
        {
            StatePath = git.PathFor("original-lifetime"),
            CampaignLedgerPath = git.PathFor("original-lifetime-campaign.sqlite"),
            Goal = "Recover original closed-window evidence without additional native work.",
            NativeModel = "controlled-model",
            CreditAllocation = shared ? "shared" : "per-session",
            // The real calibration spends 7.25; two original legacy allocations
            // consequently receive 300 each, not a seeded reservation amount.
            RunAiCreditAllowance = 607.25m,
            CampaignAiCreditAllowance = 607.25m,
        };
        Database = new(configuration.StatePath, configuration);
        Ledger = new(configuration.CampaignLedgerPath, configuration.CampaignAiCreditAllowance);
        CollaborationOperations collaboration = new(Database);
        originalFactory = new(Database, new(Database), Ledger, () => CreateHost(recovery: false));
        Coordinator = new(
            Database,
            collaboration,
            new(Database, collaboration),
            (_, _, _) => new DriverFactory(this)
        );
    }

    private readonly NativeWorkerFactory originalFactory;
    internal RunDatabase Database { get; }
    internal CreditLedger Ledger { get; }
    internal Coordinator Coordinator { get; }
    internal Host OriginalHost => hosts.Single(host => host.Original);
    internal Session Original =>
        OriginalHost.Sessions.Single(session => session.Worker == "worker1");
    internal CreditReservation Reservation =>
        Ledger.Reservations(Database.RunId).Single(row => row.Worker == "worker1");
    internal CreditReservation Peer =>
        Ledger.Reservations(Database.RunId).Single(row => row.Worker == "worker2");
    internal OperationException OriginalFailure { get; } =
        new(
            "original_native_work_cancelled",
            "Original native work did not deliver a settled terminal."
        );
    internal List<Host> RecoveryHosts { get; } = [];
    internal string RecoveryCase { get; set; } = "";
    internal string RecoveryVersion { get; set; } = Phase6NativeControl.RuntimeVersion;
    internal double FreshNano { get; set; } = 247_000_000_000d;
    internal ManualScenarioClock HistoryClock { get; } = new();
    internal string Cwd => Original.Cwd;
    internal string Root => git.PathFor("original-lifetime");

    internal static async Task<RecoveryLifetimeControl> CreateAsync(
        CancellationToken token,
        bool shared = false,
        string version = "1.0.107",
        bool undelivered = false
    )
    {
        using CancellationTokenSource lifetime =
            CancellationTokenSource.CreateLinkedTokenSource(token);
        lifetime.CancelAfter(TimeSpan.FromSeconds(120));
        token = lifetime.Token;
        // Test-host strings only, never inspect user/global/system file contents.
        string[] names =
        [
            "GIT_CONFIG_GLOBAL",
            "GIT_CONFIG_SYSTEM",
            "HOME",
            "USERPROFILE",
            "XDG_CONFIG_HOME",
        ];
        Dictionary<string, string?> environment = names.ToDictionary(
            name => name,
            Environment.GetEnvironmentVariable,
            StringComparer.Ordinal
        );
        GitScenarioState git = await GitScenarioState.CreateAsync(token).ConfigureAwait(false);
        RecoveryLifetimeControl state =
            new(git, shared, version, undelivered, environment, token);
        try
        {
            await state.Coordinator.InitializeAsync(token).ConfigureAwait(false);
            await state.Coordinator.PreflightAsync(token).ConfigureAwait(false);
            await state.Coordinator.CalibrateAsync(token).ConfigureAwait(false);
            JsonObject run = await state.Coordinator.RunAsync("run", token).ConfigureAwait(false);
            Console.WriteLine("PHASE5_ORIGINAL_RUN " + run.ToJsonString());
            return state;
        }
        catch (Exception primaryFailure)
        {
            await state.DisposeAsync(primaryFailure).ConfigureAwait(false);
            throw;
        }
    }

    private Host CreateHost(bool recovery)
    {
        using var connection = Database.Open();
        bool original =
            !recovery
            && (long)
                Sql.Scalar(
                    connection,
                    "SELECT COUNT(*) FROM coordinator_segments WHERE ended_at IS NULL;"
                )! == 1;
        Host host = new(
            this,
            original,
            recovery,
            original
                ? originalVersion
                : recovery
                    ? RecoveryVersion
                    : Phase6NativeControl.RuntimeVersion
        );
        hosts.Add(host);
        if (recovery)
        {
            RecoveryHosts.Add(host);
        }
        return host;
    }

    internal async Task<RecoveredNativeUsage> RecoverAsync(CancellationToken token)
    {
        NativeWorkerFactory factory = new(
            Database,
            new(Database),
            Ledger,
            () => CreateHost(recovery: true)
        );
        return await factory
            .ReadSettledUsageAsync(
                Reservation,
                Cwd,
                git.PathFor("recovery-" + Guid.NewGuid().ToString("N")),
                token
            )
            .ConfigureAwait(false);
    }

    internal void AssertOriginalLifetime()
    {
        Assert.AreEqual("uncertain", Reservation.State);
        Assert.AreEqual(300m, Reservation.Allowance);
        Assert.AreEqual(200m, Reservation.Actual);
        Assert.AreEqual("uncertain", Peer.State);
        Assert.AreEqual(
            undelivered ? 200_000_000_000d : 230_000_000_000d,
            Original.Current.TotalNanoAiu
        );
        Assert.AreEqual(3L, Original.Current.TotalUserRequests);
        Assert.IsGreaterThan(0L, OriginalHost.LeaseId);
        Assert.IsNotNull(OriginalHost.Ready);
        using var connection = Database.Open();
        Assert.AreEqual(
            1L,
            (long)
                Sql.Scalar(
                    connection,
                    """
                    SELECT COUNT(*) FROM native_sessions n
                    JOIN native_attempts a ON a.reservation_id=n.reservation_id
                        AND a.session_id=n.session_id AND a.created_at=n.created_at
                    JOIN process_leases p ON p.id=$lease
                    JOIN coordinator_segments s ON s.id=$segment
                    WHERE n.reservation_id=$id AND p.state='exited'
                        AND p.pid=$pid AND p.start_ticks=$birth
                        AND s.ended_at IS NOT NULL
                        AND n.created_at BETWEEN s.started_at AND s.ended_at
                        AND p.created_at BETWEEN s.started_at AND s.ended_at;
                    """,
                    null,
                    ("$lease", OriginalHost.LeaseId),
                    ("$segment", OriginalHost.CreationSegment),
                    ("$id", Reservation.Id),
                    ("$pid", OriginalHost.Ready!.Pid),
                    ("$birth", OriginalHost.Ready.BirthTicks)
                )!
        );
        Assert.AreEqual(Original.SessionId, Reservation.SessionId);
        Console.WriteLine(
            "PHASE5_ORIGINAL_IDENTITY "
                + System.Text.Json.JsonSerializer.Serialize(
                    new
                    {
                        runId = Database.RunId,
                        original = Reservation,
                        peer = Peer,
                        runtime = OriginalHost.Id,
                        lease = OriginalHost.LeaseId,
                        segment = OriginalHost.CreationSegment,
                        ready = OriginalHost.Ready,
                        current = Original.Current,
                        originalVersion,
                        metadata = Original.Metadata,
                        start = JsonNode.Parse(
                            (string)
                                Sql.Scalar(
                                    connection,
                                    "SELECT value FROM meta WHERE key=$key;",
                                    null,
                                    ("$key", NoDispatchCancellation.Marker(Reservation.Id))
                                )!
                        ),
                        root = Root,
                    }
                )
        );
    }

    internal JsonObject FinancialSnapshot =>
        new()
        {
            ["own"] = System.Text.Json.JsonSerializer.SerializeToNode(Reservation),
            ["peer"] = System.Text.Json.JsonSerializer.SerializeToNode(Peer),
            ["status"] = Ledger.Status(Database.RunId, Database.Configuration.RunAiCreditAllowance),
        };

    internal void DamageSavedObservation(string selection)
    {
        string key = selection is "missing-start" or "nonzero-origin" or "start-session"
            ? NoDispatchCancellation.Marker(Reservation.Id)
            : $"native:session:{Reservation.SessionId}";
        using var connection = Database.Open();
        if (selection == "missing-start")
        {
            Sql.Execute(connection, "DELETE FROM meta WHERE key=$key;", null, ("$key", key));
            return;
        }
        JsonObject observation = JsonNode
            .Parse(
                (string)
                    Sql.Scalar(
                        connection,
                        "SELECT value FROM meta WHERE key=$key;",
                        null,
                        ("$key", key)
                    )!
            )!
            .AsObject();
        switch (selection)
        {
            case "nonzero-origin":
                observation["initial"]!["reportedNanoAiu"] = "1";
                break;
            case "start-session":
                observation["sessionId"] = "other-original-session";
                break;
            case "lineage":
                observation["runtimeId"] = "other-original-runtime";
                break;
            case "lagging-cap":
                observation["appliedLimit"] = 200m;
                break;
            case "changed-sdk":
                observation["sdkVersion"] = "1.0.27";
                break;
            case "runtime-version":
                observation["runtimeVersion"] = "conflicting-original-runtime";
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(selection));
        }
        Database.WriteMetadata(key, observation.ToJsonString());
    }

    internal async Task RunBodyAsync(Func<Task> body)
    {
        Exception? primaryFailure = null;
        try
        {
            await body().ConfigureAwait(false);
        }
        catch (Exception exception)
        {
            primaryFailure = exception;
            throw;
        }
        finally
        {
            await DisposeAsync(primaryFailure).ConfigureAwait(false);
        }
    }

    public ValueTask DisposeAsync() => DisposeAsync(null);

    private async ValueTask DisposeAsync(Exception? primaryFailure)
    {
        List<Exception> cleanupFailures = [];
        try
        {
            ReportSqliteIncidents(cleanupFailures);
        }
        catch (Exception exception)
        {
            cleanupFailures.Add(exception);
        }
        foreach (Host host in hosts)
        {
            try
            {
                await host.CleanupAsync().ConfigureAwait(false);
            }
            catch (Exception exception)
            {
                cleanupFailures.Add(exception);
            }
        }
        try
        {
            git.Dispose();
        }
        catch (Exception exception)
        {
            cleanupFailures.Add(exception);
        }
        try
        {
            Console.WriteLine(
                "PHASE5_CLEANUP "
                    + System.Text.Json.JsonSerializer.Serialize(
                        new
                        {
                            root = Root,
                            rootAbsent = !Directory.Exists(Root),
                            capturedTestEnvironmentUnchanged = environment.All(pair =>
                                Environment.GetEnvironmentVariable(pair.Key) == pair.Value
                            ),
                            owned = hosts
                                .Select(host => host.CleanupWitness)
                                .Where(witness => witness is not null)
                                .ToArray(),
                        }
                    )
            );
        }
        catch (Exception exception)
        {
            cleanupFailures.Add(exception);
        }
        if (cleanupFailures.Count != 0)
        {
            // Preserve the actual primary object, without consulting framework outcome state.
            if (primaryFailure is not null)
            {
                primaryFailure.Data["Phase5CleanupFailures"] = cleanupFailures.ToArray();
            }
            try
            {
                Console.WriteLine(
                    "PHASE5_SECONDARY_CLEANUP_FAILURES "
                        + System.Text.Json.JsonSerializer.Serialize(
                            new
                            {
                                primary = primaryFailure?.ToString(),
                                secondary = cleanupFailures
                                    .Select(exception => exception.ToString())
                                    .ToArray(),
                            }
                        )
                );
            }
            catch (Exception loggingFailure)
            {
                cleanupFailures.Add(loggingFailure);
                if (primaryFailure is not null)
                {
                    primaryFailure.Data["Phase5CleanupFailures"] = cleanupFailures.ToArray();
                }
            }
            if (primaryFailure is null)
            {
                throw new AggregateException("Phase5 fixture cleanup failed.", cleanupFailures);
            }
        }
    }

    private void ReportSqliteIncidents(List<Exception> cleanupFailures)
    {
        string[] directories =
        [
            Path.Combine(Root, "run.sqlite.diagnostics"),
            git.PathFor("original-lifetime-campaign.sqlite.diagnostics"),
        ];
        foreach (string directory in directories)
        {
            if (!Directory.Exists(directory))
            {
                continue;
            }
            ReportSqliteIncidentFiles(
                Directory.EnumerateFiles(directory, "incident-*.json"),
                Console.Out,
                cleanupFailures
            );
        }
    }

    internal static void ReportSqliteIncidentFiles(
        IEnumerable<string> paths,
        TextWriter output,
        List<Exception> failures
    )
    {
        foreach (string path in paths)
        {
            try
            {
                JsonNode? incident = JsonNode.Parse(File.ReadAllText(path));
                output.WriteLine("PHASE5_SQLITE_INCIDENT " + incident!.ToJsonString());
            }
            catch (Exception exception)
            {
                failures.Add(exception);
            }
        }
    }

    internal static TaskCompletionSource Signal() =>
        new(TaskCreationOptions.RunContinuationsAsynchronously);

    private sealed class DriverFactory(RecoveryLifetimeControl state) : INativeWorkerFactory
    {
        public JsonArray SettlementFailures => state.originalFactory.SettlementFailures;
        public JsonArray SettlementDetails => state.originalFactory.SettlementDetails;

        public INativeWorker Create(CreditReservation? reservation, bool calibration)
        {
            INativeWorker real = state.originalFactory.Create(reservation, calibration);
            return calibration ? real : new Driver(state, real, reservation!.Worker);
        }

        public Task<JsonObject> PreflightAsync(
            IReadOnlyList<(string Worker, string Cwd)> peers,
            CancellationToken token
        ) => state.originalFactory.PreflightAsync(peers, token);

        public Task CompleteAsync() => state.originalFactory.CompleteAsync();

        public Task<RecoveredNativeUsage> ReadSettledUsageAsync(
            CreditReservation row,
            string cwd,
            string evidence,
            CancellationToken token
        ) => state.originalFactory.ReadSettledUsageAsync(row, cwd, evidence, token);
    }

    private sealed class Driver(RecoveryLifetimeControl state, INativeWorker real, string worker)
        : INativeWorker
    {
        public string? SessionId => real.SessionId;
        public string ReservationId => real.ReservationId;
        public JsonObject? Diagnostic => real.Diagnostic;
        public string? NativeFailure => real.NativeFailure;
        public Exception? FirstFailure => real.FirstFailure;

        public void BindOwner(CancellationToken token) => real.BindOwner(token);

        public NativeRequest BeginRequest(CancellationToken token) => real.BeginRequest(token);

        public async Task StartAsync(
            string cwd,
            string? exact,
            CancellationToken token,
            string? recoveryLabel = null,
            WorkAdmission? admission = null
        )
        {
            await real.StartAsync(cwd, exact, token, recoveryLabel, admission)
                .ConfigureAwait(false);
            if (worker == "worker2")
            {
                state.peerStarted.TrySetResult();
            }
        }

        public async Task<JsonObject> PromptAsync(
            string text,
            CancellationToken token,
            WorkAdmission? admission = null
        )
        {
            using CancellationTokenSource coordination =
                CancellationTokenSource.CreateLinkedTokenSource(token, state.originalLifetime);
            if (worker == "worker2")
            {
                await state
                    .originalFailed.Task.WaitAsync(coordination.Token)
                    .ConfigureAwait(false);
                token.ThrowIfCancellationRequested();
                throw state.OriginalFailure;
            }
            try
            {
                await state
                    .peerStarted.Task.WaitAsync(coordination.Token)
                    .ConfigureAwait(false);
                for (int turn = 0; turn < 3; turn++)
                {
                    await real.PromptAsync(text, token, admission).ConfigureAwait(false);
                    await real.VerifyCreditsAsync(token).ConfigureAwait(false);
                }
                if (state.undelivered)
                {
                    state.Original.FailNextSend = true;
                    return await real.PromptAsync(text, token, admission).ConfigureAwait(false);
                }
                state.Original.CancelOriginalCompaction();
                throw state.OriginalFailure;
            }
            finally
            {
                state.originalFailed.TrySetResult();
            }
        }

        public Task VerifyCreditsAsync(CancellationToken token) => real.VerifyCreditsAsync(token);

        public Task StopAsync() => real.StopAsync();

        public ValueTask DisposeAsync() => real.DisposeAsync();
    }

    internal sealed class Host(
        RecoveryLifetimeControl state,
        bool original,
        bool recovery,
        string version
    ) : INativeSdkHost
    {
        private OwnedChildControl? child;
        private Process? process;
        private bool exited;
        private ExitObservation? normalDisposeExit;
        private ExitObservation? fixtureCleanupExit;
        private bool wrapperCleanupCompleted;

        private sealed record ExitObservation(
            bool ProcessExited,
            int ExitCode,
            DateTimeOffset ObservedAt,
            string Boundary
        );

        public string Id { get; } = Guid.NewGuid().ToString("N");
        public long LeaseId { get; private set; }
        internal bool Original => original;
        internal bool Recovery => recovery;
        internal string Version => version;
        internal long CreationSegment { get; private set; }
        internal OwnedChildIdentity? Ready { get; private set; }
        internal object? CleanupWitness =>
            child is null
                ? null
                : new
                {
                    runtime = Id,
                    lease = LeaseId,
                    ready = Ready,
                    normalHostDisposeCompleted = exited,
                    leaseCompletedByNormalHostDispose = exited,
                    normalDisposeExit,
                    fixtureCleanupExit,
                    wrapperCleanupCompleted,
                };
        internal List<Session> Sessions { get; } = [];

        public INativeSdkSession CreateSession(string cwd, bool isolated)
        {
            Session session = new(state, this, Path.GetFullPath(cwd), isolated);
            Sessions.Add(session);
            return session;
        }

        internal async Task StartAsync(CancellationToken token)
        {
            if (child is not null || (!original && !recovery))
            {
                return;
            }
            if (original)
            {
                using var connection = state.Database.Open();
                CreationSegment = (long)
                    Sql.Scalar(
                        connection,
                        "SELECT id FROM coordinator_segments WHERE ended_at IS NULL;"
                    )!;
            }
            child = new();
            process = child.StartDirect(role: OwnedChildControl.ControlledNativeHostRole);
            LeaseId = new ProcessRunner(state.Database).Register(process, $"native:runtime:{Id}");
            Ready = await child.ReadReadyAsync(token).ConfigureAwait(false);
            Assert.AreEqual(process.Id, Ready.Pid);
            Assert.AreEqual(process.StartTime.ToUniversalTime().Ticks, Ready.BirthTicks);
            state.Database.WriteMetadata(
                $"native:runtime:{Id}",
                new JsonObject
                {
                    ["runtimeId"] = Id,
                    ["runtimeLeaseId"] = LeaseId,
                    ["runtimeVersion"] = version,
                    ["state"] = "ready",
                    ["evidenceKind"] = "controlled-native-dependency",
                }.ToJsonString()
            );
        }

        public async ValueTask DisposeAsync()
        {
            if (recovery && state.RecoveryCase == "host-stop")
            {
                throw new OperationException(
                    "recovery_host_stop_failed",
                    "The actual owned recovery host could not settle."
                );
            }
            if (recovery && state.RecoveryCase == "after-read")
            {
                Sessions.Single().ObserveAdditionalWork();
            }
            if (recovery && state.RecoveryCase == "background")
            {
                Sessions.Single().FailBackgroundCompaction();
            }
            if (child is not null && !exited)
            {
                await child.ReleaseAsync(CancellationToken.None).ConfigureAwait(false);
                await process!
                    .WaitForExitAsync(CancellationToken.None)
                    .WaitAsync(TimeSpan.FromSeconds(5))
                    .ConfigureAwait(false);
                normalDisposeExit = new(
                    process.HasExited,
                    process.ExitCode,
                    DateTimeOffset.UtcNow,
                    "normal-application-host-dispose-after-wait"
                );
                Assert.AreEqual(0, normalDisposeExit.ExitCode);
                new ProcessRunner(state.Database).Complete(LeaseId);
                exited = true;
            }
        }

        internal async Task CleanupAsync()
        {
            if (child is not null)
            {
                try
                {
                    if (normalDisposeExit is null)
                    {
                        if (!process!.HasExited)
                        {
                            await child.ReleaseAsync(CancellationToken.None).ConfigureAwait(false);
                        }
                        await process!
                            .WaitForExitAsync(CancellationToken.None)
                            .WaitAsync(TimeSpan.FromSeconds(5))
                            .ConfigureAwait(false);
                        fixtureCleanupExit = new(
                            process.HasExited,
                            process.ExitCode,
                            DateTimeOffset.UtcNow,
                            "exceptional-owned-fixture-cleanup-after-wait"
                        );
                    }
                }
                finally
                {
                    await child.DisposeAsync().ConfigureAwait(false);
                    wrapperCleanupCompleted = true;
                }
            }
        }
    }

    internal sealed class Session(
        RecoveryLifetimeControl state,
        Host host,
        string cwd,
        bool isolated
    ) : INativeSdkSession
    {
        private readonly NativeUsageFence fence = new();
        private decimal cap;
        private NativeUsage? current;
        private SessionStartEvent? opening;
        private bool disposed;
        public string SessionId { get; private set; } = "";
        public bool HasSession { get; private set; }
        public string? NativeFailure => FirstFailure?.Message;
        public Exception? FirstFailure { get; private set; }
        public JsonObject? Metadata { get; private set; }
        internal string Worker { get; private set; } = "";
        internal string Cwd => cwd;
        internal NativeUsage Current => current!;
        internal bool FailNextSend { get; set; }
        internal List<SessionEvent> History { get; } = [];
        internal List<string?> ReadCursors { get; } = [];
        internal List<CancellationToken> PageTokens { get; } = [];
        internal decimal? ExactCap { get; private set; }
        internal string? ExactSession { get; private set; }

        public async Task OpenAsync(
            string worker,
            string sessionId,
            decimal? limit,
            string? exact,
            string evidence,
            CancellationToken token
        )
        {
            token.ThrowIfCancellationRequested();
            await host.StartAsync(token).ConfigureAwait(false);
            Worker = worker;
            SessionId = sessionId;
            cap = limit!.Value;
            if (
                host.Recovery
                && cap < (decimal)state.Original.Current.TotalNanoAiu! / 1_000_000_000m
            )
            {
                throw new OperationException(
                    "native_limit_rejected",
                    "The absolute native cap is below already used credits."
                );
            }
            ExactCap = exact is null ? null : cap;
            ExactSession = exact;
            HasSession = true;
            current = new(
                sessionId,
                host.Recovery ? state.FreshNano : 0,
                host.Recovery
                    ? state.RecoveryCase == "partial"
                        ? "partial"
                        : "complete"
                    : null,
                host.Recovery
                    ? state.RecoveryCase == "requests-regress"
                        ? 2
                        : 4
                    : 0
            );
            DateTimeOffset startTime = DateTimeOffset.UtcNow;
            opening = new()
            {
                Id = Guid.NewGuid(),
                Timestamp = startTime,
                Data = new()
                {
                    Producer = "controlled-session-events",
                    Version = 1,
                    SessionId = sessionId,
                    CopilotVersion = host.Version,
                    AlreadyInUse = false,
                    StartTime = startTime,
                },
            };
            // Both timestamps originate at actual startup (not repaired later).
            History.Add(opening);
            state.Database.WriteMetadata(
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

        public Task<NativeUsage> ReadAsync(
            string sessionId,
            decimal? limit,
            CancellationToken token,
            bool validateEvents = true
        )
        {
            token.ThrowIfCancellationRequested();
            bool fresh = host.Recovery;
#pragma warning disable GHCP001
            SessionMetadataSnapshot snapshot = new()
            {
                SessionId = SessionId,
                WorkingDirectory = cwd,
                SelectedModel = "controlled-model",
                AlreadyInUse = false,
                IsRemote = false,
                SessionLimits = new()
                {
                    MaxAiCredits = NativeSdkSession.NativeLimit(
                        cap + (fresh && state.RecoveryCase == "live-cap" ? 1 : 0)
                    ),
                },
            };
            NativeSdkSession.ValidateMetadata(
                snapshot,
                SessionId,
                sessionId,
                cwd,
                limit,
                "controlled-model"
            );
#pragma warning restore GHCP001
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
                ["runtimeVersion"] =
                    fresh && state.RecoveryCase == "missing-live-version" ? "" : host.Version,
                ["sdkVersion"] = NativeSdkSession.SdkVersion,
            };
            return Task.FromResult(Current);
        }
#pragma warning disable GHCP001 // Selected public processing/activity contracts.
        public async Task<NativeUsage> ReadSettledAsync(
            string sessionId,
            decimal? limit,
            CancellationToken token,
            bool validateEvents = true,
            bool verifyLocalWork = false
        ) =>
            await fence
                .CaptureAsync(
                    next =>
                        verifyLocalWork
                            ? NativeSdkSession.ReadIdleRecoveryUsageAsync(
                                read => ReadAsync(sessionId, limit, read, validateEvents),
                                idle =>
                                {
                                    idle.ThrowIfCancellationRequested();
                                    Assert.IsTrue(isolated);
                                    NativeSdkSession.RequireIdleRecoveryWork(
                                        new MetadataIsProcessingResult
                                        {
                                            Processing = state.RecoveryCase == "processing",
                                        },
                                        new SessionActivity
                                        {
                                            HasActiveWork = false,
                                            Abortable = false,
                                        }
                                    );
                                    return Task.CompletedTask;
                                },
                                next
                            )
                            : ReadAsync(sessionId, limit, next, validateEvents),
                    token
                )
                .ConfigureAwait(false);
#pragma warning restore GHCP001
        public void WithSettledUsage(NativeUsage usage, Action record) => fence.Use(usage, record);

        public async Task<NativeUsage> UpdateLimitAsync(
            string sessionId,
            decimal limit,
            CancellationToken token
        )
        {
            if (limit < (decimal)(Current.TotalNanoAiu ?? 0) / 1_000_000_000m)
            {
                throw new OperationException(
                    "native_limit_rejected",
                    "The absolute native cap is below already used credits."
                );
            }
            cap = limit;
            return await ReadAsync(sessionId, limit, token).ConfigureAwait(false);
        }

        public void MarkDispatchPending() => fence.SetTurnPending(true);

        internal void ObserveAdditionalWork() =>
            fence.Observe(
                new AssistantUsageEvent
                {
                    Id = Guid.NewGuid(),
                    Timestamp = DateTimeOffset.UtcNow,
                    Data = new()
                    {
                        Model = "controlled-model",
                        Accounting = new()
                        {
                            SourceSessionId = SessionId,
                            Sequence = Current.TotalUserRequests + 1,
                            UsageId = Guid.NewGuid().ToString("N"),
                        },
                    },
                }
            );

        internal void FailBackgroundCompaction()
        {
            fence.Observe(
                new SessionCompactionStartEvent
                {
                    Id = Guid.NewGuid(),
                    Timestamp = DateTimeOffset.UtcNow,
                    Data = new() { CompactionAttemptId = "fresh-background" },
                }
            );
            fence.Observe(
                new SessionCompactionCompleteEvent
                {
                    Id = Guid.NewGuid(),
                    Timestamp = DateTimeOffset.UtcNow,
                    Data = new() { CompactionAttemptId = "fresh-background", Success = false },
                }
            );
            FirstFailure = new OperationException(
                "recovery_background_failed",
                "Fresh recovery background compaction failed."
            );
        }

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
            if (host.Recovery)
            {
                throw new InvalidOperationException("Exact recovery must never send.");
            }
            if (FailNextSend)
            {
                FirstFailure = state.OriginalFailure;
                throw state.OriginalFailure;
            }
            long requests = Current.TotalUserRequests + 1;
            double nano =
                Worker == "calibration" ? 7_250_000_000d
                : requests == 1 ? 100_000_000_000d
                : 200_000_000_000d;
            current = new(SessionId, nano, "complete", requests);
            History.Add(
                new AssistantUsageEvent
                {
                    Id = Guid.NewGuid(),
                    Timestamp = DateTimeOffset.UtcNow,
                    Data = new()
                    {
                        Model = "controlled-model",
                        Accounting = new()
                        {
                            Sequence = requests,
                            SourceSessionId = SessionId,
                            UsageId = Guid.NewGuid().ToString("N"),
                        },
                    },
                }
            );
            fence.SetTurnPending(false);
            return Task.FromResult(new JsonObject { ["text"] = "READY" });
        }

        internal void CancelOriginalCompaction()
        {
            fence.Observe(
                new SessionCompactionStartEvent
                {
                    Id = Guid.NewGuid(),
                    Timestamp = DateTimeOffset.UtcNow,
                    Data = new() { CompactionAttemptId = "original-compaction" },
                }
            );
            fence.Observe(
                new SessionCompactionCompleteEvent
                {
                    Id = Guid.NewGuid(),
                    Timestamp = DateTimeOffset.UtcNow,
                    Data = new() { CompactionAttemptId = "original-compaction", Success = false },
                }
            );
            // The cancelled compaction's final native total is not an eligible
            // settled reading. Original routine shutdown will independently
            // expose 230; the durable booked/prior amount remains 200.
            current = new(SessionId, 230_000_000_000d, "complete", Current.TotalUserRequests);
            FirstFailure = state.OriginalFailure;
        }

        public Task AbortAsync(CancellationToken token)
        {
            token.ThrowIfCancellationRequested();
            return Task.CompletedTask;
        }

        public void Quiesce() { }

        public void RequireHealthy()
        {
            if (FirstFailure is not null)
            {
                throw FirstFailure;
            }
        }
#pragma warning disable GHCP001 // Selected public original history/usage contract.
        public ValueTask DisposeAsync()
        {
            if (host.Recovery && state.RecoveryCase == "session-stop")
            {
                throw new OperationException(
                    "recovery_session_stop_failed",
                    "The actual recovery session could not settle."
                );
            }
            if (host.Original && Worker == "worker2")
            {
                throw new OperationException(
                    "original_peer_stop_failed",
                    "The unrelated original peer retains its own unresolved stop."
                );
            }
            if (!disposed)
            {
                History.Add(
                    new SessionShutdownEvent
                    {
                        Id = Guid.NewGuid(),
                        Timestamp = DateTimeOffset.UtcNow,
                        Data = new()
                        {
                            CodeChanges = new()
                            {
                                FilesModified = [],
                                LinesAdded = 0,
                                LinesRemoved = 0,
                            },
                            ModelMetrics = new Dictionary<string, ShutdownModelMetric>(),
                            TotalApiDuration = TimeSpan.Zero,
                            SessionStartTime = opening!.Data.StartTime.ToUnixTimeMilliseconds(),
                            ShutdownType = ShutdownType.Routine,
                            TotalNanoAiu = Current.TotalNanoAiu,
                            UsageAccountingWatermarks = new Dictionary<string, long>
                            {
                                [SessionId] = Current.TotalUserRequests,
                            },
                        },
                    }
                );
                disposed = true;
            }
            return ValueTask.CompletedTask;
        }

        public async Task<OriginalNativeShutdown> ReadOriginalShutdownAsync(
            NativeRecoveryOrigin origin,
            string evidence,
            CancellationToken token
        )
        {
            Console.WriteLine(
                "PHASE5_FACTORY_ORIGIN " + System.Text.Json.JsonSerializer.Serialize(origin)
            );
            OriginalNativeShutdown? result = null;
            // One supported probe budget surrounds ALL typed pages. This does not
            // execute the concrete NativeSdkSession host acquisition/file wrapper.
            await NativeSdkSession
                .ReadProbeAsync(
                    async budget =>
                    {
                        result = await NativeSdkSession
                            .ReadOriginalShutdownAsync(
                                origin,
                                (cursor, caller) =>
                                {
                                    caller.ThrowIfCancellationRequested();
                                    ReadCursors.Add(cursor);
                                    PageTokens.Add(caller);
                                    int index = cursor is null
                                        ? 0
                                        : int.Parse(
                                            cursor,
                                            System.Globalization.CultureInfo.InvariantCulture
                                        );
                                    SessionEvent[] history = state.Original.History.ToArray();
                                    SessionEvent update = history[index];
                                    // Alter one public page response only; original history remains
                                    // immutable.
                                    if (
                                        state.RecoveryCase == "missing-shutdown"
                                        && update is SessionShutdownEvent
                                    )
                                    {
                                        return Task.FromResult(
                                            new EventsReadResult
                                            {
                                                CursorStatus = EventsCursorStatus.Ok,
                                                Cursor = "end",
                                                Events = [],
                                                HasMore = false,
                                            }
                                        );
                                    }
                                    return Task.FromResult(
                                        new EventsReadResult
                                        {
                                            CursorStatus = EventsCursorStatus.Ok,
                                            Cursor = (index + 1).ToString(
                                                System.Globalization.CultureInfo.InvariantCulture
                                            ),
                                            Events = [update],
                                            HasMore = index + 1 < history.Length,
                                        }
                                    );
                                },
                                budget
                            )
                            .ConfigureAwait(false);
                        return new NativeUsage(SessionId, result.TotalNanoAiu, "complete", 3);
                    },
                    token,
                    () => "original history composition",
                    state.HistoryClock
                )
                .ConfigureAwait(false);
            return result!;
        }
#pragma warning restore GHCP001
    }
}
