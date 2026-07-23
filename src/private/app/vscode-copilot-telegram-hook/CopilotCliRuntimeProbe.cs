namespace Hcoona.VsCodeCopilotTelegramHook;

internal sealed class CopilotCliRuntimeProbe(IProcessRunner processRunner)
{
    public async Task<CopilotCliRuntimeStatus> GetStatusAsync(
        CancellationToken cancellationToken)
    {
        ProcessExecutionResult result;
        try
        {
            result = await processRunner.RunAsync(
                "copilot",
                ["version"],
                workingDirectory: null,
                standardInput: null,
                logOptions: null,
                cancellationToken);
        }
        catch (InvalidOperationException ex)
        {
            return new CopilotCliRuntimeStatus(false, null, ex.Message);
        }

        if (!result.Succeeded)
        {
            string error = string.IsNullOrWhiteSpace(result.StandardError)
                ? $"copilot version exited with code {result.ExitCode}."
                : result.StandardError.Trim();
            return new CopilotCliRuntimeStatus(false, null, error);
        }

        if (!TryParseVersion(result.StandardOutput, out Version? version))
        {
            return new CopilotCliRuntimeStatus(
                false,
                null,
                $"Could not parse Copilot CLI version from: {result.StandardOutput.Trim()}");
        }

        bool userExtensionsLoadByDefault =
            version >= AppConstants.MinimumCopilotCliUserExtensionsVersion;
        return new CopilotCliRuntimeStatus(
            userExtensionsLoadByDefault,
            version,
            userExtensionsLoadByDefault
                ? $"GitHub Copilot CLI {version}"
                : $"GitHub Copilot CLI {version}; update to "
                    + $"{AppConstants.MinimumCopilotCliUserExtensionsVersion} or later");
    }

    internal static bool TryParseVersion(string output, out Version? version)
    {
        const string versionPrefix = "GitHub Copilot CLI ";
        int prefixIndex = output.IndexOf(versionPrefix, StringComparison.OrdinalIgnoreCase);
        if (prefixIndex >= 0)
        {
            string remainder = output[(prefixIndex + versionPrefix.Length)..].TrimStart();
            int separatorIndex = remainder.IndexOfAny([' ', '\r', '\n', '\t']);
            string versionText = separatorIndex >= 0
                ? remainder[..separatorIndex]
                : remainder;
            string versionCore = versionText.Split('-', 2)[0];
            if (Version.TryParse(versionCore, out version))
            {
                return true;
            }
        }

        version = null;
        return false;
    }
}

internal sealed record CopilotCliRuntimeStatus(
    bool UserExtensionsSupported,
    Version? Version,
    string Detail);
