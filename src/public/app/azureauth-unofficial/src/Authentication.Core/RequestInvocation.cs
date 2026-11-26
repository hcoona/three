using System.Runtime.CompilerServices;

[assembly: InternalsVisibleTo("Authentication.Scenarios")]
[assembly: InternalsVisibleTo("Authentication.Windows.Scenarios")]

namespace Authentication.Core;

// The Windows implementation owns fixed-volume admission, one bounded file read,
// and cancellation. This seam supplies bytes without selecting or provisioning a Profile.
public interface IProfileSource
{
    Task<ReadOnlyMemory<byte>> ReadAsync(string path, CancellationToken cancellationToken);
}

// One managed authentication invocation. The process host captures entryTimestamp
// before parsing and retains this object through its bounded shutdown drain.
public sealed class RequestInvocation : IDisposable
{
    private readonly RequestLifetime lifetime;
    private readonly IRequestHost host;
    private AuthenticationTrace? trace;

    public ParsedRequest? Request { get; }

    public Task OperationCompletion => lifetime.OperationCompletion;

    public long? TerminalTimestamp => lifetime.TerminalTimestamp;

    public async Task CompleteAsync()
    {
        await lifetime.CompleteAsync().ConfigureAwait(false);
        trace?.Dispose();
    }

    public void FailHost() => lifetime.FailHost();

    public RequestInvocation(IReadOnlyList<string> arguments, IRequestHost host,
        long entryTimestamp, CancellationToken cancellationToken = default)
    {
        this.host = host;
        Request = RequestSyntax.Parse(arguments);
        lifetime = new(host.Clock, entryTimestamp,
            TimeSpan.FromSeconds(Request?.TimeoutSeconds ?? 120), cancellationToken);
    }

    // The returned observation is provisional. Only TryCommitResult can prepare output.
    // This Profile-only factory form is a scenario seam; the product binds the request.
    internal Task<AuthenticationOutcome> RunAsync(IProfileSource profiles,
        Func<ClientProfile, IAuthenticationProvider> createProvider,
        Task<bool>? hostAdmission = null) =>
        RunAsync(profiles, (profile, _) => createProvider(profile), hostAdmission);

    public Task<AuthenticationOutcome> RunAsync(IProfileSource profiles,
        Func<ClientProfile, AuthenticationRequest, IAuthenticationProvider> createProvider,
        Task<bool>? hostAdmission = null)
    {
        trace ??= AuthenticationTrace.Start();
        return lifetime.RunAsync(async token =>
        {
            if (Request is null) return new(null, AuthenticationFailure.InvalidRequest);

            // Native admission can block independently. Stop waiting with the
            // original token; a late admission must never resume Profile work.
            if (hostAdmission is not null)
            {
                var admitted = await hostAdmission.WaitAsync(token).ConfigureAwait(false);
                token.ThrowIfCancellationRequested();
                if (!admitted) return new(null, AuthenticationFailure.InvalidRequest);
            }

            ReadOnlyMemory<byte> bytes;
            try
            {
                token.ThrowIfCancellationRequested();
                using var phase = trace.Begin(AuthenticationStage.ProfileRead);
                bytes = await profiles.ReadAsync(Request.ProfilePath, token).ConfigureAwait(false);
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                return InvalidConfiguration();
            }

            token.ThrowIfCancellationRequested();
            var profile = ProfileSyntax.Parse(bytes);
            if (profile is null) return InvalidConfiguration();
            if (!ProfileSyntax.TryResolveTenant(profile, Request.Tenant, out var tenant))
            {
                // A valid Profile whose fixed tenant conflicts with --tenant is a
                // caller/Profile mismatch, not an unusable configuration.
                return new(null, AuthenticationFailure.InvalidRequest, Reason: AuthenticationReason.TenantConflict);
            }

            var request = new AuthenticationRequest(Request.AccountEmail, Request.Scopes,
                Request.InteractionAllowed, tenant, Request.ForceInteractive);
            token.ThrowIfCancellationRequested();
            IAuthenticationProvider provider;
            try
            {
                provider = createProvider(profile, request);
            }
            catch (ProviderFailureException exception)
            {
                return RequestCoordinator.ProviderOutcome(exception, trace);
            }

            token.ThrowIfCancellationRequested();
            return await new RequestCoordinator(provider, host, trace).AuthenticateAsync(request, token).ConfigureAwait(false);
        });
    }

    private static AuthenticationOutcome InvalidConfiguration() =>
        new(null, AuthenticationFailure.InvalidRequest, Reason: AuthenticationReason.InvalidConfiguration);

    public bool TryCommitResult(out SerializedResult? result)
    {
        result = null;
        if (!lifetime.TryCommit(out var outcome)) return false;
        result = ResultProjection.Serialize(outcome!);
        trace?.Committed(outcome!);
        return true;
    }

    public void Dispose()
    {
        lifetime.Dispose();
        _ = EndTraceAsync();
    }

    private async Task EndTraceAsync()
    {
        try { await lifetime.CompleteAsync().ConfigureAwait(false); }
        catch (Exception) { }
        finally { trace?.Dispose(); }
    }
}
