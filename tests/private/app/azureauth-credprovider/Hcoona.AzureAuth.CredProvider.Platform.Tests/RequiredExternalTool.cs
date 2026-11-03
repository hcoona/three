using System.Diagnostics.CodeAnalysis;
using Xunit;

namespace Hcoona.AzureAuth.CredProvider.Platform.Tests;

// CI must run real-tool tests, so a missing tool fails there instead of skipping.
internal static class RequiredExternalTool
{
    public static void Require([DoesNotReturnIf(false)] bool available, string reason)
    {
        if (available)
        {
            return;
        }

        if (
            string.Equals(
                Environment.GetEnvironmentVariable("CI"),
                "true",
                StringComparison.OrdinalIgnoreCase
            )
        )
        {
            Assert.Fail(reason + " CI must run this test.");
        }

        Assert.Skip(reason);
    }
}
