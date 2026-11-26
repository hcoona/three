using Authentication.Core;

namespace Authentication.Windows.Scenarios;

// Every identity and provider result is synthetic. Each case owns one child and
// at most one lazy Win32 parent; it never initializes MSAL or inspects the desktop.
internal sealed class OwnedProcessChild(string scenario) : IAuthenticationProvider, IWindowsHostAdmission
{
    private static readonly TimeSpan Limit = TimeSpan.FromSeconds(3);
    private readonly TaskCompletionSource notificationRelease = NewSignal();
    private readonly TaskCompletionSource cancelPending = NewSignal();
    private readonly TaskCompletionSource faultPending = NewSignal();
    private readonly TaskCompletionSource cancelDrained = NewSignal();
    private readonly TaskCompletionSource cancelForwarded = NewSignal();
    private readonly TaskCompletionSource faultDrained = NewSignal();
    private readonly TaskCompletionSource nativeCleanup = NewSignal();
    private readonly TaskCompletionSource cleanupRelease = NewSignal();
    private OwnedRequestHost? host;
    private Thread? uiThread;
    private nint parent;
    private int faultArmed;

    internal static bool Supports(string value) => value is "host-success" or "host-cancel"
        or "host-fault" or "host-create-cancel" or "host-callback-drain" or "host-create-failure"
        or "host-fault-before-commit" or "host-fault-after-commit" or "host-ui-join" or "host-close-stall"
        or "host-notice" or "host-notice-raw" or "host-notice-blocked";

    // These cases use the product's default host construction and notice path.
    internal static bool DefaultHost(string value) => value is "host-notice" or "host-notice-raw" or "host-notice-blocked";

    internal static int Run(string scenario, string[] arguments, long entry)
    {
        ProcessChild.Mark("entered", entry);
        var fixture = new OwnedProcessChild(scenario);
        var result = WindowsProcess.RunOwned(arguments, entry, (profile, request) =>
        {
            ProcessChild.Mark("provider-created");
            if (profile.Name != "synthetic-process-profile" || request.AccountEmail != ProcessChild.Email
                || request.ExactTenant != Guid.Parse(ProcessChild.Tenant)
                || !request.InteractionAllowed || !request.Scopes.SequenceEqual([ProcessChild.Scope]))
                throw new InvalidOperationException("Synthetic request admission failed.");
            return fixture;
        }, createHost: DefaultHost(scenario) ? null : fixture.CreateHost, checkpoint: fixture.ProcessCheckpoint);
        if (DefaultHost(scenario)) return result;
        if (fixture.host!.Completion.IsCompletedSuccessfully && fixture.uiThread is { IsAlive: false })
            ProcessChild.Mark("host-drained");
        else ProcessChild.Mark("host-undrained");
        return result;
    }

    private OwnedRequestHost CreateHost(Func<Task> cancel, Action fault)
    {
        host = new OwnedRequestHost(async () =>
        {
            ProcessChild.Mark("cancel-pending");
            cancelPending.TrySetResult();
            await notificationRelease.Task.WaitAsync(Limit);
            await cancel();
            ProcessChild.Mark("cancel-forwarded");
            cancelForwarded.TrySetResult();
            if (scenario == "host-callback-drain") await Task.Delay(Limit);
            ProcessChild.Mark("cancel-drained");
            cancelDrained.TrySetResult();
        }, () =>
        {
            ProcessChild.Mark("fault-pending");
            faultPending.TrySetResult();
            notificationRelease.Task.WaitAsync(Limit).GetAwaiter().GetResult();
            fault();
            ProcessChild.Mark("fault-drained");
            faultDrained.TrySetResult();
        }, HostCheckpoint, this);
        return host;
    }

    private void HostCheckpoint(OwnedHostCheckpoint stage, nint window)
    {
        if (stage == OwnedHostCheckpoint.ThreadStarted)
        {
            uiThread = Thread.CurrentThread;
            if (uiThread.GetApartmentState() != ApartmentState.STA)
                throw new InvalidOperationException("The owned thread must be STA.");
            ProcessChild.Mark("ui-thread-started");
        }
        if (stage == OwnedHostCheckpoint.HiddenParentCreated)
        {
            OwnedWindowObservation.RequireOwned(window);
            if (OwnedWindowObservation.IsVisible(window))
                throw new InvalidOperationException("The parent was visible before readiness.");
            parent = window;
            ProcessChild.Mark("hidden-parent-created");
            if (scenario == "host-create-failure")
                throw new InvalidOperationException("Synthetic creation failure.");
            if (scenario == "host-create-cancel")
            {
                ProcessChild.Mark("local-close-sent");
                OwnedWindowObservation.CloseOnCreatingThread(window);
                ProcessChild.Mark("local-close-returned");
            }
        }
        if (stage == OwnedHostCheckpoint.MessageDispatch)
        {
            if (Interlocked.Exchange(ref faultArmed, 0) == 1)
            {
                ProcessChild.Mark("native-fault-injected");
                throw new InvalidOperationException("Synthetic post-readiness callback failure.");
            }
        }
        if (stage == OwnedHostCheckpoint.Closing)
        {
            ProcessChild.Mark("host-closing");
            if (scenario == "host-create-cancel")
                throw new InvalidOperationException("Synthetic closure failure after local cancellation.");
            if (scenario == "host-close-stall") Thread.Sleep(Limit);
        }
        if (stage == OwnedHostCheckpoint.NativeCleanupCompleted)
        {
            if (OwnedWindowObservation.Exists(parent))
                throw new InvalidOperationException("Native teardown left the owned parent alive.");
            ProcessChild.Mark("native-cleanup-completed");
            nativeCleanup.TrySetResult();
            if (scenario == "host-ui-join") Thread.Sleep(Limit);
            if (scenario == "host-fault-after-commit")
                cleanupRelease.Task.WaitAsync(Limit).GetAwaiter().GetResult();
            if (scenario is "host-fault-before-commit" or "host-fault-after-commit")
            {
                ProcessChild.Mark("cleanup-fault-injected");
                throw new InvalidOperationException("Synthetic final owned cleanup failure.");
            }
        }
    }

