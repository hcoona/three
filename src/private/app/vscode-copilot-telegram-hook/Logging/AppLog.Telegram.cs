using Microsoft.Extensions.Logging;

namespace Hcoona.VsCodeCopilotTelegramHook.Logging;

internal static partial class AppLog
{
    [LoggerMessage(
        EventId = 1400,
        EventName = nameof(SendingTelegramAttempt),
        Level = LogLevel.Debug,
        Message =
            "Sending Telegram message attempt {Attempt} using credentials from "
            + "{CredentialSource}; payload length is {MessageLength}.")]
    public static partial void SendingTelegramAttempt(
        ILogger logger,
        int attempt,
        string credentialSource,
        int messageLength);

    [LoggerMessage(
        EventId = 1401,
        EventName = nameof(SentTelegramAttempt),
        Level = LogLevel.Information,
        Message = "Sent Telegram message successfully on attempt {Attempt}.")]
    public static partial void SentTelegramAttempt(ILogger logger, int attempt);

    [LoggerMessage(
        EventId = 1402,
        EventName = nameof(RetryingTelegramSend),
        Level = LogLevel.Warning,
        Message =
            "Retrying Telegram send after attempt {Attempt}. status={StatusCode}, "
            + "errorCode={ErrorCode}, delayMs={DelayMilliseconds}")]
    public static partial void RetryingTelegramSend(
        ILogger logger,
        int attempt,
        int statusCode,
        int? errorCode,
        double delayMilliseconds);

    [LoggerMessage(
        EventId = 1403,
        EventName = nameof(TelegramDeliveryFailed),
        Level = LogLevel.Error,
        Message =
            "Telegram delivery failed on attempt {Attempt}. status={StatusCode}, "
            + "errorCode={ErrorCode}, description={Description}")]
    public static partial void TelegramDeliveryFailed(
        ILogger logger,
        int attempt,
        int statusCode,
        int? errorCode,
        string description);
}
