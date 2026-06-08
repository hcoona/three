using System.ComponentModel;
using System.Diagnostics;
using System.Text;

namespace Hcoona.AzureAuth.CredProvider.Platform.Processes;

public sealed class SystemProcessRunner : IProcessRunner
{
    private static readonly Encoding RedirectedStreamEncoding = new UTF8Encoding(
        encoderShouldEmitUTF8Identifier: false,
        throwOnInvalidBytes: true
    );

    public async Task<ProcessResult> RunAsync(
        ProcessStartSpec startSpec,
        CancellationToken cancellationToken = default
    )
    {
        ArgumentNullException.ThrowIfNull(startSpec);
        cancellationToken.ThrowIfCancellationRequested();

        using var process = new Process { StartInfo = CreateStartInfo(startSpec) };

        if (startSpec.PreStartValidation is not null)
        {
            await startSpec.PreStartValidation(cancellationToken).ConfigureAwait(false);
        }

        cancellationToken.ThrowIfCancellationRequested();

        if (!process.Start())
        {
            throw new InvalidOperationException($"Failed to start process '{startSpec.FileName}'.");
        }

        var standardOutputTask = process.StandardOutput.ReadToEndAsync(cancellationToken);
        var standardErrorTask = process.StandardError.ReadToEndAsync(cancellationToken);

        try
        {
            if (startSpec.StandardInput is not null)
            {
                await process
                    .StandardInput.WriteAsync(startSpec.StandardInput.AsMemory(), cancellationToken)
                    .ConfigureAwait(false);
                await process.StandardInput.FlushAsync(cancellationToken).ConfigureAwait(false);
            }

            process.StandardInput.Close();

            await process.WaitForExitAsync(cancellationToken).ConfigureAwait(false);
            var standardOutput = await standardOutputTask.ConfigureAwait(false);
            var standardError = await standardErrorTask.ConfigureAwait(false);

            return new ProcessResult(process.ExitCode, standardOutput, standardError);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            await TerminateStartedProcessAsync(process, standardOutputTask, standardErrorTask)
                .ConfigureAwait(false);
            throw;
        }
        catch
        {
            await TerminateStartedProcessAsync(process, standardOutputTask, standardErrorTask)
                .ConfigureAwait(false);
            throw;
        }
    }

    private static ProcessStartInfo CreateStartInfo(ProcessStartSpec startSpec)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = startSpec.FileName,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            RedirectStandardInput = true,
            StandardOutputEncoding = RedirectedStreamEncoding,
            StandardErrorEncoding = RedirectedStreamEncoding,
            StandardInputEncoding = RedirectedStreamEncoding,
            CreateNoWindow = true,
        };

        if (startSpec.WorkingDirectory is not null)
        {
            startInfo.WorkingDirectory = startSpec.WorkingDirectory;
        }

        ApplyEnvironmentMode(startInfo, startSpec.EnvironmentMode);

        foreach (var argument in startSpec.Arguments)
        {
            startInfo.ArgumentList.Add(argument);
        }

        foreach (var variable in startSpec.Environment)
        {
            if (variable.Value is null)
            {
                startInfo.Environment.Remove(variable.Key);
            }
            else
            {
                startInfo.Environment[variable.Key] = variable.Value;
            }
        }

        return startInfo;
    }

    private static void ApplyEnvironmentMode(
        ProcessStartInfo startInfo,
        ProcessEnvironmentMode environmentMode
    )
    {
        switch (environmentMode)
        {
            case ProcessEnvironmentMode.Inherit:
                break;
            case ProcessEnvironmentMode.ExplicitOnly:
                startInfo.Environment.Clear();
                break;
            default:
                throw new ArgumentOutOfRangeException(
                    nameof(environmentMode),
                    environmentMode,
                    "Unsupported process environment mode."
                );
        }
    }

    private static async Task TerminateStartedProcessAsync(
        Process process,
        Task<string> standardOutputTask,
        Task<string> standardErrorTask
    )
    {
        KillProcessTree(process);
        await WaitForExitAfterTerminationAsync(process).ConfigureAwait(false);
        await ObserveTaskExceptionAsync(standardOutputTask).ConfigureAwait(false);
        await ObserveTaskExceptionAsync(standardErrorTask).ConfigureAwait(false);
    }

    private static void KillProcessTree(Process process)
    {
        try
        {
            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: true);
            }
        }
        catch (InvalidOperationException) { }
        catch (Win32Exception) { }
    }

    private static async Task WaitForExitAfterTerminationAsync(Process process)
    {
        try
        {
            await process.WaitForExitAsync(CancellationToken.None).ConfigureAwait(false);
        }
        catch (InvalidOperationException) { }
    }

    private static async Task ObserveTaskExceptionAsync(Task task)
    {
        try
        {
            await task.ConfigureAwait(false);
        }
        catch (Exception) { }
    }
}
