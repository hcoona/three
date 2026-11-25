using System.Text.Json.Nodes;
using LiteAgensh.Tests.Support;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace LiteAgensh.Tests;

[TestClass]
public sealed class ClosedWindowRecoverySpecs(TestContext context)
{
    private static readonly string[] SavedObservationFaults =
    [
        "missing-start",
        "nonzero-origin",
        "start-session",
        "lineage",
        "lagging-cap",
        "changed-sdk",
    ];

    [TestMethod]
    [DataRow(247)]
    [DataRow(347)]
    [Timeout(120_000, CooperativeCancellation = true)]
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
    [DataRow("requests-regress", "Fresh recovery usage regressed")]
    [DataRow("session-stop", "actual recovery session could not settle")]
    [DataRow("host-stop", "actual owned recovery host could not settle")]
    [DataRow("background", "Fresh recovery background compaction failed")]
    [Timeout(120_000, CooperativeCancellation = true)]
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
            var failure = await Assert.ThrowsExactlyAsync<OperationException>(() =>
                state.RecoverAsync(context.CancellationToken)
            );
            Assert.Contains(reason, failure.ToString());
            Assert.AreEqual(before, state.FinancialSnapshot.ToJsonString());
            Assert.AreSame(state.OriginalFailure, state.Original.FirstFailure);
        });
    }

    [TestMethod]
    [Timeout(120_000, CooperativeCancellation = true)]
    public async Task ZeroOriginSavedObservationFaultsRestoreExactProducerBytesWithoutActivation()
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
            string startKey = NoDispatchCancellation.Marker(state.Reservation.Id);
            string sessionKey = $"native:session:{state.Reservation.SessionId}";
            string startBytes = state.Database.ReadMetadata(startKey)!;
            string sessionBytes = state.Database.ReadMetadata(sessionKey)!;
            Assert.IsNotNull(startBytes);
            Assert.IsNotNull(sessionBytes);
            foreach (string fault in SavedObservationFaults)
            {
                Assert.AreEqual(startBytes, state.Database.ReadMetadata(startKey), fault);
                Assert.AreEqual(sessionBytes, state.Database.ReadMetadata(sessionKey), fault);
                using SavedObservationScope guard = new(
                    context,
                    state.Database,
                    fault,
                    startKey,
                    startBytes,
                    sessionKey,
                    sessionBytes
                );
                state.DamageSavedObservation(fault);

                var failure = await Assert.ThrowsExactlyAsync<OperationException>(() =>
                    state.RecoverAsync(context.CancellationToken)
                );

                Assert.Contains(
                    fault == "missing-start"
                        ? "Required original native limit evidence is missing"
                        : "compatible original zero-origin and verified cap evidence",
                    failure.ToString(),
                    message: fault
                );
                Assert.HasCount(
                    1,
                    state.RecoveryHosts,
                    $"{fault} must reject before another host or exact activation."
                );
                Assert.AreEqual(before, state.FinancialSnapshot.ToJsonString(), fault);
                Assert.AreSame(state.OriginalFailure, state.Original.FirstFailure, fault);
                guard.Complete();
            }
        });
    }

    [TestMethod]
    [DataRow("1.0.106", false)]
    [DataRow("1.0.106", true)]
    [Timeout(120_000, CooperativeCancellation = true)]
    public async Task RuntimeProvenanceMismatchRejectsWithoutRewritingOriginal(
        string original,
        bool runtimeRecord
    )
    {
        RecoveryLifetimeControl state = await RecoveryLifetimeControl.CreateAsync(
            context.CancellationToken,
            version: original
        );
        await state.RunBodyAsync(async () =>
        {
            state.AssertOriginalLifetime();
            Assert.AreEqual(
                247m,
                (await state.RecoverAsync(context.CancellationToken)).Usage.RequireComplete()
            );
            string sessionKey = $"native:session:{state.Reservation.SessionId}";
            string runtimeKey = $"native:runtime:{state.OriginalHost.Id}";
            string[] provenanceKeys =
            [
                NoDispatchCancellation.Marker(state.Reservation.Id),
                sessionKey,
                runtimeKey,
                $"{runtimeKey}:session:{state.Reservation.SessionId}",
            ];
            var originalEvidence = provenanceKeys.ToDictionary(
                key => key, key => state.Database.ReadMetadata(key), StringComparer.Ordinal
            );
            foreach (var (key, bytes) in originalEvidence)
            {
                Assert.IsNotNull(bytes, key);
            }
            string damagedKey = runtimeRecord ? runtimeKey : sessionKey;
            if (runtimeRecord)
            {
                JsonObject runtime = JsonNode.Parse(originalEvidence[runtimeKey]!)!.AsObject();
                runtime["runtimeVersion"] = "1.0.107";
                state.Database.WriteMetadata(runtimeKey, runtime.ToJsonString());
            }
            else
            {
                state.DamageSavedObservation("runtime-version");
            }
            string? damaged = state.Database.ReadMetadata(damagedKey);
            Assert.IsNotNull(damaged);
            Assert.AreNotEqual(
                original,
                JsonNode.Parse(damaged)!["runtimeVersion"]!.GetValue<string>()
            );
            var before = state.FinancialSnapshot.ToJsonString();
            var failure = await Assert.ThrowsExactlyAsync<OperationException>(() =>
                state.RecoverAsync(context.CancellationToken)
            );
            Assert.Contains(
                "compatible original zero-origin and verified cap evidence",
                failure.ToString()
            );
            foreach (var (key, bytes) in originalEvidence)
            {
                Assert.AreEqual(
                    key == damagedKey ? damaged : bytes,
                    state.Database.ReadMetadata(key),
                    key
                );
            }
            Assert.HasCount(1, state.RecoveryHosts);
            Assert.AreEqual(before, state.FinancialSnapshot.ToJsonString());
        });
    }

    [TestMethod]
    [DataRow("missing-live-version", "compatible original zero-origin and verified cap evidence")]
    [DataRow("live-cap", "limit")]
    [Timeout(120_000, CooperativeCancellation = true)]
    public async Task InstalledRuntimeRecoveryRequiresLiveIdentityAndCap(
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
    [Timeout(120_000, CooperativeCancellation = true)]
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
    [DataRow("1.0.105")]
    [Timeout(120_000, CooperativeCancellation = true)]
    public async Task InstalledRuntimeRecoveryPreservesOriginalVersionWithoutPairAllowlist(
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
            state.RecoveryVersion = "1.0.112";
            var before = state.FinancialSnapshot.ToJsonString();
            var reservation = state.Reservation;
            string sessionKey = $"native:session:{reservation.SessionId}";
            string runtimeKey = $"native:runtime:{state.OriginalHost.Id}";
            long leaseId = state.OriginalHost.LeaseId;
            string[] provenanceKeys =
            [
                NoDispatchCancellation.Marker(reservation.Id),
                sessionKey,
                runtimeKey,
                $"{runtimeKey}:session:{reservation.SessionId}",
            ];
            var originalEvidence = provenanceKeys.ToDictionary(
                key => key, key => state.Database.ReadMetadata(key), StringComparer.Ordinal
            );
            foreach (var (key, bytes) in originalEvidence)
            {
                Assert.IsNotNull(bytes, key);
            }
            var originalLease = ReadOriginalLease();

            var recovered = await state.RecoverAsync(context.CancellationToken);

            Assert.AreEqual(247m, recovered.Usage.RequireComplete());
            foreach (var (key, bytes) in originalEvidence)
            {
                Assert.AreEqual(bytes, state.Database.ReadMetadata(key), key);
            }
            Assert.AreEqual(originalLease, ReadOriginalLease());
            JsonNode retained = JsonNode.Parse(originalEvidence[sessionKey]!)!;
            Assert.AreEqual(original, retained["runtimeVersion"]!.GetValue<string>());
            Assert.AreEqual(
                "1.0.112",
                state.RecoveryHosts.Single().Sessions.Single().Metadata![
                    "runtimeVersion"
                ]!.GetValue<string>()
            );
            Assert.AreEqual(
                NativeSdkSession.SdkVersion,
                retained["sdkVersion"]!.GetValue<string>()
            );
            Assert.AreEqual(before, state.FinancialSnapshot.ToJsonString());

            (long Pid, long BirthTicks, string Kind, string State, string CreatedAt)
                ReadOriginalLease()
            {
                using var connection = state.Database.Open();
                using var command = Sql.Command(
                    connection,
                    """
                    SELECT pid,start_ticks,kind,state,created_at
                    FROM process_leases WHERE id=$lease;
                    """,
                    null,
                    ("$lease", leaseId)
                );
                using var row = Sql.Reader(command);
                Assert.IsTrue(row.Read(), "The original runtime lease is missing.");
                return (
                    row.GetInt64(0),
                    row.GetInt64(1),
                    row.GetString(2),
                    row.GetString(3),
                    row.GetString(4)
                );
            }
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

    private sealed class SavedObservationScope(
        TestContext context,
        RunDatabase database,
        string fault,
        string startKey,
        string startBytes,
        string sessionKey,
        string sessionBytes
    ) : IDisposable
    {
        private bool completed;

        internal void Complete() => completed = true;

        public void Dispose()
        {
            try
            {
                database.WriteMetadata(startKey, startBytes);
                database.WriteMetadata(sessionKey, sessionBytes);
                Assert.AreEqual(startBytes, database.ReadMetadata(startKey), fault);
                Assert.AreEqual(sessionBytes, database.ReadMetadata(sessionKey), fault);
            }
            catch (Exception restoration) when (!completed)
            {
                context.WriteLine(
                    "Secondary saved observation restoration failure ({0}): {1}",
                    fault,
                    restoration
                );
            }
        }
    }
}
