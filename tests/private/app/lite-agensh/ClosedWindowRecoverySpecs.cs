using LiteAgensh.Tests.Support;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace LiteAgensh.Tests;

[TestClass]
[DoNotParallelize]
public sealed class ClosedWindowRecoverySpecs(TestContext context)
{
    [TestMethod]
    [DataRow(247)]
    [DataRow(347)]
    [Timeout(120_000)]
    public async Task ClosedWindowMonotonicFinalChargeOriginalLifetimeSettledDeltaWithoutNewWork(
        int final
    )
    {
        RecoveryLifetimeControl state = await RecoveryLifetimeControl.CreateAsync(
            context.CancellationToken,
            shared: true
        );
        await state.RunBodyAsync(async () =>
        {
            state.AssertOriginalLifetime();
            var original = state.Reservation;
            var peer = state.Peer;
            var before = state.Ledger.Status(state.Database.RunId, 607.25m);
            Assert.IsFalse(original.GrantOpen);
            state.FreshNano = final * 1_000_000_000d;

            var recovered = await state.RecoverAsync(context.CancellationToken);
            Assert.AreEqual(
                original,
                state.Reservation,
                "A read is not yet a financial disposition."
            );
            state.Ledger.RecordRecoveredSdkUsage(recovered);

            Assert.AreEqual("reconciled", state.Reservation.State);
            Assert.AreEqual((decimal)final, state.Reservation.Actual);
            Assert.AreEqual(original.Allowance, state.Reservation.Allowance);
            Assert.AreEqual(Math.Max(0, final - 300m), state.Reservation.Overshoot);
            Assert.AreEqual(peer, state.Peer);
            Assert.AreEqual((decimal)final, recovered.Usage.RequireComplete());
            var after = state.Ledger.Status(state.Database.RunId, 607.25m);
            Assert.AreEqual(
                final - 200m,
                after["actualReported"]!.GetValue<decimal>()
                    - before["actualReported"]!.GetValue<decimal>()
            );
            state.Ledger.RecordRecoveredSdkUsage(recovered);
            Assert.AreEqual(
                after.ToJsonString(),
                state.Ledger.Status(state.Database.RunId, 607.25m).ToJsonString()
            );
            Assert.AreEqual(original.SessionId, recovered.Usage.SessionId);
            Assert.AreEqual(300m, state.RecoveryHosts.Single().Sessions.Single().ExactCap);
            Assert.AreEqual(
                "worker2",
                Assert
                    .ContainsSingle(
                        state
                            .Ledger.Reservations(state.Database.RunId)
                            .Where(row => row.State == "uncertain")
                    )
                    .Worker
            );
            var paidGate = Assert.ThrowsExactly<OperationException>(
                state.Ledger.RequirePaidAdmission
            );
            Assert.AreEqual(
                "Unknown campaign usage blocks all new paid work, including calibration.",
                paidGate.Message
            );
        });
    }

    [TestMethod]
    [DataRow("partial", "absent, invalid or incomplete")]
    [DataRow("regression", "Fresh recovery usage regressed")]
    [DataRow("requests-regress", "Fresh recovery usage regressed")]
    [DataRow("session-stop", "actual recovery session could not settle")]
    [DataRow("host-stop", "actual owned recovery host could not settle")]
    [DataRow("background", "Fresh recovery background compaction failed")]
    [Timeout(120_000)]
    public async Task ClosedWindowMonotonicFinalChargeEligibleThenOneFaultPreservesOriginalBacking(
        string fault,
        string reason
    )
    {
        RecoveryLifetimeControl state = await RecoveryLifetimeControl.CreateAsync(
            context.CancellationToken,
            shared: true
        );
        await state.RunBodyAsync(async () =>
        {
            state.AssertOriginalLifetime();
            Assert.IsFalse(state.Reservation.GrantOpen);
            Assert.AreEqual(
                247m,
                (await state.RecoverAsync(context.CancellationToken)).Usage.RequireComplete()
            );
            var before = state.FinancialSnapshot.ToJsonString();
            state.RecoveryCase = fault;
            if (fault == "regression")
            {
                state.FreshNano = 199_000_000_000d;
            }
            var failure = await Assert.ThrowsExactlyAsync<OperationException>(() =>
                state.RecoverAsync(context.CancellationToken)
            );
            Assert.Contains(reason, failure.ToString());
            Assert.AreEqual(before, state.FinancialSnapshot.ToJsonString());
            Assert.AreSame(state.OriginalFailure, state.Original.FirstFailure);
        });
    }

