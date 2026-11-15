using System.Security.Cryptography;
using System.Text.Json.Nodes;
using System.Xml.Linq;
using LiteAgensh.Tests.Support;
using Microsoft.Data.Sqlite;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace LiteAgensh.Tests;

[TestClass]
[DoNotParallelize]
public sealed class TrustedBoundarySpecs(TestContext context)
{
    private static readonly string[] PreflightBusinessTables =
    [
        "tasks",
        "task_comments",
        "messages",
        "board_history",
        "board_recent",
        "events",
        "delivery_batches",
        "batch_gists",
        "shown_notices",
        "shown_gists",
        "notice_cursors",
        "coordinator_segments",
        "coordinator_turns",
        "coordinator_controls",
        "native_sessions",
        "native_attempts",
    ];

    [TestMethod]
    [TestCategory("HostTrustAndPinnedSelection")]
    [Timeout(120_000, CooperativeCancellation = true)]
    public async Task HostTrustAndPinnedSelection()
    {
        GitScenarioState state = await GitScenarioState
            .CreateAsync(context.CancellationToken)
            .ConfigureAwait(false);
        bool completed = false;
        try
        {
            string home = Path.Combine(state.Database.StatePath, "phase8-owned-home");
            string settings = Path.Combine(home, ".copilot", "settings.json");
            Directory.CreateDirectory(Path.GetDirectoryName(settings)!);
            const string credential = "PHASE8_OWNED_CREDENTIAL_NOT_A_REAL_SECRET_76430";
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
                                    ["credential"] = credential,
                                },
                                context.CancellationToken
                            )
                            .ConfigureAwait(false);
                        Assert.IsFalse(rejected["ok"]!.GetValue<bool>());
                        Assert.AreEqual("invalid_arguments", rejected["code"]!.GetValue<string>());
                        Assert.DoesNotContain(credential, rejected.ToJsonString());
                        Assert.AreEqual(1L, state.Number("SELECT COUNT(*) FROM tasks;"));
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

