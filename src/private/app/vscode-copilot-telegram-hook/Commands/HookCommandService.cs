using System.Text.Json;
using System.Text.Json.Serialization.Metadata;
using Hcoona.VsCodeCopilotTelegramHook.Logging;
using Hcoona.VsCodeCopilotTelegramHook.Notifications;
using Hcoona.VsCodeCopilotTelegramHook.State;
using Microsoft.Extensions.Logging;

namespace Hcoona.VsCodeCopilotTelegramHook.Commands;

internal sealed class HookCommandService(
    WorkspaceStateStore workspaceStateStore,
    TelegramBotClient telegramBotClient,
    TelegramCredentialProvider telegramCredentialProvider,
    GitRepositoryProbe gitRepositoryProbe,
    SessionLogFileContext sessionLogFileContext,
    ILogger<HookCommandService> logger)
{
    public async Task<int> HandleSessionStartAsync(
        Stream standardInput,
        Stream standardOutput,
        CancellationToken cancellationToken)
    {
        IDisposable? logScope = null;
        try
        {
            byte[] payload = await ReadPayloadAsync(standardInput, cancellationToken);
            SessionStartHookInput? hookInput = DeserializePayload(
                payload,
                AppJsonSerializerContext.Default.SessionStartHookInput);

            string? workspacePath = GetWorkspacePathOrNull(hookInput?.Cwd);
            logScope = TryOpenHookLogScope(workspacePath, hookInput?.SessionId);

            if (hookInput is null
                || workspacePath is null
                || string.IsNullOrWhiteSpace(hookInput.SessionId))
            {
                string reason = BuildInvalidInputReason(
                    hookInput,
                    payload,
                    ("cwd", workspacePath is null),
                    ("session_id", string.IsNullOrWhiteSpace(hookInput?.SessionId)));
                AppLog.IgnoringInvalidHookInput(logger, "SessionStart", reason);
                await Console.Error.WriteLineAsync($"SessionStart hook warning: {reason}");
                return 0;
            }

            AppLog.HandlingSessionStart(logger, hookInput.SessionId, workspacePath);

            SessionState sessionState = await workspaceStateStore.InitializeSessionAsync(
                hookInput,
                cancellationToken);
            HookResponse response = new()
            {
                HookSpecificOutput = new HookSpecificOutput
                {
                    HookEventName = "SessionStart",
                    AdditionalContext = BuildAdditionalContext(sessionState),
                },
            };

            await JsonSerializer.SerializeAsync(
                standardOutput,
                response,
                AppJsonSerializerContext.Default.HookResponse,
                cancellationToken);
            AppLog.WroteSessionStartContext(logger, hookInput.SessionId);
        }
        catch (Exception ex)
        {
            AppLog.SessionStartFailed(logger, ex);
            await Console.Error.WriteLineAsync($"SessionStart hook warning: {ex.Message}");
        }
        finally
        {
            logScope?.Dispose();
        }

        return 0;
    }

    public async Task<int> HandleUserPromptSubmitAsync(
        Stream standardInput,
        CancellationToken cancellationToken)
    {
        IDisposable? logScope = null;
        try
        {
            byte[] payload = await ReadPayloadAsync(standardInput, cancellationToken);
            UserPromptSubmitHookInput? hookInput = DeserializePayload(
                payload,
                AppJsonSerializerContext.Default.UserPromptSubmitHookInput);

            string? workspacePath = GetWorkspacePathOrNull(hookInput?.Cwd);
            logScope = TryOpenHookLogScope(workspacePath, hookInput?.SessionId);

            if (hookInput is null
                || workspacePath is null
                || string.IsNullOrWhiteSpace(hookInput.SessionId))
            {
                string reason = BuildInvalidInputReason(
                    hookInput,
                    payload,
                    ("cwd", workspacePath is null),
                    ("session_id", string.IsNullOrWhiteSpace(hookInput?.SessionId)));
                AppLog.IgnoringInvalidHookInput(logger, "UserPromptSubmit", reason);
                await Console.Error.WriteLineAsync($"UserPromptSubmit hook warning: {reason}");
                return 0;
            }

            AppLog.HandlingUserPromptSubmit(
                logger,
                hookInput.SessionId,
                workspacePath,
                hookInput.Prompt?.Length ?? 0);
            _ = await workspaceStateStore.StartTurnAsync(hookInput, cancellationToken);
        }
        catch (Exception ex)
        {
            AppLog.UserPromptSubmitFailed(logger, ex);
            await Console.Error.WriteLineAsync($"UserPromptSubmit hook warning: {ex.Message}");
        }
        finally
        {
            logScope?.Dispose();
        }

        return 0;
    }

    public async Task<int> HandleStopAsync(
        Stream standardInput,
        CancellationToken cancellationToken)
    {
        IDisposable? logScope = null;
        try
        {
            byte[] payload = await ReadPayloadAsync(standardInput, cancellationToken);
            StopHookInput? hookInput = DeserializePayload(
                payload,
                AppJsonSerializerContext.Default.StopHookInput);

            string? workspacePath = GetWorkspacePathOrNull(hookInput?.Cwd);
            logScope = TryOpenHookLogScope(workspacePath, hookInput?.SessionId);

            if (hookInput is null
                || workspacePath is null
                || string.IsNullOrWhiteSpace(hookInput.SessionId)
                || string.IsNullOrWhiteSpace(hookInput.Timestamp))
            {
                string reason = BuildInvalidInputReason(
                    hookInput,
                    payload,
                    ("cwd", workspacePath is null),
                    ("session_id", string.IsNullOrWhiteSpace(hookInput?.SessionId)),
                    ("timestamp", string.IsNullOrWhiteSpace(hookInput?.Timestamp)));
                AppLog.IgnoringInvalidHookInput(logger, "Stop", reason);
                await Console.Error.WriteLineAsync($"Stop hook warning: {reason}");
                return 0;
            }

            AppLog.HandlingStopHook(logger, hookInput.SessionId, workspacePath);

            _ = await workspaceStateStore.TryReadSessionAsync(
                workspacePath,
                hookInput.SessionId,
                cancellationToken);
            TurnState? turnState = await workspaceStateStore.TryReadTurnAsync(
                workspacePath,
                hookInput.SessionId,
                cancellationToken);
            SummaryRecord? summaryRecord = await workspaceStateStore.TryReadSummaryAsync(
                workspacePath,
                hookInput.SessionId,
                cancellationToken);

            if (await workspaceStateStore.WasStopAlreadySentAsync(
                workspacePath,
                hookInput.SessionId,
                turnState?.TurnId,
                hookInput.Timestamp,
                cancellationToken))
            {
                AppLog.SkippingDuplicateStop(
                    logger,
                    hookInput.SessionId,
                    turnState?.TurnId ?? "<unknown>");
                return 0;
            }

            if (summaryRecord is not null
                && (!string.Equals(
                        summaryRecord.SessionId,
                        hookInput.SessionId,
                        StringComparison.Ordinal)
                    || turnState is null
                    || !string.Equals(
                        summaryRecord.TurnId,
                        turnState.TurnId,
                        StringComparison.Ordinal)))
            {
                summaryRecord = null;
            }

            string turnId = turnState?.TurnId
                ?? summaryRecord?.TurnId
                ?? CreateStopFallbackTurnId(hookInput.Timestamp);

            GitRepositoryMetadata? repositoryMetadata = await gitRepositoryProbe.TryProbeAsync(
                workspacePath,
                cancellationToken);

            TelegramCredentials credentials =
                await telegramCredentialProvider.ResolveAsync(cancellationToken);

            NotificationContext context = new()
            {
                SessionId = hookInput.SessionId,
                TurnId = turnId,
                StopTimestamp = hookInput.Timestamp,
                SentAt = workspaceStateStore.GetCurrentUtcTimestamp(),
                WorkspacePath = workspacePath,
                HostName = Environment.MachineName,
                ExecutionEnvironment = AppPaths.GetExecutionEnvironmentDisplay(),
                RepositoryName = repositoryMetadata?.RepositoryName,
                BranchName = repositoryMetadata?.BranchName,
                CommitId = ShortCommit(repositoryMetadata?.CommitId),
                TranscriptPath = hookInput.TranscriptPath,
            };

            IReadOnlyList<string> messages = NotificationComposer.Compose(
                context,
                summaryRecord);
            AppLog.SendingStopNotification(
                logger,
                messages.Count,
                context.SessionId,
                context.TurnId);
            await telegramBotClient.SendMessagesAsync(credentials, messages, cancellationToken);
            await WorkspaceStateStore.RecordNotificationAsync(
                hookInput,
                context,
                summaryRecord,
                cancellationToken);
            AppLog.RecordedStopNotification(logger, context.SessionId, context.TurnId);
        }
        catch (Exception ex)
        {
            AppLog.StopHookFailed(logger, ex);
            await Console.Error.WriteLineAsync($"Stop hook warning: {ex.Message}");
        }
        finally
        {
            logScope?.Dispose();
        }

        return 0;
    }

    private static string BuildAdditionalContext(SessionState sessionState)
    {
        string turnStatePath = AppPaths.GetRelativeTurnStatePath(sessionState.SessionId);
        string summaryStatePath = AppPaths.GetRelativeSummaryStatePath(sessionState.SessionId);

        return string.Join(
            " ",
            [
                "Notification summary handoff is enabled for this workspace.",
                $"Your session_id is {sessionState.SessionId}.",
                $"Before you finish each task, read {turnStatePath}",
                $"and overwrite {summaryStatePath} with valid JSON.",
                "Copy session_id and turn_id from the turn state file,",
                "and write the summary field as concise human-readable text,",
                "preferably in Chinese on a best-effort basis.",
            ]);
    }

    private static string CreateStopFallbackTurnId(string timestamp)
    {
        string normalized = new(timestamp.Where(char.IsLetterOrDigit).ToArray());
        return string.IsNullOrWhiteSpace(normalized)
            ? $"stop-{Guid.NewGuid():n}"
            : $"stop-{normalized.ToLowerInvariant()}";
    }

    private static string? ShortCommit(string? commitId)
    {
        if (string.IsNullOrWhiteSpace(commitId))
        {
            return null;
        }

        return commitId.Length <= 12 ? commitId : commitId[..12];
    }

    private IDisposable? TryOpenHookLogScope(string? workspacePath, string? sessionId)
    {
        if (string.IsNullOrWhiteSpace(workspacePath))
        {
            return null;
        }

        string logFilePath = string.IsNullOrWhiteSpace(sessionId)
            ? AppPaths.GetWorkspaceLogPath(workspacePath)
            : AppPaths.GetSessionLogPath(workspacePath, sessionId);
        return sessionLogFileContext.UseLogFile(logFilePath);
    }

    private static string? GetWorkspacePathOrNull(string? cwd)
        => string.IsNullOrWhiteSpace(cwd) ? null : Path.GetFullPath(cwd);

    private static string BuildInvalidInputReason<T>(
        T? hookInput,
        ReadOnlyMemory<byte> payload,
        params (string FieldName, bool IsMissing)[] fieldChecks)
        where T : class
    {
        string? payloadShape = TryDescribePayloadShape(payload);

        if (hookInput is null)
        {
            return payloadShape is null
                ? "payload could not be deserialized."
                : $"payload could not be deserialized; {payloadShape}";
        }

        string[] missingFields = fieldChecks
            .Where(static fieldCheck => fieldCheck.IsMissing)
            .Select(static fieldCheck => fieldCheck.FieldName)
            .ToArray();

        string reason = missingFields.Length == 0
            ? "payload could not be processed."
            : $"missing required field(s): {string.Join(", ", missingFields)}.";

        return payloadShape is null ? reason : $"{reason} {payloadShape}";
    }

    private static T? DeserializePayload<T>(
        ReadOnlyMemory<byte> payload,
        JsonTypeInfo<T> jsonTypeInfo)
        where T : class
    {
        if (payload.IsEmpty)
        {
            return null;
        }

        return JsonSerializer.Deserialize(payload.Span, jsonTypeInfo);
    }

    private static async Task<byte[]> ReadPayloadAsync(
        Stream standardInput,
        CancellationToken cancellationToken)
    {
        using MemoryStream buffer = new();
        await standardInput.CopyToAsync(buffer, cancellationToken);
        return buffer.ToArray();
    }

    private static string? TryDescribePayloadShape(ReadOnlyMemory<byte> payload)
    {
        if (payload.IsEmpty)
        {
            return "payload was empty.";
        }

        try
        {
            using JsonDocument document = JsonDocument.Parse(payload);
            JsonElement root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object)
            {
                return $"payload root kind was {root.ValueKind}.";
            }

            string[] propertyNames = root
                .EnumerateObject()
                .Select(static property => property.Name)
                .OrderBy(static name => name, StringComparer.Ordinal)
                .ToArray();

            return propertyNames.Length == 0
                ? "payload object had no top-level fields."
                : $"present top-level field(s): {string.Join(", ", propertyNames)}.";
        }
        catch (JsonException)
        {
            return null;
        }
    }
}
