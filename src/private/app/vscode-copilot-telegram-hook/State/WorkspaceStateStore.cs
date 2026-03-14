using System.Text.Json;
using System.Text.Json.Serialization.Metadata;

namespace Hcoona.VsCodeCopilotTelegramHook.State;

internal sealed class WorkspaceStateStore(TimeProvider timeProvider)
{
    public async Task<SessionState> InitializeSessionAsync(
        SessionStartHookInput input,
        CancellationToken cancellationToken)
    {
        string workspacePath = Path.GetFullPath(input.Cwd);
        string copilotDirectory = AppPaths.GetWorkspaceCopilotDirectory(workspacePath);
        Directory.CreateDirectory(copilotDirectory);

        string now = GetCurrentUtcTimestamp();
        SessionState sessionState = new()
        {
            RunId = Guid.NewGuid().ToString("n"),
            SessionId = input.SessionId,
            WorkspacePath = workspacePath,
            CreatedAt = now,
            UpdatedAt = now,
            TranscriptPath = input.TranscriptPath,
        };

        SummaryRecord placeholderSummary = new()
        {
            RunId = sessionState.RunId,
            UpdatedAt = now,
            Details = [],
            ChangedFiles = [],
            NextSteps = [],
        };

        await WriteJsonAsync(
            AppPaths.GetSessionStatePath(workspacePath),
            sessionState,
            AppJsonSerializerContext.Default.SessionState,
            cancellationToken);

        await WriteJsonAsync(
            AppPaths.GetSummaryStatePath(workspacePath),
            placeholderSummary,
            AppJsonSerializerContext.Default.SummaryRecord,
            cancellationToken);

        return sessionState;
    }

    public static Task<SessionState?> TryReadSessionAsync(
        string workspacePath,
        CancellationToken cancellationToken)
        => ReadJsonAsync(
            AppPaths.GetSessionStatePath(Path.GetFullPath(workspacePath)),
            AppJsonSerializerContext.Default.SessionState,
            cancellationToken);

    public static Task<SummaryRecord?> TryReadSummaryAsync(
        string workspacePath,
        CancellationToken cancellationToken)
        => ReadJsonAsync(
            AppPaths.GetSummaryStatePath(Path.GetFullPath(workspacePath)),
            AppJsonSerializerContext.Default.SummaryRecord,
            cancellationToken);

    public static Task<LastSentState?> TryReadLastSentAsync(
        string workspacePath,
        CancellationToken cancellationToken)
        => ReadJsonAsync(
            AppPaths.GetLastSentStatePath(Path.GetFullPath(workspacePath)),
            AppJsonSerializerContext.Default.LastSentState,
            cancellationToken);

    public static async Task<bool> WasStopAlreadySentAsync(
        StopHookInput input,
        CancellationToken cancellationToken)
    {
        LastSentState? lastSentState = await TryReadLastSentAsync(input.Cwd, cancellationToken);
        if (lastSentState is null)
        {
            return false;
        }

        return string.Equals(
            lastSentState.WorkspacePath,
            Path.GetFullPath(input.Cwd),
            StringComparison.Ordinal)
            && string.Equals(lastSentState.SessionId, input.SessionId, StringComparison.Ordinal)
            && string.Equals(
                lastSentState.StopTimestamp,
                input.Timestamp,
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
            RunId = context.RunId,
            SessionId = input.SessionId,
            WorkspacePath = Path.GetFullPath(input.Cwd),
            StopTimestamp = input.Timestamp,
            SentAt = context.SentAt,
            SummaryUpdatedAt = summary?.UpdatedAt,
        };

        return WriteJsonAsync(
            AppPaths.GetLastSentStatePath(Path.GetFullPath(input.Cwd)),
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
