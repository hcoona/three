using Hcoona.AzureAuth.CredProvider.Contracts;

namespace Hcoona.AzureAuth.CredProvider.Platform.CredentialCore;

internal interface ITokenExchange
{
    TokenExchangeResult Exchange(
        CredentialRequest request,
        IdentityMaterial identity,
        CacheKey cacheKey);
}
