using Hcoona.VsCodeCopilotTelegramHook.Commands;
using Hcoona.VsCodeCopilotTelegramHook.Logging;
using Hcoona.VsCodeCopilotTelegramHook.Notifications;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Hcoona.VsCodeCopilotTelegramHook.Tests;

public sealed class UserCommandServiceTests
{
    [Fact]
    public async Task DiagnoseAsyncWritesUserCommandLogFile()
    {
        DirectoryInfo installRoot = CreateHomeScopedTempSubdirectory();
        string managedHookFilePath = Path.Combine(
            installRoot.FullName,
            AppConstants.ManagedHookFileName);
        string[] vsCodeSettingsPaths = CreateVsCodeSettingsPaths(installRoot);

        try
        {
            SessionLogFileContext logContext = new();
            using ILoggerFactory loggerFactory = LoggerFactory.Create(builder =>
            {
                builder.ClearProviders();
                builder.SetMinimumLevel(LogLevel.Debug);
                builder.AddProvider(new SessionFileLoggerProvider(logContext));
            });

            UserCommandService service = CreateUserCommandService(
                new RecordingHttpMessageHandler(),
                loggerFactory,
                logContext);

            int exitCode = await service.DiagnoseAsync(
                new UserPathOverrides
                {
                    InstallRoot = installRoot,
                    ManagedHookFilePath = new FileInfo(managedHookFilePath),
                    CopilotCliHookFilePath = new FileInfo(
                        CreateCopilotCliHookFilePath(installRoot)),
                    VsCodeSettingsPaths = CreateVsCodeSettingsOverrides(vsCodeSettingsPaths),
                },
                CancellationToken.None);

            Assert.Equal(0, exitCode);

            string logPath = AppPaths.GetUserLogPath(installRoot.FullName);
            Assert.True(File.Exists(logPath));

            string logContent = await File.ReadAllTextAsync(logPath, CancellationToken.None);
            Assert.Contains("Starting user diagnose command", logContent, StringComparison.Ordinal);
            Assert.Contains(
                "Completed user diagnose command",
                logContent,
                StringComparison.Ordinal);
            Assert.DoesNotContain("| InstallRoot=", logContent, StringComparison.Ordinal);
            FileAssertions.AssertOwnerOnlyFileMode(logPath);
        }
        finally
        {
            installRoot.Delete(recursive: true);
        }
    }

    [Fact]
    public async Task InstallAsyncWhenSecretsAlreadyExistAsksBeforeKeepingThem()
    {
        DirectoryInfo installRoot = CreateHomeScopedTempSubdirectory();
        DirectoryInfo publishDirectory = Directory.CreateTempSubdirectory();
        string managedHookFilePath = Path.Combine(
            installRoot.FullName,
            AppConstants.ManagedHookFileName);
        string[] vsCodeSettingsPaths = CreateVsCodeSettingsPaths(installRoot);

        try
        {
            FakeProcessRunner processRunner = new();
            processRunner.SeedSecret(AppPaths.GetTelegramBotTokenSecretPath(), "old-token");
            processRunner.SeedSecret(AppPaths.GetTelegramChatIdSecretPath(), "old-chat-id");

            FakeInteractiveConsole interactiveConsole = new(
                canPrompt: true,
                confirmResponses: [false, false]);
            UserCommandService service = CreateUserCommandService(
                new RecordingHttpMessageHandler(),
                loggerFactory: null,
                new SessionLogFileContext(),
                processRunner,
                interactiveConsole);

            int exitCode = await service.InstallAsync(
                new InstallCommandOptions
                {
                    BinaryPath = new FileInfo(CreatePublishedBinary(publishDirectory)),
                    InstallRoot = installRoot,
                    ManagedHookFilePath = new FileInfo(managedHookFilePath),
                    CopilotCliHookFilePath = new FileInfo(
                        CreateCopilotCliHookFilePath(installRoot)),
                    VsCodeSettingsPaths = CreateVsCodeSettingsOverrides(vsCodeSettingsPaths),
                },
                CancellationToken.None);

            Assert.Equal(0, exitCode);
            Assert.True(
                UserHookConfigurationManager.IsManagedHookFileInstalled(
                    managedHookFilePath));
            Assert.True(
                UserHookConfigurationManager.IsManagedCopilotCliHookFileInstalled(
                    CreateCopilotCliHookFilePath(installRoot)));
            Assert.All(
                vsCodeSettingsPaths,
                settingsPath => Assert.True(
                    VsCodeSettingsManager.IsHookFileRegistered(
                        settingsPath,
                        managedHookFilePath)));
            Assert.Equal(
                "old-token",
                processRunner.GetSecret(AppPaths.GetTelegramBotTokenSecretPath()));
            Assert.Equal(
                "old-chat-id",
                processRunner.GetSecret(AppPaths.GetTelegramChatIdSecretPath()));
            Assert.Equal(2, interactiveConsole.ConfirmationPrompts.Count);
        }
        finally
        {
            installRoot.Delete(recursive: true);
            publishDirectory.Delete(recursive: true);
        }
    }

    [Fact]
    public async Task InstallAsyncWhenPromptsAreDisabledKeepsExistingSecrets()
    {
        DirectoryInfo installRoot = CreateHomeScopedTempSubdirectory();
        DirectoryInfo publishDirectory = Directory.CreateTempSubdirectory();
        string managedHookFilePath = Path.Combine(
            installRoot.FullName,
            AppConstants.ManagedHookFileName);
        string[] vsCodeSettingsPaths = CreateVsCodeSettingsPaths(installRoot);

        try
        {
            FakeProcessRunner processRunner = new();
            processRunner.SeedSecret(AppPaths.GetTelegramBotTokenSecretPath(), "old-token");
            processRunner.SeedSecret(AppPaths.GetTelegramChatIdSecretPath(), "old-chat-id");

            UserCommandService service = CreateUserCommandService(
                new RecordingHttpMessageHandler(),
                loggerFactory: null,
                new SessionLogFileContext(),
                processRunner,
                new FakeInteractiveConsole(canPrompt: false));

            int exitCode = await service.InstallAsync(
                new InstallCommandOptions
                {
                    BinaryPath = new FileInfo(CreatePublishedBinary(publishDirectory)),
                    TelegramBotToken = "new-token",
                    TelegramChatId = "new-chat-id",
                    InstallRoot = installRoot,
                    ManagedHookFilePath = new FileInfo(managedHookFilePath),
                    CopilotCliHookFilePath = new FileInfo(
                        CreateCopilotCliHookFilePath(installRoot)),
                    VsCodeSettingsPaths = CreateVsCodeSettingsOverrides(vsCodeSettingsPaths),
                },
                CancellationToken.None);

            Assert.Equal(0, exitCode);
            Assert.Equal(
                "old-token",
                processRunner.GetSecret(AppPaths.GetTelegramBotTokenSecretPath()));
            Assert.Equal(
                "old-chat-id",
                processRunner.GetSecret(AppPaths.GetTelegramChatIdSecretPath()));
        }
        finally
        {
            installRoot.Delete(recursive: true);
            publishDirectory.Delete(recursive: true);
        }
    }

    [Fact]
    public async Task UninstallAsyncRemovesManagedHookFileAndVsCodeRegistration()
    {
        DirectoryInfo installRoot = CreateHomeScopedTempSubdirectory();
        DirectoryInfo publishDirectory = Directory.CreateTempSubdirectory();
        string managedHookFilePath = Path.Combine(
            installRoot.FullName,
            AppConstants.ManagedHookFileName);
        string[] vsCodeSettingsPaths = CreateVsCodeSettingsPaths(installRoot);

        try
        {
            FakeProcessRunner processRunner = new();
            processRunner.SeedSecret(AppPaths.GetTelegramBotTokenSecretPath(), "bot-token");
            processRunner.SeedSecret(AppPaths.GetTelegramChatIdSecretPath(), "chat-id");

            UserCommandService service = CreateUserCommandService(
                new RecordingHttpMessageHandler(),
                loggerFactory: null,
                new SessionLogFileContext(),
                processRunner,
                new FakeInteractiveConsole(canPrompt: false));

            int installExitCode = await service.InstallAsync(
                new InstallCommandOptions
                {
                    BinaryPath = new FileInfo(CreatePublishedBinary(publishDirectory)),
                    InstallRoot = installRoot,
                    ManagedHookFilePath = new FileInfo(managedHookFilePath),
                    CopilotCliHookFilePath = new FileInfo(
                        CreateCopilotCliHookFilePath(installRoot)),
                    VsCodeSettingsPaths = CreateVsCodeSettingsOverrides(vsCodeSettingsPaths),
                    SkipSecretPrompt = true,
                },
                CancellationToken.None);

            int uninstallExitCode = await service.UninstallAsync(
                new UninstallCommandOptions
                {
                    InstallRoot = installRoot,
                    ManagedHookFilePath = new FileInfo(managedHookFilePath),
                    CopilotCliHookFilePath = new FileInfo(
                        CreateCopilotCliHookFilePath(installRoot)),
                    VsCodeSettingsPaths = CreateVsCodeSettingsOverrides(vsCodeSettingsPaths),
                },
                CancellationToken.None);

            Assert.Equal(0, installExitCode);
            Assert.Equal(0, uninstallExitCode);
            Assert.False(File.Exists(managedHookFilePath));
            Assert.False(File.Exists(CreateCopilotCliHookFilePath(installRoot)));
            Assert.All(
                vsCodeSettingsPaths,
                settingsPath => Assert.False(
                    VsCodeSettingsManager.IsHookFileRegistered(
                        settingsPath,
                        managedHookFilePath)));
        }
        finally
        {
            installRoot.Delete(recursive: true);
            publishDirectory.Delete(recursive: true);
        }
    }

    [Fact]
    public async Task UninstallAsyncPreservesNonCliGenericManagedEntriesInCopilotCliHookFile()
    {
        DirectoryInfo installRoot = CreateHomeScopedTempSubdirectory();
        DirectoryInfo publishDirectory = Directory.CreateTempSubdirectory();
        string managedHookFilePath = Path.Combine(
            installRoot.FullName,
            AppConstants.ManagedHookFileName);
        string copilotCliHookFilePath = CreateCopilotCliHookFilePath(installRoot);
        string[] vsCodeSettingsPaths = CreateVsCodeSettingsPaths(installRoot);

        try
        {
            FakeProcessRunner processRunner = new();
            processRunner.SeedSecret(AppPaths.GetTelegramBotTokenSecretPath(), "bot-token");
            processRunner.SeedSecret(AppPaths.GetTelegramChatIdSecretPath(), "chat-id");

            UserCommandService service = CreateUserCommandService(
                new RecordingHttpMessageHandler(),
                loggerFactory: null,
                new SessionLogFileContext(),
                processRunner,
                new FakeInteractiveConsole(canPrompt: false));

            int installExitCode = await service.InstallAsync(
                new InstallCommandOptions
                {
                    BinaryPath = new FileInfo(CreatePublishedBinary(publishDirectory)),
                    InstallRoot = installRoot,
                    ManagedHookFilePath = new FileInfo(managedHookFilePath),
                    CopilotCliHookFilePath = new FileInfo(copilotCliHookFilePath),
                    VsCodeSettingsPaths = CreateVsCodeSettingsOverrides(vsCodeSettingsPaths),
                    SkipSecretPrompt = true,
                },
                CancellationToken.None);

            Assert.Equal(0, installExitCode);
            File.WriteAllText(
                copilotCliHookFilePath,
                """
                {
                                        "version": 1,
                                        "hooks": {
                                                "Stop": [
                                                        {
                                                                "type": "command",
                                                                "command": "generic stop",
                                                                "timeout": 20,
                                                                "env": {
                                                                        "HCOONA_VSCODE_COPILOT_TELEGRAM_HOOK": "1"
                                                                }
                                                        }
                                                ]
                                        }
                }
                """);

            int uninstallExitCode = await service.UninstallAsync(
                new UninstallCommandOptions
                {
                    InstallRoot = installRoot,
                    ManagedHookFilePath = new FileInfo(managedHookFilePath),
                    CopilotCliHookFilePath = new FileInfo(copilotCliHookFilePath),
                    VsCodeSettingsPaths = CreateVsCodeSettingsOverrides(vsCodeSettingsPaths),
                },
                CancellationToken.None);

            Assert.Equal(0, uninstallExitCode);
            Assert.True(File.Exists(copilotCliHookFilePath));
            string copilotCliHookFileContent = await File.ReadAllTextAsync(
                copilotCliHookFilePath,
                CancellationToken.None);
            Assert.Contains("generic stop", copilotCliHookFileContent, StringComparison.Ordinal);
            Assert.Contains(
                "HCOONA_VSCODE_COPILOT_TELEGRAM_HOOK",
                copilotCliHookFileContent,
                StringComparison.Ordinal);
            Assert.False(File.Exists(managedHookFilePath));
        }
        finally
        {
            installRoot.Delete(recursive: true);
            publishDirectory.Delete(recursive: true);
        }
    }

