using System.Diagnostics;
using System.Runtime.ExceptionServices;
using System.Text;
using Microsoft.Extensions.Time.Testing;
using WorkflowDelivery.Platform;

namespace WorkflowDelivery.Tests.Platform;

[TestClass]
public sealed class NativeProcessTests
{
    public TestContext TestContext { get; set; } = null!;

    [TestMethod]
    public async Task ExecuteClosesStandardInputBeforeAwaitingNativeExit()
    {
        NativeCommandResult result = await NativeProcess.ExecuteAsync(new(
            TestProcess.Executable, Path.GetTempPath(), TestProcess.Arguments("stdin-length"), 30,
            TestProcess.Environment(), CloseStandardInput: true), TestContext.CancellationToken);

        Assert.AreEqual(NativeTermination.Exited, result.Termination);
        Assert.AreEqual(0, result.ExitCode);
        Assert.AreEqual("0", result.Stdout);
        Assert.IsEmpty(result.Stderr);
        Assert.IsNull(result.Error);
    }

    // Null values remove a variable and InheritEnvironment=false isolates the child from
    // ambient variables, such as receiver-only secrets.
    [TestMethod]
    public async Task ExecuteAppliesCommandEnvironmentSemantics()
    {
        string marker = "WORKFLOW_PROCESS_PROBE_" + Guid.NewGuid().ToString("N");
        string removed = OperatingSystem.IsWindows() ? "USERPROFILE" : "HOME";
        Assert.IsNotNull(Environment.GetEnvironmentVariable(removed));
        Assert.IsNotNull(Environment.GetEnvironmentVariable("PATH"));
        Dictionary<string, string?> removal = TestProcess.Environment();
        removal[removed] = null;
        Dictionary<string, string?> isolated = TestProcess.Environment();
        isolated[marker] = "kept";
        // Only what the apphost needs to locate the runtime crosses the cleared boundary.
        foreach (string name in (string[])["DOTNET_ROOT", "SystemRoot"])
            if (Environment.GetEnvironmentVariable(name) is { } value) isolated[name] = value;
        NativeCommandResult applied = await NativeProcess.ExecuteAsync(new(
            TestProcess.Executable, Path.GetTempPath(), TestProcess.Arguments("variables", removed),
            30, removal),
            TestContext.CancellationToken);
        NativeCommandResult cleared = await NativeProcess.ExecuteAsync(new(
            TestProcess.Executable, Path.GetTempPath(),
            TestProcess.Arguments("variables", marker, removed, "PATH"), 30, isolated,
            InheritEnvironment: false), TestContext.CancellationToken);

        Assert.IsTrue(applied.Succeeded, applied.Stderr);
        Assert.IsEmpty(applied.Stdout);
        Assert.IsTrue(cleared.Succeeded, cleared.Stderr);
        Assert.AreEqual("kept||", cleared.Stdout);
    }

    [TestMethod]
    public async Task RunTranslatesFailedExitWithBoundedNativeDiagnostics()
    {
        string prefix = "omitted-head" + new string('x', 5000);
        InvalidDataException error = await Assert.ThrowsExactlyAsync<InvalidDataException>(
            () => NativeProcess.RunAsync(TestProcess.Executable, Path.GetTempPath(),
                TestProcess.Arguments("exit", "7", prefix + "stdout-cause",
                    prefix + "stderr-cause"),
                TestContext.CancellationToken, Guard()));

        Assert.Contains(Path.GetFileNameWithoutExtension(TestProcess.Executable) + " exit",
            error.Message);
        Assert.Contains("Exited, exit 7.", error.Message);
        Assert.Contains("stdout-cause", error.Message);
        Assert.Contains("stderr-cause", error.Message);
        Assert.Contains("[truncated]", error.Message);
        Assert.DoesNotContain("omitted-head", error.Message);
        Assert.IsLessThan(2 * NativeDiagnostic.TailLength + 512, error.Message.Length);
    }

    [TestMethod]
    public async Task RunTranslatesInvalidUtf8ToDecoderFailure()
    {
        await Assert.ThrowsExactlyAsync<DecoderFallbackException>(() =>
            NativeProcess.RunAsync(TestProcess.Executable, Path.GetTempPath(),
                TestProcess.Arguments("bytes", "wyg=", ""), TestContext.CancellationToken,
                Guard()));
    }

    [TestMethod]
    public async Task RunPreservesLeadingUtf8BomAsNativeData()
    {
        const string expected = "\uFEFFsnow";
        string output = await NativeProcess.RunAsync(TestProcess.Executable, Path.GetTempPath(),
            TestProcess.Arguments("bytes", TestProcess.Base64(expected), ""),
            TestContext.CancellationToken, Guard());

        Assert.AreEqual(expected, output);
    }

