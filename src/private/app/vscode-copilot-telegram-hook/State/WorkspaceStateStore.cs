using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization.Metadata;
using Hcoona.VsCodeCopilotTelegramHook.Logging;
using Microsoft.Extensions.Logging;

namespace Hcoona.VsCodeCopilotTelegramHook.State;

internal sealed class WorkspaceStateStore(
    TimeProvider timeProvider,
    ILogger<WorkspaceStateStore> logger)
{
    private const UnixFileMode OwnerOnlyDirectoryMode =
        UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute;

    private const UnixFileMode OwnerOnlyFileMode =
        UnixFileMode.UserRead | UnixFileMode.UserWrite;

    public async Task<NotificationSession> InitializeSessionAsync(
        SessionStartHookInput input,
        CancellationToken cancellationToken)
    {
        string workspacePath = Path.GetFullPath(input.Cwd);
        string now = GetCurrentUtcTimestamp();
        AppLog.InitializingSessionState(logger, input.SessionId, workspacePath);
        return await EnsureSessionAsync(
            workspacePath,
            input.SessionId,
            input.TranscriptPath,
            now,
            cancellationToken);
    }

    public async Task<PromptObservation> RecordPromptObservationAsync(
        UserPromptSubmitHookInput input,
        PromptClassification classification,
        CancellationToken cancellationToken)
    {
        string workspacePath = Path.GetFullPath(input.Cwd);
        string now = GetCurrentUtcTimestamp();
        _ = await EnsureSessionAsync(
            workspacePath,
            input.SessionId,
            input.TranscriptPath,
            now,
            cancellationToken);

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
                observation.PromptObservationId),
            observation,
            AppJsonSerializerContext.Default.PromptObservation,
            cancellationToken);
        AppLog.RecordedPromptObservation(
            logger,
            observation.PromptObservationId,
            input.SessionId,
            classification.Kind,
            classification.Reason);
        return observation;
    }

    public async Task<NotificationTurn> CreateNotificationTurnAsync(
        UserPromptSubmitHookInput input,
        PromptObservation observation,
        CancellationToken cancellationToken)
    {
        string workspacePath = Path.GetFullPath(input.Cwd);
        string now = GetCurrentUtcTimestamp();
        string createdAt = string.IsNullOrWhiteSpace(input.Timestamp)
            ? now
            : input.Timestamp;
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
            TranscriptPath = input.TranscriptPath,
        };

        NotificationSummary placeholderSummary = new()
        {
            SessionId = turn.SessionId,
            NotificationTurnId = turn.NotificationTurnId,
            NotificationNonce = turn.NotificationNonce,
            UpdatedAt = now,
            Status = "pending",
            Details = [],
            ChangedFiles = [],
            NextSteps = [],
        };

        string summaryPath = AppPaths.GetSummaryStatePath(
            workspacePath,
            input.SessionId,
            turn.NotificationTurnId);
        CurrentNotificationState current = new()
        {
            SessionId = turn.SessionId,
            NotificationTurnId = turn.NotificationTurnId,
            NotificationNonce = turn.NotificationNonce,
            SummaryPath = summaryPath,
            UpdatedAt = now,
        };

        await WriteJsonAsync(
            AppPaths.GetTurnStatePath(workspacePath, input.SessionId, turn.NotificationTurnId),
            turn,
            AppJsonSerializerContext.Default.NotificationTurn,
            cancellationToken);
        await WriteJsonAsync(
            summaryPath,
            placeholderSummary,
            AppJsonSerializerContext.Default.NotificationSummary,
            cancellationToken);
        await WriteJsonAsync(
            AppPaths.GetCurrentStatePath(workspacePath, input.SessionId),
            current,
            AppJsonSerializerContext.Default.CurrentNotificationState,
            cancellationToken);
        AppLog.CreatedTurnState(logger, turn.NotificationTurnId, turn.SessionId);
        return turn;
    }

    public Task<NotificationSession?> TryReadSessionAsync(
        string workspacePath,
        string sessionId,
        CancellationToken cancellationToken)
        => ReadJsonAsync(
            AppPaths.GetSessionStatePath(Path.GetFullPath(workspacePath), sessionId),
            "notification session",
            AppJsonSerializerContext.Default.NotificationSession,
            cancellationToken);

    public Task<CurrentNotificationState?> TryReadCurrentAsync(
        string workspacePath,
        string sessionId,
        CancellationToken cancellationToken)
        => ReadJsonAsync(
            AppPaths.GetCurrentStatePath(Path.GetFullPath(workspacePath), sessionId),
            "current notification cache",
            AppJsonSerializerContext.Default.CurrentNotificationState,
            cancellationToken);

    public Task<NotificationTurn?> TryReadTurnAsync(
        string workspacePath,
        string sessionId,
        string notificationTurnId,
        CancellationToken cancellationToken)
        => ReadJsonAsync(
            AppPaths.GetTurnStatePath(
                Path.GetFullPath(workspacePath),
                sessionId,
                notificationTurnId),
            "notification turn",
            AppJsonSerializerContext.Default.NotificationTurn,
            cancellationToken);

    public Task<NotificationSummary?> TryReadSummaryAsync(
        string workspacePath,
        string sessionId,
        string notificationTurnId,
        CancellationToken cancellationToken)
        => ReadJsonAsync(
            AppPaths.GetSummaryStatePath(
                Path.GetFullPath(workspacePath),
                sessionId,
                notificationTurnId),
            "notification summary",
            AppJsonSerializerContext.Default.NotificationSummary,
            cancellationToken);

    public async Task<IReadOnlyList<NotificationTurn>> ListOpenTurnsAsync(
        string workspacePath,
        string sessionId,
        CancellationToken cancellationToken)
    {
        string turnsDirectory = AppPaths.GetTurnsDirectoryPath(
            Path.GetFullPath(workspacePath),
            sessionId);
        if (!Directory.Exists(turnsDirectory))
        {
            return [];
        }

        List<NotificationTurn> turns = [];
        foreach (string turnFile in Directory.EnumerateFiles(
                     turnsDirectory,
                     AppConstants.TurnFileName,
                     SearchOption.AllDirectories))
        {
            NotificationTurn? turn = await ReadJsonAsync(
                turnFile,
                "notification turn",
                AppJsonSerializerContext.Default.NotificationTurn,
                cancellationToken);
            if (turn is not null
                && string.Equals(turn.SessionId, sessionId, StringComparison.Ordinal)
                && string.Equals(turn.Status, "open", StringComparison.Ordinal)
                && !await HasDurableDeliveryRecordAsync(
                    Path.GetFullPath(workspacePath),
                    sessionId,
                    turn.NotificationTurnId,
                    cancellationToken))
            {
                turns.Add(turn);
            }
        }

        return turns
            .OrderBy(static turn => turn.CreatedAt, StringComparer.Ordinal)
            .ToArray();
    }

    public async Task<IReadOnlyList<PromptObservation>> ListPromptObservationsAsync(
        string workspacePath,
        string sessionId,
        CancellationToken cancellationToken)
    {
        string promptsDirectory = Path.Combine(
            AppPaths.GetSessionDirectoryPath(Path.GetFullPath(workspacePath), sessionId),
            AppConstants.PromptsDirectoryName);
        if (!Directory.Exists(promptsDirectory))
        {
            return [];
        }

        List<PromptObservation> observations = [];
        foreach (string promptFile in Directory.EnumerateFiles(
                     promptsDirectory,
                     "*.json",
                     SearchOption.TopDirectoryOnly))
        {
            PromptObservation? observation = await ReadJsonAsync(
                promptFile,
                "prompt observation",
                AppJsonSerializerContext.Default.PromptObservation,
                cancellationToken);
            if (observation is not null
                && string.Equals(observation.SessionId, sessionId, StringComparison.Ordinal))
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
        CancellationToken cancellationToken)
    {
        string notificationsDirectory = Path.Combine(
            AppPaths.GetSessionDirectoryPath(Path.GetFullPath(workspacePath), sessionId),
            AppConstants.NotificationsRecordsDirectoryName);
        if (!Directory.Exists(notificationsDirectory))
        {
            return [];
        }

        List<NotificationRecord> records = [];
        foreach (string notificationFile in Directory.EnumerateFiles(
                     notificationsDirectory,
                     "*.json",
                     SearchOption.TopDirectoryOnly))
        {
            NotificationRecord? record = await ReadJsonAsync(
                notificationFile,
                "session notification record",
                AppJsonSerializerContext.Default.NotificationRecord,
                cancellationToken);
            if (record is not null
                && string.Equals(record.SessionId, sessionId, StringComparison.Ordinal))
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
        CancellationToken cancellationToken)
        => Task.FromResult(File.Exists(path));

    public static async Task<bool> TryClaimStopNotificationAsync(
        string path,
        string claimedAt,
        CancellationToken cancellationToken)
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
                    cancellationToken);
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
        catch (Exception ex) when (
            ex is IOException or UnauthorizedAccessException or DirectoryNotFoundException)
        {
        }
    }

    public static async Task<bool> TryReclaimStaleClaimAsync(
        string path,
        string reclaimPath,
        string claimedAt,
        TimeSpan staleAfter,
        Func<Task<bool>> hasDurableDeliveryRecordAsync,
        CancellationToken cancellationToken)
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
            cancellationToken);
        if (!claimedReclaim)
        {
            staleReclaimLock = await TryAcquireStaleReclaimLockAsync(
                reclaimPath,
                claimedAt,
                staleAfter,
                cancellationToken);
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
        CancellationToken cancellationToken)
    {
        await WriteJsonAsync(
            AppPaths.GetStopObservationPath(
                Path.GetFullPath(workspacePath),
                turn.SessionId,
                turn.NotificationTurnId,
                observation.StopId),
            observation,
            AppJsonSerializerContext.Default.StopObservation,
            cancellationToken);
    }

    public static async Task RecordNotificationAsync(
        string path,
        NotificationRecord record,
        CancellationToken cancellationToken)
    {
        await WriteJsonAsync(
            path,
            record,
            AppJsonSerializerContext.Default.NotificationRecord,
            cancellationToken);
    }

    public static async Task MarkTurnNotifiedAsync(
        string workspacePath,
        NotificationTurn turn,
        string now,
        CancellationToken cancellationToken)
    {
        turn.Status = "notified";
        turn.UpdatedAt = now;
        await WriteJsonAsync(
            AppPaths.GetTurnStatePath(
                Path.GetFullPath(workspacePath),
                turn.SessionId,
                turn.NotificationTurnId),
            turn,
            AppJsonSerializerContext.Default.NotificationTurn,
            cancellationToken);
    }

    public string GetCurrentUtcTimestamp()
    {
        return timeProvider
            .GetUtcNow()
            .UtcDateTime
            .ToString("yyyy-MM-ddTHH:mm:ss.fff'Z'", CultureInfo.InvariantCulture);
    }

    private async Task<NotificationSession> EnsureSessionAsync(
        string workspacePath,
        string sessionId,
        string? transcriptPath,
        string now,
        CancellationToken cancellationToken)
    {
        NotificationSession session = await TryReadSessionAsync(
                workspacePath,
                sessionId,
                cancellationToken)
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
            cancellationToken);
        AppLog.WroteSessionState(logger, sessionId, sessionStatePath);
        return session;
    }

    private async Task<T?> ReadJsonAsync<T>(
        string path,
        string stateFileKind,
        JsonTypeInfo<T> jsonTypeInfo,
        CancellationToken cancellationToken)
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
        catch (Exception ex) when (
            ex is IOException or JsonException or UnauthorizedAccessException
                or NotSupportedException)
        {
            AppLog.FailedToReadStateFile(logger, ex, stateFileKind, path);
            return null;
        }
    }

    private static Task WriteJsonAsync<T>(
        string path,
        T value,
        JsonTypeInfo<T> jsonTypeInfo,
        CancellationToken cancellationToken)
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
        CancellationToken cancellationToken)
    {
        string turnNotificationsDirectory = Path.Combine(
            AppPaths.GetTurnDirectoryPath(workspacePath, sessionId, notificationTurnId),
            AppConstants.NotificationsRecordsDirectoryName);
        if (await HasDurableDeliveryRecordInDirectoryAsync(
                turnNotificationsDirectory,
                sessionId,
                notificationTurnId,
                cancellationToken))
        {
            return true;
        }

        string sessionNotificationsDirectory = Path.Combine(
            AppPaths.GetSessionDirectoryPath(workspacePath, sessionId),
            AppConstants.NotificationsRecordsDirectoryName);
        return await HasDurableDeliveryRecordInDirectoryAsync(
            sessionNotificationsDirectory,
            sessionId,
            notificationTurnId,
            cancellationToken);
    }

    private static async Task<bool> HasDurableDeliveryRecordInDirectoryAsync(
        string notificationsDirectory,
        string sessionId,
        string notificationTurnId,
        CancellationToken cancellationToken)
    {
        if (!Directory.Exists(notificationsDirectory))
        {
            return false;
        }

        foreach (string notificationFile in Directory.EnumerateFiles(
                     notificationsDirectory,
                     "*.json",
                     SearchOption.TopDirectoryOnly))
        {
            NotificationRecord? record = await ReadJsonFileAsync(
                notificationFile,
                AppJsonSerializerContext.Default.NotificationRecord,
                cancellationToken);
            if (record is not null
                && string.Equals(record.SessionId, sessionId, StringComparison.Ordinal)
                && string.Equals(
                    record.NotificationTurnId,
                    notificationTurnId,
                    StringComparison.Ordinal)
                && IsDurableDeliveryStatus(record.DeliveryStatus))
            {
                return true;
            }
        }

        return false;
    }

    private static bool IsDurableDeliveryStatus(string? deliveryStatus)
        => string.Equals(deliveryStatus, "sent", StringComparison.Ordinal)
            || string.Equals(deliveryStatus, "partial", StringComparison.Ordinal);

    private static async Task<FileStream?> TryAcquireStaleReclaimLockAsync(
        string path,
        string claimedAt,
        TimeSpan staleAfter,
        CancellationToken cancellationToken)
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
                FileOptions.Asynchronous);
        }
        catch (Exception ex) when (
            ex is FileNotFoundException
                or DirectoryNotFoundException
                or IOException
                or UnauthorizedAccessException)
        {
            return null;
        }

        try
        {
            using StreamReader reader = new(
                stream,
                System.Text.Encoding.UTF8,
                detectEncodingFromByteOrderMarks: true,
                leaveOpen: true);
            string existingClaimedAt = await reader.ReadToEndAsync(cancellationToken);
            DateTimeOffset existing = TryParseClaimedAt(
                existingClaimedAt,
                out DateTimeOffset parsedExisting)
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

    private static async Task<bool> IsClaimStaleAsync(
        string path,
        string currentClaimedAt,
        TimeSpan staleAfter,
        CancellationToken cancellationToken)
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
            out DateTimeOffset parsedExisting)
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

    private static bool TryParseClaimedAt(string claimedAt, out DateTimeOffset timestamp)
        => DateTimeOffset.TryParseExact(
            claimedAt.Trim(),
            "yyyy-MM-ddTHH:mm:ss.fff'Z'",
            CultureInfo.InvariantCulture,
            DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal,
            out timestamp);

    private static async Task<T?> ReadJsonFileAsync<T>(
        string path,
        JsonTypeInfo<T> jsonTypeInfo,
        CancellationToken cancellationToken)
        where T : class
    {
        try
        {
            await using FileStream stream = File.OpenRead(path);
            return await JsonSerializer.DeserializeAsync(stream, jsonTypeInfo, cancellationToken);
        }
        catch (Exception ex) when (
            ex is IOException or JsonException or UnauthorizedAccessException
                or NotSupportedException)
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
    public bool IsHighConfidenceMainPrompt
        => string.Equals(Kind, "main-user-prompt", StringComparison.Ordinal);
}
