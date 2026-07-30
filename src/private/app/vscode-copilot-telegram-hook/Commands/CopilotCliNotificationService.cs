using System.Globalization;
using System.Text.Json;
using Hcoona.VsCodeCopilotTelegramHook.Logging;
using Hcoona.VsCodeCopilotTelegramHook.Notifications;
using Microsoft.Extensions.Logging;

namespace Hcoona.VsCodeCopilotTelegramHook.Commands;

internal sealed class CopilotCliNotificationService(
    TelegramBotClient telegramBotClient,
    TelegramCredentialProvider telegramCredentialProvider,
    GitRepositoryProbe gitRepositoryProbe,
    SessionLogFileContext sessionLogFileContext,
    TimeProvider timeProvider,
    ILogger<CopilotCliNotificationService> logger)
{
    private const int MaxSummaryLength = 1600;
    internal TimeSpan DeliveryTimeout { get; init; } = TimeSpan.FromSeconds(30);

    public async Task<int> HandleSessionEventFileAsync(
        FileInfo eventFile,
        CancellationToken cancellationToken)
    {
        string readyPath = eventFile.FullName;
        string workingPath = readyPath + ".working";
        string cancellationPath = readyPath + ".cancelled";

        try
        {
            try
            {
                File.Move(readyPath, workingPath);
            }
            catch (FileNotFoundException)
            {
                return 0;
            }
            catch (IOException) when (File.Exists(workingPath))
            {
                return 0;
            }

            CopilotCliSessionEventInput input = await ReadInputAsync(
                workingPath,
                cancellationToken);
            await WaitUntilDeliverableAsync(input.DeliverAfter, cancellationToken);
            if (File.Exists(cancellationPath))
            {
                DeleteEventFiles(readyPath, workingPath, cancellationPath);
                return 0;
            }

            using CancellationTokenSource timeoutSource =
                CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeoutSource.CancelAfter(DeliveryTimeout);
            await SendAsync(input, timeoutSource.Token);
            DeleteEventFiles(readyPath, workingPath, cancellationPath);
            return 0;
        }
        catch (TelegramSendMessagesException ex) when (ex.SuccessfulMessageCount > 0)
        {
            DeleteEventFiles(readyPath, workingPath, cancellationPath);
            AppLog.PartialCopilotCliNotification(logger, ex);
            return 1;
        }
        catch (InvalidDataException ex)
        {
            DeleteEventFiles(readyPath, workingPath, cancellationPath);
            AppLog.InvalidCopilotCliEventFile(logger, ex);
            await Console.Error.WriteLineAsync(
                $"Copilot CLI session event warning: {ex.Message}");
            return 1;
        }
        catch (Exception ex)
        {
            RestoreReadyFile(readyPath, workingPath, cancellationPath);
            AppLog.CopilotCliNotificationFailed(logger, ex);
            await Console.Error.WriteLineAsync(
                $"Copilot CLI session event warning: {ex.Message}");
            return 1;
        }
    }

    private static async Task<CopilotCliSessionEventInput> ReadInputAsync(
        string workingPath,
        CancellationToken cancellationToken)
    {
        try
        {
            await using FileStream stream = File.OpenRead(workingPath);
            CopilotCliSessionEventInput input = await JsonSerializer.DeserializeAsync(
                    stream,
                    AppJsonSerializerContext.Default.CopilotCliSessionEventInput,
                    cancellationToken)
                ?? throw new InvalidDataException("The Copilot CLI event file is empty.");
            ValidateInput(input);
            return input;
        }
        catch (JsonException ex)
        {
            throw new InvalidDataException("The Copilot CLI event file is invalid.", ex);
        }
    }

    private async Task SendAsync(
        CopilotCliSessionEventInput input,
        CancellationToken cancellationToken)
    {
        string workspacePath = Path.GetFullPath(input.Cwd);
        using IDisposable logScope = sessionLogFileContext.UseLogFile(
            AppPaths.GetSessionLogPath(workspacePath, input.SessionId));

        GitRepositoryMetadata? repositoryMetadata = await gitRepositoryProbe.TryProbeAsync(
            workspacePath,
            cancellationToken);
        TelegramCredentials credentials = await telegramCredentialProvider.ResolveAsync(
            cancellationToken);
        bool isCompletion = input.EventType == "session_idle";
        string sentAt = timeProvider.GetUtcNow().UtcDateTime.ToString(
            "yyyy-MM-ddTHH:mm:ss.fff'Z'",
            CultureInfo.InvariantCulture);

        NotificationContext context = new()
        {
            SessionId = input.SessionId,
            TurnId = input.EventId,
            StopTimestamp = input.Timestamp,
            SentAt = sentAt,
            WorkspacePath = workspacePath,
            HostName = Environment.MachineName,
            ExecutionEnvironment = AppPaths.GetExecutionEnvironmentDisplay(),
            RepositoryName = repositoryMetadata?.RepositoryName,
            BranchName = repositoryMetadata?.BranchName,
            CommitId = ShortCommit(repositoryMetadata?.CommitId),
            Heading = isCompletion
                ? "✅ Copilot 已完成当前工作"
                : "⚠️ Copilot 需要人工介入",
            IdentifierLabel = "事件 ID",
            EventTimestampLabel = "事件时间",
            BodyLabel = isCompletion ? "摘要" : "等待事项",
            MissingBodyText = isCompletion ? "未捕获到最终回复。" : "需要人工处理。",
        };
        NotificationSummary summary = new()
        {
            Summary = isCompletion
                ? TruncateSummary(input.Summary)
                : BuildAttentionMessage(input),
            Status = isCompletion && !string.IsNullOrWhiteSpace(input.SummarySource)
                ? $"summary source: {input.SummarySource}"
                : null,
        };

        IReadOnlyList<string> messages = NotificationComposer.Compose(context, summary);
        AppLog.SendingCopilotCliNotification(
            logger,
            messages.Count,
            input.EventType,
            input.EventId);
        await telegramBotClient.SendMessagesAsync(credentials, messages, cancellationToken);
    }

    private static void ValidateInput(CopilotCliSessionEventInput input)
    {
        if (string.IsNullOrWhiteSpace(input.SessionId)
            || string.IsNullOrWhiteSpace(input.Cwd)
            || string.IsNullOrWhiteSpace(input.EventId)
            || string.IsNullOrWhiteSpace(input.EventType)
            || string.IsNullOrWhiteSpace(input.Timestamp))
        {
            throw new InvalidDataException(
                "The Copilot CLI event file is missing required fields.");
        }

        if (input.EventType is not "session_idle"
            and not "permission_requested"
            and not "elicitation_requested"
            and not "user_input_requested"
            and not "exit_plan_mode_requested"
            and not "auto_mode_switch_requested"
            and not "session_limits_exhausted_requested"
            and not "mcp_oauth_required")
        {
            throw new InvalidDataException(
                $"Unsupported Copilot CLI event type: {input.EventType}");
        }
    }

    private static async Task WaitUntilDeliverableAsync(
        string? deliverAfter,
        CancellationToken cancellationToken)
    {
        if (!DateTimeOffset.TryParse(
                deliverAfter,
                CultureInfo.InvariantCulture,
                DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal,
                out DateTimeOffset deliveryTime))
        {
            return;
        }

        TimeSpan delay = deliveryTime - DateTimeOffset.UtcNow;
        if (delay <= TimeSpan.Zero)
        {
            return;
        }

        await Task.Delay(
            delay > TimeSpan.FromSeconds(5) ? TimeSpan.FromSeconds(5) : delay,
            cancellationToken);
    }

    private static string BuildAttentionMessage(CopilotCliSessionEventInput input)
    {
        if (string.IsNullOrWhiteSpace(input.Summary))
        {
            return input.Message?.Trim() ?? "Copilot is waiting for input.";
        }

        if (string.IsNullOrWhiteSpace(input.Message))
        {
            return input.Summary.Trim();
        }

        return $"{input.Summary.Trim()}\n{input.Message.Trim()}";
    }

    private static string TruncateSummary(string? value)
    {
        string summary = string.IsNullOrWhiteSpace(value)
            ? "未捕获到最终回复。"
            : value.Trim();
        return summary.Length <= MaxSummaryLength
            ? summary
            : summary[..MaxSummaryLength].TrimEnd() + "...";
    }

    private static string? ShortCommit(string? commitId)
    {
        if (string.IsNullOrWhiteSpace(commitId))
        {
            return null;
        }

        return commitId.Length <= 12 ? commitId : commitId[..12];
    }

    private static void RestoreReadyFile(
        string readyPath,
        string workingPath,
        string cancellationPath)
    {
        if (File.Exists(cancellationPath))
        {
            DeleteEventFiles(readyPath, workingPath, cancellationPath);
            return;
        }

        if (File.Exists(workingPath) && !File.Exists(readyPath))
        {
            File.Move(workingPath, readyPath);
        }
    }

    private static void DeleteEventFiles(params string[] paths)
    {
        foreach (string path in paths)
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
    }
}
