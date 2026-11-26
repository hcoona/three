using WorkflowDelivery.Ecosystems;
using WorkflowDelivery.Platform;
using WorkflowDelivery.Repository;

namespace WorkflowDelivery.CI;

internal sealed record PythonGroupRunRequest(PythonPytestRunRequest Native, string PackageHelper);
internal sealed record PythonGroupRunResult(string Candidate, CheckResult[] Results,
    PythonPytestRunResult Pytest, PythonPackageRunResult Packages, CiOutcome Outcome);

// Two concrete partitions retain the original plan; there is no general task scheduler.
internal static class PythonGroupExecution
{
    internal static Task<PythonGroupRunResult> RunAsync(CiPlan plan,
        PythonGroupRunRequest request, CancellationToken token) => RunAsync(plan, request,
            null, null, NativeProcess.ExecuteAsync, token);

    // Null partitions run the product executors on this group's checkout binding.
    internal static async Task<PythonGroupRunResult> RunAsync(CiPlan plan,
        PythonGroupRunRequest request,
        Func<CiPlan, PythonPytestRunRequest, CancellationToken, Task<PythonPytestRunResult>>?
            pytest,
        Func<CiPlan, PythonPackageRunRequest, CancellationToken, Task<PythonPackageRunResult>>?
            packages,
        Func<NativeCommand, CancellationToken, Task<NativeCommandResult>> metadata,
        CancellationToken token)
    {
        plan.Validate();
        if (plan.Scope != PythonFactsAssembler.Scope)
            throw new InvalidDataException("Unsupported native Python group scope.");
        foreach (PlannedCheck item in plan.Checks)
            if (!(item.Origins.SequenceEqual([CheckOrigin.NativeRetained]) &&
                    item.QualityPresets.Length == 0) &&
                !(item.Origins.SequenceEqual([CheckOrigin.Preset]) &&
                    item.QualityPresets.SequenceEqual([PythonPackageChecks.Preset])))
                throw new InvalidDataException("Unsupported native Python group contract.");
        (CiPlan pytestPlan, CiPlan packagePlan) = plan.Partition(item =>
            item.Origins[0] == CheckOrigin.NativeRetained);

        PythonPytestRunRequest native = request.Native;
        _ = OwnedPath.Absolute(native.Interpreter, PathMessage);
        if (native.DeadlineSeconds <= 0)
            throw new InvalidDataException("Python group execution requires a positive deadline.");
        string scratch = OwnedPath.Absolute(native.Scratch, PathMessage);
        if (packagePlan.Checks.Length != 0)
        {
            _ = OwnedPath.Absolute(request.PackageHelper, PathMessage);
            // Apply the archive consumer's boundary before either partition can execute.
            PythonPackageOperations.RequireGitFreeScratch(scratch,
                "Python package consumers require Git-free scratch.");
        }
        (string checkout, _) = OwnedPath.RequireFreshExternalScratch(native.Checkout, scratch,
            "Python group requires fresh external scratch.", PathMessage);
        if (!GitReader.IsCommitId(plan.Candidate))
            throw new InvalidDataException("Python group requires its exact candidate.");
        GitMaterialization materialization = await GitMaterialization.BindAsync(checkout,
            plan.Candidate, token);
        pytest ??= (partition, partitionRequest, cancellation) =>
            PythonPytestExecution.RunAsync(partition, partitionRequest, NativeProcess.ExecuteAsync,
                cancellation, materialization);
        packages ??= (partition, partitionRequest, cancellation) =>
            PythonPackageExecution.RunAsync(partition, partitionRequest, NativeProcess.ExecuteAsync,
                cancellation, materialization);
        PythonPytestExecution.ReadChecks(pytestPlan, native, materialization.Revision);
        await PythonPackageExecution.ReadChecksAsync(packagePlan, native.Members,
            materialization, token);
        if (plan.Checks.Length != 0)
            native = await PythonPytestExecution.BindMembershipAsync(native, metadata, token);

        PythonPytestRunResult retained;
        try
        {
            retained = pytestPlan.Checks.Length == 0 ? new(plan.Candidate, [], [], []) :
                await pytest(pytestPlan, native with
                { Scratch = Directory.CreateDirectory(Path.Combine(scratch, "pytest")).FullName },
                    token);
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested)
        { retained = new(plan.Candidate, Cancelled(pytestPlan), [], []); }
        PythonPackageRunResult package;
        try
        {
            package = packagePlan.Checks.Length == 0 ? new(plan.Candidate, [], [], [], []) :
                token.IsCancellationRequested ?
                    new(plan.Candidate, Cancelled(packagePlan), [], [], []) :
                await packages(packagePlan, new(checkout,
                    Directory.CreateDirectory(Path.Combine(scratch, "packages")).FullName,
                    native.Uv, native.Interpreter, request.PackageHelper, native.Members,
                    native.DeadlineSeconds, native.Environment), token);
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested)
        { package = new(plan.Candidate, Cancelled(packagePlan), [], [], []); }
        if (retained.Candidate != plan.Candidate || package.Candidate != plan.Candidate)
            throw new InvalidDataException("Python group result has a different candidate.");
        CheckResult[] results = [.. retained.Results, .. package.Results];
        CiOutcome outcome = ResultCollector.CollectValidated(plan, results);
        return new(plan.Candidate, results, retained, package, outcome);

        CheckResult[] Cancelled(CiPlan partition) => partition.Checks.Select(item =>
            new CheckResult(plan.Candidate, item.Work.Key, CheckStatus.Cancelled)).ToArray();
    }

    private const string PathMessage = "Python group paths must be absolute.";
}
