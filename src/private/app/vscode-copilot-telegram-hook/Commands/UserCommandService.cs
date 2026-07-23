using System.Globalization;
using Hcoona.VsCodeCopilotTelegramHook.Logging;
using Hcoona.VsCodeCopilotTelegramHook.Notifications;
using Microsoft.Extensions.Logging;

namespace Hcoona.VsCodeCopilotTelegramHook.Commands;

internal sealed class UserCommandService(
    TelegramBotClient telegramBotClient,
    TelegramCredentialProvider telegramCredentialProvider,
    CopilotCliRuntimeProbe copilotCliRuntimeProbe,
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
            string sourceBinaryPath = ResolveInstallableBinaryPath(options.BinaryPath);
            ValidateUserArtifactPaths(userPaths, sourceBinaryPath);

            await using UserOperationLock operationLock =
                await UserOperationLock.AcquireAsync(cancellationToken);

            string currentTimestamp = GetCurrentUtcTimestamp();
            string sessionStartCommand = UserHookConfigurationManager.CreateCopilotCliHookCommand(
                userPaths.InstalledBinaryPath,
                "session-start");
            string userPromptSubmitCommand =
                UserHookConfigurationManager.CreateCopilotCliHookCommand(
                    userPaths.InstalledBinaryPath,
                    "user-prompt-submit");
            string stopCommand = UserHookConfigurationManager.CreateCopilotCliHookCommand(
                userPaths.InstalledBinaryPath,
                "stop");
            string notificationCommand = UserHookConfigurationManager.CreateCopilotCliHookCommand(
                userPaths.InstalledBinaryPath,
                "notification");
            ConfigurationApplyResult? copilotCliHookFilePreflightResult =
                UserHookConfigurationManager.PreflightManagedCopilotCliHookFile(
                    userPaths.CopilotCliHookFilePath,
                    notificationCommand,
                    currentTimestamp);
            if (copilotCliHookFilePreflightResult is not null)
            {
                await Console.Out.WriteLineAsync(copilotCliHookFilePreflightResult.Message);
                if (copilotCliHookFilePreflightResult.CandidatePath is not null)
                {
                    await Console.Error.WriteLineAsync(
                        "Copilot CLI hook file candidate: "
                        + copilotCliHookFilePreflightResult.CandidatePath);
                }

                await Console.Out.WriteLineAsync(
                    "Skipped user installation because the Copilot CLI hook file "
                    + "could not be updated.");
                return 1;
            }

            ConfigurationApplyResult? copilotCliExtensionPreflightResult =
                CopilotCliExtensionManager.PreflightInstall(
                    userPaths.CopilotCliExtensionFilePath);
            if (copilotCliExtensionPreflightResult is not null)
            {
                await Console.Out.WriteLineAsync(copilotCliExtensionPreflightResult.Message);
                await Console.Out.WriteLineAsync(
                    "Skipped user installation because the Copilot CLI extension "
                    + "could not be updated.");
                return 1;
            }

            using IDisposable logScope = sessionLogFileContext.UseLogFile(
                userPaths.UserLogFilePath);
            AppLog.StartingUserInstall(logger);
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
                    "Skipped user installation because no applicable VS Code "
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

            StoredTelegramSecrets storedSecretsSnapshot =
                await telegramCredentialProvider.ReadStoredSecretsAsync(cancellationToken);
            StoredTelegramSecretsTransaction secretTransaction = new(storedSecretsSnapshot);
            InstallArtifactSnapshot? artifactSnapshot = null;
            try
            {
                IReadOnlyList<string> secretMessages =
                    await telegramCredentialProvider.StoreForInstallAsync(
                        options.TelegramBotToken,
                        options.TelegramChatId,
                        options.SkipSecretPrompt,
                        secretTransaction,
                        cancellationToken);

                ConfigurationApplyResult? currentCopilotCliHookFilePreflightResult =
                    UserHookConfigurationManager.PreflightManagedCopilotCliHookFile(
                        userPaths.CopilotCliHookFilePath,
                        notificationCommand,
                        currentTimestamp);
                ConfigurationApplyResult? currentCopilotCliExtensionPreflightResult =
                    CopilotCliExtensionManager.PreflightInstall(
                        userPaths.CopilotCliExtensionFilePath);
                if (currentCopilotCliHookFilePreflightResult is not null
                    || currentCopilotCliExtensionPreflightResult is not null)
                {
                    if (currentCopilotCliHookFilePreflightResult is not null)
                    {
                        await Console.Out.WriteLineAsync(
                            currentCopilotCliHookFilePreflightResult.Message);
                    }

                    if (currentCopilotCliExtensionPreflightResult is not null)
                    {
                        await Console.Out.WriteLineAsync(
                            currentCopilotCliExtensionPreflightResult.Message);
                    }

                    await RestoreFailedInstallStateAsync(
                        registrationPlans,
                        artifactSnapshot,
                        secretTransaction,
                        preserveNewVsCodeArtifacts: false);
                    AppLog.CompletedUserInstall(logger, userPaths.InstallRoot, succeeded: false);
                    return 1;
                }

                artifactSnapshot = InstallArtifactSnapshot.Capture(userPaths);
                CopyBinary(
                    sourceBinaryPath,
                    userPaths.InstalledBinaryPath,
                    artifactSnapshot.BeginMutation,
                    artifactSnapshot.RecordMutation);

                ConfigurationApplyResult hookFileResult = ApplyTrackedArtifactChange(
                    artifactSnapshot,
                    userPaths.ManagedHookFilePath,
                    () => UserHookConfigurationManager.InstallManagedHookFile(
                            userPaths.ManagedHookFilePath,
                            sessionStartCommand,
                            userPromptSubmitCommand,
                            stopCommand,
                            currentTimestamp));
                ConfigurationApplyResult copilotCliHookFileResult = ApplyTrackedArtifactChange(
                    artifactSnapshot,
                    userPaths.CopilotCliHookFilePath,
                    () => UserHookConfigurationManager.InstallManagedCopilotCliHookFile(
                            userPaths.CopilotCliHookFilePath,
                            notificationCommand,
                            currentTimestamp));
                ConfigurationApplyResult copilotCliExtensionResult = ApplyTrackedArtifactChange(
                    artifactSnapshot,
                    userPaths.CopilotCliExtensionFilePath,
                    () => CopilotCliExtensionManager.Install(
                        userPaths.CopilotCliExtensionFilePath,
                        userPaths.InstalledBinaryPath));

                await Console.Out.WriteLineAsync(
                    $"Installed binary: {userPaths.InstalledBinaryPath}");
                await Console.Out.WriteLineAsync(hookFileResult.Message);
                await Console.Out.WriteLineAsync(copilotCliHookFileResult.Message);
                await Console.Out.WriteLineAsync(copilotCliExtensionResult.Message);

                foreach (string secretMessage in secretMessages)
                {
                    await Console.Out.WriteLineAsync(secretMessage);
                }

                await Console.Out.WriteLineAsync(
                    $"Telegram credentials are ready in gopass under "
                    + $"'{AppConstants.SecretPrefix}'.");

                if (hookFileResult.CandidatePath is not null)
                {
                    await Console.Error.WriteLineAsync(
                        $"Managed hook file candidate: {hookFileResult.CandidatePath}");
                }

                if (copilotCliHookFileResult.CandidatePath is not null)
                {
                    await Console.Error.WriteLineAsync(
                        $"Copilot CLI hook file candidate: "
                        + copilotCliHookFileResult.CandidatePath);
                }

                if (!hookFileResult.Applied
                    || !copilotCliHookFileResult.Applied
                    || !copilotCliExtensionResult.Applied)
                {
                    await Console.Out.WriteLineAsync(
                        "Skipped VS Code settings registration because one or more managed "
                        + "Copilot artifacts were not updated.");
                    await RestoreFailedInstallStateAsync(
                        registrationPlans,
                        artifactSnapshot,
                        secretTransaction,
                        preserveNewVsCodeArtifacts: false);
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
                    await RestoreFailedInstallStateAsync(
                        registrationPlans,
                        artifactSnapshot,
                        secretTransaction,
                        preserveNewVsCodeArtifacts: hasRollbackFailures);
                    AppLog.CompletedUserInstall(logger, userPaths.InstallRoot, succeeded: false);
                    return 1;
                }

                AppLog.CompletedUserInstall(
                    logger,
                    userPaths.InstallRoot,
                    succeeded: true);
                return 0;
            }
            catch
            {
                await RestoreFailedInstallStateAsync(
                    registrationPlans,
                    artifactSnapshot,
                    secretTransaction,
                    preserveNewVsCodeArtifacts: false);
                throw;
            }
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
            ValidateUserArtifactPaths(
                userPaths,
                includeVsCodeSettingsTarget: ShouldIncludeVsCodeSettingsTargetForUninstall);

            await using UserOperationLock operationLock =
                await UserOperationLock.AcquireAsync(cancellationToken);

            using IDisposable logScope = sessionLogFileContext.UseLogFile(
                userPaths.UserLogFilePath);
            AppLog.StartingUserUninstall(logger);

            string currentTimestamp = GetCurrentUtcTimestamp();
            ConfigurationApplyResult? copilotCliExtensionPreflightResult =
                CopilotCliExtensionManager.PreflightUninstall(
                    userPaths.CopilotCliExtensionFilePath);
            if (copilotCliExtensionPreflightResult is not null)
            {
                await Console.Out.WriteLineAsync(copilotCliExtensionPreflightResult.Message);
                await Console.Out.WriteLineAsync(
                    "Skipped uninstall cleanup because the Copilot CLI extension file "
                    + "could not be removed. Remove it manually, then run uninstall again.");
                AppLog.CompletedUserUninstall(logger, userPaths.InstallRoot, succeeded: false);
                return 1;
            }

            ConfigurationApplyResult? copilotCliHookPreflightResult =
                UserHookConfigurationManager.PreflightUninstallManagedCopilotCliHookFile(
                    userPaths.CopilotCliHookFilePath);
            if (copilotCliHookPreflightResult is not null)
            {
                await Console.Out.WriteLineAsync(copilotCliHookPreflightResult.Message);
                await Console.Out.WriteLineAsync(
                    "Skipped uninstall cleanup because the Copilot CLI hook file "
                    + "could not be updated. Remove its managed entries manually, "
                    + "then run uninstall again.");
                AppLog.CompletedUserUninstall(logger, userPaths.InstallRoot, succeeded: false);
                return 1;
            }

            ConfigurationApplyResult? managedHookPreflightResult =
                UserHookConfigurationManager.PreflightUninstallManagedHookFile(
                    userPaths.ManagedHookFilePath);
            if (managedHookPreflightResult is not null)
            {
                await Console.Out.WriteLineAsync(managedHookPreflightResult.Message);
                await Console.Out.WriteLineAsync(
                    "Skipped uninstall cleanup because the VS Code managed hook file "
                    + "could not be updated. Remove its managed entries manually, "
                    + "then run uninstall again.");
                AppLog.CompletedUserUninstall(logger, userPaths.InstallRoot, succeeded: false);
                return 1;
            }

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

            InstallArtifactSnapshot artifactSnapshot = InstallArtifactSnapshot.Capture(userPaths);
            StoredTelegramSecrets? storedSecretsSnapshot = options.RemoveSecrets
                ? await telegramCredentialProvider.ReadStoredSecretsAsync(cancellationToken)
                : null;
            StoredTelegramSecretsTransaction? secretTransaction = storedSecretsSnapshot is null
                ? null
                : new StoredTelegramSecretsTransaction(storedSecretsSnapshot);
            try
            {
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
                    AppLog.CompletedUserUninstall(
                        logger,
                        userPaths.InstallRoot,
                        succeeded: false);
                    return 1;
                }

                ConfigurationApplyResult copilotCliHookFileResult = ApplyTrackedArtifactChange(
                    artifactSnapshot,
                    userPaths.CopilotCliHookFilePath,
                    () => UserHookConfigurationManager.UninstallManagedCopilotCliHookFile(
                        userPaths.CopilotCliHookFilePath));
                await Console.Out.WriteLineAsync(copilotCliHookFileResult.Message);
                ConfigurationApplyResult copilotCliExtensionResult = ApplyTrackedArtifactChange(
                    artifactSnapshot,
                    userPaths.CopilotCliExtensionFilePath,
                    () => CopilotCliExtensionManager.Uninstall(
                        userPaths.CopilotCliExtensionFilePath));
                await Console.Out.WriteLineAsync(copilotCliExtensionResult.Message);
                ConfigurationApplyResult hookFileResult = ApplyTrackedArtifactChange(
                    artifactSnapshot,
                    userPaths.ManagedHookFilePath,
                    () => UserHookConfigurationManager.UninstallManagedHookFile(
                        userPaths.ManagedHookFilePath));
                await Console.Out.WriteLineAsync(hookFileResult.Message);

                if (!copilotCliHookFileResult.Applied
                    || !copilotCliExtensionResult.Applied
                    || !hookFileResult.Applied)
                {
                    await RestoreFailedUninstallStateAsync(
                        registrationPlans,
                        artifactSnapshot,
                        secretTransaction);
                    AppLog.CompletedUserUninstall(
                        logger,
                        userPaths.InstallRoot,
                        succeeded: false);
                    return 1;
                }

                DeleteManagedBinary(
                    userPaths.InstalledBinaryPath,
                    artifactSnapshot.BeginMutation,
                    artifactSnapshot.RecordMutation);

                if (options.RemoveSecrets)
                {
                    await telegramCredentialProvider.RemoveStoredSecretsAsync(
                        secretTransaction
                            ?? throw new InvalidOperationException(
                                "The secret transaction was not initialized."),
                        cancellationToken);
                }

                await Console.Out.WriteLineAsync(
                    $"Removed installed binary if it existed: {userPaths.InstalledBinaryPath}");

                if (options.RemoveSecrets)
                {
                    await Console.Out.WriteLineAsync(
                        "Removed stored Telegram secrets from gopass when present.");
                }

                AppLog.CompletedUserUninstall(logger, userPaths.InstallRoot, succeeded: true);
                return 0;
            }
            catch
            {
                await RestoreFailedUninstallStateAsync(
                    registrationPlans,
                    artifactSnapshot,
                    secretTransaction);
                throw;
            }
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
            ValidateUserArtifactPaths(
                userPaths,
                includeVsCodeSettingsTarget: ShouldIncludeVsCodeSettingsTargetForUninstall);
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
            CopilotCliHookFileStatus copilotCliHookFileStatus =
                UserHookConfigurationManager.GetManagedCopilotCliHookFileStatus(
                    userPaths.CopilotCliHookFilePath);
            bool copilotCliExtensionInstalled = CopilotCliExtensionManager.IsInstalled(
                userPaths.CopilotCliExtensionFilePath,
                userPaths.InstalledBinaryPath);
            CopilotCliRuntimeStatus copilotCliRuntimeStatus =
                await copilotCliRuntimeProbe.GetStatusAsync(cancellationToken);
            List<VsCodeSettingsStatus> hookRegistrationStatuses = GetVsCodeSettingsStatuses(
                userPaths.VsCodeSettingsTargets,
                userPaths.ManagedHookFilePath);

            await Console.Out.WriteLineAsync(
                FormatCheck("Installed binary", binaryInstalled, userPaths.InstalledBinaryPath));
            await Console.Out.WriteLineAsync(
                FormatCheck(
                    "Managed hook file",
                    managedHookFileInstalled,
                    userPaths.ManagedHookFilePath));
            await Console.Out.WriteLineAsync(
                FormatCheck(
                    "Legacy managed Copilot CLI hooks removed",
                    copilotCliHookFileStatus.IsClean,
                    copilotCliHookFileStatus.Detail));
            await Console.Out.WriteLineAsync(
                FormatCheck(
                    "Copilot CLI extension",
                    copilotCliExtensionInstalled,
                    userPaths.CopilotCliExtensionFilePath));
            await Console.Out.WriteLineAsync(
                FormatCheck(
                    "Copilot CLI user-extension runtime",
                    copilotCliRuntimeStatus.UserExtensionsSupported,
                    copilotCliRuntimeStatus.Detail));
            foreach (VsCodeSettingsStatus status in hookRegistrationStatuses)
            {
                await Console.Out.WriteLineAsync(FormatVsCodeSettingsCheck(status));
            }
            await Console.Out.WriteLineAsync(
                FormatCheck("gopass available", secretStoreAvailable, "PATH lookup"));
            await Console.Out.WriteLineAsync(
                FormatCheck(
                    "Telegram credentials",
                    credentialsAvailable,
                    secretStoreAvailable ? "environment or gopass" : "environment only"));

            bool isHealthy = binaryInstalled
                && managedHookFileInstalled
                && copilotCliHookFileStatus.IsClean
                && copilotCliExtensionInstalled
                && copilotCliRuntimeStatus.UserExtensionsSupported
                && hookRegistrationStatuses
                    .Where(static item => item.Target.IsApplicable)
                    .All(static item => item.IsRegistered)
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
            ValidateUserArtifactPaths(
                userPaths,
                includeVsCodeSettingsTarget: ShouldIncludeVsCodeSettingsTargetForUninstall);
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
            CopilotCliHookFileStatus copilotCliHookFileStatus =
                UserHookConfigurationManager.GetManagedCopilotCliHookFileStatus(
                    userPaths.CopilotCliHookFilePath);
            bool copilotCliExtensionInstalled = CopilotCliExtensionManager.IsInstalled(
                userPaths.CopilotCliExtensionFilePath,
                userPaths.InstalledBinaryPath);
            CopilotCliRuntimeStatus copilotCliRuntimeStatus =
                await copilotCliRuntimeProbe.GetStatusAsync(cancellationToken);
            List<VsCodeSettingsStatus> hookRegistrationStatuses = GetVsCodeSettingsStatuses(
                userPaths.VsCodeSettingsTargets,
                userPaths.ManagedHookFilePath);
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
            await Console.Out.WriteLineAsync(
                $"Copilot CLI hook file path : {userPaths.CopilotCliHookFilePath}");
            await Console.Out.WriteLineAsync(
                $"Copilot CLI extension file path : {userPaths.CopilotCliExtensionFilePath}");
            await Console.Out.WriteLineAsync("VS Code settings targets :");
            foreach (VsCodeSettingsStatus status in hookRegistrationStatuses)
            {
                await Console.Out.WriteLineAsync(FormatVsCodeSettingsDiagnoseLine(status));
            }
            await Console.Out.WriteLineAsync(
                $"Managed hook file installed : "
                + $"{managedHookFileInstalled}");
            await Console.Out.WriteLineAsync(
                $"Legacy managed Copilot CLI hooks removed : "
                + $"{copilotCliHookFileStatus.IsClean} ({copilotCliHookFileStatus.Detail})");
            await Console.Out.WriteLineAsync(
                $"Copilot CLI extension installed : "
                + $"{copilotCliExtensionInstalled}");
            await Console.Out.WriteLineAsync(
                $"Copilot CLI user-extension runtime : "
                + $"{copilotCliRuntimeStatus.Detail}");
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
            await using UserOperationLock operationLock =
                await UserOperationLock.AcquireAsync(cancellationToken);

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

            NotificationSummary summaryRecord = new()
            {
                SessionId = context.SessionId,
                NotificationTurnId = context.TurnId,
                NotificationNonce = "test",
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

    private static ConfigurationApplyResult ApplyTrackedArtifactChange(
        InstallArtifactSnapshot artifactSnapshot,
        string filePath,
        Func<ConfigurationApplyResult> apply)
    {
        artifactSnapshot.BeginMutation(filePath);
        try
        {
            ConfigurationApplyResult result = apply();
            if (result.Applied)
            {
                artifactSnapshot.RecordMutation(filePath);
            }

            return result;
        }
        catch
        {
            artifactSnapshot.RecordMutation(filePath);
            throw;
        }
    }

    private static void CopyBinary(
        string sourceBinaryPath,
        string targetBinaryPath,
        Action<string> beginMutation,
        Action<string> recordMutation)
    {
        if (!string.Equals(sourceBinaryPath, targetBinaryPath, StringComparison.Ordinal))
        {
            CopyFileAtomically(
                sourceBinaryPath,
                targetBinaryPath,
                beginMutation,
                recordMutation);
        }

        CopyCompanionFile(
            sourceBinaryPath,
            targetBinaryPath,
            ".pdb",
            beginMutation,
            recordMutation);
        CopyCompanionFile(
            sourceBinaryPath,
            targetBinaryPath,
            ".dbg",
            beginMutation,
            recordMutation);
    }

    private static void DeleteManagedBinary(
        string installedBinaryPath,
        Action<string> beginMutation,
        Action<string> recordMutation)
    {
        DeleteTrackedFile(installedBinaryPath, beginMutation, recordMutation);
        DeleteTrackedFile(
            Path.ChangeExtension(installedBinaryPath, ".pdb"),
            beginMutation,
            recordMutation);
        DeleteTrackedFile(
            Path.ChangeExtension(installedBinaryPath, ".dbg"),
            beginMutation,
            recordMutation);
    }

    private static void DeleteTrackedFile(
        string path,
        Action<string> beginMutation,
        Action<string> recordMutation)
    {
        beginMutation(path);
        try
        {
            TryDeleteFile(path);
        }
        finally
        {
            recordMutation(path);
        }
    }

    private static void CopyCompanionFile(
        string sourceBinaryPath,
        string targetBinaryPath,
        string extension,
        Action<string> beginMutation,
        Action<string> recordMutation)
    {
        string sourceCompanionPath = Path.ChangeExtension(sourceBinaryPath, extension);
        if (!File.Exists(sourceCompanionPath))
        {
            return;
        }

        string targetCompanionPath = Path.ChangeExtension(targetBinaryPath, extension);
        CopyFileAtomically(
            sourceCompanionPath,
            targetCompanionPath,
            beginMutation,
            recordMutation);
    }

    private static void CopyFileAtomically(
        string sourcePath,
        string targetPath,
        Action<string> beginMutation,
        Action<string> recordMutation)
    {
        beginMutation(targetPath);
        string directoryPath = Path.GetDirectoryName(targetPath)
            ?? throw new InvalidOperationException(
                "The target artifact path does not have a parent directory.");
        string temporaryPath = Path.Combine(
            directoryPath,
            $".{Path.GetFileName(targetPath)}.{Guid.NewGuid():N}.tmp");
        try
        {
            Directory.CreateDirectory(directoryPath);
            File.Copy(sourcePath, temporaryPath, overwrite: false);
            CopyUnixFileMode(sourcePath, temporaryPath);
            File.Move(temporaryPath, targetPath, overwrite: true);
            temporaryPath = string.Empty;
        }
        finally
        {
            TryDeleteFile(temporaryPath);
            recordMutation(targetPath);
        }
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

    private static void ValidateUserArtifactPaths(
        UserInstallationPaths userPaths,
        string? sourceBinaryPath = null,
        Func<VsCodeSettingsTarget, bool>? includeVsCodeSettingsTarget = null)
    {
        string? validationError = AppPaths.ValidateUserArtifactPathCollisions(
            userPaths,
            sourceBinaryPath,
            includeVsCodeSettingsTarget);
        if (validationError is not null)
        {
            throw new InvalidOperationException(validationError);
        }
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
                ShouldIncludeVsCodeSettingsTargetForUninstall)
        ];

    private static bool ShouldIncludeVsCodeSettingsTargetForUninstall(
        VsCodeSettingsTarget target)
        => target.IsApplicable || File.Exists(target.SettingsPath);

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

    private async Task RestoreFailedInstallStateAsync(
        IEnumerable<(VsCodeSettingsTarget Target, ConfigurationPlanResult Plan)> registrationPlans,
        InstallArtifactSnapshot? artifactSnapshot,
        StoredTelegramSecretsTransaction secretTransaction,
        bool preserveNewVsCodeArtifacts)
    {
        List<ConfigurationApplyResult> settingsRollbackResults =
            RollbackVsCodeSettingsPlans(registrationPlans);
        bool hasSettingsRollbackFailures =
            settingsRollbackResults.Any(static result => !result.Applied);
        await TryRunCleanupStepAsync(
            "VS Code settings rollback reporting",
            async () =>
            {
                await WriteConfigurationResultsAsync(settingsRollbackResults);
                await WriteRollbackFailureSummaryAsync(hasSettingsRollbackFailures);
            });

        if (artifactSnapshot is not null)
        {
            await TryRunCleanupStepAsync(
                "managed artifact rollback",
                async () =>
                {
                    artifactSnapshot.Restore(
                        preserveNewVsCodeArtifacts || hasSettingsRollbackFailures);
                    await Console.Out.WriteLineAsync(
                        preserveNewVsCodeArtifacts || hasSettingsRollbackFailures
                            ? "Restored the previous installation while preserving newly created "
                                + "VS Code artifacts that may still be referenced."
                            : "Restored the installation artifacts to their pre-install state.");
                });
        }
        await TryRunCleanupStepAsync(
            "Telegram secret rollback",
            async () =>
            {
                await telegramCredentialProvider.RestoreStoredSecretsAsync(
                    secretTransaction,
                    CancellationToken.None);
                await Console.Out.WriteLineAsync(
                    "Restored Telegram secrets to their pre-install state.");
            });
    }

    private async Task RestoreFailedUninstallStateAsync(
        IEnumerable<(VsCodeSettingsTarget Target, ConfigurationPlanResult Plan)> registrationPlans,
        InstallArtifactSnapshot artifactSnapshot,
        StoredTelegramSecretsTransaction? secretTransaction)
    {
        List<ConfigurationApplyResult> settingsRollbackResults =
            RollbackVsCodeSettingsPlans(registrationPlans);
        await TryRunCleanupStepAsync(
            "VS Code settings rollback reporting",
            async () =>
            {
                await WriteConfigurationResultsAsync(settingsRollbackResults);
                await WriteRollbackFailureSummaryAsync(
                    settingsRollbackResults.Any(static result => !result.Applied));
            });

        await TryRunCleanupStepAsync(
            "managed artifact rollback",
            async () =>
            {
                artifactSnapshot.Restore(preserveNewVsCodeArtifacts: false);
                await Console.Out.WriteLineAsync(
                    "Restored managed artifacts to their pre-uninstall state.");
            });

        if (secretTransaction is not null)
        {
            await TryRunCleanupStepAsync(
                "Telegram secret rollback",
                async () =>
                {
                    await telegramCredentialProvider.RestoreStoredSecretsAsync(
                        secretTransaction,
                        CancellationToken.None);
                    await Console.Out.WriteLineAsync(
                        "Restored Telegram secrets to their pre-uninstall state.");
                });
        }
    }

    private sealed class InstallArtifactSnapshot
    {
        private readonly IReadOnlyList<InstallFileSnapshot> files;
        private readonly Dictionary<string, InstallFileSnapshot> filesByPath;

        private InstallArtifactSnapshot(IReadOnlyList<InstallFileSnapshot> files)
        {
            this.files = files;
            StringComparer pathComparer = OperatingSystem.IsWindows()
                ? StringComparer.OrdinalIgnoreCase
                : StringComparer.Ordinal;
            filesByPath = files.ToDictionary(
                static file => Path.GetFullPath(file.FilePath),
                pathComparer);
        }

        public static InstallArtifactSnapshot Capture(UserInstallationPaths paths)
        {
            return new InstallArtifactSnapshot(
            [
                InstallFileSnapshot.Capture(
                    paths.InstalledBinaryPath,
                    preserveIfCreatedAndSettingsRollbackFails: true),
                InstallFileSnapshot.Capture(
                    Path.ChangeExtension(paths.InstalledBinaryPath, ".pdb"),
                    preserveIfCreatedAndSettingsRollbackFails: true),
                InstallFileSnapshot.Capture(
                    Path.ChangeExtension(paths.InstalledBinaryPath, ".dbg"),
                    preserveIfCreatedAndSettingsRollbackFails: true),
                InstallFileSnapshot.Capture(
                    paths.ManagedHookFilePath,
                    preserveIfCreatedAndSettingsRollbackFails: true),
                InstallFileSnapshot.Capture(
                    paths.CopilotCliHookFilePath,
                    preserveIfCreatedAndSettingsRollbackFails: false),
                InstallFileSnapshot.Capture(
                    paths.CopilotCliExtensionFilePath,
                    preserveIfCreatedAndSettingsRollbackFails: false),
            ]);
        }

        public void RecordMutation(string filePath)
        {
            if (!filesByPath.TryGetValue(Path.GetFullPath(filePath), out InstallFileSnapshot? file))
            {
                throw new InvalidOperationException(
                    $"The managed artifact snapshot does not contain '{filePath}'.");
            }

            file.RecordMutation();
        }

        public void BeginMutation(string filePath)
        {
            if (!filesByPath.TryGetValue(Path.GetFullPath(filePath), out InstallFileSnapshot? file))
            {
                throw new InvalidOperationException(
                    $"The managed artifact snapshot does not contain '{filePath}'.");
            }

            file.RefreshBaseline();
        }

        public void Restore(bool preserveNewVsCodeArtifacts)
        {
            List<string> failures = [];
            foreach (InstallFileSnapshot file in files)
            {
                if (preserveNewVsCodeArtifacts
                    && !file.Existed
                    && file.PreserveIfCreatedAndSettingsRollbackFails)
                {
                    continue;
                }

                try
                {
                    file.Restore();
                }
                catch (Exception ex) when (
                    ex is IOException or UnauthorizedAccessException or NotSupportedException
                        or InvalidOperationException)
                {
                    failures.Add($"{file.FilePath}: {ex.Message}");
                }
            }

            if (failures.Count > 0)
            {
                throw new InvalidOperationException(
                    "One or more managed artifacts could not be restored: "
                    + string.Join(" | ", failures));
            }
        }
    }

    private sealed class InstallFileSnapshot
    {
        private byte[]? expectedContent;
        private FileSystemMetadataSnapshot? expectedMetadata;

        private InstallFileSnapshot(
            string filePath,
            byte[]? content,
            FileSystemMetadataSnapshot metadata,
            bool preserveIfCreatedAndSettingsRollbackFails)
        {
            FilePath = filePath;
            Content = content;
            Metadata = metadata;
            PreserveIfCreatedAndSettingsRollbackFails =
                preserveIfCreatedAndSettingsRollbackFails;
        }

        public string FilePath { get; }

        public byte[]? Content { get; private set; }

        public FileSystemMetadataSnapshot Metadata { get; private set; }

        public bool PreserveIfCreatedAndSettingsRollbackFails { get; }

        public bool Existed => Content is not null;

        private bool WasMutated { get; set; }

        public static InstallFileSnapshot Capture(
            string filePath,
            bool preserveIfCreatedAndSettingsRollbackFails)
        {
            if (!File.Exists(filePath))
            {
                return new InstallFileSnapshot(
                    filePath,
                    content: null,
                    FileSystemMetadataSnapshot.Capture(filePath, fileExisted: false),
                    preserveIfCreatedAndSettingsRollbackFails);
            }

            return new InstallFileSnapshot(
                filePath,
                File.ReadAllBytes(filePath),
                FileSystemMetadataSnapshot.Capture(filePath, fileExisted: true),
                preserveIfCreatedAndSettingsRollbackFails);
        }

        public void RecordMutation()
        {
            byte[]? currentContent = ReadContent(FilePath);
            FileSystemMetadataSnapshot currentMetadata = FileSystemMetadataSnapshot.Capture(
                FilePath,
                currentContent is not null);
            if (!WasMutated
                && ContentEquals(currentContent, Content)
                && Metadata.MatchesCurrent(FilePath, Content is not null))
            {
                return;
            }

            WasMutated = true;
            expectedContent = currentContent;
            expectedMetadata = currentMetadata;
        }

        public void RefreshBaseline()
        {
            if (WasMutated)
            {
                return;
            }

            Content = ReadContent(FilePath);
            Metadata = FileSystemMetadataSnapshot.Capture(
                FilePath,
                Content is not null);
        }

        public void Restore()
        {
            if (!WasMutated)
            {
                return;
            }

            byte[]? currentContent = ReadContent(FilePath);
            if (!ContentEquals(currentContent, expectedContent)
                || expectedMetadata is null
                || !expectedMetadata.MatchesCurrent(
                    FilePath,
                    expectedContent is not null))
            {
                throw new InvalidOperationException(
                    "The file changed after the managed operation wrote it; "
                    + "automatic rollback was skipped.");
            }

            if (Content is null)
            {
                TryDeleteFile(FilePath);
                Metadata.Restore(FilePath);
                return;
            }

            string? parentDirectory = Path.GetDirectoryName(FilePath);
            if (!string.IsNullOrWhiteSpace(parentDirectory))
            {
                Directory.CreateDirectory(parentDirectory);
            }

            string temporaryPath = $"{FilePath}.restore.{Guid.NewGuid():N}.tmp";
            try
            {
                File.WriteAllBytes(temporaryPath, Content);
                File.Move(temporaryPath, FilePath, overwrite: true);
                Metadata.Restore(FilePath);
            }
            finally
            {
                TryDeleteFile(temporaryPath);
            }
        }

        private static byte[]? ReadContent(string filePath)
            => File.Exists(filePath) ? File.ReadAllBytes(filePath) : null;

        private static bool ContentEquals(byte[]? left, byte[]? right)
            => left is null
                ? right is null
                : right is not null && left.AsSpan().SequenceEqual(right);
    }

    private static async Task TryRunCleanupStepAsync(string stepName, Func<Task> cleanupStep)
    {
        try
        {
            await cleanupStep();
        }
        catch (Exception ex) when (
            ex is IOException or UnauthorizedAccessException or NotSupportedException
                or InvalidOperationException)
        {
            await Console.Error.WriteLineAsync($"{stepName} failed during cleanup: {ex.Message}");
        }
    }

    private async Task<bool> TryResolveCredentialsAsync(CancellationToken cancellationToken)
        => await telegramCredentialProvider.TryResolveAsync(cancellationToken) is not null;

    private string GetCurrentUtcTimestamp()
        => timeProvider
            .GetUtcNow()
            .UtcDateTime
            .ToString("yyyy-MM-ddTHH:mm:ss.fff'Z'", CultureInfo.InvariantCulture);
}
