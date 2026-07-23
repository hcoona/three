using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Hcoona.VsCodeCopilotTelegramHook.Logging;
using Hcoona.VsCodeCopilotTelegramHook.Notifications;
using Hcoona.VsCodeCopilotTelegramHook.State;
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
    private const int RetryableClaimConflictExitCode = 75;
    internal TimeSpan SessionEventTimeout { get; init; } = TimeSpan.FromSeconds(25);

    public async Task<int> HandleNotificationAsync(
        Stream standardInput,
        CancellationToken cancellationToken)
    {
        using CancellationTokenSource timeoutSource =
            CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutSource.CancelAfter(SessionEventTimeout);
        CancellationToken operationCancellationToken = timeoutSource.Token;

        try
        {
            CopilotCliNotificationHookInput? input = await JsonSerializer.DeserializeAsync(
                standardInput,
                AppJsonSerializerContext.Default.CopilotCliNotificationHookInput,
                operationCancellationToken);
            if (input is null
                || string.IsNullOrWhiteSpace(input.SessionId)
                || string.IsNullOrWhiteSpace(input.Cwd)
                || string.IsNullOrWhiteSpace(input.NotificationType)
                || string.IsNullOrWhiteSpace(input.Message))
            {
                await Console.Error.WriteLineAsync(
                    "Copilot CLI notification hook warning: missing required input.");
                return 0;
            }

            if (input.NotificationType is not "permission_prompt" and not "elicitation_dialog")
            {
                return 0;
            }

            string timestamp = DateTimeOffset
                .FromUnixTimeMilliseconds(input.Timestamp)
                .UtcDateTime
                .ToString("yyyy-MM-ddTHH:mm:ss.fff'Z'", CultureInfo.InvariantCulture);
            return await SendAsync(
                new CopilotCliSessionEventInput
                {
                    SessionId = input.SessionId,
                    Timestamp = timestamp,
                    Cwd = input.Cwd,
                    EventId = CreateNotificationEventId(input),
                    EventType = input.NotificationType,
                    Message = input.Message,
                    Summary = input.Title,
                },
                operationCancellationToken);
        }
        catch (Exception ex)
        {
            AppLog.CopilotCliNotificationFailed(logger, ex);
            await Console.Error.WriteLineAsync(
                $"Copilot CLI notification hook warning: {ex.Message}");
            return 1;
        }
    }

    public async Task<int> HandleSessionEventAsync(
        Stream standardInput,
        CancellationToken cancellationToken)
    {
        using CancellationTokenSource timeoutSource =
            CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutSource.CancelAfter(SessionEventTimeout);
        CancellationToken operationCancellationToken = timeoutSource.Token;

        try
        {
            CopilotCliSessionEventInput? input = await JsonSerializer.DeserializeAsync(
                standardInput,
                AppJsonSerializerContext.Default.CopilotCliSessionEventInput,
                operationCancellationToken);
            if (input is null
                || string.IsNullOrWhiteSpace(input.SessionId)
                || string.IsNullOrWhiteSpace(input.Cwd)
                || string.IsNullOrWhiteSpace(input.EventId)
                || string.IsNullOrWhiteSpace(input.EventType)
                || string.IsNullOrWhiteSpace(input.Timestamp))
            {
                await Console.Error.WriteLineAsync(
                    "Copilot CLI session event warning: missing required input.");
                return 0;
            }

            if (input.EventType is not "session_idle"
                and not "user_input_requested"
                and not "permission_requested"
                and not "elicitation_requested")
            {
                return 0;
            }

            return await SendAsync(input, operationCancellationToken);
        }
        catch (Exception ex)
        {
            AppLog.CopilotCliNotificationFailed(logger, ex);
            await Console.Error.WriteLineAsync($"Copilot CLI session event warning: {ex.Message}");
            return 1;
        }
    }

    private async Task<int> SendAsync(
        CopilotCliSessionEventInput input,
        CancellationToken cancellationToken)
    {
        string workspacePath = Path.GetFullPath(input.Cwd);
        using IDisposable logScope = sessionLogFileContext.UseLogFile(
            AppPaths.GetSessionLogPath(workspacePath, input.SessionId));

        string eventKey = $"{input.EventType}\n{input.EventId}";
        string sentMarkerPath = AppPaths.GetCopilotCliEventMarkerPath(
            workspacePath,
            input.SessionId,
            eventKey);
        if (await WorkspaceStateStore.WasNotificationAlreadySentAsync(
                sentMarkerPath,
                cancellationToken))
        {
            AppLog.SkippingDuplicateCopilotCliNotification(
                logger,
                input.SessionId,
                input.EventId);
            return 0;
        }

        string claimPath = AppPaths.GetCopilotCliEventClaimPath(
            workspacePath,
            input.SessionId,
            eventKey);
        string reclaimClaimPath = AppPaths.GetCopilotCliEventReclaimClaimPath(
            workspacePath,
            input.SessionId,
            eventKey);
        string claimedAt = timeProvider.GetUtcNow().UtcDateTime.ToString(
            "yyyy-MM-ddTHH:mm:ss.fff'Z'",
            CultureInfo.InvariantCulture);
        string claimOwner = string.Create(
            CultureInfo.InvariantCulture,
            $"{claimedAt}\n{Environment.ProcessId}");
        bool claimed = await WorkspaceStateStore.TryClaimStopNotificationAsync(
            claimPath,
            claimOwner,
            cancellationToken);
        if (!claimed)
        {
            claimed = await WorkspaceStateStore.TryReclaimStaleClaimAsync(
                claimPath,
                reclaimClaimPath,
                claimOwner,
                TimeSpan.FromMinutes(AppConstants.CopilotCliEventClaimStaleAfterMinutes),
                () => WorkspaceStateStore.WasNotificationAlreadySentAsync(
                    sentMarkerPath,
                    cancellationToken),
                cancellationToken,
                IsAbandonedProcessClaim);
        }

        if (!claimed)
        {
            if (await WorkspaceStateStore.WasNotificationAlreadySentAsync(
                    sentMarkerPath,
                    cancellationToken))
            {
                AppLog.SkippingDuplicateCopilotCliNotification(
                    logger,
                    input.SessionId,
                    input.EventId);
                return 0;
            }

            AppLog.CopilotCliNotificationClaimBusy(
                logger,
                input.SessionId,
                input.EventId);
            return RetryableClaimConflictExitCode;
        }

        try
        {
            if (await WorkspaceStateStore.WasNotificationAlreadySentAsync(
                    sentMarkerPath,
                    cancellationToken))
            {
                AppLog.SkippingDuplicateCopilotCliNotification(
                    logger,
                    input.SessionId,
                    input.EventId);
                return 0;
            }

            GitRepositoryMetadata? repositoryMetadata = await gitRepositoryProbe.TryProbeAsync(
                workspacePath,
                cancellationToken);
            TelegramCredentials credentials = await telegramCredentialProvider.ResolveAsync(
                cancellationToken);
            bool isCompletion = input.EventType == "session_idle";

            NotificationContext context = new()
            {
                SessionId = input.SessionId,
                TurnId = input.EventId,
                StopTimestamp = input.Timestamp,
                SentAt = claimedAt,
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
                EventType = input.EventType,
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
                input.SessionId,
                input.EventId,
                input.EventType);
            int sentCount = await telegramBotClient.SendMessagesAsync(
                credentials,
                messages,
                cancellationToken);
            if (sentCount > 0)
            {
                AtomicTextFileWriter.WriteAllText(sentMarkerPath, claimedAt);
            }

            return 0;
        }
        catch (TelegramSendMessagesException ex) when (ex.SuccessfulMessageCount > 0)
        {
            AtomicTextFileWriter.WriteAllText(sentMarkerPath, claimedAt);
            throw;
        }
        finally
        {
            await WorkspaceStateStore.ReleaseOwnedStopNotificationClaimAsync(
                claimPath,
                claimOwner,
                CancellationToken.None);
        }
    }

    private static bool IsAbandonedProcessClaim(string claimOwner)
    {
        ReadOnlySpan<char> value = claimOwner.AsSpan().Trim();
        int firstLineEnd = value.IndexOfAny('\r', '\n');
        if (firstLineEnd < 0)
        {
            return false;
        }

        ReadOnlySpan<char> processIdText = value[(firstLineEnd + 1)..].Trim();
        int secondLineEnd = processIdText.IndexOfAny('\r', '\n');
        if (secondLineEnd >= 0)
        {
            processIdText = processIdText[..secondLineEnd];
        }

        if (!int.TryParse(
                processIdText,
                NumberStyles.None,
                CultureInfo.InvariantCulture,
                out int processId)
            || processId <= 0
            || processId == Environment.ProcessId)
        {
            return false;
        }

        try
        {
            using Process process = Process.GetProcessById(processId);
            return process.HasExited;
        }
        catch (ArgumentException)
        {
            return true;
        }
        catch (Exception ex) when (
            ex is InvalidOperationException
                or Win32Exception
                or NotSupportedException
                or UnauthorizedAccessException)
        {
            return false;
        }
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
        if (summary.Length <= MaxSummaryLength)
        {
            return summary;
        }

        int end = summary.LastIndexOfAny(
            ['\n', ' ', '。', '.', '！', '!', '？', '?'],
            MaxSummaryLength - 1,
            MaxSummaryLength);
        if (end < MaxSummaryLength / 2)
        {
            end = MaxSummaryLength;
        }

        return summary[..end].TrimEnd() + "...";
    }

    private static string CreateNotificationEventId(CopilotCliNotificationHookInput input)
    {
        string raw =
            $"{input.NotificationType}\n{input.Timestamp}\n{input.Title}\n{input.Message}";
        return Convert
            .ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(raw)))[..32]
            .ToLowerInvariant();
    }

    private static string? ShortCommit(string? commitId)
    {
        if (string.IsNullOrWhiteSpace(commitId))
        {
            return null;
        }

        return commitId.Length <= 12 ? commitId : commitId[..12];
    }
}
