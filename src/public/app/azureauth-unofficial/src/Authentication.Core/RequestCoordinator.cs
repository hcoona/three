namespace Authentication.Core;

public sealed class RequestCoordinator(IAuthenticationProvider provider, IRequestHost host,
    AuthenticationTrace? trace = null)
{
    private readonly IAuthenticationProvider provider = provider;
    private readonly IRequestHost host = host;

    public async Task<AuthenticationOutcome> AuthenticateAsync(
        AuthenticationRequest request,
        CancellationToken cancellationToken = default)
    {
        using var ownedTrace = trace is null ? AuthenticationTrace.Start() : null;
        var observation = trace ?? ownedTrace!;
        var closeOwnedUi = false;
        var interactive = false;
        AuthenticationOutcome outcome;
        try
        {
            outcome = await AcquireAsync();
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            outcome = new(null, AuthenticationFailure.Cancelled);
        }
        catch (ProviderFailureException exception)
        {
            outcome = ProviderOutcome(exception, observation, interactive);
        }
        catch (Exception)
        {
            outcome = new(null, AuthenticationFailure.InternalFailure, Interactive: interactive);
        }

        if (closeOwnedUi)
        {
            try
            {
                using var phase = observation.Begin(AuthenticationStage.InteractionClose);
                await host.CloseInteractionAsync();
            }
            catch (Exception)
            {
                // A cleanup fault cannot expose a token or replace an existing failure.
                // The process host must still enforce its finite shutdown allowance.
                if (outcome.Success is not null)
                {
                    outcome = new(null, AuthenticationFailure.InternalFailure, Interactive: interactive);
                }
            }
        }

        return cancellationToken.IsCancellationRequested
            ? new(null, AuthenticationFailure.Cancelled, Interactive: interactive)
            : outcome;

        async Task<AuthenticationOutcome> AcquireAsync()
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (request.ForceInteractive && !request.InteractionAllowed)
                return new(null, AuthenticationFailure.InvalidRequest);
            IReadOnlyList<ProviderAccount> accounts;
            using (observation.Begin(AuthenticationStage.AccountDiscovery))
                accounts = await provider.GetAccountsAsync(cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();
            ProviderAccount? selected = null;
            foreach (var account in accounts)
            {
                if (!string.Equals(account.Email, request.AccountEmail, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                if (selected is not null)
                {
                    return new(null, AuthenticationFailure.AccountAmbiguous);
                }

                selected = account;
            }

            string? claims = null;
            var interactionReason = AuthenticationReason.None;
            if (selected is not null && !request.ForceInteractive)
            {
                try
                {
                    var silentOperation = Guid.NewGuid();
                    TokenCandidate candidate;
                    using (observation.Begin(AuthenticationStage.SilentAcquisition))
                        candidate = await provider.AcquireSilentAsync(request, selected, silentOperation, cancellationToken);
                    cancellationToken.ThrowIfCancellationRequested();
                    return ValidateCandidate(request, candidate, silentOperation, interactive: false, observation);
                }
                catch (ProviderFailureException exception) when (exception.Failure == AuthenticationFailure.InteractionRequired)
                {
                    claims = exception.Claims;
                    interactionReason = exception.Reason;
                }
            }

            cancellationToken.ThrowIfCancellationRequested();
            if (!request.InteractionAllowed)
            {
                return new(null, AuthenticationFailure.InteractionRequired, Reason: interactionReason);
            }

            closeOwnedUi = true;
            nint parent;
            using (observation.Begin(AuthenticationStage.InteractionOpen))
                parent = await host.OpenInteractionAsync(cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();
            if (parent == 0)
            {
                return new(null, AuthenticationFailure.MechanismUnavailable);
            }

            interactive = true;
            var interactiveOperation = Guid.NewGuid();
            TokenCandidate interactiveCandidate;
            using (observation.Begin(AuthenticationStage.InteractiveAcquisition))
                interactiveCandidate = await provider.AcquireInteractiveAsync(
                    request, selected, claims, parent, interactiveOperation, cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();
            return ValidateCandidate(request, interactiveCandidate, interactiveOperation, interactive: true, observation);
        }
    }

    private AuthenticationOutcome ValidateCandidate(
        AuthenticationRequest request, TokenCandidate? candidate, Guid operationId, bool interactive,
        AuthenticationTrace observation)
    {
        using var phase = observation.Begin(AuthenticationStage.CandidateValidation);
        if (candidate is null || string.IsNullOrEmpty(candidate.AccessToken)
            || !string.Equals(candidate.Email, request.AccountEmail, StringComparison.OrdinalIgnoreCase)
            || candidate.Tenant is null || candidate.Tenant == Guid.Empty
            || (request.ExactTenant is not null && candidate.Tenant != request.ExactTenant)
            || string.IsNullOrEmpty(candidate.TokenType)
            || candidate.ExpiresOn <= host.Clock.GetUtcNow()
            || candidate.OperationId != operationId
            || candidate.Scopes is null || candidate.Scopes.Any(string.IsNullOrEmpty)
            || !SatisfiesScopes(request.Scopes, candidate.Scopes))
        {
            return new(null, AuthenticationFailure.IdentityValidationFailed, Interactive: interactive);
        }

        return new(candidate, null, PersistenceUnconfirmed: true, Interactive: interactive);
    }

    // One safe filter for every provider-reported failure, including provider
    // construction. Request validity and the deadline belong to the engine: a
    // provider cannot report invalid_request or timeout, and an undefined value
    // cannot become a public outcome. Reasons survive only with their own failure.
    internal static AuthenticationOutcome ProviderOutcome(ProviderFailureException exception,
        AuthenticationTrace observation, bool interactive = false)
    {
        var failure = exception.Failure switch
        {
            AuthenticationFailure.InteractionRequired or AuthenticationFailure.AccountAmbiguous
                or AuthenticationFailure.IdentityValidationFailed
                or AuthenticationFailure.Cancelled or AuthenticationFailure.Denied
                or AuthenticationFailure.MechanismUnavailable
                or AuthenticationFailure.TemporarilyUnavailable => exception.Failure,
            _ => AuthenticationFailure.InternalFailure,
        };
        if (failure == AuthenticationFailure.InternalFailure)
        {
            observation.RecordProviderFailure(exception.Failure == AuthenticationFailure.InternalFailure
                ? exception.DiagnosticError : ProviderDiagnosticError.Unexpected);
            return new(null, failure, Interactive: interactive);
        }

        return new(null, failure, Interactive: interactive, Reason: exception.Reason);
    }

    private static bool SatisfiesScopes(IReadOnlyList<string> requested, IReadOnlyList<string> granted)
    {
        // Admission has already established one resource and isolated /.default.
        // Providers need not return the literal /.default spelling among grants, so
        // granted scopes cannot be compared here. The operation ID check above only
        // binds the candidate to the provider call that carried this request; it does
        // not prove the granted resource. Otherwise each requested dynamic scope must
        // appear among the granted scopes, compared ordinally; extra grants are allowed.
        if (requested.Count == 1 && requested[0].EndsWith("/.default", StringComparison.Ordinal))
        {
            return true;
        }

        return requested.All(scope => granted.Contains(scope, StringComparer.Ordinal));
    }
}
