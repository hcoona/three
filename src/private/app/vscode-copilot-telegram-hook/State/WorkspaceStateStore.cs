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
                && string.Equals(turn.Status, "open", StringComparison.Ordinal))
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
        try
        {
            await using FileStream stream = OpenClaimFile(path);
            await using StreamWriter writer = new(stream);
            await writer.WriteAsync(claimedAt.AsMemory(), cancellationToken);
            await writer.FlushAsync(cancellationToken);
            await stream.FlushAsync(cancellationToken);
            return true;
        }
        catch (IOException)
        {
            return false;
        }
        catch (UnauthorizedAccessException)
        {
            return false;
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
            .ToString("yyyy-MM-ddTHH:mm:ss.fff'Z'");
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
