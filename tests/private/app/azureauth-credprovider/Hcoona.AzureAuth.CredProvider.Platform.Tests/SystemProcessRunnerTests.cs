using System.Buffers;
using System.Diagnostics;
using System.Runtime.InteropServices;
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
                environment: ProcessTestApp.CreateHelperEnvironment(helperNonce)
            ),
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
                environment: ProcessTestApp.CreateHelperEnvironment(helperNonce)
            ),
            TestContext.Current.CancellationToken
        );

        Assert.False(result.Succeeded);
        Assert.Equal(ConfigurationErrorExitCode, result.ExitCode);
        Assert.Equal(string.Empty, result.StandardOutput);
        AssertExactNormalizedStderr(result, "Unknown process helper command 'unknown-command'.\n");
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
        int childProcessId = await WaitForProcessIdAsync(
            pidFile,
            TestContext.Current.CancellationToken
        );

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
                    StandardErrorByteLimit = 32,
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
    public void ProcessStartSpecRejectsInvalidTimeoutAndCaptureLimits()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            new ProcessStartSpec("tool", timeout: TimeSpan.Zero)
        );
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            new ProcessStartSpec(
                "tool",
                outputCaptureOptions: new ProcessOutputCaptureOptions
                {
                    StandardOutputByteLimit = 0,
                    StandardErrorByteLimit = 1,
                }
            )
        );
    }

    private static ProcessStartSpec HelperStartSpec(
        string command,
        string? workingDirectory = null,
        IReadOnlyList<string>? arguments = null,
        IReadOnlyDictionary<string, string?>? environment = null,
        string? standardInput = null
    )
    {
        var helperNonce = ProcessTestApp.CreateHelperNonce();
        var allArguments = ProcessTestApp.CreateHelperArguments(helperNonce, command, arguments);

        return new ProcessStartSpec(
            ProcessTestApp.AppHostPath(),
            allArguments,
            workingDirectory,
            ProcessTestApp.CreateHelperEnvironment(helperNonce, environment),
            standardInput
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

    [Fact]
    public async Task RunAsyncStreamsBoundedStderrBeforeProcessExitAndCapturesStdout()
    {
        const string standardInput = "device-code-input";
        const string expectedStandardOutput = "stdout=device-code-input / 雪 🌍";
        const string expectedStandardError = "stderr=device-code-input / café";
        var promptWriter = new CoordinatedFlushStringWriter();
        var helperNonce = ProcessTestApp.CreateHelperNonce();
        var startSpec = new ProcessStartSpec(
            ProcessTestApp.AppHostPath(),
            ProcessTestApp.CreateHelperArguments(helperNonce, "utf8-roundtrip"),
            environment: ProcessTestApp.CreateHelperEnvironment(helperNonce),
            standardInput: standardInput,
            outputCaptureOptions: new ProcessOutputCaptureOptions
            {
                StandardOutputByteLimit = 256,
                StandardErrorByteLimit = 256,
            },
            standardErrorTee: promptWriter
        );
        var runner = new SystemProcessRunner();

        Assert.Same(promptWriter, startSpec.StandardErrorTee);
        Task<ProcessResult> runTask = runner.RunAsync(
            startSpec,
            TestContext.Current.CancellationToken
        );
        try
        {
            await promptWriter.FlushObserved.WaitAsync(
                TimeSpan.FromSeconds(10),
                TestContext.Current.CancellationToken
            );
            Assert.Equal(expectedStandardError, promptWriter.ToString());
            Assert.DoesNotContain(
                expectedStandardOutput,
                promptWriter.ToString(),
                StringComparison.Ordinal
            );
            Assert.False(runTask.IsCompleted);
        }
        finally
        {
            promptWriter.Release();
        }

        ProcessResult result = await runTask.WaitAsync(
            TimeSpan.FromSeconds(10),
            TestContext.Current.CancellationToken
        );

        Assert.Equal(ProcessExecutionStatus.Success, result.Status);
        Assert.Equal(0, result.ExitCode);
        Assert.Equal(expectedStandardOutput, result.StandardOutput);
        Assert.Equal(expectedStandardError, result.StandardError);
        Assert.Equal(result.StandardError, promptWriter.ToString());
    }

    [Fact]
    public async Task RunAsyncStandardErrorTeeHonorsStandardErrorByteLimit()
    {
        const int standardErrorByteLimit = 64;
        string standardInput = new('p', 5_000);
        string completeStandardError = "stderr=" + standardInput + " / café";
        byte[] completeStandardErrorBytes = Encoding.UTF8.GetBytes(completeStandardError);
        string expectedBoundedPrefix = Encoding.UTF8.GetString(
            completeStandardErrorBytes,
            0,
            standardErrorByteLimit
        );
        var promptWriter = new StringWriter();
        var helperNonce = ProcessTestApp.CreateHelperNonce();
        var startSpec = new ProcessStartSpec(
            ProcessTestApp.AppHostPath(),
            ProcessTestApp.CreateHelperArguments(helperNonce, "utf8-roundtrip"),
            environment: ProcessTestApp.CreateHelperEnvironment(helperNonce),
            standardInput: standardInput,
            outputCaptureOptions: new ProcessOutputCaptureOptions
            {
                StandardOutputByteLimit = 8_192,
                StandardErrorByteLimit = standardErrorByteLimit,
            },
            standardErrorTee: promptWriter
        );
        var runner = new SystemProcessRunner();

        ProcessResult result = await runner.RunAsync(
            startSpec,
            TestContext.Current.CancellationToken
        );

        Assert.Equal(ProcessExecutionStatus.OutputTooLarge, result.Status);
        Assert.False(result.Succeeded);
        Assert.Equal(standardErrorByteLimit, Encoding.UTF8.GetByteCount(result.StandardError));
        Assert.Equal(expectedBoundedPrefix, result.StandardError);
        Assert.Equal(result.StandardError, promptWriter.ToString());
        Assert.DoesNotContain("café", promptWriter.ToString(), StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("AZUREAUTH_MODE")]
    [InlineData("AZUREAUTH_NO_USER")]
    [InlineData("Corext_NonInteractive")]
    public async Task RunAsyncRemovesInheritedAzureAuthControlVariable(string variableName)
    {
        const string inheritedSentinel = "must-not-reach-child";
        string? originalValue = Environment.GetEnvironmentVariable(variableName);
        Environment.SetEnvironmentVariable(variableName, inheritedSentinel);

        try
        {
            var startSpec = HelperStartSpec(
                "read-env-list",
                arguments: [variableName],
                environment: new Dictionary<string, string?> { [variableName] = null }
            );
            var runner = new SystemProcessRunner();

            ProcessResult result = await runner.RunAsync(
                startSpec,
                TestContext.Current.CancellationToken
            );

            Assert.True(result.Succeeded);
            Assert.Equal(0, result.ExitCode);
            Assert.Null(startSpec.Environment[variableName]);
            Assert.Equal(string.Empty, DecodeLines(result.StandardOutput)["env0"]);
            Assert.Equal(string.Empty, result.StandardError);
            Assert.DoesNotContain(
                inheritedSentinel,
                result.StandardOutput,
                StringComparison.Ordinal
            );
        }
        finally
        {
            Environment.SetEnvironmentVariable(variableName, originalValue);
        }
    }

    private sealed class CoordinatedFlushStringWriter : StringWriter
    {
        private readonly TaskCompletionSource flushObserved = new(
            TaskCreationOptions.RunContinuationsAsynchronously
        );
        private readonly TaskCompletionSource release = new(
            TaskCreationOptions.RunContinuationsAsynchronously
        );

        public Task FlushObserved => flushObserved.Task;

        public void Release()
        {
            release.TrySetResult();
        }

        public override void Flush()
        {
            flushObserved.TrySetResult();
            release.Task.GetAwaiter().GetResult();
        }

        public override Task FlushAsync()
        {
            flushObserved.TrySetResult();
            return release.Task;
        }

        public override Task FlushAsync(CancellationToken cancellationToken)
        {
            flushObserved.TrySetResult();
            return release.Task.WaitAsync(cancellationToken);
        }
    }

    [Theory]
    [InlineData(NonCooperativeTeeOperation.Write)]
    [InlineData(NonCooperativeTeeOperation.Flush)]
    public async Task RunAsyncExternalCancellationDoesNotWaitForNonCooperativeStderrTee(
        NonCooperativeTeeOperation blockedOperation
    )
    {
        const string arbitraryStderrSentinel = "stderr=";
        var writer = new NonCooperativeTextWriter(blockedOperation);
        var cancellation = CancellationTokenSource.CreateLinkedTokenSource(
            TestContext.Current.CancellationToken
        );
        Task<ProcessResult>? runTask = null;

        try
        {
            var helperNonce = ProcessTestApp.CreateHelperNonce();
            runTask = new SystemProcessRunner().RunAsync(
                new ProcessStartSpec(
                    ProcessTestApp.AppHostPath(),
                    ProcessTestApp.CreateHelperArguments(helperNonce, "exit", ["0"]),
                    environment: ProcessTestApp.CreateHelperEnvironment(helperNonce),
                    timeout: TimeSpan.FromSeconds(30),
                    standardErrorTee: writer
                ),
                cancellation.Token
            );

            Task firstCompletion = await Task.WhenAny(writer.OperationEntered, runTask)
                .WaitAsync(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken);
            Assert.Same(writer.OperationEntered, firstCompletion);
            Assert.False(writer.PendingOperation.IsCompleted);
            Assert.False(runTask.IsCompleted);
            Assert.Contains(arbitraryStderrSentinel, writer.WrittenText, StringComparison.Ordinal);

            cancellation.Cancel();

            OperationCanceledException exception =
                await Assert.ThrowsAnyAsync<OperationCanceledException>(async () =>
                    await runTask.WaitAsync(
                        TimeSpan.FromSeconds(10),
                        TestContext.Current.CancellationToken
                    )
                );

            Assert.False(writer.PendingOperation.IsCompleted);
            Assert.DoesNotContain(
                arbitraryStderrSentinel,
                exception.Message,
                StringComparison.Ordinal
            );
        }
        finally
        {
            await CleanupNonCooperativeRunAsync(writer, cancellation, runTask);
        }
    }

    [Theory]
    [InlineData(NonCooperativeTeeOperation.Write)]
    [InlineData(NonCooperativeTeeOperation.Flush)]
    public async Task RunAsyncConfiguredTimeoutDoesNotWaitForNonCooperativeStderrTee(
        NonCooperativeTeeOperation blockedOperation
    )
    {
        const string arbitraryStderrSentinel = "stderr=";
        var writer = new NonCooperativeTextWriter(blockedOperation);
        var callerCancellation = CancellationTokenSource.CreateLinkedTokenSource(
            TestContext.Current.CancellationToken
        );
        Task<ProcessResult>? runTask = null;

        try
        {
            var helperNonce = ProcessTestApp.CreateHelperNonce();
            runTask = new SystemProcessRunner().RunAsync(
                new ProcessStartSpec(
                    ProcessTestApp.AppHostPath(),
                    ProcessTestApp.CreateHelperArguments(helperNonce, "exit", ["0"]),
                    environment: ProcessTestApp.CreateHelperEnvironment(helperNonce),
                    timeout: TimeSpan.FromSeconds(2),
                    standardErrorTee: writer
                ),
                callerCancellation.Token
            );

            Task firstCompletion = await Task.WhenAny(writer.OperationEntered, runTask)
                .WaitAsync(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken);
            Assert.Same(writer.OperationEntered, firstCompletion);
            Assert.False(writer.PendingOperation.IsCompleted);
            Assert.False(runTask.IsCompleted);
            Assert.Contains(arbitraryStderrSentinel, writer.WrittenText, StringComparison.Ordinal);

            ProcessResult result = await runTask.WaitAsync(
                TimeSpan.FromSeconds(10),
                TestContext.Current.CancellationToken
            );

            Assert.Equal(ProcessExecutionStatus.TimedOut, result.Status);
            Assert.False(result.Succeeded);
            Assert.False(writer.PendingOperation.IsCompleted);
            Assert.Contains(
                arbitraryStderrSentinel,
                result.StandardError,
                StringComparison.Ordinal
            );
            Assert.Equal(writer.WrittenText, result.StandardError);
            Assert.DoesNotContain(
                arbitraryStderrSentinel,
                result.ToString(),
                StringComparison.Ordinal
            );
        }
        finally
        {
            await CleanupNonCooperativeRunAsync(writer, callerCancellation, runTask);
        }
    }

    [Fact]
    public async Task RunAsyncCancellationRetainedStderrTeeMemoryRemainsOwnedAfterPoolReuse()
    {
        const string standardInput = "retained-owned-memory-雪";
        const string expectedStandardError = "stderr=retained-owned-memory-雪 / café";
        const char poolOverwrite = '\u25A1';
        var writer = new NonCooperativeTextWriter(NonCooperativeTeeOperation.Write);
        var cancellation = CancellationTokenSource.CreateLinkedTokenSource(
            TestContext.Current.CancellationToken
        );
        Task<ProcessResult>? runTask = null;
        var rentedBuffers = new List<char[]>();

        try
        {
            var helperNonce = ProcessTestApp.CreateHelperNonce();
            runTask = new SystemProcessRunner().RunAsync(
                new ProcessStartSpec(
                    ProcessTestApp.AppHostPath(),
                    ProcessTestApp.CreateHelperArguments(helperNonce, "utf8-roundtrip"),
                    environment: ProcessTestApp.CreateHelperEnvironment(helperNonce),
                    standardInput: standardInput,
                    timeout: TimeSpan.FromSeconds(30),
                    standardErrorTee: writer
                ),
                cancellation.Token
            );

            await writer.OperationEntered.WaitAsync(
                TimeSpan.FromSeconds(10),
                TestContext.Current.CancellationToken
            );
            Assert.False(runTask.IsCompleted);

            cancellation.Cancel();
            await Assert.ThrowsAnyAsync<OperationCanceledException>(async () =>
                await runTask.WaitAsync(
                    TimeSpan.FromSeconds(10),
                    TestContext.Current.CancellationToken
                )
            );
            Assert.False(writer.PendingOperation.IsCompleted);

            ReadOnlyMemory<char> retainedMemory = writer.RetainedWriteMemory;
            bool isStringBacked = MemoryMarshal.TryGetString(retainedMemory, out _, out _, out _);
            bool isExactArrayBacked =
                MemoryMarshal.TryGetArray(retainedMemory, out ArraySegment<char> segment)
                && segment.Array is not null
                && segment.Offset == 0
                && segment.Count == segment.Array.Length;

            int pooledDecoderBufferLength = Encoding.UTF8.GetMaxCharCount(4096);
            for (var index = 0; index < 32; index++)
            {
                char[] rented = ArrayPool<char>.Shared.Rent(pooledDecoderBufferLength);
                rented.AsSpan().Fill(poolOverwrite);
                rentedBuffers.Add(rented);
            }

            writer.Release();
            await writer.PendingOperation.WaitAsync(
                TimeSpan.FromSeconds(10),
                TestContext.Current.CancellationToken
            );
            string retainedPayload = writer.ReadRetainedWritePayload();

            Assert.True(
                isStringBacked || isExactArrayBacked,
                "The tee retained a view over a larger buffer that could be returned to a pool."
            );
            Assert.Equal(expectedStandardError, retainedPayload);
            Assert.DoesNotContain(
                poolOverwrite.ToString(),
                retainedPayload,
                StringComparison.Ordinal
            );
        }
        finally
        {
            foreach (char[] rented in rentedBuffers)
            {
                ArrayPool<char>.Shared.Return(rented, clearArray: true);
            }

            await CleanupNonCooperativeRunAsync(writer, cancellation, runTask);
        }
    }

    private static async Task CleanupNonCooperativeRunAsync(
        NonCooperativeTextWriter writer,
        CancellationTokenSource cancellation,
        Task<ProcessResult>? runTask
    )
    {
        Task pendingOperation = writer.PendingOperation;
        writer.Release();

        try
        {
            await pendingOperation.WaitAsync(TimeSpan.FromSeconds(10));
        }
        finally
        {
            cancellation.Cancel();
            try
            {
                if (runTask is not null)
                {
                    try
                    {
                        await runTask.WaitAsync(TimeSpan.FromSeconds(10));
                    }
                    catch (OperationCanceledException) when (cancellation.IsCancellationRequested)
                    { }
                }
            }
            finally
            {
                cancellation.Dispose();
            }
        }
    }

    public enum NonCooperativeTeeOperation
    {
        Write,
        Flush,
    }

    private sealed class NonCooperativeTextWriter(NonCooperativeTeeOperation blockedOperation)
        : TextWriter
    {
        private readonly object sync = new();
        private readonly StringBuilder writtenText = new();
        private readonly TaskCompletionSource operationEntered = new(
            TaskCreationOptions.RunContinuationsAsynchronously
        );
        private readonly TaskCompletionSource release = new(
            TaskCreationOptions.RunContinuationsAsynchronously
        );
        private Task? pendingOperation;
        private ReadOnlyMemory<char> retainedWriteMemory;
        private bool hasRetainedWriteMemory;

        public override Encoding Encoding => Encoding.UTF8;

        public Task OperationEntered => operationEntered.Task;

        public Task PendingOperation => Volatile.Read(ref pendingOperation) ?? Task.CompletedTask;

        public ReadOnlyMemory<char> RetainedWriteMemory
        {
            get
            {
                lock (sync)
                {
                    if (!hasRetainedWriteMemory)
                    {
                        throw new InvalidOperationException("No tee write has been retained.");
                    }

                    return retainedWriteMemory;
                }
            }
        }

        public string WrittenText
        {
            get
            {
                lock (sync)
                {
                    return writtenText.ToString();
                }
            }
        }

        public void Release()
        {
            release.TrySetResult();
        }

        public string ReadRetainedWritePayload()
        {
            return RetainedWriteMemory.ToString();
        }

        public override Task WriteAsync(
            ReadOnlyMemory<char> buffer,
            CancellationToken cancellationToken = default
        )
        {
            lock (sync)
            {
                writtenText.Append(buffer.Span);
                retainedWriteMemory = buffer;
                hasRetainedWriteMemory = true;
            }

            if (blockedOperation != NonCooperativeTeeOperation.Write)
            {
                return Task.CompletedTask;
            }

            BlockCurrentOperation();
            return release.Task;
        }

        public override Task FlushAsync(CancellationToken cancellationToken)
        {
            if (blockedOperation != NonCooperativeTeeOperation.Flush)
            {
                return Task.CompletedTask;
            }

            BlockCurrentOperation();
            return release.Task;
        }

        private void BlockCurrentOperation()
        {
            Volatile.Write(ref pendingOperation, release.Task);
            operationEntered.TrySetResult();
        }
    }

    [Theory]
    [InlineData(NonCooperativeTeeOperation.Write)]
    [InlineData(NonCooperativeTeeOperation.Flush)]
    public async Task RunAsyncStderrTeeFailureCancelsProcessWithoutWaitingForTimeout(
        NonCooperativeTeeOperation failedOperation
    )
    {
        var helperNonce = ProcessTestApp.CreateHelperNonce();
        var stopwatch = Stopwatch.StartNew();

        ProcessResult result = await new SystemProcessRunner().RunAsync(
            new ProcessStartSpec(
                ProcessTestApp.AppHostPath(),
                ProcessTestApp.CreateHelperArguments(helperNonce, "stderr-then-sleep"),
                environment: ProcessTestApp.CreateHelperEnvironment(helperNonce),
                timeout: TimeSpan.FromSeconds(5),
                standardErrorTee: new ThrowingTextWriter(failedOperation)
            ),
            TestContext.Current.CancellationToken
        );

        stopwatch.Stop();
        Assert.Equal(ProcessExecutionStatus.InvalidOutput, result.Status);
        Assert.Equal("device-code prompt", result.StandardError);
        Assert.True(
            stopwatch.Elapsed < TimeSpan.FromSeconds(2),
            $"Runner took {stopwatch.Elapsed}."
        );
    }

    private sealed class ThrowingTextWriter(NonCooperativeTeeOperation failedOperation) : TextWriter
    {
        public override Encoding Encoding => Encoding.UTF8;

        public override Task WriteAsync(
            ReadOnlyMemory<char> buffer,
            CancellationToken cancellationToken = default
        )
        {
            return failedOperation == NonCooperativeTeeOperation.Write
                ? Task.FromException(new IOException("Prompt writer failed."))
                : Task.CompletedTask;
        }

        public override Task FlushAsync(CancellationToken cancellationToken)
        {
            return failedOperation == NonCooperativeTeeOperation.Flush
                ? Task.FromException(new IOException("Prompt writer failed."))
                : Task.CompletedTask;
        }
    }

    [Fact]
    public async Task RunAsyncReturnsInvalidOutputForInvalidUtf8StderrWithoutTeeLeak()
    {
        if (!OperatingSystem.IsLinux() || !File.Exists("/bin/sh"))
        {
            return;
        }

        var standardErrorTee = new StringWriter();
        var runner = new SystemProcessRunner();
        ProcessResult result = await runner.RunAsync(
            new ProcessStartSpec(
                "/bin/sh",
                ["-c", "printf '\\377' >&2"],
                standardErrorTee: standardErrorTee
            ),
            TestContext.Current.CancellationToken
        );

        Assert.Equal(ProcessExecutionStatus.InvalidOutput, result.Status);
        Assert.Empty(result.StandardOutput);
        Assert.Empty(result.StandardError);
        Assert.Equal(string.Empty, standardErrorTee.ToString());
    }
}
