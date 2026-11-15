using System.Text.Json.Nodes;
using LiteAgensh.Tests.Support;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace LiteAgensh.Tests;

[TestClass]
public sealed class ReconnectSettlementSpecs(TestContext context)
{
    [TestMethod]
    [Timeout(120_000)]
    [DataRow("controlled")]
    [DataRow("missing-capability")]
    [DataRow("unsettled-stop")]
    public async Task ControlledReceiptAdmissionBoundary(string scenario)
    {
        Phase6PublicRun state = await Phase6PublicRun.CreateAsync(1, context.CancellationToken);
        await state.RunBodyAsync(async () =>
        {
            state.MissingLimitCapability = scenario == "missing-capability";
            state.FailPaidStop = scenario == "unsettled-stop";
            JsonObject preflight = await state.Reopen().PreflightAsync(context.CancellationToken);
            if (scenario == "missing-capability")
            {
                Assert.IsFalse(preflight["ok"]!.GetValue<bool>());
                Assert.AreEqual("native_probe_failed", preflight["code"]!.GetValue<string>());
                Assert.IsNull(state.Database.ReadMetadata("native:controlled_preflight_receipt"));
                Assert.IsEmpty(state.Ledger.Reservations(state.Database.RunId));
                Assert.IsNull(state.Database.ReadMetadata("native:sdk_preflight_receipt"));
                return;
            }
            GitScenarioState.Data(preflight);
            JsonObject receipt = JsonNode
                .Parse(state.Database.ReadMetadata("native:controlled_preflight_receipt")!)!
                .AsObject();
            Assert.AreEqual(
                "controlled-native-dependency",
                receipt["nativeEvidenceKind"]!.GetValue<string>()
            );
            Assert.AreEqual(
                preflight["data"]!["sessionId"]!.GetValue<string>(),
                receipt["sessionId"]!.GetValue<string>()
            );
            Assert.AreEqual(30d, receipt["sessions"]![0]!["appliedLimit"]!.GetValue<double>());
            JsonObject calibrated = await state.Reopen().CalibrateAsync(context.CancellationToken);
            CreditReservation calibration = state
                .Ledger.Reservations(state.Database.RunId)
                .Single();
            if (scenario == "unsettled-stop")
            {
                Assert.IsFalse(calibrated["ok"]!.GetValue<bool>());
                Assert.AreEqual(
                    "credit_or_ownership_unknown",
                    calibrated["code"]!.GetValue<string>()
                );
                Assert.AreEqual("uncertain", calibration.State);
                Assert.AreEqual(7.25m, calibration.Actual);
                Assert.IsNull(state.Database.ReadMetadata("native:controlled_billing_receipt"));
                Assert.IsTrue(
                    state.Ledger.Status(state.Database.RunId, 207.25m)[
                        "uncertain"
                    ]!.GetValue<bool>()
                );
                return;
            }
            GitScenarioState.Data(calibrated);
            Assert.AreEqual(7.25m, calibration.Actual);
            string billingBytes = state.Database.ReadMetadata("native:controlled_billing_receipt")!;
            JsonObject billing = JsonNode.Parse(billingBytes)!.AsObject();
            Assert.AreEqual(calibration.Id, billing["reservationId"]!.GetValue<string>());
            Assert.AreEqual(calibration.SessionId, billing["sessionId"]!.GetValue<string>());
            Assert.AreEqual(
                "controlled-native-dependency",
                billing["nativeEvidenceKind"]!.GetValue<string>()
            );
            JsonObject before = Phase6State.Financial(state.Database, state.Ledger);
            CollaborationOperations collaboration = new(state.Database);
            Coordinator nativeAdmission = new(
                state.Database,
                collaboration,
                new(state.Database, collaboration)
            );
            JsonObject rejected = await nativeAdmission.RunAsync("run", context.CancellationToken);
            Assert.IsFalse(rejected["ok"]!.GetValue<bool>());
            Assert.AreEqual("unavailable", rejected["code"]!.GetValue<string>());
            Assert.Contains("preflight", rejected["message"]!.GetValue<string>());
            Assert.AreEqual(
                before.ToJsonString(),
                Phase6State.Financial(state.Database, state.Ledger).ToJsonString()
            );
            Assert.AreEqual(
                0L,
                Phase6State.Number(state.Database, "SELECT COUNT(*) FROM coordinator_segments;")
            );
            Assert.AreEqual(
                billingBytes,
                state.Database.ReadMetadata("native:controlled_billing_receipt")
            );
            Assert.IsNull(state.Database.ReadMetadata("native:sdk_billing_receipt"));
        });
    }

