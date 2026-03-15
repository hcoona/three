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

    [Fact]
    public void ResolveUserPathsUsesManagedHookFileAndVsCodeSettingsTargets()
    {
        string installRoot = Path.Combine(Path.GetTempPath(), "install-root");
        string instructionsDirectory = Path.Combine(Path.GetTempPath(), "instructions-root");
        string desktopVsCodeSettingsPath = Path.Combine(
            Path.GetTempPath(),
            "vscode-user",
            "settings.json");
        string serverVsCodeSettingsPath = Path.Combine(
            Path.GetTempPath(),
            "vscode-server-machine",
            "settings.json");

        UserInstallationPaths resolvedPaths = AppPaths.ResolveUserPaths(
            new UserPathOverrides
            {
                InstallRoot = new DirectoryInfo(installRoot),
                InstructionsDirectory = new DirectoryInfo(instructionsDirectory),
                VsCodeSettingsPaths =
                [
                    new FileInfo(desktopVsCodeSettingsPath),
                    new FileInfo(serverVsCodeSettingsPath),
                ],
            });

        Assert.Equal(
            Path.Combine(Path.GetFullPath(installRoot), AppConstants.ManagedHookFileName),
            resolvedPaths.ManagedHookFilePath);
        Assert.Collection(
            resolvedPaths.VsCodeSettingsTargets,
            target =>
            {
                Assert.Equal(Path.GetFullPath(desktopVsCodeSettingsPath), target.SettingsPath);
                Assert.True(target.IsApplicable);
            },
            target =>
            {
                Assert.Equal(Path.GetFullPath(serverVsCodeSettingsPath), target.SettingsPath);
                Assert.True(target.IsApplicable);
            });
    }

    [Fact]
    public void GetDefaultVsCodeSettingsTargetsIncludeDesktopAndServerTargets()
    {
        IReadOnlyList<VsCodeSettingsTarget> defaultSettingsTargets =
            AppPaths.GetDefaultVsCodeSettingsTargets();

        VsCodeSettingsTarget desktopTarget = Assert.Single(
            defaultSettingsTargets,
            target => string.Equals(
                target.SettingsPath,
                Path.GetFullPath(AppPaths.GetDefaultVsCodeSettingsPath()),
                StringComparison.Ordinal));
        Assert.True(desktopTarget.IsApplicable);

        VsCodeSettingsTarget serverTarget = Assert.Single(
            defaultSettingsTargets,
            target => string.Equals(
                target.SettingsPath,
                Path.GetFullPath(AppPaths.GetDefaultVsCodeServerSettingsPath()),
                StringComparison.Ordinal));
        Assert.Equal(OperatingSystem.IsLinux(), serverTarget.IsApplicable);
        Assert.Equal(
            OperatingSystem.IsLinux()
                ? null
                : "No same-host VS Code Server installation was detected under '~/.vscode-server'.",
            serverTarget.InapplicableReason);
    }
}
