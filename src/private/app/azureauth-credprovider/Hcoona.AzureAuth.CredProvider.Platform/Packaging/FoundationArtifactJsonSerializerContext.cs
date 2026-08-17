using System.Text.Json;
using System.Text.Json.Serialization;

namespace Hcoona.AzureAuth.CredProvider.Platform.Packaging;

[JsonSerializable(typeof(FoundationArtifactManifest))]
[JsonSerializable(typeof(FoundationArtifactFile))]
[JsonSerializable(typeof(IReadOnlyList<FoundationArtifactFile>))]
[JsonSerializable(typeof(FoundationArtifactFile[]))]
[JsonSourceGenerationOptions(
    JsonSerializerDefaults.Web,
    WriteIndented = false,
    GenerationMode = JsonSourceGenerationMode.Metadata
)]
internal sealed partial class FoundationArtifactJsonSerializerContext : JsonSerializerContext;
