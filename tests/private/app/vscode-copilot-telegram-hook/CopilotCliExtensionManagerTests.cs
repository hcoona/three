using Xunit;

namespace Hcoona.VsCodeCopilotTelegramHook.Tests;

public sealed class CopilotCliExtensionManagerTests
{
    [Fact]
    public void BuildExtensionSourceUsesGlobalSessionIdleAndFiltersSubagentEvents()
    {
        string source = CopilotCliExtensionManager.BuildExtensionSource("/tmp/notifier");

        Assert.Contains("session.on(\"session.idle\"", source, StringComparison.Ordinal);
        Assert.Contains("session.on(\"assistant.idle\"", source, StringComparison.Ordinal);
        Assert.Contains(
            "if (event.agentId || event.data.aborted || pendingHumanRequests.size > 0)",
            source,
            StringComparison.Ordinal);
        Assert.Contains("session.on(\"assistant.message\"", source, StringComparison.Ordinal);
        Assert.Contains("session.on(\"session.task_complete\"", source, StringComparison.Ordinal);
        Assert.Contains("session.on(\"session.context_changed\"", source, StringComparison.Ordinal);
        Assert.Contains("cwd = event.data.cwd", source, StringComparison.Ordinal);
        Assert.Contains("const retryDelaysMs = [1000, 5000]", source, StringComparison.Ordinal);
        Assert.Contains(
            "const claimConflictRetryDelaysMs = [2000, 4000, 8000, 16000, 8000]",
            source,
            StringComparison.Ordinal);
        Assert.Contains(
            "error?.exitCode === retryableClaimConflictExitCode",
            source,
            StringComparison.Ordinal);
        Assert.Contains("error.exitCode = exitCode", source, StringComparison.Ordinal);
        Assert.Contains("const notifierTimeoutMs = 30000", source, StringComparison.Ordinal);
        Assert.Contains("let timedOut = false", source, StringComparison.Ordinal);
        Assert.Contains("child.kill()", source, StringComparison.Ordinal);
        Assert.Contains("child.kill(\"SIGKILL\")", source, StringComparison.Ordinal);
        Assert.Contains("child.on(\"close\"", source, StringComparison.Ordinal);
        Assert.Contains(
            "invokeNotifierWithRetry(payload, shouldDeliver)",
            source,
            StringComparison.Ordinal);
        Assert.Contains(
            "if (shouldDeliver && !shouldDeliver())",
            source,
            StringComparison.Ordinal);
        Assert.Contains(
            "() => isPendingRequest(requestKey)",
            source,
            StringComparison.Ordinal);
        Assert.Contains("let activityGeneration = 0", source, StringComparison.Ordinal);
        Assert.Contains("let completionPending = false", source, StringComparison.Ordinal);
        Assert.Contains("if (!completionPending)", source, StringComparison.Ordinal);
        Assert.Contains("lastMainMessage = null", source, StringComparison.Ordinal);
        Assert.Contains("generation: activityGeneration", source, StringComparison.Ordinal);
        Assert.Contains(
            "const completionGeneration = activityGeneration",
            source,
            StringComparison.Ordinal);
        Assert.Contains(
            "if (activityGeneration === completionGeneration)",
            source,
            StringComparison.Ordinal);
        Assert.Contains("pendingCompletionKeys.delete(completionKey)", source, StringComparison.Ordinal);
        Assert.Contains("event.data.delivery === \"queued\"", source, StringComparison.Ordinal);
        Assert.Contains("queuedRootInteractions.delete(interactionId)", source, StringComparison.Ordinal);
        Assert.Contains("event_type: \"permission_requested\"", source, StringComparison.Ordinal);
        Assert.Contains("event_type: \"elicitation_requested\"", source, StringComparison.Ordinal);
        Assert.Contains("summary: summary?.summary ?? null", source, StringComparison.Ordinal);
        Assert.Contains("summary_source: summary?.source ?? null", source, StringComparison.Ordinal);
    }

    [Fact]
    public void InstallCreatesParentDirectoryAndUninstallPreservesIt()
    {
        DirectoryInfo tempDirectory = Directory.CreateTempSubdirectory();

        try
        {
            string extensionFilePath = Path.Combine(
                tempDirectory.FullName,
                "extensions",
                "vscode-copilot-telegram-hook",
                AppConstants.CopilotCliExtensionFileName);
            string installedBinaryPath = Path.Combine(tempDirectory.FullName, "notifier");
            string extensionDirectory = Path.GetDirectoryName(extensionFilePath)!;
            if (!OperatingSystem.IsWindows())
            {
                Directory.CreateDirectory(extensionDirectory);
                File.SetUnixFileMode(
                    extensionDirectory,
                    UnixFileMode.UserRead
                        | UnixFileMode.UserWrite
                        | UnixFileMode.UserExecute
                        | UnixFileMode.GroupRead
                        | UnixFileMode.GroupExecute
                        | UnixFileMode.OtherRead
                        | UnixFileMode.OtherExecute);
            }

            ConfigurationApplyResult installResult = CopilotCliExtensionManager.Install(
                extensionFilePath,
                installedBinaryPath);
            ConfigurationApplyResult uninstallResult = CopilotCliExtensionManager.Uninstall(
                extensionFilePath);

            Assert.True(installResult.Applied);
            Assert.True(uninstallResult.Applied);
            Assert.False(File.Exists(extensionFilePath));
            Assert.True(Directory.Exists(Path.GetDirectoryName(extensionFilePath)));
            if (!OperatingSystem.IsWindows())
            {
                Assert.Equal(
                    UnixFileMode.UserRead
                        | UnixFileMode.UserWrite
                        | UnixFileMode.UserExecute,
                    File.GetUnixFileMode(extensionDirectory));
            }
        }
        finally
        {
            tempDirectory.Delete(recursive: true);
        }
    }

    [Fact]
    public void InstallRefusesToOverwriteUnmanagedExtension()
    {
        DirectoryInfo tempDirectory = Directory.CreateTempSubdirectory();

        try
        {
            string extensionFilePath = Path.Combine(
                tempDirectory.FullName,
                AppConstants.CopilotCliExtensionFileName);
            File.WriteAllText(extensionFilePath, "export default {};");

            ConfigurationApplyResult installResult = CopilotCliExtensionManager.Install(
                extensionFilePath,
                Path.Combine(tempDirectory.FullName, "notifier"));

            Assert.False(installResult.Applied);
            Assert.Equal("export default {};", File.ReadAllText(extensionFilePath));
        }
        finally
        {
            tempDirectory.Delete(recursive: true);
        }
    }

    [Fact]
    public void PreflightUninstallRejectsUnmanagedExtension()
    {
        DirectoryInfo tempDirectory = Directory.CreateTempSubdirectory();

        try
        {
            string extensionFilePath = Path.Combine(
                tempDirectory.FullName,
                AppConstants.CopilotCliExtensionFileName);
            File.WriteAllText(extensionFilePath, "export default {};");

            ConfigurationApplyResult? preflightResult =
                CopilotCliExtensionManager.PreflightUninstall(extensionFilePath);

            Assert.NotNull(preflightResult);
            Assert.False(preflightResult.Applied);
            Assert.Equal("export default {};", File.ReadAllText(extensionFilePath));
        }
        finally
        {
            tempDirectory.Delete(recursive: true);
        }
    }
}
