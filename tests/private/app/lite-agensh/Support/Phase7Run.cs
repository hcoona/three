using System.Globalization;
using System.Runtime.ExceptionServices;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Data.Sqlite;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace LiteAgensh.Tests.Support;

// Public coordinator arrangement. Git setup and typed native behavior are
// separate collaborators; this class does not implement product control logic.
internal sealed class Phase7Run
{
    private static readonly string[] GitEnvironment =
    [
        "GIT_CONFIG_GLOBAL",
        "GIT_CONFIG_SYSTEM",
        "GIT_CONFIG_NOSYSTEM",
        "HOME",
        "USERPROFILE",
        "XDG_CONFIG_HOME",
        "GIT_CONFIG",
        "GIT_CONFIG_COUNT",
        "GIT_CONFIG_PARAMETERS",
        "GIT_DIR",
        "GIT_COMMON_DIR",
        "GIT_WORK_TREE",
        "GIT_INDEX_FILE",
        "GIT_OBJECT_DIRECTORY",
        "GIT_ALTERNATE_OBJECT_DIRECTORIES",
        "GIT_NAMESPACE",
        "GIT_CEILING_DIRECTORIES",
    ];
    private readonly Dictionary<string, string?> environment;
    private readonly CancellationTokenSource owner;
    private Task<JsonObject>? active;
    private IDisposable? clockObservation;
    private Phase7Handoff? currentHandoff;
    private TaskCompletionSource<long> ownerPolling = new(
        TaskCreationOptions.RunContinuationsAsynchronously
    );
    private int ownerPollWorkers;

    private Phase7Run(
        GitScenarioState git,
        RunDatabase database,
        Dictionary<string, string?> environment,
        string? hostRole,
        Phase7Clock clock,
        Phase7Journal? journal,
        CancellationToken token,
        bool ownedHosts = true
    )
    {
        Git = git;
        this.environment = environment;
        Clock = clock;
        Database = database;
        Ledger = new(
            Database.Configuration.CampaignLedgerPath,
            Database.Configuration.CampaignAiCreditAllowance
        );
        Collaboration = new(Database);
        Operations = new(Database, Collaboration);
        Sessions = new(Database, hostRole, journal, ownedHosts);
        // No partially constructed fixture can strand this registration.
        owner = CancellationTokenSource.CreateLinkedTokenSource(token);
    }

    internal GitScenarioState Git { get; }
    internal Phase7Clock Clock { get; }
    internal Func<Task>? BeforeGitCleanup { get; private set; }
    internal RunDatabase Database { get; }
    internal CreditLedger Ledger { get; }
    internal CollaborationOperations Collaboration { get; }
    internal GitOperations Operations { get; }
    internal Phase7Sessions Sessions { get; }
    internal TimeProvider? SettlementTimeProvider { get; set; }
    internal Phase7Exhaustion? Exhaustion { get; set; }
    internal Phase7Handoff? Handoff { get; set; }
    internal Action<IReadOnlyList<Task>>? ObserveWorkerCompletion { get; set; }
    internal Task<JsonObject> Active =>
        active ?? throw new InvalidOperationException("Run has not started.");
    internal DateTimeOffset WorkDeadline =>
        Clock.WorkDeadline ?? throw new InvalidOperationException("Run has no work deadline.");
    internal DateTimeOffset DrainDeadline => WorkDeadline.AddSeconds(30);

