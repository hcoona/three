using Microsoft.Extensions.Logging;

namespace Hcoona.VsCodeCopilotTelegramHook.Logging;

internal static partial class AppLog
{
    [LoggerMessage(
        EventId = 1600,
        EventName = nameof(StartingUserInstall),
        Level = LogLevel.Information,
        Message = "Starting user install command.")]
    public static partial void StartingUserInstall(ILogger logger);

    [LoggerMessage(
        EventId = 1601,
        EventName = nameof(CompletedUserInstall),
        Level = LogLevel.Information,
        Message = "Completed user install command under {InstallRoot}; success={Succeeded}.")]
    public static partial void CompletedUserInstall(
        ILogger logger,
        string installRoot,
        bool succeeded);

    [LoggerMessage(
        EventId = 1602,
        EventName = nameof(UserInstallFailed),
        Level = LogLevel.Error,
        Message = "User install command failed.")]
    public static partial void UserInstallFailed(ILogger logger, Exception exception);

    [LoggerMessage(
        EventId = 1603,
        EventName = nameof(StartingUserUninstall),
        Level = LogLevel.Information,
        Message = "Starting user uninstall command.")]
    public static partial void StartingUserUninstall(ILogger logger);

    [LoggerMessage(
        EventId = 1604,
        EventName = nameof(UserUninstallFailed),
        Level = LogLevel.Error,
        Message = "User uninstall command failed.")]
    public static partial void UserUninstallFailed(ILogger logger, Exception exception);

    [LoggerMessage(
        EventId = 1605,
        EventName = nameof(StartingUserHealth),
        Level = LogLevel.Information,
        Message = "Starting user health command.")]
    public static partial void StartingUserHealth(ILogger logger);

    [LoggerMessage(
        EventId = 1606,
        EventName = nameof(UserHealthFailed),
        Level = LogLevel.Error,
        Message = "User health command failed.")]
    public static partial void UserHealthFailed(ILogger logger, Exception exception);

    [LoggerMessage(
        EventId = 1607,
        EventName = nameof(StartingUserDiagnose),
        Level = LogLevel.Information,
        Message = "Starting user diagnose command.")]
    public static partial void StartingUserDiagnose(ILogger logger);

    [LoggerMessage(
        EventId = 1608,
        EventName = nameof(UserDiagnoseFailed),
        Level = LogLevel.Error,
        Message = "User diagnose command failed.")]
    public static partial void UserDiagnoseFailed(ILogger logger, Exception exception);

    [LoggerMessage(
        EventId = 1609,
        EventName = nameof(StartingTestNotification),
        Level = LogLevel.Information,
        Message = "Starting test-notification command.")]
    public static partial void StartingTestNotification(ILogger logger);

    [LoggerMessage(
        EventId = 1610,
        EventName = nameof(TestNotificationFailed),
        Level = LogLevel.Error,
        Message = "Test-notification command failed.")]
    public static partial void TestNotificationFailed(ILogger logger, Exception exception);

    [LoggerMessage(
        EventId = 1611,
        EventName = nameof(CompletedUserUninstall),
        Level = LogLevel.Information,
        Message = "Completed user uninstall command under {InstallRoot}; success={Succeeded}.")]
    public static partial void CompletedUserUninstall(
        ILogger logger,
        string installRoot,
        bool succeeded);

    [LoggerMessage(
        EventId = 1612,
        EventName = nameof(CompletedUserHealth),
        Level = LogLevel.Information,
        Message = "Completed user health command under {InstallRoot}; healthy={IsHealthy}.")]
    public static partial void CompletedUserHealth(
        ILogger logger,
        string installRoot,
        bool isHealthy);

    [LoggerMessage(
        EventId = 1613,
        EventName = nameof(CompletedUserDiagnose),
        Level = LogLevel.Information,
        Message = "Completed user diagnose command under {InstallRoot}.")]
    public static partial void CompletedUserDiagnose(ILogger logger, string installRoot);

    [LoggerMessage(
        EventId = 1614,
        EventName = nameof(CompletedTestNotification),
        Level = LogLevel.Information,
        Message = "Completed test-notification command successfully.")]
    public static partial void CompletedTestNotification(ILogger logger);
}
