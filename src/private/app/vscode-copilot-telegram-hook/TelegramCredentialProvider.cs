using Hcoona.VsCodeCopilotTelegramHook.Logging;
using Microsoft.Extensions.Logging;

namespace Hcoona.VsCodeCopilotTelegramHook;

internal sealed class TelegramCredentialProvider(
    IProcessRunner processRunner,
    IInteractiveConsole interactiveConsole,
    ILogger<TelegramCredentialProvider> logger)
{
    private const int GopassRemoveNotFoundExitCode = 10;
    private const int GopassShowNotFoundExitCode = 11;
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
        StoredTelegramSecretsTransaction transaction,
        CancellationToken cancellationToken)
    {
        await EnsureSecretStoreAvailableAsync(cancellationToken);

        StoredTelegramSecrets existingSecrets = transaction.Original;
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
            await StoreTrackedSecretAsync(
                AppPaths.GetTelegramBotTokenSecretPath(),
                transaction.ExpectedBotToken,
                botTokenDecision.Value,
                transaction.RecordBotToken,
                cancellationToken);
            storedAny = true;
        }

        if (chatIdDecision.ShouldStore)
        {
            await StoreTrackedSecretAsync(
                AppPaths.GetTelegramChatIdSecretPath(),
                transaction.ExpectedChatId,
                chatIdDecision.Value,
                transaction.RecordChatId,
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

    public async Task RestoreStoredSecretsAsync(
        StoredTelegramSecretsTransaction transaction,
        CancellationToken cancellationToken)
    {
        await EnsureSecretStoreAvailableAsync(cancellationToken);
        List<Exception> failures = [];
        if (transaction.BotTokenMutated)
        {
            await TryRestoreSecretIfUnchangedAsync(
                AppPaths.GetTelegramBotTokenSecretPath(),
                transaction.Original.BotToken,
                transaction.ExpectedBotToken,
                failures,
                cancellationToken);
        }

        if (transaction.ChatIdMutated)
        {
            await TryRestoreSecretIfUnchangedAsync(
                AppPaths.GetTelegramChatIdSecretPath(),
                transaction.Original.ChatId,
                transaction.ExpectedChatId,
                failures,
                cancellationToken);
        }

        if (failures.Count > 0)
        {
            throw new InvalidOperationException(
                "One or more Telegram secrets could not be restored.",
                new AggregateException(failures));
        }
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

    public async Task RemoveStoredSecretsAsync(
        StoredTelegramSecretsTransaction transaction,
        CancellationToken cancellationToken)
    {
        await EnsureSecretStoreAvailableAsync(cancellationToken);
        await RemoveTrackedSecretAsync(
            AppPaths.GetTelegramBotTokenSecretPath(),
            transaction.ExpectedBotToken,
            transaction.RecordBotToken,
            cancellationToken);
        await RemoveTrackedSecretAsync(
            AppPaths.GetTelegramChatIdSecretPath(),
            transaction.ExpectedChatId,
            transaction.RecordChatId,
            cancellationToken);
        AppLog.RemovedTelegramCredentials(logger);
    }

    private async Task EnsureSecretStoreAvailableAsync(CancellationToken cancellationToken)
    {
        if (!await IsSecretStoreAvailableAsync(cancellationToken))
        {
            throw new InvalidOperationException(
                "gopass is required for user-level installation but is not available on PATH.");
        }
    }

    private async Task RestoreSecretAsync(
        string secretPath,
        string? value,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            await RemoveSecretIfPresentAsync(secretPath, cancellationToken);
            return;
        }

        await StoreSecretAsync(secretPath, value, cancellationToken);
    }

    private async Task TryRestoreSecretIfUnchangedAsync(
        string secretPath,
        string? originalValue,
        string? expectedValue,
        List<Exception> failures,
        CancellationToken cancellationToken)
    {
        try
        {
            await EnsureSecretMatchesAsync(secretPath, expectedValue, cancellationToken);
            await RestoreSecretAsync(secretPath, originalValue, cancellationToken);
        }
        catch (Exception ex) when (ex is InvalidOperationException or IOException)
        {
            failures.Add(ex);
        }
    }

    private async Task RemoveTrackedSecretAsync(
        string secretPath,
        string? expectedValue,
        Action<string?> recordMutation,
        CancellationToken cancellationToken)
    {
        await EnsureSecretMatchesAsync(secretPath, expectedValue, cancellationToken);
        if (expectedValue is null)
        {
            return;
        }

        try
        {
            bool removed = await RemoveSecretIfPresentAsync(secretPath, cancellationToken);
            if (removed)
            {
                recordMutation(null);
            }
        }
        catch
        {
            await RecordSecretMutationIfChangedAsync(
                secretPath,
                expectedValue,
                recordMutation,
                CancellationToken.None);
            throw;
        }
    }

    private async Task StoreTrackedSecretAsync(
        string secretPath,
        string? expectedValue,
        string value,
        Action<string?> recordMutation,
        CancellationToken cancellationToken)
    {
        await EnsureSecretMatchesAsync(secretPath, expectedValue, cancellationToken);
        try
        {
            await StoreSecretAsync(secretPath, value, cancellationToken);
            recordMutation(value);
        }
        catch
        {
            await RecordSecretMutationIfChangedAsync(
                secretPath,
                expectedValue,
                recordMutation,
                CancellationToken.None);
            throw;
        }
    }

    private async Task RecordSecretMutationIfChangedAsync(
        string secretPath,
        string? previousValue,
        Action<string?> recordMutation,
        CancellationToken cancellationToken)
    {
        string? currentValue = await TryReadSecretAsync(secretPath, cancellationToken);
        if (!string.Equals(currentValue, previousValue, StringComparison.Ordinal))
        {
            recordMutation(currentValue);
        }
    }

    private async Task<StoredTelegramSecrets> ReadStoredSecretsCoreAsync(
        CancellationToken cancellationToken)
    {
        string? botToken = await TryReadSecretAsync(
            AppPaths.GetTelegramBotTokenSecretPath(),
            cancellationToken);
        string? chatId = await TryReadSecretAsync(
            AppPaths.GetTelegramChatIdSecretPath(),
            cancellationToken);

        return new StoredTelegramSecrets(botToken, chatId);
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

            if (result.Succeeded)
            {
                return NullIfWhitespace(result.StandardOutput.Trim());
            }

            if (result.ExitCode == GopassShowNotFoundExitCode)
            {
                AppLog.MissingSecretValue(logger, secretPath);
                return null;
            }

            throw new InvalidOperationException(
                $"Failed to read secret '{secretPath}' from gopass "
                + $"(exit code {result.ExitCode}).");
        }
        catch (InvalidOperationException ex)
        {
            AppLog.ReadSecretFailed(logger, ex, secretPath);
            throw;
        }
    }

    private async Task EnsureSecretMatchesAsync(
        string secretPath,
        string? expectedValue,
        CancellationToken cancellationToken)
    {
        string? currentValue = await TryReadSecretAsync(secretPath, cancellationToken);
        if (!string.Equals(currentValue, expectedValue, StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                $"Stored secret '{secretPath}' changed during the managed operation.");
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

    private async Task<bool> RemoveSecretIfPresentAsync(
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
            if (!result.Succeeded && result.ExitCode != GopassRemoveNotFoundExitCode)
            {
                throw new InvalidOperationException(
                    $"Failed to remove secret '{secretPath}' from gopass "
                    + $"(exit code {result.ExitCode}).");
            }

            return result.Succeeded;
        }
        catch (InvalidOperationException ex)
        {
            AppLog.RemoveSecretFailed(logger, ex, secretPath);
            throw;
        }
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
