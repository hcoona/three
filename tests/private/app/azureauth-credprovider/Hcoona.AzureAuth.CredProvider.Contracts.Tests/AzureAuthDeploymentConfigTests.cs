using System.Text.Json;
using System.Text.Json.Serialization;
using Hcoona.AzureAuth.CredProvider.Contracts.AzureAuthDeployment;
using Xunit;

namespace Hcoona.AzureAuth.CredProvider.Contracts.Tests;

public sealed class AzureAuthDeploymentConfigTests
{
    [Fact]
    public void StrictJsonRoundTripsAndBlocksDirectSerializerUse()
    {
        AzureAuthDeploymentConfig config = CreateConfig();
        string json = AzureAuthDeploymentConfigJson.Serialize(config);

        Assert.Equal(config, AzureAuthDeploymentConfigJson.Deserialize(json));
        Assert.Throws<NotSupportedException>(() =>
            JsonSerializer.Serialize(
                config,
                AzureAuthDeploymentContractJsonContext.Default.AzureAuthDeploymentConfig)
        );
        Assert.Throws<NotSupportedException>(() =>
            JsonSerializer.Deserialize(
                json,
                AzureAuthDeploymentContractJsonContext.Default.AzureAuthDeploymentConfig)
        );
    }

    [Fact]
    public void SerializeEmitsExactFrozenDeploymentConfigJson()
    {
        Assert.Equal(
            """{"schemaVersion":1,"executablePath":"C:\\Tools\\AzureAuth.exe","executableSha256":"aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa","signerIdentity":"CN=AzureAuth, O=Hcoona, C=US","publisherName":"Hcoona AzureAuth","executableVersion":"1.0.0.0","provenanceIdentifier":"foundation/wp2"}""",
            AzureAuthDeploymentConfigJson.Serialize(CreateConfig())
        );
    }

    [Theory]
    [InlineData(" ")]
    [InlineData("{")]
    [InlineData(
        """
        {
          "schemaVersion": 1,
          "executablePath": "C:\\Tools\\AzureAuth.exe",
          "executableSha256": "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa",
          "signerIdentity": "CN=AzureAuth, O=Hcoona, C=US",
          "publisherName": "Hcoona AzureAuth",
          "executableVersion": "1.0.0.0",
          "provenanceIdentifier": "foundation/wp2",
          "unexpected": "value"
        }
        """
    )]
    [InlineData(
        """
        {
          "SchemaVersion": 1,
          "executablePath": "C:\\Tools\\AzureAuth.exe",
          "executableSha256": "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa",
          "signerIdentity": "CN=AzureAuth, O=Hcoona, C=US",
          "publisherName": "Hcoona AzureAuth",
          "executableVersion": "1.0.0.0",
          "provenanceIdentifier": "foundation/wp2"
        }
        """
    )]
    [InlineData(
        """
        {
          "schemaVersion": "1",
          "executablePath": "C:\\Tools\\AzureAuth.exe",
          "executableSha256": "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa",
          "signerIdentity": "CN=AzureAuth, O=Hcoona, C=US",
          "publisherName": "Hcoona AzureAuth",
          "executableVersion": "1.0.0.0",
          "provenanceIdentifier": "foundation/wp2"
        }
        """
    )]
    [InlineData(
        """
        {
          "schemaVersion": 1,
          "executablePath": 42,
          "executableSha256": "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa",
          "signerIdentity": "CN=AzureAuth, O=Hcoona, C=US",
          "publisherName": "Hcoona AzureAuth",
          "executableVersion": "1.0.0.0",
          "provenanceIdentifier": "foundation/wp2"
        }
        """
    )]
    [InlineData(
        """
        {
          "schemaVersion": 1,
          "executablePath": "C:\\Tools\\AzureAuth.exe",
          "executableSha256": "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa",
          "signerIdentity": "CN=AzureAuth, O=Hcoona, C=US",
          "publisherName": "first",
          "publisherName": "second",
          "executableVersion": "1.0.0.0",
          "provenanceIdentifier": "foundation/wp2"
        }
        """
    )]
    public void StrictJsonRejectsMalformedUnknownCaseDuplicateAndNumericPayloads(string json)
    {
        Assert.Throws<JsonException>(() => AzureAuthDeploymentConfigJson.Deserialize(json));
    }

