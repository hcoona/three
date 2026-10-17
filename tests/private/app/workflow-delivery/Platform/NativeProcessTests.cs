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
        const string script = "[Console]::Out.Write([Console]::In.ReadToEnd().Length); exit 0";
        string encoded = Convert.ToBase64String(Encoding.Unicode.GetBytes(script));
        NativeCommandResult result = await NativeProcess.ExecuteAsync(new("pwsh",
            Path.GetTempPath(), ["-NoLogo", "-NoProfile", "-NonInteractive", "-EncodedCommand",
                encoded], 30, CloseStandardInput: true), TestContext.CancellationToken);

        Assert.AreEqual(NativeTermination.Exited, result.Termination);
        Assert.AreEqual(0, result.ExitCode);
        Assert.AreEqual("0", result.Stdout);
        Assert.IsEmpty(result.Stderr);
        Assert.IsNull(result.Error);
    }

    [TestMethod]
    public async Task ExecuteAppliesPerCommandEnvironmentWithoutAmbientMutation()
    {
        string marker = "WORKFLOW_PROCESS_PROBE_" + Guid.NewGuid().ToString("N");
        string removed = OperatingSystem.IsWindows() ? "USERPROFILE" : "HOME";
        string? original = Environment.GetEnvironmentVariable(removed);
        Assert.IsNotNull(original);
        string inheritedName = OperatingSystem.IsWindows() ? "SystemRoot" : "USER";
        string? inherited = Environment.GetEnvironmentVariable(inheritedName);
        Assert.IsNotNull(inherited);
        const string value = "native spaces;percent% equals=tail";
        string script = "[Console]::Out.Write([Environment]::GetEnvironmentVariable('" +
            marker + "') + '|' + [Environment]::GetEnvironmentVariable('" + removed +
            "') + '|' + [Environment]::GetEnvironmentVariable('" + inheritedName + "'))";
        string encoded = Convert.ToBase64String(Encoding.Unicode.GetBytes(script));
        var environment = new Dictionary<string, string?>
        {
            [marker] = value,
            [removed] = null,
        };

        NativeCommandResult result = await NativeProcess.ExecuteAsync(new("pwsh",
            Path.GetTempPath(), ["-NoLogo", "-NoProfile", "-NonInteractive", "-EncodedCommand",
                encoded], 30, environment), TestContext.CancellationToken);

        Assert.IsTrue(result.Succeeded, result.Stderr);
        Assert.AreEqual(value + "||" + inherited, result.Stdout);
        Assert.IsNull(Environment.GetEnvironmentVariable(marker));
        Assert.AreEqual(original, Environment.GetEnvironmentVariable(removed));
        Assert.AreEqual(inherited, Environment.GetEnvironmentVariable(inheritedName));
    }

    [TestMethod]
    public async Task ExecuteWithoutInheritedEnvironmentPassesOnlyCommandEnvironment()
    {
        // The app's own Environment.Clear wrapper line; ambient variables are only read.
        // PATH is always present in the test host and is deliberately not passed below.
        // pwsh itself adds its own directory to PATH, so only the other host entries count.
        const string ambientName = "PATH";
        string[] hostPath = (Environment.GetEnvironmentVariable(ambientName) ?? "")
            .Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries);
        string pwsh = hostPath
            .Select(directory => Path.Combine(directory,
                OperatingSystem.IsWindows() ? "pwsh.exe" : "pwsh"))
            .First(File.Exists);
        string pwshDirectory = Path.GetDirectoryName(pwsh)!;
        string[] ambientEntries = [.. hostPath.Where(directory =>
            !string.Equals(directory.TrimEnd(Path.DirectorySeparatorChar), pwshDirectory,
                StringComparison.OrdinalIgnoreCase))];
        Assert.IsNotEmpty(ambientEntries);
        string marker = "WORKFLOW_PROCESS_PROBE_" + Guid.NewGuid().ToString("N");
        var environment = new Dictionary<string, string?> { [marker] = "kept" };
        foreach (string name in (string[])(OperatingSystem.IsWindows() ?
            ["SystemRoot", "TEMP", "TMP", "USERPROFILE"] : ["HOME", "TMPDIR"]))
            if (Environment.GetEnvironmentVariable(name) is { } value)
                environment[name] = value;
        string script = "[Console]::Out.Write([Environment]::GetEnvironmentVariable('" +
            marker + "') + '|' + [Environment]::GetEnvironmentVariable('" + ambientName +
            "'))";
        string encoded = Convert.ToBase64String(Encoding.Unicode.GetBytes(script));

        NativeCommandResult result = await NativeProcess.ExecuteAsync(new(pwsh,
            Path.GetTempPath(), ["-NoLogo", "-NoProfile", "-NonInteractive", "-EncodedCommand",
                encoded], 30, environment, InheritEnvironment: false),
            TestContext.CancellationToken);

        Assert.IsTrue(result.Succeeded, result.Stderr);
        string[] observed = result.Stdout.Split('|', 2);
        Assert.AreEqual("kept", observed[0]);
        string[] childPath = observed[1].Split(Path.PathSeparator,
            StringSplitOptions.RemoveEmptyEntries);
        foreach (string entry in ambientEntries)
            Assert.DoesNotContain(entry, childPath, "Ambient PATH entry leaked: " + entry);
    }

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
            "$start = [Diagnostics.ProcessStartInfo]::new([Environment]::ProcessPath); " +
            "foreach ($argument in '-NoLogo', '-NoProfile', '-NonInteractive', '-Command', " +
            "'Start-Sleep -Seconds 60') { $start.ArgumentList.Add($argument) }; " +
            "$start.RedirectStandardInput = $true; $start.RedirectStandardOutput = $true; " +
            "$start.RedirectStandardError = $true; " +
            "$descendant = [Diagnostics.Process]::Start($start); " +
            "[System.IO.File]::WriteAllText($staging, [string]$PID + ' ' + $descendant.Id); " +
            "[System.IO.File]::Move($staging, $path); Start-Sleep -Seconds 60";
        string encoded = Convert.ToBase64String(Encoding.Unicode.GetBytes(script));
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(
            TestContext.CancellationToken);
        cancellation.CancelAfter(TimeSpan.FromSeconds(15));
        Task<string> query = NativeProcess.RunAsync("pwsh", Path.GetTempPath(),
            ["-NoLogo", "-NoProfile", "-NonInteractive", "-EncodedCommand", encoded],
            cancellation.Token);
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
                    (exception.HResult & 0xffff) is 32 or 33) { }
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
