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
        Assert.Contains("<b>轮次 ID：</b><code>turn-789</code>", message, StringComparison.Ordinal);
        Assert.Contains("摘要：当前轮未生成摘要。", message, StringComparison.Ordinal);
    }

    [Fact]
    public void ComposeSplitsOverlengthNotificationsIntoMultipleMessages()
    {
        NotificationContext context = CreateContext();
        NotificationSummary summaryRecord = new()
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

    [Fact]
    public void ComposeTruncatesLongHeaderFieldsToKeepMessagesWithinTelegramLimit()
    {
        NotificationContext context = CreateContext(
            workspacePath: "/very-long/" + new string('w', 6000),
            transcriptPath: "/very-long/" + new string('t', 6000));
        NotificationSummary summaryRecord = new()
        {
            Summary = "Short summary that must remain visible.",
        };

        string message = Assert.Single(NotificationComposer.Compose(context, summaryRecord));

        Assert.True(message.Length <= AppConstants.MaxTelegramHtmlMessageLength);
        Assert.Contains(
            "摘要：Short summary that must remain visible.",
            message,
            StringComparison.Ordinal);
        Assert.Contains("<b>工作区：</b><code>", message, StringComparison.Ordinal);
        Assert.Contains("...", message, StringComparison.Ordinal);
    }

    [Fact]
    public void ComposeUsesCustomLifecyclePresentation()
    {
        NotificationContext context = CreateContext(
            heading: "⚠️ Copilot needs attention",
            identifierLabel: "Event ID",
            eventTimestampLabel: "Event time",
            bodyLabel: "Waiting for",
            eventType: "user_input_requested");

        string message = Assert.Single(
            NotificationComposer.Compose(
                context,
                new NotificationSummary { Summary = "Choose a deployment target." }));

        Assert.Contains("<b>⚠️ Copilot needs attention</b>", message, StringComparison.Ordinal);
        Assert.Contains("<b>Event ID：</b><code>turn-789</code>", message, StringComparison.Ordinal);
        Assert.Contains(
            "<b>Event time：</b><code>2026-03-13T12:34:56.789Z</code>",
            message,
            StringComparison.Ordinal);
        Assert.Contains(
            "<b>事件类型：</b><code>user_input_requested</code>",
            message,
            StringComparison.Ordinal);
        Assert.Contains("Waiting for：Choose a deployment target.", message, StringComparison.Ordinal);
    }

    private static NotificationContext CreateContext(
        string? workspacePath = null,
        string? transcriptPath = null,
        string? heading = null,
        string? identifierLabel = null,
        string? eventTimestampLabel = null,
        string? bodyLabel = null,
        string? eventType = null)
    {
        return new NotificationContext
        {
            SessionId = "session-456",
            TurnId = "turn-789",
            StopTimestamp = "2026-03-13T12:34:56.789Z",
            SentAt = "2026-03-13T12:34:57.000Z",
            WorkspacePath = workspacePath ?? "/tmp/workspace",
            HostName = "builder-host",
            ExecutionEnvironment = "Linux | X64",
            RepositoryName = "three",
            BranchName = "main",
            CommitId = "abcdef123456",
            TranscriptPath = transcriptPath ?? "/tmp/workspace/.copilot/transcript.json",
            Heading = heading ?? "✅ Copilot 当前轮已完成",
            IdentifierLabel = identifierLabel ?? "轮次 ID",
            EventTimestampLabel = eventTimestampLabel ?? "停止时间",
            BodyLabel = bodyLabel ?? "摘要",
            EventType = eventType,
        };
    }
}
