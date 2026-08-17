using System.Security.Cryptography;
using System.Text;
using Hcoona.AzureAuth.CredProvider.Contracts;

namespace Hcoona.AzureAuth.CredProvider.Platform.CredentialCore;

public sealed class DeterministicFakeIdentityProvider : IIdentityProvider
{
    private static readonly DateTimeOffset DefaultExpiresAt = new(
        2030,
        1,
        1,
        0,
        0,
        0,
        TimeSpan.Zero
    );

    public int InvocationCount { get; private set; }

    public IdentityMaterial GetIdentity(CredentialRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);

        InvocationCount++;

        string account = ResolveAccount(request);
        string tenant = ResolveTenant(request);
        string seed = CreateSeed(request, account, tenant);

        return new IdentityMaterial
        {
            Account = account,
            Tenant = tenant,
            Secret = CreateDeterministicValue("fake-secret", seed),
            AccessToken = CreateDeterministicValue("fake-token", seed),
            ExpiresAt = DefaultExpiresAt,
        };
    }

    private static string ResolveAccount(CredentialRequest request)
    {
        if (!string.IsNullOrWhiteSpace(request.AccountHint))
        {
            return request.AccountHint.Trim().ToLowerInvariant();
        }

        string organization = request.Resource.Organization.ToLowerInvariant();
        return request.IdentityFlow == IdentityFlow.AzurePipelinesSystemAccessToken
            ? $"build-service@{organization}"
            : $"{request.ServiceIdentity}@{organization}.example";
    }

    private static string ResolveTenant(CredentialRequest request)
    {
        if (!string.IsNullOrWhiteSpace(request.TenantHint))
        {
            return request.TenantHint.Trim().ToLowerInvariant();
        }

        return $"{request.Resource.Organization.ToLowerInvariant()}-tenant";
    }

    private static string CreateSeed(CredentialRequest request, string account, string tenant)
        => CacheKeySchema.Create(request, account, tenant).Value;

    private static string CreateDeterministicValue(string prefix, string seed)
    {
        string hash = Convert
            .ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(seed)))
            .ToLowerInvariant();
        return $"{prefix}-{hash[..24]}";
    }
}
