using System.Diagnostics;
using System.Globalization;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text;

namespace WorkflowDelivery.Tests.Platform;

// Runs this test assembly's own apphost as an inert child process, so process-boundary tests
// need no external interpreter.
internal static class TestProcess
{
    internal const string GuardVariable = "WORKFLOW_DELIVERY_TEST_PROCESS";
    private const string Switch = "--workflow-delivery-test-process";

    internal static string Executable { get; } = ResolveExecutable();

    internal static string[] Arguments(params string[] command) => [Switch, .. command];

    // Inherited unless a command clears the environment; then the caller passes it explicitly.
    internal static Dictionary<string, string?> Environment() => new()
    {
        [GuardVariable] = "1",
    };

    [ModuleInitializer]
    internal static void Dispatch()
    {
        string[] arguments = System.Environment.GetCommandLineArgs();
        if (System.Environment.GetEnvironmentVariable(GuardVariable) != "1" ||
            arguments.Length < 3 || arguments[1] != Switch)
            return;
        System.Environment.Exit(Run(arguments[2], arguments[3..]));
    }

    private static int Run(string command, string[] arguments)
    {
        switch (command)
        {
            case "stdin-length":
                Console.Out.Write(Console.In.ReadToEnd().Length);
                return 0;
            case "exit":
                Console.Out.Write(arguments[1]);
                Console.Error.Write(arguments[2]);
                return int.Parse(arguments[0], CultureInfo.InvariantCulture);
            case "variables":
                Console.Out.Write(string.Join('|', arguments.Select(
                    name => System.Environment.GetEnvironmentVariable(name) ?? "")));
                return 0;
            case "bytes":
                WriteBytes(Console.OpenStandardOutput(), arguments[0]);
                WriteBytes(Console.OpenStandardError(), arguments[1]);
                if (arguments.Length > 2) Sleep();
                return 0;
            case "flood":
                (arguments[0] == "stdout" ? Console.Out : Console.Error)
                    .Write(new string('x', 32 * 1024 * 1024 + 1));
                Sleep();
                return 0;
            case "marker":
                File.WriteAllText(arguments[0], "started");
                return 0;
            case "pid":
                string ids = System.Environment.ProcessId.ToString(CultureInfo.InvariantCulture);
                if (arguments.Length > 1) ids += " " + StartDescendant();
                File.WriteAllText(arguments[0] + ".tmp", ids);
                File.Move(arguments[0] + ".tmp", arguments[0]);
                Sleep();
                return 0;
            case "sleep":
                Sleep();
                return 0;
            default:
                Console.Error.Write("Unknown test process command: " + command);
                return 64;
        }
    }

    private static void WriteBytes(Stream stream, string base64)
    {
        byte[] bytes = Convert.FromBase64String(base64);
        stream.Write(bytes);
        stream.Flush();
    }

    private static int StartDescendant()
    {
        var start = new ProcessStartInfo(Executable)
        {
            UseShellExecute = false,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };
        foreach (string argument in Arguments("sleep")) start.ArgumentList.Add(argument);
        start.Environment[GuardVariable] = "1";
        return Process.Start(start)!.Id;
    }

    private static void Sleep() => Thread.Sleep(TimeSpan.FromSeconds(60));

    private static string ResolveExecutable()
    {
        string assembly = Assembly.GetExecutingAssembly().Location;
        string path = Path.Combine(Path.GetDirectoryName(assembly)!,
            Path.GetFileNameWithoutExtension(assembly) +
            (OperatingSystem.IsWindows() ? ".exe" : ""));
        if (!File.Exists(path))
            throw new FileNotFoundException("The test apphost is unavailable.", path);
        return path;
    }

    internal static string Base64(string text) =>
        Convert.ToBase64String(Encoding.UTF8.GetBytes(text));
}
