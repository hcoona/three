using System.Text.Json.Serialization;

namespace Hcoona.VsCodeCopilotTelegramHook;

[JsonSourceGenerationOptions(
    WriteIndented = true,
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull)]
[JsonSerializable(typeof(HookResponse))]
[JsonSerializable(typeof(CopilotCliHookOutput))]
[JsonSerializable(typeof(HookSpecificOutput))]
[JsonSerializable(typeof(SessionStartHookInput))]
[JsonSerializable(typeof(UserPromptSubmitHookInput))]
[JsonSerializable(typeof(StopHookInput))]
[JsonSerializable(typeof(NotificationSession))]
[JsonSerializable(typeof(PromptObservation))]
[JsonSerializable(typeof(NotificationTurn))]
[JsonSerializable(typeof(NotificationSummary))]
[JsonSerializable(typeof(StopObservation))]
[JsonSerializable(typeof(NotificationRecord))]
[JsonSerializable(typeof(CurrentNotificationState))]
[JsonSerializable(typeof(TelegramSendMessageRequest))]
[JsonSerializable(typeof(TelegramApiResponse))]
[JsonSerializable(typeof(UserHookSettingsDocument))]
[JsonSerializable(typeof(VsCodeUserSettingsDocument))]
[JsonSerializable(typeof(UserHookEntry))]
internal sealed partial class AppJsonSerializerContext : JsonSerializerContext
{
}
