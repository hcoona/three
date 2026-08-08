using System.Collections.Concurrent;
using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;

namespace Hcoona.AzureAuth.CredProvider.Platform.AzureAuthProvider;

public sealed record SystemAzureAuthSecureRecordStoreOptions
{
    public const string ConfigRootEnvironmentVariable = "AZUREAUTH_CREDPROVIDER_CONFIG_ROOT";

    public string? ConfigRootPath { get; init; }

    public Func<string, string?>? EnvironmentVariableReader { get; init; }

    public TimeSpan LockTimeout { get; init; } = TimeSpan.FromSeconds(3);
}

public sealed class SystemAzureAuthSecureRecordStore
    : IAzureAuthSecureRecordStore,
        IAzureAuthSecureRecordStoreOperationScope
{
    private const int MaximumRecordBytes = 1024 * 1024;
    private static readonly UTF8Encoding StrictUtf8 = new(false, true);
    private static readonly ConcurrentDictionary<string, object> ProcessLocks = new(
        OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal
    );

    private readonly string configRootPath;
    private readonly TimeSpan lockTimeout;

    internal const int MaximumRecordBytesForTesting = MaximumRecordBytes;

    public SystemAzureAuthSecureRecordStore()
        : this(new SystemAzureAuthSecureRecordStoreOptions()) { }

    public SystemAzureAuthSecureRecordStore(string configRootPath)
        : this(new SystemAzureAuthSecureRecordStoreOptions { ConfigRootPath = configRootPath }) { }

    public SystemAzureAuthSecureRecordStore(SystemAzureAuthSecureRecordStoreOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(options.LockTimeout, TimeSpan.Zero);
        configRootPath =
            ResolveConfigRoot(options)
            ?? throw new InvalidOperationException(
                "An absolute AzureAuth configuration root is required."
            );
        lockTimeout = options.LockTimeout;
    }

    public string ConfigRootPath => configRootPath;

    public AzureAuthSecureRecordReadResult Read(string path)
    {
        AzureAuthSecureRecordStoreContract.EnsureKnownRecordName(path);
        if (!Directory.Exists(configRootPath))
        {
            return AzureAuthSecureRecordReadResult.Missing();
        }

        using RecordLockScope scope = AcquireLock(createRoot: false);
        return ReadUnderLock(path);
    }

    TResult IAzureAuthSecureRecordStoreOperationScope.Execute<TResult>(
        Func<IAzureAuthSecureRecordStore, TResult> operation
    )
    {
        ArgumentNullException.ThrowIfNull(operation);
        using RecordLockScope scope = AcquireLock(createRoot: true);
        return operation(new LockedRecordStore(this));
    }

    public AzureAuthSecureRecordWriteResult CompareExchange(
        string path,
        string expectedRevision,
        ReadOnlyMemory<byte> newContent
    )
    {
        AzureAuthSecureRecordStoreContract.EnsureKnownRecordName(path);
        ArgumentException.ThrowIfNullOrWhiteSpace(expectedRevision);
        if (newContent.Length > MaximumRecordBytes)
        {
            throw new ArgumentOutOfRangeException(
                nameof(newContent),
                "AzureAuth records cannot exceed one MiB."
            );
        }

        _ = StrictUtf8.GetString(newContent.Span);
        using RecordLockScope scope = AcquireLock(createRoot: true);
        return CompareExchangeUnderLock(path, expectedRevision, newContent);
    }

    public AzureAuthSecureRecordWriteResult CompareDelete(string path, string expectedRevision)
    {
        AzureAuthSecureRecordStoreContract.EnsureKnownRecordName(path);
        ArgumentException.ThrowIfNullOrWhiteSpace(expectedRevision);

        if (!Directory.Exists(configRootPath))
        {
            return expectedRevision == AzureAuthSecureRecordStoreContract.MissingRevision
                ? AzureAuthSecureRecordWriteResult.Success(
                    AzureAuthSecureRecordStoreContract.MissingRevision
                )
                : AzureAuthSecureRecordWriteResult.Conflict();
        }

        using RecordLockScope scope = AcquireLock(createRoot: false);
        return CompareDeleteUnderLock(path, expectedRevision);
    }

    private RecordLockScope AcquireLock(bool createRoot)
    {
        object processLock = ProcessLocks.GetOrAdd(configRootPath, static _ => new object());
        Monitor.Enter(processLock);
        try
        {
            if (createRoot)
            {
                CreateProductDirectory(configRootPath);
            }
            else if (!Directory.Exists(configRootPath))
            {
                throw new DirectoryNotFoundException();
            }

            string lockPath = Path.Combine(configRootPath, ".records.lock");
            bool lockExisted = File.Exists(lockPath);
            var stream = new FileStream(
                lockPath,
                FileMode.OpenOrCreate,
                FileAccess.ReadWrite,
                FileShare.ReadWrite
            );
            try
            {
                if (!lockExisted)
                {
                    SetOwnerOnlyFileMode(lockPath);
                }

                AcquireFileLock(stream);
                return new RecordLockScope(processLock, stream);
            }
            catch
            {
                stream.Dispose();
                throw;
            }
        }
        catch
        {
            Monitor.Exit(processLock);
            throw;
        }
    }

    private void AcquireFileLock(FileStream stream)
    {
        if (OperatingSystem.IsMacOS())
        {
            throw new PlatformNotSupportedException(
                "AzureAuth persistence file locking is unsupported on macOS."
            );
        }

        long started = Stopwatch.GetTimestamp();
        while (true)
        {
            try
            {
                stream.Lock(0, 1);
                return;
            }
            catch (IOException) when (Stopwatch.GetElapsedTime(started) < lockTimeout)
            {
                Thread.Sleep(TimeSpan.FromMilliseconds(25));
            }
        }
    }

    private AzureAuthSecureRecordReadResult ReadUnderLock(string recordName)
    {
        string recordPath = GetRecordPath(recordName);
        if (!File.Exists(recordPath))
        {
            return AzureAuthSecureRecordReadResult.Missing();
        }

        var info = new FileInfo(recordPath);
        if (info.Length > MaximumRecordBytes)
        {
            throw new InvalidDataException("AzureAuth record exceeds the supported size.");
        }

        byte[] content = File.ReadAllBytes(recordPath);
        return AzureAuthSecureRecordReadResult.Present(ComputeRevision(content), content);
    }

    private AzureAuthSecureRecordWriteResult CompareExchangeUnderLock(
        string path,
        string expectedRevision,
        ReadOnlyMemory<byte> newContent
    )
    {
        AzureAuthSecureRecordReadResult current = ReadUnderLock(path);
        if (!RevisionMatches(current, expectedRevision))
        {
            return AzureAuthSecureRecordWriteResult.Conflict();
        }

        string revision = ComputeRevision(newContent.Span);
        if (
            current.Status == AzureAuthSecureRecordReadStatus.Present
            && string.Equals(current.Revision, revision, StringComparison.Ordinal)
        )
        {
            return AzureAuthSecureRecordWriteResult.Success(revision);
        }

        WriteUnderLock(path, newContent.Span);
        return AzureAuthSecureRecordWriteResult.Success(revision);
    }

    private AzureAuthSecureRecordWriteResult CompareDeleteUnderLock(
        string path,
        string expectedRevision
    )
    {
        AzureAuthSecureRecordReadResult current = ReadUnderLock(path);
        if (!RevisionMatches(current, expectedRevision))
        {
            return AzureAuthSecureRecordWriteResult.Conflict();
        }

        if (current.Status == AzureAuthSecureRecordReadStatus.Present)
        {
            File.Delete(GetRecordPath(path));
        }

        return AzureAuthSecureRecordWriteResult.Success(
            AzureAuthSecureRecordStoreContract.MissingRevision
        );
    }

    private void WriteUnderLock(string recordName, ReadOnlySpan<byte> content)
    {
        string recordPath = GetRecordPath(recordName);
        string parentPath =
            Path.GetDirectoryName(recordPath)
            ?? throw new IOException("AzureAuth record parent directory is unavailable.");
        CreateProductDirectory(parentPath);
        string temporaryPath = Path.Combine(
            parentPath,
            $".{Path.GetFileName(recordPath)}.{Guid.NewGuid():N}.tmp"
        );
        try
        {
            File.WriteAllBytes(temporaryPath, content);
            SetOwnerOnlyFileMode(temporaryPath);
            File.Move(temporaryPath, recordPath, overwrite: true);
            SetOwnerOnlyFileMode(recordPath);
        }
        finally
        {
            File.Delete(temporaryPath);
        }
    }

    private string GetRecordPath(string recordName) =>
        Path.Combine(configRootPath, recordName.Replace('/', Path.DirectorySeparatorChar));

    private static string ComputeRevision(ReadOnlySpan<byte> content) =>
        Convert.ToHexString(SHA256.HashData(content)).ToLowerInvariant();

    private static bool RevisionMatches(
        AzureAuthSecureRecordReadResult current,
        string expectedRevision
    ) =>
        current.Status switch
        {
            AzureAuthSecureRecordReadStatus.Missing => expectedRevision
                == AzureAuthSecureRecordStoreContract.MissingRevision,
            AzureAuthSecureRecordReadStatus.Present => string.Equals(
                current.Revision,
                expectedRevision,
                StringComparison.Ordinal
            ),
            _ => false,
        };

    private static void CreateProductDirectory(string path)
    {
        bool existed = Directory.Exists(path);
        Directory.CreateDirectory(path);
        if (!existed && !OperatingSystem.IsWindows())
        {
            File.SetUnixFileMode(
                path,
                UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute
            );
        }
    }

    private static void SetOwnerOnlyFileMode(string path)
    {
        if (!OperatingSystem.IsWindows())
        {
            File.SetUnixFileMode(path, UnixFileMode.UserRead | UnixFileMode.UserWrite);
        }
    }

    private static string? ResolveConfigRoot(SystemAzureAuthSecureRecordStoreOptions options)
    {
        Func<string, string?> read =
            options.EnvironmentVariableReader ?? Environment.GetEnvironmentVariable;
        string? configured =
            NullIfWhiteSpace(options.ConfigRootPath)
            ?? NullIfWhiteSpace(
                read(SystemAzureAuthSecureRecordStoreOptions.ConfigRootEnvironmentVariable)
            );
        if (configured is not null)
        {
            return TryGetAbsolutePath(configured);
        }

        if (OperatingSystem.IsWindows())
        {
            string localApplicationData = Environment.GetFolderPath(
                Environment.SpecialFolder.LocalApplicationData
            );
            return string.IsNullOrWhiteSpace(localApplicationData)
                ? null
                : Path.Combine(localApplicationData, "azureauth-credprovider");
        }

        string? xdg = NullIfWhiteSpace(read("XDG_CONFIG_HOME"));
        if (xdg is not null)
        {
            string? absoluteXdg = TryGetAbsolutePath(xdg);
            return absoluteXdg is null ? null : Path.Combine(absoluteXdg, "azureauth-credprovider");
        }

        string? home = NullIfWhiteSpace(read("HOME"));
        string? absoluteHome = home is null ? null : TryGetAbsolutePath(home);
        return absoluteHome is null
            ? null
            : Path.Combine(absoluteHome, ".config", "azureauth-credprovider");
    }

    private static string? TryGetAbsolutePath(string path) =>
        Path.IsPathFullyQualified(path)
            ? Path.TrimEndingDirectorySeparator(Path.GetFullPath(path))
            : null;

    private static string? NullIfWhiteSpace(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value;

    private sealed class RecordLockScope(object processLock, FileStream stream) : IDisposable
    {
        private FileStream? stream = stream;

        public void Dispose()
        {
            FileStream? current = Interlocked.Exchange(ref stream, null);
            if (current is null)
            {
                return;
            }

            try
            {
                if (!OperatingSystem.IsMacOS())
                {
                    current.Unlock(0, 1);
                }
                current.Dispose();
            }
            finally
            {
                Monitor.Exit(processLock);
            }
        }
    }

    private sealed class LockedRecordStore(SystemAzureAuthSecureRecordStore owner)
        : IAzureAuthSecureRecordStore
    {
        public AzureAuthSecureRecordReadResult Read(string path)
        {
            AzureAuthSecureRecordStoreContract.EnsureKnownRecordName(path);
            return owner.ReadUnderLock(path);
        }

        public AzureAuthSecureRecordWriteResult CompareExchange(
            string path,
            string expectedRevision,
            ReadOnlyMemory<byte> newContent
        )
        {
            AzureAuthSecureRecordStoreContract.EnsureKnownRecordName(path);
            ArgumentException.ThrowIfNullOrWhiteSpace(expectedRevision);
            if (newContent.Length > MaximumRecordBytes)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(newContent),
                    "AzureAuth records cannot exceed one MiB."
                );
            }

            _ = StrictUtf8.GetString(newContent.Span);
            return owner.CompareExchangeUnderLock(path, expectedRevision, newContent);
        }

        public AzureAuthSecureRecordWriteResult CompareDelete(
            string path,
            string expectedRevision
        )
        {
            AzureAuthSecureRecordStoreContract.EnsureKnownRecordName(path);
            ArgumentException.ThrowIfNullOrWhiteSpace(expectedRevision);
            return owner.CompareDeleteUnderLock(path, expectedRevision);
        }
    }
}
