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
        ConfigurationPlanResult plan = PlanRegisterHookFile(settingsPath, hookFilePath, timestamp);
        if (!plan.Applied || plan.WritePlan is null)
        {
            return new ConfigurationApplyResult(plan.Applied, plan.Message, plan.CandidatePath);
        }

        return ApplyWritePlan(plan.WritePlan, timestamp);
    }

    public static ConfigurationPlanResult PlanRegisterHookFile(
        string settingsPath,
        string hookFilePath,
        string timestamp)
    {
        if (!TryGetSupportedHookFileLocation(
                hookFilePath,
                out string supportedHookFileLocation,
                out string? pathErrorMessage))
        {
            return new ConfigurationPlanResult(
                Applied: false,
                Message: pathErrorMessage
                    ?? "The managed hook file path could not be converted into a supported "
                    + "VS Code hook location entry.");
        }

        bool originalFileExisted = File.Exists(settingsPath);
        string? originalContent = null;
        FileSystemMetadataSnapshot originalMetadata;
        VsCodeUserSettingsDocument rootDocument;
        try
        {
            originalMetadata = FileSystemMetadataSnapshot.Capture(
                settingsPath,
                originalFileExisted);
            if (originalFileExisted)
            {
                originalContent = File.ReadAllText(settingsPath);
                rootDocument = JsonSerializer.Deserialize(
                        originalContent,
                        ReadContext.VsCodeUserSettingsDocument)
                    ?? throw new InvalidOperationException(
                        "The VS Code settings file must contain a JSON object.");
            }
            else
            {
                rootDocument = new VsCodeUserSettingsDocument();
            }
        }
        catch (Exception ex) when (
            ex is IOException or JsonException or InvalidOperationException
                or UnauthorizedAccessException or NotSupportedException)
        {
            string? candidatePath = TryWriteCandidateFile(
                settingsPath,
                SerializeSettings(CreateDesiredDocument(supportedHookFileLocation)),
                timestamp);
            return new ConfigurationPlanResult(
                Applied: false,
                Message:
                    $"The existing VS Code settings file could not be updated automatically: "
                    + ex.Message,
                CandidatePath: candidatePath);
        }

        rootDocument.ChatHookFilesLocations ??=
            new Dictionary<string, bool>(StringComparer.Ordinal);
        RemoveLegacyHookFileLocations(
            rootDocument.ChatHookFilesLocations,
            hookFilePath,
            supportedHookFileLocation);
        rootDocument.ChatHookFilesLocations[supportedHookFileLocation] = true;

        return new ConfigurationPlanResult(
            Applied: true,
            Message: $"Planned VS Code hook registration settings update: {settingsPath}",
            WritePlan: new VsCodeSettingsWritePlan(
                settingsPath,
                SerializeSettings(rootDocument),
                originalFileExisted,
                originalContent,
                originalMetadata,
                SuccessMessage: $"Updated VS Code hook registration settings: {settingsPath}",
                FailureMessage: "The VS Code settings file could not be updated automatically: "));
    }

    public static ConfigurationApplyResult UnregisterHookFile(
        string settingsPath,
        string hookFilePath,
        string timestamp)
    {
        ConfigurationPlanResult plan = PlanUnregisterHookFile(
            settingsPath,
            hookFilePath,
            timestamp);
        if (!plan.Applied || plan.WritePlan is null)
        {
            return new ConfigurationApplyResult(plan.Applied, plan.Message, plan.CandidatePath);
        }

        return ApplyWritePlan(plan.WritePlan, timestamp);
    }

    public static ConfigurationPlanResult PlanUnregisterHookFile(
        string settingsPath,
        string hookFilePath,
        string timestamp)
    {
        if (!File.Exists(settingsPath))
        {
            return new ConfigurationPlanResult(
                Applied: true,
                Message: "The VS Code settings file is already absent.");
        }

        string originalContent;
        FileSystemMetadataSnapshot originalMetadata;
        VsCodeUserSettingsDocument rootDocument;
        try
        {
            originalMetadata = FileSystemMetadataSnapshot.Capture(
                settingsPath,
                fileExisted: true);
            originalContent = File.ReadAllText(settingsPath);
            rootDocument = JsonSerializer.Deserialize(
                    originalContent,
                    ReadContext.VsCodeUserSettingsDocument)
                ?? throw new InvalidOperationException(
                    "The VS Code settings file must contain a JSON object.");
        }
        catch (Exception ex) when (
            ex is IOException or JsonException or InvalidOperationException
                or UnauthorizedAccessException or NotSupportedException)
        {
            return new ConfigurationPlanResult(
                Applied: false,
                Message: "The VS Code settings file could not be parsed. Remove the managed hook "
                + $"registration manually. {ex.Message}");
        }

        bool removedManagedRegistration = false;
        if (rootDocument.ChatHookFilesLocations is not null)
        {
            if (TryGetSupportedHookFileLocation(
                    hookFilePath,
                    out string supportedHookFileLocation,
                    out _))
            {
                removedManagedRegistration = rootDocument.ChatHookFilesLocations.Remove(
                    supportedHookFileLocation);
            }

            removedManagedRegistration |= RemoveLegacyHookFileLocations(
                rootDocument.ChatHookFilesLocations,
                hookFilePath,
                exceptLocation: null);
        }

        if (!removedManagedRegistration)
        {
            return new ConfigurationPlanResult(
                Applied: true,
                Message: "No managed VS Code hook registration was found.");
        }

        if (rootDocument.ChatHookFilesLocations is { Count: 0 })
        {
            rootDocument.ChatHookFilesLocations = null;
        }

        return new ConfigurationPlanResult(
            Applied: true,
            Message: $"Planned VS Code hook registration removal: {settingsPath}",
            WritePlan: new VsCodeSettingsWritePlan(
                settingsPath,
                SerializeSettings(rootDocument),
                OriginalFileExisted: true,
                OriginalContent: originalContent,
                OriginalMetadata: originalMetadata,
                SuccessMessage: $"Removed VS Code hook registration from: {settingsPath}",
                FailureMessage:
                    "The VS Code settings file could not be updated automatically while "
                    + "removing the managed hook registration: "));
    }

    internal static ConfigurationApplyResult ApplyWritePlan(
        VsCodeSettingsWritePlan writePlan,
        string timestamp)
    {
        ConfigurationApplyResult? freshnessFailure = VerifyPlanSnapshotIsCurrent(
            writePlan,
            timestamp);
        if (freshnessFailure is not null)
        {
            return freshnessFailure;
        }

        try
        {
            AtomicTextFileWriter.WriteAllText(writePlan.SettingsPath, writePlan.SerializedSettings);
        }
        catch (Exception ex) when (
            ex is IOException or UnauthorizedAccessException or NotSupportedException)
        {
            string? candidatePath = TryWriteCandidateFile(
                writePlan.SettingsPath,
                writePlan.SerializedSettings,
                timestamp);
            return new ConfigurationApplyResult(
                Applied: false,
                Message: writePlan.FailureMessage + ex.Message,
                CandidatePath: candidatePath);
        }

        return new ConfigurationApplyResult(true, writePlan.SuccessMessage);
    }

    internal static ConfigurationApplyResult RollbackWritePlan(VsCodeSettingsWritePlan writePlan)
    {
        ConfigurationApplyResult? freshnessFailure = VerifyRollbackSnapshotIsCurrent(writePlan);
        if (freshnessFailure is not null)
        {
            return freshnessFailure;
        }

        try
        {
            if (writePlan.OriginalFileExisted)
            {
                AtomicTextFileWriter.WriteAllText(
                    writePlan.SettingsPath,
                    writePlan.OriginalContent ?? string.Empty);
            }
            else if (File.Exists(writePlan.SettingsPath))
            {
                File.Delete(writePlan.SettingsPath);
            }

            writePlan.OriginalMetadata.Restore(writePlan.SettingsPath);
        }
        catch (Exception ex) when (
            ex is IOException or UnauthorizedAccessException or NotSupportedException)
        {
            return new ConfigurationApplyResult(
                Applied: false,
                Message:
                    $"Failed to roll back the VS Code settings file '{writePlan.SettingsPath}': "
                    + ex.Message);
        }

        return new ConfigurationApplyResult(
            Applied: true,
            Message: $"Rolled back VS Code settings file: {writePlan.SettingsPath}");
    }

    public static bool IsHookFileRegistered(string settingsPath, string hookFilePath)
    {
        VsCodeUserSettingsDocument? rootDocument = TryParseSettings(settingsPath);
        if (rootDocument?.ChatHookFilesLocations is null)
        {
            return false;
        }

        if (!TryGetSupportedHookFileLocation(
                hookFilePath,
            out string supportedHookFileLocation,
                out _))
        {
            return false;
        }

        return rootDocument.ChatHookFilesLocations.TryGetValue(
            supportedHookFileLocation,
                out bool isEnabled)
            && isEnabled;
    }

    internal static bool TryGetSupportedHookFileLocation(
        string hookFilePath,
        out string supportedHookFileLocation,
        out string? errorMessage)
    {
        string fullHookFilePath = Path.GetFullPath(hookFilePath);
        string userHomePath = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);

        if (string.IsNullOrWhiteSpace(userHomePath))
        {
            supportedHookFileLocation = string.Empty;
            errorMessage =
                "The current user's home directory could not be resolved for "
                + "chat.hookFilesLocations registration.";
            return false;
        }

        string fullUserHomePath = Path.GetFullPath(userHomePath);
        string relativeHookFilePath = Path.GetRelativePath(fullUserHomePath, fullHookFilePath);
        string normalizedRelativeHookFilePath = NormalizePathSeparators(relativeHookFilePath);

        if (Path.IsPathRooted(relativeHookFilePath)
            || string.Equals(normalizedRelativeHookFilePath, ".", StringComparison.Ordinal)
            || string.Equals(normalizedRelativeHookFilePath, "..", StringComparison.Ordinal)
            || normalizedRelativeHookFilePath.StartsWith("../", StringComparison.Ordinal))
        {
            supportedHookFileLocation = string.Empty;
            errorMessage =
                $"The managed hook file '{fullHookFilePath}' is outside the current user's "
                + "home directory. VS Code only supports chat.hookFilesLocations entries "
                + "that are relative or start with '~/', so choose a hook file path under "
                + $"'{fullUserHomePath}'.";
            return false;
        }

        supportedHookFileLocation = $"~/{normalizedRelativeHookFilePath}";
        errorMessage = null;
        return true;
    }

    internal static string GetSupportedHookFileLocation(string hookFilePath)
    {
        if (TryGetSupportedHookFileLocation(
                hookFilePath,
                out string supportedHookFileLocation,
                out string? errorMessage))
        {
            return supportedHookFileLocation;
        }

        throw new InvalidOperationException(
            errorMessage
            ?? "The managed hook file path could not be converted into a supported VS "
            + "Code hook location entry.");
    }

    private static VsCodeUserSettingsDocument CreateDesiredDocument(string hookFileLocation)
    {
        return new VsCodeUserSettingsDocument
        {
            ChatHookFilesLocations = new Dictionary<string, bool>(StringComparer.Ordinal)
            {
                [hookFileLocation] = true,
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

    private static ConfigurationApplyResult? VerifyPlanSnapshotIsCurrent(
        VsCodeSettingsWritePlan writePlan,
        string timestamp)
    {
        try
        {
            if (writePlan.OriginalFileExisted)
            {
                if (!File.Exists(writePlan.SettingsPath))
                {
                    return CreatePlanSnapshotFailure(
                        writePlan,
                        timestamp,
                        "The VS Code settings file changed after the managed update was "
                        + "planned, so it was not overwritten automatically.");
                }

                string currentContent = File.ReadAllText(writePlan.SettingsPath);
                if (!string.Equals(
                        currentContent,
                        writePlan.OriginalContent ?? string.Empty,
                        StringComparison.Ordinal))
                {
                    return CreatePlanSnapshotFailure(
                        writePlan,
                        timestamp,
                        "The VS Code settings file changed after the managed update was "
                        + "planned, so it was not overwritten automatically.");
                }

                return null;
            }

            if (File.Exists(writePlan.SettingsPath))
            {
                return CreatePlanSnapshotFailure(
                    writePlan,
                    timestamp,
                    "The VS Code settings file was created after the managed update was "
                    + "planned, so it was not overwritten automatically.");
            }
        }
        catch (Exception ex) when (
            ex is IOException or UnauthorizedAccessException or NotSupportedException)
        {
            string? candidatePath = TryWriteCandidateFile(
                writePlan.SettingsPath,
                writePlan.SerializedSettings,
                timestamp);
            return new ConfigurationApplyResult(
                Applied: false,
                Message:
                    $"The VS Code settings file could not be validated before update: "
                    + ex.Message,
                CandidatePath: candidatePath);
        }

        return null;
    }

    private static ConfigurationApplyResult CreatePlanSnapshotFailure(
        VsCodeSettingsWritePlan writePlan,
        string timestamp,
        string message)
    {
        string? candidatePath = TryWriteCandidateFile(
            writePlan.SettingsPath,
            writePlan.SerializedSettings,
            timestamp);
        return new ConfigurationApplyResult(
            Applied: false,
            Message: message,
            CandidatePath: candidatePath);
    }

    private static ConfigurationApplyResult? VerifyRollbackSnapshotIsCurrent(
        VsCodeSettingsWritePlan writePlan)
    {
        try
        {
            if (!writePlan.OriginalFileExisted)
            {
                if (!File.Exists(writePlan.SettingsPath))
                {
                    return null;
                }

                string currentContent = File.ReadAllText(writePlan.SettingsPath);
                if (!string.Equals(
                        currentContent,
                        writePlan.SerializedSettings,
                        StringComparison.Ordinal))
                {
                    return new ConfigurationApplyResult(
                        Applied: false,
                        Message:
                            $"Failed to roll back the VS Code settings file "
                            + $"'{writePlan.SettingsPath}' because it changed after the managed "
                            + "update was written.");
                }

                return null;
            }

            if (!File.Exists(writePlan.SettingsPath))
            {
                return new ConfigurationApplyResult(
                    Applied: false,
                    Message:
                        $"Failed to roll back the VS Code settings file '{writePlan.SettingsPath}' "
                        + "because it changed after the managed update was written.");
            }

            string existingContent = File.ReadAllText(writePlan.SettingsPath);
            if (string.Equals(
                    existingContent,
                    writePlan.OriginalContent ?? string.Empty,
                    StringComparison.Ordinal))
            {
                return null;
            }

            if (!string.Equals(
                    existingContent,
                    writePlan.SerializedSettings,
                    StringComparison.Ordinal))
            {
                return new ConfigurationApplyResult(
                    Applied: false,
                    Message:
                        $"Failed to roll back the VS Code settings file '{writePlan.SettingsPath}' "
                        + "because it changed after the managed update was written.");
            }
        }
        catch (Exception ex) when (
            ex is IOException or UnauthorizedAccessException or NotSupportedException)
        {
            return new ConfigurationApplyResult(
                Applied: false,
                Message:
                    $"Failed to roll back the VS Code settings file '{writePlan.SettingsPath}': "
                    + ex.Message);
        }

        return null;
    }

    private static bool RemoveLegacyHookFileLocations(
        Dictionary<string, bool> hookFilesLocations,
        string hookFilePath,
        string? exceptLocation)
    {
        bool removedAny = false;

        foreach (string legacyHookFileLocation in GetLegacyHookFileLocations(hookFilePath))
        {
            if (string.Equals(legacyHookFileLocation, exceptLocation, StringComparison.Ordinal))
            {
                continue;
            }

            removedAny |= hookFilesLocations.Remove(legacyHookFileLocation);
        }

        return removedAny;
    }

    private static IReadOnlyList<string> GetLegacyHookFileLocations(string hookFilePath)
    {
        string fullHookFilePath = Path.GetFullPath(hookFilePath);
        HashSet<string> legacyHookFileLocations = new(
            OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal)
        {
            fullHookFilePath,
            NormalizePathSeparators(fullHookFilePath),
        };

        return [.. legacyHookFileLocations];
    }

    private static string NormalizePathSeparators(string path)
        => path.Replace('\\', '/');

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
