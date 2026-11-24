using System.Diagnostics;
using System.Globalization;
using System.Text.Json.Nodes;
using LiteAgensh.Tests.Support;
using Microsoft.Data.Sqlite;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace LiteAgensh.Tests;

[TestClass]
public sealed class CreditWindowSpecs
{
    private readonly TestContext context;

    public CreditWindowSpecs(TestContext context)
    {
        this.context = context;
    }

    [TestMethod]
    [TestCategory("SharedBackingAndReuse")]
    [Timeout(120_000, CooperativeCancellation = true)]
    public async Task PeerExceedsEqualShareAndCompletedWindowsReturnReusableBacking()
    {
        using CreditWindowState state = new(230, 200);
        CreditReservation unrelated = state.Unrelated();
        CreditReservation first = state.Reserve("worker1");
        CreditReservation second = state.Reserve("worker2");
        CreditReservation cold = state.Reserve("worker3");
        Assert.AreEqual(0m, state.Read(cold.Id).Allowance);
        Assert.IsFalse(state.Read(cold.Id).GrantOpen);
        Assert.AreEqual("reserved", state.Read(cold.Id).State);
        Assert.AreEqual(
            0L,
            state.Count(
                "SELECT dispatch_count FROM credit_sdk_usage WHERE reservation_id=$id;",
                ("$id", cold.Id)
            )
        );
        // A saved session identity is not a started native session or permission to dispatch.
        Assert.AreEqual(
            "credit_unknown",
            Assert.ThrowsExactly<OperationException>(() => state.Dispatch(cold)).Code
        );
        CreditReservation grant = await state
            .Ledger.AcquireSharedWindowAsync(first.Id, context.CancellationToken)
            .ConfigureAwait(false);
        Assert.AreEqual(100m, grant.Allowance);
        Assert.IsGreaterThan(200m / 3m, grant.Allowance);
        Assert.IsTrue(state.Read(first.Id).GrantOpen);
        state.AssertAccounting(130, 100, 0, 100);
        Assert.AreEqual(
            100m,
            230m
                - state.Ledger.Status("run", state.RunAllowance)[
                    "campaignCommitted"
                ]!.GetValue<decimal>()
        );
        state.Start(first);
        state.Dispatch(first);
        state.Ledger.RecordSdkUsage(first.Id, Usage(first, 20, 1));
        state.Ledger.RecordSdkUsage(first.Id, Usage(first, 35, 2));
        state.Ledger.RecordSdkUsage(first.Id, Usage(first, 35, 2));
        state.AssertAccounting(130, 100, 35, 65);
        Assert.AreEqual(
            2L,
            state.Count(
                "SELECT COUNT(*) FROM credit_reports WHERE reservation_id=$id;",
                ("$id", first.Id)
            )
        );
        Assert.AreEqual(
            2L,
            state.Count(
                """
                SELECT COUNT(DISTINCT actual) FROM credit_reports
                WHERE reservation_id=$id AND scope='sdk-session-cumulative'
                    AND actual IN ('20','35');
                """,
                ("$id", first.Id)
            )
        );
        state.Ledger.CloseSharedWindow(first.Id);
        state.AssertAccounting(65, 35, 35, 0);
        Assert.AreEqual(
            165m,
            state.RunAllowance
                - state.Ledger.Status("run", state.RunAllowance)[
                    "runCommitted"
                ]!.GetValue<decimal>()
        );

        CreditReservation secondGrant = await state
            .Ledger.AcquireSharedWindowAsync(second.Id, context.CancellationToken)
            .ConfigureAwait(false);
        Assert.AreEqual(100m, secondGrant.Allowance);
        state.AssertAccounting(165, 135, 35, 100);
        state.Start(second);
        state.Dispatch(second);
        state.Ledger.RecordSdkUsage(second.Id, Usage(second, 40, 1), final: true);
        state.AssertAccounting(105, 75, 75, 0);
        Assert.AreEqual("reconciled", state.Read(second.Id).State);
        CreditReservation renewed = await state
            .Ledger.AcquireSharedWindowAsync(first.Id, context.CancellationToken)
            .ConfigureAwait(false);
        Assert.AreEqual(135m, renewed.Allowance);
        Assert.AreEqual(35m, renewed.Actual);
        state.AssertAccounting(205, 175, 75, 100);
        Assert.AreEqual(
            25m,
            state.RunAllowance
                - state.Ledger.Status("run", state.RunAllowance)[
                    "runCommitted"
                ]!.GetValue<decimal>()
        );
        Assert.AreEqual(unrelated, state.Read(unrelated.Id));
        Assert.AreEqual(cold, state.Read(cold.Id));
    }

