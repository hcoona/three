using System.Text;
using System.Text.Json;
using Authentication.Core;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Authentication.Scenarios;

// Synthetic application-boundary cases. Windows file/pipe semantics and real WAM
// require their own evidence. Finite waits detect stuck fixtures, not host shutdown.
[TestClass]
public sealed class ApplicationScenarios
{
    private const string ProfilePath = @"C:\synthetic\selected-profile.json";
    private const string PersonalEmail = "personal@example.test";
    private const string WorkEmail = "work@example.test";
    private const string ClientId = "aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee";
    private const string Tenant = "11111111-2222-3333-4444-555555555555";
    private const string Scope = "499b84ac-1321-427f-aa17-267ca6975798/user_impersonation";
    // Failure-only harness bound: passing runs never wait for it. Shared CI runners can
    // delay thread-pool and timer callbacks by seconds under parallel scenarios.
    private static readonly TimeSpan HarnessLimit = TimeSpan.FromSeconds(30);
    private const string ProfileJson = """
        {
          "schemaVersion": 1,
          "name": "synthetic-selected-profile",
          "clientId": "aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee",
          "cloud": "public",
          "tenantPolicy": { "kind": "multitenant" },
          "integration": "windows-wam",
          "registration": {
            "owner": "Synthetic external owner",
            "externalDependency": true,
            "supportNotice": "Synthetic configuration; no support commitment."
          }
        }
        """;

    [TestMethod]
    [DataRow("absent-version")]
    [DataRow("unsupported-version")]
    [DataRow("absent-profile")]
    [DataRow("invalid-timeout")]
    [DataRow("unknown-option")]
    public async Task InvalidInvocationUsesBootstrapFailureWithoutReadingAProfile(string defect)
    {
        var arguments = Arguments();
        switch (defect)
        {
            case "absent-version": arguments.RemoveRange(arguments.IndexOf("--protocol"), 2); break;
            case "unsupported-version": arguments[arguments.IndexOf("--protocol") + 1] = "999"; break;
            case "absent-profile": arguments.RemoveRange(arguments.IndexOf("--profile"), 2); break;
            case "invalid-timeout": arguments.AddRange(["--timeout-seconds", "0"]); break;
            case "unknown-option": arguments.AddRange(["--client-secret", "synthetic-private-marker"]); break;
        }
        var scene = new Scene();
        using var invocation = scene.Create(arguments);
        await RunAsync(invocation, scene);

        AssertFailure(Commit(invocation), "invalid_request", "invalid_request");
        Assert.IsEmpty(scene.Reads);
        Assert.IsEmpty(scene.Effects);
    }

    [TestMethod]
    [DataRow("read-error")]
    [DataRow("access-denied")]
    [DataRow("invalid-utf8")]
    [DataRow("duplicate-field")]
    public async Task UnusableSelectedProfileFailsSafelyBeforeProviderConstruction(string defect)
    {
        var scene = new Scene();
        var arguments = Arguments();
        switch (defect)
        {
            case "read-error": scene.ReadError = new IOException("synthetic-private-marker"); break;
            case "access-denied": scene.ReadError = new UnauthorizedAccessException("synthetic-private-marker"); break;
            case "invalid-utf8": scene.ProfileBytes = [0xff]; break;
            case "duplicate-field":
                scene.ProfileBytes = Encoding.UTF8.GetBytes(ProfileJson.Replace("\"schemaVersion\": 1,", "\"schemaVersion\": 1, \"schemaVersion\": 1,"));
                break;
        }
        using var invocation = scene.Create(arguments);
        await RunAsync(invocation, scene);

        AssertFailure(Commit(invocation), "invalid_request", "invalid_configuration");
        CollectionAssert.AreEqual(new[] { ProfilePath }, scene.Reads);
        Assert.IsEmpty(scene.Effects);
    }

