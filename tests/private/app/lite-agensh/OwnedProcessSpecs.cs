using System.Diagnostics;
using System.Globalization;
using System.Text.Json.Nodes;
using LiteAgensh.Tests.Support;
using Microsoft.Extensions.Time.Testing;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace LiteAgensh.Tests;

[TestClass]
public sealed class OwnedProcessSpecs(TestContext context)
{
    public static IEnumerable<(string? Frame, string Kind)> InvalidReadyFrames =>
    [
        (null, "eof"),
        ("", "empty"),
        ("private-local-frame", "malformed"),
        (new string('x', 129) + "private-local-frame", "oversized"),
    ];

    [TestMethod]
    [DynamicData(nameof(InvalidReadyFrames))]
    public void InvalidReadinessClassifiesTheFrameWithoutExportingItsContent(
        string? frame, string kind
    )
    {
        context.CancellationToken.ThrowIfCancellationRequested();
        InvalidOperationException error = Assert.ThrowsExactly<InvalidOperationException>(
            () => OwnedChildControl.ParseReady(frame)
        );
        JsonNode details = JsonNode.Parse(error.Message[error.Message.IndexOf('{')..])!;

        Assert.AreEqual(kind, details["frameKind"]!.GetValue<string>());
        Assert.AreEqual(frame?.Length, details["frameLength"]?.GetValue<int>());
        Assert.AreEqual("ready-frame", details["stage"]!.GetValue<string>());
        Assert.IsLessThan(512, error.Message.Length);
        if (!string.IsNullOrEmpty(frame))
        {
            Assert.DoesNotContain(frame, error.Message);
        }
    }

    [TestMethod]
    [Timeout(120_000, CooperativeCancellation = true)]
    public async Task FailedReleaseRetainsItsErrorWhileRemainingOwnedDisposalStillCompletes()
    {
        await using DirectChildScope scope = new(context);
        Process owned = scope.Control.StartDirect();
        OwnedChildIdentity ready = await scope
            .Control.ReadReadyAsync(context.CancellationToken)
            .ConfigureAwait(false);
        AssertOriginalIdentity(owned, ready);
        using Process observer = OpenObservedOwnedChild(ready.Pid, ready.BirthTicks);
        Assert.IsFalse(observer.HasExited);

        using CancellationTokenSource releaseCancellation = new();
        await releaseCancellation.CancelAsync().ConfigureAwait(false);
        OperationCanceledException primary = await Assert
            .ThrowsAsync<OperationCanceledException>(async () =>
                await scope.Control.ReleaseAsync(releaseCancellation.Token).ConfigureAwait(false)
            )
            .ConfigureAwait(false);
        Assert.AreEqual(releaseCancellation.Token, primary.CancellationToken);

        await scope.DisposeControlAsync().ConfigureAwait(false);

        Assert.IsTrue(observer.HasExited);
        Assert.ThrowsExactly<InvalidOperationException>(
            () => _ = owned.Id,
            "The directly owned Process must be disposed despite the preceding release error."
        );
        scope.Complete();
    }

    [TestMethod]
    [Timeout(120_000, CooperativeCancellation = true)]
    [DataRow(0)]
    [DataRow(7)]
    [TestCategory("CancelSettleOnlyOwnedChildren")]
    public async Task BuiltChildDeclaredExitQualifiesTheRealCommandBoundary(
        int exitCode
    )
    {
        using OwnedProcessState state = new();
        await using RunningCommandScope command = new(
            context,
            state,
            ["exit", exitCode.ToString(CultureInfo.InvariantCulture)],
            TimeSpan.FromSeconds(120)
        );

        ProcessResult result = await command.Running.ConfigureAwait(false);
        OwnedLease retired = state.Lease();

        Assert.AreEqual(exitCode, result.ExitCode);
        Assert.IsFalse(result.TimedOut);
        Assert.AreEqual(
            $"owned check exit {exitCode}{Environment.NewLine}",
            result.StandardOutput
        );
        Assert.AreEqual(
            exitCode == 0 ? "" : $"owned check failed {exitCode}{Environment.NewLine}",
            result.StandardError
        );
        Assert.IsGreaterThan(0L, retired.Id);
        Assert.IsGreaterThan(0, retired.Pid);
        Assert.IsGreaterThan(0L, retired.BirthTicks);
        Assert.AreEqual("command", retired.Kind);
        Assert.AreEqual("exited", retired.State);
        command.Complete();
    }

