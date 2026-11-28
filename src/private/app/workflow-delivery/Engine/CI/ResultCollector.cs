namespace WorkflowDelivery.CI;

internal static class ResultCollector
{
    internal static CiOutcome Collect(CiPlan plan, CheckResult[] results)
    {
        ArgumentNullException.ThrowIfNull(plan);
        ArgumentNullException.ThrowIfNull(results);
        plan.Validate();
        return CollectValidated(plan, results);
    }

    internal static CiOutcome CollectValidated(CiPlan plan, CheckResult[] results)
    {
        var expected = plan.Checks.ToDictionary(item => item.Work.Key, item => item.Work.Required);
        var observed = new Dictionary<CheckKey, List<string>>();
        var errors = new List<string>();
        foreach (CheckResult result in results)
        {
            ArgumentNullException.ThrowIfNull(result);
            ImpactPlanner.ValidateKey(result.Key);
            bool bound = result.Candidate == plan.Candidate;
            if (!expected.TryGetValue(result.Key, out bool required) || (required && !bound))
            {
                errors.Add($"Unexpected result subject: {result.Candidate} / {result.Key}");
                continue;
            }
            bool known = Enum.IsDefined(result.Status);
            if (required && !known)
            {
                errors.Add($"Unknown result status: {result.Key}");
                continue;
            }
            if (!observed.TryGetValue(result.Key, out List<string>? states))
                observed.Add(result.Key, states = []);
            states.Add(!bound ? "unbound" : !known ? "unknown" :
                result.Status.ToString().ToLowerInvariant());
        }
        CheckOutcome[] outcomes = plan.Checks.Select(item =>
        {
            string status = !observed.TryGetValue(item.Work.Key, out List<string>? states)
                ? "missing" : states.Count == 1 ? states[0]
                : states.All(state => state == states[0]) ? "duplicate" : "conflicting";
            if (item.Work.Required && states is { Count: > 1 })
                errors.Add($"Repeated result for required check: {item.Work.Key}");
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

    internal static (CheckResult[] Results, CiOutcome Outcome) Join(CiPlan original,
        IEnumerable<string> plannedGroups, Func<PlannedCheck, string> groupOf,
        IEnumerable<KeyValuePair<string, CiGroupReceipt>> receipts, string groupLabel,
        bool requireCompleteSets)
    {
        original.Validate();
        var groups = plannedGroups.ToDictionary(group => group, _ => new HashSet<CheckKey>(),
            StringComparer.Ordinal);
        var partition = original.Checks.ToDictionary(check => check.Work.Key, groupOf);
        foreach (PlannedCheck check in original.Checks)
        {
            string group = partition[check.Work.Key];
            if (!groups.TryGetValue(group, out HashSet<CheckKey>? required) ||
                check.Work.Prerequisites.Any(key => partition[key] != group))
                throw new InvalidDataException("Check has no closed execution partition: " +
                    check.Work.Key);
            if (check.Work.Required) required.Add(check.Work.Key);
        }

        var received = new HashSet<string>(StringComparer.Ordinal);
        var results = new List<CheckResult>();
        foreach ((string key, CiGroupReceipt receipt) in receipts)
        {
            ArgumentNullException.ThrowIfNull(receipt);
            if (!groups.TryGetValue(key, out HashSet<CheckKey>? required) ||
                !received.Add(key) || receipt.Group != key ||
                receipt.Candidate != original.Candidate)
                throw new InvalidDataException(
                    $"{groupLabel} result has an unexpected subject: {key}");
            receipt.OriginalPlan.Validate();
            if (!original.SamePlan(receipt.OriginalPlan))
                throw new InvalidDataException(
                    $"{groupLabel} result has a different original plan: {key}");

            var mandatory = new HashSet<CheckKey>();
            foreach (CheckResult result in receipt.Results)
            {
                ArgumentNullException.ThrowIfNull(result);
                bool needed = required.Contains(result.Key);
                if (!partition.TryGetValue(result.Key, out string? owner) || owner != key ||
                    (needed && (result.Candidate != original.Candidate ||
                        !Enum.IsDefined(result.Status))))
                    throw new InvalidDataException(
                        $"{groupLabel} has an unexpected receipt: {result.Key}");
                if (requireCompleteSets && needed && !mandatory.Add(result.Key))
                    throw new InvalidDataException(
                        $"{groupLabel} repeated required check: {result.Key}");
                results.Add(result);
            }
            if (requireCompleteSets && mandatory.Count != required.Count)
                throw new InvalidDataException($"{groupLabel} omitted required checks: {key}");
        }

        CheckResult[] union = [.. results];
        CiOutcome outcome = CollectValidated(original, union);
        string[] absent = [.. groups.Keys.Where(key => !received.Contains(key))
            .Select(key => $"{groupLabel} reported no result set: {key}")];
        return (union, absent.Length == 0 ? outcome : outcome with
        {
            Satisfied = false,
            Errors = [.. outcome.Errors, .. absent],
        });
    }
}