    [Fact]
    public async Task UninstallAsyncPreservesManagedArtifactsWhenCopilotCliHookFileCannotBeParsed()
    {
        DirectoryInfo installRoot = CreateHomeScopedTempSubdirectory();
        DirectoryInfo publishDirectory = Directory.CreateTempSubdirectory();
        string managedHookFilePath = Path.Combine(
            installRoot.FullName,
            AppConstants.ManagedHookFileName);
        string copilotCliHookFilePath = CreateCopilotCliHookFilePath(installRoot);
        string[] vsCodeSettingsPaths = CreateVsCodeSettingsPaths(installRoot);

        try
        {
            FakeProcessRunner processRunner = new();
            processRunner.SeedSecret(AppPaths.GetTelegramBotTokenSecretPath(), "bot-token");
            processRunner.SeedSecret(AppPaths.GetTelegramChatIdSecretPath(), "chat-id");

            UserCommandService service = CreateUserCommandService(
                new RecordingHttpMessageHandler(),
                loggerFactory: null,
                new SessionLogFileContext(),
                processRunner,
                new FakeInteractiveConsole(canPrompt: false));

            int installExitCode = await service.InstallAsync(
                new InstallCommandOptions
                {
                    BinaryPath = new FileInfo(CreatePublishedBinary(publishDirectory)),
                    InstallRoot = installRoot,
                    ManagedHookFilePath = new FileInfo(managedHookFilePath),
                    CopilotCliHookFilePath = new FileInfo(copilotCliHookFilePath),
                    VsCodeSettingsPaths = CreateVsCodeSettingsOverrides(vsCodeSettingsPaths),
                    SkipSecretPrompt = true,
                },
                CancellationToken.None);

            Assert.Equal(0, installExitCode);

            string installedBinaryPath = Path.Combine(
                installRoot.FullName,
                AppPaths.GetManagedExecutableName());
            string originalManagedHookFileContent = await File.ReadAllTextAsync(
                managedHookFilePath,
                CancellationToken.None);
            string[] originalSettingsContents =
                [.. vsCodeSettingsPaths.Select(File.ReadAllText)];
            const string unparseableCopilotCliHookFileContent = "{";
            await File.WriteAllTextAsync(
                copilotCliHookFilePath,
                unparseableCopilotCliHookFileContent,
                CancellationToken.None);

            int uninstallExitCode = await service.UninstallAsync(
                new UninstallCommandOptions
                {
                    InstallRoot = installRoot,
                    ManagedHookFilePath = new FileInfo(managedHookFilePath),
                    CopilotCliHookFilePath = new FileInfo(copilotCliHookFilePath),
                    VsCodeSettingsPaths = CreateVsCodeSettingsOverrides(vsCodeSettingsPaths),
                    RemoveSecrets = true,
                },
                CancellationToken.None);

            Assert.Equal(1, uninstallExitCode);
            Assert.True(File.Exists(installedBinaryPath));
            Assert.Equal("native-aot-placeholder", File.ReadAllText(installedBinaryPath));
            Assert.True(File.Exists(managedHookFilePath));
            Assert.Equal(
                originalManagedHookFileContent,
                await File.ReadAllTextAsync(managedHookFilePath, CancellationToken.None));
            Assert.Equal(
                unparseableCopilotCliHookFileContent,
                await File.ReadAllTextAsync(copilotCliHookFilePath, CancellationToken.None));
            Assert.All(
                vsCodeSettingsPaths,
                settingsPath => Assert.True(
                    VsCodeSettingsManager.IsHookFileRegistered(
                        settingsPath,
                        managedHookFilePath)));
            Assert.Equal(originalSettingsContents[0], File.ReadAllText(vsCodeSettingsPaths[0]));
            Assert.Equal(originalSettingsContents[1], File.ReadAllText(vsCodeSettingsPaths[1]));
            Assert.Equal(
                "bot-token",
                processRunner.GetSecret(AppPaths.GetTelegramBotTokenSecretPath()));
            Assert.Equal(
                "chat-id",
                processRunner.GetSecret(AppPaths.GetTelegramChatIdSecretPath()));
        }
        finally
        {
            installRoot.Delete(recursive: true);
            publishDirectory.Delete(recursive: true);
        }
    }

    [Fact]
    public async Task HealthAsyncReportsHealthyWhenManagedHookFileAndRegistrationExist()
    {
        DirectoryInfo installRoot = CreateHomeScopedTempSubdirectory();
        DirectoryInfo publishDirectory = Directory.CreateTempSubdirectory();
        string managedHookFilePath = Path.Combine(
            installRoot.FullName,
            AppConstants.ManagedHookFileName);
        string[] vsCodeSettingsPaths = CreateVsCodeSettingsPaths(installRoot);

        try
        {
            FakeProcessRunner processRunner = new();
            processRunner.SeedSecret(AppPaths.GetTelegramBotTokenSecretPath(), "bot-token");
            processRunner.SeedSecret(AppPaths.GetTelegramChatIdSecretPath(), "chat-id");

            UserCommandService service = CreateUserCommandService(
                new RecordingHttpMessageHandler(),
                loggerFactory: null,
                new SessionLogFileContext(),
                processRunner,
                new FakeInteractiveConsole(canPrompt: false));

            int installExitCode = await service.InstallAsync(
                new InstallCommandOptions
                {
                    BinaryPath = new FileInfo(CreatePublishedBinary(publishDirectory)),
                    InstallRoot = installRoot,
                    ManagedHookFilePath = new FileInfo(managedHookFilePath),
                    CopilotCliHookFilePath = new FileInfo(
                        CreateCopilotCliHookFilePath(installRoot)),
                    VsCodeSettingsPaths = CreateVsCodeSettingsOverrides(vsCodeSettingsPaths),
                    SkipSecretPrompt = true,
                },
                CancellationToken.None);

            int healthExitCode = await service.HealthAsync(
                new UserPathOverrides
                {
                    InstallRoot = installRoot,
                    ManagedHookFilePath = new FileInfo(managedHookFilePath),
                    CopilotCliHookFilePath = new FileInfo(
                        CreateCopilotCliHookFilePath(installRoot)),
                    VsCodeSettingsPaths = CreateVsCodeSettingsOverrides(vsCodeSettingsPaths),
                },
                CancellationToken.None);

            Assert.Equal(0, installExitCode);
            Assert.Equal(0, healthExitCode);
        }
        finally
        {
            installRoot.Delete(recursive: true);
            publishDirectory.Delete(recursive: true);
        }
    }

    [Fact]
    public async Task HealthAndDiagnoseAsyncReportInvalidArtifactPathConfiguration()
    {
        DirectoryInfo installRoot = CreateHomeScopedTempSubdirectory();
        string managedHookFilePath = Path.Combine(
            installRoot.FullName,
            AppConstants.ManagedHookFileName);
        string installedBinaryPath = Path.Combine(
            installRoot.FullName,
            AppPaths.GetManagedExecutableName());
        string[] vsCodeSettingsPaths = CreateVsCodeSettingsPaths(installRoot);

        try
        {
            UserCommandService service = CreateUserCommandService(
                new RecordingHttpMessageHandler(),
                loggerFactory: null,
                new SessionLogFileContext(),
                processRunner: new FakeProcessRunner(),
                interactiveConsole: new FakeInteractiveConsole(canPrompt: false));
            UserPathOverrides invalidOptions = new()
            {
                InstallRoot = installRoot,
                ManagedHookFilePath = new FileInfo(managedHookFilePath),
                CopilotCliHookFilePath = new FileInfo(installedBinaryPath),
                VsCodeSettingsPaths = CreateVsCodeSettingsOverrides(vsCodeSettingsPaths),
            };

            int healthExitCode = await service.HealthAsync(invalidOptions, CancellationToken.None);
            int diagnoseExitCode = await service.DiagnoseAsync(invalidOptions, CancellationToken.None);

            Assert.Equal(1, healthExitCode);
            Assert.Equal(1, diagnoseExitCode);
        }
        finally
        {
            installRoot.Delete(recursive: true);
        }
    }

    [Fact]
    public async Task HealthAndDiagnoseAsyncRejectExistingInapplicableSettingsPathCollision()
    {
        DirectoryInfo installRoot = CreateHomeScopedTempSubdirectory();
        DirectoryInfo publishDirectory = Directory.CreateTempSubdirectory();
        string managedHookFilePath = Path.Combine(
            installRoot.FullName,
            AppConstants.ManagedHookFileName);
        string[] vsCodeSettingsPaths = CreateVsCodeSettingsPaths(installRoot);
        VsCodeSettingsTarget[] installTargets = CreateVsCodeSettingsTargets(
            vsCodeSettingsPaths,
            serverApplicable: false);
        VsCodeSettingsTarget[] healthTargets =
        [
            installTargets[0],
            installTargets[1] with { SettingsPath = managedHookFilePath },
        ];

        try
        {
            FakeProcessRunner processRunner = new();
            processRunner.SeedSecret(AppPaths.GetTelegramBotTokenSecretPath(), "bot-token");
            processRunner.SeedSecret(AppPaths.GetTelegramChatIdSecretPath(), "chat-id");
            UserCommandService service = CreateUserCommandService(
                new RecordingHttpMessageHandler(),
                loggerFactory: null,
                new SessionLogFileContext(),
                processRunner,
                new FakeInteractiveConsole(canPrompt: false));

            int installExitCode = await service.InstallAsync(
                new InstallCommandOptions
                {
                    BinaryPath = new FileInfo(CreatePublishedBinary(publishDirectory)),
                    InstallRoot = installRoot,
                    ManagedHookFilePath = new FileInfo(managedHookFilePath),
                    CopilotCliHookFilePath = new FileInfo(
                        CreateCopilotCliHookFilePath(installRoot)),
                    VsCodeSettingsTargets = installTargets,
                    SkipSecretPrompt = true,
                },
                CancellationToken.None);

            UserPathOverrides invalidOptions = new()
            {
                InstallRoot = installRoot,
                ManagedHookFilePath = new FileInfo(managedHookFilePath),
                CopilotCliHookFilePath = new FileInfo(
                    CreateCopilotCliHookFilePath(installRoot)),
                VsCodeSettingsTargets = healthTargets,
            };
            int healthExitCode = await service.HealthAsync(invalidOptions, CancellationToken.None);
            int diagnoseExitCode = await service.DiagnoseAsync(
                invalidOptions,
                CancellationToken.None);

            Assert.Equal(0, installExitCode);
            Assert.Equal(1, healthExitCode);
            Assert.Equal(1, diagnoseExitCode);
        }
        finally
        {
            installRoot.Delete(recursive: true);
            publishDirectory.Delete(recursive: true);
        }
    }

