using System.Text.Json;
using System.Text.Json.Nodes;
using LiteAgensh;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace LiteAgensh.Tests;

internal sealed class ScriptedNative
{
    private ScriptedNative(string directory) => DirectoryPath = directory;

    public string DirectoryPath { get; }

    public string Executable => Path.Combine(DirectoryPath, "bin", "ScriptedNative.exe");

    public static NativeCreditContract Contract =>
        new()
        {
            AgentVersion = "self-authored-native-1",
            EvidenceReference =
                "TEST ONLY: self-authored BCL ACP substitute; "
                + "not native observation or approved real credit fields",
            AppliedLimitDocumentation =
                "https://docs.github.com/en/copilot/how-tos/"
                + "copilot-cli/use-copilot-cli/set-session-limit",
            UsageReportDocumentation = "https://agentclientprotocol.com/protocol/session-setup",
            InformationalCommand = "usage",
            AppliedLimitPointer = "/limit",
            SessionUsagePointer = "/used",
            ReportCreditsPointer = "/usage/credits",
            ReportSessionPointer = "/sessionId",
            ReportUsageScope = "session-cumulative",
            UsageIsSessionCumulative = true,
        };

    public static async Task<ScriptedNative> BuildAsync(
        GitScenario fixture,
        string mode,
        CancellationToken token
    )
    {
        string directory = Path.Combine(fixture.Fixture.RootPath, "scripted-native");
        Directory.CreateDirectory(directory);
        File.WriteAllText(
            Path.Combine(directory, "global.json"),
            """{"sdk":{"version":"10.0.403","rollForward":"disable"}}"""
        );
        File.WriteAllText(
            Path.Combine(directory, "nuget.config"),
            """<configuration><packageSources><clear /></packageSources></configuration>"""
        );
        File.WriteAllText(
            Path.Combine(directory, "ScriptedNative.csproj"),
            """
            <Project Sdk="Microsoft.NET.Sdk">
              <PropertyGroup>
                <OutputType>Exe</OutputType>
                <TargetFramework>net10.0</TargetFramework>
                <ImplicitUsings>enable</ImplicitUsings>
                <Nullable>enable</Nullable>
                <NuGetAudit>false</NuGetAudit>
              </PropertyGroup>
            </Project>
            """
        );
        File.WriteAllText(Path.Combine(directory, "Program.cs"), WorkerSource);
        ProcessResult build = await new ProcessRunner(fixture.Database).RunAsync(
            Scenario.DotnetHost,
            [
                "build",
                "ScriptedNative.csproj",
                "--output",
                "bin",
                "--verbosity",
                "quiet",
                "--nologo",
            ],
            directory,
            TimeSpan.FromSeconds(60),
            token
        );
        Assert.AreEqual(0, build.ExitCode, build.StandardOutput + build.StandardError);
        ScriptedNative native = new(directory);
        native.SetMode(mode);
        fixture.Database.SaveConfiguration(
            fixture.Database.Configuration with
            {
                NativeExecutable = native.Executable,
                RunAiCreditAllowance = 120,
                CampaignAiCreditAllowance = 600,
                DurationSeconds = 120,
            }
        );
        fixture.Database.WriteMetadata(
            "native:credit_contract",
            JsonSerializer.Serialize(Contract, Json.Options)
        );
        return native;
    }

    public void SetMode(string mode) =>
        File.WriteAllText(
            Path.Combine(DirectoryPath, "bin", "scenario.json"),
            new JsonObject { ["mode"] = mode }.ToJsonString()
        );

    public HashSet<string> CaptureWire() =>
        Directory
            .EnumerateFiles(Path.Combine(DirectoryPath, "bin"), "wire-*.ndjson")
            .ToHashSet(StringComparer.Ordinal);

    public JsonObject[] Wire(IReadOnlySet<string>? before = null) =>
        Directory
            .EnumerateFiles(Path.Combine(DirectoryPath, "bin"), "wire-*.ndjson")
            .Where(path => before is null || !before.Contains(path))
            .SelectMany(File.ReadAllLines)
            .Where(line => line.Length > 0)
            .Select(line => JsonNode.Parse(line)!.AsObject())
            .ToArray();

    public string[] OrdinaryPrompts(IReadOnlySet<string>? before = null) =>
        Wire(before)
            .Where(message => message["method"]?.GetValue<string>() == "session/prompt")
            .Select(message => message["params"]!["prompt"]![0]!["text"]!.GetValue<string>())
            .Where(text => !text.StartsWith("/usage", StringComparison.Ordinal))
            .ToArray();

