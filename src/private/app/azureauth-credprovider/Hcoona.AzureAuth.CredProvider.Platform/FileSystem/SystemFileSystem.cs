using System.Diagnostics;
using System.Text;

namespace Hcoona.AzureAuth.CredProvider.Platform.FileSystem;

public sealed class SystemFileSystem
    : IFileSystem,
        IFileSystemMutationLock,
        IFileSystemLinkResolver,
        IFileSystemGitConfigLock
{
    private const UnixFileMode OwnerOnlyFileMode = UnixFileMode.UserRead | UnixFileMode.UserWrite;
    private static readonly Encoding Utf8NoBom = new UTF8Encoding(
        encoderShouldEmitUTF8Identifier: false,
        throwOnInvalidBytes: true
    );
    private readonly Action<string>? beforeTemporaryFileWrite;

    public SystemFileSystem() { }

    internal SystemFileSystem(Action<string> beforeTemporaryFileWrite)
    {
        ArgumentNullException.ThrowIfNull(beforeTemporaryFileWrite);
        this.beforeTemporaryFileWrite = beforeTemporaryFileWrite;
    }

    public bool FileExists(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        return File.Exists(path);
    }

    public bool IsExecutableFile(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        if (!File.Exists(path))
        {
            return false;
        }

        if (OperatingSystem.IsWindows())
        {
            return true;
        }

        const UnixFileMode executeModes =
            UnixFileMode.UserExecute
            | UnixFileMode.GroupExecute
            | UnixFileMode.OtherExecute;
        return (File.GetUnixFileMode(path) & executeModes) != 0;
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

    string IFileSystemLinkResolver.ResolveFilePathForWrite(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);

        string fullPath = Path.GetFullPath(path);
        var file = new FileInfo(fullPath);
        string? linkTarget = file.LinkTarget;
        if (linkTarget is null)
        {
            if (!file.Exists)
            {
                if (Directory.Exists(fullPath))
                {
                    throw new IOException($"The file path '{fullPath}' is a directory.");
                }

                return fullPath;
            }

            if ((file.Attributes & FileAttributes.ReparsePoint) == 0)
            {
                return fullPath;
            }
        }

        FileSystemInfo? resolvedTarget = file.ResolveLinkTarget(returnFinalTarget: true);
        if (resolvedTarget is not FileInfo resolvedFile || !resolvedFile.Exists)
        {
            throw new IOException(
                $"The file link '{fullPath}' does not resolve to an existing file."
            );
        }

        return resolvedFile.FullName;
    }

    IGitConfigLockFile IFileSystemGitConfigLock.AcquireGitConfigLock(string targetPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(targetPath);
        return new GitConfigLockFile(targetPath);
    }

    private void AtomicWrite(
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
            if (
                !OperatingSystem.IsWindows()
                && (options & AtomicWriteOptions.RestrictUnixFileModeToOwnerOnly) != 0
            )
            {
                using var temporaryFile = new FileStream(
                    temporaryPath,
                    new FileStreamOptions
                    {
                        Mode = FileMode.CreateNew,
                        Access = FileAccess.Write,
                        Share = FileShare.None,
                        UnixCreateMode = OwnerOnlyFileMode,
                    }
                );
                File.SetUnixFileMode(temporaryFile.SafeFileHandle, OwnerOnlyFileMode);
            }

            beforeTemporaryFileWrite?.Invoke(temporaryPath);
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

    private sealed class GitConfigLockFile : IGitConfigLockFile
    {
        private readonly string lockPath;
        private readonly string targetPath;
        private FileStream? stream;
        private bool committed;

        public GitConfigLockFile(string targetPath)
        {
            this.targetPath = Path.GetFullPath(targetPath);
            string directory =
                Path.GetDirectoryName(this.targetPath)
                ?? throw new IOException(
                    $"The file path '{this.targetPath}' has no parent directory."
                );
            Directory.CreateDirectory(directory);
            lockPath = this.targetPath + ".lock";
            try
            {
                stream = new FileStream(
                    lockPath,
                    FileMode.CreateNew,
                    FileAccess.Write,
                    FileShare.None
                );

                if (!OperatingSystem.IsWindows() && File.Exists(this.targetPath))
                {
                    File.SetUnixFileMode(lockPath, File.GetUnixFileMode(this.targetPath));
                }
            }
            catch
            {
                if (stream is not null)
                {
                    stream.Dispose();
                    stream = null;
                    File.Delete(lockPath);
                }
                throw;
            }
        }

        public void WriteAllBytes(byte[] contents)
        {
            ArgumentNullException.ThrowIfNull(contents);
            FileStream currentStream =
                stream ?? throw new ObjectDisposedException(nameof(GitConfigLockFile));
            currentStream.SetLength(0);
            currentStream.Write(contents);
            currentStream.Flush(flushToDisk: true);
        }

        public void Commit()
        {
            FileStream currentStream =
                stream ?? throw new ObjectDisposedException(nameof(GitConfigLockFile));
            currentStream.Flush(flushToDisk: true);
            currentStream.Dispose();
            stream = null;
            File.Move(lockPath, targetPath, overwrite: true);
            committed = true;
        }

        public void Dispose()
        {
            stream?.Dispose();
            stream = null;
            if (committed)
            {
                return;
            }

            try
            {
                File.Delete(lockPath);
            }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }
    }
}
