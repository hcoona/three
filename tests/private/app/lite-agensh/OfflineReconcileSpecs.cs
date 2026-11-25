using System.Diagnostics;
using System.Text.Json.Nodes;
using LiteAgensh.Tests.Support;
using Microsoft.Data.Sqlite;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace LiteAgensh.Tests;

[TestClass]
public sealed class OfflineReconcileSpecs(TestContext context)
{
    private static readonly string[] cancellationFailureCodes =
    [
        "credit_unknown",
        "credit_exhausted",
    ];

    [TestMethod]
    [Timeout(120_000, CooperativeCancellation = true)]
    public async Task EndedRunExclusiveProofAndRecheck()
    {
        UnresolvedRunCreator creator = await UnresolvedRunCreator
            .OpenAsync(context.CancellationToken);
        await creator.RunBodyAsync(async () =>
        {
            Coordinator accounting = creator.AccountingOnly();
            JsonObject active = RunStateQueries.Financial(creator.Database, creator.Ledger);
            JsonObject conflict = accounting.Reconcile(creator.TargetId);
            Assert.AreEqual("conflict", conflict["code"]!.GetValue<string>());
            Assert.AreEqual(
                active.ToJsonString(),
                RunStateQueries.Financial(creator.Database, creator.Ledger).ToJsonString()
            );
            await creator.EndRunAsync(context.CancellationToken);
            AssertGenuineTerminal(creator);
            JsonObject history = RunStateQueries.History(creator.Database);
            JsonObject money = RunStateQueries.Financial(creator.Database, creator.Ledger);
            JsonObject live = accounting.Reconcile(creator.TargetId);
            Assert.AreEqual("credit_unknown", live["code"]!.GetValue<string>());
            Assert.AreEqual(
                "An exact original owner/runtime identity is still alive.",
                live["message"]!.GetValue<string>()
            );
            Assert.AreEqual(
                history.ToJsonString(),
                RunStateQueries.History(creator.Database).ToJsonString()
            );
            Assert.AreEqual(
                money.ToJsonString(),
                RunStateQueries.Financial(creator.Database, creator.Ledger).ToJsonString()
            );
            await creator.ExitAsync(context.CancellationToken);
            AssertSyntheticRuntimeIdentityRefusals(context, creator, accounting);
            AssertCampaignBindingRefusal(context, creator, accounting);
            AssertCancellationAndRepeat(creator, accounting, null, "controlled-native-dependency");
            // Controlled local cancellation is legitimate; native PAID admission
            // must still reject the unchanged non-native receipt namespace.
            CollaborationOperations collaboration = new(creator.Database);
            Coordinator native = new(
                creator.Database,
                collaboration,
                new(creator.Database, collaboration)
            );
            JsonObject settled = RunStateQueries.Financial(creator.Database, creator.Ledger);
            JsonObject rejected = await native.CalibrateAsync(context.CancellationToken);
            Assert.AreEqual("unavailable", rejected["code"]!.GetValue<string>());
            Assert.Contains("preflight", rejected["message"]!.GetValue<string>());
            Assert.AreEqual(
                settled.ToJsonString(),
                RunStateQueries.Financial(creator.Database, creator.Ledger).ToJsonString()
            );
            Assert.IsNull(
                GitScenarioState.Data(native.Status())["nativeBillingCalibration"]
            );
        });
    }

