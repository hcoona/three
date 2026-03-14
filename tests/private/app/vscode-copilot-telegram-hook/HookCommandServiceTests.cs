using System.Text.Json;
using Hcoona.VsCodeCopilotTelegramHook.Commands;
using Hcoona.VsCodeCopilotTelegramHook.Notifications;
using Hcoona.VsCodeCopilotTelegramHook.State;
using Xunit;

namespace Hcoona.VsCodeCopilotTelegramHook.Tests;

public sealed class HookCommandServiceTests
{
    [Fact]
    public async Task HandleSessionStartAsyncWritesAdditionalContextAndSessionState()
    {
        DirectoryInfo tempDirectory = Directory.CreateTempSubdirectory();

        try
        {
            HookCommandService service = CreateHookCommandService(
                new RecordingHttpMessageHandler());
            SessionStartHookInput sessionStartInput = new()
            {
                Cwd = tempDirectory.FullName,
                SessionId = "session-123",
                Timestamp = "2026-03-14T15:51:50.783Z",
                TranscriptPath = "/tmp/transcript.json",
                Source = "new",
            };
            await using MemoryStream output = new();

            int exitCode = await service.HandleSessionStartAsync(
                CreateJsonStream(
                    sessionStartInput,
                    AppJsonSerializerContext.Default.SessionStartHookInput),
                output,
                CancellationToken.None);

            Assert.Equal(0, exitCode);

            output.Position = 0;
            HookResponse response = await JsonSerializer.DeserializeAsync(
                    output,
                    AppJsonSerializerContext.Default.HookResponse,
                    CancellationToken.None)
                ?? throw new InvalidOperationException("Expected a valid hook response.");

            string additionalContext = Assert.IsType<string>(
                response.HookSpecificOutput?.AdditionalContext);
            Assert.Contains("Notification summary handoff is enabled", additionalContext);
            Assert.Contains(
                AppPaths.GetRelativeTurnStatePath("session-123"),
                additionalContext,
                StringComparison.Ordinal);
            Assert.Contains(
                AppPaths.GetRelativeSummaryStatePath("session-123"),
                additionalContext,
                StringComparison.Ordinal);

            SessionState? sessionState = await WorkspaceStateStore.TryReadSessionAsync(
                tempDirectory.FullName,
                "session-123",
                CancellationToken.None);
            Assert.NotNull(sessionState);
            Assert.Equal("/tmp/transcript.json", sessionState!.TranscriptPath);
        }
        finally
        {
            tempDirectory.Delete(recursive: true);
        }
    }

    [Fact]
    public async Task HandleUserPromptSubmitAsyncCreatesTurnAndSummaryState()
    {
        DirectoryInfo tempDirectory = Directory.CreateTempSubdirectory();

        try
        {
            HookCommandService service = CreateHookCommandService(
                new RecordingHttpMessageHandler());
            UserPromptSubmitHookInput promptInput = new()
            {
                Cwd = tempDirectory.FullName,
                SessionId = "session-123",
                Timestamp = "2026-03-14T15:51:50.783Z",
                TranscriptPath = "/tmp/transcript.json",
                Prompt = "Summarize the task.",
            };

            int exitCode = await service.HandleUserPromptSubmitAsync(
                CreateJsonStream(
                    promptInput,
                    AppJsonSerializerContext.Default.UserPromptSubmitHookInput),
                CancellationToken.None);

            Assert.Equal(0, exitCode);

            TurnState? turnState = await WorkspaceStateStore.TryReadTurnAsync(
                tempDirectory.FullName,
                "session-123",
                CancellationToken.None);
            SummaryRecord? summaryRecord = await WorkspaceStateStore.TryReadSummaryAsync(
                tempDirectory.FullName,
                "session-123",
                CancellationToken.None);

            Assert.NotNull(turnState);
            Assert.NotNull(summaryRecord);
            Assert.Equal("session-123", turnState!.SessionId);
            Assert.Equal(turnState.TurnId, summaryRecord!.TurnId);
        }
        finally
        {
            tempDirectory.Delete(recursive: true);
        }
    }

