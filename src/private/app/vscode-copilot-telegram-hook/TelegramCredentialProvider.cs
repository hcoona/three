using Hcoona.VsCodeCopilotTelegramHook.Logging;
using Microsoft.Extensions.Logging;

namespace Hcoona.VsCodeCopilotTelegramHook;

internal sealed class TelegramCredentialProvider(
    IProcessRunner processRunner,
    IInteractiveConsole interactiveConsole,
    ILogger<TelegramCredentialProvider> logger)
{
    private const int MaxGopassErrorLength = 300;

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

    public async Task<StoredTelegramSecrets> ReadStoredSecretsAsync(
        CancellationToken cancellationToken)
    {
        await EnsureSecretStoreAvailableAsync(cancellationToken);
        return await ReadStoredSecretsCoreAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<string>> StoreForInstallAsync(
        string? botTokenOption,
        string? chatIdOption,
        bool skipPrompt,
        CancellationToken cancellationToken)
    {
        await EnsureSecretStoreAvailableAsync(cancellationToken);

        StoredTelegramSecrets existingSecrets = await ReadStoredSecretsCoreAsync(
            cancellationToken);
        bool canPrompt = !skipPrompt && interactiveConsole.CanPrompt;

        SecretInstallDecision botTokenDecision = DetermineInstallDecision(
            "Telegram bot token",
            "Telegram bot token: ",
            isSensitive: true,
            existingSecrets.BotToken,
            NullIfWhitespace(botTokenOption)
                ?? NullIfWhitespace(
                    Environment.GetEnvironmentVariable(
                        AppConstants.TelegramBotTokenEnvironmentVariable)),
            canPrompt);

        SecretInstallDecision chatIdDecision = DetermineInstallDecision(
            "Telegram chat id",
            "Telegram chat id: ",
            isSensitive: false,
            existingSecrets.ChatId,
            NullIfWhitespace(chatIdOption)
                ?? NullIfWhitespace(
                    Environment.GetEnvironmentVariable(
                        AppConstants.TelegramChatIdEnvironmentVariable)),
            canPrompt);

        if (string.IsNullOrWhiteSpace(botTokenDecision.Value)
            || string.IsNullOrWhiteSpace(chatIdDecision.Value))
        {
            AppLog.MissingCredentialInput(logger);
            throw new InvalidOperationException(
                "Both the Telegram bot token and chat id are required. Pass them explicitly, "
                + "set TG_BOT_TOKEN and TG_CHAT_ID, or allow interactive prompts.");
        }

        bool storedAny = false;

        if (botTokenDecision.ShouldStore)
        {
            await StoreSecretAsync(
                AppPaths.GetTelegramBotTokenSecretPath(),
                botTokenDecision.Value,
                cancellationToken);
            storedAny = true;
        }

        if (chatIdDecision.ShouldStore)
        {
            await StoreSecretAsync(
                AppPaths.GetTelegramChatIdSecretPath(),
                chatIdDecision.Value,
                cancellationToken);
            storedAny = true;
        }

        if (storedAny)
        {
            AppLog.StoredTelegramCredentials(logger);
        }
        else
        {
            AppLog.UsingExistingTelegramCredentials(logger);
        }

        return [botTokenDecision.Message, chatIdDecision.Message];
    }

    public async Task<IReadOnlyList<string>> SetStoredSecretsAsync(
        string? botTokenOption,
        string? chatIdOption,
        bool promptForMissing,
        CancellationToken cancellationToken)
    {
        await EnsureSecretStoreAvailableAsync(cancellationToken);

        bool canPrompt = promptForMissing && interactiveConsole.CanPrompt;
        string? botToken = NullIfWhitespace(botTokenOption);
        string? chatId = NullIfWhitespace(chatIdOption);

        if (string.IsNullOrWhiteSpace(botToken) && canPrompt)
        {
            botToken = NullIfWhitespace(interactiveConsole.ReadSecret("Telegram bot token: "));
        }

        if (string.IsNullOrWhiteSpace(chatId) && canPrompt)
        {
            chatId = NullIfWhitespace(interactiveConsole.ReadLine("Telegram chat id: "));
        }

        List<string> messages = [];

        if (!string.IsNullOrWhiteSpace(botToken))
        {
            await StoreSecretAsync(
                AppPaths.GetTelegramBotTokenSecretPath(),
                botToken,
                cancellationToken);
            messages.Add("Stored Telegram bot token in gopass.");
        }

        if (!string.IsNullOrWhiteSpace(chatId))
        {
            await StoreSecretAsync(
                AppPaths.GetTelegramChatIdSecretPath(),
                chatId,
                cancellationToken);
            messages.Add("Stored Telegram chat id in gopass.");
        }

        if (messages.Count == 0)
        {
            AppLog.MissingCredentialInput(logger);
            throw new InvalidOperationException(
                "No secret values were supplied. Pass --telegram-bot-token, "
                + "--telegram-chat-id, or --prompt.");
        }

        AppLog.StoredTelegramCredentials(logger);
        return messages;
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

    public async Task<bool> RemoveStoredSecretsAsync(CancellationToken cancellationToken)
    {
        if (!await IsSecretStoreAvailableAsync(cancellationToken))
        {
            AppLog.SkippingSecretRemoval(logger);
            return false;
        }

        bool botTokenRemoved = await TryRemoveSecretAsync(
            AppPaths.GetTelegramBotTokenSecretPath(),
            cancellationToken);
        bool chatIdRemoved = await TryRemoveSecretAsync(
            AppPaths.GetTelegramChatIdSecretPath(),
            cancellationToken);
        if (botTokenRemoved && chatIdRemoved)
        {
            AppLog.RemovedTelegramCredentials(logger);
        }

        return botTokenRemoved && chatIdRemoved;
    }

    private async Task EnsureSecretStoreAvailableAsync(CancellationToken cancellationToken)
    {
        if (!await IsSecretStoreAvailableAsync(cancellationToken))
        {
            throw new InvalidOperationException(
                "gopass is required for user-level installation but is not available on PATH.");
        }
    }

    private async Task<StoredTelegramSecrets> ReadStoredSecretsCoreAsync(
        CancellationToken cancellationToken)
    {
        string? botToken = await TryReadSecretAsync(
            AppPaths.GetTelegramBotTokenSecretPath(),
            cancellationToken,
            failOnReadError: true);
        string? chatId = await TryReadSecretAsync(
            AppPaths.GetTelegramChatIdSecretPath(),
            cancellationToken,
            failOnReadError: true);

        return new StoredTelegramSecrets(botToken, chatId);
    }

    private async Task<string?> TryReadSecretAsync(
        string secretPath,
        CancellationToken cancellationToken,
        bool failOnReadError = false)
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
                if (IsMissingSecretError(result.StandardError))
                {
                    AppLog.MissingSecretValue(logger, secretPath);
                    return null;
                }

                if (failOnReadError)
                {
                    throw CreateReadSecretException(secretPath, result);
                }

                AppLog.ReadSecretFailed(
                    logger,
                    CreateReadSecretException(secretPath, result),
                    secretPath);
                return null;
            }

            return result.Succeeded ? NullIfWhitespace(result.StandardOutput.Trim()) : null;
        }
        catch (InvalidOperationException ex)
        {
            if (failOnReadError)
            {
                throw;
            }

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

    private async Task<bool> TryRemoveSecretAsync(
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
            if (result.Succeeded || IsMissingSecretError(result.StandardError))
            {
                return true;
            }

            AppLog.RemoveSecretFailed(
                logger,
                new InvalidOperationException(result.StandardError.Trim()),
                secretPath);
            return false;
        }
        catch (InvalidOperationException ex)
        {
            AppLog.RemoveSecretFailed(logger, ex, secretPath);
            return false;
        }
    }

    private static bool IsMissingSecretError(string standardError)
        => standardError.Contains("not found", StringComparison.OrdinalIgnoreCase)
            || standardError.Contains(
                "not in the password store",
                StringComparison.OrdinalIgnoreCase)
            || standardError.Contains("does not exist", StringComparison.OrdinalIgnoreCase);

    private static InvalidOperationException CreateReadSecretException(
        string secretPath,
        ProcessExecutionResult result)
    {
        string standardError = result.StandardError.Trim();
        if (standardError.Length > MaxGopassErrorLength)
        {
            standardError = standardError[..MaxGopassErrorLength] + "...";
        }

        string detail = string.IsNullOrWhiteSpace(standardError)
            ? $"exit code {result.ExitCode}"
            : standardError;
        return new InvalidOperationException(
            $"Failed to read secret '{secretPath}': {detail}");
    }

    private SecretInstallDecision DetermineInstallDecision(
        string displayName,
        string prompt,
        bool isSensitive,
        string? existingValue,
        string? providedValue,
        bool canPrompt)
    {
        string normalizedDisplayName = displayName.ToLowerInvariant();
        string? normalizedExistingValue = NullIfWhitespace(existingValue);
        string? normalizedProvidedValue = NullIfWhitespace(providedValue);

        if (!string.IsNullOrWhiteSpace(normalizedExistingValue))
        {
            if (!string.IsNullOrWhiteSpace(normalizedProvidedValue)
                && string.Equals(
                    normalizedExistingValue,
                    normalizedProvidedValue,
                    StringComparison.Ordinal))
            {
                return new(
                    normalizedExistingValue,
                    ShouldStore: false,
                    $"{displayName} already matches the stored value in gopass.");
            }

            if (!string.IsNullOrWhiteSpace(normalizedProvidedValue))
            {
                if (canPrompt
                    && interactiveConsole.Confirm(
                        $"{displayName} is already stored. Overwrite it?",
                        defaultAnswer: false))
                {
                    return new(
                        normalizedProvidedValue,
                        ShouldStore: true,
                        $"Stored {normalizedDisplayName} in gopass.");
                }

                return new(
                    normalizedExistingValue,
                    ShouldStore: false,
                    canPrompt
                        ? $"Kept existing {normalizedDisplayName} in gopass."
                        : $"Kept existing {normalizedDisplayName} in gopass; "
                        + "install defaults to not overwriting stored secrets "
                        + "when prompts are disabled.");
            }

            if (canPrompt
                && interactiveConsole.Confirm(
                    $"{displayName} is already stored. Overwrite it?",
                    defaultAnswer: false))
            {
                string? promptedValue = ReadInteractiveValue(prompt, isSensitive);
                if (!string.IsNullOrWhiteSpace(promptedValue))
                {
                    return new(
                        promptedValue,
                        ShouldStore: true,
                        $"Stored {normalizedDisplayName} in gopass.");
                }
            }

            return new(
                normalizedExistingValue,
                ShouldStore: false,
                $"Kept existing {normalizedDisplayName} in gopass.");
        }

        string? resolvedValue = normalizedProvidedValue;
        if (string.IsNullOrWhiteSpace(resolvedValue) && canPrompt)
        {
            resolvedValue = ReadInteractiveValue(prompt, isSensitive);
        }

        resolvedValue = NullIfWhitespace(resolvedValue);
        return new(
            resolvedValue,
            ShouldStore: !string.IsNullOrWhiteSpace(resolvedValue),
            $"Stored {normalizedDisplayName} in gopass.");
    }

    private string ReadInteractiveValue(string prompt, bool isSensitive)
        => isSensitive
            ? interactiveConsole.ReadSecret(prompt)
            : interactiveConsole.ReadLine(prompt);

    private static string? NullIfWhitespace(string? value)
        => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private sealed record SecretInstallDecision(string? Value, bool ShouldStore, string Message);
}
