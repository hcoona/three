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
    public async Task SimultaneousAdmissionsPersistCompatibleGrantsWithoutDoubleBacking()
    {
        using CreditWindowState state = new(180, 150);
        CreditReservation unrelated = state.Unrelated();
        CreditReservation first = state.Reserve("worker1");
        CreditReservation second = state.Reserve("worker2");
        using CancellationTokenSource cancellation =
            CancellationTokenSource.CreateLinkedTokenSource(context.CancellationToken);
        TaskCompletionSource release = new(TaskCreationOptions.RunContinuationsAsynchronously);
        Task<CreditReservation> left = Task.Run(
            async () =>
            {
                await release.Task.ConfigureAwait(false);
                return await state
                    .Ledger.AcquireSharedWindowAsync(first.Id, cancellation.Token)
                    .ConfigureAwait(false);
            },
            context.CancellationToken
        );
        Task<CreditReservation> right = Task.Run(
            async () =>
            {
                await release.Task.ConfigureAwait(false);
                return await state
                    .Ledger.AcquireSharedWindowAsync(second.Id, cancellation.Token)
                    .ConfigureAwait(false);
            },
            context.CancellationToken
        );
        try
        {
            release.SetResult();
            await Task.WhenAll(left, right).ConfigureAwait(false);
            CreditReservation[] durable = state.Ledger.Reservations("run").ToArray();
            decimal[] grantedAmounts = durable.Select(row => row.Allowance).Order().ToArray();
            Assert.HasCount(2, grantedAmounts);
            Assert.AreEqual(50m, grantedAmounts[0]);
            Assert.AreEqual(100m, grantedAmounts[1]);
            Assert.AreEqual(await left.ConfigureAwait(false), state.Read(first.Id));
            Assert.AreEqual(await right.ConfigureAwait(false), state.Read(second.Id));
            Assert.IsTrue(
                durable.All(row => row.Shared && row.GrantOpen && row.State == "reserved")
            );
            Assert.AreEqual(0L, state.Count("SELECT SUM(dispatch_count) FROM credit_sdk_usage;"));
            state.AssertAccounting(180, 150, 0, 150);
            Assert.AreEqual(0m, state.RunAllowance - durable.Sum(row => row.Allowance));
            Assert.AreEqual(unrelated, state.Read(unrelated.Id));
        }
        finally
        {
            release.TrySetResult();
            await cancellation.CancelAsync().ConfigureAwait(false);
            await Drain(left).ConfigureAwait(false);
            await Drain(right).ConfigureAwait(false);
        }
    }

    [TestMethod]
    [TestCategory("SharedBackingAndReuse")]
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
    [TestCategory("WaitThenCancelOrAdmit")]
    [DataRow(false)]
    [DataRow(true)]
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