    [TestMethod]
    [Timeout(120_000)]
    [DataRow(false)]
    [DataRow(true)]
    public async Task ControlledReceiptAdmissionUnknownAssociationPreservesBacking(bool mismatch)
    {
        Phase6PublicRun state = await Phase6PublicRun.CreateAsync(1, context.CancellationToken);
        await state.RunBodyAsync(async () =>
        {
            CreditReservation reservation = state.Ledger.Reserve(
                state.Database.RunId,
                "worker1",
                30m,
                207.25m,
                Path.Combine(state.Database.StatePath, "owned-unknown")
            );
            state.Ledger.MarkStarted(reservation.Id);
            if (mismatch)
            {
                state.Ledger.AttachSession(reservation.Id, reservation.SessionId!);
                OperationException error = Assert.ThrowsExactly<OperationException>(() =>
                    state.Ledger.RecordSdkUsage(
                        reservation.Id,
                        new NativeUsage("foreign-controlled-session", 0, null, 0),
                        final: false
                    )
                );
                Assert.AreEqual("credit_unknown", error.Code);
            }
            JsonObject rejected = await state.Coordinator.CalibrateAsync(context.CancellationToken);
            Assert.IsFalse(rejected["ok"]!.GetValue<bool>());
            Assert.AreEqual("credit_unknown", rejected["code"]!.GetValue<string>());
            CreditReservation retained = state.Ledger.Reservations(state.Database.RunId).Single();
            Assert.AreEqual(reservation.Id, retained.Id);
            Assert.AreEqual("uncertain", retained.State);
            Assert.AreEqual(30m, retained.Allowance);
            Assert.IsNull(retained.Actual);
            Assert.IsNull(state.Database.ReadMetadata("native:controlled_billing_receipt"));
            using var campaign = Phase6State.OpenCampaign(state.Database);
            Assert.IsEmpty(Phase6State.Rows(campaign, "SELECT * FROM credit_reports;"));
        });
    }

    [TestMethod]
    [Timeout(120_000)]
    public async Task ClosedFailedRunUsageOnly()
    {
        Phase6PublicRun state = await Phase6PublicRun.CreateAsync(1, context.CancellationToken);
        await state.RunBodyAsync(async () =>
        {
            await state.FailOriginalAsync(context.CancellationToken);
            CreditReservation original = state
                .Ledger.Reservations(state.Database.RunId)
                .Single(row => row.Worker == "worker1");
            // Legitimate owned configuration edit makes the ACTUAL controlled
            // receipts stale, without creating any SDK receipt or invoking it.
            state.Database.SaveConfiguration(
                state.Database.Configuration with
                {
                    NativeArguments = ["--reasoning-effort=low"],
                }
            );
            JsonObject history = Phase6State.History(state.Database);
            JsonObject before = Phase6State.Financial(state.Database, state.Ledger);
            state.Reader = (row, token) =>
            {
                token.ThrowIfCancellationRequested();
                return Task.FromResult(
                    new RecoveredNativeUsage(
                        row,
                        new(row.SessionId!, 220_000_000_000d, "complete", 2)
                    )
                );
            };
            JsonObject result = await state
                .Reopen()
                .RunAsync("reconnect", context.CancellationToken);
            GitScenarioState.Data(result);
            Assert.AreEqual("usage-settlement", result["data"]!["operation"]!.GetValue<string>());
            Assert.IsFalse(result["data"]!["runUncertain"]!.GetValue<bool>());
            CreditReservation settled = state
                .Ledger.Reservations(state.Database.RunId)
                .Single(row => row.Id == original.Id);
            Assert.AreEqual("reconciled", settled.State);
            Assert.AreEqual(220m, settled.Actual);
            JsonObject accounting = state.Ledger.Status(state.Database.RunId, 207.25m);
            Assert.AreEqual(227.25m, accounting["actualReported"]!.GetValue<decimal>());
            Assert.AreEqual(20m, accounting["overshoot"]!.GetValue<decimal>());
            Assert.AreEqual(140m, settled.Actual!.Value - original.Actual!.Value);
            Assert.AreEqual(
                history.ToJsonString(),
                Phase6State.History(state.Database).ToJsonString()
            );
            JsonObject after = Phase6State.Financial(state.Database, state.Ledger);
            AssertOldReportsPreserved(before, after);
            state.Reader = null; // Any recovery read now throws; not a call counter.
            JsonObject repeated = await state
                .Reopen()
                .RunAsync("reconnect", context.CancellationToken);
            Assert.AreEqual(result.ToJsonString(), repeated.ToJsonString());
            Assert.AreEqual(
                after.ToJsonString(),
                Phase6State.Financial(state.Database, state.Ledger).ToJsonString()
            );
            Assert.AreEqual(
                history.ToJsonString(),
                Phase6State.History(state.Database).ToJsonString()
            );
        });
    }

