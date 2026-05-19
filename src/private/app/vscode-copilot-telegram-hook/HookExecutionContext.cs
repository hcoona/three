namespace Hcoona.VsCodeCopilotTelegramHook;

internal enum HookSurface
{
    VsCode,
    CopilotCli,
}

internal sealed class HookExecutionContext(HookSurface? forcedSurface = null)
{
    public HookSurface GetSurface()
    {
        if (forcedSurface is not null)
        {
            return forcedSurface.Value;
        }

        string? surface = Environment.GetEnvironmentVariable(
            AppConstants.ManagedHookSurfaceEnvironmentVariable);
        return string.Equals(
            surface,
            AppConstants.ManagedHookCopilotCliSurfaceValue,
            StringComparison.OrdinalIgnoreCase)
                ? HookSurface.CopilotCli
                : HookSurface.VsCode;
    }
}
