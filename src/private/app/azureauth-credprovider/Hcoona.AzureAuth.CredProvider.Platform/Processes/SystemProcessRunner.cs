using System.Buffers;
using System.ComponentModel;
using System.Diagnostics;
using System.Text;

namespace Hcoona.AzureAuth.CredProvider.Platform.Processes;

public sealed class SystemProcessRunner : IProcessRunner
{
    private static readonly TimeSpan ProcessCleanupTimeout = TimeSpan.FromSeconds(2);
    private static readonly Encoding RedirectedStreamEncoding = new UTF8Encoding(
        encoderShouldEmitUTF8Identifier: false,
        throwOnInvalidBytes: true
    );
    private readonly IProcessCleanupStrategy processCleanup;
    private readonly TimeSpan processCleanupTimeout;
    private readonly IProcessStartStrategy processStart;

    public SystemProcessRunner()
        : this(
            SystemProcessCleanupStrategy.Instance,
            ProcessCleanupTimeout,
            SystemProcessStartStrategy.Instance
        )
    { }

    internal SystemProcessRunner(
        IProcessCleanupStrategy processCleanup,
        TimeSpan processCleanupTimeout
    )
        : this(processCleanup, processCleanupTimeout, SystemProcessStartStrategy.Instance) { }

    internal SystemProcessRunner(IProcessStartStrategy processStart)
        : this(SystemProcessCleanupStrategy.Instance, ProcessCleanupTimeout, processStart) { }

    private SystemProcessRunner(
        IProcessCleanupStrategy processCleanup,
        TimeSpan processCleanupTimeout,
        IProcessStartStrategy processStart
    )
    {
        ArgumentNullException.ThrowIfNull(processCleanup);
        ArgumentNullException.ThrowIfNull(processStart);
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(
            processCleanupTimeout,
            TimeSpan.Zero
        );
        this.processCleanup = processCleanup;
        this.processCleanupTimeout = processCleanupTimeout;
        this.processStart = processStart;
    }

