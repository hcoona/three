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

    [JsonPropertyName("session_id")]
    public string SessionId { get; set; } = string.Empty;

    [JsonPropertyName("hook_event_name")]
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

    [JsonPropertyName("session_id")]
    public string SessionId { get; set; } = string.Empty;

    [JsonPropertyName("hook_event_name")]
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

    [JsonPropertyName("session_id")]
    public string SessionId { get; set; } = string.Empty;

    [JsonPropertyName("hook_event_name")]
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

internal sealed class CopilotCliHookOutput
{
    [JsonPropertyName("modifiedPrompt")]
    public string? ModifiedPrompt { get; init; }

    [JsonPropertyName("additionalContext")]
    public string? AdditionalContext { get; init; }

    [JsonPropertyName("suppressOutput")]
    public bool? SuppressOutput { get; init; }

    [JsonPropertyName("decision")]
    public string? Decision { get; init; }

    [JsonPropertyName("reason")]
    public string? Reason { get; init; }
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

internal sealed class NotificationSession
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

internal sealed class PromptObservation
{
    public int Version { get; set; } = AppConstants.SchemaVersion;

    [JsonPropertyName("session_id")]
    public string SessionId { get; set; } = string.Empty;

    [JsonPropertyName("prompt_observation_id")]
    public string PromptObservationId { get; set; } = string.Empty;

    [JsonPropertyName("workspace_path")]
    public string WorkspacePath { get; set; } = string.Empty;

    [JsonPropertyName("observed_at")]
    public string ObservedAt { get; set; } = string.Empty;

    [JsonPropertyName("hook_event_name")]
    public string HookEventName { get; set; } = string.Empty;

    [JsonPropertyName("prompt")]
    public string? Prompt { get; set; }

    [JsonPropertyName("classification")]
    public string Classification { get; set; } = string.Empty;

    [JsonPropertyName("classification_reason")]
    public string ClassificationReason { get; set; } = string.Empty;

    [JsonPropertyName("transcript_path")]
    public string? TranscriptPath { get; set; }
}

internal sealed class NotificationTurn
{
    public int Version { get; set; } = AppConstants.SchemaVersion;

    [JsonPropertyName("session_id")]
    public string SessionId { get; set; } = string.Empty;

    [JsonPropertyName("notification_turn_id")]
    public string NotificationTurnId { get; set; } = string.Empty;

    [JsonPropertyName("notification_nonce")]
    public string NotificationNonce { get; set; } = string.Empty;

    [JsonPropertyName("prompt_observation_id")]
    public string PromptObservationId { get; set; } = string.Empty;

    [JsonPropertyName("workspace_path")]
    public string WorkspacePath { get; set; } = string.Empty;

    [JsonPropertyName("created_at")]
    public string CreatedAt { get; set; } = string.Empty;

    [JsonPropertyName("updated_at")]
    public string UpdatedAt { get; set; } = string.Empty;

    [JsonPropertyName("status")]
    public string Status { get; set; } = "open";

    [JsonPropertyName("summary_placeholder_created_at")]
    public string? SummaryPlaceholderCreatedAt { get; set; }

    [JsonPropertyName("transcript_path")]
    public string? TranscriptPath { get; set; }
}

internal sealed class NotificationSummary
{
    public int Version { get; set; } = AppConstants.SchemaVersion;

    [JsonPropertyName("session_id")]
    public string? SessionId { get; set; }

    [JsonPropertyName("notification_turn_id")]
    public string? NotificationTurnId { get; set; }

    [JsonPropertyName("notification_nonce")]
    public string? NotificationNonce { get; set; }

    [JsonPropertyName("updated_at")]
    public string? UpdatedAt { get; set; }

    [JsonPropertyName("placeholder_created_at")]
    public string? PlaceholderCreatedAt { get; set; }

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

internal sealed class StopObservation
{
    public int Version { get; set; } = AppConstants.SchemaVersion;

    [JsonPropertyName("session_id")]
    public string SessionId { get; set; } = string.Empty;

    [JsonPropertyName("notification_turn_id")]
    public string NotificationTurnId { get; set; } = string.Empty;

    [JsonPropertyName("stop_id")]
    public string StopId { get; set; } = string.Empty;

    [JsonPropertyName("observed_at")]
    public string ObservedAt { get; set; } = string.Empty;

    [JsonPropertyName("stop_timestamp")]
    public string StopTimestamp { get; set; } = string.Empty;

    [JsonPropertyName("match_reason")]
    public string MatchReason { get; set; } = string.Empty;

    [JsonPropertyName("summary_valid")]
    public bool SummaryValid { get; set; }

    [JsonPropertyName("summary_pending_handoff")]
    public bool SummaryPendingHandoff { get; set; }

    [JsonPropertyName("summary_failure_reason")]
    public string? SummaryFailureReason { get; set; }
}

internal sealed class NotificationRecord
{
    public int Version { get; set; } = AppConstants.SchemaVersion;

    [JsonPropertyName("session_id")]
    public string SessionId { get; set; } = string.Empty;

    [JsonPropertyName("notification_turn_id")]
    public string? NotificationTurnId { get; set; }

