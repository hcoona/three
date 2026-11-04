using System.Diagnostics;
using System.Text.Json;
using System.Text.Json.Nodes;
using LiteAgensh;
using Microsoft.Data.Sqlite;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;

namespace LiteAgensh.Tests;

[TestClass]
public sealed class ApplicationContractTests(TestContext testContext)
{
    private static readonly HashSet<string> AllowedTools = new(StringComparer.Ordinal)
    {
        "run_info",
        "agent_info",
        "agent_list",
        "task_write",
        "task_read",
        "task_list",
        "work_search",
        "message_send",
        "message_read",
        "message_thread",
        "message_search",
        "channel_read",
        "channel_list",
        "board_publish",
        "board_recent",
        "board_search",
        "board_expand",
        "change_write",
        "change_read",
        "change_list",
        "change_review",
        "check_read",
    };

    [TestMethod]
    [DataRow(".json")]
    [DataRow(".config")]
    [Timeout(120000, CooperativeCancellation = true)]
    public async Task CliInitStatusAndToolUseProductRuntimeSerializationWithoutTestHostOverrides(
        string extension
    )
    {
        CancellationToken token = testContext.CancellationToken;
        using GitScenario fixture = await GitScenario.CreateAsync(token);
        string state = Path.Combine(fixture.Fixture.RootPath, "cli-state");
        RunConfiguration configuration = fixture.Database.Configuration with
        {
            StatePath = state,
            CampaignLedgerPath = Path.Combine(
                fixture.Fixture.RootPath,
                "cli-campaign",
                "ledger.sqlite"
            ),
        };
        string configPath = Path.Combine(fixture.Fixture.RootPath, "cli-config" + extension);
        File.WriteAllText(configPath, JsonSerializer.Serialize(configuration, Json.Options));
        ProcessRunner processes = new(fixture.Database);

        JsonObject initialized = Scenario.Data(
            await CliAsync(
                processes,
                fixture.Fixture.RepositoryPath,
                token,
                "init",
                "--config",
                configPath
            )
        );
        RunDatabase cliDatabase = new(state);
        Assert.AreEqual(cliDatabase.RunId, initialized["runId"]!.GetValue<string>());
        JsonObject status = Scenario.Data(
            await CliAsync(
                processes,
                fixture.Fixture.RepositoryPath,
                token,
                "status",
                "--state",
                state
            )
        );
        Assert.AreEqual(cliDatabase.RunId, status["runId"]!.GetValue<string>());
        Assert.AreEqual("initialized", status["lifecycle"]!.GetValue<string>());
        JsonObject task = Scenario.Data(
            await CliAsync(
                processes,
                fixture.Fixture.RepositoryPath,
                token,
                "tool",
                "--state",
                state,
                "--name",
                "task_write",
                "--json",
                """{"action":"create","title":"CLI serialized mutation","""
                    + "\"body\":\"No native worker required.\"}"
            )
        );
        Assert.AreEqual("owner", task["author"]!.GetValue<string>());
        Assert.AreEqual("CLI serialized mutation", task["title"]!.GetValue<string>());
        JsonObject persisted = Scenario.Call(
            new CollaborationOperations(cliDatabase),
            "worker1",
            "task_read",
            new { id = Scenario.Id(task) }
        );
        Assert.AreEqual("No native worker required.", persisted["body"]!.GetValue<string>());
        using SqliteConnection retained = cliDatabase.Open();
        Assert.AreEqual(
            0L,
            Sql.Scalar(retained, "SELECT COUNT(*) FROM process_leases WHERE kind LIKE 'native:%';")
        );
        JsonObject invalid = Json.SerializePayload(configuration);
        invalid["unsupportedConfigurationField"] = true;
        File.WriteAllText(configPath, invalid.ToJsonString());
        ProcessResult rejected = await processes.RunAsync(
            Scenario.DotnetHost,
            [typeof(RunDatabase).Assembly.Location, "init", "--config", configPath],
            fixture.Fixture.RepositoryPath,
            TimeSpan.FromSeconds(60),
            token
        );
        Assert.AreEqual(1, rejected.ExitCode);
        JsonObject failure = JsonNode.Parse(rejected.StandardOutput)!.AsObject();
        Scenario.Failure(failure, "failed");
        Assert.Contains("unsupportedConfigurationField", failure["message"]!.GetValue<string>());
        Assert.AreEqual(cliDatabase.RunId, new RunDatabase(state).RunId);
        await fixture.AssertOriginalPreservedAsync(token);
    }

