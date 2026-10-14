using System.Text.Json;
using System.Text.Json.Nodes;
using WorkflowDelivery.Repository;

namespace WorkflowDelivery.CI.Callers;

internal static class DotNetCiCaller
{
    internal const string Scope = "dotnet/native-endpoint-selection-v1";

    // Native shell builds once (Build + TargetPath), copies the complete output and archives
    // helpers. This phase records the already prepared distribution; it cannot build itself.
    internal static void Bootstrap(string root, string directory, string transfer)
    {
        CiCallerTransfer.External(root, directory);
        string application = CiCallerTransfer.Application(transfer);
        if (!File.Exists(Path.Combine(transfer, "python-control.zip")) ||
            !File.Exists(Path.Combine(transfer, "ruby", "facts.rb")))
            throw new InvalidDataException("Candidate native helper transfer is incomplete.");
        Directory.CreateDirectory(directory);
        if (File.Exists(Path.Combine(directory, "control.json")))
            throw new InvalidDataException("Candidate control has already been registered.");
        CiCallerTransfer.Write(Path.Combine(directory, "control.json"),
            new CiCallerControl(application, CiCallerTransfer.Absolute(root), transfer),
            TransferJson.Default.CiCallerControl);
        CiCallerTransfer.Output("application", application);
        CiCallerTransfer.Output("transfer", transfer);
    }

    // Actions prepares exact endpoint SDKs, tools and locked dirs.proj restore first.
    internal static string Prepare(string root, string directory, string endpointsPath)
    {
        CiCallerTransfer.External(root, directory);
        CiCallerControl control = CiCallerTransfer.Read(Path.Combine(directory, "control.json"),
            TransferJson.Default.CiCallerControl);
        if (control.Root != CiCallerTransfer.Absolute(root) ||
            CiCallerTransfer.Application(control.Transfer) != control.Application)
            throw new InvalidDataException("Prepared control has a different source checkout.");
        CiCallerEndpoints endpoints = CiCallerTransfer.Read(endpointsPath,
            TransferJson.Default.CiCallerEndpoints);
        CiCallerTransfer.Commit(endpoints.Basis.Reference);
        CiCallerTransfer.Commit(endpoints.Candidate.Reference);
        if (endpoints.Full && endpoints.Basis.Reference != endpoints.Candidate.Reference)
            throw new InvalidDataException("Explicit full has a different basis.");
        string state = CiCallerTransfer.Fresh(Path.Combine(directory, "planning"));
        DotNetReadFiles files(string name) => new(control.Application,
            Path.Combine(state, name + ".proj"), Path.Combine(state, name + "-graph-request.json"),
            Path.Combine(state, name + "-graph.json"), 900);
        DotNetReadFiles candidateFiles = files("candidate");
        var request = new DotNetGroupRequest(endpoints.Basis, endpoints.Candidate, endpoints.Full,
            Path.Combine(state, "preliminary-scope.json"),
            endpoints.Basis == endpoints.Candidate ? candidateFiles : files("basis"),
                candidateFiles,
            new(endpoints.Candidate.Directory, CiCallerTransfer.Fresh(Path.Combine(state,
                "completion")), 900));
        string path = Path.Combine(state, "request.json");
        CiCallerTransfer.Write(path, request, TransferJson.Default.DotNetGroupRequest);
        CiCallerTransfer.Output("request", path);
        return path;
    }

