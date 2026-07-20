using System.Text.Json;
using System.Text.Json.Serialization;
using Hcoona.AzureAuth.CredProvider.Contracts;
using Hcoona.AzureAuth.CredProvider.Contracts.AzureAuthDeployment;
using Hcoona.AzureAuth.CredProvider.Platform.AzureAuthProvider;
using Xunit;

namespace Hcoona.AzureAuth.CredProvider.Platform.Tests;

public sealed class AzureAuthProviderConfigTests
{
    [Fact]
    public void DefaultFactorySelectsDirectMsal()
    {
        AzureAuthProviderConfig config = AzureAuthProviderConfig.CreateDefault();

        Assert.Equal(ContractVersions.AzureAuthProviderConfigSchemaMajor, config.SchemaVersion);
        Assert.Equal(AzureAuthProviderSelection.DirectMsal, config.Selection);
        Assert.Null(config.DeploymentConfig);
    }

    [Fact]
    public void StrictJsonRoundTripsAndBlocksDirectSerializerUse()
    {
        AzureAuthProviderConfig config = AzureAuthProviderConfig.CreateAzureAuth(CreateDeploymentConfig());
        string json = AzureAuthProviderConfigJson.Serialize(config);

        Assert.Equal(config, AzureAuthProviderConfigJson.Deserialize(json));
        Assert.Throws<NotSupportedException>(() =>
            JsonSerializer.Serialize(config, PlatformPersistedJsonContext.Default.AzureAuthProviderConfig)
        );
        Assert.Throws<NotSupportedException>(() =>
            JsonSerializer.Deserialize(
                json,
                PlatformPersistedJsonContext.Default.AzureAuthProviderConfig)
        );
    }

    [Fact]
    public void SerializeEmitsExactFrozenDirectMsalProviderConfigJson()
    {
        Assert.Equal(
            """{"schemaVersion":1,"selection":"directMsal","deploymentConfig":null}""",
            AzureAuthProviderConfigJson.Serialize(AzureAuthProviderConfig.CreateDefault())
        );
    }

    [Fact]
    public void SerializeEmitsExactFrozenAzureAuthProviderConfigJson()
    {
        Assert.Equal(
            """{"schemaVersion":1,"selection":"azureAuth","deploymentConfig":{"schemaVersion":1,"executablePath":"C:\\Tools\\AzureAuth.exe","executableSha256":"aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa","signerIdentity":"CN=AzureAuth, O=Hcoona, C=US","publisherName":"Hcoona AzureAuth","executableVersion":"1.0.0.0","provenanceIdentifier":"foundation/wp2"}}""",
            AzureAuthProviderConfigJson.Serialize(
                AzureAuthProviderConfig.CreateAzureAuth(CreateDeploymentConfig())
            )
        );
    }

    [Theory]
    [InlineData(" ")]
    [InlineData("{")]
    [InlineData(
        """
        {
          "schemaVersion": 1,
          "selection": "directMsal",
          "unexpected": "value"
        }
        """
    )]
    [InlineData(
        """
        {
          "SchemaVersion": 1,
          "selection": "directMsal"
        }
        """
    )]
    [InlineData(
        """
        {
          "schemaVersion": 1,
          "selection": 1
        }
        """
    )]
    [InlineData(
        """
        {
          "schemaVersion": 1,
          "selection": "directMsal",
          "selection": "azureAuth"
        }
        """
    )]
    public void StrictJsonRejectsMalformedUnknownCaseDuplicateAndNumericPayloads(string json)
    {
        Assert.Throws<JsonException>(() => AzureAuthProviderConfigJson.Deserialize(json));
    }

    [Theory]
    [InlineData(
        """
        {
          "schemaVersion": 1,
          "selection": "unspecified"
        }
        """
    )]
    [InlineData(
        """
        {
          "schemaVersion": 1,
          "selection": "azureAuth"
        }
        """
    )]
    [InlineData(
        """
        {
          "schemaVersion": 1,
          "selection": "directMsal",
          "deploymentConfig": {
            "schemaVersion": 1,
            "executablePath": "C:\\Tools\\AzureAuth.exe",
            "executableSha256": "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa",
            "signerIdentity": "CN=AzureAuth, O=Hcoona, C=US",
            "publisherName": "Hcoona AzureAuth",
            "executableVersion": "1.0.0.0",
            "provenanceIdentifier": "foundation/wp2"
          }
        }
        """
    )]
    public void PolicyRejectsSemanticallyInvalidPayloads(string json)
    {
        Assert.Throws<ArgumentException>(() => AzureAuthProviderConfigJson.Deserialize(json));
    }

    [Fact]
    public void StrictJsonRejectsNullInput()
    {
        Assert.Throws<ArgumentNullException>(() => AzureAuthProviderConfigJson.Deserialize(null!));
    }

    private static AzureAuthDeploymentConfig CreateDeploymentConfig() =>
        new()
        {
            SchemaVersion = ContractVersions.AzureAuthDeploymentConfigSchemaMajor,
            ExecutablePath = @"C:\Tools\AzureAuth.exe",
            ExecutableSha256 = new string('a', 64),
            SignerIdentity = "CN=AzureAuth, O=Hcoona, C=US",
            PublisherName = "Hcoona AzureAuth",
            ExecutableVersion = "1.0.0.0",
            ProvenanceIdentifier = "foundation/wp2",
        };
}

[JsonSerializable(typeof(AzureAuthProviderConfig))]
[JsonSerializable(typeof(AzureAuthBinding))]
internal sealed partial class PlatformPersistedJsonContext : JsonSerializerContext;
