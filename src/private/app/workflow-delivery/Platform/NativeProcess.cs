using System.ComponentModel;
using System.Diagnostics;
using System.Text;

namespace WorkflowDelivery.Platform;

internal static class NativeProcess
{
    internal static async Task<NativeCommandResult> ExecuteAsync(NativeCommand command,
        CancellationToken token)
    {
        ArgumentNullException.ThrowIfNull(command);
        if (command.DeadlineSeconds <= 0)
            throw new ArgumentException("A native command requires a positive deadline.");
        var elapsed = Stopwatch.StartNew();
        if (token.IsCancellationRequested)
            return new(NativeTermination.Cancelled, null, "", "", 0, "Command cancelled.");
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(token);
        timeout.CancelAfter(TimeSpan.FromSeconds(command.DeadlineSeconds));
        using var process = new Process
        {
            StartInfo = new(command.Executable)
            {
                WorkingDirectory = command.Directory,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true,
            },
        };
        foreach (string argument in command.Arguments)
            process.StartInfo.ArgumentList.Add(argument);
        if (!command.InheritEnvironment) process.StartInfo.Environment.Clear();
        if (command.Environment is not null)
            foreach ((string name, string? value) in command.Environment)
            {
                if (value is null) process.StartInfo.Environment.Remove(name);
                else process.StartInfo.Environment[name] = value;
            }
        try
        {
            if (!process.Start())
                return new(NativeTermination.StartFailed, null, "", "",
                    elapsed.Elapsed.TotalSeconds, "Native command could not start.");
        }
        catch (Exception exception) when (exception is Win32Exception or
            InvalidOperationException)
        {
            return new(NativeTermination.StartFailed, null, "", "", elapsed.Elapsed.TotalSeconds,
                "Native command could not start: " + exception.Message);
        }

        using var output = StrictReader(process.StandardOutput.BaseStream);
        using var error = StrictReader(process.StandardError.BaseStream);
        var stdout = new StringBuilder();
        var stderr = new StringBuilder();
        Task[] tasks = [CaptureAsync(output, stdout, timeout.Token),
            CaptureAsync(error, stderr, timeout.Token), process.WaitForExitAsync(timeout.Token)];
        NativeTermination termination = NativeTermination.Exited;
        string? failure = null;
        try
        {
            var pending = new List<Task>(tasks);
            while (pending.Count != 0)
            {
                Task completed = await Task.WhenAny(pending);
                await completed;
                pending.Remove(completed);
            }
        }
        catch (OperationCanceledException)
        {
            termination = token.IsCancellationRequested
                ? NativeTermination.Cancelled : NativeTermination.TimedOut;
            failure = termination == NativeTermination.Cancelled ? "Command cancelled."
                : $"Native command exceeded its {command.DeadlineSeconds}-second deadline.";
        }
        catch (Exception exception) when (exception is IOException or DecoderFallbackException)
        {
            termination = NativeTermination.InvalidOutput;
            failure = exception.Message;
        }
        finally
        {
            await timeout.CancelAsync();
            try
            {
                await StopAsync(process);
            }
            catch (Exception exception) when (exception is TimeoutException or Win32Exception or
                InvalidOperationException)
            {
                termination = NativeTermination.CleanupFailed;
                failure = (failure is null ? "" : failure + " ") + exception.Message;
            }
            finally
            {
                await Task.WhenAll(tasks).ConfigureAwait(ConfigureAwaitOptions.SuppressThrowing);
            }
        }
        int? code = process.HasExited ? process.ExitCode : null;
        return new(termination, code, stdout.ToString(), stderr.ToString(),
            elapsed.Elapsed.TotalSeconds, failure);
    }

    private static StreamReader StrictReader(Stream stream) => new(stream,
        new UTF8Encoding(false, true), detectEncodingFromByteOrderMarks: false, leaveOpen: true);

    private static async Task CaptureAsync(StreamReader reader, StringBuilder text,
        CancellationToken token)
    {
        char[] buffer = new char[4096];
        int count;
        while ((count = await reader.ReadAsync(buffer.AsMemory(), token)) != 0)
        {
            int remaining = 32 * 1024 * 1024 - text.Length;
            text.Append(buffer, 0, Math.Min(count, remaining));
            if (count > remaining)
                throw new IOException("Native command output exceeded 33,554,432 characters.");
        }
    }

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
        // Process's built-in reader detects BOMs even with a supplied encoding.
        // Native path output must retain an initial U+FEFF and reject invalid bytes.
        using var output = new StreamReader(process.StandardOutput.BaseStream,
            new UTF8Encoding(false, true), detectEncodingFromByteOrderMarks: false,
            leaveOpen: true);
        Task<string> stdout = ReadAsync(output, timeout.Token);
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