    private void ProcessCheckpoint(OwnedProcessCheckpoint stage)
    {
        if (stage == OwnedProcessCheckpoint.BeforeCommit)
        {
            // These waits arrange actual host observations; they do not select a
            // result or run under the process/host/lifetime commitment gates.
            if (scenario == "host-fault-before-commit")
                faultPending.Task.WaitAsync(Limit).GetAwaiter().GetResult();
            if (scenario is "host-ui-join" or "host-fault-after-commit" or "host-create-cancel")
                nativeCleanup.Task.WaitAsync(Limit).GetAwaiter().GetResult();
            if (scenario == "host-callback-drain")
            {
                // Isolate callback drainage from OS-thread drainage. The outgoing
                // callback remains pending, so aggregate Host.Completion must not
                // be awaited by this setup or it would hide the production gap.
                nativeCleanup.Task.WaitAsync(Limit).GetAwaiter().GetResult();
                if (uiThread is null || !uiThread.Join(Limit))
                    throw new InvalidOperationException("The callback-only setup did not observe actual STA exit.");
                ProcessChild.Mark("ui-thread-exited");
            }
            if (scenario is "host-success" or "host-create-failure")
                host!.Completion.WaitAsync(Limit).GetAwaiter().GetResult();
            ProcessChild.Mark("before-commit");
        }
        else if (stage == OwnedProcessCheckpoint.AfterCommit)
        {
            ProcessChild.Mark("after-commit");
            cleanupRelease.TrySetResult();
            notificationRelease.TrySetResult();
            if (scenario == "host-cancel")
                cancelDrained.Task.WaitAsync(Limit).GetAwaiter().GetResult();
            if (scenario == "host-callback-drain")
                cancelForwarded.Task.WaitAsync(Limit).GetAwaiter().GetResult();
            if (scenario is "host-fault" or "host-fault-before-commit" or "host-fault-after-commit")
                faultDrained.Task.WaitAsync(Limit).GetAwaiter().GetResult();
        }
    }

    public void Admit(CancellationToken token) => token.ThrowIfCancellationRequested();

    public void Recheck(CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        if (Thread.CurrentThread != uiThread)
            throw new InvalidOperationException("UI admission left the owned thread.");
    }

    public Task<IReadOnlyList<ProviderAccount>> GetAccountsAsync(CancellationToken token) =>
        Task.FromResult<IReadOnlyList<ProviderAccount>>([]);

    public Task<TokenCandidate> AcquireSilentAsync(AuthenticationRequest request,
        ProviderAccount account, Guid operationId, CancellationToken token) =>
        throw new InvalidOperationException("This scenario requires owned interaction.");

    public async Task<TokenCandidate> AcquireInteractiveAsync(AuthenticationRequest request,
        ProviderAccount? selected, string? claims, nint window, Guid operationId, CancellationToken token)
    {
        if (DefaultHost(scenario))
        {
            OwnedWindowObservation.RequireOwned(window);
            if (OwnedWindowObservation.IsVisible(window))
                throw new InvalidOperationException("The default owned parent must stay hidden.");
        }
        else if (window != parent || OwnedWindowObservation.IsVisible(window))
            throw new InvalidOperationException("Interactive acquisition requires the ready owned parent.");
        ProcessChild.Mark("provider-ready");
        if (scenario is "host-cancel" or "host-callback-drain")
        {
            OwnedWindowObservation.Post(window, 0x0010);
            await cancelPending.Task.WaitAsync(Limit);
        }
        if (scenario == "host-fault")
        {
            Interlocked.Exchange(ref faultArmed, 1);
            OwnedWindowObservation.Post(window, 0);
            await faultPending.Task.WaitAsync(Limit);
        }
        ProcessChild.Mark("candidate-returned");
        return new(ProcessChild.Token, ProcessChild.Email, Guid.Parse(ProcessChild.Tenant),
            [ProcessChild.Scope], "Bearer", DateTimeOffset.UtcNow.AddMinutes(10), operationId);
    }

    private static TaskCompletionSource NewSignal() => new(TaskCreationOptions.RunContinuationsAsynchronously);
}