    public async Task<ProcessResult> RunAsync(
        ProcessStartSpec startSpec,
        CancellationToken cancellationToken = default
    )
    {
        ArgumentNullException.ThrowIfNull(startSpec);
        cancellationToken.ThrowIfCancellationRequested();

        using var timeoutCancellation = new CancellationTokenSource(startSpec.Timeout);
        using var executionCancellation = new CancellationTokenSource();
        var cancellationCause = (int)ProcessCancellationCause.None;
        using CancellationTokenRegistration callerCancellationRegistration =
            cancellationToken.Register(() =>
                CancelExecution(ProcessCancellationCause.Caller)
            );
        using CancellationTokenRegistration timeoutCancellationRegistration =
            timeoutCancellation.Token.Register(() =>
                CancelExecution(ProcessCancellationCause.Timeout)
            );

        void CancelExecution(ProcessCancellationCause cause)
        {
            Interlocked.CompareExchange(
                ref cancellationCause,
                (int)cause,
                (int)ProcessCancellationCause.None
            );
            executionCancellation.Cancel();
        }

        using var process = new Process { StartInfo = CreateStartInfo(startSpec) };
        try
        {
            if (!processStart.Start(process))
            {
                return ProcessResult.LaunchFailure();
            }
        }
        catch (Win32Exception)
        {
            return ProcessResult.LaunchFailure();
        }

        var standardOutput = new BoundedOutputCapture(
            startSpec.OutputCaptureOptions.StandardOutputByteLimit
        );
        var standardError = new BoundedOutputCapture(
            startSpec.OutputCaptureOptions.StandardErrorByteLimit,
            startSpec.StandardErrorTee
        );

        Task standardInputTask = WriteAndCloseStandardInputAsync(
            process.StandardInput,
            startSpec.StandardInput,
            executionCancellation.Token
        );
        Task standardOutputTask = standardOutput.ReadAsync(
            process.StandardOutput.BaseStream,
            executionCancellation
        );
        Task standardErrorTask = standardError.ReadAsync(
            process.StandardError.BaseStream,
            executionCancellation
        );
        Task waitForExitTask = process.WaitForExitAsync(executionCancellation.Token);
        Task allTasks = Task.WhenAll(
            standardInputTask,
            standardOutputTask,
            standardErrorTask,
            waitForExitTask
        );

        try
        {
            await allTasks.ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            bool outputTooLarge = standardOutput.TooLarge || standardError.TooLarge;
            bool invalidOutput = standardOutput.InvalidUtf8 || standardError.InvalidUtf8;
            bool teeFailed = standardError.TeeFailed;
            ProcessCancellationCause triggeringCancellationCause =
                (ProcessCancellationCause)Volatile.Read(ref cancellationCause);

            await KillAndWaitAsync(process).ConfigureAwait(false);

            if (outputTooLarge)
            {
                return ProcessResult.OutputTooLarge(
                    standardOutput.Content,
                    standardError.Content,
                    TryGetExitCode(process)
                );
            }

            if (invalidOutput)
            {
                return ProcessResult.InvalidOutput(
                    standardOutput.Content,
                    standardError.Content,
                    TryGetExitCode(process)
                );
            }

            if (teeFailed)
            {
                return ProcessResult.InvalidOutput(
                    standardOutput.Content,
                    standardError.Content,
                    TryGetExitCode(process)
                );
            }

            if (triggeringCancellationCause == ProcessCancellationCause.Caller)
            {
                cancellationToken.ThrowIfCancellationRequested();
            }

            return ProcessResult.TimedOut(
                standardOutput.Content,
                standardError.Content,
                TryGetExitCode(process)
            );
        }
        catch
        {
            await KillAndWaitAsync(process).ConfigureAwait(false);
            throw;
        }

        if (standardOutput.TooLarge || standardError.TooLarge)
        {
            return ProcessResult.OutputTooLarge(
                standardOutput.Content,
                standardError.Content,
                process.ExitCode
            );
        }

        if (standardOutput.InvalidUtf8 || standardError.InvalidUtf8)
        {
            return ProcessResult.InvalidOutput(
                standardOutput.Content,
                standardError.Content,
                process.ExitCode
            );
        }

        if (standardError.TeeFailed)
        {
            return ProcessResult.InvalidOutput(
                standardOutput.Content,
                standardError.Content,
                process.ExitCode
            );
        }

        return new ProcessResult(process.ExitCode, standardOutput.Content, standardError.Content);
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

        foreach (string argument in startSpec.Arguments)
        {
            startInfo.ArgumentList.Add(argument);
        }

        foreach ((string key, string? value) in startSpec.Environment)
        {
            if (value is null)
            {
                startInfo.Environment.Remove(key);
            }
            else
            {
                startInfo.Environment[key] = value;
            }
        }

        return startInfo;
    }

