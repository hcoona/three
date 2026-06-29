using System.Globalization;
using Hcoona.AzureAuth.CredProvider.Platform.CredentialCore;
using Xunit;

namespace Hcoona.AzureAuth.CredProvider.Platform.Tests;

public sealed class CacheBoundaryToStringTests
{
    private static readonly DateTimeOffset ExpiresAt = new(2030, 1, 1, 0, 0, 0, TimeSpan.Zero);

    [Fact]
    public void IdentityMaterialToStringRedactsSecretAndAccessToken()
    {
        IdentityMaterial identity = CreateIdentityMaterial();

        string text = identity.ToString();

        Assert.Equal(
            string.Format(
                CultureInfo.InvariantCulture,
                "IdentityMaterial {{ Account = {0}, Tenant = {1}, Secret = <redacted>, "
                    + "AccessToken = <redacted>, ExpiresAt = {2} }}",
                identity.Account,
                identity.Tenant,
                ExpiresAt.ToString("O", CultureInfo.InvariantCulture)
            ),
            text);
        Assert.DoesNotContain(
            Assert.IsType<string>(identity.Secret),
            text,
            StringComparison.Ordinal);
        Assert.DoesNotContain(
            Assert.IsType<string>(identity.AccessToken),
            text,
            StringComparison.Ordinal);
    }

    [Fact]
    public void DerivedCredentialCacheReadResultToStringRedactsNestedIdentitySecrets()
    {
        IdentityMaterial identity = CreateIdentityMaterial();

        string text = DerivedCredentialCacheReadResult.Hit(identity).ToString();

        Assert.Equal(
            string.Format(
                CultureInfo.InvariantCulture,
                "DerivedCredentialCacheReadResult {{ Status = Hit, Identity = "
                    + "IdentityMaterial {{ Account = {0}, Tenant = {1}, Secret = <redacted>, "
                    + "AccessToken = <redacted>, ExpiresAt = {2} }} }}",
                identity.Account,
                identity.Tenant,
                ExpiresAt.ToString("O", CultureInfo.InvariantCulture)
            ),
            text);
        Assert.DoesNotContain(
            Assert.IsType<string>(identity.Secret),
            text,
            StringComparison.Ordinal);
        Assert.DoesNotContain(
            Assert.IsType<string>(identity.AccessToken),
            text,
            StringComparison.Ordinal);
    }

    private static IdentityMaterial CreateIdentityMaterial() =>
        new()
        {
            Account = "user@example.com",
            Tenant = "tenant-1",
            Secret = "safe-secret",
            AccessToken = "safe-token",
            ExpiresAt = ExpiresAt,
        };
}
