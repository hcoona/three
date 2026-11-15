using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.Build.Evaluation;
using Microsoft.Build.Execution;
using Microsoft.Build.Framework;
using Microsoft.Build.Logging;
using WorkflowDelivery.Repository;

namespace WorkflowDelivery.CI;

internal sealed record DotNetTestEntry(DotNetNodeIdentity Identity, string TestTarget);
internal sealed record DotNetTestRequest(DotNetTestEntry[] Entries, string Destination);
internal sealed record DotNetTestEntryResult(DotNetNodeIdentity Identity, string TestTarget,
    string Overall, string Build, string GetTargetPath, string Test, DotNetBuildOutput[] Outputs,
    string Log);

// Loaded by the checkout's SDK. One native session owns node scheduling, concurrency and
// shared reference configurations; each selected entry remains its own build submission.
public sealed class DotNetTestTask : ITask
{
    private const int LogLimit = 4 * 1024 * 1024;

    public IBuildEngine BuildEngine { get; set; } = null!;
    public ITaskHost HostObject { get; set; } = null!;
    [Required]
    public string RequestFile { get; set; } = "";

    internal static string Receipt(string destination, int index) =>
        Path.Combine(destination, "entry-" + index + ".json");

    public bool Execute()
    {
        string requestText = File.ReadAllText(RequestFile);
        DotNetTestRequest request = JsonSerializer.Deserialize(requestText,
            TransferJson.Default.DotNetTestRequest)
            ?? throw new InvalidDataException("Missing native test request.");
        Directory.CreateDirectory(request.Destination);
        using var collection = new ProjectCollection();
        var logs = new SubmissionLogs(request, requestText);
        var failures = new List<Exception>();
        using var manager = new BuildManager();
        manager.BeginBuild(new BuildParameters(collection)
        {
            DisableInProcNode = true,
            EnableNodeReuse = false,
            MaxNodeCount = Environment.ProcessorCount,
            Loggers = [new ConsoleLogger(LoggerVerbosity.Minimal), logs],
        });
        try
        {
            BuildSubmission[] submissions = request.Entries.Select(entry =>
                manager.PendBuildRequest(new BuildRequestData(entry.Identity.Project,
                    entry.Identity.Globals.ToDictionary(pair => pair.Key,
                        pair => (string?)pair.Value, StringComparer.OrdinalIgnoreCase), null,
                    ["Build", "GetTargetPath", entry.TestTarget], null))).ToArray();
            logs.Bind(submissions.Select(submission => submission.SubmissionId));
            using var pending = new CountdownEvent(submissions.Length);
            for (int index = 0; index < submissions.Length; index++)
            {
                logs.Progress(submissions[index].SubmissionId).Mark("prepared");
                submissions[index].ExecuteAsync(submission =>
                {
                    logs.Progress(submission.SubmissionId).Mark("callbackObserved");
                    // A durable receipt per completed submission keeps it independent of a
                    // later interruption or failure elsewhere in this native session.
                    try
                    {
                        int entry = (int)submission.AsyncContext!;
                        WriteReceipt(Receipt(request.Destination, entry), Project(
                            request.Entries[entry], submission.BuildResult,
                            logs.Text(submission.SubmissionId)));
                    }
                    catch (Exception exception) when (exception is IOException or
                        UnauthorizedAccessException or InvalidOperationException)
                    {
                        lock (failures) failures.Add(exception);
                    }
                    finally { pending.Signal(); }
                }, index);
                logs.Progress(submissions[index].SubmissionId).Mark("executeAsyncReturned");
            }
            pending.Wait();
        }
        finally
        {
            manager.EndBuild();
        }
        if (failures.Count != 0)
            throw new AggregateException("Native test receipts could not be written.", failures);
        return true;
    }

