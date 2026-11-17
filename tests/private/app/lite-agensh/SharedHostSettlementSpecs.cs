using System.Text.Json.Nodes;
using LiteAgensh.Tests.Support;
using Microsoft.Data.Sqlite;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace LiteAgensh.Tests;

[TestClass]
public sealed class SharedHostSettlementSpecs(TestContext context)
{
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
        Assert.AreEqual(own.SessionId, host.Database.ReadMetadata("worker:worker1:session"));
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
            failedTable == "native_attempts"
                ? "Controlled first association write rejected."
                : "Controlled second association write rejected after matching attempt.",
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
        TypedHostControl host = new() { HoldSettlement = true };
        Task? completion = null;
        Exception? primaryFailure = null;
        try
        {
            CreditReservation own = host.Reserve("worker1");
            INativeWorker worker = host.Factory.Create(own, calibration: false);
            await worker.StartAsync(host.Cwd("worker1"), null, context.CancellationToken);
            await worker.PromptAsync("Complete a measured turn.", context.CancellationToken);

            completion = host.Factory.CompleteAsync();
            try
            {
                await host.SettlementEntered.Task.WaitAsync(
                    TimeSpan.FromSeconds(5),
                    context.CancellationToken
                );
                Assert.IsTrue(host.Sessions[0].Closed);
                Assert.AreEqual("stopped", host.Database.ReadMetadata("worker:worker1:lifecycle"));
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
            }
            finally
            {
                host.ReleaseSettlement();
            }
            await completion.WaitAsync(TimeSpan.FromSeconds(5), context.CancellationToken);
            Assert.AreEqual("reconciled", host.Read(own).State);
            Assert.AreEqual(7.25m, host.Read(own).Actual);
            Assert.IsFalse(host.Read(own).GrantOpen);
            Assert.AreEqual(7.25m, host.Accounting["actualReported"]!.GetValue<decimal>());
            Assert.AreEqual(7.25m, host.Accounting["runCommitted"]!.GetValue<decimal>());
            Assert.AreEqual(0m, host.Accounting["outstandingAuthorization"]!.GetValue<decimal>());
        }
        catch (Exception exception)
        {
            primaryFailure = exception;
            throw;
        }
        finally
        {
            host.ReleaseSettlement();
            try
            {
                if (completion is not null)
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
                if (completion is { IsCompleted: false })
                {
                    context.WriteLine(
                        "Retained state for pending factory completion: {0}",
                        host.Cwd(string.Empty)
                    );
                }
                else
                {
                    try
                    {
                        host.Dispose();
                    }
                    catch (Exception exception) when (primaryFailure is not null)
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
        Phase4SessionControl host = new();
        try
        {
            CreditReservation first = host.Reserve("worker1", shared);
            CreditReservation peer = host.Reserve("worker2", shared);
            INativeWorker one = host.Factory.Create(first, calibration: false);
            INativeWorker two = host.Factory.Create(peer, calibration: false);
            await Task.WhenAll(
                one.StartAsync(host.State.Cwd("worker1"), null, context.CancellationToken),
                two.StartAsync(host.State.Cwd("worker2"), null, context.CancellationToken)
            );
            Assert.AreNotEqual(one.SessionId, two.SessionId);
            Assert.AreEqual("worker1", host.Sessions[0].OpenedWorker);
            Assert.AreEqual("worker2", host.Sessions[1].OpenedWorker);
            Assert.AreEqual(host.State.Cwd("worker1"), host.Sessions[0].OpenedCwd);
            Assert.AreEqual(host.State.Cwd("worker2"), host.Sessions[1].OpenedCwd);
            Assert.IsFalse(host.Sessions[0].Isolated);
            await one.PromptAsync("First independent conversation.", context.CancellationToken);
            host.Sessions[0].CompactionStart();
            host.Sessions[0].Publish(9_000_000_000, "complete", 1);
            host.Sessions[0].CompactionComplete();
            await one.StopAsync();
            Assert.IsTrue(host.Sessions[0].Closed);
            Assert.IsFalse(host.Sessions[1].Closed);
            Assert.AreEqual(0d, host.Sessions[1].Current.TotalNanoAiu);
            Assert.IsNull(host.Sessions[1].Current.Status);

            JsonObject result = await two.PromptAsync(
                "A later peer operation.",
                context.CancellationToken
            );
            Assert.AreEqual(peer.SessionId, result["sessionId"]!.GetValue<string>());
            Assert.AreEqual("worker2", result["actor"]!.GetValue<string>());
            Assert.AreEqual(host.State.Cwd("worker2"), result["cwd"]!.GetValue<string>());
            Assert.AreEqual(100m, result["appliedLimit"]!.GetValue<decimal>());
            Assert.AreEqual("A later peer operation.", host.Sessions[1].AcceptedText);
            Assert.AreEqual(1L, Dispatches(host.State, peer));
            await host.Factory.CompleteAsync();
            Assert.AreEqual(9m, host.State.Read(first).Actual);
            Assert.AreEqual(7.25m, host.State.Read(peer).Actual);
            Assert.AreEqual("reconciled", host.State.Read(peer).State);
        }
        finally
        {
            await host.CleanupAsync();
        }
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
        Phase4SessionControl host = new();
        try
        {
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
        }
        finally
        {
            await host.CleanupAsync();
        }
    }

    [TestMethod]
    [DataRow("session")]
    [DataRow("host")]
    public async Task CommonHostBarrierOwnsFinalityFailurePreservesOriginalAndMeasuredPeer(
        string boundary
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
            await one.PromptAsync("First usage.", context.CancellationToken);
            await two.PromptAsync("Measured peer.", context.CancellationToken);
            OperationException original = new(
                "ownership_unresolved",
                "Controlled settlement boundary failed."
            );
            if (boundary == "session")
            {
                host.Sessions[0].StopFailure = original;
            }
            else
            {
                host.HostFailure = original;
            }

            OperationException error = await Assert.ThrowsExactlyAsync<OperationException>(
                host.Factory.CompleteAsync
            );

            Assert.AreEqual("credit_or_ownership_unknown", error.Code);
            Assert.Contains(original.Message, error.Message);
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
            Assert.IsTrue(host.Sessions.All(session => session.Closed));
        }
        finally
        {
            await host.CleanupAsync();
        }
    }

    [TestMethod]
    public async Task
    AssociationCommitBeforeDispatchOwnedTupleAndIndependentCapRenewalPreservePeer()
    {
        Phase4SessionControl host = new();
        string scratchRoot = host.State.Cwd(string.Empty);
        try
        {
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
            Task<JsonObject> first = one.PromptAsync(
                "Observe the owned first admission.",
                context.CancellationToken
            );
            try
            {
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
                Assert.AreEqual(
                    own.Id,
                    host.State.Database.ReadMetadata("worker:worker1:reservation")
                );
                Assert.AreEqual(
                    own.SessionId,
                    host.State.Database.ReadMetadata("worker:worker1:session")
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
                    Phase6NativeControl.RuntimeVersion,
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
                Assert.AreEqual(1L, Dispatches(host.State, own));
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
            }
            finally
            {
                host.Sessions[0].HoldSend = false;
                host.Sessions[0].SendRelease.TrySetResult();
                await first;
            }
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
        }
        finally
        {
            await host.CleanupAsync();
            context.WriteLine(
                $"Phase4ManagedCleanup: {scratchRoot}; exists={Directory.Exists(scratchRoot)}"
            );
            Assert.IsFalse(Directory.Exists(scratchRoot));
        }
    }

    [TestMethod]
    [DataRow("native_attempts")]
    [DataRow("native_sessions")]
    public async Task AssociationCommitBeforeDispatchOwnedWriteRollbackPreservesMeasuredPeer(
        string failedTable
    )
    {
        Phase4SessionControl host = new();
        string scratchRoot = host.State.Cwd(string.Empty);
        try
        {
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
            using SqliteConnection ledger = new(
                new SqliteConnectionStringBuilder
                {
                    DataSource = host.State.Database.Configuration.CampaignLedgerPath,
                    Pooling = false,
                }.ToString()
            );
            ledger.Open();
            Assert.AreEqual(
                0L,
                Sql.Scalar(
                    ledger,
                    "SELECT COUNT(*) FROM credit_reports WHERE reservation_id=$id;",
                    null,
                    ("$id", own.Id)
                )
            );
            Assert.AreEqual(
                1L,
                Sql.Scalar(
                    ledger,
                    "SELECT COUNT(*) FROM credit_reports WHERE reservation_id=$id;",
                    null,
                    ("$id", peer.Id)
                )
            );
            Assert.AreEqual(
                "exited",
                host.State.RunValue(
                    "SELECT state FROM process_leases WHERE kind=$id;",
                    "native:runtime:" + host.Id
                )
            );
        }
        finally
        {
            await host.CleanupAsync();
            context.WriteLine(
                $"Phase4ManagedCleanup: {scratchRoot}; exists={Directory.Exists(scratchRoot)}"
            );
            Assert.IsFalse(Directory.Exists(scratchRoot));
        }
    }

    [TestMethod]
    public async Task
    CompactionReadFencePendingSubtotalFortyRequiresFreshFortySevenBeforeReturningBacking()
    {
        // A tight real pool makes the next grant attributable to the returned 53.
        Phase4SessionControl host = new(new TypedHostControl(creditAllowance: 100));
        try
        {
            CreditReservation own = host.Reserve("worker1");
            CreditReservation peer = host.Reserve("worker2");
            INativeWorker worker = host.Factory.Create(own, calibration: false);
            await worker.StartAsync(host.State.Cwd("worker1"), null, context.CancellationToken);
            host.Sessions[0].TurnNanoAiu = 40_000_000_000;
            await worker.PromptAsync(
                "Complete turn with background work.",
                context.CancellationToken
            );
            host.Sessions[0].CompactionStart();
            Task verification = worker.VerifyCreditsAsync(context.CancellationToken);
            try
            {
                await host.Sessions[0]
                    .FenceEntered.Task.WaitAsync(
                        TimeSpan.FromSeconds(5),
                        context.CancellationToken
                    );
                Assert.AreEqual(40_000_000_000d, host.Sessions[0].Current.TotalNanoAiu);
                Assert.AreEqual("complete", host.Sessions[0].Current.Status);
                Assert.IsTrue(host.State.Read(own).GrantOpen);
                Assert.AreEqual(
                    100m,
                    host.State.Accounting["outstandingAuthorization"]!.GetValue<decimal>()
                );
                Assert.AreEqual(peer, host.State.Read(peer));
            }
            finally
            {
                host.Sessions[0].Publish(47_000_000_000, "complete", 1);
                host.Sessions[0].CompactionComplete();
                await verification;
            }
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
        }
        finally
        {
            await host.CleanupAsync();
        }
    }

    [TestMethod]
    [DataRow("during-read")]
    [DataRow("after-stage")]
    [DataRow("failed-terminal")]
    public async Task CompactionReadFenceChangedOrFailedWorkCannotBeRepairedByHostExit(
        string scenario
    )
    {
        Phase4SessionControl host = new() { HoldHost = true };
        Task? completion = null;
        Exception? primaryFailure = null;
        bool completionObserved = false;
        try
        {
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
            completion = host.Factory.CompleteAsync();
            try
            {
                await host.HostEntered.Task.WaitAsync(
                    TimeSpan.FromSeconds(5),
                    context.CancellationToken
                );
                Assert.IsTrue(host.Sessions.All(session => session.Closed));
                Assert.IsTrue(host.State.Read(own).GrantOpen);
                Assert.IsTrue(host.State.Read(peer).GrantOpen);
                if (scenario == "after-stage")
                {
                    Assert.AreEqual(7.25m, host.State.Read(own).Actual);
                    host.Sessions[0].CompactionStart();
                    host.Sessions[0].CompactionComplete();
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
                        await completion;
                    }
                    catch (Exception exception)
                        when (primaryFailure is not null || completionObserved)
                    {
                        context.WriteLine(
                            "Observed factory completion fault before cleanup: {0}",
                            exception
                        );
                    }
                }
            }
            finally
            {
                try
                {
                    await host.CleanupAsync();
                }
                catch (Exception exception) when (primaryFailure is not null)
                {
                    context.WriteLine("Secondary fixture cleanup failure: {0}", exception);
                }
            }
        }
    }

    [TestMethod]
    public async Task
    CompactionReadFenceCancellationWhilePendingRetainsBackingAndOriginalCancellation()
    {
        Phase4SessionControl host = new();
        using CancellationTokenSource cancellation = new();
        try
        {
            CreditReservation own = host.Reserve("worker1");
            INativeWorker worker = host.Factory.Create(own, calibration: false);
            await worker.StartAsync(host.State.Cwd("worker1"), null, context.CancellationToken);
            await worker.PromptAsync(
                "Turn before cancelled compaction read.",
                context.CancellationToken
            );
            host.Sessions[0].CompactionStart();
            Task verification = worker.VerifyCreditsAsync(cancellation.Token);
            await host.Sessions[0]
                .FenceEntered.Task.WaitAsync(TimeSpan.FromSeconds(5), context.CancellationToken);
            await cancellation.CancelAsync();
            OperationCanceledException error = await Assert.ThrowsAsync<OperationCanceledException>(
                () =>
                    verification
            );
            Assert.IsTrue(error.CancellationToken.IsCancellationRequested);
            Assert.AreEqual("uncertain", host.State.Read(own).State);
            Assert.IsTrue(host.State.Read(own).GrantOpen);
            Assert.IsNull(host.State.Read(own).Actual);
            Assert.AreEqual(1L, Dispatches(host.State, own));
            // End the owned dependency work explicitly; do not retry failed verification.
            host.Sessions[0].CompactionComplete();
        }
        finally
        {
            await host.CleanupAsync();
        }
    }

    [TestMethod]
    public async Task
    SerializedNativeEvidenceContractGeneratedStartAndMeasuredDiagnosticPreserveSemantics()
    {
        Phase4SessionControl host = new();
        try
        {
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
                Phase6NativeControl.RuntimeVersion,
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
        }
        finally
        {
            await host.CleanupAsync();
        }
    }

    [TestMethod]
    public async Task PeerIsolationAndHostLifetimeOnePendingCompactionDoesNotFencePeerUsage()
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
            await one.PromptAsync("First session turn.", context.CancellationToken);
            await two.PromptAsync("Second session turn.", context.CancellationToken);
            host.Sessions[0].CompactionStart();
            await two.VerifyCreditsAsync(context.CancellationToken);
            Assert.AreEqual(7.25m, host.State.Read(peer).Actual);
            Assert.IsFalse(host.State.Read(peer).GrantOpen);
            Assert.IsTrue(host.State.Read(own).GrantOpen);
            Assert.IsNull(host.State.Read(own).Actual);
            host.Sessions[0].CompactionComplete();
            await host.Factory.CompleteAsync();
            Assert.AreEqual(14.5m, host.State.Accounting["actualReported"]!.GetValue<decimal>());
        }
        finally
        {
            await host.CleanupAsync();
        }
    }

    [TestMethod]
    public async Task CompactionReadFenceStopWaitsForOutstandingWorkBeforeHostSettlement()
    {
        Phase4SessionControl host = new() { HoldHost = true };
        Task? completion = null;
        Exception? primaryFailure = null;
        bool ownCompactionPending = false;
        try
        {
            CreditReservation own = host.Reserve("worker1");
            INativeWorker worker = host.Factory.Create(own, calibration: false);
            await worker.StartAsync(host.State.Cwd("worker1"), null, context.CancellationToken);
            await worker.PromptAsync(
                "Stop still needs compaction settlement.",
                context.CancellationToken
            );
            host.Sessions[0].CompactionStart();
            ownCompactionPending = true;
            completion = host.Factory.CompleteAsync();
            try
            {
                await host.Sessions[0]
                    .FenceEntered.Task.WaitAsync(
                        TimeSpan.FromSeconds(5),
                        context.CancellationToken
                    );
                Assert.AreEqual("active", host.State.Read(own).State);
                Assert.IsTrue(host.State.Read(own).GrantOpen);
                Assert.IsFalse(host.Sessions[0].Closed);
                host.Sessions[0].Publish(47_000_000_000, "complete", 1);
                host.Sessions[0].CompactionComplete();
                ownCompactionPending = false;
                await host.HostEntered.Task.WaitAsync(
                    TimeSpan.FromSeconds(5),
                    context.CancellationToken
                );
                Assert.AreEqual(47m, host.State.Read(own).Actual);
                Assert.IsTrue(host.State.Read(own).GrantOpen);
                Assert.IsTrue(host.Sessions[0].Closed);
            }
            finally
            {
                host.ReleaseHost();
            }
            await completion;
            Assert.AreEqual("reconciled", host.State.Read(own).State);
            Assert.IsFalse(host.State.Read(own).GrantOpen);
            Assert.AreEqual(47m, host.State.Read(own).Actual);
        }
        catch (Exception exception)
        {
            primaryFailure = exception;
            throw;
        }
        finally
        {
            // Exceptional release is explicitly failed work, never fabricated
            // successful compaction or a replacement accounting measurement.
            if (ownCompactionPending)
            {
                host.Sessions[0].CompactionComplete(success: false);
            }
            host.ReleaseHost();
            try
            {
                if (completion is not null)
                {
                    try
                    {
                        await completion;
                    }
                    catch (Exception exception) when (primaryFailure is not null)
                    {
                        context.WriteLine(
                            "Secondary factory completion failure before cleanup: {0}",
                            exception
                        );
                    }
                }
            }
            finally
            {
                try
                {
                    await host.CleanupAsync();
                }
                catch (Exception exception) when (primaryFailure is not null)
                {
                    context.WriteLine("Secondary fixture cleanup failure: {0}", exception);
                }
            }
        }
    }

    [TestMethod]
    public async Task CompactionReadFenceMissingTerminalHitsExistingBoundAndRetainsUncertainty()
    {
        ManualScenarioClock settlementClock = new();
        Phase4SessionControl host = new(settlementTimeProvider: settlementClock);
        Task? completion = null;
        Task? finalReadEntered = null;
        Exception? primaryFailure = null;
        bool deadlineAdvanced = false;
        bool completionObserved = false;
        try
        {
            CreditReservation own = host.Reserve("worker1");
            CreditReservation peer = host.Reserve("worker2");
            INativeWorker one = host.Factory.Create(own, calibration: false);
            INativeWorker two = host.Factory.Create(peer, calibration: false);
            await one.StartAsync(host.State.Cwd("worker1"), null, context.CancellationToken);
            await two.StartAsync(host.State.Cwd("worker2"), null, context.CancellationToken);
            await one.PromptAsync("Missing background terminal.", context.CancellationToken);
            await two.PromptAsync("Peer remains measured.", context.CancellationToken);
            host.Sessions[0].CompactionStart();
            finalReadEntered = host.Sessions[0].ArmFence();
            completion = host.Factory.CompleteAsync();
            await finalReadEntered.WaitAsync(TimeSpan.FromSeconds(5), context.CancellationToken);
            settlementClock.Advance(TimeSpan.FromSeconds(30));
            deadlineAdvanced = true;
            OperationException error = await Assert.ThrowsExactlyAsync<OperationException>(() =>
                completion.WaitAsync(TimeSpan.FromSeconds(5), context.CancellationToken)
            );
            completionObserved = true;
            Assert.AreEqual("credit_or_ownership_unknown", error.Code);
            Assert.Contains("Native cohort settlement retained unresolved evidence", error.Message);
            Assert.AreEqual("uncertain", host.State.Read(own).State);
            Assert.IsTrue(host.State.Read(own).GrantOpen);
            Assert.IsNull(host.State.Read(own).Actual);
            Assert.IsTrue(host.Sessions[0].Closed);
            Assert.AreEqual("reconciled", host.State.Read(peer).State);
            Assert.AreEqual(7.25m, host.State.Read(peer).Actual);
            Assert.IsNotEmpty(host.Factory.SettlementDetails);
        }
        catch (Exception exception)
        {
            primaryFailure = exception;
            throw;
        }
        finally
        {
            try
            {
                if (completion is not null)
                {
                    if (!deadlineAdvanced)
                    {
                        await finalReadEntered!.WaitAsync(
                            TimeSpan.FromSeconds(5),
                            CancellationToken.None
                        );
                        settlementClock.Advance(TimeSpan.FromSeconds(30));
                    }
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
    [DataRow(false)]
    [DataRow(true)]
    public async Task SerializedNativeEvidenceContractFreshAdmissionConsumesMatchedBinding(
        bool mismatch
    )
    {
        Phase4SessionControl host = new() { HoldHost = true };
        string scratchRoot = host.State.Cwd(string.Empty);
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
            await two.PromptAsync("Keep separate measured evidence.", context.CancellationToken);
            string key = $"native:session:{own.SessionId}";
            Task completion = host.Factory.CompleteAsync();
            OperationException? failure = null;
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
                }
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
            using SqliteConnection ledger = new(
                new SqliteConnectionStringBuilder
                {
                    DataSource = host.State.Database.Configuration.CampaignLedgerPath,
                    Pooling = false,
                }.ToString()
            );
            ledger.Open();
            Assert.AreEqual(
                0L,
                (long)
                    Sql.Scalar(
                        ledger,
                        "SELECT COUNT(*) FROM credit_reports WHERE reservation_id=$id;",
                        null,
                        ("$id", own.Id)
                    )!
            );
            Assert.AreEqual(
                1L,
                (long)
                    Sql.Scalar(
                        ledger,
                        "SELECT COUNT(*) FROM credit_reports WHERE reservation_id=$id;",
                        null,
                        ("$id", peer.Id)
                    )!
            );
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
                Assert.IsNull(failure, failure?.ToString());
                Assert.AreEqual("released", host.State.Read(own).State);
                Assert.IsFalse(host.State.Read(own).GrantOpen);
                Assert.AreEqual(
                    0m,
                    host.State.Accounting["outstandingAuthorization"]!.GetValue<decimal>()
                );
                Assert.AreEqual(
                    "verified-before-first-dispatch",
                    (string?)
                        Sql.Scalar(
                            ledger,
                            "SELECT reason FROM credit_cancellations WHERE reservation_id=$id;",
                            null,
                            ("$id", own.Id)
                        )
                );
                Assert.AreEqual(
                    "cohort-barrier",
                    (string?)
                        Sql.Scalar(
                            ledger,
                            "SELECT inspection FROM credit_cancellations WHERE reservation_id=$id;",
                            null,
                            ("$id", own.Id)
                        )
                );
                JsonObject proof = JsonNode
                    .Parse(
                        (string)
                            Sql.Scalar(
                                ledger,
                                "SELECT proof FROM credit_cancellations WHERE reservation_id=$id;",
                                null,
                                ("$id", own.Id)
                            )!
                    )!
                    .AsObject();
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
                    Phase6NativeControl.RuntimeVersion,
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
                Assert.HasCount(1, host.State.Accounting["cancellations"]!.AsArray());
                Assert.IsFalse(host.State.Read(own).GrantOpen);
                Assert.IsNull(host.State.Read(own).Actual);
                Assert.AreEqual(7.25m, host.State.Read(peer).Actual);
            }
        }
        finally
        {
            await host.CleanupAsync();
            bool remains = Directory.Exists(scratchRoot);
            context.WriteLine($"Phase4AdmissionCleanup: {scratchRoot}; exists={remains}");
            Assert.IsFalse(remains);
        }
    }

    private static void AssertFreshAdmissionDiagnostic(
        JsonNode diagnostic,
        Phase4SessionControl host,
        CreditReservation own,
        string cwd
    )
    {
        Assert.AreEqual(own.SessionId, diagnostic["sessionId"]!.GetValue<string>());
        Assert.AreEqual(host.Id, diagnostic["runtimeId"]!.GetValue<string>());
        Assert.AreEqual(host.LeaseId, diagnostic["runtimeLeaseId"]!.GetValue<long>());
        Assert.AreEqual(NativeSdkSession.SdkVersion, diagnostic["sdkVersion"]!.GetValue<string>());
        Assert.AreEqual(
            Phase6NativeControl.RuntimeVersion,
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
        Phase4SessionControl original = new();
        try
        {
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
            Phase4SessionControl next = new(original.State) { FailExactOpen = true };
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
                next.State.Database.ReadMetadata("worker:worker1:recovery")
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
        }
        finally
        {
            await original.CleanupAsync();
        }
    }

    [TestMethod]
    public async Task
    PeerIsolationAndHostLifetimeOwnerCancellationStopsCohortWithoutRefundingPendingSends()
    {
        Phase4SessionControl host = new();
        using CancellationTokenSource owner = new();
        Task<JsonObject>? firstSend = null;
        Task<JsonObject>? secondSend = null;
        Task? ownerCancellation = null;
        Task? completion = null;
        Exception? primaryFailure = null;
        bool completionObserved = false;
        try
        {
            CreditReservation own = host.Reserve("worker1");
            CreditReservation second = host.Reserve("worker2");
            CreditReservation measured = host.Reserve("worker3");
            INativeWorker one = host.Factory.Create(own, calibration: false);
            INativeWorker two = host.Factory.Create(second, calibration: false);
            INativeWorker three = host.Factory.Create(measured, calibration: false);
            one.BindOwner(owner.Token);
            two.BindOwner(owner.Token);
            await one.StartAsync(host.State.Cwd("worker1"), null, context.CancellationToken);
            await two.StartAsync(host.State.Cwd("worker2"), null, context.CancellationToken);
            await three.StartAsync(host.State.Cwd("worker3"), null, context.CancellationToken);
            await three.PromptAsync("Already measured unrelated peer.", context.CancellationToken);
            host.Sessions[0].HoldSend = true;
            host.Sessions[1].HoldSend = true;
            firstSend = one.PromptAsync(
                "First pending cohort send.",
                context.CancellationToken
            );
            secondSend = two.PromptAsync(
                "Second pending cohort send.",
                context.CancellationToken
            );
            await Task.WhenAll(host.Sessions[0].SendEntered.Task, host.Sessions[1].SendEntered.Task)
                .WaitAsync(TimeSpan.FromSeconds(5), context.CancellationToken);
            ownerCancellation = owner.CancelAsync();
            await ownerCancellation;
            await Assert.ThrowsAsync<OperationCanceledException>(() => firstSend);
            await Assert.ThrowsAsync<OperationCanceledException>(() => secondSend);
            completion = host.Factory.CompleteAsync();
            OperationException error = await Assert.ThrowsExactlyAsync<OperationException>(() =>
                completion
            );
            completionObserved = true;
            Assert.Contains("Native send has no delivered terminal boundary", error.Message);
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
        }
        catch (Exception exception)
        {
            primaryFailure = exception;
            throw;
        }
        finally
        {
            try
            {
                ownerCancellation ??= owner.CancelAsync();
            }
            catch (Exception exception)
            {
                ownerCancellation = Task.FromException(exception);
            }
            Task[] operations =
            [
                .. new Task?[] { firstSend, secondSend, ownerCancellation, completion }
                    .OfType<Task>(),
            ];
            try
            {
                try
                {
                    await Task.WhenAll(operations)
                        .WaitAsync(TimeSpan.FromSeconds(5), CancellationToken.None);
                }
                catch (Exception) when (
                    operations.All(operation => operation.IsCompleted)
                    && ownerCancellation.IsCompletedSuccessfully
                    && firstSend is not { IsFaulted: true }
                    && secondSend is not { IsFaulted: true }
                    && (completion is null || completionObserved)
                )
                {
                    // Requested send cancellation and the asserted settlement fault are terminal.
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
                    context.WriteLine(
                        "Secondary owned-operation completion failure: {0}", exception
                    );
                }
            }
            finally
            {
                bool preserveState = operations.Any(operation => !operation.IsCompleted);
                if (preserveState)
                {
                    context.WriteLine(
                        "Retained state for pending owned operations: {0}",
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
}