    [TestMethod]
    [DataRow(".yaml")]
    [DataRow(".YML")]
    [Timeout(120000, CooperativeCancellation = true)]
    public async Task CliYamlInitUsesConfigurationDirectoryAndRetainsGoalSnapshot(
        string extension
    )
    {
        CancellationToken token = testContext.CancellationToken;
        using GitScenario fixture = await GitScenario.CreateAsync(token);
        string inputs = Path.Combine(fixture.Fixture.RootPath, "inputs");
        Directory.CreateDirectory(inputs);
        string configPath = Path.Combine(inputs, "run" + extension);
        string goalPath = Path.Combine(inputs, "goal.md");
        const string goal = "# Shared goal\n\nPeers select and review their own work.\n";
        File.WriteAllText(goalPath, goal);
        File.WriteAllText(
            configPath,
            """
            repositoryPath: ../repository
            statePath: ../yaml-state
            campaignLedgerPath: ../yaml-campaign/credits.sqlite
            goalFile: goal.md
            workerCount: 10
            planningSeconds: 0
            nativeExecutable: copilot
            nativeArguments: [--effort, high]
            requirePeerReview: true
            finalChecks:
              - executable: dotnet
                arguments: [--version]
                timeoutSeconds: 20
            """
        );
        ProcessRunner processes = new(fixture.Database);
        JsonObject initialized = Scenario.Data(
            await CliAsync(
                processes,
                fixture.Fixture.RepositoryPath,
                token,
                "init",
                "--config",
                configPath
            )
        );
        string state = Path.Combine(fixture.Fixture.RootPath, "yaml-state");
        RunDatabase database = new(state);
        Assert.AreEqual(database.RunId, initialized["runId"]!.GetValue<string>());
        RunConfiguration saved = database.Configuration;
        Assert.AreEqual(fixture.Fixture.RepositoryPath, saved.RepositoryPath);
        Assert.AreEqual(state, saved.StatePath);
        Assert.AreEqual(
            Path.Combine(fixture.Fixture.RootPath, "yaml-campaign", "credits.sqlite"),
            saved.CampaignLedgerPath
        );
        Assert.AreEqual(goal, saved.Goal);
        Assert.AreEqual(10, saved.WorkerCount);
        Assert.AreEqual("high", saved.GetReasoningEffort());
        Assert.AreEqual("copilot", saved.NativeExecutable);
        CheckCommand check = Assert.ContainsSingle(saved.FinalChecks);
        Assert.AreEqual("dotnet", check.Executable);
        Assert.Contains("--version", check.Arguments);
        Assert.AreEqual(20, check.TimeoutSeconds);

        File.WriteAllText(goalPath, "Edited goal must not change this run.");
        File.WriteAllText(configPath, "not a valid configuration anymore");
        JsonObject status = Scenario.Data(
            await CliAsync(
                processes,
                fixture.Fixture.RepositoryPath,
                token,
                "status",
                "--state",
                state
            )
        );
        Assert.AreEqual("initialized", status["lifecycle"]!.GetValue<string>());
        Assert.AreEqual(goal, new RunDatabase(state).Configuration.Goal);
        using SqliteConnection retained = database.Open();
        Assert.AreEqual(
            0L,
            Sql.Scalar(retained, "SELECT COUNT(*) FROM process_leases WHERE kind LIKE 'native:%';")
        );
        await fixture.AssertOriginalPreservedAsync(token);
    }