    internal static async Task<Phase7Run> CreateAsync(
        CancellationToken token,
        int workers = 2,
        int interval = 0,
        int planning = 0,
        int duration = 1800,
        string allocation = "per-session",
        string? hostRole = OwnedChildControl.ControlledNativeHostRole,
        decimal allowance = 207.25m,
        bool ownedHosts = true
    )
    {
        Dictionary<string, string?> environment = GitEnvironment.ToDictionary(
            name => name,
            Environment.GetEnvironmentVariable,
            StringComparer.Ordinal
        );
        GitScenarioState git = await GitScenarioState.CreateAsync(token).ConfigureAwait(false);
        Phase7Run? state = null;
        try
        {
            RunConfiguration configuration = git.Database.Configuration with
            {
                StatePath = git.PathFor("phase7-run"),
                CampaignLedgerPath = git.PathFor("phase7-campaign.sqlite"),
                NativeModel = "controlled-model",
                WorkerCount = workers,
                WorkerStartIntervalSeconds = interval,
                PlanningSeconds = planning,
                DurationSeconds = duration,
                CreditAllocation = allocation,
                RunAiCreditAllowance = allowance,
                CampaignAiCreditAllowance = 407.25m,
            };
            Phase7Clock clock = new();
            RunDatabase initialized = new(configuration.StatePath, configuration, clock);
            state = new(git, initialized, environment, hostRole, clock, null, token, ownedHosts);
            GitScenarioState.Data(
                await state.Coordinator("initialize").InitializeAsync(token).ConfigureAwait(false)
            );
            GitScenarioState.Data(
                await state.Coordinator("preflight").PreflightAsync(token).ConfigureAwait(false)
            );
            GitScenarioState.Data(
                await state.Coordinator("calibration").CalibrateAsync(token).ConfigureAwait(false)
            );
            Assert.AreEqual(
                7.25m,
                state
                    .Ledger.Reservations(state.Database.RunId)
                    .Single(row => row.Worker == "calibration")
                    .Actual
            );
            return state;
        }
        catch (Exception primary)
        {
            if (state is not null)
            {
                await state.CleanupAsync(primary).ConfigureAwait(false);
            }
            else
            {
                GitScenarioState.AssertEnvironmentUnchanged();
                Console.Error.WriteLine("PHASE7_SETUP_PRIMARY " + Json.ExceptionDetails(primary));
                Console.WriteLine("PHASE7_PRESERVED_SETUP_ROOT " + git.Repository);
            }
            throw;
        }
    }

    // This route deliberately performs no Initialize/Preflight/Calibrate. The
    // actual creator's configuration, campaign and advanced epoch are retained.
    internal static Phase7Run Attach(Phase7OwnedCreator creator, CancellationToken token)
    {
        Phase7Clock restoredClock = new(creator.Checkpoint["clock"]!.GetValue<DateTimeOffset>());
        // Explicit existing-state role. Supplying configuration here means init,
        // not validation, and must never be used to reopen the creator's state.
        RunDatabase reopened = new(creator.Configuration.StatePath, timeProvider: restoredClock);
        Assert.AreEqual(creator.Checkpoint["runId"]!.GetValue<string>(), reopened.RunId);
        // Public Git initialization resolves the supplied base revision to the
        // genuine checkout commit. Compare against that observed identity, not
        // against the pre-initialization spelling of the revision.
        RunConfiguration expected = creator.Configuration with
        {
            BaseRevision = creator.Git.BaseCommit,
        };
        Assert.IsTrue(
            JsonNode.DeepEquals(
                JsonSerializer.SerializeToNode(expected, Json.Options),
                JsonSerializer.SerializeToNode(reopened.Configuration, Json.Options)
            ),
            "The persisted creator configuration differs; it must not be rewritten."
        );
        Console.WriteLine(
            "PHASE7_REOPENED_ORIGINAL "
                + new JsonObject
                {
                    ["runId"] = reopened.RunId,
                    ["statePath"] = reopened.StatePath,
                    ["campaignPath"] = reopened.Configuration.CampaignLedgerPath,
                    ["clock"] = restoredClock.GetUtcNow(),
                    ["originalRuntimeId"] = creator.Checkpoint["runtimeId"]!.DeepClone(),
                    ["configurationValidatedWithoutRewrite"] = true,
                }.ToJsonString()
        );
        Phase7Journal journal = new(creator.JournalPath)
        {
            PinnedRuntime = creator.Checkpoint["runtimeId"]!.GetValue<string>(),
        };
        return new(
            creator.Git,
            reopened,
            creator.Environment,
            OwnedChildControl.ControlledNativeHostRole,
            restoredClock,
            journal,
            token
        )
        {
            BeforeGitCleanup = () => creator.DisposeAsync().AsTask(),
        };
    }

