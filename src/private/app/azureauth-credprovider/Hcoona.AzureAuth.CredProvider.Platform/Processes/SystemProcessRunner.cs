using System.Buffers;
using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;

namespace Hcoona.AzureAuth.CredProvider.Platform.Processes;

public sealed class SystemProcessRunner : IProcessRunner
{
    private static readonly TimeSpan ProcessCleanupTimeout = TimeSpan.FromSeconds(2);
    private static readonly Encoding RedirectedStreamEncoding = new UTF8Encoding(
        encoderShouldEmitUTF8Identifier: false,
        throwOnInvalidBytes: true
    );
    private static readonly string? UnixSessionLauncherPath = FindUnixSessionLauncher();
    private readonly IProcessCleanupStrategy processCleanup;
    private readonly TimeSpan processCleanupTimeout;
    private readonly IProcessStartStrategy processStart;
    private readonly Action<ProcessMilestone>? processMilestoneObserver;

    internal static bool IsUnixSessionLauncherAvailable => UnixSessionLauncherPath is not null;

    public SystemProcessRunner()
        : this(
            SystemProcessCleanupStrategy.Instance,
            ProcessCleanupTimeout,
            SystemProcessStartStrategy.Instance,
            processMilestoneObserver: null
        )
    { }

    internal SystemProcessRunner(
        IProcessCleanupStrategy processCleanup,
        TimeSpan processCleanupTimeout
    )
        : this(
            processCleanup,
            processCleanupTimeout,
            SystemProcessStartStrategy.Instance,
            processMilestoneObserver: null
        )
    { }

    internal SystemProcessRunner(IProcessStartStrategy processStart)
        : this(
            SystemProcessCleanupStrategy.Instance,
            ProcessCleanupTimeout,
            processStart,
            processMilestoneObserver: null
        )
    { }

    internal SystemProcessRunner(Action<ProcessMilestone> processMilestoneObserver)
        : this(
            SystemProcessCleanupStrategy.Instance,
            ProcessCleanupTimeout,
            SystemProcessStartStrategy.Instance,
            processMilestoneObserver
        )
    {
        ArgumentNullException.ThrowIfNull(processMilestoneObserver);
    }

    private SystemProcessRunner(
        IProcessCleanupStrategy processCleanup,
        TimeSpan processCleanupTimeout,
        IProcessStartStrategy processStart,
        Action<ProcessMilestone>? processMilestoneObserver
    )
    {
        ArgumentNullException.ThrowIfNull(processCleanup);
        ArgumentNullException.ThrowIfNull(processStart);
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(
            processCleanupTimeout,
            TimeSpan.Zero
        );
        this.processCleanup = processCleanup;
        this.processCleanupTimeout = processCleanupTimeout;
        this.processStart = processStart;
        this.processMilestoneObserver = processMilestoneObserver;
    }

