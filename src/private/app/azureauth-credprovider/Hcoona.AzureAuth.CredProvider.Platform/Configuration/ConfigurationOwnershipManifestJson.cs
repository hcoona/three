using System.Text.Json;
using System.Text.Json.Serialization;
using Hcoona.AzureAuth.CredProvider.Contracts;

namespace Hcoona.AzureAuth.CredProvider.Platform.Configuration;

[JsonSerializable(typeof(ConfigurationOwnershipManifest))]
[JsonSerializable(typeof(ConfigurationOwnershipManifestEntry))]
[JsonSerializable(typeof(ConfigurationDryRunPlan))]
[JsonSerializable(typeof(ConfigurationPlanResult))]
[JsonSerializable(typeof(ConfigurationPlannedChange))]
[JsonSerializable(typeof(ConfigurationPlannedOperation))]
[JsonSerializable(typeof(IReadOnlyList<ConfigurationOwnershipManifestEntry>))]
[JsonSerializable(typeof(ConfigurationOwnershipManifestEntry[]))]
[JsonSerializable(typeof(IReadOnlyList<ConfigurationPlannedChange>))]
[JsonSerializable(typeof(ConfigurationPlannedChange[]))]
[JsonSerializable(typeof(IReadOnlyList<ConfigurationPlannedOperation>))]
[JsonSerializable(typeof(ConfigurationPlannedOperation[]))]
[JsonSerializable(typeof(IReadOnlyDictionary<string, string>))]
[JsonSerializable(typeof(Dictionary<string, string>))]
[JsonSerializable(typeof(ConfigurationChangeOperation))]
[JsonSerializable(typeof(ConfigurationTargetKind))]
[JsonSerializable(typeof(ConfigurationScope))]
[JsonSourceGenerationOptions(
    JsonSerializerDefaults.Web,
    WriteIndented = false,
    GenerationMode = JsonSourceGenerationMode.Metadata,
    UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow
)]
internal sealed partial class ConfigurationOwnershipManifestJson : JsonSerializerContext
{
    public static JsonSerializerOptions CreateSerializerOptions()
    {
        JsonSerializerOptions options = ContractJson.CreateSerializerOptions();
        options.TypeInfoResolver = Default;
        options.UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow;
        return options;
    }
}
