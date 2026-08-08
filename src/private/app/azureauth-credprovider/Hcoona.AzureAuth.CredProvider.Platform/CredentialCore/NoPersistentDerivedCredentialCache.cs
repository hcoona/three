using Hcoona.AzureAuth.CredProvider.Contracts;

namespace Hcoona.AzureAuth.CredProvider.Platform.CredentialCore;

public sealed class NoPersistentDerivedCredentialCache : IDerivedCredentialCache
{
    internal int PersistentAvailabilityCheckCount { get; private set; }

    internal int PersistentReadCount { get; private set; }

    internal int PersistentWriteCount { get; private set; }

    public DerivedCredentialCacheAvailability GetPersistentAvailability(CredentialRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        PersistentAvailabilityCheckCount++;
        return DerivedCredentialCacheAvailability.Unsupported;
    }

    public DerivedCredentialCacheReadResult TryReadPersistent(
        CredentialRequest request,
        CacheKey cacheKey)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(cacheKey);
        PersistentReadCount++;
        return DerivedCredentialCacheReadResult.Unsupported;
    }

    public DerivedCredentialCacheWriteResult TryWritePersistent(
        CredentialRequest request,
        CacheKey cacheKey,
        IdentityMaterial identity)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(cacheKey);
        ArgumentNullException.ThrowIfNull(identity);
        PersistentWriteCount++;
        return DerivedCredentialCacheWriteResult.Unsupported;
    }
}
