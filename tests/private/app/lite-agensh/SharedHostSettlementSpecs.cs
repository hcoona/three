using System.Text.Json.Nodes;
using LiteAgensh.Tests.Support;
using Microsoft.Data.Sqlite;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace LiteAgensh.Tests;

[TestClass]
public sealed class SharedHostSettlementSpecs(TestContext context)
{
    [TestMethod]
    [DataRow(false, null)]
    [DataRow(false, "complete")]
    [DataRow(true, null)]
    [Timeout(120_000, CooperativeCancellation = true)]
    public async Task HandoffObservationPreservesEmptyAndMeasuredStopStaging(
        bool dispatched,
        string? initialUsageStatus
    )
    {
        using TypedHostControl host = new(initialUsageStatus: initialUsageStatus);
        using WorkerHandoffControl handoff = new();
        await using INativeSdkHost observed = handoff.Wrap(host);
        NativeWorkerFactory factory = new(
            host.Database, new ProcessRunner(host.Database), host.Ledger, () => observed
        );
        CreditReservation own = host.Reserve("worker1");
        INativeWorker worker = factory.Create(own, calibration: false);
        await worker.StartAsync(host.Cwd("worker1"), null, context.CancellationToken);
        if (dispatched)
        {
            await worker.PromptAsync("Record one controlled turn.", context.CancellationToken);
        }
        CreditReservation before = host.Read(own);

        await worker.StopAsync();

        CreditReservation after = host.Read(own);
        Assert.AreEqual(before with { Actual = dispatched ? 7.25m : null }, after);
        Assert.AreEqual(dispatched ? 1L : 0L, Dispatches(host, own));
        JsonObject reported = Assert.IsInstanceOfType<JsonObject>(
            Assert.ContainsSingle(host.Accounting["reported"]!.AsArray())
        );
        Assert.AreEqual(
            dispatched ? "complete" : initialUsageStatus,
            reported["status"]?.GetValue<string>()
        );
        Assert.IsTrue(host.Sessions[0].Closed);
        if (dispatched)
        {
            Assert.IsTrue(handoff.PositiveRecorded.Task.IsCompletedSuccessfully);
            Assert.AreEqual(7.25m, await handoff.PositiveRecorded.Task);
        }
        else
        {
            Assert.IsTrue(after.GrantOpen);
            Assert.IsFalse(handoff.PositiveRecorded.Task.IsCompleted);
        }
    }

    [TestMethod]
    [DataRow(false, false)]
    [DataRow(false, true)]
    [DataRow(true, false)]
    [DataRow(true, true)]
    public async Task AssociationCommitBeforeDispatchCapFailureRetainsBackingAndOriginalFailure(
        bool duringPrompt,
        bool mismatch
    )
    {
        using TypedHostControl host = new();
        CreditReservation own = host.Reserve("worker1");
        CreditReservation peer = host.Reserve("worker2");
        INativeWorker worker = host.Factory.Create(own, calibration: false);
        if (duringPrompt)
        {
            await worker.StartAsync(host.Cwd("worker1"), null, context.CancellationToken);
        }
        OperationException unsupported = new(
            "unavailable",
            "Controlled cap application unsupported."
        );
        if (duringPrompt)
        {
            host.Sessions[0].UnsupportedCap = mismatch ? null : unsupported;
            host.Sessions[0].MismatchAppliedCap = mismatch;
            host.Sessions[0].FailOnlyOnUpdate = true;
        }
        else
        {
            // An unsupported/readback fault must apply to the session created by Start.
            // This branch is configured by the host rather than pre-opening a worker.
            host.NextUnsupportedCap = mismatch ? null : unsupported;
            host.NextMismatchAppliedCap = mismatch;
        }

        OperationException error = await Assert.ThrowsExactlyAsync<OperationException>(async () =>
        {
            if (duringPrompt)
            {
                await worker.PromptAsync(
                    "Do not send on an invalid cap.",
                    context.CancellationToken
                );
            }
            else
            {
                await worker.StartAsync(host.Cwd("worker1"), null, context.CancellationToken);
            }
        });

        TypedHostControl.TypedSession session = host.Sessions[^1];
        Assert.AreSame(mismatch ? session.FirstFailure : unsupported, error);
        Assert.AreEqual(mismatch ? "credit_unknown" : "unavailable", error.Code);
        Assert.AreEqual(
            mismatch
                ? (
                    "Native identity, working directory, ownership, model or applied "
                    + "limit mismatched."
                )
                : "Controlled cap application unsupported.",
            error.Message
        );
        Assert.AreEqual("uncertain", host.Read(own).State);
        Assert.IsTrue(host.Read(own).GrantOpen);
        Assert.AreEqual(100m, host.Read(own).Allowance);
        Assert.IsNull(host.Read(own).Actual);
        Assert.IsNull(session.DeliveredOperation);
        Assert.AreEqual(peer, host.Read(peer));
        Assert.AreEqual(0L, Dispatches(host, own));
        Assert.AreEqual(
            own.SessionId,
            host.RunValue(
                "SELECT session_id FROM native_sessions WHERE reservation_id=$id;",
                own.Id
            )
        );
        Assert.AreEqual(
            own.SessionId,
            host.RunValue(
                "SELECT session_id FROM native_attempts WHERE reservation_id=$id;",
                own.Id
            )
        );
        Assert.AreEqual(100m, host.Accounting["outstandingAuthorization"]!.GetValue<decimal>());
        if (duringPrompt)
        {
            Assert.IsInstanceOfType<long>(error.Data["liteagensh.nativeFailureObserved"]);
            Assert.AreEqual(false, error.Data["liteagensh.nativeFailureCancelled"]);
        }
    }

    [TestMethod]
    public async Task AssociationCommitBeforeDispatchHealthyStartPublishesExactTupleBeforeSending()
    {
        using TypedHostControl host = new();
        CreditReservation own = host.Reserve("worker1");
        INativeWorker worker = host.Factory.Create(own, calibration: false);

        await worker.StartAsync(host.Cwd("worker1"), null, context.CancellationToken);

        Assert.AreEqual("active", host.Read(own).State);
        Assert.AreEqual(own.SessionId, worker.SessionId);
        Assert.AreEqual(
            own.Worker,
            host.RunValue("SELECT worker FROM native_sessions WHERE reservation_id=$id;", own.Id)
        );
        Assert.AreEqual(
            own.SessionId,
            host.RunValue(
                "SELECT session_id FROM native_attempts WHERE reservation_id=$id;",
                own.Id
            )
        );
        Assert.AreEqual(
            own.UsagePath,
            host.RunValue(
                "SELECT usage_path FROM native_attempts WHERE reservation_id=$id;",
                own.Id
            )
        );
        Assert.AreEqual(own.Id, host.Database.ReadMetadata("worker:worker1:reservation"));
        Assert.AreEqual(
            own.SessionId,
            WorkerStatus(host.Database, host.Factory, "worker1")["sessionId"]!.GetValue<string>()
        );
        Assert.AreEqual(0L, Dispatches(host, own));
        Assert.IsNull(host.Sessions[0].DeliveredOperation);
        JsonObject binding = JsonNode
            .Parse(
                host.Database.ReadMetadata($"native:runtime:{host.Id}:session:{own.SessionId}")!
            )!
            .AsObject();
        Assert.AreEqual(host.Id, binding["runtimeId"]!.GetValue<string>());
        Assert.AreEqual(own.Worker, binding["worker"]!.GetValue<string>());
        Assert.AreEqual(host.Cwd("worker1"), binding["workingDirectory"]!.GetValue<string>());

        JsonObject result = await worker.PromptAsync(
            "Publish the first finding.",
            context.CancellationToken
        );
        Assert.AreEqual("Publish the first finding.", result["acceptedText"]!.GetValue<string>());
        Assert.AreEqual(own.SessionId, result["sessionId"]!.GetValue<string>());
        Assert.AreEqual(1L, Dispatches(host, own));
    }

    [TestMethod]
    [DataRow("native_attempts")]
    [DataRow("native_sessions")]
    [DataRow("meta")]
    public async Task AssociationCommitBeforeDispatchFailedWriteRollsBackAndBlocksUnverifiedSend(
        string failedTable
    )
    {
        using TypedHostControl host = new();
        CreditReservation own = host.Reserve("worker1");
        CreditReservation peer = host.Reserve("worker2");
        INativeWorker worker = host.Factory.Create(own, calibration: false);
        host.RejectAssociationWrite(failedTable, own);

        SqliteException failure = await Assert.ThrowsExactlyAsync<SqliteException>(async () =>
            await worker.StartAsync(host.Cwd("worker1"), null, context.CancellationToken)
        );

        Assert.AreEqual(19, failure.SqliteErrorCode);
        Assert.AreEqual(1811, failure.SqliteExtendedErrorCode);
        Assert.Contains(
            failedTable switch
            {
                "native_attempts" => "Controlled first association write rejected.",
                "native_sessions" =>
                    "Controlled second association write rejected after matching attempt.",
                "meta" =>
                    "Controlled worker binding write rejected after matching associations.",
                _ => throw new ArgumentOutOfRangeException(nameof(failedTable)),
            },
            failure.Message
        );
        Assert.AreEqual(
            0L,
            host.RunValue("SELECT COUNT(*) FROM native_attempts WHERE reservation_id=$id;", own.Id)
        );
        Assert.AreEqual(
            0L,
            host.RunValue("SELECT COUNT(*) FROM native_sessions WHERE reservation_id=$id;", own.Id)
        );
        Assert.IsNull(host.Database.ReadMetadata("worker:worker1:reservation"));
        Assert.IsNull(host.Database.ReadMetadata("worker:worker1:session"));
        Assert.IsNull(host.Database.ReadMetadata("worker:worker1:recovery"));
        Assert.IsNull(worker.SessionId);
        Assert.IsEmpty(host.Sessions);

        OperationException error = await Assert.ThrowsExactlyAsync<OperationException>(async () =>
            await worker.PromptAsync(
                "An unverified worker must not send.",
                context.CancellationToken
            )
        );

        context.WriteLine(
            new JsonObject
            {
                ["failedTable"] = failedTable,
                ["reservationId"] = own.Id,
                ["sessionId"] = own.SessionId,
                ["runtimeId"] = host.Id,
                ["startFailure"] = failure.ToString(),
                ["sqliteCode"] = failure.SqliteErrorCode,
                ["sqliteExtendedCode"] = failure.SqliteExtendedErrorCode,
                ["unverifiedPromptFailure"] = error.ToString(),
                ["accounting"] = host.Accounting,
                ["dispatches"] = Dispatches(host, own),
            }.ToJsonString()
        );
        Assert.AreEqual("credit_unknown", error.Code);
        Assert.AreEqual(
            "A diagnostic or unverified session cannot send model prompts.",
            error.Message
        );
        Assert.IsEmpty(host.Sessions);
        Assert.AreEqual(0L, Dispatches(host, own));
        Assert.AreEqual("started", host.Read(own).State);
        Assert.IsTrue(host.Read(own).GrantOpen);
        Assert.AreEqual(100m, host.Read(own).Allowance);
        Assert.IsNull(host.Read(own).Actual);
        Assert.AreEqual(100m, host.Accounting["outstandingAuthorization"]!.GetValue<decimal>());
        Assert.AreEqual(peer, host.Read(peer));
    }

