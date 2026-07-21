using System.Diagnostics;
using System.Globalization;
using Hcoona.AzureAuth.CredProvider.Contracts;
using Hcoona.AzureAuth.CredProvider.Platform.CredentialCore;
using Hcoona.AzureAuth.CredProvider.Platform.TokenMaterialization;

namespace Hcoona.AzureAuth.CredProvider.Platform.AzurePipelines;

[DebuggerDisplay("<redacted>")]
public sealed class AzurePipelinesSystemAccessToken
{
    public const int MaximumLength = 16 * 1024;

    private AzurePipelinesSystemAccessToken(string value)
    {
        Value = value;
    }

    internal string Value { get; }

    public static bool TryCreate(
        string? value,
        out AzurePipelinesSystemAccessToken? token,
        out string code)
    {
        token = null;
        if (string.IsNullOrWhiteSpace(value))
        {
            code = "AzurePipelinesSystemAccessTokenUnavailable";
            return false;
        }

        if (value.Length > MaximumLength || value.Any(char.IsControl))
        {
            code = "AzurePipelinesSystemAccessTokenInvalid";
            return false;
        }

        token = new AzurePipelinesSystemAccessToken(value);
        code = "AzurePipelinesSystemAccessTokenValid";
        return true;
    }

    public override string ToString() => "<redacted>";
}

public enum AzurePipelinesSystemAccessTokenResultStatus
{
    Unspecified = 0,
    Success = 1,
    CredentialUnavailable = 2,
    InvalidToken = 3,
    InvalidRequest = 4,
    Unsupported = 5,
}

public enum AzurePipelinesCredentialLifetime
{
    Unspecified = 0,
    JobScopedUnknownExpiry = 1,
}

public sealed record AzurePipelinesSystemAccessTokenResult
{
    public required AzurePipelinesSystemAccessTokenResultStatus Status { get; init; }
    public required CredentialEcosystem Ecosystem { get; init; }
    public string? Username { get; init; }
    public SecretText? Password { get; init; }
    public SecretText? BearerToken { get; init; }
    public required AzurePipelinesCredentialLifetime Lifetime { get; init; }
    public required string Code { get; init; }
    public required string SafeMessage { get; init; }

    public bool Succeeded => Status == AzurePipelinesSystemAccessTokenResultStatus.Success;

    public CredentialResult CreateProtocolResult(string diagnosticsCorrelationId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(diagnosticsCorrelationId);

        CredentialResultStatus status = Status switch
        {
            AzurePipelinesSystemAccessTokenResultStatus.Success =>
                CredentialResultStatus.Success,
            AzurePipelinesSystemAccessTokenResultStatus.CredentialUnavailable =>
                CredentialResultStatus.CredentialUnavailable,
            AzurePipelinesSystemAccessTokenResultStatus.InvalidToken
                or AzurePipelinesSystemAccessTokenResultStatus.InvalidRequest
                or AzurePipelinesSystemAccessTokenResultStatus.Unsupported =>
                CredentialResultStatus.ProtocolViolation,
            _ => CredentialResultStatus.Fatal,
        };
        return new CredentialResult
        {
            Status = status,
            Username = Username,
            Password = Password?.Value,
            BearerToken = BearerToken?.Value,
            ExpiresAt = null,
            Account = null,
            Tenant = null,
            CacheKey = null,
            DiagnosticsCorrelationId = diagnosticsCorrelationId,
            Error = Succeeded
                ? null
                : new CredentialError
                {
                    Kind = Status == AzurePipelinesSystemAccessTokenResultStatus.CredentialUnavailable
                        ? CredentialErrorKind.CredentialUnavailable
                        : CredentialErrorKind.ProtocolViolation,
                    Code = Code,
                    SafeMessage = SafeMessage,
                },
        };
    }

    public override string ToString() =>
        string.Format(
            CultureInfo.InvariantCulture,
            "{0} {{ {1} = {2}, {3} = {4}, {5} = {6}, {7} = <redacted>, "
                + "{8} = <redacted>, {9} = {10}, {11} = {12}, {13} = {14} }}",
            nameof(AzurePipelinesSystemAccessTokenResult),
            nameof(Status),
            Status,
            nameof(Ecosystem),
            Ecosystem,
            nameof(Username),
            Username,
            nameof(Password),
            nameof(BearerToken),
            nameof(Lifetime),
            Lifetime,
            nameof(Code),
            Code,
            nameof(SafeMessage),
            SafeMessage);
}

public static class AzurePipelinesSystemAccessTokenService
{
    public static AzurePipelinesSystemAccessTokenResult Handle(
        CredentialRequest request,
        string? providedToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        return HandleCore(request, AcquisitionMode.Unspecified, providedToken);
    }

    public static AzurePipelinesSystemAccessTokenResult Handle(
        CredentialRequestV2 request,
        string? providedToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        string? violation = CredentialRequestV2Policy.GetViolation(request);
        if (violation is not null)
        {
            return Failure(
                request.Ecosystem,
                AzurePipelinesSystemAccessTokenResultStatus.InvalidRequest,
                "AzurePipelinesSystemAccessTokenRequestInvalid",
                "The Azure Pipelines system access token request is invalid.");
        }

        return HandleCore(ToV1Projection(request), request.AcquisitionMode, providedToken);
    }

