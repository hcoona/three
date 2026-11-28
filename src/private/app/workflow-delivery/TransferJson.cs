using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;
using WorkflowDelivery.CI;
using WorkflowDelivery.CI.Callers;
using WorkflowDelivery.Destinations;
using WorkflowDelivery.Platform;
using WorkflowDelivery.Release;
using WorkflowDelivery.Repository;

namespace WorkflowDelivery;

[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    Converters = new[] { typeof(CheckStatusJsonConverter), typeof(NativeTerminationJsonConverter),
        typeof(CheckOriginJsonConverter), typeof(ReleaseCheckStatusJsonConverter),
        typeof(ReleaseFileStateJsonConverter), typeof(ReleaseBusinessStateJsonConverter),
        typeof(RubySourceStateJsonConverter), typeof(StoreStateJsonConverter) },
    WriteIndented = true,
    RespectNullableAnnotations = true,
    RespectRequiredConstructorParameters = true,
    UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow
)]
[JsonSerializable(typeof(JsonElement))]
[JsonSerializable(typeof(string[]))]
[JsonSerializable(typeof(ScopeCoordinate[]))]
[JsonSerializable(typeof(CiCallerEndpoints))]
[JsonSerializable(typeof(CiCallerControl))]
[JsonSerializable(typeof(CiCallerCommand))]
[JsonSerializable(typeof(PythonCiCallerContext))]
[JsonSerializable(typeof(RubyCiCallerContext))]
[JsonSerializable(typeof(NodeCiCallerContext))]
[JsonSerializable(typeof(PythonCiCallerReadback))]
[JsonSerializable(typeof(RubyCiCallerReadback))]
[JsonSerializable(typeof(GithubNpmToolSelection))]
[JsonSerializable(typeof(PreparedGithubAnchor))]
[JsonSerializable(typeof(ReleasePreparationRequest))]
[JsonSerializable(typeof(ReleasePreparationResult))]
[JsonSerializable(typeof(ReleaseObservationResult))]
[JsonSerializable(typeof(ReleasePublicationResult))]
[JsonSerializable(typeof(ReleaseCompletionResult))]
[JsonSerializable(typeof(ReleaseDryRun))]
[JsonSerializable(typeof(ReleaseOutcome))]
[JsonSerializable(typeof(PythonIndexTools))]
[JsonSerializable(typeof(PythonIndexToolRequest))]
[JsonSerializable(typeof(PythonIndexToolResult))]
[JsonSerializable(typeof(PythonPublisherTools))]
[JsonSerializable(typeof(RubySourceTools))]
[JsonSerializable(typeof(RubyPublisherTools))]
[JsonSerializable(typeof(ControlTestInputRequest))]
[JsonSerializable(typeof(DotNetRunRequest))]
[JsonSerializable(typeof(DotNetRunResult))]
[JsonSerializable(typeof(DotNetGroupRequest))]
[JsonSerializable(typeof(DotNetGroupReadback))]
[JsonSerializable(typeof(DotNetGroupRunResult))]
[JsonSerializable(typeof(PythonGroupRunRequest))]
[JsonSerializable(typeof(PythonGroupRunResult))]
[JsonSerializable(typeof(PythonGroupRequest))]
[JsonSerializable(typeof(PythonGroupReadback))]
[JsonSerializable(typeof(PythonGroupPreparationRequest))]
[JsonSerializable(typeof(PythonGroupPreparationResult))]
[JsonSerializable(typeof(PythonPreparationCommand[]))]
[JsonSerializable(typeof(RubyGroupPreparationRequest))]
[JsonSerializable(typeof(RubyGroupPreparationResult))]
[JsonSerializable(typeof(RubyGroupRequest))]
[JsonSerializable(typeof(RubyGroupReadback))]
[JsonSerializable(typeof(RubyGroupRunRequest))]
[JsonSerializable(typeof(RubyGroupRun))]
[JsonSerializable(typeof(NodeGroupRequest))]
[JsonSerializable(typeof(NodeGroupReadback))]
[JsonSerializable(typeof(Dictionary<string, string[]>),
    TypeInfoPropertyName = "RetainedDirectories")]
[JsonSerializable(typeof(NodeRunRequest))]
[JsonSerializable(typeof(NodeRunResult))]
[JsonSerializable(typeof(CiPlan))]
[JsonSerializable(typeof(CheckResult[]))]
[JsonSerializable(typeof(DotNetToolGroup[]))]
[JsonSerializable(typeof(CiGroupReceipt))]
[JsonSerializable(typeof(CiOutcome))]
internal partial class TransferJson : JsonSerializerContext
{
    internal JsonTypeInfo<ReleasePreparationResult> NuGetPreparationResult =>
        ReleasePreparationResult;
    internal JsonTypeInfo<ReleaseObservationResult> NuGetObservationResult =>
        ReleaseObservationResult;
    internal JsonTypeInfo<ReleaseCompletionResult> NuGetCompletionResult =>
        ReleaseCompletionResult;
}

internal sealed class CheckStatusJsonConverter()
    : JsonStringEnumConverter<CheckStatus>(allowIntegerValues: false);

internal sealed class NativeTerminationJsonConverter()
    : JsonStringEnumConverter<NativeTermination>(allowIntegerValues: false);

internal sealed class CheckOriginJsonConverter()
    : JsonStringEnumConverter<CheckOrigin>(allowIntegerValues: false);

internal sealed class ReleaseCheckStatusJsonConverter()
    : JsonStringEnumConverter<ReleaseCheckStatus>(allowIntegerValues: false);

internal sealed class ReleaseFileStateJsonConverter()
    : JsonStringEnumConverter<ReleaseFileState>(allowIntegerValues: false);

internal sealed class ReleaseBusinessStateJsonConverter()
    : JsonStringEnumConverter<ReleaseBusinessState>(allowIntegerValues: false);

internal sealed class RubySourceStateJsonConverter()
    : JsonStringEnumConverter<RubySourceState>(allowIntegerValues: false);

internal sealed class StoreStateJsonConverter()
    : JsonStringEnumConverter<StoreState>(allowIntegerValues: false);
