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

    [Fact]
    public void UninstallManagedHookFilePreservesTopLevelVersion()
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
                                        "version": 1,
                                        "hooks": {
                                                "SessionStart": [
                                                        {
                                                                "type": "command",
                                                                "command": "managed session-start",
                                                                "timeout": 10,
                                                                "env": {
                                                                        "HCOONA_VSCODE_COPILOT_TELEGRAM_HOOK": "1"
                                                                }
                                                        }
                                                ],
                                                "UserPromptSubmit": [
                                                        {
                                                                "type": "command",
                                                                "command": "managed user-prompt-submit",
                                                                "timeout": 10,
                                                                "env": {
                                                                        "HCOONA_VSCODE_COPILOT_TELEGRAM_HOOK": "1"
                                                                }
                                                        }
                                                ],
                                                "Stop": [
                                                        {
                                                                "type": "command",
                                                                "command": "managed stop",
                                                                "timeout": 20,
                                                                "env": {
                                                                        "HCOONA_VSCODE_COPILOT_TELEGRAM_HOOK": "1"
                                                                }
                                                        }
                                                ]
                                        }
                }
                """);

            ConfigurationApplyResult uninstallResult =
                UserHookConfigurationManager.UninstallManagedHookFile(hookFilePath);

            Assert.True(uninstallResult.Applied);
            Assert.True(File.Exists(hookFilePath));
            UserHookSettingsDocument uninstalledSettings = ReadSettings(hookFilePath);
            Assert.Equal(1, uninstalledSettings.Version);
            Assert.Empty(uninstalledSettings.Hooks);
        }
        finally
        {
            tempDirectory.Delete(recursive: true);
        }
    }

    [Fact]
    public void InstallManagedCopilotCliHookFileWritesVersionAndTimeoutSec()
    {
        DirectoryInfo tempDirectory = Directory.CreateTempSubdirectory();

        try
        {
            string hookFilePath = Path.Combine(
                tempDirectory.FullName,
                AppConstants.CopilotCliHookFileName);

            ConfigurationApplyResult installResult =
                UserHookConfigurationManager.InstallManagedCopilotCliHookFile(
                    hookFilePath,
                    "managed session-start",
                    "managed user-prompt-submit",
                    "managed stop",
                    "2026-03-13T12:34:56.789Z");

            Assert.True(installResult.Applied);
            Assert.True(
                UserHookConfigurationManager.IsManagedCopilotCliHookFileInstalled(hookFilePath));

            string rawJson = File.ReadAllText(hookFilePath);
            Assert.Contains("\"version\": 1", rawJson, StringComparison.Ordinal);
            Assert.Contains("\"timeoutSec\": 10", rawJson, StringComparison.Ordinal);
            Assert.DoesNotContain("\"timeout\":", rawJson, StringComparison.Ordinal);

            UserHookSettingsDocument installedSettings = ReadSettings(hookFilePath);
            Assert.Equal(1, installedSettings.Version);
            UserHookEntry stopEntry = Assert.Single(installedSettings.Hooks["Stop"]);
            Assert.Equal(20, stopEntry.TimeoutSec);
            Assert.Null(stopEntry.Timeout);
            Assert.Equal(
                AppConstants.ManagedHookCopilotCliSurfaceValue,
                stopEntry.Env[AppConstants.ManagedHookSurfaceEnvironmentVariable]);
        }
        finally
        {
            tempDirectory.Delete(recursive: true);
        }
    }

    [Fact]
    public void InstallManagedCopilotCliHookFilePreservesExplicitNullTimeoutOnUnrelatedEntries()
    {
        DirectoryInfo tempDirectory = Directory.CreateTempSubdirectory();

        try
        {
            string hookFilePath = Path.Combine(
                tempDirectory.FullName,
                AppConstants.CopilotCliHookFileName);
            File.WriteAllText(
                hookFilePath,
                """
                {
                                        "version": 1,
                                        "hooks": {
                                                "SessionStart": [
                                                        {
                                                                "type": "command",
                                                                "command": "custom session-start",
                                                                "timeout": null,
                                                                "env": {
                                                                        "CUSTOM_FLAG": "1"
                                                                }
                                                        }
                                                ]
                                        }
                }
                """);

            ConfigurationApplyResult installResult =
                UserHookConfigurationManager.InstallManagedCopilotCliHookFile(
                    hookFilePath,
                    "managed session-start",
                    "managed user-prompt-submit",
                    "managed stop",
                    "2026-03-13T12:34:56.789Z");

            Assert.True(installResult.Applied);
            string rawJson = File.ReadAllText(hookFilePath);
            Assert.Contains("\"timeout\": null", rawJson, StringComparison.Ordinal);
            UserHookSettingsDocument installedSettings = ReadSettings(hookFilePath);
            UserHookEntry preservedEntry = installedSettings.Hooks["SessionStart"]
                .Single(static entry => entry.Command == "custom session-start");
            Assert.True(preservedEntry.TimeoutPropertyPresent);
            Assert.Null(preservedEntry.Timeout);
            UserHookEntry managedEntry = installedSettings.Hooks["SessionStart"]
                .Single(static entry => entry.Command == "managed session-start");
            Assert.False(managedEntry.TimeoutPropertyPresent);
            Assert.Null(managedEntry.Timeout);
            Assert.Equal(10, managedEntry.TimeoutSec);
        }
        finally
        {
            tempDirectory.Delete(recursive: true);
        }
    }

    [Fact]
    public void InstallManagedCopilotCliHookFileRejectsExistingVersion2HookFile()
    {
        DirectoryInfo tempDirectory = Directory.CreateTempSubdirectory();

        try
        {
            string hookFilePath = Path.Combine(
                tempDirectory.FullName,
                AppConstants.CopilotCliHookFileName);
            const string OriginalContent = """
                {
                                        "version": 2,
                                        "hooks": {
                                                "Stop": [
                                                        {
                                                                "type": "command",
                                                                "command": "future stop",
                                                                "timeoutSec": 20
                                                        }
                                                ]
                                        }
                }
                """;
            File.WriteAllText(hookFilePath, OriginalContent);

            ConfigurationApplyResult installResult =
                UserHookConfigurationManager.InstallManagedCopilotCliHookFile(
                    hookFilePath,
                    "managed session-start",
                    "managed user-prompt-submit",
                    "managed stop",
                    "2026-03-13T12:34:56.789Z");

            Assert.False(installResult.Applied);
            Assert.NotNull(installResult.CandidatePath);
            Assert.True(File.Exists(installResult.CandidatePath));
            Assert.Equal(OriginalContent, File.ReadAllText(hookFilePath));

            UserHookSettingsDocument candidateSettings = ReadSettings(installResult.CandidatePath!);
            Assert.Equal(1, candidateSettings.Version);
            Assert.True(
                UserHookConfigurationManager.IsManagedCopilotCliHookFileInstalled(
                    installResult.CandidatePath!));
        }
        finally
        {
            tempDirectory.Delete(recursive: true);
        }
    }

    [Fact]
    public void InstallManagedCopilotCliHookFileRejectsExistingHookFileWithoutVersion()
    {
        DirectoryInfo tempDirectory = Directory.CreateTempSubdirectory();

        try
        {
            string hookFilePath = Path.Combine(
                tempDirectory.FullName,
                AppConstants.CopilotCliHookFileName);
            const string OriginalContent = """
                {
                                        "hooks": {
                                                "Stop": [
                                                        {
                                                                "type": "command",
                                                                "command": "missing version stop",
                                                                "timeoutSec": 20
                                                        }
                                                ]
                                        }
                }
                """;
            File.WriteAllText(hookFilePath, OriginalContent);

            ConfigurationApplyResult installResult =
                UserHookConfigurationManager.InstallManagedCopilotCliHookFile(
                    hookFilePath,
                    "managed session-start",
                    "managed user-prompt-submit",
                    "managed stop",
                    "2026-03-13T12:34:56.789Z");

            Assert.False(installResult.Applied);
            Assert.NotNull(installResult.CandidatePath);
            Assert.True(File.Exists(installResult.CandidatePath));
            Assert.Equal(OriginalContent, File.ReadAllText(hookFilePath));

            UserHookSettingsDocument candidateSettings = ReadSettings(installResult.CandidatePath!);
            Assert.Equal(1, candidateSettings.Version);
            Assert.True(
                UserHookConfigurationManager.IsManagedCopilotCliHookFileInstalled(
                    installResult.CandidatePath!));
        }
        finally
        {
            tempDirectory.Delete(recursive: true);
        }
    }

    [Theory]
    [InlineData("\"version\": 2,")]
    [InlineData("")]
    public void PreflightManagedCopilotCliHookFileRejectsUnsupportedOrMissingVersionWithoutCandidate(
        string versionProperty)
    {
        DirectoryInfo tempDirectory = Directory.CreateTempSubdirectory();

        try
        {
            string hookFilePath = Path.Combine(
                tempDirectory.FullName,
                AppConstants.CopilotCliHookFileName);
            string originalContent =
                $$"""
                {
                                        {{versionProperty}}
                                        "hooks": {
                                                "Stop": [
                                                        {
                                                                "type": "command",
                                                                "command": "existing stop",
                                                                "timeoutSec": 20
                                                        }
                                                ]
                                        }
                }
                """;
            File.WriteAllText(hookFilePath, originalContent);
            Dictionary<string, string> originalDirectoryContents =
                ReadDirectoryFileContents(tempDirectory.FullName);

            ConfigurationApplyResult? preflightResult =
                UserHookConfigurationManager.PreflightManagedCopilotCliHookFile(
                    hookFilePath,
                    "managed session-start",
                    "managed user-prompt-submit",
                    "managed stop",
                    "2026-03-13T12:34:56.789Z");

            Assert.NotNull(preflightResult);
            Assert.False(preflightResult.Applied);
            Assert.Null(preflightResult.CandidatePath);
            Assert.Contains(
                "Manual review is required",
                preflightResult.Message,
                StringComparison.Ordinal);
            Assert.Equal(originalContent, File.ReadAllText(hookFilePath));
            Assert.Equal(
                originalDirectoryContents,
                ReadDirectoryFileContents(tempDirectory.FullName));
            Assert.Empty(
                Directory.EnumerateFiles(
                    tempDirectory.FullName,
                    "*.candidate.json",
                    SearchOption.AllDirectories));
        }
        finally
        {
            tempDirectory.Delete(recursive: true);
        }
    }

    [Fact]
    public void UninstallManagedCopilotCliHookFileDeletesToolOwnedVersionedHookFile()
    {
        DirectoryInfo tempDirectory = Directory.CreateTempSubdirectory();

        try
        {
            string hookFilePath = Path.Combine(
                tempDirectory.FullName,
                AppConstants.CopilotCliHookFileName);

            ConfigurationApplyResult installResult =
                UserHookConfigurationManager.InstallManagedCopilotCliHookFile(
                    hookFilePath,
                    "managed session-start",
                    "managed user-prompt-submit",
                    "managed stop",
                    "2026-03-13T12:34:56.789Z");

            UserHookSettingsDocument installedSettings = ReadSettings(hookFilePath);
            Assert.Equal(1, installedSettings.Version);

            ConfigurationApplyResult uninstallResult =
                UserHookConfigurationManager.UninstallManagedCopilotCliHookFile(hookFilePath);

            Assert.True(installResult.Applied);
            Assert.True(uninstallResult.Applied);
            Assert.False(File.Exists(hookFilePath));
        }
        finally
        {
            tempDirectory.Delete(recursive: true);
        }
    }

    [Fact]
    public void UninstallManagedCopilotCliHookFilePreservesVersion2HookFile()
    {
        DirectoryInfo tempDirectory = Directory.CreateTempSubdirectory();

        try
        {
            string hookFilePath = Path.Combine(
                tempDirectory.FullName,
                AppConstants.CopilotCliHookFileName);
            const string OriginalContent =
                """
                {
                                        "version": 2,
                                        "hooks": {
                                                "SessionStart": [
                                                        {
                                                                "type": "command",
                                                                "command": "managed session-start",
                                                                "timeoutSec": 10,
                                                                "env": {
                                                                        "HCOONA_VSCODE_COPILOT_TELEGRAM_HOOK": "1",
                                                                        "HCOONA_VSCODE_COPILOT_TELEGRAM_HOOK_SURFACE": "copilot-cli"
                                                                }
                                                        }
                                                ],
                                                "UserPromptSubmit": [
                                                        {
                                                                "type": "command",
                                                                "command": "managed user-prompt-submit",
                                                                "timeoutSec": 10,
                                                                "env": {
                                                                        "HCOONA_VSCODE_COPILOT_TELEGRAM_HOOK": "1",
                                                                        "HCOONA_VSCODE_COPILOT_TELEGRAM_HOOK_SURFACE": "copilot-cli"
                                                                }
                                                        }
                                                ],
                                                "Stop": [
                                                        {
                                                                "type": "command",
                                                                "command": "managed stop",
                                                                "timeoutSec": 20,
                                                                "env": {
                                                                        "HCOONA_VSCODE_COPILOT_TELEGRAM_HOOK": "1",
                                                                        "HCOONA_VSCODE_COPILOT_TELEGRAM_HOOK_SURFACE": "copilot-cli"
                                                                }
                                                        }
                                                ]
                                        }
                }
                """;
            File.WriteAllText(hookFilePath, OriginalContent);

            ConfigurationApplyResult uninstallResult =
                UserHookConfigurationManager.UninstallManagedCopilotCliHookFile(hookFilePath);

            Assert.False(uninstallResult.Applied);
            Assert.Contains("manual", uninstallResult.Message, StringComparison.OrdinalIgnoreCase);
            Assert.True(File.Exists(hookFilePath));
            Assert.Equal(OriginalContent, File.ReadAllText(hookFilePath));
        }
        finally
        {
            tempDirectory.Delete(recursive: true);
        }
    }

    [Fact]
    public void UninstallManagedCopilotCliHookFilePreservesHookFileWithoutVersion()
    {
        DirectoryInfo tempDirectory = Directory.CreateTempSubdirectory();

        try
        {
            string hookFilePath = Path.Combine(
                tempDirectory.FullName,
                AppConstants.CopilotCliHookFileName);
            const string OriginalContent =
                """
                {
                                        "hooks": {
                                                "SessionStart": [
                                                        {
                                                                "type": "command",
                                                                "command": "managed session-start",
                                                                "timeoutSec": 10,
                                                                "env": {
                                                                        "HCOONA_VSCODE_COPILOT_TELEGRAM_HOOK": "1",
                                                                        "HCOONA_VSCODE_COPILOT_TELEGRAM_HOOK_SURFACE": "copilot-cli"
                                                                }
                                                        }
                                                ],
                                                "UserPromptSubmit": [
                                                        {
                                                                "type": "command",
                                                                "command": "managed user-prompt-submit",
                                                                "timeoutSec": 10,
                                                                "env": {
                                                                        "HCOONA_VSCODE_COPILOT_TELEGRAM_HOOK": "1",
                                                                        "HCOONA_VSCODE_COPILOT_TELEGRAM_HOOK_SURFACE": "copilot-cli"
                                                                }
                                                        }
                                                ],
                                                "Stop": [
                                                        {
                                                                "type": "command",
                                                                "command": "managed stop",
                                                                "timeoutSec": 20,
                                                                "env": {
                                                                        "HCOONA_VSCODE_COPILOT_TELEGRAM_HOOK": "1",
                                                                        "HCOONA_VSCODE_COPILOT_TELEGRAM_HOOK_SURFACE": "copilot-cli"
                                                                }
                                                        }
                                                ]
                                        }
                }
                """;
            File.WriteAllText(hookFilePath, OriginalContent);

            ConfigurationApplyResult uninstallResult =
                UserHookConfigurationManager.UninstallManagedCopilotCliHookFile(hookFilePath);

            Assert.False(uninstallResult.Applied);
            Assert.Contains("manual", uninstallResult.Message, StringComparison.OrdinalIgnoreCase);
            Assert.True(File.Exists(hookFilePath));
            Assert.Equal(OriginalContent, File.ReadAllText(hookFilePath));
        }
        finally
        {
            tempDirectory.Delete(recursive: true);
        }
    }

    [Fact]
    public void InstallManagedCopilotCliHookFilePreservesNonCliGenericManagedEntries()
    {
        DirectoryInfo tempDirectory = Directory.CreateTempSubdirectory();

        try
        {
            string hookFilePath = Path.Combine(
                tempDirectory.FullName,
                AppConstants.CopilotCliHookFileName);
            File.WriteAllText(
                hookFilePath,
                """
                {
                                        "version": 1,
                                        "hooks": {
                                                "SessionStart": [
                                                        {
                                                                "type": "command",
                                                                "command": "generic session-start",
                                                                "timeout": 10,
                                                                "env": {
                                                                        "HCOONA_VSCODE_COPILOT_TELEGRAM_HOOK": "1"
                                                                }
                                                        },
                                                        {
                                                                "type": "command",
                                                                "command": "old cli session-start",
                                                                "timeoutSec": 10,
                                                                "env": {
                                                                        "HCOONA_VSCODE_COPILOT_TELEGRAM_HOOK": "1",
                                                                        "HCOONA_VSCODE_COPILOT_TELEGRAM_HOOK_SURFACE": "copilot-cli"
                                                                }
                                                        }
                                                ],
                                                "UserPromptSubmit": [
                                                        {
                                                                "type": "command",
                                                                "command": "generic user-prompt-submit",
                                                                "timeout": 10,
                                                                "env": {
                                                                        "HCOONA_VSCODE_COPILOT_TELEGRAM_HOOK": "1"
                                                                }
                                                        }
                                                ],
                                                "Stop": [
                                                        {
                                                                "type": "command",
                                                                "command": "generic stop",
                                                                "timeout": 20,
                                                                "env": {
                                                                        "HCOONA_VSCODE_COPILOT_TELEGRAM_HOOK": "1"
                                                                }
                                                        },
                                                        {
                                                                "type": "command",
                                                                "command": "old cli stop",
                                                                "timeoutSec": 20,
                                                                "env": {
                                                                        "HCOONA_VSCODE_COPILOT_TELEGRAM_HOOK": "1",
                                                                        "HCOONA_VSCODE_COPILOT_TELEGRAM_HOOK_SURFACE": "copilot-cli"
                                                                }
                                                        }
                                                ]
                                        }
                }
                """);

            ConfigurationApplyResult installResult =
                UserHookConfigurationManager.InstallManagedCopilotCliHookFile(
                    hookFilePath,
                    "managed session-start",
                    "managed user-prompt-submit",
                    "managed stop",
                    "2026-03-13T12:34:56.789Z");

            Assert.True(installResult.Applied);
            UserHookSettingsDocument installedSettings = ReadSettings(hookFilePath);

            Assert.Contains(
                installedSettings.Hooks["SessionStart"],
                entry => entry.Command == "generic session-start");
            Assert.Contains(
                installedSettings.Hooks["UserPromptSubmit"],
                entry => entry.Command == "generic user-prompt-submit");
            Assert.Contains(
                installedSettings.Hooks["Stop"],
                entry => entry.Command == "generic stop");
            Assert.Contains(
                installedSettings.Hooks["SessionStart"],
                entry => entry.Command == "managed session-start");
            Assert.Contains(
                installedSettings.Hooks["UserPromptSubmit"],
                entry => entry.Command == "managed user-prompt-submit");
            Assert.Contains(
                installedSettings.Hooks["Stop"],
                entry => entry.Command == "managed stop");
            Assert.DoesNotContain(
                installedSettings.Hooks["SessionStart"],
                entry => entry.Command == "old cli session-start");
            Assert.DoesNotContain(
                installedSettings.Hooks["Stop"],
                entry => entry.Command == "old cli stop");
        }
        finally
        {
            tempDirectory.Delete(recursive: true);
        }
    }

    [Fact]
    public void UninstallManagedCopilotCliHookFilePreservesNonCliGenericManagedEntries()
    {
        DirectoryInfo tempDirectory = Directory.CreateTempSubdirectory();

        try
        {
            string hookFilePath = Path.Combine(
                tempDirectory.FullName,
                AppConstants.CopilotCliHookFileName);
            string originalContent =
                """
                {
                                        "version": 1,
                                        "hooks": {
                                                "SessionStart": [
                                                        {
                                                                "type": "command",
                                                                "command": "generic session-start",
                                                                "timeout": 10,
                                                                "env": {
                                                                        "HCOONA_VSCODE_COPILOT_TELEGRAM_HOOK": "1"
                                                                }
                                                        }
                                                ],
                                                "UserPromptSubmit": [
                                                        {
                                                                "type": "command",
                                                                "command": "generic user-prompt-submit",
                                                                "timeout": 10,
                                                                "env": {
                                                                        "HCOONA_VSCODE_COPILOT_TELEGRAM_HOOK": "1"
                                                                }
                                                        }
                                                ],
                                                "Stop": [
                                                        {
                                                                "type": "command",
                                                                "command": "generic stop",
                                                                "timeout": 20,
                                                                "env": {
                                                                        "HCOONA_VSCODE_COPILOT_TELEGRAM_HOOK": "1"
                                                                }
                                                        }
                                                ]
                                        }
                }
                """;
            File.WriteAllText(hookFilePath, originalContent);

            using IDisposable _ = AtomicTextFileWriter.UseWriterForTesting(
                new ThrowingTextFileWriter());

            ConfigurationApplyResult uninstallResult =
                UserHookConfigurationManager.UninstallManagedCopilotCliHookFile(hookFilePath);

            Assert.True(uninstallResult.Applied);
            Assert.True(File.Exists(hookFilePath));
            Assert.Equal(originalContent, File.ReadAllText(hookFilePath));
            UserHookSettingsDocument uninstalledSettings = ReadSettings(hookFilePath);
            Assert.Equal(
                "generic session-start",
                uninstalledSettings.Hooks["SessionStart"][0].Command);
            Assert.Equal(
                "generic user-prompt-submit",
                uninstalledSettings.Hooks["UserPromptSubmit"][0].Command);
            Assert.Equal("generic stop", uninstalledSettings.Hooks["Stop"][0].Command);
        }
        finally
        {
            tempDirectory.Delete(recursive: true);
        }
    }

    [Fact]
    public void UninstallManagedCopilotCliHookFileRemovesMalformedCliSurfaceEntries()
    {
        DirectoryInfo tempDirectory = Directory.CreateTempSubdirectory();

        try
        {
            string hookFilePath = Path.Combine(
                tempDirectory.FullName,
                AppConstants.CopilotCliHookFileName);
            File.WriteAllText(
                hookFilePath,
                """
                {
                                        "version": 1,
                                        "hooks": {
                                                "SessionStart": [
                                                        {
                                                                "type": "command",
                                                                "command": "generic session-start",
                                                                "timeout": 10,
                                                                "env": {
                                                                        "HCOONA_VSCODE_COPILOT_TELEGRAM_HOOK": "1"
                                                                }
                                                        },
                                                        {
                                                                "type": "command",
                                                                "command": "stale cli session-start",
                                                                "timeout": 10,
                                                                "env": {
                                                                        "HCOONA_VSCODE_COPILOT_TELEGRAM_HOOK": "1",
                                                                        "HCOONA_VSCODE_COPILOT_TELEGRAM_HOOK_SURFACE": "copilot-cli"
                                                                }
                                                        }
                                                ],
                                                "UserPromptSubmit": [
                                                        {
                                                                "type": "command",
                                                                "command": "generic user-prompt-submit",
                                                                "timeout": 10,
                                                                "env": {
                                                                        "HCOONA_VSCODE_COPILOT_TELEGRAM_HOOK": "1"
                                                                }
                                                        },
                                                        {
                                                                "type": "command",
                                                                "command": "malformed cli user-prompt-submit",
                                                                "timeout": 10,
                                                                "timeoutSec": 10,
                                                                "env": {
                                                                        "HCOONA_VSCODE_COPILOT_TELEGRAM_HOOK": "1",
                                                                        "HCOONA_VSCODE_COPILOT_TELEGRAM_HOOK_SURFACE": "copilot-cli"
                                                                }
                                                        }
                                                ],
                                                "Stop": [
                                                        {
                                                                "type": "command",
                                                                "command": "generic stop",
                                                                "timeout": 20,
                                                                "env": {
                                                                        "HCOONA_VSCODE_COPILOT_TELEGRAM_HOOK": "1"
                                                                }
                                                        },
                                                        {
                                                                "type": "command",
                                                                "command": "malformed cli stop",
                                                                "env": {
                                                                        "HCOONA_VSCODE_COPILOT_TELEGRAM_HOOK": "1",
                                                                        "HCOONA_VSCODE_COPILOT_TELEGRAM_HOOK_SURFACE": "copilot-cli"
                                                                }
                                                        }
                                                ]
                                        }
                }
                """);

            Assert.False(
                UserHookConfigurationManager.IsManagedCopilotCliHookFileInstalled(hookFilePath));

            ConfigurationApplyResult uninstallResult =
                UserHookConfigurationManager.UninstallManagedCopilotCliHookFile(hookFilePath);

            Assert.True(uninstallResult.Applied);
            Assert.True(File.Exists(hookFilePath));
            UserHookSettingsDocument uninstalledSettings = ReadSettings(hookFilePath);
            UserHookEntry sessionStartEntry =
                Assert.Single(uninstalledSettings.Hooks["SessionStart"]);
            UserHookEntry userPromptSubmitEntry =
                Assert.Single(uninstalledSettings.Hooks["UserPromptSubmit"]);
            UserHookEntry stopEntry = Assert.Single(uninstalledSettings.Hooks["Stop"]);
            Assert.Equal("generic session-start", sessionStartEntry.Command);
            Assert.Equal("generic user-prompt-submit", userPromptSubmitEntry.Command);
            Assert.Equal("generic stop", stopEntry.Command);
        }
        finally
        {
            tempDirectory.Delete(recursive: true);
        }
    }

    [Fact]
    public void UninstallManagedCopilotCliHookFileRemovesCliSurfaceEntriesFromUnknownEvents()
    {
        DirectoryInfo tempDirectory = Directory.CreateTempSubdirectory();

        try
        {
            string hookFilePath = Path.Combine(
                tempDirectory.FullName,
                AppConstants.CopilotCliHookFileName);
            File.WriteAllText(
                hookFilePath,
                """
                {
                                        "version": 1,
                                        "hooks": {
                                                "Notification": [
                                                        {
                                                                "type": "command",
                                                                "command": "custom notification",
                                                                "timeoutSec": 10,
                                                                "env": {
                                                                        "CUSTOM_FLAG": "1"
                                                                }
                                                        },
                                                        {
                                                                "type": "command",
                                                                "command": "generic managed notification",
                                                                "timeout": 10,
                                                                "env": {
                                                                        "HCOONA_VSCODE_COPILOT_TELEGRAM_HOOK": "1"
                                                                }
                                                        },
                                                        {
                                                                "type": "command",
                                                                "command": "stale cli notification",
                                                                "timeoutSec": 10,
                                                                "env": {
                                                                        "HCOONA_VSCODE_COPILOT_TELEGRAM_HOOK": "1",
                                                                        "HCOONA_VSCODE_COPILOT_TELEGRAM_HOOK_SURFACE": "copilot-cli"
                                                                }
                                                        }
                                                ]
                                        }
                }
                """);

            ConfigurationApplyResult uninstallResult =
                UserHookConfigurationManager.UninstallManagedCopilotCliHookFile(hookFilePath);

            Assert.True(uninstallResult.Applied);
            Assert.True(File.Exists(hookFilePath));
            UserHookSettingsDocument uninstalledSettings = ReadSettings(hookFilePath);
            Assert.True(uninstalledSettings.Hooks.ContainsKey("Notification"));
            Assert.Contains(
                uninstalledSettings.Hooks["Notification"],
                entry => entry.Command == "custom notification");
            Assert.Contains(
                uninstalledSettings.Hooks["Notification"],
                entry => entry.Command == "generic managed notification");
            Assert.DoesNotContain(
                uninstalledSettings.Hooks["Notification"],
                entry => entry.Command == "stale cli notification");
        }
        finally
        {
            tempDirectory.Delete(recursive: true);
        }
    }

    [Fact]
    public void IsManagedCopilotCliHookFileInstalledRequiresCliTimeoutSecEntries()
    {
        DirectoryInfo tempDirectory = Directory.CreateTempSubdirectory();

        try
        {
            string hookFilePath = Path.Combine(
                tempDirectory.FullName,
                AppConstants.CopilotCliHookFileName);
            File.WriteAllText(
                hookFilePath,
                """
                {
                                        "version": 1,
                                        "hooks": {
                                                "SessionStart": [
                                                        {
                                                                "type": "command",
                                                                "command": "managed session-start",
                                                                "timeout": 10,
                                                                "env": {
                                                                        "HCOONA_VSCODE_COPILOT_TELEGRAM_HOOK": "1"
                                                                }
                                                        }
                                                ],
                                                "UserPromptSubmit": [
                                                        {
                                                                "type": "command",
                                                                "command": "managed user-prompt-submit",
                                                                "timeout": 10,
                                                                "env": {
                                                                        "HCOONA_VSCODE_COPILOT_TELEGRAM_HOOK": "1"
                                                                }
                                                        }
                                                ],
                                                "Stop": [
                                                        {
                                                                "type": "command",
                                                                "command": "managed stop",
                                                                "timeout": 20,
                                                                "env": {
                                                                        "HCOONA_VSCODE_COPILOT_TELEGRAM_HOOK": "1"
                                                                }
                                                        }
                                                ]
                                        }
                }
                """);

            Assert.False(
                UserHookConfigurationManager.IsManagedCopilotCliHookFileInstalled(hookFilePath));
        }
        finally
        {
            tempDirectory.Delete(recursive: true);
        }
    }

    [Fact]
    public void IsManagedCopilotCliHookFileInstalledRejectsExplicitNullTimeout()
    {
        DirectoryInfo tempDirectory = Directory.CreateTempSubdirectory();

        try
        {
            string hookFilePath = Path.Combine(
                tempDirectory.FullName,
                AppConstants.CopilotCliHookFileName);
            File.WriteAllText(
                hookFilePath,
                """
                {
                                        "version": 1,
                                        "hooks": {
                                                "SessionStart": [
                                                        {
                                                                "type": "command",
                                                                "command": "managed session-start",
                                                                "timeout": null,
                                                                "timeoutSec": 10,
                                                                "env": {
                                                                        "HCOONA_VSCODE_COPILOT_TELEGRAM_HOOK": "1",
                                                                        "HCOONA_VSCODE_COPILOT_TELEGRAM_HOOK_SURFACE": "copilot-cli"
                                                                }
                                                        }
                                                ],
                                                "UserPromptSubmit": [
                                                        {
                                                                "type": "command",
                                                                "command": "managed user-prompt-submit",
                                                                "timeout": null,
                                                                "timeoutSec": 10,
                                                                "env": {
                                                                        "HCOONA_VSCODE_COPILOT_TELEGRAM_HOOK": "1",
                                                                        "HCOONA_VSCODE_COPILOT_TELEGRAM_HOOK_SURFACE": "copilot-cli"
                                                                }
                                                        }
                                                ],
                                                "Stop": [
                                                        {
                                                                "type": "command",
                                                                "command": "managed stop",
                                                                "timeout": null,
                                                                "timeoutSec": 20,
                                                                "env": {
                                                                        "HCOONA_VSCODE_COPILOT_TELEGRAM_HOOK": "1",
                                                                        "HCOONA_VSCODE_COPILOT_TELEGRAM_HOOK_SURFACE": "copilot-cli"
                                                                }
                                                        }
                                                ]
                                        }
                }
                """);

            Assert.False(
                UserHookConfigurationManager.IsManagedCopilotCliHookFileInstalled(hookFilePath));
        }
        finally
        {
            tempDirectory.Delete(recursive: true);
        }
    }

    [Fact]
    public void IsManagedCopilotCliHookFileInstalledRequiresCliSurfaceEntries()
    {
        DirectoryInfo tempDirectory = Directory.CreateTempSubdirectory();

        try
        {
            string hookFilePath = Path.Combine(
                tempDirectory.FullName,
                AppConstants.CopilotCliHookFileName);
            File.WriteAllText(
                hookFilePath,
                """
                {
                                        "version": 1,
                                        "hooks": {
                                                "SessionStart": [
                                                        {
                                                                "type": "command",
                                                                "command": "managed session-start",
                                                                "timeoutSec": 10,
                                                                "env": {
                                                                        "HCOONA_VSCODE_COPILOT_TELEGRAM_HOOK": "1"
                                                                }
                                                        }
                                                ],
                                                "UserPromptSubmit": [
                                                        {
                                                                "type": "command",
                                                                "command": "managed user-prompt-submit",
                                                                "timeoutSec": 10,
                                                                "env": {
                                                                        "HCOONA_VSCODE_COPILOT_TELEGRAM_HOOK": "1"
                                                                }
                                                        }
                                                ],
                                                "Stop": [
                                                        {
                                                                "type": "command",
                                                                "command": "managed stop",
                                                                "timeoutSec": 20,
                                                                "env": {
                                                                        "HCOONA_VSCODE_COPILOT_TELEGRAM_HOOK": "1"
                                                                }
                                                        }
                                                ]
                                        }
                }
                """);

            Assert.False(
                UserHookConfigurationManager.IsManagedCopilotCliHookFileInstalled(hookFilePath));
        }
        finally
        {
            tempDirectory.Delete(recursive: true);
        }
    }

    [Fact]
    public void InstallManagedHookFileDoesNotLeaveFinalHookFileWhenAtomicWriteFails()
    {
        DirectoryInfo tempDirectory = Directory.CreateTempSubdirectory();

        try
        {
            string hookFilePath = Path.Combine(
                tempDirectory.FullName,
                AppConstants.ManagedHookFileName);

            using IDisposable _ = AtomicTextFileWriter.UseWriterForTesting(
                new ThrowingTextFileWriter());

            Assert.Throws<IOException>(() =>
                UserHookConfigurationManager.InstallManagedHookFile(
                    hookFilePath,
                    "managed session-start",
                    "managed user-prompt-submit",
                    "managed stop",
                    "2026-03-13T12:34:56.789Z"));

            Assert.False(File.Exists(hookFilePath));
        }
        finally
        {
            tempDirectory.Delete(recursive: true);
        }
    }

    private static Dictionary<string, string> ReadDirectoryFileContents(string directoryPath)
    {
        return Directory.EnumerateFiles(directoryPath, "*", SearchOption.AllDirectories)
            .OrderBy(static filePath => filePath, StringComparer.Ordinal)
            .ToDictionary(
                filePath => Path.GetRelativePath(directoryPath, filePath),
                File.ReadAllText,
                StringComparer.Ordinal);
    }

    private static UserHookSettingsDocument ReadSettings(string settingsPath)
    {
        return JsonSerializer.Deserialize(
                File.ReadAllText(settingsPath),
                AppJsonSerializerContext.Default.UserHookSettingsDocument)
            ?? throw new InvalidOperationException("Expected a valid settings document.");
    }

    private sealed class ThrowingTextFileWriter : ITextFileWriter
    {
        public void WriteAllText(string path, string content)
            => throw new IOException("Simulated write failure.");
    }
}
