using System.Diagnostics;
using System.Globalization;
using System.Text.Json.Nodes;
using Microsoft.Data.Sqlite;
using static LiteAgensh.Json;
using static LiteAgensh.Operation;

namespace LiteAgensh;

internal sealed class CoordinatorSegmentRunner(
    CoordinatorControls controls,
    RunDatabase database,
    CollaborationOperations collaboration,
    IGitWorkspace git,
    CreditLedger credits,
    INativeWorkerFactory nativeWorkers,
    CoordinatorRecovery recovery,
    Action<IReadOnlyList<Task>>? observeWorkerCompletion,
    string mode,
    CancellationToken cancellationToken
) : IDisposable
{
    private const int DeadlineDrainSeconds = 30;
    private static readonly TimeSpan PollInterval = TimeSpan.FromMilliseconds(250);

    private readonly CancellationTokenSource stop =
        CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
    private readonly Lock workerMembership = new();
    private readonly List<(string Id, INativeWorker Worker)> workers = [];
    private readonly List<(string Id, Task Loop)> loops = [];
    private readonly Dictionary<string, CreditReservation> allocations = [];
    private readonly HashSet<string> reconnecting = [];
    private readonly HashSet<int> reminders = [];
    private readonly JsonArray settlementFailures = [];
    private readonly JsonArray settlementDetails = [];
    private readonly string[] ids = database.Configuration.WorkerIds;
    private readonly bool incrementalAdmission =
        database.Configuration.WorkerStartIntervalSeconds > 0
        || database.Configuration.CreditAllocation == "shared";

    private long segment;
    private DateTimeOffset workDeadline;
    private WorkAdmission? workAdmission;
    private CancellationToken workerStop;
    private CancellationToken requestDeadlineToken;
    private Task<INativeWorker>? admission;
    private string? admissionId;
    private long stopObserved = long.MaxValue;
    private bool planning;
    private volatile bool released;
    private DateTimeOffset plannerDeadline;
    private DateTimeOffset nextAdmission;
    private bool creditWarningSent;

    private string reason = "startup-failed";
    private bool finalize;
    private Exception? failure;
    private bool persistenceFailed;
    private bool draining;
    private string? drainOutcome;
    private OperationException? drainAccountingStop;
    private long drainAccountingObserved;
    private string? drainAccountingCause;

    private WorkAdmission Admission => workAdmission!;

    public static void EnsureSegmentStorage(
        SqliteConnection connection,
        SqliteTransaction transaction
    ) =>
        Sql.Execute(
            connection,
            """
            CREATE TABLE IF NOT EXISTS coordinator_segments(
                id INTEGER PRIMARY KEY AUTOINCREMENT, mode TEXT NOT NULL, state TEXT NOT NULL,
                started_at TEXT NOT NULL, deadline TEXT NOT NULL, ended_at TEXT, stop_reason TEXT);
            CREATE TABLE IF NOT EXISTS coordinator_turns(
                batch_id INTEGER PRIMARY KEY, worker TEXT NOT NULL, state TEXT NOT NULL,
                stop_reason TEXT, completed_at TEXT);
            """,
            transaction
        );

    public void Dispose()
    {
        workAdmission?.Dispose();
        stop.Dispose();
    }

    public async Task<JsonObject> ExecuteAsync()
    {
        workerStop = stop.Token;
        using CancellationTokenRegistration stopObservation = workerStop.Register(() =>
            Interlocked.CompareExchange(ref stopObserved, Stopwatch.GetTimestamp(), long.MaxValue)
        );
        try
        {
            RecoverCompletedTurns();
            collaboration.RecoverInterrupted();
            (segment, workDeadline) = BeginSegment(mode);
            database.Meta.WriteJson(RunMetadata.SettlementFailuresKey, settlementFailures);
            if (workDeadline <= database.Now)
            {
                StopAtDeadline();
            }
            else
            {
                await RunSegmentBodyAsync().ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException exception) when (
            (stop.IsCancellationRequested || workAdmission?.IsClosed == true)
            && (cancellationToken.IsCancellationRequested || !IsNonRequestCancellation(exception))
        )
        {
            RecordCancellation();
        }
        catch (Exception exception)
        {
            RecordSegmentFailure(exception);
        }
        finally
        {
            try
            {
                await SettleCohortUnconditionallyAsync().ConfigureAwait(false);
            }
            finally
            {
                workAdmission?.Dispose();
            }
        }

        return await PublishTerminalOutcomeAsync().ConfigureAwait(false);
    }

    private async Task RunSegmentBodyAsync()
    {
        TimeSpan requestRemaining = workDeadline.AddSeconds(DeadlineDrainSeconds) - database.Now;
        using CancellationTokenSource requestDeadline = new(
            requestRemaining > TimeSpan.Zero ? requestRemaining : TimeSpan.Zero,
            database.TimeProvider
        );
        if (requestRemaining <= TimeSpan.Zero)
        {
            await requestDeadline.CancelAsync().ConfigureAwait(false);
        }
        requestDeadlineToken = requestDeadline.Token;
        using CancellationTokenRegistration deadlineStop =
            requestDeadlineToken.Register(stop.Cancel);
        workAdmission = new(database.TimeProvider, workDeadline, stop.Token);
        await git.InitializeAsync(Admission.Token).ConfigureAwait(false);
        database.Meta.SetLifecycle("preflighting");
        planning = database.Configuration.PlanningSeconds > 0 && !database.Meta.IsPlanIntegrated;
        released = !planning;

        bool normalDeadline = false;
        try
        {
            ReserveCohortAllocations();
            if (!incrementalAdmission)
            {
                await AdmitInitialWorkersAsync().ConfigureAwait(false);
            }

            EnterActiveCoordination();
            await RunActiveCoordinationLoopAsync().ConfigureAwait(false);
            if (reason == "startup-failed")
            {
                StopForCancellation();
            }
            normalDeadline = reason == "deadline" && CanDrainDeadline();
        }
        catch (OperationCanceledException exception) when (
            !IsNonRequestCancellation(exception) && Admission.IsClosed && CanDrainDeadline()
        )
        {
            StopAtDeadline();
            normalDeadline = true;
        }
        finally
        {
            try
            {
                if (normalDeadline)
                {
                    await DrainBeforeStopAsync().ConfigureAwait(false);
                }
                cancellationToken.ThrowIfCancellationRequested();
            }
            finally
            {
                try
                {
                    await StopAdmissionAsync().ConfigureAwait(false);
                }
                finally
                {
                    ReleaseUnstartedAllocations();
                }
            }
        }
    }

    private void ReserveCohortAllocations()
    {
        for (int i = 0; i < ids.Length; i++)
        {
            Admission.ThrowIfClosed();
            string id = ids[i];
            CreditReservation? previous =
                mode == "reconnect" ? recovery.ReadReconnectReservation(id) : null;
            if (previous is not null)
            {
                reconnecting.Add(id);
            }

            allocations.Add(
                id,
                previous is null
                    ? Reserve(id, ids.Length - i)
                    : credits.ContinueExact(previous.Id, database.CreateSdkEvidencePath(id))
            );
        }
    }

    private async Task AdmitInitialWorkersAsync()
    {
        foreach (string id in ids)
        {
            admission = StartWorkerAsync(id);
            while (!admission.IsCompleted)
            {
                CheckNativeFailures();
                if (controls.NextPending() is { } action)
                {
                    StopForControl(action);
                    await stop.CancelAsync().ConfigureAwait(false);
                    break;
                }

                try
                {
                    await admission.WaitAsync(PollInterval, database.TimeProvider, stop.Token)
                        .ConfigureAwait(false);
                }
                catch (TimeoutException)
                {
                    // Await completion below to distinguish startup faults from polling ticks.
                }
            }
            await admission.ConfigureAwait(false);
            admission = null;
            Admission.ThrowIfClosed();
        }
    }

    private async Task<INativeWorker> StartWorkerAsync(string id)
    {
        Admission.ThrowIfClosed();
        CreditReservation allocation = allocations[id];
        INativeWorker worker = OwnWorker(id, allocation);
        string? exact = reconnecting.Contains(id) ? allocation.SessionId : null;
        try
        {
            await worker.StartAsync(git.GetWorkerPath(id), exact, Admission.Token,
                admission: Admission).ConfigureAwait(false);
        }
        catch (OperationException exception) when (exception.Code == "session_load_failed")
        {
            await worker.DisposeAsync().ConfigureAwait(false);
            lock (workerMembership)
            {
                workers.Remove((id, worker));
            }
            Admission.ThrowIfClosed();
            worker = OwnWorker(id, Reserve(id, 1));
            await worker.StartAsync(
                git.GetWorkerPath(id),
                null,
                Admission.Token,
                recoveryLabel: "fresh-summary-fallback: exact-session load unavailable or failed",
                admission: Admission
            ).ConfigureAwait(false);
        }
        return worker;
    }

    private INativeWorker OwnWorker(string id, CreditReservation reservation)
    {
        INativeWorker worker = nativeWorkers.Create(reservation, calibration: false);
        worker.BindOwner(stop.Token);
        lock (workerMembership)
        {
            workers.Add((id, worker));
        }
        return worker;
    }

    private void EnterActiveCoordination()
    {
        database.Meta.SetLifecycle("running");
        ResetPlannerDeadline();
        nextAdmission = database.Now;
        if (database.Now >= workDeadline)
        {
            StopAtDeadline();
        }
        foreach ((string id, INativeWorker native) in workers)
        {
            StartLoop(id, native);
        }
        if ((workDeadline - database.Now).TotalSeconds <= 900)
        {
            reminders.Add(900);
        }
        creditWarningSent = database.Meta.IsCreditWarningSent;
    }

    private void StartLoop(string id, INativeWorker worker)
    {
        if (Admission.IsClosed)
        {
            return;
        }
        if (!planning || released || id == "worker1")
        {
            collaboration.QueueKickoff(id, mode == "run"
                ? "Initial peer kickoff" : $"Durable {mode}; inspect state before replay");
        }

        loops.Add((id, WorkerLoopAsync(id, worker)));
    }

    private async Task RunActiveCoordinationLoopAsync()
    {
        while (!stop.IsCancellationRequested)
        {
            CheckNativeFailures();
            if (Admission.IsClosed)
            {
                StopAtDeadline();
                break;
            }
            if (ObserveOwnerStop(reopenWorkerClosure: true))
            {
                break;
            }

            observeWorkerCompletion?.Invoke(loops.Select(item => item.Loop).ToArray());
            if (loops.FirstOrDefault(item => item.Loop.IsCompleted) is { Loop: Task ended })
            {
                await ObserveEndedLoopAsync(ended).ConfigureAwait(false);
                continue;
            }
            if (admission is { IsCompleted: true } completed)
            {
                await FinishAdmissionAsync(completed).ConfigureAwait(false);
                continue;
            }

            DateTimeOffset now = database.Now;
            if (now >= workDeadline)
            {
                StopAtDeadline();
                break;
            }
            if (TryStartEligibleWorker(now))
            {
                continue;
            }
            if (!await RunActiveGitPassAsync(Admission.Token)
                .ConfigureAwait(false))
            {
                break;
            }

            ReleasePlannerPeers();
            PublishReminders(workDeadline, reminders);
            PublishCreditWarning();
            await WaitForProgressAsync(PendingWork().ToArray()).ConfigureAwait(false);
        }
    }

    private async Task FinishAdmissionAsync(Task<INativeWorker> completed)
    {
        admission = null;
        INativeWorker worker = await completed.ConfigureAwait(false);
        Admission.ThrowIfClosed();
        if (workers.Count == 1)
        {
            ResetPlannerDeadline();
        }
        nextAdmission = database.Now.AddSeconds(database.Configuration.WorkerStartIntervalSeconds);
        StartLoop(admissionId!, worker);
    }

    private bool TryStartEligibleWorker(DateTimeOffset now)
    {
        if (!incrementalAdmission || admission is not null || workers.Count >= ids.Length
            || now < nextAdmission || (planning && !released && workers.Count != 0))
        {
            return false;
        }
        admissionId = ids[workers.Count];
        admission = StartWorkerAsync(admissionId);
        return true;
    }

    private void ResetPlannerDeadline() =>
        plannerDeadline = database.Now.AddSeconds(database.Configuration.PlanningSeconds);

    private void ReleasePlannerPeers()
    {
        if (!planning || released || loops.Count == 0
            || (!database.Meta.IsPlanIntegrated && database.Now < plannerDeadline))
        {
            return;
        }
        released = true;
        foreach (string peer in ids.Skip(1))
        {
            collaboration.QueueKickoff(
                peer, "Planner release: integrated plan or planning deadline reached"
            );
        }
    }

    private bool ObserveOwnerStop(bool reopenWorkerClosure = false)
    {
        if (controls.NextPending() is { } action)
        {
            StopForControl(action);
            return true;
        }
        string? closedBy = database.Meta.RootClosedBy;
        if (closedBy == "owner")
        {
            reason = "owner-root-closure";
            finalize = true;
            return true;
        }
        if (reopenWorkerClosure && closedBy is not null)
        {
            collaboration.ReopenWorkerClosedRoot();
        }
        return false;
    }

    private async Task<bool> RunActiveGitPassAsync(CancellationToken activeCancellationToken)
    {
        if (controls.HasPendingPause())
        {
            StopForControl("pause");
            await stop.CancelAsync().ConfigureAwait(false);
            return false;
        }

        using CancellationTokenSource gitPassStop =
            CancellationTokenSource.CreateLinkedTokenSource(activeCancellationToken);
        Task gitPass = git.ProcessPendingAsync(gitPassStop.Token);
        try
        {
            while (!gitPass.IsCompleted && !gitPassStop.IsCancellationRequested)
            {
                CheckNativeFailures();
                if (controls.HasPendingPause())
                {
                    StopForControl("pause");
                    await stop.CancelAsync().ConfigureAwait(false);
                    await gitPassStop.CancelAsync().ConfigureAwait(false);
                    break;
                }
                await WatchWorkersDuringGitPassAsync().ConfigureAwait(false);
                try
                {
                    await gitPass.WaitAsync(TimeSpan.FromMilliseconds(50), CancellationToken.None)
                        .ConfigureAwait(false);
                }
                catch (TimeoutException)
                {
                }
            }
        }
        catch
        {
            try
            {
                await Task.WhenAll(gitPass, gitPassStop.CancelAsync()).ConfigureAwait(false);
            }
            catch
            {
                // Observe the pass without replacing the supervision failure.
            }
            throw;
        }

        try
        {
            await gitPass.ConfigureAwait(false);
            return reason != "owner-pause";
        }
        catch (OperationCanceledException) when (
            gitPassStop.IsCancellationRequested && reason == "owner-pause"
        )
        {
            return false;
        }
    }

    private async Task WatchWorkersDuringGitPassAsync()
    {
        if (database.Meta.RootClosedBy is not null and not "owner")
        {
            collaboration.ReopenWorkerClosedRoot();
        }
        if (loops.FirstOrDefault(item =>
            item.Loop.IsFaulted || (!Admission.IsClosed && item.Loop.IsCompleted)
        ) is { Loop: Task ended })
        {
            await ObserveEndedLoopAsync(ended).ConfigureAwait(false);
        }
    }

    private async Task ObserveEndedLoopAsync(Task loop)
    {
        await loop.ConfigureAwait(false);
        if (!Admission.IsClosed)
        {
            throw new OperationException("worker_stopped",
                "A worker loop ended before run quiescence.");
        }
    }

    private async Task DrainBeforeStopAsync()
    {
        draining = true;
        await Admission.CloseAsync().ConfigureAwait(false);
        PersistShutdown(() => database.Meta.SetLifecycle("draining"));
        PersistShutdown(() => RecordDeadlineDrain(segment, workDeadline, "draining", "deadline"));
        if (persistenceFailed)
        {
            drainOutcome = "interrupted";
            return;
        }

        DateTimeOffset drainDeadline = workDeadline.AddSeconds(DeadlineDrainSeconds);
        if (drainDeadline <= database.Now)
        {
            drainOutcome = "timed-out";
            await stop.CancelAsync().ConfigureAwait(false);
            return;
        }
        while (!stop.IsCancellationRequested)
        {
            // Snapshot pending work before observing completions that could race these checks.
            Task[] pending = PendingWork().Where(task => !task.IsCompleted).ToArray();
            CheckNativeFailures();
            if (ObserveOwnerStop())
            {
                drainOutcome = "interrupted";
                return;
            }
            foreach (Task loop in loops.Select(item => item.Loop).Where(task => task.IsCompleted))
            {
                await loop.ConfigureAwait(false);
            }
            if (admission is { IsCompleted: true })
            {
                await ObserveDrainingAdmissionAsync().ConfigureAwait(false);
            }
            CheckDrainAccounting();
            if (database.Now >= drainDeadline)
            {
                drainOutcome = "timed-out";
                return;
            }
            if (loops.All(item => item.Loop.IsCompleted) && (admission?.IsCompleted ?? true))
            {
                await Task.WhenAll(loops.Select(item => item.Loop)).ConfigureAwait(false);
                await ObserveDrainingAdmissionAsync().ConfigureAwait(false);
                drainOutcome = "completed";
                return;
            }
            await WaitForProgressAsync(pending).ConfigureAwait(false);
        }
        drainOutcome = cancellationToken.IsCancellationRequested ? "interrupted" : "timed-out";
    }

    private async Task ObserveDrainingAdmissionAsync()
    {
        if (admission is not null)
        {
            try
            {
                await admission.ConfigureAwait(false);
            }
            catch (OperationCanceledException)
                when (Admission.IsClosed && !stop.IsCancellationRequested)
            {
            }
        }
    }

    private void CheckDrainAccounting()
    {
        try
        {
            credits.RequirePaidAdmission();
        }
        catch (OperationException exception) when (exception.Code == "credit_unknown")
        {
            drainAccountingObserved = Stopwatch.GetTimestamp();
            drainAccountingStop = exception;
            drainAccountingCause = cancellationToken.IsCancellationRequested ? "caller-cancelled"
                : requestDeadlineToken.IsCancellationRequested ? "deadline" : "credit_unknown";
            throw;
        }
    }

    private IEnumerable<Task> PendingWork()
    {
        foreach ((_, Task loop) in loops)
        {
            yield return loop;
        }
        if (admission is not null)
        {
            yield return admission;
        }
    }

    private async Task WaitForProgressAsync(Task[] subjects)
    {
        stop.Token.ThrowIfCancellationRequested();
        if (subjects.Any(task => task.IsCompleted))
        {
            return;
        }
        using CancellationTokenSource polling =
            CancellationTokenSource.CreateLinkedTokenSource(stop.Token);
        Task poll = Task.Delay(PollInterval, database.TimeProvider, polling.Token);
        try
        {
            if (subjects.Length == 0)
            {
                await poll.ConfigureAwait(false);
            }
            else
            {
                await Task.WhenAny(subjects.Append(poll)).WaitAsync(stop.Token)
                    .ConfigureAwait(false);
                stop.Token.ThrowIfCancellationRequested();
            }
        }
        finally
        {
            await polling.CancelAsync().ConfigureAwait(false);
            try
            {
                await poll.ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (polling.IsCancellationRequested)
            {
            }
        }
    }

    private async Task StopAdmissionAsync()
    {
        await stop.CancelAsync().ConfigureAwait(false);
        if (admission is not null)
        {
            try
            {
                await admission.ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (stop.IsCancellationRequested)
            {
            }
        }
    }

    private void ReleaseUnstartedAllocations()
    {
        HashSet<string> owned = [.. workers.Select(item => item.Worker.ReservationId)];
        foreach (CreditReservation allocation in allocations.Values.Where(item =>
            !owned.Contains(item.Id)))
        {
            CreditReservation current = credits.Reservations(database.RunId)
                .Single(row => row.Id == allocation.Id);
            if (current.State == "reserved")
            {
                credits.ReleaseUnstarted(allocation.Id);
            }
        }
    }

    private void StopForControl(string action)
    {
        reason = action == "pause" ? "owner-pause" : "owner-finalize";
        finalize = action == "finalize";
    }

    private void StopAtDeadline()
    {
        reason = "deadline";
        finalize = true;
    }

    private void StopForCancellation()
    {
        reason = cancellationToken.IsCancellationRequested ? "caller-cancelled" : "deadline";
        finalize = !cancellationToken.IsCancellationRequested;
    }

    private bool CanDrainDeadline() =>
        !stop.IsCancellationRequested || requestDeadlineToken.IsCancellationRequested;

    private void RecordCancellation()
    {
        if (cancellationToken.IsCancellationRequested
            || reason is not ("owner-pause" or "owner-finalize"))
        {
            StopForCancellation();
        }
        if (draining)
        {
            drainOutcome = cancellationToken.IsCancellationRequested ? "interrupted" : "timed-out";
        }
    }

    private void RecordSegmentFailure(Exception exception)
    {
        failure = exception;
        reason = FailureReason(exception);
        if (ReferenceEquals(exception, drainAccountingStop))
        {
            drainOutcome = drainAccountingCause == "deadline" ? "timed-out" : "interrupted";
            finalize = drainAccountingCause == "deadline";
            if (drainAccountingCause != "credit_unknown")
            {
                reason = drainAccountingCause!;
                failure = null;
            }
        }
    }

    private static string FailureReason(Exception exception) =>
        exception is OperationException operation ? operation.Code : "coordinator-failed";

    private bool IsNonRequestCancellation(OperationCanceledException exception) =>
        exception.Data["liteagensh.nativeFailureObserved"] is long observed
        && observed <= Volatile.Read(ref stopObserved)
        && exception.Data["liteagensh.nativeFailureCancelled"] is false;

    private void CheckNativeFailures()
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (stop.IsCancellationRequested)
        {
            return;
        }
        INativeWorker[] snapshot;
        lock (workerMembership)
        {
            snapshot = [.. workers.Select(item => item.Worker)];
        }
        Exception? originating = snapshot.Select(worker => worker.FirstFailure)
            .OfType<Exception>()
            .Where(error => error.Data["liteagensh.nativeFailureObserved"] is long observed
                && observed <= Volatile.Read(ref stopObserved)
                && error.Data["liteagensh.nativeFailureCancelled"] is not true)
            .OrderBy(error => (long)error.Data["liteagensh.nativeFailureObserved"]!)
            .FirstOrDefault();
        cancellationToken.ThrowIfCancellationRequested();
        if (originating is not null)
        {
            if (originating is OperationCanceledException cancelled
                && IsNonRequestCancellation(cancelled))
            {
                finalize = false;
            }
            throw originating;
        }
    }

    private async Task SettleCohortUnconditionallyAsync()
    {
        try
        {
            await stop.CancelAsync().ConfigureAwait(false);
        }
        catch (Exception exception)
        {
            failure ??= exception;
        }
        if (!draining && requestDeadlineToken.IsCancellationRequested)
        {
            draining = true;
            drainOutcome = reason == "deadline" ? "timed-out" : "interrupted";
        }
        PersistDrainOutcome();
        PersistShutdown(() => database.Meta.SetLifecycle("pausing"));
        PersistShutdown(() => database.Meta.StopReason = reason);
        PublishStoppingNotice();

        await StopAndDisposeWorkersAsync().ConfigureAwait(false);
        await JoinWorkerLoopsAsync().ConfigureAwait(false);
        AttributeOriginatingNativeFailure();
        await CompleteNativeRuntimeAsync().ConfigureAwait(false);

        if (draining)
        {
            PersistShutdown(() => database.Meta.StopReason = reason);
            PersistDrainOutcome();
        }
        if (failure is { } primaryFailure)
        {
            RetainFailureDetails(primaryFailure);
            RetainFailureSummary(primaryFailure);
        }
    }

    private void PersistDrainOutcome()
    {
        if (draining)
        {
            PersistShutdown(() => RecordDeadlineDrain(
                segment, workDeadline, drainOutcome ?? "interrupted", reason, drainAccountingStop
            ));
        }
    }

    private void PublishStoppingNotice()
    {
        try
        {
            collaboration.Invoke(
                "owner",
                "message_send",
                new()
                {
                    ["recipient"] = "team",
                    ["body"] =
                        $"Run stopping: {reason}. New dispatch is disabled; "
                        + "native cancellation and owned-process settlement follow. "
                        + "Committed and partial work, tasks, board findings and "
                        + "session associations are retained. "
                        + "Explicit resume uses fresh sessions and durable state; "
                        + "a stop reason is not final-check success.",
                    ["urgent"] = true,
                }
            );
        }
        catch (Exception exception)
        {
            failure ??= exception;
        }
    }

    private async Task StopAndDisposeWorkersAsync()
    {
        bool workerStopFailed = false;
        foreach ((string id, INativeWorker worker) in workers)
        {
            try
            {
                await worker.StopAsync().ConfigureAwait(false);
            }
            catch (Exception exception)
            {
                workerStopFailed = true;
                RecordWorkerSettlementFailure(id, worker, "stop", exception);
            }
        }
        if (workerStopFailed)
        {
            return;
        }

        foreach ((string id, INativeWorker worker) in workers)
        {
            try
            {
                await worker.DisposeAsync().ConfigureAwait(false);
            }
            catch (Exception exception)
            {
                RecordWorkerSettlementFailure(id, worker, "dispose", exception);
            }
        }
    }

    private void RecordWorkerSettlementFailure(
        string id, INativeWorker worker, string phase, Exception exception
    )
    {
        failure ??= exception;
        AddSettlementDetails(settlementDetails, id, worker.ReservationId, phase, exception);
        settlementFailures.Add(new JsonObject
        {
            ["worker"] = id,
            ["reservationId"] = worker.ReservationId,
            ["phase"] = phase,
            ["message"] = exception.Message,
        });
        PersistUnresolvedSettlement();
    }

    private async Task JoinWorkerLoopsAsync()
    {
        try
        {
            await Task.WhenAll(loops.Select(item => item.Loop))
                .WaitAsync(TimeSpan.FromSeconds(15), CancellationToken.None)
                .ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (stop.IsCancellationRequested)
        {
            // Native cancellation is expected; all processes were settled separately above.
        }
        catch (Exception exception)
        {
            failure ??= exception;
            if (exception is TimeoutException)
            {
                PersistShutdown(() => database.Meta.SetLifecycle("unresolved"));
            }
        }
    }

    private void AttributeOriginatingNativeFailure()
    {
        bool accountingFailure = drainAccountingStop is not null
            && ReferenceEquals(failure, drainAccountingStop);
        bool deadlineDrainExit = draining && reason == "deadline"
            && drainOutcome is "timed-out" or "completed";
        if (!accountingFailure && !deadlineDrainExit)
        {
            return;
        }

        long observedBefore = accountingFailure
            ? drainAccountingObserved : Volatile.Read(ref stopObserved);
        // A later cancellation-induced fault cannot become the original interruption.
        Exception? originating = loops.Select(item => item.Loop)
            .Where(loop => loop.IsFaulted)
            .SelectMany(loop => loop.Exception!.Flatten().InnerExceptions)
            .Concat(workers.Select(item => item.Worker.FirstFailure).OfType<Exception>())
            .Where(error => error.Data["liteagensh.nativeFailureObserved"] is long observed
                && observed <= observedBefore
                && (accountingFailure || error.Data["liteagensh.nativeFailureCancelled"] is false))
            .OrderBy(error => (long)error.Data["liteagensh.nativeFailureObserved"]!)
            .FirstOrDefault();
        if (originating is not null)
        {
            failure = originating;
            reason = FailureReason(originating);
            drainOutcome = "interrupted";
            finalize = false;
        }
    }

    private async Task CompleteNativeRuntimeAsync()
    {
        try
        {
            await nativeWorkers.CompleteAsync().ConfigureAwait(false);
        }
        catch (Exception exception)
        {
            failure ??= exception;
            if (nativeWorkers.SettlementFailures.Count == 0)
            {
                settlementFailures.Add(new JsonObject
                {
                    ["phase"] = "native-runtime",
                    ["message"] = exception.Message,
                });
            }
            foreach (JsonNode? item in nativeWorkers.SettlementFailures)
            {
                if (!settlementFailures.Any(existing => JsonNode.DeepEquals(existing, item)))
                {
                    settlementFailures.Add(item!.DeepClone());
                }
            }
            PersistUnresolvedSettlement();
        }
    }

    private void PersistUnresolvedSettlement()
    {
        PersistShutdown(() => database.Meta.WriteJson(
            RunMetadata.SettlementFailuresKey, settlementFailures
        ));
        PersistShutdown(() => database.Meta.SetLifecycle("unresolved"));
    }

    private void RetainFailureDetails(Exception primaryFailure)
    {
        try
        {
            JsonArray workerDetails = [];
            foreach ((string id, Task loop) in loops)
            {
                if (loop.Exception is { } errors)
                {
                    foreach (Exception error in errors.Flatten().InnerExceptions)
                    {
                        workerDetails.Add(new JsonObject
                        {
                            ["worker"] = id,
                            ["error"] = ExceptionDetails(error),
                        });
                    }
                }
            }
            database.Meta.WriteJson(
                RunMetadata.FailureDetailsKey(segment),
                new JsonObject
                {
                    ["primary"] = ExceptionDetails(primaryFailure),
                    ["workers"] = workerDetails,
                    ["settlements"] = settlementDetails,
                    ["factorySettlements"] = new JsonArray(nativeWorkers.SettlementDetails
                        .Select(item => item!.DeepClone()).ToArray()),
                    ["nativeFailures"] = new JsonArray(workers.Select(item =>
                        item.Worker.NativeFailure is { } message
                            ? (JsonNode)new JsonObject
                            {
                                ["worker"] = item.Id,
                                ["message"] = message,
                                ["detailsKind"] = "native-message-only",
                            } : null).OfType<JsonNode>().ToArray()),
                }
            );
        }
        catch (Exception exception) when (IsExpected(exception))
        {
            // Diagnostic retention must not replace the original failure or settlement.
        }
    }

    private void RetainFailureSummary(Exception primaryFailure)
    {
        IEnumerable<string> loopFailures = loops.SelectMany(item =>
            item.Loop.IsFaulted
                ? item.Loop.Exception!.Flatten().InnerExceptions
                    .Where(exception => exception.Message != primaryFailure.Message)
                    .Select(exception => item.Id + ": " + exception.Message)
                : []
        );
        string[] otherFailures = loopFailures
            .Concat(workers.SelectMany(item =>
                item.Worker.NativeFailure is { } nativeFailure
                && nativeFailure != primaryFailure.Message
                    ? [item.Id + ": " + nativeFailure]
                    : Array.Empty<string>()
            ))
            .Distinct(StringComparer.Ordinal)
            .ToArray();
        if (otherFailures.Length > 0)
        {
            failure = new OperationException(
                primaryFailure is OperationException operation ? operation.Code : "unavailable",
                primaryFailure.Message + "\nOther worker failures:\n"
                    + string.Join("\n", otherFailures)
            );
        }
        string failureMessage = failure!.Message;
        PersistShutdown(() => database.Meta.Failure = failureMessage);
    }

    private void PersistShutdown(Action persist)
    {
        try
        {
            persist();
        }
        catch (Exception exception) when (IsExpected(exception))
        {
            persistenceFailed = true;
            failure ??= exception;
        }
    }

    private async Task<JsonObject> PublishTerminalOutcomeAsync()
    {
        bool unresolved = false;
        if (!persistenceFailed)
        {
            PersistShutdown(() => unresolved = database.Meta.HasUnresolvedState());
        }
        if (!persistenceFailed && !unresolved)
        {
            (JsonObject? result, string terminalState, ControlActions completedControls) =
                failure is { } error
                    ? (ExpectedFailure(error), "stopped", ControlActions.None)
                    : finalize
                        ? await FinalizeDeliveryAsync().ConfigureAwait(false)
                        : (
                            PausedResult(),
                            "paused",
                            reason == "caller-cancelled"
                                ? ControlActions.None
                                : ControlActions.Pause
                        );
            (JsonObject Result, string State)? committed = null;
            if (!persistenceFailed && result is not null)
            {
                PersistShutdown(
                    () =>
                        committed = controls.Commit(
                            segment,
                            reason,
                            terminalState,
                            result,
                            completedControls
                        )
                );
            }
            if (!persistenceFailed && committed is { } outcome)
            {
                if (outcome.State != "unresolved")
                {
                    cancellationToken.ThrowIfCancellationRequested();
                }
                return outcome.Result;
            }
        }

        JsonObject unresolvedResult = Failure(
            "ownership_or_usage_unresolved",
            failure?.Message ?? "Quiescence could not be established."
        );
        PersistShutdown(
            () =>
                unresolvedResult = controls
                    .Commit(
                        segment,
                        reason,
                        "unresolved",
                        unresolvedResult,
                        ControlActions.None,
                        failureMessage: failure?.Message
                    )
                    .Result
        );
        return unresolvedResult;
    }

    private async Task<(
        JsonObject? Result,
        string State,
        ControlActions CompletedControls
    )> FinalizeDeliveryAsync()
    {
        try
        {
            if (controls.HasPendingPause())
            {
                StopForControl("pause");
                PersistShutdown(() => database.Meta.StopReason = reason);
                return (PausedResult(), "paused", ControlActions.Pause);
            }

            if (
                reason is "owner-finalize" or "owner-root-closure"
                && workDeadline - database.Now is var remaining
                && remaining > TimeSpan.Zero
                && !await RunFinalGitPassAsync(remaining).ConfigureAwait(false)
            )
            {
                PersistShutdown(() => database.Meta.StopReason = reason);
                return (PausedResult(), "paused", ControlActions.Pause);
            }

            (bool paused, (JsonObject Result, string Lifecycle) finalized) =
                await RunPostQuiescenceInterruptibleAsync(
                        ct => controls.FinalizeOwnedAsync(ct),
                        cancellationToken
                    )
                    .ConfigureAwait(false);
            if (paused)
            {
                PersistShutdown(() => database.Meta.StopReason = reason);
                return (PausedResult(), "paused", ControlActions.Pause);
            }

            return (finalized.Result, finalized.Lifecycle, ControlActions.Finalize);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            reason = "caller-cancelled";
            PersistShutdown(() => database.Meta.StopReason = reason);
            return (
                Failure(
                    "cancelled",
                    "The operation was cancelled; inspect retained run status before replacement."
                ),
                "paused",
                ControlActions.None
            );
        }
        catch (SqliteException sqliteFailure)
        {
            persistenceFailed = true;
            failure ??= sqliteFailure;
            return (null, "unresolved", ControlActions.None);
        }
        catch (Exception exception) when (IsExpected(exception))
        {
            PersistShutdown(() => database.Meta.Failure = exception.Message);
            return (ExpectedFailure(exception), "stopped", ControlActions.None);
        }
    }

    private async Task<bool> RunFinalGitPassAsync(TimeSpan remaining)
    {
        using CancellationTokenSource window = new(remaining, database.TimeProvider);
        using CancellationTokenSource lifetime =
            CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, window.Token);
        try
        {
            (bool paused, _) = await RunPostQuiescenceInterruptibleAsync<bool>(
                    async ct =>
                    {
                        await git.ProcessPendingAsync(ct).ConfigureAwait(false);
                        return true;
                    },
                    lifetime.Token
                )
                .ConfigureAwait(false);
            return !paused;
        }
        catch (OperationCanceledException) when (
            !cancellationToken.IsCancellationRequested && window.IsCancellationRequested
        )
        {
            return true;
        }
    }

    private async Task<(bool Paused, T? Value)> RunPostQuiescenceInterruptibleAsync<T>(
        Func<CancellationToken, Task<T>> operation,
        CancellationToken operationCancellationToken
    )
    {
        if (controls.HasPendingPause())
        {
            StopForControl("pause");
            return (true, default);
        }

        using CancellationTokenSource stepStop =
            CancellationTokenSource.CreateLinkedTokenSource(operationCancellationToken);
        bool pausedByControl = false;
        Task<T> task = operation(stepStop.Token);
        try
        {
            while (!task.IsCompleted && !stepStop.IsCancellationRequested)
            {
                if (controls.HasPendingPause())
                {
                    pausedByControl = true;
                    await stepStop.CancelAsync().ConfigureAwait(false);
                    break;
                }
                try
                {
                    await task.WaitAsync(TimeSpan.FromMilliseconds(50), CancellationToken.None)
                        .ConfigureAwait(false);
                }
                catch (TimeoutException)
                {
                }
            }
        }
        catch
        {
            try
            {
                await Task.WhenAll(task, stepStop.CancelAsync()).ConfigureAwait(false);
            }
            catch
            {
                // Observe the task without replacing the supervision failure.
            }
            throw;
        }

        try
        {
            return (false, await task.ConfigureAwait(false));
        }
        catch (OperationCanceledException) when (
            pausedByControl && !operationCancellationToken.IsCancellationRequested
        )
        {
            StopForControl("pause");
            return (true, default);
        }
    }

    private JsonObject PausedResult() => Success(
        new { lifecycle = "paused", stopReason = reason },
        "Owned native processes settled; work is retained."
    );

    private sealed class PeerProgress
    {
        public DateTimeOffset LastWork { get; set; }
        public DateTimeOffset LastNudge { get; set; }
        public bool InitialPlanner { get; set; }
        public bool Bootstrap { get; set; }
    }

    private sealed class TurnProgress
    {
        public bool ModelAttempted { get; set; }
        public bool NativeCompleted { get; set; }
        public bool Acknowledged { get; set; }
        public string StopReason { get; set; } = "unknown";
    }

    private async Task WorkerLoopAsync(string id, INativeWorker worker)
    {
        PeerProgress peer = new()
        {
            LastWork = database.Now,
            LastNudge = database.Now,
            InitialPlanner = planning && id == "worker1",
            Bootstrap = !database.Meta.IsBootstrapConfirmed(worker.SessionId),
        };
        while (!Admission.IsClosed)
        {
            if (planning && !released && id != "worker1")
            {
                await Task.Delay(PollInterval, database.TimeProvider, workerStop)
                    .ConfigureAwait(false);
                continue;
            }

            DeliveryBatch? batch;
            try
            {
                batch = collaboration.ClaimBatch(id, peer.Bootstrap, Admission);
            }
            catch (Exception exception)
            {
                ObserveWorkerFailure(exception);
                throw;
            }
            if (batch is null)
            {
                if (database.Now - peer.LastWork >= TimeSpan.FromSeconds(600)
                    && database.Now - peer.LastNudge >= TimeSpan.FromSeconds(600))
                {
                    collaboration.QueueKickoff(id, WorkerInstructions.Idle(CurrentPhase()));
                    peer.LastNudge = database.Now;
                }

                await Task.Delay(PollInterval, database.TimeProvider, workerStop)
                    .ConfigureAwait(false);
            }
            else if (!await RunWorkerTurnAsync(id, worker, batch, peer).ConfigureAwait(false))
            {
                return;
            }
        }
    }

    private async Task<bool> RunWorkerTurnAsync(
        string id, INativeWorker worker, DeliveryBatch batch, PeerProgress peer
    )
    {
        TurnProgress turn = new();
        try
        {
            await DispatchAndAcknowledgeTurnAsync(id, worker, batch, peer, turn)
                .ConfigureAwait(false);
            return true;
        }
        catch (OperationCanceledException) when (
            workerStop.IsCancellationRequested || (!turn.ModelAttempted && Admission.IsClosed)
        )
        {
            if (!turn.NativeCompleted)
            {
                collaboration.FailBatch(
                    batch.Id,
                    "Turn interrupted by coordinator stop; durable replay may repeat actions."
                );
            }
            if (!workerStop.IsCancellationRequested)
            {
                return false;
            }
            throw;
        }
        catch (Exception exception)
        {
            ObserveWorkerFailure(exception);
            exception.Data["liteagensh.batchId"] = batch.Id;
            exception.Data["liteagensh.phase"] = turn.NativeCompleted
                ? turn.Acknowledged ? "credit-verification" : "acknowledgement"
                : turn.ModelAttempted ? "native-turn" : "composition";
            if (turn.NativeCompleted)
            {
                if (turn.Acknowledged)
                {
                    throw;
                }
                throw RetainUnacknowledgedTurn(batch.Id, id, turn.StopReason, exception);
            }

            collaboration.FailBatch(batch.Id, exception.Message);
            if (exception is OperationException { Code: "admission_closed" }
                && Admission.IsClosed && !workerStop.IsCancellationRequested)
            {
                return false;
            }
            if (!turn.ModelAttempted && IsExpected(exception))
            {
                return true;
            }

            // A transport or accounting failure is not permission to issue another model call.
            throw;
        }
    }

    private async Task DispatchAndAcknowledgeTurnAsync(
        string id, INativeWorker worker, DeliveryBatch batch, PeerProgress peer, TurnProgress turn
    )
    {
        string changes = peer.Bootstrap
            ? (await git.InvokeAsync(id, "change_list", new(), Admission.Token)
                .ConfigureAwait(false)).ToJsonString()
            : "";
        string text = WorkerInstructions.Compose(
            database, id, changes, batch.Prompt, peer.InitialPlanner, peer.Bootstrap
        );
        Admission.ThrowIfClosed();
        turn.ModelAttempted = true;
        credits.RequirePromptAllowance(
            database.RunId, worker.ReservationId, database.Configuration.RunAiCreditAllowance
        );
        // Keep the worker-bound request alive through durable ACK and verification.
        using NativeRequest request = worker.BeginRequest(workerStop);
        JsonObject result = await worker.PromptAsync(text, request.Token, Admission)
            .ConfigureAwait(false);
        turn.NativeCompleted = true;
        peer.InitialPlanner = false;
        string? stopReason = result["stopReason"]?.GetValue<string>();
        turn.StopReason = stopReason ?? "unknown";
        if (peer.Bootstrap)
        {
            database.Meta.ConfirmBootstrap(worker.SessionId);
            peer.Bootstrap = false;
        }
        RecordCompletedTurn(batch.Id, id, turn.StopReason);
        collaboration.CompleteBatch(batch.Id);
        MarkAcknowledged(batch.Id);
        turn.Acknowledged = true;
        // Unknown usage after a completed model response forbids the next model call.
        await worker.VerifyCreditsAsync(request.Token).ConfigureAwait(false);
        peer.LastWork = database.Now;
        if (stopReason is not ("end_turn" or "max_turn_requests"))
        {
            throw new OperationException(
                stopReason == "max_tokens" ? "native_limit" : "native_stopped",
                $"Native worker stopped with {stopReason ?? "unknown stop reason"}."
            );
        }
    }

    private void ObserveWorkerFailure(Exception exception)
    {
        if (exception.Data["liteagensh.nativeFailureObserved"] is not long)
        {
            exception.Data["liteagensh.nativeFailureCancelled"] =
                workerStop.IsCancellationRequested;
            exception.Data["liteagensh.nativeFailureObserved"] = Stopwatch.GetTimestamp();
        }
    }

    private OperationException RetainUnacknowledgedTurn(
        long batchId, string worker, string stopReason, Exception exception
    )
    {
        // Never schedule a completed model action again due to acknowledgement failure.
        try
        {
            RecordCompletedTurn(batchId, worker, stopReason);
        }
        catch (Exception persistenceFailure) when (IsExpected(persistenceFailure))
        {
            database.Meta.RecordCompletionAmbiguity(batchId);
        }

        OperationException failure = new(
            "post_delivery_failed",
            $"Completed native turn retained without retry: {exception.Message}",
            exception
        );
        failure.Data["liteagensh.nativeFailureObserved"] =
            exception.Data["liteagensh.nativeFailureObserved"];
        failure.Data["liteagensh.nativeFailureCancelled"] =
            exception.Data["liteagensh.nativeFailureCancelled"];
        return failure;
    }

    private CreditReservation Reserve(string worker, int remainingWorkers)
    {
        if (database.Configuration.CreditAllocation == "shared")
        {
            return credits.Reserve(
                database.RunId,
                worker,
                0,
                database.Configuration.RunAiCreditAllowance,
                database.CreateSdkEvidencePath(worker),
                shared: true
            );
        }

        return credits.ReservePerSessionShare(
            database.RunId,
            worker,
            remainingWorkers,
            database.Configuration.RunAiCreditAllowance,
            database.CreateSdkEvidencePath(worker)
        );
    }

    private (long Segment, DateTimeOffset Deadline) BeginSegment(string mode)
    {
        using SqliteConnection connection = database.Open();
        using SqliteTransactionScope transaction = Sql.BeginTransaction(connection);
        string nowText = database.Now.ToString("O");
        DateTimeOffset deadline = database.Now.AddSeconds(database.Configuration.DurationSeconds);
        if (mode == "reconnect")
        {
            string? existing =
                Sql.Scalar(
                    connection,
                    "SELECT deadline FROM coordinator_segments WHERE ended_at IS NULL "
                        + "ORDER BY id DESC LIMIT 1;",
                    transaction
                ) as string;
            if (existing is not null)
            {
                deadline = DateTimeOffset.Parse(existing, CultureInfo.InvariantCulture);
            }

            Sql.Execute(
                connection,
                """
                UPDATE coordinator_segments SET state='interrupted',ended_at=$time,
                    stop_reason='crash-reconnect'
                WHERE ended_at IS NULL;
                """,
                transaction,
                ("$time", nowText)
            );
        }
        else if (
            Sql.Scalar(
                connection,
                "SELECT id FROM coordinator_segments WHERE ended_at IS NULL LIMIT 1;",
                transaction
            )
            is not null
        )
        {
            throw new OperationException(
                "conflict",
                "An unfinished coordinator segment is already open."
            );
        }

        Sql.Execute(
            connection,
            """
            INSERT INTO coordinator_segments(mode,state,started_at,deadline)
            VALUES($mode,'starting',$time,$deadline);
            """,
            transaction,
            ("$mode", mode),
            ("$time", nowText),
            ("$deadline", deadline.ToString("O"))
        );
        long id = (long)(
            Sql.Scalar(connection, "SELECT last_insert_rowid();", transaction)
            ?? throw new InvalidOperationException()
        );
        if (mode == "resume")
        {
            CollaborationOperations.ReopenOwnerClosedRoot(connection, transaction, nowText);
        }

        Sql.SetMetadata(connection, transaction, RunMetadata.LifecycleKey, "starting");
        Sql.SetMetadata(
            connection,
            transaction,
            RunMetadata.DeadlineKey,
            deadline.ToString("O")
        );
        Sql.SetMetadata(
            connection,
            transaction,
            RunMetadata.DeadlineDrainKey(id),
            BuildDeadlineDrainState(deadline, "not-entered", null).ToJsonString()
        );
        Sql.Execute(
            connection,
            "UPDATE meta SET value='false' WHERE key=$key;",
            transaction,
            ("$key", RunMetadata.GitFinalizedKey)
        );
        transaction.Commit();
        return (id, deadline);
    }

    private void RecordDeadlineDrain(
        long segment, DateTimeOffset deadline, string outcome, string? reason,
        OperationException? accountingStop = null
    )
    {
        database.Meta.WriteJson(
            RunMetadata.DeadlineDrainKey(segment),
            BuildDeadlineDrainState(deadline, outcome, reason, accountingStop)
        );
    }

    private static JsonObject BuildDeadlineDrainState(
        DateTimeOffset deadline,
        string outcome,
        string? reason,
        OperationException? accountingStop = null
    )
    {
        JsonObject state = new()
        {
            ["workDeadline"] = deadline.ToString("O"),
            ["drainDeadline"] = deadline.AddSeconds(DeadlineDrainSeconds).ToString("O"),
            ["outcome"] = outcome,
            ["stopReason"] = reason,
        };
        if (accountingStop is not null)
        {
            state["accountingStop"] = new JsonObject
            {
                ["code"] = accountingStop.Code,
                ["message"] = accountingStop.Message,
            };
        }

        return state;
    }

    private void RecordCompletedTurn(long batchId, string worker, string stopReason)
    {
        using SqliteConnection connection = database.Open();
        Sql.Execute(
            connection,
            """
            INSERT INTO coordinator_turns(batch_id,worker,state,stop_reason,completed_at)
            VALUES($batch,$worker,'native-complete',$reason,$time)
            ON CONFLICT(batch_id) DO UPDATE SET state='native-complete',
                stop_reason=excluded.stop_reason,completed_at=excluded.completed_at;
            """,
            null,
            ("$batch", batchId),
            ("$worker", worker),
            ("$reason", stopReason),
            ("$time", database.Now.ToString("O"))
        );
    }

    private void MarkAcknowledged(long batchId)
    {
        using SqliteConnection connection = database.Open();
        Sql.Execute(
            connection,
            "UPDATE coordinator_turns SET state='acknowledged' WHERE batch_id=$id;",
            null,
            ("$id", batchId)
        );
    }

    private void RecoverCompletedTurns()
    {
        List<long> ids = [];
        using (SqliteConnection connection = database.Open())
        using (
            SqliteCommand command = Sql.Command(
                connection,
                "SELECT batch_id FROM coordinator_turns WHERE state='native-complete';"
            )
        )
        using (ObservedSqliteReader reader = Sql.Reader(command))
        {
            while (reader.Read())
            {
                ids.Add(reader.GetInt64(0));
            }
        }

        foreach (long id in ids)
        {
            collaboration.CompleteBatch(id);
            MarkAcknowledged(id);
        }
    }

    private string CurrentPhase()
    {
        string? text = database.ReadMetadata(RunMetadata.DeadlineKey);
        double remaining = text is null
            ? 0
            : (
                DateTimeOffset.Parse(text, CultureInfo.InvariantCulture) - database.Now
            ).TotalSeconds;
        int deliveryWindow = database.Configuration.DurationSeconds > 2700 ? 2700 : 300;
        return remaining <= deliveryWindow ? "delivery" : "development";
    }

    private void PublishReminders(DateTimeOffset deadline, HashSet<int> sent)
    {
        foreach (int beforeEnd in new[] { 900 })
        {
            if (
                beforeEnd < database.Configuration.DurationSeconds
                && (deadline - database.Now).TotalSeconds <= beforeEnd
                && sent.Add(beforeEnd)
            )
            {
                collaboration.Invoke(
                    "owner",
                    "message_send",
                    new()
                    {
                        ["recipient"] = "team",
                        ["body"] =
                            $"T-{beforeEnd / 60}m: The run will end "
                            + "and retain a checked local delivery. "
                            + "Integrate what is ready, keep the integration branch passing, "
                            + "avoid new large pieces, and record unfinished work as tasks. "
                            + "Final checks, not task closure, determine delivery success.",
                    }
                );
            }
        }
    }

    private void PublishCreditWarning()
    {
        if (creditWarningSent)
        {
            return;
        }
        decimal actual = credits.ReadRunActualReported(database.RunId);
        if (actual < database.Configuration.RunAiCreditAllowance * 0.9m)
        {
            return;
        }

        JsonObject result = collaboration.Invoke(
            "owner",
            "message_send",
            new()
            {
                ["recipient"] = "team",
                ["body"] =
                    "At least 90% of the run's native-reported AI Credits have been spent. "
                    + "Wrap up checked work, retain commits and record open "
                    + "tasks/board findings. "
                    + "Do not start large new pieces. Native soft limits may "
                    + "overshoot one in-flight response.",
                ["urgent"] = true,
            }
        );
        if (result["ok"]?.GetValue<bool>() != true)
        {
            throw new OperationException(
                "lifecycle_alert_failed",
                result["message"]?.GetValue<string>() ?? "Credit warning could not be recorded."
            );
        }

        database.Meta.MarkCreditWarningSent();
        creditWarningSent = true;
    }

}
