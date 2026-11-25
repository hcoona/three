using System.Collections.Concurrent;
using System.Diagnostics;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Data.Sqlite;

namespace LiteAgensh.Tests.Support;

internal sealed class FixtureDiagnosticSink : IDisposable
{
    private const int MaximumRecords = 65_536;
    private const long MaximumBytes = 32 * 1024 * 1024;
    private const int MaximumActivityBytes = 128 * 1024;
    private const int MaximumIncidentBytes = 512 * 1024;
    private static readonly string[] Resources = ["run", "campaign"];
    private static readonly JsonSerializerOptions ProjectionOptions = new(Json.Options)
    {
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Skip,
    };
    private static readonly HashSet<string> ActivityNames =
    [
        "coordinator.run", "coordinator.ownership", "sqlite.open", "sqlite.closed",
        "sqlite.acquisition", "sqlite.statement", "sqlite.read", "sqlite.commit",
        "sqlite.rollback", "sqlite.transaction-dispose", "sqlite.reader-dispose",
    ];
    private static readonly HashSet<string> TagNames =
    [
        "process.pid", "db.system.name", "db.operation.name", "liteagensh.actor",
        "liteagensh.operation", "liteagensh.result.code", "liteagensh.ownership.acquired",
        "error.type", "liteagensh.ownership.hresult", "liteagensh.sqlite.database_id",
        "liteagensh.sqlite.connection_id", "liteagensh.sqlite.transaction_id",
        "liteagensh.sqlite.primary_code", "liteagensh.sqlite.extended_code",
        "liteagensh.sqlite.elapsed_ms", "liteagensh.sqlite.monotonic_timestamp",
        "liteagensh.sqlite.deferred", "liteagensh.sqlite.write_acquisition_observed",
    ];
    private readonly object gate = new();
    private readonly ConcurrentDictionary<ActivitySpanId, Activity> live = new();
    private readonly ActivityTraceId traceId = ActivityTraceId.CreateRandom();
    private readonly ActivitySpanId parentId = ActivitySpanId.CreateRandom();
    private readonly ActivityListener listener;
    private readonly Action<string> report;
    private readonly string participant;
    private readonly string? resultsRoot, testIdentity;
    private readonly long began = Stopwatch.GetTimestamp();
    private readonly long? birthTicks = OwnBirth();
    private FileStream? output;
    private long bytes;
    private int records, dropped, unsupported, exportFailures, collectionFailures;
    private bool disposed, exportUnavailable;

    internal FixtureDiagnosticSink(
        string id,
        string participant,
        string? resultsRoot,
        string? testIdentity = null,
        Action<string>? report = null
    )
    {
        Id = id;
        this.participant = participant;
        this.resultsRoot = resultsRoot;
        this.testIdentity = testIdentity;
        this.report = report ?? Console.WriteLine;
        DirectoryPath = string.IsNullOrWhiteSpace(resultsRoot)
            ? Path.Combine(Path.GetTempPath(), "lite-agensh-evidence-" + id)
            : Path.Combine(resultsRoot, "LiteAgensh.Tests", "diagnostics", id);
        FilePath = Path.Combine(
            DirectoryPath, $"{participant}-{Environment.ProcessId}-{birthTicks}.jsonl"
        );
        try
        {
            Directory.CreateDirectory(DirectoryPath);
            output = new(FilePath, FileMode.CreateNew, FileAccess.Write, FileShare.Read);
        }
        catch (Exception error) when (DiagnosticFailure(error))
        {
            ExportFailed(error);
        }
        listener = new()
        {
            ShouldListenTo = source => source.Name == "LiteAgensh",
            Sample = (ref ActivityCreationOptions<ActivityContext> options) =>
                options.Parent.TraceId == traceId
                    ? ActivitySamplingResult.AllDataAndRecorded
                    : ActivitySamplingResult.None,
            ActivityStarted = activity =>
            {
                if (Owned(activity))
                {
                    if (live.Count < 512)
                    {
                        live.TryAdd(activity.SpanId, activity);
                    }
                    else
                    {
                        Interlocked.Increment(ref dropped);
                    }
                    Observe(activity, "started");
                }
            },
            ActivityStopped = activity =>
            {
                if (Owned(activity))
                {
                    live.TryRemove(activity.SpanId, out _);
                    Observe(activity, "stopped");
                }
            },
        };
        ActivitySource.AddActivityListener(listener);
        Write("collection", new
        {
            state = "started",
            testIdentity,
            traceId = traceId.ToString(),
            source = "controlled-fixture-and-existing-LiteAgensh-activities",
            resultsRootConfigured = !string.IsNullOrWhiteSpace(resultsRoot),
        });
        Report($"FIXTURE_EVIDENCE fixture={Id} participant={participant} file={FilePath}");
    }

