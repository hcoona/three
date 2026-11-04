using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Data.Sqlite;

namespace LiteAgensh;

public static class Program
{
    public static async Task<int> Main(string[] args)
    {
        if (args.Length == 0 || args[0] is "--help" or "-h" or "help")
        {
            Console.WriteLine(
                "LiteAgensh: local C# coordination for native Copilot workers.\n"
                    + "\n"
                    + "init --config ABSOLUTE_JSON_FILE\n"
                    + "run|pause|resume|reconnect|status|preflight|calibrate|finalize|cleanup "
                    + "--state ABSOLUTE_STATE_DIRECTORY\n"
                    + "tool --state ABSOLUTE_STATE_DIRECTORY --name TOOL --json JSON_OBJECT\n"
                    + "mcp --state ABSOLUTE_STATE_DIRECTORY --actor MEMBER\n"
                    + "\n"
                    + "init preserves an existing checkout and does not start models.\n"
                    + "run/resume/reconnect may spend native Copilot credits; "
                    + "verified limits/usage are required.\n"
                    + "preflight performs non-model native contract checks only.\n"
                    + "calibrate is PAID: one tool-disabled READY turn, a 30-credit native "
                    + "soft cap and a two-minute turn deadline. Unknown usage blocks it.\n"
                    + "pause/finalize retain work. "
                    + "cleanup is explicit and owned-resource-specific.\n"
                    + "Tool invocations are bound to owner; "
                    + "internal MCP helpers are bound at launch."
            );
            return 0;
        }

        bool isMcp = args[0] == "mcp";
        using CancellationTokenSource cancellation = new();
        ConsoleCancelEventHandler handler = (_, eventArgs) =>
        {
            eventArgs.Cancel = true;
            cancellation.Cancel();
        };
        Console.CancelKeyPress += handler;
        try
        {
            Dictionary<string, string> options = ParseOptions(args[1..]);
            JsonObject result;
            if (args[0] == "init")
            {
                RequireOptions(options, ["--config"]);
                string text = await File.ReadAllTextAsync(options["--config"], cancellation.Token)
                    .ConfigureAwait(false);
                RunConfiguration configuration =
                    JsonSerializer.Deserialize<RunConfiguration>(text, Json.Options)
                    ?? throw new ArgumentException("Configuration must be a JSON object.");
                configuration.Validate();
                RunDatabase database = new(configuration.StatePath, configuration);
                LocalTools tools = new(database);
                Coordinator coordinator = new(database, tools.Collaboration, tools.Git);
                result = await coordinator
                    .InitializeAsync(cancellation.Token)
                    .ConfigureAwait(false);
            }
            else
            {
                RequireOptions(
                    options,
                    args[0] switch
                    {
                        "mcp" => ["--state", "--actor"],
                        "tool" => ["--state", "--name", "--json"],
                        "run"
                        or "resume"
                        or "reconnect"
                        or "status"
                        or "pause"
                        or "preflight"
                        or "calibrate"
                        or "finalize"
                        or "cleanup" => ["--state"],
                        _ => throw new ArgumentException(
                            "Unknown command. Run --help for supported syntax."
                        ),
                    }
                );
                RunDatabase database = new(options["--state"]);
                if (isMcp)
                {
                    await McpHost
                        .RunAsync(database, options["--actor"], cancellation.Token)
                        .ConfigureAwait(false);
                    return 0;
                }

                LocalTools tools = new(database);
                Coordinator coordinator = new(database, tools.Collaboration, tools.Git);
                result = args[0] switch
                {
                    "run" or "resume" or "reconnect" => await coordinator
                        .RunAsync(args[0], cancellation.Token)
                        .ConfigureAwait(false),
                    "status" => coordinator.Status(),
                    "pause" => coordinator.RequestControl("pause"),
                    "preflight" => await coordinator
                        .PreflightAsync(cancellation.Token)
                        .ConfigureAwait(false),
                    "calibrate" => await coordinator
                        .CalibrateAsync(cancellation.Token)
                        .ConfigureAwait(false),
                    "finalize" => await coordinator
                        .FinalizeAsync(cancellation.Token)
                        .ConfigureAwait(false),
                    "cleanup" => await coordinator
                        .CleanupAsync(cancellation.Token)
                        .ConfigureAwait(false),
                    "tool" => await tools
                        .InvokeAsync(
                            "owner",
                            options["--name"],
                            JsonNode.Parse(options["--json"])?.AsObject()
                                ?? throw new ArgumentException("--json must contain an object."),
                            cancellation.Token
                        )
                        .ConfigureAwait(false),
                    _ => throw new ArgumentException(
                        "Unknown command. Run --help for supported syntax."
                    ),
                };
            }

            Console.WriteLine(result.ToJsonString(Json.Options));
            return result["ok"]?.GetValue<bool>() == true ? 0 : 1;
        }
        catch (OperationCanceledException) when (cancellation.IsCancellationRequested)
        {
            WriteError(
                isMcp,
                "cancelled",
                "The operation was cancelled; inspect retained run status before replacement."
            );
            return 1;
        }
        catch (Exception exception)
            when (exception
                    is ArgumentException
                        or IOException
                        or InvalidOperationException
                        or JsonException
                        or OperationException
                        or SqliteException
                        or System.ComponentModel.Win32Exception
                        or TimeoutException
            )
        {
            WriteError(
                isMcp,
                exception is OperationException operation ? operation.Code : "failed",
                exception.Message
            );
            return 1;
        }
        finally
        {
            Console.CancelKeyPress -= handler;
        }
    }

    private static Dictionary<string, string> ParseOptions(string[] args)
    {
        if (args.Length % 2 != 0)
        {
            throw new ArgumentException("Options require separate --name value pairs.");
        }

        Dictionary<string, string> options = new(StringComparer.Ordinal);
        for (int index = 0; index < args.Length; index += 2)
        {
            if (
                !args[index].StartsWith("--", StringComparison.Ordinal)
                || !options.TryAdd(args[index], args[index + 1])
            )
            {
                throw new ArgumentException("Option names must be unique --name value pairs.");
            }
        }

        return options;
    }

    private static void RequireOptions(Dictionary<string, string> options, string[] required)
    {
        if (options.Count != required.Length || required.Any(name => !options.ContainsKey(name)))
        {
            throw new ArgumentException($"Expected options: {string.Join(", ", required)}.");
        }
    }

    private static void WriteError(bool isMcp, string code, string message)
    {
        if (isMcp)
        {
            Console.Error.WriteLine(message);
        }
        else
        {
            Console.WriteLine(Operation.Failure(code, message).ToJsonString(Json.Options));
        }
    }
}
