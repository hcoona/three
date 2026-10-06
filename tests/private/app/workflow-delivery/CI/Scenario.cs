using WorkflowDelivery.CI;

namespace WorkflowDelivery.Tests.CI;

internal static class Scenario
{
    internal static CheckSpec Check(
        string target,
        string operation = "test",
        string variant = "default",
        bool required = true,
        params CheckKey[] prerequisites
    ) => new(new(target, operation, variant), [], "ubuntu-latest", required, prerequisites);

    internal static ProjectFacts Project(
        string id,
        string[]? dependencies = null,
        string[]? qualityConsumers = null,
        string? releaseUnit = null,
        string? directory = null,
        string? preset = "standard",
        params CheckSpec[] checks
    ) => new(id, directory ?? $"src/{id}", dependencies ?? [], qualityConsumers ?? [],
        releaseUnit, preset, checks.Length == 0 ? [Check(id)] : checks);

    internal static RepositoryFacts Facts(string revision, params ProjectFacts[] projects)
        => new(revision, "fixture-projects", projects, [], ["docs/guide.md"], []);

    internal static PlanRequest Request(ProjectFacts[] projects, params string[] changedPaths)
        => new(Facts("base", projects), Facts("candidate", projects), changedPaths, false);

    internal static CiPlan Plan(params CheckSpec[] checks)
        => new("base", "candidate", "fixture-projects",
            checks.Select(check => new PlannedCheck(check, ["standard"],
                [new("src/library/code.cs", "candidate", "library")],
                [CheckOrigin.Preset])).ToArray());
}
