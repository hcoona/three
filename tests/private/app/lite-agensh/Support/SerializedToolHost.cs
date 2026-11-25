using System.Diagnostics;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;

namespace LiteAgensh.Tests.Support;

// Owns only the product's actual stdio process. Framing/negotiation remain in
// the official MCP library; no native agent or alternate transport is supplied.
internal sealed class SerializedToolHost : IAsyncDisposable
{
    private readonly Process process;
    private readonly Task<string> error;
    private readonly string actor;
    private readonly string statePath;
    private readonly long birthTicks;
    private McpClient? client;
    private Task<string>? output;
    private Exception? primary;
    private readonly int expectedExit;

    private SerializedToolHost(
        string statePath,
        string actor,
        string[] arguments,
        int expectedExit,
        string? workingDirectory = null
    )
    {
        this.actor = actor;
        this.expectedExit = expectedExit;
        this.statePath = statePath;
        workingDirectory ??= statePath;
        string home = Path.Combine(workingDirectory, "phase8-owned-home");
        Directory.CreateDirectory(home);
        string global = Path.Combine(home, "global.gitconfig");
        if (!File.Exists(global))
        {
            File.WriteAllText(global, "");
        }
        ProcessStartInfo start = new(Path.Combine(AppContext.BaseDirectory, "LiteAgensh.exe"))
        {
            WorkingDirectory = workingDirectory,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };
        start.Environment.Clear();
        string[] inherited = ["PATH", "SystemRoot", "TEMP", "TMP", "DOTNET_ROOT"];
        foreach (string name in inherited)
        {
            if (Environment.GetEnvironmentVariable(name) is { } value)
            {
                start.Environment[name] = value;
            }
        }
        start.Environment["DOTNET_PROCESSOR_COUNT"] = "2";
        start.Environment["HOME"] = home;
        start.Environment["USERPROFILE"] = home;
        start.Environment["XDG_CONFIG_HOME"] = home;
        start.Environment["GIT_CONFIG_GLOBAL"] = global;
        start.Environment["GIT_CONFIG_NOSYSTEM"] = "1";
        start.Environment["DOTNET_CLI_TELEMETRY_OPTOUT"] = "1";
        start.Environment["OTEL_SDK_DISABLED"] = "true";
        foreach (string argument in arguments)
        {
            start.ArgumentList.Add(argument);
        }
        process = new Process { StartInfo = start };
        bool started = false;
        try
        {
            started = process.Start();
            if (!started)
            {
                throw new InvalidOperationException("Product host did not start.");
            }
            birthTicks = process.StartTime.ToUniversalTime().Ticks;
            error = process.StandardError.ReadToEndAsync();
        }
        catch (Exception primary)
        {
            Console.Error.WriteLine(
                "TOOL_HOST_CONSTRUCTOR_PRIMARY " + Json.ExceptionDetails(primary)
            );
            try
            {
                if (started && !process.HasExited)
                {
                    process.Kill(entireProcessTree: true);
                }
                if (started && !process.WaitForExit(5_000))
                {
                    throw new TimeoutException("Owned product constructor cleanup did not join.");
                }
            }
            catch (Exception secondary)
            {
                Console.Error.WriteLine(
                    "TOOL_HOST_CONSTRUCTOR_SECONDARY " + Json.ExceptionDetails(secondary)
                );
            }
            finally
            {
                process.Dispose();
            }
            throw;
        }
    }

    internal McpClient Client =>
        client ?? throw new InvalidOperationException("MCP negotiation not complete.");

    internal static async Task RunAsync(
        RunDatabase database,
        string actor,
        Func<SerializedToolHost, Task> body,
        CancellationToken token
    )
    {
        SerializedToolHost host = new(
            database.StatePath, actor,
            ["mcp", "--state", database.StatePath, "--actor", actor], expectedExit: 0
        );
        try
        {
            host.client = await McpClient
                .CreateAsync(
                    new StreamClientTransport(
                        host.process.StandardInput.BaseStream,
                        host.process.StandardOutput.BaseStream
                    ),
                    cancellationToken: token
                )
                .ConfigureAwait(false);
            await body(host).ConfigureAwait(false);
        }
        catch (Exception exception)
        {
            host.primary = exception;
            Console.Error.WriteLine("TOOL_HOST_PRIMARY " + Json.ExceptionDetails(exception));
            throw;
        }
        finally
        {
            await host.DisposeAsync().ConfigureAwait(false);
        }
    }

    internal async Task<JsonObject> InvokeAsync(
        string name,
        JsonObject arguments,
        CancellationToken token
    )
    {
        Dictionary<string, object?> values = arguments.ToDictionary(
            pair => pair.Key,
            pair => (object?)JsonSerializer.SerializeToElement(pair.Value, Json.Options)
        );
        CallToolResult wire = await Client
            .CallToolAsync(name, values, cancellationToken: token)
            .ConfigureAwait(false);
        TextContentBlock text = Assert.IsInstanceOfType<TextContentBlock>(
            Assert.ContainsSingle(wire.Content)
        );
        JsonObject envelope = JsonNode.Parse(text.Text)!.AsObject();
        Assert.AreEqual(envelope["ok"]!.GetValue<bool>(), wire.IsError != true, text.Text);
        return envelope;
    }