    [TestMethod]
    [Timeout(120_000, CooperativeCancellation = true)]
    [TestCategory("CancelSettleOnlyOwnedChildren")]
    public async Task CommandDeadlineRetainsDiagnosticsAndSettlesItsActualOwnedChild()
    {
        FakeTimeProvider clock = new();
        using OwnedProcessState state = new(clock);
        await using OwnedChildControl control = new();
        await using RunningCommandScope command = new(
            context,
            state,
            control.Arguments(role: OwnedChildControl.HeldCandidateCheckRole),
            TimeSpan.FromSeconds(1)
        );
        OwnedChildIdentity ready = await control
            .ReadReadyAsync(command.Running, context.CancellationToken)
            .ConfigureAwait(false);
        OwnedLease live = state.Lease(pid: ready.Pid);
        using Process child = OpenObservedOwnedChild(ready.Pid, ready.BirthTicks);
        Assert.AreEqual(ready.BirthTicks, live.BirthTicks);
        Assert.AreEqual("running", live.State);
        Assert.IsFalse(child.HasExited);

        clock.Advance(TimeSpan.FromSeconds(1));
        ProcessResult result = await command.Running.ConfigureAwait(false);

        Assert.IsTrue(result.TimedOut);
        Assert.IsFalse(command.IsCancellationRequested);
        Assert.AreEqual("exited", state.Lease(id: live.Id).State);
        Assert.IsTrue(child.HasExited);
        Assert.Contains("owned check diagnostic stdout before READY", result.StandardOutput);
        Assert.Contains("owned check diagnostic stderr before READY", result.StandardError);
        Assert.DoesNotContain("owned check exit 0", result.StandardOutput);
        command.Complete();
    }

    [TestMethod]
    [Timeout(120_000, CooperativeCancellation = true)]
    [TestCategory("CancelSettleOnlyOwnedChildren")]
    public async Task CommandCancellationRetainsDrainedDiagnosticsWithoutReturningSuccess()
    {
        using OwnedProcessState state = new();
        await using OwnedChildControl control = new();
        await using RunningCommandScope command = new(
            context,
            state,
            control.Arguments(role: OwnedChildControl.HeldCandidateCheckRole),
            TimeSpan.FromSeconds(120)
        );
        OwnedChildIdentity ready = await control
            .ReadReadyAsync(command.Running, context.CancellationToken)
            .ConfigureAwait(false);
        OwnedLease live = state.Lease(pid: ready.Pid);
        Assert.AreEqual(ready.BirthTicks, live.BirthTicks);
        Assert.AreEqual("command", live.Kind);
        Assert.AreEqual("running", live.State);
        Assert.AreEqual(0, ready.DescendantPid);
        Assert.AreEqual(0L, ready.DescendantBirthTicks);
        using Process child = OpenObservedOwnedChild(ready.Pid, ready.BirthTicks);
        Assert.IsFalse(child.HasExited);

        await command.CancelAsync().ConfigureAwait(false);
        OperationCanceledException error = await Assert
            .ThrowsAsync<OperationCanceledException>(async () =>
                await command.Running.ConfigureAwait(false)
            )
            .ConfigureAwait(false);
        JsonNode evidence = JsonNode.Parse(Assert.IsInstanceOfType<string>(
            error.Data[ProcessRunner.CancellationEvidenceKey]
        ))!;
        OwnedLease retired = state.Lease(id: live.Id);

        Assert.AreEqual(command.Token, error.CancellationToken);
        Assert.AreEqual(live.Id, evidence["leaseId"]!.GetValue<long>());
        Assert.AreEqual("exited", retired.State);
        Assert.AreEqual(live.Pid, retired.Pid);
        Assert.AreEqual(live.BirthTicks, retired.BirthTicks);
        Assert.IsTrue(child.HasExited);
        Assert.IsTrue(evidence["stdout"]!["complete"]!.GetValue<bool>());
        Assert.IsTrue(evidence["stderr"]!["complete"]!.GetValue<bool>());
        Assert.Contains(
            "owned check diagnostic stdout before READY",
            evidence["stdout"]!["text"]!.GetValue<string>()
        );
        Assert.Contains(
            "owned check diagnostic stderr before READY",
            evidence["stderr"]!["text"]!.GetValue<string>()
        );
        command.Complete();
    }

