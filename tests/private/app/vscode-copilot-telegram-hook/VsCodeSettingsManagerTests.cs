using System.Text.Json;
using Xunit;

namespace Hcoona.VsCodeCopilotTelegramHook.Tests;

public sealed class VsCodeSettingsManagerTests
{
    [Fact]
    public void RegisterHookFilePreservesUnrelatedSettingsAndUnregisterRemovesOnlyManagedEntry()
    {
        DirectoryInfo tempDirectory = CreateHomeScopedTempSubdirectory();

        try
        {
            string settingsPath = Path.Combine(tempDirectory.FullName, "settings.json");
            string managedHookFilePath = Path.Combine(
                tempDirectory.FullName,
                AppConstants.ManagedHookFileName);
            string managedHookFileLocation =
                VsCodeSettingsManager.GetSupportedHookFileLocation(managedHookFilePath);
            string unrelatedHookFileLocation = "~/other-hook.json";

            File.WriteAllText(
                settingsPath,
                $$"""
                {
                    "editor.fontSize": 14,
                    "{{AppConstants.ChatHookFilesLocationsSettingName}}": {
                        "{{unrelatedHookFileLocation}}": false
                    }
                }
                """);

            ConfigurationApplyResult installResult = VsCodeSettingsManager.RegisterHookFile(
                settingsPath,
                managedHookFilePath,
                "2026-03-13T12:34:56.789Z");

            Assert.True(installResult.Applied);
            Assert.True(
                VsCodeSettingsManager.IsHookFileRegistered(
                    settingsPath,
                    managedHookFilePath));

            VsCodeUserSettingsDocument installedSettings = ReadSettings(settingsPath);
            Assert.Equal(14, installedSettings.AdditionalProperties?["editor.fontSize"].GetInt32());
            Assert.NotNull(installedSettings.ChatHookFilesLocations);
            Assert.True(installedSettings.ChatHookFilesLocations![managedHookFileLocation]);
            Assert.False(installedSettings.ChatHookFilesLocations[unrelatedHookFileLocation]);

            ConfigurationApplyResult uninstallResult = VsCodeSettingsManager.UnregisterHookFile(
                settingsPath,
                managedHookFilePath,
                "2026-03-13T12:34:57.789Z");

            Assert.True(uninstallResult.Applied);
            Assert.False(
                VsCodeSettingsManager.IsHookFileRegistered(
                    settingsPath,
                    managedHookFilePath));

            VsCodeUserSettingsDocument uninstalledSettings = ReadSettings(settingsPath);
            Assert.NotNull(uninstalledSettings.ChatHookFilesLocations);
            Assert.False(
                uninstalledSettings.ChatHookFilesLocations!.ContainsKey(managedHookFileLocation));
            Assert.False(uninstalledSettings.ChatHookFilesLocations[unrelatedHookFileLocation]);
        }
        finally
        {
            tempDirectory.Delete(recursive: true);
        }
    }

    [Fact]
    public void RegisterHookFileSupportsJsoncSettingsFiles()
    {
        DirectoryInfo tempDirectory = CreateHomeScopedTempSubdirectory();

        try
        {
            string settingsPath = Path.Combine(tempDirectory.FullName, "settings.json");
            string managedHookFilePath = Path.Combine(
                tempDirectory.FullName,
                AppConstants.ManagedHookFileName);
            string managedHookFileLocation =
                VsCodeSettingsManager.GetSupportedHookFileLocation(managedHookFilePath);
            string unrelatedHookFileLocation = "~/other-hook.json";

            File.WriteAllText(
                settingsPath,
                $$"""
                {
                    // VS Code stores settings in JSONC.
                    "editor.fontSize": 14,
                    "{{AppConstants.ChatHookFilesLocationsSettingName}}": {
                        "{{unrelatedHookFileLocation}}": false,
                    },
                }
                """);

            ConfigurationApplyResult installResult = VsCodeSettingsManager.RegisterHookFile(
                settingsPath,
                managedHookFilePath,
                "2026-03-13T12:34:56.789Z");

            Assert.True(installResult.Applied);
            Assert.True(
                VsCodeSettingsManager.IsHookFileRegistered(
                    settingsPath,
                    managedHookFilePath));

            ConfigurationApplyResult uninstallResult = VsCodeSettingsManager.UnregisterHookFile(
                settingsPath,
                managedHookFilePath,
                "2026-03-13T12:34:57.789Z");

            Assert.True(uninstallResult.Applied);
            Assert.False(
                VsCodeSettingsManager.IsHookFileRegistered(
                    settingsPath,
                    managedHookFilePath));

            VsCodeUserSettingsDocument settings = ReadSettings(settingsPath);
            Assert.Equal(14, settings.AdditionalProperties?["editor.fontSize"].GetInt32());
            Assert.NotNull(settings.ChatHookFilesLocations);
            Assert.False(settings.ChatHookFilesLocations!.ContainsKey(managedHookFileLocation));
            Assert.False(settings.ChatHookFilesLocations[unrelatedHookFileLocation]);
        }
        finally
        {
            tempDirectory.Delete(recursive: true);
        }
    }

