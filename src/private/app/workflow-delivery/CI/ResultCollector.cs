namespace WorkflowDelivery.CI;

internal static class ResultCollector
{
    internal static CiOutcome Collect(CiPlan plan, CheckResult[] results)
    {
        ArgumentNullException.ThrowIfNull(plan);
        ArgumentNullException.ThrowIfNull(results);
        ImpactPlanner.RequireText(plan.Comparison, "comparison");
        ImpactPlanner.RequireText(plan.Candidate, "candidate");
        ImpactPlanner.RequireText(plan.Scope, "coverage scope");
        ArgumentNullException.ThrowIfNull(plan.Checks);
        var expected = new Dictionary<CheckKey, CheckSpec>();
        foreach (PlannedCheck check in plan.Checks)
        {
            ArgumentNullException.ThrowIfNull(check);
            ImpactPlanner.ValidateCheck(check.Work);
            if (!expected.TryAdd(check.Work.Key, check.Work))
                throw new InvalidDataException($"Duplicate planned check: {check.Work.Key}");
        }
        ImpactPlanner.ValidatePrerequisites(expected);
        if (expected.Values.Any(c => c.Required && c.Prerequisites.Any(p => !expected[p].Required)))
            throw new InvalidDataException("A required check has an advisory prerequisite.");

        var observed = new Dictionary<CheckKey, HashSet<CheckStatus>>();
        var errors = new List<string>();
        foreach (CheckResult result in results)
        {
            ArgumentNullException.ThrowIfNull(result);
            ImpactPlanner.ValidateKey(result.Key);
            if (result.Candidate != plan.Candidate || !expected.ContainsKey(result.Key))
            {
                errors.Add($"Unexpected result subject: {result.Candidate} / {result.Key}");
                continue;
            }
            if (!Enum.IsDefined(result.Status))
            {
                errors.Add($"Unknown result status: {result.Key}");
                continue;
            }
            if (!observed.TryGetValue(result.Key, out HashSet<CheckStatus>? states))
                observed.Add(result.Key, states = []);
            states.Add(result.Status);
        }
        CheckOutcome[] outcomes = plan.Checks.Select(item =>
        {
            string status = !observed.TryGetValue(item.Work.Key, out HashSet<CheckStatus>? states)
                ? "missing"
                : states.Count != 1 ? "conflicting" : states.Single().ToString().ToLowerInvariant();
            return new CheckOutcome(item.Work.Key, item.Work.Required, status);
        }).ToArray();
        return new(
            plan.Comparison,
            plan.Candidate,
            plan.Scope,
            errors.Count == 0 && outcomes.All(o => !o.Required || o.Status == "passed"),
            outcomes,
            errors.ToArray()
        );
    }
}
