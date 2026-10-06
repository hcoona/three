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
);

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
