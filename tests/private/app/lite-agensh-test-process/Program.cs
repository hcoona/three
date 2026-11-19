using System.Diagnostics;
using System.Globalization;
using System.IO.Pipes;
using System.Text;
using System.Text.Json.Nodes;

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
        double? connectReturnedAtMilliseconds = null;
        double? readyWriteEnteredAtMilliseconds = null;
        bool? startupCancelledAtReadyEntry = null;
        string attemptedOperation = "connect";
        string activeGuard = "startup";
        CancellationToken activeGuardToken = default;
        long startupStarted = Stopwatch.GetTimestamp();
        try
        {
            using CancellationTokenSource guard = new(TimeSpan.FromSeconds(5));
            activeGuardToken = guard.Token;
            using NamedPipeClientStream pipe = new(
                ".",
                arguments[1],
                PipeDirection.InOut,
                PipeOptions.Asynchronous
            );
            await pipe.ConnectAsync(activeGuardToken).ConfigureAwait(false);
            connectReturnedAtMilliseconds = Stopwatch
                .GetElapsedTime(startupStarted)
                .TotalMilliseconds;
            using StreamReader reader = new(pipe, Encoding.UTF8, false, 1024, leaveOpen: true);
            using StreamWriter writer = new(pipe, new UTF8Encoding(false), 1024, leaveOpen: true)
            {
                AutoFlush = true,
            };
            if (heldCandidateCheck)
            {
                Console.WriteLine("owned check diagnostic stdout before READY");
                Console.Error.WriteLine("owned check diagnostic stderr before READY");
            }

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
            attemptedOperation = "ready-write";
            readyWriteEnteredAtMilliseconds = Stopwatch
                .GetElapsedTime(startupStarted)
                .TotalMilliseconds;
            startupCancelledAtReadyEntry = activeGuardToken.IsCancellationRequested;
            await writer.WriteLineAsync(ready.AsMemory(), activeGuardToken).ConfigureAwait(false);
            // Connection and READY retain the five-second guard. Holding an owned
            // witness uses its case120 bound; hosts also retain stop/teardown time.
            using CancellationTokenSource postReadyHold = new(
                TimeSpan.FromSeconds(controlledHost ? 180 : 120)
            );
            attemptedOperation = "release-read";
            activeGuard = "hold";
            activeGuardToken = postReadyHold.Token;
            string? release = await reader
                .ReadLineAsync(activeGuardToken)
                .ConfigureAwait(false);
            if (release != "RELEASE")
            {
                Console.Error.WriteLine("Owned helper was not explicitly released.");
                return 65;
            }

            return ReportExit(exitCode);
        }
        catch (OperationCanceledException error)
        {
            Console.Error.WriteLine(
                "Owned-helper protocol failure bound reached. "
                    + new JsonObject
                    {
                        ["attemptedOperation"] = attemptedOperation,
                        ["activeGuard"] = activeGuard,
                        ["connectReturnedAtMilliseconds"] = connectReturnedAtMilliseconds,
                        ["readyWriteEnteredAtMilliseconds"] = readyWriteEnteredAtMilliseconds,
                        ["startupCancelledAtReadyEntry"] = startupCancelledAtReadyEntry,
                        ["activeGuardCancellationRequestedAtCatch"] =
                            activeGuardToken.IsCancellationRequested,
                        ["exceptionTokenMatchesActiveGuard"] =
                            error.CancellationToken == activeGuardToken,
                        ["helperElapsedAtCatchMilliseconds"] = Stopwatch
                            .GetElapsedTime(startupStarted)
                            .TotalMilliseconds,
                    }.ToJsonString()
            );
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
