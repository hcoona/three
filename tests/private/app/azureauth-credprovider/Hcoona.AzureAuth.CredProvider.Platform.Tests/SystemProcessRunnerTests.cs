using System.Diagnostics;
using System.Text;
using Hcoona.AzureAuth.CredProvider.Platform.Processes;
using Xunit;

namespace Hcoona.AzureAuth.CredProvider.Platform.Tests;

public sealed class SystemProcessRunnerTests
{
    private const int ConfigurationErrorExitCode = 64;

    [Fact]
    public async Task RunAsyncPassesArgumentsEnvironmentWorkingDirectoryAndStandardInput()
    {
        var workingDirectory = CreateTestDirectory("process cwd with spaces");
        var runner = new SystemProcessRunner();
        var startSpec = HelperStartSpec(
            "inspect",
            workingDirectory,
            ["first argument", "path with spaces\\file.txt"],
            new Dictionary<string, string?>
            {
                ["AZUREAUTH_PROCESS_HELPER_VALUE"] = "environment value with spaces",
            },
            "stdin value with spaces"
        );

        var result = await runner.RunAsync(startSpec, TestContext.Current.CancellationToken);

        Assert.True(result.Succeeded);
        var output = DecodeLines(result.StandardOutput);
        Assert.Equal(workingDirectory, output["cwd"]);
        Assert.Equal("environment value with spaces", output["env"]);
        Assert.Equal("stdin value with spaces", output["stdin"]);
        Assert.Equal("2", output["argc"]);
        Assert.Equal("first argument", output["arg0"]);
        Assert.Equal("path with spaces\\file.txt", output["arg1"]);
        Assert.Equal("standard error with spaces", DecodeLines(result.StandardError)["stderr"]);
    }

    [Fact]
    public async Task RunAsyncUsesUtf8ForRedirectedStandardStreams()
    {
        var runner = new SystemProcessRunner();
        const string standardInput = "stdin café 雪 🌍";

        var result = await runner.RunAsync(
            HelperStartSpec("utf8-roundtrip", standardInput: standardInput),
            TestContext.Current.CancellationToken
        );

        Assert.True(result.Succeeded);
        Assert.Equal("stdout=stdin café 雪 🌍 / 雪 🌍", result.StandardOutput);
        Assert.Equal("stderr=stdin café 雪 🌍 / café", result.StandardError);
    }

    [Fact]
    public async Task RunAsyncPassesShellMetacharactersAsLiteralArgumentsWithoutSideEffects()
    {
        var workingDirectory = CreateTestDirectory("process metacharacters");
        var markerFile = Path.Combine(workingDirectory, "shell-marker.txt");
        var metacharacterArgument = OperatingSystem.IsWindows()
            ? $"literal & type nul > \"{markerFile}\" & rem"
            : $"literal; touch {ShellQuote(markerFile)} #";
        var runner = new SystemProcessRunner();

        var result = await runner.RunAsync(
            HelperStartSpec("inspect", workingDirectory, [metacharacterArgument]),
            TestContext.Current.CancellationToken
        );

        Assert.True(result.Succeeded);
        var output = DecodeLines(result.StandardOutput);
        Assert.Equal("1", output["argc"]);
        Assert.Equal(metacharacterArgument, output["arg0"]);
        Assert.False(File.Exists(markerFile));
    }

    [Fact]
    public async Task RunAsyncReturnsNonZeroExitCodeWithCapturedOutput()
    {
        var runner = new SystemProcessRunner();
        var startSpec = HelperStartSpec("exit", arguments: ["37"]);

        var result = await runner.RunAsync(startSpec, TestContext.Current.CancellationToken);

        Assert.Equal(ProcessExecutionStatus.NonZeroExit, result.Status);
        Assert.Equal(37, result.ExitCode);
        Assert.False(result.Succeeded);
        Assert.Equal("nonzero stdout", DecodeLines(result.StandardOutput)["stdout"]);
        Assert.Equal("nonzero stderr", DecodeLines(result.StandardError)["stderr"]);
    }

