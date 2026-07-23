using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;

namespace Hcoona.VsCodeCopilotTelegramHook;

internal static class AppConstants
{
    public const int SchemaVersion = 1;

    public const string CopilotDirectoryName = ".copilot";
    public const string NotificationsDirectoryName = "notifications";
    public const string SessionsDirectoryName = "sessions";
    public const string PromptsDirectoryName = "prompts";
    public const string TurnsDirectoryName = "turns";
    public const string StopsDirectoryName = "stops";
    public const string NotificationsRecordsDirectoryName = "notifications";
    public const string ClaimsDirectoryName = "claims";
    public const string SessionFileName = "session.json";
    public const string CurrentFileName = "current.json";
    public const string TurnFileName = "turn.json";
    public const string SummaryFileName = "summary.json";
    public const string SessionLogFileName = "hook.log";
    public const string UserCommandLogFileName = "user-command.log";
    public const string UserOperationLockFilePrefix =
        "hcoona-vscode-copilot-telegram-hook-user-operation";
    public const string ManagedHookFileName = "vscode-copilot-telegram-hook.hooks.json";
    public const string CopilotCliHookFileName = "vscode-copilot-telegram-hook.json";
    public const string CopilotCliExtensionDirectoryName = "vscode-copilot-telegram-hook";
    public const string CopilotCliExtensionFileName = "extension.mjs";
    public const string CopilotCliEventsDirectoryName = "copilot-cli-events";
    public const string ChatHookFilesLocationsSettingName = "chat.hookFilesLocations";

    public const string ManagedHookEnvironmentVariable = "HCOONA_VSCODE_COPILOT_TELEGRAM_HOOK";
    public const string ManagedHookEnvironmentValue = "1";
    public const string ManagedHookEventEnvironmentVariable =
        "HCOONA_VSCODE_COPILOT_TELEGRAM_HOOK_EVENT";
    public const string ManagedHookSurfaceEnvironmentVariable =
        "HCOONA_VSCODE_COPILOT_TELEGRAM_HOOK_SURFACE";
    public const string ManagedHookCopilotCliSurfaceValue = "copilot-cli";

    public const string CopilotHomeEnvironmentVariable = "COPILOT_HOME";
    public const string TelegramBotTokenEnvironmentVariable = "TG_BOT_TOKEN";
    public const string TelegramChatIdEnvironmentVariable = "TG_CHAT_ID";

    public const string SecretPrefix = "copilot/vscode-copilot-telegram-hook";
    public const string TelegramBotTokenSecretName = "telegram-bot-token";
    public const string TelegramChatIdSecretName = "telegram-chat-id";

    public const int MaxTelegramHtmlMessageLength = 3900;
    public const int SummaryReadRetryCount = 3;
    public const int SummaryReadRetryDelayMilliseconds = 50;
    public const int TurnDeliveryClaimStaleAfterMinutes = 5;
    public const int CopilotCliEventClaimStaleAfterMinutes = 5;
    public static readonly Version MinimumCopilotCliUserExtensionsVersion = new(1, 0, 41);
}

internal static class AppPaths
{
    public static string GetDefaultInstallRoot()
    {
        if (OperatingSystem.IsWindows())
        {
            return Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "Hcoona",
                "VsCodeCopilotTelegramHook");
        }

