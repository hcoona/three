using System.Net;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Hcoona.VsCodeCopilotTelegramHook;

internal sealed class SessionStartHookInput
{
    [JsonPropertyName("timestamp")]
    public string Timestamp { get; set; } = string.Empty;

    [JsonPropertyName("cwd")]
    public string Cwd { get; set; } = string.Empty;

    [JsonPropertyName("sessionId")]
    public string SessionId { get; set; } = string.Empty;

    [JsonPropertyName("hookEventName")]
    public string HookEventName { get; set; } = string.Empty;

    [JsonPropertyName("transcript_path")]
    public string? TranscriptPath { get; set; }

    [JsonPropertyName("source")]
    public string? Source { get; set; }
}

internal sealed class UserPromptSubmitHookInput
{
    [JsonPropertyName("timestamp")]
    public string Timestamp { get; set; } = string.Empty;

    [JsonPropertyName("cwd")]
    public string Cwd { get; set; } = string.Empty;

    [JsonPropertyName("sessionId")]
    public string SessionId { get; set; } = string.Empty;

    [JsonPropertyName("hookEventName")]
    public string HookEventName { get; set; } = string.Empty;

    [JsonPropertyName("transcript_path")]
    public string? TranscriptPath { get; set; }

    [JsonPropertyName("prompt")]
    public string? Prompt { get; set; }
}

internal sealed class StopHookInput
{
    [JsonPropertyName("timestamp")]
    public string Timestamp { get; set; } = string.Empty;

    [JsonPropertyName("cwd")]
    public string Cwd { get; set; } = string.Empty;

    [JsonPropertyName("sessionId")]
    public string SessionId { get; set; } = string.Empty;

    [JsonPropertyName("hookEventName")]
    public string HookEventName { get; set; } = string.Empty;

    [JsonPropertyName("transcript_path")]
    public string? TranscriptPath { get; set; }

    [JsonPropertyName("stop_hook_active")]
    public bool StopHookActive { get; set; }
}

internal sealed class HookResponse
{
    [JsonPropertyName("hookSpecificOutput")]
    public HookSpecificOutput? HookSpecificOutput { get; set; }
}

internal sealed class HookSpecificOutput
{
    [JsonPropertyName("hookEventName")]
    public string HookEventName { get; init; } = string.Empty;

    [JsonPropertyName("additionalContext")]
    public string? AdditionalContext { get; init; }

    [JsonPropertyName("decision")]
    public string? Decision { get; init; }

    [JsonPropertyName("reason")]
    public string? Reason { get; init; }
}

internal sealed class SessionState
{
    public int Version { get; set; } = AppConstants.SchemaVersion;

    [JsonPropertyName("session_id")]
    public string SessionId { get; set; } = string.Empty;

    [JsonPropertyName("workspace_path")]
    public string WorkspacePath { get; set; } = string.Empty;

    [JsonPropertyName("created_at")]
    public string CreatedAt { get; set; } = string.Empty;

    [JsonPropertyName("updated_at")]
    public string UpdatedAt { get; set; } = string.Empty;

    [JsonPropertyName("transcript_path")]
    public string? TranscriptPath { get; set; }
}

internal sealed class TurnState
{
    public int Version { get; set; } = AppConstants.SchemaVersion;

    [JsonPropertyName("session_id")]
    public string SessionId { get; set; } = string.Empty;

    [JsonPropertyName("turn_id")]
    public string TurnId { get; set; } = string.Empty;

    [JsonPropertyName("workspace_path")]
    public string WorkspacePath { get; set; } = string.Empty;

    [JsonPropertyName("created_at")]
    public string CreatedAt { get; set; } = string.Empty;

    [JsonPropertyName("updated_at")]
    public string UpdatedAt { get; set; } = string.Empty;

    [JsonPropertyName("transcript_path")]
    public string? TranscriptPath { get; set; }
}

internal sealed class SummaryRecord
{
    public int Version { get; set; } = AppConstants.SchemaVersion;

    [JsonPropertyName("session_id")]
    public string? SessionId { get; set; }

    [JsonPropertyName("turn_id")]
    public string? TurnId { get; set; }

    [JsonPropertyName("updated_at")]
    public string? UpdatedAt { get; set; }

    [JsonPropertyName("status")]
    public string? Status { get; set; }

    [JsonPropertyName("summary")]
    public string? Summary { get; set; }

    [JsonPropertyName("details")]
    public List<string> Details { get; set; } = [];

    [JsonPropertyName("changed_files")]
    public List<string> ChangedFiles { get; set; } = [];