    internal async Task<JsonObject> DataAsync(
        string name,
        JsonObject arguments,
        CancellationToken token
    ) => GitScenarioState.Data(await InvokeAsync(name, arguments, token).ConfigureAwait(false));

    internal static Task<JsonObject> RejectIncompatiblePreflightAsync(
        RunDatabase database,
        CancellationToken token
    ) => ReadLaunchAsync(
        new(database.StatePath, "owner", ["preflight", "--state", database.StatePath],
            expectedExit: 1), token
    );

    internal static Task<JsonObject> RejectInvalidInitializationAsync(
        string configurationPath,
        string statePath,
        CancellationToken token
    ) => ReadLaunchAsync(
        new(statePath, "owner", ["init", "--config", configurationPath], expectedExit: 1,
            workingDirectory: Path.GetDirectoryName(configurationPath)), token
    );

    internal static Task<JsonObject> InitializeAsync(
        string configurationPath,
        string statePath,
        string workingDirectory,
        CancellationToken token
    ) => ReadLaunchAsync(
        new(statePath, "owner", ["init", "--config", configurationPath], expectedExit: 0,
            workingDirectory: workingDirectory), token
    );

    internal static Task<JsonObject> RejectRetainedRecoveryAsync(
        RunDatabase database,
        string command,
        string reservation,
        CancellationToken token
    ) => ReadLaunchAsync(
        new(database.StatePath, "owner",
            command == "reconcile"
                ? [command, "--state", database.StatePath, "--reservation", reservation]
                : [command, "--state", database.StatePath],
            expectedExit: 1), token
    );

    private static async Task<JsonObject> ReadLaunchAsync(
        SerializedToolHost host,
        CancellationToken token
    )
    {
        try
        {
            host.process.StandardInput.Close();
            host.output = host.process.StandardOutput.ReadToEndAsync(CancellationToken.None);
            await host.process.WaitForExitAsync(token).ConfigureAwait(false);
            Assert.AreEqual(host.expectedExit, host.process.ExitCode);
            return JsonNode
                .Parse(await host.output.WaitAsync(token).ConfigureAwait(false))!
                .AsObject();
        }
        catch (Exception exception)
        {
            host.primary = exception;
            Console.Error.WriteLine("PHASE8_PRIMARY " + Json.ExceptionDetails(exception));
            Console.WriteLine("PHASE8_PRESERVED_STATE " + host.statePath);
            throw;
        }
        finally
        {
            await host.DisposeAsync().ConfigureAwait(false);
        }
    }

    public ValueTask DisposeAsync() => new(CloseAsync());

    private async Task CloseAsync()
    {
        List<Exception> failures = [];
        bool forced = false;
        async Task Stage(Func<Task> action)
        {
            try
            {
                await action().ConfigureAwait(false);
            }
            catch (Exception error)
            {
                failures.Add(error);
            }
        }
        Task? disposing = null;
        if (client is not null)
        {
            disposing = client.DisposeAsync().AsTask();
            await Stage(() => disposing.WaitAsync(TimeSpan.FromSeconds(5))).ConfigureAwait(false);
        }
        await Stage(() =>
            {
                process.StandardInput.Close();
                return Task.CompletedTask;
            })
            .ConfigureAwait(false);
        await Stage(async () =>
            {
                try
                {
                    await process
                        .WaitForExitAsync()
                        .WaitAsync(TimeSpan.FromSeconds(5))
                        .ConfigureAwait(false);
                }
                finally
                {
                    if (!process.HasExited)
                    {
                        forced = true;
                        process.Kill(entireProcessTree: true);
                        await process
                            .WaitForExitAsync()
                            .WaitAsync(TimeSpan.FromSeconds(5))
                            .ConfigureAwait(false);
                    }
                }
            })
            .ConfigureAwait(false);
        string? stderr = null;
        await Stage(async () =>
                stderr = await error.WaitAsync(TimeSpan.FromSeconds(5)).ConfigureAwait(false)
            )
            .ConfigureAwait(false);
        if (output is not null)
        {
            await Stage(async () =>
                    _ = await output.WaitAsync(TimeSpan.FromSeconds(5)).ConfigureAwait(false)
                )
                .ConfigureAwait(false);
        }
        if (disposing is { IsCompleted: false })
        {
            await Stage(() => disposing.WaitAsync(TimeSpan.FromSeconds(5))).ConfigureAwait(false);
        }
        int? exit = process.HasExited ? process.ExitCode : null;
        _ = birthTicks;
        _ = statePath;
        bool failed =
            primary is not null
            || failures.Count != 0
            || forced
            || exit != expectedExit;
        if (failed && !string.IsNullOrWhiteSpace(stderr))
        {
            Console.Error.WriteLine("TOOL_HOST_STDERR " + stderr);
            primary?.Data["ToolHostStandardError"] = stderr;
        }
        process.Dispose();
        foreach (Exception failure in failures)
        {
            Console.Error.WriteLine(
                "TOOL_HOST_CLEANUP_SECONDARY " + Json.ExceptionDetails(failure)
            );
        }
        if (primary is null)
        {
            if (failures.Count != 0)
            {
                throw new AggregateException(failures);
            }
            Assert.IsFalse(forced);
            Assert.AreEqual(expectedExit, exit);
        }
    }
}