    internal static async Task<JsonElement> FinishAsync(string root, string scopePath,
        string requestPath, string transfer, CancellationToken token)
    {
        DotNetGroupRequest request = CiCallerTransfer.Read(requestPath,
            TransferJson.Default.DotNetGroupRequest);
        GitComparison comparison = await CiCallerTransfer.ComparisonAsync(root, scopePath, token);
        if (comparison.Basis.Commit != request.Basis.Reference ||
            comparison.Candidate.Commit != request.Candidate.Reference ||
            CiCallerTransfer.Application(transfer) != request.CandidateFiles.Assembly)
            throw new
                InvalidDataException(".NET planning request has a different original binding.");
        JsonElement preliminary = CiCallerTransfer.Read(scopePath,
            TransferJson.Default.JsonElement);
        CiCallerTransfer.Write(request.ScopePath, preliminary, TransferJson.Default.JsonElement);
        DotNetGroupReadback readback = await DotNetGroupReader.ReadAsync(request, token);
        CiCallerTransfer.Plan(readback.Plan, request.Basis.Reference,
            request.Candidate.Reference, Scope);
        CiCallerTransfer.Write(Path.Combine(transfer, "dotnet-plan.json"), readback.Plan,
            TransferJson.Default.CiPlan);
        CiCallerTransfer.Write(Path.Combine(Path.GetDirectoryName(requestPath)!, "readback.json"),
            readback, TransferJson.Default.DotNetGroupReadback);
        JsonObject joined = JsonNode.Parse(preliminary.GetRawText())!.AsObject();
        joined["endpoint_owners"]!["basis"]!["paths"] = JsonSerializer.SerializeToNode(
            readback.Basis, TransferJson.Default.ScopeCoordinateArray);
        joined["endpoint_owners"]!["candidate"]!["paths"] = JsonSerializer.SerializeToNode(
            readback.Candidate, TransferJson.Default.ScopeCoordinateArray);
        joined["scopes"]!["dotnet"] = readback.Plan.Checks.Length != 0;
        joined["reasons"]!["dotnet"] = new JsonArray(readback.Plan.Checks.SelectMany(c => c.Reasons)
            .Select(r => $"{r.Path} -> {r.Project} ({r.Revision})").Distinct(StringComparer.Ordinal)
            .Order(StringComparer.Ordinal).Select(r => (JsonNode?)JsonValue.Create(r)).ToArray());
        using JsonDocument document = JsonDocument.Parse(joined.ToJsonString());
        return document.RootElement.Clone();
    }

    internal static async Task<CiOutcome> ExecuteAsync(string root, string transfer,
        string directory, string basis, string candidate, CancellationToken token,
        Func<CiPlan, DotNetRunRequest, CancellationToken, Task<DotNetGroupRunResult>>? execute =
            null)
    {
        CiCallerTransfer.External(root, directory);
        _ = CiCallerTransfer.Application(transfer);
        CiPlan plan = CiCallerTransfer.Read(Path.Combine(transfer, "dotnet-plan.json"),
            TransferJson.Default.CiPlan);
        CiCallerTransfer.Plan(plan, basis, candidate, Scope);
        if ((await new GitReader(root).ReadAsync("HEAD", token)).Commit != candidate ||
            (await new GitReader(root).ReadAsync(basis, token)).Commit != basis)
            throw new InvalidDataException(".NET execution has a different original checkout.");
        CiCallerTransfer.Fresh(directory);
        var request = new DotNetRunRequest(root,
            CiCallerTransfer.Fresh(Path.Combine(directory, "execution")), 1200);
        CiCallerTransfer.Write(Path.Combine(directory, "request.json"), request,
            TransferJson.Default.DotNetRunRequest);
        DotNetGroupRunResult result = await (execute ?? DotNetGroupExecution.RunAsync)(plan,
            request, token);
        CiCallerTransfer.Write(Path.Combine(directory, "result.json"), result,
            TransferJson.Default.DotNetGroupRunResult);
        CiCallerTransfer.Write(Path.Combine(directory, "results.json"), result.Results,
            TransferJson.Default.CheckResultArray);
        if (result.Candidate != candidate)
            throw new InvalidDataException(".NET result has a different original candidate.");
        CiOutcome outcome = CiCallerTransfer.Outcome(plan, result.Results, result.Outcome);
        CiCallerTransfer.Write(Path.Combine(directory, "outcome.json"), outcome,
            TransferJson.Default.CiOutcome);
        return outcome;
    }
}
