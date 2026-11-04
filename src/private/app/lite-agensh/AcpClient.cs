using System.Collections.Concurrent;
using System.Diagnostics;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace LiteAgensh;

public sealed class AcpClient : IAsyncDisposable
{
    private readonly Process process;
    private readonly ProcessRunner processes;
    private readonly long lease;
    private readonly string permissionPolicy;
    private readonly SemaphoreSlim writer = new(1, 1);
    private readonly SemaphoreSlim turns = new(1, 1);
    private readonly ConcurrentDictionary<long, TaskCompletionSource<JsonObject>> pending = new();
    private readonly ConcurrentDictionary<string, JsonObject> commandSnapshots = new(
        StringComparer.Ordinal
    );
    private readonly Task outputPump;
    private readonly Task errorPump;
    private readonly Action<JsonObject>? onUpdate;
    private long nextId;
    private bool settled;

    private AcpClient(
        Process process,
        ProcessRunner processes,
        long lease,
        string permissionPolicy,
        string errorPath,
        Action<JsonObject>? onUpdate
    )
    {
        this.process = process;
        this.processes = processes;
        this.lease = lease;
        this.permissionPolicy = permissionPolicy;
        this.onUpdate = onUpdate;
        errorPump = CopyErrorAsync(errorPath);
        outputPump = ReadOutputAsync();
    }

    public JsonObject Initialization { get; private set; } = [];

    public string? SessionId { get; private set; }

    public bool SupportsLoad =>
        Initialization["agentCapabilities"]?["loadSession"]?.GetValue<bool>() == true;

    public static AcpClient Start(
        ProcessStartInfo startInfo,
        ProcessRunner processes,
        string kind,
        string permissionPolicy,
        string errorPath,
        Action<JsonObject>? onUpdate = null
    )
    {
        if (permissionPolicy is not ("deny" or "trusted"))
        {
            throw new ArgumentException(
                "Unknown native permission policy.",
                nameof(permissionPolicy)
            );
        }

        if (!Path.IsPathFullyQualified(startInfo.WorkingDirectory))
        {
            throw new ArgumentException(
                "ACP requires an absolute working directory.",
                nameof(startInfo)
            );
        }

        startInfo.UseShellExecute = false;
        startInfo.RedirectStandardInput = true;
        startInfo.RedirectStandardOutput = true;
        startInfo.RedirectStandardError = true;
        startInfo.CreateNoWindow = true;
        startInfo.StandardInputEncoding = new UTF8Encoding(false);
        startInfo.StandardOutputEncoding = Encoding.UTF8;
        Process process = new() { StartInfo = startInfo };
        bool started = false;
        try
        {
            process.Start();
            started = true;
            long lease = processes.Register(process, kind);
            return new(process, processes, lease, permissionPolicy, errorPath, onUpdate);
        }
        catch (Exception startupFailure)
        {
            if (started)
            {
                try
                {
                    if (!process.HasExited)
                    {
                        process.Kill(entireProcessTree: true);
                    }

                    if (!process.WaitForExit(5000))
                    {
                        throw new TimeoutException(
                            "The native process did not exit after startup failure."
                        );
                    }
                }
                catch (Exception settlementFailure)
                {
                    int pid = process.Id;
                    process.Dispose();
                    throw new OperationException(
                        "ownership_unresolved",
                        $"Native PID {pid} could not be settled after startup/journal failure: "
                            + $"{startupFailure.Message}; {settlementFailure.Message}"
                    );
                }
            }

            process.Dispose();
            if (!started)
            {
                throw new OperationException(
                    "native_start_failed",
                    $"No native process was started: {startupFailure.Message}"
                );
            }

            throw;
        }
    }

