using System.ComponentModel;
using System.Diagnostics;
using System.Text;

namespace WorkflowDelivery.Platform;

internal static class NativeProcess
{
    internal static async Task<string> RunAsync(string executable, string directory,
        string[] arguments, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(30));
        using var process = new Process
        {
            StartInfo = new(executable)
            {
                WorkingDirectory = directory,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true,
            },
        };
        foreach (string argument in arguments)
            process.StartInfo.ArgumentList.Add(argument);
        try
        {
            if (!process.Start())
                throw new InvalidDataException("Native query could not start.");
        }
        catch (Win32Exception exception)
        {
            throw new InvalidDataException("Native query executable is unavailable.", exception);
        }
        Task<string> stdout = ReadAsync(process.StandardOutput, timeout.Token);
        Task<string> stderr = ReadAsync(process.StandardError, timeout.Token);
        Task exit = process.WaitForExitAsync(timeout.Token);
        Task[] tasks = [stdout, stderr, exit];
        try
        {
            var pending = new List<Task>(tasks);
            while (pending.Count != 0)
            {
                Task completed = await Task.WhenAny(pending);
                await completed;
                pending.Remove(completed);
            }
            if (process.ExitCode != 0)
                throw new InvalidDataException(
                    $"Native query failed with exit code {process.ExitCode}.");
            return await stdout;
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            throw new TimeoutException("Native query exceeded its 30-second deadline.");
        }
        finally
        {
            await timeout.CancelAsync();
            try
            {
                await StopAsync(process);
            }
            finally
            {
                await Task.WhenAll(tasks).ConfigureAwait(ConfigureAwaitOptions.SuppressThrowing);
            }
        }
    }

    private static async Task StopAsync(Process process)
    {
        try
        {
            if (!process.HasExited)
                process.Kill(entireProcessTree: true);
        }
        catch (InvalidOperationException) when (process.HasExited) { }
        catch (Win32Exception) when (process.HasExited) { }
        // Kill is asynchronous. The canceled operation waiter is not an exit barrier.
        using var cleanup = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        try
        {
            await process.WaitForExitAsync(cleanup.Token);
        }
        catch (OperationCanceledException)
        {
            throw new TimeoutException("Native query cleanup exceeded its 5-second deadline.");
        }
    }

    private static async Task<string> ReadAsync(StreamReader reader, CancellationToken token)
    {
        var text = new StringBuilder();
        char[] buffer = new char[4096];
        int count;
        while ((count = await reader.ReadAsync(buffer.AsMemory(), token)) != 0)
        {
            if (text.Length + count > 32 * 1024 * 1024)
                throw new InvalidDataException(
                    "Native query output exceeded 33,554,432 characters.");
            text.Append(buffer, 0, count);
        }
        return text.ToString();
    }
}
