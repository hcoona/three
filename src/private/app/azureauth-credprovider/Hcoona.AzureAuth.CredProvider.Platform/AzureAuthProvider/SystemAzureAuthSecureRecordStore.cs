using System.Collections.Concurrent;
using System.Diagnostics;
using System.Text;
using System.Text.Json;
using System.Runtime.Versioning;
using System.Runtime.InteropServices;
using System.Text.Json.Serialization;
using Hcoona.AzureAuth.CredProvider.Platform.FileSystem;

namespace Hcoona.AzureAuth.CredProvider.Platform.AzureAuthProvider;

public sealed record SystemAzureAuthSecureRecordStoreOptions
{
    public const string ConfigRootEnvironmentVariable = "AZUREAUTH_CREDPROVIDER_CONFIG_ROOT";

    public string? ConfigRootPath { get; init; }

    public Func<string, string?>? EnvironmentVariableReader { get; init; }

    public IAzureAuthDirectoryDurability? DirectoryDurability { get; init; }

    /// <summary>Maximum time to wait for another process to release the record lock.</summary>
    public TimeSpan LockTimeout { get; init; } = TimeSpan.FromSeconds(3);

    internal ILinuxFileMetadataProvider? LinuxFileMetadataProvider { get; init; }

    internal IAzureAuthFileLock FileLock { get; init; } = new SystemAzureAuthFileLock();
}

public interface IAzureAuthDirectoryDurability
{
    bool IsSupported { get; }

    void Flush(string directoryPath);
}

/// <summary>Linux/WSL owner-only, process-safe persistence for provider enrollment records.</summary>
public sealed class SystemAzureAuthSecureRecordStore : IAzureAuthSecureRecordStore
{
    private const UnixFileMode DirectoryMode =
        UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute;
    private const UnixFileMode FileMode = UnixFileMode.UserRead | UnixFileMode.UserWrite;
    private const UnixFileMode StandardStickyTemporaryDirectoryMode =
        UnixFileMode.UserRead
        | UnixFileMode.UserWrite
        | UnixFileMode.UserExecute
        | UnixFileMode.GroupRead
        | UnixFileMode.GroupWrite
        | UnixFileMode.GroupExecute
        | UnixFileMode.OtherRead
        | UnixFileMode.OtherWrite
        | UnixFileMode.OtherExecute
        | UnixFileMode.StickyBit;
    private const int MaximumRecordBytes = 1024 * 1024;
    private const int MaximumEnvelopeBytes = 2 * 1024 * 1024;
    private static readonly TimeSpan MaximumLockTimeout = TimeSpan.FromSeconds(5);
    private static readonly TimeSpan InitialLockRetryDelay = TimeSpan.FromMilliseconds(25);
    private static readonly TimeSpan MaximumLockRetryDelay = TimeSpan.FromMilliseconds(250);
    private static readonly UTF8Encoding StrictUtf8 = new(false, true);
    private static readonly ConcurrentDictionary<string, object> ProcessLocks =
        new(OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal);
    private readonly string? configRootPath;
    private readonly SystemFileSystem fileSystem = new();
    private readonly IAzureAuthDirectoryDurability directoryDurability;
    private readonly IAzureAuthFileLock fileLock;
    private readonly ILinuxFileMetadataProvider linuxFileMetadataProvider;
    private readonly TimeSpan lockTimeout;

    internal const int MaximumEnvelopeBytesForTesting = MaximumEnvelopeBytes;

    internal const int MaximumRecordBytesForTesting = MaximumRecordBytes;

    public SystemAzureAuthSecureRecordStore()
        : this(new SystemAzureAuthSecureRecordStoreOptions())
    { }

    public SystemAzureAuthSecureRecordStore(string configRootPath)
        : this(new SystemAzureAuthSecureRecordStoreOptions { ConfigRootPath = configRootPath })
    { }

