using System.Text.Json.Nodes;

namespace LiteAgensh.Tests.Support;

// Observe the supported timer surface; ManualScenarioClock alone schedules and
// invokes callbacks. Observation lifetime belongs to the test's public run.
internal sealed class Phase7Clock : TimeProvider
{
    private readonly ManualScenarioClock clock = new();
    private readonly object gate = new();
    private readonly List<RegisteredTimer> timers = [];
    private TaskCompletionSource changed = Signal();
    private long nextObservation,
        observation,
        nextTimer;
    private readonly TimeSpan epochOffset;

    internal Phase7Clock(DateTimeOffset? epoch = null)
    {
        epochOffset = epoch is { } value ? value - clock.GetUtcNow() : TimeSpan.Zero;
    }

    internal DateTimeOffset? WorkDeadline { get; set; }

    public override DateTimeOffset GetUtcNow() => clock.GetUtcNow() + epochOffset;

    public override long GetTimestamp() => clock.GetTimestamp();

    public override long TimestampFrequency => clock.TimestampFrequency;

    internal IDisposable BeginObservation()
    {
        lock (gate)
        {
            observation = ++nextObservation;
            timers.Clear();
            Pulse();
            return new Observation(this, observation);
        }
    }

    public override ITimer CreateTimer(
        TimerCallback callback,
        object? state,
        TimeSpan dueTime,
        TimeSpan period
    )
    {
        lock (gate)
        {
            RegisteredTimer timer = new(this, observation, ++nextTimer);
            timer.Inner = clock.CreateTimer(
                value =>
                {
                    lock (gate)
                    {
                        timer.Fired();
                    }
                    callback(value);
                },
                state,
                dueTime,
                period
            );
            timer.SetDue(dueTime, period);
            if (observation != 0)
            {
                timers.Add(timer);
            }
            timer.Record("registered");
            Pulse();
            return timer;
        }
    }

    private RegisteredTimer? Earliest(DateTimeOffset? strictlyBefore) =>
        timers
            .Where(timer =>
                timer.Observation == observation
                && !timer.Disposed
                && timer.Due is { } due
                && (strictlyBefore is not { } cutoff || due < cutoff)
            )
            .MinBy(timer => timer.Due);

    internal async Task WaitForPollAsync(
        CancellationToken token,
        DateTimeOffset? strictlyBefore = null
    )
    {
        while (true)
        {
            Task wait;
            lock (gate)
            {
                token.ThrowIfCancellationRequested();
                if (observation == 0)
                {
                    throw new InvalidOperationException(
                        "Timer observation must start before the run."
                    );
                }
                if (Earliest(strictlyBefore) is not null)
                {
                    return;
                }
                wait = changed.Task;
            }
            await wait.WaitAsync(token).ConfigureAwait(false);
        }
    }

    private Task ObserveChange()
    {
        lock (gate)
        {
            return changed.Task;
        }
    }

    // Readiness never mutates time. Subscribe before checking the goal, then
    // recheck it after every actual registration/state change and at the step.
    internal Task UntilAsync(
        Func<bool> goal,
        Task subject,
        DateTimeOffset strictlyBefore,
        CancellationToken token
    ) => UntilAsync(goal, null, subject, strictlyBefore, token);

    internal Task UntilAsync(
        Task goal,
        Task subject,
        DateTimeOffset strictlyBefore,
        CancellationToken token
    ) => UntilAsync(() => goal.IsCompleted, goal, subject, strictlyBefore, token);

    private async Task UntilAsync(
        Func<bool> goal,
        Task? goalTask,
        Task subject,
        DateTimeOffset strictlyBefore,
        CancellationToken token
    )
    {
        while (true)
        {
            Task change = ObserveChange();
            token.ThrowIfCancellationRequested();
            if (subject.IsCompleted)
            {
                await subject.ConfigureAwait(false);
                AssertGoal();
                return;
            }
            if (goal())
            {
                return;
            }
            if (TryAdvance(strictlyBefore, () => goal() || subject.IsCompleted, token))
            {
                continue;
            }
            // The revalidated step may have declined because the goal won.
            if (goal())
            {
                if (subject.IsCompleted)
                {
                    await subject.ConfigureAwait(false);
                }
                return;
            }
            if (subject.IsCompleted)
            {
                continue;
            }
            using CancellationTokenSource observation =
                CancellationTokenSource.CreateLinkedTokenSource(token);
            Task waiting = change.WaitAsync(observation.Token);
            try
            {
                Task wake = goalTask is null
                    ? Task.WhenAny(waiting, subject)
                    : Task.WhenAny(waiting, subject, goalTask);
                await wake.WaitAsync(token).ConfigureAwait(false);
            }
            finally
            {
                observation.Cancel();
                try
                {
                    await waiting.ConfigureAwait(false);
                }
                catch (OperationCanceledException) when (observation.IsCancellationRequested) { }
            }
        }

        void AssertGoal()
        {
            if (!goal())
            {
                throw new InvalidOperationException(
                    "Subject ended before the required durable goal."
                );
            }
        }
    }

