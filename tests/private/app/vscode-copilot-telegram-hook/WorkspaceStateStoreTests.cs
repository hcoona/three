using System.Globalization;
using Hcoona.VsCodeCopilotTelegramHook.State;
using Hcoona.VsCodeCopilotTelegramHook.Logging;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Hcoona.VsCodeCopilotTelegramHook.Tests;

public sealed class WorkspaceStateStoreTests
{
    [Fact]
    public void GetCurrentUtcTimestampUsesInvariantCulture()
    {
        CultureInfo originalCulture = CultureInfo.CurrentCulture;
        CultureInfo originalUICulture = CultureInfo.CurrentUICulture;

        try
        {
            CultureInfo? nonInvariantCulture = CultureInfo
                .GetCultures(CultureTypes.SpecificCultures)
                .FirstOrDefault(c => c.Name.Length > 0);
            if (nonInvariantCulture is null)
            {
                return;
            }

            CultureInfo.CurrentCulture = nonInvariantCulture;
            CultureInfo.CurrentUICulture = nonInvariantCulture;
            WorkspaceStateStore store = new(
                new FixedTimeProvider(
                    new DateTimeOffset(2026, 3, 14, 15, 51, 50, 783, TimeSpan.Zero)),
                NullLogger<WorkspaceStateStore>.Instance);

            string timestamp = store.GetCurrentUtcTimestamp();

            Assert.Equal("2026-03-14T15:51:50.783Z", timestamp);
            Assert.True(DateTimeOffset.TryParseExact(
                timestamp,
                "yyyy-MM-ddTHH:mm:ss.fff'Z'",
                CultureInfo.InvariantCulture,
                DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal,
                out DateTimeOffset parsed));
            Assert.Equal(
                new DateTimeOffset(2026, 3, 14, 15, 51, 50, 783, TimeSpan.Zero),
                parsed);
        }
        finally
        {
            CultureInfo.CurrentCulture = originalCulture;
            CultureInfo.CurrentUICulture = originalUICulture;
        }
    }

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
            Assert.Contains(
                "Failed to read notification summary",
                logContent,
                StringComparison.Ordinal);
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

    [Fact]
    public async Task TryClaimStopNotificationAsyncRethrowsNonDuplicateCreateFailures()
    {
        DirectoryInfo tempDirectory = Directory.CreateTempSubdirectory();

        try
        {
            string claimPath = AppPaths.GetSessionStopClaimPath(
                tempDirectory.FullName,
                "session-123",
                "stop-test");
            Directory.CreateDirectory(claimPath);

            Exception exception = await Assert.ThrowsAnyAsync<Exception>(
                () => WorkspaceStateStore.TryClaimStopNotificationAsync(
                    claimPath,
                    "2026-03-14T15:51:49.783Z",
                    CancellationToken.None));

            Assert.True(exception is IOException or UnauthorizedAccessException);
        }
        finally
        {
            tempDirectory.Delete(recursive: true);
        }
    }

    [Fact]
    public async Task ReleaseOwnedStopNotificationClaimAsyncRequiresMatchingOwnerToken()
    {
        DirectoryInfo tempDirectory = Directory.CreateTempSubdirectory();

        try
        {
            string claimPath = AppPaths.GetSessionStopClaimPath(
                tempDirectory.FullName,
                "session-123",
                "stop-test");
            const string OwnerToken = "2026-03-14T15:51:49.783Z";
            Assert.True(
                await WorkspaceStateStore.TryClaimStopNotificationAsync(
                    claimPath,
                    OwnerToken,
                    CancellationToken.None));

            await WorkspaceStateStore.ReleaseOwnedStopNotificationClaimAsync(
                claimPath,
                "2026-03-14T15:51:50.783Z",
                CancellationToken.None);
            Assert.True(File.Exists(claimPath));

            await WorkspaceStateStore.ReleaseOwnedStopNotificationClaimAsync(
                claimPath,
                OwnerToken,
                CancellationToken.None);
            Assert.False(File.Exists(claimPath));
        }
        finally
        {
            tempDirectory.Delete(recursive: true);
        }
    }

    [Fact]
    public async Task ClaimMutationsWaitForCoordinationLock()
    {
        DirectoryInfo tempDirectory = Directory.CreateTempSubdirectory();

        try
        {
            string claimPath = AppPaths.GetSessionStopClaimPath(
                tempDirectory.FullName,
                "session-123",
                "stop-test");
            const string OwnerToken = "2026-03-14T15:51:49.783Z";
            Assert.True(
                await WorkspaceStateStore.TryClaimStopNotificationAsync(
                    claimPath,
                    OwnerToken,
                    CancellationToken.None));

            UserOperationLock coordinationLock = await UserOperationLock.AcquireAsync(
                claimPath + ".coordination.lock",
                CancellationToken.None);
            Task releaseTask = WorkspaceStateStore.ReleaseOwnedStopNotificationClaimAsync(
                claimPath,
                OwnerToken,
                CancellationToken.None);

            try
            {
                await Task.Delay(250, CancellationToken.None);
                Assert.False(releaseTask.IsCompleted);
                Assert.True(File.Exists(claimPath));
            }
            finally
            {
                await coordinationLock.DisposeAsync();
            }

            await releaseTask;
            Assert.False(File.Exists(claimPath));
        }
        finally
        {
            tempDirectory.Delete(recursive: true);
        }
    }

    private sealed class FixedTimeProvider(DateTimeOffset utcNow) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => utcNow;
    }
}
