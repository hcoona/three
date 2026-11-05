using System.Text.Json.Nodes;
using LiteAgensh;
using Microsoft.Data.Sqlite;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace LiteAgensh.Tests;

[TestClass]
public sealed class NativeSettlementScenarioTests(TestContext testContext)
{
    [TestMethod]
    [Timeout(30000, CooperativeCancellation = true)]
    [DataRow(1, "per-session", false)]
    [DataRow(2, "shared", true)]
    public async Task ProductionPreflightRetainsObservedUsageInDiagnosticsAndReceipt(
        int peerCount, string allocation, bool missingMetrics
    )
    {
        CancellationToken token = testContext.CancellationToken;
        using GitScenario fixture = await GitScenario.CreateAsync(token, configuration =>
            configuration with
            {
                WorkerCount = peerCount,
                CreditAllocation = allocation,
                NativeExecutable = Path.Combine(
                    configuration.RepositoryPath, "missing-native-runtime.exe"
                ),
            });
        ControlledSdkHost host = new("none", token, missingMetrics);
        host.Release.TrySetResult();
        Coordinator coordinator = new(
            fixture.Database, fixture.Collaboration, fixture.Operations,
            (database, processes, credits) => new NativeWorkerFactory(
                database, processes, credits, () => host
            )
        );

        JsonObject data = Scenario.Data(await coordinator.PreflightAsync(token));
        JsonObject receipt = JsonNode.Parse(
            fixture.Database.ReadMetadata("native:controlled_preflight_receipt")!
        )!.AsObject();
        JsonArray sessions = data["sessions"]!.AsArray();
        Assert.HasCount(peerCount, sessions);
        Assert.IsTrue(JsonNode.DeepEquals(sessions, receipt["sessions"]));
        Assert.IsTrue(JsonNode.DeepEquals(sessions[0], data["diagnostics"]));
        foreach (JsonNode? session in sessions)
        {
            Assert.IsNotNull(session);
            Assert.IsTrue(session.AsObject().ContainsKey("totalUserRequests"));
            Assert.AreEqual(0L, session["totalUserRequests"]!.GetValue<long>());
            if (missingMetrics)
            {
                Assert.IsTrue(session.AsObject().ContainsKey("reportedNanoAiu"));
                Assert.IsNull(session["reportedNanoAiu"]);
                Assert.IsNull(session["aiCreditsStatus"]);
            }
            else
            {
                Assert.AreEqual("0", session["reportedNanoAiu"]!.GetValue<string>());
                Assert.AreEqual("complete", session["aiCreditsStatus"]!.GetValue<string>());
            }
        }
        Assert.IsTrue(host.Settled);
        Assert.IsTrue(host.Sessions.All(session => session.CloseAttempted));
    }

    [TestMethod]
    [Timeout(30000, CooperativeCancellation = true)]
    [DataRow("per-session", "none")]
    [DataRow("shared", "none")]
    [DataRow("shared", "host")]
    [DataRow("per-session", "session")]
    public async Task ProductionCohortRetainsBackingUntilHostSettlement(
        string allocation, string failure
    )
    {
        CancellationToken token = testContext.CancellationToken;
        using RunFixture fixture = new();
        RunDatabase database = new(fixture.StatePath, fixture.Configuration with
        {
            NativeExecutable = Path.Combine(fixture.RootPath, "missing-native-runtime.exe"),
            CreditAllocation = allocation,
            RunAiCreditAllowance = 300,
            CampaignAiCreditAllowance = 600,
        });
        CollaborationOperations collaboration = new(database);
        _ = new Coordinator(database, collaboration, new(database, collaboration));
        CreditLedger ledger = new(database.Configuration.CampaignLedgerPath, 600);
        ControlledSdkHost host = new(failure, token);
        NativeWorkerFactory factory = new(database, new(database), ledger, () => host);
        List<INativeWorker> peers = [];
        foreach (string worker in new[] { "worker1", "worker2" })
        {
            CreditReservation reservation = ledger.Reserve(
                database.RunId, worker, allocation == "shared" ? 0 : 60, 300,
                Path.Combine(fixture.RootPath, $"usage-{worker}"), shared: allocation == "shared"
            );
            INativeWorker peer = factory.Create(reservation, calibration: false);
            peers.Add(peer);
            await peer.StartAsync(fixture.RepositoryPath, null, token);
        }
        foreach (INativeWorker peer in peers)
        {
            await peer.PromptAsync("Controlled request.", token);
            if (failure == "session")
            {
                await peer.VerifyCreditsAsync(token);
            }
        }
        decimal backing = ledger.Status(database.RunId, 300)["campaignCommitted"]!
            .GetValue<decimal>();
        ledger.RequirePaidAdmission();
        if (failure == "session")
        {
            OperationException stop = await Assert.ThrowsExactlyAsync<OperationException>(
                peers[0].StopAsync
            );
            Assert.Contains("Controlled session disposal failed.", stop.Message);
        }
        else
        {
            await peers[0].StopAsync();
        }
        await peers[1].StopAsync();

        Task completion = factory.CompleteAsync();
        try
        {
            await host.Closing.Task.WaitAsync(token);
            Assert.IsFalse(host.Settled);
            Assert.IsTrue(host.Sessions.All(session => session.CloseAttempted));
            CreditReservation[] staged = ledger.Reservations().ToArray();
            Assert.HasCount(2, staged);
            Assert.IsTrue(staged.All(row => row.State != "reconciled"));
            Assert.IsTrue(staged.All(row => row.Actual == 1m));
            Assert.AreEqual(backing,
                ledger.Status(database.RunId, 300)["campaignCommitted"]!.GetValue<decimal>());
            if (failure == "session")
            {
                Assert.AreEqual("uncertain", staged[0].State);
                Assert.AreEqual("credit_unknown",
                    Assert.ThrowsExactly<OperationException>(ledger.RequirePaidAdmission).Code);
            }

            host.Release.TrySetResult();
            if (failure == "none")
            {
                await completion;
                Assert.IsTrue(ledger.Reservations().All(row => row.State == "reconciled"));
                Assert.AreEqual(2m,
                    ledger.Status(database.RunId, 300)["campaignCommitted"]!.GetValue<decimal>());
                ledger.RequirePaidAdmission();
                Assert.IsEmpty(factory.SettlementFailures);
            }
            else
            {
                await Assert.ThrowsExactlyAsync<OperationException>(() => completion);
                CreditReservation[] retained = ledger.Reservations().ToArray();
                Assert.AreEqual("uncertain", retained[0].State);
                Assert.AreEqual(failure == "host" ? "uncertain" : "reconciled", retained[1].State);
                Assert.AreEqual("credit_unknown",
                    Assert.ThrowsExactly<OperationException>(ledger.RequirePaidAdmission).Code);
                JsonNode? evidence = Assert.ContainsSingle(factory.SettlementFailures);
                Assert.IsNotNull(evidence);
                Assert.AreEqual(failure == "host" ? "native-runtime" : "stop",
                    evidence["phase"]!.GetValue<string>());
                if (failure == "session")
                {
                    Assert.AreEqual(retained[0].Id, evidence["reservationId"]!.GetValue<string>());
                    Assert.AreEqual("worker1", evidence["worker"]!.GetValue<string>());
                    Assert.Contains("Controlled session disposal failed.",
                        evidence["message"]!.GetValue<string>());
                }
            }
            Assert.AreEqual(failure != "host", host.Settled);
            using SqliteConnection connection = database.Open();
            Assert.AreEqual(0L, Sql.Scalar(connection, "SELECT COUNT(*) FROM process_leases;"));
        }
        finally
        {
            host.Release.TrySetResult();
            try
            {
                await completion;
            }
            catch (OperationException)
            {
                // Failed settlement is the observed result, not a cleanup retry.
            }
        }
    }

