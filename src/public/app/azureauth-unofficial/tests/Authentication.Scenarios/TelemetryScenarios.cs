using System.Collections.Concurrent;
using System.Diagnostics;
using System.Text;
using System.Text.Json;
using Authentication.Core;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Authentication.Scenarios;

[TestClass]
[DoNotParallelize]
public sealed class TelemetryScenarios
{
    // Failure-only harness bound shared with the other scenario suites; a passing
    // case completes far sooner, and a slow shared runner must not turn it red.
    private static readonly TimeSpan Limit = TimeSpan.FromSeconds(30);
    private const string PrivateMarker = "SYNTHETIC_PRIVATE_OBSERVATION_MARKER";
    private const string Email = "personal@example.test";
    private const string Scope = "499b84ac-1321-427f-aa17-267ca6975798/user_impersonation";

    [TestMethod]
    public async Task WaitingAcquisitionIsObservableBeforeItCompletes()
    {
        var messages = new ConcurrentQueue<byte[]>();
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var observer = new LocalActivityTelemetry(TimeProvider.System.GetTimestamp(), bytes =>
        {
            messages.Enqueue(bytes);
            using var json = JsonDocument.Parse(bytes);
            if (json.RootElement.GetProperty("event").GetString() == "stage_started"
                && json.RootElement.GetProperty("stage").GetString() == "silent_acquisition") started.TrySetResult();
            return true;
        });
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var provider = new Provider(release.Task);
        var acquisition = new RequestCoordinator(provider, new Host()).AuthenticateAsync(Request());
        try
        {
            await started.Task.WaitAsync(Limit);
            Assert.IsFalse(acquisition.IsCompleted, "A pending span needs an immediate start observation.");
        }
        finally { release.TrySetResult(); }
        var outcome = await acquisition.WaitAsync(Limit);
        Assert.IsNotNull(outcome.Success);
        observer.Dispose();
        await observer.Completion.WaitAsync(Limit);
        var records = messages.Select(bytes => JsonDocument.Parse(bytes)).ToArray();
        try
        {
            var root = records.Single(record => Text(record, "event") == "stage_started"
                && Text(record, "stage") == "request").RootElement;
            var silent = records.Single(record => Text(record, "event") == "stage_started"
                && Text(record, "stage") == "silent_acquisition").RootElement;
            Assert.AreEqual(root.GetProperty("traceId").GetString(), silent.GetProperty("traceId").GetString());
            Assert.AreEqual(root.GetProperty("spanId").GetString(), silent.GetProperty("parentSpanId").GetString());
            Assert.IsTrue(records.Any(record => Text(record, "event") == "stage_completed"
                && Text(record, "stage") == "silent_acquisition"));
        }
        finally { foreach (var record in records) record.Dispose(); }
    }

