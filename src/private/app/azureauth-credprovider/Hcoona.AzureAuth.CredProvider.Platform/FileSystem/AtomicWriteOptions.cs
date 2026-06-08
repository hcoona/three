namespace Hcoona.AzureAuth.CredProvider.Platform.FileSystem;

[Flags]
public enum AtomicWriteOptions
{
    None = 0,
    RestrictUnixFileModeToOwnerOnly = 1,
}
