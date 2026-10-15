using System.Text.Json;
using WorkflowDelivery.Platform;
using WorkflowDelivery.Repository;

namespace WorkflowDelivery.CI.Callers;

// Preparation and the default Debug resource query straddle native SDK setup.
internal static class ControlCiCaller
{
    internal static int? Run(string[] args, TextWriter output)
    {
        if (args is ["ci", "caller-control", "prepare", var root, var directory,
                var eventName, var eventPath, var candidate])
        {
            using JsonDocument payload = JsonDocument.Parse(File.ReadAllText(eventPath));
            ControlTestInputRequest request = PrepareAsync(root, directory,
                GitHubComparison.Resolve(eventName, payload.RootElement, candidate),
                CancellationToken.None).GetAwaiter().GetResult();
            output.WriteLine(JsonSerializer.Serialize(request,
                TransferJson.Default.ControlTestInputRequest));
            return 0;
        }
        return null;
    }

    internal static async Task<ControlTestInputRequest> PrepareAsync(string root,
        string directory, CiComparison requested, CancellationToken token)
    {
        root = CiCallerTransfer.Absolute(root);
        CiCallerTransfer.External(root, directory);
        var reader = new GitReader(root);
        GitComparison comparison = await reader.CompareAsync(requested.Basis,
            requested.Candidate, token);
        if (comparison.Basis.Commit != requested.Basis ||
            comparison.Candidate.Commit != requested.Candidate ||
            await reader.ResolveAsync("HEAD", token) != requested.Candidate ||
            (requested.Full && requested.Basis != requested.Candidate))
            throw new InvalidDataException("Control preparation requires the exact candidate.");
        ControlTestInputs.RequireOwner(comparison.Candidate, false);
        bool basisPresent = ControlTestInputs.RequireOwner(comparison.Basis, true);
        directory = CiCallerTransfer.Fresh(directory);
        var candidate = new MaterializedEndpoint(Path.Combine(directory, "candidate"),
            requested.Candidate);
        var basis = requested.Basis == requested.Candidate ? candidate :
            new MaterializedEndpoint(Path.Combine(directory, "basis"), requested.Basis);
        // Git creates each fresh detached checkout at the exact commit; consumers bind it.
        foreach (MaterializedEndpoint endpoint in new[] { candidate, basis }.Distinct())
            await CiCallerTransfer.RunAsync(new("git", root, ["worktree", "add", "--detach",
                endpoint.Directory, endpoint.Reference], 300), directory,
                Path.GetFileName(endpoint.Directory) + "-checkout", token);
        CiCallerTransfer.Write(Path.Combine(directory, "group-endpoints.json"),
            new CiCallerEndpoints(basis, candidate, requested.Full),
            TransferJson.Default.CiCallerEndpoints);
        var request = new ControlTestInputRequest(requested, root,
            basisPresent ? basis.Directory : null, candidate.Directory);
        string path = Path.Combine(directory, "request.json");
        CiCallerTransfer.Write(path, request, TransferJson.Default.ControlTestInputRequest);
        CiCallerTransfer.Output("request", path);
        CiCallerTransfer.Output("candidate_directory", candidate.Directory);
        CiCallerTransfer.Output("basis_directory", basis.Directory);
        CiCallerTransfer.Output("basis_separate",
            (basis != candidate).ToString().ToLowerInvariant());
        CiCallerTransfer.Output("basis", requested.Basis);
        CiCallerTransfer.Output("candidate", requested.Candidate);
        CiCallerTransfer.Output("full", requested.Full.ToString().ToLowerInvariant());
        return request;
    }
}
