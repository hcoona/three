using System.Net;
using Microsoft.Identity.Client;

namespace Authentication.Windows.Scenarios;

// The default composition owns the real HTTP client. Only its admission, loader,
// session, and terminal transport are substituted; no identity or network is read.
internal sealed class DefaultHttpProcessChild(string scenario) : IWindowsHostAdmission, IMsalSessionFactory, IMsalSession
{
    private static readonly TimeSpan Limit = TimeSpan.FromSeconds(3);
    private readonly TaskCompletionSource<long> httpNotification = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly TaskCompletionSource callbackPending = NewSignal();
    private readonly TaskCompletionSource callbackCompleted = NewSignal();
    private readonly TaskCompletionSource hostForwarded = NewSignal();
    private readonly TaskCompletionSource hostCallbackPublished = NewSignal();
    private readonly TaskCompletionSource release = NewSignal();
    private MsalHttpClientFactory? owner;
    private Transport? transport;
    private OwnedRequestHost? host;
    private Thread? uiThread;
    private nint parent;
    private CancellationToken original;
    private Task<HttpResponseMessage>? send;
    private Task<AuthenticationResult>? provider;
    private Task? hostCallback;

    private bool Cancelling => scenario == "default-http-cancel-drain";

    internal static bool Supports(string value) => value is "default-http-cancel-drain" or "default-http-dispose-stall";

    internal static int Run(string scenario, string[] arguments, long entry)
    {
        ProcessChild.Mark("entered", entry);
        var scene = new DefaultHttpProcessChild(scenario);
        var result = WindowsProcess.RunDefault(arguments, entry, scene, scene.RestrictLoader,
            scene, scene.CreateHttp, scene.CreateHost, scene.ProcessCheckpoint);
        ProcessChild.Mark("process-returned");
        return result;
    }

    private OwnedRequestHost CreateHost(Func<Task> cancel, Action fault, IWindowsHostAdmission admission)
    {
        Require(ReferenceEquals(admission, this));
        ProcessChild.Mark("host-created");
        host = new OwnedRequestHost(() =>
        {
            var pending = ForwardCancelAsync(cancel);
            Volatile.Write(ref hostCallback, pending);
            hostCallbackPublished.TrySetResult();
            return pending;
        }, fault, HostCheckpoint, admission);
        return host;
    }

    private async Task ForwardCancelAsync(Func<Task> cancel)
    {
        Require(Cancelling);
        ProcessChild.Mark("host-callback-pending");
        await httpNotification.Task.WaitAsync(Limit);
        var forwarding = cancel();
        ProcessChild.Mark("host-callback-forwarded");
        hostForwarded.TrySetResult();
        await release.Task.WaitAsync(Limit);
        await forwarding.WaitAsync(Limit);
    }

    private MsalHttpClientFactory CreateHttp()
    {
        Require(owner is null);
        transport = new Transport(this);
        owner = new MsalHttpClientFactory(transport);
        ProcessChild.Mark("http-owner-created");
        return owner;
    }

    public void Admit(CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        Require(token.CanBeCanceled);
        original = token;
    }

    public void Recheck(CancellationToken token) => CheckToken(token);

    private void CheckToken(CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        Require(token == original);
    }

    private void RestrictLoader(CancellationToken token)
    {
        CheckToken(token);
        Require(owner is not null && transport is not null);
        ProcessChild.Mark("loader-entered");
    }

    public IMsalSession Create(MsalClientSettings settings, IMsalHttpClientFactory http, CancellationToken token)
    {
        CheckToken(token);
        Require(ReferenceEquals(http, owner)
            && settings.ClientId == Guid.Parse("aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee")
            && settings.Authority == "https://login.microsoftonline.com/" + ProcessChild.Tenant
            && settings.RedirectUri == "ms-appx-web://microsoft.aad.brokerplugin/" + settings.ClientId
            && settings.ListOperatingSystemAccounts && !settings.MsaPassthrough);
        ProcessChild.Mark("session-created");
        return this;
    }

    public Task<IReadOnlyList<IAccount>> GetAccountsAsync(CancellationToken token)
    {
        CheckToken(token);
        return Task.FromResult<IReadOnlyList<IAccount>>([]);
    }

