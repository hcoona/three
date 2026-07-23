using Microsoft.Extensions.Logging;

namespace Hcoona.VsCodeCopilotTelegramHook.Logging;

internal static partial class AppLog
{
    [LoggerMessage(
        EventId = 1200,
        EventName = nameof(MissingTelegramCredentials),
        Level = LogLevel.Error,
        Message =
            "Telegram credentials are missing after checking environment variables "
            + "and gopass.")]
    public static partial void MissingTelegramCredentials(ILogger logger);

    [LoggerMessage(
        EventId = 1201,
        EventName = nameof(ResolvedTelegramCredentials),
        Level = LogLevel.Information,
        Message = "Resolved Telegram credentials from {CredentialSource}.")]
    public static partial void ResolvedTelegramCredentials(ILogger logger, string credentialSource);

    [LoggerMessage(
        EventId = 1202,
        EventName = nameof(GopassAvailabilityChecked),
        Level = LogLevel.Debug,
        Message = "gopass availability check returned {ExitCode}.")]
    public static partial void GopassAvailabilityChecked(ILogger logger, int exitCode);

    [LoggerMessage(
        EventId = 1203,
        EventName = nameof(GopassUnavailable),
        Level = LogLevel.Warning,
        Message = "gopass is not available on PATH.")]
    public static partial void GopassUnavailable(ILogger logger, Exception exception);

    [LoggerMessage(
        EventId = 1204,
        EventName = nameof(MissingCredentialInput),
        Level = LogLevel.Error,
        Message = "Telegram credential storage failed because one or more values are missing.")]
    public static partial void MissingCredentialInput(ILogger logger);

    [LoggerMessage(
        EventId = 1205,
        EventName = nameof(StoredTelegramCredentials),
        Level = LogLevel.Information,
        Message = "Stored Telegram credentials in gopass.")]
    public static partial void StoredTelegramCredentials(ILogger logger);

    [LoggerMessage(
        EventId = 1213,
        EventName = nameof(UsingExistingTelegramCredentials),
        Level = LogLevel.Information,
        Message = "Using existing stored Telegram credentials without overwriting them.")]
    public static partial void UsingExistingTelegramCredentials(ILogger logger);

    [LoggerMessage(
        EventId = 1207,
        EventName = nameof(RemovedTelegramCredentials),
        Level = LogLevel.Information,
        Message = "Removed stored Telegram secrets from gopass when present.")]
    public static partial void RemovedTelegramCredentials(ILogger logger);

    [LoggerMessage(
        EventId = 1208,
        EventName = nameof(MissingSecretValue),
        Level = LogLevel.Debug,
        Message = "gopass did not return a value for secret path {SecretPath}.")]
    public static partial void MissingSecretValue(ILogger logger, string secretPath);

    [LoggerMessage(
        EventId = 1209,
        EventName = nameof(ReadSecretFailed),
        Level = LogLevel.Warning,
        Message = "Failed to read Telegram secret path {SecretPath}.")]
    public static partial void ReadSecretFailed(
        ILogger logger,
        Exception exception,
        string secretPath);

    [LoggerMessage(
        EventId = 1210,
        EventName = nameof(StoreSecretFailed),
        Level = LogLevel.Error,
        Message = "Failed to store Telegram secret at {SecretPath}. stderr={StandardError}")]
    public static partial void StoreSecretFailed(
        ILogger logger,
        string secretPath,
        string standardError);

    [LoggerMessage(
        EventId = 1211,
        EventName = nameof(SecretRemovalCompleted),
        Level = LogLevel.Debug,
        Message = "gopass removal for secret path {SecretPath} exited with code {ExitCode}.")]
    public static partial void SecretRemovalCompleted(
        ILogger logger,
        string secretPath,
        int exitCode);

    [LoggerMessage(
        EventId = 1212,
        EventName = nameof(RemoveSecretFailed),
        Level = LogLevel.Warning,
        Message = "Failed to remove Telegram secret path {SecretPath}.")]
    public static partial void RemoveSecretFailed(
        ILogger logger,
        Exception exception,
        string secretPath);
}
