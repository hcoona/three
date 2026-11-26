namespace WorkflowDelivery.CI;

// The caller orders its recipe steps and owns their native commands, errors and receipts.
internal sealed class CheckExecutionRunner(CiPlan plan, CancellationToken token)
{
    private readonly Dictionary<CheckKey, CheckResult> results = [];

    internal async Task RunAsync(CheckSpec work, Func<Task<CheckStatus>> step)
    {
        CheckStatus status;
        if (token.IsCancellationRequested)
            status = CheckStatus.Cancelled;
        else if (work.Prerequisites.Any(key =>
                     !results.TryGetValue(key, out CheckResult? result) ||
                     result.Status != CheckStatus.Passed))
            status = CheckStatus.Skipped;
        else
            status = await step();

        results.Add(work.Key, new(plan.Candidate, work.Key, status));
    }

    internal CheckResult[] InPlanOrder() =>
        plan.Checks.Select(item => results[item.Work.Key]).ToArray();

    internal CheckResult[] InExecutionOrder() => [.. results.Values];
}
