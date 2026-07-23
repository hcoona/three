using System.Text.Json;

namespace Hcoona.VsCodeCopilotTelegramHook;

internal static class CopilotCliExtensionManager
{
    private const string ManagedMarker =
        "// Managed by hcoona-vscode-copilot-telegram-hook.";
    private const UnixFileMode OwnerOnlyDirectoryMode =
        UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute;

    public static ConfigurationApplyResult? PreflightInstall(string extensionFilePath)
    {
        if (!File.Exists(extensionFilePath))
        {
            return null;
        }

        try
        {
            string content = File.ReadAllText(extensionFilePath);
            return content.Contains(ManagedMarker, StringComparison.Ordinal)
                ? null
                : new ConfigurationApplyResult(
                    false,
                    "The Copilot CLI extension file already exists and is not managed by this "
                    + $"application: {extensionFilePath}");
        }
        catch (Exception ex) when (
            ex is IOException or UnauthorizedAccessException or NotSupportedException)
        {
            return new ConfigurationApplyResult(
                false,
                $"The Copilot CLI extension file could not be inspected: {ex.Message}");
        }
    }

    public static ConfigurationApplyResult Install(
        string extensionFilePath,
        string installedBinaryPath)
    {
        ConfigurationApplyResult? preflightResult = PreflightInstall(extensionFilePath);
        if (preflightResult is not null)
        {
            return preflightResult;
        }

        EnsureParentDirectory(extensionFilePath);
        AtomicTextFileWriter.WriteAllText(
            extensionFilePath,
            BuildExtensionSource(installedBinaryPath));
        return new ConfigurationApplyResult(
            true,
            $"Updated Copilot CLI extension file: {extensionFilePath}");
    }

    public static ConfigurationApplyResult? PreflightUninstall(string extensionFilePath)
    {
        if (!File.Exists(extensionFilePath))
        {
            return null;
        }

        try
        {
            string content = File.ReadAllText(extensionFilePath);
            return content.Contains(ManagedMarker, StringComparison.Ordinal)
                ? null
                : new ConfigurationApplyResult(
                    false,
                    "The Copilot CLI extension file is not managed by this application. "
                    + $"Remove it manually if appropriate: {extensionFilePath}");
        }
        catch (Exception ex) when (
            ex is IOException or UnauthorizedAccessException or NotSupportedException)
        {
            return new ConfigurationApplyResult(
                false,
                $"The Copilot CLI extension file could not be inspected: {ex.Message}");
        }
    }

    public static ConfigurationApplyResult Uninstall(string extensionFilePath)
    {
        if (!File.Exists(extensionFilePath))
        {
            return new ConfigurationApplyResult(
                true,
                "The Copilot CLI extension file is already absent.");
        }

        ConfigurationApplyResult? preflightResult = PreflightUninstall(extensionFilePath);
        if (preflightResult is not null)
        {
            return preflightResult;
        }

        File.Delete(extensionFilePath);
        return new ConfigurationApplyResult(
            true,
            $"Removed Copilot CLI extension file: {extensionFilePath}");
    }

    public static bool IsInstalled(string extensionFilePath, string installedBinaryPath)
    {
        if (!File.Exists(extensionFilePath))
        {
            return false;
        }

        try
        {
            return string.Equals(
                File.ReadAllText(extensionFilePath),
                BuildExtensionSource(installedBinaryPath),
                StringComparison.Ordinal);
        }
        catch (Exception ex) when (
            ex is IOException or UnauthorizedAccessException or NotSupportedException)
        {
            return false;
        }
    }

