using Hcoona.AzureAuth.CredProvider.Contracts;
using Hcoona.AzureAuth.CredProvider.Platform.CredentialCore;

namespace Hcoona.AzureAuth.CredProvider.Platform.AzureAuthProvider;

internal sealed record AzureAuthRequestPreflightFailure(
    AcquiredAccessTokenStatus Status,
    string Code,
    string SafeMessage
)
{
    internal AcquiredAccessTokenResult ToAcquisitionResult() =>
        AcquiredAccessTokenResult.Failure(Status, Code, SafeMessage);
}

internal static class AzureAuthRequestPreflightPolicy
{
    internal static AzureAuthRequestPreflightFailure? Evaluate(
        CredentialRequestV2 request,
        AzureAuthHostPlatform hostPlatform
    )
    {
        ArgumentNullException.ThrowIfNull(request);

        switch (request.AcquisitionMode)
        {
            case AcquisitionMode.Unspecified:
                return new AzureAuthRequestPreflightFailure(
                    AcquiredAccessTokenStatus.InteractionBlocked,
                    "AzureAuthAcquisitionModeRequired",
                    "AzureAuth requires an explicit acquisition mode."
                );
            case AcquisitionMode.SilentOnly:
                if (hostPlatform != AzureAuthHostPlatform.NativeLinux)
                {
                    return new AzureAuthRequestPreflightFailure(
                        AcquiredAccessTokenStatus.InteractionRequired,
                        "SilentAcquisitionUnavailable",
                        "AzureAuth 0.9.5 has no cache-only mode on Windows or WSL, so "
                            + "SilentOnly acquisition is unavailable."
                    );
                }
                break;
            case AcquisitionMode.InteractionAllowed:
                break;
            default:
                return RequestRejected();
        }

        if (CredentialRequestV2Policy.GetViolation(request) is not null)
        {
            return RequestRejected();
        }

        if (request.IdentityFlow == IdentityFlow.DeviceCode)
        {
            return new AzureAuthRequestPreflightFailure(
                AcquiredAccessTokenStatus.RequestRejected,
                "AzureAuthDeviceCodeUnsupported",
                "The AzureAuth integration uses broker and web modes and does not support "
                    + "device-code requests."
            );
        }

        if (request.CachePolicy == CachePolicyMode.FuturePersistentCacheRequested)
        {
            return new AzureAuthRequestPreflightFailure(
                AcquiredAccessTokenStatus.PrerequisiteFailed,
                "AzureAuthPersistentCacheUnsupported",
                "Product-managed persistent cache is unsupported; "
                    + "the AzureAuth host cache remains enabled."
            );
        }

        if (request.IdentityFlow != IdentityFlow.InteractiveBrowser)
        {
            return new AzureAuthRequestPreflightFailure(
                AcquiredAccessTokenStatus.RequestRejected,
                "AzureAuthPolicyRejected",
                "AzureAuth rejected the credential request."
            );
        }

        if (
            request.AcquisitionMode == AcquisitionMode.InteractionAllowed
            && !IdentityFlowPolicy.IsAcceptedMvpRequest(ToV1Projection(request))
        )
        {
            return new AzureAuthRequestPreflightFailure(
                AcquiredAccessTokenStatus.RequestRejected,
                "AzureAuthPolicyRejected",
                "AzureAuth rejected the credential request."
            );
        }

        return null;
    }

    private static AzureAuthRequestPreflightFailure RequestRejected() =>
        new(
            AcquiredAccessTokenStatus.RequestRejected,
            "AzureAuthRequestRejected",
            "AzureAuth rejected the credential request."
        );

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
