using Xunit;

namespace Hcoona.VsCodeCopilotTelegramHook.Tests;

internal static class FileAssertions
{
    public static void AssertOwnerOnlyFileMode(string path)
    {
        if (OperatingSystem.IsWindows())
        {
            return;
        }

        Assert.Equal(
            UnixFileMode.UserRead | UnixFileMode.UserWrite,
            File.GetUnixFileMode(path));
    }
}
