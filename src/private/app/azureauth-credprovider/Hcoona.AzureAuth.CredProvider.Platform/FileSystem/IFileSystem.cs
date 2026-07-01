using System.Text;

namespace Hcoona.AzureAuth.CredProvider.Platform.FileSystem;

public interface IFileSystem
{
    bool SupportsConditionalFileMutations { get; }

    bool FileExists(string path);

    bool DirectoryExists(string path);

    string GetFullPath(string path);

    bool IsPathFullyQualified(string path);

    bool IsSymbolicLink(string path);

    byte[] ComputeSha256Hash(string path);

    FileIntegritySnapshot CaptureFileIntegritySnapshot(string path);

    bool FileMatchesIntegritySnapshot(string path, FileIntegritySnapshot snapshot);

    IReadOnlyList<TrustedDirectorySnapshot> CaptureTrustedParentDirectorySnapshots(string path);

    FileSystemOwner GetCurrentOwner();

    FileSystemOwner GetOwner(string path);

    string ReadAllText(string path, Encoding? encoding = null);

    byte[] ReadAllBytes(string path);

    void WriteAllText(string path, string contents, Encoding? encoding = null);

    void AtomicWriteAllText(
        string path,
        string contents,
        Encoding? encoding = null,
        AtomicWriteOptions options = AtomicWriteOptions.None,
        FileMutationExpectation? expectation = null
    );

    void AtomicWriteAllBytes(
        string path,
        byte[] contents,
        AtomicWriteOptions options = AtomicWriteOptions.None,
        FileMutationExpectation? expectation = null
    );

    UnixFileMode GetUnixFileMode(string path);

    void SetUnixFileMode(string path, UnixFileMode mode);

    void CreateDirectory(string path);

    void DeleteFile(string path, FileMutationExpectation? expectation = null);

    void DeleteDirectory(string path, bool recursive = false);

    IEnumerable<string> EnumerateFiles(
        string path,
        string searchPattern = "*",
        SearchOption searchOption = SearchOption.TopDirectoryOnly
    );

    IEnumerable<string> EnumerateDirectories(
        string path,
        string searchPattern = "*",
        SearchOption searchOption = SearchOption.TopDirectoryOnly
    );
}

internal interface IFileSystemMutationLock
{
    IDisposable AcquireMutationLock(string directory, bool createDirectory = true);
}

internal interface IFileSystemReparsePointSafety
{
    bool IsReparsePoint(string path);
}

internal interface IFileSystemNoFollowEnumeration
{
    IEnumerable<string> EnumerateFileSystemEntriesNoFollow(
        string path,
        string searchPattern = "*",
        SearchOption searchOption = SearchOption.TopDirectoryOnly
    );
}

internal interface IFileSystemFileLength
{
    long GetFileLength(string path);
}

internal interface IFakeAdapterScaffoldMaterializationFileSystem
{
    FileIntegritySnapshot AtomicWriteAllTextAndCaptureSnapshotNoFollow(
        string path,
        string contents,
        Encoding? encoding = null,
        AtomicWriteOptions options = AtomicWriteOptions.None,
        FileMutationExpectation? expectation = null
    );

    FileIntegritySnapshot CaptureFileIntegritySnapshotWithoutTrustedParents(string path);

    void CreateDirectoryNoFollow(string path);

    void DeleteFileIfMatchesSnapshotNoFollow(string path, FileIntegritySnapshot snapshot);

    void SetUnixFileModeNoFollow(string path, UnixFileMode mode);
}

public sealed record FileMutationExpectation(bool Exists, string? Sha256Hash)
{
    public static FileMutationExpectation Existing(string sha256Hash) => new(true, sha256Hash);

    public static FileMutationExpectation Missing { get; } = new(false, null);
}

internal enum FileMutationCheckpoint
{
    BeforeMutationLock,
    BeforeAtomicWriteMutation,
    BeforeDeleteMutation,
}

internal sealed class FileMutationException : IOException
{
    public FileMutationException(
        string message,
        bool mutationMayHaveReachedDurableState,
        Exception innerException
    )
        : base(message, innerException)
    {
        MutationMayHaveReachedDurableState = mutationMayHaveReachedDurableState;
    }

    public bool MutationMayHaveReachedDurableState { get; }
}
