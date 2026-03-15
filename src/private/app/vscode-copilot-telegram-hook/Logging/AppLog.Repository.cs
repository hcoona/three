using Microsoft.Extensions.Logging;

namespace Hcoona.VsCodeCopilotTelegramHook.Logging;

internal static partial class AppLog
{
    [LoggerMessage(
        EventId = 1100,
        EventName = nameof(ProbingGitMetadata),
        Level = LogLevel.Debug,
        Message = "Probing git metadata for {WorkspacePath}.")]
    public static partial void ProbingGitMetadata(ILogger logger, string workspacePath);

    [LoggerMessage(
        EventId = 1101,
        EventName = nameof(WorkspaceNotGitRepo),
        Level = LogLevel.Debug,
        Message = "Workspace {WorkspacePath} does not appear to be inside a git repository.")]
    public static partial void WorkspaceNotGitRepo(ILogger logger, string workspacePath);

    [LoggerMessage(
        EventId = 1102,
        EventName = nameof(ResolvedGitMetadata),
        Level = LogLevel.Debug,
        Message =
            "Resolved git metadata for {WorkspacePath}: repository={RepositoryName}, "
            + "branch={BranchName}, commit={CommitId}.")]
    public static partial void ResolvedGitMetadata(
        ILogger logger,
        string workspacePath,
        string repositoryName,
        string branchName,
        string commitId);

    [LoggerMessage(
        EventId = 1103,
        EventName = nameof(GitCommandNoMetadata),
        Level = LogLevel.Debug,
        Message =
            "Git command {GitCommand} {GitArgument} did not return metadata for "
            + "{WorkingDirectory}.")]
    public static partial void GitCommandNoMetadata(
        ILogger logger,
        string gitCommand,
        string gitArgument,
        string workingDirectory);

    [LoggerMessage(
        EventId = 1104,
        EventName = nameof(GitCommandFailed),
        Level = LogLevel.Warning,
        Message = "Git command {GitCommand} {GitArgument} failed for {WorkingDirectory}.")]
    public static partial void GitCommandFailed(
        ILogger logger,
        Exception exception,
        string gitCommand,
        string gitArgument,
        string workingDirectory);
}
