using Xunit;

namespace Hcoona.VsCodeCopilotTelegramHook.Tests;

public sealed class CopilotCliRuntimeProbeTests
{
    [Theory]
    [InlineData("GitHub Copilot CLI 1.0.74-2", "1.0.74")]
    [InlineData("GitHub Copilot CLI 1.0.41", "1.0.41")]
    [InlineData("GitHub Copilot CLI 2.0", "2.0")]
    public void TryParseVersionAcceptsCopilotVersionOutput(
        string output,
        string expectedVersion)
    {
        bool parsed = CopilotCliRuntimeProbe.TryParseVersion(
            output,
            out Version? version);

        Assert.True(parsed);
        Assert.Equal(Version.Parse(expectedVersion), version);
    }

    [Theory]
    [InlineData("")]
    [InlineData("GitHub Copilot CLI ")]
    [InlineData("GitHub Copilot CLI unknown")]
    [InlineData("build completed on 2026-07-22")]
    public void TryParseVersionRejectsUnknownOutput(string output)
    {
        Assert.False(
            CopilotCliRuntimeProbe.TryParseVersion(output, out Version? version));
        Assert.Null(version);
    }
}
