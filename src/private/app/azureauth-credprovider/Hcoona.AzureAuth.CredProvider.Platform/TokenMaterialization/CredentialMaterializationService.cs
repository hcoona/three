using Hcoona.AzureAuth.CredProvider.Contracts;
using Hcoona.AzureAuth.CredProvider.Platform.CredentialCore;

namespace Hcoona.AzureAuth.CredProvider.Platform.TokenMaterialization;

public sealed class CredentialMaterializationService
{
    private readonly ITokenExchange _tokenExchange;
    private readonly TimeProvider _timeProvider;

    public CredentialMaterializationService(
        ITokenExchange tokenExchange,
        TimeProvider? timeProvider = null)
    {
        ArgumentNullException.ThrowIfNull(tokenExchange);
        _tokenExchange = tokenExchange;
        _timeProvider = timeProvider ?? TimeProvider.System;
    }

    public async ValueTask<CredentialMaterializationResult> MaterializeAsync(
        CredentialRequestV2 request,
        AcquiredAccessToken sourceToken,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(sourceToken);

        CredentialFormPolicyDecision decision = CredentialFormPolicy.Evaluate(request);
        if (!decision.IsEnabled)
        {
            return Failure(
                CredentialMaterializationStatus.Disabled,
                decision.Code,
                "The requested credential form is disabled.");
        }

        if (
            CredentialRequestV2Policy.GetViolation(request) is not null
            || request.Resource is null
            || !CanonicalResourceIdentityPolicy.IsServiceEndpointCompatibleWithEcosystem(
                request.Resource.ServiceEndpoint,
                request.Ecosystem)
        )
        {
            return Failure(
                CredentialMaterializationStatus.InvalidRequest,
                "CredentialRequestInvalid",
                "The credential request is invalid.");
        }

        DateTimeOffset now = _timeProvider.GetUtcNow();
        if (
            sourceToken.ClaimValidation
                != AccessTokenClaimValidation.AzureDevOpsClaimConsistency
            || sourceToken.Provenance == AccessTokenAcquisitionProvenance.Unspecified
            || string.IsNullOrWhiteSpace(sourceToken.TenantId)
            || string.IsNullOrEmpty(sourceToken.Token?.Value)
            || sourceToken.IssuedAt is null
            || sourceToken.NotBefore is null
            || sourceToken.ExpiresAt is null
            || sourceToken.NotBefore > now
            || sourceToken.ExpiresAt <= now
            || (
                request.TenantHint is not null
                && !string.Equals(
                    request.TenantHint,
                    sourceToken.TenantId,
                    StringComparison.OrdinalIgnoreCase)
            )
        )
        {
            return Failure(
                CredentialMaterializationStatus.InvalidToken,
                "AcquiredTokenInvalid",
                "The acquired access token is not currently usable.");
        }

        return decision.Action switch
        {
            CredentialMaterializationAction.DirectBasicPassword => Success(
                CredentialFormPolicy.DirectBasicUsername,
                sourceToken.Token.Value,
                bearerToken: null,
                sourceToken.ExpiresAt.Value),
            CredentialMaterializationAction.DirectBearer => Success(
                username: null,
                password: null,
                sourceToken.Token.Value,
                sourceToken.ExpiresAt.Value),
            CredentialMaterializationAction.ExchangeNuGetSessionToken =>
                await ExchangeAsync(request, sourceToken, cancellationToken).ConfigureAwait(false),
            _ => Failure(
                CredentialMaterializationStatus.Unsupported,
                "CredentialFormUnsupported",
                "The requested credential form is unsupported."),
        };
    }

    private async ValueTask<CredentialMaterializationResult> ExchangeAsync(
        CredentialRequestV2 request,
        AcquiredAccessToken sourceToken,
        CancellationToken cancellationToken)
    {
        AsyncTokenExchangeResult exchange = await _tokenExchange
            .ExchangeAsync(request, sourceToken, cancellationToken)
            .ConfigureAwait(false);

        if (
            exchange.Status != AsyncTokenExchangeStatus.Success
            || exchange.Token is null
            || string.IsNullOrEmpty(exchange.Token.Value)
            || exchange.ExpiresAt is null
        )
        {
            return exchange.Status switch
            {
                AsyncTokenExchangeStatus.Disabled => Failure(
                    CredentialMaterializationStatus.Disabled,
                    exchange.Code,
                    "Token exchange is disabled."),
                AsyncTokenExchangeStatus.Canceled => Failure(
                    CredentialMaterializationStatus.Canceled,
                    exchange.Code,
                    "Token exchange was canceled."),
                AsyncTokenExchangeStatus.TimedOut => Failure(
                    CredentialMaterializationStatus.TimedOut,
                    exchange.Code,
                    "Token exchange timed out."),
                _ => Failure(
                    CredentialMaterializationStatus.ExchangeFailed,
                    exchange.Code,
                    "Token exchange failed."),
            };
        }

        DateTimeOffset expiry =
            exchange.ExpiresAt.Value < sourceToken.ExpiresAt!.Value
                ? exchange.ExpiresAt.Value
                : sourceToken.ExpiresAt.Value;
        return Success(
            CredentialFormPolicy.NuGetSessionTokenUsername,
            exchange.Token.Value,
            bearerToken: null,
            expiry);
    }

    private static CredentialMaterializationResult Success(
        string? username,
        string? password,
        string? bearerToken,
        DateTimeOffset expiresAt) =>
        new()
        {
            Status = CredentialMaterializationStatus.Success,
            Username = username,
            Password = password,
            BearerToken = bearerToken,
            ExpiresAt = expiresAt.ToUniversalTime(),
            Code = "CredentialMaterialized",
            SafeMessage = "Credential materialization succeeded.",
        };

    private static CredentialMaterializationResult Failure(
        CredentialMaterializationStatus status,
        string code,
        string safeMessage) =>
        new()
        {
            Status = status,
            Code = code,
            SafeMessage = safeMessage,
        };
}
