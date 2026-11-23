using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.Runtime.ExceptionServices;
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
        long watchdogArmMs = elapsed.ElapsedMilliseconds;
        timeout.CancelAfter(TimeSpan.FromSeconds(command.DeadlineSeconds));
        using var process = new Process
        {
            StartInfo = new(command.Executable)
            {
                WorkingDirectory = command.Directory,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                RedirectStandardInput = command.CloseStandardInput,
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
        long startBeginMs;
        long startReturnMs;
        try
        {
            startBeginMs = elapsed.ElapsedMilliseconds;
            bool started = process.Start();
            startReturnMs = elapsed.ElapsedMilliseconds;
            if (!started)
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
        Task stdoutDrain = CaptureAsync(output, stdout, timeout.Token);
        Task stderrDrain = CaptureAsync(error, stderr, timeout.Token);
        Task exitWait = process.WaitForExitAsync(timeout.Token);
        Task[] tasks = [stdoutDrain, stderrDrain, exitWait];
        NativeTermination termination = NativeTermination.Exited;
        string? failure = null;
        bool closingInput = false;
        Task? awaited = null;
        try
        {
            if (command.CloseStandardInput)
            {
                closingInput = true;
                process.StandardInput.Close();
                closingInput = false;
            }
            var pending = new List<Task>(tasks);
            while (pending.Count != 0)
            {
                awaited = await Task.WhenAny(pending);
                await awaited;
                pending.Remove(awaited);
                awaited = null;
            }
        }
        catch (OperationCanceledException)
        {
            long cancellationObservedMs = elapsed.ElapsedMilliseconds;
            termination = token.IsCancellationRequested
                ? NativeTermination.Cancelled : NativeTermination.TimedOut;
            failure = termination == NativeTermination.Cancelled ? "Command cancelled."
                : $"Native command exceeded its {command.DeadlineSeconds}-second deadline.";
            if (termination == NativeTermination.TimedOut)
            {
                string observedAwait = awaited == exitWait ? "exit-wait"
                    : awaited == stdoutDrain ? "stdout-drain"
                    : awaited == stderrDrain ? "stderr-drain" : "unobserved";
                // These awaits share a token; the observed await does not identify a stall.
                failure += " Pre-cleanup timeout observation (monotonic ms since stopwatch start): "
                    + $"watchdog-arm={watchdogArmMs}, start-begin={startBeginMs}, "
                    + $"start-return={startReturnMs}, "
                    + $"cancellation-observed={cancellationObservedMs}; "
                    + $"observed-await={observedAwait}; exit-wait={Completion(exitWait)}, "
                    + $"stdout-drain={Completion(stdoutDrain)}, "
                    + $"stderr-drain={Completion(stderrDrain)}; "
                    + DescribeRoot(process) + ".";
            }
        }
        catch (Exception exception) when (exception is IOException or DecoderFallbackException ||
            closingInput && exception is InvalidOperationException)
        {
            termination = closingInput ? NativeTermination.InputFailed
                : NativeTermination.InvalidOutput;
            failure = closingInput ? "Native standard input could not close." : exception.Message;
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

    private static string Completion(Task task) => task.Status switch
    {
        TaskStatus.RanToCompletion => "succeeded",
        TaskStatus.Canceled => "canceled",
        TaskStatus.Faulted => "faulted",
        _ => "not-completed",
    };

    private static string DescribeRoot(Process process)
    {
        bool? exited = null;
        int? exitCode = null;
        try
        {
            exited = process.HasExited;
            if (exited == true) exitCode = process.ExitCode;
        }
        catch (Exception exception) when (exception is Win32Exception or InvalidOperationException)
        {
            // A failed observation must not replace the timeout or prevent cleanup.
        }

        string state = exited switch
        {
            true => "exited",
            false => "not-exited",
            null => "unavailable",
        };
        string code = exited == false ? "not-applicable"
            : exitCode?.ToString(CultureInfo.InvariantCulture) ?? "unavailable";
        return $"root={state}, root-exit-code={code}";
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
        string[] arguments, CancellationToken cancellationToken,
        IReadOnlyDictionary<string, string>? environment = null, TimeProvider? timeProvider = null)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var elapsed = Stopwatch.StartNew();
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(30),
            timeProvider ?? TimeProvider.System);
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken,
            deadline.Token);
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
        foreach ((string name, string value) in environment ?? new Dictionary<string, string>())
            process.StartInfo.Environment[name] = value;
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
        var stdoutText = new StringBuilder();
        var stderrText = new StringBuilder();
        Task<string> stdout = ReadAsync(output, stdoutText, timeout.Token);
        Task<string> stderr = ReadAsync(process.StandardError, stderrText, timeout.Token);
        Task exit = process.WaitForExitAsync(timeout.Token);
        Task[] tasks = [stdout, stderr, exit];
        ExceptionDispatchInfo? primary = null;
        Exception? cleanupFailure = null;
        bool deadlineExpired = false;
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
        catch (Exception exception)
        {
            deadlineExpired = exception is OperationCanceledException &&
                !cancellationToken.IsCancellationRequested;
            primary = ExceptionDispatchInfo.Capture(exception);
        }
        finally
        {
            await timeout.CancelAsync();
            try
            {
                await StopAsync(process);
            }
            catch (Exception exception)
            {
                cleanupFailure = exception;
            }
            finally
            {
                await Task.WhenAll(tasks).ConfigureAwait(ConfigureAwaitOptions.SuppressThrowing);
            }
        }
        var command = new NativeCommand(executable, directory, arguments, 30);
        int? code = process.HasExited ? process.ExitCode : null;
        if (deadlineExpired)
        {
            var result = new NativeCommandResult(NativeTermination.TimedOut, code,
                stdoutText.ToString(), stderrText.ToString(), elapsed.Elapsed.TotalSeconds,
                "Native query exceeded its 30-second deadline.");
            primary = ExceptionDispatchInfo.Capture(new TimeoutException(
                $"Native query ({NativeDiagnostic.Operation(command)}) failed: " +
                NativeDiagnostic.Describe(result, includeStdout: true),
                primary!.SourceException));
        }
        if (primary is not null && cleanupFailure is not null)
        {
            var failures = new AggregateException(primary.SourceException, cleanupFailure);
            throw new InvalidDataException(
                $"Native query ({NativeDiagnostic.Operation(command)}) failed: " +
                failures.Message, failures);
        }
        primary?.Throw();
        if (cleanupFailure is not null) ExceptionDispatchInfo.Capture(cleanupFailure).Throw();
        if (code != 0)
        {
            var result = new NativeCommandResult(NativeTermination.Exited, code,
                await stdout, await stderr, elapsed.Elapsed.TotalSeconds, null);
            throw new InvalidDataException(
                $"Native query ({NativeDiagnostic.Operation(command)}) failed: " +
                NativeDiagnostic.Describe(result, includeStdout: true));
        }
        return await stdout;
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

    private static async Task<string> ReadAsync(StreamReader reader, StringBuilder text,
        CancellationToken token)
    {
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
