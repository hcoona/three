using System.Text.Json;

namespace Hcoona.VsCodeCopilotTelegramHook;

internal static class VsCodeSettingsManager
{
    private static readonly JsonSerializerOptions ReadOptions = new(
        AppJsonSerializerContext.Default.Options)
    {
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
    };

    private static readonly AppJsonSerializerContext ReadContext = new(ReadOptions);

    private static readonly JsonSerializerOptions WriteIndentedOptions = new(
        AppJsonSerializerContext.Default.Options)
    {
        WriteIndented = true,
    };

    private static readonly AppJsonSerializerContext WriteIndentedContext =
        new(WriteIndentedOptions);

    public static ConfigurationApplyResult RegisterHookFile(
        string settingsPath,
        string hookFilePath,
        string timestamp)
    {
        string normalizedHookFilePath = Path.GetFullPath(hookFilePath);
        VsCodeUserSettingsDocument desiredDocument = CreateDesiredDocument(normalizedHookFilePath);
        VsCodeUserSettingsDocument rootDocument;

        if (File.Exists(settingsPath))
        {
            try
            {
                rootDocument = JsonSerializer.Deserialize(
                        File.ReadAllText(settingsPath),
                        ReadContext.VsCodeUserSettingsDocument)
                    ?? throw new InvalidOperationException(
                        "The VS Code settings file must contain a JSON object.");
            }
            catch (Exception ex) when (
                ex is IOException or JsonException or InvalidOperationException
                    or UnauthorizedAccessException or NotSupportedException)
            {
                string? candidatePath = TryWriteCandidateFile(
                    settingsPath,
                    SerializeSettings(desiredDocument),
                    timestamp);
                return new ConfigurationApplyResult(
                    Applied: false,
                    Message:
                        $"The existing VS Code settings file could not be updated automatically: "
                        + ex.Message,
                    CandidatePath: candidatePath);
            }
        }
        else
        {
            rootDocument = new VsCodeUserSettingsDocument();
        }

        rootDocument.ChatHookFilesLocations ??=
            new Dictionary<string, bool>(StringComparer.Ordinal);
        rootDocument.ChatHookFilesLocations[normalizedHookFilePath] = true;

        string serializedSettings = SerializeSettings(rootDocument);
        try
        {
            AtomicTextFileWriter.WriteAllText(settingsPath, serializedSettings);
        }
        catch (Exception ex) when (
            ex is IOException or UnauthorizedAccessException or NotSupportedException)
        {
            string? candidatePath = TryWriteCandidateFile(
                settingsPath,
                serializedSettings,
                timestamp);
            return new ConfigurationApplyResult(
                Applied: false,
                Message:
                    $"The VS Code settings file could not be updated automatically: "
                    + ex.Message,
                CandidatePath: candidatePath);
        }

        return new ConfigurationApplyResult(
            true,
            $"Updated VS Code hook registration settings: {settingsPath}");
    }

    public static ConfigurationApplyResult UnregisterHookFile(
        string settingsPath,
        string hookFilePath,
        string timestamp)
    {
        if (!File.Exists(settingsPath))
        {
            return new ConfigurationApplyResult(
                true,
                "The VS Code settings file is already absent.");
        }

        VsCodeUserSettingsDocument? rootDocument = TryParseSettings(settingsPath);
        if (rootDocument is null)
        {
            return new ConfigurationApplyResult(
                false,
                "The VS Code settings file could not be parsed. Remove the managed hook "
                + "registration manually.");
        }

        string normalizedHookFilePath = Path.GetFullPath(hookFilePath);
        if (rootDocument.ChatHookFilesLocations is null
            || !rootDocument.ChatHookFilesLocations.Remove(normalizedHookFilePath))
        {
            return new ConfigurationApplyResult(
                true,
                "No managed VS Code hook registration was found.");
        }

        if (rootDocument.ChatHookFilesLocations.Count == 0)
        {
            rootDocument.ChatHookFilesLocations = null;
        }

        string serializedSettings = SerializeSettings(rootDocument);
        try
        {
            AtomicTextFileWriter.WriteAllText(settingsPath, serializedSettings);
        }
        catch (Exception ex) when (
            ex is IOException or UnauthorizedAccessException or NotSupportedException)
        {
            string? candidatePath = TryWriteCandidateFile(
                settingsPath,
                serializedSettings,
                timestamp);
            return new ConfigurationApplyResult(
                Applied: false,
                Message:
                    "The VS Code settings file could not be updated automatically while "
                    + $"removing the managed hook registration: {ex.Message}",
                CandidatePath: candidatePath);
        }

        return new ConfigurationApplyResult(
            true,
            $"Removed VS Code hook registration from: {settingsPath}");
    }

    public static bool IsHookFileRegistered(string settingsPath, string hookFilePath)
    {
        VsCodeUserSettingsDocument? rootDocument = TryParseSettings(settingsPath);
        if (rootDocument?.ChatHookFilesLocations is null)
        {
            return false;
        }

        string normalizedHookFilePath = Path.GetFullPath(hookFilePath);
        return rootDocument.ChatHookFilesLocations.TryGetValue(
                normalizedHookFilePath,
                out bool isEnabled)
            && isEnabled;
    }

    private static VsCodeUserSettingsDocument CreateDesiredDocument(string hookFilePath)
    {
        return new VsCodeUserSettingsDocument
        {
            ChatHookFilesLocations = new Dictionary<string, bool>(StringComparer.Ordinal)
            {
                [hookFilePath] = true,
            },
        };
    }

    private static VsCodeUserSettingsDocument? TryParseSettings(string path)
    {
        try
        {
            return JsonSerializer.Deserialize(
                File.ReadAllText(path),
                ReadContext.VsCodeUserSettingsDocument);
        }
        catch (Exception ex) when (
            ex is IOException or JsonException or UnauthorizedAccessException
                or NotSupportedException)
        {
            return null;
        }
    }

    private static string SerializeSettings(VsCodeUserSettingsDocument document)
    {
        return JsonSerializer.Serialize(document, WriteIndentedContext.VsCodeUserSettingsDocument);
    }

    private static string? TryWriteCandidateFile(
        string originalPath,
        string content,
        string timestamp)
    {
        try
        {
            return WriteCandidateFile(originalPath, content, timestamp);
        }
        catch (Exception ex) when (
            ex is IOException or UnauthorizedAccessException or NotSupportedException)
        {
            return null;
        }
    }

    private static string WriteCandidateFile(
        string originalPath,
        string content,
        string timestamp)
    {
        string candidatePath = BuildCandidatePath(originalPath, timestamp);
        EnsureParentDirectory(candidatePath);
        File.WriteAllText(candidatePath, content);
        return candidatePath;
    }

    private static string BuildCandidatePath(string originalPath, string timestamp)
    {
        string directoryPath = Path.GetDirectoryName(originalPath)
            ?? throw new InvalidOperationException(
                $"Cannot compute a candidate path for '{originalPath}'.");

        string fileName = Path.GetFileNameWithoutExtension(originalPath);
        string extension = Path.GetExtension(originalPath);
        string sanitizedTimestamp = timestamp.Replace(":", string.Empty, StringComparison.Ordinal);
        return Path.Combine(directoryPath, $"{fileName}.{sanitizedTimestamp}.candidate{extension}");
    }

    private static void EnsureParentDirectory(string path)
    {
        string? directoryPath = Path.GetDirectoryName(path);
        if (!string.IsNullOrWhiteSpace(directoryPath))
        {
            Directory.CreateDirectory(directoryPath);
        }
    }
}
