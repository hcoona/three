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
                    UpdatedAt = "2026-03-14T15:51:50.783Z",
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
    public async Task HandleStopAsyncDoesNotLoopOnStaleTurnAfterFallbackDefault()
    {
        DirectoryInfo tempDirectory = Directory.CreateTempSubdirectory();
        using EnvironmentScope environment = SetTelegramEnvironment();

        try
        {
            WorkspaceStateStore stateStore = new(
                TimeProvider.System,
                NullLogger<WorkspaceStateStore>.Instance);
            _ = await CreateTurnAsync(stateStore, tempDirectory.FullName, "session-123");
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

            Assert.Equal(2, handler.Requests.Count);
            TelegramSendMessageRequest secondPayload =
                DeserializeTelegramPayload(handler.Requests[1]);
            Assert.Contains(
                "stop-20260314t155250783z",
                secondPayload.Text,
                StringComparison.Ordinal);
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
            _ = await CreateTurnAsync(
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

            _ = await CreateTurnAsync(
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
            RecordingHttpMessageHandler handler = new(
                [
                    RecordingHttpMessageHandler.CreateJsonResponse(
                        HttpStatusCode.BadGateway,
                        """{"ok":false,"description":"temporary failure"}"""),
                    RecordingHttpMessageHandler.CreateJsonResponse(
                        HttpStatusCode.OK,
                        """{"ok":true}"""),
                ]);
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

            _ = await CreateTurnAsync(
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

            Assert.Equal(2, handler.Requests.Count);
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
                TimeProvider.System,
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
    public async Task HandleStopAsyncSkipsDifferentTimestampStopWhileTurnDeliveryClaimHeld()
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

            Assert.Equal(3, handler.Requests.Count);
            TelegramSendMessageRequest laterStopPayload =
                DeserializeTelegramPayload(handler.Requests[2]);
            Assert.Contains("摘要：当前轮未生成摘要。", laterStopPayload.Text, StringComparison.Ordinal);
            Assert.DoesNotContain(
                "This long summary forces multiple Telegram messages.",
                laterStopPayload.Text,
                StringComparison.Ordinal);
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

            TelegramSendMessageRequest payload = DeserializeTelegramPayload(
                Assert.Single(handler.Requests));
            Assert.Contains("摘要：当前轮未生成摘要。", payload.Text, StringComparison.Ordinal);
            Assert.DoesNotContain(
                "This already-delivered turn must not be resent.",
                payload.Text,
                StringComparison.Ordinal);

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

    private static async Task<NotificationRecord> ReadNotificationRecordAsync(string path)
    {
        await using FileStream stream = File.OpenRead(path);
        return await JsonSerializer.DeserializeAsync(
                stream,
                AppJsonSerializerContext.Default.NotificationRecord,
                CancellationToken.None)
            ?? throw new InvalidOperationException("Expected a notification record.");
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
