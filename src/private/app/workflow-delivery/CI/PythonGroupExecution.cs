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
            PythonPytestExecution.RunBoundAsync, PythonPackageExecution.RunAsync,
            NativeProcess.ExecuteAsync, token);

    internal static async Task<PythonGroupRunResult> RunAsync(CiPlan plan,
        PythonGroupRunRequest request,
        Func<CiPlan, PythonPytestRunRequest, CancellationToken, Task<PythonPytestRunResult>> pytest,
        Func<CiPlan, PythonPackageRunRequest, CancellationToken, Task<PythonPackageRunResult>>
            packages,
        Func<NativeCommand, CancellationToken, Task<NativeCommandResult>> metadata,
        CancellationToken token)
    {
        ResultCollector.Collect(plan, []);
        if (plan.Scope != PythonFactsAssembler.Scope)
            throw new InvalidDataException("Unsupported native Python group scope.");
        foreach (PlannedCheck item in plan.Checks)
            if (!(item.Origins.SequenceEqual([CheckOrigin.NativeRetained]) &&
                    item.QualityPresets.Length == 0) &&
                !(item.Origins.SequenceEqual([CheckOrigin.Preset]) &&
                    item.QualityPresets.SequenceEqual([PythonPackageChecks.Preset])))
                throw new InvalidDataException("Unsupported native Python group contract.");
        CiPlan pytestPlan = plan with { Checks = plan.Checks.Where(item =>
            item.Origins.SequenceEqual([CheckOrigin.NativeRetained])).ToArray() };
        CiPlan packagePlan = plan with { Checks = plan.Checks.Where(item =>
            item.Origins.SequenceEqual([CheckOrigin.Preset])).ToArray() };
        ResultCollector.Collect(pytestPlan, []);
        ResultCollector.Collect(packagePlan, []);

        PythonPytestRunRequest native = request.Native;
        string checkout = OwnedPath.Absolute(native.Checkout, PathMessage);
        string scratch = OwnedPath.Absolute(native.Scratch, PathMessage);
        _ = OwnedPath.Absolute(native.Interpreter, PathMessage);
        if (native.DeadlineSeconds <= 0 || !Directory.Exists(checkout) ||
            !Directory.Exists(scratch) || Directory.EnumerateFileSystemEntries(scratch).Any() ||
            OwnedPath.Overlap(scratch, checkout))
            throw new InvalidDataException("Python group requires fresh external scratch.");
        if (packagePlan.Checks.Length != 0)
        {
            _ = OwnedPath.Absolute(request.PackageHelper, PathMessage);
            // Apply the archive consumer's boundary before either partition can execute.
            for (DirectoryInfo? parent = new(scratch); parent is not null; parent = parent.Parent)
                if (File.Exists(Path.Combine(parent.FullName, ".git")) ||
                    Directory.Exists(Path.Combine(parent.FullName, ".git")))
                    throw new InvalidDataException(
                        "Python package consumers require Git-free scratch.");
        }
        GitRevision revision = await new GitReader(checkout).ReadAsync(plan.Candidate, token);
        if (revision.Commit != plan.Candidate)
            throw new InvalidDataException("Python group requires its exact candidate.");
        GitMaterialization materialization = await GitMaterialization.BindAsync(checkout,
            revision, token);
        PythonPytestExecution.ReadChecks(pytestPlan, native, revision);
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
        CiOutcome outcome = ResultCollector.Collect(plan, results);
        return new(plan.Candidate, results, retained, package, outcome);

        CheckResult[] Cancelled(CiPlan partition) => partition.Checks.Select(item =>
            new CheckResult(plan.Candidate, item.Work.Key, CheckStatus.Cancelled)).ToArray();
    }

    private const string PathMessage = "Python group paths must be absolute.";
}
