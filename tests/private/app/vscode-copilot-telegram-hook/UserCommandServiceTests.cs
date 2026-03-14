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
                    HookSettingsPath = new FileInfo(
                        Path.Combine(installRoot.FullName, "settings.json")),
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

    private static UserCommandService CreateUserCommandService(
        RecordingHttpMessageHandler handler,
        ILoggerFactory? loggerFactory,
        SessionLogFileContext logContext)
    {
        HttpClient httpClient = new(handler)
        {
            BaseAddress = new Uri("https://api.telegram.org/"),
        };

        ProcessRunner processRunner = new(CreateLogger<ProcessRunner>(loggerFactory));
        TelegramCredentialProvider credentialProvider = new(
            processRunner,
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
}