    [TestMethod]
    [Timeout(120_000, CooperativeCancellation = true)]
    public async Task ReusedRuntimePidLeavesCurrentHelperLiveAndReleasesUncalledBacking()
    {
        UnresolvedRunCreator creator = await UnresolvedRunCreator
            .OpenAsync(context.CancellationToken);
        await creator.RunBodyAsync(async () =>
        {
            await creator.EndRunAsync(context.CancellationToken);
            AssertGenuineTerminal(creator);
            await using OwnedChildScope child = new(context);
            Process live = child.Control.StartDirect();
            OwnedChildIdentity ready = await child.Control.ReadReadyAsync(
                context.CancellationToken
            );
            Assert.IsFalse(live.HasExited);
            Assert.AreEqual(live.Id, ready.Pid);
            Assert.AreEqual(live.StartTime.ToUniversalTime().Ticks, ready.BirthTicks);
            long oldBirth = ready.BirthTicks - 1;
            Assert.IsGreaterThan(0L, oldBirth);
            await creator.ExitAsync(context.CancellationToken);
            JsonNode diagnostic = JsonNode.Parse(
                creator.Database.ReadMetadata($"native:session:{creator.Target.SessionId}")!
            )!;
            long lease = diagnostic["runtimeLeaseId"]!.GetValue<long>();
            using SqliteConnection connection = creator.Database.Open();
            JsonNode original = Assert.ContainsSingle(RunStateQueries.Rows(
                connection,
                "SELECT pid,start_ticks FROM process_leases WHERE id=$id AND state='exited';",
                ("$id", lease)
            ))!;
            JsonObject originalHistory = RunStateQueries.History(creator.Database);
            using (SyntheticLeaseScope leaseGuard = new(
                context,
                creator.Database,
                connection,
                lease,
                original["pid"]!.GetValue<long>(),
                original["start_ticks"]!.GetValue<long>(),
                originalHistory
            ))
            {
                // Synthetic retained identity; the helper's observed birth stays unchanged.
                leaseGuard.SetIdentity(ready.Pid, oldBirth);
                AssertCancellationAndRepeat(
                    creator,
                    creator.AccountingOnly(),
                    null,
                    "controlled-native-dependency"
                );
                Assert.IsFalse(live.HasExited);
                Assert.AreEqual(ready.Pid, live.Id);
                Assert.AreEqual(ready.BirthTicks, live.StartTime.ToUniversalTime().Ticks);
                leaseGuard.Complete();
            }

            using CancellationTokenSource exit =
                CancellationTokenSource.CreateLinkedTokenSource(context.CancellationToken);
            exit.CancelAfter(TimeSpan.FromSeconds(5));
            await child.Control.ReleaseAsync(exit.Token);
            await live.WaitForExitAsync(exit.Token);
            Assert.AreEqual(0, live.ExitCode);
            child.Complete();
        });
    }

    [TestMethod(DisplayName = "SYNTHETIC CONTRACT ONLY: legacy compatibility guard inputs")]
    [Timeout(120_000, CooperativeCancellation = true)]
    public async Task LegacyCompatibilityContractGuards()
    {
        UnresolvedRunCreator creator = await UnresolvedRunCreator
            .OpenAsync(context.CancellationToken);
        await creator.RunBodyAsync(async () =>
        {
            await creator.EndRunAsync(context.CancellationToken);
            AssertGenuineTerminal(creator);
            await creator.ExitAsync(context.CancellationToken);
            CancellationGuardInput input = await CancellationGuardInput.CreateAsync(
                creator,
                context.CancellationToken
            );
            Coordinator accounting = creator.AccountingOnly();
            JsonObject history = RunStateQueries.History(creator.Database);
            JsonObject money = RunStateQueries.Financial(creator.Database, creator.Ledger);
            foreach (
                (string defect, string message) in new[]
                {
                    ("C", "Legacy proof-path source compatibility is unestablished."),
                    ("H", "A referenced owned observation differs from the retained evidence."),
                    (
                        "A",
                        "The referenced native observations contradict the live retained "
                            + "associations."
                    ),
                    (
                        "W",
                        "Retained source/worktree observations contradict the compatible source "
                            + "lineage."
                    ),
                }
            )
            {
                using LegacyDefectScope guard = new(context, input, defect);
                input.Damage(defect);
                JsonObject result = accounting.Reconcile(creator.TargetId, input.EvidencePath);
                Assert.IsFalse(result["ok"]!.GetValue<bool>());
                Assert.AreEqual("credit_unknown", result["code"]!.GetValue<string>());
                Assert.AreEqual(message, result["message"]!.GetValue<string>());
                Assert.IsNull(result["data"]);
                Assert.AreEqual(
                    history.ToJsonString(),
                    RunStateQueries.History(creator.Database).ToJsonString()
                );
                Assert.AreEqual(
                    money.ToJsonString(),
                    RunStateQueries.Financial(creator.Database, creator.Ledger).ToJsonString()
                );
                Assert.AreEqual("uncertain", creator.Target.State);
                Assert.IsNull(creator.Target.Actual);
                guard.Complete();
            }

            AssertCancellationAndRepeat(creator, accounting, input.EvidencePath, "native-sdk");
        });
    }

