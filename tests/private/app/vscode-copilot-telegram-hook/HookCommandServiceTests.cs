using System.Text.Json;
using System.Net;
using System.Security.Cryptography;
using System.Text;
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
    public async Task HandleSessionStartAsyncWritesProtocolOverviewWithoutLegacySingletonPaths()
    {
        DirectoryInfo tempDirectory = Directory.CreateTempSubdirectory();

        try
        {
            WorkspaceStateStore stateStore = new(
                FixedUtcNow(),
                NullLogger<WorkspaceStateStore>.Instance);
            HookCommandService service = CreateHookCommandService(
                new RecordingHttpMessageHandler(),
                stateStore: stateStore);
            SessionStartHookInput sessionStartInput = new()
            {
                Cwd = tempDirectory.FullName,
                SessionId = "session-123",
                Timestamp = "2026-03-14T15:51:50.783Z",
                TranscriptPath = "/workspace/transcript.json",
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
            string additionalContext = Assert.IsType<string>(
                response.HookSpecificOutput?.AdditionalContext);
            Assert.Contains("Notification Assignment", additionalContext, StringComparison.Ordinal);
            Assert.Contains(
                "only that exact assigned summary path",
                additionalContext,
                StringComparison.Ordinal);
            Assert.DoesNotContain("notify-turn.json", additionalContext, StringComparison.Ordinal);
            Assert.DoesNotContain(
                "notify-summary.json",
                additionalContext,
                StringComparison.Ordinal);
            Assert.Contains(
                "Recovery guidance is not a new task",
                additionalContext,
                StringComparison.Ordinal);

            NotificationSession? session = await stateStore.TryReadSessionAsync(
                tempDirectory.FullName,
                "session-123",
                CancellationToken.None);
            Assert.NotNull(session);
            Assert.Equal("/workspace/transcript.json", session!.TranscriptPath);
            FileAssertions.AssertOwnerOnlyFileMode(
                AppPaths.GetSessionStatePath(tempDirectory.FullName, "session-123"));
        }
        finally
        {
            tempDirectory.Delete(recursive: true);
        }
    }

    [Fact]
    public async Task HandleUserPromptSubmitAsyncRecordsObservationOnlyForUncertainGeneratedPrompt()
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
                TranscriptPath = "/workspace/transcript.json",
                Prompt = "You are the Coder subagent for Group 1 formal implementation.",
            };
            await using MemoryStream output = new();

            int exitCode = await service.HandleUserPromptSubmitAsync(
                CreateJsonStream(
                    promptInput,
                    AppJsonSerializerContext.Default.UserPromptSubmitHookInput),
                output,
                CancellationToken.None);

            Assert.Equal(0, exitCode);
            Assert.Equal(0, output.Length);
            Assert.Empty(await stateStore.ListOpenTurnsAsync(
                tempDirectory.FullName,
                "session-123",
                CancellationToken.None));
            string promptsDirectory = Path.Combine(
                AppPaths.GetSessionDirectoryPath(tempDirectory.FullName, "session-123"),
                AppConstants.PromptsDirectoryName);
            Assert.Single(Directory.EnumerateFiles(promptsDirectory, "*.json"));
        }
        finally
        {
            tempDirectory.Delete(recursive: true);
        }
    }

    [Theory]
    [InlineData("<system_reminder>\nNotification assignment reminder.\n</system_reminder>")]
    [InlineData("Contents of AGENTS.md:\n# Instructions for Current Repository")]
    public async Task HandleUserPromptSubmitAsyncRecordsObservationOnlyForSystemAndAgentsReminders(
        string prompt)
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
                TranscriptPath = "/workspace/transcript.json",
                Prompt = prompt,
            };
            await using MemoryStream output = new();

            int exitCode = await service.HandleUserPromptSubmitAsync(
                CreateJsonStream(
                    promptInput,
                    AppJsonSerializerContext.Default.UserPromptSubmitHookInput),
                output,
                CancellationToken.None);

            Assert.Equal(0, exitCode);
            Assert.Equal(0, output.Length);
            Assert.Empty(await stateStore.ListOpenTurnsAsync(
                tempDirectory.FullName,
                "session-123",
                CancellationToken.None));
            Assert.Null(await stateStore.TryReadCurrentAsync(
                tempDirectory.FullName,
                "session-123",
                CancellationToken.None));
            PromptObservation observation = Assert.Single(
                await stateStore.ListPromptObservationsAsync(
                    tempDirectory.FullName,
                    "session-123",
                    CancellationToken.None));
            Assert.Equal("observation-only", observation.Classification);
        }
        finally
        {
            tempDirectory.Delete(recursive: true);
        }
    }

    [Theory]
    [InlineData("<system_reminder>\nNotification assignment reminder.\n</system_reminder>")]
    [InlineData("<system_notification>\nAgent finished processing.\n</system_notification>")]
    [InlineData("Contents of AGENTS.md:\n# Instructions for Current Repository")]
    public async Task HandleStopAsyncNonSubagentObservationOnlyBetweenMainTurnAndStopDeliversMainSummary(
        string observationPrompt)
    {
        DirectoryInfo tempDirectory = Directory.CreateTempSubdirectory();
        using EnvironmentScope environment = SetTelegramEnvironment();

        try
        {
            WorkspaceStateStore stateStore = new(
                TimeProvider.System,
                NullLogger<WorkspaceStateStore>.Instance);
            RecordingHttpMessageHandler handler = new();
            HookCommandService service = CreateHookCommandService(handler, stateStore);
            const string stopTimestamp = "2026-03-14T15:51:50.783Z";

            _ = await service.HandleUserPromptSubmitAsync(
                CreateJsonStream(
                    new UserPromptSubmitHookInput
                    {
                        Cwd = tempDirectory.FullName,
                        SessionId = "session-123",
                        Timestamp = "2026-03-14T15:51:40.783Z",
                        TranscriptPath = "/workspace/transcript.json",
                        Prompt = "Ship the main turn.",
                    },
                    AppJsonSerializerContext.Default.UserPromptSubmitHookInput),
                new MemoryStream(),
                CancellationToken.None);
            NotificationTurn mainTurn = Assert.Single(await stateStore.ListOpenTurnsAsync(
                tempDirectory.FullName,
                "session-123",
                CancellationToken.None));
            await WriteSummaryAsync(
                tempDirectory.FullName,
                "session-123",
                mainTurn,
                new NotificationSummary
                {
                    SessionId = "session-123",
                    NotificationTurnId = mainTurn.NotificationTurnId,
                    NotificationNonce = mainTurn.NotificationNonce,
                    UpdatedAt = stopTimestamp,
                    Summary = "The main turn summary should deliver.",
                });

            _ = await service.HandleUserPromptSubmitAsync(
                CreateJsonStream(
                    new UserPromptSubmitHookInput
                    {
                        Cwd = tempDirectory.FullName,
                        SessionId = "session-123",
                        Timestamp = "2026-03-14T15:51:45.783Z",
                        TranscriptPath = "/workspace/transcript.json",
                        Prompt = observationPrompt,
                    },
                    AppJsonSerializerContext.Default.UserPromptSubmitHookInput),
                new MemoryStream(),
                CancellationToken.None);

            NotificationTurn stillOpenTurn = Assert.Single(await stateStore.ListOpenTurnsAsync(
                tempDirectory.FullName,
                "session-123",
                CancellationToken.None));
            Assert.Equal(mainTurn.NotificationTurnId, stillOpenTurn.NotificationTurnId);
            CurrentNotificationState? current = await stateStore.TryReadCurrentAsync(
                tempDirectory.FullName,
                "session-123",
                CancellationToken.None);
            Assert.Equal(mainTurn.NotificationTurnId, current?.NotificationTurnId);

            _ = await service.HandleStopAsync(
                CreateJsonStream(
                    CreateStopInput(tempDirectory.FullName, stopTimestamp),
                    AppJsonSerializerContext.Default.StopHookInput),
                new MemoryStream(),
                CancellationToken.None);

            TelegramSendMessageRequest payload = DeserializeTelegramPayload(
                Assert.Single(handler.Requests));
            Assert.Contains(
                "摘要：The main turn summary should deliver.",
                payload.Text,
                StringComparison.Ordinal);
            Assert.Contains(mainTurn.NotificationTurnId, payload.Text, StringComparison.Ordinal);
        }
        finally
        {
            tempDirectory.Delete(recursive: true);
        }
    }

    [Fact]
    public async Task HandleUserPromptSubmitAsyncTreatsReviewerSubagentAsObservationOnly()
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
                Timestamp = "2026-03-14T15:51:45.783Z",
                TranscriptPath = "/workspace/transcript.json",
                Prompt = "You are an independent Reviewer subagent. Review Group 1 changes.",
            };
            await using MemoryStream output = new();

            int exitCode = await service.HandleUserPromptSubmitAsync(
                CreateJsonStream(
                    promptInput,
                    AppJsonSerializerContext.Default.UserPromptSubmitHookInput),
                output,
                CancellationToken.None);

            Assert.Equal(0, exitCode);
            Assert.Equal(0, output.Length);
            Assert.Empty(await stateStore.ListOpenTurnsAsync(
                tempDirectory.FullName,
                "session-123",
                CancellationToken.None));
        }
        finally
        {
            tempDirectory.Delete(recursive: true);
        }
    }

    [Fact]
    public async Task HandleUserPromptSubmitAsyncTreatsSystemNotificationAsObservationOnly()
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
                Timestamp = "2026-03-14T15:51:45.783Z",
                TranscriptPath = "/workspace/transcript.json",
                Prompt = string.Join(
                    '\n',
                    [
                        "<system_notification>",
                        "Agent finished processing.",
                        "</system_notification>",
                    ]),
            };
            await using MemoryStream output = new();

            int exitCode = await service.HandleUserPromptSubmitAsync(
                CreateJsonStream(
                    promptInput,
                    AppJsonSerializerContext.Default.UserPromptSubmitHookInput),
                output,
                CancellationToken.None);

            Assert.Equal(0, exitCode);
            Assert.Equal(0, output.Length);
            Assert.Empty(await stateStore.ListOpenTurnsAsync(
                tempDirectory.FullName,
                "session-123",
                CancellationToken.None));
        }
        finally
        {
            tempDirectory.Delete(recursive: true);
        }
    }

    [Fact]
    public async Task ReviewerSubagentStopDoesNotCloseMainTurnAndLaterMainStopCanNotify()
    {
        DirectoryInfo tempDirectory = Directory.CreateTempSubdirectory();
        using EnvironmentScope environment = SetTelegramEnvironment();

        try
        {
            WorkspaceStateStore stateStore = new(
                TimeProvider.System,
                NullLogger<WorkspaceStateStore>.Instance);
            NotificationTurn turn = await CreateTurnAsync(
                stateStore,
                tempDirectory.FullName,
                "session-123",
                "2026-03-14T15:51:40.783Z");
            await WriteSummaryAsync(
                tempDirectory.FullName,
                "session-123",
                turn,
                new NotificationSummary
                {
                    SessionId = "session-123",
                    NotificationTurnId = turn.NotificationTurnId,
                    NotificationNonce = turn.NotificationNonce,
                    UpdatedAt = "2026-03-14T15:51:55.783Z",
                    Summary = "The main turn summary remains valid.",
                });
            RecordingHttpMessageHandler handler = new();
            HookCommandService service = CreateHookCommandService(handler, stateStore);

            _ = await service.HandleUserPromptSubmitAsync(
                CreateJsonStream(
                    new UserPromptSubmitHookInput
                    {
                        Cwd = tempDirectory.FullName,
                        SessionId = "session-123",
                        Timestamp = "2026-03-14T15:51:45.783Z",
                        TranscriptPath = "/workspace/transcript.json",
                        Prompt = "You are an independent Reviewer subagent. "
                            + "Review Group 1 changes.",
                    },
                    AppJsonSerializerContext.Default.UserPromptSubmitHookInput),
                new MemoryStream(),
                CancellationToken.None);

            _ = await service.HandleStopAsync(
                CreateJsonStream(
                    CreateStopInput(tempDirectory.FullName, "2026-03-14T15:51:50.783Z"),
                    AppJsonSerializerContext.Default.StopHookInput),
                new MemoryStream(),
                CancellationToken.None);

            NotificationTurn? stillOpenTurn = await stateStore.TryReadTurnAsync(
                tempDirectory.FullName,
                "session-123",
                turn.NotificationTurnId,
                CancellationToken.None);
            Assert.Equal("open", stillOpenTurn?.Status);
            TelegramSendMessageRequest subagentStopPayload = DeserializeTelegramPayload(
                Assert.Single(handler.Requests));
            Assert.Contains("摘要：当前轮未生成摘要。", subagentStopPayload.Text, StringComparison.Ordinal);

            _ = await service.HandleStopAsync(
                CreateJsonStream(
                    CreateStopInput(tempDirectory.FullName, "2026-03-14T15:52:50.783Z"),
                    AppJsonSerializerContext.Default.StopHookInput),
                new MemoryStream(),
                CancellationToken.None);

            Assert.Equal(2, handler.Requests.Count);
            TelegramSendMessageRequest mainStopPayload =
                DeserializeTelegramPayload(handler.Requests[1]);
            Assert.Contains(
                "摘要：The main turn summary remains valid.",
                mainStopPayload.Text,
                StringComparison.Ordinal);
            Assert.Contains(
                turn.NotificationTurnId,
                mainStopPayload.Text,
                StringComparison.Ordinal);
            NotificationTurn? notifiedTurn = await stateStore.TryReadTurnAsync(
                tempDirectory.FullName,
                "session-123",
                turn.NotificationTurnId,
                CancellationToken.None);
            Assert.Equal("notified", notifiedTurn?.Status);
        }
        finally
        {
            tempDirectory.Delete(recursive: true);
        }
    }

    [Fact]
    public async Task HandleStopAsyncPerTurnDurableDeliveryResolvesInterveningSubagentObservation()
    {
        DirectoryInfo tempDirectory = Directory.CreateTempSubdirectory();
        using EnvironmentScope environment = SetTelegramEnvironment();

        try
        {
            WorkspaceStateStore stateStore = new(
                TimeProvider.System,
                NullLogger<WorkspaceStateStore>.Instance);
            RecordingHttpMessageHandler handler = new();
            HookCommandService service = CreateHookCommandService(handler, stateStore);
            const string stopTimestamp = "2026-03-14T15:52:50.783Z";
            NotificationTurn mainTurn = await CreateTurnAsync(
                stateStore,
                tempDirectory.FullName,
                "session-123",
                "2026-03-14T15:51:40.783Z");
            await WriteSummaryAsync(
                tempDirectory.FullName,
                "session-123",
                mainTurn,
                new NotificationSummary
                {
                    SessionId = "session-123",
                    NotificationTurnId = mainTurn.NotificationTurnId,
                    NotificationNonce = mainTurn.NotificationNonce,
                    UpdatedAt = "2026-03-14T15:52:49.783Z",
                    Summary = "The main turn summary remains deliverable after observation.",
                });

            _ = await service.HandleUserPromptSubmitAsync(
                CreateJsonStream(
                    new UserPromptSubmitHookInput
                    {
                        Cwd = tempDirectory.FullName,
                        SessionId = "session-123",
                        Timestamp = "2026-03-14T15:51:45.783Z",
                        TranscriptPath = "/workspace/transcript.json",
                        Prompt = "You are an independent Reviewer subagent. "
                            + "Review Group 1 changes.",
                    },
                    AppJsonSerializerContext.Default.UserPromptSubmitHookInput),
                new MemoryStream(),
                CancellationToken.None);

            const string interveningStopTimestamp = "2026-03-14T15:51:50.783Z";
            string interveningNotificationKey = CreateStopNotificationKeyForTest(
                interveningStopTimestamp);
            const string interveningTurnId = "delivered-turn";
            await WriteNotificationRecordAsync(
                AppPaths.GetNotificationRecordPath(
                    tempDirectory.FullName,
                    "session-123",
                    interveningTurnId,
                    interveningNotificationKey),
                new NotificationRecord
                {
                    SessionId = "session-123",
                    NotificationTurnId = interveningTurnId,
                    NotificationKey = interveningNotificationKey,
                    WorkspacePath = tempDirectory.FullName,
                    StopTimestamp = interveningStopTimestamp,
                    SentAt = "2026-03-14T15:51:51.783Z",
                    SummaryUpdatedAt = interveningStopTimestamp,
                    DeliveryStatus = "sent",
                });

            _ = await service.HandleStopAsync(
                CreateJsonStream(
                    CreateStopInput(tempDirectory.FullName, stopTimestamp),
                    AppJsonSerializerContext.Default.StopHookInput),
                new MemoryStream(),
                CancellationToken.None);

            TelegramSendMessageRequest payload = DeserializeTelegramPayload(
                Assert.Single(handler.Requests));
            Assert.Contains(
                "摘要：The main turn summary remains deliverable after observation.",
                payload.Text,
                StringComparison.Ordinal);
            Assert.Contains(mainTurn.NotificationTurnId, payload.Text, StringComparison.Ordinal);
            Assert.False(File.Exists(AppPaths.GetSessionNotificationRecordPath(
                tempDirectory.FullName,
                "session-123",
                interveningNotificationKey)));
        }
        finally
        {
            tempDirectory.Delete(recursive: true);
        }
    }

    [Fact]
    public async Task HandleUserPromptSubmitAsyncCreatesTurnAndAssignmentForMainPrompt()
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
                TranscriptPath = "/workspace/transcript.json",
                Prompt = "Ship the notification redesign.",
            };
            await using MemoryStream output = new();

            int exitCode = await service.HandleUserPromptSubmitAsync(
                CreateJsonStream(
                    promptInput,
                    AppJsonSerializerContext.Default.UserPromptSubmitHookInput),
                output,
                CancellationToken.None);

            Assert.Equal(0, exitCode);
            NotificationTurn turn = Assert.Single(await stateStore.ListOpenTurnsAsync(
                tempDirectory.FullName,
                "session-123",
                CancellationToken.None));
            HookResponse response = await DeserializeHookResponseAsync(output);
            Assert.Equal("UserPromptSubmit", response.HookSpecificOutput?.HookEventName);
            string assignment = Assert.IsType<string>(
                response.HookSpecificOutput?.AdditionalContext);
            Assert.Contains(AppPaths.GetSummaryStatePath(
                tempDirectory.FullName,
                "session-123",
                turn.NotificationTurnId), assignment, StringComparison.Ordinal);
            Assert.Contains(turn.NotificationNonce, assignment, StringComparison.Ordinal);
            Assert.DoesNotContain("notify-summary.json", assignment, StringComparison.Ordinal);
        }
        finally
        {
            tempDirectory.Delete(recursive: true);
        }
    }

    [Fact]
    public async Task HandleUserPromptSubmitAsyncUsesHookSpecificContextForVsCodeSurface()
    {
        DirectoryInfo tempDirectory = Directory.CreateTempSubdirectory();

        try
        {
            WorkspaceStateStore stateStore = new(
                TimeProvider.System,
                NullLogger<WorkspaceStateStore>.Instance);
            HookCommandService service = CreateHookCommandService(
                new RecordingHttpMessageHandler(),
                stateStore: stateStore,
                surface: HookSurface.VsCode);
            UserPromptSubmitHookInput promptInput = new()
            {
                Cwd = tempDirectory.FullName,
                SessionId = "session-123",
                Timestamp = "2026-03-14T15:51:50.783Z",
                TranscriptPath = "/workspace/transcript.json",
                Prompt = "Ship the notification redesign.",
            };
            await using MemoryStream output = new();

            int exitCode = await service.HandleUserPromptSubmitAsync(
                CreateJsonStream(
                    promptInput,
                    AppJsonSerializerContext.Default.UserPromptSubmitHookInput),
                output,
                CancellationToken.None);

            Assert.Equal(0, exitCode);
            NotificationTurn turn = Assert.Single(await stateStore.ListOpenTurnsAsync(
                tempDirectory.FullName,
                "session-123",
                CancellationToken.None));
            JsonElement root = ReadJsonRootElement(output);
            AssertJsonProperties(root, "hookSpecificOutput");
            JsonElement hookSpecificOutput = root.GetProperty("hookSpecificOutput");
            AssertJsonProperties(
                hookSpecificOutput,
                "hookEventName",
                "additionalContext");
            Assert.Equal(
                "UserPromptSubmit",
                hookSpecificOutput.GetProperty("hookEventName").GetString());
            Assert.False(root.TryGetProperty("modifiedPrompt", out _));
            Assert.False(root.TryGetProperty("additionalContext", out _));
            HookResponse response = await DeserializeHookResponseAsync(output);
            Assert.Equal("UserPromptSubmit", response.HookSpecificOutput?.HookEventName);
            string assignment = Assert.IsType<string>(
                response.HookSpecificOutput?.AdditionalContext);
            Assert.Contains(AppPaths.GetSummaryStatePath(
                tempDirectory.FullName,
                "session-123",
                turn.NotificationTurnId), assignment, StringComparison.Ordinal);
            Assert.Contains(turn.NotificationNonce, assignment, StringComparison.Ordinal);
            Assert.Contains(
                "write summary in Chinese when practical",
                assignment,
                StringComparison.Ordinal);
        }
        finally
        {
            tempDirectory.Delete(recursive: true);
        }
    }

    [Fact]
    public async Task HandleUserPromptSubmitAsyncUsesModifiedPromptForCopilotCliAssignment()
    {
        DirectoryInfo tempDirectory = Directory.CreateTempSubdirectory();

        try
        {
            WorkspaceStateStore stateStore = new(
                TimeProvider.System,
                NullLogger<WorkspaceStateStore>.Instance);
            HookCommandService service = CreateHookCommandService(
                new RecordingHttpMessageHandler(),
                stateStore: stateStore,
                surface: HookSurface.CopilotCli);
            UserPromptSubmitHookInput promptInput = new()
            {
                Cwd = tempDirectory.FullName,
                SessionId = "session-123",
                Timestamp = "2026-03-14T15:51:50.783Z",
                TranscriptPath = "/workspace/transcript.json",
                Prompt = "Ship the notification redesign.",
            };
            await using MemoryStream output = new();

            int exitCode = await service.HandleUserPromptSubmitAsync(
                CreateJsonStream(
                    promptInput,
                    AppJsonSerializerContext.Default.UserPromptSubmitHookInput),
                output,
                CancellationToken.None);

            Assert.Equal(0, exitCode);
            NotificationTurn turn = Assert.Single(await stateStore.ListOpenTurnsAsync(
                tempDirectory.FullName,
                "session-123",
                CancellationToken.None));
            JsonElement root = ReadJsonRootElement(output);
            AssertJsonProperties(root, "modifiedPrompt");
            Assert.False(root.TryGetProperty("hookSpecificOutput", out _));
            Assert.False(root.TryGetProperty("additionalContext", out _));
            CopilotCliHookOutput response = await DeserializeCopilotCliHookOutputAsync(output);
            Assert.Null(response.AdditionalContext);
            string modifiedPrompt = Assert.IsType<string>(response.ModifiedPrompt);
            const string reminderStart = "\n\n<system_reminder>\n";
            const string reminderEnd = "\n</system_reminder>";
            Assert.StartsWith(
                promptInput.Prompt + reminderStart,
                modifiedPrompt,
                StringComparison.Ordinal);
            Assert.EndsWith(reminderEnd, modifiedPrompt, StringComparison.Ordinal);
            string assignment = modifiedPrompt.Substring(
                promptInput.Prompt.Length + reminderStart.Length,
                modifiedPrompt.Length
                    - promptInput.Prompt.Length
                    - reminderStart.Length
                    - reminderEnd.Length);
            Assert.Equal(
                promptInput.Prompt + reminderStart + assignment + reminderEnd,
                modifiedPrompt);
            Assert.StartsWith("Notification Assignment", assignment, StringComparison.Ordinal);
            Assert.Contains(AppPaths.GetSummaryStatePath(
                tempDirectory.FullName,
                "session-123",
                turn.NotificationTurnId), assignment, StringComparison.Ordinal);
            Assert.Contains(turn.NotificationNonce, assignment, StringComparison.Ordinal);
            Assert.Contains(
                "write summary in Chinese when practical",
                assignment,
                StringComparison.Ordinal);
        }
        finally
        {
            tempDirectory.Delete(recursive: true);
        }
    }

    [Fact]
    public async Task HandleStopAsyncSendsValidatedPerTurnSummaryAndSuppressesDuplicate()
    {
        DirectoryInfo tempDirectory = Directory.CreateTempSubdirectory();
        using EnvironmentScope environment = SetTelegramEnvironment();

        try
        {
            WorkspaceStateStore stateStore = new(
                TimeProvider.System,
                NullLogger<WorkspaceStateStore>.Instance);
            NotificationTurn turn = await CreateTurnAsync(
                stateStore,
                tempDirectory.FullName,
                "session-123");
            await WriteSummaryAsync(
                tempDirectory.FullName,
                "session-123",
                turn,
                new NotificationSummary
                {
                    SessionId = "session-123",
                    NotificationTurnId = turn.NotificationTurnId,
                    NotificationNonce = turn.NotificationNonce,
                    UpdatedAt = "2026-03-14T15:51:50.783Z",
                    Summary = "The redesign is complete.",
                });

            RecordingHttpMessageHandler handler = new();
            HookCommandService service = CreateHookCommandService(handler, stateStore);
            StopHookInput stopInput = CreateStopInput(tempDirectory.FullName);
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
            Assert.Contains("摘要：The redesign is complete.", payload.Text, StringComparison.Ordinal);
            Assert.Contains(turn.NotificationTurnId, payload.Text, StringComparison.Ordinal);
        }
        finally
        {
            tempDirectory.Delete(recursive: true);
        }
    }

    [Fact]
    public async Task HandleStopAsyncSendsDegradedFallbackForMissingOrStaleSummaryWithoutBlocking()
    {
        DirectoryInfo tempDirectory = Directory.CreateTempSubdirectory();
        using EnvironmentScope environment = SetTelegramEnvironment();

        try
        {
            WorkspaceStateStore stateStore = new(
                TimeProvider.System,
                NullLogger<WorkspaceStateStore>.Instance);
            NotificationTurn turn = await CreateTurnAsync(
                stateStore,
                tempDirectory.FullName,
                "session-123");
            await WriteSummaryAsync(
                tempDirectory.FullName,
                "session-123",
                turn,
                new NotificationSummary
                {
                    SessionId = "session-123",
                    NotificationTurnId = "another-turn",
                    NotificationNonce = turn.NotificationNonce,
                    UpdatedAt = "2026-03-14T15:51:50.783Z",
                    Summary = "Stale summary.",
                });

            RecordingHttpMessageHandler handler = new();
            HookCommandService service = CreateHookCommandService(handler, stateStore);
            await using MemoryStream output = new();

            int exitCode = await service.HandleStopAsync(
                CreateJsonStream(
                    CreateStopInput(tempDirectory.FullName),
                    AppJsonSerializerContext.Default.StopHookInput),
                output,
                CancellationToken.None);

            Assert.Equal(0, exitCode);
            Assert.Equal(0, output.Length);
            TelegramSendMessageRequest payload = DeserializeTelegramPayload(
                Assert.Single(handler.Requests));
            Assert.Contains("摘要：当前轮未生成摘要。", payload.Text, StringComparison.Ordinal);
            NotificationTurn? updatedTurn = await stateStore.TryReadTurnAsync(
                tempDirectory.FullName,
                "session-123",
                turn.NotificationTurnId,
                CancellationToken.None);
            Assert.Equal("notified", updatedTurn?.Status);
        }
        finally
        {
            tempDirectory.Delete(recursive: true);
        }
    }

    [Fact]
    public async Task HandleStopAsyncDefersPendingPlaceholderSummaryAndLaterStopSends()
    {
        DirectoryInfo tempDirectory = Directory.CreateTempSubdirectory();
        using EnvironmentScope environment = SetTelegramEnvironment();

        try
        {
            WorkspaceStateStore stateStore = new(
                TimeProvider.System,
                NullLogger<WorkspaceStateStore>.Instance);
            NotificationTurn turn = await CreateTurnAsync(
                stateStore,
                tempDirectory.FullName,
                "session-123");
            RecordingHttpMessageHandler handler = new();
            HookCommandService service = CreateHookCommandService(handler, stateStore);
            const string firstStopTimestamp = "2026-03-14T15:51:50.783Z";
            const string secondStopTimestamp = "2026-03-14T15:52:50.783Z";

            _ = await service.HandleStopAsync(
                CreateJsonStream(
                    CreateStopInput(tempDirectory.FullName, firstStopTimestamp),
                    AppJsonSerializerContext.Default.StopHookInput),
                new MemoryStream(),
                CancellationToken.None);

            Assert.Empty(handler.Requests);
            await AssertPendingStopAsync(
                stateStore,
                tempDirectory.FullName,
                turn,
                firstStopTimestamp,
                "summary must be a non-empty human-readable sentence");

            await WriteSummaryAsync(
                tempDirectory.FullName,
                "session-123",
                turn,
                new NotificationSummary
                {
                    SessionId = "session-123",
                    NotificationTurnId = turn.NotificationTurnId,
                    NotificationNonce = turn.NotificationNonce,
                    UpdatedAt = secondStopTimestamp,
                    Summary = "The deferred placeholder summary is complete.",
                });

            _ = await service.HandleStopAsync(
                CreateJsonStream(
                    CreateStopInput(tempDirectory.FullName, secondStopTimestamp),
                    AppJsonSerializerContext.Default.StopHookInput),
                new MemoryStream(),
                CancellationToken.None);

            TelegramSendMessageRequest payload = DeserializeTelegramPayload(
                Assert.Single(handler.Requests));
            Assert.Contains(
                "摘要：The deferred placeholder summary is complete.",
                payload.Text,
                StringComparison.Ordinal);
            NotificationTurn? notifiedTurn = await stateStore.TryReadTurnAsync(
                tempDirectory.FullName,
                "session-123",
                turn.NotificationTurnId,
                CancellationToken.None);
            Assert.Equal("notified", notifiedTurn?.Status);
        }
        finally
        {
            tempDirectory.Delete(recursive: true);
        }
    }

    [Fact]
    public async Task HandleStopAsyncDelayedOldPlaceholderStopDefersSessionFallbackAndRetrySendsExact()
    {
        DirectoryInfo tempDirectory = Directory.CreateTempSubdirectory();
        using EnvironmentScope environment = SetTelegramEnvironment();

        try
        {
            RecordingHttpMessageHandler handler = new();
            const string firstPromptTimestamp = "2026-03-14T15:51:40.783Z";
            const string firstStopTimestamp = "2026-03-14T15:51:50.783Z";
            WorkspaceStateStore stateStore = new(
                FixedUtcNow(),
                NullLogger<WorkspaceStateStore>.Instance);
            HookCommandService service = CreateHookCommandService(handler, stateStore);

            _ = await service.HandleUserPromptSubmitAsync(
                CreateJsonStream(
                    new UserPromptSubmitHookInput
                    {
                        Cwd = tempDirectory.FullName,
                        SessionId = "session-123",
                        Timestamp = firstPromptTimestamp,
                        TranscriptPath = "/workspace/transcript.json",
                        Prompt = "Ship the first prompt.",
                    },
                    AppJsonSerializerContext.Default.UserPromptSubmitHookInput),
                new MemoryStream(),
                CancellationToken.None);
            CurrentNotificationState firstCurrent = (await stateStore.TryReadCurrentAsync(
                tempDirectory.FullName,
                "session-123",
                CancellationToken.None))!;
            NotificationTurn firstTurn = (await stateStore.TryReadTurnAsync(
                tempDirectory.FullName,
                "session-123",
                firstCurrent.NotificationTurnId,
                CancellationToken.None))!;

            _ = await service.HandleUserPromptSubmitAsync(
                CreateJsonStream(
                    new UserPromptSubmitHookInput
                    {
                        Cwd = tempDirectory.FullName,
                        SessionId = "session-123",
                        Timestamp = "2026-03-14T15:52:40.783Z",
                        TranscriptPath = "/workspace/transcript.json",
                        Prompt = "Ship the second prompt.",
                    },
                    AppJsonSerializerContext.Default.UserPromptSubmitHookInput),
                new MemoryStream(),
                CancellationToken.None);
            NotificationTurn? abandonedFirstTurn = await stateStore.TryReadTurnAsync(
                tempDirectory.FullName,
                "session-123",
                firstTurn.NotificationTurnId,
                CancellationToken.None);
            Assert.Equal("abandoned", abandonedFirstTurn?.Status);

            _ = await service.HandleStopAsync(
                CreateJsonStream(
                    CreateStopInput(tempDirectory.FullName, firstStopTimestamp),
                    AppJsonSerializerContext.Default.StopHookInput),
                new MemoryStream(),
                CancellationToken.None);

            string notificationKey = CreateStopNotificationKeyForTest(firstStopTimestamp);
            Assert.Empty(handler.Requests);
            Assert.False(File.Exists(AppPaths.GetSessionNotificationRecordPath(
                tempDirectory.FullName,
                "session-123",
                notificationKey)));
            Assert.False(File.Exists(AppPaths.GetTurnDeliveryClaimPath(
                tempDirectory.FullName,
                "session-123",
                firstTurn.NotificationTurnId)));
            Assert.False(File.Exists(AppPaths.GetNotificationRecordPath(
                tempDirectory.FullName,
                "session-123",
                firstTurn.NotificationTurnId,
                notificationKey)));

            await WriteSummaryAsync(
                tempDirectory.FullName,
                "session-123",
                firstTurn,
                new NotificationSummary
                {
                    SessionId = "session-123",
                    NotificationTurnId = firstTurn.NotificationTurnId,
                    NotificationNonce = firstTurn.NotificationNonce,
                    UpdatedAt = firstStopTimestamp,
                    Summary = "The delayed first prompt summary is complete.",
                });

            _ = await service.HandleStopAsync(
                CreateJsonStream(
                    CreateStopInput(tempDirectory.FullName, firstStopTimestamp),
                    AppJsonSerializerContext.Default.StopHookInput),
                new MemoryStream(),
                CancellationToken.None);

            TelegramSendMessageRequest payload = DeserializeTelegramPayload(
                Assert.Single(handler.Requests));
            Assert.Contains(
                "摘要：The delayed first prompt summary is complete.",
                payload.Text,
                StringComparison.Ordinal);
            Assert.Contains(firstTurn.NotificationTurnId, payload.Text, StringComparison.Ordinal);
        }
        finally
        {
            tempDirectory.Delete(recursive: true);
        }
    }

    [Fact]
    public async Task HandleStopAsyncRecoversAbandonedExactSummaryDespiteLaterEligiblePlaceholder()
    {
        DirectoryInfo tempDirectory = Directory.CreateTempSubdirectory();
        using EnvironmentScope environment = SetTelegramEnvironment();

        try
        {
            WorkspaceStateStore stateStore = new(
                TimeProvider.System,
                NullLogger<WorkspaceStateStore>.Instance);
            RecordingHttpMessageHandler handler = new();
            HookCommandService service = CreateHookCommandService(handler, stateStore);
            const string firstPromptTimestamp = "2026-03-14T15:51:40.783Z";
            const string secondPromptTimestamp = "2026-03-14T15:52:40.783Z";
            const string firstStopTimestamp = "2026-03-14T15:52:50.783Z";

            _ = await service.HandleUserPromptSubmitAsync(
                CreateJsonStream(
                    new UserPromptSubmitHookInput
                    {
                        Cwd = tempDirectory.FullName,
                        SessionId = "session-123",
                        Timestamp = firstPromptTimestamp,
                        TranscriptPath = "/workspace/transcript.json",
                        Prompt = "Ship the first prompt.",
                    },
                    AppJsonSerializerContext.Default.UserPromptSubmitHookInput),
                new MemoryStream(),
                CancellationToken.None);
            CurrentNotificationState firstCurrent = (await stateStore.TryReadCurrentAsync(
                tempDirectory.FullName,
                "session-123",
                CancellationToken.None))!;
            NotificationTurn firstTurn = (await stateStore.TryReadTurnAsync(
                tempDirectory.FullName,
                "session-123",
                firstCurrent.NotificationTurnId,
                CancellationToken.None))!;

            _ = await service.HandleUserPromptSubmitAsync(
                CreateJsonStream(
                    new UserPromptSubmitHookInput
                    {
                        Cwd = tempDirectory.FullName,
                        SessionId = "session-123",
                        Timestamp = secondPromptTimestamp,
                        TranscriptPath = "/workspace/transcript.json",
                        Prompt = "Ship the second prompt.",
                    },
                    AppJsonSerializerContext.Default.UserPromptSubmitHookInput),
                new MemoryStream(),
                CancellationToken.None);
            NotificationTurn? abandonedFirstTurn = await stateStore.TryReadTurnAsync(
                tempDirectory.FullName,
                "session-123",
                firstTurn.NotificationTurnId,
                CancellationToken.None);
            Assert.Equal("abandoned", abandonedFirstTurn?.Status);

            await WriteSummaryAsync(
                tempDirectory.FullName,
                "session-123",
                firstTurn,
                new NotificationSummary
                {
                    SessionId = "session-123",
                    NotificationTurnId = firstTurn.NotificationTurnId,
                    NotificationNonce = firstTurn.NotificationNonce,
                    UpdatedAt = firstStopTimestamp,
                    Status = "completed",
                    Summary = "The abandoned exact first summary should beat the later placeholder.",
                });

            _ = await service.HandleStopAsync(
                CreateJsonStream(
                    CreateStopInput(tempDirectory.FullName, firstStopTimestamp),
                    AppJsonSerializerContext.Default.StopHookInput),
                new MemoryStream(),
                CancellationToken.None);
            _ = await service.HandleStopAsync(
                CreateJsonStream(
                    CreateStopInput(tempDirectory.FullName, firstStopTimestamp),
                    AppJsonSerializerContext.Default.StopHookInput),
                new MemoryStream(),
                CancellationToken.None);

            TelegramSendMessageRequest payload = DeserializeTelegramPayload(
                Assert.Single(handler.Requests));
            Assert.Contains(
                "摘要：The abandoned exact first summary should beat the later placeholder.",
                payload.Text,
                StringComparison.Ordinal);
            Assert.Contains(firstTurn.NotificationTurnId, payload.Text, StringComparison.Ordinal);
        }
        finally
        {
            tempDirectory.Delete(recursive: true);
        }
    }

    [Fact]
    public async Task HandleStopAsyncRecoversOlderAbandonedExactSummaryBeforeLaterAbandonedPlaceholder()
    {
        DirectoryInfo tempDirectory = Directory.CreateTempSubdirectory();
        using EnvironmentScope environment = SetTelegramEnvironment();

        try
        {
            WorkspaceStateStore stateStore = new(
                TimeProvider.System,
                NullLogger<WorkspaceStateStore>.Instance);
            RecordingHttpMessageHandler handler = new();
            HookCommandService service = CreateHookCommandService(handler, stateStore);
            const string firstPromptTimestamp = "2026-03-14T15:51:40.783Z";
            const string secondPromptTimestamp = "2026-03-14T15:52:40.783Z";
            const string stopTimestamp = "2026-03-14T15:52:50.783Z";
            const string currentPromptTimestamp = "2026-03-14T15:53:40.783Z";

            _ = await service.HandleUserPromptSubmitAsync(
                CreateJsonStream(
                    new UserPromptSubmitHookInput
                    {
                        Cwd = tempDirectory.FullName,
                        SessionId = "session-123",
                        Timestamp = firstPromptTimestamp,
                        TranscriptPath = "/workspace/transcript.json",
                        Prompt = "Ship the first prompt.",
                    },
                    AppJsonSerializerContext.Default.UserPromptSubmitHookInput),
                new MemoryStream(),
                CancellationToken.None);
            CurrentNotificationState firstCurrent = (await stateStore.TryReadCurrentAsync(
                tempDirectory.FullName,
                "session-123",
                CancellationToken.None))!;
            NotificationTurn firstTurn = (await stateStore.TryReadTurnAsync(
                tempDirectory.FullName,
                "session-123",
                firstCurrent.NotificationTurnId,
                CancellationToken.None))!;

            _ = await service.HandleUserPromptSubmitAsync(
                CreateJsonStream(
                    new UserPromptSubmitHookInput
                    {
                        Cwd = tempDirectory.FullName,
                        SessionId = "session-123",
                        Timestamp = secondPromptTimestamp,
                        TranscriptPath = "/workspace/transcript.json",
                        Prompt = "Ship the second prompt.",
                    },
                    AppJsonSerializerContext.Default.UserPromptSubmitHookInput),
                new MemoryStream(),
                CancellationToken.None);
            CurrentNotificationState secondCurrent = (await stateStore.TryReadCurrentAsync(
                tempDirectory.FullName,
                "session-123",
                CancellationToken.None))!;
            NotificationTurn secondTurn = (await stateStore.TryReadTurnAsync(
                tempDirectory.FullName,
                "session-123",
                secondCurrent.NotificationTurnId,
                CancellationToken.None))!;

            _ = await service.HandleUserPromptSubmitAsync(
                CreateJsonStream(
                    new UserPromptSubmitHookInput
                    {
                        Cwd = tempDirectory.FullName,
                        SessionId = "session-123",
                        Timestamp = currentPromptTimestamp,
                        TranscriptPath = "/workspace/transcript.json",
                        Prompt = "Ship the current prompt.",
                    },
                    AppJsonSerializerContext.Default.UserPromptSubmitHookInput),
                new MemoryStream(),
                CancellationToken.None);
            NotificationTurn? abandonedFirstTurn = await stateStore.TryReadTurnAsync(
                tempDirectory.FullName,
                "session-123",
                firstTurn.NotificationTurnId,
                CancellationToken.None);
            NotificationTurn? abandonedSecondTurn = await stateStore.TryReadTurnAsync(
                tempDirectory.FullName,
                "session-123",
                secondTurn.NotificationTurnId,
                CancellationToken.None);
            Assert.Equal("abandoned", abandonedFirstTurn?.Status);
            Assert.Equal("abandoned", abandonedSecondTurn?.Status);

            await WriteSummaryAsync(
                tempDirectory.FullName,
                "session-123",
                firstTurn,
                new NotificationSummary
                {
                    SessionId = "session-123",
                    NotificationTurnId = firstTurn.NotificationTurnId,
                    NotificationNonce = firstTurn.NotificationNonce,
                    UpdatedAt = stopTimestamp,
                    Status = "completed",
                    Summary = "The older abandoned exact summary should beat the later abandoned placeholder.",
                });

            _ = await service.HandleStopAsync(
                CreateJsonStream(
                    CreateStopInput(tempDirectory.FullName, stopTimestamp),
                    AppJsonSerializerContext.Default.StopHookInput),
                new MemoryStream(),
                CancellationToken.None);
            _ = await service.HandleStopAsync(
                CreateJsonStream(
                    CreateStopInput(tempDirectory.FullName, stopTimestamp),
                    AppJsonSerializerContext.Default.StopHookInput),
                new MemoryStream(),
                CancellationToken.None);

            TelegramSendMessageRequest payload = DeserializeTelegramPayload(
                Assert.Single(handler.Requests));
            Assert.Contains(
                "摘要：The older abandoned exact summary should beat the later abandoned placeholder.",
                payload.Text,
                StringComparison.Ordinal);
            Assert.Contains(firstTurn.NotificationTurnId, payload.Text, StringComparison.Ordinal);
            Assert.DoesNotContain(secondTurn.NotificationTurnId, payload.Text, StringComparison.Ordinal);
        }
        finally
        {
            tempDirectory.Delete(recursive: true);
        }
    }

    [Fact]
    public async Task HandleStopAsyncSuppressesMultipleAbandonedCompletedExactSummaries()
    {
        DirectoryInfo tempDirectory = Directory.CreateTempSubdirectory();
        using EnvironmentScope environment = SetTelegramEnvironment();

        try
        {
            WorkspaceStateStore stateStore = new(
                TimeProvider.System,
                NullLogger<WorkspaceStateStore>.Instance);
            RecordingHttpMessageHandler handler = new();
            HookCommandService service = CreateHookCommandService(handler, stateStore);
            const string firstPromptTimestamp = "2026-03-14T15:51:40.783Z";
            const string secondPromptTimestamp = "2026-03-14T15:52:40.783Z";
            const string stopTimestamp = "2026-03-14T15:52:50.783Z";
            const string currentPromptTimestamp = "2026-03-14T15:53:40.783Z";

            _ = await service.HandleUserPromptSubmitAsync(
                CreateJsonStream(
                    new UserPromptSubmitHookInput
                    {
                        Cwd = tempDirectory.FullName,
                        SessionId = "session-123",
                        Timestamp = firstPromptTimestamp,
                        TranscriptPath = "/workspace/transcript.json",
                        Prompt = "Ship the first prompt.",
                    },
                    AppJsonSerializerContext.Default.UserPromptSubmitHookInput),
                new MemoryStream(),
                CancellationToken.None);
            CurrentNotificationState firstCurrent = (await stateStore.TryReadCurrentAsync(
                tempDirectory.FullName,
                "session-123",
                CancellationToken.None))!;
            NotificationTurn firstTurn = (await stateStore.TryReadTurnAsync(
                tempDirectory.FullName,
                "session-123",
                firstCurrent.NotificationTurnId,
                CancellationToken.None))!;

            _ = await service.HandleUserPromptSubmitAsync(
                CreateJsonStream(
                    new UserPromptSubmitHookInput
                    {
                        Cwd = tempDirectory.FullName,
                        SessionId = "session-123",
                        Timestamp = secondPromptTimestamp,
                        TranscriptPath = "/workspace/transcript.json",
                        Prompt = "Ship the second prompt.",
                    },
                    AppJsonSerializerContext.Default.UserPromptSubmitHookInput),
                new MemoryStream(),
                CancellationToken.None);
            CurrentNotificationState secondCurrent = (await stateStore.TryReadCurrentAsync(
                tempDirectory.FullName,
                "session-123",
                CancellationToken.None))!;
            NotificationTurn secondTurn = (await stateStore.TryReadTurnAsync(
                tempDirectory.FullName,
                "session-123",
                secondCurrent.NotificationTurnId,
                CancellationToken.None))!;

            _ = await service.HandleUserPromptSubmitAsync(
                CreateJsonStream(
                    new UserPromptSubmitHookInput
                    {
                        Cwd = tempDirectory.FullName,
                        SessionId = "session-123",
                        Timestamp = currentPromptTimestamp,
                        TranscriptPath = "/workspace/transcript.json",
                        Prompt = "Ship the current prompt.",
                    },
                    AppJsonSerializerContext.Default.UserPromptSubmitHookInput),
                new MemoryStream(),
                CancellationToken.None);
            Assert.Equal(
                "abandoned",
                (await stateStore.TryReadTurnAsync(
                    tempDirectory.FullName,
                    "session-123",
                    firstTurn.NotificationTurnId,
                    CancellationToken.None))?.Status);
            Assert.Equal(
                "abandoned",
                (await stateStore.TryReadTurnAsync(
                    tempDirectory.FullName,
                    "session-123",
                    secondTurn.NotificationTurnId,
                    CancellationToken.None))?.Status);

            foreach (NotificationTurn turn in new[] { firstTurn, secondTurn })
            {
                await WriteSummaryAsync(
                    tempDirectory.FullName,
                    "session-123",
                    turn,
                    new NotificationSummary
                    {
                        SessionId = "session-123",
                        NotificationTurnId = turn.NotificationTurnId,
                        NotificationNonce = turn.NotificationNonce,
                        UpdatedAt = stopTimestamp,
                        Status = "completed",
                        Summary = $"Exact completed summary for {turn.NotificationTurnId}.",
                    });
            }

            _ = await service.HandleStopAsync(
                CreateJsonStream(
                    CreateStopInput(tempDirectory.FullName, stopTimestamp),
                    AppJsonSerializerContext.Default.StopHookInput),
                new MemoryStream(),
                CancellationToken.None);

            Assert.Empty(handler.Requests);
            string notificationKey = CreateStopNotificationKeyForTest(stopTimestamp);
            Assert.False(File.Exists(AppPaths.GetSessionNotificationRecordPath(
                tempDirectory.FullName,
                "session-123",
                notificationKey)));
            foreach (NotificationTurn turn in new[] { firstTurn, secondTurn })
            {
                Assert.False(File.Exists(AppPaths.GetNotificationRecordPath(
                    tempDirectory.FullName,
                    "session-123",
                    turn.NotificationTurnId,
                    notificationKey)));
            }
        }
        finally
        {
            tempDirectory.Delete(recursive: true);
        }
    }

    [Fact]
    public async Task HandleStopAsyncDefersAbandonedCompletedExactWhenAnotherAbandonedExactIsPending()
    {
        DirectoryInfo tempDirectory = Directory.CreateTempSubdirectory();
        using EnvironmentScope environment = SetTelegramEnvironment();

        try
        {
            WorkspaceStateStore stateStore = new(
                TimeProvider.System,
                NullLogger<WorkspaceStateStore>.Instance);
            RecordingHttpMessageHandler handler = new();
            HookCommandService service = CreateHookCommandService(handler, stateStore);
            const string stopTimestamp = "2026-03-14T15:51:50.783Z";
            NotificationTurn completedTurn = await CreateTurnAsync(
                stateStore,
                tempDirectory.FullName,
                "session-123",
                "2026-03-14T15:51:30.783Z");
            NotificationTurn pendingTurn = await CreateTurnAsync(
                stateStore,
                tempDirectory.FullName,
                "session-123",
                "2026-03-14T15:51:35.783Z");
            await WriteSummaryAsync(
                tempDirectory.FullName,
                "session-123",
                completedTurn,
                new NotificationSummary
                {
                    SessionId = "session-123",
                    NotificationTurnId = completedTurn.NotificationTurnId,
                    NotificationNonce = completedTurn.NotificationNonce,
                    UpdatedAt = stopTimestamp,
                    Status = "completed",
                    Summary = "The completed abandoned exact summary must wait behind pending exact.",
                });
            await WriteSummaryAsync(
                tempDirectory.FullName,
                "session-123",
                pendingTurn,
                new NotificationSummary
                {
                    SessionId = "session-123",
                    NotificationTurnId = pendingTurn.NotificationTurnId,
                    NotificationNonce = pendingTurn.NotificationNonce,
                    UpdatedAt = stopTimestamp,
                    Status = "pending",
                    Summary = " ",
                });
            completedTurn.Status = "abandoned";
            pendingTurn.Status = "abandoned";
            await WriteTurnStateAsync(tempDirectory.FullName, completedTurn);
            await WriteTurnStateAsync(tempDirectory.FullName, pendingTurn);

            _ = await service.HandleStopAsync(
                CreateJsonStream(
                    CreateStopInput(tempDirectory.FullName, stopTimestamp),
                    AppJsonSerializerContext.Default.StopHookInput),
                new MemoryStream(),
                CancellationToken.None);

            Assert.Empty(handler.Requests);
            string notificationKey = CreateStopNotificationKeyForTest(stopTimestamp);
            Assert.False(File.Exists(AppPaths.GetSessionNotificationRecordPath(
                tempDirectory.FullName,
                "session-123",
                notificationKey)));
            foreach (NotificationTurn turn in new[] { completedTurn, pendingTurn })
            {
                Assert.False(File.Exists(AppPaths.GetNotificationRecordPath(
                    tempDirectory.FullName,
                    "session-123",
                    turn.NotificationTurnId,
                    notificationKey)));
            }
        }
        finally
        {
            tempDirectory.Delete(recursive: true);
        }
    }

    [Fact]
    public async Task HandleStopAsyncDefersOlderAbandonedExactPendingBeforeLaterAbandonedNonExactAndLaterRecovers()
    {
        DirectoryInfo tempDirectory = Directory.CreateTempSubdirectory();
        using EnvironmentScope environment = SetTelegramEnvironment();

        try
        {
            WorkspaceStateStore stateStore = new(
                TimeProvider.System,
                NullLogger<WorkspaceStateStore>.Instance);
            RecordingHttpMessageHandler handler = new();
            HookCommandService service = CreateHookCommandService(handler, stateStore);
            const string stopTimestamp = "2026-03-14T15:51:50.783Z";
            NotificationTurn pendingTurn = await CreateTurnAsync(
                stateStore,
                tempDirectory.FullName,
                "session-123",
                "2026-03-14T15:51:30.783Z");
            NotificationTurn nonExactTurn = await CreateTurnAsync(
                stateStore,
                tempDirectory.FullName,
                "session-123",
                "2026-03-14T15:51:40.783Z");
            await WriteSummaryAsync(
                tempDirectory.FullName,
                "session-123",
                pendingTurn,
                new NotificationSummary
                {
                    SessionId = "session-123",
                    NotificationTurnId = pendingTurn.NotificationTurnId,
                    NotificationNonce = pendingTurn.NotificationNonce,
                    UpdatedAt = stopTimestamp,
                    Status = "pending",
                    Summary = " ",
                });
            await WriteSummaryAsync(
                tempDirectory.FullName,
                "session-123",
                nonExactTurn,
                new NotificationSummary
                {
                    SessionId = "session-123",
                    NotificationTurnId = nonExactTurn.NotificationTurnId,
                    NotificationNonce = nonExactTurn.NotificationNonce,
                    UpdatedAt = "2026-03-14T15:52:50.783Z",
                    Status = "completed",
                    Summary = "The later abandoned summary belongs to a different Stop.",
                });
            pendingTurn.Status = "abandoned";
            nonExactTurn.Status = "abandoned";
            await WriteTurnStateAsync(tempDirectory.FullName, pendingTurn);
            await WriteTurnStateAsync(tempDirectory.FullName, nonExactTurn);

            _ = await service.HandleStopAsync(
                CreateJsonStream(
                    CreateStopInput(tempDirectory.FullName, stopTimestamp),
                    AppJsonSerializerContext.Default.StopHookInput),
                new MemoryStream(),
                CancellationToken.None);

            Assert.Empty(handler.Requests);
            string notificationKey = CreateStopNotificationKeyForTest(stopTimestamp);
            Assert.False(File.Exists(AppPaths.GetSessionNotificationRecordPath(
                tempDirectory.FullName,
                "session-123",
                notificationKey)));
            foreach (NotificationTurn turn in new[] { pendingTurn, nonExactTurn })
            {
                Assert.False(File.Exists(AppPaths.GetNotificationRecordPath(
                    tempDirectory.FullName,
                    "session-123",
                    turn.NotificationTurnId,
                    notificationKey)));
            }

            await WriteSummaryAsync(
                tempDirectory.FullName,
                "session-123",
                pendingTurn,
                new NotificationSummary
                {
                    SessionId = "session-123",
                    NotificationTurnId = pendingTurn.NotificationTurnId,
                    NotificationNonce = pendingTurn.NotificationNonce,
                    UpdatedAt = stopTimestamp,
                    Status = "completed",
                    Summary = "The older abandoned exact summary completed after the pending deferral.",
                });

            _ = await service.HandleStopAsync(
                CreateJsonStream(
                    CreateStopInput(tempDirectory.FullName, stopTimestamp),
                    AppJsonSerializerContext.Default.StopHookInput),
                new MemoryStream(),
                CancellationToken.None);
            _ = await service.HandleStopAsync(
                CreateJsonStream(
                    CreateStopInput(tempDirectory.FullName, stopTimestamp),
                    AppJsonSerializerContext.Default.StopHookInput),
                new MemoryStream(),
                CancellationToken.None);

            TelegramSendMessageRequest payload = DeserializeTelegramPayload(
                Assert.Single(handler.Requests));
            Assert.Contains(
                "摘要：The older abandoned exact summary completed after the pending deferral.",
                payload.Text,
                StringComparison.Ordinal);
            Assert.Contains(pendingTurn.NotificationTurnId, payload.Text, StringComparison.Ordinal);
            Assert.DoesNotContain(nonExactTurn.NotificationTurnId, payload.Text, StringComparison.Ordinal);
            Assert.True(File.Exists(AppPaths.GetNotificationRecordPath(
                tempDirectory.FullName,
                "session-123",
                pendingTurn.NotificationTurnId,
                notificationKey)));
            Assert.False(File.Exists(AppPaths.GetNotificationRecordPath(
                tempDirectory.FullName,
                "session-123",
                nonExactTurn.NotificationTurnId,
                notificationKey)));
        }
        finally
        {
            tempDirectory.Delete(recursive: true);
        }
    }

    [Theory]
    [InlineData("missing")]
    [InlineData("corrupt")]
    public async Task HandleStopAsyncCachelessOlderExactPendingSuppressesLaterInvalidAndLaterDeliversOnce(
        string currentState)
    {
        DirectoryInfo tempDirectory = Directory.CreateTempSubdirectory();
        using EnvironmentScope environment = SetTelegramEnvironment();

        try
        {
            WorkspaceStateStore stateStore = new(
                TimeProvider.System,
                NullLogger<WorkspaceStateStore>.Instance);
            RecordingHttpMessageHandler handler = new();
            HookCommandService service = CreateHookCommandService(handler, stateStore);
            const string sessionId = "session-123";
            const string stopTimestamp = "2026-03-14T15:51:50.783Z";
            NotificationTurn pendingTurn = await CreateTurnAsync(
                stateStore,
                tempDirectory.FullName,
                sessionId,
                "2026-03-14T15:51:30.783Z");
            await WriteSummaryAsync(
                tempDirectory.FullName,
                sessionId,
                pendingTurn,
                new NotificationSummary
                {
                    SessionId = sessionId,
                    NotificationTurnId = pendingTurn.NotificationTurnId,
                    NotificationNonce = pendingTurn.NotificationNonce,
                    UpdatedAt = stopTimestamp,
                    Status = "pending",
                    Summary = " ",
                });

            _ = await service.HandleStopAsync(
                CreateJsonStream(
                    CreateStopInput(tempDirectory.FullName, stopTimestamp),
                    AppJsonSerializerContext.Default.StopHookInput),
                new MemoryStream(),
                CancellationToken.None);

            string notificationKey = CreateStopNotificationKeyForTest(stopTimestamp);
            Assert.Empty(handler.Requests);
            await AssertPendingStopAsync(
                stateStore,
                tempDirectory.FullName,
                pendingTurn,
                stopTimestamp,
                "summary must be a non-empty human-readable sentence");

            NotificationTurn invalidTurn = await CreateTurnAsync(
                stateStore,
                tempDirectory.FullName,
                sessionId,
                "2026-03-14T15:51:40.783Z");
            await WriteRawSummaryJsonAsync(
                tempDirectory.FullName,
                sessionId,
                invalidTurn,
                "{}");
            string currentPath = AppPaths.GetCurrentStatePath(tempDirectory.FullName, sessionId);
            if (string.Equals(currentState, "missing", StringComparison.Ordinal))
            {
                File.Delete(currentPath);
            }
            else
            {
                await File.WriteAllTextAsync(currentPath, "{");
            }

            _ = await service.HandleStopAsync(
                CreateJsonStream(
                    CreateStopInput(tempDirectory.FullName, stopTimestamp),
                    AppJsonSerializerContext.Default.StopHookInput),
                new MemoryStream(),
                CancellationToken.None);

            Assert.Empty(handler.Requests);
            Assert.False(File.Exists(AppPaths.GetSessionNotificationRecordPath(
                tempDirectory.FullName,
                sessionId,
                notificationKey)));
            Assert.False(File.Exists(AppPaths.GetNotificationRecordPath(
                tempDirectory.FullName,
                sessionId,
                invalidTurn.NotificationTurnId,
                notificationKey)));
            Assert.False(await WorkspaceStateStore.HasDurableDeliveryRecordAsync(
                tempDirectory.FullName,
                sessionId,
                invalidTurn.NotificationTurnId,
                CancellationToken.None));
            await WriteSummaryAsync(
                tempDirectory.FullName,
                sessionId,
                pendingTurn,
                new NotificationSummary
                {
                    SessionId = sessionId,
                    NotificationTurnId = pendingTurn.NotificationTurnId,
                    NotificationNonce = pendingTurn.NotificationNonce,
                    UpdatedAt = stopTimestamp,
                    Status = "completed",
                    Summary = "The cacheless older exact summary completed after pending.",
                });

            _ = await service.HandleStopAsync(
                CreateJsonStream(
                    CreateStopInput(tempDirectory.FullName, stopTimestamp),
                    AppJsonSerializerContext.Default.StopHookInput),
                new MemoryStream(),
                CancellationToken.None);
            _ = await service.HandleStopAsync(
                CreateJsonStream(
                    CreateStopInput(tempDirectory.FullName, stopTimestamp),
                    AppJsonSerializerContext.Default.StopHookInput),
                new MemoryStream(),
                CancellationToken.None);

            TelegramSendMessageRequest payload = DeserializeTelegramPayload(
                Assert.Single(handler.Requests));
            Assert.Contains(
                "摘要：The cacheless older exact summary completed after pending.",
                payload.Text,
                StringComparison.Ordinal);
            Assert.Contains(pendingTurn.NotificationTurnId, payload.Text, StringComparison.Ordinal);
            Assert.DoesNotContain(invalidTurn.NotificationTurnId, payload.Text, StringComparison.Ordinal);
            Assert.True(File.Exists(AppPaths.GetNotificationRecordPath(
                tempDirectory.FullName,
                sessionId,
                pendingTurn.NotificationTurnId,
                notificationKey)));
            Assert.False(File.Exists(AppPaths.GetNotificationRecordPath(
                tempDirectory.FullName,
                sessionId,
                invalidTurn.NotificationTurnId,
                notificationKey)));
        }
        finally
        {
            tempDirectory.Delete(recursive: true);
        }
    }

    [Theory]
    [InlineData("missing")]
    [InlineData("corrupt")]
    public async Task HandleStopAsyncCachelessOlderExactPendingSuppressesLaterValidAndLaterDeliversOnce(
        string currentState)
    {
        DirectoryInfo tempDirectory = Directory.CreateTempSubdirectory();
        using EnvironmentScope environment = SetTelegramEnvironment();

        try
        {
            WorkspaceStateStore stateStore = new(
                TimeProvider.System,
                NullLogger<WorkspaceStateStore>.Instance);
            RecordingHttpMessageHandler handler = new();
            HookCommandService service = CreateHookCommandService(handler, stateStore);
            const string sessionId = "session-123";
            const string stopTimestamp = "2026-03-14T15:51:55.783Z";
            NotificationTurn pendingTurn = await CreateTurnAsync(
                stateStore,
                tempDirectory.FullName,
                sessionId,
                "2026-03-14T15:51:30.783Z");
            await WriteSummaryAsync(
                tempDirectory.FullName,
                sessionId,
                pendingTurn,
                new NotificationSummary
                {
                    SessionId = sessionId,
                    NotificationTurnId = pendingTurn.NotificationTurnId,
                    NotificationNonce = pendingTurn.NotificationNonce,
                    UpdatedAt = stopTimestamp,
                    Status = "pending",
                    Summary = " ",
                });

            _ = await service.HandleStopAsync(
                CreateJsonStream(
                    CreateStopInput(tempDirectory.FullName, stopTimestamp),
                    AppJsonSerializerContext.Default.StopHookInput),
                new MemoryStream(),
                CancellationToken.None);

            string notificationKey = CreateStopNotificationKeyForTest(stopTimestamp);
            Assert.Empty(handler.Requests);
            await AssertPendingStopAsync(
                stateStore,
                tempDirectory.FullName,
                pendingTurn,
                stopTimestamp,
                "summary must be a non-empty human-readable sentence");

            NotificationTurn nonExactTurn = await CreateTurnAsync(
                stateStore,
                tempDirectory.FullName,
                sessionId,
                "2026-03-14T15:51:45.783Z");
            await WriteSummaryAsync(
                tempDirectory.FullName,
                sessionId,
                nonExactTurn,
                new NotificationSummary
                {
                    SessionId = sessionId,
                    NotificationTurnId = nonExactTurn.NotificationTurnId,
                    NotificationNonce = nonExactTurn.NotificationNonce,
                    UpdatedAt = "2026-03-14T15:51:45.783Z",
                    Status = "completed",
                    Summary = "The later non-exact summary must wait behind pending exact attribution.",
                });
            string currentPath = AppPaths.GetCurrentStatePath(tempDirectory.FullName, sessionId);
            if (string.Equals(currentState, "missing", StringComparison.Ordinal))
            {
                File.Delete(currentPath);
            }
            else
            {
                await File.WriteAllTextAsync(currentPath, "{");
            }

            _ = await service.HandleStopAsync(
                CreateJsonStream(
                    CreateStopInput(tempDirectory.FullName, stopTimestamp),
                    AppJsonSerializerContext.Default.StopHookInput),
                new MemoryStream(),
                CancellationToken.None);

            Assert.Empty(handler.Requests);
            Assert.False(File.Exists(AppPaths.GetNotificationRecordPath(
                tempDirectory.FullName,
                sessionId,
                nonExactTurn.NotificationTurnId,
                notificationKey)));
            Assert.False(await WorkspaceStateStore.HasDurableDeliveryRecordAsync(
                tempDirectory.FullName,
                sessionId,
                nonExactTurn.NotificationTurnId,
                CancellationToken.None));
            await WriteSummaryAsync(
                tempDirectory.FullName,
                sessionId,
                pendingTurn,
                new NotificationSummary
                {
                    SessionId = sessionId,
                    NotificationTurnId = pendingTurn.NotificationTurnId,
                    NotificationNonce = pendingTurn.NotificationNonce,
                    UpdatedAt = stopTimestamp,
                    Status = "completed",
                    Summary = "The cacheless older exact summary completed after valid non-exact.",
                });

            _ = await service.HandleStopAsync(
                CreateJsonStream(
                    CreateStopInput(tempDirectory.FullName, stopTimestamp),
                    AppJsonSerializerContext.Default.StopHookInput),
                new MemoryStream(),
                CancellationToken.None);
            _ = await service.HandleStopAsync(
                CreateJsonStream(
                    CreateStopInput(tempDirectory.FullName, stopTimestamp),
                    AppJsonSerializerContext.Default.StopHookInput),
                new MemoryStream(),
                CancellationToken.None);

            TelegramSendMessageRequest payload = DeserializeTelegramPayload(
                Assert.Single(handler.Requests));
            Assert.Contains(
                "摘要：The cacheless older exact summary completed after valid non-exact.",
                payload.Text,
                StringComparison.Ordinal);
            Assert.Contains(pendingTurn.NotificationTurnId, payload.Text, StringComparison.Ordinal);
            Assert.DoesNotContain(nonExactTurn.NotificationTurnId, payload.Text, StringComparison.Ordinal);
            Assert.True(File.Exists(AppPaths.GetNotificationRecordPath(
                tempDirectory.FullName,
                sessionId,
                pendingTurn.NotificationTurnId,
                notificationKey)));
            Assert.False(File.Exists(AppPaths.GetNotificationRecordPath(
                tempDirectory.FullName,
                sessionId,
                nonExactTurn.NotificationTurnId,
                notificationKey)));
        }
        finally
        {
            tempDirectory.Delete(recursive: true);
        }
    }

    [Theory]
    [InlineData("missing", "Summary file is missing")]
    [InlineData("invalid-json", "could not be parsed as JSON")]
    [InlineData("json-null", "is empty or does not contain a JSON object")]
    [InlineData("blank-assigned", "summary must be a non-empty human-readable sentence")]
    [InlineData("null-assigned", "summary must be a non-empty human-readable sentence")]
    public async Task HandleStopAsyncDefersPendingSummaryStatesAndLaterStopRetrySends(
        string pendingState,
        string expectedFailureReason)
    {
        DirectoryInfo tempDirectory = Directory.CreateTempSubdirectory();
        using EnvironmentScope environment = SetTelegramEnvironment();

        try
        {
            WorkspaceStateStore stateStore = new(
                TimeProvider.System,
                NullLogger<WorkspaceStateStore>.Instance);
            NotificationTurn turn = await CreateTurnAsync(
                stateStore,
                tempDirectory.FullName,
                "session-123");
            RecordingHttpMessageHandler handler = new();
            HookCommandService service = CreateHookCommandService(handler, stateStore);
            const string stopTimestamp = "2026-03-14T15:51:50.783Z";
            const string laterStopTimestamp = "2026-03-14T15:51:51.783Z";
            string summaryPath = AppPaths.GetSummaryStatePath(
                tempDirectory.FullName,
                "session-123",
                turn.NotificationTurnId);

            switch (pendingState)
            {
                case "missing":
                    File.Delete(summaryPath);
                    break;
                case "invalid-json":
                    await WriteRawSummaryJsonAsync(
                        tempDirectory.FullName,
                        "session-123",
                        turn,
                        "{");
                    break;
                case "json-null":
                    await WriteRawSummaryJsonAsync(
                        tempDirectory.FullName,
                        "session-123",
                        turn,
                        "null");
                    break;
                case "blank-assigned":
                case "null-assigned":
                    await WriteSummaryAsync(
                        tempDirectory.FullName,
                        "session-123",
                        turn,
                        new NotificationSummary
                        {
                            SessionId = "session-123",
                            NotificationTurnId = turn.NotificationTurnId,
                            NotificationNonce = turn.NotificationNonce,
                            UpdatedAt = stopTimestamp,
                            Status = "pending",
                            Summary = string.Equals(
                                pendingState,
                                "blank-assigned",
                                StringComparison.Ordinal)
                                ? " "
                                : null,
                        });
                    break;
            }

            _ = await service.HandleStopAsync(
                CreateJsonStream(
                    CreateStopInput(tempDirectory.FullName, stopTimestamp),
                    AppJsonSerializerContext.Default.StopHookInput),
                new MemoryStream(),
                CancellationToken.None);

            Assert.Empty(handler.Requests);
            await AssertPendingStopAsync(
                stateStore,
                tempDirectory.FullName,
                turn,
                stopTimestamp,
                expectedFailureReason);

            bool assignedPending = string.Equals(pendingState, "blank-assigned", StringComparison.Ordinal)
                || string.Equals(pendingState, "null-assigned", StringComparison.Ordinal);
            string completionStopTimestamp = assignedPending
                ? stopTimestamp
                : laterStopTimestamp;
            if (!assignedPending)
            {
                _ = await service.HandleStopAsync(
                    CreateJsonStream(
                        CreateStopInput(tempDirectory.FullName, laterStopTimestamp),
                        AppJsonSerializerContext.Default.StopHookInput),
                    new MemoryStream(),
                    CancellationToken.None);

                Assert.Empty(handler.Requests);
                await AssertPendingStopAsync(
                    stateStore,
                    tempDirectory.FullName,
                    turn,
                    laterStopTimestamp,
                    expectedFailureReason);
            }

            await WriteSummaryAsync(
                tempDirectory.FullName,
                "session-123",
                turn,
                new NotificationSummary
                {
                    SessionId = "session-123",
                    NotificationTurnId = turn.NotificationTurnId,
                    NotificationNonce = turn.NotificationNonce,
                    UpdatedAt = completionStopTimestamp,
                    Summary = "The pending summary state is complete.",
                });

            _ = await service.HandleStopAsync(
                CreateJsonStream(
                    CreateStopInput(tempDirectory.FullName, completionStopTimestamp),
                    AppJsonSerializerContext.Default.StopHookInput),
                new MemoryStream(),
                CancellationToken.None);

            TelegramSendMessageRequest payload = DeserializeTelegramPayload(
                Assert.Single(handler.Requests));
            Assert.Contains("摘要：The pending summary state is complete.", payload.Text, StringComparison.Ordinal);
            Assert.Contains(turn.NotificationTurnId, payload.Text, StringComparison.Ordinal);

            _ = await service.HandleStopAsync(
                CreateJsonStream(
                    CreateStopInput(tempDirectory.FullName, completionStopTimestamp),
                    AppJsonSerializerContext.Default.StopHookInput),
                new MemoryStream(),
                CancellationToken.None);

            Assert.Single(handler.Requests);
        }
        finally
        {
            tempDirectory.Delete(recursive: true);
        }
    }

    [Theory]
    [InlineData(" ")]
    [InlineData(null)]
    public async Task HandleStopAsyncTreatsBlankOrNullCompletedAssignedSummaryAsInvalidFallback(
        string? completedSummary)
    {
        DirectoryInfo tempDirectory = Directory.CreateTempSubdirectory();
        using EnvironmentScope environment = SetTelegramEnvironment();

        try
        {
            WorkspaceStateStore stateStore = new(
                TimeProvider.System,
                NullLogger<WorkspaceStateStore>.Instance);
            NotificationTurn turn = await CreateTurnAsync(
                stateStore,
                tempDirectory.FullName,
                "session-123");
            RecordingHttpMessageHandler handler = new();
            HookCommandService service = CreateHookCommandService(handler, stateStore);
            const string stopTimestamp = "2026-03-14T15:51:50.783Z";
            await WriteSummaryAsync(
                tempDirectory.FullName,
                "session-123",
                turn,
                new NotificationSummary
                {
                    SessionId = "session-123",
                    NotificationTurnId = turn.NotificationTurnId,
                    NotificationNonce = turn.NotificationNonce,
                    UpdatedAt = stopTimestamp,
                    Status = "completed",
                    Summary = completedSummary,
                });

            _ = await service.HandleStopAsync(
                CreateJsonStream(
                    CreateStopInput(tempDirectory.FullName, stopTimestamp),
                    AppJsonSerializerContext.Default.StopHookInput),
                new MemoryStream(),
                CancellationToken.None);

            TelegramSendMessageRequest payload = DeserializeTelegramPayload(
                Assert.Single(handler.Requests));
            Assert.Contains("摘要：当前轮未生成摘要。", payload.Text, StringComparison.Ordinal);
            Assert.Contains(turn.NotificationTurnId, payload.Text, StringComparison.Ordinal);

            string notificationKey = CreateStopNotificationKeyForTest(stopTimestamp);
            StopObservation observation = await ReadStopObservationAsync(
                AppPaths.GetStopObservationPath(
                    tempDirectory.FullName,
                    "session-123",
                    turn.NotificationTurnId,
                    notificationKey));
            Assert.False(observation.SummaryValid);
            Assert.False(observation.SummaryPendingHandoff);
            Assert.Contains(
                "summary must be a non-empty human-readable sentence",
                observation.SummaryFailureReason,
                StringComparison.Ordinal);
            Assert.True(File.Exists(AppPaths.GetNotificationRecordPath(
                tempDirectory.FullName,
                "session-123",
                turn.NotificationTurnId,
                notificationKey)));
        }
        finally
        {
            tempDirectory.Delete(recursive: true);
        }
    }

    [Fact]
    public async Task HandleStopAsyncDefersLockedSummaryAndRetrySendsAfterItCanBeRead()
    {
        DirectoryInfo tempDirectory = Directory.CreateTempSubdirectory();
        using EnvironmentScope environment = SetTelegramEnvironment();

        try
        {
            WorkspaceStateStore stateStore = new(
                TimeProvider.System,
                NullLogger<WorkspaceStateStore>.Instance);
            NotificationTurn turn = await CreateTurnAsync(
                stateStore,
                tempDirectory.FullName,
                "session-123");
            RecordingHttpMessageHandler handler = new();
            HookCommandService service = CreateHookCommandService(handler, stateStore);
            const string stopTimestamp = "2026-03-14T15:51:50.783Z";
            string summaryPath = AppPaths.GetSummaryStatePath(
                tempDirectory.FullName,
                "session-123",
                turn.NotificationTurnId);

            await using (FileStream lockedSummary = File.Open(
                             summaryPath,
                             FileMode.Open,
                             FileAccess.ReadWrite,
                             FileShare.None))
            {
                _ = await service.HandleStopAsync(
                    CreateJsonStream(
                        CreateStopInput(tempDirectory.FullName, stopTimestamp),
                        AppJsonSerializerContext.Default.StopHookInput),
                    new MemoryStream(),
                    CancellationToken.None);

                Assert.Empty(handler.Requests);
                await AssertPendingStopAsync(
                    stateStore,
                    tempDirectory.FullName,
                    turn,
                    stopTimestamp,
                    "could not be parsed as JSON");

                _ = await service.HandleStopAsync(
                    CreateJsonStream(
                        CreateStopInput(tempDirectory.FullName, stopTimestamp),
                        AppJsonSerializerContext.Default.StopHookInput),
                    new MemoryStream(),
                    CancellationToken.None);

                Assert.Empty(handler.Requests);
                await AssertPendingStopAsync(
                    stateStore,
                    tempDirectory.FullName,
                    turn,
                    stopTimestamp,
                    "could not be parsed as JSON");
            }

            Assert.Empty(handler.Requests);
            await AssertPendingStopAsync(
                stateStore,
                tempDirectory.FullName,
                turn,
                stopTimestamp,
                "could not be parsed as JSON");

            await WriteSummaryAsync(
                tempDirectory.FullName,
                "session-123",
                turn,
                new NotificationSummary
                {
                    SessionId = "session-123",
                    NotificationTurnId = turn.NotificationTurnId,
                    NotificationNonce = turn.NotificationNonce,
                    UpdatedAt = stopTimestamp,
                    Summary = "The locked summary can now be read.",
                });

            _ = await service.HandleStopAsync(
                CreateJsonStream(
                    CreateStopInput(tempDirectory.FullName, stopTimestamp),
                    AppJsonSerializerContext.Default.StopHookInput),
                new MemoryStream(),
                CancellationToken.None);

            TelegramSendMessageRequest payload = DeserializeTelegramPayload(
                Assert.Single(handler.Requests));
            Assert.Contains("摘要：The locked summary can now be read.", payload.Text, StringComparison.Ordinal);
        }
        finally
        {
            tempDirectory.Delete(recursive: true);
        }
    }

    [Fact]
    public async Task HandleStopAsyncSameTimestampPendingRetrySendsOnceAndReplaySuppresses()
    {
        DirectoryInfo tempDirectory = Directory.CreateTempSubdirectory();
        using EnvironmentScope environment = SetTelegramEnvironment();

        try
        {
            WorkspaceStateStore stateStore = new(
                TimeProvider.System,
                NullLogger<WorkspaceStateStore>.Instance);
            NotificationTurn turn = await CreateTurnAsync(
                stateStore,
                tempDirectory.FullName,
                "session-123");
            RecordingHttpMessageHandler handler = new();
            HookCommandService service = CreateHookCommandService(handler, stateStore);
            const string stopTimestamp = "2026-03-14T15:51:50.783Z";

            _ = await service.HandleStopAsync(
                CreateJsonStream(
                    CreateStopInput(tempDirectory.FullName, stopTimestamp),
                    AppJsonSerializerContext.Default.StopHookInput),
                new MemoryStream(),
                CancellationToken.None);

            Assert.Empty(handler.Requests);
            await AssertPendingStopAsync(
                stateStore,
                tempDirectory.FullName,
                turn,
                stopTimestamp,
                "summary must be a non-empty human-readable sentence");

            await WriteSummaryAsync(
                tempDirectory.FullName,
                "session-123",
                turn,
                new NotificationSummary
                {
                    SessionId = "session-123",
                    NotificationTurnId = turn.NotificationTurnId,
                    NotificationNonce = turn.NotificationNonce,
                    UpdatedAt = stopTimestamp,
                    Summary = "The original pending Stop retry now has a summary.",
                });

            _ = await service.HandleStopAsync(
                CreateJsonStream(
                    CreateStopInput(tempDirectory.FullName, stopTimestamp),
                    AppJsonSerializerContext.Default.StopHookInput),
                new MemoryStream(),
                CancellationToken.None);
            _ = await service.HandleStopAsync(
                CreateJsonStream(
                    CreateStopInput(tempDirectory.FullName, stopTimestamp),
                    AppJsonSerializerContext.Default.StopHookInput),
                new MemoryStream(),
                CancellationToken.None);

            TelegramSendMessageRequest payload = DeserializeTelegramPayload(
                Assert.Single(handler.Requests));
            Assert.Contains(
                "摘要：The original pending Stop retry now has a summary.",
                payload.Text,
                StringComparison.Ordinal);
        }
        finally
        {
            tempDirectory.Delete(recursive: true);
        }
    }

    [Fact]
    public async Task HandleStopAsyncPendingThenAbandonedExactSummaryRetrySendsOnceAndReplaySuppresses()
    {
        DirectoryInfo tempDirectory = Directory.CreateTempSubdirectory();
        using EnvironmentScope environment = SetTelegramEnvironment();

        try
        {
            WorkspaceStateStore stateStore = new(
                TimeProvider.System,
                NullLogger<WorkspaceStateStore>.Instance);
            NotificationTurn turn = await CreateTurnAsync(
                stateStore,
                tempDirectory.FullName,
                "session-123",
                "2026-03-14T15:51:40.783Z");
            RecordingHttpMessageHandler handler = new();
            HookCommandService service = CreateHookCommandService(handler, stateStore);
            const string stopTimestamp = "2026-03-14T15:51:50.783Z";

            _ = await service.HandleStopAsync(
                CreateJsonStream(
                    CreateStopInput(tempDirectory.FullName, stopTimestamp),
                    AppJsonSerializerContext.Default.StopHookInput),
                new MemoryStream(),
                CancellationToken.None);

            Assert.Empty(handler.Requests);
            await AssertPendingStopAsync(
                stateStore,
                tempDirectory.FullName,
                turn,
                stopTimestamp,
                "summary must be a non-empty human-readable sentence");

            _ = await service.HandleUserPromptSubmitAsync(
                CreateJsonStream(
                    new UserPromptSubmitHookInput
                    {
                        Cwd = tempDirectory.FullName,
                        SessionId = "session-123",
                        Timestamp = "2026-03-14T15:52:40.783Z",
                        TranscriptPath = "/workspace/transcript.json",
                        Prompt = "Supersede the pending turn before its summary completes.",
                    },
                    AppJsonSerializerContext.Default.UserPromptSubmitHookInput),
                new MemoryStream(),
                CancellationToken.None);

            NotificationTurn? abandonedTurn = await stateStore.TryReadTurnAsync(
                tempDirectory.FullName,
                "session-123",
                turn.NotificationTurnId,
                CancellationToken.None);
            Assert.Equal("abandoned", abandonedTurn?.Status);

            await WriteSummaryAsync(
                tempDirectory.FullName,
                "session-123",
                turn,
                new NotificationSummary
                {
                    SessionId = "session-123",
                    NotificationTurnId = turn.NotificationTurnId,
                    NotificationNonce = turn.NotificationNonce,
                    UpdatedAt = stopTimestamp,
                    Summary = "The abandoned pending Stop now has an exact completed summary.",
                });

            _ = await service.HandleStopAsync(
                CreateJsonStream(
                    CreateStopInput(tempDirectory.FullName, stopTimestamp),
                    AppJsonSerializerContext.Default.StopHookInput),
                new MemoryStream(),
                CancellationToken.None);
            _ = await service.HandleStopAsync(
                CreateJsonStream(
                    CreateStopInput(tempDirectory.FullName, stopTimestamp),
                    AppJsonSerializerContext.Default.StopHookInput),
                new MemoryStream(),
                CancellationToken.None);

            TelegramSendMessageRequest payload = DeserializeTelegramPayload(
                Assert.Single(handler.Requests));
            Assert.Contains(
                "摘要：The abandoned pending Stop now has an exact completed summary.",
                payload.Text,
                StringComparison.Ordinal);
            Assert.Contains(turn.NotificationTurnId, payload.Text, StringComparison.Ordinal);
        }
        finally
        {
            tempDirectory.Delete(recursive: true);
        }
    }

    [Fact]
    public async Task HandleStopAsyncAbandonedHookPlaceholderRetryDefersSessionFallback()
    {
        DirectoryInfo tempDirectory = Directory.CreateTempSubdirectory();
        using EnvironmentScope environment = SetTelegramEnvironment();

        try
        {
            WorkspaceStateStore stateStore = new(
                FixedUtcNow(),
                NullLogger<WorkspaceStateStore>.Instance);
            NotificationTurn turn = await CreateTurnAsync(
                stateStore,
                tempDirectory.FullName,
                "session-123",
                "2026-03-14T15:51:40.783Z");
            RecordingHttpMessageHandler handler = new();
            HookCommandService service = CreateHookCommandService(handler, stateStore);
            const string stopTimestamp = "2026-03-14T15:51:55.783Z";

            _ = await service.HandleStopAsync(
                CreateJsonStream(
                    CreateStopInput(tempDirectory.FullName, stopTimestamp),
                    AppJsonSerializerContext.Default.StopHookInput),
                new MemoryStream(),
                CancellationToken.None);

            Assert.Empty(handler.Requests);
            await AssertPendingStopAsync(
                stateStore,
                tempDirectory.FullName,
                turn,
                stopTimestamp,
                "summary must be a non-empty human-readable sentence");

            _ = await service.HandleUserPromptSubmitAsync(
                CreateJsonStream(
                    new UserPromptSubmitHookInput
                    {
                        Cwd = tempDirectory.FullName,
                        SessionId = "session-123",
                        Timestamp = "2026-03-14T15:52:40.783Z",
                        TranscriptPath = "/workspace/transcript.json",
                        Prompt = "Supersede the hook placeholder before exact summary completion.",
                    },
                    AppJsonSerializerContext.Default.UserPromptSubmitHookInput),
                new MemoryStream(),
                CancellationToken.None);

            _ = await service.HandleStopAsync(
                CreateJsonStream(
                    CreateStopInput(tempDirectory.FullName, stopTimestamp),
                    AppJsonSerializerContext.Default.StopHookInput),
                new MemoryStream(),
                CancellationToken.None);

            string notificationKey = CreateStopNotificationKeyForTest(stopTimestamp);
            Assert.Empty(handler.Requests);
            Assert.False(File.Exists(AppPaths.GetSessionNotificationRecordPath(
                tempDirectory.FullName,
                "session-123",
                notificationKey)));
            Assert.False(File.Exists(AppPaths.GetNotificationRecordPath(
                tempDirectory.FullName,
                "session-123",
                turn.NotificationTurnId,
                notificationKey)));
        }
        finally
        {
            tempDirectory.Delete(recursive: true);
        }
    }

    [Theory]
    [InlineData("missing", "Summary file is missing")]
    [InlineData("invalid-json", "could not be parsed as JSON")]
    [InlineData("json-null", "is empty or does not contain a JSON object")]
    [InlineData("locked", "could not be parsed as JSON")]
    public async Task HandleStopAsyncAbandonedPendingUnreadableSummaryRetryDefersUntilExactSummary(
        string pendingState,
        string expectedFailureReason)
    {
        DirectoryInfo tempDirectory = Directory.CreateTempSubdirectory();
        using EnvironmentScope environment = SetTelegramEnvironment();

        try
        {
            WorkspaceStateStore stateStore = new(
                TimeProvider.System,
                NullLogger<WorkspaceStateStore>.Instance);
            NotificationTurn turn = await CreateTurnAsync(
                stateStore,
                tempDirectory.FullName,
                "session-123",
                "2026-03-14T15:51:40.783Z");
            RecordingHttpMessageHandler handler = new();
            HookCommandService service = CreateHookCommandService(handler, stateStore);
            const string stopTimestamp = "2026-03-14T15:51:50.783Z";
            string summaryPath = AppPaths.GetSummaryStatePath(
                tempDirectory.FullName,
                "session-123",
                turn.NotificationTurnId);
            FileStream? lockedSummary = null;

            try
            {
                switch (pendingState)
                {
                    case "missing":
                        File.Delete(summaryPath);
                        break;
                    case "invalid-json":
                        await WriteRawSummaryJsonAsync(
                            tempDirectory.FullName,
                            "session-123",
                            turn,
                            "{");
                        break;
                    case "json-null":
                        await WriteRawSummaryJsonAsync(
                            tempDirectory.FullName,
                            "session-123",
                            turn,
                            "null");
                        break;
                    case "locked":
                        lockedSummary = File.Open(
                            summaryPath,
                            FileMode.Open,
                            FileAccess.ReadWrite,
                            FileShare.None);
                        break;
                }

                _ = await service.HandleStopAsync(
                    CreateJsonStream(
                        CreateStopInput(tempDirectory.FullName, stopTimestamp),
                        AppJsonSerializerContext.Default.StopHookInput),
                    new MemoryStream(),
                    CancellationToken.None);

                Assert.Empty(handler.Requests);
                await AssertPendingStopAsync(
                    stateStore,
                    tempDirectory.FullName,
                    turn,
                    stopTimestamp,
                    expectedFailureReason);

                _ = await service.HandleUserPromptSubmitAsync(
                    CreateJsonStream(
                        new UserPromptSubmitHookInput
                        {
                            Cwd = tempDirectory.FullName,
                            SessionId = "session-123",
                            Timestamp = "2026-03-14T15:52:40.783Z",
                            TranscriptPath = "/workspace/transcript.json",
                            Prompt = "Supersede the unreadable pending summary.",
                        },
                        AppJsonSerializerContext.Default.UserPromptSubmitHookInput),
                    new MemoryStream(),
                    CancellationToken.None);

                _ = await service.HandleStopAsync(
                    CreateJsonStream(
                        CreateStopInput(tempDirectory.FullName, stopTimestamp),
                        AppJsonSerializerContext.Default.StopHookInput),
                    new MemoryStream(),
                    CancellationToken.None);

                string notificationKey = CreateStopNotificationKeyForTest(stopTimestamp);
                Assert.Empty(handler.Requests);
                Assert.False(File.Exists(AppPaths.GetSessionNotificationRecordPath(
                    tempDirectory.FullName,
                    "session-123",
                    notificationKey)));
                Assert.False(File.Exists(AppPaths.GetNotificationRecordPath(
                    tempDirectory.FullName,
                    "session-123",
                    turn.NotificationTurnId,
                    notificationKey)));
                Assert.False(await WorkspaceStateStore.HasDurableDeliveryRecordAsync(
                    tempDirectory.FullName,
                    "session-123",
                    turn.NotificationTurnId,
                    CancellationToken.None));

                lockedSummary?.Dispose();
                lockedSummary = null;
                await WriteSummaryAsync(
                    tempDirectory.FullName,
                    "session-123",
                    turn,
                    new NotificationSummary
                    {
                        SessionId = "session-123",
                        NotificationTurnId = turn.NotificationTurnId,
                        NotificationNonce = turn.NotificationNonce,
                        UpdatedAt = stopTimestamp,
                        Summary = "The abandoned unreadable pending summary is now exact.",
                    });

                _ = await service.HandleStopAsync(
                    CreateJsonStream(
                        CreateStopInput(tempDirectory.FullName, stopTimestamp),
                        AppJsonSerializerContext.Default.StopHookInput),
                    new MemoryStream(),
                    CancellationToken.None);
                _ = await service.HandleStopAsync(
                    CreateJsonStream(
                        CreateStopInput(tempDirectory.FullName, stopTimestamp),
                        AppJsonSerializerContext.Default.StopHookInput),
                    new MemoryStream(),
                    CancellationToken.None);

                TelegramSendMessageRequest payload = DeserializeTelegramPayload(
                    Assert.Single(handler.Requests));
                Assert.Contains(
                    "摘要：The abandoned unreadable pending summary is now exact.",
                    payload.Text,
                    StringComparison.Ordinal);
                Assert.Contains(turn.NotificationTurnId, payload.Text, StringComparison.Ordinal);
                Assert.True(File.Exists(AppPaths.GetNotificationRecordPath(
                    tempDirectory.FullName,
                    "session-123",
                    turn.NotificationTurnId,
                    notificationKey)));
            }
            finally
            {
                lockedSummary?.Dispose();
            }
        }
        finally
        {
            tempDirectory.Delete(recursive: true);
        }
    }

    [Theory]
    [InlineData("missing", "Summary file is missing")]
    [InlineData("invalid-json", "could not be parsed as JSON")]
    [InlineData("json-null", "is empty or does not contain a JSON object")]
    [InlineData("locked", "could not be parsed as JSON")]
    public async Task HandleStopAsyncAbandonedUnreadableObservedDeferralDoesNotRecoverAfterInterveningDelivery(
        string pendingState,
        string expectedFailureReason)
    {
        DirectoryInfo tempDirectory = Directory.CreateTempSubdirectory();
        using EnvironmentScope environment = SetTelegramEnvironment();

        try
        {
            WorkspaceStateStore stateStore = new(
                TimeProvider.System,
                NullLogger<WorkspaceStateStore>.Instance);
            NotificationTurn oldTurn = await CreateTurnAsync(
                stateStore,
                tempDirectory.FullName,
                "session-123",
                "2026-03-14T15:51:40.783Z");
            RecordingHttpMessageHandler handler = new();
            HookCommandService service = CreateHookCommandService(handler, stateStore);
            const string stopTimestamp = "2026-03-14T15:53:50.783Z";
            string summaryPath = AppPaths.GetSummaryStatePath(
                tempDirectory.FullName,
                "session-123",
                oldTurn.NotificationTurnId);
            FileStream? lockedSummary = null;

            try
            {
                switch (pendingState)
                {
                    case "missing":
                        File.Delete(summaryPath);
                        break;
                    case "invalid-json":
                        await WriteRawSummaryJsonAsync(
                            tempDirectory.FullName,
                            "session-123",
                            oldTurn,
                            "{");
                        break;
                    case "json-null":
                        await WriteRawSummaryJsonAsync(
                            tempDirectory.FullName,
                            "session-123",
                            oldTurn,
                            "null");
                        break;
                    case "locked":
                        lockedSummary = File.Open(
                            summaryPath,
                            FileMode.Open,
                            FileAccess.ReadWrite,
                            FileShare.None);
                        break;
                }

                _ = await service.HandleStopAsync(
                    CreateJsonStream(
                        CreateStopInput(tempDirectory.FullName, stopTimestamp),
                        AppJsonSerializerContext.Default.StopHookInput),
                    new MemoryStream(),
                    CancellationToken.None);

                Assert.Empty(handler.Requests);
                await AssertPendingStopAsync(
                    stateStore,
                    tempDirectory.FullName,
                    oldTurn,
                    stopTimestamp,
                    expectedFailureReason);

                _ = await service.HandleUserPromptSubmitAsync(
                    CreateJsonStream(
                        new UserPromptSubmitHookInput
                        {
                            Cwd = tempDirectory.FullName,
                            SessionId = "session-123",
                            Timestamp = "2026-03-14T15:52:40.783Z",
                            TranscriptPath = "/workspace/transcript.json",
                            Prompt = "Supersede the unreadable pending summary before it becomes readable.",
                        },
                        AppJsonSerializerContext.Default.UserPromptSubmitHookInput),
                    new MemoryStream(),
                    CancellationToken.None);

                NotificationTurn? abandonedTurn = await stateStore.TryReadTurnAsync(
                    tempDirectory.FullName,
                    "session-123",
                    oldTurn.NotificationTurnId,
                    CancellationToken.None);
                Assert.Equal("abandoned", abandonedTurn?.Status);
                const string interveningStopTimestamp = "2026-03-14T15:52:50.783Z";
                string interveningNotificationKey = CreateStopNotificationKeyForTest(interveningStopTimestamp);
                await WriteNotificationRecordAsync(
                    AppPaths.GetSessionNotificationRecordPath(
                        tempDirectory.FullName,
                        "session-123",
                        interveningNotificationKey),
                    new NotificationRecord
                    {
                        SessionId = "session-123",
                        NotificationKey = interveningNotificationKey,
                        WorkspacePath = tempDirectory.FullName,
                        StopTimestamp = interveningStopTimestamp,
                        SentAt = "2026-03-14T15:52:51.783Z",
                        Degraded = true,
                        DeliveryStatus = "sent",
                    });

                lockedSummary?.Dispose();
                lockedSummary = null;
                await WriteSummaryAsync(
                    tempDirectory.FullName,
                    "session-123",
                    oldTurn,
                    new NotificationSummary
                    {
                        SessionId = "session-123",
                        NotificationTurnId = oldTurn.NotificationTurnId,
                        NotificationNonce = oldTurn.NotificationNonce,
                        UpdatedAt = stopTimestamp,
                        Summary = "The stale abandoned unreadable summary must not recover.",
                    });

                _ = await service.HandleStopAsync(
                    CreateJsonStream(
                        CreateStopInput(tempDirectory.FullName, stopTimestamp),
                        AppJsonSerializerContext.Default.StopHookInput),
                    new MemoryStream(),
                    CancellationToken.None);

                Assert.Empty(handler.Requests);
                string notificationKey = CreateStopNotificationKeyForTest(stopTimestamp);
                Assert.False(File.Exists(AppPaths.GetNotificationRecordPath(
                    tempDirectory.FullName,
                    "session-123",
                    oldTurn.NotificationTurnId,
                    notificationKey)));
                Assert.False(await WorkspaceStateStore.HasDurableDeliveryRecordAsync(
                    tempDirectory.FullName,
                    "session-123",
                    oldTurn.NotificationTurnId,
                    CancellationToken.None));
            }
            finally
            {
                lockedSummary?.Dispose();
            }
        }
        finally
        {
            tempDirectory.Delete(recursive: true);
        }
    }

    [Theory]
    [InlineData("missing")]
    [InlineData("invalid-json")]
    [InlineData("json-null")]
    [InlineData("locked")]
    public async Task HandleStopAsyncAbandonedUnreadableSummaryWithoutObservationDefersUntilExactSummary(
        string pendingState)
    {
        DirectoryInfo tempDirectory = Directory.CreateTempSubdirectory();
        using EnvironmentScope environment = SetTelegramEnvironment();

        try
        {
            WorkspaceStateStore stateStore = new(
                TimeProvider.System,
                NullLogger<WorkspaceStateStore>.Instance);
            NotificationTurn turn = await CreateTurnAsync(
                stateStore,
                tempDirectory.FullName,
                "session-123",
                "2026-03-14T15:51:40.783Z");
            RecordingHttpMessageHandler handler = new();
            HookCommandService service = CreateHookCommandService(handler, stateStore);
            const string stopTimestamp = "2026-03-14T15:51:50.783Z";
            string summaryPath = AppPaths.GetSummaryStatePath(
                tempDirectory.FullName,
                "session-123",
                turn.NotificationTurnId);
            FileStream? lockedSummary = null;

            try
            {
                switch (pendingState)
                {
                    case "missing":
                        File.Delete(summaryPath);
                        break;
                    case "invalid-json":
                        await WriteRawSummaryJsonAsync(
                            tempDirectory.FullName,
                            "session-123",
                            turn,
                            "{");
                        break;
                    case "json-null":
                        await WriteRawSummaryJsonAsync(
                            tempDirectory.FullName,
                            "session-123",
                            turn,
                            "null");
                        break;
                    case "locked":
                        lockedSummary = File.Open(
                            summaryPath,
                            FileMode.Open,
                            FileAccess.ReadWrite,
                            FileShare.None);
                        break;
                }

                _ = await service.HandleUserPromptSubmitAsync(
                    CreateJsonStream(
                        new UserPromptSubmitHookInput
                        {
                            Cwd = tempDirectory.FullName,
                            SessionId = "session-123",
                            Timestamp = "2026-03-14T15:52:40.783Z",
                            TranscriptPath = "/workspace/transcript.json",
                            Prompt = "Supersede the unreadable summary before Stop observes it.",
                        },
                        AppJsonSerializerContext.Default.UserPromptSubmitHookInput),
                    new MemoryStream(),
                    CancellationToken.None);

                NotificationTurn? abandonedTurn = await stateStore.TryReadTurnAsync(
                    tempDirectory.FullName,
                    "session-123",
                    turn.NotificationTurnId,
                    CancellationToken.None);
                Assert.Equal("abandoned", abandonedTurn?.Status);

                _ = await service.HandleStopAsync(
                    CreateJsonStream(
                        CreateStopInput(tempDirectory.FullName, stopTimestamp),
                        AppJsonSerializerContext.Default.StopHookInput),
                    new MemoryStream(),
                    CancellationToken.None);

                string notificationKey = CreateStopNotificationKeyForTest(stopTimestamp);
                Assert.Empty(handler.Requests);
                Assert.False(File.Exists(AppPaths.GetSessionNotificationRecordPath(
                    tempDirectory.FullName,
                    "session-123",
                    notificationKey)));
                Assert.False(File.Exists(AppPaths.GetNotificationRecordPath(
                    tempDirectory.FullName,
                    "session-123",
                    turn.NotificationTurnId,
                    notificationKey)));
                Assert.False(await WorkspaceStateStore.HasDurableDeliveryRecordAsync(
                    tempDirectory.FullName,
                    "session-123",
                    turn.NotificationTurnId,
                    CancellationToken.None));

                lockedSummary?.Dispose();
                lockedSummary = null;
                await WriteSummaryAsync(
                    tempDirectory.FullName,
                    "session-123",
                    turn,
                    new NotificationSummary
                    {
                        SessionId = "session-123",
                        NotificationTurnId = turn.NotificationTurnId,
                        NotificationNonce = turn.NotificationNonce,
                        UpdatedAt = stopTimestamp,
                        Summary = "The abandoned unreadable summary without observation is now exact.",
                    });

                _ = await service.HandleStopAsync(
                    CreateJsonStream(
                        CreateStopInput(tempDirectory.FullName, stopTimestamp),
                        AppJsonSerializerContext.Default.StopHookInput),
                    new MemoryStream(),
                    CancellationToken.None);
                _ = await service.HandleStopAsync(
                    CreateJsonStream(
                        CreateStopInput(tempDirectory.FullName, stopTimestamp),
                        AppJsonSerializerContext.Default.StopHookInput),
                    new MemoryStream(),
                    CancellationToken.None);

                TelegramSendMessageRequest payload = DeserializeTelegramPayload(
                    Assert.Single(handler.Requests));
                Assert.Contains(
                    "摘要：The abandoned unreadable summary without observation is now exact.",
                    payload.Text,
                    StringComparison.Ordinal);
                Assert.Contains(turn.NotificationTurnId, payload.Text, StringComparison.Ordinal);
                Assert.True(File.Exists(AppPaths.GetNotificationRecordPath(
                    tempDirectory.FullName,
                    "session-123",
                    turn.NotificationTurnId,
                    notificationKey)));
            }
            finally
            {
                lockedSummary?.Dispose();
            }
        }
        finally
        {
            tempDirectory.Delete(recursive: true);
        }
    }

    [Fact]
    public async Task HandleStopAsyncAbandonedPendingObservationWithOnlyHookPlaceholderDefersSessionFallback()
    {
        DirectoryInfo tempDirectory = Directory.CreateTempSubdirectory();
        using EnvironmentScope environment = SetTelegramEnvironment();

        try
        {
            WorkspaceStateStore stateStore = new(
                FixedUtcNow(),
                NullLogger<WorkspaceStateStore>.Instance);
            const string stopTimestamp = "2026-03-14T15:51:50.783Z";
            NotificationTurn turn = await CreateTurnAsync(
                stateStore,
                tempDirectory.FullName,
                "session-123",
                "2026-03-14T15:51:40.783Z");
            turn.Status = "abandoned";
            await WriteTurnStateAsync(tempDirectory.FullName, turn);
            string notificationKey = CreateStopNotificationKeyForTest(stopTimestamp);
            await WorkspaceStateStore.RecordStopObservationAsync(
                tempDirectory.FullName,
                turn,
                new StopObservation
                {
                    SessionId = "session-123",
                    NotificationTurnId = turn.NotificationTurnId,
                    StopId = notificationKey,
                    ObservedAt = stopTimestamp,
                    StopTimestamp = stopTimestamp,
                    MatchReason = "stale pending handoff",
                    SummaryValid = false,
                    SummaryPendingHandoff = true,
                    SummaryFailureReason = "summary must be a non-empty human-readable sentence",
                },
                CancellationToken.None);
            RecordingHttpMessageHandler handler = new();
            HookCommandService service = CreateHookCommandService(handler, stateStore);

            _ = await service.HandleStopAsync(
                CreateJsonStream(
                    CreateStopInput(tempDirectory.FullName, stopTimestamp),
                    AppJsonSerializerContext.Default.StopHookInput),
                new MemoryStream(),
                CancellationToken.None);
            _ = await service.HandleStopAsync(
                CreateJsonStream(
                    CreateStopInput(tempDirectory.FullName, stopTimestamp),
                    AppJsonSerializerContext.Default.StopHookInput),
                new MemoryStream(),
                CancellationToken.None);

            Assert.Empty(handler.Requests);
            Assert.False(File.Exists(AppPaths.GetSessionNotificationRecordPath(
                tempDirectory.FullName,
                "session-123",
                notificationKey)));
        }
        finally
        {
            tempDirectory.Delete(recursive: true);
        }
    }

    [Theory]
    [InlineData(null)]
    [InlineData("not-a-timestamp")]
    public async Task HandleStopAsyncAssignedBlankSummaryWithMissingOrInvalidUpdatedAtSendsFallback(
        string? updatedAt)
    {
        DirectoryInfo tempDirectory = Directory.CreateTempSubdirectory();
        using EnvironmentScope environment = SetTelegramEnvironment();

        try
        {
            WorkspaceStateStore stateStore = new(
                TimeProvider.System,
                NullLogger<WorkspaceStateStore>.Instance);
            NotificationTurn turn = await CreateTurnAsync(
                stateStore,
                tempDirectory.FullName,
                "session-123");
            await WriteSummaryAsync(
                tempDirectory.FullName,
                "session-123",
                turn,
                new NotificationSummary
                {
                    SessionId = "session-123",
                    NotificationTurnId = turn.NotificationTurnId,
                    NotificationNonce = turn.NotificationNonce,
                    UpdatedAt = updatedAt,
                    Status = "pending",
                    Summary = " ",
                });
            RecordingHttpMessageHandler handler = new();
            HookCommandService service = CreateHookCommandService(handler, stateStore);

            _ = await service.HandleStopAsync(
                CreateJsonStream(
                    CreateStopInput(tempDirectory.FullName),
                    AppJsonSerializerContext.Default.StopHookInput),
                new MemoryStream(),
                CancellationToken.None);

            TelegramSendMessageRequest payload = DeserializeTelegramPayload(
                Assert.Single(handler.Requests));
            Assert.Contains("摘要：当前轮未生成摘要。", payload.Text, StringComparison.Ordinal);
            NotificationTurn? storedTurn = await stateStore.TryReadTurnAsync(
                tempDirectory.FullName,
                "session-123",
                turn.NotificationTurnId,
                CancellationToken.None);
            Assert.Equal("notified", storedTurn?.Status);
        }
        finally
        {
            tempDirectory.Delete(recursive: true);
        }
    }

    [Theory]
    [InlineData(null)]
    [InlineData("not-a-timestamp")]
    public async Task HandleStopAsyncAssignedNonEmptySummaryWithMissingOrInvalidUpdatedAtSendsFallback(
        string? updatedAt)
    {
        DirectoryInfo tempDirectory = Directory.CreateTempSubdirectory();
        using EnvironmentScope environment = SetTelegramEnvironment();

        try
        {
            WorkspaceStateStore stateStore = new(
                TimeProvider.System,
                NullLogger<WorkspaceStateStore>.Instance);
            NotificationTurn turn = await CreateTurnAsync(
                stateStore,
                tempDirectory.FullName,
                "session-123");
            await WriteSummaryAsync(
                tempDirectory.FullName,
                "session-123",
                turn,
                new NotificationSummary
                {
                    SessionId = "session-123",
                    NotificationTurnId = turn.NotificationTurnId,
                    NotificationNonce = turn.NotificationNonce,
                    UpdatedAt = updatedAt,
                    Summary = "A non-empty summary still needs a valid updated_at.",
                });
            RecordingHttpMessageHandler handler = new();
            HookCommandService service = CreateHookCommandService(handler, stateStore);
            const string stopTimestamp = "2026-03-14T15:51:50.783Z";

            _ = await service.HandleStopAsync(
                CreateJsonStream(
                    CreateStopInput(tempDirectory.FullName, stopTimestamp),
                    AppJsonSerializerContext.Default.StopHookInput),
                new MemoryStream(),
                CancellationToken.None);

            TelegramSendMessageRequest payload = DeserializeTelegramPayload(
                Assert.Single(handler.Requests));
            Assert.Contains("摘要：当前轮未生成摘要。", payload.Text, StringComparison.Ordinal);
            StopObservation observation = await ReadStopObservationAsync(
                AppPaths.GetStopObservationPath(
                    tempDirectory.FullName,
                    "session-123",
                    turn.NotificationTurnId,
                    CreateStopNotificationKeyForTest(stopTimestamp)));
            Assert.False(observation.SummaryValid);
            Assert.False(observation.SummaryPendingHandoff);
        }
        finally
        {
            tempDirectory.Delete(recursive: true);
        }
    }

    [Fact]
    public async Task HandleStopAsyncEmptySummaryObjectSendsFallback()
    {
        DirectoryInfo tempDirectory = Directory.CreateTempSubdirectory();
        using EnvironmentScope environment = SetTelegramEnvironment();

        try
        {
            WorkspaceStateStore stateStore = new(
                TimeProvider.System,
                NullLogger<WorkspaceStateStore>.Instance);
            NotificationTurn turn = await CreateTurnAsync(
                stateStore,
                tempDirectory.FullName,
                "session-123");
            string summaryPath = AppPaths.GetSummaryStatePath(
                tempDirectory.FullName,
                "session-123",
                turn.NotificationTurnId);
            await File.WriteAllTextAsync(summaryPath, "{}");
            RecordingHttpMessageHandler handler = new();
            HookCommandService service = CreateHookCommandService(handler, stateStore);
            const string stopTimestamp = "2026-03-14T15:51:50.783Z";

            _ = await service.HandleStopAsync(
                CreateJsonStream(
                    CreateStopInput(tempDirectory.FullName, stopTimestamp),
                    AppJsonSerializerContext.Default.StopHookInput),
                new MemoryStream(),
                CancellationToken.None);

            TelegramSendMessageRequest payload = DeserializeTelegramPayload(
                Assert.Single(handler.Requests));
            Assert.Contains("摘要：当前轮未生成摘要。", payload.Text, StringComparison.Ordinal);
            NotificationTurn? storedTurn = await stateStore.TryReadTurnAsync(
                tempDirectory.FullName,
                "session-123",
                turn.NotificationTurnId,
                CancellationToken.None);
            Assert.Equal("notified", storedTurn?.Status);
            StopObservation observation = await ReadStopObservationAsync(
                AppPaths.GetStopObservationPath(
                    tempDirectory.FullName,
                    "session-123",
                    turn.NotificationTurnId,
                    CreateStopNotificationKeyForTest(stopTimestamp)));
            Assert.False(observation.SummaryValid);
            Assert.False(observation.SummaryPendingHandoff);
        }
        finally
        {
            tempDirectory.Delete(recursive: true);
        }
    }

    [Fact]
    public async Task HandleStopAsyncAssignedBlankSummaryWithValidUpdatedAtDefers()
    {
        DirectoryInfo tempDirectory = Directory.CreateTempSubdirectory();
        using EnvironmentScope environment = SetTelegramEnvironment();

        try
        {
            WorkspaceStateStore stateStore = new(
                TimeProvider.System,
                NullLogger<WorkspaceStateStore>.Instance);
            NotificationTurn turn = await CreateTurnAsync(
                stateStore,
                tempDirectory.FullName,
                "session-123");
            const string stopTimestamp = "2026-03-14T15:51:50.783Z";
            await WriteSummaryAsync(
                tempDirectory.FullName,
                "session-123",
                turn,
                new NotificationSummary
                {
                    SessionId = "session-123",
                    NotificationTurnId = turn.NotificationTurnId,
                    NotificationNonce = turn.NotificationNonce,
                    UpdatedAt = stopTimestamp,
                    Status = "pending",
                    Summary = " ",
                });
            RecordingHttpMessageHandler handler = new();
            HookCommandService service = CreateHookCommandService(handler, stateStore);

            _ = await service.HandleStopAsync(
                CreateJsonStream(
                    CreateStopInput(tempDirectory.FullName, stopTimestamp),
                    AppJsonSerializerContext.Default.StopHookInput),
                new MemoryStream(),
                CancellationToken.None);

            Assert.Empty(handler.Requests);
            await AssertPendingStopAsync(
                stateStore,
                tempDirectory.FullName,
                turn,
                stopTimestamp,
                "summary must be a non-empty human-readable sentence");
        }
        finally
        {
            tempDirectory.Delete(recursive: true);
        }
    }

    [Fact]
    public async Task HandleStopAsyncAssignedBlankSummaryForDifferentStopSendsFallback()
    {
        DirectoryInfo tempDirectory = Directory.CreateTempSubdirectory();
        using EnvironmentScope environment = SetTelegramEnvironment();

        try
        {
            WorkspaceStateStore stateStore = new(
                TimeProvider.System,
                NullLogger<WorkspaceStateStore>.Instance);
            NotificationTurn turn = await CreateTurnAsync(
                stateStore,
                tempDirectory.FullName,
                "session-123");
            const string stopTimestamp = "2026-03-14T15:51:50.783Z";
            await WriteSummaryAsync(
                tempDirectory.FullName,
                "session-123",
                turn,
                new NotificationSummary
                {
                    SessionId = "session-123",
                    NotificationTurnId = turn.NotificationTurnId,
                    NotificationNonce = turn.NotificationNonce,
                    UpdatedAt = "2026-03-14T15:51:51.783Z",
                    Status = "pending",
                    Summary = " ",
                });
            RecordingHttpMessageHandler handler = new();
            HookCommandService service = CreateHookCommandService(handler, stateStore);

            _ = await service.HandleStopAsync(
                CreateJsonStream(
                    CreateStopInput(tempDirectory.FullName, stopTimestamp),
                    AppJsonSerializerContext.Default.StopHookInput),
                new MemoryStream(),
                CancellationToken.None);

            TelegramSendMessageRequest payload = DeserializeTelegramPayload(
                Assert.Single(handler.Requests));
            Assert.Contains("摘要：当前轮未生成摘要。", payload.Text, StringComparison.Ordinal);
            Assert.True(File.Exists(AppPaths.GetNotificationRecordPath(
                tempDirectory.FullName,
                "session-123",
                turn.NotificationTurnId,
                CreateStopNotificationKeyForTest(stopTimestamp))));
        }
        finally
        {
            tempDirectory.Delete(recursive: true);
        }
    }

    [Theory]
    [InlineData("session_id", " ")]
    [InlineData("notification_turn_id", null)]
    [InlineData("notification_nonce", "")]
    public async Task HandleStopAsyncMismatchedAssignedBlankSummarySendsFallback(
        string mismatchedField,
        string? summaryText)
    {
        DirectoryInfo tempDirectory = Directory.CreateTempSubdirectory();
        using EnvironmentScope environment = SetTelegramEnvironment();

        try
        {
            WorkspaceStateStore stateStore = new(
                TimeProvider.System,
                NullLogger<WorkspaceStateStore>.Instance);
            NotificationTurn turn = await CreateTurnAsync(
                stateStore,
                tempDirectory.FullName,
                "session-123");
            const string stopTimestamp = "2026-03-14T15:51:50.783Z";
            await WriteSummaryAsync(
                tempDirectory.FullName,
                "session-123",
                turn,
                new NotificationSummary
                {
                    SessionId = mismatchedField == "session_id"
                        ? "another-session"
                        : "session-123",
                    NotificationTurnId = mismatchedField == "notification_turn_id"
                        ? "another-turn"
                        : turn.NotificationTurnId,
                    NotificationNonce = mismatchedField == "notification_nonce"
                        ? "another-nonce"
                        : turn.NotificationNonce,
                    UpdatedAt = stopTimestamp,
                    Summary = summaryText,
                });
            RecordingHttpMessageHandler handler = new();
            HookCommandService service = CreateHookCommandService(handler, stateStore);

            _ = await service.HandleStopAsync(
                CreateJsonStream(
                    CreateStopInput(tempDirectory.FullName, stopTimestamp),
                    AppJsonSerializerContext.Default.StopHookInput),
                new MemoryStream(),
                CancellationToken.None);

            TelegramSendMessageRequest payload = DeserializeTelegramPayload(
                Assert.Single(handler.Requests));
            Assert.Contains("摘要：当前轮未生成摘要。", payload.Text, StringComparison.Ordinal);
            NotificationTurn? storedTurn = await stateStore.TryReadTurnAsync(
                tempDirectory.FullName,
                "session-123",
                turn.NotificationTurnId,
                CancellationToken.None);
            Assert.Equal("notified", storedTurn?.Status);
        }
        finally
        {
            tempDirectory.Delete(recursive: true);
        }
    }

    [Fact]
    public async Task HandleUserPromptSubmitAsyncAbandonsOldPendingTurnBeforeNewTurnStop()
    {
        DirectoryInfo tempDirectory = Directory.CreateTempSubdirectory();
        using EnvironmentScope environment = SetTelegramEnvironment();

        try
        {
            WorkspaceStateStore stateStore = new(
                TimeProvider.System,
                NullLogger<WorkspaceStateStore>.Instance);
            NotificationTurn oldTurn = await CreateTurnAsync(
                stateStore,
                tempDirectory.FullName,
                "session-123",
                "2026-03-14T15:51:40.783Z");
            RecordingHttpMessageHandler handler = new();
            HookCommandService service = CreateHookCommandService(handler, stateStore);

            _ = await service.HandleStopAsync(
                CreateJsonStream(
                    CreateStopInput(tempDirectory.FullName, "2026-03-14T15:51:50.783Z"),
                    AppJsonSerializerContext.Default.StopHookInput),
                new MemoryStream(),
                CancellationToken.None);

            Assert.Empty(handler.Requests);
            await AssertPendingStopAsync(
                stateStore,
                tempDirectory.FullName,
                oldTurn,
                "2026-03-14T15:51:50.783Z",
                "summary must be a non-empty human-readable sentence");

            _ = await service.HandleUserPromptSubmitAsync(
                CreateJsonStream(
                    new UserPromptSubmitHookInput
                    {
                        Cwd = tempDirectory.FullName,
                        SessionId = "session-123",
                        Timestamp = "2026-03-14T15:52:40.783Z",
                        TranscriptPath = "/workspace/transcript.json",
                        Prompt = "Ship the follow-up change.",
                    },
                    AppJsonSerializerContext.Default.UserPromptSubmitHookInput),
                new MemoryStream(),
                CancellationToken.None);

            NotificationTurn? abandonedOldTurn = await stateStore.TryReadTurnAsync(
                tempDirectory.FullName,
                "session-123",
                oldTurn.NotificationTurnId,
                CancellationToken.None);
            Assert.Equal("abandoned", abandonedOldTurn?.Status);
            NotificationTurn newTurn = Assert.Single(
                await stateStore.ListOpenTurnsAsync(
                    tempDirectory.FullName,
                    "session-123",
                    CancellationToken.None),
                turn => !string.Equals(
                    turn.NotificationTurnId,
                    oldTurn.NotificationTurnId,
                    StringComparison.Ordinal));

            _ = await service.HandleStopAsync(
                CreateJsonStream(
                    CreateStopInput(tempDirectory.FullName, "2026-03-14T15:51:50.783Z"),
                    AppJsonSerializerContext.Default.StopHookInput),
                new MemoryStream(),
                CancellationToken.None);

            string oldNotificationKey = CreateStopNotificationKeyForTest("2026-03-14T15:51:50.783Z");
            Assert.Empty(handler.Requests);
            Assert.False(File.Exists(AppPaths.GetSessionNotificationRecordPath(
                tempDirectory.FullName,
                "session-123",
                oldNotificationKey)));
            Assert.False(File.Exists(AppPaths.GetNotificationRecordPath(
                tempDirectory.FullName,
                "session-123",
                oldTurn.NotificationTurnId,
                oldNotificationKey)));

            const string newStopTimestamp = "2026-03-14T15:52:50.783Z";
            await WriteSummaryAsync(
                tempDirectory.FullName,
                "session-123",
                newTurn,
                new NotificationSummary
                {
                    SessionId = "session-123",
                    NotificationTurnId = newTurn.NotificationTurnId,
                    NotificationNonce = newTurn.NotificationNonce,
                    UpdatedAt = newStopTimestamp,
                    Summary = "The follow-up turn completed after abandoning the old pending turn.",
                });

            _ = await service.HandleStopAsync(
                CreateJsonStream(
                    CreateStopInput(tempDirectory.FullName, newStopTimestamp),
                    AppJsonSerializerContext.Default.StopHookInput),
                new MemoryStream(),
                CancellationToken.None);

            TelegramSendMessageRequest payload = DeserializeTelegramPayload(
                Assert.Single(handler.Requests));
            Assert.Contains(
                "摘要：The follow-up turn completed after abandoning the old pending turn.",
                payload.Text,
                StringComparison.Ordinal);
            Assert.Contains(newTurn.NotificationTurnId, payload.Text, StringComparison.Ordinal);
            Assert.DoesNotContain(oldTurn.NotificationTurnId, payload.Text, StringComparison.Ordinal);
        }
        finally
        {
            tempDirectory.Delete(recursive: true);
        }
    }

    [Fact]
    public async Task HandleUserPromptSubmitAsyncPreservesObservedExactPendingWhenSupersedingCreatedAtMatchesSummaryUpdatedAt()
    {
        DirectoryInfo tempDirectory = Directory.CreateTempSubdirectory();
        using EnvironmentScope environment = SetTelegramEnvironment();

        try
        {
            WorkspaceStateStore stateStore = new(
                TimeProvider.System,
                NullLogger<WorkspaceStateStore>.Instance);
            RecordingHttpMessageHandler handler = new();
            HookCommandService service = CreateHookCommandService(handler, stateStore);
            const string stopTimestamp = "2026-03-14T15:51:50.783Z";
            NotificationTurn oldTurn = await CreateTurnAsync(
                stateStore,
                tempDirectory.FullName,
                "session-123",
                "2026-03-14T15:51:40.783Z");
            await WriteSummaryAsync(
                tempDirectory.FullName,
                "session-123",
                oldTurn,
                new NotificationSummary
                {
                    SessionId = "session-123",
                    NotificationTurnId = oldTurn.NotificationTurnId,
                    NotificationNonce = oldTurn.NotificationNonce,
                    UpdatedAt = stopTimestamp,
                    Status = "pending",
                    Summary = string.Empty,
                });

            _ = await service.HandleStopAsync(
                CreateJsonStream(
                    CreateStopInput(tempDirectory.FullName, stopTimestamp),
                    AppJsonSerializerContext.Default.StopHookInput),
                new MemoryStream(),
                CancellationToken.None);

            Assert.Empty(handler.Requests);
            await AssertPendingStopAsync(
                stateStore,
                tempDirectory.FullName,
                oldTurn,
                stopTimestamp,
                "summary must be a non-empty human-readable sentence");

            _ = await service.HandleUserPromptSubmitAsync(
                CreateJsonStream(
                    new UserPromptSubmitHookInput
                    {
                        Cwd = tempDirectory.FullName,
                        SessionId = "session-123",
                        Timestamp = stopTimestamp,
                        TranscriptPath = "/workspace/transcript.json",
                        Prompt = "Start the follow-up exactly when the old pending summary was updated.",
                    },
                    AppJsonSerializerContext.Default.UserPromptSubmitHookInput),
                new MemoryStream(),
                CancellationToken.None);

            NotificationTurn? preservedOldTurn = await stateStore.TryReadTurnAsync(
                tempDirectory.FullName,
                "session-123",
                oldTurn.NotificationTurnId,
                CancellationToken.None);
            Assert.Equal("open", preservedOldTurn?.Status);
            NotificationTurn newTurn = Assert.Single(
                await stateStore.ListOpenTurnsAsync(
                    tempDirectory.FullName,
                    "session-123",
                    CancellationToken.None),
                turn => !string.Equals(
                    turn.NotificationTurnId,
                    oldTurn.NotificationTurnId,
                    StringComparison.Ordinal));

            await WriteSummaryAsync(
                tempDirectory.FullName,
                "session-123",
                oldTurn,
                new NotificationSummary
                {
                    SessionId = "session-123",
                    NotificationTurnId = oldTurn.NotificationTurnId,
                    NotificationNonce = oldTurn.NotificationNonce,
                    UpdatedAt = stopTimestamp,
                    Summary = "The observed exact pending summary survives the matching superseding timestamp.",
                });

            _ = await service.HandleStopAsync(
                CreateJsonStream(
                    CreateStopInput(tempDirectory.FullName, stopTimestamp),
                    AppJsonSerializerContext.Default.StopHookInput),
                new MemoryStream(),
                CancellationToken.None);
            _ = await service.HandleStopAsync(
                CreateJsonStream(
                    CreateStopInput(tempDirectory.FullName, stopTimestamp),
                    AppJsonSerializerContext.Default.StopHookInput),
                new MemoryStream(),
                CancellationToken.None);

            TelegramSendMessageRequest payload = DeserializeTelegramPayload(
                Assert.Single(handler.Requests));
            Assert.Contains(
                "摘要：The observed exact pending summary survives the matching superseding timestamp.",
                payload.Text,
                StringComparison.Ordinal);
            Assert.Contains(oldTurn.NotificationTurnId, payload.Text, StringComparison.Ordinal);
            Assert.DoesNotContain(newTurn.NotificationTurnId, payload.Text, StringComparison.Ordinal);
        }
        finally
        {
            tempDirectory.Delete(recursive: true);
        }
    }

    [Fact]
    public async Task HandleUserPromptSubmitAsyncDelayedOlderPromptDoesNotAbandonNewerPendingTurn()
    {
        DirectoryInfo tempDirectory = Directory.CreateTempSubdirectory();
        using EnvironmentScope environment = SetTelegramEnvironment();

        try
        {
            WorkspaceStateStore stateStore = new(
                TimeProvider.System,
                NullLogger<WorkspaceStateStore>.Instance);
            RecordingHttpMessageHandler handler = new();
            HookCommandService service = CreateHookCommandService(handler, stateStore);
            _ = await service.HandleUserPromptSubmitAsync(
                CreateJsonStream(
                    new UserPromptSubmitHookInput
                    {
                        Cwd = tempDirectory.FullName,
                        SessionId = "session-123",
                        Timestamp = "2026-03-14T15:52:40.783Z",
                        TranscriptPath = "/workspace/transcript.json",
                        Prompt = "Start the newer current turn.",
                    },
                    AppJsonSerializerContext.Default.UserPromptSubmitHookInput),
                new MemoryStream(),
                CancellationToken.None);
            NotificationTurn newerTurn = Assert.Single(await stateStore.ListOpenTurnsAsync(
                tempDirectory.FullName,
                "session-123",
                CancellationToken.None));
            const string newerStopTimestamp = "2026-03-14T15:52:50.783Z";
            await WriteSummaryAsync(
                tempDirectory.FullName,
                "session-123",
                newerTurn,
                new NotificationSummary
                {
                    SessionId = "session-123",
                    NotificationTurnId = newerTurn.NotificationTurnId,
                    NotificationNonce = newerTurn.NotificationNonce,
                    UpdatedAt = newerStopTimestamp,
                    Status = "pending",
                    Summary = " ",
                });

            _ = await service.HandleUserPromptSubmitAsync(
                CreateJsonStream(
                    new UserPromptSubmitHookInput
                    {
                        Cwd = tempDirectory.FullName,
                        SessionId = "session-123",
                        Timestamp = "2026-03-14T15:51:40.783Z",
                        TranscriptPath = "/workspace/transcript.json",
                        Prompt = "This delayed older prompt must not abandon the newer pending turn.",
                    },
                    AppJsonSerializerContext.Default.UserPromptSubmitHookInput),
                new MemoryStream(),
                CancellationToken.None);

            NotificationTurn? storedNewerTurn = await stateStore.TryReadTurnAsync(
                tempDirectory.FullName,
                "session-123",
                newerTurn.NotificationTurnId,
                CancellationToken.None);
            Assert.Equal("open", storedNewerTurn?.Status);
            CurrentNotificationState? current = await stateStore.TryReadCurrentAsync(
                tempDirectory.FullName,
                "session-123",
                CancellationToken.None);
            Assert.Equal(newerTurn.NotificationTurnId, current?.NotificationTurnId);
            Assert.Contains(
                await stateStore.ListOpenTurnsAsync(tempDirectory.FullName, "session-123", CancellationToken.None),
                turn => string.Equals(
                    turn.NotificationTurnId,
                    newerTurn.NotificationTurnId,
                    StringComparison.Ordinal));

            _ = await service.HandleStopAsync(
                CreateJsonStream(
                    CreateStopInput(tempDirectory.FullName, newerStopTimestamp),
                    AppJsonSerializerContext.Default.StopHookInput),
                new MemoryStream(),
                CancellationToken.None);

            Assert.Empty(handler.Requests);
            await AssertPendingStopAsync(
                stateStore,
                tempDirectory.FullName,
                newerTurn,
                newerStopTimestamp,
                "summary must be a non-empty human-readable sentence");
        }
        finally
        {
            tempDirectory.Delete(recursive: true);
        }
    }

    [Fact]
    public async Task HandleUserPromptSubmitAsyncKeepsOpenTurnWhenTargetIsDeliveredBeforeAbandonWrite()
    {
        DirectoryInfo tempDirectory = Directory.CreateTempSubdirectory();
        using EnvironmentScope environment = SetTelegramEnvironment();

        try
        {
            WorkspaceStateStore stateStore = new(
                TimeProvider.System,
                NullLogger<WorkspaceStateStore>.Instance);
            HookCommandService service = CreateHookCommandService(new RecordingHttpMessageHandler(), stateStore);
            NotificationTurn oldTurn = await CreateTurnAsync(
                stateStore,
                tempDirectory.FullName,
                "session-123",
                "2026-03-14T15:51:40.783Z");
            oldTurn.Status = "open";
            await WriteTurnStateAsync(tempDirectory.FullName, oldTurn);
            const string stopTimestamp = "2026-03-14T15:51:50.783Z";
            string notificationKey = CreateStopNotificationKeyForTest(stopTimestamp);
            stateStore.OnBeforeAbandonOpenTurnForTestingAsync =
                async (currentTurn, _) =>
                    await RecordSentNotificationAsync(
                        tempDirectory.FullName,
                        currentTurn,
                        notificationKey,
                        stopTimestamp);

            _ = await service.HandleUserPromptSubmitAsync(
                CreateJsonStream(
                    new UserPromptSubmitHookInput
                    {
                        Cwd = tempDirectory.FullName,
                        SessionId = "session-123",
                        Timestamp = "2026-03-14T15:52:40.783Z",
                        TranscriptPath = "/workspace/transcript.json",
                        Prompt = "This prompt must not abandon a turn that just delivered.",
                    },
                    AppJsonSerializerContext.Default.UserPromptSubmitHookInput),
                new MemoryStream(),
                CancellationToken.None);

            NotificationTurn? storedOldTurn = await stateStore.TryReadTurnAsync(
                tempDirectory.FullName,
                "session-123",
                oldTurn.NotificationTurnId,
                CancellationToken.None);
            Assert.Equal("open", storedOldTurn?.Status);
            Assert.True(await WorkspaceStateStore.HasDurableDeliveryRecordAsync(
                tempDirectory.FullName,
                "session-123",
                oldTurn.NotificationTurnId,
                CancellationToken.None));
        }
        finally
        {
            tempDirectory.Delete(recursive: true);
        }
    }

    [Fact]
    public async Task HandleUserPromptSubmitAsyncEqualCreatedAtPreservesExistingCurrentAndPeers()
    {
        DirectoryInfo tempDirectory = Directory.CreateTempSubdirectory();
        using EnvironmentScope environment = SetTelegramEnvironment();

        try
        {
            WorkspaceStateStore stateStore = new(
                TimeProvider.System,
                NullLogger<WorkspaceStateStore>.Instance);
            HookCommandService service = CreateHookCommandService(
                new RecordingHttpMessageHandler(),
                stateStore);
            const string sharedTimestamp = "2026-03-14T15:51:40.783Z";
            NotificationTurn existingTurn = await CreateTurnAsync(
                stateStore,
                tempDirectory.FullName,
                "session-123",
                sharedTimestamp);

            _ = await service.HandleUserPromptSubmitAsync(
                CreateJsonStream(
                    new UserPromptSubmitHookInput
                    {
                        Cwd = tempDirectory.FullName,
                        SessionId = "session-123",
                        Timestamp = sharedTimestamp,
                        TranscriptPath = "/workspace/transcript.json",
                        Prompt = "Ship the equal-created follow-up.",
                    },
                    AppJsonSerializerContext.Default.UserPromptSubmitHookInput),
                new MemoryStream(),
                CancellationToken.None);

            NotificationTurn[] openTurns = (await stateStore.ListOpenTurnsAsync(
                    tempDirectory.FullName,
                    "session-123",
                    CancellationToken.None))
                .OrderBy(static turn => turn.NotificationTurnId, StringComparer.Ordinal)
                .ToArray();
            Assert.Equal(2, openTurns.Length);
            Assert.All(openTurns, turn => Assert.Equal(sharedTimestamp, turn.CreatedAt));
            Assert.Contains(
                openTurns,
                turn => string.Equals(
                    existingTurn.NotificationTurnId,
                    turn.NotificationTurnId,
                    StringComparison.Ordinal));
            CurrentNotificationState? current = await stateStore.TryReadCurrentAsync(
                tempDirectory.FullName,
                "session-123",
                CancellationToken.None);
            Assert.Equal(existingTurn.NotificationTurnId, current?.NotificationTurnId);
        }
        finally
        {
            tempDirectory.Delete(recursive: true);
        }
    }

    [Fact]
    public async Task AbandonSupersededOpenTurnsAsyncIgnoresCachedCurrentWhenNotOpen()
    {
        DirectoryInfo tempDirectory = Directory.CreateTempSubdirectory();

        try
        {
            WorkspaceStateStore stateStore = new(
                TimeProvider.System,
                NullLogger<WorkspaceStateStore>.Instance);
            NotificationTurn olderOpenTurn = await CreateTurnAsync(
                stateStore,
                tempDirectory.FullName,
                "session-123",
                "2026-03-14T15:51:40.783Z");
            NotificationTurn cachedNewerTurn = await CreateTurnAsync(
                stateStore,
                tempDirectory.FullName,
                "session-123",
                "2026-03-14T15:51:50.783Z");
            await WorkspaceStateStore.MarkTurnNotifiedAsync(
                tempDirectory.FullName,
                cachedNewerTurn,
                "2026-03-14T15:52:00.783Z",
                CancellationToken.None);
            olderOpenTurn.Status = "open";
            await WriteTurnStateAsync(tempDirectory.FullName, olderOpenTurn);

            await stateStore.AbandonSupersededOpenTurnsAsync(
                tempDirectory.FullName,
                "session-123",
                "2026-03-14T15:52:10.783Z",
                CancellationToken.None);

            NotificationTurn? storedOlderTurn = await stateStore.TryReadTurnAsync(
                tempDirectory.FullName,
                "session-123",
                olderOpenTurn.NotificationTurnId,
                CancellationToken.None);
            Assert.Equal("open", storedOlderTurn?.Status);
        }
        finally
        {
            tempDirectory.Delete(recursive: true);
        }
    }

    [Theory]
    [InlineData("empty-object")]
    [InlineData("missing-updated-at")]
    [InlineData("invalid-updated-at")]
    [InlineData("wrong-session")]
    [InlineData("wrong-turn-id")]
    [InlineData("wrong-nonce")]
    public async Task HandleUserPromptSubmitAsyncAbandonsOlderReadableInvalidSummaryTurn(
        string invalidSummaryKind)
    {
        DirectoryInfo tempDirectory = Directory.CreateTempSubdirectory();
        using EnvironmentScope environment = SetTelegramEnvironment();

        try
        {
            WorkspaceStateStore stateStore = new(
                TimeProvider.System,
                NullLogger<WorkspaceStateStore>.Instance);
            NotificationTurn oldTurn = await CreateTurnAsync(
                stateStore,
                tempDirectory.FullName,
                "session-123",
                "2026-03-14T15:51:40.783Z");
            await WriteInvalidSummaryAsync(
                tempDirectory.FullName,
                "session-123",
                oldTurn,
                invalidSummaryKind);
            RecordingHttpMessageHandler handler = new();
            HookCommandService service = CreateHookCommandService(handler, stateStore);

            _ = await service.HandleUserPromptSubmitAsync(
                CreateJsonStream(
                    new UserPromptSubmitHookInput
                    {
                        Cwd = tempDirectory.FullName,
                        SessionId = "session-123",
                        Timestamp = "2026-03-14T15:52:40.783Z",
                        TranscriptPath = "/workspace/transcript.json",
                        Prompt = "Start a follow-up turn after an invalid old summary.",
                    },
                    AppJsonSerializerContext.Default.UserPromptSubmitHookInput),
                new MemoryStream(),
                CancellationToken.None);

            NotificationTurn? abandonedOldTurn = await stateStore.TryReadTurnAsync(
                tempDirectory.FullName,
                "session-123",
                oldTurn.NotificationTurnId,
                CancellationToken.None);
            Assert.Equal("abandoned", abandonedOldTurn?.Status);
            NotificationTurn newTurn = Assert.Single(
                await stateStore.ListOpenTurnsAsync(
                    tempDirectory.FullName,
                    "session-123",
                    CancellationToken.None),
                turn => !string.Equals(
                    turn.NotificationTurnId,
                    oldTurn.NotificationTurnId,
                    StringComparison.Ordinal));
            const string newStopTimestamp = "2026-03-14T15:52:50.783Z";
            await WriteSummaryAsync(
                tempDirectory.FullName,
                "session-123",
                newTurn,
                new NotificationSummary
                {
                    SessionId = "session-123",
                    NotificationTurnId = newTurn.NotificationTurnId,
                    NotificationNonce = newTurn.NotificationNonce,
                    UpdatedAt = newStopTimestamp,
                    Summary = "The new turn is not poisoned by the older invalid summary.",
                });

            _ = await service.HandleStopAsync(
                CreateJsonStream(
                    CreateStopInput(tempDirectory.FullName, newStopTimestamp),
                    AppJsonSerializerContext.Default.StopHookInput),
                new MemoryStream(),
                CancellationToken.None);

            TelegramSendMessageRequest payload = DeserializeTelegramPayload(
                Assert.Single(handler.Requests));
            Assert.Contains(
                "摘要：The new turn is not poisoned by the older invalid summary.",
                payload.Text,
                StringComparison.Ordinal);
            Assert.Contains(newTurn.NotificationTurnId, payload.Text, StringComparison.Ordinal);
            Assert.DoesNotContain(oldTurn.NotificationTurnId, payload.Text, StringComparison.Ordinal);
        }
        finally
        {
            tempDirectory.Delete(recursive: true);
        }
    }

    [Fact]
    public async Task HandleStopAsyncOldPendingStopReplayDoesNotNotifyNewTurnWithSameTimestamp()
    {
        DirectoryInfo tempDirectory = Directory.CreateTempSubdirectory();
        using EnvironmentScope environment = SetTelegramEnvironment();

        try
        {
            WorkspaceStateStore stateStore = new(
                TimeProvider.System,
                NullLogger<WorkspaceStateStore>.Instance);
            NotificationTurn oldTurn = await CreateTurnAsync(
                stateStore,
                tempDirectory.FullName,
                "session-123",
                "2026-03-14T15:51:40.783Z");
            RecordingHttpMessageHandler handler = new();
            HookCommandService service = CreateHookCommandService(handler, stateStore);
            const string oldStopTimestamp = "2026-03-14T15:51:50.783Z";
            await WriteSummaryAsync(
                tempDirectory.FullName,
                "session-123",
                oldTurn,
                new NotificationSummary
                {
                    SessionId = "session-123",
                    NotificationTurnId = oldTurn.NotificationTurnId,
                    NotificationNonce = oldTurn.NotificationNonce,
                    UpdatedAt = oldStopTimestamp,
                    Status = "pending",
                    Summary = " ",
                });

            _ = await service.HandleStopAsync(
                CreateJsonStream(
                    CreateStopInput(tempDirectory.FullName, oldStopTimestamp),
                    AppJsonSerializerContext.Default.StopHookInput),
                new MemoryStream(),
                CancellationToken.None);

            Assert.Empty(handler.Requests);
            await AssertPendingStopAsync(
                stateStore,
                tempDirectory.FullName,
                oldTurn,
                oldStopTimestamp,
                "summary must be a non-empty human-readable sentence");

            _ = await service.HandleUserPromptSubmitAsync(
                CreateJsonStream(
                    new UserPromptSubmitHookInput
                    {
                        Cwd = tempDirectory.FullName,
                        SessionId = "session-123",
                        Timestamp = oldStopTimestamp,
                        TranscriptPath = "/workspace/transcript.json",
                        Prompt = "Ship the timestamp-collision follow-up change.",
                    },
                    AppJsonSerializerContext.Default.UserPromptSubmitHookInput),
                new MemoryStream(),
                CancellationToken.None);

            NotificationTurn newTurn = Assert.Single(
                await stateStore.ListOpenTurnsAsync(
                    tempDirectory.FullName,
                    "session-123",
                    CancellationToken.None),
                turn => !string.Equals(
                    turn.NotificationTurnId,
                    oldTurn.NotificationTurnId,
                    StringComparison.Ordinal));
            await WriteSummaryAsync(
                tempDirectory.FullName,
                "session-123",
                newTurn,
                new NotificationSummary
                {
                    SessionId = "session-123",
                    NotificationTurnId = newTurn.NotificationTurnId,
                    NotificationNonce = newTurn.NotificationNonce,
                    UpdatedAt = "2026-03-14T15:52:50.783Z",
                    Summary = "The new turn summary belongs to a later Stop.",
                });

            _ = await service.HandleStopAsync(
                CreateJsonStream(
                    CreateStopInput(tempDirectory.FullName, oldStopTimestamp),
                    AppJsonSerializerContext.Default.StopHookInput),
                new MemoryStream(),
                CancellationToken.None);

            Assert.Empty(handler.Requests);

            const string newStopTimestamp = "2026-03-14T15:52:50.783Z";
            await WriteSummaryAsync(
                tempDirectory.FullName,
                "session-123",
                newTurn,
                new NotificationSummary
                {
                    SessionId = "session-123",
                    NotificationTurnId = newTurn.NotificationTurnId,
                    NotificationNonce = newTurn.NotificationNonce,
                    UpdatedAt = newStopTimestamp,
                    Summary = "The timestamp-collision follow-up turn completed.",
                });

            _ = await service.HandleStopAsync(
                CreateJsonStream(
                    CreateStopInput(tempDirectory.FullName, newStopTimestamp),
                    AppJsonSerializerContext.Default.StopHookInput),
                new MemoryStream(),
                CancellationToken.None);

            TelegramSendMessageRequest payload = DeserializeTelegramPayload(
                Assert.Single(handler.Requests));
            Assert.Contains(
                "摘要：The timestamp-collision follow-up turn completed.",
                payload.Text,
                StringComparison.Ordinal);
            Assert.Contains(newTurn.NotificationTurnId, payload.Text, StringComparison.Ordinal);
        }
        finally
        {
            tempDirectory.Delete(recursive: true);
        }
    }

    [Fact]
    public async Task HandleStopAsyncAbandonedPendingSameTimestampSuppressesCurrentValidStop()
    {
        DirectoryInfo tempDirectory = Directory.CreateTempSubdirectory();
        using EnvironmentScope environment = SetTelegramEnvironment();

        try
        {
            WorkspaceStateStore stateStore = new(
                TimeProvider.System,
                NullLogger<WorkspaceStateStore>.Instance);
            NotificationTurn oldTurn = await CreateTurnAsync(
                stateStore,
                tempDirectory.FullName,
                "session-123",
                "2026-03-14T15:51:40.783Z");
            RecordingHttpMessageHandler handler = new();
            HookCommandService service = CreateHookCommandService(handler, stateStore);
            const string sharedStopTimestamp = "2026-03-14T15:51:50.783Z";
            await WriteSummaryAsync(
                tempDirectory.FullName,
                "session-123",
                oldTurn,
                new NotificationSummary
                {
                    SessionId = "session-123",
                    NotificationTurnId = oldTurn.NotificationTurnId,
                    NotificationNonce = oldTurn.NotificationNonce,
                    UpdatedAt = sharedStopTimestamp,
                    Status = "pending",
                    Summary = " ",
                });

            _ = await service.HandleStopAsync(
                CreateJsonStream(
                    CreateStopInput(tempDirectory.FullName, sharedStopTimestamp),
                    AppJsonSerializerContext.Default.StopHookInput),
                new MemoryStream(),
                CancellationToken.None);
            Assert.Empty(handler.Requests);
            await AssertPendingStopAsync(
                stateStore,
                tempDirectory.FullName,
                oldTurn,
                sharedStopTimestamp,
                "summary must be a non-empty human-readable sentence");

            _ = await service.HandleUserPromptSubmitAsync(
                CreateJsonStream(
                    new UserPromptSubmitHookInput
                    {
                        Cwd = tempDirectory.FullName,
                        SessionId = "session-123",
                        Timestamp = sharedStopTimestamp,
                        TranscriptPath = "/workspace/transcript.json",
                        Prompt = "Ship a legitimate current turn with the same timestamp.",
                    },
                    AppJsonSerializerContext.Default.UserPromptSubmitHookInput),
                new MemoryStream(),
                CancellationToken.None);
            NotificationTurn currentTurn = Assert.Single(
                await stateStore.ListOpenTurnsAsync(
                    tempDirectory.FullName,
                    "session-123",
                    CancellationToken.None),
                turn => !string.Equals(
                    turn.NotificationTurnId,
                    oldTurn.NotificationTurnId,
                    StringComparison.Ordinal));
            await WriteSummaryAsync(
                tempDirectory.FullName,
                "session-123",
                currentTurn,
                new NotificationSummary
                {
                    SessionId = "session-123",
                    NotificationTurnId = currentTurn.NotificationTurnId,
                    NotificationNonce = currentTurn.NotificationNonce,
                    UpdatedAt = sharedStopTimestamp,
                    Summary = "The current same-timestamp turn must wait behind pending recovery.",
                });

            _ = await service.HandleStopAsync(
                CreateJsonStream(
                    CreateStopInput(tempDirectory.FullName, sharedStopTimestamp),
                    AppJsonSerializerContext.Default.StopHookInput),
                new MemoryStream(),
                CancellationToken.None);

            Assert.Empty(handler.Requests);
            Assert.False(File.Exists(AppPaths.GetNotificationRecordPath(
                tempDirectory.FullName,
                "session-123",
                currentTurn.NotificationTurnId,
                CreateStopNotificationKeyForTest(sharedStopTimestamp))));
        }
        finally
        {
            tempDirectory.Delete(recursive: true);
        }
    }

    [Fact]
    public async Task HandleStopAsyncPrefersCurrentWhenOlderAndCurrentSummariesAreExact()
    {
        DirectoryInfo tempDirectory = Directory.CreateTempSubdirectory();
        using EnvironmentScope environment = SetTelegramEnvironment();

        try
        {
            WorkspaceStateStore stateStore = new(
                TimeProvider.System,
                NullLogger<WorkspaceStateStore>.Instance);
            RecordingHttpMessageHandler handler = new();
            HookCommandService service = CreateHookCommandService(handler, stateStore);
            const string stopTimestamp = "2026-03-14T15:51:50.783Z";
            NotificationTurn oldTurn = await CreateTurnAsync(
                stateStore,
                tempDirectory.FullName,
                "session-123",
                "2026-03-14T15:51:40.783Z");
            await WriteSummaryAsync(
                tempDirectory.FullName,
                "session-123",
                oldTurn,
                new NotificationSummary
                {
                    SessionId = "session-123",
                    NotificationTurnId = oldTurn.NotificationTurnId,
                    NotificationNonce = oldTurn.NotificationNonce,
                    UpdatedAt = stopTimestamp,
                    Summary = "The older exact summary must not beat the current exact summary.",
                });
            NotificationTurn currentTurn = await CreateTurnAsync(
                stateStore,
                tempDirectory.FullName,
                "session-123",
                "2026-03-14T15:51:45.783Z");
            await WriteSummaryAsync(
                tempDirectory.FullName,
                "session-123",
                currentTurn,
                new NotificationSummary
                {
                    SessionId = "session-123",
                    NotificationTurnId = currentTurn.NotificationTurnId,
                    NotificationNonce = currentTurn.NotificationNonce,
                    UpdatedAt = stopTimestamp,
                    Summary = "The current exact summary should deliver.",
                });

            _ = await service.HandleStopAsync(
                CreateJsonStream(
                    CreateStopInput(tempDirectory.FullName, stopTimestamp),
                    AppJsonSerializerContext.Default.StopHookInput),
                new MemoryStream(),
                CancellationToken.None);

            TelegramSendMessageRequest payload = DeserializeTelegramPayload(
                Assert.Single(handler.Requests));
            Assert.Contains("摘要：The current exact summary should deliver.", payload.Text, StringComparison.Ordinal);
            Assert.Contains(currentTurn.NotificationTurnId, payload.Text, StringComparison.Ordinal);
            Assert.DoesNotContain(oldTurn.NotificationTurnId, payload.Text, StringComparison.Ordinal);
        }
        finally
        {
            tempDirectory.Delete(recursive: true);
        }
    }

    [Fact]
    public async Task HandleStopAsyncIgnoresCurrentHookPlaceholderWhenOlderCompletedSummaryIsExact()
    {
        DirectoryInfo tempDirectory = Directory.CreateTempSubdirectory();
        using EnvironmentScope environment = SetTelegramEnvironment();

        try
        {
            WorkspaceStateStore stateStore = new(
                FixedUtcNow(),
                NullLogger<WorkspaceStateStore>.Instance);
            RecordingHttpMessageHandler handler = new();
            HookCommandService service = CreateHookCommandService(handler, stateStore);
            const string stopTimestamp = "2026-03-14T15:51:50.783Z";
            NotificationTurn oldTurn = await CreateTurnAsync(
                stateStore,
                tempDirectory.FullName,
                "session-123",
                "2026-03-14T15:51:40.783Z");
            await WriteSummaryAsync(
                tempDirectory.FullName,
                "session-123",
                oldTurn,
                new NotificationSummary
                {
                    SessionId = "session-123",
                    NotificationTurnId = oldTurn.NotificationTurnId,
                    NotificationNonce = oldTurn.NotificationNonce,
                    UpdatedAt = stopTimestamp,
                    Status = "completed",
                    Summary = "The older completed exact summary should beat the current hook placeholder.",
                });
            NotificationTurn currentTurn = await CreateTurnAsync(
                stateStore,
                tempDirectory.FullName,
                "session-123",
                "2026-03-14T15:51:45.783Z");
            Assert.Equal(stopTimestamp, currentTurn.UpdatedAt);
            oldTurn.Status = "open";
            await WriteTurnStateAsync(tempDirectory.FullName, oldTurn);

            _ = await service.HandleStopAsync(
                CreateJsonStream(
                    CreateStopInput(tempDirectory.FullName, stopTimestamp),
                    AppJsonSerializerContext.Default.StopHookInput),
                new MemoryStream(),
                CancellationToken.None);

            TelegramSendMessageRequest payload = DeserializeTelegramPayload(
                Assert.Single(handler.Requests));
            Assert.Contains(
                "摘要：The older completed exact summary should beat the current hook placeholder.",
                payload.Text,
                StringComparison.Ordinal);
            Assert.Contains(oldTurn.NotificationTurnId, payload.Text, StringComparison.Ordinal);
            Assert.DoesNotContain(currentTurn.NotificationTurnId, payload.Text, StringComparison.Ordinal);
            Assert.True(File.Exists(AppPaths.GetNotificationRecordPath(
                tempDirectory.FullName,
                "session-123",
                oldTurn.NotificationTurnId,
                CreateStopNotificationKeyForTest(stopTimestamp))));
            Assert.False(File.Exists(AppPaths.GetStopObservationPath(
                tempDirectory.FullName,
                "session-123",
                currentTurn.NotificationTurnId,
                CreateStopNotificationKeyForTest(stopTimestamp))));
        }
        finally
        {
            tempDirectory.Delete(recursive: true);
        }
    }

    [Fact]
    public async Task HandleStopAsyncDoesNotChooseArbitraryOlderTurnWhenMultipleOlderSummariesAreExact()
    {
        DirectoryInfo tempDirectory = Directory.CreateTempSubdirectory();
        using EnvironmentScope environment = SetTelegramEnvironment();

        try
        {
            WorkspaceStateStore stateStore = new(
                TimeProvider.System,
                NullLogger<WorkspaceStateStore>.Instance);
            RecordingHttpMessageHandler handler = new();
            HookCommandService service = CreateHookCommandService(handler, stateStore);
            const string stopTimestamp = "2026-03-14T15:51:50.783Z";
            NotificationTurn firstOldTurn = await CreateTurnAsync(
                stateStore,
                tempDirectory.FullName,
                "session-123",
                "2026-03-14T15:51:30.783Z");
            NotificationTurn secondOldTurn = await CreateTurnAsync(
                stateStore,
                tempDirectory.FullName,
                "session-123",
                "2026-03-14T15:51:35.783Z");
            await WriteSummaryAsync(
                tempDirectory.FullName,
                "session-123",
                firstOldTurn,
                new NotificationSummary
                {
                    SessionId = "session-123",
                    NotificationTurnId = firstOldTurn.NotificationTurnId,
                    NotificationNonce = firstOldTurn.NotificationNonce,
                    UpdatedAt = stopTimestamp,
                    Summary = "The first older exact summary must not be chosen arbitrarily.",
                });
            await WriteSummaryAsync(
                tempDirectory.FullName,
                "session-123",
                secondOldTurn,
                new NotificationSummary
                {
                    SessionId = "session-123",
                    NotificationTurnId = secondOldTurn.NotificationTurnId,
                    NotificationNonce = secondOldTurn.NotificationNonce,
                    UpdatedAt = stopTimestamp,
                    Summary = "The second older exact summary must not be chosen arbitrarily.",
                });
            NotificationTurn currentTurn = await CreateTurnAsync(
                stateStore,
                tempDirectory.FullName,
                "session-123",
                "2026-03-14T15:51:45.783Z");
            await WriteSummaryAsync(
                tempDirectory.FullName,
                "session-123",
                currentTurn,
                new NotificationSummary
                {
                    SessionId = "session-123",
                    NotificationTurnId = currentTurn.NotificationTurnId,
                    NotificationNonce = currentTurn.NotificationNonce,
                    UpdatedAt = "2026-03-14T15:52:50.783Z",
                    Summary = "The current non-exact summary should keep ownership.",
                });
            firstOldTurn.Status = "open";
            secondOldTurn.Status = "open";
            await File.WriteAllTextAsync(
                AppPaths.GetTurnStatePath(
                    tempDirectory.FullName,
                    "session-123",
                    firstOldTurn.NotificationTurnId),
                JsonSerializer.Serialize(
                    firstOldTurn,
                    AppJsonSerializerContext.Default.NotificationTurn));
            await File.WriteAllTextAsync(
                AppPaths.GetTurnStatePath(
                    tempDirectory.FullName,
                    "session-123",
                    secondOldTurn.NotificationTurnId),
                JsonSerializer.Serialize(
                    secondOldTurn,
                    AppJsonSerializerContext.Default.NotificationTurn));

            _ = await service.HandleStopAsync(
                CreateJsonStream(
                    CreateStopInput(tempDirectory.FullName, stopTimestamp),
                    AppJsonSerializerContext.Default.StopHookInput),
                new MemoryStream(),
                CancellationToken.None);

            TelegramSendMessageRequest payload = DeserializeTelegramPayload(
                Assert.Single(handler.Requests));
            Assert.Contains(currentTurn.NotificationTurnId, payload.Text, StringComparison.Ordinal);
            Assert.DoesNotContain(firstOldTurn.NotificationTurnId, payload.Text, StringComparison.Ordinal);
            Assert.DoesNotContain(secondOldTurn.NotificationTurnId, payload.Text, StringComparison.Ordinal);
            Assert.False(File.Exists(AppPaths.GetNotificationRecordPath(
                tempDirectory.FullName,
                "session-123",
                firstOldTurn.NotificationTurnId,
                CreateStopNotificationKeyForTest(stopTimestamp))));
            Assert.False(File.Exists(AppPaths.GetNotificationRecordPath(
                tempDirectory.FullName,
                "session-123",
                secondOldTurn.NotificationTurnId,
                CreateStopNotificationKeyForTest(stopTimestamp))));
        }
        finally
        {
            tempDirectory.Delete(recursive: true);
        }
    }

    [Fact]
    public async Task HandleStopAsyncUsesLatestDurableValidSummaryWhenCurrentCacheIsStaleAndOlderExactIsAmbiguous()
    {
        DirectoryInfo tempDirectory = Directory.CreateTempSubdirectory();
        using EnvironmentScope environment = SetTelegramEnvironment();

        try
        {
            WorkspaceStateStore stateStore = new(
                TimeProvider.System,
                NullLogger<WorkspaceStateStore>.Instance);
            RecordingHttpMessageHandler handler = new();
            const string sessionId = "session-123";
            const string stopTimestamp = "2026-03-14T15:51:50.783Z";
            NotificationTurn staleCachedTurn = await CreateTurnAsync(
                stateStore,
                tempDirectory.FullName,
                sessionId,
                "2026-03-14T15:51:25.783Z");
            string staleCurrentJson = await File.ReadAllTextAsync(AppPaths.GetCurrentStatePath(
                tempDirectory.FullName,
                sessionId));
            NotificationTurn secondOldTurn = await CreateTurnAsync(
                stateStore,
                tempDirectory.FullName,
                sessionId,
                "2026-03-14T15:51:30.783Z");
            NotificationTurn latestDurableTurn = await CreateTurnAsync(
                stateStore,
                tempDirectory.FullName,
                sessionId,
                "2026-03-14T15:51:45.783Z");
            foreach (NotificationTurn oldTurn in new[] { staleCachedTurn, secondOldTurn })
            {
                await WriteSummaryAsync(
                    tempDirectory.FullName,
                    sessionId,
                    oldTurn,
                    new NotificationSummary
                    {
                        SessionId = sessionId,
                        NotificationTurnId = oldTurn.NotificationTurnId,
                        NotificationNonce = oldTurn.NotificationNonce,
                        UpdatedAt = stopTimestamp,
                        Summary = "An older exact summary must not be chosen arbitrarily.",
                    });
                oldTurn.Status = "open";
                await WriteTurnStateAsync(tempDirectory.FullName, oldTurn);
            }

            await WriteSummaryAsync(
                tempDirectory.FullName,
                sessionId,
                latestDurableTurn,
                new NotificationSummary
                {
                    SessionId = sessionId,
                    NotificationTurnId = latestDurableTurn.NotificationTurnId,
                    NotificationNonce = latestDurableTurn.NotificationNonce,
                    UpdatedAt = "2026-03-14T15:52:50.783Z",
                    Summary = "The latest durable non-exact summary should win over the stale cache.",
                });
            await File.WriteAllTextAsync(
                AppPaths.GetCurrentStatePath(tempDirectory.FullName, sessionId),
                staleCurrentJson);
            HookCommandService service = CreateHookCommandService(handler, stateStore);

            _ = await service.HandleStopAsync(
                CreateJsonStream(
                    CreateStopInput(tempDirectory.FullName, stopTimestamp),
                    AppJsonSerializerContext.Default.StopHookInput),
                new MemoryStream(),
                CancellationToken.None);

            TelegramSendMessageRequest payload = DeserializeTelegramPayload(
                Assert.Single(handler.Requests));
            Assert.Contains(latestDurableTurn.NotificationTurnId, payload.Text, StringComparison.Ordinal);
            Assert.Contains(
                "摘要：The latest durable non-exact summary should win over the stale cache.",
                payload.Text,
                StringComparison.Ordinal);
            Assert.DoesNotContain(staleCachedTurn.NotificationTurnId, payload.Text, StringComparison.Ordinal);
            Assert.DoesNotContain(secondOldTurn.NotificationTurnId, payload.Text, StringComparison.Ordinal);
        }
        finally
        {
            tempDirectory.Delete(recursive: true);
        }
    }

    [Fact]
    public async Task HandleStopAsyncUsesLatestDurableValidSummaryWhenCurrentCacheIsStaleAndSingleOlderExactExists()
    {
        DirectoryInfo tempDirectory = Directory.CreateTempSubdirectory();
        using EnvironmentScope environment = SetTelegramEnvironment();

        try
        {
            WorkspaceStateStore stateStore = new(
                TimeProvider.System,
                NullLogger<WorkspaceStateStore>.Instance);
            RecordingHttpMessageHandler handler = new();
            const string sessionId = "session-123";
            const string stopTimestamp = "2026-03-14T15:51:50.783Z";
            NotificationTurn staleCachedTurn = await CreateTurnAsync(
                stateStore,
                tempDirectory.FullName,
                sessionId,
                "2026-03-14T15:51:25.783Z");
            string staleCurrentJson = await File.ReadAllTextAsync(AppPaths.GetCurrentStatePath(
                tempDirectory.FullName,
                sessionId));
            NotificationTurn exactOldTurn = await CreateTurnAsync(
                stateStore,
                tempDirectory.FullName,
                sessionId,
                "2026-03-14T15:51:30.783Z");
            await WriteSummaryAsync(
                tempDirectory.FullName,
                sessionId,
                exactOldTurn,
                new NotificationSummary
                {
                    SessionId = sessionId,
                    NotificationTurnId = exactOldTurn.NotificationTurnId,
                    NotificationNonce = exactOldTurn.NotificationNonce,
                    UpdatedAt = stopTimestamp,
                    Summary = "A singleton older exact summary must not beat the latest durable turn.",
                });
            NotificationTurn latestDurableTurn = await CreateTurnAsync(
                stateStore,
                tempDirectory.FullName,
                sessionId,
                "2026-03-14T15:51:45.783Z");
            await WriteSummaryAsync(
                tempDirectory.FullName,
                sessionId,
                latestDurableTurn,
                new NotificationSummary
                {
                    SessionId = sessionId,
                    NotificationTurnId = latestDurableTurn.NotificationTurnId,
                    NotificationNonce = latestDurableTurn.NotificationNonce,
                    UpdatedAt = "2026-03-14T15:51:49.783Z",
                    Summary = "The latest durable non-exact summary should win over the stale current cache.",
                });
            await File.WriteAllTextAsync(
                AppPaths.GetCurrentStatePath(tempDirectory.FullName, sessionId),
                staleCurrentJson);
            HookCommandService service = CreateHookCommandService(handler, stateStore);

            _ = await service.HandleStopAsync(
                CreateJsonStream(
                    CreateStopInput(tempDirectory.FullName, stopTimestamp),
                    AppJsonSerializerContext.Default.StopHookInput),
                new MemoryStream(),
                CancellationToken.None);

            TelegramSendMessageRequest payload = DeserializeTelegramPayload(
                Assert.Single(handler.Requests));
            Assert.Contains(latestDurableTurn.NotificationTurnId, payload.Text, StringComparison.Ordinal);
            Assert.Contains(
                "摘要：The latest durable non-exact summary should win over the stale current cache.",
                payload.Text,
                StringComparison.Ordinal);
            Assert.DoesNotContain(staleCachedTurn.NotificationTurnId, payload.Text, StringComparison.Ordinal);
            Assert.DoesNotContain(exactOldTurn.NotificationTurnId, payload.Text, StringComparison.Ordinal);
        }
        finally
        {
            tempDirectory.Delete(recursive: true);
        }
    }

    [Fact]
    public async Task HandleStopAsyncDefersCurrentPendingWhenMultipleOlderSummariesAreExact()
    {
        DirectoryInfo tempDirectory = Directory.CreateTempSubdirectory();
        using EnvironmentScope environment = SetTelegramEnvironment();

        try
        {
            WorkspaceStateStore stateStore = new(
                TimeProvider.System,
                NullLogger<WorkspaceStateStore>.Instance);
            RecordingHttpMessageHandler handler = new();
            HookCommandService service = CreateHookCommandService(handler, stateStore);
            const string stopTimestamp = "2026-03-14T15:51:50.783Z";
            NotificationTurn firstOldTurn = await CreateTurnAsync(
                stateStore,
                tempDirectory.FullName,
                "session-123",
                "2026-03-14T15:51:30.783Z");
            NotificationTurn secondOldTurn = await CreateTurnAsync(
                stateStore,
                tempDirectory.FullName,
                "session-123",
                "2026-03-14T15:51:35.783Z");
            await WriteSummaryAsync(
                tempDirectory.FullName,
                "session-123",
                firstOldTurn,
                new NotificationSummary
                {
                    SessionId = "session-123",
                    NotificationTurnId = firstOldTurn.NotificationTurnId,
                    NotificationNonce = firstOldTurn.NotificationNonce,
                    UpdatedAt = stopTimestamp,
                    Summary = "The first older exact summary must not be chosen arbitrarily.",
                });
            await WriteSummaryAsync(
                tempDirectory.FullName,
                "session-123",
                secondOldTurn,
                new NotificationSummary
                {
                    SessionId = "session-123",
                    NotificationTurnId = secondOldTurn.NotificationTurnId,
                    NotificationNonce = secondOldTurn.NotificationNonce,
                    UpdatedAt = stopTimestamp,
                    Summary = "The second older exact summary must not be chosen arbitrarily.",
                });
            NotificationTurn currentTurn = await CreateTurnAsync(
                stateStore,
                tempDirectory.FullName,
                "session-123",
                "2026-03-14T15:51:45.783Z");
            await WriteSummaryAsync(
                tempDirectory.FullName,
                "session-123",
                currentTurn,
                new NotificationSummary
                {
                    SessionId = "session-123",
                    NotificationTurnId = currentTurn.NotificationTurnId,
                    NotificationNonce = currentTurn.NotificationNonce,
                    UpdatedAt = stopTimestamp,
                    Status = "pending",
                    Summary = string.Empty,
                });
            firstOldTurn.Status = "open";
            secondOldTurn.Status = "open";
            await WriteTurnStateAsync(tempDirectory.FullName, firstOldTurn);
            await WriteTurnStateAsync(tempDirectory.FullName, secondOldTurn);

            _ = await service.HandleStopAsync(
                CreateJsonStream(
                    CreateStopInput(tempDirectory.FullName, stopTimestamp),
                    AppJsonSerializerContext.Default.StopHookInput),
                new MemoryStream(),
                CancellationToken.None);

            Assert.Empty(handler.Requests);
            await AssertPendingStopAsync(
                stateStore,
                tempDirectory.FullName,
                currentTurn,
                stopTimestamp,
                "summary must be a non-empty human-readable sentence");
            Assert.False(File.Exists(AppPaths.GetNotificationRecordPath(
                tempDirectory.FullName,
                "session-123",
                firstOldTurn.NotificationTurnId,
                CreateStopNotificationKeyForTest(stopTimestamp))));
            Assert.False(File.Exists(AppPaths.GetNotificationRecordPath(
                tempDirectory.FullName,
                "session-123",
                secondOldTurn.NotificationTurnId,
                CreateStopNotificationKeyForTest(stopTimestamp))));
        }
        finally
        {
            tempDirectory.Delete(recursive: true);
        }
    }

    [Fact]
    public async Task HandleStopAsyncUsesCurrentInvalidWhenMultipleOlderSummariesAreExact()
    {
        DirectoryInfo tempDirectory = Directory.CreateTempSubdirectory();
        using EnvironmentScope environment = SetTelegramEnvironment();

        try
        {
            WorkspaceStateStore stateStore = new(
                TimeProvider.System,
                NullLogger<WorkspaceStateStore>.Instance);
            RecordingHttpMessageHandler handler = new();
            HookCommandService service = CreateHookCommandService(handler, stateStore);
            const string stopTimestamp = "2026-03-14T15:51:50.783Z";
            NotificationTurn firstOldTurn = await CreateTurnAsync(
                stateStore,
                tempDirectory.FullName,
                "session-123",
                "2026-03-14T15:51:30.783Z");
            NotificationTurn secondOldTurn = await CreateTurnAsync(
                stateStore,
                tempDirectory.FullName,
                "session-123",
                "2026-03-14T15:51:35.783Z");
            await WriteSummaryAsync(
                tempDirectory.FullName,
                "session-123",
                firstOldTurn,
                new NotificationSummary
                {
                    SessionId = "session-123",
                    NotificationTurnId = firstOldTurn.NotificationTurnId,
                    NotificationNonce = firstOldTurn.NotificationNonce,
                    UpdatedAt = stopTimestamp,
                    Summary = "The first older exact summary must not be chosen arbitrarily.",
                });
            await WriteSummaryAsync(
                tempDirectory.FullName,
                "session-123",
                secondOldTurn,
                new NotificationSummary
                {
                    SessionId = "session-123",
                    NotificationTurnId = secondOldTurn.NotificationTurnId,
                    NotificationNonce = secondOldTurn.NotificationNonce,
                    UpdatedAt = stopTimestamp,
                    Summary = "The second older exact summary must not be chosen arbitrarily.",
                });
            NotificationTurn currentTurn = await CreateTurnAsync(
                stateStore,
                tempDirectory.FullName,
                "session-123",
                "2026-03-14T15:51:45.783Z");
            await WriteInvalidSummaryAsync(
                tempDirectory.FullName,
                "session-123",
                currentTurn,
                "empty-object");
            firstOldTurn.Status = "open";
            secondOldTurn.Status = "open";
            await WriteTurnStateAsync(tempDirectory.FullName, firstOldTurn);
            await WriteTurnStateAsync(tempDirectory.FullName, secondOldTurn);

            _ = await service.HandleStopAsync(
                CreateJsonStream(
                    CreateStopInput(tempDirectory.FullName, stopTimestamp),
                    AppJsonSerializerContext.Default.StopHookInput),
                new MemoryStream(),
                CancellationToken.None);

            TelegramSendMessageRequest payload = DeserializeTelegramPayload(
                Assert.Single(handler.Requests));
            Assert.Contains(currentTurn.NotificationTurnId, payload.Text, StringComparison.Ordinal);
            Assert.DoesNotContain(firstOldTurn.NotificationTurnId, payload.Text, StringComparison.Ordinal);
            Assert.DoesNotContain(secondOldTurn.NotificationTurnId, payload.Text, StringComparison.Ordinal);
            Assert.False(File.Exists(AppPaths.GetNotificationRecordPath(
                tempDirectory.FullName,
                "session-123",
                firstOldTurn.NotificationTurnId,
                CreateStopNotificationKeyForTest(stopTimestamp))));
            Assert.False(File.Exists(AppPaths.GetNotificationRecordPath(
                tempDirectory.FullName,
                "session-123",
                secondOldTurn.NotificationTurnId,
                CreateStopNotificationKeyForTest(stopTimestamp))));
        }
        finally
        {
            tempDirectory.Delete(recursive: true);
        }
    }

    [Theory]
    [InlineData("missing")]
    [InlineData("invalid-json")]
    public async Task HandleStopAsyncDoesNotChooseUnrelatedNonExactOldWhenMultipleOlderSummariesAreExact(
        string currentSummaryState)
    {
        DirectoryInfo tempDirectory = Directory.CreateTempSubdirectory();
        using EnvironmentScope environment = SetTelegramEnvironment();

        try
        {
            WorkspaceStateStore stateStore = new(
                TimeProvider.System,
                NullLogger<WorkspaceStateStore>.Instance);
            RecordingHttpMessageHandler handler = new();
            HookCommandService service = CreateHookCommandService(handler, stateStore);
            const string stopTimestamp = "2026-03-14T15:51:50.783Z";
            NotificationTurn firstOldTurn = await CreateTurnAsync(
                stateStore,
                tempDirectory.FullName,
                "session-123",
                "2026-03-14T15:51:25.783Z");
            NotificationTurn secondOldTurn = await CreateTurnAsync(
                stateStore,
                tempDirectory.FullName,
                "session-123",
                "2026-03-14T15:51:30.783Z");
            NotificationTurn nonExactOldTurn = await CreateTurnAsync(
                stateStore,
                tempDirectory.FullName,
                "session-123",
                "2026-03-14T15:51:35.783Z");
            await WriteSummaryAsync(
                tempDirectory.FullName,
                "session-123",
                firstOldTurn,
                new NotificationSummary
                {
                    SessionId = "session-123",
                    NotificationTurnId = firstOldTurn.NotificationTurnId,
                    NotificationNonce = firstOldTurn.NotificationNonce,
                    UpdatedAt = stopTimestamp,
                    Summary = "The first older exact summary must not be chosen arbitrarily.",
                });
            await WriteSummaryAsync(
                tempDirectory.FullName,
                "session-123",
                secondOldTurn,
                new NotificationSummary
                {
                    SessionId = "session-123",
                    NotificationTurnId = secondOldTurn.NotificationTurnId,
                    NotificationNonce = secondOldTurn.NotificationNonce,
                    UpdatedAt = stopTimestamp,
                    Summary = "The second older exact summary must not be chosen arbitrarily.",
                });
            await WriteSummaryAsync(
                tempDirectory.FullName,
                "session-123",
                nonExactOldTurn,
                new NotificationSummary
                {
                    SessionId = "session-123",
                    NotificationTurnId = nonExactOldTurn.NotificationTurnId,
                    NotificationNonce = nonExactOldTurn.NotificationNonce,
                    UpdatedAt = "2026-03-14T15:51:49.783Z",
                    Summary = "The unrelated older non-exact summary must not be chosen.",
                });
            NotificationTurn currentTurn = await CreateTurnAsync(
                stateStore,
                tempDirectory.FullName,
                "session-123",
                "2026-03-14T15:51:45.783Z");
            if (string.Equals(currentSummaryState, "missing", StringComparison.Ordinal))
            {
                File.Delete(AppPaths.GetSummaryStatePath(
                    tempDirectory.FullName,
                    "session-123",
                    currentTurn.NotificationTurnId));
            }
            else
            {
                await WriteRawSummaryJsonAsync(
                    tempDirectory.FullName,
                    "session-123",
                    currentTurn,
                    "{");
            }

            firstOldTurn.Status = "open";
            secondOldTurn.Status = "open";
            nonExactOldTurn.Status = "open";
            await WriteTurnStateAsync(tempDirectory.FullName, firstOldTurn);
            await WriteTurnStateAsync(tempDirectory.FullName, secondOldTurn);
            await WriteTurnStateAsync(tempDirectory.FullName, nonExactOldTurn);

            _ = await service.HandleStopAsync(
                CreateJsonStream(
                    CreateStopInput(tempDirectory.FullName, stopTimestamp),
                    AppJsonSerializerContext.Default.StopHookInput),
                new MemoryStream(),
                CancellationToken.None);

            Assert.Empty(handler.Requests);
            await AssertPendingStopAsync(
                stateStore,
                tempDirectory.FullName,
                currentTurn,
                stopTimestamp,
                string.Equals(currentSummaryState, "missing", StringComparison.Ordinal)
                    ? "Summary file is missing"
                    : "could not be parsed as JSON");
            foreach (NotificationTurn oldTurn in new[] { firstOldTurn, secondOldTurn, nonExactOldTurn })
            {
                Assert.False(File.Exists(AppPaths.GetNotificationRecordPath(
                    tempDirectory.FullName,
                    "session-123",
                    oldTurn.NotificationTurnId,
                    CreateStopNotificationKeyForTest(stopTimestamp))));
            }
        }
        finally
        {
            tempDirectory.Delete(recursive: true);
        }
    }

    [Fact]
    public async Task HandleStopAsyncFallsBackToCurrentInvalidWhenMultipleOlderExactAndNonExactSummariesExist()
    {
        DirectoryInfo tempDirectory = Directory.CreateTempSubdirectory();
        using EnvironmentScope environment = SetTelegramEnvironment();

        try
        {
            WorkspaceStateStore stateStore = new(
                TimeProvider.System,
                NullLogger<WorkspaceStateStore>.Instance);
            RecordingHttpMessageHandler handler = new();
            HookCommandService service = CreateHookCommandService(handler, stateStore);
            const string stopTimestamp = "2026-03-14T15:51:50.783Z";
            NotificationTurn firstOldTurn = await CreateTurnAsync(
                stateStore,
                tempDirectory.FullName,
                "session-123",
                "2026-03-14T15:51:25.783Z");
            NotificationTurn secondOldTurn = await CreateTurnAsync(
                stateStore,
                tempDirectory.FullName,
                "session-123",
                "2026-03-14T15:51:30.783Z");
            NotificationTurn nonExactOldTurn = await CreateTurnAsync(
                stateStore,
                tempDirectory.FullName,
                "session-123",
                "2026-03-14T15:51:35.783Z");
            foreach (NotificationTurn oldTurn in new[] { firstOldTurn, secondOldTurn })
            {
                await WriteSummaryAsync(
                    tempDirectory.FullName,
                    "session-123",
                    oldTurn,
                    new NotificationSummary
                    {
                        SessionId = "session-123",
                        NotificationTurnId = oldTurn.NotificationTurnId,
                        NotificationNonce = oldTurn.NotificationNonce,
                        UpdatedAt = stopTimestamp,
                        Summary = "An older exact summary must not be chosen arbitrarily.",
                    });
                oldTurn.Status = "open";
                await WriteTurnStateAsync(tempDirectory.FullName, oldTurn);
            }

            await WriteSummaryAsync(
                tempDirectory.FullName,
                "session-123",
                nonExactOldTurn,
                new NotificationSummary
                {
                    SessionId = "session-123",
                    NotificationTurnId = nonExactOldTurn.NotificationTurnId,
                    NotificationNonce = nonExactOldTurn.NotificationNonce,
                    UpdatedAt = "2026-03-14T15:51:49.783Z",
                    Summary = "The unrelated older non-exact summary must not be chosen.",
                });
            nonExactOldTurn.Status = "open";
            await WriteTurnStateAsync(tempDirectory.FullName, nonExactOldTurn);
            NotificationTurn currentTurn = await CreateTurnAsync(
                stateStore,
                tempDirectory.FullName,
                "session-123",
                "2026-03-14T15:51:45.783Z");
            await WriteInvalidSummaryAsync(
                tempDirectory.FullName,
                "session-123",
                currentTurn,
                "empty-object");

            _ = await service.HandleStopAsync(
                CreateJsonStream(
                    CreateStopInput(tempDirectory.FullName, stopTimestamp),
                    AppJsonSerializerContext.Default.StopHookInput),
                new MemoryStream(),
                CancellationToken.None);

            TelegramSendMessageRequest payload = DeserializeTelegramPayload(
                Assert.Single(handler.Requests));
            Assert.Contains("摘要：当前轮未生成摘要。", payload.Text, StringComparison.Ordinal);
            Assert.Contains(currentTurn.NotificationTurnId, payload.Text, StringComparison.Ordinal);
            foreach (NotificationTurn oldTurn in new[] { firstOldTurn, secondOldTurn, nonExactOldTurn })
            {
                Assert.DoesNotContain(oldTurn.NotificationTurnId, payload.Text, StringComparison.Ordinal);
                Assert.False(File.Exists(AppPaths.GetNotificationRecordPath(
                    tempDirectory.FullName,
                    "session-123",
                    oldTurn.NotificationTurnId,
                    CreateStopNotificationKeyForTest(stopTimestamp))));
            }
        }
        finally
        {
            tempDirectory.Delete(recursive: true);
        }
    }

    [Fact]
    public async Task HandleStopAsyncPrefersCurrentExactWhenMultipleOlderSummariesAreExact()
    {
        DirectoryInfo tempDirectory = Directory.CreateTempSubdirectory();
        using EnvironmentScope environment = SetTelegramEnvironment();

        try
        {
            WorkspaceStateStore stateStore = new(
                TimeProvider.System,
                NullLogger<WorkspaceStateStore>.Instance);
            RecordingHttpMessageHandler handler = new();
            HookCommandService service = CreateHookCommandService(handler, stateStore);
            const string stopTimestamp = "2026-03-14T15:51:50.783Z";
            NotificationTurn firstOldTurn = await CreateTurnAsync(
                stateStore,
                tempDirectory.FullName,
                "session-123",
                "2026-03-14T15:51:30.783Z");
            NotificationTurn secondOldTurn = await CreateTurnAsync(
                stateStore,
                tempDirectory.FullName,
                "session-123",
                "2026-03-14T15:51:35.783Z");
            await WriteSummaryAsync(
                tempDirectory.FullName,
                "session-123",
                firstOldTurn,
                new NotificationSummary
                {
                    SessionId = "session-123",
                    NotificationTurnId = firstOldTurn.NotificationTurnId,
                    NotificationNonce = firstOldTurn.NotificationNonce,
                    UpdatedAt = stopTimestamp,
                    Summary = "The first older exact summary must not beat current exact.",
                });
            await WriteSummaryAsync(
                tempDirectory.FullName,
                "session-123",
                secondOldTurn,
                new NotificationSummary
                {
                    SessionId = "session-123",
                    NotificationTurnId = secondOldTurn.NotificationTurnId,
                    NotificationNonce = secondOldTurn.NotificationNonce,
                    UpdatedAt = stopTimestamp,
                    Summary = "The second older exact summary must not beat current exact.",
                });
            NotificationTurn currentTurn = await CreateTurnAsync(
                stateStore,
                tempDirectory.FullName,
                "session-123",
                "2026-03-14T15:51:45.783Z");
            await WriteSummaryAsync(
                tempDirectory.FullName,
                "session-123",
                currentTurn,
                new NotificationSummary
                {
                    SessionId = "session-123",
                    NotificationTurnId = currentTurn.NotificationTurnId,
                    NotificationNonce = currentTurn.NotificationNonce,
                    UpdatedAt = stopTimestamp,
                    Summary = "The current exact summary wins over multiple older exact summaries.",
                });
            firstOldTurn.Status = "open";
            secondOldTurn.Status = "open";
            await WriteTurnStateAsync(tempDirectory.FullName, firstOldTurn);
            await WriteTurnStateAsync(tempDirectory.FullName, secondOldTurn);

            _ = await service.HandleStopAsync(
                CreateJsonStream(
                    CreateStopInput(tempDirectory.FullName, stopTimestamp),
                    AppJsonSerializerContext.Default.StopHookInput),
                new MemoryStream(),
                CancellationToken.None);

            TelegramSendMessageRequest payload = DeserializeTelegramPayload(
                Assert.Single(handler.Requests));
            Assert.Contains(
                "摘要：The current exact summary wins over multiple older exact summaries.",
                payload.Text,
                StringComparison.Ordinal);
            Assert.Contains(currentTurn.NotificationTurnId, payload.Text, StringComparison.Ordinal);
            Assert.DoesNotContain(firstOldTurn.NotificationTurnId, payload.Text, StringComparison.Ordinal);
            Assert.DoesNotContain(secondOldTurn.NotificationTurnId, payload.Text, StringComparison.Ordinal);
            Assert.False(File.Exists(AppPaths.GetNotificationRecordPath(
                tempDirectory.FullName,
                "session-123",
                firstOldTurn.NotificationTurnId,
                CreateStopNotificationKeyForTest(stopTimestamp))));
            Assert.False(File.Exists(AppPaths.GetNotificationRecordPath(
                tempDirectory.FullName,
                "session-123",
                secondOldTurn.NotificationTurnId,
                CreateStopNotificationKeyForTest(stopTimestamp))));
        }
        finally
        {
            tempDirectory.Delete(recursive: true);
        }
    }

    [Fact]
    public async Task HandleStopAsyncCurrentCacheDoesNotBreakSameCreatedAtExactSummaryTie()
    {
        DirectoryInfo tempDirectory = Directory.CreateTempSubdirectory();
        using EnvironmentScope environment = SetTelegramEnvironment();

        try
        {
            WorkspaceStateStore stateStore = new(
                TimeProvider.System,
                NullLogger<WorkspaceStateStore>.Instance);
            RecordingHttpMessageHandler handler = new();
            HookCommandService service = CreateHookCommandService(handler, stateStore);
            const string stopTimestamp = "2026-03-14T15:51:50.783Z";
            const string sharedCreatedAt = "2026-03-14T15:51:45.783Z";
            NotificationTurn firstTurn = await CreateTurnAsync(
                stateStore,
                tempDirectory.FullName,
                "session-123",
                sharedCreatedAt);
            NotificationTurn cachedCurrentTurn = await CreateTurnAsync(
                stateStore,
                tempDirectory.FullName,
                "session-123",
                sharedCreatedAt);
            await WriteSummaryAsync(
                tempDirectory.FullName,
                "session-123",
                firstTurn,
                new NotificationSummary
                {
                    SessionId = "session-123",
                    NotificationTurnId = firstTurn.NotificationTurnId,
                    NotificationNonce = firstTurn.NotificationNonce,
                    UpdatedAt = stopTimestamp,
                    Status = "completed",
                    Summary = "The first exact summary ties the cached current turn.",
                });
            await WriteSummaryAsync(
                tempDirectory.FullName,
                "session-123",
                cachedCurrentTurn,
                new NotificationSummary
                {
                    SessionId = "session-123",
                    NotificationTurnId = cachedCurrentTurn.NotificationTurnId,
                    NotificationNonce = cachedCurrentTurn.NotificationNonce,
                    UpdatedAt = stopTimestamp,
                    Status = "completed",
                    Summary = "The cached current exact summary must not break the tie.",
                });
            firstTurn.Status = "open";
            cachedCurrentTurn.Status = "open";
            await WriteTurnStateAsync(tempDirectory.FullName, firstTurn);
            await WriteTurnStateAsync(tempDirectory.FullName, cachedCurrentTurn);

            _ = await service.HandleStopAsync(
                CreateJsonStream(
                    CreateStopInput(tempDirectory.FullName, stopTimestamp),
                    AppJsonSerializerContext.Default.StopHookInput),
                new MemoryStream(),
                CancellationToken.None);

            Assert.Empty(handler.Requests);
            string notificationKey = CreateStopNotificationKeyForTest(stopTimestamp);
            foreach (NotificationTurn turn in new[] { firstTurn, cachedCurrentTurn })
            {
                Assert.False(File.Exists(AppPaths.GetNotificationRecordPath(
                    tempDirectory.FullName,
                    "session-123",
                    turn.NotificationTurnId,
                    notificationKey)));
            }

            Assert.False(File.Exists(AppPaths.GetSessionNotificationRecordPath(
                tempDirectory.FullName,
                "session-123",
                notificationKey)));
        }
        finally
        {
            tempDirectory.Delete(recursive: true);
        }
    }

    [Fact]
    public async Task HandleStopAsyncAbandonedPendingSameTimestampSuppressesCurrentInvalidFallback()
    {
        DirectoryInfo tempDirectory = Directory.CreateTempSubdirectory();
        using EnvironmentScope environment = SetTelegramEnvironment();

        try
        {
            WorkspaceStateStore stateStore = new(
                TimeProvider.System,
                NullLogger<WorkspaceStateStore>.Instance);
            NotificationTurn oldTurn = await CreateTurnAsync(
                stateStore,
                tempDirectory.FullName,
                "session-123",
                "2026-03-14T15:51:40.783Z");
            RecordingHttpMessageHandler handler = new();
            HookCommandService service = CreateHookCommandService(handler, stateStore);
            const string sharedStopTimestamp = "2026-03-14T15:51:50.783Z";
            await WriteSummaryAsync(
                tempDirectory.FullName,
                "session-123",
                oldTurn,
                new NotificationSummary
                {
                    SessionId = "session-123",
                    NotificationTurnId = oldTurn.NotificationTurnId,
                    NotificationNonce = oldTurn.NotificationNonce,
                    UpdatedAt = sharedStopTimestamp,
                    Status = "pending",
                    Summary = " ",
                });

            _ = await service.HandleStopAsync(
                CreateJsonStream(
                    CreateStopInput(tempDirectory.FullName, sharedStopTimestamp),
                    AppJsonSerializerContext.Default.StopHookInput),
                new MemoryStream(),
                CancellationToken.None);

            Assert.Empty(handler.Requests);
            await AssertPendingStopAsync(
                stateStore,
                tempDirectory.FullName,
                oldTurn,
                sharedStopTimestamp,
                "summary must be a non-empty human-readable sentence");

            _ = await service.HandleUserPromptSubmitAsync(
                CreateJsonStream(
                    new UserPromptSubmitHookInput
                    {
                        Cwd = tempDirectory.FullName,
                        SessionId = "session-123",
                        Timestamp = sharedStopTimestamp,
                        TranscriptPath = "/workspace/transcript.json",
                        Prompt = "Ship a current turn whose invalid summary should fall back.",
                    },
                    AppJsonSerializerContext.Default.UserPromptSubmitHookInput),
                new MemoryStream(),
                CancellationToken.None);
            NotificationTurn currentTurn = Assert.Single(
                await stateStore.ListOpenTurnsAsync(
                    tempDirectory.FullName,
                    "session-123",
                    CancellationToken.None),
                turn => !string.Equals(
                    turn.NotificationTurnId,
                    oldTurn.NotificationTurnId,
                    StringComparison.Ordinal));
            string summaryPath = AppPaths.GetSummaryStatePath(
                tempDirectory.FullName,
                "session-123",
                currentTurn.NotificationTurnId);
            await File.WriteAllTextAsync(summaryPath, "{}");

            _ = await service.HandleStopAsync(
                CreateJsonStream(
                    CreateStopInput(tempDirectory.FullName, sharedStopTimestamp),
                    AppJsonSerializerContext.Default.StopHookInput),
                new MemoryStream(),
                CancellationToken.None);

            Assert.Empty(handler.Requests);
            Assert.False(File.Exists(AppPaths.GetNotificationRecordPath(
                tempDirectory.FullName,
                "session-123",
                currentTurn.NotificationTurnId,
                CreateStopNotificationKeyForTest(sharedStopTimestamp))));
        }
        finally
        {
            tempDirectory.Delete(recursive: true);
        }
    }

    [Fact]
    public async Task HandleStopAsyncAbandonedPendingSameTimestampSuppressesCurrentNonExactValidSummary()
    {
        DirectoryInfo tempDirectory = Directory.CreateTempSubdirectory();
        using EnvironmentScope environment = SetTelegramEnvironment();

        try
        {
            WorkspaceStateStore stateStore = new(
                TimeProvider.System,
                NullLogger<WorkspaceStateStore>.Instance);
            NotificationTurn oldTurn = await CreateTurnAsync(
                stateStore,
                tempDirectory.FullName,
                "session-123",
                "2026-03-14T15:51:40.783Z");
            RecordingHttpMessageHandler handler = new();
            HookCommandService service = CreateHookCommandService(handler, stateStore);
            const string sharedStopTimestamp = "2026-03-14T15:51:50.783Z";
            await WriteSummaryAsync(
                tempDirectory.FullName,
                "session-123",
                oldTurn,
                new NotificationSummary
                {
                    SessionId = "session-123",
                    NotificationTurnId = oldTurn.NotificationTurnId,
                    NotificationNonce = oldTurn.NotificationNonce,
                    UpdatedAt = sharedStopTimestamp,
                    Status = "pending",
                    Summary = " ",
                });

            _ = await service.HandleStopAsync(
                CreateJsonStream(
                    CreateStopInput(tempDirectory.FullName, sharedStopTimestamp),
                    AppJsonSerializerContext.Default.StopHookInput),
                new MemoryStream(),
                CancellationToken.None);

            Assert.Empty(handler.Requests);
            await AssertPendingStopAsync(
                stateStore,
                tempDirectory.FullName,
                oldTurn,
                sharedStopTimestamp,
                "summary must be a non-empty human-readable sentence");

            _ = await service.HandleUserPromptSubmitAsync(
                CreateJsonStream(
                    new UserPromptSubmitHookInput
                    {
                        Cwd = tempDirectory.FullName,
                        SessionId = "session-123",
                        Timestamp = sharedStopTimestamp,
                        TranscriptPath = "/workspace/transcript.json",
                        Prompt = "Ship a current turn whose non-exact summary should deliver.",
                    },
                    AppJsonSerializerContext.Default.UserPromptSubmitHookInput),
                new MemoryStream(),
                CancellationToken.None);
            NotificationTurn currentTurn = Assert.Single(
                await stateStore.ListOpenTurnsAsync(
                    tempDirectory.FullName,
                    "session-123",
                    CancellationToken.None),
                turn => !string.Equals(
                    turn.NotificationTurnId,
                    oldTurn.NotificationTurnId,
                    StringComparison.Ordinal));
            await WriteSummaryAsync(
                tempDirectory.FullName,
                "session-123",
                currentTurn,
                new NotificationSummary
                {
                    SessionId = "session-123",
                    NotificationTurnId = currentTurn.NotificationTurnId,
                    NotificationNonce = currentTurn.NotificationNonce,
                    UpdatedAt = "2026-03-14T15:51:49.783Z",
                    Summary = "The current non-exact summary should deliver.",
                });

            _ = await service.HandleStopAsync(
                CreateJsonStream(
                    CreateStopInput(tempDirectory.FullName, sharedStopTimestamp),
                    AppJsonSerializerContext.Default.StopHookInput),
                new MemoryStream(),
                CancellationToken.None);

            Assert.Empty(handler.Requests);
            Assert.False(File.Exists(AppPaths.GetNotificationRecordPath(
                tempDirectory.FullName,
                "session-123",
                currentTurn.NotificationTurnId,
                CreateStopNotificationKeyForTest(sharedStopTimestamp))));
        }
        finally
        {
            tempDirectory.Delete(recursive: true);
        }
    }

    [Theory]
    [InlineData("missing")]
    [InlineData("locked")]
    [InlineData("invalid-json")]
    [InlineData("json-null")]
    public async Task HandleStopAsyncAbandonedPendingSameTimestampSuppressesCurrentPendingReplayWithoutRecordingCurrent(
        string currentSummaryState)
    {
        DirectoryInfo tempDirectory = Directory.CreateTempSubdirectory();
        using EnvironmentScope environment = SetTelegramEnvironment();

        try
        {
            WorkspaceStateStore stateStore = new(
                TimeProvider.System,
                NullLogger<WorkspaceStateStore>.Instance);
            NotificationTurn oldTurn = await CreateTurnAsync(
                stateStore,
                tempDirectory.FullName,
                "session-123",
                "2026-03-14T15:51:40.783Z");
            RecordingHttpMessageHandler handler = new();
            HookCommandService service = CreateHookCommandService(handler, stateStore);
            const string sharedStopTimestamp = "2026-03-14T15:51:50.783Z";
            await WriteSummaryAsync(
                tempDirectory.FullName,
                "session-123",
                oldTurn,
                new NotificationSummary
                {
                    SessionId = "session-123",
                    NotificationTurnId = oldTurn.NotificationTurnId,
                    NotificationNonce = oldTurn.NotificationNonce,
                    UpdatedAt = sharedStopTimestamp,
                    Status = "pending",
                    Summary = " ",
                });

            _ = await service.HandleStopAsync(
                CreateJsonStream(
                    CreateStopInput(tempDirectory.FullName, sharedStopTimestamp),
                    AppJsonSerializerContext.Default.StopHookInput),
                new MemoryStream(),
                CancellationToken.None);

            Assert.Empty(handler.Requests);
            await AssertPendingStopAsync(
                stateStore,
                tempDirectory.FullName,
                oldTurn,
                sharedStopTimestamp,
                "summary must be a non-empty human-readable sentence");

            _ = await service.HandleUserPromptSubmitAsync(
                CreateJsonStream(
                    new UserPromptSubmitHookInput
                    {
                        Cwd = tempDirectory.FullName,
                        SessionId = "session-123",
                        Timestamp = sharedStopTimestamp,
                        TranscriptPath = "/workspace/transcript.json",
                        Prompt = "Ship a current turn whose pending replay should be suppressed.",
                    },
                    AppJsonSerializerContext.Default.UserPromptSubmitHookInput),
                new MemoryStream(),
                CancellationToken.None);
            NotificationTurn currentTurn = Assert.Single(
                await stateStore.ListOpenTurnsAsync(
                    tempDirectory.FullName,
                    "session-123",
                    CancellationToken.None),
                turn => !string.Equals(
                    turn.NotificationTurnId,
                    oldTurn.NotificationTurnId,
                    StringComparison.Ordinal));
            string summaryPath = AppPaths.GetSummaryStatePath(
                tempDirectory.FullName,
                "session-123",
                currentTurn.NotificationTurnId);
            FileStream? lockedSummary = null;
            try
            {
                switch (currentSummaryState)
                {
                    case "missing":
                        File.Delete(summaryPath);
                        break;
                    case "locked":
                        lockedSummary = File.Open(
                            summaryPath,
                            FileMode.Open,
                            FileAccess.ReadWrite,
                            FileShare.None);
                        break;
                    case "invalid-json":
                        await WriteRawSummaryJsonAsync(
                            tempDirectory.FullName,
                            "session-123",
                            currentTurn,
                            "{");
                        break;
                    case "json-null":
                        await WriteRawSummaryJsonAsync(
                            tempDirectory.FullName,
                            "session-123",
                            currentTurn,
                            "null");
                        break;
                }

                _ = await service.HandleStopAsync(
                    CreateJsonStream(
                        CreateStopInput(tempDirectory.FullName, sharedStopTimestamp),
                        AppJsonSerializerContext.Default.StopHookInput),
                    new MemoryStream(),
                    CancellationToken.None);
            }
            finally
            {
                lockedSummary?.Dispose();
            }

            Assert.Empty(handler.Requests);
            string notificationKey = CreateStopNotificationKeyForTest(sharedStopTimestamp);
            Assert.False(File.Exists(AppPaths.GetStopObservationPath(
                tempDirectory.FullName,
                "session-123",
                currentTurn.NotificationTurnId,
                notificationKey)));
            Assert.False(File.Exists(AppPaths.GetNotificationRecordPath(
                tempDirectory.FullName,
                "session-123",
                currentTurn.NotificationTurnId,
                notificationKey)));
            NotificationTurn? storedCurrentTurn = await stateStore.TryReadTurnAsync(
                tempDirectory.FullName,
                "session-123",
                currentTurn.NotificationTurnId,
                CancellationToken.None);
            Assert.Equal("open", storedCurrentTurn?.Status);
        }
        finally
        {
            tempDirectory.Delete(recursive: true);
        }
    }

    [Theory]
    [InlineData(" ")]
    [InlineData(null)]
    public async Task HandleStopAsyncAbandonedPendingSameTimestampSuppressesCurrentExactBlankPendingSummary(
        string? currentSummary)
    {
        DirectoryInfo tempDirectory = Directory.CreateTempSubdirectory();
        using EnvironmentScope environment = SetTelegramEnvironment();

        try
        {
            WorkspaceStateStore stateStore = new(
                TimeProvider.System,
                NullLogger<WorkspaceStateStore>.Instance);
            NotificationTurn oldTurn = await CreateTurnAsync(
                stateStore,
                tempDirectory.FullName,
                "session-123",
                "2026-03-14T15:51:40.783Z");
            RecordingHttpMessageHandler handler = new();
            HookCommandService service = CreateHookCommandService(handler, stateStore);
            const string sharedStopTimestamp = "2026-03-14T15:51:50.783Z";
            await WriteSummaryAsync(
                tempDirectory.FullName,
                "session-123",
                oldTurn,
                new NotificationSummary
                {
                    SessionId = "session-123",
                    NotificationTurnId = oldTurn.NotificationTurnId,
                    NotificationNonce = oldTurn.NotificationNonce,
                    UpdatedAt = sharedStopTimestamp,
                    Status = "pending",
                    Summary = " ",
                });

            _ = await service.HandleStopAsync(
                CreateJsonStream(
                    CreateStopInput(tempDirectory.FullName, sharedStopTimestamp),
                    AppJsonSerializerContext.Default.StopHookInput),
                new MemoryStream(),
                CancellationToken.None);

            Assert.Empty(handler.Requests);
            await AssertPendingStopAsync(
                stateStore,
                tempDirectory.FullName,
                oldTurn,
                sharedStopTimestamp,
                "summary must be a non-empty human-readable sentence");

            _ = await service.HandleUserPromptSubmitAsync(
                CreateJsonStream(
                    new UserPromptSubmitHookInput
                    {
                        Cwd = tempDirectory.FullName,
                        SessionId = "session-123",
                        Timestamp = sharedStopTimestamp,
                        TranscriptPath = "/workspace/transcript.json",
                        Prompt = "Ship a current exact blank pending summary.",
                    },
                    AppJsonSerializerContext.Default.UserPromptSubmitHookInput),
                new MemoryStream(),
                CancellationToken.None);
            NotificationTurn currentTurn = Assert.Single(
                await stateStore.ListOpenTurnsAsync(
                    tempDirectory.FullName,
                    "session-123",
                    CancellationToken.None),
                turn => !string.Equals(
                    turn.NotificationTurnId,
                    oldTurn.NotificationTurnId,
                    StringComparison.Ordinal));
            await WriteSummaryAsync(
                tempDirectory.FullName,
                "session-123",
                currentTurn,
                new NotificationSummary
                {
                    SessionId = "session-123",
                    NotificationTurnId = currentTurn.NotificationTurnId,
                    NotificationNonce = currentTurn.NotificationNonce,
                    UpdatedAt = sharedStopTimestamp,
                    Status = "pending",
                    Summary = currentSummary,
                });

            _ = await service.HandleStopAsync(
                CreateJsonStream(
                    CreateStopInput(tempDirectory.FullName, sharedStopTimestamp),
                    AppJsonSerializerContext.Default.StopHookInput),
                new MemoryStream(),
                CancellationToken.None);

            Assert.Empty(handler.Requests);
            Assert.False(File.Exists(AppPaths.GetStopObservationPath(
                tempDirectory.FullName,
                "session-123",
                currentTurn.NotificationTurnId,
                CreateStopNotificationKeyForTest(sharedStopTimestamp))));
        }
        finally
        {
            tempDirectory.Delete(recursive: true);
        }
    }

    [Fact]
    public async Task HandleStopAsyncFilledOldAbandonedPendingTurnWithoutExactAttributionSendsFallback()
    {
        DirectoryInfo tempDirectory = Directory.CreateTempSubdirectory();
        using EnvironmentScope environment = SetTelegramEnvironment();

        try
        {
            WorkspaceStateStore stateStore = new(
                TimeProvider.System,
                NullLogger<WorkspaceStateStore>.Instance);
            NotificationTurn oldTurn = await CreateTurnAsync(
                stateStore,
                tempDirectory.FullName,
                "session-123",
                "2026-03-14T15:51:40.783Z");
            RecordingHttpMessageHandler handler = new();
            HookCommandService service = CreateHookCommandService(handler, stateStore);
            const string oldStopTimestamp = "2026-03-14T15:51:50.783Z";

            _ = await service.HandleStopAsync(
                CreateJsonStream(
                    CreateStopInput(tempDirectory.FullName, oldStopTimestamp),
                    AppJsonSerializerContext.Default.StopHookInput),
                new MemoryStream(),
                CancellationToken.None);

            Assert.Empty(handler.Requests);
            await AssertPendingStopAsync(
                stateStore,
                tempDirectory.FullName,
                oldTurn,
                oldStopTimestamp,
                "summary must be a non-empty human-readable sentence");

            _ = await service.HandleUserPromptSubmitAsync(
                CreateJsonStream(
                    new UserPromptSubmitHookInput
                    {
                        Cwd = tempDirectory.FullName,
                        SessionId = "session-123",
                        Timestamp = "2026-03-14T15:52:40.783Z",
                        TranscriptPath = "/workspace/transcript.json",
                        Prompt = "Ship a superseding turn.",
                    },
                    AppJsonSerializerContext.Default.UserPromptSubmitHookInput),
                new MemoryStream(),
                CancellationToken.None);
            NotificationTurn newTurn = Assert.Single(await stateStore.ListOpenTurnsAsync(
                tempDirectory.FullName,
                "session-123",
                CancellationToken.None));
            await WriteSummaryAsync(
                tempDirectory.FullName,
                "session-123",
                oldTurn,
                new NotificationSummary
                {
                    SessionId = "session-123",
                    NotificationTurnId = oldTurn.NotificationTurnId,
                    NotificationNonce = oldTurn.NotificationNonce,
                    UpdatedAt = "2026-03-14T15:52:50.783Z",
                    Summary = "The old pending summary was filled without exact Stop attribution.",
                });

            _ = await service.HandleStopAsync(
                CreateJsonStream(
                    CreateStopInput(tempDirectory.FullName, oldStopTimestamp),
                    AppJsonSerializerContext.Default.StopHookInput),
                new MemoryStream(),
                CancellationToken.None);

            TelegramSendMessageRequest fallbackPayload = DeserializeTelegramPayload(
                Assert.Single(handler.Requests));
            Assert.Contains("摘要：当前轮未生成摘要。", fallbackPayload.Text, StringComparison.Ordinal);
            NotificationTurn? storedNewTurn = await stateStore.TryReadTurnAsync(
                tempDirectory.FullName,
                "session-123",
                newTurn.NotificationTurnId,
                CancellationToken.None);
            Assert.Equal("open", storedNewTurn?.Status);
        }
        finally
        {
            tempDirectory.Delete(recursive: true);
        }
    }

    [Theory]
    [InlineData("completed", null)]
    [InlineData("completed", " ")]
    [InlineData(null, null)]
    public async Task HandleStopAsyncStaleAbandonedPendingObservationDoesNotSuppressNonPendingInvalidSummary(
        string? summaryStatus,
        string? summaryText)
    {
        DirectoryInfo tempDirectory = Directory.CreateTempSubdirectory();
        using EnvironmentScope environment = SetTelegramEnvironment();

        try
        {
            WorkspaceStateStore stateStore = new(
                TimeProvider.System,
                NullLogger<WorkspaceStateStore>.Instance);
            NotificationTurn oldTurn = await CreateTurnAsync(
                stateStore,
                tempDirectory.FullName,
                "session-123",
                "2026-03-14T15:51:40.783Z");
            RecordingHttpMessageHandler handler = new();
            HookCommandService service = CreateHookCommandService(handler, stateStore);
            const string stopTimestamp = "2026-03-14T15:51:50.783Z";

            _ = await service.HandleStopAsync(
                CreateJsonStream(
                    CreateStopInput(tempDirectory.FullName, stopTimestamp),
                    AppJsonSerializerContext.Default.StopHookInput),
                new MemoryStream(),
                CancellationToken.None);

            Assert.Empty(handler.Requests);
            oldTurn.Status = "abandoned";
            await WriteTurnStateAsync(tempDirectory.FullName, oldTurn);
            await WriteSummaryAsync(
                tempDirectory.FullName,
                "session-123",
                oldTurn,
                new NotificationSummary
                {
                    SessionId = "session-123",
                    NotificationTurnId = oldTurn.NotificationTurnId,
                    NotificationNonce = oldTurn.NotificationNonce,
                    UpdatedAt = stopTimestamp,
                    Status = summaryStatus,
                    Summary = summaryText,
                });

            _ = await service.HandleStopAsync(
                CreateJsonStream(
                    CreateStopInput(tempDirectory.FullName, stopTimestamp),
                    AppJsonSerializerContext.Default.StopHookInput),
                new MemoryStream(),
                CancellationToken.None);

            TelegramSendMessageRequest payload = DeserializeTelegramPayload(
                Assert.Single(handler.Requests));
            Assert.Contains("摘要：当前轮未生成摘要。", payload.Text, StringComparison.Ordinal);
            Assert.True(File.Exists(AppPaths.GetSessionNotificationRecordPath(
                tempDirectory.FullName,
                "session-123",
                CreateStopNotificationKeyForTest(stopTimestamp))));
        }
        finally
        {
            tempDirectory.Delete(recursive: true);
        }
    }

    [Fact]
    public async Task HandleStopAsyncStaleAbandonedPendingObservationDoesNotSuppressDifferentPendingStop()
    {
        DirectoryInfo tempDirectory = Directory.CreateTempSubdirectory();
        using EnvironmentScope environment = SetTelegramEnvironment();

        try
        {
            WorkspaceStateStore stateStore = new(
                TimeProvider.System,
                NullLogger<WorkspaceStateStore>.Instance);
            NotificationTurn oldTurn = await CreateTurnAsync(
                stateStore,
                tempDirectory.FullName,
                "session-123",
                "2026-03-14T15:51:40.783Z");
            RecordingHttpMessageHandler handler = new();
            HookCommandService service = CreateHookCommandService(handler, stateStore);
            const string stopTimestamp = "2026-03-14T15:51:50.783Z";
            const string differentPendingTimestamp = "2026-03-14T15:51:51.783Z";

            await WriteSummaryAsync(
                tempDirectory.FullName,
                "session-123",
                oldTurn,
                new NotificationSummary
                {
                    SessionId = "session-123",
                    NotificationTurnId = oldTurn.NotificationTurnId,
                    NotificationNonce = oldTurn.NotificationNonce,
                    UpdatedAt = stopTimestamp,
                    Status = "pending",
                    Summary = " ",
                });

            _ = await service.HandleStopAsync(
                CreateJsonStream(
                    CreateStopInput(tempDirectory.FullName, stopTimestamp),
                    AppJsonSerializerContext.Default.StopHookInput),
                new MemoryStream(),
                CancellationToken.None);

            Assert.Empty(handler.Requests);
            oldTurn.Status = "abandoned";
            await WriteTurnStateAsync(tempDirectory.FullName, oldTurn);
            await WriteSummaryAsync(
                tempDirectory.FullName,
                "session-123",
                oldTurn,
                new NotificationSummary
                {
                    SessionId = "session-123",
                    NotificationTurnId = oldTurn.NotificationTurnId,
                    NotificationNonce = oldTurn.NotificationNonce,
                    UpdatedAt = differentPendingTimestamp,
                    Status = "pending",
                    Summary = " ",
                });

            _ = await service.HandleStopAsync(
                CreateJsonStream(
                    CreateStopInput(tempDirectory.FullName, stopTimestamp),
                    AppJsonSerializerContext.Default.StopHookInput),
                new MemoryStream(),
                CancellationToken.None);

            TelegramSendMessageRequest payload = DeserializeTelegramPayload(
                Assert.Single(handler.Requests));
            Assert.Contains("摘要：当前轮未生成摘要。", payload.Text, StringComparison.Ordinal);
            Assert.True(File.Exists(AppPaths.GetSessionNotificationRecordPath(
                tempDirectory.FullName,
                "session-123",
                CreateStopNotificationKeyForTest(stopTimestamp))));
        }
        finally
        {
            tempDirectory.Delete(recursive: true);
        }
    }

    [Fact]
    public async Task HandleStopAsyncStaleAbandonedPendingObservationDoesNotSuppressAfterInterveningSessionDelivery()
    {
        DirectoryInfo tempDirectory = Directory.CreateTempSubdirectory();
        using EnvironmentScope environment = SetTelegramEnvironment();

        try
        {
            WorkspaceStateStore stateStore = new(
                TimeProvider.System,
                NullLogger<WorkspaceStateStore>.Instance);
            NotificationTurn oldTurn = await CreateTurnAsync(
                stateStore,
                tempDirectory.FullName,
                "session-123",
                "2026-03-14T15:51:40.783Z");
            File.Delete(AppPaths.GetSummaryStatePath(
                tempDirectory.FullName,
                "session-123",
                oldTurn.NotificationTurnId));
            RecordingHttpMessageHandler handler = new();
            HookCommandService service = CreateHookCommandService(handler, stateStore);
            const string stopTimestamp = "2026-03-14T15:53:50.783Z";

            _ = await service.HandleStopAsync(
                CreateJsonStream(
                    CreateStopInput(tempDirectory.FullName, stopTimestamp),
                    AppJsonSerializerContext.Default.StopHookInput),
                new MemoryStream(),
                CancellationToken.None);

            Assert.Empty(handler.Requests);
            await AssertPendingStopAsync(
                stateStore,
                tempDirectory.FullName,
                oldTurn,
                stopTimestamp,
                "Summary file is missing");

            const string interveningStopTimestamp = "2026-03-14T15:52:50.783Z";
            string interveningNotificationKey = CreateStopNotificationKeyForTest(interveningStopTimestamp);
            await WriteNotificationRecordAsync(
                AppPaths.GetSessionNotificationRecordPath(
                    tempDirectory.FullName,
                    "session-123",
                    interveningNotificationKey),
                new NotificationRecord
                {
                    SessionId = "session-123",
                    NotificationKey = interveningNotificationKey,
                    WorkspacePath = tempDirectory.FullName,
                    StopTimestamp = interveningStopTimestamp,
                    SentAt = "2026-03-14T15:52:51.783Z",
                    Degraded = true,
                    DeliveryStatus = "sent",
                });

            NotificationTurn currentTurn = await CreateTurnAsync(
                stateStore,
                tempDirectory.FullName,
                "session-123",
                "2026-03-14T15:53:40.783Z");
            await WriteSummaryAsync(
                tempDirectory.FullName,
                "session-123",
                currentTurn,
                new NotificationSummary
                {
                    SessionId = "session-123",
                    NotificationTurnId = currentTurn.NotificationTurnId,
                    NotificationNonce = currentTurn.NotificationNonce,
                    UpdatedAt = "2026-03-14T15:53:49.783Z",
                    Summary = "The current non-exact delivery is not blocked by stale pending observation.",
                });

            _ = await service.HandleStopAsync(
                CreateJsonStream(
                    CreateStopInput(tempDirectory.FullName, stopTimestamp),
                    AppJsonSerializerContext.Default.StopHookInput),
                new MemoryStream(),
                CancellationToken.None);

            TelegramSendMessageRequest payload = DeserializeTelegramPayload(
                Assert.Single(handler.Requests));
            Assert.Contains(
                "摘要：The current non-exact delivery is not blocked by stale pending observation.",
                payload.Text,
                StringComparison.Ordinal);
            Assert.Contains(currentTurn.NotificationTurnId, payload.Text, StringComparison.Ordinal);
            Assert.DoesNotContain(oldTurn.NotificationTurnId, payload.Text, StringComparison.Ordinal);
        }
        finally
        {
            tempDirectory.Delete(recursive: true);
        }
    }

    [Theory]
    [InlineData(" ")]
    [InlineData(null)]
    public async Task HandleStopAsyncAbandonedExactPendingDoesNotSuppressFallbackAfterInterveningSessionDelivery(
        string? pendingSummary)
    {
        DirectoryInfo tempDirectory = Directory.CreateTempSubdirectory();
        using EnvironmentScope environment = SetTelegramEnvironment();

        try
        {
            WorkspaceStateStore stateStore = new(
                TimeProvider.System,
                NullLogger<WorkspaceStateStore>.Instance);
            RecordingHttpMessageHandler handler = new();
            HookCommandService service = CreateHookCommandService(handler, stateStore);
            const string stopTimestamp = "2026-03-14T15:53:50.783Z";
            NotificationTurn oldTurn = await CreateTurnAsync(
                stateStore,
                tempDirectory.FullName,
                "session-123",
                "2026-03-14T15:51:40.783Z");
            await WriteSummaryAsync(
                tempDirectory.FullName,
                "session-123",
                oldTurn,
                new NotificationSummary
                {
                    SessionId = "session-123",
                    NotificationTurnId = oldTurn.NotificationTurnId,
                    NotificationNonce = oldTurn.NotificationNonce,
                    UpdatedAt = stopTimestamp,
                    Status = "pending",
                    Summary = pendingSummary,
                });

            _ = await service.HandleStopAsync(
                CreateJsonStream(
                    CreateStopInput(tempDirectory.FullName, stopTimestamp),
                    AppJsonSerializerContext.Default.StopHookInput),
                new MemoryStream(),
                CancellationToken.None);
            Assert.Empty(handler.Requests);
            await AssertPendingStopAsync(
                stateStore,
                tempDirectory.FullName,
                oldTurn,
                stopTimestamp,
                "summary must be a non-empty human-readable sentence");

            oldTurn.Status = "abandoned";
            oldTurn.UpdatedAt = "2026-03-14T15:51:41.783Z";
            await WriteTurnStateAsync(tempDirectory.FullName, oldTurn);
            const string interveningStopTimestamp = "2026-03-14T15:52:50.783Z";
            string interveningNotificationKey = CreateStopNotificationKeyForTest(interveningStopTimestamp);
            await WriteNotificationRecordAsync(
                AppPaths.GetSessionNotificationRecordPath(
                    tempDirectory.FullName,
                    "session-123",
                    interveningNotificationKey),
                new NotificationRecord
                {
                    SessionId = "session-123",
                    NotificationKey = interveningNotificationKey,
                    WorkspacePath = tempDirectory.FullName,
                    StopTimestamp = interveningStopTimestamp,
                    SentAt = "2026-03-14T15:52:51.783Z",
                    Degraded = true,
                    DeliveryStatus = "sent",
                });

            _ = await service.HandleStopAsync(
                CreateJsonStream(
                    CreateStopInput(tempDirectory.FullName, stopTimestamp),
                    AppJsonSerializerContext.Default.StopHookInput),
                new MemoryStream(),
                CancellationToken.None);

            TelegramSendMessageRequest payload = DeserializeTelegramPayload(
                Assert.Single(handler.Requests));
            Assert.Contains("摘要：当前轮未生成摘要。", payload.Text, StringComparison.Ordinal);
            Assert.DoesNotContain(oldTurn.NotificationTurnId, payload.Text, StringComparison.Ordinal);
        }
        finally
        {
            tempDirectory.Delete(recursive: true);
        }
    }

    [Theory]
    [InlineData(" ")]
    [InlineData(null)]
    public async Task HandleStopAsyncAbandonedExactPendingDoesNotSuppressFallbackAfterInterveningPerTurnDelivery(
        string? pendingSummary)
    {
        DirectoryInfo tempDirectory = Directory.CreateTempSubdirectory();
        using EnvironmentScope environment = SetTelegramEnvironment();

        try
        {
            WorkspaceStateStore stateStore = new(
                TimeProvider.System,
                NullLogger<WorkspaceStateStore>.Instance);
            RecordingHttpMessageHandler handler = new();
            HookCommandService service = CreateHookCommandService(handler, stateStore);
            const string stopTimestamp = "2026-03-14T15:53:50.783Z";
            NotificationTurn oldTurn = await CreateTurnAsync(
                stateStore,
                tempDirectory.FullName,
                "session-123",
                "2026-03-14T15:51:40.783Z");
            await WriteSummaryAsync(
                tempDirectory.FullName,
                "session-123",
                oldTurn,
                new NotificationSummary
                {
                    SessionId = "session-123",
                    NotificationTurnId = oldTurn.NotificationTurnId,
                    NotificationNonce = oldTurn.NotificationNonce,
                    UpdatedAt = stopTimestamp,
                    Status = "pending",
                    Summary = pendingSummary,
                });

            _ = await service.HandleStopAsync(
                CreateJsonStream(
                    CreateStopInput(tempDirectory.FullName, stopTimestamp),
                    AppJsonSerializerContext.Default.StopHookInput),
                new MemoryStream(),
                CancellationToken.None);
            Assert.Empty(handler.Requests);
            await AssertPendingStopAsync(
                stateStore,
                tempDirectory.FullName,
                oldTurn,
                stopTimestamp,
                "summary must be a non-empty human-readable sentence");

            oldTurn.Status = "abandoned";
            oldTurn.UpdatedAt = "2026-03-14T15:51:41.783Z";
            await WriteTurnStateAsync(tempDirectory.FullName, oldTurn);
            NotificationTurn interveningTurn = await CreateTurnAsync(
                stateStore,
                tempDirectory.FullName,
                "session-123",
                "2026-03-14T15:52:40.783Z");
            interveningTurn.Status = "notified";
            await WriteTurnStateAsync(tempDirectory.FullName, interveningTurn);
            const string interveningStopTimestamp = "2026-03-14T15:52:50.783Z";
            string interveningNotificationKey = CreateStopNotificationKeyForTest(interveningStopTimestamp);
            await WriteNotificationRecordAsync(
                AppPaths.GetNotificationRecordPath(
                    tempDirectory.FullName,
                    "session-123",
                    interveningTurn.NotificationTurnId,
                    interveningNotificationKey),
                new NotificationRecord
                {
                    SessionId = "session-123",
                    NotificationTurnId = interveningTurn.NotificationTurnId,
                    NotificationKey = interveningNotificationKey,
                    WorkspacePath = tempDirectory.FullName,
                    StopTimestamp = interveningStopTimestamp,
                    SentAt = "2026-03-14T15:52:51.783Z",
                    SummaryUpdatedAt = interveningStopTimestamp,
                    DeliveryStatus = "sent",
                });

            _ = await service.HandleStopAsync(
                CreateJsonStream(
                    CreateStopInput(tempDirectory.FullName, stopTimestamp),
                    AppJsonSerializerContext.Default.StopHookInput),
                new MemoryStream(),
                CancellationToken.None);

            TelegramSendMessageRequest payload = DeserializeTelegramPayload(
                Assert.Single(handler.Requests));
            Assert.Contains("摘要：当前轮未生成摘要。", payload.Text, StringComparison.Ordinal);
            Assert.DoesNotContain(oldTurn.NotificationTurnId, payload.Text, StringComparison.Ordinal);
            Assert.False(File.Exists(AppPaths.GetSessionNotificationRecordPath(
                tempDirectory.FullName,
                "session-123",
                interveningNotificationKey)));
        }
        finally
        {
            tempDirectory.Delete(recursive: true);
        }
    }

    [Theory]
    [InlineData("sent")]
    [InlineData("partial")]
    public async Task HandleStopAsyncNoOpenDelayedFallbackSuppressedByPriorPerTurnDurableDelivery(
        string deliveryStatus)
    {
        DirectoryInfo tempDirectory = Directory.CreateTempSubdirectory();
        using EnvironmentScope environment = SetTelegramEnvironment();

        try
        {
            WorkspaceStateStore stateStore = new(
                TimeProvider.System,
                NullLogger<WorkspaceStateStore>.Instance);
            NotificationTurn turn = await CreateTurnAsync(
                stateStore,
                tempDirectory.FullName,
                "session-123",
                "2026-03-14T15:51:40.783Z");
            turn.Status = "notified";
            await WriteTurnStateAsync(tempDirectory.FullName, turn);
            const string priorStopTimestamp = "2026-03-14T15:51:50.783Z";
            string priorNotificationKey = CreateStopNotificationKeyForTest(priorStopTimestamp);
            await WriteNotificationRecordAsync(
                AppPaths.GetNotificationRecordPath(
                    tempDirectory.FullName,
                    "session-123",
                    turn.NotificationTurnId,
                    priorNotificationKey),
                new NotificationRecord
                {
                    SessionId = "session-123",
                    NotificationTurnId = turn.NotificationTurnId,
                    NotificationKey = priorNotificationKey,
                    WorkspacePath = tempDirectory.FullName,
                    StopTimestamp = priorStopTimestamp,
                    SentAt = "2026-03-14T15:51:51.783Z",
                    SummaryUpdatedAt = priorStopTimestamp,
                    DeliveryStatus = deliveryStatus,
                    SuccessfulMessageCount = string.Equals(
                        deliveryStatus,
                        "partial",
                        StringComparison.Ordinal)
                            ? 1
                            : null,
                });
            RecordingHttpMessageHandler handler = new();
            HookCommandService service = CreateHookCommandService(handler, stateStore);
            const string delayedStopTimestamp = "2026-03-14T15:52:00.783Z";

            _ = await service.HandleStopAsync(
                CreateJsonStream(
                    CreateStopInput(tempDirectory.FullName, delayedStopTimestamp),
                    AppJsonSerializerContext.Default.StopHookInput),
                new MemoryStream(),
                CancellationToken.None);

            Assert.Empty(handler.Requests);
            Assert.False(File.Exists(AppPaths.GetSessionNotificationRecordPath(
                tempDirectory.FullName,
                "session-123",
                CreateStopNotificationKeyForTest(delayedStopTimestamp))));
            Assert.False(File.Exists(AppPaths.GetNotificationRecordPath(
                tempDirectory.FullName,
                "session-123",
                turn.NotificationTurnId,
                CreateStopNotificationKeyForTest(delayedStopTimestamp))));
        }
        finally
        {
            tempDirectory.Delete(recursive: true);
        }
    }

    [Fact]
    public async Task HandleStopAsyncStaleAbandonedPendingObservationDoesNotSuppressAfterInterveningPerTurnDelivery()
    {
        DirectoryInfo tempDirectory = Directory.CreateTempSubdirectory();
        using EnvironmentScope environment = SetTelegramEnvironment();

        try
        {
            WorkspaceStateStore stateStore = new(
                TimeProvider.System,
                NullLogger<WorkspaceStateStore>.Instance);
            NotificationTurn oldTurn = await CreateTurnAsync(
                stateStore,
                tempDirectory.FullName,
                "session-123",
                "2026-03-14T15:51:40.783Z");
            File.Delete(AppPaths.GetSummaryStatePath(
                tempDirectory.FullName,
                "session-123",
                oldTurn.NotificationTurnId));
            RecordingHttpMessageHandler handler = new();
            HookCommandService service = CreateHookCommandService(handler, stateStore);
            const string stopTimestamp = "2026-03-14T15:53:50.783Z";

            _ = await service.HandleStopAsync(
                CreateJsonStream(
                    CreateStopInput(tempDirectory.FullName, stopTimestamp),
                    AppJsonSerializerContext.Default.StopHookInput),
                new MemoryStream(),
                CancellationToken.None);

            Assert.Empty(handler.Requests);
            await AssertPendingStopAsync(
                stateStore,
                tempDirectory.FullName,
                oldTurn,
                stopTimestamp,
                "Summary file is missing");

            oldTurn.Status = "abandoned";
            oldTurn.UpdatedAt = "2026-03-14T15:51:41.783Z";
            await WriteTurnStateAsync(tempDirectory.FullName, oldTurn);
            NotificationTurn interveningTurn = await CreateTurnAsync(
                stateStore,
                tempDirectory.FullName,
                "session-123",
                "2026-03-14T15:52:40.783Z");
            interveningTurn.Status = "notified";
            await WriteTurnStateAsync(tempDirectory.FullName, interveningTurn);
            const string interveningStopTimestamp = "2026-03-14T15:52:50.783Z";
            string interveningNotificationKey = CreateStopNotificationKeyForTest(interveningStopTimestamp);
            await WriteNotificationRecordAsync(
                AppPaths.GetNotificationRecordPath(
                    tempDirectory.FullName,
                    "session-123",
                    interveningTurn.NotificationTurnId,
                    interveningNotificationKey),
                new NotificationRecord
                {
                    SessionId = "session-123",
                    NotificationTurnId = interveningTurn.NotificationTurnId,
                    NotificationKey = interveningNotificationKey,
                    WorkspacePath = tempDirectory.FullName,
                    StopTimestamp = interveningStopTimestamp,
                    SentAt = "2026-03-14T15:52:51.783Z",
                    SummaryUpdatedAt = interveningStopTimestamp,
                    DeliveryStatus = "sent",
                });

            NotificationTurn currentTurn = await CreateTurnAsync(
                stateStore,
                tempDirectory.FullName,
                "session-123",
                "2026-03-14T15:53:40.783Z");
            await WriteSummaryAsync(
                tempDirectory.FullName,
                "session-123",
                currentTurn,
                new NotificationSummary
                {
                    SessionId = "session-123",
                    NotificationTurnId = currentTurn.NotificationTurnId,
                    NotificationNonce = currentTurn.NotificationNonce,
                    UpdatedAt = "2026-03-14T15:53:49.783Z",
                    Summary = "The current non-exact delivery is not blocked by stale pending observation.",
                });

            _ = await service.HandleStopAsync(
                CreateJsonStream(
                    CreateStopInput(tempDirectory.FullName, stopTimestamp),
                    AppJsonSerializerContext.Default.StopHookInput),
                new MemoryStream(),
                CancellationToken.None);

            TelegramSendMessageRequest payload = DeserializeTelegramPayload(
                Assert.Single(handler.Requests));
            Assert.Contains(
                "摘要：The current non-exact delivery is not blocked by stale pending observation.",
                payload.Text,
                StringComparison.Ordinal);
            Assert.Contains(currentTurn.NotificationTurnId, payload.Text, StringComparison.Ordinal);
            Assert.DoesNotContain(oldTurn.NotificationTurnId, payload.Text, StringComparison.Ordinal);
            Assert.False(File.Exists(AppPaths.GetSessionNotificationRecordPath(
                tempDirectory.FullName,
                "session-123",
                interveningNotificationKey)));
        }
        finally
        {
            tempDirectory.Delete(recursive: true);
        }
    }

    [Fact]
    public async Task HandleStopAsyncAbandonedCompletedExactDoesNotRecoverAfterInterveningSessionDelivery()
    {
        DirectoryInfo tempDirectory = Directory.CreateTempSubdirectory();
        using EnvironmentScope environment = SetTelegramEnvironment();

        try
        {
            WorkspaceStateStore stateStore = new(
                TimeProvider.System,
                NullLogger<WorkspaceStateStore>.Instance);
            RecordingHttpMessageHandler handler = new();
            HookCommandService service = CreateHookCommandService(handler, stateStore);
            const string stopTimestamp = "2026-03-14T15:53:50.783Z";
            NotificationTurn oldTurn = await CreateTurnAsync(
                stateStore,
                tempDirectory.FullName,
                "session-123",
                "2026-03-14T15:51:40.783Z");
            await WriteSummaryAsync(
                tempDirectory.FullName,
                "session-123",
                oldTurn,
                new NotificationSummary
                {
                    SessionId = "session-123",
                    NotificationTurnId = oldTurn.NotificationTurnId,
                    NotificationNonce = oldTurn.NotificationNonce,
                    UpdatedAt = stopTimestamp,
                    Status = "completed",
                    Summary = "The stale abandoned completed exact summary must not recover.",
                });
            oldTurn.Status = "abandoned";
            oldTurn.UpdatedAt = "2026-03-14T15:51:41.783Z";
            await WriteTurnStateAsync(tempDirectory.FullName, oldTurn);
            const string interveningStopTimestamp = "2026-03-14T15:52:50.783Z";
            string interveningNotificationKey = CreateStopNotificationKeyForTest(interveningStopTimestamp);
            await WriteNotificationRecordAsync(
                AppPaths.GetSessionNotificationRecordPath(
                    tempDirectory.FullName,
                    "session-123",
                    interveningNotificationKey),
                new NotificationRecord
                {
                    SessionId = "session-123",
                    NotificationKey = interveningNotificationKey,
                    WorkspacePath = tempDirectory.FullName,
                    StopTimestamp = interveningStopTimestamp,
                    SentAt = "2026-03-14T15:52:51.783Z",
                    Degraded = true,
                    DeliveryStatus = "sent",
                });

            _ = await service.HandleStopAsync(
                CreateJsonStream(
                    CreateStopInput(tempDirectory.FullName, stopTimestamp),
                    AppJsonSerializerContext.Default.StopHookInput),
                new MemoryStream(),
                CancellationToken.None);

            TelegramSendMessageRequest payload = DeserializeTelegramPayload(
                Assert.Single(handler.Requests));
            Assert.Contains("摘要：当前轮未生成摘要。", payload.Text, StringComparison.Ordinal);
            Assert.DoesNotContain(
                "The stale abandoned completed exact summary must not recover.",
                payload.Text,
                StringComparison.Ordinal);
            Assert.DoesNotContain(oldTurn.NotificationTurnId, payload.Text, StringComparison.Ordinal);
        }
        finally
        {
            tempDirectory.Delete(recursive: true);
        }
    }

    [Fact]
    public async Task HandleStopAsyncAbandonedCompletedExactDoesNotRecoverAfterInterveningPerTurnDelivery()
    {
        DirectoryInfo tempDirectory = Directory.CreateTempSubdirectory();
        using EnvironmentScope environment = SetTelegramEnvironment();

        try
        {
            WorkspaceStateStore stateStore = new(
                TimeProvider.System,
                NullLogger<WorkspaceStateStore>.Instance);
            RecordingHttpMessageHandler handler = new();
            HookCommandService service = CreateHookCommandService(handler, stateStore);
            const string stopTimestamp = "2026-03-14T15:52:54.783Z";
            NotificationTurn oldTurn = await CreateTurnAsync(
                stateStore,
                tempDirectory.FullName,
                "session-123",
                "2026-03-14T15:51:40.783Z");
            await WriteSummaryAsync(
                tempDirectory.FullName,
                "session-123",
                oldTurn,
                new NotificationSummary
                {
                    SessionId = "session-123",
                    NotificationTurnId = oldTurn.NotificationTurnId,
                    NotificationNonce = oldTurn.NotificationNonce,
                    UpdatedAt = stopTimestamp,
                    Status = "completed",
                    Summary = "The stale abandoned completed exact summary must not recover.",
                });
            oldTurn.Status = "abandoned";
            oldTurn.UpdatedAt = "2026-03-14T15:51:41.783Z";
            await WriteTurnStateAsync(tempDirectory.FullName, oldTurn);
            NotificationTurn interveningTurn = await CreateTurnAsync(
                stateStore,
                tempDirectory.FullName,
                "session-123",
                "2026-03-14T15:52:40.783Z");
            interveningTurn.Status = "notified";
            await WriteTurnStateAsync(tempDirectory.FullName, interveningTurn);
            const string interveningStopTimestamp = "2026-03-14T15:52:51.783Z";
            string interveningNotificationKey = CreateStopNotificationKeyForTest(interveningStopTimestamp);
            await WriteNotificationRecordAsync(
                AppPaths.GetNotificationRecordPath(
                    tempDirectory.FullName,
                    "session-123",
                    interveningTurn.NotificationTurnId,
                    interveningNotificationKey),
                new NotificationRecord
                {
                    SessionId = "session-123",
                    NotificationTurnId = interveningTurn.NotificationTurnId,
                    NotificationKey = interveningNotificationKey,
                    WorkspacePath = tempDirectory.FullName,
                    StopTimestamp = interveningStopTimestamp,
                    SentAt = "2026-03-14T15:52:52.783Z",
                    SummaryUpdatedAt = interveningStopTimestamp,
                    DeliveryStatus = "sent",
                });

            _ = await service.HandleStopAsync(
                CreateJsonStream(
                    CreateStopInput(tempDirectory.FullName, stopTimestamp),
                    AppJsonSerializerContext.Default.StopHookInput),
                new MemoryStream(),
                CancellationToken.None);

            Assert.Empty(handler.Requests);
            Assert.False(File.Exists(AppPaths.GetSessionNotificationRecordPath(
                tempDirectory.FullName,
                "session-123",
                CreateStopNotificationKeyForTest(stopTimestamp))));
            Assert.False(File.Exists(AppPaths.GetNotificationRecordPath(
                tempDirectory.FullName,
                "session-123",
                oldTurn.NotificationTurnId,
                CreateStopNotificationKeyForTest(stopTimestamp))));
        }
        finally
        {
            tempDirectory.Delete(recursive: true);
        }
    }

    [Fact]
    public async Task HandleStopAsyncCurrentPendingDefersDespiteOlderValidSummary()
    {
        DirectoryInfo tempDirectory = Directory.CreateTempSubdirectory();
        using EnvironmentScope environment = SetTelegramEnvironment();

        try
        {
            WorkspaceStateStore stateStore = new(
                TimeProvider.System,
                NullLogger<WorkspaceStateStore>.Instance);
            NotificationTurn oldTurn = await CreateTurnAsync(
                stateStore,
                tempDirectory.FullName,
                "session-123",
                "2026-03-14T15:51:40.783Z");
            await WriteSummaryAsync(
                tempDirectory.FullName,
                "session-123",
                oldTurn,
                new NotificationSummary
                {
                    SessionId = "session-123",
                    NotificationTurnId = oldTurn.NotificationTurnId,
                    NotificationNonce = oldTurn.NotificationNonce,
                    UpdatedAt = "2026-03-14T15:51:50.783Z",
                    Summary = "The old completed turn should not steal the new pending Stop.",
                });
            RecordingHttpMessageHandler handler = new();
            HookCommandService service = CreateHookCommandService(handler, stateStore);

            _ = await service.HandleUserPromptSubmitAsync(
                CreateJsonStream(
                    new UserPromptSubmitHookInput
                    {
                        Cwd = tempDirectory.FullName,
                        SessionId = "session-123",
                        Timestamp = "2026-03-14T15:52:40.783Z",
                        TranscriptPath = "/workspace/transcript.json",
                        Prompt = "Start a current turn whose summary is still pending.",
                    },
                    AppJsonSerializerContext.Default.UserPromptSubmitHookInput),
                new MemoryStream(),
                CancellationToken.None);
            NotificationTurn currentTurn = Assert.Single(
                await stateStore.ListOpenTurnsAsync(
                    tempDirectory.FullName,
                    "session-123",
                    CancellationToken.None),
                turn => !string.Equals(
                    turn.NotificationTurnId,
                    oldTurn.NotificationTurnId,
                    StringComparison.Ordinal));
            const string currentStopTimestamp = "2026-03-14T15:52:50.783Z";

            _ = await service.HandleStopAsync(
                CreateJsonStream(
                    CreateStopInput(tempDirectory.FullName, currentStopTimestamp),
                    AppJsonSerializerContext.Default.StopHookInput),
                new MemoryStream(),
                CancellationToken.None);

            Assert.Empty(handler.Requests);
            await AssertPendingStopAsync(
                stateStore,
                tempDirectory.FullName,
                currentTurn,
                currentStopTimestamp,
                "summary must be a non-empty human-readable sentence");
            NotificationTurn? storedOldTurn = await stateStore.TryReadTurnAsync(
                tempDirectory.FullName,
                "session-123",
                oldTurn.NotificationTurnId,
                CancellationToken.None);
            Assert.Equal("open", storedOldTurn?.Status);

            _ = await service.HandleStopAsync(
                CreateJsonStream(
                    CreateStopInput(tempDirectory.FullName, "2026-03-14T15:51:50.783Z"),
                    AppJsonSerializerContext.Default.StopHookInput),
                new MemoryStream(),
                CancellationToken.None);

            TelegramSendMessageRequest payload = DeserializeTelegramPayload(
                Assert.Single(handler.Requests));
            Assert.Contains(
                "摘要：The old completed turn should not steal the new pending Stop.",
                payload.Text,
                StringComparison.Ordinal);
            Assert.Contains(oldTurn.NotificationTurnId, payload.Text, StringComparison.Ordinal);
        }
        finally
        {
            tempDirectory.Delete(recursive: true);
        }
    }

    [Fact]
    public async Task HandleStopAsyncOlderExactTimestampSummaryDeliversWhenCurrentInvalid()
    {
        DirectoryInfo tempDirectory = Directory.CreateTempSubdirectory();
        using EnvironmentScope environment = SetTelegramEnvironment();

        try
        {
            WorkspaceStateStore stateStore = new(
                TimeProvider.System,
                NullLogger<WorkspaceStateStore>.Instance);
            NotificationTurn oldTurn = await CreateTurnAsync(
                stateStore,
                tempDirectory.FullName,
                "session-123",
                "2026-03-14T15:51:40.783Z");
            const string currentStopTimestamp = "2026-03-14T15:52:50.783Z";
            await WriteSummaryAsync(
                tempDirectory.FullName,
                "session-123",
                oldTurn,
                new NotificationSummary
                {
                    SessionId = "session-123",
                    NotificationTurnId = oldTurn.NotificationTurnId,
                    NotificationNonce = oldTurn.NotificationNonce,
                    UpdatedAt = currentStopTimestamp,
                    Summary = "The old exact timestamp summary should deliver while current is invalid.",
                });
            RecordingHttpMessageHandler handler = new();
            HookCommandService service = CreateHookCommandService(handler, stateStore);

            _ = await service.HandleUserPromptSubmitAsync(
                CreateJsonStream(
                    new UserPromptSubmitHookInput
                    {
                        Cwd = tempDirectory.FullName,
                        SessionId = "session-123",
                        Timestamp = "2026-03-14T15:52:40.783Z",
                        TranscriptPath = "/workspace/transcript.json",
                        Prompt = "Start a current turn with invalid summary.",
                    },
                    AppJsonSerializerContext.Default.UserPromptSubmitHookInput),
                new MemoryStream(),
                CancellationToken.None);
            NotificationTurn currentTurn = Assert.Single(
                await stateStore.ListOpenTurnsAsync(
                    tempDirectory.FullName,
                    "session-123",
                    CancellationToken.None),
                turn => !string.Equals(
                    turn.NotificationTurnId,
                    oldTurn.NotificationTurnId,
                    StringComparison.Ordinal));
            await WriteSummaryAsync(
                tempDirectory.FullName,
                "session-123",
                currentTurn,
                new NotificationSummary
                {
                    SessionId = "session-123",
                    NotificationTurnId = currentTurn.NotificationTurnId,
                    NotificationNonce = currentTurn.NotificationNonce,
                    UpdatedAt = "not-a-timestamp",
                    Summary = "Current invalid summary should degrade.",
                });

            _ = await service.HandleStopAsync(
                CreateJsonStream(
                    CreateStopInput(tempDirectory.FullName, currentStopTimestamp),
                    AppJsonSerializerContext.Default.StopHookInput),
                new MemoryStream(),
                CancellationToken.None);

            TelegramSendMessageRequest payload = DeserializeTelegramPayload(
                Assert.Single(handler.Requests));
            Assert.Contains(
                "摘要：The old exact timestamp summary should deliver while current is invalid.",
                payload.Text,
                StringComparison.Ordinal);
            Assert.Contains(oldTurn.NotificationTurnId, payload.Text, StringComparison.Ordinal);
            Assert.DoesNotContain(currentTurn.NotificationTurnId, payload.Text, StringComparison.Ordinal);
        }
        finally
        {
            tempDirectory.Delete(recursive: true);
        }
    }

    [Fact]
    public async Task HandleStopAsyncOlderExactTimestampSummaryDeliversWhileCurrentPending()
    {
        DirectoryInfo tempDirectory = Directory.CreateTempSubdirectory();
        using EnvironmentScope environment = SetTelegramEnvironment();

        try
        {
            WorkspaceStateStore stateStore = new(
                TimeProvider.System,
                NullLogger<WorkspaceStateStore>.Instance);
            NotificationTurn oldTurn = await CreateTurnAsync(
                stateStore,
                tempDirectory.FullName,
                "session-123",
                "2026-03-14T15:51:40.783Z");
            const string currentStopTimestamp = "2026-03-14T15:52:50.783Z";
            await WriteSummaryAsync(
                tempDirectory.FullName,
                "session-123",
                oldTurn,
                new NotificationSummary
                {
                    SessionId = "session-123",
                    NotificationTurnId = oldTurn.NotificationTurnId,
                    NotificationNonce = oldTurn.NotificationNonce,
                    UpdatedAt = currentStopTimestamp,
                    Summary = "The old exact timestamp summary should deliver while current is pending.",
                });
            RecordingHttpMessageHandler handler = new();
            HookCommandService service = CreateHookCommandService(handler, stateStore);

            _ = await service.HandleUserPromptSubmitAsync(
                CreateJsonStream(
                    new UserPromptSubmitHookInput
                    {
                        Cwd = tempDirectory.FullName,
                        SessionId = "session-123",
                        Timestamp = "2026-03-14T15:52:40.783Z",
                        TranscriptPath = "/workspace/transcript.json",
                        Prompt = "Start a current turn with pending summary.",
                    },
                    AppJsonSerializerContext.Default.UserPromptSubmitHookInput),
                new MemoryStream(),
                CancellationToken.None);
            NotificationTurn currentTurn = Assert.Single(
                await stateStore.ListOpenTurnsAsync(
                    tempDirectory.FullName,
                    "session-123",
                    CancellationToken.None),
                turn => !string.Equals(
                    turn.NotificationTurnId,
                    oldTurn.NotificationTurnId,
                    StringComparison.Ordinal));

            _ = await service.HandleStopAsync(
                CreateJsonStream(
                    CreateStopInput(tempDirectory.FullName, currentStopTimestamp),
                    AppJsonSerializerContext.Default.StopHookInput),
                new MemoryStream(),
                CancellationToken.None);

            TelegramSendMessageRequest payload = DeserializeTelegramPayload(
                Assert.Single(handler.Requests));
            Assert.Contains(
                "摘要：The old exact timestamp summary should deliver while current is pending.",
                payload.Text,
                StringComparison.Ordinal);
            Assert.Contains(oldTurn.NotificationTurnId, payload.Text, StringComparison.Ordinal);
            Assert.DoesNotContain(currentTurn.NotificationTurnId, payload.Text, StringComparison.Ordinal);
        }
        finally
        {
            tempDirectory.Delete(recursive: true);
        }
    }

    [Theory]
    [InlineData(" ")]
    [InlineData(null)]
    public async Task HandleStopAsyncCurrentExactBlankPendingDefersDespiteOlderExactSummary(
        string? currentSummary)
    {
        DirectoryInfo tempDirectory = Directory.CreateTempSubdirectory();
        using EnvironmentScope environment = SetTelegramEnvironment();

        try
        {
            WorkspaceStateStore stateStore = new(
                TimeProvider.System,
                NullLogger<WorkspaceStateStore>.Instance);
            NotificationTurn oldTurn = await CreateTurnAsync(
                stateStore,
                tempDirectory.FullName,
                "session-123",
                "2026-03-14T15:51:40.783Z");
            const string stopTimestamp = "2026-03-14T15:52:50.783Z";
            await WriteSummaryAsync(
                tempDirectory.FullName,
                "session-123",
                oldTurn,
                new NotificationSummary
                {
                    SessionId = "session-123",
                    NotificationTurnId = oldTurn.NotificationTurnId,
                    NotificationNonce = oldTurn.NotificationNonce,
                    UpdatedAt = stopTimestamp,
                    Summary = "The older exact summary must not steal current exact pending attribution.",
                });
            RecordingHttpMessageHandler handler = new();
            HookCommandService service = CreateHookCommandService(handler, stateStore);

            _ = await service.HandleUserPromptSubmitAsync(
                CreateJsonStream(
                    new UserPromptSubmitHookInput
                    {
                        Cwd = tempDirectory.FullName,
                        SessionId = "session-123",
                        Timestamp = "2026-03-14T15:52:40.783Z",
                        TranscriptPath = "/workspace/transcript.json",
                        Prompt = "Start a current turn with exact pending summary.",
                    },
                    AppJsonSerializerContext.Default.UserPromptSubmitHookInput),
                new MemoryStream(),
                CancellationToken.None);
            NotificationTurn currentTurn = Assert.Single(
                await stateStore.ListOpenTurnsAsync(
                    tempDirectory.FullName,
                    "session-123",
                    CancellationToken.None),
                turn => !string.Equals(
                    turn.NotificationTurnId,
                    oldTurn.NotificationTurnId,
                    StringComparison.Ordinal));
            await WriteSummaryAsync(
                tempDirectory.FullName,
                "session-123",
                currentTurn,
                new NotificationSummary
                {
                    SessionId = "session-123",
                    NotificationTurnId = currentTurn.NotificationTurnId,
                    NotificationNonce = currentTurn.NotificationNonce,
                    UpdatedAt = stopTimestamp,
                    Status = "pending",
                    Summary = currentSummary,
                });

            _ = await service.HandleStopAsync(
                CreateJsonStream(
                    CreateStopInput(tempDirectory.FullName, stopTimestamp),
                    AppJsonSerializerContext.Default.StopHookInput),
                new MemoryStream(),
                CancellationToken.None);

            Assert.Empty(handler.Requests);
            await AssertPendingStopAsync(
                stateStore,
                tempDirectory.FullName,
                currentTurn,
                stopTimestamp,
                "summary must be a non-empty human-readable sentence");
            NotificationTurn? storedOldTurn = await stateStore.TryReadTurnAsync(
                tempDirectory.FullName,
                "session-123",
                oldTurn.NotificationTurnId,
                CancellationToken.None);
            Assert.Equal("open", storedOldTurn?.Status);
        }
        finally
        {
            tempDirectory.Delete(recursive: true);
        }
    }

    [Theory]
    [InlineData("missing-current")]
    [InlineData("corrupt-current")]
    public async Task HandleStopAsyncFreshClaimedLatestNonExactSuppressesSingleOlderExactWithoutReadableCurrentCache(
        string currentCacheState)
    {
        DirectoryInfo tempDirectory = Directory.CreateTempSubdirectory();
        using EnvironmentScope environment = SetTelegramEnvironment();

        try
        {
            WorkspaceStateStore stateStore = new(
                FixedUtcNow(),
                NullLogger<WorkspaceStateStore>.Instance);
            const string stopTimestamp = "2026-03-14T15:51:50.783Z";
            NotificationTurn exactOldTurn = await CreateTurnAsync(
                stateStore,
                tempDirectory.FullName,
                "session-123",
                "2026-03-14T15:51:30.783Z");
            await WriteSummaryAsync(
                tempDirectory.FullName,
                "session-123",
                exactOldTurn,
                new NotificationSummary
                {
                    SessionId = "session-123",
                    NotificationTurnId = exactOldTurn.NotificationTurnId,
                    NotificationNonce = exactOldTurn.NotificationNonce,
                    UpdatedAt = stopTimestamp,
                    Summary = "The older exact summary must wait behind the claimed latest turn.",
                });
            NotificationTurn latestTurn = await CreateTurnAsync(
                stateStore,
                tempDirectory.FullName,
                "session-123",
                "2026-03-14T15:51:45.783Z");
            await WriteSummaryAsync(
                tempDirectory.FullName,
                "session-123",
                latestTurn,
                new NotificationSummary
                {
                    SessionId = "session-123",
                    NotificationTurnId = latestTurn.NotificationTurnId,
                    NotificationNonce = latestTurn.NotificationNonce,
                    UpdatedAt = "2026-03-14T15:51:49.783Z",
                    Summary = "The latest non-exact summary is still being delivered.",
                });
            string currentPath = AppPaths.GetCurrentStatePath(
                tempDirectory.FullName,
                "session-123");
            if (string.Equals(currentCacheState, "missing-current", StringComparison.Ordinal))
            {
                File.Delete(currentPath);
            }
            else
            {
                await File.WriteAllTextAsync(currentPath, "{");
            }

            string latestTurnClaimPath = AppPaths.GetTurnDeliveryClaimPath(
                tempDirectory.FullName,
                "session-123",
                latestTurn.NotificationTurnId);
            await WriteClaimAsync(latestTurnClaimPath, string.Empty);
            File.SetLastWriteTimeUtc(
                latestTurnClaimPath,
                new DateTime(2026, 3, 14, 15, 51, 49, 783, DateTimeKind.Utc));
            RecordingHttpMessageHandler handler = new();
            HookCommandService service = CreateHookCommandService(handler, stateStore);

            _ = await service.HandleStopAsync(
                CreateJsonStream(
                    CreateStopInput(tempDirectory.FullName, stopTimestamp),
                    AppJsonSerializerContext.Default.StopHookInput),
                new MemoryStream(),
                CancellationToken.None);

            Assert.Empty(handler.Requests);
            Assert.False(File.Exists(AppPaths.GetNotificationRecordPath(
                tempDirectory.FullName,
                "session-123",
                exactOldTurn.NotificationTurnId,
                CreateStopNotificationKeyForTest(stopTimestamp))));
            Assert.False(File.Exists(AppPaths.GetSessionNotificationRecordPath(
                tempDirectory.FullName,
                "session-123",
                CreateStopNotificationKeyForTest(stopTimestamp))));
            Assert.True(File.Exists(latestTurnClaimPath));
        }
        finally
        {
            tempDirectory.Delete(recursive: true);
        }
    }

    [Theory]
    [InlineData("missing-current")]
    [InlineData("corrupt-current")]
    [InlineData("stale-current")]
    public async Task HandleStopAsyncFreshClaimedLatestNonExactDeliversLatestAfterClaimClearsDespiteSingleOlderExact(
        string currentCacheState)
    {
        DirectoryInfo tempDirectory = Directory.CreateTempSubdirectory();
        using EnvironmentScope environment = SetTelegramEnvironment();

        try
        {
            WorkspaceStateStore stateStore = new(
                FixedUtcNow(),
                NullLogger<WorkspaceStateStore>.Instance);
            const string stopTimestamp = "2026-03-14T15:51:50.783Z";
            NotificationTurn exactOldTurn = await CreateTurnAsync(
                stateStore,
                tempDirectory.FullName,
                "session-123",
                "2026-03-14T15:51:30.783Z");
            await WriteSummaryAsync(
                tempDirectory.FullName,
                "session-123",
                exactOldTurn,
                new NotificationSummary
                {
                    SessionId = "session-123",
                    NotificationTurnId = exactOldTurn.NotificationTurnId,
                    NotificationNonce = exactOldTurn.NotificationNonce,
                    UpdatedAt = stopTimestamp,
                    Summary = "The singleton older exact summary must not beat the latest retry.",
                });
            string currentPath = AppPaths.GetCurrentStatePath(
                tempDirectory.FullName,
                "session-123");
            string staleCurrentJson = await File.ReadAllTextAsync(currentPath);
            NotificationTurn latestTurn = await CreateTurnAsync(
                stateStore,
                tempDirectory.FullName,
                "session-123",
                "2026-03-14T15:51:45.783Z");
            await WriteSummaryAsync(
                tempDirectory.FullName,
                "session-123",
                latestTurn,
                new NotificationSummary
                {
                    SessionId = "session-123",
                    NotificationTurnId = latestTurn.NotificationTurnId,
                    NotificationNonce = latestTurn.NotificationNonce,
                    UpdatedAt = "2026-03-14T15:51:49.783Z",
                    Summary = "The latest non-exact summary should deliver after its claim clears.",
                });
            if (string.Equals(currentCacheState, "missing-current", StringComparison.Ordinal))
            {
                File.Delete(currentPath);
            }
            else if (string.Equals(currentCacheState, "corrupt-current", StringComparison.Ordinal))
            {
                await File.WriteAllTextAsync(currentPath, "{");
            }
            else
            {
                await File.WriteAllTextAsync(currentPath, staleCurrentJson);
            }

            string latestTurnClaimPath = AppPaths.GetTurnDeliveryClaimPath(
                tempDirectory.FullName,
                "session-123",
                latestTurn.NotificationTurnId);
            await WriteClaimAsync(latestTurnClaimPath, string.Empty);
            File.SetLastWriteTimeUtc(
                latestTurnClaimPath,
                new DateTime(2026, 3, 14, 15, 51, 49, 783, DateTimeKind.Utc));
            RecordingHttpMessageHandler handler = new();
            HookCommandService service = CreateHookCommandService(handler, stateStore);

            _ = await service.HandleStopAsync(
                CreateJsonStream(
                    CreateStopInput(tempDirectory.FullName, stopTimestamp),
                    AppJsonSerializerContext.Default.StopHookInput),
                new MemoryStream(),
                CancellationToken.None);

            Assert.Empty(handler.Requests);
            Assert.True(File.Exists(latestTurnClaimPath));

            File.Delete(latestTurnClaimPath);
            _ = await service.HandleStopAsync(
                CreateJsonStream(
                    CreateStopInput(tempDirectory.FullName, stopTimestamp),
                    AppJsonSerializerContext.Default.StopHookInput),
                new MemoryStream(),
                CancellationToken.None);

            TelegramSendMessageRequest payload = DeserializeTelegramPayload(
                Assert.Single(handler.Requests));
            Assert.Contains(
                "摘要：The latest non-exact summary should deliver after its claim clears.",
                payload.Text,
                StringComparison.Ordinal);
            Assert.Contains(latestTurn.NotificationTurnId, payload.Text, StringComparison.Ordinal);
            Assert.DoesNotContain(exactOldTurn.NotificationTurnId, payload.Text, StringComparison.Ordinal);
            Assert.False(File.Exists(AppPaths.GetNotificationRecordPath(
                tempDirectory.FullName,
                "session-123",
                exactOldTurn.NotificationTurnId,
                CreateStopNotificationKeyForTest(stopTimestamp))));
            Assert.True(File.Exists(AppPaths.GetNotificationRecordPath(
                tempDirectory.FullName,
                "session-123",
                latestTurn.NotificationTurnId,
                CreateStopNotificationKeyForTest(stopTimestamp))));
        }
        finally
        {
            tempDirectory.Delete(recursive: true);
        }
    }

    [Theory]
    [InlineData("missing-current")]
    [InlineData("corrupt-current")]
    [InlineData("stale-current")]
    public async Task HandleStopAsyncFreshClaimedLatestInvalidSummarySuppressesSingleOlderExactWithoutReadableCurrentCache(
        string currentCacheState)
    {
        DirectoryInfo tempDirectory = Directory.CreateTempSubdirectory();
        using EnvironmentScope environment = SetTelegramEnvironment();

        try
        {
            WorkspaceStateStore stateStore = new(
                FixedUtcNow(),
                NullLogger<WorkspaceStateStore>.Instance);
            const string stopTimestamp = "2026-03-14T15:51:50.783Z";
            NotificationTurn exactOldTurn = await CreateTurnAsync(
                stateStore,
                tempDirectory.FullName,
                "session-123",
                "2026-03-14T15:51:30.783Z");
            await WriteSummaryAsync(
                tempDirectory.FullName,
                "session-123",
                exactOldTurn,
                new NotificationSummary
                {
                    SessionId = "session-123",
                    NotificationTurnId = exactOldTurn.NotificationTurnId,
                    NotificationNonce = exactOldTurn.NotificationNonce,
                    UpdatedAt = stopTimestamp,
                    Summary = "The older exact summary must wait behind the claimed latest invalid turn.",
                });
            string currentPath = AppPaths.GetCurrentStatePath(
                tempDirectory.FullName,
                "session-123");
            string staleCurrentJson = await File.ReadAllTextAsync(currentPath);
            NotificationTurn latestTurn = await CreateTurnAsync(
                stateStore,
                tempDirectory.FullName,
                "session-123",
                "2026-03-14T15:51:45.783Z");
            await File.WriteAllTextAsync(
                AppPaths.GetSummaryStatePath(
                    tempDirectory.FullName,
                    "session-123",
                    latestTurn.NotificationTurnId),
                "{}");
            if (string.Equals(currentCacheState, "missing-current", StringComparison.Ordinal))
            {
                File.Delete(currentPath);
            }
            else if (string.Equals(currentCacheState, "corrupt-current", StringComparison.Ordinal))
            {
                await File.WriteAllTextAsync(currentPath, "{");
            }
            else
            {
                await File.WriteAllTextAsync(currentPath, staleCurrentJson);
            }

            string latestTurnClaimPath = AppPaths.GetTurnDeliveryClaimPath(
                tempDirectory.FullName,
                "session-123",
                latestTurn.NotificationTurnId);
            await WriteClaimAsync(latestTurnClaimPath, string.Empty);
            File.SetLastWriteTimeUtc(
                latestTurnClaimPath,
                new DateTime(2026, 3, 14, 15, 51, 49, 783, DateTimeKind.Utc));
            RecordingHttpMessageHandler handler = new();
            HookCommandService service = CreateHookCommandService(handler, stateStore);

            _ = await service.HandleStopAsync(
                CreateJsonStream(
                    CreateStopInput(tempDirectory.FullName, stopTimestamp),
                    AppJsonSerializerContext.Default.StopHookInput),
                new MemoryStream(),
                CancellationToken.None);

            Assert.Empty(handler.Requests);
            Assert.False(File.Exists(AppPaths.GetNotificationRecordPath(
                tempDirectory.FullName,
                "session-123",
                exactOldTurn.NotificationTurnId,
                CreateStopNotificationKeyForTest(stopTimestamp))));
            Assert.False(File.Exists(AppPaths.GetSessionNotificationRecordPath(
                tempDirectory.FullName,
                "session-123",
                CreateStopNotificationKeyForTest(stopTimestamp))));
            Assert.True(File.Exists(latestTurnClaimPath));
        }
        finally
        {
            tempDirectory.Delete(recursive: true);
        }
    }

    [Theory]
    [InlineData("missing-current", "missing")]
    [InlineData("missing-current", "corrupt")]
    [InlineData("missing-current", "json-null")]
    [InlineData("missing-current", "blank-assigned")]
    [InlineData("corrupt-current", "missing")]
    [InlineData("corrupt-current", "corrupt")]
    [InlineData("corrupt-current", "json-null")]
    [InlineData("corrupt-current", "null-assigned")]
    [InlineData("stale-current", "missing")]
    [InlineData("stale-current", "corrupt")]
    [InlineData("stale-current", "json-null")]
    [InlineData("stale-current", "blank-assigned")]
    public async Task HandleStopAsyncFreshClaimedLatestPendingSuppressesSingleOlderExactWithoutReadableCurrentCache(
        string currentCacheState,
        string pendingSummaryState)
    {
        DirectoryInfo tempDirectory = Directory.CreateTempSubdirectory();
        using EnvironmentScope environment = SetTelegramEnvironment();

        try
        {
            WorkspaceStateStore stateStore = new(
                FixedUtcNow(),
                NullLogger<WorkspaceStateStore>.Instance);
            const string stopTimestamp = "2026-03-14T15:51:50.783Z";
            NotificationTurn exactOldTurn = await CreateTurnAsync(
                stateStore,
                tempDirectory.FullName,
                "session-123",
                "2026-03-14T15:51:30.783Z");
            await WriteSummaryAsync(
                tempDirectory.FullName,
                "session-123",
                exactOldTurn,
                new NotificationSummary
                {
                    SessionId = "session-123",
                    NotificationTurnId = exactOldTurn.NotificationTurnId,
                    NotificationNonce = exactOldTurn.NotificationNonce,
                    UpdatedAt = stopTimestamp,
                    Summary = "The older exact summary must wait behind the claimed latest pending turn.",
                });
            string currentPath = AppPaths.GetCurrentStatePath(
                tempDirectory.FullName,
                "session-123");
            string staleCurrentJson = await File.ReadAllTextAsync(currentPath);
            NotificationTurn latestTurn = await CreateTurnAsync(
                stateStore,
                tempDirectory.FullName,
                "session-123",
                "2026-03-14T15:51:45.783Z");
            if (string.Equals(pendingSummaryState, "corrupt", StringComparison.Ordinal))
            {
                await WriteRawSummaryJsonAsync(tempDirectory.FullName, "session-123", latestTurn, "{");
            }
            else if (string.Equals(pendingSummaryState, "json-null", StringComparison.Ordinal))
            {
                await WriteRawSummaryJsonAsync(tempDirectory.FullName, "session-123", latestTurn, "null");
            }
            else if (string.Equals(pendingSummaryState, "blank-assigned", StringComparison.Ordinal)
                || string.Equals(pendingSummaryState, "null-assigned", StringComparison.Ordinal))
            {
                await WriteSummaryAsync(
                    tempDirectory.FullName,
                    "session-123",
                    latestTurn,
                    new NotificationSummary
                    {
                        SessionId = "session-123",
                        NotificationTurnId = latestTurn.NotificationTurnId,
                        NotificationNonce = latestTurn.NotificationNonce,
                        UpdatedAt = stopTimestamp,
                        Summary = string.Equals(
                            pendingSummaryState,
                            "blank-assigned",
                            StringComparison.Ordinal)
                            ? " "
                            : null,
                    });
            }

            if (string.Equals(currentCacheState, "missing-current", StringComparison.Ordinal))
            {
                File.Delete(currentPath);
            }
            else if (string.Equals(currentCacheState, "corrupt-current", StringComparison.Ordinal))
            {
                await File.WriteAllTextAsync(currentPath, "{");
            }
            else
            {
                await File.WriteAllTextAsync(currentPath, staleCurrentJson);
            }

            string latestTurnClaimPath = AppPaths.GetTurnDeliveryClaimPath(
                tempDirectory.FullName,
                "session-123",
                latestTurn.NotificationTurnId);
            await WriteClaimAsync(latestTurnClaimPath, string.Empty);
            File.SetLastWriteTimeUtc(
                latestTurnClaimPath,
                new DateTime(2026, 3, 14, 15, 51, 49, 783, DateTimeKind.Utc));
            RecordingHttpMessageHandler handler = new();
            HookCommandService service = CreateHookCommandService(handler, stateStore);

            _ = await service.HandleStopAsync(
                CreateJsonStream(
                    CreateStopInput(tempDirectory.FullName, stopTimestamp),
                    AppJsonSerializerContext.Default.StopHookInput),
                new MemoryStream(),
                CancellationToken.None);

            Assert.Empty(handler.Requests);
            Assert.False(File.Exists(AppPaths.GetNotificationRecordPath(
                tempDirectory.FullName,
                "session-123",
                exactOldTurn.NotificationTurnId,
                CreateStopNotificationKeyForTest(stopTimestamp))));
            Assert.False(File.Exists(AppPaths.GetSessionNotificationRecordPath(
                tempDirectory.FullName,
                "session-123",
                CreateStopNotificationKeyForTest(stopTimestamp))));
            Assert.True(File.Exists(latestTurnClaimPath));
        }
        finally
        {
            tempDirectory.Delete(recursive: true);
        }
    }

    [Theory]
    [InlineData("older-non-exact", "missing")]
    [InlineData("older-non-exact", "corrupt")]
    [InlineData("older-non-exact", "json-null")]
    [InlineData("older-non-exact", "blank-assigned")]
    [InlineData("older-non-exact", "null-assigned")]
    [InlineData("no-open-fallback", "missing")]
    [InlineData("no-open-fallback", "corrupt")]
    [InlineData("no-open-fallback", "json-null")]
    [InlineData("no-open-fallback", "blank-assigned")]
    [InlineData("no-open-fallback", "null-assigned")]
    public async Task HandleStopAsyncFreshClaimedLatestPendingSuppressesFallbacks(
        string scenario,
        string pendingSummaryState)
    {
        DirectoryInfo tempDirectory = Directory.CreateTempSubdirectory();
        using EnvironmentScope environment = SetTelegramEnvironment();

        try
        {
            WorkspaceStateStore stateStore = new(
                FixedUtcNow(),
                NullLogger<WorkspaceStateStore>.Instance);
            const string stopTimestamp = "2026-03-14T15:51:50.783Z";
            NotificationTurn? oldTurn = null;
            if (string.Equals(scenario, "older-non-exact", StringComparison.Ordinal))
            {
                oldTurn = await CreateTurnAsync(
                    stateStore,
                    tempDirectory.FullName,
                    "session-123",
                    "2026-03-14T15:51:30.783Z");
                await WriteSummaryAsync(
                    tempDirectory.FullName,
                    "session-123",
                    oldTurn,
                    new NotificationSummary
                    {
                        SessionId = "session-123",
                        NotificationTurnId = oldTurn.NotificationTurnId,
                        NotificationNonce = oldTurn.NotificationNonce,
                        UpdatedAt = "2026-03-14T15:51:49.783Z",
                        Summary = "The older non-exact summary must wait behind the claimed latest pending turn.",
                    });
            }

            NotificationTurn latestTurn = await CreateTurnAsync(
                stateStore,
                tempDirectory.FullName,
                "session-123",
                "2026-03-14T15:51:45.783Z");
            if (string.Equals(pendingSummaryState, "corrupt", StringComparison.Ordinal))
            {
                await WriteRawSummaryJsonAsync(tempDirectory.FullName, "session-123", latestTurn, "{");
            }
            else if (string.Equals(pendingSummaryState, "json-null", StringComparison.Ordinal))
            {
                await WriteRawSummaryJsonAsync(tempDirectory.FullName, "session-123", latestTurn, "null");
            }
            else if (string.Equals(pendingSummaryState, "blank-assigned", StringComparison.Ordinal)
                || string.Equals(pendingSummaryState, "null-assigned", StringComparison.Ordinal))
            {
                await WriteSummaryAsync(
                    tempDirectory.FullName,
                    "session-123",
                    latestTurn,
                    new NotificationSummary
                    {
                        SessionId = "session-123",
                        NotificationTurnId = latestTurn.NotificationTurnId,
                        NotificationNonce = latestTurn.NotificationNonce,
                        UpdatedAt = stopTimestamp,
                        Summary = string.Equals(
                            pendingSummaryState,
                            "blank-assigned",
                            StringComparison.Ordinal)
                            ? " "
                            : null,
                    });
            }

            File.Delete(AppPaths.GetCurrentStatePath(tempDirectory.FullName, "session-123"));
            string latestTurnClaimPath = AppPaths.GetTurnDeliveryClaimPath(
                tempDirectory.FullName,
                "session-123",
                latestTurn.NotificationTurnId);
            await WriteClaimAsync(latestTurnClaimPath, string.Empty);
            File.SetLastWriteTimeUtc(
                latestTurnClaimPath,
                new DateTime(2026, 3, 14, 15, 51, 49, 783, DateTimeKind.Utc));
            RecordingHttpMessageHandler handler = new();
            HookCommandService service = CreateHookCommandService(handler, stateStore);

            _ = await service.HandleStopAsync(
                CreateJsonStream(
                    CreateStopInput(tempDirectory.FullName, stopTimestamp),
                    AppJsonSerializerContext.Default.StopHookInput),
                new MemoryStream(),
                CancellationToken.None);

            Assert.Empty(handler.Requests);
            Assert.True(File.Exists(latestTurnClaimPath));
            Assert.False(File.Exists(AppPaths.GetSessionNotificationRecordPath(
                tempDirectory.FullName,
                "session-123",
                CreateStopNotificationKeyForTest(stopTimestamp))));
            if (oldTurn is not null)
            {
                Assert.False(File.Exists(AppPaths.GetNotificationRecordPath(
                    tempDirectory.FullName,
                    "session-123",
                    oldTurn.NotificationTurnId,
                    CreateStopNotificationKeyForTest(stopTimestamp))));
            }
        }
        finally
        {
            tempDirectory.Delete(recursive: true);
        }
    }

    [Theory]
    [InlineData("older-exact")]
    [InlineData("older-non-exact")]
    [InlineData("no-open-fallback")]
    public async Task HandleStopAsyncTiedLatestFreshClaimsSuppressOlderAndFallbackDelivery(string scenario)
    {
        DirectoryInfo tempDirectory = Directory.CreateTempSubdirectory();
        using EnvironmentScope environment = SetTelegramEnvironment();

        try
        {
            WorkspaceStateStore stateStore = new(
                FixedUtcNow(),
                NullLogger<WorkspaceStateStore>.Instance);
            const string sessionId = "session-123";
            const string stopTimestamp = "2026-03-14T15:51:50.783Z";
            NotificationTurn? oldTurn = null;
            if (!string.Equals(scenario, "no-open-fallback", StringComparison.Ordinal))
            {
                oldTurn = await CreateTurnAsync(
                    stateStore,
                    tempDirectory.FullName,
                    sessionId,
                    "2026-03-14T15:51:30.783Z");
                await WriteSummaryAsync(
                    tempDirectory.FullName,
                    sessionId,
                    oldTurn,
                    new NotificationSummary
                    {
                        SessionId = sessionId,
                        NotificationTurnId = oldTurn.NotificationTurnId,
                        NotificationNonce = oldTurn.NotificationNonce,
                        UpdatedAt = string.Equals(scenario, "older-exact", StringComparison.Ordinal)
                            ? stopTimestamp
                            : "2026-03-14T15:51:49.783Z",
                        Summary = "An older candidate must wait behind tied latest fresh claims.",
                    });
            }

            NotificationTurn tiedMissingTurn = await CreateTurnAsync(
                stateStore,
                tempDirectory.FullName,
                sessionId,
                "2026-03-14T15:51:45.783Z");
            NotificationTurn tiedCorruptTurn = await CreateTurnAsync(
                stateStore,
                tempDirectory.FullName,
                sessionId,
                "2026-03-14T15:51:45.783Z");
            tiedMissingTurn.Status = "open";
            tiedCorruptTurn.Status = "open";
            await WriteTurnStateAsync(tempDirectory.FullName, tiedMissingTurn);
            await WriteTurnStateAsync(tempDirectory.FullName, tiedCorruptTurn);
            await WriteRawSummaryJsonAsync(tempDirectory.FullName, sessionId, tiedCorruptTurn, "{");
            File.Delete(AppPaths.GetCurrentStatePath(tempDirectory.FullName, sessionId));

            string tiedMissingClaimPath = AppPaths.GetTurnDeliveryClaimPath(
                tempDirectory.FullName,
                sessionId,
                tiedMissingTurn.NotificationTurnId);
            string tiedCorruptClaimPath = AppPaths.GetTurnDeliveryClaimPath(
                tempDirectory.FullName,
                sessionId,
                tiedCorruptTurn.NotificationTurnId);
            await WriteClaimAsync(tiedMissingClaimPath, string.Empty);
            await WriteClaimAsync(tiedCorruptClaimPath, string.Empty);
            File.SetLastWriteTimeUtc(
                tiedMissingClaimPath,
                new DateTime(2026, 3, 14, 15, 51, 49, 783, DateTimeKind.Utc));
            File.SetLastWriteTimeUtc(
                tiedCorruptClaimPath,
                new DateTime(2026, 3, 14, 15, 51, 49, 783, DateTimeKind.Utc));
            RecordingHttpMessageHandler handler = new();
            HookCommandService service = CreateHookCommandService(handler, stateStore);

            _ = await service.HandleStopAsync(
                CreateJsonStream(
                    CreateStopInput(tempDirectory.FullName, stopTimestamp),
                    AppJsonSerializerContext.Default.StopHookInput),
                new MemoryStream(),
                CancellationToken.None);

            Assert.Empty(handler.Requests);
            Assert.True(File.Exists(tiedMissingClaimPath));
            Assert.True(File.Exists(tiedCorruptClaimPath));
            Assert.False(File.Exists(AppPaths.GetSessionNotificationRecordPath(
                tempDirectory.FullName,
                sessionId,
                CreateStopNotificationKeyForTest(stopTimestamp))));
            if (oldTurn is not null)
            {
                Assert.False(File.Exists(AppPaths.GetNotificationRecordPath(
                    tempDirectory.FullName,
                    sessionId,
                    oldTurn.NotificationTurnId,
                    CreateStopNotificationKeyForTest(stopTimestamp))));
            }
        }
        finally
        {
            tempDirectory.Delete(recursive: true);
        }
    }

    [Theory]
    [InlineData("missing-current")]
    [InlineData("corrupt-current")]
    public async Task HandleStopAsyncCurrentExactBlankPendingDefersWithoutReadableCurrentCache(
        string currentCacheState)
    {
        DirectoryInfo tempDirectory = Directory.CreateTempSubdirectory();
        using EnvironmentScope environment = SetTelegramEnvironment();

        try
        {
            WorkspaceStateStore stateStore = new(
                TimeProvider.System,
                NullLogger<WorkspaceStateStore>.Instance);
            NotificationTurn oldTurn = await CreateTurnAsync(
                stateStore,
                tempDirectory.FullName,
                "session-123",
                "2026-03-14T15:51:40.783Z");
            const string stopTimestamp = "2026-03-14T15:52:50.783Z";
            await WriteSummaryAsync(
                tempDirectory.FullName,
                "session-123",
                oldTurn,
                new NotificationSummary
                {
                    SessionId = "session-123",
                    NotificationTurnId = oldTurn.NotificationTurnId,
                    NotificationNonce = oldTurn.NotificationNonce,
                    UpdatedAt = stopTimestamp,
                    Summary = "The older exact summary must not steal cacheless pending current attribution.",
                });
            RecordingHttpMessageHandler handler = new();
            HookCommandService service = CreateHookCommandService(handler, stateStore);

            _ = await service.HandleUserPromptSubmitAsync(
                CreateJsonStream(
                    new UserPromptSubmitHookInput
                    {
                        Cwd = tempDirectory.FullName,
                        SessionId = "session-123",
                        Timestamp = "2026-03-14T15:52:40.783Z",
                        TranscriptPath = "/workspace/transcript.json",
                        Prompt = "Start a current turn with exact pending summary and no current cache.",
                    },
                    AppJsonSerializerContext.Default.UserPromptSubmitHookInput),
                new MemoryStream(),
                CancellationToken.None);
            NotificationTurn currentTurn = Assert.Single(
                await stateStore.ListOpenTurnsAsync(
                    tempDirectory.FullName,
                    "session-123",
                    CancellationToken.None),
                turn => !string.Equals(
                    turn.NotificationTurnId,
                    oldTurn.NotificationTurnId,
                    StringComparison.Ordinal));
            await WriteSummaryAsync(
                tempDirectory.FullName,
                "session-123",
                currentTurn,
                new NotificationSummary
                {
                    SessionId = "session-123",
                    NotificationTurnId = currentTurn.NotificationTurnId,
                    NotificationNonce = currentTurn.NotificationNonce,
                    UpdatedAt = stopTimestamp,
                    Status = "pending",
                    Summary = " ",
                });
            string currentPath = AppPaths.GetCurrentStatePath(
                tempDirectory.FullName,
                "session-123");
            if (string.Equals(currentCacheState, "missing-current", StringComparison.Ordinal))
            {
                File.Delete(currentPath);
            }
            else
            {
                await File.WriteAllTextAsync(currentPath, "{");
            }

            _ = await service.HandleStopAsync(
                CreateJsonStream(
                    CreateStopInput(tempDirectory.FullName, stopTimestamp),
                    AppJsonSerializerContext.Default.StopHookInput),
                new MemoryStream(),
                CancellationToken.None);

            Assert.Empty(handler.Requests);
            await AssertPendingStopAsync(
                stateStore,
                tempDirectory.FullName,
                currentTurn,
                stopTimestamp,
                "summary must be a non-empty human-readable sentence");
            Assert.False(File.Exists(AppPaths.GetNotificationRecordPath(
                tempDirectory.FullName,
                "session-123",
                oldTurn.NotificationTurnId,
                CreateStopNotificationKeyForTest(stopTimestamp))));
        }
        finally
        {
            tempDirectory.Delete(recursive: true);
        }
    }

    [Theory]
    [InlineData("missing-current")]
    [InlineData("corrupt-current")]
    public async Task HandleStopAsyncCachelessValidExactAndPendingExactDefersAsNonUniqueAttribution(
        string currentCacheState)
    {
        DirectoryInfo tempDirectory = Directory.CreateTempSubdirectory();
        using EnvironmentScope environment = SetTelegramEnvironment();

        try
        {
            const string stopTimestamp = "2026-03-14T15:52:50.783Z";
            WorkspaceStateStore stateStore = new(
                new FixedTimeProvider(
                    new DateTimeOffset(2026, 3, 14, 15, 52, 50, 783, TimeSpan.Zero)),
                NullLogger<WorkspaceStateStore>.Instance);
            NotificationTurn validExactTurn = await CreateTurnAsync(
                stateStore,
                tempDirectory.FullName,
                "session-123",
                "2026-03-14T15:52:35.783Z");
            await WriteSummaryAsync(
                tempDirectory.FullName,
                "session-123",
                validExactTurn,
                new NotificationSummary
                {
                    SessionId = "session-123",
                    NotificationTurnId = validExactTurn.NotificationTurnId,
                    NotificationNonce = validExactTurn.NotificationNonce,
                    UpdatedAt = stopTimestamp,
                    Summary = "The valid exact summary must not win against pending exact evidence.",
                });
            NotificationTurn pendingExactTurn = await CreateTurnAsync(
                stateStore,
                tempDirectory.FullName,
                "session-123",
                "2026-03-14T15:52:40.783Z");
            await WriteSummaryAsync(
                tempDirectory.FullName,
                "session-123",
                pendingExactTurn,
                new NotificationSummary
                {
                    SessionId = "session-123",
                    NotificationTurnId = pendingExactTurn.NotificationTurnId,
                    NotificationNonce = pendingExactTurn.NotificationNonce,
                    UpdatedAt = stopTimestamp,
                    Status = "pending",
                    Summary = " ",
                });
            string currentPath = AppPaths.GetCurrentStatePath(
                tempDirectory.FullName,
                "session-123");
            if (string.Equals(currentCacheState, "missing-current", StringComparison.Ordinal))
            {
                File.Delete(currentPath);
            }
            else
            {
                await File.WriteAllTextAsync(currentPath, "{");
            }

            RecordingHttpMessageHandler handler = new();
            HookCommandService service = CreateHookCommandService(handler, stateStore);

            _ = await service.HandleStopAsync(
                CreateJsonStream(
                    CreateStopInput(tempDirectory.FullName, stopTimestamp),
                    AppJsonSerializerContext.Default.StopHookInput),
                new MemoryStream(),
                CancellationToken.None);

            Assert.Empty(handler.Requests);
            await AssertPendingStopAsync(
                stateStore,
                tempDirectory.FullName,
                pendingExactTurn,
                stopTimestamp,
                "summary must be a non-empty human-readable sentence");
            Assert.False(File.Exists(AppPaths.GetNotificationRecordPath(
                tempDirectory.FullName,
                "session-123",
                validExactTurn.NotificationTurnId,
                CreateStopNotificationKeyForTest(stopTimestamp))));
        }
        finally
        {
            tempDirectory.Delete(recursive: true);
        }
    }

    [Fact]
    public async Task HandleStopAsyncCurrentCompletedExactDefersBehindOlderPendingExact()
    {
        DirectoryInfo tempDirectory = Directory.CreateTempSubdirectory();
        using EnvironmentScope environment = SetTelegramEnvironment();

        try
        {
            WorkspaceStateStore stateStore = new(
                TimeProvider.System,
                NullLogger<WorkspaceStateStore>.Instance);
            RecordingHttpMessageHandler handler = new();
            HookCommandService service = CreateHookCommandService(handler, stateStore);
            const string stopTimestamp = "2026-03-14T15:52:50.783Z";
            NotificationTurn pendingExactTurn = await CreateTurnAsync(
                stateStore,
                tempDirectory.FullName,
                "session-123",
                "2026-03-14T15:52:30.783Z");
            await WriteSummaryAsync(
                tempDirectory.FullName,
                "session-123",
                pendingExactTurn,
                new NotificationSummary
                {
                    SessionId = "session-123",
                    NotificationTurnId = pendingExactTurn.NotificationTurnId,
                    NotificationNonce = pendingExactTurn.NotificationNonce,
                    UpdatedAt = stopTimestamp,
                    Status = "pending",
                    Summary = string.Empty,
                });
            NotificationTurn completedExactTurn = await CreateTurnAsync(
                stateStore,
                tempDirectory.FullName,
                "session-123",
                "2026-03-14T15:52:40.783Z");
            await WriteSummaryAsync(
                tempDirectory.FullName,
                "session-123",
                completedExactTurn,
                new NotificationSummary
                {
                    SessionId = "session-123",
                    NotificationTurnId = completedExactTurn.NotificationTurnId,
                    NotificationNonce = completedExactTurn.NotificationNonce,
                    UpdatedAt = stopTimestamp,
                    Summary = "The current completed exact summary must wait behind pending exact evidence.",
                });

            _ = await service.HandleStopAsync(
                CreateJsonStream(
                    CreateStopInput(tempDirectory.FullName, stopTimestamp),
                    AppJsonSerializerContext.Default.StopHookInput),
                new MemoryStream(),
                CancellationToken.None);

            Assert.Empty(handler.Requests);
            Assert.False(File.Exists(AppPaths.GetNotificationRecordPath(
                tempDirectory.FullName,
                "session-123",
                completedExactTurn.NotificationTurnId,
                CreateStopNotificationKeyForTest(stopTimestamp))));
            Assert.False(File.Exists(AppPaths.GetSessionNotificationRecordPath(
                tempDirectory.FullName,
                "session-123",
                CreateStopNotificationKeyForTest(stopTimestamp))));
            Assert.False(await WorkspaceStateStore.HasDurableDeliveryRecordAsync(
                tempDirectory.FullName,
                "session-123",
                completedExactTurn.NotificationTurnId,
                CancellationToken.None));

            await WriteSummaryAsync(
                tempDirectory.FullName,
                "session-123",
                pendingExactTurn,
                new NotificationSummary
                {
                    SessionId = "session-123",
                    NotificationTurnId = pendingExactTurn.NotificationTurnId,
                    NotificationNonce = pendingExactTurn.NotificationNonce,
                    UpdatedAt = stopTimestamp,
                    Summary = "The older pending exact summary completed and owns the Stop.",
                });

            _ = await service.HandleStopAsync(
                CreateJsonStream(
                    CreateStopInput(tempDirectory.FullName, stopTimestamp),
                    AppJsonSerializerContext.Default.StopHookInput),
                new MemoryStream(),
                CancellationToken.None);
            _ = await service.HandleStopAsync(
                CreateJsonStream(
                    CreateStopInput(tempDirectory.FullName, stopTimestamp),
                    AppJsonSerializerContext.Default.StopHookInput),
                new MemoryStream(),
                CancellationToken.None);

            TelegramSendMessageRequest payload = DeserializeTelegramPayload(
                Assert.Single(handler.Requests));
            Assert.Contains(
                "摘要：The older pending exact summary completed and owns the Stop.",
                payload.Text,
                StringComparison.Ordinal);
            Assert.Contains(pendingExactTurn.NotificationTurnId, payload.Text, StringComparison.Ordinal);
            Assert.DoesNotContain(completedExactTurn.NotificationTurnId, payload.Text, StringComparison.Ordinal);
            Assert.True(await WorkspaceStateStore.HasDurableDeliveryRecordAsync(
                tempDirectory.FullName,
                "session-123",
                pendingExactTurn.NotificationTurnId,
                CancellationToken.None));
            Assert.False(await WorkspaceStateStore.HasDurableDeliveryRecordAsync(
                tempDirectory.FullName,
                "session-123",
                completedExactTurn.NotificationTurnId,
                CancellationToken.None));
        }
        finally
        {
            tempDirectory.Delete(recursive: true);
        }
    }

    [Theory]
    [InlineData("missing-current")]
    [InlineData("corrupt-current")]
    public async Task HandleStopAsyncCachelessCompletedExactBeatsLaterHookPlaceholder(
        string currentCacheState)
    {
        DirectoryInfo tempDirectory = Directory.CreateTempSubdirectory();
        using EnvironmentScope environment = SetTelegramEnvironment();

        try
        {
            WorkspaceStateStore stateStore = new(
                TimeProvider.System,
                NullLogger<WorkspaceStateStore>.Instance);
            const string stopTimestamp = "2026-03-14T15:52:50.783Z";
            NotificationTurn oldTurn = await CreateTurnAsync(
                stateStore,
                tempDirectory.FullName,
                "session-123",
                "2026-03-14T15:51:40.783Z");
            await WriteSummaryAsync(
                tempDirectory.FullName,
                "session-123",
                oldTurn,
                new NotificationSummary
                {
                    SessionId = "session-123",
                    NotificationTurnId = oldTurn.NotificationTurnId,
                    NotificationNonce = oldTurn.NotificationNonce,
                    UpdatedAt = stopTimestamp,
                    Status = "completed",
                    Summary = "The older completed exact summary must beat the cacheless hook placeholder.",
                });
            NotificationTurn currentTurn = await CreateTurnAsync(
                stateStore,
                tempDirectory.FullName,
                "session-123",
                "2026-03-14T15:52:40.783Z");
            string currentPath = AppPaths.GetCurrentStatePath(
                tempDirectory.FullName,
                "session-123");
            if (string.Equals(currentCacheState, "missing-current", StringComparison.Ordinal))
            {
                File.Delete(currentPath);
            }
            else
            {
                await File.WriteAllTextAsync(currentPath, "{");
            }

            RecordingHttpMessageHandler handler = new();
            HookCommandService service = CreateHookCommandService(handler, stateStore);

            _ = await service.HandleStopAsync(
                CreateJsonStream(
                    CreateStopInput(tempDirectory.FullName, stopTimestamp),
                    AppJsonSerializerContext.Default.StopHookInput),
                new MemoryStream(),
                CancellationToken.None);

            TelegramSendMessageRequest payload = DeserializeTelegramPayload(
                Assert.Single(handler.Requests));
            Assert.Contains(
                "摘要：The older completed exact summary must beat the cacheless hook placeholder.",
                payload.Text,
                StringComparison.Ordinal);
            Assert.Contains(oldTurn.NotificationTurnId, payload.Text, StringComparison.Ordinal);
            Assert.DoesNotContain(currentTurn.NotificationTurnId, payload.Text, StringComparison.Ordinal);
        }
        finally
        {
            tempDirectory.Delete(recursive: true);
        }
    }

    [Theory]
    [InlineData("missing-current")]
    [InlineData("corrupt-current")]
    [InlineData("stale-current")]
    public async Task HandleStopAsyncLatestCurrentExactSummaryDeliversWithoutReadableCurrentCache(
        string currentCacheState)
    {
        DirectoryInfo tempDirectory = Directory.CreateTempSubdirectory();
        using EnvironmentScope environment = SetTelegramEnvironment();

        try
        {
            WorkspaceStateStore stateStore = new(
                TimeProvider.System,
                NullLogger<WorkspaceStateStore>.Instance);
            const string stopTimestamp = "2026-03-14T15:52:50.783Z";
            NotificationTurn oldTurn = await CreateTurnAsync(
                stateStore,
                tempDirectory.FullName,
                "session-123",
                "2026-03-14T15:51:40.783Z");
            await WriteSummaryAsync(
                tempDirectory.FullName,
                "session-123",
                oldTurn,
                new NotificationSummary
                {
                    SessionId = "session-123",
                    NotificationTurnId = oldTurn.NotificationTurnId,
                    NotificationNonce = oldTurn.NotificationNonce,
                    UpdatedAt = stopTimestamp,
                    Summary = "The older exact summary must not win cacheless current attribution.",
                });
            string currentPath = AppPaths.GetCurrentStatePath(
                tempDirectory.FullName,
                "session-123");
            string staleCurrentJson = await File.ReadAllTextAsync(currentPath);
            RecordingHttpMessageHandler handler = new();
            HookCommandService service = CreateHookCommandService(handler, stateStore);

            _ = await service.HandleUserPromptSubmitAsync(
                CreateJsonStream(
                    new UserPromptSubmitHookInput
                    {
                        Cwd = tempDirectory.FullName,
                        SessionId = "session-123",
                        Timestamp = "2026-03-14T15:52:40.783Z",
                        TranscriptPath = "/workspace/transcript.json",
                        Prompt = "Start a current turn with exact summary and no current cache.",
                    },
                    AppJsonSerializerContext.Default.UserPromptSubmitHookInput),
                new MemoryStream(),
                CancellationToken.None);
            NotificationTurn currentTurn = Assert.Single(
                await stateStore.ListOpenTurnsAsync(
                    tempDirectory.FullName,
                    "session-123",
                    CancellationToken.None),
                turn => !string.Equals(
                    turn.NotificationTurnId,
                    oldTurn.NotificationTurnId,
                    StringComparison.Ordinal));
            await WriteSummaryAsync(
                tempDirectory.FullName,
                "session-123",
                currentTurn,
                new NotificationSummary
                {
                    SessionId = "session-123",
                    NotificationTurnId = currentTurn.NotificationTurnId,
                    NotificationNonce = currentTurn.NotificationNonce,
                    UpdatedAt = stopTimestamp,
                    Summary = "The latest durable exact current summary should deliver.",
                });
            if (string.Equals(currentCacheState, "missing-current", StringComparison.Ordinal))
            {
                File.Delete(currentPath);
            }
            else if (string.Equals(currentCacheState, "corrupt-current", StringComparison.Ordinal))
            {
                await File.WriteAllTextAsync(currentPath, "{");
            }
            else
            {
                await File.WriteAllTextAsync(currentPath, staleCurrentJson);
            }

            _ = await service.HandleStopAsync(
                CreateJsonStream(
                    CreateStopInput(tempDirectory.FullName, stopTimestamp),
                    AppJsonSerializerContext.Default.StopHookInput),
                new MemoryStream(),
                CancellationToken.None);

            TelegramSendMessageRequest payload = DeserializeTelegramPayload(
                Assert.Single(handler.Requests));
            Assert.Contains(
                "摘要：The latest durable exact current summary should deliver.",
                payload.Text,
                StringComparison.Ordinal);
            Assert.False(File.Exists(AppPaths.GetNotificationRecordPath(
                tempDirectory.FullName,
                "session-123",
                oldTurn.NotificationTurnId,
                CreateStopNotificationKeyForTest(stopTimestamp))));
            Assert.True(File.Exists(AppPaths.GetNotificationRecordPath(
                tempDirectory.FullName,
                "session-123",
                currentTurn.NotificationTurnId,
                CreateStopNotificationKeyForTest(stopTimestamp))));
        }
        finally
        {
            tempDirectory.Delete(recursive: true);
        }
    }

    [Theory]
    [InlineData("missing-current")]
    [InlineData("corrupt-current")]
    public async Task HandleStopAsyncCachelessLatestPendingDefersInsteadOfOlderNonExactValid(
        string currentCacheState)
    {
        DirectoryInfo tempDirectory = Directory.CreateTempSubdirectory();
        using EnvironmentScope environment = SetTelegramEnvironment();

        try
        {
            WorkspaceStateStore stateStore = new(
                TimeProvider.System,
                NullLogger<WorkspaceStateStore>.Instance);
            const string stopTimestamp = "2026-03-14T15:52:50.783Z";
            NotificationTurn oldTurn = await CreateTurnAsync(
                stateStore,
                tempDirectory.FullName,
                "session-123",
                "2026-03-14T15:51:40.783Z");
            await WriteSummaryAsync(
                tempDirectory.FullName,
                "session-123",
                oldTurn,
                new NotificationSummary
                {
                    SessionId = "session-123",
                    NotificationTurnId = oldTurn.NotificationTurnId,
                    NotificationNonce = oldTurn.NotificationNonce,
                    UpdatedAt = "2026-03-14T15:52:49.783Z",
                    Summary = "The older non-exact valid summary must not deliver.",
                });
            NotificationTurn currentTurn = await CreateTurnAsync(
                stateStore,
                tempDirectory.FullName,
                "session-123",
                "2026-03-14T15:52:40.783Z");
            await WriteSummaryAsync(
                tempDirectory.FullName,
                "session-123",
                currentTurn,
                new NotificationSummary
                {
                    SessionId = "session-123",
                    NotificationTurnId = currentTurn.NotificationTurnId,
                    NotificationNonce = currentTurn.NotificationNonce,
                    UpdatedAt = stopTimestamp,
                    Status = "pending",
                    Summary = " ",
                });
            string currentPath = AppPaths.GetCurrentStatePath(
                tempDirectory.FullName,
                "session-123");
            if (string.Equals(currentCacheState, "missing-current", StringComparison.Ordinal))
            {
                File.Delete(currentPath);
            }
            else
            {
                await File.WriteAllTextAsync(currentPath, "{");
            }

            RecordingHttpMessageHandler handler = new();
            HookCommandService service = CreateHookCommandService(handler, stateStore);
            _ = await service.HandleStopAsync(
                CreateJsonStream(
                    CreateStopInput(tempDirectory.FullName, stopTimestamp),
                    AppJsonSerializerContext.Default.StopHookInput),
                new MemoryStream(),
                CancellationToken.None);

            Assert.Empty(handler.Requests);
            await AssertPendingStopAsync(
                stateStore,
                tempDirectory.FullName,
                currentTurn,
                stopTimestamp,
                "summary must be a non-empty human-readable sentence");
            Assert.False(File.Exists(AppPaths.GetNotificationRecordPath(
                tempDirectory.FullName,
                "session-123",
                oldTurn.NotificationTurnId,
                CreateStopNotificationKeyForTest(stopTimestamp))));
        }
        finally
        {
            tempDirectory.Delete(recursive: true);
        }
    }

    [Theory]
    [InlineData("missing-current")]
    [InlineData("corrupt-current")]
    public async Task HandleStopAsyncCachelessNonUniqueOlderExactFallsBackToLatestDurableNonExactValid(
        string currentCacheState)
    {
        DirectoryInfo tempDirectory = Directory.CreateTempSubdirectory();
        using EnvironmentScope environment = SetTelegramEnvironment();

        try
        {
            WorkspaceStateStore stateStore = new(
                TimeProvider.System,
                NullLogger<WorkspaceStateStore>.Instance);
            const string stopTimestamp = "2026-03-14T15:52:50.783Z";
            NotificationTurn firstOldTurn = await CreateTurnAsync(
                stateStore,
                tempDirectory.FullName,
                "session-123",
                "2026-03-14T15:51:40.783Z");
            await WriteSummaryAsync(
                tempDirectory.FullName,
                "session-123",
                firstOldTurn,
                new NotificationSummary
                {
                    SessionId = "session-123",
                    NotificationTurnId = firstOldTurn.NotificationTurnId,
                    NotificationNonce = firstOldTurn.NotificationNonce,
                    UpdatedAt = stopTimestamp,
                    Summary = "The first older exact summary is not unique.",
                });
            NotificationTurn secondOldTurn = await CreateTurnAsync(
                stateStore,
                tempDirectory.FullName,
                "session-123",
                "2026-03-14T15:51:40.783Z");
            await WriteSummaryAsync(
                tempDirectory.FullName,
                "session-123",
                secondOldTurn,
                new NotificationSummary
                {
                    SessionId = "session-123",
                    NotificationTurnId = secondOldTurn.NotificationTurnId,
                    NotificationNonce = secondOldTurn.NotificationNonce,
                    UpdatedAt = stopTimestamp,
                    Summary = "The second older exact summary is not unique.",
                });
            NotificationTurn currentTurn = await CreateTurnAsync(
                stateStore,
                tempDirectory.FullName,
                "session-123",
                "2026-03-14T15:52:40.783Z");
            await WriteSummaryAsync(
                tempDirectory.FullName,
                "session-123",
                currentTurn,
                new NotificationSummary
                {
                    SessionId = "session-123",
                    NotificationTurnId = currentTurn.NotificationTurnId,
                    NotificationNonce = currentTurn.NotificationNonce,
                    UpdatedAt = "2026-03-14T15:52:49.783Z",
                    Summary = "The latest durable non-exact valid summary should deliver.",
                });
            string currentPath = AppPaths.GetCurrentStatePath(
                tempDirectory.FullName,
                "session-123");
            if (string.Equals(currentCacheState, "missing-current", StringComparison.Ordinal))
            {
                File.Delete(currentPath);
            }
            else
            {
                await File.WriteAllTextAsync(currentPath, "{");
            }

            RecordingHttpMessageHandler handler = new();
            HookCommandService service = CreateHookCommandService(handler, stateStore);
            _ = await service.HandleStopAsync(
                CreateJsonStream(
                    CreateStopInput(tempDirectory.FullName, stopTimestamp),
                    AppJsonSerializerContext.Default.StopHookInput),
                new MemoryStream(),
                CancellationToken.None);

            TelegramSendMessageRequest payload = DeserializeTelegramPayload(
                Assert.Single(handler.Requests));
            Assert.Contains(
                "摘要：The latest durable non-exact valid summary should deliver.",
                payload.Text,
                StringComparison.Ordinal);
            Assert.Contains(currentTurn.NotificationTurnId, payload.Text, StringComparison.Ordinal);
        }
        finally
        {
            tempDirectory.Delete(recursive: true);
        }
    }

    [Theory]
    [InlineData("missing-current")]
    [InlineData("corrupt-current")]
    public async Task HandleStopAsyncLatestInvalidBeatsFreshClaimedOlderExact(
        string currentCacheState)
    {
        DirectoryInfo tempDirectory = Directory.CreateTempSubdirectory();
        using EnvironmentScope environment = SetTelegramEnvironment();

        try
        {
            WorkspaceStateStore stateStore = new(
                FixedUtcNow(),
                NullLogger<WorkspaceStateStore>.Instance);
            const string stopTimestamp = "2026-03-14T15:51:50.783Z";
            NotificationTurn oldTurn = await CreateTurnAsync(
                stateStore,
                tempDirectory.FullName,
                "session-123",
                "2026-03-14T15:51:30.783Z");
            await WriteSummaryAsync(
                tempDirectory.FullName,
                "session-123",
                oldTurn,
                new NotificationSummary
                {
                    SessionId = "session-123",
                    NotificationTurnId = oldTurn.NotificationTurnId,
                    NotificationNonce = oldTurn.NotificationNonce,
                    UpdatedAt = stopTimestamp,
                    Summary = "The fresh claimed older exact summary is already being delivered.",
                });
            NotificationTurn currentTurn = await CreateTurnAsync(
                stateStore,
                tempDirectory.FullName,
                "session-123",
                "2026-03-14T15:51:45.783Z");
            await File.WriteAllTextAsync(
                AppPaths.GetSummaryStatePath(
                    tempDirectory.FullName,
                    "session-123",
                    currentTurn.NotificationTurnId),
                "{}");
            string currentPath = AppPaths.GetCurrentStatePath(
                tempDirectory.FullName,
                "session-123");
            if (string.Equals(currentCacheState, "missing-current", StringComparison.Ordinal))
            {
                File.Delete(currentPath);
            }
            else
            {
                await File.WriteAllTextAsync(currentPath, "{");
            }

            string oldTurnClaimPath = AppPaths.GetTurnDeliveryClaimPath(
                tempDirectory.FullName,
                "session-123",
                oldTurn.NotificationTurnId);
            await WriteClaimAsync(oldTurnClaimPath, stopTimestamp);
            File.SetLastWriteTimeUtc(
                oldTurnClaimPath,
                new DateTime(2026, 3, 14, 15, 51, 50, 783, DateTimeKind.Utc));
            RecordingHttpMessageHandler handler = new();
            HookCommandService service = CreateHookCommandService(handler, stateStore);

            _ = await service.HandleStopAsync(
                CreateJsonStream(
                    CreateStopInput(tempDirectory.FullName, stopTimestamp),
                    AppJsonSerializerContext.Default.StopHookInput),
                new MemoryStream(),
                CancellationToken.None);

            TelegramSendMessageRequest payload = DeserializeTelegramPayload(
                Assert.Single(handler.Requests));
            Assert.Contains("摘要：当前轮未生成摘要。", payload.Text, StringComparison.Ordinal);
            Assert.Contains(currentTurn.NotificationTurnId, payload.Text, StringComparison.Ordinal);
            Assert.DoesNotContain(oldTurn.NotificationTurnId, payload.Text, StringComparison.Ordinal);
            Assert.True(File.Exists(AppPaths.GetSessionNotificationRecordPath(
                tempDirectory.FullName,
                "session-123",
                CreateStopNotificationKeyForTest(stopTimestamp))));
            Assert.True(File.Exists(oldTurnClaimPath));
        }
        finally
        {
            tempDirectory.Delete(recursive: true);
        }
    }

    [Theory]
    [InlineData("missing-current", "exact")]
    [InlineData("missing-current", "non-exact")]
    [InlineData("corrupt-current", "exact")]
    [InlineData("corrupt-current", "non-exact")]
    [InlineData("stale-current", "exact")]
    [InlineData("stale-current", "non-exact")]
    public async Task HandleStopAsyncFreshClaimedLatestExactCurrentSuppressesOlderWithoutReadableCurrentCache(
        string currentCacheState,
        string olderSummaryAttribution)
    {
        DirectoryInfo tempDirectory = Directory.CreateTempSubdirectory();
        using EnvironmentScope environment = SetTelegramEnvironment();

        try
        {
            WorkspaceStateStore stateStore = new(
                FixedUtcNow(),
                NullLogger<WorkspaceStateStore>.Instance);
            const string stopTimestamp = "2026-03-14T15:51:50.783Z";
            NotificationTurn oldTurn = await CreateTurnAsync(
                stateStore,
                tempDirectory.FullName,
                "session-123",
                "2026-03-14T15:51:30.783Z");
            await WriteSummaryAsync(
                tempDirectory.FullName,
                "session-123",
                oldTurn,
                new NotificationSummary
                {
                    SessionId = "session-123",
                    NotificationTurnId = oldTurn.NotificationTurnId,
                    NotificationNonce = oldTurn.NotificationNonce,
                    UpdatedAt = string.Equals(olderSummaryAttribution, "exact", StringComparison.Ordinal)
                        ? stopTimestamp
                        : "2026-03-14T15:51:49.783Z",
                    Summary = "The older summary must not deliver while the latest exact current is claimed.",
                });
            string staleCurrentJson = await File.ReadAllTextAsync(AppPaths.GetCurrentStatePath(
                tempDirectory.FullName,
                "session-123"));
            NotificationTurn currentTurn = await CreateTurnAsync(
                stateStore,
                tempDirectory.FullName,
                "session-123",
                "2026-03-14T15:51:45.783Z");
            await WriteSummaryAsync(
                tempDirectory.FullName,
                "session-123",
                currentTurn,
                new NotificationSummary
                {
                    SessionId = "session-123",
                    NotificationTurnId = currentTurn.NotificationTurnId,
                    NotificationNonce = currentTurn.NotificationNonce,
                    UpdatedAt = stopTimestamp,
                    Summary = "The fresh claimed latest current exact summary should own this Stop.",
                });
            string currentPath = AppPaths.GetCurrentStatePath(
                tempDirectory.FullName,
                "session-123");
            if (string.Equals(currentCacheState, "missing-current", StringComparison.Ordinal))
            {
                File.Delete(currentPath);
            }
            else if (string.Equals(currentCacheState, "corrupt-current", StringComparison.Ordinal))
            {
                await File.WriteAllTextAsync(currentPath, "{");
            }
            else
            {
                await File.WriteAllTextAsync(currentPath, staleCurrentJson);
            }

            string currentTurnClaimPath = AppPaths.GetTurnDeliveryClaimPath(
                tempDirectory.FullName,
                "session-123",
                currentTurn.NotificationTurnId);
            await WriteClaimAsync(currentTurnClaimPath, stopTimestamp);
            File.SetLastWriteTimeUtc(
                currentTurnClaimPath,
                new DateTime(2026, 3, 14, 15, 51, 50, 783, DateTimeKind.Utc));
            RecordingHttpMessageHandler handler = new();
            HookCommandService service = CreateHookCommandService(handler, stateStore);

            _ = await service.HandleStopAsync(
                CreateJsonStream(
                    CreateStopInput(tempDirectory.FullName, stopTimestamp),
                    AppJsonSerializerContext.Default.StopHookInput),
                new MemoryStream(),
                CancellationToken.None);

            Assert.Empty(handler.Requests);
            Assert.False(File.Exists(AppPaths.GetNotificationRecordPath(
                tempDirectory.FullName,
                "session-123",
                oldTurn.NotificationTurnId,
                CreateStopNotificationKeyForTest(stopTimestamp))));
            Assert.False(File.Exists(AppPaths.GetSessionNotificationRecordPath(
                tempDirectory.FullName,
                "session-123",
                CreateStopNotificationKeyForTest(stopTimestamp))));
            Assert.True(File.Exists(currentTurnClaimPath));
        }
        finally
        {
            tempDirectory.Delete(recursive: true);
        }
    }

    [Theory]
    [InlineData("missing-current")]
    [InlineData("corrupt-current")]
    [InlineData("stale-current")]
    public async Task HandleStopAsyncFreshClaimedLatestNonExactSuppressesOlderWithoutReadableCurrentCache(
        string currentCacheState)
    {
        DirectoryInfo tempDirectory = Directory.CreateTempSubdirectory();
        using EnvironmentScope environment = SetTelegramEnvironment();

        try
        {
            WorkspaceStateStore stateStore = new(
                FixedUtcNow(),
                NullLogger<WorkspaceStateStore>.Instance);
            const string stopTimestamp = "2026-03-14T15:51:50.783Z";
            NotificationTurn oldTurn = await CreateTurnAsync(
                stateStore,
                tempDirectory.FullName,
                "session-123",
                "2026-03-14T15:51:30.783Z");
            await WriteSummaryAsync(
                tempDirectory.FullName,
                "session-123",
                oldTurn,
                new NotificationSummary
                {
                    SessionId = "session-123",
                    NotificationTurnId = oldTurn.NotificationTurnId,
                    NotificationNonce = oldTurn.NotificationNonce,
                    UpdatedAt = "2026-03-14T15:51:49.783Z",
                    Summary = "The older non-exact summary must not deliver while latest is claimed.",
                });
            string currentPath = AppPaths.GetCurrentStatePath(
                tempDirectory.FullName,
                "session-123");
            string staleCurrentJson = await File.ReadAllTextAsync(currentPath);
            NotificationTurn latestTurn = await CreateTurnAsync(
                stateStore,
                tempDirectory.FullName,
                "session-123",
                "2026-03-14T15:51:45.783Z");
            await WriteSummaryAsync(
                tempDirectory.FullName,
                "session-123",
                latestTurn,
                new NotificationSummary
                {
                    SessionId = "session-123",
                    NotificationTurnId = latestTurn.NotificationTurnId,
                    NotificationNonce = latestTurn.NotificationNonce,
                    UpdatedAt = "2026-03-14T15:51:49.783Z",
                    Summary = "The latest non-exact summary is already being delivered.",
                });
            if (string.Equals(currentCacheState, "missing-current", StringComparison.Ordinal))
            {
                File.Delete(currentPath);
            }
            else if (string.Equals(currentCacheState, "corrupt-current", StringComparison.Ordinal))
            {
                await File.WriteAllTextAsync(currentPath, "{");
            }
            else
            {
                await File.WriteAllTextAsync(currentPath, staleCurrentJson);
            }

            string latestTurnClaimPath = AppPaths.GetTurnDeliveryClaimPath(
                tempDirectory.FullName,
                "session-123",
                latestTurn.NotificationTurnId);
            await WriteClaimAsync(latestTurnClaimPath, string.Empty);
            File.SetLastWriteTimeUtc(
                latestTurnClaimPath,
                new DateTime(2026, 3, 14, 15, 51, 49, 783, DateTimeKind.Utc));
            RecordingHttpMessageHandler handler = new();
            HookCommandService service = CreateHookCommandService(handler, stateStore);

            _ = await service.HandleStopAsync(
                CreateJsonStream(
                    CreateStopInput(tempDirectory.FullName, stopTimestamp),
                    AppJsonSerializerContext.Default.StopHookInput),
                new MemoryStream(),
                CancellationToken.None);

            Assert.Empty(handler.Requests);
            Assert.False(File.Exists(AppPaths.GetNotificationRecordPath(
                tempDirectory.FullName,
                "session-123",
                oldTurn.NotificationTurnId,
                CreateStopNotificationKeyForTest(stopTimestamp))));
            Assert.False(File.Exists(AppPaths.GetSessionNotificationRecordPath(
                tempDirectory.FullName,
                "session-123",
                CreateStopNotificationKeyForTest(stopTimestamp))));
            Assert.True(File.Exists(latestTurnClaimPath));

            File.Delete(latestTurnClaimPath);

            _ = await service.HandleStopAsync(
                CreateJsonStream(
                    CreateStopInput(tempDirectory.FullName, stopTimestamp),
                    AppJsonSerializerContext.Default.StopHookInput),
                new MemoryStream(),
                CancellationToken.None);

            Assert.Single(handler.Requests);
            Assert.False(File.Exists(AppPaths.GetNotificationRecordPath(
                tempDirectory.FullName,
                "session-123",
                oldTurn.NotificationTurnId,
                CreateStopNotificationKeyForTest(stopTimestamp))));
            Assert.True(File.Exists(AppPaths.GetNotificationRecordPath(
                tempDirectory.FullName,
                "session-123",
                latestTurn.NotificationTurnId,
                CreateStopNotificationKeyForTest(stopTimestamp))));
        }
        finally
        {
            tempDirectory.Delete(recursive: true);
        }
    }

    [Fact]
    public async Task HandleStopAsyncFreshClaimedLatestNonExactSuppressesMultipleOlderExactThenDeliversLatest()
    {
        DirectoryInfo tempDirectory = Directory.CreateTempSubdirectory();
        using EnvironmentScope environment = SetTelegramEnvironment();

        try
        {
            WorkspaceStateStore stateStore = new(
                FixedUtcNow(),
                NullLogger<WorkspaceStateStore>.Instance);
            const string stopTimestamp = "2026-03-14T15:51:50.783Z";
            NotificationTurn firstOldTurn = await CreateTurnAsync(
                stateStore,
                tempDirectory.FullName,
                "session-123",
                "2026-03-14T15:51:30.783Z");
            await WriteSummaryAsync(
                tempDirectory.FullName,
                "session-123",
                firstOldTurn,
                new NotificationSummary
                {
                    SessionId = "session-123",
                    NotificationTurnId = firstOldTurn.NotificationTurnId,
                    NotificationNonce = firstOldTurn.NotificationNonce,
                    UpdatedAt = stopTimestamp,
                    Summary = "The first older exact summary must wait behind the latest claim.",
                });
            NotificationTurn secondOldTurn = await CreateTurnAsync(
                stateStore,
                tempDirectory.FullName,
                "session-123",
                "2026-03-14T15:51:35.783Z");
            await WriteSummaryAsync(
                tempDirectory.FullName,
                "session-123",
                secondOldTurn,
                new NotificationSummary
                {
                    SessionId = "session-123",
                    NotificationTurnId = secondOldTurn.NotificationTurnId,
                    NotificationNonce = secondOldTurn.NotificationNonce,
                    UpdatedAt = stopTimestamp,
                    Summary = "The second older exact summary must wait behind the latest claim.",
                });
            NotificationTurn latestTurn = await CreateTurnAsync(
                stateStore,
                tempDirectory.FullName,
                "session-123",
                "2026-03-14T15:51:45.783Z");
            await WriteSummaryAsync(
                tempDirectory.FullName,
                "session-123",
                latestTurn,
                new NotificationSummary
                {
                    SessionId = "session-123",
                    NotificationTurnId = latestTurn.NotificationTurnId,
                    NotificationNonce = latestTurn.NotificationNonce,
                    UpdatedAt = "2026-03-14T15:51:49.783Z",
                    Summary = "The claimed latest non-exact summary should deliver after the claim clears.",
                });
            File.Delete(AppPaths.GetCurrentStatePath(tempDirectory.FullName, "session-123"));

            string latestTurnClaimPath = AppPaths.GetTurnDeliveryClaimPath(
                tempDirectory.FullName,
                "session-123",
                latestTurn.NotificationTurnId);
            await WriteClaimAsync(latestTurnClaimPath, string.Empty);
            File.SetLastWriteTimeUtc(
                latestTurnClaimPath,
                new DateTime(2026, 3, 14, 15, 51, 49, 783, DateTimeKind.Utc));
            RecordingHttpMessageHandler handler = new();
            HookCommandService service = CreateHookCommandService(handler, stateStore);

            _ = await service.HandleStopAsync(
                CreateJsonStream(
                    CreateStopInput(tempDirectory.FullName, stopTimestamp),
                    AppJsonSerializerContext.Default.StopHookInput),
                new MemoryStream(),
                CancellationToken.None);

            Assert.Empty(handler.Requests);
            Assert.False(File.Exists(AppPaths.GetSessionNotificationRecordPath(
                tempDirectory.FullName,
                "session-123",
                CreateStopNotificationKeyForTest(stopTimestamp))));

            File.Delete(latestTurnClaimPath);
            _ = await service.HandleStopAsync(
                CreateJsonStream(
                    CreateStopInput(tempDirectory.FullName, stopTimestamp),
                    AppJsonSerializerContext.Default.StopHookInput),
                new MemoryStream(),
                CancellationToken.None);

            TelegramSendMessageRequest payload = DeserializeTelegramPayload(
                Assert.Single(handler.Requests));
            Assert.Contains(
                "摘要：The claimed latest non-exact summary should deliver after the claim clears.",
                payload.Text,
                StringComparison.Ordinal);
            Assert.Contains(latestTurn.NotificationTurnId, payload.Text, StringComparison.Ordinal);
            Assert.DoesNotContain(firstOldTurn.NotificationTurnId, payload.Text, StringComparison.Ordinal);
            Assert.DoesNotContain(secondOldTurn.NotificationTurnId, payload.Text, StringComparison.Ordinal);
        }
        finally
        {
            tempDirectory.Delete(recursive: true);
        }
    }

    [Theory]
    [InlineData("older-non-exact")]
    [InlineData("no-open-fallback")]
    public async Task HandleStopAsyncFreshClaimedLatestInvalidSummarySuppressesFallbacks(
        string scenario)
    {
        DirectoryInfo tempDirectory = Directory.CreateTempSubdirectory();
        using EnvironmentScope environment = SetTelegramEnvironment();

        try
        {
            WorkspaceStateStore stateStore = new(
                FixedUtcNow(),
                NullLogger<WorkspaceStateStore>.Instance);
            const string stopTimestamp = "2026-03-14T15:51:50.783Z";
            NotificationTurn? oldTurn = null;
            if (string.Equals(scenario, "older-non-exact", StringComparison.Ordinal))
            {
                oldTurn = await CreateTurnAsync(
                    stateStore,
                    tempDirectory.FullName,
                    "session-123",
                    "2026-03-14T15:51:30.783Z");
                await WriteSummaryAsync(
                    tempDirectory.FullName,
                    "session-123",
                    oldTurn,
                    new NotificationSummary
                    {
                        SessionId = "session-123",
                        NotificationTurnId = oldTurn.NotificationTurnId,
                        NotificationNonce = oldTurn.NotificationNonce,
                        UpdatedAt = "2026-03-14T15:51:49.783Z",
                        Summary = "The older non-exact summary must not become a fallback.",
                    });
            }

            NotificationTurn latestTurn = await CreateTurnAsync(
                stateStore,
                tempDirectory.FullName,
                "session-123",
                "2026-03-14T15:51:45.783Z");
            await File.WriteAllTextAsync(
                AppPaths.GetSummaryStatePath(
                    tempDirectory.FullName,
                    "session-123",
                    latestTurn.NotificationTurnId),
                "{}");
            File.Delete(AppPaths.GetCurrentStatePath(tempDirectory.FullName, "session-123"));
            string latestTurnClaimPath = AppPaths.GetTurnDeliveryClaimPath(
                tempDirectory.FullName,
                "session-123",
                latestTurn.NotificationTurnId);
            await WriteClaimAsync(latestTurnClaimPath, string.Empty);
            File.SetLastWriteTimeUtc(
                latestTurnClaimPath,
                new DateTime(2026, 3, 14, 15, 51, 49, 783, DateTimeKind.Utc));
            RecordingHttpMessageHandler handler = new();
            HookCommandService service = CreateHookCommandService(handler, stateStore);

            _ = await service.HandleStopAsync(
                CreateJsonStream(
                    CreateStopInput(tempDirectory.FullName, stopTimestamp),
                    AppJsonSerializerContext.Default.StopHookInput),
                new MemoryStream(),
                CancellationToken.None);

            Assert.Empty(handler.Requests);
            Assert.True(File.Exists(latestTurnClaimPath));
            Assert.False(File.Exists(AppPaths.GetSessionNotificationRecordPath(
                tempDirectory.FullName,
                "session-123",
                CreateStopNotificationKeyForTest(stopTimestamp))));
            if (oldTurn is not null)
            {
                Assert.False(File.Exists(AppPaths.GetNotificationRecordPath(
                    tempDirectory.FullName,
                    "session-123",
                    oldTurn.NotificationTurnId,
                    CreateStopNotificationKeyForTest(stopTimestamp))));
            }
        }
        finally
        {
            tempDirectory.Delete(recursive: true);
        }
    }

    [Theory]
    [InlineData("missing-current")]
    [InlineData("corrupt-current")]
    [InlineData("stale-current")]
    public async Task HandleStopAsyncFreshClaimedLatestInvalidSummaryDeliversLatestAfterClaimClears(
        string currentCacheState)
    {
        DirectoryInfo tempDirectory = Directory.CreateTempSubdirectory();
        using EnvironmentScope environment = SetTelegramEnvironment();

        try
        {
            WorkspaceStateStore stateStore = new(
                FixedUtcNow(),
                NullLogger<WorkspaceStateStore>.Instance);
            const string stopTimestamp = "2026-03-14T15:51:50.783Z";
            NotificationTurn oldTurn = await CreateTurnAsync(
                stateStore,
                tempDirectory.FullName,
                "session-123",
                "2026-03-14T15:51:30.783Z");
            await WriteSummaryAsync(
                tempDirectory.FullName,
                "session-123",
                oldTurn,
                new NotificationSummary
                {
                    SessionId = "session-123",
                    NotificationTurnId = oldTurn.NotificationTurnId,
                    NotificationNonce = oldTurn.NotificationNonce,
                    UpdatedAt = "2026-03-14T15:51:49.783Z",
                    Summary = "The older non-exact summary must not win after the latest claim clears.",
                });
            string currentPath = AppPaths.GetCurrentStatePath(
                tempDirectory.FullName,
                "session-123");
            string staleCurrentJson = await File.ReadAllTextAsync(currentPath);
            NotificationTurn latestTurn = await CreateTurnAsync(
                stateStore,
                tempDirectory.FullName,
                "session-123",
                "2026-03-14T15:51:45.783Z");
            await WriteRawSummaryJsonAsync(tempDirectory.FullName, "session-123", latestTurn, "{}");
            if (string.Equals(currentCacheState, "missing-current", StringComparison.Ordinal))
            {
                File.Delete(currentPath);
            }
            else if (string.Equals(currentCacheState, "corrupt-current", StringComparison.Ordinal))
            {
                await File.WriteAllTextAsync(currentPath, "{");
            }
            else
            {
                await File.WriteAllTextAsync(currentPath, staleCurrentJson);
            }

            string latestTurnClaimPath = AppPaths.GetTurnDeliveryClaimPath(
                tempDirectory.FullName,
                "session-123",
                latestTurn.NotificationTurnId);
            await WriteClaimAsync(latestTurnClaimPath, string.Empty);
            File.SetLastWriteTimeUtc(
                latestTurnClaimPath,
                new DateTime(2026, 3, 14, 15, 51, 49, 783, DateTimeKind.Utc));
            RecordingHttpMessageHandler handler = new();
            HookCommandService service = CreateHookCommandService(handler, stateStore);

            _ = await service.HandleStopAsync(
                CreateJsonStream(
                    CreateStopInput(tempDirectory.FullName, stopTimestamp),
                    AppJsonSerializerContext.Default.StopHookInput),
                new MemoryStream(),
                CancellationToken.None);
            Assert.Empty(handler.Requests);
            Assert.True(File.Exists(latestTurnClaimPath));
            Assert.False(File.Exists(AppPaths.GetNotificationRecordPath(
                tempDirectory.FullName,
                "session-123",
                oldTurn.NotificationTurnId,
                CreateStopNotificationKeyForTest(stopTimestamp))));
            Assert.False(File.Exists(AppPaths.GetNotificationRecordPath(
                tempDirectory.FullName,
                "session-123",
                latestTurn.NotificationTurnId,
                CreateStopNotificationKeyForTest(stopTimestamp))));
            Assert.False(File.Exists(AppPaths.GetSessionNotificationRecordPath(
                tempDirectory.FullName,
                "session-123",
                CreateStopNotificationKeyForTest(stopTimestamp))));

            File.Delete(latestTurnClaimPath);
            _ = await service.HandleStopAsync(
                CreateJsonStream(
                    CreateStopInput(tempDirectory.FullName, stopTimestamp),
                    AppJsonSerializerContext.Default.StopHookInput),
                new MemoryStream(),
                CancellationToken.None);

            TelegramSendMessageRequest payload = DeserializeTelegramPayload(
                Assert.Single(handler.Requests));
            Assert.Contains("摘要：当前轮未生成摘要。", payload.Text, StringComparison.Ordinal);
            Assert.Contains(latestTurn.NotificationTurnId, payload.Text, StringComparison.Ordinal);
            Assert.False(File.Exists(AppPaths.GetNotificationRecordPath(
                tempDirectory.FullName,
                "session-123",
                oldTurn.NotificationTurnId,
                CreateStopNotificationKeyForTest(stopTimestamp))));
            Assert.True(File.Exists(AppPaths.GetNotificationRecordPath(
                tempDirectory.FullName,
                "session-123",
                latestTurn.NotificationTurnId,
                CreateStopNotificationKeyForTest(stopTimestamp))));
        }
        finally
        {
            tempDirectory.Delete(recursive: true);
        }
    }

    [Theory]
    [InlineData("missing-current")]
    [InlineData("corrupt-current")]
    [InlineData("stale-current")]
    public async Task HandleStopAsyncLatestInvalidSummaryBeatsSingleOlderExactAfterClaimClears(
        string currentCacheState)
    {
        DirectoryInfo tempDirectory = Directory.CreateTempSubdirectory();
        using EnvironmentScope environment = SetTelegramEnvironment();

        try
        {
            WorkspaceStateStore stateStore = new(
                FixedUtcNow(),
                NullLogger<WorkspaceStateStore>.Instance);
            const string stopTimestamp = "2026-03-14T15:51:50.783Z";
            NotificationTurn exactOldTurn = await CreateTurnAsync(
                stateStore,
                tempDirectory.FullName,
                "session-123",
                "2026-03-14T15:51:30.783Z");
            await WriteSummaryAsync(
                tempDirectory.FullName,
                "session-123",
                exactOldTurn,
                new NotificationSummary
                {
                    SessionId = "session-123",
                    NotificationTurnId = exactOldTurn.NotificationTurnId,
                    NotificationNonce = exactOldTurn.NotificationNonce,
                    UpdatedAt = stopTimestamp,
                    Summary = "The older exact summary must not win after the latest invalid claim clears.",
                });
            string currentPath = AppPaths.GetCurrentStatePath(
                tempDirectory.FullName,
                "session-123");
            string staleCurrentJson = await File.ReadAllTextAsync(currentPath);
            NotificationTurn latestTurn = await CreateTurnAsync(
                stateStore,
                tempDirectory.FullName,
                "session-123",
                "2026-03-14T15:51:45.783Z");
            await WriteRawSummaryJsonAsync(tempDirectory.FullName, "session-123", latestTurn, "{}");
            if (string.Equals(currentCacheState, "missing-current", StringComparison.Ordinal))
            {
                File.Delete(currentPath);
            }
            else if (string.Equals(currentCacheState, "corrupt-current", StringComparison.Ordinal))
            {
                await File.WriteAllTextAsync(currentPath, "{");
            }
            else
            {
                await File.WriteAllTextAsync(currentPath, staleCurrentJson);
            }

            string latestTurnClaimPath = AppPaths.GetTurnDeliveryClaimPath(
                tempDirectory.FullName,
                "session-123",
                latestTurn.NotificationTurnId);
            await WriteClaimAsync(latestTurnClaimPath, string.Empty);
            File.SetLastWriteTimeUtc(
                latestTurnClaimPath,
                new DateTime(2026, 3, 14, 15, 51, 49, 783, DateTimeKind.Utc));
            RecordingHttpMessageHandler handler = new();
            HookCommandService service = CreateHookCommandService(handler, stateStore);

            _ = await service.HandleStopAsync(
                CreateJsonStream(
                    CreateStopInput(tempDirectory.FullName, stopTimestamp),
                    AppJsonSerializerContext.Default.StopHookInput),
                new MemoryStream(),
                CancellationToken.None);
            Assert.Empty(handler.Requests);
            Assert.True(File.Exists(latestTurnClaimPath));
            Assert.False(File.Exists(AppPaths.GetNotificationRecordPath(
                tempDirectory.FullName,
                "session-123",
                exactOldTurn.NotificationTurnId,
                CreateStopNotificationKeyForTest(stopTimestamp))));
            Assert.False(File.Exists(AppPaths.GetNotificationRecordPath(
                tempDirectory.FullName,
                "session-123",
                latestTurn.NotificationTurnId,
                CreateStopNotificationKeyForTest(stopTimestamp))));
            Assert.False(File.Exists(AppPaths.GetSessionNotificationRecordPath(
                tempDirectory.FullName,
                "session-123",
                CreateStopNotificationKeyForTest(stopTimestamp))));

            File.Delete(latestTurnClaimPath);
            _ = await service.HandleStopAsync(
                CreateJsonStream(
                    CreateStopInput(tempDirectory.FullName, stopTimestamp),
                    AppJsonSerializerContext.Default.StopHookInput),
                new MemoryStream(),
                CancellationToken.None);

            TelegramSendMessageRequest payload = DeserializeTelegramPayload(
                Assert.Single(handler.Requests));
            Assert.Contains("摘要：当前轮未生成摘要。", payload.Text, StringComparison.Ordinal);
            Assert.Contains(latestTurn.NotificationTurnId, payload.Text, StringComparison.Ordinal);
            Assert.DoesNotContain(exactOldTurn.NotificationTurnId, payload.Text, StringComparison.Ordinal);
            Assert.False(File.Exists(AppPaths.GetNotificationRecordPath(
                tempDirectory.FullName,
                "session-123",
                exactOldTurn.NotificationTurnId,
                CreateStopNotificationKeyForTest(stopTimestamp))));
            Assert.True(File.Exists(AppPaths.GetNotificationRecordPath(
                tempDirectory.FullName,
                "session-123",
                latestTurn.NotificationTurnId,
                CreateStopNotificationKeyForTest(stopTimestamp))));
        }
        finally
        {
            tempDirectory.Delete(recursive: true);
        }
    }

    [Theory]
    [InlineData("missing-current", "blank")]
    [InlineData("missing-current", "missing")]
    [InlineData("missing-current", "corrupt")]
    [InlineData("missing-current", "json-null")]
    [InlineData("corrupt-current", "blank")]
    [InlineData("corrupt-current", "missing")]
    [InlineData("corrupt-current", "corrupt")]
    [InlineData("corrupt-current", "json-null")]
    [InlineData("stale-current", "blank")]
    [InlineData("stale-current", "missing")]
    [InlineData("stale-current", "corrupt")]
    [InlineData("stale-current", "json-null")]
    public async Task HandleStopAsyncDurableLatestExactCurrentAbandonsOlderInvalidWithoutReadableCurrentCache(
        string currentCacheState,
        string olderSummaryState)
    {
        DirectoryInfo tempDirectory = Directory.CreateTempSubdirectory();
        using EnvironmentScope environment = SetTelegramEnvironment();

        try
        {
            WorkspaceStateStore stateStore = new(
                TimeProvider.System,
                NullLogger<WorkspaceStateStore>.Instance);
            const string stopTimestamp = "2026-03-14T15:52:50.783Z";
            NotificationTurn oldTurn = await CreateTurnAsync(
                stateStore,
                tempDirectory.FullName,
                "session-123",
                "2026-03-14T15:51:40.783Z");
            string staleCurrentJson = await File.ReadAllTextAsync(AppPaths.GetCurrentStatePath(
                tempDirectory.FullName,
                "session-123"));
            await WriteSummaryAsync(
                tempDirectory.FullName,
                "session-123",
                oldTurn,
                new NotificationSummary
                {
                    SessionId = "session-123",
                    NotificationTurnId = oldTurn.NotificationTurnId,
                    NotificationNonce = oldTurn.NotificationNonce,
                    UpdatedAt = "2026-03-14T15:51:45.783Z",
                    Summary = "This initially complete older turn must survive current creation.",
                });
            NotificationTurn currentTurn = await CreateTurnAsync(
                stateStore,
                tempDirectory.FullName,
                "session-123",
                "2026-03-14T15:52:40.783Z");
            oldTurn.Status = "open";
            await WriteTurnStateAsync(tempDirectory.FullName, oldTurn);
            string oldSummaryPath = AppPaths.GetSummaryStatePath(
                tempDirectory.FullName,
                "session-123",
                oldTurn.NotificationTurnId);
            if (string.Equals(olderSummaryState, "missing", StringComparison.Ordinal))
            {
                File.Delete(oldSummaryPath);
            }
            else if (string.Equals(olderSummaryState, "corrupt", StringComparison.Ordinal))
            {
                await File.WriteAllTextAsync(oldSummaryPath, "{");
            }
            else if (string.Equals(olderSummaryState, "json-null", StringComparison.Ordinal))
            {
                await File.WriteAllTextAsync(oldSummaryPath, "null");
            }
            else
            {
                await WriteSummaryAsync(
                    tempDirectory.FullName,
                    "session-123",
                    oldTurn,
                    new NotificationSummary
                    {
                        SessionId = "session-123",
                        NotificationTurnId = oldTurn.NotificationTurnId,
                        NotificationNonce = oldTurn.NotificationNonce,
                        UpdatedAt = stopTimestamp,
                        Status = "pending",
                        Summary = " ",
                    });
            }
            await WriteSummaryAsync(
                tempDirectory.FullName,
                "session-123",
                currentTurn,
                new NotificationSummary
                {
                    SessionId = "session-123",
                    NotificationTurnId = currentTurn.NotificationTurnId,
                    NotificationNonce = currentTurn.NotificationNonce,
                    UpdatedAt = stopTimestamp,
                    Summary = "The durable latest exact current should supersede older invalid turns.",
                });
            string currentPath = AppPaths.GetCurrentStatePath(
                tempDirectory.FullName,
                "session-123");
            if (string.Equals(currentCacheState, "missing-current", StringComparison.Ordinal))
            {
                File.Delete(currentPath);
            }
            else if (string.Equals(currentCacheState, "corrupt-current", StringComparison.Ordinal))
            {
                await File.WriteAllTextAsync(currentPath, "{");
            }
            else
            {
                await File.WriteAllTextAsync(currentPath, staleCurrentJson);
            }

            RecordingHttpMessageHandler handler = new();
            HookCommandService service = CreateHookCommandService(handler, stateStore);
            _ = await service.HandleStopAsync(
                CreateJsonStream(
                    CreateStopInput(tempDirectory.FullName, stopTimestamp),
                    AppJsonSerializerContext.Default.StopHookInput),
                new MemoryStream(),
                CancellationToken.None);

            if (string.Equals(olderSummaryState, "blank", StringComparison.Ordinal))
            {
                Assert.Empty(handler.Requests);
                Assert.False(File.Exists(AppPaths.GetNotificationRecordPath(
                    tempDirectory.FullName,
                    "session-123",
                    currentTurn.NotificationTurnId,
                    CreateStopNotificationKeyForTest(stopTimestamp))));
                return;
            }

            TelegramSendMessageRequest payload = DeserializeTelegramPayload(
                Assert.Single(handler.Requests));
            Assert.Contains(
                "摘要：The durable latest exact current should supersede older invalid turns.",
                payload.Text,
                StringComparison.Ordinal);
            NotificationTurn? storedOldTurn = await stateStore.TryReadTurnAsync(
                tempDirectory.FullName,
                "session-123",
                oldTurn.NotificationTurnId,
                CancellationToken.None);
            Assert.Equal("abandoned", storedOldTurn?.Status);
        }
        finally
        {
            tempDirectory.Delete(recursive: true);
        }
    }

    [Fact]
    public async Task HandleStopAsyncStaleCurrentCacheDoesNotAbandonNewerExactPendingTurn()
    {
        DirectoryInfo tempDirectory = Directory.CreateTempSubdirectory();
        using EnvironmentScope environment = SetTelegramEnvironment();

        try
        {
            WorkspaceStateStore stateStore = new(
                TimeProvider.System,
                NullLogger<WorkspaceStateStore>.Instance);
            const string stopTimestamp = "2026-03-14T15:52:50.783Z";
            NotificationTurn oldTurn = await CreateTurnAsync(
                stateStore,
                tempDirectory.FullName,
                "session-123",
                "2026-03-14T15:51:40.783Z");
            string currentPath = AppPaths.GetCurrentStatePath(
                tempDirectory.FullName,
                "session-123");
            string staleCurrentJson = await File.ReadAllTextAsync(currentPath);
            await WriteSummaryAsync(
                tempDirectory.FullName,
                "session-123",
                oldTurn,
                new NotificationSummary
                {
                    SessionId = "session-123",
                    NotificationTurnId = oldTurn.NotificationTurnId,
                    NotificationNonce = oldTurn.NotificationNonce,
                    UpdatedAt = stopTimestamp,
                    Summary = "The stale current cache summary must not steal newer pending attribution.",
                });
            RecordingHttpMessageHandler handler = new();
            HookCommandService service = CreateHookCommandService(handler, stateStore);

            _ = await service.HandleUserPromptSubmitAsync(
                CreateJsonStream(
                    new UserPromptSubmitHookInput
                    {
                        Cwd = tempDirectory.FullName,
                        SessionId = "session-123",
                        Timestamp = "2026-03-14T15:52:40.783Z",
                        TranscriptPath = "/workspace/transcript.json",
                        Prompt = "Start a newer turn while current cache later becomes stale.",
                    },
                    AppJsonSerializerContext.Default.UserPromptSubmitHookInput),
                new MemoryStream(),
                CancellationToken.None);
            NotificationTurn currentTurn = Assert.Single(
                await stateStore.ListOpenTurnsAsync(
                    tempDirectory.FullName,
                    "session-123",
                    CancellationToken.None),
                turn => !string.Equals(
                    turn.NotificationTurnId,
                    oldTurn.NotificationTurnId,
                    StringComparison.Ordinal));
            await WriteSummaryAsync(
                tempDirectory.FullName,
                "session-123",
                currentTurn,
                new NotificationSummary
                {
                    SessionId = "session-123",
                    NotificationTurnId = currentTurn.NotificationTurnId,
                    NotificationNonce = currentTurn.NotificationNonce,
                    UpdatedAt = stopTimestamp,
                    Status = "pending",
                    Summary = null,
                });
            await File.WriteAllTextAsync(currentPath, staleCurrentJson);

            _ = await service.HandleStopAsync(
                CreateJsonStream(
                    CreateStopInput(tempDirectory.FullName, stopTimestamp),
                    AppJsonSerializerContext.Default.StopHookInput),
                new MemoryStream(),
                CancellationToken.None);

            Assert.Empty(handler.Requests);
            await AssertPendingStopAsync(
                stateStore,
                tempDirectory.FullName,
                currentTurn,
                stopTimestamp,
                "summary must be a non-empty human-readable sentence");
            NotificationTurn? storedCurrentTurn = await stateStore.TryReadTurnAsync(
                tempDirectory.FullName,
                "session-123",
                currentTurn.NotificationTurnId,
                CancellationToken.None);
            Assert.Equal("open", storedCurrentTurn?.Status);
            Assert.False(File.Exists(AppPaths.GetNotificationRecordPath(
                tempDirectory.FullName,
                "session-123",
                oldTurn.NotificationTurnId,
                CreateStopNotificationKeyForTest(stopTimestamp))));
        }
        finally
        {
            tempDirectory.Delete(recursive: true);
        }
    }

    [Theory]
    [InlineData("missing-current")]
    [InlineData("corrupt-current")]
    [InlineData("stale-current")]
    public async Task HandleStopAsyncCacheRecoveryOlderExactPendingSuppressesLatestPendingTurn(
        string currentCacheState)
    {
        DirectoryInfo tempDirectory = Directory.CreateTempSubdirectory();
        using EnvironmentScope environment = SetTelegramEnvironment();

        try
        {
            WorkspaceStateStore stateStore = new(
                TimeProvider.System,
                NullLogger<WorkspaceStateStore>.Instance);
            const string stopTimestamp = "2026-03-14T15:52:50.783Z";
            NotificationTurn oldTurn = await CreateTurnAsync(
                stateStore,
                tempDirectory.FullName,
                "session-123",
                "2026-03-14T15:51:40.783Z");
            string staleCurrentJson = await File.ReadAllTextAsync(AppPaths.GetCurrentStatePath(
                tempDirectory.FullName,
                "session-123"));
            await WriteSummaryAsync(
                tempDirectory.FullName,
                "session-123",
                oldTurn,
                new NotificationSummary
                {
                    SessionId = "session-123",
                    NotificationTurnId = oldTurn.NotificationTurnId,
                    NotificationNonce = oldTurn.NotificationNonce,
                    UpdatedAt = stopTimestamp,
                    Status = "pending",
                    Summary = null,
                });
            NotificationTurn currentTurn = await CreateTurnAsync(
                stateStore,
                tempDirectory.FullName,
                "session-123",
                "2026-03-14T15:52:40.783Z");
            oldTurn.Status = "open";
            await WriteTurnStateAsync(tempDirectory.FullName, oldTurn);
            await WriteSummaryAsync(
                tempDirectory.FullName,
                "session-123",
                currentTurn,
                new NotificationSummary
                {
                    SessionId = "session-123",
                    NotificationTurnId = currentTurn.NotificationTurnId,
                    NotificationNonce = currentTurn.NotificationNonce,
                    UpdatedAt = stopTimestamp,
                    Status = "pending",
                    Summary = " ",
                });
            string currentPath = AppPaths.GetCurrentStatePath(
                tempDirectory.FullName,
                "session-123");
            if (string.Equals(currentCacheState, "missing-current", StringComparison.Ordinal))
            {
                File.Delete(currentPath);
            }
            else if (string.Equals(currentCacheState, "corrupt-current", StringComparison.Ordinal))
            {
                await File.WriteAllTextAsync(currentPath, "{");
            }
            else
            {
                await File.WriteAllTextAsync(currentPath, staleCurrentJson);
            }

            RecordingHttpMessageHandler handler = new();
            HookCommandService service = CreateHookCommandService(handler, stateStore);
            _ = await service.HandleStopAsync(
                CreateJsonStream(
                    CreateStopInput(tempDirectory.FullName, stopTimestamp),
                    AppJsonSerializerContext.Default.StopHookInput),
                new MemoryStream(),
                CancellationToken.None);

            Assert.Empty(handler.Requests);
            Assert.False(File.Exists(AppPaths.GetStopObservationPath(
                tempDirectory.FullName,
                "session-123",
                currentTurn.NotificationTurnId,
                CreateStopNotificationKeyForTest(stopTimestamp))));
            Assert.False(File.Exists(AppPaths.GetNotificationRecordPath(
                tempDirectory.FullName,
                "session-123",
                currentTurn.NotificationTurnId,
                CreateStopNotificationKeyForTest(stopTimestamp))));
        }
        finally
        {
            tempDirectory.Delete(recursive: true);
        }
    }

    [Theory]
    [InlineData("missing-current")]
    [InlineData("corrupt-current")]
    [InlineData("stale-current")]
    public async Task HandleStopAsyncCacheRecoveryLatestPendingPreservesOlderCompletedTurn(
        string currentCacheState)
    {
        DirectoryInfo tempDirectory = Directory.CreateTempSubdirectory();
        using EnvironmentScope environment = SetTelegramEnvironment();

        try
        {
            WorkspaceStateStore stateStore = new(
                TimeProvider.System,
                NullLogger<WorkspaceStateStore>.Instance);
            const string stopTimestamp = "2026-03-14T15:52:50.783Z";
            NotificationTurn oldTurn = await CreateTurnAsync(
                stateStore,
                tempDirectory.FullName,
                "session-123",
                "2026-03-14T15:51:40.783Z");
            string staleCurrentJson = await File.ReadAllTextAsync(AppPaths.GetCurrentStatePath(
                tempDirectory.FullName,
                "session-123"));
            await WriteSummaryAsync(
                tempDirectory.FullName,
                "session-123",
                oldTurn,
                new NotificationSummary
                {
                    SessionId = "session-123",
                    NotificationTurnId = oldTurn.NotificationTurnId,
                    NotificationNonce = oldTurn.NotificationNonce,
                    UpdatedAt = "2026-03-14T15:51:45.783Z",
                    Summary = "The older completed turn must survive pending cache recovery.",
                });
            NotificationTurn currentTurn = await CreateTurnAsync(
                stateStore,
                tempDirectory.FullName,
                "session-123",
                "2026-03-14T15:52:40.783Z");
            oldTurn.Status = "open";
            await WriteTurnStateAsync(tempDirectory.FullName, oldTurn);
            await WriteSummaryAsync(
                tempDirectory.FullName,
                "session-123",
                currentTurn,
                new NotificationSummary
                {
                    SessionId = "session-123",
                    NotificationTurnId = currentTurn.NotificationTurnId,
                    NotificationNonce = currentTurn.NotificationNonce,
                    UpdatedAt = stopTimestamp,
                    Status = "pending",
                    Summary = null,
                });
            string currentPath = AppPaths.GetCurrentStatePath(
                tempDirectory.FullName,
                "session-123");
            if (string.Equals(currentCacheState, "missing-current", StringComparison.Ordinal))
            {
                File.Delete(currentPath);
            }
            else if (string.Equals(currentCacheState, "corrupt-current", StringComparison.Ordinal))
            {
                await File.WriteAllTextAsync(currentPath, "{");
            }
            else
            {
                await File.WriteAllTextAsync(currentPath, staleCurrentJson);
            }

            RecordingHttpMessageHandler handler = new();
            HookCommandService service = CreateHookCommandService(handler, stateStore);
            _ = await service.HandleStopAsync(
                CreateJsonStream(
                    CreateStopInput(tempDirectory.FullName, stopTimestamp),
                    AppJsonSerializerContext.Default.StopHookInput),
                new MemoryStream(),
                CancellationToken.None);

            Assert.Empty(handler.Requests);
            NotificationTurn? storedOldTurn = await stateStore.TryReadTurnAsync(
                tempDirectory.FullName,
                "session-123",
                oldTurn.NotificationTurnId,
                CancellationToken.None);
            Assert.Equal("open", storedOldTurn?.Status);
        }
        finally
        {
            tempDirectory.Delete(recursive: true);
        }
    }

    [Fact]
    public async Task HandleStopAsyncOlderExactPendingAbandonedSummarySuppressesLaterCurrentAttribution()
    {
        DirectoryInfo tempDirectory = Directory.CreateTempSubdirectory();
        using EnvironmentScope environment = SetTelegramEnvironment();

        try
        {
            WorkspaceStateStore stateStore = new(
                TimeProvider.System,
                NullLogger<WorkspaceStateStore>.Instance);
            const string stopTimestamp = "2026-03-14T15:52:50.783Z";
            NotificationTurn oldTurn = await CreateTurnAsync(
                stateStore,
                tempDirectory.FullName,
                "session-123",
                "2026-03-14T15:51:40.783Z");
            await WriteSummaryAsync(
                tempDirectory.FullName,
                "session-123",
                oldTurn,
                new NotificationSummary
                {
                    SessionId = "session-123",
                    NotificationTurnId = oldTurn.NotificationTurnId,
                    NotificationNonce = oldTurn.NotificationNonce,
                    UpdatedAt = stopTimestamp,
                    Status = "pending",
                    Summary = null,
                });
            oldTurn.Status = "abandoned";
            await WriteTurnStateAsync(tempDirectory.FullName, oldTurn);
            await WorkspaceStateStore.RecordStopObservationAsync(
                tempDirectory.FullName,
                oldTurn,
                new StopObservation
                {
                    SessionId = "session-123",
                    NotificationTurnId = oldTurn.NotificationTurnId,
                    StopId = CreateStopNotificationKeyForTest(stopTimestamp),
                    ObservedAt = stopTimestamp,
                    StopTimestamp = stopTimestamp,
                    MatchReason = "older pending handoff",
                    SummaryValid = false,
                    SummaryPendingHandoff = true,
                    SummaryFailureReason = "summary must be a non-empty human-readable sentence",
                },
                CancellationToken.None);
            NotificationTurn currentTurn = await CreateTurnAsync(
                stateStore,
                tempDirectory.FullName,
                "session-123",
                "2026-03-14T15:52:40.783Z");
            await WriteSummaryAsync(
                tempDirectory.FullName,
                "session-123",
                currentTurn,
                new NotificationSummary
                {
                    SessionId = "session-123",
                    NotificationTurnId = currentTurn.NotificationTurnId,
                    NotificationNonce = currentTurn.NotificationNonce,
                    UpdatedAt = stopTimestamp,
                    Summary = "The current exact summary must wait behind older pending replay suppression.",
                });

            RecordingHttpMessageHandler handler = new();
            HookCommandService service = CreateHookCommandService(handler, stateStore);
            _ = await service.HandleStopAsync(
                CreateJsonStream(
                    CreateStopInput(tempDirectory.FullName, stopTimestamp),
                    AppJsonSerializerContext.Default.StopHookInput),
                new MemoryStream(),
                CancellationToken.None);

            Assert.Empty(handler.Requests);
            Assert.False(File.Exists(AppPaths.GetSessionNotificationRecordPath(
                tempDirectory.FullName,
                "session-123",
                CreateStopNotificationKeyForTest(stopTimestamp))));
            Assert.False(File.Exists(AppPaths.GetNotificationRecordPath(
                tempDirectory.FullName,
                "session-123",
                currentTurn.NotificationTurnId,
                CreateStopNotificationKeyForTest(stopTimestamp))));
        }
        finally
        {
            tempDirectory.Delete(recursive: true);
        }
    }

    [Fact]
    public async Task HandleStopAsyncDelayedOldExactStopDeliversWhileCurrentPending()
    {
        DirectoryInfo tempDirectory = Directory.CreateTempSubdirectory();
        using EnvironmentScope environment = SetTelegramEnvironment();

        try
        {
            WorkspaceStateStore stateStore = new(
                TimeProvider.System,
                NullLogger<WorkspaceStateStore>.Instance);
            NotificationTurn oldTurn = await CreateTurnAsync(
                stateStore,
                tempDirectory.FullName,
                "session-123",
                "2026-03-14T15:51:00.783Z");
            const string oldStopTimestamp = "2026-03-14T15:51:50.783Z";
            await WriteSummaryAsync(
                tempDirectory.FullName,
                "session-123",
                oldTurn,
                new NotificationSummary
                {
                    SessionId = "session-123",
                    NotificationTurnId = oldTurn.NotificationTurnId,
                    NotificationNonce = oldTurn.NotificationNonce,
                    UpdatedAt = oldStopTimestamp,
                    Summary = "The delayed old exact Stop should deliver old summary.",
                });
            NotificationTurn currentTurn = await CreateTurnAsync(
                stateStore,
                tempDirectory.FullName,
                "session-123",
                "2026-03-14T15:51:53.783Z");
            RecordingHttpMessageHandler handler = new();
            HookCommandService service = CreateHookCommandService(handler, stateStore);

            _ = await service.HandleStopAsync(
                CreateJsonStream(
                    CreateStopInput(tempDirectory.FullName, oldStopTimestamp),
                    AppJsonSerializerContext.Default.StopHookInput),
                new MemoryStream(),
                CancellationToken.None);

            TelegramSendMessageRequest payload = DeserializeTelegramPayload(
                Assert.Single(handler.Requests));
            Assert.Contains(
                "摘要：The delayed old exact Stop should deliver old summary.",
                payload.Text,
                StringComparison.Ordinal);
            Assert.Contains(oldTurn.NotificationTurnId, payload.Text, StringComparison.Ordinal);
            Assert.DoesNotContain(currentTurn.NotificationTurnId, payload.Text, StringComparison.Ordinal);
        }
        finally
        {
            tempDirectory.Delete(recursive: true);
        }
    }

    [Fact]
    public async Task HandleStopAsyncDelayedOldExactStopDeliversAfterCurrentHasBeenOpen()
    {
        DirectoryInfo tempDirectory = Directory.CreateTempSubdirectory();
        using EnvironmentScope environment = SetTelegramEnvironment();

        try
        {
            WorkspaceStateStore stateStore = new(
                TimeProvider.System,
                NullLogger<WorkspaceStateStore>.Instance);
            NotificationTurn oldTurn = await CreateTurnAsync(
                stateStore,
                tempDirectory.FullName,
                "session-123",
                "2026-03-14T15:51:00.783Z");
            const string oldStopTimestamp = "2026-03-14T15:51:50.783Z";
            await WriteSummaryAsync(
                tempDirectory.FullName,
                "session-123",
                oldTurn,
                new NotificationSummary
                {
                    SessionId = "session-123",
                    NotificationTurnId = oldTurn.NotificationTurnId,
                    NotificationNonce = oldTurn.NotificationNonce,
                    UpdatedAt = oldStopTimestamp,
                    Summary = "The delayed old exact Stop should deliver even after current has been open.",
                });
            NotificationTurn currentTurn = await CreateTurnAsync(
                stateStore,
                tempDirectory.FullName,
                "session-123",
                "2026-03-14T15:51:40.783Z");
            RecordingHttpMessageHandler handler = new();
            HookCommandService service = CreateHookCommandService(handler, stateStore);

            _ = await service.HandleStopAsync(
                CreateJsonStream(
                    CreateStopInput(tempDirectory.FullName, oldStopTimestamp),
                    AppJsonSerializerContext.Default.StopHookInput),
                new MemoryStream(),
                CancellationToken.None);

            TelegramSendMessageRequest payload = DeserializeTelegramPayload(
                Assert.Single(handler.Requests));
            Assert.Contains(
                "摘要：The delayed old exact Stop should deliver even after current has been open.",
                payload.Text,
                StringComparison.Ordinal);
            Assert.Contains(oldTurn.NotificationTurnId, payload.Text, StringComparison.Ordinal);
            Assert.DoesNotContain(currentTurn.NotificationTurnId, payload.Text, StringComparison.Ordinal);
        }
        finally
        {
            tempDirectory.Delete(recursive: true);
        }
    }

    [Fact]
    public async Task HandleStopAsyncDelayedOldExactStopDeliversDespiteFreshCurrentClaim()
    {
        DirectoryInfo tempDirectory = Directory.CreateTempSubdirectory();
        using EnvironmentScope environment = SetTelegramEnvironment();

        try
        {
            WorkspaceStateStore stateStore = new(
                FixedUtcNow(),
                NullLogger<WorkspaceStateStore>.Instance);
            NotificationTurn oldTurn = await CreateTurnAsync(
                stateStore,
                tempDirectory.FullName,
                "session-123",
                "2026-03-14T15:51:00.783Z");
            const string oldStopTimestamp = "2026-03-14T15:51:50.783Z";
            await WriteSummaryAsync(
                tempDirectory.FullName,
                "session-123",
                oldTurn,
                new NotificationSummary
                {
                    SessionId = "session-123",
                    NotificationTurnId = oldTurn.NotificationTurnId,
                    NotificationNonce = oldTurn.NotificationNonce,
                    UpdatedAt = oldStopTimestamp,
                    Summary = "The delayed old exact Stop should ignore the fresh current claim.",
                });
            NotificationTurn currentTurn = await CreateTurnAsync(
                stateStore,
                tempDirectory.FullName,
                "session-123",
                "2026-03-14T15:51:53.783Z");
            string currentTurnClaimPath = AppPaths.GetTurnDeliveryClaimPath(
                tempDirectory.FullName,
                "session-123",
                currentTurn.NotificationTurnId);
            await WriteClaimAsync(currentTurnClaimPath, string.Empty);
            File.SetLastWriteTimeUtc(
                currentTurnClaimPath,
                new DateTime(2026, 3, 14, 15, 51, 54, 783, DateTimeKind.Utc));
            RecordingHttpMessageHandler handler = new();
            HookCommandService service = CreateHookCommandService(handler, stateStore);

            _ = await service.HandleStopAsync(
                CreateJsonStream(
                    CreateStopInput(tempDirectory.FullName, oldStopTimestamp),
                    AppJsonSerializerContext.Default.StopHookInput),
                new MemoryStream(),
                CancellationToken.None);

            TelegramSendMessageRequest payload = DeserializeTelegramPayload(
                Assert.Single(handler.Requests));
            Assert.Contains(
                "摘要：The delayed old exact Stop should ignore the fresh current claim.",
                payload.Text,
                StringComparison.Ordinal);
            Assert.Contains(oldTurn.NotificationTurnId, payload.Text, StringComparison.Ordinal);
            Assert.DoesNotContain(currentTurn.NotificationTurnId, payload.Text, StringComparison.Ordinal);
            Assert.Equal(string.Empty, await File.ReadAllTextAsync(currentTurnClaimPath));
        }
        finally
        {
            tempDirectory.Delete(recursive: true);
        }
    }

    [Fact]
    public async Task HandleStopAsyncOldExactStopWaitsBehindFreshCurrentNullSummaryClaim()
    {
        DirectoryInfo tempDirectory = Directory.CreateTempSubdirectory();
        using EnvironmentScope environment = SetTelegramEnvironment();

        try
        {
            WorkspaceStateStore stateStore = new(
                FixedUtcNow(),
                NullLogger<WorkspaceStateStore>.Instance);
            NotificationTurn oldTurn = await CreateTurnAsync(
                stateStore,
                tempDirectory.FullName,
                "session-123",
                "2026-03-14T15:51:00.783Z");
            const string oldStopTimestamp = "2026-03-14T15:51:50.783Z";
            await WriteSummaryAsync(
                tempDirectory.FullName,
                "session-123",
                oldTurn,
                new NotificationSummary
                {
                    SessionId = "session-123",
                    NotificationTurnId = oldTurn.NotificationTurnId,
                    NotificationNonce = oldTurn.NotificationNonce,
                    UpdatedAt = oldStopTimestamp,
                    Summary = "The older exact Stop should wait while the current null summary is claimed.",
                });
            NotificationTurn currentTurn = await CreateTurnAsync(
                stateStore,
                tempDirectory.FullName,
                "session-123",
                "2026-03-14T15:51:40.783Z");
            await WriteSummaryAsync(
                tempDirectory.FullName,
                "session-123",
                currentTurn,
                new NotificationSummary
                {
                    SessionId = "session-123",
                    NotificationTurnId = currentTurn.NotificationTurnId,
                    NotificationNonce = currentTurn.NotificationNonce,
                    UpdatedAt = "2026-03-14T15:51:49.783Z",
                    Status = "pending",
                    Summary = null,
                });
            string currentTurnClaimPath = AppPaths.GetTurnDeliveryClaimPath(
                tempDirectory.FullName,
                "session-123",
                currentTurn.NotificationTurnId);
            await WriteClaimAsync(currentTurnClaimPath, string.Empty);
            File.SetLastWriteTimeUtc(
                currentTurnClaimPath,
                new DateTime(2026, 3, 14, 15, 51, 49, 783, DateTimeKind.Utc));
            RecordingHttpMessageHandler handler = new();
            HookCommandService service = CreateHookCommandService(handler, stateStore);

            _ = await service.HandleStopAsync(
                CreateJsonStream(
                    CreateStopInput(tempDirectory.FullName, oldStopTimestamp),
                    AppJsonSerializerContext.Default.StopHookInput),
                new MemoryStream(),
                CancellationToken.None);

            Assert.Empty(handler.Requests);
            Assert.False(File.Exists(AppPaths.GetNotificationRecordPath(
                tempDirectory.FullName,
                "session-123",
                oldTurn.NotificationTurnId,
                CreateStopNotificationKeyForTest(oldStopTimestamp))));
            Assert.True(File.Exists(currentTurnClaimPath));
        }
        finally
        {
            tempDirectory.Delete(recursive: true);
        }
    }

    [Theory]
    [InlineData("valid")]
    [InlineData("invalid")]
    public async Task HandleStopAsyncOldExactStopDeliversWhenCurrentHasNoExactStopEvidence(
        string currentSummaryKind)
    {
        DirectoryInfo tempDirectory = Directory.CreateTempSubdirectory();
        using EnvironmentScope environment = SetTelegramEnvironment();

        try
        {
            WorkspaceStateStore stateStore = new(
                TimeProvider.System,
                NullLogger<WorkspaceStateStore>.Instance);
            NotificationTurn oldTurn = await CreateTurnAsync(
                stateStore,
                tempDirectory.FullName,
                "session-123",
                "2026-03-14T15:51:00.783Z");
            const string oldStopTimestamp = "2026-03-14T15:51:50.783Z";
            await WriteSummaryAsync(
                tempDirectory.FullName,
                "session-123",
                oldTurn,
                new NotificationSummary
                {
                    SessionId = "session-123",
                    NotificationTurnId = oldTurn.NotificationTurnId,
                    NotificationNonce = oldTurn.NotificationNonce,
                    UpdatedAt = oldStopTimestamp,
                    Summary = "The old exact Stop should not bind to the current turn.",
                });
            NotificationTurn currentTurn = await CreateTurnAsync(
                stateStore,
                tempDirectory.FullName,
                "session-123",
                "2026-03-14T15:51:48.783Z");
            if (string.Equals(currentSummaryKind, "valid", StringComparison.Ordinal))
            {
                await WriteSummaryAsync(
                    tempDirectory.FullName,
                    "session-123",
                    currentTurn,
                    new NotificationSummary
                    {
                        SessionId = "session-123",
                        NotificationTurnId = currentTurn.NotificationTurnId,
                        NotificationNonce = currentTurn.NotificationNonce,
                        UpdatedAt = "2026-03-14T15:52:50.783Z",
                        Summary = "The current turn is valid for its own later Stop.",
                    });
            }
            else
            {
                await WriteInvalidSummaryAsync(
                    tempDirectory.FullName,
                    "session-123",
                    currentTurn,
                    "wrong-nonce");
            }

            RecordingHttpMessageHandler handler = new();
            HookCommandService service = CreateHookCommandService(handler, stateStore);

            _ = await service.HandleStopAsync(
                CreateJsonStream(
                    CreateStopInput(tempDirectory.FullName, oldStopTimestamp),
                    AppJsonSerializerContext.Default.StopHookInput),
                new MemoryStream(),
                CancellationToken.None);

            TelegramSendMessageRequest payload = DeserializeTelegramPayload(
                Assert.Single(handler.Requests));
            Assert.Contains(
                "摘要：The old exact Stop should not bind to the current turn.",
                payload.Text,
                StringComparison.Ordinal);
            Assert.Contains(oldTurn.NotificationTurnId, payload.Text, StringComparison.Ordinal);
            Assert.DoesNotContain(currentTurn.NotificationTurnId, payload.Text, StringComparison.Ordinal);
        }
        finally
        {
            tempDirectory.Delete(recursive: true);
        }
    }

    [Fact]
    public async Task HandleStopAsyncOlderExactTimestampSummaryDeliversWhenCurrentValidIsNonExact()
    {
        DirectoryInfo tempDirectory = Directory.CreateTempSubdirectory();
        using EnvironmentScope environment = SetTelegramEnvironment();

        try
        {
            WorkspaceStateStore stateStore = new(
                TimeProvider.System,
                NullLogger<WorkspaceStateStore>.Instance);
            NotificationTurn oldTurn = await CreateTurnAsync(
                stateStore,
                tempDirectory.FullName,
                "session-123",
                "2026-03-14T15:51:40.783Z");
            const string currentStopTimestamp = "2026-03-14T15:52:50.783Z";
            await WriteSummaryAsync(
                tempDirectory.FullName,
                "session-123",
                oldTurn,
                new NotificationSummary
                {
                    SessionId = "session-123",
                    NotificationTurnId = oldTurn.NotificationTurnId,
                    NotificationNonce = oldTurn.NotificationNonce,
                    UpdatedAt = currentStopTimestamp,
                    Summary = "The old exact timestamp summary should deliver while current is non-exact.",
                });
            RecordingHttpMessageHandler handler = new();
            HookCommandService service = CreateHookCommandService(handler, stateStore);

            _ = await service.HandleUserPromptSubmitAsync(
                CreateJsonStream(
                    new UserPromptSubmitHookInput
                    {
                        Cwd = tempDirectory.FullName,
                        SessionId = "session-123",
                        Timestamp = "2026-03-14T15:52:40.783Z",
                        TranscriptPath = "/workspace/transcript.json",
                        Prompt = "Start a current turn with valid summary.",
                    },
                    AppJsonSerializerContext.Default.UserPromptSubmitHookInput),
                new MemoryStream(),
                CancellationToken.None);
            NotificationTurn currentTurn = Assert.Single(
                await stateStore.ListOpenTurnsAsync(
                    tempDirectory.FullName,
                    "session-123",
                    CancellationToken.None),
                turn => !string.Equals(
                    turn.NotificationTurnId,
                    oldTurn.NotificationTurnId,
                    StringComparison.Ordinal));
            await WriteSummaryAsync(
                tempDirectory.FullName,
                "session-123",
                currentTurn,
                new NotificationSummary
                {
                    SessionId = "session-123",
                    NotificationTurnId = currentTurn.NotificationTurnId,
                    NotificationNonce = currentTurn.NotificationNonce,
                    UpdatedAt = "2026-03-14T15:52:49.783Z",
                    Summary = "The current valid summary should not win without exact timestamp.",
                });

            _ = await service.HandleStopAsync(
                CreateJsonStream(
                    CreateStopInput(tempDirectory.FullName, currentStopTimestamp),
                    AppJsonSerializerContext.Default.StopHookInput),
                new MemoryStream(),
                CancellationToken.None);

            TelegramSendMessageRequest payload = DeserializeTelegramPayload(
                Assert.Single(handler.Requests));
            Assert.Contains(
                "摘要：The old exact timestamp summary should deliver while current is non-exact.",
                payload.Text,
                StringComparison.Ordinal);
            Assert.Contains(oldTurn.NotificationTurnId, payload.Text, StringComparison.Ordinal);
            Assert.DoesNotContain(currentTurn.NotificationTurnId, payload.Text, StringComparison.Ordinal);
        }
        finally
        {
            tempDirectory.Delete(recursive: true);
        }
    }

    [Fact]
    public async Task HandleStopAsyncValidOldTurnSurvivesNewPromptAndDelivers()
    {
        DirectoryInfo tempDirectory = Directory.CreateTempSubdirectory();
        using EnvironmentScope environment = SetTelegramEnvironment();

        try
        {
            WorkspaceStateStore stateStore = new(
                TimeProvider.System,
                NullLogger<WorkspaceStateStore>.Instance);
            NotificationTurn oldTurn = await CreateTurnAsync(
                stateStore,
                tempDirectory.FullName,
                "session-123",
                "2026-03-14T15:51:40.783Z");
            const string oldStopTimestamp = "2026-03-14T15:51:50.783Z";
            await WriteSummaryAsync(
                tempDirectory.FullName,
                "session-123",
                oldTurn,
                new NotificationSummary
                {
                    SessionId = "session-123",
                    NotificationTurnId = oldTurn.NotificationTurnId,
                    NotificationNonce = oldTurn.NotificationNonce,
                    UpdatedAt = oldStopTimestamp,
                    Summary = "The old completed turn remains deliverable.",
                });
            RecordingHttpMessageHandler handler = new();
            HookCommandService service = CreateHookCommandService(handler, stateStore);

            _ = await service.HandleUserPromptSubmitAsync(
                CreateJsonStream(
                    new UserPromptSubmitHookInput
                    {
                        Cwd = tempDirectory.FullName,
                        SessionId = "session-123",
                        Timestamp = "2026-03-14T15:51:45.783Z",
                        TranscriptPath = "/workspace/transcript.json",
                        Prompt = "Start the follow-up before the old Stop arrives.",
                    },
                    AppJsonSerializerContext.Default.UserPromptSubmitHookInput),
                new MemoryStream(),
                CancellationToken.None);

            NotificationTurn? stillOpenOldTurn = await stateStore.TryReadTurnAsync(
                tempDirectory.FullName,
                "session-123",
                oldTurn.NotificationTurnId,
                CancellationToken.None);
            Assert.Equal("open", stillOpenOldTurn?.Status);
            Assert.False(File.Exists(AppPaths.GetTurnDeliveryClaimPath(
                tempDirectory.FullName,
                "session-123",
                oldTurn.NotificationTurnId)));

            NotificationTurn newTurn = Assert.Single(
                await stateStore.ListOpenTurnsAsync(
                    tempDirectory.FullName,
                    "session-123",
                    CancellationToken.None),
                turn => !string.Equals(
                    turn.NotificationTurnId,
                    oldTurn.NotificationTurnId,
                    StringComparison.Ordinal));
            Assert.Equal("open", newTurn.Status);

            const string newStopTimestamp = "2026-03-14T15:52:50.783Z";
            await WriteSummaryAsync(
                tempDirectory.FullName,
                "session-123",
                newTurn,
                new NotificationSummary
                {
                    SessionId = "session-123",
                    NotificationTurnId = newTurn.NotificationTurnId,
                    NotificationNonce = newTurn.NotificationNonce,
                    UpdatedAt = newStopTimestamp,
                    Summary = "The newer completed turn remains deliverable too.",
                });

            _ = await service.HandleStopAsync(
                CreateJsonStream(
                    CreateStopInput(tempDirectory.FullName, newStopTimestamp),
                    AppJsonSerializerContext.Default.StopHookInput),
                new MemoryStream(),
                CancellationToken.None);

            TelegramSendMessageRequest payload = DeserializeTelegramPayload(
                Assert.Single(handler.Requests));
            Assert.Contains(
                "摘要：The newer completed turn remains deliverable too.",
                payload.Text,
                StringComparison.Ordinal);
            Assert.Contains(newTurn.NotificationTurnId, payload.Text, StringComparison.Ordinal);

            _ = await service.HandleStopAsync(
                CreateJsonStream(
                    CreateStopInput(tempDirectory.FullName, oldStopTimestamp),
                    AppJsonSerializerContext.Default.StopHookInput),
                new MemoryStream(),
                CancellationToken.None);

            TelegramSendMessageRequest secondPayload =
                DeserializeTelegramPayload(handler.Requests[1]);
            Assert.Contains(
                "摘要：The old completed turn remains deliverable.",
                secondPayload.Text,
                StringComparison.Ordinal);
            Assert.Contains(oldTurn.NotificationTurnId, secondPayload.Text, StringComparison.Ordinal);
        }
        finally
        {
            tempDirectory.Delete(recursive: true);
        }
    }

    [Fact]
    public async Task HandleStopAsyncCurrentValidPreferredWhenOldAndNewBothValid()
    {
        DirectoryInfo tempDirectory = Directory.CreateTempSubdirectory();
        using EnvironmentScope environment = SetTelegramEnvironment();

        try
        {
            WorkspaceStateStore stateStore = new(
                TimeProvider.System,
                NullLogger<WorkspaceStateStore>.Instance);
            NotificationTurn oldTurn = await CreateTurnAsync(
                stateStore,
                tempDirectory.FullName,
                "session-123",
                "2026-03-14T15:51:40.783Z");
            const string oldStopTimestamp = "2026-03-14T15:51:50.783Z";
            await WriteSummaryAsync(
                tempDirectory.FullName,
                "session-123",
                oldTurn,
                new NotificationSummary
                {
                    SessionId = "session-123",
                    NotificationTurnId = oldTurn.NotificationTurnId,
                    NotificationNonce = oldTurn.NotificationNonce,
                    UpdatedAt = oldStopTimestamp,
                    Summary = "The old valid summary remains for its own Stop.",
                });
            RecordingHttpMessageHandler handler = new();
            HookCommandService service = CreateHookCommandService(handler, stateStore);

            _ = await service.HandleUserPromptSubmitAsync(
                CreateJsonStream(
                    new UserPromptSubmitHookInput
                    {
                        Cwd = tempDirectory.FullName,
                        SessionId = "session-123",
                        Timestamp = "2026-03-14T15:52:40.783Z",
                        TranscriptPath = "/workspace/transcript.json",
                        Prompt = "Start a current turn with valid summary.",
                    },
                    AppJsonSerializerContext.Default.UserPromptSubmitHookInput),
                new MemoryStream(),
                CancellationToken.None);
            NotificationTurn currentTurn = Assert.Single(
                await stateStore.ListOpenTurnsAsync(
                    tempDirectory.FullName,
                    "session-123",
                    CancellationToken.None),
                turn => !string.Equals(
                    turn.NotificationTurnId,
                    oldTurn.NotificationTurnId,
                    StringComparison.Ordinal));
            const string currentStopTimestamp = "2026-03-14T15:52:50.783Z";
            await WriteSummaryAsync(
                tempDirectory.FullName,
                "session-123",
                currentTurn,
                new NotificationSummary
                {
                    SessionId = "session-123",
                    NotificationTurnId = currentTurn.NotificationTurnId,
                    NotificationNonce = currentTurn.NotificationNonce,
                    UpdatedAt = currentStopTimestamp,
                    Summary = "The current valid summary should be preferred.",
                });

            _ = await service.HandleStopAsync(
                CreateJsonStream(
                    CreateStopInput(tempDirectory.FullName, currentStopTimestamp),
                    AppJsonSerializerContext.Default.StopHookInput),
                new MemoryStream(),
                CancellationToken.None);

            TelegramSendMessageRequest payload = DeserializeTelegramPayload(
                Assert.Single(handler.Requests));
            Assert.Contains(
                "摘要：The current valid summary should be preferred.",
                payload.Text,
                StringComparison.Ordinal);
            Assert.Contains(currentTurn.NotificationTurnId, payload.Text, StringComparison.Ordinal);

            NotificationTurn? storedOldTurn = await stateStore.TryReadTurnAsync(
                tempDirectory.FullName,
                "session-123",
                oldTurn.NotificationTurnId,
                CancellationToken.None);
            Assert.Equal("open", storedOldTurn?.Status);

            _ = await service.HandleStopAsync(
                CreateJsonStream(
                    CreateStopInput(tempDirectory.FullName, oldStopTimestamp),
                    AppJsonSerializerContext.Default.StopHookInput),
                new MemoryStream(),
                CancellationToken.None);

            TelegramSendMessageRequest oldPayload = DeserializeTelegramPayload(
                Assert.Single(handler.Requests.Skip(1)));
            Assert.Contains(
                "摘要：The old valid summary remains for its own Stop.",
                oldPayload.Text,
                StringComparison.Ordinal);
            Assert.Contains(oldTurn.NotificationTurnId, oldPayload.Text, StringComparison.Ordinal);
        }
        finally
        {
            tempDirectory.Delete(recursive: true);
        }
    }

    [Fact]
    public async Task HandleUserPromptSubmitAsyncAbandonsOldTurnWithPendingSummary()
    {
        foreach (string summaryState in new[]
        {
            "missing",
            "invalid-json",
            "null-json",
            "blank-assigned",
            "null-assigned",
            "locked",
        })
        {
            DirectoryInfo tempDirectory = Directory.CreateTempSubdirectory();
            using EnvironmentScope environment = SetTelegramEnvironment();
            FileStream? lockedSummary = null;

            try
            {
                WorkspaceStateStore stateStore = new(
                    TimeProvider.System,
                    NullLogger<WorkspaceStateStore>.Instance);
                NotificationTurn oldTurn = await CreateTurnAsync(
                    stateStore,
                    tempDirectory.FullName,
                    "session-123",
                    "2026-03-14T15:51:40.783Z");
                string summaryPath = AppPaths.GetSummaryStatePath(
                    tempDirectory.FullName,
                    "session-123",
                    oldTurn.NotificationTurnId);
                switch (summaryState)
                {
                    case "missing":
                        File.Delete(summaryPath);
                        break;
                    case "invalid-json":
                        await File.WriteAllTextAsync(summaryPath, "{");
                        break;
                    case "null-json":
                        await File.WriteAllTextAsync(summaryPath, "null");
                        break;
                    case "blank-assigned":
                    case "null-assigned":
                        await WriteSummaryAsync(
                            tempDirectory.FullName,
                            "session-123",
                            oldTurn,
                            new NotificationSummary
                            {
                                SessionId = "session-123",
                                NotificationTurnId = oldTurn.NotificationTurnId,
                                NotificationNonce = oldTurn.NotificationNonce,
                                UpdatedAt = "2026-03-14T15:51:55.783Z",
                                Status = "pending",
                                Summary = string.Equals(
                                    summaryState,
                                    "blank-assigned",
                                    StringComparison.Ordinal)
                                    ? " "
                                    : null,
                            });
                        break;
                    case "locked":
                        lockedSummary = File.Open(
                            summaryPath,
                            FileMode.Open,
                            FileAccess.ReadWrite,
                            FileShare.None);
                        break;
                }

                HookCommandService service = CreateHookCommandService(
                    new RecordingHttpMessageHandler(),
                    stateStore);

                _ = await service.HandleUserPromptSubmitAsync(
                    CreateJsonStream(
                        new UserPromptSubmitHookInput
                        {
                            Cwd = tempDirectory.FullName,
                            SessionId = "session-123",
                            Timestamp = "2026-03-14T15:52:40.783Z",
                            TranscriptPath = "/workspace/transcript.json",
                            Prompt = $"Supersede a turn with {summaryState} summary.",
                        },
                        AppJsonSerializerContext.Default.UserPromptSubmitHookInput),
                    new MemoryStream(),
                    CancellationToken.None);

                NotificationTurn? storedOldTurn = await stateStore.TryReadTurnAsync(
                    tempDirectory.FullName,
                    "session-123",
                    oldTurn.NotificationTurnId,
                    CancellationToken.None);
                bool preservesExactAssignedPending = summaryState is "blank-assigned" or "null-assigned";
                Assert.Equal(
                    preservesExactAssignedPending ? "open" : "abandoned",
                    storedOldTurn?.Status);
                IReadOnlyList<NotificationTurn> openTurns = await stateStore.ListOpenTurnsAsync(
                    tempDirectory.FullName,
                    "session-123",
                    CancellationToken.None);
                NotificationTurn newTurn = preservesExactAssignedPending
                    ? Assert.Single(
                        openTurns,
                        turn => !string.Equals(
                            turn.NotificationTurnId,
                            oldTurn.NotificationTurnId,
                            StringComparison.Ordinal))
                    : Assert.Single(openTurns);
                Assert.NotEqual(oldTurn.NotificationTurnId, newTurn.NotificationTurnId);
            }
            finally
            {
                lockedSummary?.Dispose();
                tempDirectory.Delete(recursive: true);
            }
        }
    }

    [Theory]
    [InlineData("completed", " ")]
    [InlineData("completed", null)]
    [InlineData(null, " ")]
    [InlineData(null, null)]
    public async Task HandleUserPromptSubmitAsyncAbandonsOldTurnWithNonPendingBlankOrNullAssignedSummary(
        string? summaryStatus,
        string? summaryText)
    {
        DirectoryInfo tempDirectory = Directory.CreateTempSubdirectory();
        using EnvironmentScope environment = SetTelegramEnvironment();

        try
        {
            WorkspaceStateStore stateStore = new(
                TimeProvider.System,
                NullLogger<WorkspaceStateStore>.Instance);
            NotificationTurn oldTurn = await CreateTurnAsync(
                stateStore,
                tempDirectory.FullName,
                "session-123",
                "2026-03-14T15:51:40.783Z");
            await WriteSummaryAsync(
                tempDirectory.FullName,
                "session-123",
                oldTurn,
                new NotificationSummary
                {
                    SessionId = "session-123",
                    NotificationTurnId = oldTurn.NotificationTurnId,
                    NotificationNonce = oldTurn.NotificationNonce,
                    UpdatedAt = "2026-03-14T15:51:55.783Z",
                    Status = summaryStatus,
                    Summary = summaryText,
                });
            HookCommandService service = CreateHookCommandService(
                new RecordingHttpMessageHandler(),
                stateStore);

            _ = await service.HandleUserPromptSubmitAsync(
                CreateJsonStream(
                    new UserPromptSubmitHookInput
                    {
                        Cwd = tempDirectory.FullName,
                        SessionId = "session-123",
                        Timestamp = "2026-03-14T15:52:40.783Z",
                        TranscriptPath = "/workspace/transcript.json",
                        Prompt = "Supersede a turn with a non-pending blank summary.",
                    },
                    AppJsonSerializerContext.Default.UserPromptSubmitHookInput),
                new MemoryStream(),
                CancellationToken.None);

            NotificationTurn? storedOldTurn = await stateStore.TryReadTurnAsync(
                tempDirectory.FullName,
                "session-123",
                oldTurn.NotificationTurnId,
                CancellationToken.None);
            Assert.Equal("abandoned", storedOldTurn?.Status);
            Assert.Single(await stateStore.ListOpenTurnsAsync(
                tempDirectory.FullName,
                "session-123",
                CancellationToken.None));
        }
        finally
        {
            tempDirectory.Delete(recursive: true);
        }
    }

    [Fact]
    public async Task HandleStopAsyncAbandonsSupersededPendingTurnAfterFreshClaimClears()
    {
        DirectoryInfo tempDirectory = Directory.CreateTempSubdirectory();
        using EnvironmentScope environment = SetTelegramEnvironment();

        try
        {
            WorkspaceStateStore stateStore = new(
                FixedUtcNow(),
                NullLogger<WorkspaceStateStore>.Instance);
            NotificationTurn oldTurn = await CreateTurnAsync(
                stateStore,
                tempDirectory.FullName,
                "session-123",
                "2026-03-14T15:51:40.783Z");
            string turnClaimPath = AppPaths.GetTurnDeliveryClaimPath(
                tempDirectory.FullName,
                "session-123",
                oldTurn.NotificationTurnId);
            Assert.True(await WorkspaceStateStore.TryClaimStopNotificationAsync(
                turnClaimPath,
                "2026-03-14T15:51:50.783Z",
                CancellationToken.None));

            HookCommandService service = CreateHookCommandService(
                new RecordingHttpMessageHandler(),
                stateStore);
            _ = await service.HandleUserPromptSubmitAsync(
                CreateJsonStream(
                    new UserPromptSubmitHookInput
                    {
                        Cwd = tempDirectory.FullName,
                        SessionId = "session-123",
                        Timestamp = "2026-03-14T15:52:40.783Z",
                        TranscriptPath = "/workspace/transcript.json",
                        Prompt = "Ship the follow-up after old claim clears.",
                    },
                    AppJsonSerializerContext.Default.UserPromptSubmitHookInput),
                new MemoryStream(),
                CancellationToken.None);

            NotificationTurn newTurn = Assert.Single(await stateStore.ListOpenTurnsAsync(
                tempDirectory.FullName,
                "session-123",
                CancellationToken.None));
            WorkspaceStateStore.ReleaseStopNotificationClaim(turnClaimPath);
            const string newStopTimestamp = "2026-03-14T15:52:50.783Z";
            await WriteSummaryAsync(
                tempDirectory.FullName,
                "session-123",
                newTurn,
                new NotificationSummary
                {
                    SessionId = "session-123",
                    NotificationTurnId = newTurn.NotificationTurnId,
                    NotificationNonce = newTurn.NotificationNonce,
                    UpdatedAt = newStopTimestamp,
                    Summary = "The new turn delivered after the old claim cleared.",
                });
            RecordingHttpMessageHandler handler = new();
            service = CreateHookCommandService(handler, stateStore);

            _ = await service.HandleStopAsync(
                CreateJsonStream(
                    CreateStopInput(tempDirectory.FullName, newStopTimestamp),
                    AppJsonSerializerContext.Default.StopHookInput),
                new MemoryStream(),
                CancellationToken.None);

            TelegramSendMessageRequest payload = DeserializeTelegramPayload(
                Assert.Single(handler.Requests));
            Assert.Contains(
                "摘要：The new turn delivered after the old claim cleared.",
                payload.Text,
                StringComparison.Ordinal);
            NotificationTurn? abandonedOldTurn = await stateStore.TryReadTurnAsync(
                tempDirectory.FullName,
                "session-123",
                oldTurn.NotificationTurnId,
                CancellationToken.None);
            Assert.Equal("abandoned", abandonedOldTurn?.Status);
        }
        finally
        {
            tempDirectory.Delete(recursive: true);
        }
    }

    [Fact]
    public async Task HandleStopAsyncAbandonsSupersededPendingTurnAfterFreshClaimStales()
    {
        DirectoryInfo tempDirectory = Directory.CreateTempSubdirectory();
        using EnvironmentScope environment = SetTelegramEnvironment();

        try
        {
            WorkspaceStateStore stateStore = new(
                FixedUtcNow(),
                NullLogger<WorkspaceStateStore>.Instance);
            NotificationTurn oldTurn = await CreateTurnAsync(
                stateStore,
                tempDirectory.FullName,
                "session-123",
                "2026-03-14T15:51:40.783Z");
            string turnClaimPath = AppPaths.GetTurnDeliveryClaimPath(
                tempDirectory.FullName,
                "session-123",
                oldTurn.NotificationTurnId);
            Assert.True(await WorkspaceStateStore.TryClaimStopNotificationAsync(
                turnClaimPath,
                "2026-03-14T15:51:50.783Z",
                CancellationToken.None));

            HookCommandService service = CreateHookCommandService(
                new RecordingHttpMessageHandler(),
                stateStore);
            _ = await service.HandleUserPromptSubmitAsync(
                CreateJsonStream(
                    new UserPromptSubmitHookInput
                    {
                        Cwd = tempDirectory.FullName,
                        SessionId = "session-123",
                        Timestamp = "2026-03-14T15:52:40.783Z",
                        TranscriptPath = "/workspace/transcript.json",
                        Prompt = "Ship the follow-up after old claim stales.",
                    },
                    AppJsonSerializerContext.Default.UserPromptSubmitHookInput),
                new MemoryStream(),
                CancellationToken.None);

            NotificationTurn newTurn = Assert.Single(await stateStore.ListOpenTurnsAsync(
                tempDirectory.FullName,
                "session-123",
                CancellationToken.None));
            await WriteClaimAsync(turnClaimPath, "2026-03-14T15:40:49.783Z");
            File.SetLastWriteTimeUtc(
                turnClaimPath,
                new DateTime(2026, 3, 14, 15, 40, 49, 783, DateTimeKind.Utc));

            const string newStopTimestamp = "2026-03-14T15:52:50.783Z";
            await WriteSummaryAsync(
                tempDirectory.FullName,
                "session-123",
                newTurn,
                new NotificationSummary
                {
                    SessionId = "session-123",
                    NotificationTurnId = newTurn.NotificationTurnId,
                    NotificationNonce = newTurn.NotificationNonce,
                    UpdatedAt = newStopTimestamp,
                    Summary = "The new turn delivered after the old claim staled.",
                });
            RecordingHttpMessageHandler handler = new();
            service = CreateHookCommandService(handler, stateStore);

            _ = await service.HandleStopAsync(
                CreateJsonStream(
                    CreateStopInput(tempDirectory.FullName, newStopTimestamp),
                    AppJsonSerializerContext.Default.StopHookInput),
                new MemoryStream(),
                CancellationToken.None);

            TelegramSendMessageRequest payload = DeserializeTelegramPayload(
                Assert.Single(handler.Requests));
            Assert.Contains(
                "摘要：The new turn delivered after the old claim staled.",
                payload.Text,
                StringComparison.Ordinal);
            NotificationTurn? abandonedOldTurn = await stateStore.TryReadTurnAsync(
                tempDirectory.FullName,
                "session-123",
                oldTurn.NotificationTurnId,
                CancellationToken.None);
            Assert.Equal("abandoned", abandonedOldTurn?.Status);
        }
        finally
        {
            tempDirectory.Delete(recursive: true);
        }
    }

    [Fact]
    public async Task HandleUserPromptSubmitAsyncAbandonsOldPendingTurnWithStaleDeliveryClaim()
    {
        DirectoryInfo tempDirectory = Directory.CreateTempSubdirectory();
        using EnvironmentScope environment = SetTelegramEnvironment();

        try
        {
            WorkspaceStateStore stateStore = new(
                FixedUtcNow(),
                NullLogger<WorkspaceStateStore>.Instance);
            NotificationTurn oldTurn = await CreateTurnAsync(
                stateStore,
                tempDirectory.FullName,
                "session-123",
                "2026-03-14T15:51:40.783Z");
            string turnClaimPath = AppPaths.GetTurnDeliveryClaimPath(
                tempDirectory.FullName,
                "session-123",
                oldTurn.NotificationTurnId);
            await WriteClaimAsync(turnClaimPath, "2026-03-14T15:40:49.783Z");
            File.SetLastWriteTimeUtc(
                turnClaimPath,
                new DateTime(2026, 3, 14, 15, 40, 49, 783, DateTimeKind.Utc));
            RecordingHttpMessageHandler handler = new();
            HookCommandService service = CreateHookCommandService(handler, stateStore);

            _ = await service.HandleUserPromptSubmitAsync(
                CreateJsonStream(
                    new UserPromptSubmitHookInput
                    {
                        Cwd = tempDirectory.FullName,
                        SessionId = "session-123",
                        Timestamp = "2026-03-14T15:52:40.783Z",
                        TranscriptPath = "/workspace/transcript.json",
                        Prompt = "Ship the follow-up after a stale delivery claim.",
                    },
                    AppJsonSerializerContext.Default.UserPromptSubmitHookInput),
                new MemoryStream(),
                CancellationToken.None);

            NotificationTurn? abandonedOldTurn = await stateStore.TryReadTurnAsync(
                tempDirectory.FullName,
                "session-123",
                oldTurn.NotificationTurnId,
                CancellationToken.None);
            Assert.Equal("abandoned", abandonedOldTurn?.Status);
            Assert.False(File.Exists(turnClaimPath));
            NotificationTurn newTurn = Assert.Single(await stateStore.ListOpenTurnsAsync(
                tempDirectory.FullName,
                "session-123",
                CancellationToken.None));

            const string newStopTimestamp = "2026-03-14T15:52:50.783Z";
            await WriteSummaryAsync(
                tempDirectory.FullName,
                "session-123",
                newTurn,
                new NotificationSummary
                {
                    SessionId = "session-123",
                    NotificationTurnId = newTurn.NotificationTurnId,
                    NotificationNonce = newTurn.NotificationNonce,
                    UpdatedAt = newStopTimestamp,
                    Summary = "The new turn delivered after abandoning the stale claim.",
                });

            _ = await service.HandleStopAsync(
                CreateJsonStream(
                    CreateStopInput(tempDirectory.FullName, newStopTimestamp),
                    AppJsonSerializerContext.Default.StopHookInput),
                new MemoryStream(),
                CancellationToken.None);

            TelegramSendMessageRequest payload = DeserializeTelegramPayload(
                Assert.Single(handler.Requests));
            Assert.Contains(
                "摘要：The new turn delivered after abandoning the stale claim.",
                payload.Text,
                StringComparison.Ordinal);
            Assert.Contains(newTurn.NotificationTurnId, payload.Text, StringComparison.Ordinal);
        }
        finally
        {
            tempDirectory.Delete(recursive: true);
        }
    }

    [Fact]
    public async Task HandleUserPromptSubmitAsyncDoesNotAbandonWhenFreshClaimAppearsAfterAbandonFinalGuard()
    {
        DirectoryInfo tempDirectory = Directory.CreateTempSubdirectory();
        using EnvironmentScope environment = SetTelegramEnvironment();

        try
        {
            WorkspaceStateStore stateStore = new(
                FixedUtcNow(),
                NullLogger<WorkspaceStateStore>.Instance);
            NotificationTurn oldTurn = await CreateTurnAsync(
                stateStore,
                tempDirectory.FullName,
                "session-123",
                "2026-03-14T15:51:40.783Z");
            string oldTurnClaimPath = AppPaths.GetTurnDeliveryClaimPath(
                tempDirectory.FullName,
                "session-123",
                oldTurn.NotificationTurnId);
            bool claimedFreshDelivery = false;
            stateStore.OnAfterAbandonSupersededTurnFinalGuardForTestingAsync =
                async (turn, _, cancellationToken) =>
                {
                    if (!string.Equals(
                            turn.NotificationTurnId,
                            oldTurn.NotificationTurnId,
                            StringComparison.Ordinal)
                        || claimedFreshDelivery)
                    {
                        return;
                    }

                    claimedFreshDelivery = await WorkspaceStateStore.TryClaimStopNotificationAsync(
                        oldTurnClaimPath,
                        "2026-03-14T15:51:50.783Z",
                        cancellationToken);
                    Assert.True(claimedFreshDelivery);
                };
            HookCommandService service = CreateHookCommandService(
                new RecordingHttpMessageHandler(),
                stateStore);

            _ = await service.HandleUserPromptSubmitAsync(
                CreateJsonStream(
                    new UserPromptSubmitHookInput
                    {
                        Cwd = tempDirectory.FullName,
                        SessionId = "session-123",
                        Timestamp = "2026-03-14T15:52:40.783Z",
                        TranscriptPath = "/workspace/transcript.json",
                        Prompt = "Ship the follow-up while a Stop claims the old turn.",
                    },
                    AppJsonSerializerContext.Default.UserPromptSubmitHookInput),
                new MemoryStream(),
                CancellationToken.None);

            NotificationTurn? storedOldTurn = await stateStore.TryReadTurnAsync(
                tempDirectory.FullName,
                "session-123",
                oldTurn.NotificationTurnId,
                CancellationToken.None);
            Assert.True(claimedFreshDelivery);
            Assert.Equal("open", storedOldTurn?.Status);
            Assert.Equal(
                "2026-03-14T15:51:50.783Z",
                await File.ReadAllTextAsync(oldTurnClaimPath));
        }
        finally
        {
            tempDirectory.Delete(recursive: true);
        }
    }

    [Fact]
    public async Task HandleUserPromptSubmitAsyncDoesNotAbandonWhenDurableDeliveryAppearsAfterAbandonFinalGuard()
    {
        DirectoryInfo tempDirectory = Directory.CreateTempSubdirectory();
        using EnvironmentScope environment = SetTelegramEnvironment();

        try
        {
            WorkspaceStateStore stateStore = new(
                FixedUtcNow(),
                NullLogger<WorkspaceStateStore>.Instance);
            NotificationTurn oldTurn = await CreateTurnAsync(
                stateStore,
                tempDirectory.FullName,
                "session-123",
                "2026-03-14T15:51:40.783Z");
            string oldTurnClaimPath = AppPaths.GetTurnDeliveryClaimPath(
                tempDirectory.FullName,
                "session-123",
                oldTurn.NotificationTurnId);
            bool recordedDurableDelivery = false;
            stateStore.OnAfterAbandonSupersededTurnFinalGuardForTestingAsync =
                async (turn, _, _) =>
                {
                    if (!string.Equals(
                            turn.NotificationTurnId,
                            oldTurn.NotificationTurnId,
                            StringComparison.Ordinal)
                        || recordedDurableDelivery)
                    {
                        return;
                    }

                    await RecordSentNotificationAsync(
                        tempDirectory.FullName,
                        oldTurn,
                        CreateStopNotificationKeyForTest("2026-03-14T15:51:50.783Z"),
                        "2026-03-14T15:51:50.783Z");
                    recordedDurableDelivery = true;
                };
            HookCommandService service = CreateHookCommandService(
                new RecordingHttpMessageHandler(),
                stateStore);

            _ = await service.HandleUserPromptSubmitAsync(
                CreateJsonStream(
                    new UserPromptSubmitHookInput
                    {
                        Cwd = tempDirectory.FullName,
                        SessionId = "session-123",
                        Timestamp = "2026-03-14T15:52:40.783Z",
                        TranscriptPath = "/workspace/transcript.json",
                        Prompt = "Ship the follow-up after the old turn was delivered.",
                    },
                    AppJsonSerializerContext.Default.UserPromptSubmitHookInput),
                new MemoryStream(),
                CancellationToken.None);

            NotificationTurn? storedOldTurn = await stateStore.TryReadTurnAsync(
                tempDirectory.FullName,
                "session-123",
                oldTurn.NotificationTurnId,
                CancellationToken.None);
            Assert.True(recordedDurableDelivery);
            Assert.Equal("open", storedOldTurn?.Status);
            Assert.False(File.Exists(oldTurnClaimPath));
            Assert.True(await WorkspaceStateStore.HasDurableDeliveryRecordAsync(
                tempDirectory.FullName,
                "session-123",
                oldTurn.NotificationTurnId,
                CancellationToken.None));
        }
        finally
        {
            tempDirectory.Delete(recursive: true);
        }
    }

    [Fact]
    public async Task HandleUserPromptSubmitAsyncDoesNotAbandonTurnWithDeliveryClaim()
    {
        DirectoryInfo tempDirectory = Directory.CreateTempSubdirectory();
        using EnvironmentScope environment = SetTelegramEnvironment();

        try
        {
            WorkspaceStateStore stateStore = new(
                FixedUtcNow(),
                NullLogger<WorkspaceStateStore>.Instance);
            NotificationTurn oldTurn = await CreateTurnAsync(
                stateStore,
                tempDirectory.FullName,
                "session-123",
                "2026-03-14T15:51:40.783Z");
            string turnClaimPath = AppPaths.GetTurnDeliveryClaimPath(
                tempDirectory.FullName,
                "session-123",
                oldTurn.NotificationTurnId);
            Assert.True(await WorkspaceStateStore.TryClaimStopNotificationAsync(
                turnClaimPath,
                "2026-03-14T15:51:50.783Z",
                CancellationToken.None));

            NotificationTurn newTurn = await CreateTurnAsync(
                stateStore,
                tempDirectory.FullName,
                "session-123",
                "2026-03-14T15:52:40.783Z");

            NotificationTurn? storedOldTurn = await stateStore.TryReadTurnAsync(
                tempDirectory.FullName,
                "session-123",
                oldTurn.NotificationTurnId,
                CancellationToken.None);
            Assert.Equal("open", storedOldTurn?.Status);

            Assert.Equal(newTurn.NotificationTurnId, Assert.Single(
                await stateStore.ListOpenTurnsAsync(
                    tempDirectory.FullName,
                    "session-123",
                    CancellationToken.None)).NotificationTurnId);

            const string newStopTimestamp = "2026-03-14T15:52:50.783Z";
            await WriteSummaryAsync(
                tempDirectory.FullName,
                "session-123",
                newTurn,
                new NotificationSummary
                {
                    SessionId = "session-123",
                    NotificationTurnId = newTurn.NotificationTurnId,
                    NotificationNonce = newTurn.NotificationNonce,
                    UpdatedAt = newStopTimestamp,
                    Summary = "The new turn delivered while the old turn claim was fresh.",
                });
            RecordingHttpMessageHandler handler = new();
            HookCommandService service = CreateHookCommandService(handler, stateStore);

            _ = await service.HandleStopAsync(
                CreateJsonStream(
                    CreateStopInput(tempDirectory.FullName, newStopTimestamp),
                    AppJsonSerializerContext.Default.StopHookInput),
                new MemoryStream(),
                CancellationToken.None);

            TelegramSendMessageRequest payload = DeserializeTelegramPayload(
                Assert.Single(handler.Requests));
            Assert.Contains(
                "摘要：The new turn delivered while the old turn claim was fresh.",
                payload.Text,
                StringComparison.Ordinal);
            Assert.Contains(newTurn.NotificationTurnId, payload.Text, StringComparison.Ordinal);
        }
        finally
        {
            tempDirectory.Delete(recursive: true);
        }
    }

    [Fact]
    public async Task HandleStopAsyncOlderFreshClaimDoesNotSuppressNewerExactDurableDelivery()
    {
        DirectoryInfo tempDirectory = Directory.CreateTempSubdirectory();
        using EnvironmentScope environment = SetTelegramEnvironment();

        try
        {
            WorkspaceStateStore stateStore = new(
                FixedUtcNow(),
                NullLogger<WorkspaceStateStore>.Instance);
            RecordingHttpMessageHandler handler = new();
            HookCommandService service = CreateHookCommandService(handler, stateStore);
            const string stopTimestamp = "2026-03-14T15:52:50.783Z";
            NotificationTurn oldTurn = await CreateTurnAsync(
                stateStore,
                tempDirectory.FullName,
                "session-123",
                "2026-03-14T15:51:40.783Z");
            await WriteSummaryAsync(
                tempDirectory.FullName,
                "session-123",
                oldTurn,
                new NotificationSummary
                {
                    SessionId = "session-123",
                    NotificationTurnId = oldTurn.NotificationTurnId,
                    NotificationNonce = oldTurn.NotificationNonce,
                    UpdatedAt = stopTimestamp,
                    Summary = "The older exact summary is already being delivered.",
                });
            string oldTurnClaimPath = AppPaths.GetTurnDeliveryClaimPath(
                tempDirectory.FullName,
                "session-123",
                oldTurn.NotificationTurnId);
            await WriteClaimAsync(oldTurnClaimPath, string.Empty);
            File.SetLastWriteTimeUtc(
                oldTurnClaimPath,
                new DateTime(2026, 3, 14, 15, 52, 49, 783, DateTimeKind.Utc));
            NotificationTurn newerTurn = await CreateTurnAsync(
                stateStore,
                tempDirectory.FullName,
                "session-123",
                "2026-03-14T15:52:40.783Z");
            await WriteSummaryAsync(
                tempDirectory.FullName,
                "session-123",
                newerTurn,
                new NotificationSummary
                {
                    SessionId = "session-123",
                    NotificationTurnId = newerTurn.NotificationTurnId,
                    NotificationNonce = newerTurn.NotificationNonce,
                    UpdatedAt = stopTimestamp,
                    Summary = "The newer exact summary must not wait behind an older fresh claim.",
                });

            _ = await service.HandleStopAsync(
                CreateJsonStream(
                    CreateStopInput(tempDirectory.FullName, stopTimestamp),
                    AppJsonSerializerContext.Default.StopHookInput),
                new MemoryStream(),
                CancellationToken.None);

            TelegramSendMessageRequest payload = DeserializeTelegramPayload(
                Assert.Single(handler.Requests));
            Assert.Contains(
                "摘要：The newer exact summary must not wait behind an older fresh claim.",
                payload.Text,
                StringComparison.Ordinal);
            Assert.Contains(newerTurn.NotificationTurnId, payload.Text, StringComparison.Ordinal);
            Assert.DoesNotContain(oldTurn.NotificationTurnId, payload.Text, StringComparison.Ordinal);
            Assert.Equal(string.Empty, await File.ReadAllTextAsync(oldTurnClaimPath));
        }
        finally
        {
            tempDirectory.Delete(recursive: true);
        }
    }

    [Fact]
    public async Task HandleStopAsyncOlderFreshClaimWithoutCurrentCacheDoesNotSuppressNewerExactDurableDelivery()
    {
        DirectoryInfo tempDirectory = Directory.CreateTempSubdirectory();
        using EnvironmentScope environment = SetTelegramEnvironment();

        try
        {
            WorkspaceStateStore stateStore = new(
                FixedUtcNow(),
                NullLogger<WorkspaceStateStore>.Instance);
            RecordingHttpMessageHandler handler = new();
            HookCommandService service = CreateHookCommandService(handler, stateStore);
            const string stopTimestamp = "2026-03-14T15:52:50.783Z";
            NotificationTurn oldTurn = await CreateTurnAsync(
                stateStore,
                tempDirectory.FullName,
                "session-123",
                "2026-03-14T15:51:40.783Z");
            await WriteSummaryAsync(
                tempDirectory.FullName,
                "session-123",
                oldTurn,
                new NotificationSummary
                {
                    SessionId = "session-123",
                    NotificationTurnId = oldTurn.NotificationTurnId,
                    NotificationNonce = oldTurn.NotificationNonce,
                    UpdatedAt = stopTimestamp,
                    Summary = "The older exact summary is already being delivered.",
                });
            string oldTurnClaimPath = AppPaths.GetTurnDeliveryClaimPath(
                tempDirectory.FullName,
                "session-123",
                oldTurn.NotificationTurnId);
            await WriteClaimAsync(oldTurnClaimPath, string.Empty);
            File.SetLastWriteTimeUtc(
                oldTurnClaimPath,
                new DateTime(2026, 3, 14, 15, 52, 49, 783, DateTimeKind.Utc));
            NotificationTurn newerTurn = await CreateTurnAsync(
                stateStore,
                tempDirectory.FullName,
                "session-123",
                "2026-03-14T15:52:40.783Z");
            await WriteSummaryAsync(
                tempDirectory.FullName,
                "session-123",
                newerTurn,
                new NotificationSummary
                {
                    SessionId = "session-123",
                    NotificationTurnId = newerTurn.NotificationTurnId,
                    NotificationNonce = newerTurn.NotificationNonce,
                    UpdatedAt = stopTimestamp,
                    Summary = "The newer exact summary must not wait behind a cacheless older fresh claim.",
                });
            File.Delete(AppPaths.GetCurrentStatePath(tempDirectory.FullName, "session-123"));

            _ = await service.HandleStopAsync(
                CreateJsonStream(
                    CreateStopInput(tempDirectory.FullName, stopTimestamp),
                    AppJsonSerializerContext.Default.StopHookInput),
                new MemoryStream(),
                CancellationToken.None);

            TelegramSendMessageRequest payload = DeserializeTelegramPayload(
                Assert.Single(handler.Requests));
            Assert.Contains(
                "摘要：The newer exact summary must not wait behind a cacheless older fresh claim.",
                payload.Text,
                StringComparison.Ordinal);
            Assert.Contains(newerTurn.NotificationTurnId, payload.Text, StringComparison.Ordinal);
            Assert.DoesNotContain(oldTurn.NotificationTurnId, payload.Text, StringComparison.Ordinal);
        }
        finally
        {
            tempDirectory.Delete(recursive: true);
        }
    }

    [Theory]
    [InlineData("cached-current")]
    [InlineData("missing-current")]
    public async Task HandleStopAsyncOlderFreshClaimDoesNotSuppressNewerNonExactDurableDelivery(
        string currentCacheState)
    {
        DirectoryInfo tempDirectory = Directory.CreateTempSubdirectory();
        using EnvironmentScope environment = SetTelegramEnvironment();

        try
        {
            WorkspaceStateStore stateStore = new(
                FixedUtcNow(),
                NullLogger<WorkspaceStateStore>.Instance);
            RecordingHttpMessageHandler handler = new();
            HookCommandService service = CreateHookCommandService(handler, stateStore);
            const string stopTimestamp = "2026-03-14T15:52:50.783Z";
            NotificationTurn oldTurn = await CreateTurnAsync(
                stateStore,
                tempDirectory.FullName,
                "session-123",
                "2026-03-14T15:51:40.783Z");
            await WriteSummaryAsync(
                tempDirectory.FullName,
                "session-123",
                oldTurn,
                new NotificationSummary
                {
                    SessionId = "session-123",
                    NotificationTurnId = oldTurn.NotificationTurnId,
                    NotificationNonce = oldTurn.NotificationNonce,
                    UpdatedAt = stopTimestamp,
                    Summary = "The older exact summary already has a fresh delivery claim.",
                });
            string oldTurnClaimPath = AppPaths.GetTurnDeliveryClaimPath(
                tempDirectory.FullName,
                "session-123",
                oldTurn.NotificationTurnId);
            await WriteClaimAsync(oldTurnClaimPath, string.Empty);
            File.SetLastWriteTimeUtc(
                oldTurnClaimPath,
                new DateTime(2026, 3, 14, 15, 52, 49, 783, DateTimeKind.Utc));
            NotificationTurn newerTurn = await CreateTurnAsync(
                stateStore,
                tempDirectory.FullName,
                "session-123",
                "2026-03-14T15:52:40.783Z");
            await WriteSummaryAsync(
                tempDirectory.FullName,
                "session-123",
                newerTurn,
                new NotificationSummary
                {
                    SessionId = "session-123",
                    NotificationTurnId = newerTurn.NotificationTurnId,
                    NotificationNonce = newerTurn.NotificationNonce,
                    UpdatedAt = "2026-03-14T15:52:49.783Z",
                    Summary = "The newer non-exact summary must not wait behind an older fresh claim.",
                });
            if (string.Equals(currentCacheState, "missing-current", StringComparison.Ordinal))
            {
                File.Delete(AppPaths.GetCurrentStatePath(tempDirectory.FullName, "session-123"));
            }

            _ = await service.HandleStopAsync(
                CreateJsonStream(
                    CreateStopInput(tempDirectory.FullName, stopTimestamp),
                    AppJsonSerializerContext.Default.StopHookInput),
                new MemoryStream(),
                CancellationToken.None);

            TelegramSendMessageRequest payload = DeserializeTelegramPayload(
                Assert.Single(handler.Requests));
            Assert.Contains(
                "摘要：The newer non-exact summary must not wait behind an older fresh claim.",
                payload.Text,
                StringComparison.Ordinal);
            Assert.Contains(newerTurn.NotificationTurnId, payload.Text, StringComparison.Ordinal);
            Assert.DoesNotContain(oldTurn.NotificationTurnId, payload.Text, StringComparison.Ordinal);
            Assert.Equal(string.Empty, await File.ReadAllTextAsync(oldTurnClaimPath));
        }
        finally
        {
            tempDirectory.Delete(recursive: true);
        }
    }

    [Fact]
    public async Task HandleStopAsyncStaleCurrentOlderFreshClaimDoesNotSuppressNewerExactDurableDelivery()
    {
        DirectoryInfo tempDirectory = Directory.CreateTempSubdirectory();
        using EnvironmentScope environment = SetTelegramEnvironment();

        try
        {
            WorkspaceStateStore stateStore = new(
                FixedUtcNow(),
                NullLogger<WorkspaceStateStore>.Instance);
            RecordingHttpMessageHandler handler = new();
            HookCommandService service = CreateHookCommandService(handler, stateStore);
            const string stopTimestamp = "2026-03-14T15:52:50.783Z";
            NotificationTurn oldTurn = await CreateTurnAsync(
                stateStore,
                tempDirectory.FullName,
                "session-123",
                "2026-03-14T15:51:40.783Z");
            await WriteSummaryAsync(
                tempDirectory.FullName,
                "session-123",
                oldTurn,
                new NotificationSummary
                {
                    SessionId = "session-123",
                    NotificationTurnId = oldTurn.NotificationTurnId,
                    NotificationNonce = oldTurn.NotificationNonce,
                    UpdatedAt = stopTimestamp,
                    Summary = "The stale current exact summary already has a fresh delivery claim.",
                });
            string oldTurnClaimPath = AppPaths.GetTurnDeliveryClaimPath(
                tempDirectory.FullName,
                "session-123",
                oldTurn.NotificationTurnId);
            await WriteClaimAsync(oldTurnClaimPath, string.Empty);
            File.SetLastWriteTimeUtc(
                oldTurnClaimPath,
                new DateTime(2026, 3, 14, 15, 52, 49, 783, DateTimeKind.Utc));
            NotificationTurn newerTurn = await CreateTurnAsync(
                stateStore,
                tempDirectory.FullName,
                "session-123",
                "2026-03-14T15:52:40.783Z");
            await WriteSummaryAsync(
                tempDirectory.FullName,
                "session-123",
                newerTurn,
                new NotificationSummary
                {
                    SessionId = "session-123",
                    NotificationTurnId = newerTurn.NotificationTurnId,
                    NotificationNonce = newerTurn.NotificationNonce,
                    UpdatedAt = stopTimestamp,
                    Summary = "The newer exact summary must not wait behind a stale current fresh claim.",
                });
            await File.WriteAllTextAsync(
                AppPaths.GetCurrentStatePath(tempDirectory.FullName, "session-123"),
                JsonSerializer.Serialize(
                    new CurrentNotificationState
                    {
                        SessionId = "session-123",
                        NotificationTurnId = oldTurn.NotificationTurnId,
                        NotificationNonce = oldTurn.NotificationNonce,
                        SummaryPath = AppPaths.GetSummaryStatePath(
                            tempDirectory.FullName,
                            "session-123",
                            oldTurn.NotificationTurnId),
                        UpdatedAt = stopTimestamp,
                    },
                    AppJsonSerializerContext.Default.CurrentNotificationState));

            _ = await service.HandleStopAsync(
                CreateJsonStream(
                    CreateStopInput(tempDirectory.FullName, stopTimestamp),
                    AppJsonSerializerContext.Default.StopHookInput),
                new MemoryStream(),
                CancellationToken.None);

            TelegramSendMessageRequest payload = DeserializeTelegramPayload(
                Assert.Single(handler.Requests));
            Assert.Contains(
                "摘要：The newer exact summary must not wait behind a stale current fresh claim.",
                payload.Text,
                StringComparison.Ordinal);
            Assert.Contains(newerTurn.NotificationTurnId, payload.Text, StringComparison.Ordinal);
            Assert.DoesNotContain(oldTurn.NotificationTurnId, payload.Text, StringComparison.Ordinal);
        }
        finally
        {
            tempDirectory.Delete(recursive: true);
        }
    }

    [Fact]
    public async Task HandleStopAsyncDefersTiedLatestExactPendingSummariesWithoutFallback()
    {
        DirectoryInfo tempDirectory = Directory.CreateTempSubdirectory();
        using EnvironmentScope environment = SetTelegramEnvironment();

        try
        {
            WorkspaceStateStore stateStore = new(
                FixedUtcNow(),
                NullLogger<WorkspaceStateStore>.Instance);
            RecordingHttpMessageHandler handler = new();
            HookCommandService service = CreateHookCommandService(handler, stateStore);
            const string stopTimestamp = "2026-03-14T15:52:50.783Z";
            NotificationTurn olderExactTurn = await CreateTurnAsync(
                stateStore,
                tempDirectory.FullName,
                "session-123",
                "2026-03-14T15:51:40.783Z");
            await WriteSummaryAsync(
                tempDirectory.FullName,
                "session-123",
                olderExactTurn,
                new NotificationSummary
                {
                    SessionId = "session-123",
                    NotificationTurnId = olderExactTurn.NotificationTurnId,
                    NotificationNonce = olderExactTurn.NotificationNonce,
                    UpdatedAt = stopTimestamp,
                    Summary = "The older exact summary must not beat tied latest pending turns.",
                });
            NotificationTurn firstPendingTurn = await CreateTurnAsync(
                stateStore,
                tempDirectory.FullName,
                "session-123",
                "2026-03-14T15:52:40.783Z");
            await WriteSummaryAsync(
                tempDirectory.FullName,
                "session-123",
                firstPendingTurn,
                new NotificationSummary
                {
                    SessionId = "session-123",
                    NotificationTurnId = firstPendingTurn.NotificationTurnId,
                    NotificationNonce = firstPendingTurn.NotificationNonce,
                    UpdatedAt = stopTimestamp,
                    Status = "pending",
                    Summary = string.Empty,
                });
            NotificationTurn secondPendingTurn = await CreateTurnAsync(
                stateStore,
                tempDirectory.FullName,
                "session-123",
                "2026-03-14T15:52:40.783Z");
            await WriteSummaryAsync(
                tempDirectory.FullName,
                "session-123",
                secondPendingTurn,
                new NotificationSummary
                {
                    SessionId = "session-123",
                    NotificationTurnId = secondPendingTurn.NotificationTurnId,
                    NotificationNonce = secondPendingTurn.NotificationNonce,
                    UpdatedAt = stopTimestamp,
                    Status = "pending",
                    Summary = string.Empty,
                });
            olderExactTurn.Status = "open";
            await WriteTurnStateAsync(tempDirectory.FullName, olderExactTurn);
            File.Delete(AppPaths.GetCurrentStatePath(tempDirectory.FullName, "session-123"));

            _ = await service.HandleStopAsync(
                CreateJsonStream(
                    CreateStopInput(tempDirectory.FullName, stopTimestamp),
                    AppJsonSerializerContext.Default.StopHookInput),
                new MemoryStream(),
                CancellationToken.None);

            Assert.Empty(handler.Requests);
            string notificationKey = CreateStopNotificationKeyForTest(stopTimestamp);
            Assert.False(File.Exists(AppPaths.GetSessionNotificationRecordPath(
                tempDirectory.FullName,
                "session-123",
                notificationKey)));
            Assert.False(File.Exists(AppPaths.GetStopObservationPath(
                tempDirectory.FullName,
                "session-123",
                firstPendingTurn.NotificationTurnId,
                notificationKey)));
            Assert.False(File.Exists(AppPaths.GetStopObservationPath(
                tempDirectory.FullName,
                "session-123",
                secondPendingTurn.NotificationTurnId,
                notificationKey)));
        }
        finally
        {
            tempDirectory.Delete(recursive: true);
        }
    }

    [Theory]
    [InlineData("missing")]
    [InlineData("invalid-json")]
    [InlineData("json-null")]
    [InlineData("pending")]
    public async Task HandleStopAsyncDefersMixedTiedLatestAbandonedPendingAndInvalidWithoutFallback(
        string pendingSummaryState)
    {
        DirectoryInfo tempDirectory = Directory.CreateTempSubdirectory();
        using EnvironmentScope environment = SetTelegramEnvironment();

        try
        {
            WorkspaceStateStore stateStore = new(
                FixedUtcNow(),
                NullLogger<WorkspaceStateStore>.Instance);
            RecordingHttpMessageHandler handler = new();
            HookCommandService service = CreateHookCommandService(handler, stateStore);
            const string stopTimestamp = "2026-03-14T15:52:50.783Z";
            const string tiedCreatedAt = "2026-03-14T15:52:40.783Z";
            NotificationTurn pendingTurn = await CreateTurnAsync(
                stateStore,
                tempDirectory.FullName,
                "session-123",
                tiedCreatedAt);
            NotificationTurn invalidTurn = await CreateTurnAsync(
                stateStore,
                tempDirectory.FullName,
                "session-123",
                tiedCreatedAt);

            switch (pendingSummaryState)
            {
                case "missing":
                    File.Delete(AppPaths.GetSummaryStatePath(
                        tempDirectory.FullName,
                        "session-123",
                        pendingTurn.NotificationTurnId));
                    break;
                case "invalid-json":
                    await WriteRawSummaryJsonAsync(
                        tempDirectory.FullName,
                        "session-123",
                        pendingTurn,
                        "{");
                    break;
                case "json-null":
                    await WriteRawSummaryJsonAsync(
                        tempDirectory.FullName,
                        "session-123",
                        pendingTurn,
                        "null");
                    break;
                case "pending":
                    await WriteSummaryAsync(
                        tempDirectory.FullName,
                        "session-123",
                        pendingTurn,
                        new NotificationSummary
                        {
                            SessionId = "session-123",
                            NotificationTurnId = pendingTurn.NotificationTurnId,
                            NotificationNonce = pendingTurn.NotificationNonce,
                            UpdatedAt = stopTimestamp,
                            Status = "pending",
                            Summary = string.Empty,
                        });
                    break;
            }

            await WriteInvalidSummaryAsync(
                tempDirectory.FullName,
                "session-123",
                invalidTurn,
                "empty-object");
            pendingTurn.Status = "abandoned";
            invalidTurn.Status = "abandoned";
            await WriteTurnStateAsync(tempDirectory.FullName, pendingTurn);
            await WriteTurnStateAsync(tempDirectory.FullName, invalidTurn);

            _ = await service.HandleStopAsync(
                CreateJsonStream(
                    CreateStopInput(tempDirectory.FullName, stopTimestamp),
                    AppJsonSerializerContext.Default.StopHookInput),
                new MemoryStream(),
                CancellationToken.None);

            Assert.Empty(handler.Requests);
            string notificationKey = CreateStopNotificationKeyForTest(stopTimestamp);
            Assert.False(File.Exists(AppPaths.GetSessionNotificationRecordPath(
                tempDirectory.FullName,
                "session-123",
                notificationKey)));
            foreach (NotificationTurn turn in new[] { pendingTurn, invalidTurn })
            {
                Assert.False(File.Exists(AppPaths.GetNotificationRecordPath(
                    tempDirectory.FullName,
                    "session-123",
                    turn.NotificationTurnId,
                    notificationKey)));
            }

            NotificationTurn? storedPendingTurn = await stateStore.TryReadTurnAsync(
                tempDirectory.FullName,
                "session-123",
                pendingTurn.NotificationTurnId,
                CancellationToken.None);
            Assert.Equal("abandoned", storedPendingTurn?.Status);
        }
        finally
        {
            tempDirectory.Delete(recursive: true);
        }
    }

    [Fact]
    public async Task HandleStopAsyncSuppressesLaterNoOpenFallbackAfterDurablePerTurnDelivery()
    {
        DirectoryInfo tempDirectory = Directory.CreateTempSubdirectory();
        using EnvironmentScope environment = SetTelegramEnvironment();

        try
        {
            WorkspaceStateStore stateStore = new(
                TimeProvider.System,
                NullLogger<WorkspaceStateStore>.Instance);
            NotificationTurn oldTurn = await CreateTurnAsync(
                stateStore,
                tempDirectory.FullName,
                "session-123",
                "2026-03-14T15:51:40.783Z");
            RecordingHttpMessageHandler handler = new();
            HookCommandService service = CreateHookCommandService(handler, stateStore);

            _ = await service.HandleUserPromptSubmitAsync(
                CreateJsonStream(
                    new UserPromptSubmitHookInput
                    {
                        Cwd = tempDirectory.FullName,
                        SessionId = "session-123",
                        Timestamp = "2026-03-14T15:52:40.783Z",
                        TranscriptPath = "/workspace/transcript.json",
                        Prompt = "Ship the only active follow-up.",
                    },
                    AppJsonSerializerContext.Default.UserPromptSubmitHookInput),
                new MemoryStream(),
                CancellationToken.None);
            NotificationTurn? abandonedOldTurn = await stateStore.TryReadTurnAsync(
                tempDirectory.FullName,
                "session-123",
                oldTurn.NotificationTurnId,
                CancellationToken.None);
            Assert.Equal("abandoned", abandonedOldTurn?.Status);
            NotificationTurn newTurn = Assert.Single(await stateStore.ListOpenTurnsAsync(
                tempDirectory.FullName,
                "session-123",
                CancellationToken.None));
            const string deliveredStopTimestamp = "2026-03-14T15:52:50.783Z";
            await WriteSummaryAsync(
                tempDirectory.FullName,
                "session-123",
                newTurn,
                new NotificationSummary
                {
                    SessionId = "session-123",
                    NotificationTurnId = newTurn.NotificationTurnId,
                    NotificationNonce = newTurn.NotificationNonce,
                    UpdatedAt = deliveredStopTimestamp,
                    Summary = "The active follow-up completed.",
                });

            _ = await service.HandleStopAsync(
                CreateJsonStream(
                    CreateStopInput(tempDirectory.FullName, deliveredStopTimestamp),
                    AppJsonSerializerContext.Default.StopHookInput),
                new MemoryStream(),
                CancellationToken.None);
            Assert.Single(handler.Requests);

            _ = await service.HandleStopAsync(
                CreateJsonStream(
                    CreateStopInput(tempDirectory.FullName, "2026-03-14T15:53:50.783Z"),
                    AppJsonSerializerContext.Default.StopHookInput),
                new MemoryStream(),
                CancellationToken.None);

            Assert.Single(handler.Requests);
            Assert.False(File.Exists(AppPaths.GetSessionNotificationRecordPath(
                tempDirectory.FullName,
                "session-123",
                CreateStopNotificationKeyForTest("2026-03-14T15:53:50.783Z"))));
        }
        finally
        {
            tempDirectory.Delete(recursive: true);
        }
    }

    [Fact]
    public async Task HandleStopAsyncSameTimestampAbandonedTurnWithoutObservationNotifiesNewTurn()
    {
        DirectoryInfo tempDirectory = Directory.CreateTempSubdirectory();
        using EnvironmentScope environment = SetTelegramEnvironment();

        try
        {
            WorkspaceStateStore stateStore = new(
                TimeProvider.System,
                NullLogger<WorkspaceStateStore>.Instance);
            NotificationTurn oldTurn = await CreateTurnAsync(
                stateStore,
                tempDirectory.FullName,
                "session-123",
                "2026-03-14T15:51:40.783Z");
            RecordingHttpMessageHandler handler = new();
            HookCommandService service = CreateHookCommandService(handler, stateStore);
            const string collidingTimestamp = "2026-03-14T15:51:50.783Z";

            _ = await service.HandleUserPromptSubmitAsync(
                CreateJsonStream(
                    new UserPromptSubmitHookInput
                    {
                        Cwd = tempDirectory.FullName,
                        SessionId = "session-123",
                        Timestamp = collidingTimestamp,
                        TranscriptPath = "/workspace/transcript.json",
                        Prompt = "Ship a colliding follow-up without old Stop observation.",
                    },
                    AppJsonSerializerContext.Default.UserPromptSubmitHookInput),
                new MemoryStream(),
                CancellationToken.None);

            NotificationTurn? abandonedOldTurn = await stateStore.TryReadTurnAsync(
                tempDirectory.FullName,
                "session-123",
                oldTurn.NotificationTurnId,
                CancellationToken.None);
            Assert.Equal("abandoned", abandonedOldTurn?.Status);
            NotificationTurn newTurn = Assert.Single(await stateStore.ListOpenTurnsAsync(
                tempDirectory.FullName,
                "session-123",
                CancellationToken.None));
            await WriteSummaryAsync(
                tempDirectory.FullName,
                "session-123",
                newTurn,
                new NotificationSummary
                {
                    SessionId = "session-123",
                    NotificationTurnId = newTurn.NotificationTurnId,
                    NotificationNonce = newTurn.NotificationNonce,
                    UpdatedAt = collidingTimestamp,
                    Summary = "This colliding summary should notify without old Stop evidence.",
                });

            _ = await service.HandleStopAsync(
                CreateJsonStream(
                    CreateStopInput(tempDirectory.FullName, collidingTimestamp),
                    AppJsonSerializerContext.Default.StopHookInput),
                new MemoryStream(),
                CancellationToken.None);

            TelegramSendMessageRequest payload = DeserializeTelegramPayload(
                Assert.Single(handler.Requests));
            Assert.Contains(
                "摘要：This colliding summary should notify without old Stop evidence.",
                payload.Text,
                StringComparison.Ordinal);
            Assert.Contains(newTurn.NotificationTurnId, payload.Text, StringComparison.Ordinal);
        }
        finally
        {
            tempDirectory.Delete(recursive: true);
        }
    }

    [Fact]
    public async Task HandleStopAsyncAbandonsSupersededPendingTurnWhenClaimClears()
    {
        DirectoryInfo tempDirectory = Directory.CreateTempSubdirectory();
        using EnvironmentScope environment = SetTelegramEnvironment();

        try
        {
            WorkspaceStateStore stateStore = new(
                FixedUtcNow(),
                NullLogger<WorkspaceStateStore>.Instance);
            NotificationTurn oldTurn = await CreateTurnAsync(
                stateStore,
                tempDirectory.FullName,
                "session-123",
                "2026-03-14T15:51:40.783Z");
            string turnClaimPath = AppPaths.GetTurnDeliveryClaimPath(
                tempDirectory.FullName,
                "session-123",
                oldTurn.NotificationTurnId);
            Assert.True(await WorkspaceStateStore.TryClaimStopNotificationAsync(
                turnClaimPath,
                "2026-03-14T15:51:50.783Z",
                CancellationToken.None));
            HookCommandService service = CreateHookCommandService(
                new RecordingHttpMessageHandler(),
                stateStore);

            _ = await service.HandleUserPromptSubmitAsync(
                CreateJsonStream(
                    new UserPromptSubmitHookInput
                    {
                        Cwd = tempDirectory.FullName,
                        SessionId = "session-123",
                        Timestamp = "2026-03-14T15:52:40.783Z",
                        TranscriptPath = "/workspace/transcript.json",
                        Prompt = "Ship the superseding prompt.",
                    },
                    AppJsonSerializerContext.Default.UserPromptSubmitHookInput),
                new MemoryStream(),
                CancellationToken.None);

            NotificationTurn? stillOpenOldTurn = await stateStore.TryReadTurnAsync(
                tempDirectory.FullName,
                "session-123",
                oldTurn.NotificationTurnId,
                CancellationToken.None);
            Assert.Equal("open", stillOpenOldTurn?.Status);

            WorkspaceStateStore.ReleaseStopNotificationClaim(turnClaimPath);
            await stateStore.MarkTurnAbandonedIfSupersededAsync(
                tempDirectory.FullName,
                oldTurn,
                "2026-03-14T15:51:50.783Z",
                CancellationToken.None);

            NotificationTurn? abandonedOldTurn = await stateStore.TryReadTurnAsync(
                tempDirectory.FullName,
                "session-123",
                oldTurn.NotificationTurnId,
                CancellationToken.None);
            Assert.Equal("abandoned", abandonedOldTurn?.Status);
            Assert.False(File.Exists(turnClaimPath));
        }
        finally
        {
            tempDirectory.Delete(recursive: true);
        }
    }

    [Fact]
    public async Task MarkTurnAbandonedIfSupersededAsyncKeepsTurnOpenWhenSummaryCompletesBeforeAbandonment()
    {
        DirectoryInfo tempDirectory = Directory.CreateTempSubdirectory();

        try
        {
            WorkspaceStateStore stateStore = new(
                FixedUtcNow(),
                NullLogger<WorkspaceStateStore>.Instance);
            NotificationTurn oldTurn = await CreateTurnAsync(
                stateStore,
                tempDirectory.FullName,
                "session-123",
                "2026-03-14T15:51:40.783Z");
            string turnClaimPath = AppPaths.GetTurnDeliveryClaimPath(
                tempDirectory.FullName,
                "session-123",
                oldTurn.NotificationTurnId);
            Assert.True(await WorkspaceStateStore.TryClaimStopNotificationAsync(
                turnClaimPath,
                "2026-03-14T15:51:50.783Z",
                CancellationToken.None));
            _ = await CreateTurnAsync(
                stateStore,
                tempDirectory.FullName,
                "session-123",
                "2026-03-14T15:52:40.783Z");
            await WriteSummaryAsync(
                tempDirectory.FullName,
                "session-123",
                oldTurn,
                new NotificationSummary
                {
                    SessionId = "session-123",
                    NotificationTurnId = oldTurn.NotificationTurnId,
                    NotificationNonce = oldTurn.NotificationNonce,
                    UpdatedAt = "2026-03-14T15:51:50.783Z",
                    Summary = "The older turn completed before deferred abandonment ran.",
                });
            WorkspaceStateStore.ReleaseStopNotificationClaim(turnClaimPath);

            await stateStore.MarkTurnAbandonedIfSupersededAsync(
                tempDirectory.FullName,
                oldTurn,
                "2026-03-14T15:52:50.783Z",
                CancellationToken.None);

            NotificationTurn? storedOldTurn = await stateStore.TryReadTurnAsync(
                tempDirectory.FullName,
                "session-123",
                oldTurn.NotificationTurnId,
                CancellationToken.None);
            Assert.Equal("open", storedOldTurn?.Status);
        }
        finally
        {
            tempDirectory.Delete(recursive: true);
        }
    }

    [Fact]
    public async Task HandleUserPromptSubmitAsyncPreservesExactAssignedPendingSummaryBeforeStopObservation()
    {
        DirectoryInfo tempDirectory = Directory.CreateTempSubdirectory();
        using EnvironmentScope environment = SetTelegramEnvironment();

        try
        {
            WorkspaceStateStore stateStore = new(
                FixedUtcNow(),
                NullLogger<WorkspaceStateStore>.Instance);
            HookCommandService service = CreateHookCommandService(
                new RecordingHttpMessageHandler(),
                stateStore);
            NotificationTurn oldTurn = await CreateTurnAsync(
                stateStore,
                tempDirectory.FullName,
                "session-123",
                "2026-03-14T15:51:40.783Z");
            await WriteSummaryAsync(
                tempDirectory.FullName,
                "session-123",
                oldTurn,
                new NotificationSummary
                {
                    SessionId = "session-123",
                    NotificationTurnId = oldTurn.NotificationTurnId,
                    NotificationNonce = oldTurn.NotificationNonce,
                    UpdatedAt = "2026-03-14T15:51:55.783Z",
                    Status = "pending",
                    Summary = " ",
                });

            _ = await service.HandleUserPromptSubmitAsync(
                CreateJsonStream(
                    new UserPromptSubmitHookInput
                    {
                        Cwd = tempDirectory.FullName,
                        SessionId = "session-123",
                        Timestamp = "2026-03-14T15:52:40.783Z",
                        TranscriptPath = "/workspace/transcript.json",
                        Prompt = "Ship the follow-up change.",
                    },
                    AppJsonSerializerContext.Default.UserPromptSubmitHookInput),
                new MemoryStream(),
                CancellationToken.None);

            NotificationTurn? storedOldTurn = await stateStore.TryReadTurnAsync(
                tempDirectory.FullName,
                "session-123",
                oldTurn.NotificationTurnId,
                CancellationToken.None);
            Assert.Equal("open", storedOldTurn?.Status);
            Assert.False(Directory.Exists(Path.Combine(
                AppPaths.GetTurnDirectoryPath(
                    tempDirectory.FullName,
                    "session-123",
                    oldTurn.NotificationTurnId),
                AppConstants.StopsDirectoryName)));
        }
        finally
        {
            tempDirectory.Delete(recursive: true);
        }
    }

    [Fact]
    public async Task HandleUserPromptSubmitAsyncPreservesExactAssignedPendingSummaryWithStopTimestamp()
    {
        DirectoryInfo tempDirectory = Directory.CreateTempSubdirectory();
        using EnvironmentScope environment = SetTelegramEnvironment();

        try
        {
            WorkspaceStateStore stateStore = new(
                FixedUtcNow(),
                NullLogger<WorkspaceStateStore>.Instance);
            HookCommandService service = CreateHookCommandService(
                new RecordingHttpMessageHandler(),
                stateStore);
            NotificationTurn oldTurn = await CreateTurnAsync(
                stateStore,
                tempDirectory.FullName,
                "session-123",
                "2026-03-14T15:51:40.783Z");
            await WriteSummaryAsync(
                tempDirectory.FullName,
                "session-123",
                oldTurn,
                new NotificationSummary
                {
                    SessionId = "session-123",
                    NotificationTurnId = oldTurn.NotificationTurnId,
                    NotificationNonce = oldTurn.NotificationNonce,
                    UpdatedAt = "2026-03-14T15:51:55.783Z",
                    Status = "pending",
                    Summary = " ",
                });

            _ = await service.HandleUserPromptSubmitAsync(
                CreateJsonStream(
                    new UserPromptSubmitHookInput
                    {
                        Cwd = tempDirectory.FullName,
                        SessionId = "session-123",
                        Timestamp = "2026-03-14T15:52:40.783Z",
                        TranscriptPath = "/workspace/transcript.json",
                        Prompt = "Ship the follow-up change.",
                    },
                    AppJsonSerializerContext.Default.UserPromptSubmitHookInput),
                new MemoryStream(),
                CancellationToken.None);

            NotificationTurn? storedOldTurn = await stateStore.TryReadTurnAsync(
                tempDirectory.FullName,
                "session-123",
                oldTurn.NotificationTurnId,
                CancellationToken.None);
            Assert.Equal("open", storedOldTurn?.Status);
        }
        finally
        {
            tempDirectory.Delete(recursive: true);
        }
    }

    [Fact]
    public async Task HandleUserPromptSubmitAsyncAbandonsLegacyHookCreatedPendingPlaceholderWithTurnUpdatedAtTimestamp()
    {
        DirectoryInfo tempDirectory = Directory.CreateTempSubdirectory();
        using EnvironmentScope environment = SetTelegramEnvironment();

        try
        {
            WorkspaceStateStore stateStore = new(
                FixedUtcNow(),
                NullLogger<WorkspaceStateStore>.Instance);
            HookCommandService service = CreateHookCommandService(
                new RecordingHttpMessageHandler(),
                stateStore);
            NotificationTurn oldTurn = await CreateTurnAsync(
                stateStore,
                tempDirectory.FullName,
                "session-123",
                "2026-03-14T15:51:40.783Z");
            await WriteSummaryAsync(
                tempDirectory.FullName,
                "session-123",
                oldTurn,
                new NotificationSummary
                {
                    SessionId = "session-123",
                    NotificationTurnId = oldTurn.NotificationTurnId,
                    NotificationNonce = oldTurn.NotificationNonce,
                    UpdatedAt = oldTurn.UpdatedAt,
                    Status = "pending",
                    Summary = null,
                });

            _ = await service.HandleUserPromptSubmitAsync(
                CreateJsonStream(
                    new UserPromptSubmitHookInput
                    {
                        Cwd = tempDirectory.FullName,
                        SessionId = "session-123",
                        Timestamp = "2026-03-14T15:52:40.783Z",
                        TranscriptPath = "/workspace/transcript.json",
                        Prompt = "Ship the follow-up change.",
                    },
                    AppJsonSerializerContext.Default.UserPromptSubmitHookInput),
                new MemoryStream(),
                CancellationToken.None);

            NotificationTurn? storedOldTurn = await stateStore.TryReadTurnAsync(
                tempDirectory.FullName,
                "session-123",
                oldTurn.NotificationTurnId,
                CancellationToken.None);
            Assert.Equal("abandoned", storedOldTurn?.Status);
        }
        finally
        {
            tempDirectory.Delete(recursive: true);
        }
    }

    [Fact]
    public async Task HandleUserPromptSubmitAsyncAbandonsLegacyHookCreatedPendingPlaceholderWithTurnCreatedAtTimestamp()
    {
        DirectoryInfo tempDirectory = Directory.CreateTempSubdirectory();
        using EnvironmentScope environment = SetTelegramEnvironment();

        try
        {
            WorkspaceStateStore stateStore = new(
                FixedUtcNow(),
                NullLogger<WorkspaceStateStore>.Instance);
            HookCommandService service = CreateHookCommandService(
                new RecordingHttpMessageHandler(),
                stateStore);
            NotificationTurn oldTurn = await CreateTurnAsync(
                stateStore,
                tempDirectory.FullName,
                "session-123",
                "2026-03-14T15:51:40.783Z");
            oldTurn.SummaryPlaceholderCreatedAt = null;
            oldTurn.UpdatedAt = "2026-03-14T15:51:41.783Z";
            await WriteTurnStateAsync(tempDirectory.FullName, oldTurn);
            await WriteSummaryAsync(
                tempDirectory.FullName,
                "session-123",
                oldTurn,
                new NotificationSummary
                {
                    SessionId = "session-123",
                    NotificationTurnId = oldTurn.NotificationTurnId,
                    NotificationNonce = oldTurn.NotificationNonce,
                    UpdatedAt = oldTurn.CreatedAt,
                    Status = "pending",
                    Summary = null,
                });

            _ = await service.HandleUserPromptSubmitAsync(
                CreateJsonStream(
                    new UserPromptSubmitHookInput
                    {
                        Cwd = tempDirectory.FullName,
                        SessionId = "session-123",
                        Timestamp = "2026-03-14T15:52:40.783Z",
                        TranscriptPath = "/workspace/transcript.json",
                        Prompt = "Ship the follow-up change.",
                    },
                    AppJsonSerializerContext.Default.UserPromptSubmitHookInput),
                new MemoryStream(),
                CancellationToken.None);

            NotificationTurn? storedOldTurn = await stateStore.TryReadTurnAsync(
                tempDirectory.FullName,
                "session-123",
                oldTurn.NotificationTurnId,
                CancellationToken.None);
            Assert.Equal("abandoned", storedOldTurn?.Status);
        }
        finally
        {
            tempDirectory.Delete(recursive: true);
        }
    }

    [Fact]
    public async Task HandleUserPromptSubmitAsyncPreservesExactAssignedPendingNullSummaryWithSummaryPlaceholderField()
    {
        DirectoryInfo tempDirectory = Directory.CreateTempSubdirectory();
        using EnvironmentScope environment = SetTelegramEnvironment();

        try
        {
            WorkspaceStateStore stateStore = new(
                FixedUtcNow(),
                NullLogger<WorkspaceStateStore>.Instance);
            HookCommandService service = CreateHookCommandService(
                new RecordingHttpMessageHandler(),
                stateStore);
            NotificationTurn oldTurn = await CreateTurnAsync(
                stateStore,
                tempDirectory.FullName,
                "session-123",
                "2026-03-14T15:51:40.783Z");
            oldTurn.SummaryPlaceholderCreatedAt = null;
            await WriteTurnStateAsync(tempDirectory.FullName, oldTurn);
            const string stopTimestamp = "2026-03-14T15:51:55.783Z";
            await WriteSummaryAsync(
                tempDirectory.FullName,
                "session-123",
                oldTurn,
                new NotificationSummary
                {
                    SessionId = "session-123",
                    NotificationTurnId = oldTurn.NotificationTurnId,
                    NotificationNonce = oldTurn.NotificationNonce,
                    UpdatedAt = stopTimestamp,
                    PlaceholderCreatedAt = stopTimestamp,
                    Status = "pending",
                    Summary = null,
                });

            _ = await service.HandleUserPromptSubmitAsync(
                CreateJsonStream(
                    new UserPromptSubmitHookInput
                    {
                        Cwd = tempDirectory.FullName,
                        SessionId = "session-123",
                        Timestamp = "2026-03-14T15:52:40.783Z",
                        TranscriptPath = "/workspace/transcript.json",
                        Prompt = "Ship the follow-up change.",
                    },
                    AppJsonSerializerContext.Default.UserPromptSubmitHookInput),
                new MemoryStream(),
                CancellationToken.None);

            NotificationTurn? storedOldTurn = await stateStore.TryReadTurnAsync(
                tempDirectory.FullName,
                "session-123",
                oldTurn.NotificationTurnId,
                CancellationToken.None);
            Assert.Equal("open", storedOldTurn?.Status);
        }
        finally
        {
            tempDirectory.Delete(recursive: true);
        }
    }

    [Fact]
    public async Task HandleUserPromptSubmitAsyncPreservesExactPendingSummaryWithSummaryAuthoredPlaceholderProvenance()
    {
        DirectoryInfo tempDirectory = Directory.CreateTempSubdirectory();
        using EnvironmentScope environment = SetTelegramEnvironment();

        try
        {
            WorkspaceStateStore stateStore = new(
                FixedUtcNow(),
                NullLogger<WorkspaceStateStore>.Instance);
            HookCommandService service = CreateHookCommandService(
                new RecordingHttpMessageHandler(),
                stateStore);
            NotificationTurn oldTurn = await CreateTurnAsync(
                stateStore,
                tempDirectory.FullName,
                "session-123",
                "2026-03-14T15:51:40.783Z");
            string placeholderCreatedAt = Assert.IsType<string>(oldTurn.SummaryPlaceholderCreatedAt);
            const string stopTimestamp = "2026-03-14T15:51:55.783Z";
            await WriteSummaryAsync(
                tempDirectory.FullName,
                "session-123",
                oldTurn,
                new NotificationSummary
                {
                    SessionId = "session-123",
                    NotificationTurnId = oldTurn.NotificationTurnId,
                    NotificationNonce = oldTurn.NotificationNonce,
                    UpdatedAt = stopTimestamp,
                    PlaceholderCreatedAt = placeholderCreatedAt,
                    Status = "pending",
                    Summary = null,
                });

            _ = await service.HandleUserPromptSubmitAsync(
                CreateJsonStream(
                    new UserPromptSubmitHookInput
                    {
                        Cwd = tempDirectory.FullName,
                        SessionId = "session-123",
                        Timestamp = "2026-03-14T15:52:40.783Z",
                        TranscriptPath = "/workspace/transcript.json",
                        Prompt = "Ship the follow-up change.",
                    },
                    AppJsonSerializerContext.Default.UserPromptSubmitHookInput),
                new MemoryStream(),
                CancellationToken.None);

            NotificationTurn? storedOldTurn = await stateStore.TryReadTurnAsync(
                tempDirectory.FullName,
                "session-123",
                oldTurn.NotificationTurnId,
                CancellationToken.None);
            Assert.Equal("open", storedOldTurn?.Status);
        }
        finally
        {
            tempDirectory.Delete(recursive: true);
        }
    }

    [Fact]
    public async Task HandleUserPromptSubmitAsyncPreservesObservedExactPendingSummaryWithPlaceholderProvenance()
    {
        DirectoryInfo tempDirectory = Directory.CreateTempSubdirectory();
        using EnvironmentScope environment = SetTelegramEnvironment();

        try
        {
            WorkspaceStateStore stateStore = new(
                FixedUtcNow(),
                NullLogger<WorkspaceStateStore>.Instance);
            RecordingHttpMessageHandler handler = new();
            HookCommandService service = CreateHookCommandService(handler, stateStore);
            NotificationTurn oldTurn = await CreateTurnAsync(
                stateStore,
                tempDirectory.FullName,
                "session-123",
                "2026-03-14T15:51:40.783Z");
            string placeholderCreatedAt = Assert.IsType<string>(oldTurn.SummaryPlaceholderCreatedAt);
            const string stopTimestamp = "2026-03-14T15:51:55.783Z";
            await WriteSummaryAsync(
                tempDirectory.FullName,
                "session-123",
                oldTurn,
                new NotificationSummary
                {
                    SessionId = "session-123",
                    NotificationTurnId = oldTurn.NotificationTurnId,
                    NotificationNonce = oldTurn.NotificationNonce,
                    UpdatedAt = stopTimestamp,
                    PlaceholderCreatedAt = placeholderCreatedAt,
                    Status = "pending",
                    Summary = null,
                });

            _ = await service.HandleStopAsync(
                CreateJsonStream(
                    CreateStopInput(tempDirectory.FullName, stopTimestamp),
                    AppJsonSerializerContext.Default.StopHookInput),
                new MemoryStream(),
                CancellationToken.None);

            Assert.Empty(handler.Requests);
            await AssertPendingStopAsync(
                stateStore,
                tempDirectory.FullName,
                oldTurn,
                stopTimestamp,
                "summary must be a non-empty human-readable sentence");

            _ = await service.HandleUserPromptSubmitAsync(
                CreateJsonStream(
                    new UserPromptSubmitHookInput
                    {
                        Cwd = tempDirectory.FullName,
                        SessionId = "session-123",
                        Timestamp = "2026-03-14T15:52:40.783Z",
                        TranscriptPath = "/workspace/transcript.json",
                        Prompt = "Ship the follow-up change.",
                    },
                    AppJsonSerializerContext.Default.UserPromptSubmitHookInput),
                new MemoryStream(),
                CancellationToken.None);

            NotificationTurn? storedOldTurn = await stateStore.TryReadTurnAsync(
                tempDirectory.FullName,
                "session-123",
                oldTurn.NotificationTurnId,
                CancellationToken.None);
            Assert.Equal("open", storedOldTurn?.Status);

            await WriteSummaryAsync(
                tempDirectory.FullName,
                "session-123",
                oldTurn,
                new NotificationSummary
                {
                    SessionId = "session-123",
                    NotificationTurnId = oldTurn.NotificationTurnId,
                    NotificationNonce = oldTurn.NotificationNonce,
                    UpdatedAt = stopTimestamp,
                    Status = "completed",
                    Summary = "The observed exact pending summary completed after supersession.",
                });

            _ = await service.HandleStopAsync(
                CreateJsonStream(
                    CreateStopInput(tempDirectory.FullName, stopTimestamp),
                    AppJsonSerializerContext.Default.StopHookInput),
                new MemoryStream(),
                CancellationToken.None);
            _ = await service.HandleStopAsync(
                CreateJsonStream(
                    CreateStopInput(tempDirectory.FullName, stopTimestamp),
                    AppJsonSerializerContext.Default.StopHookInput),
                new MemoryStream(),
                CancellationToken.None);

            TelegramSendMessageRequest payload = DeserializeTelegramPayload(
                Assert.Single(handler.Requests));
            Assert.Contains(
                "摘要：The observed exact pending summary completed after supersession.",
                payload.Text,
                StringComparison.Ordinal);
            Assert.Contains(oldTurn.NotificationTurnId, payload.Text, StringComparison.Ordinal);
        }
        finally
        {
            tempDirectory.Delete(recursive: true);
        }
    }

    [Fact]
    public async Task HandleUserPromptSubmitAsyncPreservesObservedExactPendingSummaryWithoutPlaceholderProvenance()
    {
        DirectoryInfo tempDirectory = Directory.CreateTempSubdirectory();
        using EnvironmentScope environment = SetTelegramEnvironment();

        try
        {
            WorkspaceStateStore stateStore = new(
                FixedUtcNow(),
                NullLogger<WorkspaceStateStore>.Instance);
            RecordingHttpMessageHandler handler = new();
            HookCommandService service = CreateHookCommandService(handler, stateStore);
            NotificationTurn oldTurn = await CreateTurnAsync(
                stateStore,
                tempDirectory.FullName,
                "session-123",
                "2026-03-14T15:51:40.783Z");
            const string stopTimestamp = "2026-03-14T15:51:55.783Z";
            await WriteSummaryAsync(
                tempDirectory.FullName,
                "session-123",
                oldTurn,
                new NotificationSummary
                {
                    SessionId = "session-123",
                    NotificationTurnId = oldTurn.NotificationTurnId,
                    NotificationNonce = oldTurn.NotificationNonce,
                    UpdatedAt = stopTimestamp,
                    Status = "pending",
                    Summary = null,
                });

            _ = await service.HandleStopAsync(
                CreateJsonStream(
                    CreateStopInput(tempDirectory.FullName, stopTimestamp),
                    AppJsonSerializerContext.Default.StopHookInput),
                new MemoryStream(),
                CancellationToken.None);

            Assert.Empty(handler.Requests);
            await AssertPendingStopAsync(
                stateStore,
                tempDirectory.FullName,
                oldTurn,
                stopTimestamp,
                "summary must be a non-empty human-readable sentence");

            _ = await service.HandleUserPromptSubmitAsync(
                CreateJsonStream(
                    new UserPromptSubmitHookInput
                    {
                        Cwd = tempDirectory.FullName,
                        SessionId = "session-123",
                        Timestamp = "2026-03-14T15:52:40.783Z",
                        TranscriptPath = "/workspace/transcript.json",
                        Prompt = "Ship the follow-up change.",
                    },
                    AppJsonSerializerContext.Default.UserPromptSubmitHookInput),
                new MemoryStream(),
                CancellationToken.None);

            NotificationTurn? storedOldTurn = await stateStore.TryReadTurnAsync(
                tempDirectory.FullName,
                "session-123",
                oldTurn.NotificationTurnId,
                CancellationToken.None);
            Assert.Equal("open", storedOldTurn?.Status);
        }
        finally
        {
            tempDirectory.Delete(recursive: true);
        }
    }

    [Fact]
    public async Task HandleStopAsyncSuppressesHookPlaceholderAfterTurnUpdatedAtMutation()
    {
        DirectoryInfo tempDirectory = Directory.CreateTempSubdirectory();
        using EnvironmentScope environment = SetTelegramEnvironment();

        try
        {
            WorkspaceStateStore stateStore = new(
                FixedUtcNow(),
                NullLogger<WorkspaceStateStore>.Instance);
            NotificationTurn turn = await CreateTurnAsync(
                stateStore,
                tempDirectory.FullName,
                "session-123",
                "2026-03-14T15:51:40.783Z");
            string placeholderCreatedAt = Assert.IsType<string>(turn.SummaryPlaceholderCreatedAt);
            await WriteSummaryAsync(
                tempDirectory.FullName,
                "session-123",
                turn,
                new NotificationSummary
                {
                    SessionId = "session-123",
                    NotificationTurnId = turn.NotificationTurnId,
                    NotificationNonce = turn.NotificationNonce,
                    UpdatedAt = placeholderCreatedAt,
                    Status = "pending",
                    Summary = null,
                });
            turn.Status = "abandoned";
            turn.UpdatedAt = "2026-03-14T15:52:40.783Z";
            await WriteTurnStateAsync(tempDirectory.FullName, turn);
            RecordingHttpMessageHandler handler = new();
            HookCommandService service = CreateHookCommandService(handler, stateStore);

            _ = await service.HandleStopAsync(
                CreateJsonStream(
                    CreateStopInput(tempDirectory.FullName, placeholderCreatedAt),
                    AppJsonSerializerContext.Default.StopHookInput),
                new MemoryStream(),
                CancellationToken.None);

            Assert.Empty(handler.Requests);
            Assert.False(File.Exists(AppPaths.GetSessionNotificationRecordPath(
                tempDirectory.FullName,
                "session-123",
                CreateStopNotificationKeyForTest(placeholderCreatedAt))));
        }
        finally
        {
            tempDirectory.Delete(recursive: true);
        }
    }

    [Fact]
    public async Task HandleStopAsyncRecoversCompletedExactWhenLegacyPlaceholderUpdatedAtMutatesOnAbandonment()
    {
        DirectoryInfo tempDirectory = Directory.CreateTempSubdirectory();
        using EnvironmentScope environment = SetTelegramEnvironment();

        try
        {
            MutableTimeProvider timeProvider = new(
                new DateTimeOffset(2026, 3, 14, 15, 51, 50, 783, TimeSpan.Zero));
            WorkspaceStateStore stateStore = new(
                timeProvider,
                NullLogger<WorkspaceStateStore>.Instance);
            const string stopTimestamp = "2026-03-14T15:51:50.783Z";
            NotificationTurn completedExactTurn = await CreateTurnAsync(
                stateStore,
                tempDirectory.FullName,
                "session-123",
                "2026-03-14T15:51:40.783Z");
            completedExactTurn.Status = "abandoned";
            completedExactTurn.UpdatedAt = "2026-03-14T15:52:10.783Z";
            await WriteTurnStateAsync(tempDirectory.FullName, completedExactTurn);
            await WriteSummaryAsync(
                tempDirectory.FullName,
                "session-123",
                completedExactTurn,
                new NotificationSummary
                {
                    SessionId = "session-123",
                    NotificationTurnId = completedExactTurn.NotificationTurnId,
                    NotificationNonce = completedExactTurn.NotificationNonce,
                    UpdatedAt = stopTimestamp,
                    Status = "completed",
                    Summary = "The completed exact summary must beat the legacy abandoned placeholder.",
                });
            NotificationTurn legacyPlaceholderTurn = await CreateTurnAsync(
                stateStore,
                tempDirectory.FullName,
                "session-123",
                "2026-03-14T15:51:45.783Z");
            legacyPlaceholderTurn.SummaryPlaceholderCreatedAt = null;
            await WriteTurnStateAsync(tempDirectory.FullName, legacyPlaceholderTurn);
            await WriteSummaryAsync(
                tempDirectory.FullName,
                "session-123",
                legacyPlaceholderTurn,
                new NotificationSummary
                {
                    SessionId = "session-123",
                    NotificationTurnId = legacyPlaceholderTurn.NotificationTurnId,
                    NotificationNonce = legacyPlaceholderTurn.NotificationNonce,
                    UpdatedAt = stopTimestamp,
                    Status = "pending",
                    Summary = null,
                });
            timeProvider.SetUtcNow(
                new DateTimeOffset(2026, 3, 14, 15, 52, 20, 783, TimeSpan.Zero));
            _ = await CreateTurnAsync(
                stateStore,
                tempDirectory.FullName,
                "session-123",
                "2026-03-14T15:52:00.783Z");
            NotificationTurn abandonedPlaceholderTurn = (await stateStore.TryReadTurnAsync(
                tempDirectory.FullName,
                "session-123",
                legacyPlaceholderTurn.NotificationTurnId,
                CancellationToken.None))!;
            Assert.Equal("abandoned", abandonedPlaceholderTurn.Status);
            Assert.Equal(stopTimestamp, abandonedPlaceholderTurn.SummaryPlaceholderCreatedAt);
            Assert.Equal("2026-03-14T15:52:20.783Z", abandonedPlaceholderTurn.UpdatedAt);
            RecordingHttpMessageHandler handler = new();
            HookCommandService service = CreateHookCommandService(handler, stateStore);

            _ = await service.HandleStopAsync(
                CreateJsonStream(
                    CreateStopInput(tempDirectory.FullName, stopTimestamp),
                    AppJsonSerializerContext.Default.StopHookInput),
                new MemoryStream(),
                CancellationToken.None);

            TelegramSendMessageRequest payload = DeserializeTelegramPayload(
                Assert.Single(handler.Requests));
            Assert.Contains(
                "摘要：The completed exact summary must beat the legacy abandoned placeholder.",
                payload.Text,
                StringComparison.Ordinal);
            Assert.Contains(completedExactTurn.NotificationTurnId, payload.Text, StringComparison.Ordinal);
            Assert.DoesNotContain(legacyPlaceholderTurn.NotificationTurnId, payload.Text, StringComparison.Ordinal);
        }
        finally
        {
            tempDirectory.Delete(recursive: true);
        }
    }

    [Fact]
    public async Task HandleStopAsyncRecoversCompletedExactBesideUnstampedLegacyUpdatedAtPlaceholder()
    {
        DirectoryInfo tempDirectory = Directory.CreateTempSubdirectory();
        using EnvironmentScope environment = SetTelegramEnvironment();

        try
        {
            WorkspaceStateStore stateStore = new(
                FixedUtcNow(),
                NullLogger<WorkspaceStateStore>.Instance);
            const string stopTimestamp = "2026-03-14T15:51:50.783Z";
            NotificationTurn completedExactTurn = await CreateTurnAsync(
                stateStore,
                tempDirectory.FullName,
                "session-123",
                "2026-03-14T15:51:40.783Z");
            completedExactTurn.Status = "abandoned";
            completedExactTurn.UpdatedAt = "2026-03-14T15:52:10.783Z";
            await WriteTurnStateAsync(tempDirectory.FullName, completedExactTurn);
            await WriteSummaryAsync(
                tempDirectory.FullName,
                "session-123",
                completedExactTurn,
                new NotificationSummary
                {
                    SessionId = "session-123",
                    NotificationTurnId = completedExactTurn.NotificationTurnId,
                    NotificationNonce = completedExactTurn.NotificationNonce,
                    UpdatedAt = stopTimestamp,
                    Status = "completed",
                    Summary = "The completed exact summary must beat the unstamped legacy placeholder.",
                });
            NotificationTurn legacyPlaceholderTurn = await CreateTurnAsync(
                stateStore,
                tempDirectory.FullName,
                "session-123",
                "2026-03-14T15:51:45.783Z");
            legacyPlaceholderTurn.SummaryPlaceholderCreatedAt = null;
            legacyPlaceholderTurn.Status = "abandoned";
            legacyPlaceholderTurn.UpdatedAt = stopTimestamp;
            await WriteTurnStateAsync(tempDirectory.FullName, legacyPlaceholderTurn);
            await WriteSummaryAsync(
                tempDirectory.FullName,
                "session-123",
                legacyPlaceholderTurn,
                new NotificationSummary
                {
                    SessionId = "session-123",
                    NotificationTurnId = legacyPlaceholderTurn.NotificationTurnId,
                    NotificationNonce = legacyPlaceholderTurn.NotificationNonce,
                    UpdatedAt = stopTimestamp,
                    Status = "pending",
                    Summary = null,
                });
            RecordingHttpMessageHandler handler = new();
            HookCommandService service = CreateHookCommandService(handler, stateStore);

            _ = await service.HandleStopAsync(
                CreateJsonStream(
                    CreateStopInput(tempDirectory.FullName, stopTimestamp),
                    AppJsonSerializerContext.Default.StopHookInput),
                new MemoryStream(),
                CancellationToken.None);

            TelegramSendMessageRequest payload = DeserializeTelegramPayload(
                Assert.Single(handler.Requests));
            Assert.Contains(
                "摘要：The completed exact summary must beat the unstamped legacy placeholder.",
                payload.Text,
                StringComparison.Ordinal);
            Assert.Contains(completedExactTurn.NotificationTurnId, payload.Text, StringComparison.Ordinal);
            Assert.DoesNotContain(legacyPlaceholderTurn.NotificationTurnId, payload.Text, StringComparison.Ordinal);
        }
        finally
        {
            tempDirectory.Delete(recursive: true);
        }
    }

    [Fact]
    public async Task HandleStopAsyncDoesNotTreatInvalidFailedObservationAsPendingSuppression()
    {
        DirectoryInfo tempDirectory = Directory.CreateTempSubdirectory();
        using EnvironmentScope environment = SetTelegramEnvironment();

        try
        {
            WorkspaceStateStore stateStore = new(
                TimeProvider.System,
                NullLogger<WorkspaceStateStore>.Instance);
            NotificationTurn oldTurn = await CreateTurnAsync(
                stateStore,
                tempDirectory.FullName,
                "session-123",
                "2026-03-14T15:51:40.783Z");
            const string oldStopTimestamp = "2026-03-14T15:51:50.783Z";
            await WriteSummaryAsync(
                tempDirectory.FullName,
                "session-123",
                oldTurn,
                new NotificationSummary
                {
                    SessionId = "session-123",
                    NotificationTurnId = "another-turn",
                    NotificationNonce = oldTurn.NotificationNonce,
                    UpdatedAt = oldStopTimestamp,
                    Summary = " ",
                });
            RecordingHttpMessageHandler handler = new(
                [
                    RecordingHttpMessageHandler.CreateJsonResponse(
                        HttpStatusCode.BadRequest,
                        """{"ok":false,"description":"bad request"}"""),
                    RecordingHttpMessageHandler.CreateJsonResponse(
                        HttpStatusCode.OK,
                        """{"ok":true}"""),
                ]);
            HookCommandService service = CreateHookCommandService(handler, stateStore);

            _ = await service.HandleStopAsync(
                CreateJsonStream(
                    CreateStopInput(tempDirectory.FullName, oldStopTimestamp),
                    AppJsonSerializerContext.Default.StopHookInput),
                new MemoryStream(),
                CancellationToken.None);

            Assert.Single(handler.Requests);
            StopObservation observation = await ReadStopObservationAsync(
                AppPaths.GetStopObservationPath(
                    tempDirectory.FullName,
                    "session-123",
                    oldTurn.NotificationTurnId,
                    CreateStopNotificationKeyForTest(oldStopTimestamp)));
            Assert.False(observation.SummaryValid);
            Assert.False(observation.SummaryPendingHandoff);

            _ = await service.HandleUserPromptSubmitAsync(
                CreateJsonStream(
                    new UserPromptSubmitHookInput
                    {
                        Cwd = tempDirectory.FullName,
                        SessionId = "session-123",
                        Timestamp = "2026-03-14T15:52:40.783Z",
                        TranscriptPath = "/workspace/transcript.json",
                        Prompt = "Ship the follow-up after failed invalid notification.",
                    },
                    AppJsonSerializerContext.Default.UserPromptSubmitHookInput),
                new MemoryStream(),
                CancellationToken.None);

            NotificationTurn? abandonedOldTurn = await stateStore.TryReadTurnAsync(
                tempDirectory.FullName,
                "session-123",
                oldTurn.NotificationTurnId,
                CancellationToken.None);
            Assert.Equal("abandoned", abandonedOldTurn?.Status);

            _ = await service.HandleStopAsync(
                CreateJsonStream(
                    CreateStopInput(tempDirectory.FullName, oldStopTimestamp),
                    AppJsonSerializerContext.Default.StopHookInput),
                new MemoryStream(),
                CancellationToken.None);

            Assert.Equal(2, handler.Requests.Count);
            TelegramSendMessageRequest retryPayload =
                DeserializeTelegramPayload(handler.Requests[1]);
            Assert.Contains(
                "stop-20260314t155150783z",
                retryPayload.Text,
                StringComparison.Ordinal);
            Assert.Contains("摘要：当前轮未生成摘要。", retryPayload.Text, StringComparison.Ordinal);
        }
        finally
        {
            tempDirectory.Delete(recursive: true);
        }
    }

    [Fact]
    public async Task AbandonSupersededOpenTurnsAsyncIgnoresCachedCurrentWithDurableDeliveryRecord()
    {
        DirectoryInfo tempDirectory = Directory.CreateTempSubdirectory();

        try
        {
            WorkspaceStateStore stateStore = new(
                TimeProvider.System,
                NullLogger<WorkspaceStateStore>.Instance);
            NotificationTurn olderOpenTurn = await CreateTurnAsync(
                stateStore,
                tempDirectory.FullName,
                "session-123",
                "2026-03-14T15:51:40.783Z");
            NotificationTurn cachedNewerTurn = await CreateTurnAsync(
                stateStore,
                tempDirectory.FullName,
                "session-123",
                "2026-03-14T15:51:50.783Z");
            const string stopTimestamp = "2026-03-14T15:52:00.783Z";
            string notificationKey = CreateStopNotificationKeyForTest(stopTimestamp);
            await WorkspaceStateStore.RecordNotificationAsync(
                AppPaths.GetNotificationRecordPath(
                    Path.GetFullPath(tempDirectory.FullName),
                    "session-123",
                    cachedNewerTurn.NotificationTurnId,
                    notificationKey),
                new NotificationRecord
                {
                    SessionId = "session-123",
                    NotificationTurnId = cachedNewerTurn.NotificationTurnId,
                    NotificationKey = notificationKey,
                    WorkspacePath = Path.GetFullPath(tempDirectory.FullName),
                    StopTimestamp = stopTimestamp,
                    SentAt = stopTimestamp,
                    DeliveryStatus = "sent",
                },
                CancellationToken.None);
            Assert.True(await WorkspaceStateStore.HasDurableDeliveryRecordAsync(
                tempDirectory.FullName,
                "session-123",
                cachedNewerTurn.NotificationTurnId,
                CancellationToken.None));
            olderOpenTurn.Status = "open";
            await WriteTurnStateAsync(tempDirectory.FullName, olderOpenTurn);

            await stateStore.AbandonSupersededOpenTurnsAsync(
                tempDirectory.FullName,
                "session-123",
                "2026-03-14T15:52:10.783Z",
                CancellationToken.None);

            NotificationTurn? storedOlderTurn = await stateStore.TryReadTurnAsync(
                tempDirectory.FullName,
                "session-123",
                olderOpenTurn.NotificationTurnId,
                CancellationToken.None);
            Assert.Equal("open", storedOlderTurn?.Status);
        }
        finally
        {
            tempDirectory.Delete(recursive: true);
        }
    }

    [Fact]
    public async Task AbandonSupersededOpenTurnsAsyncSkipsDeliveredCachedCurrentAndUsesNewerOpenTurn()
    {
        DirectoryInfo tempDirectory = Directory.CreateTempSubdirectory();

        try
        {
            WorkspaceStateStore stateStore = new(
                TimeProvider.System,
                NullLogger<WorkspaceStateStore>.Instance);
            NotificationTurn olderOpenTurn = await CreateTurnAsync(
                stateStore,
                tempDirectory.FullName,
                "session-123",
                "2026-03-14T15:51:30.783Z");
            NotificationTurn deliveredCurrentTurn = await CreateTurnAsync(
                stateStore,
                tempDirectory.FullName,
                "session-123",
                "2026-03-14T15:52:00.783Z");
            NotificationTurn newerOpenTurn = await CreateTurnAsync(
                stateStore,
                tempDirectory.FullName,
                "session-123",
                "2026-03-14T15:51:50.783Z");
            const string stopTimestamp = "2026-03-14T15:52:05.783Z";
            string notificationKey = CreateStopNotificationKeyForTest(stopTimestamp);
            await RecordSentNotificationAsync(
                tempDirectory.FullName,
                deliveredCurrentTurn,
                notificationKey,
                stopTimestamp);
            await File.WriteAllTextAsync(
                AppPaths.GetCurrentStatePath(tempDirectory.FullName, "session-123"),
                JsonSerializer.Serialize(
                    new CurrentNotificationState
                    {
                        SessionId = "session-123",
                        NotificationTurnId = deliveredCurrentTurn.NotificationTurnId,
                        NotificationNonce = deliveredCurrentTurn.NotificationNonce,
                        SummaryPath = AppPaths.GetSummaryStatePath(
                            tempDirectory.FullName,
                            "session-123",
                            deliveredCurrentTurn.NotificationTurnId),
                        UpdatedAt = stopTimestamp,
                    },
                    AppJsonSerializerContext.Default.CurrentNotificationState));
            olderOpenTurn.Status = "open";
            deliveredCurrentTurn.Status = "open";
            newerOpenTurn.Status = "open";
            await WriteTurnStateAsync(tempDirectory.FullName, olderOpenTurn);
            await WriteTurnStateAsync(tempDirectory.FullName, deliveredCurrentTurn);
            await WriteTurnStateAsync(tempDirectory.FullName, newerOpenTurn);

            await stateStore.AbandonSupersededOpenTurnsAsync(
                tempDirectory.FullName,
                "session-123",
                "2026-03-14T15:52:10.783Z",
                CancellationToken.None);

            NotificationTurn? storedOlderTurn = await stateStore.TryReadTurnAsync(
                tempDirectory.FullName,
                "session-123",
                olderOpenTurn.NotificationTurnId,
                CancellationToken.None);
            Assert.Equal("abandoned", storedOlderTurn?.Status);
            NotificationTurn? storedNewerOpenTurn = await stateStore.TryReadTurnAsync(
                tempDirectory.FullName,
                "session-123",
                newerOpenTurn.NotificationTurnId,
                CancellationToken.None);
            Assert.Equal("open", storedNewerOpenTurn?.Status);
        }
        finally
        {
            tempDirectory.Delete(recursive: true);
        }
    }

    [Fact]
    public async Task AbandonSupersededOpenTurnsAsyncKeepsOlderOpenWhenSupersederIsDeliveredAfterResolution()
    {
        DirectoryInfo tempDirectory = Directory.CreateTempSubdirectory();

        try
        {
            WorkspaceStateStore stateStore = new(
                TimeProvider.System,
                NullLogger<WorkspaceStateStore>.Instance);
            NotificationTurn olderOpenTurn = await CreateTurnAsync(
                stateStore,
                tempDirectory.FullName,
                "session-123",
                "2026-03-14T15:51:40.783Z");
            NotificationTurn newerOpenTurn = await CreateTurnAsync(
                stateStore,
                tempDirectory.FullName,
                "session-123",
                "2026-03-14T15:51:50.783Z");
            olderOpenTurn.Status = "open";
            await WriteTurnStateAsync(tempDirectory.FullName, olderOpenTurn);
            const string stopTimestamp = "2026-03-14T15:52:00.783Z";
            string notificationKey = CreateStopNotificationKeyForTest(stopTimestamp);
            stateStore.OnSupersedingOpenTurnResolvedForTestingAsync = async (supersedingTurn, _) =>
                await RecordSentNotificationAsync(
                    tempDirectory.FullName,
                    supersedingTurn,
                    notificationKey,
                    stopTimestamp);

            await stateStore.AbandonSupersededOpenTurnsAsync(
                tempDirectory.FullName,
                "session-123",
                "2026-03-14T15:52:10.783Z",
                CancellationToken.None);

            NotificationTurn? storedOlderTurn = await stateStore.TryReadTurnAsync(
                tempDirectory.FullName,
                "session-123",
                olderOpenTurn.NotificationTurnId,
                CancellationToken.None);
            Assert.Equal("open", storedOlderTurn?.Status);
            Assert.True(await WorkspaceStateStore.HasDurableDeliveryRecordAsync(
                tempDirectory.FullName,
                "session-123",
                newerOpenTurn.NotificationTurnId,
                CancellationToken.None));
        }
        finally
        {
            tempDirectory.Delete(recursive: true);
        }
    }

    [Fact]
    public async Task AbandonSupersededOpenTurnsAsyncKeepsTargetOpenWhenTargetIsDeliveredAfterResolution()
    {
        DirectoryInfo tempDirectory = Directory.CreateTempSubdirectory();

        try
        {
            WorkspaceStateStore stateStore = new(
                TimeProvider.System,
                NullLogger<WorkspaceStateStore>.Instance);
            NotificationTurn olderOpenTurn = await CreateTurnAsync(
                stateStore,
                tempDirectory.FullName,
                "session-123",
                "2026-03-14T15:51:40.783Z");
            _ = await CreateTurnAsync(
                stateStore,
                tempDirectory.FullName,
                "session-123",
                "2026-03-14T15:51:50.783Z");
            olderOpenTurn.Status = "open";
            await WriteTurnStateAsync(tempDirectory.FullName, olderOpenTurn);
            const string stopTimestamp = "2026-03-14T15:52:00.783Z";
            string notificationKey = CreateStopNotificationKeyForTest(stopTimestamp);
            bool abandonWriteHookReached = false;
            stateStore.OnSupersedingOpenTurnResolvedForTestingAsync = async (_, _) =>
                await RecordSentNotificationAsync(
                    tempDirectory.FullName,
                    olderOpenTurn,
                    notificationKey,
                    stopTimestamp);
            stateStore.OnBeforeAbandonSupersededTurnForTestingAsync = (_, _, _) =>
            {
                abandonWriteHookReached = true;
                return Task.CompletedTask;
            };

            await stateStore.AbandonSupersededOpenTurnsAsync(
                tempDirectory.FullName,
                "session-123",
                "2026-03-14T15:52:10.783Z",
                CancellationToken.None);

            Assert.False(abandonWriteHookReached);
            NotificationTurn? storedOlderTurn = await stateStore.TryReadTurnAsync(
                tempDirectory.FullName,
                "session-123",
                olderOpenTurn.NotificationTurnId,
                CancellationToken.None);
            Assert.Equal("open", storedOlderTurn?.Status);
            Assert.True(await WorkspaceStateStore.HasDurableDeliveryRecordAsync(
                tempDirectory.FullName,
                "session-123",
                olderOpenTurn.NotificationTurnId,
                CancellationToken.None));
        }
        finally
        {
            tempDirectory.Delete(recursive: true);
        }
    }

    [Fact]
    public async Task AbandonSupersededOpenTurnsAsyncKeepsOlderOpenWhenSupersederIsDeliveredBeforeAbandonWrite()
    {
        DirectoryInfo tempDirectory = Directory.CreateTempSubdirectory();

        try
        {
            WorkspaceStateStore stateStore = new(
                TimeProvider.System,
                NullLogger<WorkspaceStateStore>.Instance);
            NotificationTurn olderOpenTurn = await CreateTurnAsync(
                stateStore,
                tempDirectory.FullName,
                "session-123",
                "2026-03-14T15:51:40.783Z");
            NotificationTurn newerOpenTurn = await CreateTurnAsync(
                stateStore,
                tempDirectory.FullName,
                "session-123",
                "2026-03-14T15:51:50.783Z");
            olderOpenTurn.Status = "open";
            await WriteTurnStateAsync(tempDirectory.FullName, olderOpenTurn);
            const string stopTimestamp = "2026-03-14T15:52:00.783Z";
            string notificationKey = CreateStopNotificationKeyForTest(stopTimestamp);
            stateStore.OnBeforeAbandonSupersededTurnForTestingAsync =
                async (_, supersedingTurn, _) =>
                    await RecordSentNotificationAsync(
                        tempDirectory.FullName,
                        supersedingTurn,
                        notificationKey,
                        stopTimestamp);

            await stateStore.AbandonSupersededOpenTurnsAsync(
                tempDirectory.FullName,
                "session-123",
                "2026-03-14T15:52:10.783Z",
                CancellationToken.None);

            NotificationTurn? storedOlderTurn = await stateStore.TryReadTurnAsync(
                tempDirectory.FullName,
                "session-123",
                olderOpenTurn.NotificationTurnId,
                CancellationToken.None);
            Assert.Equal("open", storedOlderTurn?.Status);
            Assert.True(await WorkspaceStateStore.HasDurableDeliveryRecordAsync(
                tempDirectory.FullName,
                "session-123",
                newerOpenTurn.NotificationTurnId,
                CancellationToken.None));
        }
        finally
        {
            tempDirectory.Delete(recursive: true);
        }
    }

    [Fact]
    public async Task AbandonSupersededOpenTurnsAsyncKeepsOlderOpenWhenTargetIsDeliveredBeforeAbandonWrite()
    {
        DirectoryInfo tempDirectory = Directory.CreateTempSubdirectory();

        try
        {
            WorkspaceStateStore stateStore = new(
                TimeProvider.System,
                NullLogger<WorkspaceStateStore>.Instance);
            NotificationTurn olderOpenTurn = await CreateTurnAsync(
                stateStore,
                tempDirectory.FullName,
                "session-123",
                "2026-03-14T15:51:40.783Z");
            _ = await CreateTurnAsync(
                stateStore,
                tempDirectory.FullName,
                "session-123",
                "2026-03-14T15:51:50.783Z");
            olderOpenTurn.Status = "open";
            await WriteTurnStateAsync(tempDirectory.FullName, olderOpenTurn);
            const string stopTimestamp = "2026-03-14T15:52:00.783Z";
            string notificationKey = CreateStopNotificationKeyForTest(stopTimestamp);
            stateStore.OnBeforeAbandonSupersededTurnForTestingAsync =
                async (currentTurn, _, _) =>
                    await RecordSentNotificationAsync(
                        tempDirectory.FullName,
                        currentTurn,
                        notificationKey,
                        stopTimestamp);

            await stateStore.AbandonSupersededOpenTurnsAsync(
                tempDirectory.FullName,
                "session-123",
                "2026-03-14T15:52:10.783Z",
                CancellationToken.None);

            NotificationTurn? storedOlderTurn = await stateStore.TryReadTurnAsync(
                tempDirectory.FullName,
                "session-123",
                olderOpenTurn.NotificationTurnId,
                CancellationToken.None);
            Assert.Equal("open", storedOlderTurn?.Status);
            Assert.True(await WorkspaceStateStore.HasDurableDeliveryRecordAsync(
                tempDirectory.FullName,
                "session-123",
                olderOpenTurn.NotificationTurnId,
                CancellationToken.None));
        }
        finally
        {
            tempDirectory.Delete(recursive: true);
        }
    }

    [Fact]
    public async Task MarkTurnAbandonedIfSupersededAsyncIgnoresDurablyDeliveredSuperseder()
    {
        DirectoryInfo tempDirectory = Directory.CreateTempSubdirectory();

        try
        {
            WorkspaceStateStore stateStore = new(
                TimeProvider.System,
                NullLogger<WorkspaceStateStore>.Instance);
            NotificationTurn olderOpenTurn = await CreateTurnAsync(
                stateStore,
                tempDirectory.FullName,
                "session-123",
                "2026-03-14T15:51:40.783Z");
            NotificationTurn newerDeliveredTurn = await CreateTurnAsync(
                stateStore,
                tempDirectory.FullName,
                "session-123",
                "2026-03-14T15:51:50.783Z");
            const string stopTimestamp = "2026-03-14T15:52:00.783Z";
            string notificationKey = CreateStopNotificationKeyForTest(stopTimestamp);
            await WorkspaceStateStore.RecordNotificationAsync(
                AppPaths.GetNotificationRecordPath(
                    Path.GetFullPath(tempDirectory.FullName),
                    "session-123",
                    newerDeliveredTurn.NotificationTurnId,
                    notificationKey),
                new NotificationRecord
                {
                    SessionId = "session-123",
                    NotificationTurnId = newerDeliveredTurn.NotificationTurnId,
                    NotificationKey = notificationKey,
                    WorkspacePath = Path.GetFullPath(tempDirectory.FullName),
                    StopTimestamp = stopTimestamp,
                    SentAt = stopTimestamp,
                    DeliveryStatus = "sent",
                },
                CancellationToken.None);
            olderOpenTurn.Status = "open";
            await WriteTurnStateAsync(tempDirectory.FullName, olderOpenTurn);

            await stateStore.MarkTurnAbandonedIfSupersededAsync(
                tempDirectory.FullName,
                olderOpenTurn,
                "2026-03-14T15:52:10.783Z",
                CancellationToken.None);

            NotificationTurn? storedOlderTurn = await stateStore.TryReadTurnAsync(
                tempDirectory.FullName,
                "session-123",
                olderOpenTurn.NotificationTurnId,
                CancellationToken.None);
            Assert.Equal("open", storedOlderTurn?.Status);
        }
        finally
        {
            tempDirectory.Delete(recursive: true);
        }
    }

    [Fact]
    public async Task MarkTurnAbandonedIfSupersededAsyncKeepsTurnOpenWhenSupersederIsDeliveredBeforeAbandonWrite()
    {
        DirectoryInfo tempDirectory = Directory.CreateTempSubdirectory();

        try
        {
            WorkspaceStateStore stateStore = new(
                TimeProvider.System,
                NullLogger<WorkspaceStateStore>.Instance);
            NotificationTurn olderOpenTurn = await CreateTurnAsync(
                stateStore,
                tempDirectory.FullName,
                "session-123",
                "2026-03-14T15:51:40.783Z");
            NotificationTurn newerOpenTurn = await CreateTurnAsync(
                stateStore,
                tempDirectory.FullName,
                "session-123",
                "2026-03-14T15:51:50.783Z");
            olderOpenTurn.Status = "open";
            await WriteTurnStateAsync(tempDirectory.FullName, olderOpenTurn);
            const string stopTimestamp = "2026-03-14T15:52:00.783Z";
            string notificationKey = CreateStopNotificationKeyForTest(stopTimestamp);
            stateStore.OnBeforeAbandonSupersededTurnForTestingAsync =
                async (_, supersedingTurn, _) =>
                    await RecordSentNotificationAsync(
                        tempDirectory.FullName,
                        supersedingTurn,
                        notificationKey,
                        stopTimestamp);

            await stateStore.MarkTurnAbandonedIfSupersededAsync(
                tempDirectory.FullName,
                olderOpenTurn,
                "2026-03-14T15:52:10.783Z",
                CancellationToken.None);

            NotificationTurn? storedOlderTurn = await stateStore.TryReadTurnAsync(
                tempDirectory.FullName,
                "session-123",
                olderOpenTurn.NotificationTurnId,
                CancellationToken.None);
            Assert.Equal("open", storedOlderTurn?.Status);
            Assert.True(await WorkspaceStateStore.HasDurableDeliveryRecordAsync(
                tempDirectory.FullName,
                "session-123",
                newerOpenTurn.NotificationTurnId,
                CancellationToken.None));
        }
        finally
        {
            tempDirectory.Delete(recursive: true);
        }
    }

    [Fact]
    public async Task MarkTurnAbandonedIfSupersededAsyncKeepsTurnOpenWhenTargetIsDeliveredBeforeAbandonWrite()
    {
        DirectoryInfo tempDirectory = Directory.CreateTempSubdirectory();

        try
        {
            WorkspaceStateStore stateStore = new(
                TimeProvider.System,
                NullLogger<WorkspaceStateStore>.Instance);
            NotificationTurn olderOpenTurn = await CreateTurnAsync(
                stateStore,
                tempDirectory.FullName,
                "session-123",
                "2026-03-14T15:51:40.783Z");
            _ = await CreateTurnAsync(
                stateStore,
                tempDirectory.FullName,
                "session-123",
                "2026-03-14T15:51:50.783Z");
            olderOpenTurn.Status = "open";
            await WriteTurnStateAsync(tempDirectory.FullName, olderOpenTurn);
            const string stopTimestamp = "2026-03-14T15:52:00.783Z";
            string notificationKey = CreateStopNotificationKeyForTest(stopTimestamp);
            stateStore.OnBeforeAbandonSupersededTurnForTestingAsync =
                async (currentTurn, _, _) =>
                    await RecordSentNotificationAsync(
                        tempDirectory.FullName,
                        currentTurn,
                        notificationKey,
                        stopTimestamp);

            await stateStore.MarkTurnAbandonedIfSupersededAsync(
                tempDirectory.FullName,
                olderOpenTurn,
                "2026-03-14T15:52:10.783Z",
                CancellationToken.None);

            NotificationTurn? storedOlderTurn = await stateStore.TryReadTurnAsync(
                tempDirectory.FullName,
                "session-123",
                olderOpenTurn.NotificationTurnId,
                CancellationToken.None);
            Assert.Equal("open", storedOlderTurn?.Status);
            Assert.True(await WorkspaceStateStore.HasDurableDeliveryRecordAsync(
                tempDirectory.FullName,
                "session-123",
                olderOpenTurn.NotificationTurnId,
                CancellationToken.None));
        }
        finally
        {
            tempDirectory.Delete(recursive: true);
        }
    }

    [Fact]
    public async Task MarkTurnAbandonedIfSupersededAsyncKeepsTurnOpenWhenSupersederIsDeliveredAfterResolution()
    {
        DirectoryInfo tempDirectory = Directory.CreateTempSubdirectory();

        try
        {
            WorkspaceStateStore stateStore = new(
                TimeProvider.System,
                NullLogger<WorkspaceStateStore>.Instance);
            NotificationTurn olderOpenTurn = await CreateTurnAsync(
                stateStore,
                tempDirectory.FullName,
                "session-123",
                "2026-03-14T15:51:40.783Z");
            NotificationTurn newerOpenTurn = await CreateTurnAsync(
                stateStore,
                tempDirectory.FullName,
                "session-123",
                "2026-03-14T15:51:50.783Z");
            olderOpenTurn.Status = "open";
            await WriteTurnStateAsync(tempDirectory.FullName, olderOpenTurn);
            const string stopTimestamp = "2026-03-14T15:52:00.783Z";
            string notificationKey = CreateStopNotificationKeyForTest(stopTimestamp);
            stateStore.OnSupersedingOpenTurnResolvedForTestingAsync = async (supersedingTurn, _) =>
                await RecordSentNotificationAsync(
                    tempDirectory.FullName,
                    supersedingTurn,
                    notificationKey,
                    stopTimestamp);

            await stateStore.MarkTurnAbandonedIfSupersededAsync(
                tempDirectory.FullName,
                olderOpenTurn,
                "2026-03-14T15:52:10.783Z",
                CancellationToken.None);

            NotificationTurn? storedOlderTurn = await stateStore.TryReadTurnAsync(
                tempDirectory.FullName,
                "session-123",
                olderOpenTurn.NotificationTurnId,
                CancellationToken.None);
            Assert.Equal("open", storedOlderTurn?.Status);
            Assert.True(await WorkspaceStateStore.HasDurableDeliveryRecordAsync(
                tempDirectory.FullName,
                "session-123",
                newerOpenTurn.NotificationTurnId,
                CancellationToken.None));
        }
        finally
        {
            tempDirectory.Delete(recursive: true);
        }
    }

    [Fact]
    public async Task MarkTurnAbandonedIfSupersededAsyncKeepsTurnOpenWhenTargetIsDeliveredAfterResolution()
    {
        DirectoryInfo tempDirectory = Directory.CreateTempSubdirectory();

        try
        {
            WorkspaceStateStore stateStore = new(
                TimeProvider.System,
                NullLogger<WorkspaceStateStore>.Instance);
            NotificationTurn olderOpenTurn = await CreateTurnAsync(
                stateStore,
                tempDirectory.FullName,
                "session-123",
                "2026-03-14T15:51:40.783Z");
            _ = await CreateTurnAsync(
                stateStore,
                tempDirectory.FullName,
                "session-123",
                "2026-03-14T15:51:50.783Z");
            olderOpenTurn.Status = "open";
            await WriteTurnStateAsync(tempDirectory.FullName, olderOpenTurn);
            const string stopTimestamp = "2026-03-14T15:52:00.783Z";
            string notificationKey = CreateStopNotificationKeyForTest(stopTimestamp);
            bool abandonWriteHookReached = false;
            stateStore.OnSupersedingOpenTurnResolvedForTestingAsync = async (_, _) =>
                await RecordSentNotificationAsync(
                    tempDirectory.FullName,
                    olderOpenTurn,
                    notificationKey,
                    stopTimestamp);
            stateStore.OnBeforeAbandonSupersededTurnForTestingAsync = (_, _, _) =>
            {
                abandonWriteHookReached = true;
                return Task.CompletedTask;
            };

            await stateStore.MarkTurnAbandonedIfSupersededAsync(
                tempDirectory.FullName,
                olderOpenTurn,
                "2026-03-14T15:52:10.783Z",
                CancellationToken.None);

            Assert.False(abandonWriteHookReached);
            NotificationTurn? storedOlderTurn = await stateStore.TryReadTurnAsync(
                tempDirectory.FullName,
                "session-123",
                olderOpenTurn.NotificationTurnId,
                CancellationToken.None);
            Assert.Equal("open", storedOlderTurn?.Status);
            Assert.True(await WorkspaceStateStore.HasDurableDeliveryRecordAsync(
                tempDirectory.FullName,
                "session-123",
                olderOpenTurn.NotificationTurnId,
                CancellationToken.None));
        }
        finally
        {
            tempDirectory.Delete(recursive: true);
        }
    }

    [Fact]
    public async Task MarkTurnAbandonedIfSupersededAsyncSkipsDeliveredSupersederAndUsesNextOpenTurn()
    {
        DirectoryInfo tempDirectory = Directory.CreateTempSubdirectory();

        try
        {
            WorkspaceStateStore stateStore = new(
                TimeProvider.System,
                NullLogger<WorkspaceStateStore>.Instance);
            NotificationTurn olderOpenTurn = await CreateTurnAsync(
                stateStore,
                tempDirectory.FullName,
                "session-123",
                "2026-03-14T15:51:30.783Z");
            NotificationTurn newerOpenTurn = await CreateTurnAsync(
                stateStore,
                tempDirectory.FullName,
                "session-123",
                "2026-03-14T15:51:50.783Z");
            NotificationTurn deliveredTurn = await CreateTurnAsync(
                stateStore,
                tempDirectory.FullName,
                "session-123",
                "2026-03-14T15:52:00.783Z");
            const string stopTimestamp = "2026-03-14T15:52:05.783Z";
            string notificationKey = CreateStopNotificationKeyForTest(stopTimestamp);
            await WorkspaceStateStore.RecordNotificationAsync(
                AppPaths.GetNotificationRecordPath(
                    Path.GetFullPath(tempDirectory.FullName),
                    "session-123",
                    deliveredTurn.NotificationTurnId,
                    notificationKey),
                new NotificationRecord
                {
                    SessionId = "session-123",
                    NotificationTurnId = deliveredTurn.NotificationTurnId,
                    NotificationKey = notificationKey,
                    WorkspacePath = Path.GetFullPath(tempDirectory.FullName),
                    StopTimestamp = stopTimestamp,
                    SentAt = stopTimestamp,
                    DeliveryStatus = "sent",
                },
                CancellationToken.None);
            olderOpenTurn.Status = "open";
            await WriteTurnStateAsync(tempDirectory.FullName, olderOpenTurn);
            deliveredTurn.Status = "open";
            await WriteTurnStateAsync(tempDirectory.FullName, deliveredTurn);
            newerOpenTurn.Status = "open";
            await WriteTurnStateAsync(tempDirectory.FullName, newerOpenTurn);
            await File.WriteAllTextAsync(
                AppPaths.GetCurrentStatePath(tempDirectory.FullName, "session-123"),
                JsonSerializer.Serialize(
                    new CurrentNotificationState
                    {
                        SessionId = "session-123",
                        NotificationTurnId = deliveredTurn.NotificationTurnId,
                        NotificationNonce = deliveredTurn.NotificationNonce,
                        SummaryPath = AppPaths.GetSummaryStatePath(
                            tempDirectory.FullName,
                            "session-123",
                            deliveredTurn.NotificationTurnId),
                        UpdatedAt = stopTimestamp,
                    },
                    AppJsonSerializerContext.Default.CurrentNotificationState));

            await stateStore.MarkTurnAbandonedIfSupersededAsync(
                tempDirectory.FullName,
                olderOpenTurn,
                "2026-03-14T15:52:10.783Z",
                CancellationToken.None);

            NotificationTurn? storedOlderTurn = await stateStore.TryReadTurnAsync(
                tempDirectory.FullName,
                "session-123",
                olderOpenTurn.NotificationTurnId,
                CancellationToken.None);
            Assert.Equal("abandoned", storedOlderTurn?.Status);
            NotificationTurn? storedNewerOpenTurn = await stateStore.TryReadTurnAsync(
                tempDirectory.FullName,
                "session-123",
                newerOpenTurn.NotificationTurnId,
                CancellationToken.None);
            Assert.Equal("open", storedNewerOpenTurn?.Status);
            Assert.True(File.Exists(AppPaths.GetNotificationRecordPath(
                tempDirectory.FullName,
                "session-123",
                deliveredTurn.NotificationTurnId,
                notificationKey)));
        }
        finally
        {
            tempDirectory.Delete(recursive: true);
        }
    }

    [Fact]
    public async Task MarkTurnNotifiedAsyncDoesNotOverwriteAbandonedTurn()
    {
        DirectoryInfo tempDirectory = Directory.CreateTempSubdirectory();

        try
        {
            WorkspaceStateStore stateStore = new(
                TimeProvider.System,
                NullLogger<WorkspaceStateStore>.Instance);
            NotificationTurn oldTurn = await CreateTurnAsync(
                stateStore,
                tempDirectory.FullName,
                "session-123",
                "2026-03-14T15:51:40.783Z");
            _ = await CreateTurnAsync(
                stateStore,
                tempDirectory.FullName,
                "session-123",
                "2026-03-14T15:52:40.783Z");

            await WorkspaceStateStore.MarkTurnNotifiedAsync(
                tempDirectory.FullName,
                oldTurn,
                "2026-03-14T15:52:50.783Z",
                CancellationToken.None);

            NotificationTurn? storedOldTurn = await stateStore.TryReadTurnAsync(
                tempDirectory.FullName,
                "session-123",
                oldTurn.NotificationTurnId,
                CancellationToken.None);
            Assert.Equal("abandoned", storedOldTurn?.Status);
        }
        finally
        {
            tempDirectory.Delete(recursive: true);
        }
    }

    [Fact]
    public async Task HandleStopAsyncDefersMissingSummaryAndLaterStopSends()
    {
        DirectoryInfo tempDirectory = Directory.CreateTempSubdirectory();
        using EnvironmentScope environment = SetTelegramEnvironment();

        try
        {
            WorkspaceStateStore stateStore = new(
                TimeProvider.System,
                NullLogger<WorkspaceStateStore>.Instance);
            NotificationTurn turn = await CreateTurnAsync(
                stateStore,
                tempDirectory.FullName,
                "session-123");
            string summaryPath = AppPaths.GetSummaryStatePath(
                tempDirectory.FullName,
                "session-123",
                turn.NotificationTurnId);
            File.Delete(summaryPath);
            RecordingHttpMessageHandler handler = new();
            HookCommandService service = CreateHookCommandService(handler, stateStore);
            const string firstStopTimestamp = "2026-03-14T15:51:50.783Z";
            const string secondStopTimestamp = "2026-03-14T15:52:50.783Z";

            _ = await service.HandleStopAsync(
                CreateJsonStream(
                    CreateStopInput(tempDirectory.FullName, firstStopTimestamp),
                    AppJsonSerializerContext.Default.StopHookInput),
                new MemoryStream(),
                CancellationToken.None);

            Assert.Empty(handler.Requests);
            await AssertPendingStopAsync(
                stateStore,
                tempDirectory.FullName,
                turn,
                firstStopTimestamp,
                "Summary file is missing");

            await WriteSummaryAsync(
                tempDirectory.FullName,
                "session-123",
                turn,
                new NotificationSummary
                {
                    SessionId = "session-123",
                    NotificationTurnId = turn.NotificationTurnId,
                    NotificationNonce = turn.NotificationNonce,
                    UpdatedAt = secondStopTimestamp,
                    Summary = "The missing summary was written later.",
                });

            _ = await service.HandleStopAsync(
                CreateJsonStream(
                    CreateStopInput(tempDirectory.FullName, secondStopTimestamp),
                    AppJsonSerializerContext.Default.StopHookInput),
                new MemoryStream(),
                CancellationToken.None);

            TelegramSendMessageRequest payload = DeserializeTelegramPayload(
                Assert.Single(handler.Requests));
            Assert.Contains(
                "摘要：The missing summary was written later.",
                payload.Text,
                StringComparison.Ordinal);
        }
        finally
        {
            tempDirectory.Delete(recursive: true);
        }
    }

    [Fact]
    public async Task HandleStopAsyncDefersInvalidJsonSummaryAndLaterStopSends()
    {
        DirectoryInfo tempDirectory = Directory.CreateTempSubdirectory();
        using EnvironmentScope environment = SetTelegramEnvironment();

        try
        {
            WorkspaceStateStore stateStore = new(
                TimeProvider.System,
                NullLogger<WorkspaceStateStore>.Instance);
            NotificationTurn turn = await CreateTurnAsync(
                stateStore,
                tempDirectory.FullName,
                "session-123");
            string summaryPath = AppPaths.GetSummaryStatePath(
                tempDirectory.FullName,
                "session-123",
                turn.NotificationTurnId);
            await File.WriteAllTextAsync(summaryPath, "{");
            RecordingHttpMessageHandler handler = new();
            HookCommandService service = CreateHookCommandService(handler, stateStore);
            const string firstStopTimestamp = "2026-03-14T15:51:50.783Z";
            const string secondStopTimestamp = "2026-03-14T15:52:50.783Z";

            _ = await service.HandleStopAsync(
                CreateJsonStream(
                    CreateStopInput(tempDirectory.FullName, firstStopTimestamp),
                    AppJsonSerializerContext.Default.StopHookInput),
                new MemoryStream(),
                CancellationToken.None);

            Assert.Empty(handler.Requests);
            await AssertPendingStopAsync(
                stateStore,
                tempDirectory.FullName,
                turn,
                firstStopTimestamp,
                "could not be parsed as JSON");

            await WriteSummaryAsync(
                tempDirectory.FullName,
                "session-123",
                turn,
                new NotificationSummary
                {
                    SessionId = "session-123",
                    NotificationTurnId = turn.NotificationTurnId,
                    NotificationNonce = turn.NotificationNonce,
                    UpdatedAt = secondStopTimestamp,
                    Summary = "The half-written summary was completed later.",
                });

            _ = await service.HandleStopAsync(
                CreateJsonStream(
                    CreateStopInput(tempDirectory.FullName, secondStopTimestamp),
                    AppJsonSerializerContext.Default.StopHookInput),
                new MemoryStream(),
                CancellationToken.None);

            TelegramSendMessageRequest payload = DeserializeTelegramPayload(
                Assert.Single(handler.Requests));
            Assert.Contains(
                "摘要：The half-written summary was completed later.",
                payload.Text,
                StringComparison.Ordinal);
        }
        finally
        {
            tempDirectory.Delete(recursive: true);
        }
    }

    [Fact]
    public async Task HandleStopAsyncDoesNotLoopOnStaleTurnAfterFallbackDefault()
    {
        DirectoryInfo tempDirectory = Directory.CreateTempSubdirectory();
        using EnvironmentScope environment = SetTelegramEnvironment();

        try
        {
            WorkspaceStateStore stateStore = new(
                TimeProvider.System,
                NullLogger<WorkspaceStateStore>.Instance);
            NotificationTurn turn = await CreateTurnAsync(
                stateStore,
                tempDirectory.FullName,
                "session-123");
            RecordingHttpMessageHandler handler = new();
            HookCommandService service = CreateHookCommandService(handler, stateStore);

            _ = await service.HandleStopAsync(
                CreateJsonStream(
                    CreateStopInput(tempDirectory.FullName, "2026-03-14T15:51:50.783Z"),
                    AppJsonSerializerContext.Default.StopHookInput),
                new MemoryStream(),
                CancellationToken.None);
            _ = await service.HandleStopAsync(
                CreateJsonStream(
                    CreateStopInput(tempDirectory.FullName, "2026-03-14T15:52:50.783Z"),
                    AppJsonSerializerContext.Default.StopHookInput),
                new MemoryStream(),
                CancellationToken.None);

            Assert.Empty(handler.Requests);
            NotificationTurn? storedTurn = await stateStore.TryReadTurnAsync(
                tempDirectory.FullName,
                "session-123",
                turn.NotificationTurnId,
                CancellationToken.None);
            Assert.Equal("open", storedTurn?.Status);
        }
        finally
        {
            tempDirectory.Delete(recursive: true);
        }
    }

    [Fact]
    public async Task HandleStopAsyncDoesNotCloseNewerTurnForReplayedOlderStop()
    {
        DirectoryInfo tempDirectory = Directory.CreateTempSubdirectory();
        using EnvironmentScope environment = SetTelegramEnvironment();

        try
        {
            WorkspaceStateStore stateStore = new(
                TimeProvider.System,
                NullLogger<WorkspaceStateStore>.Instance);
            NotificationTurn turn = await CreateTurnAsync(
                stateStore,
                tempDirectory.FullName,
                "session-123",
                "2026-03-14T15:51:40.783Z");
            await WriteSummaryAsync(
                tempDirectory.FullName,
                "session-123",
                turn,
                new NotificationSummary
                {
                    SessionId = "session-123",
                    NotificationTurnId = turn.NotificationTurnId,
                    NotificationNonce = turn.NotificationNonce,
                    UpdatedAt = "2026-03-14T15:51:50.783Z",
                    Summary = "The replayed older Stop was already delivered.",
                });
            RecordingHttpMessageHandler handler = new();
            HookCommandService service = CreateHookCommandService(handler, stateStore);

            _ = await service.HandleStopAsync(
                CreateJsonStream(
                    CreateStopInput(tempDirectory.FullName, "2026-03-14T15:51:50.783Z"),
                    AppJsonSerializerContext.Default.StopHookInput),
                new MemoryStream(),
                CancellationToken.None);

            NotificationTurn newerTurn = await CreateTurnAsync(
                stateStore,
                tempDirectory.FullName,
                "session-123",
                "2026-03-14T15:53:40.783Z");

            _ = await service.HandleStopAsync(
                CreateJsonStream(
                    CreateStopInput(tempDirectory.FullName, "2026-03-14T15:51:50.783Z"),
                    AppJsonSerializerContext.Default.StopHookInput),
                new MemoryStream(),
                CancellationToken.None);

            Assert.Single(handler.Requests);
            NotificationTurn? storedNewerTurn = await stateStore.TryReadTurnAsync(
                tempDirectory.FullName,
                "session-123",
                newerTurn.NotificationTurnId,
                CancellationToken.None);
            Assert.Equal("open", storedNewerTurn?.Status);
        }
        finally
        {
            tempDirectory.Delete(recursive: true);
        }
    }

    [Fact]
    public async Task HandleStopAsyncDoesNotSendPerTurnDuplicateAfterSessionFallback()
    {
        DirectoryInfo tempDirectory = Directory.CreateTempSubdirectory();
        using EnvironmentScope environment = SetTelegramEnvironment();

        try
        {
            WorkspaceStateStore stateStore = new(
                TimeProvider.System,
                NullLogger<WorkspaceStateStore>.Instance);
            RecordingHttpMessageHandler handler = new();
            HookCommandService service = CreateHookCommandService(handler, stateStore);
            const string stopTimestamp = "2026-03-14T15:51:50.783Z";

            _ = await service.HandleStopAsync(
                CreateJsonStream(
                    CreateStopInput(tempDirectory.FullName, stopTimestamp),
                    AppJsonSerializerContext.Default.StopHookInput),
                new MemoryStream(),
                CancellationToken.None);

            NotificationTurn lateCreatedTurn = await CreateTurnAsync(
                stateStore,
                tempDirectory.FullName,
                "session-123",
                "2026-03-14T15:51:40.783Z");

            _ = await service.HandleStopAsync(
                CreateJsonStream(
                    CreateStopInput(tempDirectory.FullName, stopTimestamp),
                    AppJsonSerializerContext.Default.StopHookInput),
                new MemoryStream(),
                CancellationToken.None);

            TelegramSendMessageRequest firstPayload = DeserializeTelegramPayload(
                Assert.Single(handler.Requests));
            Assert.Contains(
                "stop-20260314t155150783z",
                firstPayload.Text,
                StringComparison.Ordinal);

            NotificationTurn? storedLateCreatedTurn = await stateStore.TryReadTurnAsync(
                tempDirectory.FullName,
                "session-123",
                lateCreatedTurn.NotificationTurnId,
                CancellationToken.None);
            Assert.Equal("open", storedLateCreatedTurn?.Status);
        }
        finally
        {
            tempDirectory.Delete(recursive: true);
        }
    }

    [Fact]
    public async Task HandleStopAsyncDoesNotSendDifferentTurnDuplicateAfterPerTurnSend()
    {
        DirectoryInfo tempDirectory = Directory.CreateTempSubdirectory();
        using EnvironmentScope environment = SetTelegramEnvironment();

        try
        {
            WorkspaceStateStore stateStore = new(
                TimeProvider.System,
                NullLogger<WorkspaceStateStore>.Instance);
            RecordingHttpMessageHandler handler = new();
            HookCommandService service = CreateHookCommandService(handler, stateStore);
            const string stopTimestamp = "2026-03-14T15:51:50.783Z";

            NotificationTurn firstTurn = await CreateTurnAsync(
                stateStore,
                tempDirectory.FullName,
                "session-123",
                "2026-03-14T15:51:40.783Z");
            await WriteSummaryAsync(
                tempDirectory.FullName,
                "session-123",
                firstTurn,
                new NotificationSummary
                {
                    SessionId = "session-123",
                    NotificationTurnId = firstTurn.NotificationTurnId,
                    NotificationNonce = firstTurn.NotificationNonce,
                    UpdatedAt = stopTimestamp,
                    Summary = "This first turn was already delivered.",
                });

            _ = await service.HandleStopAsync(
                CreateJsonStream(
                    CreateStopInput(tempDirectory.FullName, stopTimestamp),
                    AppJsonSerializerContext.Default.StopHookInput),
                new MemoryStream(),
                CancellationToken.None);

            NotificationTurn lateCreatedEligibleTurn = await CreateTurnAsync(
                stateStore,
                tempDirectory.FullName,
                "session-123",
                "2026-03-14T15:51:45.783Z");

            _ = await service.HandleStopAsync(
                CreateJsonStream(
                    CreateStopInput(tempDirectory.FullName, stopTimestamp),
                    AppJsonSerializerContext.Default.StopHookInput),
                new MemoryStream(),
                CancellationToken.None);

            Assert.Single(handler.Requests);
            NotificationTurn? storedLateCreatedTurn = await stateStore.TryReadTurnAsync(
                tempDirectory.FullName,
                "session-123",
                lateCreatedEligibleTurn.NotificationTurnId,
                CancellationToken.None);
            Assert.Equal("open", storedLateCreatedTurn?.Status);
        }
        finally
        {
            tempDirectory.Delete(recursive: true);
        }
    }

    [Fact]
    public async Task HandleStopAsyncDoesNotDedupeFallbackAgainstStopObservationOnly()
    {
        DirectoryInfo tempDirectory = Directory.CreateTempSubdirectory();
        using EnvironmentScope environment = SetTelegramEnvironment();

        try
        {
            WorkspaceStateStore stateStore = new(
                TimeProvider.System,
                NullLogger<WorkspaceStateStore>.Instance);
            RecordingHttpMessageHandler handler = new();
            HookCommandService service = CreateHookCommandService(handler, stateStore);
            const string stopTimestamp = "2026-03-14T15:51:50.783Z";
            NotificationTurn firstTurn = await CreateTurnAsync(
                stateStore,
                tempDirectory.FullName,
                "session-123",
                "2026-03-14T15:51:40.783Z");

            _ = await service.HandleStopAsync(
                CreateJsonStream(
                    CreateStopInput(tempDirectory.FullName, stopTimestamp),
                    AppJsonSerializerContext.Default.StopHookInput),
                new MemoryStream(),
                CancellationToken.None);

            string stopsDirectory = Path.Combine(
                AppPaths.GetTurnDirectoryPath(
                    tempDirectory.FullName,
                    "session-123",
                    firstTurn.NotificationTurnId),
                AppConstants.StopsDirectoryName);
            Assert.Single(Directory.EnumerateFiles(stopsDirectory, "*.json"));

            NotificationTurn secondTurn = await CreateTurnAsync(
                stateStore,
                tempDirectory.FullName,
                "session-123",
                "2026-03-14T15:51:45.783Z");
            await WriteSummaryAsync(
                tempDirectory.FullName,
                "session-123",
                secondTurn,
                new NotificationSummary
                {
                    SessionId = "session-123",
                    NotificationTurnId = secondTurn.NotificationTurnId,
                    NotificationNonce = secondTurn.NotificationNonce,
                    UpdatedAt = stopTimestamp,
                    Summary = "The retry should deliver this later turn.",
                });

            _ = await service.HandleStopAsync(
                CreateJsonStream(
                    CreateStopInput(tempDirectory.FullName, stopTimestamp),
                    AppJsonSerializerContext.Default.StopHookInput),
                new MemoryStream(),
                CancellationToken.None);

            TelegramSendMessageRequest payload = DeserializeTelegramPayload(
                Assert.Single(handler.Requests));
            Assert.Contains(
                secondTurn.NotificationTurnId,
                payload.Text,
                StringComparison.Ordinal);
        }
        finally
        {
            tempDirectory.Delete(recursive: true);
        }
    }

    [Fact]
    public async Task HandleStopAsyncInvalidTimestampKeyDoesNotSuppressValidTimestamp()
    {
        DirectoryInfo tempDirectory = Directory.CreateTempSubdirectory();
        using EnvironmentScope environment = SetTelegramEnvironment();

        try
        {
            WorkspaceStateStore stateStore = new(
                TimeProvider.System,
                NullLogger<WorkspaceStateStore>.Instance);
            RecordingHttpMessageHandler handler = new();
            HookCommandService service = CreateHookCommandService(handler, stateStore);
            const string validTimestamp = "2026-03-14T15:51:50.783Z";

            _ = await service.HandleStopAsync(
                CreateJsonStream(
                    CreateStopInput(tempDirectory.FullName, $"{validTimestamp}!!!"),
                    AppJsonSerializerContext.Default.StopHookInput),
                new MemoryStream(),
                CancellationToken.None);
            _ = await service.HandleStopAsync(
                CreateJsonStream(
                    CreateStopInput(tempDirectory.FullName, validTimestamp),
                    AppJsonSerializerContext.Default.StopHookInput),
                new MemoryStream(),
                CancellationToken.None);

            Assert.Equal(2, handler.Requests.Count);
            string notificationsDirectory = Path.Combine(
                AppPaths.GetSessionDirectoryPath(tempDirectory.FullName, "session-123"),
                AppConstants.NotificationsRecordsDirectoryName);
            Assert.Equal(2, Directory.EnumerateFiles(notificationsDirectory, "*.json").Count());
        }
        finally
        {
            tempDirectory.Delete(recursive: true);
        }
    }

    [Fact]
    public async Task HandleStopAsyncSkipsWhenSessionStopClaimAlreadyExists()
    {
        DirectoryInfo tempDirectory = Directory.CreateTempSubdirectory();
        using EnvironmentScope environment = SetTelegramEnvironment();

        try
        {
            WorkspaceStateStore stateStore = new(
                FixedUtcNow(),
                NullLogger<WorkspaceStateStore>.Instance);
            RecordingHttpMessageHandler handler = new();
            HookCommandService service = CreateHookCommandService(handler, stateStore);
            const string stopTimestamp = "2026-03-14T15:51:50.783Z";
            string claimPath = AppPaths.GetSessionStopClaimPath(
                tempDirectory.FullName,
                "session-123",
                CreateStopNotificationKeyForTest(stopTimestamp));
            Assert.True(await WorkspaceStateStore.TryClaimStopNotificationAsync(
                claimPath,
                "2026-03-14T15:51:49.783Z",
                CancellationToken.None));

            _ = await service.HandleStopAsync(
                CreateJsonStream(
                    CreateStopInput(tempDirectory.FullName, stopTimestamp),
                    AppJsonSerializerContext.Default.StopHookInput),
                new MemoryStream(),
                CancellationToken.None);

            Assert.Empty(handler.Requests);
        }
        finally
        {
            tempDirectory.Delete(recursive: true);
        }
    }

    [Fact]
    public async Task HandleStopAsyncReclaimsStaleSessionStopClaimForFallback()
    {
        DirectoryInfo tempDirectory = Directory.CreateTempSubdirectory();
        using EnvironmentScope environment = SetTelegramEnvironment();

        try
        {
            WorkspaceStateStore stateStore = new(
                FixedUtcNow(),
                NullLogger<WorkspaceStateStore>.Instance);
            RecordingHttpMessageHandler handler = new();
            HookCommandService service = CreateHookCommandService(handler, stateStore);
            const string stopTimestamp = "2026-03-14T15:51:50.783Z";
            string notificationKey = CreateStopNotificationKeyForTest(stopTimestamp);
            string claimPath = AppPaths.GetSessionStopClaimPath(
                tempDirectory.FullName,
                "session-123",
                notificationKey);
            await WriteClaimAsync(claimPath, "2026-03-14T15:40:49.783Z");

            _ = await service.HandleStopAsync(
                CreateJsonStream(
                    CreateStopInput(tempDirectory.FullName, stopTimestamp),
                    AppJsonSerializerContext.Default.StopHookInput),
                new MemoryStream(),
                CancellationToken.None);

            TelegramSendMessageRequest payload = DeserializeTelegramPayload(
                Assert.Single(handler.Requests));
            Assert.Contains("摘要：当前轮未生成摘要。", payload.Text, StringComparison.Ordinal);
            Assert.Equal(stopTimestamp, await File.ReadAllTextAsync(claimPath));
            Assert.False(File.Exists(AppPaths.GetSessionStopReclaimClaimPath(
                tempDirectory.FullName,
                "session-123",
                notificationKey)));
        }
        finally
        {
            tempDirectory.Delete(recursive: true);
        }
    }

    [Fact]
    public async Task HandleStopAsyncDoesNotTreatDifferentTimestampAsFreshClaimDuplicate()
    {
        DirectoryInfo tempDirectory = Directory.CreateTempSubdirectory();
        using EnvironmentScope environment = SetTelegramEnvironment();

        try
        {
            WorkspaceStateStore stateStore = new(
                TimeProvider.System,
                NullLogger<WorkspaceStateStore>.Instance);
            BlockingFirstResponseHttpMessageHandler handler = new();
            HookCommandService service = CreateHookCommandService(handler, stateStore);
            const string firstStopTimestamp = "2026-03-14T15:51:50.783Z";
            const string secondStopTimestamp = "2026-03-14T15:51:51.783Z";
            NotificationTurn turn = await CreateTurnAsync(
                stateStore,
                tempDirectory.FullName,
                "session-123",
                "2026-03-14T15:51:40.783Z");
            await WriteSummaryAsync(
                tempDirectory.FullName,
                "session-123",
                turn,
                new NotificationSummary
                {
                    SessionId = "session-123",
                    NotificationTurnId = turn.NotificationTurnId,
                    NotificationNonce = turn.NotificationNonce,
                    UpdatedAt = firstStopTimestamp,
                    Summary = "Only the first Stop may deliver this turn.",
                });

            Task<int> firstStopTask = service.HandleStopAsync(
                CreateJsonStream(
                    CreateStopInput(tempDirectory.FullName, firstStopTimestamp),
                    AppJsonSerializerContext.Default.StopHookInput),
                new MemoryStream(),
                CancellationToken.None);
            await handler.FirstRequestStarted.WaitAsync(TimeSpan.FromSeconds(5));

            _ = await service.HandleStopAsync(
                CreateJsonStream(
                    CreateStopInput(tempDirectory.FullName, secondStopTimestamp),
                    AppJsonSerializerContext.Default.StopHookInput),
                new MemoryStream(),
                CancellationToken.None);

            Assert.Equal(1, handler.RequestCount);
            Assert.False(File.Exists(AppPaths.GetSessionStopClaimPath(
                tempDirectory.FullName,
                "session-123",
                CreateStopNotificationKeyForTest(secondStopTimestamp))));

            handler.AllowFirstResponse();
            Assert.Equal(0, await firstStopTask);
            Assert.Equal(1, handler.RequestCount);
            Assert.True(File.Exists(AppPaths.GetTurnDeliveryClaimPath(
                tempDirectory.FullName,
                "session-123",
                turn.NotificationTurnId)));
        }
        finally
        {
            tempDirectory.Delete(recursive: true);
        }
    }

    [Fact]
    public async Task HandleStopAsyncSkipsFreshTurnDeliveryClaim()
    {
        DirectoryInfo tempDirectory = Directory.CreateTempSubdirectory();
        using EnvironmentScope environment = SetTelegramEnvironment();

        try
        {
            WorkspaceStateStore stateStore = new(
                FixedUtcNow(),
                NullLogger<WorkspaceStateStore>.Instance);
            RecordingHttpMessageHandler handler = new();
            HookCommandService service = CreateHookCommandService(handler, stateStore);
            const string stopTimestamp = "2026-03-14T15:51:50.783Z";
            NotificationTurn turn = await CreateTurnAsync(
                stateStore,
                tempDirectory.FullName,
                "session-123",
                "2026-03-14T15:51:40.783Z");
            await WriteSummaryAsync(
                tempDirectory.FullName,
                "session-123",
                turn,
                new NotificationSummary
                {
                    SessionId = "session-123",
                    NotificationTurnId = turn.NotificationTurnId,
                    NotificationNonce = turn.NotificationNonce,
                    UpdatedAt = stopTimestamp,
                    Summary = "A fresh turn delivery claim must suppress delivery.",
                });

            string turnClaimPath = AppPaths.GetTurnDeliveryClaimPath(
                tempDirectory.FullName,
                "session-123",
                turn.NotificationTurnId);
            await WriteClaimAsync(turnClaimPath, string.Empty);
            File.SetLastWriteTimeUtc(
                turnClaimPath,
                new DateTime(2026, 3, 14, 15, 51, 49, 783, DateTimeKind.Utc));

            _ = await service.HandleStopAsync(
                CreateJsonStream(
                    CreateStopInput(tempDirectory.FullName, stopTimestamp),
                    AppJsonSerializerContext.Default.StopHookInput),
                new MemoryStream(),
                CancellationToken.None);

            Assert.Empty(handler.Requests);
            Assert.Equal(string.Empty, await File.ReadAllTextAsync(turnClaimPath));
        }
        finally
        {
            tempDirectory.Delete(recursive: true);
        }
    }

    [Fact]
    public async Task HandleStopAsyncSkipsFreshCurrentTurnDeliveryClaimWithoutUsingOlderValidTurn()
    {
        DirectoryInfo tempDirectory = Directory.CreateTempSubdirectory();
        using EnvironmentScope environment = SetTelegramEnvironment();

        try
        {
            WorkspaceStateStore stateStore = new(
                FixedUtcNow(),
                NullLogger<WorkspaceStateStore>.Instance);
            RecordingHttpMessageHandler handler = new();
            HookCommandService service = CreateHookCommandService(handler, stateStore);
            NotificationTurn oldTurn = await CreateTurnAsync(
                stateStore,
                tempDirectory.FullName,
                "session-123",
                "2026-03-14T15:51:40.783Z");
            const string currentStopTimestamp = "2026-03-14T15:52:50.783Z";
            await WriteSummaryAsync(
                tempDirectory.FullName,
                "session-123",
                oldTurn,
                new NotificationSummary
                {
                    SessionId = "session-123",
                    NotificationTurnId = oldTurn.NotificationTurnId,
                    NotificationNonce = oldTurn.NotificationNonce,
                    UpdatedAt = "2026-03-14T15:52:49.783Z",
                    Summary = "The older valid turn must not steal a fresh-claimed current Stop.",
                });
            NotificationTurn currentTurn = await CreateTurnAsync(
                stateStore,
                tempDirectory.FullName,
                "session-123",
                "2026-03-14T15:52:40.783Z");
            string currentTurnClaimPath = AppPaths.GetTurnDeliveryClaimPath(
                tempDirectory.FullName,
                "session-123",
                currentTurn.NotificationTurnId);
            await WriteClaimAsync(currentTurnClaimPath, string.Empty);
            File.SetLastWriteTimeUtc(
                currentTurnClaimPath,
                new DateTime(2026, 3, 14, 15, 52, 49, 783, DateTimeKind.Utc));

            _ = await service.HandleStopAsync(
                CreateJsonStream(
                    CreateStopInput(tempDirectory.FullName, currentStopTimestamp),
                    AppJsonSerializerContext.Default.StopHookInput),
                new MemoryStream(),
                CancellationToken.None);

            Assert.Empty(handler.Requests);
            Assert.False(File.Exists(AppPaths.GetNotificationRecordPath(
                tempDirectory.FullName,
                "session-123",
                oldTurn.NotificationTurnId,
                CreateStopNotificationKeyForTest(currentStopTimestamp))));
            Assert.False(File.Exists(AppPaths.GetNotificationRecordPath(
                tempDirectory.FullName,
                "session-123",
                currentTurn.NotificationTurnId,
                CreateStopNotificationKeyForTest(currentStopTimestamp))));
            NotificationTurn? storedCurrentTurn = await stateStore.TryReadTurnAsync(
                tempDirectory.FullName,
                "session-123",
                currentTurn.NotificationTurnId,
                CancellationToken.None);
            Assert.Equal("open", storedCurrentTurn?.Status);
            Assert.Equal(string.Empty, await File.ReadAllTextAsync(currentTurnClaimPath));

            await WriteSummaryAsync(
                tempDirectory.FullName,
                "session-123",
                currentTurn,
                new NotificationSummary
                {
                    SessionId = "session-123",
                    NotificationTurnId = currentTurn.NotificationTurnId,
                    NotificationNonce = currentTurn.NotificationNonce,
                    UpdatedAt = currentStopTimestamp,
                    Summary = "The current turn should deliver after its fresh claim clears.",
                });
            File.Delete(currentTurnClaimPath);

            _ = await service.HandleStopAsync(
                CreateJsonStream(
                    CreateStopInput(tempDirectory.FullName, currentStopTimestamp),
                    AppJsonSerializerContext.Default.StopHookInput),
                new MemoryStream(),
                CancellationToken.None);

            TelegramSendMessageRequest payload = DeserializeTelegramPayload(
                Assert.Single(handler.Requests));
            Assert.Contains(
                "摘要：The current turn should deliver after its fresh claim clears.",
                payload.Text,
                StringComparison.Ordinal);
            Assert.Contains(currentTurn.NotificationTurnId, payload.Text, StringComparison.Ordinal);
            Assert.DoesNotContain(oldTurn.NotificationTurnId, payload.Text, StringComparison.Ordinal);
        }
        finally
        {
            tempDirectory.Delete(recursive: true);
        }
    }

    [Theory]
    [InlineData("empty-object")]
    [InlineData("missing-updated-at")]
    [InlineData("missing")]
    public async Task HandleStopAsyncOlderFreshExactTurnDoesNotSuppressCurrentInvalidFallback(
        string currentSummaryState)
    {
        DirectoryInfo tempDirectory = Directory.CreateTempSubdirectory();
        using EnvironmentScope environment = SetTelegramEnvironment();

        try
        {
            WorkspaceStateStore stateStore = new(
                FixedUtcNow(),
                NullLogger<WorkspaceStateStore>.Instance);
            RecordingHttpMessageHandler handler = new();
            HookCommandService service = CreateHookCommandService(handler, stateStore);
            const string stopTimestamp = "2026-03-14T15:51:50.783Z";
            NotificationTurn oldTurn = await CreateTurnAsync(
                stateStore,
                tempDirectory.FullName,
                "session-123",
                "2026-03-14T15:51:30.783Z");
            await WriteSummaryAsync(
                tempDirectory.FullName,
                "session-123",
                oldTurn,
                new NotificationSummary
                {
                    SessionId = "session-123",
                    NotificationTurnId = oldTurn.NotificationTurnId,
                    NotificationNonce = oldTurn.NotificationNonce,
                    UpdatedAt = stopTimestamp,
                    Summary = "The fresh-claimed older exact turn owns this Stop.",
                });
            NotificationTurn currentTurn = await CreateTurnAsync(
                stateStore,
                tempDirectory.FullName,
                "session-123",
                "2026-03-14T15:51:45.783Z");
            if (!string.Equals(currentSummaryState, "missing", StringComparison.Ordinal))
            {
                await WriteInvalidSummaryAsync(
                    tempDirectory.FullName,
                    "session-123",
                    currentTurn,
                    currentSummaryState);
            }
            oldTurn.Status = "open";
            await WriteTurnStateAsync(tempDirectory.FullName, oldTurn);
            string oldTurnClaimPath = AppPaths.GetTurnDeliveryClaimPath(
                tempDirectory.FullName,
                "session-123",
                oldTurn.NotificationTurnId);
            await WriteClaimAsync(oldTurnClaimPath, string.Empty);
            File.SetLastWriteTimeUtc(
                oldTurnClaimPath,
                new DateTime(2026, 3, 14, 15, 51, 49, 783, DateTimeKind.Utc));

            _ = await service.HandleStopAsync(
                CreateJsonStream(
                    CreateStopInput(tempDirectory.FullName, stopTimestamp),
                    AppJsonSerializerContext.Default.StopHookInput),
                new MemoryStream(),
                CancellationToken.None);

            if (string.Equals(currentSummaryState, "missing", StringComparison.Ordinal))
            {
                Assert.Empty(handler.Requests);
                await AssertPendingStopAsync(
                    stateStore,
                    tempDirectory.FullName,
                    currentTurn,
                    stopTimestamp,
                    "Summary file");
                Assert.Equal(string.Empty, await File.ReadAllTextAsync(oldTurnClaimPath));
                Assert.False(File.Exists(AppPaths.GetNotificationRecordPath(
                    tempDirectory.FullName,
                    "session-123",
                    oldTurn.NotificationTurnId,
                    CreateStopNotificationKeyForTest(stopTimestamp))));
                return;
            }

            TelegramSendMessageRequest payload = DeserializeTelegramPayload(
                Assert.Single(handler.Requests));
            Assert.Contains("摘要：当前轮未生成摘要。", payload.Text, StringComparison.Ordinal);
            Assert.Contains(currentTurn.NotificationTurnId, payload.Text, StringComparison.Ordinal);
            Assert.DoesNotContain(oldTurn.NotificationTurnId, payload.Text, StringComparison.Ordinal);
            Assert.True(File.Exists(AppPaths.GetNotificationRecordPath(
                tempDirectory.FullName,
                "session-123",
                currentTurn.NotificationTurnId,
                CreateStopNotificationKeyForTest(stopTimestamp))));
            Assert.Equal(string.Empty, await File.ReadAllTextAsync(oldTurnClaimPath));
            Assert.False(File.Exists(AppPaths.GetNotificationRecordPath(
                tempDirectory.FullName,
                "session-123",
                oldTurn.NotificationTurnId,
                CreateStopNotificationKeyForTest(stopTimestamp))));
        }
        finally
        {
            tempDirectory.Delete(recursive: true);
        }
    }

    [Fact]
    public async Task HandleStopAsyncCachedCurrentTiedWithFreshClaimSuppressesCurrentDelivery()
    {
        DirectoryInfo tempDirectory = Directory.CreateTempSubdirectory();
        using EnvironmentScope environment = SetTelegramEnvironment();

        try
        {
            WorkspaceStateStore stateStore = new(
                FixedUtcNow(),
                NullLogger<WorkspaceStateStore>.Instance);
            RecordingHttpMessageHandler handler = new();
            HookCommandService service = CreateHookCommandService(handler, stateStore);
            const string stopTimestamp = "2026-03-14T15:52:50.783Z";
            const string tiedCreatedAt = "2026-03-14T15:52:40.783Z";
            NotificationTurn currentTurn = await CreateTurnAsync(
                stateStore,
                tempDirectory.FullName,
                "session-123",
                tiedCreatedAt);
            await WriteSummaryAsync(
                tempDirectory.FullName,
                "session-123",
                currentTurn,
                new NotificationSummary
                {
                    SessionId = "session-123",
                    NotificationTurnId = currentTurn.NotificationTurnId,
                    NotificationNonce = currentTurn.NotificationNonce,
                    UpdatedAt = "2026-03-14T15:52:49.783Z",
                    Summary = "The tied current summary must wait behind the fresh claim.",
                });
            NotificationTurn tiedFreshClaimedTurn = await CreateTurnAsync(
                stateStore,
                tempDirectory.FullName,
                "session-123",
                tiedCreatedAt);
            tiedFreshClaimedTurn.Status = "open";
            await WriteTurnStateAsync(tempDirectory.FullName, tiedFreshClaimedTurn);
            string tiedClaimPath = AppPaths.GetTurnDeliveryClaimPath(
                tempDirectory.FullName,
                "session-123",
                tiedFreshClaimedTurn.NotificationTurnId);
            await WriteClaimAsync(tiedClaimPath, string.Empty);
            File.SetLastWriteTimeUtc(
                tiedClaimPath,
                new DateTime(2026, 3, 14, 15, 52, 49, 783, DateTimeKind.Utc));
            await File.WriteAllTextAsync(
                AppPaths.GetCurrentStatePath(tempDirectory.FullName, "session-123"),
                JsonSerializer.Serialize(
                    new CurrentNotificationState
                    {
                        SessionId = "session-123",
                        NotificationTurnId = currentTurn.NotificationTurnId,
                        NotificationNonce = currentTurn.NotificationNonce,
                        SummaryPath = AppPaths.GetSummaryStatePath(
                            tempDirectory.FullName,
                            "session-123",
                            currentTurn.NotificationTurnId),
                        UpdatedAt = stopTimestamp,
                    },
                    AppJsonSerializerContext.Default.CurrentNotificationState));

            _ = await service.HandleStopAsync(
                CreateJsonStream(
                    CreateStopInput(tempDirectory.FullName, stopTimestamp),
                    AppJsonSerializerContext.Default.StopHookInput),
                new MemoryStream(),
                CancellationToken.None);

            Assert.Empty(handler.Requests);
            Assert.False(File.Exists(AppPaths.GetNotificationRecordPath(
                tempDirectory.FullName,
                "session-123",
                currentTurn.NotificationTurnId,
                CreateStopNotificationKeyForTest(stopTimestamp))));
            Assert.True(File.Exists(tiedClaimPath));
        }
        finally
        {
            tempDirectory.Delete(recursive: true);
        }
    }

    [Fact]
    public async Task HandleStopAsyncStaleCurrentMiddleFreshExactDoesNotSuppressNewerDurableDelivery()
    {
        DirectoryInfo tempDirectory = Directory.CreateTempSubdirectory();
        using EnvironmentScope environment = SetTelegramEnvironment();

        try
        {
            WorkspaceStateStore stateStore = new(
                FixedUtcNow(),
                NullLogger<WorkspaceStateStore>.Instance);
            RecordingHttpMessageHandler handler = new();
            HookCommandService service = CreateHookCommandService(handler, stateStore);
            const string stopTimestamp = "2026-03-14T15:52:50.783Z";
            NotificationTurn staleCachedTurn = await CreateTurnAsync(
                stateStore,
                tempDirectory.FullName,
                "session-123",
                "2026-03-14T15:51:30.783Z");
            string staleCurrentJson = await File.ReadAllTextAsync(AppPaths.GetCurrentStatePath(
                tempDirectory.FullName,
                "session-123"));
            await WriteSummaryAsync(
                tempDirectory.FullName,
                "session-123",
                staleCachedTurn,
                new NotificationSummary
                {
                    SessionId = "session-123",
                    NotificationTurnId = staleCachedTurn.NotificationTurnId,
                    NotificationNonce = staleCachedTurn.NotificationNonce,
                    UpdatedAt = "2026-03-14T15:52:40.783Z",
                    Summary = "The stale current summary must not drive suppression.",
                });
            NotificationTurn middleFreshExactTurn = await CreateTurnAsync(
                stateStore,
                tempDirectory.FullName,
                "session-123",
                "2026-03-14T15:52:00.783Z");
            await WriteSummaryAsync(
                tempDirectory.FullName,
                "session-123",
                middleFreshExactTurn,
                new NotificationSummary
                {
                    SessionId = "session-123",
                    NotificationTurnId = middleFreshExactTurn.NotificationTurnId,
                    NotificationNonce = middleFreshExactTurn.NotificationNonce,
                    UpdatedAt = stopTimestamp,
                    Summary = "The middle exact summary already has a fresh delivery claim.",
                });
            string middleClaimPath = AppPaths.GetTurnDeliveryClaimPath(
                tempDirectory.FullName,
                "session-123",
                middleFreshExactTurn.NotificationTurnId);
            await WriteClaimAsync(middleClaimPath, string.Empty);
            File.SetLastWriteTimeUtc(
                middleClaimPath,
                new DateTime(2026, 3, 14, 15, 52, 49, 783, DateTimeKind.Utc));
            NotificationTurn newerTurn = await CreateTurnAsync(
                stateStore,
                tempDirectory.FullName,
                "session-123",
                "2026-03-14T15:52:40.783Z");
            await WriteSummaryAsync(
                tempDirectory.FullName,
                "session-123",
                newerTurn,
                new NotificationSummary
                {
                    SessionId = "session-123",
                    NotificationTurnId = newerTurn.NotificationTurnId,
                    NotificationNonce = newerTurn.NotificationNonce,
                    UpdatedAt = "2026-03-14T15:52:49.783Z",
                    Summary = "The newer durable summary must not wait behind the middle fresh claim.",
                });
            await File.WriteAllTextAsync(
                AppPaths.GetCurrentStatePath(tempDirectory.FullName, "session-123"),
                staleCurrentJson);

            _ = await service.HandleStopAsync(
                CreateJsonStream(
                    CreateStopInput(tempDirectory.FullName, stopTimestamp),
                    AppJsonSerializerContext.Default.StopHookInput),
                new MemoryStream(),
                CancellationToken.None);

            TelegramSendMessageRequest payload = DeserializeTelegramPayload(
                Assert.Single(handler.Requests));
            Assert.Contains(
                "摘要：The newer durable summary must not wait behind the middle fresh claim.",
                payload.Text,
                StringComparison.Ordinal);
            Assert.Contains(newerTurn.NotificationTurnId, payload.Text, StringComparison.Ordinal);
            Assert.DoesNotContain(middleFreshExactTurn.NotificationTurnId, payload.Text, StringComparison.Ordinal);
        }
        finally
        {
            tempDirectory.Delete(recursive: true);
        }
    }

    [Fact]
    public async Task HandleStopAsyncSkipsFreshCurrentExactAttributionWithoutDeliveringOlderExactTurn()
    {
        DirectoryInfo tempDirectory = Directory.CreateTempSubdirectory();
        using EnvironmentScope environment = SetTelegramEnvironment();

        try
        {
            WorkspaceStateStore stateStore = new(
                FixedUtcNow(),
                NullLogger<WorkspaceStateStore>.Instance);
            RecordingHttpMessageHandler handler = new();
            HookCommandService service = CreateHookCommandService(handler, stateStore);
            NotificationTurn oldTurn = await CreateTurnAsync(
                stateStore,
                tempDirectory.FullName,
                "session-123",
                "2026-03-14T15:51:40.783Z");
            const string stopTimestamp = "2026-03-14T15:52:50.783Z";
            await WriteSummaryAsync(
                tempDirectory.FullName,
                "session-123",
                oldTurn,
                new NotificationSummary
                {
                    SessionId = "session-123",
                    NotificationTurnId = oldTurn.NotificationTurnId,
                    NotificationNonce = oldTurn.NotificationNonce,
                    UpdatedAt = stopTimestamp,
                    Summary = "The older exact summary must wait while current is freshly claimed.",
                });
            NotificationTurn currentTurn = await CreateTurnAsync(
                stateStore,
                tempDirectory.FullName,
                "session-123",
                "2026-03-14T15:52:40.783Z");
            await WriteSummaryAsync(
                tempDirectory.FullName,
                "session-123",
                currentTurn,
                new NotificationSummary
                {
                    SessionId = "session-123",
                    NotificationTurnId = currentTurn.NotificationTurnId,
                    NotificationNonce = currentTurn.NotificationNonce,
                    UpdatedAt = stopTimestamp,
                    Summary = "The current exact summary owns the fresh-claimed Stop.",
                });
            string currentTurnClaimPath = AppPaths.GetTurnDeliveryClaimPath(
                tempDirectory.FullName,
                "session-123",
                currentTurn.NotificationTurnId);
            await WriteClaimAsync(currentTurnClaimPath, string.Empty);
            File.SetLastWriteTimeUtc(
                currentTurnClaimPath,
                new DateTime(2026, 3, 14, 15, 52, 49, 783, DateTimeKind.Utc));

            _ = await service.HandleStopAsync(
                CreateJsonStream(
                    CreateStopInput(tempDirectory.FullName, stopTimestamp),
                    AppJsonSerializerContext.Default.StopHookInput),
                new MemoryStream(),
                CancellationToken.None);

            Assert.Empty(handler.Requests);
            Assert.False(File.Exists(AppPaths.GetNotificationRecordPath(
                tempDirectory.FullName,
                "session-123",
                oldTurn.NotificationTurnId,
                CreateStopNotificationKeyForTest(stopTimestamp))));
            Assert.False(File.Exists(AppPaths.GetNotificationRecordPath(
                tempDirectory.FullName,
                "session-123",
                currentTurn.NotificationTurnId,
                CreateStopNotificationKeyForTest(stopTimestamp))));
            NotificationTurn? storedOldTurn = await stateStore.TryReadTurnAsync(
                tempDirectory.FullName,
                "session-123",
                oldTurn.NotificationTurnId,
                CancellationToken.None);
            Assert.Equal("open", storedOldTurn?.Status);
            NotificationTurn? storedCurrentTurn = await stateStore.TryReadTurnAsync(
                tempDirectory.FullName,
                "session-123",
                currentTurn.NotificationTurnId,
                CancellationToken.None);
            Assert.Equal("open", storedCurrentTurn?.Status);
            Assert.Equal(string.Empty, await File.ReadAllTextAsync(currentTurnClaimPath));

            File.Delete(currentTurnClaimPath);

            _ = await service.HandleStopAsync(
                CreateJsonStream(
                    CreateStopInput(tempDirectory.FullName, stopTimestamp),
                    AppJsonSerializerContext.Default.StopHookInput),
                new MemoryStream(),
                CancellationToken.None);

            TelegramSendMessageRequest payload = DeserializeTelegramPayload(
                Assert.Single(handler.Requests));
            Assert.Contains(
                "摘要：The current exact summary owns the fresh-claimed Stop.",
                payload.Text,
                StringComparison.Ordinal);
            Assert.Contains(currentTurn.NotificationTurnId, payload.Text, StringComparison.Ordinal);
            Assert.DoesNotContain(oldTurn.NotificationTurnId, payload.Text, StringComparison.Ordinal);
        }
        finally
        {
            tempDirectory.Delete(recursive: true);
        }
    }

    [Fact]
    public async Task HandleStopAsyncPriorNonExactDurableDeliverySuppressesRetryFallback()
    {
        DirectoryInfo tempDirectory = Directory.CreateTempSubdirectory();
        using EnvironmentScope environment = SetTelegramEnvironment();

        try
        {
            WorkspaceStateStore stateStore = new(
                FixedUtcNow(),
                NullLogger<WorkspaceStateStore>.Instance);
            BlockingFirstResponseHttpMessageHandler handler = new();
            HookCommandService service = CreateHookCommandService(handler, stateStore);
            const string firstStopTimestamp = "2026-03-14T15:51:50.783Z";
            const string secondStopTimestamp = "2026-03-14T15:51:51.783Z";
            NotificationTurn turn = await CreateTurnAsync(
                stateStore,
                tempDirectory.FullName,
                "session-123",
                "2026-03-14T15:51:40.783Z");
            await WriteSummaryAsync(
                tempDirectory.FullName,
                "session-123",
                turn,
                new NotificationSummary
                {
                    SessionId = "session-123",
                    NotificationTurnId = turn.NotificationTurnId,
                    NotificationNonce = turn.NotificationNonce,
                    UpdatedAt = firstStopTimestamp,
                    Summary = "The first Stop non-exact delivery suppresses later fallback.",
                });

            Task<int> firstStopTask = service.HandleStopAsync(
                CreateJsonStream(
                    CreateStopInput(tempDirectory.FullName, firstStopTimestamp),
                    AppJsonSerializerContext.Default.StopHookInput),
                new MemoryStream(),
                CancellationToken.None);
            await handler.FirstRequestStarted.WaitAsync(TimeSpan.FromSeconds(5));

            _ = await service.HandleStopAsync(
                CreateJsonStream(
                    CreateStopInput(tempDirectory.FullName, secondStopTimestamp),
                    AppJsonSerializerContext.Default.StopHookInput),
                new MemoryStream(),
                CancellationToken.None);

            handler.AllowFirstResponse();
            Assert.Equal(0, await firstStopTask);
            Assert.Equal(1, handler.RequestCount);
            Assert.True(File.Exists(AppPaths.GetNotificationRecordPath(
                tempDirectory.FullName,
                "session-123",
                turn.NotificationTurnId,
                CreateStopNotificationKeyForTest(firstStopTimestamp))));

            _ = await service.HandleStopAsync(
                CreateJsonStream(
                    CreateStopInput(tempDirectory.FullName, secondStopTimestamp),
                    AppJsonSerializerContext.Default.StopHookInput),
                new MemoryStream(),
                CancellationToken.None);

            Assert.Equal(1, handler.RequestCount);
            Assert.False(File.Exists(AppPaths.GetSessionNotificationRecordPath(
                tempDirectory.FullName,
                "session-123",
                CreateStopNotificationKeyForTest(secondStopTimestamp))));
        }
        finally
        {
            tempDirectory.Delete(recursive: true);
        }
    }

    [Fact]
    public async Task HandleStopAsyncPriorNonExactPerTurnRecordSuppressesRetryFallback()
    {
        DirectoryInfo tempDirectory = Directory.CreateTempSubdirectory();
        using EnvironmentScope environment = SetTelegramEnvironment();

        try
        {
            WorkspaceStateStore stateStore = new(
                FixedUtcNow(),
                NullLogger<WorkspaceStateStore>.Instance);
            RecordingHttpMessageHandler handler = new();
            HookCommandService service = CreateHookCommandService(handler, stateStore);
            const string firstStopTimestamp = "2026-03-14T15:51:50.783Z";
            const string secondStopTimestamp = "2026-03-14T15:51:51.783Z";
            NotificationTurn turn = await CreateTurnAsync(
                stateStore,
                tempDirectory.FullName,
                "session-123",
                "2026-03-14T15:51:40.783Z");
            turn.Status = "notified";
            await WriteTurnStateAsync(tempDirectory.FullName, turn);
            await WorkspaceStateStore.RecordNotificationAsync(
                AppPaths.GetNotificationRecordPath(
                    tempDirectory.FullName,
                    "session-123",
                    turn.NotificationTurnId,
                    CreateStopNotificationKeyForTest(firstStopTimestamp)),
                new NotificationRecord
                {
                    SessionId = "session-123",
                    NotificationTurnId = turn.NotificationTurnId,
                    NotificationKey = CreateStopNotificationKeyForTest(firstStopTimestamp),
                    WorkspacePath = Path.GetFullPath(tempDirectory.FullName),
                    StopTimestamp = firstStopTimestamp,
                    SentAt = firstStopTimestamp,
                    SummaryUpdatedAt = firstStopTimestamp,
                    Degraded = false,
                    DeliveryStatus = "sent",
                },
                CancellationToken.None);

            _ = await service.HandleStopAsync(
                CreateJsonStream(
                    CreateStopInput(tempDirectory.FullName, secondStopTimestamp),
                    AppJsonSerializerContext.Default.StopHookInput),
                new MemoryStream(),
                CancellationToken.None);

            Assert.Empty(handler.Requests);
            Assert.False(File.Exists(AppPaths.GetSessionNotificationRecordPath(
                tempDirectory.FullName,
                "session-123",
                CreateStopNotificationKeyForTest(secondStopTimestamp))));
        }
        finally
        {
            tempDirectory.Delete(recursive: true);
        }
    }

    [Fact]
    public async Task HandleStopAsyncNotifiedExactRetrySendsOnceThenSuppressesReplay()
    {
        DirectoryInfo tempDirectory = Directory.CreateTempSubdirectory();
        using EnvironmentScope environment = SetTelegramEnvironment();

        try
        {
            WorkspaceStateStore stateStore = new(
                FixedUtcNow(),
                NullLogger<WorkspaceStateStore>.Instance);
            RecordingHttpMessageHandler handler = new();
            HookCommandService service = CreateHookCommandService(handler, stateStore);
            const string laterStopTimestamp = "2026-03-14T15:51:51.783Z";
            NotificationTurn turn = await CreateTurnAsync(
                stateStore,
                tempDirectory.FullName,
                "session-123",
                "2026-03-14T15:51:40.783Z");
            await WriteSummaryAsync(
                tempDirectory.FullName,
                "session-123",
                turn,
                new NotificationSummary
                {
                    SessionId = "session-123",
                    NotificationTurnId = turn.NotificationTurnId,
                    NotificationNonce = turn.NotificationNonce,
                    UpdatedAt = laterStopTimestamp,
                    Summary = "The exact later Stop retries from the notified turn.",
                });
            turn.Status = "notified";
            await WriteTurnStateAsync(tempDirectory.FullName, turn);

            _ = await service.HandleStopAsync(
                CreateJsonStream(
                    CreateStopInput(tempDirectory.FullName, laterStopTimestamp),
                    AppJsonSerializerContext.Default.StopHookInput),
                new MemoryStream(),
                CancellationToken.None);

            TelegramSendMessageRequest payload = DeserializeTelegramPayload(
                Assert.Single(handler.Requests));
            Assert.Contains(
                "摘要：The exact later Stop retries from the notified turn.",
                payload.Text,
                StringComparison.Ordinal);
            Assert.Contains(turn.NotificationTurnId, payload.Text, StringComparison.Ordinal);
            string laterNotificationPath = AppPaths.GetNotificationRecordPath(
                tempDirectory.FullName,
                "session-123",
                turn.NotificationTurnId,
                CreateStopNotificationKeyForTest(laterStopTimestamp));
            NotificationRecord laterRecord = await ReadNotificationRecordAsync(laterNotificationPath);
            Assert.Equal(turn.NotificationTurnId, laterRecord.NotificationTurnId);
            Assert.Equal(laterStopTimestamp, laterRecord.StopTimestamp);
            Assert.Equal("sent", laterRecord.DeliveryStatus);

            _ = await service.HandleStopAsync(
                CreateJsonStream(
                    CreateStopInput(tempDirectory.FullName, laterStopTimestamp),
                    AppJsonSerializerContext.Default.StopHookInput),
                new MemoryStream(),
                CancellationToken.None);

            Assert.Single(handler.Requests);
        }
        finally
        {
            tempDirectory.Delete(recursive: true);
        }
    }

    [Fact]
    public async Task HandleStopAsyncPriorDurableDeliverySuppressesLaterExactRetryOnNotifiedTurn()
    {
        DirectoryInfo tempDirectory = Directory.CreateTempSubdirectory();
        using EnvironmentScope environment = SetTelegramEnvironment();

        try
        {
            WorkspaceStateStore stateStore = new(
                FixedUtcNow(),
                NullLogger<WorkspaceStateStore>.Instance);
            RecordingHttpMessageHandler handler = new();
            HookCommandService service = CreateHookCommandService(handler, stateStore);
            const string earlierStopTimestamp = "2026-03-14T15:51:50.783Z";
            const string laterStopTimestamp = "2026-03-14T15:51:51.783Z";
            NotificationTurn turn = await CreateTurnAsync(
                stateStore,
                tempDirectory.FullName,
                "session-123",
                "2026-03-14T15:51:40.783Z");
            await WriteSummaryAsync(
                tempDirectory.FullName,
                "session-123",
                turn,
                new NotificationSummary
                {
                    SessionId = "session-123",
                    NotificationTurnId = turn.NotificationTurnId,
                    NotificationNonce = turn.NotificationNonce,
                    UpdatedAt = laterStopTimestamp,
                    Summary = "The exact later Stop retries after the earlier delivery completes.",
                });
            string turnClaimPath = AppPaths.GetTurnDeliveryClaimPath(
                tempDirectory.FullName,
                "session-123",
                turn.NotificationTurnId);
            await WriteClaimAsync(turnClaimPath, earlierStopTimestamp);
            File.SetLastWriteTimeUtc(
                turnClaimPath,
                new DateTime(2026, 3, 14, 15, 51, 50, 783, DateTimeKind.Utc));

            _ = await service.HandleStopAsync(
                CreateJsonStream(
                    CreateStopInput(tempDirectory.FullName, laterStopTimestamp),
                    AppJsonSerializerContext.Default.StopHookInput),
                new MemoryStream(),
                CancellationToken.None);

            Assert.Empty(handler.Requests);
            File.Delete(turnClaimPath);
            turn.Status = "notified";
            await WriteTurnStateAsync(tempDirectory.FullName, turn);
            await WorkspaceStateStore.RecordNotificationAsync(
                AppPaths.GetNotificationRecordPath(
                    tempDirectory.FullName,
                    "session-123",
                    turn.NotificationTurnId,
                    CreateStopNotificationKeyForTest(earlierStopTimestamp)),
                new NotificationRecord
                {
                    SessionId = "session-123",
                    NotificationTurnId = turn.NotificationTurnId,
                    NotificationKey = CreateStopNotificationKeyForTest(earlierStopTimestamp),
                    WorkspacePath = Path.GetFullPath(tempDirectory.FullName),
                    StopTimestamp = earlierStopTimestamp,
                    SentAt = earlierStopTimestamp,
                    SummaryUpdatedAt = earlierStopTimestamp,
                    Degraded = false,
                    DeliveryStatus = "sent",
                },
                CancellationToken.None);

            _ = await service.HandleStopAsync(
                CreateJsonStream(
                    CreateStopInput(tempDirectory.FullName, laterStopTimestamp),
                    AppJsonSerializerContext.Default.StopHookInput),
                new MemoryStream(),
                CancellationToken.None);

            Assert.Empty(handler.Requests);
            Assert.False(File.Exists(AppPaths.GetNotificationRecordPath(
                tempDirectory.FullName,
                "session-123",
                turn.NotificationTurnId,
                CreateStopNotificationKeyForTest(laterStopTimestamp))));
        }
        finally
        {
            tempDirectory.Delete(recursive: true);
        }
    }

    [Fact]
    public async Task HandleStopAsyncOpenExactSelectionSkipsWhenFinalRereadLosesAttribution()
    {
        DirectoryInfo tempDirectory = Directory.CreateTempSubdirectory();
        using EnvironmentScope environment = SetTelegramEnvironment();

        try
        {
            const string earlierStopTimestamp = "2026-03-14T15:51:50.783Z";
            const string laterStopTimestamp = "2026-03-14T15:51:51.783Z";
            NotificationTurn? turnToRewrite = null;
            RewriteOnNextUtcNowTimeProvider timeProvider = new(
                new DateTimeOffset(2026, 3, 14, 15, 51, 51, 783, TimeSpan.Zero),
                () =>
                {
                    if (turnToRewrite is null)
                    {
                        return;
                    }

                    string summaryPath = AppPaths.GetSummaryStatePath(
                        tempDirectory.FullName,
                        "session-123",
                        turnToRewrite.NotificationTurnId);
                    File.WriteAllText(
                        summaryPath,
                        JsonSerializer.Serialize(
                            new NotificationSummary
                            {
                                SessionId = "session-123",
                                NotificationTurnId = turnToRewrite.NotificationTurnId,
                                NotificationNonce = turnToRewrite.NotificationNonce,
                                UpdatedAt = earlierStopTimestamp,
                                Summary = "The final reread no longer belongs to the selected Stop.",
                            },
                            AppJsonSerializerContext.Default.NotificationSummary));
                });
            WorkspaceStateStore stateStore = new(
                timeProvider,
                NullLogger<WorkspaceStateStore>.Instance);
            RecordingHttpMessageHandler handler = new();
            HookCommandService service = CreateHookCommandService(handler, stateStore);
            NotificationTurn turn = await CreateTurnAsync(
                stateStore,
                tempDirectory.FullName,
                "session-123",
                "2026-03-14T15:51:40.783Z");
            await WriteSummaryAsync(
                tempDirectory.FullName,
                "session-123",
                turn,
                new NotificationSummary
                {
                    SessionId = "session-123",
                    NotificationTurnId = turn.NotificationTurnId,
                    NotificationNonce = turn.NotificationNonce,
                    UpdatedAt = laterStopTimestamp,
                    Summary = "The initially exact Stop must be revalidated before open-turn delivery.",
                });
            turnToRewrite = turn;
            timeProvider.Arm(2);

            _ = await service.HandleStopAsync(
                CreateJsonStream(
                    CreateStopInput(tempDirectory.FullName, laterStopTimestamp),
                    AppJsonSerializerContext.Default.StopHookInput),
                new MemoryStream(),
                CancellationToken.None);

            Assert.Empty(handler.Requests);
            Assert.False(File.Exists(AppPaths.GetNotificationRecordPath(
                tempDirectory.FullName,
                "session-123",
                turn.NotificationTurnId,
                CreateStopNotificationKeyForTest(laterStopTimestamp))));
            Assert.False(File.Exists(AppPaths.GetSessionNotificationRecordPath(
                tempDirectory.FullName,
                "session-123",
                CreateStopNotificationKeyForTest(laterStopTimestamp))));
            Assert.False(File.Exists(AppPaths.GetStopObservationPath(
                tempDirectory.FullName,
                "session-123",
                turn.NotificationTurnId,
                CreateStopNotificationKeyForTest(laterStopTimestamp))));
        }
        finally
        {
            tempDirectory.Delete(recursive: true);
        }
    }

    [Fact]
    public async Task HandleStopAsyncNotifiedExactRetrySkipsWhenFinalRereadLosesAttribution()
    {
        DirectoryInfo tempDirectory = Directory.CreateTempSubdirectory();
        using EnvironmentScope environment = SetTelegramEnvironment();

        try
        {
            const string earlierStopTimestamp = "2026-03-14T15:51:50.783Z";
            const string laterStopTimestamp = "2026-03-14T15:51:51.783Z";
            NotificationTurn? turnToRewrite = null;
            RewriteOnNextUtcNowTimeProvider timeProvider = new(
                new DateTimeOffset(2026, 3, 14, 15, 51, 50, 783, TimeSpan.Zero),
                () =>
                {
                    if (turnToRewrite is null)
                    {
                        return;
                    }

                    string summaryPath = AppPaths.GetSummaryStatePath(
                        tempDirectory.FullName,
                        "session-123",
                        turnToRewrite.NotificationTurnId);
                    File.WriteAllText(
                        summaryPath,
                        JsonSerializer.Serialize(
                            new NotificationSummary
                            {
                                SessionId = "session-123",
                                NotificationTurnId = turnToRewrite.NotificationTurnId,
                                NotificationNonce = turnToRewrite.NotificationNonce,
                                UpdatedAt = earlierStopTimestamp,
                                Summary = "The final reread no longer belongs to the later Stop.",
                            },
                            AppJsonSerializerContext.Default.NotificationSummary));
                });
            WorkspaceStateStore stateStore = new(
                timeProvider,
                NullLogger<WorkspaceStateStore>.Instance);
            RecordingHttpMessageHandler handler = new();
            HookCommandService service = CreateHookCommandService(handler, stateStore);
            NotificationTurn turn = await CreateTurnAsync(
                stateStore,
                tempDirectory.FullName,
                "session-123",
                "2026-03-14T15:51:40.783Z");
            await WriteSummaryAsync(
                tempDirectory.FullName,
                "session-123",
                turn,
                new NotificationSummary
                {
                    SessionId = "session-123",
                    NotificationTurnId = turn.NotificationTurnId,
                    NotificationNonce = turn.NotificationNonce,
                    UpdatedAt = laterStopTimestamp,
                    Summary = "The initially exact later Stop must be revalidated before retry delivery.",
                });
            turn.Status = "notified";
            await WriteTurnStateAsync(tempDirectory.FullName, turn);
            await WorkspaceStateStore.RecordNotificationAsync(
                AppPaths.GetNotificationRecordPath(
                    tempDirectory.FullName,
                    "session-123",
                    turn.NotificationTurnId,
                    CreateStopNotificationKeyForTest(earlierStopTimestamp)),
                new NotificationRecord
                {
                    SessionId = "session-123",
                    NotificationTurnId = turn.NotificationTurnId,
                    NotificationKey = CreateStopNotificationKeyForTest(earlierStopTimestamp),
                    WorkspacePath = Path.GetFullPath(tempDirectory.FullName),
                    StopTimestamp = earlierStopTimestamp,
                    SentAt = earlierStopTimestamp,
                    SummaryUpdatedAt = earlierStopTimestamp,
                    Degraded = false,
                    DeliveryStatus = "sent",
                },
                CancellationToken.None);
            turnToRewrite = turn;
            timeProvider.Arm(2);

            _ = await service.HandleStopAsync(
                CreateJsonStream(
                    CreateStopInput(tempDirectory.FullName, laterStopTimestamp),
                    AppJsonSerializerContext.Default.StopHookInput),
                new MemoryStream(),
                CancellationToken.None);

            Assert.Empty(handler.Requests);
            Assert.False(File.Exists(AppPaths.GetNotificationRecordPath(
                tempDirectory.FullName,
                "session-123",
                turn.NotificationTurnId,
                CreateStopNotificationKeyForTest(laterStopTimestamp))));
            Assert.False(File.Exists(AppPaths.GetSessionNotificationRecordPath(
                tempDirectory.FullName,
                "session-123",
                CreateStopNotificationKeyForTest(laterStopTimestamp))));
            Assert.False(File.Exists(AppPaths.GetStopObservationPath(
                tempDirectory.FullName,
                "session-123",
                turn.NotificationTurnId,
                CreateStopNotificationKeyForTest(laterStopTimestamp))));
        }
        finally
        {
            tempDirectory.Delete(recursive: true);
        }
    }

    [Fact]
    public async Task HandleStopAsyncPersistentEarlierClaimAndDurableRecordSuppressLaterExactRetryOnNotifiedTurn()
    {
        DirectoryInfo tempDirectory = Directory.CreateTempSubdirectory();
        using EnvironmentScope environment = SetTelegramEnvironment();

        try
        {
            WorkspaceStateStore stateStore = new(
                FixedUtcNow(),
                NullLogger<WorkspaceStateStore>.Instance);
            RecordingHttpMessageHandler handler = new();
            HookCommandService service = CreateHookCommandService(handler, stateStore);
            const string earlierStopTimestamp = "2026-03-14T15:51:50.783Z";
            const string laterStopTimestamp = "2026-03-14T15:51:51.783Z";
            NotificationTurn turn = await CreateTurnAsync(
                stateStore,
                tempDirectory.FullName,
                "session-123",
                "2026-03-14T15:51:40.783Z");
            await WriteSummaryAsync(
                tempDirectory.FullName,
                "session-123",
                turn,
                new NotificationSummary
                {
                    SessionId = "session-123",
                    NotificationTurnId = turn.NotificationTurnId,
                    NotificationNonce = turn.NotificationNonce,
                    UpdatedAt = laterStopTimestamp,
                    Summary = "The exact later Stop retries even while the earlier claim persists.",
                });
            string turnClaimPath = AppPaths.GetTurnDeliveryClaimPath(
                tempDirectory.FullName,
                "session-123",
                turn.NotificationTurnId);
            await WriteClaimAsync(turnClaimPath, earlierStopTimestamp);
            File.SetLastWriteTimeUtc(
                turnClaimPath,
                new DateTime(2026, 3, 14, 15, 51, 50, 783, DateTimeKind.Utc));
            turn.Status = "notified";
            await WriteTurnStateAsync(tempDirectory.FullName, turn);
            await WorkspaceStateStore.RecordNotificationAsync(
                AppPaths.GetNotificationRecordPath(
                    tempDirectory.FullName,
                    "session-123",
                    turn.NotificationTurnId,
                    CreateStopNotificationKeyForTest(earlierStopTimestamp)),
                new NotificationRecord
                {
                    SessionId = "session-123",
                    NotificationTurnId = turn.NotificationTurnId,
                    NotificationKey = CreateStopNotificationKeyForTest(earlierStopTimestamp),
                    WorkspacePath = Path.GetFullPath(tempDirectory.FullName),
                    StopTimestamp = earlierStopTimestamp,
                    SentAt = earlierStopTimestamp,
                    SummaryUpdatedAt = earlierStopTimestamp,
                    Degraded = false,
                    DeliveryStatus = "sent",
                },
                CancellationToken.None);

            _ = await service.HandleStopAsync(
                CreateJsonStream(
                    CreateStopInput(tempDirectory.FullName, laterStopTimestamp),
                    AppJsonSerializerContext.Default.StopHookInput),
                new MemoryStream(),
                CancellationToken.None);

            Assert.Empty(handler.Requests);
            Assert.True(File.Exists(turnClaimPath));
            Assert.Equal(earlierStopTimestamp, await File.ReadAllTextAsync(turnClaimPath));
            Assert.False(File.Exists(AppPaths.GetNotificationRecordPath(
                tempDirectory.FullName,
                "session-123",
                turn.NotificationTurnId,
                CreateStopNotificationKeyForTest(laterStopTimestamp))));
        }
        finally
        {
            tempDirectory.Delete(recursive: true);
        }
    }

    [Fact]
    public async Task HandleStopAsyncFreshCurrentClaimDoesNotLoseLaterExactStop()
    {
        DirectoryInfo tempDirectory = Directory.CreateTempSubdirectory();
        using EnvironmentScope environment = SetTelegramEnvironment();

        try
        {
            WorkspaceStateStore stateStore = new(
                FixedUtcNow(),
                NullLogger<WorkspaceStateStore>.Instance);
            RecordingHttpMessageHandler handler = new();
            HookCommandService service = CreateHookCommandService(handler, stateStore);
            const string earlierStopTimestamp = "2026-03-14T15:51:50.783Z";
            const string laterStopTimestamp = "2026-03-14T15:51:51.783Z";
            NotificationTurn turn = await CreateTurnAsync(
                stateStore,
                tempDirectory.FullName,
                "session-123",
                "2026-03-14T15:51:40.783Z");
            await WriteSummaryAsync(
                tempDirectory.FullName,
                "session-123",
                turn,
                new NotificationSummary
                {
                    SessionId = "session-123",
                    NotificationTurnId = turn.NotificationTurnId,
                    NotificationNonce = turn.NotificationNonce,
                    UpdatedAt = laterStopTimestamp,
                    Summary = "The later exact Stop should retry after the earlier claim clears.",
                });
            string turnClaimPath = AppPaths.GetTurnDeliveryClaimPath(
                tempDirectory.FullName,
                "session-123",
                turn.NotificationTurnId);
            await WriteClaimAsync(turnClaimPath, earlierStopTimestamp);
            File.SetLastWriteTimeUtc(
                turnClaimPath,
                new DateTime(2026, 3, 14, 15, 51, 50, 783, DateTimeKind.Utc));

            _ = await service.HandleStopAsync(
                CreateJsonStream(
                    CreateStopInput(tempDirectory.FullName, laterStopTimestamp),
                    AppJsonSerializerContext.Default.StopHookInput),
                new MemoryStream(),
                CancellationToken.None);

            Assert.Empty(handler.Requests);
            Assert.False(File.Exists(AppPaths.GetNotificationRecordPath(
                tempDirectory.FullName,
                "session-123",
                turn.NotificationTurnId,
                CreateStopNotificationKeyForTest(laterStopTimestamp))));
            Assert.Equal(earlierStopTimestamp, await File.ReadAllTextAsync(turnClaimPath));

            File.Delete(turnClaimPath);
            _ = await service.HandleStopAsync(
                CreateJsonStream(
                    CreateStopInput(tempDirectory.FullName, laterStopTimestamp),
                    AppJsonSerializerContext.Default.StopHookInput),
                new MemoryStream(),
                CancellationToken.None);

            TelegramSendMessageRequest payload = DeserializeTelegramPayload(
                Assert.Single(handler.Requests));
            Assert.Contains(
                "摘要：The later exact Stop should retry after the earlier claim clears.",
                payload.Text,
                StringComparison.Ordinal);
            Assert.Contains(turn.NotificationTurnId, payload.Text, StringComparison.Ordinal);
        }
        finally
        {
            tempDirectory.Delete(recursive: true);
        }
    }

    [Fact]
    public async Task HandleStopAsyncOlderFreshExactClaimDoesNotSuppressCurrentNonExactDelivery()
    {
        DirectoryInfo tempDirectory = Directory.CreateTempSubdirectory();
        using EnvironmentScope environment = SetTelegramEnvironment();

        try
        {
            WorkspaceStateStore stateStore = new(
                FixedUtcNow(),
                NullLogger<WorkspaceStateStore>.Instance);
            RecordingHttpMessageHandler handler = new();
            HookCommandService service = CreateHookCommandService(handler, stateStore);
            const string stopTimestamp = "2026-03-14T15:51:50.783Z";
            NotificationTurn oldTurn = await CreateTurnAsync(
                stateStore,
                tempDirectory.FullName,
                "session-123",
                "2026-03-14T15:51:30.783Z");
            await WriteSummaryAsync(
                tempDirectory.FullName,
                "session-123",
                oldTurn,
                new NotificationSummary
                {
                    SessionId = "session-123",
                    NotificationTurnId = oldTurn.NotificationTurnId,
                    NotificationNonce = oldTurn.NotificationNonce,
                    UpdatedAt = stopTimestamp,
                    Summary = "The fresh-claimed older exact summary retries after the claim clears.",
                });
            NotificationTurn currentTurn = await CreateTurnAsync(
                stateStore,
                tempDirectory.FullName,
                "session-123",
                "2026-03-14T15:51:45.783Z");
            await WriteSummaryAsync(
                tempDirectory.FullName,
                "session-123",
                currentTurn,
                new NotificationSummary
                {
                    SessionId = "session-123",
                    NotificationTurnId = currentTurn.NotificationTurnId,
                    NotificationNonce = currentTurn.NotificationNonce,
                    UpdatedAt = "2026-03-14T15:51:49.783Z",
                    Summary = "The current non-exact summary must deliver despite the older fresh exact claim.",
                });
            oldTurn.Status = "open";
            await WriteTurnStateAsync(tempDirectory.FullName, oldTurn);
            string oldTurnClaimPath = AppPaths.GetTurnDeliveryClaimPath(
                tempDirectory.FullName,
                "session-123",
                oldTurn.NotificationTurnId);
            await WriteClaimAsync(oldTurnClaimPath, string.Empty);
            File.SetLastWriteTimeUtc(
                oldTurnClaimPath,
                new DateTime(2026, 3, 14, 15, 51, 49, 783, DateTimeKind.Utc));

            _ = await service.HandleStopAsync(
                CreateJsonStream(
                    CreateStopInput(tempDirectory.FullName, stopTimestamp),
                    AppJsonSerializerContext.Default.StopHookInput),
                new MemoryStream(),
                CancellationToken.None);

            TelegramSendMessageRequest payload = DeserializeTelegramPayload(
                Assert.Single(handler.Requests));
            Assert.Contains(
                "摘要：The current non-exact summary must deliver despite the older fresh exact claim.",
                payload.Text,
                StringComparison.Ordinal);
            Assert.Contains(currentTurn.NotificationTurnId, payload.Text, StringComparison.Ordinal);
            Assert.DoesNotContain(oldTurn.NotificationTurnId, payload.Text, StringComparison.Ordinal);
            Assert.Equal(string.Empty, await File.ReadAllTextAsync(oldTurnClaimPath));
        }
        finally
        {
            tempDirectory.Delete(recursive: true);
        }
    }

    [Theory]
    [InlineData(" ")]
    [InlineData(null)]
    public async Task HandleStopAsyncFreshOlderExactPendingClaimDoesNotSuppressCurrentNonExactDelivery(string? pendingSummary)
    {
        DirectoryInfo tempDirectory = Directory.CreateTempSubdirectory();
        using EnvironmentScope environment = SetTelegramEnvironment();

        try
        {
            WorkspaceStateStore stateStore = new(
                FixedUtcNow(),
                NullLogger<WorkspaceStateStore>.Instance);
            RecordingHttpMessageHandler handler = new();
            HookCommandService service = CreateHookCommandService(handler, stateStore);
            const string stopTimestamp = "2026-03-14T15:51:50.783Z";
            NotificationTurn oldTurn = await CreateTurnAsync(
                stateStore,
                tempDirectory.FullName,
                "session-123",
                "2026-03-14T15:51:30.783Z");
            await WriteSummaryAsync(
                tempDirectory.FullName,
                "session-123",
                oldTurn,
                new NotificationSummary
                {
                    SessionId = "session-123",
                    NotificationTurnId = oldTurn.NotificationTurnId,
                    NotificationNonce = oldTurn.NotificationNonce,
                    UpdatedAt = stopTimestamp,
                    Status = "pending",
                    Summary = pendingSummary,
                });
            NotificationTurn currentTurn = await CreateTurnAsync(
                stateStore,
                tempDirectory.FullName,
                "session-123",
                "2026-03-14T15:51:45.783Z");
            await WriteSummaryAsync(
                tempDirectory.FullName,
                "session-123",
                currentTurn,
                new NotificationSummary
                {
                    SessionId = "session-123",
                    NotificationTurnId = currentTurn.NotificationTurnId,
                    NotificationNonce = currentTurn.NotificationNonce,
                    UpdatedAt = "2026-03-14T15:51:49.783Z",
                    Summary = "The current non-exact summary must deliver despite older pending exact attribution.",
                });
            oldTurn.Status = "open";
            await WriteTurnStateAsync(tempDirectory.FullName, oldTurn);
            string oldTurnClaimPath = AppPaths.GetTurnDeliveryClaimPath(
                tempDirectory.FullName,
                "session-123",
                oldTurn.NotificationTurnId);
            await WriteClaimAsync(oldTurnClaimPath, string.Empty);
            File.SetLastWriteTimeUtc(
                oldTurnClaimPath,
                new DateTime(2026, 3, 14, 15, 51, 49, 783, DateTimeKind.Utc));

            _ = await service.HandleStopAsync(
                CreateJsonStream(
                    CreateStopInput(tempDirectory.FullName, stopTimestamp),
                    AppJsonSerializerContext.Default.StopHookInput),
                new MemoryStream(),
                CancellationToken.None);

            TelegramSendMessageRequest payload = DeserializeTelegramPayload(
                Assert.Single(handler.Requests));
            Assert.Contains(
                "摘要：The current non-exact summary must deliver despite older pending exact attribution.",
                payload.Text,
                StringComparison.Ordinal);
            Assert.Contains(currentTurn.NotificationTurnId, payload.Text, StringComparison.Ordinal);
            Assert.DoesNotContain(oldTurn.NotificationTurnId, payload.Text, StringComparison.Ordinal);
            Assert.True(File.Exists(oldTurnClaimPath));
        }
        finally
        {
            tempDirectory.Delete(recursive: true);
        }
    }

    [Theory]
    [InlineData(" ")]
    [InlineData(null)]
    public async Task HandleStopAsyncCurrentNonExactDeliveryDefersBehindOlderExactPendingClaim(
        string? pendingSummary)
    {
        DirectoryInfo tempDirectory = Directory.CreateTempSubdirectory();
        using EnvironmentScope environment = SetTelegramEnvironment();

        try
        {
            WorkspaceStateStore stateStore = new(
                FixedUtcNow(),
                NullLogger<WorkspaceStateStore>.Instance);
            RecordingHttpMessageHandler handler = new();
            HookCommandService service = CreateHookCommandService(handler, stateStore);
            const string stopTimestamp = "2026-03-14T15:51:50.783Z";
            NotificationTurn oldTurn = await CreateTurnAsync(
                stateStore,
                tempDirectory.FullName,
                "session-123",
                "2026-03-14T15:51:30.783Z");
            await WriteSummaryAsync(
                tempDirectory.FullName,
                "session-123",
                oldTurn,
                new NotificationSummary
                {
                    SessionId = "session-123",
                    NotificationTurnId = oldTurn.NotificationTurnId,
                    NotificationNonce = oldTurn.NotificationNonce,
                    UpdatedAt = stopTimestamp,
                    Status = "pending",
                    Summary = pendingSummary,
                });

            _ = await service.HandleStopAsync(
                CreateJsonStream(
                    CreateStopInput(tempDirectory.FullName, stopTimestamp),
                    AppJsonSerializerContext.Default.StopHookInput),
                new MemoryStream(),
                CancellationToken.None);
            Assert.Empty(handler.Requests);
            await AssertPendingStopAsync(
                stateStore,
                tempDirectory.FullName,
                oldTurn,
                stopTimestamp,
                "summary must be a non-empty human-readable sentence");

            NotificationTurn currentTurn = await CreateTurnAsync(
                stateStore,
                tempDirectory.FullName,
                "session-123",
                "2026-03-14T15:51:45.783Z");
            oldTurn.Status = "abandoned";
            oldTurn.UpdatedAt = "2026-03-14T15:51:31.783Z";
            await WriteTurnStateAsync(tempDirectory.FullName, oldTurn);
            await WriteSummaryAsync(
                tempDirectory.FullName,
                "session-123",
                currentTurn,
                new NotificationSummary
                {
                    SessionId = "session-123",
                    NotificationTurnId = currentTurn.NotificationTurnId,
                    NotificationNonce = currentTurn.NotificationNonce,
                    UpdatedAt = "2026-03-14T15:51:49.783Z",
                    Summary = "The current non-exact summary must wait behind older exact pending retry.",
                });
            string oldTurnClaimPath = AppPaths.GetTurnDeliveryClaimPath(
                tempDirectory.FullName,
                "session-123",
                oldTurn.NotificationTurnId);
            await WriteClaimAsync(oldTurnClaimPath, string.Empty);
            File.SetLastWriteTimeUtc(
                oldTurnClaimPath,
                new DateTime(2026, 3, 14, 15, 51, 49, 783, DateTimeKind.Utc));

            _ = await service.HandleStopAsync(
                CreateJsonStream(
                    CreateStopInput(tempDirectory.FullName, stopTimestamp),
                    AppJsonSerializerContext.Default.StopHookInput),
                new MemoryStream(),
                CancellationToken.None);

            Assert.Empty(handler.Requests);
            NotificationTurn? storedOldTurn = await stateStore.TryReadTurnAsync(
                tempDirectory.FullName,
                "session-123",
                oldTurn.NotificationTurnId,
                CancellationToken.None);
            Assert.Equal("abandoned", storedOldTurn?.Status);
        }
        finally
        {
            tempDirectory.Delete(recursive: true);
        }
    }

    [Theory]
    [InlineData(" ")]
    [InlineData(null)]
    public async Task HandleStopAsyncPriorExactPendingObservationWithoutClaimDefersCurrentNonExact(
        string? pendingSummary)
    {
        DirectoryInfo tempDirectory = Directory.CreateTempSubdirectory();
        using EnvironmentScope environment = SetTelegramEnvironment();

        try
        {
            WorkspaceStateStore stateStore = new(
                FixedUtcNow(),
                NullLogger<WorkspaceStateStore>.Instance);
            RecordingHttpMessageHandler handler = new();
            HookCommandService service = CreateHookCommandService(handler, stateStore);
            const string stopTimestamp = "2026-03-14T15:51:50.783Z";
            NotificationTurn oldTurn = await CreateTurnAsync(
                stateStore,
                tempDirectory.FullName,
                "session-123",
                "2026-03-14T15:51:30.783Z");
            oldTurn.UpdatedAt = "2026-03-14T15:51:31.783Z";
            await WriteTurnStateAsync(tempDirectory.FullName, oldTurn);
            await WriteSummaryAsync(
                tempDirectory.FullName,
                "session-123",
                oldTurn,
                new NotificationSummary
                {
                    SessionId = "session-123",
                    NotificationTurnId = oldTurn.NotificationTurnId,
                    NotificationNonce = oldTurn.NotificationNonce,
                    UpdatedAt = stopTimestamp,
                    Status = "pending",
                    Summary = pendingSummary,
                });

            _ = await service.HandleStopAsync(
                CreateJsonStream(
                    CreateStopInput(tempDirectory.FullName, stopTimestamp),
                    AppJsonSerializerContext.Default.StopHookInput),
                new MemoryStream(),
                CancellationToken.None);
            Assert.Empty(handler.Requests);
            await AssertPendingStopAsync(
                stateStore,
                tempDirectory.FullName,
                oldTurn,
                stopTimestamp,
                "summary must be a non-empty human-readable sentence");

            NotificationTurn currentTurn = await CreateTurnAsync(
                stateStore,
                tempDirectory.FullName,
                "session-123",
                "2026-03-14T15:51:45.783Z");
            oldTurn.Status = "abandoned";
            oldTurn.UpdatedAt = "2026-03-14T15:51:31.783Z";
            await WriteTurnStateAsync(tempDirectory.FullName, oldTurn);
            await WriteSummaryAsync(
                tempDirectory.FullName,
                "session-123",
                currentTurn,
                new NotificationSummary
                {
                    SessionId = "session-123",
                    NotificationTurnId = currentTurn.NotificationTurnId,
                    NotificationNonce = currentTurn.NotificationNonce,
                    UpdatedAt = "2026-03-14T15:51:49.783Z",
                    Summary = "The current non-exact summary must wait behind the prior exact pending owner.",
                });

            _ = await service.HandleStopAsync(
                CreateJsonStream(
                    CreateStopInput(tempDirectory.FullName, stopTimestamp),
                    AppJsonSerializerContext.Default.StopHookInput),
                new MemoryStream(),
                CancellationToken.None);

            Assert.Empty(handler.Requests);
            Assert.False(File.Exists(AppPaths.GetNotificationRecordPath(
                tempDirectory.FullName,
                "session-123",
                currentTurn.NotificationTurnId,
                CreateStopNotificationKeyForTest(stopTimestamp))));
            NotificationTurn? storedOldTurn = await stateStore.TryReadTurnAsync(
                tempDirectory.FullName,
                "session-123",
                oldTurn.NotificationTurnId,
                CancellationToken.None);
            Assert.Equal("abandoned", storedOldTurn?.Status);
        }
        finally
        {
            tempDirectory.Delete(recursive: true);
        }
    }

    [Theory]
    [InlineData(" ")]
    [InlineData(null)]
    public async Task HandleStopAsyncInterveningSessionDeliveryLetsCurrentNonExactBypassAbandonedExactPending(
        string? pendingSummary)
    {
        DirectoryInfo tempDirectory = Directory.CreateTempSubdirectory();
        using EnvironmentScope environment = SetTelegramEnvironment();

        try
        {
            WorkspaceStateStore stateStore = new(
                FixedUtcNow(),
                NullLogger<WorkspaceStateStore>.Instance);
            RecordingHttpMessageHandler handler = new();
            HookCommandService service = CreateHookCommandService(handler, stateStore);
            const string stopTimestamp = "2026-03-14T15:53:50.783Z";
            NotificationTurn oldTurn = await CreateTurnAsync(
                stateStore,
                tempDirectory.FullName,
                "session-123",
                "2026-03-14T15:51:30.783Z");
            await WriteSummaryAsync(
                tempDirectory.FullName,
                "session-123",
                oldTurn,
                new NotificationSummary
                {
                    SessionId = "session-123",
                    NotificationTurnId = oldTurn.NotificationTurnId,
                    NotificationNonce = oldTurn.NotificationNonce,
                    UpdatedAt = stopTimestamp,
                    Status = "pending",
                    Summary = pendingSummary,
                });

            _ = await service.HandleStopAsync(
                CreateJsonStream(
                    CreateStopInput(tempDirectory.FullName, stopTimestamp),
                    AppJsonSerializerContext.Default.StopHookInput),
                new MemoryStream(),
                CancellationToken.None);
            Assert.Empty(handler.Requests);
            await AssertPendingStopAsync(
                stateStore,
                tempDirectory.FullName,
                oldTurn,
                stopTimestamp,
                "summary must be a non-empty human-readable sentence");

            NotificationTurn currentTurn = await CreateTurnAsync(
                stateStore,
                tempDirectory.FullName,
                "session-123",
                "2026-03-14T15:53:40.783Z");
            oldTurn.Status = "abandoned";
            oldTurn.UpdatedAt = "2026-03-14T15:51:31.783Z";
            await WriteTurnStateAsync(tempDirectory.FullName, oldTurn);
            await WriteSummaryAsync(
                tempDirectory.FullName,
                "session-123",
                currentTurn,
                new NotificationSummary
                {
                    SessionId = "session-123",
                    NotificationTurnId = currentTurn.NotificationTurnId,
                    NotificationNonce = currentTurn.NotificationNonce,
                    UpdatedAt = "2026-03-14T15:53:49.783Z",
                    Summary = "The current non-exact summary is not blocked by resolved exact pending.",
                });
            const string interveningStopTimestamp = "2026-03-14T15:52:50.783Z";
            string interveningNotificationKey = CreateStopNotificationKeyForTest(interveningStopTimestamp);
            await WriteNotificationRecordAsync(
                AppPaths.GetSessionNotificationRecordPath(
                    tempDirectory.FullName,
                    "session-123",
                    interveningNotificationKey),
                new NotificationRecord
                {
                    SessionId = "session-123",
                    NotificationKey = interveningNotificationKey,
                    WorkspacePath = tempDirectory.FullName,
                    StopTimestamp = interveningStopTimestamp,
                    SentAt = "2026-03-14T15:52:51.783Z",
                    Degraded = true,
                    DeliveryStatus = "sent",
                });

            _ = await service.HandleStopAsync(
                CreateJsonStream(
                    CreateStopInput(tempDirectory.FullName, stopTimestamp),
                    AppJsonSerializerContext.Default.StopHookInput),
                new MemoryStream(),
                CancellationToken.None);

            TelegramSendMessageRequest payload = DeserializeTelegramPayload(
                Assert.Single(handler.Requests));
            Assert.Contains(
                "摘要：The current non-exact summary is not blocked by resolved exact pending.",
                payload.Text,
                StringComparison.Ordinal);
            Assert.Contains(currentTurn.NotificationTurnId, payload.Text, StringComparison.Ordinal);
            Assert.DoesNotContain(oldTurn.NotificationTurnId, payload.Text, StringComparison.Ordinal);
        }
        finally
        {
            tempDirectory.Delete(recursive: true);
        }
    }

    [Theory]
    [InlineData("session", "sent")]
    [InlineData("session", "partial")]
    [InlineData("per-turn", "sent")]
    [InlineData("per-turn", "partial")]
    public async Task HandleStopAsyncInterveningDurableDeliveryLetsFallbackBypassSoleOpenExactPending(
        string deliveryRecordScope,
        string deliveryStatus)
    {
        DirectoryInfo tempDirectory = Directory.CreateTempSubdirectory();
        using EnvironmentScope environment = SetTelegramEnvironment();

        try
        {
            WorkspaceStateStore stateStore = new(
                FixedUtcNow(),
                NullLogger<WorkspaceStateStore>.Instance);
            RecordingHttpMessageHandler handler = new();
            HookCommandService service = CreateHookCommandService(handler, stateStore);
            const string stopTimestamp = "2026-03-14T15:53:50.783Z";
            NotificationTurn staleTurn = await CreateTurnAsync(
                stateStore,
                tempDirectory.FullName,
                "session-123",
                "2026-03-14T15:51:30.783Z");
            await WriteSummaryAsync(
                tempDirectory.FullName,
                "session-123",
                staleTurn,
                new NotificationSummary
                {
                    SessionId = "session-123",
                    NotificationTurnId = staleTurn.NotificationTurnId,
                    NotificationNonce = staleTurn.NotificationNonce,
                    UpdatedAt = stopTimestamp,
                    Status = "pending",
                    Summary = " ",
                });
            const string interveningStopTimestamp = "2026-03-14T15:52:50.783Z";
            string interveningNotificationKey = CreateStopNotificationKeyForTest(interveningStopTimestamp);
            string notificationRecordPath = string.Equals(
                    deliveryRecordScope,
                    "session",
                    StringComparison.Ordinal)
                ? AppPaths.GetSessionNotificationRecordPath(
                    tempDirectory.FullName,
                    "session-123",
                    interveningNotificationKey)
                : AppPaths.GetNotificationRecordPath(
                    tempDirectory.FullName,
                    "session-123",
                    staleTurn.NotificationTurnId,
                    interveningNotificationKey);
            await WriteNotificationRecordAsync(
                notificationRecordPath,
                new NotificationRecord
                {
                    SessionId = "session-123",
                    NotificationTurnId = string.Equals(
                        deliveryRecordScope,
                        "per-turn",
                        StringComparison.Ordinal)
                            ? staleTurn.NotificationTurnId
                            : null,
                    NotificationKey = interveningNotificationKey,
                    WorkspacePath = tempDirectory.FullName,
                    StopTimestamp = interveningStopTimestamp,
                    SentAt = "2026-03-14T15:52:51.783Z",
                    Degraded = string.Equals(deliveryRecordScope, "session", StringComparison.Ordinal),
                    DeliveryStatus = deliveryStatus,
                });

            _ = await service.HandleStopAsync(
                CreateJsonStream(
                    CreateStopInput(tempDirectory.FullName, stopTimestamp),
                    AppJsonSerializerContext.Default.StopHookInput),
                new MemoryStream(),
                CancellationToken.None);

            TelegramSendMessageRequest payload = DeserializeTelegramPayload(
                Assert.Single(handler.Requests));
            Assert.Contains("摘要：当前轮未生成摘要。", payload.Text, StringComparison.Ordinal);
            Assert.DoesNotContain(staleTurn.NotificationTurnId, payload.Text, StringComparison.Ordinal);
            Assert.False(File.Exists(AppPaths.GetNotificationRecordPath(
                tempDirectory.FullName,
                "session-123",
                staleTurn.NotificationTurnId,
                CreateStopNotificationKeyForTest(stopTimestamp))));
        }
        finally
        {
            tempDirectory.Delete(recursive: true);
        }
    }

    [Theory]
    [InlineData("session", "sent")]
    [InlineData("session", "partial")]
    [InlineData("per-turn", "sent")]
    [InlineData("per-turn", "partial")]
    public async Task HandleStopAsyncInterveningDurableDeliveryLetsFallbackBypassSoleOpenExactCompleted(
        string deliveryRecordScope,
        string deliveryStatus)
    {
        DirectoryInfo tempDirectory = Directory.CreateTempSubdirectory();
        using EnvironmentScope environment = SetTelegramEnvironment();

        try
        {
            WorkspaceStateStore stateStore = new(
                FixedUtcNow(),
                NullLogger<WorkspaceStateStore>.Instance);
            RecordingHttpMessageHandler handler = new();
            HookCommandService service = CreateHookCommandService(handler, stateStore);
            const string stopTimestamp = "2026-03-14T15:53:50.783Z";
            NotificationTurn staleTurn = await CreateTurnAsync(
                stateStore,
                tempDirectory.FullName,
                "session-123",
                "2026-03-14T15:51:30.783Z");
            await WriteSummaryAsync(
                tempDirectory.FullName,
                "session-123",
                staleTurn,
                new NotificationSummary
                {
                    SessionId = "session-123",
                    NotificationTurnId = staleTurn.NotificationTurnId,
                    NotificationNonce = staleTurn.NotificationNonce,
                    UpdatedAt = stopTimestamp,
                    Status = "completed",
                    Summary = "The stale completed exact summary must not be sent.",
                });
            const string interveningStopTimestamp = "2026-03-14T15:52:50.783Z";
            string interveningNotificationKey = CreateStopNotificationKeyForTest(interveningStopTimestamp);
            string notificationRecordPath = string.Equals(
                    deliveryRecordScope,
                    "session",
                    StringComparison.Ordinal)
                ? AppPaths.GetSessionNotificationRecordPath(
                    tempDirectory.FullName,
                    "session-123",
                    interveningNotificationKey)
                : AppPaths.GetNotificationRecordPath(
                    tempDirectory.FullName,
                    "session-123",
                    staleTurn.NotificationTurnId,
                    interveningNotificationKey);
            await WriteNotificationRecordAsync(
                notificationRecordPath,
                new NotificationRecord
                {
                    SessionId = "session-123",
                    NotificationTurnId = string.Equals(
                        deliveryRecordScope,
                        "per-turn",
                        StringComparison.Ordinal)
                            ? staleTurn.NotificationTurnId
                            : null,
                    NotificationKey = interveningNotificationKey,
                    WorkspacePath = tempDirectory.FullName,
                    StopTimestamp = interveningStopTimestamp,
                    SentAt = "2026-03-14T15:52:51.783Z",
                    Degraded = string.Equals(deliveryRecordScope, "session", StringComparison.Ordinal),
                    DeliveryStatus = deliveryStatus,
                });

            _ = await service.HandleStopAsync(
                CreateJsonStream(
                    CreateStopInput(tempDirectory.FullName, stopTimestamp),
                    AppJsonSerializerContext.Default.StopHookInput),
                new MemoryStream(),
                CancellationToken.None);

            TelegramSendMessageRequest payload = DeserializeTelegramPayload(
                Assert.Single(handler.Requests));
            Assert.Contains("摘要：当前轮未生成摘要。", payload.Text, StringComparison.Ordinal);
            Assert.DoesNotContain(
                "The stale completed exact summary must not be sent.",
                payload.Text,
                StringComparison.Ordinal);
            Assert.DoesNotContain(staleTurn.NotificationTurnId, payload.Text, StringComparison.Ordinal);
            Assert.False(File.Exists(AppPaths.GetNotificationRecordPath(
                tempDirectory.FullName,
                "session-123",
                staleTurn.NotificationTurnId,
                CreateStopNotificationKeyForTest(stopTimestamp))));
        }
        finally
        {
            tempDirectory.Delete(recursive: true);
        }
    }

    [Theory]
    [InlineData("session", "sent")]
    [InlineData("session", "partial")]
    [InlineData("per-turn", "sent")]
    [InlineData("per-turn", "partial")]
    public async Task HandleStopAsyncInterveningDurableDeliveryLetsCurrentNonExactBypassOpenExactPending(
        string deliveryRecordScope,
        string deliveryStatus)
    {
        DirectoryInfo tempDirectory = Directory.CreateTempSubdirectory();
        using EnvironmentScope environment = SetTelegramEnvironment();

        try
        {
            WorkspaceStateStore stateStore = new(
                FixedUtcNow(),
                NullLogger<WorkspaceStateStore>.Instance);
            RecordingHttpMessageHandler handler = new();
            HookCommandService service = CreateHookCommandService(handler, stateStore);
            const string stopTimestamp = "2026-03-14T15:53:50.783Z";
            NotificationTurn oldTurn = await CreateTurnAsync(
                stateStore,
                tempDirectory.FullName,
                "session-123",
                "2026-03-14T15:51:30.783Z");
            await WriteSummaryAsync(
                tempDirectory.FullName,
                "session-123",
                oldTurn,
                new NotificationSummary
                {
                    SessionId = "session-123",
                    NotificationTurnId = oldTurn.NotificationTurnId,
                    NotificationNonce = oldTurn.NotificationNonce,
                    UpdatedAt = stopTimestamp,
                    Status = "pending",
                    Summary = " ",
                });

            _ = await service.HandleStopAsync(
                CreateJsonStream(
                    CreateStopInput(tempDirectory.FullName, stopTimestamp),
                    AppJsonSerializerContext.Default.StopHookInput),
                new MemoryStream(),
                CancellationToken.None);
            Assert.Empty(handler.Requests);
            await AssertPendingStopAsync(
                stateStore,
                tempDirectory.FullName,
                oldTurn,
                stopTimestamp,
                "summary must be a non-empty human-readable sentence");

            NotificationTurn currentTurn = await CreateTurnAsync(
                stateStore,
                tempDirectory.FullName,
                "session-123",
                "2026-03-14T15:53:40.783Z");
            await WriteSummaryAsync(
                tempDirectory.FullName,
                "session-123",
                currentTurn,
                new NotificationSummary
                {
                    SessionId = "session-123",
                    NotificationTurnId = currentTurn.NotificationTurnId,
                    NotificationNonce = currentTurn.NotificationNonce,
                    UpdatedAt = "2026-03-14T15:53:49.783Z",
                    Summary = "The current non-exact summary is not blocked by resolved open exact pending.",
                });
            const string interveningStopTimestamp = "2026-03-14T15:52:50.783Z";
            string interveningNotificationKey = CreateStopNotificationKeyForTest(interveningStopTimestamp);
            string notificationRecordPath = string.Equals(
                    deliveryRecordScope,
                    "session",
                    StringComparison.Ordinal)
                ? AppPaths.GetSessionNotificationRecordPath(
                    tempDirectory.FullName,
                    "session-123",
                    interveningNotificationKey)
                : AppPaths.GetNotificationRecordPath(
                    tempDirectory.FullName,
                    "session-123",
                    oldTurn.NotificationTurnId,
                    interveningNotificationKey);
            await WriteNotificationRecordAsync(
                notificationRecordPath,
                new NotificationRecord
                {
                    SessionId = "session-123",
                    NotificationTurnId = string.Equals(
                        deliveryRecordScope,
                        "per-turn",
                        StringComparison.Ordinal)
                            ? oldTurn.NotificationTurnId
                            : null,
                    NotificationKey = interveningNotificationKey,
                    WorkspacePath = tempDirectory.FullName,
                    StopTimestamp = interveningStopTimestamp,
                    SentAt = "2026-03-14T15:52:51.783Z",
                    Degraded = string.Equals(deliveryRecordScope, "session", StringComparison.Ordinal),
                    DeliveryStatus = deliveryStatus,
                });

            _ = await service.HandleStopAsync(
                CreateJsonStream(
                    CreateStopInput(tempDirectory.FullName, stopTimestamp),
                    AppJsonSerializerContext.Default.StopHookInput),
                new MemoryStream(),
                CancellationToken.None);

            TelegramSendMessageRequest payload = DeserializeTelegramPayload(
                Assert.Single(handler.Requests));
            Assert.Contains(
                "摘要：The current non-exact summary is not blocked by resolved open exact pending.",
                payload.Text,
                StringComparison.Ordinal);
            Assert.Contains(currentTurn.NotificationTurnId, payload.Text, StringComparison.Ordinal);
            Assert.DoesNotContain(oldTurn.NotificationTurnId, payload.Text, StringComparison.Ordinal);
        }
        finally
        {
            tempDirectory.Delete(recursive: true);
        }
    }

    [Theory]
    [InlineData("session", "sent")]
    [InlineData("session", "partial")]
    [InlineData("per-turn", "sent")]
    [InlineData("per-turn", "partial")]
    public async Task HandleStopAsyncInterveningDurableDeliveryBypassesObservedPendingCompletedExact(
        string deliveryRecordScope,
        string deliveryStatus)
    {
        DirectoryInfo tempDirectory = Directory.CreateTempSubdirectory();
        using EnvironmentScope environment = SetTelegramEnvironment();

        try
        {
            WorkspaceStateStore stateStore = new(
                FixedUtcNow(),
                NullLogger<WorkspaceStateStore>.Instance);
            RecordingHttpMessageHandler handler = new();
            HookCommandService service = CreateHookCommandService(handler, stateStore);
            const string stopTimestamp = "2026-03-14T15:53:50.783Z";
            NotificationTurn oldTurn = await CreateTurnAsync(
                stateStore,
                tempDirectory.FullName,
                "session-123",
                "2026-03-14T15:51:30.783Z");
            await WriteSummaryAsync(
                tempDirectory.FullName,
                "session-123",
                oldTurn,
                new NotificationSummary
                {
                    SessionId = "session-123",
                    NotificationTurnId = oldTurn.NotificationTurnId,
                    NotificationNonce = oldTurn.NotificationNonce,
                    UpdatedAt = stopTimestamp,
                    Status = "pending",
                    Summary = " ",
                });

            _ = await service.HandleStopAsync(
                CreateJsonStream(
                    CreateStopInput(tempDirectory.FullName, stopTimestamp),
                    AppJsonSerializerContext.Default.StopHookInput),
                new MemoryStream(),
                CancellationToken.None);
            Assert.Empty(handler.Requests);
            await AssertPendingStopAsync(
                stateStore,
                tempDirectory.FullName,
                oldTurn,
                stopTimestamp,
                "summary must be a non-empty human-readable sentence");

            NotificationTurn currentTurn = await CreateTurnAsync(
                stateStore,
                tempDirectory.FullName,
                "session-123",
                "2026-03-14T15:53:40.783Z");
            await WriteSummaryAsync(
                tempDirectory.FullName,
                "session-123",
                currentTurn,
                new NotificationSummary
                {
                    SessionId = "session-123",
                    NotificationTurnId = currentTurn.NotificationTurnId,
                    NotificationNonce = currentTurn.NotificationNonce,
                    UpdatedAt = "2026-03-14T15:53:49.783Z",
                    Summary = "The current non-exact summary owns the Stop after durable delivery.",
                });
            await WriteSummaryAsync(
                tempDirectory.FullName,
                "session-123",
                oldTurn,
                new NotificationSummary
                {
                    SessionId = "session-123",
                    NotificationTurnId = oldTurn.NotificationTurnId,
                    NotificationNonce = oldTurn.NotificationNonce,
                    UpdatedAt = stopTimestamp,
                    Status = "completed",
                    Summary = "The stale observed completed exact summary must not be sent.",
                });

            const string interveningStopTimestamp = "2026-03-14T15:52:50.783Z";
            string interveningNotificationKey = CreateStopNotificationKeyForTest(interveningStopTimestamp);
            string notificationRecordPath = string.Equals(
                    deliveryRecordScope,
                    "session",
                    StringComparison.Ordinal)
                ? AppPaths.GetSessionNotificationRecordPath(
                    tempDirectory.FullName,
                    "session-123",
                    interveningNotificationKey)
                : AppPaths.GetNotificationRecordPath(
                    tempDirectory.FullName,
                    "session-123",
                    oldTurn.NotificationTurnId,
                    interveningNotificationKey);
            await WriteNotificationRecordAsync(
                notificationRecordPath,
                new NotificationRecord
                {
                    SessionId = "session-123",
                    NotificationTurnId = string.Equals(
                        deliveryRecordScope,
                        "per-turn",
                        StringComparison.Ordinal)
                            ? oldTurn.NotificationTurnId
                            : null,
                    NotificationKey = interveningNotificationKey,
                    WorkspacePath = tempDirectory.FullName,
                    StopTimestamp = interveningStopTimestamp,
                    SentAt = "2026-03-14T15:52:51.783Z",
                    Degraded = string.Equals(deliveryRecordScope, "session", StringComparison.Ordinal),
                    DeliveryStatus = deliveryStatus,
                });

            _ = await service.HandleStopAsync(
                CreateJsonStream(
                    CreateStopInput(tempDirectory.FullName, stopTimestamp),
                    AppJsonSerializerContext.Default.StopHookInput),
                new MemoryStream(),
                CancellationToken.None);

            TelegramSendMessageRequest payload = DeserializeTelegramPayload(
                Assert.Single(handler.Requests));
            Assert.Contains(
                "摘要：The current non-exact summary owns the Stop after durable delivery.",
                payload.Text,
                StringComparison.Ordinal);
            Assert.Contains(currentTurn.NotificationTurnId, payload.Text, StringComparison.Ordinal);
            Assert.DoesNotContain(oldTurn.NotificationTurnId, payload.Text, StringComparison.Ordinal);
            Assert.DoesNotContain(
                "The stale observed completed exact summary must not be sent.",
                payload.Text,
                StringComparison.Ordinal);
            Assert.False(File.Exists(AppPaths.GetNotificationRecordPath(
                tempDirectory.FullName,
                "session-123",
                oldTurn.NotificationTurnId,
                CreateStopNotificationKeyForTest(stopTimestamp))));
        }
        finally
        {
            tempDirectory.Delete(recursive: true);
        }
    }

    [Theory]
    [InlineData("session", "sent")]
    [InlineData("session", "partial")]
    [InlineData("per-turn", "sent")]
    [InlineData("per-turn", "partial")]
    public async Task HandleStopAsyncInterveningDurableDeliveryFiltersMultiOpenLatestCompletedExact(
        string deliveryRecordScope,
        string deliveryStatus)
    {
        DirectoryInfo tempDirectory = Directory.CreateTempSubdirectory();
        using EnvironmentScope environment = SetTelegramEnvironment();

        try
        {
            WorkspaceStateStore stateStore = new(
                FixedUtcNow(),
                NullLogger<WorkspaceStateStore>.Instance);
            RecordingHttpMessageHandler handler = new();
            HookCommandService service = CreateHookCommandService(handler, stateStore);
            const string stopTimestamp = "2026-03-14T15:53:50.783Z";
            NotificationTurn fallbackTurn = await CreateTurnAsync(
                stateStore,
                tempDirectory.FullName,
                "session-123",
                "2026-03-14T15:51:30.783Z");
            await WriteSummaryAsync(
                tempDirectory.FullName,
                "session-123",
                fallbackTurn,
                new NotificationSummary
                {
                    SessionId = "session-123",
                    NotificationTurnId = fallbackTurn.NotificationTurnId,
                    NotificationNonce = fallbackTurn.NotificationNonce,
                    UpdatedAt = "2026-03-14T15:53:49.783Z",
                    Summary = "The older fallback summary owns the Stop after stale exact completion.",
                });
            NotificationTurn staleExactTurn = await CreateTurnAsync(
                stateStore,
                tempDirectory.FullName,
                "session-123",
                "2026-03-14T15:53:40.783Z");
            await WriteSummaryAsync(
                tempDirectory.FullName,
                "session-123",
                staleExactTurn,
                new NotificationSummary
                {
                    SessionId = "session-123",
                    NotificationTurnId = staleExactTurn.NotificationTurnId,
                    NotificationNonce = staleExactTurn.NotificationNonce,
                    UpdatedAt = stopTimestamp,
                    Status = "completed",
                    Summary = "The stale latest exact summary must not be sent.",
                });
            File.Delete(AppPaths.GetCurrentStatePath(tempDirectory.FullName, "session-123"));

            const string interveningStopTimestamp = "2026-03-14T15:53:45.783Z";
            string interveningNotificationKey = CreateStopNotificationKeyForTest(interveningStopTimestamp);
            string notificationRecordPath = string.Equals(
                    deliveryRecordScope,
                    "session",
                    StringComparison.Ordinal)
                ? AppPaths.GetSessionNotificationRecordPath(
                    tempDirectory.FullName,
                    "session-123",
                    interveningNotificationKey)
                : AppPaths.GetNotificationRecordPath(
                    tempDirectory.FullName,
                    "session-123",
                    staleExactTurn.NotificationTurnId,
                    interveningNotificationKey);
            await WriteNotificationRecordAsync(
                notificationRecordPath,
                new NotificationRecord
                {
                    SessionId = "session-123",
                    NotificationTurnId = string.Equals(
                        deliveryRecordScope,
                        "per-turn",
                        StringComparison.Ordinal)
                            ? staleExactTurn.NotificationTurnId
                            : null,
                    NotificationKey = interveningNotificationKey,
                    WorkspacePath = tempDirectory.FullName,
                    StopTimestamp = interveningStopTimestamp,
                    SentAt = "2026-03-14T15:53:46.783Z",
                    Degraded = string.Equals(deliveryRecordScope, "session", StringComparison.Ordinal),
                    DeliveryStatus = deliveryStatus,
                });

            _ = await service.HandleStopAsync(
                CreateJsonStream(
                    CreateStopInput(tempDirectory.FullName, stopTimestamp),
                    AppJsonSerializerContext.Default.StopHookInput),
                new MemoryStream(),
                CancellationToken.None);

            TelegramSendMessageRequest payload = DeserializeTelegramPayload(
                Assert.Single(handler.Requests));
            Assert.Contains(
                "摘要：The older fallback summary owns the Stop after stale exact completion.",
                payload.Text,
                StringComparison.Ordinal);
            Assert.Contains(fallbackTurn.NotificationTurnId, payload.Text, StringComparison.Ordinal);
            Assert.DoesNotContain(staleExactTurn.NotificationTurnId, payload.Text, StringComparison.Ordinal);
            Assert.DoesNotContain(
                "The stale latest exact summary must not be sent.",
                payload.Text,
                StringComparison.Ordinal);
        }
        finally
        {
            tempDirectory.Delete(recursive: true);
        }
    }

    [Theory]
    [InlineData("session", "sent")]
    [InlineData("session", "partial")]
    [InlineData("per-turn", "sent")]
    [InlineData("per-turn", "partial")]
    public async Task HandleStopAsyncInterveningDurableDeliveryFiltersMultiOpenLatestExactPending(
        string deliveryRecordScope,
        string deliveryStatus)
    {
        DirectoryInfo tempDirectory = Directory.CreateTempSubdirectory();
        using EnvironmentScope environment = SetTelegramEnvironment();

        try
        {
            WorkspaceStateStore stateStore = new(
                FixedUtcNow(),
                NullLogger<WorkspaceStateStore>.Instance);
            RecordingHttpMessageHandler handler = new();
            HookCommandService service = CreateHookCommandService(handler, stateStore);
            const string stopTimestamp = "2026-03-14T15:53:50.783Z";
            NotificationTurn fallbackTurn = await CreateTurnAsync(
                stateStore,
                tempDirectory.FullName,
                "session-123",
                "2026-03-14T15:51:30.783Z");
            await WriteSummaryAsync(
                tempDirectory.FullName,
                "session-123",
                fallbackTurn,
                new NotificationSummary
                {
                    SessionId = "session-123",
                    NotificationTurnId = fallbackTurn.NotificationTurnId,
                    NotificationNonce = fallbackTurn.NotificationNonce,
                    UpdatedAt = "2026-03-14T15:53:49.783Z",
                    Summary = "The older fallback summary is not delayed by stale pending.",
                });
            NotificationTurn stalePendingTurn = await CreateTurnAsync(
                stateStore,
                tempDirectory.FullName,
                "session-123",
                "2026-03-14T15:53:40.783Z");
            await WriteSummaryAsync(
                tempDirectory.FullName,
                "session-123",
                stalePendingTurn,
                new NotificationSummary
                {
                    SessionId = "session-123",
                    NotificationTurnId = stalePendingTurn.NotificationTurnId,
                    NotificationNonce = stalePendingTurn.NotificationNonce,
                    UpdatedAt = stopTimestamp,
                    Status = "pending",
                    Summary = " ",
                });
            File.Delete(AppPaths.GetCurrentStatePath(tempDirectory.FullName, "session-123"));

            const string interveningStopTimestamp = "2026-03-14T15:53:45.783Z";
            string interveningNotificationKey = CreateStopNotificationKeyForTest(interveningStopTimestamp);
            string notificationRecordPath = string.Equals(
                    deliveryRecordScope,
                    "session",
                    StringComparison.Ordinal)
                ? AppPaths.GetSessionNotificationRecordPath(
                    tempDirectory.FullName,
                    "session-123",
                    interveningNotificationKey)
                : AppPaths.GetNotificationRecordPath(
                    tempDirectory.FullName,
                    "session-123",
                    stalePendingTurn.NotificationTurnId,
                    interveningNotificationKey);
            await WriteNotificationRecordAsync(
                notificationRecordPath,
                new NotificationRecord
                {
                    SessionId = "session-123",
                    NotificationTurnId = string.Equals(
                        deliveryRecordScope,
                        "per-turn",
                        StringComparison.Ordinal)
                            ? stalePendingTurn.NotificationTurnId
                            : null,
                    NotificationKey = interveningNotificationKey,
                    WorkspacePath = tempDirectory.FullName,
                    StopTimestamp = interveningStopTimestamp,
                    SentAt = "2026-03-14T15:53:46.783Z",
                    Degraded = string.Equals(deliveryRecordScope, "session", StringComparison.Ordinal),
                    DeliveryStatus = deliveryStatus,
                });

            _ = await service.HandleStopAsync(
                CreateJsonStream(
                    CreateStopInput(tempDirectory.FullName, stopTimestamp),
                    AppJsonSerializerContext.Default.StopHookInput),
                new MemoryStream(),
                CancellationToken.None);

            TelegramSendMessageRequest payload = DeserializeTelegramPayload(
                Assert.Single(handler.Requests));
            Assert.Contains(
                "摘要：The older fallback summary is not delayed by stale pending.",
                payload.Text,
                StringComparison.Ordinal);
            Assert.Contains(fallbackTurn.NotificationTurnId, payload.Text, StringComparison.Ordinal);
            Assert.DoesNotContain(stalePendingTurn.NotificationTurnId, payload.Text, StringComparison.Ordinal);
            Assert.False(File.Exists(AppPaths.GetNotificationRecordPath(
                tempDirectory.FullName,
                "session-123",
                stalePendingTurn.NotificationTurnId,
                CreateStopNotificationKeyForTest(stopTimestamp))));
        }
        finally
        {
            tempDirectory.Delete(recursive: true);
        }
    }

    [Theory]
    [InlineData(" ")]
    [InlineData(null)]
    public async Task HandleStopAsyncUnresolvedExactPendingBlocksCompletedExactAndLaterNonExact(
        string? pendingSummary)
    {
        DirectoryInfo tempDirectory = Directory.CreateTempSubdirectory();
        using EnvironmentScope environment = SetTelegramEnvironment();

        try
        {
            WorkspaceStateStore stateStore = new(
                TimeProvider.System,
                NullLogger<WorkspaceStateStore>.Instance);
            RecordingHttpMessageHandler handler = new();
            HookCommandService service = CreateHookCommandService(handler, stateStore);
            const string stopTimestamp = "2026-03-14T15:51:50.783Z";
            NotificationTurn validExactTurn = await CreateTurnAsync(
                stateStore,
                tempDirectory.FullName,
                "session-123",
                "2026-03-14T15:51:30.783Z");
            await WriteSummaryAsync(
                tempDirectory.FullName,
                "session-123",
                validExactTurn,
                new NotificationSummary
                {
                    SessionId = "session-123",
                    NotificationTurnId = validExactTurn.NotificationTurnId,
                    NotificationNonce = validExactTurn.NotificationNonce,
                    UpdatedAt = stopTimestamp,
                    Summary = "The valid older exact summary is not unique evidence.",
                });
            NotificationTurn pendingExactTurn = await CreateTurnAsync(
                stateStore,
                tempDirectory.FullName,
                "session-123",
                "2026-03-14T15:51:35.783Z");
            await WriteSummaryAsync(
                tempDirectory.FullName,
                "session-123",
                pendingExactTurn,
                new NotificationSummary
                {
                    SessionId = "session-123",
                    NotificationTurnId = pendingExactTurn.NotificationTurnId,
                    NotificationNonce = pendingExactTurn.NotificationNonce,
                    UpdatedAt = stopTimestamp,
                    Status = "pending",
                    Summary = pendingSummary,
                });
            NotificationTurn currentTurn = await CreateTurnAsync(
                stateStore,
                tempDirectory.FullName,
                "session-123",
                "2026-03-14T15:51:45.783Z");
            await WriteSummaryAsync(
                tempDirectory.FullName,
                "session-123",
                currentTurn,
                new NotificationSummary
                {
                    SessionId = "session-123",
                    NotificationTurnId = currentTurn.NotificationTurnId,
                    NotificationNonce = currentTurn.NotificationNonce,
                    UpdatedAt = "2026-03-14T15:51:49.783Z",
                    Summary = "The later non-exact summary must wait behind unresolved exact pending.",
                });

            _ = await service.HandleStopAsync(
                CreateJsonStream(
                    CreateStopInput(tempDirectory.FullName, stopTimestamp),
                    AppJsonSerializerContext.Default.StopHookInput),
                new MemoryStream(),
                CancellationToken.None);

            Assert.Empty(handler.Requests);
            await AssertPendingStopAsync(
                stateStore,
                tempDirectory.FullName,
                pendingExactTurn,
                stopTimestamp,
                "summary must be a non-empty human-readable sentence");
            Assert.False(File.Exists(AppPaths.GetNotificationRecordPath(
                tempDirectory.FullName,
                "session-123",
                currentTurn.NotificationTurnId,
                CreateStopNotificationKeyForTest(stopTimestamp))));
            Assert.False(File.Exists(AppPaths.GetNotificationRecordPath(
                tempDirectory.FullName,
                "session-123",
                validExactTurn.NotificationTurnId,
                CreateStopNotificationKeyForTest(stopTimestamp))));
            Assert.False(File.Exists(AppPaths.GetNotificationRecordPath(
                tempDirectory.FullName,
                "session-123",
                pendingExactTurn.NotificationTurnId,
                CreateStopNotificationKeyForTest(stopTimestamp))));

            await WriteSummaryAsync(
                tempDirectory.FullName,
                "session-123",
                pendingExactTurn,
                new NotificationSummary
                {
                    SessionId = "session-123",
                    NotificationTurnId = pendingExactTurn.NotificationTurnId,
                    NotificationNonce = pendingExactTurn.NotificationNonce,
                    UpdatedAt = stopTimestamp,
                    Summary = "The previously unresolved exact pending summary now owns the Stop.",
                });

            _ = await service.HandleStopAsync(
                CreateJsonStream(
                    CreateStopInput(tempDirectory.FullName, stopTimestamp),
                    AppJsonSerializerContext.Default.StopHookInput),
                new MemoryStream(),
                CancellationToken.None);
            _ = await service.HandleStopAsync(
                CreateJsonStream(
                    CreateStopInput(tempDirectory.FullName, stopTimestamp),
                    AppJsonSerializerContext.Default.StopHookInput),
                new MemoryStream(),
                CancellationToken.None);

            TelegramSendMessageRequest payload = DeserializeTelegramPayload(
                Assert.Single(handler.Requests));
            Assert.Contains(
                "摘要：The previously unresolved exact pending summary now owns the Stop.",
                payload.Text,
                StringComparison.Ordinal);
            Assert.Contains(pendingExactTurn.NotificationTurnId, payload.Text, StringComparison.Ordinal);
            Assert.DoesNotContain(validExactTurn.NotificationTurnId, payload.Text, StringComparison.Ordinal);
            Assert.DoesNotContain(currentTurn.NotificationTurnId, payload.Text, StringComparison.Ordinal);
            Assert.True(await WorkspaceStateStore.HasDurableDeliveryRecordAsync(
                tempDirectory.FullName,
                "session-123",
                pendingExactTurn.NotificationTurnId,
                CancellationToken.None));
            Assert.False(await WorkspaceStateStore.HasDurableDeliveryRecordAsync(
                tempDirectory.FullName,
                "session-123",
                validExactTurn.NotificationTurnId,
                CancellationToken.None));
            Assert.False(await WorkspaceStateStore.HasDurableDeliveryRecordAsync(
                tempDirectory.FullName,
                "session-123",
                currentTurn.NotificationTurnId,
                CancellationToken.None));
        }
        finally
        {
            tempDirectory.Delete(recursive: true);
        }
    }

    [Theory]
    [InlineData("missing-current")]
    [InlineData("corrupt-current")]
    public async Task HandleStopAsyncCachelessOlderPendingExactSuppressesLatestValidExact(
        string currentCacheState)
    {
        DirectoryInfo tempDirectory = Directory.CreateTempSubdirectory();
        using EnvironmentScope environment = SetTelegramEnvironment();

        try
        {
            WorkspaceStateStore stateStore = new(
                TimeProvider.System,
                NullLogger<WorkspaceStateStore>.Instance);
            RecordingHttpMessageHandler handler = new();
            HookCommandService service = CreateHookCommandService(handler, stateStore);
            const string stopTimestamp = "2026-03-14T15:51:50.783Z";
            NotificationTurn pendingExactTurn = await CreateTurnAsync(
                stateStore,
                tempDirectory.FullName,
                "session-123",
                "2026-03-14T15:51:35.783Z");
            await WriteSummaryAsync(
                tempDirectory.FullName,
                "session-123",
                pendingExactTurn,
                new NotificationSummary
                {
                    SessionId = "session-123",
                    NotificationTurnId = pendingExactTurn.NotificationTurnId,
                    NotificationNonce = pendingExactTurn.NotificationNonce,
                    UpdatedAt = stopTimestamp,
                    Status = "pending",
                    Summary = " ",
                });
            NotificationTurn validExactTurn = await CreateTurnAsync(
                stateStore,
                tempDirectory.FullName,
                "session-123",
                "2026-03-14T15:51:40.783Z");
            await WriteSummaryAsync(
                tempDirectory.FullName,
                "session-123",
                validExactTurn,
                new NotificationSummary
                {
                    SessionId = "session-123",
                    NotificationTurnId = validExactTurn.NotificationTurnId,
                    NotificationNonce = validExactTurn.NotificationNonce,
                    UpdatedAt = stopTimestamp,
                    Summary = "The latest valid exact summary waits behind older pending exact.",
                });
            string currentPath = AppPaths.GetCurrentStatePath(tempDirectory.FullName, "session-123");
            if (string.Equals(currentCacheState, "missing-current", StringComparison.Ordinal))
            {
                File.Delete(currentPath);
            }
            else
            {
                await File.WriteAllTextAsync(currentPath, "{");
            }

            _ = await service.HandleStopAsync(
                CreateJsonStream(
                    CreateStopInput(tempDirectory.FullName, stopTimestamp),
                    AppJsonSerializerContext.Default.StopHookInput),
                new MemoryStream(),
                CancellationToken.None);

            Assert.Empty(handler.Requests);
            Assert.False(File.Exists(AppPaths.GetNotificationRecordPath(
                tempDirectory.FullName,
                "session-123",
                validExactTurn.NotificationTurnId,
                CreateStopNotificationKeyForTest(stopTimestamp))));
            Assert.False(File.Exists(AppPaths.GetNotificationRecordPath(
                tempDirectory.FullName,
                "session-123",
                pendingExactTurn.NotificationTurnId,
                CreateStopNotificationKeyForTest(stopTimestamp))));
        }
        finally
        {
            tempDirectory.Delete(recursive: true);
        }
    }

    [Theory]
    [InlineData("missing-current")]
    [InlineData("corrupt-current")]
    public async Task HandleStopAsyncCachelessSingleOlderPendingExactSuppressesLatestNonExact(
        string currentCacheState)
    {
        DirectoryInfo tempDirectory = Directory.CreateTempSubdirectory();
        using EnvironmentScope environment = SetTelegramEnvironment();

        try
        {
            WorkspaceStateStore stateStore = new(
                TimeProvider.System,
                NullLogger<WorkspaceStateStore>.Instance);
            RecordingHttpMessageHandler handler = new();
            HookCommandService service = CreateHookCommandService(handler, stateStore);
            const string stopTimestamp = "2026-03-14T15:51:50.783Z";
            NotificationTurn pendingExactTurn = await CreateTurnAsync(
                stateStore,
                tempDirectory.FullName,
                "session-123",
                "2026-03-14T15:51:35.783Z");
            await WriteSummaryAsync(
                tempDirectory.FullName,
                "session-123",
                pendingExactTurn,
                new NotificationSummary
                {
                    SessionId = "session-123",
                    NotificationTurnId = pendingExactTurn.NotificationTurnId,
                    NotificationNonce = pendingExactTurn.NotificationNonce,
                    UpdatedAt = stopTimestamp,
                    Status = "pending",
                    Summary = " ",
                });
            NotificationTurn latestNonExactTurn = await CreateTurnAsync(
                stateStore,
                tempDirectory.FullName,
                "session-123",
                "2026-03-14T15:51:40.783Z");
            await WriteSummaryAsync(
                tempDirectory.FullName,
                "session-123",
                latestNonExactTurn,
                new NotificationSummary
                {
                    SessionId = "session-123",
                    NotificationTurnId = latestNonExactTurn.NotificationTurnId,
                    NotificationNonce = latestNonExactTurn.NotificationNonce,
                    UpdatedAt = "2026-03-14T15:51:49.783Z",
                    Summary = "The latest non-exact summary must wait behind older pending exact.",
                });
            string currentPath = AppPaths.GetCurrentStatePath(tempDirectory.FullName, "session-123");
            if (string.Equals(currentCacheState, "missing-current", StringComparison.Ordinal))
            {
                File.Delete(currentPath);
            }
            else
            {
                await File.WriteAllTextAsync(currentPath, "{");
            }

            _ = await service.HandleStopAsync(
                CreateJsonStream(
                    CreateStopInput(tempDirectory.FullName, stopTimestamp),
                    AppJsonSerializerContext.Default.StopHookInput),
                new MemoryStream(),
                CancellationToken.None);

            Assert.Empty(handler.Requests);
            await AssertPendingStopAsync(
                stateStore,
                tempDirectory.FullName,
                pendingExactTurn,
                stopTimestamp,
                "summary must be a non-empty human-readable sentence");
            Assert.False(File.Exists(AppPaths.GetNotificationRecordPath(
                tempDirectory.FullName,
                "session-123",
                latestNonExactTurn.NotificationTurnId,
                CreateStopNotificationKeyForTest(stopTimestamp))));
        }
        finally
        {
            tempDirectory.Delete(recursive: true);
        }
    }

    [Theory]
    [InlineData("missing-current")]
    [InlineData("corrupt-current")]
    public async Task HandleStopAsyncEqualCreatedAtExactEvidenceDoesNotDeliverOrAbandonPendingTurn(
        string currentCacheState)
    {
        DirectoryInfo tempDirectory = Directory.CreateTempSubdirectory();
        using EnvironmentScope environment = SetTelegramEnvironment();

        try
        {
            WorkspaceStateStore stateStore = new(
                TimeProvider.System,
                NullLogger<WorkspaceStateStore>.Instance);
            RecordingHttpMessageHandler handler = new();
            HookCommandService service = CreateHookCommandService(handler, stateStore);
            const string stopTimestamp = "2026-03-14T15:51:50.783Z";
            const string sharedCreatedAt = "2026-03-14T15:51:40.783Z";
            NotificationTurn validExactTurn = await CreateTurnAsync(
                stateStore,
                tempDirectory.FullName,
                "session-123",
                sharedCreatedAt);
            await WriteSummaryAsync(
                tempDirectory.FullName,
                "session-123",
                validExactTurn,
                new NotificationSummary
                {
                    SessionId = "session-123",
                    NotificationTurnId = validExactTurn.NotificationTurnId,
                    NotificationNonce = validExactTurn.NotificationNonce,
                    UpdatedAt = stopTimestamp,
                    Summary = "The equal-created valid exact summary must not be delivered arbitrarily.",
                });
            NotificationTurn pendingExactTurn = await CreateTurnAsync(
                stateStore,
                tempDirectory.FullName,
                "session-123",
                sharedCreatedAt);
            await WriteSummaryAsync(
                tempDirectory.FullName,
                "session-123",
                pendingExactTurn,
                new NotificationSummary
                {
                    SessionId = "session-123",
                    NotificationTurnId = pendingExactTurn.NotificationTurnId,
                    NotificationNonce = pendingExactTurn.NotificationNonce,
                    UpdatedAt = stopTimestamp,
                    Status = "pending",
                    Summary = " ",
                });
            string currentPath = AppPaths.GetCurrentStatePath(tempDirectory.FullName, "session-123");
            if (string.Equals(currentCacheState, "missing-current", StringComparison.Ordinal))
            {
                File.Delete(currentPath);
            }
            else
            {
                await File.WriteAllTextAsync(currentPath, "{");
            }

            _ = await service.HandleStopAsync(
                CreateJsonStream(
                    CreateStopInput(tempDirectory.FullName, stopTimestamp),
                    AppJsonSerializerContext.Default.StopHookInput),
                new MemoryStream(),
                CancellationToken.None);

            Assert.Empty(handler.Requests);
            await AssertPendingStopAsync(
                stateStore,
                tempDirectory.FullName,
                pendingExactTurn,
                stopTimestamp,
                "summary must be a non-empty human-readable sentence");
            NotificationTurn? storedValidTurn = await stateStore.TryReadTurnAsync(
                tempDirectory.FullName,
                "session-123",
                validExactTurn.NotificationTurnId,
                CancellationToken.None);
            Assert.Equal("open", storedValidTurn?.Status);
            Assert.False(File.Exists(AppPaths.GetNotificationRecordPath(
                tempDirectory.FullName,
                "session-123",
                validExactTurn.NotificationTurnId,
                CreateStopNotificationKeyForTest(stopTimestamp))));
        }
        finally
        {
            tempDirectory.Delete(recursive: true);
        }
    }

    [Theory]
    [InlineData("missing-summary")]
    [InlineData("corrupt-summary")]
    [InlineData("null-summary")]
    [InlineData("unreadable-summary")]
    public async Task HandleUserPromptSubmitAsyncEqualCreatedAtCurrentCacheDoesNotAbandonAmbiguousPendingHandoff(
        string pendingHandoffState)
    {
        DirectoryInfo tempDirectory = Directory.CreateTempSubdirectory();
        using EnvironmentScope environment = SetTelegramEnvironment();

        try
        {
            WorkspaceStateStore stateStore = new(
                TimeProvider.System,
                NullLogger<WorkspaceStateStore>.Instance);
            RecordingHttpMessageHandler handler = new();
            HookCommandService service = CreateHookCommandService(handler, stateStore);
            const string sharedCreatedAt = "2026-03-14T15:51:40.783Z";
            const string stopTimestamp = "2026-03-14T15:51:50.783Z";
            NotificationTurn pendingHandoffTurn = await CreateTurnAsync(
                stateStore,
                tempDirectory.FullName,
                "session-123",
                sharedCreatedAt);
            NotificationTurn cachedInvalidTurn = await CreateTurnAsync(
                stateStore,
                tempDirectory.FullName,
                "session-123",
                sharedCreatedAt);
            await WriteSummaryAsync(
                tempDirectory.FullName,
                "session-123",
                cachedInvalidTurn,
                new NotificationSummary
                {
                    SessionId = "session-123",
                    NotificationTurnId = cachedInvalidTurn.NotificationTurnId,
                    NotificationNonce = cachedInvalidTurn.NotificationNonce,
                    UpdatedAt = "2026-03-14T15:51:49.783Z",
                    Status = "completed",
                    Summary = " ",
                });
            await WritePendingHandoffSummaryStateAsync(
                tempDirectory.FullName,
                "session-123",
                pendingHandoffTurn,
                pendingHandoffState);
            await WriteCurrentStateAsync(
                tempDirectory.FullName,
                cachedInvalidTurn,
                "2026-03-14T15:51:49.783Z");

            _ = await service.HandleUserPromptSubmitAsync(
                CreateJsonStream(
                    new UserPromptSubmitHookInput
                    {
                        Cwd = tempDirectory.FullName,
                        SessionId = "session-123",
                        Timestamp = "2026-03-14T15:52:40.783Z",
                        TranscriptPath = "/workspace/transcript.json",
                        Prompt = "Start a superseding turn without resolving the tied pending handoff.",
                    },
                    AppJsonSerializerContext.Default.UserPromptSubmitHookInput),
                new MemoryStream(),
                CancellationToken.None);

            NotificationTurn? storedPendingHandoffTurn = await stateStore.TryReadTurnAsync(
                tempDirectory.FullName,
                "session-123",
                pendingHandoffTurn.NotificationTurnId,
                CancellationToken.None);
            Assert.Equal("open", storedPendingHandoffTurn?.Status);

            _ = await service.HandleStopAsync(
                CreateJsonStream(
                    CreateStopInput(tempDirectory.FullName, stopTimestamp),
                    AppJsonSerializerContext.Default.StopHookInput),
                new MemoryStream(),
                CancellationToken.None);

            Assert.Empty(handler.Requests);
            Assert.False(File.Exists(AppPaths.GetSessionNotificationRecordPath(
                tempDirectory.FullName,
                "session-123",
                CreateStopNotificationKeyForTest(stopTimestamp))));
        }
        finally
        {
            tempDirectory.Delete(recursive: true);
        }
    }

    [Fact]
    public async Task HandleStopAsyncReclaimsStaleTurnDeliveryClaimWithoutDurableRecord()
    {
        DirectoryInfo tempDirectory = Directory.CreateTempSubdirectory();
        using EnvironmentScope environment = SetTelegramEnvironment();

        try
        {
            WorkspaceStateStore stateStore = new(
                FixedUtcNow(),
                NullLogger<WorkspaceStateStore>.Instance);
            RecordingHttpMessageHandler handler = new();
            HookCommandService service = CreateHookCommandService(handler, stateStore);
            const string stopTimestamp = "2026-03-14T15:51:50.783Z";
            NotificationTurn turn = await CreateTurnAsync(
                stateStore,
                tempDirectory.FullName,
                "session-123",
                "2026-03-14T15:51:40.783Z");
            await WriteSummaryAsync(
                tempDirectory.FullName,
                "session-123",
                turn,
                new NotificationSummary
                {
                    SessionId = "session-123",
                    NotificationTurnId = turn.NotificationTurnId,
                    NotificationNonce = turn.NotificationNonce,
                    UpdatedAt = stopTimestamp,
                    Summary = "A stale turn delivery claim may be reclaimed.",
                });

            string turnClaimPath = AppPaths.GetTurnDeliveryClaimPath(
                tempDirectory.FullName,
                "session-123",
                turn.NotificationTurnId);
            await WriteClaimAsync(turnClaimPath, "not-a-timestamp");
            File.SetLastWriteTimeUtc(
                turnClaimPath,
                new DateTime(2026, 3, 14, 15, 40, 49, 783, DateTimeKind.Utc));

            _ = await service.HandleStopAsync(
                CreateJsonStream(
                    CreateStopInput(tempDirectory.FullName, stopTimestamp),
                    AppJsonSerializerContext.Default.StopHookInput),
                new MemoryStream(),
                CancellationToken.None);

            Assert.Single(handler.Requests);
            Assert.Equal(stopTimestamp, await File.ReadAllTextAsync(turnClaimPath));
            Assert.False(File.Exists(AppPaths.GetTurnDeliveryReclaimClaimPath(
                tempDirectory.FullName,
                "session-123",
                turn.NotificationTurnId)));
        }
        finally
        {
            tempDirectory.Delete(recursive: true);
        }
    }

    [Fact]
    public async Task HandleStopAsyncSkipsFreshSessionStopClaim()
    {
        DirectoryInfo tempDirectory = Directory.CreateTempSubdirectory();
        using EnvironmentScope environment = SetTelegramEnvironment();

        try
        {
            WorkspaceStateStore stateStore = new(
                FixedUtcNow(),
                NullLogger<WorkspaceStateStore>.Instance);
            RecordingHttpMessageHandler handler = new();
            HookCommandService service = CreateHookCommandService(handler, stateStore);
            const string stopTimestamp = "2026-03-14T15:51:50.783Z";
            NotificationTurn turn = await CreateTurnWithSummaryAsync(
                stateStore,
                tempDirectory.FullName,
                stopTimestamp,
                "A fresh session Stop claim must suppress delivery.");
            string notificationKey = CreateStopNotificationKeyForTest(stopTimestamp);
            string claimPath = AppPaths.GetSessionStopClaimPath(
                tempDirectory.FullName,
                "session-123",
                notificationKey);
            await WriteClaimAsync(claimPath, "2026-03-14T15:51:49.783Z");

            _ = await service.HandleStopAsync(
                CreateJsonStream(
                    CreateStopInput(tempDirectory.FullName, stopTimestamp),
                    AppJsonSerializerContext.Default.StopHookInput),
                new MemoryStream(),
                CancellationToken.None);

            Assert.Empty(handler.Requests);
            Assert.Equal("2026-03-14T15:51:49.783Z", await File.ReadAllTextAsync(claimPath));
            Assert.False(File.Exists(AppPaths.GetTurnDeliveryClaimPath(
                tempDirectory.FullName,
                "session-123",
                turn.NotificationTurnId)));
        }
        finally
        {
            tempDirectory.Delete(recursive: true);
        }
    }

    [Fact]
    public async Task HandleStopAsyncReclaimsStaleSessionStopClaimWithoutDurableRecord()
    {
        DirectoryInfo tempDirectory = Directory.CreateTempSubdirectory();
        using EnvironmentScope environment = SetTelegramEnvironment();

        try
        {
            WorkspaceStateStore stateStore = new(
                FixedUtcNow(),
                NullLogger<WorkspaceStateStore>.Instance);
            RecordingHttpMessageHandler handler = new();
            HookCommandService service = CreateHookCommandService(handler, stateStore);
            const string stopTimestamp = "2026-03-14T15:51:50.783Z";
            _ = await CreateTurnWithSummaryAsync(
                stateStore,
                tempDirectory.FullName,
                stopTimestamp,
                "A stale session Stop claim may be reclaimed.");
            string notificationKey = CreateStopNotificationKeyForTest(stopTimestamp);
            string claimPath = AppPaths.GetSessionStopClaimPath(
                tempDirectory.FullName,
                "session-123",
                notificationKey);
            await WriteClaimAsync(claimPath, "2026-03-14T15:40:49.783Z");

            _ = await service.HandleStopAsync(
                CreateJsonStream(
                    CreateStopInput(tempDirectory.FullName, stopTimestamp),
                    AppJsonSerializerContext.Default.StopHookInput),
                new MemoryStream(),
                CancellationToken.None);

            Assert.Single(handler.Requests);
            Assert.Equal(stopTimestamp, await File.ReadAllTextAsync(claimPath));
            Assert.False(File.Exists(AppPaths.GetSessionStopReclaimClaimPath(
                tempDirectory.FullName,
                "session-123",
                notificationKey)));
        }
        finally
        {
            tempDirectory.Delete(recursive: true);
        }
    }

    [Theory]
    [InlineData("")]
    [InlineData("not-a-timestamp")]
    public async Task HandleStopAsyncSkipsFreshMalformedSessionStopClaim(string claimContent)
    {
        DirectoryInfo tempDirectory = Directory.CreateTempSubdirectory();
        using EnvironmentScope environment = SetTelegramEnvironment();

        try
        {
            WorkspaceStateStore stateStore = new(
                FixedUtcNow(),
                NullLogger<WorkspaceStateStore>.Instance);
            RecordingHttpMessageHandler handler = new();
            HookCommandService service = CreateHookCommandService(handler, stateStore);
            const string stopTimestamp = "2026-03-14T15:51:50.783Z";
            _ = await CreateTurnWithSummaryAsync(
                stateStore,
                tempDirectory.FullName,
                stopTimestamp,
                "A fresh malformed session Stop claim must suppress delivery.");
            string notificationKey = CreateStopNotificationKeyForTest(stopTimestamp);
            string claimPath = AppPaths.GetSessionStopClaimPath(
                tempDirectory.FullName,
                "session-123",
                notificationKey);
            await WriteClaimAsync(claimPath, claimContent);
            File.SetLastWriteTimeUtc(
                claimPath,
                new DateTime(2026, 3, 14, 15, 51, 49, 783, DateTimeKind.Utc));

            _ = await service.HandleStopAsync(
                CreateJsonStream(
                    CreateStopInput(tempDirectory.FullName, stopTimestamp),
                    AppJsonSerializerContext.Default.StopHookInput),
                new MemoryStream(),
                CancellationToken.None);

            Assert.Empty(handler.Requests);
            Assert.Equal(claimContent, await File.ReadAllTextAsync(claimPath));
        }
        finally
        {
            tempDirectory.Delete(recursive: true);
        }
    }

    [Theory]
    [InlineData("")]
    [InlineData("not-a-timestamp")]
    public async Task HandleStopAsyncReclaimsStaleMalformedSessionStopClaim(
        string claimContent)
    {
        DirectoryInfo tempDirectory = Directory.CreateTempSubdirectory();
        using EnvironmentScope environment = SetTelegramEnvironment();

        try
        {
            WorkspaceStateStore stateStore = new(
                FixedUtcNow(),
                NullLogger<WorkspaceStateStore>.Instance);
            RecordingHttpMessageHandler handler = new();
            HookCommandService service = CreateHookCommandService(handler, stateStore);
            const string stopTimestamp = "2026-03-14T15:51:50.783Z";
            _ = await CreateTurnWithSummaryAsync(
                stateStore,
                tempDirectory.FullName,
                stopTimestamp,
                "A stale malformed session Stop claim may be reclaimed.");
            string notificationKey = CreateStopNotificationKeyForTest(stopTimestamp);
            string claimPath = AppPaths.GetSessionStopClaimPath(
                tempDirectory.FullName,
                "session-123",
                notificationKey);
            await WriteClaimAsync(claimPath, claimContent);
            File.SetLastWriteTimeUtc(
                claimPath,
                new DateTime(2026, 3, 14, 15, 40, 49, 783, DateTimeKind.Utc));

            _ = await service.HandleStopAsync(
                CreateJsonStream(
                    CreateStopInput(tempDirectory.FullName, stopTimestamp),
                    AppJsonSerializerContext.Default.StopHookInput),
                new MemoryStream(),
                CancellationToken.None);

            Assert.Single(handler.Requests);
            Assert.Equal(stopTimestamp, await File.ReadAllTextAsync(claimPath));
            Assert.False(File.Exists(AppPaths.GetSessionStopReclaimClaimPath(
                tempDirectory.FullName,
                "session-123",
                notificationKey)));
        }
        finally
        {
            tempDirectory.Delete(recursive: true);
        }
    }

    [Theory]
    [InlineData("sent")]
    [InlineData("partial")]
    public async Task HandleStopAsyncDoesNotReclaimStaleSessionStopClaimWithDurableRecord(
        string deliveryStatus)
    {
        DirectoryInfo tempDirectory = Directory.CreateTempSubdirectory();
        using EnvironmentScope environment = SetTelegramEnvironment();

        try
        {
            WorkspaceStateStore stateStore = new(
                FixedUtcNow(),
                NullLogger<WorkspaceStateStore>.Instance);
            RecordingHttpMessageHandler handler = new();
            HookCommandService service = CreateHookCommandService(handler, stateStore);
            const string stopTimestamp = "2026-03-14T15:51:50.783Z";
            NotificationTurn turn = await CreateTurnWithSummaryAsync(
                stateStore,
                tempDirectory.FullName,
                stopTimestamp,
                "A durable record must suppress session Stop claim reclaim.");
            string notificationKey = CreateStopNotificationKeyForTest(stopTimestamp);
            string claimPath = AppPaths.GetSessionStopClaimPath(
                tempDirectory.FullName,
                "session-123",
                notificationKey);
            await WriteClaimAsync(claimPath, "2026-03-14T15:40:49.783Z");
            await WriteNotificationRecordAsync(
                AppPaths.GetNotificationRecordPath(
                    tempDirectory.FullName,
                    "session-123",
                    turn.NotificationTurnId,
                    notificationKey),
                new NotificationRecord
                {
                    SessionId = "session-123",
                    NotificationTurnId = turn.NotificationTurnId,
                    NotificationKey = notificationKey,
                    WorkspacePath = tempDirectory.FullName,
                    StopTimestamp = stopTimestamp,
                    SentAt = "2026-03-14T15:51:51.783Z",
                    DeliveryStatus = deliveryStatus,
                    SuccessfulMessageCount = deliveryStatus == "partial" ? 1 : null,
                });

            _ = await service.HandleStopAsync(
                CreateJsonStream(
                    CreateStopInput(tempDirectory.FullName, stopTimestamp),
                    AppJsonSerializerContext.Default.StopHookInput),
                new MemoryStream(),
                CancellationToken.None);

            Assert.Empty(handler.Requests);
            Assert.Equal("2026-03-14T15:40:49.783Z", await File.ReadAllTextAsync(claimPath));
            Assert.False(File.Exists(AppPaths.GetSessionStopReclaimClaimPath(
                tempDirectory.FullName,
                "session-123",
                notificationKey)));
        }
        finally
        {
            tempDirectory.Delete(recursive: true);
        }
    }

    [Theory]
    [InlineData("")]
    [InlineData("not-a-timestamp")]
    public async Task HandleStopAsyncSkipsFreshTurnDeliveryReclaimLock(string reclaimContent)
    {
        DirectoryInfo tempDirectory = Directory.CreateTempSubdirectory();
        using EnvironmentScope environment = SetTelegramEnvironment();

        try
        {
            WorkspaceStateStore stateStore = new(
                FixedUtcNow(),
                NullLogger<WorkspaceStateStore>.Instance);
            RecordingHttpMessageHandler handler = new();
            HookCommandService service = CreateHookCommandService(handler, stateStore);
            const string stopTimestamp = "2026-03-14T15:51:50.783Z";
            NotificationTurn turn = await CreateTurnWithSummaryAsync(
                stateStore,
                tempDirectory.FullName,
                stopTimestamp,
                "A fresh reclaim lock must suppress reclaim.");
            string turnClaimPath = AppPaths.GetTurnDeliveryClaimPath(
                tempDirectory.FullName,
                "session-123",
                turn.NotificationTurnId);
            string reclaimPath = AppPaths.GetTurnDeliveryReclaimClaimPath(
                tempDirectory.FullName,
                "session-123",
                turn.NotificationTurnId);
            await WriteClaimAsync(turnClaimPath, "2026-03-14T15:40:49.783Z");
            await WriteClaimAsync(reclaimPath, reclaimContent);
            File.SetLastWriteTimeUtc(
                reclaimPath,
                new DateTime(2026, 3, 14, 15, 51, 49, 783, DateTimeKind.Utc));

            _ = await service.HandleStopAsync(
                CreateJsonStream(
                    CreateStopInput(tempDirectory.FullName, stopTimestamp),
                    AppJsonSerializerContext.Default.StopHookInput),
                new MemoryStream(),
                CancellationToken.None);

            Assert.Empty(handler.Requests);
            Assert.Equal("2026-03-14T15:40:49.783Z", await File.ReadAllTextAsync(turnClaimPath));
            Assert.Equal(reclaimContent, await File.ReadAllTextAsync(reclaimPath));
        }
        finally
        {
            tempDirectory.Delete(recursive: true);
        }
    }

    [Theory]
    [InlineData("")]
    [InlineData("not-a-timestamp")]
    public async Task HandleStopAsyncRecoversStaleTurnDeliveryReclaimLock(string reclaimContent)
    {
        DirectoryInfo tempDirectory = Directory.CreateTempSubdirectory();
        using EnvironmentScope environment = SetTelegramEnvironment();

        try
        {
            WorkspaceStateStore stateStore = new(
                FixedUtcNow(),
                NullLogger<WorkspaceStateStore>.Instance);
            RecordingHttpMessageHandler handler = new();
            HookCommandService service = CreateHookCommandService(handler, stateStore);
            const string stopTimestamp = "2026-03-14T15:51:50.783Z";
            NotificationTurn turn = await CreateTurnWithSummaryAsync(
                stateStore,
                tempDirectory.FullName,
                stopTimestamp,
                "A stale reclaim lock may be recovered.");
            string turnClaimPath = AppPaths.GetTurnDeliveryClaimPath(
                tempDirectory.FullName,
                "session-123",
                turn.NotificationTurnId);
            string reclaimPath = AppPaths.GetTurnDeliveryReclaimClaimPath(
                tempDirectory.FullName,
                "session-123",
                turn.NotificationTurnId);
            await WriteClaimAsync(turnClaimPath, "2026-03-14T15:40:49.783Z");
            await WriteClaimAsync(reclaimPath, reclaimContent);
            File.SetLastWriteTimeUtc(
                reclaimPath,
                new DateTime(2026, 3, 14, 15, 40, 49, 783, DateTimeKind.Utc));

            _ = await service.HandleStopAsync(
                CreateJsonStream(
                    CreateStopInput(tempDirectory.FullName, stopTimestamp),
                    AppJsonSerializerContext.Default.StopHookInput),
                new MemoryStream(),
                CancellationToken.None);

            Assert.Single(handler.Requests);
            Assert.Equal(stopTimestamp, await File.ReadAllTextAsync(turnClaimPath));
            Assert.False(File.Exists(reclaimPath));
        }
        finally
        {
            tempDirectory.Delete(recursive: true);
        }
    }

    [Fact]
    public async Task HandleStopAsyncReleasesTurnDeliveryClaimAfterZeroSuccessFailure()
    {
        DirectoryInfo tempDirectory = Directory.CreateTempSubdirectory();
        using EnvironmentScope environment = SetTelegramEnvironment();

        try
        {
            WorkspaceStateStore stateStore = new(
                TimeProvider.System,
                NullLogger<WorkspaceStateStore>.Instance);
            RecordingHttpMessageHandler handler = new(
                [
                    RecordingHttpMessageHandler.CreateJsonResponse(
                        HttpStatusCode.BadRequest,
                        """{"ok":false,"description":"bad request"}"""),
                    RecordingHttpMessageHandler.CreateJsonResponse(
                        HttpStatusCode.OK,
                        """{"ok":true}"""),
                ]);
            HookCommandService service = CreateHookCommandService(handler, stateStore);
            const string stopTimestamp = "2026-03-14T15:51:50.783Z";
            NotificationTurn turn = await CreateTurnAsync(
                stateStore,
                tempDirectory.FullName,
                "session-123",
                "2026-03-14T15:51:40.783Z");
            await WriteSummaryAsync(
                tempDirectory.FullName,
                "session-123",
                turn,
                new NotificationSummary
                {
                    SessionId = "session-123",
                    NotificationTurnId = turn.NotificationTurnId,
                    NotificationNonce = turn.NotificationNonce,
                    UpdatedAt = stopTimestamp,
                    Summary = "Retry after zero successful Telegram messages.",
                });

            _ = await service.HandleStopAsync(
                CreateJsonStream(
                    CreateStopInput(tempDirectory.FullName, stopTimestamp),
                    AppJsonSerializerContext.Default.StopHookInput),
                new MemoryStream(),
                CancellationToken.None);

            string turnClaimPath = AppPaths.GetTurnDeliveryClaimPath(
                tempDirectory.FullName,
                "session-123",
                turn.NotificationTurnId);
            Assert.False(File.Exists(turnClaimPath));

            _ = await service.HandleStopAsync(
                CreateJsonStream(
                    CreateStopInput(tempDirectory.FullName, stopTimestamp),
                    AppJsonSerializerContext.Default.StopHookInput),
                new MemoryStream(),
                CancellationToken.None);

            Assert.Equal(2, handler.Requests.Count);
            Assert.True(File.Exists(turnClaimPath));
        }
        finally
        {
            tempDirectory.Delete(recursive: true);
        }
    }

    [Fact]
    public async Task HandleStopAsyncKeepsClaimAfterPartialMultiMessageSendFailure()
    {
        DirectoryInfo tempDirectory = Directory.CreateTempSubdirectory();
        using EnvironmentScope environment = SetTelegramEnvironment();

        try
        {
            WorkspaceStateStore stateStore = new(
                TimeProvider.System,
                NullLogger<WorkspaceStateStore>.Instance);
            RecordingHttpMessageHandler handler = new(
                [
                    RecordingHttpMessageHandler.CreateJsonResponse(
                        HttpStatusCode.OK,
                        """{"ok":true}"""),
                    RecordingHttpMessageHandler.CreateJsonResponse(
                        HttpStatusCode.BadGateway,
                        """{"ok":false,"description":"temporary failure"}"""),
                    RecordingHttpMessageHandler.CreateJsonResponse(
                        HttpStatusCode.OK,
                        """{"ok":true}"""),
                ]);
            HookCommandService service = CreateHookCommandService(handler, stateStore);
            const string stopTimestamp = "2026-03-14T15:51:50.783Z";
            NotificationTurn turn = await CreateTurnAsync(
                stateStore,
                tempDirectory.FullName,
                "session-123",
                "2026-03-14T15:51:40.783Z");
            await WriteSummaryAsync(
                tempDirectory.FullName,
                "session-123",
                turn,
                new NotificationSummary
                {
                    SessionId = "session-123",
                    NotificationTurnId = turn.NotificationTurnId,
                    NotificationNonce = turn.NotificationNonce,
                    UpdatedAt = stopTimestamp,
                    Summary = string.Join(
                        Environment.NewLine,
                        Enumerable.Repeat(
                            "This long summary forces multiple Telegram messages.",
                            260)),
                });

            _ = await service.HandleStopAsync(
                CreateJsonStream(
                    CreateStopInput(tempDirectory.FullName, stopTimestamp),
                    AppJsonSerializerContext.Default.StopHookInput),
                new MemoryStream(),
                CancellationToken.None);
            _ = await service.HandleStopAsync(
                CreateJsonStream(
                    CreateStopInput(tempDirectory.FullName, stopTimestamp),
                    AppJsonSerializerContext.Default.StopHookInput),
                new MemoryStream(),
                CancellationToken.None);

            Assert.Equal(2, handler.Requests.Count);
            NotificationTurn? storedTurn = await stateStore.TryReadTurnAsync(
                tempDirectory.FullName,
                "session-123",
                turn.NotificationTurnId,
                CancellationToken.None);
            Assert.Equal("notified", storedTurn?.Status);

            NotificationRecord partialRecord = await ReadNotificationRecordAsync(
                AppPaths.GetNotificationRecordPath(
                    tempDirectory.FullName,
                    "session-123",
                    turn.NotificationTurnId,
                    CreateStopNotificationKeyForTest(stopTimestamp)));
            Assert.Equal("partial", partialRecord.DeliveryStatus);
            Assert.Equal(1, partialRecord.SuccessfulMessageCount);
            Assert.True(File.Exists(AppPaths.GetSessionStopClaimPath(
                tempDirectory.FullName,
                "session-123",
                CreateStopNotificationKeyForTest(stopTimestamp))));

            _ = await service.HandleStopAsync(
                CreateJsonStream(
                    CreateStopInput(tempDirectory.FullName, "2026-03-14T15:52:50.783Z"),
                    AppJsonSerializerContext.Default.StopHookInput),
                new MemoryStream(),
                CancellationToken.None);

            Assert.Equal(2, handler.Requests.Count);
            Assert.False(File.Exists(AppPaths.GetSessionNotificationRecordPath(
                tempDirectory.FullName,
                "session-123",
                CreateStopNotificationKeyForTest("2026-03-14T15:52:50.783Z"))));
        }
        finally
        {
            tempDirectory.Delete(recursive: true);
        }
    }

    [Theory]
    [InlineData("sent")]
    [InlineData("partial")]
    public async Task HandleStopAsyncDoesNotResendOpenTurnWithDurableDeliveryRecord(
        string deliveryStatus)
    {
        DirectoryInfo tempDirectory = Directory.CreateTempSubdirectory();
        using EnvironmentScope environment = SetTelegramEnvironment();

        try
        {
            WorkspaceStateStore stateStore = new(
                TimeProvider.System,
                NullLogger<WorkspaceStateStore>.Instance);
            RecordingHttpMessageHandler handler = new();
            HookCommandService service = CreateHookCommandService(handler, stateStore);
            NotificationTurn turn = await CreateTurnAsync(
                stateStore,
                tempDirectory.FullName,
                "session-123",
                "2026-03-14T15:51:40.783Z");
            await WriteSummaryAsync(
                tempDirectory.FullName,
                "session-123",
                turn,
                new NotificationSummary
                {
                    SessionId = "session-123",
                    NotificationTurnId = turn.NotificationTurnId,
                    NotificationNonce = turn.NotificationNonce,
                    UpdatedAt = "2026-03-14T15:51:50.783Z",
                    Summary = "This already-delivered turn must not be resent.",
                });
            string staleClaimPath = AppPaths.GetTurnDeliveryClaimPath(
                tempDirectory.FullName,
                "session-123",
                turn.NotificationTurnId);
            string staleReclaimPath = AppPaths.GetTurnDeliveryReclaimClaimPath(
                tempDirectory.FullName,
                "session-123",
                turn.NotificationTurnId);
            await WriteClaimAsync(staleClaimPath, "not-a-timestamp");
            File.SetLastWriteTimeUtc(
                staleClaimPath,
                new DateTime(2026, 3, 14, 15, 40, 49, 783, DateTimeKind.Utc));
            await WriteClaimAsync(staleReclaimPath, "not-a-timestamp");
            File.SetLastWriteTimeUtc(
                staleReclaimPath,
                new DateTime(2026, 3, 14, 15, 40, 49, 783, DateTimeKind.Utc));
            await WriteNotificationRecordAsync(
                AppPaths.GetNotificationRecordPath(
                    tempDirectory.FullName,
                    "session-123",
                    turn.NotificationTurnId,
                    CreateStopNotificationKeyForTest("2026-03-14T15:51:50.783Z")),
                new NotificationRecord
                {
                    SessionId = "session-123",
                    NotificationTurnId = turn.NotificationTurnId,
                    NotificationKey = CreateStopNotificationKeyForTest("2026-03-14T15:51:50.783Z"),
                    WorkspacePath = tempDirectory.FullName,
                    StopTimestamp = "2026-03-14T15:51:50.783Z",
                    SentAt = "2026-03-14T15:51:51.783Z",
                    DeliveryStatus = deliveryStatus,
                    SuccessfulMessageCount = deliveryStatus == "partial" ? 1 : null,
                });

            _ = await service.HandleStopAsync(
                CreateJsonStream(
                    CreateStopInput(tempDirectory.FullName, "2026-03-14T15:52:50.783Z"),
                    AppJsonSerializerContext.Default.StopHookInput),
                new MemoryStream(),
                CancellationToken.None);
            _ = await service.HandleStopAsync(
                CreateJsonStream(
                    CreateStopInput(tempDirectory.FullName, "2026-03-14T15:52:50.783Z"),
                    AppJsonSerializerContext.Default.StopHookInput),
                new MemoryStream(),
                CancellationToken.None);

            Assert.Empty(handler.Requests);
            Assert.False(File.Exists(AppPaths.GetNotificationRecordPath(
                tempDirectory.FullName,
                "session-123",
                turn.NotificationTurnId,
                CreateStopNotificationKeyForTest("2026-03-14T15:52:50.783Z"))));

            NotificationTurn? storedTurn = await stateStore.TryReadTurnAsync(
                tempDirectory.FullName,
                "session-123",
                turn.NotificationTurnId,
                CancellationToken.None);
            Assert.Equal("open", storedTurn?.Status);
            Assert.Equal(
                "not-a-timestamp",
                await File.ReadAllTextAsync(staleClaimPath));
            Assert.Equal(
                "not-a-timestamp",
                await File.ReadAllTextAsync(staleReclaimPath));
        }
        finally
        {
            tempDirectory.Delete(recursive: true);
        }
    }

    private static async Task<NotificationTurn> CreateTurnAsync(
        WorkspaceStateStore stateStore,
        string workspacePath,
        string sessionId,
        string timestamp = "2026-03-14T15:51:40.783Z")
    {
        UserPromptSubmitHookInput input = new()
        {
            Cwd = workspacePath,
            SessionId = sessionId,
            Timestamp = timestamp,
            TranscriptPath = "/workspace/transcript.json",
            Prompt = "Ship the change.",
        };
        PromptObservation observation = await stateStore.RecordPromptObservationAsync(
            input,
            new PromptClassification("main-user-prompt", "test"),
            CancellationToken.None);
        return await stateStore.CreateNotificationTurnAsync(
            input,
            observation,
            CancellationToken.None);
    }

    private static async Task<NotificationTurn> CreateTurnWithSummaryAsync(
        WorkspaceStateStore stateStore,
        string workspacePath,
        string stopTimestamp,
        string summaryText)
    {
        NotificationTurn turn = await CreateTurnAsync(
            stateStore,
            workspacePath,
            "session-123",
            "2026-03-14T15:51:40.783Z");
        await WriteSummaryAsync(
            workspacePath,
            "session-123",
            turn,
            new NotificationSummary
            {
                SessionId = "session-123",
                NotificationTurnId = turn.NotificationTurnId,
                NotificationNonce = turn.NotificationNonce,
                UpdatedAt = stopTimestamp,
                Summary = summaryText,
            });
        return turn;
    }

    private static StopHookInput CreateStopInput(
        string workspacePath,
        string timestamp = "2026-03-14T15:51:50.783Z")
        => new()
        {
            Cwd = workspacePath,
            SessionId = "session-123",
            Timestamp = timestamp,
            TranscriptPath = "/workspace/transcript.json",
        };

    private static string CreateStopNotificationKeyForTest(string timestamp)
    {
        byte[] hash = SHA256.HashData(Encoding.UTF8.GetBytes(timestamp));
        return $"stop-{Convert.ToHexString(hash)[..32].ToLowerInvariant()}";
    }

    private static HookCommandService CreateHookCommandService(
        HttpMessageHandler handler,
        WorkspaceStateStore? stateStore = null,
        ILoggerFactory? loggerFactory = null,
        SessionLogFileContext? logContext = null,
        HookSurface? surface = null)
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
            new HookExecutionContext(surface),
            CreateLogger<HookCommandService>(loggerFactory));
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

    private static async Task<HookResponse> DeserializeHookResponseAsync(MemoryStream output)
    {
        output.Position = 0;
        return await JsonSerializer.DeserializeAsync(
                output,
                AppJsonSerializerContext.Default.HookResponse,
                CancellationToken.None)
            ?? throw new InvalidOperationException("Expected a valid hook response.");
    }

    private static async Task<CopilotCliHookOutput> DeserializeCopilotCliHookOutputAsync(
        MemoryStream output)
    {
        output.Position = 0;
        return await JsonSerializer.DeserializeAsync(
                output,
                AppJsonSerializerContext.Default.CopilotCliHookOutput,
                CancellationToken.None)
            ?? throw new InvalidOperationException("Expected a valid Copilot CLI hook output.");
    }

    private static JsonElement ReadJsonRootElement(MemoryStream output)
    {
        using JsonDocument document = JsonDocument.Parse(output.ToArray());
        return document.RootElement.Clone();
    }

    private static void AssertJsonProperties(JsonElement element, params string[] expectedNames)
    {
        string[] actualNames = element.EnumerateObject()
            .Select(static property => property.Name)
            .OrderBy(static name => name, StringComparer.Ordinal)
            .ToArray();
        Assert.Equal(
            expectedNames.Order(StringComparer.Ordinal).ToArray(),
            actualNames);
    }

    private static async Task WriteSummaryAsync(
        string workspacePath,
        string sessionId,
        NotificationTurn turn,
        NotificationSummary summary)
    {
        string summaryPath = AppPaths.GetSummaryStatePath(
            workspacePath,
            sessionId,
            turn.NotificationTurnId);
        Directory.CreateDirectory(Path.GetDirectoryName(summaryPath)!);
        await using FileStream stream = File.Create(summaryPath);
        await JsonSerializer.SerializeAsync(
            stream,
            summary,
            AppJsonSerializerContext.Default.NotificationSummary,
            CancellationToken.None);
    }

    private static async Task RecordSentNotificationAsync(
        string workspacePath,
        NotificationTurn turn,
        string notificationKey,
        string stopTimestamp)
    {
        await WorkspaceStateStore.RecordNotificationAsync(
            AppPaths.GetNotificationRecordPath(
                Path.GetFullPath(workspacePath),
                turn.SessionId,
                turn.NotificationTurnId,
                notificationKey),
            new NotificationRecord
            {
                SessionId = turn.SessionId,
                NotificationTurnId = turn.NotificationTurnId,
                NotificationKey = notificationKey,
                WorkspacePath = Path.GetFullPath(workspacePath),
                StopTimestamp = stopTimestamp,
                SentAt = stopTimestamp,
                DeliveryStatus = "sent",
            },
            CancellationToken.None);
    }

    private static async Task WriteTurnStateAsync(string workspacePath, NotificationTurn turn)
        => await File.WriteAllTextAsync(
            AppPaths.GetTurnStatePath(
                workspacePath,
                turn.SessionId,
                turn.NotificationTurnId),
            JsonSerializer.Serialize(
                turn,
                AppJsonSerializerContext.Default.NotificationTurn));

    private static async Task WriteInvalidSummaryAsync(
        string workspacePath,
        string sessionId,
        NotificationTurn turn,
        string invalidSummaryKind)
    {
        if (string.Equals(invalidSummaryKind, "empty-object", StringComparison.Ordinal))
        {
            await WriteRawSummaryJsonAsync(workspacePath, sessionId, turn, "{}");
            return;
        }

        NotificationSummary summary = invalidSummaryKind switch
        {
            "missing-updated-at" => new NotificationSummary
            {
                SessionId = sessionId,
                NotificationTurnId = turn.NotificationTurnId,
                NotificationNonce = turn.NotificationNonce,
                UpdatedAt = null,
                Summary = "This summary is readable but has no timestamp.",
            },
            "invalid-updated-at" => new NotificationSummary
            {
                SessionId = sessionId,
                NotificationTurnId = turn.NotificationTurnId,
                NotificationNonce = turn.NotificationNonce,
                UpdatedAt = "not-a-timestamp",
                Summary = "This summary is readable but has an invalid timestamp.",
            },
            "wrong-session" => new NotificationSummary
            {
                SessionId = "other-session",
                NotificationTurnId = turn.NotificationTurnId,
                NotificationNonce = turn.NotificationNonce,
                UpdatedAt = "2026-03-14T15:51:50.783Z",
                Summary = "This summary is readable but assigned to another session.",
            },
            "wrong-turn-id" => new NotificationSummary
            {
                SessionId = sessionId,
                NotificationTurnId = "other-turn",
                NotificationNonce = turn.NotificationNonce,
                UpdatedAt = "2026-03-14T15:51:50.783Z",
                Summary = "This summary is readable but assigned to another turn.",
            },
            "wrong-nonce" => new NotificationSummary
            {
                SessionId = sessionId,
                NotificationTurnId = turn.NotificationTurnId,
                NotificationNonce = "other-nonce",
                UpdatedAt = "2026-03-14T15:51:50.783Z",
                Summary = "This summary is readable but has the wrong nonce.",
            },
            _ => throw new ArgumentOutOfRangeException(
                nameof(invalidSummaryKind),
                invalidSummaryKind,
                "Unexpected invalid summary kind."),
        };
        await WriteSummaryAsync(workspacePath, sessionId, turn, summary);
    }

    private static async Task WriteRawSummaryJsonAsync(
        string workspacePath,
        string sessionId,
        NotificationTurn turn,
        string json)
    {
        string summaryPath = AppPaths.GetSummaryStatePath(
            workspacePath,
            sessionId,
            turn.NotificationTurnId);
        Directory.CreateDirectory(Path.GetDirectoryName(summaryPath)!);
        await File.WriteAllTextAsync(summaryPath, json);
    }

    private static async Task WritePendingHandoffSummaryStateAsync(
        string workspacePath,
        string sessionId,
        NotificationTurn turn,
        string pendingHandoffState)
    {
        string summaryPath = AppPaths.GetSummaryStatePath(
            workspacePath,
            sessionId,
            turn.NotificationTurnId);
        if (string.Equals(pendingHandoffState, "missing-summary", StringComparison.Ordinal))
        {
            File.Delete(summaryPath);
            return;
        }

        if (string.Equals(pendingHandoffState, "corrupt-summary", StringComparison.Ordinal))
        {
            await WriteRawSummaryJsonAsync(workspacePath, sessionId, turn, "{");
            return;
        }

        if (string.Equals(pendingHandoffState, "null-summary", StringComparison.Ordinal))
        {
            await WriteRawSummaryJsonAsync(workspacePath, sessionId, turn, "null");
            return;
        }

        if (string.Equals(pendingHandoffState, "unreadable-summary", StringComparison.Ordinal))
        {
            File.Delete(summaryPath);
            Directory.CreateDirectory(summaryPath);
            return;
        }

        throw new ArgumentOutOfRangeException(
            nameof(pendingHandoffState),
            pendingHandoffState,
            "Unexpected pending handoff state.");
    }

    private static async Task WriteCurrentStateAsync(
        string workspacePath,
        NotificationTurn turn,
        string updatedAt)
    {
        string currentPath = AppPaths.GetCurrentStatePath(workspacePath, turn.SessionId);
        Directory.CreateDirectory(Path.GetDirectoryName(currentPath)!);
        await File.WriteAllTextAsync(
            currentPath,
            JsonSerializer.Serialize(
                new CurrentNotificationState
                {
                    SessionId = turn.SessionId,
                    NotificationTurnId = turn.NotificationTurnId,
                    NotificationNonce = turn.NotificationNonce,
                    SummaryPath = AppPaths.GetSummaryStatePath(
                        workspacePath,
                        turn.SessionId,
                        turn.NotificationTurnId),
                    UpdatedAt = updatedAt,
                },
                AppJsonSerializerContext.Default.CurrentNotificationState));
    }

    private static async Task<NotificationRecord> ReadNotificationRecordAsync(string path)
    {
        await using FileStream stream = File.OpenRead(path);
        return await JsonSerializer.DeserializeAsync(
                stream,
                AppJsonSerializerContext.Default.NotificationRecord,
                CancellationToken.None)
            ?? throw new InvalidOperationException("Expected a notification record.");
    }

    private static async Task<StopObservation> ReadStopObservationAsync(string path)
    {
        await using FileStream stream = File.OpenRead(path);
        return await JsonSerializer.DeserializeAsync(
                stream,
                AppJsonSerializerContext.Default.StopObservation,
                CancellationToken.None)
            ?? throw new InvalidOperationException("Expected a Stop observation.");
    }

    private static async Task AssertPendingStopAsync(
        WorkspaceStateStore stateStore,
        string workspacePath,
        NotificationTurn turn,
        string stopTimestamp,
        string expectedFailureReason)
    {
        string notificationKey = CreateStopNotificationKeyForTest(stopTimestamp);
        StopObservation observation = await ReadStopObservationAsync(
            AppPaths.GetStopObservationPath(
                workspacePath,
                "session-123",
                turn.NotificationTurnId,
                notificationKey));

        Assert.False(observation.SummaryValid);
        Assert.True(observation.SummaryPendingHandoff);
        Assert.Contains(
            expectedFailureReason,
            observation.SummaryFailureReason,
            StringComparison.Ordinal);
        Assert.False(File.Exists(AppPaths.GetSessionStopClaimPath(
            workspacePath,
            "session-123",
            notificationKey)));
        Assert.False(File.Exists(AppPaths.GetTurnDeliveryClaimPath(
            workspacePath,
            "session-123",
            turn.NotificationTurnId)));
        Assert.False(File.Exists(AppPaths.GetSessionNotificationRecordPath(
            workspacePath,
            "session-123",
            notificationKey)));
        Assert.False(File.Exists(AppPaths.GetNotificationRecordPath(
            workspacePath,
            "session-123",
            turn.NotificationTurnId,
            notificationKey)));

        NotificationTurn? storedTurn = await stateStore.TryReadTurnAsync(
            workspacePath,
            "session-123",
            turn.NotificationTurnId,
            CancellationToken.None);
        Assert.Equal("open", storedTurn?.Status);
    }

    private static async Task WriteNotificationRecordAsync(
        string path,
        NotificationRecord record)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        await using FileStream stream = File.Create(path);
        await JsonSerializer.SerializeAsync(
            stream,
            record,
            AppJsonSerializerContext.Default.NotificationRecord,
            CancellationToken.None);
    }

    private static async Task WriteClaimAsync(string path, string claimedAt)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        await File.WriteAllTextAsync(path, claimedAt);
    }

    private static EnvironmentScope SetTelegramEnvironment()
    {
        string? originalBotToken = Environment.GetEnvironmentVariable(
            AppConstants.TelegramBotTokenEnvironmentVariable);
        string? originalChatId = Environment.GetEnvironmentVariable(
            AppConstants.TelegramChatIdEnvironmentVariable);
        Environment.SetEnvironmentVariable(
            AppConstants.TelegramBotTokenEnvironmentVariable,
            "123456:ABCdef_token");
        Environment.SetEnvironmentVariable(
            AppConstants.TelegramChatIdEnvironmentVariable,
            "7713476101");
        return new EnvironmentScope(originalBotToken, originalChatId);
    }

    private static ILogger<T> CreateLogger<T>(ILoggerFactory? loggerFactory)
        => loggerFactory?.CreateLogger<T>() ?? NullLogger<T>.Instance;

    private static FixedTimeProvider FixedUtcNow()
        => new FixedTimeProvider(
            new DateTimeOffset(2026, 3, 14, 15, 51, 50, 783, TimeSpan.Zero));

    private sealed class EnvironmentScope(string? botToken, string? chatId) : IDisposable
    {
        public void Dispose()
        {
            Environment.SetEnvironmentVariable(
                AppConstants.TelegramBotTokenEnvironmentVariable,
                botToken);
            Environment.SetEnvironmentVariable(
                AppConstants.TelegramChatIdEnvironmentVariable,
                chatId);
        }
    }

    private sealed class FixedTimeProvider(DateTimeOffset utcNow) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => utcNow;
    }

    private sealed class MutableTimeProvider(DateTimeOffset utcNow) : TimeProvider
    {
        private DateTimeOffset currentUtcNow = utcNow;

        public override DateTimeOffset GetUtcNow() => currentUtcNow;

        public void SetUtcNow(DateTimeOffset value) => currentUtcNow = value;
    }

    private sealed class RewriteOnNextUtcNowTimeProvider(
        DateTimeOffset utcNow,
        Action rewrite) : TimeProvider
    {
        private int remainingCallsBeforeRewrite;

        public void Arm(int callsBeforeRewrite = 1)
            => Volatile.Write(ref remainingCallsBeforeRewrite, callsBeforeRewrite);

        public override DateTimeOffset GetUtcNow()
        {
            int remainingCalls = Volatile.Read(ref remainingCallsBeforeRewrite);
            if (remainingCalls > 0
                && Interlocked.Decrement(ref remainingCallsBeforeRewrite) == 0)
            {
                rewrite();
            }

            return utcNow;
        }
    }

    private sealed class BlockingFirstResponseHttpMessageHandler : HttpMessageHandler
    {
        private readonly TaskCompletionSource firstRequestStarted =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly TaskCompletionSource allowFirstResponse =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        private int requestCount;

        public Task FirstRequestStarted => firstRequestStarted.Task;

        public int RequestCount => Volatile.Read(ref requestCount);

        public void AllowFirstResponse()
            => allowFirstResponse.SetResult();

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            int currentRequestCount = Interlocked.Increment(ref requestCount);
            if (currentRequestCount == 1)
            {
                firstRequestStarted.SetResult();
                await allowFirstResponse.Task.WaitAsync(cancellationToken);
            }

            return RecordingHttpMessageHandler.CreateJsonResponse(
                HttpStatusCode.OK,
                """{"ok":true}""");
        }
    }
}
