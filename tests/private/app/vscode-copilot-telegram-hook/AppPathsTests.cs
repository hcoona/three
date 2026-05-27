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
                "notifications",
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
        string desktopVsCodeSettingsPath = Path.Combine(
            Path.GetTempPath(),
            "vscode-user",
            "settings.json");
        string serverVsCodeSettingsPath = Path.Combine(
            Path.GetTempPath(),
            "vscode-server-machine",
            "settings.json");
        string copilotCliHookFilePath = Path.Combine(
            Path.GetTempPath(),
            "copilot",
            "hooks",
            AppConstants.CopilotCliHookFileName);

        UserInstallationPaths resolvedPaths = AppPaths.ResolveUserPaths(
            new UserPathOverrides
            {
                InstallRoot = new DirectoryInfo(installRoot),
                CopilotCliHookFilePath = new FileInfo(copilotCliHookFilePath),
                VsCodeSettingsPaths =
                [
                    new FileInfo(desktopVsCodeSettingsPath),
                    new FileInfo(serverVsCodeSettingsPath),
                ],
            });

        Assert.Equal(
            Path.Combine(Path.GetFullPath(installRoot), AppConstants.ManagedHookFileName),
            resolvedPaths.ManagedHookFilePath);
        Assert.Equal(
            Path.GetFullPath(copilotCliHookFilePath),
            resolvedPaths.CopilotCliHookFilePath);
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

    [Fact]
    public void ValidateUserArtifactPathCollisionsRejectsSymlinkAliases()
    {
        if (!OperatingSystem.IsLinux())
        {
            return;
        }

        DirectoryInfo installRoot = Directory.CreateTempSubdirectory();
        try
        {
            string installedBinaryPath = Path.Combine(
                installRoot.FullName,
                AppPaths.GetManagedExecutableName());
            string managedHookFileAliasPath = Path.Combine(
                installRoot.FullName,
                AppConstants.ManagedHookFileName);
            File.WriteAllText(installedBinaryPath, "binary");
            File.CreateSymbolicLink(managedHookFileAliasPath, installedBinaryPath);

            UserInstallationPaths paths = new(
                installRoot.FullName,
                installedBinaryPath,
                managedHookFileAliasPath,
                Path.Combine(installRoot.FullName, AppConstants.CopilotCliHookFileName),
                [],
                AppPaths.GetUserLogPath(installRoot.FullName));

            string? validationError = AppPaths.ValidateUserArtifactPathCollisions(paths);

            Assert.NotNull(validationError);
        }
        finally
        {
            installRoot.Delete(recursive: true);
        }
    }

    [Fact]
    public void ValidateUserArtifactPathCollisionsCanIncludeExistingInapplicableSettingsTargets()
    {
        DirectoryInfo installRoot = Directory.CreateTempSubdirectory();
        try
        {
            string managedHookFilePath = Path.Combine(
                installRoot.FullName,
                AppConstants.ManagedHookFileName);
            File.WriteAllText(managedHookFilePath, "{}");
            UserInstallationPaths paths = new(
                installRoot.FullName,
                Path.Combine(installRoot.FullName, AppPaths.GetManagedExecutableName()),
                managedHookFilePath,
                Path.Combine(installRoot.FullName, AppConstants.CopilotCliHookFileName),
                [
                    new VsCodeSettingsTarget(
                        managedHookFilePath,
                        IsApplicable: false,
                        DisplayName: "VS Code Server Machine settings",
                        InapplicableReason: "not detected"),
                ],
                AppPaths.GetUserLogPath(installRoot.FullName));

            string? validationError = AppPaths.ValidateUserArtifactPathCollisions(
                paths,
                includeVsCodeSettingsTarget: static target =>
                    target.IsApplicable || File.Exists(target.SettingsPath));

            Assert.NotNull(validationError);
        }
        finally
        {
            installRoot.Delete(recursive: true);
        }
    }
}
