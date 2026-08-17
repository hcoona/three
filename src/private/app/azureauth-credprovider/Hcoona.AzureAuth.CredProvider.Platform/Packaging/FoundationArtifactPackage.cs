namespace Hcoona.AzureAuth.CredProvider.Platform.Packaging;

public sealed record FoundationArtifactPackage(
    FoundationArtifactManifest Manifest,
    byte[] ManifestBytes
);