    public SystemAzureAuthSecureRecordStore(SystemAzureAuthSecureRecordStoreOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        if (options.LockTimeout <= TimeSpan.Zero || options.LockTimeout > MaximumLockTimeout)
        {
            throw new ArgumentOutOfRangeException(
                nameof(options),
                $"Secure-record lock timeout must be positive and cannot exceed {MaximumLockTimeout}.");
        }

        configRootPath = ResolveConfigRoot(options);
        directoryDurability =
            options.DirectoryDurability ?? new LinuxDirectoryDurability();
        fileLock = options.FileLock ?? throw new ArgumentException(
            "A file-lock implementation is required.",
            nameof(options));
        linuxFileMetadataProvider =
            options.LinuxFileMetadataProvider ?? new SystemLinuxFileMetadataProvider();
        lockTimeout = options.LockTimeout;
    }

    public string? ConfigRootPath => configRootPath;

    public AzureAuthSecureRecordReadResult Read(string path)
    {
        AzureAuthRecordNamePolicy.EnsureValid(path);
        if (!OperatingSystem.IsLinux() || configRootPath is null)
        {
            return AzureAuthSecureRecordReadResult.Unsupported();
        }

        try
        {
            using RecordLockScope recordLock = AcquireRecordLock(createRoot: false);
            return ReadUnderLock(path);
        }
        catch (DirectoryNotFoundException)
        {
            return AzureAuthSecureRecordReadResult.Missing();
        }
        catch (PlatformNotSupportedException)
        {
            return AzureAuthSecureRecordReadResult.Unsupported();
        }
        catch (SecureRecordLockUnavailableException)
        {
            return AzureAuthSecureRecordReadResult.Unavailable();
        }
        catch (Exception exception) when (IsUnsafeFileSystemFailure(exception))
        {
            return AzureAuthSecureRecordReadResult.Unsafe();
        }
    }

    public AzureAuthSecureRecordRevisionCheckResult CompareRevision(
        string path,
        string expectedRevision)
    {
        AzureAuthRecordNamePolicy.EnsureValid(path);
        ArgumentException.ThrowIfNullOrWhiteSpace(expectedRevision);
        if (!OperatingSystem.IsLinux() || configRootPath is null)
        {
            return AzureAuthSecureRecordRevisionCheckResult.Unsupported();
        }

        try
        {
            using RecordLockScope recordLock = AcquireRecordLock(createRoot: false);
            return CompareRevisionUnderLock(path, expectedRevision);
        }
        catch (DirectoryNotFoundException)
        {
            return expectedRevision == AzureAuthSecureRecordStoreContract.MissingRevision
                ? AzureAuthSecureRecordRevisionCheckResult.Match()
                : AzureAuthSecureRecordRevisionCheckResult.Conflict();
        }
        catch (PlatformNotSupportedException)
        {
            return AzureAuthSecureRecordRevisionCheckResult.Unsupported();
        }
        catch (SecureRecordLockUnavailableException)
        {
            return AzureAuthSecureRecordRevisionCheckResult.Unavailable();
        }
        catch (Exception exception) when (IsUnsafeFileSystemFailure(exception))
        {
            return AzureAuthSecureRecordRevisionCheckResult.Unsafe();
        }
    }

    public AzureAuthSecureRecordWriteResult CompareExchange(
        string path,
        string expectedRevision,
        ReadOnlyMemory<byte> newContent)
    {
        AzureAuthRecordNamePolicy.EnsureValid(path);
        ArgumentException.ThrowIfNullOrWhiteSpace(expectedRevision);
        if (newContent.Length > MaximumRecordBytes)
        {
            throw new ArgumentOutOfRangeException(
                nameof(newContent),
                "Secure records cannot exceed one MiB.");
        }

        _ = StrictUtf8.GetString(newContent.Span);
        if (!OperatingSystem.IsLinux() || configRootPath is null)
        {
            return AzureAuthSecureRecordWriteResult.Unsupported();
        }
        if (!directoryDurability.IsSupported)
        {
            return AzureAuthSecureRecordWriteResult.Unsupported();
        }

        try
        {
            using RecordLockScope recordLock = AcquireRecordLock(createRoot: true);
            return CompareExchangeUnderLock(path, expectedRevision, newContent);
        }
        catch (Exception exception) when (IsUnsafeFileSystemFailure(exception))
        {
            return AzureAuthSecureRecordWriteResult.Unsafe();
        }
        catch (SecureRecordLockUnavailableException)
        {
            return AzureAuthSecureRecordWriteResult.Unavailable();
        }
        catch (PlatformNotSupportedException)
        {
            return AzureAuthSecureRecordWriteResult.Unsupported();
        }
    }