    [TestMethod]
    [Timeout(120_000, CooperativeCancellation = true)]
    [TestCategory("CancelSettleOnlyOwnedChildren")]
    public async Task CommandCancellationWithAnUnsettledInheritedPipeDurablyBlocksReplacement()
    {
        // A case timeout must not make the runner kill a root that still owns the holder.
        using CancellationTokenSource entry = CancellationTokenSource.CreateLinkedTokenSource(
            context.CancellationToken
        );
        entry.CancelAfter(TimeSpan.FromSeconds(30));
        await using OutputHandoffScope handoff = new(context);
        Task<ProcessResult> command = handoff.Launch();

        OwnedChildIdentity rootAtStart = await handoff
            .StartControl.ReadReadyAsync(command, entry.Token)
            .ConfigureAwait(false);
        OwnedLease live = handoff.State.Lease(pid: rootAtStart.Pid);
        Assert.AreEqual(rootAtStart.BirthTicks, live.BirthTicks);
        Assert.AreEqual("command", live.Kind);
        Assert.AreEqual("running", live.State);
        Assert.AreEqual(0, rootAtStart.DescendantPid);
        Process root = handoff.ObserveRoot(rootAtStart);
        Assert.IsFalse(root.HasExited);
        if (command.IsCompleted)
        {
            await command.ConfigureAwait(false);
            Assert.Fail("The command completed before holder startup was granted.");
        }

        // This RELEASE grants only holder startup, and a failed write may still deliver it.
        await handoff.ReleaseHolderStartAsync(entry.Token).ConfigureAwait(false);
        OwnedChildIdentity rootReady = await handoff
            .RootControl.ReadReadyAsync(command, entry.Token)
            .ConfigureAwait(false);
        Assert.AreEqual(rootAtStart.Pid, rootReady.Pid);
        Assert.AreEqual(rootAtStart.BirthTicks, rootReady.BirthTicks);
        OwnedChildIdentity holderReady = await handoff
            .HolderControl.ReadReadyAsync(command, entry.Token)
            .ConfigureAwait(false);
        Assert.AreEqual(rootReady.DescendantPid, holderReady.Pid);
        Assert.AreEqual(rootReady.DescendantBirthTicks, holderReady.BirthTicks);
        Assert.AreNotEqual(rootReady.Pid, holderReady.Pid);
        Assert.AreEqual(0, holderReady.DescendantPid);
        Process holder = handoff.ObserveHolder(holderReady);
        Assert.IsFalse(holder.HasExited);
        Assert.IsFalse(root.HasExited);

        await handoff.RootControl.ReleaseAsync(entry.Token).ConfigureAwait(false);
        await JoinExitAsync(root, entry.Token).ConfigureAwait(false);
        Assert.IsFalse(holder.HasExited);
        Assert.IsFalse(command.IsCompleted, "The exited root must still have output to drain.");
        Assert.AreEqual(live, handoff.State.Lease(id: live.Id));

        // Leave the command clock alone; only the caller stop and real drain bound apply.
        await handoff.CallerStop.CancelAsync().ConfigureAwait(false);
        InvalidOperationException postStopFailure = await Assert
            .ThrowsExactlyAsync<InvalidOperationException>(async () =>
                await command
                    .WaitAsync(TimeSpan.FromSeconds(15), context.CancellationToken)
                    .ConfigureAwait(false)
            )
            .ConfigureAwait(false);
        handoff.MarkPostStopObserved();
        Assert.AreEqual(
            "Owned command or inherited pipes did not settle; replacement is blocked.",
            postStopFailure.Message
        );
        Assert.AreEqual(typeof(TimeoutException), postStopFailure.InnerException?.GetType());
        OwnedLease unresolved = handoff.State.Lease(id: live.Id);
        Assert.AreEqual(
            live with { State = "unresolved" },
            unresolved,
            "The surviving runner must durably mark the exact registered command."
        );

        using CancellationTokenSource admission = new(TimeSpan.FromSeconds(5));
        InvalidOperationException refusal = await Assert
            .ThrowsExactlyAsync<InvalidOperationException>(async () =>
                await handoff
                    .State.Runner.ResolveSurvivorsAsync(admission.Token)
                    .ConfigureAwait(false)
            )
            .ConfigureAwait(false);
        Assert.AreEqual(
            $"Process lease {live.Id} needs explicit ownership resolution.",
            refusal.Message
        );
        Assert.AreEqual(unresolved, handoff.State.Lease(id: live.Id));
        Assert.IsFalse(holder.HasExited);

        CollaborationOperations collaboration = new(handoff.State.Database);
        LightweightGitWorkspace workspace = new(handoff.State.Database, collaboration);
        Coordinator coordinator = new(
            handoff.State.Database,
            collaboration,
            workspace,
            nativeWorkerFactory: null
        );
        JsonObject pendingPause = GitScenarioState.Data(coordinator.RequestControl("pause"));
        JsonObject finalizeRefusal = await coordinator
            .FinalizeAsync(entry.Token)
            .ConfigureAwait(false);
        Assert.IsFalse(finalizeRefusal["ok"]!.GetValue<bool>());
        Assert.AreEqual("unavailable", finalizeRefusal["code"]!.GetValue<string>());
        Assert.AreEqual(
            "unresolved",
            GitScenarioState.Data(coordinator.Status())["lifecycle"]!.GetValue<string>()
        );
        Assert.AreEqual(
            pendingPause["requestId"]!.GetValue<long>(),
            GitScenarioState.Data(coordinator.RequestControl("pause"))["requestId"]!
                .GetValue<long>()
        );

        await handoff.HolderControl.ReleaseAsync(context.CancellationToken).ConfigureAwait(false);
        await JoinExitAsync(holder, context.CancellationToken).ConfigureAwait(false);
        handoff.Complete();
    }

