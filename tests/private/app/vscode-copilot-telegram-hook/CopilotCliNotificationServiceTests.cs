using System.Text;
using System.Text.Json;
using System.Net;
using Hcoona.VsCodeCopilotTelegramHook.Commands;
using Hcoona.VsCodeCopilotTelegramHook.Logging;
using Hcoona.VsCodeCopilotTelegramHook.Notifications;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Hcoona.VsCodeCopilotTelegramHook.Tests;

public sealed class CopilotCliNotificationServiceTests
{
    [Fact]
    public async Task HandleSessionEventAsyncSendsCompletionSummaryOnce()
    {
        DirectoryInfo workspace = Directory.CreateTempSubdirectory();

        try
        {
            RecordingHttpMessageHandler handler = new();
            CopilotCliNotificationService service = CreateService(handler);
            CopilotCliSessionEventInput input = CreateSessionEvent(
                workspace.FullName,
                eventType: "session_idle",
                summary: "Implemented the requested notification flow.",
                summarySource: "assistant.message");

            Assert.Equal(0, await HandleSessionEventAsync(service, input));
            Assert.Equal(0, await HandleSessionEventAsync(service, input));

            CapturedHttpRequest request = Assert.Single(handler.Requests);
            TelegramSendMessageRequest payload = DeserializePayload(request);
            Assert.Contains(
                "<b>✅ Copilot 已完成当前工作</b>",
                payload.Text,
                StringComparison.Ordinal);
            Assert.Contains(
                "摘要：Implemented the requested notification flow.",
                payload.Text,
                StringComparison.Ordinal);
            Assert.Contains(
                "状态：summary source: assistant.message",
                payload.Text,
                StringComparison.Ordinal);
        }
        finally
        {
            workspace.Delete(recursive: true);
        }
    }

    [Fact]
    public async Task HandleSessionEventAsyncRechecksSentMarkerAfterClaiming()
    {
        DirectoryInfo workspace = Directory.CreateTempSubdirectory();

        try
        {
            RecordingHttpMessageHandler handler = new();
            CopilotCliSessionEventInput input = CreateSessionEvent(
                workspace.FullName,
                eventType: "session_idle");
            string eventKey = $"{input.EventType}\n{input.EventId}";
            string markerPath = AppPaths.GetCopilotCliEventMarkerPath(
                workspace.FullName,
                input.SessionId,
                eventKey);
            string claimPath = AppPaths.GetCopilotCliEventClaimPath(
                workspace.FullName,
                input.SessionId,
                eventKey);
            CopilotCliNotificationService service = CreateService(
                handler,
                new CallbackTimeProvider(
                    () =>
                    {
                        Directory.CreateDirectory(Path.GetDirectoryName(markerPath)!);
                        File.WriteAllText(markerPath, "already delivered");
                    }));

            int exitCode = await HandleSessionEventAsync(service, input);

            Assert.Equal(0, exitCode);
            Assert.Empty(handler.Requests);
            Assert.True(File.Exists(markerPath));
            Assert.False(File.Exists(claimPath));
        }
        finally
        {
            workspace.Delete(recursive: true);
        }
    }

    [Fact]
    public async Task HandleNotificationAsyncSendsPermissionPromptContext()
    {
        DirectoryInfo workspace = Directory.CreateTempSubdirectory();

        try
        {
            RecordingHttpMessageHandler handler = new();
            CopilotCliNotificationService service = CreateService(handler);
            CopilotCliNotificationHookInput input = new()
            {
                SessionId = "session-permission",
                Timestamp = 1_773_400_496_789,
                Cwd = workspace.FullName,
                HookEventName = "notification",
                NotificationType = "permission_prompt",
                Title = "Permission required",
                Message = "Allow the command to access the network?",
            };

            int exitCode = await service.HandleNotificationAsync(
                SerializeToStream(
                    input,
                    AppJsonSerializerContext.Default.CopilotCliNotificationHookInput),
                CancellationToken.None);

            Assert.Equal(0, exitCode);
            TelegramSendMessageRequest payload = DeserializePayload(Assert.Single(handler.Requests));
            Assert.Contains(
                "<b>⚠️ Copilot 需要人工介入</b>",
                payload.Text,
                StringComparison.Ordinal);
            Assert.Contains("Permission required", payload.Text, StringComparison.Ordinal);
            Assert.Contains(
                "Allow the command to access the network?",
                payload.Text,
                StringComparison.Ordinal);
        }
        finally
        {
            workspace.Delete(recursive: true);
        }
    }

