using System.Text.Json;
using Hcoona.VsCodeCopilotTelegramHook.Commands;
using Hcoona.VsCodeCopilotTelegramHook.Logging;
using Hcoona.VsCodeCopilotTelegramHook.Notifications;
using Hcoona.VsCodeCopilotTelegramHook.State;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Hcoona.VsCodeCopilotTelegramHook.Tests;

public sealed class HookCommandServiceTests
{
    [Fact]
    public async Task HandleSessionStartAsyncWritesReminderAdditionalContextAndSessionState()
    {
        DirectoryInfo tempDirectory = Directory.CreateTempSubdirectory();

        try
        {
            WorkspaceStateStore stateStore = new(
                TimeProvider.System,
                NullLogger<WorkspaceStateStore>.Instance);
            HookCommandService service = CreateHookCommandService(
                new RecordingHttpMessageHandler(),
                stateStore: stateStore);
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

            HookResponse response = await DeserializeHookResponseAsync(output);
            Assert.Equal("SessionStart", response.HookSpecificOutput?.HookEventName);
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

            SessionState? sessionState = await stateStore.TryReadSessionAsync(
                tempDirectory.FullName,
                "session-123",
                CancellationToken.None);
            Assert.NotNull(sessionState);
            Assert.Equal("/tmp/transcript.json", sessionState!.TranscriptPath);
            FileAssertions.AssertOwnerOnlyFileMode(
                AppPaths.GetSessionStatePath(tempDirectory.FullName, "session-123"));
        }
        finally
        {
            tempDirectory.Delete(recursive: true);
        }
    }

    [Fact]
    public async Task HandleSessionStartAsyncWritesSessionStartContextLogEntry()
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

            HookCommandService service = CreateHookCommandService(
                new RecordingHttpMessageHandler(),
                loggerFactory: loggerFactory,
                logContext: logContext);
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

            string logPath = AppPaths.GetSessionLogPath(tempDirectory.FullName, "session-123");
            Assert.True(File.Exists(logPath));

            string logContent = await File.ReadAllTextAsync(logPath, CancellationToken.None);
            Assert.Contains("Handling SessionStart hook", logContent, StringComparison.Ordinal);
            Assert.Contains(
                "Wrote SessionStart additional context",
                logContent,
                StringComparison.Ordinal);
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
            WorkspaceStateStore stateStore = new(
                TimeProvider.System,
                NullLogger<WorkspaceStateStore>.Instance);
            HookCommandService service = CreateHookCommandService(
                new RecordingHttpMessageHandler(),
                stateStore: stateStore);
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

            TurnState? turnState = await stateStore.TryReadTurnAsync(
                tempDirectory.FullName,
                "session-123",
                CancellationToken.None);
            SummaryRecord? summaryRecord = await stateStore.TryReadSummaryAsync(
                tempDirectory.FullName,
                "session-123",
                CancellationToken.None);

