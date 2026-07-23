using Microsoft.Extensions.Logging;

namespace Hcoona.VsCodeCopilotTelegramHook.Logging;

internal static partial class AppLog
{
    [LoggerMessage(
        EventId = 1800,
        EventName = nameof(SendingCopilotCliNotification),
        Level = LogLevel.Information,
        Message =
            "Sending {MessageCount} Copilot CLI Telegram message(s) for session "
            + "{SessionId}, event {EventId}, type {EventType}.")]
    public static partial void SendingCopilotCliNotification(
        ILogger logger,
        int messageCount,
        string sessionId,
        string eventId,
        string eventType);

    [LoggerMessage(
        EventId = 1801,
        EventName = nameof(SkippingDuplicateCopilotCliNotification),
        Level = LogLevel.Information,
        Message =
            "Skipping duplicate Copilot CLI notification for session {SessionId} "
            + "and event {EventId}.")]
    public static partial void SkippingDuplicateCopilotCliNotification(
        ILogger logger,
        string sessionId,
        string eventId);

    [LoggerMessage(
        EventId = 1802,
        EventName = nameof(CopilotCliNotificationFailed),
        Level = LogLevel.Error,
        Message = "Copilot CLI notification delivery failed.")]
    public static partial void CopilotCliNotificationFailed(
        ILogger logger,
        Exception exception);

    [LoggerMessage(
        EventId = 1803,
        EventName = nameof(CopilotCliNotificationClaimBusy),
        Level = LogLevel.Information,
        Message =
            "Copilot CLI notification delivery is already in flight for session {SessionId} "
            + "and event {EventId}; returning a retryable result.")]
    public static partial void CopilotCliNotificationClaimBusy(
        ILogger logger,
        string sessionId,
        string eventId);
}
