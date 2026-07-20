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

    [Fact]
    public void TokenExchangeMaterialToStringRedactsPasswordAndBearerToken()
    {
        var material = new TokenExchangeMaterial
        {
            Username = "AzureDevOps",
            Password = "safe-password",
            BearerToken = "safe-bearer-token",
        };

        string text = material.ToString();

        Assert.Equal(
            "TokenExchangeMaterial { Username = AzureDevOps, Password = <redacted>, "
                + "BearerToken = <redacted> }",
            text);
        Assert.DoesNotContain(
            Assert.IsType<string>(material.Password),
            text,
            StringComparison.Ordinal);
        Assert.DoesNotContain(
            Assert.IsType<string>(material.BearerToken),
            text,
            StringComparison.Ordinal);
    }

    [Fact]
    public void SecretTextAndAcquiredAccessTokenToStringRedactTokenValue()
    {
        var secret = new SecretText { Value = "opaque-access-token" };
        var token = new AcquiredAccessToken
        {
            AccountId = null,
            TenantId = "tenant-1",
            DeploymentKey = "deployment-key",
            Token = secret,
            ExpiresAt = null,
        };

        string secretText = secret.ToString();
        string tokenText = token.ToString();
        string resultText = AcquiredAccessTokenResult.Success(token).ToString();

        Assert.Equal("<redacted>", secretText);
        Assert.DoesNotContain(secret.Value, secretText, StringComparison.Ordinal);
        Assert.DoesNotContain(secret.Value, tokenText, StringComparison.Ordinal);
        Assert.DoesNotContain(secret.Value, resultText, StringComparison.Ordinal);
        Assert.Equal(
            "AcquiredAccessToken { AccountId = <unknown>, TenantId = tenant-1, "
                + "DeploymentKey = deployment-key, "
                + "Token = <redacted>, ExpiresAt = <unknown> }",
            tokenText);
        Assert.Equal(
            "AcquiredAccessTokenResult { Status = Success, AccessToken = "
                + "AcquiredAccessToken { AccountId = <unknown>, TenantId = tenant-1, "
                + "DeploymentKey = deployment-key, "
                + "Token = <redacted>, ExpiresAt = <unknown> } }",
            resultText);
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
