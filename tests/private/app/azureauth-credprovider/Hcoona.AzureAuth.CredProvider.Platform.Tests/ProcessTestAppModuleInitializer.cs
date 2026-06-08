using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Text;

namespace Hcoona.AzureAuth.CredProvider.Platform.Tests;

internal static class ProcessTestAppModuleInitializer
{
    private const string HelperEnabledVariable = "AZUREAUTH_PROCESS_HELPER";
    private const string HelperEnabledValue = "1";

    [ModuleInitializer]
    public static void Initialize()
    {
        if (
            !string.Equals(
                Environment.GetEnvironmentVariable(HelperEnabledVariable),
                HelperEnabledValue,
                StringComparison.Ordinal
            )
        )
        {
            return;
        }

        var args = Environment.GetCommandLineArgs().Skip(1).ToArray();
        if (
            args.Length < 2
            || !string.Equals(args[0], "--process-helper", StringComparison.Ordinal)
        )
        {
            return;
        }

        switch (args[1])
        {
            case "inspect":
                Inspect(args.Skip(2).ToArray());
                break;
            case "exit":
                Exit(args.Skip(2).ToArray());
                break;
            case "sleep":
                Thread.Sleep(TimeSpan.FromSeconds(30));
                Environment.Exit(0);
                break;
            case "read-env":
                ReadEnvironment(args.Skip(2).ToArray());
                break;
            case "read-env-list":
                ReadEnvironmentList(args.Skip(2).ToArray());
                break;
            case "spawn-child-and-sleep":
                SpawnChildAndSleep(args.Skip(2).ToArray());
                break;
            case "write-marker":
                WriteMarker(args.Skip(2).ToArray());
                break;
            case "utf8-roundtrip":
                Utf8Roundtrip();
                break;
        }
    }

    private static void Inspect(string[] args)
    {
        var helperValue =
            Environment.GetEnvironmentVariable("AZUREAUTH_PROCESS_HELPER_VALUE") ?? string.Empty;

        Console.WriteLine($"cwd={Encode(Environment.CurrentDirectory)}");
        Console.WriteLine($"env={Encode(helperValue)}");
        Console.WriteLine($"stdin={Encode(Console.In.ReadToEnd())}");
        Console.WriteLine($"argc={Encode(args.Length.ToString(provider: null))}");

        for (var index = 0; index < args.Length; index++)
        {
            Console.WriteLine($"arg{index}={Encode(args[index])}");
        }

        Console.Error.WriteLine($"stderr={Encode("standard error with spaces")}");
        Environment.Exit(0);
    }

    private static void Exit(string[] args)
    {
        var exitCode = int.Parse(args[0], provider: null);
        Console.WriteLine($"stdout={Encode("nonzero stdout")}");
        Console.Error.WriteLine($"stderr={Encode("nonzero stderr")}");
        Environment.Exit(exitCode);
    }

    private static void ReadEnvironment(string[] args)
    {
        Console.WriteLine(
            $"env={Encode(Environment.GetEnvironmentVariable(args[0]) ?? string.Empty)}"
        );
        Environment.Exit(0);
    }

    private static void ReadEnvironmentList(string[] args)
    {
        for (var index = 0; index < args.Length; index++)
        {
            var value = Environment.GetEnvironmentVariable(args[index]) ?? string.Empty;
            Console.WriteLine($"env{index}={Encode(value)}");
        }

        Environment.Exit(0);
    }

    private static void SpawnChildAndSleep(string[] args)
    {
        using var child = Process.Start(
            new ProcessStartInfo
            {
                FileName = Environment.ProcessPath ?? "dotnet",
                UseShellExecute = false,
                RedirectStandardInput = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
            }.WithArguments([Environment.GetCommandLineArgs()[0], "--process-helper", "sleep"])
        );

        if (child is null)
        {
            Environment.Exit(1);
        }

        child.StandardInput.Close();
        File.WriteAllText(args[0], child.Id.ToString(provider: null));
        Thread.Sleep(TimeSpan.FromSeconds(30));
        Environment.Exit(0);
    }

    private static void WriteMarker(string[] args)
    {
        File.WriteAllText(args[0], "launched");
        Environment.Exit(0);
    }

    private static void Utf8Roundtrip()
    {
        using var input = Console.OpenStandardInput();
        using var output = Console.OpenStandardOutput();
        using var error = Console.OpenStandardError();
        using var memory = new MemoryStream();
        input.CopyTo(memory);
        var standardInput = Encoding.UTF8.GetString(memory.ToArray());

        WriteUtf8(output, "stdout=" + standardInput + " / 雪 🌍");
        WriteUtf8(error, "stderr=" + standardInput + " / café");
        Environment.Exit(0);
    }

    private static void WriteUtf8(Stream stream, string value)
    {
        stream.Write(Encoding.UTF8.GetBytes(value));
        stream.Flush();
    }

    private static string Encode(string value)
    {
        return Convert.ToBase64String(Encoding.UTF8.GetBytes(value));
    }

    private static ProcessStartInfo WithArguments(
        this ProcessStartInfo startInfo,
        IEnumerable<string> arguments
    )
    {
        foreach (var argument in arguments)
        {
            startInfo.ArgumentList.Add(argument);
        }

        return startInfo;
    }
}
