using System.Net;
using System.Text.Json;
using Hcoona.VsCodeCopilotTelegramHook.Commands;
using Hcoona.VsCodeCopilotTelegramHook.Logging;
using Hcoona.VsCodeCopilotTelegramHook.Notifications;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Hcoona.VsCodeCopilotTelegramHook.Tests;

public sealed class CopilotCliNotificationServiceTests
{
    [Fact]
    public async Task SuccessfulDeliveryDeletesTheClaimedEvent()
    {
        using TemporaryDirectory temporaryDirectory = new();
        RecordingHttpMessageHandler handler = new();
        string eventPath = await WriteEventAsync(temporaryDirectory.Path);
        using HttpClient httpClient = CreateHttpClient(handler);
        CopilotCliNotificationService service = CreateService(httpClient);

        int exitCode = await service.HandleSessionEventFileAsync(
            new FileInfo(eventPath),
            CancellationToken.None);

        Assert.Equal(0, exitCode);
        Assert.Single(handler.Requests);
        Assert.False(File.Exists(eventPath));
        Assert.False(File.Exists(eventPath + ".working"));
        Assert.False(File.Exists(eventPath + ".cancelled"));
    }

    [Fact]
    public async Task CancellationSuppressesDeliveryAndDeletesEventFiles()
    {
        using TemporaryDirectory temporaryDirectory = new();
        RecordingHttpMessageHandler handler = new();
        string eventPath = await WriteEventAsync(temporaryDirectory.Path);
        await File.WriteAllTextAsync(
            eventPath + ".cancelled",
            string.Empty,
            CancellationToken.None);
        using HttpClient httpClient = CreateHttpClient(handler);
        CopilotCliNotificationService service = CreateService(httpClient);

        int exitCode = await service.HandleSessionEventFileAsync(
            new FileInfo(eventPath),
            CancellationToken.None);

        Assert.Equal(0, exitCode);
        Assert.Empty(handler.Requests);
        Assert.False(File.Exists(eventPath));
        Assert.False(File.Exists(eventPath + ".working"));
        Assert.False(File.Exists(eventPath + ".cancelled"));
    }

    [Fact]
    public async Task DeliveryFailureRestoresReadyEventForStartupRecovery()
    {
        using TemporaryDirectory temporaryDirectory = new();
        RecordingHttpMessageHandler handler = new(
        [
            RecordingHttpMessageHandler.CreateJsonResponse(
                HttpStatusCode.BadRequest,
                """{"ok":false,"error_code":400,"description":"chat not found"}"""),
        ]);
        string eventPath = await WriteEventAsync(temporaryDirectory.Path);
        using HttpClient httpClient = CreateHttpClient(handler);
        CopilotCliNotificationService service = CreateService(httpClient);

        int exitCode = await service.HandleSessionEventFileAsync(
            new FileInfo(eventPath),
            CancellationToken.None);

        Assert.Equal(1, exitCode);
        Assert.Single(handler.Requests);
        Assert.True(File.Exists(eventPath));
        Assert.False(File.Exists(eventPath + ".working"));
    }

    [Fact]
    public async Task MalformedTelegramErrorResponseRestoresReadyEvent()
    {
        using TemporaryDirectory temporaryDirectory = new();
        RecordingHttpMessageHandler handler = new(
        [
            new HttpResponseMessage(HttpStatusCode.BadGateway)
            {
                Content = new StringContent("<html>Bad Gateway</html>"),
            },
        ]);
        string eventPath = await WriteEventAsync(temporaryDirectory.Path);
        using HttpClient httpClient = CreateHttpClient(handler);
        CopilotCliNotificationService service = CreateService(httpClient);

        int exitCode = await service.HandleSessionEventFileAsync(
            new FileInfo(eventPath),
            CancellationToken.None);

        Assert.Equal(1, exitCode);
        Assert.Single(handler.Requests);
        Assert.True(File.Exists(eventPath));
        Assert.False(File.Exists(eventPath + ".working"));
    }

