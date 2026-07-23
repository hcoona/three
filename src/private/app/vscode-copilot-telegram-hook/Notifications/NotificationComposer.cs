using System.Net;
using System.Text;

namespace Hcoona.VsCodeCopilotTelegramHook.Notifications;

internal static class NotificationComposer
{
    private const int PreferredBodyLength = 256;
    private const int ReservedHeadingAndSeparatorLength = 64;
    private const string TruncationMarker = "...";

    public static IReadOnlyList<string> Compose(
        NotificationContext context,
        NotificationSummary? summary)
    {
        string bodyText = BuildBodyText(context, summary);
        int desiredBodyLength = Math.Min(
            PreferredBodyLength,
            WebUtility.HtmlEncode(bodyText).Length);
        int maxHeaderLength = Math.Max(
            0,
            AppConstants.MaxTelegramHtmlMessageLength
                - ReservedHeadingAndSeparatorLength
                - desiredBodyLength);
        string headerHtml = BuildHeaderHtml(context, maxHeaderLength);

        int availableBodyLength = Math.Max(
            1,
            AppConstants.MaxTelegramHtmlMessageLength
                - headerHtml.Length
                - ReservedHeadingAndSeparatorLength);
        List<string> bodyChunks = SplitPlainText(bodyText, availableBodyLength);
        int messageCount = Math.Max(1, bodyChunks.Count);

        if (bodyChunks.Count == 0)
        {
            bodyChunks = [$"{context.BodyLabel}：{context.MissingBodyText}"];
        }

        List<string> messages = new(messageCount);
        for (int index = 0; index < bodyChunks.Count; index++)
        {
            string headingText = bodyChunks.Count == 1
                ? context.Heading
                : $"{context.Heading}（{index + 1}/{bodyChunks.Count}）";
            string heading = $"<b>{WebUtility.HtmlEncode(headingText)}</b>";

            string bodyHtml = WebUtility.HtmlEncode(bodyChunks[index]);
            messages.Add($"{heading}\n{headerHtml}\n{bodyHtml}");
        }

        return messages;
    }

    private static string BuildHeaderHtml(NotificationContext context, int maxLength)
    {
        if (maxLength <= 0)
        {
            return string.Empty;
        }

        List<HeaderField> fields =
        [
            new("发送时间", context.SentAt, Optional: false),
            new("会话 ID", context.SessionId, Optional: false),
            new(context.IdentifierLabel, context.TurnId, Optional: false),
            new(context.EventTimestampLabel, context.StopTimestamp, Optional: false),
            new("事件类型", context.EventType, Optional: true),
            new("工作区", context.WorkspacePath, Optional: false),
            new("主机", context.HostName, Optional: false),
            new("环境", context.ExecutionEnvironment, Optional: false),
            new("仓库", context.RepositoryName, Optional: true),
            new("分支", context.BranchName, Optional: true),
            new("提交", context.CommitId, Optional: true),
            new("转录文件", context.TranscriptPath, Optional: true),
        ];

        List<string> lines = [];
        int remainingLength = maxLength;

        foreach (HeaderField field in fields)
        {
            if (string.IsNullOrWhiteSpace(field.Value))
            {
                continue;
            }

            string value = field.Value;

            int separatorLength = lines.Count == 0 ? 0 : 1;
            int lineBudget = remainingLength - separatorLength;
            if (lineBudget <= 0)
            {
                break;
            }

            string line = TryFormatCodeLine(field.Label, value, lineBudget);
            if (string.IsNullOrEmpty(line))
            {
                if (field.Optional)
                {
                    continue;
                }

                break;
            }

            lines.Add(line);
            remainingLength -= line.Length + separatorLength;
        }

        return string.Join("\n", lines);
    }

    private static string BuildBodyText(
        NotificationContext context,
        NotificationSummary? summary)
    {
        StringBuilder builder = new();

        string summaryText = string.IsNullOrWhiteSpace(summary?.Summary)
            ? context.MissingBodyText
            : summary.Summary.Trim();

        builder.Append(context.BodyLabel);
        builder.Append('：');
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
        if (string.IsNullOrEmpty(text) || maxEncodedLength <= 0)
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
        => GetCodeLinePrefix(label)
            + $"{WebUtility.HtmlEncode(value)}</code>";

    private static string TryFormatCodeLine(string label, string value, int maxLength)
    {
        string line = FormatCodeLine(label, value);
        if (line.Length <= maxLength)
        {
            return line;
        }

        string prefix = GetCodeLinePrefix(label);
        const string suffix = "</code>";
        int maxEncodedValueLength = maxLength - prefix.Length - suffix.Length;
        if (maxEncodedValueLength <= 0)
        {
            return string.Empty;
        }

        string truncatedValue = TruncateToEncodedLength(value, maxEncodedValueLength);
        if (string.IsNullOrEmpty(truncatedValue))
        {
            return string.Empty;
        }

        return prefix + WebUtility.HtmlEncode(truncatedValue) + suffix;
    }

    private static string TruncateToEncodedLength(string value, int maxEncodedLength)
    {
        string encodedValue = WebUtility.HtmlEncode(value);
        if (encodedValue.Length <= maxEncodedLength)
        {
            return value;
        }

        if (maxEncodedLength <= TruncationMarker.Length)
        {
            return TruncationMarker[..maxEncodedLength];
        }

        int prefixBudget = maxEncodedLength - TruncationMarker.Length;
        int end = FindEncodedPrefixEnd(value, prefixBudget);
        if (end <= 0)
        {
            return TruncationMarker;
        }

        string trimmedPrefix = value[..end].TrimEnd();
        return string.IsNullOrEmpty(trimmedPrefix)
            ? TruncationMarker
            : trimmedPrefix + TruncationMarker;
    }

    private static int FindEncodedPrefixEnd(string value, int maxEncodedLength)
    {
        for (int index = 1; index <= value.Length; index++)
        {
            if (WebUtility.HtmlEncode(value[..index]).Length > maxEncodedLength)
            {
                return index - 1;
            }
        }

        return value.Length;
    }

    private static string GetCodeLinePrefix(string label)
        => $"<b>{WebUtility.HtmlEncode(label)}：</b><code>";

    private sealed record HeaderField(string Label, string? Value, bool Optional);
}
