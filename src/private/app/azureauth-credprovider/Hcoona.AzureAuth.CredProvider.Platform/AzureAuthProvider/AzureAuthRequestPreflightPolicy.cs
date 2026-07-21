using Hcoona.AzureAuth.CredProvider.Contracts;
using Hcoona.AzureAuth.CredProvider.Platform.CredentialCore;

namespace Hcoona.AzureAuth.CredProvider.Platform.AzureAuthProvider;

internal sealed record AzureAuthRequestPreflightFailure(
    AcquiredAccessTokenStatus Status,
    string Code,
    string SafeMessage)
{
    internal AcquiredAccessTokenResult ToAcquisitionResult() =>
        AcquiredAccessTokenResult.Failure(Status, Code, SafeMessage);
}

internal static class AzureAuthRequestPreflightPolicy
{
    internal static AzureAuthRequestPreflightFailure? Evaluate(CredentialRequestV2 request)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (CredentialRequestV2Policy.GetViolation(request) is not null)
        {
            return RequestRejected();
        }

        switch (request.AcquisitionMode)
        {
            case AcquisitionMode.Unspecified:
                return new AzureAuthRequestPreflightFailure(
                    AcquiredAccessTokenStatus.InteractionBlocked,
                    "AzureAuthAcquisitionModeRequired",
                    "AzureAuth requires acquisitionMode interactionAllowed.");
            case AcquisitionMode.SilentOnly:
                return new AzureAuthRequestPreflightFailure(
                    AcquiredAccessTokenStatus.InteractionRequired,
                    "SilentAcquisitionUnavailable",
                    "Silent AzureAuth acquisition is not implemented; use explicit interactive "
                        + "login for interactive operations only. No automatic remediation is available.");
            case AcquisitionMode.InteractionAllowed:
                break;
            default:
                return RequestRejected();
        }

        if (request.IdentityFlow == IdentityFlow.DeviceCode)
        {
            return new AzureAuthRequestPreflightFailure(
                AcquiredAccessTokenStatus.RequestRejected,
                "AzureAuthDeviceCodeUnsupported",
                "AzureAuth device-code interaction is unavailable until a secret-safe interaction channel exists.");
        }

        if (request.CachePolicy == CachePolicyMode.FuturePersistentCacheRequested)
        {
            return new AzureAuthRequestPreflightFailure(
                AcquiredAccessTokenStatus.PrerequisiteFailed,
                "AzureAuthPersistentCacheUnsupported",
                "AzureAuth persistent cache is not enabled in this work package.");
        }

        if (!IsValidHint(request.AccountHint) || !IsValidHint(request.TenantHint))
        {
            return RequestRejected();
        }

        if (!IdentityFlowPolicy.IsAcceptedMvpRequest(ToV1Projection(request)))
        {
            return new AzureAuthRequestPreflightFailure(
                AcquiredAccessTokenStatus.RequestRejected,
                "AzureAuthPolicyRejected",
                "AzureAuth rejected the credential request.");
        }

        return null;
    }

    private static AzureAuthRequestPreflightFailure RequestRejected() =>
        new(
            AcquiredAccessTokenStatus.RequestRejected,
            "AzureAuthRequestRejected",
            "AzureAuth rejected the credential request.");

    private static bool IsValidHint(string? hint)
    {
        if (hint is null)
        {
            return true;
        }

        try
        {
            _ = AzureAuthBindingPolicy.NormalizeObservedIdentifier(hint, nameof(hint));
            return true;
        }
        catch (ArgumentException)
        {
            return false;
        }
    }

    private static CredentialRequest ToV1Projection(CredentialRequestV2 request) =>
        new()
        {
            ContractMajor = ContractVersions.CredentialContractMajor,
            Ecosystem = request.Ecosystem,
            Operation = request.Operation,
            Resource = request.Resource!,
            ServiceIdentity = request.ServiceIdentity!,
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
