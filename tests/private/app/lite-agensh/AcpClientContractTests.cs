using System.Diagnostics;
using System.Text.Json;
using System.Text.Json.Nodes;
using LiteAgensh;
using Microsoft.Data.Sqlite;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace LiteAgensh.Tests;

[TestClass]
public sealed class AcpClientContractTests(TestContext testContext)
{
    private static readonly string[] SerializedTurnOrder =
    [
        "session/prompt",
        "session/cancel",
        "session/prompt",
    ];

    [TestMethod]
    [Timeout(60000, CooperativeCancellation = true)]
    [DataRow(false)]
    [DataRow(true)]
    public async Task SessionNewAndExactLoadUseAdvertisedIdentityAndMcpConfiguration(bool load)
    {
        using RunFixture fixture = new();
        RunDatabase database = fixture.CreateDatabase();
        string transcript = Path.Combine(fixture.RootPath, "acp-input.ndjson");
        string diagnostics = Path.Combine(fixture.RootPath, "acp-stderr.txt");
        await using AcpClient client = StartWorker(
            fixture,
            database,
            "ordinary",
            transcript,
            diagnostics
        );
        JsonArray mcp =
        [
            new JsonObject
            {
                ["name"] = "bound-local-tools",
                ["command"] = "self-authored-helper-not-executed",
                ["args"] = new JsonArray("mcp", fixture.StatePath, "worker1"),
                ["env"] = new JsonArray(),
            },
        ];

        JsonObject initialized = await client.InitializeAsync(testContext.CancellationToken);
        string session = await client.OpenSessionAsync(
            fixture.RepositoryPath,
            mcp,
            load ? "opaque-saved-session" : null,
            testContext.CancellationToken
        );
        await client.WaitForCommandsAsync(TimeSpan.FromSeconds(5), testContext.CancellationToken);
        JsonObject result = await client.PromptAsync(
            "inspect retained project state",
            testContext.CancellationToken
        );

        Assert.AreEqual(
            "self-authored-acp-1",
            initialized["agentInfo"]!["version"]!.GetValue<string>()
        );
        Assert.IsTrue(client.SupportsLoad);
        Assert.AreEqual(load ? "opaque-saved-session" : "scripted-session", session);
        Assert.AreEqual(session, client.SessionId);
        Assert.AreEqual("end_turn", result["stopReason"]!.GetValue<string>());
        Assert.AreEqual(session, result["seenSession"]!.GetValue<string>());
        Assert.AreEqual("inspect retained project state", result["seenText"]!.GetValue<string>());
        JsonObject command = Assert.ContainsSingle(client.AdvertisedCommands.OfType<JsonObject>());
        Assert.AreEqual("usage", command["name"]!.GetValue<string>());
        JsonObject sessionRequest = Assert.ContainsSingle(
            ReadTranscript(transcript)
                .Where(request =>
                    request["method"]?.GetValue<string>() == (load ? "session/load" : "session/new")
                )
        );
        JsonNode parameters = sessionRequest["params"]!;
        Assert.AreEqual(fixture.RepositoryPath, parameters["cwd"]!.GetValue<string>());
        Assert.AreEqual(
            "bound-local-tools",
            parameters["mcpServers"]![0]!["name"]!.GetValue<string>()
        );
        Assert.AreEqual("worker1", parameters["mcpServers"]![0]!["args"]![2]!.GetValue<string>());
        if (load)
        {
            Assert.AreEqual("opaque-saved-session", parameters["sessionId"]!.GetValue<string>());
        }

        await client.StopAsync();
        Assert.Contains("scripted diagnostic; no native agent", File.ReadAllText(diagnostics));
        using SqliteConnection retained = database.Open();
        Assert.AreEqual(
            0L,
            Sql.Scalar(retained, "SELECT COUNT(*) FROM process_leases WHERE state<>'exited';")
        );
    }

    [TestMethod]
    [Timeout(60000, CooperativeCancellation = true)]
    public async Task OpenSessionUnadvertisedExactLoadDoesNotSendLoadRequest()
    {
        using RunFixture fixture = new();
        string transcript = Path.Combine(fixture.RootPath, "acp-input.ndjson");
        await using AcpClient client = StartWorker(
            fixture,
            fixture.CreateDatabase(),
            "no-load",
            transcript,
            Path.Combine(fixture.RootPath, "stderr.txt")
        );
        await client.InitializeAsync(testContext.CancellationToken);

        OperationException error = await Assert.ThrowsExactlyAsync<OperationException>(() =>
            client.OpenSessionAsync(
                fixture.RepositoryPath,
                [],
                "saved-session",
                testContext.CancellationToken
            )
        );

        Assert.AreEqual("unavailable", error.Code);
        Assert.IsFalse(client.SupportsLoad);
        Assert.IsNull(client.SessionId);
        Assert.IsFalse(
            ReadTranscript(transcript)
                .Any(request => request["method"]?.GetValue<string>() == "session/load")
        );
    }

