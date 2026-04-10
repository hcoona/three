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
                    VsCodeSettingsPaths = CreateVsCodeSettingsOverrides(vsCodeSettingsPaths),
                },
                CancellationToken.None);

            Assert.Equal(0, exitCode);
            Assert.True(
                UserHookConfigurationManager.IsManagedHookFileInstalled(
                    managedHookFilePath));
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
                    VsCodeSettingsPaths = CreateVsCodeSettingsOverrides(vsCodeSettingsPaths),
                    SkipSecretPrompt = true,
                },
                CancellationToken.None);

            int uninstallExitCode = await service.UninstallAsync(
                new UninstallCommandOptions
                {
                    InstallRoot = installRoot,
                    ManagedHookFilePath = new FileInfo(managedHookFilePath),
                    VsCodeSettingsPaths = CreateVsCodeSettingsOverrides(vsCodeSettingsPaths),
                },
                CancellationToken.None);

            Assert.Equal(0, installExitCode);
            Assert.Equal(0, uninstallExitCode);
            Assert.False(File.Exists(managedHookFilePath));
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
                    VsCodeSettingsPaths = CreateVsCodeSettingsOverrides(vsCodeSettingsPaths),
                    SkipSecretPrompt = true,
                },
                CancellationToken.None);

            int healthExitCode = await service.HealthAsync(
                new UserPathOverrides
                {
                    InstallRoot = installRoot,
                    ManagedHookFilePath = new FileInfo(managedHookFilePath),
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
                    VsCodeSettingsTargets = settingsTargets,
                    SkipSecretPrompt = true,
                },
                CancellationToken.None);

            int healthExitCode = await service.HealthAsync(
                new UserPathOverrides
                {
                    InstallRoot = installRoot,
                    ManagedHookFilePath = new FileInfo(managedHookFilePath),
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
        }
        finally
        {
            installRoot.Delete(recursive: true);
            publishDirectory.Delete(recursive: true);
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

    private static FileInfo[] CreateVsCodeSettingsOverrides(IEnumerable<string> settingsPaths)
        => [.. settingsPaths.Select(static settingsPath => new FileInfo(settingsPath))];

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
