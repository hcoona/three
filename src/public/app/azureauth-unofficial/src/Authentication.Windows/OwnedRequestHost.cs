using System.Runtime.CompilerServices;
using Authentication.Core;

[assembly: InternalsVisibleTo("Authentication.Windows.Scenarios")]

namespace Authentication.Windows;

// One request owns one lazy, hidden, childless STA owner window. Closure invalidates readiness immediately;
// Completion separately drains callbacks and joins the actual creating thread.
internal sealed partial class OwnedRequestHost : IRequestHost
{
    private readonly object gate = new();
    private readonly Func<Task> cancel;
    private readonly Action fault;
    private readonly Action<OwnedHostCheckpoint, nint>? checkpoint;
    private readonly IWindowsHostAdmission? admission;
    private readonly Action<byte[]>? notice;
    private readonly TaskCompletionSource<nint> ready = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly TaskCompletionSource completed = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private ClientProfile? profile;
    private CancellationToken cancellationToken;
    private CancellationTokenRegistration cancellationRegistration;
    private Thread? thread;
    private nint parent;
    private bool terminal;
    private bool published;
    private Task? cancellation;
    private Task? faultNotification;
    private Exception? cleanupFailure;
    private OwnedHostObservation observation;
    private long endingTimestamp = long.MinValue;

    internal OwnedRequestHost(Func<Task> cancel, Action fault,
        Action<OwnedHostCheckpoint, nint>? checkpoint = null, IWindowsHostAdmission? admission = null,
        Action<byte[]>? notice = null)
    {
        this.cancel = cancel;
        this.fault = fault;
        this.checkpoint = checkpoint;
        this.admission = admission;
        this.notice = notice;
    }

    // One bounded fixed-template indication before the provider display. Profile
    // name and owner are admitted public values (at most 128 characters, no controls).
    internal static byte[] Notice(ClientProfile profile) => System.Text.Encoding.UTF8.GetBytes(
        $"Unofficial hcoona/microsoft-authentication-cli. Profile: {profile.Name}. "
        + $"Registration owner: {profile.RegistrationOwner}. Sign-in continues in the provider display.\n");

    public TimeProvider Clock => TimeProvider.System;

    internal long? EndingTimestamp
    {
        get
        {
            var value = Volatile.Read(ref endingTimestamp);
            return value == long.MinValue ? null : value;
        }
    }

    // Only bounded process/core state work may run here. Native dispatch records
    // its observation and queues notifications without calling into those gates.
    internal void WithLocalObservation(Action<OwnedHostObservation> action)
    {
        lock (gate) action(observation);
    }

    internal Task Completion
    {
        get { lock (gate) return thread is null ? Task.CompletedTask : completed.Task; }
    }

    internal void BindProfile(ClientProfile profile)
    {
        ArgumentNullException.ThrowIfNull(profile);
        lock (gate)
        {
            if (terminal || thread is not null || this.profile is not null)
                throw new InvalidOperationException("The request presentation is already bound or closed.");
            this.profile = profile; // Retain the admitted immutable value; never reopen its file.
        }
    }

    public Task<nint> OpenInteractionAsync(CancellationToken cancellationToken)
    {
        lock (gate)
        {
            if (terminal) return Task.FromResult<nint>(0);
            if (thread is not null) return ready.Task;
            if (profile is null || cancellationToken.IsCancellationRequested)
            {
                Ending();
                terminal = true;
                ready.TrySetResult(0);
                return ready.Task;
            }

            this.cancellationToken = cancellationToken;
            cancellationRegistration = cancellationToken.UnsafeRegister(
                static state => ((OwnedRequestHost)state!).CloseFromCancellation(), this);
            // Registration can synchronously observe cancellation before returning.
            if (terminal)
            {
                cancellationRegistration.Unregister();
                return ready.Task;
            }

            var started = false;
            try
            {
                thread = new Thread(RunWindow) { IsBackground = true };
                thread.SetApartmentState(ApartmentState.STA);
                thread.Start();
                started = true;
            }
            catch (Exception)
            {
                Ending();
                terminal = true;
                ready.TrySetResult(0);
                cancellationRegistration.Unregister();
            }

            // Join also handles a failed startup that reached the stopped state.
            if (thread is { } ownedThread && (started || (ownedThread.ThreadState & ThreadState.Unstarted) == 0))
            {
                try { _ = Task.Run(() => JoinWindowAsync(ownedThread)); }
                catch (Exception exception)
                {
                    CloseLocked();
                    completed.TrySetException(exception);
                }
            }
            else completed.TrySetResult();
            return ready.Task;
        }
    }

    public Task CloseInteractionAsync()
    {
        lock (gate)
        {
            try
            {
                CloseLocked();
                return Task.CompletedTask;
            }
            catch (Exception exception)
            {
                RecordCleanupFailureLocked(exception);
                return Task.FromException(exception);
            }
        }
    }

    private void CloseFromCancellation()
    {
        lock (gate)
        {
            try { CloseLocked(); }
            catch (Exception exception) { RecordCleanupFailureLocked(exception); }
        }
    }

    private void CloseLocked()
    {
        if (terminal) return;
        Ending();
        terminal = true;
        // Test-only hook (null in production). It runs under gate, so it must not wait
        // for another thread that takes gate.
        checkpoint?.Invoke(OwnedHostCheckpoint.Closing, parent);
        ready.TrySetResult(0);
        // Detachment and posting share this lock, so a recycled HWND cannot receive
        // a late close. Calls from another thread never wait for native destruction.
        if (parent == 0) return;
        PostOwnedClose(parent);
    }

