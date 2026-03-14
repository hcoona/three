using Xunit;

namespace Hcoona.VsCodeCopilotTelegramHook.Tests;

public sealed class AppPathsTests
{
    [Fact]
    public void GetSessionLogPathStoresHookLogInsideSessionDirectory()
    {
        string workspacePath = Path.Combine(Path.GetTempPath(), "workspace-root");
        string sessionId = "session-123";

        string logPath = AppPaths.GetSessionLogPath(workspacePath, sessionId);

        Assert.Equal(
            Path.Combine(
                Path.GetFullPath(workspacePath),
                ".copilot",
                "sessions",
                AppPaths.GetSessionDirectoryName(sessionId),
                AppConstants.SessionLogFileName),
            logPath);
    }

    [Fact]
    public void GetWorkspaceAndUserLogPathsUseExpectedLocations()
    {
        string workspacePath = Path.Combine(Path.GetTempPath(), "workspace-root");
        string installRoot = Path.Combine(Path.GetTempPath(), "install-root");

        string workspaceLogPath = AppPaths.GetWorkspaceLogPath(workspacePath);
        string userLogPath = AppPaths.GetUserLogPath(installRoot);

        Assert.Equal(
            Path.Combine(
                Path.GetFullPath(workspacePath),
                AppConstants.CopilotDirectoryName,
                AppConstants.SessionLogFileName),
            workspaceLogPath);
        Assert.Equal(
            Path.Combine(
                Path.GetFullPath(installRoot),
                AppConstants.UserCommandLogFileName),
            userLogPath);
    }
}
