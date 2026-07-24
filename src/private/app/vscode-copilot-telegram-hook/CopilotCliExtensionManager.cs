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
            return File.ReadAllText(extensionFilePath)
                .Contains(ManagedMarker, StringComparison.Ordinal)
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

    public static ConfigurationApplyResult Uninstall(string extensionFilePath)
    {
        if (!File.Exists(extensionFilePath))
        {
            return new ConfigurationApplyResult(
                true,
                "The Copilot CLI extension file is already absent.");
        }

        ConfigurationApplyResult? preflightResult = PreflightInstall(extensionFilePath);
        if (preflightResult is not null)
        {
            return preflightResult with
            {
                Message =
                    "The Copilot CLI extension file is not managed by this application. "
                    + $"Remove it manually if appropriate: {extensionFilePath}",
            };
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
        => BuildExtensionSource(
            installedBinaryPath,
            AppPaths.GetCopilotCliEventSpoolDirectory(installedBinaryPath));

    internal static string BuildExtensionSource(
        string installedBinaryPath,
        string eventSpoolDirectory)
    {
        string serializedBinaryPath = JsonSerializer.Serialize(
            Path.GetFullPath(installedBinaryPath),
            AppJsonSerializerContext.Default.String);
        string serializedSpoolDirectory = JsonSerializer.Serialize(
            Path.GetFullPath(eventSpoolDirectory),
            AppJsonSerializerContext.Default.String);

        return $$"""
            {{ManagedMarker}}
            import { spawn } from "node:child_process";
            import { createHash, randomUUID } from "node:crypto";
            import {
                closeSync,
                existsSync,
                mkdirSync,
                openSync,
                readdirSync,
                renameSync,
                statSync,
                unlinkSync,
                writeFileSync,
            } from "node:fs";
            import { basename, join } from "node:path";
            import { joinSession } from "@github/copilot-sdk/extension";

            const notifierPath = {{serializedBinaryPath}};
            const eventSpoolDirectory = {{serializedSpoolDirectory}};
            const completionDelayMs = 1000;
            const attentionDelayMs = 250;
            const staleWorkingFileMs = 120000;
            const ownerOnlyDirectoryMode = 0o700;
            const ownerOnlyFileMode = 0o600;

            let cwd = process.cwd();
            let sessionMode = null;
            let activityGeneration = 0;
            let completionPending = false;
            let taskCompleteGeneration = null;
            let activeInteractionId = null;
            let rootAssistantIdle = false;
            let lastRootActivityKey = null;
            let lastMainMessage = null;
            let lastTaskSummary = null;
            let activeCompletionKey = null;
            let pendingQueueItemCount = 0;
            let pendingQueueRefreshCount = 0;
            let deferredIdleEvent = null;
            let queueRefresh = Promise.resolve();
            const pendingHumanRequests = new Map();
            const queuedRootInteractions = new Set();
            const queuedRootMessagesWithoutInteraction = [];
            const activeEventFiles = new Map();

            let session;
            try {
                session = await joinSession({
                    onPermissionRequest() {
                        return { kind: "no-result" };
                    },
                });
            } catch (error) {
                if (
                    !(error instanceof Error)
                    || !error.message.includes("denied permission access")
                ) {
                    throw error;
                }

                console.error(
                    "[vscode-copilot-telegram-hook] permission-request notifications "
                    + "are disabled because elevated extension access was denied",
                );
                session = await joinSession();
            }

            function ensureSpoolDirectory() {
                mkdirSync(eventSpoolDirectory, {
                    recursive: true,
                    mode: ownerOnlyDirectoryMode,
                });
            }

            function getEventFilePath(deliveryKey) {
                const fileName = createHash("sha256")
                    .update(deliveryKey)
                    .digest("hex");
                return join(eventSpoolDirectory, `${fileName}.json`);
            }

            function getCancellationPath(eventFilePath) {
                return `${eventFilePath}.cancelled`;
            }

            function isReadyEventFile(name) {
                return /^[0-9a-f]{64}\.json$/.test(name);
            }

            function isWorkingEventFile(name) {
                return /^[0-9a-f]{64}\.json\.working$/.test(name);
            }

            function spawnWorker(eventFilePath) {
                if (!existsSync(eventFilePath)) {
                    return;
                }

                const child = spawn(
                    notifierPath,
                    [
                        "copilot-cli",
                        "session-event",
                        "--event-file",
                        eventFilePath,
                    ],
                    {
                        cwd: eventSpoolDirectory,
                        detached: true,
                        stdio: "ignore",
                        windowsHide: true,
                    },
                );
                child.on("error", (error) => {
                    console.error(
                        `[vscode-copilot-telegram-hook] failed to start notifier: ${error}`,
                    );
                });
                child.unref();
            }

            function publishEvent(payload, deliveryKey, delayMs) {
                ensureSpoolDirectory();
                const eventFilePath = getEventFilePath(deliveryKey);
                const workingFilePath = `${eventFilePath}.working`;
                if (existsSync(eventFilePath) || existsSync(workingFilePath)) {
                    activeEventFiles.set(deliveryKey, eventFilePath);
                    spawnWorker(eventFilePath);
                    return eventFilePath;
                }

                const tempFilePath = join(
                    eventSpoolDirectory,
                    `.${randomUUID()}.tmp`,
                );
                const eventPayload = {
                    ...payload,
                    deliver_after: new Date(Date.now() + delayMs).toISOString(),
                };
                const tempFile = openSync(tempFilePath, "wx", ownerOnlyFileMode);
                try {
                    writeFileSync(tempFile, JSON.stringify(eventPayload), "utf8");
                } finally {
                    closeSync(tempFile);
                }

                renameSync(tempFilePath, eventFilePath);
                activeEventFiles.set(deliveryKey, eventFilePath);
                spawnWorker(eventFilePath);
                return eventFilePath;
            }

            function cancelDelivery(deliveryKey) {
                const eventFilePath = activeEventFiles.get(deliveryKey);
                if (!eventFilePath) {
                    return;
                }

                try {
                    writeFileSync(
                        getCancellationPath(eventFilePath),
                        "",
                        { flag: "w", mode: ownerOnlyFileMode },
                    );
                } catch (error) {
                    console.error(
                        `[vscode-copilot-telegram-hook] failed to cancel delivery: ${error}`,
                    );
                }
                activeEventFiles.delete(deliveryKey);
            }

            function recoverSpool() {
                ensureSpoolDirectory();
                const now = Date.now();
                for (const entry of readdirSync(eventSpoolDirectory)) {
                    const path = join(eventSpoolDirectory, entry);
                    if (isReadyEventFile(entry)) {
                        spawnWorker(path);
                        continue;
                    }

                    if (!isWorkingEventFile(entry)) {
                        continue;
                    }

                    try {
                        if (now - statSync(path).mtimeMs < staleWorkingFileMs) {
                            continue;
                        }

                        const readyPath = path.slice(0, -".working".length);
                        if (!existsSync(readyPath)) {
                            renameSync(path, readyPath);
                            spawnWorker(readyPath);
                        }
                    } catch (error) {
                        console.error(
                            `[vscode-copilot-telegram-hook] failed to recover ${path}: ${error}`,
                        );
                    }
                }
            }

            function updateSessionMode(mode) {
                if (typeof mode === "string" && mode.trim().length > 0) {
                    sessionMode = mode.trim().toLowerCase();
                }
            }

            function isAutopilotMode() {
                return sessionMode === "autopilot";
            }

            function cancelCompletion() {
                if (activeCompletionKey !== null) {
                    cancelDelivery(activeCompletionKey);
                    activeCompletionKey = null;
                }
            }

            function expireCompletionCancellation(deliveryKey) {
                const timeout = setTimeout(() => {
                    activeEventFiles.delete(deliveryKey);
                    if (activeCompletionKey === deliveryKey) {
                        activeCompletionKey = null;
                    }
                }, completionDelayMs);
                timeout.unref();
            }

            function clearPendingHumanRequests() {
                for (const deliveryKey of pendingHumanRequests.values()) {
                    cancelDelivery(deliveryKey);
                }
                pendingHumanRequests.clear();
            }

            function startRootActivity(event, interactionId = null) {
                cancelCompletion();
                deferredIdleEvent = null;
                activityGeneration++;
                completionPending = true;
                taskCompleteGeneration = null;
                activeInteractionId = interactionId;
                rootAssistantIdle = false;
                lastRootActivityKey = event.id;
                lastMainMessage = null;
                lastTaskSummary = null;
            }

            function observeRootActivity(event) {
                if (event.agentId) {
                    return;
                }

                if (!completionPending) {
                    startRootActivity(event);
                } else {
                    lastRootActivityKey = event.id;
                }
            }

            function chooseSummary(generation) {
                const taskSummary =
                    lastTaskSummary?.generation === generation ? lastTaskSummary : null;
                const mainMessage =
                    lastMainMessage?.generation === generation ? lastMainMessage : null;
                if (
                    taskSummary
                    && (!mainMessage || taskSummary.timestamp >= mainMessage.timestamp)
                ) {
                    return taskSummary;
                }
                return mainMessage;
            }

            function addPendingRequest(kind, requestId, eventId) {
                const requestKey =
                    typeof requestId === "string" && requestId.length > 0
                        ? `${kind}:${requestId}`
                        : `${kind}:event:${eventId}`;
                const deliveryKey = `attention:${session.sessionId}:${requestKey}`;
                pendingHumanRequests.set(requestKey, deliveryKey);
                return { requestKey, deliveryKey };
            }

            function removePendingRequest(kind, requestId) {
                const prefix = `${kind}:`;
                for (const [requestKey, deliveryKey] of pendingHumanRequests) {
                    if (
                        (typeof requestId === "string" && requestId.length > 0)
                            ? requestKey === `${kind}:${requestId}`
                            : requestKey.startsWith(prefix)
                    ) {
                        cancelDelivery(deliveryKey);
                        pendingHumanRequests.delete(requestKey);
                    }
                }
            }

            function sanitizeText(value, fallback) {
                if (typeof value !== "string") {
                    return fallback;
                }

                const normalized = value.replace(/\s+/g, " ").trim();
                if (normalized.length === 0) {
                    return fallback;
                }
                return normalized.length <= 400
                    ? normalized
                    : `${normalized.slice(0, 397)}...`;
            }

            function summarizePermission(event) {
                const request =
                    event.data.permissionRequest ?? event.data.promptRequest ?? {};
                const details = [];
                if (typeof request.kind === "string") {
                    details.push(`Type: ${sanitizeText(request.kind, "permission")}`);
                }
                if (typeof request.intention === "string") {
                    details.push(
                        `Reason: ${sanitizeText(request.intention, "approval required")}`,
                    );
                }
                if (typeof request.fileName === "string") {
                    details.push(`File: ${basename(request.fileName)}`);
                }
                return details.length > 0
                    ? details.join("\n")
                    : "Copilot is waiting for permission approval.";
            }

            function publishAttention(
                event,
                kind,
                eventType,
                summary,
                message,
            ) {
                if (event.agentId) {
                    return;
                }

                cancelCompletion();
                const { deliveryKey } = addPendingRequest(
                    kind,
                    event.data.requestId,
                    event.id,
                );
                publishEvent(
                    {
                        session_id: session.sessionId,
                        timestamp: event.timestamp,
                        cwd,
                        event_id: event.id,
                        event_type: eventType,
                        summary,
                        message,
                    },
                    deliveryKey,
                    attentionDelayMs,
                );
            }

            session.on("session.context_changed", (event) => {
                if (!event.agentId && event.data.cwd) {
                    cwd = event.data.cwd;
                }
            });
            session.on("session.mode_changed", (event) => {
                if (!event.agentId) {
                    updateSessionMode(event.data.newMode ?? event.data.mode);
                }
            });
            session.on("assistant.turn_start", (event) => {
                if (event.agentId) {
                    return;
                }

                const interactionId = event.data.interactionId ?? null;
                if (interactionId && queuedRootInteractions.delete(interactionId)) {
                    pendingQueueItemCount = Math.max(0, pendingQueueItemCount - 1);
                    startRootActivity(event, interactionId);
                    return;
                }
                if (interactionId && interactionId !== activeInteractionId) {
                    startRootActivity(event, interactionId);
                    return;
                }
                if (
                    !interactionId
                    && rootAssistantIdle
                    && queuedRootMessagesWithoutInteraction.length > 0
                ) {
                    queuedRootMessagesWithoutInteraction.shift();
                    pendingQueueItemCount = Math.max(0, pendingQueueItemCount - 1);
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
                if (event.agentId || !event.data.content?.trim()) {
                    return;
                }

                observeRootActivity(event);
                lastMainMessage = {
                    generation: activityGeneration,
                    summary: event.data.content,
                    source: "assistant.message",
                    timestamp: event.timestamp,
                };
            });
            session.on("session.task_complete", (event) => {
                if (event.agentId) {
                    return;
                }

                observeRootActivity(event);
                taskCompleteGeneration = activityGeneration;
                if (event.data.summary?.trim()) {
                    lastTaskSummary = {
                        generation: activityGeneration,
                        summary: event.data.summary,
                        source: "session.task_complete",
                        timestamp: event.timestamp,
                    };
                }
            });
            session.on("user.message", (event) => {
                if (event.agentId) {
                    return;
                }

                updateSessionMode(event.data.agentMode);
                cancelCompletion();
                const interactionId = event.data.interactionId ?? null;
                if (event.data.delivery === "queued") {
                    pendingQueueItemCount++;
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
            session.on("pending_messages.modified", (event) => {
                if (event.agentId) {
                    return;
                }

                pendingQueueRefreshCount++;
                queueRefresh = queueRefresh
                    .then(async () => {
                        let refreshed = false;
                        try {
                            const snapshot = await session.rpc.queue.pendingItems();
                            pendingQueueItemCount = snapshot.items.length;
                            refreshed = true;
                            if (pendingQueueItemCount === 0) {
                                queuedRootInteractions.clear();
                                queuedRootMessagesWithoutInteraction.length = 0;
                            }
                        } catch (error) {
                            console.error(
                                "[vscode-copilot-telegram-hook] failed to refresh pending "
                                + `messages: ${error}`,
                            );
                        } finally {
                            pendingQueueRefreshCount--;
                        }

                        if (
                            refreshed
                            && pendingQueueRefreshCount === 0
                            && pendingQueueItemCount === 0
                        ) {
                            const idleEvent = deferredIdleEvent;
                            deferredIdleEvent = null;
                            if (idleEvent !== null) {
                                handleSessionIdle(idleEvent);
                            }
                        }
                    });
            });

            session.on("permission.requested", (event) => {
                if (!event.data.resolvedByHook) {
                    publishAttention(
                        event,
                        "permission",
                        "permission_requested",
                        "Permission required",
                        summarizePermission(event),
                    );
                }
            });
            session.on("permission.completed", (event) => {
                if (!event.agentId) {
                    removePendingRequest("permission", event.data.requestId);
                }
            });
            session.on("elicitation.requested", (event) => {
                publishAttention(
                    event,
                    "elicitation",
                    "elicitation_requested",
                    event.data.elicitationSource
                        ?? event.data.mode
                        ?? "Additional information required",
                    sanitizeText(
                        event.data.message,
                        "Copilot is waiting for additional information.",
                    ),
                );
            });
            session.on("elicitation.completed", (event) => {
                if (!event.agentId) {
                    removePendingRequest("elicitation", event.data.requestId);
                }
            });
            session.on("user_input.requested", (event) => {
                publishAttention(
                    event,
                    "user-input",
                    "user_input_requested",
                    "Input required",
                    sanitizeText(event.data.question, "Copilot is waiting for input."),
                );
            });
            session.on("user_input.completed", (event) => {
                if (!event.agentId) {
                    removePendingRequest("user-input", event.data.requestId);
                }
            });
            session.on("exit_plan_mode.requested", (event) => {
                publishAttention(
                    event,
                    "exit-plan-mode",
                    "exit_plan_mode_requested",
                    "Plan approval required",
                    sanitizeText(event.data.summary, "Copilot is waiting for plan approval."),
                );
            });
            session.on("exit_plan_mode.completed", (event) => {
                if (!event.agentId) {
                    removePendingRequest("exit-plan-mode", event.data.requestId);
                }
            });
            session.on("auto_mode_switch.requested", (event) => {
                publishAttention(
                    event,
                    "auto-mode-switch",
                    "auto_mode_switch_requested",
                    "Model switch approval required",
                    sanitizeText(
                        event.data.errorCode,
                        "Copilot is waiting for model switch approval.",
                    ),
                );
            });
            session.on("auto_mode_switch.completed", (event) => {
                if (!event.agentId) {
                    removePendingRequest("auto-mode-switch", event.data.requestId);
                }
            });
            session.on("session_limits_exhausted.requested", (event) => {
                publishAttention(
                    event,
                    "session-limits",
                    "session_limits_exhausted_requested",
                    "Session limit reached",
                    `AI credits used: ${event.data.usedAiCredits}`
                        + ` / ${event.data.maxAiCredits}`,
                );
            });
            session.on("session_limits_exhausted.completed", (event) => {
                if (!event.agentId) {
                    removePendingRequest("session-limits", event.data.requestId);
                }
            });
            session.on("mcp.oauth_required", (event) => {
                publishAttention(
                    event,
                    "mcp-oauth",
                    "mcp_oauth_required",
                    "MCP authorization required",
                    sanitizeText(
                        event.data.serverName,
                        "Copilot is waiting for MCP authorization.",
                    ),
                );
            });
            session.on("mcp.oauth_completed", (event) => {
                if (!event.agentId) {
                    removePendingRequest("mcp-oauth", event.data.requestId);
                }
            });

            function handleSessionIdle(event) {
                if (event.agentId) {
                    return;
                }
                if (event.data.aborted) {
                    cancelCompletion();
                    clearPendingHumanRequests();
                    completionPending = false;
                    pendingQueueItemCount = 0;
                    deferredIdleEvent = null;
                    queuedRootInteractions.clear();
                    queuedRootMessagesWithoutInteraction.length = 0;
                    return;
                }
                if (pendingQueueRefreshCount > 0 || pendingQueueItemCount > 0) {
                    deferredIdleEvent = event;
                    return;
                }
                if (
                    !completionPending
                    || pendingHumanRequests.size > 0
                    || (
                        isAutopilotMode()
                        && taskCompleteGeneration !== activityGeneration
                    )
                ) {
                    return;
                }

                const summary = chooseSummary(activityGeneration);
                const completionKey =
                    `completion:${session.sessionId}:${activityGeneration}:`
                    + `${lastRootActivityKey}`;
                activeCompletionKey = completionKey;
                publishEvent(
                    {
                        session_id: session.sessionId,
                        timestamp: event.timestamp,
                        cwd,
                        event_id: event.id,
                        event_type: "session_idle",
                        summary: summary?.summary ?? null,
                        summary_source: summary?.source ?? null,
                    },
                    completionKey,
                    completionDelayMs,
                );
                expireCompletionCancellation(completionKey);
                completionPending = false;
                deferredIdleEvent = null;
            }

            session.on("session.idle", (event) => {
                handleSessionIdle(event);
            });

            recoverSpool();
            """;
    }

    private static void EnsureParentDirectory(string path)
    {
        string? directoryPath = Path.GetDirectoryName(path);
        if (string.IsNullOrWhiteSpace(directoryPath))
        {
            return;
        }

        if (OperatingSystem.IsWindows())
        {
            Directory.CreateDirectory(directoryPath);
            return;
        }

        Directory.CreateDirectory(directoryPath, OwnerOnlyDirectoryMode);
        File.SetUnixFileMode(directoryPath, OwnerOnlyDirectoryMode);
    }
}
