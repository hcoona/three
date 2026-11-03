namespace Hcoona.VsCodeCopilotTelegramHook.Tests;

// Default runner for services whose tests do not exercise process behavior: the workspace is
// not a Git repository and every other tool, such as gopass, is not installed.
internal sealed class NoExternalToolsProcessRunner : IProcessRunner
{
    public Task<ProcessExecutionResult> RunAsync(
        string fileName,
        IReadOnlyList<string> arguments,
        string? workingDirectory,
        string? standardInput,
        ProcessLogOptions? logOptions,
        CancellationToken cancellationToken)
    {
        if (string.Equals(fileName, "git", StringComparison.Ordinal))
        {
            return Task.FromResult(
                new ProcessExecutionResult(128, string.Empty, "not a git repository"));
        }

        throw new InvalidOperationException($"Failed to start process '{fileName}'.");
    }
}
