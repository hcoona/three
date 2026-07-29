using Hcoona.VsCodeCopilotTelegramHook.Logging;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Hcoona.VsCodeCopilotTelegramHook.Tests;

[Collection(TelegramEnvironmentTestGroup.Name)]
public sealed class TelegramCredentialProviderTests
{
    [Fact]
    public async Task TryResolveAsyncLogsUnexpectedGopassReadFailure()
    {
        DirectoryInfo temporaryDirectory = Directory.CreateTempSubdirectory();
        string logPath = Path.Combine(temporaryDirectory.FullName, "credentials.log");
        string? originalBotToken = Environment.GetEnvironmentVariable(
            AppConstants.TelegramBotTokenEnvironmentVariable);
        string? originalChatId = Environment.GetEnvironmentVariable(
            AppConstants.TelegramChatIdEnvironmentVariable);

        try
        {
            Environment.SetEnvironmentVariable(
                AppConstants.TelegramBotTokenEnvironmentVariable,
                null);
            Environment.SetEnvironmentVariable(
                AppConstants.TelegramChatIdEnvironmentVariable,
                null);
            SessionLogFileContext logContext = new();
            using ILoggerFactory loggerFactory = LoggerFactory.Create(builder =>
            {
                builder.ClearProviders();
                builder.SetMinimumLevel(LogLevel.Debug);
                builder.AddProvider(new SessionFileLoggerProvider(logContext));
            });
            TelegramCredentialProvider provider = new(
                new FailingReadProcessRunner(),
                new NonInteractiveConsole(),
                loggerFactory.CreateLogger<TelegramCredentialProvider>());
            using IDisposable _ = logContext.UseLogFile(logPath);

            TelegramCredentials? credentials = await provider.TryResolveAsync(
                CancellationToken.None);

            Assert.Null(credentials);
            string logContent = await File.ReadAllTextAsync(logPath, CancellationToken.None);
            Assert.Contains(
                "Failed to read Telegram secret path",
                logContent,
                StringComparison.Ordinal);
            Assert.Contains(
                "gopass backend unavailable",
                logContent,
                StringComparison.Ordinal);
            Assert.DoesNotContain(
                "gopass did not return a value",
                logContent,
                StringComparison.Ordinal);
        }
        finally
        {
            Environment.SetEnvironmentVariable(
                AppConstants.TelegramBotTokenEnvironmentVariable,
                originalBotToken);
            Environment.SetEnvironmentVariable(
                AppConstants.TelegramChatIdEnvironmentVariable,
                originalChatId);
            temporaryDirectory.Delete(recursive: true);
        }
    }

    [Fact]
    public async Task ReadStoredSecretsAsyncAddsSecretPathToProcessFailure()
    {
        TelegramCredentialProvider provider = new(
            new ThrowingReadProcessRunner(),
            new NonInteractiveConsole(),
            NullLogger<TelegramCredentialProvider>.Instance);

        InvalidOperationException exception = await Assert.ThrowsAsync<InvalidOperationException>(
            () => provider.ReadStoredSecretsAsync(CancellationToken.None));

        Assert.Contains(
            AppPaths.GetTelegramBotTokenSecretPath(),
            exception.Message,
            StringComparison.Ordinal);
        Assert.IsType<InvalidOperationException>(exception.InnerException);
    }

    private sealed class FailingReadProcessRunner : IProcessRunner
    {
        public Task<ProcessExecutionResult> RunAsync(
            string fileName,
            IReadOnlyList<string> arguments,
            string? workingDirectory,
            string? standardInput,
            ProcessLogOptions? logOptions,
            CancellationToken cancellationToken)
            => Task.FromResult(
                new ProcessExecutionResult(
                    2,
                    string.Empty,
                    "gopass backend unavailable"));
    }

    private sealed class ThrowingReadProcessRunner : IProcessRunner
    {
        public Task<ProcessExecutionResult> RunAsync(
            string fileName,
            IReadOnlyList<string> arguments,
            string? workingDirectory,
            string? standardInput,
            ProcessLogOptions? logOptions,
            CancellationToken cancellationToken)
        {
            if (arguments.Count == 1 && arguments[0] == "version")
            {
                return Task.FromResult(
                    new ProcessExecutionResult(0, "gopass 1.0.0", string.Empty));
            }

            throw new InvalidOperationException("Failed to start process 'gopass'.");
        }
    }

    private sealed class NonInteractiveConsole : IInteractiveConsole
    {
        public bool CanPrompt => false;

        public bool Confirm(string prompt, bool defaultAnswer) => defaultAnswer;

        public string ReadSecret(string prompt) => string.Empty;

        public string ReadLine(string prompt) => string.Empty;
    }
}

[CollectionDefinition(Name, DisableParallelization = true)]
public sealed class TelegramEnvironmentTestGroup
{
    public const string Name = "Telegram environment";
}
