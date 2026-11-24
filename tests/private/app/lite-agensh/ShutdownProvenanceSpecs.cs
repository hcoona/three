using System.Text.Json.Nodes;
using LiteAgensh.Tests.Support;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace LiteAgensh.Tests;

[TestClass]
public sealed class ShutdownProvenanceSpecs(TestContext context)
{
    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    [Timeout(120_000, CooperativeCancellation = true)]
    public async Task OriginalLifetimeFullSnapshotShortPagesOriginalFailureAndCompleteFrontier(
        bool undelivered
    )
    {
        const decimal authorizedCeiling = 300.3m;
        CancellationToken token = context.CancellationToken;
        // After the controlled 7.25-credit calibration, each of two peers can reserve 300.3.
        RecoveryLifetimeControl state = await RecoveryLifetimeControl.CreateAsync(
            token, undelivered: undelivered, runAndCampaignAllowance: 607.85m
        );
        await state.RunBodyAsync(async () =>
        {
            state.AssertOriginalLifetime(authorizedCeiling);
            var before = state.FinancialSnapshot.ToJsonString();
            string startKey = NoDispatchCancellation.Marker(state.Reservation.Id);
            string startBytes = state.Database.ReadMetadata(startKey)!;
            string sessionKey = $"native:session:{state.Reservation.SessionId}";
            string sessionBytes = state.Database.ReadMetadata(sessionKey)!;

            var recovered = await state.RecoverAsync(token);

            var reader = state.RecoveryHosts.Single().Sessions.Single();
            Assert.AreEqual(authorizedCeiling, reader.ExactCap);
            Assert.AreEqual(
                JsonNode.Parse(sessionBytes)!["appliedLimit"]!.GetValue<double>(),
                reader.Metadata!["appliedLimit"]!.GetValue<double>()
            );
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
            Assert.AreEqual(startBytes, state.Database.ReadMetadata(startKey));
            Assert.AreEqual(sessionBytes, state.Database.ReadMetadata(sessionKey));

            token.ThrowIfCancellationRequested();
            JsonNode start = JsonNode.Parse(startBytes)!;
            // JSON 300.3 reads as the next binary64 above the backed cap.
            start["initial"]!["appliedLimit"] = authorizedCeiling;
            try
            {
                state.Database.WriteMetadata(startKey, start.ToJsonString());
                OperationException failure = await Assert.ThrowsExactlyAsync<OperationException>(
                    () => state.RecoverAsync(token)
                );
                Assert.AreEqual("credit_unknown", failure.Code);
                Assert.Contains(
                    "compatible original zero-origin and verified cap evidence",
                    failure.Message
                );
                Assert.HasCount(
                    1,
                    state.RecoveryHosts,
                    "The unbacked initial cap must reject before activation."
                );
                Assert.AreEqual(before, state.FinancialSnapshot.ToJsonString());
            }
            finally
            {
                state.Database.WriteMetadata(startKey, startBytes);
            }
        });
    }

    [TestMethod]
    [Timeout(120_000, CooperativeCancellation = true)]
    public async Task FreshReadMustStayIdleAndSettleNewLocalBoundaryOnlyPostHostResultCanBeBooked()
    {
        CancellationToken token = context.CancellationToken;
        RecoveryLifetimeControl state = await RecoveryLifetimeControl.CreateAsync(token);
        await state.RunBodyAsync(async () =>
        {
            state.AssertOriginalLifetime();
            var original = state.Reservation;
            var peer = state.Peer;

            var recovered = await state.RecoverWithHeldNormalStopAsync(token);
            Assert.AreEqual(original, state.Reservation);
            token.ThrowIfCancellationRequested();
            state.Ledger.RecordRecoveredSdkUsage(recovered);

            Assert.AreEqual(247m, state.Reservation.Actual);
            Assert.AreEqual("reconciled", state.Reservation.State);
            Assert.AreEqual(peer, state.Peer);
            Assert.AreEqual(4L, recovered.Usage.TotalUserRequests);
            Assert.AreSame(state.OriginalFailure, state.Original.FirstFailure);
        });
    }

    [TestMethod]
    [Timeout(120_000, CooperativeCancellation = true)]
    public async Task OriginalLifetimeMissingShutdownRejectsBeforeExactActivation()
    {
        CancellationToken token = context.CancellationToken;
        RecoveryLifetimeControl state = await RecoveryLifetimeControl.CreateAsync(token);
        await state.RunBodyAsync(async () =>
        {
            state.AssertOriginalLifetime();
            Assert.AreEqual(
                247m,
                (await state.RecoverAsync(token)).Usage.RequireComplete()
            );
            var before = state.FinancialSnapshot.ToJsonString();
            state.RecoveryCase = "missing-shutdown";
            var failure = await Assert.ThrowsExactlyAsync<OperationException>(() =>
                state.RecoverAsync(token)
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
    [Timeout(120_000, CooperativeCancellation = true)]
    public async Task
    FreshReadMustStayIdleAndSettleEligibleThenWorkOrCapabilityFaultPreservesOriginal(
        string fault,
        string reason
    )
    {
        CancellationToken token = context.CancellationToken;
        RecoveryLifetimeControl state = await RecoveryLifetimeControl.CreateAsync(token);
        await state.RunBodyAsync(async () =>
        {
            state.AssertOriginalLifetime();
            Assert.AreEqual(
                247m,
                (await state.RecoverAsync(token)).Usage.RequireComplete()
            );
            var before = state.FinancialSnapshot.ToJsonString();
            state.RecoveryCase = fault;
            var failure = await Assert.ThrowsExactlyAsync<OperationException>(() =>
                state.RecoverAsync(token)
            );
            Assert.Contains(reason, failure.ToString());
            Assert.AreEqual(before, state.FinancialSnapshot.ToJsonString());
            Assert.AreSame(state.OriginalFailure, state.Original.FirstFailure);
        });
    }

    [TestMethod]
    [Timeout(120_000, CooperativeCancellation = true)]
    public async Task
    FreshReadMustStayIdleAndSettleShutdownOnlyLowerBoundRejectsOtherwiseNonregressingUsage()
    {
        CancellationToken token = context.CancellationToken;
        RecoveryLifetimeControl state = await RecoveryLifetimeControl.CreateAsync(token);
        await state.RunBodyAsync(async () =>
        {
            state.AssertOriginalLifetime();
            Assert.AreEqual(200m, state.Reservation.Actual);
            Assert.AreEqual(230_000_000_000d, state.Original.Current.TotalNanoAiu);
            Assert.AreEqual(
                247m,
                (await state.RecoverAsync(token)).Usage.RequireComplete()
            );
            var before = state.FinancialSnapshot.ToJsonString();
            state.FreshNano = 220_000_000_000d; // >= booked/prior 200, but < original shutdown 230.
            var failure = await Assert.ThrowsExactlyAsync<OperationException>(() =>
                state.RecoverAsync(token)
            );
            Assert.Contains("does not cover original native termination", failure.ToString());
            Assert.AreEqual(before, state.FinancialSnapshot.ToJsonString());
            Assert.AreSame(state.OriginalFailure, state.Original.FirstFailure);
        });
    }
}
