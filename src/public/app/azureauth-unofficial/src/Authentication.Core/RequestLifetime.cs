namespace Authentication.Core;

// Application lifetime seam. RunAsync supplies a provisional observation; only the
// outcome returned by TryCommit may be delivered through the public result channel.
public sealed class RequestLifetime : IDisposable
{
    private readonly object gate = new();
    private readonly TimeProvider clock;
    private readonly long entryTimestamp;
    private readonly TimeSpan timeout;
    private readonly CancellationToken callerCancellation;
    private readonly CancellationTokenSource providerCancellation = new();
    private readonly CancellationToken providerToken;
    private readonly TaskCompletionSource<AuthenticationOutcome> terminal = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly TaskCompletionSource operationObserved = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private CancellationTokenRegistration callerRegistration;
    private ITimer? deadlineTimer;
    private Task cancellationCallbacks = Task.CompletedTask;
    private long terminalTimestamp = long.MinValue;
    private AuthenticationOutcome? outcome;
    private bool started;
    private bool committed;
    private bool disposed;

    // The host can include operation observation in its bounded shutdown drain.
    // Completion here does not establish owned-UI or process quiescence.
    public Task OperationCompletion => operationObserved.Task;

    // Read without the terminal lock so a process watchdog never waits on work.
    public long? TerminalTimestamp
    {
        get
        {
            var value = Volatile.Read(ref terminalTimestamp);
            return value == long.MinValue ? null : value;
        }
    }

    public async Task CompleteAsync()
    {
        // Select assigns cancellationCallbacks before publishing terminal. Reading
        // it before that publication could miss callbacks that are still running.
        await terminal.Task.ConfigureAwait(false);
        await OperationCompletion.ConfigureAwait(false);
        await cancellationCallbacks.ConfigureAwait(false);
    }

    public RequestLifetime(TimeProvider clock, long entryTimestamp, TimeSpan timeout,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(clock);
        if (timeout <= TimeSpan.Zero || timeout > TimeSpan.FromSeconds(600))
        {
            throw new ArgumentOutOfRangeException(nameof(timeout));
        }

        this.clock = clock;
        this.entryTimestamp = entryTimestamp;
        this.timeout = timeout;
        callerCancellation = cancellationToken;
        providerToken = providerCancellation.Token;
    }

    public Task<AuthenticationOutcome> RunAsync(Func<CancellationToken, Task<AuthenticationOutcome>> operation)
    {
        ArgumentNullException.ThrowIfNull(operation);
        lock (gate)
        {
            ObjectDisposedException.ThrowIf(disposed, this);
            if (started) throw new InvalidOperationException("A lifetime owns one request.");
            started = true;

            if (outcome is null)
            {
                if (callerCancellation.IsCancellationRequested)
                {
                    Select(Failed(AuthenticationFailure.Cancelled));
                }
                else if (Remaining <= TimeSpan.Zero)
                {
                    Select(Failed(AuthenticationFailure.Timeout));
                }
                else
                {
                    deadlineTimer = clock.CreateTimer(static state => ((RequestLifetime)state!).CheckDeadline(),
                        this, Timeout.InfiniteTimeSpan, Timeout.InfiniteTimeSpan);
                    callerRegistration = callerCancellation.Register(static state => ((RequestLifetime)state!).CancelFromCaller(), this);
                    // Registration may synchronously observe an already requested cancellation.
                    if (outcome is null) CheckDeadline();
                }
            }

            if (outcome is not null)
            {
                operationObserved.TrySetResult();
                return terminal.Task;
            }
        }

        // Return the terminal task even when a dependency blocks before its first
        // await. Do not cancel dispatch: its finally must publish actual completion.
        // The concrete request body checks the original token before each effect.
        _ = Task.Run(() => ObserveOperationAsync(operation));
        return terminal.Task;
    }

    private TimeSpan Remaining => timeout - clock.GetElapsedTime(entryTimestamp);

