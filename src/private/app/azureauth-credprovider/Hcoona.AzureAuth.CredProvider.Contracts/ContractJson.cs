using System.Text.Json;
using System.Text.Json.Serialization;

namespace Hcoona.AzureAuth.CredProvider.Contracts;

public static class ContractJson
{
    public static JsonSerializerOptions CreateSerializerOptions()
    {
        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web)
        {
            NumberHandling = JsonNumberHandling.Strict,
            WriteIndented = false,
            TypeInfoResolver = ContractJsonSerializerContext.Default,
        };
        AddEnumConverter<AcquisitionMode>(options);
        AddEnumConverter<AdapterHostExitCode>(options);
        AddEnumConverter<AdapterProtocol>(options);
        AddEnumConverter<CachePolicyMode>(options);
        AddEnumConverter<ConfigurationChangeOperation>(options);
        AddEnumConverter<ConfigurationDeclarationPreservation>(options);
        AddEnumConverter<ConfigurationScope>(options);
        AddEnumConverter<ConfigurationTargetKind>(options);
        AddEnumConverter<ConfigurationTemporaryContainerKind>(options);
        AddEnumConverter<CredentialEcosystem>(options);
        AddEnumConverter<CredentialErrorKind>(options);
        AddEnumConverter<CredentialKind>(options);
        AddEnumConverter<CredentialOperation>(options);
        AddEnumConverter<CredentialResultStatus>(options);
        AddEnumConverter<DoctorCheckSeverity>(options);
        AddEnumConverter<DoctorCheckStatus>(options);
        AddEnumConverter<IdentityFlow>(options);
        AddEnumConverter<IdentityFlowState>(options);
        AddEnumConverter<InteractivePolicy>(options);
        AddEnumConverter<KeyringHelperMode>(options);
        AddEnumConverter<TokenAudience>(options);
        return options;
    }

    private static void AddEnumConverter<TEnum>(JsonSerializerOptions options)
        where TEnum : struct, Enum =>
        options.Converters.Add(new ContractEnumJsonConverter<TEnum>());
}
