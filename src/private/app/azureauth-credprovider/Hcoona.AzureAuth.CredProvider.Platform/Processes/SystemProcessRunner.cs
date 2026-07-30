using System.Buffers;
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

        using var timeoutCancellation = new CancellationTokenSource(startSpec.Timeout);
        using var executionCancellation = CancellationTokenSource.CreateLinkedTokenSource(
            cancellationToken,
            timeoutCancellation.Token
        );
        var standardOutput = new BoundedOutputCapture(
            startSpec.OutputCaptureOptions.StandardOutputByteLimit
        );
        var standardError = new BoundedOutputCapture(
            startSpec.OutputCaptureOptions.StandardErrorByteLimit
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
            await KillAndWaitAsync(process).ConfigureAwait(false);

            if (standardOutput.TooLarge || standardError.TooLarge)
            {
                return ProcessResult.OutputTooLarge(
                    standardOutput.Content,
                    standardError.Content,
                    TryGetExitCode(process)
                );
            }

            if (standardOutput.InvalidUtf8 || standardError.InvalidUtf8)
            {
                return ProcessResult.InvalidOutput(
                    standardOutput.Content,
                    standardError.Content,
                    TryGetExitCode(process)
                );
            }

            cancellationToken.ThrowIfCancellationRequested();
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

    private static async Task KillAndWaitAsync(Process process)
    {
        try
        {
            process.Kill(entireProcessTree: true);
        }
        catch (InvalidOperationException) { }
        catch (NotSupportedException) { }
        catch (Win32Exception) { }

        try
        {
            await process.WaitForExitAsync(CancellationToken.None).ConfigureAwait(false);
        }
        catch (InvalidOperationException) { }
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
    }

    private sealed class BoundedOutputCapture(int byteLimit)
    {
        private readonly StringBuilder builder = new();

        public string Content => builder.ToString();

        public bool InvalidUtf8 { get; private set; }

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

                    observedBytes += bytesRead;
                    if (observedBytes > byteLimit)
                    {
                        TooLarge = true;
                        executionCancellation.Cancel();
                        return;
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
                        InvalidUtf8 = true;
                        executionCancellation.Cancel();
                        return;
                    }

                    builder.Append(charBuffer, 0, charactersRead);
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
                    builder.Append(charBuffer, 0, remainingCharacters);
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
    }
}