    [TestMethod]
    [Timeout(120_000)]
    [DataRow("dispatch")]
    [DataRow("contradictory-usage")]
    [DataRow("stale-expected")]
    public void CoreStaleProofRechecksCurrentState(string mutation)
    {
        context.CancellationToken.ThrowIfCancellationRequested();
        using ScratchDirectory scratch = new();
        CreditLedger ledger = new(scratch.PathFor("core.sqlite"), 200);
        (CreditReservation row, JsonObject proof) = CoreOriginal(ledger, scratch.PathFor("raw"));
        CreditReservation expected = row;
        if (mutation == "dispatch")
        {
            ledger.BeginDispatch(row.RunId, row.Id, 200);
            OperationException incomplete = Assert.ThrowsExactly<OperationException>(() =>
                ledger.RecordSdkUsage(
                    row.Id,
                    new(row.SessionId!, 0, null, 0),
                    final: false
                )
            );
            Assert.AreEqual("credit_unknown", incomplete.Code);
        }
        else if (mutation == "contradictory-usage")
        {
            OperationException error = Assert.ThrowsExactly<OperationException>(() =>
                ledger.RecordSdkUsage(
                    row.Id,
                    new(row.SessionId!, 1_000_000_000d, "partial", 1),
                    final: false
                )
            );
            Assert.AreEqual("credit_unknown", error.Code);
        }
        else
        {
            expected = row with { Allowance = row.Allowance + 1 };
        }
        JsonObject before = CoreProjection(ledger, scratch.PathFor("core.sqlite"));
        if (mutation == "dispatch")
        {
            JsonNode usage = Assert.ContainsSingle(before["usage"]!.AsArray())!;
            Assert.AreEqual(row.SessionId, usage["session_id"]!.GetValue<string>());
            Assert.AreEqual(
                proof["diagnostic"]!["reportedNanoAiu"]!.GetValue<string>(),
                usage["reported_nano_aiu"]!.GetValue<string>()
            );
            Assert.IsNull(usage["status"]);
            Assert.AreEqual(0L, usage["reported_requests"]!.GetValue<long>());
            Assert.AreEqual(1L, usage["dispatch_count"]!.GetValue<long>());
            Assert.AreEqual("uncertain", ledger.Reservations().Single().State);
        }
        Assert.IsEmpty(before["status"]!["cancellations"]!.AsArray());
        Assert.IsEmpty(before["reports"]!.AsArray());
        OperationException rejection = Assert.ThrowsExactly<OperationException>(() =>
            ledger.CancelBeforeDispatch(expected, proof, "core-guard-input-not-public-proof")
        );
        Assert.AreEqual("credit_unknown", rejection.Code);
        Assert.AreEqual(
            mutation switch
            {
                "dispatch" => "Live SDK usage or ownership contradicts cancellation.",
                "contradictory-usage" =>
                    "A prior contradictory native reading prohibits cancellation.",
                _ => "Cancellation association changed.",
            },
            rejection.Message
        );
        Assert.AreEqual(
            before.ToJsonString(),
            CoreProjection(ledger, scratch.PathFor("core.sqlite")).ToJsonString()
        );
        Assert.IsNull(ledger.Reservations().Single().Actual);
        Assert.AreNotEqual("released", ledger.Reservations().Single().State);
    }

    [TestMethod]
    [Timeout(120_000)]
    public void CoreCancellationRejectsContradictoryRepeatWithoutChangingStoredProof()
    {
        context.CancellationToken.ThrowIfCancellationRequested();
        using ScratchDirectory scratch = new();
        string path = scratch.PathFor("core.sqlite");
        CreditLedger ledger = new(path, 200);
        (CreditReservation row, JsonObject proof) = CoreOriginal(ledger, scratch.PathFor("raw"));
        proof["source"] = new JsonObject
        {
            ["applicationBuild"] = "11111111-1111-4111-8111-111111111111",
        };
        const string inspection = "core-guard-input-not-public-proof";
        JsonObject first = ledger.CancelBeforeDispatch(row, proof, inspection);
        Assert.IsFalse(first["idempotent"]!.GetValue<bool>());
        Assert.AreEqual("released", ledger.Reservations().Single().State);
        Assert.IsFalse(ledger.Reservations().Single().GrantOpen);
        JsonObject before = CoreProjection(ledger, path);
        using SqliteConnection connection = RunStateQueries.OpenFile(path);
        const string cancellationRows =
            "SELECT * FROM credit_cancellations ORDER BY reservation_id;";
        string stored = RunStateQueries.Rows(connection, cancellationRows).ToJsonString();
        JsonObject contradictory = proof.DeepClone().AsObject();
        contradictory["source"]!["applicationBuild"] = "22222222-2222-4222-8222-222222222222";

        OperationException rejected = Assert.ThrowsExactly<OperationException>(() =>
            ledger.CancelBeforeDispatch(row, contradictory, inspection)
        );
        Assert.AreEqual("credit_unknown", rejected.Code);
        Assert.AreEqual(
            "Existing cancellation has contradictory retained evidence.", rejected.Message
        );
        Assert.AreEqual(before.ToJsonString(), CoreProjection(ledger, path).ToJsonString());
        Assert.AreEqual(stored, RunStateQueries.Rows(connection, cancellationRows).ToJsonString());

        JsonObject repeat = ledger.CancelBeforeDispatch(row, proof, inspection);
        Assert.IsTrue(repeat["idempotent"]!.GetValue<bool>());
        Assert.AreEqual(first["proof"]!.ToJsonString(), repeat["proof"]!.ToJsonString());
        Assert.AreEqual(before.ToJsonString(), CoreProjection(ledger, path).ToJsonString());
        Assert.AreEqual(stored, RunStateQueries.Rows(connection, cancellationRows).ToJsonString());
    }

