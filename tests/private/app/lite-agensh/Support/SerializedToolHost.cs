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

    private SerializedToolHost(RunDatabase database, string actor, bool preflight)
    {
        this.actor = actor;
        expectedExit = preflight ? 1 : 0;
        statePath = database.StatePath;
        string home = Path.Combine(database.StatePath, "phase8-owned-home");
        Directory.CreateDirectory(home);
        string global = Path.Combine(home, "global.gitconfig");
        if (!File.Exists(global))
        {
            File.WriteAllText(global, "");
        }
        ProcessStartInfo start = new(Path.Combine(AppContext.BaseDirectory, "LiteAgensh.exe"))
        {
            WorkingDirectory = database.StatePath,
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
        start.ArgumentList.Add(preflight ? "preflight" : "mcp");
        start.ArgumentList.Add("--state");
        start.ArgumentList.Add(database.StatePath);
        if (!preflight)
        {
            start.ArgumentList.Add("--actor");
            start.ArgumentList.Add(actor);
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
            Console.WriteLine(
                "PHASE8_PRODUCT_STARTED "
                    + new JsonObject
                    {
                        ["pid"] = process.Id,
                        ["birthTicks"] = birthTicks,
                        ["actor"] = actor,
                        ["mode"] = preflight ? "preflight" : "mcp",
                        ["state"] = statePath,
                        ["productSha256"] = Convert.ToHexString(
                            SHA256.HashData(File.ReadAllBytes(typeof(Program).Assembly.Location))
                        ),
                    }.ToJsonString()
            );
        }
        catch (Exception primary)
        {
            Console.Error.WriteLine("PHASE8_CONSTRUCTOR_PRIMARY " + Json.ExceptionDetails(primary));
            Console.WriteLine("PHASE8_PRESERVED_STATE " + statePath);
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
                    "PHASE8_CONSTRUCTOR_SECONDARY " + Json.ExceptionDetails(secondary)
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
        SerializedToolHost host = new(database, actor, false);
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
            Console.Error.WriteLine("PHASE8_PRIMARY " + Json.ExceptionDetails(exception));
            Console.WriteLine("PHASE8_PRESERVED_STATE " + database.StatePath);
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
        Console.WriteLine(
            "PHASE8_TOOL_RESULT "
                + new JsonObject
                {
                    ["actor"] = actor,
                    ["operation"] = name,
                    ["envelope"] = envelope.DeepClone(),
                }.ToJsonString()
        );
        return envelope;
    }

    internal async Task<JsonObject> DataAsync(
        string name,
        JsonObject arguments,
        CancellationToken token
    ) => GitScenarioState.Data(await InvokeAsync(name, arguments, token).ConfigureAwait(false));

    internal static async Task<JsonObject> RejectIncompatiblePreflightAsync(
        RunDatabase database,
        CancellationToken token
    )
    {
        SerializedToolHost host = new(database, "owner", true);
        try
        {
            host.process.StandardInput.Close();
            host.output = host.process.StandardOutput.ReadToEndAsync(CancellationToken.None);
            await host.process.WaitForExitAsync(token).ConfigureAwait(false);
            Assert.AreEqual(1, host.process.ExitCode);
            return JsonNode
                .Parse(await host.output.WaitAsync(token).ConfigureAwait(false))!
                .AsObject();
        }
        catch (Exception exception)
        {
            host.primary = exception;
            Console.Error.WriteLine("PHASE8_PRIMARY " + Json.ExceptionDetails(exception));
            Console.WriteLine("PHASE8_PRESERVED_STATE " + database.StatePath);
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
        Console.WriteLine(
            "PHASE8_PRODUCT_CLEANUP "
                + new JsonObject
                {
                    ["pid"] = process.Id,
                    ["birthTicks"] = birthTicks,
                    ["actor"] = actor,
                    ["state"] = statePath,
                    ["joined"] = process.HasExited,
                    ["exitCode"] = exit,
                    ["expectedExitCode"] = expectedExit,
                    ["stderrDrained"] = error.IsCompleted,
                    ["stderr"] = stderr,
                    ["forced"] = forced,
                    ["secondaryCount"] = failures.Count,
                }.ToJsonString()
        );
        process.Dispose();
        foreach (Exception failure in failures)
        {
            Console.Error.WriteLine("PHASE8_CLEANUP_SECONDARY " + Json.ExceptionDetails(failure));
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