    public async Task<JsonObject> InitializeAsync(CancellationToken cancellationToken = default)
    {
        Initialization = await RequestAsync(
                "initialize",
                new()
                {
                    ["protocolVersion"] = 1,
                    ["clientInfo"] = new JsonObject { ["name"] = "LiteAgensh", ["version"] = "1" },
                    ["clientCapabilities"] = new JsonObject(),
                },
                cancellationToken
            )
            .ConfigureAwait(false);
        if (Initialization["protocolVersion"]?.GetValue<int>() != 1)
        {
            throw new OperationException(
                "unavailable",
                "The native agent did not negotiate ACP protocol version 1."
            );
        }

        return Initialization;
    }

    public async Task<string> OpenSessionAsync(
        string cwd,
        JsonArray mcpServers,
        string? exactSessionId = null,
        CancellationToken cancellationToken = default
    )
    {
        if (!Path.IsPathFullyQualified(cwd))
        {
            throw new ArgumentException("The native session cwd must be absolute.", nameof(cwd));
        }

        if (exactSessionId is not null && !SupportsLoad)
        {
            throw new OperationException(
                "unavailable",
                "The native agent did not advertise exact-session loading."
            );
        }

        JsonObject parameters = new()
        {
            ["cwd"] = Path.GetFullPath(cwd),
            ["mcpServers"] = mcpServers.DeepClone(),
        };
        if (exactSessionId is not null)
        {
            parameters["sessionId"] = exactSessionId;
        }

        JsonObject result = await RequestAsync(
                exactSessionId is null ? "session/new" : "session/load",
                parameters,
                cancellationToken
            )
            .ConfigureAwait(false);
        string id =
            exactSessionId
            ?? result["sessionId"]?.GetValue<string>()
            ?? throw new InvalidDataException("ACP session/new returned no session identity.");
        if (
            string.IsNullOrWhiteSpace(id)
            || (
                exactSessionId is not null
                && result["sessionId"] is JsonValue returned
                && returned.GetValue<string>() != exactSessionId
            )
        )
        {
            throw new InvalidDataException(
                "The native session identity does not match the requested session."
            );
        }

        SessionId = id;
        return id;
    }

    public JsonArray AdvertisedCommands =>
        SessionId is not null && commandSnapshots.TryGetValue(SessionId, out JsonObject? snapshot)
            ? snapshot["availableCommands"]?.DeepClone().AsArray() ?? []
            : [];

    public async Task WaitForCommandsAsync(
        TimeSpan timeout,
        CancellationToken cancellationToken = default
    )
    {
        using CancellationTokenSource deadline = new(timeout);
        using CancellationTokenSource linked = CancellationTokenSource.CreateLinkedTokenSource(
            cancellationToken,
            deadline.Token
        );
        try
        {
            while (SessionId is not null && !commandSnapshots.ContainsKey(SessionId))
            {
                if (outputPump.IsCompleted)
                {
                    await outputPump.ConfigureAwait(false);
                    throw new EndOfStreamException(
                        "ACP ended before advertising informational commands."
                    );
                }

                await Task.Delay(TimeSpan.FromMilliseconds(50), linked.Token).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            throw new OperationException(
                "unavailable",
                "The native session did not advertise informational commands in time."
            );
        }
    }

    public async Task<JsonObject> PromptAsync(
        string text,
        CancellationToken cancellationToken = default
    )
    {
        await turns.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            return await RequestAsync(
                    "session/prompt",
                    new()
                    {
                        ["sessionId"] =
                            SessionId
                            ?? throw new InvalidOperationException("No native session is open."),
                        ["prompt"] = new JsonArray(
                            new JsonObject { ["type"] = "text", ["text"] = text }
                        ),
                    },
                    cancellationToken
                )
                .ConfigureAwait(false);
        }
        finally
        {
            turns.Release();
        }
    }

    public Task CancelAsync(CancellationToken cancellationToken = default) =>
        SessionId is null
            ? Task.CompletedTask
            : SendAsync(
                new()
                {
                    ["jsonrpc"] = "2.0",
                    ["method"] = "session/cancel",
                    ["params"] = new JsonObject { ["sessionId"] = SessionId },
                },
                cancellationToken
            );

