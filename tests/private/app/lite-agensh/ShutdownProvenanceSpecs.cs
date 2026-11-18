using LiteAgensh.Tests.Support;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace LiteAgensh.Tests;

[TestClass]
public sealed class ShutdownProvenanceSpecs(TestContext context)
{
    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    [Timeout(120_000)]
    public async Task OriginalLifetimeFullSnapshotShortPagesOriginalFailureAndCompleteFrontier(
        bool undelivered
    )
    {
        RecoveryLifetimeControl state = await RecoveryLifetimeControl.CreateAsync(
            context.CancellationToken,
            undelivered: undelivered
        );
        await state.RunBodyAsync(async () =>
        {
            state.AssertOriginalLifetime();
            var before = state.FinancialSnapshot.ToJsonString();

            var recovered = await state.RecoverAsync(context.CancellationToken);

            var reader = state.RecoveryHosts.Single().Sessions.Single();
            Assert.AreEqual(5, reader.ReadCursors.Count);
            Assert.HasCount(5, reader.PageTokens);
            Assert.AreEqual(reader.PageTokens[0], reader.PageTokens[4]);
            Assert.AreEqual("4", reader.ReadCursors[4]);
            Assert.AreSame(state.OriginalFailure, state.Original.FirstFailure);
            Assert.AreEqual(
                247m,
                recovered.Usage.RequireComplete(),
                "Original shutdown is a lower bound, not the recovered amount."
            );
            Assert.AreEqual(before, state.FinancialSnapshot.ToJsonString());
        });
    }

    [TestMethod]
    [Timeout(120_000)]
    public async Task FreshReadMustStayIdleAndSettleNewLocalBoundaryOnlyPostHostResultCanBeBooked()
    {
        RecoveryLifetimeControl state = await RecoveryLifetimeControl.CreateAsync(
            context.CancellationToken
        );
        await state.RunBodyAsync(async () =>
        {
            state.AssertOriginalLifetime();
            var original = state.Reservation;
            var peer = state.Peer;

            var recovered = await state.RecoverAsync(context.CancellationToken);
            Assert.AreEqual(original, state.Reservation);
            state.Ledger.RecordRecoveredSdkUsage(recovered);

            Assert.AreEqual(247m, state.Reservation.Actual);
            Assert.AreEqual("reconciled", state.Reservation.State);
            Assert.AreEqual(peer, state.Peer);
            Assert.AreEqual(4L, recovered.Usage.TotalUserRequests);
            Assert.AreSame(state.OriginalFailure, state.Original.FirstFailure);
        });
    }

    [TestMethod]
    [Timeout(120_000)]
    public async Task OriginalLifetimeMissingShutdownRejectsBeforeExactActivation()
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
            var before = state.FinancialSnapshot.ToJsonString();
            state.RecoveryCase = "missing-shutdown";
            var failure = await Assert.ThrowsExactlyAsync<OperationException>(() =>
                state.RecoverAsync(context.CancellationToken)
            );
            Assert.Contains(
                "Matching original routine native termination is missing",
                failure.ToString()
            );
            Assert.AreEqual(before, state.FinancialSnapshot.ToJsonString());
            Assert.AreSame(state.OriginalFailure, state.Original.FirstFailure);
            Assert.IsNull(
                state.RecoveryHosts.Last().Sessions.Single().ExactSession,
                "Failed original history must reject before exact activation."
            );
        });
    }

    [TestMethod]
    [DataRow("after-read", "Native work invalidated the staged usage reading")]
    [DataRow("processing", "still has processing or active work")]
    [Timeout(120_000)]
    public async Task
    FreshReadMustStayIdleAndSettleEligibleThenWorkOrCapabilityFaultPreservesOriginal(
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
            var before = state.FinancialSnapshot.ToJsonString();
            state.RecoveryCase = fault;
            var failure = await Assert.ThrowsExactlyAsync<OperationException>(() =>
                state.RecoverAsync(context.CancellationToken)
            );
            Assert.Contains(reason, failure.ToString());
            Assert.AreEqual(before, state.FinancialSnapshot.ToJsonString());
            Assert.AreSame(state.OriginalFailure, state.Original.FirstFailure);
        });
    }

    [TestMethod]
    [Timeout(120_000)]
    public async Task
    FreshReadMustStayIdleAndSettleShutdownOnlyLowerBoundRejectsOtherwiseNonregressingUsage()
    {
        RecoveryLifetimeControl state = await RecoveryLifetimeControl.CreateAsync(
            context.CancellationToken
        );
        await state.RunBodyAsync(async () =>
        {
            state.AssertOriginalLifetime();
            Assert.AreEqual(200m, state.Reservation.Actual);
            Assert.AreEqual(230_000_000_000d, state.Original.Current.TotalNanoAiu);
            Assert.AreEqual(
                247m,
                (await state.RecoverAsync(context.CancellationToken)).Usage.RequireComplete()
            );
            var before = state.FinancialSnapshot.ToJsonString();
            state.FreshNano = 220_000_000_000d; // >= booked/prior 200, but < original shutdown 230.
            var failure = await Assert.ThrowsExactlyAsync<OperationException>(() =>
                state.RecoverAsync(context.CancellationToken)
            );
            Assert.Contains("does not cover original native termination", failure.ToString());
            Assert.AreEqual(before, state.FinancialSnapshot.ToJsonString());
            Assert.AreSame(state.OriginalFailure, state.Original.FirstFailure);
        });
    }
}