    [TestMethod]
    [TestCategory("SharedBackingAndReuse")]
    [Timeout(120_000, CooperativeCancellation = true)]
    public async Task SimultaneousAdmissionsPersistCompatibleGrantsWithoutDoubleBacking()
    {
        using CreditWindowState state = new(180, 150);
        CreditReservation unrelated = state.Unrelated();
        CreditReservation first = state.Reserve("worker1");
        CreditReservation second = state.Reserve("worker2");
        using CancellationTokenSource cancellation =
            CancellationTokenSource.CreateLinkedTokenSource(context.CancellationToken);
        ActivityTraceId traceId = ActivityTraceId.CreateRandom();
        TaskCompletionSource<Activity> leftRequest = new(
            TaskCreationOptions.RunContinuationsAsynchronously
        );
        TaskCompletionSource<Activity> rightRequest = new(
            TaskCreationOptions.RunContinuationsAsynchronously
        );
        using ActivityListener listener = new()
        {
            ShouldListenTo = source => source.Name == "LiteAgensh",
            Sample = (ref ActivityCreationOptions<ActivityContext> options) =>
                options.Parent.TraceId == traceId && options.Name == "sqlite.acquisition"
                    ? ActivitySamplingResult.AllDataAndRecorded
                    : ActivitySamplingResult.None,
            ActivityStarted = activity =>
            {
                if (activity.TraceId != traceId || activity.OperationName != "sqlite.acquisition")
                {
                    return;
                }

                // Entry precedes the provider call and the diagnostic tags. Correlate the
                // worker scope here and verify the emitted operation and actor after joining.
                string? worker = activity.Parent?.OperationName;
                if (worker == first.Worker)
                {
                    leftRequest.TrySetResult(activity);
                }
                else if (worker == second.Worker)
                {
                    rightRequest.TrySetResult(activity);
                }
            },
        };
        ActivitySource.AddActivityListener(listener);

        Task<CreditReservation>? left = null;
        Task<CreditReservation>? right = null;
        try
        {
            Task<Activity[]> requests = Task.WhenAll(leftRequest.Task, rightRequest.Task);
            Task observed;
            using (SqliteDiagnostics.UseActor("owner"))
            using (SqliteConnection writer = Sql.Open(
                new SqliteConnection(new SqliteConnectionStringBuilder
                {
                    DataSource = state.LedgerPath,
                    Mode = SqliteOpenMode.ReadWrite,
                    Pooling = false,
                }.ToString()),
                "campaign"
            ))
            using (Sql.BeginTransaction(writer, deferred: false, operation: "HeldWriter"))
            {
                left = Acquire(first);
                right = Acquire(second);
                observed = await Task.WhenAny(requests, left, right)
                    .WaitAsync(TimeSpan.FromSeconds(5), context.CancellationToken)
                    .ConfigureAwait(false);
            }

            if (!ReferenceEquals(observed, requests))
            {
                await observed.ConfigureAwait(false);
                Assert.Fail("An acquisition returned before both request entries were observed.");
            }

            CreditReservation[] granted = await Task.WhenAll(left, right).ConfigureAwait(false);
            Activity[] entries = await requests.ConfigureAwait(false);
            Assert.AreEqual("TryAcquireSharedWindow", entries[0].GetTagItem("db.operation.name"));
            Assert.AreEqual(first.Worker, entries[0].GetTagItem("liteagensh.actor"));
            Assert.AreEqual("TryAcquireSharedWindow", entries[1].GetTagItem("db.operation.name"));
            Assert.AreEqual(second.Worker, entries[1].GetTagItem("liteagensh.actor"));

            CreditReservation[] durable = state.Ledger.Reservations("run").ToArray();
            decimal[] grantedAmounts = durable.Select(row => row.Allowance).Order().ToArray();
            Assert.HasCount(2, grantedAmounts);
            Assert.AreEqual(50m, grantedAmounts[0]);
            Assert.AreEqual(100m, grantedAmounts[1]);
            Assert.AreEqual(granted[0], state.Read(first.Id));
            Assert.AreEqual(granted[1], state.Read(second.Id));
            Assert.IsTrue(
                durable.All(row => row.Shared && row.GrantOpen && row.State == "reserved")
            );
            Assert.AreEqual(0L, state.Count("SELECT SUM(dispatch_count) FROM credit_sdk_usage;"));
            state.AssertAccounting(180, 150, 0, 150);
            Assert.AreEqual(0m, state.RunAllowance - durable.Sum(row => row.Allowance));
            Assert.AreEqual(unrelated, state.Read(unrelated.Id));
        }
        catch (Exception primary)
        {
            try
            {
                await Task.WhenAll(
                    cancellation.CancelAsync(),
                    (Task?)left ?? Task.CompletedTask,
                    (Task?)right ?? Task.CompletedTask
                ).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (cancellation.IsCancellationRequested)
            {
            }
            catch (Exception secondary)
            {
                if (!ReferenceEquals(secondary, primary))
                {
                    primary.Data["CreditWindowAdmissionsCleanup"] =
                        Json.ExceptionDetails(secondary);
                }
            }
            throw;
        }

        Task<CreditReservation> Acquire(CreditReservation row) =>
            Task.Run(
                async () =>
                {
                    using Activity scope = new Activity(row.Worker)
                        .SetIdFormat(ActivityIdFormat.W3C)
                        .SetParentId(
                            traceId, ActivitySpanId.CreateRandom(), ActivityTraceFlags.Recorded
                        ).Start();
                    using IDisposable actor = SqliteDiagnostics.UseActor(row.Worker);
                    return await state
                        .Ledger.AcquireSharedWindowAsync(row.Id, cancellation.Token)
                        .ConfigureAwait(false);
                },
                cancellation.Token
            );
    }

    [TestMethod]
    [TestCategory("SharedBackingAndReuse")]
    [Timeout(120_000, CooperativeCancellation = true)]
    public async Task SpentPoolBelowMinimumRejectsAdmissionWithoutCreatingAnotherGrant()
    {
        using CreditWindowState state = new(130, 100);
        CreditReservation unrelated = state.Unrelated();
        CreditReservation first = state.Reserve("worker1");
        CreditReservation contender = state.Reserve("worker2");
        await state
            .Ledger.AcquireSharedWindowAsync(first.Id, context.CancellationToken)
            .ConfigureAwait(false);
        state.Start(first);
        state.Dispatch(first);
        state.Ledger.RecordSdkUsage(first.Id, Usage(first, 80, 1), final: true);
        state.AssertAccounting(110, 80, 80, 0);
        Assert.AreEqual(20m, state.RunAllowance - state.Read(first.Id).Actual);
        OperationException error = await Assert
            .ThrowsExactlyAsync<OperationException>(async () =>
                await state
                    .Ledger.AcquireSharedWindowAsync(contender.Id, context.CancellationToken)
                    .ConfigureAwait(false)
            )
            .ConfigureAwait(false);
        Assert.AreEqual("credit_exhausted", error.Code);
        Assert.AreEqual(contender, state.Read(contender.Id));
        Assert.AreEqual(unrelated, state.Read(unrelated.Id));
        Assert.AreEqual(1L, state.Count("SELECT COUNT(*) FROM credit_reports;"));
        state.AssertAccounting(110, 80, 80, 0);
    }

    [TestMethod]
    [TestCategory("SharedBackingAndReuse")]
    [DataRow(false)]
    [DataRow(true)]
    public async Task CampaignCeilingBindsRunSolventSharedWindowForSpentOrHeldOtherRun(bool spent)
    {
        using CreditWindowState state = new(120, 100);
        CreditReservation other = state.Ledger.Reserve(
            "other", "worker9", 100, 100, state.NewEvidencePath()
        );
        CreditReservation contender = state.Reserve("worker2");
        if (spent)
        {
            state.Start(other);
            state.Ledger.BeginDispatch("other", other.Id, 100);
            state.Ledger.RecordSdkUsage(other.Id, Usage(other, 95, 1), final: true);
            Assert.AreEqual(95m, state.Read(other.Id).Actual);
        }
        state.AssertAccounting(spent ? 95 : 100, 0, 0, 0);
        Assert.AreEqual(100m, state.RunAllowance);
        Assert.AreEqual(0m, contender.Allowance);
        Assert.IsFalse(contender.GrantOpen);
        using CancellationTokenSource cancellation =
            CancellationTokenSource.CreateLinkedTokenSource(context.CancellationToken);
        Task<CreditReservation>? pending = null;
        try
        {
            using (SqliteDiagnostics.UseActor("worker2"))
            {
                pending = state.Ledger.AcquireSharedWindowAsync(contender.Id, cancellation.Token);
            }
            if (spent)
            {
                OperationException error = await Assert.ThrowsExactlyAsync<OperationException>(
                    () => pending.WaitAsync(TimeSpan.FromSeconds(5), context.CancellationToken)
                );
                Assert.AreEqual("credit_exhausted", error.Code);
                Assert.AreEqual(contender, state.Read(contender.Id));
                Assert.AreEqual(1L, state.Count("SELECT COUNT(*) FROM credit_reports;"));
                state.AssertAccounting(95, 0, 0, 0);
            }
            else
            {
                JsonObject witness = AssertCommittedContenderAttempt(state);
                context.WriteLine("Campaign-bound wait witness: " + witness.ToJsonString());
                Assert.AreEqual(other, state.Read(other.Id));
                Assert.AreEqual(contender, state.Read(contender.Id));
                Assert.AreEqual(0L, state.Count("SELECT COUNT(*) FROM credit_reports;"));
                state.AssertAccounting(100, 0, 0, 0);

                state.Ledger.ReleaseUnstarted(other.Id);
                CreditReservation granted = await pending.WaitAsync(
                    TimeSpan.FromSeconds(5), context.CancellationToken
                );
                Assert.AreEqual(contender.Id, granted.Id);
                Assert.AreEqual(100m, granted.Allowance);
                Assert.IsTrue(granted.GrantOpen);
                Assert.AreEqual("released", state.Read(other.Id).State);
                state.AssertAccounting(100, 100, 0, 100);
            }
        }
        finally
        {
            await cancellation.CancelAsync();
            if (pending is not null)
            {
                await Drain(pending);
            }
        }
    }

    [TestMethod]
    [TestCategory("SharedBackingAndReuse")]
    [Timeout(120_000, CooperativeCancellation = true)]
    public async Task CampaignCeilingBacksMinimumSharedGrantWhileRunHasMoreRoom()
    {
        using CreditWindowState state = new(130, 100);
        CreditReservation other = state.Ledger.Reserve(
            "other", "worker9", 100, 100, state.NewEvidencePath()
        );
        CreditReservation contender = state.Reserve("worker2");

        CreditReservation grant = await state.Ledger.AcquireSharedWindowAsync(
            contender.Id, context.CancellationToken
        ).ConfigureAwait(false);

        Assert.AreEqual(contender.Id, grant.Id);
        Assert.AreEqual(contender.Worker, grant.Worker);
        Assert.AreEqual(contender.SessionId, grant.SessionId);
        Assert.AreEqual("reserved", grant.State);
        Assert.AreEqual(30m, grant.Allowance);
        Assert.IsTrue(grant.GrantOpen);
        Assert.IsNull(grant.Actual);
        Assert.AreEqual(grant, state.Read(contender.Id));
        Assert.AreEqual(other, state.Read(other.Id));
        state.AssertAccounting(130, 30, 0, 30);
        Assert.AreEqual(0L, state.Count(
            "SELECT dispatch_count FROM credit_sdk_usage WHERE reservation_id=$id;",
            ("$id", grant.Id)
        ));
        Assert.AreEqual(0L, state.Count("SELECT COUNT(*) FROM credit_reports;"));
    }

    [TestMethod]
    [TestCategory("WaitThenCancelOrAdmit")]
    [DataRow(false)]
    [DataRow(true)]
    [Timeout(120_000, CooperativeCancellation = true)]
    public async Task CommittedBackingWaitIsObservedBeforeReleaseOrCancellation(bool cancel)
    {
        using CreditWindowState state = new(130, 100);
        CreditReservation unrelated = state.Unrelated();
        CreditReservation holder = state.Reserve("worker1");
        CreditReservation contender = state.Reserve("worker2");
        using CancellationTokenSource cancellation =
            CancellationTokenSource.CreateLinkedTokenSource(context.CancellationToken);
        using (SqliteDiagnostics.UseActor("worker1"))
        {
            CreditReservation granted = await state
                .Ledger.AcquireSharedWindowAsync(holder.Id, cancellation.Token)
                .ConfigureAwait(false);
            Assert.AreEqual(100m, granted.Allowance);
        }
        CreditReservation held = state.Read(holder.Id);
        Assert.AreEqual("reserved", held.State);
        Assert.IsTrue(held.GrantOpen);
        Assert.IsNull(held.Actual);
        Assert.AreEqual(contender, state.Read(contender.Id));
        Assert.AreEqual(0L, state.Count("SELECT SUM(dispatch_count) FROM credit_sdk_usage;"));
        Assert.AreEqual(
            3L,
            state.Count(
                """
                SELECT COUNT(*) FROM credit_reservations r JOIN credit_sdk_usage u
                    ON u.reservation_id=r.id AND u.session_id=r.session_id
                JOIN credit_sdk_owners o ON o.reservation_id=r.id
                WHERE r.state='reserved' AND u.dispatch_count=0;
                """
            )
        );
        state.AssertAccounting(130, 100, 0, 100);
        // Actual remaining=100, but available backing=100-100=0; contender reclaim=0.
        Assert.AreEqual(100m, state.RunAllowance - (held.Actual ?? 0));
        Assert.AreEqual(0m, state.RunAllowance - held.Allowance - contender.Allowance);
        Assert.IsFalse(cancellation.IsCancellationRequested);
        Task<CreditReservation>? pending = null;
        try
        {
            using (SqliteDiagnostics.UseActor("worker2"))
            {
                // Deliberately direct: its synchronous transaction prefix precedes the first await.
                pending = state.Ledger.AcquireSharedWindowAsync(contender.Id, cancellation.Token);
            }
            JsonObject witness = AssertCommittedContenderAttempt(state);
            context.WriteLine("Committed credit wait witness: " + witness.ToJsonString());
            Assert.AreEqual(held, state.Read(holder.Id));
            Assert.AreEqual(contender, state.Read(contender.Id));
            state.AssertAccounting(130, 100, 0, 100);
            if (cancel)
            {
                await cancellation.CancelAsync().ConfigureAwait(false);
                OperationCanceledException error = await Assert
                    .ThrowsAsync<OperationCanceledException>(async () =>
                        await pending.ConfigureAwait(false)
                    )
                    .ConfigureAwait(false);
                Assert.AreEqual(cancellation.Token, error.CancellationToken);
                Assert.IsTrue(pending.IsCanceled);
                Assert.IsFalse(context.CancellationToken.IsCancellationRequested);
                Assert.AreEqual(held, state.Read(holder.Id));
                Assert.AreEqual(contender, state.Read(contender.Id));
                state.AssertAccounting(130, 100, 0, 100);
                state.Ledger.ReleaseUnstarted(holder.Id);
                CreditReservation replacement = state.Reserve("worker3");
                CreditReservation admitted = await state
                    .Ledger.AcquireSharedWindowAsync(replacement.Id, context.CancellationToken)
                    .ConfigureAwait(false);
                Assert.AreEqual(100m, admitted.Allowance);
                Assert.AreEqual(contender, state.Read(contender.Id));
                state.AssertAccounting(130, 100, 0, 100);
            }
            else
            {
                state.Ledger.ReleaseUnstarted(holder.Id);
                CreditReservation admitted = await pending.ConfigureAwait(false);
                Assert.AreEqual(contender.Id, admitted.Id);
                Assert.AreEqual(contender.SessionId, admitted.SessionId);
                Assert.AreEqual(100m, admitted.Allowance);
                Assert.IsTrue(state.Read(contender.Id).GrantOpen);
                Assert.AreEqual("released", state.Read(holder.Id).State);
                Assert.IsFalse(state.Read(holder.Id).GrantOpen);
                state.AssertAccounting(130, 100, 0, 100);
            }
            Assert.AreEqual(unrelated, state.Read(unrelated.Id));
            Assert.AreEqual(0L, state.Count("SELECT COUNT(*) FROM credit_reports;"));
        }
        finally
        {
            // Even a missing diagnostic/assertion failure must drain before scratch deletion.
            await cancellation.CancelAsync().ConfigureAwait(false);
            if (pending is not null)
            {
                await Drain(pending).ConfigureAwait(false);
            }
        }
    }

    [TestMethod]
    [TestCategory("WaitThenCancelOrAdmit")]
    [Timeout(120_000, CooperativeCancellation = true)]
    public async Task LedgerCancellationAndNewAdmissionCannotSpendReturnedCapacityTwice()
    {
        using CreditWindowState state = new(130, 100);
        CreditReservation unrelated = state.Unrelated();
        CreditReservation holder = state.Reserve("worker1");
        await state
            .Ledger.AcquireSharedWindowAsync(holder.Id, context.CancellationToken)
            .ConfigureAwait(false);
        state.Start(holder);
        // Real empty-session evidence at the existing internal cancellation boundary.
        state.Ledger.RecordSdkUsage(holder.Id, new(holder.SessionId!, 0, null, 0));
        holder = state.Read(holder.Id);
        JsonObject proof = new()
        {
            ["owner"] = state.Ledger.ReadSdkOwner(holder.Id),
            ["diagnostic"] = new JsonObject
            {
                ["reportedNanoAiu"] = "0",
                ["aiCreditsStatus"] = null,
            },
        };
        TaskCompletionSource release = new(TaskCreationOptions.RunContinuationsAsynchronously);
        Task<JsonObject> cancellation = Task.Run(async () =>
        {
            await release.Task.ConfigureAwait(false);
            return state.Ledger.CancelBeforeDispatch(holder, proof, "controlled-ledger-boundary");
        });
        Task<(CreditReservation? Reservation, string? Error)> admission = Task.Run(async () =>
        {
            await release.Task.ConfigureAwait(false);
            try
            {
                return (state.Reserve("worker2", shared: false, allowance: 100), (string?)null);
            }
            catch (OperationException error)
            {
                return ((CreditReservation?)null, error.Code);
            }
        });
        release.SetResult();
        await Task.WhenAll(cancellation, admission).ConfigureAwait(false);
        Assert.AreEqual(
            "verified-before-first-dispatch",
            (await cancellation.ConfigureAwait(false))["classification"]!.GetValue<string>()
        );
        (CreditReservation? reservation, string? errorCode) = await admission.ConfigureAwait(false);
        if (reservation is null)
        {
            Assert.AreEqual("credit_exhausted", errorCode);
            state.AssertAccounting(30, 0, 0, 0);
            reservation = state.Reserve("worker2", shared: false, allowance: 100);
        }
        else
        {
            Assert.IsNull(errorCode);
        }
        Assert.AreEqual("released", state.Read(holder.Id).State);
        Assert.IsFalse(state.Read(holder.Id).GrantOpen);
        Assert.IsNull(state.Read(holder.Id).Actual);
        Assert.AreEqual(100m, state.Read(reservation.Id).Allowance);
        Assert.AreEqual(2, state.Ledger.Reservations("run").Count);
        state.AssertAccounting(130, 100, 0, 0);
        Assert.AreEqual(
            1L,
            state.Count(
                "SELECT COUNT(*) FROM credit_cancellations WHERE reservation_id=$id;",
                ("$id", holder.Id)
            )
        );
        Assert.AreEqual(0L, state.Count("SELECT COUNT(*) FROM credit_reports;"));
        Assert.AreEqual(unrelated, state.Read(unrelated.Id));
        Assert.IsTrue(
            state.Ledger.CancelBeforeDispatch(holder, proof, "controlled-ledger-boundary")[
                "idempotent"
            ]!.GetValue<bool>()
        );
        Assert.AreEqual(
            "credit_exhausted",
            Assert
                .ThrowsExactly<OperationException>(() =>
                    state.Reserve("worker3", shared: false, allowance: 30)
                )
                .Code
        );
        state.AssertAccounting(130, 100, 0, 0);
    }

    [TestMethod]
    [TestCategory("WaitThenCancelOrAdmit")]
    [Timeout(120_000, CooperativeCancellation = true)]
    public async Task FirstDispatchAndLedgerCancellationHaveOnlyOnePersistedWinner()
    {
        using CreditWindowState state = new(130, 100);
        CreditReservation unrelated = state.Unrelated();
        CreditReservation holder = state.Reserve("worker1");
        CreditReservation contender = state.Reserve("worker2");
        await state
            .Ledger.AcquireSharedWindowAsync(holder.Id, context.CancellationToken)
            .ConfigureAwait(false);
        state.Start(holder);
        state.Ledger.RecordSdkUsage(holder.Id, new(holder.SessionId!, 0, null, 0));
        holder = state.Read(holder.Id);
        JsonObject proof = new()
        {
            ["owner"] = state.Ledger.ReadSdkOwner(holder.Id),
            ["diagnostic"] = new JsonObject
            {
                ["reportedNanoAiu"] = "0",
                ["aiCreditsStatus"] = null,
            },
        };
        TaskCompletionSource release = new(TaskCreationOptions.RunContinuationsAsynchronously);
        Task<string> cancellation = Task.Run(async () =>
        {
            await release.Task.ConfigureAwait(false);
            try
            {
                state.Ledger.CancelBeforeDispatch(holder, proof, "controlled-ledger-boundary");
                return "cancelled";
            }
            catch (OperationException error)
            {
                return error.Code;
            }
        });
        Task<string> dispatch = Task.Run(async () =>
        {
            await release.Task.ConfigureAwait(false);
            try
            {
                state.Dispatch(holder);
                return "dispatched";
            }
            catch (OperationException error)
            {
                return error.Code;
            }
        });
        release.SetResult();
        await Task.WhenAll(cancellation, dispatch).ConfigureAwait(false);
        if (await cancellation.ConfigureAwait(false) == "cancelled")
        {
            Assert.AreEqual("credit_unknown", await dispatch.ConfigureAwait(false));
            Assert.AreEqual("released", state.Read(holder.Id).State);
            Assert.IsFalse(state.Read(holder.Id).GrantOpen);
            Assert.AreEqual(
                0L,
                state.Count(
                    "SELECT dispatch_count FROM credit_sdk_usage WHERE reservation_id=$id;",
                    ("$id", holder.Id)
                )
            );
            state.AssertAccounting(30, 0, 0, 0);
            CreditReservation granted = await state
                .Ledger.AcquireSharedWindowAsync(contender.Id, context.CancellationToken)
                .ConfigureAwait(false);
            Assert.AreEqual(100m, granted.Allowance);
            state.AssertAccounting(130, 100, 0, 100);
        }
        else
        {
            Assert.AreEqual("credit_unknown", await cancellation.ConfigureAwait(false));
            Assert.AreEqual("dispatched", await dispatch.ConfigureAwait(false));
            Assert.AreEqual("active", state.Read(holder.Id).State);
            Assert.IsTrue(state.Read(holder.Id).GrantOpen);
            Assert.AreEqual(
                1L,
                state.Count(
                    "SELECT dispatch_count FROM credit_sdk_usage WHERE reservation_id=$id;",
                    ("$id", holder.Id)
                )
            );
            Assert.AreEqual(contender, state.Read(contender.Id));
            Assert.AreEqual(
                "credit_exhausted",
                Assert
                    .ThrowsExactly<OperationException>(() =>
                        state.Reserve("worker3", shared: false, allowance: 30)
                    )
                    .Code
            );
            state.AssertAccounting(130, 100, 0, 100);
        }
        Assert.IsNull(state.Read(holder.Id).Actual);
        Assert.AreEqual(0L, state.Count("SELECT COUNT(*) FROM credit_reports;"));
        Assert.AreEqual(unrelated, state.Read(unrelated.Id));
    }

    [TestMethod]
    [TestCategory("CapAndUsageConservation")]
    [DataRow("partial")]
    [DataRow("unavailable")]
    [DataRow("absent")]
    [DataRow("regressingAmount")]
    [DataRow("regressingRequests")]
    [DataRow("unassociated")]
    [DataRow("emptyComplete")]
    [Timeout(120_000, CooperativeCancellation = true)]
    public async Task InvalidUsageRetainsSpendBackingAssociationAndBlocksFurtherAdmission(
        string kind
    )
    {
        using CreditWindowState state = new(230, 200);
        CreditReservation unrelated = state.Unrelated();
        CreditReservation reservation = state.Reserve("worker1");
        await state
            .Ledger.AcquireSharedWindowAsync(reservation.Id, context.CancellationToken)
            .ConfigureAwait(false);
        state.Start(reservation);
        state.Dispatch(reservation);
        state.Ledger.RecordSdkUsage(reservation.Id, Usage(reservation, 35, 2));
        CreditReservation before = state.Read(reservation.Id);
        NativeUsage invalid = kind switch
        {
            "partial" => new(reservation.SessionId!, 40_000_000_000, "partial", 3),
            "unavailable" => new(reservation.SessionId!, null, "unavailable", 3),
            "absent" => new(reservation.SessionId!, null, null, 3),
            "regressingAmount" => Usage(reservation, 34, 3),
            "regressingRequests" => Usage(reservation, 40, 1),
            "unassociated" => new("different-session", 40_000_000_000, "complete", 3),
            "emptyComplete" => Usage(reservation, 0, 0),
            _ => throw new ArgumentOutOfRangeException(nameof(kind)),
        };
        Assert.AreEqual(
            "credit_unknown",
            Assert
                .ThrowsExactly<OperationException>(() =>
                    state.Ledger.RecordSdkUsage(reservation.Id, invalid, final: true)
                )
                .Code
        );
        Assert.AreEqual(before with { State = "uncertain" }, state.Read(reservation.Id));
        state.AssertAccounting(130, 100, 35, 65, uncertain: true);
        if (kind is "partial" or "unavailable" or "absent")
        {
            JsonObject reported = Assert.ContainsSingle(
                state.Ledger.Status("run", state.RunAllowance)["reported"]!.AsArray()
            )!.AsObject();
            string? nano = reported["reportedNanoAiu"]?.GetValue<string>();
            double? reportedNano = nano is null
                ? null
                : double.Parse(nano, CultureInfo.InvariantCulture);
            Assert.AreEqual(invalid.TotalNanoAiu, reportedNano);
            Assert.AreEqual(invalid.Status, reported["status"]?.GetValue<string>());
        }
        Assert.IsTrue(state.Ledger.HasSdkAssociation(reservation.Id));
        Assert.AreEqual(
            1L,
            state.Count(
                (
                    "SELECT COUNT(*) FROM credit_sdk_usage WHERE reservation_id=$id AND "
                    + "session_id=$session;"
                ),
                ("$id", reservation.Id),
                ("$session", reservation.SessionId)
            )
        );
        Assert.AreEqual(
            1L,
            state.Count(
                "SELECT COUNT(*) FROM credit_reports WHERE reservation_id=$id;",
                ("$id", reservation.Id)
            )
        );
        Assert.AreEqual(
            "credit_unknown",
            Assert.ThrowsExactly<OperationException>(() => state.Reserve("worker2")).Code
        );
        Assert.AreEqual(unrelated, state.Read(unrelated.Id));
    }

    [TestMethod]
    [TestCategory("CapAndUsageConservation")]
    public void ExactContinuationRetainsIdentityBaselineAndCeilingWhileFreshSessionNeedsNewBacking()
    {
        using CreditWindowState state = new(230, 200);
        CreditReservation unrelated = state.Unrelated();
        CreditReservation original = state.Reserve("worker1", shared: false, allowance: 100);
        state.Start(original);
        state.Dispatch(original);
        state.Ledger.RecordSdkUsage(original.Id, Usage(original, 35, 2), final: true);
        state.Ledger.RecordSdkUsage(original.Id, Usage(original, 35, 2), final: true);
        state.AssertAccounting(65, 35, 35, 0);
        CreditReservation continued = state.Ledger.ContinueExact(
            original.Id,
            state.NewEvidencePath()
        );
        Assert.AreEqual(original.Id, continued.Id);
        Assert.AreEqual(original.SessionId, continued.SessionId);
        Assert.AreEqual(100m, continued.Allowance);
        Assert.AreEqual(35m, continued.Actual);
        Assert.AreEqual("reserved", continued.State);
        Assert.AreNotEqual(original.UsagePath, continued.UsagePath);
        Assert.AreEqual(
            1L,
            state.Count(
                "SELECT COUNT(*) FROM credit_reports WHERE reservation_id=$id;",
                ("$id", original.Id)
            )
        );
        state.AssertAccounting(130, 100, 35, 0);
        state.Start(continued);
        state.Dispatch(continued);
        state.Ledger.RecordSdkUsage(continued.Id, Usage(continued, 50, 3), final: true);
        state.Ledger.RecordSdkUsage(continued.Id, Usage(continued, 50, 3), final: true);
        state.AssertAccounting(80, 50, 50, 0);
        Assert.AreEqual(
            2L,
            state.Count(
                "SELECT COUNT(*) FROM credit_reports WHERE reservation_id=$id;",
                ("$id", original.Id)
            )
        );
        CreditReservation fresh = state.Reserve("worker2", shared: false, allowance: 100);
        Assert.AreNotEqual(original.Id, fresh.Id);
        Assert.AreNotEqual(original.SessionId, fresh.SessionId);
        state.AssertAccounting(180, 150, 50, 0);
        state.Start(fresh);
        state.Dispatch(fresh);
        state.Ledger.MarkUncertain(fresh.Id);
        CreditReservation uncertain = state.Read(fresh.Id);
        Assert.AreEqual(
            "credit_unknown",
            Assert
                .ThrowsExactly<OperationException>(() =>
                    state.Reserve("worker3", shared: false, allowance: 30)
                )
                .Code
        );
        Assert.AreEqual(
            "credit_unknown",
            Assert
                .ThrowsExactly<OperationException>(() =>
                    state.Ledger.ContinueExact(original.Id, state.NewEvidencePath())
                )
                .Code
        );
        Assert.AreEqual(uncertain, state.Read(fresh.Id));
        Assert.AreEqual(50m, state.Read(original.Id).Actual);
        Assert.AreEqual("reconciled", state.Read(original.Id).State);
        Assert.AreEqual(unrelated, state.Read(unrelated.Id));
        state.AssertAccounting(180, 150, 50, 0, uncertain: true);
    }

    [TestMethod]
    [TestCategory("CapAndUsageConservation")]
    public void SeparatelyBackedFreshReservationCannotEraseOlderUncertainty()
    {
        using CreditWindowState state = new(230, 200);
        CreditReservation unrelated = state.Unrelated();
        CreditReservation original = state.Reserve("worker1", shared: false, allowance: 100);
        CreditReservation fresh = state.Reserve("worker2", shared: false, allowance: 100);
        state.Start(original);
        state.Dispatch(original);
        state.Ledger.RecordSdkUsage(original.Id, Usage(original, 35, 2));
        state.Ledger.MarkUncertain(original.Id);
        CreditReservation unknown = state.Read(original.Id);
        state.Start(fresh);
        CreditReservation freshBefore = state.Read(fresh.Id);
        Assert.AreNotEqual(original.SessionId, fresh.SessionId);
        Assert.AreEqual(
            "credit_unknown",
            Assert.ThrowsExactly<OperationException>(() => state.Dispatch(fresh)).Code
        );
        Assert.AreEqual(
            "credit_unknown",
            Assert
                .ThrowsExactly<OperationException>(() =>
                    state.Ledger.ContinueExact(original.Id, state.NewEvidencePath())
                )
                .Code
        );
        Assert.AreEqual(unknown, state.Read(original.Id));
        Assert.AreEqual(freshBefore, state.Read(fresh.Id));
        Assert.AreEqual(
            0L,
            state.Count(
                "SELECT dispatch_count FROM credit_sdk_usage WHERE reservation_id=$id;",
                ("$id", fresh.Id)
            )
        );
        Assert.AreEqual(1L, state.Count("SELECT COUNT(*) FROM credit_reports;"));
        Assert.AreEqual(unrelated, state.Read(unrelated.Id));
        state.AssertAccounting(230, 200, 35, 0, uncertain: true);
    }

    [TestMethod]
    [TestCategory("CapAndUsageConservation")]
    [DataRow(45, 0)]
    [DataRow(110, 10)]
    [Timeout(120_000, CooperativeCancellation = true)]
    public async Task SettledRecoveryBooksOnlyCumulativeIncreaseWithoutReopeningClosedWindow(
        int total,
        int overshoot
    )
    {
        using CreditWindowState state = new(230, 200);
        CreditReservation unrelated = state.Unrelated();
        CreditReservation reservation = state.Reserve("worker1");
        await state
            .Ledger.AcquireSharedWindowAsync(reservation.Id, context.CancellationToken)
            .ConfigureAwait(false);
        state.Start(reservation);
        state.Dispatch(reservation);
        state.Ledger.RecordSdkUsage(reservation.Id, Usage(reservation, 35, 2));
        state.Ledger.CloseSharedWindow(reservation.Id);
        state.Ledger.MarkUncertain(reservation.Id);
        CreditReservation uncertain = state.Read(reservation.Id);
        state.AssertAccounting(65, 35, 35, 0, uncertain: true);
        Assert.AreEqual(
            "credit_unknown",
            Assert
                .ThrowsExactly<OperationException>(() =>
                    state.Ledger.RecordSdkUsage(
                        reservation.Id,
                        Usage(reservation, total, 3),
                        final: true
                    )
                )
                .Code
        );
        Assert.AreEqual(uncertain, state.Read(reservation.Id));
        RecoveredNativeUsage recovery = new(uncertain, Usage(reservation, total, 3));
        state.Ledger.RecordRecoveredSdkUsage(recovery);
        state.Ledger.RecordRecoveredSdkUsage(recovery);
        CreditReservation reconciled = state.Read(reservation.Id);
        Assert.AreEqual("reconciled", reconciled.State);
        Assert.AreEqual((decimal)total, reconciled.Actual);
        Assert.AreEqual(100m, reconciled.Allowance);
        Assert.AreEqual((decimal)overshoot, reconciled.Overshoot);
        Assert.IsFalse(reconciled.GrantOpen);
        Assert.AreEqual(
            2L,
            state.Count(
                "SELECT COUNT(*) FROM credit_reports WHERE reservation_id=$id;",
                ("$id", reservation.Id)
            )
        );
        Assert.AreEqual(
            1L,
            state.Count(
                "SELECT dispatch_count FROM credit_sdk_usage WHERE reservation_id=$id;",
                ("$id", reservation.Id)
            )
        );
        state.AssertAccounting(30 + total, total, total, 0);
        Assert.AreEqual(
            (decimal)overshoot,
            state.Ledger.Status("run", state.RunAllowance)["overshoot"]!.GetValue<decimal>()
        );
        Assert.AreEqual(unrelated, state.Read(unrelated.Id));
    }

    [TestMethod]
    [TestCategory("CapAndUsageConservation")]
    public void RevisedFinalUsageRetainsBookedAmountAndRestoresConservativeHold()
    {
        using CreditWindowState state = new(130, 100);
        CreditReservation unrelated = state.Unrelated();
        CreditReservation reservation = state.Reserve("worker1", shared: false, allowance: 100);
        state.Start(reservation);
        state.Dispatch(reservation);
        state.Ledger.RecordSdkUsage(reservation.Id, Usage(reservation, 35, 2), final: true);
        CreditReservation final = state.Read(reservation.Id);
        state.AssertAccounting(65, 35, 35, 0);
        Assert.AreEqual(
            "credit_unknown",
            Assert
                .ThrowsExactly<OperationException>(() =>
                    state.Ledger.RecordSdkUsage(
                        reservation.Id,
                        Usage(reservation, 36, 3),
                        final: true
                    )
                )
                .Code
        );
        Assert.AreEqual(final with { State = "uncertain" }, state.Read(reservation.Id));
        state.AssertAccounting(130, 100, 35, 0, uncertain: true);
        Assert.AreEqual(1L, state.Count("SELECT COUNT(*) FROM credit_reports;"));
        Assert.AreEqual(
            "credit_unknown",
            Assert.ThrowsExactly<OperationException>(() => state.Ledger.RequirePaidAdmission()).Code
        );
        Assert.AreEqual(unrelated, state.Read(unrelated.Id));
    }

    [TestMethod]
    [TestCategory("CapAndUsageConservation")]
    [DataRow("association")]
    [DataRow("regression")]
    [DataRow("partial")]
    [Timeout(120_000, CooperativeCancellation = true)]
    public async Task RejectedRecoveryPreservesClosedWindowAndUnknownDisposition(string kind)
    {
        using CreditWindowState state = new(230, 200);
        CreditReservation unrelated = state.Unrelated();
        CreditReservation reservation = state.Reserve("worker1");
        await state
            .Ledger.AcquireSharedWindowAsync(reservation.Id, context.CancellationToken)
            .ConfigureAwait(false);
        state.Start(reservation);
        state.Dispatch(reservation);
        state.Ledger.RecordSdkUsage(reservation.Id, Usage(reservation, 35, 2));
        state.Ledger.CloseSharedWindow(reservation.Id);
        state.Ledger.MarkUncertain(reservation.Id);
        CreditReservation uncertain = state.Read(reservation.Id);
        RecoveredNativeUsage recovery = kind switch
        {
            "association" => new(uncertain with { Worker = "worker2" }, Usage(reservation, 45, 3)),
            "regression" => new(uncertain, Usage(reservation, 34, 3)),
            "partial" => new(uncertain, new(reservation.SessionId!, 45_000_000_000, "partial", 3)),
            _ => throw new ArgumentOutOfRangeException(nameof(kind)),
        };
        Assert.AreEqual(
            "credit_unknown",
            Assert
                .ThrowsExactly<OperationException>(() =>
                    state.Ledger.RecordRecoveredSdkUsage(recovery)
                )
                .Code
        );
        Assert.AreEqual(uncertain, state.Read(reservation.Id));
        state.AssertAccounting(65, 35, 35, 0, uncertain: true);
        Assert.AreEqual(
            1L,
            state.Count(
                "SELECT COUNT(*) FROM credit_reports WHERE reservation_id=$id;",
                ("$id", reservation.Id)
            )
        );
        Assert.AreEqual(
            "credit_unknown",
            Assert.ThrowsExactly<OperationException>(() => state.Ledger.RequirePaidAdmission()).Code
        );
        Assert.AreEqual(unrelated, state.Read(unrelated.Id));
    }

    [TestMethod]
    [TestCategory("CapAndUsageConservation")]
    [DataRow("final-flag", "outside its backed shared spending window")]
    [DataRow("association", "Settled recovery association changed")]
    [DataRow("revised-final", "revised a final total")]
    [Timeout(120_000, CooperativeCancellation = true)]
    public async Task ClosedWindowMonotonicFinalChargeLedgerTupleGuardPreservesBookedSpend(
        string fault,
        string reason
    )
    {
        using CreditWindowState state = new(230, 200);
        CreditReservation peer = state.Unrelated();
        CreditReservation reservation = state.Reserve("worker1");
        await state.Ledger
            .AcquireSharedWindowAsync(reservation.Id, context.CancellationToken)
            .ConfigureAwait(false);
        state.Start(reservation);
        state.Dispatch(reservation);
        state.Ledger.RecordSdkUsage(reservation.Id, Usage(reservation, 35, 2));
        state.Ledger.CloseSharedWindow(reservation.Id);
        state.Ledger.MarkUncertain(reservation.Id);
        RecoveredNativeUsage settled = new(
            state.Read(reservation.Id), Usage(reservation, 45, 3)
        );
        if (fault == "revised-final")
        {
            state.Ledger.RecordRecoveredSdkUsage(settled);
        }
        CreditReservation before = state.Read(reservation.Id);
        OperationException failure = Assert.ThrowsExactly<OperationException>(() =>
        {
            switch (fault)
            {
                case "final-flag":
                    state.Ledger.RecordSdkUsage(before.Id, settled.Usage, final: true);
                    break;
                case "association":
                    state.Ledger.RecordRecoveredSdkUsage(
                        settled with
                        {
                            Reservation = settled.Reservation with { Worker = "worker2" },
                        }
                    );
                    break;
                case "revised-final":
                    state.Ledger.RecordRecoveredSdkUsage(
                        settled with { Usage = Usage(reservation, 46, 3) }
                    );
                    break;
            }
        });
        Assert.Contains(reason, failure.Message);
        CreditReservation after = state.Read(reservation.Id);
        Assert.AreEqual(before.Actual, after.Actual);
        Assert.AreEqual(before.Allowance, after.Allowance);
        Assert.AreEqual(before.Overshoot, after.Overshoot);
        Assert.AreEqual(before.SessionId, after.SessionId);
        Assert.AreEqual("uncertain", after.State);
        Assert.IsFalse(after.GrantOpen);
        Assert.AreEqual(peer, state.Read(peer.Id));
    }

    [TestMethod]
    [TestCategory("CapAndUsageConservation")]
    public void MismatchedAttachCannotReplaceReservedSessionAndDispatchRequiresVerifiedAssociation()
    {
        using CreditWindowState state = new(130, 100);
        CreditReservation unrelated = state.Unrelated();
        CreditReservation reservation = state.Reserve("worker1", shared: false, allowance: 100);
        state.Ledger.MarkStarted(reservation.Id);
        CreditReservation started = state.Read(reservation.Id);
        Assert.ThrowsExactly<InvalidOperationException>(() =>
            state.Ledger.AttachSession(reservation.Id, "wrong-session")
        );
        Assert.AreEqual(started, state.Read(reservation.Id));
        Assert.AreEqual(
            "credit_unknown",
            Assert.ThrowsExactly<OperationException>(() => state.Dispatch(reservation)).Code
        );
        Assert.AreEqual(
            0L,
            state.Count(
                "SELECT dispatch_count FROM credit_sdk_usage WHERE reservation_id=$id;",
                ("$id", reservation.Id)
            )
        );
        state.Ledger.AttachSession(reservation.Id, reservation.SessionId!);
        state.Dispatch(reservation);
        state.Ledger.RecordSdkUsage(reservation.Id, Usage(reservation, 25, 1), final: true);
        Assert.AreEqual(reservation.SessionId, state.Read(reservation.Id).SessionId);
        state.AssertAccounting(55, 25, 25, 0);
        Assert.AreEqual(unrelated, state.Read(unrelated.Id));
    }

    [TestMethod]
    [TestCategory("SharedBackingAndReuse")]
    [Timeout(120_000, CooperativeCancellation = true)]
    public async Task AdmissionClosingBeforeWriterHandoffCannotCreateDurableGrant()
    {
        using CreditWindowState state = new(100m, 100m);
        CreditReservation reserved = state.Reserve("worker2");
        state.AssertAccounting(0m, 0m, 0m, 0m);
        Assert.AreEqual(0L, state.Count("SELECT COUNT(*) FROM credit_reports;"));
        ManualScenarioClock clock = new();
        Activity acquisition = await AssertAdmissionCancelsAtWriterHandoffAsync(
            state, clock, reserved.Worker, "TryAcquireSharedWindow",
            (admission, cancellationToken) => state.Ledger.AcquireSharedWindowAsync(
                reserved.Id, cancellationToken, admission: admission
            )
        ).ConfigureAwait(false);

        // The grant represents the broader acquisition telemetry contract for both paths.
        Assert.AreEqual("sqlite", acquisition.GetTagItem("db.system.name"));
        foreach (string tag in new[]
        {
            "liteagensh.sqlite.database_id",
            "liteagensh.sqlite.connection_id",
        })
        {
            Assert.IsNotEmpty(Assert.IsInstanceOfType<string>(acquisition.GetTagItem(tag)));
        }
        ActivityEvent request = Assert.ContainsSingle(
            acquisition.Events.Where(item => item.Name == "acquisition-requested")
        );
        Assert.AreEqual(false, request.Tags.Single(
            item => item.Key == "liteagensh.sqlite.deferred"
        ).Value);
        Assert.AreEqual(false, request.Tags.Single(
            item => item.Key == "liteagensh.sqlite.write_acquisition_observed"
        ).Value);
        Assert.DoesNotContain(
            "acquisition-failed", acquisition.Events.Select(item => item.Name)
        );

        Assert.AreEqual(reserved, state.Read(reserved.Id));
        Assert.HasCount(1, state.Ledger.Reservations("run"));
        state.AssertAccounting(0m, 0m, 0m, 0m);
        Assert.AreEqual(0L, state.Count("SELECT COUNT(*) FROM credit_reports;"));
        Assert.AreEqual(0L, state.Count("SELECT SUM(dispatch_count) FROM credit_sdk_usage;"));

        using WorkAdmission open = new(
            clock, clock.GetUtcNow().AddMinutes(1), CancellationToken.None
        );
        CreditReservation granted = await state.Ledger.AcquireSharedWindowAsync(
            reserved.Id, context.CancellationToken, admission: open
        ).ConfigureAwait(false);
        Assert.AreEqual(reserved.Id, granted.Id);
        Assert.AreEqual(100m, granted.Allowance);
        Assert.IsTrue(state.Read(reserved.Id).GrantOpen);
        state.AssertAccounting(100m, 100m, 0m, 100m);
        Assert.AreEqual(0L, state.Count("SELECT COUNT(*) FROM credit_reports;"));
    }

    [TestMethod]
    [TestCategory("CapAndUsageConservation")]
    [Timeout(120_000, CooperativeCancellation = true)]
    public async Task AdmissionClosingBeforeWriterHandoffCannotRecordDurableDispatchIntent()
    {
        using CreditWindowState state = new(130m, 100m);
        CreditReservation unrelated = state.Unrelated();
        CreditReservation seat = state.Reserve("worker2");
        await state.Ledger.AcquireSharedWindowAsync(seat.Id, context.CancellationToken)
            .ConfigureAwait(false);
        state.Start(seat);
        CreditReservation backed = seat with
        {
            Allowance = 100m,
            State = "active",
            GrantOpen = true,
        };
        AssertSeat(dispatches: 0L, status: null);

        ManualScenarioClock clock = new();
        await AssertAdmissionCancelsAtWriterHandoffAsync(
            state, clock, seat.Worker, nameof(CreditLedger.BeginDispatch),
            (admission, _) =>
            {
                state.Ledger.BeginDispatch(seat.RunId, seat.Id, state.RunAllowance, admission);
                return Task.CompletedTask;
            }
        ).ConfigureAwait(false);
        AssertSeat(dispatches: 0L, status: null);

        using WorkAdmission open = new(
            clock, clock.GetUtcNow().AddMinutes(1), CancellationToken.None
        );
        state.Ledger.BeginDispatch(seat.RunId, seat.Id, state.RunAllowance, open);
        AssertSeat(dispatches: 1L, status: "pending");

        void AssertSeat(long dispatches, string? status)
        {
            Assert.AreEqual(backed, state.Read(seat.Id));
            Assert.AreEqual(unrelated, state.Read(unrelated.Id));
            Assert.AreEqual(
                1L,
                state.Count(
                    """
                    SELECT COUNT(*) FROM credit_sdk_usage u
                    JOIN credit_sdk_owners o ON o.reservation_id=u.reservation_id
                    WHERE u.reservation_id=$id AND u.session_id=$session
                        AND u.dispatch_count=$dispatches AND u.status IS $status
                        AND u.reported_nano_aiu IS NULL AND u.reported_requests IS NULL;
                    """,
                    ("$id", seat.Id),
                    ("$session", seat.SessionId),
                    ("$dispatches", dispatches),
                    ("$status", status)
                ),
                $"Expected the associated usage row with {dispatches} dispatches, "
                    + $"status {status ?? "NULL"} and no usage reading."
            );
            Assert.AreEqual(0L, state.Count("SELECT COUNT(*) FROM credit_reports;"));
            state.AssertAccounting(130m, 100m, 0m, 100m);
        }
    }

    private async Task<Activity> AssertAdmissionCancelsAtWriterHandoffAsync(
        CreditWindowState state,
        ManualScenarioClock clock,
        string worker,
        string operation,
        Func<WorkAdmission, CancellationToken, Task> contend
    )
    {
        using WorkAdmission admission = new(
            clock, clock.GetUtcNow().AddSeconds(1), CancellationToken.None
        );
        CancellationToken admitted = admission.Token;
        using CancellationTokenSource cancellation =
            CancellationTokenSource.CreateLinkedTokenSource(context.CancellationToken);
        ActivityTraceId traceId = ActivityTraceId.CreateRandom();
        TaskCompletionSource<Activity> requested = new(
            TaskCreationOptions.RunContinuationsAsynchronously
        );
        using ActivityListener listener = new()
        {
            ShouldListenTo = source => source.Name == "LiteAgensh",
            Sample = (ref ActivityCreationOptions<ActivityContext> options) =>
                options.Parent.TraceId == traceId && options.Name == "sqlite.acquisition"
                    ? ActivitySamplingResult.AllDataAndRecorded
                    : ActivitySamplingResult.None,
            ActivityStarted = activity =>
            {
                // Entry precedes the provider and diagnostic tags; qualify it after joining.
                if (activity.TraceId == traceId && activity.OperationName == "sqlite.acquisition"
                    && activity.Parent?.OperationName == operation)
                {
                    requested.TrySetResult(activity);
                }
            },
        };
        ActivitySource.AddActivityListener(listener);
        Task? pending = null;
        try
        {
            string heldTransaction;
            using (SqliteDiagnostics.UseActor("owner"))
            using (SqliteConnection writer = Sql.Open(
                new SqliteConnection(new SqliteConnectionStringBuilder
                {
                    DataSource = state.LedgerPath,
                    Mode = SqliteOpenMode.ReadWrite,
                    Pooling = false,
                }.ToString()),
                "campaign"
            ))
            using (SqliteTransactionScope held = Sql.BeginTransaction(
                writer, deferred: false, operation: "HeldWriter"
            ))
            {
                heldTransaction = held.Id;
                pending = Task.Run(async () =>
                {
                    using Activity scope = new Activity(operation)
                        .SetIdFormat(ActivityIdFormat.W3C)
                        .SetParentId(
                            traceId, ActivitySpanId.CreateRandom(), ActivityTraceFlags.Recorded
                        ).Start();
                    using IDisposable actor = SqliteDiagnostics.UseActor(worker);
                    await contend(admission, cancellation.Token).ConfigureAwait(false);
                });
                Task first = await Task.WhenAny(requested.Task, pending)
                    .WaitAsync(TimeSpan.FromSeconds(5), context.CancellationToken)
                    .ConfigureAwait(false);
                if (ReferenceEquals(first, pending))
                {
                    await pending.ConfigureAwait(false);
                    Assert.Fail($"{operation} returned before its writer entry was observed.");
                }
                Assert.IsFalse(pending.IsCompleted);
                Assert.IsFalse(admitted.IsCancellationRequested);
                Assert.IsFalse(cancellation.IsCancellationRequested);
                clock.Advance(TimeSpan.FromSeconds(1));
                Assert.IsTrue(admitted.IsCancellationRequested);
                Assert.IsFalse(cancellation.IsCancellationRequested);
            }

            OperationCanceledException error = await Assert.ThrowsAsync<OperationCanceledException>(
                async () => await pending.ConfigureAwait(false)
            ).ConfigureAwait(false);
            Assert.AreEqual(admitted, error.CancellationToken);
            Assert.IsFalse(context.CancellationToken.IsCancellationRequested);
            Activity acquisition = await requested.Task.ConfigureAwait(false);
            Assert.AreEqual(operation, acquisition.GetTagItem("db.operation.name"));
            Assert.AreEqual(worker, acquisition.GetTagItem("liteagensh.actor"));
            string transactionId = Assert.IsInstanceOfType<string>(
                acquisition.GetTagItem("liteagensh.sqlite.transaction_id")
            );
            Assert.IsNotEmpty(transactionId);
            Assert.AreNotEqual(heldTransaction, transactionId);
            ActivityEvent acquired = Assert.ContainsSingle(
                acquisition.Events.Where(item => item.Name == "acquisition-succeeded")
            );
            Assert.AreEqual(false, acquired.Tags.Single(
                item => item.Key == "liteagensh.sqlite.deferred"
            ).Value);
            Assert.AreEqual(true, acquired.Tags.Single(
                item => item.Key == "liteagensh.sqlite.write_acquisition_observed"
            ).Value);
            return acquisition;
        }
        catch (Exception primary)
        {
            try
            {
                await Task.WhenAll(cancellation.CancelAsync(), pending ?? Task.CompletedTask)
                    .ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (cancellation.IsCancellationRequested)
            {
            }
            catch (Exception secondary)
            {
                if (!ReferenceEquals(primary, secondary))
                {
                    primary.Data["CreditWindowContenderCleanup"] = Json.ExceptionDetails(secondary);
                }
            }

            throw;
        }
    }

    private static NativeUsage Usage(CreditReservation row, decimal credits, long requests) =>
        new(row.SessionId!, (double)(credits * 1_000_000_000m), "complete", requests);

    private static async Task Drain(Task task)
    {
        try
        {
            await task.ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            // Cleanup only. The scenario asserts cancellation or success before this boundary.
        }
        catch (OperationException)
        {
            // Preserve the original assertion/fault; never leave an acquisition using deleted
            // state.
        }
    }

    private static JsonObject AssertCommittedContenderAttempt(CreditWindowState state)
    {
        using IDisposable actor = SqliteDiagnostics.UseActor("owner");
        using SqliteConnection connection = Sql.Open(
            new SqliteConnection(
                new SqliteConnectionStringBuilder
                {
                    DataSource = state.LedgerPath,
                    Pooling = false,
                }.ToString()
            ),
            "campaign"
        );
        // Capture runs inside the existing database diagnostic gate and produces an
        // immutable incident. Reading the live activity file could race its next rewrite.
        // This controlled notification is not a real storage failure or wait witness:
        // only the earlier worker2 acquisition/commit pair establishes the attempt.
        SqliteException notification = new(
            "Controlled diagnostic snapshot, not an actual storage failure.",
            10,
            522
        );
        SqliteException observed = Assert.ThrowsExactly<SqliteException>(() =>
            SqliteDiagnostics.Observe<int>(
                connection,
                "WaitWitness",
                "statement",
                () => throw notification
            )
        );
        Assert.AreSame(notification, observed);
        string path = Assert.IsInstanceOfType<string>(observed.Data["LiteAgensh.SqliteIncident"]);
        JsonObject incident = JsonNode.Parse(File.ReadAllText(path))!.AsObject();
        Assert.IsNull(incident["warning"]);
        JsonObject[] snapshots = incident["sources"]!
            .AsArray()
            .OfType<JsonObject>()
            .Where(source => source["status"]!.GetValue<string>() == "available")
            .Select(source => source["snapshot"]!.AsObject())
            .Where(source => source["processId"]!.GetValue<int>() == Environment.ProcessId)
            .ToArray();
        Assert.HasCount(1, snapshots, incident.ToJsonString());
        JsonObject snapshot = snapshots[0];
        Assert.AreEqual("campaign", snapshot["kind"]!.GetValue<string>());
        JsonObject[] records = snapshot["recent"]!.AsArray().OfType<JsonObject>().ToArray();
        JsonObject[] commits = records
            .Where(item =>
                item["actor"]?.GetValue<string>() == "worker2"
                && item["operation"]?.GetValue<string>() == "TryAcquireSharedWindow"
                && item["phase"]?.GetValue<string>() == "commit-succeeded"
            )
            .ToArray();
        Assert.IsNotEmpty(
            commits,
            "No actor-correlated committed acquisition attempt; waiting is not established."
        );
        JsonObject? matched = commits.FirstOrDefault(commit =>
        {
            string transaction = commit["transaction"]!.GetValue<string>();
            string connection = commit["connection"]!.GetValue<string>();
            return !commit["writeAcquisitionObserved"]!.GetValue<bool>()
                && records.Any(item =>
                    item["actor"]?.GetValue<string>() == "worker2"
                    && item["transaction"]?.GetValue<string>() == transaction
                    && item["connection"]?.GetValue<string>() == connection
                    && item["operation"]?.GetValue<string>() == "TryAcquireSharedWindow"
                    && item["phase"]?.GetValue<string>() == "acquisition-succeeded"
                );
        });
        Assert.IsNotNull(
            matched,
            "At least one complete actor-correlated acquisition/commit pair is required."
        );
        JsonObject acquisition = records.First(item =>
            item["actor"]?.GetValue<string>() == "worker2"
            && item["transaction"]?.GetValue<string>() == matched["transaction"]!.GetValue<string>()
            && item["connection"]?.GetValue<string>() == matched["connection"]!.GetValue<string>()
            && item["operation"]?.GetValue<string>() == "TryAcquireSharedWindow"
            && item["phase"]?.GetValue<string>() == "acquisition-succeeded"
        );
        return new()
        {
            ["acquisition"] = acquisition.DeepClone(),
            ["commit"] = matched.DeepClone(),
        };
    }
}
