using System.Text.Json.Serialization;

namespace Hcoona.VsCodeCopilotTelegramHook;

[JsonSourceGenerationOptions(
    WriteIndented = true,
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull)]
[JsonSerializable(typeof(HookResponse))]
[JsonSerializable(typeof(HookSpecificOutput))]
[JsonSerializable(typeof(SessionStartHookInput))]
[JsonSerializable(typeof(UserPromptSubmitHookInput))]
[JsonSerializable(typeof(StopHookInput))]
[JsonSerializable(typeof(SessionState))]
[JsonSerializable(typeof(TurnState))]
[JsonSerializable(typeof(SummaryRecord))]
[JsonSerializable(typeof(LastSentState))]
[JsonSerializable(typeof(TelegramSendMessageRequest))]
[JsonSerializable(typeof(TelegramApiResponse))]
[JsonSerializable(typeof(UserHookSettingsDocument))]
[JsonSerializable(typeof(VsCodeUserSettingsDocument))]
[JsonSerializable(typeof(UserHookEntry))]
internal sealed partial class AppJsonSerializerContext : JsonSerializerContext
{
}
