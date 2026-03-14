using System.Text.Json;
using Hcoona.VsCodeCopilotTelegramHook.Notifications;
using Hcoona.VsCodeCopilotTelegramHook.State;

namespace Hcoona.VsCodeCopilotTelegramHook.Commands;

internal sealed class HookCommandService(
    WorkspaceStateStore workspaceStateStore,
    TelegramBotClient telegramBotClient)
{
    public async Task<int> HandleSessionStartAsync(
        Stream standardInput,
        Stream standardOutput,
        CancellationToken cancellationToken)
    {
        try
        {
            SessionStartHookInput? hookInput = await JsonSerializer.DeserializeAsync(
                standardInput,
                AppJsonSerializerContext.Default.SessionStartHookInput,
                cancellationToken);

            if (hookInput is null
                || string.IsNullOrWhiteSpace(hookInput.Cwd)
                || string.IsNullOrWhiteSpace(hookInput.SessionId))
            {
                return 0;
            }

            SessionState sessionState = await workspaceStateStore.InitializeSessionAsync(
                hookInput,
                cancellationToken);
            HookResponse response = new()
            {
                HookSpecificOutput = new HookSpecificOutput
                {
                    HookEventName = "SessionStart",
                    AdditionalContext = BuildAdditionalContext(sessionState),
                },
            };

            await JsonSerializer.SerializeAsync(
                standardOutput,
                response,
                AppJsonSerializerContext.Default.HookResponse,
                cancellationToken);
        }
        catch (Exception ex)
        {
            await Console.Error.WriteLineAsync($"SessionStart hook warning: {ex.Message}");
        }

        return 0;
    }

    public async Task<int> HandleStopAsync(
        Stream standardInput,
        CancellationToken cancellationToken)
    {
        try
        {
            StopHookInput? hookInput = await JsonSerializer.DeserializeAsync(
                standardInput,
                AppJsonSerializerContext.Default.StopHookInput,
                cancellationToken);

            if (hookInput is null
                || string.IsNullOrWhiteSpace(hookInput.Cwd)
                || string.IsNullOrWhiteSpace(hookInput.Timestamp))
            {
                return 0;
            }

            if (await WorkspaceStateStore.WasStopAlreadySentAsync(hookInput, cancellationToken))
            {
                return 0;
            }

            SessionState? sessionState = await WorkspaceStateStore.TryReadSessionAsync(
                hookInput.Cwd,
                cancellationToken);
            SummaryRecord? summaryRecord = await WorkspaceStateStore.TryReadSummaryAsync(
                hookInput.Cwd,
                cancellationToken);

            if (sessionState is not null
                && summaryRecord is not null
                && !string.Equals(
                    summaryRecord.RunId,
                    sessionState.RunId,
                    StringComparison.Ordinal))
            {
                summaryRecord = null;
            }

            string runId = sessionState?.RunId
                ?? summaryRecord?.RunId
                ?? hookInput.SessionId;

            GitRepositoryMetadata? repositoryMetadata = await GitRepositoryProbe.TryProbeAsync(
                hookInput.Cwd,
                cancellationToken);

            TelegramCredentials credentials =
                await TelegramCredentialProvider.ResolveAsync(cancellationToken);

            NotificationContext context = new()
            {
                RunId = runId,
                SessionId = hookInput.SessionId,
                StopTimestamp = hookInput.Timestamp,
                SentAt = workspaceStateStore.GetCurrentUtcTimestamp(),
                WorkspacePath = Path.GetFullPath(hookInput.Cwd),
                HostName = Environment.MachineName,
                ExecutionEnvironment = AppPaths.GetExecutionEnvironmentDisplay(),
                RepositoryName = repositoryMetadata?.RepositoryName,
                BranchName = repositoryMetadata?.BranchName,
                CommitId = ShortCommit(repositoryMetadata?.CommitId),
                TranscriptPath = hookInput.TranscriptPath,
            };

            IReadOnlyList<string> messages = NotificationComposer.Compose(
                context,
                summaryRecord);
            await telegramBotClient.SendMessagesAsync(credentials, messages, cancellationToken);
            await WorkspaceStateStore.RecordNotificationAsync(
                hookInput,
                context,
                summaryRecord,
                cancellationToken);
        }
        catch (Exception ex)
        {
            await Console.Error.WriteLineAsync($"Stop hook warning: {ex.Message}");
        }

        return 0;
    }

    private static string BuildAdditionalContext(SessionState sessionState)
    {
        return string.Join(
            " ",
            [
                "Notification summary handoff is enabled for this workspace.",
                "Before you finish the current task, overwrite .copilot/notify-summary.json",
                "with valid JSON,",
                $"copy run_id {sessionState.RunId} from .copilot/notify-session.json,",
                "and write the summary field in concise Chinese.",
            ]);
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