    [Fact]
    public void StrictJsonRejectsNullInput()
    {
        Assert.Throws<ArgumentNullException>(() => AzureAuthDeploymentConfigJson.Deserialize(null!));
    }

    [Theory]
    [InlineData("AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA")]
    [InlineData("aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa")]
    public void DeploymentConfigRejectsInvalidDigest(string digest)
    {
        AzureAuthDeploymentConfig config = CreateConfig() with { ExecutableSha256 = digest };

        Assert.Throws<ArgumentException>(() => AzureAuthDeploymentConfigPolicy.EnsureValid(config));
    }

    [Theory]
    [InlineData(" CN=AzureAuth, O=Hcoona, C=US", "Hcoona AzureAuth", "1.0.0.0", "foundation/wp2")]
    [InlineData("CN=AzureAuth, O=Hcoona, C=US", "Hcoona\u202EAzureAuth", "1.0.0.0", "foundation/wp2")]
    [InlineData("CN=AzureAuth, O=Hcoona, C=US", "Hcoona AzureAuth", "1.0-beta", "foundation/wp2")]
    [InlineData("CN=AzureAuth, O=Hcoona, C=US", "Hcoona AzureAuth", "1.0.0.0", "Foundation:Build")]
    public void DeploymentConfigRejectsInvalidPins(
        string signerIdentity,
        string publisherName,
        string executableVersion,
        string provenanceIdentifier
    )
    {
        AzureAuthDeploymentConfig config = CreateConfig() with
        {
            SignerIdentity = signerIdentity,
            PublisherName = publisherName,
            ExecutableVersion = executableVersion,
            ProvenanceIdentifier = provenanceIdentifier,
        };

        Assert.Throws<ArgumentException>(() => AzureAuthDeploymentConfigPolicy.EnsureValid(config));
    }

    [Theory]
    [InlineData(@"\\server\share\AzureAuth.exe")]
    [InlineData(@"\\?\C:\tools\AzureAuth.exe")]
    [InlineData(@"\\.\pipe\AzureAuth.exe")]
    [InlineData(@"\tools\AzureAuth.exe")]
    [InlineData(@"/tools/AzureAuth.exe")]
    [InlineData(@"c:\tools\AzureAuth.exe")]
    [InlineData(@"C:tools\AzureAuth.exe")]
    [InlineData(@"C:/tools/AzureAuth.exe")]
    [InlineData(@"C:\tools\\AzureAuth.exe")]
    [InlineData(@"C:\tools\..\AzureAuth.exe")]
    [InlineData(@"C:\tools\.\AzureAuth.exe")]
    [InlineData(@"C:\PROGRA~1\AzureAuth\AzureAuth.exe")]
    [InlineData(@"C:\tools\AZUREA~1\AzureAuth.exe")]
    [InlineData(@"C:\tools\AzureAuth.exe:Zone.Identifier")]
    [InlineData(@"C:\%temp%\AzureAuth.exe")]
    [InlineData(@"C:\CON\AzureAuth.exe")]
    [InlineData(@"C:\con.txt\AzureAuth.exe")]
    [InlineData(@"C:\tools.\AzureAuth.exe")]
    [InlineData(@"C:\tools \AzureAuth.exe")]
    [InlineData(@"C:\tools\azureauth.exe")]
    [InlineData("C:\\tools\\Az\u202EureAuth.exe")]
    [InlineData("C:\\tools\\ＡzureAuth.exe")]
    public void WindowsPathPolicyRejectsUnsafeExecutablePaths(string path)
    {
        Assert.Throws<ArgumentException>(() => WindowsPathPolicy.ValidateExecutablePath(path));
    }

    [Fact]
    public void WindowsPathPolicyAcceptsExactExecutablePath()
    {
        WindowsPathPolicy.ValidateExecutablePath(@"C:\Program Files\AzureAuth\AzureAuth.exe");
    }

    private static AzureAuthDeploymentConfig CreateConfig() =>
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

[JsonSerializable(typeof(AzureAuthDeploymentConfig))]
internal sealed partial class AzureAuthDeploymentContractJsonContext : JsonSerializerContext;