    [Fact]
    public async Task HandleNotificationAsyncUsesBoundedOperationTimeout()
    {
        DirectoryInfo workspace = Directory.CreateTempSubdirectory();

        try
        {
            CopilotCliNotificationService service = CreateService(
                new BlockingHttpMessageHandler(),
                sessionEventTimeout: TimeSpan.FromMilliseconds(20));
            CopilotCliNotificationHookInput input = new()
            {
                SessionId = "session-permission-timeout",
                Timestamp = 1_773_400_496_789,
                Cwd = workspace.FullName,
                HookEventName = "notification",
                NotificationType = "permission_prompt",
                Title = "Permission required",
                Message = "Allow the command to access the network?",
            };

            int exitCode = await service.HandleNotificationAsync(
                SerializeToStream(
                    input,
                    AppJsonSerializerContext.Default.CopilotCliNotificationHookInput),
                CancellationToken.None);

            Assert.Equal(1, exitCode);
        }
        finally
        {
            workspace.Delete(recursive: true);
        }
    }

    [Theory]
    [InlineData("permission_requested", "Permission required", "Allow network access?")]
    [InlineData("elicitation_requested", "example-mcp", "Choose a deployment environment.")]
    public async Task HandleSessionEventAsyncSendsRootAttentionContext(
        string eventType,
        string summary,
        string message)
    {
        DirectoryInfo workspace = Directory.CreateTempSubdirectory();

        try
        {
            RecordingHttpMessageHandler handler = new();
            CopilotCliNotificationService service = CreateService(handler);
            CopilotCliSessionEventInput input = CreateSessionEvent(
                workspace.FullName,
                eventType,
                summary: summary);
            input.Message = message;

            int exitCode = await HandleSessionEventAsync(service, input);

            Assert.Equal(0, exitCode);
            TelegramSendMessageRequest payload = DeserializePayload(Assert.Single(handler.Requests));
            Assert.Contains(
                "<b>⚠️ Copilot 需要人工介入</b>",
                payload.Text,
                StringComparison.Ordinal);
            Assert.Contains(summary, payload.Text, StringComparison.Ordinal);
            Assert.Contains(message, payload.Text, StringComparison.Ordinal);
        }
        finally
        {
            workspace.Delete(recursive: true);
        }
    }

    [Fact]
    public async Task HandleSessionEventAsyncIgnoresUnsupportedEventsAndSendsCompletionFallback()
    {
        DirectoryInfo workspace = Directory.CreateTempSubdirectory();

        try
        {
            RecordingHttpMessageHandler handler = new();
            CopilotCliNotificationService service = CreateService(handler);

            int unsupportedExitCode = await HandleSessionEventAsync(
                service,
                CreateSessionEvent(workspace.FullName, eventType: "assistant_idle"));
            int emptyCompletionExitCode = await HandleSessionEventAsync(
                service,
                CreateSessionEvent(
                    workspace.FullName,
                    eventType: "session_idle",
                    eventId: "event-without-summary"));

            Assert.Equal(0, unsupportedExitCode);
            Assert.Equal(0, emptyCompletionExitCode);
            TelegramSendMessageRequest payload = DeserializePayload(Assert.Single(handler.Requests));
            Assert.Contains("摘要：未捕获到最终回复。", payload.Text, StringComparison.Ordinal);
        }
        finally
        {
            workspace.Delete(recursive: true);
        }
    }

    [Fact]
    public async Task HandleSessionEventAsyncTruncatesLongCompletionSummary()
    {
        DirectoryInfo workspace = Directory.CreateTempSubdirectory();

        try
        {
            RecordingHttpMessageHandler handler = new();
            CopilotCliNotificationService service = CreateService(handler);
            string summary = new('x', 2000);

            int exitCode = await HandleSessionEventAsync(
                service,
                CreateSessionEvent(
                    workspace.FullName,
                    eventType: "session_idle",
                    summary: summary));

            Assert.Equal(0, exitCode);
            TelegramSendMessageRequest payload = DeserializePayload(Assert.Single(handler.Requests));
            Assert.Contains(new string('x', 1600) + "...", payload.Text, StringComparison.Ordinal);
            Assert.DoesNotContain(new string('x', 1601), payload.Text, StringComparison.Ordinal);
        }
        finally
        {
            workspace.Delete(recursive: true);
        }
    }

