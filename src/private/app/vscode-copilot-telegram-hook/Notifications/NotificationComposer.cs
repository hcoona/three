using System.Net;
using System.Text;

namespace Hcoona.VsCodeCopilotTelegramHook.Notifications;

internal static class NotificationComposer
{
    public static IReadOnlyList<string> Compose(NotificationContext context, SummaryRecord? summary)
    {
        string headerHtml = BuildHeaderHtml(context);
        string bodyText = BuildBodyText(summary);

        int availableBodyLength = Math.Max(
            256,
            AppConstants.MaxTelegramHtmlMessageLength - headerHtml.Length - 64);
        List<string> bodyChunks = SplitPlainText(bodyText, availableBodyLength);
        int messageCount = Math.Max(1, bodyChunks.Count);

        if (bodyChunks.Count == 0)
        {
            bodyChunks = ["摘要：当前轮未生成摘要。"];
        }

        List<string> messages = new(messageCount);
        for (int index = 0; index < bodyChunks.Count; index++)
        {
            string heading = bodyChunks.Count == 1
                ? "<b>✅ Copilot 当前轮已完成</b>"
                : $"<b>✅ Copilot 当前轮已完成（{index + 1}/{bodyChunks.Count}）</b>";

            string bodyHtml = WebUtility.HtmlEncode(bodyChunks[index]);
            messages.Add($"{heading}\n{headerHtml}\n{bodyHtml}");
        }

        return messages;
    }

    private static string BuildHeaderHtml(NotificationContext context)
    {
        List<string> lines =
        [
            FormatCodeLine("发送时间", context.SentAt),
            FormatCodeLine("运行 ID", context.RunId),
            FormatCodeLine("会话 ID", context.SessionId),
            FormatCodeLine("Stop 时间", context.StopTimestamp),
            FormatCodeLine("工作区", context.WorkspacePath),
            FormatCodeLine("主机", context.HostName),
            FormatCodeLine("环境", context.ExecutionEnvironment),
        ];

        if (!string.IsNullOrWhiteSpace(context.RepositoryName))
        {
            lines.Add(FormatCodeLine("仓库", context.RepositoryName));
        }

        if (!string.IsNullOrWhiteSpace(context.BranchName))
        {
            lines.Add(FormatCodeLine("分支", context.BranchName));
        }

        if (!string.IsNullOrWhiteSpace(context.CommitId))
        {
            lines.Add(FormatCodeLine("提交", context.CommitId));
        }

        if (!string.IsNullOrWhiteSpace(context.TranscriptPath))
        {
            lines.Add(FormatCodeLine("转录文件", context.TranscriptPath));
        }

        return string.Join("\n", lines);
    }

    private static string BuildBodyText(SummaryRecord? summary)
    {
        StringBuilder builder = new();

        string summaryText = string.IsNullOrWhiteSpace(summary?.Summary)
            ? "当前轮未生成摘要。"
            : summary.Summary.Trim();

        builder.Append("摘要：");
        builder.AppendLine(summaryText);

        if (!string.IsNullOrWhiteSpace(summary?.Status))
        {
            builder.AppendLine();
            builder.Append("状态：");
            builder.AppendLine(summary.Status.Trim());
        }

        AppendListSection(builder, "详情", summary?.Details);
        AppendListSection(builder, "变更文件", summary?.ChangedFiles);
        AppendListSection(builder, "后续建议", summary?.NextSteps);

        return builder.ToString().TrimEnd();
    }

    private static void AppendListSection(
        StringBuilder builder,
        string label,
        IReadOnlyCollection<string>? values)
    {
        if (values is null)
        {
            return;
        }

        List<string> filteredValues = values
            .Where(static value => !string.IsNullOrWhiteSpace(value))
            .Select(static value => value.Trim())
            .ToList();

        if (filteredValues.Count == 0)
        {
            return;
        }

        builder.AppendLine();
        builder.Append(label);
        builder.AppendLine("：");

        foreach (string value in filteredValues)
        {
            builder.Append("- ");
            builder.AppendLine(value);
        }
    }

    private static List<string> SplitPlainText(string text, int maxEncodedLength)
    {
        if (string.IsNullOrEmpty(text))
        {
            return [];
        }

        List<string> chunks = [];
        int start = 0;

        while (start < text.Length)
        {
            int end = FindChunkEnd(text, start, maxEncodedLength);
            string chunk = text[start..end].Trim('\r', '\n');
            if (!string.IsNullOrWhiteSpace(chunk))
            {
                chunks.Add(chunk);
            }

            start = end;
            while (start < text.Length && (text[start] == '\r' || text[start] == '\n'))
            {
                start++;
            }
        }

        return chunks;
    }

    private static int FindChunkEnd(string text, int start, int maxEncodedLength)
    {
        int lastBreak = -1;

        for (int index = start + 1; index <= text.Length; index++)
        {
            string candidate = text[start..index];
            int encodedLength = WebUtility.HtmlEncode(candidate).Length;
            if (encodedLength > maxEncodedLength)
            {
                if (lastBreak > start)
                {
                    return lastBreak;
                }

                return Math.Max(start + 1, index - 1);
            }

            if (index < text.Length
                && (text[index - 1] == '\n' || char.IsWhiteSpace(text[index - 1])))
            {
                lastBreak = index;
            }
        }

        return text.Length;
    }

    private static string FormatCodeLine(string label, string value)
        => $"<b>{WebUtility.HtmlEncode(label)}：</b>"
            + $"<code>{WebUtility.HtmlEncode(value)}</code>";
}