    public Task<AuthenticationResult> AcquireSilentAsync(MsalSilentOperation operation, CancellationToken token) =>
        throw new InvalidOperationException("The synthetic journey requires its owned parent.");

    public Task<AuthenticationResult> AcquireInteractiveAsync(MsalInteractiveOperation operation, CancellationToken token)
    {
        CheckToken(token);
        Require(operation.ParentWindow == parent && !OwnedWindowObservation.IsVisible(parent)
            && operation.Account is null && operation.LoginHint == ProcessChild.Email
            && operation.Claims is null && operation.Tenant == ProcessChild.Tenant
            && operation.Scopes.SequenceEqual([ProcessChild.Scope]) && operation.CorrelationId != Guid.Empty);
        OwnedWindowObservation.RequireOwned(parent);
        ProcessChild.Mark("provider-ready");
        var pending = SendAndReturnAsync(operation, token);
        Volatile.Write(ref provider, pending);
        if (Cancelling)
        {
            _ = Task.Run(ObserveReleaseAsync);
            OwnedWindowObservation.Post(parent, 0x0010);
        }
        return pending;
    }

    private async Task<AuthenticationResult> SendAndReturnAsync(MsalInteractiveOperation operation, CancellationToken token)
    {
        using var callback = Cancelling ? token.Register(HoldOriginalCallback) : default;
        using var request = new HttpRequestMessage(HttpMethod.Get, "https://authority.example.test/synthetic");
        var sending = owner!.GetHttpClient().SendAsync(request, token);
        Volatile.Write(ref send, sending);
        using var response = await sending;
        Require(response.StatusCode == HttpStatusCode.OK);
        CheckToken(token);
        ProcessChild.Mark("candidate-returned");
        var now = DateTimeOffset.UtcNow;
        return new AuthenticationResult(ProcessChild.Token, false, "SYNTHETIC_ID_TOKEN",
            now.AddMinutes(10), now.AddMinutes(10), ProcessChild.Tenant,
            new SyntheticAccount(), "SYNTHETIC_UNIQUE_ID", [ProcessChild.Scope],
            operation.CorrelationId, tokenType: "Bearer");
    }

    private void HoldOriginalCallback()
    {
        try
        {
            // Registration order alone is insufficient: never block the thread
            // that must first deliver HttpClient's linked-token notification.
            if (!httpNotification.Task.IsCompletedSuccessfully)
            {
                ProcessChild.Mark("cancellation-order-invalid");
                callbackCompleted.TrySetException(new InvalidOperationException("HTTP notification was not first."));
                return;
            }
            ProcessChild.Mark("http-cancel-observed", httpNotification.Task.Result);
            ProcessChild.Mark("provider-callback-pending");
            callbackPending.TrySetResult();
            release.Task.WaitAsync(Limit).GetAwaiter().GetResult();
            callbackCompleted.TrySetResult();
        }
        catch (Exception exception)
        {
            callbackCompleted.TrySetException(exception);
            throw;
        }
    }

    private async Task ObserveReleaseAsync()
    {
        await Task.WhenAll(httpNotification.Task, callbackPending.Task, hostForwarded.Task,
            hostCallbackPublished.Task).WaitAsync(Limit);
        Require(Volatile.Read(ref send) is { IsCompleted: false }
            && Volatile.Read(ref provider) is { IsCompleted: false }
            && host!.Completion is { IsCompleted: false }
            && Volatile.Read(ref hostCallback) is { IsCompleted: false }
            && !callbackCompleted.Task.IsCompleted && transport!.Disposed == 0
            && !Marked("http-dispose-entered") && !Marked("process-returned"));
        ProcessChild.Mark("pending-drain-observed");
        var start = TimeProvider.System.GetTimestamp();
        while (!Marked("release-drain"))
        {
            Require(TimeProvider.System.GetElapsedTime(start) < Limit);
            await Task.Delay(5);
        }
        Require(new FileInfo(Path.Combine(Path.GetTempPath(), "release-drain")).Length == 0);
        ProcessChild.Mark("drain-release-observed");
        release.TrySetResult();
    }

