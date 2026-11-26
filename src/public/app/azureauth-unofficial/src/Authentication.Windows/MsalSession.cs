using Microsoft.Identity.Client;

namespace Authentication.Windows;

// The adapter owns these request-local intentions. Only the concrete session may
// construct MSAL or invoke its broker, account, and authentication operations.
internal sealed record MsalClientSettings(Guid ClientId, string Authority, string RedirectUri,
    bool ListOperatingSystemAccounts, bool MsaPassthrough)
{
    public override string ToString() => nameof(MsalClientSettings);
}

internal sealed record MsalSilentOperation(IReadOnlyList<string> Scopes, IAccount Account,
    string Tenant, Guid CorrelationId)
{
    public override string ToString() => nameof(MsalSilentOperation);
}

internal sealed record MsalInteractiveOperation(IReadOnlyList<string> Scopes, IAccount? Account,
    string? LoginHint, string Tenant, string? Claims, nint ParentWindow, Guid CorrelationId,
    bool ForceInteractive = false)
{
    public override string ToString() => nameof(MsalInteractiveOperation);
}

internal interface IMsalSessionFactory
{
    IMsalSession Create(MsalClientSettings settings, IMsalHttpClientFactory http,
        CancellationToken cancellationToken);
}

internal interface IMsalSession
{
    Task<IReadOnlyList<IAccount>> GetAccountsAsync(CancellationToken cancellationToken);
    Task<AuthenticationResult> AcquireSilentAsync(MsalSilentOperation operation,
        CancellationToken cancellationToken);
    Task<AuthenticationResult> AcquireInteractiveAsync(MsalInteractiveOperation operation,
        CancellationToken cancellationToken);
}
