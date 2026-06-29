using Hcoona.AzureAuth.CredProvider.Contracts;

namespace Hcoona.AzureAuth.CredProvider.Platform.CredentialCore;

internal sealed class DeterministicLocalTokenExchange : ITokenExchange
{
    private const string AzureDevOpsUsername = "AzureDevOps";

    public TokenExchangeResult Exchange(
        CredentialRequest request,
        IdentityMaterial identity,
        CacheKey cacheKey)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(identity);
        ArgumentNullException.ThrowIfNull(cacheKey);

        return request.CredentialKind switch
        {
            CredentialKind.BearerToken or CredentialKind.NpmAuthToken =>
                TokenExchangeResult.Success(
                    new TokenExchangeMaterial
                    {
                        BearerToken = identity.AccessToken
                            ?? throw new InvalidOperationException(
                                "Identity provider returned incomplete credential core material."),
                    }
                ),
            CredentialKind.BasicPassword
            or CredentialKind.NuGetPluginCredential
            or CredentialKind.PatCompatibility => TokenExchangeResult.Success(
                new TokenExchangeMaterial
                {
                    Username = AzureDevOpsUsername,
                    Password = identity.Secret
                        ?? throw new InvalidOperationException(
                            "Identity provider returned incomplete credential core material."),
                }
            ),
            _ => throw new InvalidOperationException(
                "Credential kind is not supported by the credential core scaffold."),
        };
    }
}
