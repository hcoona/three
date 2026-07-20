namespace Hcoona.AzureAuth.CredProvider.Contracts;

public static class ContractVersions
{
    public const int CredentialContractMajor = 1;
    public const string CredentialContractId = "azureauth-credprovider-credential-contract-v1";

    /// <summary>
    /// Public contract major and identifier for the separate v2 credential request root.
    /// Work-package-1 exposes the v2 contract surface but does not yet route it through
    /// CredentialCoreService.
    /// </summary>
    public const int CredentialContractV2Major = 2;
    public const string CredentialContractV2Id = "azureauth-credprovider-credential-contract-v2";
    public const int CacheKeySchemaMajor = 1;
    public const string CacheKeySchemaPrefix = "azdo-cache-v1";
    public const int ConfigurationChangePlanMajor = 1;
    public const int DoctorCheckMajor = 1;
    public const int AdapterHostResultMajor = 1;
    public const int KeyringHelperMajor = 2;
    public const string KeyringHelperContractId = "keyring-helper-v2";
}