    [TestMethod]
    public async Task RunRejectsStdoutBeyondQueryOutputLimit()
    {
        InvalidDataException error = await Assert.ThrowsExactlyAsync<InvalidDataException>(() =>
            NativeProcess.RunAsync(TestProcess.Executable, Path.GetTempPath(),
                TestProcess.Arguments("flood", "stdout"), TestContext.CancellationToken, Guard()));

        Assert.AreEqual("Native query output exceeded 33,554,432 characters.", error.Message);
    }

    [TestMethod]
    [Timeout(15000, CooperativeCancellation = true)]
    public async Task RunDeadlineRetainsOperationAndTerminalObservation()
    {
        var clock = new FakeTimeProvider();
        Task<string> query = NativeProcess.RunAsync(TestProcess.Executable, Path.GetTempPath(),
            TestProcess.Arguments("sleep"), TestContext.CancellationToken, Guard(), clock);

        clock.Advance(TimeSpan.FromSeconds(30));
        TimeoutException error = await Assert.ThrowsExactlyAsync<TimeoutException>(() => query);

        Assert.Contains(Path.GetFileNameWithoutExtension(TestProcess.Executable) + " sleep",
            error.Message);
        Assert.Contains("TimedOut, exit ", error.Message);
        Assert.Contains("30-second deadline", error.Message);
        Assert.IsInstanceOfType<OperationCanceledException>(error.InnerException);
    }

    private static Dictionary<string, string> Guard() =>
        new() { [TestProcess.GuardVariable] = "1" };

    [TestMethod]
    public async Task RunCancellationTerminatesOwnedProcess()
    {
        string pidPath = Path.Combine(Path.GetTempPath(),
            "workflow native process " + Guid.NewGuid().ToString("N") + ".pid");
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(
            TestContext.CancellationToken);
        cancellation.CancelAfter(TimeSpan.FromSeconds(15));
        Task<string> query = NativeProcess.RunAsync(TestProcess.Executable, Path.GetTempPath(),
            TestProcess.Arguments("pid", pidPath, "descendant"), cancellation.Token, Guard());
        Exception? failure = null;
        Process? descendant = null;
        try
        {
            string? pidText = null;
            while (pidText is null && !query.IsCompleted)
            {
                try
                {
                    pidText = await File.ReadAllTextAsync(pidPath, cancellation.Token);
                }
                catch (FileNotFoundException) { }
                catch (IOException exception) when (OperatingSystem.IsWindows() &&
                    (exception.HResult & 0xffff) is 32 or 33)
                { }
                if (pidText is null) await Task.Delay(50, cancellation.Token);
            }
            Assert.IsNotNull(pidText,
                "The child process must start before cancellation.");
            string[] pids = pidText.Split(' ');
            using Process child = Process.GetProcessById(int.Parse(pids[0],
                System.Globalization.CultureInfo.InvariantCulture));
            // Open the descendant handle before interruption so its exit stays observable.
            descendant = Process.GetProcessById(int.Parse(pids[1],
                System.Globalization.CultureInfo.InvariantCulture));

            await cancellation.CancelAsync();
            await Assert.ThrowsAsync<OperationCanceledException>(() => query);

            Assert.IsTrue(child.HasExited, "The owned root must exit before RunAsync returns.");
            // Tree kill is asynchronous and RunAsync waits only for the root.
            using var descendantExit = new CancellationTokenSource(TimeSpan.FromSeconds(5));
            try
            {
                await descendant.WaitForExitAsync(descendantExit.Token);
            }
            catch (OperationCanceledException) when (descendantExit.IsCancellationRequested)
            {
                Assert.Fail("The owned descendant must exit after cancellation.");
            }
        }
        catch (Exception exception)
        {
            failure = exception;
        }
        finally
        {
            try
            {
                await cancellation.CancelAsync();
            }
            catch (Exception exception)
            {
                failure = failure is null ? exception : new AggregateException(failure, exception);
            }
            try
            {
                await query;
            }
            catch (OperationCanceledException) when (cancellation.IsCancellationRequested) { }
            catch (Exception exception)
            {
                failure = failure is null ? exception : new AggregateException(failure, exception);
            }
            if (descendant is not null)
            {
                try
                {
                    if (!descendant.HasExited) descendant.Kill();
                }
                catch (Exception exception)
                {
                    failure = failure is null ? exception
                        : new AggregateException(failure, exception);
                }
                descendant.Dispose();
            }
            foreach (string path in new[] { pidPath, pidPath + ".tmp" })
            {
                try
                {
                    File.Delete(path);
                }
                catch (Exception exception)
                {
                    failure = failure is null ? exception
                        : new AggregateException(failure, exception);
                }
            }
        }
        if (failure is not null) ExceptionDispatchInfo.Capture(failure).Throw();
    }
}