    internal static string BuildExtensionSource(string installedBinaryPath)
    {
        string serializedBinaryPath = JsonSerializer.Serialize(
            Path.GetFullPath(installedBinaryPath),
            AppJsonSerializerContext.Default.String);

        return $$"""
            {{ManagedMarker}}
            import { spawn } from "node:child_process";
            import { joinSession } from "@github/copilot-sdk/extension";

            const notifierPath = {{serializedBinaryPath}};
            const session = await joinSession();

            const retryDelaysMs = [1000, 5000];
            const claimConflictRetryDelaysMs = [2000, 4000, 8000, 16000, 8000];
            const retryableClaimConflictExitCode = 75;
            const notifierTimeoutMs = 30000;
            let deliveryQueue = Promise.resolve();
            let cwd = process.cwd();
            let activityGeneration = 0;
            let completionPending = false;
            let activeInteractionId = null;
            let rootAssistantIdle = false;
            let lastRootActivityKey = null;
            let lastMainMessage = null;
            let lastTaskSummary = null;
            const deliveredCompletionKeys = new Set();
            const pendingCompletionKeys = new Set();
            const pendingHumanRequests = new Set();
            const queuedRootInteractions = new Set();
            const queuedRootMessagesWithoutInteraction = [];

            function enqueue(
                payload,
                onDelivered = null,
                onFailed = null,
                shouldDeliver = null,
            ) {
                deliveryQueue = deliveryQueue
                    .then(() => invokeNotifierWithRetry(payload, shouldDeliver))
                    .then((delivered) => {
                        if (delivered) {
                            onDelivered?.();
                        }
                    })
                    .catch((error) => {
                        onFailed?.();
                        console.error(
                            `[vscode-copilot-telegram-hook] ${error instanceof Error ? error.message : String(error)}`,
                        );
                    });
            }

            async function invokeNotifierWithRetry(payload, shouldDeliver = null) {
                let lastError = null;
                let retryAttempt = 0;
                let claimConflictRetryAttempt = 0;
                while (true) {
                    if (shouldDeliver && !shouldDeliver()) {
                        return false;
                    }

                    try {
                        await invokeNotifier(payload);
                        return true;
                    } catch (error) {
                        lastError = error;
                        let retryDelay;
                        if (error?.exitCode === retryableClaimConflictExitCode) {
                            if (claimConflictRetryAttempt === claimConflictRetryDelaysMs.length) {
                                break;
                            }

                            retryDelay =
                                claimConflictRetryDelaysMs[claimConflictRetryAttempt];
                            claimConflictRetryAttempt += 1;
                        } else {
                            if (retryAttempt === retryDelaysMs.length) {
                                break;
                            }

                            retryDelay = retryDelaysMs[retryAttempt];
                            retryAttempt += 1;
                        }

                        await new Promise((resolve) => {
                            setTimeout(resolve, retryDelay);
                        });
                    }
                }

                throw lastError;
            }

            function invokeNotifier(payload) {
                return new Promise((resolve, reject) => {
                    const child = spawn(
                        notifierPath,
                        ["copilot-cli", "session-event"],
                        {
                            cwd,
                            stdio: ["pipe", "ignore", "pipe"],
                            windowsHide: true,
                        },
                    );
                    let standardError = "";
                    let settled = false;
                    let timedOut = false;
                    let forceKillTimeout = null;
                    const timeout = setTimeout(() => {
                        timedOut = true;
                        child.kill();
                        forceKillTimeout = setTimeout(() => {
                            child.kill("SIGKILL");
                        }, 5000);
                    }, notifierTimeoutMs);

                    function settle(action) {
                        if (settled) {
                            return;
                        }

                        settled = true;
                        clearTimeout(timeout);
                        if (forceKillTimeout !== null) {
                            clearTimeout(forceKillTimeout);
                        }
                        action();
                    }

                    child.stderr.setEncoding("utf8");
                    child.stderr.on("data", (chunk) => {
                        standardError += chunk;
                    });
                    child.on("error", (error) => {
                        if (timedOut) {
                            return;
                        }

                        settle(() => {
                            reject(error);
                        });
                    });
                    child.on("close", (exitCode) => {
                        settle(() => {
                            if (timedOut) {
                                reject(
                                    new Error(
                                        `notifier timed out after ${notifierTimeoutMs}ms`,
                                    ),
                                );
                                return;
                            }

                            if (exitCode === 0) {
                                resolve();
                                return;
                            }

                            const error = new Error(
                                `notifier exited with code ${exitCode}: ${standardError.trim()}`,
                            );
                            error.exitCode = exitCode;
                            reject(error);
                        });
                    });
                    child.stdin.on("error", (error) => {
                        if (timedOut) {
                            return;
                        }

                        settle(() => {
                            reject(error);
                        });
                    });
                    child.stdin.end(JSON.stringify(payload));
                });
            }

            function addPendingRequest(kind, requestId) {
                if (requestId) {
                    const requestKey = `${kind}:${requestId}`;
                    pendingHumanRequests.add(requestKey);
                    return requestKey;
                }

                return null;
            }

            function serializeRequest(value, fallback) {
                try {
                    return JSON.stringify(value, null, 2);
                } catch {
                    return fallback;
                }
            }

            function removePendingRequest(kind, requestId) {
                if (requestId) {
                    pendingHumanRequests.delete(`${kind}:${requestId}`);
                }
            }

            function isPendingRequest(requestKey) {
                return requestKey === null || pendingHumanRequests.has(requestKey);
            }

            function chooseSummary(generation) {
                const taskSummary =
                    lastTaskSummary?.generation === generation ? lastTaskSummary : null;
                const mainMessage =
                    lastMainMessage?.generation === generation ? lastMainMessage : null;
                if (
                    taskSummary &&
                    (!mainMessage || taskSummary.timestamp >= mainMessage.timestamp)
                ) {
                    return taskSummary;
                }

                return mainMessage;
            }

            function startRootActivity(event, interactionId = null) {
                activityGeneration++;
                completionPending = true;
                activeInteractionId = interactionId;
                rootAssistantIdle = false;
                lastRootActivityKey = event.id;
                lastMainMessage = null;
                lastTaskSummary = null;
            }

            function observeRootActivity(event) {
                if (event.agentId) {
                    return false;
                }

                if (!completionPending) {
                    startRootActivity(event);
                } else {
                    lastRootActivityKey = event.id;
                }

                return true;
            }

            session.on("session.context_changed", (event) => {
                if (!event.agentId && event.data.cwd) {
                    cwd = event.data.cwd;
                }
            });

            session.on("assistant.turn_start", (event) => {
                if (event.agentId) {
                    return;
                }

                const interactionId = event.data.interactionId ?? null;
                if (interactionId && queuedRootInteractions.delete(interactionId)) {
                    startRootActivity(event, interactionId);
                    return;
                }

                if (interactionId && activeInteractionId !== interactionId) {
                    startRootActivity(event, interactionId);
                    return;
                }

                if (
                    !interactionId &&
                    rootAssistantIdle &&
                    queuedRootMessagesWithoutInteraction.length > 0
                ) {
                    queuedRootMessagesWithoutInteraction.shift();
                    startRootActivity(event);
                    return;
                }

                rootAssistantIdle = false;
                observeRootActivity(event);
            });
            session.on("assistant.idle", (event) => {
                if (!event.agentId) {
                    rootAssistantIdle = true;
                }
            });

            session.on("assistant.message", (event) => {
                if (event.agentId || !event.data.content.trim()) {
                    return;
                }

                observeRootActivity(event);
                lastMainMessage = {
                    generation: activityGeneration,
                    key: `assistant:${event.data.messageId}`,
                    messageId: event.data.messageId,
                    source: "assistant.message",
                    summary: event.data.content,
                    timestamp: event.timestamp,
                };
            });

            session.on("session.task_complete", (event) => {
                if (event.agentId || event.data.success === false || !event.data.summary?.trim()) {
                    return;
                }

                observeRootActivity(event);
                lastTaskSummary = {
                    generation: activityGeneration,
                    key: `task-complete:${event.id}`,
                    messageId: null,
                    source: "session.task_complete",
                    summary: event.data.summary,
                    timestamp: event.timestamp,
                };
            });

            session.on("user.message", (event) => {
                if (event.agentId) {
                    return;
                }

                const interactionId = event.data.interactionId ?? null;
                if (event.data.delivery === "queued") {
                    if (interactionId) {
                        queuedRootInteractions.add(interactionId);
                    } else {
                        queuedRootMessagesWithoutInteraction.push(event.id);
                    }
                    return;
                }

                if (event.data.delivery === "steering") {
                    observeRootActivity(event);
                    return;
                }

                startRootActivity(event, interactionId);
            });

            session.on("permission.requested", (event) => {
                if (!event.agentId && !event.data.resolvedByHook) {
                    const requestKey = addPendingRequest(
                        "permission",
                        event.data.requestId,
                    );
                    enqueue(
                        {
                            session_id: session.sessionId,
                            timestamp: event.timestamp,
                            cwd,
                            event_id: event.id,
                            event_type: "permission_requested",
                            summary: "Permission required",
                            message: serializeRequest(
                                event.data.promptRequest ?? event.data.permissionRequest,
                                "Copilot is waiting for permission approval.",
                            ),
                        },
                        null,
                        null,
                        () => isPendingRequest(requestKey),
                    );
                }
            });
            session.on("permission.completed", (event) => {
                if (!event.agentId) {
                    removePendingRequest("permission", event.data.requestId);
                }
            });
            session.on("elicitation.requested", (event) => {
                if (!event.agentId) {
                    const requestKey = addPendingRequest(
                        "elicitation",
                        event.data.requestId,
                    );
                    enqueue(
                        {
                            session_id: session.sessionId,
                            timestamp: event.timestamp,
                            cwd,
                            event_id: event.id,
                            event_type: "elicitation_requested",
                            summary:
                                event.data.elicitationSource ??
                                event.data.mode ??
                                "Additional information required",
                            message: event.data.message,
                        },
                        null,
                        null,
                        () => isPendingRequest(requestKey),
                    );
                }
            });
            session.on("elicitation.completed", (event) => {
                if (!event.agentId) {
                    removePendingRequest("elicitation", event.data.requestId);
                }
            });
            session.on("user_input.requested", (event) => {
                if (event.agentId) {
                    return;
                }

                const requestKey = addPendingRequest(
                    "user-input",
                    event.data.requestId,
                );
                enqueue(
                    {
                        session_id: session.sessionId,
                        timestamp: event.timestamp,
                        cwd,
                        event_id: event.id,
                        event_type: "user_input_requested",
                        message: event.data.question,
                    },
                    null,
                    null,
                    () => isPendingRequest(requestKey),
                );
            });
            session.on("user_input.completed", (event) => {
                if (!event.agentId) {
                    removePendingRequest("user-input", event.data.requestId);
                }
            });

            session.on("session.idle", (event) => {
                if (event.agentId || event.data.aborted || pendingHumanRequests.size > 0) {
                    return;
                }

                if (!completionPending) {
                    return;
                }

                const completionGeneration = activityGeneration;
                const summary = chooseSummary(completionGeneration);
                const completionKey =
                    `activity:${completionGeneration}:${lastRootActivityKey}`;
                if (
                    deliveredCompletionKeys.has(completionKey) ||
                    pendingCompletionKeys.has(completionKey)
                ) {
                    return;
                }

                pendingCompletionKeys.add(completionKey);
                enqueue(
                    {
                        session_id: session.sessionId,
                        timestamp: event.timestamp,
                        cwd,
                        event_id: event.id,
                        event_type: "session_idle",
                        summary: summary?.summary ?? null,
                        summary_source: summary?.source ?? null,
                        message_id: summary?.messageId ?? null,
                    },
                    () => {
                        deliveredCompletionKeys.add(completionKey);
                        pendingCompletionKeys.delete(completionKey);
                        if (activityGeneration === completionGeneration) {
                            completionPending = false;
                        }
                    },
                    () => {
                        pendingCompletionKeys.delete(completionKey);
                    },
                );
            });
            """;
    }

    private static void EnsureParentDirectory(string path)
    {
        string? directoryPath = Path.GetDirectoryName(path);
        if (!string.IsNullOrWhiteSpace(directoryPath))
        {
            if (OperatingSystem.IsWindows())
            {
                Directory.CreateDirectory(directoryPath);
                return;
            }

            Directory.CreateDirectory(directoryPath, OwnerOnlyDirectoryMode);
            File.SetUnixFileMode(directoryPath, OwnerOnlyDirectoryMode);
        }
    }
}
