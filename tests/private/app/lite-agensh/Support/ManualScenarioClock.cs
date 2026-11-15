namespace LiteAgensh.Tests.Support;

internal sealed class ManualScenarioClock : TimeProvider
{
    private readonly object gate = new();
    private readonly List<ScenarioTimer> timers = [];
    private readonly DateTimeOffset origin = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);
    private long ticks;

    public override long TimestampFrequency => TimeSpan.TicksPerSecond;

    public override DateTimeOffset GetUtcNow()
    {
        lock (gate)
        {
            return origin.AddTicks(ticks);
        }
    }

    public override long GetTimestamp()
    {
        lock (gate)
        {
            return ticks;
        }
    }

    public override ITimer CreateTimer(
        TimerCallback callback, object? state, TimeSpan dueTime, TimeSpan period
    )
    {
        ArgumentNullException.ThrowIfNull(callback);
        lock (gate)
        {
            ScenarioTimer timer = new(this, callback, state);
            timer.Change(dueTime, period);
            timers.Add(timer);
            return timer;
        }
    }

    internal void Advance(TimeSpan amount)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(amount, TimeSpan.Zero);
        long target;
        lock (gate)
        {
            target = checked(ticks + amount.Ticks);
        }

        while (true)
        {
            ScenarioTimer? next;
            lock (gate)
            {
                next = timers
                    .Where(timer => timer.Due != long.MaxValue && timer.Due <= target)
                    .MinBy(timer => timer.Due);
                if (next is null)
                {
                    ticks = Math.Max(ticks, target);
                    return;
                }

                ticks = Math.Max(ticks, next.Due);
                next.Due = next.Period > 0
                    ? checked(ticks + next.Period)
                    : long.MaxValue;
            }

            next.Callback(next.State);
        }
    }

    private sealed class ScenarioTimer(
        ManualScenarioClock owner, TimerCallback callback, object? state
    ) : ITimer
    {
        private bool disposed;

        internal TimerCallback Callback { get; } = callback;

        internal object? State { get; } = state;

        internal long Due { get; set; } = long.MaxValue;

        internal long Period { get; private set; }

        public bool Change(TimeSpan dueTime, TimeSpan period)
        {
            Validate(dueTime);
            Validate(period);
            lock (owner.gate)
            {
                if (disposed)
                {
                    return false;
                }

                Due = dueTime == Timeout.InfiniteTimeSpan
                    ? long.MaxValue
                    : checked(owner.ticks + dueTime.Ticks);
                Period = period == Timeout.InfiniteTimeSpan ? 0 : period.Ticks;
                return true;
            }
        }

        public void Dispose()
        {
            lock (owner.gate)
            {
                disposed = true;
                Due = long.MaxValue;
                owner.timers.Remove(this);
            }
        }

        public ValueTask DisposeAsync()
        {
            Dispose();
            return ValueTask.CompletedTask;
        }

        private static void Validate(TimeSpan value)
        {
            if (value < TimeSpan.Zero && value != Timeout.InfiniteTimeSpan)
            {
                throw new ArgumentOutOfRangeException(nameof(value));
            }
        }
    }
}
