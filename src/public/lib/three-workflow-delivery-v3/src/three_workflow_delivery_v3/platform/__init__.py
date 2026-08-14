"""Injectable platform client contracts for Workflow Delivery v3."""

from three_workflow_delivery_v3.platform.github import (
    GitHubActionsHistoryClient,
    GitHubArtifact,
    GitHubJob,
    GitHubPage,
    GitHubRun,
    GitHubRunAttemptFact,
    iter_all_artifacts,
    iter_all_attempt_jobs,
    iter_all_jobs,
    iter_all_runs,
)

__all__ = [
    "GitHubActionsHistoryClient",
    "GitHubArtifact",
    "GitHubJob",
    "GitHubPage",
    "GitHubRun",
    "GitHubRunAttemptFact",
    "iter_all_artifacts",
    "iter_all_attempt_jobs",
    "iter_all_jobs",
    "iter_all_runs",
]