    [TestMethod]
    [Timeout(120_000, CooperativeCancellation = true)]
    [TestCategory("CancelSettleOnlyOwnedChildren")]
    public async Task CancellationSettlesTheCorrelatedOwnedTreeAndPreservesAnAdjacentOwnedSentinel()
    {
        using OwnedProcessState state = new();
        await using OwnedChildControl rootControl = new();
        await using OwnedChildControl leafControl = new();
        await using OwnedChildControl sentinelControl = new();
        Process sentinel = sentinelControl.StartDirect();
        OwnedChildIdentity sentinelReady = await sentinelControl
            .ReadReadyAsync(context.CancellationToken)
            .ConfigureAwait(false);
        AssertOriginalIdentity(sentinel, sentinelReady);
        await using RunningCommandScope command = new(
            context,
            state,
            rootControl.Arguments(descendantPipe: leafControl.PipeName),
            TimeSpan.FromSeconds(120)
        );
        OwnedChildIdentity rootReady = await rootControl
            .ReadReadyAsync(command.Running, context.CancellationToken)
            .ConfigureAwait(false);
        OwnedChildIdentity leafReady = await leafControl
            .ReadReadyAsync(command.Running, context.CancellationToken)
            .ConfigureAwait(false);
        Assert.AreEqual(rootReady.DescendantPid, leafReady.Pid);
        Assert.AreEqual(rootReady.DescendantBirthTicks, leafReady.BirthTicks);
        Assert.AreNotEqual(rootReady.Pid, leafReady.Pid);
        OwnedLease lease = state.Lease(pid: rootReady.Pid);
        Assert.AreEqual(rootReady.BirthTicks, lease.BirthTicks);
        Assert.AreEqual("running", lease.State);
        using Process root = OpenObservedOwnedChild(rootReady.Pid, rootReady.BirthTicks);
        using Process leaf = OpenObservedOwnedChild(leafReady.Pid, leafReady.BirthTicks);
        Assert.IsFalse(root.HasExited);
        Assert.IsFalse(leaf.HasExited);

        await command.CancelAsync().ConfigureAwait(false);
        OperationCanceledException failure = await Assert
            .ThrowsAsync<OperationCanceledException>(async () =>
                await command.Running.ConfigureAwait(false)
            )
            .ConfigureAwait(false);

        Assert.AreEqual(command.Token, failure.CancellationToken);
        Assert.IsTrue(
            root.HasExited,
            "RunAsync must settle the root before returning cancellation."
        );
        Assert.IsTrue(
            leaf.HasExited,
            "RunAsync must settle the owned descendant before returning cancellation."
        );
        Assert.AreEqual("exited", state.Lease(id: lease.Id).State);
        await RequireExitAsync(root).ConfigureAwait(false);
        await RequireExitAsync(leaf).ConfigureAwait(false);
        Assert.IsTrue(command.Running.IsCanceled);
        await state
            .Runner.ResolveSurvivorsAsync(context.CancellationToken)
            .ConfigureAwait(false);
        Assert.IsFalse(sentinel.HasExited);
        AssertOriginalIdentity(sentinel, sentinelReady);
        await sentinelControl.ReleaseAsync(context.CancellationToken).ConfigureAwait(false);
        await RequireExitAsync(sentinel).ConfigureAwait(false);
        Assert.AreEqual(0, sentinel.ExitCode);
        command.Complete();
    }

