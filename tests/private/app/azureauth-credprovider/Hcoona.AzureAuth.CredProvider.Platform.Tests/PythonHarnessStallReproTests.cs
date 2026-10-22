using System.Diagnostics;
using System.Globalization;
using System.Text;
using Hcoona.AzureAuth.CredProvider.Platform.Processes;
using Hcoona.AzureAuth.CredProvider.Platform.Tests.TestDoubles;
using Xunit;

namespace Hcoona.AzureAuth.CredProvider.Platform.Tests;

// Temporary bounded reproduction for Issue #1395. This file is never merged.
public sealed partial class PythonPhase11ProductDoctorTests
{
    // The original test contributes the 50th launch of each run.
    private const int ReproLaunchLimit = 49;
    private static readonly TimeSpan ReproLaunchTimeout = TimeSpan.FromSeconds(10);
    private static readonly TimeSpan ReproSlowThreshold = TimeSpan.FromSeconds(5);

    [Fact]
    public async Task ReproduceWindowsPythonHarnessStall()
    {
        Assert.SkipUnless(OperatingSystem.IsWindows(), "Windows-only stall reproduction.");
        (_, FakeProcessRunner runner, _) = await RunPosixDoctorAsync(ProductHealthyResult());
        string productProbeScript = Assert.IsType<string>(runner.StartSpecs[1].Arguments[1]);
        CancellationToken cancellationToken = TestContext.Current.CancellationToken;

        var report = new StringBuilder();
        report.AppendLine(CultureInfo.InvariantCulture, $"reproStartUtc={Now()}");
        report.AppendLine(
            CultureInfo.InvariantCulture,
            $"processorCount={Environment.ProcessorCount}"
        );
        report.AppendLine(CultureInfo.InvariantCulture,
            $"resolved={ResolveCommandForDiagnostics("python") ?? "<unresolved>"}");
        report.AppendLine(CultureInfo.InvariantCulture,
            $"PATH={Environment.GetEnvironmentVariable("PATH")}");
        string[] keys =
            ["PYTHONHOME", "PYTHONPATH", "PYTHONSTARTUP", "VIRTUAL_ENV", "MISE_SHIMS_DIR"];
        foreach (string key in keys)
        {
            report.AppendLine(CultureInfo.InvariantCulture,
                $"{key}={Environment.GetEnvironmentVariable(key) ?? "<unset>"}");
        }

        var wallTimes = new List<double>();
        int stalls = 0;
        for (int index = 0; index < ReproLaunchLimit; index++)
        {
            LaunchRecord record = await LaunchHarnessAsync(productProbeScript, cancellationToken);
            wallTimes.Add(record.WallMilliseconds);
            report.AppendLine(CultureInfo.InvariantCulture,
                $"launch#{index} startUtc={record.StartUtc} wallMs={record.WallMilliseconds:F0} "
                    + $"preScriptMs={Format(record.PreScriptMilliseconds)} "
                    + $"exit={(record.TimedOut ? "<timeout>" : record.ExitCode)} "
                    + $"exe={record.Executable ?? "?"}");
            report.AppendLine(CultureInfo.InvariantCulture, $"  slowImports={record.SlowImports}");
            if (record.WallMilliseconds >= ReproSlowThreshold.TotalMilliseconds || record.TimedOut)
            {
                report.AppendLine(
                    CultureInfo.InvariantCulture,
                    $"  SLOW snapshots={record.Snapshots}"
                );
                report.AppendLine(
                    CultureInfo.InvariantCulture,
                    $"  stdout={record.StandardOutput}"
                );
                report.AppendLine(CultureInfo.InvariantCulture, $"  stderr={record.StandardError}");
            }

            if (record.TimedOut)
            {
                stalls++;
                break;
            }
        }

        string defender = await QueryDefenderAsync(cancellationToken);
        report.AppendLine(CultureInfo.InvariantCulture, $"defender={defender}");
        double[] sorted = [.. wallTimes.Order()];
        string summary = string.Create(
            CultureInfo.InvariantCulture,
            $"AZSTALL_SUMMARY count={sorted.Length} first={wallTimes[0]:F0} "
                + $"median={Percentile(sorted, 0.5):F0} p95={Percentile(sorted, 0.95):F0} "
                + $"max={sorted[^1]:F0} "
                + $"slow={sorted.Count(value => value >= ReproSlowThreshold.TotalMilliseconds)} "
                + $"stalls={stalls}"
        );
        report.Insert(0, summary + Environment.NewLine);

        string outputDirectory =
            Environment.GetEnvironmentVariable("TESTINGPLATFORM_DIAGNOSTIC_OUTPUT_DIRECTORY")
            ?? AppContext.BaseDirectory;
        Directory.CreateDirectory(outputDirectory);
        await File.WriteAllTextAsync(
            Path.Combine(outputDirectory, "azstall-repro.txt"),
            report.ToString(),
            cancellationToken
        );
        TestContext.Current.SendDiagnosticMessage(summary);

        Assert.True(stalls == 0, "AZSTALL_CAPTURED " + report);
    }

