using System.Text.Json.Serialization;
using WorkflowDelivery.Platform;

namespace WorkflowDelivery.Destinations;

// Runner-local tool selection, scratch paths and command inputs are not Release transfers.
[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    Converters = [typeof(ToolTerminationJsonConverter)], WriteIndented = true,
    RespectNullableAnnotations = true, RespectRequiredConstructorParameters = true,
    UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow)]
[JsonSerializable(typeof(PythonIndexTools))]
[JsonSerializable(typeof(PythonIndexToolRequest))]
[JsonSerializable(typeof(PythonIndexToolResult))]
[JsonSerializable(typeof(PythonPublisherTools))]
[JsonSerializable(typeof(RubySourceTools))]
[JsonSerializable(typeof(RubyPublisherTools))]
internal partial class DestinationToolJson : JsonSerializerContext;

internal sealed class ToolTerminationJsonConverter()
    : JsonStringEnumConverter<NativeTermination>(allowIntegerValues: false);
