using System.Diagnostics;
using System.Globalization;
using System.Runtime.InteropServices;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace WorkflowDeliveryV3NuGetAuthority.Tests;

internal sealed record AuthorityProcessOptions
{
    public TimeSpan ProcessTimeout { get; init; } = TimeSpan.FromMinutes(2);
    public TimeSpan DumpTimeout { get; init; } = TimeSpan.FromSeconds(30);
    public TimeSpan SlowProcessThreshold { get; init; } = TimeSpan.FromSeconds(30);
    public string? DiagnosticsDirectory { get; init; }
    public string? DumpTool { get; init; }
    // Regression tests can expire deadlines after explicit child readiness.
    public CancellationToken ExecutionDeadlineToken { get; init; }
    public CancellationToken CollectionDeadlineToken { get; init; }
}

internal static class AuthorityProcess
{
    private static readonly TimeSpan CleanupTimeout = TimeSpan.FromSeconds(10);

    public static async Task<(int ExitCode, string Output, string Error)> RunAsync(
        ProcessStartInfo startInfo,
        string request,
        string family,
        TestContext testContext,
        AuthorityProcessOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        options ??= new AuthorityProcessOptions();
        string root = options.DiagnosticsDirectory
            ?? Environment.GetEnvironmentVariable("NUGET_AUTHORITY_DIAGNOSTICS_DIRECTORY")
            ?? Path.Combine(
                testContext.TestRunResultsDirectory ?? AppContext.BaseDirectory,
                "nuget-authority");
        string directory = Path.GetFullPath(Path.Combine(root, $"{family}-{Guid.NewGuid():N}"));
        Directory.CreateDirectory(directory);
        using var log = new StreamWriter(Path.Combine(directory, "process.log"))
        {
            AutoFlush = true,
        };
        var elapsed = Stopwatch.StartNew();
        void Record(string message)
        {
            try
            {
                lock (log)
                {
                    log.WriteLine(
                        $"{DateTimeOffset.UtcNow:O} "
                            + $"{elapsed.Elapsed.TotalMilliseconds:F3}ms {message}");
                }
            }
            catch (Exception error) when (error is IOException or ObjectDisposedException)
            {
                // A diagnostic write must not prevent termination of the child.
                testContext.WriteLine($"Diagnostic log unavailable ({error.Message}): {message}");
            }
        }

        Record($"Command: {startInfo.FileName} {string.Join(' ', startInfo.ArgumentList)}");
        Record($"OS: {RuntimeInformation.OSDescription}; "
            + $"architecture: {RuntimeInformation.ProcessArchitecture}; "
            + $"test runtime: {Environment.Version}; processors: {Environment.ProcessorCount}");
        string[] environmentNames =
        [
            "GITHUB_SHA", "GITHUB_RUN_ID", "GITHUB_RUN_ATTEMPT", "RUNNER_OS", "RUNNER_ARCH",
            "ImageOS", "ImageVersion", "DOTNET_ROOT",
        ];
        foreach (string name in environmentNames)
        {
            Record($"{name}={Environment.GetEnvironmentVariable(name)}");
        }

        // Trace only this helper, including startup before its managed entry point.
        startInfo.Environment["DOTNET_HOST_TRACE"] = "1";
        startInfo.Environment["DOTNET_HOST_TRACEFILE"] = Path.Combine(directory, "host.txt");
        startInfo.Environment["DOTNET_HOST_TRACE_VERBOSITY"] = "4";
        startInfo.Environment["DOTNET_EnableEventPipe"] = "1";
        startInfo.Environment["DOTNET_EventPipeOutputPath"] =
            Path.Combine(directory, "runtime.nettrace");
        startInfo.Environment["DOTNET_EventPipeOutputStreaming"] = "1";
        startInfo.Environment["DOTNET_EventPipeCircularMB"] = "10"; // Hexadecimal: 16 MiB.

        using var process = new Process { StartInfo = startInfo };
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(
            testContext.CancellationToken, cancellationToken);
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(
            cancellation.Token, options.ExecutionDeadlineToken);
        using var outputCancellation = new CancellationTokenSource();
        Task outputTask = Task.CompletedTask;
        Task errorTask = Task.CompletedTask;
        bool started = false;
        string stage = "starting process";
        string outputPath = Path.Combine(directory, "stdout.txt");
        string errorPath = Path.Combine(directory, "stderr.txt");
        try
        {
            Record("Launch requested");
            started = process.Start();
            if (!started)
            {
                throw new InvalidOperationException("NuGet authority did not start.");
            }

            Record($"Process started; PID={process.Id}; executable={process.MainModule?.FileName}");
            // A hosted Windows process can exceed 30 seconds. Retain a finite
            // execution budget and separate budgets for evidence and cleanup.
            timeout.CancelAfter(options.ProcessTimeout);
            outputTask = CaptureOutputAsync(
                process.StandardOutput, outputPath, Record, outputCancellation.Token);
            errorTask = CaptureOutputAsync(
                process.StandardError, errorPath, Record, outputCancellation.Token);
            stage = "writing request";
            await process.StandardInput.WriteAsync(request.AsMemory(), timeout.Token);
            Record("Request written");
            process.StandardInput.Close();
            Record("Stdin closed");
            stage = "waiting for process exit";
            await process.WaitForExitAsync(timeout.Token);
            Record($"Process exited; exit code={process.ExitCode}");
            stage = "draining stdout/stderr";
            await Task.WhenAll(outputTask, errorTask).WaitAsync(timeout.Token);
            if (elapsed.Elapsed >= options.SlowProcessThreshold)
            {
                Record("Slow successful invocation; retain diagnostics");
                await File.WriteAllTextAsync(
                    Path.Combine(directory, "slow-process.txt"),
                    $"PID={process.Id}; elapsed={elapsed.Elapsed}; "
                        + $"threshold={options.SlowProcessThreshold}",
                    cancellation.Token);
            }

            return (
                process.ExitCode,
                await File.ReadAllTextAsync(outputPath, cancellation.Token),
                await File.ReadAllTextAsync(errorPath, cancellation.Token));
        }
        catch (Exception failure)
        {
            Record($"Failure during {stage}: {failure}");
            try
            {
                if (started)
                {
                    RecordSnapshot(process, Record);
                    if (!process.HasExited
                        && !cancellation.IsCancellationRequested)
                    {
                        await CollectDumpAsync(
                            process.Id, directory, Record, options, cancellation.Token);
                    }
                }
            }
            catch (Exception diagnosticFailure)
            {
                Record($"Diagnostic collection failed: {diagnosticFailure}");
            }
            finally
            {
                if (started)
                {
                    await TerminateAsync(process, Record);
                }

                using var drainTimeout = new CancellationTokenSource(CleanupTimeout);
                try
                {
                    await Task.WhenAll(outputTask, errorTask).WaitAsync(drainTimeout.Token);
                }
                catch (Exception drainFailure)
                {
                    Record($"Output drain failed: {drainFailure}");
                }

                await StopCaptureAsync(outputTask, errorTask, outputCancellation, Record);
            }

            throw new AssertFailedException(
                $"NuGet authority failed during {stage} "
                    + $"(execution budget {options.ProcessTimeout}). "
                    + $"Diagnostics: {directory}."
                    + Environment.NewLine + $"stdout: {ReadOutput(outputPath)}"
                    + Environment.NewLine + $"stderr: {ReadOutput(errorPath)}",
                failure);
        }
        finally
        {
            testContext.WriteLine($"NuGet authority diagnostics: {directory}");
        }
    }