    [TestMethod]
    [Timeout(120_000)]
    [DataRow("association")]
    [DataRow("partial")]
    [DataRow("ownership")]
    [DataRow("session")]
    [DataRow("host")]
    public async Task MixedRecoveryPreservesPaidGates(string rejection)
    {
        Phase6PublicRun state = await Phase6PublicRun.CreateAsync(2, context.CancellationToken);
        await state.RunBodyAsync(async () =>
        {
            await state.FailOriginalAsync(context.CancellationToken);
            CreditReservation[] originals =
            [
                .. state
                    .Ledger.Reservations(state.Database.RunId)
                    .Where(row => row.Worker != "calibration"),
            ];
            Assert.HasCount(2, originals);
            JsonObject history = Phase6State.History(state.Database);
            state.Reader = (row, token) =>
            {
                token.ThrowIfCancellationRequested();
                if (row.Id == originals[0].Id)
                {
                    return Task.FromResult(
                        new RecoveredNativeUsage(
                            row,
                            new(row.SessionId!, 120_000_000_000d, "complete", 2)
                        )
                    );
                }
                if (rejection is "ownership" or "session" or "host")
                {
                    throw new OperationException(
                        "credit_unknown",
                        "Controlled validated-reader rejected " + rejection + " uncertainty."
                    );
                }
                return Task.FromResult(
                    new RecoveredNativeUsage(
                        rejection == "association"
                            ? row with
                            {
                                UsagePath = row.UsagePath + ".foreign",
                            }
                            : row,
                        new(
                            row.SessionId!,
                            120_000_000_000d,
                            rejection == "partial" ? "partial" : "complete",
                            2
                        )
                    )
                );
            };
            JsonObject result = await state
                .Reopen()
                .RunAsync("reconnect", context.CancellationToken);
            Assert.IsFalse(result["ok"]!.GetValue<bool>());
            Assert.AreEqual("credit_unknown", result["code"]!.GetValue<string>());
            Assert.AreEqual("usage-settlement", result["data"]!["operation"]!.GetValue<string>());
            Assert.IsTrue(result["data"]!["runUncertain"]!.GetValue<bool>());
            CreditReservation[] actual =
            [
                .. state
                    .Ledger.Reservations(state.Database.RunId)
                    .Where(row => row.Worker != "calibration"),
            ];
            Assert.AreEqual("reconciled", actual[0].State);
            Assert.AreEqual(120m, actual[0].Actual);
            Assert.AreEqual(40m, actual[0].Actual!.Value - originals[0].Actual!.Value);
            Assert.AreEqual("uncertain", actual[1].State);
            Assert.AreEqual(80m, actual[1].Actual);
            Assert.AreEqual(originals[1].Allowance, actual[1].Allowance);
            Assert.AreEqual(
                history.ToJsonString(),
                Phase6State.History(state.Database).ToJsonString()
            );
            JsonObject resume = await state.Reopen().RunAsync("resume", context.CancellationToken);
            Assert.IsFalse(resume["ok"]!.GetValue<bool>());
            Assert.AreEqual("conflict", resume["code"]!.GetValue<string>());
            Assert.AreEqual(
                history.ToJsonString(),
                Phase6State.History(state.Database).ToJsonString()
            );
        });
    }

