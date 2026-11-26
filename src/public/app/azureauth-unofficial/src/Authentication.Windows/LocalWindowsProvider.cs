using Authentication.Core;

namespace Authentication.Windows;

// Observations remain synchronous on the actual request or UI thread. The native
// implementation must preserve the original token around every individual query.
internal interface IWindowsHostAdmission
{
    void Admit(CancellationToken cancellationToken);
    void Recheck(CancellationToken cancellationToken);
}

// One instance belongs to one sequential coordinator request. Native observations
// and MSAL initialization are supplied separately; construction remains inert.
internal sealed class LocalWindowsProvider : IAuthenticationProvider
{
    private readonly IWindowsHostAdmission admission;
    private readonly Func<CancellationToken, IAuthenticationProvider> initialize;
    private IAuthenticationProvider? provider;

    internal LocalWindowsProvider(IWindowsHostAdmission admission,
        Func<CancellationToken, IAuthenticationProvider> initialize)
    {
        ArgumentNullException.ThrowIfNull(admission);
        ArgumentNullException.ThrowIfNull(initialize);
        this.admission = admission;
        this.initialize = initialize;
    }

    public Task<IReadOnlyList<ProviderAccount>> GetAccountsAsync(CancellationToken cancellationToken)
    {
        return PrepareOperation(cancellationToken).GetAccountsAsync(cancellationToken);
    }

    public Task<TokenCandidate> AcquireSilentAsync(AuthenticationRequest request,
        ProviderAccount account, Guid operationId, CancellationToken cancellationToken)
    {
        return PrepareOperation(cancellationToken).AcquireSilentAsync(
            request, account, operationId, cancellationToken);
    }

    public Task<TokenCandidate> AcquireInteractiveAsync(AuthenticationRequest request,
        ProviderAccount? account, string? claims, nint parentWindow, Guid operationId,
        CancellationToken cancellationToken)
    {
        return PrepareOperation(cancellationToken).AcquireInteractiveAsync(
            request, account, claims, parentWindow, operationId, cancellationToken);
    }

    private IAuthenticationProvider PrepareOperation(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (provider is null)
        {
            Observe(admission.Admit, cancellationToken);
            Observe(token => provider = initialize(token), cancellationToken);
            ArgumentNullException.ThrowIfNull(provider);
        }

        Observe(admission.Recheck, cancellationToken);
        return provider;
    }

    private static void Observe(Action<CancellationToken> observation, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        try
        {
            observation(cancellationToken);
        }
        catch
        {
            // Cancellation also wins when the synchronous effect throws.
            cancellationToken.ThrowIfCancellationRequested();
            throw;
        }

        cancellationToken.ThrowIfCancellationRequested();
    }
}
