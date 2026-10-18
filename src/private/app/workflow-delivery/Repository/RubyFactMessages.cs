using System.Text.Json.Serialization;

namespace WorkflowDelivery.Repository;

// Application-owned wire projections of RubyGems/Bundler answers, not upstream format models.
internal record RubyGemMessage
{
    public required string Name { get; init; }
    public required string Version { get; init; }
    public required string Platform { get; init; }
    public required string[] RequirePaths { get; init; }
    public required string[] Files { get; init; }
    public required RubyDependency[] RuntimeDependencies { get; init; }
    public string[]? Extensions { get; init; }
    public Dictionary<string, string>? Metadata { get; init; }
}
internal sealed record RubySourceMessage : RubyGemMessage
{
    public required string Gemspec { get; init; }
}
internal sealed record RubyArchiveMessage : RubyGemMessage
{
    public required string Archive { get; init; }
    public required string[] Contents { get; init; }
}
// Locked bundle members: Bundler's lock resolution, not installed specifications.
internal sealed record RubySpecificationMessage(string Name, string Version, string Platform,
    RubyDependency[] RuntimeDependencies, RubyNativeSource Source);
internal sealed record RubyContextMessage(string Gemfile, string Lockfile,
    string BundlerVersion, string LockedBundlerVersion);
internal sealed record RubyVersionMessage(string Gemspec, string Raw, string Version,
    string Literal);
internal sealed record RubyBundleMessage(string Gemfile, string Lockfile, string BundlerVersion,
    string LockedBundlerVersion, string[] Gemfiles, string[] DirectDependencies,
    RubySpecificationMessage[] Specifications);

[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.SnakeCaseLower,
    RespectNullableAnnotations = true, RespectRequiredConstructorParameters = true,
    UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow)]
[JsonSerializable(typeof(RubySourceRequest))]
[JsonSerializable(typeof(RubySourceAnswer))]
[JsonSerializable(typeof(RubySourceMessage))]
[JsonSerializable(typeof(RubyArchiveMessage))]
[JsonSerializable(typeof(RubyContextMessage))]
[JsonSerializable(typeof(RubyVersionMessage))]
[JsonSerializable(typeof(RubyBundleMessage))]
internal partial class RubyFactJson : JsonSerializerContext;

internal sealed record RubySourceRequest(string Host, string Name, string Version,
    string Directory);
internal sealed record RubySourceAnswer(string State, string? Name = null, string? Version = null,
    string? Platform = null, string? Path = null, long? Size = null, string? Sha256 = null);