    [Fact]
    public async Task HandleSessionEventAsyncReclaimsStaleInFlightClaim()
    {
        DirectoryInfo workspace = Directory.CreateTempSubdirectory();

        try
        {
            RecordingHttpMessageHandler handler = new();
            CopilotCliNotificationService service = CreateService(handler);
            CopilotCliSessionEventInput input = CreateSessionEvent(
                workspace.FullName,
                eventType: "session_idle",
                summary: "Completed after a stale notifier claim.");
            string eventKey = $"{input.EventType}\n{input.EventId}";
            string claimPath = AppPaths.GetCopilotCliEventClaimPath(
                workspace.FullName,
                input.SessionId,
                eventKey);
            Directory.CreateDirectory(Path.GetDirectoryName(claimPath)!);
            File.WriteAllText(claimPath, "2020-01-01T00:00:00.000Z");

            int exitCode = await HandleSessionEventAsync(service, input);

            Assert.Equal(0, exitCode);
            Assert.Single(handler.Requests);
            Assert.False(File.Exists(claimPath));
            Assert.True(
                File.Exists(
                    AppPaths.GetCopilotCliEventMarkerPath(
                        workspace.FullName,
                        input.SessionId,
                        eventKey)));
        }
        finally
        {
            workspace.Delete(recursive: true);
        }
    }

    [Fact]
    public async Task HandleSessionEventAsyncReclaimsClaimOwnedByExitedProcess()
    {
        DirectoryInfo workspace = Directory.CreateTempSubdirectory();

        try
        {
            RecordingHttpMessageHandler handler = new();
            CopilotCliNotificationService service = CreateService(handler);
            CopilotCliSessionEventInput input = CreateSessionEvent(
                workspace.FullName,
                eventType: "session_idle",
                summary: "Completed after an abandoned notifier claim.");
            string eventKey = $"{input.EventType}\n{input.EventId}";
            string claimPath = AppPaths.GetCopilotCliEventClaimPath(
                workspace.FullName,
                input.SessionId,
                eventKey);
            Directory.CreateDirectory(Path.GetDirectoryName(claimPath)!);
            string claimedAt = DateTimeOffset.UtcNow.UtcDateTime.ToString(
                "yyyy-MM-ddTHH:mm:ss.fff'Z'",
                global::System.Globalization.CultureInfo.InvariantCulture);
            File.WriteAllText(claimPath, $"{claimedAt}\n{int.MaxValue}");

            int exitCode = await HandleSessionEventAsync(service, input);

            Assert.Equal(0, exitCode);
            Assert.Single(handler.Requests);
            Assert.False(File.Exists(claimPath));
        }
        finally
        {
            workspace.Delete(recursive: true);
        }
    }

    [Fact]
    public async Task HandleSessionEventAsyncReturnsRetryableResultForLiveClaim()
    {
        DirectoryInfo workspace = Directory.CreateTempSubdirectory();

        try
        {
            RecordingHttpMessageHandler handler = new();
            CopilotCliNotificationService service = CreateService(handler);
            CopilotCliSessionEventInput input = CreateSessionEvent(
                workspace.FullName,
                eventType: "session_idle");
            string eventKey = $"{input.EventType}\n{input.EventId}";
            string claimPath = AppPaths.GetCopilotCliEventClaimPath(
                workspace.FullName,
                input.SessionId,
                eventKey);
            Directory.CreateDirectory(Path.GetDirectoryName(claimPath)!);
            string claimedAt = DateTimeOffset.UtcNow.UtcDateTime.ToString(
                "yyyy-MM-ddTHH:mm:ss.fff'Z'",
                global::System.Globalization.CultureInfo.InvariantCulture);
            File.WriteAllText(claimPath, $"{claimedAt}\n{Environment.ProcessId}");

            int exitCode = await HandleSessionEventAsync(service, input);

            Assert.Equal(75, exitCode);
            Assert.Empty(handler.Requests);
            Assert.True(File.Exists(claimPath));
        }
        finally
        {
            workspace.Delete(recursive: true);
        }
    }

