using System.Text.Json;
using System.Text.Json.Serialization.Metadata;
using WorkflowDelivery.Repository;

namespace WorkflowDelivery.CI.Callers;

// Finite positional commands for current CI callers.
internal static class CiCallerCli
{
    internal static int? Run(string[] args, TextWriter output)
    {
        if (args.Length < 2 || args[0] != "ci" || !args[1].StartsWith("caller-",
            StringComparison.Ordinal))
            return null;
        CancellationToken token = CancellationToken.None;
        switch (args)
        {
            case ["ci", "caller-dotnet", "bootstrap", var root, var directory, var transfer]:
                DotNetCiCaller.Bootstrap(root, directory, transfer);
                return 0;
            case ["ci", "caller-dotnet", "prepare", var root, var directory, var endpoints]:
                output.WriteLine(DotNetCiCaller.Prepare(root, directory, endpoints));
                return 0;
            case ["ci", "caller-dotnet", "select", var root, var request, var control,
                var transfer, var scope]:
                output.WriteLine(DotNetCiCaller.SelectAsync(root, request, control, transfer,
                    scope, token).GetAwaiter().GetResult().ToJsonString());
                return 0;
            case ["ci", "caller-dotnet", "execute", var root, var transfer, var directory,
                var basis, var candidate]:
                return Outcome(DotNetCiCaller.ExecuteAsync(root, transfer, directory, basis,
                    candidate, token).GetAwaiter().GetResult());
            case ["ci", var caller, "materialize", var root, var scope, var transfer, var directory]
                when caller is "caller-python" or "caller-ruby" or "caller-node":
                Materialize(caller, root, scope, transfer, directory);
                return 0;
            case ["ci", "caller-ruby", "plan", var directory, var scope, var ruby, var gem,
                var bundle, var dotnet, var probe]:
                Write(RubyCiCaller.PlanAsync(directory, scope, new(ruby, gem, bundle, dotnet),
                    probe,
                    token).GetAwaiter().GetResult(), TransferJson.Default.RubyGroupReadback);
                return 0;
            case ["ci", "caller-ruby", "execute", var directory]:
                return Outcome(RubyCiCaller.ExecuteAsync(directory,
                    token).GetAwaiter().GetResult());
            case ["ci", "caller-python", "plan", var directory, var scope, var uv, var interpreter,
                var dotnet]:
                Write(PythonCiCaller.PlanAsync(directory, scope, new(uv, interpreter, dotnet),
                    token).GetAwaiter().GetResult(), TransferJson.Default.PythonGroupReadback);
                return 0;
            case ["ci", "caller-python", "execute", var directory, var miseData, var mise,
                var node, var pwsh]:
                return Outcome(PythonCiCaller.ExecuteAsync(directory, Optional(miseData),
                    Optional(mise),
                    Optional(node), Optional(pwsh), token).GetAwaiter().GetResult());
            case ["ci", "caller-node", "plan", var directory, var scope, var rubyInputs]:
                Write(NodeCiCaller.PlanAsync(directory, scope, Optional(rubyInputs),
                    token).GetAwaiter().GetResult(),
                    TransferJson.Default.NodeGroupReadback);
                return 0;
            case ["ci", "caller-node", "runtime", var root, var directory, var runtime]:
                Write(NodeCiCaller.RuntimeAsync(root, directory, runtime,
                    token).GetAwaiter().GetResult(),
                    TransferJson.Default.NodeRunRequest);
                return 0;
            case ["ci", "caller-node", "execute", var directory, var runtime]:
                return Outcome(NodeCiCaller.ExecuteRuntimeAsync(directory, runtime,
                    token).GetAwaiter().GetResult());
            case ["ci", "caller-node", "join", var directory]:
                return Outcome(NodeCiCaller.Join(directory));
            case ["ci", "caller-node", "retained", var root, var directory, var members,
                var adopted, var probe, var pnpm]:
                string[] selected = JsonSerializer.Deserialize(members,
                    TransferJson.Default.StringArray)
                    ?? throw new InvalidDataException("Missing retained members.");
                NodeCiCaller.RetainedAsync(root, directory, selected, adopted, probe, pnpm, token)
                    .GetAwaiter().GetResult();
                return 0;
            default:
                throw new
                    InvalidDataException("Unsupported finite CI caller command or argument count.");
        }

        void Materialize(string caller, string root, string scope, string transfer, string
            directory)
        {
            if (caller == "caller-python") Write(PythonCiCaller.MaterializeAsync(root, scope,
                transfer,
                directory, token).GetAwaiter().GetResult(),
                    TransferJson.Default.PythonCiCallerContext);
            else if (caller == "caller-ruby") Write(RubyCiCaller.MaterializeAsync(root, scope,
                transfer,
                directory, token).GetAwaiter().GetResult(),
                    TransferJson.Default.RubyCiCallerContext);
            else Write(NodeCiCaller.MaterializeAsync(root, scope, transfer, directory, token)
                .GetAwaiter().GetResult(), TransferJson.Default.NodeCiCallerContext);
        }
        int Outcome(CiOutcome outcome)
        {
            Write(outcome, TransferJson.Default.CiOutcome);
            return outcome.Satisfied ? 0 : 1;
        }
        void Write<T>(T value, JsonTypeInfo<T> type) =>
            output.WriteLine(JsonSerializer.Serialize(value, type));
        static string? Optional(string path) => path == "-" ? null :
            CiCallerTransfer.Absolute(path);
    }
}