    private static string? ResolveConfigRoot(SystemAzureAuthSecureRecordStoreOptions options)
    {
        Func<string, string?> readEnvironment =
            options.EnvironmentVariableReader ?? Environment.GetEnvironmentVariable;
        string? explicitRoot = NullIfWhiteSpace(options.ConfigRootPath)
            ?? NullIfWhiteSpace(
                readEnvironment(SystemAzureAuthSecureRecordStoreOptions.ConfigRootEnvironmentVariable));
        if (explicitRoot is not null)
        {
            return TryGetAbsolutePath(explicitRoot);
        }

        string? xdgConfigHome = NullIfWhiteSpace(readEnvironment("XDG_CONFIG_HOME"));
        if (xdgConfigHome is not null)
        {
            string? absoluteXdgPath = TryGetAbsolutePath(xdgConfigHome);
            return absoluteXdgPath is null
                ? null
                : Path.Combine(absoluteXdgPath, "azureauth-credprovider");
        }

        string? home = NullIfWhiteSpace(readEnvironment("HOME"));
        string? absoluteHome = home is null ? null : TryGetAbsolutePath(home);
        return absoluteHome is null
            ? null
            : Path.Combine(absoluteHome, ".config", "azureauth-credprovider");
    }

    private static string? TryGetAbsolutePath(string path)
    {
        try
        {
            return Path.IsPathFullyQualified(path)
                ? Path.TrimEndingDirectorySeparator(Path.GetFullPath(path))
                : null;
        }
        catch (Exception exception) when (exception is ArgumentException or NotSupportedException)
        {
            return null;
        }
    }

    [SupportedOSPlatform("linux")]
    private RecordLockScope AcquireRecordLock(bool createRoot)
    {
        object processLock = ProcessLocks.GetOrAdd(configRootPath!, static _ => new object());
        FileStream? fileLockStream = null;
        Monitor.Enter(processLock);
        try
        {
            EnsureRoot(createRoot);
            fileLockStream = AcquireLockUnderProcessLock();
            return new RecordLockScope(processLock, fileLockStream);
        }
        catch
        {
            fileLockStream?.Dispose();
            Monitor.Exit(processLock);
            throw;
        }
    }

    [SupportedOSPlatform("linux")]
    private AzureAuthSecureRecordRevisionCheckResult CompareRevisionUnderLock(
        string path,
        string expectedRevision)
    {
        AzureAuthSecureRecordReadResult current = ReadUnderLock(path);
        return current.Status switch
        {
            AzureAuthSecureRecordReadStatus.Missing
                when expectedRevision == AzureAuthSecureRecordStoreContract.MissingRevision =>
                AzureAuthSecureRecordRevisionCheckResult.Match(),
            AzureAuthSecureRecordReadStatus.Present
                when string.Equals(
                    current.Revision,
                    expectedRevision,
                    StringComparison.Ordinal) =>
                AzureAuthSecureRecordRevisionCheckResult.Match(),
            AzureAuthSecureRecordReadStatus.Missing
                or AzureAuthSecureRecordReadStatus.Present =>
                AzureAuthSecureRecordRevisionCheckResult.Conflict(),
            _ => AzureAuthSecureRecordRevisionCheckResult.Unsafe(),
        };
    }

    [SupportedOSPlatform("linux")]
    private AzureAuthSecureRecordWriteResult CompareExchangeUnderLock(
        string path,
        string expectedRevision,
        ReadOnlyMemory<byte> newContent)
    {
        AzureAuthSecureRecordReadResult current = ReadUnderLock(path);
        if (!RevisionMatches(current, expectedRevision))
        {
            return current.Status is AzureAuthSecureRecordReadStatus.Missing
                or AzureAuthSecureRecordReadStatus.Present
                ? AzureAuthSecureRecordWriteResult.Conflict()
                : AzureAuthSecureRecordWriteResult.Unsafe();
        }

        string revision = Guid.NewGuid().ToString("N");
        WriteUnderLock(path, revision, newContent.Span);
        return AzureAuthSecureRecordWriteResult.Success(revision);
    }

