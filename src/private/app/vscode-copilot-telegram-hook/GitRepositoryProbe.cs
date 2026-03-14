namespace Hcoona.VsCodeCopilotTelegramHook;

internal static class GitRepositoryProbe
{
    public static async Task<GitRepositoryMetadata?> TryProbeAsync(
        string workspacePath,
        CancellationToken cancellationToken)
    {
        string fullWorkspacePath = Path.GetFullPath(workspacePath);

        string? topLevelPath = await TryGetTrimmedOutputAsync(
            fullWorkspacePath,
            "rev-parse",
            "--show-toplevel",
            cancellationToken);

        if (string.IsNullOrWhiteSpace(topLevelPath))
        {
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

        return new GitRepositoryMetadata(
            topLevelPath,
            repositoryName,
            NullIfWhitespace(branchName),
            NullIfWhitespace(commitId));
    }

    private static async Task<string?> TryGetTrimmedOutputAsync(
        string workingDirectory,
        string gitCommand,
        string gitArgument,
        CancellationToken cancellationToken)
    {
        try
        {
            ProcessExecutionResult result = await ProcessRunner.RunAsync(
                "git",
                ["-C", workingDirectory, gitCommand, gitArgument],
                workingDirectory,
                standardInput: null,
                cancellationToken);

            return result.Succeeded ? NullIfWhitespace(result.StandardOutput.Trim()) : null;
        }
        catch (InvalidOperationException)
        {
            return null;
        }
    }

    private static string? NullIfWhitespace(string? value)
        => string.IsNullOrWhiteSpace(value) ? null : value;
}