    [TestMethod]
    [TestCategory("LeaseIdentityBeforeReplacement")]
    public async Task ObservedOwnedExitRetiresTheOldLeaseWithoutTouchingTheLiveSentinel()
    {
        using OwnedProcessState state = new();
        await using OwnedChildControl oldControl = new();
        await using OwnedChildControl sentinelControl = new();
        Process old = oldControl.StartDirect();
        OwnedChildIdentity oldReady = await oldControl
            .ReadReadyAsync(context.CancellationToken)
            .ConfigureAwait(false);
        AssertOriginalIdentity(old, oldReady);
        long id = state.Runner.Register(old, "observed-old-command");
        Process sentinel = sentinelControl.StartDirect();
        OwnedChildIdentity sentinelReady = await sentinelControl
            .ReadReadyAsync(context.CancellationToken)
            .ConfigureAwait(false);
        AssertOriginalIdentity(sentinel, sentinelReady);
        await oldControl.ReleaseAsync(context.CancellationToken).ConfigureAwait(false);
        await RequireExitAsync(old).ConfigureAwait(false);
        Assert.AreEqual("running", state.Lease(id: id).State);

        await state.Runner.ResolveSurvivorsAsync(context.CancellationToken).ConfigureAwait(false);

        Assert.AreEqual("exited", state.Lease(id: id).State);
        Assert.AreEqual(oldReady.Pid, state.Lease(id: id).Pid);
        Assert.AreEqual(oldReady.BirthTicks, state.Lease(id: id).BirthTicks);
        Assert.IsFalse(sentinel.HasExited);
        AssertOriginalIdentity(sentinel, sentinelReady);
        await sentinelControl.ReleaseAsync(context.CancellationToken).ConfigureAwait(false);
        await RequireExitAsync(sentinel).ConfigureAwait(false);
        Assert.AreEqual(0, sentinel.ExitCode);
    }

    [TestMethod]
    [TestCategory("LeaseIdentityBeforeReplacement")]
    public async Task BirthMismatchRetiresOnlyTheRecordedOldIdentityAndPreservesBothLiveChildren()
    {
        using OwnedProcessState state = new();
        await using OwnedChildControl currentControl = new();
        await using OwnedChildControl sentinelControl = new();
        Process current = currentControl.StartDirect();
        OwnedChildIdentity currentReady = await currentControl
            .ReadReadyAsync(context.CancellationToken)
            .ConfigureAwait(false);
        AssertOriginalIdentity(current, currentReady);
        long id = state.Runner.Register(current, "old-birth-identity");
        long oldBirth = currentReady.BirthTicks - 1;
        state.Execute(
            "UPDATE process_leases SET start_ticks=$birth WHERE id=$id;",
            ("$birth", oldBirth),
            ("$id", id)
        );
        Process sentinel = sentinelControl.StartDirect();
        OwnedChildIdentity sentinelReady = await sentinelControl
            .ReadReadyAsync(context.CancellationToken)
            .ConfigureAwait(false);
        AssertOriginalIdentity(sentinel, sentinelReady);

        await state.Runner.ResolveSurvivorsAsync(context.CancellationToken).ConfigureAwait(false);

        OwnedLease retired = state.Lease(id: id);
        Assert.AreEqual("exited", retired.State);
        Assert.AreEqual(currentReady.Pid, retired.Pid);
        Assert.AreEqual(oldBirth, retired.BirthTicks);
        Assert.IsFalse(current.HasExited);
        Assert.IsFalse(sentinel.HasExited);
        AssertOriginalIdentity(current, currentReady);
        AssertOriginalIdentity(sentinel, sentinelReady);
        await currentControl.ReleaseAsync(context.CancellationToken).ConfigureAwait(false);
        await sentinelControl.ReleaseAsync(context.CancellationToken).ConfigureAwait(false);
        await RequireExitAsync(current).ConfigureAwait(false);
        await RequireExitAsync(sentinel).ConfigureAwait(false);
        Assert.AreEqual(0, current.ExitCode);
        Assert.AreEqual(0, sentinel.ExitCode);
    }

