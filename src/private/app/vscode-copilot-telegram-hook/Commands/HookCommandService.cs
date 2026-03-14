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

    public async Task<int> HandleUserPromptSubmitAsync(
        Stream standardInput,
        CancellationToken cancellationToken)
    {
        try
        {
            UserPromptSubmitHookInput? hookInput = await JsonSerializer.DeserializeAsync(
                standardInput,
                AppJsonSerializerContext.Default.UserPromptSubmitHookInput,
                cancellationToken);

            if (hookInput is null
                || string.IsNullOrWhiteSpace(hookInput.Cwd)
                || string.IsNullOrWhiteSpace(hookInput.SessionId))
            {
                return 0;
            }

            _ = await workspaceStateStore.StartTurnAsync(hookInput, cancellationToken);
        }
        catch (Exception ex)
        {
            await Console.Error.WriteLineAsync($"UserPromptSubmit hook warning: {ex.Message}");
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

            SessionState? sessionState = await WorkspaceStateStore.TryReadSessionAsync(
                hookInput.Cwd,
                hookInput.SessionId,
                cancellationToken);
            TurnState? turnState = await WorkspaceStateStore.TryReadTurnAsync(
                hookInput.Cwd,
                hookInput.SessionId,
                cancellationToken);
            SummaryRecord? summaryRecord = await WorkspaceStateStore.TryReadSummaryAsync(
                hookInput.Cwd,
                hookInput.SessionId,
                cancellationToken);

            if (await WorkspaceStateStore.WasStopAlreadySentAsync(
                hookInput.Cwd,
                hookInput.SessionId,
                turnState?.TurnId,
                hookInput.Timestamp,
                cancellationToken))
            {
                return 0;
            }

            if (summaryRecord is not null
                && (!string.Equals(
                        summaryRecord.SessionId,
                        hookInput.SessionId,
                        StringComparison.Ordinal)
                    || turnState is null
                    || !string.Equals(
                        summaryRecord.TurnId,
                        turnState.TurnId,
                        StringComparison.Ordinal)))
            {
                summaryRecord = null;
            }

            string turnId = turnState?.TurnId
                ?? summaryRecord?.TurnId
                ?? CreateStopFallbackTurnId(hookInput.Timestamp);

            GitRepositoryMetadata? repositoryMetadata = await GitRepositoryProbe.TryProbeAsync(
                hookInput.Cwd,
                cancellationToken);

            TelegramCredentials credentials =
                await TelegramCredentialProvider.ResolveAsync(cancellationToken);

            NotificationContext context = new()
            {
                SessionId = hookInput.SessionId,
                TurnId = turnId,
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
        string turnStatePath = AppPaths.GetRelativeTurnStatePath(sessionState.SessionId);
        string summaryStatePath = AppPaths.GetRelativeSummaryStatePath(sessionState.SessionId);

        return string.Join(
            " ",
            [
                "Notification summary handoff is enabled for this workspace.",
                $"Your session_id is {sessionState.SessionId}.",
                $"Before you finish each task, read {turnStatePath}",
                $"and overwrite {summaryStatePath} with valid JSON.",
                "Copy session_id and turn_id from the turn state file,",
                "and write the summary field as concise human-readable text,",
                "preferably in Chinese on a best-effort basis.",
            ]);
    }

    private static string CreateStopFallbackTurnId(string timestamp)
    {
        string normalized = new(timestamp.Where(char.IsLetterOrDigit).ToArray());
        return string.IsNullOrWhiteSpace(normalized)
            ? $"stop-{Guid.NewGuid():n}"
            : $"stop-{normalized.ToLowerInvariant()}";
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
