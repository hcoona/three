using System.Text.Json.Nodes;
using LiteAgensh;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace LiteAgensh.Tests;

internal sealed class ScenarioClock : TimeProvider
{
    private DateTimeOffset now = new(2026, 11, 3, 12, 0, 0, TimeSpan.Zero);

    public override DateTimeOffset GetUtcNow() => now;

    public void Advance(TimeSpan interval) => now += interval;
}

internal sealed class DeadlineClock : TimeProvider
{
    private readonly Lock gate = new();
    private readonly ScenarioClock clock = new();
    private readonly List<DeadlineTimer> timers = [];

    public override DateTimeOffset GetUtcNow()
    {
        lock (gate)
        {
            return clock.GetUtcNow();
        }
    }

    public override ITimer CreateTimer(
        TimerCallback callback, object? state, TimeSpan dueTime, TimeSpan period
    )
    {
        // Polling stays live while the test explicitly advances the long deadline timers.
        if (dueTime < TimeSpan.FromSeconds(30))
        {
            return System.CreateTimer(callback, state, dueTime, period);
        }
        lock (gate)
        {
            DeadlineTimer timer = new(this, callback, state);
            timers.Add(timer);
            timer.Change(dueTime, period);
            return timer;
        }
    }

    public void Advance(TimeSpan interval)
    {
        DeadlineTimer[] ready;
        lock (gate)
        {
            clock.Advance(interval);
            ready = timers.Where(timer => timer.Due <= clock.GetUtcNow()).ToArray();
            foreach (DeadlineTimer timer in ready)
            {
                timer.Due = DateTimeOffset.MaxValue;
            }
        }
        foreach (DeadlineTimer timer in ready)
        {
            timer.Invoke();
        }
    }

    private sealed class DeadlineTimer(
        DeadlineClock owner, TimerCallback callback, object? state
    ) : ITimer
    {
        public DateTimeOffset Due { get; set; }

        public void Invoke() => callback(state);

        public bool Change(TimeSpan dueTime, TimeSpan period)
        {
            Assert.AreEqual(Timeout.InfiniteTimeSpan, period);
            lock (owner.gate)
            {
                Due = dueTime == Timeout.InfiniteTimeSpan
                    ? DateTimeOffset.MaxValue : owner.clock.GetUtcNow() + dueTime;
                return owner.timers.Contains(this);
            }
        }

        public void Dispose()
        {
            lock (owner.gate)
            {
                owner.timers.Remove(this);
            }
        }

        public ValueTask DisposeAsync()
        {
            Dispose();
            return ValueTask.CompletedTask;
        }
    }
}

internal static class Scenario
{
    public static string DotnetHost =>
        Path.GetFullPath(
            Path.Combine(
                System.Runtime.InteropServices.RuntimeEnvironment.GetRuntimeDirectory(),
                "..",
                "..",
                "..",
                "dotnet.exe"
            )
        );

    public static JsonObject Data(JsonObject result)
    {
        Assert.IsTrue(result["ok"]!.GetValue<bool>(), result.ToJsonString());
        Assert.AreEqual("ok", result["code"]!.GetValue<string>());
        return result["data"]!.AsObject();
    }

    public static void Failure(JsonObject result, string code)
    {
        Assert.IsFalse(result["ok"]!.GetValue<bool>(), result.ToJsonString());
        Assert.AreEqual(code, result["code"]!.GetValue<string>(), result.ToJsonString());
    }

    public static JsonObject Call(
        CollaborationOperations operations,
        string actor,
        string name,
        object arguments
    ) => Data(operations.Invoke(actor, name, Json.SerializePayload(arguments)));

    public static JsonObject[] Items(JsonObject data) =>
        data["items"]!.AsArray().OfType<JsonObject>().ToArray();

    public static long Id(JsonObject data) => data["id"]!.GetValue<long>();
}