    [TestMethod]
    [TestCategory("LeaseIdentityBeforeReplacement")]
    public async Task ExactOwnedLiveTreeIsSettledBeforeItsLeaseRetiresWithoutBroadCleanup()
    {
        using OwnedProcessState state = new();
        await using OwnedChildControl rootControl = new();
        await using OwnedChildControl leafControl = new();
        await using OwnedChildControl sentinelControl = new();
        Process root = rootControl.StartDirect(descendantPipe: leafControl.PipeName);
        OwnedChildIdentity rootReady = await rootControl
            .ReadReadyAsync(context.CancellationToken)
            .ConfigureAwait(false);
        OwnedChildIdentity leafReady = await leafControl
            .ReadReadyAsync(context.CancellationToken)
            .ConfigureAwait(false);
        AssertOriginalIdentity(root, rootReady);
        Assert.AreEqual(rootReady.DescendantPid, leafReady.Pid);
        Assert.AreEqual(rootReady.DescendantBirthTicks, leafReady.BirthTicks);
        using Process leaf = OpenObservedOwnedChild(leafReady.Pid, leafReady.BirthTicks);
        long id = state.Runner.Register(root, "verified-survivor");
        Process sentinel = sentinelControl.StartDirect();
        OwnedChildIdentity sentinelReady = await sentinelControl
            .ReadReadyAsync(context.CancellationToken)
            .ConfigureAwait(false);
        AssertOriginalIdentity(sentinel, sentinelReady);
        Assert.AreEqual("running", state.Lease(id: id).State);
        Assert.IsFalse(root.HasExited);
        Assert.IsFalse(leaf.HasExited);

        await state.Runner.ResolveSurvivorsAsync(context.CancellationToken).ConfigureAwait(false);

        Assert.IsTrue(
            root.HasExited,
            "Successful survivor resolution must already have settled the root."
        );
        Assert.IsTrue(
            leaf.HasExited,
            "Successful survivor resolution must not leave the owned descendant live."
        );
        await RequireExitAsync(root).ConfigureAwait(false);
        await RequireExitAsync(leaf).ConfigureAwait(false);
        OwnedLease retired = state.Lease(id: id);
        Assert.AreEqual("exited", retired.State);
        Assert.AreEqual(rootReady.Pid, retired.Pid);
        Assert.AreEqual(rootReady.BirthTicks, retired.BirthTicks);
        Assert.IsFalse(sentinel.HasExited);
        AssertOriginalIdentity(sentinel, sentinelReady);
        await state.Runner.ResolveSurvivorsAsync(context.CancellationToken).ConfigureAwait(false);
        Assert.AreEqual(retired, state.Lease(id: id));
        Assert.IsFalse(sentinel.HasExited);
        await sentinelControl.ReleaseAsync(context.CancellationToken).ConfigureAwait(false);
        await RequireExitAsync(sentinel).ConfigureAwait(false);
        Assert.AreEqual(0, sentinel.ExitCode);
    }

    [TestMethod]
    [TestCategory("LeaseIdentityBeforeReplacement")]
    public async Task DurableUnresolvedOwnershipRejectsBeforeAnyProcessIdentityInspection()
    {
        using OwnedProcessState state = new();
        // The unresolved marker must reject before any identity lookup. A negative
        // non-host identity keeps this DB-only case incapable of targeting a host PID.
        state.Execute(
            """
            INSERT INTO process_leases(pid,start_ticks,kind,state,created_at)
            VALUES(-1,1,'unsettled-command','unresolved','2026-01-01T00:00:00Z');
            """
        );
        OwnedLease before = state.Lease(pid: -1);

        InvalidOperationException failure = await Assert
            .ThrowsExactlyAsync<InvalidOperationException>(async () =>
                await state
                    .Runner.ResolveSurvivorsAsync(context.CancellationToken)
                    .ConfigureAwait(false)
            )
            .ConfigureAwait(false);

        Assert.AreEqual(
            $"Process lease {before.Id} needs explicit ownership resolution.",
            failure.Message
        );
        Assert.AreEqual(before, state.Lease(id: before.Id));
        Assert.AreEqual("unresolved", state.Lease(id: before.Id).State);

        // Resolver rejection alone would miss removal of the application's separate
        // writer-admission guard. This must fail before Initialize launches any Git
        // command, so the same SQLite-only arrangement is sufficient.
        GitOperations operations = new(state.Database, new CollaborationOperations(state.Database));
        OperationException admission = await Assert
            .ThrowsExactlyAsync<OperationException>(async () =>
                await operations.InitializeAsync(context.CancellationToken).ConfigureAwait(false)
            )
            .ConfigureAwait(false);
        Assert.AreEqual("unavailable", admission.Code);
        Assert.AreEqual(
            "An unresolved owned process blocks further coordinator execution.",
            admission.Message
        );
        Assert.AreEqual(before, state.Lease(id: before.Id));
        Assert.IsNull(state.Database.ReadMetadata("git:initialized"));
        Assert.IsFalse(
            Directory.Exists(Path.Combine(state.Database.Configuration.RepositoryPath, ".git"))
        );
    }

