using System.Diagnostics;
using System.Text;

namespace Hcoona.AzureAuth.CredProvider.Platform.FileSystem;

public sealed class SystemFileSystem : IFileSystem, IFileSystemMutationLock
{
    private const UnixFileMode OwnerOnlyFileMode = UnixFileMode.UserRead | UnixFileMode.UserWrite;
    private static readonly Encoding Utf8NoBom = new UTF8Encoding(
        encoderShouldEmitUTF8Identifier: false,
        throwOnInvalidBytes: true
    );

    public bool FileExists(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        return File.Exists(path);
    }

    public bool DirectoryExists(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        return Directory.Exists(path);
    }

    public string GetFullPath(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        return Path.GetFullPath(path);
    }

    public bool IsPathFullyQualified(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        return Path.IsPathFullyQualified(path);
    }

    public string ReadAllText(string path, Encoding? encoding = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        return File.ReadAllText(path, encoding ?? Utf8NoBom);
    }

    public byte[] ReadAllBytes(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        return File.ReadAllBytes(path);
    }

    public long GetFileLength(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        return new FileInfo(path).Length;
    }

    public void WriteAllText(string path, string contents, Encoding? encoding = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        ArgumentNullException.ThrowIfNull(contents);
        File.WriteAllText(path, contents, encoding ?? Utf8NoBom);
    }

    public void AtomicWriteAllText(
        string path,
        string contents,
        Encoding? encoding = null,
        AtomicWriteOptions options = AtomicWriteOptions.None
    )
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        ArgumentNullException.ThrowIfNull(contents);

        AtomicWrite(
            path,
            temporaryPath => File.WriteAllText(temporaryPath, contents, encoding ?? Utf8NoBom),
            options
        );
    }

    public void AtomicWriteAllBytes(
        string path,
        byte[] contents,
        AtomicWriteOptions options = AtomicWriteOptions.None
    )
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        ArgumentNullException.ThrowIfNull(contents);

        AtomicWrite(path, temporaryPath => File.WriteAllBytes(temporaryPath, contents), options);
    }

    public UnixFileMode GetUnixFileMode(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        if (OperatingSystem.IsWindows())
        {
            throw new PlatformNotSupportedException(
                "Unix file modes are not supported on Windows."
            );
        }

        return File.GetUnixFileMode(path);
    }

    public void SetUnixFileMode(string path, UnixFileMode mode)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        if (OperatingSystem.IsWindows())
        {
            throw new PlatformNotSupportedException(
                "Unix file modes are not supported on Windows."
            );
        }

        File.SetUnixFileMode(path, mode);
    }

    public void CreateDirectory(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        Directory.CreateDirectory(path);
    }

    public void DeleteFile(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        File.Delete(path);
    }

    public void DeleteDirectory(string path, bool recursive = false)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        Directory.Delete(path, recursive);
    }

    public IEnumerable<string> EnumerateFiles(
        string path,
        string searchPattern = "*",
        SearchOption searchOption = SearchOption.TopDirectoryOnly
    )
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        ArgumentException.ThrowIfNullOrWhiteSpace(searchPattern);
        return Directory.EnumerateFiles(path, searchPattern, searchOption);
    }

    public IEnumerable<string> EnumerateDirectories(
        string path,
        string searchPattern = "*",
        SearchOption searchOption = SearchOption.TopDirectoryOnly
    )
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        ArgumentException.ThrowIfNullOrWhiteSpace(searchPattern);
        return Directory.EnumerateDirectories(path, searchPattern, searchOption);
    }

    IDisposable IFileSystemMutationLock.AcquireMutationLock(string directory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(directory);

        string fullDirectory = Path.GetFullPath(directory);
        Directory.CreateDirectory(fullDirectory);
        string lockPath = Path.Combine(fullDirectory, ".lock");
        var stopwatch = Stopwatch.StartNew();

        while (true)
        {
            try
            {
                return new FileStream(
                    lockPath,
                    FileMode.OpenOrCreate,
                    FileAccess.ReadWrite,
                    FileShare.None
                );
            }
            catch (IOException) when (stopwatch.Elapsed < TimeSpan.FromSeconds(2))
            {
                Thread.Sleep(25);
            }
        }
    }

    private static void AtomicWrite(
        string path,
        Action<string> writeTemporaryFile,
        AtomicWriteOptions options
    )
    {
        string fullPath = Path.GetFullPath(path);
        string directory =
            Path.GetDirectoryName(fullPath)
            ?? throw new IOException($"The file path '{fullPath}' has no parent directory.");
        Directory.CreateDirectory(directory);

        string temporaryPath = Path.Combine(
            directory,
            $".{Path.GetFileName(fullPath)}.{Guid.NewGuid():N}.tmp"
        );
        UnixFileMode? existingMode =
            !OperatingSystem.IsWindows() && File.Exists(fullPath)
                ? File.GetUnixFileMode(fullPath)
                : null;
        try
        {
            writeTemporaryFile(temporaryPath);
            if (!OperatingSystem.IsWindows())
            {
                UnixFileMode? replacementMode =
                    (options & AtomicWriteOptions.RestrictUnixFileModeToOwnerOnly) != 0
                        ? OwnerOnlyFileMode
                        : existingMode;
                if (replacementMode is { } mode)
                {
                    File.SetUnixFileMode(temporaryPath, mode);
                }
            }

            File.Move(temporaryPath, fullPath, overwrite: true);
        }
        finally
        {
            try
            {
                File.Delete(temporaryPath);
            }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }
    }
}