    public async Task<ProcessResult> RunAsync(
        ProcessStartSpec startSpec,
        CancellationToken cancellationToken = default
    )
    {
        ArgumentNullException.ThrowIfNull(startSpec);
        cancellationToken.ThrowIfCancellationRequested();

        ProcessMilestoneReporter? milestones = processMilestoneObserver is null
            ? null
            : new ProcessMilestoneReporter(processMilestoneObserver);
        using var timeoutCancellation = new CancellationTokenSource(startSpec.Timeout);
        using var executionCancellation = new CancellationTokenSource();
        var cancellationCause = (int)ProcessCancellationCause.None;
        using CancellationTokenRegistration callerCancellationRegistration =
            cancellationToken.Register(() =>
                CancelExecution(ProcessCancellationCause.Caller)
            );
        using CancellationTokenRegistration timeoutCancellationRegistration =
            timeoutCancellation.Token.Register(() =>
                CancelExecution(ProcessCancellationCause.Timeout)
            );

        void CancelExecution(ProcessCancellationCause cause)
        {
            bool wonCancellation =
                Interlocked.CompareExchange(
                    ref cancellationCause,
                    (int)cause,
                    (int)ProcessCancellationCause.None
                ) == (int)ProcessCancellationCause.None;
            if (wonCancellation && cause == ProcessCancellationCause.Timeout)
            {
                milestones?.Report(ProcessMilestoneName.TimeoutInitiated);
            }

            executionCancellation.Cancel();
        }

        var process = new Process { StartInfo = CreateStartInfo(startSpec) };
        using var processLifetime = new ProcessLifetime(process, milestones);
        milestones?.Report(ProcessMilestoneName.LaunchRequested);
        try
        {
            if (!processStart.Start(process))
            {
                return ProcessResult.LaunchFailure();
            }
        }
        catch (Win32Exception)
        {
            return ProcessResult.LaunchFailure();
        }
        milestones?.Report(ProcessMilestoneName.ProcessStarted);

        var standardOutput = new BoundedOutputCapture(
            startSpec.OutputCaptureOptions.StandardOutputByteLimit
        );
        var standardError = new BoundedOutputCapture(
            startSpec.OutputCaptureOptions.StandardErrorByteLimit,
            startSpec.StandardErrorTee
        );

        Task standardInputTask = WriteAndCloseStandardInputAsync(
            process.StandardInput,
            startSpec.StandardInput,
            milestones,
            executionCancellation.Token
        );
        Task standardOutputTask = standardOutput.ReadAsync(
            process.StandardOutput.BaseStream,
            executionCancellation,
            milestones,
            ProcessMilestoneName.StandardOutputEof
        );
        Task standardErrorTask = standardError.ReadAsync(
            process.StandardError.BaseStream,
            executionCancellation,
            milestones,
            ProcessMilestoneName.StandardErrorEof
        );
        Task waitForExitTask =
            milestones is null
                ? process.WaitForExitAsync(executionCancellation.Token)
                : WaitForExitAsync(
                    process,
                    milestones,
                    executionCancellation.Token
                );
        Task allTasks = Task.WhenAll(
            standardInputTask,
            standardOutputTask,
            standardErrorTask,
            waitForExitTask
        );

        try
        {
            await allTasks.ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            bool outputTooLarge = standardOutput.TooLarge || standardError.TooLarge;
            bool invalidOutput = standardOutput.InvalidUtf8 || standardError.InvalidUtf8;
            bool teeFailed = standardError.TeeFailed;
            ProcessCancellationCause triggeringCancellationCause =
                (ProcessCancellationCause)Volatile.Read(ref cancellationCause);

            await KillAndWaitAsync(process, milestones).ConfigureAwait(false);

            if (outputTooLarge)
            {
                return ProcessResult.OutputTooLarge(
                    standardOutput.Content,
                    standardError.Content,
                    TryGetExitCode(process)
                );
            }

            if (invalidOutput)
            {
                return ProcessResult.InvalidOutput(
                    standardOutput.Content,
                    standardError.Content,
                    TryGetExitCode(process)
                );
            }

            if (teeFailed)
            {
                return ProcessResult.InvalidOutput(
                    standardOutput.Content,
                    standardError.Content,
                    TryGetExitCode(process)
                );
            }

            if (triggeringCancellationCause == ProcessCancellationCause.Caller)
            {
                cancellationToken.ThrowIfCancellationRequested();
            }

            return ProcessResult.TimedOut(
                standardOutput.Content,
                standardError.Content,
                TryGetExitCode(process)
            );
        }
        catch
        {
            await KillAndWaitAsync(process, milestones).ConfigureAwait(false);
            throw;
        }

        if (standardOutput.TooLarge || standardError.TooLarge)
        {
            return ProcessResult.OutputTooLarge(
                standardOutput.Content,
                standardError.Content,
                process.ExitCode
            );
        }

        if (standardOutput.InvalidUtf8 || standardError.InvalidUtf8)
        {
            return ProcessResult.InvalidOutput(
                standardOutput.Content,
                standardError.Content,
                process.ExitCode
            );
        }

        if (standardError.TeeFailed)
        {
            return ProcessResult.InvalidOutput(
                standardOutput.Content,
                standardError.Content,
                process.ExitCode
            );
        }

        return new ProcessResult(process.ExitCode, standardOutput.Content, standardError.Content);
    }

    private static async Task WriteAndCloseStandardInputAsync(
        StreamWriter standardInput,
        string? content,
        ProcessMilestoneReporter? milestones,
        CancellationToken cancellationToken
    )
    {
        try
        {
            if (content is not null)
            {
                await standardInput
                    .WriteAsync(content.AsMemory(), cancellationToken)
                    .ConfigureAwait(false);
                await standardInput.FlushAsync(cancellationToken).ConfigureAwait(false);
            }
        }
        finally
        {
            standardInput.Close();
            milestones?.Report(ProcessMilestoneName.StandardInputClosed);
        }
    }