    [TestMethod]
    [TestCategory("LeaseIdentityBeforeReplacement")]
    public async Task CancelledResolutionKeepsKnownLiveOwnershipPendingUntilActualExit()
    {
        using OwnedProcessState state = new();
        await using OwnedChildControl control = new();
        Process child = control.StartDirect();
        OwnedChildIdentity ready = await control
            .ReadReadyAsync(context.CancellationToken)
            .ConfigureAwait(false);
        AssertOriginalIdentity(child, ready);
        long id = state.Runner.Register(child, "pending-resolution");
        OwnedLease live = state.Lease(id: id);
        using CancellationTokenSource cancellation = new();
        await cancellation.CancelAsync().ConfigureAwait(false);

        OperationCanceledException failure = await Assert
            .ThrowsAsync<OperationCanceledException>(async () =>
                await state.Runner.ResolveSurvivorsAsync(cancellation.Token).ConfigureAwait(false)
            )
            .ConfigureAwait(false);

        Assert.AreEqual(cancellation.Token, failure.CancellationToken);
        Assert.AreEqual(live, state.Lease(id: id));
        Assert.AreEqual("running", state.Lease(id: id).State);
        Assert.IsFalse(child.HasExited);
        await control.ReleaseAsync(context.CancellationToken).ConfigureAwait(false);
        await RequireExitAsync(child).ConfigureAwait(false);
        await state.Runner.ResolveSurvivorsAsync(context.CancellationToken).ConfigureAwait(false);
        Assert.AreEqual("exited", state.Lease(id: id).State);
        Assert.AreEqual(ready.BirthTicks, state.Lease(id: id).BirthTicks);
    }

    private static Process OpenObservedOwnedChild(int pid, long birthTicks)
    {
        Process process = Process.GetProcessById(pid);
        try
        {
            Assert.AreEqual(
                birthTicks,
                process.StartTime.ToUniversalTime().Ticks,
                "Only the child's actual correlated PID/birth identity may be inspected."
            );
            return process;
        }
        catch
        {
            process.Dispose();
            throw;
        }
    }

    private static void AssertOriginalIdentity(Process process, OwnedChildIdentity ready)
    {
        Assert.AreEqual(process.Id, ready.Pid);
        Assert.AreEqual(process.StartTime.ToUniversalTime().Ticks, ready.BirthTicks);
    }

    private static async Task RequireExitAsync(Process process)
    {
        await process
            .WaitForExitAsync(CancellationToken.None)
            .WaitAsync(TimeSpan.FromSeconds(5), CancellationToken.None)
            .ConfigureAwait(false);
        Assert.IsTrue(process.HasExited);
    }

    private static async Task JoinExitAsync(Process process, CancellationToken cancellationToken)
    {
        using CancellationTokenSource exit = CancellationTokenSource.CreateLinkedTokenSource(
            cancellationToken
        );
        exit.CancelAfter(TimeSpan.FromSeconds(5));
        await process.WaitForExitAsync(exit.Token).ConfigureAwait(false);
        Assert.IsTrue(process.HasExited);
    }

    private sealed class DirectChildScope(TestContext context) : IAsyncDisposable
    {
        private bool disposed;
        private bool completed;

        internal OwnedChildControl Control { get; } = new();

        internal async Task DisposeControlAsync()
        {
            disposed = true;
            await Control.DisposeAsync().ConfigureAwait(false);
        }

        internal void Complete() => completed = true;

        public async ValueTask DisposeAsync()
        {
            if (disposed)
            {
                return;
            }
            try
            {
                disposed = true;
                await Control.DisposeAsync().ConfigureAwait(false);
            }
            catch (Exception secondary) when (!completed)
            {
                context.WriteLine("Secondary owned child cleanup failure: {0}", secondary);
            }
        }
    }

    private sealed class RunningCommandScope : IAsyncDisposable
    {
        private readonly TestContext context;
        private readonly CancellationTokenSource cancellation;
        private bool completed;

        internal RunningCommandScope(
            TestContext context,
            OwnedProcessState state,
            IReadOnlyList<string> arguments,
            TimeSpan timeout
        )
        {
            this.context = context;
            cancellation = CancellationTokenSource.CreateLinkedTokenSource(
                context.CancellationToken
            );
            Running = state.Runner.RunAsync(
                OwnedChildControl.ExecutablePath,
                arguments,
                state.WorkingDirectory,
                timeout,
                cancellation.Token
            );
        }

        internal Task<ProcessResult> Running { get; }

        internal CancellationToken Token => cancellation.Token;

        internal bool IsCancellationRequested => cancellation.IsCancellationRequested;

        internal Task CancelAsync() => cancellation.CancelAsync();

        internal void Complete() => completed = true;

        public async ValueTask DisposeAsync()
        {
            try
            {
                await cancellation.CancelAsync().ConfigureAwait(false);
                await Running.ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (cancellation.IsCancellationRequested)
            {
            }
            catch (Exception secondary) when (!completed)
            {
                context.WriteLine(
                    "Secondary command cleanup failure: {0}",
                    Json.ExceptionDetails(secondary).ToJsonString()
                );
            }
            finally
            {
                cancellation.Dispose();
            }
        }
    }

