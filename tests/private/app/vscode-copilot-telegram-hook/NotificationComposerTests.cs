using Hcoona.VsCodeCopilotTelegramHook.Notifications;
using Xunit;

namespace Hcoona.VsCodeCopilotTelegramHook.Tests;

public sealed class NotificationComposerTests
{
    [Fact]
    public void ComposeUsesFallbackSummaryWhenModelSummaryIsMissing()
    {
        NotificationContext context = CreateContext();

        IReadOnlyList<string> messages = NotificationComposer.Compose(context, summary: null);

        string message = Assert.Single(messages);
        Assert.Contains("<b>✅ Copilot 当前轮已完成</b>", message, StringComparison.Ordinal);
        Assert.Contains("摘要：当前轮未生成摘要。", message, StringComparison.Ordinal);
    }

    [Fact]
    public void ComposeSplitsOverlengthNotificationsIntoMultipleMessages()
    {
        NotificationContext context = CreateContext();
        SummaryRecord summaryRecord = new()
        {
            Summary = string.Join(
                Environment.NewLine,
                Enumerable.Repeat("这是一个非常长的摘要段落，用来验证 Telegram 长消息会被自动拆分，并且每一段都保持可读性。", 240)),
        };

        IReadOnlyList<string> messages = NotificationComposer.Compose(context, summaryRecord);

        Assert.True(messages.Count > 1);
        for (int index = 0; index < messages.Count; index++)
        {
            Assert.Contains(
                $"（{index + 1}/{messages.Count}）",
                messages[index],
                StringComparison.Ordinal);
            Assert.True(messages[index].Length <= AppConstants.MaxTelegramHtmlMessageLength);
        }
    }

    private static NotificationContext CreateContext()
    {
        return new NotificationContext
        {
            RunId = "run-123",
            SessionId = "session-456",
            StopTimestamp = "2026-03-13T12:34:56.789Z",
            SentAt = "2026-03-13T12:34:57.000Z",
            WorkspacePath = "/tmp/workspace",
            HostName = "builder-host",
            ExecutionEnvironment = "Linux | X64",
            RepositoryName = "three",
            BranchName = "main",
            CommitId = "abcdef123456",
            TranscriptPath = "/tmp/workspace/.copilot/transcript.json",
        };
    }
}
