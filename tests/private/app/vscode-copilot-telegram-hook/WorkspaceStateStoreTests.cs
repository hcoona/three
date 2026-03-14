using Hcoona.VsCodeCopilotTelegramHook.State;
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
            WorkspaceStateStore store = new(TimeProvider.System);
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

            SessionState? sessionState = await WorkspaceStateStore.TryReadSessionAsync(
                tempDirectory.FullName,
                "session-123",
                CancellationToken.None);
            TurnState? storedTurnState = await WorkspaceStateStore.TryReadTurnAsync(
                tempDirectory.FullName,
                "session-123",
                CancellationToken.None);
            SummaryRecord? summaryRecord = await WorkspaceStateStore.TryReadSummaryAsync(
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
        }
        finally
        {
            tempDirectory.Delete(recursive: true);
        }
    }
}