    [TestMethod]
    [DataRow("goalFile: goal.md\nunknownField: true")]
    [DataRow("goalFile: goal.md\nworkerCount: 2\nworkerCount: 3")]
    [DataRow("goalFile: goal.md\nworkerCount: '2'")]
    [DataRow("goalFile: goal.md\nworkerCount: 0")]
    [DataRow("goalFile: goal.md\nrequirePeerReview: 'false'")]
    [DataRow("goalFile: goal.md\nprotectedPaths: [null]")]
    [DataRow("goalFile: goal.md\nprotectedPaths:\n  -")]
    [DataRow("goalFile: goal.md\nnativeArguments: [null]")]
    [DataRow("goalFile: goal.md\nfinalChecks: [{executable: dotnet, arguments: [null]}]")]
    [DataRow("goalFile: goal.md\nfinalChecks: [{executable: dotnet, extra: true}]")]
    [DataRow("goalFile: goal.md\nfinalChecks: [{executable: dotnet, timeoutSeconds: '20'}]")]
    [DataRow("goalFile: goal.md\ngoal: conflicting inline goal")]
    [DataRow("goalFile: null")]
    [DataRow("goalFile: 5")]
    [DataRow("goalFile: ' '")]
    [DataRow("goalFile: missing.md")]
    [DataRow("goalFile: empty.md")]
    [DataRow("workerCount: 2")]
    [DataRow("goalFile: goal.md\n---\n{}")]
    [DataRow("[]")]
    [Timeout(60000, CooperativeCancellation = true)]
    public async Task CliYamlInitInvalidInputRejectsBeforeCreatingRunState(string input)
    {
        using RunFixture fixture = new();
        File.WriteAllText(Path.Combine(fixture.RootPath, "goal.md"), "# Shared goal");
        File.WriteAllText(Path.Combine(fixture.RootPath, "empty.md"), " \n");
        string state = Path.Combine(fixture.RootPath, "rejected-state");
        string prefix =
            $"repositoryPath: {JsonSerializer.Serialize(fixture.RepositoryPath)}\n"
            + $"statePath: {JsonSerializer.Serialize(state)}\n"
            + "campaignLedgerPath: ./campaign/credits.sqlite\n";
        string configPath = Path.Combine(fixture.RootPath, "invalid.yaml");
        File.WriteAllText(configPath, input == "[]" ? input : prefix + input);
        ProcessRunner processes = new(fixture.CreateDatabase());
        ProcessResult result = await processes.RunAsync(
            Scenario.DotnetHost,
            [typeof(RunDatabase).Assembly.Location, "init", "--config", configPath],
            fixture.RepositoryPath,
            TimeSpan.FromSeconds(30),
            testContext.CancellationToken
        );
        Assert.AreEqual(1, result.ExitCode, result.StandardError);
        Scenario.Failure(JsonNode.Parse(result.StandardOutput)!.AsObject(), "failed");
        Assert.IsFalse(Directory.Exists(state), "Rejected input must not create run state.");
        Assert.IsFalse(Directory.Exists(Path.Combine(fixture.RootPath, "campaign")));
    }

    [TestMethod]
    [Timeout(60000, CooperativeCancellation = true)]
    public async Task McpStdioDiscoveryAndActorBoundCallsEnforceSerializedLocalContracts()
    {
        RunFixture fixture = new();
        List<McpProcess> children = [];
        Exception? primaryFailure = null;
        AggregateException? cleanupFailure = null;
        try
        {
            await VerifyMcpContractsAsync(fixture, children);
        }
        catch (Exception exception)
        {
            primaryFailure = exception;
            throw;
        }
        finally
        {
            List<Exception> cleanupFailures = [];
            foreach (McpProcess child in children.AsEnumerable().Reverse())
            {
                try
                {
                    await SettleMcpProcessAsync(child);
                }
                catch (Exception exception)
                {
                    cleanupFailures.Add(exception);
                }
            }

            if (cleanupFailures.Count == 0)
            {
                testContext.WriteLine("Owned MCP children settled before fixture deletion.");
                try
                {
                    fixture.Dispose();
                }
                catch (Exception exception)
                {
                    cleanupFailures.Add(exception);
                }
            }
            else
            {
                testContext.WriteLine($"Retained unsettled MCP fixture: {fixture.RootPath}");
            }

            if (cleanupFailures.Count > 0)
            {
                cleanupFailure = new("MCP contract fixture cleanup failed.", cleanupFailures);
                if (primaryFailure is not null)
                {
                    testContext.WriteLine($"Secondary cleanup failure: {cleanupFailure}");
                }
            }
        }

        if (cleanupFailure is not null)
        {
            throw cleanupFailure;
        }
    }

