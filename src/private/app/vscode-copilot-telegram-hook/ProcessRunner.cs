using System.ComponentModel;
using System.Diagnostics;
using Hcoona.VsCodeCopilotTelegramHook.Logging;
using Microsoft.Extensions.Logging;

namespace Hcoona.VsCodeCopilotTelegramHook;

internal interface IProcessRunner
{
    Task<ProcessExecutionResult> RunAsync(
        string fileName,
        IReadOnlyList<string> arguments,
        string? workingDirectory,
        string? standardInput,
        ProcessLogOptions? logOptions,
        CancellationToken cancellationToken);
}

internal sealed class ProcessRunner(ILogger<ProcessRunner> logger) : IProcessRunner
{
    private const int MaxLoggedStandardErrorLength = 300;

    public async Task<ProcessExecutionResult> RunAsync(
        string fileName,
        IReadOnlyList<string> arguments,
        string? workingDirectory,
        string? standardInput,
        ProcessLogOptions? logOptions,
        CancellationToken cancellationToken)
    {
        using Process process = new();
        ProcessLogOptions effectiveLogOptions = logOptions ?? new();

        ProcessStartInfo startInfo = new()
        {
            FileName = fileName,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            RedirectStandardInput = standardInput is not null,
        };

        if (!string.IsNullOrWhiteSpace(workingDirectory))
        {
            startInfo.WorkingDirectory = workingDirectory;
        }

        foreach (string argument in arguments)
        {
            startInfo.ArgumentList.Add(argument);
        }

        process.StartInfo = startInfo;

        string renderedArguments = effectiveLogOptions.IncludeArgumentsInLogs
            ? string.Join(' ', arguments)
            : "<redacted>";
        string renderedWorkingDirectory = effectiveLogOptions.IncludeWorkingDirectoryInLogs
            ? workingDirectory ?? "<default>"
            : "<redacted>";

        try
        {
            AppLog.StartingProcess(
                logger,
                fileName,
                renderedArguments,
                renderedWorkingDirectory);
            process.Start();
        }
        catch (Exception ex) when (ex is InvalidOperationException or Win32Exception)
        {
            AppLog.ProcessStartFailed(
                logger,
                ex,
                fileName,
                renderedArguments);
            throw new InvalidOperationException($"Failed to start process '{fileName}'.", ex);
        }

        Task<string> standardOutputTask = process.StandardOutput.ReadToEndAsync(cancellationToken);
        Task<string> standardErrorTask = process.StandardError.ReadToEndAsync(cancellationToken);

        if (standardInput is not null)
        {
            await process.StandardInput.WriteAsync(standardInput.AsMemory(), cancellationToken);
            await process.StandardInput.FlushAsync(cancellationToken);
            process.StandardInput.Close();
        }

        try
        {
            await process.WaitForExitAsync(cancellationToken);
        }
        catch
        {
            AppLog.CancellingProcess(
                logger,
                fileName,
                renderedArguments);
            TryKill(process);
            throw;
        }

        string standardOutput = await standardOutputTask;
        string standardError = await standardErrorTask;
        AppLog.ProcessExited(
            logger,
            fileName,
            process.ExitCode);

        if (process.ExitCode != 0)
        {
            AppLog.ProcessFailed(
                logger,
                fileName,
                process.ExitCode,
                effectiveLogOptions.IncludeStandardErrorInLogs
                    ? TrimForLog(standardError)
                    : "<redacted>");
        }

        return new ProcessExecutionResult(process.ExitCode, standardOutput, standardError);
    }

    private static void TryKill(Process process)
    {
        try
        {
            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: true);
            }
        }
        catch (InvalidOperationException)
        {
        }
    }

    private static string TrimForLog(string value)
    {
        string trimmed = value.Trim();
        if (trimmed.Length <= MaxLoggedStandardErrorLength)
        {
            return trimmed;
        }

        return trimmed[..MaxLoggedStandardErrorLength] + "...";
    }
}