    [TestMethod]
    public async Task OwnedSpansDoNotBecomeProviderAmbientContextOrExposeArbitraryAttributes()
    {
        using var previous = new Activity("synthetic-caller").Start();
        previous.AddBaggage("private", PrivateMarker);
        using var adversarial = new ActivityListener
        {
            ShouldListenTo = source => source.Name == AuthenticationTrace.SourceName,
            Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllData,
            ActivityStarted = activity =>
            {
                activity.SetTag("private", PrivateMarker);
                activity.SetTag("error.type", activity.OperationName == "request" ? "msal_client" : PrivateMarker);
                activity.AddBaggage("private", PrivateMarker);
                activity.DisplayName = PrivateMarker;
                activity.TraceStateString = PrivateMarker;
                activity.SetStatus(ActivityStatusCode.Error, PrivateMarker);
            },
        };
        ActivitySource.AddActivityListener(adversarial);
        var messages = new ConcurrentQueue<byte[]>();
        using var observer = new LocalActivityTelemetry(TimeProvider.System.GetTimestamp(), bytes =>
        { messages.Enqueue(bytes); return true; });
        var provider = new Provider(Task.CompletedTask) { ExpectedAmbient = previous };
        var outcome = await new RequestCoordinator(provider, new Host()).AuthenticateAsync(Request()).WaitAsync(Limit);
        Assert.IsNotNull(outcome.Success);
        Assert.IsTrue(provider.AmbientPreserved);
        Assert.AreSame(previous, Activity.Current);
        using (var spoof = new ActivitySource(AuthenticationTrace.SourceName))
        using (spoof.StartActivity("silent_acquisition")) { }
        observer.Dispose();
        await observer.Completion.WaitAsync(Limit);
        var text = Encoding.UTF8.GetString(messages.SelectMany(bytes => bytes).ToArray());
        Assert.IsFalse(text.Contains(PrivateMarker, StringComparison.Ordinal));
        Assert.IsFalse(text.Contains(Email, StringComparison.Ordinal));
        Assert.IsFalse(text.Contains(Scope, StringComparison.Ordinal));
        Assert.IsFalse(text.Contains("SYNTHETIC_OPAQUE_TOKEN", StringComparison.Ordinal));
        foreach (var bytes in messages)
        {
            using var json = JsonDocument.Parse(bytes);
            var root = json.RootElement;
            var allowed = new HashSet<string>(["event", "stage", "traceId", "spanId", "parentSpanId",
                "elapsedMilliseconds", "durationMilliseconds", "status"]);
            Assert.IsTrue(root.EnumerateObject().All(property => allowed.Contains(property.Name)));
            if (root.GetProperty("stage").GetString() == "request")
                Assert.AreEqual(new string('0', 16), root.GetProperty("parentSpanId").GetString());
        }
        Assert.AreEqual(2, messages.Count(bytes =>
        {
            using var json = JsonDocument.Parse(bytes);
            return json.RootElement.GetProperty("stage").GetString() == "silent_acquisition";
        }), "A same-name foreign source must not enter the owned collector.");
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task ListenerAndSinkFailuresDoNotChangeAuthentication(bool throwOnStop)
    {
        using var broken = new ActivityListener
        {
            ShouldListenTo = source => source.Name == AuthenticationTrace.SourceName,
            Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllData,
            ActivityStarted = _ => { if (!throwOnStop) throw new InvalidOperationException(PrivateMarker); },
            ActivityStopped = _ => { if (throwOnStop) throw new InvalidOperationException(PrivateMarker); },
        };
        ActivitySource.AddActivityListener(broken);
        using var observer = new LocalActivityTelemetry(TimeProvider.System.GetTimestamp(), _ =>
            throw new IOException(PrivateMarker));
        var outcome = await new RequestCoordinator(new Provider(Task.CompletedTask), new Host())
            .AuthenticateAsync(Request()).WaitAsync(Limit);
        Assert.IsNotNull(outcome.Success);
        Assert.AreEqual(0, ResultProjection.Serialize(outcome).ExitCode);
        observer.Dispose();
        await observer.Completion.WaitAsync(Limit);
    }

    [TestMethod]
    public async Task OverflowKeepsFinalCapacityAndBlockedWriterDoesNotBlockAuthentication()
    {
        var messages = new ConcurrentQueue<byte[]>();
        using var observer = new LocalActivityTelemetry(TimeProvider.System.GetTimestamp(), bytes =>
        { messages.Enqueue(bytes); return true; });
        using (var trace = AuthenticationTrace.Start())
            for (var index = 0; index < 100; index++)
                using (trace.Begin(AuthenticationStage.AccountDiscovery)) { }
        var final = "Authentication request completed.\n"u8.ToArray();
        Assert.IsTrue(observer.TryWriteFinal(final));
        Assert.IsFalse(observer.TryWriteFinal(final));
        observer.Dispose();
        await observer.Completion.WaitAsync(Limit);
        Assert.IsTrue(messages.Sum(bytes => bytes.Length) <= 8192);
        CollectionAssert.AreEqual(final, messages.Last());

        using var entered = new ManualResetEventSlim();
        using var release = new ManualResetEventSlim();
        using var blocked = new LocalActivityTelemetry(TimeProvider.System.GetTimestamp(), _ =>
        { entered.Set(); release.Wait(); return true; });
        try
        {
            var outcome = await new RequestCoordinator(new Provider(Task.CompletedTask), new Host())
                .AuthenticateAsync(Request()).WaitAsync(Limit);
            Assert.IsNotNull(outcome.Success);
            Assert.IsTrue(entered.Wait(Limit));
            blocked.Dispose();
            Assert.IsFalse(blocked.Completion.IsCompleted);
        }
        finally
        {
            release.Set();
            await blocked.Completion.WaitAsync(Limit);
        }
    }

    [TestMethod]
    [DataRow("opaque")]
    [DataRow("duplicate")]
    [DataRow("oversized")]
    [DataRow("jwt")]
    public async Task InterpretationDoesNotChangeOpaqueTokenSuccessOrExposePayload(string kind)
    {
        var payload = kind == "duplicate" ? "{\"exp\":1,\"exp\":2}"
            : "{\"exp\":123,\"aud\":\"SYNTHETIC_PRIVATE_OBSERVATION_MARKER\",\"scp\":\"private\",\"email\":\"personal@example.test\"}";
        var token = kind == "opaque" ? "SYNTHETIC_OPAQUE_TOKEN"
            : kind == "oversized" ? new string('x', 65537) : Jwt(payload);
        var outcome = await new RequestCoordinator(new Provider(Task.CompletedTask) { Token = token }, new Host())
            .AuthenticateAsync(Request()).WaitAsync(Limit);
        Assert.IsNotNull(outcome.Success);
        var serialized = ResultProjection.Serialize(outcome);
        Assert.AreEqual(0, serialized.ExitCode);
        var interpretation = TokenDiagnostics.Describe(outcome.Success.AccessToken);
        Assert.AreEqual(kind switch
        {
            "jwt" => TokenDiagnosticFormat.DecodedUnverified,
            "oversized" => TokenDiagnosticFormat.LimitExceeded, _ => TokenDiagnosticFormat.Unreadable,
        }, interpretation.Format);
        Assert.IsFalse(interpretation.ToString().Contains(PrivateMarker, StringComparison.Ordinal));
        using var result = JsonDocument.Parse(serialized.Utf8Json);
        Assert.AreEqual(token, result.RootElement.GetProperty("accessToken").GetString());
    }

    [TestMethod]
    public async Task SinkThatStopsAcceptingEndsTheWriterWithoutChangingAuthentication()
    {
        var writes = 0;
        using var observer = new LocalActivityTelemetry(TimeProvider.System.GetTimestamp(), _ =>
        {
            Interlocked.Increment(ref writes);
            return false;
        });
        var outcome = await new RequestCoordinator(new Provider(Task.CompletedTask), new Host())
            .AuthenticateAsync(Request()).WaitAsync(Limit);

        Assert.IsNotNull(outcome.Success);
        Assert.AreEqual(0, ResultProjection.Serialize(outcome).ExitCode);
        await observer.Completion.WaitAsync(Limit);
        Assert.AreEqual(1, Volatile.Read(ref writes), "A refused write must end the local writer.");
        Assert.IsFalse(observer.TryWriteFinal("Authentication request completed.\n"u8.ToArray()));
    }

    [TestMethod]
    public async Task ExhaustedPhaseBudgetRefusesTheNoticeButKeepsRequestStopAndFinalCapacity()
    {
        var messages = new ConcurrentQueue<byte[]>();
        using var observer = new LocalActivityTelemetry(TimeProvider.System.GetTimestamp(), bytes =>
        { messages.Enqueue(bytes); return true; });
        using (var trace = AuthenticationTrace.Start())
        {
            for (var index = 0; index < 100; index++)
                using (trace.Begin(AuthenticationStage.AccountDiscovery)) { }
            // Larger than any one phase event, so it cannot fit the remainder they leave.
            Assert.IsFalse(observer.TryWriteNotice(new byte[400]), "The notice shares the exhausted phase allocation.");
        }
        var final = "Authentication request completed.\n"u8.ToArray();
        Assert.IsTrue(observer.TryWriteFinal(final));
        observer.Dispose();
        await observer.Completion.WaitAsync(Limit);

        Assert.IsTrue(messages.Sum(bytes => bytes.Length) <= 8192);
        CollectionAssert.AreEqual(final, messages.Last());
        var stops = messages.Where(bytes => bytes[0] == (byte)'{').Count(bytes =>
        {
            using var json = JsonDocument.Parse(bytes);
            return Text(json, "event") == "stage_completed" && Text(json, "stage") == "request";
        });
        Assert.AreEqual(1, stops, "The request span stop event keeps its reserved capacity.");
    }

    [TestMethod]
    [DataRow("{\"exp\":\"private\",\"aud\":123,\"scp\":false}")]
    [DataRow("{\"nested\":{\"a\":1,\"a\":2}}")]
    [DataRow("[]")]
    public void InvalidOrUnexpectedClaimsHaveOnlyFixedDiagnosticInterpretation(string payload)
    {
        var value = TokenDiagnostics.Describe(Jwt(payload));
        Assert.IsFalse(value.HasExpiration);
        Assert.IsFalse(value.HasAudience);
        Assert.IsFalse(value.HasScopes);
    }

    // Synthetic unsigned payloads only. Each expected claim, in its expected type, is
    // reported present; the interpretation still carries no claim value.
    [TestMethod]
    [DataRow("{\"exp\":1767225600,\"aud\":\"" + PrivateMarker + "\",\"scp\":\"synthetic.read\"}")]
    [DataRow("{\"exp\":1767225600,\"aud\":[\"" + PrivateMarker + "\"],\"scp\":\"synthetic.read\"}")]
    public void ExpectedClaimsInTheirExpectedTypesAreReportedPresent(string payload)
    {
        var value = TokenDiagnostics.Describe(Jwt(payload));
        Assert.AreEqual(TokenDiagnosticFormat.DecodedUnverified, value.Format);
        Assert.IsTrue(value.HasExpiration);
        Assert.IsTrue(value.HasAudience);
        Assert.IsTrue(value.HasScopes);
        Assert.IsFalse(value.ToString().Contains(PrivateMarker, StringComparison.Ordinal));
    }

    [TestMethod]
    [DataRow(ProviderDiagnosticError.QueryParameterConflict, "query_parameter_conflict", false)]
    [DataRow(ProviderDiagnosticError.BrokerContract, "broker_contract", false)]
    [DataRow(ProviderDiagnosticError.BrokerConfiguration, "broker_configuration", false)]
    [DataRow(ProviderDiagnosticError.MsalClient, "msal_client", false)]
    [DataRow(ProviderDiagnosticError.MsalService, "msal_service", false)]
    [DataRow(ProviderDiagnosticError.Argument, "argument", false)]
    [DataRow(ProviderDiagnosticError.InvalidOperation, "invalid_operation", false)]
    [DataRow(ProviderDiagnosticError.Unexpected, "unexpected", false)]
    [DataRow(ProviderDiagnosticError.MsalClient, "msal_client", true)]
    [DataRow(ProviderDiagnosticError.None, null, false)]
    [DataRow((ProviderDiagnosticError)999, null, false)]
    public async Task OnlyCommittedInternalFailureExportsFixedErrorType(
        ProviderDiagnosticError category, string? expected, bool constructionFailure)
    {
        var messages = new ConcurrentQueue<byte[]>();
        using var observer = new LocalActivityTelemetry(TimeProvider.System.GetTimestamp(), bytes =>
        { messages.Enqueue(bytes); return true; });
        var scene = new FailureScene(category);
        using var invocation = FailureInvocation(scene);
        await invocation.RunAsync(scene, _ => constructionFailure ? throw scene.Failure : scene).WaitAsync(Limit);
        Assert.IsTrue(invocation.TryCommitResult(out var result));
        Assert.IsNotNull(result);
        Assert.AreEqual(1, result.ExitCode);
        using var json = JsonDocument.Parse(result.Utf8Json);
        Assert.AreEqual("internal_failure", json.RootElement.GetProperty("outcome").GetString());
        Assert.IsFalse(Encoding.UTF8.GetString(result.Utf8Json).Contains("error.type", StringComparison.Ordinal));
        await invocation.CompleteAsync().WaitAsync(Limit);
        observer.Dispose();
        await observer.Completion.WaitAsync(Limit);
        var classified = 0;
        foreach (var bytes in messages)
        {
            Assert.IsFalse(Encoding.UTF8.GetString(bytes).Contains(PrivateMarker, StringComparison.Ordinal));
            using var record = JsonDocument.Parse(bytes);
            var root = record.RootElement;
            if (root.TryGetProperty("error.type", out var error))
            {
                classified++;
                Assert.AreEqual("stage_completed", root.GetProperty("event").GetString());
                Assert.AreEqual("request", root.GetProperty("stage").GetString());
                Assert.AreEqual("error", root.GetProperty("status").GetString());
                Assert.AreEqual(expected, error.GetString());
            }
        }
        Assert.AreEqual(expected is null ? 0 : 1, classified);
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task CancellationOrDeadlineSuppressesPendingProviderClassification(bool deadline)
    {
        var messages = new ConcurrentQueue<byte[]>();
        using var observer = new LocalActivityTelemetry(TimeProvider.System.GetTimestamp(), bytes =>
        { messages.Enqueue(bytes); return true; });
        using var caller = new CancellationTokenSource();
        var scene = new FailureScene(ProviderDiagnosticError.QueryParameterConflict) { HoldClose = true };
        using var invocation = FailureInvocation(scene, caller.Token);
        var run = invocation.RunAsync(scene, _ => scene);
        try
        {
            await scene.CloseEntered.Task.WaitAsync(Limit);
            if (deadline) scene.Clock.Advance(TimeSpan.FromSeconds(121));
            else caller.Cancel();
            scene.CloseRelease.TrySetResult();
            await run.WaitAsync(Limit);
            Assert.IsTrue(invocation.TryCommitResult(out var result));
            Assert.IsNotNull(result);
            using var json = JsonDocument.Parse(result.Utf8Json);
            Assert.AreEqual(deadline ? "timeout" : "cancelled",
                json.RootElement.GetProperty("outcome").GetString());
        }
        finally
        {
            scene.CloseRelease.TrySetResult();
            await run.WaitAsync(Limit);
            await invocation.CompleteAsync().WaitAsync(Limit);
            observer.Dispose();
            await observer.Completion.WaitAsync(Limit);
        }
        Assert.IsTrue(messages.All(bytes =>
        {
            using var json = JsonDocument.Parse(bytes);
            return !json.RootElement.TryGetProperty("error.type", out _);
        }));
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task FailureClassificationDoesNotDependOnTelemetry(bool brokenSink)
    {
        using var observer = brokenSink
            ? new LocalActivityTelemetry(TimeProvider.System.GetTimestamp(), _ => throw new IOException(PrivateMarker))
            : null;
        var scene = new FailureScene(ProviderDiagnosticError.BrokerContract);
        using var invocation = FailureInvocation(scene);
        await invocation.RunAsync(scene, _ => scene).WaitAsync(Limit);
        Assert.IsTrue(invocation.TryCommitResult(out var result));
        Assert.IsNotNull(result);
        using var json = JsonDocument.Parse(result.Utf8Json);
        Assert.AreEqual("internal_failure", json.RootElement.GetProperty("outcome").GetString());
        await invocation.CompleteAsync().WaitAsync(Limit);
        if (observer is not null)
        {
            observer.Dispose();
            await observer.Completion.WaitAsync(Limit);
        }
    }

    private static RequestInvocation FailureInvocation(FailureScene scene, CancellationToken token = default) =>
        new(["authenticate", "--protocol", "1", "--profile", @"C:\synthetic\selected-profile.json",
            "--account-email", Email, "--scope", Scope, "--interaction", "force-interactive"],
            scene, scene.Clock.GetTimestamp(), token);

    private sealed class FailureScene(ProviderDiagnosticError category) : IProfileSource, IAuthenticationProvider, IRequestHost
    {
        public ProviderFailureException Failure { get; } = new(AuthenticationFailure.InternalFailure,
            claims: PrivateMarker, diagnosticError: category);
        public ScenarioClock Clock { get; } = new();
        TimeProvider IRequestHost.Clock => Clock;
        public bool HoldClose { get; init; }
        public TaskCompletionSource CloseEntered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource CloseRelease { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public Task<ReadOnlyMemory<byte>> ReadAsync(string path, CancellationToken token) =>
            Task.FromResult<ReadOnlyMemory<byte>>(Encoding.UTF8.GetBytes("""
                {"schemaVersion":1,"name":"synthetic-selected-profile",
                 "clientId":"aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee","cloud":"public",
                 "tenantPolicy":{"kind":"multitenant"},"integration":"windows-wam",
                 "registration":{"owner":"Synthetic external owner","externalDependency":true,
                 "supportNotice":"Synthetic configuration; no support commitment."}}
                """));
        public Task<IReadOnlyList<ProviderAccount>> GetAccountsAsync(CancellationToken token) =>
            Task.FromResult<IReadOnlyList<ProviderAccount>>([]);
        public Task<TokenCandidate> AcquireSilentAsync(AuthenticationRequest request, ProviderAccount account,
            Guid operationId, CancellationToken token) => throw new AssertFailedException("Forced intent must skip silent acquisition.");
        public Task<TokenCandidate> AcquireInteractiveAsync(AuthenticationRequest request, ProviderAccount? account,
            string? claims, nint parentWindow, Guid operationId, CancellationToken token) =>
            Task.FromException<TokenCandidate>(Failure);
        public Task<nint> OpenInteractionAsync(CancellationToken token) => Task.FromResult<nint>(42);
        public async Task CloseInteractionAsync()
        {
            CloseEntered.TrySetResult();
            if (HoldClose) await CloseRelease.Task;
        }
    }

    private sealed class ScenarioClock : TimeProvider
    {
        private long timestamp;
        public override long GetTimestamp() => Interlocked.Read(ref timestamp);
        public override long TimestampFrequency => TimeSpan.TicksPerSecond;
        public void Advance(TimeSpan elapsed) => Interlocked.Add(ref timestamp, elapsed.Ticks);
    }

    private static string Jwt(string payload) => "e30." + Convert.ToBase64String(Encoding.UTF8.GetBytes(payload))
        .TrimEnd('=').Replace('+', '-').Replace('/', '_') + ".c3ludGhldGlj";
    private static string? Text(JsonDocument document, string property) => document.RootElement.GetProperty(property).GetString();
    private static AuthenticationRequest Request() => new(Email, [Scope], false, Guid.Parse("11111111-2222-3333-4444-555555555555"));
    private sealed class Host : IRequestHost
    {
        public TimeProvider Clock => TimeProvider.System;
        public Task<nint> OpenInteractionAsync(CancellationToken cancellationToken) => Task.FromResult<nint>(0);
        public Task CloseInteractionAsync() => Task.CompletedTask;
    }

    private sealed class Provider(Task release) : IAuthenticationProvider
    {
        private readonly object handle = new();
        public string Token { get; init; } = "SYNTHETIC_OPAQUE_TOKEN";
        public Activity? ExpectedAmbient { get; init; }
        public bool AmbientPreserved { get; private set; } = true;
        public Task<IReadOnlyList<ProviderAccount>> GetAccountsAsync(CancellationToken cancellationToken)
        {
            AmbientPreserved &= ReferenceEquals(ExpectedAmbient, Activity.Current);
            return Task.FromResult<IReadOnlyList<ProviderAccount>>([new(Email, handle)]);
        }
        public async Task<TokenCandidate> AcquireSilentAsync(AuthenticationRequest request,
            ProviderAccount account, Guid operationId, CancellationToken cancellationToken)
        {
            AmbientPreserved &= ReferenceEquals(ExpectedAmbient, Activity.Current);
            await release.WaitAsync(cancellationToken);
            return new(Token, Email, request.ExactTenant, [Scope], "Bearer", DateTimeOffset.UtcNow.AddMinutes(10), operationId);
        }

        public Task<TokenCandidate> AcquireInteractiveAsync(AuthenticationRequest request, ProviderAccount? account,
            string? claims, nint parentWindow, Guid operationId, CancellationToken cancellationToken) =>
            Task.FromException<TokenCandidate>(new ProviderFailureException(AuthenticationFailure.MechanismUnavailable));
    }
}