    private static async Task WaitForExitAsync(
        Process process,
        ProcessMilestoneReporter? milestones,
        CancellationToken cancellationToken
    )
    {
        await process.WaitForExitAsync(cancellationToken).ConfigureAwait(false);
        milestones?.Report(ProcessMilestoneName.ProcessExited);
    }

    private static ProcessStartInfo CreateStartInfo(ProcessStartSpec startSpec)
    {
        bool useUnixSessionLauncher = ShouldUseUnixSessionLauncher(startSpec);
        string fileName = useUnixSessionLauncher
            ? UnixSessionLauncherPath!
            : startSpec.FileName;
        var startInfo = new ProcessStartInfo
        {
            FileName = fileName,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            RedirectStandardInput = true,
            StandardOutputEncoding = RedirectedStreamEncoding,
            StandardErrorEncoding = RedirectedStreamEncoding,
            StandardInputEncoding = RedirectedStreamEncoding,
            CreateNoWindow = true,
        };

        if (startSpec.WorkingDirectory is not null)
        {
            startInfo.WorkingDirectory = startSpec.WorkingDirectory;
        }

        if (useUnixSessionLauncher)
        {
            startInfo.ArgumentList.Add("--");
            startInfo.ArgumentList.Add(startSpec.FileName);
        }

        foreach (string argument in startSpec.Arguments)
        {
            startInfo.ArgumentList.Add(argument);
        }

        foreach ((string key, string? value) in startSpec.Environment)
        {
            if (value is null)
            {
                startInfo.Environment.Remove(key);
            }
            else
            {
                startInfo.Environment[key] = value;
            }
        }

        return startInfo;
    }

    private static bool ShouldUseUnixSessionLauncher(ProcessStartSpec startSpec)
    {
        if (UnixSessionLauncherPath is null)
        {
            return false;
        }

        string executable = startSpec.FileName;
        if (executable.Contains(Path.DirectorySeparatorChar))
        {
            string executablePath = Path.IsPathFullyQualified(executable)
                ? executable
                : Path.Combine(
                    startSpec.WorkingDirectory ?? Environment.CurrentDirectory,
                    executable
                );
            return File.Exists(executablePath);
        }

        string? searchPath =
            startSpec.Environment.TryGetValue("PATH", out string? configuredPath)
                ? configuredPath
                : Environment.GetEnvironmentVariable("PATH");
        string workingDirectory = startSpec.WorkingDirectory ?? Environment.CurrentDirectory;
        return searchPath
            ?.Split(Path.PathSeparator)
            .Any(directory =>
                File.Exists(
                    Path.Combine(
                        string.IsNullOrEmpty(directory)
                            ? workingDirectory
                            : Path.IsPathFullyQualified(directory)
                                ? directory
                                : Path.Combine(workingDirectory, directory),
                        executable
                    )
                )
            ) == true;
    }

    private static string? FindUnixSessionLauncher()
    {
        if (OperatingSystem.IsWindows())
        {
            return null;
        }

        foreach (string candidate in new[] { "/usr/bin/setsid", "/bin/setsid" })
        {
            if (File.Exists(candidate))
            {
                return candidate;
            }
        }

        return null;
    }

    private async Task KillAndWaitAsync(
        Process process,
        ProcessMilestoneReporter? milestones
    )
    {
        try
        {
            processCleanup.Kill(process);
            milestones?.Report(ProcessMilestoneName.KillCompleted);
        }
        catch (InvalidOperationException) { }
        catch (NotSupportedException) { }
        catch (Win32Exception) { }

        using var cleanupCancellation = new CancellationTokenSource(processCleanupTimeout);
        try
        {
            await processCleanup
                .WaitForExitAsync(process, cleanupCancellation.Token)
                .ConfigureAwait(false);
            milestones?.Report(ProcessMilestoneName.ProcessExited);
        }
        catch (InvalidOperationException) { }
        catch (NotSupportedException) { }
        catch (Win32Exception) { }
        catch (OperationCanceledException) when (cleanupCancellation.IsCancellationRequested) { }
    }