    [TestMethod]
    [Timeout(120_000, CooperativeCancellation = true)]
    public async Task CoreCancellationAndAdmissionConserveOneAuthorization()
    {
        using ScratchDirectory scratch = new();
        string path = scratch.PathFor("core.sqlite");
        CreditLedger ledger = new(path, 200);
        (CreditReservation row, JsonObject proof) = CoreOriginal(ledger, scratch.PathFor("raw"));
        await using ContenderGateScope gate = new(context, 2);

        async Task<bool> CompeteAsync(int index)
        {
            await gate.SignalReadyAndWaitAsync(index, context.CancellationToken);
            try
            {
                if (index == 0)
                {
                    ledger.CancelBeforeDispatch(row, proof, "core-guard-input-not-public-proof");
                }
                else
                {
                    ledger.BeginDispatch(row.RunId, row.Id, 200);
                }
                return true;
            }
            catch (OperationException error)
            {
                Assert.Contains(error.Code, cancellationFailureCodes);
                return false;
            }
        }

        Task<bool> cancellation = gate.Track(
            Task.Run(() => CompeteAsync(0), context.CancellationToken)
        );
        Task<bool> admission = gate.Track(
            Task.Run(() => CompeteAsync(1), context.CancellationToken)
        );
        await gate.ReleaseWhenAllReadyAsync(context.CancellationToken);
        bool[] outcomes = await Task.WhenAll(cancellation, admission);
        gate.Complete();

        Assert.AreEqual(1, outcomes.Count(value => value));
        CreditReservation actual = ledger.Reservations().Single();
        JsonObject status = ledger.Status(row.RunId, 200);
        Assert.AreEqual(outcomes[0] ? "released" : "active", actual.State);
        Assert.AreEqual(outcomes[0] ? 0m : 100m, status["campaignCommitted"]!.GetValue<decimal>());
        Assert.IsNull(actual.Actual);
        JsonObject reported = Assert.ContainsSingle(status["reported"]!.AsArray())!.AsObject();
        Assert.AreEqual(outcomes[0] ? 0L : 1L, reported["dispatches"]!.GetValue<long>());
        Assert.IsFalse(ledger.HasCompleteSdkUsage(row.Id));
        using SqliteConnection connection = RunStateQueries.OpenFile(path);
        Assert.IsEmpty(RunStateQueries.Rows(connection, "SELECT * FROM credit_reports;"));
    }

