using Hcoona.AzureAuth.CredProvider.Contracts;

namespace Hcoona.AzureAuth.CredProvider.Platform.CredentialCore;

public interface IDerivedCredentialCache
{
    DerivedCredentialCacheAvailability GetPersistentAvailability(CredentialRequest request);

    DerivedCredentialCacheReadResult TryReadPersistent(
        CredentialRequest request,
        CacheKey cacheKey);

    DerivedCredentialCacheWriteResult TryWritePersistent(
        CredentialRequest request,
        CacheKey cacheKey,
        IdentityMaterial identity);
}
