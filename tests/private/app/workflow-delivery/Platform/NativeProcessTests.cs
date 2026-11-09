using System.Diagnostics;
using System.Runtime.ExceptionServices;
using System.Text;
using Microsoft.VisualStudio.TestTools.UnitTesting;
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
        Dictionary<string, string?> inherited = TestProcess.Environment();
        inherited[marker] = "set";
        inherited[removed] = null;
        Dictionary<string, string?> isolated = TestProcess.Environment();
        isolated[marker] = "kept";
        // Only what the apphost needs to locate the runtime crosses the cleared boundary.
        foreach (string name in (string[])["DOTNET_ROOT", "SystemRoot"])
            if (Environment.GetEnvironmentVariable(name) is { } value) isolated[name] = value;
        string[] arguments = TestProcess.Arguments("variables", marker, removed, "PATH");

        NativeCommandResult applied = await NativeProcess.ExecuteAsync(new(
            TestProcess.Executable, Path.GetTempPath(), arguments, 30, inherited),
            TestContext.CancellationToken);
        NativeCommandResult cleared = await NativeProcess.ExecuteAsync(new(
            TestProcess.Executable, Path.GetTempPath(), arguments, 30, isolated,
            InheritEnvironment: false), TestContext.CancellationToken);

        Assert.IsTrue(applied.Succeeded, applied.Stderr);
        Assert.AreEqual("set||" + Environment.GetEnvironmentVariable("PATH"), applied.Stdout);
        Assert.IsTrue(cleared.Succeeded, cleared.Stderr);
        Assert.AreEqual("kept||", cleared.Stdout);
    }

    [TestMethod]
    [DataRow("short")]
    [DataRow("long")]
    [DataRow("empty")]
    public async Task RunFailureIncludesOperationExitAndBoundedNativeOutput(string output)
    {
        string prefix = output == "long" ? "omitted-head" + new string('x', 5000) : "";
        string stdout = output == "empty" ? "" : prefix + "stdout-cause";
        string stderr = output == "empty" ? "" : prefix + "stderr-cause";
        InvalidDataException error = await Assert.ThrowsExactlyAsync<InvalidDataException>(
            () => NativeProcess.RunAsync(TestProcess.Executable, Path.GetTempPath(),
                TestProcess.Arguments("exit", "7", stdout, stderr),
                TestContext.CancellationToken, Guard()));

        Assert.Contains(Path.GetFileNameWithoutExtension(TestProcess.Executable) + " exit",
            error.Message);
        Assert.Contains("Exited, exit 7.", error.Message);
        if (output == "empty")
        {
            Assert.DoesNotContain("stdout:", error.Message);
            Assert.DoesNotContain("stderr:", error.Message);
        }
        else
        {
            Assert.Contains("stdout-cause", error.Message);
            Assert.Contains("stderr-cause", error.Message);
        }
        if (output == "long")
        {
            Assert.Contains("[truncated]", error.Message);
            Assert.DoesNotContain("omitted-head", error.Message);
            Assert.IsLessThan(2 * NativeDiagnostic.TailLength + 512, error.Message.Length);
        }
    }

    [TestMethod]
    public async Task RunPreservesLeadingUtf8BomAsNativeData()
    {
        const string expected = "\uFEFF雪\0";

        string output = await RunRawOutputAsync(TestProcess.Base64(expected),
            TestContext.CancellationToken);

        Assert.AreEqual(expected, output);
    }

    // One invalid sequence and one BOM prove strict UTF-8 without BOM detection; the other
    // BOM forms exercise only StreamReader's documented detection.
    [TestMethod]
    [DataRow("wyg=")]
    [DataRow("//5BAA==")]
    public async Task RunRejectsInvalidUtf8WithoutSwitchingEncoding(string bytes)
    {
        await Assert.ThrowsExactlyAsync<DecoderFallbackException>(
            () => RunRawOutputAsync(bytes, TestContext.CancellationToken));
    }

    private static Task<string> RunRawOutputAsync(string payload, CancellationToken token) =>
        NativeProcess.RunAsync(TestProcess.Executable, Path.GetTempPath(),
            TestProcess.Arguments("bytes", payload, ""), token, Guard());

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

    [TestMethod]
    public async Task RunOutputLimitStopsQueryBeforeItsDeadline()
    {
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(
            TestContext.CancellationToken);
        cancellation.CancelAfter(TimeSpan.FromSeconds(15));

        InvalidDataException error = await Assert.ThrowsExactlyAsync<InvalidDataException>(
            () => NativeProcess.RunAsync(TestProcess.Executable, Path.GetTempPath(),
                TestProcess.Arguments("flood", "stdout"), cancellation.Token, Guard()));

        Assert.Contains("characters", error.Message);
    }
}