    [Fact]
    public async Task HandleStopAsyncSendsFallbackSummaryWhenTurnStateIsMissing()
    {
        DirectoryInfo tempDirectory = Directory.CreateTempSubdirectory();
        string? originalBotToken = Environment.GetEnvironmentVariable(
            AppConstants.TelegramBotTokenEnvironmentVariable);
        string? originalChatId = Environment.GetEnvironmentVariable(
            AppConstants.TelegramChatIdEnvironmentVariable);

        try
        {
            Environment.SetEnvironmentVariable(
                AppConstants.TelegramBotTokenEnvironmentVariable,
                "123456:ABCdef_token");
            Environment.SetEnvironmentVariable(
                AppConstants.TelegramChatIdEnvironmentVariable,
                "7713476101");

            RecordingHttpMessageHandler handler = new();
            HookCommandService service = CreateHookCommandService(handler);
            StopHookInput stopInput = new()
            {
                Cwd = tempDirectory.FullName,
                SessionId = "session-123",
                Timestamp = "2026-03-14T15:51:50.783Z",
                TranscriptPath = "/tmp/transcript.json",
            };

            int exitCode = await service.HandleStopAsync(
                CreateJsonStream(stopInput, AppJsonSerializerContext.Default.StopHookInput),
                CancellationToken.None);

            Assert.Equal(0, exitCode);

            TelegramSendMessageRequest payload = DeserializeTelegramPayload(
                Assert.Single(handler.Requests));
            Assert.Contains("摘要：当前轮未生成摘要。", payload.Text, StringComparison.Ordinal);
            Assert.Contains(
                "<b>轮次 ID：</b><code>stop-20260314t155150783z</code>",
                payload.Text,
                StringComparison.Ordinal);

            LastSentState? lastSentState = await WorkspaceStateStore.TryReadLastSentAsync(
                tempDirectory.FullName,
                "session-123",
                CancellationToken.None);
            Assert.NotNull(lastSentState);
            Assert.Equal("stop-20260314t155150783z", lastSentState!.TurnId);
        }
        finally
        {
            Environment.SetEnvironmentVariable(
                AppConstants.TelegramBotTokenEnvironmentVariable,
                originalBotToken);
            Environment.SetEnvironmentVariable(
                AppConstants.TelegramChatIdEnvironmentVariable,
                originalChatId);
            tempDirectory.Delete(recursive: true);
        }
    }

    [Fact]
    public async Task HandleStopAsyncIgnoresSummaryForDifferentTurn()
    {
        DirectoryInfo tempDirectory = Directory.CreateTempSubdirectory();
        string? originalBotToken = Environment.GetEnvironmentVariable(
            AppConstants.TelegramBotTokenEnvironmentVariable);
        string? originalChatId = Environment.GetEnvironmentVariable(
            AppConstants.TelegramChatIdEnvironmentVariable);

        try
        {
            Environment.SetEnvironmentVariable(
                AppConstants.TelegramBotTokenEnvironmentVariable,
                "123456:ABCdef_token");
            Environment.SetEnvironmentVariable(
                AppConstants.TelegramChatIdEnvironmentVariable,
                "7713476101");

            WorkspaceStateStore stateStore = new(TimeProvider.System);
            TurnState turnState = await stateStore.StartTurnAsync(
                new UserPromptSubmitHookInput
                {
                    Cwd = tempDirectory.FullName,
                    SessionId = "session-123",
                    TranscriptPath = "/tmp/transcript.json",
                    Prompt = "Summarize the latest changes.",
                },
                CancellationToken.None);

            SummaryRecord staleSummary = new()
            {
                SessionId = "session-123",
                TurnId = "another-turn",
                UpdatedAt = "2026-03-14T15:51:50.783Z",
                Summary = "这是一条不应该被发送的旧摘要。",
            };
            await WriteSummaryAsync(
                tempDirectory.FullName,
                "session-123",
                staleSummary,
                CancellationToken.None);

            RecordingHttpMessageHandler handler = new();
            HookCommandService service = CreateHookCommandService(handler, stateStore);
            StopHookInput stopInput = new()
            {
                Cwd = tempDirectory.FullName,
                SessionId = "session-123",
                Timestamp = "2026-03-14T15:51:50.783Z",
                TranscriptPath = "/tmp/transcript.json",
            };

            _ = await service.HandleStopAsync(
                CreateJsonStream(stopInput, AppJsonSerializerContext.Default.StopHookInput),
                CancellationToken.None);

            TelegramSendMessageRequest payload = DeserializeTelegramPayload(
                Assert.Single(handler.Requests));
            Assert.Contains("摘要：当前轮未生成摘要。", payload.Text, StringComparison.Ordinal);
            Assert.DoesNotContain("旧摘要", payload.Text, StringComparison.Ordinal);
            Assert.Contains(turnState.TurnId, payload.Text, StringComparison.Ordinal);
        }
        finally
        {
            Environment.SetEnvironmentVariable(
                AppConstants.TelegramBotTokenEnvironmentVariable,
                originalBotToken);
            Environment.SetEnvironmentVariable(
                AppConstants.TelegramChatIdEnvironmentVariable,
                originalChatId);
            tempDirectory.Delete(recursive: true);
        }
    }

