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
        Phase4SessionControl host = new() { HoldHost = true };
        Task? completion = null;
        Exception? primaryFailure = null;
        bool completionObserved = false;
        try
        {
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

            completion = host.Factory.CompleteAsync();
            try
            {
                await host.HostEntered.Task.WaitAsync(
                    TimeSpan.FromSeconds(5),
                    context.CancellationToken
                );
                Assert.IsTrue(host.Sessions.All(session => session.Closed));
                Assert.AreEqual(
                    "stopped",
                    host.State.Database.ReadMetadata("worker:worker1:lifecycle")
                );
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
            }
            finally
            {
                host.ReleaseHost();
            }
            await completion.WaitAsync(TimeSpan.FromSeconds(5), CancellationToken.None);
            completionObserved = true;

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
            Assert.AreEqual(0L, Reports(host, own));
            Assert.AreEqual(1L, Reports(host, peer));
        }
        catch (Exception exception)
        {
            primaryFailure = exception;
        }
        finally
        {
            host.ReleaseHost();
            try
            {
                if (completion is not null && !completionObserved)
                {
                    await completion.WaitAsync(TimeSpan.FromSeconds(5), CancellationToken.None);
                }
            }
            catch (Exception exception)
            {
                if (primaryFailure is null)
                {
                    primaryFailure = exception;
                }
                else
                {
                    context.WriteLine("Secondary factory completion failure: {0}", exception);
                }
            }
            finally
            {
                bool preserveState = completion is { IsCompleted: false };
                if (preserveState)
                {
                    context.WriteLine(
                        "Retained state for pending factory completion: {0}",
                        host.State.Cwd(string.Empty)
                    );
                }
                try
                {
                    await host.CleanupAsync(preserveState);
                }
                catch (Exception exception)
                {
                    if (primaryFailure is null)
                    {
                        primaryFailure = exception;
                    }
                    else
                    {
                        context.WriteLine("Secondary fixture cleanup failure: {0}", exception);
                    }
                }
            }
        }
        if (primaryFailure is not null)
        {
            System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(primaryFailure).Throw();
        }
    }

    [TestMethod]
    public async Task NeverDispatchedAfterHostSettlementUnstartedReservationNeedsNoRuntimeOrUsage()
    {
        Phase4SessionControl host = new();
        try
        {
            CreditReservation own = host.Reserve("worker1");
            _ = host.Factory.Create(own, calibration: false);
            await host.Factory.CompleteAsync();
            Assert.AreEqual("released", host.State.Read(own).State);
            Assert.IsNull(host.State.Read(own).Actual);
            Assert.IsFalse(host.State.Read(own).GrantOpen);
            Assert.IsEmpty(host.Sessions);
            Assert.AreEqual(0L, Reports(host, own));
            Assert.IsEmpty(host.State.Accounting["cancellations"]!.AsArray());
        }
        finally
        {
            await host.CleanupAsync();
        }
    }

    [TestMethod]
    public async Task
    NeverDispatchedAfterHostSettlementUnopenedExactLoadKeepsSettledCompleteBaseline()
    {
        Phase4SessionControl original = new();
        try
        {
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
            Phase4SessionControl next = new(original.State) { FailExactOpen = true };
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
            Phase4SessionControl.Session failedSession = next.Sessions[^1];
            Assert.IsFalse(failedSession.HasSession);
            await loading.StopAsync();
            Assert.AreEqual("reconciled", original.State.Read(own).State);
            Assert.AreEqual(7.25m, original.State.Read(own).Actual);
            Assert.AreEqual(1L, Reports(original, own));
            Assert.IsEmpty(original.State.Accounting["cancellations"]!.AsArray());
        }
        finally
        {
            await original.CleanupAsync();
        }
    }

    [TestMethod]
    [DynamicData(nameof(RejectedReadings))]
    public async Task RejectAmbiguousOrPositiveCancellationRawReadingCannotReleaseBacking(
        double? nano,
        string? status,
        long requests
    )
    {
        Phase4SessionControl host = new();
        try
        {
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
            Assert.AreEqual(0L, Reports(host, own));
            Assert.IsEmpty(host.State.Accounting["cancellations"]!.AsArray());
            Assert.AreEqual("reconciled", host.State.Read(peer).State);
            Assert.AreEqual(7.25m, host.State.Read(peer).Actual);
            Assert.AreEqual(
                own.SessionId,
                host.State.RunValue(
                    "SELECT session_id FROM native_sessions WHERE reservation_id=$id;",
                    own.Id
                )
            );
            Assert.IsNull(host.Sessions[0].AcceptedText);
        }
        finally
        {
            await host.CleanupAsync();
        }
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task RejectAmbiguousOrPositiveCancellationDispatchedOrCancelledSendIsNotFresh(
        bool racing
    )
    {
        Phase4SessionControl host = new();
        using CancellationTokenSource cancellation = new();
        try
        {
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
            Task<JsonObject> send = one.PromptAsync("An actual paid intent.", cancellation.Token);
            await host.Sessions[0]
                .SendEntered.Task.WaitAsync(TimeSpan.FromSeconds(5), context.CancellationToken);
            if (racing)
            {
                await cancellation.CancelAsync();
                OperationCanceledException original =
                    await Assert.ThrowsAsync<OperationCanceledException>(() => send);
                Assert.IsTrue(original.CancellationToken.IsCancellationRequested);
                OperationException settlement = await Assert.ThrowsExactlyAsync<OperationException>(
                    host.Factory.CompleteAsync
                );
                Assert.Contains(
                    "Native send has no delivered terminal boundary",
                    settlement.Message
                );
                Assert.AreEqual("uncertain", host.State.Read(own).State);
                Assert.IsTrue(host.State.Read(own).GrantOpen);
                Assert.IsNull(host.State.Read(own).Actual);
            }
            else
            {
                await send;
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
            JsonObject ownReport = host.State.Accounting["reported"]!
                .AsArray()
                .OfType<JsonObject>()
                .Single(row => row["reservationId"]!.GetValue<string>() == own.Id);
            Assert.AreEqual(1L, ownReport["dispatches"]!.GetValue<long>());
        }
        finally
        {
            host.Sessions.FirstOrDefault()?.SendRelease.TrySetResult();
            await host.CleanupAsync();
        }
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
        Phase4SessionControl host = new() { HoldHost = true };
        Task? completion = null;
        Exception? primaryFailure = null;
        bool completionObserved = false;
        try
        {
            await host.StartOwnedRuntimeAsync(context.CancellationToken);
            CreditReservation own = host.Reserve("worker1");
            CreditReservation peer = host.Reserve("worker2");
            INativeWorker one = host.Factory.Create(own, calibration: false);
            INativeWorker two = host.Factory.Create(peer, calibration: false);
            await one.StartAsync(host.State.Cwd("worker1"), null, context.CancellationToken);
            await two.StartAsync(host.State.Cwd("worker2"), null, context.CancellationToken);
            await two.PromptAsync("Binding rejection must preserve me.", context.CancellationToken);
            completion = host.Factory.CompleteAsync();
            try
            {
                await host.HostEntered.Task.WaitAsync(
                    TimeSpan.FromSeconds(5),
                    context.CancellationToken
                );
                using SqliteConnection connection = host.State.Database.Open();
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
                            (
                                "INSERT INTO native_sessions(\n    "
                                + "reservation_id,worker,session_id,recovery,initialize_json,c"
                                + "reated_at)\nSELECT reservation_id,worker,session_id,recover"
                                + "y,initialize_json,created_at\nFROM native_sessions WHERE "
                                + "reservation_id=$id;"
                            ),
                            null,
                            ("$id", own.Id)
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
            finally
            {
                host.ReleaseHost();
            }
            OperationException error = await Assert.ThrowsExactlyAsync<OperationException>(() =>
                completion
            );
            completionObserved = true;
            Assert.AreEqual("credit_or_ownership_unknown", error.Code);
            Assert.Contains(
                scenario switch
                {
                    "missing-lineage" =>
                        (
                            "Legacy cancellation requires explicit compatible retained lineage "
                            + "evidence."
                        ),
                    "unsettled-ownership" => "Controlled owned-runtime stop failed.",
                    "missing-association" => "A required native association is missing.",
                    "reused-session" => "A required native association is not unique.",
                    _ => throw new ArgumentOutOfRangeException(nameof(scenario)),
                },
                error.Message
            );
            Assert.AreEqual("uncertain", host.State.Read(own).State);
            Assert.IsTrue(host.State.Read(own).GrantOpen);
            Assert.IsNull(host.State.Read(own).Actual);
            Assert.AreEqual(0L, Reports(host, own));
            Assert.AreEqual(7.25m, host.State.Read(peer).Actual);
            Assert.AreEqual(
                scenario == "unsettled-ownership" ? "uncertain" : "reconciled",
                host.State.Read(peer).State
            );
            Assert.IsEmpty(host.State.Accounting["cancellations"]!.AsArray());
        }
        catch (Exception exception)
        {
            primaryFailure = exception;
            throw;
        }
        finally
        {
            host.ReleaseHost();
            try
            {
                if (completion is not null)
                {
                    try
                    {
                        await completion.WaitAsync(TimeSpan.FromSeconds(5), CancellationToken.None);
                    }
                    catch (Exception) when (completionObserved)
                    {
                        // The body already asserted this terminal settlement failure.
                    }
                }
            }
            catch (Exception exception)
            {
                if (primaryFailure is null)
                {
                    primaryFailure = exception;
                }
                else
                {
                    context.WriteLine("Secondary factory completion failure: {0}", exception);
                }
            }
            finally
            {
                bool preserveState = completion is { IsCompleted: false };
                if (preserveState)
                {
                    context.WriteLine(
                        "Retained state for pending factory completion: {0}",
                        host.State.Cwd(string.Empty)
                    );
                }
                try
                {
                    await host.CleanupAsync(preserveState);
                }
                catch (Exception exception) when (primaryFailure is not null)
                {
                    context.WriteLine("Secondary fixture cleanup failure: {0}", exception);
                }
            }
        }
        if (primaryFailure is not null)
        {
            System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(primaryFailure).Throw();
        }
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
        Phase4SessionControl host = new()
        {
            HoldHost = true,
            ReportSettlement = scenario == "fresh-stop",
        };
        Task? completion = null;
        Exception? primaryFailure = null;
        bool completionObserved = false;
        string scratchRoot = host.State.Cwd(string.Empty);
        OperationException original = new(
            "controlled_fresh_stop",
            "Controlled fresh zero stop failed."
        );
        try
        {
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
            using SqliteConnection ledger = new(
                new SqliteConnectionStringBuilder
                {
                    DataSource = host.State.Database.Configuration.CampaignLedgerPath,
                    Pooling = false,
                }.ToString()
            );
            ledger.Open();
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
            completion = host.Factory.CompleteAsync();
            OperationException? failure = null;
            try
            {
                await host.HostEntered.Task.WaitAsync(
                    TimeSpan.FromSeconds(5),
                    context.CancellationToken
                );
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
                Assert.AreEqual(
                    0L,
                    Sql.Scalar(
                        ledger,
                        "SELECT dispatch_count FROM credit_sdk_usage WHERE reservation_id=$id;",
                        null,
                        ("$id", own.Id)
                    )
                );
                Assert.AreEqual(
                    0L,
                    Sql.Scalar(
                        ledger,
                        "SELECT reported_requests FROM credit_sdk_usage WHERE reservation_id=$id;",
                        null,
                        ("$id", own.Id)
                    )
                );
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
                    Assert.AreEqual(
                        "stopped",
                        host.State.Database.ReadMetadata("worker:worker1:lifecycle")
                    );
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
                        // SDK long remains zero. Only the application's durable observation is
                        // lost.
                        Assert.AreEqual(
                            1,
                            Sql.Execute(
                                ledger,
                                (
                                    "UPDATE credit_sdk_usage SET reported_requests=NULL WHERE "
                                    + "reservation_id=$id;"
                                ),
                                null,
                                ("$id", own.Id)
                            )
                        );
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
            }
            finally
            {
                host.ReleaseHost();
                try
                {
                    await completion.WaitAsync(TimeSpan.FromSeconds(5), CancellationToken.None);
                }
                catch (OperationException exception)
                {
                    failure = exception;
                    completionObserved = true;
                }
            }
            OperationException rejected = Assert.IsInstanceOfType<OperationException>(failure);
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
            Assert.AreEqual(0L, Reports(host, own));
            Assert.AreEqual(1L, Reports(host, peer));
            Assert.AreEqual(
                0L,
                Sql.Scalar(
                    ledger,
                    "SELECT dispatch_count FROM credit_sdk_usage WHERE reservation_id=$id;",
                    null,
                    ("$id", own.Id)
                )
            );
            Assert.AreEqual(
                "0",
                Sql.Scalar(
                    ledger,
                    "SELECT reported_nano_aiu FROM credit_sdk_usage WHERE reservation_id=$id;",
                    null,
                    ("$id", own.Id)
                )
            );
            Assert.IsNull(
                Sql.Scalar(
                    ledger,
                    "SELECT status FROM credit_sdk_usage WHERE reservation_id=$id;",
                    null,
                    ("$id", own.Id)
                )
            );
            if (scenario == "missing-durable-requests")
            {
                Assert.IsNull(
                    Sql.Scalar(
                        ledger,
                        "SELECT reported_requests FROM credit_sdk_usage WHERE reservation_id=$id;",
                        null,
                        ("$id", own.Id)
                    )
                );
            }
            else
            {
                Assert.AreEqual(
                    0L,
                    Sql.Scalar(
                        ledger,
                        "SELECT reported_requests FROM credit_sdk_usage WHERE reservation_id=$id;",
                        null,
                        ("$id", own.Id)
                    )
                );
            }
            Assert.IsNull(host.Sessions[0].AcceptedText);
            Assert.AreEqual("reconciled", host.State.Read(peer).State);
            Assert.AreEqual(7.25m, host.State.Read(peer).Actual);
            Assert.AreEqual(100m, host.State.Read(peer).Allowance);
            Assert.AreEqual(7.25m, host.State.Accounting["actualReported"]!.GetValue<decimal>());
            context.WriteLine(
                $"Phase4FreshRejectionOutcome: {scenario}: {rejected.Code}: {rejected.Message}"
            );
        }
        catch (Exception exception)
        {
            primaryFailure = exception;
        }
        finally
        {
            host.ReleaseHost();
            try
            {
                if (completion is not null && !completionObserved)
                {
                    await completion.WaitAsync(TimeSpan.FromSeconds(5), CancellationToken.None);
                }
            }
            catch (Exception exception)
            {
                if (primaryFailure is null)
                {
                    primaryFailure = exception;
                }
                else
                {
                    context.WriteLine("Secondary factory completion failure: {0}", exception);
                }
            }
            finally
            {
                bool preserveState = completion is { IsCompleted: false };
                if (scenario == "fresh-stop")
                {
                    try
                    {
                        TaskStatus? status = completion?.Status;
                        bool terminal = status is TaskStatus.RanToCompletion
                            or TaskStatus.Faulted or TaskStatus.Canceled;
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
                    catch (Exception exception)
                    {
                        context.WriteLine(
                            "Secondary factory settlement evidence failure: {0}", exception
                        );
                    }
                }
                if (preserveState)
                {
                    context.WriteLine(
                        "Retained state for pending factory completion: {0}", scratchRoot
                    );
                }
                try
                {
                    await host.CleanupAsync(preserveState);
                    context.WriteLine(
                        $"Phase4ManagedCleanup: {scratchRoot}; exists={Directory.Exists(
                            scratchRoot
                        )}"
                    );
                    if (!preserveState)
                    {
                        Assert.IsFalse(Directory.Exists(scratchRoot));
                    }
                }
                catch (Exception exception)
                {
                    if (primaryFailure is null)
                    {
                        primaryFailure = exception;
                    }
                    else
                    {
                        context.WriteLine("Secondary fixture cleanup failure: {0}", exception);
                    }
                }
            }
        }
        if (primaryFailure is not null)
        {
            System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(primaryFailure).Throw();
        }
    }

    private static long Reports(Phase4SessionControl host, CreditReservation reservation)
    {
        using SqliteConnection connection = new(
            new SqliteConnectionStringBuilder
            {
                DataSource = host.State.Database.Configuration.CampaignLedgerPath,
                Pooling = false,
            }.ToString()
        );
        connection.Open();
        return (long)
            Sql.Scalar(
                connection,
                "SELECT COUNT(*) FROM credit_reports WHERE reservation_id=$id;",
                null,
                ("$id", reservation.Id)
            )!;
    }

    private static string LeaseState(Phase4SessionControl host) =>
        (string)
            host.State.RunValue(
                "SELECT state FROM process_leases WHERE id=$id;",
                host.LeaseId.ToString(System.Globalization.CultureInfo.InvariantCulture)
            )!;
}