    [SupportedOSPlatform("linux")]
    private FileStream AcquireLockUnderProcessLock()
    {
        string lockPath = Path.Combine(configRootPath!, ".records.lock");
        ThrowIfReparsePoint(lockPath, allowMissing: true);
        var stream = new FileStream(
            lockPath,
            new FileStreamOptions
            {
                Mode = System.IO.FileMode.OpenOrCreate,
                Access = FileAccess.ReadWrite,
                Share = FileShare.ReadWrite,
                BufferSize = 1,
                UnixCreateMode = FileMode,
            });
        try
        {
            EnsureOwnedOwnerOnlyFile(lockPath);
            AcquireFileLockWithRetry(stream);
            EnsureSafePathChain(lockPath, includeFinal: true);
            return stream;
        }
        catch
        {
            stream.Dispose();
            throw;
        }
    }

    [SupportedOSPlatform("linux")]
    private void AcquireFileLockWithRetry(FileStream stream)
    {
        long startedAt = Stopwatch.GetTimestamp();
        TimeSpan retryDelay = InitialLockRetryDelay;
        IOException? lastContention = null;
        while (Stopwatch.GetElapsedTime(startedAt) < lockTimeout)
        {
            try
            {
                fileLock.Lock(stream, 0, 1);
                return;
            }
            catch (IOException exception)
            {
                lastContention = exception;
            }

            TimeSpan remaining = lockTimeout - Stopwatch.GetElapsedTime(startedAt);
            if (remaining <= TimeSpan.Zero)
            {
                break;
            }

            Thread.Sleep(retryDelay < remaining ? retryDelay : remaining);
            retryDelay = TimeSpan.FromTicks(
                Math.Min(retryDelay.Ticks * 2, MaximumLockRetryDelay.Ticks));
        }

        throw new SecureRecordLockUnavailableException(lastContention);
    }

    [SupportedOSPlatform("linux")]
    private AzureAuthSecureRecordReadResult ReadUnderLock(string recordName)
    {
        EnsureRoot(create: false);
        string recordPath = GetRecordPath(recordName);
        EnsureSafePathChain(recordPath, includeFinal: false);
        ThrowIfReparsePoint(recordPath, allowMissing: true);
        if (!File.Exists(recordPath))
        {
            return PathExists(recordPath)
                ? AzureAuthSecureRecordReadResult.Unsafe()
                : AzureAuthSecureRecordReadResult.Missing();
        }

        if (linuxFileMetadataProvider.GetMetadataWithoutFollowingLinks(recordPath).EntryType
            != LinuxFileSystemEntryType.RegularFile)
        {
            return AzureAuthSecureRecordReadResult.Unsafe();
        }

        EnsureOwnedOwnerOnlyFile(recordPath);
        byte[] envelopeBytes = ReadEnvelopeBytes(recordPath);
        if (envelopeBytes.Length == 0)
        {
            return AzureAuthSecureRecordReadResult.Unsafe();
        }

        string envelopeJson = StrictUtf8.GetString(envelopeBytes);
        SystemAzureAuthSecureRecordEnvelope? envelope = JsonSerializer.Deserialize(
            envelopeJson,
            SystemAzureAuthSecureRecordJsonContext.Default.SystemAzureAuthSecureRecordEnvelope);
        if (envelope is null
            || string.IsNullOrWhiteSpace(envelope.Revision)
            || envelope.Revision == AzureAuthSecureRecordStoreContract.MissingRevision
            || envelope.Content is null)
        {
            return AzureAuthSecureRecordReadResult.Unsafe();
        }

        byte[] content;
        try
        {
            content = Convert.FromBase64String(envelope.Content);
            if (content.Length > MaximumRecordBytes)
            {
                return AzureAuthSecureRecordReadResult.Unsafe();
            }

            _ = StrictUtf8.GetString(content);
        }
        catch (FormatException)
        {
            return AzureAuthSecureRecordReadResult.Unsafe();
        }

        return AzureAuthSecureRecordReadResult.Present(envelope.Revision, content);
    }