    private async Task VerifyMcpContractsAsync(RunFixture fixture, List<McpProcess> children)
    {
        RunDatabase database = fixture.CreateDatabase();
        CollaborationOperations peers = new(database);
        Scenario.Call(
            peers,
            "worker2",
            "message_send",
            new { recipient = "worker1", body = "fresh notice from the other peer" }
        );
        await using McpClient client = await ConnectAsync(fixture, "worker1", children);
        IList<McpClientTool> tools = await client.ListToolsAsync(
            cancellationToken: testContext.CancellationToken
        );
        Assert.IsTrue(
            AllowedTools.SetEquals(tools.Select(tool => tool.Name)),
            "Actual stdio allowlist: " + string.Join(", ", tools.Select(tool => tool.Name))
        );
        McpClientTool taskWrite = Assert.ContainsSingle(
            tools.Where(tool => tool.Name == "task_write")
        );
        JsonElement schema = taskWrite.JsonSchema;
        Assert.IsFalse(schema.GetProperty("additionalProperties").GetBoolean());
        Assert.IsFalse(schema.GetProperty("properties").TryGetProperty("actor", out _));
        Assert.IsFalse(
            tools.Any(tool =>
                tool.Name.Contains("gitea", StringComparison.OrdinalIgnoreCase)
                || tool.Name.Contains("credential", StringComparison.OrdinalIgnoreCase)
                || tool.Name.Contains("mattermost", StringComparison.OrdinalIgnoreCase)
            )
        );
        McpClientTool taskList = Assert.ContainsSingle(
            tools.Where(tool => tool.Name == "task_list")
        );
        Assert.AreEqual(
            1,
            taskList
                .JsonSchema.GetProperty("properties")
                .GetProperty("limit")
                .GetProperty("minimum")
                .GetInt32()
        );
        Assert.AreEqual(
            200,
            taskList
                .JsonSchema.GetProperty("properties")
                .GetProperty("limit")
                .GetProperty("maximum")
                .GetInt32()
        );

        JsonObject created = await CallAsync(
            client,
            "task_write",
            new()
            {
                ["action"] = "create",
                ["title"] = "Actor-bound task",
                ["body"] = "Created through actual product stdio.",
            }
        );
        JsonObject task = Scenario.Data(created);
        Assert.AreEqual("worker1", task["author"]!.GetValue<string>());
        Assert.AreEqual("Actor-bound task", task["title"]!.GetValue<string>());
        Assert.AreEqual("worker1", created["notices"]!["recipient"]!.GetValue<string>());
        Assert.AreEqual(
            "dm",
            Assert.ContainsSingle(created["notices"]!["items"]!.AsArray().OfType<JsonObject>())[
                "kind"
            ]!.GetValue<string>()
        );
        JsonObject spoofed = await CallAsync(
            client,
            "task_write",
            new()
            {
                ["action"] = "create",
                ["title"] = "Must not exist",
                ["body"] = "spoof",
                ["actor"] = "owner",
            }
        );
        Scenario.Failure(spoofed, "invalid_arguments");
        Scenario.Failure(
            await CallAsync(client, "task_list", new() { ["limit"] = 0 }),
            "invalid_arguments"
        );
        JsonObject read = await CallAsync(
            client,
            "task_read",
            new() { ["id"] = Scenario.Id(task) }
        );
        Assert.AreEqual(
            "Created through actual product stdio.",
            Scenario.Data(read)["body"]!.GetValue<string>()
        );
        Assert.IsEmpty(read["notices"]!["items"]!.AsArray());
        using SqliteConnection retained = database.Open();
        Assert.AreEqual(2L, Sql.Scalar(retained, "SELECT COUNT(*) FROM tasks;"));
        Assert.IsNull(peers.ClaimBatch("worker1"));

        JsonObject privateMessage = Scenario.Data(
            await CallAsync(
                client,
                "message_send",
                new() { ["recipient"] = "owner", ["body"] = "private-stdio-decision" }
            )
        );
        await using McpClient other = await ConnectAsync(fixture, "worker2", children);
        Scenario.Failure(
            await CallAsync(
                other,
                "message_thread",
                new() { ["threadId"] = Scenario.Id(privateMessage) }
            ),
            "not_visible"
        );
        Assert.IsEmpty(
            Scenario.Items(
                Scenario.Data(
                    await CallAsync(
                        other,
                        "message_search",
                        new() { ["query"] = "private-stdio-decision" }
                    )
                )
            )
        );
        Assert.AreEqual(
            "private-stdio-decision",
            Assert.ContainsSingle(
                Scenario.Items(
                    Scenario.Data(
                        await CallAsync(
                            client,
                            "message_thread",
                            new() { ["threadId"] = Scenario.Id(privateMessage) }
                        )
                    )
                )
            )["body"]!.GetValue<string>()
        );
    }

