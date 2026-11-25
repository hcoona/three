using System.Globalization;
using System.Text.Json.Nodes;
using LiteAgensh.Tests.Support;
using Microsoft.Data.Sqlite;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace LiteAgensh.Tests;

[TestClass]
public sealed class DispatchCancellationSpecs(TestContext context)
{
    public static IEnumerable<(double? nano, string? status, long requests)> RejectedReadings =>
        [
            (1d, "complete", 0),
            (null, null, 0),
            (double.NaN, "complete", 0),
            (double.PositiveInfinity, "complete", 0),
            (double.NegativeInfinity, "complete", 0),
            (0d, null, 1),
            (0d, "partial", 0),
            (0d, "unavailable", 0),
            (0d, null, -1),
        ];

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task
    NeverDispatchedAfterHostSettlementOwnedRuntimeReturnsBackingOnceWithoutMeasurement(
        bool completeStatus
    )
    {
        await using DispatchSessionScope scope = new(context, holdHost: true);
        DispatchSessionControl host = scope.Host;
        await host.StartOwnedRuntimeAsync(context.CancellationToken);
        Assert.IsNotNull(host.ReadyIdentity);
        Assert.IsGreaterThan(0L, host.LeaseId);
        CreditReservation own = host.Reserve("worker1");
        CreditReservation peer = host.Reserve("worker2");
        INativeWorker one = host.Factory.Create(own, calibration: false);
        INativeWorker two = host.Factory.Create(peer, calibration: false);
        await one.StartAsync(host.State.Cwd("worker1"), null, context.CancellationToken);
        await two.StartAsync(host.State.Cwd("worker2"), null, context.CancellationToken);
        host.Sessions[0].Publish(0, completeStatus ? "complete" : null, 0);
        await two.PromptAsync("The measured peer is not cancelled.", context.CancellationToken);
        JsonObject initial = JsonNode
            .Parse(host.State.Database.ReadMetadata(NoDispatchCancellation.Marker(own.Id))!)!
            .AsObject();
        Assert.AreEqual(host.LeaseId, initial["initial"]!["runtimeLeaseId"]!.GetValue<long>());
        Assert.AreEqual(host.Id, initial["initial"]!["runtimeId"]!.GetValue<string>());
        Assert.AreEqual(
            own.Worker,
            host.State.RunValue(
                "SELECT worker FROM native_sessions WHERE reservation_id=$id;",
                own.Id
            )
        );
        Assert.AreEqual(
            own.UsagePath,
            host.State.RunValue(
                "SELECT usage_path FROM native_attempts WHERE reservation_id=$id;",
                own.Id
            )
        );

        await scope.EnterHeldHostAsync();
        Assert.IsTrue(host.Sessions.All(session => session.Closed));
        Assert.AreEqual("stopped", WorkerLifecycle(host, "worker1"));
        Assert.AreEqual("active", host.State.Read(own).State);
        Assert.IsTrue(host.State.Read(own).GrantOpen);
        Assert.IsNull(host.State.Read(own).Actual);
        Assert.AreEqual(7.25m, host.State.Read(peer).Actual);
        Assert.IsTrue(host.State.Read(peer).GrantOpen);
        Assert.AreEqual(
            192.75m,
            host.State.Accounting["outstandingAuthorization"]!.GetValue<decimal>()
        );
        Assert.AreEqual("running", LeaseState(host));

        await scope.AwaitHeldHostAsync();

        Assert.AreEqual("exited", LeaseState(host));
        JsonObject runtime = JsonNode
            .Parse(host.State.Database.ReadMetadata($"native:runtime:{host.Id}")!)!
            .AsObject();
        Assert.AreEqual("settled", runtime["state"]!.GetValue<string>());
        Assert.AreEqual(
            "controlled-native-dependency",
            runtime["evidenceKind"]!.GetValue<string>()
        );
        Assert.AreEqual("released", host.State.Read(own).State);
        Assert.IsFalse(host.State.Read(own).GrantOpen);
        Assert.IsNull(host.State.Read(own).Actual);
        Assert.AreEqual("reconciled", host.State.Read(peer).State);
        Assert.AreEqual(7.25m, host.State.Read(peer).Actual);
        Assert.AreEqual(7.25m, host.State.Accounting["runCommitted"]!.GetValue<decimal>());
        Assert.AreEqual(
            0m,
            host.State.Accounting["outstandingAuthorization"]!.GetValue<decimal>()
        );
        JsonObject repeated = NoDispatchCancellation.Cancel(
            host.State.Database,
            host.State.Ledger,
            own.Id,
            retained: false
        );
        Assert.IsTrue(repeated["idempotent"]!.GetValue<bool>());
        Assert.IsNull(repeated["measuredActual"]);
        Assert.AreEqual("0", repeated["reportedNanoAiu"]!.GetValue<string>());
        Assert.AreEqual(
            completeStatus ? "complete" : null,
            repeated["aiCreditsStatus"]?.GetValue<string>()
        );
        Assert.HasCount(1, host.State.Accounting["cancellations"]!.AsArray());
        Assert.IsFalse(host.State.Ledger.HasCompleteSdkUsage(own.Id));
        Assert.IsTrue(host.State.Ledger.HasCompleteSdkUsage(peer.Id));
        Assert.AreEqual(0L, ReportCount(host, own.Id));
        Assert.AreEqual(1L, ReportCount(host, peer.Id));
        Assert.AreEqual(0L, ReportedUsage(host, own)["dispatches"]!.GetValue<long>());
        Assert.AreEqual(1L, ReportedUsage(host, peer)["dispatches"]!.GetValue<long>());
        scope.Complete();
    }

