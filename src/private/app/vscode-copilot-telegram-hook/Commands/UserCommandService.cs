using Hcoona.VsCodeCopilotTelegramHook.Notifications;

namespace Hcoona.VsCodeCopilotTelegramHook.Commands;

internal sealed class UserCommandService(
    InstructionTemplateProvider instructionTemplateProvider,
    TelegramBotClient telegramBotClient,
    TimeProvider timeProvider)
{
    public async Task<int> InstallAsync(
        InstallCommandOptions options,
        CancellationToken cancellationToken)
    {
        try
        {
            string sourceBinaryPath = ResolveInstallableBinaryPath(options.BinaryPath);
            UserInstallationPaths userPaths = AppPaths.ResolveUserPaths(options);
            string currentTimestamp = GetCurrentUtcTimestamp();

            await TelegramCredentialProvider.StoreAsync(
                options.TelegramBotToken,
                options.TelegramChatId,
                options.SkipSecretPrompt,
                cancellationToken);

            CopyBinary(sourceBinaryPath, userPaths.InstalledBinaryPath);

            string sessionStartCommand = $"\"{userPaths.InstalledBinaryPath}\" hook session-start";
            string stopCommand = $"\"{userPaths.InstalledBinaryPath}\" hook stop";
            ConfigurationApplyResult hooksResult = UserHookConfigurationManager.InstallHooks(
                userPaths.HookSettingsPath,
                sessionStartCommand,
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
            await Console.Out.WriteLineAsync(
                $"Stored Telegram credentials in gopass under '{AppConstants.SecretPrefix}'.");

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

            return hooksResult.Applied && instructionResult.Applied ? 0 : 1;
        }
        catch (Exception ex)
        {
            await Console.Error.WriteLineAsync($"Install failed: {ex.Message}");
            return 1;
        }
    }

    public static async Task<int> UninstallAsync(
        UninstallCommandOptions options,
        CancellationToken cancellationToken)
    {
        try
        {
            UserInstallationPaths userPaths = AppPaths.ResolveUserPaths(options);

            ConfigurationApplyResult hooksResult =
                UserHookConfigurationManager.UninstallHooks(userPaths.HookSettingsPath);
            ConfigurationApplyResult instructionResult =
                UserHookConfigurationManager.UninstallInstruction(userPaths.InstructionFilePath);

            DeleteManagedBinary(userPaths.InstalledBinaryPath);

            if (options.RemoveSecrets)
            {
                await TelegramCredentialProvider.RemoveStoredSecretsAsync(cancellationToken);
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

            return hooksResult.Applied && instructionResult.Applied ? 0 : 1;
        }
        catch (Exception ex)
        {
            await Console.Error.WriteLineAsync($"Uninstall failed: {ex.Message}");
            return 1;
        }
    }

    public static async Task<int> HealthAsync(
        UserPathOverrides options,
        CancellationToken cancellationToken)
    {
        try
        {
            UserInstallationPaths userPaths = AppPaths.ResolveUserPaths(options);
            bool secretStoreAvailable =
                await TelegramCredentialProvider.IsSecretStoreAvailableAsync(cancellationToken);
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

            return binaryInstalled
                && hooksInstalled
                && instructionInstalled
                && credentialsAvailable
                ? 0
                : 1;
        }
        catch (Exception ex)
        {
            await Console.Error.WriteLineAsync($"Health check failed: {ex.Message}");
            return 1;
        }
    }

    public static async Task<int> DiagnoseAsync(
        UserPathOverrides options,
        CancellationToken cancellationToken)
    {
        try
        {
            UserInstallationPaths userPaths = AppPaths.ResolveUserPaths(options);
            string currentProcessPath = Environment.ProcessPath ?? "<unavailable>";
            bool currentBinaryLooksAot = LooksLikeNativeAotBinary(currentProcessPath);
            bool installedBinaryLooksAot = File.Exists(userPaths.InstalledBinaryPath)
                && LooksLikeNativeAotBinary(userPaths.InstalledBinaryPath);

            bool secretStoreAvailable =
                await TelegramCredentialProvider.IsSecretStoreAvailableAsync(cancellationToken);
            bool credentialsAvailable = await TryResolveCredentialsAsync(cancellationToken);
            bool managedHooksInstalled = UserHookConfigurationManager.IsHookInstalled(
                userPaths.HookSettingsPath);
            bool managedInstructionInstalled =
                UserHookConfigurationManager.IsInstructionInstalled(userPaths.InstructionFilePath);
            string executionEnvironment = AppPaths.GetExecutionEnvironmentDisplay();

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

            return 0;
        }
        catch (Exception ex)
        {
            await Console.Error.WriteLineAsync($"Diagnose failed: {ex.Message}");
            return 1;
        }
    }

    public async Task<int> TestNotificationAsync(
        TestNotificationCommandOptions options,
        CancellationToken cancellationToken)
    {
        try
        {
            TelegramCredentials credentials =
                await TelegramCredentialProvider.ResolveAsync(cancellationToken);
            string now = GetCurrentUtcTimestamp();
            NotificationContext context = new()
            {
                RunId = $"test-{Guid.NewGuid():n}",
                SessionId = "test",
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
                RunId = context.RunId,
                UpdatedAt = now,
                Summary = string.IsNullOrWhiteSpace(options.Message)
                    ? "这是一条来自 VS Code Copilot Telegram Hook 的测试通知。"
                    : options.Message.Trim(),
            };

            IReadOnlyList<string> messages = NotificationComposer.Compose(context, summaryRecord);
            await telegramBotClient.SendMessagesAsync(credentials, messages, cancellationToken);
            await Console.Out.WriteLineAsync("Sent a test Telegram notification successfully.");
            return 0;
        }
        catch (Exception ex)
        {
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

    private static async Task<bool> TryResolveCredentialsAsync(CancellationToken cancellationToken)
    {
        try
        {
            _ = await TelegramCredentialProvider.ResolveAsync(cancellationToken);
            return true;
        }
        catch (InvalidOperationException)
        {
            return false;
        }
    }

    private string GetCurrentUtcTimestamp()
        => timeProvider
            .GetUtcNow()
            .UtcDateTime
            .ToString("yyyy-MM-ddTHH:mm:ss.fff'Z'");
}
