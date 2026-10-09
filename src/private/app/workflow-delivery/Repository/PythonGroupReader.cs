using WorkflowDelivery.CI;
using WorkflowDelivery.Platform;

namespace WorkflowDelivery.Repository;

internal sealed record PythonGroupEndpoint(MaterializedEndpoint Checkout,
    PythonRepositoryRequest Collection, IReadOnlyDictionary<string, string?> Environment);
internal sealed record PythonGroupRequest(PythonGroupEndpoint Basis, PythonGroupEndpoint Candidate,
    bool Full, string ScopePath);
internal sealed record PythonGroupReadback(CiPlan Plan, PythonMetadataMember[] Members,
    string Interpreter, PythonPytestConfiguration Configuration, PythonPytestTargetSet Targets,
    PythonPytestOperation Operation);

// The caller prepares exact endpoints/tools; one native group owns paired Python selection.
internal static class PythonGroupReader
{
    internal static Task<PythonGroupReadback> ReadAsync(PythonGroupRequest request,
        CancellationToken token) => ReadAsync(request, NativeProcess.ExecuteAsync, token);

    internal static Task<PythonGroupReadback> ReadAsync(PythonGroupRequest request,
        Func<NativeCommand, CancellationToken, Task<NativeCommandResult>> execute,
        CancellationToken token)
    {
        return ReadAsync(request, (checkout, endpoint, cancellation) =>
            new PythonRepositoryReader(checkout,
                new PythonNativeGraphReader(ExecuteAsync).ReadAsync, ExecuteAsync).ReadAsync(
                    endpoint.Collection, endpoint.Environment, cancellation), token);

        Task<NativeCommandResult> ExecuteAsync(NativeCommand command,
            CancellationToken cancellation) => execute(command with
            { InheritEnvironment = false }, cancellation);
    }

    internal static async Task<PythonGroupReadback> ReadAsync(PythonGroupRequest request,
        Func<GitMaterialization, PythonGroupEndpoint, CancellationToken,
            Task<PythonRevisionInputs>> collect, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        PythonRepositoryRequest beforeRequest = request.Basis.Collection;
        PythonRepositoryRequest afterRequest = request.Candidate.Collection;
        if (beforeRequest.Graph.Interpreter != afterRequest.Graph.Interpreter ||
            beforeRequest.Graph.Uv != afterRequest.Graph.Uv ||
            beforeRequest.Graph.SupplementExecutable != afterRequest.Graph.SupplementExecutable ||
            beforeRequest.Graph.PassiveScript != afterRequest.Graph.PassiveScript ||
            beforeRequest.PytestHelper != afterRequest.PytestHelper ||
            beforeRequest.PackageHelper != afterRequest.PackageHelper ||
            beforeRequest.Operation.Runner != afterRequest.Operation.Runner ||
            beforeRequest.Operation.Variant != afterRequest.Operation.Variant ||
            !beforeRequest.Operation.Dimensions.OrderBy(pair => pair.Key, StringComparer.Ordinal)
                .SequenceEqual(afterRequest.Operation.Dimensions.OrderBy(pair => pair.Key,
                    StringComparer.Ordinal)))
            throw new InvalidDataException("Python endpoints require one actual control runtime.");
        GitComparison comparison = await new GitReader(request.Candidate.Checkout.Directory)
            .CompareAsync(request.Basis.Checkout.Reference, request.Candidate.Checkout.Reference,
                token);
        GitMaterialization basis = await GitMaterialization.BindAsync(
            request.Basis.Checkout.Directory, comparison.Basis, token);
        GitMaterialization candidate = await GitMaterialization.BindAsync(
            request.Candidate.Checkout.Directory, comparison.Candidate, token);
        ScopeInputs scope = ScopeInputs.Read(await File.ReadAllTextAsync(request.ScopePath, token),
            comparison, request.Full);
        PythonRevisionInputs before = await collect(basis, request.Basis, token);
        token.ThrowIfCancellationRequested();
        PythonRevisionInputs after = await collect(candidate, request.Candidate, token);
        token.ThrowIfCancellationRequested();
        before = WithOtherResponsibilities(before, scope.Basis);
        after = WithOtherResponsibilities(after, scope.Candidate);
        CiPlan plan = PythonGroupPlanning.Plan(before, after, comparison, request.Full);
        return new(plan, after.Graph.Metadata.Members, after.Graph.Interpreter,
            after.Facts.Configuration, after.Targets, after.Facts.Operation);
    }

    private static PythonRevisionInputs WithOtherResponsibilities(PythonRevisionInputs inputs,
        ScopeCoordinate[] coordinates) => inputs with
        {
            Facts = inputs.Facts with
            {
                // Other retained jobs/records establish repository responsibility only.
                // Native Python relations still decide every Python effect; legacy Python
                // heuristics cannot declare an otherwise unresolved coordinate unaffected.
                Inputs = [.. inputs.Facts.Inputs, .. coordinates.Where(row => row.Present &&
                    row.Reasons.Length != 0 && row.Reasons.All(reason => reason.Owner != "python"))
                    .Select(row => new PythonInputBinding(row.Path, [], []))]
            }
        };
}
