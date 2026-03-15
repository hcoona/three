using System.CommandLine;
using Microsoft.Extensions.DependencyInjection;

namespace Hcoona.VsCodeCopilotTelegramHook.Commands;

internal static class CliFactory
{
    public static RootCommand CreateRootCommand(IServiceProvider services)
    {
        HookCommandService hookCommandService = services.GetRequiredService<HookCommandService>();
        UserCommandService userCommandService = services.GetRequiredService<UserCommandService>();

        RootCommand rootCommand = new("VS Code Copilot Telegram hook tool.");
        rootCommand.Subcommands.Add(CreateHookCommand(hookCommandService));
        rootCommand.Subcommands.Add(CreateUserCommand(userCommandService));
        return rootCommand;
    }

    private static Command CreateHookCommand(HookCommandService hookCommandService)
    {
        Command hookCommand = new("hook", "Run hook lifecycle commands.");

        Command sessionStartCommand = new(
            "session-start",
            "Handle the VS Code SessionStart hook event.");
        sessionStartCommand.SetAction((ParseResult _, CancellationToken cancellationToken) =>
            hookCommandService.HandleSessionStartAsync(
                Console.OpenStandardInput(),
                Console.OpenStandardOutput(),
                cancellationToken));

        Command userPromptSubmitCommand = new(
            "user-prompt-submit",
            "Handle the VS Code UserPromptSubmit hook event.");
        userPromptSubmitCommand.SetAction((ParseResult _, CancellationToken cancellationToken) =>
            hookCommandService.HandleUserPromptSubmitAsync(
                Console.OpenStandardInput(),
                cancellationToken));

        Command stopCommand = new(
            "stop",
            "Handle the VS Code Stop hook event.");
        stopCommand.SetAction((ParseResult _, CancellationToken cancellationToken) =>
            hookCommandService.HandleStopAsync(Console.OpenStandardInput(), cancellationToken));

        hookCommand.Subcommands.Add(sessionStartCommand);
        hookCommand.Subcommands.Add(userPromptSubmitCommand);
        hookCommand.Subcommands.Add(stopCommand);
        return hookCommand;
    }

