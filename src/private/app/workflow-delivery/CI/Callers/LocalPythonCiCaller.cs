using System.Text.Json;
using System.Text.Json.Nodes;
using WorkflowDelivery.Repository;

namespace WorkflowDelivery.CI.Callers;

internal static class LocalPythonCiCaller
{
    internal static int? Run(string[] args, TextWriter output)
    {
        if (args is not ["ci", "caller-local-python", var root, var directory, var transfer,
                var workingDirectory,
                var data, var uv, var python, var dotnet, var mise,
                var node, var pwsh]) return null;
        if (!OperatingSystem.IsLinux())
            throw new InvalidDataException("The current local Python group supports Linux.");
        return ExecuteAsync(root, directory, transfer, workingDirectory, data == "-" ? null : data,
            new(uv, python, dotnet), mise, node, pwsh, output,
            CancellationToken.None).GetAwaiter().GetResult();
    }

    internal static string ToolData(string? selected, string workingDirectory)
    {
        selected ??= Environment.GetEnvironmentVariable("MISE_DATA_DIR");
        if (selected is null)
        {
            string shared = Environment.GetEnvironmentVariable("XDG_DATA_HOME") ??
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                    ".local", "share");
            selected = Path.Combine(shared, "mise");
        }
        return Path.GetFullPath(selected, CiCallerTransfer.Absolute(workingDirectory));
    }

    internal static async Task<int> ExecuteAsync(string root, string directory, string transfer,
        string workingDirectory, string? data, PythonQueryTools tools, string mise,
        string node, string pwsh,
        TextWriter output, CancellationToken token)
    {
        CiCallerTransfer.External(root, directory);
        GitRevision head = (await GitMaterialization.BindHeadAsync(root, token)).Revision;
        CiCallerControl control = CiCallerTransfer.Read(Path.Combine(directory,
            "control-build", "control.json"), TransferJson.Default.CiCallerControl);
        GitRevision source = (await GitMaterialization.BindHeadAsync(control.Root, token))
            .Revision;
        if (source.Commit != head.Commit || control.Transfer != transfer ||
            control.Application != CiCallerTransfer.Application(transfer))
            throw new InvalidDataException("Local control differs from committed full source.");
        string scopePath = Path.Combine(directory, "scope.json");
        var scope = new JsonObject
        {
            ["base"] = head.Commit,
            ["candidate"] = head.Commit,
            ["full"] = true,
            ["changed_paths"] = new JsonArray(),
            ["endpoint_owners"] = new JsonObject
            {
                ["basis"] = Endpoint(head.Commit),
                ["candidate"] = Endpoint(head.Commit),
            },
        };
        File.WriteAllText(scopePath, scope.ToJsonString() + "\n");
        string state = Path.Combine(directory, "group");
        await PythonCiCaller.MaterializeAsync(root, scopePath, transfer, state, token);
        await PythonCiCaller.PlanAsync(state, scopePath, tools, token);
        CiOutcome outcome = await PythonCiCaller.ExecuteAsync(state,
            ToolData(data, workingDirectory),
            mise == "-" ? null : mise, node == "-" ? null : node,
            pwsh == "-" ? null : pwsh, token);
        output.WriteLine(JsonSerializer.Serialize(outcome, TransferJson.Default.CiOutcome));
        return outcome.Satisfied ? 0 : 1;
    }

    private static JsonObject Endpoint(string commit) => new()
    { ["revision"] = commit, ["paths"] = new JsonArray() };
}