    [Fact]
    public void RegisterHookFileCreatesCandidateWhenExistingSettingsFileIsInvalid()
    {
        DirectoryInfo tempDirectory = CreateHomeScopedTempSubdirectory();

        try
        {
            string settingsPath = Path.Combine(tempDirectory.FullName, "settings.json");
            string managedHookFilePath = Path.Combine(
                tempDirectory.FullName,
                AppConstants.ManagedHookFileName);
            string managedHookFileLocation =
                VsCodeSettingsManager.GetSupportedHookFileLocation(managedHookFilePath);
            File.WriteAllText(settingsPath, "{ invalid json");

            ConfigurationApplyResult result = VsCodeSettingsManager.RegisterHookFile(
                settingsPath,
                managedHookFilePath,
                "2026-03-13T12:34:56.789Z");

            Assert.False(result.Applied);
            Assert.NotNull(result.CandidatePath);
            Assert.True(File.Exists(result.CandidatePath));

            VsCodeUserSettingsDocument candidateSettings = ReadSettings(result.CandidatePath);
            Assert.NotNull(candidateSettings.ChatHookFilesLocations);
            Assert.True(candidateSettings.ChatHookFilesLocations![managedHookFileLocation]);
        }
        finally
        {
            tempDirectory.Delete(recursive: true);
        }
    }

    [Fact]
    public void RegisterHookFilePreservesOriginalSettingsWhenWriteFails()
    {
        DirectoryInfo tempDirectory = CreateHomeScopedTempSubdirectory();

        try
        {
            string settingsPath = Path.Combine(tempDirectory.FullName, "settings.json");
            string managedHookFilePath = Path.Combine(
                tempDirectory.FullName,
                AppConstants.ManagedHookFileName);
            string managedHookFileLocation =
                VsCodeSettingsManager.GetSupportedHookFileLocation(managedHookFilePath);
            File.WriteAllText(settingsPath, """{ "editor.fontSize": 14 }""");
            string originalContent = File.ReadAllText(settingsPath);

            using IDisposable _ = AtomicTextFileWriter.UseWriterForTesting(
                new ThrowingTextFileWriter());

            ConfigurationApplyResult result = VsCodeSettingsManager.RegisterHookFile(
                settingsPath,
                managedHookFilePath,
                "2026-03-13T12:34:56.789Z");

            Assert.False(result.Applied);
            Assert.NotNull(result.CandidatePath);
            Assert.Equal(originalContent, File.ReadAllText(settingsPath));

            VsCodeUserSettingsDocument candidateSettings = ReadSettings(result.CandidatePath!);
            Assert.NotNull(candidateSettings.ChatHookFilesLocations);
            Assert.True(candidateSettings.ChatHookFilesLocations![managedHookFileLocation]);
        }
        finally
        {
            tempDirectory.Delete(recursive: true);
        }
    }

