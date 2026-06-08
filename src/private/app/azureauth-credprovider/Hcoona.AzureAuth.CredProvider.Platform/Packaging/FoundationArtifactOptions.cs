namespace Hcoona.AzureAuth.CredProvider.Platform.Packaging;

public sealed record FoundationArtifactOptions(
    string ArtifactName,
    string BuildOs,
    string TargetRid,
    string ProductVersion,
    string SourceRevision
);