    private static void AssertOldReportsPreserved(JsonObject before, JsonObject after)
    {
        JsonArray old = before["reports"]!.AsArray();
        JsonArray current = after["reports"]!.AsArray();
        Assert.IsNotEmpty(old);
        foreach (JsonNode? report in old)
        {
            long id = report!["id"]!.GetValue<long>();
            Assert.AreEqual(
                report.ToJsonString(),
                current.Single(row => row!["id"]!.GetValue<long>() == id)!.ToJsonString()
            );
        }
    }

    [TestMethod]
    [Timeout(120_000)]
    public async Task MixedRecoveryUnfinishedCrashAndFreshResumeDoNotFallback()
    {
        Phase6PublicRun fresh = await Phase6PublicRun.CreateAsync(1, context.CancellationToken);
        await fresh.RunBodyAsync(async () =>
        {
            JsonObject history = Phase6State.History(fresh.Database);
            JsonObject noSegment = await fresh.Coordinator.RunAsync(
                "reconnect",
                context.CancellationToken
            );
            Assert.AreEqual("conflict", noSegment["code"]!.GetValue<string>());
            Assert.AreEqual(
                history.ToJsonString(),
                Phase6State.History(fresh.Database).ToJsonString()
            );
        });
        Phase6OwnedCreator crash = await Phase6OwnedCreator.OpenAsync(context.CancellationToken);
        await crash.RunBodyAsync(async () =>
        {
            Assert.AreEqual(
                1L,
                Phase6State.Number(
                    crash.Database,
                    "SELECT COUNT(*) FROM coordinator_segments WHERE ended_at IS NULL;"
                )
            );
            await crash.CrashAsync(context.CancellationToken);
            CreditReservation before = crash.Target;
            JsonObject result = await crash
                .AccountingOnly()
                .RunAsync("reconnect", context.CancellationToken);
            Assert.IsFalse(result["ok"]!.GetValue<bool>());
            Assert.AreEqual("unavailable", result["code"]!.GetValue<string>());
            Assert.Contains("reader gate", result["message"]!.GetValue<string>());
            Assert.AreEqual("uncertain", crash.Target.State);
            Assert.IsNull(crash.Target.Actual);
            Assert.AreEqual(before.Allowance, crash.Target.Allowance);
            Assert.AreEqual(
                1L,
                Phase6State.Number(crash.Database, "SELECT COUNT(*) FROM coordinator_segments;")
            );
            Assert.AreEqual(
                1L,
                Phase6State.Number(
                    crash.Database,
                    "SELECT COUNT(*) FROM coordinator_segments WHERE ended_at IS NULL;"
                )
            );
            Assert.AreEqual(
                0L,
                Phase6State.Number(crash.Database, "SELECT COUNT(*) FROM coordinator_turns;")
            );
        });
    }

    [TestMethod]
    [Timeout(120_000)]
    public async Task MixedRecoveryFreshResumeRetainsStalePaidGate()
    {
        Phase6PublicRun state = await Phase6PublicRun.CreateAsync(1, context.CancellationToken);
        await state.RunBodyAsync(async () =>
        {
            await state.PauseOriginalAsync(context.CancellationToken);
            state.Database.SaveConfiguration(
                state.Database.Configuration with
                {
                    NativeArguments = ["--reasoning-effort=low"],
                }
            );
            JsonObject history = Phase6State.History(state.Database);
            JsonObject money = Phase6State.Financial(state.Database, state.Ledger);
            JsonObject result = await state.Reopen().RunAsync("resume", context.CancellationToken);
            Assert.AreEqual("unavailable", result["code"]!.GetValue<string>());
            Assert.Contains("preflight", result["message"]!.GetValue<string>());
            Assert.AreEqual(
                history.ToJsonString(),
                Phase6State.History(state.Database).ToJsonString()
            );
            Assert.AreEqual(
                money.ToJsonString(),
                Phase6State.Financial(state.Database, state.Ledger).ToJsonString()
            );
        });
    }
}
