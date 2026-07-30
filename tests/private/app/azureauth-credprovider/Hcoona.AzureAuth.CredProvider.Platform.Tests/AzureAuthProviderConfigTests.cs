using System.Text.Json;
using Hcoona.AzureAuth.CredProvider.Contracts;
using Hcoona.AzureAuth.CredProvider.Platform.AzureAuthProvider;
using Xunit;

namespace Hcoona.AzureAuth.CredProvider.Platform.Tests;

public sealed class AzureAuthProviderConfigTests
{
    [Fact]
    public void AzureAuthConfigPersistsOnlySelectionAndSupportedVersion()
    {
        AzureAuthProviderConfig config = AzureAuthProviderConfig.CreateAzureAuth();
        string json = AzureAuthProviderConfigJson.Serialize(config);

        Assert.Equal(config, AzureAuthProviderConfigJson.Deserialize(json));
        Assert.Equal(
            """{"schemaVersion":1,"selection":"azureAuth","azureAuthVersion":"0.9.5"}""",
            json
        );
        Assert.DoesNotContain("path", json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("sha", json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("signer", json, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void DirectMsalConfigDoesNotCarryAzureAuthVersion()
    {
        AzureAuthProviderConfig config = AzureAuthProviderConfig.CreateDirectMsal();
        string json = AzureAuthProviderConfigJson.Serialize(config);

        Assert.Equal(AzureAuthProviderSelection.DirectMsal, config.Selection);
        Assert.Null(config.AzureAuthVersion);
        Assert.Equal("""{"schemaVersion":1,"selection":"directMsal"}""", json);
        Assert.Equal(config, AzureAuthProviderConfigJson.Deserialize(json));
    }

    [Theory]
    [InlineData("0.9.4")]
    [InlineData("0.9.5.0")]
    [InlineData("")]
    public void PolicyRejectsUnsupportedAzureAuthVersion(string version)
    {
        AzureAuthProviderConfig config = AzureAuthProviderConfig.CreateAzureAuth(version);

        Assert.Throws<ArgumentException>(() => AzureAuthProviderConfigPolicy.EnsureValid(config));
    }

    [Fact]
    public void JsonRejectsMalformedOrUnknownContent()
    {
        Assert.Throws<JsonException>(() => AzureAuthProviderConfigJson.Deserialize("{"));
        Assert.Throws<JsonException>(() =>
            AzureAuthProviderConfigJson.Deserialize(
                """{"schemaVersion":1,"selection":"directMsal","unknown":true}"""
            )
        );
        Assert.Throws<ArgumentException>(() =>
            AzureAuthProviderConfigJson.Deserialize(
                """{"schemaVersion":1,"selection":"azureAuth","azureAuthVersion":"0.9.4"}"""
            )
        );
    }
}
