using Microsoft.Testing.Platform.Builder;

namespace Authentication.Windows.Scenarios;

internal static class Program
{
    internal static string? NativeCliExecutable { get; private set; }

    private static async Task<int> Main(string[] arguments)
    {
        var entry = TimeProvider.System.GetTimestamp();
        if (arguments.Length >= 2 && arguments[0] == "--scenario-child")
        {
            return ProcessChild.Run(arguments[1], arguments[2..], entry);
        }

        // These modes belong only to the test executable. They never reach the CLI.
        if (arguments.Length > 0 && arguments[0] == "--native-profile-cases")
        {
            if (arguments.Length != 2) return 90;
            var executable = ProcessFixture.RequireNativeCliExecutable(arguments[1]);
            return await NativeProfileScenarios.RunAsync(executable);
        }
        if (arguments.Length > 0 && arguments[0] == "--native-cli-executable")
        {
            if (arguments.Length < 2) return 90;
            NativeCliExecutable = ProcessFixture.RequireNativeCliExecutable(arguments[1]);
            arguments = arguments[2..];
        }

        var builder = await TestApplication.CreateBuilderAsync(arguments);
        // This finite harness admits only the framework and its TRX report.
        // Optional package hooks must not enlarge the reviewed process topology.
        Microsoft.VisualStudio.TestTools.UnitTesting.TestingPlatformBuilderHook.AddExtensions(builder, arguments);
        Microsoft.Testing.Extensions.TrxReport.TestingPlatformBuilderHook.AddExtensions(builder, arguments);
        using var application = await builder.BuildAsync();
        return await application.RunAsync();
    }
}