    internal Coordinator Coordinator(string operation = "run") =>
        new(
            Database, Collaboration, Operations, (_, _, _) => new Factory(this, operation),
            loops =>
            {
                if (loops.Count == ownerPollWorkers)
                {
                    ownerPolling.TrySetResult(Clock.TimerWatermark);
                }
                ObserveWorkerCompletion?.Invoke(loops);
            }
        );

    internal Task<JsonObject> Start(string mode = "run", int? ownerPollWorkers = null)
    {
        currentHandoff?.Dispose();
        if (ReferenceEquals(Handoff, currentHandoff))
        {
            Handoff = null;
        }
        Handoff ??= new();
        currentHandoff = Handoff;
        clockObservation?.Dispose();
        clockObservation = Clock.BeginObservation();
        ownerPolling = new(TaskCreationOptions.RunContinuationsAsynchronously);
        this.ownerPollWorkers = ownerPollWorkers ?? Database.Configuration.WorkerCount;
        Clock.WorkDeadline =
            mode == "reconnect"
                ? DateTimeOffset.Parse(
                    (string)
                        Value(
                            (
                                "SELECT deadline FROM coordinator_segments WHERE ended_at IS "
                                + "NULL ORDER BY id DESC LIMIT 1;"
                            )
                        )!,
                    CultureInfo.InvariantCulture
                )
                : Clock.GetUtcNow().AddSeconds(Database.Configuration.DurationSeconds);
        active = Coordinator(mode).RunAsync(mode, owner.Token);
        return active;
    }

    internal void CancelCaller() => owner.Cancel();

    internal Task UntilAsync(
        Func<bool> condition,
        DateTimeOffset strictlyBefore,
        CancellationToken token
    ) => Clock.UntilAsync(condition, Active, strictlyBefore, token);

    internal Task UntilAsync(
        Task goal,
        DateTimeOffset strictlyBefore,
        CancellationToken token
    ) => Clock.UntilAsync(goal, Active, strictlyBefore, token);

    // The scenario owns only its channel/entry observation, never the business
    // token. An ended subject cancels and joins that observation before return.
    internal async Task<T> ReadyAsync<T>(
        Func<CancellationToken, Task<T>> observe,
        DateTimeOffset strictlyBefore,
        CancellationToken token
    ) =>
        await ObserveAsync(
                observe,
                (goal, next) => Clock.UntilAsync(goal, Active, strictlyBefore, next),
                token
            )
            .ConfigureAwait(false);

    // Accounting retries and native/Git setup use real cancellable operations,
    // not this clock. Observe those tasks without consuming work-admission time.
    internal async Task<T> FixedAsync<T>(
        Func<CancellationToken, Task<T>> observe,
        CancellationToken token
    ) =>
        await ObserveAsync(
                observe,
                async (goal, next) =>
                {
                    Task winner = await Task.WhenAny(goal, Active)
                        .WaitAsync(next)
                        .ConfigureAwait(false);
                    if (winner == Active && !goal.IsCompleted)
                    {
                        Assert.Fail(
                            "Run ended before fixed-clock readiness: "
                                + (await Active.ConfigureAwait(false)).ToJsonString()
                        );
                    }
                },
                token
            )
            .ConfigureAwait(false);

