using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Nodes;
using LiteAgensh.Tests.Support;
using Microsoft.Data.Sqlite;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace LiteAgensh.Tests;

[TestClass]
[DoNotParallelize]
public sealed class TrustedBoundarySpecs(TestContext context)
{
    [TestMethod]
    [TestCategory("HostTrust")]
    [Timeout(120_000, CooperativeCancellation = true)]
    public async Task HostTrustPreservesOwnedFilesAndCheckout()
    {
        using GitScenarioState state = await GitScenarioState
            .CreateAsync(context.CancellationToken)
            .ConfigureAwait(false);
        string home = Path.Combine(state.Database.StatePath, "phase8-owned-home");
        string settings = Path.Combine(home, ".copilot", "settings.json");
        Directory.CreateDirectory(Path.GetDirectoryName(settings)!);
        const string credential = "OWNED_CREDENTIAL_NOT_A_REAL_SECRET_76430";
        string[] files =
        [
            Path.Combine(state.Repository, "AGENTS.md"),
            Path.Combine(home, "credentials.json"),
            settings,
            Path.Combine(home, ".copilot", "policy.json"),
            state.GlobalConfig,
            state.SystemConfig,
        ];
        File.AppendAllText(
            files[0],
            "\nPreserve the owned trust sentinel; never rewrite target instructions.\n"
        );
        File.WriteAllText(files[1], new JsonObject { ["token"] = credential }.ToJsonString());
        File.WriteAllText(settings, """{"phase8":"owned-settings","autoUpdate":false}""");
        File.WriteAllText(
            files[3],
            """{"phase8":"owned-policy","allowRemoteDelivery":false}"""
        );
        Dictionary<string, string> before = files.ToDictionary(file => file, Digest);
        string head = await state
            .GitAsync(state.Repository, context.CancellationToken, "rev-parse", "HEAD")
            .ConfigureAwait(false);
        string status = await state
            .GitAsync(state.Repository, context.CancellationToken, "status", "--porcelain=v1")
            .ConfigureAwait(false);
        await state.Operations.InitializeAsync(context.CancellationToken).ConfigureAwait(false);
        await SerializedToolHost
            .RunAsync(
                state.Database,
                "worker1",
                async host =>
                {
                    JsonObject result = await host.DataAsync(
                            "run_info",
                            new(),
                            context.CancellationToken
                        )
                        .ConfigureAwait(false);
                    Assert.AreEqual(
                        state.Repository,
                        result["repositoryPath"]!.GetValue<string>()
                    );
                    Assert.DoesNotContain(credential, result.ToJsonString());
                    JsonObject rejected = await host.InvokeAsync(
                            "task_write",
                            new()
                            {
                                ["action"] = "create",
                                ["title"] = "Rejected sentinel",
                                ["body"] = "Otherwise valid synthetic task body.",
                                ["credential"] = credential,
                            },
                            context.CancellationToken
                        )
                        .ConfigureAwait(false);
                    Assert.IsFalse(rejected["ok"]!.GetValue<bool>());
                    Assert.AreEqual("invalid_arguments", rejected["code"]!.GetValue<string>());
                    Assert.DoesNotContain(credential, rejected.ToJsonString());
                    JsonObject tasks = await host.DataAsync(
                            "task_list",
                            new(),
                            context.CancellationToken
                        )
                        .ConfigureAwait(false);
                    Assert.AreEqual(
                        1L,
                        Assert.ContainsSingle(tasks["items"]!.AsArray())!["id"]!.GetValue<long>()
                    );
                },
                context.CancellationToken
            )
            .ConfigureAwait(false);
        foreach ((string file, string digest) in before)
        {
            Assert.AreEqual(digest, Digest(file), file);
        }
        Assert.AreEqual(
            head,
            await state
                .GitAsync(state.Repository, context.CancellationToken, "rev-parse", "HEAD")
                .ConfigureAwait(false)
        );
        Assert.AreEqual(
            status,
            await state
                .GitAsync(
                    state.Repository,
                    context.CancellationToken,
                    "status",
                    "--porcelain=v1"
                )
                .ConfigureAwait(false)
        );
    }

