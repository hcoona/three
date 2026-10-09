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
            PythonPytestExecution.RunAsync, PythonPackageExecution.RunAsync, token);

    internal static async Task<PythonGroupRunResult> RunAsync(CiPlan plan,
        PythonGroupRunRequest request,
        Func<CiPlan, PythonPytestRunRequest, CancellationToken, Task<PythonPytestRunResult>> pytest,
        Func<CiPlan, PythonPackageRunRequest, CancellationToken, Task<PythonPackageRunResult>>
            packages,
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
        string checkout = Absolute(native.Checkout), scratch = Absolute(native.Scratch);
        _ = Absolute(native.Interpreter);
        StringComparison paths = OperatingSystem.IsWindows()
            ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
        if (native.DeadlineSeconds <= 0 || !Directory.Exists(checkout) ||
            !Directory.Exists(scratch) || Directory.EnumerateFileSystemEntries(scratch).Any() ||
            Linked(checkout) || Linked(scratch) || Within(scratch, checkout) ||
            Within(checkout, scratch))
            throw new InvalidDataException("Python group requires fresh external scratch.");
        if (packagePlan.Checks.Length != 0)
        {
            _ = Absolute(request.PackageHelper);
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
        bool Within(string child, string parent) => child.Equals(parent, paths) ||
            child.StartsWith(parent + Path.DirectorySeparatorChar, paths);
    }

    private static string Absolute(string path) => Path.IsPathFullyQualified(path)
        ? Path.TrimEndingDirectorySeparator(Path.GetFullPath(path))
        : throw new InvalidDataException("Python group paths must be absolute.");
    private static bool Linked(string path)
    {
        for (DirectoryInfo? directory = new(path); directory is not null;
             directory = directory.Parent)
            if ((directory.Attributes & FileAttributes.ReparsePoint) != 0) return true;
        return false;
    }
}