    [TestMethod]
    [Timeout(60000, CooperativeCancellation = true)]
    public async Task OpenSessionReturnedIdentityMismatchRejectsChangedSession()
    {
        using RunFixture fixture = new();
        string transcript = Path.Combine(fixture.RootPath, "acp-input.ndjson");
        await using AcpClient client = StartWorker(
            fixture,
            fixture.CreateDatabase(),
            "wrong-session",
            transcript,
            Path.Combine(fixture.RootPath, "stderr.txt")
        );
        await client.InitializeAsync(testContext.CancellationToken);

        InvalidDataException error = await Assert.ThrowsExactlyAsync<InvalidDataException>(() =>
            client.OpenSessionAsync(
                fixture.RepositoryPath,
                [],
                "saved-session",
                testContext.CancellationToken
            )
        );

        Assert.Contains("identity", error.Message);
        Assert.IsNull(client.SessionId);
        JsonObject request = Assert.ContainsSingle(
            ReadTranscript(transcript)
                .Where(value => value["method"]?.GetValue<string>() == "session/load")
        );
        Assert.AreEqual("saved-session", request["params"]!["sessionId"]!.GetValue<string>());
    }

    [TestMethod]
    [Timeout(60000, CooperativeCancellation = true)]
    [DataRow("deny", "execute", "denied-once")]
    [DataRow("trusted", "edit", "allowed-once")]
    [DataRow("trusted", "unknown-action", "denied-once")]
    public async Task PromptPermissionRequestsHonorBoundPolicy(
        string policy,
        string kind,
        string optionId
    )
    {
        using RunFixture fixture = new();
        string transcript = Path.Combine(fixture.RootPath, "acp-input.ndjson");
        await using AcpClient client = StartWorker(
            fixture,
            fixture.CreateDatabase(),
            "permission",
            transcript,
            Path.Combine(fixture.RootPath, "stderr.txt"),
            policy,
            kind
        );
        await client.InitializeAsync(testContext.CancellationToken);
        await client.OpenSessionAsync(
            fixture.RepositoryPath,
            [],
            cancellationToken: testContext.CancellationToken
        );

        JsonObject result = await client.PromptAsync(
            "bounded scripted operation",
            testContext.CancellationToken
        );

        Assert.AreEqual("end_turn", result["stopReason"]!.GetValue<string>());
        Assert.AreEqual("selected", result["permissionOutcome"]!["outcome"]!.GetValue<string>());
        Assert.AreEqual(optionId, result["permissionOutcome"]!["optionId"]!.GetValue<string>());
        JsonObject response = Assert.ContainsSingle(
            ReadTranscript(transcript).Where(message => message["method"] is null)
        );
        Assert.AreEqual("permission-1", response["id"]!.GetValue<string>());
        Assert.AreEqual(optionId, response["result"]!["outcome"]!["optionId"]!.GetValue<string>());
        Assert.IsFalse(response.ContainsKey("error"));
    }

