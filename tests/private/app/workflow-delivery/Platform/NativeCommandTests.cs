using System.Diagnostics;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using WorkflowDelivery.Platform;

namespace WorkflowDelivery.Tests.Platform;

[TestClass]
[DoNotParallelize]
public sealed class NativeCommandTests(TestContext context)
{
    [TestMethod]
    public async Task ExecutePreservesFailureDiagnostics()
    {
        using var fixture = new CommandFixture();

        NativeCommandResult result = await fixture.RunAsync(context.CancellationToken,
            "exit", "7", "partial stdout", "visible failure");
        NativeCommandResult absent = await NativeProcess.ExecuteAsync(new(
            Path.Combine(fixture.Directory, "unavailable-executable"), fixture.Directory, [], 3),
            context.CancellationToken);

        Assert.AreEqual(NativeTermination.Exited, result.Termination);
        Assert.AreEqual(7, result.ExitCode);
        Assert.AreEqual("partial stdout", result.Stdout);
        Assert.AreEqual("visible failure", result.Stderr);
        Assert.IsFalse(result.Succeeded);
        Assert.AreEqual(NativeTermination.StartFailed, absent.Termination);
        Assert.IsNull(absent.ExitCode);
        Assert.IsNotNull(absent.Error);
        Assert.IsFalse(absent.Succeeded);
    }

    [TestMethod]
    public async Task ExecutePrecancelledCommandDoesNotStart()
    {
        using var fixture = new CommandFixture();
        string marker = Path.Combine(fixture.Directory, "marker.txt");
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(
            context.CancellationToken);
        await cancellation.CancelAsync();

        NativeCommandResult result = await fixture.RunAsync(cancellation.Token, "marker",
            marker);

        Assert.AreEqual(NativeTermination.Cancelled, result.Termination);
        Assert.IsNull(result.ExitCode);
        Assert.IsFalse(File.Exists(marker));
        Assert.IsFalse(result.Succeeded);
    }

    [TestMethod]
    public async Task ExecuteCancellationTerminatesOwnedRoot()
    {
        using var fixture = new CommandFixture();
        string pidPath = Path.Combine(fixture.Directory, "pid.txt");
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(
            context.CancellationToken);
        cancellation.CancelAfter(TimeSpan.FromSeconds(15));
        Task<NativeCommandResult> execution = fixture.RunAsync(cancellation.Token, "pid",
            pidPath);
        using Process root = await WaitForRootAsync(execution, pidPath, cancellation.Token);

        await cancellation.CancelAsync();
        NativeCommandResult result = await execution;

        Assert.AreEqual(NativeTermination.Cancelled, result.Termination);
        Assert.IsTrue(root.HasExited, "The owned root must exit before returning.");
        Assert.IsFalse(result.Succeeded);
    }

    [TestMethod]
    public async Task ExecuteDeadlineTerminatesOwnedRoot()
    {
        using var fixture = new CommandFixture();
        fixture.DeadlineSeconds = 3;
        string pidPath = Path.Combine(fixture.Directory, "pid.txt");
        Task<NativeCommandResult> execution = fixture.RunAsync(context.CancellationToken, "pid",
            pidPath);
        using Process root = await WaitForRootAsync(execution, pidPath, context.CancellationToken);

        NativeCommandResult result = await execution;

        Assert.AreEqual(NativeTermination.TimedOut, result.Termination);
        Assert.Contains("3-second deadline", result.Error!);
        Assert.IsTrue(root.HasExited, "Timeout cleanup must join the owned root.");
        Assert.IsFalse(result.Succeeded);
    }

    // Each stream builds its own strict decoder, so both reject invalid UTF-8. The bound is
    // enforced by one shared capture, so a single limit row covers both streams.
    [TestMethod]
    [DataRow("stdout", "utf8")]
    [DataRow("stderr", "utf8")]
    [DataRow("stderr", "limit")]
    public async Task ExecuteRejectsInvalidUtf8OrBoundedOutput(string stream, string defect)
    {
        string[] command = defect == "utf8"
            ? ["bytes", stream == "stdout" ? "wyg=" : "", stream == "stdout" ? "" : "wyg=",
                "sleep"]
            : ["flood", stream];
        using var fixture = new CommandFixture();
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(
            context.CancellationToken);
        cancellation.CancelAfter(TimeSpan.FromSeconds(15));

        NativeCommandResult result = await fixture.RunAsync(cancellation.Token, command);

        Assert.AreEqual(NativeTermination.InvalidOutput, result.Termination);
        Assert.IsFalse(result.Succeeded);
        Assert.IsNotEmpty(result.Error!);
        if (defect == "limit")
        {
            Assert.Contains("characters", result.Error!);
            Assert.AreEqual(32 * 1024 * 1024,
                (stream == "stdout" ? result.Stdout : result.Stderr).Length);
        }
    }

    [TestMethod]
    public async Task ExecutePreservesLeadingUtf8BomAsDataInBothStreams()
    {
        const string expected = "\uFEFF雪\0";
        string bytes = TestProcess.Base64(expected);
        using var fixture = new CommandFixture();

        NativeCommandResult result = await fixture.RunAsync(context.CancellationToken, "bytes",
            bytes, bytes);

        Assert.IsTrue(result.Succeeded);
        Assert.AreEqual(expected, result.Stdout);
        Assert.AreEqual(expected, result.Stderr);
    }

    [TestMethod]
    [DataRow(0)]
    [DataRow(-1)]
    public async Task ExecuteRejectsNonpositiveDeadlineBeforeLaunch(int seconds)
    {
        using var fixture = new CommandFixture();
        fixture.DeadlineSeconds = seconds;

        await Assert.ThrowsExactlyAsync<ArgumentException>(() =>
            fixture.RunAsync(context.CancellationToken, "sleep"));
    }

    private static async Task<Process> WaitForRootAsync(Task<NativeCommandResult> execution,
        string pidPath, CancellationToken token)
    {
        while (!File.Exists(pidPath) && !execution.IsCompleted)
            await Task.Delay(50, token);
        Assert.IsTrue(File.Exists(pidPath), "The inert root must start before termination.");
        int pid = int.Parse(await File.ReadAllTextAsync(pidPath, token),
            System.Globalization.CultureInfo.InvariantCulture);
        return Process.GetProcessById(pid);
    }

    private sealed class CommandFixture : IDisposable
    {
        internal string Directory { get; } = System.IO.Directory.CreateTempSubdirectory(
            "workflow-native-command-").FullName;
        internal int DeadlineSeconds { get; set; } = 30;

        internal Task<NativeCommandResult> RunAsync(CancellationToken token,
            params string[] command) => NativeProcess.ExecuteAsync(new(TestProcess.Executable,
                Directory, TestProcess.Arguments(command), DeadlineSeconds,
                TestProcess.Environment()), token);

        public void Dispose()
        {
            var elapsed = Stopwatch.StartNew();
            while (true)
            {
                try
                {
                    System.IO.Directory.Delete(Directory, recursive: true);
                    return;
                }
                catch (IOException exception) when (OperatingSystem.IsWindows() &&
                    (exception.HResult == unchecked((int)0x80070020) ||
                        exception.HResult == unchecked((int)0x80070021)) &&
                    elapsed.Elapsed < TimeSpan.FromSeconds(5))
                {
                    Thread.Sleep(50);
                }
            }
        }
    }
}