        return Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
            ".local",
            "share",
            "hcoona",
            "vscode-copilot-telegram-hook");
    }

    public static string GetDefaultManagedHookFilePath()
        => GetDefaultManagedHookFilePath(GetDefaultInstallRoot());

    public static string GetDefaultManagedHookFilePath(string installRoot)
        => Path.Combine(installRoot, AppConstants.ManagedHookFileName);

    public static string GetDefaultCopilotCliHookFilePath()
        => Path.Combine(GetDefaultCopilotCliHooksDirectory(), AppConstants.CopilotCliHookFileName);

    public static string GetDefaultCopilotCliHooksDirectory()
        => Path.Combine(GetCopilotCliHomeDirectory(), "hooks");

    public static string GetDefaultCopilotCliExtensionFilePath()
        => Path.Combine(
            GetCopilotCliHomeDirectory(),
            "extensions",
            AppConstants.CopilotCliExtensionDirectoryName,
            AppConstants.CopilotCliExtensionFileName);

    private static string GetCopilotCliHomeDirectory()
    {
        string? copilotHome = Environment.GetEnvironmentVariable(
            AppConstants.CopilotHomeEnvironmentVariable);
        if (!string.IsNullOrWhiteSpace(copilotHome))
        {
            return Path.GetFullPath(copilotHome.Trim());
        }

        return Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
            AppConstants.CopilotDirectoryName);
    }

    public static string GetDefaultVsCodeSettingsPath()
    {
        string applicationDataPath = Environment.GetFolderPath(
            Environment.SpecialFolder.ApplicationData);

        if (string.IsNullOrWhiteSpace(applicationDataPath))
        {
            string userProfilePath = Environment.GetFolderPath(
                Environment.SpecialFolder.UserProfile);
            applicationDataPath = OperatingSystem.IsWindows()
                ? userProfilePath
                : Path.Combine(userProfilePath, ".config");
        }

        return Path.Combine(applicationDataPath, "Code", "User", "settings.json");
    }

    public static string GetDefaultVsCodeServerSettingsPath()
    {
        return Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
            ".vscode-server",
            "data",
            "Machine",
            "settings.json");
    }

    public static IReadOnlyList<VsCodeSettingsTarget> GetDefaultVsCodeSettingsTargets()
    {
        bool serverTargetApplicable = IsDefaultVsCodeServerSettingsApplicable();
        return GetDistinctSettingsTargets(
            [
                new VsCodeSettingsTarget(
                    GetDefaultVsCodeSettingsPath(),
                    IsApplicable: true,
                    DisplayName: "VS Code desktop user settings"),
                new VsCodeSettingsTarget(
                    GetDefaultVsCodeServerSettingsPath(),
                    IsApplicable: serverTargetApplicable,
                    DisplayName: "VS Code Server Machine settings",
                    InapplicableReason: serverTargetApplicable
                        ? null
                        :
                        "No same-host VS Code Server installation was detected under "
                        + "'~/.vscode-server'."),
            ]);
    }

    public static UserInstallationPaths ResolveUserPaths(UserPathOverrides overrides)
    {
        string installRoot = overrides.InstallRoot?.FullName ?? GetDefaultInstallRoot();
        string managedHookFilePath =
            overrides.ManagedHookFilePath?.FullName
            ?? GetDefaultManagedHookFilePath(installRoot);
        string copilotCliHookFilePath =
            overrides.CopilotCliHookFilePath?.FullName
            ?? GetDefaultCopilotCliHookFilePath();
        string copilotCliExtensionFilePath =
            overrides.CopilotCliExtensionFilePath?.FullName
            ?? GetCopilotCliExtensionFilePathForHookOverride(
                overrides.CopilotCliHookFilePath?.FullName)
            ?? GetDefaultCopilotCliExtensionFilePath();
        IReadOnlyList<VsCodeSettingsTarget> vsCodeSettingsTargets =
            overrides.VsCodeSettingsTargets is { Count: > 0 }
                ? GetDistinctSettingsTargets(overrides.VsCodeSettingsTargets)
                : overrides.VsCodeSettingsPaths is { Count: > 0 }
                    ? GetDistinctSettingsTargets(
                        overrides.VsCodeSettingsPaths.Select(
                            static fileInfo =>
                                new VsCodeSettingsTarget(
                                    fileInfo.FullName,
                                    IsApplicable: true,
                                    DisplayName: "VS Code settings override")))
                    : GetDefaultVsCodeSettingsTargets();
        string installedBinaryPath = Path.Combine(installRoot, GetManagedExecutableName());
        string userLogFilePath = GetUserLogPath(installRoot);

        return new UserInstallationPaths(
            Path.GetFullPath(installRoot),
            Path.GetFullPath(installedBinaryPath),
            Path.GetFullPath(managedHookFilePath),
            Path.GetFullPath(copilotCliHookFilePath),
            Path.GetFullPath(copilotCliExtensionFilePath),
            vsCodeSettingsTargets,
            Path.GetFullPath(userLogFilePath));
    }

    private static string? GetCopilotCliExtensionFilePathForHookOverride(
        string? copilotCliHookFilePath)
    {
        if (string.IsNullOrWhiteSpace(copilotCliHookFilePath))
        {
            return null;
        }

        string? hooksDirectory = Path.GetDirectoryName(Path.GetFullPath(copilotCliHookFilePath));
        if (!GetPlatformPathComparer().Equals(Path.GetFileName(hooksDirectory), "hooks"))
        {
            return null;
        }

        string? copilotHome = Path.GetDirectoryName(hooksDirectory);
        return copilotHome is null
            ? null
            : Path.Combine(
                copilotHome,
                "extensions",
                AppConstants.CopilotCliExtensionDirectoryName,
                AppConstants.CopilotCliExtensionFileName);
    }

    public static string? ValidateUserArtifactPathCollisions(
        UserInstallationPaths paths,
        string? sourceBinaryPath = null,
        Func<VsCodeSettingsTarget, bool>? includeVsCodeSettingsTarget = null)
    {
        includeVsCodeSettingsTarget ??= static target => target.IsApplicable;
        List<(string Label, string Path)> managedArtifactPaths =
        [
            ("installed binary", paths.InstalledBinaryPath),
            (
                "installed binary PDB companion",
                Path.ChangeExtension(paths.InstalledBinaryPath, ".pdb")
            ),
            (
                "installed binary debug companion",
                Path.ChangeExtension(paths.InstalledBinaryPath, ".dbg")
            ),
            ("VS Code managed hook file", paths.ManagedHookFilePath),
            ("Copilot CLI hook file", paths.CopilotCliHookFilePath),
            ("Copilot CLI extension file", paths.CopilotCliExtensionFilePath),
            .. paths.VsCodeSettingsTargets
                .Where(includeVsCodeSettingsTarget)
                .Select(static target =>
                    ($"VS Code settings file ({target.DisplayName})", target.SettingsPath)),
        ];

        StringComparer comparer = GetPlatformPathComparer();
        Dictionary<string, (string Label, string Path)> seenPaths = new(comparer);
        Dictionary<FileSystemIdentity, (string Label, string Path)> seenIdentities = [];
        Dictionary<FuturePathIdentity, (string Label, string Path)> seenFuturePaths =
            new(new FuturePathIdentityComparer(comparer));
        foreach ((string label, string path) in managedArtifactPaths)
        {
            string normalizedPath = Path.GetFullPath(path);
            if (seenPaths.TryGetValue(normalizedPath, out (string Label, string Path) existing))
            {
                return FormatPathCollisionMessage(
                    existing.Label,
                    existing.Path,
                    label,
                    path,
                    normalizedPath);
            }

            seenPaths.Add(normalizedPath, (label, path));
            if (TryGetFileSystemIdentity(normalizedPath, out FileSystemIdentity identity))
            {
                if (seenIdentities.TryGetValue(identity, out existing))
                {
                    return FormatPathCollisionMessage(
                        existing.Label,
                        existing.Path,
                        label,
                        path,
                        normalizedPath);
                }

                seenIdentities.Add(identity, (label, path));
            }

            FuturePathIdentity futurePath = GetFuturePathIdentity(normalizedPath);
            if (seenFuturePaths.TryGetValue(futurePath, out existing))
            {
                return FormatPathCollisionMessage(
                    existing.Label,
                    existing.Path,
                    label,
                    path,
                    futurePath.DisplayPath);
            }

            seenFuturePaths.Add(futurePath, (label, path));
        }

        if (!string.IsNullOrWhiteSpace(sourceBinaryPath))
        {
            foreach ((string sourceLabel, string sourcePath) in
                EnumerateSourceArtifactPaths(sourceBinaryPath))
            {
                foreach ((string label, string path) in managedArtifactPaths)
                {
                    if (PathsReferToSameCurrentOrFutureFile(
                            sourcePath,
                            path,
                            comparer,
                            out string matchedPath))
                    {
                        return FormatPathCollisionMessage(
                            sourceLabel,
                            sourcePath,
                            label,
                            path,
                            matchedPath);
                    }
                }
            }
        }

        return null;
    }

    public static string GetManagedExecutableName()
        => OperatingSystem.IsWindows()
            ? "vscode-copilot-telegram-hook.exe"
            : "vscode-copilot-telegram-hook";

    public static string GetWorkspaceCopilotDirectory(string workspacePath)
        => Path.Combine(workspacePath, AppConstants.CopilotDirectoryName);

    public static string GetWorkspaceSessionsDirectory(string workspacePath)
        => Path.Combine(
            GetWorkspaceCopilotDirectory(workspacePath),
            AppConstants.NotificationsDirectoryName,
            AppConstants.SessionsDirectoryName);

    public static string GetWorkspaceLogPath(string workspacePath)
        => Path.Combine(
            GetWorkspaceCopilotDirectory(Path.GetFullPath(workspacePath)),
            AppConstants.SessionLogFileName);

    public static string GetSessionDirectoryName(string sessionId)
    {
        if (string.IsNullOrWhiteSpace(sessionId))
        {
            throw new InvalidOperationException("The session id cannot be empty.");
        }

        char[] invalidCharacters = Path.GetInvalidFileNameChars();
        string sanitized = new(
            sessionId
                .Select(static character => character)
                .Select(character =>
                    character == Path.DirectorySeparatorChar
                    || character == Path.AltDirectorySeparatorChar
                    || Array.IndexOf(invalidCharacters, character) >= 0
                    || char.IsControl(character)
                        ? '_'
                        : character)
                .ToArray());

        sanitized = sanitized.Trim().TrimEnd('.');
        if (string.IsNullOrWhiteSpace(sanitized))
        {
            sanitized = "session";
        }

        if (sanitized.Length > 48)
        {
            sanitized = sanitized[..48];
        }

        string hash = Convert
            .ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(sessionId)))[..12]
            .ToLowerInvariant();
        return $"{sanitized}-{hash}";
    }

    public static string GetSessionDirectoryPath(string workspacePath, string sessionId)
        => Path.Combine(
            GetWorkspaceSessionsDirectory(workspacePath),
            GetSessionDirectoryName(sessionId));

    public static string GetSessionStatePath(string workspacePath, string sessionId)
        => Path.Combine(
            GetSessionDirectoryPath(workspacePath, sessionId),
            AppConstants.SessionFileName);

    public static string GetCurrentStatePath(string workspacePath, string sessionId)
        => Path.Combine(
            GetSessionDirectoryPath(workspacePath, sessionId),
            AppConstants.CurrentFileName);

    public static string GetPromptObservationPath(
        string workspacePath,
        string sessionId,
        string promptObservationId)
        => Path.Combine(
            GetSessionDirectoryPath(workspacePath, sessionId),
            AppConstants.PromptsDirectoryName,
            $"{promptObservationId}.json");

    public static string GetTurnsDirectoryPath(string workspacePath, string sessionId)
        => Path.Combine(
            GetSessionDirectoryPath(workspacePath, sessionId),
            AppConstants.TurnsDirectoryName);

    public static string GetTurnDirectoryPath(
        string workspacePath,
        string sessionId,
        string notificationTurnId)
        => Path.Combine(GetTurnsDirectoryPath(workspacePath, sessionId), notificationTurnId);

    public static string GetTurnStatePath(
        string workspacePath,
        string sessionId,
        string notificationTurnId)
        => Path.Combine(
            GetTurnDirectoryPath(workspacePath, sessionId, notificationTurnId),
            AppConstants.TurnFileName);

    public static string GetSummaryStatePath(
        string workspacePath,
        string sessionId,
        string notificationTurnId)
        => Path.Combine(
            GetTurnDirectoryPath(workspacePath, sessionId, notificationTurnId),
            AppConstants.SummaryFileName);

    public static string GetStopObservationPath(
        string workspacePath,
        string sessionId,
        string notificationTurnId,
        string stopId)
        => Path.Combine(
            GetTurnDirectoryPath(workspacePath, sessionId, notificationTurnId),
            AppConstants.StopsDirectoryName,
            $"{stopId}.json");

    public static string GetNotificationRecordPath(
        string workspacePath,
        string sessionId,
        string notificationTurnId,
        string notificationKey)
        => Path.Combine(
            GetTurnDirectoryPath(workspacePath, sessionId, notificationTurnId),
            AppConstants.NotificationsRecordsDirectoryName,
            $"{notificationKey}.json");

    public static string GetSessionNotificationRecordPath(
        string workspacePath,
        string sessionId,
        string notificationKey)
        => Path.Combine(
            GetSessionDirectoryPath(workspacePath, sessionId),
            AppConstants.NotificationsRecordsDirectoryName,
            $"{notificationKey}.json");

    public static string GetSessionStopClaimPath(
        string workspacePath,
        string sessionId,
        string notificationKey)
        => Path.Combine(
            GetSessionDirectoryPath(workspacePath, sessionId),
            AppConstants.ClaimsDirectoryName,
            $"{notificationKey}.claim");

    public static string GetSessionStopReclaimClaimPath(
        string workspacePath,
        string sessionId,
        string notificationKey)
        => Path.Combine(
            GetSessionDirectoryPath(workspacePath, sessionId),
            AppConstants.ClaimsDirectoryName,
            $"{notificationKey}.reclaim.claim");

    public static string GetCopilotCliEventMarkerPath(
        string workspacePath,
        string sessionId,
        string eventKey)
        => GetCopilotCliEventPath(workspacePath, sessionId, eventKey, ".sent");

    public static string GetCopilotCliEventClaimPath(
        string workspacePath,
        string sessionId,
        string eventKey)
        => GetCopilotCliEventPath(workspacePath, sessionId, eventKey, ".claim");

    public static string GetCopilotCliEventReclaimClaimPath(
        string workspacePath,
        string sessionId,
        string eventKey)
        => GetCopilotCliEventPath(workspacePath, sessionId, eventKey, ".reclaim.claim");

    private static string GetCopilotCliEventPath(
        string workspacePath,
        string sessionId,
        string eventKey,
        string suffix)
    {
        string hash = Convert
            .ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(eventKey)))[..32]
            .ToLowerInvariant();
        return Path.Combine(
            GetSessionDirectoryPath(workspacePath, sessionId),
            AppConstants.CopilotCliEventsDirectoryName,
            hash + suffix);
    }

    public static string GetTurnDeliveryClaimPath(
        string workspacePath,
        string sessionId,
        string notificationTurnId)
        => Path.Combine(
            GetTurnDirectoryPath(workspacePath, sessionId, notificationTurnId),
            AppConstants.ClaimsDirectoryName,
            "delivery.claim");

    public static string GetTurnDeliveryReclaimClaimPath(
        string workspacePath,
        string sessionId,
        string notificationTurnId)
        => Path.Combine(
            GetTurnDirectoryPath(workspacePath, sessionId, notificationTurnId),
            AppConstants.ClaimsDirectoryName,
            "delivery.reclaim.claim");

    public static string GetSessionLogPath(string workspacePath, string sessionId)
        => Path.Combine(
            GetSessionDirectoryPath(Path.GetFullPath(workspacePath), sessionId),
            AppConstants.SessionLogFileName);

    public static string GetSessionLogPathPattern(string workspacePath)
        => Path.Combine(
            GetWorkspaceSessionsDirectory(Path.GetFullPath(workspacePath)),
            "<session_id>",
            AppConstants.SessionLogFileName);

    public static string GetUserLogPath(string installRoot)
        => Path.Combine(
            Path.GetFullPath(installRoot),
            AppConstants.UserCommandLogFileName);

    public static string GetRelativeSessionStatePath(string sessionId)
        => GetRelativeSessionFilePath(sessionId, AppConstants.SessionFileName);

    public static string GetRelativeTurnStatePath(string sessionId)
        => GetRelativeSessionFilePath(
            sessionId,
            AppConstants.TurnsDirectoryName,
            "<notification_turn_id>",
            AppConstants.TurnFileName);

    public static string GetRelativeSummaryStatePath(string sessionId)
        => GetRelativeSessionFilePath(
            sessionId,
            AppConstants.TurnsDirectoryName,
            "<notification_turn_id>",
            AppConstants.SummaryFileName);

    public static string GetRelativeSummaryStatePath(string sessionId, string notificationTurnId)
        => GetRelativeSessionFilePath(
            sessionId,
            AppConstants.TurnsDirectoryName,
            notificationTurnId,
            AppConstants.SummaryFileName);

    private static string GetRelativeSessionFilePath(string sessionId, params string[] pathSegments)
    {
        return string.Join(
            '/',
            [
                AppConstants.CopilotDirectoryName,
                AppConstants.NotificationsDirectoryName,
                AppConstants.SessionsDirectoryName,
                GetSessionDirectoryName(sessionId),
                .. pathSegments,
            ]);
    }

    public static string GetTelegramBotTokenSecretPath()
        => $"{AppConstants.SecretPrefix}/{AppConstants.TelegramBotTokenSecretName}";

    public static string GetTelegramChatIdSecretPath()
        => $"{AppConstants.SecretPrefix}/{AppConstants.TelegramChatIdSecretName}";

    public static string GetExecutionEnvironmentDisplay()
    {
        string os = RuntimeInformation.OSDescription.Trim();
        string architecture = RuntimeInformation.ProcessArchitecture.ToString();
        string? wslDistribution = Environment.GetEnvironmentVariable("WSL_DISTRO_NAME");

        return string.IsNullOrWhiteSpace(wslDistribution)
            ? $"{os} | {architecture}"
            : $"WSL {wslDistribution} | {os} | {architecture}";
    }

    internal static bool IsDefaultVsCodeServerSettingsApplicable()
        => OperatingSystem.IsLinux();

    private static List<VsCodeSettingsTarget> GetDistinctSettingsTargets(
        IEnumerable<VsCodeSettingsTarget> targets)
    {
        HashSet<string> seenPaths = new(GetPlatformPathComparer());
        List<VsCodeSettingsTarget> distinctTargets = [];

        foreach (VsCodeSettingsTarget target in targets)
        {
            string fullPath = Path.GetFullPath(target.SettingsPath);
            if (seenPaths.Add(fullPath))
            {
                distinctTargets.Add(target with { SettingsPath = fullPath });
            }
        }

        return distinctTargets;
    }

    private static StringComparer GetPlatformPathComparer()
        => OperatingSystem.IsWindows()
            ? StringComparer.OrdinalIgnoreCase
            : StringComparer.Ordinal;

    private static IEnumerable<(string Label, string Path)> EnumerateSourceArtifactPaths(
        string sourceBinaryPath)
    {
        yield return ("source executable", sourceBinaryPath);

        foreach (string extension in new[] { ".pdb", ".dbg" })
        {
            string sourceCompanionPath = Path.ChangeExtension(sourceBinaryPath, extension);
            if (File.Exists(sourceCompanionPath))
            {
                yield return ($"source {extension} companion", sourceCompanionPath);
            }
        }
    }

    private static bool PathsReferToSameCurrentOrFutureFile(
        string leftPath,
        string rightPath,
        StringComparer comparer,
        out string matchedPath)
    {
        string normalizedLeftPath = Path.GetFullPath(leftPath);
        string normalizedRightPath = Path.GetFullPath(rightPath);
        if (comparer.Equals(normalizedLeftPath, normalizedRightPath))
        {
            matchedPath = normalizedRightPath;
            return true;
        }

        if (TryGetFileSystemIdentity(normalizedLeftPath, out FileSystemIdentity leftIdentity)
            && TryGetFileSystemIdentity(normalizedRightPath, out FileSystemIdentity rightIdentity)
            && leftIdentity == rightIdentity)
        {
            matchedPath = normalizedRightPath;
            return true;
        }

        FuturePathIdentity leftFuturePath = GetFuturePathIdentity(normalizedLeftPath);
        FuturePathIdentity rightFuturePath = GetFuturePathIdentity(normalizedRightPath);
        if (new FuturePathIdentityComparer(comparer).Equals(leftFuturePath, rightFuturePath))
        {
            matchedPath = rightFuturePath.DisplayPath;
            return true;
        }

        matchedPath = string.Empty;
        return false;
    }

    private static FuturePathIdentity GetFuturePathIdentity(string path)
    {
        string fullPath = Path.GetFullPath(path);
        Stack<string> remainingSegments = [];
        string ancestorPath = fullPath;
        while (!File.Exists(ancestorPath) && !Directory.Exists(ancestorPath))
        {
            string? segment = Path.GetFileName(ancestorPath);
            if (!string.IsNullOrEmpty(segment))
            {
                remainingSegments.Push(segment);
            }

            string? parentPath = Path.GetDirectoryName(ancestorPath);
            if (string.IsNullOrEmpty(parentPath)
                || string.Equals(parentPath, ancestorPath, StringComparison.Ordinal))
            {
                break;
            }

            ancestorPath = parentPath;
        }

        string relativePath = string.Join(Path.DirectorySeparatorChar, remainingSegments);
        FileSystemIdentity? ancestorIdentity =
            TryGetFileSystemIdentity(ancestorPath, out FileSystemIdentity identity)
                ? identity
                : null;
        string displayPath = string.IsNullOrEmpty(relativePath)
            ? Path.GetFullPath(ancestorPath)
            : Path.Combine(Path.GetFullPath(ancestorPath), relativePath);

        return new FuturePathIdentity(
            ancestorIdentity,
            Path.GetFullPath(ancestorPath),
            relativePath,
            displayPath);
    }

    private static bool TryGetFileSystemIdentity(
        string path,
        out FileSystemIdentity identity)
    {
        identity = default;
        if (!OperatingSystem.IsLinux())
        {
            return false;
        }

        try
        {
            if (!File.Exists(path) && !Directory.Exists(path))
            {
                return false;
            }

            if (stat(path, out StatBuffer statBuffer) != 0)
            {
                return false;
            }

            identity = new FileSystemIdentity(statBuffer.Dev, statBuffer.Ino);
            return true;
        }
        catch (Exception ex) when (
            ex is IOException or UnauthorizedAccessException or NotSupportedException
                or DllNotFoundException or EntryPointNotFoundException)
        {
            return false;
        }
    }

    [DllImport("libc", EntryPoint = "stat", SetLastError = true)]
    private static extern int stat(string path, out StatBuffer buffer);

    private readonly record struct FileSystemIdentity(ulong Device, ulong Inode);

    private readonly record struct FuturePathIdentity(
        FileSystemIdentity? AncestorIdentity,
        string AncestorPath,
        string RelativePath,
        string DisplayPath);

    private sealed class FuturePathIdentityComparer(StringComparer pathComparer)
        : IEqualityComparer<FuturePathIdentity>
    {
        public bool Equals(FuturePathIdentity left, FuturePathIdentity right)
        {
            if (left.AncestorIdentity is { } leftIdentity
                && right.AncestorIdentity is { } rightIdentity)
            {
                return leftIdentity == rightIdentity
                    && pathComparer.Equals(left.RelativePath, right.RelativePath);
            }

            if (left.AncestorIdentity is not null || right.AncestorIdentity is not null)
            {
                return false;
            }

            return pathComparer.Equals(left.AncestorPath, right.AncestorPath)
                && pathComparer.Equals(left.RelativePath, right.RelativePath);
        }

        public int GetHashCode(FuturePathIdentity value)
        {
            HashCode hashCode = new();
            if (value.AncestorIdentity is { } identity)
            {
                hashCode.Add(identity);
            }
            else
            {
                hashCode.Add(value.AncestorPath, pathComparer);
            }

            hashCode.Add(value.RelativePath, pathComparer);
            return hashCode.ToHashCode();
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct StatBuffer
    {
        public ulong Dev;
        public ulong Ino;
        public ulong Nlink;
        public uint Mode;
        public uint Uid;
        public uint Gid;
        public int Pad0;
        public ulong Rdev;
        public long Size;
        public long Blksize;
        public long Blocks;
        public Timespec Atime;
        public Timespec Mtime;
        public Timespec Ctime;
        public long Reserved0;
        public long Reserved1;
        public long Reserved2;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct Timespec
    {
        public long Seconds;
        public long Nanoseconds;
    }

    private static string FormatPathCollisionMessage(
        string leftLabel,
        string leftPath,
        string rightLabel,
        string rightPath,
        string normalizedPath)
        => "Invalid path configuration: "
            + $"{leftLabel} ('{Path.GetFullPath(leftPath)}') and "
            + $"{rightLabel} ('{Path.GetFullPath(rightPath)}') "
            + "resolve to the same path "
            + $"'{normalizedPath}'. Configure different paths.";
}
