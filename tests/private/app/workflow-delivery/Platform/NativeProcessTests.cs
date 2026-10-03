using System.Diagnostics;
using System.Text;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using WorkflowDelivery.Platform;

namespace WorkflowDelivery.Tests.Platform;

[TestClass]
public sealed class NativeProcessTests
{
    public TestContext TestContext { get; set; } = null!;

    [TestMethod]
    public async Task RunPreservesLiteralArgumentsAndWorkingDirectory()
    {
        using var fixture = new ProcessFixture(
            "[Console]::Out.Write((Get-Location).Path + '|' + $args[0])");
        const string argument = "spaces 'quotes' $variable ; & literal";

        string output = await fixture.RunAsync(TestContext.CancellationToken, argument);

        Assert.AreEqual(fixture.Directory + "|" + argument, output);
    }

    [TestMethod]
    public async Task RunRejectsFailureWithoutExposingRawDiagnostics()
    {
        using var fixture = new ProcessFixture(
            "[Console]::Error.Write('private diagnostic'); exit 7");

        InvalidDataException error = await Assert.ThrowsExactlyAsync<InvalidDataException>(
            () => fixture.RunAsync(TestContext.CancellationToken));

        Assert.Contains("exit code 7", error.Message);
        Assert.DoesNotContain("private diagnostic", error.Message);
    }

    [TestMethod]
    public async Task RunPreservesLeadingUtf8BomAsNativeData()
    {
        using var fixture = RawOutputFixture();
        const string expected = "\uFEFF雪\0";

        string output = await fixture.RunAsync(TestContext.CancellationToken,
            Convert.ToBase64String(Encoding.UTF8.GetBytes(expected)));

        Assert.AreEqual(expected, output);
    }

    [TestMethod]
    [DataRow("wyg=")]
    [DataRow("//5BAA==")]
    [DataRow("/v8AQQ==")]
    [DataRow("//4AAA==")]
    public async Task RunRejectsInvalidUtf8WithoutSwitchingEncoding(string bytes)
    {
        using var fixture = RawOutputFixture();

        await Assert.ThrowsExactlyAsync<DecoderFallbackException>(
            () => fixture.RunAsync(TestContext.CancellationToken, bytes));
    }

    private static ProcessFixture RawOutputFixture() => new(
        "$bytes = [Convert]::FromBase64String($args[0]); " +
        "[Console]::OpenStandardOutput().Write($bytes, 0, $bytes.Length)");

    [TestMethod]
    public async Task RunCancellationTerminatesOwnedProcess()
    {
        using var fixture = new ProcessFixture(
            "[System.IO.File]::WriteAllText($args[0], [string]$PID); Start-Sleep -Seconds 60");
        string pidPath = Path.Combine(fixture.Directory, "pid.txt");
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(
            TestContext.CancellationToken);
        cancellation.CancelAfter(TimeSpan.FromSeconds(15));
        Task<string> query = fixture.RunAsync(cancellation.Token, pidPath);
        while (!File.Exists(pidPath) && !query.IsCompleted)
            await Task.Delay(50, cancellation.Token);
        Assert.IsTrue(File.Exists(pidPath), "The child process must start before cancellation.");
        int pid = int.Parse(await File.ReadAllTextAsync(pidPath, cancellation.Token),
            System.Globalization.CultureInfo.InvariantCulture);
        using Process child = Process.GetProcessById(pid);

        await cancellation.CancelAsync();
        await Assert.ThrowsAsync<OperationCanceledException>(() => query);

        Assert.IsTrue(child.HasExited, "The owned root must exit before RunAsync returns.");
    }

    [TestMethod]
    public async Task RunOutputLimitStopsQueryBeforeItsDeadline()
    {
        using var fixture = new ProcessFixture(
            "[Console]::Out.Write(('x' * (32 * 1024 * 1024 + 1))); Start-Sleep -Seconds 60");
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(
            TestContext.CancellationToken);
        cancellation.CancelAfter(TimeSpan.FromSeconds(15));

        InvalidDataException error = await Assert.ThrowsExactlyAsync<InvalidDataException>(
            () => fixture.RunAsync(cancellation.Token));

        Assert.Contains("characters", error.Message);
    }

    private sealed class ProcessFixture : IDisposable
    {
        internal string Directory { get; } = Path.Combine(Path.GetTempPath(),
            "workflow native process " + Guid.NewGuid().ToString("N"));
        private readonly string script;

        internal ProcessFixture(string content)
        {
            System.IO.Directory.CreateDirectory(Directory);
            script = Path.Combine(Directory, "query.ps1");
            File.WriteAllText(script, content);
        }

        internal Task<string> RunAsync(CancellationToken token, params string[] arguments) =>
            NativeProcess.RunAsync("pwsh", Directory,
                ["-NoLogo", "-NoProfile", "-NonInteractive", "-File", script, .. arguments], token);

        public void Dispose() => System.IO.Directory.Delete(Directory, recursive: true);
    }
}