    private static string Format(double? value) =>
        value?.ToString("F0", CultureInfo.InvariantCulture) ?? "?";

    private static string Now() =>
        DateTimeOffset.UtcNow.ToString("O", CultureInfo.InvariantCulture);

    private static double Percentile(double[] sorted, double percentile) =>
        sorted[Math.Min(sorted.Length - 1, (int)Math.Ceiling(percentile * sorted.Length) - 1)];

    private static string BuildReproHarness(
        string productProbeScript,
        long dumpDeadlineMilliseconds
    ) =>
        "import time;_azstall_t0=time.time()\n"
        + "import sys;sys.stderr.write("
        + "'AZSTALL_T0=%r exe=%s\\n'%(_azstall_t0,sys.executable));sys.stderr.flush()\n"
        + "import faulthandler\n"
        + "faulthandler.dump_traceback_later(max(0.001,"
        + dumpDeadlineMilliseconds.ToString(CultureInfo.InvariantCulture)
        + "/1000-time.time()))\n"
        + """
        import abc,importlib.metadata,sys,types
        keyring_module=types.ModuleType('keyring')
        backend_module=types.ModuleType('keyring.backend')
        class KeyringBackend(abc.ABC):
            @property
            @abc.abstractmethod
            def priority(self):
                raise NotImplementedError
        backend_module.KeyringBackend=KeyringBackend
        keyring_module.backend=backend_module
        sys.modules['keyring']=keyring_module
        sys.modules['keyring.backend']=backend_module
        product_module=types.ModuleType('azureauth_credprovider_keyring.backend')
        class AzureAuthKeyringBackend(KeyringBackend):
            def get_password(self,*args): return None
            def get_credential(self,*args): return None
            def set_password(self,*args): return None
            def delete_password(self,*args): return None
        AzureAuthKeyringBackend.__module__='azureauth_credprovider_keyring.backend'
        product_module.AzureAuthKeyringBackend=AzureAuthKeyringBackend
        sys.modules['azureauth_credprovider_keyring.backend']=product_module
        class EntryPoint:
            group='keyring.backends'
            name='azureauth'
            value='azureauth_credprovider_keyring.backend:AzureAuthKeyringBackend'
            def load(self): return AzureAuthKeyringBackend
        class Distribution:
            entry_points=[EntryPoint()]
        importlib.metadata.distribution=lambda name: Distribution()
        import base64
        exec(base64.b64decode(
        """
        + "'"
        + Convert.ToBase64String(Encoding.UTF8.GetBytes(productProbeScript))
        + "'))";