    [Fact]
    public async Task RunAsyncNullEnvironmentValueRemovesInheritedVariable()
    {
        var runner = new SystemProcessRunner();
        var variableName = "AZUREAUTH_PROCESS_HELPER_REMOVED_" + Guid.NewGuid().ToString("N");
        var originalValue = Environment.GetEnvironmentVariable(variableName);
        Environment.SetEnvironmentVariable(variableName, "inherited");

        try
        {
            var startSpec = HelperStartSpec(
                "read-env",
                arguments: [variableName],
                environment: new Dictionary<string, string?> { [variableName] = null }
            );

            var result = await runner.RunAsync(startSpec, TestContext.Current.CancellationToken);

            Assert.True(result.Succeeded);
            Assert.Equal(string.Empty, DecodeLines(result.StandardOutput)["env"]);
        }
        finally
        {
            Environment.SetEnvironmentVariable(variableName, originalValue);
        }
    }

    [Fact]
    public async Task RunAsyncWithExplicitOnlyEnvironmentClearsInheritedVariables()
    {
        var runner = new SystemProcessRunner();
        var inheritedVariableName =
            "AZUREAUTH_PROCESS_HELPER_INHERITED_" + Guid.NewGuid().ToString("N");
        var approvedVariableName =
            "AZUREAUTH_PROCESS_HELPER_APPROVED_" + Guid.NewGuid().ToString("N");
        var originalValue = Environment.GetEnvironmentVariable(inheritedVariableName);
        Environment.SetEnvironmentVariable(inheritedVariableName, "inherited");

        try
        {
            var startSpec = HelperStartSpec(
                "read-env-list",
                arguments: [inheritedVariableName, approvedVariableName],
                environment: new Dictionary<string, string?>
                {
                    [approvedVariableName] = "approved",
                },
                environmentMode: ProcessEnvironmentMode.ExplicitOnly
            );

            var result = await runner.RunAsync(startSpec, TestContext.Current.CancellationToken);

            Assert.True(result.Succeeded);
            var output = DecodeLines(result.StandardOutput);
            Assert.Equal(string.Empty, output["env0"]);
            Assert.Equal("approved", output["env1"]);
        }
        finally
        {
            Environment.SetEnvironmentVariable(inheritedVariableName, originalValue);
        }
    }

    [Fact]
    public async Task RunAsyncWithInvalidEnvironmentModeThrowsWithoutLaunchingProcess()
    {
        var markerFile = Path.Combine(
            CreateTestDirectory("invalid environment mode"),
            "launched.txt"
        );
        var runner = new SystemProcessRunner();
        var startSpec = HelperStartSpec(
            "write-marker",
            arguments: [markerFile],
            environmentMode: (ProcessEnvironmentMode)42
        );

        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() =>
            runner.RunAsync(startSpec, TestContext.Current.CancellationToken)
        );