    [TestMethod]
    [TestCategory("HostTrust")]
    [Timeout(120_000, CooperativeCancellation = true)]
    public async Task PublicYamlInitFromAnotherDirectoryRetainsGoalAfterSourceRemoval()
    {
        using GitScenarioState state = await GitScenarioState.CreateAsync(
            context.CancellationToken
        );
        string input = state.PathFor("yaml-input");
        string launch = state.PathFor("separate-launch-directory");
        Directory.CreateDirectory(input);
        Directory.CreateDirectory(launch);
        string configuration = Path.Combine(input, "run.yaml");
        string goalPath = Path.Combine(input, "goal.md");
        string retainedState = Path.Combine(input, "initialized-run");
        string unavailableNative = state.PathFor("unavailable-native", "copilot");
        const string goal = "# Public initialization\nRetain this exact original goal.\n";
        File.WriteAllText(goalPath, goal);
        File.WriteAllText(
            configuration,
            $"""
            repositoryPath: ../checkout
            statePath: ./initialized-run
            campaignLedgerPath: ./credits.sqlite
            goalFile: ./goal.md
            workerCount: 1
            creditAllocation: shared
            nativeExecutable: {JsonSerializer.Serialize(unavailableNative)}
            finalChecks: []
            """
        );

        JsonObject initialized = GitScenarioState.Data(
            await SerializedToolHost.InitializeAsync(
                configuration, retainedState, launch, context.CancellationToken
            )
        );
        Assert.AreEqual(retainedState, initialized["statePath"]!.GetValue<string>());
        Assert.AreEqual(state.Repository, initialized["repositoryPath"]!.GetValue<string>());
        string runId = initialized["runId"]!.GetValue<string>();
        File.Delete(goalPath);
        RunDatabase reopened = new(retainedState);
        Assert.AreEqual(runId, reopened.RunId);
        Assert.AreEqual(
            Path.Combine(input, "credits.sqlite"), reopened.Configuration.CampaignLedgerPath
        );
        Assert.AreEqual("shared", reopened.Configuration.CreditAllocation);

        await SerializedToolHost.RunAsync(
            reopened, "owner",
            async host =>
            {
                JsonObject info = await host.DataAsync(
                    "run_info", new(), context.CancellationToken
                );
                Assert.AreEqual(goal, info["goal"]!.GetValue<string>());
                Assert.AreEqual(retainedState, info["statePath"]!.GetValue<string>());
                Assert.AreEqual(state.Repository, info["repositoryPath"]!.GetValue<string>());
            },
            context.CancellationToken
        );
    }

