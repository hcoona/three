using WorkflowDelivery.CI;
using WorkflowDelivery.Platform;

namespace WorkflowDelivery.Repository;

internal sealed record RubyGroupEndpoint(MaterializedEndpoint Checkout,
    RubyRepositoryRequest Collection);
internal sealed record RubyGroupRequest(RubyGroupEndpoint Basis, RubyGroupEndpoint Candidate,
    bool Full, string ScopePath);
internal sealed record RubyGroupReadback(CiPlan Plan, RubyRevisionInputs Basis,
    RubyRevisionInputs Candidate);

// Native endpoint collection and one paired selection; the caller owns preparation.
internal static class RubyGroupReader
{
    internal static Task<RubyGroupReadback> ReadAsync(RubyGroupRequest request,
        CancellationToken token) => ReadAsync(request, NativeProcess.ExecuteAsync, token);

    internal static Task<RubyGroupReadback> ReadAsync(RubyGroupRequest request,
        Func<NativeCommand, CancellationToken, Task<NativeCommandResult>> execute,
        CancellationToken token) => ReadAsync(request, (checkout, endpoint, cancellation) =>
            RubyRepositoryReader.ReadAsync(checkout, endpoint.Collection, execute, cancellation),
            token);

    internal static async Task<RubyGroupReadback> ReadAsync(RubyGroupRequest request,
        Func<GitMaterialization, RubyGroupEndpoint, CancellationToken,
            Task<RubyRevisionInputs>> collect, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        RubyRepositoryRequest beforeRequest = request.Basis.Collection;
        RubyRepositoryRequest afterRequest = request.Candidate.Collection;
        if (beforeRequest.Tools.Ruby != afterRequest.Tools.Ruby ||
            beforeRequest.Tools.Helper != afterRequest.Tools.Helper ||
            beforeRequest.Nbgv != afterRequest.Nbgv ||
            beforeRequest.BundlerProbeVersion != afterRequest.BundlerProbeVersion)
            throw new InvalidDataException("Ruby endpoints require one actual control runtime.");
        GitComparison comparison = await new GitReader(request.Candidate.Checkout.Directory)
            .CompareAsync(request.Basis.Checkout.Reference, request.Candidate.Checkout.Reference,
                token);
        GitMaterialization basis = await GitMaterialization.BindAsync(
            request.Basis.Checkout.Directory, comparison.Basis, token);
        GitMaterialization candidate = await GitMaterialization.BindAsync(
            request.Candidate.Checkout.Directory, comparison.Candidate, token);
        ScopeInputs scope = ScopeInputs.Read(await File.ReadAllTextAsync(request.ScopePath, token),
            comparison, request.Full);
        RubyRevisionInputs before = await collect(basis, request.Basis, token);
        token.ThrowIfCancellationRequested();
        RubyRevisionInputs after = await collect(candidate, request.Candidate, token);
        token.ThrowIfCancellationRequested();
        RubyFactsEndpoint beforeFacts = WithOtherResponsibilities(before.Facts, scope.Basis);
        RubyFactsEndpoint afterFacts = WithOtherResponsibilities(after.Facts, scope.Candidate);
        if (beforeFacts.Revision.Commit != comparison.Basis.Commit ||
            afterFacts.Revision.Commit != comparison.Candidate.Commit)
            throw new InvalidDataException("Ruby collection returned different endpoints.");
        RubyFactAssembly assembly = RubyFactsAssembler.Assemble(beforeFacts, afterFacts,
            comparison.ChangedPaths, request.Full);
        return new(ImpactPlanner.Plan(assembly.Request, assembly.PairedReasons), before, after);
    }

    private static RubyFactsEndpoint WithOtherResponsibilities(RubyFactsEndpoint inputs,
        ScopeCoordinate[] coordinates) => inputs with
        {
            Native = inputs.Native with
            {
                // Retained responsibilities can establish a known non-Ruby input only.
                // Actual native relations still determine all Ruby consumption.
                Inputs = [.. inputs.Native.Inputs, .. coordinates.Where(row => row.Present &&
                    row.Reasons.Length != 0 && row.Reasons.All(reason => reason.Owner != "ruby"))
                    .Select(row => new SharedInput(row.Path, []))]
            }
        };
}