    private async Task KillAndWaitAsync(Process process)
    {
        try
        {
            processCleanup.Kill(process);
        }
        catch (InvalidOperationException) { }
        catch (NotSupportedException) { }
        catch (Win32Exception) { }

        using var cleanupCancellation = new CancellationTokenSource(processCleanupTimeout);
        try
        {
            await processCleanup
                .WaitForExitAsync(process, cleanupCancellation.Token)
                .ConfigureAwait(false);
        }
        catch (InvalidOperationException) { }
        catch (NotSupportedException) { }
        catch (Win32Exception) { }
        catch (OperationCanceledException) when (cleanupCancellation.IsCancellationRequested) { }
    }

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
        catch (NotSupportedException)
        {
            return null;
        }
        catch (Win32Exception)
        {
            return null;
        }
    }

    internal interface IProcessCleanupStrategy
    {
        void Kill(Process process);

        Task WaitForExitAsync(Process process, CancellationToken cancellationToken);
    }

    internal interface IProcessStartStrategy
    {
        bool Start(Process process);
    }

    private enum ProcessCancellationCause
    {
        None,
        Caller,
        Timeout,
    }

    internal sealed class SystemProcessStartStrategy : IProcessStartStrategy
    {
        internal static SystemProcessStartStrategy Instance { get; } = new();

        private SystemProcessStartStrategy() { }

        public bool Start(Process process) => process.Start();
    }

    internal sealed class SystemProcessCleanupStrategy : IProcessCleanupStrategy
    {
        internal static SystemProcessCleanupStrategy Instance { get; } = new();

        private SystemProcessCleanupStrategy() { }

        public void Kill(Process process)
        {
            process.Kill(entireProcessTree: true);
        }

        public Task WaitForExitAsync(Process process, CancellationToken cancellationToken) =>
            process.WaitForExitAsync(cancellationToken);
    }

    private sealed class BoundedOutputCapture
    {
        private readonly StringBuilder builder = new();
        private readonly int byteLimit;
        private readonly TextWriter? tee;

        public BoundedOutputCapture(int byteLimit, TextWriter? tee = null)
        {
            this.byteLimit = byteLimit;
            this.tee = tee;
        }

        public string Content => builder.ToString();

        public bool InvalidUtf8 { get; private set; }

        public bool TeeFailed { get; private set; }

        public bool TooLarge { get; private set; }

        public async Task ReadAsync(Stream stream, CancellationTokenSource executionCancellation)
        {
            byte[] byteBuffer = ArrayPool<byte>.Shared.Rent(4096);
            char[] charBuffer = ArrayPool<char>.Shared.Rent(
                RedirectedStreamEncoding.GetMaxCharCount(byteBuffer.Length)
            );
            Decoder decoder = RedirectedStreamEncoding.GetDecoder();
            int observedBytes = 0;

            try
            {
                while (true)
                {
                    int bytesRead = await stream
                        .ReadAsync(
                            byteBuffer.AsMemory(0, byteBuffer.Length),
                            executionCancellation.Token
                        )
                        .ConfigureAwait(false);
                    if (bytesRead == 0)
                    {
                        break;
                    }

                    int bytesToCapture = Math.Min(bytesRead, byteLimit - observedBytes);
                    if (bytesToCapture > 0)
                    {
                        int charactersRead;
                        try
                        {
                            charactersRead = decoder.GetChars(
                                byteBuffer,
                                0,
                                bytesToCapture,
                                charBuffer,
                                0,
                                flush: false
                            );
                        }
                        catch (DecoderFallbackException)
                        {
                            InvalidUtf8 = true;
                            executionCancellation.Cancel();
                            return;
                        }

                        observedBytes += bytesToCapture;
                        await AppendAsync(charBuffer, charactersRead, executionCancellation)
                            .ConfigureAwait(false);
                    }

                    if (bytesToCapture < bytesRead)
                    {
                        TooLarge = true;
                        executionCancellation.Cancel();
                        return;
                    }
                }

                try
                {
                    int remainingCharacters = decoder.GetChars(
                        Array.Empty<byte>(),
                        0,
                        0,
                        charBuffer,
                        0,
                        flush: true
                    );
                    await AppendAsync(charBuffer, remainingCharacters, executionCancellation)
                        .ConfigureAwait(false);
                }
                catch (DecoderFallbackException)
                {
                    InvalidUtf8 = true;
                    executionCancellation.Cancel();
                }
            }
            finally
            {
                ArrayPool<byte>.Shared.Return(byteBuffer);
                ArrayPool<char>.Shared.Return(charBuffer);
            }
        }

        private async Task AppendAsync(
            char[] characters,
            int count,
            CancellationTokenSource executionCancellation
        )
        {
            if (count == 0)
            {
                return;
            }

            builder.Append(characters, 0, count);
            if (tee is null)
            {
                return;
            }

            string teePayload = new(characters, 0, count);
            try
            {
                await tee.WriteAsync(teePayload.AsMemory(), executionCancellation.Token)
                    .WaitAsync(executionCancellation.Token)
                    .ConfigureAwait(false);
                await tee.FlushAsync(executionCancellation.Token)
                    .WaitAsync(executionCancellation.Token)
                    .ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (executionCancellation.IsCancellationRequested)
            {
                throw;
            }
            catch
            {
                TeeFailed = true;
                executionCancellation.Cancel();
            }
        }
    }
}