    [TestMethod]
    public async Task
    CommonHostBarrierOwnsFinalityMeasuredUsageRemainsBackedUntilEnteredBarrierReleases()
    {
        await using TypedSettlementScope scope = new(context);
        TypedHostControl host = scope.Host;
        CreditReservation own = host.Reserve("worker1");
        INativeWorker worker = host.Factory.Create(own, calibration: false);
        await worker.StartAsync(host.Cwd("worker1"), null, context.CancellationToken);
        await worker.PromptAsync("Complete a measured turn.", context.CancellationToken);

        await scope.EnterBarrierAsync();
        Assert.IsTrue(host.Sessions[0].Closed);
        Assert.AreEqual(
            "stopped",
            WorkerStatus(host.Database, host.Factory, "worker1")["lifecycle"]!.GetValue<string>()
        );
        Assert.AreEqual("active", host.Read(own).State);
        Assert.AreEqual(7.25m, host.Read(own).Actual);
        Assert.IsTrue(host.Read(own).GrantOpen);
        Assert.AreEqual(
            92.75m,
            host.Accounting["outstandingAuthorization"]!.GetValue<decimal>()
        );
        Assert.AreEqual(
            own.SessionId,
            host.RunValue(
                "SELECT session_id FROM native_sessions WHERE reservation_id=$id;",
                own.Id
            )
        );

        await scope.ReleaseAndAwaitAsync();

        Assert.AreEqual("reconciled", host.Read(own).State);
        Assert.AreEqual(7.25m, host.Read(own).Actual);
        Assert.IsFalse(host.Read(own).GrantOpen);
        Assert.AreEqual(7.25m, host.Accounting["actualReported"]!.GetValue<decimal>());
        Assert.AreEqual(7.25m, host.Accounting["runCommitted"]!.GetValue<decimal>());
        Assert.AreEqual(0m, host.Accounting["outstandingAuthorization"]!.GetValue<decimal>());
        scope.Complete();
    }

    private static long Dispatches(TypedHostControl host, CreditReservation reservation) =>
        host.Accounting["reported"]!
            .AsArray()
            .OfType<JsonObject>()
            .Single(row => row["reservationId"]!.GetValue<string>() == reservation.Id)[
            "dispatches"
        ]!.GetValue<long>();

    [TestMethod]
    [DataRow(true)]
    [DataRow(false)]
    public async Task PeerIsolationAndHostLifetimeMutationAndCloseLeaveLaterPeerOperationUsable(
        bool shared
    )
    {
        await using DispatchHostScope scope = new(context);
        DispatchSessionControl host = scope.Host;
        CreditReservation first = host.Reserve("worker1", shared);
        CreditReservation peer = host.Reserve("worker2", shared);
        INativeWorker one = host.Factory.Create(first, calibration: false);
        INativeWorker two = host.Factory.Create(peer, calibration: false);
        await one.StartAsync(host.State.Cwd("worker1"), null, context.CancellationToken);
        await two.StartAsync(host.State.Cwd("worker2"), null, context.CancellationToken);
        DispatchSessionControl.Session firstSession = host.Sessions.Single(session =>
        session.SessionId == one.SessionId
        );
        DispatchSessionControl.Session peerSession = host.Sessions.Single(session =>
        session.SessionId == two.SessionId
        );
        Assert.AreNotEqual(one.SessionId, two.SessionId);
        Assert.AreEqual("worker1", firstSession.OpenedWorker);
        Assert.AreEqual("worker2", peerSession.OpenedWorker);
        Assert.AreEqual(host.State.Cwd("worker1"), firstSession.OpenedCwd);
        Assert.AreEqual(host.State.Cwd("worker2"), peerSession.OpenedCwd);
        Assert.IsFalse(firstSession.Isolated);
        await one.PromptAsync("First independent conversation.", context.CancellationToken);
        firstSession.CompactionStart();
        firstSession.Publish(9_000_000_000, "complete", 1);
        firstSession.CompactionComplete();
        await one.StopAsync();
        Assert.IsTrue(firstSession.Closed);
        Assert.IsFalse(peerSession.Closed);
        Assert.AreEqual(0d, peerSession.Current.TotalNanoAiu);
        Assert.IsNull(peerSession.Current.Status);

        JsonObject result = await two.PromptAsync(
        "A later peer operation.",
        context.CancellationToken
        );

        Assert.AreEqual(peer.SessionId, result["sessionId"]!.GetValue<string>());
        Assert.AreEqual("worker2", result["actor"]!.GetValue<string>());
        Assert.AreEqual(host.State.Cwd("worker2"), result["cwd"]!.GetValue<string>());
        Assert.AreEqual(100m, result["appliedLimit"]!.GetValue<decimal>());
        Assert.AreEqual("A later peer operation.", peerSession.AcceptedText);
        Assert.AreEqual(1L, Dispatches(host.State, peer));
        await host.Factory.CompleteAsync();
        Assert.AreEqual(9m, host.State.Read(first).Actual);
        Assert.AreEqual(7.25m, host.State.Read(peer).Actual);
        Assert.AreEqual("reconciled", host.State.Read(peer).State);
        scope.Complete();
    }

    [TestMethod]
    [DataRow(false, false)]
    [DataRow(false, true)]
    [DataRow(true, false)]
    [DataRow(true, true)]
    public async Task PeerIsolationAndHostLifetimeCapFailurePreservesMeasuredPeer(
        bool duringPrompt,
        bool mismatch
    )
    {
        await using DispatchHostScope scope = new(context);
        DispatchSessionControl host = scope.Host;
        CreditReservation peer = host.Reserve("worker2");
        CreditReservation own = host.Reserve("worker1");
        INativeWorker two = host.Factory.Create(peer, calibration: false);
        INativeWorker one = host.Factory.Create(own, calibration: false);
        await two.StartAsync(host.State.Cwd("worker2"), null, context.CancellationToken);
        await two.PromptAsync("Preserve measured peer usage.", context.CancellationToken);
        await two.VerifyCreditsAsync(context.CancellationToken);
        CreditReservation peerBefore = host.State.Read(peer);
        if (duringPrompt)
        {
            await one.StartAsync(host.State.Cwd("worker1"), null, context.CancellationToken);
        }
        OperationException unsupported = new("unavailable", "Controlled unsupported peer cap.");
        if (duringPrompt)
        {
            host.Sessions[1].Guard.UnsupportedCap = mismatch ? null : unsupported;
            host.Sessions[1].Guard.MismatchAppliedCap = mismatch;
            host.Sessions[1].Guard.FailOnlyOnUpdate = true;
        }
        else
        {
            host.State.NextUnsupportedCap = mismatch ? null : unsupported;
            host.State.NextMismatchAppliedCap = mismatch;
        }

        OperationException error = await Assert.ThrowsExactlyAsync<OperationException>(
            async () =>
            {
                if (duringPrompt)
                {
                    await one.PromptAsync("Must not dispatch.", context.CancellationToken);
                }
                else
                {
                    await one.StartAsync(
                        host.State.Cwd("worker1"),
                        null,
                        context.CancellationToken
                    );
                }
            }
        );

        Assert.AreSame(mismatch ? host.Sessions[1].FirstFailure : unsupported, error);
        Assert.AreEqual(mismatch ? "credit_unknown" : "unavailable", error.Code);
        Assert.AreEqual("uncertain", host.State.Read(own).State);
        Assert.IsTrue(host.State.Read(own).GrantOpen);
        Assert.AreEqual(100m, host.State.Read(own).Allowance);
        Assert.IsNull(host.State.Read(own).Actual);
        Assert.AreEqual(0L, Dispatches(host.State, own));
        Assert.IsNull(host.Sessions[1].AcceptedText);
        Assert.AreEqual(
            own.SessionId,
            host.State.RunValue(
                "SELECT session_id FROM native_attempts WHERE reservation_id=$id;",
                own.Id
            )
        );
        Assert.AreEqual(
            own.SessionId,
            host.State.RunValue(
                "SELECT session_id FROM native_sessions WHERE reservation_id=$id;",
                own.Id
            )
        );
        Assert.AreEqual(peerBefore, host.State.Read(peer));
        Assert.AreEqual(7.25m, host.State.Read(peer).Actual);
        OperationException admission = await Assert.ThrowsExactlyAsync<OperationException>(
            async () =>
                await two.PromptAsync(
                    "Peer cannot bypass unresolved campaign usage.",
                    context.CancellationToken
                )
        );
        Assert.AreEqual("credit_unknown", admission.Code);
        Assert.AreEqual(
            "Unknown campaign usage or unverified native state blocks additional model calls.",
            admission.Message
        );
        Assert.AreEqual(1L, Dispatches(host.State, peer));
        Assert.AreEqual("Preserve measured peer usage.", host.Sessions[0].AcceptedText);
        Assert.AreEqual(7.25m, host.State.Read(peer).Actual);
        Assert.AreEqual("uncertain", host.State.Read(own).State);
        Assert.IsTrue(host.State.Read(own).GrantOpen);
        Assert.AreEqual(100m, host.State.Read(own).Allowance);
        Assert.IsNull(host.State.Read(own).Actual);
        Assert.AreEqual(0L, Dispatches(host.State, own));
        scope.Complete();
    }

    [TestMethod]
    [DataRow("session")]
    [DataRow("host")]
    [DataRow("both")]
    public async Task CommonHostBarrierOwnsFinalityFailurePreservesOriginalAndMeasuredPeer(
        string boundary
    )
    {
        await using DispatchHostScope scope = new(context);
        DispatchSessionControl host = scope.Host;
        CreditReservation own = host.Reserve("worker1");
        CreditReservation peer = host.Reserve("worker2");
        INativeWorker one = host.Factory.Create(own, calibration: false);
        INativeWorker two = host.Factory.Create(peer, calibration: false);
        await one.StartAsync(host.State.Cwd("worker1"), null, context.CancellationToken);
        await two.StartAsync(host.State.Cwd("worker2"), null, context.CancellationToken);
        await one.PromptAsync("First usage.", context.CancellationToken);
        await two.PromptAsync("Measured peer.", context.CancellationToken);
        OperationException original = new(
            "ownership_unresolved",
            "Controlled settlement boundary failed."
        );
        OperationException runtimeFailure = new(
            "ownership_unresolved",
            "Controlled common runtime settlement failed."
        );
        if (boundary != "host")
        {
            host.Sessions[0].StopFailure = original;
        }
        if (boundary != "session")
        {
            host.HostFailure = boundary == "host" ? original : runtimeFailure;
        }
        host.ReadStopAtHostEntry = () => new(
            host.Sessions.All(session => session.Closed),
            null,
            own
        );

        OperationException error = await Assert.ThrowsExactlyAsync<OperationException>(
            host.Factory.CompleteAsync
        );

        Assert.AreEqual("credit_or_ownership_unknown", error.Code);
        Assert.Contains(original.Message, error.Message);
        Assert.IsTrue(host.HostEntered.Task.IsCompletedSuccessfully);
        Assert.IsNotNull(host.StopAtHostEntry);
        Assert.IsTrue(host.StopAtHostEntry.SessionClosed);
        Assert.AreEqual("uncertain", host.State.Read(own).State);
        Assert.IsTrue(host.State.Read(own).GrantOpen);
        Assert.AreEqual(7.25m, host.State.Read(peer).Actual);
        Assert.AreEqual(
            boundary == "session" ? "reconciled" : "uncertain",
            host.State.Read(peer).State
        );
        Assert.AreEqual(boundary != "session", host.State.Read(peer).GrantOpen);
        Assert.AreEqual(
            own.SessionId,
            host.State.RunValue(
                "SELECT session_id FROM native_sessions WHERE reservation_id=$id;",
                own.Id
            )
        );
        Assert.Contains(original.Message, host.Factory.SettlementFailures.ToJsonString());
        if (boundary == "both")
        {
            JsonObject workerFailure = Assert.ContainsSingle(
                host.Factory.SettlementFailures.OfType<JsonObject>().Where(item =>
                    item["phase"]?.GetValue<string>() == "stop"
                    && item["worker"]?.GetValue<string>() == own.Worker
                    && item["reservationId"]?.GetValue<string>() == own.Id
                )
            );
            Assert.Contains(original.Message, workerFailure["message"]!.GetValue<string>());
            JsonObject hostFailure = Assert.ContainsSingle(
                host.Factory.SettlementFailures.OfType<JsonObject>().Where(item =>
                    item["phase"]?.GetValue<string>() == "native-runtime"
                )
            );
            Assert.AreEqual(
                runtimeFailure.Message, hostFailure["message"]!.GetValue<string>()
            );
        }
        Assert.IsTrue(host.Sessions.All(session => session.Closed));
        scope.Complete();
    }

