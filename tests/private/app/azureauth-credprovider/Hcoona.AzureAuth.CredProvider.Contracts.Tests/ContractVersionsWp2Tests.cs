using Hcoona.AzureAuth.CredProvider.Contracts;
using Xunit;

namespace Hcoona.AzureAuth.CredProvider.Contracts.Tests;

public sealed class ContractVersionsWp2Tests
{
    [Fact]
    public void Wp2ContractIdsAndSchemaMajorsRemainFrozen()
    {
        Assert.Equal(1, ContractVersions.AzureAuthProviderConfigSchemaMajor);
        Assert.Equal(
            "azureauth-provider-config-v1",
            ContractVersions.AzureAuthProviderConfigContractId
        );
        Assert.Equal(
            "azureauth-account-binding-v1",
            ContractVersions.AzureAuthAccountBindingContractId
        );
    }
}