    [TestMethod]
    [Timeout(60000, CooperativeCancellation = true)]
    public async Task PromptConcurrentTurnsStaySerializedAcrossCancellationBoundary()
    {
        using RunFixture fixture = new();
        string transcript = Path.Combine(fixture.RootPath, "acp-input.ndjson");
        TaskCompletionSource active = new(TaskCreationOptions.RunContinuationsAsynchronously);
        await using AcpClient client = StartWorker(
            fixture,
            fixture.CreateDatabase(),
            "serialized",
            transcript,
            Path.Combine(fixture.RootPath, "stderr.txt"),
            onUpdate: update =>
            {
                if (
                    update["update"]?["content"]?["text"]?.GetValue<string>()
                    == "first prompt active"
                )
                {
                    active.TrySetResult();
                }
            }
        );
        await client.InitializeAsync(testContext.CancellationToken);
        await client.OpenSessionAsync(
            fixture.RepositoryPath,
            [],
            cancellationToken: testContext.CancellationToken
        );
        Task<JsonObject> first = client.PromptAsync("first", testContext.CancellationToken);
        await active.Task.WaitAsync(testContext.CancellationToken);

        Task<JsonObject> second = client.PromptAsync("second", testContext.CancellationToken);
        // A protocol notification is a deterministic fence: the scripted peer
        // completes the first prompt only after receiving this session's cancel.
        await client.CancelAsync(testContext.CancellationToken);
        JsonObject firstResult = await first;
        JsonObject secondResult = await second;

        Assert.AreEqual("cancelled", firstResult["stopReason"]!.GetValue<string>());
        Assert.AreEqual("end_turn", secondResult["stopReason"]!.GetValue<string>());
        Assert.AreEqual("second", secondResult["seenText"]!.GetValue<string>());
        JsonObject[] turns = ReadTranscript(transcript)
            .Where(request =>
                request["method"]?.GetValue<string>() is "session/prompt" or "session/cancel"
            )
            .ToArray();
        Assert.IsTrue(
            SerializedTurnOrder.SequenceEqual(
                turns.Select(request => request["method"]!.GetValue<string>())
            ),
            "The second prompt must not reach ACP before the first turn completes."
        );
        Assert.IsTrue(
            turns.All(request =>
                request["params"]!["sessionId"]!.GetValue<string>() == "scripted-session"
            )
        );
    }

    [TestMethod]
    [Timeout(60000, CooperativeCancellation = true)]
    public async Task PromptNativeErrorPropagatesFailureWithoutSuccessfulTurn()
    {
        using RunFixture fixture = new();
        string transcript = Path.Combine(fixture.RootPath, "acp-input.ndjson");
        await using AcpClient client = StartWorker(
            fixture,
            fixture.CreateDatabase(),
            "native-error",
            transcript,
            Path.Combine(fixture.RootPath, "stderr.txt")
        );
        await client.InitializeAsync(testContext.CancellationToken);
        await client.OpenSessionAsync(
            fixture.RepositoryPath,
            [],
            cancellationToken: testContext.CancellationToken
        );

        OperationException error = await Assert.ThrowsExactlyAsync<OperationException>(() =>
            client.PromptAsync("must fail", testContext.CancellationToken)
        );

        Assert.AreEqual("native_error", error.Code);
        Assert.Contains("scripted rejection", error.Message);
        JsonObject request = Assert.ContainsSingle(
            ReadTranscript(transcript)
                .Where(value => value["method"]?.GetValue<string>() == "session/prompt")
        );
        Assert.AreEqual("must fail", request["params"]!["prompt"]![0]!["text"]!.GetValue<string>());
    }

    [TestMethod]
    [Timeout(60000, CooperativeCancellation = true)]
    public async Task PromptNonProtocolStdoutRejectsDiagnosticsAsResponse()
    {
        using RunFixture fixture = new();
        RunDatabase database = fixture.CreateDatabase();
        await using AcpClient client = StartWorker(
            fixture,
            database,
            "bad-stdout",
            Path.Combine(fixture.RootPath, "acp-input.ndjson"),
            Path.Combine(fixture.RootPath, "stderr.txt")
        );
        await client.InitializeAsync(testContext.CancellationToken);
        await client.OpenSessionAsync(
            fixture.RepositoryPath,
            [],
            cancellationToken: testContext.CancellationToken
        );

        await Assert.ThrowsAsync<JsonException>(() =>
            client.PromptAsync("must not become a completed turn", testContext.CancellationToken)
        );
        await client.StopAsync();

        using SqliteConnection retained = database.Open();
        Assert.AreEqual(
            0L,
            Sql.Scalar(retained, "SELECT COUNT(*) FROM process_leases WHERE state<>'exited';")
        );
    }

    private static AcpClient StartWorker(
        RunFixture fixture,
        RunDatabase database,
        string mode,
        string transcript,
        string diagnostics,
        string policy = "deny",
        string kind = "execute",
        Action<JsonObject>? onUpdate = null
    )
    {
        string script = fixture.WriteScript(ScriptedWorker);
        ProcessStartInfo start = new("pwsh") { WorkingDirectory = fixture.RepositoryPath };
        foreach (
            string argument in new[]
            {
                "-NoLogo",
                "-NoProfile",
                "-File",
                script,
                "-Mode",
                mode,
                "-Transcript",
                transcript,
                "-PermissionKind",
                kind,
            }
        )
        {
            start.ArgumentList.Add(argument);
        }

        return AcpClient.Start(
            start,
            new ProcessRunner(database),
            "scripted-acp",
            policy,
            diagnostics,
            onUpdate
        );
    }

