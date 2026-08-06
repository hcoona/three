using Hcoona.AzureAuth.CredProvider.Contracts;
using Hcoona.AzureAuth.CredProvider.Platform.Configuration;
using Xunit;

namespace Hcoona.AzureAuth.CredProvider.Platform.Tests;

#pragma warning disable CA1707
public sealed class ConfigurationOwnershipManifestPolicyTests
{
    [Fact]
    public void IsValid_WhenTargetPathContainsNul_ReturnsFalse()
    {
        var manifest = new ConfigurationOwnershipManifest
        {
            ManifestId = "test-manifest",
            OwnerProductId = "azureauth-credprovider",
            Scope = ConfigurationScope.User,
            EntrySelector = "test-key",
            Entries =
            [
                new ConfigurationOwnershipManifestEntry
                {
                    Sequence = 1,
                    TargetKind = ConfigurationTargetKind.Npmrc,
                    TargetPathOrName = "/home/alice/.npmrc\0forged",
                    Key = "test-key",
                },
            ],
        };

        Assert.False(ConfigurationOwnershipManifestPolicy.IsValid(manifest));
        Assert.Throws<ArgumentException>(() =>
            ConfigurationOwnershipManifestPolicy.EnsureValid(manifest)
        );
    }
}
#pragma warning restore CA1707
