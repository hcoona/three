namespace WorkflowDelivery.CI;

// These values carry resolved native facts, not a second project evaluator.
internal sealed record RepositoryFacts(
    string Revision,
    string Scope,
    ProjectFacts[] Projects,
    SharedInput[] SharedInputs,
    string[] UnaffectedPaths,
    string[] Errors
);

internal sealed record ProjectFacts(
    string Id,
    string Directory,
    string[] Dependencies,
    string[] QualityConsumers,
    string? ReleaseUnit,
    string? QualityPreset,
    CheckSpec[] Checks,
    string[]? OwnedPaths = null,
    CheckOrigin Origin = CheckOrigin.Preset
);

internal enum CheckOrigin
{
    Preset,
    NativeRetained,
}

internal sealed record SharedInput(string Path, string[] Consumers);

// Variant names belong to the resolved declaration. Dimensions carry its actual values.
internal sealed record CheckKey(string Target, string Check, string Variant);

internal sealed record CheckSpec(
    CheckKey Key,
    Dictionary<string, string> Dimensions,
    string Runner,
    bool Required,
    CheckKey[] Prerequisites,
    PackageTarget? Package = null
);

// Only artifact checks carry a package subject; native project checks use their source target.
internal sealed record PackageOutput(string Id, string Role, string Kind);

internal sealed record PackageTarget(
    string Unit,
    string Declaration,
    string Build,
    string Definition,
    string Directory,
    string EntryPoint,
    string? PublishDirectory,
    string ExpectedVersion,
    PackageOutput[] Outputs
);

internal sealed record PlanRequest(
    RepositoryFacts Basis,
    RepositoryFacts Candidate,
    string[] ChangedPaths,
    bool Full
);

internal sealed record SelectionReason(string Path, string Revision, string Project);

internal sealed record PlannedCheck(
    CheckSpec Work,
    string[] QualityPresets,
    SelectionReason[] Reasons,
    CheckOrigin[] Origins
);

internal sealed record CiPlan(
    string Comparison,
    string Candidate,
    string Scope,
    PlannedCheck[] Checks
)
{
    internal void Validate()
    {
        ImpactPlanner.RequireText(Comparison, "comparison");
        ImpactPlanner.RequireText(Candidate, "candidate");
        ImpactPlanner.RequireText(Scope, "coverage scope");
        ArgumentNullException.ThrowIfNull(Checks);
        var expected = new Dictionary<CheckKey, CheckSpec>();
        foreach (PlannedCheck check in Checks)
        {
            ArgumentNullException.ThrowIfNull(check);
            ImpactPlanner.ValidateCheck(check.Work);
            ArgumentNullException.ThrowIfNull(check.Origins);
            ArgumentNullException.ThrowIfNull(check.QualityPresets);
            if (check.Origins.Length == 0 || check.Origins.Any(origin => !Enum.IsDefined(origin)) ||
                check.Origins.Contains(CheckOrigin.Preset) != (check.QualityPresets.Length != 0) ||
                check.QualityPresets.Any(string.IsNullOrWhiteSpace))
                throw new InvalidDataException("Invalid planned check origin/preset relationship.");
            if (!expected.TryAdd(check.Work.Key, check.Work))
                throw new InvalidDataException($"Duplicate planned check: {check.Work.Key}");
        }
        ImpactPlanner.ValidatePrerequisites(expected);
        if (expected.Values.Any(c => c.Required && c.Prerequisites.Any(p => !expected[p].Required)))
            throw new InvalidDataException("A required check has an advisory prerequisite.");
    }

    // The full plan is already valid; only edges crossing the partition can break closure.
    internal (CiPlan First, CiPlan Second) Partition(Func<PlannedCheck, bool> inFirst)
    {
        PlannedCheck[] first = Checks.Where(inFirst).ToArray();
        var firstKeys = first.Select(item => item.Work.Key).ToHashSet();
        if (Checks.Any(item => item.Work.Prerequisites.Any(prerequisite =>
                firstKeys.Contains(prerequisite) != firstKeys.Contains(item.Work.Key))))
            throw new InvalidDataException("A prerequisite crosses CI execution partitions.");
        return (this with { Checks = first }, this with
        {
            Checks = Checks.Where(item => !firstKeys.Contains(item.Work.Key)).ToArray(),
        });
    }
}

internal enum CheckStatus
{
    Passed,
    Failed,
    Cancelled,
    TimedOut,
    Skipped,
}

internal sealed record CheckResult(string Candidate, CheckKey Key, CheckStatus Status);

internal sealed record CheckOutcome(CheckKey Key, bool Required, string Status);

internal sealed record CiOutcome(
    string Comparison,
    string Candidate,
    string Scope,
    bool Satisfied,
    CheckOutcome[] Checks,
    string[] Errors
);