    private static byte[] ReadEnvelopeBytes(string recordPath)
    {
        using var stream = new FileStream(
            recordPath,
            new FileStreamOptions
            {
                Mode = System.IO.FileMode.Open,
                Access = FileAccess.Read,
                Share = FileShare.Read,
                BufferSize = 4096,
                Options = FileOptions.SequentialScan,
            });
        long length = stream.Length;
        if (length <= 0 || length > MaximumEnvelopeBytes)
        {
            return [];
        }

        var bytes = new byte[(int)length];
        stream.ReadExactly(bytes);
        return stream.ReadByte() == -1 ? bytes : [];
    }

    [SupportedOSPlatform("linux")]
    private void WriteUnderLock(string recordName, string revision, ReadOnlySpan<byte> content)
    {
        string recordPath = GetRecordPath(recordName);
        string parentPath = Path.GetDirectoryName(recordPath)
            ?? throw new IOException("Secure record parent directory is unavailable.");
        CreateOwnedDirectories(parentPath);
        EnsureSafePathChain(recordPath, includeFinal: false);
        ThrowIfReparsePoint(recordPath, allowMissing: true);

        var envelope = new SystemAzureAuthSecureRecordEnvelope
        {
            Revision = revision,
            Content = Convert.ToBase64String(content),
        };
        byte[] bytes = StrictUtf8.GetBytes(
            JsonSerializer.Serialize(
                envelope,
                SystemAzureAuthSecureRecordJsonContext.Default
                    .SystemAzureAuthSecureRecordEnvelope));
        string temporaryPath = Path.Combine(
            parentPath,
            $".{Path.GetFileName(recordPath)}.{Guid.NewGuid():N}.tmp");
        try
        {
            using (var stream = new FileStream(
                temporaryPath,
                new FileStreamOptions
                {
                    Mode = System.IO.FileMode.CreateNew,
                    Access = FileAccess.Write,
                    Share = FileShare.None,
                    BufferSize = 4096,
                    Options = FileOptions.WriteThrough,
                    UnixCreateMode = FileMode,
                }))
            {
                File.SetUnixFileMode(temporaryPath, FileMode);
                stream.Write(bytes);
                stream.Flush(flushToDisk: true);
            }

            EnsureOwnedOwnerOnlyFile(temporaryPath);
            EnsureSafePathChain(recordPath, includeFinal: false);
            ThrowIfReparsePoint(recordPath, allowMissing: true);
            File.Move(temporaryPath, recordPath, overwrite: true);
            EnsureOwnedOwnerOnlyFile(recordPath);
            directoryDurability.Flush(parentPath);
        }
        finally
        {
            try
            {
                File.Delete(temporaryPath);
            }
            catch (IOException)
            {
            }
            catch (UnauthorizedAccessException)
            {
            }
        }
    }

    [SupportedOSPlatform("linux")]
    private void EnsureRoot(bool create)
    {
        EnsureAncestorChainIsSafe(configRootPath!);
        bool rootExisted = Directory.Exists(configRootPath);
        if (!rootExisted)
        {
            if (PathExists(configRootPath!))
            {
                throw new UnauthorizedAccessException(
                    "Secure-store root must be a directory.");
            }

            if (!create)
            {
                throw new DirectoryNotFoundException();
            }

            CreateDirectoryChainDurably(configRootPath!);
        }

        ThrowIfReparsePoint(configRootPath!, allowMissing: false);
        EnsureOwnedDirectory(configRootPath!);
    }

    [SupportedOSPlatform("linux")]
    private void CreateOwnedDirectories(string targetDirectory)
    {
        string relative = Path.GetRelativePath(configRootPath!, targetDirectory);
        string current = configRootPath!;
        foreach (string segment in relative.Split(Path.DirectorySeparatorChar))
        {
            current = Path.Combine(current, segment);
            ThrowIfReparsePoint(current, allowMissing: true);
            bool existed = Directory.Exists(current);
            if (!existed)
            {
                Directory.CreateDirectory(current, DirectoryMode);
                EnsureOwnedDirectory(current);
                FlushNewDirectoryAndParent(current);
            }

            EnsureOwnedDirectory(current);
        }
    }

