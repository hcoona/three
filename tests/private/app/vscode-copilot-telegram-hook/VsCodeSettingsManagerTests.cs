using System.Text.Json;
using Xunit;

namespace Hcoona.VsCodeCopilotTelegramHook.Tests;

public sealed class VsCodeSettingsManagerTests
{
    [Fact]
    public void RegisterHookFilePreservesUnrelatedSettingsAndUnregisterRemovesOnlyManagedEntry()
    {
        DirectoryInfo tempDirectory = Directory.CreateTempSubdirectory();

        try
        {
            string settingsPath = Path.Combine(tempDirectory.FullName, "settings.json");
            string managedHookFilePath = Path.Combine(
                tempDirectory.FullName,
                AppConstants.ManagedHookFileName);
            string unrelatedHookFilePath = Path.Combine(tempDirectory.FullName, "other-hook.json");

            File.WriteAllText(
                settingsPath,
                $$"""
                {
                    "editor.fontSize": 14,
                    "{{AppConstants.ChatHookFilesLocationsSettingName}}": {
                        "{{unrelatedHookFilePath}}": false
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
            Assert.True(installedSettings.ChatHookFilesLocations![managedHookFilePath]);
            Assert.False(installedSettings.ChatHookFilesLocations[unrelatedHookFilePath]);

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
                uninstalledSettings.ChatHookFilesLocations!.ContainsKey(managedHookFilePath));
            Assert.False(uninstalledSettings.ChatHookFilesLocations[unrelatedHookFilePath]);
        }
        finally
        {
            tempDirectory.Delete(recursive: true);
        }
    }

    [Fact]
    public void RegisterHookFileSupportsJsoncSettingsFiles()
    {
        DirectoryInfo tempDirectory = Directory.CreateTempSubdirectory();

        try
        {
            string settingsPath = Path.Combine(tempDirectory.FullName, "settings.json");
            string managedHookFilePath = Path.Combine(
                tempDirectory.FullName,
                AppConstants.ManagedHookFileName);
            string unrelatedHookFilePath = Path.Combine(tempDirectory.FullName, "other-hook.json");

            File.WriteAllText(
                settingsPath,
                $$"""
                {
                    // VS Code stores settings in JSONC.
                    "editor.fontSize": 14,
                    "{{AppConstants.ChatHookFilesLocationsSettingName}}": {
                        "{{unrelatedHookFilePath}}": false,
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
            Assert.False(settings.ChatHookFilesLocations![unrelatedHookFilePath]);
        }
        finally
        {
            tempDirectory.Delete(recursive: true);
        }
    }

    [Fact]
    public void RegisterHookFileCreatesCandidateWhenExistingSettingsFileIsInvalid()
    {
        DirectoryInfo tempDirectory = Directory.CreateTempSubdirectory();

        try
        {
            string settingsPath = Path.Combine(tempDirectory.FullName, "settings.json");
            string managedHookFilePath = Path.Combine(
                tempDirectory.FullName,
                AppConstants.ManagedHookFileName);
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
            Assert.True(candidateSettings.ChatHookFilesLocations![managedHookFilePath]);
        }
        finally
        {
            tempDirectory.Delete(recursive: true);
        }
    }

    [Fact]
    public void RegisterHookFilePreservesOriginalSettingsWhenWriteFails()
    {
        DirectoryInfo tempDirectory = Directory.CreateTempSubdirectory();

        try
        {
            string settingsPath = Path.Combine(tempDirectory.FullName, "settings.json");
            string managedHookFilePath = Path.Combine(
                tempDirectory.FullName,
                AppConstants.ManagedHookFileName);
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
            Assert.True(candidateSettings.ChatHookFilesLocations![managedHookFilePath]);
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

    private sealed class ThrowingTextFileWriter : ITextFileWriter
    {
        public void WriteAllText(string path, string content)
            => throw new IOException("Simulated write failure.");
    }
}
