using System.Buffers;
using System.ComponentModel;
using System.Diagnostics;
using System.Text;

namespace Hcoona.AzureAuth.CredProvider.Platform.Processes;

public sealed class SystemProcessRunner : IProcessRunner
{
    internal static readonly TimeSpan TerminationCleanupTimeout = TimeSpan.FromSeconds(2);

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
        startSpec.ValidateForRun();
        cancellationToken.ThrowIfCancellationRequested();

        if (startSpec.PreStartValidation is not null)
        {
            await startSpec.PreStartValidation(cancellationToken).ConfigureAwait(false);
        }

        cancellationToken.ThrowIfCancellationRequested();

        using var process = new Process { StartInfo = CreateStartInfo(startSpec) };

        try
        {
            if (!process.Start())
            {
                return ProcessResult.LaunchFailure();
            }
        }
        catch (Win32Exception)
        {
            return ProcessResult.LaunchFailure();
        }
        catch (FileNotFoundException)
        {
            return ProcessResult.LaunchFailure();
        }
        catch (DirectoryNotFoundException)
        {
            return ProcessResult.LaunchFailure();
        }
        catch (UnauthorizedAccessException)
        {
            return ProcessResult.LaunchFailure();
        }
        catch (PlatformNotSupportedException)
        {
            return ProcessResult.LaunchFailure();
        }

        Task<ProcessOutputReadResult> standardOutputTask = ReadStreamAsync(
            process.StandardOutput.BaseStream,
            startSpec.OutputCaptureOptions.StandardOutputByteLimit,
            startSpec.OutputCaptureOptions.StandardOutputCharacterLimit
        );
        Task<ProcessOutputReadResult> standardErrorTask = ReadStreamAsync(
            process.StandardError.BaseStream,
            startSpec.OutputCaptureOptions.StandardErrorByteLimit,
            startSpec.OutputCaptureOptions.StandardErrorCharacterLimit
        );

