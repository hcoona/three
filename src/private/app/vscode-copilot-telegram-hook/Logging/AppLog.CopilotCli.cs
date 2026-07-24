using Microsoft.Extensions.Logging;

namespace Hcoona.VsCodeCopilotTelegramHook.Logging;

internal static partial class AppLog
{
    [LoggerMessage(
        EventId = 1700,
        Level = LogLevel.Information,
        Message =
            "Sending {MessageCount} Telegram message(s) for Copilot CLI event "
            + "{EventType} ({EventId}).")]
    public static partial void SendingCopilotCliNotification(
        ILogger logger,
        int messageCount,
        string eventType,
        string eventId);

    [LoggerMessage(
        EventId = 1701,
        Level = LogLevel.Warning,
        Message =
            "A Copilot CLI notification was partially delivered and will not be retried.")]
    public static partial void PartialCopilotCliNotification(
        ILogger logger,
        Exception exception);

    [LoggerMessage(
        EventId = 1702,
        Level = LogLevel.Error,
        Message = "Discarding an invalid Copilot CLI event file.")]
    public static partial void InvalidCopilotCliEventFile(
        ILogger logger,
        Exception exception);

    [LoggerMessage(
        EventId = 1703,
        Level = LogLevel.Error,
        Message = "Copilot CLI notification delivery failed.")]
    public static partial void CopilotCliNotificationFailed(
        ILogger logger,
        Exception exception);
}