    [TestMethod]
    public async Task NeverDispatchedAfterHostSettlementUnstartedReservationNeedsNoRuntimeOrUsage()
    {
        await using DispatchSessionScope scope = new(context);
        DispatchSessionControl host = scope.Host;
        CreditReservation own = host.Reserve("worker1");
        _ = host.Factory.Create(own, calibration: false);

        await host.Factory.CompleteAsync();

        Assert.AreEqual("released", host.State.Read(own).State);
        Assert.IsNull(host.State.Read(own).Actual);
        Assert.IsFalse(host.State.Read(own).GrantOpen);
        Assert.IsEmpty(host.Sessions);
        Assert.IsFalse(host.State.Ledger.HasCompleteSdkUsage(own.Id));
        Assert.AreEqual(0L, ReportCount(host, own.Id));
        Assert.AreEqual(0m, host.State.Accounting["actualReported"]!.GetValue<decimal>());
        Assert.AreEqual(0L, ReportedUsage(host, own)["dispatches"]!.GetValue<long>());
        Assert.IsEmpty(host.State.Accounting["cancellations"]!.AsArray());
        scope.Complete();
    }

    [TestMethod]
    public async Task
    NeverDispatchedAfterHostSettlementUnopenedExactLoadKeepsSettledCompleteBaseline()
    {
        await using DispatchSessionScope scope = new(context);
        DispatchSessionControl original = scope.Host;
        CreditReservation own = original.Reserve("worker1", shared: false);
        INativeWorker worker = original.Factory.Create(own, calibration: false);
        await worker.StartAsync(original.State.Cwd("worker1"), null, context.CancellationToken);
        await worker.PromptAsync(
            "Establish the old measured baseline.",
            context.CancellationToken
        );
        await original.Factory.CompleteAsync();
        Assert.AreEqual("reconciled", original.State.Read(own).State);
        CreditReservation resumed = original.State.Ledger.ContinueExact(
            own.Id,
            original.State.Cwd("resume.usage")
        );
        // A new dependency host represents the next operation; the old factory is settled.
        DispatchSessionControl next = new(original.State) { FailExactOpen = true };
        INativeWorker loading = next.Factory.Create(resumed, calibration: false);

        OperationException error = await Assert.ThrowsExactlyAsync<OperationException>(
            async () =>
                await loading.StartAsync(
                    original.State.Cwd("worker1"),
                    own.SessionId,
                    context.CancellationToken
                )
        );

        Assert.AreEqual("session_load_failed", error.Code);
        DispatchSessionControl.Session failedSession = next.Sessions[^1];
        Assert.IsFalse(failedSession.HasSession);
        await loading.StopAsync();
        Assert.AreEqual("reconciled", original.State.Read(own).State);
        Assert.AreEqual(7.25m, original.State.Read(own).Actual);
        Assert.IsTrue(original.State.Ledger.HasCompleteSdkUsage(own.Id));
        Assert.AreEqual(7.25m, original.State.Accounting["actualReported"]!.GetValue<decimal>());
        Assert.AreEqual(1L, ReportedUsage(original, own)["dispatches"]!.GetValue<long>());
        Assert.IsEmpty(original.State.Accounting["cancellations"]!.AsArray());
        scope.Complete();
    }

