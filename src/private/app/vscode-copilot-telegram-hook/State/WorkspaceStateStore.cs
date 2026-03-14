using System.Text.Json;
using System.Text.Json.Serialization.Metadata;

namespace Hcoona.VsCodeCopilotTelegramHook.State;

internal sealed class WorkspaceStateStore(TimeProvider timeProvider)
{
    public async Task<SessionState> InitializeSessionAsync(
        SessionStartHookInput input,
        CancellationToken cancellationToken)
    {
        string now = GetCurrentUtcTimestamp();
        return await EnsureSessionAsync(
            Path.GetFullPath(input.Cwd),
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

        return turnState;
    }

    public static Task<SessionState?> TryReadSessionAsync(
        string workspacePath,
        string sessionId,
        CancellationToken cancellationToken)
        => ReadJsonAsync(
            AppPaths.GetSessionStatePath(Path.GetFullPath(workspacePath), sessionId),
            AppJsonSerializerContext.Default.SessionState,
            cancellationToken);

    public static Task<TurnState?> TryReadTurnAsync(
        string workspacePath,
        string sessionId,
        CancellationToken cancellationToken)
        => ReadJsonAsync(
            AppPaths.GetTurnStatePath(Path.GetFullPath(workspacePath), sessionId),
            AppJsonSerializerContext.Default.TurnState,
            cancellationToken);

    public static Task<SummaryRecord?> TryReadSummaryAsync(
        string workspacePath,
        string sessionId,
        CancellationToken cancellationToken)
        => ReadJsonAsync(
            AppPaths.GetSummaryStatePath(Path.GetFullPath(workspacePath), sessionId),
            AppJsonSerializerContext.Default.SummaryRecord,
            cancellationToken);

    public static Task<LastSentState?> TryReadLastSentAsync(
        string workspacePath,
        string sessionId,
        CancellationToken cancellationToken)
        => ReadJsonAsync(
            AppPaths.GetLastSentStatePath(Path.GetFullPath(workspacePath), sessionId),
            AppJsonSerializerContext.Default.LastSentState,
            cancellationToken);

    public static async Task<bool> WasStopAlreadySentAsync(
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

    private static async Task<SessionState> EnsureSessionAsync(
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

        await WriteJsonAsync(
            AppPaths.GetSessionStatePath(workspacePath, sessionId),
            sessionState,
            AppJsonSerializerContext.Default.SessionState,
            cancellationToken);

        return sessionState;
    }

    private static async Task<T?> ReadJsonAsync<T>(
        string path,
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
            return null;
        }
    }

    private static async Task WriteJsonAsync<T>(
        string path,
        T value,
        JsonTypeInfo<T> jsonTypeInfo,
        CancellationToken cancellationToken)
    {
        string? directoryPath = Path.GetDirectoryName(path);
        if (!string.IsNullOrWhiteSpace(directoryPath))
        {
            Directory.CreateDirectory(directoryPath);
        }

        await using FileStream stream = File.Create(path);
        await JsonSerializer.SerializeAsync(stream, value, jsonTypeInfo, cancellationToken);
    }
}