    internal string Id { get; }
    internal string DirectoryPath { get; }
    internal string FilePath { get; }

    internal FixtureDiagnosticSink Follow(string role) =>
        new(Id, role, resultsRoot, testIdentity, report);

    internal Activity BeginScope() =>
        new Activity("fixture.scope")
            .SetIdFormat(ActivityIdFormat.W3C)
            .SetParentId(traceId, parentId, ActivityTraceFlags.Recorded)
            .Start();

    internal void Phase(
        string phase,
        string outcome,
        DateTimeOffset? workClock = null,
        Exception? error = null
    ) => Write("phase", new
    {
        phase,
        outcome,
        workClock,
        errorType = error?.GetType().FullName,
        hresult = error?.HResult,
        primaryCode = (error as SqliteException)?.SqliteErrorCode,
        extendedCode = (error as SqliteException)?.SqliteExtendedErrorCode,
    });

    internal void Process(
        string phase, string outcome, int pid, long? birth, int? exitCode = null
    ) => Write("owned-process", new { phase, outcome, pid, birthTicks = birth, exitCode });

    internal void Host(
        string phase, string outcome, string runtimeId, long? leaseId,
        OwnedChildIdentity? child, bool managed, int? exitCode = null, Exception? error = null
    ) => Write("controlled-host", new
    {
        phase,
        outcome,
        runtimeId,
        leaseId,
        kind = managed ? "managed" : "owned-process",
        pid = child?.Pid,
        birthTicks = child?.BirthTicks,
        exitCode,
        errorType = error?.GetType().FullName,
        hresult = error?.HResult,
    });

    internal void Session(
        string phase, string outcome, string runtimeId, string sessionId, string worker,
        DateTimeOffset workClock, Exception? error = null
    ) => Write("controlled-session", new
    {
        phase,
        outcome,
        runtimeId,
        sessionId,
        worker,
        workClock,
        errorType = error?.GetType().FullName,
        hresult = error?.HResult,
    });

    internal void BindRun(string runId, DateTimeOffset workClock) =>
        Write("run", new { runId, workClock, resources = Resources });

    internal void Flush()
    {
        // Start callbacks precede later tags/events. Snapshot live scopes before
        // CHECKPOINT, not after the parent may already have killed the creator.
        foreach (Activity activity in live.Values)
        {
            Observe(activity, "live-snapshot");
        }
        Status();
    }

    internal void CaptureSqlite(IEnumerable<(string Resource, string Directory)> directories)
    {
        foreach ((string resource, string directory) in directories)
        {
            int captured = 0, unavailable = 0;
            for (int slot = 0; slot < 32; slot++)
            {
                Capture<Snapshot>("activity", slot, MaximumActivityBytes);
            }
            for (int slot = 0; slot < 8; slot++)
            {
                Capture<Incident>("incident", slot, MaximumIncidentBytes);
            }
            Write("sqlite-coverage", new
            {
                resource,
                availableFiles = captured,
                unavailableFiles = unavailable,
                state = captured != 0 ? "partial-application-observations"
                    : unavailable != 0 ? "unavailable" : "absent",
                externalOrUninstrumentedHolder = "unknown",
            });

            void Capture<T>(string kind, int slot, int limit)
            {
                string path = Path.Combine(directory, $"{kind}-{slot:D2}.json");
                try
                {
                    using FileStream input = new(
                        path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete
                    );
                    byte[] buffer = new byte[limit];
                    int count = input.ReadAtLeast(buffer, limit, throwOnEndOfStream: false);
                    if (input.ReadByte() != -1)
                    {
                        unavailable++;
                        Write("sqlite-source", new { resource, kind, slot, state = "truncated" });
                        return;
                    }
                    using JsonDocument document = JsonDocument.Parse(buffer.AsMemory(0, count));
                    T? data = document.RootElement.Deserialize<T>(ProjectionOptions);
                    bool available = data switch
                    {
                        Snapshot
                        {
                            Version: 1, Database: { Length: > 0 }, Kind: "run" or "campaign",
                            ProcessId: > 0, Active: not null, Recent: not null,
                        } => true,
                        Incident
                        {
                            Version: 1, Database: { Length: > 0 }, ProcessId: > 0,
                            Failure: not null, Sources: not null,
                        } => true,
                        _ => false,
                    };
                    bool warningPresent = HasWarning(document.RootElement);
                    if (document.RootElement.TryGetProperty("sources", out JsonElement sources)
                        && sources.ValueKind == JsonValueKind.Array)
                    {
                        warningPresent |= sources.EnumerateArray().Any(source =>
                            source.TryGetProperty("snapshot", out JsonElement snapshot)
                            && snapshot.ValueKind == JsonValueKind.Object && HasWarning(snapshot)
                        );
                    }
                    if (!available)
                    {
                        unavailable++;
                    }
                    else
                    {
                        captured++;
                    }
                    Write("sqlite-source", new
                    {
                        resource,
                        kind,
                        slot,
                        state = available ? "captured" : "unavailable",
                        warningPresent,
                        stale = available && data is Snapshot activity
                            ? (bool?)(DateTimeOffset.UtcNow - activity.ObservedAtUtc
                                > TimeSpan.FromSeconds(30)) : null,
                        data,
                    });
                }
                catch (Exception error) when (
                    error is FileNotFoundException or DirectoryNotFoundException
                )
                { }
                catch (Exception error) when (DiagnosticFailure(error))
                {
                    unavailable++;
                    Interlocked.Increment(ref collectionFailures);
                    Write("sqlite-source", new
                    {
                        resource,
                        kind,
                        slot,
                        state = "unavailable-or-malformed",
                        errorType = error.GetType().FullName,
                        hresult = error.HResult,
                    });
                }
            }
        }
    }