        var cancellationSignal = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously
        );
        using CancellationTokenRegistration cancellationRegistration = cancellationToken.Register(
            static state => ((TaskCompletionSource)state!).TrySetResult(),
            cancellationSignal
        );
        using var timeoutTimerCancellation = new CancellationTokenSource();
        Task timeoutTask = Task.Delay(
            startSpec.Timeout!.Value,
            timeoutTimerCancellation.Token
        );
        var cleanupCompleted = false;
        Task? standardInputTask = null;

        try
        {
            StreamWriter standardInput = process.StandardInput;
            standardInputTask = Task.Run(
                () =>
                    WriteAndCloseStandardInputAsync(
                        standardInput,
                        startSpec.StandardInput,
                        cancellationToken
                    ),
                CancellationToken.None
            );
            Task waitForExitTask = process.WaitForExitAsync(CancellationToken.None);

            Task? standardInputMonitor = standardInputTask;
            Task? exitMonitor = waitForExitTask;
            Task<ProcessOutputReadResult>? standardOutputMonitor = standardOutputTask;
            Task<ProcessOutputReadResult>? standardErrorMonitor = standardErrorTask;

            while (
                standardInputMonitor is not null
                || exitMonitor is not null
                || standardOutputMonitor is not null
                || standardErrorMonitor is not null
            )
            {
                var tasks = new List<Task>(6) { cancellationSignal.Task, timeoutTask };
                if (standardInputMonitor is not null)
                {
                    tasks.Add(standardInputMonitor);
                }

                if (exitMonitor is not null)
                {
                    tasks.Add(exitMonitor);
                }

                if (standardOutputMonitor is not null)
                {
                    tasks.Add(standardOutputMonitor);
                }

                if (standardErrorMonitor is not null)
                {
                    tasks.Add(standardErrorMonitor);
                }

                Task completed = await Task.WhenAny(tasks).ConfigureAwait(false);
                if (cancellationSignal.Task.IsCompleted)
                {
                    await TerminateStartedProcessAsync(
                            process,
                            standardInputTask,
                            standardOutputTask,
                            standardErrorTask
                        )
                        .ConfigureAwait(false);
                    cleanupCompleted = true;
                    throw new OperationCanceledException(cancellationToken);
                }

                if (timeoutTask.IsCompleted)
                {
                    cleanupCompleted = true;
                    return await TerminateAndFinalizeAsync(
                            process,
                            standardInputTask,
                            standardOutputTask,
                            standardErrorTask,
                            ProcessExecutionStatus.TimedOut
                        )
                        .ConfigureAwait(false);
                }

                if (completed == standardInputMonitor)
                {
                    await standardInputMonitor.ConfigureAwait(false);
                    standardInputMonitor = null;
                    continue;
                }

                if (completed == exitMonitor)
                {
                    await exitMonitor.ConfigureAwait(false);
                    exitMonitor = null;
                    continue;
                }

                if (completed == standardOutputMonitor)
                {
                    ProcessOutputReadResult outputResult =
                        await standardOutputMonitor.ConfigureAwait(false);
                    standardOutputMonitor = null;
                    if (outputResult.Status != ProcessOutputReadStatus.Completed)
                    {
                        cleanupCompleted = true;
                        return await TerminateAndFinalizeAsync(
                                process,
                                standardInputTask,
                                standardOutputTask,
                                standardErrorTask,
                                MapReadStatus(outputResult.Status)
                            )
                            .ConfigureAwait(false);
                    }

                    continue;
                }

                if (completed == standardErrorMonitor)
                {
                    ProcessOutputReadResult errorResult =
                        await standardErrorMonitor.ConfigureAwait(false);
                    standardErrorMonitor = null;
                    if (errorResult.Status != ProcessOutputReadStatus.Completed)
                    {
                        cleanupCompleted = true;
                        return await TerminateAndFinalizeAsync(
                                process,
                                standardInputTask,
                                standardOutputTask,
                                standardErrorTask,
                                MapReadStatus(errorResult.Status)
                            )
                            .ConfigureAwait(false);
                    }
                }
            }

            ProcessOutputReadResult standardOutput =
                await standardOutputTask.ConfigureAwait(false);
            ProcessOutputReadResult standardError = await standardErrorTask.ConfigureAwait(false);
            cleanupCompleted = true;
            return new ProcessResult(process.ExitCode, standardOutput.Content, standardError.Content);
        }
        catch
        {
            if (!cleanupCompleted)
            {
                await TerminateStartedProcessAsync(
                        process,
                        standardInputTask,
                        standardOutputTask,
                        standardErrorTask
                    )
                    .ConfigureAwait(false);
            }

            throw;
        }
        finally
        {
            timeoutTimerCancellation.Cancel();
            await ObserveTaskExceptionAsync(timeoutTask).ConfigureAwait(false);
        }
    }

    private static async Task WriteAndCloseStandardInputAsync(
        StreamWriter standardInput,
        string? content,
        CancellationToken cancellationToken
    )
    {
        try
        {
            if (content is not null)
            {
                await standardInput
                    .WriteAsync(content.AsMemory(), cancellationToken)
                    .ConfigureAwait(false);
                await standardInput.FlushAsync(cancellationToken).ConfigureAwait(false);
            }
        }
        finally
        {
            standardInput.Close();
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

        foreach (string argument in startSpec.Arguments)
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

    private static ProcessExecutionStatus MapReadStatus(ProcessOutputReadStatus status) =>
        status switch
        {
            ProcessOutputReadStatus.TooLarge => ProcessExecutionStatus.OutputTooLarge,
            ProcessOutputReadStatus.InvalidOutput => ProcessExecutionStatus.InvalidOutput,
            _ => throw new ArgumentOutOfRangeException(
                nameof(status),
                status,
                "Unsupported read completion status."
            ),
        };

    private static async Task<ProcessResult> TerminateAndFinalizeAsync(
        Process process,
        Task standardInputTask,
        Task<ProcessOutputReadResult> standardOutputTask,
        Task<ProcessOutputReadResult> standardErrorTask,
        ProcessExecutionStatus terminationStatus
    )
    {
        KillProcessTree(process);
        await WaitForBoundedCleanupAsync(
                process,
                standardInputTask,
                standardOutputTask,
                standardErrorTask
            )
            .ConfigureAwait(false);
        ProcessOutputReadResult standardOutput = GetCompletedReadResult(standardOutputTask);
        ProcessOutputReadResult standardError = GetCompletedReadResult(standardErrorTask);
        int? exitCode = TryGetExitCode(process);

        return terminationStatus switch
        {
            ProcessExecutionStatus.Canceled => ProcessResult.Canceled(
                standardOutput.Content,
                standardError.Content,
                exitCode
            ),
            ProcessExecutionStatus.TimedOut => ProcessResult.TimedOut(
                standardOutput.Content,
                standardError.Content,
                exitCode
            ),
            ProcessExecutionStatus.OutputTooLarge => ProcessResult.OutputTooLarge(
                standardOutput.Content,
                standardError.Content,
                exitCode
            ),
            ProcessExecutionStatus.InvalidOutput => ProcessResult.InvalidOutput(
                standardOutput.Content,
                standardError.Content,
                exitCode
            ),
            _ => throw new ArgumentOutOfRangeException(
                nameof(terminationStatus),
                terminationStatus,
                "Unsupported termination status."
            ),
        };
    }

    private static async Task TerminateStartedProcessAsync(
        Process process,
        Task? standardInputTask,
        Task<ProcessOutputReadResult> standardOutputTask,
        Task<ProcessOutputReadResult> standardErrorTask
    )
    {
        KillProcessTree(process);
        await WaitForBoundedCleanupAsync(
                process,
                standardInputTask,
                standardOutputTask,
                standardErrorTask
            )
            .ConfigureAwait(false);
    }

    private static void KillProcessTree(Process process)
    {
        try
        {
            process.Kill(entireProcessTree: true);
        }
        catch (InvalidOperationException) { }
        catch (NotSupportedException) { }
        catch (Win32Exception) { }
    }

    private static async Task WaitForBoundedCleanupAsync(
        Process process,
        Task? standardInputTask,
        Task<ProcessOutputReadResult> standardOutputTask,
        Task<ProcessOutputReadResult> standardErrorTask
    )
    {
        Task exitTask = WaitForExitAfterTerminationAsync(process);
        Task cleanupTask = Task.WhenAll(
            ObserveTaskExceptionAsync(exitTask),
            ObserveTaskExceptionAsync(standardInputTask ?? Task.CompletedTask),
            ObserveTaskExceptionAsync(standardOutputTask),
            ObserveTaskExceptionAsync(standardErrorTask)
        );
        using var cleanupTimerCancellation = new CancellationTokenSource();
        Task cleanupTimer = Task.Delay(TerminationCleanupTimeout, cleanupTimerCancellation.Token);
        Task completed = await Task.WhenAny(cleanupTask, cleanupTimer).ConfigureAwait(false);
        if (completed == cleanupTask)
        {
            cleanupTimerCancellation.Cancel();
            await ObserveTaskExceptionAsync(cleanupTimer).ConfigureAwait(false);
            await cleanupTask.ConfigureAwait(false);
            return;
        }

        _ = cleanupTask.ContinueWith(
            static task => _ = task.Exception,
            CancellationToken.None,
            TaskContinuationOptions.OnlyOnFaulted | TaskContinuationOptions.ExecuteSynchronously,
            TaskScheduler.Default
        );
    }

    private static async Task WaitForExitAfterTerminationAsync(Process process)
    {
        try
        {
            await process.WaitForExitAsync(CancellationToken.None).ConfigureAwait(false);
        }
        catch (InvalidOperationException) { }
    }

    private static ProcessOutputReadResult GetCompletedReadResult(
        Task<ProcessOutputReadResult> task
    ) =>
        task.IsCompletedSuccessfully
            ? task.Result
            : new ProcessOutputReadResult(ProcessOutputReadStatus.Completed, string.Empty);

    private static int? TryGetExitCode(Process process)
    {
        try
        {
            return process.HasExited ? process.ExitCode : null;
        }
        catch (InvalidOperationException)
        {
            return null;
        }
    }

    private static async Task ObserveTaskExceptionAsync(Task task)
    {
        try
        {
            await task.ConfigureAwait(false);
        }
        catch (Exception) { }
    }

    private static async Task<ProcessOutputReadResult> ReadStreamAsync(
        Stream stream,
        int? byteLimit,
        int? characterLimit
    )
    {
        byte[] byteBuffer = ArrayPool<byte>.Shared.Rent(4096);
        char[] charBuffer = ArrayPool<char>.Shared.Rent(
            RedirectedStreamEncoding.GetMaxCharCount(byteBuffer.Length)
        );
        Decoder decoder = RedirectedStreamEncoding.GetDecoder();
        var builder = new StringBuilder();
        int observedBytes = 0;
        int observedCharacters = 0;

        try
        {
            while (true)
            {
                int bytesRead = await stream
                    .ReadAsync(byteBuffer.AsMemory(0, byteBuffer.Length), CancellationToken.None)
                    .ConfigureAwait(false);
                if (bytesRead == 0)
                {
                    break;
                }

                observedBytes += bytesRead;
                if (byteLimit is { } maxBytes && observedBytes > maxBytes)
                {
                    return new ProcessOutputReadResult(
                        ProcessOutputReadStatus.TooLarge,
                        builder.ToString()
                    );
                }

                int charactersRead;
                try
                {
                    charactersRead = decoder.GetChars(
                        byteBuffer,
                        0,
                        bytesRead,
                        charBuffer,
                        0,
                        flush: false
                    );
                }
                catch (DecoderFallbackException)
                {
                    return new ProcessOutputReadResult(
                        ProcessOutputReadStatus.InvalidOutput,
                        builder.ToString()
                    );
                }

                if (
                    TryAppendWithinCharacterLimit(
                        builder,
                        charBuffer.AsSpan(0, charactersRead),
                        ref observedCharacters,
                        characterLimit
                    )
                )
                {
                    return new ProcessOutputReadResult(
                        ProcessOutputReadStatus.TooLarge,
                        builder.ToString()
                    );
                }
            }

            int remainingCharacters;
            try
            {
                remainingCharacters = decoder.GetChars(Array.Empty<byte>(), 0, 0, charBuffer, 0, flush: true);
            }
            catch (DecoderFallbackException)
            {
                return new ProcessOutputReadResult(
                    ProcessOutputReadStatus.InvalidOutput,
                    builder.ToString()
                );
            }

            if (
                TryAppendWithinCharacterLimit(
                    builder,
                    charBuffer.AsSpan(0, remainingCharacters),
                    ref observedCharacters,
                    characterLimit
                )
            )
            {
                return new ProcessOutputReadResult(
                    ProcessOutputReadStatus.TooLarge,
                    builder.ToString()
                );
            }

            return new ProcessOutputReadResult(ProcessOutputReadStatus.Completed, builder.ToString());
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(byteBuffer);
            ArrayPool<char>.Shared.Return(charBuffer);
        }
    }

    private static bool TryAppendWithinCharacterLimit(
        StringBuilder builder,
        ReadOnlySpan<char> characters,
        ref int observedCharacters,
        int? characterLimit
    )
    {
        if (characters.Length == 0)
        {
            return false;
        }

        if (characterLimit is not { } maxCharacters)
        {
            builder.Append(characters);
            observedCharacters += characters.Length;
            return false;
        }

        int remainingCharacters = maxCharacters - observedCharacters;
        if (remainingCharacters <= 0)
        {
            return true;
        }

        if (characters.Length > remainingCharacters)
        {
            builder.Append(characters[..remainingCharacters]);
            observedCharacters = maxCharacters;
            return true;
        }

        builder.Append(characters);
        observedCharacters += characters.Length;
        return false;
    }

    private enum ProcessOutputReadStatus
    {
        Completed = 0,
        TooLarge = 1,
        InvalidOutput = 2,
    }

    private sealed record ProcessOutputReadResult(ProcessOutputReadStatus Status, string Content);
}