    [TestMethod]
    [DataRow("missing-start", "Required original native limit evidence is missing")]
    [DataRow("nonzero-origin", "compatible original zero-origin and verified cap evidence")]
    [DataRow("start-session", "compatible original zero-origin and verified cap evidence")]
    [DataRow("lineage", "compatible original zero-origin and verified cap evidence")]
    [DataRow("lagging-cap", "compatible original zero-origin and verified cap evidence")]
    [DataRow("changed-sdk", "compatible original zero-origin and verified cap evidence")]
    [Timeout(120_000)]
    public async Task ZeroOriginUsesVerifiedCeilingEligibleThenSavedObservationFaultNoActivation(
        string fault,
        string reason
    )
    {
        RecoveryLifetimeControl state = await RecoveryLifetimeControl.CreateAsync(
            context.CancellationToken
        );
        await state.RunBodyAsync(async () =>
        {
            state.AssertOriginalLifetime();
            Assert.AreEqual(
                247m,
                (await state.RecoverAsync(context.CancellationToken)).Usage.RequireComplete()
            );
            state.DamageSavedObservation(fault);
            var before = state.FinancialSnapshot.ToJsonString();
            var failure = await Assert.ThrowsExactlyAsync<OperationException>(() =>
                state.RecoverAsync(context.CancellationToken)
            );
            Assert.Contains(reason, failure.ToString());
            Assert.HasCount(
                1,
                state.RecoveryHosts,
                "The failed saved observation must reject before another host."
            );
            Assert.AreEqual(before, state.FinancialSnapshot.ToJsonString());
        });
    }

    [TestMethod]
    [DataRow("1.0.106")]
    [DataRow("1.0.108")]
    [Timeout(120_000)]
    public async Task SelectedVersionBridgeOtherOriginalOrderedPairRejectsWithoutRewriting(
        string original
    )
    {
        RecoveryLifetimeControl state = await RecoveryLifetimeControl.CreateAsync(
            context.CancellationToken,
            version: original
        );
        await state.RunBodyAsync(async () =>
        {
            state.AssertOriginalLifetime();
            var before = state.FinancialSnapshot.ToJsonString();
            var failure = await Assert.ThrowsExactlyAsync<OperationException>(() =>
                state.RecoverAsync(context.CancellationToken)
            );
            Assert.Contains(
                "compatible original zero-origin and verified cap evidence",
                failure.ToString()
            );
            Assert.AreEqual(
                original,
                state.Original.Metadata!["runtimeVersion"]!.GetValue<string>()
            );
            Assert.IsEmpty(state.RecoveryHosts);
            Assert.AreEqual(before, state.FinancialSnapshot.ToJsonString());
        });
    }

    [TestMethod]
    [DataRow("live-version", "compatible original zero-origin and verified cap evidence")]
    [DataRow("live-cap", "limit")]
    [Timeout(120_000)]
    public async Task SelectedVersionBridgeEligibleThenLiveMismatchNoDisposition(
        string fault,
        string reason
    )
    {
        RecoveryLifetimeControl state = await RecoveryLifetimeControl.CreateAsync(
            context.CancellationToken,
            version: "1.0.105"
        );
        await state.RunBodyAsync(async () =>
        {
            state.AssertOriginalLifetime();
            Assert.AreEqual(
                247m,
                (await state.RecoverAsync(context.CancellationToken)).Usage.RequireComplete()
            );
            var before = state.FinancialSnapshot.ToJsonString();
            state.RecoveryCase = fault;
            var failure = await Assert.ThrowsExactlyAsync<OperationException>(() =>
                state.RecoverAsync(context.CancellationToken)
            );
            Assert.Contains(reason, failure.ToString());
            Assert.AreEqual(before, state.FinancialSnapshot.ToJsonString());
            Assert.AreEqual(
                "1.0.105",
                state.Original.Metadata!["runtimeVersion"]!.GetValue<string>()
            );
        });
    }

    [TestMethod]
    [Timeout(120_000)]
    public async Task ZeroOriginUsesVerifiedCeilingOriginal300Spent200ExactCapRemains300()
    {
        RecoveryLifetimeControl state = await RecoveryLifetimeControl.CreateAsync(
            context.CancellationToken
        );
        await state.RunBodyAsync(async () =>
        {
            state.AssertOriginalLifetime();
            Assert.AreEqual(100m, state.Reservation.Allowance - state.Reservation.Actual);
            var before = state.FinancialSnapshot.ToJsonString();

            var recovered = await state.RecoverAsync(context.CancellationToken);

            var dependency = state.RecoveryHosts.Single().Sessions.Single();
            Assert.AreEqual(300m, dependency.ExactCap);
            Assert.AreEqual(state.Original.SessionId, dependency.ExactSession);
            Assert.AreEqual(300d, dependency.Metadata!["appliedLimit"]!.GetValue<double>());
            Assert.AreEqual(247m, recovered.Usage.RequireComplete());
            Assert.AreEqual(before, state.FinancialSnapshot.ToJsonString());
        });
    }

