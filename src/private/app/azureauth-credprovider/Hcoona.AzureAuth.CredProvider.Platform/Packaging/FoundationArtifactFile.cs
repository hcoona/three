namespace Hcoona.AzureAuth.CredProvider.Platform.Packaging;

public sealed record FoundationArtifactFile(
    string Path,
    long Length,
    string Sha256
);