    // Keep the actual operation (not merely an async success observation) in
    // the race. If subject completion coincides with a startup fault/cancel,
    // awaiting this exact task preserves the real operation's failure first.
    internal Task FixedAsync(Task operation, CancellationToken token) =>
        ObserveAsync(
            async next =>
            {
                await operation.WaitAsync(next).ConfigureAwait(false);
                return true;
            },
            async (_, next) =>
            {
                await Task.WhenAny(operation, Active).WaitAsync(next).ConfigureAwait(false);
                if (operation.IsCompleted)
                {
                    await operation.ConfigureAwait(false);
                }
                else
                {
                    Assert.Fail(
                        "Run ended before fixed-clock operation: "
                            + (await Active.ConfigureAwait(false)).ToJsonString()
                    );
                }
            },
            token
        );

    private static async Task<T> ObserveAsync<T>(
        Func<CancellationToken, Task<T>> observe,
        Func<Task<T>, CancellationToken, Task> wait,
        CancellationToken token
    )
    {
        using CancellationTokenSource observation = CancellationTokenSource.CreateLinkedTokenSource(
            token
        );
        Task<T> goal = observe(observation.Token);
        T result = default!;
        Exception? primary = null;
        try
        {
            await wait(goal, observation.Token).ConfigureAwait(false);
            result = await goal.ConfigureAwait(false);
        }
        catch (Exception error)
        {
            primary = error;
        }
        List<Exception> secondary = [];
        try
        {
            observation.Cancel();
        }
        catch (Exception error)
        {
            secondary.Add(error);
        }
        try
        {
            await goal.ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (observation.IsCancellationRequested) { }
        catch (Exception error)
        {
            if (!ReferenceEquals(error, primary))
            {
                secondary.Add(error);
            }
        }
        foreach (Exception error in secondary)
        {
            Console.Error.WriteLine("PHASE7_READINESS_SECONDARY " + Json.ExceptionDetails(error));
        }
        if (primary is not null)
        {
            ExceptionDispatchInfo.Capture(primary).Throw();
        }
        if (secondary.Count != 0)
        {
            throw new AggregateException("Readiness observation cleanup failed.", secondary);
        }
        return result;
    }

    internal async Task StepAsync(DateTimeOffset strictlyBefore, CancellationToken token)
    {
        using CancellationTokenSource observation = CancellationTokenSource.CreateLinkedTokenSource(
            token
        );
        Task step = Clock.TickAsync(observation.Token, strictlyBefore, () => Active.IsCompleted);
        try
        {
            if (await Task.WhenAny(step, Active).WaitAsync(token).ConfigureAwait(false) == Active)
            {
                JsonObject result = await Active.ConfigureAwait(false);
                Assert.Fail(
                    "Run ended before the requested progress step: " + result.ToJsonString()
                );
            }
            await step.ConfigureAwait(false);
        }
        finally
        {
            observation.Cancel();
            try
            {
                await step.ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (observation.IsCancellationRequested) { }
        }
    }

    // Exclude startup waits using the actual admitted-cohort main-loop milestone.
    // Hold responses while observing its next poll, then freeze time through ACK.
    internal Task<Phase7Clock.ObservedPoll> ObserveOwnerPollAsync(CancellationToken token) =>
        FixedAsync(
            async next =>
            {
                long registeredAfter = await ownerPolling.Task
                    .WaitAsync(next).ConfigureAwait(false);
                return await Clock
                    .WaitForPollAsync(
                        next, WorkDeadline, requireSingle: true, registeredAfter: registeredAfter
                    )
                    .ConfigureAwait(false);
            },
            token
        );

    internal async Task<JsonObject> StopRunningAsync(
        string action,
        Phase7Clock.ObservedPoll poll,
        CancellationToken token
    )
    {
        JsonObject request = GitScenarioState.Data(Coordinator().RequestControl(action));
        Assert.AreEqual("pending", request["state"]!.GetValue<string>());
        Clock.Advance(poll, WorkDeadline, token);
        await FixedAsync(Handoff!.OwnerStopped.Task, token).ConfigureAwait(false);
        return await Active.WaitAsync(token).ConfigureAwait(false);
    }

    internal JsonObject Invoke(string actor, string operation, JsonObject arguments) =>
        GitScenarioState.Data(Collaboration.Invoke(actor, operation, arguments));

    internal async Task<JsonObject> GitToolAsync(
        string actor,
        string operation,
        JsonObject arguments,
        CancellationToken token
    ) =>
        GitScenarioState.Data(
            await Operations.InvokeAsync(actor, operation, arguments, token).ConfigureAwait(false)
        );

    internal object? Value(string statement, params (string Name, object? Value)[] parameters)
    {
        using SqliteConnection connection = new(
            new SqliteConnectionStringBuilder
            {
                DataSource = Path.Combine(Database.StatePath, "run.sqlite"),
                Mode = SqliteOpenMode.ReadOnly,
                Pooling = false,
                DefaultTimeout = 5,
            }.ToString()
        );
        connection.Open();
        return Sql.Scalar(connection, statement, null, parameters);
    }

    internal long Number(string statement, params (string Name, object? Value)[] parameters) =>
        Convert.ToInt64(Value(statement, parameters), CultureInfo.InvariantCulture);

    internal void Execute(string statement)
    {
        using SqliteConnection connection = Database.Open();
        Sql.Execute(connection, statement);
    }

    internal CreditReservation Reservation(string worker) =>
        Ledger.Reservations(Database.RunId).Last(row => row.Worker == worker);

    internal long Dispatches(string reservationId)
    {
        JsonObject status = Ledger.Status(
            Database.RunId,
            Database.Configuration.RunAiCreditAllowance
        );
        JsonArray reported = Assert.IsInstanceOfType<JsonArray>(status["reported"]);
        JsonObject entry = reported
            .Select(item => Assert.IsInstanceOfType<JsonObject>(item))
            .Single(item => item["reservationId"]!.GetValue<string>() == reservationId);
        return entry["dispatches"]!.GetValue<long>();
    }

    internal string WorkerPath(string worker) =>
        Database.ReadMetadata($"worktree:{worker}")
        ?? throw new InvalidOperationException("Worker worktree not prepared.");

    internal long SendingBatch(string worker) =>
        Number(
            "SELECT id FROM delivery_batches WHERE recipient=$worker AND state='sending';",
            ("$worker", worker)
        );

    internal Task WaitAckAsync(
        long batch,
        DateTimeOffset strictlyBefore,
        CancellationToken token
    ) => WaitDurableAsync(
        () =>
            (string?)Value("SELECT state FROM delivery_batches WHERE id=$id;", ("$id", batch))
            == "sent",
        strictlyBefore,
        token
    );

    // Observe real storage without spending business time; persistence does not
    // certify completion of the operation that wrote it or financial settlement.
    internal Task WaitDurableAsync(
        Func<bool> goal,
        DateTimeOffset strictlyBefore,
        CancellationToken token
    ) =>
        ObserveAsync(
            async next =>
            {
                next.ThrowIfCancellationRequested();
                Assert.IsLessThan(strictlyBefore, Clock.GetUtcNow());
                while (!goal())
                {
                    await Task.Delay(10, next).ConfigureAwait(false);
                }
                next.ThrowIfCancellationRequested();
                Assert.IsLessThan(strictlyBefore, Clock.GetUtcNow());
                return true;
            },
            async (observation, next) =>
            {
                await Task.WhenAny(observation, Active).WaitAsync(next).ConfigureAwait(false);
                if (Active.IsCompleted)
                {
                    await Active.ConfigureAwait(false);
                    if (!goal())
                    {
                        throw new InvalidOperationException(
                            "Subject ended before the required durable goal."
                        );
                    }
                }
            },
            token
        );

    internal async Task RunBodyAsync(Func<Task> body)
    {
        Exception? primary = null;
        try
        {
            await body().ConfigureAwait(false);
        }
        catch (Exception error)
        {
            primary = error;
            Console.Error.WriteLine("PHASE7_PRIMARY " + Json.ExceptionDetails(error));
            throw;
        }
        finally
        {
            await CleanupAsync(primary).ConfigureAwait(false);
        }
    }

    private async Task CleanupAsync(Exception? primary)
    {
        List<Exception> secondary = [];
        try
        {
            if (active is { IsCompleted: false })
            {
                owner.Cancel();
                try
                {
                    await active.ConfigureAwait(false);
                }
                catch (Exception error)
                {
                    secondary.Add(error);
                }
            }
        }
        catch (Exception error)
        {
            secondary.Add(error);
        }
        try
        {
            await Sessions.DisposeAsync().ConfigureAwait(false);
        }
        catch (Exception error)
        {
            secondary.Add(error);
        }
        try
        {
            string[] directories =
            [
                Path.Combine(Database.StatePath, "run.sqlite.diagnostics"),
                Database.Configuration.CampaignLedgerPath + ".diagnostics",
            ];
            SqliteIncidentOutput.Report(directories, Console.Out, secondary);
        }
        catch (Exception error)
        {
            secondary.Add(error);
        }
        if (BeforeGitCleanup is { } cleanup)
        {
            try
            {
                await cleanup().ConfigureAwait(false);
            }
            catch (Exception error)
            {
                secondary.Add(error);
            }
        }
        try
        {
            if (primary is null && secondary.Count == 0)
            {
                Git.Dispose();
            }
            else
            {
                GitScenarioState.AssertEnvironmentUnchanged();
                Console.WriteLine("PHASE7_PRESERVED_ROOT " + Database.StatePath);
            }
            foreach ((string name, string? expected) in environment)
            {
                Assert.AreEqual(expected, Environment.GetEnvironmentVariable(name), name);
            }
            Console.WriteLine("PHASE7_INHERITED_ENVIRONMENT_UNCHANGED " + environment.Count);
        }
        catch (Exception error)
        {
            secondary.Add(error);
        }
        finally
        {
            currentHandoff?.Dispose();
            clockObservation?.Dispose();
            owner.Dispose();
            foreach (Exception error in secondary)
            {
                Console.Error.WriteLine("PHASE7_CLEANUP_SECONDARY " + Json.ExceptionDetails(error));
            }
        }
        if (primary is null && secondary.Count != 0)
        {
            throw new AggregateException("Phase7 cleanup failed.", secondary);
        }
    }

    private sealed class Factory(Phase7Run state, string operation) : INativeWorkerFactory
    {
        private readonly NativeWorkerFactory real = new(
            state.Database,
            new(state.Database),
            state.Ledger,
            () =>
            {
                INativeSdkHost host = state.Sessions.CreateHost(operation);
                host = state.Handoff?.Wrap(host) ?? host;
                return state.Exhaustion?.Wrap(host) ?? host;
            },
            settlementTimeProvider: state.SettlementTimeProvider
        );
        public JsonArray SettlementFailures => real.SettlementFailures;
        public JsonArray SettlementDetails => real.SettlementDetails;

        public INativeWorker Create(CreditReservation? reservation, bool calibration)
        {
            INativeWorker worker = real.Create(reservation, calibration);
            return state.Handoff is { } observation && reservation is not null && !calibration
                ? observation.Wrap(worker, reservation.Worker)
                : worker;
        }

        public Task<JsonObject> PreflightAsync(
            IReadOnlyList<(string Worker, string Cwd)> peers,
            CancellationToken token
        ) => real.PreflightAsync(peers, token);

        public Task CompleteAsync() => real.CompleteAsync();

        public Task<RecoveredNativeUsage> ReadSettledUsageAsync(
            CreditReservation reservation,
            string cwd,
            string evidencePath,
            CancellationToken token
        ) => real.ReadSettledUsageAsync(reservation, cwd, evidencePath, token);
    }
}