    [TestMethod]
    [DynamicData(nameof(RejectedReadings))]
    public async Task RejectAmbiguousOrPositiveCancellationRawReadingCannotReleaseBacking(
        double? nano,
        string? status,
        long requests
    )
    {
        await using DispatchSessionScope scope = new(context);
        DispatchSessionControl host = scope.Host;
        CreditReservation own = host.Reserve("worker1");
        CreditReservation peer = host.Reserve("worker2");
        INativeWorker one = host.Factory.Create(own, calibration: false);
        INativeWorker two = host.Factory.Create(peer, calibration: false);
        await one.StartAsync(host.State.Cwd("worker1"), null, context.CancellationToken);
        await two.StartAsync(host.State.Cwd("worker2"), null, context.CancellationToken);
        await two.PromptAsync("Keep the measured peer.", context.CancellationToken);
        host.Sessions[0].Publish(nano, status, requests);

        OperationException error = await Assert.ThrowsExactlyAsync<OperationException>(
            host.Factory.CompleteAsync
        );

        Assert.AreEqual("credit_or_ownership_unknown", error.Code);
        bool nonzero = (nano is double total && total != 0) || requests != 0;
        string phase = nonzero ? "stop" : "final-usage";
        string reason = nonzero
            ? "Native usage appeared before this fresh session's first paid dispatch."
            : nano is null
                ? "Native credit usage is absent, invalid or incomplete; reservation retained."
                : "Missing, positive or incomplete native usage cannot prove cancellation.";
        JsonObject detail = Assert.ContainsSingle(
            host.Factory.SettlementDetails.OfType<JsonObject>()
                .Where(item => item["reservationId"]?.GetValue<string>() == own.Id
                    && item["worker"]?.GetValue<string>() == "worker1"
                    && item["phase"]?.GetValue<string>() == phase)
        );
        JsonObject cause = detail["error"]!.AsObject();
        if (nonzero)
        {
            cause = cause["innerException"]!.AsObject();
        }
        Assert.AreEqual("credit_unknown", cause["operationCode"]!.GetValue<string>());
        Assert.AreEqual(reason, cause["message"]!.GetValue<string>());
        Assert.AreEqual("uncertain", host.State.Read(own).State);
        Assert.IsTrue(host.State.Read(own).GrantOpen);
        Assert.IsNull(host.State.Read(own).Actual);
        Assert.IsFalse(host.State.Ledger.HasCompleteSdkUsage(own.Id));
        Assert.AreEqual(0L, ReportedUsage(host, own)["dispatches"]!.GetValue<long>());
        Assert.IsEmpty(host.State.Accounting["cancellations"]!.AsArray());
        Assert.AreEqual("reconciled", host.State.Read(peer).State);
        Assert.AreEqual(7.25m, host.State.Read(peer).Actual);
        Assert.AreEqual(7.25m, host.State.Accounting["actualReported"]!.GetValue<decimal>());
        Assert.AreEqual(
            own.SessionId,
            host.State.RunValue(
                "SELECT session_id FROM native_sessions WHERE reservation_id=$id;",
                own.Id
            )
        );
        Assert.IsNull(host.Sessions[0].AcceptedText);
        scope.Complete();
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task RejectAmbiguousOrPositiveCancellationDispatchedOrCancelledSendIsNotFresh(
        bool racing
    )
    {
        await using DispatchSessionScope scope = new(context);
        DispatchSessionControl host = scope.Host;
        CreditReservation own = host.Reserve("worker1");
        CreditReservation peer = host.Reserve("worker2");
        INativeWorker one = host.Factory.Create(own, calibration: false);
        INativeWorker two = host.Factory.Create(peer, calibration: false);
        await one.StartAsync(host.State.Cwd("worker1"), null, context.CancellationToken);
        await two.StartAsync(host.State.Cwd("worker2"), null, context.CancellationToken);
        await two.PromptAsync(
            "Preserve measured peer during cancellation.",
            context.CancellationToken
        );
        host.Sessions[0].HoldSend = racing;
        Task entered = host.Sessions[0].SendEntered.Task;
        Task<JsonObject> send = scope.TrackPrompt(
            one.PromptAsync("An actual paid intent.", scope.Token)
        );
        if (
            await Task.WhenAny(entered, send)
                .WaitAsync(TimeSpan.FromSeconds(5), context.CancellationToken) == send
        )
        {
            await send;
            Assert.Fail("The prompt returned before entering its controlled send.");
        }
        await entered;

        if (racing)
        {
            await scope.CancelAsync();
            OperationCanceledException original =
                await Assert.ThrowsAsync<OperationCanceledException>(() =>
                    send.WaitAsync(TimeSpan.FromSeconds(5), CancellationToken.None)
                );
            scope.MarkPromptObserved();
            Assert.IsTrue(original.CancellationToken.IsCancellationRequested);
            OperationException settlement = await Assert.ThrowsExactlyAsync<OperationException>(
                host.Factory.CompleteAsync
            );
            Assert.Contains("Native send has no delivered terminal boundary", settlement.Message);
            Assert.AreEqual("uncertain", host.State.Read(own).State);
            Assert.IsTrue(host.State.Read(own).GrantOpen);
            Assert.IsNull(host.State.Read(own).Actual);
        }
        else
        {
            await send;
            scope.MarkPromptObserved();
            await host.Factory.CompleteAsync();
            Assert.AreEqual("reconciled", host.State.Read(own).State);
            Assert.AreEqual(7.25m, host.State.Read(own).Actual);
            OperationException rejection = Assert.ThrowsExactly<OperationException>(() =>
                NoDispatchCancellation.Cancel(
                    host.State.Database,
                    host.State.Ledger,
                    own.Id,
                    retained: false
                )
            );
            Assert.AreEqual("credit_unknown", rejection.Code);
            Assert.Contains("unmeasured fresh session", rejection.Message);
        }

        Assert.IsEmpty(host.State.Accounting["cancellations"]!.AsArray());
        Assert.AreEqual(7.25m, host.State.Read(peer).Actual);
        Assert.AreEqual("reconciled", host.State.Read(peer).State);
        Assert.AreEqual(1L, ReportedUsage(host, own)["dispatches"]!.GetValue<long>());
        scope.Complete();
    }

    [TestMethod]
    [DataRow("missing-association")]
    [DataRow("reused-session")]
    [DataRow("missing-lineage")]
    [DataRow("unsettled-ownership")]
    public async Task
    RejectAmbiguousOrPositiveCancellationApplicationGuardRejectsContradictoryBinding(
        string scenario
    )
    {
        await using DispatchSessionScope scope = new(context, holdHost: true);
        DispatchSessionControl host = scope.Host;
        await host.StartOwnedRuntimeAsync(context.CancellationToken);
        CreditReservation own = host.Reserve("worker1");
        CreditReservation peer = host.Reserve("worker2");
        INativeWorker one = host.Factory.Create(own, calibration: false);
        INativeWorker two = host.Factory.Create(peer, calibration: false);
        await one.StartAsync(host.State.Cwd("worker1"), null, context.CancellationToken);
        await two.StartAsync(host.State.Cwd("worker2"), null, context.CancellationToken);
        await two.PromptAsync("Binding rejection must preserve me.", context.CancellationToken);
        await scope.EnterHeldHostAsync();

        using (SqliteConnection connection = host.State.Database.Open())
        {
            switch (scenario)
            {
                case "missing-association":
                    Sql.Execute(
                        connection,
                        "DELETE FROM native_attempts WHERE reservation_id=$id;",
                        null,
                        ("$id", own.Id)
                    );
                    break;
                case "reused-session":
                    Sql.Execute(
                        connection,
                        """
                        INSERT INTO native_sessions(
                            reservation_id,worker,session_id,recovery,initialize_json,
                            created_at)
                        SELECT $other,worker,session_id,recovery,initialize_json,created_at
                        FROM native_sessions WHERE reservation_id=$id;
                        """,
                        null,
                        ("$id", own.Id),
                        ("$other", peer.Id)
                    );
                    break;
                case "missing-lineage":
                    Sql.Execute(
                        connection,
                        "DELETE FROM meta WHERE key=$key;",
                        null,
                        ("$key", NoDispatchCancellation.Marker(own.Id))
                    );
                    break;
                case "unsettled-ownership":
                    host.HostFailure = new(
                        "ownership_unresolved",
                        "Controlled owned-runtime stop failed."
                    );
                    break;
                default:
                    throw new ArgumentOutOfRangeException(nameof(scenario));
            }
        }

        OperationException error = await Assert.ThrowsExactlyAsync<OperationException>(
            scope.AwaitHeldHostAsync
        );

        Assert.AreEqual("credit_or_ownership_unknown", error.Code);
        Assert.Contains(
            scenario switch
            {
                "missing-lineage" =>
                    "Legacy cancellation requires explicit compatible retained lineage "
                        + "evidence.",
                "unsettled-ownership" => "Controlled owned-runtime stop failed.",
                "missing-association" => "A required native association is missing.",
                "reused-session" =>
                    "Fresh native associations are missing, reused or contradictory.",
                _ => throw new ArgumentOutOfRangeException(nameof(scenario)),
            },
            error.Message
        );
        Assert.AreEqual("uncertain", host.State.Read(own).State);
        Assert.IsTrue(host.State.Read(own).GrantOpen);
        Assert.IsNull(host.State.Read(own).Actual);
        Assert.IsFalse(host.State.Ledger.HasCompleteSdkUsage(own.Id));
        Assert.AreEqual(0L, ReportedUsage(host, own)["dispatches"]!.GetValue<long>());
        Assert.AreEqual(7.25m, host.State.Read(peer).Actual);
        Assert.AreEqual(
            scenario == "unsettled-ownership" ? "uncertain" : "reconciled",
            host.State.Read(peer).State
        );
        Assert.IsEmpty(host.State.Accounting["cancellations"]!.AsArray());
        scope.Complete();
    }

    [TestMethod]
    [DataRow("runtime-binding")]
    [DataRow("lease-binding")]
    [DataRow("cwd-binding")]
    [DataRow("missing-durable-requests")]
    [DataRow("fresh-stop")]
    public async Task RejectAmbiguousOrPositiveCancellationOwnedFreshGuardRetainsBacking(
        string scenario
    )
    {
        await using DispatchSessionScope scope = new(
            context,
            holdHost: true,
            reportRootCleanup: true
        );
        DispatchSessionControl host = scope.Host;
        if (scenario == "fresh-stop")
        {
            scope.CaptureSettlement = ReportFactorySettlement;
        }
        string scratchRoot = host.State.Cwd(string.Empty);
        OperationException original = new(
            "controlled_fresh_stop",
            "Controlled fresh zero stop failed."
        );
        await host.StartOwnedRuntimeAsync(context.CancellationToken);
        Assert.IsNotNull(host.ReadyIdentity);
        Assert.IsGreaterThan(0L, host.LeaseId);
        CreditReservation own = host.Reserve("worker1");
        CreditReservation peer = host.Reserve("worker2");
        INativeWorker one = host.Factory.Create(own, calibration: false);
        INativeWorker two = host.Factory.Create(peer, calibration: false);
        string cwd = host.State.Cwd("worker1");
        await one.StartAsync(cwd, null, context.CancellationToken);
        await two.StartAsync(host.State.Cwd("worker2"), null, context.CancellationToken);
        await two.PromptAsync(
            "Retain measured peer beside fresh rejection.",
            context.CancellationToken
        );
        Assert.AreEqual(0d, host.Sessions[0].Current.TotalNanoAiu);
        Assert.IsNull(host.Sessions[0].Current.Status);
        Assert.AreEqual(0L, host.Sessions[0].Current.TotalUserRequests);
        if (scenario == "fresh-stop")
        {
            host.Sessions[0].StopFailure = original;
            context.WriteLine(
                "Phase4FreshRejectionFactoryInvoking: {0:O}; runtime={1}",
                DateTimeOffset.UtcNow,
                host.Id
            );
        }
        await scope.EnterHeldHostAsync();
        Assert.IsTrue(host.Sessions.All(session => session.Closed));
        Assert.AreEqual("running", LeaseState(host));
        Assert.AreEqual(
            (long)host.ReadyIdentity.Pid,
            host.State.RunValue(
                "SELECT pid FROM process_leases WHERE kind=$id;",
                "native:runtime:" + host.Id
            )
        );
        Assert.AreEqual(
            host.ReadyIdentity.BirthTicks,
            host.State.RunValue(
                "SELECT start_ticks FROM process_leases WHERE kind=$id;",
                "native:runtime:" + host.Id
            )
        );
        Assert.IsTrue(host.State.Read(own).GrantOpen);
        Assert.IsNull(host.State.Read(own).Actual);
        Assert.AreEqual(100m, host.State.Read(own).Allowance);
        Assert.AreEqual(7.25m, host.State.Read(peer).Actual);
        Assert.IsEmpty(host.State.Accounting["cancellations"]!.AsArray());
        string key = $"native:runtime:{host.Id}:session:{own.SessionId}";
        JsonObject binding = JsonNode
            .Parse(host.State.Database.ReadMetadata(key)!)!
            .AsObject();
        Assert.AreEqual(host.Id, binding["runtimeId"]!.GetValue<string>());
        Assert.AreEqual(host.LeaseId, binding["runtimeLeaseId"]!.GetValue<long>());
        Assert.AreEqual(own.SessionId, binding["sessionId"]!.GetValue<string>());
        Assert.AreEqual(own.Worker, binding["worker"]!.GetValue<string>());
        Assert.AreEqual(cwd, binding["workingDirectory"]!.GetValue<string>());
        JsonObject diagnostic = JsonNode
            .Parse(host.State.Database.ReadMetadata($"native:session:{own.SessionId}")!)!
            .AsObject();
        Assert.AreEqual(own.SessionId, diagnostic["sessionId"]!.GetValue<string>());
        Assert.AreEqual(host.Id, diagnostic["runtimeId"]!.GetValue<string>());
        Assert.AreEqual(host.LeaseId, diagnostic["runtimeLeaseId"]!.GetValue<long>());
        Assert.AreEqual(cwd, diagnostic["workingDirectory"]!.GetValue<string>());
        Assert.AreEqual("0", diagnostic["reportedNanoAiu"]!.GetValue<string>());
        Assert.IsNull(diagnostic["aiCreditsStatus"]);
        Assert.AreEqual(0L, diagnostic["totalUserRequests"]!.GetValue<long>());
        Assert.AreEqual(0L, ReportedUsage(host, own)["dispatches"]!.GetValue<long>());
        Assert.AreEqual(0L, ReadReportedRequests(host, own.Id));
        context.WriteLine(
            $"Phase4FreshRejectionBaseline: {new JsonObject
            {
                ["scenario"] = scenario,
                ["runId"] = host.State.Database.RunId,
                ["reservationId"] = own.Id,
                ["pid"] = host.ReadyIdentity.Pid,
                ["birthTicks"] = host.ReadyIdentity.BirthTicks,
                ["binding"] = binding.DeepClone(),
                ["diagnostic"] = diagnostic,
                ["scratchRoot"] = scratchRoot,
            }.ToJsonString()}"
        );
        if (scenario != "fresh-stop")
        {
            Assert.AreEqual("stopped", WorkerLifecycle(host, "worker1"));
        }
        switch (scenario)
        {
            case "runtime-binding":
                binding["runtimeId"] = "contradictory-serialized-runtime";
                break;
            case "lease-binding":
                // A negative absent-lease binding, not a fabricated positive lease.
                binding["runtimeLeaseId"] = 0L;
                break;
            case "cwd-binding":
                binding["workingDirectory"] = host.State.Cwd("contradictory-cwd");
                break;
            case "missing-durable-requests":
                // SDK long remains zero. Only the application's durable observation is lost.
                ClearReportedRequests(host, own.Id);
                break;
            case "fresh-stop":
                Assert.AreSame(original, host.Sessions[0].FirstFailure);
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(scenario));
        }
        if (scenario is "runtime-binding" or "lease-binding" or "cwd-binding")
        {
            host.State.Database.WriteMetadata(key, binding.ToJsonString());
        }

