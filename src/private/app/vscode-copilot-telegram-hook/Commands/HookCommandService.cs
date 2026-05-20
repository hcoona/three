using System.Globalization;
using System.Security.Cryptography;
using System.Text;
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

            NotificationSession session = await workspaceStateStore.InitializeSessionAsync(
                hookInput,
                cancellationToken);

            await WriteAdditionalContextResponseAsync(
                standardOutput,
                "SessionStart",
                BuildProtocolOverviewContext(session),
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
        Stream standardOutput,
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

            PromptClassification classification = ClassifyPrompt(hookInput);
            PromptObservation observation = await workspaceStateStore.RecordPromptObservationAsync(
                hookInput,
                classification,
                cancellationToken);
            if (!classification.IsHighConfidenceMainPrompt)
            {
                return 0;
            }

            NotificationTurn turn = await workspaceStateStore.CreateNotificationTurnAsync(
                hookInput,
                observation,
                cancellationToken);
            await WriteAdditionalContextResponseAsync(
                standardOutput,
                "UserPromptSubmit",
                BuildNotificationAssignmentContext(workspacePath, turn),
                cancellationToken);
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

            IReadOnlyList<NotificationTurn> openTurns =
                await workspaceStateStore.ListOpenTurnsAsync(
                    workspacePath,
                    hookInput.SessionId,
                    cancellationToken);
            IReadOnlyList<PromptObservation> promptObservations =
                await workspaceStateStore.ListPromptObservationsAsync(
                    workspacePath,
                    hookInput.SessionId,
                    cancellationToken);
            IReadOnlyList<NotificationRecord> sessionNotificationRecords =
                await workspaceStateStore.ListSessionNotificationRecordsAsync(
                    workspacePath,
                    hookInput.SessionId,
                    cancellationToken);
            StopResolution resolution = ResolveStopTurn(
                openTurns,
                promptObservations,
                sessionNotificationRecords,
                hookInput.Timestamp);

            if (resolution.Turn is null)
            {
                await SendSessionLevelFallbackAsync(
                    hookInput,
                    workspacePath,
                    resolution.Reason,
                    cancellationToken);
                return 0;
            }

            NotificationTurn turn = resolution.Turn;
            string notificationKey = CreateStopNotificationKey(hookInput.Timestamp);
            string notificationPath = AppPaths.GetNotificationRecordPath(
                workspacePath,
                hookInput.SessionId,
                turn.NotificationTurnId,
                notificationKey);
            string sessionNotificationPath = AppPaths.GetSessionNotificationRecordPath(
                workspacePath,
                hookInput.SessionId,
                notificationKey);
            string claimPath = AppPaths.GetSessionStopClaimPath(
                workspacePath,
                hookInput.SessionId,
                notificationKey);
            string reclaimPath = AppPaths.GetSessionStopReclaimClaimPath(
                workspacePath,
                hookInput.SessionId,
                notificationKey);
            string turnClaimPath = AppPaths.GetTurnDeliveryClaimPath(
                workspacePath,
                hookInput.SessionId,
                turn.NotificationTurnId);
            string turnReclaimPath = AppPaths.GetTurnDeliveryReclaimClaimPath(
                workspacePath,
                hookInput.SessionId,
                turn.NotificationTurnId);
            if (await AnyPerTurnNotificationRecordExistsAsync(
                    workspacePath,
                    hookInput.SessionId,
                    notificationKey,
                    cancellationToken)
                || await WorkspaceStateStore.WasNotificationAlreadySentAsync(
                    sessionNotificationPath,
                    cancellationToken))
            {
                AppLog.SkippingDuplicateStop(logger, hookInput.SessionId, turn.NotificationTurnId);
                return 0;
            }

            string claimedAt = workspaceStateStore.GetCurrentUtcTimestamp();
            bool claimedSessionStop = await WorkspaceStateStore.TryClaimStopNotificationAsync(
                claimPath,
                claimedAt,
                cancellationToken);
            if (!claimedSessionStop)
            {
                claimedSessionStop = await WorkspaceStateStore.TryReclaimStaleClaimAsync(
                    claimPath,
                    reclaimPath,
                    claimedAt,
                    TimeSpan.FromMinutes(AppConstants.TurnDeliveryClaimStaleAfterMinutes),
                    async () =>
                        await AnyPerTurnNotificationRecordExistsAsync(
                            workspacePath,
                            hookInput.SessionId,
                            notificationKey,
                            cancellationToken)
                        || await WorkspaceStateStore.WasNotificationAlreadySentAsync(
                            sessionNotificationPath,
                            cancellationToken),
                    cancellationToken);
            }

            if (!claimedSessionStop)
            {
                AppLog.SkippingDuplicateStop(logger, hookInput.SessionId, turn.NotificationTurnId);
                return 0;
            }

            bool claimedTurnDelivery = await WorkspaceStateStore.TryClaimStopNotificationAsync(
                turnClaimPath,
                claimedAt,
                cancellationToken);
            if (!claimedTurnDelivery
                && !await WorkspaceStateStore.HasDurableDeliveryRecordAsync(
                    workspacePath,
                    hookInput.SessionId,
                    turn.NotificationTurnId,
                    cancellationToken))
            {
                claimedTurnDelivery =
                    await WorkspaceStateStore.TryReclaimStaleClaimAsync(
                        turnClaimPath,
                        turnReclaimPath,
                        claimedAt,
                        TimeSpan.FromMinutes(AppConstants.TurnDeliveryClaimStaleAfterMinutes),
                        () => WorkspaceStateStore.HasDurableDeliveryRecordAsync(
                            workspacePath,
                            hookInput.SessionId,
                            turn.NotificationTurnId,
                            cancellationToken),
                        cancellationToken);
            }

            if (!claimedTurnDelivery)
            {
                WorkspaceStateStore.ReleaseStopNotificationClaim(claimPath);
                AppLog.SkippingDuplicateStop(logger, hookInput.SessionId, turn.NotificationTurnId);
                return 0;
            }

            NotificationTurn? currentTurn = await workspaceStateStore.TryReadTurnAsync(
                workspacePath,
                hookInput.SessionId,
                turn.NotificationTurnId,
                cancellationToken);
            if (currentTurn is null
                || !string.Equals(currentTurn.Status, "open", StringComparison.Ordinal)
                || await WorkspaceStateStore.HasDurableDeliveryRecordAsync(
                    workspacePath,
                    hookInput.SessionId,
                    turn.NotificationTurnId,
                    cancellationToken))
            {
                WorkspaceStateStore.ReleaseStopNotificationClaim(claimPath);
                WorkspaceStateStore.ReleaseStopNotificationClaim(turnClaimPath);
                AppLog.SkippingDuplicateStop(logger, hookInput.SessionId, turn.NotificationTurnId);
                return 0;
            }

            turn = currentTurn;
            bool notificationSent = false;
            try
            {
                SummaryValidationResult summaryValidation = await ValidateSummaryWithRetryAsync(
                    workspacePath,
                    hookInput.SessionId,
                    turn,
                    cancellationToken);
                NotificationSummary? summary = summaryValidation.IsValid
                    ? summaryValidation.Record
                    : null;
                string sentAt = workspaceStateStore.GetCurrentUtcTimestamp();

                await WorkspaceStateStore.RecordStopObservationAsync(
                    workspacePath,
                    turn,
                    new StopObservation
                    {
                        SessionId = hookInput.SessionId,
                        NotificationTurnId = turn.NotificationTurnId,
                        StopId = notificationKey,
                        ObservedAt = sentAt,
                        StopTimestamp = hookInput.Timestamp,
                        MatchReason = resolution.Reason,
                        SummaryValid = summaryValidation.IsValid,
                        SummaryFailureReason = summaryValidation.FailureReason,
                    },
                    cancellationToken);

                try
                {
                    int sentMessageCount = await SendNotificationAsync(
                        hookInput,
                        workspacePath,
                        turn.NotificationTurnId,
                        sentAt,
                        summary,
                        cancellationToken);
                    notificationSent = sentMessageCount > 0;
                }
                catch (TelegramSendMessagesException ex)
                {
                    notificationSent = ex.SuccessfulMessageCount > 0;
                    if (notificationSent)
                    {
                        await RecordTurnNotificationDeliveryAsync(
                            hookInput,
                            workspacePath,
                            turn,
                            notificationPath,
                            sessionNotificationPath,
                            notificationKey,
                            sentAt,
                            summary,
                            degraded: true,
                            reason: $"partial Telegram delivery: {ex.Message}",
                            deliveryStatus: "partial",
                            successfulMessageCount: ex.SuccessfulMessageCount,
                            cancellationToken);
                    }

                    throw;
                }

                await RecordTurnNotificationDeliveryAsync(
                    hookInput,
                    workspacePath,
                    turn,
                    notificationPath,
                    sessionNotificationPath,
                    notificationKey,
                    sentAt,
                    summary,
                    !summaryValidation.IsValid,
                    summaryValidation.FailureReason,
                    deliveryStatus: "sent",
                    successfulMessageCount: null,
                    cancellationToken);
            }
            catch
            {
                if (!notificationSent)
                {
                    WorkspaceStateStore.ReleaseStopNotificationClaim(claimPath);
                    WorkspaceStateStore.ReleaseStopNotificationClaim(turnClaimPath);
                }

                throw;
            }

            AppLog.RecordedStopNotification(logger, hookInput.SessionId, turn.NotificationTurnId);
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

    private async Task SendSessionLevelFallbackAsync(
        StopHookInput hookInput,
        string workspacePath,
        string reason,
        CancellationToken cancellationToken)
    {
        string notificationKey = CreateStopNotificationKey(hookInput.Timestamp);
        if (await AnyPerTurnNotificationRecordExistsAsync(
                workspacePath,
                hookInput.SessionId,
                notificationKey,
                cancellationToken))
        {
            AppLog.SkippingDuplicateStop(logger, hookInput.SessionId, notificationKey);
            return;
        }

        string notificationPath = AppPaths.GetSessionNotificationRecordPath(
            workspacePath,
            hookInput.SessionId,
            notificationKey);
        string claimPath = AppPaths.GetSessionStopClaimPath(
            workspacePath,
            hookInput.SessionId,
            notificationKey);
        string reclaimPath = AppPaths.GetSessionStopReclaimClaimPath(
            workspacePath,
            hookInput.SessionId,
            notificationKey);
        if (await WorkspaceStateStore.WasNotificationAlreadySentAsync(
                notificationPath,
                cancellationToken))
        {
            AppLog.SkippingDuplicateStop(logger, hookInput.SessionId, notificationKey);
            return;
        }

        string claimedAt = workspaceStateStore.GetCurrentUtcTimestamp();
        bool claimedSessionStop = await WorkspaceStateStore.TryClaimStopNotificationAsync(
            claimPath,
            claimedAt,
            cancellationToken);
        if (!claimedSessionStop)
        {
            claimedSessionStop = await WorkspaceStateStore.TryReclaimStaleClaimAsync(
                claimPath,
                reclaimPath,
                claimedAt,
                TimeSpan.FromMinutes(AppConstants.TurnDeliveryClaimStaleAfterMinutes),
                async () =>
                    await WorkspaceStateStore.WasNotificationAlreadySentAsync(
                        notificationPath,
                        cancellationToken)
                    || await AnyPerTurnNotificationRecordExistsAsync(
                        workspacePath,
                        hookInput.SessionId,
                        notificationKey,
                        cancellationToken),
                cancellationToken);
        }

        if (!claimedSessionStop)
        {
            AppLog.SkippingDuplicateStop(logger, hookInput.SessionId, notificationKey);
            return;
        }

        string fallbackTurnId = CreateStopFallbackTurnId(hookInput.Timestamp);
        bool notificationSent = false;
        try
        {
            string sentAt = workspaceStateStore.GetCurrentUtcTimestamp();
            try
            {
                int sentMessageCount = await SendNotificationAsync(
                    hookInput,
                    workspacePath,
                    fallbackTurnId,
                    sentAt,
                    summary: null,
                    cancellationToken);
                notificationSent = sentMessageCount > 0;
            }
            catch (TelegramSendMessagesException ex)
            {
                notificationSent = ex.SuccessfulMessageCount > 0;
                if (notificationSent)
                {
                    await WorkspaceStateStore.RecordNotificationAsync(
                        notificationPath,
                        BuildNotificationRecord(
                            hookInput,
                            workspacePath,
                            notificationTurnId: null,
                            notificationKey,
                            sentAt,
                            summary: null,
                            degraded: true,
                            reason: $"partial Telegram delivery: {ex.Message}",
                            deliveryStatus: "partial",
                            successfulMessageCount: ex.SuccessfulMessageCount),
                        cancellationToken);
                }

                throw;
            }

            await WorkspaceStateStore.RecordNotificationAsync(
                notificationPath,
                BuildNotificationRecord(
                    hookInput,
                    workspacePath,
                    notificationTurnId: null,
                    notificationKey,
                    sentAt,
                    summary: null,
                    degraded: true,
                    reason,
                    deliveryStatus: "sent",
                    successfulMessageCount: null),
                cancellationToken);
        }
        catch
        {
            if (!notificationSent)
            {
                WorkspaceStateStore.ReleaseStopNotificationClaim(claimPath);
            }

            throw;
        }

        AppLog.RecordedStopNotification(logger, hookInput.SessionId, fallbackTurnId);
    }

    private async Task<int> SendNotificationAsync(
        StopHookInput hookInput,
        string workspacePath,
        string turnId,
        string sentAt,
        NotificationSummary? summary,
        CancellationToken cancellationToken)
    {
        GitRepositoryMetadata? repositoryMetadata = await gitRepositoryProbe.TryProbeAsync(
            workspacePath,
            cancellationToken);
        TelegramCredentials credentials = await telegramCredentialProvider.ResolveAsync(
            cancellationToken);
        NotificationContext context = new()
        {
            SessionId = hookInput.SessionId,
            TurnId = turnId,
            StopTimestamp = hookInput.Timestamp,
            SentAt = sentAt,
            WorkspacePath = workspacePath,
            HostName = Environment.MachineName,
            ExecutionEnvironment = AppPaths.GetExecutionEnvironmentDisplay(),
            RepositoryName = repositoryMetadata?.RepositoryName,
            BranchName = repositoryMetadata?.BranchName,
            CommitId = ShortCommit(repositoryMetadata?.CommitId),
            TranscriptPath = hookInput.TranscriptPath,
        };

        IReadOnlyList<string> messages = NotificationComposer.Compose(context, summary);
        AppLog.SendingStopNotification(
            logger,
            messages.Count,
            context.SessionId,
            context.TurnId);
        return await telegramBotClient.SendMessagesAsync(credentials, messages, cancellationToken);
    }

    private static NotificationRecord BuildNotificationRecord(
        StopHookInput input,
        string workspacePath,
        string? notificationTurnId,
        string notificationKey,
        string sentAt,
        NotificationSummary? summary,
        bool degraded,
        string? reason,
        string deliveryStatus,
        int? successfulMessageCount)
        => new()
        {
            SessionId = input.SessionId,
            NotificationTurnId = notificationTurnId,
            NotificationKey = notificationKey,
            WorkspacePath = Path.GetFullPath(workspacePath),
            StopTimestamp = input.Timestamp,
            SentAt = sentAt,
            SummaryUpdatedAt = summary?.UpdatedAt,
            Degraded = degraded,
            DeliveryStatus = deliveryStatus,
            SuccessfulMessageCount = successfulMessageCount,
            Reason = reason,
        };

    private static async Task RecordTurnNotificationDeliveryAsync(
        StopHookInput input,
        string workspacePath,
        NotificationTurn turn,
        string notificationPath,
        string sessionNotificationPath,
        string notificationKey,
        string sentAt,
        NotificationSummary? summary,
        bool degraded,
        string? reason,
        string deliveryStatus,
        int? successfulMessageCount,
        CancellationToken cancellationToken)
    {
        NotificationRecord record = BuildNotificationRecord(
            input,
            workspacePath,
            turn.NotificationTurnId,
            notificationKey,
            sentAt,
            summary,
            degraded,
            reason,
            deliveryStatus,
            successfulMessageCount);
        await WorkspaceStateStore.RecordNotificationAsync(
            notificationPath,
            record,
            cancellationToken);
        await WorkspaceStateStore.RecordNotificationAsync(
            sessionNotificationPath,
            record,
            cancellationToken);
        await WorkspaceStateStore.MarkTurnNotifiedAsync(
            workspacePath,
            turn,
            sentAt,
            cancellationToken);
    }

    private static PromptClassification ClassifyPrompt(UserPromptSubmitHookInput input)
    {
        string prompt = input.Prompt ?? string.Empty;
        if (string.IsNullOrWhiteSpace(prompt))
        {
            return new PromptClassification("observation-only", "prompt is empty or missing");
        }

        string trimmed = prompt.TrimStart();
        if (trimmed.StartsWith("<system_reminder>", StringComparison.OrdinalIgnoreCase)
            || trimmed.StartsWith("Contents of AGENTS.md", StringComparison.OrdinalIgnoreCase)
            || IsExplicitSubagentHandoff(trimmed)
            || trimmed.Contains("Coder subagent", StringComparison.OrdinalIgnoreCase)
            || trimmed.Contains(
                "OA is not allowed to code directly",
                StringComparison.OrdinalIgnoreCase))
        {
            return new PromptClassification(
                "observation-only",
                "prompt text matches explicit generated/subagent/system handoff markers");
        }

        if (!string.IsNullOrWhiteSpace(input.HookEventName)
            && !string.Equals(input.HookEventName, "UserPromptSubmit", StringComparison.Ordinal))
        {
            return new PromptClassification(
                "observation-only",
                "hook_event_name is not UserPromptSubmit");
        }

        return new PromptClassification(
            "main-user-prompt",
            "UserPromptSubmit with non-empty prompt and no generated/subagent markers");
    }

    private static bool IsExplicitSubagentHandoff(string trimmedPrompt)
    {
        if (!trimmedPrompt.StartsWith("You are ", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        int firstLineEnd = trimmedPrompt.IndexOfAny(['\r', '\n']);
        string firstLine = firstLineEnd < 0
            ? trimmedPrompt
            : trimmedPrompt[..firstLineEnd];
        return firstLine.Contains("subagent", StringComparison.OrdinalIgnoreCase);
    }

    private static StopResolution ResolveStopTurn(
        IReadOnlyList<NotificationTurn> openTurns,
        IReadOnlyList<PromptObservation> promptObservations,
        IReadOnlyList<NotificationRecord> sessionNotificationRecords,
        string stopTimestamp)
    {
        if (!TryParseUtcTimestamp(stopTimestamp, out DateTimeOffset parsedStopTimestamp))
        {
            return new StopResolution(
                null,
                $"invalid Stop timestamp '{stopTimestamp}'");
        }

        if (openTurns.Count == 0)
        {
            return new StopResolution(
                null,
                $"no open notification turn for Stop {stopTimestamp}");
        }

        NotificationTurn[] eligibleTurns = openTurns
            .Where(turn => TryParseUtcTimestamp(turn.CreatedAt, out DateTimeOffset createdAt)
                && createdAt <= parsedStopTimestamp)
            .ToArray();

        if (eligibleTurns.Length == 1)
        {
            if (HasUnresolvedInterveningSubagentObservation(
                    eligibleTurns[0],
                    promptObservations,
                    sessionNotificationRecords,
                    parsedStopTimestamp))
            {
                return new StopResolution(
                    null,
                    "explicit observation-only subagent handoff intervened before "
                        + $"Stop {stopTimestamp}");
            }

            return new StopResolution(
                eligibleTurns[0],
                "unique open notification turn created no later than Stop timestamp");
        }

        if (eligibleTurns.Length == 0)
        {
            return new StopResolution(
                null,
                $"no eligible open notification turn at or before Stop {stopTimestamp}");
        }

        return new StopResolution(
            null,
            $"ambiguous Stop {stopTimestamp}: "
                + $"{eligibleTurns.Length} eligible open notification turns");
    }

    private static bool HasUnresolvedInterveningSubagentObservation(
        NotificationTurn turn,
        IReadOnlyList<PromptObservation> promptObservations,
        IReadOnlyList<NotificationRecord> sessionNotificationRecords,
        DateTimeOffset stopTimestamp)
    {
        if (!TryParseUtcTimestamp(turn.CreatedAt, out DateTimeOffset turnCreatedAt))
        {
            return false;
        }

        return promptObservations.Any(observation =>
            IsExplicitObservationOnlySubagentObservation(observation)
            && TryParseUtcTimestamp(observation.ObservedAt, out DateTimeOffset observedAt)
            && observedAt > turnCreatedAt
            && observedAt <= stopTimestamp
            && !WasObservationAlreadyHandledByEarlierSessionStop(
                observedAt,
                stopTimestamp,
                sessionNotificationRecords));
    }

    private static bool IsExplicitObservationOnlySubagentObservation(PromptObservation observation)
        => string.Equals(observation.Classification, "observation-only", StringComparison.Ordinal)
            && !string.IsNullOrWhiteSpace(observation.Prompt)
            && IsExplicitSubagentHandoff(observation.Prompt.TrimStart());

    private static bool WasObservationAlreadyHandledByEarlierSessionStop(
        DateTimeOffset observedAt,
        DateTimeOffset currentStopTimestamp,
        IReadOnlyList<NotificationRecord> sessionNotificationRecords)
        => sessionNotificationRecords.Any(record =>
            TryParseUtcTimestamp(record.StopTimestamp, out DateTimeOffset previousStopTimestamp)
            && previousStopTimestamp >= observedAt
            && previousStopTimestamp < currentStopTimestamp);

    private static string BuildProtocolOverviewContext(NotificationSession session)
    {
        return string.Join(
            " ",
            [
                "Notification summary handoff protocol is enabled for this workspace.",
                $"Your session_id is {session.SessionId}.",
                "A summary is advisory and may only be written when a hook-emitted",
                "Notification Assignment gives an exact per-turn summary.json path.",
                "Write only that exact assigned summary path; do not create or update",
                "legacy singleton notification files.",
                "Recovery guidance is not a new task and must not start a new summary handoff.",
            ]);
    }

    private static string BuildNotificationAssignmentContext(
        string workspacePath,
        NotificationTurn turn)
    {
        string summaryPath = AppPaths.GetSummaryStatePath(
            workspacePath,
            turn.SessionId,
            turn.NotificationTurnId);
        string relativeSummaryPath = AppPaths.GetRelativeSummaryStatePath(
            turn.SessionId,
            turn.NotificationTurnId);

        return string.Join(
            " ",
            [
                "Notification Assignment:",
                $"write the task summary only to {summaryPath}",
                $"(workspace-relative {relativeSummaryPath}).",
                $"Use session_id='{turn.SessionId}',",
                $"notification_turn_id='{turn.NotificationTurnId}',",
                $"notification_nonce='{turn.NotificationNonce}'.",
                "updated_at must be a UTC timestamp in yyyy-MM-ddTHH:mm:ss.fffZ format.",
                "summary must be a non-empty concise human-readable sentence.",
                "details, changed_files, and next_steps must be JSON arrays.",
                "Do not write legacy singleton notification files.",
            ]);
    }

    private static string CreateStopFallbackTurnId(string timestamp)
    {
        string normalized = NormalizeKey(timestamp);
        return string.IsNullOrWhiteSpace(normalized)
            ? $"stop-{Guid.NewGuid():n}"
            : $"stop-{normalized}";
    }

    private static string CreateStopNotificationKey(string timestamp)
    {
        byte[] hash = SHA256.HashData(Encoding.UTF8.GetBytes(timestamp));
        return $"stop-{Convert.ToHexString(hash)[..32].ToLowerInvariant()}";
    }

    private static string NormalizeKey(string value)
    {
        string normalized = new(value.Where(char.IsLetterOrDigit).ToArray());
        return normalized.ToLowerInvariant();
    }

    private static async Task<bool> AnyPerTurnNotificationRecordExistsAsync(
        string workspacePath,
        string sessionId,
        string notificationKey,
        CancellationToken cancellationToken)
    {
        string turnsDirectory = AppPaths.GetTurnsDirectoryPath(workspacePath, sessionId);
        if (!Directory.Exists(turnsDirectory))
        {
            return false;
        }

        foreach (string turnDirectory in Directory.EnumerateDirectories(turnsDirectory))
        {
            string recordPath = Path.Combine(
                turnDirectory,
                AppConstants.NotificationsRecordsDirectoryName,
                $"{notificationKey}.json");
            if (!File.Exists(recordPath))
            {
                continue;
            }

            try
            {
                await using FileStream stream = File.OpenRead(recordPath);
                NotificationRecord? record = await JsonSerializer.DeserializeAsync(
                    stream,
                    AppJsonSerializerContext.Default.NotificationRecord,
                    cancellationToken);
                if (record is not null
                    && string.Equals(record.SessionId, sessionId, StringComparison.Ordinal)
                    && string.Equals(
                        record.NotificationKey,
                        notificationKey,
                        StringComparison.Ordinal))
                {
                    return true;
                }
            }
            catch (Exception ex) when (
                ex is IOException or JsonException or UnauthorizedAccessException
                    or NotSupportedException)
            {
                continue;
            }
        }

        return false;
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

    private static async Task<SummaryValidationResult> ValidateSummaryWithRetryAsync(
        string workspacePath,
        string sessionId,
        NotificationTurn turn,
        CancellationToken cancellationToken)
    {
        SummaryValidationResult result = SummaryValidationResult.Invalid("Summary was not read.");
        for (int attempt = 0; attempt < AppConstants.SummaryReadRetryCount; attempt++)
        {
            result = await ValidateSummaryOnceAsync(
                workspacePath,
                sessionId,
                turn,
                cancellationToken);
            if (result.IsValid || attempt == AppConstants.SummaryReadRetryCount - 1)
            {
                return result;
            }

            await Task.Delay(AppConstants.SummaryReadRetryDelayMilliseconds, cancellationToken);
        }

        return result;
    }

    private static async Task<SummaryValidationResult> ValidateSummaryOnceAsync(
        string workspacePath,
        string sessionId,
        NotificationTurn turn,
        CancellationToken cancellationToken)
    {
        string summaryPath = AppPaths.GetSummaryStatePath(
            workspacePath,
            sessionId,
            turn.NotificationTurnId);
        string summaryDisplayPath = AppPaths.GetRelativeSummaryStatePath(
            sessionId,
            turn.NotificationTurnId);
        if (!File.Exists(summaryPath))
        {
            return SummaryValidationResult.Invalid(
                $"Summary file is missing at '{summaryDisplayPath}'.");
        }

        NotificationSummary? summary;
        try
        {
            await using FileStream stream = File.Open(
                summaryPath,
                FileMode.Open,
                FileAccess.Read,
                FileShare.ReadWrite);
            summary = await JsonSerializer.DeserializeAsync(
                stream,
                AppJsonSerializerContext.Default.NotificationSummary,
                cancellationToken);
        }
        catch (Exception ex) when (
            ex is IOException or JsonException or UnauthorizedAccessException
                or NotSupportedException)
        {
            return SummaryValidationResult.Invalid(
                $"Summary file '{summaryDisplayPath}' could not be parsed as JSON: {ex.Message}");
        }

        if (summary is null)
        {
            return SummaryValidationResult.Invalid(
                $"Summary file '{summaryDisplayPath}' is empty or does not contain a JSON object.");
        }

        List<string> failures = [];
        if (!string.Equals(summary.SessionId, turn.SessionId, StringComparison.Ordinal))
        {
            failures.Add($"session_id must equal '{turn.SessionId}'");
        }

        if (!string.Equals(
                summary.NotificationTurnId,
                turn.NotificationTurnId,
                StringComparison.Ordinal))
        {
            failures.Add($"notification_turn_id must equal '{turn.NotificationTurnId}'");
        }

        if (!string.Equals(
                summary.NotificationNonce,
                turn.NotificationNonce,
                StringComparison.Ordinal))
        {
            failures.Add("notification_nonce must equal the assigned nonce");
        }

        if (!IsValidUtcTimestamp(summary.UpdatedAt))
        {
            failures.Add(
                "updated_at must be a UTC timestamp in yyyy-MM-ddTHH:mm:ss.fffZ format");
        }

        if (string.IsNullOrWhiteSpace(summary.Summary))
        {
            failures.Add("summary must be a non-empty human-readable sentence");
        }

        if (failures.Count > 0)
        {
            return SummaryValidationResult.Invalid(
                $"Summary file '{summaryDisplayPath}' is invalid: {string.Join("; ", failures)}.");
        }

        return SummaryValidationResult.Valid(summary);
    }

    private static bool IsValidUtcTimestamp(string? value)
        => TryParseUtcTimestamp(value, out DateTimeOffset parsed)
            && string.Equals(
                parsed.ToString(UtcTimestampFormat, CultureInfo.InvariantCulture),
                value,
                StringComparison.Ordinal);

    private static bool TryParseUtcTimestamp(string? value, out DateTimeOffset parsed)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            parsed = default;
            return false;
        }

        return DateTimeOffset.TryParseExact(
                value,
                UtcTimestampFormat,
                CultureInfo.InvariantCulture,
                DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal,
                out parsed);
    }

    private async Task WriteAdditionalContextResponseAsync(
        Stream standardOutput,
        string hookEventName,
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
                    HookEventName = hookEventName,
                    AdditionalContext = additionalContext,
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
        NotificationSummary? Record,
        string? FailureReason)
    {
        public static SummaryValidationResult Valid(NotificationSummary record)
            => new(true, record, null);

        public static SummaryValidationResult Invalid(string failureReason)
            => new(false, null, failureReason);
    }

    private sealed record StopResolution(NotificationTurn? Turn, string Reason);

    private static string? TryDescribePayloadShape(ReadOnlyMemory<byte> payload)
    {
        if (payload.IsEmpty)
        {
            return null;
        }

        try
        {
            using JsonDocument document = JsonDocument.Parse(payload);
            if (document.RootElement.ValueKind != JsonValueKind.Object)
            {
                return $"payload JSON root kind is {document.RootElement.ValueKind}.";
            }

            string[] propertyNames = document.RootElement
                .EnumerateObject()
                .Select(static property => property.Name)
                .Order(StringComparer.Ordinal)
                .ToArray();
            return propertyNames.Length == 0
                ? "payload object has no top-level fields."
                : $"present top-level field(s): {string.Join(", ", propertyNames)}.";
        }
        catch (JsonException)
        {
            return null;
        }
    }
}