    private static DotNetTestEntryResult Project(DotNetTestEntry entry, BuildResult? result,
        string log)
    {
        TargetResult? build = null;
        TargetResult? path = null;
        TargetResult? test = null;
        result?.ResultsByTarget?.TryGetValue("Build", out build);
        result?.ResultsByTarget?.TryGetValue("GetTargetPath", out path);
        result?.ResultsByTarget?.TryGetValue(entry.TestTarget, out test);
        if (result?.Exception is { } exception)
            log += "Native submission exception: " + exception.Message + Environment.NewLine;
        // The overall code includes errors that native logging attributed to this submission.
        return new(entry.Identity, entry.TestTarget,
            result?.OverallResult.ToString() ?? "Missing", Code(build), Code(path), Code(test),
            path?.Items.Select(item => new DotNetBuildOutput(item.ItemSpec,
                item.GetMetadata("FullPath"))).ToArray() ?? [], log);

        static string Code(TargetResult? target) =>
            target?.ResultCode.ToString() ?? "Missing";
    }

    private static void WriteReceipt(string receipt, DotNetTestEntryResult result)
    {
        string partial = receipt + ".partial";
        using (var output = new FileStream(partial, FileMode.CreateNew, FileAccess.Write,
                   FileShare.None))
            JsonSerializer.Serialize(output, result, TransferJson.Default.DotNetTestEntryResult);
        File.Move(partial, receipt);
    }