    private static async Task<LaunchRecord> LaunchHarnessAsync(
        string productProbeScript,
        CancellationToken cancellationToken
    )
    {
        DateTimeOffset start = DateTimeOffset.UtcNow;
        long deadline = start.AddSeconds(9).ToUnixTimeMilliseconds();
        var encoding = new UTF8Encoding(false);
        var startInfo = new ProcessStartInfo
        {
            FileName = "python",
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            RedirectStandardInput = true,
            StandardOutputEncoding = encoding,
            StandardErrorEncoding = encoding,
            StandardInputEncoding = encoding,
            CreateNoWindow = true,
        };
        startInfo.ArgumentList.Add("-X");
        startInfo.ArgumentList.Add("importtime");
        startInfo.ArgumentList.Add("-c");
        startInfo.ArgumentList.Add(BuildReproHarness(productProbeScript, deadline));

        var stopwatch = Stopwatch.StartNew();
        using var process = new Process { StartInfo = startInfo };
        process.Start();
        process.StandardInput.Close();
        Task<string> stdoutTask = process.StandardOutput.ReadToEndAsync(cancellationToken);
        Task<string> stderrTask = process.StandardError.ReadToEndAsync(cancellationToken);
        Task exitTask = process.WaitForExitAsync(cancellationToken);
        var snapshots = new StringBuilder();
        bool timedOut = false;

        Task slowDelay = Task.Delay(ReproSlowThreshold, cancellationToken);
        if (await Task.WhenAny(exitTask, slowDelay) != exitTask)
        {
            snapshots.Append(
                CultureInfo.InvariantCulture,
                $"[@{stopwatch.ElapsedMilliseconds}ms {Snapshot(process)}]"
            );
            TimeSpan untilLate = TimeSpan.FromMilliseconds(9500) - stopwatch.Elapsed;
            if (untilLate > TimeSpan.Zero
                && await Task.WhenAny(exitTask, Task.Delay(untilLate, cancellationToken))
                    != exitTask)
            {
                snapshots.Append(
                CultureInfo.InvariantCulture,
                $"[@{stopwatch.ElapsedMilliseconds}ms {Snapshot(process)}]"
            );
            }

            TimeSpan remaining = ReproLaunchTimeout - stopwatch.Elapsed;
            if (remaining > TimeSpan.Zero)
            {
                await Task.WhenAny(exitTask, Task.Delay(remaining, cancellationToken));
            }

            if (!exitTask.IsCompleted)
            {
                timedOut = true;
                process.Kill(entireProcessTree: true);
                await process.WaitForExitAsync(cancellationToken);
            }
        }

        await exitTask.WaitAsync(TimeSpan.FromSeconds(5), cancellationToken);
        double wall = stopwatch.Elapsed.TotalMilliseconds;
        string stdout = await stdoutTask.WaitAsync(TimeSpan.FromSeconds(5), cancellationToken);
        string stderr = await stderrTask.WaitAsync(TimeSpan.FromSeconds(5), cancellationToken);

        double? preScript = null;
        string? executable = null;
        var slowImports = new List<string>();
        foreach (string line in stderr.Split('\n'))
        {
            string trimmed = line.TrimEnd('\r');
            if (trimmed.StartsWith("AZSTALL_T0=", StringComparison.Ordinal))
            {
                string[] parts = trimmed.Split(' ', 2);
                if (double.TryParse(parts[0]["AZSTALL_T0=".Length..], NumberStyles.Float,
                        CultureInfo.InvariantCulture, out double t0))
                {
                    preScript = (t0 * 1000) - start.ToUnixTimeMilliseconds();
                }

                executable = parts.Length > 1
                    ? parts[1].Replace("exe=", string.Empty, StringComparison.Ordinal)
                    : null;
            }
            else if (trimmed.StartsWith("import time:", StringComparison.Ordinal))
            {
                string[] columns = trimmed["import time:".Length..].Split('|');
                if (columns.Length == 3
                    && long.TryParse(columns[1].Trim(), NumberStyles.Integer,
                        CultureInfo.InvariantCulture, out long cumulative)
                    && cumulative >= 50_000)
                {
                    slowImports.Add(columns[2].Trim() + "=" + (cumulative / 1000) + "ms");
                }
            }
        }

        return new LaunchRecord(
            start.ToString("O", CultureInfo.InvariantCulture),
            wall,
            preScript,
            timedOut ? null : process.ExitCode,
            timedOut,
            executable,
            string.Join(",", slowImports),
            snapshots.ToString(),
            stdout,
            stderr
        );
    }

    private static string Snapshot(Process process)
    {
        if (!OperatingSystem.IsWindows())
        {
            return "non-windows";
        }

        var text = new StringBuilder();
        try
        {
            process.Refresh();
            text.Append(CultureInfo.InvariantCulture,
                $"pid={process.Id} cpuMs={process.TotalProcessorTime.TotalMilliseconds:F0} "
                    + $"userMs={process.UserProcessorTime.TotalMilliseconds:F0} "
                    + $"ws={process.WorkingSet64} handles={process.HandleCount} threads=");
            foreach (ProcessThread thread in process.Threads)
            {
                string wait = thread.ThreadState == System.Diagnostics.ThreadState.Wait
                    ? thread.WaitReason.ToString()
                    : "-";
                text.Append(CultureInfo.InvariantCulture,
                    $"{thread.Id}:{thread.ThreadState}/{wait}/"
                        + $"{thread.TotalProcessorTime.TotalMilliseconds:F0}ms;");
            }
        }
        catch (Exception exception) when (exception is InvalidOperationException
            or System.ComponentModel.Win32Exception or NotSupportedException)
        {
            text.Append(CultureInfo.InvariantCulture, $" threadSnapshotFailed={exception.Message}");
        }

        try
        {
            text.Append(" modules=");
            foreach (ProcessModule module in process.Modules)
            {
                text.Append(module.ModuleName).Append(';');
            }
        }
        catch (Exception exception) when (exception is InvalidOperationException
            or System.ComponentModel.Win32Exception or NotSupportedException)
        {
            text.Append(CultureInfo.InvariantCulture, $" moduleSnapshotFailed={exception.Message}");
        }

        return text.ToString();
    }