    [JsonPropertyName("notification_key")]
    public string NotificationKey { get; set; } = string.Empty;

    [JsonPropertyName("workspace_path")]
    public string WorkspacePath { get; set; } = string.Empty;

    [JsonPropertyName("stop_timestamp")]
    public string StopTimestamp { get; set; } = string.Empty;

    [JsonPropertyName("sent_at")]
    public string SentAt { get; set; } = string.Empty;

    [JsonPropertyName("summary_updated_at")]
    public string? SummaryUpdatedAt { get; set; }

    [JsonPropertyName("degraded")]
    public bool Degraded { get; set; }

    [JsonPropertyName("delivery_status")]
    public string DeliveryStatus { get; set; } = string.Empty;

    [JsonPropertyName("successful_message_count")]
    public int? SuccessfulMessageCount { get; set; }

    [JsonPropertyName("reason")]
    public string? Reason { get; set; }
}

internal sealed class CurrentNotificationState
{
    public int Version { get; set; } = AppConstants.SchemaVersion;

    [JsonPropertyName("session_id")]
    public string SessionId { get; set; } = string.Empty;

    [JsonPropertyName("notification_turn_id")]
    public string NotificationTurnId { get; set; } = string.Empty;

    [JsonPropertyName("notification_nonce")]
    public string NotificationNonce { get; set; } = string.Empty;

    [JsonPropertyName("summary_path")]
    public string SummaryPath { get; set; } = string.Empty;

    [JsonPropertyName("updated_at")]
    public string UpdatedAt { get; set; } = string.Empty;
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
    [JsonPropertyName("version")]
    public int? Version { get; set; }

    [JsonPropertyName("hooks")]
    public Dictionary<string, List<UserHookEntry>> Hooks { get; set; } =
        new(StringComparer.Ordinal);

    [JsonExtensionData]
    public Dictionary<string, JsonElement>? AdditionalProperties { get; set; }
}

internal sealed class VsCodeUserSettingsDocument
{
    [JsonPropertyName("chat.hookFilesLocations")]
    public Dictionary<string, bool>? ChatHookFilesLocations { get; set; }

    [JsonExtensionData]
    public Dictionary<string, JsonElement>? AdditionalProperties { get; set; }
}

[JsonConverter(typeof(UserHookEntryJsonConverter))]
internal sealed class UserHookEntry
{
    [JsonPropertyName("type")]
    public string? Type { get; set; }

    [JsonPropertyName("command")]
    public string? Command { get; set; }

    [JsonPropertyName("timeout")]
    public int? Timeout { get; set; }

    [JsonIgnore]
    public bool TimeoutPropertyPresent { get; set; }

    [JsonPropertyName("timeoutSec")]
    public int? TimeoutSec { get; set; }

    [JsonPropertyName("env")]
    public Dictionary<string, string> Env { get; set; } =
        new(StringComparer.Ordinal);

    [JsonExtensionData]
    public Dictionary<string, JsonElement>? AdditionalProperties { get; set; }
}

internal sealed class UserHookEntryJsonConverter : JsonConverter<UserHookEntry>
{
    public override UserHookEntry Read(
        ref Utf8JsonReader reader,
        Type typeToConvert,
        JsonSerializerOptions options)
    {
        if (reader.TokenType != JsonTokenType.StartObject)
        {
            throw new JsonException("Expected a hook entry object.");
        }

        UserHookEntry entry = new();
        while (reader.Read())
        {
            if (reader.TokenType == JsonTokenType.EndObject)
            {
                return entry;
            }

            if (reader.TokenType != JsonTokenType.PropertyName)
            {
                throw new JsonException("Expected a hook entry property name.");
            }

            string propertyName = reader.GetString()
                ?? throw new JsonException("Expected a hook entry property name.");
            if (!reader.Read())
            {
                throw new JsonException("Expected a hook entry property value.");
            }

            switch (propertyName)
            {
                case "type":
                    entry.Type = ReadNullableString(ref reader);
                    break;
                case "command":
                    entry.Command = ReadNullableString(ref reader);
                    break;
                case "timeout":
                    entry.TimeoutPropertyPresent = true;
                    entry.Timeout = ReadNullableInt32(ref reader);
                    break;
                case "timeoutSec":
                    entry.TimeoutSec = ReadNullableInt32(ref reader);
                    break;
                case "env":
                    entry.Env = ReadEnv(ref reader);
                    break;
                default:
                    entry.AdditionalProperties ??= new Dictionary<string, JsonElement>(
                        StringComparer.Ordinal);
                    entry.AdditionalProperties[propertyName] = JsonElement.ParseValue(ref reader);
                    break;
            }
        }

        throw new JsonException("Expected the end of a hook entry object.");
    }