    [TestMethod]
    [DataRow("common")]
    [DataRow("99999999-8888-7777-6666-555555555555")]
    public async Task TenantConflictWithAFixedProfileHasItsOwnSafeReason(string selector)
    {
        var scene = new Scene { ProfileBytes = FixedProfile() };
        var arguments = Arguments();
        arguments.AddRange(["--tenant", selector]);
        using var invocation = scene.Create(arguments);
        await RunAsync(invocation, scene);

        AssertFailure(Commit(invocation), "invalid_request", "tenant_conflict");
        CollectionAssert.AreEqual(new[] { ProfilePath }, scene.Reads);
        Assert.IsEmpty(scene.Effects);
    }

    [TestMethod]
    [DataRow("personal")]
    [DataRow("fixed-work")]
    [DataRow("explicit-work")]
    public async Task SelectedProfileAndIntentReachTheSelectedAccountRequest(string kind)
    {
        var scene = new Scene();
        var email = kind == "personal" ? PersonalEmail : WorkEmail;
        var arguments = Arguments(email);
        if (kind == "fixed-work") scene.ProfileBytes = FixedProfile();
        if (kind == "explicit-work") arguments.AddRange(["--tenant", Tenant]);
        using var invocation = scene.Create(arguments);
        AuthenticationRequest? initializationRequest = null;
        await invocation.RunAsync(scene, (profile, request) =>
        {
            initializationRequest = request;
            return scene.CreateProvider(profile);
        }).WaitAsync(HarnessLimit);
        await invocation.CompleteAsync().WaitAsync(HarnessLimit);

        using var json = AssertSuccess(Commit(invocation), email);
        Assert.AreEqual(Tenant, json.RootElement.GetProperty("tenantId").GetString());
        Assert.AreEqual("silent", json.RootElement.GetProperty("interaction").GetString());
        Assert.IsNotNull(scene.BoundProfile);
        Assert.AreEqual(new Guid(ClientId), scene.BoundProfile.ClientId);
        Assert.AreEqual("windows-wam", scene.BoundProfile.Integration);
        Assert.AreEqual("Synthetic external owner", scene.BoundProfile.RegistrationOwner);
        Assert.IsNotNull(scene.ProviderRequest);
        Assert.AreEqual(email, scene.ProviderRequest.AccountEmail);
        CollectionAssert.AreEquivalent(new[] { Scope }, scene.ProviderRequest.Scopes.ToArray());
        Assert.IsFalse(scene.ProviderRequest.InteractionAllowed);
        Assert.AreEqual(kind == "personal" ? (Guid?)null : new Guid(Tenant), scene.ProviderRequest.ExactTenant);
        Assert.IsNotNull(initializationRequest);
        Assert.AreEqual(email, initializationRequest.AccountEmail);
        CollectionAssert.AreEquivalent(new[] { Scope }, initializationRequest.Scopes.ToArray());
        Assert.IsFalse(initializationRequest.InteractionAllowed);
        Assert.AreEqual(kind == "personal" ? (Guid?)null : new Guid(Tenant), initializationRequest.ExactTenant);
        CollectionAssert.AreEqual(new[] { ProfilePath }, scene.Reads);
    }

    [TestMethod]
    public async Task ForcedIntentReachesProviderConstructionAndTheInteractiveResult()
    {
        var scene = new Scene();
        var arguments = Arguments();
        arguments[arguments.IndexOf("--interaction") + 1] = "force-interactive";
        using var invocation = scene.Create(arguments);
        AuthenticationRequest? initialization = null;
        await invocation.RunAsync(scene, (profile, request) =>
        {
            initialization = request;
            return scene.CreateProvider(profile);
        }).WaitAsync(HarnessLimit);
        await invocation.CompleteAsync().WaitAsync(HarnessLimit);

        using var json = AssertSuccess(Commit(invocation), PersonalEmail);
        Assert.AreEqual("interactive", json.RootElement.GetProperty("interaction").GetString());
        Assert.IsNotNull(initialization);
        Assert.IsTrue(initialization.ForceInteractive);
        Assert.IsTrue(initialization.InteractionAllowed);
        Assert.AreEqual(initialization, scene.ProviderRequest);
        CollectionAssert.AreEqual(new[] { "construct", "discover", "open", "interactive", "close" }, scene.Effects.ToArray());
    }

