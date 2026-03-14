using System.Text.Json;
using Xunit;

namespace Hcoona.VsCodeCopilotTelegramHook.Tests;

public sealed class UserHookConfigurationManagerTests
{
    [Fact]
    public void InstallHooksPreservesUnmanagedEntriesAndUninstallRemovesOnlyManagedOnes()
    {
        DirectoryInfo tempDirectory = Directory.CreateTempSubdirectory();

        try
        {
            string settingsPath = Path.Combine(tempDirectory.FullName, "settings.json");
            File.WriteAllText(
                settingsPath,
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

            ConfigurationApplyResult installResult = UserHookConfigurationManager.InstallHooks(
                settingsPath,
                "managed session-start",
                "managed user-prompt-submit",
                "managed stop",
                "2026-03-13T12:34:56.789Z");

            Assert.True(installResult.Applied);
            Assert.True(UserHookConfigurationManager.IsHookInstalled(settingsPath));

            UserHookSettingsDocument installedSettings = ReadSettings(settingsPath);
            Assert.Equal("gpt-5.4", installedSettings.AdditionalProperties?["model"].GetString());
            Assert.Single(installedSettings.Hooks["SessionStart"]);
            Assert.Single(installedSettings.Hooks["UserPromptSubmit"]);
            Assert.Equal(2, installedSettings.Hooks["Stop"].Count);
            Assert.Contains(
                installedSettings.Hooks["Stop"],
                static entry => string.Equals(
                    entry.Command,
                    "echo custom-stop",
                    StringComparison.Ordinal));
            Assert.Contains(
                installedSettings.Hooks["Stop"],
                static entry => entry.Env.ContainsKey(AppConstants.ManagedHookEnvironmentVariable));

            ConfigurationApplyResult uninstallResult =
                UserHookConfigurationManager.UninstallHooks(settingsPath);

            Assert.True(uninstallResult.Applied);
            Assert.False(UserHookConfigurationManager.IsHookInstalled(settingsPath));

            UserHookSettingsDocument uninstalledSettings = ReadSettings(settingsPath);
            Assert.False(uninstalledSettings.Hooks.ContainsKey("SessionStart"));
            Assert.False(uninstalledSettings.Hooks.ContainsKey("UserPromptSubmit"));
            Assert.Single(uninstalledSettings.Hooks["Stop"]);
            Assert.Equal(
                "echo custom-stop",
                uninstalledSettings.Hooks["Stop"][0].Command);
        }
        finally
        {
            tempDirectory.Delete(recursive: true);
        }
    }

    [Fact]
    public void InstallInstructionCreatesCandidateWhenExistingFileIsUserManaged()
    {
        DirectoryInfo tempDirectory = Directory.CreateTempSubdirectory();

        try
        {
            string instructionPath = Path.Combine(
                tempDirectory.FullName,
                AppConstants.ManagedInstructionFileName);
            File.WriteAllText(instructionPath, "User-owned instruction content.");

            string managedContent = string.Join(
                Environment.NewLine,
                [
                    AppConstants.ManagedInstructionMarker,
                    "# Managed instruction",
                ]);

            ConfigurationApplyResult result = UserHookConfigurationManager.InstallInstruction(
                instructionPath,
                managedContent,
                "2026-03-13T12:34:56.789Z");

            Assert.False(result.Applied);
            Assert.NotNull(result.CandidatePath);
            Assert.Equal(
                "User-owned instruction content.",
                File.ReadAllText(instructionPath));
            Assert.True(File.Exists(result.CandidatePath));
            Assert.Equal(managedContent, File.ReadAllText(result.CandidatePath));
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