    private sealed class OutputHandoffScope(TestContext context) : IAsyncDisposable
    {
        private Process? root;
        private Process? holder;
        private Task<ProcessResult>? running;
        private bool launchAttempted;
        private bool holderStartAttempted;
        private bool postStopObserved;
        private bool completed;

        internal OwnedProcessState State { get; } = new(new FakeTimeProvider());

        internal CancellationTokenSource CallerStop { get; } = new();

        internal OwnedChildControl StartControl { get; } = new();

        internal OwnedChildControl RootControl { get; } = new();

        internal OwnedChildControl HolderControl { get; } = new();

        internal Task<ProcessResult> Launch()
        {
            launchAttempted = true;
            running = State.Runner.RunAsync(
                OwnedChildControl.ExecutablePath,
                [
                    "output-handoff-root",
                    RootControl.PipeName,
                    "0",
                    HolderControl.PipeName,
                    StartControl.PipeName,
                ],
                State.WorkingDirectory,
                TimeSpan.FromSeconds(120),
                CallerStop.Token
            );
            return running;
        }

        internal Process ObserveRoot(OwnedChildIdentity ready) =>
            root = OpenObservedOwnedChild(ready.Pid, ready.BirthTicks);

        internal Task ReleaseHolderStartAsync(CancellationToken token)
        {
            holderStartAttempted = true;
            return StartControl.ReleaseAsync(token);
        }

        internal Process ObserveHolder(OwnedChildIdentity ready) =>
            holder = OpenObservedOwnedChild(ready.Pid, ready.BirthTicks);

        internal void MarkPostStopObserved() => postStopObserved = true;

        internal void Complete() => completed = true;

        public async ValueTask DisposeAsync()
        {
            List<Exception> cleanupFailures = [];
            // Closing (not releasing) the root channel leaves pre-handoff cleanup to the root.
            await AttemptAsync("start-channel", () => StartControl.DisposeAsync().AsTask())
                .ConfigureAwait(false);
            await AttemptAsync("root-channel", () => RootControl.DisposeAsync().AsTask())
                .ConfigureAwait(false);
            await AttemptAsync("holder-channel", () => HolderControl.DisposeAsync().AsTask())
                .ConfigureAwait(false);

            bool exitsJoined = !launchAttempted
                || (root is not null && (!holderStartAttempted || holder is not null));
            foreach (
                (Process? observer, string role) in new[] { (root, "root"), (holder, "holder") }
            )
            {
                if (observer is not null)
                {
                    exitsJoined &= await AttemptAsync(
                        $"{role}-exit",
                        () => JoinExitAsync(observer, CancellationToken.None)
                    ).ConfigureAwait(false);
                    await AttemptAsync($"{role}-observer-dispose", () => DisposeOwned(observer))
                        .ConfigureAwait(false);
                }
            }

            bool runnerJoined = !launchAttempted || postStopObserved;
            if (running is { } command && !runnerJoined)
            {
                await AttemptAsync(
                    "runner-join",
                    async () =>
                    {
                        try
                        {
                            await command
                                .WaitAsync(TimeSpan.FromSeconds(15), CancellationToken.None)
                                .ConfigureAwait(false);
                        }
                        catch (Exception) when (command.IsCompleted)
                        {
                            runnerJoined = true;
                            try
                            {
                                await command.ConfigureAwait(false);
                            }
                            catch (Exception) when (!completed)
                            {
                            }
                        }
                        runnerJoined = true;
                    }
                ).ConfigureAwait(false);
            }

            await AttemptAsync("caller-stop-dispose", () => DisposeOwned(CallerStop))
                .ConfigureAwait(false);

            if (exitsJoined && runnerJoined)
            {
                await AttemptAsync("state-dispose", () => DisposeOwned(State))
                    .ConfigureAwait(false);
            }
            else
            {
                cleanupFailures.Add(new InvalidOperationException(
                    "Kept owned handoff state because a participant identity or join is unproved."
                ));
            }

            if (cleanupFailures.Count != 0)
            {
                AggregateException cleanup = new(
                    "Owned output handoff cleanup failed.",
                    cleanupFailures
                );
                if (completed)
                {
                    throw cleanup;
                }
                context.WriteLine("Secondary output handoff cleanup failure: {0}", cleanup);
            }

            async Task<bool> AttemptAsync(string step, Func<Task> action)
            {
                try
                {
                    await action().ConfigureAwait(false);
                    return true;
                }
                catch (Exception error)
                {
                    error.Data["OwnedOutputHandoffCleanupStep"] = step;
                    cleanupFailures.Add(error);
                    return false;
                }
            }

            static Task DisposeOwned(IDisposable owned)
            {
                owned.Dispose();
                return Task.CompletedTask;
            }
        }
    }
}