    [TestMethod]
    [DataRow("reconcile")]
    [DataRow("reconnect")]
    [TestCategory("HostTrust")]
    [Timeout(120_000, CooperativeCancellation = true)]
    public async Task MissingOriginalLedgerRefusesPublicRecoveryWithoutReplacement(string command)
    {
        using BoardDeliveryState state = new();
        state.Send("owner", "worker1", "Retain the run whose original ledger is missing.");
        LocalTools tools = new(state.Database);
        _ = new Coordinator(state.Database, tools.Collaboration, tools.Git);
        string ledgerPath = state.Database.Configuration.CampaignLedgerPath;
        CreditLedger ledger = new(
            ledgerPath,
            state.Database.Configuration.CampaignAiCreditAllowance
        );
        CreditReservation reservation = ledger.Reserve(
            state.Database.RunId,
            "worker1",
            30,
            state.Database.Configuration.RunAiCreditAllowance,
            Path.Combine(state.Database.StatePath, "uncalled.usage")
        );
        string runId = state.Database.RunId;
        string binding = state.Database.ReadMetadata("native:ledger_binding")!;
        Assert.IsNotNull(binding);
        JsonObject before = CaptureCollaborationSnapshot(state.Operations);
        using (SqliteConnection ownedPool = new(Sql.ConnectionStringFor(ledgerPath)))
        {
            SqliteConnection.ClearPool(ownedPool);
        }
        string retainedLedger = ledgerPath + ".retained";
        File.Move(ledgerPath, retainedLedger);
        string originalDigest = Digest(retainedLedger);
        Assert.IsFalse(File.Exists(ledgerPath));

        JsonObject result = await SerializedToolHost
            .RejectRetainedRecoveryAsync(
                state.Database,
                command,
                reservation.Id,
                context.CancellationToken
            )
            .ConfigureAwait(false);

        Assert.IsFalse(result["ok"]!.GetValue<bool>());
        Assert.AreEqual("credit_unknown", result["code"]!.GetValue<string>());
        Assert.Contains(
            "requires its original campaign ledger",
            result["message"]!.GetValue<string>()
        );
        Assert.IsTrue(
            JsonNode.DeepEquals(before, CaptureCollaborationSnapshot(state.Operations))
        );
        DeliveryBatch retainedPending =
            state.Operations.ClaimBatch("worker1", false)
            ?? throw new AssertFailedException(
                "Expected pending message to remain queued after rejected recovery."
            );
        Assert.AreEqual(
            "Retain the run whose original ledger is missing.",
            SinglePromptMessageBody(retainedPending)
        );
        Assert.AreEqual(runId, state.Database.RunId);
        Assert.AreEqual(binding, state.Database.ReadMetadata("native:ledger_binding"));
        Assert.IsFalse(File.Exists(ledgerPath));
        Assert.AreEqual(originalDigest, Digest(retainedLedger));
        Assert.IsFalse(File.Exists(Path.Combine(state.Database.StatePath, "coordinator.lock")));
    }

    [TestMethod]
    [TestCategory("HostTrust")]
    [Timeout(120_000, CooperativeCancellation = true)]
    public async Task InitDuplicateLegacyGoalReturnsFailureWithoutCreatingState()
    {
        using ScratchDirectory scratch = new();
        string path = scratch.PathFor("duplicate.json");
        string state = scratch.PathFor("run");
        string ledger = scratch.PathFor("credits.sqlite");
        using (FileStream stream = File.Create(path))
        {
            using Utf8JsonWriter writer = new(stream);
            writer.WriteStartObject();
            writer.WriteString("repositoryPath", scratch.PathFor("checkout"));
            writer.WriteString("statePath", state);
            writer.WriteString("campaignLedgerPath", ledger);
            writer.WriteString("goal", "Review only.");
            writer.WriteString("goal", "Modify sources.");
            writer.WriteEndObject();
        }

        JsonObject result = await SerializedToolHost.RejectInvalidInitializationAsync(
            path, state, context.CancellationToken
        ).ConfigureAwait(false);

        Assert.IsFalse(result["ok"]!.GetValue<bool>());
        Assert.AreEqual("failed", result["code"]!.GetValue<string>());
        Assert.Contains("goal", result["message"]!.GetValue<string>());
        Assert.IsTrue(result.TryGetPropertyValue("data", out JsonNode? data));
        Assert.IsNull(data);
        Assert.IsFalse(Directory.Exists(state));
        Assert.IsFalse(File.Exists(ledger));
    }