            // Declared/source selection, NOT installed CLI or native policy readback.
            XDocument project = XDocument.Load(SerializedToolSpecs.SourcePath("LiteAgensh.csproj"));
            Assert.AreEqual("true", project.Descendants("CopilotSkipCliDownload").Single().Value);
            Assert.AreEqual("1.0.107", project.Descendants("CopilotCliVersion").Single().Value);
            Assert.ContainsSingle(
                project
                    .Descendants("PackageReference")
                    .Where(reference =>
                        (string?)reference.Attribute("Include") == "GitHub.Copilot.SDK"
                    )
            );
            string root = Path.GetFullPath(
                Path.Combine(SerializedToolSpecs.SourcePath("."), "../../../..")
            );
            XDocument packages = XDocument.Load(Path.Combine(root, "Directory.Packages.props"));
            XNamespace msbuild = packages.Root!.Name.Namespace;
            XElement sdk = packages
                .Descendants(msbuild + "PackageVersion")
                .Single(item => (string?)item.Attribute("Include") == "GitHub.Copilot.SDK");
            string productName = Path.GetFileNameWithoutExtension(
                SerializedToolSpecs.SourcePath("LiteAgensh.csproj")
            );
            Assert.Contains(
                "'$(MSBuildProjectName)' == '" + productName + "'",
                sdk.Attribute("Condition")!.Value.Split(" or ", StringSplitOptions.TrimEntries)
            );
            Assert.AreEqual("1.0.28", sdk.Attribute("Version")!.Value);
            JsonObject locked = JsonNode
                .Parse(File.ReadAllText(SerializedToolSpecs.SourcePath("packages.lock.json")))!
                .AsObject();
            JsonObject dependency = locked["dependencies"]!.AsObject().Single().Value![
                "GitHub.Copilot.SDK"
            ]!.AsObject();
            Assert.AreEqual("1.0.28", dependency["resolved"]!.GetValue<string>());
            Assert.AreEqual("1.0.107", NativeSdkSession.RuntimeVersion);
            Console.WriteLine(
                "PHASE8_TRUST_EFFECTS "
                    + new JsonObject
                    {
                        ["ownedFiles"] = new JsonArray(
                            before
                                .Select(item =>
                                    (JsonNode)
                                        new JsonObject
                                        {
                                            ["path"] = item.Key,
                                            ["beforeSha256"] = item.Value,
                                            ["afterSha256"] = Digest(item.Key),
                                        }
                                )
                                .ToArray()
                        ),
                        ["checkoutHeadPreserved"] = true,
                        ["checkoutStatusPreserved"] = true,
                        ["sdkDeclaredSelection"] = "1.0.28",
                        ["cliDeclaredSelection"] = NativeSdkSession.RuntimeVersion,
                        ["installedNativeReadback"] = false,
                        ["nativePolicyEffectivenessObserved"] = false,
                    }.ToJsonString()
            );
            completed = true;
        }
        finally
        {
            if (completed)
            {
                state.Dispose();
            }
            else
            {
                try
                {
                    state.RestoreEnvironment();
                }
                catch (Exception secondary)
                {
                    Console.Error.WriteLine(
                        "PHASE8_ENVIRONMENT_SECONDARY " + Json.ExceptionDetails(secondary)
                    );
                }
                Console.WriteLine("PHASE8_PRESERVED_STATE " + state.Database.StatePath);
            }
        }
    }

    [TestMethod]
    [TestCategory("HostTrustAndPinnedSelection")]
    [Timeout(120_000, CooperativeCancellation = true)]
    public async Task IncompatibleOwnedStateRejectedAtPublicLaunchWithoutRecreation()
    {
        BoardDeliveryState state = new();
        bool completed = false;
        try
        {
            state.Send("owner", "worker1", "Retain the incompatible run's existing message.");
            state.Database.WriteMetadata("phase8:sentinel", "owned-existing-state");
            state.Database.WriteMetadata("schema_version", "999");
            string sentinel = Path.Combine(
                state.Database.StatePath,
                "owned-incompatible-sentinel.txt"
            );
            File.WriteAllText(sentinel, "Do not replace this owned run.\n");
            string beforeFile = Digest(sentinel);
            JsonObject Projection()
            {
                using SqliteConnection connection = state.Database.Open();
                return new()
                {
                    ["metadata"] = Phase6State.Rows(
                        connection,
                        "SELECT key,value FROM meta ORDER BY key;"
                    ),
                    ["schema"] = Phase6State.Rows(
                        connection,
                        "SELECT name,sql FROM sqlite_master WHERE type='table' ORDER BY name;"
                    ),
                    ["tasks"] = Phase6State.Rows(connection, "SELECT * FROM tasks ORDER BY id;"),
                    ["messages"] = Phase6State.Rows(
                        connection,
                        "SELECT * FROM messages ORDER BY id;"
                    ),
                    ["events"] = Phase6State.Rows(connection, "SELECT * FROM events ORDER BY id;"),
                };
            }
            JsonObject before = Projection();
            JsonObject result = await SerializedToolHost
                .RejectIncompatiblePreflightAsync(state.Database, context.CancellationToken)
                .ConfigureAwait(false);
            Assert.IsFalse(result["ok"]!.GetValue<bool>());
            Assert.AreEqual("failed", result["code"]!.GetValue<string>());
            Assert.Contains("Unsupported run schema 999", result["message"]!.GetValue<string>());
            Assert.Contains("state has not been recreated", result["message"]!.GetValue<string>());
            JsonObject after = Projection();
            Assert.AreEqual(before.ToJsonString(), after.ToJsonString());
            Assert.AreEqual(beforeFile, Digest(sentinel));
            Assert.AreEqual("999", state.Database.ReadMetadata("schema_version"));
            Assert.IsFalse(File.Exists(Path.Combine(state.Database.StatePath, "coordinator.lock")));
            Console.WriteLine(
                "PHASE8_INCOMPATIBLE_PRESERVATION "
                    + new JsonObject
                    {
                        ["state"] = state.Database.StatePath,
                        ["before"] = before,
                        ["after"] = after,
                        ["result"] = result,
                        ["sentinelSha256"] = beforeFile,
                    }.ToJsonString()
            );
            completed = true;
        }
        finally
        {
            if (completed)
            {
                state.Dispose();
            }
            else
            {
                Console.WriteLine("PHASE8_PRESERVED_STATE " + state.Database.StatePath);
            }
        }
    }

    [TestMethod]
    [TestCategory("HostTrustAndPinnedSelection")]
    [Timeout(120_000, CooperativeCancellation = true)]
    public async Task MissingCapabilityPreservesBusinessStateAndReleasesOperationOwnership()
    {
        Phase6PublicRun state = await Phase6PublicRun
            .CreateAsync(1, context.CancellationToken)
            .ConfigureAwait(false);
        await state
            .RunBodyAsync(async () =>
            {
                CollaborationOperations collaboration = new(state.Database);
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
                        new() { ["recipient"] = "worker1", ["body"] = "Retained pending delivery." }
                    )
                );

                JsonObject Projection()
                {
                    using SqliteConnection connection = state.Database.Open();
                    JsonObject projection = new()
                    {
                        ["identityConfigurationAssociations"] = Phase6State.Rows(
                            connection,
                            (
                                "SELECT key,value FROM meta WHERE key IN "
                                + "('run_id','configuration','native:ledger_binding') "
                            )
                                + "OR key LIKE 'git:%' OR key LIKE 'worktree:%' ORDER BY key;"
                        ),
                        ["financial"] = Phase6State.Financial(state.Database, state.Ledger),
                    };
                    foreach (string table in PreflightBusinessTables)
                    {
                        projection[table] = Phase6State.Rows(
                            connection,
                            $"SELECT * FROM {table} ORDER BY rowid;"
                        );
                    }
                    return projection;
                }
                JsonObject beforeStatus = GitScenarioState.Data(state.Coordinator.Status());
                string beforeLifecycle = beforeStatus["lifecycle"]!.GetValue<string>();
                Assert.AreEqual("initialized", beforeLifecycle);
                Assert.IsFalse(beforeStatus["active"]!.GetValue<bool>());
                Assert.IsNull(beforeStatus["failure"]);
                Assert.IsEmpty(beforeStatus["settlementFailures"]!.AsArray());
                Assert.IsFalse(beforeStatus["credits"]!["uncertain"]!.GetValue<bool>());
                Assert.AreEqual(
                    0L,
                    Phase6State.Number(state.Database, "SELECT COUNT(*) FROM coordinator_segments;")
                );
                Assert.IsEmpty(state.Ledger.Reservations(state.Database.RunId));
                Assert.IsNull(state.Database.ReadMetadata("native:controlled_preflight_receipt"));
                Assert.IsNull(state.Database.ReadMetadata("native:sdk_preflight_receipt"));
                JsonObject before = Projection();
                Assert.HasCount(2, before["tasks"]!.AsArray());
                Assert.HasCount(2, before["messages"]!.AsArray());
                Assert.HasCount(1, before["delivery_batches"]!.AsArray());
                state.MissingLimitCapability = true;
                JsonObject result = await state
                    .Reopen()
                    .PreflightAsync(context.CancellationToken)
                    .ConfigureAwait(false);
                Assert.IsFalse(result["ok"]!.GetValue<bool>());
                Assert.AreEqual("native_probe_failed", result["code"]!.GetValue<string>());
                Assert.IsNotEmpty(result["message"]!.GetValue<string>());
                JsonObject after = Projection();
                Assert.AreEqual(before.ToJsonString(), after.ToJsonString());
                Assert.AreEqual("stopped", state.Database.ReadMetadata("lifecycle"));
                Assert.IsNull(state.Database.ReadMetadata("native:controlled_preflight_receipt"));
                Assert.IsNull(state.Database.ReadMetadata("native:sdk_preflight_receipt"));
                Assert.IsEmpty(state.Ledger.Reservations(state.Database.RunId));
                Assert.AreEqual(
                    0L,
                    Phase6State.Number(state.Database, "SELECT COUNT(*) FROM native_sessions;")
                );
                Assert.AreEqual(
                    0L,
                    Phase6State.Number(state.Database, "SELECT COUNT(*) FROM native_attempts;")
                );
                Assert.AreEqual(
                    0L,
                    Phase6State.Number(state.Database, "SELECT COUNT(*) FROM coordinator_segments;")
                );
                using (
                    FileStream ownership = new(
                        Path.Combine(state.Database.StatePath, "coordinator.lock"),
                        FileMode.Open,
                        FileAccess.ReadWrite,
                        FileShare.None
                    )
                )
                {
                    Assert.IsTrue(ownership.CanWrite);
                }
                Console.WriteLine(
                    "PHASE8_MISSING_CAPABILITY_PRESERVATION "
                        + new JsonObject
                        {
                            ["state"] = state.Database.StatePath,
                            ["before"] = before,
                            ["after"] = after,
                            ["beforeLifecycle"] = beforeLifecycle,
                            ["afterLifecycle"] = state.Database.ReadMetadata("lifecycle"),
                            ["operationOwnershipReleased"] = true,
                            ["result"] = result,
                            ["allowedEffects"] =
                                (
                                    "temporary ownership; owned Git preparation; preflight "
                                    + "lifecycle/diagnostics; successfully settled controlled "
                                    + "probe"
                                ),
                            ["installedNativeObserved"] = false,
                        }.ToJsonString()
                );
            })
            .ConfigureAwait(false);
    }

    private static string Digest(string file) =>
        Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes(file)));
}