    public override void Write(
        Utf8JsonWriter writer,
        UserHookEntry value,
        JsonSerializerOptions options)
    {
        writer.WriteStartObject();
        WriteStringPropertyIfNotNull(writer, "type", value.Type);
        WriteStringPropertyIfNotNull(writer, "command", value.Command);
        if (value.Timeout is not null)
        {
            writer.WriteNumber("timeout", value.Timeout.Value);
        }
        else if (value.TimeoutPropertyPresent)
        {
            writer.WriteNull("timeout");
        }

        if (value.TimeoutSec is not null)
        {
            writer.WriteNumber("timeoutSec", value.TimeoutSec.Value);
        }

        if (value.Env is not null)
        {
            writer.WritePropertyName("env");
            writer.WriteStartObject();
            foreach (KeyValuePair<string, string> item in value.Env)
            {
                if (item.Value is null)
                {
                    writer.WriteNull(item.Key);
                }
                else
                {
                    writer.WriteString(item.Key, item.Value);
                }
            }

            writer.WriteEndObject();
        }

        if (value.AdditionalProperties is not null)
        {
            foreach (KeyValuePair<string, JsonElement> item in value.AdditionalProperties)
            {
                if (IsKnownProperty(item.Key))
                {
                    continue;
                }

                writer.WritePropertyName(item.Key);
                item.Value.WriteTo(writer);
            }
        }

        writer.WriteEndObject();
    }

    private static string? ReadNullableString(ref Utf8JsonReader reader)
    {
        if (reader.TokenType == JsonTokenType.Null)
        {
            return null;
        }

        if (reader.TokenType != JsonTokenType.String)
        {
            throw new JsonException("Expected a string value.");
        }

        return reader.GetString();
    }

    private static int? ReadNullableInt32(ref Utf8JsonReader reader)
    {
        if (reader.TokenType == JsonTokenType.Null)
        {
            return null;
        }

        if (reader.TokenType != JsonTokenType.Number || !reader.TryGetInt32(out int value))
        {
            throw new JsonException("Expected an integer value.");
        }

        return value;
    }

    private static Dictionary<string, string> ReadEnv(ref Utf8JsonReader reader)
    {
        if (reader.TokenType == JsonTokenType.Null)
        {
            return null!;
        }

        if (reader.TokenType != JsonTokenType.StartObject)
        {
            throw new JsonException("Expected an env object.");
        }

        Dictionary<string, string> env = new(StringComparer.Ordinal);
        while (reader.Read())
        {
            if (reader.TokenType == JsonTokenType.EndObject)
            {
                return env;
            }

            if (reader.TokenType != JsonTokenType.PropertyName)
            {
                throw new JsonException("Expected an env property name.");
            }

            string propertyName = reader.GetString()
                ?? throw new JsonException("Expected an env property name.");
            if (!reader.Read())
            {
                throw new JsonException("Expected an env property value.");
            }

            env[propertyName] = ReadNullableString(ref reader)!;
        }

        throw new JsonException("Expected the end of an env object.");
    }

    private static void WriteStringPropertyIfNotNull(
        Utf8JsonWriter writer,
        string propertyName,
        string? value)
    {
        if (value is not null)
        {
            writer.WriteString(propertyName, value);
        }
    }

    private static bool IsKnownProperty(string propertyName)
        => propertyName is "type" or "command" or "timeout" or "timeoutSec" or "env";
}

internal sealed record VsCodeSettingsTarget(
    string SettingsPath,
    bool IsApplicable,
    string DisplayName,
    string? InapplicableReason = null);

internal sealed record VsCodeSettingsStatus(
    VsCodeSettingsTarget Target,
    bool IsRegistered);

internal sealed record VsCodeSettingsWritePlan(
    string SettingsPath,
    string SerializedSettings,
    bool OriginalFileExisted,
    string? OriginalContent,
    string SuccessMessage,
    string FailureMessage);

internal sealed record ConfigurationPlanResult(
    bool Applied,
    string Message,
    VsCodeSettingsWritePlan? WritePlan = null,
    string? CandidatePath = null);

internal class UserPathOverrides
{
    public DirectoryInfo? InstallRoot { get; init; }

    public FileInfo? ManagedHookFilePath { get; init; }

    public FileInfo? CopilotCliHookFilePath { get; init; }

    public IReadOnlyList<FileInfo>? VsCodeSettingsPaths { get; init; }

    internal IReadOnlyList<VsCodeSettingsTarget>? VsCodeSettingsTargets { get; init; }
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

internal sealed class SecretCommandOptions : UserPathOverrides
{
    public string? TelegramBotToken { get; init; }

    public string? TelegramChatId { get; init; }

    public bool Prompt { get; init; }
}

internal sealed record UserInstallationPaths(
    string InstallRoot,
    string InstalledBinaryPath,
    string ManagedHookFilePath,
    string CopilotCliHookFilePath,
    IReadOnlyList<VsCodeSettingsTarget> VsCodeSettingsTargets,
    string UserLogFilePath);

internal sealed record TelegramCredentials(string BotToken, string ChatId, string Source);

internal sealed record StoredTelegramSecrets(string? BotToken, string? ChatId);

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

internal sealed record ProcessLogOptions(
    bool IncludeArgumentsInLogs = true,
    bool IncludeWorkingDirectoryInLogs = true,
    bool IncludeStandardErrorInLogs = true);

internal sealed record ConfigurationApplyResult(
    bool Applied,
    string Message,
    string? CandidatePath = null);
