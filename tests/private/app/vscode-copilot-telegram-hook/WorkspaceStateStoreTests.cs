using Hcoona.VsCodeCopilotTelegramHook.State;
using Hcoona.VsCodeCopilotTelegramHook.Logging;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Hcoona.VsCodeCopilotTelegramHook.Tests;

public sealed class WorkspaceStateStoreTests
{
    [Fact]
    public async Task StartTurnAsyncWritesSessionScopedTurnAndSummaryState()
    {
        DirectoryInfo tempDirectory = Directory.CreateTempSubdirectory();

        try
        {
            WorkspaceStateStore store = new(
                TimeProvider.System,
                NullLogger<WorkspaceStateStore>.Instance);
            SessionStartHookInput sessionStartInput = new()
            {
                Cwd = tempDirectory.FullName,
                SessionId = "session-123",
                TranscriptPath = "/tmp/transcript.json",
            };

            _ = await store.InitializeSessionAsync(sessionStartInput, CancellationToken.None);

            UserPromptSubmitHookInput promptInput = new()
            {
                Cwd = tempDirectory.FullName,
                SessionId = "session-123",
                TranscriptPath = "/tmp/transcript.json",
                Prompt = "Summarize the task.",
            };

            TurnState turnState = await store.StartTurnAsync(promptInput, CancellationToken.None);

            SessionState? sessionState = await store.TryReadSessionAsync(
                tempDirectory.FullName,
                "session-123",
                CancellationToken.None);
            TurnState? storedTurnState = await store.TryReadTurnAsync(
                tempDirectory.FullName,
                "session-123",
                CancellationToken.None);
            SummaryRecord? summaryRecord = await store.TryReadSummaryAsync(
                tempDirectory.FullName,
                "session-123",
                CancellationToken.None);

            Assert.NotNull(sessionState);
            Assert.NotNull(storedTurnState);
            Assert.NotNull(summaryRecord);
            Assert.Equal("session-123", sessionState!.SessionId);
            Assert.Equal("session-123", storedTurnState!.SessionId);
            Assert.Equal(turnState.TurnId, storedTurnState.TurnId);
            Assert.Equal("session-123", summaryRecord!.SessionId);
            Assert.Equal(turnState.TurnId, summaryRecord.TurnId);
            Assert.True(
                File.Exists(AppPaths.GetTurnStatePath(tempDirectory.FullName, "session-123")));
            Assert.True(
                File.Exists(AppPaths.GetSummaryStatePath(tempDirectory.FullName, "session-123")));
            FileAssertions.AssertOwnerOnlyFileMode(
                AppPaths.GetSessionStatePath(tempDirectory.FullName, "session-123"));
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
            string summaryPath = AppPaths.GetSummaryStatePath(tempDirectory.FullName, sessionId);
            Directory.CreateDirectory(Path.GetDirectoryName(summaryPath)!);
            await File.WriteAllTextAsync(summaryPath, "{not-json", CancellationToken.None);

            using IDisposable logScope = logContext.UseLogFile(
                AppPaths.GetSessionLogPath(tempDirectory.FullName, sessionId));
            SummaryRecord? summary = await store.TryReadSummaryAsync(
                tempDirectory.FullName,
                sessionId,
                CancellationToken.None);

            Assert.Null(summary);

            string logContent = await File.ReadAllTextAsync(
                AppPaths.GetSessionLogPath(tempDirectory.FullName, sessionId),
                CancellationToken.None);
            Assert.Contains("Failed to read summary state", logContent, StringComparison.Ordinal);
        }
        finally
        {
            tempDirectory.Delete(recursive: true);
        }
    }
}