    public async Task StopAsync()
    {
        if (settled)
        {
            return;
        }

        try
        {
            using CancellationTokenSource cancelDeadline = new(TimeSpan.FromSeconds(2));
            await CancelAsync(cancelDeadline.Token).ConfigureAwait(false);
        }
        catch (Exception exception)
            when (exception is IOException or OperationCanceledException or ObjectDisposedException)
        {
            // EOF can race cancellation; only process and pipe settlement establish quiescence.
        }

        try
        {
            process.StandardInput.Close();
        }
        catch (Exception exception) when (exception is IOException or InvalidOperationException)
        {
            // A concurrent failed write must not skip the bounded process-settlement path.
        }
        try
        {
            await AwaitExitAndPipesAsync().WaitAsync(TimeSpan.FromSeconds(5)).ConfigureAwait(false);
        }
        catch (TimeoutException)
        {
            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: true);
            }

            try
            {
                await AwaitExitAndPipesAsync()
                    .WaitAsync(TimeSpan.FromSeconds(5))
                    .ConfigureAwait(false);
            }
            catch (TimeoutException exception)
            {
                throw new OperationException(
                    "ownership_unresolved",
                    $"Owned native process or inherited pipes did not settle: {exception.Message}"
                );
            }
        }

        processes.Complete(lease);
        settled = true;
    }

    public async ValueTask DisposeAsync()
    {
        await StopAsync().ConfigureAwait(false);
        process.Dispose();
        writer.Dispose();
        turns.Dispose();
        GC.SuppressFinalize(this);
    }

    private async Task AwaitExitAndPipesAsync()
    {
        await process.WaitForExitAsync(CancellationToken.None).ConfigureAwait(false);
        try
        {
            await outputPump.ConfigureAwait(false);
        }
        catch (Exception exception)
            when (exception
                    is IOException
                        or JsonException
                        or InvalidDataException
                        or OperationException
            )
        {
            // Protocol failure is delivered to pending callers;
            // it does not prevent confirming exit.
        }

        await errorPump.ConfigureAwait(false);
    }

    private async Task<JsonObject> RequestAsync(
        string method,
        JsonObject parameters,
        CancellationToken cancellationToken
    )
    {
        long id = Interlocked.Increment(ref nextId);
        TaskCompletionSource<JsonObject> completion = new(
            TaskCreationOptions.RunContinuationsAsynchronously
        );
        if (!pending.TryAdd(id, completion))
        {
            throw new InvalidOperationException("Duplicate ACP request identity.");
        }

        try
        {
            if (outputPump.IsCompleted)
            {
                await outputPump.ConfigureAwait(false);
                throw new EndOfStreamException("The ACP protocol stream has ended.");
            }

            await SendAsync(
                    new()
                    {
                        ["jsonrpc"] = "2.0",
                        ["id"] = id,
                        ["method"] = method,
                        ["params"] = parameters,
                    },
                    cancellationToken
                )
                .ConfigureAwait(false);
            return await completion.Task.WaitAsync(cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            pending.TryRemove(id, out _);
        }
    }

    private async Task SendAsync(JsonObject message, CancellationToken cancellationToken)
    {
        await writer.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await process
                .StandardInput.WriteLineAsync(message.ToJsonString().AsMemory(), cancellationToken)
                .ConfigureAwait(false);
            await process.StandardInput.FlushAsync(cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            writer.Release();
        }
    }

    private async Task ReadOutputAsync()
    {
        Exception terminal = new EndOfStreamException("Native ACP protocol reached EOF.");
        try
        {
            while (
                await process
                    .StandardOutput.ReadLineAsync(CancellationToken.None)
                    .ConfigureAwait(false)
                    is string line
            )
            {
                JsonObject message =
                    JsonNode.Parse(line)?.AsObject()
                    ?? throw new InvalidDataException("Native stdout was not an ACP JSON object.");
                if (message["jsonrpc"]?.GetValue<string>() != "2.0")
                {
                    throw new InvalidDataException(
                        "Native stdout contained a non-JSON-RPC message."
                    );
                }

                if (message["method"] is JsonValue method)
                {
                    if (message["id"] is not null)
                    {
                        await RespondAsync(message, method.GetValue<string>())
                            .ConfigureAwait(false);
                    }
                    else if (
                        method.GetValue<string>() == "session/update"
                        && message["params"] is JsonObject parameters
                    )
                    {
                        string? session = parameters["sessionId"]?.GetValue<string>();
                        JsonObject? update = parameters["update"] as JsonObject;
                        if (
                            session is not null
                            && update?["sessionUpdate"]?.GetValue<string>()
                                == "available_commands_update"
                        )
                        {
                            commandSnapshots[session] = (JsonObject)update.DeepClone();
                        }

                        onUpdate?.Invoke((JsonObject)parameters.DeepClone());
                    }
                }
                else if (
                    message["id"] is JsonValue identity
                    && identity.TryGetValue(out long id)
                    && pending.TryGetValue(id, out TaskCompletionSource<JsonObject>? completion)
                )
                {
                    if (message["error"] is JsonObject error)
                    {
                        completion.TrySetException(
                            new OperationException("native_error", error.ToJsonString())
                        );
                    }
                    else if (message["result"] is JsonObject result)
                    {
                        completion.TrySetResult((JsonObject)result.DeepClone());
                    }
                    else
                    {
                        completion.TrySetException(
                            new InvalidDataException("ACP response has no object result.")
                        );
                    }
                }
            }
        }
        catch (Exception exception)
        {
            terminal = exception;
            throw;
        }
        finally
        {
            foreach (TaskCompletionSource<JsonObject> completion in pending.Values)
            {
                completion.TrySetException(terminal);
            }
        }
    }

    private async Task RespondAsync(JsonObject request, string method)
    {
        JsonObject response = new() { ["jsonrpc"] = "2.0", ["id"] = request["id"]!.DeepClone() };
        if (method == "session/request_permission" && request["params"] is JsonObject parameters)
        {
            response["result"] = PermissionResponse(parameters, permissionPolicy);
        }
        else
        {
            response["error"] = new JsonObject
            {
                ["code"] = -32601,
                ["message"] = "Client method is not supported.",
            };
        }

        await SendAsync(response, CancellationToken.None).ConfigureAwait(false);
    }

    public static JsonObject PermissionResponse(JsonObject parameters, string policy)
    {
        string? kind = parameters["toolCall"]?["kind"]?.GetValue<string>();
        bool trusted =
            policy == "trusted"
            && kind is "read" or "edit" or "delete" or "move" or "search" or "execute" or "fetch";
        JsonObject[] options =
            parameters["options"]?.AsArray().OfType<JsonObject>().ToArray() ?? [];
        string[] preferences = trusted ? ["allow_once"] : ["reject_once", "reject_always"];
        foreach (string preference in preferences)
        {
            JsonObject? option = options.FirstOrDefault(item =>
                item["kind"]?.GetValue<string>() == preference
                && item["optionId"] is JsonValue value
                && value.TryGetValue(out string? id)
                && !string.IsNullOrWhiteSpace(id)
            );
            if (option is not null)
            {
                return new()
                {
                    ["outcome"] = new JsonObject
                    {
                        ["outcome"] = "selected",
                        ["optionId"] = option["optionId"]!.DeepClone(),
                    },
                };
            }
        }

        return new() { ["outcome"] = new JsonObject { ["outcome"] = "cancelled" } };
    }

    private async Task CopyErrorAsync(string path)
    {
        await using FileStream stream = new(
            path,
            FileMode.CreateNew,
            FileAccess.Write,
            FileShare.Read
        );
        await process
            .StandardError.BaseStream.CopyToAsync(stream, CancellationToken.None)
            .ConfigureAwait(false);
    }
}