    private static async Task CaptureOutputAsync(
        StreamReader reader,
        string path,
        Action<string> record,
        CancellationToken cancellationToken)
    {
        using var writer = new StreamWriter(path) { AutoFlush = true };
        var buffer = new char[4096];
        bool first = true;
        int count;
        while ((count = await reader.ReadAsync(buffer, cancellationToken)) > 0)
        {
            if (first)
            {
                record($"{Path.GetFileName(path)} first output");
                first = false;
            }

            await writer.WriteAsync(buffer.AsMemory(0, count), cancellationToken);
        }

        record($"{Path.GetFileName(path)} EOF");
    }

    private static void RecordSnapshot(Process process, Action<string> record)
    {
        try
        {
            process.Refresh();
            record($"Snapshot: PID={process.Id}; exited={process.HasExited}; "
                + $"CPU={process.TotalProcessorTime}; working set={process.WorkingSet64}; "
                + $"threads={process.Threads.Count}");
            foreach (ProcessThread thread in process.Threads)
            {
                using (thread)
                {
                    record($"Thread {thread.Id}: state={thread.ThreadState}"
                        + (thread.ThreadState == System.Diagnostics.ThreadState.Wait
                            ? $"; wait={thread.WaitReason}" : string.Empty));
                }
            }
        }
        catch (Exception error)
        {
            record($"Process snapshot unavailable: {error.Message}");
        }
    }

