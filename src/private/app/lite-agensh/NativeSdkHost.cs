using System.Text.Json.Nodes;
using GitHub.Copilot;

namespace LiteAgensh;

internal sealed class NativeSdkHost(
    RunDatabase database,
    ProcessRunner processes
) : IAsyncDisposable
{
    private readonly SemaphoreSlim lifecycle = new(1, 1);
    private NativeRuntime? runtime;
    private CopilotClient? client;
    private Exception? startupFailure;
    private Exception? settlementFailure;
    private bool disposed;

    public string Id { get; } = Guid.NewGuid().ToString("N");

    public string? ActualRuntimeVersion { get; private set; }

    public long LeaseId =>
        runtime?.LeaseId ?? throw new InvalidOperationException("No owned native runtime.");

    public async Task<CopilotClient> GetClientAsync(
        string cwd,
        string evidencePath,
        CancellationToken cancellationToken
    )
    {
        cancellationToken.ThrowIfCancellationRequested();
        await lifecycle.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (disposed || startupFailure is not null)
            {
                throw new OperationException(
                    "unavailable",
                    "The operation's native runtime is closed or failed; replacement is blocked."
                );
            }

            if (client is not null)
            {
                return client;
            }

            try
            {
                database.WriteMetadata(
                    $"native:runtime:{Id}",
                    new JsonObject
                    {
                        ["runtimeId"] = Id,
                        ["state"] = "opening",
                        ["evidencePrefix"] = evidencePath,
                    }.ToJsonString()
                );
                runtime = new(database, processes, cwd);
                string endpoint = await runtime
                    .StartAsync($"native:runtime:{Id}", evidencePath, cancellationToken)
                    .ConfigureAwait(false);
                client = new(
                    new CopilotClientOptions
                    {
                        Connection = RuntimeConnection.ForUri(endpoint, runtime.ConnectionToken),
                        Mode = CopilotClientMode.Empty,
                    }
                );
                using CancellationTokenSource deadline =
                    CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                deadline.CancelAfter(TimeSpan.FromSeconds(30));
                await client.StartAsync(deadline.Token).ConfigureAwait(false);
                GetStatusResponse status = await client
                    .GetStatusAsync(deadline.Token)
                    .ConfigureAwait(false);
                if (status.Version != NativeSdkSession.RuntimeVersion)
                {
                    throw new OperationException(
                        "unavailable",
                        "The installed native runtime does not match the SDK package's CLI pin."
                    );
                }

                ActualRuntimeVersion = status.Version;
                RecordState("ready");
                return client;
            }
            catch (Exception exception)
            {
                startupFailure = exception;
                throw;
            }
        }
        finally
        {
            lifecycle.Release();
        }
    }

    public void Associate(string worker, string sessionId, string cwd) =>
        database.WriteMetadata(
            $"native:runtime:{Id}:session:{sessionId}",
            new JsonObject
            {
                ["runtimeId"] = Id,
                ["runtimeLeaseId"] = LeaseId,
                ["sessionId"] = sessionId,
                ["worker"] = worker,
                ["workingDirectory"] = cwd,
            }.ToJsonString()
        );

    public async ValueTask DisposeAsync()
    {
        await lifecycle.WaitAsync(CancellationToken.None).ConfigureAwait(false);
        try
        {
            if (disposed)
            {
                if (settlementFailure is not null)
                {
                    throw new OperationException(
                        "ownership_unresolved",
                        $"The native runtime's prior settlement failed: {settlementFailure.Message}"
                    );
                }
                return;
            }

            disposed = true;
            try
            {
                try
                {
                    if (client is not null)
                    {
                        await client.DisposeAsync().AsTask()
                            .WaitAsync(TimeSpan.FromSeconds(5), CancellationToken.None)
                            .ConfigureAwait(false);
                    }
                }
                finally
                {
                    if (runtime is not null)
                    {
                        await runtime.DisposeAsync().ConfigureAwait(false);
                    }
                }
                if (runtime is not null)
                {
                    RecordState("settled");
                }
            }
            catch (Exception exception)
            {
                settlementFailure = exception;
                if (runtime is not null)
                {
                    RecordState("unresolved");
                }
                throw;
            }
        }
        finally
        {
            lifecycle.Release();
        }
    }

    private void RecordState(string state) =>
        database.WriteMetadata(
            $"native:runtime:{Id}",
            new JsonObject
            {
                ["runtimeId"] = Id,
                ["runtimeLeaseId"] = runtime?.LeaseId,
                ["runtimeVersion"] = ActualRuntimeVersion,
                ["state"] = state,
            }.ToJsonString()
        );
}
