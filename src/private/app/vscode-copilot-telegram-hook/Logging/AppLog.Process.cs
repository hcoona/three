using Microsoft.Extensions.Logging;

namespace Hcoona.VsCodeCopilotTelegramHook.Logging;

internal static partial class AppLog
{
    [LoggerMessage(
        EventId = 1000,
        EventName = nameof(StartingProcess),
        Level = LogLevel.Debug,
        Message = "Starting process {FileName} {Arguments} in {WorkingDirectory}.")]
    public static partial void StartingProcess(
        ILogger logger,
        string fileName,
        string arguments,
        string workingDirectory);

    [LoggerMessage(
        EventId = 1001,
        EventName = nameof(ProcessStartFailed),
        Level = LogLevel.Error,
        Message = "Failed to start process {FileName} {Arguments}.")]
    public static partial void ProcessStartFailed(
        ILogger logger,
        Exception exception,
        string fileName,
        string arguments);

    [LoggerMessage(
        EventId = 1002,
        EventName = nameof(CancellingProcess),
        Level = LogLevel.Warning,
        Message = "Cancelling process {FileName} {Arguments}; attempting to terminate it.")]
    public static partial void CancellingProcess(
        ILogger logger,
        string fileName,
        string arguments);

    [LoggerMessage(
        EventId = 1003,
        EventName = nameof(ProcessExited),
        Level = LogLevel.Debug,
        Message = "Process {FileName} exited with code {ExitCode}.")]
    public static partial void ProcessExited(
        ILogger logger,
        string fileName,
        int exitCode);

    [LoggerMessage(
        EventId = 1004,
        EventName = nameof(ProcessFailed),
        Level = LogLevel.Warning,
        Message = "Process {FileName} failed with exit code {ExitCode}. stderr={StandardError}")]
    public static partial void ProcessFailed(
        ILogger logger,
        string fileName,
        int exitCode,
        string standardError);
}