    private static async Task CollectDumpAsync(
        int processId,
        string directory,
        Action<string> record,
        AuthorityProcessOptions options,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        string? tool = options.DumpTool
            ?? Environment.GetEnvironmentVariable("NUGET_AUTHORITY_DUMP_TOOL");
        if (string.IsNullOrEmpty(tool))
        {
            record("Dump not collected: NUGET_AUTHORITY_DUMP_TOOL is not configured.");
            return;
        }

        var startInfo = new ProcessStartInfo(tool)
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };
        string[] arguments =
        [
            "collect", "--process-id", processId.ToString(CultureInfo.InvariantCulture),
            "--type", "Heap", "--output", Path.Combine(directory, "hang.dmp"), "--diag",
        ];
        foreach (string argument in arguments)
        {
            startInfo.ArgumentList.Add(argument);
        }

        record($"Dump requested: {tool}; PID={processId}; budget={options.DumpTimeout}");
        using Process collector = Process.Start(startInfo)
            ?? throw new InvalidOperationException("Dump collector did not start.");
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(
            cancellationToken, options.CollectionDeadlineToken);
        timeout.CancelAfter(options.DumpTimeout);
        using var outputCancellation = new CancellationTokenSource();
        Task output = CaptureOutputAsync(
            collector.StandardOutput, Path.Combine(directory, "dump-stdout.txt"),
            record, outputCancellation.Token);
        Task error = CaptureOutputAsync(
            collector.StandardError, Path.Combine(directory, "dump-stderr.txt"),
            record, outputCancellation.Token);
        try
        {
            await collector.WaitForExitAsync(timeout.Token);
            await Task.WhenAll(output, error).WaitAsync(timeout.Token);
            record($"Dump collector exited: {collector.ExitCode}; "
                + $"dump exists={File.Exists(Path.Combine(directory, "hang.dmp"))}");
        }
        finally
        {
            await TerminateAsync(collector, record);
            await StopCaptureAsync(output, error, outputCancellation, record);
        }
    }

    private static async Task StopCaptureAsync(
        Task output,
        Task error,
        CancellationTokenSource cancellation,
        Action<string> record)
    {
        await cancellation.CancelAsync();
        try
        {
            await Task.WhenAll(output, error).WaitAsync(CleanupTimeout);
        }
        catch (Exception failure)
        {
            record($"Output capture stopped: stdout={output.Status}; "
                + $"stderr={error.Status}; {failure.Message}");
        }
    }

    private static async Task TerminateAsync(Process process, Action<string> record)
    {
        try
        {
            if (!process.HasExited)
            {
                record($"Terminating process tree: PID={process.Id}");
                try
                {
                    process.Kill(entireProcessTree: true);
                }
                catch (InvalidOperationException) when (process.HasExited)
                {
                    // The process exited between the check and termination.
                }
            }

            using var timeout = new CancellationTokenSource(CleanupTimeout);
            await process.WaitForExitAsync(timeout.Token);
            record($"Cleanup confirmed exit: PID={process.Id}; exit code={process.ExitCode}");
        }
        catch (Exception error)
        {
            record($"Process cleanup failed: {error}");
        }
    }

    private static string ReadOutput(string path)
    {
        try
        {
            if (!File.Exists(path))
            {
                return "<not created>";
            }

            string output = File.ReadAllText(path);
            return output.Length <= 4096
                ? output
                : output[..4096] + $"<truncated; complete output: {path}>";
        }
        catch (IOException error)
        {
            return $"<unavailable: {error.Message}>";
        }
    }
}
