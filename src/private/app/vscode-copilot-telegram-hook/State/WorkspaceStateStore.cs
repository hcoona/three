using System.Text.Json;
using System.Text.Json.Serialization.Metadata;
using Hcoona.VsCodeCopilotTelegramHook.Logging;
using Microsoft.Extensions.Logging;

namespace Hcoona.VsCodeCopilotTelegramHook.State;

internal sealed class WorkspaceStateStore(
    TimeProvider timeProvider,
    ILogger<WorkspaceStateStore> logger)
{
    public async Task<SessionState> InitializeSessionAsync(
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

    public async Task<TurnState> StartTurnAsync(
        UserPromptSubmitHookInput input,
        CancellationToken cancellationToken)
    {
        string workspacePath = Path.GetFullPath(input.Cwd);
        string now = GetCurrentUtcTimestamp();
        AppLog.StartingTurnState(logger, input.SessionId, workspacePath);

        _ = await EnsureSessionAsync(
            workspacePath,
            input.SessionId,
            input.TranscriptPath,
            now,
            cancellationToken);

        TurnState turnState = new()
        {
            SessionId = input.SessionId,
            TurnId = Guid.NewGuid().ToString("n"),
            WorkspacePath = workspacePath,
            CreatedAt = now,
            UpdatedAt = now,
            TranscriptPath = input.TranscriptPath,
        };

        SummaryRecord placeholderSummary = new()
        {
            SessionId = turnState.SessionId,
            TurnId = turnState.TurnId,
            UpdatedAt = now,
            Details = [],
            ChangedFiles = [],
            NextSteps = [],
        };

        await WriteJsonAsync(
            AppPaths.GetTurnStatePath(workspacePath, input.SessionId),
            turnState,
            AppJsonSerializerContext.Default.TurnState,
            cancellationToken);

        await WriteJsonAsync(
            AppPaths.GetSummaryStatePath(workspacePath, input.SessionId),
            placeholderSummary,
            AppJsonSerializerContext.Default.SummaryRecord,
            cancellationToken);
        AppLog.CreatedTurnState(logger, turnState.TurnId, turnState.SessionId);

        return turnState;
    }

    public Task<SessionState?> TryReadSessionAsync(
        string workspacePath,
        string sessionId,
        CancellationToken cancellationToken)
        => ReadJsonAsync(
            AppPaths.GetSessionStatePath(Path.GetFullPath(workspacePath), sessionId),
            "session state",
            AppJsonSerializerContext.Default.SessionState,
            cancellationToken);

    public Task<TurnState?> TryReadTurnAsync(
        string workspacePath,
        string sessionId,
        CancellationToken cancellationToken)
        => ReadJsonAsync(
            AppPaths.GetTurnStatePath(Path.GetFullPath(workspacePath), sessionId),
            "turn state",
            AppJsonSerializerContext.Default.TurnState,
            cancellationToken);

    public Task<SummaryRecord?> TryReadSummaryAsync(
        string workspacePath,
        string sessionId,
        CancellationToken cancellationToken)
        => ReadJsonAsync(
            AppPaths.GetSummaryStatePath(Path.GetFullPath(workspacePath), sessionId),
            "summary state",
            AppJsonSerializerContext.Default.SummaryRecord,
            cancellationToken);

    public Task<LastSentState?> TryReadLastSentAsync(
        string workspacePath,
        string sessionId,
        CancellationToken cancellationToken)
        => ReadJsonAsync(
            AppPaths.GetLastSentStatePath(Path.GetFullPath(workspacePath), sessionId),
            "last-sent state",
            AppJsonSerializerContext.Default.LastSentState,
            cancellationToken);

    public async Task<bool> WasStopAlreadySentAsync(
        string workspacePath,
        string sessionId,
        string? turnId,
        string stopTimestamp,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(turnId))
        {
            return false;
        }

        LastSentState? lastSentState = await TryReadLastSentAsync(
            workspacePath,
            sessionId,
            cancellationToken);
        if (lastSentState is null)
        {
            return false;
        }

        return string.Equals(
            lastSentState.WorkspacePath,
            Path.GetFullPath(workspacePath),
            StringComparison.Ordinal)
            && string.Equals(lastSentState.SessionId, sessionId, StringComparison.Ordinal)
            && string.Equals(lastSentState.TurnId, turnId, StringComparison.Ordinal)
            && string.Equals(
                lastSentState.StopTimestamp,
                stopTimestamp,
                StringComparison.Ordinal);
    }

    public static Task RecordNotificationAsync(
        StopHookInput input,
        NotificationContext context,
        SummaryRecord? summary,
        CancellationToken cancellationToken)
    {
        LastSentState lastSentState = new()
        {
            SessionId = input.SessionId,
            TurnId = context.TurnId,
            WorkspacePath = Path.GetFullPath(input.Cwd),
            StopTimestamp = input.Timestamp,
            SentAt = context.SentAt,
            SummaryUpdatedAt = summary?.UpdatedAt,
        };

        return WriteJsonAsync(
            AppPaths.GetLastSentStatePath(Path.GetFullPath(input.Cwd), input.SessionId),
            lastSentState,
            AppJsonSerializerContext.Default.LastSentState,
            cancellationToken);
    }

    public string GetCurrentUtcTimestamp()
    {
        return timeProvider
            .GetUtcNow()
            .UtcDateTime
            .ToString("yyyy-MM-ddTHH:mm:ss.fff'Z'");
    }

    private async Task<SessionState> EnsureSessionAsync(
        string workspacePath,
        string sessionId,
        string? transcriptPath,
        string now,
        CancellationToken cancellationToken)
    {
        SessionState sessionState = await TryReadSessionAsync(
                workspacePath,
                sessionId,
                cancellationToken)
            ?? new SessionState
            {
                SessionId = sessionId,
                WorkspacePath = workspacePath,
                CreatedAt = now,
            };

        sessionState.WorkspacePath = workspacePath;
        sessionState.UpdatedAt = now;
        if (!string.IsNullOrWhiteSpace(transcriptPath))
        {
            sessionState.TranscriptPath = transcriptPath;
        }

        string sessionStatePath = AppPaths.GetSessionStatePath(workspacePath, sessionId);
        await WriteJsonAsync(
            sessionStatePath,
            sessionState,
            AppJsonSerializerContext.Default.SessionState,
            cancellationToken);
        AppLog.WroteSessionState(logger, sessionId, sessionStatePath);

        return sessionState;
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

    private static async Task WriteJsonAsync<T>(
        string path,
        T value,
        JsonTypeInfo<T> jsonTypeInfo,
        CancellationToken cancellationToken)
    {
        await using FileStream stream = AppFileSystem.CreateFile(path);
        await JsonSerializer.SerializeAsync(stream, value, jsonTypeInfo, cancellationToken);
    }
}
