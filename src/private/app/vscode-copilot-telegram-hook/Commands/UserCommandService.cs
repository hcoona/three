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
            if (!VsCodeSettingsManager.TryGetSupportedHookFileLocation(
                    userPaths.ManagedHookFilePath,
                    out _,
                    out string? hookPathErrorMessage))
            {
                await Console.Error.WriteLineAsync(
                    hookPathErrorMessage
                    ?? "The managed hook file path could not be converted into a supported "
                    + "VS Code hook location entry.");
                AppLog.CompletedUserInstall(logger, userPaths.InstallRoot, succeeded: false);
                return 1;
            }

            List<VsCodeSettingsTarget> registrationTargets =
                [.. userPaths.VsCodeSettingsTargets.Where(static target => target.IsApplicable)];
            await WriteSkippedVsCodeSettingsTargetMessagesAsync(userPaths.VsCodeSettingsTargets);

            if (registrationTargets.Count == 0)
            {
                await Console.Out.WriteLineAsync(
                    "Skipped user instruction installation because no applicable VS Code "
                    + "settings targets were detected on this host.");
                AppLog.CompletedUserInstall(logger, userPaths.InstallRoot, succeeded: false);
                return 1;
            }

            List<(VsCodeSettingsTarget Target, ConfigurationPlanResult Plan)> registrationPlans =
                PlanVsCodeSettingsChanges(
                    registrationTargets,
                    currentTimestamp,
                    userPaths.ManagedHookFilePath,
                    VsCodeSettingsManager.PlanRegisterHookFile);
            await WriteVsCodeSettingsPlanMessagesAsync(registrationPlans);

            if (!registrationPlans.All(static item => item.Plan.Applied))
            {
                await Console.Out.WriteLineAsync(
                    "Skipped user installation because one or more VS Code settings "
                    + "registrations could not be prepared.");
                AppLog.CompletedUserInstall(logger, userPaths.InstallRoot, succeeded: false);
                return 1;
            }

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
            ConfigurationApplyResult hookFileResult =
                UserHookConfigurationManager.InstallManagedHookFile(
                    userPaths.ManagedHookFilePath,
                    sessionStartCommand,
                    userPromptSubmitCommand,
                    stopCommand,
                    currentTimestamp);

            await Console.Out.WriteLineAsync($"Installed binary: {userPaths.InstalledBinaryPath}");
            await Console.Out.WriteLineAsync(hookFileResult.Message);

            foreach (string secretMessage in secretMessages)
            {
                await Console.Out.WriteLineAsync(secretMessage);
            }

            await Console.Out.WriteLineAsync(
                $"Telegram credentials are ready in gopass under '{AppConstants.SecretPrefix}'.");

            if (hookFileResult.CandidatePath is not null)
            {
                await Console.Error.WriteLineAsync(
                    $"Managed hook file candidate: {hookFileResult.CandidatePath}");
            }

            if (!hookFileResult.Applied)
            {
                await Console.Out.WriteLineAsync(
                    "Skipped VS Code settings registration because the managed hook file "
                    + "was not updated.");
                await Console.Out.WriteLineAsync(
                    "Skipped user instruction installation because the managed hook file "
                    + "was not updated.");
                AppLog.CompletedUserInstall(logger, userPaths.InstallRoot, succeeded: false);
                return 1;
            }

            (
                bool registrationsApplied,
                List<(VsCodeSettingsTarget Target, ConfigurationApplyResult Result)>
                    registrationResults,
                List<ConfigurationApplyResult> rollbackResults
            ) = ApplyVsCodeSettingsPlansAtomically(registrationPlans, currentTimestamp);
            await WriteVsCodeSettingsApplyMessagesAsync(registrationResults);
            await WriteConfigurationResultsAsync(rollbackResults);
            bool hasRollbackFailures = rollbackResults.Any(static result => !result.Applied);
            await WriteRollbackFailureSummaryAsync(hasRollbackFailures);

            if (!registrationsApplied)
            {
                await Console.Out.WriteLineAsync(
                    "Skipped user installation because one or more VS Code "
                    + "settings registrations were not updated.");
                await CleanupFailedInstallArtifactsAsync(
                    userPaths,
                    preserveManagedArtifacts: hasRollbackFailures);
                AppLog.CompletedUserInstall(logger, userPaths.InstallRoot, succeeded: false);
                return 1;
            }

            ConfigurationApplyResult instructionResult =
                UserHookConfigurationManager.InstallInstruction(
                userPaths.InstructionFilePath,
                instructionTemplateProvider.GetTemplate(),
                currentTimestamp);
            await Console.Out.WriteLineAsync(instructionResult.Message);

            if (instructionResult.CandidatePath is not null)
            {
                await Console.Error.WriteLineAsync(
                    $"Instruction candidate file: {instructionResult.CandidatePath}");
            }

            if (!instructionResult.Applied)
            {
                List<ConfigurationApplyResult> settingsRollbackResults =
                    RollbackVsCodeSettingsPlans(registrationPlans);
                await WriteConfigurationResultsAsync(settingsRollbackResults);
                bool hasSettingsRollbackFailures =
                    settingsRollbackResults.Any(static result => !result.Applied);
                await WriteRollbackFailureSummaryAsync(hasSettingsRollbackFailures);
                await CleanupFailedInstallArtifactsAsync(
                    userPaths,
                    preserveManagedArtifacts: hasSettingsRollbackFailures);
                AppLog.CompletedUserInstall(logger, userPaths.InstallRoot, succeeded: false);
                return 1;
            }

            AppLog.CompletedUserInstall(
                logger,
                userPaths.InstallRoot,
                succeeded: true);
            return 0;
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

            string currentTimestamp = GetCurrentUtcTimestamp();
            List<VsCodeSettingsTarget> registrationTargets = GetUninstallRegistrationTargets(
                userPaths.VsCodeSettingsTargets);
            List<(VsCodeSettingsTarget Target, ConfigurationPlanResult Plan)> registrationPlans =
                PlanVsCodeSettingsChanges(
                    registrationTargets,
                    currentTimestamp,
                    userPaths.ManagedHookFilePath,
                    VsCodeSettingsManager.PlanUnregisterHookFile);
            await WriteVsCodeSettingsPlanMessagesAsync(registrationPlans);

            if (!registrationPlans.All(static item => item.Plan.Applied))
            {
                await Console.Out.WriteLineAsync(
                    "Skipped uninstall cleanup because one or more VS Code settings "
                    + "registrations could not be prepared for removal.");
                AppLog.CompletedUserUninstall(logger, userPaths.InstallRoot, succeeded: false);
                return 1;
            }

            (
                bool registrationsApplied,
                List<(VsCodeSettingsTarget Target, ConfigurationApplyResult Result)>
                    registrationResults,
                List<ConfigurationApplyResult> rollbackResults
            ) = ApplyVsCodeSettingsPlansAtomically(registrationPlans, currentTimestamp);

            await WriteVsCodeSettingsApplyMessagesAsync(registrationResults);
            await WriteConfigurationResultsAsync(rollbackResults);
            await WriteRollbackFailureSummaryAsync(
                rollbackResults.Any(static result => !result.Applied));

            if (!registrationsApplied)
            {
                await Console.Out.WriteLineAsync(
                    "Skipped uninstall cleanup because one or more VS Code settings "
                    + "registrations were not removed.");
                AppLog.CompletedUserUninstall(logger, userPaths.InstallRoot, succeeded: false);
                return 1;
            }

            ConfigurationApplyResult hookFileResult =
                UserHookConfigurationManager.UninstallManagedHookFile(
                    userPaths.ManagedHookFilePath);
            ConfigurationApplyResult instructionResult =
                UserHookConfigurationManager.UninstallInstruction(userPaths.InstructionFilePath);

            DeleteManagedBinary(userPaths.InstalledBinaryPath);

            if (options.RemoveSecrets)
            {
                await telegramCredentialProvider.RemoveStoredSecretsAsync(cancellationToken);
            }

            await Console.Out.WriteLineAsync(hookFileResult.Message);
            await Console.Out.WriteLineAsync(instructionResult.Message);
            await Console.Out.WriteLineAsync(
                $"Removed installed binary if it existed: {userPaths.InstalledBinaryPath}");

            if (options.RemoveSecrets)
            {
                await Console.Out.WriteLineAsync(
                    "Removed stored Telegram secrets from gopass when present.");
            }

            bool succeeded = registrationsApplied
                && hookFileResult.Applied
                && instructionResult.Applied;
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
            bool managedHookFileInstalled =
                UserHookConfigurationManager.IsManagedHookFileInstalled(
                    userPaths.ManagedHookFilePath);
            List<VsCodeSettingsStatus> hookRegistrationStatuses = GetVsCodeSettingsStatuses(
                userPaths.VsCodeSettingsTargets,
                userPaths.ManagedHookFilePath);
            bool instructionInstalled =
                UserHookConfigurationManager.IsInstructionInstalled(userPaths.InstructionFilePath);

            await Console.Out.WriteLineAsync(
                FormatCheck("Installed binary", binaryInstalled, userPaths.InstalledBinaryPath));
            await Console.Out.WriteLineAsync(
                FormatCheck(
                    "Managed hook file",
                    managedHookFileInstalled,
                    userPaths.ManagedHookFilePath));
            foreach (VsCodeSettingsStatus status in hookRegistrationStatuses)
            {
                await Console.Out.WriteLineAsync(FormatVsCodeSettingsCheck(status));
            }

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
                && managedHookFileInstalled
                && hookRegistrationStatuses
                    .Where(static item => item.Target.IsApplicable)
                    .All(static item => item.IsRegistered)
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
            bool managedHookFileInstalled =
                UserHookConfigurationManager.IsManagedHookFileInstalled(
                    userPaths.ManagedHookFilePath);
            List<VsCodeSettingsStatus> hookRegistrationStatuses = GetVsCodeSettingsStatuses(
                userPaths.VsCodeSettingsTargets,
                userPaths.ManagedHookFilePath);
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
            await Console.Out.WriteLineAsync(
                $"Managed hook file path : {userPaths.ManagedHookFilePath}");
            await Console.Out.WriteLineAsync("VS Code settings targets :");
            foreach (VsCodeSettingsStatus status in hookRegistrationStatuses)
            {
                await Console.Out.WriteLineAsync(FormatVsCodeSettingsDiagnoseLine(status));
            }

            await Console.Out.WriteLineAsync(
                $"Instructions directory : {userPaths.InstructionsDirectory}");
            await Console.Out.WriteLineAsync(
                $"Managed instruction file : {userPaths.InstructionFilePath}");
            await Console.Out.WriteLineAsync(
                $"Managed hook file installed : "
                + $"{managedHookFileInstalled}");
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

    private static string FormatInfo(string label, string details)
        => $"[INFO] {label}: {details}";

    private static string FormatSecretValue(string? value)
        => string.IsNullOrWhiteSpace(value) ? "<missing>" : value;

    private static List<(VsCodeSettingsTarget Target, ConfigurationPlanResult Plan)>
        PlanVsCodeSettingsChanges(
            IEnumerable<VsCodeSettingsTarget> targets,
            string timestamp,
            string hookFilePath,
            Func<string, string, string, ConfigurationPlanResult> planner)
        => [
            .. targets.Select(target =>
                (
                    Target: target,
                    Plan: planner(target.SettingsPath, hookFilePath, timestamp)
                ))
        ];

    private static List<ConfigurationApplyResult> RollbackVsCodeSettingsPlans(
        IEnumerable<(VsCodeSettingsTarget Target, ConfigurationPlanResult Plan)> plannedChanges)
        => [
            .. plannedChanges
                .Select(static item => item.Plan.WritePlan)
                .Where(static writePlan => writePlan is not null)
                .Reverse()
                .Select(static writePlan => VsCodeSettingsManager.RollbackWritePlan(writePlan!))
        ];

    private static (
        bool Applied,
        List<(VsCodeSettingsTarget Target, ConfigurationApplyResult Result)> Results,
        List<ConfigurationApplyResult> RollbackResults
    ) ApplyVsCodeSettingsPlansAtomically(
        IEnumerable<(VsCodeSettingsTarget Target, ConfigurationPlanResult Plan)> plannedChanges,
        string timestamp)
    {
        List<(VsCodeSettingsTarget Target, ConfigurationApplyResult Result)> results = [];
        List<(VsCodeSettingsTarget Target, VsCodeSettingsWritePlan WritePlan)> appliedChanges = [];

        foreach ((VsCodeSettingsTarget target, ConfigurationPlanResult plan) in plannedChanges)
        {
            if (plan.WritePlan is null)
            {
                continue;
            }

            ConfigurationApplyResult result = VsCodeSettingsManager.ApplyWritePlan(
                plan.WritePlan,
                timestamp);
            results.Add((target, result));
            if (!result.Applied)
            {
                List<ConfigurationApplyResult> rollbackResults = [];
                foreach (
                    (_, VsCodeSettingsWritePlan writePlan) in
                    appliedChanges.AsEnumerable().Reverse())
                {
                    rollbackResults.Add(VsCodeSettingsManager.RollbackWritePlan(writePlan));
                }

                return (false, results, rollbackResults);
            }

            appliedChanges.Add((target, plan.WritePlan));
        }

        return (true, results, []);
    }

    private static List<VsCodeSettingsStatus> GetVsCodeSettingsStatuses(
        IEnumerable<VsCodeSettingsTarget> targets,
        string hookFilePath)
        => [
            .. targets.Select(target =>
                new VsCodeSettingsStatus(
                    target,
                    VsCodeSettingsManager.IsHookFileRegistered(
                        target.SettingsPath,
                        hookFilePath)))
        ];

    private static List<VsCodeSettingsTarget> GetUninstallRegistrationTargets(
        IEnumerable<VsCodeSettingsTarget> targets)
        => [
            .. targets.Where(
                static target => target.IsApplicable || File.Exists(target.SettingsPath))
        ];

    private static string FormatVsCodeSettingsCheck(VsCodeSettingsStatus status)
    {
        string details = $"{status.Target.SettingsPath} ({status.Target.DisplayName})";
        return status.Target.IsApplicable
            ? FormatCheck("VS Code hook registration", status.IsRegistered, details)
            : FormatInfo(
                "VS Code hook registration",
                $"{details} | not applicable: {status.Target.InapplicableReason}");
    }

    private static string FormatVsCodeSettingsDiagnoseLine(VsCodeSettingsStatus status)
    {
        string applicability = status.Target.IsApplicable
            ? "applicable"
            : $"not applicable ({status.Target.InapplicableReason})";
        return $"  - {status.Target.SettingsPath} | {status.Target.DisplayName} | "
            + $"{applicability} | hook registered = {status.IsRegistered}";
    }

    private static async Task WriteSkippedVsCodeSettingsTargetMessagesAsync(
        IEnumerable<VsCodeSettingsTarget> targets)
    {
        foreach (
            VsCodeSettingsTarget target in
            targets.Where(static target => !target.IsApplicable))
        {
            await Console.Out.WriteLineAsync(
                $"Skipped VS Code hook registration for {target.DisplayName}: "
                + $"{target.InapplicableReason}");
        }
    }

    private static async Task WriteVsCodeSettingsPlanMessagesAsync(
        IEnumerable<(VsCodeSettingsTarget Target, ConfigurationPlanResult Plan)> plannedChanges)
    {
        foreach ((_, ConfigurationPlanResult plan) in plannedChanges)
        {
            await Console.Out.WriteLineAsync(plan.Message);
            if (plan.CandidatePath is not null)
            {
                await Console.Error.WriteLineAsync(
                    $"VS Code settings candidate file: {plan.CandidatePath}");
            }
        }
    }

    private static async Task WriteVsCodeSettingsApplyMessagesAsync(
        IEnumerable<(VsCodeSettingsTarget Target, ConfigurationApplyResult Result)> results)
    {
        await WriteConfigurationResultsAsync(results.Select(static item => item.Result));
    }

    private static async Task WriteConfigurationResultsAsync(
        IEnumerable<ConfigurationApplyResult> results)
    {
        foreach (ConfigurationApplyResult result in results)
        {
            await Console.Out.WriteLineAsync(result.Message);
            if (result.CandidatePath is not null)
            {
                await Console.Error.WriteLineAsync(
                    $"VS Code settings candidate file: {result.CandidatePath}");
            }
        }
    }

    private static async Task WriteRollbackFailureSummaryAsync(bool hasRollbackFailures)
    {
        if (hasRollbackFailures)
        {
            await Console.Out.WriteLineAsync(
                "One or more VS Code settings files could not be rolled back automatically. "
                + "Manual recovery may be required.");
        }
    }

    private static async Task CleanupFailedInstallArtifactsAsync(
        UserInstallationPaths userPaths,
        bool preserveManagedArtifacts)
    {
        if (preserveManagedArtifacts)
        {
            await Console.Out.WriteLineAsync(
                "Preserved the managed hook file and installed binary because one or "
                + "more VS Code settings files may still reference them.");
            return;
        }

        ConfigurationApplyResult hookFileCleanupResult =
            UserHookConfigurationManager.UninstallManagedHookFile(userPaths.ManagedHookFilePath);
        await Console.Out.WriteLineAsync(hookFileCleanupResult.Message);
        DeleteManagedBinary(userPaths.InstalledBinaryPath);
        await Console.Out.WriteLineAsync(
            $"Removed installed binary if it existed: {userPaths.InstalledBinaryPath}");
    }

    private async Task<bool> TryResolveCredentialsAsync(CancellationToken cancellationToken)
        => await telegramCredentialProvider.TryResolveAsync(cancellationToken) is not null;

    private string GetCurrentUtcTimestamp()
        => timeProvider
            .GetUtcNow()
            .UtcDateTime
            .ToString("yyyy-MM-ddTHH:mm:ss.fff'Z'");
}