        OperationException rejected = await Assert.ThrowsExactlyAsync<OperationException>(
            scope.AwaitHeldHostAsync
        );

        Assert.AreEqual("credit_or_ownership_unknown", rejected.Code);
        Assert.AreEqual(
            "Native cohort settlement retained unresolved evidence: "
                + (
                    scenario switch
                    {
                        "missing-durable-requests" =>
                            "Live SDK usage or ownership contradicts cancellation.",
                        "fresh-stop" => "Native settlement retained unresolved evidence: "
                            + original.Message,
                        _ => "Session/handler or common-runtime settlement is not established.",
                    }
                ),
            rejected.Message
        );
        Assert.AreEqual("exited", LeaseState(host));
        Assert.AreEqual("uncertain", host.State.Read(own).State);
        Assert.IsTrue(host.State.Read(own).GrantOpen);
        Assert.IsNull(host.State.Read(own).Actual);
        Assert.AreEqual(100m, host.State.Read(own).Allowance);
        Assert.AreEqual(
            100m,
            host.State.Accounting["outstandingAuthorization"]!.GetValue<decimal>()
        );
        Assert.IsEmpty(host.State.Accounting["cancellations"]!.AsArray());
        Assert.IsFalse(host.State.Ledger.HasCompleteSdkUsage(own.Id));
        Assert.IsTrue(host.State.Ledger.HasCompleteSdkUsage(peer.Id));
        JsonObject ownReported = ReportedUsage(host, own);
        Assert.AreEqual(0L, ownReported["dispatches"]!.GetValue<long>());
        Assert.AreEqual("0", ownReported["reportedNanoAiu"]!.GetValue<string>());
        Assert.IsNull(ownReported["status"]);
        Assert.AreEqual(
            scenario == "missing-durable-requests" ? (long?)null : 0L,
            ReadReportedRequests(host, own.Id)
        );
        Assert.IsNull(host.Sessions[0].AcceptedText);
        Assert.AreEqual("reconciled", host.State.Read(peer).State);
        Assert.AreEqual(7.25m, host.State.Read(peer).Actual);
        Assert.AreEqual(100m, host.State.Read(peer).Allowance);
        Assert.AreEqual(7.25m, host.State.Accounting["actualReported"]!.GetValue<decimal>());
        context.WriteLine(
            $"Phase4FreshRejectionOutcome: {scenario}: {rejected.Code}: {rejected.Message}"
        );
        await scope.CleanupAndAssertScratchRemovedAsync(scratchRoot);

