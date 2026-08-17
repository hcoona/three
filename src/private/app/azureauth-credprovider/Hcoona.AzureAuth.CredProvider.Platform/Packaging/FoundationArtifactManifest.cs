namespace Hcoona.AzureAuth.CredProvider.Platform.Packaging;

public sealed record FoundationArtifactManifest(
    string SchemaVersion,
    string ArtifactName,
    string BuildOs,
    string TargetRid,
    string ProductVersion,
    string SourceRevision,
    string ProducedBy,
    string ReleaseStatus,
    string SignatureStatus,
    bool IsInternal,
    bool IsRelease,
    bool IsSigned,
    IReadOnlyList<FoundationArtifactFile> Files
)
{
    public const string CurrentSchemaVersion = "azureauth-credprovider-foundation-artifact-v1";
}