    private const string WorkerSource = """
        using System.Globalization;
        using System.Text.Json.Nodes;

        string scenarioPath = Path.Combine(AppContext.BaseDirectory, "scenario.json");
        string mode = JsonNode.Parse(File.ReadAllText(scenarioPath))!["mode"]!.GetValue<string>();
        string wire = Path.Combine(AppContext.BaseDirectory, $"wire-{Environment.ProcessId}-");
        int sequence = 0;
        string report = "";
        decimal limit = 0;
        for (int i = 0; i < args.Length; i++)
        {
            if (args[i] == "--usage-output-file") report = args[++i];
            else if (args[i] == "--max-ai-credits")
                limit = decimal.Parse(args[++i], CultureInfo.InvariantCulture);
        }
        if (mode == "reject-startup" &&
            args.Contains("--acp") && args.Contains("--usage-output-file"))
        {
            File.WriteAllText(
                Path.Combine(AppContext.BaseDirectory, $"rejected-{Environment.ProcessId}.txt"),
                "SCRIPTED startup incompatibility: --acp together with --usage-output-file");
            Console.Error.WriteLine(
                "SCRIPTED startup incompatibility: --acp together with --usage-output-file");
            Environment.ExitCode = 2;
            return;
        }
        string? session = null;
        decimal used = 0;
        JsonNode? held = null;
        void Send(JsonObject message) => Console.WriteLine(message.ToJsonString());
        void Reply(JsonNode id, JsonObject result) => Send(new()
        {
            ["jsonrpc"] = "2.0", ["id"] = id.DeepClone(), ["result"] = result,
        });
        void SaveUsage()
        {
            JsonObject value = new()
            {
                ["sessionId"] = session, ["usage"] = new JsonObject { ["credits"] = used },
            };
            File.WriteAllText(report, value.ToJsonString());
            if (session is not null)
                File.WriteAllText(Path.Combine(AppContext.BaseDirectory, session + ".json"),
                    new JsonObject { ["used"] = used }.ToJsonString());
        }
        SaveUsage();
        try
        {
            while (Console.ReadLine() is string line)
            {
                string entry = wire + (++sequence) + ".ndjson";
                File.WriteAllText(entry + ".tmp", line + Environment.NewLine);
                File.Move(entry + ".tmp", entry);
                JsonObject request = JsonNode.Parse(line)!.AsObject();
                string? method = request["method"]?.GetValue<string>();
                switch (method)
                {
                    case "initialize":
                        Reply(request["id"]!, new()
                        {
                            ["protocolVersion"] = 1,
                            ["agentInfo"] = new JsonObject
                                { ["version"] = "self-authored-native-1" },
                            ["agentCapabilities"] = new JsonObject
                                { ["loadSession"] = mode != "no-load" },
                        });
                        break;
                    case "session/new":
                    case "session/load":
                        session = method == "session/load"
                            ? request["params"]!["sessionId"]!.GetValue<string>()
                            : "scripted-" + Guid.NewGuid().ToString("N");
                        string saved = Path.Combine(AppContext.BaseDirectory, session + ".json");
                        if (File.Exists(saved))
                            used = JsonNode.Parse(File.ReadAllText(saved))!["used"]!
                                .GetValue<decimal>();
                        SaveUsage();
                        Reply(request["id"]!, new() { ["sessionId"] = session });
                        Send(new()
                        {
                            ["jsonrpc"] = "2.0", ["method"] = "session/update",
                            ["params"] = new JsonObject
                            {
                                ["sessionId"] = session,
                                ["update"] = new JsonObject
                                {
                                    ["sessionUpdate"] = "available_commands_update",
                                    ["availableCommands"] = new JsonArray(new JsonObject
                                    {
                                        ["name"] = "usage",
                                        ["description"] = "Scripted non-model information",
                                    }),
                                },
                            },
                        });
                        break;
                    case "session/prompt":
                        string text = request["params"]!["prompt"]![0]!["text"]!.GetValue<string>();
                        if (text == "/usage")
                        {
                            JsonObject result = new()
                                { ["stopReason"] = "end_turn", ["limit"] = limit };
                            if (mode != "unknown-usage") result["used"] = used;
                            if (mode == "wrong-limit") result["limit"] = limit + 1;
                            Reply(request["id"]!, result);
                        }
                        else if (mode == "hold")
                        {
                            held = request["id"]!.DeepClone();
                            File.WriteAllText(Path.Combine(AppContext.BaseDirectory,
                                $"active-{Environment.ProcessId}"), session);
                        }
                        else
                        {
                            used += 1;
                            SaveUsage();
                            Reply(request["id"]!, new() { ["stopReason"] = "end_turn" });
                        }
                        break;
                    case "session/cancel":
                        if (held is not null)
                        {
                            Reply(held, new() { ["stopReason"] = "cancelled" });
                            held = null;
                        }
                        break;
                }
            }
        }
        finally { SaveUsage(); }
        """;
}
