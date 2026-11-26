using System.Globalization;
using Authentication.Core;

namespace Authentication.Windows.Scenarios;

// Only this test executable has a child selector. The production executable has
// no test flags or environment-selected provider. Markers contain fixed text.
internal static class ProcessChild
{
    internal const string Email = "personal@example.test";
    internal const string Scope = "499b84ac-1321-427f-aa17-267ca6975798/user_impersonation";
    internal const string Tenant = "11111111-2222-3333-4444-555555555555";
    internal const string Token = "SYNTHETIC_PROCESS_SCENARIO_TOKEN";
    internal const string Profile = """
        {"schemaVersion":1,"name":"synthetic-process-profile",
         "clientId":"aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee","cloud":"public",
         "tenantPolicy":{"kind":"multitenant"},"integration":"windows-wam",
         "registration":{"owner":"Synthetic external owner","externalDependency":true,
         "supportNotice":"Synthetic configuration; no support commitment."}}
        """;

    internal static int Run(string scenario, string[] arguments, long entry)
    {
        if (NativeProcessChild.Supports(scenario)) return NativeProcessChild.Run(scenario, arguments, entry);
        if (DefaultHttpProcessChild.Supports(scenario)) return DefaultHttpProcessChild.Run(scenario, arguments, entry);
        if (OwnedProcessChild.Supports(scenario)) return OwnedProcessChild.Run(scenario, arguments, entry);
        if (scenario is "diagnostic-stall" or "diagnostic-stall-denied")
        {
            // Request work drains normally; the diagnostic drain then never completes.
            Mark("entered", entry);
            return WindowsProcess.RunOwned(arguments, entry, (_, _) => new Provider(scenario), new ProfileSource(),
                checkpoint: stage =>
                {
                    if (stage != OwnedProcessCheckpoint.BeforeDiagnosticDrain) return;
                    Mark("diagnostic-stall");
                    Thread.Sleep(Timeout.Infinite);
                });
        }
        if (scenario is not ("success" or "file-stdin" or "closed-stdin" or "close-pending"
            or "unused-stdin" or "data-close" or "deadline" or "broken-output"
            or "blocked-output" or "blocked-diagnostics" or "drain-stall" or "drain-stall-denied")) return 90;

        Mark("entered", entry);
        return WindowsProcess.Run(arguments, entry, new Host(), profile =>
        {
            Mark("provider-created");
            if (profile.ClientId != Guid.Parse("aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee")
                || profile.Name != "synthetic-process-profile")
            {
                throw new ProviderFailureException(AuthenticationFailure.InvalidRequest);
            }
            return new Provider(scenario);
        }, new ProfileSource());
    }

    internal static void Mark(string name, long? timestamp = null) =>
        File.WriteAllText(Path.Combine(Path.GetTempPath(), name),
            (timestamp ?? TimeProvider.System.GetTimestamp()).ToString(CultureInfo.InvariantCulture) + "\n");

    private sealed class Host : IRequestHost
    {
        public TimeProvider Clock => TimeProvider.System;
        public Task<nint> OpenInteractionAsync(CancellationToken cancellationToken) => Task.FromResult<nint>(0);
        public Task CloseInteractionAsync() => Task.CompletedTask;
    }

    private sealed class ProfileSource : IProfileSource
    {
        public Task<ReadOnlyMemory<byte>> ReadAsync(string path, CancellationToken token)
        {
            Mark("profile-read");
            return new WindowsProfileSource().ReadAsync(path, token);
        }
    }

    private sealed class Provider(string scenario) : IAuthenticationProvider
    {
        private readonly object handle = new();

        public Task<IReadOnlyList<ProviderAccount>> GetAccountsAsync(CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<ProviderAccount>>([new(Email, handle)]);

        public async Task<TokenCandidate> AcquireSilentAsync(AuthenticationRequest request,
            ProviderAccount account, Guid operationId, CancellationToken token)
        {
            if (request.AccountEmail != Email || request.ExactTenant != Guid.Parse(Tenant)
                || !request.Scopes.SequenceEqual([Scope]) || !ReferenceEquals(account.Handle, handle))
            {
                throw new ProviderFailureException(AuthenticationFailure.InvalidRequest);
            }

            Mark("provider-ready");
            if (scenario is "drain-stall" or "drain-stall-denied")
            {
                // Terminal selection cancels this token; the callback never returns,
                // so required drain stalls only after the result is committed.
                token.Register(() => { Mark("callback-pending"); Thread.Sleep(Timeout.Infinite); });
            }
            if (scenario is "diagnostic-stall-denied" or "drain-stall-denied")
                throw new ProviderFailureException(AuthenticationFailure.Denied);
            if (scenario == "deadline")
            {
                // Ignore cancellation and keep both work and a callback pending.
                token.Register(() => { Mark("callback-pending"); Thread.Sleep(Timeout.Infinite); });
                await new TaskCompletionSource().Task;
            }
            if (scenario is "close-pending" or "data-close")
            {
                // Return a late candidate when cancellation is observed. The
                // application must reject it and finish its bounded shutdown.
                try { await Task.Delay(Timeout.InfiniteTimeSpan, token); }
                catch (OperationCanceledException) when (token.IsCancellationRequested) { }
                Mark("cancel-observed");
            }

            Mark("candidate-returned");
            return new(scenario == "blocked-output" ? new string('S', 262144) : Token,
                Email, Guid.Parse(Tenant), [Scope], "Bearer", DateTimeOffset.UtcNow.AddMinutes(10), operationId);
        }

        public Task<TokenCandidate> AcquireInteractiveAsync(AuthenticationRequest request, ProviderAccount? account,
            string? claims, nint parentWindow, Guid operationId, CancellationToken cancellationToken) =>
            Task.FromException<TokenCandidate>(new ProviderFailureException(AuthenticationFailure.MechanismUnavailable));
    }
}