    [Fact]
    public void ApplyWritePlanFailsWhenSettingsFileChangesAfterPlanning()
    {
        DirectoryInfo tempDirectory = CreateHomeScopedTempSubdirectory();

        try
        {
            string settingsPath = Path.Combine(tempDirectory.FullName, "settings.json");
            string managedHookFilePath = Path.Combine(
                tempDirectory.FullName,
                AppConstants.ManagedHookFileName);
            string managedHookFileLocation =
                VsCodeSettingsManager.GetSupportedHookFileLocation(managedHookFilePath);
            File.WriteAllText(settingsPath, """{ "editor.fontSize": 14 }""");

            ConfigurationPlanResult plan = VsCodeSettingsManager.PlanRegisterHookFile(
                settingsPath,
                managedHookFilePath,
                "2026-03-15T04:40:00.000Z");

            VsCodeSettingsWritePlan writePlan =
                Assert.IsType<VsCodeSettingsWritePlan>(plan.WritePlan);
            File.WriteAllText(settingsPath, """{ "editor.fontSize": 16 }""");

            ConfigurationApplyResult result = VsCodeSettingsManager.ApplyWritePlan(
                writePlan,
                "2026-03-15T04:40:01.000Z");

            Assert.False(result.Applied);
            Assert.NotNull(result.CandidatePath);
            Assert.Equal("""{ "editor.fontSize": 16 }""", File.ReadAllText(settingsPath));

            VsCodeUserSettingsDocument candidateSettings = ReadSettings(result.CandidatePath!);
            Assert.NotNull(candidateSettings.ChatHookFilesLocations);
            Assert.True(candidateSettings.ChatHookFilesLocations![managedHookFileLocation]);
        }
        finally
        {
            tempDirectory.Delete(recursive: true);
        }
    }

    [Fact]
    public void RegisterHookFileReplacesLegacyAbsoluteLocationWithSupportedHomeRelativeLocation()
    {
        DirectoryInfo tempDirectory = CreateHomeScopedTempSubdirectory();

        try
        {
            string settingsPath = Path.Combine(tempDirectory.FullName, "settings.json");
            string managedHookFilePath = Path.Combine(
                tempDirectory.FullName,
                AppConstants.ManagedHookFileName);
            string managedHookFileLocation =
                VsCodeSettingsManager.GetSupportedHookFileLocation(managedHookFilePath);
            string legacyAbsoluteHookFileLocation = Path.GetFullPath(managedHookFilePath);

            File.WriteAllText(
                settingsPath,
                JsonSerializer.Serialize(
                    new Dictionary<string, Dictionary<string, bool>>
                    {
                        [AppConstants.ChatHookFilesLocationsSettingName] = new()
                        {
                            [legacyAbsoluteHookFileLocation] = true,
                        },
                    }));

            ConfigurationApplyResult result = VsCodeSettingsManager.RegisterHookFile(
                settingsPath,
                managedHookFilePath,
                "2026-03-13T12:34:56.789Z");

            Assert.True(result.Applied);

            VsCodeUserSettingsDocument settings = ReadSettings(settingsPath);
            Assert.NotNull(settings.ChatHookFilesLocations);
            Assert.False(
                settings.ChatHookFilesLocations!.ContainsKey(legacyAbsoluteHookFileLocation));
            Assert.True(settings.ChatHookFilesLocations[managedHookFileLocation]);
        }
        finally
        {
            tempDirectory.Delete(recursive: true);
        }
    }

    [Fact]
    public void PlanRegisterHookFileRejectsManagedHookPathOutsideHomeDirectory()
    {
        DirectoryInfo tempDirectory = CreateHomeScopedTempSubdirectory();
        string outsideHomeHookFilePath = CreatePathOutsideHomeDirectory();

        try
        {
            string settingsPath = Path.Combine(tempDirectory.FullName, "settings.json");

            ConfigurationPlanResult result = VsCodeSettingsManager.PlanRegisterHookFile(
                settingsPath,
                outsideHomeHookFilePath,
                "2026-03-13T12:34:56.789Z");

            Assert.False(result.Applied);
            Assert.Null(result.WritePlan);
            Assert.Contains("outside the current user's home directory", result.Message);
            Assert.False(File.Exists(settingsPath));
        }
        finally
        {
            tempDirectory.Delete(recursive: true);
        }
    }

    [Fact]
    public void ApplyWritePlanAndRollbackRestoreAbsentSettingsFile()
    {
        DirectoryInfo tempDirectory = CreateHomeScopedTempSubdirectory();

        try
        {
            string settingsPath = Path.Combine(tempDirectory.FullName, "settings.json");
            string managedHookFilePath = Path.Combine(
                tempDirectory.FullName,
                AppConstants.ManagedHookFileName);
            ConfigurationPlanResult plan = VsCodeSettingsManager.PlanRegisterHookFile(
                settingsPath,
                managedHookFilePath,
                "2026-03-13T12:34:56.789Z");

            Assert.True(plan.Applied);
            VsCodeSettingsWritePlan writePlan =
                Assert.IsType<VsCodeSettingsWritePlan>(plan.WritePlan);

            ConfigurationApplyResult applyResult = VsCodeSettingsManager.ApplyWritePlan(
                writePlan,
                "2026-03-13T12:34:56.789Z");
            Assert.True(applyResult.Applied);
            Assert.True(File.Exists(settingsPath));

            ConfigurationApplyResult rollbackResult = VsCodeSettingsManager.RollbackWritePlan(
                writePlan);
            Assert.True(rollbackResult.Applied);
            Assert.False(File.Exists(settingsPath));
        }
        finally
        {
            tempDirectory.Delete(recursive: true);
        }
    }