    [TestMethod]
    public async Task
    AssociationCommitBeforeDispatchOwnedTupleAndIndependentCapRenewalPreservePeer()
    {
        await using DispatchHostScope scope = new(context);
        DispatchSessionControl host = scope.Host;
        string scratchRoot = host.State.Cwd(string.Empty);
        await host.StartOwnedRuntimeAsync(context.CancellationToken);
        Assert.IsNotNull(host.ReadyIdentity);
        CreditReservation own = host.Reserve("worker1");
        CreditReservation peer = host.Reserve("worker2");
        INativeWorker one = host.Factory.Create(own, calibration: false);
        INativeWorker two = host.Factory.Create(peer, calibration: false);
        string cwd = host.State.Cwd("worker1");
        await one.StartAsync(cwd, null, context.CancellationToken);
        await two.StartAsync(host.State.Cwd("worker2"), null, context.CancellationToken);
        await two.PromptAsync(
            "Keep independent measured peer usage.",
            context.CancellationToken
        );
        await two.VerifyCreditsAsync(context.CancellationToken);
        host.Sessions[0].HoldSend = true;
        host.Sessions[0].ReadDispatchAtSendEntry = () => Dispatches(host.State, own);
        Task<JsonObject> first = scope.Track(
            one.PromptAsync("Observe the owned first admission.", context.CancellationToken),
            "first held send"
        );

        // This is the first actual Send entry, not a stale signal from an earlier turn.
        await host.Sessions[0]
            .SendEntered.Task.WaitAsync(TimeSpan.FromSeconds(5), context.CancellationToken);
        Assert.IsGreaterThan(0L, host.LeaseId);
        Assert.AreEqual(
            "running",
            host.State.RunValue(
                "SELECT state FROM process_leases WHERE kind=$id;",
                "native:runtime:" + host.Id
            )
        );
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
        foreach (string table in new[] { "native_attempts", "native_sessions" })
        {
            Assert.AreEqual(
                1L,
                host.State.RunValue(
                    $"SELECT COUNT(*) FROM {table} WHERE reservation_id=$id;",
                    own.Id
                )
            );
            Assert.AreEqual(
                own.Worker,
                host.State.RunValue(
                    $"SELECT worker FROM {table} WHERE reservation_id=$id;",
                    own.Id
                )
            );
            Assert.AreEqual(
                own.SessionId,
                host.State.RunValue(
                    $"SELECT session_id FROM {table} WHERE reservation_id=$id;",
                    own.Id
                )
            );
            Assert.AreEqual(
                "sdk:" + NativeSdkSession.SdkVersion,
                host.State.RunValue(
                    $"SELECT initialize_json FROM {table} WHERE reservation_id=$id;",
                    own.Id
                )
            );
        }
        Assert.AreEqual(
            own.UsagePath,
            host.State.RunValue(
                "SELECT usage_path FROM native_attempts WHERE reservation_id=$id;",
                own.Id
            )
        );
        Assert.AreEqual(own.Id, host.State.Database.ReadMetadata("worker:worker1:reservation"));
        Assert.AreEqual(
            own.SessionId,
            WorkerStatus(
                host.State.Database, host.Factory, "worker1"
            )["sessionId"]!.GetValue<string>()
        );
        JsonObject binding = JsonNode
            .Parse(
                host.State.Database.ReadMetadata(
                    $"native:runtime:{host.Id}:session:{own.SessionId}"
                )!
            )!
            .AsObject();
        Assert.AreEqual(host.Id, binding["runtimeId"]!.GetValue<string>());
        Assert.AreEqual(host.LeaseId, binding["runtimeLeaseId"]!.GetValue<long>());
        Assert.AreEqual(own.SessionId, binding["sessionId"]!.GetValue<string>());
        Assert.AreEqual(own.Worker, binding["worker"]!.GetValue<string>());
        Assert.AreEqual(cwd, binding["workingDirectory"]!.GetValue<string>());
        JsonObject diagnostic = JsonNode
            .Parse(host.State.Database.ReadMetadata($"native:session:{own.SessionId}")!)!
            .AsObject();
        AssertFreshAdmissionDiagnostic(diagnostic, host, own, cwd);
        JsonObject runtime = JsonNode
            .Parse(host.State.Database.ReadMetadata($"native:runtime:{host.Id}")!)!
            .AsObject();
        Assert.AreEqual(host.Id, runtime["runtimeId"]!.GetValue<string>());
        Assert.AreEqual(host.LeaseId, runtime["runtimeLeaseId"]!.GetValue<long>());
        Assert.AreEqual("ready", runtime["state"]!.GetValue<string>());
        Assert.AreEqual(
            OfflineNativeControl.RuntimeVersion,
            runtime["runtimeVersion"]!.GetValue<string>()
        );
        JsonObject start = JsonNode
            .Parse(
                host.State.Database.ReadMetadata(NoDispatchCancellation.Marker(own.Id))!
            )!
            .AsObject();
        Assert.AreEqual(host.State.Database.RunId, start["runId"]!.GetValue<string>());
        Assert.AreEqual(own.Id, start["reservationId"]!.GetValue<string>());
        Assert.AreEqual(own.SessionId, start["sessionId"]!.GetValue<string>());
        Assert.AreEqual(cwd, start["workingDirectory"]!.GetValue<string>());
        Assert.AreEqual(
            "controlled-native-dependency",
            start["evidenceKind"]!.GetValue<string>()
        );
        AssertFreshAdmissionDiagnostic(start["initial"]!.AsObject(), host, own, cwd);
        Assert.AreEqual(1L, Assert.ContainsSingle(host.Sessions[0].DispatchesAtSendEntry));
        Assert.AreEqual(0L, host.Sessions[0].Current.TotalUserRequests);
        Assert.IsNull(host.Sessions[0].AcceptedText);
        Assert.AreEqual(7.25m, host.State.Read(peer).Actual);
        context.WriteLine(
            $"Phase4OwnedBeforeSend: {new JsonObject
            {
                ["runId"] = host.State.Database.RunId,
                ["reservationId"] = own.Id,
                ["pid"] = host.ReadyIdentity.Pid,
                ["birthTicks"] = host.ReadyIdentity.BirthTicks,
                ["binding"] = binding,
                ["diagnostic"] = diagnostic,
                ["scratchRoot"] = scratchRoot,
            }.ToJsonString()}"
        );

