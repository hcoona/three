namespace Hcoona.AzureAuth.CredProvider.Platform.FileSystem;

public sealed record TrustedDirectorySnapshot(
    string FullPath,
    FileSystemEntryIdentity Identity,
    FileSystemOwner Owner,
    UnixFileMode UnixFileMode
);
