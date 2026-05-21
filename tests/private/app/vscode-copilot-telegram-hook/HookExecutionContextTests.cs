using Xunit;

namespace Hcoona.VsCodeCopilotTelegramHook.Tests;

public sealed class HookExecutionContextTests : IDisposable
{
    private readonly string? originalSurface = Environment.GetEnvironmentVariable(
        AppConstants.ManagedHookSurfaceEnvironmentVariable);

    public void Dispose()
    {
        Environment.SetEnvironmentVariable(
            AppConstants.ManagedHookSurfaceEnvironmentVariable,
            originalSurface);
    }

    [Fact]
    public void GetSurfaceDefaultsToVsCode()
    {
        Environment.SetEnvironmentVariable(
            AppConstants.ManagedHookSurfaceEnvironmentVariable,
            null);

        HookExecutionContext context = new();

        Assert.Equal(HookSurface.VsCode, context.GetSurface());
    }

    [Fact]
    public void GetSurfaceReturnsCopilotCliForManagedSurfaceEnvironmentVariable()
    {
        Environment.SetEnvironmentVariable(
            AppConstants.ManagedHookSurfaceEnvironmentVariable,
            AppConstants.ManagedHookCopilotCliSurfaceValue);

        HookExecutionContext context = new();

        Assert.Equal(HookSurface.CopilotCli, context.GetSurface());
    }

    [Fact]
    public void GetSurfaceDefaultsToVsCodeForUnknownSurfaceEnvironmentVariable()
    {
        Environment.SetEnvironmentVariable(
            AppConstants.ManagedHookSurfaceEnvironmentVariable,
            "unknown-surface");

        HookExecutionContext context = new();

        Assert.Equal(HookSurface.VsCode, context.GetSurface());
    }
}
