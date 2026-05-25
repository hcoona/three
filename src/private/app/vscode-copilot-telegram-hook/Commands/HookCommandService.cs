using System.Diagnostics.CodeAnalysis;
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
    ILogger<HookCommandService> logger
)
{
    private const string UtcTimestampFormat = "yyyy-MM-ddTHH:mm:ss.fff'Z'";
    private static readonly HookOutputAdapter VsCodeAdapter = new HookSpecificOutputAdapter();
    private static readonly HookOutputAdapter CopilotCliAdapter = new CopilotCliOutputAdapter();

    public async Task<int> HandleSessionStartAsync(
        Stream standardInput,
        Stream standardOutput,
        CancellationToken cancellationToken
    )
    {
        IDisposable? logScope = null;
        try
        {
            byte[] payload = await ReadPayloadAsync(standardInput, cancellationToken);
            SessionStartHookInput? hookInput = DeserializePayload(
                payload,
                AppJsonSerializerContext.Default.SessionStartHookInput
            );

            string? workspacePath = GetWorkspacePathOrNull(hookInput?.Cwd);
            logScope = TryOpenHookLogScope(workspacePath, hookInput?.SessionId);

            if (
                hookInput is null
                || workspacePath is null
                || string.IsNullOrWhiteSpace(hookInput.SessionId)
            )
            {
                string reason = BuildInvalidInputReason(
                    hookInput,
                    payload,
                    ("cwd", workspacePath is null),
                    ("session_id", string.IsNullOrWhiteSpace(hookInput?.SessionId))
                );
                AppLog.IgnoringInvalidHookInput(logger, "SessionStart", reason);
                await Console.Error.WriteLineAsync($"SessionStart hook warning: {reason}");
                return 0;
            }

            AppLog.HandlingSessionStart(logger, hookInput.SessionId, workspacePath);

            NotificationSession session = await workspaceStateStore.InitializeSessionAsync(
                hookInput,
                cancellationToken
            );

            await WriteAdditionalContextResponseAsync(
                standardOutput,
                "SessionStart",
                BuildProtocolOverviewContext(session),
                cancellationToken
            );
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
        CancellationToken cancellationToken
    )
    {
        IDisposable? logScope = null;
        try
        {
            byte[] payload = await ReadPayloadAsync(standardInput, cancellationToken);
            UserPromptSubmitHookInput? hookInput = DeserializePayload(
                payload,
                AppJsonSerializerContext.Default.UserPromptSubmitHookInput
            );

            string? workspacePath = GetWorkspacePathOrNull(hookInput?.Cwd);
            logScope = TryOpenHookLogScope(workspacePath, hookInput?.SessionId);

            if (
                hookInput is null
                || workspacePath is null
                || string.IsNullOrWhiteSpace(hookInput.SessionId)
            )
            {
                string reason = BuildInvalidInputReason(
                    hookInput,
                    payload,
                    ("cwd", workspacePath is null),
                    ("session_id", string.IsNullOrWhiteSpace(hookInput?.SessionId))
                );
                AppLog.IgnoringInvalidHookInput(logger, "UserPromptSubmit", reason);
                await Console.Error.WriteLineAsync($"UserPromptSubmit hook warning: {reason}");
                return 0;
            }

            AppLog.HandlingUserPromptSubmit(
                logger,
                hookInput.SessionId,
                workspacePath,
                hookInput.Prompt?.Length ?? 0
            );

            PromptClassification classification = ClassifyPrompt(hookInput);
            PromptObservation observation = await workspaceStateStore.RecordPromptObservationAsync(
                hookInput,
                classification,
                cancellationToken
            );
            if (!classification.IsHighConfidenceMainPrompt)
            {
                return 0;
            }

            NotificationTurn turn = await workspaceStateStore.CreateNotificationTurnAsync(
                hookInput,
                observation,
                cancellationToken
            );
            await WriteUserPromptSubmitResponseAsync(
                standardOutput,
                hookInput.Prompt ?? string.Empty,
                BuildNotificationAssignmentContext(workspacePath, turn),
                cancellationToken
            );
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
        CancellationToken cancellationToken
    )
    {
        IDisposable? logScope = null;
        try
        {
            byte[] payload = await ReadPayloadAsync(standardInput, cancellationToken);
            StopHookInput? hookInput = DeserializePayload(
                payload,
                AppJsonSerializerContext.Default.StopHookInput
            );

            string? workspacePath = GetWorkspacePathOrNull(hookInput?.Cwd);
            logScope = TryOpenHookLogScope(workspacePath, hookInput?.SessionId);

            if (
                hookInput is null
                || workspacePath is null
                || string.IsNullOrWhiteSpace(hookInput.SessionId)
                || string.IsNullOrWhiteSpace(hookInput.Timestamp)
            )
            {
                string reason = BuildInvalidInputReason(
                    hookInput,
                    payload,
                    ("cwd", workspacePath is null),
                    ("session_id", string.IsNullOrWhiteSpace(hookInput?.SessionId)),
                    ("timestamp", string.IsNullOrWhiteSpace(hookInput?.Timestamp))
                );
                AppLog.IgnoringInvalidHookInput(logger, "Stop", reason);
                await Console.Error.WriteLineAsync($"Stop hook warning: {reason}");
                return 0;
            }

            AppLog.HandlingStopHook(logger, hookInput.SessionId, workspacePath);

            string notificationKey = CreateStopNotificationKey(hookInput.Timestamp);
            await workspaceStateStore.AbandonSupersededOpenTurnsAsync(
                workspacePath,
                hookInput.SessionId,
                workspaceStateStore.GetCurrentUtcTimestamp(),
                cancellationToken
            );
            IReadOnlyList<NotificationTurn> openTurns =
                await workspaceStateStore.ListOpenTurnsAsync(
                    workspacePath,
                    hookInput.SessionId,
                    cancellationToken
                );
            IReadOnlyList<NotificationTurn> abandonedTurns =
                await workspaceStateStore.ListAbandonedTurnsAsync(
                    workspacePath,
                    hookInput.SessionId,
                    cancellationToken
                );
            IReadOnlyList<NotificationTurn> freshClaimedOpenTurns =
                await workspaceStateStore.ListFreshDeliveryClaimedOpenTurnsAsync(
                    workspacePath,
                    hookInput.SessionId,
                    cancellationToken
                );
            IReadOnlyList<NotificationTurn> exactNotifiedRetryTurns =
                await ListExactNotifiedStopRetryTurnsAsync(
                    workspacePath,
                    hookInput.SessionId,
                    notificationKey,
                    hookInput.Timestamp,
                    cancellationToken
                );
            if (exactNotifiedRetryTurns.Count > 0)
            {
                openTurns = openTurns
                    .Concat(exactNotifiedRetryTurns)
                    .OrderBy(static turn => turn.CreatedAt, StringComparer.Ordinal)
                    .ToArray();
            }
            IReadOnlyList<PromptObservation> promptObservations =
                await workspaceStateStore.ListPromptObservationsAsync(
                    workspacePath,
                    hookInput.SessionId,
                    cancellationToken
                );
            IReadOnlyList<NotificationRecord> sessionNotificationRecords =
                await workspaceStateStore.ListSessionNotificationRecordsAsync(
                    workspacePath,
                    hookInput.SessionId,
                    cancellationToken
                );
            IReadOnlyList<NotificationRecord> perTurnNotificationRecords =
                await ListPerTurnNotificationRecordsAsync(
                    workspacePath,
                    hookInput.SessionId,
                    cancellationToken
                );
            NotificationRecord[] durableNotificationRecords = sessionNotificationRecords
                .Concat(perTurnNotificationRecords)
                .ToArray();
            CurrentNotificationState? current = await workspaceStateStore.TryReadCurrentAsync(
                workspacePath,
                hookInput.SessionId,
                cancellationToken
            );
            if (TryParseUtcTimestamp(hookInput.Timestamp, out DateTimeOffset parsedStopTimestamp))
            {
                RecoverableAbandonedTurnsResult recoverableAbandonedTurns = !HasEligibleTurn(
                    openTurns,
                    parsedStopTimestamp
                )
                    ? await ListRecoverableAbandonedTurnsForStopAsync(
                        workspacePath,
                        hookInput.SessionId,
                        abandonedTurns,
                        durableNotificationRecords,
                        notificationKey,
                        hookInput.Timestamp,
                        cancellationToken
                    )
                    : await ListRecoverableExactCompletedAbandonedTurnsForStopAsync(
                        workspacePath,
                        hookInput.SessionId,
                        abandonedTurns,
                        durableNotificationRecords,
                        notificationKey,
                        hookInput.Timestamp,
                        cancellationToken
                    );
                if (recoverableAbandonedTurns.SuppressStop)
                {
                    return 0;
                }

                if (recoverableAbandonedTurns.Turns.Count > 0)
                {
                    openTurns = openTurns
                        .Concat(recoverableAbandonedTurns.Turns)
                        .OrderBy(static turn => turn.CreatedAt, StringComparer.Ordinal)
                        .ToArray();
                }
            }

            IReadOnlyList<NotificationTurn> freshClaimedStopAttributionTurns =
                await FilterFreshClaimedTurnsWithStopAttributionAsync(
                    workspacePath,
                    hookInput.SessionId,
                    openTurns,
                    freshClaimedOpenTurns,
                    hookInput.Timestamp,
                    cancellationToken
                );
            IReadOnlyList<NotificationTurn> prePreferenceOpenTurns = openTurns;
            openTurns = await PreferSingleValidSummaryTurnAsync(
                workspacePath,
                hookInput.SessionId,
                current,
                freshClaimedOpenTurns,
                openTurns,
                durableNotificationRecords,
                hookInput.Timestamp,
                cancellationToken
            );
            if (
                await AreAllPreferredTurnsNonUniqueExactStopAttributionsAsync(
                    workspacePath,
                    hookInput.SessionId,
                    openTurns,
                    hookInput.Timestamp,
                    cancellationToken
                )
            )
            {
                return 0;
            }

            if (
                await IsCurrentTurnFreshClaimedAsync(
                    workspacePath,
                    hookInput.SessionId,
                    current,
                    freshClaimedOpenTurns,
                    openTurns,
                    hookInput.Timestamp,
                    cancellationToken
                )
            )
            {
                return 0;
            }

            StopResolution resolution = ResolveStopTurn(
                openTurns,
                freshClaimedStopAttributionTurns,
                promptObservations,
                durableNotificationRecords,
                hookInput.Timestamp
            );

            if (resolution.Turn is null)
            {
                if (
                    freshClaimedStopAttributionTurns.Count > 0
                    || HasBlockingFreshClaimedTurn(
                        openTurns,
                        freshClaimedOpenTurns,
                        hookInput.Timestamp
                    )
                )
                {
                    return 0;
                }

                if (
                    await HasPendingStopObservationOnAbandonedTurnAsync(
                        workspacePath,
                        hookInput.SessionId,
                        abandonedTurns,
                        durableNotificationRecords,
                        notificationKey,
                        hookInput.Timestamp,
                        cancellationToken
                    )
                )
                {
                    return 0;
                }

                if (
                    await HasEqualCreatedAtExactSummaryPendingHandoffAmbiguityAsync(
                        workspacePath,
                        hookInput.SessionId,
                        openTurns,
                        hookInput.Timestamp,
                        cancellationToken
                    )
                )
                {
                    return 0;
                }

                if (resolution.SuppressFallback)
                {
                    return 0;
                }

                if (
                    HasPriorNonExactDurableDelivery(durableNotificationRecords, hookInput.Timestamp)
                )
                {
                    return 0;
                }

                if (
                    prePreferenceOpenTurns.Count == 0
                    && !await HasAnyPendingHandoffAbandonedTurnForStopAsync(
                        workspacePath,
                        hookInput.SessionId,
                        abandonedTurns,
                        hookInput.Timestamp,
                        cancellationToken
                    )
                    && await HasPriorClosedPerTurnDurableDeliveryAsync(
                        workspacePath,
                        hookInput.SessionId,
                        perTurnNotificationRecords,
                        hookInput.Timestamp,
                        cancellationToken
                    )
                )
                {
                    return 0;
                }

                if (
                    await HasDurableDeliveryForCurrentTurnSummaryAsync(
                        workspacePath,
                        hookInput.SessionId,
                        prePreferenceOpenTurns,
                        durableNotificationRecords,
                        hookInput.Timestamp,
                        cancellationToken
                    )
                )
                {
                    return 0;
                }

                await SendSessionLevelFallbackAsync(
                    hookInput,
                    workspacePath,
                    resolution.Reason,
                    cancellationToken
                );
                return 0;
            }

            NotificationTurn turn = resolution.Turn;
            string notificationPath = AppPaths.GetNotificationRecordPath(
                workspacePath,
                hookInput.SessionId,
                turn.NotificationTurnId,
                notificationKey
            );
            string sessionNotificationPath = AppPaths.GetSessionNotificationRecordPath(
                workspacePath,
                hookInput.SessionId,
                notificationKey
            );
            string claimPath = AppPaths.GetSessionStopClaimPath(
                workspacePath,
                hookInput.SessionId,
                notificationKey
            );
            string reclaimPath = AppPaths.GetSessionStopReclaimClaimPath(
                workspacePath,
                hookInput.SessionId,
                notificationKey
            );
            string turnClaimPath = AppPaths.GetTurnDeliveryClaimPath(
                workspacePath,
                hookInput.SessionId,
                turn.NotificationTurnId
            );
            string turnReclaimPath = AppPaths.GetTurnDeliveryReclaimClaimPath(
                workspacePath,
                hookInput.SessionId,
                turn.NotificationTurnId
            );
            SummaryValidationResult resolvedSummaryValidation = await ValidateSummaryOnceAsync(
                workspacePath,
                hookInput.SessionId,
                turn,
                cancellationToken
            );
            if (
                await HasPendingStopObservationOnAbandonedTurnAsync(
                    workspacePath,
                    hookInput.SessionId,
                    abandonedTurns,
                    durableNotificationRecords,
                    notificationKey,
                    hookInput.Timestamp,
                    cancellationToken
                )
                && !ResolvedTurnHasPositiveStopAttribution(
                    hookInput.Timestamp,
                    resolvedSummaryValidation
                )
                && !HasCurrentStopAttribution(resolvedSummaryValidation, turn, hookInput.Timestamp)
                && (
                    resolvedSummaryValidation.IsPendingHandoff
                    || !resolvedSummaryValidation.IsValid
                    || !string.Equals(
                        resolvedSummaryValidation.Record?.UpdatedAt,
                        hookInput.Timestamp,
                        StringComparison.Ordinal
                    )
                    || string.Equals(turn.CreatedAt, hookInput.Timestamp, StringComparison.Ordinal)
                )
            )
            {
                return 0;
            }

            if (
                await AnyPerTurnNotificationRecordExistsAsync(
                    workspacePath,
                    hookInput.SessionId,
                    notificationKey,
                    cancellationToken
                )
                || await WorkspaceStateStore.WasNotificationAlreadySentAsync(
                    sessionNotificationPath,
                    cancellationToken
                )
            )
            {
                AppLog.SkippingDuplicateStop(logger, hookInput.SessionId, turn.NotificationTurnId);
                return 0;
            }

            string claimedAt = workspaceStateStore.GetCurrentUtcTimestamp();
            bool claimedSessionStop = await WorkspaceStateStore.TryClaimStopNotificationAsync(
                claimPath,
                claimedAt,
                cancellationToken
            );
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
                            cancellationToken
                        )
                        || await WorkspaceStateStore.WasNotificationAlreadySentAsync(
                            sessionNotificationPath,
                            cancellationToken
                        ),
                    cancellationToken
                );
            }

            if (!claimedSessionStop)
            {
                AppLog.SkippingDuplicateStop(logger, hookInput.SessionId, turn.NotificationTurnId);
                return 0;
            }

            bool claimedTurnDelivery = await WorkspaceStateStore.TryClaimStopNotificationAsync(
                turnClaimPath,
                claimedAt,
                cancellationToken
            );
            bool ownsTurnDeliveryClaim = claimedTurnDelivery;
            if (
                !claimedTurnDelivery
                && !await WorkspaceStateStore.HasDurableDeliveryRecordAsync(
                    workspacePath,
                    hookInput.SessionId,
                    turn.NotificationTurnId,
                    cancellationToken
                )
            )
            {
                claimedTurnDelivery = await WorkspaceStateStore.TryReclaimStaleClaimAsync(
                    turnClaimPath,
                    turnReclaimPath,
                    claimedAt,
                    TimeSpan.FromMinutes(AppConstants.TurnDeliveryClaimStaleAfterMinutes),
                    () =>
                        WorkspaceStateStore.HasDurableDeliveryRecordAsync(
                            workspacePath,
                            hookInput.SessionId,
                            turn.NotificationTurnId,
                            cancellationToken
                        ),
                    cancellationToken
                );
                ownsTurnDeliveryClaim = claimedTurnDelivery;
            }

            NotificationTurn? currentTurn = null;
            if (!claimedTurnDelivery)
            {
                currentTurn = await workspaceStateStore.TryReadTurnAsync(
                    workspacePath,
                    hookInput.SessionId,
                    turn.NotificationTurnId,
                    cancellationToken
                );
                bool retryableNotifiedExactTurn =
                    currentTurn is not null
                    && string.Equals(currentTurn.Status, "notified", StringComparison.Ordinal)
                    && HasStopAttributionForTurn(
                        resolvedSummaryValidation,
                        currentTurn,
                        hookInput.Timestamp
                    )
                    && !await WorkspaceStateStore.WasNotificationAlreadySentAsync(
                        notificationPath,
                        cancellationToken
                    );
                claimedTurnDelivery = retryableNotifiedExactTurn;
            }

            if (!claimedTurnDelivery)
            {
                WorkspaceStateStore.ReleaseStopNotificationClaim(claimPath);
                AppLog.SkippingDuplicateStop(logger, hookInput.SessionId, turn.NotificationTurnId);
                return 0;
            }

            currentTurn ??= await workspaceStateStore.TryReadTurnAsync(
                workspacePath,
                hookInput.SessionId,
                turn.NotificationTurnId,
                cancellationToken
            );
            bool currentTurnHasExactStopAttribution =
                currentTurn is not null
                && HasStopAttributionForTurn(
                    resolvedSummaryValidation,
                    currentTurn,
                    hookInput.Timestamp
                );
            bool isRetryableNotifiedExactTurn =
                currentTurn is not null
                && string.Equals(currentTurn.Status, "notified", StringComparison.Ordinal)
                && currentTurnHasExactStopAttribution
                && !await WorkspaceStateStore.WasNotificationAlreadySentAsync(
                    notificationPath,
                    cancellationToken
                );
            bool isRecoverableAbandonedTurn =
                currentTurn is not null
                && string.Equals(currentTurn.Status, "abandoned", StringComparison.Ordinal)
                && (
                    HasPendingStopAttributionForTurn(
                        resolvedSummaryValidation,
                        currentTurn,
                        hookInput.Timestamp
                    )
                    || HasStopAttributionForTurn(
                        resolvedSummaryValidation,
                        currentTurn,
                        hookInput.Timestamp
                    )
                );
            bool hasDurableDeliveryRecord = await WorkspaceStateStore.HasDurableDeliveryRecordAsync(
                workspacePath,
                hookInput.SessionId,
                turn.NotificationTurnId,
                cancellationToken
            );
            if (
                currentTurn is null
                || (
                    !string.Equals(currentTurn.Status, "open", StringComparison.Ordinal)
                    && !isRetryableNotifiedExactTurn
                    && !isRecoverableAbandonedTurn
                )
                || hasDurableDeliveryRecord
            )
            {
                WorkspaceStateStore.ReleaseStopNotificationClaim(claimPath);
                if (ownsTurnDeliveryClaim)
                {
                    WorkspaceStateStore.ReleaseStopNotificationClaim(turnClaimPath);
                }

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
                    cancellationToken
                );
                if (
                    currentTurnHasExactStopAttribution
                    && !HasStopAttributionForTurn(summaryValidation, turn, hookInput.Timestamp)
                )
                {
                    WorkspaceStateStore.ReleaseStopNotificationClaim(claimPath);
                    if (ownsTurnDeliveryClaim)
                    {
                        WorkspaceStateStore.ReleaseStopNotificationClaim(turnClaimPath);
                    }

                    AppLog.SkippingDuplicateStop(
                        logger,
                        hookInput.SessionId,
                        turn.NotificationTurnId
                    );
                    return 0;
                }

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
                        SummaryPendingHandoff = summaryValidation.IsPendingHandoff,
                        SummaryFailureReason = summaryValidation.FailureReason,
                    },
                    cancellationToken
                );

                if (
                    summaryValidation.IsPendingHandoff
                    && (
                        summaryValidation.Record is null
                        || HasPendingStopAttributionForTurn(
                            summaryValidation,
                            turn,
                            hookInput.Timestamp
                        )
                        || IsHookCreatedPlaceholderSummary(summaryValidation.Record, turn)
                    )
                )
                {
                    await workspaceStateStore.MarkTurnAbandonedIfSupersededAsync(
                        workspacePath,
                        turn,
                        sentAt,
                        cancellationToken
                    );
                    WorkspaceStateStore.ReleaseStopNotificationClaim(claimPath);
                    if (ownsTurnDeliveryClaim)
                    {
                        WorkspaceStateStore.ReleaseStopNotificationClaim(turnClaimPath);
                    }

                    return 0;
                }

                try
                {
                    int sentMessageCount = await SendNotificationAsync(
                        hookInput,
                        workspacePath,
                        turn.NotificationTurnId,
                        sentAt,
                        summary,
                        cancellationToken
                    );
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
                            cancellationToken
                        );
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
                    cancellationToken
                );
            }
            catch
            {
                if (!notificationSent)
                {
                    WorkspaceStateStore.ReleaseStopNotificationClaim(claimPath);
                    if (ownsTurnDeliveryClaim)
                    {
                        WorkspaceStateStore.ReleaseStopNotificationClaim(turnClaimPath);
                    }
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
        CancellationToken cancellationToken
    )
    {
        string notificationKey = CreateStopNotificationKey(hookInput.Timestamp);
        if (
            await AnyPerTurnNotificationRecordExistsAsync(
                workspacePath,
                hookInput.SessionId,
                notificationKey,
                cancellationToken
            )
        )
        {
            AppLog.SkippingDuplicateStop(logger, hookInput.SessionId, notificationKey);
            return;
        }

        string notificationPath = AppPaths.GetSessionNotificationRecordPath(
            workspacePath,
            hookInput.SessionId,
            notificationKey
        );
        string claimPath = AppPaths.GetSessionStopClaimPath(
            workspacePath,
            hookInput.SessionId,
            notificationKey
        );
        string reclaimPath = AppPaths.GetSessionStopReclaimClaimPath(
            workspacePath,
            hookInput.SessionId,
            notificationKey
        );
        if (
            await WorkspaceStateStore.WasNotificationAlreadySentAsync(
                notificationPath,
                cancellationToken
            )
        )
        {
            AppLog.SkippingDuplicateStop(logger, hookInput.SessionId, notificationKey);
            return;
        }

        string claimedAt = workspaceStateStore.GetCurrentUtcTimestamp();
        bool claimedSessionStop = await WorkspaceStateStore.TryClaimStopNotificationAsync(
            claimPath,
            claimedAt,
            cancellationToken
        );
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
                        cancellationToken
                    )
                    || await AnyPerTurnNotificationRecordExistsAsync(
                        workspacePath,
                        hookInput.SessionId,
                        notificationKey,
                        cancellationToken
                    ),
                cancellationToken
            );
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
                    cancellationToken
                );
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
                            successfulMessageCount: ex.SuccessfulMessageCount
                        ),
                        cancellationToken
                    );
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
                    successfulMessageCount: null
                ),
                cancellationToken
            );
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
        CancellationToken cancellationToken
    )
    {
        GitRepositoryMetadata? repositoryMetadata = await gitRepositoryProbe.TryProbeAsync(
            workspacePath,
            cancellationToken
        );
        TelegramCredentials credentials = await telegramCredentialProvider.ResolveAsync(
            cancellationToken
        );
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
        AppLog.SendingStopNotification(logger, messages.Count, context.SessionId, context.TurnId);
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
        int? successfulMessageCount
    ) =>
        new()
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
        CancellationToken cancellationToken
    )
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
            successfulMessageCount
        );
        await WorkspaceStateStore.RecordNotificationAsync(
            notificationPath,
            record,
            cancellationToken
        );
        await WorkspaceStateStore.RecordNotificationAsync(
            sessionNotificationPath,
            record,
            cancellationToken
        );
        await WorkspaceStateStore.MarkTurnNotifiedAsync(
            workspacePath,
            turn,
            sentAt,
            cancellationToken
        );
    }

    private static PromptClassification ClassifyPrompt(UserPromptSubmitHookInput input)
    {
        string prompt = input.Prompt ?? string.Empty;
        if (string.IsNullOrWhiteSpace(prompt))
        {
            return new PromptClassification("observation-only", "prompt is empty or missing");
        }

        string trimmed = prompt.TrimStart();
        if (
            trimmed.StartsWith("<system_reminder>", StringComparison.OrdinalIgnoreCase)
            || trimmed.StartsWith("<system_notification>", StringComparison.OrdinalIgnoreCase)
            || trimmed.StartsWith("Contents of AGENTS.md", StringComparison.OrdinalIgnoreCase)
            || HasExplicitSubagentMarker(trimmed)
            || trimmed.Contains(
                "OA is not allowed to code directly",
                StringComparison.OrdinalIgnoreCase
            )
        )
        {
            return new PromptClassification(
                "observation-only",
                "prompt text matches explicit generated/subagent/system handoff markers"
            );
        }

        if (
            !string.IsNullOrWhiteSpace(input.HookEventName)
            && !string.Equals(input.HookEventName, "UserPromptSubmit", StringComparison.Ordinal)
        )
        {
            return new PromptClassification(
                "observation-only",
                "hook_event_name is not UserPromptSubmit"
            );
        }

        return new PromptClassification(
            "main-user-prompt",
            "UserPromptSubmit with non-empty prompt and no generated/subagent markers"
        );
    }

    private static bool IsExplicitSubagentHandoff(string trimmedPrompt)
    {
        if (!trimmedPrompt.StartsWith("You are ", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        int firstLineEnd = trimmedPrompt.IndexOfAny(['\r', '\n']);
        string firstLine = firstLineEnd < 0 ? trimmedPrompt : trimmedPrompt[..firstLineEnd];
        return firstLine.StartsWith(
                "You are the Coder subagent for ",
                StringComparison.OrdinalIgnoreCase
            )
            || firstLine.StartsWith(
                "You are an independent Reviewer subagent.",
                StringComparison.OrdinalIgnoreCase
            );
    }

    private static bool IsExplicitSubagentObservation(string trimmedPrompt)
    {
        int firstLineEnd = trimmedPrompt.IndexOfAny(['\r', '\n']);
        string firstLine = firstLineEnd < 0 ? trimmedPrompt : trimmedPrompt[..firstLineEnd];
        return firstLine.StartsWith(
            "Coder subagent observation:",
            StringComparison.OrdinalIgnoreCase
        );
    }

    private static bool HasExplicitSubagentMarker(string trimmedPrompt) =>
        IsExplicitSubagentHandoff(trimmedPrompt) || IsExplicitSubagentObservation(trimmedPrompt);

    private static StopResolution ResolveStopTurn(
        IReadOnlyList<NotificationTurn> openTurns,
        IReadOnlyList<NotificationTurn> freshClaimedOpenTurns,
        IReadOnlyList<PromptObservation> promptObservations,
        IReadOnlyList<NotificationRecord> durableNotificationRecords,
        string stopTimestamp
    )
    {
        if (!TryParseUtcTimestamp(stopTimestamp, out DateTimeOffset parsedStopTimestamp))
        {
            return new StopResolution(null, $"invalid Stop timestamp '{stopTimestamp}'");
        }

        if (openTurns.Count == 0)
        {
            if (HasEligibleTurn(freshClaimedOpenTurns, parsedStopTimestamp))
            {
                return new StopResolution(
                    null,
                    $"Stop {stopTimestamp} matches an active delivery claim",
                    SuppressFallback: true
                );
            }

            return new StopResolution(null, $"no open notification turn for Stop {stopTimestamp}");
        }

        NotificationTurn[] eligibleTurns = openTurns
            .Where(turn =>
                TryParseUtcTimestamp(turn.CreatedAt, out DateTimeOffset createdAt)
                && createdAt <= parsedStopTimestamp
            )
            .ToArray();
        if (eligibleTurns.Length == 1)
        {
            if (
                HasUnresolvedInterveningSubagentObservation(
                    eligibleTurns[0],
                    promptObservations,
                    durableNotificationRecords,
                    parsedStopTimestamp
                )
            )
            {
                return new StopResolution(
                    null,
                    "explicit observation-only subagent handoff intervened before "
                        + $"Stop {stopTimestamp}"
                );
            }

            return new StopResolution(
                eligibleTurns[0],
                "unique open notification turn created no later than Stop timestamp"
            );
        }

        if (eligibleTurns.Length == 0)
        {
            if (HasEligibleTurn(freshClaimedOpenTurns, parsedStopTimestamp))
            {
                return new StopResolution(
                    null,
                    $"Stop {stopTimestamp} matches an active delivery claim",
                    SuppressFallback: true
                );
            }

            return new StopResolution(
                null,
                $"no eligible open notification turn at or before Stop {stopTimestamp}"
            );
        }

        return new StopResolution(
            null,
            $"ambiguous Stop {stopTimestamp}: "
                + $"{eligibleTurns.Length} eligible open notification turns"
        );
    }

    private static bool HasEligibleTurn(
        IReadOnlyList<NotificationTurn> turns,
        DateTimeOffset stopTimestamp
    ) =>
        turns.Any(turn =>
            TryParseUtcTimestamp(turn.CreatedAt, out DateTimeOffset createdAt)
            && createdAt <= stopTimestamp
        );

    private static async Task<RecoverableAbandonedTurnsResult>
        ListRecoverableAbandonedTurnsForStopAsync(
        string workspacePath,
        string sessionId,
        IReadOnlyList<NotificationTurn> abandonedTurns,
        IReadOnlyList<NotificationRecord> sessionNotificationRecords,
        string notificationKey,
        string stopTimestamp,
        CancellationToken cancellationToken
    )
    {
        if (!TryParseUtcTimestamp(stopTimestamp, out DateTimeOffset parsedStopTimestamp))
        {
            return RecoverableAbandonedTurnsResult.Empty;
        }

        List<(
            NotificationTurn Turn,
            DateTimeOffset CreatedAt,
            SummaryValidationResult Validation
        )> eligibleAbandonedTurns = [];
        List<NotificationTurn> exactSummaryTurns = [];
        List<NotificationTurn> exactPendingSummaryTurns = [];
        DateTimeOffset latestCreatedAt = DateTimeOffset.MinValue;
        foreach (NotificationTurn abandonedTurn in abandonedTurns)
        {
            if (
                !TryParseUtcTimestamp(abandonedTurn.CreatedAt, out DateTimeOffset createdAt)
                || createdAt > parsedStopTimestamp
                || await WorkspaceStateStore.WasNotificationAlreadySentAsync(
                    AppPaths.GetNotificationRecordPath(
                        workspacePath,
                        sessionId,
                        abandonedTurn.NotificationTurnId,
                        notificationKey
                    ),
                    cancellationToken
                )
            )
            {
                continue;
            }

            SummaryValidationResult abandonedValidation = await ValidateSummaryOnceAsync(
                workspacePath,
                sessionId,
                abandonedTurn,
                cancellationToken
            );
            eligibleAbandonedTurns.Add((abandonedTurn, createdAt, abandonedValidation));
            if (
                abandonedValidation.IsValid
                && HasStopAttributionForTurn(abandonedValidation, abandonedTurn, stopTimestamp)
                && !HasInterveningSessionDelivery(
                    abandonedTurn,
                    sessionNotificationRecords,
                    parsedStopTimestamp
                )
            )
            {
                exactSummaryTurns.Add(abandonedTurn);
            }
            else if (
                abandonedValidation.IsPendingHandoff
                && HasPendingStopAttributionForTurn(
                    abandonedValidation,
                    abandonedTurn,
                    stopTimestamp
                )
            )
            {
                exactPendingSummaryTurns.Add(abandonedTurn);
            }

            if (createdAt > latestCreatedAt)
            {
                latestCreatedAt = createdAt;
            }
        }

        if (
            exactPendingSummaryTurns.Any(turn =>
                !HasInterveningSessionDelivery(
                    turn,
                    sessionNotificationRecords,
                    parsedStopTimestamp
                )
            )
        )
        {
            return new RecoverableAbandonedTurnsResult([], SuppressStop: true);
        }

        if (
            HasEqualCreatedAtUndeliverablePendingAbandonedHandoff(
                eligibleAbandonedTurns,
                exactSummaryTurns,
                sessionNotificationRecords,
                parsedStopTimestamp,
                stopTimestamp
            )
        )
        {
            return new RecoverableAbandonedTurnsResult([], SuppressStop: true);
        }

        if (exactSummaryTurns.Count > 0)
        {
            return exactSummaryTurns.Count == 1
                ? new RecoverableAbandonedTurnsResult([exactSummaryTurns[0]], SuppressStop: false)
                : new RecoverableAbandonedTurnsResult([], SuppressStop: true);
        }

        var latestEligibleAbandonedTurns =
            new List<(NotificationTurn Turn, SummaryValidationResult Validation)>();
        foreach (var (turn, createdAt, turnValidation) in eligibleAbandonedTurns)
        {
            if (createdAt == latestCreatedAt)
            {
                latestEligibleAbandonedTurns.Add((turn, turnValidation));
            }
        }

        if (latestEligibleAbandonedTurns.Count != 1)
        {
            if (
                latestEligibleAbandonedTurns.Count > 0
                && latestEligibleAbandonedTurns.Any(candidate =>
                    IsUndeliverablePendingAbandonedHandoff(
                        candidate.Turn,
                        candidate.Validation,
                        stopTimestamp
                    )
                    && !HasInterveningSessionDelivery(
                        candidate.Turn,
                        sessionNotificationRecords,
                        parsedStopTimestamp
                    )
                )
            )
            {
                return new RecoverableAbandonedTurnsResult([], SuppressStop: true);
            }

            return RecoverableAbandonedTurnsResult.Empty;
        }

        (NotificationTurn candidate, SummaryValidationResult validation) =
            latestEligibleAbandonedTurns[0];
        if (
            validation.IsPendingHandoff
            && HasStopAttributionForTurn(validation, candidate, stopTimestamp)
        )
        {
            return HasInterveningSessionDelivery(
                candidate,
                sessionNotificationRecords,
                parsedStopTimestamp
            )
                ? RecoverableAbandonedTurnsResult.Empty
                : new RecoverableAbandonedTurnsResult([candidate], SuppressStop: false);
        }

        if (validation.IsPendingHandoff && IsHookCreatedPlaceholderForStop(validation, candidate))
        {
            return HasInterveningSessionDelivery(
                candidate,
                sessionNotificationRecords,
                parsedStopTimestamp
            )
                ? RecoverableAbandonedTurnsResult.Empty
                : new RecoverableAbandonedTurnsResult([], SuppressStop: true);
        }

        if (validation.IsPendingHandoff && validation.Record is null)
        {
            return HasInterveningSessionDelivery(
                candidate,
                sessionNotificationRecords,
                parsedStopTimestamp
            )
                ? RecoverableAbandonedTurnsResult.Empty
                : new RecoverableAbandonedTurnsResult([], SuppressStop: true);
        }

        if (!HasStopAttributionForTurn(validation, candidate, stopTimestamp))
        {
            return RecoverableAbandonedTurnsResult.Empty;
        }

        return HasInterveningSessionDelivery(
            candidate,
            sessionNotificationRecords,
            parsedStopTimestamp
        )
            ? RecoverableAbandonedTurnsResult.Empty
            : new RecoverableAbandonedTurnsResult([candidate], SuppressStop: false);
    }

    private static async Task<RecoverableAbandonedTurnsResult>
        ListRecoverableExactCompletedAbandonedTurnsForStopAsync(
        string workspacePath,
        string sessionId,
        IReadOnlyList<NotificationTurn> abandonedTurns,
        IReadOnlyList<NotificationRecord> sessionNotificationRecords,
        string notificationKey,
        string stopTimestamp,
        CancellationToken cancellationToken
    )
    {
        if (!TryParseUtcTimestamp(stopTimestamp, out DateTimeOffset parsedStopTimestamp))
        {
            return RecoverableAbandonedTurnsResult.Empty;
        }

        List<NotificationTurn> exactCompletedTurns = [];
        List<NotificationTurn> exactPendingTurns = [];
        List<(
            NotificationTurn Turn,
            DateTimeOffset CreatedAt,
            SummaryValidationResult Validation
        )> eligibleAbandonedTurns = [];
        foreach (NotificationTurn abandonedTurn in abandonedTurns)
        {
            if (
                !TryParseUtcTimestamp(abandonedTurn.CreatedAt, out DateTimeOffset createdAt)
                || createdAt > parsedStopTimestamp
                || await WorkspaceStateStore.WasNotificationAlreadySentAsync(
                    AppPaths.GetNotificationRecordPath(
                        workspacePath,
                        sessionId,
                        abandonedTurn.NotificationTurnId,
                        notificationKey
                    ),
                    cancellationToken
                )
            )
            {
                continue;
            }

            SummaryValidationResult validation = await ValidateSummaryOnceAsync(
                workspacePath,
                sessionId,
                abandonedTurn,
                cancellationToken
            );
            eligibleAbandonedTurns.Add((abandonedTurn, createdAt, validation));
            if (
                validation.IsValid
                && HasStopAttributionForTurn(validation, abandonedTurn, stopTimestamp)
                && !HasInterveningSessionDelivery(
                    abandonedTurn,
                    sessionNotificationRecords,
                    parsedStopTimestamp
                )
            )
            {
                exactCompletedTurns.Add(abandonedTurn);
            }
            else if (
                validation.IsPendingHandoff
                && HasPendingStopAttributionForTurn(validation, abandonedTurn, stopTimestamp)
            )
            {
                exactPendingTurns.Add(abandonedTurn);
            }
        }

        if (
            exactPendingTurns.Any(turn =>
                !HasInterveningSessionDelivery(
                    turn,
                    sessionNotificationRecords,
                    parsedStopTimestamp
                )
            )
        )
        {
            return new RecoverableAbandonedTurnsResult([], SuppressStop: true);
        }

        if (
            HasEqualCreatedAtUndeliverablePendingAbandonedHandoff(
                eligibleAbandonedTurns,
                exactCompletedTurns,
                sessionNotificationRecords,
                parsedStopTimestamp,
                stopTimestamp
            )
        )
        {
            return new RecoverableAbandonedTurnsResult([], SuppressStop: true);
        }

        return exactCompletedTurns.Count > 1
            ? new RecoverableAbandonedTurnsResult([], SuppressStop: true)
            : new RecoverableAbandonedTurnsResult(
                exactCompletedTurns
                    .OrderBy(static turn => turn.CreatedAt, StringComparer.Ordinal)
                    .ToArray(),
                SuppressStop: false
            );
    }

    private static bool HasEqualCreatedAtUndeliverablePendingAbandonedHandoff(
        IReadOnlyList<(
            NotificationTurn Turn,
            DateTimeOffset CreatedAt,
            SummaryValidationResult Validation
        )> eligibleAbandonedTurns,
        IReadOnlyList<NotificationTurn> exactSummaryTurns,
        IReadOnlyList<NotificationRecord> sessionNotificationRecords,
        DateTimeOffset parsedStopTimestamp,
        string stopTimestamp
    )
    {
        foreach (NotificationTurn exactTurn in exactSummaryTurns)
        {
            if (!TryParseUtcTimestamp(exactTurn.CreatedAt, out DateTimeOffset exactCreatedAt))
            {
                continue;
            }

            if (
                eligibleAbandonedTurns.Any(candidate =>
                    !string.Equals(
                        candidate.Turn.NotificationTurnId,
                        exactTurn.NotificationTurnId,
                        StringComparison.Ordinal
                    )
                    && candidate.CreatedAt == exactCreatedAt
                    && IsUndeliverablePendingAbandonedHandoff(
                        candidate.Turn,
                        candidate.Validation,
                        stopTimestamp
                    )
                    && !HasInterveningSessionDelivery(
                        candidate.Turn,
                        sessionNotificationRecords,
                        parsedStopTimestamp
                    )
                )
            )
            {
                return true;
            }
        }

        return false;
    }

    private static bool HasInterveningSessionDelivery(
        NotificationTurn candidate,
        IReadOnlyList<NotificationRecord> sessionNotificationRecords,
        DateTimeOffset parsedStopTimestamp
    )
    {
        if (!TryParseUtcTimestamp(candidate.CreatedAt, out DateTimeOffset candidateCreatedAt))
        {
            return false;
        }

        return sessionNotificationRecords.Any(record =>
            TryParseUtcTimestamp(record.StopTimestamp, out DateTimeOffset recordStopTimestamp)
            && recordStopTimestamp > candidateCreatedAt
            && recordStopTimestamp <= parsedStopTimestamp
        );
    }

    private async Task<IReadOnlyList<NotificationTurn>> ListExactNotifiedStopRetryTurnsAsync(
        string workspacePath,
        string sessionId,
        string notificationKey,
        string stopTimestamp,
        CancellationToken cancellationToken
    )
    {
        if (!TryParseUtcTimestamp(stopTimestamp, out DateTimeOffset parsedStopTimestamp))
        {
            return [];
        }

        IReadOnlyList<NotificationTurn> notifiedTurns =
            await workspaceStateStore.ListNotifiedTurnsAsync(
                workspacePath,
                sessionId,
                cancellationToken
            );
        List<NotificationTurn> retryTurns = [];
        foreach (NotificationTurn turn in notifiedTurns)
        {
            if (
                !TryParseUtcTimestamp(turn.CreatedAt, out DateTimeOffset createdAt)
                || createdAt > parsedStopTimestamp
            )
            {
                continue;
            }

            string notificationPath = AppPaths.GetNotificationRecordPath(
                workspacePath,
                sessionId,
                turn.NotificationTurnId,
                notificationKey
            );
            if (
                await WorkspaceStateStore.WasNotificationAlreadySentAsync(
                    notificationPath,
                    cancellationToken
                )
            )
            {
                continue;
            }

            SummaryValidationResult validation = await ValidateSummaryOnceAsync(
                workspacePath,
                sessionId,
                turn,
                cancellationToken
            );
            if (HasStopAttributionForTurn(validation, turn, stopTimestamp))
            {
                retryTurns.Add(turn);
            }
        }

        return retryTurns.OrderBy(static turn => turn.CreatedAt, StringComparer.Ordinal).ToArray();
    }

    private static async Task<bool> AreAllPreferredTurnsNonUniqueExactStopAttributionsAsync(
        string workspacePath,
        string sessionId,
        IReadOnlyList<NotificationTurn> preferredOpenTurns,
        string stopTimestamp,
        CancellationToken cancellationToken
    )
    {
        if (preferredOpenTurns.Count <= 1)
        {
            return false;
        }

        foreach (NotificationTurn turn in preferredOpenTurns)
        {
            SummaryValidationResult validation = await ValidateSummaryOnceAsync(
                workspacePath,
                sessionId,
                turn,
                cancellationToken
            );
            if (!HasStopAttributionForTurn(validation, turn, stopTimestamp))
            {
                return false;
            }
        }

        return true;
    }

    private static async Task<bool> HasEqualCreatedAtExactSummaryPendingHandoffAmbiguityAsync(
        string workspacePath,
        string sessionId,
        IReadOnlyList<NotificationTurn> openTurns,
        string stopTimestamp,
        CancellationToken cancellationToken
    )
    {
        if (!TryParseUtcTimestamp(stopTimestamp, out DateTimeOffset parsedStopTimestamp))
        {
            return false;
        }

        NotificationTurn[] eligibleTurns = openTurns
            .Where(turn =>
                TryParseUtcTimestamp(turn.CreatedAt, out DateTimeOffset createdAt)
                && createdAt <= parsedStopTimestamp
            )
            .ToArray();
        if (eligibleTurns.Length <= 1)
        {
            return false;
        }

        foreach (NotificationTurn exactTurn in eligibleTurns)
        {
            if (!TryParseUtcTimestamp(exactTurn.CreatedAt, out DateTimeOffset exactCreatedAt))
            {
                continue;
            }

            SummaryValidationResult exactValidation = await ValidateSummaryOnceAsync(
                workspacePath,
                sessionId,
                exactTurn,
                cancellationToken
            );
            if (
                !exactValidation.IsValid
                || !HasStopAttributionForTurn(exactValidation, exactTurn, stopTimestamp)
            )
            {
                continue;
            }

            foreach (NotificationTurn pendingTurn in eligibleTurns)
            {
                if (
                    string.Equals(
                        pendingTurn.NotificationTurnId,
                        exactTurn.NotificationTurnId,
                        StringComparison.Ordinal
                    )
                    || !TryParseUtcTimestamp(
                        pendingTurn.CreatedAt,
                        out DateTimeOffset pendingCreatedAt
                    )
                    || pendingCreatedAt != exactCreatedAt
                )
                {
                    continue;
                }

                SummaryValidationResult pendingValidation = await ValidateSummaryOnceAsync(
                    workspacePath,
                    sessionId,
                    pendingTurn,
                    cancellationToken
                );
                if (
                    pendingValidation.IsPendingHandoff
                    && !HasStopAttributionForTurn(pendingValidation, pendingTurn, stopTimestamp)
                )
                {
                    return true;
                }
            }
        }

        return false;
    }

    private static async Task<IReadOnlyList<NotificationTurn>> PreferSingleValidSummaryTurnAsync(
        string workspacePath,
        string sessionId,
        CurrentNotificationState? current,
        IReadOnlyList<NotificationTurn> freshClaimedOpenTurns,
        IReadOnlyList<NotificationTurn> openTurns,
        IReadOnlyList<NotificationRecord> durableNotificationRecords,
        string stopTimestamp,
        CancellationToken cancellationToken
    )
    {
        if (!TryParseUtcTimestamp(stopTimestamp, out DateTimeOffset parsedStopTimestamp))
        {
            return openTurns;
        }

        NotificationTurn[] eligibleTurns = openTurns
            .Where(turn =>
                TryParseUtcTimestamp(turn.CreatedAt, out DateTimeOffset createdAt)
                && createdAt <= parsedStopTimestamp
            )
            .ToArray();
        NotificationTurn[] eligibleFreshClaimedTurns = freshClaimedOpenTurns
            .Where(turn =>
                TryParseUtcTimestamp(turn.CreatedAt, out DateTimeOffset createdAt)
                && createdAt <= parsedStopTimestamp
            )
            .ToArray();
        NotificationTurn? currentTurn = current is null
            ? null
            : eligibleTurns.FirstOrDefault(turn =>
                string.Equals(
                    turn.NotificationTurnId,
                    current.NotificationTurnId,
                    StringComparison.Ordinal
                )
            );
        NotificationTurn? freshCurrentTurn = current is null
            ? null
            : freshClaimedOpenTurns.FirstOrDefault(turn =>
                string.Equals(
                    turn.NotificationTurnId,
                    current.NotificationTurnId,
                    StringComparison.Ordinal
                )
            );
        if (
            freshCurrentTurn is not null
            && TryParseUtcTimestamp(
                freshCurrentTurn.CreatedAt,
                out DateTimeOffset freshCurrentCreatedAt
            )
            && TryParseUtcTimestamp(stopTimestamp, out DateTimeOffset parsedFreshStopTimestamp)
            && freshCurrentCreatedAt <= parsedFreshStopTimestamp
            && IsTurnAtLatestEligibleCreatedAt(
                freshCurrentTurn,
                eligibleTurns.Concat(eligibleFreshClaimedTurns).ToArray()
            )
        )
        {
            SummaryValidationResult freshCurrentValidation = await ValidateSummaryOnceAsync(
                workspacePath,
                sessionId,
                freshCurrentTurn,
                cancellationToken
            );
            if (HasStopAttributionForTurn(freshCurrentValidation, freshCurrentTurn, stopTimestamp))
            {
                return [];
            }
        }

        List<NotificationTurn> exactFreshClaimedSummaryTurns = [];
        foreach (NotificationTurn turn in freshClaimedOpenTurns)
        {
            if (
                !TryParseUtcTimestamp(turn.CreatedAt, out DateTimeOffset createdAt)
                || createdAt > parsedStopTimestamp
            )
            {
                continue;
            }

            SummaryValidationResult validation = await ValidateSummaryOnceAsync(
                workspacePath,
                sessionId,
                turn,
                cancellationToken
            );
            if (HasStopAttributionForTurn(validation, turn, stopTimestamp))
            {
                exactFreshClaimedSummaryTurns.Add(turn);
            }
        }

        if (
            exactFreshClaimedSummaryTurns.Count > 0
            && TrySelectLatestExactAtLatestEligibleTurn(
                exactFreshClaimedSummaryTurns,
                eligibleTurns
                    .Concat(freshClaimedOpenTurns)
                    .Where(turn =>
                        TryParseUtcTimestamp(turn.CreatedAt, out DateTimeOffset createdAt)
                        && createdAt <= parsedStopTimestamp
                    )
                    .ToArray(),
                out _
            )
        )
        {
            return [];
        }

        if (currentTurn is not null)
        {
            SummaryValidationResult currentValidation = await ValidateSummaryOnceAsync(
                workspacePath,
                sessionId,
                currentTurn,
                cancellationToken
            );
            if (
                await HasFreshClaimedExactSummaryForDifferentTurnAsync(
                    workspacePath,
                    sessionId,
                    currentTurn,
                    currentValidation,
                    freshClaimedOpenTurns,
                    eligibleTurns.Concat(eligibleFreshClaimedTurns).ToArray(),
                    stopTimestamp,
                    cancellationToken
                )
            )
            {
                return [];
            }
        }

        bool currentTurnIsLatestEligible =
            currentTurn is not null
            && TrySelectLatestEligibleTurn(
                [currentTurn],
                eligibleTurns.Concat(eligibleFreshClaimedTurns).ToArray(),
                out _
            );
        bool currentTurnTiesFreshClaimedLatestEligible =
            currentTurn is not null
            && IsTurnTiedWithFreshClaimedLatestEligible(
                currentTurn,
                eligibleFreshClaimedTurns,
                eligibleTurns.Concat(eligibleFreshClaimedTurns).ToArray()
            );
        if (
            (!currentTurnIsLatestEligible || currentTurnTiesFreshClaimedLatestEligible)
            && await HasBlockingFreshClaimedCompletedOrInvalidTurnAsync(
                workspacePath,
                sessionId,
                freshClaimedOpenTurns,
                eligibleTurns.Concat(eligibleFreshClaimedTurns).ToArray(),
                stopTimestamp,
                blockPendingHandoff: true,
                cancellationToken
            )
        )
        {
            return [];
        }

        if (eligibleTurns.Length <= 1)
        {
            bool onlyEligibleHasUnresolvedStopAttribution = false;
            NotificationTurn? onlyEligibleTurnWithInterveningDelivery = null;
            if (eligibleTurns.Length == 1)
            {
                SummaryValidationResult onlyEligibleValidation = await ValidateSummaryOnceAsync(
                    workspacePath,
                    sessionId,
                    eligibleTurns[0],
                    cancellationToken
                );
                bool onlyEligibleHasStopAttribution = HasStopAttributionForTurn(
                    onlyEligibleValidation,
                    eligibleTurns[0],
                    stopTimestamp
                );
                bool onlyEligibleHasInterveningDelivery =
                    onlyEligibleHasStopAttribution
                    && string.Equals(eligibleTurns[0].Status, "open", StringComparison.Ordinal)
                    && HasInterveningSessionDelivery(
                        eligibleTurns[0],
                        durableNotificationRecords,
                        parsedStopTimestamp
                    );
                onlyEligibleHasUnresolvedStopAttribution =
                    onlyEligibleHasStopAttribution && !onlyEligibleHasInterveningDelivery;
                if (onlyEligibleHasInterveningDelivery)
                {
                    onlyEligibleTurnWithInterveningDelivery = eligibleTurns[0];
                }
            }

            if (eligibleTurns.Length == 1 && freshCurrentTurn is not null)
            {
                if (
                    onlyEligibleHasUnresolvedStopAttribution
                    && TrySelectLatestEligibleTurn(
                        [freshCurrentTurn],
                        eligibleTurns.Concat(eligibleFreshClaimedTurns).ToArray(),
                        out _
                    )
                )
                {
                    return openTurns;
                }
            }

            if (
                currentTurn is null
                && !(eligibleTurns.Length == 1 && onlyEligibleHasUnresolvedStopAttribution)
                && await HasBlockingFreshClaimedCompletedOrInvalidTurnAsync(
                    workspacePath,
                    sessionId,
                    freshClaimedOpenTurns,
                    eligibleTurns.Concat(eligibleFreshClaimedTurns).ToArray(),
                    stopTimestamp,
                    blockPendingHandoff: true,
                    cancellationToken
                )
            )
            {
                return [];
            }

            if (eligibleTurns.Length == 1)
            {
                if (onlyEligibleHasUnresolvedStopAttribution)
                {
                    return openTurns;
                }
            }

            if (onlyEligibleTurnWithInterveningDelivery is not null)
            {
                return openTurns
                    .Where(turn =>
                        !string.Equals(
                            turn.NotificationTurnId,
                            onlyEligibleTurnWithInterveningDelivery.NotificationTurnId,
                            StringComparison.Ordinal
                        )
                    )
                    .ToArray();
            }

            return openTurns;
        }

        List<NotificationTurn> validSummaryTurns = [];
        List<NotificationTurn> exactStopSummaryTurns = [];
        List<NotificationTurn> exactPendingSummaryTurns = [];
        List<NotificationTurn> pendingSummaryTurns = [];
        List<NotificationTurn> hookPlaceholderPendingSummaryTurns = [];
        List<NotificationTurn> invalidSummaryTurns = [];
        HashSet<string> staleStopAttributedTurnIds = new(StringComparer.Ordinal);
        foreach (NotificationTurn turn in eligibleTurns)
        {
            SummaryValidationResult validation = await ValidateSummaryOnceAsync(
                workspacePath,
                sessionId,
                turn,
                cancellationToken
            );
            bool hasStopAttribution = HasStopAttributionForTurn(validation, turn, stopTimestamp);
            if (
                hasStopAttribution
                && HasInterveningSessionDelivery(
                    turn,
                    durableNotificationRecords,
                    parsedStopTimestamp
                )
            )
            {
                staleStopAttributedTurnIds.Add(turn.NotificationTurnId);
                continue;
            }

            if (validation.IsValid)
            {
                validSummaryTurns.Add(turn);
                if (
                    string.Equals(
                        validation.Record?.UpdatedAt,
                        stopTimestamp,
                        StringComparison.Ordinal
                    )
                )
                {
                    exactStopSummaryTurns.Add(turn);
                }
            }
            else if (validation.IsPendingHandoff && hasStopAttribution)
            {
                pendingSummaryTurns.Add(turn);
                exactPendingSummaryTurns.Add(turn);
            }
            else if (validation.IsPendingHandoff)
            {
                pendingSummaryTurns.Add(turn);
                if (
                    validation.Record is not null
                    && IsHookCreatedPlaceholderSummary(validation.Record, turn)
                )
                {
                    hookPlaceholderPendingSummaryTurns.Add(turn);
                }
            }
            else
            {
                invalidSummaryTurns.Add(turn);
            }
        }
        NotificationTurn[] selectionEligibleTurns =
            staleStopAttributedTurnIds.Count == 0
                ? eligibleTurns
                : eligibleTurns
                    .Where(turn => !staleStopAttributedTurnIds.Contains(turn.NotificationTurnId))
                    .ToArray();
        NotificationTurn[] filteredOpenTurns =
            staleStopAttributedTurnIds.Count == 0
                ? openTurns.ToArray()
                : openTurns
                    .Where(turn => !staleStopAttributedTurnIds.Contains(turn.NotificationTurnId))
                    .ToArray();
        List<NotificationTurn> exactStopAttributionTurns =
        [
            .. exactStopSummaryTurns,
            .. exactPendingSummaryTurns,
        ];
        List<NotificationTurn> nonExactPendingSummaryTurns = pendingSummaryTurns
            .Where(turn =>
                !exactPendingSummaryTurns.Any(exactPendingTurn =>
                    string.Equals(
                        exactPendingTurn.NotificationTurnId,
                        turn.NotificationTurnId,
                        StringComparison.Ordinal
                    )
                )
            )
            .ToList();
        if (
            HasEqualCreatedAtExactSummaryPendingHandoffAmbiguity(
                exactStopSummaryTurns,
                nonExactPendingSummaryTurns
            )
        )
        {
            return filteredOpenTurns;
        }

        if (currentTurn is not null)
        {
            SummaryValidationResult currentValidation = await ValidateSummaryOnceAsync(
                workspacePath,
                sessionId,
                currentTurn,
                cancellationToken
            );
            if (exactPendingSummaryTurns.Count > 0)
            {
                return exactPendingSummaryTurns;
            }

            NotificationTurn? observedPendingCompletedExactTurn =
                await SelectSingleObservedPendingCompletedExactTurnAsync(
                    workspacePath,
                    sessionId,
                    exactStopSummaryTurns,
                    durableNotificationRecords,
                    parsedStopTimestamp,
                    stopTimestamp,
                    cancellationToken
                );
            if (observedPendingCompletedExactTurn is not null)
            {
                return [observedPendingCompletedExactTurn];
            }

            if (
                TrySelectLatestExactAtLatestEligibleTurn(
                    exactPendingSummaryTurns,
                    selectionEligibleTurns,
                    out NotificationTurn? latestExactPendingTurn
                )
            )
            {
                return [latestExactPendingTurn];
            }

            if (
                exactStopSummaryTurns.Count == 1
                && exactStopAttributionTurns.Count == 1
                && currentTurnIsLatestEligible
                && ShouldPreferExactOlderSummary(
                    exactStopSummaryTurns[0],
                    currentTurn,
                    currentValidation,
                    stopTimestamp
                )
            )
            {
                return exactStopSummaryTurns;
            }

            if (
                exactPendingSummaryTurns.Count > 0
                && exactStopSummaryTurns.Count > 0
                && TrySelectLatestExactAtLatestEligibleTurn(
                    exactStopSummaryTurns,
                    selectionEligibleTurns,
                    out _
                )
            )
            {
                return exactPendingSummaryTurns;
            }

            if (
                TrySelectLatestExactAtLatestEligibleTurn(
                    exactStopSummaryTurns,
                    selectionEligibleTurns,
                    out NotificationTurn? latestExactStopTurn
                )
            )
            {
                return [latestExactStopTurn];
            }

            if (
                TrySelectTiedLatestExactAtLatestEligibleTurns(
                    exactStopAttributionTurns,
                    selectionEligibleTurns,
                    out NotificationTurn[]? tiedLatestExactTurns
                )
            )
            {
                return tiedLatestExactTurns;
            }

            if (
                TrySelectLatestEligibleTurn(
                    pendingSummaryTurns,
                    selectionEligibleTurns,
                    out NotificationTurn? latestPendingTurn
                )
            )
            {
                return [latestPendingTurn];
            }

            if (
                TrySelectLatestEligibleTurn(
                    invalidSummaryTurns,
                    selectionEligibleTurns,
                    out NotificationTurn? latestInvalidTurn
                )
            )
            {
                return [latestInvalidTurn];
            }

            if (
                TrySelectLatestEligibleTurn(
                    validSummaryTurns,
                    selectionEligibleTurns,
                    out NotificationTurn? latestValidTurn
                )
            )
            {
                return [latestValidTurn];
            }

            if (staleStopAttributedTurnIds.Contains(currentTurn.NotificationTurnId))
            {
                return filteredOpenTurns;
            }

            return [currentTurn];
        }

        if (exactPendingSummaryTurns.Count > 0)
        {
            return exactPendingSummaryTurns;
        }

        NotificationTurn? cachelessObservedPendingCompletedExactTurn =
            await SelectSingleObservedPendingCompletedExactTurnAsync(
                workspacePath,
                sessionId,
                exactStopSummaryTurns,
                durableNotificationRecords,
                parsedStopTimestamp,
                stopTimestamp,
                cancellationToken
            );
        if (cachelessObservedPendingCompletedExactTurn is not null)
        {
            return [cachelessObservedPendingCompletedExactTurn];
        }

        if (
            TrySelectLatestExactAtLatestEligibleTurn(
                exactStopAttributionTurns,
                selectionEligibleTurns,
                out NotificationTurn? latestCachelessExactTurn
            )
        )
        {
            return [latestCachelessExactTurn];
        }

        NotificationTurn? latestCachelessPendingAtLatestExactTurn =
            SelectUniqueLatestTurnForPendingObservation(exactPendingSummaryTurns);
        if (
            exactStopSummaryTurns.Count > 0
            && latestCachelessPendingAtLatestExactTurn is not null
            && IsTurnAtLatestEligibleCreatedAt(
                latestCachelessPendingAtLatestExactTurn,
                exactStopAttributionTurns.ToArray()
            )
        )
        {
            return [latestCachelessPendingAtLatestExactTurn];
        }

        NotificationTurn[] tiedLatestCachelessPendingAtLatestExactTurns =
            SelectTiedLatestTurnsForPendingObservation(exactPendingSummaryTurns);
        if (
            exactStopSummaryTurns.Count > 0
            && tiedLatestCachelessPendingAtLatestExactTurns.Length > 1
            && IsTurnAtLatestEligibleCreatedAt(
                tiedLatestCachelessPendingAtLatestExactTurns[0],
                exactStopAttributionTurns.ToArray()
            )
        )
        {
            return tiedLatestCachelessPendingAtLatestExactTurns;
        }

        if (
            TrySelectLatestExactAtLatestEligibleTurn(
                exactStopSummaryTurns,
                selectionEligibleTurns,
                out NotificationTurn? latestCachelessExactStopTurn
            )
        )
        {
            return [latestCachelessExactStopTurn];
        }

        if (
            TrySelectTiedLatestExactAtLatestEligibleTurns(
                exactStopAttributionTurns,
                selectionEligibleTurns,
                out NotificationTurn[]? tiedLatestCachelessExactTurns
            )
        )
        {
            return tiedLatestCachelessExactTurns;
        }

        if (
            exactStopSummaryTurns.Count == 1
            && exactStopAttributionTurns.Count == 1
            && await HasPendingStopObservationForTurnAsync(
                workspacePath,
                sessionId,
                exactStopSummaryTurns[0],
                stopTimestamp,
                cancellationToken
            )
        )
        {
            return exactStopSummaryTurns;
        }

        if (
            exactStopSummaryTurns.Count == 1
            && exactStopAttributionTurns.Count == 1
            && TrySelectLatestEligibleTurn(
                hookPlaceholderPendingSummaryTurns,
                selectionEligibleTurns,
                out _
            )
        )
        {
            return exactStopSummaryTurns;
        }

        if (
            TrySelectLatestEligibleTurn(
                pendingSummaryTurns,
                selectionEligibleTurns,
                out NotificationTurn? latestCachelessNonExactPendingTurn
            )
        )
        {
            return [latestCachelessNonExactPendingTurn];
        }

        if (
            TrySelectLatestEligibleTurn(
                validSummaryTurns,
                selectionEligibleTurns,
                out NotificationTurn? latestCachelessValidTurn
            )
        )
        {
            return [latestCachelessValidTurn];
        }

        if (
            TrySelectLatestEligibleTurn(
                invalidSummaryTurns,
                selectionEligibleTurns,
                out NotificationTurn? latestCachelessInvalidTurn
            )
        )
        {
            return [latestCachelessInvalidTurn];
        }

        return validSummaryTurns.Count == 1 ? validSummaryTurns : filteredOpenTurns;
    }

    private static bool HasEqualCreatedAtExactSummaryPendingHandoffAmbiguity(
        List<NotificationTurn> exactStopSummaryTurns,
        List<NotificationTurn> pendingSummaryTurns
    )
    {
        foreach (NotificationTurn exactTurn in exactStopSummaryTurns)
        {
            if (!TryParseUtcTimestamp(exactTurn.CreatedAt, out DateTimeOffset exactCreatedAt))
            {
                continue;
            }

            if (
                pendingSummaryTurns.Any(pendingTurn =>
                    !string.Equals(
                        pendingTurn.NotificationTurnId,
                        exactTurn.NotificationTurnId,
                        StringComparison.Ordinal
                    )
                    && TryParseUtcTimestamp(
                        pendingTurn.CreatedAt,
                        out DateTimeOffset pendingCreatedAt
                    )
                    && pendingCreatedAt == exactCreatedAt
                )
            )
            {
                return true;
            }
        }

        return false;
    }

    private static bool IsTurnAtLatestEligibleCreatedAt(
        NotificationTurn turn,
        NotificationTurn[] eligibleTurns
    )
    {
        if (!TryParseUtcTimestamp(turn.CreatedAt, out DateTimeOffset turnCreatedAt))
        {
            return false;
        }

        DateTimeOffset latestEligibleCreatedAt = eligibleTurns
            .Where(static candidate => TryParseUtcTimestamp(candidate.CreatedAt, out _))
            .Select(static candidate =>
            {
                _ = TryParseUtcTimestamp(candidate.CreatedAt, out DateTimeOffset createdAt);
                return createdAt;
            })
            .DefaultIfEmpty(DateTimeOffset.MinValue)
            .Max();
        return turnCreatedAt == latestEligibleCreatedAt;
    }

    private static bool IsTurnTiedWithFreshClaimedLatestEligible(
        NotificationTurn turn,
        NotificationTurn[] eligibleFreshClaimedTurns,
        NotificationTurn[] eligibleTurns
    )
    {
        if (!TryParseUtcTimestamp(turn.CreatedAt, out DateTimeOffset turnCreatedAt))
        {
            return false;
        }

        DateTimeOffset latestEligibleCreatedAt = eligibleTurns
            .Where(static candidate => TryParseUtcTimestamp(candidate.CreatedAt, out _))
            .Select(static candidate =>
            {
                _ = TryParseUtcTimestamp(candidate.CreatedAt, out DateTimeOffset createdAt);
                return createdAt;
            })
            .DefaultIfEmpty(DateTimeOffset.MinValue)
            .Max();
        return turnCreatedAt == latestEligibleCreatedAt
            && eligibleFreshClaimedTurns.Any(freshClaimedTurn =>
                TryParseUtcTimestamp(
                    freshClaimedTurn.CreatedAt,
                    out DateTimeOffset freshClaimedCreatedAt
                )
                && freshClaimedCreatedAt == latestEligibleCreatedAt
                && !string.Equals(
                    freshClaimedTurn.NotificationTurnId,
                    turn.NotificationTurnId,
                    StringComparison.Ordinal
                )
            );
    }

    private static bool TrySelectLatestEligibleTurn(
        List<NotificationTurn> candidateTurns,
        NotificationTurn[] eligibleTurns,
        [NotNullWhen(true)] out NotificationTurn? selectedTurn
    )
    {
        selectedTurn = null;
        if (candidateTurns.Count == 0)
        {
            return false;
        }

        NotificationTurn[] orderedCandidateTurns = candidateTurns
            .Where(static turn => TryParseUtcTimestamp(turn.CreatedAt, out _))
            .OrderByDescending(static turn =>
            {
                _ = TryParseUtcTimestamp(turn.CreatedAt, out DateTimeOffset createdAt);
                return createdAt;
            })
            .ToArray();
        if (orderedCandidateTurns.Length == 0)
        {
            return false;
        }

        _ = TryParseUtcTimestamp(
            orderedCandidateTurns[0].CreatedAt,
            out DateTimeOffset latestCandidateCreatedAt
        );
        if (
            orderedCandidateTurns.Length > 1
            && TryParseUtcTimestamp(
                orderedCandidateTurns[1].CreatedAt,
                out DateTimeOffset secondCandidateCreatedAt
            )
            && secondCandidateCreatedAt == latestCandidateCreatedAt
        )
        {
            return false;
        }

        DateTimeOffset latestEligibleCreatedAt = eligibleTurns
            .Where(static turn => TryParseUtcTimestamp(turn.CreatedAt, out _))
            .Select(static turn =>
            {
                _ = TryParseUtcTimestamp(turn.CreatedAt, out DateTimeOffset createdAt);
                return createdAt;
            })
            .DefaultIfEmpty(DateTimeOffset.MinValue)
            .Max();
        if (latestCandidateCreatedAt != latestEligibleCreatedAt)
        {
            return false;
        }

        selectedTurn = orderedCandidateTurns[0];
        return true;
    }

    private static NotificationTurn? SelectUniqueLatestTurnForPendingObservation(
        List<NotificationTurn> candidateTurns
    )
    {
        NotificationTurn[] orderedCandidateTurns = candidateTurns
            .Where(static turn => TryParseUtcTimestamp(turn.CreatedAt, out _))
            .OrderByDescending(static turn =>
            {
                _ = TryParseUtcTimestamp(turn.CreatedAt, out DateTimeOffset createdAt);
                return createdAt;
            })
            .ToArray();
        if (orderedCandidateTurns.Length == 0)
        {
            return null;
        }

        _ = TryParseUtcTimestamp(
            orderedCandidateTurns[0].CreatedAt,
            out DateTimeOffset latestCreatedAt
        );
        return
            orderedCandidateTurns.Length > 1
            && TryParseUtcTimestamp(
                orderedCandidateTurns[1].CreatedAt,
                out DateTimeOffset secondCreatedAt
            )
            && secondCreatedAt == latestCreatedAt
            ? null
            : orderedCandidateTurns[0];
    }

    private static NotificationTurn[] SelectTiedLatestTurnsForPendingObservation(
        List<NotificationTurn> candidateTurns
    )
    {
        NotificationTurn[] orderedCandidateTurns = candidateTurns
            .Where(static turn => TryParseUtcTimestamp(turn.CreatedAt, out _))
            .OrderByDescending(static turn =>
            {
                _ = TryParseUtcTimestamp(turn.CreatedAt, out DateTimeOffset createdAt);
                return createdAt;
            })
            .ToArray();
        if (orderedCandidateTurns.Length == 0)
        {
            return [];
        }

        _ = TryParseUtcTimestamp(
            orderedCandidateTurns[0].CreatedAt,
            out DateTimeOffset latestCreatedAt
        );
        return orderedCandidateTurns
            .Where(turn =>
                TryParseUtcTimestamp(turn.CreatedAt, out DateTimeOffset createdAt)
                && createdAt == latestCreatedAt
            )
            .ToArray();
    }

    private static bool TrySelectLatestExactAtLatestEligibleTurn(
        List<NotificationTurn> exactTurns,
        NotificationTurn[] eligibleTurns,
        [NotNullWhen(true)] out NotificationTurn? selectedTurn
    )
    {
        selectedTurn = null;
        if (exactTurns.Count == 0)
        {
            return false;
        }

        NotificationTurn[] orderedExactTurns = exactTurns
            .Where(static turn => TryParseUtcTimestamp(turn.CreatedAt, out _))
            .OrderByDescending(static turn =>
            {
                _ = TryParseUtcTimestamp(turn.CreatedAt, out DateTimeOffset createdAt);
                return createdAt;
            })
            .ToArray();
        if (orderedExactTurns.Length == 0)
        {
            return false;
        }

        _ = TryParseUtcTimestamp(
            orderedExactTurns[0].CreatedAt,
            out DateTimeOffset latestExactCreatedAt
        );
        if (
            orderedExactTurns.Length > 1
            && TryParseUtcTimestamp(
                orderedExactTurns[1].CreatedAt,
                out DateTimeOffset secondExactCreatedAt
            )
            && secondExactCreatedAt == latestExactCreatedAt
        )
        {
            return false;
        }

        DateTimeOffset latestEligibleCreatedAt = eligibleTurns
            .Where(static turn => TryParseUtcTimestamp(turn.CreatedAt, out _))
            .Select(static turn =>
            {
                _ = TryParseUtcTimestamp(turn.CreatedAt, out DateTimeOffset createdAt);
                return createdAt;
            })
            .DefaultIfEmpty(DateTimeOffset.MinValue)
            .Max();
        if (latestExactCreatedAt != latestEligibleCreatedAt)
        {
            return false;
        }

        selectedTurn = orderedExactTurns[0];
        return true;
    }

    private static bool TrySelectTiedLatestExactAtLatestEligibleTurns(
        List<NotificationTurn> exactTurns,
        NotificationTurn[] eligibleTurns,
        [NotNullWhen(true)] out NotificationTurn[]? selectedTurns
    )
    {
        selectedTurns = null;
        NotificationTurn[] tiedLatestExactTurns = SelectTiedLatestTurnsForPendingObservation(
            exactTurns
        );
        if (
            tiedLatestExactTurns.Length <= 1
            || !IsTurnAtLatestEligibleCreatedAt(tiedLatestExactTurns[0], eligibleTurns)
        )
        {
            return false;
        }

        selectedTurns = tiedLatestExactTurns;
        return true;
    }

    private static async Task<bool> HasFreshClaimedExactSummaryForDifferentTurnAsync(
        string workspacePath,
        string sessionId,
        NotificationTurn currentTurn,
        SummaryValidationResult currentValidation,
        IReadOnlyList<NotificationTurn> freshClaimedOpenTurns,
        NotificationTurn[] eligibleTurns,
        string stopTimestamp,
        CancellationToken cancellationToken
    )
    {
        if (HasCurrentStopAttribution(currentValidation, currentTurn, stopTimestamp))
        {
            return false;
        }

        foreach (NotificationTurn freshClaimedTurn in freshClaimedOpenTurns)
        {
            if (
                !IsDifferentTurnWithParsableStopTimestamp(
                    freshClaimedTurn,
                    currentTurn,
                    stopTimestamp
                )
            )
            {
                continue;
            }

            if (
                !TryParseUtcTimestamp(freshClaimedTurn.CreatedAt, out DateTimeOffset createdAt)
                || !TryParseUtcTimestamp(stopTimestamp, out DateTimeOffset parsedStopTimestamp)
                || createdAt > parsedStopTimestamp
            )
            {
                continue;
            }

            if (!IsTurnAtLatestEligibleCreatedAt(freshClaimedTurn, eligibleTurns))
            {
                continue;
            }

            if (!currentValidation.IsValid && !currentValidation.IsPendingHandoff)
            {
                return true;
            }

            SummaryValidationResult validation = await ValidateSummaryOnceAsync(
                workspacePath,
                sessionId,
                freshClaimedTurn,
                cancellationToken
            );
            if (HasStopAttributionForTurn(validation, freshClaimedTurn, stopTimestamp))
            {
                return true;
            }
        }

        return false;
    }

    private static bool HasBlockingFreshClaimedTurn(
        IReadOnlyList<NotificationTurn> openTurns,
        IReadOnlyList<NotificationTurn> freshClaimedOpenTurns,
        string stopTimestamp
    )
    {
        if (!TryParseUtcTimestamp(stopTimestamp, out DateTimeOffset parsedStopTimestamp))
        {
            return false;
        }

        NotificationTurn[] eligibleFreshClaimedTurns = freshClaimedOpenTurns
            .Where(turn =>
                TryParseUtcTimestamp(turn.CreatedAt, out DateTimeOffset createdAt)
                && createdAt <= parsedStopTimestamp
            )
            .ToArray();
        if (eligibleFreshClaimedTurns.Length == 0)
        {
            return false;
        }

        NotificationTurn[] eligibleTurns = openTurns
            .Concat(eligibleFreshClaimedTurns)
            .Where(turn =>
                TryParseUtcTimestamp(turn.CreatedAt, out DateTimeOffset createdAt)
                && createdAt <= parsedStopTimestamp
            )
            .ToArray();
        DateTimeOffset latestEligibleCreatedAt = eligibleTurns
            .Select(static turn =>
            {
                _ = TryParseUtcTimestamp(turn.CreatedAt, out DateTimeOffset createdAt);
                return createdAt;
            })
            .DefaultIfEmpty(DateTimeOffset.MinValue)
            .Max();
        bool hasLatestFreshClaimedTurn = eligibleFreshClaimedTurns.Any(turn =>
        {
            _ = TryParseUtcTimestamp(turn.CreatedAt, out DateTimeOffset createdAt);
            return createdAt == latestEligibleCreatedAt;
        });
        if (!hasLatestFreshClaimedTurn)
        {
            return false;
        }

        return true;
    }

    private static async Task<bool> HasBlockingFreshClaimedCompletedOrInvalidTurnAsync(
        string workspacePath,
        string sessionId,
        IReadOnlyList<NotificationTurn> freshClaimedOpenTurns,
        NotificationTurn[] eligibleTurns,
        string stopTimestamp,
        bool blockPendingHandoff,
        CancellationToken cancellationToken
    )
    {
        if (!TryParseUtcTimestamp(stopTimestamp, out DateTimeOffset parsedStopTimestamp))
        {
            return false;
        }

        NotificationTurn[] eligibleFreshClaimedTurns = freshClaimedOpenTurns
            .Where(turn =>
                TryParseUtcTimestamp(turn.CreatedAt, out DateTimeOffset createdAt)
                && createdAt <= parsedStopTimestamp
            )
            .ToArray();
        if (eligibleFreshClaimedTurns.Length == 0)
        {
            return false;
        }

        DateTimeOffset latestFreshClaimedCreatedAt = eligibleFreshClaimedTurns
            .Select(static turn =>
            {
                _ = TryParseUtcTimestamp(turn.CreatedAt, out DateTimeOffset createdAt);
                return createdAt;
            })
            .Max();
        DateTimeOffset latestEligibleCreatedAt = eligibleTurns
            .Where(static turn => TryParseUtcTimestamp(turn.CreatedAt, out _))
            .Select(static turn =>
            {
                _ = TryParseUtcTimestamp(turn.CreatedAt, out DateTimeOffset createdAt);
                return createdAt;
            })
            .DefaultIfEmpty(DateTimeOffset.MinValue)
            .Max();
        if (latestFreshClaimedCreatedAt != latestEligibleCreatedAt)
        {
            return false;
        }

        if (blockPendingHandoff)
        {
            _ = (workspacePath, sessionId, cancellationToken);
            return true;
        }

        if (
            !TrySelectLatestEligibleTurn(
                eligibleFreshClaimedTurns.ToList(),
                eligibleTurns,
                out NotificationTurn? latestFreshClaimedTurn
            )
        )
        {
            foreach (
                NotificationTurn tiedFreshClaimedTurn in eligibleFreshClaimedTurns.Where(turn =>
                {
                    _ = TryParseUtcTimestamp(turn.CreatedAt, out DateTimeOffset createdAt);
                    return createdAt == latestFreshClaimedCreatedAt;
                })
            )
            {
                if (
                    await IsFreshClaimedCompletedOrInvalidTurnAsync(
                        workspacePath,
                        sessionId,
                        tiedFreshClaimedTurn,
                        cancellationToken
                    )
                )
                {
                    return true;
                }
            }

            return false;
        }

        return await IsFreshClaimedCompletedOrInvalidTurnAsync(
            workspacePath,
            sessionId,
            latestFreshClaimedTurn,
            cancellationToken
        );
    }

    private static async Task<bool> IsFreshClaimedCompletedOrInvalidTurnAsync(
        string workspacePath,
        string sessionId,
        NotificationTurn latestFreshClaimedTurn,
        CancellationToken cancellationToken
    )
    {
        SummaryValidationResult latestFreshClaimedValidation = await ValidateSummaryOnceAsync(
            workspacePath,
            sessionId,
            latestFreshClaimedTurn,
            cancellationToken
        );
        return latestFreshClaimedValidation.IsValid
            || !latestFreshClaimedValidation.IsPendingHandoff;
    }

    private static bool ShouldPreferExactOlderSummary(
        NotificationTurn candidateTurn,
        NotificationTurn currentTurn,
        SummaryValidationResult currentValidation,
        string stopTimestamp
    )
    {
        return IsDifferentTurnWithParsableStopTimestamp(
                candidateTurn,
                currentTurn,
                stopTimestamp
            )
            && !HasCurrentStopAttribution(currentValidation, currentTurn, stopTimestamp);
    }

    private static async Task<
        IReadOnlyList<NotificationTurn>
    > FilterFreshClaimedTurnsWithStopAttributionAsync(
        string workspacePath,
        string sessionId,
        IReadOnlyList<NotificationTurn> openTurns,
        IReadOnlyList<NotificationTurn> freshClaimedOpenTurns,
        string stopTimestamp,
        CancellationToken cancellationToken
    )
    {
        if (!TryParseUtcTimestamp(stopTimestamp, out DateTimeOffset parsedStopTimestamp))
        {
            return [];
        }

        NotificationTurn[] eligibleFreshClaimedTurns = freshClaimedOpenTurns
            .Where(turn =>
                TryParseUtcTimestamp(turn.CreatedAt, out DateTimeOffset createdAt)
                && createdAt <= parsedStopTimestamp
            )
            .ToArray();
        if (eligibleFreshClaimedTurns.Length == 0)
        {
            return [];
        }

        DateTimeOffset latestEligibleCreatedAt = openTurns
            .Concat(eligibleFreshClaimedTurns)
            .Where(turn =>
                TryParseUtcTimestamp(turn.CreatedAt, out DateTimeOffset createdAt)
                && createdAt <= parsedStopTimestamp
            )
            .Select(static turn =>
            {
                _ = TryParseUtcTimestamp(turn.CreatedAt, out DateTimeOffset createdAt);
                return createdAt;
            })
            .DefaultIfEmpty(DateTimeOffset.MinValue)
            .Max();
        List<NotificationTurn> matchingTurns = [];
        foreach (NotificationTurn turn in eligibleFreshClaimedTurns)
        {
            _ = TryParseUtcTimestamp(turn.CreatedAt, out DateTimeOffset createdAt);
            if (createdAt != latestEligibleCreatedAt)
            {
                continue;
            }

            SummaryValidationResult validation = await ValidateSummaryOnceAsync(
                workspacePath,
                sessionId,
                turn,
                cancellationToken
            );
            if (HasCurrentStopAttribution(validation, turn, stopTimestamp))
            {
                matchingTurns.Add(turn);
            }
        }

        return matchingTurns;
    }

    private static bool HasCurrentStopAttribution(
        SummaryValidationResult currentValidation,
        NotificationTurn currentTurn,
        string stopTimestamp
    ) => HasStopAttributionForTurn(currentValidation, currentTurn, stopTimestamp);

    private static bool HasStopAttributionForTurn(
        SummaryValidationResult currentValidation,
        NotificationTurn turn,
        string stopTimestamp
    )
    {
        NotificationSummary? summary = currentValidation.Record;
        return summary is not null
            && string.Equals(summary.SessionId, turn.SessionId, StringComparison.Ordinal)
            && string.Equals(
                summary.NotificationTurnId,
                turn.NotificationTurnId,
                StringComparison.Ordinal
            )
            && string.Equals(
                summary.NotificationNonce,
                turn.NotificationNonce,
                StringComparison.Ordinal
            )
            && string.Equals(summary.UpdatedAt, stopTimestamp, StringComparison.Ordinal)
            && !IsHookCreatedPlaceholderSummary(summary, turn);
    }

    private static bool HasPendingStopAttributionForTurn(
        SummaryValidationResult currentValidation,
        NotificationTurn turn,
        string stopTimestamp
    ) => HasStopAttributionForTurn(currentValidation, turn, stopTimestamp);

    private static bool IsHookCreatedPlaceholderSummary(
        NotificationSummary summary,
        NotificationTurn turn
    ) =>
        string.Equals(summary.Status, "pending", StringComparison.Ordinal)
        && summary.Summary is null
        && (
            IsProvenHookCreatedPlaceholderSummary(summary, turn)
            || IsLegacyHookCreatedPlaceholderSummary(summary, turn)
        );

    private static bool IsProvenHookCreatedPlaceholderSummary(
        NotificationSummary summary,
        NotificationTurn turn
    ) =>
        !string.IsNullOrWhiteSpace(turn.SummaryPlaceholderCreatedAt)
        && string.Equals(
            GetPlaceholderCreatedAt(summary),
            turn.SummaryPlaceholderCreatedAt,
            StringComparison.Ordinal
        );

    private static string? GetPlaceholderCreatedAt(NotificationSummary summary) =>
        summary.PlaceholderCreatedAt ?? summary.UpdatedAt;

    private static bool IsLegacyHookCreatedPlaceholderSummary(
        NotificationSummary summary,
        NotificationTurn turn
    ) =>
        string.IsNullOrWhiteSpace(turn.SummaryPlaceholderCreatedAt)
        && (
            string.Equals(
                GetPlaceholderCreatedAt(summary),
                turn.CreatedAt,
                StringComparison.Ordinal
            )
            || string.Equals(
                GetPlaceholderCreatedAt(summary),
                turn.UpdatedAt,
                StringComparison.Ordinal
            )
        );

    private static bool IsHookCreatedPlaceholderForStop(
        SummaryValidationResult validation,
        NotificationTurn turn
    ) => validation.Record is not null && IsHookCreatedPlaceholderSummary(validation.Record, turn);

    private static bool IsUndeliverablePendingAbandonedHandoff(
        NotificationTurn turn,
        SummaryValidationResult validation,
        string stopTimestamp
    ) =>
        validation.IsPendingHandoff
        && (
            validation.Record is null
            || HasPendingStopAttributionForTurn(validation, turn, stopTimestamp)
            || IsHookCreatedPlaceholderForStop(validation, turn)
        );

    private static bool IsDifferentTurnWithParsableStopTimestamp(
        NotificationTurn candidateTurn,
        NotificationTurn currentTurn,
        string stopTimestamp
    ) =>
        !string.Equals(
            candidateTurn.NotificationTurnId,
            currentTurn.NotificationTurnId,
            StringComparison.Ordinal
        ) && TryParseUtcTimestamp(stopTimestamp, out _);

    private static bool ResolvedTurnHasPositiveStopAttribution(
        string stopTimestamp,
        SummaryValidationResult summaryValidation
    ) =>
        summaryValidation.IsValid
        && string.Equals(
            summaryValidation.Record?.UpdatedAt,
            stopTimestamp,
            StringComparison.Ordinal
        );

    private static bool HasPriorNonExactDurableDelivery(
        IReadOnlyList<NotificationRecord> sessionNotificationRecords,
        string stopTimestamp
    )
    {
        if (!TryParseUtcTimestamp(stopTimestamp, out DateTimeOffset parsedStopTimestamp))
        {
            return false;
        }

        return sessionNotificationRecords.Any(record =>
            !string.Equals(record.SummaryUpdatedAt, stopTimestamp, StringComparison.Ordinal)
            && TryParseUtcTimestamp(record.StopTimestamp, out DateTimeOffset recordStopTimestamp)
            && recordStopTimestamp <= parsedStopTimestamp
            && parsedStopTimestamp - recordStopTimestamp <= TimeSpan.FromSeconds(5)
        );
    }

    private static async Task<bool> HasPriorClosedPerTurnDurableDeliveryAsync(
        string workspacePath,
        string sessionId,
        IReadOnlyList<NotificationRecord> perTurnNotificationRecords,
        string stopTimestamp,
        CancellationToken cancellationToken
    )
    {
        if (!TryParseUtcTimestamp(stopTimestamp, out DateTimeOffset parsedStopTimestamp))
        {
            return false;
        }

        foreach (NotificationRecord record in perTurnNotificationRecords)
        {
            if (
                string.IsNullOrWhiteSpace(record.NotificationTurnId)
                || !IsDurableDeliveryStatus(record.DeliveryStatus)
                || !TryParseUtcTimestamp(
                    record.StopTimestamp,
                    out DateTimeOffset recordStopTimestamp
                )
                || recordStopTimestamp > parsedStopTimestamp
            )
            {
                continue;
            }

            NotificationTurn? turn = await TryReadNotificationTurnAsync(
                workspacePath,
                sessionId,
                record.NotificationTurnId,
                cancellationToken
            );
            if (!string.Equals(turn?.Status, "open", StringComparison.Ordinal))
            {
                return true;
            }
        }

        return false;
    }

    private static async Task<bool> HasDurableDeliveryForCurrentTurnSummaryAsync(
        string workspacePath,
        string sessionId,
        IReadOnlyList<NotificationTurn> candidateTurns,
        IReadOnlyList<NotificationRecord> durableNotificationRecords,
        string stopTimestamp,
        CancellationToken cancellationToken
    )
    {
        if (!TryParseUtcTimestamp(stopTimestamp, out DateTimeOffset parsedStopTimestamp))
        {
            return false;
        }

        NotificationTurn[] eligibleTurns = candidateTurns
            .Where(turn =>
                TryParseUtcTimestamp(turn.CreatedAt, out DateTimeOffset createdAt)
                && createdAt <= parsedStopTimestamp
            )
            .ToArray();
        if (eligibleTurns.Length > 1)
        {
            return false;
        }

        if (eligibleTurns.Length == 1)
        {
            NotificationTurn turn = eligibleTurns[0];
            if (string.Equals(turn.Status, "open", StringComparison.Ordinal))
            {
                SummaryValidationResult validation = await ValidateSummaryOnceAsync(
                    workspacePath,
                    sessionId,
                    turn,
                    cancellationToken
                );
                string? summaryUpdatedAt = validation.Record?.UpdatedAt;
                if (
                    !string.IsNullOrWhiteSpace(summaryUpdatedAt)
                    && durableNotificationRecords.Any(record =>
                        string.Equals(
                            record.NotificationTurnId,
                            turn.NotificationTurnId,
                            StringComparison.Ordinal
                        )
                        && IsDurableDeliveryStatus(record.DeliveryStatus)
                        && (
                            string.Equals(
                                record.SummaryUpdatedAt,
                                summaryUpdatedAt,
                                StringComparison.Ordinal
                            )
                            || string.Equals(
                                record.StopTimestamp,
                                summaryUpdatedAt,
                                StringComparison.Ordinal
                            )
                        )
                    )
                )
                {
                    return true;
                }
            }
        }

        foreach (NotificationRecord record in durableNotificationRecords)
        {
            if (
                string.IsNullOrWhiteSpace(record.NotificationTurnId)
                || !IsDurableDeliveryStatus(record.DeliveryStatus)
            )
            {
                continue;
            }

            string summaryPath = AppPaths.GetSummaryStatePath(
                workspacePath,
                sessionId,
                record.NotificationTurnId
            );
            if (!File.Exists(summaryPath))
            {
                continue;
            }

            NotificationTurn? turn = await TryReadNotificationTurnAsync(
                workspacePath,
                sessionId,
                record.NotificationTurnId,
                cancellationToken
            );
            if (!string.Equals(turn?.Status, "open", StringComparison.Ordinal))
            {
                continue;
            }

            try
            {
                await using FileStream stream = File.OpenRead(summaryPath);
                NotificationSummary? summary = await JsonSerializer.DeserializeAsync(
                    stream,
                    AppJsonSerializerContext.Default.NotificationSummary,
                    cancellationToken
                );
                if (
                    summary is not null
                    && string.Equals(summary.SessionId, sessionId, StringComparison.Ordinal)
                    && string.Equals(
                        summary.NotificationTurnId,
                        record.NotificationTurnId,
                        StringComparison.Ordinal
                    )
                    && (
                        string.Equals(
                            record.SummaryUpdatedAt,
                            summary.UpdatedAt,
                            StringComparison.Ordinal
                        )
                        || string.Equals(
                            record.StopTimestamp,
                            summary.UpdatedAt,
                            StringComparison.Ordinal
                        )
                    )
                )
                {
                    return true;
                }
            }
            catch (Exception ex)
                when (ex
                        is IOException
                            or JsonException
                            or UnauthorizedAccessException
                            or NotSupportedException
                )
            {
                continue;
            }
        }

        return false;
    }

    private static bool IsDurableDeliveryStatus(string? deliveryStatus) =>
        string.Equals(deliveryStatus, "sent", StringComparison.Ordinal)
        || string.Equals(deliveryStatus, "partial", StringComparison.Ordinal);

    private static async Task<NotificationTurn?> TryReadNotificationTurnAsync(
        string workspacePath,
        string sessionId,
        string notificationTurnId,
        CancellationToken cancellationToken
    )
    {
        string turnPath = AppPaths.GetTurnStatePath(workspacePath, sessionId, notificationTurnId);
        if (!File.Exists(turnPath))
        {
            return null;
        }

        try
        {
            await using FileStream stream = File.OpenRead(turnPath);
            return await JsonSerializer.DeserializeAsync(
                stream,
                AppJsonSerializerContext.Default.NotificationTurn,
                cancellationToken
            );
        }
        catch (Exception ex)
            when (ex
                    is IOException
                        or JsonException
                        or UnauthorizedAccessException
                        or NotSupportedException
            )
        {
            return null;
        }
    }

    private static async Task<bool> IsCurrentTurnFreshClaimedAsync(
        string workspacePath,
        string sessionId,
        CurrentNotificationState? current,
        IReadOnlyList<NotificationTurn> freshClaimedOpenTurns,
        IReadOnlyList<NotificationTurn> preferredOpenTurns,
        string stopTimestamp,
        CancellationToken cancellationToken
    )
    {
        if (
            current is null
            || !TryParseUtcTimestamp(stopTimestamp, out DateTimeOffset parsedStopTimestamp)
        )
        {
            return false;
        }

        NotificationTurn? freshCurrentTurn = freshClaimedOpenTurns.FirstOrDefault(turn =>
            string.Equals(
                turn.NotificationTurnId,
                current.NotificationTurnId,
                StringComparison.Ordinal
            )
            && TryParseUtcTimestamp(turn.CreatedAt, out DateTimeOffset createdAt)
            && createdAt <= parsedStopTimestamp
        );
        if (freshCurrentTurn is null)
        {
            return false;
        }

        if (
            !TrySelectLatestEligibleTurn(
                [freshCurrentTurn],
                preferredOpenTurns
                    .Concat(freshClaimedOpenTurns)
                    .Where(turn =>
                        TryParseUtcTimestamp(turn.CreatedAt, out DateTimeOffset createdAt)
                        && createdAt <= parsedStopTimestamp
                    )
                    .ToArray(),
                out _
            )
        )
        {
            return false;
        }

        SummaryValidationResult currentValidation = await ValidateSummaryOnceAsync(
            workspacePath,
            sessionId,
            freshCurrentTurn,
            cancellationToken
        );
        if (HasCurrentStopAttribution(currentValidation, freshCurrentTurn, stopTimestamp))
        {
            return false;
        }

        if (
            preferredOpenTurns.Count == 1
            && !string.Equals(
                preferredOpenTurns[0].NotificationTurnId,
                current.NotificationTurnId,
                StringComparison.Ordinal
            )
            && CanDifferentTurnWithParsableStopTimestampPreemptCurrent(
                preferredOpenTurns[0],
                freshCurrentTurn,
                stopTimestamp
            )
            && ResolvedTurnHasPositiveStopAttribution(
                stopTimestamp,
                await ValidateSummaryOnceAsync(
                    workspacePath,
                    sessionId,
                    preferredOpenTurns[0],
                    cancellationToken
                )
            )
        )
        {
            return false;
        }

        return true;
    }

    private static bool CanDifferentTurnWithParsableStopTimestampPreemptCurrent(
        NotificationTurn candidateTurn,
        NotificationTurn currentTurn,
        string stopTimestamp
    ) => IsDifferentTurnWithParsableStopTimestamp(candidateTurn, currentTurn, stopTimestamp);

    private static async Task<bool> HasPendingStopObservationOnAbandonedTurnAsync(
        string workspacePath,
        string sessionId,
        IReadOnlyList<NotificationTurn> abandonedTurns,
        IReadOnlyList<NotificationRecord> sessionNotificationRecords,
        string notificationKey,
        string stopTimestamp,
        CancellationToken cancellationToken
    )
    {
        if (!TryParseUtcTimestamp(stopTimestamp, out DateTimeOffset parsedStopTimestamp))
        {
            return false;
        }

        foreach (NotificationTurn turn in abandonedTurns)
        {
            if (
                File.Exists(
                    AppPaths.GetNotificationRecordPath(
                        workspacePath,
                        sessionId,
                        turn.NotificationTurnId,
                        notificationKey
                    )
                )
            )
            {
                continue;
            }

            string observationPath = AppPaths.GetStopObservationPath(
                workspacePath,
                sessionId,
                turn.NotificationTurnId,
                notificationKey
            );
            if (!File.Exists(observationPath))
            {
                continue;
            }

            try
            {
                await using FileStream stream = File.OpenRead(observationPath);
                StopObservation? observation = await JsonSerializer.DeserializeAsync(
                    stream,
                    AppJsonSerializerContext.Default.StopObservation,
                    cancellationToken
                );
                if (
                    observation is not null
                    && string.Equals(observation.SessionId, sessionId, StringComparison.Ordinal)
                    && string.Equals(
                        observation.NotificationTurnId,
                        turn.NotificationTurnId,
                        StringComparison.Ordinal
                    )
                    && string.Equals(observation.StopId, notificationKey, StringComparison.Ordinal)
                    && observation.SummaryPendingHandoff
                    && !HasInterveningSessionDelivery(
                        turn,
                        sessionNotificationRecords,
                        parsedStopTimestamp
                    )
                )
                {
                    SummaryValidationResult currentSummaryValidation =
                        await ValidateSummaryOnceAsync(
                            workspacePath,
                            sessionId,
                            turn,
                            cancellationToken
                        );
                    if (
                        currentSummaryValidation.IsPendingHandoff
                        && (
                            currentSummaryValidation.Record is null
                            || HasPendingStopAttributionForTurn(
                                currentSummaryValidation,
                                turn,
                                stopTimestamp
                            )
                            || IsHookCreatedPlaceholderForStop(currentSummaryValidation, turn)
                        )
                    )
                    {
                        return true;
                    }
                }
            }
            catch (Exception ex)
                when (ex
                        is IOException
                            or JsonException
                            or UnauthorizedAccessException
                            or NotSupportedException
                )
            {
                continue;
            }
        }

        return false;
    }

    private static async Task<bool> HasAnyPendingHandoffAbandonedTurnForStopAsync(
        string workspacePath,
        string sessionId,
        IReadOnlyList<NotificationTurn> abandonedTurns,
        string stopTimestamp,
        CancellationToken cancellationToken
    )
    {
        if (!TryParseUtcTimestamp(stopTimestamp, out DateTimeOffset parsedStopTimestamp))
        {
            return false;
        }

        foreach (NotificationTurn turn in abandonedTurns)
        {
            if (
                !TryParseUtcTimestamp(turn.CreatedAt, out DateTimeOffset createdAt)
                || createdAt > parsedStopTimestamp
                || !await HasPendingStopObservationForTurnAsync(
                    workspacePath,
                    sessionId,
                    turn,
                    stopTimestamp,
                    cancellationToken
                )
            )
            {
                continue;
            }

            SummaryValidationResult validation = await ValidateSummaryOnceAsync(
                workspacePath,
                sessionId,
                turn,
                cancellationToken
            );
            if (
                validation.IsPendingHandoff
                && (
                    validation.Record is null
                    || HasPendingStopAttributionForTurn(validation, turn, stopTimestamp)
                    || IsHookCreatedPlaceholderForStop(validation, turn)
                )
            )
            {
                return true;
            }
        }

        return false;
    }

    private static async Task<bool> HasPendingStopObservationForTurnAsync(
        string workspacePath,
        string sessionId,
        NotificationTurn turn,
        string stopTimestamp,
        CancellationToken cancellationToken
    )
    {
        string notificationKey = CreateStopNotificationKey(stopTimestamp);
        string observationPath = AppPaths.GetStopObservationPath(
            workspacePath,
            sessionId,
            turn.NotificationTurnId,
            notificationKey
        );
        if (!File.Exists(observationPath))
        {
            return false;
        }

        try
        {
            await using FileStream stream = File.OpenRead(observationPath);
            StopObservation? observation = await JsonSerializer.DeserializeAsync(
                stream,
                AppJsonSerializerContext.Default.StopObservation,
                cancellationToken
            );
            return observation is not null
                && string.Equals(observation.SessionId, sessionId, StringComparison.Ordinal)
                && string.Equals(
                    observation.NotificationTurnId,
                    turn.NotificationTurnId,
                    StringComparison.Ordinal
                )
                && string.Equals(observation.StopId, notificationKey, StringComparison.Ordinal)
                && observation.SummaryPendingHandoff;
        }
        catch (Exception ex)
            when (ex
                    is IOException
                        or JsonException
                        or UnauthorizedAccessException
                        or NotSupportedException
            )
        {
            return false;
        }
    }

    private static async Task<NotificationTurn?> SelectSingleObservedPendingCompletedExactTurnAsync(
        string workspacePath,
        string sessionId,
        IReadOnlyList<NotificationTurn> exactStopSummaryTurns,
        IReadOnlyList<NotificationRecord> durableNotificationRecords,
        DateTimeOffset parsedStopTimestamp,
        string stopTimestamp,
        CancellationToken cancellationToken
    )
    {
        NotificationTurn? observedTurn = null;
        foreach (NotificationTurn turn in exactStopSummaryTurns)
        {
            if (
                HasInterveningSessionDelivery(turn, durableNotificationRecords, parsedStopTimestamp)
                || !await HasPendingStopObservationForTurnAsync(
                    workspacePath,
                    sessionId,
                    turn,
                    stopTimestamp,
                    cancellationToken
                )
            )
            {
                continue;
            }

            if (observedTurn is not null)
            {
                return null;
            }

            observedTurn = turn;
        }

        return observedTurn;
    }

    private static bool HasUnresolvedInterveningSubagentObservation(
        NotificationTurn turn,
        IReadOnlyList<PromptObservation> promptObservations,
        IReadOnlyList<NotificationRecord> durableNotificationRecords,
        DateTimeOffset stopTimestamp
    )
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
                durableNotificationRecords
            )
        );
    }

    private static bool IsExplicitObservationOnlySubagentObservation(
        PromptObservation observation
    ) =>
        string.Equals(observation.Classification, "observation-only", StringComparison.Ordinal)
        && !string.IsNullOrWhiteSpace(observation.Prompt)
        && HasExplicitSubagentMarker(observation.Prompt.TrimStart());

    private static bool WasObservationAlreadyHandledByEarlierSessionStop(
        DateTimeOffset observedAt,
        DateTimeOffset currentStopTimestamp,
        IReadOnlyList<NotificationRecord> durableNotificationRecords
    ) =>
        durableNotificationRecords.Any(record =>
            TryParseUtcTimestamp(record.StopTimestamp, out DateTimeOffset previousStopTimestamp)
            && previousStopTimestamp >= observedAt
            && previousStopTimestamp < currentStopTimestamp
        );

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
            ]
        );
    }

    private static string BuildNotificationAssignmentContext(
        string workspacePath,
        NotificationTurn turn
    )
    {
        string summaryPath = AppPaths.GetSummaryStatePath(
            workspacePath,
            turn.SessionId,
            turn.NotificationTurnId
        );
        string relativeSummaryPath = AppPaths.GetRelativeSummaryStatePath(
            turn.SessionId,
            turn.NotificationTurnId
        );

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
                "summary must be a non-empty concise human-readable sentence;",
                "write summary in Chinese when practical,",
                "but a usable non-Chinese summary is allowed.",
                "details, changed_files, and next_steps must be JSON arrays.",
                "Do not write legacy singleton notification files.",
            ]
        );
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
        CancellationToken cancellationToken
    )
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
                $"{notificationKey}.json"
            );
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
                    cancellationToken
                );
                if (
                    record is not null
                    && string.Equals(record.SessionId, sessionId, StringComparison.Ordinal)
                    && string.Equals(
                        record.NotificationKey,
                        notificationKey,
                        StringComparison.Ordinal
                    )
                )
                {
                    return true;
                }
            }
            catch (Exception ex)
                when (ex
                        is IOException
                            or JsonException
                            or UnauthorizedAccessException
                            or NotSupportedException
                )
            {
                continue;
            }
        }

        return false;
    }

    private static async Task<
        IReadOnlyList<NotificationRecord>
    > ListPerTurnNotificationRecordsAsync(
        string workspacePath,
        string sessionId,
        CancellationToken cancellationToken
    )
    {
        string turnsDirectory = AppPaths.GetTurnsDirectoryPath(workspacePath, sessionId);
        if (!Directory.Exists(turnsDirectory))
        {
            return [];
        }

        List<NotificationRecord> records = [];
        foreach (
            string notificationsDirectory in Directory
                .EnumerateDirectories(turnsDirectory)
                .Select(static turnDirectory =>
                    Path.Combine(turnDirectory, AppConstants.NotificationsRecordsDirectoryName)
                )
                .Where(Directory.Exists)
        )
        {
            foreach (
                string notificationFile in Directory.EnumerateFiles(
                    notificationsDirectory,
                    "*.json",
                    SearchOption.TopDirectoryOnly
                )
            )
            {
                try
                {
                    await using FileStream stream = File.OpenRead(notificationFile);
                    NotificationRecord? record = await JsonSerializer.DeserializeAsync(
                        stream,
                        AppJsonSerializerContext.Default.NotificationRecord,
                        cancellationToken
                    );
                    if (
                        record is not null
                        && string.Equals(record.SessionId, sessionId, StringComparison.Ordinal)
                    )
                    {
                        records.Add(record);
                    }
                }
                catch (Exception ex)
                    when (ex
                            is IOException
                                or JsonException
                                or UnauthorizedAccessException
                                or NotSupportedException
                    )
                {
                    continue;
                }
            }
        }

        return records
            .OrderBy(static record => record.StopTimestamp, StringComparer.Ordinal)
            .ToArray();
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

    private static string? GetWorkspacePathOrNull(string? cwd) =>
        string.IsNullOrWhiteSpace(cwd) ? null : Path.GetFullPath(cwd);

    private static string BuildInvalidInputReason<T>(
        T? hookInput,
        ReadOnlyMemory<byte> payload,
        params (string FieldName, bool IsMissing)[] fieldChecks
    )
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

        string reason =
            missingFields.Length == 0
                ? "payload could not be processed."
                : $"missing required field(s): {string.Join(", ", missingFields)}.";

        return payloadShape is null ? reason : $"{reason} {payloadShape}";
    }

    private static T? DeserializePayload<T>(
        ReadOnlyMemory<byte> payload,
        JsonTypeInfo<T> jsonTypeInfo
    )
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
        CancellationToken cancellationToken
    )
    {
        using MemoryStream buffer = new();
        await standardInput.CopyToAsync(buffer, cancellationToken);
        return buffer.ToArray();
    }

    private static async Task<SummaryValidationResult> ValidateSummaryWithRetryAsync(
        string workspacePath,
        string sessionId,
        NotificationTurn turn,
        CancellationToken cancellationToken
    )
    {
        SummaryValidationResult result = SummaryValidationResult.Invalid("Summary was not read.");
        for (int attempt = 0; attempt < AppConstants.SummaryReadRetryCount; attempt++)
        {
            result = await ValidateSummaryOnceAsync(
                workspacePath,
                sessionId,
                turn,
                cancellationToken
            );
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
        CancellationToken cancellationToken
    )
    {
        string summaryPath = AppPaths.GetSummaryStatePath(
            workspacePath,
            sessionId,
            turn.NotificationTurnId
        );
        string summaryDisplayPath = AppPaths.GetRelativeSummaryStatePath(
            sessionId,
            turn.NotificationTurnId
        );
        if (!File.Exists(summaryPath))
        {
            return SummaryValidationResult.Pending(
                $"Summary file is missing at '{summaryDisplayPath}'."
            );
        }

        NotificationSummary? summary;
        try
        {
            await using FileStream stream = File.Open(
                summaryPath,
                FileMode.Open,
                FileAccess.Read,
                FileShare.ReadWrite
            );
            summary = await JsonSerializer.DeserializeAsync(
                stream,
                AppJsonSerializerContext.Default.NotificationSummary,
                cancellationToken
            );
        }
        catch (Exception ex)
            when (ex
                    is IOException
                        or JsonException
                        or UnauthorizedAccessException
                        or NotSupportedException
            )
        {
            return SummaryValidationResult.Pending(
                $"Summary file '{summaryDisplayPath}' could not be parsed as JSON: {ex.Message}"
            );
        }

        if (summary is null)
        {
            return SummaryValidationResult.Pending(
                $"Summary file '{summaryDisplayPath}' is empty or does not contain a JSON object."
            );
        }

        List<string> failures = [];
        if (!string.Equals(summary.SessionId, turn.SessionId, StringComparison.Ordinal))
        {
            failures.Add($"session_id must equal '{turn.SessionId}'");
        }

        if (
            !string.Equals(
                summary.NotificationTurnId,
                turn.NotificationTurnId,
                StringComparison.Ordinal
            )
        )
        {
            failures.Add($"notification_turn_id must equal '{turn.NotificationTurnId}'");
        }

        if (
            !string.Equals(
                summary.NotificationNonce,
                turn.NotificationNonce,
                StringComparison.Ordinal
            )
        )
        {
            failures.Add("notification_nonce must equal the assigned nonce");
        }

        if (!IsValidUtcTimestamp(summary.UpdatedAt))
        {
            failures.Add("updated_at must be a UTC timestamp in yyyy-MM-ddTHH:mm:ss.fffZ format");
        }

        if (string.IsNullOrWhiteSpace(summary.Summary))
        {
            failures.Add("summary must be a non-empty human-readable sentence");
        }

        bool assignedToTurn =
            string.Equals(summary.SessionId, turn.SessionId, StringComparison.Ordinal)
            && string.Equals(
                summary.NotificationTurnId,
                turn.NotificationTurnId,
                StringComparison.Ordinal
            )
            && string.Equals(
                summary.NotificationNonce,
                turn.NotificationNonce,
                StringComparison.Ordinal
            );
        if (
            assignedToTurn
            && IsValidUtcTimestamp(summary.UpdatedAt)
            && string.IsNullOrWhiteSpace(summary.Summary)
            && string.Equals(summary.Status, "pending", StringComparison.Ordinal)
        )
        {
            return SummaryValidationResult.Pending(
                $"Summary file '{summaryDisplayPath}' is pending: {string.Join("; ", failures)}.",
                summary
            );
        }

        if (failures.Count > 0)
        {
            return SummaryValidationResult.Invalid(
                $"Summary file '{summaryDisplayPath}' is invalid: {string.Join("; ", failures)}."
            );
        }

        return SummaryValidationResult.Valid(summary);
    }

    private static bool IsValidUtcTimestamp(string? value) =>
        TryParseUtcTimestamp(value, out DateTimeOffset parsed)
        && string.Equals(
            parsed.ToString(UtcTimestampFormat, CultureInfo.InvariantCulture),
            value,
            StringComparison.Ordinal
        );

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
            out parsed
        );
    }

    private async Task WriteAdditionalContextResponseAsync(
        Stream standardOutput,
        string hookEventName,
        string additionalContext,
        CancellationToken cancellationToken
    ) =>
        await GetHookOutputAdapter()
            .WriteAdditionalContextResponseAsync(
                standardOutput,
                hookEventName,
                additionalContext,
                cancellationToken
            );

    private async Task WriteUserPromptSubmitResponseAsync(
        Stream standardOutput,
        string prompt,
        string additionalContext,
        CancellationToken cancellationToken
    ) =>
        await GetHookOutputAdapter()
            .WriteUserPromptSubmitResponseAsync(
                standardOutput,
                prompt,
                additionalContext,
                cancellationToken
            );

    private HookOutputAdapter GetHookOutputAdapter() =>
        hookExecutionContext.GetSurface() switch
        {
            HookSurface.CopilotCli => CopilotCliAdapter,
            HookSurface.VsCode => VsCodeAdapter,
            _ => throw new InvalidOperationException(
                $"Unsupported hook surface '{hookExecutionContext.GetSurface()}'."
            ),
        };

    private static async Task WriteHookSpecificOutputResponseAsync(
        Stream standardOutput,
        string hookEventName,
        string additionalContext,
        CancellationToken cancellationToken
    )
    {
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
            cancellationToken
        );
    }

    private static string BuildCopilotCliUserPromptSubmitModifiedPrompt(
        string prompt,
        string additionalContext
    ) => $"{prompt}\n\n<system_reminder>\n{additionalContext}\n</system_reminder>";

    private static async Task WriteVsCodeHookResponseAsync(
        Stream standardOutput,
        HookResponse response,
        CancellationToken cancellationToken
    )
    {
        await JsonSerializer.SerializeAsync(
            standardOutput,
            response,
            AppJsonSerializerContext.Default.HookResponse,
            cancellationToken
        );
    }

    private static async Task WriteCopilotCliHookOutputAsync(
        Stream standardOutput,
        CopilotCliHookOutput output,
        CancellationToken cancellationToken
    )
    {
        await JsonSerializer.SerializeAsync(
            standardOutput,
            output,
            AppJsonSerializerContext.Default.CopilotCliHookOutput,
            cancellationToken
        );
    }

    private abstract class HookOutputAdapter
    {
        public abstract Task WriteAdditionalContextResponseAsync(
            Stream standardOutput,
            string hookEventName,
            string additionalContext,
            CancellationToken cancellationToken
        );

        public abstract Task WriteUserPromptSubmitResponseAsync(
            Stream standardOutput,
            string prompt,
            string additionalContext,
            CancellationToken cancellationToken
        );
    }

    private sealed class HookSpecificOutputAdapter : HookOutputAdapter
    {
        public override Task WriteAdditionalContextResponseAsync(
            Stream standardOutput,
            string hookEventName,
            string additionalContext,
            CancellationToken cancellationToken
        ) =>
            WriteHookSpecificOutputResponseAsync(
                standardOutput,
                hookEventName,
                additionalContext,
                cancellationToken
            );

        public override Task WriteUserPromptSubmitResponseAsync(
            Stream standardOutput,
            string prompt,
            string additionalContext,
            CancellationToken cancellationToken
        ) =>
            WriteHookSpecificOutputResponseAsync(
                standardOutput,
                "UserPromptSubmit",
                additionalContext,
                cancellationToken
            );
    }

    private sealed class CopilotCliOutputAdapter : HookOutputAdapter
    {
        public override Task WriteAdditionalContextResponseAsync(
            Stream standardOutput,
            string hookEventName,
            string additionalContext,
            CancellationToken cancellationToken
        ) =>
            WriteCopilotCliHookOutputAsync(
                standardOutput,
                new CopilotCliHookOutput { AdditionalContext = additionalContext },
                cancellationToken
            );

        public override Task WriteUserPromptSubmitResponseAsync(
            Stream standardOutput,
            string prompt,
            string additionalContext,
            CancellationToken cancellationToken
        ) =>
            WriteCopilotCliHookOutputAsync(
                standardOutput,
                new CopilotCliHookOutput
                {
                    ModifiedPrompt = BuildCopilotCliUserPromptSubmitModifiedPrompt(
                        prompt,
                        additionalContext
                    ),
                },
                cancellationToken
            );
    }

    private sealed record SummaryValidationResult(
        bool IsValid,
        bool IsPendingHandoff,
        NotificationSummary? Record,
        string? FailureReason
    )
    {
        public static SummaryValidationResult Valid(NotificationSummary record) =>
            new(true, false, record, null);

        public static SummaryValidationResult Invalid(string failureReason) =>
            new(false, false, null, failureReason);

        public static SummaryValidationResult Pending(string failureReason) =>
            new(false, true, null, failureReason);

        public static SummaryValidationResult Pending(
            string failureReason,
            NotificationSummary record
        ) => new(false, true, record, failureReason);
    }

    private sealed record StopResolution(
        NotificationTurn? Turn,
        string Reason,
        bool SuppressFallback = false
    );

    private sealed record RecoverableAbandonedTurnsResult(
        IReadOnlyList<NotificationTurn> Turns,
        bool SuppressStop
    )
    {
        public static RecoverableAbandonedTurnsResult Empty { get; } = new([], SuppressStop: false);
    }

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

            string[] propertyNames = document
                .RootElement.EnumerateObject()
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