    [TestMethod]
    [TestCategory("HostTrust")]
    [Timeout(120_000, CooperativeCancellation = true)]
    public async Task IncompatibleOwnedStateRejectedAtPublicLaunchWithoutRecreation()
    {
        using BoardDeliveryState state = new();
        state.Send("owner", "worker1", "Retain the incompatible run's existing message.");
        state.Database.WriteMetadata("phase8:sentinel", "owned-existing-state");
        state.Database.WriteMetadata("schema_version", "999");
        string sentinel = Path.Combine(
            state.Database.StatePath,
            "owned-incompatible-sentinel.txt"
        );
        File.WriteAllText(sentinel, "Do not replace this owned run.\n");
        string beforeFile = Digest(sentinel);
        string runId = state.Database.RunId;
        JsonObject before = CaptureCollaborationSnapshot(state.Operations);
        JsonObject result = await SerializedToolHost
            .RejectIncompatiblePreflightAsync(state.Database, context.CancellationToken)
            .ConfigureAwait(false);
        Assert.IsFalse(result["ok"]!.GetValue<bool>());
        Assert.AreEqual("failed", result["code"]!.GetValue<string>());
        Assert.Contains("Unsupported run schema 999", result["message"]!.GetValue<string>());
        Assert.Contains("state has not been recreated", result["message"]!.GetValue<string>());
        Assert.IsTrue(
            JsonNode.DeepEquals(before, CaptureCollaborationSnapshot(state.Operations))
        );
        DeliveryBatch retainedPending =
            state.Operations.ClaimBatch("worker1", false)
            ?? throw new AssertFailedException(
                "Expected pending message to remain queued after rejected preflight."
            );
        Assert.AreEqual(
            "Retain the incompatible run's existing message.",
            SinglePromptMessageBody(retainedPending)
        );
        Assert.AreEqual(runId, state.Database.RunId);
        Assert.AreEqual("owned-existing-state", state.Database.ReadMetadata("phase8:sentinel"));
        Assert.AreEqual(beforeFile, Digest(sentinel));
        Assert.AreEqual("999", state.Database.ReadMetadata("schema_version"));
        Assert.IsFalse(File.Exists(Path.Combine(state.Database.StatePath, "coordinator.lock")));
    }

