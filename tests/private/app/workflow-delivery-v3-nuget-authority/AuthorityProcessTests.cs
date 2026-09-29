using System.Diagnostics;
using System.Globalization;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace WorkflowDeliveryV3NuGetAuthority.Tests;

[TestClass]
public sealed class AuthorityProcessTests(TestContext testContext)
{
    private readonly string directory = Path.Combine(
        Path.GetTempPath(), $"authority-process-tests-{Guid.NewGuid():N}");
    private readonly CancellationTokenSource cancellation =
        CancellationTokenSource.CreateLinkedTokenSource(testContext.CancellationToken);
    private Task activeRun = Task.CompletedTask;

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task DeadlineRetainsPartialOutputAndCleansUpFailedCollector(bool hungCollector)
    {
        using var deadline = new CancellationTokenSource();
        using var collectionDeadline = new CancellationTokenSource();
        AuthorityProcessOptions options = CreateOptions() with
        {
            DumpTool = hungCollector ? ProbeExecutable : Path.Combine(directory, "missing-tool"),
            ExecutionDeadlineToken = deadline.Token,
            CollectionDeadlineToken = collectionDeadline.Token,
        };
        Task run = StartRun(options);
        await WaitForPidAsync("helper.pid");
        await deadline.CancelAsync();
        if (hungCollector)
        {
            await WaitForPidAsync("collector.pid");
            await collectionDeadline.CancelAsync();
        }

        AssertFailedException failure = await Assert.ThrowsExactlyAsync<AssertFailedException>(
            () => run.WaitAsync(TimeSpan.FromSeconds(20), testContext.CancellationToken));

        Assert.IsInstanceOfType<OperationCanceledException>(failure.InnerException);
        string log = AssertRetainedOutputAndExitedHelper();
        Assert.Contains("Diagnostic collection failed", log);
        if (hungCollector)
        {
            AssertProcessExited("collector.pid");
            Assert.Contains("collector-started", ReadEvidence("dump-stdout.txt"));
        }
    }

    [TestMethod]
    public async Task CancellationDuringCollectionStopsCollectorBeforeItsDeadline()
    {
        using var deadline = new CancellationTokenSource();
        AuthorityProcessOptions options = CreateOptions() with
        {
            ExecutionDeadlineToken = deadline.Token,
        };
        Task run = StartRun(options);
        try
        {
            await WaitForPidAsync("helper.pid");
            await deadline.CancelAsync();
            await WaitForPidAsync("collector.pid");
            await cancellation.CancelAsync();
            // This bound is shorter than the collector deadline: ignoring cancellation fails.
            await Assert.ThrowsExactlyAsync<AssertFailedException>(
                () => run.WaitAsync(TimeSpan.FromSeconds(20), testContext.CancellationToken));
            AssertRetainedOutputAndExitedHelper();
            AssertProcessExited("collector.pid");
        }
        finally
        {
            await cancellation.CancelAsync();
        }
    }

    [TestMethod]
    public async Task CancellationBeforeCollectionSkipsDumpAndCleansUpHelper()
    {
        Task run = StartRun(CreateOptions());
        try
        {
            await WaitForPidAsync("helper.pid");
            await cancellation.CancelAsync();
            await Assert.ThrowsExactlyAsync<AssertFailedException>(
                () => run.WaitAsync(TimeSpan.FromSeconds(20), testContext.CancellationToken));
            string log = AssertRetainedOutputAndExitedHelper();
            Assert.DoesNotContain("Dump requested", log);
            Assert.IsFalse(File.Exists(Path.Combine(directory, "collector.pid")));
        }
        finally
        {
            await cancellation.CancelAsync();
        }
    }

    [TestCleanup]
    public async Task CleanupAsync()
    {
        await cancellation.CancelAsync();
        // Also reap probes when an assertion fails; successful tests assert exit before this.
        foreach (string name in new[] { "helper.pid", "collector.pid" })
        {
            string path = Path.Combine(directory, name);
            if (!File.Exists(path))
            {
                continue;
            }

            try
            {
                using Process process = Process.GetProcessById(
                    int.Parse(await File.ReadAllTextAsync(path), CultureInfo.InvariantCulture));
                if (!process.HasExited)
                {
                    try
                    {
                        process.Kill(entireProcessTree: true);
                    }
                    catch (InvalidOperationException) when (process.HasExited)
                    {
                        // The probe exited between the check and termination.
                    }

                    await process.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(10));
                }
            }
            catch (ArgumentException)
            {
                // The probe has already exited.
            }
        }

        try
        {
            await activeRun.WaitAsync(TimeSpan.FromSeconds(20));
        }
        catch (AssertFailedException)
        {
            // The expected process failure is asserted by the scenario itself.
        }
        finally
        {
            cancellation.Dispose();
            if (Directory.Exists(directory))
            {
                Directory.Delete(directory, recursive: true);
            }
        }
    }

    private static string ProbeExecutable => Path.Combine(
        AppContext.BaseDirectory,
        "WorkflowDeliveryV3NuGetAuthority.Tests" + (OperatingSystem.IsWindows() ? ".exe" : ""));

    private AuthorityProcessOptions CreateOptions()
    {
        Directory.CreateDirectory(directory);
        return new AuthorityProcessOptions
        {
            DiagnosticsDirectory = directory,
            ProcessTimeout = TimeSpan.FromMinutes(2),
            DumpTimeout = TimeSpan.FromMinutes(2),
            DumpTool = ProbeExecutable,
        };
    }

    private Task StartRun(AuthorityProcessOptions options)
    {
        activeRun = AuthorityProcess.RunAsync(
            CreateProbe(), "request", "probe", testContext, options, cancellation.Token);
        return activeRun;
    }

    private ProcessStartInfo CreateProbe()
    {
        var start = new ProcessStartInfo(ProbeExecutable)
        {
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };
        start.ArgumentList.Add("--authority-process-probe");
        start.ArgumentList.Add(Path.Combine(directory, "helper.pid"));
        return start;
    }

    private async Task WaitForPidAsync(string name)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(
            testContext.CancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(90));
        string path = Path.Combine(directory, name);
        while (!File.Exists(path) || new FileInfo(path).Length == 0)
        {
            await Task.Delay(25, timeout.Token);
        }
    }

    private string AssertRetainedOutputAndExitedHelper()
    {
        Assert.Contains("probe-stdout", ReadEvidence("stdout.txt"));
        Assert.Contains("probe-stderr", ReadEvidence("stderr.txt"));
        AssertProcessExited("helper.pid");
        string log = ReadEvidence("process.log");
        Assert.Contains("Cleanup confirmed exit", log);
        return log;
    }

    private string ReadEvidence(string name)
    {
        string evidence = Assert.ContainsSingle(Directory.GetDirectories(directory));
        return File.ReadAllText(Path.Combine(evidence, name));
    }

    private void AssertProcessExited(string name)
    {
        int pid = int.Parse(
            File.ReadAllText(Path.Combine(directory, name)), CultureInfo.InvariantCulture);
        try
        {
            using Process process = Process.GetProcessById(pid);
            Assert.IsTrue(process.HasExited, $"Probe {pid} survived cleanup.");
        }
        catch (ArgumentException)
        {
            // An absent process also proves that cleanup completed.
        }
    }
}
