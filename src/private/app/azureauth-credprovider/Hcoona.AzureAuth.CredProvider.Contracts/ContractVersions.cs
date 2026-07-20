namespace Hcoona.AzureAuth.CredProvider.Contracts;

/// <summary>Defines stable versions and identifiers for public contracts.</summary>
public static class ContractVersions
{
    /// <summary>Gets the major version of the original credential contract.</summary>
    public const int CredentialContractMajor = 1;

    /// <summary>Gets the identifier of the original credential contract.</summary>
    public const string CredentialContractId = "azureauth-credprovider-credential-contract-v1";

    /// <summary>Gets the major version of the separate v2 credential request contract.</summary>
    public const int CredentialContractV2Major = 2;

    /// <summary>Gets the identifier of the separate v2 credential request contract.</summary>
    public const string CredentialContractV2Id = "azureauth-credprovider-credential-contract-v2";

    /// <summary>Gets the major version of the derived-credential cache key schema.</summary>
    public const int CacheKeySchemaMajor = 1;

    /// <summary>Gets the prefix of the derived-credential cache key schema.</summary>
    public const string CacheKeySchemaPrefix = "azdo-cache-v1";

    /// <summary>Gets the major version of configuration change plans.</summary>
    public const int ConfigurationChangePlanMajor = 1;

    /// <summary>Gets the major version of doctor checks.</summary>
    public const int DoctorCheckMajor = 1;

    /// <summary>Gets the major version of adapter-host results.</summary>
    public const int AdapterHostResultMajor = 1;

    /// <summary>Gets the major version of the keyring-helper contract.</summary>
    public const int KeyringHelperMajor = 2;

    /// <summary>Gets the identifier of the keyring-helper contract.</summary>
    public const string KeyringHelperContractId = "keyring-helper-v2";

    /// <summary>Gets the schema version of the AzureAuth deployment configuration contract.</summary>
    public const int AzureAuthDeploymentConfigSchemaMajor = 1;

    /// <summary>Gets the identifier of the AzureAuth deployment configuration contract.</summary>
    public const string AzureAuthDeploymentConfigContractId = "azureauth-deployment-config-v1";

    /// <summary>Gets the schema version of the AzureAuth account-binding contract.</summary>
    public const int AzureAuthAccountBindingSchemaMajor = 1;

    /// <summary>Gets the identifier of the AzureAuth account-binding contract.</summary>
    public const string AzureAuthAccountBindingContractId = "azureauth-account-binding-v1";

    /// <summary>Gets the schema version of the AzureAuth provider configuration contract.</summary>
    public const int AzureAuthProviderConfigSchemaMajor = 1;

    /// <summary>Gets the identifier of the AzureAuth provider configuration contract.</summary>
    public const string AzureAuthProviderConfigContractId = "azureauth-provider-config-v1";
}
