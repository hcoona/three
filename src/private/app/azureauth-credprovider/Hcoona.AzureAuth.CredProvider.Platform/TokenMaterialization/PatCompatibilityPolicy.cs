using Hcoona.AzureAuth.CredProvider.Contracts;

namespace Hcoona.AzureAuth.CredProvider.Platform.TokenMaterialization;

public readonly record struct PatCompatibilityPolicyDecision(
    IdentityFlowState State,
    string Code,
    string SafeMessage);

public static class PatCompatibilityPolicy
{
    public static PatCompatibilityPolicyDecision Evaluate(CredentialRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        return Evaluate(request.IdentityFlow, request.CredentialKind);
    }

    public static PatCompatibilityPolicyDecision Evaluate(CredentialRequestV2 request)
    {
        ArgumentNullException.ThrowIfNull(request);
        return Evaluate(request.IdentityFlow, request.CredentialKind);
    }

    private static PatCompatibilityPolicyDecision Evaluate(
        IdentityFlow identityFlow,
        CredentialKind credentialKind) =>
        identityFlow == IdentityFlow.PatCompatibility
            || credentialKind == CredentialKind.PatCompatibility
            ? new(
                IdentityFlowState.Deferred,
                "PatCompatibilityDeferred",
                "PAT compatibility is deferred and has no production "
                    + "acquisition or materialization path.")
            : new(
                IdentityFlowState.Disabled,
                "PatCompatibilityNotSelected",
                "PAT compatibility is not selected.");
}