    [Fact]
    public async Task InvalidEventIsDiscardedInsteadOfRetried()
    {
        using TemporaryDirectory temporaryDirectory = new();
        RecordingHttpMessageHandler handler = new();
        string eventPath = Path.Combine(temporaryDirectory.Path, "invalid.json");
        await File.WriteAllTextAsync(eventPath, "{}", CancellationToken.None);
        using HttpClient httpClient = CreateHttpClient(handler);
        CopilotCliNotificationService service = CreateService(httpClient);

        int exitCode = await service.HandleSessionEventFileAsync(
            new FileInfo(eventPath),
            CancellationToken.None);

        Assert.Equal(1, exitCode);
        Assert.Empty(handler.Requests);
        Assert.False(File.Exists(eventPath));
        Assert.False(File.Exists(eventPath + ".working"));
    }

    private static CopilotCliNotificationService CreateService(HttpClient httpClient)
    {
        TestProcessRunner processRunner = new();
        TelegramBotClient telegramBotClient = new(
            httpClient,
            NullLogger<TelegramBotClient>.Instance);
        TelegramCredentialProvider credentialProvider = new(
            processRunner,
            new NonInteractiveConsole(),
            NullLogger<TelegramCredentialProvider>.Instance);
        GitRepositoryProbe gitRepositoryProbe = new(
            processRunner,
            NullLogger<GitRepositoryProbe>.Instance);

        return new CopilotCliNotificationService(
            telegramBotClient,
            credentialProvider,
            gitRepositoryProbe,
            new SessionLogFileContext(),
            TimeProvider.System,
            NullLogger<CopilotCliNotificationService>.Instance);
    }

    private static HttpClient CreateHttpClient(HttpMessageHandler handler)
        => new(handler)
        {
            BaseAddress = new Uri("https://api.telegram.org/"),
        };

    private static async Task<string> WriteEventAsync(string directoryPath)
    {
        string eventPath = Path.Combine(directoryPath, "event.json");
        CopilotCliSessionEventInput input = new()
        {
            SessionId = "session-1",
            Timestamp = "2026-03-20T12:00:00.000Z",
            Cwd = directoryPath,
            EventId = "idle-1",
            EventType = "session_idle",
            DeliverAfter = "2026-03-20T12:00:00.000Z",
            Summary = "Completed the requested work.",
            SummarySource = "assistant.message",
        };
        await using FileStream stream = File.Create(eventPath);
        await JsonSerializer.SerializeAsync(
            stream,
            input,
            AppJsonSerializerContext.Default.CopilotCliSessionEventInput,
            CancellationToken.None);
        return eventPath;
    }

    private sealed class TestProcessRunner : IProcessRunner
    {
        public Task<ProcessExecutionResult> RunAsync(
            string fileName,
            IReadOnlyList<string> arguments,
            string? workingDirectory,
            string? standardInput,
            ProcessLogOptions? logOptions,
            CancellationToken cancellationToken)
        {
            if (fileName == "git")
            {
                return Task.FromResult(
                    new ProcessExecutionResult(1, string.Empty, "not a repository"));
            }

            if (fileName == "gopass"
                && arguments.Count == 2
                && arguments[0] == "show")
            {
                string value = arguments[1] == AppPaths.GetTelegramBotTokenSecretPath()
                    ? "123456:token"
                    : "chat-id";
                return Task.FromResult(
                    new ProcessExecutionResult(0, value + Environment.NewLine, string.Empty));
            }

            throw new InvalidOperationException(
                $"Unexpected process invocation: {fileName} {string.Join(' ', arguments)}");
        }
    }

    private sealed class NonInteractiveConsole : IInteractiveConsole
    {
        public bool CanPrompt => false;

        public bool Confirm(string prompt, bool defaultAnswer) => defaultAnswer;

        public string ReadSecret(string prompt) => string.Empty;

        public string ReadLine(string prompt) => string.Empty;
    }

    private sealed class TemporaryDirectory : IDisposable
    {
        public TemporaryDirectory()
        {
            Path = Directory.CreateTempSubdirectory().FullName;
        }

        public string Path { get; }

        public void Dispose()
        {
            Directory.Delete(Path, recursive: true);
        }
    }
}
