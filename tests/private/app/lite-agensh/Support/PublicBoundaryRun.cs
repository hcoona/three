using System.Text.Json.Nodes;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace LiteAgensh.Tests.Support;

// Original public journey and the validated-reader collaborator boundary only.
// Native recovery provenance/finality remains at the qualified Ph4/5 boundary.
internal sealed class PublicBoundaryRun : IAsyncDisposable
{
    private readonly GitScenarioState? git;
    private readonly ScratchDirectory? scratch;
    private readonly bool realGit;
    private readonly OfflineNativeControl control;
    private readonly TaskCompletionSource[] recorded;
    private Exception? primary;
    internal RunDatabase Database { get; }
    internal CreditLedger Ledger { get; }
    internal Coordinator Coordinator { get; }
    internal JsonObject Original { get; private set; } = new();
    internal bool PauseAfterUsage { get; set; }
    internal Func<
        CreditReservation,
        CancellationToken,
        Task<RecoveredNativeUsage>
    >? Reader
    { get; set; }
    internal bool MissingLimitCapability
    {
        get => control.MissingLimitCapability;
        set => control.MissingLimitCapability = value;
    }
    internal bool FailPaidStop
    {
        get => control.FailPaidStop;
        set => control.FailPaidStop = value;
    }

    private PublicBoundaryRun(GitScenarioState? git, ScratchDirectory? scratch, int workers)
    {
        this.git = git;
        this.scratch = scratch;
        realGit = git is not null;
        Func<string, string> pathFor = name =>
            git is not null ? git.PathFor(name) : scratch!.PathFor(name);
        RunConfiguration baseConfig = git?.Database.Configuration ?? new()
        {
            RepositoryPath = scratch!.PathFor("checkout"),
            StatePath = scratch.PathFor("public-run"),
            CampaignLedgerPath = scratch.PathFor("public-campaign.sqlite"),
            Goal = "Integrate two independent changes without touching the initial checkout.",
            NativeExecutable = scratch.PathFor("unavailable-native", "copilot"),
            CandidateChecks = [GitScenarioState.ExitCheck(0)],
            IntegrationChecks = [],
            FinalChecks = [GitScenarioState.ExitCheck(0)],
        };
        RunConfiguration configuration = baseConfig with
        {
            StatePath = pathFor("public-run"),
            CampaignLedgerPath = pathFor("public-campaign.sqlite"),
            NativeModel = "controlled-model",
            WorkerCount = workers,
            RunAiCreditAllowance = 207.25m,
            CampaignAiCreditAllowance = 407.25m,
        };
        Database = new(configuration.StatePath, configuration);
        Ledger = new(configuration.CampaignLedgerPath, configuration.CampaignAiCreditAllowance);
        control = new(Database);
        recorded =
        [
            .. Enumerable
                .Range(0, workers)
                .Select(_ => new TaskCompletionSource(
                    TaskCreationOptions.RunContinuationsAsynchronously
                )),
        ];
        CollaborationOperations collaboration = new(Database);
        IGitWorkspace workspace = realGit
            ? new GitOperations(Database, collaboration)
            : new LightweightGitWorkspace(Database, collaboration);
        Coordinator = new(
            Database,
            collaboration,
            workspace,
            (_, _, _) => new Factory(this)
        );
    }

    internal static async Task<PublicBoundaryRun> CreateAsync(
        int workers,
        CancellationToken token,
        bool realGit = false
    )
    {
        GitScenarioState.AssertEnvironmentUnchanged();
        GitScenarioState? git = realGit
            ? await GitScenarioState.CreateAsync(token).ConfigureAwait(false)
            : null;
        ScratchDirectory? scratch = realGit ? null : new();
        PublicBoundaryRun? state = null;
        try
        {
            state = new(git, scratch, workers);
            GitScenarioState.Data(
                await state.Coordinator.InitializeAsync(token).ConfigureAwait(false)
            );
            return state;
        }
        catch (Exception error)
        {
            if (state is not null)
            {
                state.primary = error;
                await state.DisposeAsync().ConfigureAwait(false);
            }
            else
            {
                git?.Dispose();
                scratch?.Dispose();
                GitScenarioState.AssertEnvironmentUnchanged();
            }
            throw;
        }
    }

    internal async Task CalibrateAsync(CancellationToken token)
    {
        GitScenarioState.Data(await Reopen().PreflightAsync(token).ConfigureAwait(false));
        GitScenarioState.Data(await Reopen().CalibrateAsync(token).ConfigureAwait(false));
        Assert.AreEqual(
            7.25m,
            Ledger.Reservations(Database.RunId).Single(row => row.Worker == "calibration").Actual
        );
    }

    internal async Task FailOriginalAsync(CancellationToken token)
    {
        await CalibrateAsync(token).ConfigureAwait(false);
        Original = await Coordinator.RunAsync("run", token).ConfigureAwait(false);
        Assert.IsFalse(Original["ok"]!.GetValue<bool>(), Original.ToJsonString());
        foreach (
            CreditReservation row in Ledger
                .Reservations(Database.RunId)
                .Where(row => row.Worker != "calibration")
        )
        {
            Assert.AreEqual("uncertain", row.State);
            Assert.AreEqual(80m, row.Actual);
            Assert.AreEqual(200m / Database.Configuration.WorkerCount, row.Allowance);
        }
        Assert.AreEqual(
            0L,
            RunStateQueries.Number(
                Database,
                "SELECT COUNT(*) FROM coordinator_segments WHERE ended_at IS NULL;"
            )
        );
    }

