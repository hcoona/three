using System.Text.Json.Serialization;
using WorkflowDelivery.CI;
using WorkflowDelivery.Repository;

namespace WorkflowDelivery;

// The endpoint-selected MSBuild process reads/writes only these native task messages.
[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    WriteIndented = true, RespectNullableAnnotations = true,
    RespectRequiredConstructorParameters = true,
    UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow)]
[JsonSerializable(typeof(DotNetGraphRequest))]
[JsonSerializable(typeof(DotNetGraphResponse))]
[JsonSerializable(typeof(DotNetBuildRequest))]
[JsonSerializable(typeof(DotNetBuildResponse))]
[JsonSerializable(typeof(DotNetTestRequest))]
[JsonSerializable(typeof(DotNetTestEntryResult))]
[JsonSerializable(typeof(KeyValuePair<string, string>[]))]
internal partial class DotNetNativeJson : JsonSerializerContext;
