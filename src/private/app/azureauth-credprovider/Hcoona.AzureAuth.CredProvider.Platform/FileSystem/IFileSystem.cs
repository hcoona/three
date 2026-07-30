using System.Text;

namespace Hcoona.AzureAuth.CredProvider.Platform.FileSystem;

public interface IFileSystem
{
    bool FileExists(string path);

    bool DirectoryExists(string path);

    string GetFullPath(string path);

    bool IsPathFullyQualified(string path);

    string ReadAllText(string path, Encoding? encoding = null);

    byte[] ReadAllBytes(string path);

    long GetFileLength(string path);

    void WriteAllText(string path, string contents, Encoding? encoding = null);

    void AtomicWriteAllText(
        string path,
        string contents,
        Encoding? encoding = null,
        AtomicWriteOptions options = AtomicWriteOptions.None
    );

    void AtomicWriteAllBytes(
        string path,
        byte[] contents,
        AtomicWriteOptions options = AtomicWriteOptions.None
    );

    UnixFileMode GetUnixFileMode(string path);

    void SetUnixFileMode(string path, UnixFileMode mode);

    void CreateDirectory(string path);

    void DeleteFile(string path);

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
    IDisposable AcquireMutationLock(string directory);
}