    [SupportedOSPlatform("linux")]
    private void CreateDirectoryChainDurably(string targetDirectory)
    {
        string root = Path.GetPathRoot(targetDirectory)
            ?? throw new IOException("Secure-store directory root is unavailable.");
        string current = root;
        foreach (string segment in targetDirectory[root.Length..]
            .Split(Path.DirectorySeparatorChar, StringSplitOptions.RemoveEmptyEntries))
        {
            current = Path.Combine(current, segment);
            ThrowIfReparsePoint(current, allowMissing: true);
            if (Directory.Exists(current))
            {
                continue;
            }

            Directory.CreateDirectory(current, DirectoryMode);
            EnsureOwnedDirectory(current);
            FlushNewDirectoryAndParent(current);
        }
    }

    private void FlushNewDirectoryAndParent(string directoryPath)
    {
        directoryDurability.Flush(directoryPath);
        string? parentPath = Path.GetDirectoryName(directoryPath);
        if (parentPath is null)
        {
            throw new IOException("Secure-store directory parent is unavailable.");
        }

        directoryDurability.Flush(parentPath);
    }

    [SupportedOSPlatform("linux")]
    private void EnsureSafePathChain(string finalPath, bool includeFinal)
    {
        EnsureRoot(create: false);
        string relative = Path.GetRelativePath(configRootPath!, finalPath);
        string[] segments = relative.Split(Path.DirectorySeparatorChar);
        string current = configRootPath!;
        int count = includeFinal ? segments.Length : segments.Length - 1;
        for (var index = 0; index < count; index++)
        {
            current = Path.Combine(current, segments[index]);
            ThrowIfReparsePoint(current, allowMissing: true);
            if (Directory.Exists(current))
            {
                EnsureOwnedDirectory(current);
            }
            else if (index < segments.Length - 1 && PathExists(current))
            {
                throw new UnauthorizedAccessException(
                    "Secure-store path components must be directories.");
            }
        }
    }

    [SupportedOSPlatform("linux")]
    private void EnsureAncestorChainIsSafe(string path)
    {
        string root = Path.GetPathRoot(path)
            ?? throw new IOException("Secure-store directory root is unavailable.");
        string current = root;
        EnsureSafeExistingAncestor(current);
        foreach (string segment in path[root.Length..]
            .Split(Path.DirectorySeparatorChar, StringSplitOptions.RemoveEmptyEntries))
        {
            current = Path.Combine(current, segment);
            ThrowIfReparsePoint(current, allowMissing: true);
            if (Directory.Exists(current))
            {
                EnsureSafeAncestorPermissions(current);
                continue;
            }

            if (PathExists(current))
            {
                throw new UnauthorizedAccessException(
                    "Secure-store path ancestors must be directories.");
            }

            break;
        }
    }

    [SupportedOSPlatform("linux")]
    private void EnsureSafeExistingAncestor(string path)
    {
        ThrowIfReparsePoint(path, allowMissing: false);
        if (!Directory.Exists(path))
        {
            throw new UnauthorizedAccessException(
                "Secure-store path ancestors must be directories.");
        }

        EnsureSafeAncestorPermissions(path);
    }

    [SupportedOSPlatform("linux")]
    private void EnsureSafeAncestorPermissions(string path)
    {
        LinuxFileMetadata metadata =
            linuxFileMetadataProvider.GetMetadataWithoutFollowingLinks(path);
        uint effectiveUserId = linuxFileMetadataProvider.EffectiveUserId;
        if (metadata.UserId != 0 && metadata.UserId != effectiveUserId)
        {
            throw new UnauthorizedAccessException(
                "Secure-store path ancestors must be owned by root or the current user.");
        }

        UnixFileMode mode = metadata.Mode;
        const UnixFileMode replaceWriteBits = UnixFileMode.GroupWrite | UnixFileMode.OtherWrite;
        if ((mode & replaceWriteBits) != 0
            && mode != StandardStickyTemporaryDirectoryMode)
        {
            throw new UnauthorizedAccessException(
                "Secure-store path ancestors must not permit replacement by group or other users.");
        }
    }

