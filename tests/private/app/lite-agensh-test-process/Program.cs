using System.Diagnostics;
using System.Globalization;
using System.IO.Pipes;
using System.Text;

namespace LiteAgensh.TestProcess;

internal static class Program
{
    private static async Task<int> Main(string[] arguments)
    {
        if (
            arguments.Length == 2
            && arguments[0] == "exit"
            && TryExitCode(arguments[1], out int immediateCode)
        )
        {
            return ReportExit(immediateCode);
        }

        bool controlledHost = arguments.Length > 0 && arguments[0] == "controlled-native-host";
        bool heldCandidateCheck = arguments.Length > 0 && arguments[0] == "held-candidate-check";
        if (
            arguments.Length is < 3 or > 4
            || (arguments[0] != "hold" && !controlledHost && !heldCandidateCheck)
            || ((controlledHost || heldCandidateCheck) && arguments.Length != 3)
            || !IsOwnedPipeName(arguments[1])
            || !TryExitCode(arguments[2], out int exitCode)
            || (arguments.Length == 4 && !IsOwnedPipeName(arguments[3]))
        )
        {
            Console.Error.WriteLine("Invalid owned-helper protocol arguments.");
            return 64;
        }

        Process? descendant = null;
        try
        {
            using CancellationTokenSource guard = new(TimeSpan.FromSeconds(5));
            using NamedPipeClientStream pipe = new(
                ".",
                arguments[1],
                PipeDirection.InOut,
                PipeOptions.Asynchronous
            );
            await pipe.ConnectAsync(guard.Token).ConfigureAwait(false);
            using StreamReader reader = new(pipe, Encoding.UTF8, false, 1024, leaveOpen: true);
            using StreamWriter writer = new(pipe, new UTF8Encoding(false), 1024, leaveOpen: true)
            {
                AutoFlush = true,
            };

            if (arguments.Length == 4)
            {
                ProcessStartInfo start = new(
                    Environment.ProcessPath
                        ?? throw new InvalidOperationException("Own apphost path is unavailable.")
                )
                {
                    UseShellExecute = false,
                    CreateNoWindow = true,
                };
                start.ArgumentList.Add("hold");
                start.ArgumentList.Add(arguments[3]);
                start.ArgumentList.Add("0");
                descendant =
                    Process.Start(start)
                    ?? throw new InvalidOperationException("Owned descendant did not start.");
            }

            using Process self = Process.GetCurrentProcess();
            string ready = string.Join(
                "|",
                "READY",
                self.Id.ToString(CultureInfo.InvariantCulture),
                self.StartTime.ToUniversalTime().Ticks.ToString(CultureInfo.InvariantCulture),
                (descendant?.Id ?? 0).ToString(CultureInfo.InvariantCulture),
                (descendant?.StartTime.ToUniversalTime().Ticks ?? 0).ToString(
                    CultureInfo.InvariantCulture
                )
            );
            await writer.WriteLineAsync(ready.AsMemory(), guard.Token).ConfigureAwait(false);
            // Connection and READY retain the five-second guard. Holding an owned
            // witness uses its case120 bound; hosts also retain stop/teardown time.
            using CancellationTokenSource postReadyHold = new(
                TimeSpan.FromSeconds(controlledHost ? 180 : 120)
            );
            string? release = await reader
                .ReadLineAsync(postReadyHold.Token)
                .ConfigureAwait(false);
            if (release != "RELEASE")
            {
                Console.Error.WriteLine("Owned helper was not explicitly released.");
                return 65;
            }

            return ReportExit(exitCode);
        }
        catch (OperationCanceledException)
        {
            Console.Error.WriteLine("Owned-helper protocol failure bound reached.");
            return 90;
        }
        finally
        {
            if (descendant is not null)
            {
                using (descendant)
                {
                    if (!descendant.HasExited)
                    {
                        descendant.Kill(entireProcessTree: true);
                    }

                    await descendant
                        .WaitForExitAsync(CancellationToken.None)
                        .WaitAsync(TimeSpan.FromSeconds(5), CancellationToken.None)
                        .ConfigureAwait(false);
                }
            }
        }
    }

    private static bool TryExitCode(string text, out int code) =>
        int.TryParse(text, NumberStyles.None, CultureInfo.InvariantCulture, out code)
        && code is >= 0 and <= 125;

    private static bool IsOwnedPipeName(string text)
    {
        const string prefix = "lite-agensh-own-";
        return text.StartsWith(prefix, StringComparison.Ordinal)
            && Guid.TryParseExact(text.AsSpan(prefix.Length), "N", out _);
    }

    private static int ReportExit(int code)
    {
        Console.WriteLine($"owned check exit {code.ToString(CultureInfo.InvariantCulture)}");
        if (code != 0)
        {
            Console.Error.WriteLine(
                $"owned check failed {code.ToString(CultureInfo.InvariantCulture)}"
            );
        }

        return code;
    }
}
