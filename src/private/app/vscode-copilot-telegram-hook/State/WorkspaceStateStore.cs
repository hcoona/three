using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization.Metadata;
using Hcoona.VsCodeCopilotTelegramHook.Logging;
using Microsoft.Extensions.Logging;

namespace Hcoona.VsCodeCopilotTelegramHook.State;

internal sealed class WorkspaceStateStore(
    TimeProvider timeProvider,
    ILogger<WorkspaceStateStore> logger
)
{
    private const UnixFileMode OwnerOnlyDirectoryMode =
        UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute;

    private const UnixFileMode OwnerOnlyFileMode = UnixFileMode.UserRead | UnixFileMode.UserWrite;

    public Func<
        NotificationTurn,
        CancellationToken,
        Task
    >? OnBeforeAbandonOpenTurnForTestingAsync { get; set; }

    public Func<
        NotificationTurn,
        NotificationTurn,
        CancellationToken,
        Task
    >? OnBeforeAbandonSupersededTurnForTestingAsync { get; set; }

    public Func<
        NotificationTurn,
        NotificationTurn,
        CancellationToken,
        Task
    >? OnAfterAbandonSupersededTurnFinalGuardForTestingAsync { get; set; }

    public Func<
        NotificationTurn,
        CancellationToken,
        Task
    >? OnSupersedingOpenTurnResolvedForTestingAsync { get; set; }

    public async Task<NotificationSession> InitializeSessionAsync(
        SessionStartHookInput input,
        CancellationToken cancellationToken
    )
    {
        string workspacePath = Path.GetFullPath(input.Cwd);
        string now = GetCurrentUtcTimestamp();
        AppLog.InitializingSessionState(logger, input.SessionId, workspacePath);
        return await EnsureSessionAsync(
            workspacePath,
            input.SessionId,
            input.TranscriptPath,
            now,
            cancellationToken
        );
    }

    public async Task<PromptObservation> RecordPromptObservationAsync(
        UserPromptSubmitHookInput input,
        PromptClassification classification,
        CancellationToken cancellationToken
    )
    {
        string workspacePath = Path.GetFullPath(input.Cwd);
        string now = GetCurrentUtcTimestamp();
        _ = await EnsureSessionAsync(
            workspacePath,
            input.SessionId,
            input.TranscriptPath,
            now,
            cancellationToken
        );

        PromptObservation observation = new()
        {
            SessionId = input.SessionId,
            PromptObservationId = CreateId("prompt"),
            WorkspacePath = workspacePath,
            ObservedAt = string.IsNullOrWhiteSpace(input.Timestamp) ? now : input.Timestamp,
            HookEventName = input.HookEventName,
            Prompt = input.Prompt,
            Classification = classification.Kind,
            ClassificationReason = classification.Reason,
            TranscriptPath = input.TranscriptPath,
        };

        await WriteJsonAsync(
            AppPaths.GetPromptObservationPath(
                workspacePath,
                input.SessionId,
                observation.PromptObservationId
            ),
            observation,
            AppJsonSerializerContext.Default.PromptObservation,
            cancellationToken
        );
        AppLog.RecordedPromptObservation(
            logger,
            observation.PromptObservationId,
            input.SessionId,
            classification.Kind,
            classification.Reason
        );
        return observation;
    }

    public async Task<NotificationTurn> CreateNotificationTurnAsync(
        UserPromptSubmitHookInput input,
        PromptObservation observation,
        CancellationToken cancellationToken
    )
    {
        string workspacePath = Path.GetFullPath(input.Cwd);
        string now = GetCurrentUtcTimestamp();
        string createdAt = string.IsNullOrWhiteSpace(input.Timestamp) ? now : input.Timestamp;
        AppLog.StartingTurnState(logger, input.SessionId, workspacePath);

        NotificationTurn turn = new()
        {
            SessionId = input.SessionId,
            NotificationTurnId = CreateId("turn"),
            NotificationNonce = Guid.NewGuid().ToString("n"),
            PromptObservationId = observation.PromptObservationId,
            WorkspacePath = workspacePath,
            CreatedAt = createdAt,
            UpdatedAt = now,
            Status = "open",
            SummaryPlaceholderCreatedAt = now,
            TranscriptPath = input.TranscriptPath,
        };

        NotificationSummary placeholderSummary = new()
        {
            SessionId = turn.SessionId,
            NotificationTurnId = turn.NotificationTurnId,
            NotificationNonce = turn.NotificationNonce,
            UpdatedAt = now,
            PlaceholderCreatedAt = now,
            Status = "pending",
            Details = [],
            ChangedFiles = [],
            NextSteps = [],
        };

        string summaryPath = AppPaths.GetSummaryStatePath(
            workspacePath,
            input.SessionId,
            turn.NotificationTurnId
        );
        CurrentNotificationState current = new()
        {
            SessionId = turn.SessionId,
            NotificationTurnId = turn.NotificationTurnId,
            NotificationNonce = turn.NotificationNonce,
            SummaryPath = summaryPath,
            UpdatedAt = now,
        };
        NotificationTurn? existingCurrentTurn = null;
        CurrentNotificationState? existingCurrent = await TryReadCurrentAsync(
            workspacePath,
            input.SessionId,
            cancellationToken
        );
        if (existingCurrent is not null)
        {
            existingCurrentTurn = await TryReadTurnAsync(
                workspacePath,
                input.SessionId,
                existingCurrent.NotificationTurnId,
                cancellationToken
            );
        }

        await WriteJsonAsync(
            AppPaths.GetTurnStatePath(workspacePath, input.SessionId, turn.NotificationTurnId),
            turn,
            AppJsonSerializerContext.Default.NotificationTurn,
            cancellationToken
        );
        await WriteJsonAsync(
            summaryPath,
            placeholderSummary,
            AppJsonSerializerContext.Default.NotificationSummary,
            cancellationToken
        );
        if (
            existingCurrentTurn is null
            || !string.Equals(existingCurrentTurn.Status, "open", StringComparison.Ordinal)
            || string.CompareOrdinal(existingCurrentTurn.CreatedAt, turn.CreatedAt) < 0
        )
        {
            await WriteJsonAsync(
                AppPaths.GetCurrentStatePath(workspacePath, input.SessionId),
                current,
                AppJsonSerializerContext.Default.CurrentNotificationState,
                cancellationToken
            );
            await AbandonSupersededOpenTurnsAsync(
                workspacePath,
                input.SessionId,
                now,
                cancellationToken
            );
        }

        AppLog.CreatedTurnState(logger, turn.NotificationTurnId, turn.SessionId);
        return turn;
    }

    public Task<NotificationSession?> TryReadSessionAsync(
        string workspacePath,
        string sessionId,
        CancellationToken cancellationToken
    ) =>
        ReadJsonAsync(
            AppPaths.GetSessionStatePath(Path.GetFullPath(workspacePath), sessionId),
            "notification session",
            AppJsonSerializerContext.Default.NotificationSession,
            cancellationToken
        );

    public Task<CurrentNotificationState?> TryReadCurrentAsync(
        string workspacePath,
        string sessionId,
        CancellationToken cancellationToken
    ) =>
        ReadJsonAsync(
            AppPaths.GetCurrentStatePath(Path.GetFullPath(workspacePath), sessionId),
            "current notification cache",
            AppJsonSerializerContext.Default.CurrentNotificationState,
            cancellationToken
        );

    public Task<NotificationTurn?> TryReadTurnAsync(
        string workspacePath,
        string sessionId,
        string notificationTurnId,
        CancellationToken cancellationToken
    ) =>
        ReadJsonAsync(
            AppPaths.GetTurnStatePath(
                Path.GetFullPath(workspacePath),
                sessionId,
                notificationTurnId
            ),
            "notification turn",
            AppJsonSerializerContext.Default.NotificationTurn,
            cancellationToken
        );

    public Task<NotificationSummary?> TryReadSummaryAsync(
        string workspacePath,
        string sessionId,
        string notificationTurnId,
        CancellationToken cancellationToken
    ) =>
        ReadJsonAsync(
            AppPaths.GetSummaryStatePath(
                Path.GetFullPath(workspacePath),
                sessionId,
                notificationTurnId
            ),
            "notification summary",
            AppJsonSerializerContext.Default.NotificationSummary,
            cancellationToken
        );

    public async Task<IReadOnlyList<NotificationTurn>> ListOpenTurnsAsync(
        string workspacePath,
        string sessionId,
        CancellationToken cancellationToken
    )
    {
        string turnsDirectory = AppPaths.GetTurnsDirectoryPath(
            Path.GetFullPath(workspacePath),
            sessionId
        );
        if (!Directory.Exists(turnsDirectory))
        {
            return [];
        }

        List<NotificationTurn> turns = [];
        foreach (
            string turnFile in Directory.EnumerateFiles(
                turnsDirectory,
                AppConstants.TurnFileName,
                SearchOption.AllDirectories
            )
        )
        {
            NotificationTurn? turn = await ReadJsonAsync(
                turnFile,
                "notification turn",
                AppJsonSerializerContext.Default.NotificationTurn,
                cancellationToken
            );
            if (
                turn is not null
                && string.Equals(turn.SessionId, sessionId, StringComparison.Ordinal)
                && string.Equals(turn.Status, "open", StringComparison.Ordinal)
                && !await HasFreshDeliveryClaimAsync(
                    Path.GetFullPath(workspacePath),
                    sessionId,
                    turn.NotificationTurnId,
                    cancellationToken
                )
                && !await HasDurableDeliveryRecordAsync(
                    Path.GetFullPath(workspacePath),
                    sessionId,
                    turn.NotificationTurnId,
                    cancellationToken
                )
            )
            {
                turns.Add(turn);
            }
        }

        return turns.OrderBy(static turn => turn.CreatedAt, StringComparer.Ordinal).ToArray();
    }

    public async Task<IReadOnlyList<NotificationTurn>> ListAbandonedTurnsAsync(
        string workspacePath,
        string sessionId,
        CancellationToken cancellationToken
    ) =>
        await ListTurnsWithStatusAsync(
            workspacePath,
            sessionId,
            "abandoned",
            requireNoDurableDelivery: true,
            requireFreshDeliveryClaim: false,
            cancellationToken
        );

    public async Task<IReadOnlyList<NotificationTurn>> ListNotifiedTurnsAsync(
        string workspacePath,
        string sessionId,
        CancellationToken cancellationToken
    ) =>
        await ListTurnsWithStatusAsync(
            workspacePath,
            sessionId,
            "notified",
            requireNoDurableDelivery: false,
            requireFreshDeliveryClaim: false,
            cancellationToken
        );

    public async Task<IReadOnlyList<NotificationTurn>> ListFreshDeliveryClaimedOpenTurnsAsync(
        string workspacePath,
        string sessionId,
        CancellationToken cancellationToken
    ) =>
        await ListTurnsWithStatusAsync(
            workspacePath,
            sessionId,
            "open",
            requireNoDurableDelivery: true,
            requireFreshDeliveryClaim: true,
            cancellationToken
        );

    public async Task<IReadOnlyList<PromptObservation>> ListPromptObservationsAsync(
        string workspacePath,
        string sessionId,
        CancellationToken cancellationToken
    )
    {
        string promptsDirectory = Path.Combine(
            AppPaths.GetSessionDirectoryPath(Path.GetFullPath(workspacePath), sessionId),
            AppConstants.PromptsDirectoryName
        );
        if (!Directory.Exists(promptsDirectory))
        {
            return [];
        }

        List<PromptObservation> observations = [];
        foreach (
            string promptFile in Directory.EnumerateFiles(
                promptsDirectory,
                "*.json",
                SearchOption.TopDirectoryOnly
            )
        )
        {
            PromptObservation? observation = await ReadJsonAsync(
                promptFile,
                "prompt observation",
                AppJsonSerializerContext.Default.PromptObservation,
                cancellationToken
            );
            if (
                observation is not null
                && string.Equals(observation.SessionId, sessionId, StringComparison.Ordinal)
            )
            {
                observations.Add(observation);
            }
        }

        return observations
            .OrderBy(static observation => observation.ObservedAt, StringComparer.Ordinal)
            .ToArray();
    }

    public async Task<IReadOnlyList<NotificationRecord>> ListSessionNotificationRecordsAsync(
        string workspacePath,
        string sessionId,
        CancellationToken cancellationToken
    )
    {
        string notificationsDirectory = Path.Combine(
            AppPaths.GetSessionDirectoryPath(Path.GetFullPath(workspacePath), sessionId),
            AppConstants.NotificationsRecordsDirectoryName
        );
        if (!Directory.Exists(notificationsDirectory))
        {
            return [];
        }

        List<NotificationRecord> records = [];
        foreach (
            string notificationFile in Directory.EnumerateFiles(
                notificationsDirectory,
                "*.json",
                SearchOption.TopDirectoryOnly
            )
        )
        {
            NotificationRecord? record = await ReadJsonAsync(
                notificationFile,
                "session notification record",
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

        return records
            .OrderBy(static record => record.StopTimestamp, StringComparer.Ordinal)
            .ToArray();
    }

    public static Task<bool> WasNotificationAlreadySentAsync(
        string path,
        CancellationToken cancellationToken
    ) => Task.FromResult(File.Exists(path));

    public static async Task<bool> TryClaimStopNotificationAsync(
        string path,
        string claimedAt,
        CancellationToken cancellationToken
    )
    {
        cancellationToken.ThrowIfCancellationRequested();
        EnsureOwnerOnlyParentDirectory(path);

        FileStream stream;
        try
        {
            stream = OpenClaimFile(path);
        }
        catch (IOException) when (File.Exists(path))
        {
            return false;
        }

        try
        {
            await using (stream)
            {
                await stream.WriteAsync(
                    System.Text.Encoding.UTF8.GetBytes(claimedAt),
                    cancellationToken
                );
                await stream.FlushAsync(cancellationToken);
            }

            return true;
        }
        catch
        {
            ReleaseStopNotificationClaim(path);
            throw;
        }
    }

    public static void ReleaseStopNotificationClaim(string path)
    {
        try
        {
            File.Delete(path);
        }
        catch (Exception ex)
            when (ex is IOException or UnauthorizedAccessException or DirectoryNotFoundException)
        { }
    }

    public static async Task<bool> TryReclaimStaleClaimAsync(
        string path,
        string reclaimPath,
        string claimedAt,
        TimeSpan staleAfter,
        Func<Task<bool>> hasDurableDeliveryRecordAsync,
        CancellationToken cancellationToken
    )
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!await IsClaimStaleAsync(path, claimedAt, staleAfter, cancellationToken))
        {
            return false;
        }

        if (await hasDurableDeliveryRecordAsync())
        {
            return false;
        }

        FileStream? staleReclaimLock = null;
        bool claimedReclaim = await TryClaimStopNotificationAsync(
            reclaimPath,
            claimedAt,
            cancellationToken
        );
        if (!claimedReclaim)
        {
            staleReclaimLock = await TryAcquireStaleReclaimLockAsync(
                reclaimPath,
                claimedAt,
                staleAfter,
                cancellationToken
            );
            claimedReclaim = staleReclaimLock is not null;
        }

        if (!claimedReclaim)
        {
            return false;
        }

        try
        {
            if (!await IsClaimStaleAsync(path, claimedAt, staleAfter, cancellationToken))
            {
                return false;
            }

            if (await hasDurableDeliveryRecordAsync())
            {
                return false;
            }

            ReleaseStopNotificationClaim(path);
            return await TryClaimStopNotificationAsync(path, claimedAt, cancellationToken);
        }
        finally
        {
            staleReclaimLock?.Dispose();
            ReleaseStopNotificationClaim(reclaimPath);
        }
    }

    public static async Task RecordStopObservationAsync(
        string workspacePath,
        NotificationTurn turn,
        StopObservation observation,
        CancellationToken cancellationToken
    )
    {
        await WriteJsonAsync(
            AppPaths.GetStopObservationPath(
                Path.GetFullPath(workspacePath),
                turn.SessionId,
                turn.NotificationTurnId,
                observation.StopId
            ),
            observation,
            AppJsonSerializerContext.Default.StopObservation,
            cancellationToken
        );
    }

    public static async Task RecordNotificationAsync(
        string path,
        NotificationRecord record,
        CancellationToken cancellationToken
    )
    {
        await WriteJsonAsync(
            path,
            record,
            AppJsonSerializerContext.Default.NotificationRecord,
            cancellationToken
        );
    }

    public static async Task MarkTurnNotifiedAsync(
        string workspacePath,
        NotificationTurn turn,
        string now,
        CancellationToken cancellationToken
    )
    {
        NotificationTurn? existingTurn = await ReadJsonFileAsync(
            AppPaths.GetTurnStatePath(
                Path.GetFullPath(workspacePath),
                turn.SessionId,
                turn.NotificationTurnId
            ),
            AppJsonSerializerContext.Default.NotificationTurn,
            cancellationToken
        );
        if (string.Equals(existingTurn?.Status, "abandoned", StringComparison.Ordinal))
        {
            return;
        }

        turn.Status = "notified";
        turn.UpdatedAt = now;
        await WriteJsonAsync(
            AppPaths.GetTurnStatePath(
                Path.GetFullPath(workspacePath),
                turn.SessionId,
                turn.NotificationTurnId
            ),
            turn,
            AppJsonSerializerContext.Default.NotificationTurn,
            cancellationToken
        );
    }

    public async Task AbandonSupersededOpenTurnsAsync(
        string workspacePath,
        string sessionId,
        string now,
        CancellationToken cancellationToken
    )
    {
        workspacePath = Path.GetFullPath(workspacePath);
        NotificationTurn? supersedingTurn = await ResolveSupersedingOpenTurnAsync(
            workspacePath,
            sessionId,
            excludedTurnId: null,
            cancellationToken
        );
        if (supersedingTurn is null)
        {
            return;
        }

        if (OnSupersedingOpenTurnResolvedForTestingAsync is not null)
        {
            await OnSupersedingOpenTurnResolvedForTestingAsync(supersedingTurn, cancellationToken);
        }

        if (
            await HasDurableDeliveryRecordAsync(
                workspacePath,
                sessionId,
                supersedingTurn.NotificationTurnId,
                cancellationToken
            )
        )
        {
            return;
        }

        IReadOnlyList<NotificationTurn> openTurns = await ListOpenTurnsAsync(
            workspacePath,
            sessionId,
            cancellationToken
        );
        HashSet<string> pendingHandoffAmbiguousTiedTurnIds =
            await GetPendingHandoffAmbiguousTiedTurnIdsAsync(
                workspacePath,
                openTurns,
                cancellationToken
            );
        foreach (NotificationTurn turn in openTurns)
        {
            if (
                string.Equals(
                    turn.NotificationTurnId,
                    supersedingTurn.NotificationTurnId,
                    StringComparison.Ordinal
                )
            )
            {
                continue;
            }

            await TryAbandonSupersededTurnAsync(
                workspacePath,
                turn,
                supersedingTurn,
                now,
                pendingHandoffAmbiguousTiedTurnIds,
                cancellationToken
            );
        }
    }

    public async Task MarkTurnAbandonedIfSupersededAsync(
        string workspacePath,
        NotificationTurn turn,
        string now,
        CancellationToken cancellationToken
    )
    {
        workspacePath = Path.GetFullPath(workspacePath);
        NotificationTurn? supersedingTurn = await ResolveSupersedingOpenTurnAsync(
            workspacePath,
            turn.SessionId,
            turn.NotificationTurnId,
            cancellationToken
        );
        if (supersedingTurn is null)
        {
            return;
        }

        if (OnSupersedingOpenTurnResolvedForTestingAsync is not null)
        {
            await OnSupersedingOpenTurnResolvedForTestingAsync(supersedingTurn, cancellationToken);
        }

        HashSet<string> pendingHandoffAmbiguousTiedTurnIds =
            await GetPendingHandoffAmbiguousTiedTurnIdsAsync(
                workspacePath,
                await ListOpenTurnsAsync(workspacePath, turn.SessionId, cancellationToken),
                cancellationToken
            );
        await TryAbandonSupersededTurnAsync(
            workspacePath,
            turn,
            supersedingTurn,
            now,
            pendingHandoffAmbiguousTiedTurnIds,
            cancellationToken
        );
    }

    public string GetCurrentUtcTimestamp()
    {
        return timeProvider
            .GetUtcNow()
            .UtcDateTime.ToString("yyyy-MM-ddTHH:mm:ss.fff'Z'", CultureInfo.InvariantCulture);
    }

    private async Task<NotificationSession> EnsureSessionAsync(
        string workspacePath,
        string sessionId,
        string? transcriptPath,
        string now,
        CancellationToken cancellationToken
    )
    {
        NotificationSession session =
            await TryReadSessionAsync(workspacePath, sessionId, cancellationToken)
            ?? new NotificationSession
            {
                SessionId = sessionId,
                WorkspacePath = workspacePath,
                CreatedAt = now,
            };

        session.WorkspacePath = workspacePath;
        session.UpdatedAt = now;
        if (!string.IsNullOrWhiteSpace(transcriptPath))
        {
            session.TranscriptPath = transcriptPath;
        }

        string sessionStatePath = AppPaths.GetSessionStatePath(workspacePath, sessionId);
        await WriteJsonAsync(
            sessionStatePath,
            session,
            AppJsonSerializerContext.Default.NotificationSession,
            cancellationToken
        );
        AppLog.WroteSessionState(logger, sessionId, sessionStatePath);
        return session;
    }

    private async Task<IReadOnlyList<NotificationTurn>> ListTurnsWithStatusAsync(
        string workspacePath,
        string sessionId,
        string status,
        bool requireNoDurableDelivery,
        bool requireFreshDeliveryClaim,
        CancellationToken cancellationToken
    )
    {
        workspacePath = Path.GetFullPath(workspacePath);
        string turnsDirectory = AppPaths.GetTurnsDirectoryPath(workspacePath, sessionId);
        if (!Directory.Exists(turnsDirectory))
        {
            return [];
        }

        List<NotificationTurn> turns = [];
        foreach (
            string turnFile in Directory.EnumerateFiles(
                turnsDirectory,
                AppConstants.TurnFileName,
                SearchOption.AllDirectories
            )
        )
        {
            NotificationTurn? turn = await ReadJsonAsync(
                turnFile,
                "notification turn",
                AppJsonSerializerContext.Default.NotificationTurn,
                cancellationToken
            );
            if (
                turn is null
                || !string.Equals(turn.SessionId, sessionId, StringComparison.Ordinal)
                || !string.Equals(turn.Status, status, StringComparison.Ordinal)
            )
            {
                continue;
            }

            if (
                requireNoDurableDelivery
                && await HasDurableDeliveryRecordAsync(
                    workspacePath,
                    sessionId,
                    turn.NotificationTurnId,
                    cancellationToken
                )
            )
            {
                continue;
            }

            if (
                requireFreshDeliveryClaim
                && !await HasFreshDeliveryClaimAsync(
                    workspacePath,
                    sessionId,
                    turn.NotificationTurnId,
                    cancellationToken
                )
            )
            {
                continue;
            }

            turns.Add(turn);
        }

        return turns.OrderBy(static turn => turn.CreatedAt, StringComparer.Ordinal).ToArray();
    }

    private async Task<NotificationTurn?> ResolveSupersedingOpenTurnAsync(
        string workspacePath,
        string sessionId,
        string? excludedTurnId,
        CancellationToken cancellationToken
    )
    {
        IReadOnlyList<NotificationTurn> openTurns = await ListOpenTurnsAsync(
            workspacePath,
            sessionId,
            cancellationToken
        );
        NotificationTurn[] candidateOpenTurns = openTurns
            .Where(turn =>
                !string.Equals(turn.NotificationTurnId, excludedTurnId, StringComparison.Ordinal)
            )
            .ToArray();
        NotificationTurn? latestOpenTurn = candidateOpenTurns
            .OrderByDescending(static turn => turn.CreatedAt, StringComparer.Ordinal)
            .FirstOrDefault();
        CurrentNotificationState? current = await TryReadCurrentAsync(
            workspacePath,
            sessionId,
            cancellationToken
        );
        if (
            current is not null
            && !string.Equals(current.NotificationTurnId, excludedTurnId, StringComparison.Ordinal)
        )
        {
            NotificationTurn? currentTurn = await TryReadTurnAsync(
                workspacePath,
                sessionId,
                current.NotificationTurnId,
                cancellationToken
            );
            if (
                currentTurn is not null
                && string.Equals(currentTurn.Status, "open", StringComparison.Ordinal)
                && !await HasDurableDeliveryRecordAsync(
                    workspacePath,
                    sessionId,
                    currentTurn.NotificationTurnId,
                    cancellationToken
                )
            )
            {
                NotificationTurn selectedTurn =
                    latestOpenTurn is not null
                    && string.CompareOrdinal(latestOpenTurn.CreatedAt, currentTurn.CreatedAt) > 0
                        ? latestOpenTurn
                        : currentTurn;
                return await HasPendingHandoffAmbiguityAtEqualCreatedAtAsync(
                    workspacePath,
                    candidateOpenTurns,
                    selectedTurn.CreatedAt,
                    cancellationToken
                )
                    ? null
                    : selectedTurn;
            }
        }

        return
            latestOpenTurn is not null
            && await HasPendingHandoffAmbiguityAtEqualCreatedAtAsync(
                workspacePath,
                candidateOpenTurns,
                latestOpenTurn.CreatedAt,
                cancellationToken
            )
            ? null
            : latestOpenTurn;
    }

    private async Task TryAbandonSupersededTurnAsync(
        string workspacePath,
        NotificationTurn turn,
        NotificationTurn supersedingTurn,
        string now,
        HashSet<string> pendingHandoffAmbiguousTiedTurnIds,
        CancellationToken cancellationToken
    )
    {
        if (
            await HasDurableDeliveryRecordAsync(
                workspacePath,
                turn.SessionId,
                turn.NotificationTurnId,
                cancellationToken
            )
            || await HasDurableDeliveryRecordAsync(
                workspacePath,
                supersedingTurn.SessionId,
                supersedingTurn.NotificationTurnId,
                cancellationToken
            )
            || await HasFreshDeliveryClaimAsync(
                workspacePath,
                turn.SessionId,
                turn.NotificationTurnId,
                cancellationToken
            )
            || pendingHandoffAmbiguousTiedTurnIds.Contains(turn.NotificationTurnId)
            || await HasTiedPendingHandoffAmbiguityForTurnAsync(
                workspacePath,
                turn,
                cancellationToken
            )
            || await HasAssignedSummaryWorthPreservingAsync(workspacePath, turn, cancellationToken)
        )
        {
            return;
        }

        if (OnBeforeAbandonOpenTurnForTestingAsync is not null)
        {
            await OnBeforeAbandonOpenTurnForTestingAsync(turn, cancellationToken);
        }

        if (OnBeforeAbandonSupersededTurnForTestingAsync is not null)
        {
            await OnBeforeAbandonSupersededTurnForTestingAsync(
                turn,
                supersedingTurn,
                cancellationToken
            );
        }

        if (
            await HasDurableDeliveryRecordAsync(
                workspacePath,
                turn.SessionId,
                turn.NotificationTurnId,
                cancellationToken
            )
            || await HasDurableDeliveryRecordAsync(
                workspacePath,
                supersedingTurn.SessionId,
                supersedingTurn.NotificationTurnId,
                cancellationToken
            )
            || await HasFreshDeliveryClaimAsync(
                workspacePath,
                turn.SessionId,
                turn.NotificationTurnId,
                cancellationToken
            )
            || pendingHandoffAmbiguousTiedTurnIds.Contains(turn.NotificationTurnId)
            || await HasTiedPendingHandoffAmbiguityForTurnAsync(
                workspacePath,
                turn,
                cancellationToken
            )
            || await HasAssignedSummaryWorthPreservingAsync(workspacePath, turn, cancellationToken)
        )
        {
            return;
        }

        if (OnAfterAbandonSupersededTurnFinalGuardForTestingAsync is not null)
        {
            await OnAfterAbandonSupersededTurnFinalGuardForTestingAsync(
                turn,
                supersedingTurn,
                cancellationToken
            );
        }

        string turnDeliveryClaimPath = AppPaths.GetTurnDeliveryClaimPath(
            workspacePath,
            turn.SessionId,
            turn.NotificationTurnId
        );
        string abandonmentClaimedAt = GetCurrentUtcTimestamp();
        FileStream? abandonmentClaim = await TryAcquireAbandonmentDeliveryClaimAsync(
            turnDeliveryClaimPath,
            abandonmentClaimedAt,
            cancellationToken
        );
        if (abandonmentClaim is null)
        {
            return;
        }

        try
        {
            if (
                await HasDurableDeliveryRecordAsync(
                    workspacePath,
                    turn.SessionId,
                    turn.NotificationTurnId,
                    cancellationToken
                )
                || await HasDurableDeliveryRecordAsync(
                    workspacePath,
                    supersedingTurn.SessionId,
                    supersedingTurn.NotificationTurnId,
                    cancellationToken
                )
                || pendingHandoffAmbiguousTiedTurnIds.Contains(turn.NotificationTurnId)
                || await HasTiedPendingHandoffAmbiguityForTurnAsync(
                    workspacePath,
                    turn,
                    cancellationToken
                )
                || await HasAssignedSummaryWorthPreservingAsync(
                    workspacePath,
                    turn,
                    cancellationToken
                )
            )
            {
                return;
            }

            await StampLegacyHookCreatedPendingPlaceholderAsync(
                workspacePath,
                turn,
                cancellationToken
            );
            turn.Status = "abandoned";
            turn.UpdatedAt = now;
            await WriteTurnAsync(workspacePath, turn, cancellationToken);
        }
        finally
        {
            await abandonmentClaim.DisposeAsync();
            await ReleaseOwnedStopNotificationClaimAsync(
                turnDeliveryClaimPath,
                abandonmentClaimedAt,
                CancellationToken.None
            );
        }
    }

    private static async Task<HashSet<string>> GetPendingHandoffAmbiguousTiedTurnIdsAsync(
        string workspacePath,
        IReadOnlyList<NotificationTurn> openTurns,
        CancellationToken cancellationToken
    )
    {
        HashSet<string> tiedTurnIds = openTurns
            .GroupBy(static turn => turn.CreatedAt, StringComparer.Ordinal)
            .Where(static group => group.Count() > 1)
            .SelectMany(static group => group)
            .Select(static turn => turn.NotificationTurnId)
            .ToHashSet(StringComparer.Ordinal);
        if (tiedTurnIds.Count == 0)
        {
            return [];
        }

        HashSet<string> pendingHandoffAmbiguousTurnIds = new(StringComparer.Ordinal);
        foreach (NotificationTurn turn in openTurns)
        {
            if (
                tiedTurnIds.Contains(turn.NotificationTurnId)
                && await HasPendingHandoffAmbiguityForAbandonmentAsync(
                    workspacePath,
                    turn,
                    includeHookCreatedPlaceholder: true,
                    cancellationToken
                )
            )
            {
                pendingHandoffAmbiguousTurnIds.Add(turn.NotificationTurnId);
            }
        }

        return pendingHandoffAmbiguousTurnIds;
    }

    private static async Task<bool> HasPendingHandoffAmbiguityAtEqualCreatedAtAsync(
        string workspacePath,
        IReadOnlyList<NotificationTurn> openTurns,
        string createdAt,
        CancellationToken cancellationToken
    )
    {
        NotificationTurn[] tiedTurns = openTurns
            .Where(turn => string.Equals(turn.CreatedAt, createdAt, StringComparison.Ordinal))
            .ToArray();
        if (tiedTurns.Length <= 1)
        {
            return false;
        }

        foreach (NotificationTurn tiedTurn in tiedTurns)
        {
            if (
                await HasPendingHandoffAmbiguityForAbandonmentAsync(
                    workspacePath,
                    tiedTurn,
                    includeHookCreatedPlaceholder: true,
                    cancellationToken
                )
            )
            {
                return true;
            }
        }

        return false;
    }

    private async Task<bool> HasTiedPendingHandoffAmbiguityForTurnAsync(
        string workspacePath,
        NotificationTurn turn,
        CancellationToken cancellationToken
    )
    {
        if (
            !await HasPendingHandoffAmbiguityForAbandonmentAsync(
                workspacePath,
                turn,
                includeHookCreatedPlaceholder: false,
                cancellationToken
            )
        )
        {
            return false;
        }

        IReadOnlyList<NotificationTurn> openTurns = await ListOpenTurnsAsync(
            workspacePath,
            turn.SessionId,
            cancellationToken
        );
        return openTurns.Any(candidate =>
            !string.Equals(
                candidate.NotificationTurnId,
                turn.NotificationTurnId,
                StringComparison.Ordinal
            ) && string.Equals(candidate.CreatedAt, turn.CreatedAt, StringComparison.Ordinal)
        );
    }

    private static async Task<bool> HasPendingHandoffAmbiguityForAbandonmentAsync(
        string workspacePath,
        NotificationTurn turn,
        bool includeHookCreatedPlaceholder,
        CancellationToken cancellationToken
    )
    {
        string summaryPath = AppPaths.GetSummaryStatePath(
            workspacePath,
            turn.SessionId,
            turn.NotificationTurnId
        );
        if (!File.Exists(summaryPath))
        {
            return true;
        }

        NotificationSummary? summary = await ReadJsonFileAsync(
            summaryPath,
            AppJsonSerializerContext.Default.NotificationSummary,
            cancellationToken
        );
        if (summary is null)
        {
            return true;
        }

        return string.Equals(summary.Status, "pending", StringComparison.Ordinal)
            && string.IsNullOrWhiteSpace(summary.Summary)
            && (includeHookCreatedPlaceholder || !IsHookCreatedPendingPlaceholder(summary, turn));
    }

    private static async Task StampLegacyHookCreatedPendingPlaceholderAsync(
        string workspacePath,
        NotificationTurn turn,
        CancellationToken cancellationToken
    )
    {
        if (!string.IsNullOrWhiteSpace(turn.SummaryPlaceholderCreatedAt))
        {
            return;
        }

        NotificationSummary? summary = await ReadJsonFileAsync(
            AppPaths.GetSummaryStatePath(workspacePath, turn.SessionId, turn.NotificationTurnId),
            AppJsonSerializerContext.Default.NotificationSummary,
            cancellationToken
        );
        if (summary is not null && IsHookCreatedPendingPlaceholder(summary, turn))
        {
            turn.SummaryPlaceholderCreatedAt = GetPlaceholderCreatedAt(summary);
        }
    }

    private async Task<bool> HasFreshDeliveryClaimAsync(
        string workspacePath,
        string sessionId,
        string notificationTurnId,
        CancellationToken cancellationToken
    )
    {
        string claimPath = AppPaths.GetTurnDeliveryClaimPath(
            workspacePath,
            sessionId,
            notificationTurnId
        );
        return File.Exists(claimPath)
            && !await IsClaimStaleAsync(
                claimPath,
                GetCurrentUtcTimestamp(),
                TimeSpan.FromMinutes(AppConstants.TurnDeliveryClaimStaleAfterMinutes),
                cancellationToken
            );
    }

    private static async Task<bool> HasAssignedSummaryWorthPreservingAsync(
        string workspacePath,
        NotificationTurn turn,
        CancellationToken cancellationToken
    )
    {
        NotificationSummary? summary = await ReadJsonFileAsync(
            AppPaths.GetSummaryStatePath(workspacePath, turn.SessionId, turn.NotificationTurnId),
            AppJsonSerializerContext.Default.NotificationSummary,
            cancellationToken
        );
        bool hasStopObservation = HasStopObservation(workspacePath, turn);
        if (
            summary is null
            || !string.Equals(summary.SessionId, turn.SessionId, StringComparison.Ordinal)
            || !string.Equals(
                summary.NotificationTurnId,
                turn.NotificationTurnId,
                StringComparison.Ordinal
            )
            || !string.Equals(
                summary.NotificationNonce,
                turn.NotificationNonce,
                StringComparison.Ordinal
            )
            || string.IsNullOrWhiteSpace(summary.UpdatedAt)
            || !IsValidUtcTimestamp(summary.UpdatedAt)
        )
        {
            return false;
        }

        if (!string.IsNullOrWhiteSpace(summary.Summary))
        {
            return !hasStopObservation;
        }

        return string.Equals(summary.Status, "pending", StringComparison.Ordinal)
            && string.IsNullOrWhiteSpace(summary.Summary)
            && !IsHookCreatedPendingPlaceholder(summary, turn)
            && (
                !hasStopObservation
                || (
                    await HasPendingStopObservationForTimestampAsync(
                        workspacePath,
                        turn,
                        summary.UpdatedAt,
                        cancellationToken
                    )
                )
            );
    }

    private static async Task<bool> HasPendingStopObservationForTimestampAsync(
        string workspacePath,
        NotificationTurn turn,
        string stopTimestamp,
        CancellationToken cancellationToken
    )
    {
        string stopsDirectory = Path.Combine(
            AppPaths.GetTurnDirectoryPath(workspacePath, turn.SessionId, turn.NotificationTurnId),
            AppConstants.StopsDirectoryName
        );
        if (!Directory.Exists(stopsDirectory))
        {
            return false;
        }

        foreach (string observationPath in Directory.EnumerateFiles(stopsDirectory, "*.json"))
        {
            StopObservation? observation = await ReadJsonFileAsync(
                observationPath,
                AppJsonSerializerContext.Default.StopObservation,
                cancellationToken
            );
            if (
                observation is not null
                && string.Equals(observation.SessionId, turn.SessionId, StringComparison.Ordinal)
                && string.Equals(
                    observation.NotificationTurnId,
                    turn.NotificationTurnId,
                    StringComparison.Ordinal
                )
                && observation.SummaryPendingHandoff
                && string.Equals(observation.StopTimestamp, stopTimestamp, StringComparison.Ordinal)
            )
            {
                return true;
            }
        }

        return false;
    }

    private static bool IsHookCreatedPendingPlaceholder(
        NotificationSummary summary,
        NotificationTurn turn
    )
    {
        if (!string.IsNullOrWhiteSpace(turn.SummaryPlaceholderCreatedAt))
        {
            return string.Equals(
                GetPlaceholderCreatedAt(summary),
                turn.SummaryPlaceholderCreatedAt,
                StringComparison.Ordinal
            );
        }

        string? placeholderCreatedAt = GetPlaceholderCreatedAt(summary);
        return string.Equals(placeholderCreatedAt, turn.CreatedAt, StringComparison.Ordinal)
            || string.Equals(placeholderCreatedAt, turn.UpdatedAt, StringComparison.Ordinal);
    }

    private static string? GetPlaceholderCreatedAt(NotificationSummary summary) =>
        summary.PlaceholderCreatedAt ?? summary.UpdatedAt;

    private static bool HasStopObservation(string workspacePath, NotificationTurn turn)
    {
        string stopsDirectory = Path.Combine(
            AppPaths.GetTurnDirectoryPath(workspacePath, turn.SessionId, turn.NotificationTurnId),
            AppConstants.StopsDirectoryName
        );
        return Directory.Exists(stopsDirectory)
            && Directory
                .EnumerateFiles(stopsDirectory, "*.json", SearchOption.TopDirectoryOnly)
                .Any();
    }

    private static async Task WriteTurnAsync(
        string workspacePath,
        NotificationTurn turn,
        CancellationToken cancellationToken
    ) =>
        await WriteJsonAsync(
            AppPaths.GetTurnStatePath(
                Path.GetFullPath(workspacePath),
                turn.SessionId,
                turn.NotificationTurnId
            ),
            turn,
            AppJsonSerializerContext.Default.NotificationTurn,
            cancellationToken
        );

    private async Task<T?> ReadJsonAsync<T>(
        string path,
        string stateFileKind,
        JsonTypeInfo<T> jsonTypeInfo,
        CancellationToken cancellationToken
    )
        where T : class
    {
        if (!File.Exists(path))
        {
            return null;
        }

        try
        {
            await using FileStream stream = File.OpenRead(path);
            return await JsonSerializer.DeserializeAsync(stream, jsonTypeInfo, cancellationToken);
        }
        catch (Exception ex)
            when (ex
                    is IOException
                        or JsonException
                        or UnauthorizedAccessException
                        or NotSupportedException
            )
        {
            AppLog.FailedToReadStateFile(logger, ex, stateFileKind, path);
            return null;
        }
    }

    private static Task WriteJsonAsync<T>(
        string path,
        T value,
        JsonTypeInfo<T> jsonTypeInfo,
        CancellationToken cancellationToken
    )
    {
        cancellationToken.ThrowIfCancellationRequested();
        string content = JsonSerializer.Serialize(value, jsonTypeInfo);
        AtomicTextFileWriter.WriteAllText(path, content);
        return Task.CompletedTask;
    }

    private static string CreateId(string prefix) => $"{prefix}-{Guid.NewGuid():n}";

    public static async Task<bool> HasDurableDeliveryRecordAsync(
        string workspacePath,
        string sessionId,
        string notificationTurnId,
        CancellationToken cancellationToken
    )
    {
        string turnNotificationsDirectory = Path.Combine(
            AppPaths.GetTurnDirectoryPath(workspacePath, sessionId, notificationTurnId),
            AppConstants.NotificationsRecordsDirectoryName
        );
        if (
            await HasDurableDeliveryRecordInDirectoryAsync(
                turnNotificationsDirectory,
                sessionId,
                notificationTurnId,
                cancellationToken
            )
        )
        {
            return true;
        }

        string sessionNotificationsDirectory = Path.Combine(
            AppPaths.GetSessionDirectoryPath(workspacePath, sessionId),
            AppConstants.NotificationsRecordsDirectoryName
        );
        return await HasDurableDeliveryRecordInDirectoryAsync(
            sessionNotificationsDirectory,
            sessionId,
            notificationTurnId,
            cancellationToken
        );
    }

    private static async Task<bool> HasDurableDeliveryRecordInDirectoryAsync(
        string notificationsDirectory,
        string sessionId,
        string notificationTurnId,
        CancellationToken cancellationToken
    )
    {
        if (!Directory.Exists(notificationsDirectory))
        {
            return false;
        }

        foreach (
            string notificationFile in Directory.EnumerateFiles(
                notificationsDirectory,
                "*.json",
                SearchOption.TopDirectoryOnly
            )
        )
        {
            NotificationRecord? record = await ReadJsonFileAsync(
                notificationFile,
                AppJsonSerializerContext.Default.NotificationRecord,
                cancellationToken
            );
            if (
                record is not null
                && string.Equals(record.SessionId, sessionId, StringComparison.Ordinal)
                && string.Equals(
                    record.NotificationTurnId,
                    notificationTurnId,
                    StringComparison.Ordinal
                )
                && IsDurableDeliveryStatus(record.DeliveryStatus)
            )
            {
                return true;
            }
        }

        return false;
    }

    private static bool IsDurableDeliveryStatus(string? deliveryStatus) =>
        string.Equals(deliveryStatus, "sent", StringComparison.Ordinal)
        || string.Equals(deliveryStatus, "partial", StringComparison.Ordinal);

    private static async Task<FileStream?> TryAcquireStaleReclaimLockAsync(
        string path,
        string claimedAt,
        TimeSpan staleAfter,
        CancellationToken cancellationToken
    )
    {
        if (!TryParseClaimedAt(claimedAt, out DateTimeOffset current))
        {
            return null;
        }

        FileStream stream;
        try
        {
            stream = new FileStream(
                path,
                FileMode.Open,
                FileAccess.ReadWrite,
                FileShare.None,
                bufferSize: 4096,
                FileOptions.Asynchronous
            );
        }
        catch (Exception ex)
            when (ex
                    is FileNotFoundException
                        or DirectoryNotFoundException
                        or IOException
                        or UnauthorizedAccessException
            )
        {
            return null;
        }

        try
        {
            using StreamReader reader = new(
                stream,
                System.Text.Encoding.UTF8,
                detectEncodingFromByteOrderMarks: true,
                leaveOpen: true
            );
            string existingClaimedAt = await reader.ReadToEndAsync(cancellationToken);
            DateTimeOffset existing = TryParseClaimedAt(
                existingClaimedAt,
                out DateTimeOffset parsedExisting
            )
                ? parsedExisting
                : GetClaimFileTimestamp(path);
            if (current - existing < staleAfter)
            {
                stream.Dispose();
                return null;
            }

            byte[] content = System.Text.Encoding.UTF8.GetBytes(claimedAt);
            stream.Position = 0;
            await stream.WriteAsync(content, cancellationToken);
            stream.SetLength(content.Length);
            await stream.FlushAsync(cancellationToken);
            return stream;
        }
        catch
        {
            stream.Dispose();
            throw;
        }
    }

    private static async Task<FileStream?> TryAcquireAbandonmentDeliveryClaimAsync(
        string path,
        string claimedAt,
        CancellationToken cancellationToken
    )
    {
        EnsureOwnerOnlyParentDirectory(path);

        try
        {
            FileStream createdClaim = OpenClaimFile(path);
            try
            {
                await createdClaim.WriteAsync(
                    System.Text.Encoding.UTF8.GetBytes(claimedAt),
                    cancellationToken
                );
                await createdClaim.FlushAsync(cancellationToken);
                return createdClaim;
            }
            catch
            {
                await createdClaim.DisposeAsync();
                await ReleaseOwnedStopNotificationClaimAsync(path, claimedAt, cancellationToken);
                throw;
            }
        }
        catch (IOException) when (File.Exists(path))
        {
            return await TryAcquireStaleReclaimLockAsync(
                path,
                claimedAt,
                TimeSpan.FromMinutes(AppConstants.TurnDeliveryClaimStaleAfterMinutes),
                cancellationToken
            );
        }
    }

    private static async Task ReleaseOwnedStopNotificationClaimAsync(
        string path,
        string claimedAt,
        CancellationToken cancellationToken
    )
    {
        string existingClaimedAt;
        try
        {
            existingClaimedAt = await File.ReadAllTextAsync(path, cancellationToken);
        }
        catch (Exception ex)
            when (ex
                    is FileNotFoundException
                        or DirectoryNotFoundException
                        or IOException
                        or UnauthorizedAccessException
            )
        {
            return;
        }

        if (string.Equals(existingClaimedAt, claimedAt, StringComparison.Ordinal))
        {
            ReleaseStopNotificationClaim(path);
        }
    }

    private static async Task<bool> IsClaimStaleAsync(
        string path,
        string currentClaimedAt,
        TimeSpan staleAfter,
        CancellationToken cancellationToken
    )
    {
        string existingClaimedAt;
        try
        {
            existingClaimedAt = await File.ReadAllTextAsync(path, cancellationToken);
        }
        catch (FileNotFoundException)
        {
            return false;
        }
        catch (DirectoryNotFoundException)
        {
            return false;
        }

        if (!TryParseClaimedAt(currentClaimedAt, out DateTimeOffset current))
        {
            return false;
        }

        DateTimeOffset existing = TryParseClaimedAt(
            existingClaimedAt,
            out DateTimeOffset parsedExisting
        )
            ? parsedExisting
            : GetClaimFileTimestamp(path);
        return current - existing >= staleAfter;
    }

    private static DateTimeOffset GetClaimFileTimestamp(string path)
    {
        DateTime lastWriteTime = File.GetLastWriteTimeUtc(path);
        if (lastWriteTime != DateTime.MinValue)
        {
            return new DateTimeOffset(lastWriteTime, TimeSpan.Zero);
        }

        DateTime creationTime = File.GetCreationTimeUtc(path);
        return new DateTimeOffset(creationTime, TimeSpan.Zero);
    }

    private static bool TryParseClaimedAt(string claimedAt, out DateTimeOffset timestamp) =>
        DateTimeOffset.TryParseExact(
            claimedAt.Trim(),
            "yyyy-MM-ddTHH:mm:ss.fff'Z'",
            CultureInfo.InvariantCulture,
            DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal,
            out timestamp
        );

    private static bool IsValidUtcTimestamp(string value) =>
        DateTimeOffset.TryParseExact(
            value,
            "yyyy-MM-ddTHH:mm:ss.fff'Z'",
            CultureInfo.InvariantCulture,
            DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal,
            out DateTimeOffset parsed
        )
        && string.Equals(
            parsed.ToString("yyyy-MM-ddTHH:mm:ss.fff'Z'", CultureInfo.InvariantCulture),
            value,
            StringComparison.Ordinal
        );

    private static async Task<T?> ReadJsonFileAsync<T>(
        string path,
        JsonTypeInfo<T> jsonTypeInfo,
        CancellationToken cancellationToken
    )
        where T : class
    {
        try
        {
            await using FileStream stream = File.OpenRead(path);
            return await JsonSerializer.DeserializeAsync(stream, jsonTypeInfo, cancellationToken);
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

    private static void EnsureOwnerOnlyParentDirectory(string path)
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

    private static FileStream OpenClaimFile(string path)
    {
        FileStreamOptions options = new()
        {
            Mode = FileMode.CreateNew,
            Access = FileAccess.Write,
            Share = FileShare.Read,
            Options = FileOptions.Asynchronous,
        };
        if (!OperatingSystem.IsWindows())
        {
            options.UnixCreateMode = OwnerOnlyFileMode;
        }

        return new FileStream(path, options);
    }
}

internal sealed record PromptClassification(string Kind, string Reason)
{
    public bool IsHighConfidenceMainPrompt =>
        string.Equals(Kind, "main-user-prompt", StringComparison.Ordinal);
}