    private static JsonObject[] ReadTranscript(string path) =>
        File.ReadAllLines(path).Select(line => JsonNode.Parse(line)!.AsObject()).ToArray();

    private const string ScriptedWorker = """
        param([string] $Mode, [string] $Transcript, [string] $PermissionKind)
        $ErrorActionPreference = 'Stop'
        function Send($Message) {
            [Console]::Out.WriteLine(($Message | ConvertTo-Json -Depth 30 -Compress))
        }
        function Reply($Id, $Result) {
            Send @{ jsonrpc = '2.0'; id = $Id; result = $Result }
        }
        function Commands($Session, $Name) {
            Send @{ jsonrpc = '2.0'; method = 'session/update'; params = @{
                sessionId = $Session; update = @{
                    sessionUpdate = 'available_commands_update'
                    availableCommands = @(@{
                        name = $Name; description = 'Scripted informational command'
                    })
                }
            }}
        }
        $session = 'scripted-session'
        $held = $null
        $prompts = 0
        while ($null -ne ($line = [Console]::In.ReadLine())) {
            [IO.File]::AppendAllText($Transcript, $line + [Environment]::NewLine)
            $request = ConvertFrom-Json -InputObject $line -AsHashtable
            if (-not $request.ContainsKey('method')) {
                if ($request.id -eq 'permission-1' -and $null -ne $held) {
                    Reply $held @{
                        stopReason = 'end_turn'; permissionOutcome = $request.result.outcome
                    }
                    $held = $null
                }
                continue
            }
            switch ($request.method) {
                'initialize' {
                    [Console]::Error.WriteLine('scripted diagnostic; no native agent')
                    Reply $request.id @{
                        protocolVersion = 1
                        agentInfo = @{
                            name = 'self-authored-worker'; version = 'self-authored-acp-1'
                        }
                        agentCapabilities = @{ loadSession = ($Mode -ne 'no-load') }
                    }
                }
                { $_ -in 'session/new', 'session/load' } {
                    if ($request.method -eq 'session/load') { $session = $request.params.sessionId }
                    $returned = if ($Mode -eq 'wrong-session') {
                        'unexpected-session'
                    } else { $session }
                    Reply $request.id @{ sessionId = $returned }
                    Commands $session 'usage'
                    Commands 'unrelated-session' 'must-not-leak'
                }
                'session/prompt' {
                    $prompts++
                    if ($Mode -eq 'bad-stdout') {
                        [Console]::Out.WriteLine('diagnostic is not JSON-RPC')
                        exit 0
                    }
                    if ($Mode -eq 'native-error') {
                        Send @{ jsonrpc = '2.0'; id = $request.id; error = @{
                            code = -32000; message = 'scripted rejection'
                        }}
                    } elseif ($Mode -eq 'permission') {
                        $held = $request.id
                        Send @{
                            jsonrpc = '2.0'; id = 'permission-1'
                            method = 'session/request_permission'; params = @{
                            sessionId = $session; toolCall = @{ kind = $PermissionKind }
                            options = @(
                                @{ optionId = 'persistent-allow'; kind = 'allow_always' },
                                @{ optionId = 'persistent-reject'; kind = 'reject_always' },
                                @{ optionId = 'allowed-once'; kind = 'allow_once' },
                                @{ optionId = 'denied-once'; kind = 'reject_once' }
                            )
                            }
                        }
                    } elseif ($Mode -eq 'serialized' -and $prompts -eq 1) {
                        $held = $request.id
                        Send @{ jsonrpc = '2.0'; method = 'session/update'; params = @{
                            sessionId = $session; update = @{
                                sessionUpdate = 'agent_message_chunk'
                                content = @{ type = 'text'; text = 'first prompt active' }
                            }
                        }}
                    } else {
                        Reply $request.id @{
                            stopReason = 'end_turn'; seenSession = $request.params.sessionId
                            seenText = $request.params.prompt[0].text
                        }
                    }
                }
                'session/cancel' {
                    if ($Mode -eq 'serialized' -and $null -ne $held) {
                        Reply $held @{
                            stopReason = 'cancelled'; seenSession = $request.params.sessionId
                        }
                        $held = $null
                    }
                }
            }
        }
        """;
}
