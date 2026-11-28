using System.Text.Json.Serialization;

namespace WorkflowDelivery.Destinations;

[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.SnakeCaseLower,
    RespectNullableAnnotations = true, RespectRequiredConstructorParameters = true,
    UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow)]
[JsonSerializable(typeof(RubySourceRequest))]
[JsonSerializable(typeof(RubySourceAnswer))]
internal partial class RubySourceJson : JsonSerializerContext;

internal sealed record RubySourceRequest(string Host, string Name, string Version,
    string Directory);
internal sealed record RubySourceAnswer(string State, string? Name = null, string? Version = null,
    string? Platform = null, string? Path = null, long? Size = null, string? Sha256 = null);