    private bool WorkDrained => (Cancelling
            ? Volatile.Read(ref send) is { IsCanceled: true } && Volatile.Read(ref provider) is { IsCanceled: true }
            : Volatile.Read(ref send) is { IsCompletedSuccessfully: true }
                && Volatile.Read(ref provider) is { IsCompletedSuccessfully: true })
        && host!.Completion.IsCompletedSuccessfully && uiThread is { IsAlive: false }
        && (!Cancelling || (callbackCompleted.Task.IsCompletedSuccessfully
            && Volatile.Read(ref hostCallback) is { IsCompletedSuccessfully: true }));

    private void ProcessCheckpoint(OwnedProcessCheckpoint stage)
    {
        switch (stage)
        {
            case OwnedProcessCheckpoint.BeforeCommit:
                ProcessChild.Mark("before-commit");
                break;
            case OwnedProcessCheckpoint.AfterCommit:
                ProcessChild.Mark("after-commit");
                break;
            case OwnedProcessCheckpoint.BeforeHttpDisposal:
                // Observe the real tasks only; production has already drained them.
                Require(WorkDrained);
                ProcessChild.Mark("drain-boundary");
                break;
        }
    }

    private void HostCheckpoint(OwnedHostCheckpoint stage, nint window)
    {
        if (stage == OwnedHostCheckpoint.ThreadStarted)
        {
            uiThread = Thread.CurrentThread;
            Require(uiThread.GetApartmentState() == ApartmentState.STA);
            ProcessChild.Mark("ui-thread-started");
        }
        if (stage == OwnedHostCheckpoint.HiddenParentCreated)
        {
            OwnedWindowObservation.RequireOwned(window);
            Require(!OwnedWindowObservation.IsVisible(window));
            parent = window;
            ProcessChild.Mark("hidden-parent-created");
        }
        if (stage == OwnedHostCheckpoint.Closing) ProcessChild.Mark("host-closing");
        if (stage == OwnedHostCheckpoint.NativeCleanupCompleted)
        {
            Require(!OwnedWindowObservation.Exists(parent));
            ProcessChild.Mark("native-cleanup-completed");
        }
    }

    private sealed class Transport(DefaultHttpProcessChild scene) : HttpMessageHandler
    {
        internal int Disposed;

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token)
        {
            Require(request.RequestUri?.AbsoluteUri == "https://authority.example.test/synthetic");
            using var registration = scene.Cancelling ? token.Register(() =>
            {
                // No I/O, blocking or synchronous continuations in this callback.
                scene.httpNotification.TrySetResult(TimeProvider.System.GetTimestamp());
            }) : default;
            ProcessChild.Mark("http-send-entered");
            if (scene.Cancelling)
            {
                await scene.release.Task.WaitAsync(Limit);
                token.ThrowIfCancellationRequested();
            }
            return new HttpResponseMessage(HttpStatusCode.OK);
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                Require(Interlocked.Exchange(ref Disposed, 1) == 0);
                ProcessChild.Mark("http-dispose-entered");
                if (!scene.WorkDrained) ProcessChild.Mark("premature-http-disposal");
                if (!scene.Cancelling)
                {
                    ProcessChild.Mark("dispose-stall-entered");
                    Thread.Sleep(Limit);
                }
            }
            base.Dispose(disposing);
            if (disposing) ProcessChild.Mark("http-dispose-completed");
        }
    }

    private sealed class SyntheticAccount : IAccount
    {
        public string Username => ProcessChild.Email;
        public string Environment => "login.microsoftonline.com";
        public AccountId HomeAccountId { get; } = new(
            "55555555-6666-7777-8888-999999999999." + ProcessChild.Tenant,
            "55555555-6666-7777-8888-999999999999", ProcessChild.Tenant);
    }

    private static bool Marked(string name) => File.Exists(Path.Combine(Path.GetTempPath(), name));
    private static TaskCompletionSource NewSignal() => new(TaskCreationOptions.RunContinuationsAsynchronously);
    private static void Require(bool condition)
    {
        if (!condition) throw new InvalidOperationException("Synthetic shared HTTP prerequisite failed.");
    }
}
