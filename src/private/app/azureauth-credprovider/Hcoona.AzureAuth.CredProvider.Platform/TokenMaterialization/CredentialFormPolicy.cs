using Hcoona.AzureAuth.CredProvider.Contracts;

namespace Hcoona.AzureAuth.CredProvider.Platform.TokenMaterialization;

public enum CredentialMaterializationAction
{
    Disabled = 0,
    DirectBasicPassword = 1,
    DirectBearer = 2,
    ExchangeNuGetSessionToken = 3,
}

public readonly record struct CredentialFormPolicyDecision(
    CredentialMaterializationAction Action,
    string Code)
{
    public bool IsEnabled => Action != CredentialMaterializationAction.Disabled;
}

public static class CredentialFormPolicy
{
    public const string DirectBasicUsername = "AzureDevOps";
    public const string NuGetSessionTokenUsername = "VssSessionToken";

    public static CredentialFormPolicyDecision Evaluate(CredentialRequestV2 request)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (
            request.Operation != CredentialOperation.Get
            || request.IdentityFlow
                is IdentityFlow.PatCompatibility
                    or IdentityFlow.AzurePipelinesSystemAccessToken
            || request.CredentialKind == CredentialKind.PatCompatibility
        )
        {
            return Disabled("CredentialFormDisabled");
        }

        return (request.Ecosystem, request.CredentialKind, request.RequestedAudience) switch
        {
            (
                CredentialEcosystem.Git,
                CredentialKind.BasicPassword,
                TokenAudience.AzureDevOps
            ) => Enabled(CredentialMaterializationAction.DirectBasicPassword),
            (
                CredentialEcosystem.NuGet,
                CredentialKind.NuGetPluginCredential,
                TokenAudience.AzureArtifacts
            ) => Enabled(CredentialMaterializationAction.ExchangeNuGetSessionToken),
            (
                CredentialEcosystem.Python,
                CredentialKind.BasicPassword,
                TokenAudience.AzureArtifacts
            ) => Enabled(CredentialMaterializationAction.DirectBasicPassword),
            (
                CredentialEcosystem.Npm
                    or CredentialEcosystem.Pnpm
                    or CredentialEcosystem.Yarn,
                CredentialKind.NpmAuthToken,
                TokenAudience.AzureArtifacts
            ) => Enabled(CredentialMaterializationAction.DirectBearer),
            _ => Disabled("CredentialFormUnsupported"),
        };
    }

    private static CredentialFormPolicyDecision Enabled(CredentialMaterializationAction action) =>
        new(action, "CredentialFormEnabled");

    private static CredentialFormPolicyDecision Disabled(string code) =>
        new(CredentialMaterializationAction.Disabled, code);
}