    private static bool HasWarning(JsonElement value) =>
        value.TryGetProperty("warning", out JsonElement warning)
        && warning.ValueKind is not (JsonValueKind.Null or JsonValueKind.Undefined);

    private bool Owned(Activity activity) =>
        !disposed && activity.Source.Name == "LiteAgensh" && activity.TraceId == traceId;

    private void Observe(Activity activity, string state)
    {
        if (!ActivityNames.Contains(activity.OperationName))
        {
            Interlocked.Increment(ref unsupported);
            return;
        }
        try
        {
            ActivityEvent[] events = activity.Events
                .Where(item => EventName(item.Name)).Take(33).ToArray();
            Write("activity", new
            {
                state,
                operation = activity.OperationName,
                traceId = activity.TraceId.ToString(),
                spanId = activity.SpanId.ToString(),
                parentSpanId = activity.ParentSpanId.ToString(),
                startedAtUtc = activity.StartTimeUtc,
                durationMilliseconds = state == "stopped"
                    ? (double?)activity.Duration.TotalMilliseconds : null,
                status = activity.Status.ToString(),
                tags = Tags(activity.TagObjects),
                events = events.Take(32).Select(item => new
                {
                    phase = item.Name,
                    observedAtUtc = item.Timestamp,
                    tags = Tags(item.Tags),
                }).ToArray(),
                eventsTruncated = events.Length > 32,
                excludedEvents = activity.Events.Count(item => !EventName(item.Name)),
            });
        }
        catch (Exception error) when (DiagnosticFailure(error))
        {
            if (Interlocked.Increment(ref collectionFailures) == 1)
            {
                Report(
                    $"FIXTURE_EVIDENCE_COLLECTION_UNAVAILABLE fixture={Id} "
                    + $"type={error.GetType().FullName} hresult={error.HResult}"
                );
            }
        }
    }

    private static Dictionary<string, object?> Tags(
        IEnumerable<KeyValuePair<string, object?>> tags
    ) =>
        tags.Where(item =>
            TagNames.Contains(item.Key)
            && item.Value is null or string or bool or int or long or double or decimal
        ).ToDictionary(item => item.Key, item => item.Value, StringComparer.Ordinal);

    private static bool EventName(string name) => name is
        "coordinator.ownership.acquired" or "coordinator.ownership.failed"
        or "open-requested" or "open-succeeded" or "open-failed" or "closed"
        or "acquisition-requested" or "acquisition-succeeded" or "acquisition-failed"
        or "commit-requested" or "commit-succeeded" or "commit-failed"
        or "rollback-requested" or "rollback-succeeded" or "rollback-failed"
        or "transaction-dispose-requested" or "transaction-dispose-succeeded"
        or "transaction-dispose-failed" or "statement-requested" or "statement-succeeded"
        or "statement-failed" or "read-requested" or "read-succeeded" or "read-failed"
        or "reader-dispose-requested" or "reader-dispose-succeeded" or "reader-dispose-failed";

