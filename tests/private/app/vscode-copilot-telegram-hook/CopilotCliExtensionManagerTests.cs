using System.Diagnostics;
using System.Text.Json;
using Xunit;

namespace Hcoona.VsCodeCopilotTelegramHook.Tests;

public sealed class CopilotCliExtensionManagerTests
{
    private static readonly JsonSerializerOptions HarnessJsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
    };

    [Fact]
    public async Task GeneratedExtensionPassesNodeSyntaxCheck()
    {
        using TemporaryDirectory temporaryDirectory = new();
        string extensionPath = Path.Combine(temporaryDirectory.Path, "extension.mjs");
        await File.WriteAllTextAsync(
            extensionPath,
            CopilotCliExtensionManager.BuildExtensionSource(
                Path.Combine(temporaryDirectory.Path, "missing-notifier"),
                Path.Combine(temporaryDirectory.Path, "spool")),
            CancellationToken.None);

        ProcessResult result = await RunNodeAsync(["--check", extensionPath]);

        Assert.Equal(0, result.ExitCode);
        Assert.True(
            string.IsNullOrWhiteSpace(result.StandardError),
            result.StandardError);
    }

    [Theory]
    [InlineData("completion", 1, 1, 0)]
    [InlineData("completion-with-permission-denied", 1, 1, 0)]
    [InlineData("subagent", 0, 0, 0)]
    [InlineData("autopilot", 2, 1, 1)]
    [InlineData("autopilot-failed", 1, 1, 0)]
    [InlineData("queued", 1, 1, 0)]
    [InlineData("queued-cancelled", 1, 1, 0)]
    [InlineData("queued-command-pending", 0, 0, 0)]
    [InlineData("queued-command-cancelled", 1, 1, 0)]
    [InlineData("completion-retry-survives-later-prompt", 1, 1, 0)]
    [InlineData("attention-completed", 1, 0, 1)]
    [InlineData("attention-completed-without-request-id", 1, 0, 1)]
    [InlineData("attention-survives-queued-message", 1, 1, 0)]
    public async Task GeneratedExtensionPublishesOnlyActionableRootEvents(
        string scenario,
        int expectedReadyCount,
        int expectedActiveCount,
        int expectedCancelledCount)
    {
        using TemporaryDirectory temporaryDirectory = new();
        ExtensionHarnessResult result = await RunHarnessAsync(
            temporaryDirectory.Path,
            scenario);

        Assert.Equal(expectedReadyCount, result.ReadyCount);
        Assert.Equal(expectedActiveCount, result.ActiveEvents.Count);
        Assert.Equal(expectedCancelledCount, result.CancelledCount);

        if (scenario is "completion"
            or "completion-with-permission-denied"
            or "completion-retry-survives-later-prompt"
            or "autopilot"
            or "autopilot-failed"
            or "queued"
            or "queued-cancelled"
            or "queued-command-cancelled")
        {
            JsonElement activeEvent = Assert.Single(result.ActiveEvents);
            Assert.Equal(
                "session_idle",
                activeEvent.GetProperty("event_type").GetString());
            Assert.Equal(
                "Final root response",
                activeEvent.GetProperty("summary").GetString());
        }
    }

    [Fact]
    public async Task GeneratedExtensionDoesNotSerializePermissionCommandBodies()
    {
        using TemporaryDirectory temporaryDirectory = new();
        ExtensionHarnessResult result = await RunHarnessAsync(
            temporaryDirectory.Path,
            "attention-completed");

        JsonElement attentionEvent = Assert.Single(result.Events);
        string serializedEvent = attentionEvent.GetRawText();
        Assert.DoesNotContain("rm -rf", serializedEvent, StringComparison.Ordinal);
        Assert.Contains("Permission required", serializedEvent, StringComparison.Ordinal);
    }

    private static async Task<ExtensionHarnessResult> RunHarnessAsync(
        string temporaryDirectory,
        string scenario)
    {
        string spoolDirectory = Path.Combine(temporaryDirectory, "spool");
        string extensionPath = Path.Combine(temporaryDirectory, "extension.mjs");
        await File.WriteAllTextAsync(
            extensionPath,
            CopilotCliExtensionManager.BuildExtensionSource(
                Path.Combine(temporaryDirectory, "missing-notifier"),
                spoolDirectory),
            CancellationToken.None);

        string sdkDirectory = Path.Combine(
            temporaryDirectory,
            "node_modules",
            "@github",
            "copilot-sdk");
        Directory.CreateDirectory(sdkDirectory);
        await File.WriteAllTextAsync(
            Path.Combine(sdkDirectory, "package.json"),
            """
            {
              "name": "@github/copilot-sdk",
              "type": "module",
              "exports": {
                "./extension": "./extension.js"
              }
            }
            """,
            CancellationToken.None);
        await File.WriteAllTextAsync(
            Path.Combine(sdkDirectory, "extension.js"),
            """
            export async function joinSession(options) {
              if (globalThis.__denyPermission && options?.onPermissionRequest) {
                throw new Error(
                  'Extension "user:vscode-copilot-telegram-hook" '
                  + "was denied permission access and will not be loaded.");
              }
              return globalThis.__copilotSession;
            }
            """,
            CancellationToken.None);

        string harnessPath = Path.Combine(temporaryDirectory, "harness.mjs");
        await File.WriteAllTextAsync(
            harnessPath,
            BuildHarnessSource(scenario),
            CancellationToken.None);

        ProcessResult processResult = await RunNodeAsync([harnessPath]);
        Assert.Equal(0, processResult.ExitCode);

        return JsonSerializer.Deserialize<ExtensionHarnessResult>(
                processResult.StandardOutput,
                HarnessJsonOptions)
            ?? throw new InvalidDataException("The extension harness returned no result.");
    }

    private static string BuildHarnessSource(string scenario)
    {
        string serializedScenario = JsonSerializer.Serialize(scenario);
        return $$"""
            import fs from "node:fs";
            import path from "node:path";

            const handlers = new Map();
            const scenario = {{serializedScenario}};
            let pendingItems = [];
            globalThis.__denyPermission =
              scenario === "completion-with-permission-denied";
            globalThis.__copilotSession = {
              sessionId: "root-session",
              on(eventName, handler) {
                const eventHandlers = handlers.get(eventName) ?? [];
                eventHandlers.push(handler);
                handlers.set(eventName, eventHandlers);
              },
              rpc: {
                queue: {
                  async pendingItems() {
                    return { items: pendingItems, steeringMessages: [] };
                  },
                },
              },
            };

            function emit(eventName, id, data = {}, agentId = null) {
              const event = {
                id,
                timestamp: "2026-03-20T12:00:00.000Z",
                data,
              };
              if (agentId !== null) {
                event.agentId = agentId;
              }
              for (const handler of handlers.get(eventName) ?? []) {
                handler(event);
              }
            }

            await import("./extension.mjs");

            if (
              scenario === "completion"
              || scenario === "completion-with-permission-denied"
            ) {
              emit("user.message", "user-1", {
                agentMode: "interactive",
                interactionId: "interaction-1",
              });
              emit("assistant.turn_start", "turn-1", {
                interactionId: "interaction-1",
              });
              emit("assistant.message", "message-1", {
                content: "Final root response",
              });
              emit("session.idle", "idle-1");
            } else if (scenario === "subagent") {
              emit("assistant.turn_start", "turn-sub", {}, "subagent-1");
              emit("assistant.message", "message-sub", {
                content: "Subagent response",
              }, "subagent-1");
              emit("session.idle", "idle-sub", {}, "subagent-1");
            } else if (scenario === "autopilot") {
              emit("user.message", "user-1", {
                agentMode: "autopilot",
                interactionId: "interaction-1",
              });
              emit("assistant.message", "message-1", {
                content: "Intermediate response",
              });
              emit("session.task_complete", "complete-1", {
                success: true,
                summary: "Intermediate response",
              });
              emit("session.idle", "idle-1");
              emit("user.message", "user-2", {
                agentMode: "autopilot",
                interactionId: "interaction-2",
                isAutopilotContinuation: true,
              });
              emit("assistant.message", "message-2", {
                content: "Final root response",
              });
              emit("session.task_complete", "complete-2", {
                success: true,
                summary: "Final root response",
              });
              emit("session.idle", "idle-2");
            } else if (scenario === "autopilot-failed") {
              emit("user.message", "user-1", {
                agentMode: "autopilot",
                interactionId: "interaction-1",
              });
              emit("assistant.message", "message-1", {
                content: "Final root response",
              });
              emit("session.task_complete", "complete-1", {
                success: false,
              });
              emit("session.idle", "idle-1");
            } else if (scenario === "queued") {
              emit("user.message", "user-1", {
                agentMode: "interactive",
                interactionId: "interaction-1",
              });
              emit("assistant.message", "message-1", {
                content: "First response",
              });
              emit("user.message", "user-2", {
                delivery: "queued",
                interactionId: "interaction-2",
              });
              emit("session.idle", "idle-1");
              emit("assistant.turn_start", "turn-2", {
                interactionId: "interaction-2",
              });
              emit("assistant.message", "message-2", {
                content: "Final root response",
              });
              emit("session.idle", "idle-2");
            } else if (scenario === "queued-cancelled") {
              emit("user.message", "user-1", {
                agentMode: "interactive",
                interactionId: "interaction-1",
              });
              emit("assistant.message", "message-1", {
                content: "Final root response",
              });
              pendingItems = [{ kind: "message", displayText: "cancel me" }];
              emit("user.message", "user-2", {
                delivery: "queued",
                interactionId: "interaction-2",
              });
              emit("session.idle", "idle-1");
              pendingItems = [];
              emit("pending_messages.modified", "pending-1");
              await new Promise((resolve) => setTimeout(resolve, 20));
            } else if (
              scenario === "queued-command-pending"
              || scenario === "queued-command-cancelled"
            ) {
              emit("user.message", "user-1", {
                agentMode: "interactive",
                interactionId: "interaction-1",
              });
              emit("assistant.message", "message-1", {
                content: "Final root response",
              });
              pendingItems = [{ kind: "command", displayText: "/review" }];
              emit("pending_messages.modified", "pending-1");
              emit("session.idle", "idle-1");
              await new Promise((resolve) => setTimeout(resolve, 20));
              if (scenario === "queued-command-cancelled") {
                pendingItems = [];
                emit("pending_messages.modified", "pending-2");
                await new Promise((resolve) => setTimeout(resolve, 20));
              }
            } else if (scenario === "completion-retry-survives-later-prompt") {
              emit("user.message", "user-1", {
                agentMode: "interactive",
                interactionId: "interaction-1",
              });
              emit("assistant.message", "message-1", {
                content: "Final root response",
              });
              emit("session.idle", "idle-1");
              await new Promise((resolve) => setTimeout(resolve, 1100));
              emit("user.message", "user-2", {
                agentMode: "interactive",
                interactionId: "interaction-2",
              });
            } else if (
              scenario === "attention-completed"
              || scenario === "attention-completed-without-request-id"
              || scenario === "attention-survives-queued-message"
            ) {
              const requestId =
                scenario === "attention-completed"
                || scenario === "attention-survives-queued-message"
                  ? "request-1"
                  : undefined;
              emit("permission.requested", "permission-1", {
                requestId,
                permissionRequest: {
                  kind: "shell",
                  intention: "Run a command",
                  command: "rm -rf /should-not-be-serialized",
                },
              });
              if (scenario === "attention-survives-queued-message") {
                emit("user.message", "user-queued", {
                  delivery: "queued",
                  interactionId: "interaction-queued",
                });
              } else {
                emit("permission.completed", "permission-complete-1", {
                  requestId,
                });
              }
            } else {
              throw new Error(`Unknown scenario: ${scenario}`);
            }

            const spoolDirectory = path.join(path.dirname(import.meta.filename), "spool");
            const entries = fs.readdirSync(spoolDirectory);
            const readyFiles = entries.filter((entry) => entry.endsWith(".json"));
            const events = readyFiles.map((entry) =>
              JSON.parse(fs.readFileSync(path.join(spoolDirectory, entry), "utf8")));
            const activeEvents = readyFiles
              .filter((entry) =>
                !fs.existsSync(path.join(spoolDirectory, `${entry}.cancelled`)))
              .map((entry) =>
                JSON.parse(fs.readFileSync(path.join(spoolDirectory, entry), "utf8")));

            console.log(JSON.stringify({
              readyCount: readyFiles.length,
              cancelledCount: entries.filter(
                (entry) => entry.endsWith(".json.cancelled")).length,
              events,
              activeEvents,
            }));
            """;
    }

    private static async Task<ProcessResult> RunNodeAsync(IReadOnlyList<string> arguments)
    {
        using Process process = new()
        {
            StartInfo = new ProcessStartInfo
            {
                FileName = "node",
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true,
            },
        };
        foreach (string argument in arguments)
        {
            process.StartInfo.ArgumentList.Add(argument);
        }

        process.Start();
        Task<string> standardOutput = process.StandardOutput.ReadToEndAsync();
        Task<string> standardError = process.StandardError.ReadToEndAsync();
        await process.WaitForExitAsync();

        return new ProcessResult(
            process.ExitCode,
            await standardOutput,
            await standardError);
    }

    private sealed class ExtensionHarnessResult
    {
        public int ReadyCount { get; init; }

        public int CancelledCount { get; init; }

        public List<JsonElement> Events { get; init; } = [];

        public List<JsonElement> ActiveEvents { get; init; } = [];
    }

    private sealed record ProcessResult(
        int ExitCode,
        string StandardOutput,
        string StandardError);

    private sealed class TemporaryDirectory : IDisposable
    {
        public TemporaryDirectory()
        {
            Path = Directory.CreateTempSubdirectory().FullName;
        }

        public string Path { get; }

        public void Dispose()
        {
            Directory.Delete(Path, recursive: true);
        }
    }
}
