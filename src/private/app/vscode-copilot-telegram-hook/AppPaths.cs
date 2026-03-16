using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;

namespace Hcoona.VsCodeCopilotTelegramHook;

internal static class AppConstants
{
    public const int SchemaVersion = 1;

    public const string CopilotDirectoryName = ".copilot";
    public const string SessionsDirectoryName = "sessions";
    public const string SessionFileName = "notify-session.json";
    public const string TurnFileName = "notify-turn.json";
    public const string SummaryFileName = "notify-summary.json";
    public const string LastSentFileName = "notify-last-sent.json";
    public const string SessionLogFileName = "hook.log";
    public const string UserCommandLogFileName = "user-command.log";
    public const string ManagedHookFileName = "vscode-copilot-telegram-hook.hooks.json";
    public const string ChatHookFilesLocationsSettingName = "chat.hookFilesLocations";

    public const string ManagedHookEnvironmentVariable = "HCOONA_VSCODE_COPILOT_TELEGRAM_HOOK";
    public const string ManagedHookEnvironmentValue = "1";
    public const string ManagedHookEventEnvironmentVariable =
        "HCOONA_VSCODE_COPILOT_TELEGRAM_HOOK_EVENT";

    public const string TelegramBotTokenEnvironmentVariable = "TG_BOT_TOKEN";
    public const string TelegramChatIdEnvironmentVariable = "TG_CHAT_ID";

    public const string SecretPrefix = "copilot/vscode-copilot-telegram-hook";
    public const string TelegramBotTokenSecretName = "telegram-bot-token";
    public const string TelegramChatIdSecretName = "telegram-chat-id";

    public const int MaxTelegramHtmlMessageLength = 3900;
    public const int MaxStopSummaryValidationFailures = 3;
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
            vsCodeSettingsTargets,
            Path.GetFullPath(userLogFilePath));
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

    public static string GetTurnStatePath(string workspacePath, string sessionId)
        => Path.Combine(
            GetSessionDirectoryPath(workspacePath, sessionId),
            AppConstants.TurnFileName);

    public static string GetSummaryStatePath(string workspacePath, string sessionId)
        => Path.Combine(
            GetSessionDirectoryPath(workspacePath, sessionId),
            AppConstants.SummaryFileName);

    public static string GetLastSentStatePath(string workspacePath, string sessionId)
        => Path.Combine(
            GetSessionDirectoryPath(workspacePath, sessionId),
            AppConstants.LastSentFileName);

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
        => GetRelativeSessionFilePath(sessionId, AppConstants.TurnFileName);

    public static string GetRelativeSummaryStatePath(string sessionId)
        => GetRelativeSessionFilePath(sessionId, AppConstants.SummaryFileName);

    private static string GetRelativeSessionFilePath(string sessionId, string fileName)
    {
        return string.Join(
            '/',
            [
                AppConstants.CopilotDirectoryName,
                AppConstants.SessionsDirectoryName,
                GetSessionDirectoryName(sessionId),
                fileName,
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
        HashSet<string> seenPaths = new(
            OperatingSystem.IsWindows()
                ? StringComparer.OrdinalIgnoreCase
                : StringComparer.Ordinal);
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
}