            Assert.NotNull(turnState);
            Assert.NotNull(summaryRecord);
            Assert.Equal("session-123", turnState!.SessionId);
            Assert.Equal(turnState.TurnId, summaryRecord!.TurnId);
            FileAssertions.AssertOwnerOnlyFileMode(
                AppPaths.GetTurnStatePath(tempDirectory.FullName, "session-123"));
            FileAssertions.AssertOwnerOnlyFileMode(
                AppPaths.GetSummaryStatePath(tempDirectory.FullName, "session-123"));
        }
        finally
        {
            tempDirectory.Delete(recursive: true);
        }
    }

    [Fact]
    public async Task HandleUserPromptSubmitAsyncAcceptsSnakeCaseHookFields()
    {
        DirectoryInfo tempDirectory = Directory.CreateTempSubdirectory();

        try
        {
            WorkspaceStateStore stateStore = new(
                TimeProvider.System,
                NullLogger<WorkspaceStateStore>.Instance);
            HookCommandService service = CreateHookCommandService(
                new RecordingHttpMessageHandler(),
                stateStore: stateStore);
            using MemoryStream payload = CreateJsonStream(
                new Dictionary<string, object?>
                {
                    ["cwd"] = tempDirectory.FullName,
                    ["session_id"] = "session-123",
                    ["timestamp"] = "2026-03-14T15:51:50.783Z",
                    ["hook_event_name"] = "UserPromptSubmit",
                    ["transcript_path"] = "/tmp/transcript.json",
                    ["prompt"] = "Summarize the task.",
                });

            int exitCode = await service.HandleUserPromptSubmitAsync(
                payload,
                CancellationToken.None);

            Assert.Equal(0, exitCode);

            TurnState? turnState = await stateStore.TryReadTurnAsync(
                tempDirectory.FullName,
                "session-123",
                CancellationToken.None);
            Assert.NotNull(turnState);
            Assert.Equal("session-123", turnState!.SessionId);
        }
        finally
        {
            tempDirectory.Delete(recursive: true);
        }
    }

    [Fact]
    public async Task HandleUserPromptSubmitAsyncWritesSessionLogFile()
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

            HookCommandService service = CreateHookCommandService(
                new RecordingHttpMessageHandler(),
                loggerFactory: loggerFactory,
                logContext: logContext);
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

            string logPath = AppPaths.GetSessionLogPath(tempDirectory.FullName, "session-123");
            Assert.True(File.Exists(logPath));

            string logContent = await File.ReadAllTextAsync(logPath, CancellationToken.None);
            Assert.Contains("Handling UserPromptSubmit hook", logContent, StringComparison.Ordinal);
            Assert.Contains("session-123", logContent, StringComparison.Ordinal);
            Assert.DoesNotContain("| SessionId=", logContent, StringComparison.Ordinal);
            FileAssertions.AssertOwnerOnlyFileMode(logPath);
        }
        finally
        {
            tempDirectory.Delete(recursive: true);
        }
    }

    [Fact]
    public async Task HandleStopAsyncBlocksWhenSummaryFileIsInvalidForCurrentTurn()
    {
        DirectoryInfo tempDirectory = Directory.CreateTempSubdirectory();

        try
        {
            WorkspaceStateStore stateStore = new(
                TimeProvider.System,
                NullLogger<WorkspaceStateStore>.Instance);
            TurnState turnState = await stateStore.StartTurnAsync(
                new UserPromptSubmitHookInput
                {
                    Cwd = tempDirectory.FullName,
                    SessionId = "session-123",
                    TranscriptPath = "/tmp/transcript.json",
                    Prompt = "Summarize the latest changes.",
                },
                CancellationToken.None);

            await WriteSummaryAsync(
                tempDirectory.FullName,
                "session-123",
                new SummaryRecord
                {
                    SessionId = "session-123",
                    TurnId = "another-turn",
                    UpdatedAt = "2026-03-14T15:51:50.783Z",
                    Summary = "stale summary",
                },
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
            await using MemoryStream output = new();

            int exitCode = await service.HandleStopAsync(
                CreateJsonStream(stopInput, AppJsonSerializerContext.Default.StopHookInput),
                output,
                CancellationToken.None);

            Assert.Equal(0, exitCode);
            Assert.Empty(handler.Requests);

            HookResponse response = await DeserializeHookResponseAsync(output);
            Assert.Equal("Stop", response.HookSpecificOutput?.HookEventName);
            Assert.Equal("block", response.HookSpecificOutput?.Decision);
            Assert.Contains(
                turnState.TurnId,
                response.HookSpecificOutput?.Reason,
                StringComparison.Ordinal);
            Assert.Contains(
                AppPaths.GetRelativeSummaryStatePath("session-123"),
                response.HookSpecificOutput?.Reason,
                StringComparison.Ordinal);
            Assert.DoesNotContain(
                tempDirectory.FullName,
                response.HookSpecificOutput?.Reason,
                StringComparison.Ordinal);

            TurnState? updatedTurnState = await stateStore.TryReadTurnAsync(
                tempDirectory.FullName,
                "session-123",
                CancellationToken.None);
            Assert.NotNull(updatedTurnState);
            Assert.Equal(1, updatedTurnState!.StopValidationFailureCount);
            Assert.Contains(
                "turn_id must equal",
                updatedTurnState.LastStopValidationError,
                StringComparison.Ordinal);
        }
        finally
        {
            tempDirectory.Delete(recursive: true);
        }
    }

    [Fact]
    public async Task HandleStopAsyncBlocksWhenSummaryUpdatedAtIsNotUtcTimestamp()
    {
        DirectoryInfo tempDirectory = Directory.CreateTempSubdirectory();

        try
        {
            WorkspaceStateStore stateStore = new(
                TimeProvider.System,
                NullLogger<WorkspaceStateStore>.Instance);
            TurnState turnState = await stateStore.StartTurnAsync(
                new UserPromptSubmitHookInput
                {
                    Cwd = tempDirectory.FullName,
                    SessionId = "session-123",
                    TranscriptPath = "/tmp/transcript.json",
                    Prompt = "Summarize the latest changes.",
                },
                CancellationToken.None);

            await WriteSummaryAsync(
                tempDirectory.FullName,
                "session-123",
                new SummaryRecord
                {
                    SessionId = "session-123",
                    TurnId = turnState.TurnId,
                    UpdatedAt = "not-a-timestamp",
                    Summary = "fresh summary",
                },
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
            await using MemoryStream output = new();

            int exitCode = await service.HandleStopAsync(
                CreateJsonStream(stopInput, AppJsonSerializerContext.Default.StopHookInput),
                output,
                CancellationToken.None);

            Assert.Equal(0, exitCode);
            Assert.Empty(handler.Requests);

            HookResponse response = await DeserializeHookResponseAsync(output);
            Assert.Equal("Stop", response.HookSpecificOutput?.HookEventName);
            Assert.Equal("block", response.HookSpecificOutput?.Decision);
            Assert.Contains(
                "updated_at must be a UTC timestamp in yyyy-MM-ddTHH:mm:ss.fffZ format",
                response.HookSpecificOutput?.Reason,
                StringComparison.Ordinal);

            TurnState? updatedTurnState = await stateStore.TryReadTurnAsync(
                tempDirectory.FullName,
                "session-123",
                CancellationToken.None);
            Assert.NotNull(updatedTurnState);
            Assert.Equal(1, updatedTurnState!.StopValidationFailureCount);
            Assert.Contains(
                "updated_at must be a UTC timestamp in yyyy-MM-ddTHH:mm:ss.fffZ format",
                updatedTurnState.LastStopValidationError,
                StringComparison.Ordinal);
        }
        finally
        {
            tempDirectory.Delete(recursive: true);
        }
    }

    [Fact]
    public async Task HandleStopAsyncDoesNotCountDuplicateInvalidStopForSameAttempt()
    {
        DirectoryInfo tempDirectory = Directory.CreateTempSubdirectory();

        try
        {
            WorkspaceStateStore stateStore = new(
                TimeProvider.System,
                NullLogger<WorkspaceStateStore>.Instance);
            _ = await stateStore.StartTurnAsync(
                new UserPromptSubmitHookInput
                {
                    Cwd = tempDirectory.FullName,
                    SessionId = "session-123",
                    TranscriptPath = "/tmp/transcript.json",
                    Prompt = "Summarize the latest changes.",
                },
                CancellationToken.None);

            RecordingHttpMessageHandler handler = new();
            HookCommandService service = CreateHookCommandService(handler, stateStore);
            StopHookInput stopInput = new()
            {
                Cwd = tempDirectory.FullName,
                SessionId = "session-123",
                Timestamp = "2026-03-14T15:51:50.783Z",
                TranscriptPath = "/tmp/transcript.json",
                StopHookActive = false,
            };
            await using MemoryStream firstOutput = new();
            await using MemoryStream secondOutput = new();

            _ = await service.HandleStopAsync(
                CreateJsonStream(stopInput, AppJsonSerializerContext.Default.StopHookInput),
                firstOutput,
                CancellationToken.None);
            _ = await service.HandleStopAsync(
                CreateJsonStream(stopInput, AppJsonSerializerContext.Default.StopHookInput),
                secondOutput,
                CancellationToken.None);

            Assert.Equal(
                "block",
                (await DeserializeHookResponseAsync(firstOutput)).HookSpecificOutput?.Decision);
            Assert.Equal(
                "block",
                (await DeserializeHookResponseAsync(secondOutput)).HookSpecificOutput?.Decision);

            TurnState? updatedTurnState = await stateStore.TryReadTurnAsync(
                tempDirectory.FullName,
                "session-123",
                CancellationToken.None);
            Assert.NotNull(updatedTurnState);
            Assert.Equal(1, updatedTurnState!.StopValidationFailureCount);
            Assert.Equal(stopInput.Timestamp, updatedTurnState.LastStopValidationFailureTimestamp);
            Assert.Empty(handler.Requests);
        }
        finally
        {
            tempDirectory.Delete(recursive: true);
        }
    }

    [Fact]
    public async Task HandleStopAsyncAllowsAfterThreeValidationFailuresWithoutStopHookActive()
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

            WorkspaceStateStore stateStore = new(
                TimeProvider.System,
                NullLogger<WorkspaceStateStore>.Instance);
            _ = await stateStore.StartTurnAsync(
                new UserPromptSubmitHookInput
                {
                    Cwd = tempDirectory.FullName,
                    SessionId = "session-123",
                    TranscriptPath = "/tmp/transcript.json",
                    Prompt = "Ship the change.",
                },
                CancellationToken.None);

            RecordingHttpMessageHandler handler = new();
            HookCommandService service = CreateHookCommandService(handler, stateStore);

            StopHookInput firstStop = new()
            {
                Cwd = tempDirectory.FullName,
                SessionId = "session-123",
                Timestamp = "2026-03-14T15:51:50.783Z",
                TranscriptPath = "/tmp/transcript.json",
                StopHookActive = false,
            };
            StopHookInput continuedStop = new()
            {
                Cwd = tempDirectory.FullName,
                SessionId = "session-123",
                Timestamp = "2026-03-14T15:52:10.783Z",
                TranscriptPath = "/tmp/transcript.json",
                StopHookActive = false,
            };
            StopHookInput finalContinuedStop = new()
            {
                Cwd = tempDirectory.FullName,
                SessionId = "session-123",
                Timestamp = "2026-03-14T15:52:30.783Z",
                TranscriptPath = "/tmp/transcript.json",
                StopHookActive = false,
            };

            await using MemoryStream firstOutput = new();
            await using MemoryStream secondOutput = new();
            await using MemoryStream thirdOutput = new();

            _ = await service.HandleStopAsync(
                CreateJsonStream(firstStop, AppJsonSerializerContext.Default.StopHookInput),
                firstOutput,
                CancellationToken.None);
            _ = await service.HandleStopAsync(
                CreateJsonStream(continuedStop, AppJsonSerializerContext.Default.StopHookInput),
                secondOutput,
                CancellationToken.None);
            _ = await service.HandleStopAsync(
                CreateJsonStream(
                    finalContinuedStop,
                    AppJsonSerializerContext.Default.StopHookInput),
                thirdOutput,
                CancellationToken.None);

            Assert.Equal(
                "block",
                (await DeserializeHookResponseAsync(firstOutput))
                    .HookSpecificOutput?.Decision);
            Assert.Equal(
                "block",
                (await DeserializeHookResponseAsync(secondOutput))
                    .HookSpecificOutput?.Decision);
            Assert.Equal(0, thirdOutput.Length);

            TelegramSendMessageRequest payload = DeserializeTelegramPayload(
                Assert.Single(handler.Requests));
            Assert.Contains("摘要：当前轮未生成摘要。", payload.Text, StringComparison.Ordinal);

            TurnState? turnState = await stateStore.TryReadTurnAsync(
                tempDirectory.FullName,
                "session-123",
                CancellationToken.None);
            Assert.NotNull(turnState);
            Assert.Equal(
                AppConstants.MaxStopSummaryValidationFailures,
                turnState!.StopValidationFailureCount);
            Assert.Equal(
                finalContinuedStop.Timestamp,
                turnState.LastStopValidationFailureTimestamp);
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
            WorkspaceStateStore stateStore = new(
                TimeProvider.System,
                NullLogger<WorkspaceStateStore>.Instance);
            HookCommandService service = CreateHookCommandService(handler, stateStore: stateStore);
            StopHookInput stopInput = new()
            {
                Cwd = tempDirectory.FullName,
                SessionId = "session-123",
                Timestamp = "2026-03-14T15:51:50.783Z",
                TranscriptPath = "/tmp/transcript.json",
            };
            await using MemoryStream output = new();

            int exitCode = await service.HandleStopAsync(
                CreateJsonStream(stopInput, AppJsonSerializerContext.Default.StopHookInput),
                output,
                CancellationToken.None);

            Assert.Equal(0, exitCode);
            Assert.Equal(0, output.Length);

            TelegramSendMessageRequest payload = DeserializeTelegramPayload(
                Assert.Single(handler.Requests));
            Assert.Contains("摘要：当前轮未生成摘要。", payload.Text, StringComparison.Ordinal);
            Assert.Contains(
                "<b>轮次 ID：</b><code>stop-20260314t155150783z</code>",
                payload.Text,
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
            tempDirectory.Delete(recursive: true);
        }
    }

    [Fact]
    public async Task HandleStopAsyncSuppressesDuplicateStopWhenTurnStateIsMissing()
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
            WorkspaceStateStore stateStore = new(
                TimeProvider.System,
                NullLogger<WorkspaceStateStore>.Instance);
            HookCommandService service = CreateHookCommandService(handler, stateStore: stateStore);
            StopHookInput stopInput = new()
            {
                Cwd = tempDirectory.FullName,
                SessionId = "session-123",
                Timestamp = "2026-03-14T15:51:50.783Z",
                TranscriptPath = "/tmp/transcript.json",
            };

            await using MemoryStream firstOutput = new();
            await using MemoryStream secondOutput = new();

            _ = await service.HandleStopAsync(
                CreateJsonStream(stopInput, AppJsonSerializerContext.Default.StopHookInput),
                firstOutput,
                CancellationToken.None);
            _ = await service.HandleStopAsync(
                CreateJsonStream(stopInput, AppJsonSerializerContext.Default.StopHookInput),
                secondOutput,
                CancellationToken.None);

            Assert.Equal(0, firstOutput.Length);
            Assert.Equal(0, secondOutput.Length);
            Assert.Single(handler.Requests);
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
    public async Task HandleStopAsyncSendsValidatedSummaryAndSuppressesDuplicateStop()
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

            WorkspaceStateStore stateStore = new(
                TimeProvider.System,
                NullLogger<WorkspaceStateStore>.Instance);
            TurnState turnState = await stateStore.StartTurnAsync(
                new UserPromptSubmitHookInput
                {
                    Cwd = tempDirectory.FullName,
                    SessionId = "session-123",
                    TranscriptPath = "/tmp/transcript.json",
                    Prompt = "Ship the change.",
                },
                CancellationToken.None);

            await WriteSummaryAsync(
                tempDirectory.FullName,
                "session-123",
                new SummaryRecord
                {
                    SessionId = "session-123",
                    TurnId = turnState.TurnId,
                    UpdatedAt = "2026-03-14T15:51:50.783Z",
                    Summary = "本轮工作已完成。",
                },
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

            await using MemoryStream firstOutput = new();
            await using MemoryStream secondOutput = new();

            _ = await service.HandleStopAsync(
                CreateJsonStream(stopInput, AppJsonSerializerContext.Default.StopHookInput),
                firstOutput,
                CancellationToken.None);
            _ = await service.HandleStopAsync(
                CreateJsonStream(stopInput, AppJsonSerializerContext.Default.StopHookInput),
                secondOutput,
                CancellationToken.None);

            Assert.Equal(0, firstOutput.Length);
            Assert.Equal(0, secondOutput.Length);

            TelegramSendMessageRequest payload = DeserializeTelegramPayload(
                Assert.Single(handler.Requests));
            Assert.Contains("摘要：本轮工作已完成。", payload.Text, StringComparison.Ordinal);
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
    public async Task HandleStopAsyncWithoutSessionIdWritesWorkspaceFallbackLog()
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

            RecordingHttpMessageHandler handler = new();
            HookCommandService service = CreateHookCommandService(
                handler,
                loggerFactory: loggerFactory,
                logContext: logContext);
            StopHookInput stopInput = new()
            {
                Cwd = tempDirectory.FullName,
                SessionId = string.Empty,
                Timestamp = "2026-03-14T15:51:50.783Z",
            };
            await using MemoryStream output = new();

            int exitCode = await service.HandleStopAsync(
                CreateJsonStream(stopInput, AppJsonSerializerContext.Default.StopHookInput),
                output,
                CancellationToken.None);

            Assert.Equal(0, exitCode);
            Assert.Empty(handler.Requests);
            Assert.Equal(0, output.Length);

            string workspaceLogPath = AppPaths.GetWorkspaceLogPath(tempDirectory.FullName);
            Assert.True(File.Exists(workspaceLogPath));

            string logContent = await File.ReadAllTextAsync(
                workspaceLogPath,
                CancellationToken.None);
            Assert.Contains(
                "Ignoring invalid Stop hook input",
                logContent,
                StringComparison.Ordinal);
            Assert.Contains("session_id", logContent, StringComparison.Ordinal);
        }
        finally
        {
            tempDirectory.Delete(recursive: true);
        }
    }

    [Fact]
    public async Task HandleStopAsyncWithoutSessionIdLogsObservedTopLevelFields()
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

            RecordingHttpMessageHandler handler = new();
            HookCommandService service = CreateHookCommandService(
                handler,
                loggerFactory: loggerFactory,
                logContext: logContext);
            using MemoryStream payload = CreateJsonStream(
                new Dictionary<string, object?>
                {
                    ["cwd"] = tempDirectory.FullName,
                    ["sessionId"] = "session-123",
                    ["hookEventName"] = "Stop",
                    ["timestamp"] = "2026-03-14T15:51:50.783Z",
                });
            await using MemoryStream output = new();

            int exitCode = await service.HandleStopAsync(
                payload,
                output,
                CancellationToken.None);

            Assert.Equal(0, exitCode);
            Assert.Empty(handler.Requests);
            Assert.Equal(0, output.Length);

            string workspaceLogPath = AppPaths.GetWorkspaceLogPath(tempDirectory.FullName);
            string logContent = await File.ReadAllTextAsync(
                workspaceLogPath,
                CancellationToken.None);
            Assert.Contains(
                "missing required field(s): session_id.",
                logContent,
                StringComparison.Ordinal);
            Assert.Contains(
                "present top-level field(s): cwd, hookEventName, sessionId, timestamp.",
                logContent,
                StringComparison.Ordinal);
        }
        finally
        {
            tempDirectory.Delete(recursive: true);
        }
    }

    private static HookCommandService CreateHookCommandService(
        RecordingHttpMessageHandler handler,
        WorkspaceStateStore? stateStore = null,
        ILoggerFactory? loggerFactory = null,
        SessionLogFileContext? logContext = null)
    {
        HttpClient httpClient = new(handler)
        {
            BaseAddress = new Uri("https://api.telegram.org/"),
        };

        SessionLogFileContext context = logContext ?? new SessionLogFileContext();
        IProcessRunner processRunner = new ProcessRunner(
            CreateLogger<ProcessRunner>(loggerFactory));
        TelegramCredentialProvider credentialProvider = new(
            processRunner,
            new SystemInteractiveConsole(),
            CreateLogger<TelegramCredentialProvider>(loggerFactory));
        GitRepositoryProbe gitRepositoryProbe = new(
            processRunner,
            CreateLogger<GitRepositoryProbe>(loggerFactory));

        return new HookCommandService(
            stateStore ?? new WorkspaceStateStore(
                TimeProvider.System,
                CreateLogger<WorkspaceStateStore>(loggerFactory)),
            new TelegramBotClient(httpClient, CreateLogger<TelegramBotClient>(loggerFactory)),
            credentialProvider,
            gitRepositoryProbe,
            context,
            CreateLogger<HookCommandService>(loggerFactory));
    }

    private static MemoryStream CreateJsonStream<T>(
        T value,
        System.Text.Json.Serialization.Metadata.JsonTypeInfo<T> jsonTypeInfo)
    {
        return new MemoryStream(JsonSerializer.SerializeToUtf8Bytes(value, jsonTypeInfo));
    }

    private static MemoryStream CreateJsonStream(
        IReadOnlyDictionary<string, object?> properties)
    {
        return new MemoryStream(JsonSerializer.SerializeToUtf8Bytes(properties));
    }

    private static TelegramSendMessageRequest DeserializeTelegramPayload(
        CapturedHttpRequest request)
    {
        return JsonSerializer.Deserialize(
                request.Body,
                AppJsonSerializerContext.Default.TelegramSendMessageRequest)
            ?? throw new InvalidOperationException("Expected a valid Telegram request payload.");
    }

    private static async Task<HookResponse> DeserializeHookResponseAsync(MemoryStream output)
    {
        output.Position = 0;
        return await JsonSerializer.DeserializeAsync(
                output,
                AppJsonSerializerContext.Default.HookResponse,
                CancellationToken.None)
            ?? throw new InvalidOperationException("Expected a valid hook response.");
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

    private static ILogger<T> CreateLogger<T>(ILoggerFactory? loggerFactory)
        => loggerFactory?.CreateLogger<T>() ?? NullLogger<T>.Instance;
}
