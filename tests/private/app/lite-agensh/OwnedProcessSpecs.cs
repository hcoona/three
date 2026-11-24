using System.Diagnostics;
using System.Globalization;
using System.Runtime.ExceptionServices;
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
        OwnedChildControl control = new();
        Process owned = control.StartDirect();
        bool disposalAttempted = false;
        Exception? primaryFailure = null;
        try
        {
            OwnedChildIdentity ready = await control
                .ReadReadyAsync(context.CancellationToken)
                .ConfigureAwait(false);
            AssertOriginalIdentity(owned, ready);
            using Process observer = OpenObservedOwnedChild(ready.Pid, ready.BirthTicks);
            Assert.IsFalse(observer.HasExited);
            using CancellationTokenSource releaseCancellation = new();
            await releaseCancellation.CancelAsync().ConfigureAwait(false);
            OperationCanceledException primary = await Assert
                .ThrowsAsync<OperationCanceledException>(async () =>
                    await control.ReleaseAsync(releaseCancellation.Token).ConfigureAwait(false)
                )
                .ConfigureAwait(false);
            Assert.AreEqual(releaseCancellation.Token, primary.CancellationToken);
            disposalAttempted = true;
            await control.DisposeAsync().ConfigureAwait(false);
            Assert.IsTrue(observer.HasExited);
            Assert.ThrowsExactly<InvalidOperationException>(
                () => _ = owned.Id,
                "The directly owned Process must be disposed despite the preceding release error."
            );
            context.WriteLine(
                "OWNED_EXPECTED_RELEASE_FAILURE "
                    + new System.Text.Json.Nodes.JsonObject
                    {
                        ["pid"] = ready.Pid,
                        ["birthTicks"] = ready.BirthTicks,
                        ["releasePrimary"] = Json.ExceptionDetails(primary),
                        ["ownedExitObserved"] = observer.HasExited,
                        ["ownedProcessDisposed"] = true,
                        ["intentionalReleaseCancellation"] = true,
                    }.ToJsonString()
            );
        }
        catch (Exception error)
        {
            primaryFailure = error;
            throw;
        }
        finally
        {
            if (!disposalAttempted)
            {
                try
                {
                    await control.DisposeAsync().ConfigureAwait(false);
                }
                catch (Exception secondary) when (primaryFailure is not null)
                {
                    context.WriteLine(
                        "OWNED_WITNESS_CLEANUP_SECONDARY " + Json.ExceptionDetails(secondary)
                    );
                }
            }
        }
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
        using CancellationTokenSource cancellation =
            CancellationTokenSource.CreateLinkedTokenSource(context.CancellationToken);
        Task<ProcessResult> running = state.Runner.RunAsync(
            OwnedChildControl.ExecutablePath,
            ["exit", exitCode.ToString(CultureInfo.InvariantCulture)],
            state.WorkingDirectory,
            TimeSpan.FromSeconds(120),
            cancellation.Token
        );
        Exception? primaryFailure = null;
        try
        {
            ProcessResult result = await running.ConfigureAwait(false);
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
        }
        catch (Exception error)
        {
            primaryFailure = error;
            throw;
        }
        finally
        {
            await CancelAndDrainAsync(cancellation, running, primaryFailure).ConfigureAwait(false);
        }
    }

    [TestMethod]
    [Timeout(120_000, CooperativeCancellation = true)]
    [TestCategory("CancelSettleOnlyOwnedChildren")]
    public async Task CommandDeadlineRetainsDiagnosticsAndSettlesItsActualOwnedChild()
    {
        FakeTimeProvider clock = new();
        using OwnedProcessState state = new(clock);
        await using OwnedChildControl control = new();
        using CancellationTokenSource cancellation =
            CancellationTokenSource.CreateLinkedTokenSource(context.CancellationToken);
        Task<ProcessResult> running = state.Runner.RunAsync(
            OwnedChildControl.ExecutablePath,
            control.Arguments(role: OwnedChildControl.HeldCandidateCheckRole),
            state.WorkingDirectory,
            TimeSpan.FromSeconds(1),
            cancellation.Token
        );
        Exception? primaryFailure = null;
        try
        {
            OwnedChildIdentity ready = await control
                .ReadReadyAsync(running, context.CancellationToken)
                .ConfigureAwait(false);
            OwnedLease live = state.Lease(pid: ready.Pid);
            using Process child = OpenObservedOwnedChild(ready.Pid, ready.BirthTicks);
            Assert.AreEqual(ready.BirthTicks, live.BirthTicks);
            Assert.AreEqual("running", live.State);
            Assert.IsFalse(child.HasExited);

            clock.Advance(TimeSpan.FromSeconds(1));
            ProcessResult result = await running.ConfigureAwait(false);

            Assert.IsTrue(result.TimedOut);
            Assert.IsFalse(cancellation.IsCancellationRequested);
            Assert.AreEqual("exited", state.Lease(id: live.Id).State);
            Assert.IsTrue(child.HasExited);
            Assert.Contains("owned check diagnostic stdout before READY", result.StandardOutput);
            Assert.Contains("owned check diagnostic stderr before READY", result.StandardError);
            Assert.DoesNotContain("owned check exit 0", result.StandardOutput);
        }
        catch (Exception error)
        {
            primaryFailure = error;
            throw;
        }
        finally
        {
            await CancelAndDrainAsync(cancellation, running, primaryFailure).ConfigureAwait(false);
        }
    }

    [TestMethod]
    [Timeout(120_000, CooperativeCancellation = true)]
    [TestCategory("CancelSettleOnlyOwnedChildren")]
    public async Task CommandCancellationRetainsDrainedDiagnosticsWithoutReturningSuccess()
    {
        using OwnedProcessState state = new();
        await using OwnedChildControl control = new();
        using CancellationTokenSource cancellation =
            CancellationTokenSource.CreateLinkedTokenSource(context.CancellationToken);
        Task<ProcessResult> running = state.Runner.RunAsync(
            OwnedChildControl.ExecutablePath,
            control.Arguments(role: OwnedChildControl.HeldCandidateCheckRole),
            state.WorkingDirectory,
            TimeSpan.FromSeconds(120),
            cancellation.Token
        );
        Exception? primaryFailure = null;
        try
        {
            OwnedChildIdentity ready = await control
                .ReadReadyAsync(running, context.CancellationToken)
                .ConfigureAwait(false);
            OwnedLease live = state.Lease(pid: ready.Pid);
            Assert.AreEqual(ready.BirthTicks, live.BirthTicks);
            Assert.AreEqual("command", live.Kind);
            Assert.AreEqual("running", live.State);
            Assert.AreEqual(0, ready.DescendantPid);
            Assert.AreEqual(0L, ready.DescendantBirthTicks);
            using Process child = OpenObservedOwnedChild(ready.Pid, ready.BirthTicks);
            Assert.IsFalse(child.HasExited);
            context.WriteLine(
                $"Owned readiness pid={ready.Pid}; birth={ready.BirthTicks}; lease={live.Id}"
            );

            await cancellation.CancelAsync().ConfigureAwait(false);
            OperationCanceledException error = await Assert
                .ThrowsAsync<OperationCanceledException>(async () =>
                    await running.ConfigureAwait(false)
                )
                .ConfigureAwait(false);
            JsonNode evidence = JsonNode.Parse(Assert.IsInstanceOfType<string>(
                error.Data[ProcessRunner.CancellationEvidenceKey]
            ))!;
            OwnedLease retired = state.Lease(id: live.Id);

            Assert.AreEqual(cancellation.Token, error.CancellationToken);
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
        }
        catch (Exception error)
        {
            primaryFailure = error;
            throw;
        }
        finally
        {
            await CancelAndDrainAsync(cancellation, running, primaryFailure).ConfigureAwait(false);
        }
    }

    [TestMethod]
    [Timeout(120_000, CooperativeCancellation = true)]
    [TestCategory("CancelSettleOnlyOwnedChildren")]
    public async Task CommandCancellationWithAnUnsettledInheritedPipeDurablyBlocksReplacement()
    {
        OwnedProcessState state = new(new FakeTimeProvider());
        // A case timeout must not make the runner kill a root that still owns the holder.
        using CancellationTokenSource callerStop = new();
        using CancellationTokenSource entry = CancellationTokenSource.CreateLinkedTokenSource(
            context.CancellationToken
        );
        entry.CancelAfter(TimeSpan.FromSeconds(30));
        OwnedChildControl? startControl = null;
        OwnedChildControl? rootControl = null;
        OwnedChildControl? holderControl = null;
        Process? root = null;
        Process? holder = null;
        Task<ProcessResult>? running = null;
        bool launchAttempted = false;
        bool holderStartAttempted = false;
        ExceptionDispatchInfo? primaryFailure = null;
        InvalidOperationException? postStopFailure = null;
        List<Exception> cleanupFailures = [];
        try
        {
            startControl = new();
            rootControl = new();
            holderControl = new();
            launchAttempted = true;
            Task<ProcessResult> command = state.Runner.RunAsync(
                OwnedChildControl.ExecutablePath,
                [
                    "output-handoff-root", rootControl.PipeName, "0",
                    holderControl.PipeName, startControl.PipeName,
                ],
                state.WorkingDirectory,
                TimeSpan.FromSeconds(120),
                callerStop.Token
            );
            running = command;

            OwnedChildIdentity rootAtStart = await startControl
                .ReadReadyAsync(command, entry.Token)
                .ConfigureAwait(false);
            OwnedLease live = state.Lease(pid: rootAtStart.Pid);
            Assert.AreEqual(rootAtStart.BirthTicks, live.BirthTicks);
            Assert.AreEqual("command", live.Kind);
            Assert.AreEqual("running", live.State);
            Assert.AreEqual(0, rootAtStart.DescendantPid);
            root = OpenObservedOwnedChild(rootAtStart.Pid, rootAtStart.BirthTicks);
            Assert.IsFalse(root.HasExited);
            if (command.IsCompleted)
            {
                await command.ConfigureAwait(false);
                Assert.Fail("The command completed before holder startup was granted.");
            }

            // This RELEASE grants only holder startup, and a failed write may still deliver it.
            holderStartAttempted = true;
            await startControl.ReleaseAsync(entry.Token).ConfigureAwait(false);
            OwnedChildIdentity rootReady = await rootControl
                .ReadReadyAsync(command, entry.Token)
                .ConfigureAwait(false);
            Assert.AreEqual(rootAtStart.Pid, rootReady.Pid);
            Assert.AreEqual(rootAtStart.BirthTicks, rootReady.BirthTicks);
            OwnedChildIdentity holderReady = await holderControl
                .ReadReadyAsync(command, entry.Token)
                .ConfigureAwait(false);
            Assert.AreEqual(rootReady.DescendantPid, holderReady.Pid);
            Assert.AreEqual(rootReady.DescendantBirthTicks, holderReady.BirthTicks);
            Assert.AreNotEqual(rootReady.Pid, holderReady.Pid);
            Assert.AreEqual(0, holderReady.DescendantPid);
            holder = OpenObservedOwnedChild(holderReady.Pid, holderReady.BirthTicks);
            Assert.IsFalse(holder.HasExited);
            Assert.IsFalse(root.HasExited);

            await rootControl.ReleaseAsync(entry.Token).ConfigureAwait(false);
            await JoinExitAsync(root, entry.Token).ConfigureAwait(false);
            Assert.IsFalse(holder.HasExited);
            Assert.IsFalse(command.IsCompleted, "The exited root must still have output to drain.");
            Assert.AreEqual(live, state.Lease(id: live.Id));

            // Leave the command clock alone; only the caller stop and real drain bound apply.
            await callerStop.CancelAsync().ConfigureAwait(false);
            postStopFailure = await Assert
                .ThrowsExactlyAsync<InvalidOperationException>(async () =>
                    await command
                        .WaitAsync(TimeSpan.FromSeconds(15), context.CancellationToken)
                        .ConfigureAwait(false)
                )
                .ConfigureAwait(false);
            Assert.AreEqual(
                "Owned command or inherited pipes did not settle; replacement is blocked.",
                postStopFailure.Message
            );
            Assert.AreEqual(typeof(TimeoutException), postStopFailure.InnerException?.GetType());
            OwnedLease unresolved = state.Lease(id: live.Id);
            Assert.AreEqual(
                live with { State = "unresolved" },
                unresolved,
                "The surviving runner must durably mark the exact registered command."
            );

            using CancellationTokenSource admission = new(TimeSpan.FromSeconds(5));
            InvalidOperationException refusal = await Assert
                .ThrowsExactlyAsync<InvalidOperationException>(async () =>
                    await state.Runner.ResolveSurvivorsAsync(admission.Token).ConfigureAwait(false)
                )
                .ConfigureAwait(false);
            Assert.AreEqual(
                $"Process lease {live.Id} needs explicit ownership resolution.",
                refusal.Message
            );
            Assert.AreEqual(unresolved, state.Lease(id: live.Id));
            Assert.IsFalse(holder.HasExited);

            await holderControl.ReleaseAsync(context.CancellationToken).ConfigureAwait(false);
            await JoinExitAsync(holder, context.CancellationToken).ConfigureAwait(false);
        }
        catch (Exception error)
        {
            primaryFailure = ExceptionDispatchInfo.Capture(error);
        }
        finally
        {
            // Closing (not releasing) the root channel leaves pre-handoff cleanup to the root.
            if (startControl is { } startChannel)
            {
                await CleanupAsync("start-channel", () => startChannel.DisposeAsync().AsTask())
                    .ConfigureAwait(false);
            }
            if (rootControl is { } rootChannel)
            {
                await CleanupAsync("root-channel", () => rootChannel.DisposeAsync().AsTask())
                    .ConfigureAwait(false);
            }
            if (holderControl is { } holderChannel)
            {
                await CleanupAsync("holder-channel", () => holderChannel.DisposeAsync().AsTask())
                    .ConfigureAwait(false);
            }

            bool exitsJoined = !launchAttempted
                || (root is not null && (!holderStartAttempted || holder is not null));
            foreach (
                (Process? observer, string role) in new[] { (root, "root"), (holder, "holder") }
            )
            {
                if (observer is not null)
                {
                    exitsJoined &= await CleanupAsync(
                        $"{role}-exit", () => JoinExitAsync(observer, CancellationToken.None)
                    ).ConfigureAwait(false);
                    await CleanupAsync($"{role}-observer-dispose", () => DisposeAsync(observer))
                        .ConfigureAwait(false);
                }
            }
            bool runnerJoined = !launchAttempted || postStopFailure is not null;
            if (running is { } command && !runnerJoined)
            {
                await CleanupAsync(
                    "runner-join",
                    async () =>
                    {
                        try
                        {
                            await command.WaitAsync(TimeSpan.FromSeconds(15)).ConfigureAwait(false);
                        }
                        catch (Exception) when (command.IsCompleted)
                        {
                            // Observe the actual task even if its completion raced the watchdog.
                            runnerJoined = true;
                            try
                            {
                                await command.ConfigureAwait(false);
                            }
                            catch (Exception error) when (
                                ReferenceEquals(error, primaryFailure?.SourceException)
                            )
                            { }
                        }
                        runnerJoined = true;
                    }
                ).ConfigureAwait(false);
            }
            if (exitsJoined && runnerJoined)
            {
                await CleanupAsync("state-dispose", () => DisposeAsync(state))
                    .ConfigureAwait(false);
            }
            else
            {
                cleanupFailures.Add(new InvalidOperationException(
                    "Kept owned handoff state because a participant identity or join is unproved."
                ));
            }
        }
        if (cleanupFailures.Count != 0)
        {
            AggregateException cleanup = new(
                "Owned output handoff cleanup failed.", cleanupFailures
            );
            if (primaryFailure is null)
            {
                throw cleanup;
            }
            primaryFailure.SourceException.Data["OwnedOutputHandoffCleanup"] = cleanup;
        }
        primaryFailure?.Throw();

        async Task<bool> CleanupAsync(string step, Func<Task> action)
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

        static Task DisposeAsync(IDisposable owned)
        {
            owned.Dispose();
            return Task.CompletedTask;
        }

        static async Task JoinExitAsync(Process process, CancellationToken cancellationToken)
        {
            using CancellationTokenSource exit = CancellationTokenSource.CreateLinkedTokenSource(
                cancellationToken
            );
            exit.CancelAfter(TimeSpan.FromSeconds(5));
            await process.WaitForExitAsync(exit.Token).ConfigureAwait(false);
            Assert.IsTrue(process.HasExited);
        }
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
        using CancellationTokenSource cancellation =
            CancellationTokenSource.CreateLinkedTokenSource(context.CancellationToken);
        Task<ProcessResult> running = state.Runner.RunAsync(
            OwnedChildControl.ExecutablePath,
            rootControl.Arguments(descendantPipe: leafControl.PipeName),
            state.WorkingDirectory,
            TimeSpan.FromSeconds(120),
            cancellation.Token
        );
        Exception? primaryFailure = null;
        try
        {
            OwnedChildIdentity rootReady = await rootControl
                .ReadReadyAsync(running, context.CancellationToken)
                .ConfigureAwait(false);
            OwnedChildIdentity leafReady = await leafControl
                .ReadReadyAsync(running, context.CancellationToken)
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
            context.WriteLine(
                $"Owned root={rootReady.Pid}/{rootReady.BirthTicks}; "
                    + $"descendant={leafReady.Pid}/{leafReady.BirthTicks}; lease={lease.Id}"
            );

            await cancellation.CancelAsync().ConfigureAwait(false);
            OperationCanceledException failure = await Assert
                .ThrowsAsync<OperationCanceledException>(async () =>
                    await running.ConfigureAwait(false)
                )
                .ConfigureAwait(false);

            Assert.AreEqual(cancellation.Token, failure.CancellationToken);
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
            Assert.IsTrue(running.IsCanceled);
            await state
                .Runner.ResolveSurvivorsAsync(context.CancellationToken)
                .ConfigureAwait(false);
            Assert.IsFalse(sentinel.HasExited);
            AssertOriginalIdentity(sentinel, sentinelReady);
            await sentinelControl.ReleaseAsync(context.CancellationToken).ConfigureAwait(false);
            await RequireExitAsync(sentinel).ConfigureAwait(false);
            Assert.AreEqual(0, sentinel.ExitCode);
        }
        catch (Exception error)
        {
            primaryFailure = error;
            throw;
        }
        finally
        {
            await CancelAndDrainAsync(cancellation, running, primaryFailure).ConfigureAwait(false);
        }
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

    private static async Task CancelAndDrainAsync(
        CancellationTokenSource cancellation,
        Task<ProcessResult> running,
        Exception? primaryFailure
    )
    {
        bool cancellationRequestedOnEntry = cancellation.IsCancellationRequested;
        try
        {
            await cancellation.CancelAsync().ConfigureAwait(false);
            ProcessResult result = await running.ConfigureAwait(false);
            Console.WriteLine(
                "OWNED_COMMAND_TERMINAL "
                    + new System.Text.Json.Nodes.JsonObject
                    {
                        ["exitCode"] = result.ExitCode,
                        ["timedOut"] = result.TimedOut,
                        ["stdout"] = result.StandardOutput,
                        ["stderr"] = result.StandardError,
                    }.ToJsonString()
            );
        }
        catch (OperationCanceledException error) when (cancellation.IsCancellationRequested)
        {
            if (primaryFailure is not null)
            {
                string? evidence = (string?)error.Data[ProcessRunner.CancellationEvidenceKey];
                primaryFailure.Data["OwnedCommandCleanupCancellation"] = evidence;
                Console.Error.WriteLine(
                    "OWNED_COMMAND_CLEANUP_CANCELLED "
                        + new JsonObject
                        {
                            ["cancellationRequestedOnEntry"] = cancellationRequestedOnEntry,
                            ["cancellationEvidence"] = evidence,
                        }.ToJsonString()
                );
            }
        }
        catch (Exception secondary) when (primaryFailure is not null)
        {
            string details = Json.ExceptionDetails(secondary).ToJsonString();
            primaryFailure.Data["OwnedCommandCleanupFailure"] = details;
            Console.Error.WriteLine("OWNED_COMMAND_CLEANUP_SECONDARY " + details);
        }
    }
}