    private static int? TryGetExitCode(Process process)
    {
        try
        {
            return process.HasExited ? process.ExitCode : null;
        }
        catch (InvalidOperationException)
        {
            return null;
        }
        catch (NotSupportedException)
        {
            return null;
        }
        catch (Win32Exception)
        {
            return null;
        }
    }

    internal interface IProcessCleanupStrategy
    {
        void Kill(Process process);

        Task WaitForExitAsync(Process process, CancellationToken cancellationToken);
    }

    internal interface IProcessStartStrategy
    {
        bool Start(Process process);
    }

    internal enum ProcessMilestoneName
    {
        LaunchRequested,
        ProcessStarted,
        StandardInputClosed,
        ProcessExited,
        StandardOutputEof,
        StandardErrorEof,
        TimeoutInitiated,
        KillCompleted,
        ProcessDisposalCompleted,
    }

    internal readonly record struct ProcessMilestone(
        ProcessMilestoneName Name,
        TimeSpan Elapsed
    );

    private enum ProcessCancellationCause
    {
        None,
        Caller,
        Timeout,
    }

    internal sealed class SystemProcessStartStrategy : IProcessStartStrategy
    {
        internal static SystemProcessStartStrategy Instance { get; } = new();

        private SystemProcessStartStrategy() { }

        public bool Start(Process process) => process.Start();
    }

    internal sealed class SystemProcessCleanupStrategy : IProcessCleanupStrategy
    {
        internal static SystemProcessCleanupStrategy Instance { get; } = new();

        private SystemProcessCleanupStrategy() { }

        public void Kill(Process process)
        {
            if (
                OperatingSystem.IsWindows()
                || UnixSessionLauncherPath is null
                || !string.Equals(
                    process.StartInfo.FileName,
                    UnixSessionLauncherPath,
                    StringComparison.Ordinal
                )
            )
            {
                process.Kill(entireProcessTree: true);
                return;
            }

            const int noSuchProcessError = 3;
            const int sigkill = 9;
            if (UnixNativeMethods.Kill(-process.Id, sigkill) == 0)
            {
                return;
            }

            int error = Marshal.GetLastPInvokeError();
            if (error == noSuchProcessError)
            {
                process.Kill(entireProcessTree: true);
                return;
            }

            throw new Win32Exception(error);
        }

        public Task WaitForExitAsync(Process process, CancellationToken cancellationToken) =>
            process.WaitForExitAsync(cancellationToken);
    }

    private static class UnixNativeMethods
    {
        [DllImport("libc", EntryPoint = "kill", SetLastError = true)]
        internal static extern int Kill(int processId, int signal);
    }

    private sealed class BoundedOutputCapture
    {
        private readonly StringBuilder builder = new();
        private readonly int byteLimit;
        private readonly TextWriter? tee;

        public BoundedOutputCapture(int byteLimit, TextWriter? tee = null)
        {
            this.byteLimit = byteLimit;
            this.tee = tee;
        }

        public string Content => builder.ToString();

        public bool InvalidUtf8 { get; private set; }

        public bool TeeFailed { get; private set; }

        public bool TooLarge { get; private set; }

