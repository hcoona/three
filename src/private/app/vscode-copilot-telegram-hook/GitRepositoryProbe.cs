using Hcoona.VsCodeCopilotTelegramHook.Logging;
using Microsoft.Extensions.Logging;

namespace Hcoona.VsCodeCopilotTelegramHook;

internal sealed class GitRepositoryProbe(
    IProcessRunner processRunner,
    ILogger<GitRepositoryProbe> logger)
{
    public async Task<GitRepositoryMetadata?> TryProbeAsync(
        string workspacePath,
        CancellationToken cancellationToken)
    {
        string fullWorkspacePath = Path.GetFullPath(workspacePath);
        AppLog.ProbingGitMetadata(logger, fullWorkspacePath);

        string? topLevelPath = await TryGetTrimmedOutputAsync(
            fullWorkspacePath,
            "rev-parse",
            "--show-toplevel",
            cancellationToken);

        if (string.IsNullOrWhiteSpace(topLevelPath))
        {
            AppLog.WorkspaceNotGitRepo(logger, fullWorkspacePath);
            return null;
        }

        string? branchName = await TryGetTrimmedOutputAsync(
            fullWorkspacePath,
            "branch",
            "--show-current",
            cancellationToken);

        string? commitId = await TryGetTrimmedOutputAsync(
            fullWorkspacePath,
            "rev-parse",
            "HEAD",
            cancellationToken);

        string repositoryName = Path.GetFileName(
            topLevelPath.TrimEnd(
                Path.DirectorySeparatorChar,
                Path.AltDirectorySeparatorChar));

        GitRepositoryMetadata metadata = new(
            topLevelPath,
            repositoryName,
            NullIfWhitespace(branchName),
            NullIfWhitespace(commitId));
        AppLog.ResolvedGitMetadata(
            logger,
            fullWorkspacePath,
            metadata.RepositoryName,
            metadata.BranchName ?? "<detached>",
            metadata.CommitId ?? "<unknown>");
        return metadata;
    }

    private async Task<string?> TryGetTrimmedOutputAsync(
        string workingDirectory,
        string gitCommand,
        string gitArgument,
        CancellationToken cancellationToken)
    {
        try
        {
            ProcessExecutionResult result = await processRunner.RunAsync(
                "git",
                ["-C", workingDirectory, gitCommand, gitArgument],
                workingDirectory,
                standardInput: null,
                logOptions: null,
                cancellationToken);

            if (!result.Succeeded)
            {
                AppLog.GitCommandNoMetadata(
                    logger,
                    gitCommand,
                    gitArgument,
                    workingDirectory);
            }

            return result.Succeeded ? NullIfWhitespace(result.StandardOutput.Trim()) : null;
        }
        catch (InvalidOperationException ex)
        {
            AppLog.GitCommandFailed(
                logger,
                ex,
                gitCommand,
                gitArgument,
                workingDirectory);
            return null;
        }
    }

    private static string? NullIfWhitespace(string? value)
        => string.IsNullOrWhiteSpace(value) ? null : value;
}