    [Fact]
    public async Task InstallAsyncRollsBackPriorVsCodeSettingsUpdatesWhenLaterTargetWriteFails()
    {
        DirectoryInfo installRoot = CreateHomeScopedTempSubdirectory();
        DirectoryInfo publishDirectory = Directory.CreateTempSubdirectory();
        string managedHookFilePath = Path.Combine(
            installRoot.FullName,
            AppConstants.ManagedHookFileName);
        string[] vsCodeSettingsPaths = CreateVsCodeSettingsPaths(installRoot);

        try
        {
            File.WriteAllText(vsCodeSettingsPaths[0], """{ "editor.fontSize": 14 }""");
            File.WriteAllText(vsCodeSettingsPaths[1], """{ "editor.tabSize": 4 }""");

            UserCommandService service = CreateUserCommandService(
                new RecordingHttpMessageHandler(),
                loggerFactory: null,
                new SessionLogFileContext(),
                processRunner: new FakeProcessRunner(),
                interactiveConsole: new FakeInteractiveConsole(canPrompt: false));

            using IDisposable _ = AtomicTextFileWriter.UseWriterForTesting(
                new FailOnWriteNumbersTextFileWriter(2));

            int exitCode = await service.InstallAsync(
                new InstallCommandOptions
                {
                    BinaryPath = new FileInfo(CreatePublishedBinary(publishDirectory)),
                    TelegramBotToken = "bot-token",
                    TelegramChatId = "chat-id",
                    InstallRoot = installRoot,
                    ManagedHookFilePath = new FileInfo(managedHookFilePath),
                    CopilotCliHookFilePath = new FileInfo(
                        CreateCopilotCliHookFilePath(installRoot)),
                    VsCodeSettingsPaths = CreateVsCodeSettingsOverrides(vsCodeSettingsPaths),
                },
                CancellationToken.None);

            Assert.Equal(1, exitCode);
            Assert.False(
                VsCodeSettingsManager.IsHookFileRegistered(
                    vsCodeSettingsPaths[0],
                    managedHookFilePath));
            Assert.True(File.Exists(vsCodeSettingsPaths[1]));
            Assert.False(File.Exists(managedHookFilePath));
            Assert.False(File.Exists(CreateCopilotCliHookFilePath(installRoot)));
            Assert.False(
                File.Exists(
                    Path.Combine(
                        installRoot.FullName,
                        AppPaths.GetManagedExecutableName())));
        }
        finally
        {
            installRoot.Delete(recursive: true);
            publishDirectory.Delete(recursive: true);
        }
    }

    [Fact]
    public async Task InstallAndHealthAsyncIgnoreNonApplicableVsCodeSettingsTargets()
    {
        DirectoryInfo installRoot = CreateHomeScopedTempSubdirectory();
        DirectoryInfo publishDirectory = Directory.CreateTempSubdirectory();
        string managedHookFilePath = Path.Combine(
            installRoot.FullName,
            AppConstants.ManagedHookFileName);
        string[] vsCodeSettingsPaths = CreateVsCodeSettingsPaths(installRoot);
        VsCodeSettingsTarget[] settingsTargets = CreateVsCodeSettingsTargets(
            vsCodeSettingsPaths,
            serverApplicable: false);

        try
        {
            UserCommandService service = CreateUserCommandService(
                new RecordingHttpMessageHandler(),
                loggerFactory: null,
                new SessionLogFileContext(),
                processRunner: new FakeProcessRunner(),
                interactiveConsole: new FakeInteractiveConsole(canPrompt: false));

            int installExitCode = await service.InstallAsync(
                new InstallCommandOptions
                {
                    BinaryPath = new FileInfo(CreatePublishedBinary(publishDirectory)),
                    TelegramBotToken = "bot-token",
                    TelegramChatId = "chat-id",
                    InstallRoot = installRoot,
                    ManagedHookFilePath = new FileInfo(managedHookFilePath),
                    CopilotCliHookFilePath = new FileInfo(
                        CreateCopilotCliHookFilePath(installRoot)),
                    VsCodeSettingsTargets = settingsTargets,
                    SkipSecretPrompt = true,
                },
                CancellationToken.None);

            int healthExitCode = await service.HealthAsync(
                new UserPathOverrides
                {
                    InstallRoot = installRoot,
                    ManagedHookFilePath = new FileInfo(managedHookFilePath),
                    CopilotCliHookFilePath = new FileInfo(
                        CreateCopilotCliHookFilePath(installRoot)),
                    VsCodeSettingsTargets = settingsTargets,
                },
                CancellationToken.None);

            Assert.Equal(0, installExitCode);
            Assert.Equal(0, healthExitCode);
            Assert.True(
                VsCodeSettingsManager.IsHookFileRegistered(
                    vsCodeSettingsPaths[0],
                    managedHookFilePath));
            Assert.False(File.Exists(vsCodeSettingsPaths[1]));
        }
        finally
        {
            installRoot.Delete(recursive: true);
            publishDirectory.Delete(recursive: true);
        }
    }

    [Fact]
    public async Task InstallAsyncFailsBeforeSideEffectsWhenSettingsRegistrationCannotBePlanned()
    {
        DirectoryInfo installRoot = CreateHomeScopedTempSubdirectory();
        DirectoryInfo publishDirectory = Directory.CreateTempSubdirectory();
        string managedHookFilePath = Path.Combine(
            installRoot.FullName,
            AppConstants.ManagedHookFileName);
        string[] vsCodeSettingsPaths = CreateVsCodeSettingsPaths(installRoot);

        try
        {
            File.WriteAllText(vsCodeSettingsPaths[1], "{ invalid json");
            FakeProcessRunner processRunner = new();
            UserCommandService service = CreateUserCommandService(
                new RecordingHttpMessageHandler(),
                loggerFactory: null,
                new SessionLogFileContext(),
                processRunner,
                new FakeInteractiveConsole(canPrompt: false));

            int exitCode = await service.InstallAsync(
                new InstallCommandOptions
                {
                    BinaryPath = new FileInfo(CreatePublishedBinary(publishDirectory)),
                    TelegramBotToken = "bot-token",
                    TelegramChatId = "chat-id",
                    InstallRoot = installRoot,
                    ManagedHookFilePath = new FileInfo(managedHookFilePath),
                    CopilotCliHookFilePath = new FileInfo(
                        CreateCopilotCliHookFilePath(installRoot)),
                    VsCodeSettingsPaths = CreateVsCodeSettingsOverrides(vsCodeSettingsPaths),
                },
                CancellationToken.None);

            Assert.Equal(1, exitCode);
            Assert.False(
                File.Exists(
                    Path.Combine(
                        installRoot.FullName,
                        AppPaths.GetManagedExecutableName())));
            Assert.False(File.Exists(managedHookFilePath));
            Assert.False(File.Exists(CreateCopilotCliHookFilePath(installRoot)));
            Assert.Null(processRunner.GetSecret(AppPaths.GetTelegramBotTokenSecretPath()));
            Assert.Null(processRunner.GetSecret(AppPaths.GetTelegramChatIdSecretPath()));
        }
        finally
        {
            installRoot.Delete(recursive: true);
            publishDirectory.Delete(recursive: true);
        }
    }

    [Fact]
    public async Task InstallAsyncCleansManagedArtifactsWhenCopilotCliHookWriteThrows()
    {
        DirectoryInfo installRoot = CreateHomeScopedTempSubdirectory();
        DirectoryInfo publishDirectory = Directory.CreateTempSubdirectory();
        string managedHookFilePath = Path.Combine(
            installRoot.FullName,
            AppConstants.ManagedHookFileName);
        string copilotCliHookDirectoryPath = Path.Combine(
            installRoot.FullName,
            "copilot-cli-hooks");
        string copilotCliHookFilePath = Path.Combine(
            copilotCliHookDirectoryPath,
            AppConstants.CopilotCliHookFileName);
        string[] vsCodeSettingsPaths = CreateVsCodeSettingsPaths(installRoot);

        try
        {
            File.WriteAllText(copilotCliHookDirectoryPath, "not a directory");
            UserCommandService service = CreateUserCommandService(
                new RecordingHttpMessageHandler(),
                loggerFactory: null,
                new SessionLogFileContext(),
                processRunner: new FakeProcessRunner(),
                interactiveConsole: new FakeInteractiveConsole(canPrompt: false));

            int exitCode = await service.InstallAsync(
                new InstallCommandOptions
                {
                    BinaryPath = new FileInfo(CreatePublishedBinary(publishDirectory)),
                    TelegramBotToken = "bot-token",
                    TelegramChatId = "chat-id",
                    InstallRoot = installRoot,
                    ManagedHookFilePath = new FileInfo(managedHookFilePath),
                    CopilotCliHookFilePath = new FileInfo(copilotCliHookFilePath),
                    VsCodeSettingsPaths = CreateVsCodeSettingsOverrides(vsCodeSettingsPaths),
                },
                CancellationToken.None);

            Assert.Equal(1, exitCode);
            Assert.False(
                File.Exists(
                    Path.Combine(
                        installRoot.FullName,
                        AppPaths.GetManagedExecutableName())));
            Assert.False(File.Exists(managedHookFilePath));
            Assert.False(File.Exists(copilotCliHookFilePath));
            Assert.All(
                vsCodeSettingsPaths,
                settingsPath => Assert.False(
                    VsCodeSettingsManager.IsHookFileRegistered(
                        settingsPath,
                        managedHookFilePath)));
        }
        finally
        {
            installRoot.Delete(recursive: true);
            publishDirectory.Delete(recursive: true);
        }
    }

    [Fact]
    public async Task InstallAsyncPreservesBinaryWhenCopilotCliCleanupThrows()
    {
        DirectoryInfo installRoot = CreateHomeScopedTempSubdirectory();
        DirectoryInfo publishDirectory = Directory.CreateTempSubdirectory();
        string managedHookFilePath = Path.Combine(
            installRoot.FullName,
            AppConstants.ManagedHookFileName);
        string copilotCliHookFilePath = CreateCopilotCliHookFilePath(installRoot);
        string[] vsCodeSettingsPaths = CreateVsCodeSettingsPaths(installRoot);
        string installedBinaryPath = Path.Combine(
            installRoot.FullName,
            AppPaths.GetManagedExecutableName());

        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(copilotCliHookFilePath)!);
            File.WriteAllText(
                copilotCliHookFilePath,
                """
                {
                                        "version": 1,
                                        "owner": "external",
                                        "hooks": {}
                }
                """);
            UserCommandService service = CreateUserCommandService(
                new RecordingHttpMessageHandler(),
                loggerFactory: null,
                new SessionLogFileContext(),
                processRunner: new FakeProcessRunner(),
                interactiveConsole: new FakeInteractiveConsole(canPrompt: false));

