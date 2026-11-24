using System.Text.Json.Nodes;
using LiteAgensh.Tests.Support;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace LiteAgensh.Tests;

[TestClass]
public sealed class ReconnectSettlementSpecs(TestContext context)
{
    [TestMethod]
    [Timeout(120_000, CooperativeCancellation = true)]
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
                Assert.AreEqual(
                    "native_probe_failed", preflight["code"]!.GetValue<string>(),
                    preflight.ToJsonString()
                );
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
            if (scenario == "controlled")
            {
                JsonObject preflightHistory = Phase6State.History(state.Database);
                JsonObject preflightMoney = Phase6State.Financial(state.Database, state.Ledger);
                Assert.IsEmpty(preflightHistory["coordinator_segments"]!.AsArray());
                Assert.IsEmpty(preflightHistory["coordinator_turns"]!.AsArray());
                Assert.IsEmpty(preflightMoney["rows"]!.AsArray());
                Assert.IsEmpty(preflightMoney["usage"]!.AsArray());
                JsonObject uncalibrated = await state
                    .Reopen()
                    .RunAsync("run", context.CancellationToken);
                Assert.IsFalse(uncalibrated["ok"]!.GetValue<bool>());
                Assert.AreEqual("unavailable", uncalibrated["code"]!.GetValue<string>());
                Assert.Contains(
                    "complete paid calibration",
                    uncalibrated["message"]!.GetValue<string>()
                );
                Assert.IsTrue(JsonNode.DeepEquals(
                    preflightHistory, Phase6State.History(state.Database)
                ));
                Assert.IsTrue(JsonNode.DeepEquals(
                    preflightMoney, Phase6State.Financial(state.Database, state.Ledger)
                ));
            }
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
    [Timeout(120_000, CooperativeCancellation = true)]
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
    [Timeout(120_000, CooperativeCancellation = true)]
    public async Task ClosedFailedRunUsageOnly()
    {
        Phase6PublicRun state = await Phase6PublicRun.CreateAsync(1, context.CancellationToken);
        await state.RunBodyAsync(async () =>
        {
            CreditReservation unrelated = state.Ledger.Reserve(
                "unrelated-campaign-run", "worker1", 30m, 30m,
                Path.Combine(state.Database.StatePath, "unrelated.usage")
            );
            await state.FailOriginalAsync(context.CancellationToken);
            state.Ledger.MarkStarted(unrelated.Id);
            state.Ledger.MarkUncertain(unrelated.Id);
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
            JsonObject unrelatedBefore = UnrelatedFinancial(before);
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
            Assert.IsTrue(result["data"]!["accounting"]!["uncertain"]!.GetValue<bool>());
            CreditReservation settled = state
                .Ledger.Reservations(state.Database.RunId)
                .Single(row => row.Id == original.Id);
            Assert.AreEqual("reconciled", settled.State);
            Assert.AreEqual(220m, settled.Actual);
            JsonObject accounting = state.Ledger.Status(state.Database.RunId, 207.25m);
            Assert.AreEqual(227.25m, accounting["actualReported"]!.GetValue<decimal>());
            Assert.AreEqual(20m, accounting["overshoot"]!.GetValue<decimal>());
            Assert.IsTrue(accounting["uncertain"]!.GetValue<bool>());
            Assert.AreEqual(257.25m, accounting["campaignCommitted"]!.GetValue<decimal>());
            Assert.AreEqual(140m, settled.Actual!.Value - original.Actual!.Value);
            Assert.AreEqual(
                history.ToJsonString(),
                Phase6State.History(state.Database).ToJsonString()
            );
            JsonObject after = Phase6State.Financial(state.Database, state.Ledger);
            AssertOldReportsPreserved(before, after);
            Assert.IsTrue(JsonNode.DeepEquals(unrelatedBefore, UnrelatedFinancial(after)));
            OperationException paidGate = Assert.ThrowsExactly<OperationException>(() =>
                state.Ledger.Reserve(
                    "blocked-paid-run", "worker1", 30m, 30m,
                    Path.Combine(state.Database.StatePath, "blocked.usage")
                )
            );
            Assert.AreEqual("credit_unknown", paidGate.Code);
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

            JsonObject UnrelatedFinancial(JsonObject financial)
            {
                JsonObject retained = new();
                foreach ((string table, string key) in new[]
                {
                    ("rows", "id"), ("usage", "reservation_id"),
                    ("owners", "reservation_id"), ("reports", "reservation_id"),
                })
                {
                    retained[table] = new JsonArray(financial[table]!.AsArray()
                        .Where(row => row![key]!.GetValue<string>() == unrelated.Id)
                        .Select(row => row!.DeepClone()).ToArray());
                }
                return retained;
            }
        });
    }

