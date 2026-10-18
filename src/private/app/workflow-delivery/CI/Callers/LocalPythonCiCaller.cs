using System.Text.Json;
using System.Text.Json.Nodes;
using WorkflowDelivery.Repository;

namespace WorkflowDelivery.CI.Callers;

internal static class LocalPythonCiCaller
{
    internal static int? Run(string[] args, TextWriter output, TextWriter? diagnostics = null)
    {
        if (args is not ["ci", "caller-local-python", var root, var directory, var transfer,
                var uv, var python, var dotnet, var pwsh]) return null;
        if (!OperatingSystem.IsLinux())
            throw new InvalidDataException("The current local Python group supports Linux.");
        return ExecuteAsync(root, directory, transfer, new(uv, python, dotnet), pwsh, output,
            diagnostics, CancellationToken.None).GetAwaiter().GetResult();
    }

    internal static async Task<int> ExecuteAsync(string root, string directory, string transfer,
        PythonQueryTools tools, string pwsh, TextWriter output, TextWriter? diagnostics,
        CancellationToken token)
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
            pwsh == "-" ? null : pwsh, token, diagnostics: diagnostics);
        output.WriteLine(JsonSerializer.Serialize(outcome, TransferJson.Default.CiOutcome));
        return outcome.Satisfied ? 0 : 1;
    }

    private static JsonObject Endpoint(string commit) => new()
    { ["revision"] = commit, ["paths"] = new JsonArray() };
}