    private static void AssertGenuineTerminal(UnresolvedRunCreator creator)
    {
        RunDatabase database = creator.Database;
        CreditReservation row = creator.Target;
        Assert.AreEqual("uncertain", row.State);
        Assert.AreEqual(300m, row.Allowance);
        Assert.IsNull(row.Actual);
        Assert.AreEqual(0m, row.Overshoot);
        Assert.AreEqual("unresolved", database.ReadMetadata("lifecycle"));
        Assert.AreEqual(
            1L,
            RunStateQueries.Number(
                database,
                "SELECT COUNT(*) FROM coordinator_segments WHERE ended_at IS NOT NULL;"
            )
        );
        Assert.AreEqual(
            0L,
            RunStateQueries.Number(database, "SELECT COUNT(*) FROM coordinator_turns;")
        );
        Assert.AreEqual("stopped", database.ReadMetadata("worker:worker1:lifecycle"));
        Assert.AreEqual(
            1L,
            RunStateQueries.Number(
                database,
                """
                SELECT COUNT(*) FROM native_sessions n JOIN native_attempts a
                  ON a.reservation_id=n.reservation_id AND a.session_id=n.session_id
                WHERE n.reservation_id=$id AND n.worker='worker1' AND a.worker=n.worker
                  AND a.baseline_usage='0' AND a.initialize_json=n.initialize_json
                  AND n.recovery='fresh-session-with-durable-summary';
                """,
                ("$id", row.Id)
            )
        );
        JsonObject diagnostic = JsonNode
            .Parse(database.ReadMetadata($"native:session:{row.SessionId}")!)!
            .AsObject();
        Assert.AreEqual(row.SessionId, diagnostic["sessionId"]!.GetValue<string>());
        Assert.AreEqual("0", diagnostic["reportedNanoAiu"]!.GetValue<string>());
        Assert.AreEqual(0L, diagnostic["totalUserRequests"]!.GetValue<long>());
        Assert.IsNull(diagnostic["aiCreditsStatus"]);
        Assert.IsFalse(diagnostic["alreadyInUse"]!.GetValue<bool>());
        Assert.IsFalse(diagnostic["isRemote"]!.GetValue<bool>());
        Assert.IsTrue(diagnostic["paidReservation"]!.GetValue<bool>());
        Assert.AreEqual("controlled-model", diagnostic["selectedModel"]!.GetValue<string>());
        Assert.AreEqual(
            NativeSdkSession.NativeLimit(300),
            diagnostic["appliedLimit"]!.GetValue<double>()
        );
        Assert.AreEqual(NativeSdkSession.SdkVersion, diagnostic["sdkVersion"]!.GetValue<string>());
        Assert.AreEqual("1.0.107", diagnostic["runtimeVersion"]!.GetValue<string>());
        string runtimeId = diagnostic["runtimeId"]!.GetValue<string>();
        long lease = diagnostic["runtimeLeaseId"]!.GetValue<long>();
        Assert.IsGreaterThan(0L, lease);
        JsonObject runtime = JsonNode
            .Parse(database.ReadMetadata("native:runtime:" + runtimeId)!)!
            .AsObject();
        Assert.AreEqual("settled", runtime["state"]!.GetValue<string>());
        Assert.AreEqual(lease, runtime["runtimeLeaseId"]!.GetValue<long>());
        Assert.AreEqual(
            1L,
            RunStateQueries.Number(
                database,
                (
                    "SELECT COUNT(*) FROM process_leases WHERE id=$id AND state='exited' "
                    + "AND kind=$kind;"
                ),
                ("$id", lease),
                ("$kind", "native:runtime:" + runtimeId)
            )
        );
        JsonObject marker = JsonNode
            .Parse(database.ReadMetadata(NoDispatchCancellation.Marker(row.Id))!)!
            .AsObject();
        Assert.AreEqual(
            typeof(NativeWorker).Assembly.ManifestModule.ModuleVersionId.ToString(),
            marker["applicationBuild"]!.GetValue<string>()
        );
        Assert.AreEqual("controlled-native-dependency", marker["evidenceKind"]!.GetValue<string>());
        Assert.AreEqual(row.SessionId, marker["initial"]!["sessionId"]!.GetValue<string>());
        Assert.AreEqual(0L, marker["turnBaseline"]!.GetValue<long>());
        Assert.Contains(
            "target-release-blocked",
            creator.Ended["settlementFailures"]!.ToJsonString()
        );
        Assert.HasCount(3, creator.Ended["runtimes"]!.AsArray());
        foreach (JsonNode? item in creator.Ended["runtimes"]!.AsArray())
        {
            Assert.IsTrue(item!["normalHostSettlement"]!.GetValue<bool>());
            Assert.IsTrue(item["wrapperDisposed"]!.GetValue<bool>());
            Assert.AreEqual(0, item["exit"]!["exitCode"]!.GetValue<int>());
        }
        CreditReservation calibration = creator
            .Ledger.Reservations(database.RunId)
            .Single(item => item.Worker == "calibration");
        Assert.AreEqual(7.25m, calibration.Actual);
        Assert.AreEqual("reconciled", calibration.State);
        using SqliteConnection campaign = RunStateQueries.OpenCampaign(database);
        Assert.AreEqual(
            1L,
            Sql.Scalar(
                campaign,
                "SELECT dispatch_count FROM credit_sdk_usage WHERE reservation_id=$id;",
                null,
                ("$id", calibration.Id)
            )
        );
        Assert.AreEqual(
            1L,
            Sql.Scalar(
                campaign,
                "SELECT COUNT(*) FROM credit_reports WHERE reservation_id=$id;",
                null,
                ("$id", calibration.Id)
            )
        );
        Assert.AreEqual(
            0L,
            Sql.Scalar(
                campaign,
                "SELECT dispatch_count FROM credit_sdk_usage WHERE reservation_id=$id;",
                null,
                ("$id", row.Id)
            )
        );
        Assert.IsEmpty(
            RunStateQueries.Rows(
                campaign,
                "SELECT * FROM credit_reports WHERE reservation_id=$id;",
                ("$id", row.Id)
            )
        );
    }

