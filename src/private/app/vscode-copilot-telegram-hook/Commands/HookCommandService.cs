using System.Globalization;
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
    HookExecutionContext hookExecutionContext,
    ILogger<HookCommandService> logger)
{
    private const string UtcTimestampFormat = "yyyy-MM-ddTHH:mm:ss.fff'Z'";

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

            await WriteSessionStartResponseAsync(
                standardOutput,
                BuildAdditionalContext(sessionState),
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
        Stream standardOutput,
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

            TurnState? turnState = await workspaceStateStore.TryReadTurnAsync(
                workspacePath,
                hookInput.SessionId,
                cancellationToken);

            string turnId = turnState?.TurnId
                ?? CreateStopFallbackTurnId(hookInput.Timestamp);

            SummaryValidationResult summaryValidation =
                await ValidateSummaryAsync(
                    workspacePath,
                    hookInput.SessionId,
                    turnState,
                    cancellationToken);

            if (!summaryValidation.IsValid && turnState is not null)
            {
                bool incrementFailureCount = ShouldIncrementStopValidationFailureCount(
                    turnState,
                    hookInput);
                TurnState updatedTurnState =
                    await workspaceStateStore.RecordStopValidationFailureAsync(
                        workspacePath,
                        turnState,
                        hookInput.Timestamp,
                        summaryValidation.FailureReason!,
                        incrementFailureCount,
                        cancellationToken);

                if (updatedTurnState.StopValidationFailureCount
                    < AppConstants.MaxStopSummaryValidationFailures)
                {
                    string blockingReason = BuildStopBlockingReason(
                        updatedTurnState,
                        summaryValidation.FailureReason!);
                    AppLog.BlockingStopForSummaryValidation(
                        logger,
                        hookInput.SessionId,
                        updatedTurnState.TurnId,
                        updatedTurnState.StopValidationFailureCount,
                        summaryValidation.FailureReason!);
                    await WriteStopBlockResponseAsync(
                        standardOutput,
                        blockingReason,
                        cancellationToken);
                    return 0;
                }

                AppLog.AllowingStopAfterValidationFailures(
                    logger,
                    hookInput.SessionId,
                    updatedTurnState.TurnId,
                    summaryValidation.FailureReason!);
                turnState = updatedTurnState;
            }

            SummaryRecord? summaryRecord = summaryValidation.IsValid
                ? summaryValidation.Record
                : null;

            if (await workspaceStateStore.WasStopAlreadySentAsync(
                workspacePath,
                hookInput.SessionId,
                turnId,
                hookInput.Timestamp,
                cancellationToken))
            {
                AppLog.SkippingDuplicateStop(
                    logger,
                    hookInput.SessionId,
                    turnId);
                return 0;
            }

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
                $"and overwrite {summaryStatePath} with valid JSON for the current turn.",
                "Copy session_id and turn_id from the turn-state file.",
                "updated_at must be a UTC timestamp in yyyy-MM-ddTHH:mm:ss.fffZ format.",
                "summary must be a concise human-readable sentence,",
                "preferably Chinese on a best-effort basis.",
                "details, changed_files, and next_steps must be JSON arrays.",
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

    private static async Task<SummaryValidationResult> ValidateSummaryAsync(
        string workspacePath,
        string sessionId,
        TurnState? turnState,
        CancellationToken cancellationToken)
    {
        if (turnState is null)
        {
            return SummaryValidationResult.AllowWithoutValidation();
        }

        string summaryPath = AppPaths.GetSummaryStatePath(workspacePath, sessionId);
        string summaryDisplayPath = AppPaths.GetRelativeSummaryStatePath(sessionId);
        if (!File.Exists(summaryPath))
        {
            return SummaryValidationResult.Invalid(
            $"Summary file is missing at '{summaryDisplayPath}'.");
        }

        SummaryRecord? summaryRecord;
        try
        {
            await using FileStream stream = File.OpenRead(summaryPath);
            summaryRecord = await JsonSerializer.DeserializeAsync(
                stream,
                AppJsonSerializerContext.Default.SummaryRecord,
                cancellationToken);
        }
        catch (Exception ex) when (
            ex is IOException or JsonException or UnauthorizedAccessException
                or NotSupportedException)
        {
            return SummaryValidationResult.Invalid(
                $"Summary file '{summaryDisplayPath}' could not be parsed as JSON: {ex.Message}");
        }

        if (summaryRecord is null)
        {
            return SummaryValidationResult.Invalid(
                $"Summary file '{summaryDisplayPath}' is empty or does not contain a JSON object.");
        }

        List<string> failures = [];
        if (!string.Equals(summaryRecord.SessionId, turnState.SessionId, StringComparison.Ordinal))
        {
            failures.Add($"session_id must equal '{turnState.SessionId}'");
        }

        if (!string.Equals(summaryRecord.TurnId, turnState.TurnId, StringComparison.Ordinal))
        {
            failures.Add($"turn_id must equal '{turnState.TurnId}'");
        }

        if (!IsValidUtcTimestamp(summaryRecord.UpdatedAt))
        {
            failures.Add(
                "updated_at must be a UTC timestamp in yyyy-MM-ddTHH:mm:ss.fffZ format");
        }

        if (string.IsNullOrWhiteSpace(summaryRecord.Summary))
        {
            failures.Add("summary must be a non-empty human-readable sentence");
        }

        if (failures.Count > 0)
        {
            return SummaryValidationResult.Invalid(
                $"Summary file '{summaryDisplayPath}' is invalid: {string.Join("; ", failures)}.");
        }

        return SummaryValidationResult.Valid(summaryRecord);
    }

    private static bool ShouldIncrementStopValidationFailureCount(
        TurnState turnState,
        StopHookInput hookInput)
    {
        if (turnState.StopValidationFailureCount == 0)
        {
            return true;
        }

        return !string.Equals(
            turnState.LastStopValidationFailureTimestamp,
            hookInput.Timestamp,
            StringComparison.Ordinal);
    }

    private static string BuildStopBlockingReason(TurnState turnState, string failureReason)
    {
        string turnPath = AppPaths.GetRelativeTurnStatePath(turnState.SessionId);
        string summaryPath = AppPaths.GetRelativeSummaryStatePath(turnState.SessionId);
        int attempt = turnState.StopValidationFailureCount;

        return string.Join(
            " ",
            [
                $"Summary validation failed for the current turn (attempt {attempt} of "
                + $"{AppConstants.MaxStopSummaryValidationFailures}).",
                failureReason,
                $"Read {turnPath} and overwrite {summaryPath} with valid JSON for this turn.",
                $"The file must keep session_id='{turnState.SessionId}' and "
                + $"turn_id='{turnState.TurnId}'.",
                "updated_at must be a UTC timestamp in yyyy-MM-ddTHH:mm:ss.fffZ format.",
                "summary must be a concise human-readable sentence, "
                + "preferably Chinese on a best-effort basis.",
                "details, changed_files, and next_steps must be JSON arrays.",
            ]);
    }

    private static bool IsValidUtcTimestamp(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        return DateTimeOffset.TryParseExact(
                value,
                UtcTimestampFormat,
                CultureInfo.InvariantCulture,
                DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal,
                out DateTimeOffset parsed)
            && string.Equals(
                parsed.ToString(UtcTimestampFormat, CultureInfo.InvariantCulture),
                value,
                StringComparison.Ordinal);
    }

    private async Task WriteSessionStartResponseAsync(
        Stream standardOutput,
        string additionalContext,
        CancellationToken cancellationToken)
    {
        if (hookExecutionContext.GetSurface() == HookSurface.CopilotCli)
        {
            await WriteCopilotCliHookOutputAsync(
                standardOutput,
                new CopilotCliHookOutput
                {
                    AdditionalContext = additionalContext,
                },
                cancellationToken);
            return;
        }

        await WriteVsCodeHookResponseAsync(
            standardOutput,
            new HookResponse
            {
                HookSpecificOutput = new HookSpecificOutput
                {
                    HookEventName = "SessionStart",
                    AdditionalContext = additionalContext,
                },
            },
            cancellationToken);
    }

    private async Task WriteStopBlockResponseAsync(
        Stream standardOutput,
        string reason,
        CancellationToken cancellationToken)
    {
        if (hookExecutionContext.GetSurface() == HookSurface.CopilotCli)
        {
            await WriteCopilotCliHookOutputAsync(
                standardOutput,
                new CopilotCliHookOutput
                {
                    Decision = "block",
                    Reason = reason,
                },
                cancellationToken);
            return;
        }

        await WriteVsCodeHookResponseAsync(
            standardOutput,
            new HookResponse
            {
                HookSpecificOutput = new HookSpecificOutput
                {
                    HookEventName = "Stop",
                    Decision = "block",
                    Reason = reason,
                },
            },
            cancellationToken);
    }

    private static async Task WriteVsCodeHookResponseAsync(
        Stream standardOutput,
        HookResponse response,
        CancellationToken cancellationToken)
    {
        await JsonSerializer.SerializeAsync(
            standardOutput,
            response,
            AppJsonSerializerContext.Default.HookResponse,
            cancellationToken);
    }

    private static async Task WriteCopilotCliHookOutputAsync(
        Stream standardOutput,
        CopilotCliHookOutput output,
        CancellationToken cancellationToken)
    {
        await JsonSerializer.SerializeAsync(
            standardOutput,
            output,
            AppJsonSerializerContext.Default.CopilotCliHookOutput,
            cancellationToken);
    }

    private sealed record SummaryValidationResult(
        bool IsValid,
        SummaryRecord? Record,
        string? FailureReason)
    {
        public static SummaryValidationResult AllowWithoutValidation()
            => new(true, null, null);

        public static SummaryValidationResult Valid(SummaryRecord record)
            => new(true, record, null);

        public static SummaryValidationResult Invalid(string failureReason)
            => new(false, null, failureReason);
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
