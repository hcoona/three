using WorkflowDelivery.CI;

namespace WorkflowDelivery.Repository;

// One paired selection over actual endpoint facts, with the original candidate context retained.
internal static class PythonGroupPlanning
{
    internal static CiPlan Plan(PythonRevisionInputs basis, PythonRevisionInputs candidate,
        GitComparison comparison, bool full)
    {
        if (basis.Facts.Revision.Commit != comparison.Basis.Commit ||
            candidate.Facts.Revision.Commit != comparison.Candidate.Commit)
            throw new InvalidDataException("Python planning has different comparison endpoints.");
        PythonFactAssembly assembly = PythonFactsAssembler.Assemble(basis.Facts, candidate.Facts,
            comparison.ChangedPaths, full, PythonRepositoryInputs.Pair(basis.Facts,
                candidate.Facts, comparison.ChangedPaths));
        return ImpactPlanner.Plan(assembly.Request, assembly.PairedReasons);
    }
}