    private static void AssertSyntheticRuntimeIdentityRefusals(
        TestContext context,
        UnresolvedRunCreator creator,
        Coordinator accounting
    )
    {
        JsonNode diagnostic = JsonNode.Parse(
            creator.Database.ReadMetadata($"native:session:{creator.Target.SessionId}")!
        )!;
        long lease = diagnostic["runtimeLeaseId"]!.GetValue<long>();
        using SqliteConnection connection = creator.Database.Open();
        JsonNode original = Assert.ContainsSingle(RunStateQueries.Rows(
            connection,
            "SELECT pid,start_ticks FROM process_leases WHERE id=$id AND state='exited';",
            ("$id", lease)
        ))!;
        using Process live = Process.GetCurrentProcess();
        JsonObject originalHistory = RunStateQueries.History(creator.Database);
        foreach ((long pid, long birth, string reason) in new[]
        {
            (
                (long)live.Id,
                live.StartTime.ToUniversalTime().Ticks,
                "An exact original owner/runtime identity is still alive."
            ),
            (
                original["pid"]!.GetValue<long>(),
                0L,
                "Original process identity is missing."
            ),
        })
        {
            using SyntheticLeaseScope leaseGuard = new(
                context,
                creator.Database,
                connection,
                lease,
                original["pid"]!.GetValue<long>(),
                original["start_ticks"]!.GetValue<long>(),
                originalHistory
            );
            leaseGuard.SetIdentity(pid, birth);
            JsonObject history = RunStateQueries.History(creator.Database);
            JsonObject money = RunStateQueries.Financial(creator.Database, creator.Ledger);
            JsonObject rejected = accounting.Reconcile(creator.TargetId);
            Assert.AreEqual(
                "credit_unknown", rejected["code"]?.GetValue<string>(), rejected.ToJsonString()
            );
            Assert.AreEqual(reason, rejected["message"]!.GetValue<string>());
            Assert.AreEqual(
                history.ToJsonString(),
                RunStateQueries.History(creator.Database).ToJsonString()
            );
            Assert.AreEqual(
                money.ToJsonString(),
                RunStateQueries.Financial(creator.Database, creator.Ledger).ToJsonString()
            );
            leaseGuard.Complete();
        }
    }

    private static void AssertCampaignBindingRefusal(
        TestContext context,
        UnresolvedRunCreator creator,
        Coordinator accounting
    )
    {
        JsonObject originalHistory = RunStateQueries.History(creator.Database);
        string originalBinding = creator.Database.ReadMetadata("native:ledger_binding")!;
        using (MetadataRestoreScope restore = new(
            context,
            creator.Database,
            "native:ledger_binding",
            originalBinding,
            originalHistory
        ))
        {
            // Deliberate unsupported binding INPUT, not repaired setup/provenance.
            creator.Database.WriteMetadata("native:ledger_binding", originalBinding + ".foreign");
            JsonObject history = RunStateQueries.History(creator.Database);
            JsonObject money = RunStateQueries.Financial(creator.Database, creator.Ledger);
            JsonObject rejected = accounting.Reconcile(creator.TargetId);
            Assert.AreEqual("credit_unknown", rejected["code"]!.GetValue<string>());
            Assert.AreEqual(
                "The retained campaign binding changed.",
                rejected["message"]!.GetValue<string>()
            );
            Assert.AreEqual(
                history.ToJsonString(),
                RunStateQueries.History(creator.Database).ToJsonString()
            );
            Assert.AreEqual(
                money.ToJsonString(),
                RunStateQueries.Financial(creator.Database, creator.Ledger).ToJsonString()
            );
            restore.Complete();
        }
    }

