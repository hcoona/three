using System.Text.Json;

namespace Hcoona.VsCodeCopilotTelegramHook;

internal static class UserHookConfigurationManager
{
    private static readonly JsonSerializerOptions WriteIndentedOptions = new(
        AppJsonSerializerContext.Default.Options)
    {
        WriteIndented = true,
    };

    private static readonly AppJsonSerializerContext WriteIndentedContext =
        new(WriteIndentedOptions);

    public static ConfigurationApplyResult InstallManagedHookFile(
        string hookFilePath,
        string sessionStartCommand,
        string userPromptSubmitCommand,
        string stopCommand,
        string timestamp)
    {
        UserHookSettingsDocument desiredDocument = CreateManagedHooksDocument(
            sessionStartCommand,
            userPromptSubmitCommand,
            stopCommand);
        UserHookSettingsDocument rootDocument;

        if (File.Exists(hookFilePath))
        {
            try
            {
                rootDocument = JsonSerializer.Deserialize(
                        File.ReadAllText(hookFilePath),
                        AppJsonSerializerContext.Default.UserHookSettingsDocument)
                    ?? throw new InvalidOperationException(
                        "The managed hook file must contain a JSON object.");
            }
            catch (Exception ex) when (
                ex is IOException or JsonException or InvalidOperationException
                    or UnauthorizedAccessException or NotSupportedException)
            {
                string candidatePath = WriteCandidateFile(
                    hookFilePath,
                    SerializeSettings(desiredDocument),
                    timestamp);
                return new ConfigurationApplyResult(
                    Applied: false,
                    Message:
                        $"The existing managed hook file could not be updated automatically: "
                        + ex.Message,
                    CandidatePath: candidatePath);
            }
        }
        else
        {
            rootDocument = new UserHookSettingsDocument();
        }

        rootDocument.Hooks ??= new Dictionary<string, List<UserHookEntry>>(
            StringComparer.Ordinal);

        ConfigurationApplyResult? conflict = UpsertHookEntry(
            rootDocument.Hooks,
            eventName: "SessionStart",
            entry: CreateManagedHookEntry(
                sessionStartCommand,
                "SessionStart",
                timeoutSeconds: 10));

        if (conflict is not null)
        {
            return conflict;
        }

        conflict = UpsertHookEntry(
            rootDocument.Hooks,
            eventName: "UserPromptSubmit",
            entry: CreateManagedHookEntry(
                userPromptSubmitCommand,
                "UserPromptSubmit",
                timeoutSeconds: 10));

        if (conflict is not null)
        {
            return conflict;
        }

        conflict = UpsertHookEntry(
            rootDocument.Hooks,
            eventName: "Stop",
            entry: CreateManagedHookEntry(
                stopCommand,
                "Stop",
                timeoutSeconds: 20));

        if (conflict is not null)
        {
            return conflict;
        }

        EnsureParentDirectory(hookFilePath);
        File.WriteAllText(hookFilePath, SerializeSettings(rootDocument));

        return new ConfigurationApplyResult(true, $"Updated managed hook file: {hookFilePath}");
    }

    public static ConfigurationApplyResult UninstallManagedHookFile(string hookFilePath)
    {
        if (!File.Exists(hookFilePath))
        {
            return new ConfigurationApplyResult(
                true,
                "The managed hook file is already absent.");
        }

        UserHookSettingsDocument? rootDocument = TryParseSettings(hookFilePath);
        if (rootDocument is null)
        {
            return new ConfigurationApplyResult(
                false,
                "The managed hook file could not be parsed. Remove the managed "
                + "entries manually.");
        }

        if (rootDocument.Hooks.Count == 0)
        {
            return new ConfigurationApplyResult(true, "No managed hook entries were found.");
        }

        RemoveManagedEntries(rootDocument.Hooks, "SessionStart");
        RemoveManagedEntries(rootDocument.Hooks, "UserPromptSubmit");
        RemoveManagedEntries(rootDocument.Hooks, "Stop");

        if (CanDeleteManagedHookFile(rootDocument))
        {
            File.Delete(hookFilePath);
            return new ConfigurationApplyResult(
                true,
                $"Removed managed hook file: {hookFilePath}");
        }

        File.WriteAllText(hookFilePath, SerializeSettings(rootDocument));
        return new ConfigurationApplyResult(
            true,
            $"Removed managed hook entries from managed hook file: {hookFilePath}");
    }

    public static bool IsManagedHookFileInstalled(string hookFilePath)
    {
        UserHookSettingsDocument? rootDocument = TryParseSettings(hookFilePath);
        if (rootDocument is null)
        {
            return false;
        }

        return HasManagedEntry(rootDocument.Hooks, "SessionStart")
            && HasManagedEntry(rootDocument.Hooks, "UserPromptSubmit")
            && HasManagedEntry(rootDocument.Hooks, "Stop");
    }

    public static ConfigurationApplyResult InstallInstruction(
        string instructionPath,
        string content,
        string timestamp)
    {
        if (File.Exists(instructionPath))
        {
            string existingContent = File.ReadAllText(instructionPath);
            bool isManagedInstruction = existingContent.Contains(
                AppConstants.ManagedInstructionMarker,
                StringComparison.Ordinal);

            if (!isManagedInstruction
                && !string.Equals(existingContent, content, StringComparison.Ordinal))
            {
                string candidatePath = WriteCandidateFile(instructionPath, content, timestamp);
                return new ConfigurationApplyResult(
                    Applied: false,
                    Message:
                        $"The existing instruction file '{instructionPath}' is not managed by "
                        + "this tool.",
                    CandidatePath: candidatePath);
            }
        }

        EnsureParentDirectory(instructionPath);
        File.WriteAllText(instructionPath, content);
        return new ConfigurationApplyResult(
            true,
            $"Updated user instructions file: {instructionPath}");
    }

