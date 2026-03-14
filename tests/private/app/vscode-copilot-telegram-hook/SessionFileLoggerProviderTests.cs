using Hcoona.VsCodeCopilotTelegramHook.Logging;
using Microsoft.Extensions.Logging;
using Xunit;

namespace Hcoona.VsCodeCopilotTelegramHook.Tests;

public sealed class SessionFileLoggerProviderTests
{
    [Fact]
    public void ProviderIgnoresExternalCategories()
    {
        DirectoryInfo tempDirectory = Directory.CreateTempSubdirectory();

        try
        {
            SessionLogFileContext logContext = new();
            using ILoggerFactory loggerFactory = LoggerFactory.Create(builder =>
            {
                builder.ClearProviders();
                builder.SetMinimumLevel(LogLevel.Debug);
                builder.AddProvider(new SessionFileLoggerProvider(logContext));
            });

            string logPath = AppPaths.GetSessionLogPath(tempDirectory.FullName, "session-123");
            using IDisposable logScope = logContext.UseLogFile(logPath);

            ILogger logger = loggerFactory.CreateLogger("System.Net.Http.HttpClient");
            logger.Log(
                LogLevel.Warning,
                new EventId(1, "ExternalWarning"),
                "Request to /bot123456:ABCdef_token/sendMessage failed.",
                exception: null,
                static (state, _) => state);

            Assert.False(File.Exists(logPath));
        }
        finally
        {
            tempDirectory.Delete(recursive: true);
        }
    }

    [Fact]
    public async Task ProviderSanitizesHomePathsAndTelegramRequestUris()
    {
        DirectoryInfo tempDirectory = Directory.CreateTempSubdirectory();

        try
        {
            SessionLogFileContext logContext = new();
            using ILoggerFactory loggerFactory = LoggerFactory.Create(builder =>
            {
                builder.ClearProviders();
                builder.SetMinimumLevel(LogLevel.Debug);
                builder.AddProvider(new SessionFileLoggerProvider(logContext));
            });

            string logPath = AppPaths.GetSessionLogPath(tempDirectory.FullName, "session-123");
            using IDisposable logScope = logContext.UseLogFile(logPath);

            string homePath = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            string workspacePath = Path.Combine(homePath, "workspace");

            ILogger logger = loggerFactory.CreateLogger(
                "Hcoona.VsCodeCopilotTelegramHook.Tests.Custom");
            logger.Log(
                LogLevel.Warning,
                new EventId(2, "SanitizedWarning"),
                $"Request https://api.telegram.org/bot123456:ABCdef_token/sendMessage failed in "
                + $"{workspacePath}",
                exception: null,
                static (state, _) => state);

            string logContent = await File.ReadAllTextAsync(logPath, CancellationToken.None);
            Assert.Contains("/bot<redacted>/sendMessage", logContent, StringComparison.Ordinal);
            Assert.DoesNotContain(homePath, logContent, StringComparison.Ordinal);
            Assert.Contains(
                $"~{Path.DirectorySeparatorChar}workspace",
                logContent,
                StringComparison.Ordinal);
        }
        finally
        {
            tempDirectory.Delete(recursive: true);
        }
    }
}
