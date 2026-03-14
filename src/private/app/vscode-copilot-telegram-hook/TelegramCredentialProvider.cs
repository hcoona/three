using System.Text;
using Hcoona.VsCodeCopilotTelegramHook.Logging;
using Microsoft.Extensions.Logging;

namespace Hcoona.VsCodeCopilotTelegramHook;

internal sealed class TelegramCredentialProvider(
    ProcessRunner processRunner,
    ILogger<TelegramCredentialProvider> logger)
{
    private static readonly ProcessLogOptions SensitiveProcessLogOptions = new(
        IncludeArgumentsInLogs: false,
        IncludeWorkingDirectoryInLogs: false,
        IncludeStandardErrorInLogs: false);

    public async Task<TelegramCredentials> ResolveAsync(CancellationToken cancellationToken)
        => await ResolveCoreAsync(logMissingCredentials: true, cancellationToken)
            ?? throw new InvalidOperationException(
                "Telegram credential resolution unexpectedly returned null.");

    public Task<TelegramCredentials?> TryResolveAsync(CancellationToken cancellationToken)
        => ResolveCoreAsync(logMissingCredentials: false, cancellationToken);

    private async Task<TelegramCredentials?> ResolveCoreAsync(
        bool logMissingCredentials,
        CancellationToken cancellationToken)
    {
        string? botToken = NullIfWhitespace(
            Environment.GetEnvironmentVariable(AppConstants.TelegramBotTokenEnvironmentVariable));
        string? chatId = NullIfWhitespace(
            Environment.GetEnvironmentVariable(AppConstants.TelegramChatIdEnvironmentVariable));

        if (string.IsNullOrWhiteSpace(botToken))
        {
            botToken = await TryReadSecretAsync(
                AppPaths.GetTelegramBotTokenSecretPath(),
                cancellationToken);
        }

        if (string.IsNullOrWhiteSpace(chatId))
        {
            chatId = await TryReadSecretAsync(
                AppPaths.GetTelegramChatIdSecretPath(),
                cancellationToken);
        }

        if (string.IsNullOrWhiteSpace(botToken) || string.IsNullOrWhiteSpace(chatId))
        {
            if (logMissingCredentials)
            {
                AppLog.MissingTelegramCredentials(logger);
                throw new InvalidOperationException(
                    "Telegram credentials are missing. Set TG_BOT_TOKEN and TG_CHAT_ID or store "
                    + "them with the user install command.");
            }

            return null;
        }

        bool hasEnvironmentOverride =
            Environment.GetEnvironmentVariable(AppConstants.TelegramBotTokenEnvironmentVariable)
                is not null
            || Environment.GetEnvironmentVariable(AppConstants.TelegramChatIdEnvironmentVariable)
                is not null;

        string source = hasEnvironmentOverride
            ? "environment"
            : "gopass";
        AppLog.ResolvedTelegramCredentials(logger, source);

        return new TelegramCredentials(botToken, chatId, source);
    }

    public async Task<bool> IsSecretStoreAvailableAsync(CancellationToken cancellationToken)
    {
        try
        {
            ProcessExecutionResult result = await processRunner.RunAsync(
                "gopass",
                ["version"],
                workingDirectory: null,
                standardInput: null,
                logOptions: null,
                cancellationToken);

            AppLog.GopassAvailabilityChecked(logger, result.ExitCode);
            return result.Succeeded;
        }
        catch (InvalidOperationException ex)
        {
            AppLog.GopassUnavailable(logger, ex);
            return false;
        }
    }

    public async Task StoreAsync(
        string? botTokenOption,
        string? chatIdOption,
        bool skipPrompt,
        CancellationToken cancellationToken)
    {
        string? botToken = NullIfWhitespace(botTokenOption)
            ?? NullIfWhitespace(
                Environment.GetEnvironmentVariable(
                    AppConstants.TelegramBotTokenEnvironmentVariable));

        string? chatId = NullIfWhitespace(chatIdOption)
            ?? NullIfWhitespace(
                Environment.GetEnvironmentVariable(AppConstants.TelegramChatIdEnvironmentVariable));

        bool canPrompt = !skipPrompt && !Console.IsInputRedirected;

        if (string.IsNullOrWhiteSpace(botToken) && canPrompt)
        {
            botToken = NullIfWhitespace(ReadSecretFromConsole("Telegram bot token: "));
        }

        if (string.IsNullOrWhiteSpace(chatId) && canPrompt)
        {
            chatId = NullIfWhitespace(ReadLineFromConsole("Telegram chat id: "));
        }

        if (string.IsNullOrWhiteSpace(botToken) || string.IsNullOrWhiteSpace(chatId))
        {
            AppLog.MissingCredentialInput(logger);
            throw new InvalidOperationException(
                "Both the Telegram bot token and chat id are required. Pass them explicitly, "
                + "set TG_BOT_TOKEN and TG_CHAT_ID, or allow interactive prompts.");
        }

        if (!await IsSecretStoreAvailableAsync(cancellationToken))
        {
            throw new InvalidOperationException(
                "gopass is required for user-level installation but is not available on PATH.");
        }

        await StoreSecretAsync(
            AppPaths.GetTelegramBotTokenSecretPath(),
            botToken,
            cancellationToken);
        await StoreSecretAsync(
            AppPaths.GetTelegramChatIdSecretPath(),
            chatId,
            cancellationToken);
        AppLog.StoredTelegramCredentials(logger);
    }

    public async Task RemoveStoredSecretsAsync(CancellationToken cancellationToken)
    {
        if (!await IsSecretStoreAvailableAsync(cancellationToken))
        {
            AppLog.SkippingSecretRemoval(logger);
            return;
        }

        await TryRemoveSecretAsync(AppPaths.GetTelegramBotTokenSecretPath(), cancellationToken);
        await TryRemoveSecretAsync(AppPaths.GetTelegramChatIdSecretPath(), cancellationToken);
        AppLog.RemovedTelegramCredentials(logger);
    }

    private async Task<string?> TryReadSecretAsync(
        string secretPath,
        CancellationToken cancellationToken)
    {
        try
        {
            ProcessExecutionResult result = await processRunner.RunAsync(
                "gopass",
                ["show", secretPath],
                workingDirectory: null,
                standardInput: null,
                SensitiveProcessLogOptions,
                cancellationToken);

            if (!result.Succeeded)
            {
                AppLog.MissingSecretValue(logger, secretPath);
            }

            return result.Succeeded ? NullIfWhitespace(result.StandardOutput.Trim()) : null;
        }
        catch (InvalidOperationException ex)
        {
            AppLog.ReadSecretFailed(logger, ex, secretPath);
            return null;
        }
    }

    private async Task StoreSecretAsync(
        string secretPath,
        string value,
        CancellationToken cancellationToken)
    {
        ProcessExecutionResult result = await processRunner.RunAsync(
            "gopass",
            ["insert", "-f", "-m", secretPath],
            workingDirectory: null,
            standardInput: value + Environment.NewLine,
            SensitiveProcessLogOptions,
            cancellationToken);

        if (!result.Succeeded)
        {
            AppLog.StoreSecretFailed(logger, secretPath, result.StandardError.Trim());
            throw new InvalidOperationException(
                $"Failed to store secret '{secretPath}': {result.StandardError.Trim()}");
        }
    }

    private async Task TryRemoveSecretAsync(
        string secretPath,
        CancellationToken cancellationToken)
    {
        try
        {
            ProcessExecutionResult result = await processRunner.RunAsync(
                "gopass",
                ["rm", "-f", secretPath],
                workingDirectory: null,
                standardInput: null,
                SensitiveProcessLogOptions,
                cancellationToken);
            AppLog.SecretRemovalCompleted(logger, secretPath, result.ExitCode);
        }
        catch (InvalidOperationException ex)
        {
            AppLog.RemoveSecretFailed(logger, ex, secretPath);
        }
    }

    private static string ReadSecretFromConsole(string prompt)
    {
        Console.Write(prompt);

        StringBuilder builder = new();
        while (true)
        {
            ConsoleKeyInfo key = Console.ReadKey(intercept: true);
            if (key.Key == ConsoleKey.Enter)
            {
                Console.WriteLine();
                break;
            }

            if (key.Key == ConsoleKey.Backspace)
            {
                if (builder.Length > 0)
                {
                    builder.Length -= 1;
                    Console.Write("\b \b");
                }

                continue;
            }

            if (!char.IsControl(key.KeyChar))
            {
                builder.Append(key.KeyChar);
                Console.Write('*');
            }
        }

        return builder.ToString();
    }

    private static string ReadLineFromConsole(string prompt)
    {
        Console.Write(prompt);
        return Console.ReadLine() ?? string.Empty;
    }

    private static string? NullIfWhitespace(string? value)
        => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