        void ReportFactorySettlement(TaskStatus? status)
        {
            bool terminal = status.HasValue;
            context.WriteLine(
                "Phase4FreshRejectionFactorySettlement: {0}",
                new JsonObject
                {
                    ["runtimeId"] = host.Id,
                    ["observedAt"] = DateTimeOffset.UtcNow,
                    ["factoryStatus"] = status?.ToString(),
                    ["factoryTerminal"] = terminal,
                    ["settlementFailures"] = terminal
                        ? host.Factory.SettlementFailures.DeepClone() : null,
                    ["settlementDetails"] = terminal
                        ? host.Factory.SettlementDetails.DeepClone() : null,
                }.ToJsonString()
            );
            if (terminal)
            {
                context.WriteLine(
                    "Phase4FreshRejectionLedgerAfterFactorySettlement: {0}; {1}",
                    host.Id,
                    host.State.Accounting.ToJsonString()
                );
            }
        }
    }

    private static JsonObject ReportedUsage(
        DispatchSessionControl host,
        CreditReservation reservation
    ) =>
        host.State.Accounting["reported"]!
            .AsArray()
            .OfType<JsonObject>()
            .Single(row => row["reservationId"]!.GetValue<string>() == reservation.Id);

    private static string WorkerLifecycle(DispatchSessionControl host, string worker)
    {
        CollaborationOperations collaboration = new(host.State.Database);
        Coordinator coordinator = new(
            host.State.Database,
            collaboration,
            new(host.State.Database, collaboration),
            (_, _, _) => host.Factory
        );
        return GitScenarioState.Data(coordinator.Status())["workers"]!
            .AsArray()
            .OfType<JsonObject>()
            .Single(item => item["id"]!.GetValue<string>() == worker)[
            "lifecycle"
        ]!.GetValue<string>();
    }

    private static string LeaseState(DispatchSessionControl host) =>
        (string)
            host.State.RunValue(
                "SELECT state FROM process_leases WHERE id=$id;",
                host.LeaseId.ToString(CultureInfo.InvariantCulture)
            )!;

    private static long ReportCount(DispatchSessionControl host, string reservationId)
    {
        using SqliteConnection ledger = OpenCampaign(host);
        return (long)Sql.Scalar(
            ledger,
            "SELECT COUNT(*) FROM credit_reports WHERE reservation_id=$id;",
            null,
            ("$id", reservationId)
        )!;
    }

    private static long? ReadReportedRequests(DispatchSessionControl host, string reservationId)
    {
        using SqliteConnection ledger = OpenCampaign(host);
        return Sql.Scalar(
                ledger,
                "SELECT reported_requests FROM credit_sdk_usage WHERE reservation_id=$id;",
                null,
                ("$id", reservationId)
            ) as long?;
    }

    private static void ClearReportedRequests(DispatchSessionControl host, string reservationId)
    {
        using SqliteConnection ledger = OpenCampaign(host);
        Assert.AreEqual(
            1,
            Sql.Execute(
                ledger,
                "UPDATE credit_sdk_usage SET reported_requests=NULL WHERE reservation_id=$id;",
                null,
                ("$id", reservationId)
            )
        );
    }

    private static SqliteConnection OpenCampaign(DispatchSessionControl host)
    {
        SqliteConnection ledger = new(
            new SqliteConnectionStringBuilder
            {
                DataSource = host.State.Database.Configuration.CampaignLedgerPath,
                Pooling = false,
            }.ToString()
        );
        ledger.Open();
        return ledger;
    }

    private sealed class DispatchSessionScope(
        TestContext context,
        bool holdHost = false,
        bool reportRootCleanup = false
    ) : IAsyncDisposable
    {
        private readonly CancellationTokenSource cancellation =
            CancellationTokenSource.CreateLinkedTokenSource(context.CancellationToken);
        private Task? pendingPrompt;
        private bool promptObserved;
        private bool heldFactoryStarted;
        private bool completed;
        private bool disposed;

        internal DispatchSessionControl Host { get; } = new()
        {
            HoldHost = holdHost,
        };

        internal Action<TaskStatus?>? CaptureSettlement { get; set; }

        internal CancellationToken Token => cancellation.Token;

        internal Task CancelAsync() => cancellation.CancelAsync();

        internal Task<JsonObject> TrackPrompt(Task<JsonObject> prompt)
        {
            pendingPrompt = prompt;
            return prompt;
        }

        internal void MarkPromptObserved() => promptObserved = true;

        internal async Task EnterHeldHostAsync()
        {
            Host.BeginHeldFactoryCompletion();
            heldFactoryStarted = true;
            await Host.HostEntered.Task.WaitAsync(
                TimeSpan.FromSeconds(5),
                context.CancellationToken
            );
        }

        internal Task AwaitHeldHostAsync() =>
            Host.AwaitHeldFactoryAsync(context.CancellationToken);

        internal void Complete() => completed = true;

        internal async Task CleanupAndAssertScratchRemovedAsync(string scratchRoot)
        {
            completed = true;
            await DisposeCoreAsync();
            Assert.IsFalse(Directory.Exists(scratchRoot));
        }

        public async ValueTask DisposeAsync() => await DisposeCoreAsync();

        private async Task DisposeCoreAsync()
        {
            if (disposed)
            {
                return;
            }
            disposed = true;
            Exception? sentinel = completed
                ? null
                : new InvalidOperationException("Unwinding test failure.");
            try
            {
                if (heldFactoryStarted)
                {
                    await Host.CleanupHeldFactoryAsync(
                        sentinel,
                        message => context.WriteLine(message),
                        CaptureSettlement,
                        reportRootCleanup
                    );
                    return;
                }

                if (pendingPrompt is not null && !promptObserved)
                {
                    try
                    {
                        await cancellation.CancelAsync();
                        Host.Sessions.FirstOrDefault()?.SendRelease.TrySetCanceled(
                            cancellation.Token
                        );
                        await pendingPrompt.WaitAsync(
                            TimeSpan.FromSeconds(5),
                            CancellationToken.None
                        );
                    }
                    catch (Exception secondary) when (!completed)
                    {
                        context.WriteLine("Secondary prompt completion failure: {0}", secondary);
                    }
                }

                bool preserveState = pendingPrompt is { IsCompleted: false };
                if (preserveState)
                {
                    context.WriteLine(
                        "Retained state for pending prompt: {0}",
                        Host.State.Cwd(string.Empty)
                    );
                }

                try
                {
                    await Host.CleanupAsync(preserveState);
                }
                catch (Exception secondary) when (!completed)
                {
                    context.WriteLine("Secondary fixture cleanup failure: {0}", secondary);
                }
            }
            finally
            {
                cancellation.Dispose();
            }
        }
    }
}