    [Fact]
    public async Task HandleSessionEventAsyncReleasesClaimWhenOperationTimesOut()
    {
        DirectoryInfo workspace = Directory.CreateTempSubdirectory();

        try
        {
            CopilotCliNotificationService service = CreateService(
                new BlockingHttpMessageHandler(),
                sessionEventTimeout: TimeSpan.FromMilliseconds(20));
            CopilotCliSessionEventInput input = CreateSessionEvent(
                workspace.FullName,
                eventType: "session_idle");
            string eventKey = $"{input.EventType}\n{input.EventId}";
            string claimPath = AppPaths.GetCopilotCliEventClaimPath(
                workspace.FullName,
                input.SessionId,
                eventKey);

            int exitCode = await HandleSessionEventAsync(service, input);

            Assert.Equal(1, exitCode);
            Assert.False(File.Exists(claimPath));
        }
        finally
        {
            workspace.Delete(recursive: true);
        }
    }

    [Fact]
    public async Task HandleSessionEventAsyncMarksPartialCancellationAsDelivered()
    {
        DirectoryInfo workspace = Directory.CreateTempSubdirectory();

        try
        {
            using CancellationTokenSource cancellation = new();
            CancelAfterFirstSendHandler handler = new(cancellation);
            CopilotCliNotificationService service = CreateService(handler);
            CopilotCliSessionEventInput input = CreateSessionEvent(
                workspace.FullName,
                eventType: "user_input_requested",
                summary: "Input required");
            input.Message = new string('x', 9000);
            string eventKey = $"{input.EventType}\n{input.EventId}";
            string markerPath = AppPaths.GetCopilotCliEventMarkerPath(
                workspace.FullName,
                input.SessionId,
                eventKey);

            int firstExitCode = await service.HandleSessionEventAsync(
                SerializeToStream(
                    input,
                    AppJsonSerializerContext.Default.CopilotCliSessionEventInput),
                cancellation.Token);
            int secondExitCode = await HandleSessionEventAsync(service, input);

            Assert.Equal(1, firstExitCode);
            Assert.Equal(0, secondExitCode);
            Assert.True(File.Exists(markerPath));
            Assert.Equal(2, handler.RequestCount);
        }
        finally
        {
            workspace.Delete(recursive: true);
        }
    }

    [Fact]
    public async Task HandleSessionEventAsyncReleasesClaimWhenDeliveryFails()
    {
        DirectoryInfo workspace = Directory.CreateTempSubdirectory();

        try
        {
            RecordingHttpMessageHandler handler = new(
            [
                RecordingHttpMessageHandler.CreateJsonResponse(
                    HttpStatusCode.BadRequest,
                    """{"ok":false,"description":"chat not found"}"""),
                RecordingHttpMessageHandler.CreateJsonResponse(
                    HttpStatusCode.OK,
                    """{"ok":true}"""),
            ]);
            CopilotCliNotificationService service = CreateService(handler);
            CopilotCliSessionEventInput input = CreateSessionEvent(
                workspace.FullName,
                eventType: "session_idle",
                summary: "Retryable completion.");

            Assert.Equal(1, await HandleSessionEventAsync(service, input));
            Assert.Equal(0, await HandleSessionEventAsync(service, input));

            Assert.Equal(2, handler.Requests.Count);
        }
        finally
        {
            workspace.Delete(recursive: true);
        }
    }

    private static CopilotCliSessionEventInput CreateSessionEvent(
        string workspacePath,
        string eventType,
        string? summary = null,
        string? summarySource = null,
        string eventId = "event-456")
    {
        return new CopilotCliSessionEventInput
        {
            SessionId = "session-123",
            Timestamp = "2026-03-13T12:34:56.789Z",
            Cwd = workspacePath,
            EventId = eventId,
            EventType = eventType,
            Summary = summary,
            SummarySource = summarySource,
        };
    }

    private static async Task<int> HandleSessionEventAsync(
        CopilotCliNotificationService service,
        CopilotCliSessionEventInput input)
    {
        return await service.HandleSessionEventAsync(
            SerializeToStream(input, AppJsonSerializerContext.Default.CopilotCliSessionEventInput),
            CancellationToken.None);
    }

