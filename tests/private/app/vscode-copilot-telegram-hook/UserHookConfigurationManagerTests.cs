using System.Text.Json;
using Xunit;

namespace Hcoona.VsCodeCopilotTelegramHook.Tests;

public sealed class UserHookConfigurationManagerTests
{
    [Fact]
    public void InstallManagedHookFilePreservesUnmanagedEntriesAndUninstallRemovesOnlyManagedOnes()
    {
        DirectoryInfo tempDirectory = Directory.CreateTempSubdirectory();

        try
        {
            string hookFilePath = Path.Combine(
                tempDirectory.FullName,
                AppConstants.ManagedHookFileName);
            File.WriteAllText(
                hookFilePath,
                """
                {
                                        "model": "gpt-5.4",
                                        "hooks": {
                                                "Stop": [
                                                        {
                                                                "type": "command",
                                                                "command": "echo custom-stop",
                                                                "timeout": 15,
                                                                "env": {
                                                                        "CUSTOM_FLAG": "1"
                                                                }
                                                        }
                                                ]
                                        }
                }
                """);

            ConfigurationApplyResult installResult =
                UserHookConfigurationManager.InstallManagedHookFile(
                    hookFilePath,
                    "managed session-start",
                    "managed user-prompt-submit",
                    "managed stop",
                    "2026-03-13T12:34:56.789Z");

            Assert.True(installResult.Applied);
            Assert.True(UserHookConfigurationManager.IsManagedHookFileInstalled(hookFilePath));

            UserHookSettingsDocument installedSettings = ReadSettings(hookFilePath);
            Assert.Equal("gpt-5.4", installedSettings.AdditionalProperties?["model"].GetString());
            Assert.Single(installedSettings.Hooks["SessionStart"]);
            Assert.Single(installedSettings.Hooks["UserPromptSubmit"]);
            Assert.Equal(2, installedSettings.Hooks["Stop"].Count);

            ConfigurationApplyResult uninstallResult =
                UserHookConfigurationManager.UninstallManagedHookFile(hookFilePath);

            Assert.True(uninstallResult.Applied);
            Assert.False(UserHookConfigurationManager.IsManagedHookFileInstalled(hookFilePath));

            UserHookSettingsDocument uninstalledSettings = ReadSettings(hookFilePath);
            Assert.False(uninstalledSettings.Hooks.ContainsKey("SessionStart"));
            Assert.False(uninstalledSettings.Hooks.ContainsKey("UserPromptSubmit"));
            Assert.Single(uninstalledSettings.Hooks["Stop"]);
            Assert.Equal("echo custom-stop", uninstalledSettings.Hooks["Stop"][0].Command);
        }
        finally
        {
            tempDirectory.Delete(recursive: true);
        }
    }

    [Fact]
    public void UninstallManagedHookFileDeletesToolOwnedHookFileWhenOnlyManagedEntriesExist()
    {
        DirectoryInfo tempDirectory = Directory.CreateTempSubdirectory();

        try
        {
            string hookFilePath = Path.Combine(
                tempDirectory.FullName,
                AppConstants.ManagedHookFileName);

            ConfigurationApplyResult installResult =
                UserHookConfigurationManager.InstallManagedHookFile(
                    hookFilePath,
                    "managed session-start",
                    "managed user-prompt-submit",
                    "managed stop",
                    "2026-03-13T12:34:56.789Z");

            ConfigurationApplyResult uninstallResult =
                UserHookConfigurationManager.UninstallManagedHookFile(hookFilePath);

            Assert.True(installResult.Applied);
            Assert.True(uninstallResult.Applied);
            Assert.False(File.Exists(hookFilePath));
        }
        finally
        {
            tempDirectory.Delete(recursive: true);
        }
    }

    private static UserHookSettingsDocument ReadSettings(string settingsPath)
    {
        return JsonSerializer.Deserialize(
                File.ReadAllText(settingsPath),
                AppJsonSerializerContext.Default.UserHookSettingsDocument)
            ?? throw new InvalidOperationException("Expected a valid settings document.");
    }
}
