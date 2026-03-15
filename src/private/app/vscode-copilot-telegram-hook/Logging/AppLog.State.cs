using Microsoft.Extensions.Logging;

namespace Hcoona.VsCodeCopilotTelegramHook.Logging;

internal static partial class AppLog
{
    [LoggerMessage(
        EventId = 1300,
        EventName = nameof(InitializingSessionState),
        Level = LogLevel.Debug,
        Message =
            "Initializing workspace session state for session {SessionId} in "
            + "{WorkspacePath}.")]
    public static partial void InitializingSessionState(
        ILogger logger,
        string sessionId,
        string workspacePath);

    [LoggerMessage(
        EventId = 1301,
        EventName = nameof(StartingTurnState),
        Level = LogLevel.Debug,
        Message = "Starting turn state for session {SessionId} in {WorkspacePath}.")]
    public static partial void StartingTurnState(
        ILogger logger,
        string sessionId,
        string workspacePath);

    [LoggerMessage(
        EventId = 1302,
        EventName = nameof(CreatedTurnState),
        Level = LogLevel.Information,
        Message = "Created turn {TurnId} and placeholder summary for session {SessionId}.")]
    public static partial void CreatedTurnState(
        ILogger logger,
        string turnId,
        string sessionId);

    [LoggerMessage(
        EventId = 1303,
        EventName = nameof(WroteSessionState),
        Level = LogLevel.Debug,
        Message = "Wrote session state for session {SessionId} to {SessionStatePath}.")]
    public static partial void WroteSessionState(
        ILogger logger,
        string sessionId,
        string sessionStatePath);

    [LoggerMessage(
        EventId = 1304,
        EventName = nameof(FailedToReadStateFile),
        Level = LogLevel.Warning,
        Message = "Failed to read {StateFileKind} at {StateFilePath}; treating it as missing.")]
    public static partial void FailedToReadStateFile(
        ILogger logger,
        Exception exception,
        string stateFileKind,
        string stateFilePath);
}