    private static MemoryStream SerializeToStream<T>(
        T value,
        System.Text.Json.Serialization.Metadata.JsonTypeInfo<T> typeInfo)
        => new(JsonSerializer.SerializeToUtf8Bytes(value, typeInfo));

    private static CopilotCliNotificationService CreateService(
        HttpMessageHandler handler,
        TimeProvider? timeProvider = null,
        TimeSpan? sessionEventTimeout = null)
    {
        HttpClient httpClient = new(handler)
        {
            BaseAddress = new Uri("https://api.telegram.org/"),
        };
        NotificationProcessRunner processRunner = new();
        TelegramCredentialProvider credentialProvider = new(
            processRunner,
            new NonInteractiveConsole(),
            NullLogger<TelegramCredentialProvider>.Instance);

        return new CopilotCliNotificationService(
            new TelegramBotClient(httpClient, NullLogger<TelegramBotClient>.Instance),
            credentialProvider,
            new GitRepositoryProbe(processRunner, NullLogger<GitRepositoryProbe>.Instance),
            new SessionLogFileContext(),
            timeProvider ?? TimeProvider.System,
            NullLogger<CopilotCliNotificationService>.Instance)
        {
            SessionEventTimeout = sessionEventTimeout ?? TimeSpan.FromSeconds(25),
        };
    }

    private static TelegramSendMessageRequest DeserializePayload(CapturedHttpRequest request)
    {
        return JsonSerializer.Deserialize(
                request.Body,
                AppJsonSerializerContext.Default.TelegramSendMessageRequest)
            ?? throw new InvalidOperationException("Expected a valid Telegram request payload.");
    }

    private sealed class NotificationProcessRunner : IProcessRunner
    {
        public Task<ProcessExecutionResult> RunAsync(
            string fileName,
            IReadOnlyList<string> arguments,
            string? workingDirectory,
            string? standardInput,
            ProcessLogOptions? logOptions,
            CancellationToken cancellationToken)
        {
            if (string.Equals(fileName, "git", StringComparison.Ordinal))
            {
                return Task.FromResult(new ProcessExecutionResult(1, string.Empty, "not a repo"));
            }

            if (string.Equals(fileName, "gopass", StringComparison.Ordinal)
                && arguments.Count == 2
                && string.Equals(arguments[0], "show", StringComparison.Ordinal))
            {
                string value = string.Equals(
                    arguments[1],
                    AppPaths.GetTelegramBotTokenSecretPath(),
                    StringComparison.Ordinal)
                    ? "123456:test-token"
                    : "7713476101";
                return Task.FromResult(new ProcessExecutionResult(0, value, string.Empty));
            }

            throw new InvalidOperationException($"Unexpected process '{fileName}'.");
        }

    }

    private sealed class CallbackTimeProvider(Action callback) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow()
        {
            callback();
            return DateTimeOffset.Parse(
                "2026-03-13T12:34:56.789Z",
                global::System.Globalization.CultureInfo.InvariantCulture);
        }
    }

    private sealed class BlockingHttpMessageHandler : HttpMessageHandler
    {
        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            throw new InvalidOperationException("The blocking request unexpectedly completed.");
        }
    }

    private sealed class CancelAfterFirstSendHandler(
        CancellationTokenSource cancellation) : HttpMessageHandler
    {
        public int RequestCount { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            RequestCount++;
            if (RequestCount == 1)
            {
                cancellation.Cancel();
                return Task.FromResult(
                    RecordingHttpMessageHandler.CreateJsonResponse(
                        HttpStatusCode.OK,
                        """{"ok":true}"""));
            }

            cancellationToken.ThrowIfCancellationRequested();
            throw new InvalidOperationException("Expected caller cancellation.");
        }
    }

    private sealed class NonInteractiveConsole : IInteractiveConsole
    {
        public bool CanPrompt => false;

        public bool Confirm(string prompt, bool defaultAnswer)
            => throw new InvalidOperationException("Prompting is not expected.");

        public string ReadSecret(string prompt)
            => throw new InvalidOperationException("Prompting is not expected.");

        public string ReadLine(string prompt)
            => throw new InvalidOperationException("Prompting is not expected.");
    }
}