    private static void AssertCancellationAndRepeat(
        UnresolvedRunCreator creator,
        Coordinator accounting,
        string? evidence,
        string kind
    )
    {
        JsonObject history = RunStateQueries.History(creator.Database);
        JsonObject before = RunStateQueries.Financial(creator.Database, creator.Ledger);
        CreditReservation target = creator.Target;
        JsonObject first = accounting.Reconcile(target.Id, evidence);
        JsonObject result = GitScenarioState.Data(first);
        Assert.AreEqual(kind, first["nativeEvidenceKind"]!.GetValue<string>());
        Assert.AreEqual(
            "verified-before-first-dispatch",
            result["classification"]!.GetValue<string>()
        );
        Assert.AreEqual(target.Id, result["reservationId"]!.GetValue<string>());
        Assert.IsFalse(result["idempotent"]!.GetValue<bool>());
        Assert.IsNull(result["measuredActual"]);
        Assert.AreEqual("0", result["reportedNanoAiu"]!.GetValue<string>());
        Assert.IsNull(result["aiCreditsStatus"]);
        Assert.AreEqual("released", creator.Target.State);
        Assert.IsFalse(creator.Target.GrantOpen);
        Assert.IsNull(creator.Target.Actual);
        JsonObject status = creator.Ledger.Status(creator.Database.RunId, 307.25m);
        decimal held =
            target.Shared && !target.GrantOpen
                ? target.Actual ?? 0
                : Math.Max(target.Allowance, target.Actual ?? 0);
        decimal open =
            target.Shared && target.GrantOpen
                ? Math.Max(0, target.Allowance - (target.Actual ?? 0))
                : 0;
        Assert.AreEqual(
            held,
            before["status"]!["campaignCommitted"]!.GetValue<decimal>()
                - status["campaignCommitted"]!.GetValue<decimal>()
        );
        Assert.AreEqual(
            open,
            before["status"]!["outstandingAuthorization"]!.GetValue<decimal>()
                - status["outstandingAuthorization"]!.GetValue<decimal>()
        );
        Assert.AreEqual(7.25m, status["actualReported"]!.GetValue<decimal>());
        Assert.AreEqual(0m, status["overshoot"]!.GetValue<decimal>());
        Assert.AreEqual(
            history.ToJsonString(),
            RunStateQueries.History(creator.Database).ToJsonString()
        );
        JsonObject after = RunStateQueries.Financial(creator.Database, creator.Ledger);
        Assert.AreEqual(before["reports"]!.ToJsonString(), after["reports"]!.ToJsonString());
        Assert.AreEqual(before["owners"]!.ToJsonString(), after["owners"]!.ToJsonString());
        Assert.AreEqual(before["usage"]!.ToJsonString(), after["usage"]!.ToJsonString());
        foreach (
            JsonNode? peer in before["rows"]!
                .AsArray()
                .Where(item => item!["id"]!.GetValue<string>() != target.Id)
        )
        {
            Assert.AreEqual(
                peer!.ToJsonString(),
                after["rows"]!
                    .AsArray()
                    .Single(item =>
                        item!["id"]!.GetValue<string>() == peer["id"]!.GetValue<string>()
                    )!
                    .ToJsonString()
            );
        }
        Assert.HasCount(1, status["cancellations"]!.AsArray());
        Assert.AreEqual(
            "retained-exact-process-check",
            status["cancellations"]![0]!["inspection"]!.GetValue<string>()
        );
        JsonObject repeat = GitScenarioState.Data(accounting.Reconcile(target.Id, evidence));
        Assert.IsTrue(repeat["idempotent"]!.GetValue<bool>());
        Assert.AreEqual(result["proof"]!.ToJsonString(), repeat["proof"]!.ToJsonString());
        Assert.AreEqual(
            after.ToJsonString(),
            RunStateQueries.Financial(creator.Database, creator.Ledger).ToJsonString()
        );
        Assert.AreEqual(
            history.ToJsonString(),
            RunStateQueries.History(creator.Database).ToJsonString()
        );
    }

    private static (CreditReservation Row, JsonObject Proof) CoreOriginal(
        CreditLedger ledger,
        string usagePath
    )
    {
        CreditReservation reserved = ledger.Reserve(
            "reconcile-core", "worker1", 100, 200, usagePath
        );
        ledger.MarkStarted(reserved.Id);
        ledger.AttachSession(reserved.Id, reserved.SessionId!);
        ledger.RecordSdkUsage(reserved.Id, new(reserved.SessionId!, 0, null, 0), final: false);
        CreditReservation row = ledger.Reservations().Single();
        return (
            row,
            new()
            {
                ["coreGuardInputLabel"] = "CORE ONLY; NOT ACQUIRED PUBLIC/OS/NATIVE PROOF",
                ["owner"] = ledger.ReadSdkOwner(row.Id),
                ["diagnostic"] = new JsonObject
                {
                    ["reportedNanoAiu"] = "0",
                    ["aiCreditsStatus"] = null,
                },
            }
        );
    }

    private static JsonObject CoreProjection(CreditLedger ledger, string path)
    {
        using SqliteConnection connection = RunStateQueries.OpenFile(path);
        return new()
        {
            ["status"] = ledger.Status("reconcile-core", 200),
            ["reservations"] = RunStateQueries.Rows(
                connection, "SELECT * FROM credit_reservations;"
            ),
            ["usage"] = RunStateQueries.Rows(connection, "SELECT * FROM credit_sdk_usage;"),
            ["owners"] = RunStateQueries.Rows(connection, "SELECT * FROM credit_sdk_owners;"),
            ["reports"] = RunStateQueries.Rows(connection, "SELECT * FROM credit_reports;"),
        };
    }

