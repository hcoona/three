using Hcoona.VsCodeCopilotTelegramHook.State;
using Hcoona.VsCodeCopilotTelegramHook.Logging;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Hcoona.VsCodeCopilotTelegramHook.Tests;

public sealed class WorkspaceStateStoreTests
{
    [Fact]
    public async Task CreateNotificationTurnAsyncWritesAuthoritativePerTurnFiles()
    {
        DirectoryInfo tempDirectory = Directory.CreateTempSubdirectory();

        try
        {
            WorkspaceStateStore store = new(
                TimeProvider.System,
                NullLogger<WorkspaceStateStore>.Instance);
            UserPromptSubmitHookInput promptInput = new()
            {
                Cwd = tempDirectory.FullName,
                SessionId = "session-123",
                TranscriptPath = "/workspace/transcript.json",
                Prompt = "Ship the change.",
            };
            PromptObservation observation = await store.RecordPromptObservationAsync(
                promptInput,
                new PromptClassification("main-user-prompt", "test"),
                CancellationToken.None);

            NotificationTurn turn = await store.CreateNotificationTurnAsync(
                promptInput,
                observation,
                CancellationToken.None);

            NotificationSession? session = await store.TryReadSessionAsync(
                tempDirectory.FullName,
                "session-123",
                CancellationToken.None);
            NotificationTurn? storedTurn = await store.TryReadTurnAsync(
                tempDirectory.FullName,
                "session-123",
                turn.NotificationTurnId,
                CancellationToken.None);
            NotificationSummary? summary = await store.TryReadSummaryAsync(
                tempDirectory.FullName,
                "session-123",
                turn.NotificationTurnId,
                CancellationToken.None);
            CurrentNotificationState? current = await store.TryReadCurrentAsync(
                tempDirectory.FullName,
                "session-123",
                CancellationToken.None);

            Assert.NotNull(session);
            Assert.NotNull(storedTurn);
            Assert.NotNull(summary);
            Assert.NotNull(current);
            Assert.Equal("session-123", session!.SessionId);
            Assert.Equal(turn.NotificationTurnId, storedTurn!.NotificationTurnId);
            Assert.Equal(turn.NotificationNonce, summary!.NotificationNonce);
            Assert.Equal(turn.NotificationTurnId, current!.NotificationTurnId);
            Assert.True(File.Exists(AppPaths.GetPromptObservationPath(
                tempDirectory.FullName,
                "session-123",
                observation.PromptObservationId)));
            FileAssertions.AssertOwnerOnlyFileMode(
                AppPaths.GetSessionStatePath(tempDirectory.FullName, "session-123"));
            FileAssertions.AssertOwnerOnlyFileMode(
                AppPaths.GetTurnStatePath(
                    tempDirectory.FullName,
                    "session-123",
                    turn.NotificationTurnId));
            FileAssertions.AssertOwnerOnlyFileMode(
                AppPaths.GetSummaryStatePath(
                    tempDirectory.FullName,
                    "session-123",
                    turn.NotificationTurnId));
        }
        finally
        {
            tempDirectory.Delete(recursive: true);
        }
    }

    [Fact]
    public async Task TryReadSummaryAsyncLogsInvalidJsonAsMissing()
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

            WorkspaceStateStore store = new(
                TimeProvider.System,
                loggerFactory.CreateLogger<WorkspaceStateStore>());
            string sessionId = "session-123";
            string turnId = "turn-123";
            string summaryPath = AppPaths.GetSummaryStatePath(
                tempDirectory.FullName,
                sessionId,
                turnId);
            Directory.CreateDirectory(Path.GetDirectoryName(summaryPath)!);
            await File.WriteAllTextAsync(summaryPath, "{not-json", CancellationToken.None);

            using IDisposable logScope = logContext.UseLogFile(
                AppPaths.GetSessionLogPath(tempDirectory.FullName, sessionId));
            NotificationSummary? summary = await store.TryReadSummaryAsync(
                tempDirectory.FullName,
                sessionId,
                turnId,
                CancellationToken.None);

            Assert.Null(summary);

            string logContent = await File.ReadAllTextAsync(
                AppPaths.GetSessionLogPath(tempDirectory.FullName, sessionId),
                CancellationToken.None);
            Assert.Contains("Failed to read notification summary", logContent, StringComparison.Ordinal);
        }
        finally
        {
            tempDirectory.Delete(recursive: true);
        }
    }

    [Fact]
    public async Task TryClaimStopNotificationAsyncIsSingleWinner()
    {
        DirectoryInfo tempDirectory = Directory.CreateTempSubdirectory();

        try
        {
            string claimPath = AppPaths.GetSessionStopClaimPath(
                tempDirectory.FullName,
                "session-123",
                "stop-test");

            bool firstClaim = await WorkspaceStateStore.TryClaimStopNotificationAsync(
                claimPath,
                "2026-03-14T15:51:49.783Z",
                CancellationToken.None);
            bool secondClaim = await WorkspaceStateStore.TryClaimStopNotificationAsync(
                claimPath,
                "2026-03-14T15:51:50.783Z",
                CancellationToken.None);

            Assert.True(firstClaim);
            Assert.False(secondClaim);
            Assert.True(File.Exists(claimPath));
            FileAssertions.AssertOwnerOnlyFileMode(claimPath);
        }
        finally
        {
            tempDirectory.Delete(recursive: true);
        }
    }
}
