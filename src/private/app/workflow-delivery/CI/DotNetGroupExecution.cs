using WorkflowDelivery.Repository;

namespace WorkflowDelivery.CI;

internal sealed record DotNetGroupRunResult(string Candidate, CheckResult[] Results,
    DotNetRunResult Retained, DotNetPackageRunResult Packages, CiOutcome Outcome);

// The two current contracts are explicit partitions of one unchanged original plan.
internal static class DotNetGroupExecution
{
    internal static Task<DotNetGroupRunResult> RunAsync(CiPlan plan, DotNetRunRequest request,
        CancellationToken token) => RunAsync(plan, request, DotNetExecution.RunAsync,
            DotNetPackageExecution.RunAsync, token);

    internal static async Task<DotNetGroupRunResult> RunAsync(CiPlan plan,
        DotNetRunRequest request,
        Func<CiPlan, DotNetRunRequest, CancellationToken, Task<DotNetRunResult>> retained,
        Func<CiPlan, DotNetRunRequest, CancellationToken, Task<DotNetPackageRunResult>> packages,
        CancellationToken token)
    {
        ResultCollector.Collect(plan, []);
        if (plan.Scope != DotNetFactsAssembler.Scope)
            throw new InvalidDataException("Unsupported native .NET group scope.");
        foreach (PlannedCheck item in plan.Checks)
            if (!(item.Origins.SequenceEqual([CheckOrigin.NativeRetained]) &&
                    item.QualityPresets.Length == 0) &&
                !(item.Origins.SequenceEqual([CheckOrigin.Preset]) &&
                    item.QualityPresets.SequenceEqual([DotNetPackageChecks.Preset])))
                throw new InvalidDataException("Unsupported native .NET group contract.");
        CiPlan nativePlan = plan with
        {
            Checks = plan.Checks.Where(item =>
            item.Origins.SequenceEqual([CheckOrigin.NativeRetained])).ToArray()
        };
        CiPlan packagePlan = plan with
        {
            Checks = plan.Checks.Where(item =>
            item.Origins.SequenceEqual([CheckOrigin.Preset])).ToArray()
        };
        ResultCollector.Collect(nativePlan, []);
        ResultCollector.Collect(packagePlan, []);
        string checkout = Path.TrimEndingDirectorySeparator(Path.GetFullPath(request.Checkout));
        string scratch = Path.TrimEndingDirectorySeparator(Path.GetFullPath(request.Scratch));
        StringComparison paths = OperatingSystem.IsWindows() ?
            StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
        if (!Path.IsPathFullyQualified(request.Checkout) ||
            !Path.IsPathFullyQualified(request.Scratch) || request.DeadlineSeconds <= 0 ||
            !Directory.Exists(scratch) || Directory.EnumerateFileSystemEntries(scratch).Any() ||
            scratch.Equals(checkout, paths) ||
            scratch.StartsWith(checkout + Path.DirectorySeparatorChar, paths) ||
            checkout.StartsWith(scratch + Path.DirectorySeparatorChar, paths))
            throw new InvalidDataException(
                "Native group execution requires fresh external scratch.");
        GitRevision revision = await new GitReader(checkout).ReadAsync(plan.Candidate, token);
        if (revision.Commit != plan.Candidate)
            throw new InvalidDataException("Native group execution requires its exact candidate.");
        GitMaterialization materialization = await GitMaterialization.BindAsync(checkout,
            revision, token);
        // Reuse each executor's complete contract validation before either product partition.
        DotNetExecution.ReadChecks(nativePlan, checkout);
        await DotNetPackageExecution.ReadChecksAsync(packagePlan, materialization, token);
        DotNetRunResult native = await retained(nativePlan, request with
        { Scratch = Directory.CreateDirectory(Path.Combine(scratch, "retained")).FullName }, token);
        DotNetPackageRunResult package;
        try
        {
            package = packagePlan.Checks.Length == 0
                ? new(plan.Candidate, [], [], [], [], [])
                : await packages(packagePlan, request with
                { Scratch = Directory.CreateDirectory(Path.Combine(scratch, "packages")).FullName },
                    token);
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested)
        {
            package = new(plan.Candidate, packagePlan.Checks.Select(item =>
                new CheckResult(plan.Candidate, item.Work.Key, CheckStatus.Cancelled)).ToArray(),
                [], [], [], []);
        }
        CheckResult[] results = [.. native.Results, .. package.Results];
        CiOutcome outcome = ResultCollector.Collect(plan, results);
        if (native.Candidate != plan.Candidate || package.Candidate != plan.Candidate)
            throw new InvalidDataException("Native group result has a different candidate.");
        return new(plan.Candidate, results, native, package, outcome);
    }
}
