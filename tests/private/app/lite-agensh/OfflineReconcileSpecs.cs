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
    [Timeout(120_000)]
    public async Task EndedRunExclusiveProofAndRecheck()
    {
        Phase6OwnedCreator creator = await Phase6OwnedCreator.OpenAsync(context.CancellationToken);
        await creator.RunBodyAsync(async () =>
        {
            Coordinator accounting = creator.AccountingOnly();
            JsonObject active = Phase6State.Financial(creator.Database, creator.Ledger);
            JsonObject conflict = accounting.Reconcile(creator.TargetId);
            Assert.AreEqual("conflict", conflict["code"]!.GetValue<string>());
            Assert.AreEqual(
                active.ToJsonString(),
                Phase6State.Financial(creator.Database, creator.Ledger).ToJsonString()
            );
            await creator.EndRunAsync(context.CancellationToken);
            AssertGenuineTerminal(creator);
            JsonObject history = Phase6State.History(creator.Database);
            JsonObject money = Phase6State.Financial(creator.Database, creator.Ledger);
            JsonObject live = accounting.Reconcile(creator.TargetId);
            Assert.AreEqual("credit_unknown", live["code"]!.GetValue<string>());
            Assert.AreEqual(
                "An exact original owner/runtime identity is still alive.",
                live["message"]!.GetValue<string>()
            );
            Assert.AreEqual(
                history.ToJsonString(),
                Phase6State.History(creator.Database).ToJsonString()
            );
            Assert.AreEqual(
                money.ToJsonString(),
                Phase6State.Financial(creator.Database, creator.Ledger).ToJsonString()
            );
            await creator.ExitAsync(context.CancellationToken);
            AssertCancellationAndRepeat(creator, accounting, null, "controlled-native-dependency");
            // Controlled local cancellation is legitimate; native PAID admission
            // must still reject the unchanged non-native receipt namespace.
            CollaborationOperations collaboration = new(creator.Database);
            Coordinator native = new(
                creator.Database,
                collaboration,
                new(creator.Database, collaboration)
            );
            JsonObject settled = Phase6State.Financial(creator.Database, creator.Ledger);
            JsonObject rejected = await native.CalibrateAsync(context.CancellationToken);
            Assert.AreEqual("unavailable", rejected["code"]!.GetValue<string>());
            Assert.Contains("preflight", rejected["message"]!.GetValue<string>());
            Assert.AreEqual(
                settled.ToJsonString(),
                Phase6State.Financial(creator.Database, creator.Ledger).ToJsonString()
            );
            Assert.IsNull(creator.Database.ReadMetadata("native:sdk_billing_receipt"));
        });
    }

    [TestMethod]
    [Timeout(120_000)]
    public async Task ReconcileIdempotenceAndConservation()
    {
        Phase6OwnedCreator creator = await Phase6OwnedCreator.OpenAsync(context.CancellationToken);
        await creator.RunBodyAsync(async () =>
        {
            await creator.EndRunAsync(context.CancellationToken);
            AssertGenuineTerminal(creator);
            await creator.ExitAsync(context.CancellationToken);
            Coordinator accounting = creator.AccountingOnly();
            string originalBinding = creator.Database.ReadMetadata("native:ledger_binding")!;
            // Deliberate unsupported binding INPUT, not repaired setup/provenance.
            creator.Database.WriteMetadata("native:ledger_binding", originalBinding + ".foreign");
            JsonObject history = Phase6State.History(creator.Database);
            JsonObject money = Phase6State.Financial(creator.Database, creator.Ledger);
            JsonObject rejected = accounting.Reconcile(creator.TargetId);
            Assert.AreEqual("credit_unknown", rejected["code"]!.GetValue<string>());
            Assert.AreEqual(
                "The retained campaign binding changed.",
                rejected["message"]!.GetValue<string>()
            );
            Assert.AreEqual(
                history.ToJsonString(),
                Phase6State.History(creator.Database).ToJsonString()
            );
            Assert.AreEqual(
                money.ToJsonString(),
                Phase6State.Financial(creator.Database, creator.Ledger).ToJsonString()
            );
            creator.Database.WriteMetadata("native:ledger_binding", originalBinding);
            AssertCancellationAndRepeat(creator, accounting, null, "controlled-native-dependency");
        });
    }

    [TestMethod(DisplayName = "SYNTHETIC CONTRACT ONLY: legacy compatibility guard inputs")]
    [Timeout(120_000)]
    public async Task LegacyCompatibilityContractGuards()
    {
        Phase6OwnedCreator first = await Phase6OwnedCreator.OpenAsync(context.CancellationToken);
        await first.RunBodyAsync(async () =>
        {
            await first.EndRunAsync(context.CancellationToken);
            AssertGenuineTerminal(first);
            await first.ExitAsync(context.CancellationToken);
            Phase6LegacyGuardInput input = await Phase6LegacyGuardInput.CreateAsync(
                first,
                context.CancellationToken
            );
            AssertCancellationAndRepeat(
                first,
                first.AccountingOnly(),
                input.EvidencePath,
                "native-sdk"
            );
        });
        Phase6OwnedCreator second = await Phase6OwnedCreator.OpenAsync(context.CancellationToken);
        await second.RunBodyAsync(async () =>
        {
            await second.EndRunAsync(context.CancellationToken);
            AssertGenuineTerminal(second);
            await second.ExitAsync(context.CancellationToken);
            Phase6LegacyGuardInput input = await Phase6LegacyGuardInput.CreateAsync(
                second,
                context.CancellationToken
            );
            Coordinator accounting = second.AccountingOnly();
            JsonObject history = Phase6State.History(second.Database);
            JsonObject money = Phase6State.Financial(second.Database, second.Ledger);
            foreach (
                (string defect, string message) in new[]
                {
                    ("C", "Legacy proof-path source compatibility is unestablished."),
                    ("H", "A referenced owned observation differs from the retained evidence."),
                    (
                        "A",
                        (
                            "The referenced native observations contradict the live retained "
                            + "associations."
                        )
                    ),
                }
            )
            {
                input.Damage(defect);
                JsonObject result = accounting.Reconcile(second.TargetId, input.EvidencePath);
                Assert.IsFalse(result["ok"]!.GetValue<bool>());
                Assert.AreEqual("credit_unknown", result["code"]!.GetValue<string>());
                Assert.AreEqual(message, result["message"]!.GetValue<string>());
                Assert.IsNull(result["data"]);
                Assert.AreEqual(
                    history.ToJsonString(),
                    Phase6State.History(second.Database).ToJsonString()
                );
                Assert.AreEqual(
                    money.ToJsonString(),
                    Phase6State.Financial(second.Database, second.Ledger).ToJsonString()
                );
                Assert.AreEqual("uncertain", second.Target.State);
                Assert.IsNull(second.Target.Actual);
                input.Restore(defect);
            }
            AssertCancellationAndRepeat(second, accounting, input.EvidencePath, "native-sdk");
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
    public async Task CoreCancellationAndAdmissionConserveOneAuthorization()
    {
        using ScratchDirectory scratch = new();
        string path = scratch.PathFor("core.sqlite");
        CreditLedger ledger = new(path, 200);
        (CreditReservation row, JsonObject proof) = CoreOriginal(ledger, scratch.PathFor("raw"));
        TaskCompletionSource go = new(TaskCreationOptions.RunContinuationsAsynchronously);
        TaskCompletionSource[] ready =
        [
            new(TaskCreationOptions.RunContinuationsAsynchronously),
            new(TaskCreationOptions.RunContinuationsAsynchronously),
        ];
        async Task<bool> CompeteAsync(int index)
        {
            ready[index].TrySetResult();
            await go.Task.WaitAsync(context.CancellationToken);
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
        Task<bool> cancellation = Task.Run(() => CompeteAsync(0), context.CancellationToken);
        Task<bool> admission = Task.Run(() => CompeteAsync(1), context.CancellationToken);
        Exception? readinessError = null;
        try
        {
            await Task.WhenAll(ready.Select(item => item.Task))
                .WaitAsync(TimeSpan.FromSeconds(5), context.CancellationToken);
        }
        catch (Exception error)
        {
            readinessError = error;
        }
        finally
        {
            go.TrySetResult();
        }
        bool[] outcomes;
        try
        {
            outcomes = await Task.WhenAll(cancellation, admission);
        }
        catch (Exception error) when (readinessError is not null)
        {
            throw new AggregateException(
                "Readiness failed; both owned contenders finished.",
                readinessError,
                error
            );
        }
        if (readinessError is not null)
        {
            System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(readinessError).Throw();
        }
        Assert.AreEqual(1, outcomes.Count(value => value));
        CreditReservation actual = ledger.Reservations().Single();
        JsonObject status = ledger.Status(row.RunId, 200);
        Assert.AreEqual(outcomes[0] ? "released" : "active", actual.State);
        Assert.AreEqual(outcomes[0] ? 0m : 100m, status["campaignCommitted"]!.GetValue<decimal>());
        Assert.IsNull(actual.Actual);
        using SqliteConnection connection = Phase6State.OpenFile(path);
        Assert.AreEqual(
            outcomes[0] ? 0L : 1L,
            Sql.Scalar(
                connection,
                "SELECT dispatch_count FROM credit_sdk_usage WHERE reservation_id=$id;",
                null,
                ("$id", row.Id)
            )
        );
        Assert.IsEmpty(Phase6State.Rows(connection, "SELECT * FROM credit_reports;"));
        Console.WriteLine(
            "PHASE6_CORE_LINEARIZATION "
                + string.Join(",", outcomes)
                + "; no physical-overlap or forced-winner claim"
        );
    }

    private static void AssertGenuineTerminal(Phase6OwnedCreator creator)
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
            Phase6State.Number(
                database,
                "SELECT COUNT(*) FROM coordinator_segments WHERE ended_at IS NOT NULL;"
            )
        );
        Assert.AreEqual(
            0L,
            Phase6State.Number(database, "SELECT COUNT(*) FROM coordinator_turns;")
        );
        Assert.AreEqual("stopped", database.ReadMetadata("worker:worker1:lifecycle"));
        Assert.AreEqual(
            1L,
            Phase6State.Number(
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
            Phase6State.Number(
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
            "phase6-target-release-blocked",
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
        using SqliteConnection campaign = Phase6State.OpenCampaign(database);
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
            Phase6State.Rows(
                campaign,
                "SELECT * FROM credit_reports WHERE reservation_id=$id;",
                ("$id", row.Id)
            )
        );
    }

    private static void AssertCancellationAndRepeat(
        Phase6OwnedCreator creator,
        Coordinator accounting,
        string? evidence,
        string kind
    )
    {
        JsonObject history = Phase6State.History(creator.Database);
        JsonObject before = Phase6State.Financial(creator.Database, creator.Ledger);
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
            Phase6State.History(creator.Database).ToJsonString()
        );
        JsonObject after = Phase6State.Financial(creator.Database, creator.Ledger);
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
            Phase6State.Financial(creator.Database, creator.Ledger).ToJsonString()
        );
        Assert.AreEqual(
            history.ToJsonString(),
            Phase6State.History(creator.Database).ToJsonString()
        );
        Console.WriteLine(
            "PHASE6_CANCELLATION "
                + new JsonObject
                {
                    ["outerEvidence"] = evidence is null
                        ? "genuine-current-controlled-public"
                        : "SYNTHETIC CONTRACT ONLY; literal native-sdk IS NOT native proof",
                    ["first"] = first,
                    ["repeat"] = repeat.DeepClone(),
                    ["before"] = before,
                    ["after"] = after,
                }.ToJsonString()
        );
    }

    private static (CreditReservation Row, JsonObject Proof) CoreOriginal(
        CreditLedger ledger,
        string usagePath
    )
    {
        CreditReservation reserved = ledger.Reserve("phase6-core", "worker1", 100, 200, usagePath);
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
        using SqliteConnection connection = Phase6State.OpenFile(path);
        return new()
        {
            ["status"] = ledger.Status("phase6-core", 200),
            ["reservations"] = Phase6State.Rows(connection, "SELECT * FROM credit_reservations;"),
            ["usage"] = Phase6State.Rows(connection, "SELECT * FROM credit_sdk_usage;"),
            ["owners"] = Phase6State.Rows(connection, "SELECT * FROM credit_sdk_owners;"),
            ["reports"] = Phase6State.Rows(connection, "SELECT * FROM credit_reports;"),
        };
    }
}