    [SupportedOSPlatform("linux")]
    private void EnsureOwnedDirectory(string path)
    {
        if (!Directory.Exists(path) || fileSystem.GetOwner(path) != fileSystem.GetCurrentOwner())
        {
            throw new UnauthorizedAccessException("Secure-store directory ownership is unsafe.");
        }

        if (File.GetUnixFileMode(path) != DirectoryMode)
        {
            throw new UnauthorizedAccessException("Secure-store directory permissions are unsafe.");
        }
    }

    [SupportedOSPlatform("linux")]
    private void EnsureOwnedOwnerOnlyFile(string path)
    {
        if (!File.Exists(path)
            || fileSystem.GetOwner(path) != fileSystem.GetCurrentOwner()
            || File.GetUnixFileMode(path) != FileMode)
        {
            throw new UnauthorizedAccessException("Secure-store file ownership or permissions are unsafe.");
        }
    }

    private string GetRecordPath(string recordName)
    {
        string path = Path.GetFullPath(
            Path.Combine(configRootPath!, recordName.Replace('/', Path.DirectorySeparatorChar)));
        string prefix = configRootPath!.EndsWith(Path.DirectorySeparatorChar)
            ? configRootPath
            : configRootPath + Path.DirectorySeparatorChar;
        if (!path.StartsWith(prefix, StringComparison.Ordinal))
        {
            throw new UnauthorizedAccessException("Secure record escaped its configuration root.");
        }

        return path;
    }