    private static AzurePipelinesSystemAccessTokenResult HandleCore(
        CredentialRequest request,
        AcquisitionMode acquisitionMode,
        string? providedToken)
    {
        CredentialMaterializationAction action = GetAction(request);
        if (action == CredentialMaterializationAction.Disabled)
        {
            return Failure(
                request.Ecosystem,
                IsRequiredCiRequestShape(request, acquisitionMode)
                    ? AzurePipelinesSystemAccessTokenResultStatus.Unsupported
                    : AzurePipelinesSystemAccessTokenResultStatus.InvalidRequest,
                IsRequiredCiRequestShape(request, acquisitionMode)
                    ? "AzurePipelinesSystemAccessTokenFormUnsupported"
                    : "AzurePipelinesSystemAccessTokenRequestInvalid",
                IsRequiredCiRequestShape(request, acquisitionMode)
                    ? "The requested credential form does not support an Azure Pipelines system access token."
                    : "The Azure Pipelines system access token request is invalid.");
        }

        if (!AzurePipelinesSystemAccessToken.TryCreate(
                providedToken,
                out AzurePipelinesSystemAccessToken? token,
                out string tokenCode))
        {
            bool unavailable = tokenCode == "AzurePipelinesSystemAccessTokenUnavailable";
            return Failure(
                request.Ecosystem,
                unavailable
                    ? AzurePipelinesSystemAccessTokenResultStatus.CredentialUnavailable
                    : AzurePipelinesSystemAccessTokenResultStatus.InvalidToken,
                tokenCode,
                unavailable
                    ? "Azure Pipelines system access token is unavailable in the environment."
                    : "The Azure Pipelines system access token is invalid.");
        }

        return action switch
        {
            CredentialMaterializationAction.DirectBearer => Success(
                request.Ecosystem,
                username: null,
                password: null,
                bearerToken: token!.Value),
            _ => Failure(
                request.Ecosystem,
                AzurePipelinesSystemAccessTokenResultStatus.Unsupported,
                "AzurePipelinesSystemAccessTokenFormUnsupported",
                "The requested credential form does not support an Azure Pipelines system access token."),
        };
    }

    private static CredentialMaterializationAction GetAction(CredentialRequest request)
    {
        if (!IsRequiredCiRequestShape(request, AcquisitionMode.Unspecified))
        {
            return CredentialMaterializationAction.Disabled;
        }

        return (request.Ecosystem, request.CredentialKind, request.RequestedAudience) switch
        {
            (CredentialEcosystem.Git, CredentialKind.BearerToken, TokenAudience.AzureDevOps) =>
                CredentialMaterializationAction.DirectBearer,
            (
                CredentialEcosystem.Npm
                    or CredentialEcosystem.Pnpm
                    or CredentialEcosystem.Yarn,
                CredentialKind.NpmAuthToken,
                TokenAudience.AzureArtifacts
            ) => CredentialMaterializationAction.DirectBearer,
            _ => CredentialMaterializationAction.Disabled,
        };
    }

    private static bool IsRequiredCiRequestShape(
        CredentialRequest request,
        AcquisitionMode acquisitionMode)
    {
        CiContext? ciContext = request.CiContext;
        return IdentityFlowPolicy.IsAcceptedMvpRequest(request)
            && request.Operation == CredentialOperation.Get
            && request.IdentityFlow == IdentityFlow.AzurePipelinesSystemAccessToken
            && request.InteractivePolicy == InteractivePolicy.Never
            && request.CachePolicy == CachePolicyMode.NonPersistentCi
            && acquisitionMode == AcquisitionMode.Unspecified
            && request.AccountHint is null
            && request.TenantHint is null
            && ciContext is
            {
                ExplicitCiMode: true,
                HasAzurePipelinesSystemAccessToken: true,
                AllowsPersistentWrites: false,
            }
            && string.Equals(
                ciContext.Provider,
                CiProviderNames.AzurePipelines,
                StringComparison.Ordinal);
    }

    private static AzurePipelinesSystemAccessTokenResult Success(
        CredentialEcosystem ecosystem,
        string? username,
        string? password,
        string? bearerToken) =>
        new()
        {
            Status = AzurePipelinesSystemAccessTokenResultStatus.Success,
            Ecosystem = ecosystem,
            Username = username,
            Password = password is null ? null : new SecretText { Value = password },
            BearerToken = bearerToken is null ? null : new SecretText { Value = bearerToken },
            Lifetime = AzurePipelinesCredentialLifetime.JobScopedUnknownExpiry,
            Code = "AzurePipelinesSystemAccessTokenCredential",
            SafeMessage = "The job-scoped Azure Pipelines credential is available.",
        };

    private static AzurePipelinesSystemAccessTokenResult Failure(
        CredentialEcosystem ecosystem,
        AzurePipelinesSystemAccessTokenResultStatus status,
        string code,
        string safeMessage) =>
        new()
        {
            Status = status,
            Ecosystem = ecosystem,
            Lifetime = AzurePipelinesCredentialLifetime.JobScopedUnknownExpiry,
            Code = code,
            SafeMessage = safeMessage,
        };

    private static CredentialRequest ToV1Projection(CredentialRequestV2 request) =>
        new()
        {
            Ecosystem = request.Ecosystem,
            Operation = request.Operation,
            Resource = request.Resource,
            ServiceIdentity = request.ServiceIdentity,
            AccountHint = request.AccountHint,
            TenantHint = request.TenantHint,
            RequestedAudience = request.RequestedAudience,
            CredentialKind = request.CredentialKind,
            IdentityFlow = request.IdentityFlow,
            InteractivePolicy = request.InteractivePolicy,
            CachePolicy = request.CachePolicy,
            CiContext = request.CiContext,
            ExtensionData = request.ExtensionData,
        };
}