    private static Command CreateUserCommand(UserCommandService userCommandService)
    {
        Command userCommand = new("user", "Manage the user-level installation and diagnostics.");

        Option<FileInfo?> binaryPathOption = new("--binary-path")
        {
            Description =
                "Path to the published native executable to install. Defaults to the current "
                + "executable.",
        };
        binaryPathOption.Validators.Add(result =>
        {
            FileInfo? fileInfo = result.GetValue(binaryPathOption);
            if (fileInfo is not null && !fileInfo.Exists)
            {
                result.AddError("The --binary-path value must point to an existing file.");
            }
        });

        Option<string?> telegramBotTokenOption = new("--telegram-bot-token")
        {
            Description =
                "Telegram bot token to persist in gopass when the secret "
                + "is missing or overwrite is confirmed.",
        };

        Option<string?> telegramChatIdOption = new("--telegram-chat-id")
        {
            Description =
                "Telegram chat id to persist in gopass when the secret "
                + "is missing or overwrite is confirmed.",
        };

        Option<bool> skipSecretPromptOption = new("--skip-secret-prompt")
        {
            Description =
                "Do not prompt for missing values or overwrite "
                + "confirmation; keep existing stored secrets when present.",
        };

        Option<bool> promptOption = new("--prompt")
        {
            Description =
                "Prompt interactively for any secret values not provided "
                + "explicitly when updating stored secrets.",
        };

        Option<DirectoryInfo?> installRootOption = new("--install-root")
        {
            Description = "Override the user-level installation root directory.",
        };

        Option<FileInfo?> hookFilePathOption = new("--hook-file-path")
        {
            Description = "Override the managed hook JSON file path.",
        };

        Option<FileInfo?> vsCodeSettingsPathOption = new("--vscode-settings-path")
        {
            Description =
                "Override the VS Code user settings.json path used for "
                + "chat.hookFilesLocations registration.",
        };

        Option<DirectoryInfo?> instructionsDirectoryOption = new("--instructions-dir")
        {
            Description = "Override the user instructions directory.",
        };

        Option<bool> removeSecretsOption = new("--remove-secrets")
        {
            Description = "Also remove the stored Telegram secrets from gopass during uninstall.",
        };

        Option<string?> messageOption = new("--message")
        {
            Description = "Optional Chinese text to include in the test notification summary.",
        };

        Command installCommand = new(
            "install",
            "Install the user-level hook configuration, instructions, and binary.")
        {
            binaryPathOption,
            telegramBotTokenOption,
            telegramChatIdOption,
            skipSecretPromptOption,
            installRootOption,
            hookFilePathOption,
            vsCodeSettingsPathOption,
            instructionsDirectoryOption,
        };
        installCommand.SetAction((ParseResult parseResult, CancellationToken cancellationToken) =>
            userCommandService.InstallAsync(
                new InstallCommandOptions
                {
                    BinaryPath = parseResult.GetValue(binaryPathOption),
                    TelegramBotToken = parseResult.GetValue(telegramBotTokenOption),
                    TelegramChatId = parseResult.GetValue(telegramChatIdOption),
                    SkipSecretPrompt = parseResult.GetValue(skipSecretPromptOption),
                    InstallRoot = parseResult.GetValue(installRootOption),
                    ManagedHookFilePath = parseResult.GetValue(hookFilePathOption),
                    VsCodeSettingsPath = parseResult.GetValue(vsCodeSettingsPathOption),
                    InstructionsDirectory = parseResult.GetValue(instructionsDirectoryOption),
                },
                cancellationToken));

        Command uninstallCommand = new(
            "uninstall",
            "Remove the managed user-level hook configuration, instructions, and binary.")
        {
            removeSecretsOption,
            installRootOption,
            hookFilePathOption,
            vsCodeSettingsPathOption,
            instructionsDirectoryOption,
        };
        uninstallCommand.SetAction((ParseResult parseResult, CancellationToken cancellationToken) =>
            userCommandService.UninstallAsync(
                new UninstallCommandOptions
                {
                    RemoveSecrets = parseResult.GetValue(removeSecretsOption),
                    InstallRoot = parseResult.GetValue(installRootOption),
                    ManagedHookFilePath = parseResult.GetValue(hookFilePathOption),
                    VsCodeSettingsPath = parseResult.GetValue(vsCodeSettingsPathOption),
                    InstructionsDirectory = parseResult.GetValue(instructionsDirectoryOption),
                },
                cancellationToken));

        Command healthCommand = new(
            "health",
            "Validate the current user-level installation and secret resolution.")
        {
            installRootOption,
            hookFilePathOption,
            vsCodeSettingsPathOption,
            instructionsDirectoryOption,
        };
        healthCommand.SetAction((ParseResult parseResult, CancellationToken cancellationToken) =>
            userCommandService.HealthAsync(
                new UserPathOverrides
                {
                    InstallRoot = parseResult.GetValue(installRootOption),
                    ManagedHookFilePath = parseResult.GetValue(hookFilePathOption),
                    VsCodeSettingsPath = parseResult.GetValue(vsCodeSettingsPathOption),
                    InstructionsDirectory = parseResult.GetValue(instructionsDirectoryOption),
                },
                cancellationToken));

        Command diagnoseCommand = new(
            "diagnose",
            "Print a detailed diagnostic report for the current user-level installation.")
        {
            installRootOption,
            hookFilePathOption,
            vsCodeSettingsPathOption,
            instructionsDirectoryOption,
        };
        diagnoseCommand.SetAction((ParseResult parseResult, CancellationToken cancellationToken) =>
            userCommandService.DiagnoseAsync(
                new UserPathOverrides
                {
                    InstallRoot = parseResult.GetValue(installRootOption),
                    ManagedHookFilePath = parseResult.GetValue(hookFilePathOption),
                    VsCodeSettingsPath = parseResult.GetValue(vsCodeSettingsPathOption),
                    InstructionsDirectory = parseResult.GetValue(instructionsDirectoryOption),
                },
                cancellationToken));

        Command secretCommand = new(
            "secret",
            "Read or update the stored Telegram secrets.")
        {
            telegramBotTokenOption,
            telegramChatIdOption,
            promptOption,
            installRootOption,
        };
        secretCommand.SetAction((ParseResult parseResult, CancellationToken cancellationToken) =>
            userCommandService.SecretAsync(
                new SecretCommandOptions
                {
                    TelegramBotToken = parseResult.GetValue(telegramBotTokenOption),
                    TelegramChatId = parseResult.GetValue(telegramChatIdOption),
                    Prompt = parseResult.GetValue(promptOption),
                    InstallRoot = parseResult.GetValue(installRootOption),
                },
                cancellationToken));

        Command testNotificationCommand = new(
            "test-notification",
            "Send a test Telegram notification using the configured credentials.")
        {
            messageOption,
            installRootOption,
            hookFilePathOption,
            vsCodeSettingsPathOption,
            instructionsDirectoryOption,
        };
        testNotificationCommand.SetAction(
            (ParseResult parseResult, CancellationToken cancellationToken) =>
            userCommandService.TestNotificationAsync(
                new TestNotificationCommandOptions
                {
                    Message = parseResult.GetValue(messageOption),
                    InstallRoot = parseResult.GetValue(installRootOption),
                    ManagedHookFilePath = parseResult.GetValue(hookFilePathOption),
                    VsCodeSettingsPath = parseResult.GetValue(vsCodeSettingsPathOption),
                    InstructionsDirectory = parseResult.GetValue(instructionsDirectoryOption),
                },
                cancellationToken));

        userCommand.Subcommands.Add(installCommand);
        userCommand.Subcommands.Add(uninstallCommand);
        userCommand.Subcommands.Add(healthCommand);
        userCommand.Subcommands.Add(diagnoseCommand);
        userCommand.Subcommands.Add(secretCommand);
        userCommand.Subcommands.Add(testNotificationCommand);

        return userCommand;
    }
}