    [JsonPropertyName("next_steps")]
    public List<string> NextSteps { get; set; } = [];
}

internal sealed class LastSentState
{
    public int Version { get; set; } = AppConstants.SchemaVersion;

    [JsonPropertyName("session_id")]
    public string SessionId { get; set; } = string.Empty;

    [JsonPropertyName("turn_id")]
    public string TurnId { get; set; } = string.Empty;

    [JsonPropertyName("workspace_path")]
    public string WorkspacePath { get; set; } = string.Empty;

    [JsonPropertyName("stop_timestamp")]
    public string StopTimestamp { get; set; } = string.Empty;

    [JsonPropertyName("sent_at")]
    public string SentAt { get; set; } = string.Empty;

    [JsonPropertyName("summary_updated_at")]
    public string? SummaryUpdatedAt { get; set; }
}

internal sealed class TelegramSendMessageRequest
{
    [JsonPropertyName("chat_id")]
    public string ChatId { get; set; } = string.Empty;

    [JsonPropertyName("text")]
    public string Text { get; set; } = string.Empty;

    [JsonPropertyName("parse_mode")]
    public string ParseMode { get; set; } = "HTML";
}

internal sealed class TelegramResponseParameters
{
    [JsonPropertyName("retry_after")]
    public int? RetryAfterSeconds { get; set; }
}

internal sealed class TelegramApiResponse
{
    [JsonPropertyName("ok")]
    public bool Ok { get; set; }

    [JsonPropertyName("description")]
    public string? Description { get; set; }

    [JsonPropertyName("error_code")]
    public int? ErrorCode { get; set; }

    [JsonPropertyName("parameters")]
    public TelegramResponseParameters? Parameters { get; set; }
}

internal sealed class UserHookSettingsDocument
{
    [JsonPropertyName("hooks")]
    public Dictionary<string, List<UserHookEntry>> Hooks { get; set; } =
        new(StringComparer.Ordinal);

    [JsonExtensionData]
    public Dictionary<string, JsonElement>? AdditionalProperties { get; set; }
}

internal sealed class UserHookEntry
{
    [JsonPropertyName("type")]
    public string? Type { get; set; }

    [JsonPropertyName("command")]
    public string? Command { get; set; }

    [JsonPropertyName("timeout")]
    public int? Timeout { get; set; }

    [JsonPropertyName("env")]
    public Dictionary<string, string> Env { get; set; } =
        new(StringComparer.Ordinal);

    [JsonExtensionData]
    public Dictionary<string, JsonElement>? AdditionalProperties { get; set; }
}

internal class UserPathOverrides
{
    public DirectoryInfo? InstallRoot { get; init; }

    public FileInfo? HookSettingsPath { get; init; }

    public DirectoryInfo? InstructionsDirectory { get; init; }
}

internal sealed class InstallCommandOptions : UserPathOverrides
{
    public string? TelegramBotToken { get; init; }

    public string? TelegramChatId { get; init; }

    public bool SkipSecretPrompt { get; init; }

    public FileInfo? BinaryPath { get; init; }
}

internal sealed class UninstallCommandOptions : UserPathOverrides
{
    public bool RemoveSecrets { get; init; }
}

internal sealed class TestNotificationCommandOptions : UserPathOverrides
{
    public string? Message { get; init; }
}

internal sealed record UserInstallationPaths(
    string InstallRoot,
    string InstalledBinaryPath,
    string HookSettingsPath,
    string InstructionsDirectory,
    string InstructionFilePath);

internal sealed record TelegramCredentials(string BotToken, string ChatId, string Source);

internal sealed record GitRepositoryMetadata(
    string TopLevelPath,
    string RepositoryName,
    string? BranchName,
    string? CommitId);

internal sealed class NotificationContext
{
    public string SessionId { get; init; } = string.Empty;

    public string TurnId { get; init; } = string.Empty;

    public string StopTimestamp { get; init; } = string.Empty;

    public string SentAt { get; init; } = string.Empty;

    public string WorkspacePath { get; init; } = string.Empty;

    public string HostName { get; init; } = string.Empty;

    public string ExecutionEnvironment { get; init; } = string.Empty;

    public string? RepositoryName { get; init; }

    public string? BranchName { get; init; }

    public string? CommitId { get; init; }

    public string? TranscriptPath { get; init; }
}

internal sealed record ProcessExecutionResult(
    int ExitCode,
    string StandardOutput,
    string StandardError)
{
    public bool Succeeded => ExitCode == 0;
}

internal sealed record ConfigurationApplyResult(
    bool Applied,
    string Message,
    string? CandidatePath = null);
