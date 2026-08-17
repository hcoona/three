using Hcoona.AzureAuth.CredProvider.Platform.AdapterHost;

namespace Hcoona.AzureAuth.CredProvider.Platform.Tests;

internal static class NuGetPluginProtocolTestProcess
{
    internal const string Command = "nuget-plugin-protocol";

    internal static void Run()
    {
        int exitCode = new NuGetPluginAdapter().RunPluginAsync().GetAwaiter().GetResult();
        Console.Out.Flush();
        Console.Error.Flush();
        Environment.Exit(exitCode);
    }
}
