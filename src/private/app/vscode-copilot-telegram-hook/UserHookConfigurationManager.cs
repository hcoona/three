using System.Text.Json;

namespace Hcoona.VsCodeCopilotTelegramHook;

internal static class UserHookConfigurationManager
{
    private const int SessionStartTimeoutSeconds = 10;
    private const int UserPromptSubmitTimeoutSeconds = 10;
    private const int PreToolUseTimeoutSeconds = 20;
    private const int StopTimeoutSeconds = 20;

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
        string preToolUseCommand,
        string stopCommand,
        string timestamp)
        => InstallManagedHookFileCore(
            hookFilePath,
            sessionStartCommand,
            userPromptSubmitCommand,
            preToolUseCommand,
            stopCommand,
            timestamp,
            HookFileFormat.VsCode);

    public static ConfigurationApplyResult InstallManagedCopilotCliHookFile(
        string hookFilePath,
        string sessionStartCommand,
        string userPromptSubmitCommand,
        string stopCommand,
        string timestamp)
        => InstallManagedHookFileCore(
            hookFilePath,
            sessionStartCommand,
            userPromptSubmitCommand,
            preToolUseCommand: null,
            stopCommand,
            timestamp,
            HookFileFormat.CopilotCli);

    public static ConfigurationApplyResult? PreflightManagedCopilotCliHookFile(
        string hookFilePath,
        string sessionStartCommand,
        string userPromptSubmitCommand,
        string stopCommand,
        string timestamp)
    {
        UserHookSettingsDocument desiredDocument = CreateManagedHooksDocument(
            sessionStartCommand,
            userPromptSubmitCommand,
            preToolUseCommand: null,
            stopCommand,
            HookFileFormat.CopilotCli);
        return TryLoadExistingHookFileForInstall(
            hookFilePath,
            desiredDocument,
            timestamp,
            HookFileFormat.CopilotCli,
            writeCandidateFileOnFailure: false,
            out _);
    }

    private static ConfigurationApplyResult InstallManagedHookFileCore(
        string hookFilePath,
        string sessionStartCommand,
        string userPromptSubmitCommand,
        string? preToolUseCommand,
        string stopCommand,
        string timestamp,
        HookFileFormat format)
    {
        UserHookSettingsDocument desiredDocument = CreateManagedHooksDocument(
            sessionStartCommand,
            userPromptSubmitCommand,
            preToolUseCommand,
            stopCommand,
            format);
        UserHookSettingsDocument rootDocument;

        ConfigurationApplyResult? existingFileResult = TryLoadExistingHookFileForInstall(
            hookFilePath,
            desiredDocument,
            timestamp,
            format,
            writeCandidateFileOnFailure: true,
            out rootDocument);
        if (existingFileResult is not null)
        {
            return existingFileResult;
        }

        rootDocument.Hooks ??= new Dictionary<string, List<UserHookEntry>>(
            StringComparer.Ordinal);

        if (format == HookFileFormat.CopilotCli)
        {
            rootDocument.Version = 1;
        }

        Predicate<UserHookEntry?> isManagedEntryToReplace = format == HookFileFormat.CopilotCli
            ? IsManagedCopilotCliHookEntryForRemoval
            : IsManagedHookEntry;

        ConfigurationApplyResult? conflict = UpsertHookEntry(
            rootDocument.Hooks,
            eventName: "SessionStart",
            entry: CreateManagedHookEntry(
                sessionStartCommand,
                "SessionStart",
                timeoutSeconds: SessionStartTimeoutSeconds,
                format),
            isManagedEntryToReplace);

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
                timeoutSeconds: UserPromptSubmitTimeoutSeconds,
                format),
            isManagedEntryToReplace);

        if (conflict is not null)
        {
            return conflict;
        }

        if (!string.IsNullOrWhiteSpace(preToolUseCommand))
        {
            conflict = UpsertHookEntry(
                rootDocument.Hooks,
                eventName: "PreToolUse",
                entry: CreateManagedHookEntry(
                    preToolUseCommand,
                    "PreToolUse",
                    timeoutSeconds: PreToolUseTimeoutSeconds,
                    format),
                isManagedEntryToReplace);

            if (conflict is not null)
            {
                return conflict;
            }
        }

        conflict = UpsertHookEntry(
            rootDocument.Hooks,
            eventName: "Stop",
            entry: CreateManagedHookEntry(
                stopCommand,
                "Stop",
                timeoutSeconds: StopTimeoutSeconds,
                format),
            isManagedEntryToReplace);

        if (conflict is not null)
        {
            return conflict;
        }

        EnsureParentDirectory(hookFilePath);
        AtomicTextFileWriter.WriteAllText(hookFilePath, SerializeSettings(rootDocument));

        return new ConfigurationApplyResult(true, $"Updated managed hook file: {hookFilePath}");
    }

    public static ConfigurationApplyResult UninstallManagedHookFile(string hookFilePath)
        => UninstallManagedHookFileCore(
            hookFilePath,
            RemoveManagedEntries,
            HookFileFormat.VsCode);

    public static ConfigurationApplyResult UninstallManagedCopilotCliHookFile(string hookFilePath)
        => UninstallManagedHookFileCore(
            hookFilePath,
            RemoveManagedCopilotCliEntries,
            HookFileFormat.CopilotCli);

    private static ConfigurationApplyResult UninstallManagedHookFileCore(
        string hookFilePath,
        Func<Dictionary<string, List<UserHookEntry>>, string, int> removeManagedEntries,
        HookFileFormat format)
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

        if (format == HookFileFormat.CopilotCli && rootDocument.Version != 1)
        {
            return new ConfigurationApplyResult(
                false,
                "The Copilot CLI hook file uses an unsupported schema version. "
                + "Manual review is required before removing managed entries; no changes "
                + "were applied.");
        }

        if (rootDocument.Hooks is null || rootDocument.Hooks.Count == 0)
        {
            return new ConfigurationApplyResult(true, "No managed hook entries were found.");
        }

        int removedEntryCount = 0;
        if (format == HookFileFormat.CopilotCli)
        {
            removedEntryCount += RemoveManagedCopilotCliEntriesFromAllEvents(rootDocument.Hooks);
        }
        else
        {
            removedEntryCount += removeManagedEntries(rootDocument.Hooks, "SessionStart");
            removedEntryCount += removeManagedEntries(rootDocument.Hooks, "UserPromptSubmit");
            removedEntryCount += removeManagedEntries(rootDocument.Hooks, "PreToolUse");
            removedEntryCount += removeManagedEntries(rootDocument.Hooks, "Stop");
        }

        if (removedEntryCount == 0)
        {
            return new ConfigurationApplyResult(true, "No managed hook entries were found.");
        }

        if (CanDeleteManagedHookFile(rootDocument, format))
        {
            File.Delete(hookFilePath);
            return new ConfigurationApplyResult(
                true,
                $"Removed managed hook file: {hookFilePath}");
        }

        AtomicTextFileWriter.WriteAllText(hookFilePath, SerializeSettings(rootDocument));
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

        return rootDocument.Hooks is not null
            && HasManagedEntry(rootDocument.Hooks, "SessionStart")
            && HasManagedEntry(rootDocument.Hooks, "UserPromptSubmit")
            && HasManagedEntry(rootDocument.Hooks, "PreToolUse")
            && HasManagedEntry(rootDocument.Hooks, "Stop");
    }

    public static bool IsManagedCopilotCliHookFileInstalled(string hookFilePath)
    {
        UserHookSettingsDocument? rootDocument = TryParseSettings(hookFilePath);
        if (rootDocument is null)
        {
            return false;
        }

        return rootDocument.Version == 1
            && rootDocument.Hooks is not null
            && HasManagedCopilotCliEntry(rootDocument.Hooks, "SessionStart")
            && HasManagedCopilotCliEntry(rootDocument.Hooks, "UserPromptSubmit")
            && HasManagedCopilotCliEntry(rootDocument.Hooks, "Stop");
    }

    public static bool IsManagedCopilotCliHookFileInstalled(
        string hookFilePath,
        string installedBinaryPath)
    {
        UserHookSettingsDocument? rootDocument = TryParseSettings(hookFilePath);
        if (rootDocument is null)
        {
            return false;
        }

        return rootDocument.Version == 1
            && rootDocument.Hooks is not null
            && HasStrictManagedCopilotCliEntry(
                rootDocument.Hooks,
                eventName: "SessionStart",
                command: CreateCopilotCliHookCommand(installedBinaryPath, "session-start"),
                timeoutSeconds: SessionStartTimeoutSeconds)
            && HasStrictManagedCopilotCliEntry(
                rootDocument.Hooks,
                eventName: "UserPromptSubmit",
                command: CreateCopilotCliHookCommand(installedBinaryPath, "user-prompt-submit"),
                timeoutSeconds: UserPromptSubmitTimeoutSeconds)
            && HasStrictManagedCopilotCliEntry(
                rootDocument.Hooks,
                eventName: "Stop",
                command: CreateCopilotCliHookCommand(installedBinaryPath, "stop"),
                timeoutSeconds: StopTimeoutSeconds);
    }

    public static string CreateCopilotCliHookCommand(
        string installedBinaryPath,
        string subcommand)
        => $"\"{installedBinaryPath}\" hook {subcommand}";

    private static ConfigurationApplyResult? TryLoadExistingHookFileForInstall(
        string hookFilePath,
        UserHookSettingsDocument desiredDocument,
        string timestamp,
        HookFileFormat format,
        bool writeCandidateFileOnFailure,
        out UserHookSettingsDocument rootDocument)
    {
        if (!File.Exists(hookFilePath))
        {
            rootDocument = new UserHookSettingsDocument();
            return null;
        }

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
            string? candidatePath = writeCandidateFileOnFailure
                ? WriteCandidateFile(
                    hookFilePath,
                    SerializeSettings(desiredDocument),
                    timestamp)
                : null;
            string actionMessage = writeCandidateFileOnFailure
                ? string.Empty
                : " Manual review is required; no changes were applied.";
            rootDocument = new UserHookSettingsDocument();
            return new ConfigurationApplyResult(
                Applied: false,
                Message:
                    $"The existing managed hook file could not be updated automatically: "
                    + ex.Message
                    + actionMessage,
                CandidatePath: candidatePath);
        }

        if (format == HookFileFormat.CopilotCli && rootDocument.Version != 1)
        {
            string? candidatePath = writeCandidateFileOnFailure
                ? WriteCandidateFile(
                    hookFilePath,
                    SerializeSettings(desiredDocument),
                    timestamp)
                : null;
            string actionMessage = writeCandidateFileOnFailure
                ? "Write the candidate file manually after reviewing it."
                : "Manual review is required; no changes were applied.";
            return new ConfigurationApplyResult(
                Applied: false,
                Message:
                    "The existing Copilot CLI hook file uses an unsupported schema "
                    + $"version. {actionMessage}",
                CandidatePath: candidatePath);
        }

        return null;
    }

    private static ConfigurationApplyResult? UpsertHookEntry(
        Dictionary<string, List<UserHookEntry>> hooks,
        string eventName,
        UserHookEntry entry,
        Predicate<UserHookEntry?> isManagedEntryToReplace)
    {
        if (!hooks.TryGetValue(eventName, out List<UserHookEntry>? hookEntries)
            || hookEntries is null)
        {
            hookEntries = [];
            hooks[eventName] = hookEntries;
        }

        for (int index = hookEntries.Count - 1; index >= 0; index--)
        {
            if (isManagedEntryToReplace(hookEntries[index]))
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
        string? preToolUseCommand,
        string stopCommand,
        HookFileFormat format)
    {
        Dictionary<string, List<UserHookEntry>> hooks = new(StringComparer.Ordinal)
        {
            ["SessionStart"] = [
                CreateManagedHookEntry(
                    sessionStartCommand,
                    "SessionStart",
                    SessionStartTimeoutSeconds,
                    format)
            ],
            ["UserPromptSubmit"] = [
                CreateManagedHookEntry(
                    userPromptSubmitCommand,
                    "UserPromptSubmit",
                    UserPromptSubmitTimeoutSeconds,
                    format)
            ],
            ["Stop"] = [
                CreateManagedHookEntry(
                    stopCommand,
                    "Stop",
                    StopTimeoutSeconds,
                    format)
            ],
        };

        if (!string.IsNullOrWhiteSpace(preToolUseCommand))
        {
            hooks["PreToolUse"] = [
                CreateManagedHookEntry(
                    preToolUseCommand,
                    "PreToolUse",
                    PreToolUseTimeoutSeconds,
                    format)
            ];
        }

        return new UserHookSettingsDocument
        {
            Version = format == HookFileFormat.CopilotCli ? 1 : null,
            Hooks = hooks,
        };
    }

    private static UserHookEntry CreateManagedHookEntry(
        string command,
        string eventName,
        int timeoutSeconds,
        HookFileFormat format)
    {
        UserHookEntry entry = new()
        {
            Type = "command",
            Command = command,
            Env = new Dictionary<string, string>(StringComparer.Ordinal)
            {
                [AppConstants.ManagedHookEnvironmentVariable] =
                    AppConstants.ManagedHookEnvironmentValue,
                [AppConstants.ManagedHookEventEnvironmentVariable] = eventName,
            },
        };

        if (format == HookFileFormat.CopilotCli)
        {
            entry.TimeoutSec = timeoutSeconds;
            entry.Env[AppConstants.ManagedHookSurfaceEnvironmentVariable] =
                AppConstants.ManagedHookCopilotCliSurfaceValue;
        }
        else
        {
            entry.Timeout = timeoutSeconds;
        }

        return entry;
    }

    private static int RemoveManagedEntries(
        Dictionary<string, List<UserHookEntry>> hooks,
        string eventName)
        => RemoveManagedEntriesCore(hooks, eventName, IsManagedHookEntry);

    private static int RemoveManagedCopilotCliEntries(
        Dictionary<string, List<UserHookEntry>> hooks,
        string eventName)
        => RemoveManagedEntriesCore(hooks, eventName, IsManagedCopilotCliHookEntryForRemoval);

    private static int RemoveManagedCopilotCliEntriesFromAllEvents(
        Dictionary<string, List<UserHookEntry>> hooks)
    {
        int removedEntryCount = 0;
        foreach (string eventName in hooks.Keys.ToArray())
        {
            removedEntryCount += RemoveManagedCopilotCliEntries(hooks, eventName);
        }

        return removedEntryCount;
    }

    private static int RemoveManagedEntriesCore(
        Dictionary<string, List<UserHookEntry>> hooks,
        string eventName,
        Predicate<UserHookEntry?> isManagedEntry)
    {
        if (!hooks.TryGetValue(eventName, out List<UserHookEntry>? hookEntries)
            || hookEntries is null)
        {
            return 0;
        }

        int removedEntryCount = 0;
        for (int index = hookEntries.Count - 1; index >= 0; index--)
        {
            if (isManagedEntry(hookEntries[index]))
            {
                hookEntries.RemoveAt(index);
                removedEntryCount++;
            }
        }

        if (removedEntryCount > 0 && hookEntries.Count == 0)
        {
            hooks.Remove(eventName);
        }

        return removedEntryCount;
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

    private static bool HasManagedCopilotCliEntry(
        Dictionary<string, List<UserHookEntry>> hooks,
        string eventName)
    {
        if (!hooks.TryGetValue(eventName, out List<UserHookEntry>? hookEntries)
            || hookEntries is null)
        {
            return false;
        }

        return hookEntries.Any(IsManagedCopilotCliHookEntry);
    }

    private static bool HasStrictManagedCopilotCliEntry(
        Dictionary<string, List<UserHookEntry>> hooks,
        string eventName,
        string command,
        int timeoutSeconds)
    {
        if (!hooks.TryGetValue(eventName, out List<UserHookEntry>? hookEntries)
            || hookEntries is null)
        {
            return false;
        }

        return hookEntries.Any(
            entry => IsStrictManagedCopilotCliHookEntry(
                entry,
                eventName,
                command,
                timeoutSeconds));
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

    private static bool IsManagedCopilotCliHookEntry(UserHookEntry? entry)
    {
        return IsManagedHookEntry(entry)
            && entry?.TimeoutSec is not null
            && !entry.TimeoutPropertyPresent
            && entry.Env.TryGetValue(
                AppConstants.ManagedHookSurfaceEnvironmentVariable,
                out string? surfaceValue)
            && string.Equals(
                surfaceValue,
                AppConstants.ManagedHookCopilotCliSurfaceValue,
                StringComparison.Ordinal);
    }

    private static bool IsManagedCopilotCliHookEntryForRemoval(UserHookEntry? entry)
    {
        return IsManagedHookEntry(entry)
            && (IsManagedCopilotCliSurfaceEntry(entry)
                || IsLegacyManagedCopilotCliEntry(entry));
    }

    private static bool IsStrictManagedCopilotCliHookEntry(
        UserHookEntry? entry,
        string eventName,
        string command,
        int timeoutSeconds)
    {
        return IsManagedCopilotCliHookEntry(entry)
            && entry is not null
            && string.Equals(entry.Type, "command", StringComparison.Ordinal)
            && string.Equals(entry.Command, command, StringComparison.Ordinal)
            && entry.TimeoutSec == timeoutSeconds
            && entry.Env.TryGetValue(
                AppConstants.ManagedHookEventEnvironmentVariable,
                out string? entryEventName)
            && string.Equals(entryEventName, eventName, StringComparison.Ordinal);
    }

    private static bool IsManagedCopilotCliSurfaceEntry(UserHookEntry? entry)
    {
        return entry?.Env.TryGetValue(
            AppConstants.ManagedHookSurfaceEnvironmentVariable,
            out string? surfaceValue) == true
            && string.Equals(
                surfaceValue,
                AppConstants.ManagedHookCopilotCliSurfaceValue,
                StringComparison.Ordinal);
    }

    private static bool IsLegacyManagedCopilotCliEntry(UserHookEntry? entry)
    {
        return entry is not null
            && entry.TimeoutSec is not null
            && !entry.TimeoutPropertyPresent
            && !entry.Env.ContainsKey(AppConstants.ManagedHookSurfaceEnvironmentVariable);
    }

    private static bool CanDeleteManagedHookFile(
        UserHookSettingsDocument document,
        HookFileFormat format)
    {
        bool hasAdditionalProperties = document.AdditionalProperties is { Count: > 0 };
        return document.Hooks.Count == 0
            && !hasAdditionalProperties
            && (format == HookFileFormat.VsCode
                ? document.Version is null
                : document.Version == 1);
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
        AtomicTextFileWriter.WriteAllText(candidatePath, content);
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

    private enum HookFileFormat
    {
        VsCode,
        CopilotCli,
    }
}
