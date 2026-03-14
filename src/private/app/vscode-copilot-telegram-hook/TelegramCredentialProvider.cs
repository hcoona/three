using System.Text;

namespace Hcoona.VsCodeCopilotTelegramHook;

internal static class TelegramCredentialProvider
{
    public static async Task<TelegramCredentials> ResolveAsync(CancellationToken cancellationToken)
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
            throw new InvalidOperationException(
                "Telegram credentials are missing. Set TG_BOT_TOKEN and TG_CHAT_ID or store "
                + "them with the user install command.");
        }

        bool hasEnvironmentOverride =
            Environment.GetEnvironmentVariable(AppConstants.TelegramBotTokenEnvironmentVariable)
                is not null
            || Environment.GetEnvironmentVariable(AppConstants.TelegramChatIdEnvironmentVariable)
                is not null;

        string source = hasEnvironmentOverride
            ? "environment"
            : "gopass";

        return new TelegramCredentials(botToken, chatId, source);
    }

    public static async Task<bool> IsSecretStoreAvailableAsync(CancellationToken cancellationToken)
    {
        try
        {
            ProcessExecutionResult result = await ProcessRunner.RunAsync(
                "gopass",
                ["version"],
                workingDirectory: null,
                standardInput: null,
                cancellationToken);

            return result.Succeeded;
        }
        catch (InvalidOperationException)
        {
            return false;
        }
    }

    public static async Task StoreAsync(
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
    }

    public static async Task RemoveStoredSecretsAsync(CancellationToken cancellationToken)
    {
        if (!await IsSecretStoreAvailableAsync(cancellationToken))
        {
            return;
        }

        await TryRemoveSecretAsync(AppPaths.GetTelegramBotTokenSecretPath(), cancellationToken);
        await TryRemoveSecretAsync(AppPaths.GetTelegramChatIdSecretPath(), cancellationToken);
    }

    private static async Task<string?> TryReadSecretAsync(
        string secretPath,
        CancellationToken cancellationToken)
    {
        try
        {
            ProcessExecutionResult result = await ProcessRunner.RunAsync(
                "gopass",
                ["show", secretPath],
                workingDirectory: null,
                standardInput: null,
                cancellationToken);

            return result.Succeeded ? NullIfWhitespace(result.StandardOutput.Trim()) : null;
        }
        catch (InvalidOperationException)
        {
            return null;
        }
    }

    private static async Task StoreSecretAsync(
        string secretPath,
        string value,
        CancellationToken cancellationToken)
    {
        ProcessExecutionResult result = await ProcessRunner.RunAsync(
            "gopass",
            ["insert", "-f", "-m", secretPath],
            workingDirectory: null,
            standardInput: value + Environment.NewLine,
            cancellationToken);

        if (!result.Succeeded)
        {
            throw new InvalidOperationException(
                $"Failed to store secret '{secretPath}': {result.StandardError.Trim()}");
        }
    }

    private static async Task TryRemoveSecretAsync(
        string secretPath,
        CancellationToken cancellationToken)
    {
        try
        {
            await ProcessRunner.RunAsync(
                "gopass",
                ["rm", "-f", secretPath],
                workingDirectory: null,
                standardInput: null,
                cancellationToken);
        }
        catch (InvalidOperationException)
        {
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