    private async Task ObserveOperationAsync(Func<CancellationToken, Task<AuthenticationOutcome>> operation)
    {
        try
        {
            AuthenticationOutcome candidate;
            try
            {
                providerToken.ThrowIfCancellationRequested();
                candidate = await operation(providerToken).ConfigureAwait(false)
                    ?? Failed(AuthenticationFailure.InternalFailure);
            }
            catch (OperationCanceledException) when (providerToken.IsCancellationRequested)
            {
                candidate = Failed(AuthenticationFailure.Cancelled);
            }
            catch (Exception)
            {
                candidate = Failed(AuthenticationFailure.InternalFailure);
            }

            lock (gate)
            {
                if (disposed || outcome is not null) return;
                if (callerCancellation.IsCancellationRequested)
                {
                    candidate = Failed(AuthenticationFailure.Cancelled);
                }
                else if (Remaining <= TimeSpan.Zero)
                {
                    candidate = Failed(AuthenticationFailure.Timeout);
                }

                Select(candidate);
            }
        }
        finally
        {
            operationObserved.TrySetResult();
        }
    }

    private void CancelFromCaller()
    {
        lock (gate)
        {
            if (disposed || committed) return;
            if (outcome is null)
            {
                Select(Failed(AuthenticationFailure.Cancelled));
            }
            else if (outcome.Success is not null)
            {
                outcome = Failed(AuthenticationFailure.Cancelled);
            }
        }
    }

    private void CheckDeadline()
    {
        lock (gate)
        {
            if (disposed || outcome is not null) return;
            var remaining = Remaining;
            if (remaining <= TimeSpan.Zero)
            {
                Select(Failed(AuthenticationFailure.Timeout));
            }
            else
            {
                deadlineTimer?.Change(remaining, Timeout.InfiniteTimeSpan);
            }
        }
    }

    // Called with gate held. A selected outcome invalidates every late observation.
    private void Select(AuthenticationOutcome selected)
    {
        Volatile.Write(ref terminalTimestamp, clock.GetTimestamp());
        outcome = selected;
        deadlineTimer?.Dispose();
        // CancelAsync marks the token now without running provider callbacks inline.
        cancellationCallbacks = ObserveCancellationCallbacksAsync(providerCancellation.CancelAsync());
        terminal.TrySetResult(selected);
    }

    private static async Task ObserveCancellationCallbacksAsync(Task callbacks)
    {
        try
        {
            await callbacks.ConfigureAwait(false);
        }
        catch (Exception)
        {
            // Callback failure cannot replace a terminal result or expose provider text.
        }
    }

    public bool TryCommit(out AuthenticationOutcome? result)
    {
        lock (gate)
        {
            result = null;
            if (disposed || committed || outcome is null) return false;
            if (outcome.Success is not null && callerCancellation.IsCancellationRequested)
            {
                outcome = Failed(AuthenticationFailure.Cancelled);
            }

            committed = true;
            result = outcome;
            return true;
        }
    }

    // Host faults terminate pending work and suppress success before commitment.
    // Keep the first terminal timestamp and any previously selected failure.
    public void FailHost()
    {
        lock (gate)
        {
            if (disposed || committed || outcome is { Success: null }) return;
            var failure = callerCancellation.IsCancellationRequested
                ? AuthenticationFailure.Cancelled
                : outcome is null && Remaining <= TimeSpan.Zero
                    ? AuthenticationFailure.Timeout
                    : AuthenticationFailure.InternalFailure;
            if (outcome is null) Select(Failed(failure));
            else outcome = Failed(failure);
        }
    }

    public void Dispose()
    {
        lock (gate)
        {
            if (disposed) return;
            if (outcome is null) Select(Failed(AuthenticationFailure.Cancelled));
            disposed = true;
            deadlineTimer?.Dispose();
            callerRegistration.Unregister();
            if (!started) operationObserved.TrySetResult();
        }

        _ = ReleaseProviderSourceAsync();
    }

    private async Task ReleaseProviderSourceAsync()
    {
        // Pending provider work remains subject to the host's finite shutdown bound.
        // Do not dispose its source while callbacks or operation observation use it.
        await CompleteAsync().ConfigureAwait(false);
        providerCancellation.Dispose();
    }

    private static AuthenticationOutcome Failed(AuthenticationFailure failure) => new(null, failure);
}
