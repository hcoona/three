namespace LiteAgensh;

public sealed record CheckCommand
{
    public required string Executable { get; init; }

    public string[] Arguments { get; init; } = [];

    public int TimeoutSeconds { get; init; } = 120;
}

public sealed record RunConfiguration
{
    public required string RepositoryPath { get; init; }

    public required string StatePath { get; init; }

    public required string Goal { get; init; }

    public string BaseRevision { get; init; } = "HEAD";

    public int WorkerCount { get; init; } = 2;

    public int DurationSeconds { get; init; } = 1800;

    public int PlanningSeconds { get; init; }

    public string NativeExecutable { get; init; } = "copilot";

    public string[] NativeArguments { get; init; } = [];

    public string? NativeModel { get; init; }

    public string NativePermissionPolicy { get; init; } = "deny";

    public decimal RunAiCreditAllowance { get; init; } = 10000;

    public decimal CampaignAiCreditAllowance { get; init; } = 150000;

    public required string CampaignLedgerPath { get; init; }

    public CheckCommand[] CandidateChecks { get; init; } = [];

    public CheckCommand[] IntegrationChecks { get; init; } = [];

    public CheckCommand[] FinalChecks { get; init; } = [];

    public bool RequirePeerReview { get; init; } = true;

    public string[] ProtectedPaths { get; init; } = [];

    public bool CreateRepository { get; init; }

    [System.Text.Json.Serialization.JsonIgnore]
    public string[] WorkerIds => [.. Enumerable.Range(1, WorkerCount).Select(i => $"worker{i}")];

    public void Validate()
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(RepositoryPath);
        ArgumentException.ThrowIfNullOrWhiteSpace(StatePath);
        ArgumentException.ThrowIfNullOrWhiteSpace(Goal);
        ArgumentException.ThrowIfNullOrWhiteSpace(BaseRevision);
        ArgumentException.ThrowIfNullOrWhiteSpace(NativeExecutable);
        ArgumentException.ThrowIfNullOrWhiteSpace(CampaignLedgerPath);
        if (
            !Path.IsPathFullyQualified(RepositoryPath)
            || !Path.IsPathFullyQualified(StatePath)
            || !Path.IsPathFullyQualified(CampaignLedgerPath)
        )
        {
            throw new ArgumentException(
                "Repository, state and campaign ledger paths must be absolute."
            );
        }

        string repository = Path.GetFullPath(RepositoryPath).TrimEnd(Path.DirectorySeparatorChar);
        string state = Path.GetFullPath(StatePath).TrimEnd(Path.DirectorySeparatorChar);
        if (
            state.Equals(repository, StringComparison.OrdinalIgnoreCase)
            || state.StartsWith(
                repository + Path.DirectorySeparatorChar,
                StringComparison.OrdinalIgnoreCase
            )
        )
        {
            throw new ArgumentException("Run state must be outside the original checkout.");
        }

        string ledger = Path.GetFullPath(CampaignLedgerPath);
        if (
            ledger.Equals(repository, StringComparison.OrdinalIgnoreCase)
            || ledger.StartsWith(
                repository + Path.DirectorySeparatorChar,
                StringComparison.OrdinalIgnoreCase
            )
        )
        {
            throw new ArgumentException(
                "The campaign ledger must be outside the original checkout."
            );
        }

        if (
            WorkerCount < 1
            || DurationSeconds <= 0
            || PlanningSeconds < 0
            || PlanningSeconds > DurationSeconds
            || RunAiCreditAllowance < 30
            || CampaignAiCreditAllowance < RunAiCreditAllowance
        )
        {
            throw new ArgumentException(
                "Worker count, deadlines and native credit allowances must be valid."
            );
        }

        if (NativePermissionPolicy is not ("deny" or "trusted"))
        {
            throw new ArgumentException(
                "Native permission policy must be deny or explicitly trusted."
            );
        }

        foreach (
            CheckCommand check in CandidateChecks.Concat(IntegrationChecks).Concat(FinalChecks)
        )
        {
            ArgumentNullException.ThrowIfNull(check);
            ArgumentException.ThrowIfNullOrWhiteSpace(check.Executable);
            if (check.TimeoutSeconds <= 0)
            {
                throw new ArgumentException("Every check requires a finite positive timeout.");
            }
        }
    }
}
