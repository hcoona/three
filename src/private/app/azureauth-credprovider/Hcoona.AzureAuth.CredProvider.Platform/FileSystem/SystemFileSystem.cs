using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Win32.SafeHandles;

namespace Hcoona.AzureAuth.CredProvider.Platform.FileSystem;

public sealed class SystemFileSystem
    : IFileSystem,
        IFileSystemMutationLock,
        IFileSystemReparsePointSafety,
        IFileSystemNoFollowEnumeration,
        IFileSystemFileLength
{
    private static readonly Encoding Utf8NoBom = new UTF8Encoding(
        encoderShouldEmitUTF8Identifier: false,
        throwOnInvalidBytes: true
    );
    private const UnixFileMode OwnerOnlyFileMode = UnixFileMode.UserRead | UnixFileMode.UserWrite;
    private const UnixFileMode OwnerOnlyDirectoryMode =
        UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute;
    private const int LinuxOpenReadOnly = 0;
    private const int LinuxOpenReadWrite = 2;
    private const int LinuxOpenCreate = 0x40;
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
    private const uint LinuxOwnerOnlyCreateMode = 0x180;
    private const int MacOsOpenReadOnly = 0;
    private const int MacOsOpenReadWrite = 2;
    private const int MacOsOpenCreate = 0x200;
    private const int MacOsOpenCloseOnExec = 0x1000000;
    private const int MacOsOpenNoFollow = 0x100;
    private const int MacOsOpenDirectory = 0x100000;
    private const uint WindowsGenericRead = 0x80000000;
    private const uint WindowsGenericWrite = 0x40000000;
    private const uint WindowsDeleteAccess = 0x00010000;
    private const uint WindowsFileShareRead = 0x00000001;
    private const uint WindowsFileShareWrite = 0x00000002;
    private const uint WindowsFileShareDelete = 0x00000004;
    private const uint WindowsOpenExisting = 3;
    private const uint WindowsOpenAlways = 4;
    private const uint WindowsFileAttributeNormal = 0x00000080;
    private const uint WindowsFileFlagOpenReparsePoint = 0x00200000;
    private readonly Action<FileMutationCheckpoint, string>? mutationCheckpoint;

    public SystemFileSystem() { }

    internal SystemFileSystem(Action<FileMutationCheckpoint, string> mutationCheckpoint)
    {
        this.mutationCheckpoint =
            mutationCheckpoint ?? throw new ArgumentNullException(nameof(mutationCheckpoint));
    }

    public bool SupportsConditionalFileMutations => IsConditionalFileMutationSupported();

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

    bool IFileSystemReparsePointSafety.IsReparsePoint(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);

        return (File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0;
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

    public byte[] ReadAllBytes(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);

        return File.ReadAllBytes(path);
    }

    public long GetFileLength(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);

        return GetFileLengthWithoutFollowingReparsePoints(path);
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
        AtomicWriteOptions options = AtomicWriteOptions.None,
        FileMutationExpectation? expectation = null
    )
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        ArgumentNullException.ThrowIfNull(contents);
        ThrowIfUnsupportedAtomicWritePlatform();
        ThrowIfConditionalMutationUnsupported(expectation);

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
        ValidateMutationExpectation(fullPath, expectation);
        CreateAtomicWriteDirectory(directory, missingDirectories, options);
        var temporaryPath = Path.Combine(
            directory,
            $".{Path.GetFileName(fullPath)}.{Guid.NewGuid():N}.tmp"
        );

        ValidateParentDirectoryChainHasNoSymbolicLinks(fullPath);
        mutationCheckpoint?.Invoke(FileMutationCheckpoint.BeforeMutationLock, fullPath);
        using FileStream mutationLock = AcquireMutationLockCore(directory, createDirectory: false);
        ValidateParentDirectoryChainHasNoSymbolicLinks(fullPath);
        using SafeFileHandle? temporaryDirectoryHandle =
            OpenTemporaryDirectoryCleanupHandleIfSupported(directory);
        SafeFileHandle? temporaryFileCleanupHandle = null;

        var mutationReachedDurableState = false;
        try
        {
            ValidateMutationExpectation(fullPath, expectation);
            WriteTemporaryAllText(
                temporaryPath,
                contents,
                encoding ?? Utf8NoBom,
                GetAtomicWriteUnixCreateMode(options)
            );
            temporaryFileCleanupHandle = OpenTemporaryFileCleanupHandleIfSupported(temporaryPath);

            mutationCheckpoint?.Invoke(FileMutationCheckpoint.BeforeAtomicWriteMutation, fullPath);
            ValidateParentDirectoryChainHasNoSymbolicLinks(fullPath);
            ValidateMutationExpectation(fullPath, expectation);
            var targetStateAtMutation = GetAtomicWriteTargetState(fullPath);

            if (targetStateAtMutation == AtomicWriteTargetState.File)
            {
                PreserveReplacePermissions(fullPath, temporaryPath, options);
                FlushFileMetadata(temporaryPath);
                temporaryFileCleanupHandle?.Dispose();
                temporaryFileCleanupHandle = null;
                File.Replace(temporaryPath, fullPath, destinationBackupFileName: null);
            }
            else if (targetStateAtMutation == AtomicWriteTargetState.Missing)
            {
                temporaryFileCleanupHandle?.Dispose();
                temporaryFileCleanupHandle = null;
                File.Move(temporaryPath, fullPath);
            }
            else
            {
                throw new IOException(
                    $"The atomic write destination '{fullPath}' must be a plain file or missing."
                );
            }

            mutationReachedDurableState = true;
            FlushContainingDirectoryMetadata(directory);
            FlushCreatedDirectoryMetadata(createdDirectoryMetadataFlushTargets);
        }
        catch (Exception exception)
            when (mutationReachedDurableState && exception is not OperationCanceledException)
        {
            throw new FileMutationException(
                "The file mutation reached durable state, "
                    + "but post-mutation durability work failed.",
                mutationMayHaveReachedDurableState: true,
                exception
            );
        }
        finally
        {
            DeleteTemporaryFileIfExists(
                temporaryPath,
                temporaryDirectoryHandle,
                temporaryFileCleanupHandle
            );
        }
    }

    public void AtomicWriteAllBytes(
        string path,
        byte[] contents,
        AtomicWriteOptions options = AtomicWriteOptions.None,
        FileMutationExpectation? expectation = null
    )
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        ArgumentNullException.ThrowIfNull(contents);
        ThrowIfUnsupportedAtomicWritePlatform();
        ThrowIfConditionalMutationUnsupported(expectation);

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
        ValidateMutationExpectation(fullPath, expectation);
        CreateAtomicWriteDirectory(directory, missingDirectories, options);
        var temporaryPath = Path.Combine(
            directory,
            $".{Path.GetFileName(fullPath)}.{Guid.NewGuid():N}.tmp"
        );

        ValidateParentDirectoryChainHasNoSymbolicLinks(fullPath);
        mutationCheckpoint?.Invoke(FileMutationCheckpoint.BeforeMutationLock, fullPath);
        using FileStream mutationLock = AcquireMutationLockCore(directory, createDirectory: false);
        ValidateParentDirectoryChainHasNoSymbolicLinks(fullPath);
        using SafeFileHandle? temporaryDirectoryHandle =
            OpenTemporaryDirectoryCleanupHandleIfSupported(directory);
        SafeFileHandle? temporaryFileCleanupHandle = null;

        var mutationReachedDurableState = false;
        try
        {
            ValidateMutationExpectation(fullPath, expectation);
            WriteTemporaryAllBytes(
                temporaryPath,
                contents,
                GetAtomicWriteUnixCreateMode(options)
            );
            temporaryFileCleanupHandle = OpenTemporaryFileCleanupHandleIfSupported(temporaryPath);

            mutationCheckpoint?.Invoke(FileMutationCheckpoint.BeforeAtomicWriteMutation, fullPath);
            ValidateParentDirectoryChainHasNoSymbolicLinks(fullPath);
            ValidateMutationExpectation(fullPath, expectation);
            var targetStateAtMutation = GetAtomicWriteTargetState(fullPath);

            if (targetStateAtMutation == AtomicWriteTargetState.File)
            {
                PreserveReplacePermissions(fullPath, temporaryPath, options);
                FlushFileMetadata(temporaryPath);
                temporaryFileCleanupHandle?.Dispose();
                temporaryFileCleanupHandle = null;
                File.Replace(temporaryPath, fullPath, destinationBackupFileName: null);
            }
            else if (targetStateAtMutation == AtomicWriteTargetState.Missing)
            {
                temporaryFileCleanupHandle?.Dispose();
                temporaryFileCleanupHandle = null;
                File.Move(temporaryPath, fullPath);
            }
            else
            {
                throw new IOException(
                    $"The atomic write destination '{fullPath}' must be a plain file or missing."
                );
            }

            mutationReachedDurableState = true;
            FlushContainingDirectoryMetadata(directory);
            FlushCreatedDirectoryMetadata(createdDirectoryMetadataFlushTargets);
        }
        catch (Exception exception)
            when (mutationReachedDurableState && exception is not OperationCanceledException)
        {
            throw new FileMutationException(
                "The file mutation reached durable state, "
                    + "but post-mutation durability work failed.",
                mutationMayHaveReachedDurableState: true,
                exception
            );
        }
        finally
        {
            DeleteTemporaryFileIfExists(
                temporaryPath,
                temporaryDirectoryHandle,
                temporaryFileCleanupHandle
            );
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

    public void DeleteFile(string path, FileMutationExpectation? expectation = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        ThrowIfUnsupportedAtomicWritePlatform();
        ThrowIfConditionalMutationUnsupported(expectation);

        var fullPath = Path.GetFullPath(path);
        var directory = Path.GetDirectoryName(fullPath);
        if (string.IsNullOrEmpty(directory))
        {
            directory = Directory.GetCurrentDirectory();
        }

        ValidateExistingParentDirectoryChainHasNoSymbolicLinks(directory);
        if (!Directory.Exists(directory))
        {
            ValidateMutationExpectation(fullPath, expectation);
            return;
        }

        mutationCheckpoint?.Invoke(FileMutationCheckpoint.BeforeMutationLock, fullPath);
        using FileStream mutationLock = AcquireMutationLockCore(directory, createDirectory: false);
        ValidateParentDirectoryChainHasNoSymbolicLinks(fullPath);
        mutationCheckpoint?.Invoke(FileMutationCheckpoint.BeforeDeleteMutation, fullPath);
        ValidateParentDirectoryChainHasNoSymbolicLinks(fullPath);
        ValidateMutationExpectation(fullPath, expectation);
        var targetStateAtMutation = GetAtomicWriteTargetState(fullPath);
        if (targetStateAtMutation == AtomicWriteTargetState.ReparsePoint)
        {
            throw new IOException(
                $"The delete target '{fullPath}' must not be a symbolic link or reparse point."
            );
        }

        var mutationReachedDurableState = false;
        try
        {
            File.Delete(fullPath);
            mutationReachedDurableState = true;
            FlushContainingDirectoryMetadata(directory);
        }
        catch (Exception exception)
            when (mutationReachedDurableState && exception is not OperationCanceledException)
        {
            throw new FileMutationException(
                "The file delete reached durable state, but post-mutation durability work failed.",
                mutationMayHaveReachedDurableState: true,
                exception
            );
        }
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

    IEnumerable<string> IFileSystemNoFollowEnumeration.EnumerateFileSystemEntriesNoFollow(
        string path,
        string searchPattern,
        SearchOption searchOption
    )
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        ArgumentException.ThrowIfNullOrWhiteSpace(searchPattern);

        return EnumerateFileSystemEntriesNoFollow(path, searchPattern, searchOption).ToArray();
    }

    private static IEnumerable<string> EnumerateFileSystemEntriesNoFollow(
        string path,
        string searchPattern,
        SearchOption searchOption
    )
    {
        var directoriesToVisit = new Stack<string>();
        directoriesToVisit.Push(path);
        var enumerationOptions = new EnumerationOptions
        {
            AttributesToSkip = 0,
            RecurseSubdirectories = false,
        };

        while (directoriesToVisit.Count > 0)
        {
            string currentDirectory = directoriesToVisit.Pop();
            foreach (
                string entry in Directory.EnumerateFileSystemEntries(
                    currentDirectory,
                    searchPattern,
                    enumerationOptions
                )
            )
            {
                yield return entry;
            }

            if (searchOption != SearchOption.AllDirectories)
            {
                continue;
            }

            foreach (
                string directory in Directory.EnumerateDirectories(
                    currentDirectory,
                    "*",
                    enumerationOptions
                )
            )
            {
                if ((File.GetAttributes(directory) & FileAttributes.ReparsePoint) == 0)
                {
                    directoriesToVisit.Push(directory);
                }
            }
        }
    }

    private static List<string> GetMissingDirectories(string directory)
    {
        var missingDirectories = new List<string>();
        foreach (var current in EnumerateDirectoryChain(Path.GetFullPath(directory)))
        {
            if (!TryGetFileAttributes(current, out var attributes))
            {
                missingDirectories.Add(current);
                continue;
            }

            ThrowIfDirectoryAttributesAreUnsafe(current, attributes);
            if (missingDirectories.Count > 0)
            {
                throw new IOException(
                    $"The target parent directory '{current}' could not be proven safe."
                );
            }
        }

        return missingDirectories;
    }

    IDisposable IFileSystemMutationLock.AcquireMutationLock(
        string directory,
        bool createDirectory
    ) => AcquireMutationLockCore(directory, createDirectory);

    private static FileStream AcquireMutationLockCore(string directory, bool createDirectory = true)
    {
        if (createDirectory)
        {
            CreateAtomicWriteDirectory(
                directory,
                GetMissingDirectories(directory),
                AtomicWriteOptions.RestrictUnixFileModeToOwnerOnly
            );
        }

        if (OperatingSystem.IsLinux())
        {
            return AcquireLinuxMutationLockWithoutFollowingTargetDirectory(directory);
        }

        if (OperatingSystem.IsWindows())
        {
            return AcquireWindowsMutationLockWithoutFollowingLockFile(directory);
        }

        if (OperatingSystem.IsMacOS())
        {
            return AcquireMacOsMutationLockWithoutFollowingLockFile(directory);
        }

        var lockFilePath = Path.Combine(directory, ".azureauth-credprovider.fs.lock");
        var stream = new FileStream(
            lockFilePath,
            FileMode.OpenOrCreate,
            FileAccess.ReadWrite,
            FileShare.ReadWrite
        );
        if (!OperatingSystem.IsMacOS())
        {
            stream.Lock(0, long.MaxValue);
        }

        return stream;
    }

    [SupportedOSPlatform("macos")]
    private static FileStream AcquireMacOsMutationLockWithoutFollowingLockFile(string directory)
    {
        using SafeFileHandle directoryHandle = OpenUnixDirectoryChainWithoutFollowingSymlinks(
            directory,
            MacOsOpenReadOnly
                | MacOsOpenDirectory
                | MacOsOpenCloseOnExec
                | MacOsOpenNoFollow
        );
        int directoryDescriptor = directoryHandle.DangerousGetHandle().ToInt32();
        var lockDescriptor = OpenAt(
            directoryDescriptor,
            ".azureauth-credprovider.fs.lock",
            MacOsOpenReadWrite | MacOsOpenCreate | MacOsOpenCloseOnExec | MacOsOpenNoFollow,
            LinuxOwnerOnlyCreateMode
        );
        if (lockDescriptor == -1)
        {
            throw new IOException(
                $"Failed to open mutation lock in '{directory}' without following symbolic links.",
                Marshal.GetExceptionForHR(Marshal.GetHRForLastWin32Error())
            );
        }

        return new FileStream(
            new SafeFileHandle(new IntPtr(lockDescriptor), ownsHandle: true),
            FileAccess.ReadWrite
        );
    }

    [SupportedOSPlatform("windows")]
    private static FileStream AcquireWindowsMutationLockWithoutFollowingLockFile(string directory)
    {
        ValidateDirectoryIsNotSymbolicLink(directory);
        var lockFilePath = Path.Combine(directory, ".azureauth-credprovider.fs.lock");
        SafeFileHandle lockHandle = CreateWindowsFile(
            lockFilePath,
            WindowsGenericRead | WindowsGenericWrite,
            WindowsFileShareRead | WindowsFileShareWrite,
            IntPtr.Zero,
            WindowsOpenAlways,
            WindowsFileAttributeNormal | WindowsFileFlagOpenReparsePoint,
            IntPtr.Zero
        );
        if (lockHandle.IsInvalid)
        {
            throw new IOException(
                $"Failed to open mutation lock in '{directory}' without following reparse points.",
                Marshal.GetExceptionForHR(Marshal.GetHRForLastWin32Error())
            );
        }

        try
        {
            if (!GetWindowsFileInformation(lockHandle, out var fileInformation))
            {
                throw new IOException(
                    $"Failed to get mutation lock information for '{directory}'.",
                    Marshal.GetExceptionForHR(Marshal.GetHRForLastWin32Error())
                );
            }

            if (((FileAttributes)fileInformation.FileAttributes & FileAttributes.ReparsePoint) != 0)
            {
                throw new IOException($"The mutation lock in '{directory}' is a reparse point.");
            }

            var stream = new FileStream(lockHandle, FileAccess.ReadWrite);
            try
            {
                stream.Lock(0, long.MaxValue);
                return stream;
            }
            catch
            {
                stream.Dispose();
                throw;
            }
        }
        catch
        {
            lockHandle.Dispose();
            throw;
        }
    }

    [SupportedOSPlatform("linux")]
    private static FileStream AcquireLinuxMutationLockWithoutFollowingTargetDirectory(
        string directory
    )
    {
        using SafeFileHandle directoryHandle = OpenLinuxDirectoryChainWithoutFollowingSymlinks(
            directory
        );
        int directoryDescriptor = directoryHandle.DangerousGetHandle().ToInt32();
        var lockDescriptor = OpenAt(
            directoryDescriptor,
            ".azureauth-credprovider.fs.lock",
            LinuxOpenReadWrite
                | LinuxOpenCreate
                | LinuxOpenNonBlocking
                | LinuxOpenCloseOnExec
                | LinuxOpenNoFollow,
            LinuxOwnerOnlyCreateMode
        );
        if (lockDescriptor == -1)
        {
            throw new IOException(
                $"Failed to open mutation lock in '{directory}'.",
                Marshal.GetExceptionForHR(Marshal.GetHRForLastWin32Error())
            );
        }

        var lockHandle = new SafeFileHandle(new IntPtr(lockDescriptor), ownsHandle: true);
        try
        {
            var status = StatOpenLinuxFile(
                lockHandle,
                Path.Combine(directory, ".azureauth-credprovider.fs.lock")
            );
            if ((status.Mode & LinuxStatxModeTypeMask) != LinuxStatxModeRegularFile)
            {
                throw new IOException($"The mutation lock in '{directory}' is not a regular file.");
            }

            var stream = new FileStream(lockHandle, FileAccess.ReadWrite);
            try
            {
                stream.Lock(0, long.MaxValue);
                return stream;
            }
            catch
            {
                stream.Dispose();
                throw;
            }
        }
        catch
        {
            lockHandle.Dispose();
            throw;
        }
    }

    [SupportedOSPlatform("linux")]
    private static SafeFileHandle OpenLinuxDirectoryChainWithoutFollowingSymlinks(string directory)
    {
        string fullPath = Path.GetFullPath(directory);
        string? root = Path.GetPathRoot(fullPath);
        if (string.IsNullOrEmpty(root))
        {
            throw new IOException($"The lock directory '{directory}' must be fully qualified.");
        }

        int currentDescriptor = Open(
            root,
            LinuxOpenReadOnly | LinuxOpenDirectory | LinuxOpenCloseOnExec | LinuxOpenNoFollow
        );
        if (currentDescriptor == -1)
        {
            throw new IOException(
                $"Failed to open lock directory root '{root}' without following symbolic links.",
                Marshal.GetExceptionForHR(Marshal.GetHRForLastWin32Error())
            );
        }

        var currentHandle = new SafeFileHandle(new IntPtr(currentDescriptor), ownsHandle: true);
        try
        {
            string relativePath = Path.GetRelativePath(root, fullPath);
            if (relativePath == ".")
            {
                return currentHandle;
            }

            foreach (
                string component in relativePath.Split(
                    Path.DirectorySeparatorChar,
                    StringSplitOptions.RemoveEmptyEntries
                )
            )
            {
                if (component is "." or "..")
                {
                    throw new IOException(
                        $"The lock directory '{directory}' contains an unsafe path component."
                    );
                }

                int nextDescriptor = OpenAt(
                    currentDescriptor,
                    component,
                    LinuxOpenReadOnly
                        | LinuxOpenDirectory
                        | LinuxOpenCloseOnExec
                        | LinuxOpenNoFollow,
                    mode: 0
                );
                if (nextDescriptor == -1)
                {
                    throw new IOException(
                        $"Failed to open lock directory component '{component}' in "
                            + $"'{directory}' without following symbolic links.",
                        Marshal.GetExceptionForHR(Marshal.GetHRForLastWin32Error())
                    );
                }

                var nextHandle = new SafeFileHandle(new IntPtr(nextDescriptor), ownsHandle: true);
                currentHandle.Dispose();
                currentHandle = nextHandle;
                currentDescriptor = nextDescriptor;
            }

            return currentHandle;
        }
        catch
        {
            currentHandle.Dispose();
            throw;
        }
    }

    private static void ThrowIfConditionalMutationUnsupported(FileMutationExpectation? expectation)
    {
        if (expectation is not null && !IsConditionalFileMutationSupported())
        {
            throw new PlatformNotSupportedException(
                "Conditional file mutations require cross-process file locking, which is not "
                    + "available for this implementation on the current platform."
            );
        }
    }

    private static bool IsConditionalFileMutationSupported()
    {
        return IsAtomicWritePlatformSupported() && !OperatingSystem.IsMacOS();
    }

    private static void ValidateMutationExpectation(
        string fullPath,
        FileMutationExpectation? expectation
    )
    {
        if (expectation is null)
        {
            return;
        }

        AtomicWriteTargetState targetState = GetAtomicWriteTargetState(fullPath);
        if (!expectation.Exists)
        {
            if (targetState != AtomicWriteTargetState.Missing)
            {
                throw new InvalidOperationException(
                    "Configuration conflict: expected mutation target to be absent."
                );
            }

            return;
        }

        if (targetState != AtomicWriteTargetState.File)
        {
            throw new InvalidOperationException(
                "Configuration conflict: expected mutation target to exist."
            );
        }

        if (string.IsNullOrWhiteSpace(expectation.Sha256Hash))
        {
            throw new InvalidOperationException(
                "Configuration conflict: mutation target before-state hash is required."
            );
        }

        string actualHash = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(fullPath)))
            .ToLowerInvariant();
        if (!string.Equals(expectation.Sha256Hash, actualHash, StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                "Configuration conflict: mutation target before-state hash does not match."
            );
        }
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
            CreateDirectoryChainWithoutFollowingSymbolicLinks(
                missingDirectories,
                OwnerOnlyDirectoryMode
            );
            foreach (var createdDirectory in missingDirectories)
            {
                File.SetUnixFileMode(createdDirectory, OwnerOnlyDirectoryMode);
            }

            ValidateDirectoryIsNotSymbolicLink(directory);
            return;
        }

        CreateDirectoryChainWithoutFollowingSymbolicLinks(missingDirectories, unixCreateMode: null);
        ValidateDirectoryIsNotSymbolicLink(directory);
    }

    private static void CreateDirectoryChainWithoutFollowingSymbolicLinks(
        List<string> missingDirectories,
        UnixFileMode? unixCreateMode
    )
    {
        foreach (var missingDirectory in missingDirectories)
        {
            var parent = Path.GetDirectoryName(missingDirectory);
            if (!string.IsNullOrEmpty(parent))
            {
                ValidateDirectoryIsNotSymbolicLink(parent);
            }

            if (unixCreateMode is { } mode && !OperatingSystem.IsWindows())
            {
                Directory.CreateDirectory(missingDirectory, mode);
            }
            else
            {
                Directory.CreateDirectory(missingDirectory);
            }

            ValidateDirectoryIsNotSymbolicLink(missingDirectory);
            ValidateParentDirectoryChainHasNoSymbolicLinks(missingDirectory);
        }
    }

    private static void ValidateExistingParentDirectoryChainHasNoSymbolicLinks(string directory)
    {
        foreach (var parentDirectory in EnumerateDirectoryChain(Path.GetFullPath(directory)))
        {
            if (!TryGetFileAttributes(parentDirectory, out var attributes))
            {
                continue;
            }

            ThrowIfDirectoryAttributesAreUnsafe(parentDirectory, attributes);
        }
    }

    private static void ValidateParentDirectoryChainHasNoSymbolicLinks(string fullPath)
    {
        var directory = Path.GetDirectoryName(fullPath);
        if (string.IsNullOrEmpty(directory))
        {
            directory = Directory.GetCurrentDirectory();
        }

        foreach (var parentDirectory in EnumerateDirectoryChain(directory))
        {
            ValidateDirectoryIsNotSymbolicLink(parentDirectory);
        }
    }

    private static void ValidateDirectoryIsNotSymbolicLink(string directory)
    {
        try
        {
            ThrowIfDirectoryAttributesAreUnsafe(directory, File.GetAttributes(directory));
        }
        catch (FileNotFoundException exception)
        {
            throw new IOException(
                $"The target parent directory '{directory}' could not be proven safe.",
                exception
            );
        }
        catch (DirectoryNotFoundException exception)
        {
            throw new IOException(
                $"The target parent directory '{directory}' could not be proven safe.",
                exception
            );
        }
    }

    private static bool TryGetFileAttributes(string path, out FileAttributes attributes)
    {
        try
        {
            attributes = File.GetAttributes(path);
            return true;
        }
        catch (FileNotFoundException)
        {
            attributes = default;
            return false;
        }
        catch (DirectoryNotFoundException)
        {
            attributes = default;
            return false;
        }
    }

    private static void ThrowIfDirectoryAttributesAreUnsafe(
        string directory,
        FileAttributes attributes
    )
    {
        if ((attributes & FileAttributes.ReparsePoint) != 0)
        {
            throw new NotSupportedException(
                "Conditional file mutations reject symbolic-link or reparse-point directories "
                    + "in target parent paths."
            );
        }

        if ((attributes & FileAttributes.Directory) == 0)
        {
            throw new IOException($"The target parent path '{directory}' is not a directory.");
        }
    }

    private static Stack<string> EnumerateDirectoryChain(string path)
    {
        var directories = new Stack<string>();
        string? current = Path.TrimEndingDirectorySeparator(path);
        while (!string.IsNullOrEmpty(current))
        {
            directories.Push(current);
            string? parent = Path.GetDirectoryName(current);
            if (
                string.IsNullOrEmpty(parent)
                || string.Equals(parent, current, StringComparison.Ordinal)
            )
            {
                break;
            }

            current = parent;
        }

        return directories;
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
        if (!IsAtomicWritePlatformSupported())
        {
            throw new PlatformNotSupportedException(
                "Durable atomic writes are supported only on Windows, macOS, and Linux."
            );
        }
    }

    private static bool IsAtomicWritePlatformSupported()
    {
        return OperatingSystem.IsWindows()
            || OperatingSystem.IsLinux()
            || OperatingSystem.IsMacOS();
    }

    private static FileSystemInfo CreateFileSystemInfo(string path)
    {
        return Directory.Exists(path) && !File.Exists(path)
            ? new DirectoryInfo(path)
            : new FileInfo(path);
    }

    private static long GetFileLengthWithoutFollowingReparsePoints(string path)
    {
        if (OperatingSystem.IsLinux())
        {
            var status = StatLinuxPathWithoutFollowingSymlinks(path);
            if ((status.Mode & LinuxStatxModeTypeMask) != LinuxStatxModeRegularFile)
            {
                throw new IOException(
                    $"Cannot get the length of '{path}' because it is not a regular file."
                );
            }

            return checked((long)status.Size);
        }

        if (OperatingSystem.IsWindows())
        {
            using SafeFileHandle handle = CreateWindowsFile(
                path,
                WindowsGenericRead,
                WindowsFileShareRead | WindowsFileShareWrite | WindowsFileShareDelete,
                IntPtr.Zero,
                WindowsOpenExisting,
                WindowsFileAttributeNormal | WindowsFileFlagOpenReparsePoint,
                IntPtr.Zero
            );
            if (handle.IsInvalid)
            {
                throw new IOException(
                    $"Failed to open '{path}' without following reparse points.",
                    Marshal.GetExceptionForHR(Marshal.GetHRForLastWin32Error())
                );
            }

            if (!GetWindowsFileInformation(handle, out var fileInformation))
            {
                throw new IOException(
                    $"Failed to get file information for '{path}'.",
                    Marshal.GetExceptionForHR(Marshal.GetHRForLastWin32Error())
                );
            }

            var attributes = (FileAttributes)fileInformation.FileAttributes;
            if (
                (attributes & FileAttributes.ReparsePoint) != 0
                || (attributes & FileAttributes.Directory) != 0
            )
            {
                throw new IOException(
                    $"Cannot get the length of '{path}' because it is not a regular file."
                );
            }

            if (!GetWindowsFileSize(handle, out long length))
            {
                throw new IOException(
                    $"Failed to get the length of '{path}'.",
                    Marshal.GetExceptionForHR(Marshal.GetHRForLastWin32Error())
                );
            }

            return length;
        }

        throw new PlatformNotSupportedException(
            "No-follow file length metadata is supported only on Windows and Linux."
        );
    }

    private static UnixFileMode? GetAtomicWriteUnixCreateMode(AtomicWriteOptions options)
    {
        if (OperatingSystem.IsWindows())
        {
            return null;
        }

        if ((options & AtomicWriteOptions.RestrictUnixFileModeToOwnerOnly) != 0)
        {
            return OwnerOnlyFileMode;
        }

        return OwnerOnlyFileMode;
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

    private static void WriteTemporaryAllBytes(
        string temporaryPath,
        byte[] contents,
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
        stream.Write(contents);
        stream.Flush(flushToDisk: true);
    }

    private static SafeFileHandle? OpenTemporaryDirectoryCleanupHandleIfSupported(string directory)
    {
        if (OperatingSystem.IsLinux())
        {
            return OpenLinuxDirectoryChainWithoutFollowingSymlinks(directory);
        }

        if (OperatingSystem.IsMacOS())
        {
            return OpenUnixDirectoryChainWithoutFollowingSymlinks(
                directory,
                MacOsOpenReadOnly
                    | MacOsOpenDirectory
                    | MacOsOpenCloseOnExec
                    | MacOsOpenNoFollow
            );
        }

        return null;
    }

    private static SafeFileHandle? OpenTemporaryFileCleanupHandleIfSupported(string temporaryPath)
    {
        if (!OperatingSystem.IsWindows())
        {
            return null;
        }

        SafeFileHandle handle = CreateWindowsFile(
            temporaryPath,
            WindowsDeleteAccess,
            WindowsFileShareRead | WindowsFileShareWrite | WindowsFileShareDelete,
            IntPtr.Zero,
            WindowsOpenExisting,
            WindowsFileAttributeNormal | WindowsFileFlagOpenReparsePoint,
            IntPtr.Zero
        );
        if (handle.IsInvalid)
        {
            throw new IOException(
                $"Failed to open temporary file '{temporaryPath}' for cleanup.",
                Marshal.GetExceptionForHR(Marshal.GetHRForLastWin32Error())
            );
        }

        return handle;
    }

    private static void DeleteTemporaryFileIfExists(
        string temporaryPath,
        SafeFileHandle? temporaryDirectoryHandle,
        SafeFileHandle? temporaryFileCleanupHandle
    )
    {
        using (temporaryFileCleanupHandle)
        {
            if (temporaryFileCleanupHandle is not null && OperatingSystem.IsWindows())
            {
                DeleteTemporaryWindowsFileByHandle(temporaryFileCleanupHandle, temporaryPath);
                return;
            }
        }

        if (
            temporaryDirectoryHandle is not null
            && (OperatingSystem.IsLinux() || OperatingSystem.IsMacOS())
        )
        {
            DeleteTemporaryUnixFileFromOpenDirectoryIfExists(
                temporaryDirectoryHandle,
                Path.GetFileName(temporaryPath),
                temporaryPath
            );
            return;
        }

        try
        {
            _ = File.GetAttributes(temporaryPath);
        }
        catch (FileNotFoundException)
        {
            return;
        }
        catch (DirectoryNotFoundException)
        {
            return;
        }

        File.Delete(temporaryPath);
    }

    private static AtomicWriteTargetState GetAtomicWriteTargetState(string path)
    {
        try
        {
            var attributes = File.GetAttributes(path);
            if ((attributes & FileAttributes.ReparsePoint) != 0)
            {
                return AtomicWriteTargetState.ReparsePoint;
            }

            if ((attributes & FileAttributes.Directory) != 0)
            {
                return AtomicWriteTargetState.Other;
            }

            return AtomicWriteTargetState.File;
        }
        catch (FileNotFoundException)
        {
            return AtomicWriteTargetState.Missing;
        }
        catch (DirectoryNotFoundException)
        {
            return AtomicWriteTargetState.Missing;
        }
    }

    private static SafeFileHandle OpenUnixDirectoryChainWithoutFollowingSymlinks(
        string directory,
        int openFlags
    )
    {
        string fullPath = Path.GetFullPath(directory);
        string? root = Path.GetPathRoot(fullPath);
        if (string.IsNullOrEmpty(root))
        {
            throw new IOException($"The cleanup directory '{directory}' must be fully qualified.");
        }

        int currentDescriptor = Open(root, openFlags);
        if (currentDescriptor == -1)
        {
            throw new IOException(
                $"Failed to open cleanup directory root '{root}' without following symbolic links.",
                Marshal.GetExceptionForHR(Marshal.GetHRForLastWin32Error())
            );
        }

        var currentHandle = new SafeFileHandle(new IntPtr(currentDescriptor), ownsHandle: true);
        try
        {
            string relativePath = Path.GetRelativePath(root, fullPath);
            if (relativePath == ".")
            {
                return currentHandle;
            }

            foreach (
                string component in relativePath.Split(
                    Path.DirectorySeparatorChar,
                    StringSplitOptions.RemoveEmptyEntries
                )
            )
            {
                if (component is "." or "..")
                {
                    throw new IOException(
                        $"The cleanup directory '{directory}' contains an unsafe path component."
                    );
                }

                int nextDescriptor = OpenAt(
                    currentDescriptor,
                    component,
                    openFlags,
                    mode: 0
                );
                if (nextDescriptor == -1)
                {
                    throw new IOException(
                        $"Failed to open cleanup directory component '{component}' in "
                            + $"'{directory}' without following symbolic links.",
                        Marshal.GetExceptionForHR(Marshal.GetHRForLastWin32Error())
                    );
                }

                var nextHandle = new SafeFileHandle(new IntPtr(nextDescriptor), ownsHandle: true);
                currentHandle.Dispose();
                currentHandle = nextHandle;
                currentDescriptor = nextDescriptor;
            }

            return currentHandle;
        }
        catch
        {
            currentHandle.Dispose();
            throw;
        }
    }

    private static void DeleteTemporaryUnixFileFromOpenDirectoryIfExists(
        SafeFileHandle directoryHandle,
        string fileName,
        string displayPath
    )
    {
        int directoryDescriptor = directoryHandle.DangerousGetHandle().ToInt32();
        if (UnlinkAt(directoryDescriptor, fileName, flags: 0) == 0)
        {
            return;
        }

        int error = Marshal.GetLastWin32Error();
        if (error == 2)
        {
            return;
        }

        throw new IOException(
            $"Failed to delete temporary file '{displayPath}' from its original parent directory.",
            Marshal.GetExceptionForHR(Marshal.GetHRForLastWin32Error())
        );
    }

    [SupportedOSPlatform("windows")]
    private static void DeleteTemporaryWindowsFileByHandle(
        SafeFileHandle fileHandle,
        string displayPath
    )
    {
        var disposition = new WindowsFileDispositionInfo { DeleteFile = true };
        if (
            SetWindowsFileInformationByHandle(
                fileHandle,
                WindowsFileInformationByHandleClass.FileDispositionInfo,
                ref disposition,
                (uint)Marshal.SizeOf<WindowsFileDispositionInfo>()
            )
        )
        {
            return;
        }

        throw new IOException(
            $"Failed to mark temporary file '{displayPath}' for deletion by handle.",
            Marshal.GetExceptionForHR(Marshal.GetHRForLastWin32Error())
        );
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

    [DllImport("libc", EntryPoint = "openat", SetLastError = true)]
    private static extern int OpenAt(
        int directoryFileDescriptor,
        [MarshalAs(UnmanagedType.LPUTF8Str)] string path,
        int flags,
        uint mode
    );

    [DllImport("libc", EntryPoint = "fsync", SetLastError = true)]
    private static extern int Fsync(SafeFileHandle handle);

    [DllImport("libc", EntryPoint = "unlinkat", SetLastError = true)]
    private static extern int UnlinkAt(
        int directoryFileDescriptor,
        [MarshalAs(UnmanagedType.LPUTF8Str)] string path,
        int flags
    );

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

    [DllImport("kernel32.dll", EntryPoint = "CreateFileW", SetLastError = true)]
    private static extern SafeFileHandle CreateWindowsFile(
        [MarshalAs(UnmanagedType.LPWStr)] string fileName,
        uint desiredAccess,
        uint shareMode,
        IntPtr securityAttributes,
        uint creationDisposition,
        uint flagsAndAttributes,
        IntPtr templateFile
    );

    [DllImport("kernel32.dll", EntryPoint = "GetFileSizeEx", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetWindowsFileSize(SafeFileHandle fileHandle, out long fileSize);

    [DllImport("kernel32.dll", EntryPoint = "GetFileInformationByHandle", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetWindowsFileInformation(
        SafeFileHandle fileHandle,
        out WindowsFileInformation fileInformation
    );

    [DllImport("kernel32.dll", EntryPoint = "SetFileInformationByHandle", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetWindowsFileInformationByHandle(
        SafeFileHandle fileHandle,
        WindowsFileInformationByHandleClass fileInformationClass,
        ref WindowsFileDispositionInfo fileInformation,
        uint bufferSize
    );

    private enum WindowsFileInformationByHandleClass
    {
        FileDispositionInfo = 4,
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct WindowsFileDispositionInfo
    {
        [MarshalAs(UnmanagedType.Bool)]
        public bool DeleteFile;
    }

    [StructLayout(LayoutKind.Sequential, Pack = 4)]
    private struct WindowsFileInformation
    {
        public uint FileAttributes;
        public long CreationTime;
        public long LastAccessTime;
        public long LastWriteTime;
        public uint VolumeSerialNumber;
        public uint FileSizeHigh;
        public uint FileSizeLow;
        public uint NumberOfLinks;
        public uint FileIndexHigh;
        public uint FileIndexLow;
    }

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

    private enum AtomicWriteTargetState
    {
        Missing,
        File,
        ReparsePoint,
        Other,
    }
}