    internal async Task TickAsync(
        CancellationToken token,
        DateTimeOffset? strictlyBefore = null,
        Func<bool>? stop = null
    )
    {
        while (true)
        {
            await WaitForPollAsync(token, strictlyBefore).ConfigureAwait(false);
            if (TryAdvance(strictlyBefore, stop ?? Never, token) || stop?.Invoke() == true)
            {
                return;
            }
        }
    }

    private static bool Never() => false;

    // No hidden await between the last goal/subject check and actual advance.
    // The same gate revalidates current armedness, due state and phase boundary.
    private bool TryAdvance(
        DateTimeOffset? strictlyBefore,
        Func<bool> stop,
        CancellationToken token
    )
    {
        lock (gate)
        {
            token.ThrowIfCancellationRequested();
            RegisteredTimer? timer = Earliest(strictlyBefore);
            if (timer?.Due is not { } due)
            {
                return false;
            }
            DateTimeOffset now = GetUtcNow();
            DateTimeOffset target = due > now ? due : now;
            if ((strictlyBefore is { } cutoff && target >= cutoff) || stop())
            {
                return false;
            }
            clock.Advance(target - now);
            Console.WriteLine(
                "PHASE7_TIMER_ADVANCE "
                    + new JsonObject
                    {
                        ["observation"] = observation,
                        ["timerId"] = timer.Id,
                        ["from"] = now.ToString("O"),
                        ["selectedDue"] = due.ToString("O"),
                        ["to"] = target.ToString("O"),
                        ["strictlyBefore"] = strictlyBefore?.ToString("O"),
                    }.ToJsonString()
            );
            return true;
        }
    }

    internal void Advance(TimeSpan amount)
    {
        lock (gate)
        {
            clock.Advance(amount);
        }
    }

    private void Pulse()
    {
        TaskCompletionSource previous = changed;
        changed = Signal();
        previous.TrySetResult();
    }

    private static TaskCompletionSource Signal() =>
        new(TaskCreationOptions.RunContinuationsAsynchronously);

    private sealed class Observation(Phase7Clock owner, long id) : IDisposable
    {
        public void Dispose()
        {
            lock (owner.gate)
            {
                if (owner.observation == id)
                {
                    owner.observation = 0;
                    owner.timers.Clear();
                    owner.Pulse();
                }
            }
        }
    }

    private sealed class RegisteredTimer(Phase7Clock owner, long observation, long id) : ITimer
    {
        private TimeSpan period;
        internal ITimer? Inner { get; set; }
        internal long Observation { get; } = observation;
        internal long Id { get; } = id;
        internal DateTimeOffset? Due { get; private set; }
        internal bool Disposed { get; private set; }

        internal void SetDue(TimeSpan dueTime, TimeSpan repeat)
        {
            Due = dueTime == Timeout.InfiniteTimeSpan ? null : owner.GetUtcNow() + dueTime;
            period = repeat;
        }

        internal void Fired()
        {
            Due = period > TimeSpan.Zero ? owner.GetUtcNow() + period : null;
            Record("fired");
            owner.Pulse();
        }

        internal void Record(string operation)
        {
            if (Observation != 0 && Observation == owner.observation)
            {
                Console.WriteLine(
                    "PHASE7_TIMER_STATE "
                        + new JsonObject
                        {
                            ["observation"] = Observation,
                            ["timerId"] = Id,
                            ["operation"] = operation,
                            ["due"] = Due?.ToString("O"),
                            ["disposed"] = Disposed,
                        }.ToJsonString()
                );
            }
        }

        public bool Change(TimeSpan dueTime, TimeSpan period)
        {
            lock (owner.gate)
            {
                if (!Inner!.Change(dueTime, period))
                {
                    return false;
                }
                SetDue(dueTime, period);
                Record("changed");
                owner.Pulse();
                return true;
            }
        }

        public void Dispose()
        {
            lock (owner.gate)
            {
                Disposed = true;
                Inner?.Dispose();
                Due = null;
                Record("disposed");
                owner.Pulse();
            }
        }

        public ValueTask DisposeAsync()
        {
            Dispose();
            return ValueTask.CompletedTask;
        }
    }
}