        Assert.False(File.Exists(markerFile));
    }

    [Fact]
    public async Task RunAsyncClosesStandardInputWhenStandardInputIsNull()
    {
        var runner = new SystemProcessRunner();

        var result = await runner.RunAsync(
            HelperStartSpec("inspect", standardInput: null),
            TestContext.Current.CancellationToken
        );

        Assert.True(result.Succeeded);
        Assert.Equal(string.Empty, DecodeLines(result.StandardOutput)["stdin"]);
    }

    [Fact]
    public async Task RunAsyncRejectsMalformedHelperInvocationWhenHelperModeIsEnabled()
    {
        var runner = new SystemProcessRunner();
        var helperNonce = ProcessTestApp.CreateHelperNonce();

        var result = await runner.RunAsync(
            new ProcessStartSpec(
                TestAppHostPath(),
                [ProcessTestApp.HelperSwitch, ProcessTestApp.HelperNonceSwitch, helperNonce],
                environment: ProcessTestApp.CreateHelperEnvironment(
                    helperNonce,
                    environmentMode: ProcessEnvironmentMode.ExplicitOnly
                ),
                environmentMode: ProcessEnvironmentMode.ExplicitOnly),
            TestContext.Current.CancellationToken
        );

        Assert.False(result.Succeeded);
        Assert.Equal(ConfigurationErrorExitCode, result.ExitCode);
        Assert.Equal(string.Empty, result.StandardOutput);
        AssertExactNormalizedStderr(result, "Malformed process helper invocation.\n");
    }

    [Fact]
    public async Task RunAsyncRejectsUnknownHelperCommandWhenHelperModeIsEnabled()
    {
        var runner = new SystemProcessRunner();
        var helperNonce = ProcessTestApp.CreateHelperNonce();

        var result = await runner.RunAsync(
            new ProcessStartSpec(
                TestAppHostPath(),
                [
                    ProcessTestApp.HelperSwitch,
                    ProcessTestApp.HelperNonceSwitch,
                    helperNonce,
                    "unknown-command",
                ],
                environment: ProcessTestApp.CreateHelperEnvironment(
                    helperNonce,
                    environmentMode: ProcessEnvironmentMode.ExplicitOnly
                ),
                environmentMode: ProcessEnvironmentMode.ExplicitOnly),
            TestContext.Current.CancellationToken
        );

        Assert.False(result.Succeeded);
        Assert.Equal(ConfigurationErrorExitCode, result.ExitCode);
        Assert.Equal(string.Empty, result.StandardOutput);
        AssertExactNormalizedStderr(
            result,
            "Unknown process helper command 'unknown-command'.\n");
    }

    [Fact]
    public async Task RunAsyncKillsProcessTreeAndThrowsWhenCanceled()
    {
        var pidFile = Path.Combine(CreateTestDirectory("process tree"), "child.pid");
        var runner = new SystemProcessRunner();
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(
            TestContext.Current.CancellationToken
        );
        var runTask = runner.RunAsync(
            HelperStartSpec("spawn-child-and-sleep", arguments: [pidFile]),
            cancellation.Token
        );
        var childProcessId = await WaitForProcessIdAsync(
            pidFile,
            TestContext.Current.CancellationToken
        );

        cancellation.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => runTask);
        await AssertProcessExitsAsync(childProcessId, TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task RunAsyncKillsProcessTreeAndRethrowsWhenPostStartWriteFails()
    {
        if (OperatingSystem.IsWindows() || !File.Exists("/bin/sh"))
        {
            return;
        }

        var pidFile = Path.Combine(CreateTestDirectory("process tree failure"), "child.pid");
        var runner = new SystemProcessRunner();
        var runTask = runner.RunAsync(
            new ProcessStartSpec(
                "/bin/sh",
                ["-c", $"sleep 30 & echo $! > {ShellQuote(pidFile)}; exec 0<&-; sleep 30"],
                standardInput: new string('x', 1024 * 1024)
            ),
            TestContext.Current.CancellationToken
        );
        var childProcessId = await WaitForProcessIdAsync(
            pidFile,
            TestContext.Current.CancellationToken
        );

        await Assert.ThrowsAnyAsync<IOException>(() => runTask);
        await AssertProcessExitsAsync(childProcessId, TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task RunAsyncReturnsTimedOutStatusAndKillsProcessTreeWhenTimeoutExpires()
    {
        if (OperatingSystem.IsWindows() || !File.Exists("/bin/sh"))
        {
            return;
        }

        var pidFile = Path.Combine(CreateTestDirectory("process timeout"), "child.pid");
        var runner = new SystemProcessRunner();
        Task<ProcessResult> runTask = runner.RunAsync(
            new ProcessStartSpec(
                "/bin/sh",
                ["-c", $"sleep 30 & echo $! > {ShellQuote(pidFile)}; sleep 30"],
                timeout: TimeSpan.FromMilliseconds(200)
            ),
            TestContext.Current.CancellationToken
        );
        int childProcessId = await WaitForProcessIdAsync(pidFile, TestContext.Current.CancellationToken);

        ProcessResult result = await runTask;

        Assert.Equal(ProcessExecutionStatus.TimedOut, result.Status);
        await AssertProcessExitsAsync(childProcessId, TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task RunAsyncTimesOutWhileChildDoesNotReadLargeStandardInput()
    {
        if (!OperatingSystem.IsLinux() || !File.Exists("/bin/sh"))
        {
            return;
        }

        var runner = new SystemProcessRunner();
        var stopwatch = Stopwatch.StartNew();

        ProcessResult result = await runner.RunAsync(
            new ProcessStartSpec(
                "/bin/sh",
                ["-c", "sleep 30"],
                standardInput: new string('x', 16 * 1024 * 1024),
                timeout: TimeSpan.FromMilliseconds(150)
            ),
            TestContext.Current.CancellationToken
        );

        stopwatch.Stop();
        Assert.Equal(ProcessExecutionStatus.TimedOut, result.Status);
        Assert.True(
            stopwatch.Elapsed < TimeSpan.FromSeconds(3),
            $"Runner took {stopwatch.Elapsed}."
        );
    }

    [Fact]
    public async Task RunAsyncReturnsWithinBoundWhenExitedRootLeavesInheritedPipeOpen()
    {
        if (OperatingSystem.IsWindows() || !File.Exists("/bin/sh"))
        {
            return;
        }

        var runner = new SystemProcessRunner();
        var stopwatch = Stopwatch.StartNew();

        ProcessResult result = await runner.RunAsync(
            new ProcessStartSpec(
                "/bin/sh",
                ["-c", "sleep 4 &"],
                timeout: TimeSpan.FromMilliseconds(150)
            ),
            TestContext.Current.CancellationToken
        );

        stopwatch.Stop();
        Assert.Equal(ProcessExecutionStatus.TimedOut, result.Status);
        Assert.True(
            stopwatch.Elapsed < TimeSpan.FromSeconds(3),
            $"Runner took {stopwatch.Elapsed}."
        );
    }

    [Fact]
    public async Task RunAsyncReturnsOutputTooLargeForBoundedStdoutCapture()
    {
        if (OperatingSystem.IsWindows() || !File.Exists("/bin/sh"))
        {
            return;
        }

        var runner = new SystemProcessRunner();
        ProcessResult result = await runner.RunAsync(
            new ProcessStartSpec(
                "/bin/sh",
                ["-c", "i=0; while [ $i -lt 10000 ]; do printf x; i=$((i+1)); done"],
                timeout: TimeSpan.FromSeconds(5),
                outputCaptureOptions: new ProcessOutputCaptureOptions
                {
                    StandardOutputByteLimit = 32,
                    StandardOutputCharacterLimit = 32,
                    StandardErrorByteLimit = 32,
                    StandardErrorCharacterLimit = 32,
                }
            ),
            TestContext.Current.CancellationToken
        );

        Assert.Equal(ProcessExecutionStatus.OutputTooLarge, result.Status);
        Assert.True(result.StandardOutput.Length <= 32);
    }

    [Fact]
    public async Task RunAsyncReturnsInvalidOutputForInvalidUtf8Stdout()
    {
        if (OperatingSystem.IsWindows() || !File.Exists("/bin/sh"))
        {
            return;
        }

        var runner = new SystemProcessRunner();
        ProcessResult result = await runner.RunAsync(
            new ProcessStartSpec("/bin/sh", ["-c", "printf '\\377'"]),
            TestContext.Current.CancellationToken
        );

        Assert.Equal(ProcessExecutionStatus.InvalidOutput, result.Status);
        Assert.Empty(result.StandardOutput);
    }

    [Fact]
    public async Task RunAsyncReturnsLaunchFailureForMissingExecutable()
    {
        var runner = new SystemProcessRunner();

        ProcessResult result = await runner.RunAsync(
            new ProcessStartSpec(Path.Combine(CreateTestDirectory("missing tool"), "missing-tool")),
            TestContext.Current.CancellationToken
        );

        Assert.Equal(ProcessExecutionStatus.LaunchFailure, result.Status);
        Assert.False(result.HasExitCode);
    }

    [Fact]
    public async Task RunAsyncWithPreCanceledTokenThrowsWithoutLaunchingProcess()
    {
        var markerFile = Path.Combine(CreateTestDirectory("pre canceled process"), "launched.txt");
        var runner = new SystemProcessRunner();
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(
            TestContext.Current.CancellationToken
        );
        cancellation.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            runner.RunAsync(
                HelperStartSpec("write-marker", arguments: [markerFile]),
                cancellation.Token
            )
        );

        Assert.False(File.Exists(markerFile));
    }

    [Fact]
    public async Task RunAsyncPreStartValidationFailureDoesNotLaunchHelperProcess()
    {
        var markerFile = Path.Combine(CreateTestDirectory("pre start validation"), "launched.txt");
        var runner = new SystemProcessRunner();
        var expectedException = new UnauthorizedAccessException("integrity check failed");

        var exception = await Assert.ThrowsAsync<UnauthorizedAccessException>(() =>
            runner.RunAsync(
                HelperStartSpec(
                    "write-marker",
                    arguments: [markerFile],
                    preStartValidation: _ => ValueTask.FromException(expectedException)
                ),
                TestContext.Current.CancellationToken
            )
        );

        Assert.Same(expectedException, exception);
        Assert.False(File.Exists(markerFile));
    }

    [Fact]
    public async Task RunAsyncWithCancellationAfterPreStartValidationThrows()
    {
        var markerFile = Path.Combine(
            CreateTestDirectory("post validation canceled process"),
            "launched.txt"
        );
        var runner = new SystemProcessRunner();
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(
            TestContext.Current.CancellationToken
        );

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            runner.RunAsync(
                HelperStartSpec(
                    "write-marker",
                    arguments: [markerFile],
                    preStartValidation: _ =>
                    {
                        cancellation.Cancel();
                        return ValueTask.CompletedTask;
                    }
                ),
                cancellation.Token
            )
        );

        Assert.False(File.Exists(markerFile));
    }

    [Fact]
    public void ProcessStartSpecRejectsUnboundedTimeoutAndCaptureLimits()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            new ProcessStartSpec("tool", timeout: TimeSpan.MaxValue)
        );
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            new ProcessStartSpec(
                "tool",
                outputCaptureOptions: new ProcessOutputCaptureOptions
                {
                    StandardOutputByteLimit =
                        ProcessOutputCaptureOptions.MaximumStreamLimit + 1,
                    StandardOutputCharacterLimit = 1,
                    StandardErrorByteLimit = 1,
                    StandardErrorCharacterLimit = 1,
                }
            )
        );
    }

    private static ProcessStartSpec HelperStartSpec(
        string command,
        string? workingDirectory = null,
        IReadOnlyList<string>? arguments = null,
        IReadOnlyDictionary<string, string?>? environment = null,
        string? standardInput = null,
        ProcessEnvironmentMode environmentMode = ProcessEnvironmentMode.Inherit,
        Func<CancellationToken, ValueTask>? preStartValidation = null
    )
    {
        var helperNonce = ProcessTestApp.CreateHelperNonce();
        var allArguments = ProcessTestApp.CreateHelperArguments(helperNonce, command, arguments);

        return new ProcessStartSpec(
            ProcessTestApp.AppHostPath(),
            allArguments,
            workingDirectory,
            ProcessTestApp.CreateHelperEnvironment(helperNonce, environment, environmentMode),
            standardInput,
            environmentMode,
            preStartValidation
        );
    }

    private static string TestAppHostPath()
    {
        return ProcessTestApp.AppHostPath();
    }

    private static void AssertExactNormalizedStderr(ProcessResult result, string expectedStderr)
    {
        Assert.Equal(expectedStderr, ProcessTestApp.NormalizeNewlines(result.StandardError));
    }

    private static string ShellQuote(string value)
    {
        return "'" + value.Replace("'", "'\"'\"'", StringComparison.Ordinal) + "'";
    }

    private static string CreateTestDirectory(string name)
    {
        var path = Path.Combine(AppContext.BaseDirectory, name, Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path);
        return path;
    }

    private static Dictionary<string, string> DecodeLines(string lines)
    {
        return lines
            .Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries)
            .Select(line => line.Split('=', 2))
            .ToDictionary(
                parts => parts[0],
                parts => Encoding.UTF8.GetString(Convert.FromBase64String(parts[1])),
                StringComparer.Ordinal
            );
    }

    private static async Task<int> WaitForProcessIdAsync(
        string path,
        CancellationToken cancellationToken
    )
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(10));

        while (!timeout.IsCancellationRequested)
        {
            if (File.Exists(path))
            {
                var value = await File.ReadAllTextAsync(path, timeout.Token).ConfigureAwait(false);
                if (int.TryParse(value, provider: null, out var processId))
                {
                    return processId;
                }
            }

            await Task.Delay(TimeSpan.FromMilliseconds(50), timeout.Token).ConfigureAwait(false);
        }

        throw new TimeoutException("Timed out waiting for child process id.");
    }

    private static async Task AssertProcessExitsAsync(
        int processId,
        CancellationToken cancellationToken
    )
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(10));

        while (!timeout.IsCancellationRequested)
        {
            try
            {
                using var process = Process.GetProcessById(processId);
                if (process.HasExited)
                {
                    return;
                }
            }
            catch (ArgumentException)
            {
                return;
            }

            await Task.Delay(TimeSpan.FromMilliseconds(50), timeout.Token).ConfigureAwait(false);
        }

        throw new TimeoutException($"Process {processId} did not exit after cancellation.");
    }
}
