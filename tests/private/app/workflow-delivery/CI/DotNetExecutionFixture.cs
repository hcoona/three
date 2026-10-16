using System.Text.Json;
using System.Text.Json.Nodes;
using WorkflowDelivery.CI;
using WorkflowDelivery.Platform;
using WorkflowDelivery.Repository;
using GitFixture = WorkflowDelivery.Tests.Repository.GitReaderTests.GitFixture;

namespace WorkflowDelivery.Tests.CI;

internal sealed class DotNetExecutionFixture(GitFixture repo, CiPlan plan,
    bool ownsRepository = true) : IDisposable
{
    internal GitFixture Repo { get; } = repo;
    internal CiPlan Plan { get; } = plan;
    internal string Scratch { get; } =
        Directory.CreateTempSubdirectory("workflow-dotnet-run-").FullName;
    // A row reading a shared repository writes its simulated native output privately.
    private readonly string? outputRoot = ownsRepository ? null :
        Directory.CreateTempSubdirectory("workflow-dotnet-output-").FullName;
    internal string Output => Path.Combine(outputRoot ?? Repo.Directory, "native-output",
        "product.bin");
    internal List<NativeCommand> Commands { get; } = [];
    internal Func<NativeCommand, NativeCommandResult?>? Override { get; set; }
    internal Action<JsonObject>? ModifyResponse { get; set; }
    internal Action<JsonObject>? ModifyBatchResponse { get; set; }
    internal Action<int, JsonObject>? ModifyTestReceipt { get; set; }
    internal DotNetRunRequest Request => new(Repo.Directory, Scratch, 60);

    internal static async Task<DotNetExecutionFixture> CreateAsync(CancellationToken token,
        string capability = "MTP")
    {
        GitFixture repo = await GitFixture.CreateAsync(token);
        try
        {
            string commit = await CommitAsync(repo);
            return new(repo, CreatePlan(repo.Directory, commit, capability));
        }
        catch { repo.Dispose(); throw; }
    }

    internal static async Task<string> CommitAsync(GitFixture repo)
    {
        await repo.SetAsync("product.csproj", "<Project />");
        string commit = await repo.CommitAsync();
        await repo.GitAsync("reset", "--hard", commit);
        return commit;
    }

    // Reads a class-owned committed repository; the row owns only its scratch and output.
    internal static DotNetExecutionFixture Shared(GitFixture repo, string commit,
        string capability = "MTP") =>
        new(repo, CreatePlan(repo.Directory, commit, capability), ownsRepository: false);

    internal static CiPlan CreatePlan(string root, string candidate, string capability,
        Dictionary<string, string>? globals = null, string project = "product.csproj")
    {
        globals ??= new(StringComparer.Ordinal)
        {
            ["Configuration"] = "Debug",
            ["ContinuousIntegrationBuild"] = "true",
            ["RestoreLockedMode"] = "true",
            ["TargetFramework"] = "net10.0",
            ["IsGraphBuild"] = "true",
        };
        var native = new DotNetGraphNode(new(Path.Combine(root, project), globals), false,
            new("Debug", globals.GetValueOrDefault("TargetFramework", ""), ""), "", "", [],
            capability);
        return new(candidate, candidate, DotNetFactsAssembler.Scope,
            DotNetChecks.Expand(project, native).Select(check => new PlannedCheck(check, [],
                [new(project, candidate, project)], [CheckOrigin.NativeRetained])).ToArray());
    }

    internal Task<DotNetRunResult> RunAsync(CancellationToken token, CiPlan? plan = null) =>
        DotNetExecution.RunAsync(plan ?? Plan, Request, ExecuteAsync, token);

    internal async Task<NativeCommandResult> ExecuteAsync(NativeCommand command,
        CancellationToken token)
    {
        Commands.Add(command);
        if (Override?.Invoke(command) is { } result) return result;
        Directory.CreateDirectory(Path.GetDirectoryName(Output)!);
        await File.WriteAllTextAsync(Output, "native output", token);
        bool batch = command.Arguments.Contains("-target:BuildSelected");
        var targets = new JsonObject();
        foreach (string name in new[] { "Build", "GetTargetPath", "InvokeTestingPlatform",
                     "VSTest" })
            targets[name] = new JsonObject
            {
                ["Result"] = "Success",
                ["Items"] = name == "GetTargetPath" ? new JsonArray(new JsonObject
                { ["Identity"] = Output, ["FullPath"] = Output }) : new JsonArray()
            };
        var response = new JsonObject { ["TargetResults"] = targets };
        ModifyResponse?.Invoke(response);
        if (batch)
        {
            DotNetBuildRequest request = JsonSerializer.Deserialize(await File.ReadAllTextAsync(
                Path.Combine(Scratch, "build-request.json"), token),
                TransferJson.Default.DotNetBuildRequest)!;
            var entries = new JsonArray();
            foreach (DotNetNodeIdentity identity in request.Entries)
                entries.Add(new JsonObject
                {
                    ["identity"] = JsonSerializer.SerializeToNode(identity,
                        TransferJson.Default.DotNetNodeIdentity),
                    ["build"] = Code(targets, "Build"),
                    ["getTargetPath"] = Code(targets, "GetTargetPath"),
                    ["outputs"] = Outputs(targets),
                });
            var projection = new JsonObject { ["entries"] = entries };
            ModifyBatchResponse?.Invoke(projection);
            await File.WriteAllTextAsync(request.Destination, projection.ToJsonString(), token);
            return new(NativeTermination.Exited, 0, "native batch stdout", "", 0.1, null);
        }
        DotNetTestRequest tests = JsonSerializer.Deserialize(await File.ReadAllTextAsync(
            Path.Combine(Scratch, "test-request.json"), token),
            TransferJson.Default.DotNetTestRequest)!;
        Directory.CreateDirectory(tests.Destination);
        for (int index = 0; index < tests.Entries.Length; index++)
        {
            DotNetTestEntry entry = tests.Entries[index];
            var receipt = new JsonObject
            {
                ["identity"] = JsonSerializer.SerializeToNode(entry.Identity,
                    TransferJson.Default.DotNetNodeIdentity),
                ["testTarget"] = entry.TestTarget,
                ["overall"] = "Success",
                ["build"] = Code(targets, "Build"),
                ["getTargetPath"] = Code(targets, "GetTargetPath"),
                ["test"] = Code(targets, entry.TestTarget),
                ["outputs"] = Outputs(targets),
                ["log"] = "ordinary product log " + index,
            };
            ModifyTestReceipt?.Invoke(index, receipt);
            await File.WriteAllTextAsync(DotNetTestTask.Receipt(tests.Destination, index),
                receipt.ToJsonString(), token);
        }
        return new(NativeTermination.Exited, 0, "native test batch stdout", "", 0.1, null);

        static string Code(JsonObject targets, string name) =>
            targets[name]?["Result"]?.GetValue<string>() ?? "Missing";
        static JsonArray Outputs(JsonObject targets) => new((targets["GetTargetPath"]?["Items"]
            ?.AsArray() ?? []).Select(item => (JsonNode?)new JsonObject
            {
                ["identity"] = item?["Identity"]?.DeepClone(),
                ["fullPath"] = item?["FullPath"]?.DeepClone(),
            }).ToArray());
    }

    public void Dispose()
    {
        if (ownsRepository) Repo.Dispose();
        if (outputRoot is not null) Directory.Delete(outputRoot, true);
        Directory.Delete(Scratch, true);
    }
}