    [Fact]
    public async Task HandleStopAsyncSuppressesDuplicateStopForSameTurnAndTimestamp()
    {
        DirectoryInfo tempDirectory = Directory.CreateTempSubdirectory();
        string? originalBotToken = Environment.GetEnvironmentVariable(
            AppConstants.TelegramBotTokenEnvironmentVariable);
        string? originalChatId = Environment.GetEnvironmentVariable(
            AppConstants.TelegramChatIdEnvironmentVariable);

        try
        {
            Environment.SetEnvironmentVariable(
                AppConstants.TelegramBotTokenEnvironmentVariable,
                "123456:ABCdef_token");
            Environment.SetEnvironmentVariable(
                AppConstants.TelegramChatIdEnvironmentVariable,
                "7713476101");

            WorkspaceStateStore stateStore = new(TimeProvider.System);
            TurnState turnState = await stateStore.StartTurnAsync(
                new UserPromptSubmitHookInput
                {
                    Cwd = tempDirectory.FullName,
                    SessionId = "session-123",
                    TranscriptPath = "/tmp/transcript.json",
                    Prompt = "Ship the change.",
                },
                CancellationToken.None);

            SummaryRecord summary = new()
            {
                SessionId = "session-123",
                TurnId = turnState.TurnId,
                UpdatedAt = "2026-03-14T15:51:50.783Z",
                Summary = "本轮工作已完成。",
            };
            await WriteSummaryAsync(
                tempDirectory.FullName,
                "session-123",
                summary,
                CancellationToken.None);

            RecordingHttpMessageHandler handler = new();
            HookCommandService service = CreateHookCommandService(handler, stateStore);
            StopHookInput stopInput = new()
            {
                Cwd = tempDirectory.FullName,
                SessionId = "session-123",
                Timestamp = "2026-03-14T15:51:50.783Z",
                TranscriptPath = "/tmp/transcript.json",
            };

            _ = await service.HandleStopAsync(
                CreateJsonStream(stopInput, AppJsonSerializerContext.Default.StopHookInput),
                CancellationToken.None);
            _ = await service.HandleStopAsync(
                CreateJsonStream(stopInput, AppJsonSerializerContext.Default.StopHookInput),
                CancellationToken.None);

            TelegramSendMessageRequest payload = DeserializeTelegramPayload(
                Assert.Single(handler.Requests));
            Assert.Contains("摘要：本轮工作已完成。", payload.Text, StringComparison.Ordinal);

            LastSentState? lastSentState = await WorkspaceStateStore.TryReadLastSentAsync(
                tempDirectory.FullName,
                "session-123",
                CancellationToken.None);
            Assert.NotNull(lastSentState);
            Assert.Equal(turnState.TurnId, lastSentState!.TurnId);
            Assert.Equal(stopInput.Timestamp, lastSentState.StopTimestamp);
        }
        finally
        {
            Environment.SetEnvironmentVariable(
                AppConstants.TelegramBotTokenEnvironmentVariable,
                originalBotToken);
            Environment.SetEnvironmentVariable(
                AppConstants.TelegramChatIdEnvironmentVariable,
                originalChatId);
            tempDirectory.Delete(recursive: true);
        }
    }

    private static HookCommandService CreateHookCommandService(
        RecordingHttpMessageHandler handler,
        WorkspaceStateStore? stateStore = null)
    {
        HttpClient httpClient = new(handler)
        {
            BaseAddress = new Uri("https://api.telegram.org/"),
        };

        return new HookCommandService(
            stateStore ?? new WorkspaceStateStore(TimeProvider.System),
            new TelegramBotClient(httpClient));
    }

    private static MemoryStream CreateJsonStream<T>(
        T value,
        System.Text.Json.Serialization.Metadata.JsonTypeInfo<T> jsonTypeInfo)
    {
        return new MemoryStream(JsonSerializer.SerializeToUtf8Bytes(value, jsonTypeInfo));
    }

    private static TelegramSendMessageRequest DeserializeTelegramPayload(
        CapturedHttpRequest request)
    {
        return JsonSerializer.Deserialize(
                request.Body,
                AppJsonSerializerContext.Default.TelegramSendMessageRequest)
            ?? throw new InvalidOperationException("Expected a valid Telegram request payload.");
    }

    private static async Task WriteSummaryAsync(
        string workspacePath,
        string sessionId,
        SummaryRecord summary,
        CancellationToken cancellationToken)
    {
        string summaryPath = AppPaths.GetSummaryStatePath(workspacePath, sessionId);
        Directory.CreateDirectory(Path.GetDirectoryName(summaryPath)!);
        await using FileStream stream = File.Create(summaryPath);
        await JsonSerializer.SerializeAsync(
            stream,
            summary,
            AppJsonSerializerContext.Default.SummaryRecord,
            cancellationToken);
    }
}
