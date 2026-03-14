using Hcoona.VsCodeCopilotTelegramHook.Logging;
using Hcoona.VsCodeCopilotTelegramHook.Notifications;
using Microsoft.Extensions.Logging;

namespace Hcoona.VsCodeCopilotTelegramHook.Commands;

internal sealed class UserCommandService(
    InstructionTemplateProvider instructionTemplateProvider,
    TelegramBotClient telegramBotClient,
    TelegramCredentialProvider telegramCredentialProvider,
    SessionLogFileContext sessionLogFileContext,
    TimeProvider timeProvider,
    ILogger<UserCommandService> logger)
{
    public async Task<int> InstallAsync(
        InstallCommandOptions options,
        CancellationToken cancellationToken)
    {
        try
        {
            UserInstallationPaths userPaths = AppPaths.ResolveUserPaths(options);
            using IDisposable logScope = sessionLogFileContext.UseLogFile(
                userPaths.UserLogFilePath);
            AppLog.StartingUserInstall(logger);
            string sourceBinaryPath = ResolveInstallableBinaryPath(options.BinaryPath);
            string currentTimestamp = GetCurrentUtcTimestamp();

            IReadOnlyList<string> secretMessages =
                await telegramCredentialProvider.StoreForInstallAsync(
                    options.TelegramBotToken,
                    options.TelegramChatId,
                    options.SkipSecretPrompt,
                    cancellationToken);

            CopyBinary(sourceBinaryPath, userPaths.InstalledBinaryPath);

            string sessionStartCommand = $"\"{userPaths.InstalledBinaryPath}\" hook session-start";
            string userPromptSubmitCommand =
                $"\"{userPaths.InstalledBinaryPath}\" hook user-prompt-submit";
            string stopCommand = $"\"{userPaths.InstalledBinaryPath}\" hook stop";
            ConfigurationApplyResult hooksResult = UserHookConfigurationManager.InstallHooks(
                userPaths.HookSettingsPath,
                sessionStartCommand,
                userPromptSubmitCommand,
                stopCommand,
                currentTimestamp);

            ConfigurationApplyResult instructionResult =
                UserHookConfigurationManager.InstallInstruction(
                userPaths.InstructionFilePath,
                instructionTemplateProvider.GetTemplate(),
                currentTimestamp);

            await Console.Out.WriteLineAsync($"Installed binary: {userPaths.InstalledBinaryPath}");
            await Console.Out.WriteLineAsync(hooksResult.Message);
            await Console.Out.WriteLineAsync(instructionResult.Message);

            foreach (string secretMessage in secretMessages)
            {
                await Console.Out.WriteLineAsync(secretMessage);
            }

            await Console.Out.WriteLineAsync(
                $"Telegram credentials are ready in gopass under '{AppConstants.SecretPrefix}'.");

            if (hooksResult.CandidatePath is not null)
            {
                await Console.Error.WriteLineAsync(
                    $"Hook settings candidate file: {hooksResult.CandidatePath}");
            }

            if (instructionResult.CandidatePath is not null)
            {
                await Console.Error.WriteLineAsync(
                    $"Instruction candidate file: {instructionResult.CandidatePath}");
            }

            bool succeeded = hooksResult.Applied && instructionResult.Applied;
            AppLog.CompletedUserInstall(logger, userPaths.InstallRoot, succeeded);
            return succeeded ? 0 : 1;
        }
        catch (Exception ex)
        {
            AppLog.UserInstallFailed(logger, ex);
            await Console.Error.WriteLineAsync($"Install failed: {ex.Message}");
            return 1;
        }
    }

    public async Task<int> UninstallAsync(
        UninstallCommandOptions options,
        CancellationToken cancellationToken)
    {
        try
        {
            UserInstallationPaths userPaths = AppPaths.ResolveUserPaths(options);
            using IDisposable logScope = sessionLogFileContext.UseLogFile(
                userPaths.UserLogFilePath);
            AppLog.StartingUserUninstall(logger);

            ConfigurationApplyResult hooksResult =
                UserHookConfigurationManager.UninstallHooks(userPaths.HookSettingsPath);
            ConfigurationApplyResult instructionResult =
                UserHookConfigurationManager.UninstallInstruction(userPaths.InstructionFilePath);

            DeleteManagedBinary(userPaths.InstalledBinaryPath);

            if (options.RemoveSecrets)
            {
                await telegramCredentialProvider.RemoveStoredSecretsAsync(cancellationToken);
            }

            await Console.Out.WriteLineAsync(hooksResult.Message);
            await Console.Out.WriteLineAsync(instructionResult.Message);
            await Console.Out.WriteLineAsync(
                $"Removed installed binary if it existed: {userPaths.InstalledBinaryPath}");

            if (options.RemoveSecrets)
            {
                await Console.Out.WriteLineAsync(
                    "Removed stored Telegram secrets from gopass when present.");
            }

            bool succeeded = hooksResult.Applied && instructionResult.Applied;
            AppLog.CompletedUserUninstall(logger, userPaths.InstallRoot, succeeded);
            return succeeded ? 0 : 1;
        }
        catch (Exception ex)
        {
            AppLog.UserUninstallFailed(logger, ex);
            await Console.Error.WriteLineAsync($"Uninstall failed: {ex.Message}");
            return 1;
        }
    }

    public async Task<int> HealthAsync(
        UserPathOverrides options,
        CancellationToken cancellationToken)
    {
        try
        {
            UserInstallationPaths userPaths = AppPaths.ResolveUserPaths(options);
            using IDisposable logScope = sessionLogFileContext.UseLogFile(
                userPaths.UserLogFilePath);
            AppLog.StartingUserHealth(logger);
            bool secretStoreAvailable =
                await telegramCredentialProvider.IsSecretStoreAvailableAsync(cancellationToken);
            bool credentialsAvailable = await TryResolveCredentialsAsync(cancellationToken);
            bool binaryInstalled = File.Exists(userPaths.InstalledBinaryPath);
            bool hooksInstalled =
                UserHookConfigurationManager.IsHookInstalled(userPaths.HookSettingsPath);
            bool instructionInstalled =
                UserHookConfigurationManager.IsInstructionInstalled(userPaths.InstructionFilePath);

            await Console.Out.WriteLineAsync(
                FormatCheck("Installed binary", binaryInstalled, userPaths.InstalledBinaryPath));
            await Console.Out.WriteLineAsync(
                FormatCheck("User hook settings", hooksInstalled, userPaths.HookSettingsPath));
            await Console.Out.WriteLineAsync(
                FormatCheck(
                    "User instructions",
                    instructionInstalled,
                    userPaths.InstructionFilePath));
            await Console.Out.WriteLineAsync(
                FormatCheck("gopass available", secretStoreAvailable, "PATH lookup"));
            await Console.Out.WriteLineAsync(
                FormatCheck(
                    "Telegram credentials",
                    credentialsAvailable,
                    secretStoreAvailable ? "environment or gopass" : "environment only"));

            bool isHealthy = binaryInstalled
                && hooksInstalled
                && instructionInstalled
                && credentialsAvailable;
            AppLog.CompletedUserHealth(logger, userPaths.InstallRoot, isHealthy);
            return isHealthy ? 0 : 1;
        }
        catch (Exception ex)
        {
            AppLog.UserHealthFailed(logger, ex);
            await Console.Error.WriteLineAsync($"Health check failed: {ex.Message}");
            return 1;
        }
    }

    public async Task<int> DiagnoseAsync(
        UserPathOverrides options,
        CancellationToken cancellationToken)
    {
        try
        {
            UserInstallationPaths userPaths = AppPaths.ResolveUserPaths(options);
            using IDisposable logScope = sessionLogFileContext.UseLogFile(
                userPaths.UserLogFilePath);
            AppLog.StartingUserDiagnose(logger);
            string currentProcessPath = Environment.ProcessPath ?? "<unavailable>";
            bool currentBinaryLooksAot = LooksLikeNativeAotBinary(currentProcessPath);
            bool installedBinaryLooksAot = File.Exists(userPaths.InstalledBinaryPath)
                && LooksLikeNativeAotBinary(userPaths.InstalledBinaryPath);

            bool secretStoreAvailable =
                await telegramCredentialProvider.IsSecretStoreAvailableAsync(cancellationToken);
            bool credentialsAvailable = await TryResolveCredentialsAsync(cancellationToken);
            bool managedHooksInstalled = UserHookConfigurationManager.IsHookInstalled(
                userPaths.HookSettingsPath);
            bool managedInstructionInstalled =
                UserHookConfigurationManager.IsInstructionInstalled(userPaths.InstructionFilePath);
            string executionEnvironment = AppPaths.GetExecutionEnvironmentDisplay();
            string workspaceLogPathPattern = AppPaths.GetSessionLogPathPattern(
                Environment.CurrentDirectory);
            string workspaceFallbackLogPath = AppPaths.GetWorkspaceLogPath(
                Environment.CurrentDirectory);

            await Console.Out.WriteLineAsync($"Current executable : {currentProcessPath}");
            await Console.Out.WriteLineAsync(
                $"Current executable looks Native AOT : {currentBinaryLooksAot}");
            await Console.Out.WriteLineAsync($"Install root : {userPaths.InstallRoot}");
            await Console.Out.WriteLineAsync($"Installed binary : {userPaths.InstalledBinaryPath}");
            await Console.Out.WriteLineAsync(
                $"Installed binary looks Native AOT : {installedBinaryLooksAot}");
            await Console.Out.WriteLineAsync($"Hook settings path : {userPaths.HookSettingsPath}");
            await Console.Out.WriteLineAsync(
                $"Instructions directory : {userPaths.InstructionsDirectory}");
            await Console.Out.WriteLineAsync(
                $"Managed instruction file : {userPaths.InstructionFilePath}");
            await Console.Out.WriteLineAsync(
                $"Managed hook entries installed : "
                + $"{managedHooksInstalled}");
            await Console.Out.WriteLineAsync(
                $"Managed instruction installed : {managedInstructionInstalled}");
            await Console.Out.WriteLineAsync($"gopass available : {secretStoreAvailable}");
            await Console.Out.WriteLineAsync(
                $"Telegram credentials resolvable : {credentialsAvailable}");
            await Console.Out.WriteLineAsync(
                $"Execution environment : {executionEnvironment}");
            await Console.Out.WriteLineAsync(
                $"User command log file : {userPaths.UserLogFilePath}");
            await Console.Out.WriteLineAsync(
                $"Workspace session log pattern : {workspaceLogPathPattern}");
            await Console.Out.WriteLineAsync(
                $"Workspace fallback hook log : {workspaceFallbackLogPath}");

            AppLog.CompletedUserDiagnose(logger, userPaths.InstallRoot);
            return 0;
        }
        catch (Exception ex)
        {
            AppLog.UserDiagnoseFailed(logger, ex);
            await Console.Error.WriteLineAsync($"Diagnose failed: {ex.Message}");
            return 1;
        }
    }

    public async Task<int> SecretAsync(
        SecretCommandOptions options,
        CancellationToken cancellationToken)
    {
        try
        {
            UserInstallationPaths userPaths = AppPaths.ResolveUserPaths(options);
            using IDisposable logScope = sessionLogFileContext.UseLogFile(
                userPaths.UserLogFilePath);
            AppLog.StartingUserSecret(logger);

            bool isUpdate = options.Prompt
                || !string.IsNullOrWhiteSpace(options.TelegramBotToken)
                || !string.IsNullOrWhiteSpace(options.TelegramChatId);

            if (isUpdate)
            {
                IReadOnlyList<string> messages = await telegramCredentialProvider
                    .SetStoredSecretsAsync(
                        options.TelegramBotToken,
                        options.TelegramChatId,
                        options.Prompt,
                        cancellationToken);

                foreach (string message in messages)
                {
                    await Console.Out.WriteLineAsync(message);
                }
            }
            else
            {
                StoredTelegramSecrets storedSecrets = await telegramCredentialProvider
                    .ReadStoredSecretsAsync(cancellationToken);
                await Console.Out.WriteLineAsync(
                    $"Stored Telegram bot token: {FormatSecretValue(storedSecrets.BotToken)}");
                await Console.Out.WriteLineAsync(
                    $"Stored Telegram chat id: {FormatSecretValue(storedSecrets.ChatId)}");
            }

            AppLog.CompletedUserSecret(logger, userPaths.InstallRoot, isUpdate);
            return 0;
        }
        catch (Exception ex)
        {
            AppLog.UserSecretFailed(logger, ex);
            await Console.Error.WriteLineAsync($"Secret command failed: {ex.Message}");
            return 1;
        }
    }

    public async Task<int> TestNotificationAsync(
        TestNotificationCommandOptions options,
        CancellationToken cancellationToken)
    {
        try
        {
            UserInstallationPaths userPaths = AppPaths.ResolveUserPaths(options);
            using IDisposable logScope = sessionLogFileContext.UseLogFile(
                userPaths.UserLogFilePath);
            AppLog.StartingTestNotification(logger);
            TelegramCredentials credentials =
                await telegramCredentialProvider.ResolveAsync(cancellationToken);
            string now = GetCurrentUtcTimestamp();
            NotificationContext context = new()
            {
                SessionId = "test-session",
                TurnId = $"test-turn-{Guid.NewGuid():n}",
                StopTimestamp = now,
                SentAt = now,
                WorkspacePath = Environment.CurrentDirectory,
                HostName = Environment.MachineName,
                ExecutionEnvironment = AppPaths.GetExecutionEnvironmentDisplay(),
                RepositoryName = null,
                BranchName = null,
                CommitId = null,
                TranscriptPath = null,
            };

            SummaryRecord summaryRecord = new()
            {
                SessionId = context.SessionId,
                TurnId = context.TurnId,
                UpdatedAt = now,
                Summary = string.IsNullOrWhiteSpace(options.Message)
                    ? "这是一条来自 VS Code Copilot Telegram Hook 的测试通知。"
                    : options.Message.Trim(),
            };

            IReadOnlyList<string> messages = NotificationComposer.Compose(context, summaryRecord);
            await telegramBotClient.SendMessagesAsync(credentials, messages, cancellationToken);
            await Console.Out.WriteLineAsync("Sent a test Telegram notification successfully.");
            AppLog.CompletedTestNotification(logger);
            return 0;
        }
        catch (Exception ex)
        {
            AppLog.TestNotificationFailed(logger, ex);
            await Console.Error.WriteLineAsync($"Test notification failed: {ex.Message}");
            return 1;
        }
    }

    private static string ResolveInstallableBinaryPath(FileInfo? binaryPathOption)
    {
        string? binaryPath = binaryPathOption?.FullName ?? Environment.ProcessPath;
        if (string.IsNullOrWhiteSpace(binaryPath) || !File.Exists(binaryPath))
        {
            throw new InvalidOperationException(
                "The installation binary could not be resolved. Pass --binary-path to a "
                + "published native executable.");
        }

        if (string.Equals(
            Path.GetExtension(binaryPath),
            ".dll",
            StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(
                "Install from a published native executable instead of a .dll.");
        }

        if (!LooksLikeNativeAotBinary(binaryPath))
        {
            throw new InvalidOperationException(
                "Install requires a published Native AOT executable. Publish first with "
                + "'dotnet publish -r <RID> -c Release'.");
        }

        return Path.GetFullPath(binaryPath);
    }

    private static void CopyBinary(string sourceBinaryPath, string targetBinaryPath)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(targetBinaryPath)
            ?? throw new InvalidOperationException(
                "The target binary path does not have a parent directory."));

        if (!string.Equals(sourceBinaryPath, targetBinaryPath, StringComparison.Ordinal))
        {
            File.Copy(sourceBinaryPath, targetBinaryPath, overwrite: true);
            CopyUnixFileMode(sourceBinaryPath, targetBinaryPath);
        }

        CopyCompanionFile(sourceBinaryPath, targetBinaryPath, ".pdb");
        CopyCompanionFile(sourceBinaryPath, targetBinaryPath, ".dbg");
    }

    private static void DeleteManagedBinary(string installedBinaryPath)
    {
        TryDeleteFile(installedBinaryPath);
        TryDeleteFile(Path.ChangeExtension(installedBinaryPath, ".pdb"));
        TryDeleteFile(Path.ChangeExtension(installedBinaryPath, ".dbg"));
    }

    private static void CopyCompanionFile(
        string sourceBinaryPath,
        string targetBinaryPath,
        string extension)
    {
        string sourceCompanionPath = Path.ChangeExtension(sourceBinaryPath, extension);
        if (!File.Exists(sourceCompanionPath))
        {
            return;
        }

        string targetCompanionPath = Path.ChangeExtension(targetBinaryPath, extension);
        File.Copy(sourceCompanionPath, targetCompanionPath, overwrite: true);
    }

    private static void CopyUnixFileMode(string sourceBinaryPath, string targetBinaryPath)
    {
        if (OperatingSystem.IsWindows())
        {
            return;
        }

        try
        {
            UnixFileMode mode = File.GetUnixFileMode(sourceBinaryPath);
            File.SetUnixFileMode(targetBinaryPath, mode);
        }
        catch (PlatformNotSupportedException)
        {
        }
    }

    private static void TryDeleteFile(string path)
    {
        if (File.Exists(path))
        {
            File.Delete(path);
        }
    }

    private static bool LooksLikeNativeAotBinary(string binaryPath)
    {
        if (string.IsNullOrWhiteSpace(binaryPath) || !File.Exists(binaryPath))
        {
            return false;
        }

        string runtimeConfigPath = Path.Combine(
            Path.GetDirectoryName(binaryPath) ?? string.Empty,
            Path.GetFileNameWithoutExtension(binaryPath) + ".runtimeconfig.json");

        return !File.Exists(runtimeConfigPath);
    }

    private static string FormatCheck(string label, bool isSuccess, string details)
        => $"{(isSuccess ? "[OK]" : "[FAIL]")} {label}: {details}";

    private static string FormatSecretValue(string? value)
        => string.IsNullOrWhiteSpace(value) ? "<missing>" : value;

    private async Task<bool> TryResolveCredentialsAsync(CancellationToken cancellationToken)
        => await telegramCredentialProvider.TryResolveAsync(cancellationToken) is not null;

    private string GetCurrentUtcTimestamp()
        => timeProvider
            .GetUtcNow()
            .UtcDateTime
            .ToString("yyyy-MM-ddTHH:mm:ss.fff'Z'");
}