    [TestMethod]
    [Timeout(120_000, CooperativeCancellation = true)]
    [DataRow("association")]
    [DataRow("partial")]
    [DataRow("reader-error")]
    [DataRow("association-first")]
    [DataRow("saved-session-first")]
    [DataRow("missing-start-first")]
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
            Assert.AreNotEqual(originals[0].Id, originals[1].Id);
            foreach (CreditReservation original in originals)
            {
                Assert.AreEqual("uncertain", original.State);
                Assert.AreEqual(80m, original.Actual);
            }
            bool savedSessionMismatch = rejection == "saved-session-first";
            bool missingStart = rejection == "missing-start-first";
            // First-target faults must precede the eligible sibling.
            bool rejectionFirst = rejection == "association-first" || savedSessionMismatch
                || missingStart;
            CreditReservation rejected = originals[rejectionFirst ? 0 : 1];
            CreditReservation settled = originals[rejectionFirst ? 1 : 0];
            if (savedSessionMismatch)
            {
                string sessionKey = $"worker:{rejected.Worker}:session";
                Assert.IsFalse(string.IsNullOrWhiteSpace(rejected.SessionId));
                Assert.AreEqual(rejected.SessionId, state.Database.ReadMetadata(sessionKey));
                Assert.AreEqual(
                    rejected.Id,
                    state.Database.ReadMetadata($"worker:{rejected.Worker}:reservation")
                );
                state.Database.WriteMetadata(sessionKey, rejected.SessionId + "-contradictory");
            }
            if (missingStart)
            {
                string key = NoDispatchCancellation.Marker(rejected.Id);
                using var connection = state.Database.Open();
                Assert.AreEqual(
                    1,
                    Sql.Execute(connection, "DELETE FROM meta WHERE key=$key;", null, ("$key", key))
                );
            }
            JsonObject history = Phase6State.History(state.Database);
            JsonObject before = Phase6State.Financial(state.Database, state.Ledger);
            const string readerCause =
                "Controlled generic recovery reader failed before a settled result.";
            if (rejection == "reader-error")
            {
                bool laterReaderEntered = false;
                state.Reader = (row, token) =>
                {
                    token.ThrowIfCancellationRequested();
                    if (row.Id == originals[0].Id)
                    {
                        throw new OperationException("credit_unknown", readerCause);
                    }
                    laterReaderEntered = true;
                    return Task.FromResult(
                        new RecoveredNativeUsage(
                            row,
                            new(row.SessionId!, 120_000_000_000d, "complete", 2)
                        )
                    );
                };
                JsonObject earlyFailure = await state
                    .Reopen()
                    .RunAsync("reconnect", context.CancellationToken);
                Assert.IsFalse(earlyFailure["ok"]!.GetValue<bool>());
                Assert.AreEqual("credit_unknown", earlyFailure["code"]!.GetValue<string>());
                Assert.Contains(readerCause, earlyFailure["message"]!.GetValue<string>());
                Assert.AreEqual(
                    "usage-settlement",
                    earlyFailure["data"]!["operation"]!.GetValue<string>()
                );
                Assert.IsTrue(earlyFailure["data"]!["runUncertain"]!.GetValue<bool>());
                Assert.IsFalse(laterReaderEntered);
                Assert.AreEqual(
                    before.ToJsonString(),
                    Phase6State.Financial(state.Database, state.Ledger).ToJsonString()
                );
                Assert.AreEqual(
                    history.ToJsonString(),
                    Phase6State.History(state.Database).ToJsonString()
                );
                Assert.AreEqual(
                    "credit_unknown",
                    Assert.ThrowsExactly<OperationException>(state.Ledger.RequirePaidAdmission).Code
                );
                AssertRecoveryFailure(state, originals[0], "usage-recovery", readerCause);
            }
            List<string> readerEntries = [];
            state.Reader = (row, token) =>
            {
                readerEntries.Add(row.Id);
                token.ThrowIfCancellationRequested();
                bool rejects = row.Id == rejected.Id;
                if (rejects && missingStart)
                {
                    // Take the real preactivation path; the sibling uses the validated-reader seam.
                    NativeWorkerFactory factory = new(
                        state.Database,
                        new(state.Database),
                        state.Ledger,
                        () => throw new InvalidOperationException(
                            "A missing original start must not create a recovery host."
                        )
                    );
                    return factory.ReadSettledUsageAsync(
                        row,
                        new GitOperations(state.Database, new(state.Database))
                            .GetWorkerPath(row.Worker),
                        Path.Combine(state.Database.StatePath, "native", "missing-start-recovery"),
                        token
                    );
                }
                if (rejects && rejection == "reader-error")
                {
                    throw new OperationException("credit_unknown", readerCause);
                }
                return Task.FromResult(
                    new RecoveredNativeUsage(
                        rejects && (rejection is "association" or "association-first")
                            ? row with
                            {
                                UsagePath = row.UsagePath + ".foreign",
                            }
                            : row,
                        new(
                            row.SessionId!,
                            120_000_000_000d,
                            rejects && rejection == "partial" ? "partial" : "complete",
                            2
                        )
                    )
                );
            };
            JsonObject result = await state
                .Reopen()
                .RunAsync("reconnect", context.CancellationToken);
            if (savedSessionMismatch)
            {
                Assert.DoesNotContain(rejected.Id, readerEntries);
                Assert.Contains(settled.Id, readerEntries);
            }
            if (missingStart)
            {
                CollectionAssert.AreEqual(new[] { rejected.Id, settled.Id }, readerEntries);
            }
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
            Assert.HasCount(2, actual);
            CreditReservation settledAfter = actual.Single(row => row.Id == settled.Id);
            CreditReservation rejectedAfter = actual.Single(row => row.Id == rejected.Id);
            Assert.AreEqual("reconciled", settledAfter.State);
            Assert.AreEqual(120m, settledAfter.Actual);
            Assert.AreEqual(40m, settledAfter.Actual!.Value - settled.Actual!.Value);
            Assert.AreEqual("uncertain", rejectedAfter.State);
            Assert.AreEqual(80m, rejectedAfter.Actual);
            Assert.AreEqual(rejected.Allowance, rejectedAfter.Allowance);
            JsonObject after = Phase6State.Financial(state.Database, state.Ledger);
            AssertOldReportsPreserved(before, after);
            Assert.AreEqual(
                before["reports"]!.AsArray().Count + 1,
                after["reports"]!.AsArray().Count
            );
            JsonObject finalReport = Assert.ContainsSingle(
                after["reports"]!.AsArray().OfType<JsonObject>().Where(report =>
                    report["reservation_id"]!.GetValue<string>() == settled.Id
                    && !before["reports"]!.AsArray().Any(old =>
                        old!["id"]!.GetValue<long>() == report["id"]!.GetValue<long>()
                    )
                )
            );
            Assert.AreEqual(
                120m,
                decimal.Parse(
                    finalReport["actual"]!.GetValue<string>(),
                    System.Globalization.CultureInfo.InvariantCulture
                )
            );
            if (savedSessionMismatch || missingStart)
            {
                Assert.AreEqual(rejected, rejectedAfter);
                Assert.AreEqual(
                    UsageRow(before, rejected.Id).ToJsonString(),
                    UsageRow(after, rejected.Id).ToJsonString()
                );
                JsonNode accounting = result["data"]!["accounting"]!;
                Assert.IsTrue(accounting["uncertain"]!.GetValue<bool>());
                Assert.AreEqual(
                    before["status"]!["actualReported"]!.GetValue<decimal>() + 40m,
                    accounting["actualReported"]!.GetValue<decimal>()
                );
                Assert.AreEqual(
                    before["status"]!["campaignCommitted"]!.GetValue<decimal>() + 20m,
                    accounting["campaignCommitted"]!.GetValue<decimal>()
                );
            }
            else if (rejectionFirst)
            {
                JsonObject raw = UsageRow(after, rejected.Id);
                Assert.AreEqual(
                    120_000_000_000d,
                    double.Parse(
                        raw["reported_nano_aiu"]!.GetValue<string>(),
                        System.Globalization.CultureInfo.InvariantCulture
                    )
                );
                Assert.AreEqual("complete", raw["status"]!.GetValue<string>());
                Assert.AreEqual(2L, raw["reported_requests"]!.GetValue<long>());
            }
            AssertRecoveryFailure(
                state,
                rejected,
                rejection switch
                {
                    "reader-error" => "usage-recovery",
                    "saved-session-first" => "usage-recovery-association",
                    "missing-start-first" => "usage-recovery-eligibility",
                    _ => "usage-recovery-ledger",
                },
                rejection switch
                {
                    "reader-error" => readerCause,
                    "saved-session-first" => "The saved native session/reservation association",
                    "missing-start-first" => "Required original native limit evidence is missing.",
                    "partial" => "absent, invalid or incomplete",
                    _ => "Settled recovery association changed",
                }
            );
            Assert.AreEqual(
                "credit_unknown",
                Assert.ThrowsExactly<OperationException>(state.Ledger.RequirePaidAdmission).Code
            );
            Assert.AreEqual(
                history.ToJsonString(),
                Phase6State.History(state.Database).ToJsonString()
            );
            if (missingStart)
            {
                readerEntries.Clear();
                JsonObject repeat = await state
                    .Reopen()
                    .RunAsync("reconnect", context.CancellationToken);
                Assert.AreEqual(result.ToJsonString(), repeat.ToJsonString());
                Assert.AreEqual(rejected.Id, Assert.ContainsSingle(readerEntries));
                Assert.AreEqual(
                    after.ToJsonString(),
                    Phase6State.Financial(state.Database, state.Ledger).ToJsonString()
                );
                Assert.AreEqual(
                    history.ToJsonString(),
                    Phase6State.History(state.Database).ToJsonString()
                );
            }
            JsonObject resume = await state.Reopen().RunAsync("resume", context.CancellationToken);
            Assert.IsFalse(resume["ok"]!.GetValue<bool>());
            Assert.AreEqual("conflict", resume["code"]!.GetValue<string>());
            Assert.AreEqual(
                history.ToJsonString(),
                Phase6State.History(state.Database).ToJsonString()
            );
        });

        static JsonObject UsageRow(JsonObject financial, string reservationId) =>
            Assert.ContainsSingle(
                financial["usage"]!.AsArray().OfType<JsonObject>().Where(row =>
                    row["reservation_id"]!.GetValue<string>() == reservationId
                )
            );
    }

    private static void AssertRecoveryFailure(
        Phase6PublicRun state,
        CreditReservation reservation,
        string phase,
        string cause
    )
    {
        using var connection = state.Database.Open();
        JsonObject detail = Assert.ContainsSingle(
            Phase6State.Rows(
                connection,
                "SELECT value FROM meta WHERE key LIKE 'native:recovery_failure_details:%';"
            )
                .OfType<JsonObject>()
                .SelectMany(row => JsonNode.Parse(row["value"]!.GetValue<string>())!.AsArray())
                .OfType<JsonObject>()
                .Where(row =>
                    row["reservationId"]?.GetValue<string>() == reservation.Id
                    && row["phase"]?.GetValue<string>() == phase
                )
        );
        Assert.AreEqual(reservation.Worker, detail["worker"]!.GetValue<string>());
        Assert.Contains(cause, detail["error"]!.ToJsonString());
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
    [Timeout(120_000, CooperativeCancellation = true)]
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
            Assert.AreEqual(
                "unavailable",
                result["code"]!.GetValue<string>(),
                $"Original reconnect result: {DescribeReconnectResult(result)}"
            );
            Assert.Contains("reader gate", result["message"]!.GetValue<string>());
            Assert.AreNotEqual(
                "usage-settlement",
                result["data"]?["operation"]?.GetValue<string>()
            );
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

        static string DescribeReconnectResult(JsonObject original) =>
            new JsonObject
            {
                ["ok"] = original["ok"] is JsonValue success && success.TryGetValue(out bool ok)
                    ? JsonValue.Create(ok)
                    : null,
                ["code"] = BoundedField(original["code"], 64),
                ["message"] = BoundedField(original["message"], 256),
                ["data.operation"] = BoundedField(
                    (original["data"] as JsonObject)?["operation"],
                    64
                ),
            }.ToJsonString();

        static string? BoundedField(JsonNode? field, int limit)
        {
            if (
                field is not JsonValue value
                || !value.TryGetValue(out string? text)
                || text is null
            )
            {
                return null;
            }
            string safe = string.Concat(
                text.Take(limit).Select(ch => ch is >= ' ' and <= '~' ? ch : ' ')
            );
            return text.Length <= limit ? safe : safe + "...";
        }
    }

    [TestMethod]
    [Timeout(120_000, CooperativeCancellation = true)]
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
