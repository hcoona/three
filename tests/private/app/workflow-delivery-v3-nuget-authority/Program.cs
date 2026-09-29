using System.Globalization;
using Microsoft.Testing.Platform.Builder;

namespace WorkflowDeliveryV3NuGetAuthority.Tests;

internal static class Program
{
    public static async Task<int> Main(string[] args)
    {
        if (args is ["--authority-process-probe", string pidFile])
        {
            Console.WriteLine("probe-stdout");
            Console.Error.WriteLine("probe-stderr");
            await File.WriteAllTextAsync(
                pidFile, Environment.ProcessId.ToString(CultureInfo.InvariantCulture));
            // Finite fallback if the parent test runner is forcibly terminated.
            await Task.Delay(TimeSpan.FromMinutes(2));
            return 1;
        }

        if (args.Length > 0 && args[0] == "collect")
        {
            string output = args[Array.IndexOf(args, "--output") + 1];
            string root = Directory.GetParent(Path.GetDirectoryName(output)!)!.FullName;
            Console.WriteLine("collector-started");
            await File.WriteAllTextAsync(
                Path.Combine(root, "collector.pid"),
                Environment.ProcessId.ToString(CultureInfo.InvariantCulture));
            await Task.Delay(TimeSpan.FromMinutes(2));
            return 1;
        }

        ITestApplicationBuilder builder = await TestApplication.CreateBuilderAsync(args);
        builder.AddSelfRegisteredExtensions(args);
        using ITestApplication app = await builder.BuildAsync();
        return await app.RunAsync();
    }
}
