using System.Diagnostics;
using System.Security.Cryptography;
using System.Text.Json.Nodes;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace LiteAgensh.Tests.Support;

// Shared process, pipe-stage, and async output-drain utilities for owned creator executables.
internal static class OwnedCreatorProcess
{
    internal static string Executable =>
        Path.Combine(
            AppContext.BaseDirectory,
            OperatingSystem.IsWindows() ? "LiteAgensh.OfflineOwner.exe" : "LiteAgensh.OfflineOwner"
        );

    internal static string AssemblyPath =>
        Path.Combine(AppContext.BaseDirectory, "LiteAgensh.OfflineOwner.dll");

    internal static string Hash(string path) =>
        Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes(path)));

    internal static async Task WaitForExitAsync(Process creator, CancellationToken token)
    {
        using CancellationTokenSource exitWait =
            CancellationTokenSource.CreateLinkedTokenSource(token);
        exitWait.CancelAfter(TimeSpan.FromSeconds(5));
        CancellationToken waitToken = exitWait.Token;
        Task exit = creator.WaitForExitAsync(waitToken);
        try
        {
            await exit.ConfigureAwait(false);
        }
        catch (OperationCanceledException canceled) when (
            exit.IsCanceled
            && canceled.CancellationToken == waitToken
            && waitToken.IsCancellationRequested
            && !token.IsCancellationRequested
        )
        {
            throw new TimeoutException("The creator exit wait exceeded five seconds.", canceled);
        }
    }

    internal static async Task<JsonObject> ReceiveStageAsync(
        StreamReader reader,
        string nonce,
        string stage,
        TimeSpan fuse,
        CancellationToken token
    )
    {
        using CancellationTokenSource reading =
            CancellationTokenSource.CreateLinkedTokenSource(token);
        Task<string?> read = reader.ReadLineAsync(reading.Token).AsTask();
        await ObservePipeStageAsync(read, reading, fuse, token).ConfigureAwait(false);
        string? line = await read.ConfigureAwait(false);
        Assert.IsNotNull(line, "Creator disconnected before " + stage);
        Assert.IsLessThanOrEqualTo(1_048_576, line.Length);
        JsonObject message = JsonNode.Parse(line)!.AsObject();
        Assert.AreEqual(1, message["version"]!.GetValue<int>());
        Assert.AreEqual(nonce, message["nonce"]!.GetValue<string>());
        Assert.AreEqual(stage, message["stage"]!.GetValue<string>());
        return message["data"]!.AsObject();
    }

    internal static async Task ObservePipeStageAsync(
        Task operation,
        CancellationTokenSource stage,
        TimeSpan fuse,
        CancellationToken token
    )
    {
        try
        {
            await operation.WaitAsync(fuse, token).ConfigureAwait(false);
        }
        catch (Exception primary)
        {
            List<Exception> secondary = [];
            try
            {
                await stage.CancelAsync().ConfigureAwait(false);
            }
            catch (Exception error)
            {
                secondary.Add(error);
            }
            try
            {
                await operation.ConfigureAwait(false);
            }
            catch (OperationCanceledException canceled) when (
                operation.IsCanceled
                && stage.IsCancellationRequested
                && (
                    canceled.CancellationToken == stage.Token
                    || !canceled.CancellationToken.CanBeCanceled
                    || (canceled.CancellationToken == token && token.IsCancellationRequested)
                )
            )
            { }
            catch (Exception error)
            {
                if (!ReferenceEquals(error, primary))
                {
                    secondary.Add(error);
                }
            }
            primary.Data["CreatorPipeOperationStatus"] = operation.Status.ToString();
            if (secondary.Count != 0)
            {
                primary.Data["CreatorPipeCleanupFailures"] = secondary.ToArray();
            }
            throw;
        }
    }

    internal sealed class OutputCapture(StreamReader reader)
    {
        internal StreamReader Reader { get; } = reader;
        internal Stream ReadEnd { get; } = reader.BaseStream;
        internal Task<string>? Capture { get; private set; }

        internal void Start(CancellationToken cancellationToken) =>
            Capture = Reader.ReadToEndAsync(cancellationToken);
    }

    internal static async Task<string?> DrainOutputAsync(
        OutputCapture output,
        CancellationTokenSource captureScope,
        string stream,
        CancellationToken deadline
    )
    {
        List<Exception> failures = [];
        string? text = null;
        if (output.Capture is { } capture)
        {
            try
            {
                text = await capture.WaitAsync(deadline).ConfigureAwait(false);
            }
            catch (Exception observation)
            {
                failures.Add(observation);
                if (!capture.IsCompleted)
                {
                    if (!captureScope.IsCancellationRequested)
                    {
                        Release(captureScope.Cancel);
                    }
                    if (!capture.IsCompleted)
                    {
                        Release(output.ReadEnd.Dispose);
                    }
                    if (!capture.IsCompleted)
                    {
                        TimeoutException unresolved = new(
                            $"Creator {stream} capture is unresolved after "
                                + "read-end abort was attempted.",
                            new AggregateException(failures)
                        );
                        unresolved.Data["CreatorOutputOriginalRead"] = capture;
                        unresolved.Data["CreatorOutputLateReaderRelease"] =
                            ReleaseReaderAfterCaptureAsync(output.Reader, capture);
                        throw unresolved;
                    }
                }
                try
                {
                    _ = await capture.ConfigureAwait(false);
                }
                catch (OperationCanceledException canceled) when (
                    observation is OperationCanceledException observedCancellation
                    && canceled.CancellationToken == observedCancellation.CancellationToken
                )
                { }
                catch (Exception terminal)
                {
                    if (!ReferenceEquals(terminal, observation))
                    {
                        failures.Add(terminal);
                    }
                }
            }
        }
        Release(output.Reader.Dispose);
        if (failures.Count != 0)
        {
            throw new IOException(
                $"Creator {stream} capture or reader release failed.",
                new AggregateException(failures)
            );
        }
        return text;

        void Release(Action release)
        {
            try
            {
                release();
            }
            catch (Exception error)
            {
                failures.Add(error);
            }
        }
    }

    private static async Task<Exception?> ReleaseReaderAfterCaptureAsync(
        StreamReader reader,
        Task<string> capture
    )
    {
        try
        {
            await capture.ConfigureAwait(false);
        }
        catch (Exception) { }

        try
        {
            reader.Dispose();
            return null;
        }
        catch (Exception error)
        {
            return error;
        }
    }

    internal static void DisposeOutputWhenTerminal(
        CancellationTokenSource captureScope,
        Task? stdout,
        Task? stderr
    )
    {
        _ = Task.WhenAll(stdout ?? Task.CompletedTask, stderr ?? Task.CompletedTask)
            .ContinueWith(
                static (settled, scope) =>
                {
                    _ = settled.Exception;
                    ((CancellationTokenSource)scope!).Dispose();
                },
                captureScope,
                CancellationToken.None,
                TaskContinuationOptions.ExecuteSynchronously,
                TaskScheduler.Default
            );
    }
}
