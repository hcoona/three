using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Win32.SafeHandles;

namespace Hcoona.AzureAuth.CredProvider.Platform.FileSystem;

public sealed class SystemFileSystem : IFileSystem
{
    private const UnixFileMode OwnerOnlyFileMode = UnixFileMode.UserRead | UnixFileMode.UserWrite;
    private const UnixFileMode OwnerOnlyDirectoryMode =
        UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute;
    private const int LinuxOpenReadOnly = 0;
    private const int LinuxOpenNonBlocking = 0x800;
    private const int LinuxOpenCloseOnExec = 0x80000;
    private const int LinuxOpenNoFollow = 0x20000;
    private const int LinuxOpenDirectory = 0x10000;
    private const int LinuxAtCurrentWorkingDirectory = -100;
    private const int LinuxAtEmptyPath = 0x1000;
    private const int LinuxAtSymbolicLinkNoFollow = 0x100;
    private const uint LinuxStatxBasicStats = 0x7ff;
    private const uint LinuxStatxModeTypeMask = 0xF000;
    private const uint LinuxStatxModeDirectory = 0x4000;
    private const uint LinuxStatxModeRegularFile = 0x8000;
    private const uint LinuxStatxModeSymbolicLink = 0xA000;

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

    public bool IsSymbolicLink(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);