    [TestMethod]
    public async Task ForcedInteractionConsumesTheOriginalEntryBudgetAndWithholdsLateSuccess()
    {
        var scene = new Scene { ReadTime = TimeSpan.FromSeconds(60), InteractiveTime = TimeSpan.FromSeconds(31) };
        var arguments = Arguments();
        arguments[arguments.IndexOf("--interaction") + 1] = "force-interactive";
        var entry = scene.Clock.GetTimestamp();
        scene.Clock.Advance(TimeSpan.FromSeconds(30));
        using var invocation = scene.Create(arguments, entry: entry);
        await RunAsync(invocation, scene);

        AssertFailure(Commit(invocation), "timeout");
        CollectionAssert.AreEqual(new[] { "construct", "discover", "open", "interactive", "close" }, scene.Effects.ToArray());
    }

    [TestMethod]
    public async Task ChangingTheBackingProfileDoesNotChangeTheAdmittedRequest()
    {
        var scene = new Scene();
        scene.OnConstruct = () => Array.Fill(scene.ProfileBytes, (byte)'!');
        using var invocation = scene.Create(Arguments());
        await RunAsync(invocation, scene);

        using var json = AssertSuccess(Commit(invocation), PersonalEmail);
        Assert.IsNotNull(scene.BoundProfile);
        Assert.AreEqual(new Guid(ClientId), scene.BoundProfile.ClientId);
        Assert.AreEqual("synthetic-selected-profile", scene.BoundProfile.Name);
        CollectionAssert.AreEqual(new[] { ProfilePath }, scene.Reads);
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task CancelledOrExpiredAdmissionDoesNotReadAProfile(bool deadline)
    {
        using var caller = new CancellationTokenSource();
        var scene = new Scene();
        var entry = scene.Clock.GetTimestamp();
        if (deadline) scene.Clock.Advance(TimeSpan.FromSeconds(120));
        else caller.Cancel();
        using var invocation = scene.Create(Arguments(), caller.Token, entry);
        await RunAsync(invocation, scene);

        AssertFailure(Commit(invocation), deadline ? "timeout" : "cancelled");
        Assert.IsEmpty(scene.Reads);
        Assert.IsEmpty(scene.Effects);
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task PendingProfileReadCannotDelayCancellationOrStartLateAuthentication(bool deadline)
    {
        using var caller = new CancellationTokenSource();
        var scene = new Scene { HoldProfile = true };
        using var invocation = scene.Create(Arguments(), caller.Token);
        var run = invocation.RunAsync(scene, scene.CreateProvider);
        try
        {
            await scene.ReadStarted.Task.WaitAsync(HarnessLimit);
            if (deadline) scene.Clock.Advance(TimeSpan.FromSeconds(120));
            else caller.Cancel();
            await run.WaitAsync(HarnessLimit);
            AssertFailure(Commit(invocation), deadline ? "timeout" : "cancelled");
            Assert.IsTrue(scene.ReadToken.IsCancellationRequested);
            Assert.IsFalse(invocation.OperationCompletion.IsCompleted);
            Assert.IsEmpty(scene.Effects);
        }
        finally
        {
            scene.ReleaseRead.TrySetResult();
            await invocation.OperationCompletion.WaitAsync(HarnessLimit);
            await run.WaitAsync(HarnessLimit);
        }

        Assert.IsEmpty(scene.Effects);
        Assert.IsFalse(invocation.TryCommitResult(out var late));
        Assert.IsNull(late);
    }

    [TestMethod]
    public async Task ProfileAndProviderTimeConsumeTheOriginalEntryBudget()
    {
        var scene = new Scene
        {
            ReadTime = TimeSpan.FromSeconds(60),
            ProviderTime = TimeSpan.FromSeconds(31),
        };
        var entry = scene.Clock.GetTimestamp();
        scene.Clock.Advance(TimeSpan.FromSeconds(30));
        using var invocation = scene.Create(Arguments(), entry: entry);
        await RunAsync(invocation, scene);

        AssertFailure(Commit(invocation), "timeout");
        Assert.IsTrue(scene.Effects.Contains("silent"));
    }

    [TestMethod]
    public async Task CallerTimeoutAppliesDuringProfileReading()
    {
        var scene = new Scene { ReadTime = TimeSpan.FromSeconds(2) };
        var arguments = Arguments();
        arguments.AddRange(["--timeout-seconds", "1"]);
        using var invocation = scene.Create(arguments);
        await RunAsync(invocation, scene);

        AssertFailure(Commit(invocation), "timeout");
        Assert.IsEmpty(scene.Effects);
    }

    [TestMethod]
    [DataRow(AuthenticationFailure.Timeout)]
    [DataRow(AuthenticationFailure.InvalidRequest)]
    [DataRow((AuthenticationFailure)999)]
    public async Task ProviderConstructionCannotReportEngineOwnedOrUndefinedOutcomes(AuthenticationFailure reported)
    {
        var scene = new Scene
        {
            OnConstruct = () => throw new ProviderFailureException(reported,
                reason: AuthenticationReason.InvalidConfiguration),
        };
        using var invocation = scene.Create(Arguments());
        await RunAsync(invocation, scene);

        AssertFailure(Commit(invocation), "internal_failure");
        Assert.IsFalse(scene.Effects.Contains("discover"));
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task ProviderConstructionFailureUsesOnlyItsSafeCategory(bool unavailable)
    {
        var scene = new Scene
        {
            OnConstruct = () =>
            {
                if (unavailable) throw new ProviderFailureException(AuthenticationFailure.MechanismUnavailable);
                throw new InvalidOperationException("synthetic-private-marker");
            },
        };
        using var invocation = scene.Create(Arguments());
        await RunAsync(invocation, scene);

        AssertFailure(Commit(invocation), unavailable ? "mechanism_unavailable" : "internal_failure");
        Assert.IsFalse(scene.Effects.Contains("discover"));
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task SilentChallengeUsesOnlyTheCallersInteractionPermission(bool allowed)
    {
        var scene = new Scene { Challenge = true };
        using var invocation = scene.Create(Arguments(interaction: allowed));
        await RunAsync(invocation, scene);
        var result = Commit(invocation);

        if (allowed)
        {
            using var json = AssertSuccess(result, PersonalEmail);
            Assert.AreEqual("interactive", json.RootElement.GetProperty("interaction").GetString());
            Assert.IsTrue(scene.Effects.Contains("silent"));
            Assert.IsTrue(scene.Effects.Contains("open"));
            Assert.IsTrue(scene.Effects.Contains("interactive"));
            Assert.IsTrue(scene.Effects.Contains("close"));
            Assert.IsTrue(scene.Effects.IndexOf("silent") < scene.Effects.IndexOf("open"));
            Assert.IsTrue(scene.Effects.IndexOf("open") < scene.Effects.IndexOf("interactive"));
            Assert.IsTrue(scene.Effects.IndexOf("interactive") < scene.Effects.IndexOf("close"));
        }
        else
        {
            AssertFailure(result, "interaction_required", "consent_required");
            Assert.IsFalse(scene.Effects.Any(effect => effect is "open" or "interactive"));
        }
    }

    [TestMethod]
    public async Task CancellationBeforeApplicationCommitSuppressesValidatedSuccess()
    {
        using var caller = new CancellationTokenSource();
        var scene = new Scene();
        using var invocation = scene.Create(Arguments(), caller.Token);
        var provisional = await invocation.RunAsync(scene, scene.CreateProvider).WaitAsync(HarnessLimit);
        Assert.IsNotNull(provisional.Success);
        caller.Cancel();

        AssertFailure(Commit(invocation), "cancelled");
        Assert.IsFalse(invocation.TryCommitResult(out var duplicate));
        Assert.IsNull(duplicate);
    }

    private static List<string> Arguments(string email = PersonalEmail, bool interaction = false) =>
    [
        "authenticate", "--protocol", "1", "--profile", ProfilePath,
        "--account-email", email, "--scope", Scope,
        "--interaction", interaction ? "interactive-if-needed" : "non-interactive-only",
    ];

    private static byte[] FixedProfile() => Encoding.UTF8.GetBytes(ProfileJson.Replace(
        "\"kind\": \"multitenant\"", $"\"kind\": \"single-tenant\", \"tenantId\": \"{Tenant}\""));

    private static async Task RunAsync(RequestInvocation invocation, Scene scene)
    {
        await invocation.RunAsync(scene, scene.CreateProvider).WaitAsync(HarnessLimit);
        await invocation.OperationCompletion.WaitAsync(HarnessLimit);
    }

    private static SerializedResult Commit(RequestInvocation invocation)
    {
        Assert.IsTrue(invocation.TryCommitResult(out var result));
        Assert.IsNotNull(result);
        return result;
    }

    private static void AssertFailure(SerializedResult result, string outcome, string? reason = null)
    {
        using var json = JsonDocument.Parse(result.Utf8Json);
        var root = json.RootElement;
        Assert.AreEqual(outcome, root.GetProperty("outcome").GetString());
        Assert.AreEqual(reason ?? outcome, root.GetProperty("reason").GetString());
        Assert.AreEqual(1, root.GetProperty("protocol").GetInt32());
        Assert.AreEqual(1, result.ExitCode);
        CollectionAssert.AreEquivalent(new[] { "protocol", "outcome", "reason" },
            root.EnumerateObject().Select(property => property.Name).ToArray());
        Assert.IsFalse(Encoding.UTF8.GetString(result.Utf8Json).Contains("synthetic-private-marker", StringComparison.Ordinal));
    }

    private static JsonDocument AssertSuccess(SerializedResult result, string email)
    {
        Assert.AreEqual(0, result.ExitCode);
        var json = JsonDocument.Parse(result.Utf8Json);
        Assert.AreEqual("success", json.RootElement.GetProperty("outcome").GetString());
        Assert.AreEqual(email, json.RootElement.GetProperty("accountEmail").GetString());
        Assert.AreEqual("synthetic-selected-token", json.RootElement.GetProperty("accessToken").GetString());
        return json;
    }

    private sealed class Scene : IProfileSource, IAuthenticationProvider, IRequestHost
    {
        public ApplicationClock Clock { get; } = new();
        TimeProvider IRequestHost.Clock => Clock;
        public byte[] ProfileBytes { get; set; } = Encoding.UTF8.GetBytes(ProfileJson);
        public Exception? ReadError { get; set; }
        public bool HoldProfile { get; init; }
        public bool Challenge { get; init; }
        public TimeSpan ReadTime { get; init; }
        public TimeSpan ProviderTime { get; init; }
        public TimeSpan InteractiveTime { get; init; }
        public Action? OnConstruct { get; set; }
        public List<string> Reads { get; } = [];
        public List<string> Effects { get; } = [];
        public ClientProfile? BoundProfile { get; private set; }
        public AuthenticationRequest? ProviderRequest { get; private set; }
        public CancellationToken ReadToken { get; private set; }
        public TaskCompletionSource ReadStarted { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource ReleaseRead { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public RequestInvocation Create(IReadOnlyList<string> arguments,
            CancellationToken token = default, long? entry = null) =>
            new(arguments, this, entry ?? Clock.GetTimestamp(), token);

        public async Task<ReadOnlyMemory<byte>> ReadAsync(string path, CancellationToken cancellationToken)
        {
            Reads.Add(path);
            ReadToken = cancellationToken;
            ReadStarted.TrySetResult();
            if (HoldProfile) await ReleaseRead.Task;
            Clock.Advance(ReadTime);
            if (ReadError is not null) throw ReadError;
            return ProfileBytes;
        }

        public IAuthenticationProvider CreateProvider(ClientProfile profile)
        {
            Effects.Add("construct");
            BoundProfile = profile;
            OnConstruct?.Invoke();
            return this;
        }

        public Task<IReadOnlyList<ProviderAccount>> GetAccountsAsync(CancellationToken cancellationToken)
        {
            Effects.Add("discover");
            return Task.FromResult<IReadOnlyList<ProviderAccount>>([
                new(WorkEmail, new object()), new(PersonalEmail, new object()),
            ]);
        }

        public Task<TokenCandidate> AcquireSilentAsync(AuthenticationRequest request,
            ProviderAccount account, Guid operationId, CancellationToken cancellationToken)
        {
            Effects.Add("silent");
            ProviderRequest = request;
            Clock.Advance(ProviderTime);
            if (Challenge) throw new ProviderFailureException(AuthenticationFailure.InteractionRequired,
                reason: AuthenticationReason.ConsentRequired);
            return Task.FromResult(Candidate(request, account.Email, operationId));
        }

        public Task<TokenCandidate> AcquireInteractiveAsync(AuthenticationRequest request,
            ProviderAccount? account, string? claims, nint parentWindow, Guid operationId,
            CancellationToken cancellationToken)
        {
            Effects.Add("interactive");
            ProviderRequest = request;
            Clock.Advance(InteractiveTime);
            return Task.FromResult(Candidate(request, account?.Email, operationId));
        }

        public Task<nint> OpenInteractionAsync(CancellationToken cancellationToken)
        {
            Effects.Add("open");
            return Task.FromResult<nint>(42);
        }

        public Task CloseInteractionAsync() { Effects.Add("close"); return Task.CompletedTask; }

        private TokenCandidate Candidate(AuthenticationRequest request, string? email, Guid operationId) =>
            new("synthetic-selected-token", email, request.ExactTenant ?? new Guid(Tenant),
                request.Scopes, "Bearer", Clock.GetUtcNow().AddMinutes(10), operationId);
    }

    private sealed class ApplicationClock : TimeProvider
    {
        private readonly object gate = new();
        private readonly List<ManualTimer> timers = [];
        private long timestamp;
        private DateTimeOffset utc = new(2026, 9, 13, 0, 0, 0, TimeSpan.Zero);
        public override long TimestampFrequency => TimeSpan.TicksPerSecond;
        public override long GetTimestamp() { lock (gate) return timestamp; }
        public override DateTimeOffset GetUtcNow() { lock (gate) return utc; }
        public void ShiftUtc(TimeSpan delta) { lock (gate) utc += delta; }

        public void Advance(TimeSpan delta, bool dispatchTimers = true)
        {
            List<ManualTimer> due;
            lock (gate)
            {
                timestamp += delta.Ticks;
                utc += delta;
                due = dispatchTimers ? timers.Where(timer => timer.TakeDue(timestamp)).ToList() : [];
            }

            foreach (var timer in due) timer.Fire();
        }

        public override ITimer CreateTimer(TimerCallback callback, object? state, TimeSpan dueTime, TimeSpan period)
        {
            lock (gate)
            {
                var timer = new ManualTimer(this, callback, state);
                timers.Add(timer);
                timer.Change(dueTime, period);
                return timer;
            }
        }

        private sealed class ManualTimer(ApplicationClock clock, TimerCallback callback, object? state) : ITimer
        {
            private long? due;
            private bool disposed;
            public bool Change(TimeSpan dueTime, TimeSpan period)
            {
                if (period != global::System.Threading.Timeout.InfiniteTimeSpan) throw new NotSupportedException("One-shot fixture timers only.");
                lock (clock.gate)
                {
                    if (disposed) return false;
                    due = dueTime == global::System.Threading.Timeout.InfiniteTimeSpan ? null : clock.timestamp + dueTime.Ticks;
                    return true;
                }
            }

            public bool TakeDue(long now)
            {
                if (disposed || due is null || due > now) return false;
                due = null;
                return true;
            }

            public void Fire() => callback(state);
            public void Dispose() { lock (clock.gate) { disposed = true; due = null; } }
            public ValueTask DisposeAsync() { Dispose(); return ValueTask.CompletedTask; }
        }
    }
}