        public async Task ReadAsync(
            Stream stream,
            CancellationTokenSource executionCancellation,
            ProcessMilestoneReporter? milestones,
            ProcessMilestoneName eofMilestone
        )
        {
            byte[] byteBuffer = ArrayPool<byte>.Shared.Rent(4096);
            char[] charBuffer = ArrayPool<char>.Shared.Rent(
                RedirectedStreamEncoding.GetMaxCharCount(byteBuffer.Length)
            );
            Decoder decoder = RedirectedStreamEncoding.GetDecoder();
            int observedBytes = 0;

            try
            {
                while (true)
                {
                    int bytesRead = await stream
                        .ReadAsync(
                            byteBuffer.AsMemory(0, byteBuffer.Length),
                            executionCancellation.Token
                        )
                        .ConfigureAwait(false);
                    if (bytesRead == 0)
                    {
                        milestones?.Report(eofMilestone);
                        break;
                    }

                    int bytesToCapture = Math.Min(bytesRead, byteLimit - observedBytes);
                    if (bytesToCapture > 0)
                    {
                        int charactersRead;
                        try
                        {
                            charactersRead = decoder.GetChars(
                                byteBuffer,
                                0,
                                bytesToCapture,
                                charBuffer,
                                0,
                                flush: false
                            );
                        }
                        catch (DecoderFallbackException)
                        {
                            InvalidUtf8 = true;
                            executionCancellation.Cancel();
                            return;
                        }

                        observedBytes += bytesToCapture;
                        await AppendAsync(charBuffer, charactersRead, executionCancellation)
                            .ConfigureAwait(false);
                    }

                    if (bytesToCapture < bytesRead)
                    {
                        TooLarge = true;
                        executionCancellation.Cancel();
                        return;
                    }
                }

                try
                {
                    int remainingCharacters = decoder.GetChars(
                        Array.Empty<byte>(),
                        0,
                        0,
                        charBuffer,
                        0,
                        flush: true
                    );
                    await AppendAsync(charBuffer, remainingCharacters, executionCancellation)
                        .ConfigureAwait(false);
                }
                catch (DecoderFallbackException)
                {
                    InvalidUtf8 = true;
                    executionCancellation.Cancel();
                }
            }
            finally
            {
                ArrayPool<byte>.Shared.Return(byteBuffer);
                ArrayPool<char>.Shared.Return(charBuffer);
            }
        }

        private async Task AppendAsync(
            char[] characters,
            int count,
            CancellationTokenSource executionCancellation
        )
        {
            if (count == 0)
            {
                return;
            }

            builder.Append(characters, 0, count);
            if (tee is null)
            {
                return;
            }

            string teePayload = new(characters, 0, count);
            try
            {
                await tee.WriteAsync(teePayload.AsMemory(), executionCancellation.Token)
                    .WaitAsync(executionCancellation.Token)
                    .ConfigureAwait(false);
                await tee.FlushAsync(executionCancellation.Token)
                    .WaitAsync(executionCancellation.Token)
                    .ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (executionCancellation.IsCancellationRequested)
            {
                throw;
            }
            catch
            {
                TeeFailed = true;
                executionCancellation.Cancel();
            }
        }
    }

    private readonly struct ProcessLifetime : IDisposable
    {
        private readonly Process process;
        private readonly ProcessMilestoneReporter? milestones;

        public ProcessLifetime(
            Process process,
            ProcessMilestoneReporter? milestones
        )
        {
            this.process = process;
            this.milestones = milestones;
        }

        public void Dispose()
        {
            process.Dispose();
            milestones?.Report(ProcessMilestoneName.ProcessDisposalCompleted);
        }
    }

    internal sealed class ProcessMilestoneReporter
    {
        private readonly object gate = new();
        private readonly Action<ProcessMilestone> observer;
        private readonly Queue<ProcessMilestone> pendingMilestones = [];
        private readonly HashSet<ProcessMilestoneName> reportedMilestones = [];
        private readonly long startTimestamp = Stopwatch.GetTimestamp();
        private bool observerDrainActive;
        private bool reportingCompleted;

        public ProcessMilestoneReporter(Action<ProcessMilestone> observer)
        {
            ArgumentNullException.ThrowIfNull(observer);
            this.observer = observer;
        }

        public void Report(ProcessMilestoneName name)
        {
            lock (gate)
            {
                if (reportingCompleted || !reportedMilestones.Add(name))
                {
                    return;
                }

                pendingMilestones.Enqueue(
                    new ProcessMilestone(
                        name,
                        Stopwatch.GetElapsedTime(startTimestamp)
                    )
                );
                if (name == ProcessMilestoneName.ProcessDisposalCompleted)
                {
                    reportingCompleted = true;
                }

                if (observerDrainActive)
                {
                    return;
                }

                observerDrainActive = true;
            }

            DrainObserver();
        }

        private void DrainObserver()
        {
            while (true)
            {
                ProcessMilestone milestone;
                lock (gate)
                {
                    if (pendingMilestones.Count == 0)
                    {
                        observerDrainActive = false;
                        return;
                    }

                    milestone = pendingMilestones.Dequeue();
                }

                try
                {
                    observer(milestone);
                }
                catch
                {
                    // Diagnostic observers must never alter process behavior.
                }
            }
        }
    }
}
