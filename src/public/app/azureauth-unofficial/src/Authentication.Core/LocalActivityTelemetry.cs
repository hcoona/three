using System.Collections.Concurrent;
using System.Diagnostics;
using System.Text.Json;

namespace Authentication.Core;

// A local collector for owned instrumentation, not an OTLP or network exporter.
// Producers enqueue bounded, allowlisted bytes; only the background writer does I/O.
public sealed class LocalActivityTelemetry : IDisposable
{
    // Limit is the total for one invocation. The final line keeps FinalReserve and
    // the owned request span's stop event keeps RequestStopReserve, so phase events
    // and the notice cannot exhaust either, in whichever order the two arrive.
    private const int Limit = 8192, FinalReserve = 1024, RequestStopReserve = 512;
    private readonly object gate = new();
    private readonly ConcurrentQueue<byte[]> pending = new();
    private readonly AutoResetEvent wake = new(false);
    private readonly TaskCompletionSource finished = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly ActivityListener listener;
    private readonly long entryTimestamp;
    private int ordinary, requestStop, final;
    private bool closed;

    // This means local writer completion only, never application/process closure.
    public Task Completion => finished.Task;

    public LocalActivityTelemetry(long entryTimestamp, Func<byte[], bool> write)
    {
        ArgumentNullException.ThrowIfNull(write);
        this.entryTimestamp = entryTimestamp;
        // Initialize before registration: a source constructor can notify listeners
        // before its static field has received the constructed instance.
        AuthenticationTrace.InitializeSource();
        listener = new ActivityListener
        {
            ShouldListenTo = AuthenticationTrace.Owns,
            Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllDataAndRecorded,
            ActivityStarted = activity => Observe(activity, started: true),
            ActivityStopped = activity => Observe(activity, started: false),
        };
        try
        {
            new Thread(() => Drain(write)) { IsBackground = true }.Start();
            ActivitySource.AddActivityListener(listener);
        }
        catch (Exception)
        {
            Dispose();
            throw;
        }
    }

    private void Observe(Activity activity, bool started)
    {
        try
        {
            if (!AuthenticationTrace.Owns(activity.Source)
                || !AuthenticationTrace.KnownStage(activity.OperationName)) return;
            using var buffer = new MemoryStream();
            using (var writer = new Utf8JsonWriter(buffer))
            {
                writer.WriteStartObject();
                writer.WriteString("event", started ? "stage_started" : "stage_completed");
                writer.WriteString("stage", activity.OperationName);
                writer.WriteString("traceId", activity.TraceId.ToHexString());
                writer.WriteString("spanId", activity.SpanId.ToHexString());
                writer.WriteString("parentSpanId", activity.ParentSpanId.ToHexString());
                writer.WriteNumber("elapsedMilliseconds", BoundedMilliseconds(
                    TimeProvider.System.GetElapsedTime(entryTimestamp).TotalMilliseconds));
                if (!started)
                {
                    writer.WriteNumber("durationMilliseconds", BoundedMilliseconds(activity.Duration.TotalMilliseconds));
                    writer.WriteString("status", activity.Status switch
                    {
                        ActivityStatusCode.Ok => "ok", ActivityStatusCode.Error => "error", _ => "unset",
                    });
                    if (activity.OperationName == "request" && activity.Status == ActivityStatusCode.Error
                        && AuthenticationTrace.CommittedErrorType(activity) is { } errorType)
                        writer.WriteString("error.type", errorType);
                }
                writer.WriteEndObject();
                writer.Flush();
            }
            buffer.WriteByte((byte)'\n');
            _ = Enqueue(buffer.ToArray(), !started && activity.OperationName == "request"
                ? Allocation.RequestStop : Allocation.Ordinary);
        }
        catch (Exception) { }
    }

    private static long BoundedMilliseconds(double value) =>
        double.IsFinite(value) ? (long)Math.Clamp(value, 0, 601000) : 0;

    // Only the host's fixed indication/allowlisted completion projection belongs here.
    // Leave room for it even when phase observations exhaust their allocation.
    public bool TryWriteFinal(byte[] bytes) => Enqueue(bytes, Allocation.Final);

    // The host's one bounded pre-interaction notice shares the ordinary allocation.
    public bool TryWriteNotice(byte[] bytes) => Enqueue(bytes, Allocation.Ordinary);

    private enum Allocation { Ordinary, RequestStop, Final }

    private bool Enqueue(byte[] bytes, Allocation allocation)
    {
        try
        {
            lock (gate)
            {
                if (closed || bytes.Length == 0) return false;
                var admitted = ordinary + requestStop + final + bytes.Length <= Limit && allocation switch
                {
                    Allocation.Ordinary => ordinary + bytes.Length <= Limit - FinalReserve - RequestStopReserve,
                    Allocation.RequestStop => requestStop == 0
                        && ordinary + bytes.Length <= Limit - FinalReserve,
                    _ => final == 0 && (requestStop != 0 || ordinary + bytes.Length <= Limit - RequestStopReserve),
                };
                if (!admitted) return false;
                pending.Enqueue(bytes.ToArray());
                if (allocation == Allocation.Ordinary) ordinary += bytes.Length;
                else if (allocation == Allocation.RequestStop) requestStop = bytes.Length;
                else final = bytes.Length;
                wake.Set();
                return true;
            }
        }
        catch (Exception) { return false; }
    }

    private void Drain(Func<byte[], bool> write)
    {
        try
        {
            while (true)
            {
                while (pending.TryDequeue(out var bytes))
                    if (!write(bytes)) return;
                lock (gate)
                    if (closed && pending.IsEmpty) return;
                wake.WaitOne();
            }
        }
        catch (Exception) { }
        finally
        {
            lock (gate)
            {
                closed = true;
                pending.Clear();
                wake.Dispose();
            }
            finished.TrySetResult();
        }
    }

    public void Dispose()
    {
        try { listener.Dispose(); }
        catch (Exception) { }
        lock (gate)
        {
            if (closed) return;
            closed = true;
            try { wake.Set(); }
            catch (Exception) { }
        }
        // Never join the writer or wait for a flush on an authentication thread. The
        // process host may wait for Completion within its remaining shutdown bound.
    }
}
