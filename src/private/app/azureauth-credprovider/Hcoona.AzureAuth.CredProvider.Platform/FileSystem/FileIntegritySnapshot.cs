namespace Hcoona.AzureAuth.CredProvider.Platform.FileSystem;

public sealed class FileIntegritySnapshot
{
    private readonly byte[] _sha256Hash;
    private readonly TrustedDirectorySnapshot[] _trustedParentDirectories;

    public FileIntegritySnapshot(
        string fullPath,
        FileSystemEntryIdentity identity,
        FileSystemOwner owner,
        UnixFileMode unixFileMode,
        byte[] sha256Hash,
        IEnumerable<TrustedDirectorySnapshot> trustedParentDirectories
    )
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(fullPath);
        ArgumentNullException.ThrowIfNull(identity);
        ArgumentNullException.ThrowIfNull(owner);
        ArgumentNullException.ThrowIfNull(sha256Hash);
        ArgumentNullException.ThrowIfNull(trustedParentDirectories);

        FullPath = fullPath;
        Identity = identity;
        Owner = owner;
        UnixFileMode = unixFileMode;
        _sha256Hash = (byte[])sha256Hash.Clone();
        _trustedParentDirectories = trustedParentDirectories.ToArray();
    }

    public string FullPath { get; }

    public FileSystemEntryIdentity Identity { get; }

    public FileSystemOwner Owner { get; }

    public UnixFileMode UnixFileMode { get; }

    public byte[] Sha256Hash => (byte[])_sha256Hash.Clone();

    public IReadOnlyList<TrustedDirectorySnapshot> TrustedParentDirectories =>
        (TrustedDirectorySnapshot[])_trustedParentDirectories.Clone();
}
