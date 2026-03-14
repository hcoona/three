using System.Runtime.InteropServices;

namespace Hcoona.VsCodeCopilotTelegramHook;

internal static class AppConstants
{
    public const int SchemaVersion = 1;

    public const string CopilotDirectoryName = ".copilot";
    public const string SessionFileName = "notify-session.json";
    public const string SummaryFileName = "notify-summary.json";
    public const string LastSentFileName = "notify-last-sent.json";

    public const string ManagedInstructionLogicalName =
        "CopilotNotifySummaryInstruction";
    public const string ManagedInstructionFileName = "copilot-notify-summary.instructions.md";
    public const string ManagedInstructionMarker =
        "<!-- managed-by: hcoona-vscode-copilot-telegram-hook -->";

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

    public static string GetDefaultHookSettingsPath()
    {
        return Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
            ".claude",
            "settings.json");
    }

    public static string GetDefaultInstructionsDirectory()
    {
        return Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
            ".copilot",
            "instructions");
    }

    public static UserInstallationPaths ResolveUserPaths(UserPathOverrides overrides)
    {
        string installRoot = overrides.InstallRoot?.FullName ?? GetDefaultInstallRoot();
        string hookSettingsPath =
            overrides.HookSettingsPath?.FullName ?? GetDefaultHookSettingsPath();
        string instructionsDirectory =
            overrides.InstructionsDirectory?.FullName
            ?? GetDefaultInstructionsDirectory();
        string installedBinaryPath = Path.Combine(installRoot, GetManagedExecutableName());
        string instructionFilePath = Path.Combine(
            instructionsDirectory,
            AppConstants.ManagedInstructionFileName);

        return new UserInstallationPaths(
            Path.GetFullPath(installRoot),
            Path.GetFullPath(installedBinaryPath),
            Path.GetFullPath(hookSettingsPath),
            Path.GetFullPath(instructionsDirectory),
            Path.GetFullPath(instructionFilePath));
    }

    public static string GetManagedExecutableName()
        => OperatingSystem.IsWindows()
            ? "vscode-copilot-telegram-hook.exe"
            : "vscode-copilot-telegram-hook";

    public static string GetWorkspaceCopilotDirectory(string workspacePath)
        => Path.Combine(workspacePath, AppConstants.CopilotDirectoryName);

    public static string GetSessionStatePath(string workspacePath)
        => Path.Combine(GetWorkspaceCopilotDirectory(workspacePath), AppConstants.SessionFileName);

    public static string GetSummaryStatePath(string workspacePath)
        => Path.Combine(GetWorkspaceCopilotDirectory(workspacePath), AppConstants.SummaryFileName);

    public static string GetLastSentStatePath(string workspacePath)
        => Path.Combine(GetWorkspaceCopilotDirectory(workspacePath), AppConstants.LastSentFileName);

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
}