    private void Write(string kind, object detail)
    {
        lock (gate)
        {
            if (disposed || output is null || exportUnavailable)
            {
                return;
            }
            try
            {
                byte[] record = JsonSerializer.SerializeToUtf8Bytes(new
                {
                    version = 1,
                    fixture = Id,
                    participant,
                    processId = Environment.ProcessId,
                    birthTicks,
                    sequence = records + 1,
                    observedAtUtc = DateTimeOffset.UtcNow,
                    elapsedMilliseconds = Stopwatch.GetElapsedTime(began).TotalMilliseconds,
                    kind,
                    detail,
                }, Json.Options);
                if (records >= MaximumRecords || bytes + record.Length + 1 > MaximumBytes)
                {
                    dropped++;
                    if (dropped == 1)
                    {
                        Status();
                    }
                    return;
                }
                output.Write(record);
                output.WriteByte((byte)'\n');
                output.Flush();
                bytes += record.Length + 1;
                records++;
            }
            catch (Exception error) when (DiagnosticFailure(error))
            {
                exportUnavailable = true;
                ExportFailed(error);
            }
        }
    }

    private void Status()
    {
        lock (gate)
        {
            try
            {
                File.WriteAllBytes(
                    Path.ChangeExtension(FilePath, ".status.json"),
                    JsonSerializer.SerializeToUtf8Bytes(new
                    {
                        version = 1,
                        fixture = Id,
                        participant,
                        processId = Environment.ProcessId,
                        birthTicks,
                        records,
                        bytes,
                        dropped,
                        unsupportedActivities = unsupported,
                        exportFailures,
                        collectionFailures,
                        liveActivities = live.Count,
                        disposed,
                        observedAtUtc = DateTimeOffset.UtcNow,
                    }, Json.Options)
                );
            }
            catch (Exception error) when (DiagnosticFailure(error))
            {
                ExportFailed(error);
            }
        }
    }

    private void ExportFailed(Exception error)
    {
        if (Interlocked.Increment(ref exportFailures) == 1)
        {
            Report(
                $"FIXTURE_EVIDENCE_UNAVAILABLE fixture={Id} "
                + $"type={error.GetType().FullName} hresult={error.HResult}"
            );
        }
    }

    private void Report(string message)
    {
        try
        {
            report(message);
        }
        catch (Exception error) when (DiagnosticFailure(error))
        {
            Interlocked.Increment(ref exportFailures);
        }
    }

    private static bool DiagnosticFailure(Exception error) =>
        error is IOException or UnauthorizedAccessException or JsonException
            or InvalidOperationException or ArgumentException or NotSupportedException;

    private static long? OwnBirth()
    {
        try
        {
            using Process process = System.Diagnostics.Process.GetCurrentProcess();
            return process.StartTime.ToUniversalTime().Ticks;
        }
        catch (Exception error) when (
            error is System.ComponentModel.Win32Exception or InvalidOperationException
        )
        {
            return null;
        }
    }

    public void Dispose()
    {
        if (disposed)
        {
            return;
        }
        Flush();
        listener.Dispose();
        lock (gate)
        {
            disposed = true;
            try
            {
                output?.Dispose();
            }
            catch (Exception error) when (DiagnosticFailure(error))
            {
                ExportFailed(error);
            }
            Status();
        }
    }

    private sealed record SqliteActivity(
        string? Connection, string? Transaction, string? Actor, string? Operation,
        string? Boundary, string? Phase, DateTimeOffset ObservedAtUtc,
        long MonotonicTimestamp, double ElapsedMilliseconds, bool? Deferred,
        bool WriteAcquisitionObserved, int? PrimaryCode, int? ExtendedCode
    );

    private sealed record Snapshot(
        int Version, string? Database, string? Kind, int ProcessId, DateTimeOffset? ProcessBirthUtc,
        DateTimeOffset ObservedAtUtc, bool Truncated, SqliteActivity[]? Active,
        SqliteActivity[]? Recent
    );

    private sealed record Source(int Slot, string? Status, Snapshot? Snapshot);

    private sealed record Incident(
        int Version, string? Database, int ProcessId, DateTimeOffset? ProcessBirthUtc,
        SqliteActivity? Failure, DateTimeOffset CollectionStartedAtUtc,
        DateTimeOffset CollectionCompletedAtUtc, double CollectionElapsedMilliseconds,
        Source[]? Sources
    );
}
