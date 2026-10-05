using WorkflowDelivery.Platform;

namespace WorkflowDelivery.Repository;

internal sealed record ControlTestInputRequest(CiComparison Comparison, string Repository,
    string? BasisDirectory, string CandidateDirectory);
internal sealed record ControlResourceEndpoint(string Revision, string Project, bool Present,
    MsBuildDimension? Dimension, string[] Inputs);
internal sealed record ControlTestInputResponse(CiComparison Comparison,
    ControlResourceEndpoint Basis, ControlResourceEndpoint Candidate);

// One concrete root-CI consumer. Git and MSBuild own its inventory and input semantics.
internal static class ControlTestInputs
{
    internal const string Project =
        "tests/private/app/workflow-delivery/WorkflowDelivery.Tests.csproj";

    internal static async Task<ControlTestInputResponse> ReadAsync(ControlTestInputRequest request,
        CancellationToken token)
    {
        GitComparison comparison = await new GitReader(request.Repository).CompareAsync(
            request.Comparison.Basis, request.Comparison.Candidate, token);
        GitRevision head = await new GitReader(request.Repository).ReadAsync("HEAD", token);
        if (comparison.Basis.Commit != request.Comparison.Basis ||
            comparison.Candidate.Commit != request.Comparison.Candidate ||
            head.Commit != comparison.Candidate.Commit ||
            (request.Comparison.Full && comparison.Basis.Commit != comparison.Candidate.Commit))
            throw new InvalidDataException(
                "Control inputs require the exact checked-out comparison.");
        RequireOwner(comparison.Candidate, allowAbsent: false);
        bool beforePresent = RequireOwner(comparison.Basis, allowAbsent: true);
        if (beforePresent != (request.BasisDirectory is not null))
            throw new InvalidDataException(
                "Control basis preparation disagrees with its Git inventory.");

        ControlResourceEndpoint after = await ReadEndpointAsync(request.CandidateDirectory,
            comparison.Candidate, token);
        ControlResourceEndpoint before;
        if (!beforePresent)
            before = new(comparison.Basis.Commit, Project, false, null, []);
        else if (comparison.Basis.Commit == comparison.Candidate.Commit &&
            Path.GetFullPath(request.BasisDirectory!) ==
                Path.GetFullPath(request.CandidateDirectory))
            before = after;
        else
            before = await ReadEndpointAsync(request.BasisDirectory!, comparison.Basis, token);
        return new(request.Comparison, before, after);
    }

    private static bool RequireOwner(GitRevision revision, bool allowAbsent)
    {
        GitEntry? entry = revision.Entries.SingleOrDefault(entry => entry.Path == Project);
        if (entry is null && allowAbsent)
            return false;
        if (entry is null || entry.ObjectType != "blob" || entry.Mode is not ("100644" or "100755"))
            throw new InvalidDataException(
                "The control test owner must be a committed regular project.");
        return true;
    }

    private static async Task<ControlResourceEndpoint> ReadEndpointAsync(string directory,
        GitRevision revision, CancellationToken token)
    {
        GitMaterialization checkout = await GitMaterialization.BindAsync(directory, revision,
            token);
        MsBuildResources resources = await new MsBuildResourceReader(checkout).ReadDefaultAsync(
            Project, "Debug", token);
        return new(resources.Revision, resources.Project, true, resources.Dimension,
            resources.Inputs);
    }
}
