using System.Diagnostics;
using System.Globalization;
using System.Security.Cryptography;
using System.Text.RegularExpressions;

namespace LiteAgensh;

internal sealed partial class NativeRuntime : IAsyncDisposable
{
    private readonly RunDatabase database;
    private readonly ProcessRunner processes;
    private readonly Process process;
    private readonly string token = Convert.ToHexString(RandomNumberGenerator.GetBytes(32));
    private readonly TaskCompletionSource<int> readiness = new(
        TaskCreationOptions.RunContinuationsAsynchronously
    );
    private readonly SemaphoreSlim settlement = new(1, 1);
    private Task output = Task.CompletedTask;
    private Task error = Task.CompletedTask;
    private long? lease;
    private bool started;
    private bool settled;

    public NativeRuntime(RunDatabase database, ProcessRunner processes, string cwd)
    {
        this.database = database;
        this.processes = processes;
        ProcessStartInfo start = new(database.Configuration.NativeExecutable)
        {
            WorkingDirectory = Path.GetFullPath(cwd),
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            RedirectStandardInput = true,
            CreateNoWindow = true,
        };
        start.ArgumentList.Add("--headless");
        start.ArgumentList.Add("--no-auto-update");
        start.Environment["COPILOT_CONNECTION_TOKEN"] = token;
        start.Environment.Remove("NODE_DEBUG");
        process = new() { StartInfo = start };
    }

    public string ConnectionToken => token;

    public long? LeaseId => lease;

    public async Task<string> StartAsync(
        string kind,
        string evidencePath,
        CancellationToken cancellationToken
    )
    {
        cancellationToken.ThrowIfCancellationRequested();
        Directory.CreateDirectory(Path.GetDirectoryName(evidencePath)!);
        try
        {
            process.Start();
            started = true;
            lease = processes.Register(process, kind);
            process.StandardInput.Close();
            output = PumpAsync(process.StandardOutput, evidencePath + ".stdout.log", true);
            error = PumpAsync(process.StandardError, evidencePath + ".stderr.log", false);
            using CancellationTokenSource deadline =
                CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            deadline.CancelAfter(TimeSpan.FromSeconds(30));
            int port = await readiness.Task.WaitAsync(deadline.Token).ConfigureAwait(false);
            if (process.HasExited)
            {
                throw new OperationException("native_start_failed", "Native runtime exited.");
            }

            return $"http://127.0.0.1:{port.ToString(CultureInfo.InvariantCulture)}";
        }
        catch
        {
            await DisposeAsync().ConfigureAwait(false);
            throw;
        }
    }

    public async ValueTask DisposeAsync()
    {
        await settlement.WaitAsync(CancellationToken.None).ConfigureAwait(false);
        try
        {
            if (settled)
            {
                return;
            }

            if (started)
            {
                try
                {
                    if (!process.HasExited)
                    {
                        try
                        {
                            await process
                                .WaitForExitAsync(CancellationToken.None)
                                .WaitAsync(TimeSpan.FromSeconds(2), CancellationToken.None)
                                .ConfigureAwait(false);
                        }
                        catch (TimeoutException)
                        {
                            if (!process.HasExited)
                            {
                                process.Kill(entireProcessTree: true);
                            }
                        }
                    }

                    await process
                        .WaitForExitAsync(CancellationToken.None)
                        .WaitAsync(TimeSpan.FromSeconds(5), CancellationToken.None)
                        .ConfigureAwait(false);
                    await Task.WhenAll(output, error)
                        .WaitAsync(TimeSpan.FromSeconds(5), CancellationToken.None)
                        .ConfigureAwait(false);
                    if (lease is long id)
                    {
                        processes.Complete(id);
                    }
                }
                catch (Exception exception)
                {
                    database.WriteMetadata("lifecycle:ownership_ambiguity", exception.Message);
                    throw new OperationException(
                        "ownership_unresolved",
                        "The owned native runtime or inherited pipes did not settle."
                    );
                }
            }

            settled = true;
            process.Dispose();
        }
        finally
        {
            settlement.Release();
        }
    }

    private async Task PumpAsync(StreamReader reader, string path, bool findReadiness)
    {
        try
        {
            await using StreamWriter writer = new(path, append: false);
            while (await reader.ReadLineAsync().ConfigureAwait(false) is string line)
            {
                await writer
                    .WriteLineAsync(line.Replace(token, "[redacted]", StringComparison.Ordinal))
                    .ConfigureAwait(false);
                await writer.FlushAsync().ConfigureAwait(false);
                Match match = ListeningPort().Match(line);
                if (
                    findReadiness
                    && match.Success
                    && int.TryParse(
                        match.Groups[1].Value,
                        NumberStyles.None,
                        CultureInfo.InvariantCulture,
                        out int port
                    )
                    && port is > 0 and <= 65535
                )
                {
                    readiness.TrySetResult(port);
                }
            }
        }
        finally
        {
            if (findReadiness)
            {
                readiness.TrySetException(
                    new OperationException(
                        "native_start_failed",
                        "Native stdout ended before a usable loopback readiness endpoint."
                    )
                );
            }
        }
    }

    [GeneratedRegex(@"listening on port ([0-9]+)", RegexOptions.IgnoreCase)]
    private static partial Regex ListeningPort();
}
