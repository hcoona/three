using System.ComponentModel;
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

        string role = arguments.Length > 0 ? arguments[0] : "";
        bool controlledHost = role == "controlled-native-host";
        bool heldCandidateCheck = role == "held-candidate-check";
        bool outputHandoffRoot = role == "output-handoff-root";
        bool outputHolder = role == "inherited-output-holder";
        if (
            arguments.Length is < 3 or > 5
            || (
                role != "hold"
                && !controlledHost
                && !heldCandidateCheck
                && !outputHandoffRoot
                && !outputHolder
            )
            || ((controlledHost || heldCandidateCheck || outputHolder) && arguments.Length != 3)
            || (outputHandoffRoot ? arguments.Length != 5 : arguments.Length > 4)
            || !IsOwnedPipeName(arguments[1])
            || !TryExitCode(arguments[2], out int exitCode)
            || (arguments.Length >= 4 && !IsOwnedPipeName(arguments[3]))
            || (outputHandoffRoot && !IsOwnedPipeName(arguments[4]))
        )
        {
            Console.Error.WriteLine("Invalid owned-helper protocol arguments.");
            return 64;
        }

        Process? descendant = null;
        bool handedOffDescendant = false;
        double? connectReturnedAtMilliseconds = null;
        double? readyWriteEnteredAtMilliseconds = null;
        bool? activeGuardCancellationRequestedAtReadyEntry = null;
        string attemptedOperation = "connect";
        string activeGuard = "connection";
        CancellationToken activeGuardToken = default;
        long startupStarted = Stopwatch.GetTimestamp();
        try
        {
            using NamedPipeClientStream pipe = new(
                ".",
                arguments[1],
                PipeDirection.InOut,
                PipeOptions.Asynchronous
            );
            using NamedPipeClientStream? startGrant = outputHandoffRoot
                ? new(".", arguments[4], PipeDirection.InOut, PipeOptions.Asynchronous)
                : null;
            using (CancellationTokenSource connectionGuard = new(TimeSpan.FromSeconds(5)))
            {
                activeGuardToken = connectionGuard.Token;
                await (startGrant ?? pipe).ConnectAsync(activeGuardToken).ConfigureAwait(false);
            }
            connectReturnedAtMilliseconds = Stopwatch
                .GetElapsedTime(startupStarted)
                .TotalMilliseconds;
            using CancellationTokenSource interaction = new(
                TimeSpan.FromSeconds(controlledHost ? 180 : 120)
            );
            activeGuard = "interaction";
            activeGuardToken = interaction.Token;
            if (startGrant is not null)
            {
                attemptedOperation = "holder-start-grant";
                bool granted = await ReceiveHolderStartGrantAsync(startGrant, activeGuardToken)
                    .ConfigureAwait(false);
                startGrant.Dispose();
                if (!granted)
                {
                    Console.Error.WriteLine("Owned holder startup was not granted.");
                    return 65;
                }

                attemptedOperation = "handoff-connect";
                activeGuard = "handoff-connection";
                using CancellationTokenSource handoffConnection =
                    CancellationTokenSource.CreateLinkedTokenSource(interaction.Token);
                handoffConnection.CancelAfter(TimeSpan.FromSeconds(5));
                activeGuardToken = handoffConnection.Token;
                await pipe.ConnectAsync(activeGuardToken).ConfigureAwait(false);
                activeGuard = "interaction";
                activeGuardToken = interaction.Token;
            }
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

            if (arguments.Length >= 4)
            {
                ProcessStartInfo start = new(
                    Environment.ProcessPath
                        ?? throw new InvalidOperationException("Own apphost path is unavailable.")
                )
                {
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    // Keep stdout inherited; give the handoff holder a separate stderr.
                    RedirectStandardError = outputHandoffRoot,
                };
                start.ArgumentList.Add(outputHandoffRoot ? "inherited-output-holder" : "hold");
                start.ArgumentList.Add(arguments[3]);
                start.ArgumentList.Add("0");
                descendant =
                    Process.Start(start)
                    ?? throw new InvalidOperationException("Owned descendant did not start.");
            }

            using Process self = Process.GetCurrentProcess();
            string ready = ReadyFrame(self, descendant);
            attemptedOperation = "ready-write";
            readyWriteEnteredAtMilliseconds = Stopwatch
                .GetElapsedTime(startupStarted)
                .TotalMilliseconds;
            activeGuardCancellationRequestedAtReadyEntry =
                activeGuardToken.IsCancellationRequested;
            await writer.WriteLineAsync(ready.AsMemory(), activeGuardToken).ConfigureAwait(false);
            attemptedOperation = "release-read";
            string? release = await reader
                .ReadLineAsync(activeGuardToken)
                .ConfigureAwait(false);
            if (release != "RELEASE")
            {
                Console.Error.WriteLine("Owned helper was not explicitly released.");
                return 65;
            }

            int reportedExit = outputHolder ? exitCode : ReportExit(exitCode);
            // Only RELEASE transfers custody. On EOF or setup failure the root still joins
            // its descendant; the test sends RELEASE only after acquiring the holder.
            handedOffDescendant = outputHandoffRoot;
            return reportedExit;
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
                        ["activeGuardCancellationRequestedAtReadyEntry"] =
                            activeGuardCancellationRequestedAtReadyEntry,
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
                    if (outputHandoffRoot)
                    {
                        if (!handedOffDescendant)
                        {
                            await StopOutputHolderAsync(descendant).ConfigureAwait(false);
                        }
                    }
                    else
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
    }

    private static async Task<bool> ReceiveHolderStartGrantAsync(
        NamedPipeClientStream pipe, CancellationToken cancellationToken
    )
    {
        using StreamReader reader = new(pipe, Encoding.UTF8, false, 1024, leaveOpen: true);
        using StreamWriter writer = new(pipe, new UTF8Encoding(false), 1024, leaveOpen: true)
        {
            AutoFlush = true,
        };
        using Process self = Process.GetCurrentProcess();
        await writer.WriteLineAsync(ReadyFrame(self, null).AsMemory(), cancellationToken)
            .ConfigureAwait(false);
        return await reader.ReadLineAsync(cancellationToken).ConfigureAwait(false) == "RELEASE";
    }

    private static string ReadyFrame(Process self, Process? descendant) =>
        string.Join(
            "|",
            "READY",
            self.Id.ToString(CultureInfo.InvariantCulture),
            self.StartTime.ToUniversalTime().Ticks.ToString(CultureInfo.InvariantCulture),
            (descendant?.Id ?? 0).ToString(CultureInfo.InvariantCulture),
            (descendant?.StartTime.ToUniversalTime().Ticks ?? 0).ToString(
                CultureInfo.InvariantCulture
            )
        );

    private static async Task StopOutputHolderAsync(Process holder)
    {
        Exception? stopFailure = null;
        try
        {
            if (!holder.HasExited)
            {
                holder.Kill();
            }
        }
        catch (Exception error) when (
            error is (InvalidOperationException or Win32Exception) && holder.HasExited
        )
        { }
        catch (Exception error)
        {
            stopFailure = error;
            throw;
        }
        finally
        {
            try
            {
                using CancellationTokenSource exit = new(TimeSpan.FromSeconds(5));
                await holder.WaitForExitAsync(exit.Token).ConfigureAwait(false);
            }
            catch (Exception error) when (stopFailure is not null)
            {
                stopFailure.Data["OutputHolderExitJoin"] = error;
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