    [TestMethod]
    [DataRow("1.0.107")]
    [DataRow("1.0.105")]
    [Timeout(120_000)]
    public async Task SelectedVersionBridgeDeclaredOriginalLifetimeOnlySelectedOr105(
        string original
    )
    {
        RecoveryLifetimeControl state = await RecoveryLifetimeControl.CreateAsync(
            context.CancellationToken,
            version: original
        );
        await state.RunBodyAsync(async () =>
        {
            state.AssertOriginalLifetime();
            var before = state.FinancialSnapshot.ToJsonString();

            var recovered = await state.RecoverAsync(context.CancellationToken);

            Assert.AreEqual(247m, recovered.Usage.RequireComplete());
            Assert.AreEqual(
                original,
                state.Original.Metadata!["runtimeVersion"]!.GetValue<string>()
            );
            Assert.AreEqual(
                "1.0.107",
                state.RecoveryHosts.Single().Sessions.Single().Metadata![
                    "runtimeVersion"
                ]!.GetValue<string>()
            );
            Assert.AreEqual(
                NativeSdkSession.SdkVersion,
                state.Original.Metadata!["sdkVersion"]!.GetValue<string>()
            );
            Assert.AreEqual(before, state.FinancialSnapshot.ToJsonString());
        });
    }

    [TestMethod]
    [DataRow(300)]
    [DataRow(301)]
    public async Task ZeroOriginUsesVerifiedCeilingNonpositiveBackingRejectsBeforeAnyActivation(
        int spent
    )
    {
        // Guard-only inputs, not an assertion of complete historical eligibility:
        // no Git, native process or coordinator is needed before this first guard.
        using TypedHostControl state = new();
        CreditReservation original = state.Reserve("capacity-boundary");
        CreditReservation input = original with { Allowance = 300m, Actual = spent };
        string before = state.Accounting.ToJsonString();
        var failure = await Assert.ThrowsExactlyAsync<OperationException>(() =>
            state.Factory.ReadSettledUsageAsync(
                input,
                state.Cwd("capacity-boundary"),
                state.Cwd("capacity-evidence"),
                context.CancellationToken
            )
        );
        Assert.Contains("Exact recovery has no remaining backed capacity", failure.Message);
        Assert.IsEmpty(state.Sessions);
        Assert.AreEqual(original, state.Read(original));
        Assert.AreEqual(before, state.Accounting.ToJsonString());
    }

    [TestMethod]
    [DataRow("final-flag", "outside its backed shared spending window")]
    [DataRow("association", "Settled recovery association changed")]
    [DataRow("revised-final", "revised a final total")]
    [Timeout(120_000)]
    public async Task ClosedWindowMonotonicFinalChargeLedgerTupleGuardPreservesBookedSpend(
        string fault,
        string reason
    )
    {
        RecoveryLifetimeControl state = await RecoveryLifetimeControl.CreateAsync(
            context.CancellationToken,
            shared: true
        );
        await state.RunBodyAsync(async () =>
        {
            state.AssertOriginalLifetime();
            var settled = await state.RecoverAsync(context.CancellationToken);
            var peer = state.Peer;
            if (fault == "revised-final")
            {
                state.Ledger.RecordRecoveredSdkUsage(settled);
            }
            var before = state.Reservation;
            var failure = Assert.ThrowsExactly<OperationException>(() =>
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
                            settled with
                            {
                                Usage = settled.Usage with { TotalNanoAiu = 248_000_000_000d },
                            }
                        );
                        break;
                }
            });
            Assert.Contains(reason, failure.Message);
            Assert.AreEqual(before.Actual, state.Reservation.Actual);
            Assert.AreEqual(before.Allowance, state.Reservation.Allowance);
            Assert.AreEqual(before.Overshoot, state.Reservation.Overshoot);
            Assert.AreEqual(before.SessionId, state.Reservation.SessionId);
            Assert.AreEqual("uncertain", state.Reservation.State);
            Assert.IsFalse(state.Reservation.GrantOpen);
            Assert.AreEqual(peer, state.Peer);
            // Rejected raw observations may be retained for audit. They are NOT
            // additional booked spend, a report-derived grant or a final disposition.
        });
    }
}