    internal async Task PauseOriginalAsync(CancellationToken token)
    {
        PauseAfterUsage = true;
        await CalibrateAsync(token).ConfigureAwait(false);
        Original = await Coordinator.RunAsync("run", token).ConfigureAwait(false);
        Assert.AreEqual("paused", Database.ReadMetadata("lifecycle"), Original.ToJsonString());
    }

    internal Coordinator Reopen()
    {
        CollaborationOperations collaboration = new(Database);
        IGitWorkspace workspace = realGit
            ? new GitOperations(Database, collaboration)
            : new LightweightGitWorkspace(Database, collaboration);
        return new(
            Database,
            collaboration,
            workspace,
            (_, _, _) => new Factory(this)
        );
    }

    internal async Task RunBodyAsync(Func<Task> body)
    {
        try
        {
            await body().ConfigureAwait(false);
        }
        catch (Exception error)
        {
            primary = error;
            throw;
        }
        finally
        {
            await DisposeAsync().ConfigureAwait(false);
        }
    }

    public async ValueTask DisposeAsync()
    {
        Exception? secondary = null;
        try
        {
            await control.DisposeAsync().ConfigureAwait(false);
        }
        catch (Exception error)
        {
            secondary = error;
            Console.Error.WriteLine(
                "PUBLIC_RUN_CLEANUP_SECONDARY " + Json.ExceptionDetails(error)
            );
        }
        try
        {
            if (primary is null && secondary is null)
            {
                git?.Dispose();
                scratch?.Dispose();
                GitScenarioState.AssertEnvironmentUnchanged();
            }
            else
            {
                GitScenarioState.AssertEnvironmentUnchanged();
            }
        }
        catch (Exception error)
        {
            Console.Error.WriteLine(
                "PUBLIC_RUN_GIT_CLEANUP_SECONDARY " + Json.ExceptionDetails(error)
            );
            secondary = secondary is null ? error : new AggregateException(secondary, error);
        }
        if (primary is null && secondary is not null)
        {
            throw secondary;
        }
    }

    private sealed class Factory(PublicBoundaryRun state) : INativeWorkerFactory
    {
        private readonly NativeWorkerFactory real = new(
            state.Database,
            new(state.Database),
            state.Ledger,
            state.control.CreateHost
        );
        public JsonArray SettlementFailures => real.SettlementFailures;
        public JsonArray SettlementDetails => real.SettlementDetails;

        public INativeWorker Create(CreditReservation? reservation, bool calibration)
        {
            INativeWorker worker = real.Create(reservation, calibration);
            return calibration ? worker : new FailedTurn(state, worker, reservation!.Worker);
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
        ) =>
            (
                state.Reader
                ?? throw new InvalidOperationException(
                    "Controlled typed recovery reader gate is closed."
                )
            )(reservation, token);
    }

    private sealed class FailedTurn(PublicBoundaryRun state, INativeWorker real, string worker)
        : INativeWorker
    {
        public string? SessionId => real.SessionId;
        public string ReservationId => real.ReservationId;
        public JsonObject? Diagnostic => real.Diagnostic;
        public string? NativeFailure => real.NativeFailure;
        public Exception? FirstFailure => real.FirstFailure;

        public void BindOwner(CancellationToken token) => real.BindOwner(token);

        public NativeRequest BeginRequest(CancellationToken token) => real.BeginRequest(token);

        public Task StartAsync(
            string cwd,
            string? exactSession,
            CancellationToken token,
            string? recoveryLabel = null,
            WorkAdmission? admission = null
        ) => real.StartAsync(cwd, exactSession, token, recoveryLabel, admission);

        public async Task<JsonObject> PromptAsync(
            string text,
            CancellationToken token,
            WorkAdmission? admission = null
        )
        {
            _ = await real.PromptAsync(text, token, admission).ConfigureAwait(false);
            await real.VerifyCreditsAsync(token).ConfigureAwait(false);
            if (state.PauseAfterUsage)
            {
                _ = state.Coordinator.RequestControl("pause");
                TaskCompletionSource cancelled = new(
                    TaskCreationOptions.RunContinuationsAsynchronously
                );
                using CancellationTokenRegistration registration = token.Register(() =>
                    cancelled.TrySetResult()
                );
                // Actual coordinator owner-cancellation is the causal signal.
                await cancelled.Task.WaitAsync(token).ConfigureAwait(false);
                token.ThrowIfCancellationRequested();
                throw new InvalidOperationException("Pause did not cancel its original request.");
            }
            OfflineNativeControl.Session session = state
                .control.Hosts.SelectMany(host => host.Sessions)
                .Single(item => item.SessionId == SessionId);
            session.FinalOverride = new(SessionId!, 90_000_000_000d, "partial", 1);
            int index = Array.IndexOf(state.Database.Configuration.WorkerIds, worker);
            state.recorded[index].TrySetResult();
            await Task.WhenAll(state.recorded.Select(signal => signal.Task))
                .WaitAsync(TimeSpan.FromSeconds(5), token)
                .ConfigureAwait(false);
            throw new OperationException(
                "public_original_failed",
                "Controlled original failed after durable usage; partial stop is not finality."
            );
        }

        public Task VerifyCreditsAsync(CancellationToken token) => real.VerifyCreditsAsync(token);

        public Task StopAsync() => real.StopAsync();

        public ValueTask DisposeAsync() => real.DisposeAsync();
    }
}
