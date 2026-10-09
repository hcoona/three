using System.Diagnostics;
using System.Text;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using WorkflowDelivery.Platform;

namespace WorkflowDelivery.Tests.Platform;

[TestClass]
[DoNotParallelize]
public sealed class NativeCommandTests(TestContext context)
{
    [TestMethod]
    public async Task ExecutePreservesLiteralArgumentsAndWorkingDirectory()
    {
        using var fixture = new CommandFixture(
            "[Console]::Out.Write((Get-Location).Path + '|' + $args[0])");
        const string argument = "spaces 'quotes' $variable ; & literal";

        NativeCommandResult result = await fixture.RunAsync(context.CancellationToken, argument);

        Assert.IsTrue(result.Succeeded);
        Assert.AreEqual(NativeTermination.Exited, result.Termination);
        Assert.AreEqual(0, result.ExitCode);
        Assert.AreEqual(fixture.Directory + "|" + argument, result.Stdout);
        Assert.AreEqual("", result.Stderr);
        Assert.IsNull(result.Error);
        Assert.IsTrue(result.DurationSeconds > 0);
    }

    [TestMethod]
    public async Task ExecutePreservesFailureDiagnostics()
    {
        using var fixture = new CommandFixture(
            "[Console]::Out.Write('partial stdout'); " +
                "[Console]::Error.Write('visible failure'); exit 7");

        NativeCommandResult result = await fixture.RunAsync(context.CancellationToken);
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
        using var fixture = new CommandFixture("[IO.File]::WriteAllText($args[0], 'started')");
        string marker = Path.Combine(fixture.Directory, "marker.txt");
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(
            context.CancellationToken);
        await cancellation.CancelAsync();

        NativeCommandResult result = await fixture.RunAsync(cancellation.Token, marker);

        Assert.AreEqual(NativeTermination.Cancelled, result.Termination);
        Assert.IsNull(result.ExitCode);
        Assert.IsFalse(File.Exists(marker));
        Assert.IsFalse(result.Succeeded);
    }

    [TestMethod]
    public async Task ExecuteCancellationTerminatesOwnedRoot()
    {
        using var fixture = new CommandFixture(
            "$temporary = $args[0] + '.tmp'; [IO.File]::WriteAllText($temporary, [string]$PID); " +
                "[IO.File]::Move($temporary, $args[0]); Start-Sleep -Seconds 60");
        string pidPath = Path.Combine(fixture.Directory, "pid.txt");
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(
            context.CancellationToken);
        cancellation.CancelAfter(TimeSpan.FromSeconds(15));
        Task<NativeCommandResult> execution = fixture.RunAsync(cancellation.Token, pidPath);
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
        using var fixture = new CommandFixture(
            "$temporary = $args[0] + '.tmp'; [IO.File]::WriteAllText($temporary, [string]$PID); " +
                "[IO.File]::Move($temporary, $args[0]); Start-Sleep -Seconds 60");
        fixture.DeadlineSeconds = 3;
        string pidPath = Path.Combine(fixture.Directory, "pid.txt");
        Task<NativeCommandResult> execution = fixture.RunAsync(context.CancellationToken, pidPath);
        using Process root = await WaitForRootAsync(execution, pidPath, context.CancellationToken);

        NativeCommandResult result = await execution;

        Assert.AreEqual(NativeTermination.TimedOut, result.Termination);
        Assert.Contains("3-second deadline", result.Error!);
        Assert.IsTrue(root.HasExited, "Timeout cleanup must join the owned root.");
        Assert.IsFalse(result.Succeeded);
    }

    [TestMethod]
    [DataRow("stdout", "utf8")]
    [DataRow("stderr", "utf8")]
    [DataRow("stdout", "limit")]
    [DataRow("stderr", "limit")]
    public async Task ExecuteRejectsInvalidUtf8OrBoundedOutput(string stream, string defect)
    {
        string target = stream == "stdout" ? "Out" : "Error";
        string raw = stream == "stdout" ? "OpenStandardOutput" : "OpenStandardError";
        string script = defect == "utf8"
            ? "$bytes = [Convert]::FromBase64String('wyg='); " +
                "[Console]::" + raw + "().Write($bytes, 0, $bytes.Length); Start-Sleep -Seconds 60"
            : "[Console]::" + target + ".Write(('x' * (32 * 1024 * 1024 + 1))); " +
                "Start-Sleep -Seconds 60";
        using var fixture = new CommandFixture(script);
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(
            context.CancellationToken);
        cancellation.CancelAfter(TimeSpan.FromSeconds(15));

        NativeCommandResult result = await fixture.RunAsync(cancellation.Token);

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
        string bytes = Convert.ToBase64String(Encoding.UTF8.GetBytes(expected));
        using var fixture = new CommandFixture("$bytes = [Convert]::FromBase64String('" +
            bytes + "'); [Console]::OpenStandardOutput().Write($bytes, 0, $bytes.Length); " +
            "[Console]::OpenStandardError().Write($bytes, 0, $bytes.Length)");

        NativeCommandResult result = await fixture.RunAsync(context.CancellationToken);

        Assert.IsTrue(result.Succeeded);
        Assert.AreEqual(expected, result.Stdout);
        Assert.AreEqual(expected, result.Stderr);
    }

    [TestMethod]
    [DataRow(0)]
    [DataRow(-1)]
    public async Task ExecuteRejectsNonpositiveDeadlineBeforeLaunch(int seconds)
    {
        using var fixture = new CommandFixture("throw 'must not start'");
        fixture.DeadlineSeconds = seconds;

        await Assert.ThrowsExactlyAsync<ArgumentException>(() =>
            fixture.RunAsync(context.CancellationToken));
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
        private readonly string script;

        internal CommandFixture(string content)
        {
            script = Path.Combine(Directory, "command.ps1");
            File.WriteAllText(script, content);
        }

        internal Task<NativeCommandResult> RunAsync(CancellationToken token,
            params string[] arguments) => NativeProcess.ExecuteAsync(new("pwsh", Directory,
                ["-NoLogo", "-NoProfile", "-NonInteractive", "-File", script, .. arguments],
                DeadlineSeconds), token);

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