        await DispatchHostScope.ReleaseSendAndAwaitAsync(host.Sessions[0], first);
        await one.VerifyCreditsAsync(context.CancellationToken);
        Assert.AreEqual(7.25m, host.State.Read(own).Actual);
        Assert.IsFalse(host.State.Read(own).GrantOpen);
        host.Sessions[0].TurnNanoAiu = 14_500_000_000;
        JsonObject renewed = await one.PromptAsync(
            "Use the independently renewed cap.",
            context.CancellationToken
        );
        Assert.AreEqual(
            "Use the independently renewed cap.",
            renewed["acceptedText"]!.GetValue<string>()
        );
        Assert.AreEqual(own.SessionId, renewed["sessionId"]!.GetValue<string>());
        CollectionAssert.AreEqual(
            new long[] { 1, 2 },
            host.Sessions[0].DispatchesAtSendEntry
        );
        Assert.AreEqual("worker1", renewed["actor"]!.GetValue<string>());
        Assert.AreEqual(cwd, renewed["cwd"]!.GetValue<string>());
        Assert.AreEqual(107.25m, renewed["appliedLimit"]!.GetValue<decimal>());
        Assert.AreEqual(
            107.25d,
            host.Sessions[0].Metadata!["appliedLimit"]!.GetValue<double>()
        );
        Assert.AreEqual(100d, host.Sessions[1].Metadata!["appliedLimit"]!.GetValue<double>());
        Assert.AreEqual(7_250_000_000d, host.Sessions[1].Current.TotalNanoAiu);
        Assert.AreEqual(1L, host.Sessions[1].Current.TotalUserRequests);
        Assert.AreEqual("Keep independent measured peer usage.", host.Sessions[1].AcceptedText);
        Assert.AreEqual(7.25m, host.State.Read(peer).Actual);
        Assert.AreEqual(100m, host.State.Read(peer).Allowance);
        await host.Factory.CompleteAsync();
        Assert.AreEqual(14.5m, host.State.Read(own).Actual);
        Assert.AreEqual(7.25m, host.State.Read(peer).Actual);
        Assert.AreEqual(21.75m, host.State.Accounting["actualReported"]!.GetValue<decimal>());
        Assert.AreEqual(2L, Dispatches(host.State, own));
        Assert.AreEqual(1L, Dispatches(host.State, peer));
        await scope.CleanupAndAssertScratchRemovedAsync(scratchRoot);
    }

    [TestMethod]
    [Timeout(30_000, CooperativeCancellation = true)]
    [DataRow(false)]
    [DataRow(true)]
    public async Task OrdinarySharedCapUpdateWithChangedUsageRetainsBackingWithoutAnotherSend(
        bool changeRequestCountOnly
    )
    {
        await using DispatchHostScope scope = new(context);
        DispatchSessionControl host = scope.Host;
        CreditReservation own = host.Reserve("worker1");
        INativeWorker worker = host.Factory.Create(own, calibration: false);
        await worker.StartAsync(host.State.Cwd(own.Worker), null, context.CancellationToken);
        DispatchSessionControl.Session session = Assert.ContainsSingle(host.Sessions);
        session.ReadDispatchAtSendEntry = () => Dispatches(host.State, own);
        const string firstPrompt = "Complete the first backed turn.";
        await worker.PromptAsync(firstPrompt, context.CancellationToken);
        await worker.VerifyCreditsAsync(context.CancellationToken);
        Assert.AreEqual(7.25m, host.State.Read(own).Actual);
        Assert.IsFalse(host.State.Read(own).GrantOpen);
        Assert.AreEqual(1L, session.Current.TotalUserRequests);
        JsonObject before = RunStateQueries.Financial(host.State.Database, host.State.Ledger);
        Assert.HasCount(1, before["reports"]!.AsArray());

        double changedNano = changeRequestCountOnly ? 7_250_000_000d : 8_000_000_000d;
        long changedRequests = changeRequestCountOnly ? 2L : 1L;
        session.UsageOnLimitUpdate = new(
            own.SessionId!, changedNano, "complete", changedRequests
        );
        session.TurnNanoAiu = 14_500_000_000d;
        OperationException failure = await Assert.ThrowsExactlyAsync<OperationException>(() =>
            worker.PromptAsync(
                "Do not send with an inconsistent baseline.",
                context.CancellationToken
            )
        );
        Assert.AreEqual("credit_unknown", failure.Code);
        Assert.AreEqual(
            "Native cumulative usage changed during a shared cap update.",
            failure.Message
        );

        OperationException stop = await Assert.ThrowsExactlyAsync<OperationException>(
            host.Factory.CompleteAsync
        );
        Assert.AreEqual("credit_or_ownership_unknown", stop.Code);
        Assert.IsTrue(session.Closed);
        Assert.AreEqual(firstPrompt, session.AcceptedText);
        Assert.AreEqual(1L, Assert.ContainsSingle(session.DispatchesAtSendEntry));
        Assert.AreEqual(1L, Dispatches(host.State, own));
        CreditReservation retained = host.State.Read(own);
        Assert.AreEqual("uncertain", retained.State);
        Assert.IsTrue(retained.GrantOpen);
        Assert.AreEqual(7.25m, retained.Actual);
        Assert.AreEqual(107.25m, retained.Allowance);
        Assert.AreEqual(
            100m, host.State.Accounting["outstandingAuthorization"]!.GetValue<decimal>()
        );
        Assert.AreEqual(
            "credit_unknown",
            Assert.ThrowsExactly<OperationException>(
                host.State.Ledger.RequirePaidAdmission
            ).Code
        );
        JsonObject after = RunStateQueries.Financial(host.State.Database, host.State.Ledger);
        Assert.IsTrue(JsonNode.DeepEquals(before["usage"], after["usage"]));
        Assert.IsTrue(JsonNode.DeepEquals(before["reports"], after["reports"]));
        JsonNode diagnostic = JsonNode.Parse(
            host.State.Database.ReadMetadata($"native:session:{own.SessionId}")!
        )!;
        Assert.AreEqual(107.25d, diagnostic["appliedLimit"]!.GetValue<double>());
        Assert.AreEqual(
            changeRequestCountOnly ? "7250000000" : "8000000000",
            diagnostic["reportedNanoAiu"]!.GetValue<string>()
        );
        Assert.AreEqual("complete", diagnostic["aiCreditsStatus"]!.GetValue<string>());
        Assert.AreEqual(changedRequests, diagnostic["totalUserRequests"]!.GetValue<long>());
        scope.Complete();
    }

    [TestMethod]
    [DataRow("native_attempts")]
    [DataRow("native_sessions")]
    public async Task AssociationCommitBeforeDispatchOwnedWriteRollbackPreservesMeasuredPeer(
        string failedTable
    )
    {
        await using DispatchHostScope scope = new(context);
        DispatchSessionControl host = scope.Host;
        string scratchRoot = host.State.Cwd(string.Empty);
        await host.StartOwnedRuntimeAsync(context.CancellationToken);
        Assert.IsNotNull(host.ReadyIdentity);
        Assert.IsGreaterThan(0L, host.LeaseId);
        CreditReservation own = host.Reserve("worker1");
        CreditReservation peer = host.Reserve("worker2");
        INativeWorker one = host.Factory.Create(own, calibration: false);
        INativeWorker two = host.Factory.Create(peer, calibration: false);
        await two.StartAsync(host.State.Cwd("worker2"), null, context.CancellationToken);
        await two.PromptAsync(
            "Retain the measured peer through rollback.",
            context.CancellationToken
        );
        await two.VerifyCreditsAsync(context.CancellationToken);
        host.State.RejectAssociationWrite(failedTable, own);

        SqliteException original = await Assert.ThrowsExactlyAsync<SqliteException>(async () =>
            await one.StartAsync(host.State.Cwd("worker1"), null, context.CancellationToken)
        );

        Assert.AreEqual(19, original.SqliteErrorCode);
        Assert.AreEqual(1811, original.SqliteExtendedErrorCode);
        Assert.Contains(
            failedTable == "native_attempts"
                ? "Controlled first association write rejected."
                : "Controlled second association write rejected after matching attempt.",
            original.Message
        );
        foreach (string table in new[] { "native_attempts", "native_sessions" })
        {
            Assert.AreEqual(
                0L,
                host.State.RunValue(
                    $"SELECT COUNT(*) FROM {table} WHERE reservation_id=$id;",
                    own.Id
                )
            );
            Assert.AreEqual(
                peer.SessionId,
                host.State.RunValue(
                    $"SELECT session_id FROM {table} WHERE reservation_id=$id;",
                    peer.Id
                )
            );
        }
        Assert.IsNull(host.State.Database.ReadMetadata("worker:worker1:reservation"));
        Assert.IsNull(host.State.Database.ReadMetadata("worker:worker1:session"));
        Assert.IsNull(host.State.Database.ReadMetadata("worker:worker1:recovery"));
        Assert.IsNull(one.SessionId);
        Assert.HasCount(1, host.Sessions);
        OperationException blocked = await Assert.ThrowsExactlyAsync<OperationException>(
            async () =>
                await one.PromptAsync(
                    "An unpublished session cannot dispatch.",
                    context.CancellationToken
                )
        );
        Assert.AreEqual("credit_unknown", blocked.Code);
        Assert.AreEqual(
            "A diagnostic or unverified session cannot send model prompts.",
            blocked.Message
        );
        Assert.AreEqual("started", host.State.Read(own).State);
        Assert.AreEqual(100m, host.State.Read(own).Allowance);
        Assert.IsTrue(host.State.Read(own).GrantOpen);
        Assert.IsNull(host.State.Read(own).Actual);
        Assert.AreEqual(0L, Dispatches(host.State, own));
        Assert.AreEqual(7.25m, host.State.Read(peer).Actual);
        Assert.AreEqual(
            "running",
            host.State.RunValue(
                "SELECT state FROM process_leases WHERE kind=$id;",
                "native:runtime:" + host.Id
            )
        );
        context.WriteLine(
            $"Phase4OwnedWriteRollback: {new JsonObject
            {
                ["failedTable"] = failedTable,
                ["runId"] = host.State.Database.RunId,
                ["reservationId"] = own.Id,
                ["runtimeId"] = host.Id,
                ["leaseId"] = host.LeaseId,
                ["pid"] = host.ReadyIdentity.Pid,
                ["birthTicks"] = host.ReadyIdentity.BirthTicks,
                ["originalFailure"] = original.ToString(),
                ["accounting"] = host.State.Accounting,
                ["scratchRoot"] = scratchRoot,
            }.ToJsonString()}"
        );
        // The early SQL fault precedes session creation. Stop must retain that
        // unresolved reservation; it cannot manufacture a zero observation.
        OperationException retained = await Assert.ThrowsExactlyAsync<OperationException>(
            host.Factory.CompleteAsync
        );
        Assert.AreEqual("credit_or_ownership_unknown", retained.Code);
        Assert.Contains("No SDK connection.", retained.Message);
        Assert.AreEqual("uncertain", host.State.Read(own).State);
        Assert.IsTrue(host.State.Read(own).GrantOpen);
        Assert.IsNull(host.State.Read(own).Actual);
        Assert.AreEqual(
            100m,
            host.State.Accounting["outstandingAuthorization"]!.GetValue<decimal>()
        );
        Assert.AreEqual("reconciled", host.State.Read(peer).State);
        Assert.AreEqual(7.25m, host.State.Read(peer).Actual);
        Assert.AreEqual(7.25m, host.State.Accounting["actualReported"]!.GetValue<decimal>());
        Assert.AreEqual(0L, Dispatches(host.State, own));
        Assert.AreEqual(1L, Dispatches(host.State, peer));
        Assert.IsEmpty(host.State.Accounting["cancellations"]!.AsArray());
        Assert.IsFalse(host.State.Ledger.HasCompleteSdkUsage(own.Id));
        Assert.IsTrue(host.State.Ledger.HasCompleteSdkUsage(peer.Id));
        Assert.AreEqual(
            "exited",
            host.State.RunValue(
                "SELECT state FROM process_leases WHERE kind=$id;",
                "native:runtime:" + host.Id
            )
        );
        await scope.CleanupAndAssertScratchRemovedAsync(scratchRoot);
    }

    [TestMethod]
    public async Task
    CompactionReadFencePendingSubtotalFortyRequiresFreshFortySevenBeforeReturningBacking()
    {
        // A tight real pool makes the next grant attributable to the returned 53.
        await using DispatchHostScope scope = new(
            context,
            new TypedHostControl(creditAllowance: 100)
        );
        DispatchSessionControl host = scope.Host;
        CreditReservation own = host.Reserve("worker1");
        CreditReservation peer = host.Reserve("worker2");
        INativeWorker worker = host.Factory.Create(own, calibration: false);
        await worker.StartAsync(host.State.Cwd("worker1"), null, context.CancellationToken);
        host.Sessions[0].TurnNanoAiu = 40_000_000_000;
        await worker.PromptAsync(
            "Complete turn with background work.",
            context.CancellationToken
        );
        scope.StartCompaction(host.Sessions[0]);
        Task entered = host.Sessions[0].ArmFence();
        Task verification = scope.Track(
            worker.VerifyCreditsAsync(scope.Token),
            "compaction verification"
        );
        if (
            await Task.WhenAny(entered, verification)
                .WaitAsync(TimeSpan.FromSeconds(5), context.CancellationToken) == verification
        )
        {
            await verification;
            Assert.Fail("Verification returned before entering the pending compaction fence.");
        }
        await entered;
        Assert.AreEqual(40_000_000_000d, host.Sessions[0].Current.TotalNanoAiu);
        Assert.AreEqual("complete", host.Sessions[0].Current.Status);
        Assert.IsTrue(host.State.Read(own).GrantOpen);
        Assert.AreEqual(
            100m,
            host.State.Accounting["outstandingAuthorization"]!.GetValue<decimal>()
        );
        Assert.AreEqual(peer, host.State.Read(peer));

        host.Sessions[0].Publish(47_000_000_000, "complete", 1);
        scope.CompleteCompaction(host.Sessions[0]);
        await verification.WaitAsync(TimeSpan.FromSeconds(5), context.CancellationToken);

        Assert.AreEqual(47m, host.State.Read(own).Actual);
        Assert.IsFalse(host.State.Read(own).GrantOpen);
        Assert.AreEqual(
            0m,
            host.State.Accounting["outstandingAuthorization"]!.GetValue<decimal>()
        );
        Assert.AreEqual(47m, host.State.Accounting["runCommitted"]!.GetValue<decimal>());
        INativeWorker next = host.Factory.Create(peer, calibration: false);
        await next.StartAsync(host.State.Cwd("worker2"), null, context.CancellationToken);
        Assert.AreEqual(53m, host.State.Read(peer).Allowance);
        Assert.IsTrue(host.State.Read(peer).GrantOpen);
        Assert.AreEqual(53d, host.Sessions[1].Metadata!["appliedLimit"]!.GetValue<double>());
        Assert.AreEqual(47m, host.State.Read(own).Actual);
        Assert.AreEqual(100m, host.State.Accounting["runCommitted"]!.GetValue<decimal>());
        Assert.AreEqual(100m, host.State.Accounting["campaignCommitted"]!.GetValue<decimal>());
        Assert.AreEqual(
            53m,
            host.State.Accounting["outstandingAuthorization"]!.GetValue<decimal>()
        );
        context.WriteLine(
            $"Phase4ReturnedBackingAdmission: {host.State.Accounting.ToJsonString()}"
        );
        JsonObject delivered = await next.PromptAsync(
            "Use the actually returned grant.",
            context.CancellationToken
        );
        Assert.AreEqual(
            "Use the actually returned grant.",
            delivered["acceptedText"]!.GetValue<string>()
        );
        Assert.AreEqual(53m, delivered["appliedLimit"]!.GetValue<decimal>());
        await host.Factory.CompleteAsync();
        Assert.AreEqual("reconciled", host.State.Read(own).State);
        Assert.AreEqual("reconciled", host.State.Read(peer).State);
        Assert.AreEqual(7.25m, host.State.Read(peer).Actual);
        Assert.AreEqual(54.25m, host.State.Accounting["runCommitted"]!.GetValue<decimal>());
        Assert.AreEqual(54.25m, host.State.Accounting["actualReported"]!.GetValue<decimal>());
        Assert.AreEqual(
            0m,
            host.State.Accounting["outstandingAuthorization"]!.GetValue<decimal>()
        );
        Assert.AreEqual(1L, Dispatches(host.State, own));
        Assert.AreEqual(1L, Dispatches(host.State, peer));
        scope.Complete();
    }

    [TestMethod]
    [DataRow("during-read")]
    [DataRow("after-stage")]
    [DataRow("failed-terminal")]
    public async Task CompactionReadFenceChangedOrFailedWorkCannotBeRepairedByHostExit(
        string scenario
    )
    {
        await using DispatchHostScope scope = new(context, holdHost: true);
        DispatchSessionControl host = scope.Host;
        CreditReservation own = host.Reserve("worker1");
        CreditReservation peer = host.Reserve("worker2");
        INativeWorker one = host.Factory.Create(own, calibration: false);
        INativeWorker two = host.Factory.Create(peer, calibration: false);
        await one.StartAsync(host.State.Cwd("worker1"), null, context.CancellationToken);
        await two.StartAsync(host.State.Cwd("worker2"), null, context.CancellationToken);
        await one.PromptAsync("Read fence subject.", context.CancellationToken);
        await two.PromptAsync("Measured independent peer.", context.CancellationToken);
        if (scenario == "during-read")
        {
            host.Sessions[0].ChangeDuringRead = true;
        }
        if (scenario == "failed-terminal")
        {
            host.Sessions[0].CompactionStart();
            host.Sessions[0].CompactionComplete(success: false);
        }

        await scope.EnterHostBarrierAsync();
        Assert.IsTrue(host.Sessions.All(session => session.Closed));
        Assert.IsTrue(host.State.Read(own).GrantOpen);
        Assert.IsTrue(host.State.Read(peer).GrantOpen);
        if (scenario == "after-stage")
        {
            Assert.AreEqual(7.25m, host.State.Read(own).Actual);
            host.Sessions[0].CompactionStart();
            host.Sessions[0].CompactionComplete();
        }

        OperationException error = await Assert.ThrowsExactlyAsync<OperationException>(
            scope.ReleaseHostAndAwaitAsync
        );

        Assert.AreEqual("credit_or_ownership_unknown", error.Code);
        Assert.Contains(
            scenario switch
            {
                "during-read" => "Native work changed during the final usage query.",
                "after-stage" => "Native work invalidated the staged usage reading.",
                _ => "Native compaction settlement failed or contradicted its start.",
            },
            error.Message
        );
        Assert.AreEqual("uncertain", host.State.Read(own).State);
        Assert.IsTrue(host.State.Read(own).GrantOpen);
        Assert.AreEqual(7.25m, host.State.Read(peer).Actual);
        Assert.AreEqual("reconciled", host.State.Read(peer).State);
        Assert.IsFalse(host.State.Read(peer).GrantOpen);
        scope.Complete();
    }

    [TestMethod]
    public async Task
    CompactionReadFenceCancellationWhilePendingRetainsBackingAndOriginalCancellation()
    {
        await using DispatchHostScope scope = new(context);
        DispatchSessionControl host = scope.Host;
        CreditReservation own = host.Reserve("worker1");
        INativeWorker worker = host.Factory.Create(own, calibration: false);
        await worker.StartAsync(host.State.Cwd("worker1"), null, context.CancellationToken);
        await worker.PromptAsync(
            "Turn before cancelled compaction read.",
            context.CancellationToken
        );
        scope.StartCompaction(host.Sessions[0]);
        Task entered = host.Sessions[0].ArmFence();
        Task verification = scope.Track(
            worker.VerifyCreditsAsync(scope.Token),
            "cancelled compaction verification"
        );
        if (
            await Task.WhenAny(entered, verification)
                .WaitAsync(TimeSpan.FromSeconds(5), context.CancellationToken) == verification
        )
        {
            await verification;
            Assert.Fail("Verification returned before entering the pending compaction fence.");
        }
        await entered;

        await scope.CancelAsync();
        OperationCanceledException error = await Assert.ThrowsAsync<OperationCanceledException>(
            () => verification.WaitAsync(TimeSpan.FromSeconds(5), CancellationToken.None)
        );

        Assert.IsTrue(error.CancellationToken.IsCancellationRequested);
        Assert.AreEqual("uncertain", host.State.Read(own).State);
        Assert.IsTrue(host.State.Read(own).GrantOpen);
        Assert.IsNull(host.State.Read(own).Actual);
        Assert.AreEqual(1L, Dispatches(host.State, own));
        // End the owned dependency work explicitly; do not retry failed verification.
        scope.CompleteCompaction(host.Sessions[0]);
        scope.Complete();
    }

    [TestMethod]
    public async Task
    SerializedNativeEvidenceContractGeneratedStartAndMeasuredDiagnosticPreserveSemantics()
    {
        await using DispatchHostScope scope = new(context);
        DispatchSessionControl host = scope.Host;
        CreditReservation own = host.Reserve("worker1");
        INativeWorker worker = host.Factory.Create(own, calibration: false);
        await worker.StartAsync(host.State.Cwd("worker1"), null, context.CancellationToken);
        JsonObject start = JsonNode
            .Parse(host.State.Database.ReadMetadata(NoDispatchCancellation.Marker(own.Id))!)!
            .AsObject();
        Assert.AreEqual(
            "controlled-native-dependency",
            start["evidenceKind"]!.GetValue<string>()
        );
        Assert.AreEqual(host.State.Database.RunId, start["runId"]!.GetValue<string>());
        Assert.AreEqual(own.Id, start["reservationId"]!.GetValue<string>());
        Assert.AreEqual(own.SessionId, start["sessionId"]!.GetValue<string>());
        Assert.AreEqual("0", start["initial"]!["reportedNanoAiu"]!.GetValue<string>());
        Assert.IsNull(start["initial"]!["aiCreditsStatus"]);
        Assert.AreEqual(0L, start["initial"]!["totalUserRequests"]!.GetValue<long>());
        Assert.IsNull(host.State.Read(own).Actual);

        await worker.PromptAsync("Generate application diagnostic.", context.CancellationToken);
        await worker.VerifyCreditsAsync(context.CancellationToken);

        JsonObject diagnostic = JsonNode
            .Parse(host.State.Database.ReadMetadata($"native:session:{own.SessionId}")!)!
            .AsObject();
        Assert.AreEqual(own.SessionId, diagnostic["sessionId"]!.GetValue<string>());
        Assert.AreEqual(host.Id, diagnostic["runtimeId"]!.GetValue<string>());
        Assert.AreEqual(
            NativeSdkSession.SdkVersion,
            diagnostic["sdkVersion"]!.GetValue<string>()
        );
        Assert.AreEqual(
            OfflineNativeControl.RuntimeVersion,
            diagnostic["runtimeVersion"]!.GetValue<string>()
        );
        Assert.AreEqual(
            host.State.Cwd("worker1"),
            diagnostic["workingDirectory"]!.GetValue<string>()
        );
        Assert.AreEqual(100m, diagnostic["appliedLimit"]!.GetValue<decimal>());
        Assert.AreEqual("7250000000", diagnostic["reportedNanoAiu"]!.GetValue<string>());
        Assert.AreEqual("complete", diagnostic["aiCreditsStatus"]!.GetValue<string>());
        Assert.AreEqual(1L, diagnostic["totalUserRequests"]!.GetValue<long>());
        Assert.AreEqual(7.25m, host.State.Read(own).Actual);
        Assert.AreEqual(
            "sdk:" + NativeSdkSession.SdkVersion,
            host.State.RunValue(
                "SELECT initialize_json FROM native_attempts WHERE reservation_id=$id;",
                own.Id
            )
        );
        scope.Complete();
    }

    [TestMethod]
    public async Task PeerIsolationAndHostLifetimeOnePendingCompactionDoesNotFencePeerUsage()
    {
        await using DispatchHostScope scope = new(context);
        DispatchSessionControl host = scope.Host;
        CreditReservation own = host.Reserve("worker1");
        CreditReservation peer = host.Reserve("worker2");
        INativeWorker one = host.Factory.Create(own, calibration: false);
        INativeWorker two = host.Factory.Create(peer, calibration: false);
        await one.StartAsync(host.State.Cwd("worker1"), null, context.CancellationToken);
        await two.StartAsync(host.State.Cwd("worker2"), null, context.CancellationToken);
        await one.PromptAsync("First session turn.", context.CancellationToken);
        await two.PromptAsync("Second session turn.", context.CancellationToken);
        scope.StartCompaction(host.Sessions[0]);

        Task verification = scope.Track(
            two.VerifyCreditsAsync(scope.Token),
            "peer verification"
        );
        await verification.WaitAsync(TimeSpan.FromSeconds(5), context.CancellationToken);

        Assert.AreEqual(7.25m, host.State.Read(peer).Actual);
        Assert.IsFalse(host.State.Read(peer).GrantOpen);
        Assert.IsTrue(host.State.Read(own).GrantOpen);
        Assert.IsNull(host.State.Read(own).Actual);
        scope.CompleteCompaction(host.Sessions[0]);
        await host.Factory.CompleteAsync();
        Assert.AreEqual(14.5m, host.State.Accounting["actualReported"]!.GetValue<decimal>());
        scope.Complete();
    }

    [TestMethod]
    public async Task CompactionReadFenceStopWaitsForOutstandingWorkBeforeHostSettlement()
    {
        await using DispatchHostScope scope = new(context, holdHost: true);
        DispatchSessionControl host = scope.Host;
        TaskCompletionSource releaseAbort = new(
            TaskCreationOptions.RunContinuationsAsynchronously
        );
        scope.OnUnwindRelease(() => releaseAbort.TrySetResult());
        CreditReservation own = host.Reserve("worker1");
        INativeWorker worker = host.Factory.Create(own, calibration: false);
        await worker.StartAsync(host.State.Cwd("worker1"), null, context.CancellationToken);
        await worker.PromptAsync(
            "Stop still needs compaction settlement.",
            context.CancellationToken
        );
        scope.StartCompaction(host.Sessions[0]);
        host.ReadStopAtHostEntry = () => new(
            host.Sessions[0].Closed,
            WorkerStatus(
                host.State.Database, host.Factory, "worker1"
            )["lifecycle"]!.GetValue<string>(),
            host.State.Read(own)
        );
        host.Sessions[0].AbortWait = releaseAbort.Task;
        Task<bool> finalRead = host.Sessions[0].ArmFence();

        scope.BeginCompletion();
        await host.Sessions[0].AbortEntered.Task.WaitAsync(
            TimeSpan.FromSeconds(5),
            context.CancellationToken
        );
        releaseAbort.TrySetResult();
        bool abortCompletedAtEntry = await finalRead.WaitAsync(
            TimeSpan.FromSeconds(5),
            context.CancellationToken
        );
        Assert.IsTrue(
            abortCompletedAtEntry,
            "Native cancellation must complete before final compaction/usage reads."
        );
        Assert.AreEqual("active", host.State.Read(own).State);
        Assert.IsTrue(host.State.Read(own).GrantOpen);
        Assert.IsFalse(host.Sessions[0].Closed);
        host.Sessions[0].Publish(47_000_000_000, "complete", 1);
        scope.CompleteCompaction(host.Sessions[0]);
        await scope.WaitHostEnteredAsync();
        DispatchSessionControl.HostStopEntry? entry = host.StopAtHostEntry;
        Assert.IsNotNull(entry);
        Assert.IsTrue(entry.SessionClosed);
        Assert.AreEqual("stopped", entry.WorkerLifecycle);
        Assert.AreEqual(own.Id, entry.Reservation.Id);
        Assert.AreEqual(47m, entry.Reservation.Actual);
        Assert.IsTrue(entry.Reservation.GrantOpen);

        await scope.ReleaseHostAndAwaitAsync();

        Assert.AreEqual("reconciled", host.State.Read(own).State);
        Assert.IsFalse(host.State.Read(own).GrantOpen);
        Assert.AreEqual(47m, host.State.Read(own).Actual);
        scope.Complete();
    }

    [TestMethod]
    public async Task CompactionReadFenceMissingTerminalHitsExistingBoundAndRetainsUncertainty()
    {
        ManualScenarioClock settlementClock = new();
        await using DispatchHostScope scope = new(context, settlementTimeProvider: settlementClock);
        DispatchSessionControl host = scope.Host;
        bool deadlineAdvanced = false;
        CreditReservation own = host.Reserve("worker1");
        CreditReservation peer = host.Reserve("worker2");
        INativeWorker one = host.Factory.Create(own, calibration: false);
        INativeWorker two = host.Factory.Create(peer, calibration: false);
        await one.StartAsync(host.State.Cwd("worker1"), null, context.CancellationToken);
        await two.StartAsync(host.State.Cwd("worker2"), null, context.CancellationToken);
        await one.PromptAsync("Missing background terminal.", context.CancellationToken);
        await two.PromptAsync("Peer remains measured.", context.CancellationToken);
        host.Sessions[0].CompactionStart();
        Task finalReadEntered = host.Sessions[0].ArmFence();
        scope.BeginCompletion();
        scope.OnUnwindBeforeJoin(async () =>
        {
            if (!deadlineAdvanced)
            {
                await finalReadEntered.WaitAsync(TimeSpan.FromSeconds(5), CancellationToken.None);
                settlementClock.Advance(TimeSpan.FromSeconds(30));
            }
        });
        await finalReadEntered.WaitAsync(TimeSpan.FromSeconds(5), context.CancellationToken);
        settlementClock.Advance(TimeSpan.FromSeconds(30));
        deadlineAdvanced = true;

        OperationException error = await Assert.ThrowsExactlyAsync<OperationException>(() =>
            scope.AwaitCompletionAsync(TimeSpan.FromSeconds(5))
        );

        Assert.AreEqual("credit_or_ownership_unknown", error.Code);
        Assert.Contains("Native cohort settlement retained unresolved evidence", error.Message);
        Assert.AreEqual("uncertain", host.State.Read(own).State);
        Assert.IsTrue(host.State.Read(own).GrantOpen);
        Assert.IsNull(host.State.Read(own).Actual);
        Assert.IsTrue(host.Sessions[0].Closed);
        Assert.AreEqual("reconciled", host.State.Read(peer).State);
        Assert.AreEqual(7.25m, host.State.Read(peer).Actual);
        JsonObject detail = Assert.ContainsSingle(
            host.Factory.SettlementDetails.OfType<JsonObject>()
                .Where(item =>
                    item["worker"]?.GetValue<string>() == own.Worker
                    && item["reservationId"]?.GetValue<string>() == own.Id
                    && item["phase"]?.GetValue<string>() == "stop"
                )
        );
        JsonObject stop = Assert.IsInstanceOfType<JsonObject>(detail["error"]);
        Assert.AreEqual(
            "credit_or_ownership_unknown", stop["operationCode"]!.GetValue<string>()
        );
        JsonObject cause = Assert.IsInstanceOfType<JsonObject>(stop["innerException"]);
        Type? causeType = typeof(OperationCanceledException).Assembly.GetType(
            cause["type"]!.GetValue<string>()
        );
        Assert.IsNotNull(causeType, cause.ToJsonString());
        Assert.IsTrue(
            typeof(OperationCanceledException).IsAssignableFrom(causeType),
            cause.ToJsonString()
        );
        scope.Complete();
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task SerializedNativeEvidenceContractFreshAdmissionConsumesMatchedBinding(
        bool mismatch
    )
    {
        await using DispatchHostScope scope = new(context, holdHost: true);
        DispatchSessionControl host = scope.Host;
        string scratchRoot = host.State.Cwd(string.Empty);
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
        await two.PromptAsync("Keep separate measured evidence.", context.CancellationToken);
        string key = $"native:session:{own.SessionId}";

        await scope.EnterHostBarrierAsync();
        Assert.IsTrue(host.Sessions.All(session => session.Closed));
        Assert.AreEqual(
            "stopped",
            WorkerStatus(
                host.State.Database, host.Factory, "worker1"
            )["lifecycle"]!.GetValue<string>()
        );
        Assert.AreEqual("active", host.State.Read(own).State);
        Assert.IsTrue(host.State.Read(own).GrantOpen);
        Assert.AreEqual(100m, host.State.Read(own).Allowance);
        Assert.IsNull(host.State.Read(own).Actual);
        Assert.AreEqual(7.25m, host.State.Read(peer).Actual);
        Assert.IsTrue(host.State.Read(peer).GrantOpen);
        Assert.AreEqual(
            192.75m,
            host.State.Accounting["outstandingAuthorization"]!.GetValue<decimal>()
        );
        Assert.AreEqual(
            "running",
            host.State.RunValue(
                "SELECT state FROM process_leases WHERE kind=$id;",
                "native:runtime:" + host.Id
            )
        );
        Assert.AreEqual(0L, Dispatches(host.State, own));
        Assert.AreEqual(0L, host.Sessions[0].Current.TotalUserRequests);
        Assert.IsNull(host.Sessions[0].AcceptedText);
        Assert.IsEmpty(host.State.Accounting["cancellations"]!.AsArray());
        JsonObject diagnostic = JsonNode
            .Parse(host.State.Database.ReadMetadata(key)!)!
            .AsObject();
        AssertFreshAdmissionDiagnostic(diagnostic, host, own, cwd);
        context.WriteLine(
            $"Phase4AdmissionBarrier: {new JsonObject
            {
                ["mismatch"] = mismatch,
                ["runId"] = host.State.Database.RunId,
                ["reservationId"] = own.Id,
                ["sessionId"] = own.SessionId,
                ["runtimeId"] = host.Id,
                ["leaseId"] = host.LeaseId,
                ["pid"] = host.ReadyIdentity.Pid,
                ["birthTicks"] = host.ReadyIdentity.BirthTicks,
                ["scratchRoot"] = scratchRoot,
                ["diagnostic"] = diagnostic.DeepClone(),
            }.ToJsonString()}"
        );
        if (mismatch)
        {
            // This is the only changed producer field, after Stop saved it.
            diagnostic["sessionId"] = "contradictory-serialized-session";
            host.State.Database.WriteMetadata(key, diagnostic.ToJsonString());
        }

        OperationException? failure = mismatch
            ? await Assert.ThrowsExactlyAsync<OperationException>(scope.ReleaseHostAndAwaitAsync)
            : null;
        if (!mismatch)
        {
            await scope.ReleaseHostAndAwaitAsync();
        }

        Assert.AreEqual(
            "exited",
            host.State.RunValue(
                "SELECT state FROM process_leases WHERE kind=$id;",
                "native:runtime:" + host.Id
            )
        );
        Assert.AreEqual(0L, Dispatches(host.State, own));
        Assert.IsNull(host.State.Read(own).Actual);
        Assert.AreEqual(100m, host.State.Read(own).Allowance);
        Assert.AreEqual("reconciled", host.State.Read(peer).State);
        Assert.AreEqual(7.25m, host.State.Read(peer).Actual);
        Assert.AreEqual(100m, host.State.Read(peer).Allowance);
        Assert.AreEqual(7.25m, host.State.Accounting["actualReported"]!.GetValue<decimal>());
        Assert.IsFalse(host.State.Ledger.HasCompleteSdkUsage(own.Id));
        Assert.IsTrue(host.State.Ledger.HasCompleteSdkUsage(peer.Id));
        if (mismatch)
        {
            OperationException rejected = Assert.IsInstanceOfType<OperationException>(failure);
            Assert.AreEqual("credit_or_ownership_unknown", rejected.Code);
            Assert.AreEqual(
                "Native cohort settlement retained unresolved evidence: "
                    + "Observed session identity, locality, model or limit does not match.",
                rejected.Message
            );
            Assert.AreEqual("uncertain", host.State.Read(own).State);
            Assert.IsTrue(host.State.Read(own).GrantOpen);
            Assert.AreEqual(
                100m,
                host.State.Accounting["outstandingAuthorization"]!.GetValue<decimal>()
            );
            Assert.IsEmpty(host.State.Accounting["cancellations"]!.AsArray());
            JsonObject saved = JsonNode
                .Parse(host.State.Database.ReadMetadata(key)!)!
                .AsObject();
            Assert.AreEqual(
                "contradictory-serialized-session",
                saved["sessionId"]!.GetValue<string>()
            );
            Assert.AreEqual("0", saved["reportedNanoAiu"]!.GetValue<string>());
            Assert.IsNull(saved["aiCreditsStatus"]);
            Assert.AreEqual(0L, saved["totalUserRequests"]!.GetValue<long>());
            Assert.AreEqual(
                own.SessionId,
                host.State.RunValue(
                    "SELECT session_id FROM native_attempts WHERE reservation_id=$id;",
                    own.Id
                )
            );
            Assert.AreEqual(
                own.SessionId,
                host.State.RunValue(
                    "SELECT session_id FROM native_sessions WHERE reservation_id=$id;",
                    own.Id
                )
            );
            context.WriteLine($"Phase4AdmissionRejected: {rejected.Code}: {rejected.Message}");
        }
        else
        {
            Assert.AreEqual("released", host.State.Read(own).State);
            Assert.IsFalse(host.State.Read(own).GrantOpen);
            Assert.AreEqual(
                0m,
                host.State.Accounting["outstandingAuthorization"]!.GetValue<decimal>()
            );
            JsonObject cancelled = Assert.ContainsSingle(
                host.State.Accounting["cancellations"]!.AsArray()
            )!.AsObject();
            Assert.AreEqual(own.Id, cancelled["reservationId"]!.GetValue<string>());
            Assert.AreEqual(
                "verified-before-first-dispatch",
                cancelled["classification"]!.GetValue<string>()
            );
            Assert.AreEqual("cohort-barrier", cancelled["inspection"]!.GetValue<string>());
            Assert.IsNull(cancelled["measuredActual"]);

            JsonObject repeated = NoDispatchCancellation.Cancel(
                host.State.Database,
                host.State.Ledger,
                own.Id,
                retained: false
            );
            Assert.AreEqual(
                "verified-before-first-dispatch",
                repeated["classification"]!.GetValue<string>()
            );
            Assert.IsTrue(repeated["idempotent"]!.GetValue<bool>());
            Assert.IsNull(repeated["measuredActual"]);
            Assert.AreEqual("0", repeated["reportedNanoAiu"]!.GetValue<string>());
            Assert.IsNull(repeated["aiCreditsStatus"]);

            JsonObject proof = repeated["proof"]!.AsObject();
            Assert.AreEqual("write-ahead-dispatch-v1", proof["contract"]!.GetValue<string>());
            Assert.AreEqual(host.State.Database.RunId, proof["runId"]!.GetValue<string>());
            Assert.AreEqual(own.Id, proof["reservationId"]!.GetValue<string>());
            Assert.AreEqual("worker1", proof["worker"]!.GetValue<string>());
            Assert.AreEqual(own.SessionId, proof["sessionId"]!.GetValue<string>());
            foreach (string association in new[] { "association", "attempt" })
            {
                Assert.AreEqual(
                    own.Id,
                    proof[association]!["reservation_id"]!.GetValue<string>()
                );
                Assert.AreEqual("worker1", proof[association]!["worker"]!.GetValue<string>());
                Assert.AreEqual(
                    own.SessionId,
                    proof[association]!["session_id"]!.GetValue<string>()
                );
                Assert.AreEqual(
                    "sdk:" + NativeSdkSession.SdkVersion,
                    proof[association]!["initialize_json"]!.GetValue<string>()
                );
            }
            Assert.AreEqual(own.UsagePath, proof["attempt"]!["usage_path"]!.GetValue<string>());
            Assert.AreEqual("0", proof["attempt"]!["baseline_usage"]!.GetValue<string>());
            Assert.AreEqual(host.Id, proof["runtime"]!["runtimeId"]!.GetValue<string>());
            Assert.AreEqual(
                host.LeaseId,
                proof["runtime"]!["runtimeLeaseId"]!.GetValue<long>()
            );
            Assert.AreEqual(
                OfflineNativeControl.RuntimeVersion,
                proof["runtime"]!["runtimeVersion"]!.GetValue<string>()
            );
            Assert.AreEqual("settled", proof["runtime"]!["state"]!.GetValue<string>());
            Assert.AreEqual(
                "controlled-native-dependency",
                proof["runtime"]!["evidenceKind"]!.GetValue<string>()
            );
            Assert.AreEqual(
                host.Id,
                proof["runtimeAssociation"]!["runtimeId"]!.GetValue<string>()
            );
            Assert.AreEqual(
                host.LeaseId,
                proof["runtimeAssociation"]!["runtimeLeaseId"]!.GetValue<long>()
            );
            Assert.AreEqual(
                own.SessionId,
                proof["runtimeAssociation"]!["sessionId"]!.GetValue<string>()
            );
            Assert.AreEqual(
                "worker1",
                proof["runtimeAssociation"]!["worker"]!.GetValue<string>()
            );
            Assert.AreEqual(
                cwd,
                proof["runtimeAssociation"]!["workingDirectory"]!.GetValue<string>()
            );
            Assert.AreEqual(host.LeaseId, proof["runtimeLease"]!["id"]!.GetValue<long>());
            Assert.AreEqual(
                (long)host.ReadyIdentity.Pid,
                proof["runtimeLease"]!["pid"]!.GetValue<long>()
            );
            Assert.AreEqual(
                host.ReadyIdentity.BirthTicks,
                proof["runtimeLease"]!["start_ticks"]!.GetValue<long>()
            );
            Assert.AreEqual(
                "native:runtime:" + host.Id,
                proof["runtimeLease"]!["kind"]!.GetValue<string>()
            );
            Assert.AreEqual("exited", proof["runtimeLease"]!["state"]!.GetValue<string>());
            JsonNode source = proof["source"]!;
            Assert.AreEqual("write-ahead-dispatch-v1", source["contract"]!.GetValue<string>());
            Assert.AreEqual(
                "controlled-native-dependency",
                source["evidenceKind"]!.GetValue<string>()
            );
            Assert.AreEqual(host.State.Database.RunId, source["runId"]!.GetValue<string>());
            Assert.AreEqual(own.Id, source["reservationId"]!.GetValue<string>());
            Assert.AreEqual(own.SessionId, source["sessionId"]!.GetValue<string>());
            Assert.AreEqual(cwd, source["workingDirectory"]!.GetValue<string>());
            Assert.AreEqual(0L, source["turnBaseline"]!.GetValue<long>());
            AssertFreshAdmissionDiagnostic(source["initial"]!, host, own, cwd);
            AssertFreshAdmissionDiagnostic(proof["diagnostic"]!, host, own, cwd);
            context.WriteLine($"Phase4AdmissionPersistedProof: {proof.ToJsonString()}");
            Assert.HasCount(1, host.State.Accounting["cancellations"]!.AsArray());
            Assert.IsFalse(host.State.Read(own).GrantOpen);
            Assert.IsNull(host.State.Read(own).Actual);
            Assert.AreEqual(7.25m, host.State.Read(peer).Actual);
        }

        await scope.CleanupAndAssertScratchRemovedAsync(scratchRoot);
    }

    private static void AssertFreshAdmissionDiagnostic(
        JsonNode diagnostic,
        DispatchSessionControl host,
        CreditReservation own,
        string cwd
    )
    {
        Assert.AreEqual(own.SessionId, diagnostic["sessionId"]!.GetValue<string>());
        Assert.AreEqual(host.Id, diagnostic["runtimeId"]!.GetValue<string>());
        Assert.AreEqual(host.LeaseId, diagnostic["runtimeLeaseId"]!.GetValue<long>());
        Assert.AreEqual(NativeSdkSession.SdkVersion, diagnostic["sdkVersion"]!.GetValue<string>());
        Assert.AreEqual(
            OfflineNativeControl.RuntimeVersion,
            diagnostic["runtimeVersion"]!.GetValue<string>()
        );
        Assert.AreEqual(cwd, diagnostic["workingDirectory"]!.GetValue<string>());
        Assert.AreEqual("controlled-model", diagnostic["selectedModel"]!.GetValue<string>());
        Assert.AreEqual(100d, diagnostic["appliedLimit"]!.GetValue<double>());
        Assert.IsTrue(diagnostic["paidReservation"]!.GetValue<bool>());
        Assert.IsFalse(diagnostic["alreadyInUse"]!.GetValue<bool>());
        Assert.IsFalse(diagnostic["isRemote"]!.GetValue<bool>());
        Assert.AreEqual("0", diagnostic["reportedNanoAiu"]!.GetValue<string>());
        Assert.IsNull(diagnostic["aiCreditsStatus"]);
        Assert.AreEqual(0L, diagnostic["totalUserRequests"]!.GetValue<long>());
    }

    [TestMethod]
    public async Task
    PeerIsolationAndHostLifetimeUnopenedExactFailureAllowsLabeledFreshFallbackBesidePeer()
    {
        await using DispatchHostScope scope = new(context);
        DispatchSessionControl original = scope.Host;
        CreditReservation old = original.Reserve("worker1", shared: false);
        INativeWorker worker = original.Factory.Create(old, calibration: false);
        await worker.StartAsync(original.State.Cwd("worker1"), null, context.CancellationToken);
        await worker.PromptAsync("Old complete baseline.", context.CancellationToken);
        await original.Factory.CompleteAsync();
        CreditReservation resumed = original.State.Ledger.ContinueExact(
            old.Id,
            original.State.Cwd("next.usage")
        );
        CreditReservation peer = original.Reserve("worker2");
        DispatchSessionControl next = new(original.State) { FailExactOpen = true };
        INativeWorker two = next.Factory.Create(peer, calibration: false);
        INativeWorker loading = next.Factory.Create(resumed, calibration: false);
        await two.StartAsync(next.State.Cwd("worker2"), null, context.CancellationToken);

        OperationException error = await Assert.ThrowsExactlyAsync<OperationException>(
            async () =>
                await loading.StartAsync(
                    next.State.Cwd("worker1"),
                    old.SessionId,
                    context.CancellationToken
                )
        );

        Assert.AreEqual("session_load_failed", error.Code);
        await loading.StopAsync();
        Assert.AreEqual(7.25m, next.State.Read(old).Actual);
        Assert.AreEqual("reconciled", next.State.Read(old).State);
        CreditReservation fresh = next.State.Ledger.Reserve(
            next.State.Database.RunId,
            "worker1",
            0,
            400,
            next.State.Cwd("worker1.fallback." + Guid.NewGuid().ToString("N") + ".usage"),
            shared: true
        );
        INativeWorker fallback = next.Factory.Create(fresh, calibration: false);
        await fallback.StartAsync(
            next.State.Cwd("worker1"),
            null,
            context.CancellationToken,
            recoveryLabel: "fresh-summary-fallback: exact-session load unavailable or failed"
        );
        Assert.AreNotEqual(old.SessionId, fallback.SessionId);
        Assert.AreEqual(
            "fresh-summary-fallback: exact-session load unavailable or failed",
            WorkerStatus(
                next.State.Database, next.Factory, "worker1"
            )["recovery"]!.GetValue<string>()
        );
        await fallback.PromptAsync(
            "Fresh summary after explicit fallback.",
            context.CancellationToken
        );
        JsonObject later = await two.PromptAsync(
            "Healthy peer after fallback.",
            context.CancellationToken
        );
        Assert.AreEqual(peer.SessionId, later["sessionId"]!.GetValue<string>());
        Assert.AreEqual("worker2", later["actor"]!.GetValue<string>());
        await next.Factory.CompleteAsync();
        Assert.AreEqual("reconciled", next.State.Read(fresh).State);
        Assert.AreEqual("reconciled", next.State.Read(peer).State);
        Assert.AreEqual(7.25m, next.State.Read(peer).Actual);
        Assert.AreEqual(21.75m, next.State.Accounting["actualReported"]!.GetValue<decimal>());
        scope.Complete();
    }

    [TestMethod]
    public async Task
    PeerIsolationAndHostLifetimeOwnerCancellationStopsCohortWithoutRefundingPendingSends()
    {
        await using DispatchHostScope scope = new(context);
        DispatchSessionControl host = scope.Host;
        CancellationToken ownerToken = scope.CreateOwnerToken();
        TimeSpan observerFuse = TimeSpan.FromSeconds(5);
        CreditReservation own = host.Reserve("worker1");
        CreditReservation second = host.Reserve("worker2");
        CreditReservation measured = host.Reserve("worker3");
        INativeWorker one = host.Factory.Create(own, calibration: false);
        INativeWorker two = host.Factory.Create(second, calibration: false);
        INativeWorker three = host.Factory.Create(measured, calibration: false);
        one.BindOwner(ownerToken);
        two.BindOwner(ownerToken);
        await one.StartAsync(host.State.Cwd("worker1"), null, context.CancellationToken);
        await two.StartAsync(host.State.Cwd("worker2"), null, context.CancellationToken);
        await three.StartAsync(host.State.Cwd("worker3"), null, context.CancellationToken);
        await three.PromptAsync("Already measured unrelated peer.", context.CancellationToken);
        DispatchSessionControl.Session firstSession = host.Sessions[0];
        DispatchSessionControl.Session secondSession = host.Sessions[1];
        firstSession.HoldSend = true;
        secondSession.HoldSend = true;
        Task<JsonObject> firstSend = scope.Track(
            one.PromptAsync("First pending cohort send.", context.CancellationToken),
            "first held send"
        );
        Task<JsonObject> secondSend = scope.Track(
            two.PromptAsync("Second pending cohort send.", context.CancellationToken),
            "second held send"
        );
        await Task.WhenAll(firstSession.SendEntered.Task, secondSession.SendEntered.Task)
            .WaitAsync(observerFuse, context.CancellationToken);

        await scope.CancelOwnerAsync().WaitAsync(observerFuse, CancellationToken.None);
        await AssertOwnerCanceledSendAsync(firstSend);
        await AssertOwnerCanceledSendAsync(secondSend);
        OperationException error = await Assert.ThrowsExactlyAsync<OperationException>(() =>
            scope.AwaitCompletionAsync()
        );

        Assert.Contains("Native send has no delivered terminal boundary", error.Message);
        foreach (CreditReservation pending in new[] { own, second })
        {
            JsonObject detail = Assert.ContainsSingle(
                host.Factory.SettlementDetails.OfType<JsonObject>()
                    .Where(item =>
                        item["worker"]?.GetValue<string>() == pending.Worker
                        && item["reservationId"]?.GetValue<string>() == pending.Id
                        && item["phase"]?.GetValue<string>() == "stop"
                    )
            );
            JsonObject cause = detail["error"]!["innerException"]!.AsObject();
            Assert.AreEqual("credit_unknown", cause["operationCode"]!.GetValue<string>());
            Assert.Contains(
                "Native send has no delivered terminal boundary",
                cause["message"]!.GetValue<string>()
            );
        }
        Assert.AreEqual("uncertain", host.State.Read(own).State);
        Assert.AreEqual("uncertain", host.State.Read(second).State);
        Assert.IsTrue(host.State.Read(own).GrantOpen);
        Assert.IsTrue(host.State.Read(second).GrantOpen);
        Assert.IsNull(host.State.Read(own).Actual);
        Assert.IsNull(host.State.Read(second).Actual);
        Assert.AreEqual(7.25m, host.State.Read(measured).Actual);
        Assert.AreEqual("reconciled", host.State.Read(measured).State);
        Assert.AreEqual(
            200m,
            host.State.Accounting["outstandingAuthorization"]!.GetValue<decimal>()
        );
        scope.Complete();

        async Task AssertOwnerCanceledSendAsync(Task<JsonObject> send)
        {
            // The fuse observes completion; only the original send supplies cancellation.
            _ = await Task.WhenAny(send).WaitAsync(observerFuse, CancellationToken.None);
            OperationCanceledException canceled =
                await Assert.ThrowsAsync<OperationCanceledException>(() => send);
            Assert.IsTrue(send.IsCanceled);
            Assert.IsTrue(canceled.CancellationToken.IsCancellationRequested);
            Assert.IsFalse(context.CancellationToken.IsCancellationRequested);
        }
    }

    private static JsonObject WorkerStatus(
        RunDatabase database,
        INativeWorkerFactory factory,
        string worker
    )
    {
        CollaborationOperations collaboration = new(database);
        Coordinator coordinator = new(
            database,
            collaboration,
            new(database, collaboration),
            (_, _, _) => factory
        );
        return GitScenarioState.Data(coordinator.Status())["workers"]!
            .AsArray()
            .OfType<JsonObject>()
            .Single(item => item["id"]!.GetValue<string>() == worker);
    }

    private sealed class TypedSettlementScope(TestContext context) : IAsyncDisposable
    {
        private Task? completion;
        private bool completionObserved;
        private bool completed;

        internal TypedHostControl Host { get; } = new() { HoldSettlement = true };

        internal async Task EnterBarrierAsync()
        {
            completion = Host.Factory.CompleteAsync();
            await Host.SettlementEntered.Task.WaitAsync(
                TimeSpan.FromSeconds(5),
                context.CancellationToken
            );
        }

        internal async Task ReleaseAndAwaitAsync()
        {
            Host.ReleaseSettlement();
            await completion!.WaitAsync(TimeSpan.FromSeconds(5), context.CancellationToken);
            completionObserved = true;
        }

        internal void Complete() => completed = true;

        public async ValueTask DisposeAsync()
        {
            Host.ReleaseSettlement();
            if (completion is not null && !completionObserved)
            {
                try
                {
                    await completion.WaitAsync(TimeSpan.FromSeconds(5), CancellationToken.None);
                }
                catch (Exception secondary) when (!completed)
                {
                    context.WriteLine("Secondary factory completion failure: {0}", secondary);
                }
            }

            if (completion is { IsCompleted: false })
            {
                context.WriteLine(
                    "Retained state for pending factory completion: {0}",
                    Host.Cwd(string.Empty)
                );
                return;
            }

            try
            {
                Host.Dispose();
            }
            catch (Exception secondary) when (!completed)
            {
                context.WriteLine("Secondary fixture cleanup failure: {0}", secondary);
            }
        }
    }

    private sealed class DispatchHostScope(
        TestContext context,
        TypedHostControl? state = null,
        TimeProvider? settlementTimeProvider = null,
        bool holdHost = false
    ) : IAsyncDisposable
    {
        private static readonly TimeSpan CleanupFuse = TimeSpan.FromSeconds(5);
        private readonly CancellationTokenSource cancellation =
            CancellationTokenSource.CreateLinkedTokenSource(context.CancellationToken);
        private readonly List<(Task Operation, string Description)> operations = [];
        private readonly HashSet<DispatchSessionControl.Session> pendingCompactions = [];
        private readonly List<Action> gateReleases = [];
        private CancellationTokenSource? ownerCancellation;
        private Task? ownerCancelTask;
        private Func<Task>? beforeJoinUnwind;
        private Task? completion;
        private bool completionObserved;
        private bool completed;
        private bool disposed;

        internal DispatchSessionControl Host { get; } =
            new(state, settlementTimeProvider) { HoldHost = holdHost };

        internal CancellationToken Token => cancellation.Token;

        internal Task CancelAsync() => cancellation.CancelAsync();

        internal CancellationToken CreateOwnerToken()
        {
            ownerCancellation ??= new();
            return ownerCancellation.Token;
        }

        internal Task CancelOwnerAsync()
        {
            ownerCancelTask ??= ownerCancellation!.CancelAsync();
            return ownerCancelTask;
        }

        internal TTask Track<TTask>(TTask operation, string description)
            where TTask : Task
        {
            operations.Add((operation, description));
            return operation;
        }

        internal void StartCompaction(DispatchSessionControl.Session session)
        {
            session.CompactionStart();
            pendingCompactions.Add(session);
        }

        internal void CompleteCompaction(
            DispatchSessionControl.Session session,
            bool success = true
        )
        {
            pendingCompactions.Remove(session);
            session.CompactionComplete(success);
        }

        internal void OnUnwindRelease(Action action) => gateReleases.Add(action);

        internal void OnUnwindBeforeJoin(Func<Task> action) => beforeJoinUnwind = action;

        internal void BeginCompletion() => completion = Host.Factory.CompleteAsync();

        internal Task WaitHostEnteredAsync() =>
            Host.HostEntered.Task.WaitAsync(CleanupFuse, context.CancellationToken);

        internal async Task EnterHostBarrierAsync()
        {
            BeginCompletion();
            await WaitHostEnteredAsync();
        }

        internal async Task ReleaseHostAndAwaitAsync()
        {
            Host.ReleaseHost();
            try
            {
                await completion!;
            }
            finally
            {
                if (completion!.IsCompleted)
                {
                    completionObserved = true;
                }
            }
        }

        internal async Task AwaitCompletionAsync(TimeSpan? timeout = null)
        {
            completion ??= Host.Factory.CompleteAsync();
            try
            {
                if (timeout is { } bound)
                {
                    await completion.WaitAsync(bound, context.CancellationToken);
                }
                else
                {
                    await completion;
                }
            }
            finally
            {
                if (completion.IsCompleted)
                {
                    completionObserved = true;
                }
            }
        }

        internal static async Task<JsonObject> ReleaseSendAndAwaitAsync(
            DispatchSessionControl.Session session,
            Task<JsonObject> send
        )
        {
            session.HoldSend = false;
            session.SendRelease.TrySetResult();
            return await send;
        }

        internal void Complete() => completed = true;

        internal async Task CleanupAndAssertScratchRemovedAsync(string scratchRoot)
        {
            completed = true;
            await DisposeCoreAsync();
            bool remains = Directory.Exists(scratchRoot);
            context.WriteLine($"Phase4ManagedCleanup: {scratchRoot}; exists={remains}");
            Assert.IsFalse(remains);
        }

        public async ValueTask DisposeAsync() => await DisposeCoreAsync();

        private async Task DisposeCoreAsync()
        {
            if (disposed)
            {
                return;
            }
            disposed = true;
            bool deferred = false;
            try
            {
                foreach (Action release in gateReleases)
                {
                    release();
                }

                // Exceptional release is explicitly failed work, never fabricated
                // successful compaction or a replacement accounting measurement.
                foreach (DispatchSessionControl.Session session in pendingCompactions)
                {
                    try
                    {
                        session.CompactionComplete(success: false);
                    }
                    catch (Exception secondary) when (!completed)
                    {
                        context.WriteLine("Secondary compaction cleanup failure: {0}", secondary);
                    }
                }
                pendingCompactions.Clear();
                Host.ReleaseHost();

                if (!completed)
                {
                    await UnwindPendingWorkAsync();
                }

                Task[] unsettled = new[] { completion, ownerCancelTask }
                    .OfType<Task>()
                    .Concat(operations.Select(item => item.Operation))
                    .Where(item => !item.IsCompleted)
                    .ToArray();
                if (unsettled.Length != 0)
                {
                    deferred = true;
                    context.WriteLine(
                        "Retained state for pending operations: {0}",
                        Host.State.Cwd(string.Empty)
                    );
                    _ = DeferHostCleanupAsync(
                        unsettled,
                        Host,
                        cancellation,
                        ownerCancellation
                    );
                    return;
                }

                try
                {
                    await Host.CleanupAsync(preserveState: false);
                }
                catch (Exception secondary) when (!completed)
                {
                    context.WriteLine("Secondary fixture cleanup failure: {0}", secondary);
                }
            }
            finally
            {
                if (!deferred)
                {
                    cancellation.Dispose();
                    ownerCancellation?.Dispose();
                }
            }
        }

        private static async Task DeferHostCleanupAsync(
            Task[] unsettled,
            DispatchSessionControl host,
            CancellationTokenSource cancellation,
            CancellationTokenSource? ownerCancellation
        )
        {
            foreach (Task pending in unsettled)
            {
                try
                {
                    await pending.ConfigureAwait(false);
                }
                catch (Exception) { }
            }
            try
            {
                await host.CleanupAsync(preserveState: false).ConfigureAwait(false);
            }
            catch (Exception error)
            {
                Console.Error.WriteLine(
                    "DISPATCH_HOST_DEFERRED_CLEANUP_SECONDARY " + Json.ExceptionDetails(error)
                );
            }
            finally
            {
                cancellation.Dispose();
                ownerCancellation?.Dispose();
            }
        }

        private async Task UnwindPendingWorkAsync()
        {
            try
            {
                await cancellation.CancelAsync();
            }
            catch (Exception secondary)
            {
                context.WriteLine("Secondary cancellation failure: {0}", secondary);
            }

            using CancellationTokenSource cleanup = new();
            cleanup.Cancel();
            foreach (DispatchSessionControl.Session session in Host.Sessions)
            {
                session.HoldSend = false;
                session.SendRelease.TrySetCanceled(cleanup.Token);
            }

            if (ownerCancellation is not null)
            {
                try
                {
                    ownerCancelTask ??= ownerCancellation.CancelAsync();
                }
                catch (Exception secondary)
                {
                    context.WriteLine("Secondary owner cancellation failure: {0}", secondary);
                }
            }

            if (beforeJoinUnwind is not null)
            {
                try
                {
                    await beforeJoinUnwind();
                }
                catch (Exception secondary)
                {
                    context.WriteLine("Secondary unwind advance failure: {0}", secondary);
                }
            }

            List<(Task Operation, string Description)> tracked = [.. operations];
            if (ownerCancelTask is not null)
            {
                tracked.Add((ownerCancelTask, "owner cancellation"));
            }
            if (completion is not null && !completionObserved)
            {
                tracked.Add((completion, "factory completion"));
            }
            if (tracked.Count == 0)
            {
                return;
            }

            try
            {
                await Task.WhenAll(tracked.Select(item => Task.WhenAny(item.Operation)))
                    .WaitAsync(CleanupFuse, CancellationToken.None);
            }
            catch (TimeoutException secondary)
            {
                context.WriteLine("Secondary owned-operation join timeout: {0}", secondary);
            }

            foreach ((Task operation, string description) in tracked)
            {
                if (!operation.IsCompleted)
                {
                    context.WriteLine("Unjoined owned operation: {0}", description);
                    continue;
                }
                try
                {
                    await operation;
                }
                catch (OperationCanceledException)
                {
                }
                catch (Exception secondary)
                {
                    context.WriteLine(
                        "Secondary owned-operation failure for {0}: {1}",
                        description,
                        secondary
                    );
                }
            }
        }
    }
}