    private static async Task<string> QueryDefenderAsync(CancellationToken cancellationToken)
    {
        ProcessResult result = await new SystemProcessRunner().RunAsync(
            new ProcessStartSpec(
                "powershell",
                [
                    "-NoProfile",
                    "-NonInteractive",
                    "-Command",
                    "Get-MpComputerStatus | Select-Object AMRunningMode,RealTimeProtectionEnabled,"
                        + "OnAccessProtectionEnabled,BehaviorMonitorEnabled,IsTamperProtected "
                        + "| ConvertTo-Json -Compress; 'exclusions=' + "
                        + "((Get-MpPreference).ExclusionPath -join ';'); "
                        + "'exclusionProcesses=' + ((Get-MpPreference).ExclusionProcess -join ';')",
                ],
                timeout: TimeSpan.FromSeconds(60)
            ),
            cancellationToken
        );
        return $"{result.Status} {result.StandardOutput.ReplaceLineEndings(" ")} "
            + result.StandardError.ReplaceLineEndings(" ");
    }

    private static async Task<string> WatchOriginalLaunchAsync(
        DateTime launch,
        CancellationToken stop
    )
    {
        var evidence = new StringBuilder();
        foreach (TimeSpan at in new[] { ReproSlowThreshold, TimeSpan.FromMilliseconds(9500) })
        {
            TimeSpan wait = launch + at - DateTime.Now;
            try
            {
                if (wait > TimeSpan.Zero)
                {
                    await Task.Delay(wait, stop);
                }
            }
            catch (OperationCanceledException)
            {
                return evidence.ToString();
            }

            evidence.Append(CultureInfo.InvariantCulture, $"[@{at.TotalMilliseconds:F0}ms ");
            foreach (Process candidate in Process.GetProcessesByName("python"))
            {
                using (candidate)
                {
                    try
                    {
                        if (candidate.StartTime < launch.AddSeconds(-1))
                        {
                            continue;
                        }

                        evidence.Append(CultureInfo.InvariantCulture,
                            $"start={candidate.StartTime:O} "
                                + $"path={candidate.MainModule?.FileName} ");
                    }
                    catch (Exception exception) when (exception is InvalidOperationException
                        or System.ComponentModel.Win32Exception or NotSupportedException)
                    {
                        evidence.Append(CultureInfo.InvariantCulture,
                            $"candidateFailed={exception.Message} ");
                    }

                    evidence.Append(Snapshot(candidate)).Append(' ');
                }
            }

            evidence.Append(']');
        }

        return evidence.ToString();
    }

    private static async Task RecordOriginalLaunchAsync(
        DateTime launch,
        TimeSpan wall,
        ProcessResult result,
        IEnumerable<SystemProcessRunner.ProcessMilestone> milestones,
        string watchdogEvidence
    )
    {
        var text = new StringBuilder();
        text.Append(CultureInfo.InvariantCulture,
            $"AZSTALL_ORIGINAL launchLocal={launch:O} wallMs={wall.TotalMilliseconds:F0} ");
        text.Append(CultureInfo.InvariantCulture,
            $"status={result.Status} exit={result.ExitCode} ");
        text.Append(CultureInfo.InvariantCulture,
            $"resolved={ResolveCommandForDiagnostics("python") ?? "<unresolved>"} milestones=");
        foreach (SystemProcessRunner.ProcessMilestone milestone in milestones)
        {
            text.Append(CultureInfo.InvariantCulture,
                $"{milestone.Name}@{milestone.Elapsed.TotalMilliseconds:F0}ms, ");
        }

        text.AppendLine();
        text.AppendLine(CultureInfo.InvariantCulture, $"watchdog={watchdogEvidence}");
        text.AppendLine(CultureInfo.InvariantCulture, $"stdout={result.StandardOutput}");
        text.AppendLine(CultureInfo.InvariantCulture, $"stderr={result.StandardError}");
        string outputDirectory =
            Environment.GetEnvironmentVariable("TESTINGPLATFORM_DIAGNOSTIC_OUTPUT_DIRECTORY")
            ?? AppContext.BaseDirectory;
        Directory.CreateDirectory(outputDirectory);
        await File.WriteAllTextAsync(
            Path.Combine(outputDirectory, "azstall-original.txt"),
            text.ToString(),
            CancellationToken.None
        );
    }

    private sealed record LaunchRecord(
        string StartUtc,
        double WallMilliseconds,
        double? PreScriptMilliseconds,
        int? ExitCode,
        bool TimedOut,
        string? Executable,
        string SlowImports,
        string Snapshots,
        string StandardOutput,
        string StandardError
    );
}