        var attributes = File.GetAttributes(path);
        return (attributes & FileAttributes.ReparsePoint) != 0
            && CreateFileSystemInfo(path).LinkTarget is not null;
    }

    public byte[] ComputeSha256Hash(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);

        using var stream = File.OpenRead(path);
        return SHA256.HashData(stream);
    }

    public FileIntegritySnapshot CaptureFileIntegritySnapshot(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        ThrowIfPathContainsCurrentOrParentDirectoryComponent(path);
        var fullPath = Path.GetFullPath(path);

        if (OperatingSystem.IsWindows() || OperatingSystem.IsMacOS())
        {
            return CaptureWeakFileIntegritySnapshot(fullPath);
        }

        ThrowIfUnsupportedIntegrityValidationPlatform();

        using var stream = OpenLinuxRegularFileWithoutFollowingSymlinks(path, out var status);
        var currentUserId = GetEffectiveUserId();
        ThrowIfUntrustedLinuxOwner(path, status.UserId, currentUserId, "helper file");
        var unixFileMode = GetUnixFileMode(status);
        ThrowIfUnsafeHelperUnixFileMode(path, unixFileMode, status.UserId == currentUserId);
        return new FileIntegritySnapshot(
            fullPath,
            new FileSystemEntryIdentity(FormatLinuxFileIdentity(status)),
            new FileSystemOwner(FormatUnixOwnerId(status.UserId)),
            unixFileMode,
            SHA256.HashData(stream),
            CaptureTrustedParentDirectoriesNoPlatformCheck(path, currentUserId)
        );
    }

    public bool FileMatchesIntegritySnapshot(string path, FileIntegritySnapshot snapshot)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        ArgumentNullException.ThrowIfNull(snapshot);

        try
        {
            var currentSnapshot = CaptureFileIntegritySnapshot(path);
            return string.Equals(
                    currentSnapshot.FullPath,
                    snapshot.FullPath,
                    StringComparison.Ordinal
                )
                && currentSnapshot.Identity == snapshot.Identity
                && currentSnapshot.Owner == snapshot.Owner
                && currentSnapshot.UnixFileMode == snapshot.UnixFileMode
                && TrustedParentDirectoriesMatchSnapshot(currentSnapshot, snapshot)
                && CryptographicOperations.FixedTimeEquals(
                    currentSnapshot.Sha256Hash,
                    snapshot.Sha256Hash
                );
        }
        catch (IOException)
        {
            return false;
        }
        catch (UnauthorizedAccessException)
        {
            return false;
        }
    }

    public IReadOnlyList<TrustedDirectorySnapshot> CaptureTrustedParentDirectorySnapshots(
        string path
    )
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        ThrowIfPathContainsCurrentOrParentDirectoryComponent(path);
        var fullPath = Path.GetFullPath(path);

        if (OperatingSystem.IsWindows() || OperatingSystem.IsMacOS())
        {
            return CaptureWeakTrustedParentDirectorySnapshots(fullPath);
        }

        ThrowIfUnsupportedIntegrityValidationPlatform();

        return CaptureTrustedParentDirectoriesNoPlatformCheck(path, GetEffectiveUserId());
    }

    private static void ThrowIfUnsupportedIntegrityValidationPlatform()
    {
        if (!OperatingSystem.IsLinux())
        {
            throw new PlatformNotSupportedException(
                "Helper integrity validation is supported only on Windows, macOS, and Linux."
            );
        }
    }

    private static void ThrowIfPathContainsCurrentOrParentDirectoryComponent(string path)
    {
        if (PathContainsCurrentOrParentDirectoryComponent(path))
        {
            throw new IOException(
                $"The helper integrity path '{path}' must not contain '.' or '..' path components "
                    + "or Windows path components with trailing spaces or periods."
            );
        }
    }

    private static bool PathContainsCurrentOrParentDirectoryComponent(string path)
    {
        var componentStart = 0;
        for (var index = 0; index <= path.Length; index++)
        {
            if (index < path.Length && !IsDirectorySeparator(path[index]))
            {
                continue;
            }
            var componentLength = index - componentStart;
            if (IsUnsafePathComponent(path, componentStart, componentLength))
            {
                return true;
            }

            componentStart = index + 1;
        }

        return false;
    }

    private static bool IsUnsafePathComponent(string path, int componentStart, int componentLength)
    {
        if (componentLength == 0)
        {
            return false;
        }

        if (IsCurrentOrParentDirectoryComponent(path, componentStart, componentLength))
        {
            return true;
        }

        if (!OperatingSystem.IsWindows())
        {
            return false;
        }

        char lastCharacter = path[componentStart + componentLength - 1];
        return lastCharacter is ' ' or '.';
    }

    private static bool IsCurrentOrParentDirectoryComponent(
        string path,
        int componentStart,
        int componentLength
    )
    {
        return componentLength == 1 && path[componentStart] == '.'
            || componentLength == 2
                && path[componentStart] == '.'
                && path[componentStart + 1] == '.';
    }

    private static bool IsDirectorySeparator(char value)
    {
        return value == Path.DirectorySeparatorChar || value == Path.AltDirectorySeparatorChar;
    }

    public FileSystemOwner GetCurrentOwner()
    {
        if (!OperatingSystem.IsLinux())
        {
            throw new PlatformNotSupportedException(
                "Owner identifiers are only supported on Linux."
            );
        }

        return new FileSystemOwner(FormatUnixOwnerId(GetEffectiveUserId()));
    }

    public FileSystemOwner GetOwner(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);

        if (!OperatingSystem.IsLinux())
        {
            throw new PlatformNotSupportedException(
                "Owner identifiers are only supported on Linux."
            );
        }

        return new FileSystemOwner(FormatUnixOwnerId(GetLinuxFileOwner(path)));
    }

    public string ReadAllText(string path, Encoding? encoding = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);

        return File.ReadAllText(path, encoding ?? Encoding.UTF8);
    }

    public void WriteAllText(string path, string contents, Encoding? encoding = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        ArgumentNullException.ThrowIfNull(contents);

        File.WriteAllText(path, contents, encoding ?? Encoding.UTF8);
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
        ThrowIfUnsupportedAtomicWritePlatform();

        var fullPath = Path.GetFullPath(path);
        var directory = Path.GetDirectoryName(fullPath);
        if (string.IsNullOrEmpty(directory))
        {
            directory = Directory.GetCurrentDirectory();
        }

        var missingDirectories = GetMissingDirectories(directory);
        var createdDirectoryMetadataFlushTargets = GetCreatedDirectoryMetadataFlushTargets(
            missingDirectories
        );
        CreateAtomicWriteDirectory(directory, missingDirectories, options);
        var temporaryPath = Path.Combine(
            directory,
            $".{Path.GetFileName(fullPath)}.{Guid.NewGuid():N}.tmp"
        );

        try
        {
            var targetExists = File.Exists(fullPath);
            var replacementMode = GetAtomicWriteUnixCreateMode(fullPath, targetExists, options);
            WriteTemporaryAllText(
                temporaryPath,
                contents,
                encoding ?? Encoding.UTF8,
                replacementMode
            );

            if (targetExists)
            {
                PreserveReplacePermissions(fullPath, temporaryPath, options);
                FlushFileMetadata(temporaryPath);
                File.Replace(temporaryPath, fullPath, destinationBackupFileName: null);
            }
            else
            {
                File.Move(temporaryPath, fullPath);
            }

            FlushContainingDirectoryMetadata(directory);
            FlushCreatedDirectoryMetadata(createdDirectoryMetadataFlushTargets);
        }
        finally
        {
            if (File.Exists(temporaryPath))
            {
                File.Delete(temporaryPath);
            }
        }
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

    private static List<string> GetMissingDirectories(string directory)
    {
        var missingDirectories = new List<string>();
        var current = Path.GetFullPath(directory);

        while (!Directory.Exists(current))
        {
            var parent = Path.GetDirectoryName(current);
            if (string.IsNullOrEmpty(parent))
            {
                break;
            }

            missingDirectories.Add(current);
            current = parent;
        }

        missingDirectories.Reverse();
        return missingDirectories;
    }

    private static List<string> GetCreatedDirectoryMetadataFlushTargets(
        List<string> missingDirectories
    )
    {
        return missingDirectories
            .Select(Path.GetDirectoryName)
            .Where(static parent => !string.IsNullOrEmpty(parent))
            .Select(static parent => parent!)
            .Distinct(StringComparer.Ordinal)
            .Reverse()
            .ToList();
    }

    private static void CreateAtomicWriteDirectory(
        string directory,
        List<string> missingDirectories,
        AtomicWriteOptions options
    )
    {
        if (
            !OperatingSystem.IsWindows()
            && (options & AtomicWriteOptions.RestrictUnixFileModeToOwnerOnly) != 0
        )
        {
            Directory.CreateDirectory(directory, OwnerOnlyDirectoryMode);
            foreach (var createdDirectory in missingDirectories)
            {
                File.SetUnixFileMode(createdDirectory, OwnerOnlyDirectoryMode);
            }

            return;
        }

        Directory.CreateDirectory(directory);
    }

    private static void FlushCreatedDirectoryMetadata(List<string> directories)
    {
        foreach (var directory in directories)
        {
            FlushContainingDirectoryMetadata(directory);
        }
    }

    private static void ThrowIfUnsupportedAtomicWritePlatform()
    {
        if (
            !OperatingSystem.IsWindows()
            && !OperatingSystem.IsLinux()
            && !OperatingSystem.IsMacOS()
        )
        {
            throw new PlatformNotSupportedException(
                "Durable atomic writes are supported only on Windows, macOS, and Linux."
            );
        }
    }

    private static FileSystemInfo CreateFileSystemInfo(string path)
    {
        return Directory.Exists(path) && !File.Exists(path)
            ? new DirectoryInfo(path)
            : new FileInfo(path);
    }

    private static UnixFileMode? GetAtomicWriteUnixCreateMode(
        string destinationPath,
        bool destinationExists,
        AtomicWriteOptions options
    )
    {
        if (OperatingSystem.IsWindows())
        {
            return null;
        }

        if ((options & AtomicWriteOptions.RestrictUnixFileModeToOwnerOnly) != 0)
        {
            return OwnerOnlyFileMode;
        }

        return destinationExists ? File.GetUnixFileMode(destinationPath) : OwnerOnlyFileMode;
    }

    private static void WriteTemporaryAllText(
        string temporaryPath,
        string contents,
        Encoding encoding,
        UnixFileMode? unixCreateMode
    )
    {
        var options = new FileStreamOptions
        {
            Access = FileAccess.Write,
            Mode = FileMode.CreateNew,
            Options = OperatingSystem.IsWindows() ? FileOptions.WriteThrough : FileOptions.None,
            Share = FileShare.None,
        };

        if (unixCreateMode is { } mode && !OperatingSystem.IsWindows())
        {
            options.UnixCreateMode = mode;
        }

        using var stream = new FileStream(temporaryPath, options);
        using var writer = new StreamWriter(stream, encoding, leaveOpen: true);
        writer.Write(contents);
        writer.Flush();
        stream.Flush(flushToDisk: true);
    }

    private static void FlushFileMetadata(string path)
    {
        if (OperatingSystem.IsWindows())
        {
            return;
        }

        if (!OperatingSystem.IsLinux() && !OperatingSystem.IsMacOS())
        {
            throw new PlatformNotSupportedException(
                "Durable atomic writes are not supported on this platform because "
                    + $"file metadata cannot be flushed for '{path}'."
            );
        }

        var openFlags = OperatingSystem.IsLinux()
            ? LinuxOpenReadOnly | LinuxOpenCloseOnExec
            : LinuxOpenReadOnly;
        FlushUnixFileMetadata(path, openFlags);
    }

    private static void FlushUnixFileMetadata(string path, int openFlags)
    {
        var descriptor = Open(path, openFlags);
        if (descriptor == -1)
        {
            throw new IOException(
                $"Failed to open file '{path}' for metadata flush.",
                Marshal.GetExceptionForHR(Marshal.GetHRForLastWin32Error())
            );
        }

        using var handle = new SafeFileHandle(new IntPtr(descriptor), ownsHandle: true);
        if (Fsync(handle) != 0)
        {
            throw new IOException(
                $"Failed to flush file metadata for '{path}'.",
                Marshal.GetExceptionForHR(Marshal.GetHRForLastWin32Error())
            );
        }
    }

    private static void FlushContainingDirectoryMetadata(string directory)
    {
        if (OperatingSystem.IsWindows())
        {
            // Windows FlushFileBuffers does not support directory handles and fails with
            // ERROR_INVALID_HANDLE. The file contents are flushed with write-through
            // file handles, but Windows cannot provide the POSIX directory-entry
            // durability boundary used below.
            return;
        }

        if (OperatingSystem.IsLinux())
        {
            FlushUnixDirectoryMetadata(
                directory,
                LinuxOpenReadOnly | LinuxOpenDirectory | LinuxOpenCloseOnExec
            );
            return;
        }

        if (OperatingSystem.IsMacOS())
        {
            FlushUnixDirectoryMetadata(directory, LinuxOpenReadOnly);
            return;
        }

        throw new PlatformNotSupportedException(
            "Durable atomic writes are not supported on this platform because "
                + $"directory metadata cannot be flushed for '{directory}'."
        );
    }

    private static void FlushUnixDirectoryMetadata(string directory, int openFlags)
    {
        var descriptor = Open(directory, openFlags);
        if (descriptor == -1)
        {
            throw new IOException(
                $"Failed to open directory '{directory}' for metadata flush.",
                Marshal.GetExceptionForHR(Marshal.GetHRForLastWin32Error())
            );
        }

        using var handle = new SafeFileHandle(new IntPtr(descriptor), ownsHandle: true);
        if (Fsync(handle) != 0)
        {
            throw new IOException(
                $"Failed to flush directory metadata for '{directory}'.",
                Marshal.GetExceptionForHR(Marshal.GetHRForLastWin32Error())
            );
        }
    }

    private static void PreserveReplacePermissions(
        string destinationPath,
        string replacementPath,
        AtomicWriteOptions options
    )
    {
        if (OperatingSystem.IsWindows())
        {
            return;
        }

        var replacementMode =
            (options & AtomicWriteOptions.RestrictUnixFileModeToOwnerOnly) != 0
                ? OwnerOnlyFileMode
                : File.GetUnixFileMode(destinationPath);
        File.SetUnixFileMode(replacementPath, replacementMode);
    }

    private static uint GetLinuxFileOwner(string path)
    {
        return StatLinuxPath(path).UserId;
    }

    private static FileStream OpenLinuxRegularFileWithoutFollowingSymlinks(
        string path,
        out LinuxStatx status
    )
    {
        var descriptor = Open(
            path,
            LinuxOpenReadOnly | LinuxOpenNonBlocking | LinuxOpenCloseOnExec | LinuxOpenNoFollow
        );
        if (descriptor == -1)
        {
            throw new IOException(
                $"Failed to open '{path}' without following symbolic links.",
                Marshal.GetExceptionForHR(Marshal.GetHRForLastWin32Error())
            );
        }

        var handle = new SafeFileHandle(new IntPtr(descriptor), ownsHandle: true);
        try
        {
            status = StatOpenLinuxFile(handle, path);
            if ((status.Mode & LinuxStatxModeTypeMask) != LinuxStatxModeRegularFile)
            {
                throw new IOException($"The path '{path}' is not a regular file.");
            }

            return new FileStream(handle, FileAccess.Read);
        }
        catch
        {
            handle.Dispose();
            throw;
        }
    }

    private static LinuxStatx StatLinuxPath(string path)
    {
        try
        {
            if (
                Statx(LinuxAtCurrentWorkingDirectory, path, 0, LinuxStatxBasicStats, out var status)
                != 0
            )
            {
                throw new IOException(
                    $"Failed to stat '{path}'.",
                    Marshal.GetExceptionForHR(Marshal.GetHRForLastWin32Error())
                );
            }

            return status;
        }
        catch (EntryPointNotFoundException exception)
        {
            throw new PlatformNotSupportedException(
                "Linux owner identifiers require Linux libc statx support.",
                exception
            );
        }
    }

    private static List<TrustedDirectorySnapshot> CaptureTrustedParentDirectoriesNoPlatformCheck(
        string path,
        uint currentUserId
    )
    {
        var fullPath = Path.GetFullPath(path);
        var parentPath = Path.GetDirectoryName(fullPath);
        if (string.IsNullOrEmpty(parentPath))
        {
            parentPath = Directory.GetCurrentDirectory();
        }

        var snapshots = new List<TrustedDirectorySnapshot>();
        while (true)
        {
            var status = StatLinuxPathWithoutFollowingSymlinks(parentPath);
            if ((status.Mode & LinuxStatxModeTypeMask) == LinuxStatxModeSymbolicLink)
            {
                throw new IOException(
                    $"The helper parent path '{parentPath}' must not be a symbolic link."
                );
            }

            if ((status.Mode & LinuxStatxModeTypeMask) != LinuxStatxModeDirectory)
            {
                throw new IOException($"The parent path '{parentPath}' is not a directory.");
            }

            ThrowIfUntrustedLinuxOwner(
                parentPath,
                status.UserId,
                currentUserId,
                "trusted parent directory"
            );
            var unixFileMode = GetUnixFileMode(status);
            ThrowIfUnsafeTrustedParentDirectoryUnixFileMode(parentPath, unixFileMode);
            snapshots.Add(
                new TrustedDirectorySnapshot(
                    parentPath,
                    new FileSystemEntryIdentity(FormatLinuxFileIdentity(status)),
                    new FileSystemOwner(FormatUnixOwnerId(status.UserId)),
                    unixFileMode
                )
            );

            if (string.Equals(parentPath, "/", StringComparison.Ordinal))
            {
                return snapshots;
            }

            parentPath = Path.GetDirectoryName(parentPath);
            if (string.IsNullOrEmpty(parentPath))
            {
                return snapshots;
            }
        }
    }

    private static bool TrustedParentDirectoriesMatchSnapshot(
        FileIntegritySnapshot currentSnapshot,
        FileIntegritySnapshot expectedSnapshot
    )
    {
        return currentSnapshot.TrustedParentDirectories.SequenceEqual(
            expectedSnapshot.TrustedParentDirectories
        );
    }

    private static FileIntegritySnapshot CaptureWeakFileIntegritySnapshot(string fullPath)
    {
        ThrowIfWeakSnapshotFileIsNotPlainFile(fullPath);
        using var stream = File.Open(fullPath, FileMode.Open, FileAccess.Read, FileShare.Read);
        return new FileIntegritySnapshot(
            fullPath,
            CreateWeakPathIdentity("file", fullPath),
            CreateWeakUnverifiedOwner(),
            0,
            SHA256.HashData(stream),
            CaptureWeakTrustedParentDirectorySnapshots(fullPath)
        );
    }

    private static List<TrustedDirectorySnapshot> CaptureWeakTrustedParentDirectorySnapshots(
        string fullPath
    )
    {
        var parentPath = Path.GetDirectoryName(fullPath);
        if (string.IsNullOrEmpty(parentPath))
        {
            parentPath = Directory.GetCurrentDirectory();
        }

        var snapshots = new List<TrustedDirectorySnapshot>();
        while (!string.IsNullOrEmpty(parentPath))
        {
            parentPath = Path.GetFullPath(parentPath);
            var directoryInfo = new DirectoryInfo(parentPath);
            if (!directoryInfo.Exists)
            {
                throw new DirectoryNotFoundException(parentPath);
            }

            if ((directoryInfo.Attributes & FileAttributes.ReparsePoint) != 0)
            {
                throw new IOException(
                    $"The helper parent path '{parentPath}' must not be a reparse point."
                );
            }

            snapshots.Add(
                new TrustedDirectorySnapshot(
                    parentPath,
                    CreateWeakPathIdentity("directory", parentPath),
                    CreateWeakUnverifiedOwner(),
                    0
                )
            );

            var nextParentPath = Directory.GetParent(parentPath)?.FullName;
            if (
                string.IsNullOrEmpty(nextParentPath)
                || string.Equals(nextParentPath, parentPath, StringComparison.Ordinal)
            )
            {
                return snapshots;
            }

            parentPath = nextParentPath;
        }

        return snapshots;
    }

    private static void ThrowIfWeakSnapshotFileIsNotPlainFile(string fullPath)
    {
        var fileInfo = new FileInfo(fullPath);
        if (!fileInfo.Exists)
        {
            throw new FileNotFoundException("The helper file does not exist.", fullPath);
        }

        if ((fileInfo.Attributes & FileAttributes.ReparsePoint) != 0)
        {
            throw new IOException($"The helper file '{fullPath}' must not be a reparse point.");
        }
    }

    private static FileSystemEntryIdentity CreateWeakPathIdentity(string entryKind, string fullPath)
    {
        return new FileSystemEntryIdentity($"weak-path:{entryKind}:{fullPath}");
    }

    private static FileSystemOwner CreateWeakUnverifiedOwner()
    {
        return new FileSystemOwner("weak:owner-unverified");
    }

    private static LinuxStatx StatLinuxPathWithoutFollowingSymlinks(string path)
    {
        try
        {
            if (
                Statx(
                    LinuxAtCurrentWorkingDirectory,
                    path,
                    LinuxAtSymbolicLinkNoFollow,
                    LinuxStatxBasicStats,
                    out var status
                ) != 0
            )
            {
                throw new IOException(
                    $"Failed to stat '{path}' without following symbolic links.",
                    Marshal.GetExceptionForHR(Marshal.GetHRForLastWin32Error())
                );
            }

            return status;
        }
        catch (EntryPointNotFoundException exception)
        {
            throw new PlatformNotSupportedException(
                "Linux owner identifiers require Linux libc statx support.",
                exception
            );
        }
    }

    private static LinuxStatx StatOpenLinuxFile(SafeFileHandle handle, string path)
    {
        try
        {
            if (
                Statx(handle, string.Empty, LinuxAtEmptyPath, LinuxStatxBasicStats, out var status)
                != 0
            )
            {
                throw new IOException(
                    $"Failed to stat open file '{path}'.",
                    Marshal.GetExceptionForHR(Marshal.GetHRForLastWin32Error())
                );
            }

            return status;
        }
        catch (EntryPointNotFoundException exception)
        {
            throw new PlatformNotSupportedException(
                "File integrity snapshots require Linux libc statx support.",
                exception
            );
        }
    }

    private static string FormatLinuxFileIdentity(LinuxStatx status)
    {
        return $"linux:{status.DeviceMajor}:{status.DeviceMinor}:{status.Inode}";
    }

    private static UnixFileMode GetUnixFileMode(LinuxStatx status)
    {
        return (UnixFileMode)(status.Mode & 0x0fff);
    }

    private static void ThrowIfUnsafeHelperUnixFileMode(
        string path,
        UnixFileMode mode,
        bool isCurrentUserOwned
    )
    {
        const UnixFileMode unsafeWriteBits = UnixFileMode.GroupWrite | UnixFileMode.OtherWrite;
        const UnixFileMode executableBits =
            UnixFileMode.UserExecute | UnixFileMode.GroupExecute | UnixFileMode.OtherExecute;
        if ((mode & unsafeWriteBits) != 0)
        {
            throw new UnauthorizedAccessException(
                $"The helper file '{path}' must not be writable by group or other users."
            );
        }

        if (isCurrentUserOwned && (mode & UnixFileMode.UserExecute) == 0)
        {
            throw new UnauthorizedAccessException(
                "The current-user-owned helper file "
                    + $"'{path}' must have the user executable bit set."
            );
        }

        if ((mode & executableBits) == 0)
        {
            throw new UnauthorizedAccessException(
                $"The helper file '{path}' must have an executable bit set."
            );
        }
    }

    private static void ThrowIfUntrustedLinuxOwner(
        string path,
        uint ownerUserId,
        uint currentUserId,
        string entryKind
    )
    {
        if (ownerUserId != currentUserId && ownerUserId != 0)
        {
            throw new UnauthorizedAccessException(
                $"The {entryKind} '{path}' must be owned by the current user or root."
            );
        }
    }

    private static void ThrowIfUnsafeTrustedParentDirectoryUnixFileMode(
        string path,
        UnixFileMode mode
    )
    {
        const UnixFileMode unsafeWriteBits = UnixFileMode.GroupWrite | UnixFileMode.OtherWrite;
        if ((mode & unsafeWriteBits) != 0)
        {
            throw new UnauthorizedAccessException(
                "The helper parent directory "
                    + $"'{path}' must not be writable by group or other users."
            );
        }
    }

    private static string FormatUnixOwnerId(uint userId)
    {
        return $"unix:{userId}";
    }

    [DllImport("libc", EntryPoint = "geteuid")]
    private static extern uint GetEffectiveUserId();

    [DllImport("libc", EntryPoint = "open", SetLastError = true)]
    private static extern int Open([MarshalAs(UnmanagedType.LPUTF8Str)] string path, int flags);

    [DllImport("libc", EntryPoint = "fsync", SetLastError = true)]
    private static extern int Fsync(SafeFileHandle handle);

    [DllImport("libc", EntryPoint = "statx", SetLastError = true)]
    private static extern int Statx(
        int directoryFileDescriptor,
        [MarshalAs(UnmanagedType.LPUTF8Str)] string path,
        int flags,
        uint mask,
        out LinuxStatx status
    );

    [DllImport("libc", EntryPoint = "statx", SetLastError = true)]
    private static extern int Statx(
        SafeFileHandle directoryFileDescriptor,
        [MarshalAs(UnmanagedType.LPUTF8Str)] string path,
        int flags,
        uint mask,
        out LinuxStatx status
    );

    [StructLayout(LayoutKind.Sequential, Size = 256)]
    private struct LinuxStatx
    {
        public uint Mask;
        public uint BlockSize;
        public ulong Attributes;
        public uint LinkCount;
        public uint UserId;
        public uint GroupId;
        public ushort Mode;
        public ushort Spare0;
        public ulong Inode;
        public ulong Size;
        public ulong Blocks;
        public ulong AttributesMask;
        public LinuxStatxTimestamp AccessTime;
        public LinuxStatxTimestamp CreationTime;
        public LinuxStatxTimestamp ChangeTime;
        public LinuxStatxTimestamp ModifiedTime;
        public uint DeviceIdMajor;
        public uint DeviceIdMinor;
        public uint DeviceMajor;
        public uint DeviceMinor;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct LinuxStatxTimestamp
    {
        public long Seconds;
        public uint Nanoseconds;
        public int Reserved;
    }
}