    public static ConfigurationApplyResult UninstallInstruction(string instructionPath)
    {
        if (!File.Exists(instructionPath))
        {
            return new ConfigurationApplyResult(
                true,
                "The managed instruction file is already absent.");
        }

        string existingContent = File.ReadAllText(instructionPath);
        if (!existingContent.Contains(
            AppConstants.ManagedInstructionMarker,
            StringComparison.Ordinal))
        {
            return new ConfigurationApplyResult(
                false,
                "The instruction file is not managed by this tool. Remove it manually if "
                + "desired.");
        }

        File.Delete(instructionPath);
        return new ConfigurationApplyResult(
            true,
            $"Removed managed instruction file: {instructionPath}");
    }

    public static bool IsInstructionInstalled(string instructionPath)
    {
        if (!File.Exists(instructionPath))
        {
            return false;
        }

        return File.ReadAllText(instructionPath).Contains(
            AppConstants.ManagedInstructionMarker,
            StringComparison.Ordinal);
    }

    private static ConfigurationApplyResult? UpsertHookEntry(
        Dictionary<string, List<UserHookEntry>> hooks,
        string eventName,
        UserHookEntry entry)
    {
        if (!hooks.TryGetValue(eventName, out List<UserHookEntry>? hookEntries)
            || hookEntries is null)
        {
            hookEntries = [];
            hooks[eventName] = hookEntries;
        }

        for (int index = hookEntries.Count - 1; index >= 0; index--)
        {
            if (IsManagedHookEntry(hookEntries[index]))
            {
                hookEntries.RemoveAt(index);
            }
        }

        hookEntries.Add(entry);
        return null;
    }

    private static UserHookSettingsDocument CreateManagedHooksDocument(
        string sessionStartCommand,
        string userPromptSubmitCommand,
        string stopCommand)
    {
        return new UserHookSettingsDocument
        {
            Hooks = new Dictionary<string, List<UserHookEntry>>(StringComparer.Ordinal)
            {
                ["SessionStart"] = [
                    CreateManagedHookEntry(sessionStartCommand, "SessionStart", 10)
                ],
                ["UserPromptSubmit"] = [
                    CreateManagedHookEntry(
                        userPromptSubmitCommand,
                        "UserPromptSubmit",
                        10)
                ],
                ["Stop"] = [CreateManagedHookEntry(stopCommand, "Stop", 20)],
            },
        };
    }

    private static UserHookEntry CreateManagedHookEntry(
        string command,
        string eventName,
        int timeoutSeconds)
    {
        return new UserHookEntry
        {
            Type = "command",
            Command = command,
            Timeout = timeoutSeconds,
            Env = new Dictionary<string, string>(StringComparer.Ordinal)
            {
                [AppConstants.ManagedHookEnvironmentVariable] =
                    AppConstants.ManagedHookEnvironmentValue,
                [AppConstants.ManagedHookEventEnvironmentVariable] = eventName,
            },
        };
    }

    private static void RemoveManagedEntries(
        Dictionary<string, List<UserHookEntry>> hooks,
        string eventName)
    {
        if (!hooks.TryGetValue(eventName, out List<UserHookEntry>? hookEntries)
            || hookEntries is null)
        {
            return;
        }

        for (int index = hookEntries.Count - 1; index >= 0; index--)
        {
            if (IsManagedHookEntry(hookEntries[index]))
            {
                hookEntries.RemoveAt(index);
            }
        }

        if (hookEntries.Count == 0)
        {
            hooks.Remove(eventName);
        }
    }

    private static bool HasManagedEntry(
        Dictionary<string, List<UserHookEntry>> hooks,
        string eventName)
    {
        if (!hooks.TryGetValue(eventName, out List<UserHookEntry>? hookEntries)
            || hookEntries is null)
        {
            return false;
        }

        return hookEntries.Any(IsManagedHookEntry);
    }

    private static bool IsManagedHookEntry(UserHookEntry? entry)
    {
        if (entry?.Env is null)
        {
            return false;
        }

        return entry.Env.TryGetValue(
            AppConstants.ManagedHookEnvironmentVariable,
            out string? managedValue)
            && string.Equals(
                managedValue,
                AppConstants.ManagedHookEnvironmentValue,
                StringComparison.Ordinal);
    }

    private static bool CanDeleteManagedHookFile(UserHookSettingsDocument document)
    {
        bool hasAdditionalProperties = document.AdditionalProperties is { Count: > 0 };
        return document.Hooks.Count == 0 && !hasAdditionalProperties;
    }

    private static UserHookSettingsDocument? TryParseSettings(string path)
    {
        try
        {
            return JsonSerializer.Deserialize(
                File.ReadAllText(path),
                AppJsonSerializerContext.Default.UserHookSettingsDocument);
        }
        catch (Exception ex) when (
            ex is IOException or JsonException or UnauthorizedAccessException
                or NotSupportedException)
        {
            return null;
        }
    }

    private static string SerializeSettings(UserHookSettingsDocument document)
    {
        return JsonSerializer.Serialize(document, WriteIndentedContext.UserHookSettingsDocument);
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
