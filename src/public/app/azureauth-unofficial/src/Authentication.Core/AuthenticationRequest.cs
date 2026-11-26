namespace Authentication.Core;

// This is the application boundary after CLI/Profile admission. It is not a second
// public wire protocol, and it does not select or provision a Client Profile.
public sealed record AuthenticationRequest(
    string AccountEmail,
    IReadOnlyList<string> Scopes,
    bool InteractionAllowed,
    Guid? ExactTenant,
    bool ForceInteractive = false)
{
    public override string ToString() => nameof(AuthenticationRequest);
}

public enum AuthenticationFailure
{
    InternalFailure,
    InteractionRequired,
    AccountAmbiguous,
    IdentityValidationFailed,
    InvalidRequest,
    Cancelled,
    Denied,
    MechanismUnavailable,
    TemporarilyUnavailable,
    Timeout,
}

public enum AuthenticationReason
{
    None,
    ConsentRequired,
    ProviderTransient,
    NetworkTransient,
    ServiceTransient,
    InvalidConfiguration,
    TenantConflict,
}

public sealed record AuthenticationOutcome(
    TokenCandidate? Success,
    AuthenticationFailure? Failure,
    bool PersistenceUnconfirmed = false,
    bool Interactive = false,
    AuthenticationReason Reason = AuthenticationReason.None)
{
    public override string ToString() => Failure?.ToString() ?? "Success";
}

public sealed record ProviderAccount(string? Email, object Handle)
{
    public override string ToString() => nameof(ProviderAccount);
}

public sealed record TokenCandidate(
    string AccessToken,
    string? Email,
    Guid? Tenant,
    IReadOnlyList<string> Scopes,
    string TokenType,
    DateTimeOffset ExpiresOn,
    Guid OperationId,
    Guid? CorrelationId = null)
{
    public override string ToString() => nameof(TokenCandidate);
}

public interface IAuthenticationProvider
{
    Task<IReadOnlyList<ProviderAccount>> GetAccountsAsync(CancellationToken cancellationToken);

    Task<TokenCandidate> AcquireSilentAsync(
        AuthenticationRequest request,
        ProviderAccount account,
        Guid operationId,
        CancellationToken cancellationToken);

    Task<TokenCandidate> AcquireInteractiveAsync(
        AuthenticationRequest request,
        ProviderAccount? account,
        string? claims,
        nint parentWindow,
        Guid operationId,
        CancellationToken cancellationToken);
}

public interface IRequestHost
{
    TimeProvider Clock { get; }

    Task<nint> OpenInteractionAsync(CancellationToken cancellationToken);

    Task CloseInteractionAsync();
}

public enum ProviderDiagnosticError
{
    None, QueryParameterConflict, BrokerContract, BrokerConfiguration,
    MsalClient, MsalService, Argument, InvalidOperation, Unexpected,
}

// The adapter supplies structured categories, never provider message text. Claims are
// request-local opaque input for the one permitted continuation and are not output.
public sealed class ProviderFailureException(AuthenticationFailure failure, string? claims = null,
    AuthenticationReason reason = AuthenticationReason.None,
    ProviderDiagnosticError diagnosticError = ProviderDiagnosticError.None)
    : Exception("Authentication provider failed.")
{
    public AuthenticationFailure Failure { get; } = failure;

    public string? Claims { get; } = claims;

    public AuthenticationReason Reason { get; } = reason;

    public ProviderDiagnosticError DiagnosticError { get; } = diagnosticError;
}
