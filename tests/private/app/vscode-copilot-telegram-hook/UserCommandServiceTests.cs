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
        DirectoryInfo installRoot = Directory.CreateTempSubdirectory();
        DirectoryInfo instructionsDirectory = Directory.CreateTempSubdirectory();
        string managedHookFilePath = Path.Combine(
            installRoot.FullName,
            AppConstants.ManagedHookFileName);
        string vsCodeSettingsPath = Path.Combine(installRoot.FullName, "vscode-user-settings.json");

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
                    InstructionsDirectory = instructionsDirectory,
                    ManagedHookFilePath = new FileInfo(managedHookFilePath),
                    VsCodeSettingsPath = new FileInfo(vsCodeSettingsPath),
                },
                CancellationToken.None);

            Assert.Equal(0, exitCode);

            string logPath = AppPaths.GetUserLogPath(installRoot.FullName);
            Assert.True(File.Exists(logPath));

            string logContent = await File.ReadAllTextAsync(logPath, CancellationToken.None);
            Assert.Contains(
                "Starting user diagnose command",
                logContent,
                StringComparison.Ordinal);
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
            instructionsDirectory.Delete(recursive: true);
        }
    }

    [Fact]
    public async Task InstallAsyncWhenSecretsAlreadyExistAsksBeforeKeepingThem()
    {
        DirectoryInfo installRoot = Directory.CreateTempSubdirectory();
        DirectoryInfo instructionsDirectory = Directory.CreateTempSubdirectory();
        DirectoryInfo publishDirectory = Directory.CreateTempSubdirectory();
        string managedHookFilePath = Path.Combine(
            installRoot.FullName,
            AppConstants.ManagedHookFileName);
        string vsCodeSettingsPath = Path.Combine(installRoot.FullName, "vscode-user-settings.json");

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
                    InstructionsDirectory = instructionsDirectory,
                    ManagedHookFilePath = new FileInfo(managedHookFilePath),
                    VsCodeSettingsPath = new FileInfo(vsCodeSettingsPath),
                },
                CancellationToken.None);

            Assert.Equal(0, exitCode);
            Assert.True(
                UserHookConfigurationManager.IsManagedHookFileInstalled(managedHookFilePath));
            Assert.True(
                VsCodeSettingsManager.IsHookFileRegistered(
                    vsCodeSettingsPath,
                    managedHookFilePath));
            Assert.Equal(
                "old-token",
                processRunner.GetSecret(AppPaths.GetTelegramBotTokenSecretPath()));
            Assert.Equal(
                "old-chat-id",
                processRunner.GetSecret(AppPaths.GetTelegramChatIdSecretPath()));
            Assert.Equal(2, interactiveConsole.ConfirmationPrompts.Count);
            Assert.Empty(interactiveConsole.SecretPrompts);
            Assert.Empty(interactiveConsole.LinePrompts);
        }
        finally
        {
            installRoot.Delete(recursive: true);
            instructionsDirectory.Delete(recursive: true);
            publishDirectory.Delete(recursive: true);
        }
    }

    [Fact]
    public async Task InstallAsyncWhenPromptsAreDisabledKeepsExistingSecrets()
    {
        DirectoryInfo installRoot = Directory.CreateTempSubdirectory();
        DirectoryInfo instructionsDirectory = Directory.CreateTempSubdirectory();
        DirectoryInfo publishDirectory = Directory.CreateTempSubdirectory();
        string managedHookFilePath = Path.Combine(
            installRoot.FullName,
            AppConstants.ManagedHookFileName);
        string vsCodeSettingsPath = Path.Combine(installRoot.FullName, "vscode-user-settings.json");

        try
        {
            FakeProcessRunner processRunner = new();
            processRunner.SeedSecret(AppPaths.GetTelegramBotTokenSecretPath(), "old-token");
            processRunner.SeedSecret(AppPaths.GetTelegramChatIdSecretPath(), "old-chat-id");

            FakeInteractiveConsole interactiveConsole = new(canPrompt: false);
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
                    TelegramBotToken = "new-token",
                    TelegramChatId = "new-chat-id",
                    InstallRoot = installRoot,
                    InstructionsDirectory = instructionsDirectory,
                    ManagedHookFilePath = new FileInfo(managedHookFilePath),
                    VsCodeSettingsPath = new FileInfo(vsCodeSettingsPath),
                },
                CancellationToken.None);

            Assert.Equal(0, exitCode);
            Assert.True(
                UserHookConfigurationManager.IsManagedHookFileInstalled(managedHookFilePath));
            Assert.True(
                VsCodeSettingsManager.IsHookFileRegistered(
                    vsCodeSettingsPath,
                    managedHookFilePath));
            Assert.Equal(
                "old-token",
                processRunner.GetSecret(AppPaths.GetTelegramBotTokenSecretPath()));
            Assert.Equal(
                "old-chat-id",
                processRunner.GetSecret(AppPaths.GetTelegramChatIdSecretPath()));
            Assert.Empty(interactiveConsole.ConfirmationPrompts);
        }
        finally
        {
            installRoot.Delete(recursive: true);
            instructionsDirectory.Delete(recursive: true);
            publishDirectory.Delete(recursive: true);
        }
    }

    [Fact]
    public async Task UninstallAsyncRemovesManagedHookFileAndVsCodeRegistration()
    {
        DirectoryInfo installRoot = Directory.CreateTempSubdirectory();
        DirectoryInfo instructionsDirectory = Directory.CreateTempSubdirectory();
        DirectoryInfo publishDirectory = Directory.CreateTempSubdirectory();
        string managedHookFilePath = Path.Combine(
            installRoot.FullName,
            AppConstants.ManagedHookFileName);
        string vsCodeSettingsPath = Path.Combine(installRoot.FullName, "vscode-user-settings.json");

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
                    InstallRoot = installRoot,
                    InstructionsDirectory = instructionsDirectory,
                    ManagedHookFilePath = new FileInfo(managedHookFilePath),
                    VsCodeSettingsPath = new FileInfo(vsCodeSettingsPath),
                    SkipSecretPrompt = true,
                },
                CancellationToken.None);

            int uninstallExitCode = await service.UninstallAsync(
                new UninstallCommandOptions
                {
                    InstallRoot = installRoot,
                    InstructionsDirectory = instructionsDirectory,
                    ManagedHookFilePath = new FileInfo(managedHookFilePath),
                    VsCodeSettingsPath = new FileInfo(vsCodeSettingsPath),
                },
                CancellationToken.None);

            Assert.Equal(0, installExitCode);
            Assert.Equal(0, uninstallExitCode);
            Assert.False(File.Exists(managedHookFilePath));
            Assert.False(
                VsCodeSettingsManager.IsHookFileRegistered(
                    vsCodeSettingsPath,
                    managedHookFilePath));
        }
        finally
        {
            installRoot.Delete(recursive: true);
            instructionsDirectory.Delete(recursive: true);
            publishDirectory.Delete(recursive: true);
        }
    }

    [Fact]
    public async Task HealthAsyncReportsHealthyWhenManagedHookFileAndRegistrationExist()
    {
        DirectoryInfo installRoot = Directory.CreateTempSubdirectory();
        DirectoryInfo instructionsDirectory = Directory.CreateTempSubdirectory();
        DirectoryInfo publishDirectory = Directory.CreateTempSubdirectory();
        string managedHookFilePath = Path.Combine(
            installRoot.FullName,
            AppConstants.ManagedHookFileName);
        string vsCodeSettingsPath = Path.Combine(installRoot.FullName, "vscode-user-settings.json");

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
                    InstructionsDirectory = instructionsDirectory,
                    ManagedHookFilePath = new FileInfo(managedHookFilePath),
                    VsCodeSettingsPath = new FileInfo(vsCodeSettingsPath),
                    SkipSecretPrompt = true,
                },
                CancellationToken.None);

            int healthExitCode = await service.HealthAsync(
                new UserPathOverrides
                {
                    InstallRoot = installRoot,
                    InstructionsDirectory = instructionsDirectory,
                    ManagedHookFilePath = new FileInfo(managedHookFilePath),
                    VsCodeSettingsPath = new FileInfo(vsCodeSettingsPath),
                },
                CancellationToken.None);

            Assert.Equal(0, installExitCode);
            Assert.Equal(0, healthExitCode);
        }
        finally
        {
            installRoot.Delete(recursive: true);
            instructionsDirectory.Delete(recursive: true);
            publishDirectory.Delete(recursive: true);
        }
    }

    [Fact]
    public async Task InstallAsyncDoesNotRegisterVsCodeSettingsWhenManagedHookFileInstallFails()
    {
        DirectoryInfo installRoot = Directory.CreateTempSubdirectory();
        DirectoryInfo instructionsDirectory = Directory.CreateTempSubdirectory();
        DirectoryInfo publishDirectory = Directory.CreateTempSubdirectory();
        string managedHookFilePath = Path.Combine(
            installRoot.FullName,
            AppConstants.ManagedHookFileName);
        string vsCodeSettingsPath = Path.Combine(installRoot.FullName, "vscode-user-settings.json");

        try
        {
            File.WriteAllText(managedHookFilePath, "{ invalid json");

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
                    InstructionsDirectory = instructionsDirectory,
                    ManagedHookFilePath = new FileInfo(managedHookFilePath),
                    VsCodeSettingsPath = new FileInfo(vsCodeSettingsPath),
                },
                CancellationToken.None);

            Assert.Equal(1, exitCode);
            Assert.False(File.Exists(vsCodeSettingsPath));
            Assert.False(
                VsCodeSettingsManager.IsHookFileRegistered(
                    vsCodeSettingsPath,
                    managedHookFilePath));
        }
        finally
        {
            installRoot.Delete(recursive: true);
            instructionsDirectory.Delete(recursive: true);
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
            new InstructionTemplateProvider(),
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

    private sealed class FakeInteractiveConsole(
        bool canPrompt,
        IEnumerable<bool>? confirmResponses = null,
        IEnumerable<string>? secretResponses = null,
        IEnumerable<string>? lineResponses = null) : IInteractiveConsole
    {
        private readonly Queue<bool> confirmQueue = new(confirmResponses ?? []);
        private readonly Queue<string> secretQueue = new(secretResponses ?? []);
        private readonly Queue<string> lineQueue = new(lineResponses ?? []);

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
            return secretQueue.Count > 0 ? secretQueue.Dequeue() : string.Empty;
        }

        public string ReadLine(string prompt)
        {
            LinePrompts.Add(prompt);
            return lineQueue.Count > 0 ? lineQueue.Dequeue() : string.Empty;
        }
    }
}
