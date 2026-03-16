using Microsoft.Extensions.Logging;

namespace Hcoona.VsCodeCopilotTelegramHook.Logging;

internal static partial class AppLog
{
    [LoggerMessage(
        EventId = 1500,
        EventName = nameof(HandlingSessionStart),
        Level = LogLevel.Information,
        Message = "Handling SessionStart hook for session {SessionId} in {WorkspacePath}.")]
    public static partial void HandlingSessionStart(
        ILogger logger,
        string sessionId,
        string workspacePath);

    [LoggerMessage(
        EventId = 1501,
        EventName = nameof(WroteSessionStartContext),
        Level = LogLevel.Information,
        Message = "Wrote SessionStart additional context for session {SessionId}.")]
    public static partial void WroteSessionStartContext(ILogger logger, string sessionId);

    [LoggerMessage(
        EventId = 1502,
        EventName = nameof(SessionStartFailed),
        Level = LogLevel.Error,
        Message = "SessionStart hook failed.")]
    public static partial void SessionStartFailed(ILogger logger, Exception exception);

    [LoggerMessage(
        EventId = 1503,
        EventName = nameof(HandlingUserPromptSubmit),
        Level = LogLevel.Information,
        Message =
            "Handling UserPromptSubmit hook for session {SessionId} in "
            + "{WorkspacePath}; promptLength={PromptLength}.")]
    public static partial void HandlingUserPromptSubmit(
        ILogger logger,
        string sessionId,
        string workspacePath,
        int promptLength);

    [LoggerMessage(
        EventId = 1504,
        EventName = nameof(UserPromptSubmitFailed),
        Level = LogLevel.Error,
        Message = "UserPromptSubmit hook failed.")]
    public static partial void UserPromptSubmitFailed(ILogger logger, Exception exception);

    [LoggerMessage(
        EventId = 1505,
        EventName = nameof(HandlingStopHook),
        Level = LogLevel.Information,
        Message = "Handling Stop hook for session {SessionId} in {WorkspacePath}.")]
    public static partial void HandlingStopHook(
        ILogger logger,
        string sessionId,
        string workspacePath);

    [LoggerMessage(
        EventId = 1506,
        EventName = nameof(SkippingDuplicateStop),
        Level = LogLevel.Information,
        Message =
            "Skipping duplicate Stop hook delivery for session {SessionId} and "
            + "turn {TurnId}.")]
    public static partial void SkippingDuplicateStop(
        ILogger logger,
        string sessionId,
        string turnId);

    [LoggerMessage(
        EventId = 1507,
        EventName = nameof(SendingStopNotification),
        Level = LogLevel.Information,
        Message =
            "Sending {MessageCount} Telegram message(s) for session {SessionId} "
            + "and turn {TurnId}.")]
    public static partial void SendingStopNotification(
        ILogger logger,
        int messageCount,
        string sessionId,
        string turnId);

    [LoggerMessage(
        EventId = 1508,
        EventName = nameof(RecordedStopNotification),
        Level = LogLevel.Information,
        Message = "Recorded Stop hook delivery state for session {SessionId} and turn {TurnId}.")]
    public static partial void RecordedStopNotification(
        ILogger logger,
        string sessionId,
        string turnId);

    [LoggerMessage(
        EventId = 1511,
        EventName = nameof(BlockingStopForSummaryValidation),
        Level = LogLevel.Warning,
        Message =
            "Blocking Stop hook for session {SessionId} and turn {TurnId}; "
            + "validationFailureCount={ValidationFailureCount}; reason={Reason}")]
    public static partial void BlockingStopForSummaryValidation(
        ILogger logger,
        string sessionId,
        string turnId,
        int validationFailureCount,
        string reason);

    [LoggerMessage(
        EventId = 1512,
        EventName = nameof(AllowingStopAfterValidationFailures),
        Level = LogLevel.Warning,
        Message =
            "Allowing Stop hook for session {SessionId} and turn {TurnId} after "
            + "reaching the validation failure limit; reason={Reason}")]
    public static partial void AllowingStopAfterValidationFailures(
        ILogger logger,
        string sessionId,
        string turnId,
        string reason);

    [LoggerMessage(
        EventId = 1509,
        EventName = nameof(StopHookFailed),
        Level = LogLevel.Error,
        Message = "Stop hook failed.")]
    public static partial void StopHookFailed(ILogger logger, Exception exception);

    [LoggerMessage(
        EventId = 1510,
        EventName = nameof(IgnoringInvalidHookInput),
        Level = LogLevel.Warning,
        Message = "Ignoring invalid {HookEventName} hook input: {Reason}")]
    public static partial void IgnoringInvalidHookInput(
        ILogger logger,
        string hookEventName,
        string reason);
}