    private async Task<McpClient> ConnectAsync(
        RunFixture fixture,
        string actor,
        List<McpProcess> children
    )
    {
        Process process = new()
        {
            StartInfo = new ProcessStartInfo(Scenario.DotnetHost)
            {
                ArgumentList =
                {
                    typeof(RunDatabase).Assembly.Location,
                    "mcp",
                    "--state",
                    fixture.StatePath,
                    "--actor",
                    actor,
                },
                WorkingDirectory = fixture.RepositoryPath,
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardInput = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
            },
        };
        try
        {
            Assert.IsTrue(process.Start());
        }
        catch
        {
            process.Dispose();
            throw;
        }

        children.Add(new(process, process.StandardError.ReadToEndAsync()));
        testContext.WriteLine(
            $"Owned MCP child started: {process.Id}; actor: {actor}; "
                + $"start ticks: {process.StartTime.ToUniversalTime().Ticks}."
        );
        return await McpClient.CreateAsync(
            new StreamClientTransport(
                serverInput: process.StandardInput.BaseStream,
                serverOutput: process.StandardOutput.BaseStream
            ),
            cancellationToken: testContext.CancellationToken
        );
    }

    private async Task SettleMcpProcessAsync(McpProcess child)
    {
        using Process process = child.Process;
        process.StandardInput.BaseStream.Close();
        using CancellationTokenSource graceful = new(TimeSpan.FromSeconds(5));
        try
        {
            await process.WaitForExitAsync(graceful.Token);
        }
        catch (OperationCanceledException) when (graceful.IsCancellationRequested)
        {
            testContext.WriteLine($"Terminating retained owned MCP child: {process.Id}.");
            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: true);
            }

            using CancellationTokenSource killed = new(TimeSpan.FromSeconds(5));
            await process.WaitForExitAsync(killed.Token);
        }

        string error = await child.StandardError.WaitAsync(TimeSpan.FromSeconds(5));
        process.StandardOutput.Close();
        testContext.WriteLine(
            $"Owned MCP child settled: {process.Id}; exit: {process.ExitCode}; "
                + $"stderr: {error[..Math.Min(error.Length, 2000)]}"
        );
    }

    private sealed record McpProcess(Process Process, Task<string> StandardError);

    private async Task<JsonObject> CallAsync(
        McpClient client,
        string name,
        Dictionary<string, object?> arguments
    )
    {
        CallToolResult result = await client.CallToolAsync(
            name,
            arguments,
            cancellationToken: testContext.CancellationToken
        );
        string text = Assert.ContainsSingle(result.Content.OfType<TextContentBlock>()).Text;
        JsonObject envelope = JsonNode.Parse(text)!.AsObject();
        Assert.AreEqual(envelope["ok"]!.GetValue<bool>(), result.IsError != true);
        Assert.IsTrue(envelope.ContainsKey("message"));
        Assert.IsTrue(envelope.ContainsKey("data"));
        return envelope;
    }

    private static async Task<JsonObject> CliAsync(
        ProcessRunner runner,
        string cwd,
        CancellationToken token,
        params string[] arguments
    )
    {
        ProcessResult result = await runner.RunAsync(
            Scenario.DotnetHost,
            [typeof(RunDatabase).Assembly.Location, .. arguments],
            cwd,
            TimeSpan.FromSeconds(60),
            token
        );
        Assert.AreEqual(0, result.ExitCode, result.StandardOutput + result.StandardError);
        Assert.IsFalse(result.TimedOut);
        return JsonNode.Parse(result.StandardOutput)!.AsObject();
    }
}