    [Fact]
    public void RollbackWritePlanFailsWhenSettingsFileChangesAfterManagedWrite()
    {
        DirectoryInfo tempDirectory = CreateHomeScopedTempSubdirectory();

        try
        {
            string settingsPath = Path.Combine(tempDirectory.FullName, "settings.json");
            string managedHookFilePath = Path.Combine(
                tempDirectory.FullName,
                AppConstants.ManagedHookFileName);
            File.WriteAllText(settingsPath, """{ "editor.fontSize": 14 }""");

            ConfigurationPlanResult plan = VsCodeSettingsManager.PlanRegisterHookFile(
                settingsPath,
                managedHookFilePath,
                "2026-03-15T04:41:00.000Z");

            VsCodeSettingsWritePlan writePlan =
                Assert.IsType<VsCodeSettingsWritePlan>(plan.WritePlan);
            ConfigurationApplyResult applyResult = VsCodeSettingsManager.ApplyWritePlan(
                writePlan,
                "2026-03-15T04:41:01.000Z");
            Assert.True(applyResult.Applied);

            File.WriteAllText(settingsPath, """{ "editor.fontSize": 18 }""");

            ConfigurationApplyResult rollbackResult = VsCodeSettingsManager.RollbackWritePlan(
                writePlan);

            Assert.False(rollbackResult.Applied);
            Assert.Equal("""{ "editor.fontSize": 18 }""", File.ReadAllText(settingsPath));
        }
        finally
        {
            tempDirectory.Delete(recursive: true);
        }
    }

    [Fact]
    public void PlanUnregisterHookFileFailsWhenSettingsCannotBeParsed()
    {
        DirectoryInfo tempDirectory = CreateHomeScopedTempSubdirectory();

        try
        {
            string settingsPath = Path.Combine(tempDirectory.FullName, "settings.json");
            string managedHookFilePath = Path.Combine(
                tempDirectory.FullName,
                AppConstants.ManagedHookFileName);
            File.WriteAllText(settingsPath, "{ invalid json");

            ConfigurationPlanResult plan = VsCodeSettingsManager.PlanUnregisterHookFile(
                settingsPath,
                managedHookFilePath,
                "2026-03-13T12:34:56.789Z");

            Assert.False(plan.Applied);
            Assert.Null(plan.WritePlan);
            Assert.Contains("could not be parsed", plan.Message);
        }
        finally
        {
            tempDirectory.Delete(recursive: true);
        }
    }

    private static VsCodeUserSettingsDocument ReadSettings(string settingsPath)
    {
        return JsonSerializer.Deserialize(
                File.ReadAllText(settingsPath),
                AppJsonSerializerContext.Default.VsCodeUserSettingsDocument)
            ?? throw new InvalidOperationException("Expected a valid VS Code settings document.");
    }

    private static DirectoryInfo CreateHomeScopedTempSubdirectory()
    {
        string path = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
            ".tmp",
            "hcoona-vscode-copilot-telegram-hook-tests",
            Guid.NewGuid().ToString("n"));
        return Directory.CreateDirectory(path);
    }

    private static string CreatePathOutsideHomeDirectory()
    {
        string userHomePath = Path.GetFullPath(
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile));
        string rootPath = Path.GetPathRoot(userHomePath)
            ?? throw new InvalidOperationException("Expected a root path.");
        string outsideHomePath = Path.Combine(
            rootPath,
            "hcoona-outside-home",
            Guid.NewGuid().ToString("n"),
            AppConstants.ManagedHookFileName);
        return Path.GetFullPath(outsideHomePath);
    }

    private sealed class ThrowingTextFileWriter : ITextFileWriter
    {
        public void WriteAllText(string path, string content)
            => throw new IOException("Simulated write failure.");
    }
}