            using IDisposable _ = AtomicTextFileWriter.UseWriterForTesting(
                new FailOnWriteNumbersTextFileWriter(4, 5));

            int exitCode = await service.InstallAsync(
                new InstallCommandOptions
                {
                    BinaryPath = new FileInfo(CreatePublishedBinary(publishDirectory)),
                    TelegramBotToken = "bot-token",
                    TelegramChatId = "chat-id",
                    InstallRoot = installRoot,
                    ManagedHookFilePath = new FileInfo(managedHookFilePath),
                    CopilotCliHookFilePath = new FileInfo(copilotCliHookFilePath),
                    VsCodeSettingsPaths = CreateVsCodeSettingsOverrides(vsCodeSettingsPaths),
                },
                CancellationToken.None);

            Assert.Equal(1, exitCode);
            Assert.True(File.Exists(installedBinaryPath));
            Assert.Equal("native-aot-placeholder", File.ReadAllText(installedBinaryPath));
            Assert.False(File.Exists(managedHookFilePath));
            Assert.True(File.Exists(copilotCliHookFilePath));
            Assert.False(
                VsCodeSettingsManager.IsHookFileRegistered(
                    vsCodeSettingsPaths[0],
                    managedHookFilePath));
        }
        finally
        {
            installRoot.Delete(recursive: true);
            publishDirectory.Delete(recursive: true);
        }
    }

    [Fact]
    public async Task InstallAsyncPreservesBinaryWhenCopilotCliCleanupReturnsNotApplied()
    {
        DirectoryInfo installRoot = CreateHomeScopedTempSubdirectory();
        DirectoryInfo publishDirectory = Directory.CreateTempSubdirectory();
        string managedHookFilePath = Path.Combine(
            installRoot.FullName,
            AppConstants.ManagedHookFileName);
        string copilotCliHookFilePath = CreateCopilotCliHookFilePath(installRoot);
        string[] vsCodeSettingsPaths = CreateVsCodeSettingsPaths(installRoot);
        string installedBinaryPath = Path.Combine(
            installRoot.FullName,
            AppPaths.GetManagedExecutableName());

        try
        {
            string unparseableManagedCopilotCliHookFileContent = $$"""
                { "command": "\"{{installedBinaryPath}}\" hook session-start"
                """;
            UserCommandService service = CreateUserCommandService(
                new RecordingHttpMessageHandler(),
                loggerFactory: null,
                new SessionLogFileContext(),
                processRunner: new FakeProcessRunner(),
                interactiveConsole: new FakeInteractiveConsole(canPrompt: false));

            using IDisposable _ = AtomicTextFileWriter.UseWriterForTesting(
                new CorruptFileOnWriteNumberTextFileWriter(
                    4,
                    copilotCliHookFilePath,
                    unparseableManagedCopilotCliHookFileContent));

            int exitCode = await service.InstallAsync(
                new InstallCommandOptions
                {
                    BinaryPath = new FileInfo(CreatePublishedBinary(publishDirectory)),
                    TelegramBotToken = "bot-token",
                    TelegramChatId = "chat-id",
                    InstallRoot = installRoot,
                    ManagedHookFilePath = new FileInfo(managedHookFilePath),
                    CopilotCliHookFilePath = new FileInfo(copilotCliHookFilePath),
                    VsCodeSettingsPaths = CreateVsCodeSettingsOverrides(vsCodeSettingsPaths),
                },
                CancellationToken.None);

            Assert.Equal(1, exitCode);
            Assert.True(File.Exists(installedBinaryPath));
            Assert.Equal("native-aot-placeholder", File.ReadAllText(installedBinaryPath));
            Assert.False(File.Exists(managedHookFilePath));
            Assert.Equal(
                unparseableManagedCopilotCliHookFileContent,
                await File.ReadAllTextAsync(copilotCliHookFilePath, CancellationToken.None));
            Assert.All(
                vsCodeSettingsPaths,
                settingsPath => Assert.False(
                    VsCodeSettingsManager.IsHookFileRegistered(
                        settingsPath,
                        managedHookFilePath)));
        }
        finally
        {
            installRoot.Delete(recursive: true);
            publishDirectory.Delete(recursive: true);
        }
    }

    [Fact]
    public async Task InstallAsyncPreservesBinaryWhenVsCodeManagedHookCleanupReturnsNotApplied()
    {
        DirectoryInfo installRoot = CreateHomeScopedTempSubdirectory();
        DirectoryInfo publishDirectory = Directory.CreateTempSubdirectory();
        string managedHookFilePath = Path.Combine(
            installRoot.FullName,
            AppConstants.ManagedHookFileName);
        string copilotCliHookFilePath = CreateCopilotCliHookFilePath(installRoot);
        string[] vsCodeSettingsPaths = CreateVsCodeSettingsPaths(installRoot);
        string installedBinaryPath = Path.Combine(
            installRoot.FullName,
            AppPaths.GetManagedExecutableName());

        try
        {
            string unparseableManagedHookFileContent = $$"""
                { "command": "\"{{installedBinaryPath}}\" hook session-start"
                """;
            UserCommandService service = CreateUserCommandService(
                new RecordingHttpMessageHandler(),
                loggerFactory: null,
                new SessionLogFileContext(),
                processRunner: new FakeProcessRunner(),
                interactiveConsole: new FakeInteractiveConsole(canPrompt: false));

            using IDisposable _ = AtomicTextFileWriter.UseWriterForTesting(
                new CorruptFileOnWriteNumberTextFileWriter(
                    4,
                    managedHookFilePath,
                    unparseableManagedHookFileContent));

            int exitCode = await service.InstallAsync(
                new InstallCommandOptions
                {
                    BinaryPath = new FileInfo(CreatePublishedBinary(publishDirectory)),
                    TelegramBotToken = "bot-token",
                    TelegramChatId = "chat-id",
                    InstallRoot = installRoot,
                    ManagedHookFilePath = new FileInfo(managedHookFilePath),
                    CopilotCliHookFilePath = new FileInfo(copilotCliHookFilePath),
                    VsCodeSettingsPaths = CreateVsCodeSettingsOverrides(vsCodeSettingsPaths),
                },
                CancellationToken.None);

            Assert.Equal(1, exitCode);
            Assert.True(File.Exists(installedBinaryPath));
            Assert.Equal("native-aot-placeholder", File.ReadAllText(installedBinaryPath));
            Assert.Equal(
                unparseableManagedHookFileContent,
                await File.ReadAllTextAsync(managedHookFilePath, CancellationToken.None));
            Assert.False(File.Exists(copilotCliHookFilePath));
            Assert.All(
                vsCodeSettingsPaths,
                settingsPath => Assert.False(
                    VsCodeSettingsManager.IsHookFileRegistered(
                        settingsPath,
                        managedHookFilePath)));
        }
        finally
        {
            installRoot.Delete(recursive: true);
            publishDirectory.Delete(recursive: true);
        }
    }

    [Theory]
    [InlineData("\"version\": 2,", false)]
    [InlineData("\"version\": 2,", true)]
    [InlineData("", false)]
    [InlineData("", true)]
    public async Task InstallAsyncRejectsUnsupportedOrMissingVersionCopilotCliHookBeforeSideEffects(
        string versionProperty,
        bool preCreateUserLog)
    {
        DirectoryInfo installRoot = CreateHomeScopedTempSubdirectory();
        DirectoryInfo publishDirectory = Directory.CreateTempSubdirectory();
        string managedHookFilePath = Path.Combine(
            installRoot.FullName,
            AppConstants.ManagedHookFileName);
        string copilotCliHookFilePath = CreateCopilotCliHookFilePath(installRoot);
        string[] vsCodeSettingsPaths = CreateVsCodeSettingsPaths(installRoot);
        string installedBinaryPath = Path.Combine(
            installRoot.FullName,
            AppPaths.GetManagedExecutableName());

        try
        {
            await File.WriteAllTextAsync(
                installedBinaryPath,
                "previous-binary",
                CancellationToken.None);
            ConfigurationApplyResult managedHookResult =
                UserHookConfigurationManager.InstallManagedHookFile(
                    managedHookFilePath,
                    $"\"{installedBinaryPath}\" hook session-start",
                    $"\"{installedBinaryPath}\" hook user-prompt-submit",
                    $"\"{installedBinaryPath}\" hook stop",
                    "2026-03-14T00:00:00.0000000Z");
            Assert.True(managedHookResult.Applied);
            foreach (string settingsPath in vsCodeSettingsPaths)
            {
                ConfigurationApplyResult settingsResult = VsCodeSettingsManager.RegisterHookFile(
                    settingsPath,
                    managedHookFilePath,
                    "2026-03-14T00:00:00.0000000Z");
                Assert.True(settingsResult.Applied);
            }

            Directory.CreateDirectory(Path.GetDirectoryName(copilotCliHookFilePath)!);
            string originalCopilotCliHookFileContent =
                $$"""
                {
                                        {{versionProperty}}
                                        "hooks": {
                                                "SessionStart": [
                                                        {
                                                                "type": "command",
                                                                "command": "{{installedBinaryPath}} hook session-start",
                                                                "timeoutSec": 10,
                                                                "env": {
                                                                        "HCOONA_VSCODE_COPILOT_TELEGRAM_HOOK": "1",
                                                                        "HCOONA_VSCODE_COPILOT_TELEGRAM_HOOK_SURFACE": "copilot-cli"
                                                                }
                                                        }
                                                ],
                                                "UserPromptSubmit": [
                                                        {
                                                                "type": "command",
                                                                "command": "{{installedBinaryPath}} hook user-prompt-submit",
                                                                "timeoutSec": 10,
                                                                "env": {
                                                                        "HCOONA_VSCODE_COPILOT_TELEGRAM_HOOK": "1",
                                                                        "HCOONA_VSCODE_COPILOT_TELEGRAM_HOOK_SURFACE": "copilot-cli"
                                                                }
                                                        }
                                                ],
                                                "Stop": [
                                                        {
                                                                "type": "command",
                                                                "command": "{{installedBinaryPath}} hook stop",
                                                                "timeoutSec": 20,
                                                                "env": {
                                                                        "HCOONA_VSCODE_COPILOT_TELEGRAM_HOOK": "1",
                                                                        "HCOONA_VSCODE_COPILOT_TELEGRAM_HOOK_SURFACE": "copilot-cli"
                                                                }
                                                        }
                                                ]
                                        }
                }
                """;
            await File.WriteAllTextAsync(
                copilotCliHookFilePath,
                originalCopilotCliHookFileContent,
                CancellationToken.None);
            IReadOnlyDictionary<string, string> originalCopilotCliHookDirectoryContents =
                ReadDirectoryFileContents(Path.GetDirectoryName(copilotCliHookFilePath)!);
            string originalManagedHookFileContent = await File.ReadAllTextAsync(
                managedHookFilePath,
                CancellationToken.None);
            string[] originalSettingsContents =
                [.. vsCodeSettingsPaths.Select(File.ReadAllText)];
            string userLogPath = AppPaths.GetUserLogPath(installRoot.FullName);
            const string OriginalUserLogContent = "existing user command log";
            if (preCreateUserLog)
            {
                await File.WriteAllTextAsync(
                    userLogPath,
                    OriginalUserLogContent,
                    CancellationToken.None);
            }

            SessionLogFileContext logContext = new();
            using ILoggerFactory loggerFactory = LoggerFactory.Create(builder =>
            {
                builder.ClearProviders();
                builder.SetMinimumLevel(LogLevel.Debug);
                builder.AddProvider(new SessionFileLoggerProvider(logContext));
            });

            FakeProcessRunner processRunner = new();
            processRunner.SeedSecret(AppPaths.GetTelegramBotTokenSecretPath(), "old-token");
            processRunner.SeedSecret(AppPaths.GetTelegramChatIdSecretPath(), "old-chat-id");
            FakeInteractiveConsole interactiveConsole = new(
                canPrompt: true,
                confirmResponses: [true, true]);
            UserCommandService service = CreateUserCommandService(
                new RecordingHttpMessageHandler(),
                loggerFactory,
                logContext,
                processRunner,
                interactiveConsole);

            int exitCode = await service.InstallAsync(
                new InstallCommandOptions
                {
                    BinaryPath = new FileInfo(CreatePublishedBinary(publishDirectory)),
                    TelegramBotToken = "new-token",
                    TelegramChatId = "new-chat-id",
                    InstallRoot = installRoot,
                    ManagedHookFilePath = new FileInfo(managedHookFilePath),
                    CopilotCliHookFilePath = new FileInfo(copilotCliHookFilePath),
                    VsCodeSettingsPaths = CreateVsCodeSettingsOverrides(vsCodeSettingsPaths),
                },
                CancellationToken.None);

            Assert.Equal(1, exitCode);
            Assert.True(File.Exists(installedBinaryPath));
            Assert.Equal("previous-binary", File.ReadAllText(installedBinaryPath));
            Assert.True(File.Exists(managedHookFilePath));
            Assert.Equal(
                originalManagedHookFileContent,
                await File.ReadAllTextAsync(managedHookFilePath, CancellationToken.None));
            Assert.True(File.Exists(copilotCliHookFilePath));
            Assert.Equal(
                originalCopilotCliHookFileContent,
                await File.ReadAllTextAsync(copilotCliHookFilePath, CancellationToken.None));
            Assert.Equal(
                originalCopilotCliHookDirectoryContents,
                ReadDirectoryFileContents(Path.GetDirectoryName(copilotCliHookFilePath)!));
            Assert.Empty(
                Directory.EnumerateFiles(
                    Path.GetDirectoryName(copilotCliHookFilePath)!,
                    "*.candidate.json",
                    SearchOption.AllDirectories));
            Assert.True(
                UserHookConfigurationManager.IsManagedHookFileInstalled(managedHookFilePath));
            Assert.All(
                vsCodeSettingsPaths,
                settingsPath => Assert.True(
                    VsCodeSettingsManager.IsHookFileRegistered(
                        settingsPath,
                        managedHookFilePath)));
            Assert.Equal(originalSettingsContents[0], File.ReadAllText(vsCodeSettingsPaths[0]));
            Assert.Equal(originalSettingsContents[1], File.ReadAllText(vsCodeSettingsPaths[1]));
            Assert.Equal(
                "old-token",
                processRunner.GetSecret(AppPaths.GetTelegramBotTokenSecretPath()));
            Assert.Equal(
                "old-chat-id",
                processRunner.GetSecret(AppPaths.GetTelegramChatIdSecretPath()));
            Assert.Empty(interactiveConsole.ConfirmationPrompts);
            if (preCreateUserLog)
            {
                Assert.Equal(
                    OriginalUserLogContent,
                    await File.ReadAllTextAsync(userLogPath, CancellationToken.None));
            }
            else
            {
                Assert.False(File.Exists(userLogPath));
            }
        }
        finally
        {
            installRoot.Delete(recursive: true);
            publishDirectory.Delete(recursive: true);
        }
    }

    [Fact]
    public async Task InstallAsyncRejectsIdenticalHookFilePathsBeforeSideEffects()
    {
        DirectoryInfo installRoot = CreateHomeScopedTempSubdirectory();
        DirectoryInfo publishDirectory = Directory.CreateTempSubdirectory();
        string sharedHookFilePath = Path.Combine(
            installRoot.FullName,
            AppConstants.ManagedHookFileName);
        string[] vsCodeSettingsPaths = CreateVsCodeSettingsPaths(installRoot);

        try
        {
            FakeProcessRunner processRunner = new();
            UserCommandService service = CreateUserCommandService(
                new RecordingHttpMessageHandler(),
                loggerFactory: null,
                new SessionLogFileContext(),
                processRunner,
                new FakeInteractiveConsole(canPrompt: false));

            int exitCode = await service.InstallAsync(
                new InstallCommandOptions
                {
                    BinaryPath = new FileInfo(CreatePublishedBinary(publishDirectory)),
                    TelegramBotToken = "bot-token",
                    TelegramChatId = "chat-id",
                    InstallRoot = installRoot,
                    ManagedHookFilePath = new FileInfo(sharedHookFilePath),
                    CopilotCliHookFilePath = new FileInfo(sharedHookFilePath),
                    VsCodeSettingsPaths = CreateVsCodeSettingsOverrides(vsCodeSettingsPaths),
                },
                CancellationToken.None);

            Assert.Equal(1, exitCode);
            Assert.False(
                File.Exists(
                    Path.Combine(
                        installRoot.FullName,
                        AppPaths.GetManagedExecutableName())));
            Assert.False(File.Exists(sharedHookFilePath));
            Assert.Null(processRunner.GetSecret(AppPaths.GetTelegramBotTokenSecretPath()));
            Assert.Null(processRunner.GetSecret(AppPaths.GetTelegramChatIdSecretPath()));
            Assert.All(vsCodeSettingsPaths, settingsPath => Assert.False(File.Exists(settingsPath)));
        }
        finally
        {
            installRoot.Delete(recursive: true);
            publishDirectory.Delete(recursive: true);
        }
    }

    [Fact]
    public async Task InstallAsyncRejectsCopilotCliHookPathMatchingInstalledBinaryBeforeSideEffects()
    {
        DirectoryInfo installRoot = CreateHomeScopedTempSubdirectory();
        DirectoryInfo publishDirectory = Directory.CreateTempSubdirectory();
        string managedHookFilePath = Path.Combine(
            installRoot.FullName,
            AppConstants.ManagedHookFileName);
        string installedBinaryPath = Path.Combine(
            installRoot.FullName,
            AppPaths.GetManagedExecutableName());
        string[] vsCodeSettingsPaths = CreateVsCodeSettingsPaths(installRoot);

        try
        {
            FakeProcessRunner processRunner = new();
            UserCommandService service = CreateUserCommandService(
                new RecordingHttpMessageHandler(),
                loggerFactory: null,
                new SessionLogFileContext(),
                processRunner,
                new FakeInteractiveConsole(canPrompt: false));

            int exitCode = await service.InstallAsync(
                new InstallCommandOptions
                {
                    BinaryPath = new FileInfo(CreatePublishedBinary(publishDirectory)),
                    TelegramBotToken = "bot-token",
                    TelegramChatId = "chat-id",
                    InstallRoot = installRoot,
                    ManagedHookFilePath = new FileInfo(managedHookFilePath),
                    CopilotCliHookFilePath = new FileInfo(installedBinaryPath),
                    VsCodeSettingsPaths = CreateVsCodeSettingsOverrides(vsCodeSettingsPaths),
                },
                CancellationToken.None);

            Assert.Equal(1, exitCode);
            Assert.False(File.Exists(installedBinaryPath));
            Assert.False(File.Exists(managedHookFilePath));
            Assert.Null(processRunner.GetSecret(AppPaths.GetTelegramBotTokenSecretPath()));
            Assert.Null(processRunner.GetSecret(AppPaths.GetTelegramChatIdSecretPath()));
            Assert.All(vsCodeSettingsPaths, settingsPath => Assert.False(File.Exists(settingsPath)));
        }
        finally
        {
            installRoot.Delete(recursive: true);
            publishDirectory.Delete(recursive: true);
        }
    }

    [Fact]
    public async Task InstallAsyncRejectsSymlinkedFutureArtifactPathCollisionBeforeSideEffects()
    {
        if (!OperatingSystem.IsLinux())
        {
            return;
        }

        DirectoryInfo container = CreateHomeScopedTempSubdirectory();
        DirectoryInfo publishDirectory = Directory.CreateTempSubdirectory();
        DirectoryInfo installRoot = Directory.CreateDirectory(
            Path.Combine(container.FullName, "real-install"));
        string installRootAlias = Path.Combine(container.FullName, "install-alias");
        string installedBinaryPath = Path.Combine(
            installRoot.FullName,
            AppPaths.GetManagedExecutableName());
        string aliasedManagedHookFilePath = Path.Combine(
            installRootAlias,
            AppPaths.GetManagedExecutableName());
        string[] vsCodeSettingsPaths = CreateVsCodeSettingsPaths(installRoot);

        try
        {
            Directory.CreateSymbolicLink(installRootAlias, installRoot.FullName);
            FakeProcessRunner processRunner = new();
            UserCommandService service = CreateUserCommandService(
                new RecordingHttpMessageHandler(),
                loggerFactory: null,
                new SessionLogFileContext(),
                processRunner,
                new FakeInteractiveConsole(canPrompt: false));

            int exitCode = await service.InstallAsync(
                new InstallCommandOptions
                {
                    BinaryPath = new FileInfo(CreatePublishedBinary(publishDirectory)),
                    TelegramBotToken = "bot-token",
                    TelegramChatId = "chat-id",
                    InstallRoot = installRoot,
                    ManagedHookFilePath = new FileInfo(aliasedManagedHookFilePath),
                    CopilotCliHookFilePath = new FileInfo(
                        CreateCopilotCliHookFilePath(installRoot)),
                    VsCodeSettingsPaths = CreateVsCodeSettingsOverrides(vsCodeSettingsPaths),
                },
                CancellationToken.None);

            Assert.Equal(1, exitCode);
            Assert.False(File.Exists(installedBinaryPath));
            Assert.False(File.Exists(aliasedManagedHookFilePath));
            Assert.Null(processRunner.GetSecret(AppPaths.GetTelegramBotTokenSecretPath()));
            Assert.Null(processRunner.GetSecret(AppPaths.GetTelegramChatIdSecretPath()));
            Assert.All(vsCodeSettingsPaths, settingsPath => Assert.False(File.Exists(settingsPath)));
        }
        finally
        {
            container.Delete(recursive: true);
            publishDirectory.Delete(recursive: true);
        }
    }

    [Theory]
    [InlineData("hook", ".pdb")]
    [InlineData("settings", ".dbg")]
    public async Task InstallAsyncRejectsManagedPathMatchingInstalledCompanionBeforeSideEffects(
        string conflictingArtifact,
        string companionExtension)
    {
        DirectoryInfo installRoot = CreateHomeScopedTempSubdirectory();
        DirectoryInfo publishDirectory = Directory.CreateTempSubdirectory();
        string installedBinaryPath = Path.Combine(
            installRoot.FullName,
            AppPaths.GetManagedExecutableName());
        string companionPath = Path.ChangeExtension(installedBinaryPath, companionExtension);
        string managedHookFilePath = string.Equals(
            conflictingArtifact,
            "hook",
            StringComparison.Ordinal)
                ? companionPath
                : Path.Combine(installRoot.FullName, AppConstants.ManagedHookFileName);
        string[] vsCodeSettingsPaths = string.Equals(
            conflictingArtifact,
            "settings",
            StringComparison.Ordinal)
                ?
                [
                    companionPath,
                    Path.Combine(installRoot.FullName, "vscode-server-machine-settings.json"),
                ]
                : CreateVsCodeSettingsPaths(installRoot);

        try
        {
            FakeProcessRunner processRunner = new();
            UserCommandService service = CreateUserCommandService(
                new RecordingHttpMessageHandler(),
                loggerFactory: null,
                new SessionLogFileContext(),
                processRunner,
                new FakeInteractiveConsole(canPrompt: false));

            int exitCode = await service.InstallAsync(
                new InstallCommandOptions
                {
                    BinaryPath = new FileInfo(CreatePublishedBinary(publishDirectory)),
                    TelegramBotToken = "bot-token",
                    TelegramChatId = "chat-id",
                    InstallRoot = installRoot,
                    ManagedHookFilePath = new FileInfo(managedHookFilePath),
                    CopilotCliHookFilePath = new FileInfo(
                        CreateCopilotCliHookFilePath(installRoot)),
                    VsCodeSettingsPaths = CreateVsCodeSettingsOverrides(vsCodeSettingsPaths),
                },
                CancellationToken.None);

            Assert.Equal(1, exitCode);
            Assert.False(File.Exists(installedBinaryPath));
            Assert.False(File.Exists(companionPath));
            Assert.Null(processRunner.GetSecret(AppPaths.GetTelegramBotTokenSecretPath()));
            Assert.Null(processRunner.GetSecret(AppPaths.GetTelegramChatIdSecretPath()));
            Assert.All(vsCodeSettingsPaths, settingsPath => Assert.False(File.Exists(settingsPath)));
        }
        finally
        {
            installRoot.Delete(recursive: true);
            publishDirectory.Delete(recursive: true);
        }
    }

    [Fact]
    public async Task InstallAsyncRejectsSourceCompanionAliasToManagedCompanionBeforeSideEffects()
    {
        if (!OperatingSystem.IsLinux())
        {
            return;
        }

        DirectoryInfo installRoot = CreateHomeScopedTempSubdirectory();
        DirectoryInfo publishDirectory = CreateHomeScopedTempSubdirectory();
        string managedHookFilePath = Path.Combine(
            installRoot.FullName,
            AppConstants.ManagedHookFileName);
        string installedBinaryPath = Path.Combine(
            installRoot.FullName,
            AppPaths.GetManagedExecutableName());
        string installedPdbPath = Path.ChangeExtension(installedBinaryPath, ".pdb");
        string[] vsCodeSettingsPaths = CreateVsCodeSettingsPaths(installRoot);

        try
        {
            string sourceBinaryPath = CreatePublishedBinary(publishDirectory);
            string sourcePdbPath = Path.ChangeExtension(sourceBinaryPath, ".pdb");
            File.WriteAllText(installedPdbPath, "existing-managed-pdb");
            File.CreateSymbolicLink(sourcePdbPath, installedPdbPath);

            FakeProcessRunner processRunner = new();
            UserCommandService service = CreateUserCommandService(
                new RecordingHttpMessageHandler(),
                loggerFactory: null,
                new SessionLogFileContext(),
                processRunner,
                new FakeInteractiveConsole(canPrompt: false));

            int exitCode = await service.InstallAsync(
                new InstallCommandOptions
                {
                    BinaryPath = new FileInfo(sourceBinaryPath),
                    TelegramBotToken = "bot-token",
                    TelegramChatId = "chat-id",
                    InstallRoot = installRoot,
                    ManagedHookFilePath = new FileInfo(managedHookFilePath),
                    CopilotCliHookFilePath = new FileInfo(
                        CreateCopilotCliHookFilePath(installRoot)),
                    VsCodeSettingsPaths = CreateVsCodeSettingsOverrides(vsCodeSettingsPaths),
                },
                CancellationToken.None);

            Assert.Equal(1, exitCode);
            Assert.False(File.Exists(installedBinaryPath));
            Assert.Equal("existing-managed-pdb", File.ReadAllText(installedPdbPath));
            Assert.False(File.Exists(managedHookFilePath));
            Assert.Null(processRunner.GetSecret(AppPaths.GetTelegramBotTokenSecretPath()));
            Assert.Null(processRunner.GetSecret(AppPaths.GetTelegramChatIdSecretPath()));
            Assert.All(vsCodeSettingsPaths, settingsPath => Assert.False(File.Exists(settingsPath)));
        }
        finally
        {
            installRoot.Delete(recursive: true);
            publishDirectory.Delete(recursive: true);
        }
    }

    [Fact]
    public async Task InstallAsyncRejectsManagedHookPathMatchingVsCodeSettingsBeforeSideEffects()
    {
        DirectoryInfo installRoot = CreateHomeScopedTempSubdirectory();
        DirectoryInfo publishDirectory = Directory.CreateTempSubdirectory();
        string managedHookFilePath = Path.Combine(
            installRoot.FullName,
            AppConstants.ManagedHookFileName);
        string[] vsCodeSettingsPaths =
        [
            managedHookFilePath,
            Path.Combine(installRoot.FullName, "vscode-server-machine-settings.json"),
        ];

        try
        {
            FakeProcessRunner processRunner = new();
            UserCommandService service = CreateUserCommandService(
                new RecordingHttpMessageHandler(),
                loggerFactory: null,
                new SessionLogFileContext(),
                processRunner,
                new FakeInteractiveConsole(canPrompt: false));

            int exitCode = await service.InstallAsync(
                new InstallCommandOptions
                {
                    BinaryPath = new FileInfo(CreatePublishedBinary(publishDirectory)),
                    TelegramBotToken = "bot-token",
                    TelegramChatId = "chat-id",
                    InstallRoot = installRoot,
                    ManagedHookFilePath = new FileInfo(managedHookFilePath),
                    CopilotCliHookFilePath = new FileInfo(CreateCopilotCliHookFilePath(installRoot)),
                    VsCodeSettingsPaths = CreateVsCodeSettingsOverrides(vsCodeSettingsPaths),
                },
                CancellationToken.None);

            Assert.Equal(1, exitCode);
            Assert.False(
                File.Exists(
                    Path.Combine(
                        installRoot.FullName,
                        AppPaths.GetManagedExecutableName())));
            Assert.False(File.Exists(managedHookFilePath));
            Assert.Null(processRunner.GetSecret(AppPaths.GetTelegramBotTokenSecretPath()));
            Assert.Null(processRunner.GetSecret(AppPaths.GetTelegramChatIdSecretPath()));
            Assert.All(vsCodeSettingsPaths, settingsPath => Assert.False(File.Exists(settingsPath)));
        }
        finally
        {
            installRoot.Delete(recursive: true);
            publishDirectory.Delete(recursive: true);
        }
    }

    [Fact]
    public async Task InstallAsyncRejectsSourceBinaryPathMatchingInstalledBinaryBeforeSideEffects()
    {
        DirectoryInfo installRoot = CreateHomeScopedTempSubdirectory();
        string managedHookFilePath = Path.Combine(
            installRoot.FullName,
            AppConstants.ManagedHookFileName);
        string sourceBinaryPath = CreatePublishedBinary(installRoot);
        string[] vsCodeSettingsPaths = CreateVsCodeSettingsPaths(installRoot);

        try
        {
            FakeProcessRunner processRunner = new();
            UserCommandService service = CreateUserCommandService(
                new RecordingHttpMessageHandler(),
                loggerFactory: null,
                new SessionLogFileContext(),
                processRunner,
                new FakeInteractiveConsole(canPrompt: false));

            int exitCode = await service.InstallAsync(
                new InstallCommandOptions
                {
                    BinaryPath = new FileInfo(sourceBinaryPath),
                    TelegramBotToken = "bot-token",
                    TelegramChatId = "chat-id",
                    InstallRoot = installRoot,
                    ManagedHookFilePath = new FileInfo(managedHookFilePath),
                    CopilotCliHookFilePath = new FileInfo(CreateCopilotCliHookFilePath(installRoot)),
                    VsCodeSettingsPaths = CreateVsCodeSettingsOverrides(vsCodeSettingsPaths),
                },
                CancellationToken.None);

            Assert.Equal(1, exitCode);
            Assert.True(File.Exists(sourceBinaryPath));
            Assert.Equal("native-aot-placeholder", File.ReadAllText(sourceBinaryPath));
            Assert.False(File.Exists(managedHookFilePath));
            Assert.Null(processRunner.GetSecret(AppPaths.GetTelegramBotTokenSecretPath()));
            Assert.Null(processRunner.GetSecret(AppPaths.GetTelegramChatIdSecretPath()));
            Assert.All(vsCodeSettingsPaths, settingsPath => Assert.False(File.Exists(settingsPath)));
        }
        finally
        {
            installRoot.Delete(recursive: true);
        }
    }

    [Fact]
    public async Task UninstallAsyncRejectsIdenticalHookFilePathsBeforeSideEffects()
    {
        DirectoryInfo installRoot = CreateHomeScopedTempSubdirectory();
        string sharedHookFilePath = Path.Combine(
            installRoot.FullName,
            AppConstants.ManagedHookFileName);
        string[] vsCodeSettingsPaths = CreateVsCodeSettingsPaths(installRoot);
        string installedBinaryPath = Path.Combine(
            installRoot.FullName,
            AppPaths.GetManagedExecutableName());

        try
        {
            File.WriteAllText(installedBinaryPath, "installed-binary");
            ConfigurationApplyResult hookFileResult =
                UserHookConfigurationManager.InstallManagedHookFile(
                    sharedHookFilePath,
                    $"\"{installedBinaryPath}\" hook session-start",
                    $"\"{installedBinaryPath}\" hook user-prompt-submit",
                    $"\"{installedBinaryPath}\" hook stop",
                    "2026-03-14T00:00:00.0000000Z");
            Assert.True(hookFileResult.Applied);

            foreach (string settingsPath in vsCodeSettingsPaths)
            {
                ConfigurationApplyResult settingsResult = VsCodeSettingsManager.RegisterHookFile(
                    settingsPath,
                    sharedHookFilePath,
                    "2026-03-14T00:00:00.0000000Z");
                Assert.True(settingsResult.Applied);
            }

            string originalHookFileContent = await File.ReadAllTextAsync(
                sharedHookFilePath,
                CancellationToken.None);
            string[] originalSettingsContents =
                [.. vsCodeSettingsPaths.Select(File.ReadAllText)];
            FakeProcessRunner processRunner = new();
            processRunner.SeedSecret(AppPaths.GetTelegramBotTokenSecretPath(), "bot-token");
            processRunner.SeedSecret(AppPaths.GetTelegramChatIdSecretPath(), "chat-id");
            UserCommandService service = CreateUserCommandService(
                new RecordingHttpMessageHandler(),
                loggerFactory: null,
                new SessionLogFileContext(),
                processRunner,
                new FakeInteractiveConsole(canPrompt: false));

            int exitCode = await service.UninstallAsync(
                new UninstallCommandOptions
                {
                    InstallRoot = installRoot,
                    ManagedHookFilePath = new FileInfo(sharedHookFilePath),
                    CopilotCliHookFilePath = new FileInfo(sharedHookFilePath),
                    VsCodeSettingsPaths = CreateVsCodeSettingsOverrides(vsCodeSettingsPaths),
                    RemoveSecrets = true,
                },
                CancellationToken.None);

            Assert.Equal(1, exitCode);
            Assert.True(File.Exists(sharedHookFilePath));
            Assert.Equal(
                originalHookFileContent,
                await File.ReadAllTextAsync(sharedHookFilePath, CancellationToken.None));
            Assert.True(
                UserHookConfigurationManager.IsManagedHookFileInstalled(sharedHookFilePath));
            Assert.True(File.Exists(installedBinaryPath));
            Assert.Equal("installed-binary", File.ReadAllText(installedBinaryPath));
            Assert.All(
                vsCodeSettingsPaths,
                settingsPath => Assert.True(
                    VsCodeSettingsManager.IsHookFileRegistered(
                        settingsPath,
                        sharedHookFilePath)));
            Assert.Equal(originalSettingsContents[0], File.ReadAllText(vsCodeSettingsPaths[0]));
            Assert.Equal(originalSettingsContents[1], File.ReadAllText(vsCodeSettingsPaths[1]));
            Assert.Equal(
                "bot-token",
                processRunner.GetSecret(AppPaths.GetTelegramBotTokenSecretPath()));
            Assert.Equal(
                "chat-id",
                processRunner.GetSecret(AppPaths.GetTelegramChatIdSecretPath()));
        }
        finally
        {
            installRoot.Delete(recursive: true);
        }
    }

    [Fact]
    public async Task UninstallAsyncRejectsExistingInapplicableSettingsPathCollisionBeforeSideEffects()
    {
        DirectoryInfo installRoot = CreateHomeScopedTempSubdirectory();
        string managedHookFilePath = Path.Combine(
            installRoot.FullName,
            AppConstants.ManagedHookFileName);
        string copilotCliHookFilePath = CreateCopilotCliHookFilePath(installRoot);
        string installedBinaryPath = Path.Combine(
            installRoot.FullName,
            AppPaths.GetManagedExecutableName());
        string[] vsCodeSettingsPaths =
        [
            Path.Combine(installRoot.FullName, "vscode-user-settings.json"),
            managedHookFilePath,
        ];

        try
        {
            File.WriteAllText(installedBinaryPath, "installed-binary");
            ConfigurationApplyResult hookFileResult =
                UserHookConfigurationManager.InstallManagedHookFile(
                    managedHookFilePath,
                    $"\"{installedBinaryPath}\" hook session-start",
                    $"\"{installedBinaryPath}\" hook user-prompt-submit",
                    $"\"{installedBinaryPath}\" hook stop",
                    "2026-03-14T00:00:00.0000000Z");
            Assert.True(hookFileResult.Applied);
            ConfigurationApplyResult copilotCliHookFileResult =
                UserHookConfigurationManager.InstallManagedCopilotCliHookFile(
                    copilotCliHookFilePath,
                    $"\"{installedBinaryPath}\" hook session-start",
                    $"\"{installedBinaryPath}\" hook user-prompt-submit",
                    $"\"{installedBinaryPath}\" hook stop",
                    "2026-03-14T00:00:00.0000000Z");
            Assert.True(copilotCliHookFileResult.Applied);

            FakeProcessRunner processRunner = new();
            processRunner.SeedSecret(AppPaths.GetTelegramBotTokenSecretPath(), "bot-token");
            processRunner.SeedSecret(AppPaths.GetTelegramChatIdSecretPath(), "chat-id");
            UserCommandService service = CreateUserCommandService(
                new RecordingHttpMessageHandler(),
                loggerFactory: null,
                new SessionLogFileContext(),
                processRunner,
                new FakeInteractiveConsole(canPrompt: false));

            int exitCode = await service.UninstallAsync(
                new UninstallCommandOptions
                {
                    InstallRoot = installRoot,
                    ManagedHookFilePath = new FileInfo(managedHookFilePath),
                    CopilotCliHookFilePath = new FileInfo(copilotCliHookFilePath),
                    VsCodeSettingsTargets = CreateVsCodeSettingsTargets(
                        vsCodeSettingsPaths,
                        serverApplicable: false),
                    RemoveSecrets = true,
                },
                CancellationToken.None);

            Assert.Equal(1, exitCode);
            Assert.True(File.Exists(copilotCliHookFilePath));
            Assert.True(File.Exists(installedBinaryPath));
            Assert.True(File.Exists(managedHookFilePath));
            Assert.Equal(
                "bot-token",
                processRunner.GetSecret(AppPaths.GetTelegramBotTokenSecretPath()));
            Assert.Equal(
                "chat-id",
                processRunner.GetSecret(AppPaths.GetTelegramChatIdSecretPath()));
        }
        finally
        {
            installRoot.Delete(recursive: true);
        }
    }

    [Fact]
    public async Task UninstallAsyncRejectsCopilotCliHookPathMatchingInstalledBinaryBeforeSideEffects()
    {
        DirectoryInfo installRoot = CreateHomeScopedTempSubdirectory();
        string managedHookFilePath = Path.Combine(
            installRoot.FullName,
            AppConstants.ManagedHookFileName);
        string installedBinaryPath = Path.Combine(
            installRoot.FullName,
            AppPaths.GetManagedExecutableName());
        string[] vsCodeSettingsPaths = CreateVsCodeSettingsPaths(installRoot);

        try
        {
            File.WriteAllText(installedBinaryPath, "installed-binary");
            FakeProcessRunner processRunner = new();
            processRunner.SeedSecret(AppPaths.GetTelegramBotTokenSecretPath(), "bot-token");
            processRunner.SeedSecret(AppPaths.GetTelegramChatIdSecretPath(), "chat-id");
            UserCommandService service = CreateUserCommandService(
                new RecordingHttpMessageHandler(),
                loggerFactory: null,
                new SessionLogFileContext(),
                processRunner,
                new FakeInteractiveConsole(canPrompt: false));

            int exitCode = await service.UninstallAsync(
                new UninstallCommandOptions
                {
                    InstallRoot = installRoot,
                    ManagedHookFilePath = new FileInfo(managedHookFilePath),
                    CopilotCliHookFilePath = new FileInfo(installedBinaryPath),
                    VsCodeSettingsPaths = CreateVsCodeSettingsOverrides(vsCodeSettingsPaths),
                    RemoveSecrets = true,
                },
                CancellationToken.None);

            Assert.Equal(1, exitCode);
            Assert.True(File.Exists(installedBinaryPath));
            Assert.Equal("installed-binary", File.ReadAllText(installedBinaryPath));
            Assert.Equal(
                "bot-token",
                processRunner.GetSecret(AppPaths.GetTelegramBotTokenSecretPath()));
            Assert.Equal(
                "chat-id",
                processRunner.GetSecret(AppPaths.GetTelegramChatIdSecretPath()));
        }
        finally
        {
            installRoot.Delete(recursive: true);
        }
    }

    [Fact]
    public async Task UninstallAsyncDoesNotDeleteManagedArtifactsWhenSettingsRemovalWriteFails()
    {
        DirectoryInfo installRoot = CreateHomeScopedTempSubdirectory();
        DirectoryInfo publishDirectory = Directory.CreateTempSubdirectory();
        string managedHookFilePath = Path.Combine(
            installRoot.FullName,
            AppConstants.ManagedHookFileName);
        string[] vsCodeSettingsPaths = CreateVsCodeSettingsPaths(installRoot);

        try
        {
            FakeProcessRunner processRunner = new();
            UserCommandService service = CreateUserCommandService(
                new RecordingHttpMessageHandler(),
                loggerFactory: null,
                new SessionLogFileContext(),
                processRunner,
                new FakeInteractiveConsole(canPrompt: false));

            int installExitCode = await service.InstallAsync(
                new InstallCommandOptions
                {
                    BinaryPath = new FileInfo(CreatePublishedBinary(publishDirectory)),
                    TelegramBotToken = "bot-token",
                    TelegramChatId = "chat-id",
                    InstallRoot = installRoot,
                    ManagedHookFilePath = new FileInfo(managedHookFilePath),
                    CopilotCliHookFilePath = new FileInfo(
                        CreateCopilotCliHookFilePath(installRoot)),
                    VsCodeSettingsPaths = CreateVsCodeSettingsOverrides(vsCodeSettingsPaths),
                    SkipSecretPrompt = true,
                },
                CancellationToken.None);

            Assert.Equal(0, installExitCode);

            using IDisposable _ = AtomicTextFileWriter.UseWriterForTesting(
                new FailOnWriteNumbersTextFileWriter(2));

            int uninstallExitCode = await service.UninstallAsync(
                new UninstallCommandOptions
                {
                    InstallRoot = installRoot,
                    ManagedHookFilePath = new FileInfo(managedHookFilePath),
                    CopilotCliHookFilePath = new FileInfo(
                        CreateCopilotCliHookFilePath(installRoot)),
                    VsCodeSettingsPaths = CreateVsCodeSettingsOverrides(vsCodeSettingsPaths),
                    RemoveSecrets = true,
                },
                CancellationToken.None);

            Assert.Equal(1, uninstallExitCode);
            Assert.True(
                File.Exists(
                    Path.Combine(
                        installRoot.FullName,
                        AppPaths.GetManagedExecutableName())));
            Assert.True(File.Exists(managedHookFilePath));
            Assert.False(File.Exists(CreateCopilotCliHookFilePath(installRoot)));
            Assert.True(
                VsCodeSettingsManager.IsHookFileRegistered(
                    vsCodeSettingsPaths[0],
                    managedHookFilePath));
            Assert.True(
                VsCodeSettingsManager.IsHookFileRegistered(
                    vsCodeSettingsPaths[1],
                    managedHookFilePath));
            Assert.Equal(
                "bot-token",
                processRunner.GetSecret(AppPaths.GetTelegramBotTokenSecretPath()));
            Assert.Equal(
                "chat-id",
                processRunner.GetSecret(AppPaths.GetTelegramChatIdSecretPath()));
        }
        finally
        {
            installRoot.Delete(recursive: true);
            publishDirectory.Delete(recursive: true);
        }
    }

    [Fact]
    public async Task UninstallAsyncPreservesBinaryAndSecretsWhenManagedHookFileCannotBeParsed()
    {
        DirectoryInfo installRoot = CreateHomeScopedTempSubdirectory();
        string managedHookFilePath = Path.Combine(
            installRoot.FullName,
            AppConstants.ManagedHookFileName);
        string copilotCliHookFilePath = CreateCopilotCliHookFilePath(installRoot);
        string[] vsCodeSettingsPaths = CreateVsCodeSettingsPaths(installRoot);
        string installedBinaryPath = Path.Combine(
            installRoot.FullName,
            AppPaths.GetManagedExecutableName());

        try
        {
            File.WriteAllText(installedBinaryPath, "installed-binary");
            File.WriteAllText(managedHookFilePath, "{ invalid json");
            ConfigurationApplyResult copilotCliHookFileResult =
                UserHookConfigurationManager.InstallManagedCopilotCliHookFile(
                    copilotCliHookFilePath,
                    $"\"{installedBinaryPath}\" hook session-start",
                    $"\"{installedBinaryPath}\" hook user-prompt-submit",
                    $"\"{installedBinaryPath}\" hook stop",
                    "2026-03-14T00:00:00.0000000Z");
            Assert.True(copilotCliHookFileResult.Applied);

            foreach (string settingsPath in vsCodeSettingsPaths)
            {
                ConfigurationApplyResult settingsResult = VsCodeSettingsManager.RegisterHookFile(
                    settingsPath,
                    managedHookFilePath,
                    "2026-03-14T00:00:00.0000000Z");
                Assert.True(settingsResult.Applied);
            }

            FakeProcessRunner processRunner = new();
            processRunner.SeedSecret(AppPaths.GetTelegramBotTokenSecretPath(), "bot-token");
            processRunner.SeedSecret(AppPaths.GetTelegramChatIdSecretPath(), "chat-id");
            UserCommandService service = CreateUserCommandService(
                new RecordingHttpMessageHandler(),
                loggerFactory: null,
                new SessionLogFileContext(),
                processRunner,
                new FakeInteractiveConsole(canPrompt: false));

            int uninstallExitCode = await service.UninstallAsync(
                new UninstallCommandOptions
                {
                    InstallRoot = installRoot,
                    ManagedHookFilePath = new FileInfo(managedHookFilePath),
                    CopilotCliHookFilePath = new FileInfo(copilotCliHookFilePath),
                    VsCodeSettingsPaths = CreateVsCodeSettingsOverrides(vsCodeSettingsPaths),
                    RemoveSecrets = true,
                },
                CancellationToken.None);

            Assert.Equal(1, uninstallExitCode);
            Assert.True(File.Exists(installedBinaryPath));
            Assert.Equal("installed-binary", File.ReadAllText(installedBinaryPath));
            Assert.Equal("{ invalid json", File.ReadAllText(managedHookFilePath));
            Assert.False(File.Exists(copilotCliHookFilePath));
            Assert.All(
                vsCodeSettingsPaths,
                settingsPath => Assert.False(
                    VsCodeSettingsManager.IsHookFileRegistered(
                        settingsPath,
                        managedHookFilePath)));
            Assert.Equal(
                "bot-token",
                processRunner.GetSecret(AppPaths.GetTelegramBotTokenSecretPath()));
            Assert.Equal(
                "chat-id",
                processRunner.GetSecret(AppPaths.GetTelegramChatIdSecretPath()));
        }
        finally
        {
            installRoot.Delete(recursive: true);
        }
    }

    [Fact]
    public async Task SecretAsyncSupportsSetAndReadModes()
    {
        DirectoryInfo installRoot = Directory.CreateTempSubdirectory();

        try
        {
            FakeProcessRunner processRunner = new();
            UserCommandService service = CreateUserCommandService(
                new RecordingHttpMessageHandler(),
                loggerFactory: null,
                new SessionLogFileContext(),
                processRunner,
                new FakeInteractiveConsole(canPrompt: false));

            int setExitCode = await service.SecretAsync(
                new SecretCommandOptions
                {
                    TelegramBotToken = "set-token",
                    TelegramChatId = "set-chat-id",
                    InstallRoot = installRoot,
                },
                CancellationToken.None);

            int readExitCode = await service.SecretAsync(
                new SecretCommandOptions
                {
                    InstallRoot = installRoot,
                },
                CancellationToken.None);

            Assert.Equal(0, setExitCode);
            Assert.Equal(0, readExitCode);
            Assert.Equal(
                "set-token",
                processRunner.GetSecret(AppPaths.GetTelegramBotTokenSecretPath()));
            Assert.Equal(
                "set-chat-id",
                processRunner.GetSecret(AppPaths.GetTelegramChatIdSecretPath()));
        }
        finally
        {
            installRoot.Delete(recursive: true);
        }
    }

    private static UserCommandService CreateUserCommandService(
        RecordingHttpMessageHandler handler,
        ILoggerFactory? loggerFactory,
        SessionLogFileContext logContext,
        IProcessRunner? processRunner = null,
        IInteractiveConsole? interactiveConsole = null)
    {
        HttpClient httpClient = new(handler)
        {
            BaseAddress = new Uri("https://api.telegram.org/"),
        };

        IProcessRunner effectiveProcessRunner = processRunner
            ?? new ProcessRunner(CreateLogger<ProcessRunner>(loggerFactory));
        IInteractiveConsole effectiveInteractiveConsole = interactiveConsole
            ?? new SystemInteractiveConsole();
        TelegramCredentialProvider credentialProvider = new(
            effectiveProcessRunner,
            effectiveInteractiveConsole,
            CreateLogger<TelegramCredentialProvider>(loggerFactory));

        return new UserCommandService(
            new TelegramBotClient(httpClient, CreateLogger<TelegramBotClient>(loggerFactory)),
            credentialProvider,
            logContext,
            TimeProvider.System,
            CreateLogger<UserCommandService>(loggerFactory));
    }

    private static ILogger<T> CreateLogger<T>(ILoggerFactory? loggerFactory)
        => loggerFactory?.CreateLogger<T>() ?? NullLogger<T>.Instance;

    private static string CreatePublishedBinary(DirectoryInfo directory)
    {
        string binaryPath = Path.Combine(directory.FullName, AppPaths.GetManagedExecutableName());
        File.WriteAllText(binaryPath, "native-aot-placeholder");

        if (!OperatingSystem.IsWindows())
        {
            try
            {
                File.SetUnixFileMode(
                    binaryPath,
                    UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
            }
            catch (PlatformNotSupportedException)
            {
            }
        }

        return binaryPath;
    }

    private static DirectoryInfo CreateHomeScopedTempSubdirectory()
    {
        string path = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
            ".tmp",
            "hcoona-vscode-copilot-telegram-hook-tests",
            Guid.NewGuid().ToString("n"));
        return Directory.CreateDirectory(path);
    }

    private static string[] CreateVsCodeSettingsPaths(DirectoryInfo installRoot)
    {
        return
        [
            Path.Combine(installRoot.FullName, "vscode-user-settings.json"),
            Path.Combine(installRoot.FullName, "vscode-server-machine-settings.json"),
        ];
    }

    private static string CreateCopilotCliHookFilePath(DirectoryInfo installRoot)
        => Path.Combine(
            installRoot.FullName,
            "copilot-cli-hooks",
            AppConstants.CopilotCliHookFileName);

    private static FileInfo[] CreateVsCodeSettingsOverrides(IEnumerable<string> settingsPaths)
        => [.. settingsPaths.Select(static settingsPath => new FileInfo(settingsPath))];

    private static Dictionary<string, string> ReadDirectoryFileContents(string directoryPath)
    {
        return Directory.EnumerateFiles(directoryPath, "*", SearchOption.AllDirectories)
            .OrderBy(static filePath => filePath, StringComparer.Ordinal)
            .ToDictionary(
                filePath => Path.GetRelativePath(directoryPath, filePath),
                File.ReadAllText,
                StringComparer.Ordinal);
    }

    private static VsCodeSettingsTarget[] CreateVsCodeSettingsTargets(
        string[] settingsPaths,
        bool serverApplicable)
    {
        return
        [
            new VsCodeSettingsTarget(
                settingsPaths[0],
                IsApplicable: true,
                DisplayName: "VS Code desktop user settings"),
            new VsCodeSettingsTarget(
                settingsPaths[1],
                IsApplicable: serverApplicable,
                DisplayName: "VS Code Server Machine settings",
                InapplicableReason: serverApplicable
                    ? null
                    : "No same-host VS Code Server installation was detected under "
                    + "'~/.vscode-server'."),
        ];
    }

    private sealed class FakeProcessRunner : IProcessRunner
    {
        private readonly Dictionary<string, string> secrets = new(StringComparer.Ordinal);

        public Task<ProcessExecutionResult> RunAsync(
            string fileName,
            IReadOnlyList<string> arguments,
            string? workingDirectory,
            string? standardInput,
            ProcessLogOptions? logOptions,
            CancellationToken cancellationToken)
        {
            if (!string.Equals(fileName, "gopass", StringComparison.Ordinal))
            {
                throw new InvalidOperationException($"Unexpected process '{fileName}'.");
            }

            if (arguments.Count == 1
                && string.Equals(arguments[0], "version", StringComparison.Ordinal))
            {
                return Task.FromResult(new ProcessExecutionResult(0, "gopass 1.0.0", string.Empty));
            }

            if (arguments.Count >= 2
                && string.Equals(arguments[0], "show", StringComparison.Ordinal))
            {
                string secretPath = arguments[1];
                return Task.FromResult(
                    secrets.TryGetValue(secretPath, out string? value)
                        ? new ProcessExecutionResult(0, value + Environment.NewLine, string.Empty)
                        : new ProcessExecutionResult(1, string.Empty, "secret not found"));
            }

            if (arguments.Count >= 4
                && string.Equals(arguments[0], "insert", StringComparison.Ordinal))
            {
                string secretPath = arguments[^1];
                secrets[secretPath] = (standardInput ?? string.Empty).TrimEnd('\r', '\n');
                return Task.FromResult(new ProcessExecutionResult(0, string.Empty, string.Empty));
            }

            if (arguments.Count >= 3
                && string.Equals(arguments[0], "rm", StringComparison.Ordinal))
            {
                secrets.Remove(arguments[^1]);
                return Task.FromResult(new ProcessExecutionResult(0, string.Empty, string.Empty));
            }

            throw new InvalidOperationException(
                $"Unexpected gopass invocation: {string.Join(' ', arguments)}");
        }

        public void SeedSecret(string secretPath, string value)
            => secrets[secretPath] = value;

        public string? GetSecret(string secretPath)
            => secrets.TryGetValue(secretPath, out string? value) ? value : null;
    }

    private sealed class FailOnWriteNumbersTextFileWriter(params int[] failureWriteNumbers)
        : ITextFileWriter
    {
        private readonly HashSet<int> failureNumbers = [.. failureWriteNumbers];
        private int writeCount;

        public void WriteAllText(string path, string content)
        {
            writeCount++;
            if (failureNumbers.Contains(writeCount))
            {
                throw new IOException("Simulated write failure.");
            }

            File.WriteAllText(path, content);
        }
    }

    private sealed class CorruptFileOnWriteNumberTextFileWriter(
        int failureWriteNumber,
        string targetPath,
        string corruptedContent) : ITextFileWriter
    {
        private int writeCount;

        public void WriteAllText(string path, string content)
        {
            writeCount++;
            if (writeCount == failureWriteNumber)
            {
                File.WriteAllText(targetPath, corruptedContent);
                throw new IOException("Simulated write failure.");
            }

            File.WriteAllText(path, content);
        }
    }

    private sealed class FakeInteractiveConsole(
        bool canPrompt,
        IEnumerable<bool>? confirmResponses = null) : IInteractiveConsole
    {
        private readonly Queue<bool> confirmQueue = new(confirmResponses ?? []);

        public bool CanPrompt { get; } = canPrompt;

        public List<string> ConfirmationPrompts { get; } = [];

        public List<string> SecretPrompts { get; } = [];

        public List<string> LinePrompts { get; } = [];

        public bool Confirm(string prompt, bool defaultAnswer)
        {
            ConfirmationPrompts.Add(prompt);
            return confirmQueue.Count > 0 ? confirmQueue.Dequeue() : defaultAnswer;
        }

        public string ReadSecret(string prompt)
        {
            SecretPrompts.Add(prompt);
            return string.Empty;
        }

        public string ReadLine(string prompt)
        {
            LinePrompts.Add(prompt);
            return string.Empty;
        }
    }
}
