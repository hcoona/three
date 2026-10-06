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
        const string expected = "\uFEFF雪\0";

        string output = await RunRawOutputAsync(
            Convert.ToBase64String(Encoding.UTF8.GetBytes(expected)),
            TestContext.CancellationToken);

        Assert.AreEqual(expected, output);
    }

    [TestMethod]
    [DataRow("wyg=")]
    [DataRow("//5BAA==")]
    [DataRow("/v8AQQ==")]
    [DataRow("//4AAA==")]
    public async Task RunRejectsInvalidUtf8WithoutSwitchingEncoding(string bytes)
    {
        await Assert.ThrowsExactlyAsync<DecoderFallbackException>(
            () => RunRawOutputAsync(bytes, TestContext.CancellationToken));
    }

    private static Task<string> RunRawOutputAsync(string payload, CancellationToken token)
    {
        string script = "$bytes = [Convert]::FromBase64String('" + payload + "'); " +
            "[Console]::OpenStandardOutput().Write($bytes, 0, $bytes.Length)";
        string encoded = Convert.ToBase64String(Encoding.Unicode.GetBytes(script));
        return NativeProcess.RunAsync("pwsh", Path.GetTempPath(),
            ["-NoLogo", "-NoProfile", "-NonInteractive", "-EncodedCommand", encoded], token);
    }

    [TestMethod]
    public async Task RunCancellationTerminatesOwnedProcess()
    {
        string pidPath = Path.Combine(Path.GetTempPath(),
            "workflow native process " + Guid.NewGuid().ToString("N") + ".pid");
        string encodedPath = Convert.ToBase64String(Encoding.UTF8.GetBytes(pidPath));
        string script = "$path = [Text.Encoding]::UTF8.GetString(" +
            "[Convert]::FromBase64String('" + encodedPath + "')); " +
            "$staging = $path + '.tmp'; " +
            "[System.IO.File]::WriteAllText($staging, [string]$PID); " +
            "[System.IO.File]::Move($staging, $path); Start-Sleep -Seconds 60";
        string encoded = Convert.ToBase64String(Encoding.Unicode.GetBytes(script));
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(
            TestContext.CancellationToken);
        cancellation.CancelAfter(TimeSpan.FromSeconds(15));
        Task<string> query = NativeProcess.RunAsync("pwsh", Path.GetTempPath(),
            ["-NoLogo", "-NoProfile", "-NonInteractive", "-EncodedCommand", encoded],
            cancellation.Token);
        Exception? failure = null;
        try
        {
            while (!File.Exists(pidPath) && !query.IsCompleted)
                await Task.Delay(50, cancellation.Token);
            Assert.IsTrue(File.Exists(pidPath),
                "The child process must start before cancellation.");
            int pid = int.Parse(await File.ReadAllTextAsync(pidPath, cancellation.Token),
                System.Globalization.CultureInfo.InvariantCulture);
            using Process child = Process.GetProcessById(pid);

            await cancellation.CancelAsync();
            await Assert.ThrowsAsync<OperationCanceledException>(() => query);

            Assert.IsTrue(child.HasExited, "The owned root must exit before RunAsync returns.");
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
        string script =
            "[Console]::Out.Write(('x' * (32 * 1024 * 1024 + 1))); Start-Sleep -Seconds 60";
        string encoded = Convert.ToBase64String(Encoding.Unicode.GetBytes(script));
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(
            TestContext.CancellationToken);
        cancellation.CancelAfter(TimeSpan.FromSeconds(15));

        InvalidDataException error = await Assert.ThrowsExactlyAsync<InvalidDataException>(
            () => NativeProcess.RunAsync("pwsh", Path.GetTempPath(),
                ["-NoLogo", "-NoProfile", "-NonInteractive", "-EncodedCommand", encoded],
                cancellation.Token));

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
