namespace Hcoona.VsCodeCopilotTelegramHook;

internal static class HookSessionIdentity
{
    private static readonly string[] ToolCallSessionPrefixes =
    [
        "call_",
        "toolu_",
        "toulu_",
    ];

    public static bool IsToolCallSession(string? sessionId)
    {
        if (string.IsNullOrWhiteSpace(sessionId))
        {
            return false;
        }

        return ToolCallSessionPrefixes.Any(
            prefix => sessionId.StartsWith(prefix, StringComparison.Ordinal));
    }
}