    // Controlled SDK effects only; production workers, cohort and SQLite accounting remain real.
    private sealed class ControlledSdkHost(
        string failure, CancellationToken token, bool missingMetrics = false
    ) : INativeSdkHost
    {
        public string Id => "controlled-sdk-host";

        public long LeaseId => 1;

        public List<ControlledSdkSession> Sessions { get; } = [];

        public TaskCompletionSource Closing { get; } = new(
            TaskCreationOptions.RunContinuationsAsynchronously
        );

        public TaskCompletionSource Release { get; } = new(
            TaskCreationOptions.RunContinuationsAsynchronously
        );

        public bool Settled { get; private set; }

        public INativeSdkSession CreateSession(string cwd, bool isolated)
        {
            ControlledSdkSession session = new(
                failure == "session" && Sessions.Count == 0, missingMetrics
            );
            Sessions.Add(session);
            return session;
        }

        public async ValueTask DisposeAsync()
        {
            Closing.TrySetResult();
            await Release.Task.WaitAsync(token);
            if (failure == "host")
            {
                throw new OperationException("ownership_unresolved", "Controlled host failed.");
            }
            Settled = true;
        }
    }

    private sealed class ControlledSdkSession(
        bool failDispose, bool missingMetrics = false
    ) : INativeSdkSession
    {
        private long requests;

        public string SessionId { get; private set; } = "";

        public bool HasSession { get; private set; }

        public string? NativeFailure => null;

        public JsonObject? Metadata { get; private set; }

        public bool CloseAttempted { get; private set; }

        public Task OpenAsync(
            string worker, string sessionId, decimal? limit, string? exactSession,
            string evidencePath, CancellationToken cancellationToken
        )
        {
            cancellationToken.ThrowIfCancellationRequested();
            SessionId = sessionId;
            HasSession = true;
            Metadata = new() { ["sessionId"] = sessionId, ["worker"] = worker };
            return Task.CompletedTask;
        }

        public Task<NativeUsage> ReadAsync(
            string expectedSession, decimal? expectedLimit, CancellationToken cancellationToken,
            bool validateEvents = true
        )
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult(new NativeUsage(
                SessionId, missingMetrics ? null : requests * 1_000_000_000d,
                missingMetrics ? null : "complete", requests
            ));
        }

        public Task<NativeUsage> UpdateLimitAsync(
            string expectedSession, decimal limit, CancellationToken cancellationToken
        ) => ReadAsync(expectedSession, limit, cancellationToken);

        public Task<JsonObject> SendAsync(
            string text, TimeSpan timeout, CancellationToken cancellationToken,
            Func<CancellationToken, Task<decimal>>? renewLimit = null
        )
        {
            cancellationToken.ThrowIfCancellationRequested();
            requests++;
            return Task.FromResult(new JsonObject { ["text"] = "Controlled response." });
        }

        public Task AbortAsync(CancellationToken cancellationToken) => Task.CompletedTask;

        public void Quiesce() { }

        public void RequireHealthy() { }

        public ValueTask DisposeAsync()
        {
            CloseAttempted = true;
            if (failDispose)
            {
                throw new OperationException(
                    "ownership_unresolved", "Controlled session disposal failed."
                );
            }
            HasSession = false;
            return ValueTask.CompletedTask;
        }
    }
}