    private static void ThrowIfReparsePoint(string path, bool allowMissing)
    {
        try
        {
            if ((File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0)
            {
                throw new UnauthorizedAccessException("Secure-store paths cannot contain links.");
            }
        }
        catch (FileNotFoundException) when (allowMissing)
        {
        }
        catch (DirectoryNotFoundException) when (allowMissing)
        {
        }
    }

    private static bool PathExists(string path)
    {
        try
        {
            _ = File.GetAttributes(path);
            return true;
        }
        catch (FileNotFoundException)
        {
            return false;
        }
        catch (DirectoryNotFoundException)
        {
            return false;
        }
    }

    private static bool RevisionMatches(
        AzureAuthSecureRecordReadResult current,
        string expectedRevision) =>
        current.Status switch
        {
            AzureAuthSecureRecordReadStatus.Missing =>
                expectedRevision == AzureAuthSecureRecordStoreContract.MissingRevision,
            AzureAuthSecureRecordReadStatus.Present =>
                string.Equals(current.Revision, expectedRevision, StringComparison.Ordinal),
            _ => false,
        };

    private static string? NullIfWhiteSpace(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value;

    private static bool IsUnsafeFileSystemFailure(Exception exception) =>
        exception is IOException
            or UnauthorizedAccessException
            or JsonException
            or DecoderFallbackException
            or System.Security.SecurityException;

    private sealed class RecordLockScope(object processLock, FileStream fileLock) : IDisposable
    {
        private readonly object processLock = processLock;
        private FileStream? fileLock = fileLock;

        public void Dispose()
        {
            FileStream? stream = Interlocked.Exchange(ref fileLock, null);
            if (stream is null)
            {
                return;
            }

            try
            {
                stream.Dispose();
            }
            finally
            {
                Monitor.Exit(processLock);
            }
        }
    }
}

internal interface IAzureAuthFileLock
{
    void Lock(FileStream stream, long position, long length);
}

internal sealed class SystemAzureAuthFileLock : IAzureAuthFileLock
{
    [SupportedOSPlatform("linux")]
    public void Lock(FileStream stream, long position, long length) =>
        stream.Lock(position, length);
}

internal sealed class SecureRecordLockUnavailableException(IOException? innerException)
    : Exception("The secure-record lock is temporarily unavailable.", innerException);

internal enum LinuxFileSystemEntryType
{
    Other,
    RegularFile,
}

internal readonly record struct LinuxFileMetadata(
    LinuxFileSystemEntryType EntryType,
    UnixFileMode Mode,
    uint UserId);

internal interface ILinuxFileMetadataProvider
{
    uint EffectiveUserId { get; }

    LinuxFileMetadata GetMetadataWithoutFollowingLinks(string path);
}

internal sealed class SystemLinuxFileMetadataProvider : ILinuxFileMetadataProvider
{
    private const int AtCurrentWorkingDirectory = -100;
    private const int AtSymbolicLinkNoFollow = 0x100;
    private const uint StatxBasicStats = 0x7ff;
    private const ushort ModeTypeMask = 0xF000;
    private const ushort ModeRegularFile = 0x8000;
    private const ushort ModePermissionsMask = 0x0FFF;
    private const int FunctionNotImplemented = 38;

    public uint EffectiveUserId => GetEffectiveUserId();

    [SupportedOSPlatform("linux")]
    public LinuxFileMetadata GetMetadataWithoutFollowingLinks(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        try
        {
            if (Statx(
                    AtCurrentWorkingDirectory,
                    path,
                    AtSymbolicLinkNoFollow,
                    StatxBasicStats,
                    out LinuxStatx status) != 0)
            {
                int error = Marshal.GetLastPInvokeError();
                if (error == FunctionNotImplemented)
                {
                    throw new PlatformNotSupportedException(
                        "Secure-record file-type inspection requires Linux statx support.");
                }

                throw new IOException(
                    "Unable to inspect the secure-record file type.",
                    new System.ComponentModel.Win32Exception(error));
            }

            LinuxFileSystemEntryType entryType = (status.Mode & ModeTypeMask) == ModeRegularFile
                ? LinuxFileSystemEntryType.RegularFile
                : LinuxFileSystemEntryType.Other;
            return new LinuxFileMetadata(
                entryType,
                (UnixFileMode)(status.Mode & ModePermissionsMask),
                status.UserId);
        }
        catch (EntryPointNotFoundException exception)
        {
            throw new PlatformNotSupportedException(
                "Secure-record file-type inspection requires Linux statx support.",
                exception);
        }
    }

    [DllImport("libc", EntryPoint = "statx", SetLastError = true)]
    private static extern int Statx(
        int directoryFileDescriptor,
        [MarshalAs(UnmanagedType.LPUTF8Str)] string path,
        int flags,
        uint mask,
        out LinuxStatx status);

    [DllImport("libc", EntryPoint = "geteuid")]
    private static extern uint GetEffectiveUserId();

    [StructLayout(LayoutKind.Explicit, Size = 256)]
    private struct LinuxStatx
    {
        [FieldOffset(20)]
        public uint UserId;

        [FieldOffset(28)]
        public ushort Mode;
    }
}

internal sealed class LinuxDirectoryDurability : IAzureAuthDirectoryDurability
{
    private const int OpenReadOnly = 0;
    private const int OpenDirectory = 0x10000;

    public bool IsSupported => OperatingSystem.IsLinux();

    [SupportedOSPlatform("linux")]
    public void Flush(string directoryPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(directoryPath);
        int descriptor = Open(directoryPath, OpenReadOnly | OpenDirectory);
        if (descriptor < 0)
        {
            throw new IOException(
                "Unable to open the secure-store directory for durable synchronization.",
                new System.ComponentModel.Win32Exception(Marshal.GetLastPInvokeError()));
        }

        try
        {
            if (Fsync(descriptor) != 0)
            {
                throw new IOException(
                    "Unable to durably synchronize the secure-store directory.",
                    new System.ComponentModel.Win32Exception(Marshal.GetLastPInvokeError()));
            }
        }
        finally
        {
            _ = Close(descriptor);
        }
    }

    [DllImport("libc", EntryPoint = "open", SetLastError = true)]
    private static extern int Open(string path, int flags);

    [DllImport("libc", EntryPoint = "fsync", SetLastError = true)]
    private static extern int Fsync(int descriptor);

    [DllImport("libc", EntryPoint = "close", SetLastError = true)]
    private static extern int Close(int descriptor);
}

internal sealed record SystemAzureAuthSecureRecordEnvelope
{
    public required string Revision { get; init; }

    public required string Content { get; init; }
}

[JsonSerializable(typeof(SystemAzureAuthSecureRecordEnvelope))]
internal sealed partial class SystemAzureAuthSecureRecordJsonContext : JsonSerializerContext;