    private bool IsTerminal
    {
        get
        {
            lock (gate)
            {
                if (cancellationToken.IsCancellationRequested) CloseLocked();
                return terminal;
            }
        }
    }

    private void CancelFromWindow()
    {
        lock (gate)
        {
            if (terminal) return;
            // A callback's synchronous prefix or outstanding asynchronous work must
            // not block native dispatch. Preserve cancellation before a failed
            // close could otherwise publish zero-parent unavailability.
            observation = OwnedHostObservation.UserCancellation;
            Ending();
            ready.TrySetException(new ProviderFailureException(AuthenticationFailure.Cancelled));
            try { CloseLocked(); }
            catch (Exception exception) { RecordCleanupFailureLocked(exception); }
            cancellation = Task.Run(async () =>
            {
                try { await cancel().ConfigureAwait(false); }
                catch (Exception)
                {
                    FailHost(forceNotification: true);
                    throw;
                }
            });
        }
    }

    private void FailHost(bool forceNotification = false)
    {
        lock (gate)
        {
            try { CloseLocked(); }
            catch (Exception exception) { cleanupFailure ??= exception; }
            if (published || forceNotification) NotifyFaultLocked();
        }
    }

    private void NotifyFaultLocked()
    {
        // A cleanup fault must not erase an earlier local user cancellation.
        if (observation == OwnedHostObservation.None) observation = OwnedHostObservation.HostFault;
        Ending();
        try { faultNotification ??= Task.Run(fault); }
        catch (Exception exception) { cleanupFailure ??= exception; }
    }

    private void RecordCleanupFailureLocked(Exception exception)
    {
        cleanupFailure ??= exception;
        NotifyFaultLocked();
    }

    private void RunWindow()
    {
        currentHost = this;
        try
        {
            checkpoint?.Invoke(OwnedHostCheckpoint.ThreadStarted, 0);
            if (IsTerminal) return;
            RecheckAdmission();
            if (IsTerminal) return;
            var window = CreateNativeParent();
            lock (gate) parent = window;
            checkpoint?.Invoke(OwnedHostCheckpoint.HiddenParentCreated, window);
            if (IsTerminal) return;
            RecheckAdmission();

            // The parent stays hidden and childless: WAM owns the visible sign-in display.
            if (IsTerminal) return;
            try { notice?.Invoke(Notice(profile!)); }
            catch (Exception) { } // Optional output cannot alter authentication.

            lock (gate)
            {
                // This is the only readiness publication path. Creation checkpoints
                // hold no lock and cannot publish readiness after invalidation.
                if (IsTerminal) return;
                published = true;
                ready.TrySetResult(window);
            }

            PumpNativeMessages();
        }
        catch (Exception)
        {
            FailHost(); // Before readiness this is a typed zero-parent failure.
        }
        finally
        {
            lock (gate)
            {
                Ending();
                terminal = true;
                ready.TrySetResult(0);
                parent = 0; // Prevent cross-thread posts before destruction/reuse.
            }
            try { CleanupNative(); }
            catch (Exception exception)
            {
                lock (gate) RecordCleanupFailureLocked(exception);
            }
            try { cancellationRegistration.Dispose(); }
            catch (Exception exception)
            {
                lock (gate) RecordCleanupFailureLocked(exception);
            }
            try { checkpoint?.Invoke(OwnedHostCheckpoint.NativeCleanupCompleted, 0); }
            catch (Exception exception)
            {
                lock (gate) RecordCleanupFailureLocked(exception);
            }
            // The static unmanaged callback needs no delegate/GCHandle. Its per-thread
            // managed context stays rooted through destruction and the final checkpoint.
            currentHost = null;
        }
    }

    private void RecheckAdmission()
    {
        cancellationToken.ThrowIfCancellationRequested();
        try
        {
            // Observe on the actual STA, outside the gate so cancellation can close
            // the host. Recheck terminal state before creation or the gated show.
            admission?.Recheck(cancellationToken);
        }
        catch
        {
            cancellationToken.ThrowIfCancellationRequested();
            throw;
        }

        cancellationToken.ThrowIfCancellationRequested();
    }

    private void Ending() => Interlocked.CompareExchange(ref endingTimestamp,
        Clock.GetTimestamp(), long.MinValue);

    private async Task JoinWindowAsync(Thread ownedThread)
    {
        try
        {
            ownedThread.Join(); // Actual OS-thread exit, on a separate background worker.
            Task? cancelTask;
            lock (gate) cancelTask = cancellation;
            if (cancelTask is not null) await cancelTask.ConfigureAwait(false);
            Task? faultTask;
            Exception? failure;
            lock (gate)
            {
                faultTask = faultNotification;
                failure = cleanupFailure;
            }
            if (faultTask is not null) await faultTask.ConfigureAwait(false);
            if (failure is not null) throw new InvalidOperationException("Owned UI cleanup failed.", failure);
            completed.TrySetResult();
        }
        catch (Exception exception)
        {
            completed.TrySetException(exception);
        }
    }
}

internal enum OwnedHostObservation
{
    None,
    UserCancellation,
    HostFault,
}

// These internal checkpoints control finite scheduling and callback faults in
// scenarios. They do not replace the native window or provider boundary.
internal enum OwnedHostCheckpoint
{
    ThreadStarted,
    HiddenParentCreated,
    MessageDispatch,
    NativeCleanupCompleted,
    Closing,
}