    [TestMethod]
    [TestCategory("HostTrust")]
    [Timeout(120_000, CooperativeCancellation = true)]
    public async Task MissingCapabilityPreservesBusinessStateAndReleasesOperationOwnership()
    {
        PublicBoundaryRun state = await PublicBoundaryRun
            .CreateAsync(1, context.CancellationToken)
            .ConfigureAwait(false);
        await state
            .RunBodyAsync(async () =>
            {
                CollaborationOperations collaboration = new(state.Database);
                GitOperations git = new(state.Database, collaboration);
                ProcessRunner processes = new(state.Database);
                JsonObject create = collaboration.Invoke(
                    "worker1",
                    "task_write",
                    new()
                    {
                        ["action"] = "create",
                        ["title"] = "Retained task",
                        ["body"] = "Keep this domain history.",
                    }
                );
                long task = GitScenarioState.Data(create)["id"]!.GetValue<long>();
                GitScenarioState.Data(
                    collaboration.Invoke(
                        "owner",
                        "task_write",
                        new()
                        {
                            ["action"] = "comment",
                            ["id"] = task,
                            ["body"] = "Retained peer comment.",
                        }
                    )
                );
                GitScenarioState.Data(
                    collaboration.Invoke(
                        "owner",
                        "board_publish",
                        new()
                        {
                            ["type"] = "FACT",
                            ["content"] = "decoder.cs line 17 retains a complete frame",
                        }
                    )
                );
                GitScenarioState.Data(
                    collaboration.Invoke(
                        "owner",
                        "message_send",
                        new()
                        {
                            ["recipient"] = "worker1",
                            ["body"] = "Retained completed delivery.",
                        }
                    )
                );
                DeliveryBatch batch =
                    collaboration.ClaimBatch("worker1", false)
                    ?? throw new AssertFailedException(
                        "Expected actual historical delivery snapshot."
                    );
                collaboration.CompleteBatch(batch.Id);
                GitScenarioState.Data(
                    collaboration.Invoke(
                        "owner",
                        "message_send",
                        new()
                        {
                            ["recipient"] = "worker1",
                            ["body"] = "Retained pending delivery.",
                        }
                    )
                );

                async Task<string> ResolveRefAsync(string path, string revision)
                {
                    ProcessResult resolved = await processes
                        .RunAsync(
                            "git",
                            ["rev-parse", "--verify", revision],
                            path,
                            TimeSpan.FromSeconds(30),
                            context.CancellationToken
                        )
                        .ConfigureAwait(false);
                    Assert.IsFalse(resolved.TimedOut);
                    Assert.AreEqual(0, resolved.ExitCode, resolved.StandardError);
                    return resolved.StandardOutput.Trim();
                }

                async Task<JsonObject> CaptureDomainAsync()
                {
                    string repository = state.Database.Configuration.RepositoryPath;
                    string runId = state.Database.RunId;
                    string workerPath = git.GetWorkerPath("worker1");
                    JsonObject runInfo = GitScenarioState
                        .Data(collaboration.Invoke("owner", "run_info", new()))
                        .DeepClone()
                        .AsObject();
                    runInfo.Remove("lifecycle");
                    JsonObject agents = GitScenarioState
                        .Data(collaboration.Invoke("owner", "agent_list", new()))
                        .DeepClone()
                        .AsObject();
                    foreach (JsonObject agent in agents["items"]!.AsArray().OfType<JsonObject>())
                    {
                        if (agent["id"]!.GetValue<string>() == "owner")
                        {
                            agent.Remove("lifecycle");
                        }
                    }
                    return new JsonObject
                    {
                        ["runInfo"] = runInfo,
                        ["agents"] = agents,
                        ["collaboration"] = CaptureCollaborationSnapshot(collaboration),
                        ["boardSearch"] = GitScenarioState
                            .Data(
                                collaboration.Invoke(
                                    "owner",
                                    "board_search",
                                    new() { ["query"] = "decoder.cs" }
                                )
                            )
                            .DeepClone(),
                        ["changes"] = GitScenarioState
                            .Data(
                                await git.InvokeAsync(
                                        "owner",
                                        "change_list",
                                        new(),
                                        context.CancellationToken
                                    )
                                    .ConfigureAwait(false)
                            )
                            .DeepClone(),
                        ["repositoryHead"] = await ResolveRefAsync(repository, "HEAD")
                            .ConfigureAwait(false),
                        ["integrationRef"] = await ResolveRefAsync(
                                repository,
                                $"refs/heads/lite-agensh/{runId}/integration"
                            )
                            .ConfigureAwait(false),
                        ["workerRef"] = await ResolveRefAsync(
                                repository,
                                $"refs/heads/lite-agensh/{runId}/worker1"
                            )
                            .ConfigureAwait(false),
                        ["workerHead"] = await ResolveRefAsync(workerPath, "HEAD")
                            .ConfigureAwait(false),
                    };
                }

                JsonObject beforeStatus = GitScenarioState.Data(state.Coordinator.Status());
                Assert.AreEqual("initialized", beforeStatus["lifecycle"]!.GetValue<string>());
                Assert.IsFalse(beforeStatus["active"]!.GetValue<bool>());
                Assert.IsNull(beforeStatus["failure"]);
                Assert.IsNull(beforeStatus["nativePreflight"]);
                Assert.IsNull(beforeStatus["nativeBillingCalibration"]);
                Assert.IsEmpty(beforeStatus["settlementFailures"]!.AsArray());
                JsonObject beforeCredits = state.Ledger.Status(
                    state.Database.RunId,
                    state.Database.Configuration.RunAiCreditAllowance
                );
                Assert.IsFalse(beforeCredits["uncertain"]!.GetValue<bool>());
                Assert.IsEmpty(state.Ledger.Reservations(state.Database.RunId));
                JsonObject before = await CaptureDomainAsync().ConfigureAwait(false);
                Assert.HasCount(2, before["collaboration"]!["tasks"]!["items"]!.AsArray());
                Assert.HasCount(2, before["collaboration"]!["messages"]!["items"]!.AsArray());
                Assert.HasCount(1, before["collaboration"]!["board"]!["items"]!.AsArray());

                state.MissingLimitCapability = true;
                JsonObject result = await state
                    .Reopen()
                    .PreflightAsync(context.CancellationToken)
                    .ConfigureAwait(false);

                Assert.IsFalse(result["ok"]!.GetValue<bool>());
                Assert.AreEqual("native_probe_failed", result["code"]!.GetValue<string>());
                Assert.IsNotEmpty(result["message"]!.GetValue<string>());
                JsonObject after = await CaptureDomainAsync().ConfigureAwait(false);
                Assert.IsTrue(JsonNode.DeepEquals(before, after));
                JsonObject afterStatus = GitScenarioState.Data(state.Reopen().Status());
                Assert.AreEqual("stopped", afterStatus["lifecycle"]!.GetValue<string>());
                Assert.IsFalse(afterStatus["active"]!.GetValue<bool>());
                Assert.IsNull(afterStatus["stopReason"]);
                Assert.IsNull(afterStatus["deadlineDrain"]);
                Assert.IsNull(afterStatus["failure"]);
                Assert.IsNull(afterStatus["nativePreflight"]);
                Assert.IsNull(afterStatus["nativeBillingCalibration"]);
                Assert.IsNull(afterStatus["delivery"]);
                Assert.IsEmpty(afterStatus["settlementFailures"]!.AsArray());
                Assert.IsTrue(JsonNode.DeepEquals(beforeStatus["workers"], afterStatus["workers"]));
                Assert.IsTrue(JsonNode.DeepEquals(beforeCredits, afterStatus["credits"]));
                Assert.IsTrue(
                    JsonNode.DeepEquals(
                        beforeCredits,
                        state.Ledger.Status(
                            state.Database.RunId,
                            state.Database.Configuration.RunAiCreditAllowance
                        )
                    )
                );
                Assert.IsNull(
                    GitScenarioState.Data(
                        new Coordinator(state.Database, collaboration, git).Status()
                    )["nativePreflight"]
                );
                Assert.IsEmpty(state.Ledger.Reservations());
                DeliveryBatch retainedPending =
                    collaboration.ClaimBatch("worker1", false)
                    ?? throw new AssertFailedException(
                        "Expected pending message to remain queued after failed preflight."
                    );
                Assert.Contains("Retained pending delivery.", retainedPending.Prompt);
                Assert.DoesNotContain("Retained completed delivery.", retainedPending.Prompt);
                Assert.DoesNotContain("decoder.cs line 17", retainedPending.Prompt);
                // Coordinator execution tables have no public list reader; confirm zero rows.
                Assert.AreEqual(
                    0L,
                    RunStateQueries.Number(state.Database, "SELECT COUNT(*) FROM native_sessions;")
                );
                Assert.AreEqual(
                    0L,
                    RunStateQueries.Number(state.Database, "SELECT COUNT(*) FROM native_attempts;")
                );
                Assert.AreEqual(
                    0L,
                    RunStateQueries.Number(
                        state.Database, "SELECT COUNT(*) FROM coordinator_segments;"
                    )
                );
                using FileStream ownership = new(
                    Path.Combine(state.Database.StatePath, "coordinator.lock"),
                    FileMode.Open,
                    FileAccess.ReadWrite,
                    FileShare.None
                );
                Assert.IsTrue(ownership.CanWrite);
            })
            .ConfigureAwait(false);
    }

    private static JsonObject CaptureCollaborationSnapshot(CollaborationOperations collaboration) =>
        new()
        {
            ["tasks"] = GitScenarioState
                .Data(collaboration.Invoke("owner", "task_list", new()))
                .DeepClone(),
            ["messages"] = GitScenarioState
                .Data(
                    collaboration.Invoke(
                        "owner",
                        "message_read",
                        new() { ["channelId"] = "dm:owner:worker1" }
                    )
                )
                .DeepClone(),
            ["board"] = GitScenarioState
                .Data(collaboration.Invoke("owner", "board_recent", new()))
                .DeepClone(),
        };

    private static string SinglePromptMessageBody(DeliveryBatch batch) =>
        JsonNode
            .Parse(batch.Prompt)!["events"]!
            .AsArray()
            .Single()!["payload"]!["message"]!["body"]!
            .GetValue<string>();

    private static string Digest(string file) =>
        Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes(file)));
}