    // Retains each submission's diagnostics by its native submission identity.
    private sealed class SubmissionLogs(DotNetTestRequest request, string requestText) : ILogger
    {
        private readonly Dictionary<int, StringBuilder> logs = [];
        private readonly Dictionary<int, SubmissionProgress> progress = [];
        private readonly string fingerprint =
            Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(requestText)));

        public LoggerVerbosity Verbosity { get; set; } = LoggerVerbosity.Minimal;
        public string? Parameters { get; set; }

        public void Initialize(IEventSource eventSource) =>
            eventSource.AnyEventRaised += (_, args) => Append(args);

        public void Shutdown() { }

        internal void Bind(IEnumerable<int> submissions)
        {
            lock (logs)
            {
                int index = 0;
                foreach (int submission in submissions)
                {
                    logs.Add(submission, new());
                    progress.Add(submission, new(request, fingerprint, index++, submission));
                }
            }
        }

        internal SubmissionProgress Progress(int submission)
        {
            lock (logs) return progress[submission];
        }

        internal string Text(int submission)
        {
            lock (logs) return logs[submission].ToString();
        }

        private void Append(BuildEventArgs args)
        {
            if (args.BuildEventContext is { } nativeContext)
            {
                SubmissionProgress? observer;
                lock (logs) progress.TryGetValue(nativeContext.SubmissionId, out observer);
                observer?.Observe(args, nativeContext);
            }
            string? line = args switch
            {
                BuildErrorEventArgs error => Diagnostic(error.File, error.LineNumber,
                    error.ColumnNumber, "error", error.Code, error.Message, error.ProjectFile),
                BuildWarningEventArgs warning => Diagnostic(warning.File, warning.LineNumber,
                    warning.ColumnNumber, "warning", warning.Code, warning.Message,
                    warning.ProjectFile),
                BuildMessageEventArgs { Importance: MessageImportance.High } message =>
                    message.Message,
                _ => null,
            };
            if (line is null || args.BuildEventContext is not { } context) return;
            lock (logs)
            {
                if (!logs.TryGetValue(context.SubmissionId, out StringBuilder? log) ||
                    log.Length >= LogLimit) return;
                log.AppendLine(line.Length <= LogLimit - log.Length ? line
                    : line[..(LogLimit - log.Length)] + " [native log limit reached]");
            }
        }

        private static string Diagnostic(string? file, int line, int column, string kind,
            string? code, string? message, string? project) =>
            (string.IsNullOrEmpty(file) ? "" : file + "(" + line + "," + column + "): ") +
            kind + (string.IsNullOrEmpty(code) ? "" : " " + code) + ": " + message +
            (string.IsNullOrEmpty(project) ? "" : " [" + project + "]");
    }

    // Optional first observations, never a receipt or proof that an unobserved phase ran.
    private sealed class SubmissionProgress(DotNetTestRequest request, string fingerprint,
        int index, int submission)
    {
        private const int SnapshotLimit = 8192;
        private readonly Dictionary<string, DateTimeOffset> observed = [];
        private readonly string project = request.Entries[index].Identity.Project;
        private readonly string testTarget = request.Entries[index].TestTarget;
        private readonly string directory = Path.Combine(request.Destination, "progress");
        private BuildEventContext? root;
        private bool unavailable;
        private bool ownsSnapshot;

        internal void Observe(BuildEventArgs args, BuildEventContext context)
        {
            lock (observed)
            {
                if (unavailable) return;
                if (root is null && args is ProjectStartedEventArgs started &&
                    (started.ParentProjectBuildEventContext is null ||
                     started.ParentProjectBuildEventContext.ProjectContextId ==
                        BuildEventContext.InvalidProjectContextId) &&
                    context.ProjectContextId != BuildEventContext.InvalidProjectContextId &&
                    string.Equals(started.ProjectFile, project, OperatingSystem.IsWindows()
                        ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal))
                {
                    root = context;
                    Mark("ownedRootProjectStarted");
                }
                if (root is null || context.NodeId != root.NodeId ||
                    context.ProjectContextId != root.ProjectContextId) return;
                string? target = args switch
                {
                    TargetStartedEventArgs targetStarted => targetStarted.TargetName,
                    TargetFinishedEventArgs targetFinished => targetFinished.TargetName,
                    _ => null,
                };
                string? slot = target == "Build" ? "build" :
                    target == "GetTargetPath" ? "getTargetPath" :
                    target == testTarget ? "test" : null;
                if (slot is not null)
                    Mark(slot + (args is TargetStartedEventArgs ? "Started" : "Finished"));
            }
        }

        internal void Mark(string fact)
        {
            lock (observed)
            {
                if (unavailable || observed.ContainsKey(fact) || observed.Count == 10) return;
                observed.Add(fact, DateTimeOffset.UtcNow);
                string? partial = null;
                bool ownsPartial = false;
                try
                {
                    using var bytes = new MemoryStream();
                    using (var json = new Utf8JsonWriter(bytes))
                    {
                        json.WriteStartObject();
                        json.WriteNumber("schema", 1);
                        json.WriteString("requestTextSha256", fingerprint);
                        json.WriteNumber("entryIndex", index);
                        json.WriteNumber("submissionId", submission);
                        if (root is not null)
                        {
                            json.WriteStartObject("rootContext");
                            json.WriteNumber("nodeId", root.NodeId);
                            json.WriteNumber("projectContextId", root.ProjectContextId);
                            json.WriteEndObject();
                        }
                        json.WriteStartObject("observed");
                        foreach ((string name, DateTimeOffset time) in observed)
                            json.WriteString(name, time);
                        json.WriteEndObject();
                        json.WriteEndObject();
                    }
                    if (bytes.Length > SnapshotLimit)
                        throw new InvalidOperationException("Native phase snapshot limit.");
                    Directory.CreateDirectory(directory);
                    string final = Receipt(directory, index);
                    partial = final + ".partial";
                    using (var output = new FileStream(partial, FileMode.CreateNew,
                               FileAccess.Write, FileShare.None))
                    {
                        ownsPartial = true;
                        bytes.Position = 0;
                        bytes.CopyTo(output);
                    }
                    File.Move(partial, final, overwrite: ownsSnapshot);
                    ownsSnapshot = true;
                }
                catch (Exception exception) when (OptionalFailure(exception))
                {
                    unavailable = true;
                    try
                    {
                        if (ownsPartial) File.Delete(partial!);
                    }
                    catch (Exception cleanup) when (OptionalFailure(cleanup)) { }
                    try
                    {
                        Console.Error.WriteLine(
                            $"Native phase diagnostics unavailable for entry {index}.");
                    }
                    catch (Exception warning) when (OptionalFailure(warning)) { }
                }
            }
        }

        private static bool OptionalFailure(Exception exception) => exception is IOException or
            UnauthorizedAccessException or InvalidOperationException or JsonException or
            ArgumentException or NotSupportedException;
    }
}