    private sealed class OwnedChildScope(TestContext context) : IAsyncDisposable
    {
        private bool completed;

        internal OwnedChildControl Control { get; } = new();

        internal void Complete() => completed = true;

        public async ValueTask DisposeAsync()
        {
            try
            {
                await Control.DisposeAsync();
            }
            catch (Exception secondary) when (!completed)
            {
                context.WriteLine("Secondary reused-PID helper cleanup failure: {0}", secondary);
            }
        }
    }

    private sealed class SyntheticLeaseScope(
        TestContext context,
        RunDatabase database,
        SqliteConnection connection,
        long lease,
        long originalPid,
        long originalBirthTicks,
        JsonObject originalHistory
    ) : IDisposable
    {
        private bool completed;

        internal void SetIdentity(long pid, long birth)
        {
            using SqliteCommand update = Sql.Command(
                connection,
                "UPDATE process_leases SET pid=$pid,start_ticks=$birth "
                    + "WHERE id=$id AND state='exited';",
                null,
                ("$pid", pid),
                ("$birth", birth),
                ("$id", lease)
            );
            Assert.AreEqual(1, update.ExecuteNonQuery());
        }

        internal void Complete() => completed = true;

        public void Dispose()
        {
            try
            {
                SetIdentity(originalPid, originalBirthTicks);
                Assert.AreEqual(
                    originalHistory.ToJsonString(),
                    RunStateQueries.History(database).ToJsonString()
                );
            }
            catch (Exception secondary) when (!completed)
            {
                context.WriteLine(
                    "Secondary synthetic runtime lease restore failure: {0}",
                    secondary
                );
            }
        }
    }

    private sealed class MetadataRestoreScope(
        TestContext context,
        RunDatabase database,
        string key,
        string originalValue,
        JsonObject originalHistory
    ) : IDisposable
    {
        private bool completed;

        internal void Complete() => completed = true;

        public void Dispose()
        {
            try
            {
                database.WriteMetadata(key, originalValue);
                Assert.AreEqual(
                    originalHistory.ToJsonString(),
                    RunStateQueries.History(database).ToJsonString()
                );
            }
            catch (Exception secondary) when (!completed)
            {
                context.WriteLine("Secondary metadata restore failure: {0}", secondary);
            }
        }
    }

    private sealed class LegacyDefectScope(
        TestContext context,
        CancellationGuardInput input,
        string defect
    ) : IDisposable
    {
        private bool completed;

        internal void Complete() => completed = true;

        public void Dispose()
        {
            try
            {
                input.Restore(defect);
            }
            catch (Exception secondary) when (!completed)
            {
                context.WriteLine(
                    "Secondary legacy guard restore failure ({0}): {1}",
                    defect,
                    secondary
                );
            }
        }
    }

    private sealed class ContenderGateScope(TestContext context, int count) : IAsyncDisposable
    {
        private readonly TaskCompletionSource go =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly TaskCompletionSource[] ready =
        [
            .. Enumerable
                .Range(0, count)
                .Select(_ => new TaskCompletionSource(
                    TaskCreationOptions.RunContinuationsAsynchronously
                )),
        ];
        private readonly List<Task> tasks = [];
        private bool completed;

        internal Task<T> Track<T>(Task<T> task)
        {
            tasks.Add(task);
            return task;
        }

        internal async Task SignalReadyAndWaitAsync(int index, CancellationToken token)
        {
            ready[index].TrySetResult();
            await go.Task.WaitAsync(token);
        }

        internal async Task ReleaseWhenAllReadyAsync(CancellationToken token)
        {
            try
            {
                await Task.WhenAll(ready.Select(item => item.Task))
                    .WaitAsync(TimeSpan.FromSeconds(5), token);
            }
            finally
            {
                go.TrySetResult();
            }
        }

        internal void Complete() => completed = true;

        public async ValueTask DisposeAsync()
        {
            go.TrySetResult();
            try
            {
                await Task.WhenAll(tasks).WaitAsync(
                    TimeSpan.FromSeconds(5),
                    CancellationToken.None
                );
            }
            catch (Exception secondary) when (!completed)
            {
                context.WriteLine("Secondary contender gate cleanup failure: {0}", secondary);
            }
        }
    }
}
